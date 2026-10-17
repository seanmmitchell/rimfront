using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    static void TestShieldSession(Assembly assembly)
    {
        Type effects = assembly.GetType("RimFront.NativeShieldEffects", true);
        MethodInfo begin = effects.GetMethod("BeginViewSession", Flags);
        MethodInfo controller = effects.GetMethod("EnsureHazeController", Flags);
        FieldInfo cacheField = effects.GetField("hazePawns", Flags), impactsField = effects.GetField("impacts", Flags);
        IList cache = (IList)cacheField.GetValue(null);
        Array impacts = (Array)impactsField.GetValue(null), savedImpacts = (Array)impacts.Clone();
        var savedPawns = new List<object>();
        foreach (object pawn in cache) savedPawns.Add(pawn);
        string[] names = { "hazeController", "nextHazeTick", "map", "nextImpact", "sphere", "ring", "material" };
        var saved = new Dictionary<string, object>();
        foreach (string name in names) saved[name] = effects.GetField(name, Flags).GetValue(null);
        try
        {
            // Managed identity sentinels only: no Pawn/Map/Mesh/Material constructors,
            // native draw calls, or game session are started for this lifecycle check.
            Type pawnType = cacheField.FieldType.GetGenericArguments()[0];
            object first = RuntimeHelpers.GetUninitializedObject(pawnType);
            object second = RuntimeHelpers.GetUninitializedObject(pawnType);
            object map = RuntimeHelpers.GetUninitializedObject(effects.GetField("map", Flags).FieldType);
            var resources = new Dictionary<string, object>();
            foreach (string name in new[] { "sphere", "ring", "material" })
            {
                FieldInfo field = effects.GetField(name, Flags);
                resources[name] = RuntimeHelpers.GetUninitializedObject(field.FieldType);
                field.SetValue(null, resources[name]);
            }
            effects.GetField("map", Flags).SetValue(null, map);
            effects.GetField("nextImpact", Flags).SetValue(null, 17);
            object impact = Activator.CreateInstance(impacts.GetType().GetElementType());
            Set(impact, "Pawn", first); Set(impact, "Map", map); Set(impact, "Tick", 12345); Set(impact, "Strength", .8f);
            impacts.SetValue(impact, 0);

            cache.Clear(); cache.Add(first);
            effects.GetField("hazeController", Flags).SetValue(null, first);
            effects.GetField("nextHazeTick", Flags).SetValue(null, 12360);
            begin.Invoke(null, null);
            AssertReset("new paused possession");
            AssertPreserved();

            controller.Invoke(null, new[] { first });
            cache.Add(first);
            effects.GetField("nextHazeTick", Flags).SetValue(null, 12360);
            controller.Invoke(null, new[] { first });
            Check(cache.Count == 1 && (int)effects.GetField("nextHazeTick", Flags).GetValue(null) == 12360,
                "unchanged possession retains the bounded haze scan interval");

            controller.Invoke(null, new[] { second });
            Check(cache.Count == 0 && ReferenceEquals(effects.GetField("hazeController", Flags).GetValue(null), second)
                && (int)effects.GetField("nextHazeTick", Flags).GetValue(null) <= 12345,
                "different controlled pawn invalidates stale spatial candidates without a simulation tick");
            AssertPreserved();

            cache.Add(second);
            effects.GetField("nextHazeTick", Flags).SetValue(null, 12360);
            // Exit/re-enter the same pawn during one paused tick: identity alone is insufficient.
            begin.Invoke(null, null);
            AssertReset("same-pawn paused reentry");
            controller.Invoke(null, new[] { second });
            Check(cache.Count == 0 && (int)effects.GetField("nextHazeTick", Flags).GetValue(null) <= 12345,
                "same-pawn paused reentry requests an immediate fresh haze scan");
            AssertPreserved();

            void AssertReset(string reason)
            {
                Check(cache.Count == 0 && effects.GetField("hazeController", Flags).GetValue(null) == null
                    && (int)effects.GetField("nextHazeTick", Flags).GetValue(null) <= 12345,
                    reason + " clears only the spatial haze cache and refresh deadline");
            }
            void AssertPreserved()
            {
                object retained = impacts.GetValue(0);
                Check(ReferenceEquals(Get(retained, "Pawn"), first) && (int)Get(retained, "Tick") == 12345
                    && (float)Get(retained, "Strength") == .8f && (int)effects.GetField("nextImpact", Flags).GetValue(null) == 17,
                    "haze invalidation preserves in-flight shield impacts and their ring cursor");
                Check(ReferenceEquals(effects.GetField("map", Flags).GetValue(null), map),
                    "haze invalidation preserves the shared current-map event context");
                foreach (var entry in resources)
                    Check(ReferenceEquals(effects.GetField(entry.Key, Flags).GetValue(null), entry.Value),
                        "haze invalidation retains existing GPU resource identity: " + entry.Key);
            }
        }
        finally
        {
            foreach (var entry in saved) effects.GetField(entry.Key, Flags).SetValue(null, entry.Value);
            cache.Clear(); foreach (object pawn in savedPawns) cache.Add(pawn);
            Array.Copy(savedImpacts, impacts, impacts.Length);
        }
    }
}
