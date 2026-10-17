# Native performance guide — 0.6.2

RimFront captures input each frame and consumes it on game ticks. Rendering, mesh creation, collision queries and game simulation share the process. Bounded work limits worst-case growth; it does not establish live FPS or eliminate all latency. No 0.6.2 frame-time improvement is claimed.

## Main budgets

| System | Bound / scheduling |
| --- | --- |
| Static scene | 49 chunks of 12 × 12 cells; at most one missing-chunk build per frame |
| Static refresh | Dirty notifications prioritize changes; one cached chunk checked every 100 ms; fallback full cycle about 4.9 seconds |
| Lighting | At most one cached chunk relit per frame; 16 temporary muzzle lights |
| Pawn appearance | 64 cached appearances plus nine shared fallback families; two refresh attempts and one articulated-rig build per frame |
| Pawn detail | Up to 16 articulated pawns within 14 cells; assembled models otherwise |
| Vegetation | Up to 4,096 plant cards from 8,192 candidates within 32 cells; 128 reusable texture batches; nearest plants prioritized |
| Pawn observation | 256 animation observations; 128 reload observations |
| Weapons / shields | Twelve assembled weapon families, 36 reload parts, four hand meshes; 32 physical shield meshes |
| Projectiles / impacts | 96 tracked projectiles, 128 tracer entries, 192 impact particles, 64 contact marks |
| Blood | 256 transient particles, 128 recent event identities; 96 persistent splats per chunk, two blood things per cell, up to five instances per blood thing |
| Shield effects | 48 impact entries, 24 haze candidates; haze discovery every 15 game ticks |
| Weather | 96 rain streaks and 64 snowflakes; reusable precipitation mesh updated at 20 Hz |
| Crosshair query | 1,024 cells, 768 things, 65,536 triangle checks; 768-entry CPU geometry cache |
| Pawn indicators | 24 labels, 512 retained candidates; up to 512 pawns scanned per render frame |

Live pawns and door leaves update separately from static chunks. Shared family fallback models are created lazily outside the appearance refresh budget, so cold entry into a mixed-species scene can create several models in one frame. Geometry/color setters can cause full Waylith mesh uploads; incremental relighting still has a cost. Transparent shields add blending work. Dense blood and over-budget animation use simplified presentation.

## Reproducible live measurement

1. Record game, Harmony, CE and RimFront versions, complete mod list, CPU/GPU/RAM, resolution, map size, weather, pawn count and simulation speed. Use the same save and viewpoint for comparisons.
2. Measure 30 seconds in ordinary colony view, 30 seconds after entering RimFront with a cold cache, and 30 seconds after the cache warms. Use the same tool for frame-time distribution, FPS and simulation throughput; retain the raw results.
3. Walk across multiple chunk boundaries and through a crowded colony. Open doors, build/destroy objects and change stacks. Record spikes, input-to-motion delay and visual refresh latency.
4. Repeat during sustained CE fire, multiple bursts, a native reload/bipod setup, shield contacts, bleeding and rain/snow. Distinguish normal native aiming/cooldown from input delay. Confirm combat timing and ammo agree with native behavior.
5. Repeat with **Animate nearby pawns** disabled and with each movement mode. Change one setting at a time. Include a cold mixed-species scene and a dense paused scene.
6. Enter/exit repeatedly, change maps and load another save. Record memory after settling and inspect Player.log for repeated exceptions, shader errors or resources retained across sessions.

The HUD supplies a smoothed FPS estimate, draw submissions and cached-chunk count. These are useful for spotting changes but do not replace frame-time traces or establish a performance guarantee. CPU helper tests and offline catalogs do not measure GPU rendering or live patch interaction.

## Reporting a regression

Include the exact save/mod configuration, steps, expected/actual behavior, relevant Player.log section and before/after measurements. Report whether delay occurs during ordinary movement, mesh refresh, native warmup, cooldown, reload or bipod setup. Avoid attributing a performance problem to a mod solely because unrelated startup errors appear in the same log.
