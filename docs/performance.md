# Native performance guide — 0.6.3 test build

RimFront captures input each frame and consumes it on game ticks. Rendering, mesh creation, collision queries and game simulation share the process. Bounded work limits worst-case growth; it does not establish live FPS or eliminate all latency. Live FPS improvements must be measured on the same save and hardware.

## Main budgets

| System | Bound / scheduling |
| --- | --- |
| Static scene | 121 / 289 / 441 chunks of 12 × 12 cells at 60 / 90 / 120 tiles; at most one chunk build/rebuild per frame |
| Static refresh | Direct edits first, then nearest dirty profiles, then cache expansion; one cached chunk checked every 100 ms; fallback full cycle up to 12.1 / 28.9 / 44.1 seconds at full cache size |
| Resource piles | One piece per five units, capped at 18: at most 1,152 triangles for logs or 360 for ingots/blocks; static mesh rebuild only when the quantity signature changes |
| Lighting | At most one cached chunk relit per frame; 16 temporary muzzle lights |
| Pawn appearance | 64 cached appearances plus nine shared fallback families; two refresh attempts and one articulated-rig build per frame |
| Pawn detail | Up to 16 articulated pawns within 14 cells; assembled models otherwise |
| Vegetation | Up to 4,096 plant cards from 8,192 candidates within the configured view range plus a two-cell sprite margin; 128 reusable texture batches; a bounded max heap retains the nearest candidates before distance sorting |
| Sprite weapons | At most two value-only appearance refreshes per frame across the existing 64 sprite entries; metadata refresh interval 0.5 seconds. Uses existing held weapon meshes and poses without building 3D body meshes |
| Pawn observation | 256 animation observations; 128 reload observations |
| Weapons / shields | Twelve generic assembled weapon families, 36 generic reload parts, nine machine-gun profiles with 27 split parts, two bipod meshes, four hand meshes; 32 physical shield meshes |
| Projectiles / impacts | 96 tracked projectiles, 128 tracer entries, 192 impact particles, 64 contact marks |
| Blood | 256 transient particles, 128 recent event identities; 96 persistent splats per chunk, two blood things per cell, up to five instances per blood thing |
| Shield effects | 48 impact entries, 24 haze candidates; haze discovery every 15 game ticks |
| Weather | 96 rain streaks and 64 snowflakes; reusable precipitation mesh updated at 20 Hz |
| Crosshair query | 4,096 cells, 3,072 things, 256 traversed ray segments, 65,536 triangle checks; 3,072-entry CPU geometry cache; identical pawn/map/eye/yaw/pitch/range/frame/tick queries share one result |
| Pawn indicators | 24 labels, 512 retained candidates; up to 512 pawns scanned per render frame; labels and candidates limited to half the view distance; toggle skips all indicator work |
| Minimap | 256 × 256 target (75% fewer pixels than 512²); reused 1.7 command list; native map meshes plus at most 256 local dynamic objects. No full-colony dynamic manager, pawn atlases, weather, flecks, designations or overlays. Image refresh 0.25–2 seconds, scheduled after completion, with immediate backoff toward a 2% CPU-time budget (capped at 2 seconds; expensive passes can exceed that budget). Paused refresh ≥1 second; failures back off 2 seconds. Pawn/aim overlays refresh at 5 Hz: at most 128 pawn candidates per update and 64 retained dots. Toggle off to eliminate map-pass cost |

Mountain profiles keep one rock mask and height field for the active map. A two-pass distance transform and summed-area density calculation take linear time in map cells on entry and after rock occupancy changes. Non-rock mesh changes do not rebuild the profile. A profile revision dirties cached chunks. Each chunk captures its own immutable 13 × 13 corner grid and rock occupancy, then publishes that snapshot only after replacing its mesh. Aim/projectile geometry uses the published snapshot and changes revision with that mesh. Eviction removes the snapshot. Chunks still rebuild at most once per frame, so neighboring chunks can temporarily show different revisions while updates settle. The profile includes unexplored rock bulk and caps height at 24 tiles. Unknown rock and ore share a uniform exterior tint, while opaque fog obscures underground cells. Exterior surfaces above the 2.55-tile ceiling receive sky lighting.

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
