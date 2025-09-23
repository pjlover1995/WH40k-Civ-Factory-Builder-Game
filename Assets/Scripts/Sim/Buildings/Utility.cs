using UnityEngine;
using WH30K.Sim.Environment;

namespace WH30K.Sim.Buildings
{
    public class Utility : SettlementBuilding
    {
        public override string Id => "Utility";
        public override string DisplayName => "Utility Nexus";
        public override float PopulationCapacity => 90f;
        public override float WorkforceSlots => 80f;
        public override float ProductionPerWorker => 0.55f;
        public override float UpkeepPerCycle => 14f;
        public override EnvironmentImpact EnvironmentImpact => new EnvironmentImpact
        {
            co2Delta = 1.1f,
            o2Delta = -0.08f,
            waterDelta = 1f,
            temperatureDelta = 0.05f
        };
        public override Color DisplayColor => new Color(0.45f, 0.75f, 0.6f);
    }
}
