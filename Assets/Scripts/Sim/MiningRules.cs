using System;

namespace WH30K.Sim
{
    [Serializable]
    public sealed class ExcavationState
    {
        public float[] capacity, removed;
        public float majorRadius, minorRadius, depth, grade, bearing;
        public int blocked;
        // Zero retains the original directional excavation and saved safety decisions.
        public int layoutVersion;
        public float accessAngle;
        public double extractionCarry;
    }
    [Serializable]
    public sealed class MiningConflict
    {
        public bool active, core;
        public int deposit, sector, mine, building;
    }

    // Quantities are gameplay lots of saleable ore, not tonnes of contained metal.
    // Solid pits share one finite body. Older saves retain their eight protected directions.
    public static class MiningRules
    {
        public const int Sectors = 8;
        // JsonUtility writes null inline classes as all-empty objects. Liquid/biomass deposits have no pit.
        public static ExcavationState NormalizeOptional(ExcavationState state)
            => state != null && (state.capacity == null || state.capacity.Length == 0)
                && (state.removed == null || state.removed.Length == 0)
                && state.majorRadius == 0 && state.minorRadius == 0 && state.depth == 0 && state.grade == 0
                && state.bearing == 0 && state.blocked == 0 && state.layoutVersion == 0 && state.accessAngle == 0
                && state.extractionCarry == 0 ? null : state;
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
            if (legacyRemaining < 0)
            {
                state.layoutVersion = 1;
                SizeToReserve(state, kind, total);
            }
            return state;
        }
        // Similar bodies have volume proportional to recoverable lots; their linear
        // dimensions therefore scale by the cube root of the reserve.
        public static void SizeToReserve(ExcavationState state, MaterialKind kind, float total, bool shallow = false)
        {
            var aspect = kind == MaterialKind.Crystal ? .44f : kind == MaterialKind.IronOre ? .58f : .82f;
            var depthRatio = shallow ? .16f : kind == MaterialKind.Stone ? .24f : .30f;
            state.majorRadius = (float)Math.Pow(total * 100 / (aspect * depthRatio), 1.0 / 3);
            state.minorRadius = state.majorRadius * aspect;
            state.depth = state.majorRadius * depthRatio;
        }
        public static float Capacity(ExcavationState state)
        { var total = 0f; foreach (var amount in state.capacity) total += amount; return total; }
        public static float PitScale(ExcavationState state)
            => (float)Math.Pow(Math.Max(0, Math.Min(1, 1 - Remaining(state) / Capacity(state))), 1.0 / 3);
        public static float PitRadiusScale(ExcavationState state)
        { var scale=PitScale(state);return scale<=0?0:.18f+.82f*scale; }
        public static float VeinRadius(ExcavationState state, float bearing)
        {
            var angle = (bearing - state.bearing) * Math.PI / 180;
            return (float)(1 / Math.Sqrt(Math.Pow(Math.Cos(angle) / state.majorRadius, 2)
                + Math.Pow(Math.Sin(angle) / state.minorRadius, 2)));
        }
        public static bool OutsideVein(ExcavationState state, float distance, float bearing, float footprint)
        {
            // Conservative ellipse clearance, including the oblique sides of narrow veins.
            var radius = VeinRadius(state, bearing);
            return (distance / radius - 1) * Math.Min(state.majorRadius, state.minorRadius) >= footprint + 12;
        }
        public static float ExtractSolid(ExcavationState state, float amount)
        {
            if (!ColonyRules.IsFinite(amount) || amount <= 0) return 0;
            var available = Remaining(state); var taken = Math.Min(available, amount);
            if (available <= 0) return 0;
            var requested=taken+state.extractionCarry;
            var physical=(float)Math.Max(0,Math.Min(available,requested));
            // One expanding pit, independent of the number or ordering of extractors.
            for (var s = 0; s < Sectors; s++)
                state.removed[s] = Math.Min(state.capacity[s], state.removed[s] + physical * (state.capacity[s] - state.removed[s]) / available);
            var remaining=Remaining(state);
            // Preserve sub-float work between cycles instead of gradually creating ore
            // when eight large directional counters round their small increments.
            state.extractionCarry=remaining<=0?0:requested-(available-remaining);
            return taken;
        }
        public static float RoadWidth(ExcavationState state) => Math.Min(6, state.minorRadius * .14f);
        public static void HaulPoint(ExcavationState state, float t, out float x, out float y, out float drop)
        {
            t = Math.Max(0, Math.Min(1, t));
            var scale=PitScale(state);
            FixedHaulPoint(state,1-scale+scale*t,out x,out y,out drop);
            drop-=state.depth*(1-scale);
        }
        private static void FixedHaulPoint(ExcavationState state,float t,out float x,out float y,out float drop)
        {
            var angle=state.accessAngle*Math.PI/180+t*Math.PI*4;
            var radius=1-.82f*t;
            x=state.majorRadius*radius*(float)Math.Cos(angle);
            y=state.minorRadius*radius*(float)Math.Sin(angle);
            drop=state.depth*t;
        }
        public static float SolidCutDepth(ExcavationState state, float x, float y)
        {
            var scale = PitScale(state); if (scale <= 0) return 0;
            var a = state.majorRadius; var b = state.minorRadius;
            var radius = (float)Math.Sqrt(x * x / (a * a) + y * y / (b * b));
            if (radius >= 1) return 0;
            var depth = state.depth;
            var drop = Math.Min(depth, depth * (1 - radius) / .82f);
            var terrace = (float)Math.Floor(drop / 4) * 4;
            var blend = Math.Max(0, Math.Min(1, (drop % 4 - 2) / 2));
            drop = drop>=depth ? depth : Math.Min(depth, terrace + blend * blend * (3 - 2 * blend) * 4);
            var angle = Math.Atan2(y / b, x / a) - state.accessAngle * Math.PI / 180;
            angle = (angle % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
            // Find the nearest turn of a two-turn spiral. The road is a retained,
            // graded shelf in the heightfield, not a floating line over the benches.
            var nearest = float.MaxValue; var rampDrop = 0f;
            for (var turn = 0; turn <= 2; turn++)
            {
                var t = (float)((angle + turn * 2 * Math.PI) / (4 * Math.PI));
                if (t > 1) continue;
                FixedHaulPoint(state, t, out var rx, out var ry, out var rd);
                var distance = (float)Math.Sqrt((x - rx) * (x - rx) + (y - ry) * (y - ry));
                if (distance < nearest) { nearest = distance; rampDrop = rd; }
            }
            var weight = Math.Max(0, Math.Min(1, (RoadWidth(state) * .9f - nearest) / Math.Max(.01f, RoadWidth(state) * .4f)));
            // The final benches/ramp stay fixed in ground coordinates. Lowering their
            // excavation horizon is monotonic: widening can never refill an older cut.
            return Math.Max(0, drop + (rampDrop - drop) * weight-depth*(1-scale));
        }
        public static string Geology(MaterialKind kind) => kind == MaterialKind.IronOre ? "Banded iron lens"
            : kind == MaterialKind.CopperOre ? "Disseminated copper body" : kind == MaterialKind.Crystal ? "Hydrothermal crystal vein" : "Bedded stone deposit";
        public static float RichnessMultiplier(MaterialKind kind,float grade)
        {
            var minimum=kind==MaterialKind.IronOre ? .50f : kind==MaterialKind.CopperOre ? .003f : kind==MaterialKind.Crystal ? .015f : .85f;
            var maximum=kind==MaterialKind.IronOre ? .65f : kind==MaterialKind.CopperOre ? .012f : kind==MaterialKind.Crystal ? .05f : .85f;
            var normalized=maximum-minimum<.0001f ? .5f : (float)Math.Max(0,Math.Min(1,(grade-minimum)/(maximum-minimum)));
            return .75f+normalized*.5f;
        }
        public static bool Valid(ExcavationState state)
        {
            if (state == null || state.capacity == null || state.removed == null || state.capacity.Length != 8 || state.removed.Length != 8
                || state.blocked < 0 || state.blocked > 255 || state.layoutVersion < 0 || state.layoutVersion > 1
                || state.layoutVersion == 1 && state.blocked != 0 || !Range(state.accessAngle, 0, 360)
                || double.IsNaN(state.extractionCarry) || double.IsInfinity(state.extractionCarry) || Math.Abs(state.extractionCarry)>.05
                || !Range(state.majorRadius, 1, 200) || !Range(state.minorRadius, 1, 150)
                || !Range(state.depth, 1, 80) || !Range(state.grade, .001f, 1) || !Range(state.bearing, 0, 360)) return false;
            if (state.layoutVersion == 0 && (state.majorRadius < 40 || state.minorRadius < 20 || state.depth < 8)) return false;
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
            if (state.layoutVersion == 1)
            {
                var fractionRemoved = Math.Max(0, Math.Min(1, (Capacity(state) - Remaining(state) + additional) / Capacity(state)));
                return fractionRemoved<=0?0:VeinRadius(state, bearing) * (.18f+.82f*(float)Math.Pow(fractionRemoved, 1.0 / 3));
            }
            var fraction = Math.Min(1, (state.removed[sector] + additional) / state.capacity[sector]);
            if (fraction <= 0) return 0;
            var angle = (bearing-state.bearing) * Math.PI / 180;
            var radius = 1 / Math.Sqrt(Math.Pow(Math.Cos(angle) / state.majorRadius, 2) + Math.Pow(Math.Sin(angle) / state.minorRadius, 2));
            return 32 + (float)(radius - 32) * (float)Math.Pow(fraction, .55);
        }
        public static float Depth(ExcavationState state, int sector)
            => state.layoutVersion == 1 ? state.depth * PitScale(state)
                : state.removed[sector] <= 0 ? 0 : Math.Min(state.depth, 4 + (float)Math.Floor(state.removed[sector] / state.capacity[sector] * state.depth / 4) * 4);
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
        public static void Limit(ExcavationState state, int sector) { if (state.layoutVersion==0 && sector >= 0 && sector < 8) state.blocked |= 1 << sector; }
    }
}
