using System;
using WH30K.Sim.Environment;

namespace WH30K.Sim
{
    [Serializable]
    public struct BuildingOperationState
    {
        public int level;
        public int focus;
        public bool offline;
    }

    public static class BuildingOperationRules
    {
        public static bool Valid(BuildingOperationState state)
            => state.level >= 0 && state.level <= 2 && state.focus >= 0 && state.focus <= 2;
        public static float UpgradeCost(BuildingOperationState state) => 150f * (state.level + 1);
        public static BuildingEconomy Economy(string id, BuildingEconomy basic, BuildingOperationState state)
        {
            if (state.offline) return default;
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
            var scale = 1f + state.level * 0.25f;
            if(IndustryRules.Facility(id) && id!="Mine") scale *= state.focus==1?1.4f:state.focus==2?.75f:1;
            if (id == "Factory") scale *= state.focus == 1 ? 1.5f : state.focus == 2 ? 0.6f : 1f;
            if (id == "Utility") scale *= state.focus == 1 ? 1.6f : state.focus == 2 ? 0.65f : 1f;
            return basic.Scale(scale);
        }
        public static string FocusName(string id, int focus)
        {
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
            if(id=="Mine") return "Extracts the selected mineral within 250 m. One unit per 5 staffed simulation seconds. Haulers deliver it to a consumer.";
            if(IndustryRules.Facility(id)) return focus==0?"One work cycle per 5 staffed simulation seconds. Tier upgrades increase throughput.":focus==1?"+40% throughput. Higher upkeep and emissions.":"-25% throughput. Reduced upkeep and emissions.";
            if (focus == 0) return "Standard capacity, output and operating costs.";
            switch (id)
            {
                case "HabBlock": return focus == 1 ? "+40% housing. +25% upkeep." : "+80% jobs. -15% housing. +10% upkeep.";
                case "Factory": return focus == 1 ? "+35% output per worker. +50% emissions. +25% upkeep." : "-40% emissions. -15% output. +10% upkeep.";
                case "Utility": return focus == 1 ? "+60% purification. +40% upkeep." : "-35% purification and upkeep.";
                default: return focus == 1 ? "+30% output. +25% jobs. +20% upkeep." : "+50% housing. -20% output. +10% upkeep.";
            }
        }
    }
}
