using System;
using System.Linq;
using System.Text.Json;
using WH30K.Sim;

internal static class ResearchChecks
{
    public static void Run(Action<bool, string> check)
    {
        var state = ResearchRules.Create();
        check(ResearchRules.Valid(state) && state.insight == 10 && ResearchRules.ProjectCount == 6,
            "new colonies receive a small orbital-data reserve and the research catalog is defined");
        check(Math.Abs(ResearchRules.InsightRate(true, 0) - .01f) < .0001f
            && Math.Abs(ResearchRules.InsightRate(false, 1) - .08f) < .0001f
            && Math.Abs(ResearchRules.InsightRate(true, 1) - .09f) < .0001f
            && Math.Abs(ResearchRules.InsightRate(false, 0) - 0) < .0001f,
            "the active landed vessel supplies basic research alongside optional staffed Cognitoria");
        check(!ResearchRules.Select(state, "nutrient-symbiosis")
            && ResearchRules.Select(state, "aquifer-lenses"),
            "research prerequisites prevent selecting a locked second-tier project");
        ResearchRules.Advance(state, 100, 0);
        check(Math.Abs(state.progress[0] - 10) < .001f && Math.Abs(state.insight) < .001f,
            "a research project spends banked insight gradually and preserves partial progress");
        ResearchRules.Select(state, "metallurgic-calibration");
        ResearchRules.Advance(state, 500, .08f);
        check(ResearchRules.IsCompleted(state, "metallurgic-calibration")
            && Math.Abs(state.progress[0] - 10) < .001f
            && ResearchRules.Select(state, "aquifer-lenses"),
            "research completes at the generated rate, retains switched project progress and unlocks its dependent");
        check(ResearchRules.Valid(state)
            && !ResearchRules.Select(state, "not-a-project")
            && !ResearchRules.AddInsight(state, float.NaN)
            && ResearchRules.AddInsight(state, 10000)
            && Math.Abs(state.insight - ResearchRules.MaximumInsight) < .001f,
            "research rejects invalid actions and caps its finite insight bank");
        var restored = JsonSerializer.Deserialize<ResearchSnapshot>(JsonSerializer.Serialize(state,
            new JsonSerializerOptions { IncludeFields = true }), new JsonSerializerOptions { IncludeFields = true });
        check(ResearchRules.Valid(restored) && restored.completedIds.SequenceEqual(state.completedIds)
            && restored.progress.SequenceEqual(state.progress)
            && Math.Abs(restored.insight - state.insight) < .001f,
            "research project, progress, completions and insight survive save serialization");
        check(ResearchRules.PumpYieldMultiplier(new ResearchSnapshot { completedIds = new[] { "aquifer-lenses" } }, "GroundwaterPump") == 1.2f
            && ResearchRules.FarmFoodMultiplier(new ResearchSnapshot { completedIds = new[] { "nutrient-symbiosis" } }) == 1.15f
            && ResearchRules.ProcessorWorkMultiplier(new ResearchSnapshot { completedIds = new[] { "metallurgic-calibration" } }, "Refinery") == 1.2f
            && ResearchRules.GeneratorOutputMultiplier(new ResearchSnapshot { completedIds = new[] { "reactor-catechism" } }) == 1.15f
            && ResearchRules.SurveyRadiusMultiplier(new ResearchSnapshot { completedIds = new[] { "wide-auspex" } }) == 1.25f,
            "completed research applies deterministic effects to its intended colony systems");
        var baseRewards = new DiscoveryRewardProfile { cautiousMultiplier = .7f, rapidMultiplier = 1.8f, hazardChance = .35f };
        var rewards = ResearchRules.DiscoveryRewards(new ResearchSnapshot { completedIds = new[] { "cautious-protocols" } }, baseRewards);
        check(Math.Abs(rewards.cautiousMultiplier - .805f) < .001f && Math.Abs(rewards.rapidMultiplier - 2.07f) < .001f
            && Math.Abs(rewards.hazardChance - baseRewards.hazardChance) < .001f,
            "recovery research increases both expedition rewards without changing incident risk");
        check(!ResearchRules.Valid(new ResearchSnapshot { insight = float.PositiveInfinity })
            && !ResearchRules.Valid(new ResearchSnapshot { progress = new float[1] }),
            "malformed research saves are rejected");
    }
}
