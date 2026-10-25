using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFront
{
    // Vanilla straight bullets have only an XZ trajectory. Keep their native spread,
    // hit flags and chance rolls, but reject contacts outside the owned 3D ray.
    // Explosives, arcs and custom projectile classes remain entirely native.
    internal static class NativeVanillaAim
    {
        sealed class Shot
        {
            internal Vector3 Origin, Destination;
            internal float Yaw, Pitch, Length;
            internal Vector3 SegmentStart;
            internal float SegmentLength;
            internal bool HasSegment;
            internal int FinalImpactDepth;
            internal bool PendingWorld, CheckingEligibility, HasContact;
            internal float WorldLimit;
            internal Vector3 Contact;
        }
        static readonly ConditionalWeakTable<Projectile, Shot> shots = new ConditionalWeakTable<Projectile, Shot>();
        static Verb launching;
        static bool warned;
        static readonly MethodInfo StartingTicks = AccessTools.PropertyGetter(typeof(Projectile), "StartingTicksToImpact");
        static readonly FieldInfo VerbTarget = AccessTools.Field(typeof(Verb), "currentTarget");
        static readonly MethodInfo NativeCanHit = AccessTools.Method(typeof(Projectile), "CanHit", new[] { typeof(Thing) });
        static readonly MethodInfo NativeImpact = AccessTools.Method(typeof(Bullet), "Impact", new[] { typeof(Thing), typeof(bool) });
        internal const int MaximumSegmentCells = 1024, MaximumSegmentThings = 768;
        static readonly HashSet<IntVec3> segmentCells = new HashSet<IntVec3>();
        static readonly HashSet<int> segmentThings = new HashSet<int>();
        static bool queryingWorld;

        internal static bool Supports(ThingDef definition) => definition?.thingClass == typeof(Bullet)
            && definition.projectile != null && !definition.projectile.flyOverhead
            && definition.projectile.arcHeightFactor == 0 && definition.projectile.explosionRadius <= 0;
        internal static bool Supports(Verb verb) => verb is Verb_LaunchProjectile launch && !NativeManualAim.IsCE(verb)
            && StartingTicks != null && VerbTarget != null && NativeCanHit != null && NativeImpact != null && Supports(launch.Projectile);
        internal static bool Tracks(Projectile projectile) => projectile != null && shots.TryGetValue(projectile, out Shot unused);

        internal static Vector3 AdjustDestination(Vector3 nativeDestination, Vector3 intendedCellCenter, Vector3 point)
        {
            // The native miss cell/random offset remains intact; only the cell-center
            // quantisation is replaced by the exact point under the crosshair.
            Vector3 result = nativeDestination + new Vector3(point.x - intendedCellCenter.x, 0, point.z - intendedCellCenter.z);
            result.y = 0;
            return result;
        }
        internal static Vector3 ShotDestination(Vector3 nativeDestination, Vector3 intendedCellCenter, Vector3 point, bool directThingHit)
        {
            // A successful native Thing hit ignores Launch's small cosmetic offset.
            // Keeping that offset here would turn a successful accuracy roll into a
            // miss whenever it stops short of the selected front face or tiny item.
            if (directThingHit) return new Vector3(point.x, 0, point.z);
            return AdjustDestination(nativeDestination, intendedCellCenter, point);
        }
        internal static ProjectileHitFlags CollisionFlags(ProjectileHitFlags nativeFlags)
            // Manual aim bypasses the 2D shoot-line blocker check. World geometry
            // must therefore remain eligible to intercept even an intended pawn hit.
            // Incidental pawn/friendly-fire choices remain exactly as native supplied.
            => nativeFlags | ProjectileHitFlags.NonTargetWorld;
        internal static float Height(Vector3 origin, Vector3 destination, Vector3 position)
        {
            Vector3 path = destination - origin; path.y = 0;
            Vector3 traveled = position - origin; traveled.y = 0;
            float fraction = path.sqrMagnitude > .000001f ? Mathf.Clamp01(Vector3.Dot(traveled, path) / path.sqrMagnitude) : 1;
            return Mathf.Lerp(origin.y, destination.y, fraction);
        }
        internal static bool FinalSegment(Vector3 origin, Vector3 destination, Vector3 previous, Vector3 current,
            out Vector3 start, out float length)
        {
            start = Vector3.zero; length = 0;
            if (!Valid(origin, destination) || !NativeEffectMath.IsFinite(previous) || !NativeEffectMath.IsFinite(current)) return false;
            Vector3 path = destination - origin; path.y = 0;
            float total = path.magnitude;
            if (total < .001f) return false;
            Vector3 before = previous - origin, after = current - origin;
            before.y = after.y = 0;
            float begin = Mathf.Clamp(Vector3.Dot(before, path) / total, 0, total);
            float end = Mathf.Clamp(Vector3.Dot(after, path) / total, 0, total);
            if (end + .00001f < begin) return false;
            // Native positions clamp at the endpoint, including a multi-tick step.
            // A two-millimetre numerical margin includes contact on the final face.
            begin = Math.Max(0, begin - .002f);
            start = Vector3.Lerp(origin, destination, begin / total);
            length = Math.Max(0, end - begin) + .002f;
            return true;
        }
        static bool Valid(Vector3 origin, Vector3 destination)
            => NativeEffectMath.IsFinite(origin) && NativeEffectMath.IsFinite(destination)
                && Math.Abs(origin.x) < 100000 && Math.Abs(origin.z) < 100000
                && Math.Abs(destination.x) < 100000 && Math.Abs(destination.z) < 100000
                && Math.Abs(origin.y) < 1000 && Math.Abs(destination.y) < 1000;
        static Shot Create(Vector3 origin, Vector3 destination)
        {
            Vector3 delta = destination - origin;
            float length = Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
            if (!Valid(origin, destination) || length < .001f || length > 128) return null;
            return new Shot { Origin = origin, Destination = destination, Length = length,
                Yaw = Mathf.Atan2(delta.x, delta.z), Pitch = -Mathf.Atan2(delta.y, length) };
        }
        internal static bool TryPosition(Projectile projectile, out Vector3 position)
        {
            position = Vector3.zero;
            if (projectile == null || !shots.TryGetValue(projectile, out Shot shot)) return false;
            if (shot.HasContact) { position = shot.Contact; return true; }
            position = projectile.ExactPosition;
            position.y = Height(shot.Origin, shot.Destination, position);
            return true;
        }
        internal static bool SuppressAirImpact(Projectile projectile, Thing hit, bool blockedByShield)
            => hit == null && !blockedByShield && TryPosition(projectile, out Vector3 position) && position.y > .08f;

        static void Warn(Exception error)
        {
            if (!warned) { warned = true; Log.Warning("[RimFront] Owned vanilla shot geometry failed safely: " + error.GetBaseException().Message); }
        }

        internal static bool EarlierContact(double distance, double nearest, double limit)
            => !double.IsNaN(distance) && !double.IsInfinity(distance) && distance >= 0
                && distance <= limit + .001 && distance < nearest - .00001;
        internal static Vector3 ClipNativeInterval(Vector3 end, Vector3 contact) => new Vector3(contact.x, end.y, contact.z);
        internal static bool ShouldCommitWorld(bool nativeIntercepted, bool projectileDestroyed, bool projectileSpawned,
            bool blockerSpawned, bool sameMap)
            => !nativeIntercepted && !projectileDestroyed && projectileSpawned && blockerSpawned && sameMap;
        internal static bool DeterministicWorld(bool building, bool fullFill, bool openDoor, bool intendedHit)
            => intendedHit || (building && fullFill && !openDoor);
        internal static bool CrouchCover(bool crouching, bool building, string name)
            => crouching && building && name != null
                && (name.IndexOf("barricade", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("sandbag", StringComparison.OrdinalIgnoreCase) >= 0);
        static bool DeterministicWorld(Thing thing, Projectile projectile)
            => CrouchCover(NativeView.IsCrouching(projectile.intendedTarget.Thing as Pawn), thing is Building, thing.def.defName)
                || DeterministicWorld(thing is Building, thing.def.Fillage == FillCategory.Full,
                thing is Building_Door door && door.Open,
                thing == projectile.intendedTarget.Thing && (projectile.HitFlags & ProjectileHitFlags.IntendedTarget) != 0);

        internal static bool CollectSegmentCells(Vector3 start, Vector3 end, HashSet<IntVec3> cells)
        {
            cells.Clear();
            if (!NativeEffectMath.IsFinite(start) || !NativeEffectMath.IsFinite(end)) return false;
            Vector3 delta = end - start; delta.y = 0;
            float length = delta.magnitude;
            if (length > 128) return false;
            int steps = Math.Max(1, Mathf.CeilToInt(length * 2));
            // Native occupancy covers large buildings; this margin also catches their
            // overhangs, foliage and sliding door leaves beyond the occupied cell.
            for (int i = 0; i <= steps; i++)
            {
                IntVec3 center = Vector3.Lerp(start, end, (float)i / steps).ToIntVec3();
                for (int z = -3; z <= 3; z++) for (int x = -3; x <= 3; x++)
                {
                    cells.Add(new IntVec3(center.x + x, 0, center.z + z));
                    if (cells.Count > MaximumSegmentCells) { cells.Clear(); return false; }
                }
            }
            return true;
        }

        static Thing WorldBlocker(Projectile projectile, Shot shot, out Vector3 contact, out float distance)
        {
            contact = Vector3.zero; distance = shot.SegmentLength;
            // Other Harmony callbacks may synchronously launch/check another shot.
            // Let that nested query use native interception without corrupting scratch buffers.
            if (queryingWorld) return null;
            Vector3 direction = new Vector3(Mathf.Sin(shot.Yaw), -(float)Math.Tan(shot.Pitch), Mathf.Cos(shot.Yaw));
            if (!CollectSegmentCells(shot.SegmentStart, shot.SegmentStart + direction * shot.SegmentLength, segmentCells)) return null;
            segmentThings.Clear();
            queryingWorld = true;
            Thing nearest = null;
            double closest = shot.SegmentLength + .002;
            try
            {
                foreach (IntVec3 cell in segmentCells)
                {
                    if (!cell.InBounds(projectile.Map)) continue;
                    foreach (Thing thing in projectile.Map.thingGrid.ThingsListAtFast(cell))
                    {
                        if (thing == projectile || thing == projectile.Launcher || !thing.Spawned || thing is Pawn
                            || (!(thing is Building) && !(thing is Plant) && thing.def.category != ThingCategory.Item)
                            || !DeterministicWorld(thing, projectile)
                            || !segmentThings.Add(thing.thingIDNumber)) continue;
                        if (segmentThings.Count > MaximumSegmentThings) return null;
                        Bounds coarse = NativeAimTargeting.CoarseThingBounds(thing);
                        if (!(thing is Corpse) && (!NativeAimTargeting.RayBox(shot.SegmentStart, direction, coarse.min, coarse.max, out float entry)
                            || entry > closest)) continue;
                        bool eligible;
                        shot.CheckingEligibility = true;
                        try { eligible = (bool)NativeCanHit.Invoke(projectile, new object[] { thing }); }
                        finally { shot.CheckingEligibility = false; }
                        if (!eligible || !NativeAimTargeting.TryHitThing(thing, shot.SegmentStart, shot.Yaw, shot.Pitch, out double ahead)
                            || !EarlierContact(ahead, closest, shot.SegmentLength)) continue;
                        nearest = thing; closest = ahead;
                    }
                }
                if (nearest != null)
                {
                    distance = (float)closest;
                    contact = shot.SegmentStart + direction * distance;
                }
                return nearest;
            }
            finally { segmentCells.Clear(); segmentThings.Clear(); queryingWorld = false; }
        }

        [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
        static class CastScope
        {
            sealed class Scope
            {
                internal Verb Previous, Verb;
                internal LocalTargetInfo Target;
            }
            static bool Prefix(Verb __instance, ref bool __result, out Scope __state)
            {
                __state = new Scope { Previous = launching };
                if (!NativeManualAim.CanCast(__instance)) { launching = null; __result = false; return false; }
                launching = Supports(__instance) && NativeManualAim.TryGet(__instance, out Vector3 point, out float yaw, out float pitch) ? __instance : null;
                if (launching != null && NativeManualAim.TryGetTarget(__instance, out LocalTargetInfo target))
                {
                    __state.Target = (LocalTargetInfo)VerbTarget.GetValue(__instance);
                    __state.Verb = __instance;
                    // Intended-target flags are necessary for real zero-fill items.
                    // The job remains a cell attack; only this native shot sees the Thing.
                    VerbTarget.SetValue(__instance, target);
                }
                return true;
            }
            static Exception Finalizer(Exception __exception, Scope __state)
            {
                if (__state == null) return __exception;
                launching = __state.Previous;
                if (__state.Verb != null) VerbTarget.SetValue(__state.Verb, __state.Target);
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Launch), new[] { typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) })]
        static class Launch
        {
            [HarmonyPriority(Priority.First)]
            static void Postfix(Projectile __instance, Thing launcher, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget,
                ProjectileHitFlags hitFlags, Vector3 ___origin, ref Vector3 ___destination, ref int ___ticksToImpact, ref int ___lifetime)
            {
                Pawn possessedTarget = RimFrontMultiplayer.InSession
                    ? (RimFrontGame.For(intendedTarget.Thing as Pawn) != null ? intendedTarget.Thing as Pawn : null)
                    : NativeView.Active ? NativeView.ControlledPawn : null;
                if (launching == null && possessedTarget != null && intendedTarget.Thing == possessedTarget
                    && launcher is Pawn shooter && shooter != possessedTarget
                    && __instance.GetType() == typeof(Bullet) && Supports(__instance.def))
                {
                    try
                    {
                        Vector3 incomingOrigin = ___origin;
                        incomingOrigin.y = NativeScene.GetPawnHeight(shooter) * .85f;
                        Vector3 incomingTarget = ___destination;
                        incomingTarget.y = NativeScene.GetPawnHeight(possessedTarget) * .65f;
                        Shot incoming = Create(incomingOrigin, incomingTarget);
                        if (incoming != null)
                        {
                            __instance.HitFlags = CollisionFlags(__instance.HitFlags);
                            shots.Remove(__instance); shots.Add(__instance, incoming);
                        }
                    }
                    catch (Exception error) { Warn(error); }
                    return;
                }
                if (launching == null || __instance.GetType() != typeof(Bullet) || !Supports(__instance.def)
                    || launcher != launching.CasterPawn
                    || !NativeManualAim.TryGet(launching, out Vector3 point, out float yaw, out float pitch)) return;
                try
                {
                    bool directThingHit = usedTarget.HasThing && usedTarget.Thing == intendedTarget.Thing
                        && (hitFlags & ProjectileHitFlags.IntendedTarget) != 0;
                    Vector3 destination = ShotDestination(___destination, intendedTarget.Cell.ToVector3Shifted(), point, directThingHit);
                    Vector3 origin = ___origin; origin.y = RimFrontMultiplayer.InSession
                        ? SimulationGeometry.Eye(launcher as Pawn).y : NativeView.RenderPosition.y;
                    Vector3 visualDestination = destination; visualDestination.y = point.y;
                    Shot shot = Create(origin, visualDestination);
                    if (shot == null) return;
                    Vector3 oldDestination = ___destination;
                    ___destination = destination;
                    try { ___ticksToImpact = Math.Max(1, Mathf.CeilToInt((float)StartingTicks.Invoke(__instance, null))); }
                    catch { ___destination = oldDestination; throw; }
                    // Launch initializes both counters from the same duration.
                    ___lifetime = ___ticksToImpact;
                    __instance.HitFlags = CollisionFlags(__instance.HitFlags);
                    shots.Remove(__instance); shots.Add(__instance, shot);
                }
                catch (Exception error) { Warn(error); }
            }
        }

        [HarmonyPatch(typeof(Projectile), "CanHit")]
        static class CanHit
        {
            static void Postfix(Projectile __instance, Thing thing, ref bool __result)
            {
                if (!__result || !shots.TryGetValue(__instance, out Shot shot) || shot.CheckingEligibility) return;
                try
                {
                    // World candidates are resolved once against the nearest actual
                    // surface. Native pawn rolls still run on the shortened interval.
                    if (shot.PendingWorld && !(thing is Pawn) && DeterministicWorld(thing, __instance)) { __result = false; return; }
                    bool final = shot.FinalImpactDepth > 0 || shot.PendingWorld;
                    if (final && !shot.HasSegment) { __result = false; return; }
                    Vector3 origin = final ? shot.SegmentStart : shot.Origin;
                    float limit = shot.PendingWorld ? shot.WorldLimit : final ? shot.SegmentLength : shot.Length;
                    __result = NativeAimTargeting.TryHitThing(thing, origin, shot.Yaw, shot.Pitch, out double ahead)
                        && ahead <= limit + .001;
                }
                catch (Exception error) { __result = false; Warn(error); }
            }
        }

        [HarmonyPatch(typeof(Projectile), "CheckForFreeInterceptBetween")]
        static class CaptureSegment
        {
            sealed class Pending
            {
                internal Shot Shot;
                internal Thing Blocker;
                internal Vector3 NativeEnd, Contact;
            }
            static void Prefix(Projectile __instance, Vector3 lastExactPos, ref Vector3 newExactPos, out Pending __state)
            {
                __state = null;
                if (!shots.TryGetValue(__instance, out Shot shot)) return;
                shot.HasSegment = FinalSegment(shot.Origin, shot.Destination, lastExactPos, newExactPos,
                    out shot.SegmentStart, out shot.SegmentLength);
                if (!shot.HasSegment || __instance.Destroyed || !__instance.Spawned) return;
                Vector3 originalEnd = newExactPos;
                try
                {
                    Thing blocker = WorldBlocker(__instance, shot, out Vector3 contact, out float distance);
                    if (blocker == null) return;
                    __state = new Pending { Shot = shot, Blocker = blocker, NativeEnd = originalEnd, Contact = contact };
                    shot.PendingWorld = true; shot.WorldLimit = distance;
                    // Let the native shield/interceptor and pawn checks run first, but
                    // only up to this obstruction. Never check a shield behind a wall.
                    newExactPos = ClipNativeInterval(originalEnd, contact);
                }
                catch (Exception error)
                {
                    newExactPos = originalEnd; shot.PendingWorld = shot.CheckingEligibility = false; __state = null; Warn(error);
                }
            }
            static void Postfix(Projectile __instance, Pending __state, ref bool __result)
            {
                if (__state == null) return;
                if (!ShouldCommitWorld(__result, __instance.Destroyed, __instance.Spawned,
                    __state.Blocker.Spawned, __state.Blocker.Map == __instance.Map)) return;
                __state.Shot.PendingWorld = false;
                __state.Shot.Contact = __state.Contact; __state.Shot.HasContact = true;
                __instance.Position = __state.Contact.ToIntVec3();
                // Damage, armor, destruction, notifications and impact observers all
                // remain on Bullet.Impact. Mark the interval consumed before invoking it.
                __result = true;
                NativeImpact.Invoke(__instance, new object[] { __state.Blocker, false });
            }
            static Exception Finalizer(Projectile __instance, Pending __state, ref Vector3 newExactPos, Exception __exception)
            {
                if (__state != null)
                {
                    newExactPos = __state.NativeEnd;
                    __state.Shot.PendingWorld = __state.Shot.CheckingEligibility = false;
                    if (!__instance.Destroyed) __state.Shot.HasContact = false;
                }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Projectile), "ImpactSomething")]
        static class FinalImpactScope
        {
            static void Prefix(Projectile __instance, out Shot __state)
            {
                shots.TryGetValue(__instance, out __state);
                if (__state != null) __state.FinalImpactDepth++;
            }
            static Exception Finalizer(Exception __exception, Shot __state)
            {
                if (__state != null) __state.FinalImpactDepth = Math.Max(0, __state.FinalImpactDepth - 1);
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Bullet), "Impact")]
        static class Impact
        {
            [HarmonyPriority(Priority.First)]
            static bool Prefix(Bullet __instance, Thing hitThing, bool blockedByShield)
            {
                if (!SuppressAirImpact(__instance, hitThing, blockedByShield)) return true;
                __instance.Destroy();
                return false;
            }
        }

        [HarmonyPatch(typeof(Projectile), nameof(Projectile.ExposeData))]
        static class Save
        {
            static void Postfix(Projectile __instance)
            {
                bool owned = shots.TryGetValue(__instance, out Shot shot);
                if (Scribe.mode == LoadSaveMode.Saving && !owned) return;
                Scribe_Values.Look(ref owned, "rimFrontManualRay", false);
                if (!owned) return;
                Vector3 origin = shot?.Origin ?? Vector3.zero, destination = shot?.Destination ?? Vector3.zero;
                Scribe_Values.Look(ref origin, "rimFrontRayOrigin");
                Scribe_Values.Look(ref destination, "rimFrontRayDestination");
                if (Scribe.mode == LoadSaveMode.LoadingVars && Supports(__instance.def))
                {
                    Shot loaded = Create(origin, destination);
                    if (loaded != null) { shots.Remove(__instance); shots.Add(__instance, loaded); }
                }
            }
        }
    }
}
