using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimFront
{
    // One owned attack job. Native verbs retain warmup, spread, ammunition and damage.
    // Aiming does not edit shared VerbProperties or affect any other pawn's shots.
    internal static class NativeManualAim
    {
        const string HarmonyId = "rimfront.manualaim";
        sealed class Aim
        {
            internal Pawn Pawn;
            internal Verb Verb;
            internal Job Job;
            internal NativeAimTargeting.TargetInfo Target;
            internal float Yaw, Pitch;
            internal float LastShotRange;
            internal bool Valid;
        }
        static Aim current;
        static bool validating, initialized, supported, collisionWarned;
        static string failure;
        static readonly FieldInfo VerbTarget = AccessTools.Field(typeof(Verb), "currentTarget");
        static readonly Type CeVerb = AccessTools.TypeByName("CombatExtended.Verb_LaunchProjectileCE");
        sealed class OwnedShot { }
        static readonly ConditionalWeakTable<Thing, OwnedShot> ceShots = new ConditionalWeakTable<Thing, OwnedShot>();
        static FieldInfo ceIntended, ceReportTarget, ceReportDistance, ceEstimatedDistance, ceShotsFired;
        static MethodInfo ceCollide;
        static FieldInfo ceLastPosition, ceCandidates, ceLauncher, ceTargetSelf, ceGlobalTarget, ceCollisionCells;
        static PropertyInfo ceExactPosition, ceGlobalValid;
        [ThreadStatic] static Thing boundsTarget;
        [ThreadStatic] static Bounds targetBounds;
        internal static bool IsCE(Verb verb) => CeVerb != null && CeVerb.IsInstanceOfType(verb);

        internal static bool Prepare(Pawn pawn, Verb verb, Job job, NativeAimTargeting.TargetInfo target,
            float yaw, float pitch, out string reason)
        {
            Initialize();
            reason = failure;
            if (!supported) return false;
            current = new Aim { Pawn = pawn, Verb = verb, Job = job, Target = target, Yaw = yaw, Pitch = pitch, Valid = true };
            validating = true;
            try
            {
                if (!verb.CanHitTarget(job.targetA))
                { reason = "Native weapon cannot fire here (range, shield, or weapon restriction)."; Clear(pawn); return false; }
                return true;
            }
            finally { validating = false; }
        }
        internal static void Update(Pawn pawn, float yaw, float pitch)
        {
            Aim aim = current;
            if (aim == null || aim.Pawn != pawn) return;
            if (pawn.CurJob != aim.Job || !NativeView.Active || NativeView.ControlledPawn != pawn)
            { Clear(pawn); return; }
            aim.Yaw = yaw; aim.Pitch = pitch;
            aim.Valid = NativeAimTargeting.TrySelect(pawn, yaw, pitch, SelectionRange(aim.Verb), out NativeAimTargeting.TargetInfo target);
            if (!aim.Valid) return;
            aim.Target = target;
            // A cell job avoids native automatic pawn tracking, downed-target completion,
            // and AttackStatic's optional auto-queue of nearby identical Things.
            aim.Job.targetA = target.Point.ToIntVec3();
            VerbTarget?.SetValue(aim.Verb, aim.Job.targetA);
        }
        internal static void Clear(Pawn pawn = null)
        {
            if (pawn == null || current?.Pawn == pawn) current = null;
        }
        internal static float SelectionRange(Verb verb) => Math.Min(NativeScene.ViewDistance, Math.Max(.1f, verb.EffectiveRange - .1f));
        internal static bool Owns(object expectedPawn, object expectedVerb, object expectedJob,
            object pawn, object verb, object job, bool active, bool validation)
            => active && expectedPawn != null && expectedVerb != null && expectedJob != null
                && ReferenceEquals(expectedPawn, pawn) && ReferenceEquals(expectedVerb, verb)
                && (validation || ReferenceEquals(expectedJob, job));
        static bool Matches(Verb verb)
        {
            Aim aim = current;
            return aim != null && Owns(aim.Pawn, aim.Verb, aim.Job, NativeView.ControlledPawn, verb,
                aim.Pawn?.CurJob, NativeView.Active, validating);
        }
        internal static bool TryGet(Verb verb, out Vector3 point, out float yaw, out float pitch)
        {
            point = Vector3.zero; yaw = pitch = 0;
            if (!Matches(verb) || !current.Valid) return false;
            point = current.Target.Point; yaw = current.Yaw; pitch = current.Pitch; return true;
        }
        internal static bool TryGet(Pawn pawn, out Vector3 point, out float yaw, out float pitch)
        {
            point = Vector3.zero; yaw = pitch = 0;
            return current != null && current.Pawn == pawn && TryGet(current.Verb, out point, out yaw, out pitch);
        }
        internal static bool TryGetTarget(Verb verb, out LocalTargetInfo target)
        {
            target = LocalTargetInfo.Invalid;
            if (!Matches(verb) || !current.Valid) return false;
            target = current.Target.Target;
            return target.IsValid;
        }
        internal static bool InRange(Vector3 origin, Vector3 point, float minimum, float maximum)
        {
            if (!NativeEffectMath.IsFinite(origin) || !NativeEffectMath.IsFinite(point)
                || float.IsNaN(minimum) || float.IsNaN(maximum) || float.IsInfinity(minimum) || float.IsInfinity(maximum)
                || minimum < 0 || maximum <= 0) return false;
            double x = (double)point.x - origin.x, z = (double)point.z - origin.z;
            double distance = x * x + z * z;
            return distance + .00001 >= (double)minimum * minimum && distance <= (double)maximum * maximum + .00001;
        }
        internal static float AirAngle(float pitch) => float.IsNaN(pitch) || float.IsInfinity(pitch) ? 0 : Mathf.Clamp(-pitch, -1.55f, 1.55f);
        internal static float RebaseRangeEstimate(float estimate, float previousRange, float currentRange)
        {
            float safeRange = Finite(currentRange) && currentRange > 0 ? currentRange : 0;
            if (!Finite(estimate) || !Finite(previousRange) || !Finite(currentRange) || previousRange <= .0001f || currentRange <= .0001f)
                return safeRange;
            // Preserve the native sampled fractional distance error, without resampling
            // RNG or retaining the first target's distance after the player moves aim.
            float result = (float)((double)estimate * currentRange / previousRange);
            return Finite(result) ? result : safeRange;
        }
        internal static float HorizontalDistance(Vector3 origin, Vector3 point)
        {
            double x = (double)point.x - origin.x, z = (double)point.z - origin.z;
            return (float)Math.Sqrt(x * x + z * z);
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool AllowCast(bool owned, bool valid, bool inBounds, bool inRange)
            => !owned || (valid && inBounds && inRange);

        internal static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            var harmony = new Harmony(HarmonyId);
            try
            {
                if (VerbTarget == null) throw new NotSupportedException("native verb target field");
                MethodInfo vanillaLine = AccessTools.Method(typeof(Verb), "TryFindShootLineFromTo", new[] {
                    typeof(IntVec3), typeof(LocalTargetInfo), typeof(ShootLine).MakeByRefType(), typeof(bool) });
                if (vanillaLine == null) throw new NotSupportedException("native projectile shoot line");
                harmony.Patch(vanillaLine, prefix: Hook(nameof(VanillaLine)));
                if (CeVerb != null)
                {
                    Type report = AccessTools.TypeByName("CombatExtended.ShiftVecReport");
                    Patch(harmony, "TryFindCEShootLineFromTo", new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(ShootLine).MakeByRefType(), typeof(Vector3).MakeByRefType() }, nameof(CeLine));
                    Patch(harmony, "GetTargetHeight", new[] { typeof(LocalTargetInfo), typeof(Thing), typeof(bool), typeof(Vector3) }, nameof(CeHeight));
                    Patch(harmony, "ShotAngle", new[] { typeof(Vector3), typeof(Vector3) }, nameof(CeAngle));
                    Patch(harmony, "AdjustShotHeight", new[] { typeof(Thing), typeof(LocalTargetInfo), typeof(float).MakeByRefType() }, nameof(CeAdjustHeight));
                    Patch(harmony, "Retarget", Type.EmptyTypes, nameof(CeRetarget));
                    MethodInfo cast = AccessTools.Method(CeVerb, "TryCastShot", Type.EmptyTypes);
                    MethodInfo spawn = AccessTools.Method(CeVerb, "SpawnProjectile", Type.EmptyTypes);
                    MethodInfo shift = report == null ? null : AccessTools.Method(CeVerb, "ShiftTarget", new[] { report, typeof(Vector3), typeof(float).MakeByRefType(), typeof(bool), typeof(bool) });
                    ceReportTarget = report == null ? null : AccessTools.Field(report, "target");
                    ceReportDistance = report == null ? null : AccessTools.Field(report, "shotDist");
                    ceEstimatedDistance = AccessTools.Field(CeVerb, "estimatedTargDist");
                    ceShotsFired = AccessTools.Field(CeVerb, "numShotsFired");
                    Type shootVerb = AccessTools.TypeByName("CombatExtended.Verb_ShootCE");
                    MethodInfo shootCast = shootVerb == null ? null : AccessTools.DeclaredMethod(shootVerb, "TryCastShot", Type.EmptyTypes);
                    MethodInfo shootWarmup = shootVerb == null ? null : AccessTools.DeclaredMethod(shootVerb, "WarmupComplete", Type.EmptyTypes);
                    if (cast == null || spawn == null || shift == null || ceReportTarget?.FieldType != typeof(LocalTargetInfo)
                        || ceReportDistance?.FieldType != typeof(float) || ceEstimatedDistance?.FieldType != typeof(float)
                        || ceShotsFired?.FieldType != typeof(int) || shootCast?.ReturnType != typeof(bool)
                        || shootWarmup?.ReturnType != typeof(void))
                        throw new NotSupportedException("CE shot target contract");
                    harmony.Patch(cast, prefix: Hook(nameof(CeBeginCast)), finalizer: Hook(nameof(CeEndCast)));
                    // CE awards Shooting XP and records its battle-log target after the
                    // nested TryCastShot returns. Keep the selected Thing through that
                    // whole native warmup completion, then restore the cell-owned job.
                    harmony.Patch(shootWarmup, prefix: Hook(nameof(CeBeginCast)), finalizer: Hook(nameof(CeEndCast)));
                    harmony.Patch(cast, prefix: new HarmonyMethod(typeof(NativeManualAim), nameof(CeCastGuard)) { priority = Priority.First });
                    // ShootCE prepares/splits inventory ammunition before calling its
                    // base. Reject here too, before that native preparation can occur.
                    harmony.Patch(shootCast, prefix: new HarmonyMethod(typeof(NativeManualAim), nameof(CeCastGuard)) { priority = Priority.First });
                    harmony.Patch(spawn, postfix: Hook(nameof(CeSpawn)));
                    harmony.Patch(shift, prefix: Hook(nameof(CeShift)));
                    Type projectile = AccessTools.TypeByName("CombatExtended.ProjectileCE");
                    Type utility = AccessTools.TypeByName("CombatExtended.CE_Utility");
                    ceIntended = projectile == null ? null : AccessTools.Field(projectile, "intendedTarget");
                    ceCollide = projectile == null ? null : AccessTools.Method(projectile, "TryCollideWith", new[] { typeof(Thing) });
                    MethodInfo collision = projectile == null ? null : AccessTools.Method(projectile, "CheckCellForCollision", new[] { typeof(IntVec3) });
                    MethodInfo between = projectile == null ? null : AccessTools.Method(projectile, "CheckForCollisionBetween", Type.EmptyTypes);
                    ceCollisionCells = projectile == null ? null : AccessTools.Field(projectile, "collisionCells");
                    MethodInfo canCollide = projectile == null ? null : AccessTools.Method(projectile, "CanCollideWith", new[] { typeof(Thing), typeof(float).MakeByRefType() });
                    ceLastPosition = projectile == null ? null : AccessTools.Field(projectile, "LastPos");
                    ceExactPosition = projectile == null ? null : AccessTools.Property(projectile, "ExactPosition");
                    ceCandidates = projectile == null ? null : AccessTools.Field(projectile, "potentialCollisionCandidates");
                    ceLauncher = projectile == null ? null : AccessTools.Field(projectile, "launcher");
                    ceTargetSelf = projectile == null ? null : AccessTools.Field(projectile, "canTargetSelf");
                    ceGlobalTarget = projectile == null ? null : AccessTools.Field(projectile, "globalTargetInfo");
                    ceGlobalValid = ceGlobalTarget == null ? null : AccessTools.Property(ceGlobalTarget.FieldType, "IsValid");
                    MethodInfo expose = projectile == null ? null : AccessTools.DeclaredMethod(projectile, "ExposeData", Type.EmptyTypes);
                    MethodInfo rayCast = projectile == null ? null : AccessTools.Method(projectile, "RayCast", new[] {
                        typeof(Thing), typeof(VerbProperties), typeof(Vector2), typeof(float), typeof(float), typeof(float),
                        typeof(float), typeof(float), typeof(float), typeof(Thing), typeof(bool) });
                    MethodInfo bounds = utility == null ? null : AccessTools.Method(utility, "GetBoundsFor", new[] { typeof(Thing) });
                    if (ceIntended?.FieldType != typeof(LocalTargetInfo) || ceCollide?.ReturnType != typeof(bool)
                        || collision == null || expose == null || rayCast == null || bounds?.ReturnType != typeof(Bounds)
                        || canCollide?.ReturnType != typeof(bool) || ceLastPosition?.FieldType != typeof(Vector3)
                        || ceExactPosition?.PropertyType != typeof(Vector3) || ceCandidates?.FieldType != typeof(List<Thing>)
                        || ceLauncher?.FieldType != typeof(Thing) || ceTargetSelf?.FieldType != typeof(bool)
                        || ceGlobalValid?.PropertyType != typeof(bool) || between?.ReturnType != typeof(bool)
                        || ceCollisionCells?.FieldType != typeof(List<IntVec3>))
                        throw new NotSupportedException("CE explicit item collision contract");
                    harmony.Patch(collision, transpiler: Hook(nameof(CeRenderedCandidates)));
                    harmony.Patch(between, transpiler: Hook(nameof(CeRenderedCells)));
                    harmony.Patch(canCollide, prefix: Hook(nameof(CeRenderedContact)));
                    harmony.Patch(bounds, postfix: Hook(nameof(CeBounds)));
                    harmony.Patch(expose, postfix: Hook(nameof(CeSave)));
                    harmony.Patch(rayCast, prefix: Hook(nameof(CeRayBegin)), finalizer: Hook(nameof(CeRayEnd)));
                    MethodInfo locked = AccessTools.PropertyGetter(CeVerb, "LockRotationAndAngle");
                    if (locked == null || report == null) throw new NotSupportedException("CE manual burst aim contract");
                    harmony.Patch(locked, prefix: Hook(nameof(CeRetarget)));
                }
                supported = true;
            }
            catch (Exception error)
            {
                harmony.UnpatchAll(HarmonyId);
                failure = "Manual aim unavailable: " + error.GetBaseException().Message;
                Log.Warning("[RimFront] " + failure);
            }
        }
        static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(NativeManualAim), name);
        static void Patch(Harmony harmony, string name, Type[] parameters, string hook)
        {
            MethodInfo method = AccessTools.Method(CeVerb, name, parameters);
            if (method == null) throw new NotSupportedException("CE " + name);
            harmony.Patch(method, prefix: Hook(hook));
        }
        static bool CeLine(Verb __instance, IntVec3 __0, LocalTargetInfo __1, ref ShootLine __2, ref Vector3 __3, ref bool __result)
        {
            if (!Matches(__instance)) return true;
            Aim aim = current;
            __3 = aim.Target.Point;
            __2 = new ShootLine(__0, aim.Target.Point.ToIntVec3());
            __result = ValidOwnedGeometry(__instance);
            // Actual projectile collision now decides what blocks the shot. Native
            // CanHitTargetFrom still applies violence, apparel/shield and roof rules.
            return false;
        }
        static bool ValidOwnedGeometry(Verb verb)
        {
            Aim aim = current;
            if (aim == null || aim.Pawn == null || !aim.Pawn.Spawned) return false;
            Vector3 origin = AnalogMovement.TryGetPosition(aim.Pawn, out Vector3 continuous) ? continuous : aim.Pawn.DrawPos;
            return AllowCast(true, aim.Valid, aim.Target.Point.ToIntVec3().InBounds(aim.Pawn.Map),
                InRange(origin, aim.Target.Point, verb.verbProps.minRange, verb.EffectiveRange));
        }
        static bool CeCastGuard(Verb __instance, ref bool __result)
        {
            if (CanCast(__instance)) return true;
            // CE suppressive mode can intentionally reuse its previous line after a
            // failed line query. An invalid manual ray must never fire at that old aim.
            __result = false; return false;
        }
        internal static bool CanCast(Verb verb) => !Matches(verb) || ValidOwnedGeometry(verb);
        static bool VanillaLine(Verb __instance, IntVec3 __0, ref ShootLine __2, ref bool __result)
        {
            if (!Matches(__instance) || IsCE(__instance) || !NativeVanillaAim.Supports(__instance)) return true;
            Vector3 point = Vector3.zero;
            bool runNative = CeLine(__instance, __0, default(LocalTargetInfo), ref __2, ref point, ref __result);
            // Every vanilla accuracy branch must share the intended Thing/cell anchor;
            // the owned launch adapter then applies the exact-point offset exactly once.
            __2 = new ShootLine(__0, current.Target.Target.Cell);
            return runNative;
        }
        static bool CeHeight(Verb __instance, ref float __result)
        {
            if (!Matches(__instance)) return true;
            __result = current.Target.Point.y; return false;
        }
        static bool CeAngle(Verb __instance, ref float __result)
        {
            if (!Matches(__instance) || current.Target.HitThing || current.Target.HitSurface) return true;
            // Empty air has no impact point for automatic gravity compensation.
            // CE adds its normal sway, recoil and pellet spread after this base angle.
            __result = AirAngle(current.Pitch); return false;
        }
        static bool CeAdjustHeight(Verb __instance, ref float __result)
        {
            if (!Matches(__instance)) return true;
            __result = 0; return false;
        }
        static bool CeRetarget(Verb __instance, ref bool __result)
        {
            if (!Matches(__instance)) return true;
            __result = false; return false;
        }
        sealed class CastState { internal LocalTargetInfo Target; internal Aim Context; }
        static void CeBeginCast(Verb __instance, out CastState __state)
        {
            __state = null;
            if (!TryGetTarget(__instance, out LocalTargetInfo target)) return;
            __state = PushTarget(__instance, target);
        }
        static CastState PushTarget(Verb verb, LocalTargetInfo target)
        {
            var state = new CastState { Target = (LocalTargetInfo)VerbTarget.GetValue(verb), Context = current };
            VerbTarget.SetValue(verb, target);
            return state;
        }
        static Exception CeEndCast(Verb __instance, CastState __state, Exception __exception)
        {
            // A native reload/job replacement can reset the verb inside this scope.
            // Never resurrect its previous target after that ownership was cleared.
            if (__state != null && ReferenceEquals(current, __state.Context)) VerbTarget.SetValue(__instance, __state.Target);
            return __exception;
        }
        static void CeShift(Verb __instance, object __0, ref Vector3 __1, bool __3)
        {
            if (!TryGet(__instance, out Vector3 point, out float yaw, out float pitch)) return;
            __1 = point;
            // Keep the native accuracy report but remove automatic moving-pawn lead:
            // the player's current crosshair, rather than an AI prediction, aims this shot.
            ceReportTarget.SetValue(__0, new LocalTargetInfo(point.ToIntVec3()));
            if (!__3)
            {
                Vector3 source = GenThing.TrueCenter(__instance.Caster);
                float range = HorizontalDistance(source, point);
                ceReportDistance.SetValue(__0, range);
                if ((int)ceShotsFired.GetValue(__instance) > 0 && current.LastShotRange > .0001f)
                    ceEstimatedDistance.SetValue(__instance, RebaseRangeEstimate((float)ceEstimatedDistance.GetValue(__instance), current.LastShotRange, range));
                current.LastShotRange = range;
            }
        }
        static void CeSpawn(Verb __instance, Thing __result)
        {
            if (__result != null && Matches(__instance)) ceShots.GetValue(__result, _ => new OwnedShot());
        }
        internal const int MaximumRenderedCandidates = 128;
        struct RenderContact
        {
            internal Thing Thing;
            internal float Distance;
        }
        static bool Rendered(Thing thing) => thing is Pawn || thing is Corpse || thing is Building
            || thing is Plant || thing?.def?.category == ThingCategory.Item;
        internal static bool UseRenderedContact(bool owned, bool supportedThing, bool spawned, bool sameMap)
            => owned && supportedThing && spawned && sameMap;
        static bool OwnedProjectile(Thing projectile) => projectile != null && !projectile.Destroyed
            && ceShots.TryGetValue(projectile, out _);
        internal static bool SegmentDirection(Vector3 from, Vector3 to, out float yaw, out float pitch,
            out float horizontalLength, out float length)
        {
            yaw = pitch = horizontalLength = length = 0;
            if (!NativeEffectMath.IsFinite(from) || !NativeEffectMath.IsFinite(to)) return false;
            Vector3 delta = to - from;
            horizontalLength = HorizontalDistance(from, to); length = delta.magnitude;
            if (horizontalLength < .00001f || !Finite(length) || length < .00001f) return false;
            yaw = Mathf.Atan2(delta.x, delta.z); pitch = -Mathf.Atan2(delta.y, horizontalLength);
            return Math.Abs(pitch) < Math.PI / 2 - .001;
        }
        internal static bool SegmentDistance(double horizontalHit, float horizontalLength, float length, out float distance)
        {
            distance = 0;
            if (double.IsNaN(horizontalHit) || double.IsInfinity(horizontalHit) || horizontalHit < 0
                || !Finite(horizontalLength) || horizontalLength <= 0 || !Finite(length) || length <= 0
                || horizontalHit > horizontalLength + .0001) return false;
            distance = (float)(horizontalHit / horizontalLength * length);
            return Finite(distance);
        }
        static bool Contact(Thing projectile, Thing thing, out float distance)
        {
            distance = 0;
            Vector3 from = (Vector3)ceLastPosition.GetValue(projectile);
            Vector3 to = (Vector3)ceExactPosition.GetValue(projectile, null);
            return SegmentDirection(from, to, out float yaw, out float pitch, out float horizontal, out float length)
                && NativeAimTargeting.TryHitThing(thing, from, yaw, pitch, out double hit)
                && SegmentDistance(hit, horizontal, length, out distance);
        }
        static bool CeRenderedContact(Thing __instance, Thing __0, ref float __1, ref bool __result)
        {
            if (!UseRenderedContact(OwnedProjectile(__instance), Rendered(__0), __0?.Spawned ?? false,
                __0 != null && __0.Map == __instance.Map)) return true;
            // Native CheckCellForCollision retains launcher/mount/minimum-intercept
            // filtering and blocker callbacks. Only replace the geometric narrow phase.
            try
            {
                if ((bool)ceGlobalValid.GetValue(ceGlobalTarget.GetValue(__instance), null)
                    || (ReferenceEquals(ceLauncher.GetValue(__instance), __0) && !(bool)ceTargetSelf.GetValue(__instance))) return true;
                if (!SegmentDirection((Vector3)ceLastPosition.GetValue(__instance), (Vector3)ceExactPosition.GetValue(__instance, null),
                    out _, out _, out _, out _)) return true;
                __result = Contact(__instance, __0, out __1); return false;
            }
            catch (Exception error) { CollisionWarning(error); return true; }
        }
        static float RenderedCover(float nativeHeight, Thing projectile)
            => OwnedProjectile(projectile) ? float.PositiveInfinity : nativeHeight;
        internal static List<IntVec3> SegmentCells(Vector3 from, Vector3 to)
        {
            if (!NativeEffectMath.IsFinite(from) || !NativeEffectMath.IsFinite(to)
                || Math.Abs(from.x) > 1000000 || Math.Abs(from.z) > 1000000
                || Math.Abs(to.x) > 1000000 || Math.Abs(to.z) > 1000000) return null;
            int x = (int)Math.Floor(from.x), z = (int)Math.Floor(from.z);
            int endX = (int)Math.Floor(to.x), endZ = (int)Math.Floor(to.z);
            int count = Math.Abs(endX - x) + Math.Abs(endZ - z) + 1;
            if (count > 2048) return null;
            var cells = new List<IntVec3>(count);
            double dx = (double)to.x - from.x, dz = (double)to.z - from.z;
            int stepX = Math.Sign(dx), stepZ = Math.Sign(dz);
            double deltaX = stepX == 0 ? double.PositiveInfinity : Math.Abs(1 / dx);
            double deltaZ = stepZ == 0 ? double.PositiveInfinity : Math.Abs(1 / dz);
            double nextX = stepX == 0 ? double.PositiveInfinity : ((stepX > 0 ? x + 1 : x) - from.x) / dx;
            double nextZ = stepZ == 0 ? double.PositiveInfinity : ((stepZ > 0 ? z + 1 : z) - from.z) / dz;
            for (int i = 0; i < count; i++)
            {
                cells.Add(new IntVec3(x, 0, z));
                if (x == endX && z == endZ) return cells;
                if (nextX < nextZ) { x += stepX; nextX += deltaX; }
                else if (nextZ < nextX) { z += stepZ; nextZ += deltaZ; }
                else { x += stepX; z += stepZ; nextX += deltaX; nextZ += deltaZ; }
            }
            return null;
        }
        static void SetRenderedCells(Thing projectile)
        {
            if (!OwnedProjectile(projectile)) return;
            List<IntVec3> cells = SegmentCells((Vector3)ceLastPosition.GetValue(projectile),
                (Vector3)ceExactPosition.GetValue(projectile, null));
            if (cells == null) return;
            var native = (List<IntVec3>)ceCollisionCells.GetValue(null);
            native.Clear(); native.AddRange(cells);
        }
        static IEnumerable<CodeInstruction> CeRenderedCells(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(); int matches = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                result.Add(instruction);
                if (instruction.operand is MethodInfo method && method.Name == "Sort" && method.DeclaringType == typeof(List<IntVec3>))
                {
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativeManualAim), nameof(SetRenderedCells))));
                    matches++;
                }
            }
            if (matches != 1) throw new NotSupportedException("CE floating segment cell IL contract");
            return result;
        }
        static float RenderedMinimum(float nativeDistance, Thing projectile, Thing candidate)
            => OwnedProjectile(projectile) && candidate is Building ? 0 : nativeDistance;
        internal static bool ContactInCell(Vector3 from, Vector3 to, float distance, IntVec3 cell)
        {
            Vector3 delta = to - from;
            if (!NativeEffectMath.IsFinite(from) || !NativeEffectMath.IsFinite(to) || !Finite(distance)
                || delta.sqrMagnitude < .00000001f || distance < 0) return false;
            Vector3 point = from + delta.normalized * distance;
            const float epsilon = .0001f;
            return point.x >= cell.x - epsilon && point.x <= cell.x + 1 + epsilon
                && point.z >= cell.z - epsilon && point.z <= cell.z + 1 + epsilon;
        }
        static void SortRenderedCandidates(Thing projectile, IntVec3 cell)
        {
            if (!OwnedProjectile(projectile) || projectile.Map == null) return;
            var candidates = (List<Thing>)ceCandidates.GetValue(null);
            var contacts = new List<RenderContact>();
            var seen = new HashSet<Thing>();
            try
            {
                if (!SegmentDirection((Vector3)ceLastPosition.GetValue(projectile), (Vector3)ceExactPosition.GetValue(projectile, null),
                    out _, out _, out _, out _)) return;
                // Preserve native candidates, then include visible meshes omitted by
                // CE's fillPercent broad phase, including nearby animated silhouettes.
                void Add(Thing thing)
                {
                    if (thing == null || !seen.Add(thing) || !thing.Spawned || thing.Map != projectile.Map) return;
                    if (Rendered(thing))
                    {
                        if (Contact(projectile, thing, out float distance)
                            && ContactInCell((Vector3)ceLastPosition.GetValue(projectile),
                                (Vector3)ceExactPosition.GetValue(projectile, null), distance, cell))
                            contacts.Add(new RenderContact { Thing = thing, Distance = distance });
                    }
                    else contacts.Add(new RenderContact { Thing = thing,
                        Distance = (thing.DrawPos - (Vector3)ceLastPosition.GetValue(projectile)).magnitude });
                }
                foreach (Thing thing in candidates) Add(thing);
                Thing intended = ((LocalTargetInfo)ceIntended.GetValue(projectile)).Thing;
                if (intended != null) Add(intended);
                int extra = 0;
                // Current cell first, then successive rings. Distant clutter must not
                // consume the extra budget before the actual crossed cell or target.
                for (int radius = 0; radius <= 4 && extra < MaximumRenderedCandidates; radius++)
                    for (int z = -radius; z <= radius && extra < MaximumRenderedCandidates; z++)
                    for (int x = -radius; x <= radius && extra < MaximumRenderedCandidates; x++)
                    {
                        if (Math.Max(Math.Abs(x), Math.Abs(z)) != radius) continue;
                        IntVec3 nearby = cell + new IntVec3(x, 0, z);
                        if (!nearby.InBounds(projectile.Map)) continue;
                        foreach (Thing thing in projectile.Map.thingGrid.ThingsListAtFast(nearby))
                        {
                            if (extra >= MaximumRenderedCandidates) break;
                            if (thing != projectile && Rendered(thing) && !seen.Contains(thing)) { extra++; Add(thing); }
                        }
                    }
                contacts.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                candidates.Clear();
                foreach (RenderContact contact in contacts) candidates.Add(contact.Thing);
            }
            catch (Exception error) { CollisionWarning(error); }
        }
        static IEnumerable<CodeInstruction> CeRenderedCandidates(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(); int heights = 0, sorts = 0, minimums = 0;
            CodeInstruction candidateLoad = null;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.operand is MethodInfo getter && getter.Name == "get_DrawPos"
                    && result.Count > 0) candidateLoad = new CodeInstruction(result[result.Count - 1].opcode, result[result.Count - 1].operand);
                result.Add(instruction);
                if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field && field.Name == "minCollisionDistance")
                {
                    if (candidateLoad == null || !candidateLoad.IsLdloc()) throw new NotSupportedException("CE collision candidate local contract");
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0)); result.Add(candidateLoad);
                    result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativeManualAim), nameof(RenderedMinimum))));
                    minimums++;
                }
                if (!(instruction.operand is MethodInfo call)) continue;
                if (call.Name == "HighestCoverAt" && call.ReturnType == typeof(float))
                {
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativeManualAim), nameof(RenderedCover))));
                    heights++;
                }
                if (call.Name == "SortBy" && call.DeclaringType == typeof(GenCollection))
                {
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0)); result.Add(new CodeInstruction(OpCodes.Ldarg_1));
                    result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativeManualAim), nameof(SortRenderedCandidates))));
                    sorts++;
                }
            }
            if (heights != 1 || sorts != 1 || minimums != 1) throw new NotSupportedException("CE rendered collision IL contract");
            return result;
        }
        internal static bool NeedsExplicitCandidate(bool owned, bool hitAlready, bool spawned, bool sameMap, bool zeroFill, bool isPawn)
            => owned && !hitAlready && spawned && sameMap && zeroFill && !isPawn;
        static void CeCollision(Thing __instance, IntVec3 __0, ref bool __result)
        {
            Thing previous = boundsTarget; Bounds oldBounds = targetBounds;
            try
            {
                if (!ExplicitItem(__instance, out Thing target) || __result || !target.OccupiedRect().Contains(__0)
                    || !NativeAimTargeting.TryBounds(target, out Bounds bounds)) return;
                boundsTarget = target; targetBounds = bounds;
                // This calls CE's own segment intersection, blocker callbacks and Impact.
                // It only supplies an explicit zero-fill target omitted by its broad phase.
                __result = (bool)ceCollide.Invoke(__instance, new object[] { target });
            }
            catch (Exception error) { CollisionWarning(error); }
            finally { boundsTarget = previous; targetBounds = oldBounds; }
        }
        static bool ExplicitItem(Thing projectile, out Thing target)
        {
            target = null;
            if (projectile.Destroyed || !ceShots.TryGetValue(projectile, out OwnedShot owned)) return false;
            target = ((LocalTargetInfo)ceIntended.GetValue(projectile)).Thing;
            return target != null && NeedsExplicitCandidate(true, false, target.Spawned, target.Map == projectile.Map,
                target.def.Fillage == FillCategory.None, target is Pawn);
        }
        sealed class BoundsState { internal Thing Target; internal Bounds Bounds; }
        static void CeRayBegin(Thing __instance, out BoundsState __state)
        {
            __state = null;
            try
            {
                if (!ExplicitItem(__instance, out Thing target) || !NativeAimTargeting.TryBounds(target, out Bounds bounds)) return;
                __state = new BoundsState { Target = boundsTarget, Bounds = targetBounds };
                boundsTarget = target; targetBounds = bounds;
            }
            catch (Exception error) { CollisionWarning(error); }
        }
        static Exception CeRayEnd(BoundsState __state, Exception __exception)
        {
            if (__state != null) { boundsTarget = __state.Target; targetBounds = __state.Bounds; }
            return __exception;
        }
        static void CollisionWarning(Exception error)
        {
            if (collisionWarned) return;
            collisionWarned = true;
            Log.Warning("[RimFront] Explicit item collision retained native fallback: " + error.GetBaseException().Message);
        }
        static void CeBounds(Thing __0, ref Bounds __result)
        {
            if (__0 != null && __0 == boundsTarget) __result = targetBounds;
        }
        static void CeSave(Thing __instance)
        {
            bool owned = ceShots.TryGetValue(__instance, out OwnedShot shot);
            if (Scribe.mode == LoadSaveMode.Saving && !owned) return;
            Scribe_Values.Look(ref owned, "rimFrontManualCE", false);
            if (owned && Scribe.mode == LoadSaveMode.LoadingVars) ceShots.GetValue(__instance, _ => new OwnedShot());
        }
    }
}
