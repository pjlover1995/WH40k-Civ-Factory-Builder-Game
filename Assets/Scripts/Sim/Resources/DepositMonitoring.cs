using System.Collections.Generic;
using UnityEngine;
using WH30K.Gameplay.Construction;

namespace WH30K.Sim.Resources
{
    public sealed partial class IndustrySystem
    {
        private SurfaceDeposit ExtractionDeposit(ManagedBuilding building)
        {
            if (building == null || !IndustryRules.Extractor(building.Id)) return null;
            var deposit = building.Id == "Mine" || building.Id == "Quarry" ? DepositFor(building)
                : NearestDeposit(building.transform.position, ExtractionKind(building));
            return deposit != null && Vector3.Distance(building.transform.position, deposit.Position) <= 250 ? deposit : null;
        }

        // One row per source, including temporarily idle/offline assigned extractors.
        public void FillExtractionDeposits(List<SurfaceDeposit> result)
        {
            result.Clear();
            foreach (var building in buildings)
            {
                var deposit = ExtractionDeposit(building);
                if (deposit != null && !result.Contains(deposit)) result.Add(deposit);
            }
            result.Sort((a, b) => a.Id.CompareTo(b.Id));
        }
    }
}
