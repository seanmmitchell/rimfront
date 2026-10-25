using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimFront
{
    // Session-local posture; no shared stats, definitions or saved pawn state change.
    internal static class NativeStance
    {
        internal const float CrouchHeight = .64f, CrouchEyeHeight = .53f;
        internal static float VerticalScale(float standingHeight, float scale, bool crouched)
            => crouched ? Mathf.Min(scale, (CrouchHeight - .04f) / Mathf.Max(.01f, standingHeight)) : scale;
        internal static Bounds CrouchBounds(Bounds bounds)
        {
            Vector3 size = bounds.size;
            size.y = Mathf.Min(size.y, CrouchHeight);
            bounds.size = size;
            Vector3 center = bounds.center;
            center.y = size.y * .5f;
            bounds.center = center;
            return bounds;
        }
    }

    internal static class MovingFireStance
    {
        // Only the pather may ignore an owned firing stance or the equipped
        // reload's remaining shot cooldown. Native attack toils,
        // cleanup and CE must continue seeing the real busy state, even at rest.
        internal static bool PathBusy(bool busy, Pawn_PathFollower follower)
            => busy && !(RimFrontGame.For(follower) != null
                && (RimFrontGame.For(follower)?.AllowsAttackMovement(RimFrontGame.For(follower).ControlledPawn.stances.curStance as Stance_Busy) == true
                    || RimFrontGame.For(follower)?.AllowsReloadMovement(RimFrontGame.For(follower).ControlledPawn.stances.curStance as Stance_Busy) == true));
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "CostToPayThisTick")]
    internal static class ControlledMovementPenalty
    {
        static void Postfix(Pawn_PathFollower __instance, ref float __result)
        {
            RimFrontGame session = RimFrontGame.For(__instance);
            if (session != null && session.ControlledPawn?.pather == __instance)
                __result *= session.MovementSpeedFactor;
        }
    }
}
