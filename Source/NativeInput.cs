using System;
using Verse;

namespace RimFront
{
    // Produced by the in-game view and consumed on the game thread.
    public sealed class InputCommand : IExposable
    {
        public string action;
        public float forward;
        public float strafe;
        public float yaw;
        public float pitch;
        public bool fire;
        public bool crouch;
        public bool reload;
        public bool cycleFireMode;
        public bool toggleBrace;
        public bool toggleBipod;

        internal InputCommand Copy() => (InputCommand)MemberwiseClone();
        public void ExposeData()
        {
            Scribe_Values.Look(ref action, "action", "input");
            Scribe_Values.Look(ref forward, "forward"); Scribe_Values.Look(ref strafe, "strafe");
            Scribe_Values.Look(ref yaw, "yaw"); Scribe_Values.Look(ref pitch, "pitch");
            Scribe_Values.Look(ref fire, "fire"); Scribe_Values.Look(ref crouch, "crouch");
            Scribe_Values.Look(ref reload, "reload"); Scribe_Values.Look(ref cycleFireMode, "cycleFireMode");
            Scribe_Values.Look(ref toggleBrace, "toggleBrace"); Scribe_Values.Look(ref toggleBipod, "toggleBipod");
        }
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
