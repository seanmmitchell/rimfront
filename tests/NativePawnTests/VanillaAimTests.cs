using System;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    static void TestVanillaAim(Assembly assembly)
    {
        Type aim = assembly.GetType("RimFront.NativeVanillaAim", true);
        MethodInfo adjust = aim.GetMethod("AdjustDestination", Flags), height = aim.GetMethod("Height", Flags), create = aim.GetMethod("Create", Flags);
        MethodInfo shotDestination = aim.GetMethod("ShotDestination", Flags);
        foreach (float offsetX in new[] { -.3f, 0, .3f, 2f })
        foreach (float offsetZ in new[] { -.3f, 0, .3f, -2f })
        {
            float[] destination = Coordinates(adjust.Invoke(null, new[] { V(120.5f + offsetX, 0, 130.5f + offsetZ), V(120.5f, 0, 130.5f), V(120.23f, 4, 130.87f) }));
            Check(Math.Abs(destination[0] - (120.23f + offsetX)) < .00003 && Math.Abs(destination[2] - (130.87f + offsetZ)) < .00003,
                "manual vanilla exact-point correction preserves native miss offsets");
            Check(destination[1] == 0, "native vanilla trajectory remains XZ only");
            float[] direct = Coordinates(shotDestination.Invoke(null, new object[] { V(120.5f + offsetX, 0, 130.5f + offsetZ), V(120.5f, 0, 130.5f), V(120.23f, 4, 130.87f), true }));
            Check(Math.Abs(direct[0] - 120.23f) < .00001 && Math.Abs(direct[2] - 130.87f) < .00001,
                "successful native Thing hit retains its exact selected contact despite cosmetic Launch jitter");
            float[] miss = Coordinates(shotDestination.Invoke(null, new object[] { V(120.5f + offsetX, 0, 130.5f + offsetZ), V(120.5f, 0, 130.5f), V(120.23f, 4, 130.87f), false }));
            Check(Math.Abs(miss[0] - destination[0]) < .00001 && Math.Abs(miss[2] - destination[2]) < .00001,
                "native failed accuracy roll keeps its miss offset");
        }
        foreach (float endY in new[] { .025f, .8f, 1.55f, 8f })
        foreach (float fraction in new[] { -.5f, 0, .2f, .5f, 1f, 1.5f })
        {
            object origin = V(120, 1.55f, 100), destination = V(130, endY, 110);
            float result = (float)height.Invoke(null, new[] { origin, destination, V(120 + 10 * fraction, 99, 100 + 10 * fraction) });
            float expected = 1.55f + (endY - 1.55f) * Math.Clamp(fraction, 0, 1);
            Check(Math.Abs(result - expected) < .00001, "owned vanilla height is finite, clamped, and independent of native altitude");
            Check(create.Invoke(null, new[] { origin, destination }) != null, "ordinary manual shot survives metadata validation");
        }
        float[] large = Coordinates(adjust.Invoke(null, new[] { V(121.5f, 0, 130.5f), V(120.5f, 0, 130.5f), V(123.1f, 1, 129.2f) }));
        Check(Math.Abs(large[0] - 124.1f) < .00003 && Math.Abs(large[2] - 129.2f) < .00003,
            "large Thing contact correction uses anchor cell once and retains native one-cell miss");
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 100001f })
            Check(create.Invoke(null, new[] { V(bad, 1.55f, 0), V(10, 1, 10) }) == null, "invalid serialized vanilla origin is rejected");
        Check(create.Invoke(null, new[] { V(0, 1.55f, 0), V(0, 8, 0) }) == null, "zero horizontal trajectory rejected");
        Check(create.Invoke(null, new[] { V(0, 1.55f, 0), V(0, 8, 129) }) == null, "unbounded serialized trajectory rejected");

        Assembly game = Assembly.Load("Assembly-CSharp");
        Type flagsType = game.GetType("Verse.ProjectileHitFlags", true);
        MethodInfo collisionFlags = aim.GetMethod("CollisionFlags", Flags);
        int worldFlag = Convert.ToInt32(Enum.Parse(flagsType, "NonTargetWorld"));
        for (int original = 0; original < 16; original++)
        {
            int updated = Convert.ToInt32(collisionFlags.Invoke(null, new[] { Enum.ToObject(flagsType, original) }));
            Check((updated & worldFlag) != 0, "owned straight shots retain eligibility for world blockers after LOS bypass");
            Check((updated & ~worldFlag) == (original & ~worldFlag), "world interception does not enable incidental pawn or change intended-hit flags");
        }
        // Reproduce the old short-endpoint miss against a thin object's front face.
        Type targeting = assembly.GetType("RimFront.NativeAimTargeting", true);
        object[] ray = { V(0, 1.5f, 0), V(0, 0, 1), V(-.05f, 1, 10), V(.05f, 1.6f, 10.1f), 0f };
        Check((bool)targeting.GetMethod("RayBox", Flags).Invoke(null, ray), "thin intended object intersects center ray");
        object shortShot = create.Invoke(null, new[] { V(0, 1.5f, 0), V(0, 1.5f, 9.7f) });
        object fixedDestination = shotDestination.Invoke(null, new object[] { V(0, 0, 9.7f), V(0, 0, 10), V(0, 1.5f, 10), true });
        float[] fixedPoint = Coordinates(fixedDestination);
        object fixedShot = create.Invoke(null, new[] { V(0, 1.5f, 0), V(fixedPoint[0], 1.5f, fixedPoint[2]) });
        Check((float)Get(shortShot, "Length") < (float)ray[4] && (float)Get(fixedShot, "Length") + .001f >= (float)ray[4],
            "negative cosmetic jitter no longer makes a successful native Thing hit stop before its surface");
        MethodInfo segment = aim.GetMethod("FinalSegment", Flags), rayBox = targeting.GetMethod("RayBox", Flags);
        foreach (float previousZ in new[] { 0f, 5f, 9.5f, 10f })
        {
            object[] interval = { V(0, 1.5f, 0), V(0, 1.5f, 10), V(0, 99, previousZ), V(0, 99, 10), null, 0f };
            Check((bool)segment.Invoke(null, interval), "final segment supports single, multi-tick, and clamped terminal steps");
            float[] start = Coordinates(interval[4]);
            Check(Math.Abs(start[1] - 1.5f) < .00001 && start[2] <= previousZ && start[2] + (float)interval[5] >= 10,
                "final segment uses manual height and includes the terminal surface");
            object[] endpoint = { interval[4], V(0, 0, 1), V(-.1f, 1, 10), V(.1f, 2, 10.2f), 0f };
            Check((bool)rayBox.Invoke(null, endpoint) && (float)endpoint[4] <= (float)interval[5],
                "target touching final endpoint remains selectable without historical ray acceptance");
            if (previousZ > 5)
            {
                object[] passed = { interval[4], V(0, 0, 1), V(-.3f, 1, 3.7f), V(.3f, 2, 4.3f), 0f };
                Check(!(bool)rayBox.Invoke(null, passed), "pawn moving behind the final flight segment cannot receive a delayed endpoint hit");
                passed[0] = V(0, 1.5f, 0);
                Check((bool)rayBox.Invoke(null, passed), "regression fixture reproduces the old whole-ray false hit");
            }
        }
        object[] descending = { V(0, 1.55f, 0), V(0, .025f, 10), V(0, 99, 8), V(0, 99, 10), null, 0f };
        Check((bool)segment.Invoke(null, descending) && Coordinates(descending[4])[1] < .34f,
            "final descending segment follows manual slope instead of native projectile altitude");
        object[] invalidInterval = { V(0, 1, 0), V(0, 1, 10), V(0, 0, 9), V(0, 0, 8), null, 0f };
        Check(!(bool)segment.Invoke(null, invalidInterval), "backward interval is rejected instead of reusing historical contacts");
        invalidInterval[2] = V(float.NaN, 0, 9);
        Check(!(bool)segment.Invoke(null, invalidInterval), "nonfinite interval is rejected");
        Type definition = game.GetType("Verse.ThingDef", true), properties = game.GetType("Verse.ProjectileProperties", true);
        Type projectile = game.GetType("Verse.Projectile", true), bullet = game.GetType("RimWorld.Bullet", true);
        Check(projectile.GetMethod("CanHit", Flags, null, new[] { game.GetType("Verse.Thing", true) }, null)?.ReturnType == typeof(bool),
            "vanilla collision Harmony target exists with bool result");
        MethodInfo impact = bullet.GetMethod("Impact", Flags, null, new[] { game.GetType("Verse.Thing", true), typeof(bool) }, null);
        Check(impact != null && impact.GetParameters()[0].Name == "hitThing" && impact.GetParameters()[1].Name == "blockedByShield",
            "vanilla impact Harmony argument names match installed metadata");
        Check(projectile.GetMethod("ExposeData", Flags, null, Type.EmptyTypes, null) != null
            && projectile.GetProperty("StartingTicksToImpact", Flags)?.PropertyType == typeof(float),
            "vanilla save and travel timing Harmony contracts exist");
        Check(projectile.GetField("lifetime", Flags)?.FieldType == typeof(int)
            && projectile.GetField("ticksToImpact", Flags)?.FieldType == typeof(int),
            "both native launch duration counters exist for consistent retiming");
        MethodInfo between = projectile.GetMethod("CheckForFreeInterceptBetween", Flags, null, new[] { vector, vector }, null);
        Check(between != null && between.GetParameters()[0].Name == "lastExactPos" && between.GetParameters()[1].Name == "newExactPos"
            && projectile.GetMethod("ImpactSomething", Flags, null, Type.EmptyTypes, null) != null,
            "final-impact segment capture and scope Harmony contracts match native metadata");
        object def = RuntimeHelpers.GetUninitializedObject(definition), props = RuntimeHelpers.GetUninitializedObject(properties);
        Set(def, "thingClass", game.GetType("RimWorld.Bullet", true)); Set(def, "projectile", props);
        MethodInfo supports = aim.GetMethod("Supports", Flags, null, new[] { definition }, null);
        Check((bool)supports.Invoke(null, new[] { def }), "stock straight bullet supports manual trajectory");
        Set(props, "explosionRadius", 1f);
        Check(!(bool)supports.Invoke(null, new[] { def }), "explosive behavior remains native");
        Set(props, "explosionRadius", 0f); Set(props, "arcHeightFactor", .1f);
        Check(!(bool)supports.Invoke(null, new[] { def }), "arced behavior remains native");
        Set(props, "arcHeightFactor", 0f); Set(props, "flyOverhead", true);
        Check(!(bool)supports.Invoke(null, new[] { def }), "overhead behavior remains native");
        Set(props, "flyOverhead", false); Set(def, "thingClass", game.GetType("Verse.Projectile", true));
        Check(!(bool)supports.Invoke(null, new[] { def }), "custom projectile types remain native");
        Check(!(bool)supports.Invoke(null, new object[] { null }), "missing projectile safely unsupported");
    }
}

