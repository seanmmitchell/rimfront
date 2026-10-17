using System;
using System.Reflection;

internal static partial class Program
{
    static void TestShotCadence(Assembly assembly)
    {
        Type animation = assembly.GetType("RimFront.NativePawnAnimation", true);
        Type snapshot = animation.GetNestedType("Snapshot", Flags);
        Type weapons = assembly.GetType("RimFront.NativeWeapons", true);
        MethodInfo age = animation.GetMethod("Age", Flags), recoil = animation.GetMethod("RecoilAt", Flags);
        MethodInfo evaluate = animation.GetMethod("Evaluate", Flags), offset = weapons.GetMethod("FirstPersonOffset", Flags);
        object human = Enum.Parse(form, "Human"), arm = Enum.Parse(part, "RightArm");
        object right = Enum.Parse(weapons.GetNestedType("EquipmentHand", Flags), "Right");
        float Pulse(int tick, int lastShot) => (float)recoil.Invoke(null,
            new[] { age.Invoke(null, new object[] { tick, lastShot }) });
        float Arm(float pulse, bool reload = false)
        {
            object state = Activator.CreateInstance(snapshot);
            Set(state, "Aiming", true); Set(state, "Recoil", pulse); Set(state, "Reloading", reload);
            return Coordinates(Get(evaluate.Invoke(null, new[] { human, state, arm }), "Euler"))[0];
        }
        float Gun(float pulse) => Coordinates(offset.Invoke(null, new[] { right, (object)pulse }))[2];
        // These timestamps represent actual launches, including several completed
        // bursts separated by native cooldown and reload. Holding alone adds none.
        int[] launches = { 100, 106, 112, 180, 186, 192, 420, 426, 432 };
        int last = int.MinValue, index = 0;
        for (int tick = 90; tick <= 450; tick++)
        {
            bool shot = index < launches.Length && launches[index] == tick;
            if (shot) { last = launches[index++]; }
            float pulse = Pulse(tick, last);
            Check(float.IsFinite(pulse) && pulse >= 0 && pulse <= 1, "successive native bursts keep recoil finite and bounded");
            if (shot)
            {
                Check(pulse == 1, "every observed shot restarts recoil across burst and reload boundaries");
                Check(Arm(pulse) < Arm(0) && Gun(pulse) < Gun(0), "actual shot moves both pawn arm and first-person gun");
            }
            if (last == int.MinValue || tick - last >= 10)
                Check(pulse == 0, "warmup, reload and held-input gaps without actual shots cannot create recoil");
        }
        float pausedPulse = Pulse(108, 106), pausedGun = Gun(pausedPulse), pausedArm = Arm(pausedPulse);
        for (int frame = 0; frame < 50; frame++)
            Check(Gun(Pulse(108, 106)) == pausedGun && Arm(Pulse(108, 106)) == pausedArm,
                "paused render frames cannot advance first-person or pawn recoil without a simulation tick");
        Check(Pulse(109, 106) < pausedPulse, "resuming simulation advances the existing recoil rather than creating another shot");
        Check(Arm(Pulse(108, 106), true) == Arm(0, true), "native reload pose takes priority over recent arm recoil");
        Check(Pulse(442, 432) == 0 && Gun(Pulse(442, 432)) == Gun(0), "last shot settles fully without requiring another input event");
        Check(Pulse(99, 100) == 0, "future or rewound shot timestamp does not produce premature recoil");
    }
}
