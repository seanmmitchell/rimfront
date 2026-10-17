# Native architecture — 0.6.1

RimFront runs input, presentation and game integration inside the RimWorld process.

```text
NativeView input -> RimFrontGame -> native movement / attack / reload jobs
      |                                      |
 perspective camera                    game-owned state
      |                                      |
      +--------------- NativeScene <---------+
                         |
                  engine mesh draws
```

## Ownership and lifecycle

`NativeView` temporarily owns the existing camera and gameplay input. Harmony suppresses colony camera/UI handling and flat map/dynamic rendering during possession. Entry saves camera, cursor, subcamera, background-execution and fire-at-will settings; release restores them. `NativeWindowIcon` captures/restores existing Windows icons with bounded transition retries and process-lifetime owned copies.

`RimFrontGame` validates the controlled pawn, consumes frame input on game ticks and owns only its exact jobs. Automatic one-tick posture waits, reloads and verified automatic setup of the equipped CE bipod are compatible transitions. External incompatible orders, focus/modal interruptions and pawn/map lifecycle changes release control. Mode switching clears pending fire, preserves cooldown and requires physical trigger release. Successful non-bursting attack completion preserves CE repeat-shot state; interruption resets the exact owned verb before clearing manual geometry. Possession is never serialized.

## Movement and targeting

`AnalogMovement` owns continuous XZ coordinates only for the controlled pawn. Native cost/payment determines speed. `AnalogCollision` sweeps a conservative square footprint, slides at contact and reserves float-safe separation; only rounding-scale overlaps can be repaired. Native cell setters and callbacks handle movement bookkeeping. Foreign paths and teleports reconcile or release ownership. Unsupported CE contracts or oversized footprints select native path fallback.

`NativeAimTargeting` chooses the nearest rendered contact or a visible empty endpoint. Pawn/corpse geometry follows the current drawn pose and detail level; scenery uses shared CPU geometry. Fog and weapon range constrain selection. `CombatAdapter` requests native attacks/reloads. `NativeManualAim` scopes geometry to the owned pawn, verb and job, validates CE contracts and preserves native spread, ammunition and damage.

Owned CE ballistic queries use floating-point cell traversal and rendered contact distances, with bounded neighboring candidates. Native shield callbacks, foliage chance, impact and armor processing remain authoritative. Stock straight vanilla bullets carry scoped, serialized aim metadata and test close/endpoint solid obstacles before farther targets. Unsupported vanilla trajectories and CE beam collision retain native paths. Shot metadata can outlive possession; map/game changes clear CPU caches. No replacement damage system is used.

## Rendering and caches

`NativeVegetation` draws upright camera-facing cards using the current native plant graphic's texture, tint and UV transform. It borrows textures and owns only its reusable meshes/materials. Game/mod artwork is loaded at runtime, never packaged. All plants share this path, including trees and bushes whose definitions do not use those words. Sprite dimensions and the stable world-axis collision proxy share native visual-size range and quantized growth, capped at 3 cells wide and 4.1 cells high. Alpha holes are visual; native plant interception probability remains authoritative. Custom wind/fall shaders, secondary-color masks and snow overlays are not reproduced.

`NativeScene` batches static geometry into 12 × 12 chunks, with at most 49 cached chunks and one missing-chunk build per frame. Dirty notifications prioritize changes; a 100 ms rotating signature check provides fallback invalidation. The camera far plane is 30 cells. Dynamic pawns and doors update separately. Cached meshes and materials use implemented engine APIs on both target branches.

`NativePawnModels` captures value-only appearance data; `NativePawns` caches at most 64 appearances plus nine shared family fallbacks. It refreshes at most two appearances and builds one articulated rig per frame. At most 16 pawns within 14 cells receive articulated drawing; others use assembled models. Shared family fallbacks are created lazily outside that refresh budget. `NativePawnAnimation` reads real jobs, motion and observed events; simulation ticks drive phase. Lying transforms share bed-slot position/orientation with aim geometry.

`NativeWeapons` caches twelve weapon families and split reload parts. Hand sockets and equipment allocation are shared across articulated/static rendering and missing-arm cases. `NativeReloadAnimation` observes native CE reload jobs/toils without completing them or changing ammo. `NativePawnEquipment` distinguishes owned physical shields from energy belts.

`NativeCombatEffects` observes actual launches, beam segments, impacts and shield outcomes. `NativeBloodEffects` observes real injuries and bleeding; existing blood filth supplies persistent stains. Bounded pools limit cosmetic work. `NativeShieldEffects` has a separate lifecycle for optional colony-view echoes and clears on map/game changes.

`NativeLighting` samples sky, roofs and glow with per-draw properties and incremental chunk recoloring. Face-center samples reduce light leaking across walls. `NativeWeather` updates bounded precipitation at 20 Hz on simulation time. Snow and roofs remain game-owned. These approximations do not reproduce every stock or mod effect.

## Version boundary

One source tree builds separately for 1.6 (.NET Framework/Unity references) and 1.7 (.NET 10/Waylith compatibility references). Inspected 1.7 camera, mesh, shader and input APIs have real implementations. Signature/IL checks support narrowly scoped optional adapters; they do not certify Harmony coexistence with arbitrary mod lists. Refer to [compatibility](compatibility.md) and [performance](performance.md) for validation limits.
