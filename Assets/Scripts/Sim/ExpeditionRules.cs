using System;

namespace WH30K.Sim
{
    // A finite manifest, independent of difficulty: every expedition can establish its supply chain.
    public static class ExpeditionRules
    {
        public static float[] Manifest(float basicBuildingCosts, float advancedBuildingCosts)
        {
            var stock = new float[IndustryRules.MaterialCount];
            stock[(int)MaterialKind.Components] = basicBuildingCosts;
            stock[(int)MaterialKind.Plasteel] = advancedBuildingCosts;
            return stock;
        }

        public static bool SurfaceVisible(int id) => id >= 0 && id % 3 == 0;
        public static float SurfaceReserve(int seed, int id, float multiplier = 1f)
            => (120 + new Random(unchecked(seed ^ id * 92821)).Next(61)) * Math.Max(.05f, multiplier);

        public static ExcavationState SurfaceVein(int seed, int id, MaterialKind kind, float multiplier = 1f)
        {
            var vein = MiningRules.Create(seed, id, kind);
            var scale = SurfaceReserve(seed, id, multiplier) / MiningRules.Remaining(vein);
            for (var sector = 0; sector < 8; sector++) vein.capacity[sector] *= scale;
            MiningRules.SizeToReserve(vein, kind, SurfaceReserve(seed, id, multiplier), shallow: true);
            return vein;
        }

        public static bool TrySpend(float[] cargo, MaterialKind kind, ref float amount)
        {
            if (cargo == null || cargo.Length != IndustryRules.MaterialCount || amount <= 0) return amount <= 0;
            var take = Math.Min(cargo[(int)kind], amount);
            cargo[(int)kind] -= take; amount -= take;
            return amount <= .0001f;
        }
    }
}
