# Performance evidence and test plan

Version 0.2 moves input and rendering into the game process. Native mode does not serialize map snapshots, poll HTTP endpoints, or wait for browser input messages. This removes the old transport work and its 10 Hz visual update ceiling. It does not by itself establish a particular FPS improvement; native rendering still consumes CPU and GPU time.

## Measured cost of the old transport

A standalone Release-mode .NET 10.0.402 process on the development machine encoded deterministic maps with 100 pawn records. After 10 warmup encodes, 100 encodes were timed using `Stopwatch`; current-thread allocation was measured with `GC.GetAllocatedBytesForCurrentThread`. Each cell contained a value from 0–5. This exercised the original `WireJson.Encode` implementation using `DataContractJsonSerializer` and `MemoryStream.ToArray`.

Reproduce with `./scripts/benchmark.ps1` (or pass `-Dotnet` to select a local .NET 10 SDK). The code is in `tests/PerformanceBenchmark`; results vary with the machine and workload.

| Map | Mean encode time | Allocated per encode | JSON payload | Payload at 10 updates/s |
| --- | ---: | ---: | ---: | ---: |
| 250 × 250 | 3.411 ms | 653,344 bytes | 134,315 bytes | 1,343,150 bytes/s |
| 500 × 500 | 8.380 ms | 1,538,689 bytes | 509,315 bytes | 5,093,150 bytes/s |

These numbers measure serialization only. They exclude the old once-per-second full-map terrain scan, pawn extraction, HTTP overhead, browser JSON parsing, software rendering, and garbage-collection pauses. They are not game frame times, are not measurements on the 1.6 Mono runtime, and do not isolate the cause of all reported lag.

The inspected 1.7 player log contained duplicate-mod startup errors for Combat Extended and QualityBuilder, but no repeating RimFront exception pattern. That observation does not establish that the user's live scene was error-free or that the duplicate mods caused the reported lag.

## Native rendering constraints

The renderer caches local geometry, batches terrain and objects into chunk meshes, limits cache size and rebuild work, and reuses materials. Only nearby chunks and dynamic objects are submitted. Mesh creation occurs on the game thread. An in-game run must still confirm that the chosen budgets meet the frame-time target on the user's hardware.

Read-only inspection of the installed 1.7 engine confirmed that `UnityEngine.Graphics.DrawMesh` submits real Waylith draw commands; mesh setters, normals, bounds, and material colors have implementations. The shared scene vertex shader uses model and view-projection matrices. The Cutout material writes depth. These are real engine rendering paths, but runtime validation remains necessary for appearance and performance.

## In-game measurement procedure

1. Use the same colony, camera location, game speed, resolution, and mod list for each run. Include a crowded colony and a large map.
2. Record 30 seconds in ordinary colony view, 30 seconds after entering RimFront, and 30 seconds after its mesh cache warms. Record frame time or FPS and simulation speed using the same measurement tool.
3. Walk continuously across multiple chunk boundaries; watch for spikes during cache rebuilds. Open doors and build/destroy objects; record update latency and any stale geometry.
4. Repeat with CE combat, several moving pawns, sustained fire, and a reload. Verify that GPU load does not change native combat timings.
5. Repeatedly enter/exit, change maps, and load another save. Track memory and ensure meshes/materials are released. Look for recurrent exceptions or shader warnings in the player log.

Record hardware, game build, mod versions, map size, object/pawn count, and measurements. Do not describe the lag as fully resolved until this live comparison passes.

## Version 0.5 budgets and measurement scope

The renderer retains at most 49 local scenery chunks. Effects cap tracked projectiles at 96, tracer entries at 128, sparks at 192, muzzle-light flashes at 16, assembled weapon models at 12, reload parts at 36, hand meshes at 4, reload observations at 128 pawns, and precipitation at 96 rain streaks plus 64 snowflakes. Weather uses one reusable particle mesh updated at 20 Hz; a separate small mesh provides the sun/moon cue. Roof and actual snow-depth geometry/tint stay in the chunk cache.

Pawn appearance meshes add a separate 64-entry cache plus nine shared family fallbacks, with at most two appearance refresh attempts per frame. Articulated meshes are built for at most one pawn per frame. Detailed limb drawing is limited to 16 pawns within 14 cells; far and over-budget pawns keep assembled models. Animation observations retain at most 256 pawns. The **Animate nearby pawns** setting provides a simpler rendering fallback. These limits bound work and memory; the new model catalogs and standalone tests do not measure live GPU cost, animation smoothness, cache disposal, or frame-time improvement.

Shared family fallbacks are created lazily outside the two-appearance refresh budget. First encountering several new families can therefore add mesh creation work in the same frame; the total fallback set remains capped by the nine forms. Include cold-cache mixed-species entry in live performance measurements.

Version 0.5.1 physical-shield rendering retains at most 32 shape/color meshes and 256 definition classifications. Equipment attachment changes reuse model geometry and apply transforms; they do not rebuild the weapon each animation tick. These added draw calls need the same crowded-scene runtime measurements as articulated pawns.

Shield effects retain 48 impact entries and at most 24 active haze candidates, refreshed every 15 game ticks. Impact echoes expire after 18 ticks; same-pawn hits in the same tick merge. Sphere and ring meshes are shared. Transparent overlap can still add GPU blending cost; test shield-heavy combat in both first-person and optional colony mode.

Version 0.5.3 blood effects retain 256 particles and 128 recent event identities. Blood and tissue share one reusable dynamic mesh and expire after 54 simulation ticks. Native bleeding callbacks drive droplets rather than a per-frame scan of every pawn. Persistent native blood geometry is capped at 96 splat instances per scenery chunk, two blood things per cell and five native draw instances per blood thing; the blood-definition registry holds at most 512 entries. Dense stains can therefore be simplified. Blood stays within the existing one-chunk-build-per-frame budget.

Material-specific impacts reuse the existing 192-particle pool and add at most 64 ground contact marks, expiring after 180 ticks. Surface particles last 15–35 ticks. These effects add bounded draw work, but the limits do not establish live FPS; compare sustained gunfire, dense blood trails and paused effect toggles in the same test colony.

Lighting uses real sky/roof/glow-grid state, a 100 ms cell cache, and at most one static chunk recolor per frame. Updating mesh colors can cause the Waylith compatibility layer to upload the whole mesh; this is explicitly included in the live performance checklist. Dynamic objects reuse per-draw color properties. Finite pools constrain growth but do not guarantee a particular FPS.

Version 0.4 implements analog movement by default, with continuous look-ahead native paths as a settings fallback. Both camera modes bypass the colony pawn-position spring. Read-only inspection of the installed 1.7 pawn tweener found a 9% error-correction step at 60 Hz on top of native path progress; this supports a plausible source of perceived movement delay, not a measured diagnosis of the user's session. The new camera interpolates only between adjacent authoritative tick positions. Compare analog and native fallback in the same scene, including diagonal travel, tight corners, door opening, and CE fire. No before/after input-latency or FPS measurement is claimed yet.

Version 0.5.4 bounds crosshair queries to 1,024 nearby cells, 768 things and 65,536 triangle checks, with a 768-entry CPU geometry cache. Pawn indicators show at most 24 labels, retain at most 512 candidates, and scan at most 512 pawns per render frame. New scans begin every six simulation ticks; pending batches also complete while paused. These limits bound work; live frame-time measurement remains pending.


