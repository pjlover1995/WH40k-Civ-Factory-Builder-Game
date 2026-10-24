using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using WH30K.Gameplay.Construction;
using WH30K.Sim;
using WH30K.Sim.Events;
using WH30K.Sim.Environment;

internal static class Program
{
    private static int checks;
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { IncludeFields = true };
    private static readonly HashSet<string> Ids = new HashSet<string> { "Outpost", "Factory", "HabBlock", "Utility" };
    private static readonly List<BuildingEconomy> Core = new List<BuildingEconomy>
    {
        new BuildingEconomy { capacity = 320, jobs = 40, outputPerWorker = 0.15f, upkeep = 8 },
        new BuildingEconomy { capacity = 120, jobs = 200, outputPerWorker = 1.35f, upkeep = 32 },
        new BuildingEconomy { capacity = 90, jobs = 80, outputPerWorker = 0.55f, upkeep = 14 }
    };

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        checks++;
        Console.WriteLine("PASS: " + name);
    }

    private static ColonyTick Tick(List<BuildingEconomy> buildings, float population = 160f,
        float habitability = 1f, float seconds = 1f)
        => ColonyRules.CalculateTick(buildings, population, 0.05f, 0.62f, 1f, 1f, habitability, seconds);

    private static int Main(string[] args)
    {
        try
        {
            var money = 500f;
            var stock=new float[7];stock[0]=2;
            Check(!IndustryRules.Process("Smelter",stock)&&stock[0]==2,"smelting waits for fuel without consuming ore");
            stock[3]=1;
            Check(IndustryRules.Process("Smelter",stock)&&stock[0]==0&&stock[3]==0&&stock[5]==1,"smelting consumes exact ore and biomass quantities");
            stock[1]=2;stock[3]=1;
            Check(IndustryRules.Process("Smelter",stock)&&stock[1]==0&&stock[5]==2,"copper provides an alternative smelting input");
            stock[2]=1;stock[4]=.5f;
            Check(IndustryRules.Process("Works",stock)&&stock[5]==0&&stock[2]==0&&stock[4]==0&&stock[6]==1,"fabrication consumes metal, stone and crystal atomically");
            stock[0]=2;stock[3]=1;stock[5]=120;
            Check(!IndustryRules.Process("Smelter",stock)&&stock[0]==2,"full output storage blocks recipes without losing inputs");
            stock[4]=float.NaN;
            Check(!IndustryRules.ValidStock(stock),"invalid material quantities are rejected");
            var logistics=new IndustrySnapshot{facilities=new[]{new FacilityStock(),new FacilityStock()},deposits=new[]{new DepositStock{id=0,remaining=1999}},
                cargo=new[]{new CargoStock{source=0,target=1,kind=0,amount=3,progress=.5f}}};
            Check(IndustryRules.Valid(logistics,2),"valid industrial saves retain in-flight cargo and depleted deposits");
            logistics.facilities[1].quantities[0]=119;
            Check(!IndustryRules.Valid(logistics,2),"incoming cargo reserves destination storage capacity");
            logistics.facilities[1].quantities[0]=0;logistics.cargo[0].target=3;
            Check(!IndustryRules.Valid(logistics,2),"invalid transport endpoints are rejected before loading");
            Check(ConstructionSave.ValidDirection(1,0,0)&&!ConstructionSave.ValidDirection(float.NaN,0,0)&&!ConstructionSave.ValidDirection(0,0,0),"free placement requires a finite unit surface direction");
            var free=new[]{new PlacedStructureSnapshot{id="Outpost",freePlacement=true,nx=1},new PlacedStructureSnapshot{id="Outpost",freePlacement=true,nx=.99995f,ny=.01f}};
            Check(ConstructionSave.Validate(free,96,Ids,out _),"separate free placements may share a legacy grid cell");
            free[1]=free[0];Check(!ConstructionSave.Validate(free,96,Ids,out _),"duplicate free surface positions are rejected");
            Check(IndustryRules.DayAngle(0)==IndustryRules.DayAngle(180)&&IndustryRules.DayAngle(90)==180,"lighting day cycles deterministically with simulated time");
            Check(ColonyRules.TrySpend(ref money, 320f) && money == 180f, "construction deducts exact cost");
            Check(!ColonyRules.TrySpend(ref money, 220f) && money == 180f, "unaffordable purchase changes nothing");
            Check(ColonyRules.TrySpend(ref money, 180f) && money == 0f, "exact balance purchase succeeds");
            Check(!ColonyRules.TrySpend(ref money, -1f) && !ColonyRules.TrySpend(ref money, float.NaN)
                && !ColonyRules.CanAfford(float.PositiveInfinity, 1f), "invalid amounts cannot create resources");

            var baseline = Tick(Core);
            var time = ColonyCalendar.Advance(new ClockSnapshot { speed = 1 }, 1800, false);
            Check(ColonyCalendar.Year(time.simulationSeconds) == 2 && ColonyCalendar.Month(time.simulationSeconds) == 1,
                "thirty real minutes at normal speed advance exactly one game year");
            time.paused = true;
            var pausedTime = ColonyCalendar.Advance(time, 30, false);
            Check(pausedTime.simulationSeconds == time.simulationSeconds && pausedTime.playedSeconds == 1830,
                "pause stops calendar progression while retaining real play time");
            time.paused = false; time.speed = 3;
            var faster = ColonyCalendar.Advance(time, 50, false);
            Check(ColonyCalendar.Month(faster.simulationSeconds) == 2 && faster.playedSeconds == 1850,
                "speed affects simulated months without multiplying real play time");
            Check(ColonyCalendar.Advance(time, 30, true).simulationSeconds == time.simulationSeconds,
                "pending decisions pause the simulation calendar");
            Check(!ColonyCalendar.Valid(new ClockSnapshot { simulationSeconds = double.NaN })
                && !ColonyCalendar.Valid(new ClockSnapshot { speed = 50 }), "corrupt clocks and unsupported speeds are rejected");
            var factoryFocus = BuildingOperationRules.Economy("Factory", Core[1], new BuildingOperationState { focus = 1 });
            Check(factoryFocus.outputPerWorker > Core[1].outputPerWorker && factoryFocus.upkeep > Core[1].upkeep,
                "factory surge trades higher upkeep for higher output");
            Check(BuildingOperationRules.Impact("Factory", new EnvironmentImpact { co2Delta = 2 }, new BuildingOperationState { focus = 2 }).co2Delta < 2,
                "clean production reduces ongoing factory emissions");
            Check(BuildingOperationRules.Impact("Utility", new EnvironmentImpact { co2Delta = -2 }, new BuildingOperationState { focus = 1 }).co2Delta < -2,
                "deep purification increases actual scrubber cleanup");
            var offline = BuildingOperationRules.Economy("HabBlock", Core[0], new BuildingOperationState { offline = true });
            Check(offline.capacity == 0 && offline.jobs == 0 && offline.upkeep == 0,
                "offline buildings stop capacity, jobs and upkeep");
            var upgraded = BuildingOperationRules.Economy("HabBlock", Core[0], new BuildingOperationState { level = 1 });
            Check(upgraded.capacity > Core[0].capacity && upgraded.upkeep > Core[0].upkeep
                && !BuildingOperationRules.Valid(new BuildingOperationState { level = 3 }),
                "upgrades increase capacity and reject invalid saved tiers");
            var expanded = new List<BuildingEconomy>(Core)
            {
                new BuildingEconomy { capacity = 160f, jobs = 60f, outputPerWorker = 1.4f, upkeep = 8f }
            };
            var expansion = Tick(expanded);
            Check(expansion.capacity == baseline.capacity + 160f && expansion.upkeep == baseline.upkeep + 8f,
                "outpost adds housing and upkeep");
            Check(expansion.production > baseline.production && expansion.workforce > baseline.workforce,
                "outpost changes actual production and workforce");
            var factories = new List<BuildingEconomy>(Core)
            {
                new BuildingEconomy { jobs = 120f, outputPerWorker = 2.2f, upkeep = 16f }
            };
            Check(Tick(factories).production - Tick(factories).upkeep > baseline.production - baseline.upkeep,
                "factory improves net production at starting population");
            var healthy = ColonyRules.Habitability(410, 20.8f, 4, 0);
            var polluted = ColonyRules.Habitability(1000, 19f, 30, 3);
            var damaged = Tick(Core, habitability: polluted);
            Check(healthy == 1f && polluted < healthy && polluted >= 0.25f, "pollution reduces bounded habitability");
            Check(damaged.production < baseline.production && damaged.workforce < baseline.workforce
                && damaged.population < baseline.population, "damage affects output, staffing and growth");
            Check(ColonyRules.Habitability(500, 20f, 5f, 0) > polluted, "cleaner conditions restore habitability");
            var emissions = new EnvironmentImpact { co2Delta = 1f, o2Delta = -0.01f, waterDelta = 0.1f };
            var cleaner = EnvironmentRules.ApplyPolicy(emissions, 0.5f);
            var hotter = EnvironmentRules.ApplyPolicy(emissions, 1.5f);
            Check(cleaner.co2Delta < emissions.co2Delta && hotter.waterDelta > emissions.waterDelta,
                "safety and overdrive change ongoing emissions");
            var scrubber = new EnvironmentImpact { co2Delta = -2.5f, o2Delta = 0.015f, waterDelta = -0.3f };
            Check(EnvironmentRules.ApplyPolicy(scrubber, 3f).co2Delta == scrubber.co2Delta,
                "industrial policy does not multiply scrubber benefits");
            var dirtyWorld = new EnvironmentSnapshot { Co2 = 900f, O2 = 19f, WaterPollution = 20f };
            var cleanedWorld = EnvironmentRules.ApplyIndustry(dirtyWorld, scrubber, 60f, 1f);
            Check(ColonyRules.Habitability(cleanedWorld.Co2, cleanedWorld.O2, cleanedWorld.WaterPollution, 0)
                > ColonyRules.Habitability(dirtyWorld.Co2, dirtyWorld.O2, dirtyWorld.WaterPollution, 0),
                "operating scrubbers restore conditions over time");
            var population = 160f;
            for (var i = 0; i < 10; i++) population = Tick(Core, population).population;
            Check(Math.Abs(population - Tick(Core, seconds: 10f).population) < 0.001f,
                "growth remains consistent when ticks are subdivided");

            var placements = new[] { new PlacedStructureSnapshot { id = "Outpost", face = 3, x = 30, y = 40 } };
            var encoded = JsonSerializer.Serialize(placements, JsonOptions);
            var restored = JsonSerializer.Deserialize<PlacedStructureSnapshot[]>(encoded, JsonOptions);
            Check(ConstructionSave.Validate(restored, 96, Ids, out _) && restored[0].x == 30
                && restored[0].id == "Outpost", "placement identity and cell survive JSON round trip");
            Check(!ConstructionSave.Validate(new[] { placements[0], placements[0] }, 96, Ids, out _),
                "duplicate saved cells are rejected");
            Check(!ConstructionSave.Validate(new[] { new PlacedStructureSnapshot { id = "Missing" } }, 96, Ids, out _)
                && !ConstructionSave.Validate(new[] { new PlacedStructureSnapshot { id = "Outpost", x = 96 } }, 96, Ids, out _)
                && !ConstructionSave.Validate(placements, 0, Ids, out _), "unknown buildings and invalid grids are rejected");
            Check(ConstructionSave.Validate(null, 96, Ids, out _), "legacy saves may omit construction");

            var schedule = new EventSchedule(1234, 1f);
            schedule.Advance(7f);
            var snapshot = JsonSerializer.Deserialize<EventSnapshot>(JsonSerializer.Serialize(schedule.CreateSnapshot(), JsonOptions), JsonOptions);
            var loaded = new EventSchedule(1234, 1f, snapshot);
            Check(snapshot.remainingSeconds == loaded.CreateSnapshot().remainingSeconds, "event time survives save/load");
            Check(schedule.Advance(180f) && loaded.Advance(180f) && !loaded.Advance(180f),
                "timer produces exactly one pending decision");
            var pending = new EventSchedule(1234, 1f, loaded.CreateSnapshot());
            Check(pending.AwaitingChoice && !pending.Advance(180f), "loading keeps an unanswered decision pending");
            schedule.Resolve(); loaded.Resolve(); pending.Resolve();
            Check(schedule.CreateSnapshot().remainingSeconds == loaded.CreateSnapshot().remainingSeconds
                && pending.CreateSnapshot().remainingSeconds == loaded.CreateSnapshot().remainingSeconds,
                "future event timing continues deterministically after load");
            Check(!loaded.Resolve(), "a decision cannot be applied twice");
            Check(!EventSchedule.IsValid(new EventSnapshot { initialized = true, randomState = 1, remainingSeconds = float.NaN }),
                "corrupt event state is rejected");

            // Ten minutes with repeated choices and a mid-session save/load.
            money = 500f;
            population = 160f;
            var decisions = 0;
            var world = new EnvironmentSnapshot { Co2 = 410f, O2 = 20.8f, WaterPollution = 4f };
            schedule = new EventSchedule(42, 1f);
            for (var second = 0; second < 600; second++)
            {
                var tick = Tick(expanded, population,
                    ColonyRules.Habitability(world.Co2, world.O2, world.WaterPollution, world.GlobalTemperatureOffset));
                population = tick.population;
                world = EnvironmentRules.ApplyIndustry(world, emissions.Scale(tick.utilisation), 1f, 1f);
                money = Math.Max(0f, money + tick.production - tick.upkeep);
                if (schedule.Advance(1f)) { decisions++; schedule.Resolve(); }
                if (second == 300) schedule = new EventSchedule(42, 1f, schedule.CreateSnapshot());
            }
            Check(ColonyRules.IsFinite(money) && money > 500f && population > 160f && decisions >= 10,
                "ten-minute simulation grows and continues decisions across a load");

            var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
            var prototype = File.ReadAllText(Path.Combine(root, "Assets/Scenes/Prototype/WH30K_VS01.unity"));
            Check(prototype.Contains("m_projectionMatrixMode: 1"), "prototype camera uses a computed perspective projection");
            var rotations = Regex.Matches(prototype,
                @"m_LocalRotation: \{x: ([^,]+), y: ([^,]+), z: ([^,]+), w: ([^}]+)\}");
            Check(rotations.Count > 0 && rotations.Cast<Match>().All(match =>
            {
                var lengthSquared = Enumerable.Range(1, 4).Select(index =>
                    double.Parse(match.Groups[index].Value, CultureInfo.InvariantCulture)).Sum(value => value * value);
                return Math.Abs(lengthSquared - 1d) < 0.00001d;
            }), "prototype scene rotations are normalized for Unity");
            var sources = Directory.GetFiles(Path.Combine(root, "Assets"), "*.cs", SearchOption.AllDirectories);
            foreach (var editor in new[] { false, true })
            {
                var options = new CSharpParseOptions(LanguageVersion.CSharp9,
                    preprocessorSymbols: editor ? new[] { "UNITY_EDITOR" } : Array.Empty<string>());
                var errors = sources.SelectMany(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), options, path)
                    .GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                foreach (var error in errors) Console.Error.WriteLine(error);
                Check(errors.Length == 0, $"all {sources.Length} Unity C# sources parse ({(editor ? "editor" : "player")})");
            }
            Console.WriteLine($"{checks} checks passed. Unity engine compilation/rendering requires Unity Editor.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
