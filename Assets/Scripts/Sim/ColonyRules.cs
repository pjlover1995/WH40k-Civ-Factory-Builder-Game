using System;
using System.Collections.Generic;

namespace WH30K.Sim
{
    public struct BuildingEconomy
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
    }

    // Engine-independent rules shared by the live simulation and executable checks.
    public static class ColonyRules
    {
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
                capacity = capacity
            };
        }
    }
}
