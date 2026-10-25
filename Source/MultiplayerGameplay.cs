using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimFront
{
    internal static class RimFrontMultiplayer
    {
        internal static bool InSession => MultiplayerBridge.InSession;
        internal static bool InSimulation => depth > 0;
        [ThreadStatic] static int depth;
        internal static RimFrontMultiplayerGame State => Current.Game?.GetComponent<RimFrontMultiplayerGame>();
        internal static bool RulesAnalog => InSession ? State?.Analog == true : RimFrontMod.Settings?.analogMovement ?? true;
        internal static bool RulesMovingFire => InSession ? State?.MovingFire == true : RimFrontMod.Settings?.movingFire ?? true;
        internal static bool RulesMovingReload => InSession ? State?.MovingReload == true : RimFrontMod.Settings?.movingReload ?? true;
        internal static int ViewStep => InSession ? State?.ViewStep ?? 0 : RimFrontMod.Settings?.extraViewRange ?? 0;
        internal static RimFrontGame Controller(Pawn pawn) => !InSession || pawn == null ? null : State?.Find(pawn);
        internal static RimFrontGame Controller(Pawn_PathFollower follower) => InSession ? State?.Find(follower) : null;
        internal static RimFrontGame Controller(Pawn_JobTracker jobs) => InSession ? State?.Find(jobs) : null;
        internal static void CloseLocalView() => RimFrontMultiplayerGame.CloseLocalView();
        internal static IDisposable Scope(RimFrontGame controller) => new ControllerScope(controller);
        sealed class ControllerScope : IDisposable
        {
            readonly RimFrontGame previous;
            internal ControllerScope(RimFrontGame controller) { previous = RimFrontGame.Active; RimFrontGame.Active = controller; depth++; }
            public void Dispose() { RimFrontGame.Active = previous; depth--; }
        }
        internal static bool CanEnter(out string reason)
        {
            reason = MultiplayerBridge.Failure;
            if (!MultiplayerBridge.Available) return false;
            if (!MultiplayerBridge.SharedColony) { reason = "Separate player factions are not supported by RimFront."; return false; }
            if (State?.RulesReady != true) { reason = "Waiting for the host's RimFront gameplay rules."; return false; }
            return true;
        }
        internal static void RequestClaim(Pawn pawn)
        {
            if (!CanEnter(out string reason)) { Messages.Message(reason, RimWorld.MessageTypeDefOf.RejectInput, false); return; }
            Claim(pawn);
        }
        internal static void RequestRelease(RimFrontGame controller)
        {
            State?.StopView();
            if (MultiplayerBridge.Interface && controller?.Lease != null) End(controller.ControlledPawn, controller.Lease.Generation);
        }

        // Registered with MP. Never accept sender IDs from serialized method parameters.
        internal static void Claim(Pawn pawn)
        {
            if (!MultiplayerBridge.Available || !MultiplayerBridge.SharedColony || !MultiplayerBridge.Sender.HasValue
                || State == null || !RimFrontGame.CanControl(pawn) || !State.RulesReady) return;
            int owner = MultiplayerBridge.Sender.Value;
            var previous = State.Index.Owner(owner);
            var lease = State.Index.Claim(owner, pawn.thingIDNumber, pawn.Map.uniqueID, Tick(pawn.Map), true);
            if (lease == null) return;
            if (previous != null) State.Remove(previous);
            var controller = new RimFrontGame(null) { NetworkControlled = true, Lease = lease };
            State.Controllers.Add(controller);
            using (Scope(controller)) controller.Control(pawn);
            if (controller.ControlledPawn == null) State.Remove(lease);
        }
        internal static void Input(Pawn pawn, long generation, long sequence, float forward, float strafe, float yaw, float pitch, int flags)
        {
            var controller = Controller(pawn);
            if (controller == null || !MultiplayerBridge.Sender.HasValue || (flags & ~255) != 0) return;
            var command = new InputCommand { action = "input", forward = forward, strafe = strafe, yaw = yaw, pitch = pitch,
                fire = (flags & 1) != 0, crouch = (flags & 2) != 0, reload = (flags & 4) != 0,
                cycleFireMode = (flags & 8) != 0, toggleBrace = (flags & 16) != 0, toggleBipod = (flags & 32) != 0 };
            if (!controller.Lease.Accept(MultiplayerBridge.Sender.Value, generation, sequence, Tick(pawn.Map), command)) return;
            using (Scope(controller))
            {
                if ((flags & 128) != 0) controller.AcceptNetworkStop();
                else controller.AcceptNetwork(command, (flags & 64) != 0 || Find.TickManager.Paused);
            }
            if (controller.ControlledPawn == null) State.Remove(controller.Lease);
        }
        internal static void End(Pawn pawn, long generation)
        {
            var controller = Controller(pawn);
            if (controller == null || !MultiplayerBridge.Sender.HasValue) return;
            if (controller.Lease.Owner == MultiplayerBridge.Sender.Value && controller.Lease.Generation == generation)
                State.Remove(controller.Lease);
        }
        internal static void SetRules(bool analog, bool movingFire, bool movingReload, int viewStep)
        {
            if (!MultiplayerBridge.Sender.HasValue || State == null || viewStep < 0 || viewStep > 2) return;
            State.Host = MultiplayerBridge.Sender.Value;
            State.Analog = analog; State.MovingFire = movingFire; State.MovingReload = movingReload;
            State.ViewStep = viewStep; State.RulesReady = true;
        }
        internal static void ResetSession()
        {
            if (State == null || State.Host != MultiplayerBridge.Sender) return;
            foreach (var controller in State.Controllers.ToArray()) State.Remove(controller.Lease);
        }
        internal static void Disconnect(int owner, long generation)
        {
            if (State == null || State.Host != MultiplayerBridge.Sender) return;
            var lease = State.Index.Owner(owner);
            if (lease?.Generation == generation) State.Remove(lease);
        }
        internal static int Tick(Map map) => Find.TickManager.TicksGame; // MP installs the owning map's tick context.
        internal static int Flags(InputCommand c, bool paused) => (c.fire ? 1 : 0) | (c.crouch ? 2 : 0) | (c.reload ? 4 : 0)
            | (c.cycleFireMode ? 8 : 0) | (c.toggleBrace ? 16 : 0) | (c.toggleBipod ? 32 : 0) | (paused ? 64 : 0);
    }

    public sealed class RimFrontMultiplayerGame : GameComponent
    {
        internal readonly List<RimFrontGame> Controllers = new List<RimFrontGame>();
        internal readonly PossessionIndex Index = new PossessionIndex();
        internal bool Analog = true, MovingFire = true, MovingReload = true, RulesReady;
        internal int ViewStep, Host = -1;
        RimFrontGame view;
        static RimFrontMultiplayerGame viewOwner;
        readonly MultiplayerInputPump pump = new MultiplayerInputPump();
        bool requestedRules, suppressedView;
        static WeakReference initializedSession;
        float nextRosterCheck;
        List<SavedController> saved;
        long nextGeneration = 1;
        public RimFrontMultiplayerGame(Game game) { }
        internal RimFrontGame Find(Pawn pawn) => Controllers.Find(c => c.ControlledPawn == pawn);
        internal RimFrontGame Find(Pawn_PathFollower pather) => pather == null ? null : Controllers.Find(c => c.ControlledPawn?.pather == pather);
        internal RimFrontGame Find(Pawn_JobTracker jobs) => jobs == null ? null : Controllers.Find(c => c.ControlledPawn?.jobs == jobs);
        internal void Remove(ControlLease lease)
        {
            if (lease == null) return;
            var controller = Controllers.Find(c => c.Lease == lease);
            if (controller != null)
            {
                using (RimFrontMultiplayer.Scope(controller)) controller.Release("Multiplayer control released.");
                Controllers.Remove(controller);
            }
            Index.Release(lease.Owner, lease.PawnId, lease.Generation);
        }
        internal void StopView()
        {
            if (viewOwner == this) viewOwner = null;
            view = null; suppressedView = true; pump.Reset();
            NativeView.Stop(); NativeScene.Dispose(); NativeTargetIndicators.Dispose();
        }
        internal static void CloseLocalView() { viewOwner?.StopView(); }
        public override void GameComponentUpdate()
        {
            if (!RimFrontMultiplayer.InSession)
            {
                requestedRules = false;
                if (view != null) StopView();
                foreach (var controller in Controllers.ToArray()) Remove(controller.Lease);
                return;
            }
            if (!MultiplayerBridge.Interface) return;
            if (MultiplayerBridge.Hosting)
            {
                if (!requestedRules)
                {
                    requestedRules = true;
                    var settings = RimFrontMod.Settings;
                    RimFrontMultiplayer.SetRules(settings?.analogMovement ?? true, settings?.movingFire ?? true,
                        settings?.movingReload ?? true, ViewRange.Step(settings?.extraViewRange ?? 0));
                    if (!ReferenceEquals(initializedSession?.Target, MultiplayerBridge.SessionToken))
                    {
                        initializedSession = new WeakReference(MultiplayerBridge.SessionToken);
                        RimFrontMultiplayer.ResetSession();
                    }
                }
                if (Time.realtimeSinceStartup >= nextRosterCheck)
                {
                    nextRosterCheck = Time.realtimeSinceStartup + 1;
                    var connected = MultiplayerBridge.ConnectedPlayers();
                    foreach (var controller in Controllers.ToArray())
                        if (!connected.Contains(controller.Lease.Owner) || !Verse.Find.Maps.Contains(controller.ControlledMap))
                            RimFrontMultiplayer.Disconnect(controller.Lease.Owner, controller.Lease.Generation);
                }
            }
            var lease = Index.Owner(MultiplayerBridge.LocalPlayer);
            var local = lease == null ? null : Controllers.Find(c => c.Lease == lease);
            if (view != null && local != view) StopView();
            if (local == null) { suppressedView = false; return; }
            if (view == null && !suppressedView)
            {
                if (local.ControlledMap != Verse.Find.CurrentMap) { RimFrontMultiplayer.RequestRelease(local); return; }
                try { NativeView.Start(local.ControlledPawn); view = local; viewOwner = this; pump.Reset(); pump.Sequence = lease.Sequence; }
                catch (Exception error) { Log.Warning("[RimFront] Multiplayer camera: " + error.Message); RimFrontMultiplayer.RequestRelease(local); return; }
            }
            if (view == null) return;
            try
            {
                double now = Time.realtimeSinceStartup;
                if (pump.Stale(now))
                {
                    RimFrontMultiplayer.Input(view.ControlledPawn, lease.Generation, ++pump.Sequence, 0, 0, view.Yaw, view.Pitch, 128);
                }
                InputCommand command = NativeView.Update();
                if (command == null || command.action == "release") { RimFrontMultiplayer.RequestRelease(view); return; }
                bool paused = MultiplayerBridge.Paused;
                if (paused) command.fire = NativeView.FireHeld;
                if (pump.ShouldSend(command, now, paused))
                    RimFrontMultiplayer.Input(view.ControlledPawn, lease.Generation, ++pump.Sequence,
                        command.forward, command.strafe, command.yaw, command.pitch, RimFrontMultiplayer.Flags(command, paused));
                NativeScene.Draw(view.ControlledMap, view.ControlledPawn, NativeView.Yaw);
            }
            catch (Exception error) { Log.Warning("[RimFront] Multiplayer view: " + error.Message); RimFrontMultiplayer.RequestRelease(local); }
        }
        public override void GameComponentOnGUI() { if (view != null) NativeView.OnGUI(view.Status); }
        internal void Tick(Map map)
        {
            foreach (var lease in Index.OnMap(map.uniqueID))
            {
                var controller = Controllers.Find(c => c.Lease == lease);
                if (controller == null) continue;
                if (lease.Expired(RimFrontMultiplayer.Tick(map))) { Remove(lease); continue; }
                using (RimFrontMultiplayer.Scope(controller))
                {
                    NativeManualAim.Update(controller.ControlledPawn, controller.Yaw, controller.Pitch);
                    controller.GameComponentTick();
                }
                if (controller.ControlledPawn == null) Remove(lease);
            }
        }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref Analog, "rimFrontAnalog", true); Scribe_Values.Look(ref MovingFire, "rimFrontMovingFire", true);
            Scribe_Values.Look(ref MovingReload, "rimFrontMovingReload", true); Scribe_Values.Look(ref ViewStep, "rimFrontViewStep");
            Scribe_Values.Look(ref Host, "rimFrontHost", -1); Scribe_Values.Look(ref RulesReady, "rimFrontRulesReady");
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                nextGeneration = Index.NextGeneration; saved = new List<SavedController>();
                foreach (var c in Controllers) saved.Add(new SavedController { Controller = c });
                saved.Sort((a, b) => a.Controller.Lease.PawnId.CompareTo(b.Controller.Lease.PawnId));
            }
            Scribe_Values.Look(ref nextGeneration, "rimFrontGeneration", 1L);
            Scribe_Collections.Look(ref saved, "rimFrontControllers", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Controllers.Clear(); var leases = new List<ControlLease>();
                foreach (var item in saved ?? new List<SavedController>())
                { Controllers.Add(item.Controller); leases.Add(item.Controller.Lease); }
                Index.Restore(leases, nextGeneration);
                view = null; pump.Reset();
            }
        }
        sealed class SavedController : IExposable
        {
            internal RimFrontGame Controller;
            public SavedController() { }
            public void ExposeData()
            {
                if (Controller == null) Controller = new RimFrontGame(null) { NetworkControlled = true, Lease = new ControlLease() };
                var lease = Controller.Lease;
                Scribe_Values.Look(ref lease.Owner, "owner"); Scribe_Values.Look(ref lease.PawnId, "pawnId");
                Scribe_Values.Look(ref lease.MapId, "mapId"); Scribe_Values.Look(ref lease.Generation, "generation");
                Scribe_Values.Look(ref lease.Sequence, "sequence"); Scribe_Values.Look(ref lease.AcceptedTick, "acceptedTick");
                Controller.ExposeData();
            }
        }
    }

    [HarmonyPatch(typeof(Map), nameof(Map.MapPreTick))]
    internal static class RimFrontMultiplayerMap
    {
        static void Prefix(Map __instance)
        { if (RimFrontMultiplayer.InSession) RimFrontMultiplayer.State?.Tick(__instance); }
    }
}
