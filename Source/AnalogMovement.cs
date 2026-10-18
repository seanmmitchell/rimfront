using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimFront
{
    internal static class AnalogMovement
    {
        static readonly MethodInfo MoveCost = AccessTools.Method(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new[] { typeof(Pawn), typeof(IntVec3) });
        static readonly MethodInfo MovePayment = AccessTools.Method(typeof(Pawn_PathFollower), "CostToPayThisTick", Type.EmptyTypes);
        static readonly FieldInfo LastMoved = AccessTools.Field(typeof(Pawn_PathFollower), "lastMovedTick");
        static readonly FieldInfo LastEntered = AccessTools.Field(typeof(Pawn_PathFollower), "lastEnteredCellTick");
        static readonly Func<int, int, bool> Solid = Blocked;
        static Pawn pawn;
        static Map map;
        static Building_Door pendingDoor;
        static Vector3 position, previous;
        static IntVec3 occupied;
        static float tickTime, radius;
        static int walkedCells;
        internal static bool IsMoving { get; private set; }
        internal static Vector3 Position { get { ReconcilePosition(); return position; } }
        internal static Vector3 Velocity { get { ReconcilePosition(); return IsMoving ? (position - previous) * 60f : Vector3.zero; } }
        internal static bool Active => pawn != null;
        internal static bool Controls(Pawn candidate) => pawn != null && candidate == pawn;
        internal static bool Owns(Pawn_PathFollower pather) => pawn != null && pawn.pather == pather;

        internal static Vector3 RenderPosition
        {
            get
            {
                ReconcilePosition();
                if (pawn == null || Find.TickManager.Paused) return position;
                float alpha = Mathf.Clamp01((Time.realtimeSinceStartup - tickTime) * 60f * Find.TickManager.TickRateMultiplier);
                return Vector3.Lerp(previous, position, alpha);
            }
        }
        internal static bool TryGetPosition(Thing thing, out Vector3 result)
        {
            ReconcilePosition();
            result = position;
            return pawn != null && thing == pawn && pawn.Spawned && pawn.Map == map;
        }
        internal static bool Start(Pawn controlled, out string reason)
        {
            Stop();
            reason = null;
            if (MoveCost == null || MovePayment == null || LastMoved == null || LastEntered == null)
            { reason = "Native movement API unavailable; using native path fallback."; return false; }
            if (!AnalogCombatCompatibility.Supported)
            { reason = "Analog CE adapter unavailable: " + AnalogCombatCompatibility.FailureReason + ". Using native path fallback."; return false; }
            float bodyRadius;
            try { bodyRadius = Math.Max(NativeScene.GetPawnWidth(controlled) * .5f, AnalogCombatCompatibility.GetCollisionRadius(controlled)); }
            catch (Exception error) { reason = "Could not read collision dimensions: " + error.Message + ". Using native path fallback."; return false; }
            if (float.IsNaN(bodyRadius) || float.IsInfinity(bodyRadius) || bodyRadius >= .49f)
            { reason = "This pawn's body is too wide for the analog controller. Using native path fallback."; return false; }
            controlled.pather.StopDead();
            controlled.pather.ResetToCurrentPosition();
            pawn = controlled; map = controlled.Map; occupied = controlled.Position;
            position = previous = occupied.ToVector3Shifted();
            radius = Math.Max(.14f, bodyRadius);
            tickTime = Time.realtimeSinceStartup; walkedCells = 0; IsMoving = false;
            return true;
        }
        internal static void Stop()
        {
            Pawn old = pawn;
            pawn = null; map = null; pendingDoor = null; IsMoving = false;
            if (old?.Spawned == true)
            {
                old.pather.StopDead(); old.pather.ResetToCurrentPosition();
                old.Drawer.tweener.ResetTweenedPosToRoot();
            }
        }
        internal static void Halt()
        {
            ReconcilePosition();
            IsMoving = false;
            previous = position;
            tickTime = Time.realtimeSinceStartup;
            MaintainDoors();
        }
        internal static void Tick(float yaw, float forward, float strafe)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map != map) return;
            ReconcilePosition();
            previous = position; tickTime = Time.realtimeSinceStartup; IsMoving = false;
            MaintainDoors();
            AnalogCollision.Point direction = AnalogCollision.Direction(yaw, forward, strafe);
            if (Math.Abs(direction.X) + Math.Abs(direction.Z) < .001) return;
            int sx = Math.Abs(direction.X) >= .3827 ? Math.Sign(direction.X) : 0;
            int sz = Math.Abs(direction.Z) >= .3827 ? Math.Sign(direction.Z) : 0;
            IntVec3 costCell = occupied + new IntVec3(sx, 0, sz);
            if (!costCell.InBounds(map) || !costCell.WalkableBy(map, pawn)) costCell = occupied;
            float cost = (float)MoveCost.Invoke(null, new object[] { pawn, costCell });
            if (float.IsNaN(cost) || float.IsInfinity(cost) || cost <= 0) return;
            float payment, originalCost = pawn.pather.nextCellCostTotal;
            try
            {
                // Let the native payment calculation apply stagger, flight and its minimum
                // progress rule to this movement cost. Do not retain synthetic path state.
                pawn.pather.nextCellCostTotal = cost;
                payment = (float)MovePayment.Invoke(pawn.pather, null);
            }
            finally { pawn.pather.nextCellCostTotal = originalCost; }
            if (float.IsNaN(payment) || float.IsInfinity(payment) || payment <= 0) return;
            bool diagonalCost = costCell.x != occupied.x && costCell.z != occupied.z;
            double distance = Math.Min(.25 * (RimFrontGame.Active?.MovementSpeedFactor ?? 1f),
                payment * (diagonalCost ? Math.Sqrt(2) : 1) / Math.Max(1, cost));
            AnalogCollision.Point moved = AnalogCollision.Move(position.x, position.z, direction.X * distance, direction.Z * distance, radius, Solid);
            position.x = (float)moved.X; position.z = (float)moved.Z;
            IsMoving = (position - previous).sqrMagnitude > .00000001f;
            if (!IsMoving) return;
            pawn.Rotation = Rot4.FromAngleFlat(Mathf.Atan2((float)direction.X, (float)direction.Z) * Mathf.Rad2Deg);
            pawn.pather.lastMoveDirection = Mathf.Atan2((float)direction.X, (float)direction.Z) * Mathf.Rad2Deg;
            LastMoved.SetValue(pawn.pather, Find.TickManager.TicksGame);
            IntVec3 next = position.ToIntVec3();
            if (next != occupied) EnterCell(next);
            MaintainDoors();
        }
        static void ReconcilePosition()
        {
            if (pawn == null || !pawn.Spawned || pawn.Map != map || pawn.Position == occupied) return;
            // Also runs from combat/render reads and Halt, so a teleport during a reload
            // cannot leave a stale hitbox or camera behind until movement resumes.
            occupied = pawn.Position; position = previous = occupied.ToVector3Shifted();
            IsMoving = false; pendingDoor = null; tickTime = Time.realtimeSinceStartup;
            pawn.pather.ResetToCurrentPosition();
        }
        static bool Blocked(int x, int z)
        {
            IntVec3 cell = new IntVec3(x, 0, z);
            if (!cell.InBounds(map) || cell.Fogged(map)) return true;
            Building_Door door = cell.GetDoor(map);
            if (door != null)
            {
                if (door.IsForbiddenToPass(pawn)) return true;
                if (!door.Open || door.TicksTillFullyOpened > 0)
                {
                    if (door.PawnCanOpen(pawn))
                    {
                        door.Notify_PawnApproaching(pawn, 1f);
                        if (!door.Open) door.StartManualOpenBy(pawn);
                        door.CheckFriendlyTouched(pawn);
                    }
                    return true;
                }
            }
            if (door == null && !cell.WalkableBy(map, pawn)) return true;
            // Pawns are not solid furniture: preserve native collision rules for friendly
            // traffic, jobs and hostile encounters. A pawn entering our registered cell
            // must not prevent us leaving it; walls and closed doors above remain solid.
            return cell != occupied && PawnUtility.ShouldCollideWithPawns(pawn)
                && PawnUtility.AnyPawnBlockingPathAt(cell, pawn, false, false, false, false);
        }
        static void EnterCell(IntVec3 cell)
        {
            IntVec3 last = occupied;
            pawn.Position = cell; // Native setter maintains all spatial/region/cover/gas registrations.
            occupied = cell; pawn.pather.nextCell = cell;
            LastEntered.SetValue(pawn.pather, Find.TickManager.TicksGame);
            pawn.filth?.Notify_EnteredNewCell();
            if (pawn.BodySize > .9f) map.snowGrid.AddDepth(cell, -.001f);
            if (++walkedCells >= 12)
            { GenClamor.DoClamor(pawn, 7f, ClamorDefOf.Movement); walkedCells = 0; }
            Building_Door door = last.GetDoor(map);
            if (door != null && !pawn.HostileTo(door))
            {
                door.CheckFriendlyTouched(pawn);
                pendingDoor = door;
            }
        }
        static void MaintainDoors()
        {
            if (pawn == null || !pawn.Spawned || pawn.Map != map) return;
            occupied.GetDoor(map)?.CheckFriendlyTouched(pawn);
            if (pendingDoor == null) return;
            if (!pendingDoor.Spawned || pendingDoor.Map != map) { pendingDoor = null; return; }
            CellRect rect = pendingDoor.OccupiedRect();
            bool overlaps = position.x + radius > rect.minX && position.x - radius < rect.maxX + 1
                && position.z + radius > rect.minZ && position.z - radius < rect.maxZ + 1;
            if (overlaps) { pendingDoor.CheckFriendlyTouched(pawn); return; }
            if (!pendingDoor.BlockedOpenMomentary && !pendingDoor.HoldOpen && pendingDoor.SlowsPawns && pendingDoor.PawnCanOpen(pawn)) pendingDoor.StartManualCloseBy(pawn);
            pendingDoor = null;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawPos), MethodType.Getter)]
    internal static class AnalogPawnDrawPosition
    {
        static void Postfix(Pawn __instance, ref Vector3 __result)
        {
            if (AnalogMovement.TryGetPosition(__instance, out Vector3 p)) { __result.x = p.x; __result.z = p.z; }
        }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    internal static class AnalogOwnsPathTick { static bool Prefix(Pawn_PathFollower __instance) => !AnalogMovement.Owns(__instance); }
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    internal static class AnalogOwnsPathStart
    {
        static bool Prefix(Pawn_PathFollower __instance)
        {
            if (!AnalogMovement.Owns(__instance)) return true;
            RimFrontGame session = RimFrontGame.Active;
            if (session != null && session.NeedsNativeJobControl)
            {
                // Release BEFORE the original request so a forced job keeps the exact path
                // target/end mode it requested, including jobs that path to target B or C.
                session.Release("Control released for a native movement job.");
                return true;
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.Moving), MethodType.Getter)]
    internal static class AnalogMovingState
    { static void Postfix(Pawn_PathFollower __instance, ref bool __result) { if (AnalogMovement.Owns(__instance)) __result = AnalogMovement.IsMoving; } }
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.MovingNow), MethodType.Getter)]
    internal static class AnalogMovingNowState
    { static void Postfix(Pawn_PathFollower __instance, ref bool __result) { if (AnalogMovement.Owns(__instance)) __result = AnalogMovement.IsMoving; } }
}
