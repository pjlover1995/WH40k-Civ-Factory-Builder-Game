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

The construction deck has Colony, Extraction and Processing pages. Geological survey cycles Off/All and iron/copper/stone/biomass/crystal filters, colored pins and route lines. Visit Nearest Deposit targets a matching site without changing the colony shortcut. Prospecting is deterministic from seed, terrain recipe and landing region; elevations/moisture constrain mineral and biomass placement. Mineral bodies have finite seeded reserves, different grades, extents and orientations; biomass patches retain their 2,000-unit limit. Physical outcrops indicate locations without the overlay.

Mines (280 credits) select iron, copper or crystals; quarries (200) extract stone; harvesters (210) gather biomass. Extractors need a deposit within 250 metres but can be built elsewhere. Smelters (360) convert two ore plus one biomass into one metal. Fabrication works (420) convert two metal, one stone and half a crystal into one component. Five staffed simulation seconds complete one cycle; tiers and industrial focuses modify throughput, upkeep and emissions. Each facility stores up to 120 of each material. No input is consumed unless the whole recipe and output storage are available.

Automatic atmospheric cargo haulers dispatch batches of up to three units to the closest suitable consumer, reserve incoming storage and deliver only on arrival. Components delivered to the active core factory earn 25 credits each. Vehicles follow surface-relative flight arcs rather than a road network. Deposits, local inventories, partial work and in-flight cargo persist; old saves initialize empty industrial stocks. The new industrial buildings also retain normal workforce/upkeep/environment effects. Machinery rotates only while operating and the simulation is running.

## Ore bodies, excavation and neighbouring facilities

Schema 6 stores eight directional ore reserves, extracted quantities, four-metre working benches, protected directions, foundation elevations and unanswered expansion decisions. Buildings constructed on an excavated floor retain their height on load. Iron is an elongated banded lens (50–65% estimated grade; 1,800–5,000 recoverable gameplay lots), copper a broader disseminated body (0.3–1.2%; 900–3,000 concentrated ore lots), crystals a narrower hydrothermal vein (350–1,200 lots), and stone a bedded deposit (3,500–9,000 lots). These are deliberately compressed game deposits, not real-world tonnage predictions. Grades describe host material; production lots represent recoverable ore/concentrate, preserving the existing factory recipes. Geological references: [Geoscience Australia iron](https://www.ga.gov.au/education/minerals-energy/australian-mineral-facts/iron) and [USGS porphyry copper model](https://pubs.usgs.gov/sir/2010/5070/b/pdf/SIR10-5070B.pdf).

Extraction widens and deepens working sectors, modifying the same spherical heightfield used by placement and camera clearance. Local LOD meshes refresh and a dense, banded pit surface shows benches. Vegetation is removed from worked ground. Existing foundation pillars remain; flooded cuts follow the global ocean level. Exhausted deposits retain their pits. Ore does not regenerate, and a stopped mine keeps ore physically in the ground.

Before a cut reaches another building's footprint plus a 12-metre safety margin, mining raises a persistent decision and pauses simulated time. Demolish the threatened player-built facility, or limit that working direction. Demolition removes its economic contribution, inventory and linked in-flight cargo without a refund; all other inventories and cargo endpoints are remapped. Core facilities require a directional limit. The next cut checks every remaining neighbour again. Narrative choices and mining choices retain separate pause state and cannot overwrite each other's callbacks.

Select a mine or quarry, then **Excavation Plan** to review reserves, grade, depth and individual limits. Eight sector buttons can restrict or reopen directions; reopening checks obstructions again before extraction. **Inspect Cuts on Planet** shows lettered planning outlines (gold open, red limited) with the survey overlay. Blocking directions lowers accessible ore without deleting remaining geological reserves. The mine's own foundation is retained on a pillar. Biomass harvesting does not excavate a pit.

Version-5 colonies preserve deposit IDs, remaining quantities, inventories, cargo, buildings and terrain. Their original 2,000-unit capacities migrate into directional reserves with proportional historical excavation; newer geological sizes apply to new worlds. Older schemas retain their existing migration paths. Underground tunnels, geotechnical slope stability, drilling uncertainty, waste/tailings transport and reclamation are outside this prototype.

## Runtime performance

Building registries and operation effects are cached until state changes; shared architecture materials support GPU instancing. Terrain refinement is limited to eight new patches per frame, with visible parents retained until children are ready. Local vegetation and detailed water rebuild over multiple frames. Cargo reuses pooled visuals and cached sampled arcs, with bounded route caching. Survey presentation refreshes at 10 Hz, while camera and sun work skip unchanged state. Dispatch caches consumers and incoming reservations. Save schema 6 and terrain recipes remain unchanged.

See [the performance review](PERFORMANCE_REVIEW.md) for measured editor results, tradeoffs, reproduction instructions and remaining scale limits. The short editor benchmark does not establish standalone target frame rates.

## Limits and next priorities

- There is one recurring narrative event, an aggregate credit stockpile and seven physical industrial materials. Only components currently export into credits.
- Architecture and surface materials are procedural modular art, without authored high-resolution textures or animated inhabitants. Causeways are visual decoration. Cargo uses automatic atmospheric routes, without ground road pathfinding, player-authored routes, research, combat or multiple independent settlements.
- The starting core uses the original fixed building classes; player construction uses ScriptableObject definitions. Both are aggregated once in the settlement economy.
- Engine-independent checks run without Unity. Scene rendering, engine compilation, interaction, and standalone performance must be verified in Unity before release.
- Next acceptance work: balance the extraction economy, profile release-player GPU/memory costs and colonies approaching the construction limit, and expand transport management only after the automatic loop is reliable.
