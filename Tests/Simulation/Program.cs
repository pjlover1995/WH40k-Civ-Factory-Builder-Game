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

    private static float AssetFloat(string text,string field)
    {
        var match=Regex.Match(text,@"(?m)^\s+"+Regex.Escape(field)+@":\s*([-+\d.eE]+)\s*$");
        if(!match.Success)throw new Exception("Missing serialized asset field: "+field);
        return float.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);
    }

    private static int Main(string[] args)
    {
        try
        {
            var treeHash=TreeEcologyRules.StableHash(1234,2,17,29);
            Check(treeHash==TreeEcologyRules.StableHash(1234,2,17,29)
                &&treeHash!=TreeEcologyRules.StableHash(1235,2,17,29),
                "procedural tree identities are stable for a world-grid cell and vary by planet seed");
            Check(TreeEcologyRules.Maturity(1234,2,17,29,ColonyCalendar.SecondsPerYear*8)
                >TreeEcologyRules.Maturity(1234,2,17,29,0)
                &&TreeEcologyRules.Maturity(1234,2,17,29,ColonyCalendar.SecondsPerYear*100)==1,
                "trees grow with simulation time and eventually reach maturity");
            Check(TreeEcologyRules.BiomassYield(1)>TreeEcologyRules.BiomassYield(.25f),
                "mature trees provide more harvestable biomass than young trees");
            Check(TreeEcologyRules.CellResolution(6000)==1024 && TreeEcologyRules.CellResolution(12000)==1024
                && Math.Abs(WH30K.Planet.PlanetScale.DefaultRadius*2/TreeEcologyRules.CellResolution(WH30K.Planet.PlanetScale.DefaultRadius)-6000f*2/1024)<.001f,
                "continental forests retain metre-scale tree spacing and historical small-world cell identities");
            ShipChecks.Run(Check);
            FoundingChecks.Run(Check);
            DepositUsageChecks.Run(Check);
            FarmOwnershipChecks.Run(Check);
            FarmChecks.Run(Check);
            FarmLayoutChecks.Run(Check);
            OutputChecks.Run(Check);
            FacilityChecks.Run(Check);
            ManufactorumChecks.Run(Check);
            ResearchChecks.Run(Check);
            MiningChecks.Run(Check);
            Check(WaterRules.StartingReserve == 600f && WaterRules.Capacity == 5000f
                && WaterRules.Demand(160) > WaterRules.Demand(0),
                "colonies begin with finite water and demand rises with population");
            Check(WaterRules.PumpOutput("GroundwaterPump", 1, 1, 0, 1) > WaterRules.PumpOutput("DesalinationPump", 1, 1, 0, 1)
                && WaterRules.PumpOutput("GroundwaterPump", .5f, .5f, 0, 1) < WaterRules.PumpOutput("GroundwaterPump", 1, 1, 0, 1)
                && WaterRules.PumpOutput("Unknown", 1, 1, 0, 1) == 0,
                "pump yields distinguish fresh groundwater, desalination and operating efficiency");
            var money = 500f;
            var ore=MiningRules.Create(1234,10,MaterialKind.IronOre);
            var copperBody=MiningRules.Create(1234,10,MaterialKind.CopperOre);
            Check(MiningRules.Valid(ore) && ore.grade>=.5f && copperBody.grade<.02f && ore.majorRadius>ore.minorRadius,
                "geological profiles distinguish enriched iron lenses and low-grade disseminated copper");
            Check(JsonSerializer.Serialize(ore,JsonOptions)==JsonSerializer.Serialize(MiningRules.Create(1234,10,MaterialKind.IronOre),JsonOptions),
                "seeded ore geometry, grades and directional reserves are deterministic");
            Check(MiningRules.Remaining(ore)!=MiningRules.Remaining(MiningRules.Create(1234,11,MaterialKind.IronOre)),
                "ore bodies have varied finite reserves instead of a fixed resource counter");
            var discoveriesA=ExplorationRules.Generate(1234);var discoveriesB=ExplorationRules.Generate(1234);
            Check(ExplorationRules.Valid(discoveriesA)&&JsonSerializer.Serialize(discoveriesA,JsonOptions)==JsonSerializer.Serialize(discoveriesB,JsonOptions),
                "planetary points of interest have valid, deterministic layouts for identical seeds");
            Check(JsonSerializer.Serialize(discoveriesA,JsonOptions)!=JsonSerializer.Serialize(ExplorationRules.Generate(9876),JsonOptions),
                "different planet seeds create different exploration opportunities");
            Check(ExplorationRules.WithinSurvey(1,0,0,(float)Math.Cos(.1),(float)Math.Sin(.1),0,12000,1800)
                &&!ExplorationRules.WithinSurvey(1,0,0,(float)Math.Cos(.2),(float)Math.Sin(.2),0,12000,1800),
                "survey coverage reveals only locations inside the configured spherical radius");
            var unresolved=discoveriesA[1];unresolved.discovered=true;
            Check(ExplorationRules.TryResolve(unresolved,1234,true,out var resolvedOutcomeA,out var outcomeA)
                &&ExplorationRules.TryResolve(unresolved,1234,true,out var resolvedOutcomeB,out var outcomeB)
                &&resolvedOutcomeA.resolved==resolvedOutcomeB.resolved&&outcomeA.reward==outcomeB.reward&&outcomeA.hazard==outcomeB.hazard,
                "investigation choices produce the same risk and reward for the same seeded site");
            Check(!ExplorationRules.TryResolve(resolvedOutcomeA,1234,true,out _,out _),
                "a resolved site cannot produce a second investigation reward");
            Check(Enum.GetValues<DiscoveryKind>().All(kind=>ExplorationRules.ValidProfile(ExplorationRules.DefaultProfile(kind))),
                "each discovery type has a valid content profile");
            var hazardSite=unresolved;hazardSite.kind=(int)DiscoveryKind.HazardousRegion;
            Check(ExplorationRules.TryResolve(hazardSite,1234,false,out var contained,out var cleanup)
                &&cleanup.reward==0&&!cleanup.hazard&&cleanup.impact.co2Delta<0&&cleanup.impact.waterDelta<0,
                "cautious vent containment improves planetary conditions instead of awarding salvage credits");
            var rareSite=unresolved;rareSite.kind=(int)DiscoveryKind.ArchaeotechCache;
            ExplorationRules.TryResolve(unresolved,1234,false,out _,out var ruinReward);
            Check(ExplorationRules.TryResolve(rareSite,1234,false,out _,out var rareReward)&&rareReward.reward>ruinReward.reward*3,
                "rare archaeotech caches offer more valuable recoveries than common ruins");
            var guaranteedIncident=ExplorationRules.DefaultProfile(DiscoveryKind.AncientRuins);guaranteedIncident.hazardChance=1;
            Check(ExplorationRules.TryResolve(unresolved,1234,true,guaranteedIncident,out var incident,out var incidentOutcome)
                &&incident.outcomeRecorded&&incident.outcomeHazard&&incident.rewardReceived==0&&incidentOutcome.impact.waterDelta>0,
                "a configured salvage incident records damage and cannot also award the success reward");
            var recordedIncident=JsonSerializer.Deserialize<DiscoveryRecord>(JsonSerializer.Serialize(incident,JsonOptions),JsonOptions);
            Check(recordedIncident.outcomeRecorded&&recordedIncident.rapidChoice&&recordedIncident.outcomeImpact.waterDelta==incident.outcomeImpact.waterDelta
                &&!ExplorationRules.TryResolve(recordedIncident,1234,false,out _,out _),
                "the chosen approach, incident and environmental outcome survive JSON and remain reward-locked");
            guaranteedIncident.hazardChance=float.NaN;
            Check(!ExplorationRules.TryResolve(unresolved,1234,true,guaranteedIncident,out var untouched,out _)&&!untouched.resolved,
                "invalid discovery configuration leaves the unresolved site untouched");
            var cellDirections=new List<(float x,float y,float z)>();
            for(var cell=0;cell<ExplorationRules.SurveyCellCount;cell++)
            {ExplorationRules.SurveyCellDirection(cell,out var x,out var y,out var z);cellDirections.Add((x,y,z));}
            Check(cellDirections.All(n=>Math.Abs(n.x*n.x+n.y*n.y+n.z*n.z-1)<.00001f)&&cellDirections.Distinct().Count()==ExplorationRules.SurveyCellCount,
                "the regional survey map has distinct normalized sample directions across the sphere");
            var explorationSave=new IndustrySnapshot{facilities=new[]{new FacilityStock()},deposits=new[]{new DepositStock{id=0,kind=(int)MaterialKind.Biomass,remaining=650,discovered=true}},cargo=Array.Empty<CargoStock>(),
                geologyVersion=3,materialVersion=1,explorationVersion=ExplorationRules.LayoutVersion,discoveries=discoveriesA,
                producedTotals=new float[IndustryRules.MaterialCount],consumedTotals=new float[IndustryRules.MaterialCount],
                productionRates=new float[IndustryRules.MaterialCount],consumptionRates=new float[IndustryRules.MaterialCount],
                exploredCells=new bool[ExplorationRules.SurveyCellCount]};
            explorationSave.exploredCells[5]=true;explorationSave.exploredCells[99]=true;
            explorationSave.discoveries[0].discovered=true;explorationSave.discoveries[0].resolved=true;
            var explorationReload=JsonSerializer.Deserialize<IndustrySnapshot>(JsonSerializer.Serialize(explorationSave,JsonOptions),JsonOptions);
            Check(IndustryRules.Valid(explorationReload,1)&&explorationReload.deposits[0].discovered&&explorationReload.deposits[0].remaining==650
                &&explorationReload.discoveries[0].discovered&&explorationReload.discoveries[0].resolved,
                "revealed deposits, remaining reserves and resolved discoveries persist in validated industry saves");
            Check(explorationReload.exploredCells[5]&&explorationReload.exploredCells[99]&&explorationReload.exploredCells.Count(c=>c)==2,
                "charted regional cells survive saves independently of the current outpost registry");
            explorationReload.discoveries[0].discovered=false;
            Check(!IndustryRules.Valid(explorationReload,1),"a resolved discovery cannot be hidden or rewarded twice after loading");
            explorationReload=JsonSerializer.Deserialize<IndustrySnapshot>(JsonSerializer.Serialize(explorationSave,JsonOptions),JsonOptions);
            explorationReload.discoveries=null;
            Check(!IndustryRules.Valid(explorationReload,1),"schema-8 saves cannot omit the generated discovery layout");
            explorationReload=JsonSerializer.Deserialize<IndustrySnapshot>(JsonSerializer.Serialize(explorationSave,JsonOptions),JsonOptions);
            explorationReload.exploredCells=new bool[3];
            Check(!IndustryRules.Valid(explorationReload,1),"invalid saved survey-map dimensions are rejected");
            explorationReload.exploredCells=null;
            Check(IndustryRules.Valid(explorationReload,1),"earlier schema-8 saves may omit regional coverage and recorded expedition outcomes");
            Check(MiningRules.RichnessMultiplier(MaterialKind.IronOre,.65f)>MiningRules.RichnessMultiplier(MaterialKind.IronOre,.50f)
                &&MiningRules.RichnessMultiplier(MaterialKind.CopperOre,.012f)>MiningRules.RichnessMultiplier(MaterialKind.CopperOre,.003f),
                "higher-grade deposits increase extraction throughput within each ore type");
            ore.layoutVersion=0; // Directional limits below exercise the retained legacy rules.
            var initialOre=MiningRules.Remaining(ore);var originalRadius=MiningRules.Radius(ore,0);
            MiningRules.Extract(ore,0,ore.capacity[0]*.2f);var shallowDepth=MiningRules.Depth(ore,0);var shallowRadius=MiningRules.Radius(ore,0);
            MiningRules.Extract(ore,0,ore.capacity[0]*.4f);
            Check(MiningRules.Depth(ore,0)>shallowDepth && MiningRules.Radius(ore,0)>shallowRadius && shallowRadius>originalRadius,
                "working an ore body widens the pit and descends to deeper benches");
            var physicalRemaining=MiningRules.Remaining(ore);var recoverable=MiningRules.Remaining(ore,true);
            MiningRules.Limit(ore,1);
            Check(MiningRules.Remaining(ore)==physicalRemaining && MiningRules.Remaining(ore,true)<recoverable && MiningRules.NextSector(ore)!=1
                && MiningRules.Extract(ore,1,1)==0,"directional limits retain ore in ground and exclude it from mine extraction");
            Check(MiningRules.Threatens(ore,0,40,ore.bearing,18) && !MiningRules.Threatens(ore,0,1000,ore.bearing,18)
                && !MiningRules.Threatens(ore,0,40,ore.bearing+180,1),"expansion tests use the working direction, facility footprint and safety margin");
            Check(MiningRules.Threatens(ore,0,1,ore.bearing+180,20),"facilities over the pit centre are protected regardless of sector bearing");
            var migrated=MiningRules.Create(1234,10,MaterialKind.IronOre,1578.5f);
            Check(Math.Abs(MiningRules.Remaining(migrated)-1578.5f)<.001f,"legacy deposit migration preserves remaining ore rather than refilling it");
            for(var s=0;s<8;s++)MiningRules.Limit(ore,s);
            Check(MiningRules.NextSector(ore)==-1 && MiningRules.Remaining(ore,true)==0 && MiningRules.Remaining(ore)>0,
                "a fully constrained mine stops while retaining inaccessible ore");
            ore.blocked=0;var extracted=initialOre-physicalRemaining;
            for(var s=0;s<8;s++)extracted+=MiningRules.Extract(ore,s,float.MaxValue);
            Check(MiningRules.NextSector(ore)==-1 && MiningRules.Remaining(ore)<.001f && Math.Abs(extracted-initialOre)<.001f
                && MiningRules.Extract(ore,0,1)==0,"finite veins exhaust exactly without creating ore or extracting negative residues");
            Check(MiningRules.Extract(ore,0,float.NaN)==0,"invalid extraction requests cannot corrupt ore state");
            var miningSave=new IndustrySnapshot{facilities=new[]{new FacilityStock(),new FacilityStock()},cargo=Array.Empty<CargoStock>(),geologyVersion=1,
                deposits=new[]{new DepositStock{id=0,remaining=MiningRules.Remaining(migrated),excavation=migrated}},
                conflict=new MiningConflict{active=true,deposit=0,sector=2,mine=0,building=1}};
            Check(IndustryRules.Valid(miningSave,2),"a pending expansion decision and partial benches form a valid industrial save");
            var miningReload=JsonSerializer.Deserialize<IndustrySnapshot>(JsonSerializer.Serialize(miningSave,JsonOptions),JsonOptions);
            Check(miningReload.conflict.active && miningReload.conflict.building==1 && miningReload.deposits[0].excavation.removed[0]==migrated.removed[0],
                "JSON preserves directional excavation and the exact unanswered expansion decision");
            miningReload.conflict.building=20;Check(!IndustryRules.Valid(miningReload,2),"invalid mining decision endpoints are rejected");
            migrated.removed[0]=migrated.capacity[0]+1;Check(!MiningRules.Valid(migrated),"excavated ore cannot exceed the geological reserve");
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
            var industrial=new float[IndustryRules.MaterialCount];industrial[0]=2;
            Check(!IndustryRules.Process("Refinery",industrial)&&industrial[0]==2&&industrial[7]==0,
                "refining waits for promethium without consuming iron");
            industrial[0]=4;industrial[7]=1;
            Check(IndustryRules.Process("Refinery",industrial)&&industrial[0]==2&&industrial[7]==0&&industrial[8]==1,
                "refinery converts iron and promethium into plasteel");
            Check(IndustryRules.Process("Manufactorum",industrial)&&industrial[8]==0&&industrial[6]==1,
                "manufactorum converts plasteel into components");
            industrial[7]=.2f;
            Check(IndustryRules.Process("PromethiumGenerator",industrial)&&Math.Abs(industrial[7])<.0001f,
                "generator consumes promethium instead of storing energy as a material");
            var legacyStock=Enumerable.Range(0,IndustryRules.LegacyMaterialCount).Select(i=>(float)i).ToArray();
            var upgradedStock=IndustryRules.UpgradeStock(legacyStock);
            Check(upgradedStock.Length==IndustryRules.MaterialCount&&upgradedStock[6]==6&&upgradedStock[7]==0&&upgradedStock[8]==0,
                "version-6 inventories gain new material slots without changing saved quantities");
            Check(IndustryRules.PowerFraction(10,20)==.5f&&IndustryRules.PowerFraction(30,20)==1&&IndustryRules.PowerFraction(0,20)==0,
                "power shortages allocate partial production without creating negative energy stock");
            Check(IndustryRules.FuelLimitedPower(28,.168f,1,1,10)<28
                &&IndustryRules.FuelLimitedPower(28,.168f,100,1,10)==28,
                "generator capacity is bounded by fuel available throughout each deterministic tick");
            var remainingLoad = 10f;
            var firstFuel = IndustryRules.GeneratorFuelForLoad(28, .168f, .006f, 1, 1, ref remainingLoad);
            var secondFuel = IndustryRules.GeneratorFuelForLoad(28, .168f, 100, 1, 1, ref remainingLoad);
            Check(Math.Abs(firstFuel - .006f) < .000001f && Math.Abs(secondFuel - .054f) < .000001f && remainingLoad == 0,
                "a fuel-starved first generator leaves its unmet load for a supplied second generator");
            remainingLoad = 60;
            firstFuel = IndustryRules.GeneratorFuelForLoad(28, .168f, 100, .5f, 2, ref remainingLoad);
            Check(Math.Abs(firstFuel - .168f) < .000001f && remainingLoad == 46,
                "generator fuel and unmet load respect staffing and simulation duration together");
            remainingLoad = 10;
            Check(IndustryRules.GeneratorFuelForLoad(28, .168f, 0, 1, 1, ref remainingLoad) == 0 && remainingLoad == 10
                && IndustryRules.GeneratorFuelForLoad(28, .168f, 100, 1, 0, ref remainingLoad) == 0 && remainingLoad == 10,
                "empty fuel and paused time cannot retire network demand");
            remainingLoad = 0;
            Check(IndustryRules.GeneratorFuelForLoad(28, .168f, 100, 1, 1, ref remainingLoad) == 0 && remainingLoad == 0,
                "an idle generator consumes no fuel when the reactor or another generator covers demand");
            remainingLoad = 10;
            firstFuel = IndustryRules.GeneratorFuelForLoad(28, .168f, .006f, 1, 1, ref remainingLoad);
            secondFuel = IndustryRules.GeneratorFuelForLoad(28, .168f, .012f, 1, 1, ref remainingLoad);
            Check(Math.Abs(firstFuel + secondFuel - .018f) < .000001f && Math.Abs(remainingLoad - 7) < .00001f,
                "a network-wide fuel shortage accounts for exactly the power its fuel can supply");
            Check(IndustryRules.ShareLogistics(3,3)&&!IndustryRules.ShareLogistics(3,4)&&!IndustryRules.ShareLogistics(-1,-1),
                "only facilities in the same valid logistics component share materials");
            Check(IndustryRules.Capacity(MaterialKind.IronOre,1200)==1320,
                "storage depots expand material capacity");
            var statisticsSave=new IndustrySnapshot{facilities=new[]{new FacilityStock()},deposits=Array.Empty<DepositStock>(),cargo=Array.Empty<CargoStock>(),
                materialVersion=1,geologyVersion=2,producedTotals=new float[IndustryRules.MaterialCount],consumedTotals=new float[IndustryRules.MaterialCount],
                productionRates=new float[IndustryRules.MaterialCount],consumptionRates=new float[IndustryRules.MaterialCount]};
            statisticsSave.producedTotals[8]=25;statisticsSave.consumedTotals[7]=10;statisticsSave.productionRates[8]=.5f;statisticsSave.consumptionRates[7]=.2f;
            var statisticsReload=JsonSerializer.Deserialize<IndustrySnapshot>(JsonSerializer.Serialize(statisticsSave,JsonOptions),JsonOptions);
            Check(IndustryRules.Valid(statisticsReload,1)&&statisticsReload.producedTotals[8]==25&&statisticsReload.consumptionRates[7]==.2f,
                "lifetime production totals and recent resource flow rates survive validated JSON saves");
            statisticsReload.productionRates[0]=float.NaN;
            Check(!IndustryRules.Valid(statisticsReload,1),"invalid saved production statistics are rejected");
            statisticsReload=JsonSerializer.Deserialize<IndustrySnapshot>(JsonSerializer.Serialize(statisticsSave,JsonOptions),JsonOptions);
            statisticsReload.facilities[0].quantities[8]=1500;
            Check(IndustryRules.Valid(statisticsReload,1),"expanded depot inventories remain valid in persisted industrial saves");
            statisticsReload.deposits=new[]{new DepositStock{id=0,kind=(int)MaterialKind.Components,remaining=1,excavation=MiningRules.Create(1,0,MaterialKind.Stone)}};
            Check(!IndustryRules.Valid(statisticsReload,1),"non-geological materials cannot be forged into resource deposits");
            var logistics=new IndustrySnapshot{facilities=new[]{new FacilityStock(),new FacilityStock()},deposits=new[]{new DepositStock{id=0,remaining=1999}},
                cargo=new[]{new CargoStock{source=0,target=1,kind=0,amount=3,progress=.5f}}};
            Check(IndustryRules.Valid(logistics,2),"valid industrial saves retain in-flight cargo and depleted deposits");
            logistics.deposits[0].remaining=2500;
            Check(!IndustryRules.Valid(logistics,2),"legacy deposits cannot exceed their original capacity during migration");
            logistics.deposits[0].remaining=1999;
            logistics.facilities[1].quantities[0]=119;
            Check(!IndustryRules.Valid(logistics,2),"incoming cargo reserves destination storage capacity");
            logistics.facilities[1].quantities[0]=0;logistics.cargo[0].target=3;
            Check(!IndustryRules.Valid(logistics,2),"invalid transport endpoints are rejected before loading");
            Check(ConstructionSave.ValidDirection(1,0,0)&&!ConstructionSave.ValidDirection(float.NaN,0,0)&&!ConstructionSave.ValidDirection(0,0,0),"free placement requires a finite unit surface direction");
            Check(!ConstructionSave.Validate(new[]{new PlacedStructureSnapshot{id="Outpost",freePlacement=true,nx=1,hasElevation=true,elevation=float.NaN}},96,Ids,out _),
                "invalid saved foundation elevations are rejected before loading");
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
            Check(AdaptiveBuildingRules.PlanName(0) == "Balanced district"
                && AdaptiveBuildingRules.PlanName(1) == "Vertical crown"
                && AdaptiveBuildingRules.PlanName(2) == "Radial campus"
                && AdaptiveBuildingRules.PlanName(5) == "Radial campus"
                && AdaptiveBuildingRules.Cost(new BuildingOperationState { growthPlan = 5 }) == 150
                && AdaptiveBuildingRules.Cost(new BuildingOperationState { growthPlan = 2 }) == 210,
                "new upgrades use symmetric rings while legacy directional plans and their saved cost remain readable");
            var adaptive = new BuildingOperationState();
            var adaptiveCost = AdaptiveBuildingRules.Cost(adaptive);
            Check(AdaptiveBuildingRules.Request(ref adaptive) && adaptive.improving && adaptive.level == 0,
                "player improvement marks a construction project without instant tier advancement");
            var debit = AdaptiveBuildingRules.Advance(ref adaptive, "HabBlock", 10, 1, adaptiveCost / 6, true, out var finished);
            Check(!finished && adaptive.improvementWork > 9 && Math.Abs(debit - adaptiveCost / 6) < .001f,
                "upgrade work spends only available credits and preserves partial construction progress");
            var savedAdaptive = JsonSerializer.Deserialize<BuildingOperationState>(JsonSerializer.Serialize(adaptive, JsonOptions), JsonOptions);
            Check(BuildingOperationRules.Valid(savedAdaptive) && savedAdaptive.improving
                && Math.Abs(savedAdaptive.improvementWork - adaptive.improvementWork) < .001f,
                "partly completed expansions and selected plans round-trip through save serialization");
            var stalledWork = adaptive.improvementWork;
            Check(AdaptiveBuildingRules.Advance(ref adaptive, "HabBlock", 10, 1, 0, true, out finished) == 0
                && adaptive.improvementWork == stalledWork && !finished,
                "an upgrade pauses cleanly when its credit stockpile is empty");
            var blockedWork = adaptive.improvementWork;
            Check(AdaptiveBuildingRules.Advance(ref adaptive, "HabBlock", 10, 1, 1000, false, out finished) == 0
                && adaptive.improvementWork == blockedWork && !finished,
                "a blocked expansion neither advances nor spends resources");
            var auto = new BuildingOperationState(); var milestone = AdaptiveBuildingRules.MilestoneDuration("HabBlock", auto);
            AdaptiveBuildingRules.Advance(ref auto, "HabBlock", milestone - 1, 1, 1000, true, out finished);
            Check(!auto.improving, "automatic housing upgrade waits for the complete occupancy milestone");
            AdaptiveBuildingRules.Advance(ref auto, "HabBlock", 1, 1, 1000, true, out finished);
            Check(auto.improving && !finished, "a sustained housing milestone queues gradual improvement work");
            auto.manualImprovements = true; auto.improving = false; auto.milestoneSeconds = 0;
            AdaptiveBuildingRules.Advance(ref auto, "HabBlock", milestone, 1, 1000, true, out finished);
            Check(!auto.improving, "manual improvement mode suppresses automatic milestone upgrades");
            auto.improving = true; auto.improvementWork = AdaptiveBuildingRules.Duration(auto) - 1;
            AdaptiveBuildingRules.Advance(ref auto, "HabBlock", 1, 1, 1000, true, out finished);
            Check(finished && auto.level == 1 && auto.completionSeconds == 6 && BuildingOperationRules.Valid(auto),
                "completed adaptive upgrades advance capability tier and trigger a saved completion pulse");
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

            var disabledSchedule = new EventSchedule(1234, 1f);
            var frozen = disabledSchedule.CreateSnapshot();
            disabledSchedule.TriggerNow();
            Check(!disabledSchedule.Advance(1800f) && !disabledSchedule.AwaitingChoice && !disabledSchedule.Resolve()
                && JsonSerializer.Serialize(frozen, JsonOptions) == JsonSerializer.Serialize(disabledSchedule.CreateSnapshot(), JsonOptions),
                "disabled priority transmissions freeze scheduling and ignore debug triggers and resolution");
            frozen.awaitingChoice = true;
            var disabledPending = new EventSchedule(1234, 1f, frozen);
            Check(!disabledPending.AwaitingChoice && !disabledPending.Advance(1800f) && !disabledPending.Resolve()
                && JsonSerializer.Serialize(frozen, JsonOptions) == JsonSerializer.Serialize(disabledPending.CreateSnapshot(), JsonOptions),
                "saved pending transmissions stay dormant and survive save/load unchanged");
            Check(new EventSchedule(1234, 1f, disabledPending.CreateSnapshot(), enabled: true).AwaitingChoice,
                "retained pending transmissions can resume if the feature is re-enabled");

            var schedule = new EventSchedule(1234, 1f, enabled: true);
            schedule.Advance(7f);
            var snapshot = JsonSerializer.Deserialize<EventSnapshot>(JsonSerializer.Serialize(schedule.CreateSnapshot(), JsonOptions), JsonOptions);
            var loaded = new EventSchedule(1234, 1f, snapshot, enabled: true);
            Check(snapshot.remainingSeconds == loaded.CreateSnapshot().remainingSeconds, "event time survives save/load");
            Check(schedule.Advance(180f) && loaded.Advance(180f) && !loaded.Advance(180f),
                "timer produces exactly one pending decision");
            var pending = new EventSchedule(1234, 1f, loaded.CreateSnapshot(), enabled: true);
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
            schedule = new EventSchedule(42, 1f, enabled: true);
            for (var second = 0; second < 600; second++)
            {
                var tick = Tick(expanded, population,
                    ColonyRules.Habitability(world.Co2, world.O2, world.WaterPollution, world.GlobalTemperatureOffset));
                population = tick.population;
                world = EnvironmentRules.ApplyIndustry(world, emissions.Scale(tick.utilisation), 1f, 1f);
                money = Math.Max(0f, money + tick.production - tick.upkeep);
                if (schedule.Advance(1f)) { decisions++; schedule.Resolve(); }
                if (second == 300) schedule = new EventSchedule(42, 1f, schedule.CreateSnapshot(), enabled: true);
            }
            Check(ColonyRules.IsFinite(money) && money > 500f && population > 160f && decisions >= 10,
                "ten-minute simulation grows and continues decisions across a load");

            var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
            var guideEntries = ColonyGuideContent.Entries;
            var buildableIds = Directory.GetFiles(Path.Combine(root, "Assets/Resources/Buildings"), "*.asset")
                .Select(File.ReadAllText).Where(asset => !Regex.IsMatch(asset, @"(?m)^  developerOnly: 1\s*$"))
                .Select(asset => Regex.Match(asset, @"(?m)^  id: (\S+)\s*$").Groups[1].Value).ToHashSet();
            Check(guideEntries.Count == 18 && ColonyGuideContent.PageCount == 20,
                "the colony guide provides a ship overview, all 18 facility pages and a colony completion page");
            var guideIds = guideEntries.Select(entry => entry.Id).ToHashSet();
            Check(guideIds.SetEquals(buildableIds),
                "the colony guide covers every player-buildable facility ID exactly once; missing: "
                + string.Join(",", buildableIds.Except(guideIds)) + "; unknown: " + string.Join(",", guideIds.Except(buildableIds)));
            Check(guideEntries.All(entry => entry.CatalogGroup >= 0 && entry.CatalogGroup < 6
                && !string.IsNullOrWhiteSpace(entry.HowItWorks) && !string.IsNullOrWhiteSpace(entry.ColonyRole)),
                "all colony guide pages have usable instructions and valid construction-page routing");
            Check(guideEntries.Any(entry => entry.Id == "GroundwaterPump" && entry.Essential)
                && guideEntries.Any(entry => entry.Id == "Farm" && entry.Essential)
                && guideEntries.Any(entry => entry.Id == "PromethiumGenerator" && entry.Essential)
                && !guideEntries.Any(entry => entry.Id == "DevSupply"),
                "the colony guide marks founding services as essential and excludes developer-only facilities");
            var starterCosts = Directory.GetFiles(Path.Combine(root, "Assets/Resources/Buildings"), "*.asset")
                .Select(File.ReadAllText).Where(asset => AssetFloat(asset, "developerOnly") == 0)
                .Select(asset => (basic: AssetFloat(asset, "constructionSupplies"), advanced: AssetFloat(asset, "advancedSupplies"),
                    count: Regex.IsMatch(asset, @"(?m)^  id: Mine\s*$") ? 3 : 1)).ToArray();
            var manifest = ExpeditionRules.Manifest(starterCosts.Sum(c => c.basic * c.count), starterCosts.Sum(c => c.advanced * c.count));
            Check(IndustryRules.ValidStock(manifest) && manifest[(int)MaterialKind.Components] > 0,
                "the finite expedition manifest uses all player building costs and fits persistent inventory limits");
            foreach (var cost in starterCosts)
                for (var copy = 0; copy < cost.count; copy++)
                {
                    var basic = cost.basic; var advanced = cost.advanced;
                    Check(ExpeditionRules.TrySpend(manifest, MaterialKind.Components, ref basic)
                        && ExpeditionRules.TrySpend(manifest, MaterialKind.Plasteel, ref advanced),
                        "starter cargo funds the next facility including three specialist mines");
                }
            Check(manifest.All(value => Math.Abs(value) < .0001f), "building the complete starter manifest consumes its finite supplies once within float precision");
            var overspend = 1f;
            Check(!ExpeditionRules.TrySpend(manifest, MaterialKind.Components, ref overspend) && Math.Abs(overspend - 1) < .0001f,
                "exhausted colony ship cargo cannot fund further construction");
            var reserves = Enumerable.Range(0, 150).Select(id => ExpeditionRules.SurfaceReserve(1234, id)).ToArray();
            Check(reserves.All(amount => amount >= 120 && amount <= 180) && reserves.Distinct().Count() > 10
                && reserves.SequenceEqual(Enumerable.Range(0, 150).Select(id => ExpeditionRules.SurfaceReserve(1234, id))),
                "orbital surface reserves are small, varied and seed stable");
            var categoryReserveMultipliers = new[] { 1.7f, 1.35f, 1f, .72f, .48f };
            var categoryReserves = categoryReserveMultipliers.Select(multiplier => ExpeditionRules.SurfaceReserve(1234, 23, multiplier)).ToArray();
            Check(categoryReserves.Zip(categoryReserves.Skip(1), (easier, harder) => easier > harder).All(decreases => decreases)
                && Math.Abs(categoryReserves[2] - ExpeditionRules.SurfaceReserve(1234, 23)) < .001f,
                "landing categories scale nearby reserve sizes monotonically from Easy to Extreme Hard");
            var surfaceBody = ExpeditionRules.SurfaceVein(1234, 0, MaterialKind.IronOre);
            Check(MiningRules.Valid(surfaceBody) && Math.Abs(MiningRules.Remaining(surfaceBody)-reserves[0]) < .001
                && surfaceBody.removed.All(amount => amount == 0), "small surface veins contain actual finite ore without pre-excavated pits");
            var extremeHardBody = ExpeditionRules.SurfaceVein(1234, 0, MaterialKind.IronOre, .48f);
            Check(MiningRules.Valid(extremeHardBody)
                && Math.Abs(MiningRules.Remaining(extremeHardBody) - ExpeditionRules.SurfaceReserve(1234, 0, .48f)) < .001f
                && MiningRules.Remaining(extremeHardBody) < MiningRules.Remaining(surfaceBody),
                "category-scaled mineral veins preserve their finite reserve and reduce extraction stock on harder sites");
            var expeditionSave = new IndustrySnapshot { geologyVersion=4, facilities=Array.Empty<FacilityStock>(), cargo=Array.Empty<CargoStock>(),
                expeditionCargo=ExpeditionRules.Manifest(100, 10), deposits=new[]{new DepositStock { id=0,kind=0,x=1,
                    remaining=MiningRules.Remaining(surfaceBody),excavation=surfaceBody,discovered=true }} };
            Check(IndustryRules.Valid(expeditionSave,0), "the colony ship and exact scanned deposit directions form a valid save");
            var expeditionReload = JsonSerializer.Deserialize<IndustrySnapshot>(JsonSerializer.Serialize(expeditionSave,JsonOptions),JsonOptions);
            Check(IndustryRules.Valid(expeditionReload,0) && expeditionReload.expeditionCargo[6]==100 && expeditionReload.deposits[0].x==1
                && expeditionReload.deposits[0].discovered, "ship cargo, surface reserves and landing survey directions survive JSON");
            expeditionReload.deposits[0].x=0;
            Check(!IndustryRules.Valid(expeditionReload,0), "invalid scanned deposit directions are rejected before replacing a colony");
            expeditionReload.deposits[0].x=1; expeditionReload.expeditionCargo[6]=float.NaN;
            Check(!IndustryRules.Valid(expeditionReload,0), "nonfinite colony ship cargo is rejected");
            Check(explorationSave.expeditionCargo==null && IndustryRules.Valid(explorationSave,1),
                "established saves remain valid without receiving a fresh starter manifest");
            var architecture = Path.Combine(root, "Assets/Resources/Architecture");
            var materialAsset = File.ReadAllText(Path.Combine(architecture, "BuildingSurface.mat"));
            var atlasMeta = File.ReadAllText(Path.Combine(architecture, "BuildingSurfaceAtlas.png.meta"));
            var shaderMeta = File.ReadAllText(Path.Combine(root, "Assets/Shaders/ArchitectureSurface_BuiltIn.shader.meta"));
            Check(materialAsset.Contains("guid: " + Regex.Match(atlasMeta, @"guid: (\w+)").Groups[1].Value)
                && materialAsset.Contains("guid: " + Regex.Match(shaderMeta, @"guid: (\w+)").Groups[1].Value)
                && File.Exists(Path.Combine(architecture, "BuildingSurface.mat.meta")),
                "Resources architecture material retains the real atlas and shader GUID dependencies for player builds");
            var atlasBytes = File.ReadAllBytes(Path.Combine(architecture, "BuildingSurfaceAtlas.png"));
            int PngInt(int offset) => (atlasBytes[offset] << 24) | (atlasBytes[offset + 1] << 16)
                | (atlasBytes[offset + 2] << 8) | atlasBytes[offset + 3];
            Check(atlasBytes.Take(8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 })
                && PngInt(16) == PngInt(20) && PngInt(16) >= 1024,
                "photographic architecture atlas is a valid high-resolution square PNG");
            TerrainChecks.Run(Check, JsonOptions);
            EarthworksChecks.Run(Check, JsonOptions);
            ResidentialChecks.Run(Check,root,JsonOptions);
            if(args.Contains("--residential-benchmark"))ResidentialChecks.Benchmark(root,JsonOptions);
            var definitionFiles=Directory.GetFiles(Path.Combine(root,"Assets/Resources/Discoveries"),"*.asset");
            var definitionKinds=new HashSet<int>();
            var definitionScriptGuid=Regex.Match(File.ReadAllText(Path.Combine(root,"Assets/Scripts/Sim/DiscoveryDefinition.cs.meta")),@"guid: (\w+)").Groups[1].Value;
            var definitionsValid=definitionFiles.Length==4;
            foreach(var path in definitionFiles)
            {
                var text=File.ReadAllText(path);var kind=(int)AssetFloat(text,"kind");definitionKinds.Add(kind);
                var defaults=ExplorationRules.DefaultProfile((DiscoveryKind)kind);
                definitionsValid&=text.Contains("guid: "+definitionScriptGuid)&&File.Exists(path+".meta")
                    &&Regex.IsMatch(text,@"(?m)^  description: .+")&&Regex.IsMatch(text,@"(?m)^  rewards:$")
                    &&Math.Abs(AssetFloat(text,"cautiousMultiplier")-defaults.cautiousMultiplier)<.00001f
                    &&Math.Abs(AssetFloat(text,"rapidMultiplier")-defaults.rapidMultiplier)<.00001f
                    &&Math.Abs(AssetFloat(text,"hazardChance")-defaults.hazardChance)<.00001f;
            }
            Check(definitionsValid&&definitionKinds.SetEquals(new[]{0,1,2,3}),"all discovery assets have distinct kinds, matching script GUIDs and configured reward values");
            var profileFiles=Directory.GetFiles(Path.Combine(root,"Assets/Resources/Industry"),"*.asset");
            Check(profileFiles.Length>=6&&profileFiles.All(path=>Regex.IsMatch(File.ReadAllText(path),@"(?m)^  buildingId: \S+")
                &&!Regex.IsMatch(File.ReadAllText(path),@"(?m)^(buildingId|powerDemand|powerOutput|inputs|outputs|operatingImpact):")),
                "industrial profile fields are nested inside Unity's MonoBehaviour serialized object");
            var prototype = File.ReadAllText(Path.Combine(root, "Assets/Scenes/Prototype/WH30K_VS01.unity"));
            Check(prototype.Contains("planetRadius: 3185500") && prototype.Contains("splitDistance: 1200")
                && File.ReadAllText(Path.Combine(root, "ProjectSettings/EditorBuildSettings.asset")).Contains("Assets/Scenes/Prototype/WH30K_VS01.unity"),
                "the enabled playable scene uses half Earth's radius and scaled LOD distance");
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
