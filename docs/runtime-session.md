# Runtime test session — 2026-10-16

## Startup inspection

- Game: RimWorld 1.7.4980.60, Waylith .NET 10.0.10-waylithclr.dev.
- Harmony startup reports mod 3.0.1.0, library 3.0.0.0-rc.3.
- RimFront installed and listed in activeMods.
- Current startup log contains no RimFront exceptions, assembly load failures, or Harmony patch errors.
- Two duplicate-package errors (Combat Extended and QualityBuilder) also appear in the prior session log; they predate RimFront installation. Both installed CE copies advertise 16.7.4.0 and RimWorld 1.6.
- CE initialization messages are present. Which duplicate installation was selected has not been confirmed.

This establishes only absence of reported startup failures. The control gizmo, live bridge, movement, firing, ammunition and reload behavior are awaiting in-game tests. No compatibility row is yet marked runtime-passed.
