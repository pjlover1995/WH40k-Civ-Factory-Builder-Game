# WH30K Colony Prototype

A Unity colony and industry prototype on a procedural planet. Use **Unity 2022.3.2f1** and the built-in render pipeline.

Open `Assets/Scenes/Prototype/WH30K_VS01.unity`, press Play, choose a seed and difficulty, and select **Begin**. The prototype scene is included in Build Settings.

During play, select a construction card for an outpost, housing, factory, or air scrubber, then choose **Deploy Structure**. Green previews are valid; red previews indicate insufficient resources, water, or an occupied footprint. Left-click to build; Esc or right-click cancels. Building costs are paid once and economic effects appear immediately.

Worlds open in the planetary view. **WASD** moves around the planet, **middle-drag** pans across the surface, **right-drag** rotates the view, and the **wheel** zooms between orbit and ground level. **Shift** accelerates movement. **F** optionally returns to the colony; **Home** returns to orbit. Selecting a building does not move or lock the camera. **F5** saves and **F9** loads; both actions also have visible HUD buttons.

Click a building to open its operational menu. Each type has three focuses with different capacities, output, costs or emissions. Upgrade individual buildings through three tiers (150 then 300 stockpile), or suspend/resume their operation. Upgrades and focuses affect the live colony economy and persist in saves. Buildings now use one fifth of the visual pass's dimensions to establish a stronger planetary scale.

The calendar advances days, months and years: **30 real minutes at 1x equal one game year**, with 2.5 minutes per month. Pause with **Space** or the pause button; use **1x / 3x / 10x** to change speed. Calendar, economy and events share the same simulation clock. Decisions pause simulated time; the separately displayed real play-time counter continues while the game is active, including paused planning. Version-4 saves preserve both clocks, speed, pause and individual building settings. Older saves start their clock at zero because they contain no historical play-time data.

The world has a 12,000-unit radius (four times the original radius and sixteen times its surface area). An oblique settlement camera keeps the colony readable at that scale. Gothic industrial buildings use weathered stone, iron, oxide roofs, brass, illuminated lancet windows, chimneys and filter towers. The planet has continuous terrain, a spherical ocean, drifting cloud cover, polar ice, directional lighting and an atmospheric rim against a star field. Older saves keep their original terrain recipe and reproject their settlement onto the expanded terrain.

Population and available staff limit output. Pollution reduces habitability, housing effectiveness, and output. Scrubbers and safety decisions improve conditions; furnace overdrive increases production and emissions. These are game abstractions, not physical climate models.

Saves include construction, occupied cells, population, policies, resources, environment, and the remaining event timer or unanswered decision. Original prototype saves remain loadable. Saves live at `Application.persistentDataPath/wh30k_vslice_save.json`; replacing an existing save preserves a `.bak` file.

## Verification

Run the engine-independent production rules and source syntax checks with the .NET 9 SDK:

```powershell
dotnet run --project Tests/Simulation/SimulationChecks.csproj -- .
```

These checks cover costs, economic effects, pollution, placement save validation, deterministic event continuation, and a ten-minute simulation. They do not compile or render Unity engine components.

For the actual scene checks, open the prototype scene in Unity, enter Play Mode, then select **WH30K > Run Gameplay Checks (Play Mode)**. This temporarily replaces the world, then restores the current colony; its disk save is unchanged. A safety copy of the review colony is kept in .utmp/colony-before-gameplay-checks.json. Its checks cover placement, charging, JsonUtility persistence, events, HUD controls, camera navigation, scaled geometry, building menus/focuses/upgrades and calendar/save compatibility. **F12** reveals the editor-only developer panel; it is hidden during normal play.

See `Docs/VERIFICATION.md` for the visual and disk checks and `Docs/PROJECT_BRIEF.md` for scope and priorities.
