# Production firing-loop scenarios

This executable compiles the repository's **actual `Source/Gameplay.cs`**, `NativeInput.cs`, and `NativeJobPolicy.cs`. It calls `GameComponentUpdate`, `GameComponentTick`, `BeforeNativeJobStart`, and `BeforeNativeJobCleanup` across successive frames/ticks. It does not reimplement the controller's trigger, pending-input, ownership, reload-resumption, or stale-input decisions in a test-only state machine.

`EngineFixture.cs` substitutes the engine boundary: pawn/job/stance objects, input/rendering, equipped-weapon adapter, and a deterministic weapon scheduler. The scheduler supplies finite warmup/burst/cooldown jobs, magazine/reserve accounting, native reload transitions, and finite bipod setup and a simulated bipod-toggle adapter. It invokes controller callbacks before native job replacement/cleanup and simulates the stale-job resume queue if resumption is allowed. Frame order matches the inspected 1.7 game: native tick, controller tick, then the next frame's input. Stale ages are injected without sleeping.

## What these tests establish

Named scenarios assert observable outcomes: shots and ammunition, subsequent attack orders, continued first-person ownership, absence of resumed foreign attacks, and completion of reload/setup. They cover held Auto, per-click Single/Burst, short clicks, manual and automatic reloads, last-round exhaustion, no reserve ammo, resupply, release during reload, pre-shot reload, stale frames, pause, mode changes, movement, invalid aim, exit, and external orders.

Each scenario emits a JSON result. A scenario fails if its expected outcome does not happen within its tick deadline, if the controller logs an error, or if it unexpectedly releases control. The report counts scenarios, not loop iterations or vertices.

Run from the repository root with a .NET 10 SDK:

```powershell
./scripts/test-firing-loop.ps1 -VerifyMutations
```

The mutation gate compiles seven temporary variants of the production controller, independently restoring:

- The stale-input physical-release latch (`stale-auto-recovery` must fail).
- Reload resumption of the old owned attack (`reload-resume-ownership` must fail).
- Verb reset on successful cleanup (`successful-repeat-cycle` must fail).
- A fresh-press requirement for every Auto cycle (`held-auto-next-burst` must fail).
- Failure to reset a cancelled native burst (`exit-mid-burst` must fail even after the attack job ends).
- Retaining a queued pre-shot click through movement during reload (`movement-during-reload-cancels-click` must fail).
- Leaving an accepted attack in the native queue after dropping ownership (`deferred-attack-manual-reload` must fail).

All unmodified scenarios must pass. Each altered controller must **compile** and fail its designated behavior scenario. Compile failures, process crashes, malformed results, missing scenarios, and failures in unrelated scenarios do not count as catching a regression. Each build has isolated output directories; the shipped source and game installation are never changed. JSON evidence and build logs remain under `.build/firing-loop/<run>/`.

The movement-during-reload scenario was first run against the unmodified 0.6.2 controller: it was the sole failing case out of 27, observing an unwanted shot after movement stopped. The production fix clears queued clicks before returning for a reload/busy stance. The preserved short-click scenario separately requires that a pre-shot reload still fires the accepted shot when no cancelling action occurs.

The 0.6.3 investigation corrected two fixture assumptions: ordered jobs can be accepted into a queue without starting, and interrupted cleanup can immediately start queued work. All 27 original scenarios still passed against the previous controller, while six new deferred-order scenarios failed, including a manual-R reload followed by the same unowned `AttackStatic` exit seen in the game log. The suite now also tests recycled native job identities and preservation of foreign orders across reload replacement, for 35 named scenarios. A dedicated reuse scenario changes the fake job's allocation ID to model native pooling. These additions address the observed coverage gap; they do not prove which native queue condition occurred in the user's session.

## Limits and release use

This is controller integration coverage with a simulated engine boundary. It does **not** execute Combat Extended's real reload driver, Harmony patch binding, Unity/Waylith input, projectile collision, or rendering. Changes in those dependencies can still escape the harness. Existing native metadata/helper checks complement it; live smoke tests with the installed CE build remain necessary, especially M60/bipod and last-round reloads.

`scripts/test.ps1` runs the scenarios and mutation gate before the other suites. Use `scripts/verify-release.ps1` with the game/Harmony paths to build both targets, run all gates, then package only on success. `package.ps1` is a low-level archive tool, not a verification command. Report named behavior results and live-test status rather than combining scenario counts with geometry assertions.

## V bipod controller coverage

The harness also links production `NativeWeaponBrace.cs`. Eight additional scenarios cover non-CE brace/fold/movement, native CE timed completion and cancellation, queued/paused V, stale input/exit, exact-weapon replacement, held-trigger release gating, disabled CE bipod mechanics, preservation of native reloads, and the exact-component scope of the auto-setup hook. The total is 52 named scenarios. CE components, settings and job timing are simulated; the scope hook is invoked directly. The fixture does not execute Harmony binding or CE stat calculations.

The installed-assembly helper suite separately tests the real classifier, CE spelled-out definition aliases, distinct finite machine-gun meshes, muzzle/socket agreement, nondegenerate triangles, bipod states and mesh budgets. `model-catalog.ps1` exports and renders those compiled meshes.
Bipod regressions cover G during setup/fire/reload, one-shot consumption, pause/stale/re-entry clearing, unsupported weapons, foreign jobs and movement cancelling a queued click. The `bipod-toggle-ignored` mutation must compile and fail `bipod-toggle-cancels-setup`. The weapon adapter remains simulated here; `WeaponControlTests` separately links the real bipod and source adapters.

Four integration scenarios verify V/G mount transitions, simultaneous input, unsupported G preserving a brace, and pause clearing both pending actions.

## Moving fire and crouch coverage

The harness also links production `NativeStance.cs` and directly invokes its stance and movement-payment hooks. Seven added scenarios (59 total) exercise walking warmups and repeated Auto bursts, Single/Burst press semantics, native path attack ownership, immediate setting changes, pause/stale/stun/foreign-stance exclusions, combined crouch/firing speed penalties, and hook cleanup on exit. Legacy movement-cancellation scenarios explicitly disable moving fire. These managed checks do not execute Harmony binding or native path progress; `NativePawnTests` separately checks compiled crouch bounds and actual barricade/sandbag triangles for at least 90% frontal coverage.
