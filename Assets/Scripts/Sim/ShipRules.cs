using System;

namespace WH30K.Sim
{
    /// <summary>Landing tender specifications; historical orbital ship values remain for old saves.</summary>
    public static class ShipRules
    {
        public const float Length = 500;
        public const float Beam = 90;
        public const float Height = 120;
        public const float StartingCrew = 1000;
        public const float CrewCapacity = 1500;
        public const float ReactorMW = 3;
        public const float LegacyLength = 3000;
        public const float LegacyBeam = 540;
        public const float LegacyHeight = 720;
        public const float LegacyStartingCrew = 6000;
        public const float LegacyCrewCapacity = 10000;
        public const float LegacyReactorMW = 18;
        // One year at full lander occupancy.
        public const float StartingFood = CrewCapacity * 12 * FarmRules.FoodPerColonistPerMonth;
        public static float Occupied(float population, float otherHousing, float capacity = CrewCapacity)
            => Math.Min(capacity, Math.Max(0, population) * capacity / Math.Max(capacity, capacity + otherHousing));
        public static float EnduranceMonths(float food, float population)
            => FarmRules.MonthlyDemand(population) > 0 ? Math.Max(0, food) / FarmRules.MonthlyDemand(population) : 0;
    }
}
