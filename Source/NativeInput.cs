using System;

namespace RimFront
{
    // Produced by the in-game view and consumed on the game thread.
    public sealed class InputCommand
    {
        public string action;
        public float forward;
        public float strafe;
        public float yaw;
        public float pitch;
        public bool fire;
        public bool reload;
        public bool cycleFireMode;
        public bool toggleBipod;

        public bool IsValid()
        {
            return (action == "input" || action == "release") &&
                Finite(forward) && Finite(strafe) && Finite(yaw) && Finite(pitch) && Math.Abs(pitch) < Math.PI / 2 &&
                Math.Abs(forward) <= 1 && Math.Abs(strafe) <= 1 && Math.Abs(yaw) <= 100000;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    // HUD data sampled from the equipped native weapon.
    public sealed class WeaponState
    {
        public string label = "Unarmed";
        public string status = "";
        public int ammo = -1;
        public int capacity = -1;
        public bool canReload;
        public string fireMode = "Fixed";
    }
}
