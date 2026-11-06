using System;

namespace WH30K.Sim
{
    [Serializable]
    public struct FoundingProgress
    {
        public bool enabled, waterEstablished, foodDelivered, componentsMade, completed;
        public float seconds, water, waterDemand, food, foodDemand, components, componentUse;
        public float rewardRecovered, pendingComponentUse;
    }

    // Observations come from actual simulation production/deliveries, never inventory grants.
    public static class FoundingObjectives
    {
        public const float Duration = (float)ColonyCalendar.SecondsPerMonth;
        public const float Reward = 20;
        public static int Stage(FoundingProgress p) => !p.waterEstablished ? 0 : !p.foodDelivered ? 1 : !p.componentsMade ? 2 : !p.completed ? 3 : 4;
        public static string Title(FoundingProgress p)
        {
            switch (Stage(p))
            {
                case 0: return "Establish working water supply";
                case 1: return "Deliver locally grown food";
                case 2: return "Manufacture construction components";
                case 3: return "Sustain a balanced colony for one month";
                default: return p.rewardRecovered < Reward ? "Orbital canister located / recover supplies" : "Colony established / canister recovered";
            }
        }
        public static string Detail(FoundingProgress p)
        {
            switch (Stage(p))
            {
                case 0: return "Build/resume a groundwater or coastal desalination pump. Supply workers and network power; placing a pump alone does not qualify.";
                case 1: return "Connect a farm to the ship, supply components and reclaimed-waste fertilizer, and wait for its food cargo to arrive. Check staffing and power.";
                case 2: return "Connect iron mine + stone quarry + biomass harvester to a Manufactorum on its components recipe. Add fuel extraction/generation when the 18 MW reactor is insufficient. Smelter + Works also qualifies.";
                case 3: return $"Observe a full month ({p.seconds:0}/{Duration:0} simulated seconds). Delivered food and pumped water must cover population demand; component production must cover ALL component spending, including construction/upgrades. Keep food and water above zero. Failed months restart. Expand farms, restore inputs or add power to address deficits.\nCurrent balance: water {p.water-p.waterDemand:+0.0;-0.0;0}, delivered food {p.food-p.foodDemand:+0.0;-0.0;0}, components {p.components-p.componentUse:+0.0;-0.0;0}.";
                default: return $"Your colony has demonstrated a balanced supply month. Auspex crews have located a drifting orbital cargo canister containing {Reward:0} construction components.\n\nRecover its contents into real colony storage. Remaining: {Math.Max(0, Reward-p.rewardRecovered):0.##}. Full storage leaves the remainder in the canister. This is a one-time reward; continued sustainability still needs monitoring.";
            }
        }
        public static void Observe(ref FoundingProgress p, float dt, float water, float waterDemand,
            float foodDelivered, float foodDemand, float components, float componentUse, bool reservesHealthy)
        {
            if (dt <= 0 || !ColonyRules.IsFinite(dt)) return;
            var previousStage = Stage(p);
            p.waterEstablished |= water > 0;
            p.foodDelivered |= foodDelivered > 0;
            p.componentsMade |= components > 0;
            if (p.completed || previousStage < 3) return;
            if (!reservesHealthy) { ResetWindow(ref p); return; }
            p.seconds += dt; p.water += water; p.waterDemand += waterDemand;
            p.food += foodDelivered; p.foodDemand += foodDemand;
            p.components += components; p.componentUse += componentUse;
            if (p.seconds + .0001f < Duration) return;
            p.completed = p.water + .0001f >= p.waterDemand && p.food + .0001f >= p.foodDemand
                && p.components > 0 && p.components + .0001f >= p.componentUse;
            if (!p.completed) ResetWindow(ref p);
        }
        private static void ResetWindow(ref FoundingProgress p)
        { p.seconds=p.water=p.waterDemand=p.food=p.foodDemand=p.components=p.componentUse=0; }
        public static bool Valid(FoundingProgress p)
        {
            foreach (var n in new[] { p.seconds,p.water,p.waterDemand,p.food,p.foodDemand,p.components,p.componentUse,p.rewardRecovered,p.pendingComponentUse })
                if (!ColonyRules.IsFinite(n) || n < 0) return false;
            return p.seconds <= Duration + 1.01f && p.rewardRecovered <= Reward
                && (p.completed || p.rewardRecovered == 0)
                && (!p.completed || p.waterEstablished && p.foodDelivered && p.componentsMade);
        }
    }
}
