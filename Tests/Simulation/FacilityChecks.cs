using System;
using System.Linq;
using System.Text.Json;
using WH30K.Sim;

internal static class FacilityChecks
{
    public static void Run(Action<bool, string> check)
    {
        float[] Inputs() => new float[] { 100, 0, 100, 100, 0, 0, 0, 100, 0, 0, 0, 0 };
        var config = new FacilityConfiguration();
        check(FacilityRules.Valid(config) && FacilityRules.ActiveCount(config) == 1 && config.products[0] == 0,
            "manufactorums default to the established raw-material component recipe at full speed");
        for (var lines = 1; lines <= 4; lines++)
        {
            config = new FacilityConfiguration();
            for (var slot = 1; slot < lines; slot++) FacilityRules.SelectProduct(config, slot, slot);
            var stock = Inputs(); var produced = new float[IndustryRules.MaterialCount]; var consumed = new float[IndustryRules.MaterialCount];
            FacilityRules.Advance(config, stock, 5 * lines - .25f, produced, consumed);
            check(produced.All(p => p == 0), $"{lines} selected products wait for their equal share of five work seconds");
            FacilityRules.Advance(config, stock, .25f, produced, consumed);
            check(Enumerable.Range(0, lines).All(p => produced[(int)FacilityRules.Output(p)] == 1) && FacilityRules.Valid(config),
                $"{lines} selected products complete one atomic recipe each at {100f / lines:0.#} percent speed");
        }
        config = new FacilityConfiguration(); var inputs = Inputs(); var production = new float[IndustryRules.MaterialCount]; var consumption = new float[IndustryRules.MaterialCount];
        FacilityRules.Advance(config, inputs, 5, production, consumption);
        check(inputs[0] == 99 && inputs[2] == 99 && inputs[3] == 99.5f && inputs[6] == 1
            && production[6] == 1 && consumption[0] == 1 && consumption[3] == .5f,
            "component production preserves the starter-chain recipe and records gross material flows");
        config.bufferLimit = 1;
        var before = (float[])inputs.Clone();
        FacilityRules.Advance(config, inputs, 500, production, consumption);
        check(inputs.SequenceEqual(before) && config.productWork[0] == 5,
            "a full internal buffer halts production without spending ingredients or banking unlimited work");
        inputs[6] = 0;
        FacilityRules.Advance(config, inputs, 1, production, consumption);
        check(inputs[6] == 1 && production[6] == 2, "collection automatically resumes a halted product without overflow");
        config = new FacilityConfiguration(); inputs = Inputs(); inputs[3] = 0;
        before = (float[])inputs.Clone();
        FacilityRules.Advance(config, inputs, 10, production, consumption);
        check(inputs.SequenceEqual(before), "missing ingredients never partially consume a manufactorum recipe");
        inputs = Inputs(); inputs[3] = .49999f; before = (float[])inputs.Clone();
        FacilityRules.Advance(config, inputs, 5, production, consumption);
        check(inputs.SequenceEqual(before) && inputs.All(q => q >= 0), "near-complete fractional ingredients cannot create negative inventory");
        FacilityRules.SelectProduct(config, 1, 1); inputs = Inputs(); inputs[6] = 120;
        FacilityRules.Advance(config, inputs, 10, new float[IndustryRules.MaterialCount], new float[IndustryRules.MaterialCount]);
        check(inputs[5] == 1 && inputs[6] == 120, "a blocked product keeps its reserved share while other lines run");
        check(!FacilityRules.SelectProduct(config, 2, 1) && !FacilityRules.SelectProduct(config, 4, 0)
            && !FacilityRules.SelectProduct(config, 1, FacilityRules.ProductCount), "duplicate products and invalid recipe or slot IDs are rejected");
        check(FacilityRules.SelectProduct(config, 0, -1) && config.productWork[0] == 0 && inputs[6] == 120,
            "disabling a line clears its work and retains all existing stock");
        FacilityRules.SelectProduct(config, 1, -1); before = (float[])inputs.Clone();
        FacilityRules.Advance(config, inputs, 100, production, consumption);
        check(inputs.SequenceEqual(before) && !FacilityRules.Accepts(config, MaterialKind.IronOre), "all disabled lines stop ingredient requests and production");
        config = new FacilityConfiguration(); inputs = Inputs(); config.bufferLimit = 1;
        var reserved = new float[IndustryRules.MaterialCount]; reserved[6] = 1;
        FacilityRules.Advance(config, inputs, 5, production, consumption, reserved);
        check(inputs[6] == 0 && inputs[0] == 100, "incoming cargo reserves buffer space before local production");
        inputs[6] = 600; before = (float[])inputs.Clone();
        FacilityRules.Advance(config, inputs, 500, production, consumption);
        check(inputs.SequenceEqual(before), "legacy over-limit local stock drains without deletion or new production");

        config = new FacilityConfiguration(); var depot = new float[IndustryRules.MaterialCount]; depot[0] = 1190;
        check(FacilityRules.DepotRoom(config, depot, MaterialKind.Stone, 0, 0) == 10,
            "general depots share one 1200-unit capacity across all material kinds");
        check(FacilityRules.DepotRoom(config, depot, MaterialKind.Biomass, 0, 10) == 0,
            "first reserved deliveries prevent simultaneous kinds overbooking general storage");
        config.designated = true;
        check(FacilityRules.DepotRoom(config, new float[IndustryRules.MaterialCount], MaterialKind.Stone, 0, 0) == 0,
            "zero designated allocation excludes that material");
        check(FacilityRules.SetStorageLimit(config, MaterialKind.Stone, 300)
            && FacilityRules.SetStorageLimit(config, MaterialKind.Components, 900)
            && !FacilityRules.SetStorageLimit(config, MaterialKind.Crystal, 1), "designated allocations cannot exceed physical depot capacity");
        depot = new float[IndustryRules.MaterialCount]; depot[2] = 295;
        check(FacilityRules.DepotRoom(config, depot, MaterialKind.Stone, 3, 3) == 2
            && FacilityRules.DepotRoom(config, depot, MaterialKind.Components, 0, 3) == 900,
            "designated item caps and total incoming reservations apply together");
        FacilityRules.SetStorageLimit(config, MaterialKind.Stone, 100);
        check(depot[2] == 295 && FacilityRules.DepotRoom(config, depot, MaterialKind.Stone, 0, 0) == 0,
            "lowering a reservation preserves stored surplus and blocks new deliveries");
        config.designated = false;
        check(config.storageLimits[2] == 100 && FacilityRules.DepotRoom(config, depot, MaterialKind.Stone, 0, 0) == 905,
            "switching to general storage retains designated settings and releases shared capacity");
        depot[0] = 1800;
        check(FacilityRules.DepotRoom(config, depot, MaterialKind.Components, 0, 0) == 0,
            "legacy oversized depots retain stock and reject further receipts until drained");
        config.productWork[0] = 2.5f;
        var options = new JsonSerializerOptions { IncludeFields = true };
        var facility = new FacilityStock { quantities = Inputs(), configuration = config };
        var restored = JsonSerializer.Deserialize<FacilityStock>(JsonSerializer.Serialize(facility, options), options);
        check(FacilityRules.Valid(restored.configuration) && restored.configuration.productWork[0] == 2.5f
            && restored.configuration.storageLimits[2] == 100 && restored.quantities.SequenceEqual(facility.quantities),
            "save round trips preserve exact product work, limits, mode and inventory");
        var clone = FacilityRules.Clone(config); clone.products[0] = -1; clone.storageLimits[2] = 0;
        check(config.products[0] == 0 && config.storageLimits[2] == 100, "captured facility settings never alias mutable live arrays");
        check(JsonSerializer.Deserialize<FacilityStock>("{\"quantities\":[0,0,0,0,0,0,0,0,0],\"work\":3}", options).configuration == null
            && FacilityRules.Valid(null), "older saves without configuration retain their migration path");
        var invalid = FacilityRules.Clone(config); invalid.productWork[0] = float.NaN;
        check(!FacilityRules.Valid(invalid), "corrupt nonfinite saved recipe work is rejected");
        invalid = FacilityRules.Clone(config); invalid.products[1] = 0;
        check(!FacilityRules.Valid(invalid), "duplicate saved recipes are rejected");
        invalid = FacilityRules.Clone(config); invalid.storageLimits[0] = 1200;
        check(!FacilityRules.Valid(invalid), "overallocated saved depot reservations are rejected");
        invalid = FacilityRules.Clone(config); invalid.bufferLimit = 0;
        check(!FacilityRules.Valid(invalid), "invalid saved output buffer limits are rejected");
        invalid = FacilityRules.Clone(config); invalid.products = null;
        check(!FacilityRules.Valid(invalid), "malformed saved product arrays are rejected before load");
        var snapshot = new IndustrySnapshot { facilityVersion = 1, facilities = new[] { facility }, deposits = Array.Empty<DepositStock>(), cargo = Array.Empty<CargoStock>() };
        check(IndustryRules.Valid(snapshot, 1), "versioned facility configuration passes the complete inventory save validator");
        snapshot.facilities[0].configuration = invalid;
        check(!IndustryRules.Valid(snapshot, 1), "corrupt facility configuration rejects a complete saved industry snapshot");
        snapshot.facilities[0].configuration = null;
        check(!IndustryRules.Valid(snapshot, 1), "new saved configurations cannot silently disappear");
        snapshot.facilityVersion = 0;
        check(IndustryRules.Valid(snapshot, 1), "older industry snapshots remain valid without the additive configuration fields");
        snapshot.facilityVersion = 2;
        check(!IndustryRules.Valid(snapshot, 1), "unknown facility versions reject before replacing the active colony");
    }
}
