using System;
using System.Collections.Generic;

namespace WH30K.Sim
{
    [Serializable] public struct BuildingEconomy
    {
        public float capacity;
        public float jobs;
        public float outputPerWorker;
        public float upkeep;
    }

    public struct ColonyTick
    {
        public float population;
        public float workforce;
        public float utilisation;
        public float production;
        public float upkeep;
        public float capacity;
        public float jobs;
        public float fullProduction;
        public float effectiveHabitability;
        public float productionModifier;
        public float upkeepModifier;
        public float populationTarget;
        public float populationChangePerSecond;
    }

    // Engine-independent rules shared by the live simulation and executable checks.
    public static class ColonyRules
    {
        public const float PopulationHealth = 1f; // Fixed until a health system is introduced.
        public const float LegacyProjectSupplyScale = .01f;
        public static float MonthsOfSupply(float stock, float monthlyDemand)
            => monthlyDemand > .00001f ? Math.Max(0, stock) / monthlyDemand : float.PositiveInfinity;
        public static string SupplyLabel(float stock, float monthlyDemand)
            => monthlyDemand > .00001f ? $"{MonthsOfSupply(stock, monthlyDemand):0.0} mo" : "NO DEMAND";

        public static ColonyTick CalculateSupportedTick(IReadOnlyList<BuildingEconomy> buildings, float population,
            float workforceRatio, float productionModifier, float upkeepModifier, float habitability,
            float food, float water, float health, float seconds)
        {
            var tick = CalculateTick(buildings, population, 0, workforceRatio, productionModifier, upkeepModifier,
                habitability * Math.Max(0, Math.Min(1, health)), 0);
            var foodMonths = MonthsOfSupply(food, FarmRules.MonthlyDemand(population));
            var waterMonths = MonthsOfSupply(water, WaterRules.Demand(population) * (float)ColonyCalendar.SecondsPerMonth);
            var readiness = Math.Max(0, Math.Min(1, Math.Min(foodMonths / 3f, waterMonths)));
            var housingTarget = tick.populationTarget;
            tick.populationTarget = housingTarget <= population ? housingTarget
                : population + (housingTarget - population) * readiness;
            if (food <= .001f || water <= .001f)
                tick.populationTarget = Math.Min(tick.populationTarget, population * .95f);
            const float rate = .0002f;
            tick.population = tick.populationTarget + (population - tick.populationTarget) * (float)Math.Exp(-rate * seconds);
            tick.populationChangePerSecond = (tick.populationTarget - tick.population) * rate;
            tick.workforce = Math.Min(tick.jobs, tick.population * workforceRatio * tick.effectiveHabitability);
            tick.utilisation = tick.jobs > 0 ? tick.workforce / tick.jobs : 0;
            tick.production = tick.fullProduction * tick.utilisation * tick.effectiveHabitability;
            return tick;
        }
        public static float EffectiveHabitability(float environmentalHabitability, float water)
            => environmentalHabitability * (water <= 0.001f ? 0.55f : 1f);

        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static bool CanAfford(float stockpile, float cost)
        {
            return IsFinite(stockpile) && IsFinite(cost) && cost >= 0f && stockpile >= cost;
        }

        public static bool TrySpend(ref float stockpile, float cost)
        {
            if (!CanAfford(stockpile, cost)) return false;
            stockpile -= cost;
            return true;
        }

        public static float Habitability(float co2, float oxygen, float pollution, float temperature)
        {
            var stress = Math.Max(0f, co2 - 600f) / 1200f
                         + Math.Max(0f, 19.5f - oxygen) / 5f
                         + Math.Max(0f, pollution - 10f) / 100f
                         + Math.Max(0f, Math.Abs(temperature) - 2f) / 8f;
            return Math.Max(0.25f, Math.Min(1f, 1f - stress));
        }

        public static ColonyTick CalculateTick(IReadOnlyList<BuildingEconomy> buildings, float population,
            float growthRate, float workforceRatio, float productionModifier, float upkeepModifier,
            float habitability, float seconds)
        {
            var capacity = 0f;
            var jobs = 0f;
            var output = 0f;
            var upkeep = 0f;
            foreach (var building in buildings)
            {
                capacity += building.capacity;
                jobs += building.jobs;
                output += building.jobs * building.outputPerWorker;
                upkeep += building.upkeep;
            }

            // Exponential growth makes the result independent of frame rate/tick subdivision.
            var target = capacity * habitability;
            population = target + (population - target) * (float)Math.Exp(-growthRate * seconds);
            var workforce = Math.Min(jobs, population * workforceRatio * habitability);
            var utilisation = jobs > 0f ? workforce / jobs : 0f;
            return new ColonyTick
            {
                population = population,
                workforce = workforce,
                utilisation = utilisation,
                production = output * utilisation * productionModifier * habitability,
                upkeep = upkeep * upkeepModifier,
                capacity = capacity,
                jobs = jobs,
                fullProduction = output * productionModifier,
                effectiveHabitability = habitability,
                productionModifier = productionModifier,
                upkeepModifier = upkeepModifier,
                populationTarget = target,
                populationChangePerSecond = (target - population) * growthRate
            };
        }
    }
}
