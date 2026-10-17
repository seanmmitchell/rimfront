using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

internal static partial class Program
{
    static void TestBloodEffects(Assembly assembly)
    {
        Type type = assembly.GetType("RimFront.NativeBloodEffects", true);
        MethodInfo gate = type.GetMethod("ShouldEmit", Flags), alive = type.GetMethod("Alive", Flags);
        MethodInfo scale = type.GetMethod("RemainingScale", Flags), spray = type.GetMethod("SprayVelocity", Flags);
        MethodInfo position = type.GetMethod("PositionAt", Flags), count = type.GetMethod("DropletCount", Flags), tissue = type.GetMethod("TissueCount", Flags);
        foreach (bool trail in new[] { false, true })
        foreach (bool fragment in new[] { false, true })
        foreach (bool blood in new[] { false, true })
        foreach (bool gore in new[] { false, true })
        foreach (bool trails in new[] { false, true })
            Check((bool)type.GetMethod("ShouldRender", Flags).Invoke(null, new object[] { trail, fragment, blood, gore, trails })
                == (trail ? trails : blood && (!fragment || gore)), "blood splash, gore and bleeding trail settings filter independently");
        foreach (bool external in new[] { false, true })
        foreach (bool flesh in new[] { false, true })
        foreach (bool bleeds in new[] { false, true })
        foreach (bool makesBlood in new[] { false, true })
        foreach (float damage in new[] { -1f, 0f, .01f, 18f, 999f, float.NaN, float.PositiveInfinity })
        {
            bool expected = external && flesh && bleeds && makesBlood && !float.IsNaN(damage) && !float.IsInfinity(damage) && damage > 0;
            Check((bool)gate.Invoke(null, new object[] { external, flesh, bleeds, makesBlood, damage }) == expected,
                "blood requires actual positive finite external bleeding damage on flesh");
        }
        int lifetime = (int)type.GetField("Lifetime", Flags).GetRawConstantValue();
        for (int age = -2; age <= lifetime + 2; age++)
        {
            Check((bool)alive.Invoke(null, new object[] { age }) == (age >= 0 && age < lifetime), "blood particle lifetime excludes future/expired events");
            float size = (float)scale.Invoke(null, new object[] { age });
            Check(float.IsFinite(size) && size >= 0 && size <= 1 && ((age < 0 || age >= lifetime) ? size == 0 : size > 0), "blood expiry has bounded shrink");
        }
        foreach (float damage in new[] { 0f, .01f, 17.99f, 18f, 500f, float.MaxValue, float.NaN, float.PositiveInfinity })
        {
            int drops = (int)count.Invoke(null, new object[] { damage });
            int fragments = (int)tissue.Invoke(null, new object[] { damage, true });
            Check(drops >= 0 && drops <= 16 && fragments >= 0 && fragments <= 3, "blood and tissue counts bounded even at extreme damage");
            Check((int)tissue.Invoke(null, new object[] { damage, false }) == 0, "gore toggle suppresses all tissue");
            if (damage < 18 || !float.IsFinite(damage)) Check(fragments == 0, "small and invalid hits never emit tissue");
        }
        foreach (object incoming in new[] { V(0, 0, 0), V(1, 2, -1), V(float.NaN, 0, 0), V(float.PositiveInfinity, 0, 1) })
        for (int i = 0; i < 16; i++)
        {
            object velocity = spray.Invoke(null, new object[] { incoming, i, int.MaxValue, i % 2 == 0 });
            float[] v = Coordinates(velocity);
            Check(Array.TrueForAll(v, float.IsFinite) && v[1] >= 1 && v[1] <= 3, "spray finite and bounded for invalid input direction");
            for (int age = 0; age <= lifetime; age++)
            {
                float[] a = Coordinates(position.Invoke(null, new object[] { V(15, 1, 20), velocity, age }));
                float[] b = Coordinates(position.Invoke(null, new object[] { V(15, 1, 20), velocity, age }));
                Check(Array.TrueForAll(a, float.IsFinite) && a[1] >= .03999f && Math.Abs(a[0] - 15) < 5 && Math.Abs(a[2] - 20) < 5,
                    "blood trajectory stays finite, grounded and spatially bounded");
                Check(a[0] == b[0] && a[1] == b[1] && a[2] == b[2], "unchanged simulation age freezes blood trajectory");
            }
        }
        float[] sanitized = Coordinates(position.Invoke(null, new object[] { V(float.NaN, 0, 0), V(float.PositiveInfinity, 0, 0), int.MaxValue }));
        Check(Array.TrueForAll(sanitized, float.IsFinite), "invalid particle trajectory inputs sanitized");

        // Actual ring and mesh helper execution with managed values only: no GPU/game objects.
        Array particles = (Array)type.GetField("particles", Flags).GetValue(null), savedParticles = (Array)particles.Clone();
        Array events = (Array)type.GetField("events", Flags).GetValue(null), savedEvents = (Array)events.Clone();
        string[] stateNames = { "nextParticle", "nextEvent", "revision" };
        var saved = new Dictionary<string, object>();
        foreach (string field in stateNames) saved[field] = type.GetField(field, Flags).GetValue(null);
        var lists = new Dictionary<string, object[]>();
        foreach (string field in new[] { "vertices", "triangles", "colors", "uv" })
        {
            IList list = (IList)type.GetField(field, Flags).GetValue(null);
            object[] items = new object[list.Count]; list.CopyTo(items, 0); lists[field] = items;
        }
        try
        {
            Array.Clear(particles); Array.Clear(events);
            type.GetField("nextParticle", Flags).SetValue(null, 0); type.GetField("nextEvent", Flags).SetValue(null, 0);
            Type particleType = particles.GetType().GetElementType();
            MethodInfo push = type.GetMethod("Push", Flags), observe = type.GetMethod("Observe", Flags);
            for (int i = 0; i < particles.Length * 3 + 7; i++)
            {
                object particle = Activator.CreateInstance(particleType);
                Set(particle, "Origin", V(1, 1, 1)); Set(particle, "Velocity", V(0, 1, 0)); Set(particle, "Size", .03f); Set(particle, "Owner", i);
                push.Invoke(null, new[] { particle });
            }
            Check((int)type.GetField("nextParticle", Flags).GetValue(null) == 7, "blood ring wraps at exact fixed capacity");
            foreach (object particle in particles) Check((bool)Get(particle, "Active") && (int)Get(particle, "Owner") >= particles.Length * 2 + 7,
                "blood ring retains only newest capacity events");
            object invalid = Activator.CreateInstance(particleType); Set(invalid, "Origin", V(float.NaN, 0, 0)); Set(invalid, "Size", .1f);
            push.Invoke(null, new[] { invalid });
            Check((int)type.GetField("nextParticle", Flags).GetValue(null) == 7, "invalid particle does not consume ring slot");
            Check((bool)observe.Invoke(null, new object[] { 42, 123, false }), "first actual bleed event accepted");
            Check(!(bool)observe.Invoke(null, new object[] { 42, 123, false }), "duplicate bleed event merged");
            Check((bool)observe.Invoke(null, new object[] { 42, 123, true }), "damage can replace early same-tick bleed");
            Check(!(bool)observe.Invoke(null, new object[] { 42, 123, true }), "same-tick damage burst bounded");
            Check(!(bool)observe.Invoke(null, new object[] { 42, 123, false }), "late bleed cannot duplicate damage burst");
            foreach (bool flat in new[] { false, true })
            {
                foreach (string field in lists.Keys) ((IList)type.GetField(field, Flags).GetValue(null)).Clear();
                type.GetMethod("AddOctahedron", Flags).Invoke(null, new object[] { V(0, 0, 0), .1f, flat, Tint(.5f, .02f, .03f) });
                IList meshVertices = (IList)type.GetField("vertices", Flags).GetValue(null), meshIndices = (IList)type.GetField("triangles", Flags).GetValue(null);
                Check(meshVertices.Count == 6 && meshIndices.Count == 24, "blood particle batches compact indexed geometry");
                for (int i = 0; i < meshIndices.Count; i += 3)
                {
                    float[] a = Coordinates(meshVertices[(int)meshIndices[i]]), b = Coordinates(meshVertices[(int)meshIndices[i + 1]]), c = Coordinates(meshVertices[(int)meshIndices[i + 2]]);
                    float[] u = { b[0] - a[0], b[1] - a[1], b[2] - a[2] }, v = { c[0] - a[0], c[1] - a[1], c[2] - a[2] };
                    float nx = u[1] * v[2] - u[2] * v[1], ny = u[2] * v[0] - u[0] * v[2], nz = u[0] * v[1] - u[1] * v[0];
                    Check(nx * a[0] + ny * a[1] + nz * a[2] > 0, "airborne and flat blood triangles face outward for engine culling");
                }
            }
        }
        finally
        {
            Array.Copy(savedParticles, particles, particles.Length); Array.Copy(savedEvents, events, events.Length);
            foreach (var entry in saved) type.GetField(entry.Key, Flags).SetValue(null, entry.Value);
            foreach (var entry in lists)
            {
                IList list = (IList)type.GetField(entry.Key, Flags).GetValue(null); list.Clear(); foreach (object item in entry.Value) list.Add(item);
            }
        }
    }
}
