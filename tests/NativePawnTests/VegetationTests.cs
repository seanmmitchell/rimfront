using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

internal static partial class Program
{
    static void TestVegetation(Assembly assembly)
    {
        Type vegetation = assembly.GetType("RimFront.NativeVegetation", true);
        Type vector2 = vector.Assembly.GetType("UnityEngine.Vector2", true);
        Type buffers = vegetation.GetNestedType("Buffers", Flags);
        MethodInfo sizeMethod = vegetation.GetMethod("Size", Flags), proxy = vegetation.GetMethod("AppendProxy", Flags), card = vegetation.GetMethod("AddCard", Flags);
        MethodInfo coarse = assembly.GetType("RimFront.NativeAimTargeting", true).GetMethod("CoarseBounds", Flags);
        object queryBounds = coarse.Invoke(null, new object[] { V(0, 0, 0), 1f, 1f, 0f, 1 });
        float[] queryMin = Coordinates(queryBounds.GetType().GetProperty("min").GetValue(queryBounds));
        float[] queryMax = Coordinates(queryBounds.GetType().GetProperty("max").GetValue(queryBounds));
        object V2(float x, float y) => Activator.CreateInstance(vector2, x, y);
        float[] XY(object value) => new[] { (float)Get(value, "x"), (float)Get(value, "y") };
        object Size(bool tree, float growth, float drawX = 1, float drawY = 1, float min = .7f, float max = 2)
            => sizeMethod.Invoke(null, new object[] { tree, growth, V2(drawX, drawY), min, max });
        foreach (bool tree in new[] { false, true })
        foreach (float growth in new[] { -1f, 0f, .15f, .5f, .62f, .625f, 1f, 4f, float.NaN, float.PositiveInfinity })
        foreach (float draw in new[] { -.2f, 0f, .4f, 1f, 3f, 100f, float.NaN, float.PositiveInfinity })
        {
            object size = Size(tree, growth, draw, draw);
            float[] dimensions = XY(size);
            Check(dimensions.All(float.IsFinite) && dimensions[0] > 0 && dimensions[0] <= 3.00001f && dimensions[1] > 0 && dimensions[1] <= 4.10001f,
                "native plant dimensions remain finite and fit declared coarse bounds");
            object mesh = Activator.CreateInstance(builder, true);
            proxy.Invoke(null, new[] { mesh, (object)tree, size });
            ValidateMesh(mesh, "stable vegetation proxy");
            var points = Vectors(mesh, "Vertices");
            Check(points.All(p => Math.Abs(p[0]) <= dimensions[0] / 2 + .0001f && Math.Abs(p[2]) <= dimensions[0] / 2 + .0001f
                && p[1] >= -.0001f && p[1] <= dimensions[1] + .0001f), "proxy stays inside shared sprite width and height");
            Check(points.All(p => Math.Abs(p[0]) <= 1.5001f && Math.Abs(p[2]) <= 1.5001f && p[1] + .04f <= 4.2001f),
                "world proxy fits existing plant coarse query and four-cell neighbor search");
            Check(points.All(p => p[0] >= queryMin[0] && p[0] <= queryMax[0] && p[2] >= queryMin[2] && p[2] <= queryMax[2]
                && p[1] + .04f >= queryMin[1] && p[1] + .04f <= queryMax[1]), "actual native aim broadphase contains every plant collision vertex");
        }
        foreach (bool tree in new[] { false, true })
        {
            Check(XY(Size(tree, .5f)).SequenceEqual(XY(Size(tree, .62f))), "growth within one cached step preserves sprite and proxy dimensions");
            Check(XY(Size(tree, 1f, .5f, .5f, .5f, 1f))[0] < XY(Size(tree, 1f, .5f, .5f, .5f, 2f))[0], "native visual-size range affects plant display size");
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, -1f })
                Check(XY(Size(tree, .7f, 1, 1, bad, bad)).All(float.IsFinite), "invalid native visual range is sanitized");
        }
        object stableSize = Size(true, 1f);
        MethodInfo useArt = vegetation.GetMethod("UseNativeArt", Flags), fallback = vegetation.GetMethod("AddFallback", Flags);
        Check((bool)useArt.Invoke(null, new object[] { true, true, false }), "available native plant texture uses original art");
        foreach (object[] missing in new[] { new object[] { false, false, false }, new object[] { true, false, false }, new object[] { true, true, true } })
            Check(!(bool)useArt.Invoke(null, missing), "missing material, texture or error texture selects explicit fallback");
        foreach (bool tree in new[] { false, true })
        {
            object data = Activator.CreateInstance(buffers, true);
            fallback.Invoke(null, new[] { data, V(0, 0, 0), V(0, 1.55f, 10), (object)0f, stableSize, tree, Tint(1, 1, 1) });
            ValidateMesh(data, "flat missing-art vegetation fallback");
            var points = Vectors(data, "Vertices");
            float[] size = XY(stableSize);
            Check(points.All(p => Math.Abs(p[2]) < .00001f && Math.Abs(p[0]) <= size[0] * .5f + .0001f
                && p[1] >= 0 && p[1] <= size[1] + .0001f), "fallback is a bounded flat silhouette in the same plant envelope");
        }
        object before = Activator.CreateInstance(builder, true);
        proxy.Invoke(null, new[] { before, (object)true, stableSize });
        var original = Vectors(before, "Vertices");
        foreach (float angle in new[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f })
        foreach (float distance in new[] { 0f, 1f, 30f })
        foreach (bool flip in new[] { false, true })
        {
            float radians = angle * (float)Math.PI / 180;
            object data = Activator.CreateInstance(buffers, true);
            object ground = V(2, .04f, 3), observer = V(2 + (float)Math.Sin(radians) * distance, 1.55f, 3 + (float)Math.Cos(radians) * distance);
            card.Invoke(null, new[] { data, ground, observer, (object)radians, stableSize, Tint(.3f, .6f, .2f), flip });
            ValidateMesh(data, "camera-facing plant card");
            var points = Vectors(data, "Vertices");
            var indices = (List<int>)Get(data, "Triangles");
            var uv = ((IEnumerable)Get(data, "Uv")).Cast<object>().Select(XY).ToList();
            float[] dimensions = XY(stableSize);
            Check(points.Count == 4 && indices.Count == 12 && uv.Count == 4, "each plant uses one bounded double-sided card");
            Check(Math.Abs(points.Min(p => p[1]) - .04f) < .0001f && Math.Abs(points.Max(p => p[1]) - (.04f + dimensions[1])) < .0001f,
                "billboard remains grounded and upright regardless camera height or coincidence");
            for (int i = 0; i < points.Count; i++)
            {
                Check(Math.Abs(uv[i][1] - (points[i][1] - .04f) / dimensions[1]) < .0001f, "native texture UV stays bottom-to-top upright");
                Check(uv[i].All(v => v >= 0 && v <= 1), "plant card UV stays in native full texture range");
                double dx = points[i][0] - 2, dz = points[i][2] - 3;
                Check(Math.Abs(dx * dx + dz * dz - dimensions[0] * dimensions[0] / 4) < .0001,
                    "camera facing preserves exact horizontal width instead of scaling with view distance");
                if (distance > 0)
                    Check(Math.Abs(dx * Math.Sin(radians) + dz * Math.Cos(radians)) < .0001, "card plane faces its individual viewer direction");
            }
            bool facing = false, reverse = false;
            for (int i = 0; i < indices.Count; i += 3)
            {
                float[] a = points[indices[i]], b = points[indices[i + 1]], c = points[indices[i + 2]];
                double nx = (b[1] - a[1]) * (c[2] - a[2]) - (b[2] - a[2]) * (c[1] - a[1]);
                double nz = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
                double dot = nx * Math.Sin(radians) + nz * Math.Cos(radians);
                facing |= dot > .00001; reverse |= dot < -.00001;
            }
            Check(facing && reverse, "both billboard faces remain visible with native back-face culling");
            object after = Activator.CreateInstance(builder, true);
            proxy.Invoke(null, new[] { after, (object)true, stableSize });
            var rebuilt = Vectors(after, "Vertices");
            Check(original.Count == rebuilt.Count && original.Zip(rebuilt, (a, b) => a.SequenceEqual(b)).All(equal => equal),
                "turning or moving the observer never mutates ballistic plant geometry");
        }
    }
}

