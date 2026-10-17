# Compatibility and verification — 0.6.1

Updated 2026-10-17. **Both 0.6.1 builds and all 95,887 automated checks passed.** No live 0.6.1 gameplay or GPU validation is recorded here. Compilation, metadata inspection and standalone helper tests do not certify native camera behavior, patch coexistence, combat outcomes or performance.

## Target matrix

| Target | Build boundary | 0.6.1 live status |
| --- | --- | --- |
| RimWorld 1.6 | Pinned `Krafs.Rimworld.Ref` 1.6.4871, `Lib.Harmony` 2.4.2, .NET Framework 4.7.2 | Not validated |
| RimWorld 1.7 | Installed game/Harmony, UnityStubs/Waylith references, .NET 10 | Not validated |
| Either branch with CE | Optional checked adapter; requires CE appropriate for that branch | Not validated |
| Other camera, movement or combat mods | Shared Harmony targets may conflict | No general compatibility claim |
| Multiplayer / concurrent controllers | Outside current scope | Unsupported |

The inspected 1.7 installation was 1.7.4981 on the unstable branch. Its APIs may change. An upstream CE source revision advertised 1.6; source inspection alone does not establish a supported 1.7 release. Validate the exact game, Harmony, CE and mod combination you install.

## Evidence and its limits

Version 0.6.1 passed 81,793 pawn/model/animation/combat/vegetation checks, 11,126 movement checks, 2,803 geometry/lighting/weather/weapon/effect checks, 95 job-policy checks, 20 window-icon checks, 40 native input/aim checks and 10 package contract checks. Both builds passed; 1.6 retained NU1900 for unavailable audit metadata and 1.7 built without warnings. Logs: `work/runtime-test/build-0.6.1.log` and `tests-0.6.1.log`.

The inspected 0.5.8 Player.log records returns to colony view for `AttackStatic` at 01:26:52 and 01:27:16 on 2026-10-17. Installed CE code suspends and queues that job during an equipped-weapon reload; 0.6.1 suppresses only resumption of RimFront's exact owned attack in that verified transition. The fourth `StartJob` argument and unique overload were checked in both game references. Tests cover ownership exclusions, no phantom trigger after the last shot, accepted pre-shot intent, and native hook signatures; they do not execute a live reload.

Vegetation tests cover camera-facing geometry, UVs, fallback silhouettes, shared growth/size bounds and camera-independent collision proxies. Installed 1.7 implementation inspection confirms the exact mesh-list upload, deferred bounds and material UV methods are functional. Native plant material selection is deterministic for the supplied plant. No plant texture is copied into the package. Custom wind/fall shaders, secondary-color masks and snow overlays are outside this renderer; leaf alpha is approximated for collision.
Version 0.6.0 passed 76,042 pawn/model/animation/combat lifecycle checks, 11,126 movement checks, 2,803 geometry/lighting/weather/weapon/effect checks, 95 job-policy checks, 20 window-icon checks, 40 native input/aim checks and 10 package contract checks. The retired HTTP/browser suites are no longer included. Package tests verify that stale staging files cannot leak into fresh archives and that mismatched versions or dependency DLLs are rejected. Both target DLLs passed a separate metadata inspection confirming version 0.6.0 and absence of the retired transport types and networking/serialization references.

The 1.6 build emitted NU1900 because NuGet vulnerability-audit metadata was unavailable; the 1.7 build completed without warnings. Development logs are `work/runtime-test/build-0.6.0.log`, `tests-0.6.0.log` and `native-only-0.6.0.log`. Tests ran without a browser, Node.js, HTTP listener or game session.

The previous 0.5.8 assemblies compiled for both targets, and its native helper suites passed. Its 1.6 build retained NU1900 because NuGet vulnerability-audit metadata was unavailable; the 1.7 build had no warnings. A separate probe checked 67 installed native/CE metadata and IL contracts. These are historical results, not 0.6.0 test results. Development logs are `work/runtime-test/build-0.5.8.log`, `tests-0.5.8.log` and `contracts-0.5.8.log`.

Earlier 1.7 logs confirm native-view entry/exit and CE analog-adapter startup. They also document defects subsequently addressed: repeated same-faction marker errors, exits on automatic posture waits, and 0.5.7 exits for `JobDef_SetUpBipod`. The latter occurred at 23:29:42, 23:29:43 and 23:30:04 on 2026-10-16. Installed CE inspection confirmed that resetting a completed verb discards its repeat-shot timing state. These observations support the targeted fixes; they do not prove live success after updating.

Automated coverage includes swept collision and float-storage regressions, geometry/topology and appearance, equipment/bed transforms, fractional projectile traversal, target and trigger policy, native cleanup ordering, CE contract boundaries, reload/recoil timing, lighting/weather/effect math, and injected Windows-icon lifecycle tests. Compiled geometry tests use installed 1.7 vector/quaternion implementations without creating a GPU renderer. Icon tests use a fake backend and never operate live windows. Offline catalogs are projections of actual compiled geometry, not game screenshots.

## Adapter boundaries

CE ammunition/reload integration checks `CompAmmoUser` members including `TryStartReload`, `CurMagCount`, `MagSize`, `HasMagazine`, `HasAmmo`, `UseAmmo` and `CanBeFiredNow`. Mode switching checks native current/available-mode and toggle contracts. Unsupported contracts report an unavailable operation instead of modifying ammunition or inventing modes.

Analog CE integration separately checks coordinate, collision-query, bounds and lead contracts; unsupported contracts select native path movement. The square footprint uses conservative body bounds, caps travel at 0.25 cells per tick and may restrict tight corners. Native stagger, payment rules and external paths remain authoritative.

CE ballistic contacts and supported stock straight vanilla bullets receive scoped geometry integration. Custom beams, explosive/arcing vanilla projectiles and unsupported custom trajectories retain native paths. Native shield, damage, armor, accuracy and ammunition processing remain in charge. Precise vertical aiming is not guaranteed for unsupported trajectories.

Reload presentation reads actual jobs/toils. Unknown timing uses a lowered pose. The Windows icon safeguard is defensive; neither a confirmed root cause nor live resolution of the reported taskbar regression is claimed.

## Presentation limits

Models are generic representations: nine rig families, six human shapes, fourteen apparel families, twelve weapon families and 32 additional object families. Unfamiliar anatomy, xenotypes, unusual apparel and mod-specific graphics may be approximate. The seven-part rig cannot independently represent every limb of every creature.

Native view suppresses stock flat map/dynamic drawing. Arbitrary motes, custom shaders and unrelated mod effects may be absent. Lighting is vertex/per-draw color sampling, not shadow mapping. View distance, appearance updates, detailed animation, effects and geometry work are bounded; dense scenes can simplify or delay presentation.

## Release gate

Run the [smoke checklist](smoke-tests.md) on 1.6 and 1.7, with and without appropriate CE builds. Include actual M60 setup/reload and multiple held-Auto cycles, not only entry/exit. Record game/mod versions, hardware, logs and failures. Measure live frame times using [performance.md](performance.md). Until those checks pass, describe RimFront as experimental, not universally compatible or a proven lag fix.
