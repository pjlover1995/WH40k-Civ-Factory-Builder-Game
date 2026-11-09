using UnityEngine;

namespace WH30K.Sim.Buildings
{
    public sealed class ColonyShip : SettlementBuilding
    {
        private readonly bool explorator;
        private readonly int layoutVersion;
        public ColonyShip(bool explorator = true, int layoutVersion = 2) { this.explorator = explorator; this.layoutVersion = layoutVersion; }
        public override string Id => "ColonyShip";
        public override string DisplayName => !explorator ? "Landed Colony Ship" : layoutVersion >= 2 ? "Explorator Landing Tender" : "Explorator Landing Vessel";
        public override float PopulationCapacity => !explorator ? 240f : layoutVersion >= 2 ? ShipRules.CrewCapacity : ShipRules.LegacyCrewCapacity;
        public override float WorkforceSlots => 12f;
        public override float ProductionPerWorker => 1f;
        public override float UpkeepPerCycle => 1f;
        public override Color DisplayColor => new Color(.55f, .58f, .6f);
    }
}
