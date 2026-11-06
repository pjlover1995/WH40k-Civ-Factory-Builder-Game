using System;
using System.Collections.Generic;

namespace WH30K.Sim
{
    [Serializable] public struct HabColumn { public int x, z, height; }
    [Serializable] public sealed class HabDesign
    {
        public string id, name, role, roof;
        public int residentClass, capacity;
        public float cost, work, enclosure, serviceNeed;
        public bool upper, lift;
        public int roofStyle, facadeStyle;
        public string wallColor, roofColor, trimColor;
        public HabColumn[] columns;
    }
    [Serializable] public sealed class ResidentialLibrary
    {
        public float administrationShare = .18f, leadershipShare = .04f, demandHeadroom = 1.12f;
        public float creditMultiplier = 1, workMultiplier = 1, upkeepPerResident = .0125f;
        public float serviceRadius = 250, basicServices = .35f, maxFoundationDepth = .9f, excavationClearance = 14;
        public int moduleLimit = 32, radiusLimit = 12, heightLimit = 12;
        public int spacing = 2;
        public float galleryCost = 12, galleryWork = 6;
        public float neighbourClearance = 2;
        public HabDesign[] designs;
        public bool Valid()
        {
            if(designs==null || designs.Length!=60 || moduleLimit<2 || moduleLimit>32 || radiusLimit<2 || radiusLimit>12 || heightLimit<4 || heightLimit>12
                || !ColonyRules.IsFinite(administrationShare+leadershipShare+demandHeadroom+creditMultiplier+workMultiplier+upkeepPerResident+serviceRadius+basicServices+maxFoundationDepth+excavationClearance)
                || administrationShare<=0 || leadershipShare<=0 || administrationShare+leadershipShare>=1 || demandHeadroom<1 || demandHeadroom>2
                || creditMultiplier<=0 || workMultiplier<=0 || upkeepPerResident<0 || serviceRadius<=0 || basicServices<0 || basicServices>1 || maxFoundationDepth<=0 || excavationClearance<0
                || spacing<0 || spacing>2 || !ColonyRules.IsFinite(galleryCost+galleryWork+neighbourClearance)
                || galleryCost<0 || galleryWork<0 || neighbourClearance<0 || neighbourClearance>20)return false;
            var ids=new HashSet<string>();var classes=new int[3];
            foreach(var d in designs)
            {
                if(d==null || d.residentClass<0 || d.residentClass>2 || string.IsNullOrEmpty(d.id) || !ids.Add(d.id) || d.capacity<=0
                    || !ColonyRules.IsFinite(d.cost+d.work+d.enclosure+d.serviceNeed) || d.cost<=0 || d.work<=0 || d.enclosure<0 || d.enclosure>1
                    || d.serviceNeed<=0 || d.columns==null || d.columns.Length==0 || d.columns.Length>64)return false;
                if(d.roofStyle<0 || d.roofStyle>4 || d.facadeStyle<0 || d.facadeStyle>3
                    || !ValidColor(d.wallColor) || !ValidColor(d.roofColor) || !ValidColor(d.trimColor))return false;
                var prefix=d.residentClass==0?"worker-":d.residentClass==1?"admin-":"leader-";
                if(!d.id.StartsWith(prefix,StringComparison.Ordinal) || !int.TryParse(d.id.Substring(prefix.Length),out var number)
                    || number<1 || number>20 || d.id!=prefix+number.ToString("00",System.Globalization.CultureInfo.InvariantCulture))return false;
                classes[d.residentClass]++;var columns=new HashSet<long>();
                foreach(var c in d.columns)if(Math.Abs((long)c.x)>12 || Math.Abs((long)c.z)>12 || c.height<1 || c.height>12 || !columns.Add(((long)c.x<<32)|(uint)c.z))return false;
            }
            return classes[0]==20 && classes[1]==20 && classes[2]==20;
        }
        private static bool ValidColor(string value)=>value!=null && value.Length==7 && value[0]=='#'
            && uint.TryParse(value.Substring(1),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out _);
        [NonSerialized] private Dictionary<string,HabDesign> byId;
        [NonSerialized] private HabDesign[] indexedDesigns;
        public HabDesign Find(string id)
        {
            if (id == "res-core") return ResidentialRules.Core;
            if (id == "res-link") return ResidentialRules.Link;
            if(byId==null || indexedDesigns!=designs)
            {
                byId=new Dictionary<string,HabDesign>(StringComparer.Ordinal);indexedDesigns=designs;
                if(designs!=null)foreach(var d in designs)if(d!=null && d.id!=null)byId[d.id]=d;
            }
            return id!=null && byId.TryGetValue(id,out var found)?found:null;
        }
    }
    [Serializable] public sealed class HabPlacement
    {
        public string design, owner;
        public int serial, x, z, floor, rotation, parent;
        // Additive: zero keeps the original attached layout. A straight, covered passage spans spaced wings.
        public int linkX, linkZ, linkLength, linkDirection;
        public int residentClass;
        public float housing;
        public bool complete, legacyHousingInCore;
    }
    [Serializable] public sealed class HabProject
    {
        public HabPlacement placement;
        public float cost, paid, work, duration;
        public bool legacy;
        public int relocations;
    }
    [Serializable] public sealed class ResidentialState
    {
        public int version = 1, seed, sequence;
        public string blockId;
        public List<HabPlacement> modules = new List<HabPlacement>();
        public HabProject project;
        // An old paid upgrade can wait for a legal site without losing its work or investment.
        public float legacyCost, legacyPaid, legacyWork, legacyDuration;
        public int legacyPlan;
        public float workAccumulator;
    }
    public struct HabCell
    {
        public int x, z, bottom, top;
        public bool passage;
        public HabCell(int x, int z, int bottom, int top,bool passage=false) { this.x=x; this.z=z; this.bottom=bottom; this.top=top; this.passage=passage; }
    }
    public sealed class HabCandidate
    {
        public HabPlacement placement;
        public float score, cost;
        public bool affordable;
        public string explanation;
    }
    // A preview search yields after bounded work. It owns no reservation and is safe to discard on invalidation.
    public sealed class HabSearch
    {
        private readonly IEnumerator<int> work;
        public List<HabCandidate> Result {get;}=new List<HabCandidate>();
        internal HabSearch(ResidentialState state,ResidentialLibrary library,ResidentialInputs input,Func<HabCell,bool> site,int limit,string requiredDesign)
        {work=ResidentialRules.SearchCandidates(state,library,input,site,limit,requiredDesign,Result);}
        public bool Step()=>!work.MoveNext();
    }
    public struct ResidentialInputs
    {
        public float[] demand;
        public float credits, staffing, production, services, habitability, co2, water, temperature;
        public int preference; // 0 balanced, 1 upward, 2 outward
    }

    /// <summary>Integer occupied columns are the source of truth for architecture, support, selection and previews.
    /// No Unity dependencies, random mutation, mesh peaks, rings or inferred upgrade geometry.</summary>
    public static class ResidentialRules
    {
        public const float CellSize = .2f, FloorHeight = .12f, Deck = .055f;
        public static readonly HabDesign Core = MakeCore();
        public static readonly HabDesign Link = new HabDesign {id="res-link",name="Shared gallery and service riser",role="Infill circulation and services",
            roof="Accessible terrace",residentClass=-1,capacity=0,cost=35,work=30,enclosure=.9f,serviceNeed=.1f,upper=true,lift=true,
            columns=new[]{new HabColumn{x=0,z=0,height=2}}};
        public static int BlockSeed(int worldSeed,string identity)=>unchecked((int)Hash(worldSeed,0,identity));
        private static HabDesign MakeCore()
        {
            var cells = new List<HabColumn>();
            for (var x=-2;x<2;x++) for (var z=-2;z<2;z++) cells.Add(new HabColumn{x=x,z=z,height=3});
            return new HabDesign { id="res-core", name="Founding residence", role="Apartments, shared hall and lift", residentClass=-1,
                capacity=0, upper=true, lift=true, roof="Hipped founding pavilions",roofStyle=2,facadeStyle=3,
                wallColor="#B6A18B",roofColor="#70483D",trimColor="#C8AE75",columns=cells.ToArray() };
        }
        public static ResidentialState Create(int seed, string identity)
        {
            var s = new ResidentialState { seed=seed, blockId=identity };
            s.modules.Add(new HabPlacement { design=Core.id, serial=0, parent=-1, owner=identity, complete=true, residentClass=-1 });
            return s;
        }
        public static ResidentialState Migrate(ref BuildingOperationState operation, int seed, string identity)
        {
            operation.residential=NormalizeLoaded(operation.residential);
            if (operation.residential != null && operation.residential.version == 1
                && !string.IsNullOrEmpty(operation.residential.blockId)) return operation.residential;
            var s=Create(seed,identity);
            if (operation.improving)
            {
                s.legacyCost=AdaptiveBuildingRules.Cost(operation); s.legacyDuration=AdaptiveBuildingRules.Duration(operation);
                s.legacyWork=operation.improvementWork; s.legacyPaid=s.legacyCost*s.legacyWork/s.legacyDuration;
                s.legacyPlan=operation.growthPlan;
            }
            // Completed legacy tiers stay in the original economy. They never also become module housing.
            operation.improving=false; operation.improvementWork=0; operation.milestoneSeconds=0;
            operation.residential=s;
            return s;
        }
        public static float[] Split(float total, ResidentialLibrary lib)
            => new[] { total*(1-lib.administrationShare-lib.leadershipShare), total*lib.administrationShare, total*lib.leadershipShare };
        public static float Services(float nearbyUtilityJobs,float generatedPower,float powerDemand,ResidentialLibrary lib)
            => (nearbyUtilityJobs>0?1:lib.basicServices)*(powerDemand>0?Math.Min(1,Math.Max(0,generatedPower/powerDemand)):1);
        public static float Labour(ResidentialInputs input)=>Math.Max(0,Math.Min(1,Math.Min(input.staffing,Math.Min(input.services,input.production))));
        public static float[] Demand(float population, float jobs, float habitability, float[] housing, ResidentialLibrary lib)
        {
            // Classes partition the SAME population; no new workers/jobs are created by classification.
            var target=Math.Max(population*lib.demandHeadroom, Math.Min(population*1.5f, jobs/.62f*habitability));
            var targetClasses=Split(target,lib);
            for(var i=0;i<3;i++) targetClasses[i]=Math.Max(0,targetClasses[i]-housing[i]);
            return targetClasses;
        }
        public static float[] AddedHousing(ResidentialState s, ResidentialLibrary lib)
        {
            var result=new float[3];
            if(s!=null) foreach(var m in s.modules)
            {
                var d=lib.Find(m.design);
                if(m.complete && !m.legacyHousingInCore && d!=null && m.residentClass>=0) result[m.residentClass]+=m.housing;
            }
            return result;
        }
        public static List<HabCell> BodyCells(HabPlacement p, ResidentialLibrary lib)
        {
            var result=new List<HabCell>(); FillBody(p,lib,result);return result;
        }
        private static void FillBody(HabPlacement p,ResidentialLibrary lib,List<HabCell> result)
        {
            result.Clear();var d=lib.Find(p.design);if(d==null)return;
            foreach(var c in d.columns)
            {
                Rotate(c.x,c.z,p.rotation,out var x,out var z);
                result.Add(new HabCell(p.x+x,p.z+z,p.floor,p.floor+c.height));
            }
        }
        public static List<HabCell> Cells(HabPlacement p,ResidentialLibrary lib)
        {
            var cells=BodyCells(p,lib);
            Direction(p.linkDirection,out var dx,out var dz);
            for(var i=0;i<p.linkLength;i++)cells.Add(new HabCell(p.linkX+dx*i,p.linkZ+dz*i,p.floor,p.floor+1,true));
            return cells;
        }
        private static void Direction(int side,out int x,out int z)
        {x=side==0?1:side==2?-1:0;z=side==1?1:side==3?-1:0;}
        public static void Rotate(int x,int z,int rotation,out int rx,out int rz)
        { for(var i=0;i<rotation;i++){var t=x;x=-z;z=t;} rx=x;rz=z; }
        public static bool Overlap(HabCell a,HabCell b)
            => a.x==b.x && a.z==b.z && a.bottom<b.top && b.bottom<a.top;
        public static bool Validate(ResidentialState s, HabPlacement p, ResidentialLibrary lib,
            Func<HabCell,bool> site, out string reason)
            => Validate(s,p,lib,site,new ValidationCache(s,lib),out reason);
        private sealed class ValidationCache
        {
            public readonly Dictionary<long,List<HabCell>> occupied=new Dictionary<long,List<HabCell>>();
            public readonly Dictionary<long,HashSet<int>> support=new Dictionary<long,HashSet<int>>();
            public readonly Dictionary<int,List<HabCell>> modules=new Dictionary<int,List<HabCell>>();
            public readonly Dictionary<int,HabPlacement> placements=new Dictionary<int,HabPlacement>();
            public readonly Dictionary<int,List<HabCell>> bodies=new Dictionary<int,List<HabCell>>();
            public readonly List<HabCell> scratch=new List<HabCell>(66);
            public ValidationCache(ResidentialState s,ResidentialLibrary lib)
            {
                foreach(var m in s.modules) if(m.complete)
                {
                    var d=lib.Find(m.design);var cells=Cells(m,lib);modules[m.serial]=cells;
                    placements[m.serial]=m;bodies[m.serial]=BodyCells(m,lib);
                    foreach(var c in cells)
                    {
                        var key=CellKey(c.x,c.z);
                        if(!occupied.TryGetValue(key,out var list))occupied[key]=list=new List<HabCell>();list.Add(c);
                        if(!c.passage && d!=null && d.upper && d.lift)
                        {if(!support.TryGetValue(key,out var tops))support[key]=tops=new HashSet<int>();tops.Add(c.top);}
                    }
                }
            }
        }
        private static long CellKey(int x,int z)=>((long)x<<32) | (uint)z;
        private static bool Validate(ResidentialState s, HabPlacement p, ResidentialLibrary lib,
            Func<HabCell,bool> site, ValidationCache cache, out string reason)
        {
            reason="Invalid module";
            var d=lib.Find(p.design);
            if(d==null || p.owner!=s.blockId || p.rotation<0 || p.rotation>3 || p.floor<0 || s.modules.Count>=lib.moduleLimit) return false;
            if(p.linkLength<0 || p.linkLength>2 || p.linkLength>0 && (p.floor!=0 || p.linkDirection<0 || p.linkDirection>3))return false;
            cache.placements.TryGetValue(p.parent,out var parent);
            if(parent==null) {reason="Missing completed connection";return false;}
            var parentDesign=lib.Find(parent.design);
            var parentCells=cache.bodies[parent.serial];var body=cache.scratch;FillBody(p,lib,body);var connected=false;
            if(p.linkLength>0)
            {
                Direction(p.linkDirection,out var dx,out var dz);
                var lastX=p.linkX+dx*(p.linkLength-1);var lastZ=p.linkZ+dz*(p.linkLength-1);
                var starts=false;var ends=false;
                foreach(var c in parentCells)if(c.bottom==p.floor && c.x==p.linkX-dx && c.z==p.linkZ-dz)starts=true;
                foreach(var c in body)if(c.x==lastX+dx && c.z==lastZ+dz)ends=true;
                connected=starts && ends;
                if(!connected){reason="Gallery endpoints do not meet floor entrances";return false;}
            }
            Direction(p.linkDirection,out var linkDx,out var linkDz);
            var bodyCount=body.Count;
            for(var i=0;i<p.linkLength;i++)body.Add(new HabCell(p.linkX+linkDx*i,p.linkZ+linkDz*i,p.floor,p.floor+1,true));
            foreach(var c in body)
            {
                if(Math.Abs(c.x)>lib.radiusLimit || Math.Abs(c.z)>lib.radiusLimit || c.top>lib.heightLimit)
                {reason="Development limit";return false;}
                var key=CellKey(c.x,c.z);
                if(cache.occupied.TryGetValue(key,out var volumes))
                    foreach(var occupied in volumes) if(Overlap(c,occupied)){reason="Occupied volume";return false;}
                if(c.passage)for(var i=0;i<bodyCount;i++)if(body[i].x==c.x && body[i].z==c.z){reason="Gallery intersects its residence";return false;}
                if(p.linkLength>0 && !c.passage)
                    for(var side=0;side<4;side++)
                    {
                        Direction(side,out var dx,out var dz);
                        if(cache.occupied.TryGetValue(CellKey(c.x+dx,c.z+dz),out var near))
                            foreach(var a in near)if(!a.passage && a.bottom<c.top && c.bottom<a.top)
                            {reason="Residential spacing clearance";return false;}
                    }
                if(c.bottom>0)
                {
                    var supported=cache.support.TryGetValue(key,out var tops) && tops.Contains(c.bottom);
                    if(!supported || !d.lift){reason="Unsupported floor or missing lift";return false;}
                }
                foreach(var a in parentCells)if(p.linkLength==0)
                {
                    if(c.bottom==a.bottom && Math.Abs(c.x-a.x)+Math.Abs(c.z-a.z)==1) connected=true;
                    if(c.x==a.x && c.z==a.z && a.top==c.bottom && parentDesign.upper && parentDesign.lift && d.lift) connected=true;
                }
                if(site!=null && !site(c)){reason="Terrain, infrastructure or excavation clearance";return false;}
            }
            if(!connected){reason="No matching floor entrance";return false;}
            reason=null;return true;
        }
        public static List<HabCandidate> Candidates(ResidentialState s, ResidentialLibrary lib, ResidentialInputs input,
            Func<HabCell,bool> site, int limit=5,string requiredDesign=null)
        {
            var search=BeginCandidates(s,lib,input,site,limit,requiredDesign);
            while(!search.Step()){}return search.Result;
        }
        public static HabSearch BeginCandidates(ResidentialState s,ResidentialLibrary lib,ResidentialInputs input,
            Func<HabCell,bool> site,int limit=5,string requiredDesign=null)=>new HabSearch(s,lib,input,site,limit,requiredDesign);
        internal static IEnumerator<int> SearchCandidates(ResidentialState s,ResidentialLibrary lib,ResidentialInputs input,
            Func<HabCell,bool> site,int limit,string requiredDesign,List<HabCandidate> output)
        {
            var result=new List<HabCandidate>();
            if(s==null || limit<=0 || s.modules.Count>=lib.moduleLimit)yield break;
            var attempts=0;var cache=new ValidationCache(s,lib);
            var counts=new Dictionary<string,int>();var upperCount=0;var housingCount=0;
            foreach(var m in s.modules)if(lib.Find(m.design).residentClass>=0)
            {counts.TryGetValue(m.design,out var count);counts[m.design]=count+1;housingCount++;if(m.floor>0)upperCount++;}
            // Preferences change the mix, never prohibit an available direction. Missing geometry still wins safely.
            var targetUpper=input.preference==1?2f/3:input.preference==2?1f/3:.5f;
            var wantUpper=upperCount<(int)Math.Floor((housingCount+1)*targetUpper+.001f) || housingCount==0 && input.preference==1;
            var harsh=Math.Min(1,Math.Max(1-input.habitability,Math.Max(Math.Max(0,input.co2-600)/800,Math.Max(Math.Max(0,input.water-10)/40,Math.Abs(input.temperature)/8))));
            // Four cardinal entrance anchors plus supported roof surfaces, bounded by the module cap.
            for(var parentIndex=s.modules.Count-1;parentIndex>=0;parentIndex--)
            {
                var parent=s.modules[parentIndex];
                var pc=BodyCells(parent,lib);
                foreach(var d in lib.designs)
                {
                    if(requiredDesign!=null ? d.id!=requiredDesign : input.demand==null || input.demand[d.residentClass]<=.01f) continue;
                    var need=input.demand!=null?input.demand[d.residentClass]:0;
                    counts.TryGetValue(d.id,out var repeats);
                    for(var rotation=0;rotation<4;rotation++)
                    {
                        var p=new HabPlacement {design=d.id,owner=s.blockId,parent=parent.serial,serial=s.sequence+1,rotation=rotation};
                        var dc=BodyCells(p,lib);
                        for(var option=0;option<32;option++)
                        {
                            if(++attempts>24000) goto Ranked;
                            if(attempts%128==0)yield return attempts;
                            var side=option>=24?4:option/6;var gap=side==4?0:2-option%3;
                            var farPort=side==4?(option-24)%2==1:option%6>=3;
                            var anchorSide=side==4?(option-24)/2:side;
                            if(gap>lib.spacing || gap>0 && parent.floor>0)continue;
                            var a=Anchor(pc,anchorSide,farPort);var b=Anchor(dc,side<4?(side+2)%4:anchorSide,farPort);
                            Direction(side,out var dx,out var dz);
                            p.x=a.x-b.x+dx*(gap+1);p.z=a.z-b.z+dz*(gap+1);
                            p.floor=side==4?a.top:parent.floor;
                            p.linkLength=gap;p.linkX=gap>0?a.x+dx:0;p.linkZ=gap>0?a.z+dz:0;p.linkDirection=gap>0?side:0;
                            if(!Validate(s,p,lib,site,cache,out _))continue;
                            var key=Key(p);
                            var cost=(d.cost+gap*lib.galleryCost)*lib.creditMultiplier;
                            var same=lib.Find(parent.design).residentClass==d.residentClass?3:0;
                            var upward=p.floor>parent.floor;
                            var score=30*Math.Min(need,d.capacity)/Math.Max(1,d.capacity)
                                +20*(harsh*d.enclosure+(1-harsh)*(1-d.enclosure)) +same
                                +5*Math.Min(1,input.services/Math.Max(.1f,d.serviceNeed)) -cost*.025f
                                +((p.floor>0)==wantUpper?120:0)
                                +gap*12+(repeats==0?15:-repeats*9)-(s.modules[s.modules.Count-1].design==d.id?12:0)
                                -p.floor*.6f+(Hash(s.seed,s.sequence,key)%1000)*.0001f;
                            var copy=new HabPlacement {design=p.design,owner=p.owner,parent=p.parent,serial=p.serial,rotation=p.rotation,x=p.x,z=p.z,floor=p.floor,
                                linkX=p.linkX,linkZ=p.linkZ,linkLength=p.linkLength,linkDirection=p.linkDirection};
                            RetainCandidate(result,new HabCandidate{placement=copy,cost=cost,score=score,affordable=input.credits>=cost},limit);
                        }
                    }
                }
            }
            Ranked:
            // Small connecting/shared spaces can fill real gaps between occupied wings. They are not habitation designs.
            if(requiredDesign==Link.id || requiredDesign==null && input.demand!=null && input.demand[0]+input.demand[1]+input.demand[2]>0)
            {
                var possible=new HashSet<string>();
                foreach(var parent in s.modules)foreach(var cell in cache.modules[parent.serial])
                    for(var side=0;side<4;side++)
                    {
                        if(++attempts%128==0)yield return attempts;
                        var p=new HabPlacement {design=Link.id,owner=s.blockId,parent=parent.serial,serial=s.sequence+1,
                            x=cell.x+(side==0?1:side==2?-1:0),z=cell.z+(side==1?1:side==3?-1:0),floor=parent.floor};
                        if(!possible.Add(p.x+":"+p.z+":"+p.floor))continue;
                        var neighbours=0;
                        for(var edge=0;edge<4;edge++)
                        {
                            var x=p.x+(edge==0?1:edge==2?-1:0);var z=p.z+(edge==1?1:edge==3?-1:0);
                            if(cache.occupied.TryGetValue(CellKey(x,z),out var near) && near.Exists(c=>c.bottom==p.floor))neighbours++;
                        }
                        if(neighbours<2 || !Validate(s,p,lib,site,cache,out _))continue;
                        var cost=Link.cost*lib.creditMultiplier;
                        RetainCandidate(result,new HabCandidate {placement=p,cost=cost,score=8+neighbours,affordable=input.credits>=cost,
                            explanation="Shared infill gallery joins existing floor entrances and provides a supported service terrace"},limit);
                    }
            }
            result.Sort((a,b)=> {var rank=b.score.CompareTo(a.score);return rank!=0?rank:string.CompareOrdinal(Key(a.placement),Key(b.placement));});
            // Keep the highest scoring representative per location so the display remains useful.
            var locations=new HashSet<string>();var top=new List<HabCandidate>();
            foreach(var c in result) if(locations.Add(c.placement.x+":"+c.placement.z+":"+c.placement.floor))
            {top.Add(c);if(top.Count>=limit)break;}
            if(limit>1 && !top.Exists(c=>c.placement.floor>0))
            {
                var up=result.Find(c=>c.placement.floor>0);
                if(up!=null){if(top.Count>=limit)top[top.Count-1]=up;else top.Add(up);}
            }
            if(limit>1 && !top.Exists(c=>c.placement.floor==0))
            {
                var ground=result.Find(c=>c.placement.floor==0);
                if(ground!=null){if(top.Count>=limit)top[top.Count-1]=ground;else top.Add(ground);}
            }
            foreach(var c in top)if(c.explanation==null)
            {
                var p=c.placement;var d=lib.Find(p.design);
                c.explanation=$"{d.name}: {ClassName(d.residentClass)} need {input.demand?[d.residentClass]??0:0}; "+
                    (p.floor>0?"supported roof + lift":p.linkLength>0?$"spaced wing + {p.linkLength}-tile gallery":"compact infill entrance")+
                    $"; global shelter {harsh:P0}; services {input.services:P0}; mixed upward/outward development";
            }
            output.AddRange(top);
        }
        private static void RetainCandidate(List<HabCandidate> result,HabCandidate candidate,int limit)
        {
            var p=candidate.placement;var count=0;HabCandidate worst=null;
            foreach(var c in result)
            {
                var other=c.placement;if((p.floor>0)!=(other.floor>0))continue;
                if(p.x==other.x && p.z==other.z && p.floor==other.floor)
                {if(Better(candidate,c)){result.Remove(c);result.Add(candidate);}return;}
                count++;if(worst==null || Better(worst,c))worst=c;
            }
            if(count<Math.Max(1,limit))result.Add(candidate);
            else if(Better(candidate,worst)){result.Remove(worst);result.Add(candidate);}
        }
        private static bool Better(HabCandidate a,HabCandidate b)=>a.score>b.score
            || a.score==b.score && string.CompareOrdinal(Key(a.placement),Key(b.placement))<0;
        private static HabCell Anchor(List<HabCell> cells,int side,bool farPort=false)
        {
            var best=cells[0];
            foreach(var c in cells)
            {
                var v=side==0?c.x:side==1?c.z:side==2?-c.x:-c.z;
                var bv=side==0?best.x:side==1?best.z:side==2?-best.x:-best.z;
                if(v>bv || v==bv && (farPort ? c.x>best.x || c.x==best.x && c.z>best.z : c.x<best.x || c.x==best.x && c.z<best.z))best=c;
            }
            return best;
        }
        public static string Key(HabPlacement p)=>p.design+":"+p.x+":"+p.z+":"+p.floor+":"+p.rotation+":"+p.parent
            +(p.linkLength>0?$":gallery:{p.linkX}:{p.linkZ}:{p.linkLength}:{p.linkDirection}":"");
        private static uint Hash(int seed,int sequence,string value)
        {unchecked{var h=(uint)(seed^sequence*16777619);foreach(var c in value)h=(h^c)*16777619;return h;}}
        public static string ClassName(int c)=>c==0?"Workers":c==1?"Administration":c==2?"Leadership":"Mixed founding housing";
        public static bool Request(ResidentialState s,HabCandidate c,ResidentialLibrary lib,ResidentialInputs input,Func<HabCell,bool> site)
        {
            if(s.project!=null || c==null || !Validate(s,c.placement,lib,site,out _))return false;
            var legacy=s.legacyDuration>0;
            var d=lib.Find(c.placement.design);
            var need=input.demand==null?0:d.residentClass<0?input.demand[0]+input.demand[1]+input.demand[2]:input.demand[d.residentClass];
            if(!legacy && (need<=0 || input.credits<c.cost)) return false;
            c.placement.legacyHousingInCore=legacy;
            c.placement.residentClass=d.residentClass;c.placement.housing=d.capacity;
            s.project=new HabProject {placement=c.placement, cost=legacy?s.legacyCost:c.cost,paid=legacy?s.legacyPaid:0,
                work=legacy?s.legacyWork:0,duration=legacy?s.legacyDuration:(d.work+c.placement.linkLength*lib.galleryWork)*lib.workMultiplier,legacy=legacy};
            s.legacyCost=s.legacyPaid=s.legacyWork=s.legacyDuration=0;
            return true;
        }
        public static bool Replan(ResidentialState s,ResidentialLibrary lib,ResidentialInputs input,Func<HabCell,bool> site)
        {
            var project=s.project;
            if(project==null || Validate(s,project.placement,lib,site,out _))return false;
            // Preserve the approved design and its financial contract, including when demand is now zero.
            var alternatives=Candidates(s,lib,input,site,5,project.placement.design);
            if(alternatives.Count==0)return false;
            var previous=project.placement;var next=alternatives[0].placement;
            next.residentClass=previous.residentClass;next.housing=previous.housing;next.legacyHousingInCore=previous.legacyHousingInCore;
            project.placement=next;project.relocations++;return true;
        }
        // Caller debits this returned amount atomically on the same main-thread step.
        public static float Advance(ResidentialState s,ResidentialLibrary lib,float seconds,float credits,float labour,
            bool offline,Func<HabCell,bool> site,out bool completed)
        {
            return Advance(s,lib,seconds,credits,labour,offline,site,out completed,out _);
        }
        public static float Advance(ResidentialState s,ResidentialLibrary lib,float seconds,float credits,float labour,
            bool offline,Func<HabCell,bool> site,out bool completed,out string reason)
        {
            completed=false;reason=null;var p=s.project;
            if(p==null || offline || !ColonyRules.IsFinite(seconds) || !ColonyRules.IsFinite(credits) || !ColonyRules.IsFinite(labour)
                || seconds<=0)return 0;
            if(!Validate(s,p.placement,lib,site,out reason) || labour<=0)return 0;
            var rate=p.cost/p.duration;
            var work=Math.Min(Math.Min(seconds*Math.Min(1,labour),p.duration-p.work),Math.Max(0,credits)/Math.Max(.0001f,rate));
            var targetWork=Math.Min(p.duration,p.work+work);
            var targetPaid=targetWork>=p.duration-.0001f?p.cost:p.cost*targetWork/p.duration;
            var debit=Math.Min(Math.Max(0,credits),Math.Max(0,targetPaid-p.paid));
            p.paid=Math.Min(p.cost,p.paid+debit);p.work=Math.Min(p.duration,p.paid/p.cost*p.duration);
            if(p.paid>=p.cost)
            {
                p.placement.complete=true;s.modules.Add(p.placement);s.sequence=p.placement.serial;s.project=null;completed=true;
            }
            return debit;
        }
        public static bool ValidSave(ResidentialState s)
        {
            s=NormalizeLoaded(s);
            if(s==null || s.version==0)return true;
            if(s.version!=1 || string.IsNullOrEmpty(s.blockId) || s.modules==null || s.modules.Count<1 || s.modules.Count>32
                || s.sequence<0 || s.sequence>32 || !Finite(s.legacyCost,s.legacyPaid,s.legacyWork,s.legacyDuration,s.workAccumulator)
                || s.workAccumulator<0 || s.workAccumulator>=.25f
                || s.legacyCost<0 || s.legacyPaid<0 || s.legacyPaid>s.legacyCost || s.legacyWork<0 || s.legacyWork>s.legacyDuration)return false;
            var ids=new HashSet<int>();
            foreach(var m in s.modules)
            {
                if(!ValidPlacement(m,s.blockId) || m.serial!=ids.Count || !m.complete || !ids.Add(m.serial) || m.serial>s.sequence
                    || m.serial==0 && (m.design!="res-core" || m.x!=0 || m.z!=0 || m.floor!=0 || m.parent!=-1)
                    || m.serial>0 && (!ids.Contains(m.parent) || m.parent>=m.serial))return false;
            }
            var p=s.project;
            return s.sequence==s.modules.Count-1 && ids.Contains(0) && (p==null || ValidPlacement(p.placement,s.blockId) && !p.placement.complete
                && p.placement.serial==s.sequence+1 && ids.Contains(p.placement.parent)
                && Finite(p.cost,p.paid,p.work,p.duration) && p.cost>0 && p.paid>=0 && p.paid<=p.cost+.001f
                && p.relocations>=0
                && p.work>=0 && p.duration>0 && p.work<=p.duration && Math.Abs(p.paid/p.cost-p.work/p.duration)<.001f);
        }
        public static bool ValidLayout(ResidentialState data,ResidentialLibrary lib)
        {
            data=NormalizeLoaded(data);
            if(data==null || data.version==0)return true;
            if(!ValidSave(data))return false;
            var replay=Create(data.seed,data.blockId);
            foreach(var p in data.modules)
            {
                var d=lib.Find(p.design);
                if(d==null || p.residentClass!=d.residentClass || (d.residentClass<0?p.housing!=0:p.housing<=0))return false;
                if(p.serial==0)continue;
                if(!Validate(replay,p,lib,null,out _))return false;
                replay.modules.Add(p);replay.sequence=p.serial;
            }
            if(data.project==null)return true;
            var current=data.project.placement;var design=lib.Find(current.design);
            return design!=null && current.residentClass==design.residentClass && (design.residentClass<0?current.housing==0:current.housing>0)
                && Validate(replay,current,lib,null,out _);
        }
        // Unity inline serialization can restore a null class as an empty instance. Only all-empty
        // sentinels are normalized; a real paid project or nonempty invalid layout must be validated.
        public static ResidentialState NormalizeLoaded(ResidentialState data)
        {
            if(data==null)return null;
            if(EmptyProject(data.project))data.project=null;
            if(string.IsNullOrEmpty(data.blockId) && (data.modules==null || data.modules.Count==0)
                && data.project==null && data.seed==0 && data.sequence==0 && data.legacyCost==0 && data.legacyPaid==0
                && data.legacyWork==0 && data.legacyDuration==0 && data.workAccumulator==0 && (data.version==0 || data.version==1))return null;
            return data;
        }
        private static bool EmptyProject(HabProject project)
            => project!=null && project.cost==0 && project.paid==0 && project.duration==0 && project.work==0 && !project.legacy && project.relocations==0
                && (project.placement==null || string.IsNullOrEmpty(project.placement.design) && string.IsNullOrEmpty(project.placement.owner)
                    && project.placement.serial==0 && project.placement.x==0 && project.placement.z==0 && project.placement.floor==0
                    && project.placement.rotation==0 && project.placement.parent==0 && project.placement.residentClass==0 && project.placement.housing==0
                    && !project.placement.complete && !project.placement.legacyHousingInCore && project.placement.linkLength==0
                    && project.placement.linkX==0 && project.placement.linkZ==0 && project.placement.linkDirection==0);
        private static bool ValidPlacement(HabPlacement p,string owner)
            => p!=null && p.owner==owner && p.serial>=0 && p.serial<=32 && p.rotation>=0 && p.rotation<4
                && p.residentClass>=-1 && p.residentClass<=2 && ColonyRules.IsFinite(p.housing) && p.housing>=0 && p.housing<=10000
                && Math.Abs((long)p.x)<=12 && Math.Abs((long)p.z)<=12 && p.floor>=0 && p.floor<=12
                && p.linkLength>=0 && p.linkLength<=2 && (p.linkLength==0 || p.linkDirection>=0 && p.linkDirection<4
                    && Math.Abs((long)p.linkX)<=12 && Math.Abs((long)p.linkZ)<=12)
                && (p.design=="res-core" || p.design=="res-link" || p.design!=null && (p.design.Length==9 && (p.design.StartsWith("worker-") || p.design.StartsWith("leader-")) || p.design.Length==8 && p.design.StartsWith("admin-"))
                    && int.TryParse(p.design.Substring(p.design.Length-2),out var n) && n>=1 && n<=20);
        private static bool Finite(params float[] values){foreach(var v in values)if(!ColonyRules.IsFinite(v))return false;return true;}
    }
}
