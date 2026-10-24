# Verification

## Automated checks available without Unity

From the repository root, run:

```powershell
dotnet run --project Tests/Simulation/SimulationChecks.csproj -- .
git diff --check
```

The executable links the actual production files `ColonyRules`, `EnvironmentRules`, `ConstructionSave`, `EventSchedule`, `IndustryRules`, and `MiningRules`. It exercises payment boundaries, capacity/output changes, environmental feedback and cleanup, save validation, event RNG/timer persistence, repeated decisions, and ten minutes of simulation. Roslyn also parses every C# source with and without `UNITY_EDITOR`. Parsing does not establish compatibility with Unity's engine APIs.

## Unity integration checks

1. Open the project with Unity 2022.3.2f1. Confirm there are no compiler errors or missing scripts.
2. Open `Assets/Scenes/Prototype/WH30K_VS01.unity` and enter Play Mode.
3. Select **WH30K > Run Gameplay Checks (Play Mode)**. Expect 87 passing scene checks in the Console. The checks temporarily replace the world, then restore the current colony without changing its disk save. Ctrl+Alt+G also runs this command.

Save with F5 before changing scripts during Play Mode. Unity domain reload clears runtime dictionaries, static settings and event callbacks. **WH30K > Back Up Active Colony (Play Mode)** (Ctrl+Alt+B) writes `.utmp/colony-before-gameplay-checks.json` without replacing the normal disk save. Its recovery path reconstructs missing building lists from scene components and preserves a visible unanswered decision; if the scheduler was lost during reload, its future random sequence restarts from the world seed. Restart Play Mode and load the recovered save before continuing review. Normal save/load preserves the exact scheduler state.

## Visual and disk acceptance checks

1. Begin a new expedition. The camera shows the planet. At 1280x800 and 1920x1080, all HUD labels, building details, buttons, and event choices fit their panels.
2. Select each building type. Cost and economy details update. Place an affordable outpost on land: stockpile drops by 250 once and production/upkeep change immediately. Place housing to permit further population growth.
3. Move the preview over an existing building or the starting core. Placement is blocked. Place on open water or a steep site: support piles appear and the chosen position is retained on load. Spend down the stockpile: unaffordable construction remains blocked. Clicking HUD buttons or an event does not build behind the UI; Esc cancels with the pointer off the planet.
4. Build factories and choose overdrive several times. Worsening conditions reduce habitability and output. Build scrubbers or fund safety retrofits: pollution improves. An unaffordable safety retrofit is disabled until funds are available.
5. Press F5 while an event awaits a choice. Build another structure or resolve the choice, then press F9. The saved buildings and stockpile return, the unanswered choice reappears, and loading again does not duplicate output or charge construction again.
6. Resolve the restored event. The next event eventually appears. Continue for ten minutes, monitoring frame time and resource/environment changes.
7. Exit/re-enter Play Mode and load the disk save. Verify the same structures and policies return. Confirm a second save creates the `.bak` file and a malformed save is rejected without replacing an active colony.
8. Build and launch a standalone player. Verify the scene, procedural material, HUD, save/load, and building assets work without the editor-only debug panel.
9. Press Home: inspect the planet silhouette, day/night lighting, oceans, ice, cloud cover and atmospheric rim. Press F: inspect each building's distinct architecture, foundation, shadows and service causeways. Zoom/orbit near the surface and across LOD boundaries; verify the camera stays above ground, no cracks appear and repeated loads do not accumulate visible shells or causeways.
10. Use WASD, middle-drag and right-drag to explore beyond the colony. Zoom between surface and orbit; verify clicking a building opens its own inspector without moving the camera. Change each building type's focus, upgrade it, suspend/resume it, then save/load to verify settings and economy survive.
11. Pause: calendar, stockpile and events stop while camera navigation and real play time continue. Resume at 3x/10x: calendar and economy accelerate together. Save/load while paused and while a decision is pending; verify calendar, played time, speed and pending decision restore correctly.

12. Cycle Survey through all minerals and biomass. Verify physical outcrops and colored pins, and use Visit Nearest Deposit for each filter. A mine's iron/copper/crystal focus changes which nearby deposit it extracts. A facility outside its 250-metre catchment is allowed but reports why it is idle.
13. Build mines for ore/crystals, a quarry, harvester, smelter and fabrication works. Confirm stock changes, deposit depletion and moving cargo. Suspend an extractor: its deposit stops depleting. Remove an input supply: the consumer waits without consuming other inputs. Components reaching an active core factory add 25 credits each.
14. Save with cargo in flight and partly completed production. Reload: local stock, incoming storage reservations, cargo progress and remaining deposits persist. Older version-4 colonies retain buildings, credits and terrain while initializing industry empty.
15. At 1x, watch a three-minute lighting cycle; pause and change speed to verify lighting, waves and orbital positions follow simulated time. Inspect the moon silhouettes, sun and distant world from orbit. On ocean coasts, zoom in to inspect swells/foam and pan across the local water mesh boundary.
16. In forests/wetlands, pan beyond the colony: vegetation tiles change without unbounded object growth. Check mineral outcrops and new building models remain distinguishable at colony zoom. Verify all three construction pages and the resource inspector at the target resolutions.

17. Open a mine or quarry's Excavation Plan. Review finite reserves, accessible/protected ore, grade, depth and eight sector controls. Limits and reopened directions must survive save/load; Inspect Cuts shows lettered gold/red outlines.
18. Work a mineral body: cuts widen, descend into benches and clear vegetation. Biomass must not excavate. Exhausted or fully limited bodies stop producing ore and retain their cuts.
19. Place a facility ahead of a growing cut. A decision pauses simulation before the safety margin is crossed. Test limiting and demolition in a temporary world; unaffected inventories/cargo must persist. Core facilities require protection.
20. Save with mining and narrative decisions pending together. Both must retain callbacks and pause state. Build inside a pit, then reload and verify foundation elevation.

After a successful scene check run, **WH30K > Review Excavation Test World** (Ctrl+Alt+M) and **Review Mining Decision Test World** (Ctrl+Alt+N) load local test fixtures without changing the normal disk save. F9 restores the player colony. Avoid F5 in a test world.

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

## Navigation, command UI and calendar review — 2026-10-23

- Unity 2022.3.2f1 compiled the final gameplay and editor assemblies. All 37 real-scene checks passed, including reduced architecture scale, free camera movement, contextual inspector controls, focus effects, paid upgrades, suspension/resumption and JsonUtility round trips for building operations, calendar, played time, speed and pause. Console warning/error counters were both zero.
- All 39 executable checks passed, including editor/player parsing of all 32 C# sources. `git diff --check` passed.
- Clicking a rendered hab block opened its specific inspector without recentering the camera. Its selected high-density focus, tier-II statistics, upgrade cost and suspend control were visible. Orbital and close colony views rendered the new HUD and smaller gothic architecture.
- Preserved the active review colony after an editor script reload: seed 282164432, Standard, 14 constructed buildings plus the three core structures, stockpile 32,049.38, population 3,169.95 and the pending Industrial Priorities choice. Restarted Play Mode and loaded it from disk. Ran the 37 checks again with this colony active; the command restored it automatically. F5 then saved all 14 constructions, core buildings, clock and pending choice through the game normally.
- The review colony remains running in Unity with its unanswered choice. The recovery backup is local and excluded from Git. Exact target-resolution acceptance, a ten-minute engine performance run and a standalone player/shader build remain unverified.


## Geography, orbital bodies and industry — 2026-10-24

- Unity 2022.3.2f1 / DX11 compiled both editor and player assemblies. All **64 scene checks** passed on the final gameplay code, including all nine building mesh budgets, multiple terrain biomes and relief, celestial body creation, ocean/free placement, overlap rejection, resource catchments, extraction/depletion, the full hauler-to-smelter-to-fabrication-to-credit chain, suspended extraction, and exact JsonUtility round trips for inventory, work, depleted deposits and cargo in flight. The final Console warning/error counters were zero.
- All **52 executable checks** passed, including production recipes, no-consumption-on-missing-input/full-output cases, incoming cargo capacity, invalid transport endpoints, free directions/duplicate placement, deterministic lighting phase, earlier economy/event/calendar checks and editor/player parsing of all 37 C# sources. `git diff --check` passed.
- The old-save scene check exposed JsonUtility constructing an empty industry object for missing JSON fields. Explicit version migration now initializes industry empty for pre-version-5 saves; legacy terrain, construction and event checks pass.
- Visually inspected forest and orbital views in the editor, mixed/jittered vegetation, terrain relief, ocean/cloud/atmosphere rendering, a moon silhouette, colored survey pins, automatic cargo routes, and the mine inspector with its three mineral focuses and remaining deposit quantity. Survey filtering and Visit Nearest Deposit worked through actual mouse input. Machinery/cargo use simulated time. The moon surface was refined from regular grain into irregular crater basins and mottling.
- Built a Windows x64 development player successfully using WH30K > Build Review Player. Output is local in `.utmp/ReviewPlayer/WH30K Colony.exe`; it is excluded from Git. The player launched with a 1280x800 window request and produced no startup exception or shader error in its log. Computer-use approval for the separate executable timed out, so standalone on-screen interactions and exact-resolution layout acceptance remain unverified; editor interactions and automated scene checks were completed instead.
- Loaded the user's existing seed-282164432 version-4 colony and saved it as version 5. Compared its data with `.utmp/colony-before-geography-update.json`: all 16 placement identities/cells and individual operations, credits, pending event state and terrain recipe matched. Industry initialized 16 empty building stores. A second local migration backup is `.utmp/colony-after-geography-migration.json`.
- Geography recipe 2 applies to new worlds. Existing colonies gain survey/industry, ecology, waves and orbital presentation while keeping their saved geography. The sun's 180-second visual day and compressed moon paths are presentation cycles, not orbital physics. Vegetation, buildings and resources remain procedural art. Resource reward/upkeep balance, a timed large-colony performance run, the full three-minute visual lighting acceptance pass and both exact target resolutions still need dedicated play testing.


## Finite ore bodies and excavation — 2026-10-24

- All **69 executable checks** passed, including seeded reserve/grade/geometry variation, deeper/wider benches, directional exclusion, finite exhaustion, migration without refilling ore, mining-decision JSON validation and invalid foundation elevations. All **40 C# sources** parse for editor and player. Whitespace verification passed.
- Unity 2022.3.2f1 / DX11 compiled the final gameplay/editor assemblies. All **87 scene checks** passed on the final code in **19.5 seconds**, including actual terrain lowering and pit meshes, detection before undermining neighbours, paused extraction, exact pending-decision persistence, limiting/reopening, demolition, inventory/cargo remapping, protected core facilities, simultaneous narrative/mining choices, construction inside deep cuts and retained foundation elevation after load.
- The bulk version-5 migration fixture contains **164 deposits** with historical depletion. Its final run loaded in **3.70 seconds**. Spatial indexing bounds terrain sampling to nearby excavations; terrain mesh invalidations are batched. This is a bounded loading check, not a long-session frame-rate benchmark.
- Through actual Unity mouse input, opened Excavation Plan, limited a sector and observed lower accessible ore, inspected lettered red/gold outlines and rendered terraced cuts, viewed an expansion warning, chose protection, and inspected a threatened facility with the modal hidden while time remained paused. The return control reopened the same decision. The final scene-check Console showed zero warnings/errors.
- Loaded the existing seed-282164432 schema-5 colony and saved through F5 as schema 6. Compared it with `.utmp/colony-before-excavation.json`: all **16** construction identities, cells/directions and operations, full resource snapshot, seed, terrain recipe, pending narrative event and every deposit's remaining quantity matched. Re-ran the final scene checks with this colony active; it was restored automatically. Local migration copy: `.utmp/colony-after-excavation-migration.json`.
- The fixtures can be reviewed using the WH30K menu. Art remains procedural, ore quantities are compressed gameplay lots, and retained foundations are an abstraction. Underground workings, slope-stability engineering, waste/tailings transport, reclamation, exact-resolution layout acceptance and a sustained large-colony frame-rate run remain outside this verified slice.
- The updated Windows x64 development player built successfully from the final sources using **WH30K > Build Review Player**, including player assembly compilation. Output: `.utmp/ReviewPlayer/WH30K Colony.exe`. This build was not launched for standalone interaction testing; editor rendering and mouse/keyboard checks were completed.
