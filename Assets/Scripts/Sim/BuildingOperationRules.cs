using System;
using WH30K.Sim.Environment;

namespace WH30K.Sim
{
    [Serializable]
    public struct BuildingOperationState
    {
        public string instanceId, instanceName;
        public int level;
        public int focus;
        public bool offline;
        // Additive save fields: old saves default to automatic, balanced growth with no pending work.
        public bool manualImprovements;
        public int growthPlan; // 0 balanced, 1 vertical, 2 radial campus; legacy plans 3-6 render as radial campuses.
        public bool improving;
        public float improvementWork;
        public float milestoneSeconds;
        public int firstExpansion, secondExpansion;
        public float completionSeconds;
        public ResidentialState residential;
        public FarmState farm;
        public ManufactorumState manufactorum;
    }

    public static class BuildingOperationRules
    {
        public static bool Valid(BuildingOperationState state)
            => (string.IsNullOrEmpty(state.instanceId) || Guid.TryParse(state.instanceId,out _))
                && FacilityIdentityRules.ValidName(state.instanceName) && state.level >= 0 && state.level <= 2 && state.focus >= 0 && state.focus <= 2
                && ManufactorumRules.Valid(state.manufactorum) && AdaptiveBuildingRules.Valid(state) && ResidentialRules.ValidSave(state.residential) && FarmRules.Valid(state.farm);
        public static float UpgradeCost(BuildingOperationState state) => AdaptiveBuildingRules.Cost(state);
        public static BuildingEconomy Economy(string id, BuildingEconomy basic, BuildingOperationState state)
        {
            if (state.offline) return default;
            if (id == "ColonyShip") return basic; // Berths cannot exceed the physical ship capacity.
            if (id == "Farm") { basic.jobs *= FarmRules.Scale(state.farm?.addedFields ?? 0); basic.upkeep *= FarmRules.Scale(state.farm?.addedFields ?? 0); return basic; }
            if (id == "Manufactorum" && state.manufactorum != null)
            {
                var scale = ManufactorumRules.Scale(state.manufactorum); basic.capacity *= scale; basic.jobs *= scale; basic.upkeep *= scale;
                if (state.focus == 1) { basic.jobs *= 1.15f; basic.upkeep *= 1.3f; }
                if (state.focus == 2) basic.upkeep *= .75f;
                return basic;
            }
            basic.capacity *= 1f + state.level * 0.25f;
            basic.jobs *= 1f + state.level * 0.25f;
            basic.upkeep *= 1f + state.level * 0.15f;
            if (IndustryRules.Facility(id))
            {
                if(id!="Mine" && state.focus==1) {basic.jobs*=1.15f;basic.upkeep*=1.3f;}
                if(id!="Mine" && state.focus==2) basic.upkeep*=.75f;
                return basic;
            }
            if (state.focus == 1)
            {
                if (id == "HabBlock") { basic.capacity *= 1.4f; basic.upkeep *= 1.25f; }
                else if (id == "Factory") { basic.outputPerWorker *= 1.35f; basic.upkeep *= 1.25f; }
                else if (id == "Utility") basic.upkeep *= 1.4f;
                else { basic.outputPerWorker *= 1.3f; basic.jobs *= 1.25f; basic.upkeep *= 1.2f; }
            }
            else if (state.focus == 2)
            {
                if (id == "HabBlock") { basic.capacity *= 0.85f; basic.jobs *= 1.8f; basic.upkeep *= 1.1f; }
                else if (id == "Factory") { basic.outputPerWorker *= 0.85f; basic.upkeep *= 1.1f; }
                else if (id == "Utility") basic.upkeep *= 0.65f;
                else { basic.capacity *= 1.5f; basic.outputPerWorker *= 0.8f; basic.upkeep *= 1.1f; }
            }
            return basic;
        }
        public static EnvironmentImpact Impact(string id, EnvironmentImpact basic, BuildingOperationState state)
        {
            if (state.offline) return EnvironmentImpact.Zero;
            var scale = id == "Manufactorum" ? ManufactorumRules.ProductionScale(state) : 1f + state.level * 0.25f;
            if(IndustryRules.Facility(id) && id!="Mine") scale *= state.focus==1?1.4f:state.focus==2?.75f:1;
            if (id == "Factory") scale *= state.focus == 1 ? 1.5f : state.focus == 2 ? 0.6f : 1f;
            if (id == "Utility") scale *= state.focus == 1 ? 1.6f : state.focus == 2 ? 0.65f : 1f;
            return basic.Scale(scale);
        }
        public static string FocusName(string id, int focus)
        {
            if(id=="Farm") return focus==0?"Basic fertilizer / 100%":focus==1?"Up to intermediate / 125%":"Up to advanced / 150%";
            if(id=="Mine") return focus==0?"Iron extraction":focus==1?"Copper extraction":"Crystal extraction";
            if(IndustryRules.Facility(id)) return focus==0?"Standard throughput":focus==1?"Intensive production":"Economy cycle";
            if (focus == 0) return "Balanced operation";
            switch (id)
            {
                case "HabBlock": return focus == 1 ? "High-density housing" : "Civil administration";
                case "Factory": return focus == 1 ? "Surge production" : "Clean production";
                case "Utility": return focus == 1 ? "Deep purification" : "Economy cycle";
                default: return focus == 1 ? "Supply logistics" : "Residential support";
            }
        }
        public static string FocusDescription(string id, int focus)
        {
            if(id=="Manufactorum") return "Warehouse expansion adds throughput, power and staffing. Technology tiers unlock recipes.";
            if(id=="Farm") return "Best supplied fertilizer up to this grade.\nEach field: +20% base food.";
            if(id=="Mine") return "Mineral set automatically from the deposit at placement. One unit per 5 staffed simulation seconds; haulers deliver it to a consumer.";
            if(IndustryRules.Facility(id)) return focus==0?"One work cycle per 5 staffed simulation seconds. Tier upgrades increase throughput.":focus==1?"+40% throughput. Higher emissions.":"-25% throughput. Reduced emissions.";
            if (focus == 0) return "Standard capacity and throughput.";
            switch (id)
            {
                case "HabBlock": return focus == 1 ? "+40% housing. " : "+80% jobs. -15% housing. ";
                case "Factory": return focus == 1 ? "+50% emissions; same reclaimed waste per worker. " : "-40% emissions; same reclaimed waste per worker. ";
                case "Utility": return focus == 1 ? "+60% purification. " : "-35% purification.";
                default: return focus == 1 ? "+30% output. +25% jobs. " : "+50% housing. -20% output. ";
            }
        }
    }
}
