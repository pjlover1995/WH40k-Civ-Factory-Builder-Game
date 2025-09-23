using UnityEngine;
using WH30K.Sim.Environment;

namespace WH30K.Sim.Buildings
{
    /// <summary>
    /// Base representation for a settlement building. Provides capacity, production and visual hints.
    /// </summary>
    public abstract class SettlementBuilding
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract float PopulationCapacity { get; }
        public abstract float WorkforceSlots { get; }
        public abstract float ProductionPerWorker { get; }
        public abstract float UpkeepPerCycle { get; }
        public virtual EnvironmentImpact EnvironmentImpact => EnvironmentImpact.Zero;
        public virtual Color DisplayColor => Color.white;
    }
}
