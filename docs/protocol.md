> Legacy 0.1 browser transport reference. Native 0.2 gameplay does not start this bridge or consume HTTP input. Retained for regression tests and historical context.

# Local protocol v1

The mod serves the client and API on the same loopback origin. Open the URL from the in-game control button. Its fragment contains `token=<random-secret>`; the client supplies it as `X-RimFront-Token` on API requests. The token changes with every new possession. No CORS access is enabled. A browser refresh requires taking control again in RimWorld if its input sequence resets.

## State

`GET /api/state` returns a `WorldSnapshot` JSON object:

| Field | Meaning |
|---|---|
| protocol | Integer 1 |
| gameVersion, status, combatMode, message | Human-readable runtime/status information |
| active, paused | Possession and game pause state |
| tick, mapId | Game tick and map identity |
| width, height | Map dimensions; zero for idle/released state |
| controlledPawnId | Pawn controlled by the locally granted session |
| cells | Row-major `z * width + x` tile values |
| pawns | Unfogged pawn records: id, name, x, z, health (0..1), hostile, downed |
| weapon | label, status, ammo, capacity, canReload, fireMode; negative ammo/capacity means unknown |

Tiles: 0 floor/open door, 1 impassable structure, 2 closed door, 3 other unwalkable terrain, 4 cover, 5 fog. These are rendering categories; only native pathfinding decides movement legality. Pawn positions are game draw positions in tile units. Fog remains opaque in the renderer.

State publishes at approximately 10 Hz; terrain refreshes at most once per second. Clients must accept inactive states with empty map/pawn arrays. They must not invent missing live ammunition, damage, or movement updates.

## Input

`POST /api/input`, content type `application/json`, maximum body 4096 bytes:

```json
{"seq":1,"action":"input","forward":1,"strafe":0,"yaw":0.5,"fire":false,"reload":false}
```

- `seq`: nonnegative 32-bit integer increasing within a possession.
- `action`: `input` or `release`.
- `forward`, `strafe`: finite values from -1 to 1.
- `yaw`: finite radians; 0 faces +z, positive turns toward +x. Browser normalizes to one turn.
- `fire`: held trigger intent. Short press edges are retained where sampled.
- `reload`: one-shot reload request.
- `pawnId`: optional existing possession check; cannot select a different pawn.

Clients submit serialized requests at approximately 20 Hz while controlling the pawn. A 200 response acknowledges enqueueing, not successful movement or a hit. Gameplay validates session/pawn state before acting. At most 64 commands wait in the queue; overflow returns 429. Invalid JSON/control values return 400; absent/incorrect tokens return 401; forbidden origins/hosts return 403. `release` clears possession independently of simulation pause.

Network input is stopped on focus loss, pointer unlock, page exit, and stale/disconnected state. The game also stops stale movement and releases disconnected control independently of the browser, so an interrupted release request is not the only cleanup mechanism.

## Offline demo

`?demo=1` selects a deterministic simulated training scene. It makes no game API calls. Demo ammo, damage, movement and targets are deliberately synthetic and visibly labeled. Demo behavior is not evidence that native RimWorld or CE gameplay has passed its smoke tests.


The shared native input model adds optional `cycleFireMode` in 0.5.7 (default false). Native view produces it from F; the legacy browser has no fire-mode UI and is not a supported gameplay transport.