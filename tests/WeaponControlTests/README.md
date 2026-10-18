# Bipod and shot-source coverage

Run `dotnet run --project tests/WeaponControlTests -c Release`, or use `scripts/test-portable.ps1`. The suite links the production `NativeBipods.cs` and `NativeShotSource.cs`. Small managed substitutes provide the engine, reflection target and geometry-query boundary; they do not reimplement adapter decisions.

Checks cover the exact controlled-primary scope, native decision preservation, native close invocation/failure, cancellation order, no synchronous job restart, unsupported contracts/weapons, equipment changes, release/re-entry and HUD state. Source checks cover yaw/pitch, fractional coordinates, three-dimensional clipping, near targets, invalid inputs, finite offset limits, unavailable muzzles and incomplete clearance queries.

`NativePawnTests` exercises compiled source math with installed 1.7 vectors, the source transpiler (including missing/ambiguous IL rejection), unowned fallbacks and nested cast cleanup. `NativeGeometryTests` verifies M60/LMG classification and existing weapon geometry. `FiringLoopTests` drives actual production controller frame/tick code through toggle, setup, fire, reload and input-lifetime sequences.

Optional installed CE metadata checks:

```powershell
./scripts/test.ps1 -Dotnet 'path/to/dotnet.exe' `
  -RimWorld17Dir 'path/to/RimWorld' -Harmony17Dir 'path/to/Harmony/1.7/Assemblies' `
  -CombatExtendedAssembly 'path/to/CombatExtended.dll'
```

`CeContractTests` reads CE with Mono.Cecil, without executing it. It checks setup/close signatures, native stat reset, the unique source reads needed by both transpilers, source coordinates, height and native launch. It is optional because CI cannot assume a proprietary game or CE installation. A metadata pass does not establish compatibility with another RimWorld branch.

These tests do not execute native CE/Harmony patch coexistence, Unity input, rendering, collision callbacks or a live M60. Follow the M60/LMG rows in `docs/smoke-tests.md` with branch-compatible CE before claiming those paths work in-game.
