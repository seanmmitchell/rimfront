using System;
using System.Linq;
using RimFront;
using Verse;

static class Program
{
    static int scenarios, failures;
    static void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
    static InputCommand Command(bool fire = false) => new InputCommand { action = "input", fire = fire };
    static void Test(string name, Action action)
    {
        scenarios++;
        try { action(); Console.WriteLine("{\"scenario\":\"" + name + "\",\"passed\":true}"); }
        catch (Exception error) { failures++; Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { scenario = name, passed = false, error = error.Message })); }
    }
    static int Main()
    {
        Test("claim-first-command-wins", () => {
            var index = new PossessionIndex(); var first = index.Claim(1, 5, 2, 30, true);
            Require(first != null && index.Claim(2, 5, 2, 30, true) == null && index.Pawn(5).Owner == 1, "competing claim stole pawn");
        });
        Test("failed-switch-retains-old-possession", () => {
            var index = new PossessionIndex(); var old = index.Claim(1, 5, 2, 0, true); index.Claim(2, 6, 2, 0, true);
            Require(index.Claim(1, 6, 2, 1, true) == null && index.Owner(1) == old, "failed transfer released old pawn");
            Require(index.Claim(1, 7, 2, 1, false) == null && index.Owner(1) == old, "ineligible transfer released old pawn");
        });
        Test("switch-atomic-and-generation-monotonic", () => {
            var index = new PossessionIndex(); var old = index.Claim(1, 5, 2, 0, true); var next = index.Claim(1, 6, 3, 1, true);
            Require(index.Pawn(5) == null && next.Generation > old.Generation && index.Owner(1) == next, "switch did not replace exact owner");
            Require(!index.Release(1, 6, old.Generation), "old generation released replacement");
        });
        Test("sender-generation-sequence-validation", () => {
            var lease = new PossessionIndex().Claim(1, 5, 2, 10, true);
            Require(!lease.Accept(2, lease.Generation, 1, 11, Command()), "foreign input accepted");
            Require(!lease.Accept(1, lease.Generation + 1, 1, 11, Command()), "stale session accepted");
            Require(lease.Accept(1, lease.Generation, 2, 11, Command()), "fresh input rejected");
            Require(!lease.Accept(1, lease.Generation, 2, 100, Command()) && !lease.Accept(1, lease.Generation, 1, 100, Command()), "duplicate/reordered input accepted");
            Require(lease.AcceptedTick == 11, "rejected input renewed lease");
        });
        Test("invalid-input-does-not-renew-lease", () => {
            var lease = new PossessionIndex().Claim(1, 5, 2, 0, true);
            foreach (var c in new[] { new InputCommand { action = "input", yaw = float.NaN }, new InputCommand { action = "input", forward = 2 }, new InputCommand { action = "release" } })
                Require(!lease.Accept(1, lease.Generation, 1, 90, c), "invalid input accepted");
            Require(lease.AcceptedTick == 0 && lease.Sequence == 0, "invalid input changed state");
        });
        Test("lease-expires-on-own-map-ticks", () => {
            var lease = new PossessionIndex().Claim(1, 5, 2, 50, true);
            Require(!lease.Expired(169) && lease.Expired(170), "120 tick lease boundary changed");
        });
        Test("map-controller-order-is-pawn-id", () => {
            var index = new PossessionIndex(); index.Claim(1, 9, 2, 0, true); index.Claim(2, 3, 2, 0, true); index.Claim(3, 1, 4, 0, true);
            Require(index.OnMap(2).Select(x => x.PawnId).SequenceEqual(new[] { 3, 9 }), "map order used insertion or other map");
        });
        Test("restore-keeps-order-lease-and-generation", () => {
            var original = new PossessionIndex(); var lease = original.Claim(1, 9, 2, 40, true); lease.Accept(1, lease.Generation, 7, 49, Command());
            var restored = new PossessionIndex(); restored.Restore(new[] { new ControlLease { Owner = lease.Owner, PawnId = lease.PawnId, MapId = lease.MapId,
                Generation = lease.Generation, Sequence = lease.Sequence, AcceptedTick = lease.AcceptedTick } }, original.NextGeneration);
            Require(restored.Owner(1).Sequence == 7 && restored.Owner(1).AcceptedTick == 49, "snapshot altered input lease");
            Require(restored.Claim(2, 3, 2, 50, true).Generation > lease.Generation, "snapshot reused generation");
        });
        Test("input-pump-caps-continuous-samples", () => {
            var pump = new MultiplayerInputPump(); int sent = 0;
            for (int i = 0; i < 1000; i++) if (pump.ShouldSend(Command(), i / 1000d, false)) sent++;
            Require(sent <= 20 && sent >= 19, "continuous input exceeded 20Hz budget");
        });
        Test("input-pump-preserves-short-click-and-release", () => {
            var pump = new MultiplayerInputPump(); pump.ShouldSend(Command(), 0, false);
            Require(pump.ShouldSend(Command(true), .001, false) && pump.ShouldSend(Command(false), .002, false), "short click coalesced away");
            Require(pump.ShouldSend(new InputCommand { action = "release" }, .003, false), "release throttled");
        });
        Test("input-pump-stale-threshold", () => {
            var pump = new MultiplayerInputPump(); pump.ShouldSend(Command(), 0, false);
            Require(!pump.Stale(.350) && pump.Stale(.351), "350ms producer boundary changed");
        });
        Test("concurrent-production-controllers", () => WithWorlds((state, a, b) => {
            Claim(0, a.Pawn); Claim(1, b.Pawn);
            Require(state.Controllers.Count == 2 && state.Find(a.Pawn) != state.Find(b.Pawn), "controllers overwrite each other");
            Send(0, state.Find(a.Pawn), 1, true); Send(1, state.Find(b.Pawn), 1, false);
            for (int tick = 1; tick <= 8; tick++) Step(state, a, b, tick);
            Require(a.Shots > 0 && b.Shots == 0, "one pawn's trigger affected another pawn");
            Require(!a.Pawn.drafter.FireAtWill && !b.Pawn.drafter.FireAtWill, "possession flags inconsistent");
        }));
        Test("dispatcher-rejects-foreign-input-and-release", () => WithWorlds((state, a, b) => {
            Claim(0, a.Pawn); var c = state.Find(a.Pawn); Send(1, c, 1, true);
            MultiplayerBridge.Sender = 1; RimFrontMultiplayer.End(a.Pawn, c.Lease.Generation);
            Require(c.Lease.Sequence == 0 && state.Controllers.Count == 1, "foreign command altered ownership");
        }));
        Test("same-map-concurrent-pawns-and-release", () => WithWorlds((state, a, b) => {
            b.Pawn.Map = a.Pawn.Map; Claim(0, a.Pawn); Claim(1, b.Pawn);
            var first = state.Find(a.Pawn); var second = state.Find(b.Pawn);
            Send(0, first, 1, true); Send(1, second, 1, true);
            for (int tick = 1; tick <= 8; tick++) Step(state, a, b, tick);
            MultiplayerBridge.Sender = 0; RimFrontMultiplayer.End(a.Pawn, first.Lease.Generation);
            Require(state.Find(a.Pawn) == null && state.Find(b.Pawn) == second && a.Pawn.drafter.FireAtWill
                && !b.Pawn.drafter.FireAtWill && a.Shots > 0 && b.Shots > 0, "same-map controllers shared ownership");
        }));
        Test("host-disconnect-generation-guard", () => WithWorlds((state, a, b) => {
            Claim(1, a.Pawn); var lease = state.Find(a.Pawn).Lease;
            MultiplayerBridge.Sender = 2; RimFrontMultiplayer.Disconnect(1, lease.Generation);
            Require(state.Find(a.Pawn) != null, "non-host disconnected a controller");
            MultiplayerBridge.Sender = 0; RimFrontMultiplayer.Disconnect(1, lease.Generation + 1);
            Require(state.Find(a.Pawn) != null, "stale disconnect released a new generation");
            RimFrontMultiplayer.Disconnect(1, lease.Generation);
            Require(state.Find(a.Pawn) == null && a.Pawn.drafter.FireAtWill, "host disconnect retained possession");
        }));
        Test("pawn-invalidation-releases-control", () => WithWorlds((state, a, b) => {
            Claim(0, a.Pawn); a.Pawn.Downed = true; state.Tick(a.Pawn.Map);
            Require(state.Find(a.Pawn) == null && a.Pawn.drafter.FireAtWill, "downed pawn retained possession");
        }));
        Test("short-click-dispatch-survives-coalescing", () => WithWorlds((state, a, b) => {
            a.Mode = "Single"; Claim(0, a.Pawn); var c = state.Find(a.Pawn);
            Send(0, c, 1, true); Send(0, c, 2, false);
            for (int tick = 1; tick <= 20; tick++) Step(state, a, b, tick);
            Require(a.Shots == 1, "press/release before a tick lost or repeated the shot");
        }));
        Test("lease-release-restores-only-owned-pawn", () => WithWorlds((state, a, b) => {
            Claim(0, a.Pawn); Claim(1, b.Pawn); var c = state.Find(b.Pawn); Send(1, c, 1, false);
            Find.TickManager.TicksGame = 120; state.Tick(a.Pawn.Map);
            Require(state.Find(a.Pawn) == null && state.Find(b.Pawn) == c && a.Pawn.drafter.FireAtWill, "expiry affected another map");
        }));
        Test("paused-click-does-not-fire-after-resume", () => WithWorlds((state, a, b) => {
            Claim(0, a.Pawn); var c = state.Find(a.Pawn); Send(0, c, 1, true, paused: true);
            Send(0, c, 2, true);
            for (int tick = 1; tick <= 8; tick++) Step(state, a, b, tick);
            Require(a.Shots == 0, "paused held trigger became a new click");
            Send(0, c, 3, false); Send(0, c, 4, true);
            for (int tick = 9; tick <= 16; tick++) Step(state, a, b, tick);
            Require(a.Shots > 0, "trigger did not recover after physical release");
        }));
        Test("colony-order-releases-possession", () => WithWorlds((state, a, b) => {
            Claim(0, a.Pawn); a.SetExternalJob("HaulToCell"); var foreign = a.Pawn.CurJob;
            state.Tick(a.Pawn.Map);
            Require(state.Find(a.Pawn) == null && a.Pawn.CurJob == foreign, "external job lost or possession retained");
        }));
        Test("controller-independent-of-local-wall-clock", () => {
            int Run(float clock) { int shots = 0; WithWorlds((state, a, b) => {
                UnityEngine.Time.realtimeSinceStartup = clock; Claim(0, a.Pawn); Send(0, state.Find(a.Pawn), 1, true);
                for (int tick = 1; tick <= 20; tick++) Step(state, a, b, tick);
                shots = a.Shots;
            }); return shots; }
            Require(Run(1) == Run(100000), "local clock changed firing result");
        });
        Test("stale-stop-does-not-create-single-trigger", () => WithWorlds((state, a, b) => {
            a.Mode = "Single"; Claim(0, a.Pawn); var c = state.Find(a.Pawn); Send(0, c, 1, true);
            for (int tick = 1; tick <= 8; tick++) Step(state, a, b, tick);
            int before = a.Shots;
            MultiplayerBridge.Sender = 0; RimFrontMultiplayer.Input(a.Pawn, c.Lease.Generation, 2, 0, 0, 0, 0, 128);
            Send(0, c, 3, true);
            for (int tick = 9; tick <= 20; tick++) Step(state, a, b, tick);
            Require(a.Shots == before, "stale recovery synthesized Semi trigger");
        }));
        Test("discrete-mode-events-consumed-in-order", () => WithWorlds((state, a, b) => {
            Claim(0, a.Pawn); var c = state.Find(a.Pawn); MultiplayerBridge.Sender = 0;
            RimFrontMultiplayer.Input(a.Pawn, c.Lease.Generation, 1, 0, 0, 0, 0, 8);
            RimFrontMultiplayer.Input(a.Pawn, c.Lease.Generation, 2, 0, 0, 0, 0, 8);
            Step(state, a, b, 1); Require(a.Mode == "Single", "first mode action not consumed");
            Step(state, a, b, 2); Require(a.Mode == "Burst", "second mode action coalesced away");
        }));
        foreach (string phase in new[] { "warmup", "burst", "reload" })
            Test("snapshot-roundtrip-" + phase, () => WithWorlds((state, a, b) => {
                Claim(0, a.Pawn); var controller = state.Find(a.Pawn); Send(0, controller, 1, true);
                int until = phase == "warmup" ? 1 : 4;
                for (int tick = 1; tick <= until; tick++) Step(state, a, b, tick);
                if (phase == "reload") a.StartReload();
                var job = a.Pawn.CurJob; long generation = controller.Lease.Generation;
                var snapshot = TestScribe.Save(state);
                var replacement = new RimFrontMultiplayerGame(Current.Game);
                TestScribe.Load(replacement, snapshot);
                var restored = replacement.Find(a.Pawn);
                Require(restored != null && restored.Lease.Generation == generation && restored.Lease.Sequence == 1,
                    "snapshot lost ownership or consumed sequence");
                Require(a.Pawn.CurJob == job && !a.Pawn.drafter.FireAtWill, "snapshot replayed or canceled native job");
                Require(restored.Yaw == controller.Yaw && restored.Crouching == controller.Crouching, "snapshot changed accepted input");
                Require(!restored.Lease.Accept(0, generation, 1, until, Command()), "snapshot replayed consumed input");
                // Native jobs and references were retained by this engine boundary; native Scribe still needs live verification.
            }));
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { summary = true, scenarios, passed = scenarios - failures, failed = failures }));
        return failures == 0 ? 0 : 1;
    }
    static void WithWorlds(Action<RimFrontMultiplayerGame, FiringWorld, FiringWorld> action)
    {
        MultiplayerBridge.InSession = false; Current.Game = null;
        using var a = new FiringWorld(); a.Game.Release("Prepare multiplayer fixture");
        using var b = new FiringWorld(); b.Game.Release("Prepare multiplayer fixture");
        MultiplayerBridge.InSession = true; Current.Game = new Game();
        var state = Current.Game.GetComponent<RimFrontMultiplayerGame>();
        MultiplayerBridge.Sender = 0; RimFrontMultiplayer.SetRules(false, true, true, 0);
        try { action(state, a, b); }
        finally {
            foreach (var c in state.Controllers.ToArray()) state.Remove(c.Lease);
            MultiplayerBridge.Sender = null; MultiplayerBridge.InSession = false; Current.Game = null;
            Require(a.Errors.Count == 0, string.Join(";", a.Errors));
        }
    }
    static void Claim(int owner, Pawn pawn) { MultiplayerBridge.Sender = owner; RimFrontMultiplayer.Claim(pawn); }
    static void Send(int owner, RimFrontGame c, long sequence, bool fire, bool paused = false)
    { MultiplayerBridge.Sender = owner; RimFrontMultiplayer.Input(c.ControlledPawn, c.Lease.Generation, sequence, 0, 0, 0, 0, (fire ? 1 : 0) | (paused ? 64 : 0)); }
    static void Step(RimFrontMultiplayerGame state, FiringWorld a, FiringWorld b, int tick)
    { Find.TickManager.TicksGame = tick; state.Tick(a.Pawn.Map); if (b.Pawn.Map != a.Pawn.Map) state.Tick(b.Pawn.Map); a.NativeTick(); b.NativeTick(); }
}
