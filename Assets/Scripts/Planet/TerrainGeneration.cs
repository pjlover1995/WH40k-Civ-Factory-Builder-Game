using System;

namespace WH30K.Planet
{
    public static class PlanetScale
    {
        // NASA Earth Fact Sheet: volumetric mean radius, in metres. One Unity unit = one metre.
        public const float EarthRadius = 6371000f;
        public const float DefaultRadius = EarthRadius * .5f;
        public const float MaximumRadius = 10000000f;
        public const int MaximumGridResolution = 1048576;
        public const float MaximumSurfaceRadius = MaximumRadius + 20000f;

        public static int ConstructionResolution(float radius)
            => Math.Max(256, Math.Min(MaximumGridResolution, (int)Math.Round(256d * radius / 6000d)));

        // Preserve all historical recipes; large worlds use kilometre-scale relief rather
        // than multiplying hills into mountains hundreds of kilometres high.
        public static float ReliefScale(float radius) => radius <= 50000f ? radius / 6000f : 6f;

        public static float SurfaceDistance(float ax, float ay, float az, float bx, float by, float bz, float radius)
        {
            double x = (double)ay * bz - (double)az * by;
            double y = (double)az * bx - (double)ax * bz;
            double z = (double)ax * by - (double)ay * bx;
            double dot = (double)ax * bx + (double)ay * by + (double)az * bz;
            return (float)(Math.Atan2(Math.Sqrt(x * x + y * y + z * z), dot) * radius);
        }
    }

    public enum TerrainMethod { Fractal, Geographic, Hybrid }
    public enum TerrainOverlay { None, Elevation, Slope, Biomes }
    public enum ClimateBiome { TemperateGrassland, Forest, Desert, RockyHighlands, Tundra, SnowIce }

    [Serializable]
    public sealed class TerrainSettings
    {
        public TerrainMethod method = TerrainMethod.Hybrid;
        public float radius = PlanetScale.DefaultRadius;
        // Metres relative to the reference sphere, not a noise threshold.
        public float seaLevel = -20f;
        public float continentalCoverage = 0.52f;
        public float mountainIntensity = 1f;
        public float roughness = 0.25f;
        public float temperature = 0.5f;
        public float moisture = 0.5f;
        public float forestCoverage = 0.55f;
        public float desertCoverage = 0.3f;
        public float snowTemperature = 0.22f;

        public TerrainSettings Copy() => (TerrainSettings)MemberwiseClone();

        // New expeditions use a coherent climate/landform recipe selected from the world seed.
        // The full result is saved with the world, so loading never re-rolls a planet. Presets
        // deliberately share the same bounded sampler and differ in continentality, uplift,
        // temperature and moisture rather than relying on unrelated cosmetic noise.
        public static TerrainSettings ForNewWorld(int seed, TerrainSettings basis = null)
        {
            var result = (basis ?? new TerrainSettings()).Copy();
            var random = new Random(unchecked(seed ^ (int)0x6D2B79F5));
            var type = random.Next(6);
            double variation() => random.NextDouble() * 2 - 1;
            switch (type)
            {
                case 0: // Broad temperate continents and long collision ranges.
                    result.method = TerrainMethod.Geographic; result.continentalCoverage = .64f + (float)variation() * .06f;
                    result.mountainIntensity = 1.25f + (float)random.NextDouble() * .4f; result.roughness = .38f + (float)random.NextDouble() * .2f;
                    result.temperature = .48f + (float)variation() * .07f; result.moisture = .52f + (float)variation() * .13f;
                    result.forestCoverage = .56f + (float)variation() * .12f; result.desertCoverage = .24f; result.snowTemperature = .2f;
                    break;
                case 1: // Oceanic, humid world with broken island arcs.
                    result.method = TerrainMethod.Hybrid; result.continentalCoverage = .36f + (float)variation() * .045f;
                    result.mountainIntensity = 1.15f + (float)random.NextDouble() * .35f; result.roughness = .42f + (float)random.NextDouble() * .2f;
                    result.temperature = .64f + (float)variation() * .08f; result.moisture = .7f + (float)variation() * .1f;
                    result.forestCoverage = .72f; result.desertCoverage = .12f; result.snowTemperature = .16f;
                    break;
                case 2: // Hot arid interiors, with wetter coasts and high terrain.
                    result.method = TerrainMethod.Fractal; result.continentalCoverage = .56f + (float)variation() * .06f;
                    result.mountainIntensity = 1.25f + (float)random.NextDouble() * .4f; result.roughness = .62f + (float)random.NextDouble() * .22f;
                    result.temperature = .78f + (float)variation() * .07f; result.moisture = .25f + (float)variation() * .1f;
                    result.forestCoverage = .28f; result.desertCoverage = .78f; result.snowTemperature = .12f;
                    break;
                case 3: // Cold continental world with extensive tundra and ice caps.
                    result.method = TerrainMethod.Geographic; result.continentalCoverage = .62f + (float)variation() * .07f;
                    result.mountainIntensity = 1.35f + (float)random.NextDouble() * .45f; result.roughness = .4f + (float)random.NextDouble() * .24f;
                    result.temperature = .28f + (float)variation() * .06f; result.moisture = .52f + (float)variation() * .14f;
                    result.forestCoverage = .44f; result.desertCoverage = .18f; result.snowTemperature = .34f;
                    break;
                case 4: // Rugged, highly dissected uplands and narrow lowlands.
                    result.method = TerrainMethod.Fractal; result.continentalCoverage = .59f + (float)variation() * .06f;
                    result.mountainIntensity = 1.65f + (float)random.NextDouble() * .3f; result.roughness = .76f + (float)random.NextDouble() * .18f;
                    result.temperature = .48f + (float)variation() * .1f; result.moisture = .42f + (float)variation() * .15f;
                    result.forestCoverage = .42f; result.desertCoverage = .4f; result.snowTemperature = .23f;
                    break;
                default: // Warm, wet mixed biome world with broad forests and grasslands.
                    result.method = TerrainMethod.Hybrid; result.continentalCoverage = .57f + (float)variation() * .07f;
                    result.mountainIntensity = 1.1f + (float)random.NextDouble() * .4f; result.roughness = .32f + (float)random.NextDouble() * .22f;
                    result.temperature = .62f + (float)variation() * .08f; result.moisture = .62f + (float)variation() * .12f;
                    result.forestCoverage = .64f; result.desertCoverage = .2f; result.snowTemperature = .18f;
                    break;
            }
            return result;
        }

        public static bool IsDefaultRecipe(TerrainSettings s)
        {
            var d = new TerrainSettings();
            return s != null && s.method == d.method && s.seaLevel == d.seaLevel && s.continentalCoverage == d.continentalCoverage
                && s.mountainIntensity == d.mountainIntensity && s.roughness == d.roughness && s.temperature == d.temperature
                && s.moisture == d.moisture && s.forestCoverage == d.forestCoverage && s.desertCoverage == d.desertCoverage
                && s.snowTemperature == d.snowTemperature;
        }

        public static bool Valid(TerrainSettings s) => s != null && Enum.IsDefined(typeof(TerrainMethod), s.method)
            && In(s.radius, 1000, PlanetScale.MaximumRadius) && In(s.seaLevel, -s.radius * .08f, s.radius * .08f)
            && In(s.continentalCoverage, .15f, .85f) && In(s.mountainIntensity, 0, 2)
            && In(s.roughness, 0, 1) && In(s.temperature, 0, 1) && In(s.moisture, 0, 1)
            && In(s.forestCoverage, 0, 1) && In(s.desertCoverage, 0, 1) && In(s.snowTemperature, .05f, .45f);
        private static bool In(float x, float a, float b) => !float.IsNaN(x) && !float.IsInfinity(x) && x >= a && x <= b;
    }

    public struct BiomeSample
    {
        public float temperature, moisture, grass, forest, desert, rock, tundra, snow;
        public ClimateBiome Dominant
        {
            get
            {
                var best = grass; var result = ClimateBiome.TemperateGrassland;
                if (forest > best) { best = forest; result = ClimateBiome.Forest; }
                if (desert > best) { best = desert; result = ClimateBiome.Desert; }
                if (rock > best) { best = rock; result = ClimateBiome.RockyHighlands; }
                if (tundra > best) { best = tundra; result = ClimateBiome.Tundra; }
                if (snow > best) result = ClimateBiome.SnowIce;
                return result;
            }
        }
    }

    // Deterministic relative plate motion at the nearest Voronoi boundary. Strengths are
    // dimensionless [0,1] influence masks used to build mountain belts, trenches and rifts.
    public struct TectonicBoundarySample
    {
        public float boundary, convergence, divergence, transform;
    }

    // The contract has no dependency on meshes, faces, Unity noise or LOD. Elevations are metres
    // above the reference sphere. The same immutable sampler serves rendering and gameplay.
    public interface ITerrainHeightSampler
    {
        float Elevation(float x, float y, float z);
    }

    public abstract class TerrainSampler : ITerrainHeightSampler
    {
        protected readonly TerrainSettings settings;
        private readonly int seed;
        private readonly Plate[] plates = new Plate[18];
        private struct Plate { public double x, y, z, altitude, wx, wy, wz; }
        public static TerrainSampler Create(int seed, TerrainSettings settings)
        {
            if (!TerrainSettings.Valid(settings)) throw new ArgumentException("Invalid terrain recipe.");
            switch (settings.method)
            {
                case TerrainMethod.Fractal: return new FractalTerrainSampler(seed, settings);
                case TerrainMethod.Geographic: return new GeographicTerrainSampler(seed, settings);
                default: return new HybridTerrainSampler(seed, settings);
            }
        }

        protected TerrainSampler(int seed, TerrainSettings settings)
        {
            if (!TerrainSettings.Valid(settings)) throw new ArgumentException("Invalid terrain recipe.");
            this.seed = seed; this.settings = settings.Copy();
            // Equal-area Fibonacci plate centres with seeded rotation and buoyancy. No sampling allocations.
            var angle = Unit(Hash(seed, 7, 0, 0)) * Math.PI * 2;
            for (var i = 0; i < plates.Length; i++)
            {
                var y = 1 - 2 * (i + .5) / plates.Length;
                var r = Math.Sqrt(1 - y * y); var a = i * 2.399963229728653 + angle;
                var mx = Unit(Hash(seed, i, 47, 12)) * 2 - 1;
                var my = Unit(Hash(seed, i, 53, 19)) * 2 - 1;
                var mz = Unit(Hash(seed, i, 61, 23)) * 2 - 1;
                var motionLength = Math.Sqrt(mx * mx + my * my + mz * mz);
                var speed = .035 + Unit(Hash(seed, i, 71, 29)) * .12;
                plates[i] = new Plate { x = Math.Cos(a) * r, y = y, z = Math.Sin(a) * r,
                    altitude = Unit(Hash(seed, i, 31, 91)) * 2 - 1,
                    wx = mx / motionLength * speed, wy = my / motionLength * speed, wz = mz / motionLength * speed };
            }
        }

        public float Elevation(float x, float y, float z)
        {
            Normalize(ref x, ref y, ref z);
            var elevation = SampleHeight(x, y, z);
            // Conservative physical envelope is also used by patch skirts and validation tools.
            return (float)(Math.Max(-600, Math.Min(1500, elevation)) * PlanetScale.ReliefScale(settings.radius));
        }

        protected abstract double SampleHeight(double x, double y, double z);

        // Exposed for diagnostics and engine-independent checks. The same relative motion
        // calculation feeds elevation generation, so boundaries are not merely visual lines.
        public TectonicBoundarySample PlateMotionAt(float x, float y, float z)
        {
            Normalize(ref x, ref y, ref z);
            return BoundaryMotion(x, y, z, out _, out _, out _, out _, out _);
        }

        protected double Fractal(double x, double y, double z)
        {
            var wx = x + Noise(x * 3, y * 3, z * 3, 71) * .06;
            var wy = y + Noise(x * 3, y * 3, z * 3, 73) * .06;
            var wz = z + Noise(x * 3, y * 3, z * 3, 79) * .06;
            var macro = Fbm(wx * 2, wy * 2, wz * 2, 4, .45) + CoverageBias;
            var inland = Smooth(-.04, .28, macro);
            var boundary = BoundaryMotion(x, y, z, out var first, out var second, out var gap, out var arcSide, out _);
            var relief = TectonicRelief(x, y, z, macro, boundary, first, second, gap, arcSide);
            return macro * 320 - 25 + inland * (Math.Pow(Ridge(wx * 4.5, wy * 4.5, wz * 4.5, 4), 1.5)
                * 140 * settings.mountainIntensity + Fbm(x * 24, y * 24, z * 24, 3, settings.roughness)
                    * 16 * settings.roughness * settings.roughness) + relief;
        }

        private double CoverageBias => (settings.continentalCoverage - .5) * 1.6;

        protected double Geographic(double x, double y, double z, out double belt)
        {
            // Smooth spherical Voronoi buoyancy produces regions and basins. The nearest two
            // continental plates create collision belts; ocean boundaries remain submerged.
            var motion = BoundaryMotion(x, y, z, out var plateA, out var plateB, out var gap, out var arcSide, out var buoyancy);
            var continent = buoyancy + CoverageBias;
            var coast = Noise(x * 8, y * 8, z * 8, 101) * .045
                + Noise(x * 16, y * 16, z * 16, 103) * .01;
            var elevation = (continent + coast) * 340 - 24;
            // Motion controls the boundary type. Convergence creates collision belts and
            // subduction arcs; divergence cuts rifts; shear creates long fault scarps.
            belt = motion.boundary * Smooth(-.22, .38, continent);
            var tectonicRelief = TectonicRelief(x, y, z, continent, motion, plateA, plateB, gap, arcSide);
            var inland = Smooth(-20, 100, elevation);
            var regionalUplift = belt * (75 + Noise(x * 6, y * 6, z * 6, 151) * 20) * settings.mountainIntensity;
            var valley = Math.Pow(1 - Math.Abs(Noise(x * 9, y * 9, z * 9, 179)), 4) * regionalUplift * .12;
            // Rounded slopes and valley incision approximate erosion, not a hydraulic simulation.
            return elevation + inland * (regionalUplift - valley + Noise(x * 16, y * 16, z * 16, 181)
                * 6 * settings.roughness * settings.roughness) + tectonicRelief;
        }

        private TectonicBoundarySample BoundaryMotion(double x, double y, double z, out Plate firstPlate,
            out Plate secondPlate, out double signedGap, out double arcSide, out double buoyancy)
        {
            var best = -2d; var next = -2d; var firstIndex = 0; var secondIndex = 1;
            var weighted = 0d; var totalWeight = 0d;
            for (var i = 0; i < plates.Length; i++)
            {
                var p = plates[i]; var dot = x * p.x + y * p.y + z * p.z;
                var weight = Math.Exp((dot - 1) * 13);
                weighted += p.altitude * weight; totalWeight += weight;
                if (dot > best) { next = best; secondIndex = firstIndex; best = dot; firstIndex = i; }
                else if (dot > next) { next = dot; secondIndex = i; }
            }
            buoyancy = weighted / totalWeight;
            firstPlate = plates[firstIndex]; secondPlate = plates[secondIndex];
            var idOrientation = firstIndex < secondIndex ? 1d : -1d;
            signedGap = (best - next) * idOrientation;
            arcSide = (firstPlate.altitude < secondPlate.altitude ? -1d : 1d) * idOrientation;
            var nx = firstPlate.x - secondPlate.x; var ny = firstPlate.y - secondPlate.y; var nz = firstPlate.z - secondPlate.z;
            var projection = nx * x + ny * y + nz * z;
            nx -= projection * x; ny -= projection * y; nz -= projection * z;
            var normalLength = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx /= normalLength; ny /= normalLength; nz /= normalLength;

            // Angular velocity crossed with the surface direction gives each plate's local
            // tangent velocity. Project the relative velocity into normal and shear motion.
            var ax = firstPlate.wy * z - firstPlate.wz * y;
            var ay = firstPlate.wz * x - firstPlate.wx * z;
            var az = firstPlate.wx * y - firstPlate.wy * x;
            var bx = secondPlate.wy * z - secondPlate.wz * y;
            var by = secondPlate.wz * x - secondPlate.wx * z;
            var bz = secondPlate.wx * y - secondPlate.wy * x;
            var vx = ax - bx; var vy = ay - by; var vz = az - bz;
            var normalSpeed = vx * nx + vy * ny + vz * nz;
            var totalSpeed = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            var shearSpeed = Math.Sqrt(Math.Max(0, totalSpeed * totalSpeed - normalSpeed * normalSpeed));
            var boundary = Math.Exp(-Math.Pow(signedGap * 14, 2));
            var convergence = Smooth(.012, .095, -normalSpeed);
            var divergence = Smooth(.012, .095, normalSpeed);
            var transform = Smooth(.025, .11, shearSpeed) * (1 - Smooth(.045, .16, Math.Abs(normalSpeed)));
            return new TectonicBoundarySample { boundary = (float)boundary,
                convergence = (float)(boundary * convergence), divergence = (float)(boundary * divergence),
                transform = (float)(boundary * transform) };
        }

        private double TectonicRelief(double x, double y, double z, double landField, TectonicBoundarySample motion,
            Plate firstPlate, Plate secondPlate, double signedGap, double arcSide)
        {
            var land = Smooth(-.18, .38, landField);
            var continentalA = Smooth(-.12, .42, firstPlate.altitude);
            var continentalB = Smooth(-.12, .42, secondPlate.altitude);
            var continentalCollision = continentalA * continentalB;
            var oceanContinental = continentalA * (1 - continentalB) + continentalB * (1 - continentalA);
            var convergent = motion.convergence;
            var divergent = motion.divergence;
            var transform = motion.transform;

            var collisionMountains = convergent * continentalCollision * land
                * (210 + Noise(x * 8, y * 8, z * 8, 233) * 85);
            var subduction = convergent * oceanContinental;
            var arcCenter = arcSide * .14;
            var volcanicArc = subduction * land * Math.Exp(-Math.Pow((signedGap - arcCenter) / .105, 2))
                * (145 + Noise(x * 11, y * 11, z * 11, 239) * 55);
            var trench = -subduction * Math.Exp(-Math.Pow(signedGap / .075, 2)) * 165;
            var rift = -divergent * land * Math.Exp(-Math.Pow(signedGap / .09, 2)) * 170;
            var riftShoulders = divergent * land * Math.Exp(-Math.Pow((Math.Abs(signedGap) - .2) / .12, 2)) * 72;
            var faultScarps = transform * land * Noise(x * 18, y * 18, z * 18, 251) * 95;
            return (collisionMountains + volcanicArc + trench + rift + riftShoulders + faultScarps)
                * settings.mountainIntensity;
        }

        public BiomeSample Climate(float x, float y, float z, float elevationAboveSea, float slope)
        {
            Normalize(ref x, ref y, ref z);
            var regional = Noise(x * 4.5, y * 4.5, z * 4.5, 701);
            var climateRadius = PlanetScale.ReliefScale(settings.radius) * 6000d;
            var temp = Clamp(1 - Math.Abs(y) * 1.12 - Math.Max(0, elevationAboveSea) / (climateRadius * .18)
                + (settings.temperature - .5) * 1.2 + regional * .12);
            var wet = Clamp(.5 + Noise(x * 7, y * 7, z * 7, 709) * .48 + (settings.moisture - .5) * 1.15
                + Math.Cos(y * Math.PI * 4) * .1 - Math.Max(0, elevationAboveSea) / climateRadius * 1.4);
            return Classify((float)temp, (float)wet, slope, settings);
        }

        public float Moisture(float x, float y, float z, float aboveSea) => Climate(x, y, z, aboveSea, 0).moisture;

        public static BiomeSample Classify(float temperature, float moisture, float slope, TerrainSettings s)
        {
            var snow = 1 - Smooth(s.snowTemperature - .08, s.snowTemperature + .08, temperature);
            var rock = Smooth(.22, .65, slope) * (1 - snow * .8);
            var tundra = (1 - Smooth(s.snowTemperature + .06, s.snowTemperature + .26, temperature)) * (1 - snow);
            var forest = Smooth(.72 - s.forestCoverage * .65, .92 - s.forestCoverage * .55, moisture)
                * Smooth(.25, .45, temperature);
            var desert = (1 - Smooth(.06 + s.desertCoverage * .4, .26 + s.desertCoverage * .5, moisture))
                * Smooth(.45, .7, temperature);
            var available = (1 - snow) * (1 - rock) * (1 - tundra);
            var f = available * forest; var d = available * desert * (1 - forest);
            var t = (1 - snow) * (1 - rock) * tundra;
            var r = (1 - snow) * rock;
            return new BiomeSample { temperature = temperature, moisture = moisture, snow = (float)snow,
                rock = (float)r, tundra = (float)t, forest = (float)f, desert = (float)d,
                grass = (float)Math.Max(0, 1 - snow - r - t - f - d) };
        }

        protected double Fbm(double x, double y, double z, int octaves, double roughness)
        {
            double sum = 0, weight = 1, total = 0; var persistence = .3 + roughness * .35;
            for (var i = 0; i < octaves; i++)
            { sum += Noise(x, y, z, 211 + i * 17) * weight; total += weight; weight *= persistence; x *= 2.03; y *= 2.03; z *= 2.03; }
            return sum / total;
        }

        protected double Ridge(double x, double y, double z, int octaves)
        {
            double sum = 0, total = 0, weight = 1, feedback = 1;
            for (var i = 0; i < octaves; i++)
            {
                var ridge = 1 - Math.Abs(Noise(x, y, z, 307 + i * 19)); ridge *= ridge;
                sum += ridge * weight * feedback; total += weight; feedback = Math.Min(1, ridge * 1.5);
                weight *= .5; x *= 2.07; y *= 2.07; z *= 2.07;
            }
            return sum / total;
        }

        // Seeded improved gradient noise: integer hashing, quintic interpolation, no lookup
        // textures, package dependency, global RNG state, or face-dependent coordinates.
        protected double Noise(double x, double y, double z, int salt)
        {
            var ix = (int)Math.Floor(x); var iy = (int)Math.Floor(y); var iz = (int)Math.Floor(z);
            x -= ix; y -= iy; z -= iz;
            var u = Fade(x); var v = Fade(y); var w = Fade(z);
            return Lerp(Lerp(Lerp(Grad(Hash(seed ^ salt, ix, iy, iz), x, y, z),
                Grad(Hash(seed ^ salt, ix + 1, iy, iz), x - 1, y, z), u),
                Lerp(Grad(Hash(seed ^ salt, ix, iy + 1, iz), x, y - 1, z),
                Grad(Hash(seed ^ salt, ix + 1, iy + 1, iz), x - 1, y - 1, z), u), v),
                Lerp(Lerp(Grad(Hash(seed ^ salt, ix, iy, iz + 1), x, y, z - 1),
                Grad(Hash(seed ^ salt, ix + 1, iy, iz + 1), x - 1, y, z - 1), u),
                Lerp(Grad(Hash(seed ^ salt, ix, iy + 1, iz + 1), x, y - 1, z - 1),
                Grad(Hash(seed ^ salt, ix + 1, iy + 1, iz + 1), x - 1, y - 1, z - 1), u), v), w);
        }
        private static double Grad(uint hash, double x, double y, double z)
        { var h = hash & 15; var u = h < 8 ? x : y; var v = h < 4 ? y : h == 12 || h == 14 ? x : z; return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v); }
        private static uint Hash(int seed, int x, int y, int z)
        {
            unchecked { var h = (uint)seed ^ (uint)x * 374761393u ^ (uint)y * 668265263u ^ (uint)z * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u; return h ^ (h >> 16); }
        }
        private static double Unit(uint hash) => hash / (double)uint.MaxValue;
        private static double Fade(double x) => x * x * x * (x * (x * 6 - 15) + 10);
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
        private static double Clamp(double x) => Math.Max(0, Math.Min(1, x));
        protected static double Smooth(double a, double b, double x) { x = Clamp((x - a) / (b - a)); return x * x * (3 - 2 * x); }
        private static void Normalize(ref float x, ref float y, ref float z)
        {
            var length = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
            if (length < 1e-12 || double.IsNaN(length) || double.IsInfinity(length)) throw new ArgumentException("A finite surface direction is required.");
            x = (float)(x / length); y = (float)(y / length); z = (float)(z / length);
        }
    }

    internal sealed class FractalTerrainSampler : TerrainSampler
    {
        internal FractalTerrainSampler(int seed, TerrainSettings settings) : base(seed, settings) { }
        protected override double SampleHeight(double x, double y, double z) => Fractal(x, y, z);
    }

    internal sealed class GeographicTerrainSampler : TerrainSampler
    {
        internal GeographicTerrainSampler(int seed, TerrainSettings settings) : base(seed, settings) { }
        protected override double SampleHeight(double x, double y, double z) => Geographic(x, y, z, out _);
    }

    internal sealed class HybridTerrainSampler : TerrainSampler
    {
        internal HybridTerrainSampler(int seed, TerrainSettings settings) : base(seed, settings) { }
        protected override double SampleHeight(double x, double y, double z)
        {
            var elevation = Geographic(x, y, z, out var belt);
            var inland = Smooth(-15, 120, elevation);
            var ridge = Ridge(x * 10, y * 10, z * 10, 3);
            var drainage = Math.Pow(1 - Math.Abs(Noise(x * 7, y * 7, z * 7, 411)), 4);
            return elevation + inland * ((ridge - .32) * 28 * belt * settings.mountainIntensity
                + Fbm(x * 24, y * 24, z * 24, 3, settings.roughness) * 5 * settings.roughness * settings.roughness
                - drainage * 18 * Smooth(80, 300, elevation));
        }
    }
}
