# WH30K Vertical Slice 0.1

This scene demonstrates the first playable vertical slice of the WH30K colony builder. It lives in `Assets/Scenes/Prototype/WH30K_VS01.unity` and targets the built-in render pipeline.

## Feature Overview

- **Procedural planet** – a cube-sphere LOD planet (radius ~3000) with deterministic noise driven by the selected seed. LOD updates are throttled and degrade with distance to keep updates smooth.
- **Single shared terrain material** – the `WH30K/PlanetRockDirtTriplanar_BuiltIn` shader procedurally blends rock and dirt color bands with triplanar noise to avoid seams.
- **Orbit camera** – right mouse button to orbit, scroll to zoom. Press `Home` or `F` to re-frame the planet.
- **New game panel** – enter a numeric seed (blank = random) and cycle through difficulties (Easy → Grim). Difficulty changes resource yield, environment harshness, and event cadence.
- **Settlement loop** – a single starting settlement spawns on land with Hab Block, Macro Factory, and Utility Nexus buildings. Population, workforce, production, upkeep and stockpile totals are tracked via the HUD.
- **Environment scalars** – CO₂, O₂, water pollution, and global temperature offset respond to industry output and are always visible on the HUD.
- **Narrative event** – within the first minute an "Industrial Priorities" decision appears. Each option alters the settlement multipliers, resources, and environment while logging the outcome.
- **Saving & loading** – `Save` writes JSON to the persistent data path, `Load` restores the seed, difficulty, settlement state, and environment scalars.

## Controls

| Action | Input |
| --- | --- |
| Orbit | Right Mouse Drag |
| Zoom | Mouse Scroll Wheel |
| Re-frame planet | `Home` or `F` |
| Start new game | `Begin` button |
| Save / Load | Buttons on the new game panel |

## Implementation Notes

- `PlanetBootstrap` owns the session lifecycle (planet generation, settlement spawn, event driver, save/load).
- `PlanetBootstrap` now centralizes both new game and load paths so simulation state, HUD, and event timers stay in sync when swapping sessions.
- `NewGameMenu` builds the entire UI at runtime so the scene stays lightweight.
- Resource and environment systems push data to the HUD after every tick for instant feedback.
- Narrative event scheduling is seeded so the first decision always fires deterministically for a given seed.
- Save files are stored at `Application.persistentDataPath/wh30k_vslice_save.json`.
