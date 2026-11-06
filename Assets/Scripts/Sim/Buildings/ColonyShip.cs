using UnityEngine;

namespace WH30K.Sim.Buildings
{
    public sealed class ColonyShip : SettlementBuilding
    {
        private readonly bool explorator;
        public ColonyShip(bool explorator = true) { this.explorator = explorator; }
        public override string Id => "ColonyShip";
        public override string DisplayName => explorator ? "Explorator Landing Vessel" : "Landed Colony Ship";
        public override float PopulationCapacity => explorator ? ShipRules.CrewCapacity : 240f;
        public override float WorkforceSlots => 12f;
        public override float ProductionPerWorker => 1f;
        public override float UpkeepPerCycle => 1f;
        public override Color DisplayColor => new Color(.55f, .58f, .6f);
    }
}
