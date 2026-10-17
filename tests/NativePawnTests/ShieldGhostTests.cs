using System;
using System.Reflection;

internal static partial class Program
{
    static void TestShieldGhost056(Assembly assembly)
    {
        Type effects = assembly.GetType("RimFront.NativeShieldEffects", true);
        MethodInfo offset = effects.GetMethod("GhostOffset", Flags);
        MethodInfo contact = effects.GetMethod("ShouldShowMeleeContact", Flags);
        foreach (object direction in new[] { V(1, 0, 0), V(0, 0, 1), V(1, 8, 1), V(0, 0, 0), V(float.NaN, 0, 0) })
        foreach (int age in new[] { -1, 0, 1, 8, 17, 18, 500 })
        foreach (float force in new[] { 0, .35f, .6f, 1f, 100f, float.NaN, float.PositiveInfinity })
        {
            float[] blue = Coordinates(offset.Invoke(null, new[] { direction, (object)age, false, force }));
            float[] red = Coordinates(offset.Invoke(null, new[] { direction, (object)age, true, force }));
            double dx = red[0] - blue[0], dy = red[1] - blue[1], dz = red[2] - blue[2];
            Check(float.IsFinite(red[0]) && float.IsFinite(red[1]) && float.IsFinite(red[2])
                && Math.Sqrt(dx * dx + dz * dz) <= .100001 && Math.Abs(dy) <= .020001,
                "shield echoes remain within 10 cm total horizontal and 2 cm total vertical separation, independent of force or direction");
            Check(Close(blue, new[] { -red[0], -red[1], -red[2] }), "blue and red echoes split symmetrically without expanding shield size");
            if (age < 0 || age >= 18 || force == 0 || !float.IsFinite(force))
                Check(Close(blue, new[] { 0f, 0f, 0f }), "expired, future and invalid-force echoes cannot drift from the shield");
        }
        float[] soft = Coordinates(offset.Invoke(null, new object[] { V(0, 0, 1), 0, true, .35f }));
        float[] hard = Coordinates(offset.Invoke(null, new object[] { V(0, 0, 1), 0, true, 1f }));
        float[] settled = Coordinates(offset.Invoke(null, new object[] { V(0, 0, 1), 17, true, 1f }));
        Check(hard[0] > soft[0] && hard[1] > soft[1] && Math.Abs(settled[0]) < soft[0],
            "stronger native hits displace echoes farther, then settle within the brief impact lifetime");
        foreach (bool active in new[] { false, true }) foreach (bool external in new[] { false, true })
        foreach (bool ranged in new[] { false, true }) foreach (bool explosive in new[] { false, true })
        foreach (bool pawnSource in new[] { false, true })
        {
            bool result = (bool)contact.Invoke(null, new object[] { active, external, ranged, explosive, pawnSource, 12f });
            Check(result == (active && external && !ranged && !explosive && pawnSource),
                "melee-contact feedback requires an active energy shield and an actual external melee injury");
        }
        foreach (float damage in new[] { 0, -1, float.NaN, float.PositiveInfinity })
            Check(!(bool)contact.Invoke(null, new object[] { true, true, false, false, true, damage }),
                "misses, absent damage and malformed damage cannot fake a melee-contact echo");
    }
}
