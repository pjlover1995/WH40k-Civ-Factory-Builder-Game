using UnityEngine;
using WH30K.Gameplay.Construction;
using WH30K.Sim.Environment;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;
using WH30K.UI;

namespace WH30K.Sim
{
    /// <summary>Turns powered, staffed Cognitoria and field discoveries into saved research progress.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class ResearchSystem : MonoBehaviour
    {
        private ResearchSnapshot state = ResearchRules.Create();
        private BuildingPlacementController placement;
        private IndustrySystem industry;
        public float CurrentRate { get; private set; }
        public float Insight => state.insight;
        public string ActiveProjectId => state.activeProjectId;
        public ResearchSnapshot State => ResearchRules.Clone(state);

        public void Configure(BuildingPlacementController controller, IndustrySystem industrySystem, ResearchSnapshot saved = null)
        {
            placement=controller;industry=industrySystem;state=ResearchRules.Valid(saved)?ResearchRules.Clone(saved):ResearchRules.Create();
            CurrentRate=CalculateRate();
        }

        public ResearchSnapshot Capture() => ResearchRules.Clone(state);
        public bool SelectProject(string id) => ResearchRules.Select(state,id);
        public bool IsCompleted(string id) => ResearchRules.IsCompleted(state,id);
        public bool AddInsight(float amount) => ResearchRules.AddInsight(state,amount);

        public float CalculateRate()
        {
            if(placement==null||industry==null)return 0;
            var cognitoriumWork=0f;
            foreach(var building in placement.Buildings)
                if(building!=null&&building.Id=="Cognitorium"&&!building.State.offline)
                    cognitoriumWork+=industry.OperatingFraction(building);
            var vesselActive=false;
            var settlement=GetComponent<Settlement>();
            if(settlement!=null&&settlement.HasExplorator)
                foreach(var building in settlement.CoreBuildings)
                    if(building!=null&&building.Id=="ColonyShip"&&!building.State.offline){vesselActive=true;break;}
            return ResearchRules.InsightRate(vesselActive,cognitoriumWork);
        }

        public void Simulate(float seconds)
        {
            CurrentRate=CalculateRate();
            var completed=ResearchRules.Advance(state,seconds,CurrentRate);
            if(!string.IsNullOrEmpty(completed))
            {
                var project=ResearchRules.Project(completed);
                GetComponent<NewGameMenu>()?.AppendEventLog("Research complete: "+project.Name+". "+project.Effect+".");
            }
        }

        public float PumpYieldMultiplier(string id)
            =>ResearchRules.PumpYieldMultiplier(state,id);
        public float FarmFoodMultiplier => ResearchRules.FarmFoodMultiplier(state);
        public float ProcessorWorkMultiplier(string id)
            =>ResearchRules.ProcessorWorkMultiplier(state,id);
        public float GeneratorOutputMultiplier => ResearchRules.GeneratorOutputMultiplier(state);
        public float SurveyRadiusMultiplier => ResearchRules.SurveyRadiusMultiplier(state);
        public DiscoveryRewardProfile DiscoveryRewards(DiscoveryRewardProfile profile)
            =>ResearchRules.DiscoveryRewards(state,profile);
    }
}
