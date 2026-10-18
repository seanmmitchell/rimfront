using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    static void TestAimLifecycle(Assembly assembly)
    {
        TestWarmupTargetScope(assembly);
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

    static void TestWarmupTargetScope(Assembly assembly)
    {
        Type manual = assembly.GetType("RimFront.NativeManualAim", true);
        FieldInfo current = manual.GetField("current", Flags);
        object saved = current.GetValue(null);
        Assembly native = Assembly.Load("Assembly-CSharp");
        object pawn = RuntimeHelpers.GetUninitializedObject(native.GetType("Verse.Pawn", true));
        object tracker = RuntimeHelpers.GetUninitializedObject(native.GetType("Verse.Pawn_StanceTracker", true));
        object warmup = RuntimeHelpers.GetUninitializedObject(native.GetType("Verse.Stance_Warmup", true));
        object verb = RuntimeHelpers.GetUninitializedObject(native.GetType("Verse.Verb_Shoot", true));
        object job = RuntimeHelpers.GetUninitializedObject(native.GetType("Verse.AI.Job", true));
        object cell = Activator.CreateInstance(native.GetType("Verse.IntVec3", true), new object[] { 12, 0, 16 });
        object target = Activator.CreateInstance(native.GetType("Verse.LocalTargetInfo", true), new[] { cell });
        object aim = Activator.CreateInstance(current.FieldType, true);
        Type stateType = manual.GetNestedType("CastState", Flags);
        object state = Activator.CreateInstance(stateType, true);
        try
        {
            Set(pawn, "stances", tracker); Set(tracker, "curStance", warmup);
            Set(warmup, "verb", verb); Set(job, "targetA", target);
            Set(aim, "Pawn", pawn); Set(aim, "Job", job); Set(aim, "Verb", verb);
            Set(state, "Context", aim); Set(state, "Target", target); current.SetValue(null, aim);
            manual.GetMethod("CeEndCast", Flags).Invoke(null, new[] { verb, state, null });
            Check(warmup.GetType().GetField("focusTarg", Flags).GetValue(warmup).Equals(target),
                "CE-created additional aiming stance restores the cell target with the verb scope");
        }
        finally { current.SetValue(null, saved); }
    }
}
