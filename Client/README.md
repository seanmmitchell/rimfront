# RimFront browser client

The mod serves this client from its loopback HTTP bridge. Open it through the mod's authenticated link. The URL fragment supplies the `X-RimFront-Token` header. All scripts, styling, geometry, and colors are local and original; no MW2 installation, assets, CDN, or npm dependencies are used.

Click **Enter perspective** to capture the mouse. WASD moves, mouse looks, left click fires, R requests a reload, and Escape releases control. Switching windows or tabs also clears input and sends a release command. RimWorld pause, disconnection, or a downed controlled pawn stops input. The client automatically reconnects, but requires another click before recapturing the mouse.

Browsers that block mouse capture can use **Use arrow-key look**. Left/right arrow keys turn, with WASD movement and the same fire/reload controls. Escape, switching windows, and hidden tabs release this mode as well. This fallback also works in the offline demo.

Pausing RimWorld releases possession as well as the mouse. After unpausing, start a new field link from the colonist in RimWorld and use its new authenticated URL to reacquire control. Escape and switching windows also require a new field link if the bridge has released possession. The previous tab's token is invalid after a new possession starts.

The live view is a 2.5D approximation of map cells and visible pawn positions. It does not move live pawns, simulate hits, or change live ammunition or health. Unknown ammunition displays a dash. Fog is opaque. Cover currently uses a ground material rather than raised geometry, and the held weapon is a generic original silhouette. Weapon sounds, actual game textures, elevation, and animated models are outside this MVP.

## Offline preview

With Node.js installed, run `node Client/serve-demo.mjs` from the RimFront directory, then open <http://127.0.0.1:18742/?demo=1>. The explicitly labeled offline demo uses a deterministic local scene and simulated weapon, movement, and target damage. It makes no bridge API requests and does not demonstrate Combat Extended integration. The demo does not open doors. Restart the page to reset targets.

## Validation

Run `node --test Client/core.test.mjs`. Tests cover wall and door intersection, opaque fog, invalid snapshot rejection, the deterministic fixture, clicks shorter than a network tick, held fire, reload edges, and clearing all input on focus release. Browser checks and real RimWorld / Combat Extended playtesting remain necessary; these unit tests do not validate game compatibility.
