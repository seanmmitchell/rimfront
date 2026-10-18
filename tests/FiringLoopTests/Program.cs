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
            ("held-auto-next-burst", HeldAuto),
            ("successful-repeat-cycle", SuccessfulRepeat),
            ("auto-release-stops-rounds", AutoRelease),
            ("single-held-one-shot", SingleHeld),
            ("burst-held-one-burst", BurstHeld),
            ("short-click-retains-first-shot", ShortClick),
            ("manual-reload-held-auto", ManualReload),
            ("reload-resume-ownership", LastRoundReload),
            ("single-last-round-no-phantom", SingleLastRound),
            ("burst-last-round-no-phantom", BurstLastRound),
            ("empty-magazine-reload-recovery", EmptyMagazine),
            ("exhausted-reserve-resupply", ExhaustedReserve),
            ("release-during-reload", ReleaseDuringReload),
            ("pre-shot-reload-retains-click", PreShotReload),
            ("movement-during-reload-cancels-click", MovementDuringReload),
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
            ("invalid-aim-recovery", InvalidAim),
            ("stalled-attack-recovery", StalledAttack),
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
        world.WarmupTicks = 15;
        world.Frame(held: true); world.Tick();
        world.Frame(held: true, forward: 1); Run(world, 20, forward: 1);
        Require(world.Shots == 0, "Movement did not cancel the owned warmup.");
        world.Frame(held: true);
        Until(world, () => world.Shots >= 3, "Held Auto failed to resume after movement stopped.");
    }

    static void InvalidAim()
    {
        using var world = Loaded();
        world.InvalidAim = true; world.Frame(held: true); Run(world, 20);
        Require(world.Shots == 0, "Invalid aim emitted shots.");
        world.InvalidAim = false;
        Until(world, () => world.Shots >= 3, "Held fire stayed blocked after aim became valid.");
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
        Require(world.AttackOrders == 0 && world.Shots == 0,
            "Deferred native acceptance unexpectedly started the attack synchronously.");
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
