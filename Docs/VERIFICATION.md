# Verification

## Automated checks available without Unity

From the repository root, run:

```powershell
dotnet run --project Tests/Simulation/SimulationChecks.csproj -- .
git diff --check
```

The executable links the actual production files `ColonyRules`, `EnvironmentRules`, `ConstructionSave`, and `EventSchedule`. It exercises payment boundaries, capacity/output changes, environmental feedback and cleanup, save validation, event RNG/timer persistence, repeated decisions, and ten minutes of simulation. Roslyn also parses every C# source with and without `UNITY_EDITOR`. Parsing does not establish compatibility with Unity's engine APIs.

## Unity integration checks

1. Open the project with Unity 2022.3.2f1. Confirm there are no compiler errors or missing scripts.
2. Open `Assets/Scenes/Prototype/WH30K_VS01.unity` and enter Play Mode.
3. Select **WH30K > Run Gameplay Checks (Play Mode)**. Expect 37 passing scene checks in the Console. The checks temporarily replace the world, then restore the current colony without changing its disk save. Ctrl+Alt+G also runs this command.

## Visual and disk acceptance checks

1. Begin a new expedition. The camera shows the planet. At 1280x800 and 1920x1080, all HUD labels, building details, buttons, and event choices fit their panels.
2. Select each building type. Cost and economy details update. Place an affordable outpost on land: stockpile drops by 250 once and production/upkeep change immediately. Place housing to permit further population growth.
3. Move the preview over water, an existing building, or the starting core. Placement is blocked. Spend down the stockpile: unaffordable construction remains blocked. Clicking HUD buttons or an event does not build behind the UI; Esc cancels with the pointer off the planet.
4. Build factories and choose overdrive several times. Worsening conditions reduce habitability and output. Build scrubbers or fund safety retrofits: pollution improves. An unaffordable safety retrofit is disabled until funds are available.
5. Press F5 while an event awaits a choice. Build another structure or resolve the choice, then press F9. The saved buildings and stockpile return, the unanswered choice reappears, and loading again does not duplicate output or charge construction again.
6. Resolve the restored event. The next event eventually appears. Continue for ten minutes, monitoring frame time and resource/environment changes.
7. Exit/re-enter Play Mode and load the disk save. Verify the same structures and policies return. Confirm a second save creates the `.bak` file and a malformed save is rejected without replacing an active colony.
8. Build and launch a standalone player. Verify the scene, procedural material, HUD, save/load, and building assets work without the editor-only debug panel.
9. Press Home: inspect the planet silhouette, day/night lighting, oceans, ice, cloud cover and atmospheric rim. Press F: inspect each building's distinct architecture, foundation, shadows and service causeways. Zoom/orbit near the surface and across LOD boundaries; verify the camera stays above ground, no cracks appear and repeated loads do not accumulate visible shells or causeways.
10. Use WASD, middle-drag and right-drag to explore beyond the colony. Zoom between surface and orbit; verify clicking a building opens its own inspector without moving the camera. Change each building type's focus, upgrade it, suspend/resume it, then save/load to verify settings and economy survive.
11. Pause: calendar, stockpile and events stop while camera navigation and real play time continue. Resume at 3x/10x: calendar and economy accelerate together. Save/load while paused and while a decision is pending; verify calendar, played time, speed and pending decision restore correctly.

Record Unity version, seed, difficulty, check output, screenshots, and any observed performance issues when completing these checks.

## Review run — 2026-10-23

- Unity 2022.3.2f1 on Windows, built-in renderer (DX11); seed 1234, Standard difficulty.
- Unity compiled the editor and gameplay assemblies successfully. All 20 real-scene gameplay checks passed in Play Mode; the Console showed no warnings or errors after the scene reload.
- All 29 executable checks passed, including editor/player syntax parsing; `git diff --check` passed.
- Visually confirmed settlement and planet framing, rendered terrain/buildings, readable HUD and event choices, and growing population/stockpile. F5 saved to disk and F9 restored the saved stockpile and colony in the running editor.
- Corrected the scene's non-normalized camera rotation and explicit empty projection, and resolved the scene camera again when starting or loading a colony to avoid Awake-order dependence.
- This first camera-repair run preceded the visual pass below. The full visual/disk checklist, both exact target resolutions, a ten-minute engine performance run, and a standalone player build still need acceptance testing.

## Gothic architecture and planet visual pass — 2026-10-23

- Expanded the radius from 3,000 to 12,000 units. Replaced cube visuals with four batched gothic industrial models and matching construction previews; added a spherical ocean, cloud/atmosphere shells, coherent terrain shading and an orbital sky.
- The expanded 26 scene checks passed in Unity 2022.3.2f1 (DX11), seed 1234, Standard. These include per-building mesh budgets and footprint bounds, camera framing, planet layers, gameplay/persistence and migration of a settlement saved at the old radius.
- After the compatibility pass, all 29 scene checks and all 29 executable checks passed. Version-3 saves record the terrain recipe; older saves preserve their original coastlines at the larger radius. A recorded original colony site remains land, and its terrain recipe survives a second save/load. Also loaded the existing version-2 disk save and visually confirmed all core buildings were on land with the new architecture, causeways and lighting. No warnings or errors remained in the Unity Console.
- Visually inspected the orbital and oblique colony views with seeds 1234 and 1376271481. New architecture, shadows, coastlines, ocean highlights, clouds, ice, atmosphere and stars rendered successfully. Art remains procedural, with no high-resolution imported model/texture pack.
- Standalone shader inclusion/performance and the full long-session acceptance checklist remain unverified.
