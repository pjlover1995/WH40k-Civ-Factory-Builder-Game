# Prioritized code and game review — 2026-10-29

The review covered the runtime source inventory, active bootstrap/scene wiring, simulation and save validators, construction/development, UI, terrain/ecology, logistics and rendering hot paths. It used the existing executable and Unity scene suites to cover systems beyond the changed code. This is a prioritized engineering review, not an exhaustive proof that every path is error-free. Existing uncommitted work was preserved.

## Completed priorities

1. **Correct multi-generator fuel accounting.** The supply pass respected fuel limits, but the consumption pass allocated nominal capacity. A nearly empty first generator could retire demand it could not sustain, causing another generator to deliver power without paying its corresponding fuel. Dispatch now uses actual sustainable output and retains unmet demand for subsequent generators. Trace fuel uses the same rule in both passes. Pure rules and actual scene tests cover fuel totals, power, pause and save/load.
2. **Bound route presentation work and memory.** Opening a dense Routes overlay previously generated the entire mesh synchronously. Candidate traversal and terrain sampling now continue across frames, with explicit budgets, cancellation, invalidation, cached completion and a decorative display cap. The UI reports preparation and the display limit. Full logical connectivity remains independent.
3. **Remove recurring farm scratch allocations.** Reuse the estate-operation dictionary and keep fertilizer snapshots as scalars. Reset the dictionary on world replacement so it does not retain previous-world buildings. Production, fertilizer spending and growth rules remain unchanged.
4. **Make verification reflect asynchronous behavior.** The benchmark waits for a completed overlay and records per-frame work and CPU duration. Reliability checks exercise pause, hide/show, demolition and terrain invalidation during work, exact completed vertex counts, cache reuse, display limiting, HUD states and loading. Existing IDs, scene/GUID references and schema 16 are retained.

## Remaining priorities

| Priority | Finding | Next bounded task |
| --- | --- | --- |
| High | Starter water balance is severe: 600 water / 12.05 water per second is about 50 simulation seconds at the initial 6,000 crew, before growth. One 1.2/s groundwater pump supplies about 10% of that demand. | Run a timed normal expedition, then tune starting life support, pump output or demand together with starter power and the finite manifest. Preserve exact stored reserves; avoid an automatic save refill. No balance change is hidden in this patch. |
| Medium | Logical network rebuilds still compare building pairs; presentation budgets do not bound this simulation-side cost. | Profile construction/demolition near the 4,096-building limit and introduce a spatial neighbour index if measurements warrant it. |
| Medium | Terrain traversal still has frame-time spikes; new-world generation and some excavation rebuilds are synchronous. | Capture CPU/GPU profiles in a standalone player before changing terrain quality or streaming ownership. |
| Medium | The route display cap intentionally omits some decorative links in exceptionally dense colonies. | Consider a selected-facility route view if players need to inspect every connection. Actual cargo/power remains complete. |
| Acceptance | This pass runs actual automated Play Mode and rendered Editor checks. | Manual ten-minute play, a current standalone build, GPU/memory soak and additional hardware remain separate acceptance work. |

See [verification](VERIFICATION.md) for executed checks and [performance review](PERFORMANCE_REVIEW.md) for measured data and reproduction details. Tests create temporary worlds and do not call the normal disk-save action.
