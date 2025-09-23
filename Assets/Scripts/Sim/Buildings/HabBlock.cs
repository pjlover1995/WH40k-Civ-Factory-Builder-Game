using UnityEngine;
using WH30K.Sim.Environment;

namespace WH30K.Sim.Buildings
{
    public class HabBlock : SettlementBuilding
    {
        public override string Id => "HabBlock";
        public override string DisplayName => "Hab Block";
        public override float PopulationCapacity => 320f;
        public override float WorkforceSlots => 40f;
        public override float ProductionPerWorker => 0.15f;
        public override float UpkeepPerCycle => 8f;
        public override EnvironmentImpact EnvironmentImpact => new EnvironmentImpact
        {
            co2Delta = 0.2f,
            o2Delta = -0.05f,
            waterDelta = 0.1f,
            temperatureDelta = 0.01f
        };
        public override Color DisplayColor => new Color(0.8f, 0.85f, 1f);
    }
}
