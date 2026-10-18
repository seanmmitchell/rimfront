using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimFront
{
    public sealed class RimFrontGame : GameComponent
    {
        internal static RimFrontGame Active;
        Pawn pawn;
        Map controlledMap;
        Job ownedJob;
        bool oldFireAtWill;
        InputCommand input;
        bool pendingReload, pendingFire, pendingBrace, pendingBipod;
        bool pendingFireMode, requireFireRelease, attackRequiresTriggerPress;
        bool suppressNextFireEdge, reportedStaleFire;
        readonly NativeFiringTrace firingTrace = new NativeFiringTrace();
        int attackIdleTicks;
        bool attackHasFired;
        Stance_Busy previousAttackStance;
        int previousAttackTicks, previousShotTick = -1;
        int attackInitialShotTick = -1;
        DateTime acceptedAt;
        string message = "Select a drafted colonist and choose Play in RimFront.";
        string movementMode = "Native path";

        public RimFrontGame(Game game) { }
        public static bool CanControl(Pawn candidate) => candidate != null && candidate.Spawned
            && candidate.Faction == Faction.OfPlayer && candidate.Drafted && !candidate.Dead
            && !candidate.Downed && !candidate.InMentalState && candidate.RaceProps.Humanlike;
        public bool Controls(Pawn candidate) => pawn == candidate;
        internal bool NeedsNativeJobControl
        {
            get
            {
                Job current = pawn?.CurJob;
                return current != null && NativeJobPolicy.RequiresNativeControl(current.def?.defName,
                    current == ownedJob, current.playerForced, current.forceSleep, current.expiryInterval,
                    CombatAdapter.IsSettingUpBipod(pawn));
            }
        }

        public void Control(Pawn candidate)
        {
            if (!CanControl(candidate)) return;
            Active?.Release("Control transferred.");
            pawn = candidate;
            controlledMap = candidate.Map;
            oldFireAtWill = candidate.drafter.FireAtWill;
            input = null;
            pendingBipod = pendingBrace = pendingReload = pendingFire = pendingFireMode = requireFireRelease = false;
            suppressNextFireEdge = reportedStaleFire = false;
            firingTrace.Reset();
            Active = this;
            try
            {
                candidate.drafter.FireAtWill = false;
                candidate.jobs.EndCurrentJob(JobCondition.InterruptForced);
                candidate.pather?.StopDead();
                string fallback = null;
                bool analog = (RimFrontMod.Settings?.analogMovement ?? true) && AnalogMovement.Start(candidate, out fallback);
                movementMode = analog ? "Analog" : "Native path";
                NativeBipods.Start(candidate);
                NativeView.Start(candidate);
                firingTrace.Event(pawn, "control-start", movementMode);
                message = fallback ?? (movementMode + " movement - WASD move, mouse look, LMB fire, R reload, F fire mode, V bipod, G auto bipod, Escape return.");
                if (fallback != null) Log.Warning("[RimFront] " + fallback);
                Log.Message("[RimFront] Native 3D control started on RimWorld " + VersionControl.CurrentVersionString + "; " + CombatAdapter.Mode + "; " + movementMode + " movement.");
            }
            catch (Exception e)
            {
                Log.Error("[RimFront] Native view startup failed: " + e);
                Release("Could not start native view: " + e.GetBaseException().Message);
                Messages.Message(message, MessageTypeDefOf.RejectInput, false);
            }
        }

        public void Release(string reason)
        {
            bool wasActive = pawn != null || Active == this;
            firingTrace.Event(pawn, "control-release", reason);
            try
            {
                if (pawn != null)
                {
                    try { NativeWeaponBrace.Clear(); StopOwnedJob("control release"); }
                    catch (Exception e) { Log.Warning("[RimFront] Native job cleanup: " + e.Message); }
                    if (pawn.drafter != null) pawn.drafter.FireAtWill = oldFireAtWill;
                }
            }
            finally
            {
                NativeBipods.Stop();
                pawn = null;
                controlledMap = null;
                ownedJob = null;
                input = null;
                pendingBipod = pendingBrace = pendingReload = pendingFire = pendingFireMode = requireFireRelease = false;
                suppressNextFireEdge = reportedStaleFire = false;
                firingTrace.Reset();
                message = reason;
                if (Active == this) Active = null;
                try { NativeView.Stop(); }
                catch (Exception e) { Log.Error("[RimFront] Camera restoration failed: " + e); }
                try { AnalogMovement.Stop(); }
                catch (Exception e) { Log.Error("[RimFront] Movement restoration failed: " + e); }
                try { NativeScene.Dispose(); }
                catch (Exception e) { Log.Warning("[RimFront] Scene cleanup: " + e.Message); }
                NativeTargetIndicators.Dispose();
                if (wasActive) Log.Message("[RimFront] " + reason);
            }
        }

        void StopOwnedJob(string reason = "replace owned job", bool startNewJob = true)
        {
            Job stopping = ownedJob;
            firingTrace.Observe(pawn, ownedJob);
            if (stopping != null && NativeFiringTrace.Enabled)
                firingTrace.Event(pawn, "cancel-owned-job", "job=" + stopping.def?.defName + " reason=" + reason);
            CancelOwnedAttack();
            NativeManualAim.Clear(pawn);
            ownedJob = null;
            ResetAttackProgress();
            if (pawn != null && stopping != null && pawn.CurJob == stopping)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: startNewJob);
                pawn.pather?.StopDead();
            }
        }

        void CancelOwnedAttack()
        {
            // Ending a job only cancels warmup; VerbTick can otherwise continue a burst
            // after manual geometry has been cleared. Reset only this exact owned verb.
            if (pawn != null && ownedJob?.def == JobDefOf.AttackStatic && pawn.CurJob == ownedJob)
            {
                Verb verb = ownedJob.verbToUse;
                if (verb != null)
                {
                    bool bursting = verb.Bursting;
                    Stance_Busy busy = pawn.stances?.curStance as Stance_Busy;
                    int cooldown = NativeAttackLifecycle.CancellationCooldown(bursting,
                        busy is Stance_Cooldown && busy.verb == verb ? busy.ticksLeft : 0,
                        bursting ? verb.verbProps.AdjustedCooldownTicks(verb, pawn) : 0);
                    LocalTargetInfo target = ownedJob.targetA;
                    verb.Reset();
                    if (bursting && cooldown > 0)
                        pawn.stances.SetStance(new Stance_Cooldown(cooldown, target, verb));
                }
            }
        }

        internal void BeforeNativeJobCleanup(Pawn_JobTracker tracker, JobCondition condition)
        {
            // Runs before replacement/pooling, while exact ownership is still provable.
            // Never infer ownership later from a new job using the same weapon.
            if (pawn?.jobs != tracker || ownedJob == null || pawn.CurJob != ownedJob) return;
            firingTrace.Observe(pawn, ownedJob);
            if (NativeFiringTrace.Enabled)
                firingTrace.Event(pawn, "native-cleanup", "job=" + ownedJob.def?.defName + " condition=" + condition);
            // Successful native completion has already finished the burst/cooldown.
            // Reset here would erase CE's repeating/short-warmup state every cycle.
            if (NativeAttackLifecycle.ShouldResetOnCleanup(condition == JobCondition.Succeeded,
                ownedJob.verbToUse?.Bursting ?? false)) CancelOwnedAttack();
            NativeManualAim.Clear(pawn);
            ownedJob = null;
            ResetAttackProgress();
        }

        internal void BeforeNativeJobStart(Pawn_JobTracker tracker, Job next, ref bool resumeCurrent)
        {
            if (pawn?.jobs == tracker) firingTrace.RecordJobStart(pawn, next, ownedJob, resumeCurrent);
            if (pawn?.jobs != tracker || ownedJob == null || pawn.CurJob != ownedJob) return;
            if (!NativeJobPolicy.SuppressAttackResume(true, ownedJob.def?.defName, resumeCurrent,
                CombatAdapter.IsEquippedReload(pawn, next))) return;
            // CE normally suspends and queues this exact attack while reloading.
            // Cleanup relinquishes its manual aim, so replaying that queued job would
            // be a foreign attack with stale geometry. Let the reload run unchanged;
            // subsequent fire uses the normal trigger policy and a fresh crosshair ray.
            resumeCurrent = false;
            bool freshInput = input != null && !requireFireRelease && !pendingReload && !pendingFireMode && !pendingBrace && !pendingBipod
                && !Find.TickManager.Paused && !NativeTriggerPolicy.IsStaleInput((DateTime.UtcNow - acceptedAt).TotalMilliseconds)
                && Math.Abs(input.forward) + Math.Abs(input.strafe) < .1f;
            pendingFire |= NativeAttackLifecycle.RetryAfterReload(freshInput, attackHasFired,
                attackInitialShotTick, ownedJob.verbToUse?.LastShotTick ?? attackInitialShotTick);
            if (NativeFiringTrace.Enabled)
                firingTrace.Event(pawn, "reload-suppressed-stale-resume", "freshInput=" + freshInput
                    + " pendingFire=" + pendingFire + " observedShot=" + attackHasFired
                    + " initialShotTick=" + attackInitialShotTick + " LastShotTick=" + ownedJob.verbToUse?.LastShotTick);
        }

        void ResetAttackProgress()
        {
            attackIdleTicks = 0; attackHasFired = attackRequiresTriggerPress = false;
            previousAttackStance = null; previousAttackTicks = 0; previousShotTick = -1;
            attackInitialShotTick = -1;
        }

        public override void GameComponentUpdate()
        {
            // Deferred engine window changes can land after FPS control has already ended.
            NativeWindowIcon.Tick();
            if (!NativeView.Active) NativeShieldEffects.Draw2D();
            if (pawn == null) return;
            if (!CanControl(pawn) || pawn.Map != controlledMap || Find.CurrentMap != controlledMap)
            { Release("Control released: pawn unavailable or map changed."); return; }
            try
            {
                InputCommand command = NativeView.Update();
                if (command == null || command.action == "release")
                { Release("Returned to colony view."); return; }
                if (!command.IsValid()) { Release("Invalid native input; control released."); return; }
                bool physicalFire = NativeView.FireHeld, rawFire = command.fire;
                bool reloadInput = command.reload, modeInput = command.cycleFireMode;
                bool recoveringStaleInput = suppressNextFireEdge;
                // Track physical release even when paused commands mask trigger state.
                command.fire &= NativeTriggerPolicy.AllowInput(physicalFire, Find.TickManager.Paused, ref requireFireRelease);
                bool firePressed = NativeTriggerPolicy.Pressed(command.fire, input?.fire ?? false, ref suppressNextFireEdge);
                // Every engine frame, with no 20 Hz network queue or 10 Hz full-map serialization.
                // Paused clicks must not become surprise actions after unpausing.
                if (Find.TickManager.Paused)
                {
                    command.fire = command.reload = command.cycleFireMode = command.toggleBipod = command.toggleBrace = false;
                    command.forward = command.strafe = 0;
                    pendingBipod = pendingBrace = pendingFire = pendingReload = pendingFireMode = false;
                    AnalogMovement.Halt();
                }
                else
                {
                    pendingReload |= command.reload;
                    pendingFireMode |= command.cycleFireMode;
                    pendingBrace |= command.toggleBrace;
                    pendingBipod |= command.toggleBipod;
                    pendingFire |= firePressed;
                }
                firingTrace.Input(pawn, physicalFire, rawFire, command.fire, pendingFire, requireFireRelease,
                    Find.TickManager.Paused, reloadInput, modeInput, recoveringStaleInput);
                if (recoveringStaleInput && NativeFiringTrace.Enabled)
                    firingTrace.Event(pawn, "stale-input-recovered", "held=" + physicalFire + " accepted=" + command.fire
                        + " pendingFire=" + pendingFire + " releaseGate=" + requireFireRelease);
                input = command;
                firingTrace.Observe(pawn, ownedJob);
                if (!Find.TickManager.Paused) NativeManualAim.Update(pawn, command.yaw, command.pitch);
                acceptedAt = DateTime.UtcNow;
                NativeScene.Draw(controlledMap, pawn, NativeView.Yaw);
            }
            catch (Exception e)
            {
                Log.Error("[RimFront] Native frame failed safely: " + e);
                Release("Native rendering error; returned to colony view.");
            }
        }

        public override void GameComponentOnGUI()
        {
            if (pawn != null) NativeView.OnGUI(message);
        }

        public override void GameComponentTick()
        {
            if (pawn == null || !CanControl(pawn) || input == null) return;
            try
            {
                firingTrace.Observe(pawn, ownedJob);
                double inputAge = (DateTime.UtcNow - acceptedAt).TotalMilliseconds;
                if (NativeTriggerPolicy.IsStaleInput(inputAge))
                {
                    if (!reportedStaleFire && (input.fire || ownedJob?.def == JobDefOf.AttackStatic))
                    {
                        reportedStaleFire = true;
                        Log.Message("[RimFront] Firing interrupted by stale input (" + inputAge.ToString("0")
                            + " ms); awaiting a fresh input sample.");
                    }
                    if (NativeFiringTrace.Enabled)
                        firingTrace.Event(pawn, "stale-input", "ageMs=" + inputAge.ToString("0") + " held=" + input.fire);
                    AnalogMovement.Halt();
                    NativeWeaponBrace.Clear();
                    if (ownedJob?.def == JobDefOf.Goto || ownedJob?.def == JobDefOf.AttackStatic) StopOwnedJob("stale input");
                    input = null;
                    pendingBipod = pendingBrace = pendingReload = pendingFire = pendingFireMode = false;
                    // Clear stale actions, but do not latch off an automatic trigger.
                    // The first new sample restores held state without inventing a click.
                    suppressNextFireEdge = true;
                    return;
                }
                if (NeedsNativeJobControl)
                {
                    firingTrace.ReportUnexpectedJob(pawn, ownedJob, pendingReload);
                    Release("Control released for native job: " + pawn.CurJob.def.defName + ".");
                    return;
                }
                bool movingBipod = Math.Abs(input.forward) + Math.Abs(input.strafe) >= .1f;
                NativeWeaponBrace.Update(pawn, movingBipod);
                if (pendingBipod)
                {
                    // A simultaneous G press takes precedence over V; never replay V next tick.
                    pendingBipod = pendingBrace = false;
                    if (NativeBipods.TryToggle(pawn, out message)) NativeWeaponBrace.Clear();
                    if (movingBipod || pendingReload) pendingFire = false;
                    firingTrace.Event(pawn, "bipod-toggle-result", message);
                    return;
                }
                if (pendingBrace)
                {
                    pendingBrace = false;
                    if (movingBipod) { pendingFire = false; message = "Stop moving before mounting."; return; }
                    StopOwnedJob("bipod toggle", startNewJob: false);
                    requireFireRelease |= input.fire;
                    input.fire = pendingFire = false;
                    AnalogMovement.Halt();
                    NativeWeaponBrace.Toggle(pawn, out message);
                    return;
                }
                if (CombatAdapter.IsSettingUpBipod(pawn))
                {
                    // CE's finite setup Wait toil is not a Stance_Busy. Keep it intact:
                    // replacing it with AttackStatic every tick restarts setup forever.
                    // Reload/click/mode requests stay queued until CE completes the job.
                    AnalogMovement.Halt();
                    message = "Setting up bipod.";
                    return;
                }
                if (pendingFireMode)
                {
                    pendingFireMode = false;
                    if (NativeFireModes.CanCycle(pawn))
                    {
                        if (ownedJob?.def == JobDefOf.AttackStatic) StopOwnedJob("fire mode change");
                        requireFireRelease |= input.fire;
                        input.fire = pendingFire = false;
                        NativeFireModes.TryCycle(pawn, out message);
                    }
                    else message = "This weapon has no alternate fire mode.";
                    firingTrace.Event(pawn, "fire-mode-result", message);
                    // Preserve the feedback this tick; reload and movement resume next tick.
                    return;
                }
                bool moving = Math.Abs(input.forward) + Math.Abs(input.strafe) >= .1f;
                if (moving && (input.fire || pendingFire))
                    firingTrace.Event(pawn, "trigger-blocked-movement", "Fire input present; native firing waits for movement to stop.");
                // Movement cancels queued clicks even while a native reload/busy
                // stance owns the pawn. Do this before that path returns below.
                if (moving) pendingFire = false;
                if (pendingReload) pendingFire = false;
                if (pawn.CurJob == ownedJob && ownedJob?.def == JobDefOf.AttackStatic)
                {
                    Verb verb = ownedJob.verbToUse;
                    Stance_Busy busy = pawn.stances.curStance as Stance_Busy;
                    attackHasFired |= verb?.Bursting == true || (busy is Stance_Cooldown && busy.verb == verb);
                    bool progressing = NativeAttackLifecycle.Progressing(busy != null,
                        busy != previousAttackStance, busy?.ticksLeft ?? 0, previousAttackTicks,
                        verb?.LastShotTick ?? -1, previousShotTick);
                    attackIdleTicks = NativeAttackLifecycle.NextIdleTicks(attackIdleTicks, progressing);
                    previousAttackStance = busy; previousAttackTicks = busy?.ticksLeft ?? 0;
                    previousShotTick = verb?.LastShotTick ?? -1;
                    if (NativeAttackLifecycle.ShouldCancel(input.fire || attackRequiresTriggerPress, attackHasFired, moving, pendingReload,
                        verb == null || !NativeManualAim.CanCast(verb), attackIdleTicks))
                    {
                        bool stalled = attackIdleTicks >= NativeAttackLifecycle.MaximumIdleTicks;
                        if (NativeFiringTrace.Enabled)
                            firingTrace.Event(pawn, "attack-cancel-decision", "moving=" + moving + " reload=" + pendingReload
                                + " held=" + input.fire + " requiresPress=" + attackRequiresTriggerPress
                                + " fired=" + attackHasFired + " idleTicks=" + attackIdleTicks
                                + " reason=" + (stalled ? "stalled" : moving ? "movement" : pendingReload ? "reload"
                                    : !input.fire && !attackRequiresTriggerPress && attackHasFired ? "released after shot" : "invalid native aim/verb"));
                        // R is replacing this attack with a reload. Do not let native
                        // job selection run between cancelling it and asking CE to reload.
                        StopOwnedJob("attack lifecycle cancellation", startNewJob: !pendingReload);
                        pendingFire = false;
                        if (stalled) { message = "Attack could not start; ready."; return; }
                    }
                }
                if (pendingReload && !(pawn.stances.curStance is Stance_Busy))
                {
                    pendingReload = false;
                    if (!CombatAdapter.IsReloading(pawn))
                    {
                        StopOwnedJob("manual reload", startNewJob: false);
                        CombatAdapter.TryReload(pawn, out message);
                        firingTrace.Event(pawn, "reload-result", message);
                    }
                }
                if (CombatAdapter.IsReloading(pawn) || pawn.stances.curStance is Stance_Busy)
                {
                    AnalogMovement.Halt();
                    if (CombatAdapter.IsReloading(pawn)) message = "Reloading.";
                    else if (pawn.stances.curStance is Stance_Busy busy)
                        message = busy.verb?.Bursting == true ? "Firing."
                            : (busy is Stance_Warmup ? "Aiming" : "Weapon cooldown") + " (" + (Math.Max(0, busy.ticksLeft) / 60f).ToString("0.0") + "s).";
                    return;
                }
                if (!moving && NativeTriggerPolicy.ShouldStart(input.fire, pendingFire, NativeFireModes.RequiresTriggerPress(pawn)))
                {
                    AnalogMovement.Halt();
                    if (pawn.CurJob == ownedJob && ownedJob?.def == JobDefOf.AttackStatic) return;
                    pendingFire = false;
                    StopOwnedJob();
                    Job attack = CombatAdapter.CreateAttack(pawn, input.yaw, input.pitch, out message);
                    firingTrace.Event(pawn, attack == null ? "attack-create-failed" : "attack-created", message);
                    if (attack != null) Order(attack);
                    return;
                }
                float forward = input.forward, strafe = input.strafe;
                if (AnalogMovement.Active)
                {
                    AnalogMovement.Tick(input.yaw, forward, strafe);
                    message = AnalogMovement.IsMoving ? "Analog movement." : NativeWeaponBrace.Status(pawn) != "" ? NativeWeaponBrace.Status(pawn) : "Analog - ready.";
                    return;
                }
                if (Math.Abs(forward) + Math.Abs(strafe) < 0.1)
                {
                    if (ownedJob?.def == JobDefOf.Goto) StopOwnedJob();
                    return;
                }
                double x = Math.Sin(input.yaw) * forward + Math.Cos(input.yaw) * strafe;
                double z = Math.Cos(input.yaw) * forward - Math.Sin(input.yaw) * strafe;
                double scale = Math.Max(Math.Abs(x), Math.Abs(z));
                if (scale < 0.01) return;
                int stepX = (int)Math.Round(x / scale), stepZ = (int)Math.Round(z / scale);
                IntVec3 destination = pawn.Position + new IntVec3(stepX, 0, stepZ);
                if (!NativePassable(destination))
                {
                    if (ownedJob?.def == JobDefOf.Goto) StopOwnedJob();
                    return;
                }
                // Keep a short native path ahead instead of completing a job every cell.
                for (int i = 2; i <= 4; i++)
                {
                    IntVec3 next = pawn.Position + new IntVec3(stepX * i, 0, stepZ * i);
                    if (!NativePassable(next)) break;
                    destination = next;
                }
                // Keep the active native path instead of restarting it every frame.
                if (pawn.CurJob == ownedJob && ownedJob?.def == JobDefOf.Goto)
                {
                    IntVec3 existing = ownedJob.targetA.Cell;
                    if (existing != pawn.Position && Math.Sign(existing.x - pawn.Position.x) == stepX && Math.Sign(existing.z - pawn.Position.z) == stepZ
                        && Math.Max(Math.Abs(existing.x - pawn.Position.x), Math.Abs(existing.z - pawn.Position.z)) >= 2) return;
                    ownedJob.targetA = destination;
                    pawn.pather.StartPath(destination, PathEndMode.OnCell);
                    return;
                }
                Job move = JobMaker.MakeJob(JobDefOf.Goto, destination);
                move.playerForced = true;
                Order(move);
                message = "Moving.";
            }
            catch (Exception e)
            {
                Log.Error("[RimFront] Input failed safely: " + e);
                Release("Input error; control released.");
            }
        }

        void Order(Job job)
        {
            Pawn actor = pawn;
            int issuedId = job.loadID;
            string requestedName = job.def?.defName;
            bool requestedAttack = job.def == JobDefOf.AttackStatic;
            Verb requestedVerb = job.verbToUse;
            int initialShot = requestedVerb?.LastShotTick ?? -1;
            ownedJob = job;
            ResetAttackProgress();
            attackRequiresTriggerPress = job?.def == JobDefOf.AttackStatic && NativeFireModes.RequiresTriggerPress(actor);
            attackInitialShotTick = job?.verbToUse?.LastShotTick ?? -1;
            if (NativeFiringTrace.Enabled)
                firingTrace.Event(actor, "order-request", "job=" + job?.def?.defName + " requiresPress=" + attackRequiresTriggerPress
                    + " initialShotTick=" + attackInitialShotTick);
            // Establish this verb's baseline before an immediate native cast can execute.
            firingTrace.Observe(actor, job);
            try { actor.jobs.TryTakeOrderedJob(job); }
            finally
            {
                // Acceptance can mean queued, not started. Manual aim belongs only to
                // the current job; a deferred attack must not survive as a foreign order.
                // Native cleanup pools jobs, so also verify this allocation's load ID.
                bool sameAllocation = job.loadID == issuedId;
                bool started = sameAllocation && actor.CurJob == job;
                bool deferred = sameAllocation && !started && actor.jobs.jobQueue.Contains(job);
                if (deferred)
                    actor.jobs.EndCurrentOrQueuedJob(job, JobCondition.InterruptForced, startNewJob: false);
                if (pawn == actor)
                {
                    ownedJob = started ? job : null;
                    if (deferred && requestedAttack && input != null && !requireFireRelease
                        && !pendingReload && !pendingFireMode && !pendingBrace && !pendingBipod && !Find.TickManager.Paused
                        && !NativeTriggerPolicy.IsStaleInput((DateTime.UtcNow - acceptedAt).TotalMilliseconds)
                        && Math.Abs(input.forward) + Math.Abs(input.strafe) < .1f
                        && (requestedVerb?.LastShotTick ?? initialShot) <= initialShot)
                        pendingFire = true; // Retry this accepted, unfired request with fresh aim.
                }
                if (!started) NativeManualAim.Clear(actor);
                if (NativeFiringTrace.Enabled)
                    firingTrace.Event(actor, "order-result", "requested=" + requestedName + " started=" + started
                        + " removedDeferred=" + deferred
                        + " current=" + actor.CurJob?.def?.defName);
                firingTrace.Observe(actor, ownedJob);
            }
        }
        bool NativePassable(IntVec3 cell)
        {
            if (!cell.InBounds(controlledMap) || cell.Fogged(controlledMap)) return false;
            Building_Door door = cell.GetDoor(controlledMap);
            return door != null ? !door.IsForbiddenToPass(pawn) && (door.Open || door.PawnCanOpen(pawn)) : cell.WalkableBy(controlledMap, pawn);
        }
    }

    internal static class NativeTriggerPolicy
    {
        internal static bool IsStaleInput(double ageMilliseconds) => ageMilliseconds > 350;
        internal static bool Pressed(bool held, bool previouslyHeld, ref bool suppressNextEdge)
        {
            bool pressed = held && !previouslyHeld && !suppressNextEdge;
            suppressNextEdge = false;
            return pressed;
        }
        internal static bool AllowInput(bool physicallyHeld, bool paused, ref bool requireRelease)
        {
            if (paused && physicallyHeld) requireRelease = true;
            if (requireRelease)
            {
                if (!physicallyHeld) requireRelease = false;
                return false;
            }
            return !paused && physicallyHeld;
        }
        internal static bool ShouldStart(bool held, bool pressed, bool requiresPress)
            => pressed || (held && !requiresPress);
    }

    internal static class NativeAttackLifecycle
    {
        internal const int MaximumIdleTicks = 60;
        internal static int NextIdleTicks(int previous, bool progressing)
            => progressing ? 0 : Math.Min(MaximumIdleTicks - 1, Math.Max(0, previous)) + 1;
        internal static bool Progressing(bool busy, bool changedStance, int remaining, int previousRemaining,
            int shotTick, int previousShotTick)
            => (busy && (changedStance || remaining < previousRemaining)) || shotTick > previousShotTick;
        // A short click retains its first warmup/shot. Moving or reloading explicitly
        // cancels it; releasing the trigger cancels any remaining burst after that shot.
        internal static bool ShouldCancel(bool held, bool fired, bool moving, bool reload, bool invalid, int idleTicks)
            => moving || reload || invalid || (!held && fired) || idleTicks >= MaximumIdleTicks;
        internal static int CancellationCooldown(bool bursting, int remaining, int nativeCooldown)
            => Math.Max(0, bursting ? Math.Max(remaining, nativeCooldown) : remaining);
        internal static bool ShouldResetOnCleanup(bool succeeded, bool bursting) => !succeeded || bursting;
        internal static bool RetryAfterReload(bool freshInput, bool observedShot, int initialShotTick, int lastShotTick)
            => freshInput && !observedShot && lastShotTick <= initialShotTick;
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    internal static class PreventStaleOwnedAttackAfterReload
    {
        static void Prefix(Pawn_JobTracker __instance, Job __0, ref bool __3)
            => RimFrontGame.Active?.BeforeNativeJobStart(__instance, __0, ref __3);
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), "CleanupCurrentJob")]
    internal static class CancelOwnedAttackBeforeNativeCleanup
    {
        static void Prefix(Pawn_JobTracker __instance, JobCondition __0) => RimFrontGame.Active?.BeforeNativeJobCleanup(__instance, __0);
    }

    [HarmonyPatch(typeof(Current), nameof(Current.Game), MethodType.Setter)]
    internal static class ReleaseOnGameChange
    {
        static void Prefix()
        {
            RimFrontGame.Active?.Release("Game closed or changed.");
            // In-flight shots can repopulate CPU targeting caches after view release.
            // Clear those even when no pawn is currently possessed.
            NativeManualAim.Clear();
            NativeAimTargeting.Dispose();
            NativePawns.ClearAimCache();
            NativePawnAnimation.Dispose();
            NativeTargetIndicators.Dispose();
            NativeShieldEffects.Dispose();
        }
    }
}
