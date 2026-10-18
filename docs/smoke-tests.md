# Native smoke checklist — 0.6.3 test build

These are required live checks, **not recorded passes**. Close RimWorld, install the extracted 0.6.3 package, restart and use a disposable colony. Run on both 1.6 and 1.7, first with Harmony/Core and then an appropriate CE build. Record exact versions, mod list, hardware, Player.log, expected/actual results and reproduction steps.

Before packaging a new release, run `scripts/verify-release.ps1`. Its firing-loop gate drives the production controller with simulated engine timing/ammo and must reject seven deliberately restored regressions. Record the named scenario and mutation results separately from helper assertion totals. The following live checks still cover native CE, Harmony binding and input/rendering behavior that the simulated boundary cannot certify.

For the 0.6.3 reload fix, press R during warmup, burst, cooldown, and idle with a partly loaded magazine; repeat with no reserve ammunition and while holding/releasing fire. Include M60/bipod and a non-bipod gun. Verify the reload finishes in first-person view and Auto resumes only while held. Any unexpected job exit now writes one `[RimFront:Fire] event=unexpected-job` report with the recent 12 job starts even when optional tracing is off; retain that report with the log.

## Optional 2D characters

Enable **Use viewpoint-based 2D characters** in RimFront mod settings during possession, then disable it and reenter. Verify the setting persists after restart, 3D remains the default for existing settings, and switching is immediate. Walk around stationary/turning pawns: front, side and back must follow the observer with upright, correctly oriented artwork and no opaque atlas background. Check hats, layered apparel, injuries, animals, mechs, sleeping pawns in every bed rotation, downed pawns and corpses. Change apparel while the option is active and confirm native invalidation refreshes it. Verify perspective scale, lighting and wall/fog occlusion. Confirm indicators, shields, first-person hands, shooting/reload and exit still work. Check atlas warmup and unavailable atlas frames fall back to 3D and repeated entry/exit does not leak materials or destroy game-owned art. Sprite poses are static/sideways cards, equipment extras are omitted, and body geometry remains an approximate combat proxy rather than pixel-accurate sprite collision. Run these checks on both game branches; automated helpers do not certify GPU/atlas behavior.

## Package, input and lifecycle

| Case | Action | Expected result |
| --- | --- | --- |
| Package/load | Inspect the ZIP and launch the extracted mod | About, LoadFolders, both assemblies, README/license/docs present; no bundled game/Harmony dependencies; only the matching RimFront assembly loads; no patch/load errors |
| Eligibility | Select drafted/undrafted, hostile, nonhuman and downed pawns | Entry offered only for eligible drafted player humanlikes |
| Entry and initial click | Choose Play in RimFront, then release and press LMB | Perspective view enters inside the game; entry click never attacks |
| Input ownership | Look, move and use F/V/R/Space | Native controls work without colony shortcuts, gizmos or stock camera handling interfering |
| Pause trigger | Hold LMB across pause/resume; click while paused | No fabricated new press; release/repress rearms firing; paused mode presses are discarded |
| Slow-frame recovery | Hold LMB in Auto/Fixed through a delay over 350 ms, then repeat in Semi-auto/Burst | Stale tick cancels the old attack; Auto/Fixed resumes on fresh held input; Semi-auto/Burst receives no synthetic click and waits for release/repress; pause/mode-switch release gates still apply |
| Firing trace | Enable Diagnostics → Trace firing input and actions; tap/hold/release, pause, change modes, reload and run empty; disable and reenter | Player.log connects observed input transitions to accepted/blocked triggers and subsequent actions with weapon/mode/ammo context; repeated failures are bounded, no per-frame held-input spam, disabling stops trace entries; gameplay is unchanged |
| Focus/dialog/exit | Alt-Tab, open a blocking dialog or Escape during movement, aim, burst and pause | Control releases; camera/cursor/subcamera/background/fire-at-will settings restore; no remaining owned burst fires |
| Windows icon | Compare taskbar icon before/after entry, exit and focus loss | Existing game icon remains intact; no blank/default icon |
| Pawn/map lifecycle | Undraft, down, kill, mental-break, despawn or change maps | Safe release without stale jobs or hooks affecting another pawn |
| External job/teleport | Issue a forced order with target B/C, teleport during reload, test sleep/hauling jobs | Native order survives release; position reconciles; no stranded path request |
| Automatic posture | Reenter repeatedly, finish attacks/reloads and continue moving | Automatic one-tick posture wait does not eject the player; forced/long/sleeping waits retain normal ownership |
| Save/resources | Save while controlling, exit with a projectile in flight, load another save; repeat entry/exit | Possession is not saved; owned shot metadata survives where required; old map, geometry and animation caches clear; memory does not grow continuously |

## Movement and scene

For the native minimap, compare its image with normal colony view: terrain textures, water, snow, plant artwork, walls, rotated furniture, open/closed doors, items, corpses, dressed pawns, native fog, light and visual effects must agree. These must be the game's actual graphics, not colored cells or relationship dots. Check both 1.6 and 1.7 with native and modded graphics, dirty pawn atlas frames and apparel changes. The top-right panel must stay below the status HUD and above the weapon bar across resolutions/UI scales. White marks the controlled pawn with a facing arrow; north stays up. Aim near and then at the farthest selectable point in every direction: retain 25-tile surroundings at refresh and four tiles of target context. Hollow gold means an empty aim point; the line is a selection guide. Obstructed/fogged aim remains constrained by selection; the minimap's native pawn visibility follows colony-view rules rather than first-person line of sight. Outside-map space stays dark; unexplored fog must conceal native art. Confirm updates while paused and after a teleport, and that map rendering never changes the pawn's jobs, simulation speed, FPS camera pose or input. Look for native flat meshes leaking into the first-person scene or first-person geometry entering the minimap, including during pawn-atlas refreshes. Test a dense map and measure the 5 Hz native pass cost. Exit, transfer control, change maps and load another save repeatedly: the panel and its owned camera/target must clear, and normal colony view must restore. Live GPU orientation, visibility, lighting, lifecycle and performance checks remain pending; helper and Harmony-binding tests do not certify them.

For vegetation, orbit trees and bushes at several heights and distances: cards remain upright, face the observer, obey depth/fog and use installed native artwork. Test growth, leafless/immature variants, missing art, dense forests, lamps/night and save changes. Plant hit volumes remain fixed while looking around; transparent leaf pixels do not provide exact collision silhouettes.

| Case | Action | Expected result |
| --- | --- | --- |
| Analog/fallback | Rotate while holding W; disable analog and reenter; test unsupported contract/oversized body | Continuous analog direction or reported native-path fallback, with correct HUD mode |
| Terrain and traffic | Cross terrain costs, snow, dirt, traps, fences and nearby friendly pawns | Native costs/payment, registrations, trap exposure and pawn-specific restrictions remain coherent |
| Corners | Hold diagonal motion against walls/corners at low and high map coordinates; slide and retreat with small input | No tunneling, float-rounding embedding or permanent lock; conservative footprint remains outside blockers |
| Doors | Approach closed/open doors; stop partly through a doorway; test fence gates | Waits for fully open doors; closure waits for body clearance; moving leaves have no permanent static rails |
| Native stagger | Apply stagger and compare travel | Native payment slowdown remains effective; no speed exploit |
| CE coordinates | Shoot and receive fire near cell boundaries; move laterally while another pawn aims | Origins, visible body, collision queries and lead agree with continuous position |
| Static geometry | Build/destroy walls, grow/harvest plants and change resource stacks across 5/6, 25/26, 50/51 and 75/76; split and haul them | Geometry/signatures update within bounded refresh; aim and visible growth steps agree |
| Cover models | Inspect wood/steel/stone barricades and CE embrasures, singly and in runs along both axes; repaint and rebuild them | Braced barricades stay waist-high; material tint is retained; embrasure ports remain open on all four sides; no full-wall fallback fills the ports |
| Resource models | Compare wood, steel and each stone block at 1/25/50/75 units; inspect rough chunks separately | Logs show bark/cut ends, ingots show beveled shoulders, blocks form alternating courses; piles grow within one tile, up to 18 visible pieces; chunks stay distinct from manufactured blocks |
| Embrasure targeting | Aim and fire a supported straight vanilla/CE ballistic shot through a port at standing height, then at the base, corner pier and lintel | Port selects geometry beyond it; solid frame remains targetable; record native accuracy/cover outcomes separately from the rendered opening |
| Depth/fog/cache | Look vertically, approach walls, cross chunk borders and face fog | Perspective/depth are correct; no flat-map duplicates, missing near scenery or exposed hidden objects |
| Render coexistence | Add other camera/visual/movement mods | Record conflicts and repeated errors; do not assume universal compatibility |

## Carried bipods and weapon models

| Case | Action | Expected result |
| --- | --- | --- |
| Key separation | Use F, then V on an M60/M249/PKM | F cycles native fire mode; V deploys the carried bipod without changing fire mode |
| CE timed deployment | Enable CE bipod mechanics, press V while stationary, wait | Native setup progresses without leaving first person; deployed pose starts only after CE completes the job |
| Fold and auto-setup | Press V again with CE auto-setup enabled | Native fold occurs; automatic setup does not immediately undo the explicit fold; other colonists still use native auto-setup |
| Cancellation | During setup press V or move, then wait; repeat with weapon swap, stale input and exit | Exact setup ends, no later deployment or stale mount, movement resumes |
| CE settings | Disable CE bipod mechanics, then press V with a bipod weapon | Clear feedback; no native deployment or stat changes |
| Trigger and reload | Hold LMB while pressing V, then release/repress; reload while deployed | No queued surprise shot from V; fresh click fires; native reload timing/ammunition retained |
| Non-CE long gun | V with rifle, shotgun, SMG, sniper or launcher; move | Stationary brace and visible legs; movement releases; no added gameplay stat bonus |
| Weapon appearance | Equip M60/M249/PKM/MG42/RPK/DP28/Lewis/M240/M1919 variants; fold, deploy, reload and drop | Correct receiver/feed/stock silhouette in first-person, third-person and dropped rendering; bipod legs match state; reload parts remain attached correctly |
| Lifecycle restore | Leave possession after V fold, then issue normal colony orders | Native CE automatic policy resumes; no effects on other pawns or weapons |

## Firing, targeting and repeat cycles

| Case | Action | Expected result |
| --- | --- | --- |
| Empty point/objects | Shoot visible ground, sky, walls, furniture, plants and small items | Native shots occur without a hostile target; closest displayed geometry is selected |
| Friendly fire | Deliberately target colonists, allies, guests and prisoners | No hostile-only filter; native damage/faction consequences apply |
| Occlusion/height | Aim through open doors, above low cover and behind walls | Foreground surfaces win; actual visual gaps remain aimable; supported height-aware shots reject empty space |
| Lying/animated bodies | Aim at moving limbs, head/feet of downed bodies, and sleepers in all bed rotations; aim below a mattress | Selection follows rendered pose/detail and raised/rotated body bounds, not surrounding empty space |
| Native restrictions | Test empty ammo, nonviolent pawn, shields, minimum range and overhead restrictions | Native restrictions hold; declined requests do not create recoil or shots |
| Warmup/burst aim | Change crosshair during warmup and burst, including near-to-far targets | Owned shots follow current aim with native spread; unrelated shooters keep normal targeting |
| Invalid aim | Sweep suppressive CE fire into invalid/fog boundaries | No stale-line shot or ammunition preparation after invalidation |
| Close/endpoint blockers | Close a door in a slow bullet's path, within five cells and at the target cell | Nearer solid obstacle stops the shot before a farther pawn/item; shields and native chance-based cover remain respected |
| Fractional diagonals | Fire obliquely through offset cells toward cover and a farther target; repeat reverse direction | Crossed cells are inspected in physical order; no diagonal gap or farther-first hit |
| Visible CE surfaces | Hit upper wall surfaces, tall props, limbs and dropped items | Owned ballistic contacts match displayed geometry; native accuracy, shields, armor and plant chance remain authoritative |
| Passed target | Move an intended target backward behind a slow vanilla bullet after it passes | Endpoint handling cannot damage the already-passed target |
| Unsupported trajectories | Test explosive/arcing/custom vanilla projectiles and CE beams | Documented native fallback applies; no claim of exact vertical contact for unsupported types |
| XP/save scope | Shoot a pawn then an empty cell; save/load a supported bullet in flight | Native target bookkeeping is correct; owned metadata does not affect unrelated projectiles |
| Native mode cycle | Press F with different CE mode subsets, fixed guns and melee | Only supported native modes cycle; HUD/save state agree; fixed/melee cannot acquire invented modes |
| Trigger semantics | Tap/hold in Semi-auto, Burst and Auto | One shot per click, one completed burst per click, or repeated held Auto respectively; native cadence/ammo retained |
| Mid-attack switch | F during warmup, burst and cooldown, and during reload | Owned attack cancels without shortening cooldown; held trigger requires release/repress; native reload continues |
| Short click/cancel/stall | Tap fire; move or press R during warmup; attempt an impossible shot | Tap retains first warmup/shot; movement/reload cancels explicitly; stalled attack releases without indefinite aim |
| Held Auto continuation | Hold LMB through several bursts and cooldown/aim intervals | Real rounds and recoil resume each cycle; completed cleanup preserves CE repeat-shot timing; no permanent aiming state |
| M60 bipod | Press G idle, during setup, during sustained fire and during reload; repeat after moving and re-entering possession | HUD toggles ON/OFF once per press. OFF closes/cancels setup without exit/restart; move and fire normally. ON allows CE setup under its own settings. Native reload finishes; no extra Semi/Burst click. External/forced jobs still release normally |
| LMG shot origin | Fire M60/LMG upright, up/down, with recoil and at fractional-cell positions; compare nearby/far targets and another pawn's fire | Projectile begins at displayed barrel (clipped safely near cover); rotation/elevation converge consistently. Other pawns remain native; ammo and cadence match CE |
| LMG close cover | Put the muzzle beyond a wall/closed door/low cover, aim at a near surface, then repeat at map/fog edges | Shots cannot skip the surface between camera and muzzle; failed clearance uses eye position. Test open door and a visible clear barrel too |
| Bipod input lifetime | Press G while paused, lose focus, exit/re-enter, swap to non-bipod/melee weapon | Paused/stale presses never replay. New possession starts ON. Unsupported weapons give feedback without changing jobs |
| Setup input | Hold LMB/WASD or press R/F during setup; release before completion | Setup finishes without replacement; queued intent follows mode/release rules afterward; no fabricated shot |
| Reload continuation | Partly/fully empty M60; hold Auto through reload | Native reload finishes, eligible held fire resumes, and animation stays shot-driven |
| Automatic last-round reload | Empty magazines in Auto, Semi-auto and Burst; repeat with no reserve ammo and after supplying ammo | No return to colony view for a resumed AttackStatic; no phantom Semi/Burst trigger; held Auto resumes only with valid current input |
| Reload before first shot | Trigger a native reload before the accepted click can fire; release the mouse during reload | One accepted initial shot can resume after reload using current aim; movement, pause, focus loss or switching modes cancels pending intent |

## Models, equipment and reload presentation

| Case | Action | Expected result |
| --- | --- | --- |
| Appearance | Compare adult shapes/child, species families, supported garments and unknown modded definitions | Coherent generic models with actual colors/scale; changes invalidate at bounded cadence |
| Anatomy | Remove/replace limbs and equip weapon/shield combinations, including missing right arm | Supported missing limbs disappear; prosthetics visible; weapon and shield do not occupy the same unavailable hand |
| Attachment/detail | Aim, reload and melee with animation on/off, beyond 14 cells and above detail budget | Grip stays attached; ranged barrels/muzzle follow aim at each detail level |
| Stow/identity | Undraft sword, pistol, rifle and physical-shield carriers; compare energy/unrelated belts | One-handed hip and two-handed/back placements; physical shields only from real ownership; belts do not become boards |
| Beds | Use both double-bed slots in every rotation and varied body scale | Body follows correct slot/head end and remains above mattress; ground lying stays separate |
| Real job animation | Observe walking, work, ingestion, carrying, reload, injury and lying/dead states | State-driven finite poses; pause freezes phase; incompatible action poses suppressed |
| First-person hands | Change clothing/arms/equipment and reenter | Hands/sleeves/weapon reflect appearance and remain attached |
| Reload families | Reload rifle, revolver, shotgun and launcher | Magazine/cylinder/shell/bolt/hands follow actual toils; single-round insertion loops correctly |
| Reload interruption | Pause, cancel, change weapon, exit/reenter; test rejected reload | Pose freezes/clears correctly; no fabricated ammo/completion or stale parts; unknown timing uses lowered pose |

## Effects, indicators and performance

| Case | Action | Expected result |
| --- | --- | --- |
| Actual-shot effects | Observe multiple bursts, warmup, empty magazine, pause and reload | Recoil/flash restart for real shots only; final shot settles; bow/grenade has no gun flash |
| Trajectories/materials | Fire ballistic/beam weapons at metal, wood, stone, soil, water and plants | Observed trails and impact category agree; cosmetic fragments do not add damage or ricochets |
| Blood/cleaning | Injure/fatally hit bleeding races; block hits with armor/shields; hit mechs; tend and clean | No fabricated blood from nonbleeding/fully blocked hits; native blood colors/filth and cleaning respected |
| Blood settings | Toggle splashes, gore, trails and impacts independently | Presentation follows options; damage/bleeding unchanged; no extra persistent filth |
| Shield contacts | Weak/strong bullets, physical parry and melee an active belt | Blue/red echoes follow real block/contact, expire in 18 ticks and stay within 10 cm horizontal/2 cm vertical total separation; belt gains no melee protection |
| Shield lifetime | Toggle haze/ghosting/colony effect; pause and switch controlled pawn; exit mid-impact | Correct settings and map scope; paused takeover refreshes haze without resetting impact age; optional colony echoes remain correctly oriented |
| Indicators | Compare colony, animal, trader, hosted guest, prisoner and prison break; crowd/paused map | Correct labels without same-faction errors; orange/red revolt flash; walls/fog occlude; bounded batches do not blink away |
| Light/weather | Compare day/night, roofs, colored lamps, flashes, rain and snow | Native sky/glow/weather drive effects; no neighboring-cell light leak; flash clears promptly; precipitation stays outside roofs/fog |
| Snow/celestial cue | Compare actual snow depths and dawn/noon/night/weather | Snow tint follows native grid; simple sun/moon cue is occluded and weather-aware |
| Load and disposal | Sustain effects, walk across map, pause/toggle effects, switch save and reenter | Pools/caches stay bounded; old resources expire/clear; no repeated errors |
| Performance | Follow [performance.md](performance.md) in crowded combat and cold/warm scenes | Record live frame times, simulation speed and input delay; offline tests/catalogs do not establish FPS |

Retain failed cases as reproducible issues. Passing startup or a small subset does not mark the whole matrix compatible.

## Mountains and view range

- Enter near a small outcrop and a broad natural-rock/ore formation. Confirm the larger dense mass is taller, rock colors remain recognizable, constructed walls stay flat, and no cracks appear across 12-cell chunk borders.
- Mine a visible edge and an interior tunnel; reveal fogged rock. Allow the bounded chunk rebuilds to settle. Confirm the silhouette updates, removed rock opens the passage, fog still hides underground cells and ore types beneath a uniform mountain exterior, and neighboring chunks join.
- Aim at upper slopes and fire supported vanilla/CE ballistic weapons. Crosshair and contact geometry should follow the visible mountain; native weapon limits, roofs and movement remain authoritative.
- Test View range at 0%, 50%, 100% (60, 90, 120 tiles), including changes during possession. Check terrain, doors, distant pawns, vegetation, labels and effects; verify targets beyond 30 tiles can be selected within native weapon range and fog still blocks selection.
- Face diagonally and walk across chunk boundaries at every slider stop. Check distant geometry after the cache warms. Shrink the slider and verify excess chunks leave the cache. Save/restart to verify the slider persists, and exit possession to verify the original camera clip plane is restored.
- Measure cold/warm frame times and memory at each range on both 1.6 and 1.7. The default cache can take 121 frames to fill; the largest can take 441. This PR has no live GPU or gameplay certification.

- During ongoing movement/cache expansion, mine rock near the observer. Confirm direct edits update before distant missing chunks, and aim/projectile contacts follow each displayed slope throughout the remaining rebuilds.
- Aim above fog at a mountain exterior. Confirm the crosshair reaches the slope and labels it only as a mountain; low rays still stop at fog and never identify hidden ore, pawns or objects.
- Move through dense vegetation at 120 tiles with more than 8,192 candidates in range. Confirm nearby plants remain visible when later chunks are visited. The 4,096-card presentation limit remains; this check specifically covers nearest-candidate retention.

## Moving fire and crouching

- With Move while firing enabled (default), walk/strafe during warmup, a full burst and sustained Auto fire. Check 50% movement speed in both analog and native path modes. Semi/Burst must still require a new click; releasing WASD stops movement without resetting warmup.
- Disable the option during moving warmup: the attack must cancel immediately. Re-enable and confirm held Auto recovers. R reload, G auto setup and V brace retain their existing ownership and stationary behavior. Stun, pause, stale input, external jobs and exit must not permit movement/fire leakage.
- Hold Left Ctrl behind stock barricades and sandbags from both sides, then release. Check camera, hands, muzzle and body height agree. Crouch walking is half speed, or quarter speed while firing. Repeat with small/large humanlike body sizes, native path mode and CE enabled/disabled.
- Have an enemy fire stock straight vanilla bullets and CE projectiles at the crouched pawn. Confirm cover intercepts low frontal shots and standing restores the original target height. Repeat crouching after an incoming shot is already in flight. Check flanking/high shots and open ground still cause native damage; explosive/custom weapons retain native trajectories.
- Exit/reenter while crouched and while firing; confirm standing posture and normal movement speed return. Repeat pause/focus-loss/dialog checks. Automated geometry coverage is not a live combat or GPU certification.
