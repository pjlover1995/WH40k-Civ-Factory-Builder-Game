using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UResources = UnityEngine.Resources;
using WH30K.Planet;
using WH30K.Gameplay.Construction;
using WH30K.Sim.Settlements;
using WH30K.Game;
using WH30K.UI;

namespace WH30K.Sim.Resources
{
    public sealed class SurfaceDeposit
    {
        public int Id; public MaterialKind Kind; public Vector3 Direction, Position; public float Remaining=2000; public float ReserveMultiplier=1f; public bool Discovered;
        public ExcavationState Excavation;
        public float InitialVolume;
        public readonly DepositUsage Usage = new DepositUsage();
        internal Mesh pitMesh;
        internal Transform pit;
        internal float[] renderedRadii, renderedDepths;
        internal Transform outcrop, marker;
    }
    internal sealed class SurfaceDiscovery
    {
        public DiscoveryRecord State;
        public Vector3 Direction, Position;
        public Transform Marker, Site;
    }
    // Cargo is removed at dispatch and delivered only on arrival. Production never reads remote stock.
    [DefaultExecutionOrder(100)]
    public sealed partial class IndustrySystem : MonoBehaviour
    {
        private LODPlanet planet;
        private BuildingPlacementController placement;
        private Settlement settlement;
        private ResourceSystem resources;
        private readonly List<SurfaceDeposit> deposits=new List<SurfaceDeposit>();
        private readonly List<SurfaceDiscovery> discoveries=new List<SurfaceDiscovery>();
        private readonly List<FacilityStock> stocks=new List<FacilityStock>();
        private float[] expeditionCargo;
        private float[] initialTenderManifest;
        private TenderLogisticsState tenderLogistics;
        private FoundingProgress founding;
        private float foundingFoodDelivery;
        public FoundingProgress Founding => founding;
        public void SetFoundingObjectives(bool enabled) { founding.enabled = enabled; }
        public float RecoverFoundingCanister()
        {
            if (!founding.completed) return 0;
            var remaining = FoundingObjectives.Reward - founding.rewardRecovered;
            var recovered = 0f;
            if (expeditionCargo != null)
            {
                var reserved = 0f;
                foreach (var flight in flights)
                    if (!flight.state.food && flight.state.target == -3 && flight.state.kind == (int)MaterialKind.Components)
                        reserved += flight.state.amount;
                recovered = StoreOnShip(MaterialKind.Components, Mathf.Min(remaining, Mathf.Max(0, ShipStorageRoom(MaterialKind.Components)-reserved)));
            }
            remaining -= recovered;
            for (var i=0; i<stocks.Count && remaining>0; i++)
            {
                var added = Mathf.Min(remaining, Mathf.Max(0, DeliveryRoom(i, MaterialKind.Components)));
                stocks[i].quantities[(int)MaterialKind.Components] += added;
                recovered += added; remaining -= added;
            }
            founding.rewardRecovered += recovered;
            return recovered;
        }
        public bool HasExpeditionCargo => expeditionCargo != null;
        public int TenderTripsRemaining => TenderLogisticsRules.TripsRemaining(tenderLogistics);
        public float OrbitalCrewAvailable => tenderLogistics?.orbitalCrew ?? 0;
        public float OrbitalRationsAvailable => tenderLogistics?.orbitalFood ?? 0;
        public float OrbitalSupplyAvailable(MaterialKind kind)
            => tenderLogistics?.orbitalSupplies != null && (int)kind >= 0 && (int)kind < tenderLogistics.orbitalSupplies.Length
                ? tenderLogistics.orbitalSupplies[(int)kind] : 0;
        public float ShipCargo(MaterialKind kind) => expeditionCargo != null ? expeditionCargo[(int)kind] : 0;
        // The landed vessel acts as a shared logistics store with a finite 1,920-unit
        // capacity per material; the initial construction manifest occupies part of it.
        public const float ShipStoragePerMaterial = 1920f;
        public float ShipStorageRoom(MaterialKind kind) => expeditionCargo == null ? 0
            : Mathf.Max(0, ShipStoragePerMaterial - ShipCargo(kind));
        private float StoreOnShip(MaterialKind kind, float amount)
        {
            if (expeditionCargo == null || amount <= 0) return 0;
            // Existing construction manifest counts as usable ship inventory.
            var added = Mathf.Min(amount, Mathf.Max(0, ShipStoragePerMaterial - expeditionCargo[(int)kind]));
            expeditionCargo[(int)kind] += added;
            return added;
        }
        public void PrepareExpedition(Vector3 landingRegion)
        {
            PrepareExpedition(new[]
            {
                new GameSettings.LandingSite
                {
                    category = GameSettings.CurrentDifficulty,
                    direction = landingRegion,
                    reserveMultiplier = GameSettings.GetLandingReserveMultiplier(GameSettings.CurrentDifficulty)
                }
            });
        }

        public void PrepareImmediateLanding(Vector3 landingRegion)
        {
            PrepareExpedition(landingRegion);
            // Direct, already-landed starts are used by tools and scene fixtures. They
            // have no physical orbital shuttle phase, so preserve save validity without
            // granting orbital stock or unearned return runs.
            tenderLogistics = new TenderLogisticsState
            {
                orbitalCrew = 0,
                orbitalFood = 0,
                orbitalSupplies = new float[IndustryRules.MaterialCount],
                tripsUsed = TenderLogisticsRules.MaximumTrips,
                initialLoadoutApplied = true
            };
        }

        public void PrepareExpedition(IReadOnlyList<GameSettings.LandingSite> landingSites)
        {
            var basic = 0f; var advanced = 0f;
            foreach (var definition in UResources.LoadAll<BuildableStructure>("Buildings"))
            {
                if (definition.DeveloperOnly) continue;
                var count = definition.Id == "Mine" ? 3 : 1;
                basic += count * definition.ConstructionSupplies;
                advanced += count * definition.AdvancedSupplies;
            }
            expeditionCargo = ExpeditionRules.Manifest(basic, advanced);
            // On continental worlds one local biome can span hundreds of kilometres. The
            // orbital survey seeds each selectable touchdown with a small finite starter set.
            if (planet.Radius > 50000 && landingSites != null)
                for (var i = 0; i < landingSites.Count; i++)
                    AddLandingRegionReserves(landingSites[i].direction, landingSites[i].reserveMultiplier, i);
            for (var i = 0; i < 800; i++)
            {
                var y = 1 - 2 * (i + .5f) / 800; var a = i * 2.39996323f; var r = Mathf.Sqrt(1 - y * y);
                var n = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                if (Suitable(MaterialKind.Promethium, n)) AddDeposit(MaterialKind.Promethium, n);
            }
            var revealedKinds = new HashSet<MaterialKind>();
            foreach (var deposit in deposits)
            {
                if (!deposit.Discovered && !ExpeditionRules.SurfaceVisible(deposit.Id) && revealedKinds.Contains(deposit.Kind)) continue;
                revealedKinds.Add(deposit.Kind); deposit.Discovered = true;
                var reserve = ExpeditionRules.SurfaceReserve(planet.Seed, deposit.Id, deposit.ReserveMultiplier);
                if (deposit.Excavation != null)
                {
                    deposit.Excavation = ExpeditionRules.SurfaceVein(planet.Seed, deposit.Id, deposit.Kind, deposit.ReserveMultiplier);
                }
                deposit.Remaining = deposit.Excavation != null ? MiningRules.Remaining(deposit.Excavation) : reserve;
                deposit.InitialVolume = deposit.Remaining;
            }
            SetOverlay(1);
        }

        public void PrepareOrbitalExpedition(IReadOnlyList<GameSettings.LandingSite> landingSites)
        {
            PrepareExpedition(landingSites);
            initialTenderManifest = (float[])expeditionCargo.Clone();
            tenderLogistics = new TenderLogisticsState
            {
                orbitalCrew = TenderLogisticsRules.MainVesselCrew,
                orbitalFood = TenderLogisticsRules.MainVesselRations,
                orbitalSupplies = (float[])expeditionCargo.Clone()
            };
            for (var i = 0; i < tenderLogistics.orbitalSupplies.Length; i++)
                tenderLogistics.orbitalSupplies[i] *= TenderLogisticsRules.MaximumTrips;
            expeditionCargo = new float[IndustryRules.MaterialCount];
        }

        public bool CanApplyInitialLandingLoadout(float crew, float rations, float supplyFraction)
        {
            if (tenderLogistics == null) return true;
            return !tenderLogistics.initialLoadoutApplied
                && ColonyRules.IsFinite(crew) && crew >= 250 && crew <= ShipRules.CrewCapacity
                && crew <= tenderLogistics.orbitalCrew
                && ColonyRules.IsFinite(rations) && rations >= 0 && rations <= FarmRules.FoodCapacity
                && rations <= tenderLogistics.orbitalFood
                && ColonyRules.IsFinite(supplyFraction) && supplyFraction >= 0 && supplyFraction <= 1;
        }

        public bool ApplyInitialLandingLoadout(float crew, float rations, float supplyFraction)
        {
            if (tenderLogistics == null) return true;
            if (!CanApplyInitialLandingLoadout(crew, rations, supplyFraction) || settlement == null || resources == null
                || initialTenderManifest == null || initialTenderManifest.Length != IndustryRules.MaterialCount
                || !settlement.SetTenderCrew(crew) || !resources.SetFoodReserve(rations)) return false;
            for (var i = 0; i < IndustryRules.MaterialCount; i++)
            {
                var amount = initialTenderManifest[i] * supplyFraction;
                expeditionCargo[i] += amount;
                tenderLogistics.orbitalSupplies[i] -= amount;
            }
            tenderLogistics.orbitalCrew -= crew;
            tenderLogistics.orbitalFood -= rations;
            tenderLogistics.initialLoadoutApplied = true;
            return true;
        }

        public bool TryOrbitalSupplyRun(float crew, float rations, float components, float plasteel)
        {
            if (tenderLogistics == null || tenderLogistics.tripsUsed >= TenderLogisticsRules.MaximumTrips
                || !ColonyRules.IsFinite(crew) || !ColonyRules.IsFinite(rations)
                || !ColonyRules.IsFinite(components) || !ColonyRules.IsFinite(plasteel)
                || crew < 0 || rations < 0 || components < 0 || plasteel < 0
                || crew + rations + components + plasteel <= 0
                || crew > tenderLogistics.orbitalCrew || rations > tenderLogistics.orbitalFood
                || components > tenderLogistics.orbitalSupplies[(int)MaterialKind.Components]
                || plasteel > tenderLogistics.orbitalSupplies[(int)MaterialKind.Plasteel]
                || settlement == null || resources == null || !settlement.CanReceiveTenderCrew(crew)
                || rations > resources.FoodRoom
                || components > ShipStorageRoom(MaterialKind.Components)
                || plasteel > ShipStorageRoom(MaterialKind.Plasteel)) return false;

            settlement.ReceiveTenderCrew(crew);
            resources.AddFood(rations);
            StoreOnShip(MaterialKind.Components, components);
            StoreOnShip(MaterialKind.Plasteel, plasteel);
            tenderLogistics.orbitalCrew -= crew;
            tenderLogistics.orbitalFood -= rations;
            tenderLogistics.orbitalSupplies[(int)MaterialKind.Components] -= components;
            tenderLogistics.orbitalSupplies[(int)MaterialKind.Plasteel] -= plasteel;
            tenderLogistics.tripsUsed++;
            return true;
        }
        private void AddLandingRegionReserves(Vector3 region, float reserveMultiplier, int siteIndex)
        {
            var frame = Quaternion.FromToRotation(Vector3.up, region);
            var kinds = new[] { MaterialKind.IronOre, MaterialKind.CopperOre, MaterialKind.Stone,
                MaterialKind.Biomass, MaterialKind.Crystal, MaterialKind.Promethium };
            for (var k = 0; k < kinds.Length; k++)
            {
                var random = new System.Random(unchecked(planet.Seed ^ siteIndex * 7919 ^ k * 92821 ^ 0x5A17));
                for (var attempt = 0; attempt < 48; attempt++)
                {
                    var side = k % 2 == 0 ? 1 : -1;
                    var lateral = 620 + (float)random.NextDouble() * 300;
                    var along = -700 + (float)random.NextDouble() * 1400;
                    var offset = new Vector3(side * lateral, 0, along);
                    var n = (region + frame * offset / planet.Radius).normalized;
                    if (planet.EvaluateSurfacePoint(n).magnitude < planet.SeaLevel + 8) continue;
                    var clear = true;
                    foreach (var existing in deposits)
                        if (planet.SurfaceDistance(n, existing.Direction) < 130) { clear = false; break; }
                    if (!clear) continue;
                    AddDeposit(kinds[k], n, reserveMultiplier);
                    deposits[deposits.Count - 1].Discovered = true;
                    break;
                }
            }
        }
        private readonly List<Flight> flights=new List<Flight>();
        private readonly Stack<Flight> idleFlights=new Stack<Flight>();
        private readonly Dictionary<(int,int),FlightRoute> routes=new Dictionary<(int,int),FlightRoute>();
        private readonly List<int>[] consumers = CreateConsumers();
        private static List<int>[] CreateConsumers() { var result = new List<int>[IndustryRules.MaterialCount]; for (var i=0;i<result.Length;i++) result[i]=new List<int>(); return result; }
        private float[] incoming=Array.Empty<float>();
        private readonly float[] producedWindow=new float[IndustryRules.MaterialCount],consumedWindow=new float[IndustryRules.MaterialCount];
        private readonly float[] productionRates=new float[IndustryRules.MaterialCount],consumptionRates=new float[IndustryRules.MaterialCount],recipeBefore=new float[IndustryRules.MaterialCount];
        private readonly float[] producedTotals=new float[IndustryRules.MaterialCount],consumedTotals=new float[IndustryRules.MaterialCount];
        private readonly float[] recipeProduced=new float[IndustryRules.MaterialCount],recipeConsumed=new float[IndustryRules.MaterialCount];
        private float rateWindowSeconds;
        public double DepositUsageSeconds { get; private set; }
        private Camera surveyCamera;
        private float surveyTimer;
        private float revealTimer;
        private int surveyOverlay=-1;
        public int CargoVisualCount=>flights.Count+idleFlights.Count;
        private ManagedBuilding[] buildings=Array.Empty<ManagedBuilding>();
        private ManagedBuilding[] surveyors=Array.Empty<ManagedBuilding>();
        private bool[] connectedToCore=Array.Empty<bool>();
        private int[] networkComponent=Array.Empty<int>();
        private float[] networkPowerDemand=Array.Empty<float>(),networkPowerSupply=Array.Empty<float>(),networkPowerFraction=Array.Empty<float>(),networkFuelLoad=Array.Empty<float>();
        private Mesh logisticsMesh;
        private MeshRenderer logisticsRenderer;
        private bool logisticsVisualDirty=true;
        private int logisticsSurfaceRevision=-1;
        private WH30K.Sim.Environment.EnvironmentState environment;
        private ResearchSystem research;
        public float PowerGenerated { get; private set; }
        public float PowerDemand { get; private set; }
        public float WorkersRequired { get; private set; }
        public float WorkersAvailable => resources != null ? resources.CreateSnapshot().Workforce : 0;
        public void FillRates(float[] produced,float[] consumed)
        {
            if(produced!=null)Array.Copy(productionRates,produced,Math.Min(produced.Length,productionRates.Length));
            if(consumed!=null)Array.Copy(consumptionRates,consumed,Math.Min(consumed.Length,consumptionRates.Length));
        }
        private Transform visualRoot;
        private Material haulerMaterial, foodHaulerMaterial, routeMaterial, logisticsMaterial;
        private readonly List<Material> materials=new List<Material>();
        private readonly Dictionary<string,IndustrialBuildingProfile> profiles=new Dictionary<string,IndustrialBuildingProfile>();
        private readonly Dictionary<DiscoveryKind,DiscoveryDefinition> discoveryDefinitions=new Dictionary<DiscoveryKind,DiscoveryDefinition>();
        private int inspectedLocation=-1;
        private float dispatchTimer;
        public int Overlay {get;private set;}
        public IReadOnlyList<SurfaceDeposit> Deposits=>deposits;
        public int DiscoveredCount
        { get { var count=0;foreach(var d in deposits)if(d.Discovered)count++;foreach(var d in discoveries)if(d.State.discovered)count++;return count; } }
        public int DiscoveryCount=>deposits.Count+discoveries.Count;
        public bool CanInspectLocations=>planet!=null&&placement!=null&&!placement.IsPlacing
            && !(GetComponent<EarthworksController>()?.IsOpen??false) && !(GetComponent<NewGameMenu>()?.HasOpenEvent??false);
        public int CargoCount=>flights.Count;
        public bool HasInfiniteConstructionSupplies
        {
            get { foreach(var building in buildings) if(building!=null && building.Id=="DevSupply" && !building.State.offline) return true; return false; }
        }
        public bool CanAffordConstruction(float basic,float advanced)
        {
            if (!ColonyRules.IsFinite(basic) || !ColonyRules.IsFinite(advanced) || basic < 0 || advanced < 0) return false;
            if(HasInfiniteConstructionSupplies)return true;
            var totals=ConstructionSupplyTotals();
            return totals.basic+0.0001f>=basic && totals.advanced+0.0001f>=advanced;
        }
        public bool TrySpendConstruction(float basic,float advanced)
        {
            if(!CanAffordConstruction(basic,advanced))return false;
            if(HasInfiniteConstructionSupplies)return true;
            if (!SpendAcross(MaterialKind.Components,basic) || !SpendAcross(MaterialKind.Plasteel,advanced)) return false;
            RecordConsumption((int)MaterialKind.Components,basic);
            RecordConsumption((int)MaterialKind.Plasteel,advanced);
            return true;
        }
        public (float basic,float advanced) ConstructionSupplyTotals()
        {
            var basic=expeditionCargo != null ? expeditionCargo[(int)MaterialKind.Components] : 0f;
            var advanced=expeditionCargo != null ? expeditionCargo[(int)MaterialKind.Plasteel] : 0f;
            for(var i=0;i<stocks.Count;i++)if(stocks[i]!=null&&stocks[i].quantities!=null&&stocks[i].quantities.Length>=IndustryRules.MaterialCount)
            {basic+=stocks[i].quantities[(int)MaterialKind.Components];advanced+=stocks[i].quantities[(int)MaterialKind.Plasteel];}
            return (basic,advanced);
        }
        public void AddRecoveredSupplies(float amount)
        {
            if (!ColonyRules.IsFinite(amount) || amount <= 0) return;
            // Legacy core colonies use the same saved shared physical store for recovered supplies.
            if (expeditionCargo == null) expeditionCargo = new float[IndustryRules.MaterialCount];
            amount -= StoreOnShip(MaterialKind.Components, amount);
            for (var i=0; i<stocks.Count && amount>0; i++)
            {
                var added=Mathf.Min(amount,DeliveryRoom(i,MaterialKind.Components));
                stocks[i].quantities[(int)MaterialKind.Components]+=added; amount-=added;
            }
        }
        private bool SpendAcross(MaterialKind kind,float amount)
        {
            if(amount<=0)return true;
            if (expeditionCargo != null) ExpeditionRules.TrySpend(expeditionCargo, kind, ref amount);
            for(var i=0;i<stocks.Count&&amount>0;i++)
            {var take=Mathf.Min(amount,stocks[i].quantities[(int)kind]);stocks[i].quantities[(int)kind]-=take;amount-=take;}
            return amount<=.0001f;
        }
        public void Configure(LODPlanet world, Vector3 colonyNormal, IndustrySnapshot saved=null)
        {
            GetComponent<NewGameMenu>()?.CloseFacilityPanel();
            GetComponent<NewGameMenu>()?.ResetDiscoveryPanel();
            Clear();founding=saved!=null?saved.founding:default;planet=world;placement=GetComponent<BuildingPlacementController>();settlement=GetComponent<Settlement>();resources=GetComponent<ResourceSystem>();
            environment=GetComponent<WH30K.Sim.Environment.EnvironmentState>();
            research=GetComponent<ResearchSystem>();
            profiles.Clear();foreach(var profile in UResources.LoadAll<IndustrialBuildingProfile>("Industry"))if(profile!=null&&!string.IsNullOrEmpty(profile.BuildingId))profiles[profile.BuildingId]=profile;
            foreach(var definition in UResources.LoadAll<DiscoveryDefinition>("Discoveries"))if(definition!=null)discoveryDefinitions[definition.Kind]=definition;
            visualRoot=new GameObject("Geological survey and cargo").transform;visualRoot.SetParent(planet.transform,false);
            for(var k=0;k<IndustryRules.MaterialCount;k++) materials.Add(new Material(Shader.Find("Standard")) {color=DepositColor((MaterialKind)k)});
            for(var kind=0;kind<4;kind++)materials.Add(new Material(Shader.Find("Standard"))
                {color=Definition((DiscoveryKind)kind)!=null?Definition((DiscoveryKind)kind).MarkerColor:new Color(.84f,.72f,.35f),enableInstancing=true});
            haulerMaterial=new Material(Shader.Find("Standard")) {color=new Color(.56f,.38f,.13f),enableInstancing=true};
            materials.Add(new Material(Shader.Find("Standard")){name="Harvested timber",color=new Color(.38f,.21f,.10f),enableInstancing=true});
            foodHaulerMaterial=new Material(haulerMaterial){name="Food delivery hauler",color=new Color(.30f,.66f,.22f)};materials.Add(foodHaulerMaterial);
            routeMaterial=new Material(Shader.Find("Sprites/Default"));logisticsMaterial=new Material(Shader.Find("Sprites/Default")){color=new Color(.25f,.76f,.70f,.5f)};
            materials.Add(haulerMaterial);materials.Add(routeMaterial);materials.Add(logisticsMaterial);
            if (saved != null && saved.geologyVersion >= 4)
                foreach (var record in saved.deposits) AddDeposit((MaterialKind)record.kind, new Vector3(record.x, record.y, record.z));
            else GenerateDeposits(colonyNormal);
            if(saved!=null) foreach(var record in saved.deposits)
                if(record.id<deposits.Count)
                {
                    var deposit=deposits[record.id];deposit.Remaining=record.remaining;
                    if(deposit.Kind!=MaterialKind.Biomass && deposit.Kind!=MaterialKind.Promethium)
                        deposit.Excavation=saved.geologyVersion==0?MiningRules.Create(planet.Seed,deposit.Id,deposit.Kind,record.remaining):record.excavation;
                    deposit.InitialVolume = record.initialVolume > 0 ? record.initialVolume
                        : deposit.Excavation != null ? MiningRules.Capacity(deposit.Excavation) : 0;
                    deposit.Discovered=record.discovered;
                }
            GenerateDiscoveries(saved,colonyNormal);
            Synchronize();
            RevealExistingExtractorDeposits();
            ConfigureExcavation(saved);
            ConfigureSurveyPresentation(saved);
            if(saved!=null)
            {
                expeditionCargo = saved.expeditionCargo != null && saved.expeditionCargo.Length > 0 ? (float[])IndustryRules.UpgradeStock(saved.expeditionCargo).Clone() : null;
                tenderLogistics = saved.tenderLogistics == null ? null : new TenderLogisticsState
                {
                    version = saved.tenderLogistics.version,
                    orbitalCrew = saved.tenderLogistics.orbitalCrew,
                    orbitalFood = saved.tenderLogistics.orbitalFood,
                    orbitalSupplies = saved.tenderLogistics.orbitalSupplies != null ? (float[])saved.tenderLogistics.orbitalSupplies.Clone() : null,
                    tripsUsed = saved.tenderLogistics.tripsUsed,
                    initialLoadoutApplied = saved.tenderLogistics.initialLoadoutApplied
                };
                stocks.Clear();
                foreach(var source in saved.facilities)
                {
                    stocks.Add(new FacilityStock{quantities=(float[])IndustryRules.UpgradeStock(source.quantities).Clone(),work=source.work,food=source.food,
                        configuration=saved.facilityVersion>0?FacilityRules.Clone(source.configuration):null});
                }
                RebuildConsumers();
                foreach(var cargo in saved.cargo) AddFlight(cargo);
                if(saved.materialVersion>0)
                {
                    Array.Copy(saved.producedTotals,producedTotals,Math.Min(saved.producedTotals.Length, IndustryRules.MaterialCount));
                    Array.Copy(saved.consumedTotals,consumedTotals,Math.Min(saved.consumedTotals.Length, IndustryRules.MaterialCount));
                    Array.Copy(saved.productionRates,productionRates,Math.Min(saved.productionRates.Length, IndustryRules.MaterialCount));
                    Array.Copy(saved.consumptionRates,consumptionRates,Math.Min(saved.consumptionRates.Length, IndustryRules.MaterialCount));
                }
            }
            RefreshIncoming();Overlay=0;
        }
        private void GenerateDiscoveries(IndustrySnapshot saved,Vector3 colonyNormal)
        {
            var states=ExplorationRules.Generate(planet.Seed);
            if(saved!=null&&saved.explorationVersion>0&&saved.discoveries!=null)states=saved.discoveries;
            var accepted=new List<SurfaceDiscovery>(states.Length);
            foreach(var initial in states)
            {
                var state=initial;var direction=new Vector3(state.x,state.y,state.z).normalized;
                if(saved==null||saved.explorationVersion==0||saved.discoveries==null)
                {
                    var random=new System.Random(unchecked(planet.Seed^state.id*7919^0x115A));
                    // A few early expeditions lie within successive outpost ranges; rare caches remain global.
                    var localSite=state.id==1||state.id==3||state.id==0;
                    var right=Vector3.Cross(colonyNormal,Mathf.Abs(colonyNormal.y)>.95f?Vector3.right:Vector3.up).normalized;
                    var forward=Vector3.Cross(colonyNormal,right);
                    var valid=false;
                    for(var attempt=0;attempt<256&&!valid;attempt++)
                    {
                        if(localSite&&attempt<192)
                        {
                            var range=(state.id==1?650:state.id==3?1900:3000)+(float)random.NextDouble()*700;
                            var localAngle=(float)random.NextDouble()*Mathf.PI*2;
                            direction=(colonyNormal+(right*Mathf.Cos(localAngle)+forward*Mathf.Sin(localAngle))*range/planet.Radius).normalized;
                        }
                        valid=ValidDiscoverySite(direction,accepted);
                        if(valid)break;
                        var y=(float)(random.NextDouble()*1.6-0.8);var angle=(float)(random.NextDouble()*Math.PI*2);
                        var planar=Mathf.Sqrt(1-y*y);direction=new Vector3(planar*Mathf.Cos(angle),y,planar*Mathf.Sin(angle));
                    }
                    for(var attempt=0;attempt<ExplorationRules.SurveyCellCount&&!valid;attempt++)
                    {
                        ExplorationRules.SurveyCellDirection((attempt+state.id*53)%ExplorationRules.SurveyCellCount,out var x,out var y,out var z);
                        direction=new Vector3(x,y,z);valid=ValidDiscoverySite(direction,accepted);
                    }
                    // Barren worlds can have fewer sites; never place an invalid or overlapping fallback.
                    if(!valid)continue;
                    state.x=direction.x;state.y=direction.y;state.z=direction.z;
                }
                var point=planet.EvaluateSurfacePoint(direction);
                var marker=new GameObject("Discovery - "+DiscoveryName((DiscoveryKind)state.kind),typeof(MeshFilter),typeof(MeshRenderer),typeof(SphereCollider),typeof(DiscoveryMarker));
                marker.transform.SetParent(visualRoot,false);marker.transform.SetPositionAndRotation(planet.transform.TransformPoint(point+direction*18),planet.transform.rotation*Quaternion.FromToRotation(Vector3.up,direction));
                marker.transform.localScale=Vector3.one*28;marker.GetComponent<MeshFilter>().sharedMesh=WorldArt.Octahedron;
                marker.GetComponent<Renderer>().sharedMaterial=materials[IndustryRules.MaterialCount+state.kind];marker.GetComponent<SphereCollider>().radius=.7f;
                marker.GetComponent<DiscoveryMarker>().Configure(this,state.id,false);marker.SetActive(state.discovered);
                accepted.Add(new SurfaceDiscovery{State=state,Direction=direction,Position=planet.transform.TransformPoint(point),Marker=marker.transform});
            }
            discoveries.Clear();discoveries.AddRange(accepted);
        }
        private bool ValidDiscoverySite(Vector3 direction,List<SurfaceDiscovery> accepted)
        {
            if(Mathf.Abs(direction.y)>=.94f||planet.EvaluateSurfacePoint(direction).magnitude<=planet.SeaLevel+8)return false;
            foreach(var deposit in deposits)if((direction-deposit.Direction).sqrMagnitude*planet.Radius*planet.Radius<180*180)return false;
            foreach(var other in accepted)if((direction-other.Direction).sqrMagnitude*planet.Radius*planet.Radius<420*420)return false;
            return true;
        }
        private DiscoveryDefinition Definition(DiscoveryKind kind)=>discoveryDefinitions.TryGetValue(kind,out var definition)?definition:null;
        private string DiscoveryName(DiscoveryKind kind)
        { var definition=Definition(kind);if(definition!=null)return definition.DisplayName;
          switch(kind){case DiscoveryKind.AncientRuins:return "Ancient ruins";case DiscoveryKind.ImperialInstallation:return "Abandoned Imperial installation";case DiscoveryKind.HazardousRegion:return "Hazardous region";default:return "Archaeotech cache";} }
        private void GenerateDeposits(Vector3 colony)
        {
            var random=new System.Random(planet.Seed^0x489C17);
            // Global prospecting follows elevation, moisture and latitude, rather than uniform random ore paint.
            for(var i=0;i<1600;i++)
            {
                var y=1-2*(i+.5f)/1600;var a=i*2.39996323f;var r=Mathf.Sqrt(1-y*y);
                var n=new Vector3(Mathf.Cos(a)*r,y,Mathf.Sin(a)*r);var kind=(MaterialKind)random.Next(5);
                if(Suitable(kind,n) && random.NextDouble()<.55) AddDeposit(kind,n);
            }
            var right=Vector3.Cross(colony,Mathf.Abs(colony.y)>.95f?Vector3.right:Vector3.up).normalized;var forward=Vector3.Cross(colony,right);
            // Denser prospecting around the landing region, still subject to the same geological rules.
            for(var k=0;k<5;k++)
            {
                var found=0;
                for(var attempt=0;attempt<1400 && found<7;attempt++)
                {
                    var a=(float)random.NextDouble()*Mathf.PI*2;var range=170+(float)random.NextDouble()*2600;
                    var n=(colony+(right*Mathf.Cos(a)+forward*Mathf.Sin(a))*range/planet.Radius).normalized;
                    if(!Suitable((MaterialKind)k,n)) continue;
                    var near=false;foreach(var deposit in deposits)
                        if(deposit.Kind==(MaterialKind)k && Vector3.Distance(n,deposit.Direction)*planet.Radius<120) {near=true;break;}
                    if(!near) { AddDeposit((MaterialKind)k,n);found++; }
                }
            }
            // Fuel seeps occur in dry lowlands and form finite, seeded deposits.
            var fuelFound=0;
            for(var attempt=0;attempt<1800 && fuelFound<12;attempt++)
            {
                var a=(float)random.NextDouble()*Mathf.PI*2;var range=220+(float)random.NextDouble()*5200;
                var n=(colony+(right*Mathf.Cos(a)+forward*Mathf.Sin(a))*range/planet.Radius).normalized;
                if(!Suitable(MaterialKind.Promethium,n))continue;
                var near=false;foreach(var deposit in deposits)
                    if(deposit.Kind==MaterialKind.Promethium && Vector3.Distance(n,deposit.Direction)*planet.Radius<360){near=true;break;}
                if(!near){AddDeposit(MaterialKind.Promethium,n);fuelFound++;}
            }
        }
        private bool Suitable(MaterialKind kind,Vector3 n)
        {
            var height=planet.EvaluateSurfacePoint(n).magnitude-planet.SeaLevel;var wet=planet.Moisture(n);
            if(height<5 || Mathf.Abs(n.y)>.86f) return false;
            switch(kind)
            {case MaterialKind.IronOre:return height>45;case MaterialKind.CopperOre:return height>20 && wet<.56f;
                case MaterialKind.Crystal:return height>120;case MaterialKind.Biomass:return wet>.49f && height<220;
                case MaterialKind.Promethium:return height>12 && height<130 && wet<.42f;default:return height>12;}
        }
        private void AddDeposit(MaterialKind kind,Vector3 n,float reserveMultiplier=1f)
        {
            var deposit=new SurfaceDeposit {Id=deposits.Count,Kind=kind,Direction=n,ReserveMultiplier=reserveMultiplier};deposits.Add(deposit);
            if(kind==MaterialKind.Promethium)
            {var random=new System.Random(planet.Seed^deposit.Id*92821);deposit.Remaining=1100+(float)random.NextDouble()*1700;}
            else if(kind!=MaterialKind.Biomass)
            { deposit.Excavation=MiningRules.Create(planet.Seed,deposit.Id,kind);deposit.Remaining=MiningRules.Remaining(deposit.Excavation); }
            deposit.InitialVolume = deposit.Remaining;
            var point=planet.EvaluateSurfacePoint(n);var rotation=Quaternion.FromToRotation(Vector3.up,n);
            deposit.Position=planet.transform.TransformPoint(point);
            var outcrop=new GameObject(IndustryRules.Name(kind)+" outcrop",typeof(MeshFilter),typeof(MeshRenderer));outcrop.transform.SetParent(visualRoot,false);
            outcrop.transform.SetPositionAndRotation(planet.transform.TransformPoint(point+n*2),planet.transform.rotation*rotation);
            outcrop.transform.localScale=kind==MaterialKind.Crystal?new Vector3(7,18,7):new Vector3(16,8,12);
            outcrop.GetComponent<MeshFilter>().sharedMesh=kind==MaterialKind.Biomass?WorldArt.Cone:WorldArt.Octahedron;
            outcrop.GetComponent<Renderer>().sharedMaterial=materials[(int)kind];deposit.outcrop=outcrop.transform;outcrop.SetActive(false);
            var marker=new GameObject("Survey pin - "+IndustryRules.Name(kind),typeof(MeshFilter),typeof(MeshRenderer),typeof(SphereCollider),typeof(DiscoveryMarker));marker.transform.SetParent(visualRoot,false);
            marker.GetComponent<MeshFilter>().sharedMesh=WorldArt.Octahedron;marker.GetComponent<Renderer>().sharedMaterial=materials[(int)kind];
            marker.GetComponent<SphereCollider>().radius=.7f;marker.GetComponent<DiscoveryMarker>().Configure(this,deposit.Id,true);
            deposit.marker=marker.transform;marker.SetActive(false);
        }
        public static Color DepositColor(MaterialKind kind)
        {
            switch(kind) {case MaterialKind.IronOre:return new Color(.77f,.30f,.19f);case MaterialKind.CopperOre:return new Color(.94f,.61f,.21f);
                case MaterialKind.Stone:return new Color(.67f,.69f,.66f);case MaterialKind.Biomass:return new Color(.27f,.72f,.28f);
                case MaterialKind.Promethium:return new Color(.55f,.32f,.13f);case MaterialKind.Metal:return new Color(.62f,.64f,.68f);
                case MaterialKind.Plasteel:return new Color(.42f,.63f,.74f);default:return new Color(.23f,.75f,.92f);}
        }
        public void SetOverlay(int mode)
        {
            Overlay=Mathf.Clamp(mode,0,7);
            if(Overlay==1 && planet!=null)Synchronize();
            else CancelLogisticsBuild();
            if(logisticsRenderer!=null)logisticsRenderer.enabled=Overlay==1 && !LogisticsBuildPending;
        }
        private void RevealExistingExtractorDeposits()
        {
            foreach(var building in buildings)
            {
                if(!IndustryRules.Extractor(building.Id))continue;
                var kind=ExtractionKind(building);var deposit=NearestDeposit(building.transform.position,kind,true);
                if(deposit!=null&&Vector3.Distance(deposit.Position,building.transform.position)<=250)deposit.Discovered=true;
            }
        }
        private bool Surveyed(Vector3 direction)
        {
            foreach(var cap in surveyCaps)if(planet.SurfaceDistance(cap.direction,direction)<=cap.radius)return true;
            return false;
        }
        private void RevealSurveyedLocations()
        {
            PrepareSurveyCaps();
            var newDepositCount=0;var newDiscoveryData=0;
            foreach(var deposit in deposits)if(!deposit.Discovered&&Surveyed(deposit.Direction)){deposit.Discovered=true;newDepositCount++;}
            foreach(var discovery in discoveries)if(!discovery.State.discovered&&Surveyed(discovery.Direction))
            {var state=discovery.State;state.discovered=true;discovery.State=state;newDiscoveryData++;}
            UpdateSurveyCoverage();
            if(newDiscoveryData>0)research?.AddInsight(newDiscoveryData*4f);
            if(newDepositCount > 0 || newDiscoveryData > 0)
            {
                surveyOverlay = -1;
                var menu = GetComponent<NewGameMenu>();
                menu?.AppendEventLog("Survey teams recorded new geological sites and planetary discoveries.");
                var depositsFound = newDepositCount == 1 ? "one new deposit" : $"{newDepositCount} new deposits";
                var sitesFound = newDiscoveryData == 1 ? "one new site" : $"{newDiscoveryData} new sites";
                menu?.ReportVoxSurvey(newDepositCount == 0 ? sitesFound : newDiscoveryData == 0 ? depositsFound : depositsFound + " and " + sitesFound);
            }
        }
        public void SurveyNow()
        {if(planet==null)return;Synchronize();RevealSurveyedLocations();}
        public void SelectDeposit(int id)
        {
            if(id<0||id>=deposits.Count||!deposits[id].Discovered)return;
            inspectedLocation=id;
            var deposit=deposits[id];var menu=GetComponent<NewGameMenu>();if(menu==null)return;
            var kind=IndustryRules.Name(deposit.Kind);var grade=deposit.Excavation!=null?$"Grade: {deposit.Excavation.grade:P1}":"Finite surface reserve";
            var details=$"{kind} deposit  /  SURVEY {deposit.Id:000}\nRemaining reserves: {deposit.Remaining:0} units\n{grade}\n\nPlace a compatible extractor within 250 m. Extraction is finite; an exhausted deposit stops supplying material.";
            menu.ShowDiscoveryPanel(kind.ToUpperInvariant()+" DEPOSIT",details,"LOCATE DEPOSIT",()=>{SetOverlay(deposit.Kind==MaterialKind.Promethium?7:(int)deposit.Kind+2);menu.FocusLocation(deposit.Direction);menu.AppendEventLog($"Survey #{deposit.Id}: {deposit.Remaining:0} units of {kind} remain.");});
        }
        public void BrowseDiscoveredLocation(int step)
        {
            var count=DiscoveryCount;if(count==0)return;
            var direction=step<0?-1:1;
            for(var offset=1;offset<=count;offset++)
            {
                var index=((inspectedLocation+direction*offset)%count+count)%count;
                if(index<deposits.Count){if(!deposits[index].Discovered)continue;SelectDeposit(index);return;}
                var discovery=discoveries[index-deposits.Count];if(!discovery.State.discovered)continue;
                SelectDiscovery(discovery.State.id);return;
            }
            GetComponent<NewGameMenu>()?.ShowDiscoveryPanel("SURVEY RECORDS","No sites have been revealed yet.\n\nBuild a Frontier Survey Outpost from the Colony construction page. Active outposts survey a 1,800 m surface radius. Build further outposts to explore beyond it.\n\nThe survey overlay shows grey unsurveyed regions and teal surveyed regions.","AWAITING SURVEY",null);
        }
        public void SelectDiscovery(int id)
        {
            var index=discoveries.FindIndex(d=>d.State.id==id);if(index<0)return;
            var discovery=discoveries[index];if(!discovery.State.discovered)return;
            inspectedLocation=deposits.Count+index;
            var kind=(DiscoveryKind)discovery.State.kind;var menu=GetComponent<NewGameMenu>();if(menu==null)return;
            var definition=Definition(kind);var profile=definition!=null?definition.Rewards:ExplorationRules.DefaultProfile(kind);
            if(research!=null)profile=research.DiscoveryRewards(profile);
            var description=definition!=null?definition.Description:kind==DiscoveryKind.AncientRuins?"Pre-Imperial stonework and sealed vaults are visible beneath the regolith.":
                kind==DiscoveryKind.ImperialInstallation?"An abandoned Imperial complex contains recoverable stores and damaged machinery.":
                kind==DiscoveryKind.HazardousRegion?"Survey instruments detect unstable ground and toxic emissions across this region.":
                "A rare archaeotech signal is coming from an intact buried cache.";
            var state=discovery.State;
            var body=$"{description}\n\nSurvey site {state.id+1:000}  /  {(state.resolved?"ALREADY INVESTIGATED":"UNRESOLVED")}"+
                (state.resolved?"\n"+RecordedOutcome(state):$"\nCautious recovery: +{state.reward*profile.cautiousMultiplier*ColonyRules.LegacyProjectSupplyScale:0.##} supplies\nRapid recovery: +{state.reward*profile.rapidMultiplier*ColonyRules.LegacyProjectSupplyScale:0.##} supplies, {profile.hazardChance:P0} incident risk\n"+ImpactSummary(profile.cautiousImpact));
            var cautious=definition!=null?definition.CautiousAction:"CAUTIOUS SEARCH";
            var rapid=definition!=null?definition.RapidAction:"RAPID SALVAGE";
            menu.ShowDiscoveryPanel(DiscoveryName(kind).ToUpperInvariant(),body,state.resolved?"OUTCOME RECORDED":"INVESTIGATE SITE",
                state.resolved?null:()=>menu.PromptEvent("Expedition / "+DiscoveryName(kind),description,
                    cautious+$"\n+{state.reward*profile.cautiousMultiplier*ColonyRules.LegacyProjectSupplyScale:0.##} supplies / safe",()=>ResolveDiscovery(id,false),
                    rapid+$"\n+{state.reward*profile.rapidMultiplier*ColonyRules.LegacyProjectSupplyScale:0.##} supplies / {profile.hazardChance:P0} risk",()=>ResolveDiscovery(id,true)),
                ()=>{SetOverlay(1);menu.FocusLocation(discovery.Direction);});
        }
        private static string ImpactSummary(WH30K.Sim.Environment.EnvironmentImpact impact)
        {
            if(impact.co2Delta==0&&impact.o2Delta==0&&impact.waterDelta==0&&impact.temperatureDelta==0)return "No direct planetary impact.";
            return $"Planetary impact: CO2 {impact.co2Delta:+0.#;-0.#;0}, water {impact.waterDelta:+0.#;-0.#;0}, temperature {impact.temperatureDelta:+0.00;-0.00;0} C";
        }
        private static string RecordedOutcome(DiscoveryRecord state)
            =>!state.outcomeRecorded?"Historical expedition complete; rewards cannot be recovered again.":
                $"{(state.rapidChoice?"Rapid":"Cautious")} expedition: {(state.outcomeHazard?"containment incident":"recovery complete")}\nSupplies recovered: {state.rewardReceived*ColonyRules.LegacyProjectSupplyScale:0.##}\n"+ImpactSummary(state.outcomeImpact);
        private void ResolveDiscovery(int id,bool rapid)
        {
            var index=discoveries.FindIndex(d=>d.State.id==id);if(index<0)return;
            var discovery=discoveries[index];
            var definition=Definition((DiscoveryKind)discovery.State.kind);
            var profile=definition!=null?definition.Rewards:ExplorationRules.DefaultProfile((DiscoveryKind)discovery.State.kind);
            if(research!=null)profile=research.DiscoveryRewards(profile);
            if(!ExplorationRules.TryResolve(discovery.State,planet.Seed,rapid,profile,out var state,out var outcome))return;
            environment?.ApplyImmediateImpact(outcome.impact);resources?.ModifyStockpile(outcome.reward * ColonyRules.LegacyProjectSupplyScale);
            discovery.State=state;
            research?.AddInsight(8f);
            GetComponent<NewGameMenu>()?.AppendEventLog(outcome.hazard?"Salvage caused a containment incident; the site is closed.":$"Expedition recovered {outcome.reward*ColonyRules.LegacyProjectSupplyScale:0.##} supplies. "+ImpactSummary(outcome.impact));
            SelectDiscovery(id);
        }
        public SurfaceDeposit NearestDeposit(Vector3 worldPosition,MaterialKind kind)
            =>NearestDeposit(worldPosition,kind,false);
        private SurfaceDeposit NearestDeposit(Vector3 worldPosition,MaterialKind kind,bool includeUndiscovered)
        {
            SurfaceDeposit best=null;var distance=float.MaxValue;
            foreach(var deposit in deposits) if(deposit.Kind==kind && deposit.Remaining>0&&(includeUndiscovered||deposit.Discovered))
            {var d=Vector3.Distance(deposit.Position,worldPosition);if(d<distance){best=deposit;distance=d;}}
            return best;
        }
        public bool CanPlaceExtractor(string id,Vector3 position)
        {
            if(!IndustryRules.Extractor(id))return true;
            if((id=="Mine" || id=="Quarry") && planet!=null
                && planet.EvaluateSurfacePoint(planet.transform.InverseTransformPoint(position).normalized).magnitude<=planet.SeaLevel+1)return false;
            foreach(var deposit in deposits)
                if(DepositSupportsBuilding(id,deposit.Kind)&&deposit.Discovered&&deposit.Remaining>0&&(deposit.Position-position).sqrMagnitude<=250*250)return true;
            return false;
        }
        public int DefaultExtractionFocus(string id, Vector3 position)
        {
            if (id != "Mine") return 0;
            SurfaceDeposit nearest = null;
            var distance = 250f * 250f;
            foreach (var deposit in deposits)
            {
                if (!deposit.Discovered || deposit.Remaining <= 0 || !DepositSupportsBuilding(id, deposit.Kind)) continue;
                var candidateDistance = (deposit.Position - position).sqrMagnitude;
                if (candidateDistance > distance) continue;
                nearest = deposit; distance = candidateDistance;
            }
            // Reuse the persisted mineral focus, so existing saves need no migration.
            return id == "Mine" && nearest != null
                ? nearest.Kind == MaterialKind.CopperOre ? 1 : nearest.Kind == MaterialKind.Crystal ? 2 : 0
                : 0;
        }
        public bool ClearOfMineralVeins(Vector3 position,float footprint)
        {
            if(planet==null)return true;
            var n=planet.transform.InverseTransformPoint(position).normalized;
            foreach(var deposit in deposits)
            {
                if(!deposit.Discovered || deposit.Excavation==null || deposit.Excavation.layoutVersion==0)continue;
                Coordinates(deposit,n,out var distance,out var bearing);
                if(!MiningRules.OutsideVein(deposit.Excavation,distance,bearing,footprint))return false;
            }
            return true;
        }
        public Vector3 MineRimDirection(string id,Vector3 direction,float footprint)
        {
            if(id!="Mine"&&id!="Quarry" || planet==null)return direction;
            SurfaceDeposit best=null;var nearest=float.MaxValue;
            foreach(var deposit in deposits)
            {
                if(!deposit.Discovered || deposit.Remaining<=0 || !DepositSupportsBuilding(id,deposit.Kind))continue;
                var d=(deposit.Direction-direction).magnitude*planet.Radius;
                if(d<nearest){best=deposit;nearest=d;}
            }
            if(best==null || nearest>250 || best.Excavation.layoutVersion==0)return direction;
            Coordinates(best,direction,out var distance,out var bearing);
            if(MiningRules.OutsideVein(best.Excavation,distance,bearing,footprint))return direction;
            if(distance<.1f)bearing=best.Excavation.bearing;
            Basis(best.Direction,out var right,out var forward);var angle=bearing*Mathf.Deg2Rad;
            // One metre of tolerance accounts for tangent projection back to the sphere.
            var rim=MiningRules.VeinRadius(best.Excavation,bearing)*(1+(footprint+13)/Mathf.Min(best.Excavation.majorRadius,best.Excavation.minorRadius));
            return (best.Direction+(right*Mathf.Cos(angle)+forward*Mathf.Sin(angle))*rim/planet.Radius).normalized;
        }
        public bool CanPlaceWaterPump(string id,Vector3 position)
        {
            if(id!="GroundwaterPump"&&id!="DesalinationPump")return true;
            if(planet==null)return false;
            var local=planet.transform.InverseTransformPoint(position);
            var direction=local.normalized;
            if(local.magnitude<=planet.SeaLevel+1f)return false;
            if(id=="GroundwaterPump")return true;
            var tangent=Vector3.Cross(direction,Mathf.Abs(direction.y)>.95f?Vector3.right:Vector3.up).normalized;
            var bitangent=Vector3.Cross(direction,tangent).normalized;
            for(var i=0;i<16;i++)
            {
                var angle=i*Mathf.PI*2f/16f;
                var nearby=(direction+(tangent*Mathf.Cos(angle)+bitangent*Mathf.Sin(angle))*(140f/planet.Radius)).normalized;
                if(planet.EvaluateSurfacePoint(nearby).magnitude<=planet.SeaLevel+.5f)return true;
            }
            return false;
        }
        private static bool DepositSupportsBuilding(string id,MaterialKind kind)
            =>id=="Mine"?(kind==MaterialKind.IronOre||kind==MaterialKind.CopperOre||kind==MaterialKind.Crystal)
            :id=="Quarry"?kind==MaterialKind.Stone:id=="Harvester"?kind==MaterialKind.Biomass
            :id=="PromethiumExtractor"&&kind==MaterialKind.Promethium;
        public float[] Totals()
        {var total=new float[IndustryRules.MaterialCount];FillTotals(total);return total;}
        public void FillTotals(float[] total)
        {Array.Clear(total,0,total.Length);if(expeditionCargo!=null)Array.Copy(expeditionCargo,total,Math.Min(total.Length,expeditionCargo.Length));foreach(var stock in stocks) for(var k=0;k<Mathf.Min(total.Length,stock.quantities.Length);k++) total[k]+=stock.quantities[k];foreach(var flight in flights) if(!flight.state.food && flight.state.kind<total.Length)total[flight.state.kind]+=flight.state.amount;}
        public string Status(ManagedBuilding building)
        {
            Synchronize();var index=Array.IndexOf(buildings,building);if(index<0 || !IndustryRules.Facility(building.Id)) return "";
            if(building.Id=="Cognitorium")return $"MECHANICUM COGNITORIUM / {(building.State.offline?"SUSPENDED":"ACTIVE")}\nResearch data {research?.Insight:0.0} / {ResearchRules.MaximumInsight:0}\nGeneration {research?.CurrentRate:0.000} insight/s\nStaffing and grid power govern research speed. Discoveries award one-time insight.";
            var text=new StringBuilder();
            var recipe = Profile(building.Id);
            if(building.Id=="Farm") return FarmReport(index);
            if(building.Id=="Manufactorum" || building.Id=="StorageDepot") text.Append(FacilitySummary(index));
            else if(IndustryRules.Extractor(building.Id))
            {
                var kind=ExtractionKind(building);var deposit=(building.Id=="Mine"||building.Id=="Quarry")?DepositFor(building):NearestDeposit(building.transform.position,kind);
                var distance=deposit==null?float.MaxValue:Vector3.Distance(building.transform.position,deposit.Position);
                text.Append(distance<=250?deposit.Excavation==null?$"{IndustryRules.Name(kind)}: {deposit.Remaining:0} remaining\n":
                    $"{IndustryRules.Name(kind)}: {deposit.Remaining:0} lots\nAccessible {MiningRules.Remaining(deposit.Excavation,true):0} / limited {deposit.Remaining-MiningRules.Remaining(deposit.Excavation,true):0}\n":$"No {IndustryRules.Name(kind).ToLowerInvariant()} within 250 m.\n");
            }
            else if(recipe!=null && recipe.Inputs!=null && recipe.Inputs.Length>0 && recipe.Outputs!=null && recipe.Outputs.Length>0)
            {
                for(var i=0;i<recipe.Inputs.Length;i++)text.Append((i>0?" + ":"")+$"{recipe.Inputs[i].amount:0.#} {IndustryRules.Name(recipe.Inputs[i].material)}");
                text.Append(" -> ");
                for(var i=0;i<recipe.Outputs.Length;i++)text.Append((i>0?" + ":"")+$"{recipe.Outputs[i].amount:0.#} {IndustryRules.Name(recipe.Outputs[i].material)}");
                text.Append('\n');
            }
            else text.Append(building.Id=="GroundwaterPump"?"Draws fresh water from the aquifer\n":building.Id=="DesalinationPump"?"Desalinates seawater; requires an ocean shore\n":building.Id=="Smelter"?"2 ore + 1 biomass -> 1 metal\n":building.Id=="Refinery"?"2 iron ore + 1 promethium -> 1 plasteel\n":building.Id=="Manufactorum"?"1 plasteel -> 1 component\n":building.Id=="PromethiumGenerator"?"Fuelled power generation\n":building.Id=="StorageDepot"?"Regional material storage\n":"2 metal + 1 stone + 0.5 crystal -> 1 component\n");
            for(var k=0;k<IndustryRules.MaterialCount;k++) if(stocks[index].quantities[k]>0) text.Append($"{IndustryRules.Name((MaterialKind)k)} {stocks[index].quantities[k]:0.#}  ");
            var componentIndex=index<networkComponent.Length?networkComponent[index]:-1;
            var powerEfficiency=componentIndex>=0&&componentIndex<networkPowerFraction.Length?networkPowerFraction[componentIndex]:0;
            var staffEfficiency=WorkersRequired>0?Mathf.Clamp01(WorkersAvailable/WorkersRequired):1;
            var focusMultiplier=building.State.focus==1?1.4f:building.State.focus==2?.75f:1;
            var outputEfficiency=staffEfficiency*powerEfficiency*(building.Id=="Manufactorum"?ManufactorumRules.ProductionScale(building.State):1+building.State.level*.25f)*focusMultiplier/5f;
            outputEfficiency*=research!=null?research.ProcessorWorkMultiplier(building.Id):1f;
            text.Append($"\nEfficiency {staffEfficiency*powerEfficiency:P0}");
            if(IndustryRules.Extractor(building.Id))
            {
                var kind=ExtractionKind(building);var deposit=(building.Id=="Mine"||building.Id=="Quarry")?DepositFor(building):NearestDeposit(building.transform.position,kind);
                var richness=deposit!=null&&deposit.Excavation!=null?MiningRules.RichnessMultiplier(kind,deposit.Excavation.grade):1;
                var cycle=Profile(building.Id)!=null?Profile(building.Id).ExtractionPerWorkCycle:1;
                text.Append($"  Output {IndustryRules.Name(kind)} +{cycle*richness*outputEfficiency:0.00}/s");
            }
            else if(building.Id=="GroundwaterPump"||building.Id=="DesalinationPump")
                text.Append($"  Output fresh water +{(building.Id=="GroundwaterPump" ? WaterRules.GroundwaterPerSecond : WaterRules.DesalinationPerSecond)*staffEfficiency*powerEfficiency*(building.Id=="Manufactorum"?ManufactorumRules.ProductionScale(building.State):1+building.State.level*.25f)*focusMultiplier*(research!=null?research.PumpYieldMultiplier(building.Id):1f):0.00}/s");
            else if(building.Id=="Manufactorum")
            {
                var config=Configuration(index);var count=FacilityRules.ActiveCount(config);
                text.Append($"  Output/line {(count>0?outputEfficiency/count:0):0.000}/s when supplied");
            }
            else if(Profile(building.Id)!=null&&Profile(building.Id).Outputs!=null&&Profile(building.Id).Outputs.Length>0)
                foreach(var output in Profile(building.Id).Outputs)text.Append($"  Output {IndustryRules.Name(output.material)} +{output.amount*outputEfficiency:0.00}/s");
            else if(building.Id=="Smelter")text.Append($"  Output refined metal +{outputEfficiency:0.00}/s");
            else if(building.Id=="Works")text.Append($"  Output components +{outputEfficiency:0.00}/s");
            else if(building.Id=="PromethiumGenerator")
            {var profile=Profile(building.Id);var multiplier=research!=null?research.GeneratorOutputMultiplier:1f;var output=(profile!=null?profile.PowerOutput:28f)*multiplier;var fuel=(profile!=null?profile.GeneratorFuelPerSecond:.168f)*multiplier;
                text.Append($"  Power {output:0.#} MW  Fuel -{fuel*staffEfficiency:0.000}/s");}
            var peers=0;if(networkComponent!=null&&index<networkComponent.Length&&networkComponent[index]>=0)
                for(var i=0;i<networkComponent.Length;i++)if(i!=index&&networkComponent[i]==networkComponent[index])peers++;
            var linked=peers>0||connectedToCore!=null&&index<connectedToCore.Length&&connectedToCore[index];
            text.Append(linked?"\nLOGISTICS NETWORK: CONNECTED":"\nLOGISTICS NETWORK: ISOLATED");
            if(building.State.offline)text.Append("\nSTATUS: SUSPENDED");
            else if(index>=networkComponent.Length||networkComponent[index]<0)text.Append("\nSTATUS: NO LOGISTICS LINK");
            else if(IndustryRules.Extractor(building.Id)&&!CanPlaceExtractor(building.Id,building.transform.position))text.Append("\nSTATUS: NO VALID DEPOSIT IN RANGE");
            else if(building.Id=="DesalinationPump"&&!CanPlaceWaterPump(building.Id,building.transform.position))text.Append("\nSTATUS: PLACE BESIDE THE OCEAN");
            else if(networkPowerFraction.Length<=networkComponent[index]||networkPowerFraction[networkComponent[index]]<=.001f)text.Append("\nSTATUS: POWER SHORTAGE");
            else if(WorkersAvailable+0.01f<WorkersRequired)text.Append("\nSTATUS: WORKFORCE LIMITED");
            else if(building.Id=="PromethiumGenerator"&&stocks[index].quantities[(int)MaterialKind.Promethium]<.001f)text.Append("\nSTATUS: AWAITING PROMETHIUM");
            else if(building.Id=="Smelter"&&((stocks[index].quantities[0]<2&&stocks[index].quantities[1]<2)||stocks[index].quantities[3]<1))text.Append("\nSTATUS: INPUT SHORTAGE");
            else if(building.Id=="Works"&&(stocks[index].quantities[5]<2||stocks[index].quantities[2]<1||stocks[index].quantities[4]<.5f))text.Append("\nSTATUS: INPUT SHORTAGE");
            else if(building.Id=="Refinery"&&(stocks[index].quantities[0]<2||stocks[index].quantities[7]<1))text.Append("\nSTATUS: INPUT SHORTAGE");
            else if(building.Id=="Manufactorum")text.Append("\nSTATUS: SEE PRODUCT LINES");
            else if(recipe!=null && recipe.Inputs!=null && Array.Exists(recipe.Inputs, input=>stocks[index].quantities[(int)input.material]+.0001f<input.amount))text.Append("\nSTATUS: INPUT SHORTAGE");
            else if(recipe==null && building.Id=="Manufactorum"&&stocks[index].quantities[8]<1)text.Append("\nSTATUS: AWAITING PLASTEEL");
            else text.Append("\nSTATUS: OPERATIONAL");
            text.Append($"\nPower {PowerGenerated:0.#} / {PowerDemand:0.#} MW  |  Workers {WorkersAvailable:0}/{WorkersRequired:0}");return text.ToString();
        }
        private MaterialKind ExtractionKind(ManagedBuilding building) => building.Id=="Quarry"?MaterialKind.Stone:building.Id=="Harvester"?MaterialKind.Biomass:building.Id=="PromethiumExtractor"?MaterialKind.Promethium:
            building.State.focus==1?MaterialKind.CopperOre:building.State.focus==2?MaterialKind.Crystal:MaterialKind.IronOre;
        private void Synchronize()
        {
            if(placement==null) return;var current=placement.Buildings;
            if(ReferenceEquals(current,buildings)) return;
            RemapBuildings(current);
        }
        private static bool LogisticsNode(string id) => IndustryRules.Facility(id) || id == "HabBlock" || id == "Outpost" || id == "Factory";
        private void RebuildLogisticsNetwork()
        {
            const float linkRange=850f;
            networkComponent=new int[buildings.Length];connectedToCore=new bool[buildings.Length];
            for(var i=0;i<networkComponent.Length;i++)networkComponent[i]=-1;
            var component=0;var queue=new Queue<int>();
            for(var root=0;root<buildings.Length;root++)
            {
                if(!LogisticsNode(buildings[root].Id)||networkComponent[root]>=0)continue;
                networkComponent[root]=component;queue.Enqueue(root);
                while(queue.Count>0)
                {
                    var current=queue.Dequeue();
                    for(var next=0;next<buildings.Length;next++)
                    {
                        var range=buildings[current].Id=="StorageDepot"||buildings[next].Id=="StorageDepot"?1600f:linkRange;
                        if(next==current||!LogisticsNode(buildings[next].Id)||networkComponent[next]>=0
                            ||(buildings[current].transform.position-buildings[next].transform.position).sqrMagnitude>range*range)continue;
                        networkComponent[next]=component;queue.Enqueue(next);
                    }
                }
                component++;
            }
            networkPowerDemand=new float[component];networkPowerSupply=new float[component];networkPowerFraction=new float[component];networkFuelLoad=new float[component];
            // Depots store actual cargo rather than inflating every facility's local buffer.
            var coreBuildings=settlement!=null?settlement.CoreBuildings:null;
            var componentHasCore=new bool[component];
            if(coreBuildings!=null)
                for(var i=0;i<buildings.Length;i++)
                {
                    if(networkComponent[i]<0)continue;
                    foreach(var core in coreBuildings)
                    {
                        var coreRange=buildings[i].Id=="StorageDepot"?1600f:linkRange;
                        if(core!=null && (core.Id == "ColonyShip" ? core.ShipFootprintDistance(buildings[i].transform.position) <= coreRange
                            : (buildings[i].transform.position-core.transform.position).sqrMagnitude<=coreRange*coreRange))
                        {componentHasCore[networkComponent[i]]=true;break;}
                    }
                }
            for(var i=0;i<buildings.Length;i++)if(networkComponent[i]>=0)connectedToCore[i]=componentHasCore[networkComponent[i]];
            // The continuous ship/core grid joins every attached cluster; count its reactor once.
            var sharedCoreNetwork = -1;
            for (var i = 0; i < buildings.Length; i++) if (connectedToCore[i])
            {
                if (sharedCoreNetwork < 0) sharedCoreNetwork = networkComponent[i];
                networkComponent[i] = sharedCoreNetwork;
            }
            logisticsVisualDirty=true;
        }
        public float OperatingFraction(ManagedBuilding building)
        {
            Synchronize();var index=Array.IndexOf(buildings,building);
            if(index<0||building==null||building.State.offline)return 0;
            var staffing=WorkersRequired>0?Mathf.Clamp01(WorkersAvailable/WorkersRequired):1;
            var component=index<networkComponent.Length?networkComponent[index]:-1;
            var power=component>=0&&component<networkPowerFraction.Length?networkPowerFraction[component]:0;
            var focus=building.State.focus==1?1.4f:building.State.focus==2?.75f:1f;
            return staffing*power*(building.Id=="Manufactorum"?ManufactorumRules.ProductionScale(building.State):1+building.State.level*.25f)*focus;
        }
        private void Update()
        {
            Simulate(ColonyClock.DeltaTime);
            LogisticsSegmentsLastUpdate=LogisticsPairsLastUpdate=0;
            LogisticsBuildMillisecondsLastUpdate=0;
            if(planet!=null) { if(Overlay==1)AdvanceLogisticsVisual();UpdateSurvey();PresentMiningDecision(); }
        }
        public void Simulate(float dt)
        {
            // Bound recipe/transport integration so long simulation steps still deliver before buffers fill.
            if(ColonyRules.IsFinite(dt) && dt>1){while(dt>1){Simulate(1);dt-=1;}if(dt>0)Simulate(dt);return;}
            if(planet==null || !ColonyRules.IsFinite(dt) || dt<0 || conflict.active) return;Synchronize();RefreshFarmEntities();
            if(dt>0)
            {
                foundingFoodDelivery=0;
                var foundingComponentsBefore=producedTotals[(int)MaterialKind.Components];
                var foundingReservesHealthy=resources.Water>0 && resources.Food>0;
                ResetPitOperations();
                rateWindowSeconds+=dt;
                DepositUsageSeconds += dt;
                var coreGeneration=0f;
                foreach(var core in settlement.CoreBuildings)
                    if(!core.State.offline && (core.Id=="ColonyShip" || core.Id=="Utility" || core.Id=="Factory"))
                        coreGeneration=settlement.ShipLayoutVersion >= 2 ? ShipRules.ReactorMW : ShipRules.LegacyReactorMW;
                const float generatorCapacity=28f;
                const float generatorFuelPerPowerSecond=.006f;
                WorkersRequired=0;
                foreach(var building in settlement.CoreBuildings) if(!building.State.offline)WorkersRequired+=building.Economy.jobs;
                for(var i=0;i<buildings.Length;i++)if(!buildings[i].State.offline)WorkersRequired+=buildings[i].Economy.jobs;
                var staffing=WorkersRequired>0?Mathf.Clamp01(WorkersAvailable/WorkersRequired):0;
                Array.Clear(networkPowerDemand,0,networkPowerDemand.Length);Array.Clear(networkPowerSupply,0,networkPowerSupply.Length);
                var coreComponent=-2;
                for(var i=0;i<connectedToCore.Length;i++)if(connectedToCore[i]){coreComponent=networkComponent[i];break;}
                if(coreComponent>=0)
                {
                    var coreDemand=0f;
                    foreach(var building in settlement.CoreBuildings)if(!building.State.offline)
                        coreDemand+=building.Id=="Factory"?5:building.Id=="Utility"?2:1;
                    networkPowerDemand[coreComponent]+=coreDemand;
                    networkPowerSupply[coreComponent]+=coreGeneration;
                }
                for(var i=0;i<buildings.Length;i++)
                {
                    var id=buildings[i].Id;if(!IndustryRules.Facility(id)||buildings[i].State.offline)continue;
                    var component=networkComponent[i];if(component<0)continue;
                    var demand=Profile(id)!=null?Profile(id).PowerDemand:id=="StorageDepot"?1f:id=="PromethiumGenerator"?2f
                        :id=="GroundwaterPump"||id=="DesalinationPump"?4f:IndustryRules.Extractor(id)?4f:6f;
                    if(id=="Manufactorum") demand*=ManufactorumRules.ProductionScale(buildings[i].State);
                    networkPowerDemand[component]+=demand;
                    if(id=="PromethiumGenerator"&&stocks[i].quantities[(int)MaterialKind.Promethium]>0)
                    {
                        var profile=Profile(id);var researchMultiplier=research!=null?research.GeneratorOutputMultiplier:1f;
                        var output=(profile!=null?profile.PowerOutput:generatorCapacity)*researchMultiplier;
                        var fuelRate=(profile!=null?profile.GeneratorFuelPerSecond:generatorFuelPerPowerSecond*generatorCapacity)*researchMultiplier;
                        var fuelLimitedOutput=IndustryRules.FuelLimitedPower(output,fuelRate,stocks[i].quantities[(int)MaterialKind.Promethium],staffing,dt);
                        networkPowerSupply[component]+=fuelLimitedOutput;
                    }
                }
                PowerDemand=coreComponent < 0 && coreGeneration > 0 ? 1 : 0;PowerGenerated=coreComponent < 0 ? coreGeneration : 0;
                for(var c=0;c<networkPowerDemand.Length;c++)
                {
                    PowerDemand+=networkPowerDemand[c];PowerGenerated+=networkPowerSupply[c];
                    networkPowerFraction[c]=IndustryRules.PowerFraction(networkPowerSupply[c],networkPowerDemand[c]);
                    networkFuelLoad[c]=Mathf.Max(0,networkPowerDemand[c]-(c==coreComponent?coreGeneration:0));
                }
                var activeIndustryImpact=WH30K.Sim.Environment.EnvironmentImpact.Zero;
                var waterProduced=0f;
                for(var i=0;i<buildings.Length;i++)
                {
                    var pump=buildings[i];
                    if(pump.State.offline||(pump.Id!="GroundwaterPump"&&pump.Id!="DesalinationPump"))continue;
                    var component=networkComponent[i];var power=component>=0?networkPowerFraction[component]:0;
                    if(power<=0)continue;
                    var focus=pump.State.focus==1?1.4f:pump.State.focus==2?.75f:1f;
                    waterProduced+=WaterRules.PumpOutput(pump.Id,staffing,power,pump.State.level,focus)*(research!=null?research.PumpYieldMultiplier(pump.Id):1f)*dt;
                }
                if(waterProduced>0)resources?.AddWater(waterProduced);
                if(settlement!=null&&resources!=null)
                    resources.ConsumeWater(WaterRules.Demand(settlement.Population)*dt);
                SimulateFarms(dt, staffing);
                foundingReservesHealthy &= resources.Water>0 && resources.Food>0;
                for(var i=0;i<buildings.Length;i++)
                {
                    if(buildings[i].Id!="PromethiumGenerator"||buildings[i].State.offline)continue;
                    var component=networkComponent[i];
                    if(component<0)continue;var ratio=networkPowerFraction[component];if(ratio<=0)continue;
                    var researchMultiplier=research!=null?research.GeneratorOutputMultiplier:1f;
                    var output=(Profile(buildings[i].Id)!=null?Profile(buildings[i].Id).PowerOutput:generatorCapacity)*researchMultiplier;
                    var fuelRate=(Profile(buildings[i].Id)!=null?Profile(buildings[i].Id).GeneratorFuelPerSecond:generatorFuelPerPowerSecond*generatorCapacity)*researchMultiplier;
                    var fuel=IndustryRules.GeneratorFuelForLoad(output,fuelRate,
                        stocks[i].quantities[(int)MaterialKind.Promethium],staffing,dt,ref networkFuelLoad[component]);
                    stocks[i].quantities[(int)MaterialKind.Promethium]-=fuel;
                    RecordConsumption((int)MaterialKind.Promethium,fuel);
                    if(fuel>0&&environment!=null)
                    {
                        var impact=Profile(buildings[i].Id)!=null?Profile(buildings[i].Id).OperatingImpact:new WH30K.Sim.Environment.EnvironmentImpact{co2Delta=.55f,temperatureDelta=.00015f};
                        var fraction=fuel/Mathf.Max(.0001f,fuelRate*dt);activeIndustryImpact+=impact.Scale(fraction);
                    }
                }
                for(var i=0;i<buildings.Length;i++)
                {
                    var building=buildings[i];if(!IndustryRules.Facility(building.Id) || building.State.offline) continue;
                    if(building.Id=="Farm"||building.Id=="PromethiumGenerator"||building.Id=="StorageDepot"||building.Id=="GroundwaterPump"||building.Id=="DesalinationPump")continue;
                    var component=networkComponent[i];var power=component>=0?networkPowerFraction[component]:0;
                    if(power<=0)continue;
                    var focus=building.Id=="Mine"?1:building.State.focus==1?1.4f:building.State.focus==2?.75f:1;
                    var workAdded=dt*staffing*power*(building.Id=="Manufactorum"?ManufactorumRules.ProductionScale(building.State):1+building.State.level*.25f)*focus*(research!=null?research.ProcessorWorkMultiplier(building.Id):1f);
                    var operatedCycles=0f;
                    if(building.Id=="Manufactorum")
                    {
                        Array.Clear(recipeProduced,0,recipeProduced.Length);Array.Clear(recipeConsumed,0,recipeConsumed.Length);
                        var upkeep = ManufactorumRules.Upkeep(building.State.manufactorum,dt,staffing*power);
                        if(upkeep>0 && !TrySpendConstruction(upkeep,0))workAdded*=building.State.manufactorum.inheritedCapacity/ManufactorumRules.ProductionScale(building.State);
                        operatedCycles=FacilityRules.Advance(Configuration(i),stocks[i].quantities,workAdded,recipeProduced,recipeConsumed,incoming,i*IndustryRules.MaterialCount);
                        for(var k=0;k<IndustryRules.MaterialCount;k++){RecordProduction(k,recipeProduced[k]);RecordConsumption(k,recipeConsumed[k]);}
                    }
                    else stocks[i].work+=workAdded;
                    MarkPitOperating(building,workAdded);
                    while(building.Id!="Manufactorum" && stocks[i].work>=5)
                    {
                        stocks[i].work-=5;
                        if(IndustryRules.Extractor(building.Id))
                        {
                            var kind=ExtractionKind(building);var deposit=ExtractionDeposit(building);
                            var capacity=StoreCapacity(i,kind);
                            if(deposit!=null && deposit.Remaining>.0001f && stocks[i].quantities[(int)kind]<capacity
                                && Vector3.Distance(building.transform.position,deposit.Position)<=250)
                            {
                                var outputRate=Profile(building.Id)!=null?Profile(building.Id).ExtractionPerWorkCycle:1f;
                                if(kind==MaterialKind.Biomass)
                                {
                                    var forestAge=TreeEcologyRules.Maturity(planet.Seed,0,deposit.Id,0,WH30K.Sim.ColonyClock.SimulationSeconds);
                                    outputRate*=TreeEcologyRules.BiomassYield(forestAge)/TreeEcologyRules.BiomassYield(1f);
                                }
                                if(deposit.Excavation!=null)outputRate*=MiningRules.RichnessMultiplier(kind,deposit.Excavation.grade);
                                var beforeReserve = deposit.Remaining;
                                var amount=Mathf.Min(outputRate,capacity-stocks[i].quantities[(int)kind]);
                                if(deposit.Excavation==null) {amount=Mathf.Min(amount,deposit.Remaining);deposit.Remaining-=amount;stocks[i].quantities[(int)kind]+=amount;}
                                else {amount=Excavate(deposit,i,amount);stocks[i].quantities[(int)kind]+=amount;}
                                deposit.Usage.Record(DepositUsageSeconds, beforeReserve - deposit.Remaining);
                                RecordProduction((int)kind,amount);
                                if(amount>0)operatedCycles+=amount/Mathf.Max(.0001f,outputRate);
                            }
                        }
                        else if(building.Id!="PromethiumGenerator"&&building.Id!="StorageDepot")
                        {
                            var profile=Profile(building.Id);
                            Array.Copy(stocks[i].quantities,recipeBefore,IndustryRules.MaterialCount);
                            var processed=profile!=null?profile.TryProcess(stocks[i].quantities,StoreCapacity(i,MaterialKind.Components)):
                                IndustryRules.Process(building.Id,stocks[i].quantities);
                            if(processed){RecordRecipeDelta(recipeBefore,stocks[i].quantities);operatedCycles++;}
                        }
                        if(conflict.active){stocks[i].work=Mathf.Min(stocks[i].work,5);break;}
                    }
                    if(conflict.active)break;
                    if(operatedCycles>0)
                    {
                        var profile=Profile(building.Id);
                        var impact=profile!=null?profile.OperatingImpact:building.Impact;
                        activeIndustryImpact+=impact.Scale(Mathf.Clamp01(operatedCycles*5f/Mathf.Max(.001f,dt)));
                    }
                }
                var policyImpact=WH30K.Sim.Environment.EnvironmentRules.ApplyPolicy(activeIndustryImpact,settlement.IndustryPolicyModifier);
                if(environment!=null&&(policyImpact.co2Delta!=0||policyImpact.o2Delta!=0||policyImpact.waterDelta!=0||policyImpact.temperatureDelta!=0))
                    environment.ApplyIndustryImpact(policyImpact,dt);
                AdvanceCargo(dt);dispatchTimer+=dt;if(dispatchTimer>=1){dispatchTimer%=1;Dispatch();}
                var wasEstablished=founding.completed;
                FoundingObjectives.Observe(ref founding,dt,waterProduced,WaterRules.Demand(settlement.Population)*dt,
                    foundingFoodDelivery,FarmRules.MonthlyDemand(settlement.Population)*dt/(float)ColonyCalendar.SecondsPerMonth,
                    producedTotals[(int)MaterialKind.Components]-foundingComponentsBefore,founding.pendingComponentUse,
                    foundingReservesHealthy && !HasInfiniteConstructionSupplies);
                founding.pendingComponentUse=0;
                if(!wasEstablished && founding.completed && founding.enabled)
                    GetComponent<NewGameMenu>()?.AppendEventLog("Colony established. Orbital cargo canister located: open COLONY GUIDE to recover 20 components.");
                if(rateWindowSeconds>=5)
                {
                    for(var k=0;k<IndustryRules.MaterialCount;k++)
                    {productionRates[k]=producedWindow[k]/rateWindowSeconds;consumptionRates[k]=consumedWindow[k]/rateWindowSeconds;producedWindow[k]=consumedWindow[k]=0;}
                    rateWindowSeconds=0;
                }
                research?.Simulate(dt);
            }
            RefreshFarmEntities();FlushExcavations();
            AdvancePitHaulers(dt);
        }
        private void RecordRecipeDelta(float[] before,float[] after)
        {
            for(var k=0;k<IndustryRules.MaterialCount;k++)
            {
                var delta=after[k]-before[k];if(delta>0)RecordProduction(k,delta);else RecordConsumption(k,-delta);
            }
        }
        private void RecordProduction(int kind,float amount){if(amount<=0)return;producedWindow[kind]+=amount;producedTotals[kind]+=amount;}
        private void RecordConsumption(int kind,float amount){if(amount<=0)return;consumedWindow[kind]+=amount;consumedTotals[kind]+=amount;if(kind==(int)MaterialKind.Components && FoundingObjectives.Stage(founding)==3)founding.pendingComponentUse+=amount;}
        private IndustrialBuildingProfile Profile(string id)=>profiles.TryGetValue(id,out var profile)?profile:null;
        private bool AcceptsInput(int index, MaterialKind kind)
        {
            var id=buildings[index].Id;
            if(id=="Manufactorum")return FacilityRules.Accepts(Configuration(index),kind);
            var profile = Profile(id);
            if (profile != null && profile.Inputs != null && profile.Inputs.Length > 0)
                return Array.Exists(profile.Inputs, input => input.material == kind && input.amount > 0);
            return IndustryRules.Accepts(id, kind);
        }
        private void Dispatch()
        {
            RefreshIncoming();DispatchFarmFood();
            // Ship stores must feed connected recipes as well as pay construction.
            // Otherwise the first farm has no components and surplus raw cargo is stranded.
            if (expeditionCargo != null)
                for (var i=0; i<buildings.Length; i++)
                {
                    if (buildings[i].State.offline || buildings[i].Id=="StorageDepot" || !connectedToCore[i]) continue;
                    for (var k=0; k<IndustryRules.MaterialCount; k++)
                    {
                        var kind=(MaterialKind)k;
                        if (!AcceptsInput(i,kind)) continue;
                        var amount=Mathf.Min(3,Mathf.Min(expeditionCargo[k],DeliveryRoom(i,kind)));
                        if (amount<=0) continue;
                        expeditionCargo[k]-=amount; stocks[i].quantities[k]+=amount;
                    }
                }
            var coreFactory=CoreFactory();
            for(var source=0;source<buildings.Length && flights.Count<64;source++)
            {
                if(buildings[source].State.offline) continue;
                for(var k=0;k<IndustryRules.MaterialCount && flights.Count<64;k++)
                {
                    var id=buildings[source].Id;
                    var producer=id=="Mine"&&(k==0||k==1||k==4) || id=="Quarry"&&k==2 || id=="Harvester"&&k==3
                        || id=="PromethiumExtractor"&&k==7 || id=="Smelter"&&k==5 || id=="Works"&&k==6
                        || id=="Refinery"&&k==8 || id=="Manufactorum"&&(k==4||k==5||k==6||k==8)
                        || id=="Manufactorum"&&(k==10||k==11) || k==9 && id!="Farm"
                        || id=="StorageDepot"&&stocks[source].quantities[k]>0;
                    if(!producer) continue;
                    var kind=(MaterialKind)k;var amount=Mathf.Min(3,stocks[source].quantities[k]);if(amount<.5f) continue;
                    var target=-2;var distance=float.MaxValue;
                    foreach(var i in consumers[k])
                    {
                        if(i==source || buildings[i].State.offline || !IndustryRules.ShareLogistics(networkComponent[source],networkComponent[i])
                            || buildings[i].Id=="StorageDepot" || !AcceptsInput(i,kind)) continue;
                        var demand=DeliveryRoom(i,kind);
                        var d=(buildings[source].transform.position-buildings[i].transform.position).sqrMagnitude;
                        if(demand>=.5f && d<distance){target=i;distance=d;}
                    }
                    // Direct ingredient demand wins; surplus moves once into real depot storage.
                    if(target==-2 && id!="StorageDepot") foreach(var i in consumers[k])
                    {
                        if(i==source || buildings[i].Id!="StorageDepot" || buildings[i].State.offline
                            || !IndustryRules.ShareLogistics(networkComponent[source],networkComponent[i]))continue;
                        var d=(buildings[source].transform.position-buildings[i].transform.position).sqrMagnitude;
                        if(DeliveryRoom(i,kind)>=.5f&&d<distance){target=i;distance=d;}
                    }
                    // Any remaining production can be hauled to the crashed vessel, even
                    // when the producer is outside the local building logistics network.
                    if(target==-2 && HasExpeditionCargo && ShipStorageRoom(kind)>=.5f) target=-3;
                    if(target==-2) continue;
                    if(target>=0) amount=Mathf.Min(amount,DeliveryRoom(target,kind));
                    if(target==-3) amount=Mathf.Min(amount,ShipStorageRoom(kind));
                    if(amount<.5f) continue;
                    stocks[source].quantities[k]-=amount;
                    if(target>=0)incoming[target*IndustryRules.MaterialCount+k]+=amount;
                    AddFlight(new CargoStock{source=source,target=target,kind=k,amount=amount});
                }
            }
        }
        private float StoreCapacity(int index,MaterialKind kind)
            => buildings[index].Id=="Manufactorum" && (kind==MaterialKind.Components||kind==MaterialKind.Metal||kind==MaterialKind.Plasteel||kind==MaterialKind.Crystal||kind==MaterialKind.IntermediateFertilizer||kind==MaterialKind.AdvancedFertilizer)
                ? Configuration(index).bufferLimit : buildings[index].Id=="StorageDepot" ? FacilityRules.DepotCapacity
                : Profile(buildings[index].Id)!=null?Profile(buildings[index].Id).LocalStorageCapacity:IndustryRules.StorageLimit;
        private ManagedBuilding CoreFactory()
        {foreach(var building in settlement.CoreBuildings)if(building.Id=="Factory"&&!building.State.offline)return building;return null;}
        private float Incoming(int target,int kind)=>incoming[target*IndustryRules.MaterialCount+kind];
        private Vector3 Destination(CargoStock cargo)
        {if(cargo.food)return ColonyFoodDestination()!=null?ColonyFoodDestination().transform.position:buildings[cargo.source].transform.position;if(cargo.target>=0)return buildings[cargo.target].transform.position;foreach(var building in settlement.CoreBuildings)if(building.Id=="ColonyShip"||(cargo.target==-1||cargo.target==-3)&&building.Id=="Factory")return building.transform.position;return buildings[cargo.source].transform.position;}
        private void AddFlight(CargoStock state)
        {
            if(!routes.TryGetValue((state.source,state.target),out var path))
            {
                var a=planet.transform.InverseTransformPoint(buildings[state.source].transform.position).normalized;
                var b=planet.transform.InverseTransformPoint(Destination(state)).normalized;
                var length=planet.SurfaceDistance(a,b);
                var harvestingBiomass=!state.food&&state.kind==(int)MaterialKind.Biomass;
                var stand=harvestingBiomass?NearestDeposit(buildings[state.source].transform.position,MaterialKind.Biomass):null;
                var harvestDistance=stand!=null?planet.SurfaceDistance(a,stand.Direction):0;
                var routeLength=length+harvestDistance*(harvestingBiomass?2:0);
                var points=new Vector3[Mathf.Clamp(Mathf.CeilToInt(routeLength/12)+1,25,4097)];
                for(var i=0;i<points.Length;i++)
                {
                    var t=i/(float)(points.Length-1);
                    points[i]=harvestingBiomass&&stand!=null?HarvestFlightPoint(state,stand.Direction,t):FlightPoint(state,t);
                }
                path=new FlightRoute{points=points,duration=Mathf.Max(3,routeLength/80)};
                // Bound the cache even when a large colony dispatches many unique routes.
                if(routes.Count>=64)routes.Clear();routes[(state.source,state.target)]=path;
            }
            Flight flight;
            if(idleFlights.Count>0)flight=idleFlights.Pop();
            else
            {
                var ship=new GameObject("Cargo hauler - "+IndustryRules.Name((MaterialKind)state.kind),typeof(MeshFilter),typeof(MeshRenderer));ship.transform.SetParent(visualRoot,false);
                ship.GetComponent<MeshFilter>().sharedMesh=WorldArt.Hauler;ship.GetComponent<Renderer>().sharedMaterial=haulerMaterial;ship.transform.localScale=Vector3.one*2;
                var route=new GameObject("Cargo route",typeof(LineRenderer));route.transform.SetParent(visualRoot,false);
                var line=route.GetComponent<LineRenderer>();line.sharedMaterial=routeMaterial;line.startColor=line.endColor=new Color(.8f,.65f,.28f,.35f);line.widthMultiplier=1.2f;
                flight=new Flight{ship=ship.transform,line=line};
            }
            var load=flight.ship.Find("Food crate");
            if(load==null)
            {
                var crate=new GameObject("Food crate",typeof(MeshFilter),typeof(MeshRenderer));crate.transform.SetParent(flight.ship,false);
                crate.transform.localPosition=new Vector3(0,.85f,-.6f);crate.transform.localScale=new Vector3(1.5f,.8f,1.5f);
                crate.GetComponent<MeshFilter>().sharedMesh=WorldArt.Cube;crate.GetComponent<Renderer>().sharedMaterial=foodHaulerMaterial;load=crate.transform;
            }
            load.gameObject.SetActive(state.food);
            var logs=flight.ship.Find("Harvested logs");
            if(logs==null)
            {
                var cargo=new GameObject("Harvested logs").transform;cargo.SetParent(flight.ship,false);
                for(var i=0;i<3;i++)
                {
                    var log=new GameObject("Timber "+(i+1),typeof(MeshFilter),typeof(MeshRenderer));log.transform.SetParent(cargo,false);
                    log.transform.localPosition=new Vector3((i-1)*.34f,.55f,-.55f);log.transform.localRotation=Quaternion.Euler(0,0,90);
                    log.transform.localScale=new Vector3(.34f,.28f,1.15f);log.GetComponent<MeshFilter>().sharedMesh=WorldArt.Cylinder;
                    log.GetComponent<Renderer>().sharedMaterial=materials[materials.Count-1];
                }
                logs=cargo;
            }
            logs.gameObject.SetActive(!state.food && state.kind==(int)MaterialKind.Biomass);
            flight.ship.name=state.food?"Food transport / "+buildings[state.source].DisplayName:
                state.kind==(int)MaterialKind.Biomass?"Logging truck / returning timber": "Cargo hauler / "+IndustryRules.Name((MaterialKind)state.kind);
            flight.ship.GetComponent<Renderer>().sharedMaterial=state.food?foodHaulerMaterial:haulerMaterial;
            flight.state=state;flight.route=path;flight.ship.position=path.Point(state.progress);
            flight.ship.gameObject.SetActive(true);flight.line.gameObject.SetActive(true);flight.line.enabled=state.food || Overlay!=0;
            flight.line.widthMultiplier=state.food ? .35f : 1.2f;flight.line.startColor=flight.line.endColor=state.food?new Color(.4f,.8f,.24f,.5f):new Color(.8f,.65f,.28f,.35f);
            flight.line.positionCount=path.points.Length;flight.line.SetPositions(path.points);flights.Add(flight);
        }
        private void ReleaseFlight(Flight flight)
        {
            flight.ship.gameObject.SetActive(false);flight.line.gameObject.SetActive(false);
            flight.route=null;idleFlights.Push(flight);
        }
        private Vector3 FlightPoint(CargoStock cargo,float t)
        {
            var a=planet.transform.InverseTransformPoint(buildings[cargo.source].transform.position).normalized;
            var b=planet.transform.InverseTransformPoint(Destination(cargo)).normalized;
            var n=Vector3.Slerp(a,b,t).normalized;
            var height=Mathf.Max(planet.SeaLevel,planet.EvaluateSurfacePoint(n).magnitude)+12+Mathf.Sin(t*Mathf.PI)*45;
            return planet.transform.TransformPoint(n*height);
        }
        private Vector3 HarvestFlightPoint(CargoStock cargo,Vector3 standDirection,float progress)
        {
            var facility=planet.transform.InverseTransformPoint(buildings[cargo.source].transform.position).normalized;
            var recipient=planet.transform.InverseTransformPoint(Destination(cargo)).normalized;
            Vector3 from,to;float t;
            if(progress<.3f){from=facility;to=standDirection;t=progress/.3f;}
            else if(progress<.6f){from=standDirection;to=facility;t=(progress-.3f)/.3f;}
            else{from=facility;to=recipient;t=(progress-.6f)/.4f;}
            var n=Vector3.Slerp(from,to,t).normalized;
            var height=Mathf.Max(planet.SeaLevel,planet.EvaluateSurfacePoint(n).magnitude)+12+Mathf.Sin(t*Mathf.PI)*35;
            return planet.transform.TransformPoint(n*height);
        }
        private void AdvanceCargo(float seconds)
        {
            for(var i=flights.Count-1;i>=0;i--)
            {
                var flight=flights[i];var state=flight.state;
                state.progress=Mathf.Clamp01(state.progress+seconds/flight.route.duration);flight.state=state;
                var point=flight.route.Point(state.progress);var next=flight.route.Point(Mathf.Min(1,state.progress+.01f));
                flight.ship.position=point;if((next-point).sqrMagnitude>.001f)flight.ship.rotation=Quaternion.LookRotation(next-point,(point-planet.transform.position).normalized);
                if(state.progress<1 || (state.target==-1 && CoreFactory()==null)) continue;
                if(state.food)
                {
                    var delivered=resources.AddFood(state.amount);state.amount-=delivered;foundingFoodDelivery+=delivered;
                    if(state.amount>.0001f){flight.state=state;continue;}
                    ReleaseFlight(flight);flights.RemoveAt(i);continue;
                }
                if(state.target==-3) { state.amount-=StoreOnShip((MaterialKind)state.kind,state.amount); if(state.amount>.0001f){flight.state=state;continue;} ReleaseFlight(flight);flights.RemoveAt(i);continue; }
                if(state.target==-1) AddRecoveredSupplies(state.amount);
                else {stocks[state.target].quantities[state.kind]+=state.amount;
                    incoming[state.target*IndustryRules.MaterialCount+state.kind]=Mathf.Max(0,Incoming(state.target,state.kind)-state.amount);}
                ReleaseFlight(flight);flights.RemoveAt(i);
            }
        }
        private void UpdateSurvey()
        {
            surveyTimer+=Time.unscaledDeltaTime;
            revealTimer+=Time.unscaledDeltaTime;
            if(revealTimer>=.5f){revealTimer=0;SurveyNow();}
            if(surveyOverlay==Overlay && surveyTimer<.1f)return;
            surveyTimer=0;surveyOverlay=Overlay;
            if(surveyCamera==null)surveyCamera=Camera.main;
            if(surveyCamera==null)return;var camera=surveyCamera.transform.position;
            if(logisticsRenderer!=null)logisticsRenderer.enabled=Overlay==1 && !LogisticsBuildPending;
            if(surveyCoverageRenderer!=null)surveyCoverageRenderer.enabled=Overlay==1;
            foreach(var deposit in deposits)
            {
                var world=deposit.Position;
                var distance=Vector3.Distance(world,camera);var visible=Vector3.Dot(world-planet.transform.position,camera-world)>0;
                var showOutcrop=deposit.Discovered&&visible&&distance<2000&&deposit.Remaining>0;
                if(deposit.outcrop.gameObject.activeSelf!=showOutcrop)deposit.outcrop.gameObject.SetActive(showOutcrop);
                if(deposit.outcrop.gameObject.activeSelf && deposit.Excavation!=null)
                    deposit.outcrop.position=planet.transform.TransformPoint(planet.EvaluateSurfacePoint(deposit.Direction)+deposit.Direction*2);
                var surveyIndex=deposit.Kind==MaterialKind.Promethium?7:(int)deposit.Kind+2;
                var show=deposit.Discovered&&visible&&deposit.Remaining>0&&(Overlay==1||Overlay==surveyIndex);
                if(deposit.marker.gameObject.activeSelf!=show)deposit.marker.gameObject.SetActive(show);
                if(show){var scale=Mathf.Clamp(distance*.012f,7,260);deposit.marker.position=world+planet.transform.TransformDirection(deposit.Direction)*(scale+45);
                    deposit.marker.rotation=Quaternion.FromToRotation(Vector3.up,planet.transform.TransformDirection(deposit.Direction));deposit.marker.localScale=new Vector3(scale,scale*2,scale);}
            }
            foreach(var discovery in discoveries)
            {
                var world=discovery.Position;var distance=Vector3.Distance(world,camera);
                var visible=discovery.State.discovered&&Vector3.Dot(world-planet.transform.position,camera-world)>0;
                if(discovery.Marker.gameObject.activeSelf!=visible)discovery.Marker.gameObject.SetActive(visible);
                if(discovery.Site!=null)discovery.Site.gameObject.SetActive(visible&&distance<2400);
                if(visible)
                {
                    var normal=planet.transform.TransformDirection(discovery.Direction);
                    var scale=Mathf.Clamp(distance*.012f,8,260);
                    discovery.Marker.position=world+normal*(scale+45);
                    discovery.Marker.rotation=Quaternion.FromToRotation(Vector3.up,normal);
                    discovery.Marker.localScale=new Vector3(scale,scale*1.6f,scale);
                }
            }
            foreach(var flight in flights)flight.line.enabled=flight.state.food || Overlay!=0;
            UpdateExcavationOutline();
        }
        public IndustrySnapshot Capture()
        {
            Synchronize();RefreshFarmEntities();var savedStocks=new FacilityStock[stocks.Count];
            for(var i=0;i<stocks.Count;i++)
            {
                var config=Configuration(i);
                savedStocks[i]=new FacilityStock{quantities=(float[])stocks[i].quantities.Clone(),work=stocks[i].work,food=stocks[i].food,configuration=FacilityRules.Clone(config)};
            }
            var savedDeposits=new DepositStock[deposits.Count];for(var i=0;i<deposits.Count;i++)savedDeposits[i]=new DepositStock{id=i,kind=(int)deposits[i].Kind,remaining=deposits[i].Remaining,initialVolume=deposits[i].InitialVolume,discovered=deposits[i].Discovered,
                excavation=CloneExcavation(deposits[i].Excavation),x=deposits[i].Direction.x,y=deposits[i].Direction.y,z=deposits[i].Direction.z};
            var savedDiscoveries=new DiscoveryRecord[discoveries.Count];for(var i=0;i<discoveries.Count;i++)savedDiscoveries[i]=discoveries[i].State;
            return new IndustrySnapshot{facilities=savedStocks,deposits=savedDeposits,cargo=flights.ConvertAll(f=>f.state).ToArray(),geologyVersion=4,materialVersion=2,facilityVersion=1,
                founding=founding,expeditionCargo=expeditionCargo != null ? (float[])expeditionCargo.Clone() : null,
                tenderLogistics=tenderLogistics == null ? null : new TenderLogisticsState
                {version=tenderLogistics.version,orbitalCrew=tenderLogistics.orbitalCrew,orbitalFood=tenderLogistics.orbitalFood,
                    orbitalSupplies=tenderLogistics.orbitalSupplies != null ? (float[])tenderLogistics.orbitalSupplies.Clone() : null,
                    tripsUsed=tenderLogistics.tripsUsed,initialLoadoutApplied=tenderLogistics.initialLoadoutApplied},
                discoveries=savedDiscoveries,explorationVersion=ExplorationRules.LayoutVersion,exploredCells=(bool[])exploredCells.Clone(),
                producedTotals=(float[])producedTotals.Clone(),consumedTotals=(float[])consumedTotals.Clone(),
                productionRates=(float[])productionRates.Clone(),consumptionRates=(float[])consumptionRates.Clone(),
                conflict=new MiningConflict{active=conflict.active,core=conflict.core,deposit=conflict.deposit,sector=conflict.sector,mine=conflict.mine,building=conflict.building}};
        }
        private void Clear()
        {
            CancelLogisticsBuild();LogisticsVisualTruncated=false;
            founding=default;foundingFoodDelivery=0;
            expeditionCargo = null; initialTenderManifest = null; tenderLogistics = null; farmEntityRevision=-1;farmInputPools.Clear();farmOperations.Clear();
            ClearExcavation();ClearSurveyPresentation();if(visualRoot!=null)Destroy(visualRoot.gameObject);
            foreach(var material in materials)if(material!=null)Destroy(material);
            DepositUsageSeconds=0;
            materials.Clear();deposits.Clear();discoveries.Clear();stocks.Clear();flights.Clear();idleFlights.Clear();routes.Clear();
            foreach(var group in consumers)group.Clear();
            incoming=Array.Empty<float>();buildings=Array.Empty<ManagedBuilding>();surveyors=Array.Empty<ManagedBuilding>();connectedToCore=Array.Empty<bool>();networkComponent=Array.Empty<int>();
            networkPowerDemand=networkPowerSupply=networkPowerFraction=networkFuelLoad=Array.Empty<float>();
            if(logisticsMesh!=null)Destroy(logisticsMesh);logisticsMesh=null;logisticsRenderer=null;logisticsVisualDirty=true;logisticsSurfaceRevision=-1;profiles.Clear();PowerGenerated=PowerDemand=WorkersRequired=0;
            Array.Clear(producedWindow,0,producedWindow.Length);Array.Clear(consumedWindow,0,consumedWindow.Length);
            Array.Clear(productionRates,0,productionRates.Length);Array.Clear(consumptionRates,0,consumptionRates.Length);rateWindowSeconds=0;
            Array.Clear(producedTotals,0,producedTotals.Length);Array.Clear(consumedTotals,0,consumedTotals.Length);
            dispatchTimer=0;surveyTimer=0;revealTimer=0;surveyOverlay=-1;inspectedLocation=-1;discoveryDefinitions.Clear();
        }
        private void OnDestroy(){Clear();}
        private sealed class Flight{public CargoStock state;public Transform ship;public LineRenderer line;public FlightRoute route;}
        private sealed class FlightRoute
        {
            public Vector3[] points;public float duration;
            public Vector3 Point(float progress)
            {
                var t=Mathf.Clamp01(progress)*(points.Length-1);var index=Mathf.Min((int)t,points.Length-2);
                return Vector3.Lerp(points[index],points[index+1],t-index);
            }
        }
    }
}
