using System;
using System.Text.Json;
using WH30K.Planet;

internal static class TerrainChecks
{
    public static void Run(Action<bool, string> check, JsonSerializerOptions jsonOptions)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var settings = new TerrainSettings();
        check(TerrainSettings.Valid(settings) && settings.radius == 3185500, "new terrain defaults to half Earth radius in metres");
        var halfEarth = TerrainSampler.Create(1234, settings);
        check(PlanetScale.ConstructionResolution(settings.radius) == 135915,
            "half Earth grid retains metre-scale construction cells without allocating a global grid");
        check(Math.Abs(PlanetScale.SurfaceDistance(1, 0, 0, 1, 10f / settings.radius, 0, settings.radius) - 10) < .001f
            && !WH30K.Sim.ExplorationRules.WithinSurvey(1, 0, 0, 1, 100f / settings.radius, 0, settings.radius, 50),
            "planetary distances distinguish ten-metre movements and reject out-of-range surveys");
        check(WH30K.Sim.EarthworksRules.ValidEdit(new WH30K.Sim.TerrainEditStamp {
            x = 1, radius = 24, value = settings.radius + 30, mode = (int)WH30K.Sim.EarthworkMode.Flatten }),
            "earthworks persist absolute grades on half Earth worlds");
        var halfJson = JsonSerializer.Serialize(settings, jsonOptions);
        var halfRestored = JsonSerializer.Deserialize<TerrainSettings>(halfJson, jsonOptions);
        check(TerrainSettings.Valid(halfRestored) && halfRestored.radius == 3185500
            && TerrainSampler.Create(1234, halfRestored).Elevation(.3f, -.7f, .9f) == halfEarth.Elevation(.3f, -.7f, .9f),
            "half Earth recipe and exact terrain samples survive JSON restoration");
        var recipes = new System.Collections.Generic.HashSet<string>(); var methods = new System.Collections.Generic.HashSet<TerrainMethod>();
        var coldest = 1f; var hottest = 0f; var leastLand = 1f; var mostLand = 0f;
        for (var profileSeed = 1; profileSeed <= 48; profileSeed++)
        {
            var recipe = TerrainSettings.ForNewWorld(profileSeed);
            var repeated = TerrainSettings.ForNewWorld(profileSeed);
            check(TerrainSettings.Valid(recipe) && recipe.method == repeated.method && recipe.temperature == repeated.temperature
                && recipe.moisture == repeated.moisture && recipe.mountainIntensity == repeated.mountainIntensity,
                $"new world profile {profileSeed} is valid and seed-stable");
            recipes.Add($"{recipe.method}/{recipe.continentalCoverage:F2}/{recipe.temperature:F2}/{recipe.moisture:F2}");
            methods.Add(recipe.method); coldest = Math.Min(coldest, recipe.temperature); hottest = Math.Max(hottest, recipe.temperature);
            leastLand = Math.Min(leastLand, recipe.continentalCoverage); mostLand = Math.Max(mostLand, recipe.continentalCoverage);
        }
        check(recipes.Count >= 6 && methods.Count == 3 && hottest - coldest > .4f && mostLand - leastLand > .2f,
            "seeded expedition profiles cover distinct climate, continentality and all three landform samplers");
        var movingPlates = (TerrainSampler)TerrainSampler.Create(282164432,
            new TerrainSettings { radius = 6000, method = TerrainMethod.Geographic });
        var maxConvergence = 0f; var maxDivergence = 0f; var maxTransform = 0f;
        for (var i = 0; i < 8192; i++)
        {
            Direction(i, 8192, out var x, out var y, out var z);
            var boundary = movingPlates.PlateMotionAt(x, y, z);
            maxConvergence = Math.Max(maxConvergence, boundary.convergence);
            maxDivergence = Math.Max(maxDivergence, boundary.divergence);
            maxTransform = Math.Max(maxTransform, boundary.transform);
        }
        check(maxConvergence > .5f && maxDivergence > .5f && maxTransform > .5f,
            "seeded plate motions produce convergent, divergent and transform boundary zones");
        check(movingPlates.PlateMotionAt(.3f, -.7f, .9f).convergence
                == ((TerrainSampler)TerrainSampler.Create(282164432, new TerrainSettings { radius = 6000, method = TerrainMethod.Geographic }))
                    .PlateMotionAt(.3f, -.7f, .9f).convergence,
            "plate motion and boundary classification reproduce exactly from the world seed");
        foreach (TerrainMethod method in Enum.GetValues(typeof(TerrainMethod)))
        foreach (var seed in new[] { 1234, 1376271481, 282164432 })
        {
            settings = new TerrainSettings { radius = 6000, method = method };
            ITerrainHeightSampler a = TerrainSampler.Create(seed, settings), b = TerrainSampler.Create(seed, settings);
            var c = TerrainSampler.Create(seed + 1, settings);
            var same = true; var finite = true; var varied = false; var continuous = true;
            var low = float.MaxValue; var high = float.MinValue; var land = 0;
            for (var i = 0; i < 2048; i++)
            {
                Direction(i, 2048, out var x, out var y, out var z);
                var h = a.Elevation(x, y, z);
                same &= h == b.Elevation(x, y, z);
                finite &= !float.IsNaN(h) && !float.IsInfinity(h) && h >= -600 && h <= 1500;
                varied |= Math.Abs(h - c.Elevation(x, y, z)) > 1;
                continuous &= Math.Abs(h - a.Elevation(x + .00001f, y, z)) < 8;
                low = Math.Min(low, h); high = Math.Max(high, h); if (h > settings.seaLevel) land++;
            }
            Console.WriteLine($"Terrain {method}, seed {seed}: {low:F1}..{high:F1} m; land {land / 20.48:F1}%");
            check(same && varied, $"{method}/{seed} exactly reproduces heights and varies with seed");
            check(finite && continuous && land > 30 && land < 2018 && high - low > 300,
                $"{method}/{seed} has bounded continuous relief, continents and ocean basins");
            // All 12 shared cube edges are reconstructed independently by the two incident faces.
            var seams = true;
            for (var axis = 0; axis < 3; axis++)
            foreach (var signA in new[] { -1f, 1f }) foreach (var signB in new[] { -1f, 1f })
            for (var i = 0; i <= 64; i++)
            {
                var t = i / 32f - 1;
                var one = FaceEdge(a, axis, signA, signB, t, false);
                var two = FaceEdge(a, axis, signA, signB, t, true);
                seams &= Math.Abs(one - two) < .0001;
                // Every other child vertex must land on the same sample as the coarser edge.
                if (i % 2 == 0) seams &= one == FaceEdge(b, axis, signA, signB, (i / 2) / 16f - 1, false);
            }
            check(seams, $"{method}/{seed} cube-face seams and nested LOD edge samples coincide");
        }

        var f = TerrainSampler.Create(1234, new TerrainSettings { radius = 6000, method = TerrainMethod.Fractal });
        var g = TerrainSampler.Create(1234, new TerrainSettings { radius = 6000, method = TerrainMethod.Geographic });
        var hSampler = TerrainSampler.Create(1234, new TerrainSettings { radius = 6000, method = TerrainMethod.Hybrid });
        var slopeValues = new float[512];
        var worstP95 = new float[3]; var worstP99 = new float[3];
        foreach (TerrainMethod method in Enum.GetValues(typeof(TerrainMethod))) foreach (var slopeSeed in new[] { 1234, 1376271481, 282164432 })
        {
            var smoothDefault = TerrainSampler.Create(slopeSeed, new TerrainSettings { radius = 6000, method = method });
            for (var i = 0; i < slopeValues.Length; i++)
            {
                Direction(i, slopeValues.Length, out var x, out var y, out var z);
                var axisX = Math.Abs(y) > .95f ? 1f : 0f; var axisY = Math.Abs(y) > .95f ? 0f : 1f;
                var tx = y * 0f - z * axisY; var ty = z * axisX - x * 0f; var tz = x * axisY - y * axisX;
                var length = Math.Sqrt(tx * tx + ty * ty + tz * tz); tx /= (float)length; ty /= (float)length; tz /= (float)length;
                var ahead = smoothDefault.Elevation(x + tx * .001f, y + ty * .001f, z + tz * .001f);
                var behind = smoothDefault.Elevation(x - tx * .001f, y - ty * .001f, z - tz * .001f);
                slopeValues[i] = Math.Abs(ahead - behind) / 12f;
            }
            Array.Sort(slopeValues);
            worstP95[(int)method] = Math.Max(worstP95[(int)method], slopeValues[486]);
            worstP99[(int)method] = Math.Max(worstP99[(int)method], slopeValues[506]);
        }
        for (var i = 0; i < worstP95.Length; i++)
            Console.WriteLine($"{(TerrainMethod)i} default local slope across three seeds: worst p95 {worstP95[i]:P0}, p99 {worstP99[i]:P0}");
        check(worstP95[0] < .35f && worstP95[1] < .35f && worstP95[2] < .35f
            && worstP99[0] < .7f && worstP99[1] < .7f && worstP99[2] < .7f,
            "default terrain avoids widespread sharp slopes while retaining localized mountain relief");
        double fg = 0, gh = 0;
        for (var i = 0; i < 512; i++)
        {
            Direction(i, 512, out var x, out var y, out var z);
            fg += Math.Abs(f.Elevation(x, y, z) - g.Elevation(x, y, z));
            gh += Math.Abs(g.Elevation(x, y, z) - hSampler.Elevation(x, y, z));
        }
        check(fg / 512 > 30 && gh / 512 > 1, "fractal, geographic and hybrid methods produce distinct landscapes");
        var sparse = TerrainSampler.Create(1234, new TerrainSettings { radius = 6000, continentalCoverage = .25f });
        var broad = TerrainSampler.Create(1234, new TerrainSettings { radius = 6000, continentalCoverage = .75f });
        var lessLand = 0; var moreLand = 0;
        for (var i = 0; i < 512; i++)
        {
            Direction(i, 512, out var x, out var y, out var z);
            if (sparse.Elevation(x, y, z) > -20) lessLand++;
            if (broad.Elevation(x, y, z) > -20) moreLand++;
        }
        check(moreLand > lessLand + 100, "continental coverage changes actual land area");

        settings = new TerrainSettings { radius = 6000 };
        var classes = new[]
        {
            TerrainSampler.Classify(.65f, .4f, 0, settings).Dominant,
            TerrainSampler.Classify(.65f, 1, 0, settings).Dominant,
            TerrainSampler.Classify(.9f, 0, 0, settings).Dominant,
            TerrainSampler.Classify(.65f, .5f, 1, settings).Dominant,
            TerrainSampler.Classify(.32f, .5f, 0, settings).Dominant,
            TerrainSampler.Classify(.05f, .5f, 0, settings).Dominant
        };
        check(classes[0] == ClimateBiome.TemperateGrassland && classes[1] == ClimateBiome.Forest
            && classes[2] == ClimateBiome.Desert && classes[3] == ClimateBiome.RockyHighlands
            && classes[4] == ClimateBiome.Tundra && classes[5] == ClimateBiome.SnowIce,
            "temperature, moisture and slope classify all six initial biomes");
        var climate = hSampler.Climate(1, 0, 0, 0, 0);
        var polar = hSampler.Climate(0, 1, 0, 0, 0);
        var alpine = hSampler.Climate(1, 0, 0, 900, 0);
        check(climate.temperature > polar.temperature && climate.temperature > alpine.temperature,
            "latitude and elevation cool the deterministic climate");
        var blendValid = true;
        for (var i = 0; i <= 1000; i++)
        {
            var b = TerrainSampler.Classify(i / 1000f, .5f, .35f, settings);
            var next = TerrainSampler.Classify((i + 1) / 1000f, .5f, .35f, settings);
            var sum = b.grass + b.forest + b.desert + b.rock + b.tundra + b.snow;
            blendValid &= Math.Abs(sum - 1) < .00001 && b.grass >= 0 && b.forest >= 0 && b.snow >= 0
                && Math.Abs(b.snow - next.snow) < .02 && Math.Abs(b.tundra - next.tundra) < .02;
        }
        check(blendValid, "biome weights are nonnegative, normalized and smoothly blended");
        settings = new TerrainSettings { method = TerrainMethod.Fractal, radius = 9000, seaLevel = -78,
            continentalCoverage = .65f, mountainIntensity = 1.7f, roughness = .83f,
            temperature = .62f, moisture = .34f, forestCoverage = .29f, desertCoverage = .62f, snowTemperature = .31f };
        var original = TerrainSampler.Create(1234, settings);
        var json = JsonSerializer.Serialize(settings, jsonOptions);
        var restored = JsonSerializer.Deserialize<TerrainSettings>(json, jsonOptions);
        var roundtrip = TerrainSampler.Create(1234, restored);
        check(TerrainSettings.Valid(restored) && JsonSerializer.Serialize(restored, jsonOptions) == json
            && roundtrip.Elevation(.3f, -.7f, .9f) == original.Elevation(.3f, -.7f, .9f),
            "terrain settings JSON round trip preserves exact height generation");
        var before = original.Elevation(1, 0, 0); settings.radius = 12000;
        check(original.Elevation(1, 0, 0) == before && TerrainSampler.Create(1234, settings).Elevation(1, 0, 0) != before,
            "staged editor settings cannot mutate an existing terrain sampler");
        var twice = TerrainSampler.Create(1234, new TerrainSettings { radius = 12000 });
        var scaling = true; var largeRelief = true;
        for (var i = 0; i < 128; i++)
        {
            Direction(i, 128, out var x, out var y, out var z);
            scaling &= Math.Abs(twice.Elevation(x, y, z) - hSampler.Elevation(x, y, z) * 2) < .0001;
            largeRelief &= Math.Abs(halfEarth.Elevation(x, y, z) - hSampler.Elevation(x, y, z) * 6) < .001f;
        }
        check(scaling, "historical metric relief and coastline mapping stay unchanged");
        check(largeRelief, "large planets retain kilometre-scale relief instead of scaling mountains with globe radius");
        settings.radius = float.NaN; check(!TerrainSettings.Valid(settings), "nonfinite terrain settings are rejected");
        settings = new TerrainSettings { method = (TerrainMethod)99 };
        check(!TerrainSettings.Valid(settings), "unknown generation methods are rejected");
        var rejected = false;
        try { hSampler.Elevation(0, 0, 0); } catch (ArgumentException) { rejected = true; }
        check(rejected, "invalid sampling directions cannot produce corrupt mesh vertices");
        // Warm the loop before measuring. This verifies the pure production sampler only.
        for (var i = 0; i < 100; i++) hSampler.Elevation(1, i * .01f, .5f);
        var allocations = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) hSampler.Elevation(1, i * .001f, .5f);
        allocations = GC.GetAllocatedBytesForCurrentThread() - allocations;
        check(allocations == 0, "height sampling allocates no managed objects after construction");
        Console.WriteLine($"Terrain numerical suite: {timer.Elapsed.TotalMilliseconds:F0} ms; sampling allocations {allocations} bytes. This is not a Unity frame-time benchmark.");
    }

    private static float FaceEdge(ITerrainHeightSampler sampler, int freeAxis, float a, float b, float t, bool secondFace)
    {
        // Normal + two face axes, independently reconstructed. The same cube edge must result.
        var first = secondFace ? b : a; var second = secondFace ? a : b;
        var x = freeAxis == 0 ? t : secondFace ? second : first;
        var y = freeAxis == 1 ? t : freeAxis == 0 ? (secondFace ? second : first) : (secondFace ? first : second);
        var z = freeAxis == 2 ? t : secondFace ? first : second;
        return sampler.Elevation(x, y, z);
    }
    private static void Direction(int i, int count, out float x, out float y, out float z)
    {
        y = (float)(1 - 2 * (i + .5) / count); var r = Math.Sqrt(1 - y * y); var a = i * 2.399963229728653;
        x = (float)(Math.Cos(a) * r); z = (float)(Math.Sin(a) * r);
    }

}
