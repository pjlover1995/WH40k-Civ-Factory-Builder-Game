using System;

namespace WH30K.Sim
{
    /// <summary>Explorator landing vessel specifications. Food uses the shared colony reserve.</summary>
    public static class ShipRules
    {
        public const float Length = 3000;
        public const float Beam = 540;
        public const float Height = 720;
        public const float StartingCrew = 6000;
        public const float CrewCapacity = 10000;
        public const float ReactorMW = 18;
        // One year even at full occupancy; growth cannot exhaust this in the first year.
        public const float StartingFood = CrewCapacity * 12 * FarmRules.FoodPerColonistPerMonth;
        public static float Occupied(float population, float otherHousing, float capacity = CrewCapacity)
            => Math.Min(capacity, Math.Max(0, population) * capacity / Math.Max(capacity, capacity + otherHousing));
        public static float EnduranceMonths(float food, float population)
            => FarmRules.MonthlyDemand(population) > 0 ? Math.Max(0, food) / FarmRules.MonthlyDemand(population) : 0;
    }
}
