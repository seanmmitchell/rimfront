# RimFront 0.6.3 test build

The current source includes a manual-reload fix for deferred native attack orders, 35 production-controller firing scenarios, and automatic recent-job diagnostics on unexpected exits. Live CE confirmation is pending; the latest published release linked below remains 0.6.2.

Play a drafted RimWorld colonist in first person, rendered inside the game's own engine. RimFront targets **RimWorld 1.6 and 1.7**, requires **Harmony**, and integrates with an optional, branch-compatible **Combat Extended** installation. It uses original low-polygon models and has no MW2 dependency.

**Experimental release.** Native movement, combat integration, camera behavior and performance require testing with your exact game and mod versions. Earlier builds have compile/helper-test coverage and observed 1.7 entry/exit sessions; those do not certify this release. Current verification belongs in [compatibility](docs/compatibility.md). Use a disposable colony for the [smoke checklist](docs/smoke-tests.md).

## Install and enter

1. Close RimWorld. Download `RimFront-0.6.2.zip` from the [0.6.2 release](https://github.com/seanmmitchell/rimfront/releases/tag/v0.6.2) and extract it into `RimWorld/Mods/RimFront`, with `About/About.xml` directly inside that folder. Separate 1.6 and 1.7 assemblies are included. Use the named runtime ZIP to play; GitHub's automatic source archives require a build.
2. Enable Harmony, Core, optional Combat Extended, then RimFront. Use dependency versions appropriate for your game branch and restart after updating.
3. Load a test colony, draft a player-controlled humanlike colonist, select them, and choose **Play in RimFront**. Taking control replaces their previous order.

| Input | Action |
| --- | --- |
| WASD | Move relative to the view |
| Mouse | Look |
| Left mouse | Attack at the crosshair |
| R | Request a supported native CE reload |
| F | Cycle the equipped CE weapon's supported fire modes |
| Space | Pause/resume |
| Escape | Return to colony view |

The entry click does not fire. The HUD shows health, weapon, ammunition, fire mode, movement mode, FPS estimate and rendering counters. Focus loss, a blocking dialog, loss of pawn eligibility, a map change or an incompatible external job releases control. Exit restores camera, cursor, subcamera, background-execution and fire-at-will settings. Possession is not saved. A Windows safeguard preserves existing game-window icons around transitions; its live behavior still needs verification.

## Combat and fire modes

The crosshair selects the nearest displayed pawn, object, terrain or ceiling, or an empty point within visible map and weapon range. Selection has no faction filter: deliberate friendly fire retains native consequences. Melee and unarmed attacks require an adjacent target and do not start a chase.

CE retains its own accuracy, ballistics, ammunition, armor, shields, damage, warmup, cooldown and reload rules. Supported CE modes behave as follows:

| Mode | Trigger behavior |
| --- | --- |
| Semi-auto | One native shot per click |
| Burst | One complete native burst per click |
| Auto | Repeats while held; release cancels remaining fire after the first shot |
| Fixed | Uses the weapon's native mode; no alternate modes are invented |

Switching modes cancels the owned attack, preserves cooldown, clears queued fire and requires releasing a held trigger before firing again. Native reload jobs continue. Pause and stale-input protection prevent a held button from becoming a new queued press. A quick click retains its initial warmup/shot; movement and reload requests explicitly cancel an owned warmup.

Version 0.6.2 restores held Auto/Fixed fire after fresh input returns from a slow frame. The stale tick still cancels its old attack, but no longer latches Auto off until mouse release. Recovery does not create a new Semi-auto/Burst click; explicit pause and mode-switch release requirements remain in force.

For firing problems, enable **Options → Mod settings → RimFront → Diagnostics → Trace firing input and actions**, reproduce the issue, then disable it. The option applies immediately and is off by default. Entries prefixed `[RimFront:Fire]` in `Player.log` connect physical mouse transitions to trigger gates, attack orders, observed shots, reloads, native job changes and interruptions, with weapon/mode/ammunition context. Repeated failures are deduplicated and output is rate limited. On Windows, the log is under `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log` on the inspected 1.7 installation (older versions may put it directly in the parent folder). Copy the log after reproducing, before restarting the game.

Successful completed attacks preserve CE repeat-shot timing state. Interruptions reset the owned verb. Verified automatic setup for the equipped CE bipod weapon keeps first-person control while its stationary job completes; unrelated or forced jobs retain their normal ownership rules. These behaviors need live sustained-fire and reload checks, especially with the M60.

Version 0.6.1 prevents a verified CE reload from suspending and replaying RimFront's old firing job. Reloads still run natively; subsequent fire uses current input and a fresh aim point. Automatic reload after the last round does not create a second Semi-auto or Burst trigger press. Foreign attack orders keep their usual ownership rules.

Owned CE ballistic contacts use displayed pawn and world geometry while retaining native impact handlers. Stock straight vanilla bullets have height-aware contact filtering, exact aim endpoints and close/endpoint solid-obstacle checks, with native accuracy preserved. **Explosive, arcing and custom vanilla projectiles retain their native trajectory** and report “native 2D trajectory.” CE beams and unsupported custom trajectories retain their documented native paths; precise vertical contact is not promised for every weapon.

## Movement

Analog movement is enabled by default. It uses continuous coordinates, native movement costs, swept collision and a conservative square body footprint. Cell changes still notify the game, doors wait until fully open, and native pawn-traffic rules apply. Travel is capped at 0.25 cells per tick; fogged cells remain inaccessible. The square footprint can be conservative in tight corners.

Disable **True analog movement** under **Options → Mod settings → RimFront**, then reenter, to use native path movement. Unsupported CE coordinate contracts or oversized bodies also select this fallback with a reason. Both modes follow authoritative movement; compatibility with other movement mods requires testing.

## Scene, equipment and effects

Natural rock and ore formations render as joined, faceted mountains. Height follows local rock density and distance from exposed edges: broad dense masses rise higher than isolated outcrops. Mining updates the profile. Unexplored rock contributes to the mountain silhouette with a uniform exterior tint; opaque fog still obscures underground cells and unrevealed ore types. Above-fog mountain exteriors can be aimed at as anonymous surface contacts. Mountain collision follows each displayed chunk snapshot while mining updates settle. Constructed walls and loose stone items keep their existing models.

View range defaults to **60 tiles**, twice the original 30. Under **Options → Mod settings → RimFront → View range**, the additional-range slider selects **0% / 50% / 100%** for **60 / 90 / 120 tiles**. It applies immediately to the camera, scenery, pawns, vegetation, effects and crosshair selection. Native weapon range remains authoritative. Longer ranges increase rendering work, memory and cache warmup time.

Original models represent terrain, fog, walls, animated doors, furniture, workstations, utilities, items, corpses and equipment. Unknown definitions use size/category/name-based fallbacks. Nine pawn rig families, six human body shapes, supported apparel layers and recognized missing/prosthetic limbs provide broad coverage, not bespoke models for every definition or xenotype.

Trees, bushes and other plants use their installed game/mod artwork on upright, camera-facing sprites. Perspective, depth testing, growth and native lighting still apply. Plant collision uses stable approximate trunk/foliage volumes within the sprite's size envelope; it does not rotate with the camera or trace individual transparent leaf pixels. Existing game textures are read at runtime and are not bundled with RimFront.

Nearby pawns animate from real motion, jobs and observed attacks. First-person hands and sleeves reflect supported appearance. Drafted pawns hold equipment; undrafted pawns stow one-handed weapons at the hip, two-handed weapons on the back, and physical shields on the back. Available-arm rules prevent duplicate weapon/shield hand assignments. Energy belts remain separate from physical shield models.

Recoil and muzzle flashes follow actual shots. Reload animation follows the actual CE reload job, toil timing and ammunition changes; unknown timing uses a lowered pose. Blood follows positive injuries and native bleeding callbacks; persistent trails use existing blood filth and disappear through native cleaning. Sparks, debris, tissue fragments and shield echoes are cosmetic. They do not add damage, penetration, ricochets or shield protection.

Lighting samples native sky, roofs, glow and brief muzzle flashes. Rain, snow and snow-floor tint follow game state. This is colored vertex/per-draw lighting, not shadow-mapped lighting. Stock flat map/dynamic rendering is suppressed during possession, so arbitrary stock or mod motes may be absent.

Settings control analog movement, pawn animation, indicators, blood/gore/trails, surface impacts and shield presentation. Markers distinguish visible colony, friendly, guest, prisoner and hostile pawns; native prison-break participants flash orange/red. Walls and fog hide markers. Colony-view shield impacts are optional and off by default.

The [pawn/apparel](docs/model-catalog.png), [equipment/action](docs/action-catalog.png) and [object](docs/object-catalog.png) catalogs use actual compiled model geometry projected offline. They are not game screenshots or proof of live rendering quality.

## Build and verify

Use PowerShell and a .NET **10 SDK**. Run from the project root:

```powershell
./scripts/build.ps1 -Version All `
  -RimWorld17Dir 'D:\Steam\steamapps\common\RimWorld' `
  -Harmony17Dir 'D:\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies'
./scripts/test.ps1 -RimWorld17Dir 'D:\Steam\steamapps\common\RimWorld' `
  -Harmony17Dir 'D:\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies'
./scripts/package.ps1 -IncludeSource
```

Pass `-Dotnet 'path/to/dotnet.exe'` for a local SDK. `RIMWORLD_17_DIR` and `HARMONY_17_DIR` are supported. `scripts/doctor.ps1` reports dependency/output availability. Build and package scripts do not install into the game.

| Target | Framework / references | Output |
| --- | --- | --- |
| 1.6 | .NET Framework 4.7.2; pinned compile-only references | `mod/1.6/Assemblies/RimFront.dll` |
| 1.7 | .NET 10; legally installed game and Harmony assemblies | `mod/1.7/Assemblies/RimFront.dll` |

`LoadFolders.xml` selects the matching assembly. Game/Harmony DLLs and decompiled engine code are not bundled. The optional source archive contains project source, tests and tools.

Standalone tests exercise collision, input/aim policy, geometry, appearance, animation, effects and lifecycle helpers. They do not start a GPU renderer or game session. Recreate catalogs with `scripts/model-catalog.ps1`, using the same game/Harmony paths; this optional tool also needs Python, Pillow and NumPy.

The [firing-loop suite](https://github.com/seanmmitchell/rimfront/blob/main/tests/FiringLoopTests/README.md) drives the production controller across frames, ticks, attacks and reload callbacks with a deterministic engine boundary. It checks held fire, last-round reloads, ammo exhaustion/resupply, input interruptions and continued control. `test.ps1` also deliberately restores seven firing regressions in isolated builds and requires the relevant scenarios to fail. Those results are reported as named scenarios, separately from geometry assertions. The simulated boundary does not replace live game/CE testing.

For release preparation, use `./scripts/verify-release.ps1` with the same `-Dotnet`, `-RimWorld17Dir` and `-Harmony17Dir` arguments. It builds both versions, runs all suites including firing-loop mutation checks, and only then packages. Calling `package.ps1` alone only creates archives; it does not verify behavior.

See [architecture](docs/architecture.md) for implementation boundaries, [performance](docs/performance.md) for budgets and live measurement, and [smoke tests](docs/smoke-tests.md) for release checks. Multiplayer, concurrent controllers, universal visual-mod compatibility and measured FPS improvements are not claimed.

## Contributing and CI

Open a pull request into main. The required CI gate builds 1.6 and runs the portable tests on PRs and main pushes; it does not certify 1.7 or live CE gameplay. Run the same gate with `./scripts/test-portable.ps1`. Use the PR template to report the problem, resulting behavior, validation evidence, and remaining risks. See [CI and release boundaries](https://github.com/seanmmitchell/rimfront/blob/main/docs/ci.md) for coverage and branch protection.

## Provenance

Inspired by [rkwitz/rimworld-mw2](https://github.com/rkwitz/rimworld-mw2), with an independent implementation and original geometry. RimFront is MIT-licensed. RimWorld, Harmony and Combat Extended retain their respective ownership and licenses. No game assets or decompiled engine implementation are distributed.
