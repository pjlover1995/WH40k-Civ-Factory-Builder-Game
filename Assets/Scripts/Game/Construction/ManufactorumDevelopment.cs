using UnityEngine;
using WH30K.Planet;
using WH30K.Sim;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;

namespace WH30K.Gameplay.Construction
{
    public sealed class ManufactorumDevelopment : MonoBehaviour
    {
        [SerializeField] private ManagedBuilding owner;
        [SerializeField] private Settlement settlement;
        [SerializeField] private ResourceSystem resources;
        [SerializeField] private LODPlanet planet;
        private ManufactorumVisual visual;
        public string Waiting { get; private set; } = "Approve warehouse expansion";
        public ManufactorumState Data => owner.State.manufactorum;
        public bool CanExpand => !owner.State.offline && !owner.State.improving && Data.project == null;
        public static Bounds BayBounds(IndustrialBay bay)
        {
            var bounds = new Bounds(new Vector3(bay.x * 2.1f, .35f, bay.z * 1.7f), new Vector3(2.1f, 1.5f, 1.7f));
            if (System.Math.Abs((long)bay.x) + System.Math.Abs((long)bay.z) == 1)
                bounds.Encapsulate(new Vector3(bay.x * .38f, 0, bay.z * .38f));
            return bounds;
        }
        public void Configure(ManagedBuilding building, Settlement colony, ResourceSystem stockpile)
        {
            owner = building; settlement = colony; resources = stockpile;
            planet = GetComponentInParent<LODPlanet>();
            var normalized=owner.State; normalized.manufactorum=ManufactorumRules.NormalizeLoaded(normalized.manufactorum); owner.SetResidentialState(normalized);
            if (owner.State.manufactorum == null)
            {
                var state = owner.State;
                state.manufactorum = new ManufactorumState { inheritedCapacity = 1 + state.level * .25f };
                owner.SetResidentialState(state);
            }
            visual = GetComponent<ManufactorumVisual>() ?? gameObject.AddComponent<ManufactorumVisual>();
            visual.Configure(owner); owner.ResidentialChanged();
        }
        public void Restored() => Configure(owner, settlement, resources);
        public Bounds Bounds(bool reserve)
        {
            var bounds = new Bounds(new Vector3(0, .5f, 0), Vector3.one);
            foreach (var bay in Data.bays) bounds.Encapsulate(BayBounds(bay));
            if (reserve && Data.project != null) bounds.Encapsulate(BayBounds(Data.project));
            return bounds;
        }
        public bool SiteClear(IndustrialBay bay)
        {
            var bounds = BayBounds(bay);
            if (!settlement.ResidentialCellClear(owner, bounds)) return false;
            if (planet == null) return false;
            // Sample the full warehouse foundation, including machinery and connecting aprons.
            for (var x = 0; x <= 4; x++) for (var z = 0; z <= 4; z++)
            {
                var local = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, x / 4f), 0, Mathf.Lerp(bounds.min.z, bounds.max.z, z / 4f));
                var direction = planet.transform.InverseTransformPoint(transform.TransformPoint(local)).normalized;
                var surfaceLocal = planet.EvaluateSurfacePoint(direction);
                var surface = planet.transform.TransformPoint(surfaceLocal);
                var height = transform.InverseTransformPoint(surface).y;
                if (height > .06f || height < -.4f || surfaceLocal.magnitude <= planet.SeaLevel) return false;
            }
            var industry = settlement.GetComponent<IndustrySystem>();
            foreach (var deposit in industry.Deposits) if (deposit.Excavation != null)
            {
                var center = planet.transform.InverseTransformPoint(transform.TransformPoint(bounds.center));
                var radius = new Vector2(bounds.extents.x * transform.lossyScale.x, bounds.extents.z * transform.lossyScale.z).magnitude;
                if (planet.SurfaceDistance(center, deposit.Direction) < deposit.Excavation.majorRadius + radius + 10) return false;
            }
            return true;
        }
        public bool Request()
        {
            if (!CanExpand) return false;
            var bay = ManufactorumRules.Candidate(Data, SiteClear);
            if (bay == null) { Waiting = "No clear connected land; clear obstacles or prepare terrain"; return false; }
            var industry = settlement.GetComponent<IndustrySystem>();
            if (!industry.TrySpendConstruction(ManufactorumRules.ExpansionSupplies, ManufactorumRules.ExpansionPlasteel))
            { Waiting = "Needs 20 components + 2 plasteel"; return false; }
            Data.project = bay; Data.work = 0;
            Waiting = "Warehouse site reserved / materials paid";
            owner.ResidentialChanged(); visual.Rebuild(); return true;
        }
        public bool UpgradeTechnology()
        {
            var state = owner.State;
            if (state.offline || state.improving || Data.project != null || state.level >= 2) return false;
            if (!settlement.GetComponent<IndustrySystem>().TrySpendConstruction(ManufactorumRules.TierSupplies(state.level), ManufactorumRules.TierPlasteel(state.level)))
            { Waiting = "Insufficient components / advanced plasteel for technology tier"; return false; }
            state.level++; owner.SetResidentialState(state); owner.ResidentialChanged(); return true;
        }
        public void Simulate(float seconds)
        {
            // Finish a historical paid generic improvement without discarding progress or auto-starting another.
            if (owner.State.improving)
            {
                var state = owner.State;
                var debit = AdaptiveBuildingRules.Advance(ref state, owner.Id, seconds, 0,
                    resources.Stockpile / ColonyRules.LegacyProjectSupplyScale, true, out var complete);
                if (debit > 0 && !resources.TrySpend(debit * ColonyRules.LegacyProjectSupplyScale)) return;
                if (complete) Data.inheritedCapacity += .25f;
                owner.SetResidentialState(state); if (complete) owner.ResidentialChanged(); return;
            }
            if (visual == null) Configure(owner,settlement,resources);
            if (Data.project == null) return;
            var clear = SiteClear(Data.project);
            Waiting = owner.State.offline ? "Suspended / paid work retained" : !clear ? "Site obstructed / paid work retained" : "Constructing warehouse / materials paid";
            if (ManufactorumRules.Advance(Data, seconds, owner.State.offline, clear))
            {
                Waiting = "Warehouse operational / approve another expansion";
                owner.ResidentialChanged(); visual.Rebuild();
            }
            else visual.UpdateConstruction();
        }
        private string CurrentStatus => owner.State.offline ? "Suspended" : owner.State.improving
            ? $"Historical improvement {owner.State.improvementWork / AdaptiveBuildingRules.Duration(owner.State):P0} / paid work retained"
            : Data.project == null && Data.bays.Count > 0 && !settlement.GetComponent<IndustrySystem>().CanAffordConstruction(ManufactorumRules.Upkeep(Data,1,1),0)
                ? "ANNEXES IDLE / need upkeep components" : Waiting;
        public string Status => $"WAREHOUSE CAPACITY {ManufactorumRules.Scale(Data):0.##}x / {Data.bays.Count} added bays\n" +
            (Data.project != null ? $"Construction {Data.work / ManufactorumRules.Duration:P0} / paid 20 components + 2 plasteel\n" : "Expand: 20 components + 2 plasteel / 60s\n") +
            $"Each bay: +100% base throughput, power and jobs; 1 supply/month\n{CurrentStatus}";
    }
}
