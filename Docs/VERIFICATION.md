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
3. Select **WH30K > Reset Colony and Run Gameplay Checks (Play Mode)**. Expect 20 passing scene checks in the Console. The check intentionally replaces the current in-memory colony and does not write a save.

## Visual and disk acceptance checks

1. Begin a new expedition. The camera faces the starting settlement. At 1280x800 and 1920x1080, all HUD labels, building details, buttons, and event choices fit their panels.
2. Select each building type. Cost and economy details update. Place an affordable outpost on land: stockpile drops by 250 once and production/upkeep change immediately. Place housing to permit further population growth.
3. Move the preview over water, an existing building, or the starting core. Placement is blocked. Spend down the stockpile: unaffordable construction remains blocked. Clicking HUD buttons or an event does not build behind the UI; Esc cancels with the pointer off the planet.
4. Build factories and choose overdrive several times. Worsening conditions reduce habitability and output. Build scrubbers or fund safety retrofits: pollution improves. An unaffordable safety retrofit is disabled until funds are available.
5. Press F5 while an event awaits a choice. Build another structure or resolve the choice, then press F9. The saved buildings and stockpile return, the unanswered choice reappears, and loading again does not duplicate output or charge construction again.
6. Resolve the restored event. The next event eventually appears. Continue for ten minutes, monitoring frame time and resource/environment changes.
7. Exit/re-enter Play Mode and load the disk save. Verify the same structures and policies return. Confirm a second save creates the `.bak` file and a malformed save is rejected without replacing an active colony.
8. Build and launch a standalone player. Verify the scene, procedural material, HUD, save/load, and building assets work without the editor-only debug panel.

Record Unity version, seed, difficulty, check output, screenshots, and any observed performance issues when completing these checks.

## Review run — 2026-10-23

- Unity 2022.3.2f1 on Windows, built-in renderer (DX11); seed 1234, Standard difficulty.
- Unity compiled the editor and gameplay assemblies successfully. All 20 real-scene gameplay checks passed in Play Mode; the Console showed no warnings or errors after the scene reload.
- All 29 executable checks passed, including editor/player syntax parsing; `git diff --check` passed.
- Visually confirmed settlement and planet framing, rendered terrain/buildings, readable HUD and event choices, and growing population/stockpile. F5 saved to disk and F9 restored the saved stockpile and colony in the running editor.
- Corrected the scene's non-normalized camera rotation and explicit empty projection, and resolved the scene camera again when starting or loading a colony to avoid Awake-order dependence.
- The full visual/disk checklist above, both exact target resolutions, a ten-minute engine performance run, and a standalone player build still need acceptance testing. Terrain and buildings retain the prototype's procedural visuals and placeholder shapes.
