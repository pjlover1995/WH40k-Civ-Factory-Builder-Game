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

## Geography and physical resource chains

Terrain recipe 2 adds ridged mountain ranges and coherent hills. Moisture, elevation and latitude classify forests, swamps, grassland, arid hills and alpine ice. Terrain colors use the same CPU classification as local vegetation and prospecting. Close views stream deterministic batched trees, reeds, wetland pools and rocks. Recipes 0/1 remain available for old saves, preserving coastline and building positions. Waves use simulation time with vertex swells, dynamic normals, shallow foam and a local detailed water patch.

A sun, two moons and a distant gas world provide a stylized solar scene. One visual day is 180 simulation seconds, deliberately slower than the five-second calendar day; moon orbits are also compressed. The sun rotates relative to the fixed surface coordinates, preserving all placement anchors while producing day/night lighting. These are presentation orbits without gravity, seasons or eclipse simulation.

New construction records a finite unit surface direction and ignores grid snapping. Support piles permit water and steep sites. Costs, non-overlap and the building limit still apply. Older grid placements retain their stored grid coordinates. Schema 5 includes these free directions and industry state.

The construction deck has Colony, Extraction and Processing pages. Geological survey cycles Off/All and iron/copper/stone/biomass/crystal filters, colored pins and route lines. Visit Nearest Deposit targets a matching site without changing the colony shortcut. Prospecting is deterministic from seed, terrain recipe and landing region; elevations/moisture constrain mineral and biomass placement. Finite deposits each begin with 2,000 units. Physical outcrops indicate locations without the overlay.

Mines (280 credits) select iron, copper or crystals; quarries (200) extract stone; harvesters (210) gather biomass. Extractors need a deposit within 250 metres but can be built elsewhere. Smelters (360) convert two ore plus one biomass into one metal. Fabrication works (420) convert two metal, one stone and half a crystal into one component. Five staffed simulation seconds complete one cycle; tiers and industrial focuses modify throughput, upkeep and emissions. Each facility stores up to 120 of each material. No input is consumed unless the whole recipe and output storage are available.

Automatic atmospheric cargo haulers dispatch batches of up to three units to the closest suitable consumer, reserve incoming storage and deliver only on arrival. Components delivered to the active core factory earn 25 credits each. Vehicles follow surface-relative flight arcs rather than a road network. Deposits, local inventories, partial work and in-flight cargo persist; old saves initialize empty industrial stocks. The new industrial buildings also retain normal workforce/upkeep/environment effects. Machinery rotates only while operating and the simulation is running.

## Limits and next priorities

- There is one recurring narrative event, an aggregate credit stockpile and seven physical industrial materials. Only components currently export into credits.
- Architecture and surface materials are procedural modular art, without authored high-resolution textures or animated inhabitants. Causeways are visual decoration. Cargo uses automatic atmospheric routes, without ground road pathfinding, player-authored routes, research, combat or multiple independent settlements.
- The starting core uses the original fixed building classes; player construction uses ScriptableObject definitions. Both are aggregated once in the settlement economy.
- Engine-independent checks run without Unity. Scene rendering, engine compilation, interaction, and standalone performance must be verified in Unity before release.
- Next acceptance work: balance the extraction economy, profile large colonies and local ecology generation, and expand transport management only after the automatic loop is reliable.
