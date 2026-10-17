using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

internal static partial class Program
{
    static void TestPawnAimGeometry(Assembly assembly)
    {
        Type pawns = assembly.GetType("RimFront.NativePawns", true);
        Type geometryType = pawns.GetNestedType("Geometry", Flags);
        Type animation = assembly.GetType("RimFront.NativePawnAnimation", true);
        Type snapshot = animation.GetNestedType("Snapshot", Flags);
        Type matrix = vector.Assembly.GetType("UnityEngine.Matrix4x4", true);
        Type quaternion = vector.Assembly.GetType("UnityEngine.Quaternion", true);
        MethodInfo euler = quaternion.GetMethod("Euler", new[] { typeof(float), typeof(float), typeof(float) });
        MethodInfo trs = matrix.GetMethod("TRS", new[] { vector, quaternion, vector });
        MethodInfo point = matrix.GetMethod("MultiplyPoint3x4", new[] { vector });
        MethodInfo capture = pawns.GetMethod("CaptureGeometry", Flags), aim = pawns.GetMethod("TryAimGeometry", Flags);
        MethodInfo partRoot = pawns.GetMethod("PartRoot", Flags);
        object identity = matrix.GetProperty("identity").GetValue(null);
        object MakeRoot(float scale, float yaw) => trs.Invoke(null, new[] { V(2, .04f, -3), euler.Invoke(null, new object[] { 0f, yaw, 0f }), V(scale, scale, scale) });
        Array Parts(object appearance)
        {
            Array result = Array.CreateInstance(geometryType, 7);
            for (int i = 0; i < 7; i++)
            {
                object build = Activator.CreateInstance(builder, true);
                models.GetMethod("BuildPart", Flags).Invoke(null, new[] { build, appearance, Enum.ToObject(part, i) });
                result.SetValue(capture.Invoke(null, new[] { build }), i);
            }
            return result;
        }
        bool Hit(object appearance, object state, object standing, Array parts, bool detailed, object root, object eye, float yaw, float pitch, out double distance)
        {
            object[] args = { appearance, state, standing, parts, detailed, root, eye, yaw, pitch, 0d };
            bool hit = (bool)aim.Invoke(null, args); distance = (double)args[9]; return hit;
        }
        object human = NewAppearance(), aimed = Activator.CreateInstance(snapshot);
        Set(aimed, "Aiming", true);
        object staticHuman = capture.Invoke(null, new[] { Build(human) });
        Array humanParts = Parts(human);
        object handRoot = partRoot.Invoke(null, new[] { human, aimed, Enum.Parse(part, "RightArm"), identity });
        float[] palm = Coordinates(point.Invoke(handRoot, new[] { V(0, -.49f, .025f) }));
        Check(palm[2] > .3f, "aiming palm visibly extends beyond the old standing depth envelope");
        object handEye = V(palm[0] + 3, palm[1], palm[2]);
        Check(Hit(human, aimed, staticHuman, humanParts, true, identity, handEye, -(float)Math.PI / 2, 0, out double handDistance)
            && handDistance < 3, "sideways shot hits visible animated palm outside standing bounds");
        Check(!Hit(human, aimed, staticHuman, humanParts, false, identity, handEye, -(float)Math.PI / 2, 0, out _),
            "same ray misses when actual LOD draws static arms instead of the animated palm");
        Check(!Hit(human, aimed, staticHuman, humanParts, false, identity, V(0, .3f, -3), 0, 0, out _),
            "ray through visible gap between static legs is not swallowed by whole-body bounds");
        Check(!Hit(human, aimed, staticHuman, humanParts, true, identity, V(0, .3f, -3), 0, 0, out _),
            "ray through visible gap between animated legs remains empty");
        object missing = NewAppearance(); Set(missing, "MissingParts", 1 << 3);
        Check(!Hit(missing, aimed, capture.Invoke(null, new[] { Build(missing) }), Parts(missing), true,
            identity, V(palm[0], palm[1], palm[2] + 3), (float)Math.PI, 0, out _),
            "missing right arm does not leave its old palm hit geometry behind");

        // Aim at the exact transformed surface of every available part through a
        // selection of poses, forms, yaw and scale. The first hit may be another
        // nearer part, but cannot miss or extend beyond the known surface.
        foreach (object bodyForm in Enum.GetValues(form))
        {
            object appearance = models.GetMethod("Fallback", Flags).Invoke(null, new[] { bodyForm });
            object standing = capture.Invoke(null, new[] { Build(appearance) });
            Array parts = Parts(appearance);
            foreach (string pose in new[] { "Aiming", "Moving", "Reloading", "Melee", "Lying", "Idle" })
            foreach (float scale in new[] { .5f, 2f })
            foreach (float yaw in new[] { 0f, 90f })
            {
                object state = Activator.CreateInstance(snapshot);
                if (pose == "Melee") { Set(state, "Melee", .8f); Set(state, "MeleeProgress", .65f); }
                else if (pose != "Idle") Set(state, pose, true);
                Set(state, "Phase", 1.2f); Set(state, "Speed", 5f); Set(state, "AimPitch", -20f);
                object root = MakeRoot(scale, yaw);
                for (int i = 0; i < 7; i++)
                {
                    object geometry = parts.GetValue(i);
                    if (geometry == null) continue;
                    Array vertices = (Array)Get(geometry, "Vertices"); int[] triangles = (int[])Get(geometry, "Triangles");
                    object transform = partRoot.Invoke(null, new[] { appearance, state, Enum.ToObject(part, i), root });
                    bool tested = false;
                    for (int tri = 0; tri + 2 < triangles.Length && !tested; tri += 3)
                    {
                        float[] a = Coordinates(point.Invoke(transform, new[] { vertices.GetValue(triangles[tri]) }));
                        float[] b = Coordinates(point.Invoke(transform, new[] { vertices.GetValue(triangles[tri + 1]) }));
                        float[] c = Coordinates(point.Invoke(transform, new[] { vertices.GetValue(triangles[tri + 2]) }));
                        double abx = b[0] - a[0], aby = b[1] - a[1], abz = b[2] - a[2];
                        double acx = c[0] - a[0], acy = c[1] - a[1], acz = c[2] - a[2];
                        double nx = aby * acz - abz * acy, ny = abz * acx - abx * acz, nz = abx * acy - aby * acx;
                        double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (length < .0000001 || Math.Sqrt(nx * nx + nz * nz) / length < .1) continue;
                        nx /= length; ny /= length; nz /= length;
                        float x = (a[0] + b[0] + c[0]) / 3, y = (a[1] + b[1] + c[1]) / 3, z = (a[2] + b[2] + c[2]) / 3;
                        object eye = V(x + (float)nx * 3, y + (float)ny * 3, z + (float)nz * 3);
                        float angle = (float)Math.Atan2(-nx, -nz), pitch = (float)Math.Atan2(ny, Math.Sqrt(nx * nx + nz * nz));
                        double expected = Math.Sqrt(nx * nx + nz * nz) * 3;
                        Check(Hit(appearance, state, standing, parts, true, root, eye, angle, pitch, out double hit)
                            && hit > 0 && hit <= expected + .001,
                            "exact drawn part remains targetable: " + bodyForm + "/" + i + "/" + pose + "/" + scale + "/" + yaw);
                        tested = true;
                    }
                }
            }
        }

        MethodInfo record = pawns.GetMethod("RecordLod", Flags), rendered = pawns.GetMethod("RenderedArticulated", Flags);
        IDictionary lods = (IDictionary)pawns.GetField("articulatedLods", Flags).GetValue(null);
        var saved = new Dictionary<int, int>(); foreach (DictionaryEntry entry in lods) saved[(int)entry.Key] = (int)entry.Value;
        try
        {
            lods.Clear(); record.Invoke(null, new object[] { 1, true, 20 });
            Check((bool)rendered.Invoke(null, new object[] { 1, 20 }) && (bool)rendered.Invoke(null, new object[] { 1, 21 }),
                "aim uses actual detailed LOD before or after the current frame draw");
            for (int id = 2; id < 200; id++) record.Invoke(null, new object[] { id, false, 20 });
            Check((bool)rendered.Invoke(null, new object[] { 1, 20 }), "crowded static pawns do not evict visible detailed LOD decisions");
            record.Invoke(null, new object[] { 1, false, 21 });
            Check(!(bool)rendered.Invoke(null, new object[] { 1, 21 }), "switching to static LOD immediately removes animated hit geometry");
            for (int id = 1; id <= 100; id++) record.Invoke(null, new object[] { id, true, id });
            Check(lods.Count <= 64 && (bool)rendered.Invoke(null, new object[] { 100, 100 })
                && !(bool)rendered.Invoke(null, new object[] { 100, 102 }), "LOD cache is bounded and stale/offscreen decisions expire");
        }
        finally { lods.Clear(); foreach (var entry in saved) lods.Add(entry.Key, entry.Value); }

        Type scene = assembly.GetType("RimFront.NativeScene", true);
        MethodInfo growth = scene.GetMethod("PlantGrowth", Flags);
        float Growth(float value) => (float)growth.Invoke(null, new object[] { value });
        Check(Growth(.5f) == Growth(.62f) && Growth(.625f) != Growth(.62f),
            "rendered plant size and aim cache invalidate at exactly the same growth step");
        foreach (float value in new[] { -1f, 0f, .1f, .4f, 1f, 2f, float.NaN, float.PositiveInfinity })
            Check(Growth(value) >= .15f && Growth(value) <= 1f && !float.IsNaN(Growth(value)), "plant growth geometry step stays finite and bounded");
        Type objects = assembly.GetType("RimFront.NativeObjectModels", true), kindType = objects.GetNestedType("Kind", Flags);
        foreach (string kind in new[] { "Broadleaf", "Cactus", "Mushroom" })
        {
            object a = Activator.CreateInstance(builder, true), b = Activator.CreateInstance(builder, true);
            objects.GetMethod("Build", Flags).Invoke(null, new object[] { a, Enum.Parse(kindType, kind), 1f, 1f, Growth(.5f), Tint(.4f, .5f, .3f), 1 });
            objects.GetMethod("Build", Flags).Invoke(null, new object[] { b, Enum.Parse(kindType, kind), 1f, 1f, Growth(.62f), Tint(.4f, .5f, .3f), 1 });
            List<float[]> va = Vectors(a, "Vertices"), vb = Vectors(b, "Vertices");
            Check(va.Count == vb.Count, "same growth step has stable special plant topology: " + kind);
            for (int i = 0; i < va.Count; i++) for (int axis = 0; axis < 3; axis++)
                Check(va[i][axis] == vb[i][axis], "same growth step has identical special plant rendered/aim vertices: " + kind);
        }
    }
}
