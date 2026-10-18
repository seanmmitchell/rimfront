# CI, pull requests, and release evidence

Changes to `main` go through a pull request. The repository protection policy in
`.github/main-protection.json` requires the GitHub Actions check `RimFront CI gate`,
an up-to-date branch, and resolved review conversations. It applies to admins and
blocks force pushes and deletion. No second-person approval is required while the
project has one collaborator; the PR and test gate are still required.

The JSON documents GitHub settings; merging it alone does not apply them. An admin
can inspect the current settings with `gh api repos/seanmmitchell/rimfront/branches/main/protection`
and apply this policy with:

```powershell
gh api --method PUT repos/seanmmitchell/rimfront/branches/main/protection --input .github/main-protection.json
```

`RimFront CI` runs for every PR targeting main and every push to main, including
documentation-only changes. The final required check fails if its dependency fails,
is cancelled, or is skipped. Keep its name stable; update the protection policy
before renaming it. New verification jobs must also be added to its dependencies
and success test. Actions are pinned by commit, the token is read-only, checkout
does not retain credentials, and PRs execute on disposable GitHub-hosted runners.

## What a green hosted check establishes

Run the same gate locally with `./scripts/test-portable.ps1` using .NET 10:

- The 1.6 mod builds against the pinned reference and Harmony packages.
- The actual firing controller passes named simulated-engine scenarios and detects
  deliberately restored regressions, including held fire, reloads, and empty ammo.
- Job policy, movement, window lifecycle, input, and terrain/range helper tests pass.
- Five production terrain/aim scenarios cover published mountain geometry, per-chunk
  publication, above-fog contacts, underground privacy, and nearest-plant admission.
  Three isolated mutations must compile and fail their corresponding named scenario.
  These tests simulate map boundaries and use managed math, without a renderer.
- Package contracts reject stale/extra files. These use the 1.6 DLL as a fixture
  in both version slots; they do not build or validate 1.7.

CI retains structured scenario/mutation results and logs for 14 days, including
failed runs. Successful main pushes also retain a clearly named 1.6 compile-only
artifact. That DLL is not a full two-version release or a live compatibility claim.
No game assemblies, credentials, or installed game files are uploaded.

## Release boundary

Hosted CI has no licensed local 1.7 installation. It cannot run the 1.7 build or
native-helper suites that load those assemblies. Before publishing a mod release,
run `scripts/verify-release.ps1` with installed 1.7/Harmony paths; that builds both
targets, runs the full suites, and packages only after success. Then record the
live checks in `docs/smoke-tests.md`, including CE versions and manual/automatic
reloads. Automatic publication is intentionally absent until that evidence exists.

Use the PR template to describe the problem and resulting behavior, relevant
implementation choices, exact validation evidence, and remaining risks. Mark
unperformed checks explicitly. CI does not enforce the truth of a PR description;
reviewers must compare its claims with linked evidence.
