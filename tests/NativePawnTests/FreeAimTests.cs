using System;
using System.Reflection;

internal static partial class Program
{
    static void TestFreeAim(Assembly assembly)
    {
        Type type = assembly.GetType("RimFront.NativeAimTargeting", true);
        MethodInfo ray = type.GetMethod("Ray", Flags), triangle = type.GetMethod("RayTriangle", Flags), box = type.GetMethod("RayBox", Flags);
        MethodInfo nearer = type.GetMethod("Nearer", Flags), cellRange = type.GetMethod("CellWithinRange", Flags);
        MethodInfo doorSignature = type.GetMethod("DoorSignature", Flags), coarse = type.GetMethod("CoarseBounds", Flags);
        foreach (float openness in new[] { 0f, .12f, .5f, .95f, 1f })
        {
            int first = (int)doorSignature.Invoke(null, new object[] { openness });
            for (int frame = 0; frame < 20; frame++)
                Check((int)doorSignature.Invoke(null, new object[] { openness }) == first, "stationary door geometry signature does not change with render frame");
        }
        Check((int)doorSignature.Invoke(null, new object[] { 0f }) != (int)doorSignature.Invoke(null, new object[] { .5f }), "moving door leaf geometry invalidates cache");
        Check((int)type.GetField("MaximumCached", Flags).GetRawConstantValue() >= (int)type.GetField("MaximumThings", Flags).GetRawConstantValue(),
            "one bounded query working set fits mesh cache without cyclic eviction");
        Type objectModels = assembly.GetType("RimFront.NativeObjectModels", true), kinds = objectModels.GetNestedType("Kind", Flags);
        Type quaternion = vector.Assembly.GetType("UnityEngine.Quaternion", true);
        MethodInfo euler = quaternion.GetMethod("Euler", new[] { typeof(float), typeof(float), typeof(float) });
        foreach (object kind in Enum.GetValues(kinds))
        foreach (float[] footprint in new[] { new[] { .5f, .5f }, new[] { 2f, 5f }, new[] { 20f, 20f } })
        foreach (float rotation in new[] { 0f, 90f, 180f, 270f })
        {
            string name = kind.ToString();
            int category = name == "Mushroom" || name == "Cactus" || name == "Broadleaf" ? 1
                : Convert.ToInt32(kind) >= Convert.ToInt32(Enum.Parse(kinds, "Log")) ? 2 : 0;
            object mesh = Activator.CreateInstance(builder, true);
            Set(mesh, "Origin", V(3, .04f, -7)); Set(mesh, "Rotation", euler.Invoke(null, new object[] { 0f, rotation, 0f }));
            objectModels.GetMethod("Build", Flags).Invoke(null, new object[] { mesh, kind, footprint[0], footprint[1], 1f, Tint(.5f, .5f, .5f), 100 });
            object envelope = coarse.Invoke(null, new object[] { V(3, 0, -7), footprint[0], footprint[1], rotation, category });
            float[] min = Coordinates(envelope.GetType().GetProperty("min").GetValue(envelope));
            float[] max = Coordinates(envelope.GetType().GetProperty("max").GetValue(envelope));
            bool contains = true;
            foreach (float[] vertex in Vectors(mesh, "Vertices"))
                for (int axis = 0; axis < 3; axis++) contains &= vertex[axis] >= min[axis] - .0001f && vertex[axis] <= max[axis] + .0001f;
            Check(contains, "cheap pre-build box retains every actual model vertex for rotated/large " + name);
        }
        foreach (float yaw in new[] { -3.14f, -1.57f, 0, 1.57f, 3.14f })
        foreach (float pitch in new[] { -1.1f, -.4f, 0, .4f, 1.2f })
        foreach (float range in new[] { 1f, 8f, 30f, 70f })
        {
            object[] args = { yaw, pitch, range, V(0, 0, 0), 0f };
            Check((bool)ray.Invoke(null, args), "valid free aim produces a ray without any target faction constraint");
            float[] direction = Coordinates(args[3]); float limit = (float)args[4];
            Check(Math.Abs(direction[0] * direction[0] + direction[2] * direction[2] - 1) < .00001,
                "free aim parameter retains native horizontal range units");
            double distance = Math.Sqrt(direction[0] * direction[0] + direction[1] * direction[1] + direction[2] * direction[2]) * limit;
            Check(distance <= 60.0001 && limit <= range && limit > 0, "free aim endpoint bounded by view distance and weapon range");
            Check(Math.Sign(direction[1]) == -Math.Sign(pitch), "positive pitch points downward");
        }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            object[] args = { invalid, 0f, 30f, V(0, 0, 0), 0f };
            Check(!(bool)ray.Invoke(null, args), "invalid free aim yaw rejected");
            args[0] = 0f; args[1] = invalid; Check(!(bool)ray.Invoke(null, args), "invalid free aim pitch rejected");
            args[1] = 0f; args[2] = invalid; Check(!(bool)ray.Invoke(null, args), "invalid free aim range rejected");
        }
        foreach (float range in new[] { -1f, 0f })
            Check(!(bool)ray.Invoke(null, new object[] { 0f, 0f, range, V(0, 0, 0), 0f }), "nonpositive firing range rejected");
        object eye = V(0, 1.55f, 0), directionForward = V(0, 0, 1);
        float nearest = 30;
        foreach (float z in new[] { 12f, 3f, 20f, 8f })
        {
            // Triangles represent a pawn/object behind a wall and the foreground wall.
            object[] args = { eye, directionForward, V(-1, 0, z), V(1, 0, z), V(0, 3, z), 0f };
            Check((bool)triangle.Invoke(null, args), "crosshair intersects actual visible triangle");
            float hit = (float)args[5];
            if ((bool)nearer.Invoke(null, new object[] { hit, nearest })) nearest = hit;
            object swap = args[2]; args[2] = args[3]; args[3] = swap;
            Check((bool)triangle.Invoke(null, args) && Math.Abs((float)args[5] - z) < .00001f, "ray geometry blocks both triangle windings");
        }
        Check(nearest == 3, "nearest foreground geometry wins regardless candidate traversal order");
        Check(!(bool)nearer.Invoke(null, new object[] { 8f, nearest }), "unusable front target cannot be skipped for one behind");
        foreach (float z in new[] { -4f, -1f })
            Check(!(bool)triangle.Invoke(null, new object[] { eye, directionForward, V(-1, 0, z), V(1, 0, z), V(0, 3, z), 0f }), "geometry behind crosshair ray rejected");
        Check(!(bool)triangle.Invoke(null, new object[] { eye, directionForward, V(2, 0, 3), V(3, 0, 3), V(2, 3, 3), 0f }),
            "free aim does not magnetize ray to off-axis object");
        Check(!(bool)triangle.Invoke(null, new object[] { eye, directionForward, V(-1, 0, 3), V(0, 0, 3), V(1, 0, 3), 0f }), "degenerate render triangle rejected");
        Check(!(bool)triangle.Invoke(null, new object[] { eye, directionForward, V(float.NaN, 0, 3), V(1, 0, 3), V(0, 3, 3), 0f }), "nonfinite render triangle rejected");
        // Roof underside and floor/low-object separation use the same slab helper as runtime.
        object[] roof = { V(.5f, 1.55f, .5f), V(0, 1, 1), V(0, 2.43f, 0), V(1, 2.55f, 2), 0f };
        Check((bool)box.Invoke(null, roof) && Math.Abs((float)roof[4] - .88f) < .0001, "roof underside occludes sky ray at rendered height");
        object[] low = { eye, directionForward, V(-.5f, .04f, 2), V(.5f, .7f, 3), 0f };
        Check(!(bool)box.Invoke(null, low), "level eye ray passes above rendered low cover instead of full-cell blocking");
        low[1] = V(0, -.5f, 1);
        Check((bool)box.Invoke(null, low), "downward ray selects low cover surface");
        for (int dx = -31; dx <= 31; dx++) for (int dz = -31; dz <= 31; dz++)
            Check((bool)cellRange.Invoke(null, new object[] { 100, 100, 100 + dx, 100 + dz, 30f }) == (dx * dx + dz * dz <= 900),
                "empty native cell target respects circular weapon range after cell rounding");
    }
}
