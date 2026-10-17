using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    static void TestFireDebug(Assembly assembly)
    {
        Type traceType = assembly.GetType("RimFront.NativeFiringTrace", true);
        Type trackerType = traceType.GetNestedType("InputTracker", Flags), gateType = traceType.GetNestedType("Gate", Flags);
        object tracker = Activator.CreateInstance(trackerType, true);
        MethodInfo observe = trackerType.GetMethod("Observe", Flags), resetInput = trackerType.GetMethod("Reset", Flags);
        int PressId() => (int)trackerType.GetProperty("PressId", Flags).GetValue(tracker);
        int Input(bool physical, bool raw = false, bool accepted = false, bool pending = false, bool gated = false,
            bool paused = false, bool reload = false, bool mode = false, bool suppressEdge = false)
            => (int)observe.Invoke(tracker, new object[] { physical, raw, accepted, pending, gated, paused, reload, mode, suppressEdge });
        Check((Input(true, true, true) & 8) != 0 && PressId() == 0,
            "enabling trace mid-hold reports a baseline without inventing a mouse-down or press ID");
        Check(Input(true, true, true) == 0, "unchanged held input emits no repeated transition");
        int masked = Input(true, true, false, gated: true);
        Check(masked == 1 && PressId() == 0, "accepted-input masking is logged separately from physical mouse edges");
        Check(Input(true, false, false, gated: true) == 1, "raw command transition remains visible even if physical and accepted states do not change");
        Check((Input(false) & 4) != 0 && PressId() == 0, "observed mouse-up closes the mid-hold baseline");
        Check((Input(true, true, true, pending: true) & 2) != 0 && PressId() == 1, "actual mouse-down starts a correlated press ID");
        Check(Input(true, true, true) == 1 && PressId() == 1, "consuming the queued press is a state transition rather than another mouse-down");
        foreach (int bit in new[] { 0, 1, 2, 3, 4, 5 })
        {
            int change = Input(true, true, true, pending: bit == 0, gated: bit == 1, paused: bit == 2,
                reload: bit == 3, mode: bit == 4, suppressEdge: bit == 5);
            Check(change == 1 && PressId() == 1, "policy/reload/mode state changes never fabricate physical clicks");
        }
        Input(false);
        Check((Input(true, true, true) & 2) != 0 && PressId() == 2, "separate observed clicks receive distinct correlation IDs");
        resetInput.Invoke(tracker, null);
        Check(PressId() == 0 && (Input(false) & 8) != 0, "reset removes prior press history and reports a new released baseline");
        resetInput.Invoke(tracker, null);
        Check((Input(true, true, true) & 8) != 0 && PressId() == 0, "re-enabling while still held begins a fresh baseline");

        object gate = Activator.CreateInstance(gateType, true);
        MethodInfo accept = gateType.GetMethod("Accept", Flags), resetGate = gateType.GetMethod("Reset", Flags);
        bool Accept(string kind, string detail, double now, out bool notice)
        {
            object[] args = { kind, detail, now, false };
            bool result = (bool)accept.Invoke(gate, args); notice = (bool)args[3]; return result;
        }
        Check(Accept("attack", "blocked", 10, out bool notice) && !notice, "initial action is logged");
        Check(!Accept("attack", "blocked", 10.2, out notice) && !notice, "same failure is deduplicated without a rate-limit warning");
        Check(Accept("attack", "ordered", 10.2, out notice), "changed action outcome is visible immediately");
        Check(Accept("reload", "ordered", 10.2, out notice), "different event categories are not deduplicated together");
        Check(Accept("attack", "ordered", 11.2, out notice), "unchanged outcome may be reported again after the dedup interval");
        resetGate.Invoke(gate, null);
        int budget = (int)gateType.GetField("MaximumPerSecond", Flags).GetRawConstantValue();
        int keyLimit = (int)gateType.GetField("MaximumKeys", Flags).GetRawConstantValue();
        int emitted = 0, notices = 0;
        for (int i = 0; i < budget * 4; i++)
        {
            if (Accept("event-" + i, "state", 20.1, out notice)) emitted++;
            if (notice) notices++;
        }
        Check(emitted == budget && notices == 1, "a noisy paused or stalled second emits only its budget and one suppression notice");
        Check(((IDictionary)gateType.GetField("recent", Flags).GetValue(gate)).Count <= keyLimit,
            "distinct event categories cannot create an unbounded dedup cache");
        Check(Accept("recovered", "state", 21.1, out notice) && !notice, "real-time rollover resumes logging even without simulation ticks");
        resetGate.Invoke(gate, null);
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Check(!Accept("event", "state", invalid, out notice) && !notice, "invalid diagnostic timestamps produce no output");
        Check(!Accept(null, "state", 30, out notice), "missing event category is safely rejected");
        Check(Accept("reset", "same", 30, out notice), "gate still functions after rejected diagnostic data");
        resetGate.Invoke(gate, null);
        Check(Accept("reset", "same", 30, out notice), "explicit session reset clears dedup and rate history");

        // Exercise the real opt-out wrapper with an unusable pawn: disabled logging
        // must reset its own state before trying to inspect game services or equipment.
        Type mod = assembly.GetType("RimFront.RimFrontMod", true);
        FieldInfo settingsField = mod.GetField("Settings", Flags);
        object savedSettings = settingsField.GetValue(null);
        object settings = RuntimeHelpers.GetUninitializedObject(settingsField.FieldType);
        settingsField.FieldType.GetField("traceFiring", Flags).SetValue(settings, false);
        object trace = Activator.CreateInstance(traceType, true);
        Type pawnType = traceType.GetMethod("Event", Flags).GetParameters()[0].ParameterType;
        object pawn = RuntimeHelpers.GetUninitializedObject(pawnType);
        try
        {
            settingsField.SetValue(null, settings);
            traceType.GetField("active", Flags).SetValue(trace, true);
            object ownedTracker = traceType.GetField("inputs", Flags).GetValue(trace);
            observe.Invoke(ownedTracker, new object[] { false, false, false, false, false, false, false, false, false });
            observe.Invoke(ownedTracker, new object[] { true, true, true, true, false, false, false, false, false });
            traceType.GetMethod("Input", Flags).Invoke(trace, new object[] { pawn, true, true, true, true, false, false, false, false, false });
            traceType.GetMethod("Observe", Flags).Invoke(trace, new[] { pawn, null });
            traceType.GetMethod("Event", Flags).Invoke(trace, new object[] { pawn, "must-not-log", "disabled" });
            Check(!(bool)traceType.GetField("active", Flags).GetValue(trace)
                && (int)trackerType.GetProperty("PressId", Flags).GetValue(ownedTracker) == 0,
                "disabled wrapper clears its prior session and does not inspect an unusable pawn");
            Check((int)trackerType.GetField("previous", Flags).GetValue(ownedTracker) == -1,
                "disabled input does not accumulate hidden mouse history");
            settingsField.FieldType.GetField("traceFiring", Flags).SetValue(settings, true);
            traceType.GetMethod("Event", Flags).Invoke(trace, new object[] { null, "no-pawn", "unavailable" });
            Check(!(bool)traceType.GetField("active", Flags).GetValue(trace), "enabled tracing without a pawn remains inactive and cannot affect game control");
        }
        finally { settingsField.SetValue(null, savedSettings); }
    }
}
