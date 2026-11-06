using UnityEngine;
using WH30K.Sim;
using WH30K.Sim.Environment;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;

namespace WH30K.Gameplay.Construction
{
    public sealed class ManagedBuilding : MonoBehaviour
    {
        [SerializeField] private BuildingEconomy basicEconomy;
        [SerializeField] private EnvironmentImpact basicImpact;
        [SerializeField] private ResourceSystem resources;
        [SerializeField] private Settlement settlement;
        private BuildingEconomy economy;
        private EnvironmentImpact impact;
        private AdaptiveBuildingVisual growthVisual;
        private FarmVisual farmVisual;
        [SerializeField] private ManufactorumDevelopment industrial;
        public ManufactorumDevelopment Industrial => industrial;
        private float farmGrowthRetry;
        private Bounds completedBounds, reservedBounds;
        public bool GrowthBlocked { get; private set; }
        [field: SerializeField] public string Id { get; private set; }
        [SerializeField] private string definitionTitle;
        public string DefinitionTitle => definitionTitle;
        public ManagedBuilding EntityRoot { get; private set; }
        public string DisplayName => EntityRoot!=null && EntityRoot!=this ? EntityRoot.DisplayName : string.IsNullOrEmpty(State.instanceName)?definitionTitle:State.instanceName;
        internal FarmPoint[] PlannedParcel;
        [field: SerializeField] public BuildingOperationState State { get; private set; }
        [SerializeField] private ResidentialDevelopment residential;
        public ResidentialDevelopment Residential => residential;
        public int FarmLayoutSeed
        {
            get
            {
                if (State.farm.layoutSeed == 0)
                {
                    var planet = GetComponentInParent<WH30K.Planet.LODPlanet>();
                    var direction = planet != null ? planet.transform.InverseTransformPoint(transform.position).normalized : transform.position.normalized;
                    State.farm.layoutSeed = FarmLayoutRules.Seed(planet != null ? planet.Seed : 0, direction.x, direction.y, direction.z);
                }
                return State.farm.layoutSeed;
            }
        }
        public float HousingFocusFactor => State.focus==1?1.4f:State.focus==2?.85f:1;
        public float BaseHousing => BuildingOperationRules.Economy(Id,basicEconomy,State).capacity;
        public float SurveyRadius { get; private set; }
        public BuildingEconomy Economy => economy;
        public EnvironmentImpact Impact => impact;
        private void RefreshOperation()
        {
            economy=BuildingOperationRules.Economy(Id,basicEconomy,State);impact=BuildingOperationRules.Impact(Id,basicImpact,State);
            if(Id=="HabBlock" && State.residential!=null && !State.offline)
            {
                var housing=ResidentialRules.AddedHousing(State.residential,ResidentialDevelopment.Library);var total=housing[0]+housing[1]+housing[2];
                economy.capacity+=total*HousingFocusFactor;
                economy.upkeep+=total*ResidentialDevelopment.Library.upkeepPerResident*(State.focus==1?1.25f:State.focus==2?1.1f:1);
            }
        }
        public void SetResidentialState(BuildingOperationState state){State=state;RefreshOperation();}
        public void ResidentialChanged(){RefreshOperation();RefreshGrowthBounds();settlement.RefreshReadout();settlement.NotifyResidentialGeometry();}
        public void CompleteLegacyResidentialTier(){var s=State;s.level=Mathf.Min(2,s.level+1);State=s;}
        public float UpgradeCost => Residential!=null ? Residential.Candidates.Count>0?Residential.Candidates[0].cost:0 : BuildingOperationRules.UpgradeCost(State);
        public bool CanUpgrade => Id == "Manufactorum" ? Industrial != null && Industrial.CanExpand : Id != "ColonyShip" && Id != "Farm" && (Residential!=null ? Residential.CanRequest : State.level < 2 && !State.improving && !State.offline && resources != null);
        public Bounds GrowthBounds(bool reserve = true) => Industrial != null ? Industrial.Bounds(reserve) : Id=="Farm" || Id=="ColonyShip" ? GetComponent<MeshFilter>().sharedMesh.bounds : Residential!=null && State.residential!=null ? Residential.Bounds(reserve) : reserve ? reservedBounds : completedBounds;
        public float ShipFootprintDistance(Vector3 point)
        {
            var bounds = GrowthBounds(); var local = transform.InverseTransformPoint(point);
            var x = Mathf.Max(0, Mathf.Abs(local.x - bounds.center.x) - bounds.extents.x) * transform.lossyScale.x;
            var z = Mathf.Max(0, Mathf.Abs(local.z - bounds.center.z) - bounds.extents.z) * transform.lossyScale.z;
            return Mathf.Sqrt(x * x + z * z);
        }
        private void RefreshGrowthBounds()
        {
            completedBounds = AdaptiveBuildingVisual.BoundsFor(State, false);
            reservedBounds = AdaptiveBuildingVisual.BoundsFor(State, true);
        }
        public float FootprintRadius
        {
            get
            {
                var b = GrowthBounds();
                return new Vector2((Mathf.Abs(b.center.x) + b.extents.x) * transform.lossyScale.x,
                    (Mathf.Abs(b.center.z) + b.extents.z) * transform.lossyScale.z).magnitude;
            }
        }
        public float ReservedFootprintRadius
        {
            get
            {
                var b = GrowthBounds(true);
                return new Vector2((Mathf.Abs(b.center.x) + b.extents.x) * transform.lossyScale.x,
                    (Mathf.Abs(b.center.z) + b.extents.z) * transform.lossyScale.z).magnitude;
            }
        }
        public string ImprovementStatus
        {
            get
            {
                if(Industrial!=null)return Industrial.Status;
                if(Id=="Farm")return "Standard technology / automatic field growth / +20% base capacity per field";
                if(Residential!=null)return Residential.Status;
                if (State.level >= 2) return "Tier III complete / maximum prototype tier";
                if (!State.improving) return $"{(State.manualImprovements ? "Manual" : "Auto")}: {State.milestoneSeconds:0}/{AdaptiveBuildingRules.MilestoneDuration(Id, State):0}s at 50% {(Id == "HabBlock" ? "occupancy" : "staffing")}\nNext: {UpgradeCost * ColonyRules.LegacyProjectSupplyScale:0.##} supplies / {AdaptiveBuildingRules.Duration(State):0}s";
                var remaining = AdaptiveBuildingRules.Duration(State) - State.improvementWork;
                return $"Improving {State.improvementWork / AdaptiveBuildingRules.Duration(State):P0} / {remaining:0}s work left\n" +
                    (State.offline ? "Suspended" : GrowthBlocked ? "Waiting for expansion space" : resources.Stockpile <= 0 ? "Waiting for construction supplies" : "Using construction supplies as work progresses");
            }
        }

        public void Configure(string id, string title, BuildingEconomy economy, EnvironmentImpact impact,
            ResourceSystem stockpile, Settlement owner, BuildingOperationState state = default, float surveyRadius = 0f)
        {
            Id = id; definitionTitle = id=="HabBlock"?"Residential Block":title; EntityRoot=this; basicEconomy = economy; basicImpact = impact;
            resources = stockpile; settlement = owner; if(id=="Farm")state.farm=FarmRules.Clone(state.farm); State = state; settlement.EnsureIdentity(this); if(id=="Farm" && string.IsNullOrEmpty(State.farm.entityId))State.farm.entityId=State.instanceId; SurveyRadius = Mathf.Max(0f, surveyRadius);
            RefreshOperation();
            RefreshGrowthBounds();
            var bounds = GetComponent<MeshFilter>().sharedMesh.bounds;
            var collider = gameObject.AddComponent<BoxCollider>();
            collider.center = bounds.center; collider.size = bounds.size;
            if(Id=="HabBlock")
            {
                residential=gameObject.AddComponent<ResidentialDevelopment>();residential.Configure(this,settlement,resources);
            }
            else if (Id == "Manufactorum") { industrial=GetComponent<ManufactorumDevelopment>() ?? gameObject.AddComponent<ManufactorumDevelopment>(); Industrial.Configure(this,settlement,resources); }
            else if (Id == "Farm") { farmVisual=gameObject.AddComponent<FarmVisual>(); farmVisual.Configure(this); }
            else if (Id != "ColonyShip") {growthVisual = gameObject.AddComponent<AdaptiveBuildingVisual>();growthVisual.Configure(this);}
        }
        public void Restore(BuildingOperationState state) { if(Id=="Farm")state.farm=FarmRules.Clone(state.farm); State = state; settlement.EnsureIdentity(this); if(Id=="Farm" && string.IsNullOrEmpty(State.farm.entityId))State.farm.entityId=State.instanceId; farmVisual?.Rebuild(); Industrial?.Restored(); Residential?.Restored(); RefreshOperation(); RefreshGrowthBounds(); growthVisual?.Rebuild(); settlement.NotifyResidentialGeometry(); }
        public bool SetFocus(int focus)
        {
            if (Id == "ColonyShip") return false;
            if (focus < 0 || focus > 2) return false;
            if(Id=="Farm")
            {
                foreach(var site in settlement.GetComponent<IndustrySystem>().FarmSites(this))site.ApplyFarmOperation(focus,site.State.offline);
                settlement.RefreshReadout(); return true;
            }
            var state = State; state.focus = focus; State = state;
            RefreshOperation();
            Residential?.Invalidate();
            settlement.RefreshReadout(); settlement.NotifyResidentialGeometry(); return true;
        }
        public bool Upgrade()
        {
#if UNITY_EDITOR
            if(Id=="Farm") return DevAddFarmField();
#endif
            if(Industrial!=null)return Industrial.Request();
            if(Residential!=null)return Residential.Request();
            if (!CanUpgrade) return false;
            var state = State;
            if (!AdaptiveBuildingRules.Request(ref state)) return false;
            var previous=State;State=state;RefreshGrowthBounds();
            if(!settlement.CanExpand(this)){State=previous;RefreshGrowthBounds();return false;}
            growthVisual?.Rebuild();settlement.NotifyResidentialGeometry();return true;
        }
        public bool SetGrowthPlan(int plan)
        {
            if ((Residential!=null?State.residential.project!=null:State.improving || State.level>=2) || plan < 0 || plan > 2) return false;
            var state = State; state.growthPlan = plan; State = state; Residential?.Invalidate(); return true;
        }
        public void ToggleAutomaticImprovement()
        {
            var state = State; state.manualImprovements = !state.manualImprovements; State = state;
        }
        private void Update()
        {
            if (settlement == null || !settlement.HasActiveSimulation) return;
            // Restore derived caches after Unity domain reload without changing persisted decisions.
            if(economy.capacity==0 && economy.jobs==0 && !State.offline)RefreshOperation();
            SimulateImprovement(ColonyClock.DeltaTime, settlement.BuildingUtilisation(this));
        }
        public void SimulateImprovement(float seconds, float utilisation)
        {
            if (Id == "ColonyShip" || Id == "Farm") return;
            if(Industrial!=null){Industrial.Simulate(seconds);return;}
            if(Residential!=null){Residential.Simulate(seconds);return;}
            var previous = State; var state = previous;
            GrowthBlocked = state.improving && !settlement.CanExpand(this);
            var debit = AdaptiveBuildingRules.Advance(ref state, Id, seconds, utilisation,
                resources.Stockpile / ColonyRules.LegacyProjectSupplyScale, !GrowthBlocked, out var completed);
            if (debit > 0 && !resources.TrySpend(debit * ColonyRules.LegacyProjectSupplyScale)) return;
            State = state;
            if(!previous.improving && state.improving)
            {
                RefreshGrowthBounds();
                if(!settlement.CanExpand(this)){state.improving=false;State=state;RefreshGrowthBounds();}
            }
            if (completed)
            {
                RefreshOperation(); settlement.RefreshReadout();
                settlement.GetComponent<WH30K.UI.NewGameMenu>()?.AppendEventLog(DisplayName + " improvement complete: tier " + (State.level + 1));
            }
            if (previous.improving != state.improving || previous.level != state.level)
            { RefreshGrowthBounds(); growthVisual?.Rebuild(); settlement.NotifyResidentialGeometry(); }
        }
        public void FarmChanged()
        {
            RefreshOperation(); farmVisual?.Rebuild(); RefreshGrowthBounds();
            settlement.NotifyResidentialGeometry(); settlement.RefreshReadout();
            settlement.GetComponent<IndustrySystem>()?.RefreshFarmEntities();
        }
        public bool FarmExpansionClear(float seconds)
        {
            farmGrowthRetry -= seconds;
            if (farmGrowthRetry > 0) return false;
            farmGrowthRetry = 5; // Bound repeated terrain/volume searches while a qualified farm is obstructed.
            return GetComponentInParent<WH30K.Planet.LODPlanet>() != null
                && settlement.GetComponent<IndustrySystem>().CanFarmLayout(transform, State.farm.addedFields + 1, this);
        }
#if UNITY_EDITOR
        // Explicit development override: bypasses six-month qualification AND land clearance for visual review.
        public bool DevAddFarmField()
        {
            if (Id != "Farm") return false;
            return settlement.GetComponent<IndustrySystem>().ExpandFarmEntity(this);
        }
#endif
        public void SetIdentity(string id,string name) { var state=State; state.instanceId=System.Guid.Parse(id).ToString("N");state.instanceName=name;State=state;gameObject.name=DisplayName; }
        public bool FarmExpansionDue(float seconds,bool first)
        {farmGrowthRetry-=seconds;if(!first && farmGrowthRetry>0)return false;farmGrowthRetry=5;return true;}
        public bool Rename(string name) => settlement.Rename(this,name);
        public void SetEntityRoot(ManagedBuilding root) { EntityRoot=root??this; if(Id=="Farm")State.farm.entityId=EntityRoot.State.instanceId; }
        public void ApplyFarmOperation(int focus,bool offline) { var state=State;state.focus=focus;state.offline=offline;State=state;RefreshOperation(); }
        public void CommitFarmParcel()
        {
            if(PlannedParcel==null)return;
            var parcels=new System.Collections.Generic.List<FarmParcel>(State.farm.claimedParcels??System.Array.Empty<FarmParcel>());
            parcels.Add(new FarmParcel{field=FarmRules.Fields(State.farm.addedFields)-1,polygon=PlannedParcel});
            State.farm.claimedParcels=parcels.ToArray();PlannedParcel=null;
        }
        public void ToggleOperation()
        {
            if(Id=="Farm")
            {
                var offline=!EntityRoot.State.offline;
                foreach(var site in settlement.GetComponent<IndustrySystem>().FarmSites(this))site.ApplyFarmOperation(EntityRoot.State.focus,offline);
                settlement.RefreshReadout(); return;
            }
            var state = State; state.offline = !state.offline; State = state;
            RefreshOperation();
            Residential?.Invalidate();
            settlement.RefreshReadout();
            settlement.NotifyResidentialGeometry();
        }
    }
}
