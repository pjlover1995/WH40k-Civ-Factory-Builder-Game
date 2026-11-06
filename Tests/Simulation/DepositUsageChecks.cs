using System;
using System.Text.Json;
using WH30K.Sim;

internal static class DepositUsageChecks
{
    public static void Run(Action<bool, string> check)
    {
        var usage = new DepositUsage();
        check(usage.MonthlyRate(0) == 0 && float.IsPositiveInfinity(DepositUsage.LifeMonths(120, 0)), "an unmeasured or idle source has no finite depletion forecast");
        usage.Record(5, 2); usage.Record(5, 3);
        check(usage.MonthlyRate(10) == 75 && DepositUsage.LifeMonths(150, 75) == 2, "multiple extractors aggregate actual source removal into game-month endurance");
        check(usage.MonthlyRate(10) == 75, "repeated reads during pause leave the usage estimate unchanged");
        usage.Record(20, 10);
        check(usage.MonthlyRate(30) == 75 && usage.MonthlyRate(36) == 50, "the thirty-second window ages out earlier extraction using simulation time");
        check(usage.MonthlyRate(51) == 0 && DepositUsage.LifeMonths(0, 0) == 0, "stopped extraction ages to idle and exhausted deposits retain zero life");
        usage.Record(52, float.NaN); usage.Record(52, -5);
        check(usage.MonthlyRate(52) == 0, "invalid removals cannot corrupt a deposit forecast");
        var options = new JsonSerializerOptions { IncludeFields = true };
        var original = new DepositStock { id = 0, kind = 3, remaining = 84, initialVolume = 150, discovered = true };
        var restored = JsonSerializer.Deserialize<DepositStock>(JsonSerializer.Serialize(original, options), options);
        check(restored.initialVolume == 150 && restored.remaining == 84, "starting volume and exact remaining volume persist independently");
        var old = JsonSerializer.Deserialize<DepositStock>("{\"id\":0,\"kind\":3,\"remaining\":84}", options);
        check(old.remaining == 84 && old.initialVolume == 0, "older saves retain exact remaining stock and mark missing starting volume unknown");
        var snapshot = new IndustrySnapshot { facilities = Array.Empty<FacilityStock>(), deposits = new[] { original }, cargo = Array.Empty<CargoStock>() };
        check(IndustryRules.Valid(snapshot, 0), "additive starting-volume data validates without changing the save schema");
        snapshot.deposits[0].initialVolume = 70;
        check(!IndustryRules.Valid(snapshot, 0), "starting volume below the remaining reserve is rejected");
        snapshot.deposits[0].initialVolume = float.NaN;
        check(!IndustryRules.Valid(snapshot, 0), "non-finite starting volume is rejected before loading");
    }
}
