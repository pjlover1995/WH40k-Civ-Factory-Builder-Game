using System;
using System.Collections.Generic;
using UnityEngine;
using WH30K.Game;
using WH30K.Planet;
using WH30K.Sim.Buildings;
using WH30K.Sim.Environment;
using WH30K.Sim.Resources;
using WH30K.UI;
using WH30K.Gameplay.Construction;

namespace WH30K.Sim.Settlements
{
    [Serializable]
    public struct SettlementSnapshot
    {
        public float population;
        public float workforce;
        public float productionModifier;
        public float upkeepModifier;
        public Vector3 position;
        public Vector3 normal;
        public string[] buildings;
        public float industryModifier;
        public float tickTimer;
        public BuildingOperationState[] operations;
        public int shipLayoutVersion; // 0 retains historical tender dimensions and berths.
    }

    public struct SettlementReport
    {
        public float population;
        public float workforce;
        public float production;
        public float upkeep;
        public float net;
        public float tickInterval;
    }

    /// <summary>
    /// Handles the initial settlement, building instantiation, and the lightweight resource loop.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public partial class Settlement : MonoBehaviour
    {
        [SerializeField] private float tickInterval = 1f;
        [SerializeField] private float populationGrowthRate = 0.05f;
        [SerializeField] private float workforceRatio = 0.62f;
        [SerializeField] private float buildingSpacing = 220f;

        private readonly List<SettlementBuilding> buildings = new List<SettlementBuilding>();
        private readonly List<GameObject> buildingVisuals = new List<GameObject>();
        private readonly List<ManagedBuilding> constructedBuildings = new List<ManagedBuilding>();
        private readonly List<ManagedBuilding> coreBuildings = new List<ManagedBuilding>();
        public IReadOnlyList<ManagedBuilding> CoreBuildings => coreBuildings;
        private readonly List<BuildingEconomy> economy = new List<BuildingEconomy>();

        private LODPlanet planet;
        private ResourceSystem resourceSystem;
        private EnvironmentState environmentState;
        private NewGameMenu menu;
        private Transform settlementAnchor;
        private GameObject serviceRoads;
        private Mesh serviceRoadMesh;
        private GameSettings.DifficultyDefinition currentDifficulty;

        private float population;
        public bool HasExplorator { get; private set; }
        public int ShipLayoutVersion { get; private set; }
        private float workforce;
        private float productionModifier = 1f;
        private float upkeepModifier = 1f;
        private float industryModifier = 1f;
        private float tickTimer;
        private float housingUtilisation, staffingUtilisation;
        private ColonyTick outputTick;
        private EnvironmentSnapshot outputEnvironment;
        private float outputWater, outputFood;
        public string OutputExplanation => ColonyOutputExplanation.Describe(outputTick, outputEnvironment, outputWater, outputFood);
        private Vector3 storedPosition;
        private Vector3 storedNormal;

        public void ConfigureMenu(NewGameMenu newMenu)
        {
            menu = newMenu;
        }

        public bool HasActiveSimulation => planet != null && resourceSystem != null && environmentState != null;
        public float Population => population;
        internal float ReportedHousingCapacity => outputTick.capacity;
        public float IndustryPolicyModifier => industryModifier;
        public IEnumerable<ManagedBuilding> AllBuildings
        {get{foreach(var b in coreBuildings)if(b!=null)yield return b;foreach(var b in constructedBuildings)if(b!=null)yield return b;}}
        public float[] ResidentialProvision()
        {
            var lib=ResidentialDevelopment.Library;var result=new float[3];
            foreach(var b in AllBuildings)
            {
                var split=ResidentialRules.Split(b.Id=="HabBlock"?b.BaseHousing:b.Economy.capacity,lib);
                if(b.Id=="HabBlock" && !b.State.offline)
                {
                    var added=ResidentialRules.AddedHousing(b.State.residential,lib);
                    for(var i=0;i<3;i++)split[i]+=added[i]*b.HousingFocusFactor;
                }
                for(var i=0;i<3;i++)result[i]+=split[i];
            }
            return result;
        }
        public ResidentialInputs ResidentialContext(ManagedBuilding block)
        {
            var jobs=0f;var utility=0f;
            foreach(var b in AllBuildings)
            {
                jobs+=b.Economy.jobs;
                if(b.Id=="Utility" && !b.State.offline && Vector3.Distance(block.transform.position,b.transform.position)<=ResidentialDevelopment.Library.serviceRadius)
                    utility+=b.Economy.jobs;
            }
            var staffing=jobs>0?Mathf.Clamp01(workforce/jobs):0;
            var report=resourceSystem.CreateSnapshot();var environment=environmentState.CreateSnapshot();
            var industry=GetComponent<IndustrySystem>();
            return new ResidentialInputs {credits=resourceSystem.Stockpile / ColonyRules.LegacyProjectSupplyScale,staffing=staffing,
                production=industry!=null && industry.PowerDemand>0?Mathf.Clamp01(industry.PowerGenerated/industry.PowerDemand):1,
                services=ResidentialRules.Services(utility,industry!=null?industry.PowerGenerated:0,industry!=null?industry.PowerDemand:0,ResidentialDevelopment.Library),habitability=environmentState.Habitability,co2=environment.Co2,
                water=environment.WaterPollution,temperature=environment.GlobalTemperatureOffset,
                demand=ResidentialRules.Demand(population,jobs,environmentState.Habitability,ResidentialProvision(),ResidentialDevelopment.Library)};
        }
        public bool ResidentialCellClear(ManagedBuilding owner,Bounds cell)
        {
            if(!TerritoryClear(owner.transform,LandRect(cell),owner))return false;
            EnsureResidentialVolumes();
            var axes=residentialTestAxes;axes[0]=owner.transform.right;axes[1]=owner.transform.up;axes[2]=owner.transform.forward;
            var extents=Vector3.Scale(cell.extents,owner.transform.lossyScale);
            var center=owner.transform.TransformPoint(cell.center);
            var worldBounds=WorldBounds(center,axes,extents);
            var dozer=GetComponent<EarthworksController>()?.Vehicle;
            if(dozer!=null)
            {
                var vehicleCenter=dozer.position+dozer.up*6;
                if((worldBounds.ClosestPoint(vehicleCenter)-vehicleCenter).sqrMagnitude<EarthworksRules.VehicleRadius*EarthworksRules.VehicleRadius)return false;
            }
            foreach(var group in residentialVolumes)
            {
                if(group.owner==owner || group.owner.Id=="Farm" || !worldBounds.Intersects(group.bounds))continue;
                foreach(var volume in group.volumes)
                    if(worldBounds.Intersects(volume.bounds) && BoxesOverlap(volume.center-center,axes,extents,volume.axes,volume.extents))return false;
            }
            if(cell.min.y<.1f && settlementAnchor!=null)
            {
                var point=owner.transform.TransformPoint(cell.center);
                foreach(var core in coreBuildings)if(core!=null && core!=owner)
                {
                    var a=settlementAnchor.position;var segment=core.transform.position-a;var t=Mathf.Clamp01(Vector3.Dot(point-a,segment)/Mathf.Max(.001f,segment.sqrMagnitude));
                    if(Vector3.Distance(point,a+segment*t)<1.5f+owner.transform.lossyScale.x*.1f)return false;
                }
            }
            return true;
        }
        private sealed class ResidentialVolume
        {
            public Vector3 center,extents;
            public Vector3[] axes;
            public Bounds bounds;
        }
        private sealed class ResidentialVolumeGroup
        {
            public ManagedBuilding owner;
            public Bounds bounds;
            public readonly List<ResidentialVolume> volumes=new List<ResidentialVolume>();
        }
        private readonly Vector3[] residentialTestAxes=new Vector3[3];
        private readonly List<ResidentialVolumeGroup> residentialVolumes=new List<ResidentialVolumeGroup>();
        private int residentialGeometryRevision,volumeRevision=-1,volumeSurface=-1;
        public int ResidentialGeometryRevision=>residentialGeometryRevision;
        public int ResidentialVolumeBuilds {get;private set;}
        private static Bounds WorldBounds(Vector3 center,Vector3[] axes,Vector3 extents)
        {
            var size=Vector3.zero;
            for(var i=0;i<3;i++)size+=new Vector3(Mathf.Abs(axes[i].x),Mathf.Abs(axes[i].y),Mathf.Abs(axes[i].z))*extents[i];
            return new Bounds(center,size*2);
        }
        private void EnsureResidentialVolumes()
        {
            var surface=planet!=null?planet.SurfaceRevision:0;
            if(volumeRevision==residentialGeometryRevision && volumeSurface==surface)return;
            volumeRevision=residentialGeometryRevision;volumeSurface=surface;residentialVolumes.Clear();ResidentialVolumeBuilds++;
            foreach(var other in AllBuildings)
            {
                var group=new ResidentialVolumeGroup{owner=other};
                void Add(Transform source,Bounds local)
                {
                    var axes=new[]{source.right,source.up,source.forward};
                    var center=source.TransformPoint(local.center);var extents=Vector3.Scale(local.extents,source.lossyScale);
                    var volume=new ResidentialVolume{center=center,extents=extents,axes=axes,bounds=WorldBounds(center,axes,extents)};
                    if(group.volumes.Count==0)group.bounds=volume.bounds;else group.bounds.Encapsulate(volume.bounds);
                    group.volumes.Add(volume);
                }
                if(other.Residential!=null && other.State.residential!=null)
                {
                    foreach(var m in other.State.residential.modules)
                        foreach(var c in ResidentialRules.Cells(m,ResidentialDevelopment.Library))
                        {
                            var volume=ResidentialDevelopment.CellBounds(c);
                            if(c.bottom==0){var min=volume.min;min.y=Mathf.Min(min.y,other.Residential.GroundLocal(volume.center.x,volume.center.z)-.018f);volume.SetMinMax(min,volume.max);}
                            Add(other.transform,volume);
                        }
                    if(other.State.residential.project!=null)
                        foreach(var c in ResidentialRules.Cells(other.State.residential.project.placement,ResidentialDevelopment.Library))Add(other.transform,ResidentialDevelopment.CellBounds(c));
                }
                else
                {
                    if(other.State.improving)Add(other.transform,other.GrowthBounds());
                    if(other.Industrial!=null && other.State.manufactorum.project!=null)Add(other.transform,ManufactorumDevelopment.BayBounds(other.State.manufactorum.project));
                    foreach(var mesh in other.GetComponentsInChildren<MeshFilter>())if(mesh.sharedMesh!=null)Add(mesh.transform,mesh.sharedMesh.bounds);
                }
                residentialVolumes.Add(group);
            }
        }
        // Full 3-D SAT after the broad distance check. Curved-world rotations and vertical volumes matter.
        public static bool VolumeOverlaps(Transform a,Bounds ab,Transform b,Bounds bb)
        {
            var aa=new[]{a.right,a.up,a.forward};var ba=new[]{b.right,b.up,b.forward};
            var ae=Vector3.Scale(ab.extents,a.lossyScale);var be=Vector3.Scale(bb.extents,b.lossyScale);
            var delta=b.TransformPoint(bb.center)-a.TransformPoint(ab.center);
            return BoxesOverlap(delta,aa,ae,ba,be);
        }
        private static bool BoxesOverlap(Vector3 delta,Vector3[] aa,Vector3 ae,Vector3[] ba,Vector3 be)
        {
            for(var i=0;i<3;i++)
            {
                if(VolumeSeparated(delta,aa[i],aa,ae,ba,be) || VolumeSeparated(delta,ba[i],aa,ae,ba,be))return false;
                for(var j=0;j<3;j++)if(VolumeSeparated(delta,Vector3.Cross(aa[i],ba[j]),aa,ae,ba,be))return false;
            }
            return true;
        }
        public static bool ResidentialPlacementOverlaps(ManagedBuilding block,Vector3 center,Vector3 dimensions,Quaternion rotation)
        {
            var aa=new[]{rotation*Vector3.right,rotation*Vector3.up,rotation*Vector3.forward};
            var ba=new[]{block.transform.right,block.transform.up,block.transform.forward};
            var origin=center+aa[1]*dimensions.y*.5f;
            bool Intersects(HabPlacement module)
            {
                foreach(var cell in ResidentialRules.Cells(module,ResidentialDevelopment.Library))
                {
                    var bounds=ResidentialDevelopment.CellBounds(cell);
                    if(cell.bottom==0){var min=bounds.min;min.y=-ResidentialDevelopment.Library.maxFoundationDepth;bounds.SetMinMax(min,bounds.max);}
                    if(BoxesOverlap(block.transform.TransformPoint(bounds.center)-origin,aa,dimensions*.5f,ba,
                        Vector3.Scale(bounds.extents,block.transform.lossyScale)))return true;
                }
                return false;
            }
            foreach(var module in block.State.residential.modules)if(Intersects(module))return true;
            return block.State.residential.project!=null && Intersects(block.State.residential.project.placement);
        }
        private static bool VolumeSeparated(Vector3 delta,Vector3 axis,Vector3[] a,Vector3 ae,Vector3[] b,Vector3 be)
        {
            if(axis.sqrMagnitude<.00001f)return false;axis.Normalize();
            var ra=Mathf.Abs(Vector3.Dot(axis,a[0]))*ae.x+Mathf.Abs(Vector3.Dot(axis,a[1]))*ae.y+Mathf.Abs(Vector3.Dot(axis,a[2]))*ae.z;
            var rb=Mathf.Abs(Vector3.Dot(axis,b[0]))*be.x+Mathf.Abs(Vector3.Dot(axis,b[1]))*be.y+Mathf.Abs(Vector3.Dot(axis,b[2]))*be.z;
            return Mathf.Abs(Vector3.Dot(delta,axis))>=ra+rb-.001f;
        }

        public void BeginNewGame(GameSettings.DifficultyDefinition definition, LODPlanet planetInstance,
            Vector3 surfacePosition, Vector3 surfaceNormal, ResourceSystem resources, EnvironmentState environment, bool colonyShip = true)
        {
            currentDifficulty = definition;
            planet = planetInstance;
            resourceSystem = resources;
            environmentState = environment;
            productionModifier = definition.resourceYieldMultiplier;
            upkeepModifier = Mathf.Lerp(1f, 1.3f, Mathf.InverseLerp(1f, 1.4f, definition.environmentHarshnessMultiplier));
            industryModifier = 1f;
            constructedBuildings.Clear();
            HasExplorator = colonyShip;
            ShipLayoutVersion = colonyShip ? 2 : 0;
            population = colonyShip ? ShipRules.StartingCrew : Mathf.Max(120f, 160f * definition.resourceYieldMultiplier);
            workforce = population * workforceRatio;
            tickTimer = 0f;
            storedPosition = surfacePosition;
            storedNormal = surfaceNormal;

            ClearVisuals();
            EnsureAnchor(surfacePosition, surfaceNormal);
            RebuildBuildings(colonyShip ? new[] { "ColonyShip" } : new[] { "HabBlock", "Factory", "Utility" });
            RefreshReadout();
            menu?.AppendEventLog("Colony ship landed. Deploy housing, extraction, water and manufacturing from its cargo.");
        }

        public void LoadFromSnapshot(SettlementSnapshot snapshot, LODPlanet planetInstance,
            GameSettings.DifficultyDefinition definition, ResourceSystem resources, EnvironmentState environment)
        {
            currentDifficulty = definition;
            planet = planetInstance;
            resourceSystem = resources;
            environmentState = environment;
            productionModifier = snapshot.productionModifier;
            upkeepModifier = snapshot.upkeepModifier;
            industryModifier = snapshot.industryModifier > 0f ? snapshot.industryModifier : 1f;
            constructedBuildings.Clear();
            population = snapshot.population;
            ShipLayoutVersion = snapshot.shipLayoutVersion;
            HasExplorator = ShipLayoutVersion > 0;
            workforce = snapshot.workforce;
            tickTimer = Mathf.Clamp(snapshot.tickTimer, 0f, tickInterval);
            // Keep a saved unit direction byte-for-byte stable. Re-normalizing it adds
            // float drift at planetary scale and changes the serialized landing point.
            storedNormal = snapshot.normal.sqrMagnitude > 0.01f
                ? (Mathf.Abs(snapshot.normal.sqrMagnitude - 1f) < 0.0001f
                    ? snapshot.normal
                    : snapshot.normal.normalized)
                : snapshot.position.normalized;
            // Saves made before a radius/terrain upgrade must still sit on the current surface.
            storedPosition = planet.transform.TransformPoint(planet.EvaluateSurfacePoint(storedNormal));

            ClearVisuals();
            EnsureAnchor(storedPosition, storedNormal);
            RebuildBuildings(snapshot.buildings != null && snapshot.buildings.Length > 0
                ? snapshot.buildings
                : new[] { "HabBlock", "Factory", "Utility" });
            if (snapshot.operations != null)
                for (var i = 0; i < coreBuildings.Count && i < snapshot.operations.Length; i++)
                    coreBuildings[i].Restore(snapshot.operations[i]);
            NotifyResidentialGeometry();
        }

        private void Update()
        {
            if (planet == null || resourceSystem == null || environmentState == null)
            {
                return;
            }

            tickTimer += ColonyClock.DeltaTime;
            var steps = 0;
            while (tickTimer >= tickInterval && steps++ < 8)
            {
                tickTimer -= tickInterval;
                RunTick();
            }
        }

        public SettlementSnapshot CreateSnapshot()
        {
            var ids = new string[buildings.Count];
            var operations = new BuildingOperationState[coreBuildings.Count];
            for (var i = 0; i < operations.Length; i++) operations[i] = coreBuildings[i].State;
            for (var i = 0; i < buildings.Count; i++)
            {
                ids[i] = buildings[i].Id;
            }

            return new SettlementSnapshot
            {
                shipLayoutVersion = ShipLayoutVersion,
                population = population,
                workforce = workforce,
                productionModifier = productionModifier,
                upkeepModifier = upkeepModifier,
                position = storedPosition,
                normal = storedNormal,
                buildings = ids,
                industryModifier = industryModifier,
                tickTimer = tickTimer,
                operations = operations
            };
        }

        public void AdjustPolicy(float productionDelta, float upkeepDelta, string message, float industryDelta = 0f)
        {
            productionModifier = Mathf.Clamp(productionModifier + productionDelta, 0.25f, 3f);
            upkeepModifier = Mathf.Clamp(upkeepModifier + upkeepDelta, 0.1f, 3f);
            industryModifier = Mathf.Clamp(industryModifier + industryDelta, 0.25f, 3f);
            RefreshReadout();
            if (!string.IsNullOrEmpty(message))
            {
                menu?.AppendEventLog(message);
            }
        }

#if UNITY_EDITOR
        public void DebugRunImmediateTick()
        {
            if (!HasActiveSimulation)
            {
                return;
            }

            RunTick();
        }
#endif

        private void RunTick()
        {
            var tick = CalculateEconomy(tickInterval);
            population = tick.population;
            workforce = tick.workforce;
            var totalImpact = EnvironmentImpact.Zero;
            foreach (var building in coreBuildings)
            {
                totalImpact += EnvironmentRules.ApplyPolicy(building.Impact.Scale(tick.utilisation), industryModifier);
            }
            foreach (var building in constructedBuildings)
                if(!IndustryRules.Facility(building.Id))
                    totalImpact += EnvironmentRules.ApplyPolicy(building.Impact.Scale(tick.utilisation), industryModifier);
            environmentState.ApplyIndustryImpact(totalImpact, tickInterval);
            PublishReport(tick, tickInterval);
        }

        private ColonyTick CalculateEconomy(float seconds)
        {
            economy.Clear();
            foreach (var building in coreBuildings) economy.Add(building.Economy);
            foreach (var building in constructedBuildings) economy.Add(building.Economy);
            outputEnvironment = environmentState.CreateSnapshot();
            outputWater = resourceSystem.Water; outputFood = resourceSystem.Food;
            return ColonyRules.CalculateSupportedTick(economy, population, workforceRatio,
                productionModifier, upkeepModifier, environmentState.Habitability,
                outputFood, outputWater, ColonyRules.PopulationHealth, seconds);
        }

        private void PublishReport(ColonyTick tick, float seconds)
        {
            outputTick = tick;
            housingUtilisation = tick.capacity > 0 ? Mathf.Clamp01(tick.population / tick.capacity) : 0;
            staffingUtilisation = tick.utilisation;
            resourceSystem.ApplySettlementReport(new SettlementReport
            {
                population = tick.population, workforce = tick.workforce,
                production = tick.production, upkeep = tick.upkeep,
                net = tick.production - tick.upkeep, tickInterval = seconds
            });
        }

        public void SetConstructedBuildings(IEnumerable<ManagedBuilding> definitions)
        {
            constructedBuildings.Clear();
            constructedBuildings.AddRange(definitions);
            NotifyResidentialGeometry();
            RefreshReadout();
        }

        public void RefreshReadout()
        {
            if (!HasActiveSimulation) return;
            var tick = CalculateEconomy(0f);
            workforce = tick.workforce;
            PublishReport(tick, 0f);
        }
        public void NotifyResidentialGeometry()
        {residentialGeometryRevision++;foreach(var b in AllBuildings)b.Residential?.Invalidate();}

        public bool OverlapsStartingBuildings(Vector3 center, Vector3 dimensions,Quaternion rotation)
        {
            var radius = new Vector2(dimensions.x, dimensions.z).magnitude * 0.5f;
            foreach (var building in coreBuildings)
                if (building != null && Vector3.Distance(center, building.transform.position) < radius + building.ReservedFootprintRadius
                    && (building.Id == "ColonyShip" ? ShipFootprintOverlaps(building, center, dimensions, rotation)
                        : building.Residential==null || ResidentialPlacementOverlaps(building,center,dimensions,rotation)))
                    return true;
            return false;
        }

        private static bool ShipFootprintOverlaps(ManagedBuilding ship, Vector3 center, Vector3 dimensions, Quaternion rotation)
        {
            // Project onto the ship tangent plane: a 3-km hull is not a 3-km circular exclusion zone.
            var local = ship.transform.InverseTransformPoint(center);
            var bounds = ship.GrowthBounds();
            var half = dimensions * .5f;
            var extentX = Mathf.Abs(Vector3.Dot(ship.transform.right, rotation * Vector3.right)) * half.x
                + Mathf.Abs(Vector3.Dot(ship.transform.right, rotation * Vector3.forward)) * half.z;
            var extentZ = Mathf.Abs(Vector3.Dot(ship.transform.forward, rotation * Vector3.right)) * half.x
                + Mathf.Abs(Vector3.Dot(ship.transform.forward, rotation * Vector3.forward)) * half.z;
            return Mathf.Abs(local.x - bounds.center.x) * ship.transform.lossyScale.x < bounds.extents.x * ship.transform.lossyScale.x + extentX + 4
                && Mathf.Abs(local.z - bounds.center.z) * ship.transform.lossyScale.z < bounds.extents.z * ship.transform.lossyScale.z + extentZ + 4;
        }

        public float BuildingUtilisation(ManagedBuilding building)
            => building.State.offline ? 0 : building.Id == "HabBlock" ? housingUtilisation : staffingUtilisation;

        public bool CanExpand(ManagedBuilding candidate)
        {
            var industry=GetComponent<IndustrySystem>();
            if(industry!=null && !industry.ClearOfMineralVeins(candidate.transform.position,candidate.FootprintRadius))return false;
            return TerritoryClear(candidate.transform,LandRect(candidate.GrowthBounds()),candidate);
        }
        private static bool ExpansionOverlaps(ManagedBuilding candidate, ManagedBuilding other, bool reserveOther)
        {
            var a = candidate.GrowthBounds(); var b = other.GrowthBounds(reserveOther);
            var at = candidate.transform; var bt = other.transform;
            var delta = bt.TransformPoint(new Vector3(b.center.x, 0, b.center.z))
                - at.TransformPoint(new Vector3(a.center.x, 0, a.center.z));
            var ax = at.right; var az = at.forward; var bx = bt.right; var bz = bt.forward;
            var ae = new Vector2(a.extents.x * at.lossyScale.x, a.extents.z * at.lossyScale.z);
            var be = new Vector2(b.extents.x * bt.lossyScale.x, b.extents.z * bt.lossyScale.z);
            return !Separated(delta, ax, ax, az, ae, bx, bz, be)
                && !Separated(delta, az, ax, az, ae, bx, bz, be)
                && !Separated(delta, bx, ax, az, ae, bx, bz, be)
                && !Separated(delta, bz, ax, az, ae, bx, bz, be);
        }
        private static bool Separated(Vector3 delta, Vector3 axis, Vector3 ax, Vector3 az, Vector2 ae,
            Vector3 bx, Vector3 bz, Vector2 be)
            => Mathf.Abs(Vector3.Dot(delta, axis)) >= Mathf.Abs(Vector3.Dot(ax, axis)) * ae.x
                + Mathf.Abs(Vector3.Dot(az, axis)) * ae.y + Mathf.Abs(Vector3.Dot(bx, axis)) * be.x
                + Mathf.Abs(Vector3.Dot(bz, axis)) * be.y + 1f;

        private void RebuildBuildings(IEnumerable<string> buildingIds)
        {
            buildings.Clear();
            foreach (var id in buildingIds)
            {
                var building = CreateBuilding(id);
                if (building != null)
                {
                    buildings.Add(building);
                }
            }

            if (buildings.Count == 0)
            {
                buildings.Add(new HabBlock());
                buildings.Add(new Factory());
                buildings.Add(new Utility());
            }

            SpawnVisuals();
        }

        private SettlementBuilding CreateBuilding(string id)
        {
            switch (id)
            {
                case "ColonyShip":
                    return new ColonyShip(HasExplorator, ShipLayoutVersion);
                case "HabBlock":
                    return new HabBlock();
                case "Factory":
                    return new Factory();
                case "Utility":
                    return new Utility();
                default:
                    Debug.LogWarning($"Unknown building id '{id}', defaulting to Hab Block.");
                    return new HabBlock();
            }
        }

        private void SpawnVisuals()
        {
            ClearVisuals();
            if (settlementAnchor == null)
            {
                return;
            }

            var count = buildings.Count;
            for (var i = 0; i < count; i++)
            {
                var building = buildings[i];
                var dimensions = building.Id == "ColonyShip" ? new Vector3(240f, 145f, 380f) : building.Id == "Factory" ? new Vector3(155f, 175f, 135f)
                    : building.Id == "Utility" ? new Vector3(125f, 145f, 115f) : new Vector3(120f, 195f, 120f);
                var ship = building.Id == "ColonyShip";
                var visualId = ship && ShipLayoutVersion >= 2 ? "LandingVessel" : ship && !HasExplorator ? "ColonyShipLegacy" : building.Id;
                var visualDimensions = ship && HasExplorator
                    ? ShipLayoutVersion >= 2 ? BuildingVisuals.ShipDimensions : BuildingVisuals.LegacyShipDimensions
                    : dimensions * BuildingVisuals.WorldScale;
                var visual = BuildingVisuals.Create(visualId, building.DisplayName, settlementAnchor, visualDimensions);
                var angle = (Mathf.PI * 2f / Mathf.Max(1, count)) * i;
                var offset = count == 1 ? Vector3.zero : new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * buildingSpacing * BuildingVisuals.WorldScale;
                var localDirection = planet.transform.InverseTransformPoint(settlementAnchor.TransformPoint(offset)).normalized;
                var ground = planet.transform.TransformPoint(planet.EvaluateSurfacePoint(localDirection));
                var normal = planet.transform.TransformDirection(localDirection);
                var forward = Vector3.ProjectOnPlane(settlementAnchor.forward, normal).normalized;
                visual.transform.SetPositionAndRotation(ground, Quaternion.LookRotation(forward, normal));
                if (visualId == "LandingVessel") visual.AddComponent<LandingVesselDescent>();
                buildingVisuals.Add(visual);
                var managed = visual.AddComponent<ManagedBuilding>();
                managed.Configure(building.Id, building.DisplayName, new BuildingEconomy
                {
                    capacity = building.PopulationCapacity, jobs = building.WorkforceSlots,
                    outputPerWorker = building.ProductionPerWorker, upkeep = building.UpkeepPerCycle
                }, building.EnvironmentImpact, resourceSystem, this);
                coreBuildings.Add(managed);
            }
            if (count > 1) BuildServiceRoads();
        }

        public void SetLandingPreview(bool preview)
        {
            if (settlementAnchor != null) settlementAnchor.gameObject.SetActive(!preview);
        }

        public bool CanReceiveTenderCrew(float crew)
            => ShipLayoutVersion >= 2 && ColonyRules.IsFinite(crew) && crew >= 0
                && population + crew <= ShipRules.CrewCapacity + .001f;

        public bool ReceiveTenderCrew(float crew)
        {
            if (!CanReceiveTenderCrew(crew)) return false;
            population += crew;
            workforce = population * workforceRatio;
            RefreshReadout();
            return true;
        }

        public bool SetTenderCrew(float crew)
        {
            if (ShipLayoutVersion < 2 || !ColonyRules.IsFinite(crew) || crew < 250 || crew > ShipRules.CrewCapacity) return false;
            population = crew;
            workforce = population * workforceRatio;
            RefreshReadout();
            return true;
        }

        public void BeginLanderDescent()
        {
            if (ShipLayoutVersion < 2 || planet == null) return;
            foreach (var visual in buildingVisuals)
            {
                var descent = visual != null ? visual.GetComponent<LandingVesselDescent>() : null;
                if (descent == null) continue;
                descent.Begin(visual.transform.position, visual.transform.rotation, planet.transform.TransformDirection(storedNormal));
                return;
            }
        }

        private void BuildServiceRoads()
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var roadTriangles = new List<int>();
            var markingTriangles = new List<int>();
            foreach (var visual in buildingVisuals)
            {
                var end = settlementAnchor.InverseTransformPoint(visual.transform.position);
                end.y = 0;
                for (var section = 0; section < 12; section++)
                {
                    var a = end * (section / 12f); var b = end * ((section + 1) / 12f);
                    AddRoadStrip(a, b, 26f * BuildingVisuals.WorldScale, 0.6f, roadTriangles, vertices, normals, uv);
                    if (section % 2 == 0)
                        AddRoadStrip(a, Vector3.Lerp(a, b, 0.6f), 1.5f * BuildingVisuals.WorldScale, 0.7f,
                            markingTriangles, vertices, normals, uv);
                }
            }
            serviceRoadMesh = new Mesh { name = "Surface-conforming service causeways" };
            serviceRoadMesh.SetVertices(vertices); serviceRoadMesh.SetNormals(normals); serviceRoadMesh.SetUVs(0, uv);
            serviceRoadMesh.subMeshCount = 2;
            serviceRoadMesh.SetTriangles(roadTriangles, 0); serviceRoadMesh.SetTriangles(markingTriangles, 1);
            serviceRoadMesh.RecalculateBounds();
            serviceRoads = new GameObject("Core service causeways", typeof(MeshFilter), typeof(MeshRenderer));
            serviceRoads.transform.SetParent(planet.transform, false);
            serviceRoads.GetComponent<MeshFilter>().sharedMesh = serviceRoadMesh;
            serviceRoads.GetComponent<MeshRenderer>().sharedMaterials = BuildingVisuals.ServiceRoadMaterials;
            serviceRoads.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void AddRoadStrip(Vector3 a, Vector3 b, float width, float elevation, List<int> triangles,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv)
        {
            var side = Vector3.Cross(Vector3.up, (b - a).normalized) * width * 0.5f;
            var start = vertices.Count;
            foreach (var p in new[] { a - side, b - side, b + side, a + side })
            {
                var direction = planet.transform.InverseTransformPoint(settlementAnchor.TransformPoint(p)).normalized;
                vertices.Add(planet.EvaluateSurfacePoint(direction) + direction * elevation);
                normals.Add(direction);
            }
            uv.AddRange(new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        private void ClearVisuals()
        {
            residentialGeometryRevision++;volumeRevision=-1;residentialVolumes.Clear();
            if (serviceRoads != null) Destroy(serviceRoads);
            if (serviceRoadMesh != null) Destroy(serviceRoadMesh);
            foreach (var visual in buildingVisuals)
            {
                if (visual != null)
                {
                    Destroy(visual);
                }
            }

            buildingVisuals.Clear();
            coreBuildings.Clear();
        }

        private void OnDestroy()
        {
            if (serviceRoadMesh != null) Destroy(serviceRoadMesh);
        }

        private void EnsureAnchor(Vector3 position, Vector3 normal)
        {
            // A prior planet may already be queued for destruction during new/load.
            // Create a fresh anchor instead of reparenting part of that doomed hierarchy.
            if (settlementAnchor != null && settlementAnchor.parent != planet.transform)
            {
                Destroy(settlementAnchor.gameObject);
                settlementAnchor = null;
            }
            if (settlementAnchor == null)
            {
                var anchorGO = new GameObject("SettlementAnchor");
                settlementAnchor = anchorGO.transform;
            }

            settlementAnchor.SetParent(planet.transform, false);
            settlementAnchor.position = position;
            settlementAnchor.rotation = Quaternion.FromToRotation(Vector3.up, normal.normalized);
        }

    }
}
