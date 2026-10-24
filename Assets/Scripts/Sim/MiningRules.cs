using System;

namespace WH30K.Sim
{
    [Serializable]
    public sealed class ExcavationState
    {
        public float[] capacity, removed;
        public float majorRadius, minorRadius, depth, grade, bearing;
        public int blocked;
    }
    [Serializable]
    public sealed class MiningConflict
    {
        public bool active, core;
        public int deposit, sector, mine, building;
    }

    // Quantities are gameplay lots of saleable ore, not tonnes of contained metal.
    // Eight working directions share a finite ore body; protected sectors retain their ore.
    public static class MiningRules
    {
        public const int Sectors = 8;
        public static ExcavationState Create(int seed, int id, MaterialKind kind, float legacyRemaining = -1)
        {
            var random = new Random(unchecked(seed ^ id * 73856093 ^ 0x531F29));
            var size = (float)random.NextDouble();
            var iron = kind == MaterialKind.IronOre; var copper = kind == MaterialKind.CopperOre;
            var crystal = kind == MaterialKind.Crystal;
            var total = legacyRemaining >= 0 ? 2000 : iron ? 1800 + size * 3200 : copper ? 900 + size * 2100
                : crystal ? 350 + size * 850 : 3500 + size * 5500;
            var state = new ExcavationState { capacity = new float[8], removed = new float[8],
                majorRadius = crystal ? 95 + size * 40 : iron ? 110 + size * 55 : 95 + size * 45,
                minorRadius = crystal ? 42 + size * 12 : iron ? 65 + size * 20 : 80 + size * 30,
                depth = crystal ? 44 : iron ? 40 : copper ? 48 : 32,
                grade = iron ? .50f + size * .15f : copper ? .003f + size * .009f : crystal ? .015f + size * .035f : .85f,
                bearing = (float)random.NextDouble() * 360 };
            var sum = 0f;
            for (var s = 0; s < 8; s++)
            {
                var a = s * Math.PI / 4;
                // Elongated banded iron / narrow crystal lenses; broader disseminated copper bodies.
                var shape = (float)(.65 + .35 * Math.Abs(Math.Cos(a)));
                state.capacity[s] = shape * (.8f + (float)random.NextDouble() * .4f); sum += state.capacity[s];
            }
            for (var s = 0; s < 8; s++)
            {
                state.capacity[s] *= total / sum;
                if (legacyRemaining >= 0) state.removed[s] = state.capacity[s] * (1 - legacyRemaining / 2000);
            }
            return state;
        }
        public static string Geology(MaterialKind kind) => kind == MaterialKind.IronOre ? "Banded iron lens"
            : kind == MaterialKind.CopperOre ? "Disseminated copper body" : kind == MaterialKind.Crystal ? "Hydrothermal crystal vein" : "Bedded stone deposit";
        public static bool Valid(ExcavationState state)
        {
            if (state == null || state.capacity == null || state.removed == null || state.capacity.Length != 8 || state.removed.Length != 8
                || state.blocked < 0 || state.blocked > 255 || !Range(state.majorRadius, 40, 200) || !Range(state.minorRadius, 20, 150)
                || !Range(state.depth, 8, 80) || !Range(state.grade, .001f, 1) || !Range(state.bearing, 0, 360)) return false;
            var total = 0f;
            for (var s = 0; s < 8; s++)
            { if (!Range(state.capacity[s], 1, 4000) || !Range(state.removed[s], 0, state.capacity[s])) return false; total += state.capacity[s]; }
            return total <= 20000;
        }
        private static bool Range(float value, float min, float max) => ColonyRules.IsFinite(value) && value >= min && value <= max;
        public static float Remaining(ExcavationState state, bool accessibleOnly = false)
        {
            var total = 0f;
            for (var s = 0; s < 8; s++) if (!accessibleOnly || (state.blocked & 1 << s) == 0)
                total += Math.Max(0, state.capacity[s] - state.removed[s]);
            return total;
        }
        public static int NextSector(ExcavationState state)
        {
            var best = -1; var progress = float.MaxValue;
            for (var s = 0; s < 8; s++)
            {
                if ((state.blocked & 1 << s) != 0 || state.capacity[s] - state.removed[s] < .0001f) continue;
                var fraction = state.removed[s] / state.capacity[s];
                if (fraction < progress) { progress = fraction; best = s; }
            }
            return best;
        }
        public static float Radius(ExcavationState state, int sector, float additional = 0)
            => RadiusAt(state,sector,state.bearing+sector*45,additional);
        public static float RadiusAt(ExcavationState state,int sector,float bearing,float additional=0)
        {
            var fraction = Math.Min(1, (state.removed[sector] + additional) / state.capacity[sector]);
            if (fraction <= 0) return 0;
            var angle = (bearing-state.bearing) * Math.PI / 180;
            var radius = 1 / Math.Sqrt(Math.Pow(Math.Cos(angle) / state.majorRadius, 2) + Math.Pow(Math.Sin(angle) / state.minorRadius, 2));
            return 32 + (float)(radius - 32) * (float)Math.Pow(fraction, .55);
        }
        public static float Depth(ExcavationState state, int sector)
            => state.removed[sector] <= 0 ? 0 : Math.Min(state.depth, 4 + (float)Math.Floor(state.removed[sector] / state.capacity[sector] * state.depth / 4) * 4);
        public static bool Threatens(ExcavationState state, int sector, float distance, float bearing, float footprint, float additional = 1)
        {
            var centre=state.bearing+sector*45;
            var radius = Math.Max(Radius(state, sector, additional),Math.Max(RadiusAt(state,sector,centre-22.5f,additional),RadiusAt(state,sector,centre+22.5f,additional)));
            if (distance > radius + footprint + 12) return false;
            if (distance <= footprint + 12) return true;
            var delta = Math.Abs(((bearing - state.bearing - sector * 45 + 540) % 360) - 180);
            return delta <= 22.5 + Math.Asin(Math.Min(1, (footprint + 12) / distance)) * 180 / Math.PI;
        }
        public static float Extract(ExcavationState state, int sector, float amount)
        {
            if (sector < 0 || sector >= 8 || (state.blocked & 1 << sector) != 0 || !ColonyRules.IsFinite(amount) || amount <= 0) return 0;
            var taken = Math.Min(amount, state.capacity[sector] - state.removed[sector]);
            state.removed[sector] += taken; return taken;
        }
        public static void Limit(ExcavationState state, int sector) { if (sector >= 0 && sector < 8) state.blocked |= 1 << sector; }
    }
}
