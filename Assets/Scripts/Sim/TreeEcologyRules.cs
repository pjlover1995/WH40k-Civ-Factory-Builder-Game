using System;

namespace WH30K.Sim
{
    /// <summary>Stable identity and simulation-time growth for procedural planetary trees.</summary>
    public static class TreeEcologyRules
    {
        public const double MaturitySeconds = ColonyCalendar.SecondsPerYear * 24d;
        public const int TileRadius = 20;
        public const int MaximumResidentTrees = (TileRadius * 2 + 1) * (TileRadius * 2 + 1);

        // Keep trees metres apart, including on continental worlds. Older small worlds
        // retain their original cell addresses; tree placement is derived, not saved.
        public static int CellResolution(float radius)
            => radius<=50000 ? 1024 : Math.Max(1024, Math.Min(WH30K.Planet.PlanetScale.MaximumGridResolution,
                (int)Math.Round(1024d * radius / 6000d)));

        public static int StableHash(int planetSeed, int face, int x, int y)
        {
            unchecked
            {
                var h = (uint)planetSeed ^ (uint)face * 0x9E3779B9u ^ (uint)x * 73856093u ^ (uint)y * 19349663u;
                h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
                return (int)h;
            }
        }

        public static float InitialMaturity(int planetSeed, int face, int x, int y)
        {
            var hash = (uint)StableHash(planetSeed, face, x, y);
            return .18f + (hash & 0xFFFFu) / 65535f * .42f;
        }

        public static float Maturity(int planetSeed, int face, int x, int y, double simulationSeconds)
        {
            if (double.IsNaN(simulationSeconds) || double.IsInfinity(simulationSeconds) || simulationSeconds < 0) simulationSeconds = 0;
            return Math.Min(1f, InitialMaturity(planetSeed, face, x, y) + (float)(simulationSeconds / MaturitySeconds));
        }

        public static float BiomassYield(float maturity) => 2f + 8f * Math.Max(0f, Math.Min(1f, maturity));
    }
}
