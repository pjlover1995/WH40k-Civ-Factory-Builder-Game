using UnityEngine;
using WH30K.Sim.Environment;

namespace WH30K.Sim.Buildings
{
    public class Factory : SettlementBuilding
    {
        public override string Id => "Factory";
        public override string DisplayName => "Macro Factory";
        public override float PopulationCapacity => 120f;
        public override float WorkforceSlots => 200f;
        public override float ProductionPerWorker => 1.35f;
        public override float UpkeepPerCycle => 32f;
        public override EnvironmentImpact EnvironmentImpact => new EnvironmentImpact
        {
            co2Delta = 3.5f,
            o2Delta = -0.25f,
            waterDelta = 2.1f,
            temperatureDelta = 0.18f
        };
        public override Color DisplayColor => new Color(0.85f, 0.45f, 0.3f);
    }
}
