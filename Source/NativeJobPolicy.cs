namespace RimFront
{
    internal static class NativeJobPolicy
    {
        internal static bool RequiresNativeControl(string name, bool owned, bool playerForced, bool forceSleep, int expiryInterval,
            bool compatibleBipod = false)
        {
            if (owned || name == null) return false;
            if (forceSleep) return true;
            if (compatibleBipod && name == "JobDef_SetUpBipod" && !playerForced) return false;
            if (name == "Wait_Combat" || name == "Wait" || name == "ReloadWeapon") return false;
            // EndCurrentJob inserts this one-tick posture-preserving wait between normal
            // jobs. It is bookkeeping, not an order to move or relinquish FPS control.
            if (name == "Wait_MaintainPosture" && expiryInterval == 1 && !playerForced && !forceSleep) return false;
            return true;
        }
        internal static bool CompatibleBipod(string name, bool playerForced, bool forceSleep,
            bool nativeDriver, bool equippedTarget, bool hasBipod)
            => name == "JobDef_SetUpBipod" && !playerForced && !forceSleep
                && nativeDriver && equippedTarget && hasBipod;
    }
}
