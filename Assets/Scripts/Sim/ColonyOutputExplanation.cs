using System.Text;
using WH30K.Sim.Environment;

namespace WH30K.Sim
{
    // Read-only explanation of the same inputs/result used by the last settlement report.
    // These diagnostics are rebuilt after loading; they are not part of the save schema.
    public static class ColonyOutputExplanation
    {
        public static string Describe(ColonyTick tick, EnvironmentSnapshot environment, float water, float food = -1)
        {
            var text = new StringBuilder(1800);
            text.AppendLine("<b>COLONY SUPPLIES & GROWTH</b>");
            text.AppendLine("Food/water months = stored reserve / current monthly demand.");
            text.AppendLine("This is reserve coverage, excluding future production or cargo.");
            text.AppendLine("Build supplies = components; PLAS = advanced construction.");
            text.AppendLine("Supply months use recent gross consumption, including building");
            text.AppendLine("and upgrade spending. NO DEMAND means no recent use.");
            text.AppendLine();
            text.AppendLine("<b>POPULATION GROWTH</b>");
            text.AppendLine($"Population {tick.population:0.#}; active housing {tick.capacity:0.#}");
            text.AppendLine($"Supply-supported target {tick.populationTarget:0.#}; trend {tick.populationChangePerSecond * (float)ColonyCalendar.SecondsPerMonth:+0.0;-0.0;0} /month");
            text.AppendLine($"Health {ColonyRules.PopulationHealth:P0} (fixed); habitability {tick.effectiveHabitability:P0}");
            text.AppendLine("Full growth needs 3 months food, 1 month water, and housing.");
            text.AppendLine("Low reserves slow growth; empty food or water causes decline.");
            if (tick.population > tick.populationTarget + .01f)
                text.AppendLine("- Population is falling: restore supplies / housing / conditions.");
            else if (tick.population < tick.populationTarget - .01f)
                text.AppendLine("+ Housing headroom supports growth when supplies permit.");
            else text.AppendLine("- At the population target: check reserves and housing.");
            text.AppendLine(water <= .001f ? "- Water reserve empty: build/resume a powered pump."
                : $"Water {ColonyRules.SupplyLabel(water, WaterRules.Demand(tick.population) * (float)ColonyCalendar.SecondsPerMonth)}; demand {WaterRules.Demand(tick.population) * (float)ColonyCalendar.SecondsPerMonth:0.#}/month.");
            if (food >= 0) text.AppendLine(food <= .001f ? "- Food reserve empty: supply farms with components and fertilizer."
                : $"Food {ColonyRules.SupplyLabel(food, FarmRules.MonthlyDemand(tick.population))}; demand {FarmRules.MonthlyDemand(tick.population):0}/month.");
            text.AppendLine($"Staffed jobs {tick.workforce:0.#}/{tick.jobs:0.#}. Check connected power.");
            text.AppendLine();
            var stressed = false;
            if (environment.Co2 > 600)
            { text.AppendLine($"- CO2 {environment.Co2:0.#} > 600 ppm. Add staffed scrubbers / use cleaner focus."); stressed = true; }
            if (environment.O2 < 19.5f)
            { text.AppendLine($"- Oxygen {environment.O2:0.#}% < 19.5%. Add staffed scrubbers / reduce polluting industry."); stressed = true; }
            if (environment.WaterPollution > 10)
            { text.AppendLine($"- Water pollution {environment.WaterPollution:0.#} > 10. Add scrubbers / use purification focus."); stressed = true; }
            if (environment.GlobalTemperatureOffset > 2)
            { text.AppendLine($"- Temperature {environment.GlobalTemperatureOffset:+0.0;-0.0;0.0} C > +2 C. Cleaner industry / scrubbers limit warming."); stressed = true; }
            if (environment.GlobalTemperatureOffset < -2)
            { text.AppendLine($"- Temperature {environment.GlobalTemperatureOffset:+0.0;-0.0;0.0} C < -2 C. Reduce excess cooling; retain enough scrubbers for air/water."); stressed = true; }
            if (!stressed) text.AppendLine("+ CO2, oxygen, water pollution and temperature are within healthy limits.");
            return text.ToString().TrimEnd();
        }
    }
}
