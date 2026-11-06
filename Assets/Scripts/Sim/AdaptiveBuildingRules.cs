using System;

namespace WH30K.Sim
{
    /// <summary>Deterministic upgrade work and payment. All durations use the shared simulation clock.</summary>
    public static class AdaptiveBuildingRules
    {
        public static float GalleryLocalCenter(float blockStart,float blockEnd,float annexRadius)
            => (blockStart+blockEnd)*.5f-annexRadius;
        public static string PlanName(int plan)
        {
            switch (plan)
            {
                case 1: return "Vertical crown";
                case 2: return "Radial campus";
                default: return plan >= 3 ? "Radial campus" : "Balanced district";
            }
        }
        public static float Duration(BuildingOperationState state) => 60f * (state.level + 1);
        public static float Cost(BuildingOperationState state)
            => 150f * (state.level + 1) * (state.growthPlan == 2 ? 1.4f : state.growthPlan == 0 ? 1.2f : 1f);
        public static float MilestoneDuration(string id, BuildingOperationState state)
            => (id == "HabBlock" ? 120f : 180f) * (state.level + 1);
        public static bool Valid(BuildingOperationState state)
            => state.growthPlan >= 0 && state.growthPlan <= 6
                && state.firstExpansion >= 0 && state.firstExpansion <= 6
                && state.secondExpansion >= 0 && state.secondExpansion <= 6
                && ColonyRules.IsFinite(state.improvementWork) && state.improvementWork >= 0
                && state.improvementWork < Duration(state)
                && ColonyRules.IsFinite(state.milestoneSeconds) && state.milestoneSeconds >= 0 && state.milestoneSeconds <= 540
                && ColonyRules.IsFinite(state.completionSeconds) && state.completionSeconds >= 0 && state.completionSeconds <= 6
                && (!state.improving || state.level < 2)
                && (state.improving || state.improvementWork == 0);

        public static bool Request(ref BuildingOperationState state)
        {
            if (state.level >= 2 || state.improving || state.offline) return false;
            state.improving = true;
            state.improvementWork = 0;
            return true;
        }

        // Returns the exact debit for this step; work cannot exceed the funds actually available.
        public static float Advance(ref BuildingOperationState state, string id, float seconds,
            float utilisation, float availableCredits, bool spaceAvailable, out bool completed)
        {
            completed = false;
            if (!ColonyRules.IsFinite(seconds) || seconds <= 0 || state.offline) return 0;
            state.completionSeconds = Math.Max(0, state.completionSeconds - seconds);
            if (state.level >= 2) return 0;
            if (!state.improving)
            {
                state.milestoneSeconds = utilisation >= .5f
                    ? Math.Min(MilestoneDuration(id, state), state.milestoneSeconds + seconds) : 0;
                if (state.manualImprovements || state.milestoneSeconds < MilestoneDuration(id, state)) return 0;
                Request(ref state);
                // Start on the next step, so threshold-crossing time is never counted twice.
                return 0;
            }
            if (!spaceAvailable || !ColonyRules.IsFinite(availableCredits) || availableCredits <= 0) return 0;
            var rate = Cost(state) / Duration(state);
            var work = Math.Min(Math.Min(seconds, Duration(state) - state.improvementWork), availableCredits / rate);
            var debit = Math.Min(availableCredits, work * rate);
            state.improvementWork += work;
            if (state.improvementWork >= Duration(state) - .0001f)
            {
                if (state.level == 0) state.firstExpansion = state.growthPlan;
                else state.secondExpansion = state.growthPlan;
                state.level++;
                state.improving = false;
                state.improvementWork = 0;
                state.milestoneSeconds = 0;
                state.completionSeconds = 6;
                completed = true;
            }
            return debit;
        }
    }
}
