using System;

namespace WH30K.Sim
{
    [Serializable]
    public sealed class FacilityConfiguration
    {
        // Stable recipe indices; -1 disables a line. Work belongs to the selected recipe.
        public int[] products = { 0, -1, -1, -1 };
        public float[] productWork = new float[4];
        public float bufferLimit = IndustryRules.StorageLimit;
        public bool designated;
        public float[] storageLimits = new float[IndustryRules.MaterialCount];
    }

    // Engine-independent production, allocation and storage rules shared by UI and simulation.
    public static class FacilityRules
    {
        public const int ProductCount = 6;
        public const float DepotCapacity = 1200;
        private static readonly MaterialKind[] Outputs = { MaterialKind.Components, MaterialKind.Metal, MaterialKind.Plasteel, MaterialKind.Crystal, MaterialKind.IntermediateFertilizer, MaterialKind.AdvancedFertilizer };
        private static readonly float[][] Ingredients = {
            new float[] { 1, 0, 1, .5f, 0, 0, 0, 0, 0, 0, 0, 0 },
            new float[] { 2, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0 },
            new float[] { 2, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0 },
            new float[] { 0, 0, 2, 0, 0, 0, 0, 1, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 1, 0, 0, 0, 0, 0, 2, 0, 0 },
            new float[] { 0, 0, .5f, 0, .25f, 0, 0, .25f, 0, 0, 1, 0 }
        };
        public static MaterialKind Output(int product) => Outputs[product];
        public static string Recipe(int product)
            => product == 0 ? "1 iron + 1 stone + 0.5 biomass -> 1 component"
            : product == 1 ? "2 iron + 1 biomass -> 1 refined metal"
            : product == 2 ? "2 iron + 1 promethium -> 1 plasteel"
            : product == 3 ? "2 stone + 1 promethium -> 1 synthetic crystal"
            : product == 4 ? "2 reclaimed waste + 1 biomass -> 1 compost fertilizer"
            : "1 compost fertilizer + 0.5 stone + 0.25 crystal + 0.25 promethium -> 1 mineral nutrient";
        public static int ActiveCount(FacilityConfiguration config)
        { var count = 0; foreach (var product in config.products) if (product >= 0) count++; return count; }
        public static bool Produces(FacilityConfiguration config, MaterialKind kind)
        { foreach (var product in config.products) if (product >= 0 && Output(product) == kind) return true; return false; }
        public static bool Accepts(FacilityConfiguration config, MaterialKind kind)
        { foreach (var product in config.products) if (product >= 0 && Ingredients[product][(int)kind] > 0) return true; return false; }
        public static float Total(float[] quantities)
        { var total = 0f; foreach (var amount in quantities) total += amount; return total; }
        public static float Allocated(FacilityConfiguration config) => Total(config.storageLimits);
        public static float DepotRoom(FacilityConfiguration config, float[] stock, MaterialKind kind, float reservedKind, float reservedTotal)
        {
            var totalRoom = Math.Max(0, DepotCapacity - Total(stock) - reservedTotal);
            return config.designated ? Math.Max(0, Math.Min(totalRoom, config.storageLimits[(int)kind] - stock[(int)kind] - reservedKind)) : totalRoom;
        }
        public static bool SelectProduct(FacilityConfiguration config, int slot, int product)
        {
            if (slot < 0 || slot >= 4 || product < -1 || product >= ProductCount) return false;
            for (var i = 0; i < 4; i++) if (i != slot && product >= 0 && config.products[i] == product) return false;
            if (config.products[slot] != product) { config.products[slot] = product; config.productWork[slot] = 0; }
            return true;
        }
        public static bool SetStorageLimit(FacilityConfiguration config, MaterialKind kind, float limit)
        {
            if ((int)kind < 0 || (int)kind >= IndustryRules.MaterialCount || !ColonyRules.IsFinite(limit) || limit < 0
                || Allocated(config) - config.storageLimits[(int)kind] + limit > DepotCapacity + .001f) return false;
            config.storageLimits[(int)kind] = limit; return true;
        }
        public static string ProductStatus(FacilityConfiguration config, float[] stock, int slot, float reservedOutput = 0)
        {
            var product = config.products[slot]; if (product < 0) return "Disabled";
            if (stock[(int)Output(product)] + reservedOutput + 1 > config.bufferLimit + .0001f) return "BUFFER FULL / halted";
            for (var k = 0; k < IndustryRules.MaterialCount; k++)
                if (stock[k] < Ingredients[product][k]) return "Awaiting inputs";
            return "Ready";
        }
        public static float Advance(FacilityConfiguration config, float[] stock, float effectiveWork, float[] produced, float[] consumed,
            float[] reservations = null, int reservationOffset = 0)
        {
            var count = ActiveCount(config); if (count == 0 || effectiveWork <= 0 || !ColonyRules.IsFinite(effectiveWork)) return 0;
            var cycles = 0f;
            for (var slot = 0; slot < 4; slot++)
            {
                var product = config.products[slot]; if (product < 0) continue;
                var output = (int)Output(product);
                config.productWork[slot] += effectiveWork / count;
                while (config.productWork[slot] >= 5)
                {
                    var reserved = reservations != null ? reservations[reservationOffset + output] : 0;
                    if (ProductStatus(config, stock, slot, reserved) != "Ready")
                    { config.productWork[slot] = 5; break; }
                    for (var k = 0; k < IndustryRules.MaterialCount; k++)
                    { stock[k] -= Ingredients[product][k]; consumed[k] += Ingredients[product][k]; }
                    stock[output]++; produced[output]++; config.productWork[slot] -= 5; cycles++;
                }
            }
            return cycles;
        }
        public static FacilityConfiguration Clone(FacilityConfiguration config) => config == null ? null : new FacilityConfiguration {
            products = (int[])config.products.Clone(), productWork = (float[])config.productWork.Clone(),
            bufferLimit = config.bufferLimit, designated = config.designated, storageLimits = (float[])IndustryRules.UpgradeStock(config.storageLimits).Clone() };
        public static bool Valid(FacilityConfiguration config)
        {
            if (config == null) return true; // Optional additive fields migrate existing schema-12 and older saves.
            if (config.products == null || config.products.Length != 4 || config.productWork == null || config.productWork.Length != 4
                || config.storageLimits == null || (config.storageLimits.Length != IndustryRules.MaterialCount && config.storageLimits.Length != IndustryRules.PreviousMaterialCount)
                || !ColonyRules.IsFinite(config.bufferLimit) || config.bufferLimit < 1 || config.bufferLimit > IndustryRules.StorageLimit) return false;
            var selected = 0;
            for (var slot = 0; slot < 4; slot++)
            {
                var product = config.products[slot]; var work = config.productWork[slot];
                if (product < -1 || product >= ProductCount || !ColonyRules.IsFinite(work) || work < 0 || work > 5
                    || product < 0 && work != 0 || product >= 0 && (selected & (1 << product)) != 0) return false;
                if (product >= 0) selected |= 1 << product;
            }
            foreach (var limit in config.storageLimits) if (!ColonyRules.IsFinite(limit) || limit < 0 || limit > DepotCapacity) return false;
            return Allocated(config) <= DepotCapacity + .001f;
        }
    }
}
