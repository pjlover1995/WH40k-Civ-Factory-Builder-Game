# WH30K Vertical Slice

This scene demonstrates the first playable vertical slice of the WH30K colony builder. It lives in `Assets/Scenes/Prototype/WH30K_VS01.unity` and targets the built-in render pipeline.

See the root `README.md` for setup and `Docs/VERIFICATION.md` for automated and Unity acceptance checks.

## Feature Overview

- **Procedural planet** – a cube-sphere LOD planet (radius ~3000) with deterministic noise driven by the selected seed. LOD updates are throttled and degrade with distance to keep updates smooth.
- **Single shared terrain material** – the `WH30K/PlanetRockDirtTriplanar_BuiltIn` shader procedurally blends rock and dirt color bands with triplanar noise to avoid seams.
- **Orbit camera** – right mouse button to orbit, scroll to zoom. Press `Home` to frame the planet or `F` to focus the settlement.
- **New game panel** – enter a numeric seed (blank = random) and cycle through difficulties (Easy → Grim). Difficulty changes resource yield, environment harshness, and event cadence.
- **Settlement loop** – a single starting settlement spawns on land with Hab Block, Macro Factory, and Utility Nexus buildings. Population, workforce, production, upkeep and stockpile totals are tracked via the HUD.
- **Construction** – select housing, factories, outposts, or scrubbers. Placement validates land, footprint occupancy, and affordability; buildings affect the settlement economy and persist in saves.
- **Environment** – CO₂, O₂, water pollution, and temperature affect habitability, housing, workforce, and production. Scrubbers and safety policies provide ways to recover.
- **Narrative event** – within the first minute an "Industrial Priorities" decision appears. Each option alters the settlement multipliers, resources, and environment while logging the outcome.
- **Saving & loading** – HUD buttons and F5/F9 save and restore the seed, difficulty, settlement, constructed buildings, environment, policies, and event timer or pending decision. Original prototype saves are supported.

## Controls

| Action | Input |
| --- | --- |
| Orbit | Right Mouse Drag |
| Zoom | Mouse Scroll Wheel |
| Re-frame planet | `Home` |
| Focus settlement | `F` |
| Start new game | `Begin` button |
| Save / Load | HUD buttons or `F5` / `F9` |
| Select construction type | `Next Building Type` |
| Place / Cancel | Left click / `Esc` or right click |

## Implementation Notes

- `PlanetBootstrap` owns the session lifecycle (planet generation, settlement spawn, event driver, save/load).
- Save files are stored at `Application.persistentDataPath/wh30k_vslice_save.json`.
