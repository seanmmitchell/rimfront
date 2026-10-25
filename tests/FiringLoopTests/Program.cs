using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

internal static class Program
{
    static int Main()
    {
        var scenarios = new (string Id, Action Run)[]
        {
            ("vg-mount-to-auto-transition", BraceAutoTransition),
            ("vg-simultaneous-one-action", SimultaneousBipods),
            ("vg-unsupported-auto-preserves-brace", UnsupportedAutoBrace),
            ("vg-pause-clears-both-actions", BothBipodsPaused),
            ("v-preserves-native-reload", BipodReload),
            ("v-ce-setting-and-auto-scope", BipodCeScope),
            ("v-queued-pause-discards", BipodQueuedPause),
            ("v-brace-toggle-movement", BraceMovement),
            ("v-ce-timed-setup-cancel", CeBipodToggle),
            ("v-pause-stale-exit", BipodSafety),
            ("v-weapon-swap-interruption", BipodSwap),
            ("v-trigger-release-gate", BipodFireGate),
            ("held-auto-next-burst", HeldAuto),
            ("successful-repeat-cycle", SuccessfulRepeat),
            ("auto-release-stops-rounds", AutoRelease),
            ("single-held-one-shot", SingleHeld),
            ("burst-held-one-burst", BurstHeld),
            ("short-click-retains-first-shot", ShortClick),
            ("manual-reload-held-auto", ManualReload),
            ("reload-resume-ownership", LastRoundReload),
            ("queued-stale-attack-after-reload", QueuedStaleAttackAfterReload),
            ("queued-stale-attack-after-stale-input", QueuedStaleAttackWithoutInput),
            ("completed-attack-restarts-from-native-queue", CompletedAttackRestarts),
            ("single-last-round-no-phantom", SingleLastRound),
            ("burst-last-round-no-phantom", BurstLastRound),
            ("empty-magazine-reload-recovery", EmptyMagazine),
            ("exhausted-reserve-resupply", ExhaustedReserve),
            ("release-during-reload", ReleaseDuringReload),
            ("pre-shot-reload-retains-click", PreShotReload),
            ("movement-during-reload-cancels-click", MovementDuringReload),
            ("moving-reload-manual-preserves-job", MovingReload),
            ("moving-reload-auto-trigger", MovingAutoReload),
            ("moving-reload-toggle-immediate", MovingReloadToggle),
            ("moving-reload-stale-stun-exit", MovingReloadSafety),
            ("moving-reload-foreign-job", MovingReloadForeign),
            ("stale-auto-recovery", StaleAuto),
            ("stale-single-no-new-click", StaleSingle),
            ("pause-held-release-gate", PauseHeld),
            ("mode-switch-held-release-gate", ModeSwitch),
            ("bipod-setup-held-fire", BipodSetup),
            ("bipod-toggle-cancels-setup", BipodToggle),
            ("bipod-paused-stale-clears-toggle", BipodInputLifetime),
            ("bipod-toggle-during-fire-reload", BipodWhileBusy),
            ("bipod-toggle-foreign-job", BipodForeignJob),
            ("bipod-toggle-movement-cancels-click", BipodMovingClick),
            ("movement-cancels-warmup", Movement),
            ("moving-fire-warmup-burst-auto", MovingFire),
            ("moving-fire-single-burst-trigger", MovingClick),
            ("moving-fire-native-path", MovingNative),
            ("moving-fire-toggle-immediate", MovingToggle),
            ("moving-fire-ownership-stun-stale", MovingSafety),
            ("crouch-speed-and-release", CrouchSpeed),
            ("moving-fire-production-hooks", MovingHooks),
            ("invalid-aim-recovery", InvalidAim),
            ("stalled-attack-recovery", StalledAttack),
            ("negative-warmup-recovers", NegativeWarmup),
            ("cancel-clears-owned-warmup", CancelWarmup),
            ("multiple-ticks-per-frame", MultipleTicks),
            ("fixed-mode-held-fire", FixedHeld),
            ("exit-mid-burst", ExitMidBurst),
            ("external-order-releases-control", ExternalOrder),
            ("deferred-attack-manual-reload", DeferredReload),
            ("deferred-single-click-retries-once", DeferredClick),
            ("deferred-attack-exit-cancels", DeferredExit),
            ("deferred-attack-movement-cancels", DeferredMovement),
            ("deferred-attack-stale-cancels", DeferredStale),
            ("deferred-cancellation-retains-foreign-queue", DeferredForeign),
            ("pooled-request-does-not-own-recycled-job", RecycledRequest),
            ("manual-reload-defers-foreign-queue", ReloadDefersForeign)
        };
        int failed = 0;
        foreach (var scenario in scenarios)
        {
            try
            {
                scenario.Run();
                Console.WriteLine(JsonSerializer.Serialize(new { scenario = scenario.Id, passed = true }));
            }
            catch (Exception error)
            {
                failed++;
                Console.WriteLine(JsonSerializer.Serialize(new { scenario = scenario.Id, passed = false,
                    error = error.GetBaseException().Message }));
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { summary = true, scenarios = scenarios.Length,
            passed = scenarios.Length - failed, failed }));
        return failed == 0 ? 0 : 1;
    }

    static void BraceAutoTransition()
    {
        using var w = Loaded(); var comp = AttachBipod(w);
        w.Frame(brace: true); w.Tick(); w.Frame(); w.Tick(6);
        Require(RimFront.NativeWeaponBrace.IsMounted(w.Pawn), "V mount completed before G");
        w.Frame(bipod: true); w.Tick();
        Require(!w.AutoBipod && !RimFront.NativeWeaponBrace.IsMounted(w.Pawn) && !comp.IsSetUpRn,
            "G OFF clears the manual mount and native deployment");
        w.Frame(bipod: true); w.Tick();
        var hook = typeof(RimFront.NativeWeaponBrace).GetMethod("ShouldSetUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        object[] args = { comp, true };
        Require(w.AutoBipod && (bool)hook.Invoke(null, args), "G ON returns setup decisions to native CE rules");
    }
    static void SimultaneousBipods()
    {
        using var w = Loaded(); var comp = AttachBipod(w);
        w.Frame(brace: true, bipod: true); w.Tick(); w.Frame(); w.Tick(8);
        Require(w.BipodToggles == 1 && comp.DeployCalls == 0 && !RimFront.NativeWeaponBrace.IsMounted(w.Pawn),
            "simultaneous V/G chooses G once and never replays V");
    }
    static void UnsupportedAutoBrace()
    {
        using var w = Loaded(); w.HasBipod = false;
        w.Frame(brace: true); w.Tick();
        w.Frame(bipod: true); w.Tick();
        Require(w.BipodToggles == 0 && RimFront.NativeWeaponBrace.IsMounted(w.Pawn),
            "unsupported G leaves the non-CE V brace intact");
    }
    static void BothBipodsPaused()
    {
        using var w = Loaded();
        w.Frame(brace: true, bipod: true); w.Frame(paused: true); w.Frame(); w.Tick();
        Require(w.BipodToggles == 0 && !RimFront.NativeWeaponBrace.IsMounted(w.Pawn),
            "pause discards both pending controls before unpause");
    }

    static void BipodReload()
    {
        using var w = Loaded(); var comp = AttachBipod(w);
        w.Ammo = 1; w.MagazineSize = 6; w.Reserve = 12;
        w.Frame(brace: true); w.Tick(); w.Frame(); w.Tick(6);
        Require(RimFront.NativeWeaponBrace.IsMounted(w.Pawn), "native bipod completed before reload");
        w.Frame(reload: true); w.Tick();
        var reload = w.Pawn.CurJob;
        Require(reload?.def.defName == "ReloadWeapon", "reload starts while mounted");
        w.Frame(brace: true); w.Tick();
        Require(w.Pawn.CurJob == reload && comp.IsSetUpRn, "V during reload preserves exact native reload and deployment");
        w.Frame(); w.Tick(8);
        Require(w.Ammo == 6 && w.Reserve == 7 && RimFront.NativeWeaponBrace.IsMounted(w.Pawn), "mounted reload completes with native ammo accounting");
    }
    static void BipodCeScope()
    {
        using var w = Loaded(); var comp = AttachBipod(w);
        CombatExtended.Controller.settings.Enabled = false;
        w.Frame(brace: true); w.Tick();
        Require(comp.DeployCalls == 0 && !RimFront.NativeWeaponBrace.IsMounted(w.Pawn), "disabled CE mechanics respected");
        CombatExtended.Controller.settings.Enabled = true;
        w.Frame(brace: true); w.Tick(); w.Frame(brace: true); w.Tick();
        Require(comp.DeployCalls == 1 && comp.CloseCalls == 1, "V cancels setup before completion");
        var hook = typeof(RimFront.NativeWeaponBrace).GetMethod("ShouldSetUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        object[] own = { comp, true };
        Require(!(bool)hook.Invoke(null, own) && !(bool)own[1], "explicit fold suppresses CE auto setup for owned component");
        object[] foreign = { new CombatExtended.BipodComp(), true };
        Require((bool)hook.Invoke(null, foreign) && (bool)foreign[1], "foreign component retains native auto setup");
        w.Game.Release("scope test"); own[1] = true;
        Require((bool)hook.Invoke(null, own), "release restores native auto setup");
    }
    static void BipodQueuedPause()
    {
        using var w = Loaded();
        w.Frame(brace: true); w.Frame(paused: true); w.Frame(); w.Tick();
        Require(!RimFront.NativeWeaponBrace.IsMounted(w.Pawn), "V queued before pause cannot deploy after unpause");
    }
    static void BraceMovement()
    {
        using var w = Loaded();
        w.Frame(brace: true); w.Tick();
        Require(RimFront.NativeWeaponBrace.IsMounted(w.Pawn), "V mounts a non-CE ranged weapon");
        Require(w.Mode == "Auto", "V does not change fire mode");
        w.Frame(brace: true); w.Tick();
        Require(!RimFront.NativeWeaponBrace.IsMounted(w.Pawn), "V folds the brace");
        w.Frame(brace: true); w.Tick();
        w.Frame(forward: 1); w.Tick();
        Require(!RimFront.NativeWeaponBrace.IsMounted(w.Pawn) && RimFront.AnalogMovement.IsMoving, "movement folds and resumes movement");
    }
    static CombatExtended.BipodComp AttachBipod(FiringWorld w)
    {
        var comp = new CombatExtended.BipodComp(); w.Pawn.equipment.Primary.AllComps.Add(comp); return comp;
    }
    static void CeBipodToggle()
    {
        using var w = Loaded(); var comp = AttachBipod(w);
        w.Frame(brace: true); w.Tick();
        Require(comp.DeployCalls == 1 && !comp.IsSetUpRn, "V invokes native deployment without completing setup");
        Require(!RimFront.NativeWeaponBrace.IsMounted(w.Pawn), "setup is not a completed mount");
        w.Frame(); w.Tick(6);
        Require(RimFront.NativeWeaponBrace.IsMounted(w.Pawn), "native timed setup completes");
        w.Frame(brace: true); w.Tick();
        Require(comp.CloseCalls == 1 && !comp.IsSetUpRn, "V calls native fold");
        w.Frame(brace: true); w.Tick();
        w.Frame(forward: 1); w.Tick();
        Require(w.Pawn.CurJob?.def.defName != "JobDef_SetUpBipod" && !comp.IsSetUpRn, "movement cancels pending native setup");
        w.Frame(); w.Tick(8);
        Require(!RimFront.NativeWeaponBrace.IsMounted(w.Pawn), "cancelled setup cannot mount later");
    }
    static void BipodSafety()
    {
        using var w = Loaded();
        w.Frame(paused: true, brace: true); w.Frame(); w.Tick();
        Require(!RimFront.NativeWeaponBrace.IsMounted(w.Pawn), "paused V is discarded");
        w.Frame(brace: true); w.Tick(); w.ExpireInput(); w.Tick();
        Require(!RimFront.NativeWeaponBrace.IsMounted(w.Pawn), "stale input releases mount");
        w.Frame(brace: true); w.Tick(); w.Game.Release("exit test");
        Require(!RimFront.NativeWeaponBrace.IsMounted(w.Pawn), "exit clears brace");
    }
    static void BipodSwap()
    {
        using var w = Loaded(); var comp = AttachBipod(w);
        w.Frame(brace: true); w.Tick();
        w.Pawn.equipment.Primary = new Verse.ThingWithComps(); w.Frame(); w.Tick();
        Require(!RimFront.NativeWeaponBrace.IsMounted(w.Pawn) && comp.CloseCalls == 1, "weapon swap cancels exact old component");
        Require(w.Pawn.CurJob?.def.defName != "JobDef_SetUpBipod", "old setup job ends on weapon swap");
    }
    static void BipodFireGate()
    {
        using var w = Loaded();
        w.Frame(held: true, brace: true); w.Tick(); Run(w, 20);
        Require(w.Shots == 0, "V consumes simultaneous trigger and requires release");
        w.Frame(); w.Tick(); w.Frame(held: true); Run(w, 20);
        Require(w.Shots > 0, "fresh click fires while mounted");
    }

    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    // Match the installed game's ordering: simulation tick(s), then next frame's input.
    // No sleeps and no controller decisions are implemented in this harness.
    static void Run(FiringWorld world, int ticks, bool held = true, bool paused = false,
        float forward = 0)
    {
        for (int i = 0; i < ticks; i++)
        {
            world.Tick();
            world.Frame(held: held, paused: paused, forward: forward);
            Healthy(world);
        }
    }

    static void Until(FiringWorld world, Func<bool> reached, string failure, bool held = true, int limit = 150)
    {
        for (int i = 0; i < limit && !reached(); i++) Run(world, 1, held);
        Require(reached(), failure);
    }

    static void Healthy(FiringWorld world)
    {
        Require(world.Errors.Count == 0, "Controller logged an error: " + string.Join(" | ", world.Errors));
        Require(world.Active, "Controller unexpectedly returned to colony view: " + string.Join(" | ", world.Messages));
        Require(world.Ammo >= 0 && world.Reserve >= 0, "Ammunition went negative.");
    }

    static FiringWorld Loaded(string mode = "Auto")
    {
        var world = new FiringWorld { Ammo = 120, MagazineSize = 120, Reserve = 0, Mode = mode };
        world.Frame(); // Establish an actual released input sample before the press.
        return world;
    }

    static void HeldAuto()
    {
        using var world = Loaded();
        world.Frame(held: true);
        Until(world, () => world.Shots >= 9, "One held press failed to continue through three native bursts.");
        Require(world.AttackOrders >= 3, "Held trigger did not issue subsequent finite native attack jobs.");
        Require(world.Ammo == 120 - world.Shots, "Real simulated rounds and ammo consumption diverged.");
        Require(world.ShotTicks.Distinct().Count() == world.Shots, "Duplicate shots occurred on a single simulation tick.");
    }

    static void SuccessfulRepeat()
    {
        using var world = Loaded();
        world.Frame(held: true);
        Until(world, () => world.AttackOrders >= 4, "Repeated native attacks did not complete and restart.");
        Require(world.ResetCount == 0, "Successful cleanup reset the native verb, erasing CE repeating state.");
        Require(world.Shots >= 9, "Orders restarted without completing actual rounds.");
    }

    static void AutoRelease()
    {
        using var world = Loaded();
        world.Frame(held: true);
        Until(world, () => world.Shots >= 1, "First automatic shot never fired.");
        world.Frame(held: false);
        world.Tick(); // Native tick precedes controller cancellation; allow that boundary round.
        int stoppedAt = world.Shots, orders = world.AttackOrders;
        Run(world, 35, held: false);
        Require(world.Shots == stoppedAt && world.AttackOrders == orders,
            "Released Auto trigger kept firing or issued a fresh attack.");
    }

    static void SingleHeld()
    {
        using var world = Loaded("Single");
        world.Frame(held: true);
        Run(world, 50);
        Require(world.Shots == 1 && world.AttackOrders == 1, "Held semi-auto did not remain one shot per click.");
        world.Frame(); world.Frame(held: true);
        Run(world, 30);
        Require(world.Shots == 2, "A genuine release and second click did not rearm semi-auto.");
    }

    static void BurstHeld()
    {
        using var world = Loaded("Burst");
        world.Frame(held: true);
        Run(world, 60);
        Require(world.Shots == 3 && world.AttackOrders == 1, "Held Burst fired more or fewer than one native burst.");
        world.Frame(); world.Frame(held: true);
        Run(world, 40);
        Require(world.Shots == 6, "Second click did not produce exactly one more burst.");
    }

    static void ShortClick()
    {
        using var world = Loaded("Single");
        world.WarmupTicks = 12;
        world.Frame(held: true); world.Frame();
        Run(world, 40, held: false);
        Require(world.Shots == 1, "A click released before the next tick lost its initial warmup/shot.");
    }

    static void ManualReload()
    {
        using var world = Loaded();
        world.Ammo = 2; world.MagazineSize = 8; world.Reserve = 24;
        world.WarmupTicks = 15;
        world.Frame(held: true); world.Tick();
        Require(world.AttackOrders == 1 && world.Shots == 0, "Fixture did not reach pre-shot warmup.");
        world.Frame(held: true, reload: true);
        Until(world, () => world.Reloads == 1, "Manual reload failed to interrupt warmup.");
        int orders = world.AttackOrders;
        Run(world, 2);
        Require(world.Shots == 0 && world.AttackOrders == orders, "Controller fired/replaced the native reload while busy.");
        Until(world, () => world.Shots >= 3, "Held Auto failed to resume after manual reload.");
        Require(world.ResumedForeignAttacks == 0, "Manual reload replayed a stale attack job.");
    }

    static void LastRoundReload()
    {
        using var world = Loaded();
        world.Ammo = 1; world.MagazineSize = 4; world.Reserve = 12;
        world.Frame(held: true);
        Until(world, () => world.Reloads >= 2 && world.Shots >= 6,
            "Held fire did not cross two empty-magazine/reload boundaries.");
        Require(world.ResumedForeignAttacks == 0, "CE reload resumed an unowned AttackStatic job.");
        Require(world.Ammo + world.Reserve + world.Shots == 13, "Reload loop created or lost ammunition.");
    }

    static void SingleLastRound()
    {
        using var world = Loaded("Single");
        world.Ammo = 1; world.MagazineSize = 5; world.Reserve = 10;
        world.Frame(held: true);
        Run(world, 70);
        Require(world.Shots == 1 && world.Reloads == 1 && world.Ammo == 5,
            "Last-round automatic reload fabricated another semi-auto press or failed to finish.");
        Require(world.ResumedForeignAttacks == 0, "Semi-auto reload replayed an old attack.");
        world.Frame(); world.Frame(held: true); Run(world, 30);
        Require(world.Shots == 2, "Semi-auto could not fire on a new click after reload.");
    }

    static void BurstLastRound()
    {
        using var world = Loaded("Burst");
        world.Ammo = 2; world.MagazineSize = 6; world.Reserve = 12;
        world.Frame(held: true); Run(world, 70);
        Require(world.Shots == 2 && world.Reloads == 1 && world.Ammo == 6,
            "A magazine exhausted mid-burst fabricated another burst after reloading.");
        world.Frame(); world.Frame(held: true); Run(world, 40);
        Require(world.Shots == 5, "Fresh post-reload burst did not fire exactly three rounds.");
    }

    static void EmptyMagazine()
    {
        using var world = Loaded();
        world.Ammo = 0; world.MagazineSize = 6; world.Reserve = 12;
        world.Frame(held: true); Run(world, 20);
        Require(world.Shots == 0 && world.AttackOrders == 0, "Empty magazine created attacks or phantom shots.");
        world.Frame(held: true, reload: true);
        Until(world, () => world.Shots >= 3, "Held fire did not recover after reloading an initially empty gun.");
    }

    static void ExhaustedReserve()
    {
        using var world = Loaded();
        world.Ammo = 2; world.MagazineSize = 4; world.Reserve = 0;
        world.Frame(held: true); Run(world, 80);
        Require(world.Shots == 2 && world.Ammo == 0 && world.Reloads == 0,
            "Running out of reserve ammo fabricated rounds or a reload.");
        world.Frame(held: true, reload: true); Run(world, 20);
        Require(world.Shots == 2 && world.Reloads == 0, "Rejected empty-reserve reload changed weapon state.");
        world.Reserve = 8;
        world.Frame(held: true, reload: true);
        Until(world, () => world.Shots >= 5, "Resupplying and reloading did not restore held fire.");
        Require(world.Ammo + world.Reserve + world.Shots == 10, "Resupply/reload changed total ammunition.");
    }

    static void ReleaseDuringReload()
    {
        using var world = Loaded();
        world.Ammo = 1; world.MagazineSize = 6; world.Reserve = 12;
        world.Frame(held: true);
        Until(world, () => world.Reloads == 1, "Last round did not start reload.");
        world.Frame(); Run(world, 40, held: false);
        Require(world.Shots == 1 && world.Ammo == 6, "Release during reload failed to prevent subsequent automatic fire.");
    }

    static void PreShotReload()
    {
        using var world = Loaded("Single");
        world.Ammo = 1; world.MagazineSize = 6; world.Reserve = 12;
        world.WarmupTicks = 12;
        world.Frame(held: true); world.Tick(); world.Frame();
        world.StartReload(resume: true);
        Run(world, 50, held: false);
        Require(world.Shots == 1 && world.ResumedForeignAttacks == 0,
            "Native reload before the first shot lost the accepted short click or replayed an unowned job.");
    }

    static void QueuedStaleAttackAfterReload()
    {
        using var world = Loaded("Single");
        world.Ammo = 2; world.MagazineSize = 6; world.Reserve = 12;
        world.WarmupTicks = 12;
        world.Frame(held: true); world.Tick();
        var oldAttack = world.Pawn.CurJob;
        Require(oldAttack?.def == RimWorld.JobDefOf.AttackStatic, "Fixture did not start the owned attack.");
        world.Frame(reload: true); world.Tick();
        Require(world.Pawn.CurJob?.def.defName == "ReloadWeapon", "Manual reload did not replace the attack.");
        // Model a native/CE queue path that replays the old job without the
        // StartJob resume flag intercepted by RimFront's reload hook.
        oldAttack.Suspended = true;
        world.Pawn.jobs.jobQueue.EnqueueFirst(oldAttack);
        for (int i = 0; i < 25; i++) { world.Tick(); world.Frame(); Healthy(world); }
        Require(world.ResumedForeignAttacks == 1 && world.Shots == 0 && world.PendingResumptions == 0,
            "A stale attack replayed after reload or remained in the native queue.");
        world.Frame(held: true);
        Until(world, () => world.Shots == 1, "A fresh click could not fire after discarding the stale attack.");
    }

    static void QueuedStaleAttackWithoutInput()
    {
        using var world = Loaded("Single");
        world.Ammo = 2; world.MagazineSize = 6; world.Reserve = 12;
        world.WarmupTicks = 12;
        world.Frame(held: true); world.Tick();
        var oldAttack = world.Pawn.CurJob;
        world.Frame(reload: true); world.Tick();
        Require(world.Pawn.CurJob?.def.defName == "ReloadWeapon", "Fixture did not enter native reload.");
        oldAttack.Suspended = true;
        world.Pawn.jobs.jobQueue.EnqueueFirst(oldAttack);
        world.ExpireInput();
        world.Tick(25);
        Require(world.Active && world.ResumedForeignAttacks == 1 && world.Shots == 0
            && world.PendingResumptions == 0 && world.Pawn.CurJob != oldAttack,
            "Native replay survived because no fresh input frame arrived after reload.");
    }

    static void CompletedAttackRestarts()
    {
        using var world = Loaded("Single");
        world.WarmupTicks = 4;
        world.Frame(held: true); world.Tick();
        var oldAttack = world.Pawn.CurJob;
        Require(oldAttack?.def == RimWorld.JobDefOf.AttackStatic, "Fixture did not start an owned attack.");
        world.Pawn.jobs.jobQueue.EnqueueFirst(oldAttack);
        world.Frame();
        for (int i = 0; i < 20; i++) { world.Tick(); world.Frame(); Healthy(world); }
        Require(world.Shots == 1 && world.AttackOrders == 2 && world.PendingResumptions == 0,
            "A completed attack restarted from the native queue, fired again, or released 3D control.");
        world.Frame(held: true);
        Until(world, () => world.Shots == 2, "A fresh click could not fire after a completed-job replay.");
    }

    static void StaleAuto()
    {
        using var world = Loaded();
        world.Frame(held: true); Run(world, 4);
        world.ExpireInput(); world.Tick(); world.Tick(3);
        int interruptedAt = world.Shots;
        world.Frame(held: true);
        Until(world, () => world.Shots >= interruptedAt + 6,
            "Fresh held Auto input stayed latched off after the stale-input cancellation.");
    }

    static void MovementDuringReload()
    {
        using var world = Loaded("Single");
        world.Ammo = 1; world.MagazineSize = 6; world.Reserve = 12;
        world.WarmupTicks = 12; world.ReloadTicks = 10;
        world.Frame(held: true); world.Tick(); world.Frame();
        Require(world.StartReload(resume: true), "Fixture could not interrupt the pre-shot warmup with reload.");
        world.Frame(forward: 1); Run(world, 3, held: false, forward: 1);
        world.Frame(); // Movement stops before the native reload completes.
        Run(world, 45, held: false);
        Require(world.Shots == 0 && world.Reloads == 1 && world.Ammo == 6,
            "Movement during reload failed to discard the queued pre-shot click; gun fired after movement stopped.");
        world.Frame(held: true); Run(world, 30);
        Require(world.Shots == 1, "A fresh click after cancelled reload intent did not fire exactly once.");
    }

    static FiringWorld ReloadWorld(bool analog)
    {
        var world = Loaded(); world.Game.Release("reload movement setup");
        RimFront.RimFrontMod.Settings.analogMovement = analog; world.Game.Control(world.Pawn);
        world.Ammo = 1; world.MagazineSize = 6; world.Reserve = 20; world.ReloadTicks = 10;
        return world;
    }
    static bool Walking(FiringWorld world) => RimFront.AnalogMovement.Active
        ? RimFront.AnalogMovement.IsMoving : world.Pawn.pather.Moving;

    static void MovingReload()
    {
        foreach (bool analog in new[] { true, false })
        {
            using var world = ReloadWorld(analog);
            // Reload movement is independent of the firing movement preference.
            RimFront.RimFrontMod.Settings.movingFire = false;
            world.Frame(reload: true, forward: 1); world.Tick();
            var reload = world.Pawn.CurJob;
            Require(reload?.def.defName == "ReloadWeapon" && Walking(world), "default reload did not allow movement");
            Require(world.Game.MovementSpeedFactor == 1f, "reload inherited the firing speed penalty");
            for (int i = 0; i < 9; i++)
            {
                world.Frame(forward: 1); world.Tick();
                Require(world.Pawn.CurJob == reload && Walking(world) && world.Reloads == 1 && world.Ammo == 1,
                    "WASD replaced/restarted/completed the native reload early");
            }
            world.Frame(forward: 1); world.Tick();
            Require(world.Ammo == 6 && world.Reserve == 15 && world.Reloads == 1 && world.Shots == 0,
                "moving changed native reload timing or ammunition");
        }
    }
    static void MovingAutoReload()
    {
        foreach (string mode in new[] { "Single", "Burst", "Auto" })
        foreach (bool analog in new[] { true, false })
        {
            using var world = ReloadWorld(analog); world.Mode = mode;
            world.Frame(held: true, forward: 1);
            for (int i = 0; i < 20 && world.Reloads == 0; i++) { world.Tick(); world.Frame(held: true, forward: 1); }
            Require(world.Reloads == 1 && world.Shots == 1, "last round did not start one native reload");
            world.Tick(); world.Frame(held: true, forward: 1);
            Require(Walking(world) && world.Pawn.CurJob?.def.defName == "ReloadWeapon", "automatic reload stopped movement");
            for (int i = 0; i < 45; i++) { world.Tick(); world.Frame(held: true, forward: 1); }
            Require(mode == "Auto" ? world.Shots > 1 : world.Shots == 1,
                "moving reload lost held Auto or manufactured a Single/Burst press");
            Require(world.ResumedForeignAttacks == 0, "moving reload resumed an old attack job");
        }
    }
    static void MovingReloadToggle()
    {
        foreach (bool analog in new[] { true, false })
        {
            using var world = ReloadWorld(analog); world.ReloadTicks = 30;
            RimFront.RimFrontMod.Settings.movingReload = false;
            world.Frame(reload: true, forward: 1); world.Tick(); var reload = world.Pawn.CurJob;
            Require(!Walking(world), "disabled moving reload still walked");
            RimFront.RimFrontMod.Settings.movingReload = true;
            world.Frame(forward: 1); world.Tick(); Require(Walking(world), "enabling the option did not resume walking");
            RimFront.RimFrontMod.Settings.movingReload = false;
            world.Frame(forward: 1); world.Tick(); Require(!Walking(world) && world.Pawn.CurJob == reload, "disabling canceled reload or kept walking");
            RimFront.RimFrontMod.Settings.movingReload = true;
            world.Frame(forward: 1); world.Tick(); world.Frame(); world.Tick();
            Require(!Walking(world) && world.Pawn.CurJob == reload, "WASD release failed to stop while preserving reload");
        }
    }
    static void MovingReloadSafety()
    {
        foreach (bool analog in new[] { true, false })
        {
            using var world = ReloadWorld(analog); world.ReloadTicks = 100;
            world.Frame(reload: true, forward: 1); world.Tick(); var reload = world.Pawn.CurJob;
            world.Pawn.stances.stunner.Stunned = true;
            world.Frame(forward: 1); world.Tick(); Require(!Walking(world), "stun allowed reload movement");
            world.Pawn.stances.stunner.Stunned = false;
            world.Frame(forward: 1); world.Tick(); Require(Walking(world), "walking did not resume after stun");
            world.ExpireInput(); world.Tick(); Require(!Walking(world) && world.Pawn.CurJob == reload, "stale input kept walking or interrupted reload");
            world.Frame(forward: 1); world.Tick();
            world.Frame(release: true); Require(!Walking(world) && world.Pawn.CurJob == reload, "exit kept walking or canceled native reload");
        }
    }
    static void MovingReloadForeign()
    {
        using var world = ReloadWorld(false);
        world.SetExternalJob("ReloadWeapon"); world.ReloadRemaining = 100; world.Frame(forward: 1); world.Tick();
        Require(!Walking(world), "unverified foreign reload gained movement");
        world.SetExternalJob("Goto"); world.Pawn.pather.Moving = true;
        world.Frame(); world.ExpireInput(); world.Tick();
        Require(world.Pawn.pather.Moving, "stale reload input stopped a replacement foreign path");
    }

    static void StaleSingle()
    {
        using var world = Loaded("Single");
        world.Frame(held: true); Run(world, 25);
        world.ExpireInput(); world.Tick(); world.Frame(held: true);
        Run(world, 30);
        Require(world.Shots == 1, "Stale recovery fabricated a new semi-auto press.");
        world.Frame(); world.Frame(held: true); Run(world, 25);
        Require(world.Shots == 2, "Physical release/repress failed after stale recovery.");
    }

    static void PauseHeld()
    {
        using var world = Loaded("Single");
        world.Frame(held: true); Run(world, 25);
        world.Frame(held: true, paused: true); Run(world, 10, paused: true);
        Require(world.Shots == 1, "Paused ticks advanced firing.");
        world.Frame(held: true); Run(world, 25);
        Require(world.Shots == 1, "Unpausing a held trigger created a new shot.");
        world.Frame(); world.Frame(held: true); Run(world, 25);
        Require(world.Shots == 2, "Release/repress failed after pause.");
    }

    static void ModeSwitch()
    {
        using var world = Loaded();
        world.Frame(held: true); Until(world, () => world.Shots >= 1, "Initial shot missing.");
        world.Frame(held: true, cycleMode: true); world.Tick();
        int stoppedAt = world.Shots;
        Run(world, 30);
        Require(world.Mode != "Auto" && world.Shots == stoppedAt, "Mode switch did not gate the held trigger.");
        world.Frame(); world.Frame(held: true);
        Until(world, () => world.Shots > stoppedAt, "New click failed after mode switching.");
    }

    static void BipodToggle()
    {
        using var world = Loaded();
        world.SetSetupWait(100);
        world.Frame(held: true, bipod: true); world.Tick();
        Require(world.Active && !world.AutoBipod && world.SetupRemaining == 0 && world.BipodToggles == 1,
            "G must cancel ongoing setup before the setup wait returns.");
        Run(world, 15);Require(world.Shots > 0 && world.BipodToggles == 1,"held fire recovers without repeating the toggle");
        world.Frame(bipod: true);world.Tick();Require(world.AutoBipod,"second press re-enables automatic setup");
        world.HasBipod = false;world.Frame(bipod: true);world.Tick();Healthy(world);
        Require(world.BipodToggles == 2,"unsupported weapon must not mutate bipod state");
    }
    static void BipodInputLifetime()
    {
        using var world = Loaded();
        world.Frame(bipod: true, paused: true);world.Frame();world.Tick();
        Require(world.BipodToggles == 0,"paused G replayed after unpause");
        world.Frame(bipod: true);world.ExpireInput();world.Tick();world.Frame();world.Tick();
        Require(world.BipodToggles == 0,"stale G replayed on recovery");
        world.Frame(bipod: true);world.Frame(release: true);world.Game.Control(world.Pawn);world.Frame();world.Tick();
        Require(world.BipodToggles == 0 && world.AutoBipod,"pending G leaked across possession");
    }
    static void BipodWhileBusy()
    {
        using var world = Loaded();
        world.Frame(held: true);Until(world,()=>world.Shots>0,"shot never started");
        int orders=world.AttackOrders, shots=world.Shots;
        world.Frame(held: true,bipod: true);world.Tick();Healthy(world);
        Require(world.BipodToggles==1&&world.AttackOrders==orders,"toggle replaces active attack");
        Until(world,()=>world.Shots>shots,"held auto failed after toggle");
        world.Ammo=1;world.Reserve=20;world.Frame(reload:true);world.Tick();
        Until(world,()=>world.ReloadRemaining>0,"reload never started",held:false);
        world.Frame(bipod:true);world.Tick();Healthy(world);
        Require(world.BipodToggles==2&&world.ReloadRemaining>0,"toggle interrupts native reload");
        Until(world,()=>world.ReloadRemaining<=0,"reload stuck",held:false);
    }
    static void BipodMovingClick()
    {
        using var world=Loaded("Single");
        world.Frame(held:true,forward:1,bipod:true);world.Tick();
        world.Frame();Run(world,20,held:false);
        Require(world.Shots==0&&world.BipodToggles==1,"G retained a click cancelled by movement");
        world.Frame(held:true);Until(world,()=>world.Shots==1,"fresh click failed after G/movement",held:false);
    }
    static void BipodForeignJob()
    {
        using var world=Loaded();world.SetExternalJob("JobDef_SetUpBipod",forced:true);world.SetupRemaining=100;
        world.Frame(bipod:true);world.Tick();
        Require(!world.Active&&world.BipodToggles==0,"G preempted an external/forced job");
    }
    static void BipodSetup()
    {
        using var world = Loaded();
        world.SetSetupWait(8); world.Frame(held: true);
        Run(world, 5);
        Require(world.AttackOrders == 0 && world.Shots == 0, "Held fire replaced native bipod setup before completion.");
        Until(world, () => world.Shots >= 6, "Held fire did not start and repeat after finite bipod setup.");
    }

    static void Movement()
    {
        using var world = Loaded();
        RimFront.RimFrontMod.Settings.movingFire = false;
        world.WarmupTicks = 15;
        world.Frame(held: true); world.Tick();
        world.Frame(held: true, forward: 1); Run(world, 20, forward: 1);
        Require(world.Shots == 0, "Movement did not cancel the owned warmup.");
        world.Frame(held: true);
        Until(world, () => world.Shots >= 3, "Held Auto failed to resume after movement stopped.");
    }

    static void MovingFire()
    {
        using var world = Loaded();
        world.Frame(held: true, forward: 1);
        for (int i = 0; i < 40; i++) { world.Tick(); world.Frame(held: true, forward: 1); }
        Require(world.Shots >= 9 && world.AttackOrders >= 3, "walking interrupted repeated auto bursts");
        Require(RimFront.AnalogMovement.IsMoving && world.Game.MovementSpeedFactor == .5f, "firing must walk at half speed");
    }
    static void MovingClick()
    {
        foreach (string mode in new[] { "Single", "Burst" })
        {
            using var world = Loaded(mode);
            world.Frame(held: true, strafe: 1);
            for (int i = 0; i < 40; i++) { world.Tick(); world.Frame(held: true, strafe: 1); }
            Require(world.Shots == (mode == "Single" ? 1 : 3), "walking manufactured an extra trigger press");
        }
    }
    static void MovingNative()
    {
        using var world = Loaded();
        world.Game.Release("native path test");
        RimFront.RimFrontMod.Settings.analogMovement = false;
        world.Game.Control(world.Pawn); world.Frame(held: true, forward: 1); world.Tick();
        Require(world.Pawn.pather.Moving && world.Pawn.CurJob.def == RimWorld.JobDefOf.AttackStatic, "native walking replaced attack ownership");
        var stance = (Verse.Stance_Busy)world.Pawn.stances.curStance;
        Require(world.Game.AllowsAttackMovement(stance), "native warmup still blocks walking");
        world.Frame(held: true); world.Tick();
        Require(!world.Pawn.pather.Moving, "native walking did not stop on WASD release");
    }
    static void MovingToggle()
    {
        using var world = Loaded();
        world.Frame(held: true, forward: 1); world.Tick();
        RimFront.RimFrontMod.Settings.movingFire = false;
        world.Tick();
        Require(world.Shots == 0 && world.Pawn.CurJob?.def != RimWorld.JobDefOf.AttackStatic, "opt-out did not cancel moving warmup immediately");
        RimFront.RimFrontMod.Settings.movingFire = true;
        for (int i = 0; i < 12; i++) { world.Tick(); world.Frame(held: true, forward: 1); }
        Require(world.Shots > 0, "re-enabled moving fire failed");
    }
    static void MovingSafety()
    {
        using var world = Loaded(); world.Frame(held: true, forward: 1); world.Tick();
        var stance = (Verse.Stance_Busy)world.Pawn.stances.curStance;
        Require(!world.Game.AllowsAttackMovement(new Verse.Stance_Warmup(1, default, world.Verb)), "foreign stance was unlocked");
        world.Pawn.stances.stunner.Stunned = true;
        Require(!world.Game.AllowsAttackMovement(stance), "stun was bypassed");
        world.Pawn.stances.stunner.Stunned = false;
        world.ExpireInput(); world.Tick();
        Require(!RimFront.AnalogMovement.IsMoving && !world.Game.AllowsAttackMovement(stance), "stale moving fire survived");
        world.Frame(release: true); Require(!world.Active, "moving fire prevented exit");
    }
    static void MovingHooks()
    {
        using var world = Loaded(); world.Frame(held: true, forward: 1, crouch: true); world.Tick();
        var busy = (Verse.Stance_Busy)world.Pawn.stances.curStance;
        bool Busy(Verse.Stance_Busy stance)
        {
            return stance != world.Pawn.stances.curStance || RimFront.MovingFireStance.PathBusy(true, world.Pawn.pather);
        }
        float Payment(Verse.Pawn_PathFollower pather)
        {
            object[] args = { pather, 4f };
            typeof(RimFront.ControlledMovementPenalty).GetMethod("Postfix", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, args);
            return (float)args[1];
        }
        Require(!Busy(busy) && Payment(world.Pawn.pather) == 1f, "actual movement hooks did not unlock/penalize owned fire");
        Require(busy.StanceBusy, "movement exception changed combat's native busy state");
        Require(Busy(new Verse.Stance_Warmup(1, default, world.Verb)) && Payment(new Verse.Pawn_PathFollower()) == 4f, "actual movement hooks affected another pawn/stance");
        world.Pawn.stances.stunner.Stunned = true;
        Require(Busy(busy), "actual hook bypassed stun"); world.Pawn.stances.stunner.Stunned = false;
        world.ExpireInput(); Require(Busy(busy), "actual hook bypassed stale input");
        world.Frame(held: true, forward: 1); world.Paused = true; Require(Busy(busy), "actual hook bypassed pause");
        world.Paused = false; RimFront.RimFrontMod.Settings.movingFire = false; Require(Busy(busy), "actual hook bypassed disabled setting");
        world.Frame(release: true); Require(Busy(busy) && Payment(world.Pawn.pather) == 4f, "actual hooks leaked across exit");
    }
    static void CrouchSpeed()
    {
        using var world = Loaded(); world.Frame(crouch: true, forward: 1); world.Tick();
        Require(world.Game.MovementSpeedFactor == .5f, "crouching must halve walking speed");
        world.Frame(crouch: true, held: true, forward: 1); world.Tick();
        Require(world.Game.MovementSpeedFactor == .25f, "crouch and firing penalties must combine");
        world.Frame(); world.Tick();
        Require(world.Game.MovementSpeedFactor == .5f, "released crouch retained its additional penalty");
        Run(world, 20, held: false);
        Require(world.Game.MovementSpeedFactor == 1f, "finished attack retained a movement penalty");
    }
    static void InvalidAim()
    {
        using var world = Loaded();
        world.InvalidAim = true; world.Frame(held: true); Run(world, 20);
        Require(world.Shots == 0, "Invalid aim emitted shots.");
        world.InvalidAim = false;
        Until(world, () => world.Shots >= 3, "Held fire stayed blocked after aim became valid.");
    }

    static void NegativeWarmup()
    {
        foreach (float moving in new[] { 0f, 1f })
        {
            using var world = Loaded("Single"); world.ExpiredWarmup = true;
            world.Frame(held: true, forward: moving); world.Tick();
            var attack = world.Pawn.CurJob;
            for (int i = 0; i < 75; i++) { world.Tick(); world.Frame(held: true, forward: moving); }
            Require(world.Pawn.CurJob != attack && !(world.Pawn.stances.curStance is Verse.Stance_Warmup),
                "negative warmup defeated the watchdog or left an orphan busy stance");
            Require(world.Shots == 0 && world.AttackOrders == 1, "recovery invented a Semi-auto click");
            world.ExpiredWarmup = false; world.Frame(); world.Frame(held: true, forward: moving);
            Until(world, () => world.Shots == 1, "fresh click did not recover from negative warmup");
        }
    }
    static void CancelWarmup()
    {
        using var world = Loaded(); world.WarmupTicks = 300;
        world.Frame(held: true); world.Tick(); world.InvalidAim = true; world.Tick();
        Require(!(world.Pawn.stances.curStance is Verse.Stance_Warmup), "invalid aim left a blocking warmup after cancellation");
        Require(world.WarmupPresentOnReset == 0, "verb Reset ran while its canceled warmup was still installed");
        world.InvalidAim = false;
        Until(world, () => world.Shots > 0, "held Auto failed to recover after canceled warmup", limit: 400);
    }

    static void ExitMidBurst()
    {
        using var world = Loaded();
        world.Frame(held: true); Until(world, () => world.Shots >= 1, "Initial shot missing.");
        world.Frame(release: true);
        int shots = world.Shots;
        world.Tick(30);
        Require(!world.Active && world.Shots == shots && world.Errors.Count == 0,
            "Exiting left the owned burst running or raised a controller error.");
    }

    static void StalledAttack()
    {
        using var world = Loaded();
        world.StallAttack = true;
        world.Frame(held: true);
        Until(world, () => world.ResetCount > 0, "A stalled owned attack never reached controller cancellation.");
        Require(world.Shots == 0, "The stalled native boundary fabricated a shot.");
        world.StallAttack = false;
        Until(world, () => world.Shots >= 3, "Held fire did not recover after the native stall cleared.");
    }

    static void MultipleTicks()
    {
        foreach (string mode in new[] { "Auto", "Single" })
        {
            using var world = Loaded(mode);
            world.Frame(held: true);
            for (int frame = 0; frame < 12; frame++)
            {
                world.Tick(4); world.Frame(held: true); Healthy(world);
            }
            Require(mode == "Auto" ? world.Shots >= 9 : world.Shots == 1,
                mode + " handled repeated simulation ticks as new clicks or lost held continuation.");
        }
    }

    static void FixedHeld()
    {
        using var world = Loaded("Fixed");
        world.Frame(held: true);
        Until(world, () => world.Shots >= 4, "A fixed-mode weapon did not repeat while held.");
        Require(world.AttackOrders >= 4, "Fixed mode did not complete distinct native attacks.");
    }

    static void ExternalOrder()
    {
        using var world = Loaded();
        world.SetExternalJob("HaulToCell");
        world.Frame(held: true); world.Tick();
        Require(!world.Active && world.Pawn.CurJob?.def?.defName == "HaulToCell" && world.Errors.Count == 0,
            "An incompatible external order was overwritten instead of releasing control.");
    }

    static void DeferAttack(FiringWorld world, bool shortClick = false)
    {
        world.DeferNextOrderedJob = true;
        world.HoldQueuedJobs = true;
        world.Frame(held: true);
        if (shortClick) world.Frame();
        world.Tick();
        Require(world.AttackOrders == 0 && world.Shots == 0 && world.LastCreatedAttack != null
            && !world.Pawn.jobs.jobQueue.Contains(world.LastCreatedAttack),
            "Deferred native acceptance started or left its attack queued after ownership was dropped.");
        Healthy(world);
    }

    static void DeferredReload()
    {
        using var world = Loaded();
        world.Ammo = 2; world.MagazineSize = 6; world.Reserve = 12;
        DeferAttack(world);
        world.Frame(reload: true); world.Tick();
        Require(world.Reloads == 1, "Manual R did not start reload after deferred acceptance.");
        world.HoldQueuedJobs = false;
        Run(world, 35, held: false);
        Require(world.Shots == 0 && world.PendingResumptions == 0,
            "Cancelled deferred attack survived manual reload or fired without fresh input.");
    }

    static void DeferredClick()
    {
        using var world = Loaded("Single");
        DeferAttack(world, shortClick: true);
        world.HoldQueuedJobs = false;
        Run(world, 45, held: false);
        Require(world.Shots == 1 && world.AttackOrders == 1 && world.PendingResumptions == 0,
            "Deferred single click was lost, duplicated, or replayed as an unowned job.");
    }

    static void DeferredExit()
    {
        using var world = Loaded();
        DeferAttack(world);
        world.Frame(release: true);
        world.HoldQueuedJobs = false; world.Tick(30);
        Require(!world.Active && world.Shots == 0 && world.AttackOrders == 0 && world.PendingResumptions == 0,
            "Leaving RimFront allowed a deferred owned attack to start or fire afterward.");
    }

    static void DeferredMovement()
    {
        using var world = Loaded("Single");
        RimFront.RimFrontMod.Settings.movingFire = false;
        DeferAttack(world, shortClick: true);
        world.Frame(forward: 1); world.Tick();
        world.HoldQueuedJobs = false;
        Run(world, 5, held: false, forward: 1);
        Run(world, 25, held: false);
        Require(world.Shots == 0 && world.PendingResumptions == 0,
            "Movement failed to cancel the deferred accepted click.");
    }

    static void DeferredStale()
    {
        using var world = Loaded("Single");
        DeferAttack(world, shortClick: true);
        world.ExpireInput(); world.Tick();
        world.HoldQueuedJobs = false;
        world.Frame(); Run(world, 30, held: false);
        Require(world.Shots == 0 && world.PendingResumptions == 0,
            "Stale input left a deferred click authorized to fire later.");
    }

    static void DeferredForeign()
    {
        using var world = Loaded();
        var foreign = new Verse.AI.Job { def = new Verse.AI.JobDef("HaulToCell"), playerForced = true };
        world.Pawn.jobs.jobQueue.EnqueueLast(foreign);
        DeferAttack(world);
        Require(world.Pawn.jobs.jobQueue.Contains(foreign), "Removing the deferred attack discarded an unrelated queued order.");
        world.Frame(release: true);
        Require(world.Pawn.jobs.jobQueue.Contains(foreign) && world.PendingResumptions == 1,
            "Release failed to remove exactly its deferred attack while preserving the foreign order.");
        world.HoldQueuedJobs = false; world.Tick();
        Require(ReferenceEquals(world.Pawn.CurJob, foreign), "The preserved foreign job could not start after release.");
    }

    static void RecycledRequest()
    {
        foreach (bool queued in new[] { false, true })
        {
            using var world = Loaded("Single");
            world.RecycleNextOrderedJob = true; world.QueueRecycledJob = queued;
            world.HoldQueuedJobs = queued;
            world.Frame(held: true); world.Frame(); world.Tick();
            var foreign = world.RecycledJob;
            Require(foreign != null && world.Shots == 0, "Fixture did not recycle the accepted request synchronously.");
            if (queued)
            {
                Require(world.Pawn.jobs.jobQueue.Contains(foreign), "Queued removal deleted a different generation of the pooled Job object.");
                world.Frame(release: true);
                Require(world.Pawn.jobs.jobQueue.Contains(foreign), "Release discarded the recycled foreign queued job.");
                world.HoldQueuedJobs = false; world.Tick();
            }
            else world.Tick();
            Require(!world.Active && ReferenceEquals(world.Pawn.CurJob, foreign) && world.ResetCount == 0,
                "The controller claimed a recycled foreign Job as its owned attack or cancelled the foreign order.");
        }
    }

    static void ReloadDefersForeign()
    {
        using var world = Loaded("Single");
        world.Ammo = 2; world.MagazineSize = 6; world.Reserve = 12; world.WarmupTicks = 15;
        world.Frame(held: true); world.Tick();
        var foreign = new Verse.AI.Job { def = new Verse.AI.JobDef("HaulToCell"), playerForced = true };
        world.Pawn.jobs.jobQueue.EnqueueLast(foreign);
        world.Frame(reload: true); world.Tick();
        Require(world.Pawn.CurJob?.def.defName == "ReloadWeapon" && world.Reloads == 1,
            "Manual reload failed to replace the owned warmup.");
        Require(!world.Events.Contains("job-start:HaulToCell") && world.Pawn.jobs.jobQueue.Contains(foreign),
            "Ending the owned attack started the foreign queued job inside the manual reload replacement.");
        for (int i = 0; i < 20 && world.Active; i++) { world.Frame(); world.Tick(); }
        Require(!world.Active && ReferenceEquals(world.Pawn.CurJob, foreign) && world.Ammo == 6 && world.Shots == 0,
            "The preserved foreign order failed to run normally after reload or a cancelled click fired.");
    }
}
