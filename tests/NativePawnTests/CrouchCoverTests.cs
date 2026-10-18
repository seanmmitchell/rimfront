using System;
using System.Collections;
using System.Reflection;

internal static partial class Program
{
    static void TestCrouchCover(Assembly assembly)
    {
        Type stance = assembly.GetType("RimFront.NativeStance", true);
        float height = (float)stance.GetField("CrouchHeight", Flags).GetRawConstantValue();
        float eye = (float)stance.GetField("CrouchEyeHeight", Flags).GetRawConstantValue();
        Check(eye > 0 && eye < height, "crouched camera stays inside body envelope");
        foreach (float standing in new[] { 1.2f, 1.8f, 2.4f })
        foreach (float size in new[] { .6f, 1f, 1.4f })
        {
            float y = (float)stance.GetMethod("VerticalScale", Flags).Invoke(null, new object[] { standing, size, true });
            Check(.04f + standing * y <= height + .0001f, "all humanlike body sizes fit crouched cover envelope");
            Check((float)stance.GetMethod("VerticalScale", Flags).Invoke(null, new object[] { standing, size, false }) == size,
                "standing restores original model scale");
        }
        Type bounds = vector.Assembly.GetType("UnityEngine.Bounds", true);
        object original = Activator.CreateInstance(bounds, V(10, 1.2f, 20), V(.6f, 2.4f, .7f));
        object lowered = stance.GetMethod("CrouchBounds", Flags).Invoke(null, new[] { original });
        float[] min = Coordinates(bounds.GetProperty("min").GetValue(lowered));
        float[] max = Coordinates(bounds.GetProperty("max").GetValue(lowered));
        Check(Math.Abs(min[1]) < .0001f && Math.Abs(max[1] - height) < .0001f, "CE crouched bounds are grounded and capped");
        Check(Math.Abs(min[0] - 9.7f) < .0001f && Math.Abs(max[2] - 20.35f) < .0001f, "crouch preserves horizontal CE dimensions");

        Type objects = assembly.GetType("RimFront.NativeObjectModels", true), kind = objects.GetNestedType("Kind", Flags);
        MethodInfo ray = assembly.GetType("RimFront.NativeAimTargeting", true).GetMethod("RayTriangle", Flags);
        foreach (string cover in new[] { "Barricade", "Sandbags" })
        {
            object mesh = Activator.CreateInstance(builder, true);
            objects.GetMethod("Build", Flags).Invoke(null, new[] { mesh, Enum.Parse(kind, cover), (object)1f, 1f, 1f, Tint(.5f, .5f, .5f), 1 });
            IList vertices = (IList)Get(mesh, "Vertices"); var indices = (System.Collections.Generic.List<int>)Get(mesh, "Triangles");
            foreach (int side in new[] { -1, 1 })
            {
                int covered = 0, total = 0;
                for (int x = 0; x < 12; x++)
                for (int y = 0; y < 40; y++)
                {
                    float sampleX = -.27f + .54f * (x + .5f) / 12;
                    float sampleY = height * (y + .5f) / 40;
                    bool hit = false;
                    for (int t = 0; t < indices.Count; t += 3)
                    {
                        object[] args = { V(sampleX, sampleY, -side * 3f), V(0, 0, side), vertices[indices[t]], vertices[indices[t + 1]], vertices[indices[t + 2]], 0f };
                        if ((bool)ray.Invoke(null, args)) { hit = true; break; }
                    }
                    total++; if (hit) covered++;
                }
                Check((float)covered / total >= .9f, cover + " blocks at least 90% of crouched frontal silhouette from either side");
                bool standingHit = false;
                for (int t = 0; t < indices.Count; t += 3)
                    standingHit |= (bool)ray.Invoke(null, new object[] { V(0, 1.55f, -side * 3f), V(0, 0, side), vertices[indices[t]], vertices[indices[t + 1]], vertices[indices[t + 2]], 0f });
                Check(!standingHit, "standing eye clears " + cover);
            }
        }
        MethodInfo lowCover = assembly.GetType("RimFront.NativeVanillaAim", true).GetMethod("CrouchCover", Flags);
        foreach (string name in new[] { "Barricade", "Sandbags", "Mod_BARRICADE_Steel" })
        {
            Check((bool)lowCover.Invoke(null, new object[] { true, true, name }), "crouched incoming bullet uses actual cover geometry");
            Check(!(bool)lowCover.Invoke(null, new object[] { false, true, name }), "standing keeps native partial-cover rolls");
            Check(!(bool)lowCover.Invoke(null, new object[] { true, false, name }), "cover override requires a building");
        }
        Check(!(bool)lowCover.Invoke(null, new object[] { true, true, "Chair" }), "other partial furniture keeps native interception");
        object settings = Activator.CreateInstance(assembly.GetType("RimFront.RimFrontSettings", true));
        Check((bool)Get(settings, "movingFire"), "moving fire defaults on for new settings");
        Console.WriteLine("PASS: crouched body/CE bounds and >=90% actual barricade/sandbag ray coverage.");
    }
}
