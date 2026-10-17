# RimFront 0.5.8 — native first-person mod

Control a drafted RimWorld colonist in a 3D view rendered **inside the game's own engine**. RimFront targets RimWorld 1.6 and 1.7, requires Harmony, and supports an optional Combat Extended adapter. No browser, MW2 installation, external engine, account, or network connection is needed to play.

The native renderer replaces the 0.1 browser prototype. It reads input every frame and builds cached local meshes. Version 0.5 adds body and creature families, visible apparel, animated nearby pawns, first-person sleeves/anatomy, and more world-object silhouettes. Analog movement and actual-job reloads remain available. Native movement costs, attacks, ammunition, armor, cover, damage, and reload timing remain authoritative.

**Status: experimental native implementation.** Both targets compile; automated collision, geometry, aiming, lighting, reload, weapon, weather, and effect-math checks are available. A 0.5.4 log confirms CE analog-adapter startup and native-view entry on RimWorld 1.7.4981; it also exposed a repeated faction-marker error corrected in 0.5.5. The 0.5.8 reload/held-fire fixes and prior effects, controller behavior, combat integration, visuals, and performance still need in-game validation after restarting RimWorld. Compilation and standalone tests do not certify runtime compatibility. See [compatibility](docs/compatibility.md), [performance evidence](docs/performance.md), and the [smoke checklist](docs/smoke-tests.md).

The prior 0.4.1 hotfix addressed unintended return to colony view during automatic posture transitions and analog corner-contact precision. Those safeguards remain in 0.5 alongside the new visual work; live movement validation is still required.

Version 0.5.2 corrects ranged barrel alignment across animation detail levels, uses rendered lying-body bounds for aiming, assigns missing-arm weapons and shields to separate available locations, and refreshes shield haze on paused takeovers.

Version 0.5.3 adds blood splashes from actual injuries, optional tissue fragments, visible bleeding droplets and native blood trails, plus material-dependent projectile impact particles and temporary ground contact marks. These effects are cosmetic; RimWorld and Combat Extended still own injury, bleeding, shield absorption, wound treatment and cleaning. Each effect group has a toggle under **Options → Mod settings → RimFront**.

Version 0.5.4 implements the previously missing free-fire targeting and pawn status markers. Visible colonists, friends, guests, prisoners and hostiles have distinct labels; prisoners participating in a native prison break flash orange/red. Markers can be disabled in mod settings.

Version 0.5.5 fixes repeated faction-marker errors, incorrect guest/colony labels and marker flicker on crowded paused maps. CE bursts track changing aim distance and stop on invalid aim before ammunition preparation. Stock vanilla bullets retain world obstruction checks, avoid cosmetic-jitter misses, synchronize travel counters and reject final hits on targets already passed. Changing saves clears targeting caches even after leaving first-person view.

Version 0.5.6 adds force-scaled blue/red shield echoes on absorbed bullets, physical-shield parries and confirmed melee contact with active energy belts. Total color separation is capped at 10 cm horizontally and 2 cm vertically, treating one tile as one metre for visual scale; native incoming damage estimates force. Echoes settle and fade over 18 game ticks. Melee feedback does not give energy belts extra protection: native damage and armor remain authoritative, including armor-absorbed contact. The existing colony-view toggle applies the effect in 2D.

Owned attacks now reset their verb before cancellation, exit or native job replacement. A quick click completes the first native warmup/shot; releasing then stops the remaining burst in Auto/Fixed modes. CE Semi-auto/Burst use the per-click behavior described below. Movement and reload cancel warmup explicitly. Interrupted bursts retain native cooldown, and a stalled attack is released after 60 ticks without progress.

The 0.5.6 collision pass aligns owned CE ballistic shots with displayed pawn and world geometry, including surfaces above CE's default cover height. It preserves native impact, shield and damage handlers and tests nearer surfaces before farther targets. Straight vanilla bullets also check close and endpoint solid blockers. Native stochastic foliage/partial-cover behavior and accuracy remain in effect. Animated pawn selection follows the drawn limb meshes and detail level; plant rendering and aiming share growth steps. CE's native warmup sees the selected pawn for Shooting XP and battle-log bookkeeping. Custom beams, arcing and explosive trajectories retain their documented native paths.

Version 0.5.7 adds **F: switch fire mode** and shows the equipped mode on the HUD. CE weapons cycle their native supported modes: Semi-auto fires once per click, Burst completes one native burst per click, and Auto repeats while held. Switching cancels an owned attack while preserving native cooldown and requires releasing a held trigger before firing again. Native reload jobs keep running. Paused or stale input cannot queue a surprise trigger press. Weapons without alternate CE modes retain their fixed native mode; no extra modes are invented. CE owns the weapon's persisted mode and normal shot counts/ammunition.
Version 0.5.8 fixes the CE M60/bipod setup transition ejecting the player from RimFront. Verified automatic setup on the equipped bipod weapon now keeps first-person control and finishes its native stationary job before queued input resumes. Unrelated, forced or sleeping jobs keep their normal ownership rules. Successful completed attacks no longer reset CE's repeat-shot timing state, so held Auto fire can use CE's normal repeat warmup instead of paying the full initial windup every burst. Interruptions still reset the owned verb and preserve cooldown. Recoil remains driven by actual shots; native aiming/reload delays are not replaced by fake firing animation.
## Install and use

1. Close RimWorld before replacing the mod's DLLs. Extract `dist/RimFront-0.5.8.zip` into `RimWorld/Mods/RimFront`; `About/About.xml` must be directly inside that folder. The package contains separate assemblies for 1.6 and 1.7.
2. Enable Harmony, Core, optional Combat Extended, then RimFront. Use Harmony and CE versions suitable for the game branch. Restart after changing the mod list or updating RimFront.
3. Load a disposable test colony. Draft a player-controlled humanlike colonist, select them, and choose **Play in RimFront**. Ranged, melee and unarmed native attacks are supported.
4. Use **WASD** to move, the **mouse** to look, **left mouse** to attack, **R** to reload a supported CE weapon, **F** to cycle its supported fire modes, **Space** to pause/resume, and **Escape** to return to colony view.

The HUD shows health, weapon/ammunition state, a smoothed FPS estimate, native draw submissions, and cached chunks. It is an operational aid, not a performance benchmark. Losing focus, a blocking dialog, an unavailable pawn, or changing maps releases control. Camera, cursor, subcamera, background execution, and fire-at-will settings are restored on exit.

On Windows, a transition safeguard captures the existing game-window icons and restores them around first-person entry/exit, with a short bounded retry window. This addresses the reported taskbar-icon regression defensively; its root cause and live fix still need confirmation.

Taking over replaces the pawn's previous order. No controlled state is saved. Native reloads and weapon busy stances keep their normal timings and stop movement. Multiplayer and concurrent controllers are outside this MVP.

## Movement and fallback

Analog movement uses continuous coordinates, normalized diagonal input, and swept collision against a conservative square body footprint. RimWorld still receives cell changes through its native position setter, with door, filth, snow, and movement notifications. CE shot origins, body bounds, projectile candidate queries, and lead prediction use the controlled pawn's continuous position. Other pawns keep their usual behavior.

Open **Options → Mod settings → RimFront** and clear **True analog movement (default)** to select native path movement on the next entry. The fallback uses continuous look-ahead orders and a camera based on native path progress. Both modes bypass the colony camera's slower pawn-position spring. Unsupported CE contracts or bodies too wide for the analog footprint automatically select the native fallback and show a reason.

The analog controller caps travel at 0.25 cells per tick (15 cells/second at normal speed), rejects unrevealed cells, waits for doors to fully open, and uses native pawn-traffic collision rules. Its square footprint can be conservative at tight corners. These are deliberate MVP constraints, not a claim of identical behavior to every pathfinding or movement mod.

## What is rendered

The renderer uses original low-polygon geometry and palette colors: floors and terrain, opaque fog, walls, doors with sliding leaves, trees and crops, furniture, workbenches, turrets, lamps, conduits, cover, item stacks, weapons and corpses. Version 0.5 adds 32 world-object families, including power equipment, temperature control, production/research stations, shelving, pods, fences/traps, monuments, several plants, and common resource/food/apparel items. Classification uses definition category, size and name; unknown definitions retain a generic fallback. These are representations of game objects, not recreated RimWorld textures or bespoke models for every definition.

The first-person scene uses a perspective camera and depth-tested meshes. Roof ceilings, snow coverage, bounded rain/snow particles, and simple sun/moon cues follow game state. Volumetric clouds, exact celestial motion, arbitrary motes and custom mod effects remain limitations. Stock flat map/dynamic rendering is suppressed only while the native view is active.

## Pawns, apparel and animation

Nine rig families cover humans, quadrupeds, birds, insects, serpents, biped/crawler/centipede mechs, and generic entities. Humans have six body-shape variants, skin/hair tint and simple hair silhouettes. Worn items become shirt, pants, coat, robe, vest, plate, helmet, hat, hood, mask, backpack, boots, gloves or belt geometry, using actual colors and coverage/layer data. Recognized missing structural limbs disappear and prosthetic limbs gain mechanical details. Unknown anatomy, special apparel, xenotype features and mod-specific graphics use approximations rather than bespoke models.

Nearby pawns animate from actual simulation state: movement, aiming, recoil, melee, injury response, reload, work, ingestion and carrying. Lying, downed, sleeping and dead states suppress inappropriate action poses. Game ticks drive timing, so pause freezes progress. First-person hands/sleeves reflect supported clothing, missing arms and prosthetics; the full controlled body stays hidden. Melee and unarmed attacks use the normal adjacent attack verb without automatic chasing.

**Animate nearby pawns** in RimFront mod settings is enabled by default. Disable it to keep dressed models while reducing limb draw work. At most 16 pawns within 14 cells receive articulated rendering; farther or over-budget pawns use assembled standing models. The renderer retains up to 64 appearance entries plus nine shared family fallbacks, refreshes at most two appearances per frame, and constructs at most one articulated rig per frame. Wardrobe changes are sampled at a bounded cadence, so updates may be delayed under load.

Review the [pawn/apparel catalog](docs/model-catalog.png), [outfits and body poses](docs/action-catalog.png), and [world-object catalog](docs/object-catalog.png). They show actual compiled CPU geometry and sampled pose helpers using an offline projection, not game screenshots; runtime scale, animation, lighting and mod compatibility require live checks.

Aim selects the nearest displayed pawn, object, terrain or ceiling under the crosshair, without a faction filter. Left mouse also fires toward an empty aim point within the visible map and weapon range. A foreground wall blocks selection of targets behind it. You can deliberately fire at friendlies, guests and prisoners; native faction consequences still apply. Native weapons retain warmup, accuracy, ammunition, shields, damage and reload timing. Melee requires an adjacent target and never starts a chase. CE retains its 3D ballistics. Stock straight vanilla bullets gain height-aware contact filtering and preserve native spread; explosive, arcing and custom vanilla projectiles retain their native trajectory and show “native 2D trajectory” in the firing status. This fallback does not promise precise vertical aim for those weapons.

## Combat effects, weapons, and lighting

Twelve original weapon families cover pistols, revolvers, rifles, shotguns, SMGs, sniper rifles, heavy guns, launchers, bows, blades, blunt weapons, and grenades. Models appear in first person, on equipped pawns, and for dropped weapons. Classification uses definition-name heuristics; an unknown ranged weapon falls back to a rifle silhouette. Visual recoil and muzzle flashes respond to observed game shots, not mouse clicks. Reloads animate separate weapon, magazine/cylinder/shell, bolt, and hand parts using the actual CE reload job and remaining toil ticks. Pause freezes progress; cancellation removes the reload pose. Unknown timing uses a lowered weapon pose. The observer never starts or completes a reload or changes ammunition.

Version 0.5.1 attaches held weapon grips to the rendered hand, including animated and static pawn models. Drafted pawns hold weapons; undrafted pawns carry two-handed weapons on their backs and one-handed weapons at the hip. CE's one-handed tag takes precedence when available; other weapons use family/name fallbacks. Owned physical shields are held when drafted and stowed on the back when undrafted, with a first-person model for the controlled pawn. Energy shield belts are separate from physical shields.

The update also aligns sleeping bodies with native bed slots and rotations, preserves animated fence-gate openings, invalidates resource meshes at quantity thresholds, and checks native melee verbs only after target geometry is eligible.

Shield effects have separate mod settings: **Faint shield-belt haze**, **Blue/red shield impact ghosting**, and **Shield impacts in colony view**. The colony-view option is off by default. These effects observe shield state, blocked hits and confirmed melee contacts; they do not add protection or change damage.

Projectile observers render bounded trails/segments from live vanilla or CE projectile state and CE beam endpoints. Vanilla visual height is reconstructed from its native arc inputs; CE supplies its 3D position. Impact sparks, dust, and armor-deflection cues are cosmetic. Ricochet-style fragments do not add bounces, damage, or penetration: actual trajectories and any real reflected CE beam segments remain game-owned.

Lighting follows the actual sky, roof state, colored glow grid, and temporary muzzle flashes. Night and unlit rooms become darker; powered lamps illuminate nearby surfaces. This uses vertex/per-draw colors because the 1.7 Unity Light shim does not provide a compatible dynamic-light path. It is not shadow-mapped 3D lighting. The native weather layer reads interpolated rain/snow rates, suppresses particles under roofs and in fog, and uses local time for a simple sun/moon cue. Snow-floor tint reads accumulated snow depth; this mod does not create snow or change weather.

## Build and test

Use .NET **10 SDK** and PowerShell. Version 1.6 restores pinned compile-only references; version 1.7 uses your legally installed game/Harmony assemblies.

```powershell
./scripts/build.ps1 -Version All `
  -RimWorld17Dir 'D:\Steam\steamapps\common\RimWorld' `
  -Harmony17Dir 'D:\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies'
./scripts/test.ps1 -RimWorld17Dir 'D:\Steam\steamapps\common\RimWorld' `
  -Harmony17Dir 'D:\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies'
./scripts/package.ps1 -IncludeSource
```

Pass `-Dotnet 'path/to/dotnet.exe'` to use a local SDK. `RIMWORLD_17_DIR` and `HARMONY_17_DIR` environment variables are accepted. `./scripts/doctor.ps1` reports SDK, reference, and output availability. These scripts do not install into the game.

The tests execute the real swept-collision helper, window-icon lifecycle with an injected backend, compiled geometry/classification/appearance helpers, animation/reload poses, CE coordinate helpers, and lighting/weather/weapon/effect math with the installed 1.7 math assemblies, plus shared input/aim validation and retained legacy bridge/client regressions. They do not touch live windows or start a GPU renderer or RimWorld game session. Node.js is needed only for the legacy browser regression tests. Loopback permission is needed only by the legacy bridge tests; native gameplay does not run a listener.

Recreate the geometry catalogs with `scripts/model-catalog.ps1`, passing the same game/Harmony directories and optionally `-Dotnet` and `-Python`. Catalog rendering additionally needs Python with Pillow and NumPy. It exports the actual model buffers, validates them, and renders them with an offline depth buffer.

| Game | Framework | Output |
| --- | --- | --- |
| 1.6 | .NET Framework 4.7.2 | `mod/1.6/Assemblies/RimFront.dll` |
| 1.7 | .NET 10 | `mod/1.7/Assemblies/RimFront.dll` |

`LoadFolders.xml` selects one assembly. Build references are pinned in `Source/RimFront.csproj`; local game and Harmony DLLs are never bundled. The installable ZIP contains the native mod and documentation. The optional source ZIP contains source, tests, scripts, metadata, and the archived browser prototype, excluding generated DLLs and build directories.

## Performance design

The old full-map JSON snapshots and browser polling have been removed from active gameplay. The renderer keeps at most 49 chunks of 12 × 12 cells, builds at most one missing chunk per frame, and checks one cached chunk every 100 ms once warm. Map-dirty notifications prioritize changed chunks; the fallback stationary refresh cycle can take about 4.9 seconds. Live pawns and door movement are updated separately. Cached geometry and shared materials avoid rebuilding unchanged scenery each frame.

The measured old encoder alone took 3.411 ms per 250 × 250 snapshot in a standalone benchmark. Removing that work is verified by the native control flow; improved live FPS must still be measured. Reproduce the legacy encoder benchmark with `./scripts/benchmark.ps1`. See [performance.md](docs/performance.md) for conditions and limitations.

## Provenance

Inspired by [rkwitz/rimworld-mw2](https://github.com/rkwitz/rimworld-mw2), with an independent implementation and original geometry. RimFront is MIT-licensed. RimWorld, Harmony, and Combat Extended retain their respective ownership and licenses. No game code, decompiled engine implementation, or external game assets are distributed.






