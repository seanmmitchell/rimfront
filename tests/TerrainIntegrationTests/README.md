# Terrain and aiming regressions

Run from the repository root with a .NET 10 SDK:

```powershell
./scripts/test-terrain-integration.ps1 -VerifyMutations
```

The suite links the production `NativeAimTargeting`, `NativeMountains`,
`MountainSurfaceCache`, `MountainProfile`, `NearestCandidates`, and `AimGeometry`
sources. The fixture supplies map/object state, a triangle collector, and managed
vector math. It does not run a GPU renderer, Harmony, native ballistics, or a game.

Five named scenarios run in the portable CI gate:

- `published-mountain-geometry`: mining stages a lower slope while render/collision
  retain the old snapshot; publication switches both, invalidates the aim cache,
  new rock waits for publication, and eviction removes collision.
- `independent-chunk-publication`: publishing one chunk leaves another chunk on
  its displayed revision; map changes clear published geometry.
- `fogged-mountain-exterior`: an upward ray reaches the actual visible slope
  above the fog volume and returns an anonymous cell/surface contact.
- `underground-fog-privacy`: low rays stop at fog, hidden occupants are not queried,
  native range remains bounded, and revealed rock uses normal Thing selection.
- `nearest-plant-admission`: a late near candidate displaces a far candidate from
  the full 8,192-entry queue; reversed order and equal-distance ties match a sorted
  reference result while capacity stays bounded.

With `-VerifyMutations`, the runner builds isolated copies that restore global
unpublished heights, infinite fog columns, and first-arrival plant truncation.
Each copy must compile and fail its corresponding named scenario. Build errors
and unrelated failures do not count. Logs/results remain under the ignored
`.build/terrain-regressions/` directory and CI retains them as evidence.

Native 1.7 helper suites additionally exercise the actual compiled chunk scheduler,
the actual vegetation Queue with simulated game registration, and native math.
Live visual appearance, frame times, mining and CE projectile behavior still need
the in-game smoke checklist.
