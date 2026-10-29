# Runtime performance review

## Incremental routes and generator accounting — 2026-10-29

The code review prioritized a reproducible economy defect, the previously measured Routes hitch, and recurring farm scratch allocations. Existing uncommitted work was retained. See [the review record](CODE_REVIEW.md) for scope and remaining priorities.

Routes now examines at most **2,048 candidate pairs** and generates at most **256 segments per game frame**. It reuses vertex/index buffers, samples shared segment endpoints once, publishes a complete mesh, cancels hidden work and restarts after geometry/registry changes. A **65,536-segment / 262,144-vertex display limit** bounds mesh/buffer memory; logical logistics, power and cargo are unaffected. The HUD distinguishes pending geometry from a capped display. Final mesh upload remains synchronous but is bounded by that display limit.

In graphics-enabled Unity 2022.3.2f1 / DX11 on the GTX 1660 Ti at 1280×800, the **189-facility / 184,676-vertex** fixture built over **181 frames**, with **181.044 ms total measured builder CPU time** and a **4.075 ms maximum slice**, including final mesh upload. Wall time was **2,314.3 ms**, including rendering and intervening frames: the tradeoff is a delayed complete overlay. The earlier review recorded **324.6 ms** for one synchronous build. These are separate observations, not a controlled whole-game speedup comparison or an FPS guarantee.

| Rendered scenario | Mean interval | p99 | Maximum |
| --- | ---: | ---: | ---: |
| Orbit | 2.024 ms | 3.761 ms | 7.520 ms |
| Close surface | 10.235 ms | 14.173 ms | 14.482 ms |
| Surface traversal | 10.490 ms | 27.449 ms | 34.707 ms |
| 189 facilities | 10.523 ms | 15.060 ms | 18.010 ms |

Raw data: [rendered review run](Performance/2026-10-29-incremental-routes.csv). These short Editor measurements exclude overlay UI. Do not infer a general draw-call reduction from the dense-case counter: its rendering/animation timing varies, and this change targets route generation rather than steady rendering. Allocation-counter zeros remain unavailable measurements. Farm simulation removes an explicit per-step dictionary allocation and per-farm fertilizer array; no measured whole-game GC reduction is claimed.

Generator regression coverage includes trace fuel, multiple generators, insufficient total fuel, staffing, elapsed time, pause and exact save compatibility. The supplied second generator now pays for demand the exhausted first generator cannot meet. This intentionally changes fuel consumption in affected multi-generator colonies without refilling stocks or migrating saves.

## Reliability and current-world measurement — 2026-10-28

The current half-Earth world was measured with explicit **1280×800 offscreen rendering** in graphics-enabled Unity 2022.3.2f1, DX11, NVIDIA GTX 1660 Ti. Batch Play Mode does not automatically render Game view: an initial run with zero draw calls was discarded as a rendering benchmark. `PerformanceChecks.RunBatch` now renders the distant and foreground cameras explicitly; screen-space overlay UI is not included in that offscreen image. These are short Editor frame intervals, not standalone FPS guarantees or a comparison against October 24's different world and fixture.

| Scenario | Mean | p99 | Maximum | Mean draw calls |
| --- | ---: | ---: | ---: | ---: |
| Orbit | 2.673 ms | 4.802 ms | 10.032 ms | 89 |
| Close surface | 10.266 ms | 14.118 ms | 16.358 ms | 490 |
| Surface traversal | 10.518 ms | 28.828 ms | 42.666 ms | 337 |
| 189 constructed facilities | 10.587 ms | 14.822 ms | 24.430 ms | 997 |

Raw report: [rendered current-world measurement](Performance/2026-10-28-rendered-current.csv). The fixture supplies actual construction materials, attempts 192 unrestricted facilities around the colony and confirms at least 180 succeed; 189 succeeded in this run. It no longer assumes that credits fund buildings or that a mine can be placed away from a compatible deposit. Allocation-counter zeros still mean unavailable, not zero allocation. The single-run traversal tail remains a visible limit; no ten-minute soak, standalone target or before/after speedup is claimed.

The dense Routes regression generated **184,676 vertices** with 32-bit indices. Its first on-demand build took **324.6 ms** in the initial non-rendered fixture, separate from steady-state rendering. The October 29 incremental builder above replaces this synchronous path. Hidden routes do not repeat mesh generation when a building registry changes; logical power/cargo connectivity remains independent. Route meshes do not cast shadows.

Five distinct forest tiles plus overlapping rebuilds retained **1,320 trees** at the end and peaked at **2,643** during replacement. The completed-tile limit is 1,681; two incremental tiles can coexist. Retired objects are destroyed, overlapping trees retain their instance and position, and full grid addresses avoid identity collisions. Metre-scale cell spacing now holds on continental worlds. Stationary dozers avoid repeated height samples, and exact direction comparisons prevent small local camera moves from being skipped. See [verification](VERIFICATION.md) for scene and visual results.

## Historical comparison — 2026-10-24

Target: Unity 2022.3.2f1, built-in renderer, `WH30K_VS01`. Baseline source: `91267428ba2a92b01d584873b6d8866cfd86dd7d`.

## Review and changes

The runtime source tree, editor verification tools, scene wiring and six project shaders were reviewed for recurring work, rendering cost, allocations and large synchronous operations.

| Area | Finding and action |
| --- | --- |
| Building placement / settlement | `Buildings` previously constructed a list and array and performed a component lookup for every building on every read. Cache the ordered array until placement, demolition, reset or world attachment. Industry detects registry identity changes, including a replacement that leaves the count unchanged. Inventory/save ordering is retained. |
| Building operations | Cache economy and environmental impact until configure, restore, focus, upgrade or suspension changes them. Machinery caches its building component and skips paused updates. |
| Architecture / shadows | Enable GPU instancing on the seven shared Standard materials and cargo material. Geometry, material quality and shadow settings are retained. Instancing support depends on the renderer/hardware. |
| Player shader packaging | The first actual player launch found that procedural `Shader.Find("Standard")` references were stripped. Include opaque/emissive Standard, Unlit/Color and sprite-route reference materials in Resources. Retain instancing variants explicitly because scene analysis cannot see runtime renderers. This follows [Unity's built-in instancing guidance](https://docs.unity3d.com/2022.3/Documentation/Manual/gpu-instancing-shader.html). |
| Terrain LOD | At most two complete splits (eight child patches) per frame. The visible parent covers unfinished work; deferred splits continue the next frame. Reuse mesh buffers and topology on excavation refreshes. Derive interior normals from existing surface vertices; retain shared height-sampled boundary normals. Reuse each vertex's height for biome classification. Terrain recipes, heights and placement rules are unchanged. |
| Ecology | Remove recurring scene-wide building scans. Snapshot local building positions once per tile and compare squared distances. Generate four rows per frame, retaining the previous vegetation until replacement is ready. Density and deterministic cell RNG remain unchanged. |
| Water / orbit | Generate eight wave-mesh rows per frame and reuse buffers/topology. Check water proximity at 10 Hz. Skip orbit/global-light updates when simulated time has not changed. Remove the competing landing-site light rotation so paused lighting agrees with the sun. |
| Industrial dispatch | Cache consumers by material when the building registry changes. Sum incoming cargo once per dispatch and update reservations as shipments launch. Use squared distance for the same nearest-consumer ordering. |
| Cargo | Pool hauler/route objects, bounded by the existing 64 active shipments. Cache sampled flight arcs and durations instead of evaluating terrain twice per ship per frame. Samples are normally 12 m apart (25–4,097 points); the route dictionary holds at most 64 entries. Clear route keys when buildings change. Active cargo retains its route and remaps its endpoints on demolition. Restore excavation before constructing loaded routes. Arrival/payment/progress semantics remain unchanged. |
| Survey / mining | Refresh survey presentation at 10 Hz, immediately on overlay changes, and avoid redundant activation calls. Skip excavation-refresh allocation entirely when no deposit is dirty. Mining conflicts still present every frame. |
| HUD / camera | Cache camera/industry references, speed/survey labels, material totals buffer and button tone states. Skip camera ground sampling and transform writes when the view and terrain are unchanged. Cache the colony marker's terrain point by surface revision. |
| Simulation / persistence | Pure colony, environmental, calendar, event, recipe, mining and validation rules remain unchanged. Snapshot arrays are intentional at save boundaries. Save schema remains 6; no building IDs, asset GUIDs, scene paths or user saves changed. |
| Other code | Core building definitions and ScriptableObjects are cold configuration paths. `PlanetGenerator` and `OrbitCameraController` are legacy implementations outside the playable scene. The old grid overlay is hidden by free placement. Avoid optimizing these at the expense of the active loop. |
| Shaders | Terrain/cloud procedural fragment noise, atmospheric transparency and shadow maps remain potential GPU costs. No speculative shader-quality reduction was made without a GPU capture. |

## Repeatable measurement

The menu labels select report filenames; they measure the current checkout and do not switch revisions. To reproduce the historical baseline in a separate checkout, copy `Assets/Editor/PerformanceChecks.cs` and its `.meta` into the baseline revision, then run its Baseline command with the same editor layout. The fixture detects the newer subdivision counter through an optional delegate, so it also compiles against the older runtime. Keep a copy of each CSV before repeating a label.

In Play Mode use **WH30K > Performance > Measure Baseline** (Ctrl+Alt+O) or **Measure Optimized** (Ctrl+Alt+J). Keep the Game view, editor layout and foreground/background state unchanged; do not interact with the game during measurement. Each command temporarily replaces the world, backs up the active colony (or existing disk colony) in `.utmp/performance/colony-before-measurement.json`, and restores it afterwards without writing the normal save. Cancel through the menu to restore early.

The fixture uses seed 1234, Standard difficulty, three seconds of warm-up and eight seconds of sampling per case. VSync and the frame cap are temporarily disabled and restored afterwards. It samples the orbit, a close surface view, surface navigation, and a dense active industrial colony. The last case attempts 192 placements; **189 succeed**, because three starting-core overlaps are correctly rejected. It seeds 64 shipments. The CSV name `industry_192` denotes attempted placements, not the final building count. The registry checksum is 1,890,000 for 10,000 reads.

CSV frame intervals include editor work, rendering and system scheduling. `mean_fps` is the reciprocal of the mean observed interval, **not a standalone FPS result or CPU/GPU profiler attribution**. GPU: NVIDIA GeForce GTX 1660 Ti; DX11; Game view 1255×675. One baseline and final optimized runs use the same setup. Absolute timings vary with editor/system load; compare frame distributions and draw calls, not just the best frame.

The Mono allocation counter returned zero even for the known-allocating baseline. Therefore the CSV `bytes_per_frame` zeros are **unavailable measurements**, not proof of zero allocations. Collection counts include the editor. Registry reuse is checked by object identity in engine tests, and its isolated 10,000-read time is logged separately.

## Measured results

Raw reports: [baseline](Performance/2026-10-24-baseline.csv), [optimized](Performance/2026-10-24-optimized.csv), and [optimized repeat](Performance/2026-10-24-optimized-repeat.csv). These compare the original code with the final code using matching initial daylight. An intermediate optimized run exposed a paused-lighting mismatch and was excluded from this comparison. The table shows the first corrected run; the repeat is retained below rather than selecting only the faster result.

| Scenario | Mean interval before → after | Observed editor FPS before → after | p99 interval before → after | Draw calls before → after |
| --- | ---: | ---: | ---: | ---: |
| Orbit | 7.555 → 5.981 ms | 132.36 → 167.20 | 14.391 → 10.978 ms | 69 → 69 |
| Close surface | 7.249 → 5.654 ms | 137.95 → 176.88 | 14.165 → 9.719 ms | 274 → 274 |
| Surface traversal | 8.094 → 6.245 ms | 123.55 → 160.13 | 84.299 → 21.110 ms | 144 → 143 |
| 189 constructed buildings | 13.329 → 9.693 ms | 75.02 → 103.17 | 23.234 → 15.586 ms | 6,796 → 3,856 |

Dense-colony mean frame interval improved by 27%; draw calls fell 43%. The maximum observed traversal interval fell from 196.821 to 50.377 ms. The stationary surface views have exactly matching reported triangle counts before/after; traversal triangle counts vary slightly as frame timing changes incremental LOD convergence. The isolated ordered-registry test fell from 711.11 to 0.16 ms for 10,000 reads with the same checksum. This microbenchmark measures query overhead, not a corresponding whole-game speedup.

The repeat produced **80.12 FPS / 12.481 ms** for the dense colony, with **4,527 draw calls** and a **24.699 ms p99**. Across both corrected runs this is **80–103 editor FPS**, **6–27% lower mean frame interval**, and **33–43% fewer draw calls** versus the one baseline. Dense-colony tail latency did not improve consistently: the repeat p99 exceeded the baseline's 23.234 ms, and its 30.076 ms maximum exceeded the baseline's 27.250 ms. Editor/system load and the active fixture's moving lighting/cargo introduce variation; there is no isolated CPU/GPU attribution or standalone density result yet. Further profiling is needed before promising a dense-colony frame-time target.

Exploration improvements repeated more clearly: traversal mean was **6.175 ms**, p99 **15.960 ms**, maximum **46.356 ms**. The registry repeat was **0.34 ms** for the same 10,000 reads/checksum. The repeat also exercised the final portable fixture and subdivision-budget assertion. Both optimized runs are reported even where the second was slower.

## Verification and remaining work

Engine checks cover registry reuse/invalidation, same-count replacement, cargo delivery exactly once, visual reuse, route clearance on the fixture, GPU-instancing material flags and build references, outward normalized terrain normals, unchanged heightfield vertices, immediate sun agreement and save/load. Benchmark samples assert the eight-patch subdivision budget. Original patch directions give zero heightfield error; normalizing a generated 12,000-unit vertex again introduces up to 0.0432 m of floating-point/noise sampling error in this fixture, so the regression check uses the original directions.

The Windows x64 Development player was built and launched at 1280×800. A new expedition, close architecture/terrain, contextual factory inspector and existing-colony loading were visually checked. The initial missing-Standard-shader failure was repaired with explicit Resources material references and instancing retention; the rebuilt player log had no exceptions during those checks. The disk save's SHA256 stayed identical to the pre-review backup.

Incremental terrain/ecology/water work trades immediate high-detail refresh for shorter frames; coarse parent terrain and the global ocean remain visible while refinement completes. Cached mesh buffers and route samples retain some managed memory to reduce repeated allocation. The route dictionary and ship pool are bounded; this is not a general streaming/memory-budget system.

The editor benchmark is a short fixture, not a ten-minute soak or a release-player performance certification. Follow-up profiling should use a standalone build at the intended resolutions, a GPU capture for shaders/shadows and a memory capture over repeated world loads. A 4,096-building colony, many simultaneous adjacent pit rebuilds, and production-chain balancing still need dedicated scale testing. New-world presentation and deposit generation still run synchronously on load. Pits still rebuild affected meshes synchronously when a bench changes. No universal target frame rate is claimed.
