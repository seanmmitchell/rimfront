# Native in-game smoke checklist (0.5.8)

The native cases below have not been executed in a RimWorld session. Close the game, install the new DLLs, restart, and use a disposable colony. Run separately on 1.6 and 1.7, first with Harmony only, then with an appropriate CE build. Record exact versions and the player log.

| Case | Action | Expected result |
| --- | --- | --- |
| Load | Enable dependency order and launch | No missing assembly or Harmony patch errors; only the selected game version's RimFront assembly loads |
| Eligibility | Select drafted, undrafted, hostile, nonhuman, and downed pawns | Entry appears only for eligible drafted player humanlikes |
| Native entry | Choose Play in RimFront | View stays inside RimWorld, perspective camera enters at pawn eye height, no browser opens |
| Input ownership | Look and press WASD while controlling | Camera rotates smoothly, game UI does not consume gameplay keys or pan the stock camera |
| Initial click | Enter using the gizmo | Entry click does not also shoot; releasing and pressing LMB fires normally |
| Pause | Press Space, move/look/fire, press Space again | Simulation actions pause; looking remains possible; movement/combat resume normally |
| Movement collision | Move across terrain, walls, doors, map edges, obstacles in both modes | Native movement costs affect speed; neither mode crosses blocked terrain or teleports |
| Object coverage | Inspect forest, room furniture, items, corpses, animals, modded buildings | Nearby geometry appears in proper positions and rotations; generic fallbacks are legible |
| Depth and camera | Look up/down and move beside a wall or tall tree | Correct perspective and occlusion; no inverted faces or flat-map duplicates; clipping is acceptable |
| Chunk boundaries | Walk across several chunk borders | No missing scenery within the configured view range; cache remains bounded; no recurrent spikes |
| Live scenery | Open doors; build/destroy a wall; move/change an item stack; grow/harvest a plant | Door motion follows native state; static changes appear within the refresh cycle |
| Fog | Look toward unrevealed terrain | Opaque fog geometry does not expose hidden objects or pawns |
| Aim | Aim at human, sky above human, nearby animal, behind cover, nonhostile pawn | Crosshair selects the nearest visible target of any faction or an empty aim point; native weapon restrictions remain authoritative |
| Burst/cooldown | Hold fire on automatic and slow weapons | Normal warmup, burst count, cooldown, and ammo consumption; no speed exploit |
| CE magazine | Fire partly loaded and empty weapons | Ammunition state/HUD match CE; empty weapon cannot fire |
| CE reload | R with reserve ammo, no reserve, full magazine, during busy stance/reload | Native reload time and consumption preserved; no duplicated ammo or repeated forced restart |
| Focus/modal | Alt-Tab or open a blocking dialog | Control releases; cursor, camera, UI, and temporary settings restored |
| Windows taskbar icon | Note RimWorld's taskbar icon, enter/exit native view, Alt-Tab, return and reenter | Existing game icon remains unchanged throughout; no blank/default icon after focus loss or release |
| Escape | Exit after moving, aiming, firing, and pausing | Colony view restores camera position/projection, cursor visibility/lock, subcameras, background setting, and fire-at-will |
| Pawn lifecycle | Undraft, down, kill, mental-break, despawn, transfer maps | Control releases safely without stale jobs affecting another pawn |
| Save lifecycle | Save while controlling, load another save, return to menu | No saved possession or references crossing game sessions; later entry works |
| Resource lifecycle | Enter/exit repeatedly and walk across a large map | Old meshes/materials are disposed; memory does not grow continuously |
| Render coexistence | Add another camera/visual mod plus CE | No repeated errors; document conflicts rather than assuming universal compatibility |
| Performance | Run the comparison in performance.md | Record warm/cold FPS and simulation speed; do not infer live improvement from standalone tests |

The HUD's FPS estimate and draw/cache counters can help spot problems, but use consistent measurement conditions for comparisons. Unsupported custom motes remain a visual limitation; observed projectile trails must agree with native simulation results and sounds.

Inspect the ZIP: About, LoadFolders, both version assemblies, README/license/docs present; no browser assets or game/Harmony DLLs. Test an extracted package, not only the developer build. Record reproduction steps and expected/actual behavior for each failure.

## Version 0.3 additions

| Case | Action | Expected result |
| --- | --- | --- |
| Weapon families | Equip pistol, SMG, rifle, sniper, shotgun, launcher, bow, melee, and a modded gun | Distinct bounded silhouettes appear in first person/on pawns/on the ground; unknown definitions use a coherent fallback |
| Real-shot effects | Click while empty, paused, blocked, warming up, and during a burst | Recoil and muzzle flash occur for observed shots, not declined click intents; bow/grenade has no gun muzzle flash |
| Trajectory view | Fire vanilla and CE ballistic weapons; test a CE beam | Trails follow observed positions/endpoints; supported reflected beam segments are shown without extra damage or bounces |
| Impact types | Hit ground, stone/metal, flesh, and armor that actually deflects | Cosmetic sparks/dust/deflection cues match observed impact category; gameplay damage remains native |
| Effect lifecycle | Sustain fire, pause, switch maps, exit/reenter | Bounded pools expire and clear; no old effects cross saves/maps or keep allocating forever |
| Day/night | Compare noon, dusk, midnight and a roofed room | Sky/roof/game glow drive brightness; ambient silhouette floor remains readable |
| Colored lamps | Toggle powered lamps on/off and change their color | Nearby surfaces update; walls/floor faces do not sample unrelated neighbor cells |
| Flash lighting | Fire near a wall in darkness and stop | Short muzzle illumination appears and returns promptly to the normal light level |
| Rain/snow | Use genuine rain and snow weather, step indoors, pause | Bounded precipitation follows actual rates, stays outside roofs/fog, freezes falling motion when paused |
| Ground snow | Compare cells with different actual snow depths | Floor whitening reflects the saved snow grid; no snow is created by the renderer |
| Sun/moon cue | Observe clear dawn/noon/dusk/night and dense precipitation | Simple cue follows local time, is depth-occluded, and hides in dense rain/snow; not an exact astronomy model |
| Performance | Repeat old measurements with sustained effects and weather | Record real frame times; capped pools and batching alone do not prove performance |

## Version 0.4 additions

| Case | Action | Expected result |
| --- | --- | --- |
| Analog default | Enter with default settings and rotate while holding W | HUD shows Analog; continuous direction follows view without eight-direction snapping |
| Native fallback | Disable True analog movement in RimFront mod settings, exit and reenter | HUD shows Native path; look-ahead movement uses regular pathfinding |
| Unsupported contract/body | Use an unsupported CE contract or a pawn wider than the analog limit | Entry reports native fallback reason; no partial analog ownership |
| Swept footprint | Move diagonally into a wall, a closed door, a tight corner, and nearby pawns | No tunneling or corner cutting; sliding stays outside blockers; door waits until fully open; pawn traffic follows native collision rules |
| Door body clearance | Stop while partly through a door, then move clear | Door stays open while body overlaps and can close afterward |
| Cell callbacks | Walk through snow, dirt, trap cells, terrain-cost changes, and a fenced region | Cell registrations, native trap exposure, snow/filth effects and pawn-specific restrictions remain coherent |
| Analog combat coordinates | Fire near a cell edge; receive CE bullets and beam shots across adjacent cells | Origins and visible body agree; CE collision can find the continuous body in neighboring queried cells |
| CE lead | Move laterally at several angles while a CE pawn aims at the controlled pawn | Lead follows actual velocity; stationary controlled pawn has no fabricated path-center prediction |
| Job/lifecycle | Start an external order or teleport, pause, reload, enter a busy stance, release | External state takes precedence; busy/reload stops motion; no stale pather suppression after exit |
| Stagger and forced jobs | Apply native stagger; issue a forced job with a target B/C destination; teleport during reload | Native movement payment preserves slowdown; forced path request survives release; camera/hitbox reconcile immediately |
| Reload model parts | Reload magazine rifle, revolver, shotgun, and launcher | Separate feed, bolt and hand poses follow actual reload timing; assembled gun returns afterward |
| Reload pause/cancel | Pause halfway, cancel the job, change weapon, exit/reenter | Pose freezes on game ticks, clears on cancellation, and never leaves old parts attached |
| Per-round loading | Reload partly filled shotgun/revolver, finish or cancel early | Each real insertion drives a cycle; cylinder stays present; bolt/pump is not cycled after every intermediate round |
| Rejected reload | Press R without reserve ammo, at full capacity, during busy stance | No fabricated completion or ammo change; animation appears only for an actual supported reload job |
| Responsiveness | Compare both movement modes with the prior version in the same scene | Record input-to-motion and frame times; do not label lag fixed without measurement |

## Version 0.4.1 regressions

| Case | Action | Expected result |
| --- | --- | --- |
| Automatic posture transition | Enter repeatedly, end an attack/reload, then keep walking | Native automatic one-tick Wait_MaintainPosture does not return to colony view |
| Real external job | Issue an external order, forced/sleep posture, recovery or hauling job | Native job receives control; no stranded path request or broad wait-job exemption |
| Float corner contact | Hold diagonal motion into inside/outside corners at low and high map coordinates, then move away | No one-ULP penetration, persistent corner lock, or travel through blocked cells |
| Tiny input and retreat | Apply small motion along a wall and away from a blocked corner | Collision remains finite and bounded; tangential/retreat motion remains possible |

## Version 0.5 visual coverage

| Case | Action | Expected result |
| --- | --- | --- |
| Human body types | Compare adult body types and a child at the same location | Distinct silhouettes, coherent proportions, and game-driven scale; camera/aim bounds remain consistent |
| Apparel layers | Equip/remove shirt, pants, jacket, armor, helmet and a modded garment | Matching body regions gain/remove geometry and color; layers do not remain stale after equipment changes |
| Anatomy | Compare missing and prosthetic limbs, equipped and bare hands | Visible anatomy follows supported body-part state; no duplicate arms/legs or detached weapon |
| Creature families | Observe quadruped, bird, insect, serpent, mech and unfamiliar creature definitions | Family-specific models appear, with a generic fallback for unsupported definitions |
| Gait and pause | Observe idle, walking and running pawns; pause mid-step | Limbs reflect real motion and pause freezes the animation phase; stationary pawns do not continue walking |
| Combat animation | Observe aim/fire/reload and downed/dead poses | State transitions remain finite and coherent without changing game attack/reload timing |
| First-person clothing | Change clothing/equipment, enter view and reload | Visible hands/sleeves/weapon use the current appearance and stay attached during actions |
| Visual cache | Change apparel/hair/body state, exit/reenter, change maps/save | Models invalidate when appearance changes; caches remain bounded and old session resources are released |
| World props | Inspect new object families and unknown modded props | Distinct recognizable silhouettes for supported categories; generic fallbacks remain visible |
| Performance | Compare a crowded mixed colony before/after warm cache, with moving pawns | Record actual draw/frame costs; offline model catalog and helper tests do not establish runtime performance |

## Version 0.5.1 regressions

| Case | Action | Expected result |
| --- | --- | --- |
| Fence gate | Open and pass through a fence gate in native view | No permanent static rails remain across the opening |
| Resource quantities | Increase/decrease wood, metal and stone stacks across 25, 50 and 75 items | Visible piece counts update after chunk refresh |
| Bed alignment | Sleep in both double-bed slots with each bed rotation | Body follows its actual slot and head end; mattress remains below the body |
| Attached weapon | Observe idle, aiming, melee and reload with animations enabled/disabled | Grip stays at the available hand through animation and distance changes |
| Stowed equipment | Undraft a sword/pistol pawn, rifle pawn and shield carrier, including during a native combat job | One-handed weapon at hip; two-handed weapon and physical shield on back; drafting restores held equipment |
| Shield identity | Compare physical CE shield, energy shield belt and an unrelated belt | Only an actually owned physical shield creates a shield-board model |
| Shield-belt effects | Compare active, depleted and resetting belts; absorb a real hit and pause | Faint haze follows active protection; impact echoes follow real blocks and pause with game ticks |
| Colony shield option | Toggle colony-view shield impacts, leave native view and observe an actual block | Impact rendering follows its setting independently of native-view lifetime; no duplicate damage or fabricated block |
| Native melee selection | Aim away from, then at, adjacent hostile targets with blocked and usable attacks | Geometry-rejected targets do not consume native verb selection; nearest aimed target receives the native attack if usable; a rejected foreground target does not expose one behind it |

## Version 0.5.2 regressions

| Case | Action | Expected result |
| --- | --- | --- |
| Barrel alignment | Observe ranged fire with animations disabled, beyond 14 cells, and in a crowd exceeding the animation budget; repeat with a missing right arm | Barrel and muzzle effects follow aim at each detail level, with the grip still attached |
| Lying targets | Aim at the head and feet of a sideways downed hostile, then a sleeper in each bed rotation; aim below the mattress | Visible body extent can be selected; empty space below a raised body cannot |
| Hand allocation | Draft a pawn missing its right arm with a one-handed weapon and physical shield; enter first person | Weapon uses the left hand in both views; shield stays on the back; undrafting keeps normal stow placement |
| Paused shield takeover | Pause, leave native view, then take over a shielded pawn more than 30 cells away on the same map; also reenter the same pawn | Active haze appears without advancing ticks; existing impact echoes retain their age |

## Version 0.5.3 blood and impacts

| Case | Action | Expected result |
| --- | --- | --- |
| Real injuries | Observe ranged and melee injuries to human and animal pawns, including a fatal hit | Blood appears for blood-producing injuries; severe hits can add cosmetic tissue fragments |
| Blocked/nonbleeding hits | Hit an absorbing shield, armor that fully stops damage, and a mech | No fabricated blood from blocked hits or nonbleeding targets; shield/solid impact effects remain appropriate |
| Bleeding trail | Move a wounded bleeding pawn, let it rest, then tend the wound | Droplets follow actual bleeding callbacks; native blood filth becomes visible stains; tending changes bleeding through native rules |
| Blood cleanup | Clean a bloody floor and compare existing stains after leaving/reentering native view | Native stains follow actual filth state; cosmetic transient effects are not saved as extra filth |
| Race blood colors | Compare supported race/mutant blood definitions | Effects and stains use the native blood color where available |
| Surface materials | Observe shots hitting metal, wood, stone, soil, water and plants | Material-appropriate sparks, chips, dust or splashes; contact marks are temporary and bounded |
| Pause and speed | Pause mid-impact and resume at different simulation speeds | Transient effects freeze while paused and age with game ticks |
| Settings | Toggle blood splashes, gore, trails and surface impacts independently | Each group follows its option; disabling gore leaves blood available; no setting changes damage or bleeding |
| Crowded combat | Observe sustained automatic fire and many bleeding pawns, then change map/save | Pools remain bounded, old effects expire, session resources clear; record actual frame time rather than inferring it from offline tests |

## Version 0.5.4 free fire and indicators

| Case | Action | Expected result |
| --- | --- | --- |
| Empty ground | Aim at visible empty terrain and hold fire with an available weapon | Native shots and ammunition consumption occur without requiring a hostile pawn |
| Objects and cover | Aim directly at walls, doors, furniture, plants and items | Crosshair selects the nearest displayed geometry; native collision and damage decide the outcome |
| Friendly fire | Deliberately aim at a colonist, ally, guest or prisoner in a disposable test save | No hostile-only target filter; native damage and faction consequences remain authoritative |
| Occlusion | Put a pawn behind a wall, then aim above low cover and through an opened door | Foreground geometry prevents selection behind it; open visual gaps remain aimable |
| Vertical aim | Fire upward and downward with CE and a supported stock vanilla bullet | Projectile presentation and eligible collisions follow shot height; unsupported vanilla trajectories are explicitly identified |
| Warmup and bursts | Begin firing, then move the crosshair during warmup and an automatic burst | Owned manual aim follows the current crosshair without affecting other pawns' targeting |
| Weapon restrictions | Try empty magazines, reloading, nonviolent pawns, shields, minimum range and obstructed overhead weapons | Native weapon restrictions remain in force and failures report a reason |
| Indicators | Compare colony pawns, allies, guests, ordinary prisoners and an active prison break | Distinct visible markers; prison-break markers flash orange/red and freeze while paused; walls/fog hide markers |
| Scope and cleanup | Leave native view with a bullet in flight; save/reload during flight; watch unrelated native shooters | Owned shot state survives as required; no manual-aim override applies to unrelated shooters |



## Version 0.5.5 review fixes

| Check | Action | Expected |
| --- | --- | --- |
| Own-faction markers | Keep several colonists in view and inspect Player.log | Colonist labels render without same-faction relation errors |
| Real status | Observe a trader, hosted guest, colony animal and prisoner | Neutral, Guest, Friend and Prisoner reflect actual native status |
| Crowded paused map | Enter view paused with more than 512 spawned pawns | Bounded scan completes across frames; previous batches do not blink away |
| CE burst distance | Sweep a burst from a near object to a distant one | Later rounds use the new point distance with native range error and spread |
| CE invalid aim | In suppressive mode, aim toward an invalid/fog boundary during a burst | No stale-line shot or ammo preparation after invalidation |
| Vanilla obstruction | Fire a slow straight bullet, then close a door in its path | Native interception sees the door; no shot through a new full blocker |
| Vanilla small object | Fire native successful shots at a small dropped item | Cosmetic destination jitter does not introduce an extra miss |
| Save switch after exit | Exit view with bullets in flight, let contacts occur, then switch saves | CPU geometry, pawn bounds and animation caches release old references |
| Passed target | Move the intended target backward along a slow vanilla bullet path after it passes | Final impact cannot damage a pawn behind the final movement segment |

## Version 0.5.6 combat regressions

| Case | Action | Expected result |
| --- | --- | --- |
| Shield impulse | Shoot active energy shields with weak/strong weapons; melee an active belt and parry with a physical CE shield | Blue/red echoes appear on actual block/contact, settle within 18 ticks and remain within 10 cm horizontal / 2 cm vertical total separation; misses produce none |
| Shield damage | Melee an active energy belt with and without armor absorption | Cosmetic contact appears; native injury/armor outcomes remain unchanged and the belt gains no new melee protection |
| Shield settings | Toggle ghosting and colony-view effects; leave FPS during an impact | Disabled effects stay hidden; opt-in 2D echoes remain on the overhead plane |
| Burst cancellation | Release LMB after first round; press Escape or replace the job mid-burst | No remaining owned burst fires after cancellation; genuine cooldown is retained |
| Warmup / stall | Tap fire; press WASD or R during warmup; attempt an impossible shot or empty magazine | Tap preserves first shot; movement/reload cancels warmup; no indefinite attack state or stuck firing pose |
| Close / endpoint obstruction | Shoot through a door that closes in flight, including within five tiles and an item in the doorway | The nearer closed door intercepts; no bullet skips it to damage the object behind |
| Animated aim | Aim at a moving extended arm and at empty space beside it, near and beyond animation detail range | Selection matches the drawn pose and does not treat surrounding empty space as flesh |
| Plant growth | Aim at a growing tree across a mesh refresh boundary | Visible and selectable foliage use the same growth step |
| CE visible surfaces | Shoot the upper section of a 2.4-tile wall, tall furniture, a pawn limb and a dropped item | Owned CE ballistic projectiles use the displayed geometry; a nearer solid obstacle wins; native shield, armor, accuracy and plant interception remain authoritative |
| CE skill bookkeeping | Fire at a pawn and then at an empty cell | Native Shooting XP and battle-log targeting see the selected pawn without retaining a stale target for the next shot |

These are required live checks, not claims of completed gameplay validation.

## Version 0.5.7 fire modes

| Case | Action | Expected result |
| --- | --- | --- |
| Native cycle | Equip CE weapons supporting different subsets; press F repeatedly | Native mode order wraps through only that weapon's modes; HUD shows Semi-auto, Burst or Auto; selected mode persists through CE's normal save/weapon state |
| Trigger behavior | Hold and tap LMB in each mode | Semi-auto fires once per click; Burst completes one native burst per click; Auto repeats while held; ammunition and cadence remain native |
| Mid-attack switch | Press F during warmup, between burst rounds, and during cooldown | Old owned attack cancels; cooldown is not shortened; new mode fires only after releasing/repressing the trigger |
| Reload / switch | Press F during a CE reload | Mode changes without restarting/cancelling the native reload or changing ammo |
| Pause / stale input | Hold fire across pause/resume; click while paused; stop updates long enough to trigger stale-input protection | No new shot starts until a real release/repress; mode presses while paused are discarded |
| Fixed / melee | Press F with a single-mode CE gun, vanilla gun, or melee weapon | No invented modes or errors; HUD reports native mode, Fixed or Melee |
| Exit | Press F then immediately exit or lose focus | No queued mode change or shot leaks into colony control |

Not yet validated in a live game session.
## Version 0.5.8 M60 reload and held fire

| Case | Action | Expected result |
| --- | --- | --- |
| M60 bipod setup | Enter RimFront with a CE M60/bipod weapon; let CE set up before/after firing and reload | Camera stays in RimFront; native setup completes once without a restart loop |
| Input during setup | Hold LMB, WASD, or press R while bipod setup is active | Setup owns the stationary service interval; queued reload/fire resumes appropriately after completion; no movement or new attack replaces the setup job |
| Automatic continuation | In Auto mode, hold LMB through several bursts and native cooldown/aim intervals | Real rounds and recoil resume after each valid interval; no permanent aiming pose; native cadence and ammo remain unchanged |
| Reload continuation | Reload the M60 after a partial and empty belt while holding LMB | Reload completes normally and eligible automatic fire resumes; no exit to colony view or fake shot animation during reload |
| Release / mode semantics | Release during setup/reload, then test Semi-auto and Burst | Released automatic trigger does not restart firing; Semi-auto/Burst still require their per-click intent |
| Native ownership | Issue an unrelated forced order, down/undraft pawn, or Escape during setup | Normal control release remains intact; unknown jobs are not silently allowlisted |

Run with an actual M60 and the installed CE build; standalone tests do not establish live gameplay success.