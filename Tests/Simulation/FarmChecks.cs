using System;
using System.Linq;
using System.Text.Json;
using WH30K.Sim;

internal static class FarmChecks
{
    public static void Run(Action<bool, string> check)
    {
        bool Near(float a, float b) => Math.Abs(a - b) < .001f;
        float[] Stock() { var s = new float[IndustryRules.MaterialCount]; s[6] = 100; s[9] = s[10] = s[11] = 100; return s; }
        check(FarmRules.MonthlyDemand(240) == 240 && FarmRules.MonthlyDemand(0) == 0,
            "monthly food requirement scales with actual colony population");
        check(FarmRules.GrowthSeconds == 900 && FarmRules.Fields(0) == 5 && FarmRules.Buildings(0) == 1,
            "a standard farm starts with five fields and uses six real calendar months for growth");
        for (var grade = 0; grade < 3; grade++)
        {
            var s = Stock(); var before = (float[])s.Clone();
            var food = FarmRules.Produce(s, new FarmState(), grade, 150, 1, 1, 20000, out var selected, out var operation);
            check(Near(food, FarmRules.BaseFoodPerMonth * (1 + grade * .25f)) && selected == grade && operation == 1,
                $"fertilizer grade {grade} delivers {100 + grade * 25}% standard monthly food");
            check(Near(before[6] - s[6], 1) && Near(before[9 + grade] - s[9 + grade], 6)
                && Enumerable.Range(9, 3).Where(k => k != 9 + grade).All(k => s[k] == before[k]),
                $"grade {grade} consumes only its own formulation and physical component upkeep");
        }
        var farm = new FarmState { addedFields = 2 }; var stock = Stock();
        var result = FarmRules.Produce(stock, farm, 0, 150, 1, 1, 20000, out _, out _);
        check(Near(result, FarmRules.BaseFoodPerMonth * 1.4f) && Near(100 - stock[6], 1.4f) && Near(100 - stock[9], 8.4f),
            "each new field adds 20 percent of base food, components and fertilizer rather than compounding");
        var economy = new BuildingEconomy { jobs = 16, upkeep = 5 };
        var scaled = BuildingOperationRules.Economy("Farm", economy, new BuildingOperationState { farm = farm, focus = 2 });
        check(Near(scaled.jobs, 22.4f) && Near(scaled.upkeep, 7),
            "farm employment and credit upkeep scale by the same field increment without generic focus or tier bonuses");
        stock = Stock(); stock[11] = stock[10] = 0;
        result = FarmRules.Produce(stock, new FarmState(), 2, 150, 1, 1, 20000, out var fertilizer, out _);
        check(result == FarmRules.BaseFoodPerMonth && fertilizer == 0, "advanced preference falls back to available basic waste");
        stock = Stock(); stock[11] = 1;
        FarmRules.Produce(stock, new FarmState(), 2, 150, 1, 1, 20000, out fertilizer, out var operationFraction);
        check(fertilizer == 1 && operationFraction == 1, "a complete intermediate supply wins over an insufficient advanced supply");
        stock = Stock(); stock[9] = 3; stock[10] = stock[11] = 0;
        result = FarmRules.Produce(stock, new FarmState(), 0, 150, 1, 1, 20000, out _, out operationFraction);
        check(result == FarmRules.BaseFoodPerMonth * .5f && operationFraction == .5f && stock[9] == 0 && stock[6] == 99.5f,
            "partial fertilizer produces proportionally and conserves fractional upkeep");
        foreach (var missing in new[] { 6, 9 })
        {
            stock = Stock(); stock[missing] = 0; var before = (float[])stock.Clone();
            check(FarmRules.Produce(stock, new FarmState(), 0, 150, 1, 1, 20000, out _, out _) == 0 && stock.SequenceEqual(before),
                "missing farm input halts food without consuming the other ingredient " + missing);
        }
        foreach (var constraint in new[] { "pause", "staff", "power", "storage" })
        {
            stock = Stock(); var before = (float[])stock.Clone();
            check(FarmRules.Produce(stock, new FarmState(), 0, constraint == "pause" ? 0 : 150,
                constraint == "staff" ? 0 : 1, constraint == "power" ? 0 : 1, constraint == "storage" ? 0 : 20000, out _, out _) == 0
                && stock.SequenceEqual(before), "farm halts cleanly for " + constraint);
        }
        stock = Stock(); result = FarmRules.Produce(stock, new FarmState(), 0, 150, 1, 1, 60, out _, out operationFraction);
        check(result == 60 && Near(operationFraction,60 / FarmRules.BaseFoodPerMonth) && Near(100 - stock[9], 6 * 60 / FarmRules.BaseFoodPerMonth), "food storage room bounds output and spending together");
        farm = new FarmState();
        check(!FarmRules.AdvanceGrowth(farm, 899, true, true) && farm.addedFields == 0
            && FarmRules.AdvanceGrowth(farm, 1, true, true) && farm.addedFields == 1 && farm.productiveSeconds == 0,
            "exact six-month threshold adds precisely one field and resets qualification");
        FarmRules.AdvanceGrowth(farm, 450, true, true); FarmRules.AdvanceGrowth(farm, 1, false, true);
        check(farm.productiveSeconds == 0 && farm.addedFields == 1, "under-supply interrupts the consecutive full-operation streak");
        FarmRules.AdvanceGrowth(farm, 450, true, true); FarmRules.AdvanceGrowth(farm, 0, false, true);
        check(farm.productiveSeconds == 450, "pause preserves growth qualification rather than resetting it");
        check(!FarmRules.AdvanceGrowth(farm, 450, true, false) && farm.productiveSeconds == 900
            && FarmRules.AdvanceGrowth(farm, 1, true, true), "qualified farm waits for safe land without losing earned time");
        check(FarmRules.Buildings(23) == 1 && FarmRules.Buildings(24) == 2 && FarmRules.Buildings(63) == 2
            && FarmRules.Buildings(64) == 3 && FarmRules.Buildings(103) == 3 && FarmRules.Buildings(104) == 4,
            "satellite buildings appear at four times the previous added-field thresholds: 24, 64 and 104");
        var coordinates = Enumerable.Range(0, FarmRules.Fields(FarmRules.MaxAddedFields)).Select(i => { FarmRules.FieldPosition(i, out var x, out var z); return (x, z); }).ToArray();
        check(coordinates.Distinct().Count() == coordinates.Length, "every field has a distinct saved-index-derived position around a barn");
        check(FarmRules.WastePerMonth("HabBlock", 100, 40) > 0 && FarmRules.WastePerMonth("Outpost", 0, 60) > 0
            && FarmRules.WastePerMonth("Manufactorum", 0, 20) > 0 && FarmRules.WastePerMonth("Farm", 0, 16) == 0,
            "residences, science outposts and industry supply waste without farms making their own fertilizer");
        var config = new FacilityConfiguration(); FacilityRules.SelectProduct(config, 0, 4); stock = Stock(); stock[3] = 10; stock[10] = 0;
        FacilityRules.Advance(config, stock, 5, new float[12], new float[12]);
        check(stock[9] == 98 && stock[3] == 9 && stock[10] == 1, "existing manufactorum converts biomass and reclaimed waste to intermediate fertilizer");
        FacilityRules.SelectProduct(config, 0, 5); stock[2] = 1; stock[4] = 1; stock[7] = 1; stock[11] = 0;
        FacilityRules.Advance(config, stock, 5, new float[12], new float[12]);
        check(stock[10] == 0 && stock[11] == 1 && stock[2] == .5f && stock[4] == .75f && stock[7] == .75f,
            "advanced mineral nutrient uses intermediate product and existing stone, crystal and promethium streams");
        var previous = Enumerable.Range(0, 9).Select(i => (float)i).ToArray(); var upgraded = IndustryRules.UpgradeStock(previous);
        check(IndustryRules.ValidStock(previous) && upgraded.Take(9).SequenceEqual(previous) && upgraded.Skip(9).All(x => x == 0),
            "nine-slot saves preserve all original stock and append empty fertilizer inventories");
        config.storageLimits = new float[9]; config.storageLimits[8] = 80;
        var clone = FacilityRules.Clone(config);
        check(FacilityRules.Valid(config) && clone.storageLimits.Length == 12 && clone.storageLimits[8] == 80,
            "legacy designated storage allocations migrate without losing plasteel reservations");
        var json = JsonSerializer.Serialize(new BuildingOperationState { farm = farm }, new JsonSerializerOptions { IncludeFields = true });
        var loaded = JsonSerializer.Deserialize<BuildingOperationState>(json, new JsonSerializerOptions { IncludeFields = true });
        check(BuildingOperationRules.Valid(loaded) && loaded.farm.addedFields == farm.addedFields && loaded.farm.productiveSeconds == farm.productiveSeconds,
            "farm field count and partial six-month streak round trip exactly");
        loaded.farm.productiveSeconds = float.NaN;
        check(!BuildingOperationRules.Valid(loaded), "invalid farm qualification is rejected before save restoration");
        check(FarmRules.Valid(null) && FarmRules.Clone(null).addedFields == 0, "pre-farm operations migrate with no fabricated growth");
        var legacy = new IndustrySnapshot { materialVersion = 1, geologyVersion = 2,
            facilities = new[] { new FacilityStock { quantities = previous } },
            deposits = Array.Empty<DepositStock>(), cargo = Array.Empty<CargoStock>(),
            producedTotals = new float[9], consumedTotals = new float[9], productionRates = new float[9], consumptionRates = new float[9] };
        check(IndustryRules.Valid(legacy, 1), "complete previous-material-version inventories and statistics remain valid");
        legacy.materialVersion = 2;
        check(!IndustryRules.Valid(legacy, 1), "new material-version saves must contain every fertilizer slot");
        check(MiningRules.NormalizeOptional(new ExcavationState { capacity = Array.Empty<float>(), removed = Array.Empty<float>() }) == null
            && MiningRules.NormalizeOptional(new ExcavationState { depth = 1 }) != null,
            "Unity empty excavation sentinels normalize without silently accepting corrupt nonempty geology");
        legacy.materialVersion = 1; legacy.deposits = new[] { new DepositStock { id = 0, kind = 3, remaining = 100,
            excavation = new ExcavationState { capacity = Array.Empty<float>(), removed = Array.Empty<float>() } } };
        check(IndustryRules.Valid(legacy, 1), "Unity-style empty biomass excavation does not reject a real colony save");
    }
}
