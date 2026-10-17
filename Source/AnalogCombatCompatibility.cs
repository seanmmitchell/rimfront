using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFront
{
    // Keeps the existing combat pipeline, including its narrow-phase collisions and armor.
    // No CE reference: validate the optional integration before allowing analog possession.
    internal static class AnalogCombatCompatibility
    {
        const string HarmonyId = "rimfront.analogcombat";
        static bool initialized, supported;
        static string failureReason;
        static Func<Thing, Bounds> boundsFor;
        static MethodInfo thingsAt, highestCover, checkCell, rayCast;
        static PropertyInfo reportPawn;
        [ThreadStatic] static CollisionScope scope;

        struct CollisionScope
        {
            internal Thing Projectile;
            internal Pawn Pawn;
            internal Bounds Bounds;
        }

        internal static bool Supported { get { Initialize(); return supported; } }
        internal static string FailureReason { get { Initialize(); return failureReason; } }

        internal static float GetCollisionRadius(Pawn pawn)
        {
            if (!Supported) throw new NotSupportedException(failureReason);
            if (boundsFor == null) return 0f;
            Vector3 extents = boundsFor(pawn).extents;
            float radius = Mathf.Max(extents.x, extents.z);
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f)
                throw new NotSupportedException("CE returned invalid pawn collision dimensions.");
            return radius;
        }

        static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            var harmony = new Harmony(HarmonyId);
            try
            {
                MethodInfo center = AccessTools.Method(typeof(GenThing), "TrueCenter", new[] { typeof(Thing) });
                Require(center != null && center.ReturnType == typeof(Vector3), "native Thing.TrueCenter");
                Type projectile = AccessTools.TypeByName("CombatExtended.ProjectileCE");
                bool hasCe = projectile != null || AccessTools.TypeByName("CombatExtended.CompAmmoUser") != null;
                MethodInfo bounds = null, lineOfSight = null, predict = null, speed = null;
                if (hasCe)
                {
                    Type utility = AccessTools.TypeByName("CombatExtended.CE_Utility");
                    Type verb = AccessTools.TypeByName("CombatExtended.Verb_LaunchProjectileCE");
                    Type lighting = AccessTools.TypeByName("CombatExtended.LightingTracker");
                    Require(projectile != null && typeof(Thing).IsAssignableFrom(projectile)
                        && utility != null && verb != null && typeof(Verb).IsAssignableFrom(verb)
                        && lighting != null, "CE projectile, verb, and lighting types");
                    bounds = AccessTools.Method(utility, "GetBoundsFor", new[] { typeof(Thing) });
                    Require(bounds != null && bounds.IsStatic && bounds.ReturnType == typeof(Bounds), "CE collision bounds");
                    lineOfSight = AccessTools.DeclaredMethod(verb, "CanHitFromCellIgnoringRange",
                        new[] { typeof(Vector3), typeof(LocalTargetInfo), typeof(IntVec3).MakeByRefType() });
                    Require(lineOfSight != null && lineOfSight.ReturnType == typeof(bool), "CE continuous line of sight");
                    checkCell = AccessTools.DeclaredMethod(projectile, "CheckCellForCollision", new[] { typeof(IntVec3) });
                    rayCast = AccessTools.DeclaredMethod(projectile, "RayCast", new[] {
                        typeof(Thing), typeof(VerbProperties), typeof(Vector2), typeof(float), typeof(float),
                        typeof(float), typeof(float), typeof(float), typeof(float), typeof(Thing), typeof(bool) });
                    highestCover = AccessTools.Method(lighting, "HighestCoverAt", new[] { typeof(IntVec3) });
                    thingsAt = AccessTools.Method(typeof(ThingGrid), "ThingsListAtFast", new[] { typeof(IntVec3) });
                    Require(checkCell != null && checkCell.ReturnType == typeof(bool)
                        && rayCast != null && rayCast.ReturnType == typeof(void)
                        && highestCover != null && highestCover.ReturnType == typeof(float)
                        && thingsAt != null && thingsAt.ReturnType == typeof(List<Thing>), "CE collision candidate queries");

                    // SourceLoc must still be calculated from the authoritative Thing center.
                    Type report = AccessTools.TypeByName("CombatExtended.ShiftVecReport");
                    Require(report != null, "CE aim report");
                    reportPawn = AccessTools.Property(report, "targetPawn");
                    predict = AccessTools.DeclaredMethod(report, "PredictPawnPos", new[] { typeof(float) });
                    speed = AccessTools.Method(utility, "GetMoveSpeed", new[] { typeof(Pawn) });
                    Require(reportPawn?.PropertyType == typeof(Pawn) && reportPawn.GetGetMethod(true) != null
                        && predict != null && predict.ReturnType == typeof(Vector3)
                        && speed != null && speed.IsStatic && speed.ReturnType == typeof(float), "CE moving-target prediction");
                    MethodInfo shift = AccessTools.DeclaredMethod(verb, "ShiftTarget", new[] {
                        report, typeof(Vector3), typeof(float).MakeByRefType(), typeof(bool), typeof(bool) });
                    Require(shift != null && CountCalls(shift, center) > 0, "CE shot origin pipeline");
                    Require(CountCalls(checkCell, highestCover) == 1 && CountCalls(checkCell, thingsAt) > 0
                        && CountCalls(rayCast, thingsAt) > 0, "CE collision IL contract");
                    boundsFor = (Func<Thing, Bounds>)Delegate.CreateDelegate(typeof(Func<Thing, Bounds>), bounds);
                }

                harmony.Patch(center, postfix: Hook(nameof(CenterPostfix)));
                if (hasCe)
                {
                    harmony.Patch(bounds, postfix: Hook(nameof(BoundsPostfix)));
                    harmony.Patch(lineOfSight, prefix: Hook(nameof(LineOfSightPrefix)));
                    harmony.Patch(predict, prefix: Hook(nameof(PredictionPrefix)));
                    harmony.Patch(speed, prefix: Hook(nameof(MoveSpeedPrefix)));
                    harmony.Patch(checkCell, prefix: Hook(nameof(BeginCollision)),
                        transpiler: Hook(nameof(CollisionTranspiler)), finalizer: Hook(nameof(EndCollision)));
                    harmony.Patch(rayCast, prefix: Hook(nameof(BeginCollision)),
                        transpiler: Hook(nameof(CollisionTranspiler)), finalizer: Hook(nameof(EndCollision)));
                }
                supported = true;
                failureReason = null;
                Log.Message("[RimFront] Analog combat coordinates ready" + (hasCe ? " (Combat Extended)." : " (vanilla)."));
            }
            catch (Exception error)
            {
                // Roll back this adapter only; ordinary CE and other RimFront hooks remain intact.
                harmony.UnpatchAll(HarmonyId);
                supported = false;
                failureReason = "Analog control unavailable: " + error.Message;
                Log.Warning("[RimFront] " + failureReason);
            }
        }

        static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(AnalogCombatCompatibility), name);
        static void Require(bool condition, string contract)
        {
            if (!condition) throw new NotSupportedException("Unsupported " + contract + ".");
        }

        static int CountCalls(MethodInfo method, MethodInfo target) =>
            PatchProcessor.GetOriginalInstructions(method).Count(instruction => instruction.Calls(target));

        static void CenterPostfix(Thing __0, ref Vector3 __result)
        {
            if (!AnalogMovement.TryGetPosition(__0, out Vector3 position)) return;
            __result = Recenter(__result, position);
        }

        static void BoundsPostfix(Thing __0, ref Bounds __result)
        {
            if (!AnalogMovement.TryGetPosition(__0, out Vector3 position)) return;
            __result.center = Recenter(__result.center, position);
        }

        static void LineOfSightPrefix(Verb __instance, ref Vector3 __0)
        {
            if (!AnalogMovement.TryGetPosition(__instance.Caster, out Vector3 position)) return;
            // CE also retries neighboring lean cells here. The possessed pawn is physically at
            // position, so every test must use that source, never an unoccupied lean location.
            __0 = Recenter(__0, position);
        }

        internal static Vector3 Recenter(Vector3 original, Vector3 authoritative) =>
            new Vector3(authoritative.x, original.y, authoritative.z);

        static bool PredictionPrefix(object __instance, float __0, ref Vector3 __result)
        {
            Pawn pawn = reportPawn.GetValue(__instance, null) as Pawn;
            if (!AnalogMovement.TryGetPosition(pawn, out Vector3 position)) return true;
            // Preserve CE's flight-time refinement and random lead error; replace only its
            // path-cell extrapolation with the possessed pawn's actual tick velocity.
            __result = PredictPosition(position, AnalogMovement.Velocity, __0);
            return false;
        }

        static bool MoveSpeedPrefix(Pawn __0, ref float __result)
        {
            if (!AnalogMovement.TryGetPosition(__0, out _)) return true;
            __result = AnalogMovement.Velocity.magnitude;
            return false;
        }

        internal static Vector3 PredictPosition(Vector3 position, Vector3 velocity, float seconds)
        {
            float time = float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f ? 0f : seconds;
            return new Vector3(position.x + velocity.x * time, 0f, position.z + velocity.z * time);
        }

        // Pure footprint predicate used for both projectile and instant-weapon candidate queries.
        // The narrow phase still tests CE's original 3D bounds against its actual shot ray.
        internal static bool OverlapsCell(Bounds bounds, IntVec3 cell)
        {
            Vector3 min = bounds.min, max = bounds.max;
            return min.x <= cell.x + 1f && max.x >= cell.x
                && min.z <= cell.z + 1f && max.z >= cell.z;
        }

        static void BeginCollision(Thing __instance, out CollisionScope __state)
        {
            __state = scope;
            scope = new CollisionScope { Projectile = __instance };
            Pawn pawn = NativeView.ControlledPawn;
            if (boundsFor == null || __instance?.Map == null || pawn == null || !pawn.Spawned
                || pawn.Map != __instance.Map || !AnalogMovement.TryGetPosition(pawn, out _)) return;
            // Snapshot once for this native collision query, never once per sampled ray cell.
            // Use actual CE dimensions rather than assuming a fixed humanlike body size.
            scope.Bounds = boundsFor(pawn);
            scope.Pawn = pawn;
        }

        static Exception EndCollision(Exception __exception, CollisionScope __state)
        {
            scope = __state;
            return __exception;
        }

        static bool ControlledBounds(Thing projectile, IntVec3 cell, out Pawn pawn, out Bounds bounds)
        {
            pawn = scope.Pawn;
            bounds = scope.Bounds;
            return scope.Projectile == projectile && pawn != null && OverlapsCell(bounds, cell);
        }

        static List<Thing> CandidatesAt(ThingGrid grid, IntVec3 cell, Thing projectile)
        {
            List<Thing> original = grid.ThingsListAtFast(cell);
            if (!ControlledBounds(projectile, cell, out Pawn pawn, out _) || original.Contains(pawn)) return original;
            // Never alter the game's registered grid or share a scratch list with nested impacts.
            var candidates = new List<Thing>(original.Count + 1);
            candidates.AddRange(original);
            candidates.Add(pawn);
            return candidates;
        }

        static float CoverWithControlledBounds(float original, Thing projectile, IntVec3 cell)
        {
            return ControlledBounds(projectile, cell, out _, out Bounds bounds)
                ? Mathf.Max(original, bounds.max.y) : original;
        }

        static IEnumerable<CodeInstruction> CollisionTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            MethodInfo candidates = AccessTools.Method(typeof(AnalogCombatCompatibility), nameof(CandidatesAt));
            MethodInfo cover = AccessTools.Method(typeof(AnalogCombatCompatibility), nameof(CoverWithControlledBounds));
            bool isCell = __originalMethod == checkCell;
            int queryCount = 0, coverCount = 0;
            var result = new List<CodeInstruction>();
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(thingsAt))
                {
                    // Stack already contains grid and cell. Add this projectile as context.
                    var context = new CodeInstruction(OpCodes.Ldarg_0);
                    context.labels.AddRange(instruction.labels);
                    context.blocks.AddRange(instruction.blocks);
                    instruction.labels.Clear();
                    instruction.blocks.Clear();
                    result.Add(context);
                    result.Add(new CodeInstruction(OpCodes.Call, candidates));
                    queryCount++;
                    continue;
                }
                result.Add(instruction);
                if (isCell && instruction.Calls(highestCover))
                {
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Ldarg_1));
                    result.Add(new CodeInstruction(OpCodes.Call, cover));
                    coverCount++;
                }
            }
            Require(queryCount > 0 && (!isCell || coverCount == 1), "patched CE collision IL contract");
            return result;
        }
    }
}
