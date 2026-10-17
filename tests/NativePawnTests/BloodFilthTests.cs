using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    static void TestBloodFilth(Assembly assembly)
    {
        Type scene = assembly.GetType("RimFront.NativeScene", true);
        MethodInfo build = scene.GetMethod("BuildBloodSplat", Flags), hash = scene.GetMethod("BloodFilthHash", Flags);
        foreach (int thickness in new[] { -1, 1, 3, 5, 100 })
        foreach (bool smear in new[] { false, true })
        foreach (float size in new[] { .25f, 1f, 1.5f })
        {
            object mesh = Activator.CreateInstance(builder, true);
            object blood = Activator.CreateInstance(color, .7f, .08f, .05f, .7f);
            object[] args = { mesh, V(12.5f, 99f, 8.5f), 90f, 123, thickness, smear, size, blood, Tint(.4f, .5f, .3f) };
            build.Invoke(null, args);
            var points = Vectors(mesh, "Vertices");
            // The shared validator expects a model-local envelope; the actual stain
            // above deliberately uses map coordinates to catch placement regressions.
            object local = Activator.CreateInstance(builder, true);
            IList localVertices = (IList)Get(local, "Vertices"), localColors = (IList)Get(local, "Colors"), localTriangles = (IList)Get(local, "Triangles");
            foreach (float[] point in points) localVertices.Add(V(point[0] - 12.5f, point[1], point[2] - 8.5f));
            foreach (object tint in (IEnumerable)Get(mesh, "Colors")) localColors.Add(tint);
            foreach (object index in (IEnumerable)Get(mesh, "Triangles")) localTriangles.Add(index);
            ValidateMesh(local, "native blood stain");
            Check(points.Count == (Math.Clamp(thickness, 1, 5) + 2) * 24, "blood thickness geometry bounded");
            Check(points.All(p => p[1] >= .0319f && p[1] <= .0351f), "blood stays above floor without vertical clutter");
            Check(points.All(p => Math.Abs(p[0] - 12.5f) < .65f && Math.Abs(p[2] - 8.5f) < .65f), "blood shape footprint bounded");
            for (int i = 0; i < points.Count; i += 3)
            {
                float[] a = points[i], b = points[i + 1], c = points[i + 2];
                Check((b[2] - a[2]) * (c[0] - a[0]) - (b[0] - a[0]) * (c[2] - a[2]) > 0, "blood faces upward");
            }
            object clone = Activator.CreateInstance(builder, true); args[0] = clone; build.Invoke(null, args);
            var repeated = Vectors(clone, "Vertices");
            Check(points.SelectMany(p => p).SequenceEqual(repeated.SelectMany(p => p)), "blood shape stable without gameplay RNG");
            float[] origin = Coordinates(Get(mesh, "Origin"));
            Check(origin.All(v => v == 0), "blood builder restores origin");
            object stain = ((IList)Get(mesh, "Colors"))[0];
            Check((float)Get(stain, "r") > (float)Get(stain, "g"), "native red color retained after terrain blend");
        }
        object green = Activator.CreateInstance(builder, true);
        build.Invoke(null, new object[] { green, V(0, 0, 0), 0f, 9, 2, false, 1f,
            Activator.CreateInstance(color, .1f, .8f, .15f, .7f), Tint(.4f, .4f, .3f) });
        object greenTint = ((IList)Get(green, "Colors"))[0];
        Check((float)Get(greenTint, "g") > (float)Get(greenTint, "r"), "modded green blood is not forced red");

        Type filthType = hash.GetParameters()[0].ParameterType;
        object filth = RuntimeHelpers.GetUninitializedObject(filthType);
        Set(filth, "thickness", 3);
        int thick = (int)hash.Invoke(null, new[] { filth });
        Set(filth, "thickness", 2);
        Check((int)hash.Invoke(null, new[] { filth }) != thick, "native cleaning thickness invalidates stain mesh");
        Type instancesType = filthType.GetField("drawInstances", Flags).FieldType;
        IList instances = (IList)Activator.CreateInstance(instancesType);
        Type instanceType = instancesType.GetGenericArguments()[0];
        object instance = Activator.CreateInstance(instanceType);
        Set(instance, "drawPos", V(12, 0, 8)); Set(instance, "rotation", 0f);
        instances.Add(instance); Set(filth, "drawInstances", instances);
        int before = (int)hash.Invoke(null, new[] { filth });
        Set(instance, "rotation", 90f); instances[0] = instance;
        Check((int)hash.Invoke(null, new[] { filth }) != before, "native smear rotation invalidates stain mesh");
        before = (int)hash.Invoke(null, new[] { filth });
        Set(instance, "drawPos", V(12.2f, 0, 8)); instances[0] = instance;
        Check((int)hash.Invoke(null, new[] { filth }) != before, "native smear placement invalidates stain mesh");
        int limit = (int)scene.GetField("MaximumBloodInstances", Flags).GetRawConstantValue();
        while (instances.Count < limit) instances.Add(instance);
        before = (int)hash.Invoke(null, new[] { filth });
        Set(instance, "rotation", 45f); instances.Add(instance);
        Check((int)hash.Invoke(null, new[] { filth }) == before, "unrendered excess instances do not churn hashes");
        Check((int)scene.GetField("MaximumBloodSplatsPerChunk", Flags).GetRawConstantValue() <= 128, "blood per-chunk geometry budget bounded");
    }
}
