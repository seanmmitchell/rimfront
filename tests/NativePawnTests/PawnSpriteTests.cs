using System;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    static void TestPawnSprites(Assembly assembly)
    {
        Type sprites = assembly.GetType("RimFront.NativePawnSprites", true);
        MethodInfo rootMethod = sprites.GetMethod("CardRoot", Flags);
        Type boundsType = vector.Assembly.GetType("UnityEngine.Bounds", true);
        object center = V(2, 1.04f, 3), localCenter = V(.2f, 0, -.3f);
        object bounds = Activator.CreateInstance(boundsType, localCenter, V(1.5f, 0, 1.5f));
        foreach (float yaw in new[] { 0f, 45f, 90f, 180f, 270f, 359f })
        foreach (bool lying in new[] { false, true })
        foreach (float height in new[] { .5f, 1.7f, 3f })
        {
            object root = rootMethod.Invoke(null, new[] { bounds, center, (object)height, yaw, lying });
            MethodInfo point = root.GetType().GetMethod("MultiplyPoint3x4", new[] { vector });
            float[] mappedCenter = Coordinates(point.Invoke(root, new[] { localCenter }));
            Check(Math.Abs(mappedCenter[0] - 2) < .0001f && Math.Abs(mappedCenter[1] - 1.04f) < .0001f
                && Math.Abs(mappedCenter[2] - 3) < .0001f, "sprite remains centered despite native mesh offset, yaw and posture");
            float[] bottom = Coordinates(point.Invoke(root, new[] { V(.2f, 0, -1.05f) }));
            float[] top = Coordinates(point.Invoke(root, new[] { V(.2f, 0, .45f) }));
            double radians = yaw * Math.PI / 180;
            foreach (float[] vertex in new[] { bottom, top })
                Check(Math.Abs((vertex[0] - 2) * Math.Sin(radians) + (vertex[2] - 3) * Math.Cos(radians)) < .0001,
                    "sprite plane faces viewer without tilting with camera pitch");
            if (!lying)
                Check(Math.Abs(bottom[1] - (1.04f - height * .5f)) < .0001f
                    && Math.Abs(top[1] - (1.04f + height * .5f)) < .0001f, "native art bottom and top become upright world height");
            else
                Check(Math.Abs(bottom[1] - 1.04f) < .0001f && Math.Abs(top[1] - 1.04f) < .0001f,
                    "lying sprite head and feet roll sideways at body/bed height");
            // Native +Y is the front of its XZ quad; it must face the viewer after rotation.
            MethodInfo direction = root.GetType().GetMethod("MultiplyVector", new[] { vector });
            float[] normal = Coordinates(direction.Invoke(root, new[] { V(0, 1, 0) }));
            Check(normal[0] * Math.Sin(radians) + normal[2] * Math.Cos(radians) > 0,
                "native mesh front remains visible under backface culling");
        }
        MethodInfo allow = sprites.GetMethod("AllowRefresh", Flags);
        bool Allow(bool dirty, int frame) => (bool)allow.Invoke(null, new object[] { dirty, frame });
        Check(Allow(true, 100) && Allow(true, 100) && !Allow(true, 100), "atlas refresh work is capped at two dirty frames per render frame");
        Check(Allow(false, 100) && !Allow(true, 100), "clean atlas frames remain drawable without resetting the dirty-frame budget");
        Check(Allow(true, 101) && Allow(true, 101) && !Allow(true, 101), "atlas refresh budget resets on the next render frame");
        Type settings = assembly.GetType("RimFront.RimFrontSettings", true);
        Check(!(bool)settings.GetField("spritePawns").GetValue(Activator.CreateInstance(settings)), "existing installs retain default 3D characters");
        Check((bool)settings.GetField("spriteWeapons").GetValue(Activator.CreateInstance(settings)), "3D weapons default on when sprite characters are selected");
        Check((bool)settings.GetField("movingReload").GetValue(Activator.CreateInstance(settings)), "moving reload defaults on for existing and new installs");
        MethodInfo weaponGate = sprites.GetMethod("DrawWeaponEnabled", Flags);
        foreach (bool enabled in new[] { false, true }) foreach (bool corpse in new[] { false, true }) foreach (bool lying in new[] { false, true })
            Check((bool)weaponGate.Invoke(null, new object[] { enabled, corpse, lying }) == (enabled && !corpse && !lying),
                "sprite weapon option never draws a held weapon on a corpse or lying character");
        MethodInfo weaponRefresh = sprites.GetMethod("AllowWeaponRefresh", Flags);
        bool Refresh(int frame) => (bool)weaponRefresh.Invoke(null, new object[] { frame });
        Check(Refresh(100) && Refresh(100) && !Refresh(100), "sprite weapon appearance refreshes stay bounded to two per frame");
        Check(Refresh(101) && Refresh(101) && !Refresh(101), "sprite weapon appearance budget resumes on the next frame");
        MethodInfo update = (MethodInfo)sprites.GetField("UpdateFrame", Flags).GetValue(null);
        Check(update != null, "native directional atlas refresh API resolves on installed game");
        // Bind without invoking a renderer/GPU. This catches incompatible private API signatures.
        Type renderer = update.DeclaringType;
        Type entry = sprites.GetNestedType("Entry", Flags);
        Type signature = entry.GetField("Frame", Flags).FieldType;
        Check(Delegate.CreateDelegate(signature, RuntimeHelpers.GetUninitializedObject(renderer), update) != null,
            "native frame delegate binds without a game session");
    }
}
