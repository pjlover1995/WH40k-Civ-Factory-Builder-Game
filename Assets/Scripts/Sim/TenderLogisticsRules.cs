using System;

namespace WH30K.Sim
{
    [Serializable]
    public sealed class TenderLogisticsState
    {
        public int version = 1;
        public float orbitalCrew, orbitalFood;
        public float[] orbitalSupplies;
        public int tripsUsed;
        public bool initialLoadoutApplied;
    }

    public static class TenderLogisticsRules
    {
        public const int MaximumTrips = 4;
        public const float MainVesselCrew = 6000f;
        public const float MainVesselRations = 120000f;
        public static TenderLogisticsState NormalizeOptional(TenderLogisticsState state)
            => state != null && (state.orbitalSupplies == null || state.orbitalSupplies.Length == 0)
                && state.orbitalCrew == 0 && state.orbitalFood == 0
                && state.tripsUsed == 0 && !state.initialLoadoutApplied ? null : state;
        public static bool Valid(TenderLogisticsState state)
        {
            if (state == null) return true;
            if (state.version != 1 || state.tripsUsed < 0 || state.tripsUsed > MaximumTrips
                || !ColonyRules.IsFinite(state.orbitalCrew) || state.orbitalCrew < 0 || state.orbitalCrew > MainVesselCrew
                || !ColonyRules.IsFinite(state.orbitalFood) || state.orbitalFood < 0 || state.orbitalFood > MainVesselRations
                || state.orbitalSupplies == null || state.orbitalSupplies.Length != IndustryRules.MaterialCount
                || !IndustryRules.ValidStock(state.orbitalSupplies)) return false;
            return true;
        }
        public static int TripsRemaining(TenderLogisticsState state)
            => state == null ? 0 : Math.Max(0, MaximumTrips - state.tripsUsed);
    }
}
