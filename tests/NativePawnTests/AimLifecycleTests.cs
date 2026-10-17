using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    static void TestAimLifecycle(Assembly assembly)
    {
        Type targeting = assembly.GetType("RimFront.NativeAimTargeting", true);
        Type pawns = assembly.GetType("RimFront.NativePawns", true);
        Type animation = assembly.GetType("RimFront.NativePawnAnimation", true);
        Type session = assembly.GetType("RimFront.RimFrontGame", true);
        Check(session.GetField("Active", Flags).GetValue(null) == null,
            "post-exit cache cleanup regression runs with no possessed pawn");
        // Standalone managed sentinels model a bullet querying geometry after view exit.
        // No Map/Pawn constructor or Unity resource is created.
        Type mapType = targeting.GetField("cachedMap", Flags).FieldType;
        object oldMap = RuntimeHelpers.GetUninitializedObject(mapType);
        targeting.GetField("cachedMap", Flags).SetValue(null, oldMap);
        animation.GetField("map", Flags).SetValue(null, oldMap);
        IDictionary geometry = (IDictionary)targeting.GetField("cache", Flags).GetValue(null);
        IDictionary bounds = (IDictionary)pawns.GetField("aimBounds", Flags).GetValue(null);
        IDictionary observations = (IDictionary)animation.GetField("observations", Flags).GetValue(null);
        Seed(geometry); Seed(bounds); Seed(observations);
        Type hook = assembly.GetType("RimFront.ReleaseOnGameChange", true);
        try
        {
            hook.GetMethod("Prefix", Flags).Invoke(null, null);
            Check(geometry.Count == 0 && bounds.Count == 0 && observations.Count == 0,
                "changing save after view exit releases geometry, pawn bounds and animation caches repopulated by in-flight shots");
            Check(targeting.GetField("cachedMap", Flags).GetValue(null) == null
                && animation.GetField("map", Flags).GetValue(null) == null,
                "game-change cleanup releases old map references without requiring another shot or possession");
        }
        finally
        {
            geometry.Clear(); bounds.Clear(); observations.Clear();
            targeting.GetField("cachedMap", Flags).SetValue(null, null);
            animation.GetField("map", Flags).SetValue(null, null);
        }
        void Seed(IDictionary dictionary)
        {
            Type entry = dictionary.GetType().GetGenericArguments()[1];
            dictionary[42] = Activator.CreateInstance(entry, true);
        }
    }
}
