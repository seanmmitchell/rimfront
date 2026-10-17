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
        bool pendingReload, pendingFire;
        bool pendingFireMode, requireFireRelease, attackRequiresTriggerPress;
        int attackIdleTicks;
        bool attackHasFired;
        Stance_Busy previousAttackStance;
        int previousAttackTicks, previousShotTick = -1;
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
            pendingReload = pendingFire = pendingFireMode = requireFireRelease = false;
            Active = this;
            try
            {
                candidate.drafter.FireAtWill = false;
                candidate.jobs.EndCurrentJob(JobCondition.InterruptForced);
                candidate.pather?.StopDead();
                string fallback = null;
                bool analog = (RimFrontMod.Settings?.analogMovement ?? true) && AnalogMovement.Start(candidate, out fallback);
                movementMode = analog ? "Analog" : "Native path";
                NativeView.Start(candidate);
                message = fallback ?? (movementMode + " movement - WASD move, mouse look, LMB fire, R reload, F fire mode, Escape return.");
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
            try
            {
                if (pawn != null)
                {
                    try { StopOwnedJob(); }
                    catch (Exception e) { Log.Warning("[RimFront] Native job cleanup: " + e.Message); }
                    if (pawn.drafter != null) pawn.drafter.FireAtWill = oldFireAtWill;
                }
            }
            finally
            {
                pawn = null;
                controlledMap = null;
                ownedJob = null;
                input = null;
                pendingReload = pendingFire = pendingFireMode = requireFireRelease = false;
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

        void StopOwnedJob()
        {
            Job stopping = ownedJob;
            CancelOwnedAttack();
            NativeManualAim.Clear(pawn);
            ownedJob = null;
            ResetAttackProgress();
            if (pawn != null && stopping != null && pawn.CurJob == stopping)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
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
            // Successful native completion has already finished the burst/cooldown.
            // Reset here would erase CE's repeating/short-warmup state every cycle.
            if (NativeAttackLifecycle.ShouldResetOnCleanup(condition == JobCondition.Succeeded,
                ownedJob.verbToUse?.Bursting ?? false)) CancelOwnedAttack();
            NativeManualAim.Clear(pawn);
            ownedJob = null;
            ResetAttackProgress();
        }

        void ResetAttackProgress()
        {
            attackIdleTicks = 0; attackHasFired = attackRequiresTriggerPress = false;
            previousAttackStance = null; previousAttackTicks = 0; previousShotTick = -1;
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
                // Track physical release even when paused commands mask trigger state.
                command.fire &= NativeTriggerPolicy.AllowInput(NativeView.FireHeld, Find.TickManager.Paused, ref requireFireRelease);
                // Every engine frame, with no 20 Hz network queue or 10 Hz full-map serialization.
                // Paused clicks must not become surprise actions after unpausing.
                if (Find.TickManager.Paused)
                {
                    command.fire = command.reload = command.cycleFireMode = false;
                    command.forward = command.strafe = 0;
                    pendingFire = pendingReload = pendingFireMode = false;
                    AnalogMovement.Halt();
                }
                else
                {
                    pendingReload |= command.reload;
                    pendingFireMode |= command.cycleFireMode;
                    pendingFire |= command.fire && !(input?.fire ?? false);
                }
                input = command;
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
                if ((DateTime.UtcNow - acceptedAt).TotalMilliseconds > 350)
                {
                    AnalogMovement.Halt();
                    if (ownedJob?.def == JobDefOf.Goto || ownedJob?.def == JobDefOf.AttackStatic) StopOwnedJob();
                    input = null;
                    pendingReload = pendingFire = pendingFireMode = false;
                    requireFireRelease = true;
                    return;
                }
                if (NeedsNativeJobControl)
                { Release("Control released for native job: " + pawn.CurJob.def.defName + "."); return; }
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
                        if (ownedJob?.def == JobDefOf.AttackStatic) StopOwnedJob();
                        requireFireRelease |= input.fire;
                        input.fire = pendingFire = false;
                        NativeFireModes.TryCycle(pawn, out message);
                    }
                    else message = "This weapon has no alternate fire mode.";
                    // Preserve the feedback this tick; reload and movement resume next tick.
                    return;
                }
                bool moving = Math.Abs(input.forward) + Math.Abs(input.strafe) >= .1f;
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
                        StopOwnedJob();
                        pendingFire = false;
                        if (stalled) { message = "Attack could not start; ready."; return; }
                    }
                }
                if (pendingReload && !(pawn.stances.curStance is Stance_Busy))
                {
                    pendingReload = false;
                    if (!CombatAdapter.IsReloading(pawn))
                    {
                        StopOwnedJob();
                        CombatAdapter.TryReload(pawn, out message);
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
                    if (attack != null) Order(attack);
                    return;
                }
                float forward = input.forward, strafe = input.strafe;
                if (moving) pendingFire = false;
                if (AnalogMovement.Active)
                {
                    AnalogMovement.Tick(input.yaw, forward, strafe);
                    message = AnalogMovement.IsMoving ? "Analog movement." : "Analog - ready.";
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
            ownedJob = job;
            ResetAttackProgress();
            attackRequiresTriggerPress = job?.def == JobDefOf.AttackStatic && NativeFireModes.RequiresTriggerPress(actor);
            try { actor.jobs.TryTakeOrderedJob(job); }
            finally
            {
                if (pawn == actor) ownedJob = actor.CurJob == job ? job : null;
                if (actor.CurJob != job) NativeManualAim.Clear(actor);
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
