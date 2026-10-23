# Project brief

## Intended experience

Build a colony on a procedural planet, grow industry, and respond to the environmental and social cost of expansion through construction and policy decisions.

## Current milestone

One coherent ten-minute session: begin a world, construct a useful building, see its economic/environmental effects, make a consequential industrial policy decision, save, and reload with the same colony and pending event state.

The starting core contains a Hab Block, Macro Factory, and Utility Nexus. Player construction shares the settlement economy through data-driven building definitions; its visuals and grid positions are persisted separately from the starting core to avoid duplicated buildings on load.

## Visual direction and scale

Gothic industrial architecture: weathered limestone and forged iron, oxide-red roofs, aged brass, buttresses, pointed windows and warm lumen strips. Each building has a distinct mesh silhouette. Factories have furnace stacks and roof bays; hab blocks have stacked apartments and spires; outposts are fortified keeps; scrubbers have paired filter towers, intake vanes and teal status lights. Core buildings are connected by service causeways. Built structures and placement previews share the same geometry, constrained by their placement footprints, with seven shared material groups per building.

The planet radius is 12,000 Unity units, up from 3,000, so surface area is sixteen times larger. Terrain relief remains small relative to the globe. Coherent biome shading, spherical oceans, layered clouds, atmospheric scattering, polar ice, shadows and an orbital star field establish the planetary view. F switches to a close oblique colony orbit; Home shows the hemisphere containing the colony. Terrain patches keep a bounded vertex density as their quadtree subdivides, with skirts covering LOD boundaries. Version-3 saves store the terrain recipe. Older saves retain their original coastline mapping, building IDs and grid cells, and their settlement anchors are reprojected onto the expanded terrain.

| Building | Cost | Housing | Jobs | Output/worker | Upkeep/second |
| --- | ---: | ---: | ---: | ---: | ---: |
| Frontier Outpost | 250 | 160 | 60 | 1.4 | 8 |
| Hab Block | 180 | 320 | 40 | 0.15 | 8 |
| Macro Factory | 320 | 0 | 120 | 2.2 | 16 |
| Air Scrubber | 220 | 0 | 30 | 0 | 6 |

Workers are distributed proportionally across jobs. Housing permits population growth; factories improve output; scrubbers consume upkeep and staff while cleaning air and water. Difficulty changes starting resources, yields, environmental harshness, and event frequency.

Habitability begins to fall above 600 CO2, below 19.5 oxygen, above 10 water pollution, or beyond a two-degree temperature offset. It is bounded between 25% and 100% and affects population targets, staffing, and production. Safety choices reduce future harmful emissions as well as cleaning existing damage; overdrive raises future emissions. Cleanup benefits are not multiplied by industrial policy.

## Command interface, navigation and time

The command HUD separates resources and time in the header, planetary conditions/journal on the left, construction cards below, and a contextual building inspector on the right. Developer controls are hidden behind F12 in the editor. The camera opens in free orbit, supports WASD navigation, middle-drag surface panning and right-drag rotation, and can cross from orbital to local views by zooming. F is an optional colony shortcut; selecting a building never recenters the camera.

Architecture dimensions are multiplied by 0.2 relative to the first visual pass (a core hab block is now 39 units tall on the 12,000-unit-radius world). New construction uses a 256-cell-per-face grid; older saves retain their stored resolution. Core spacing and causeways use the same smaller scale.

The shared simulation clock advances one year per 1,800 real seconds at 1x, with twelve 150-second months and thirty days per month. Pause and 1x/3x/10x affect calendar, settlement and event progression together. Real play time counts unscaled time while a colony is active, including planning pauses. Decisions pause simulated time without locking camera/UI interaction. Version-4 saves include both elapsed times, speed/pause and each building's tier/focus/offline state; older saves default to balanced active buildings and a fresh clock.

Every core and player-built structure has its own operations state. Focus is free to change. Hab blocks offer high-density housing or civil administration; factories offer surge or clean production; utilities offer deep purification or economy cycles; outposts offer logistics or residential support. Focus tradeoffs appear in the inspector and alter simulation values. Upgrades cost 150 and 300 stockpile, adding 25% capacity/jobs and 15% upkeep per tier, with a maximum of tier III. Upgrades also scale environmental effects. Suspension removes the building's capacity, jobs, output, upkeep and environmental effects until resumed.

## Limits and next priorities

- There is one recurring narrative event and one aggregate stockpile.
- Architecture and surface materials are procedural modular art, without authored high-resolution textures or animated inhabitants. Causeways are visual decoration; there are no transport networks, production recipes, research, combat, or multiple independent settlements.
- The starting core uses the original fixed building classes; player construction uses ScriptableObject definitions. Both are aggregated once in the settlement economy.
- Engine-independent checks run without Unity. Scene rendering, engine compilation, interaction, and standalone performance must be verified in Unity before release.
- After validating this loop, improve readability and balance from play sessions before expanding content or terrain complexity.
