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
        TestAutomaticJobHistory(assembly);
    }

    static void TestAutomaticJobHistory(Assembly assembly)
    {
        Type traceType = assembly.GetType("RimFront.NativeFiringTrace", true);
        MethodInfo record = traceType.GetMethod("RecordJobStart", Flags), reset = traceType.GetMethod("Reset", Flags);
        Type pawnType = record.GetParameters()[0].ParameterType, jobType = record.GetParameters()[1].ParameterType;
        Assembly native = pawnType.Assembly;
        Type currentType = native.GetType("Verse.Current", true);
        PropertyInfo currentGame = currentType.GetProperty("Game", Flags);
        object oldGame = currentGame.GetValue(null);
        FieldInfo settingsField = assembly.GetType("RimFront.RimFrontMod", true).GetField("Settings", Flags);
        object oldSettings = settingsField.GetValue(null);
        object settings = RuntimeHelpers.GetUninitializedObject(settingsField.FieldType);
        settingsField.FieldType.GetField("traceFiring", Flags).SetValue(settings, false);
        object trace = Activator.CreateInstance(traceType, true);
        object pawn = RuntimeHelpers.GetUninitializedObject(pawnType);
        object tracker = RuntimeHelpers.GetUninitializedObject(native.GetType("Verse.AI.Pawn_JobTracker", true));
        Set(pawn, "jobs", tracker);
        object NewJob(string name, bool forced)
        {
            object job = RuntimeHelpers.GetUninitializedObject(jobType);
            FieldInfo defField = jobType.GetField("def", Flags);
            object def = RuntimeHelpers.GetUninitializedObject(defField.FieldType);
            Set(def, "defName", name); defField.SetValue(job, def); Set(job, "playerForced", forced);
            return job;
        }
        string[] History() => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Cast<string>(
            (IEnumerable)traceType.GetField("jobTransitions", Flags).GetValue(trace)));
        try
        {
            // No simulation, renderer, or native log sink is started. Find.TickManager
            // only needs Current.Game to exist; its null tick manager yields tick=-1.
            currentGame.SetValue(null, RuntimeHelpers.GetUninitializedObject(currentGame.PropertyType));
            settingsField.SetValue(null, settings);
            object owned = NewJob("AttackStatic", true);
            Set(tracker, "curJob", owned);
            for (int i = 0; i < 20; i++)
                record.Invoke(trace, new[] { pawn, NewJob("Transition" + i, i % 2 == 0), owned, (object)(i % 2 != 0) });
            string[] history = History();
            Check(history.Length == 12 && history[0].Contains("to={Transition8#") && history[11].Contains("to={Transition19#"),
                "automatic history retains exactly the latest twelve actual starts while detailed trace is disabled");
            Check(history[0].Contains("forced=True") && history[0].Contains("resumeCurrent=False")
                && history[11].Contains("resumeCurrent=True") && history[11].Contains("nextIsOwned=False"),
                "transition record preserves forced, resume, and owned-reference facts");
            record.Invoke(trace, new[] { pawn, owned, owned, (object)false });
            Check(History()[11].Contains("nextIsOwned=True"), "restarting the same owned reference is distinguished from a new attack instance");
            MethodInfo label = traceType.GetMethod("JobLabel", Flags);
            string ownedLabel = (string)label.Invoke(null, new[] { owned });
            Check(ownedLabel == (string)label.Invoke(null, new[] { owned })
                && ownedLabel != (string)label.Invoke(null, new[] { NewJob("AttackStatic", true) }),
                "same-definition jobs retain separate diagnostic reference identities");
            Set(owned, "loadID", 123);
            Check(ownedLabel != (string)label.Invoke(null, new[] { owned }),
                "a reused pooled job reference is distinguished by its fresh native load ID");
            string longLabel = (string)label.Invoke(null, new[] { NewJob(new string('x', 10000), false) });
            Check(longLabel.Length < 140, "mod job names cannot inflate the bounded transition ring without limit");
            traceType.GetField("active", Flags).SetValue(trace, true);
            traceType.GetMethod("Event", Flags).Invoke(trace, new[] { pawn, (object)"disabled", "does-not-log" });
            Check(History().Length == 12 && !(bool)traceType.GetField("active", Flags).GetValue(trace),
                "turning off optional tracing preserves always-on transition history");
            record.Invoke(trace, new object[] { null, owned, owned, false });
            Check(History().Length == 12, "missing pawn cannot append a bogus job transition");
            traceType.GetField("unexpectedReported", Flags).SetValue(trace, true);
            reset.Invoke(trace, null);
            Check(History().Length == 0 && !(bool)traceType.GetField("unexpectedReported", Flags).GetValue(trace)
                && (int)traceType.GetField("session", Flags).GetValue(trace) == 0,
                "control-session reset removes prior jobs and re-arms the single unexpected-job report");
            record.Invoke(trace, new[] { pawn, owned, owned, (object)false });
            Check(History().Length == 1 && (int)traceType.GetField("session", Flags).GetValue(trace) > 0,
                "a new disabled-trace session starts with only its own history");
        }
        finally { currentGame.SetValue(null, oldGame); settingsField.SetValue(null, oldSettings); }
    }
}
