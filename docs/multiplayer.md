# Multiplayer development integration

This is draft compatibility work, not a multiplayer certification. Both game targets
compile and the portable suites exercise synchronized possession and the production
firing controller. The four live two-client combinations below remain required.

## Install for testing

Use identical game, RimFront, Harmony, Multiplayer, CE and mod settings on both clients.
Run a disposable colony. No dependencies or game assets are distributed with RimFront.

| Game | Multiplayer | Package ID | Required dependency |
| --- | --- | --- | --- |
| 1.6 | [Upstream 0.11.5 / 4a3be27](https://steamcommunity.com/sharedfiles/filedetails/?id=2606448745) | `rwmt.Multiplayer` | Its declared Prepatcher dependency |
| 1.7 | [Community 0.11.5-rw17 port](https://steamcommunity.com/sharedfiles/filedetails/?id=3820883853) | `li.multiplayer17` | Harmony; this port removes Prepatcher |

Enable only the Multiplayer build for your game branch. Load it after Core/DLC and
before RimFront. The port and upstream share the `Multiplayer` assembly name.
RimFront checks the branch-specific Multiplayer, common and API binary hashes, then
the reflection contracts. An unknown binary disables possession with a reason.
Multiplayer installed without an active session keeps single-player behavior.
Separate player factions and dedicated-server hosting are outside this integration.

The downloaded 1.7 port's author explicitly leaves long-term stability and desync
detection unverified. Its availability and matching signatures do not establish a
working RimFront/CE multiplayer combination.

## Controls and shared rules

Each player can possess one drafted eligible pawn. Different players can control
different pawns on the same or different maps. First accepted claim wins; a rejected
transfer retains the requesting player's old pawn. Colony orders release possession
and continue. Camera, cursor, minimap, graphics options and diagnostics stay local.

Mouse look is immediate. Movement and combat wait for synchronized input; there is
no movement prediction. Continuous samples are capped at 20 Hz. Trigger/action edges,
movement releases and possession releases bypass that cap. Inputs carry the current
possession generation and a monotonically increasing sequence; stale or foreign
commands cannot refresh the lease or repeat an action. Discrete actions have a bounded
32-entry queue; overflow releases possession. Actions are drained in order on map ticks.

The lease expires after 120 owning-map simulation ticks without accepted input.
The local 350 ms stale detector sends an explicit stop that does not manufacture a
new Semi-auto/Burst click. Escape, focus loss, blocking dialogs and local map changes
restore the local camera immediately and request synchronized release. A host command
releases disconnected players and controllers whose maps disappeared. Space uses
Multiplayer's own pause-vote mechanism. Paused trigger input requires physical release.

The host controls analog movement, moving while firing, moving while reloading and
view/target range. Clients see those rules read-only. These session rules do not
overwrite personal single-player defaults. Analog-mode changes affect new possessions;
moving-fire/reload and range changes apply to active controllers.

## Simulation and snapshots

Controllers run in pawn-ID order before native pawn work on each map tick. Movement,
posture, bipod policy and manual aim are per pawn. Job hooks resolve the affected pawn;
opening a camera is not required for gameplay patches to apply.

Multiplayer uses canonical CPU standing/lying body geometry with authoritative yaw
and crouch scaling. Limb animation and render LOD do not change collision. The canonical
muzzle uses synchronized eye/weapon geometry and clearance, excluding local visual
recoil/action offsets. This is an approximation of the animated viewmodel. Native
accuracy, spread, ammo, reloads, damage and existing unsupported-trajectory limits remain.
Mountain contact is derived from current rock occupancy rather than local chunk-build
history. No graphics resources are created by these collision queries.

Snapshots retain possession generation, consumed input sequence, lease tick, pending
actions, movement coordinates, owned-job allocation IDs, manual aim and bipod policy.
Existing native/CE projectile ownership tags remain serialized. Loading a join/resync
snapshot restores simulation state without opening a view. At a new hosted live session,
the host sends a reset after replay catch-up; players must possess again. A resync in
the existing session does not send that reset.

## Verification and remaining release gates

`tests/MultiplayerTests` links the production registry, dispatcher, lease/input pump
and firing controller. Its engine, transport context and Scribe boundary are simulated.
Snapshot tests retain native object references; they do not validate native XML,
cross-reference resolution, projectile flight, actual damage, RNG or network transport.
Existing firing regressions and mutation checks remain required.

The current local results are 26 passing multiplayer scenarios, 10 production bridge
checks (including the port's byte-array assembly-loading path), 69 firing scenarios
with all 10 historical mutations detected, and all three terrain mutations detected.
The remaining native helper, installed-CE metadata and package suites passed. Both
targets build; the 1.6 restore retains NU1900 when NuGet audit metadata is unavailable.
These are automated results, not entries in the live matrix below.

Run `scripts/test-portable.ps1`, both target builds, and `scripts/test.ps1` with the
installed 1.7 game/Harmony/CE paths. Reproduce dependency metadata with:

```powershell
./.build/dotnet-sdk/dotnet.exe run --project tests/MultiplayerContractTests -c Release -- `
  1.7 'D:\Steam\steamapps\workshop\content\294100\3820883853'
```

The checked-in contract manifests record the inspected artifacts, not live results.
The inspected game is **1.7.4985 rev1269**. Installed CE metadata advertises only 1.6;
its runtime suitability for 1.7 and coexistence with this port remain unverified.

| Required live combination | Result |
| --- | --- |
| 1.6 + upstream Multiplayer + vanilla | Pending |
| 1.6 + upstream Multiplayer + CE | Pending |
| 1.7 + community port + vanilla | Pending |
| 1.7 + community port + CE | Pending; compatible CE dependency not established |

For every row, use two clients and cover simultaneous same-map and separate-map
possession, contested claims, sustained Auto/Semi/Burst fire, last-round and manual
reloads, moving fire/reloads, crouched cover, V/G bipods, map pause, colony orders,
death/downing, focus loss, disconnect/reconnect and map removal. Join and resync during
movement, warmup, a burst, reload and projectile flight. Reopen a save and watch a replay.
Use differing frame rates, graphics settings and measured latency; compare native
positions, jobs, ammo, projectiles/damage and simulation RNG/desync evidence. Record
exact artifact hashes, logs, latency and outcomes. A missing desync warning alone is
insufficient, particularly with the 1.7 port's unverified detector.

Keep the PR draft and do not publish a multiplayer release until all four rows pass.
Before removing or rolling back RimFront, return every player to colony view and save
so native jobs and fire-at-will settings are restored before the integration is absent.
