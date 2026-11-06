namespace WH30K.Sim
{
    /// <summary>Shared balance rules for the colony's finite fresh-water reserve.</summary>
    public static class WaterRules
    {
        public const float StartingReserve = 600f;
        public const float Capacity = 5000f;
        public const float GroundwaterPerSecond = 1.2f;
        public const float DesalinationPerSecond = .9f;
        public const float BaseDemandPerSecond = .05f;
        public const float DemandPerColonistPerSecond = .0001f;

        public static float Demand(float population)
            => !ColonyRules.IsFinite(population) || population < 0 ? 0 : BaseDemandPerSecond + population * DemandPerColonistPerSecond;

        public static float PumpOutput(string buildingId, float staffing, float power, float level, float focus)
        {
            if (buildingId != "GroundwaterPump" && buildingId != "DesalinationPump") return 0;
            if (!ColonyRules.IsFinite(staffing) || !ColonyRules.IsFinite(power) || !ColonyRules.IsFinite(level) || !ColonyRules.IsFinite(focus)) return 0;
            var rate = buildingId == "GroundwaterPump" ? GroundwaterPerSecond : DesalinationPerSecond;
            return rate * Clamp01(staffing) * Clamp01(power) * (1 + System.Math.Max(0, level) * .25f) * System.Math.Max(0, focus);
        }

        private static float Clamp01(float value) => System.Math.Max(0, System.Math.Min(1, value));
    }
}
