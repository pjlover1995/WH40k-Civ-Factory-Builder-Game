# Project brief

## Intended experience

Build a colony on a procedural planet, grow industry, and respond to the environmental and social cost of expansion through construction and policy decisions.

## Current milestone

One coherent ten-minute session: begin a world, construct a useful building, see its economic/environmental effects, make a consequential industrial policy decision, save, and reload with the same colony and pending event state.

The starting core contains a Hab Block, Macro Factory, and Utility Nexus. Player construction shares the settlement economy through data-driven building definitions; its visuals and grid positions are persisted separately from the starting core to avoid duplicated buildings on load.

| Building | Cost | Housing | Jobs | Output/worker | Upkeep/second |
| --- | ---: | ---: | ---: | ---: | ---: |
| Frontier Outpost | 250 | 160 | 60 | 1.4 | 8 |
| Hab Block | 180 | 320 | 40 | 0.15 | 8 |
| Macro Factory | 320 | 0 | 120 | 2.2 | 16 |
| Air Scrubber | 220 | 0 | 30 | 0 | 6 |

Workers are distributed proportionally across jobs. Housing permits population growth; factories improve output; scrubbers consume upkeep and staff while cleaning air and water. Difficulty changes starting resources, yields, environmental harshness, and event frequency.

Habitability begins to fall above 600 CO2, below 19.5 oxygen, above 10 water pollution, or beyond a two-degree temperature offset. It is bounded between 25% and 100% and affects population targets, staffing, and production. Safety choices reduce future harmful emissions as well as cleaning existing damage; overdrive raises future emissions. Cleanup benefits are not multiplied by industrial policy.

## Limits and next priorities

- There is one recurring narrative event and one aggregate stockpile.
- Buildings are placeholder primitives; there are no transport networks, production recipes, research, combat, or multiple independent settlements.
- The starting core uses the original fixed building classes; player construction uses ScriptableObject definitions. Both are aggregated once in the settlement economy.
- Engine-independent checks run without Unity. Scene rendering, engine compilation, interaction, and standalone performance must be verified in Unity before release.
- After validating this loop, improve readability and balance from play sessions before expanding content or terrain complexity.
