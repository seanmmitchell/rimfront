using System;
using System.Collections.Generic;
using System.Reflection;

internal static partial class Program
{
    public enum FixtureMode { AutoFire, BurstFire, SingleFire }
    public sealed class FixtureFireModes
    {
        public FixtureMode CurrentFireMode { get; set; }
        public List<FixtureMode> AvailableFireModes { get; set; } = new List<FixtureMode>();
        public int Calls;
        public bool Throw;
        public void ToggleFireMode()
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("native toggle sentinel");
            CurrentFireMode = AvailableFireModes[(AvailableFireModes.IndexOf(CurrentFireMode) + 1) % AvailableFireModes.Count];
        }
    }
    public sealed class WrongFireModeContract
    {
        public int CurrentFireMode => 0;
        public List<int> AvailableFireModes => new List<int> { 0, 1 };
        public void ToggleFireMode() => throw new Exception("must not be invoked");
    }
    static void TestFireModes(Assembly assembly)
    {
        Type adapter = assembly.GetType("RimFront.NativeFireModes", true);
        Type contract = adapter.GetNestedType("Contract", Flags);
        object Api(Type type) => Activator.CreateInstance(contract, Flags, null, new object[] { type }, null);
        MethodInfo read = contract.GetMethod("TryRead", Flags), cycle = contract.GetMethod("TryCycle", Flags);
        object api = Api(typeof(FixtureFireModes));
        var comp = new FixtureFireModes { CurrentFireMode = FixtureMode.SingleFire,
            AvailableFireModes = new List<FixtureMode> { FixtureMode.SingleFire, FixtureMode.AutoFire, FixtureMode.BurstFire } };
        object[] args = { comp, null, 0 };
        Check((bool)read.Invoke(api, args) && (string)args[1] == "SingleFire" && (int)args[2] == 3,
            "reflection adapter reads current mode and exact native available-mode list");
        Check(comp.Calls == 0 && comp.CurrentFireMode == FixtureMode.SingleFire, "describing fire mode never mutates native state");
        foreach (var expected in new[] { FixtureMode.AutoFire, FixtureMode.BurstFire, FixtureMode.SingleFire })
        {
            int before = comp.Calls; args = new object[] { comp, null };
            Check((bool)cycle.Invoke(api, args) && comp.Calls == before + 1 && comp.CurrentFireMode == expected,
                "cycling invokes native toggle exactly once and follows its weapon-specific mode order");
            Check(((string)args[1]).Contains(expected == FixtureMode.AutoFire ? "Auto" : expected == FixtureMode.BurstFire ? "Burst" : "Semi-auto"),
                "cycle confirmation describes the actual resulting native mode");
        }
        foreach (List<FixtureMode> invalid in new[] {
            new List<FixtureMode>(), null,
            new List<FixtureMode>{FixtureMode.SingleFire},
            new List<FixtureMode>{FixtureMode.SingleFire,FixtureMode.SingleFire},
            new List<FixtureMode>{FixtureMode.SingleFire,(FixtureMode)99},
            new List<FixtureMode>{FixtureMode.AutoFire,FixtureMode.BurstFire} })
        {
            comp.AvailableFireModes = invalid; int before = comp.Calls;
            args = new object[] { comp, null };
            Check(!(bool)cycle.Invoke(api, args) && comp.Calls == before,
                "empty, single-mode, malformed, or inconsistent lists never reach native modulo/index cycling");
        }
        comp.AvailableFireModes = new List<FixtureMode> { FixtureMode.SingleFire, FixtureMode.AutoFire };
        comp.CurrentFireMode = (FixtureMode)99; int calls = comp.Calls;
        Check(!(bool)cycle.Invoke(api, new object[] { comp, null }) && comp.Calls == calls,
            "undefined current native mode fails safely without rewriting the enum");
        comp.CurrentFireMode = FixtureMode.SingleFire; comp.Throw = true;
        Check(!(bool)cycle.Invoke(api, new object[] { comp, null }) && comp.Calls == calls + 1,
            "native toggle exception is reported without retry or duplicate mode changes");
        foreach (object missing in new object[] { null, new object(), new WrongFireModeContract() })
            Check(!(bool)read.Invoke(api, new object[] { missing, null, 0 }), "wrong or absent comp cannot masquerade as supported CE fire modes");
        Check(!(bool)read.Invoke(Api(typeof(WrongFireModeContract)), new object[] { new WrongFireModeContract(), null, 0 }),
            "API drift from enum fire modes to another type is rejected");
        Check(!(bool)read.Invoke(Api(null), new object[] { comp, null, 0 }), "vanilla without CE needs no reflection contract");
        MethodInfo press = adapter.GetMethod("RequiresPress", Flags), label = adapter.GetMethod("Label", Flags);
        foreach (string mode in new[] { "SingleFire", "BurstFire", "AutoFire", "Unknown", null })
            Check((bool)press.Invoke(null, new object[] { mode }) == (mode == "SingleFire" || mode == "BurstFire"),
                "semi-auto and burst require a fresh trigger press; automatic and non-CE fallback retain hold behavior");
        Check((string)label.Invoke(null, new object[] { "SingleFire" }) == "Semi-auto"
            && (string)label.Invoke(null, new object[] { "BurstFire" }) == "Burst"
            && (string)label.Invoke(null, new object[] { "AutoFire" }) == "Auto", "native enum labels are presented clearly");
        MethodInfo start = assembly.GetType("RimFront.NativeTriggerPolicy", true).GetMethod("ShouldStart", Flags);
        MethodInfo cancel = assembly.GetType("RimFront.NativeAttackLifecycle", true).GetMethod("ShouldCancel", Flags);
        foreach (string mode in new[] { "SingleFire", "BurstFire", "AutoFire", "Fixed", "Melee" })
        {
            bool requires = (bool)press.Invoke(null, new object[] { mode });
            Check((bool)start.Invoke(null, new object[] { false, true, requires }), "a short physical click starts one attack in every fire mode");
            Check((bool)start.Invoke(null, new object[] { true, false, requires }) == !requires,
                "holding cannot retrigger semi-auto or another burst after the native attack completes");
            Check(!(bool)start.Invoke(null, new object[] { false, false, requires }), "released trigger without a queued edge never starts another attack");
            Check((bool)cancel.Invoke(null, new object[] { requires, true, false, false, false, 0 }) == !requires,
                "single/burst click completes its selected native attack while automatic fire stops remaining rounds on release");
            foreach (int abort in new[] { 0, 1, 2 })
                Check((bool)cancel.Invoke(null, new object[] { requires, false, abort == 0, abort == 1, abort == 2, 0 }),
                    "movement, reload, and invalid geometry still cancel pending semi-auto/burst attacks before firing");
        }
        MethodInfo allowInput = assembly.GetType("RimFront.NativeTriggerPolicy", true).GetMethod("AllowInput", Flags);
        bool gated = false;
        bool Input(bool held, bool paused)
        {
            object[] input = { held, paused, gated };
            bool accepted = (bool)allowInput.Invoke(null, input); gated = (bool)input[2]; return accepted;
        }
        Check(Input(true, false) && !gated, "a genuine initial press is accepted before pausing");
        Check(!Input(true, true) && gated, "pausing while holding the trigger arms a physical-release requirement");
        Check(!Input(true, false) && gated && !Input(true, false), "unpausing while still held never fabricates a fresh semi-auto/burst press");
        Check(!Input(false, false) && !gated && Input(true, false), "actual release and repress rearm shooting after pause");
        gated = false;
        Check(!Input(true, true) && gated && !Input(false, true) && !gated,
            "a click completed entirely while paused is discarded and release clears the gate");
        Check(!Input(false, false) && Input(true, false), "resuming after a completed paused click waits for a genuinely new press");
        gated = true; // Production stale-input cleanup deliberately arms this gate.
        Check(!Input(true, false) && gated && !Input(true, false), "held trigger after stale input cannot restart firing merely because accepted-input history was cleared");
        Check(!Input(false, false) && !gated && Input(true, false), "physical release and repress restore input after a stale-input stop");
    }
}
