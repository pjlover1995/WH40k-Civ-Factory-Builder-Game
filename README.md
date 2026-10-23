# WH30K Colony Prototype

A Unity colony and industry prototype on a procedural planet. Use **Unity 2022.3.2f1** and the built-in render pipeline.

Open `Assets/Scenes/Prototype/WH30K_VS01.unity`, press Play, choose a seed and difficulty, and select **Begin**. The prototype scene is included in Build Settings.

During play, use **Next Building Type** to choose an outpost, housing, factory, or air scrubber. The placement button shows its construction cost. Green previews are valid; red previews indicate insufficient resources, water, or an occupied footprint. Left-click to build; Esc or right-click cancels. Building costs are paid once and economic effects appear immediately.

Right-drag to orbit and scroll to zoom. **F** focuses the settlement; **Home** frames the planet. **F5** saves and **F9** loads; both actions also have visible HUD buttons.

The world has a 12,000-unit radius (four times the original radius and sixteen times its surface area). An oblique settlement camera keeps the colony readable at that scale. Gothic industrial buildings use weathered stone, iron, oxide roofs, brass, illuminated lancet windows, chimneys and filter towers. The planet has continuous terrain, a spherical ocean, drifting cloud cover, polar ice, directional lighting and an atmospheric rim against a star field. Older saves keep their original terrain recipe and reproject their settlement onto the expanded terrain.

Population and available staff limit output. Pollution reduces habitability, housing effectiveness, and output. Scrubbers and safety decisions improve conditions; furnace overdrive increases production and emissions. These are game abstractions, not physical climate models.

Saves include construction, occupied cells, population, policies, resources, environment, and the remaining event timer or unanswered decision. Original prototype saves remain loadable. Saves live at `Application.persistentDataPath/wh30k_vslice_save.json`; replacing an existing save preserves a `.bak` file.

## Verification

Run the engine-independent production rules and source syntax checks with the .NET 9 SDK:

```powershell
dotnet run --project Tests/Simulation/SimulationChecks.csproj -- .
```

These checks cover costs, economic effects, pollution, placement save validation, deterministic event continuation, and a ten-minute simulation. They do not compile or render Unity engine components.

For the actual scene checks, open the prototype scene in Unity, enter Play Mode, then select **WH30K > Reset Colony and Run Gameplay Checks (Play Mode)**. This resets the in-memory colony without writing its save file. Its 29 checks cover real placement, resource charging, failed purchases, JsonUtility save/load, repeated loading, pending events, HUD controls, decision costs, legacy saves, building geometry budgets/footprints and expanded-world camera/save compatibility.

See `Docs/VERIFICATION.md` for the visual and disk checks and `Docs/PROJECT_BRIEF.md` for scope and priorities.
