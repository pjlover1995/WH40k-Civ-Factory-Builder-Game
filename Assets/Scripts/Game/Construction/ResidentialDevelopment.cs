using System;
using System.Collections.Generic;
using UnityEngine;
using WH30K.Sim;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;
using WH30K.Planet;

namespace WH30K.Gameplay.Construction
{
    /// <summary>Scene adapter. All preview and construction decisions use ResidentialRules.Validate.</summary>
    public sealed class ResidentialDevelopment : MonoBehaviour
    {
        [SerializeField] private ManagedBuilding owner;
        [SerializeField] private Settlement settlement;
        [SerializeField] private ResourceSystem resources;
        [SerializeField] private LODPlanet planet;
        private ResidentialVisual visual;
        private float refreshAt;
        private float replanAt;
        private static int lastAutomaticSearchFrame=-1;
        private static readonly Queue<(ResidentialDevelopment,int)> planningQueue=new Queue<(ResidentialDevelopment,int)>();
        private HabSearch planningSearch;
        private int planningGeneration,planningSurfaceRevision;
        private int boundsCount=-1;
        private HabPlacement boundsProject;
        private Bounds completedBounds,reservedBounds;
        public int PlanningSearches {get;private set;}
        private List<HabCandidate> candidates = new List<HabCandidate>();
        private readonly Dictionary<(int,int,int,int,bool),bool> siteCache=new Dictionary<(int,int,int,int,bool),bool>();
        private readonly Dictionary<Vector2Int,float> groundCache=new Dictionary<Vector2Int,float>();
        private int surfaceRevision=-1;
        private float foundationSignature;
        private static ResidentialLibrary library;
        public static ResidentialLibrary Library
        {
            get
            {
                if(library==null)
                {
                    var json=UnityEngine.Resources.Load<TextAsset>("ResidentialModules");
                    if(json==null)throw new InvalidOperationException("ResidentialModules resource is missing.");
                    library=JsonUtility.FromJson<ResidentialLibrary>(json.text);
                    if(library==null || !library.Valid())throw new InvalidOperationException("Residential module configuration is invalid.");
                }
                return library;
            }
        }
        public ResidentialState Data=>owner.State.residential;
        public IReadOnlyList<HabCandidate> Candidates=>candidates;
        public ResidentialInputs Inputs {get;private set;}
        public int Revision {get;private set;}
        public int SurfaceRevision=>planet!=null?planet.SurfaceRevision:0;
        public string Waiting {get;private set;}
        public int SelectedModule {get;set;}=-1;
        public bool Selected {get;private set;}
        public bool CanRequest=>Data!=null && Data.project==null && !owner.State.offline && candidates.Count>0
            && (Data.legacyDuration>0 || candidates[0].affordable);
        public void Configure(ManagedBuilding building,Settlement colony,ResourceSystem stockpile)
        {
            owner=building;settlement=colony;resources=stockpile;
            planet=settlement.GetComponent<PlanetBootstrap>().ActivePlanet;
            if(planet==null)planet=GetComponentInParent<LODPlanet>();
            // Establish a level deck above the actual founding footprint on both new worlds and load.
            var raise=0f;
            for(var x=-2;x<=2;x++)for(var z=-2;z<=2;z++)raise=Mathf.Max(raise,GroundLocal(x*.2f,z*.2f)-.035f);
            if(raise>0)transform.position+=transform.up*(raise*transform.lossyScale.y);
            groundCache.Clear();
            var op=owner.State;
            var local=planet!=null?planet.transform.InverseTransformPoint(transform.position).normalized:transform.position.normalized;
            var identity=$"hab:{Mathf.RoundToInt(local.x*1000000)}:{Mathf.RoundToInt(local.y*1000000)}:{Mathf.RoundToInt(local.z*1000000)}";
            ResidentialRules.Migrate(ref op,ResidentialRules.BlockSeed(planet!=null?planet.Seed:1234,identity),identity);
            owner.SetResidentialState(op);
            visual=GetComponent<ResidentialVisual>()??gameObject.AddComponent<ResidentialVisual>();
            visual.Configure(this);Invalidate();
        }
        public void Restored()
        {
            Configure(owner,settlement,resources);
        }
        public void Invalidate(){planningSearch=null;planningGeneration++;refreshAt=0;replanAt=0;siteCache.Clear();groundCache.Clear();candidates.Clear();boundsCount=-1;Revision++;visual?.RefreshSelection();}
        public void SetSelected(bool selected){Selected=selected;if(!selected)SelectedModule=-1;else refreshAt=0;visual?.RefreshSelection();}
        private void Update()
        {
            if(owner==null || settlement==null)return;
            ResidentialRules.NormalizeLoaded(Data);
            if(visual==null)
            {
                var active=settlement.GetComponent<PlanetBootstrap>().ActivePlanet;
                if(active!=null)planet=active;
                if(planet==null)planet=GetComponentInParent<LODPlanet>();
                visual=GetComponent<ResidentialVisual>();if(visual==null)visual=gameObject.AddComponent<ResidentialVisual>();
                visual.Configure(this);Invalidate();
            }
            if(!settlement.HasActiveSimulation)return;
            if(planet!=null && surfaceRevision!=planet.SurfaceRevision)
            {
                surfaceRevision=planet.SurfaceRevision;Invalidate();
                var signature=0f;
                foreach(var m in Data.modules)if(m.floor==0)foreach(var c in ResidentialRules.Cells(m,Library))
                    signature+=GroundLocal((c.x+.5f)*.2f,(c.z+.5f)*.2f);
                if(Mathf.Abs(signature-foundationSignature)>.0001f){foundationSignature=signature;visual?.Rebuild();}
            }
            RefreshWhenDue();
        }
        private void OnDisable(){planningSearch=null;planningGeneration++;refreshAt=0;}
        private void RefreshWhenDue()
        {
            PumpPlanningQueue();
            if(Time.unscaledTime<refreshAt || planningSearch!=null || Data.project!=null || owner.State.offline && !Selected
                || !Selected && owner.State.manualImprovements)return;
            PrepareInputs();PlanningSearches++;
            planningSurfaceRevision=SurfaceRevision;planningSearch=ResidentialRules.BeginCandidates(Data,Library,Inputs,SiteValid);
            planningQueue.Enqueue((this,++planningGeneration));
            PumpPlanningQueue();
        }
        private static void PumpPlanningQueue()
        {
            if(lastAutomaticSearchFrame==Time.frameCount)return;
            while(planningQueue.Count>0)
            {
                var item=planningQueue.Dequeue();var block=item.Item1;
                if(block==null || !block.isActiveAndEnabled || block.planningSearch==null || block.planningGeneration!=item.Item2)continue;
                if(block.planningSurfaceRevision!=block.SurfaceRevision){block.Invalidate();continue;}
                lastAutomaticSearchFrame=Time.frameCount;
                if(block.planningSearch.Step())
                {
                    block.candidates=block.planningSearch.Result;block.planningSearch=null;
                    block.refreshAt=Time.unscaledTime+2;block.Revision++;block.visual?.RefreshSelection();
                }
                else planningQueue.Enqueue(item);
                return;
            }
        }
        private void PrepareInputs()
        {
            if(planet==null)planet=settlement.GetComponent<PlanetBootstrap>().ActivePlanet;
            siteCache.Clear();groundCache.Clear();Inputs=settlement.ResidentialContext(owner);
            var input=Inputs;input.preference=owner.State.growthPlan;
            if(Data.legacyDuration>0)input.demand=new[]{1f,1f,1f};
            Inputs=input;
        }
        public void RefreshCandidates()
        {
            planningSearch=null;planningGeneration++;PrepareInputs();
            if(Data.project==null){PlanningSearches++;candidates=ResidentialRules.Candidates(Data,Library,Inputs,SiteValid);}
            else candidates.Clear();
            refreshAt=Time.unscaledTime+2f;Revision++;
            visual?.RefreshSelection();
        }
        public bool Request()
        {
            if(candidates.Count==0)RefreshCandidates();
            return RequestCurrentCandidate();
        }
        private bool RequestCurrentCandidate()
        {
            var input=settlement.ResidentialContext(owner);input.preference=owner.State.growthPlan;Inputs=input;
            if(!CanRequest || !ResidentialRules.Request(Data,candidates[0],Library,input,SiteValid))return false;
            Invalidate();owner.ResidentialChanged();visual?.Rebuild();return true;
        }
        public void Simulate(float seconds)
        {
            if(Data==null)return;
            ResidentialRules.NormalizeLoaded(Data);
            if(owner.State.offline){Waiting="Suspended";return;}
            if(planet!=null && surfaceRevision!=planet.SurfaceRevision){groundCache.Clear();siteCache.Clear();refreshAt=0;}
            RefreshWhenDue();
            if(Data.project==null)
            {
                if(!owner.State.manualImprovements && !owner.State.offline && CanRequest && seconds>0)RequestCurrentCandidate();
                Waiting=Data.project!=null?"Construction approved":planningSearch!=null?"Planning connected expansion":owner.State.offline?"Suspended":Data.modules.Count>=Library.moduleLimit?"Module limit reached":candidates.Count==0?"No demanded, connected site fits":
                    !candidates[0].affordable && Data.legacyDuration<=0?"Waiting for full project funding":"Awaiting approval";
                return;
            }
            if(!ColonyRules.IsFinite(seconds) || seconds<=0)return;
            Data.workAccumulator+=seconds;
            if(Data.workAccumulator<.25f)return;
            seconds=Data.workAccumulator;Data.workAccumulator=0;
            // Invalidation and surface revisions clear external geometry caches before any payment.
            var inputs=settlement.ResidentialContext(owner);
            inputs.preference=owner.State.growthPlan;Inputs=inputs;
            var labour=ResidentialRules.Labour(inputs);
            var legacy=Data.project.legacy;
            var debit=ResidentialRules.Advance(Data,Library,seconds,resources.Stockpile / ColonyRules.LegacyProjectSupplyScale,labour,owner.State.offline,SiteValid,out var complete,out var reason);
            if(reason!=null && !owner.State.offline && Time.unscaledTime>=replanAt)
            {
                replanAt=Time.unscaledTime+2;
                if(ResidentialRules.Replan(Data,Library,inputs,SiteValid))
                {
                    owner.ResidentialChanged();visual?.Rebuild();
                    debit=ResidentialRules.Advance(Data,Library,seconds,resources.Stockpile / ColonyRules.LegacyProjectSupplyScale,labour,false,SiteValid,out complete,out reason);
                }
                else reason="No alternative site fits; paid work retained";
            }
            Waiting=owner.State.offline?"Suspended":reason??(labour<=0?"Waiting for workforce, production or services":resources.Stockpile<=0?"Waiting for construction supplies":"Constructing at reserved site");
            if(debit>0 && !resources.TrySpend(debit * ColonyRules.LegacyProjectSupplyScale))throw new InvalidOperationException("Residential supply debit lost main-thread atomicity.");
            if(complete)
            {
                // The pending historical upgrade retained its original price and work; deliver its economic tier once.
                if(legacy)owner.CompleteLegacyResidentialTier();
                owner.ResidentialChanged();Invalidate();visual?.Rebuild();
            }
            else visual?.UpdateConstruction();
        }
        public string Status
        {
            get
            {
                var provision=settlement.ResidentialProvision();var demand=Inputs.demand??new float[3];
                var text=$"Block housing {owner.Economy.capacity:0}; jobs {owner.Economy.jobs:0}\nColony housing / unmet demand\nWorkers {provision[0]:0} / {demand[0]:0}  Admin {provision[1]:0} / {demand[1]:0}\nLeadership {provision[2]:0} / {demand[2]:0}\n";
                if(Data.project!=null)
                {
                    var p=Data.project;var d=Library.Find(p.placement.design);
                    text+=$"{d.name} {p.work/p.duration:P0}\n";
                    if(!p.legacy && p.placement.housing>0)text+=$"+{p.placement.housing*owner.HousingFocusFactor:0} housing on completion\n";
                    text+=$"Supplies {p.paid * ColonyRules.LegacyProjectSupplyScale:0.##}/{p.cost * ColonyRules.LegacyProjectSupplyScale:0.##}; work left {p.duration-p.work:0}s\n{Waiting}";
                    if(p.relocations>0)text+=$"\nRedirected {p.relocations} time(s); paid work preserved";
                }
                else text+=(Data.legacyDuration>0?$"Legacy supplies {Data.legacyPaid * ColonyRules.LegacyProjectSupplyScale:0.##}/{Data.legacyCost * ColonyRules.LegacyProjectSupplyScale:0.##}; site pending\n":"")+
                    (candidates.Count>0?candidates[0].explanation+($"\n{candidates[0].cost * ColonyRules.LegacyProjectSupplyScale:0.##} supplies; "+(candidates[0].affordable?"funded":"unaffordable")):Waiting);
                var selected=Data.modules.Find(m=>m.serial==SelectedModule);
                if(selected!=null)
                {
                    var d=Library.Find(selected.design);
                    text=$"{d.name} / {ResidentialRules.ClassName(d.residentClass)}\nCapacity {(selected.serial==0?owner.BaseHousing:selected.legacyHousingInCore?0:selected.housing*owner.HousingFocusFactor):0}; {d.role}\nParent {selected.parent}; floor {selected.floor}; lift / terrace access\n"+text;
                    if(selected.legacyHousingInCore)text="Legacy housing credited in founding tier\n"+text;
                    text=d.roof+"\n"+text;
                }
                return text;
            }
        }
        public Bounds Bounds(bool reserve)
        {
            var project=Data.project?.placement;
            if(boundsCount!=Data.modules.Count || boundsProject!=project)
            {
                completedBounds=new Bounds(new Vector3(0,.2f,0),new Vector3(.8f,.4f,.8f));
                foreach(var m in Data.modules)foreach(var c in ResidentialRules.Cells(m,Library))completedBounds.Encapsulate(CellBounds(c));
                reservedBounds=completedBounds;
                if(project!=null)foreach(var c in ResidentialRules.Cells(project,Library))reservedBounds.Encapsulate(CellBounds(c));
                boundsCount=Data.modules.Count;boundsProject=project;
            }
            return reserve?reservedBounds:completedBounds;
        }
        public static Bounds CellBounds(HabCell c)=>new Bounds(new Vector3((c.x+.5f)*ResidentialRules.CellSize,
            ResidentialRules.Deck+(c.bottom+c.top)*.5f*ResidentialRules.FloorHeight,(c.z+.5f)*ResidentialRules.CellSize),
            new Vector3(ResidentialRules.CellSize,(c.top-c.bottom)*ResidentialRules.FloorHeight,ResidentialRules.CellSize));
        public float GroundLocal(float x,float z)
        {
            if(planet==null)return 0;
            var key=new Vector2Int(Mathf.RoundToInt(x*10000),Mathf.RoundToInt(z*10000));
            if(groundCache.TryGetValue(key,out var height))return height;
            var point=transform.TransformPoint(new Vector3(x,0,z));
            var n=planet.transform.InverseTransformPoint(point).normalized;
            height=transform.InverseTransformPoint(planet.transform.TransformPoint(planet.EvaluateSurfacePoint(n))).y;
            groundCache[key]=height;return height;
        }
        private bool SiteValid(HabCell cell)
        {
            var key=(cell.x,cell.z,cell.bottom,cell.top,cell.passage);
            if(siteCache.TryGetValue(key,out var cached))return cached;
            var local=CellBounds(cell);var clear=true;
            if(cell.bottom==0)
            {
                for(var dx=0;dx<=2;dx++)for(var dz=0;dz<=2;dz++)
                {
                    var h=GroundLocal((cell.x+dx*.5f)*ResidentialRules.CellSize,(cell.z+dz*.5f)*ResidentialRules.CellSize);
                    if(h>ResidentialRules.Deck-.008f || h<-Library.maxFoundationDepth)clear=false;
                }
            }
            if(clear)
            {
                if(!cell.passage)local.Expand(new Vector3(2*Library.neighbourClearance/transform.lossyScale.x,0,2*Library.neighbourClearance/transform.lossyScale.z));
                clear=settlement.ResidentialCellClear(owner,local);
            }
            // Do not establish new foundations in an existing or next excavating sector's safety envelope.
            var industry=settlement.GetComponent<IndustrySystem>();
            if(clear && cell.bottom==0 && industry!=null)
            {
                var world=transform.TransformPoint(local.center);
                foreach(var deposit in industry.Deposits) if(deposit.Excavation!=null)
                {
                    var angular=planet.SurfaceDistance(planet.transform.InverseTransformPoint(world),deposit.Direction);
                    if(angular<deposit.Excavation.majorRadius+Library.excavationClearance){clear=false;break;}
                }
            }
            siteCache[key]=clear;return clear;
        }
        public static bool ValidateLoaded(ResidentialState data)
            => ResidentialRules.ValidLayout(data,Library);
    }
}
