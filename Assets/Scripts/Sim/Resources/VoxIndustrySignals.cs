using WH30K.Gameplay.Construction;

namespace WH30K.Sim.Resources
{
    public sealed partial class IndustrySystem
    {
        // Observe the same live stock and product readiness used by production.
        internal bool TryGetVoxManufacturingBottleneck(out string manufacturing)
        {
            manufacturing = null;
            for (var i = 0; i < buildings.Length && i < stocks.Count; i++)
            {
                ManagedBuilding building = buildings[i];
                var stock = stocks[i];
                var config = stock?.configuration;
                if (building == null || building.Id != "Manufactorum" || building.State.offline ||
                    config == null || FacilityRules.ActiveCount(config) == 0) continue;

                for (var slot = 0; slot < config.products.Length; slot++)
                {
                    var product = config.products[slot];
                    if (product < 0) continue;
                    var output = FacilityRules.Output(product);
                    var status = FacilityRules.ProductStatus(config, stock.quantities, slot, Incoming(i, (int)output));
                    if (status != "Awaiting inputs") continue;
                    manufacturing = IndustryRules.Name(output);
                    return true;
                }
            }
            return false;
        }
    }
}
