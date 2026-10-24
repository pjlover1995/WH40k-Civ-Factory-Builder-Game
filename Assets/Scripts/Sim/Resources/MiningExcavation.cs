using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using WH30K.Gameplay.Construction;
using WH30K.UI;

namespace WH30K.Sim.Resources
{
    public sealed partial class IndustrySystem
    {
        private MiningConflict conflict = new MiningConflict();
        private readonly List<SurfaceDeposit> workedDeposits = new List<SurfaceDeposit>();
        private readonly HashSet<SurfaceDeposit> dirtyDeposits = new HashSet<SurfaceDeposit>();
        private readonly List<ManagedBuilding> protectedBuildings = new List<ManagedBuilding>();
        private readonly Dictionary<Vector3Int,List<SurfaceDeposit>> excavationTiles=new Dictionary<Vector3Int,List<SurfaceDeposit>>();
        private Transform outlineRoot;
        private readonly List<Transform> outlineLabels=new List<Transform>();
        public bool HasMiningDecision => conflict.active;
        public MiningConflict PendingConflict => new MiningConflict { active=conflict.active,core=conflict.core,
            deposit=conflict.deposit,sector=conflict.sector,mine=conflict.mine,building=conflict.building };

        private static ExcavationState CloneExcavation(ExcavationState state) => state == null ? null : new ExcavationState {
            capacity=(float[])state.capacity.Clone(),removed=(float[])state.removed.Clone(),blocked=state.blocked,
            majorRadius=state.majorRadius,minorRadius=state.minorRadius,depth=state.depth,grade=state.grade,bearing=state.bearing };

        private void ConfigureExcavation(IndustrySnapshot saved)
        {
            conflict = saved?.conflict != null ? new MiningConflict {active=saved.conflict.active,core=saved.conflict.core,
                deposit=saved.conflict.deposit,sector=saved.conflict.sector,mine=saved.conflict.mine,building=saved.conflict.building} : new MiningConflict();
            RefreshProtectedBuildings();
            foreach (var deposit in deposits)
                if (deposit.Excavation != null && Array.Exists(deposit.Excavation.removed, v=>v>0))
                { workedDeposits.Add(deposit); IndexExcavation(deposit); dirtyDeposits.Add(deposit); }
            planet.ExcavationHeight = ExcavatedHeight;
            GetComponent<ColonyClock>().MiningDecisionPending = conflict.active;
            GetComponent<NewGameMenu>()?.CloseMiningPanel();
            FlushExcavations();
        }
        private void RefreshProtectedBuildings()
        {
            protectedBuildings.Clear();
            protectedBuildings.AddRange(settlement.CoreBuildings); protectedBuildings.AddRange(buildings);
        }
        public SurfaceDeposit DepositFor(ManagedBuilding building)
        {
            if (building == null || (building.Id != "Mine" && building.Id != "Quarry")) return null;
            SurfaceDeposit deposit=null;
            // Keep exhausted deposits inspectable instead of silently moving their plans to another body.
            foreach(var candidate in deposits)
                if(candidate.Kind==ExtractionKind(building) && (deposit==null || Vector3.Distance(candidate.Position,building.transform.position)<Vector3.Distance(deposit.Position,building.transform.position))) deposit=candidate;
            return deposit!=null && Vector3.Distance(deposit.Position,building.transform.position)<=250 ? deposit : null;
        }
        public string ExcavationReport(ManagedBuilding building)
        {
            var deposit=DepositFor(building);if(deposit==null)return "No matching ore body within 250 m. Change mineral focus or establish a new mine.";
            var state=deposit.Excavation;var depth=0f;for(var s=0;s<8;s++)depth=Mathf.Max(depth,MiningRules.Depth(state,s));
            return $"{MiningRules.Geology(deposit.Kind)}  /  Survey #{deposit.Id}\n"+
                $"Estimated grade {state.grade*100:0.0}%  |  Extent {state.majorRadius*2:0} x {state.minorRadius*2:0} m\n"+
                $"Remaining {deposit.Remaining:0.0} lots  |  Accessible {MiningRules.Remaining(state,true):0.0}\n"+
                $"Ore held behind limits {deposit.Remaining-MiningRules.Remaining(state,true):0.0} lots\n"+
                $"Deepest working bench {depth:0} / {state.depth:0} m. Four-metre benches.\n\n"+
                "Limit a sector to protect its neighbours. Reopen it to reconsider expansion.\n"+
                "Expansion pauses before entering a facility safety margin. Core facilities and the mine's own foundation are retained.\n\n"+
                "Lots represent recoverable ore parcels; grade and geometry are survey estimates. Copper output is concentrated ore. Waste rock is not a saleable product.";
        }
        public void ToggleSector(ManagedBuilding building,int sector)
        {
            var deposit=DepositFor(building);if(deposit==null || sector<0 || sector>=8 || conflict.active)return;
            deposit.Excavation.blocked ^= 1<<sector;
            ShowExcavationOutline(deposit);
            GetComponent<NewGameMenu>()?.AppendEventLog($"Survey #{deposit.Id}: sector {(char)('A'+sector)} "+((deposit.Excavation.blocked & 1<<sector)!=0?"limited. Ore retained in ground.":"reopened for review."));
        }
        public void ShowExcavationOutline(SurfaceDeposit deposit)
        {
            if(outlineRoot!=null)Destroy(outlineRoot.gameObject);outlineLabels.Clear();
            outlineRoot=new GameObject("Planned excavation sectors").transform;outlineRoot.SetParent(visualRoot,false);
            Basis(deposit.Direction,out var right,out var forward);var state=deposit.Excavation;
            var font=UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for(var s=0;s<8;s++)
            {
                var radius=MiningRules.Radius(state,s,state.capacity[s]-state.removed[s]);
                var go=new GameObject("Sector "+(char)('A'+s),typeof(LineRenderer));go.transform.SetParent(outlineRoot,false);
                var line=go.GetComponent<LineRenderer>();line.sharedMaterial=routeMaterial;line.widthMultiplier=1;
                line.startColor=line.endColor=(state.blocked&1<<s)!=0?new Color(.95f,.25f,.18f):new Color(.95f,.73f,.3f);
                line.positionCount=12;line.loop=true;
                line.SetPosition(0,planet.transform.TransformPoint(deposit.Direction*(planet.EvaluateBaseHeight(deposit.Direction)+2)));
                for(var p=1;p<12;p++)
                {
                    var bearing=state.bearing+s*45-22.5f+(p-1)*4.5f;
                    radius=MiningRules.RadiusAt(state,s,bearing,state.capacity[s]-state.removed[s]);
                    var angle=bearing*Mathf.Deg2Rad;
                    var n=(deposit.Direction+(right*Mathf.Cos(angle)+forward*Mathf.Sin(angle))*radius/planet.Radius).normalized;
                    line.SetPosition(p,planet.transform.TransformPoint(n*(planet.EvaluateBaseHeight(n)+2)));
                }
                var label=new GameObject("Sector label "+(char)('A'+s),typeof(TextMesh));label.transform.SetParent(outlineRoot,false);
                var a=(state.bearing+s*45)*Mathf.Deg2Rad;
                var direction=(deposit.Direction+(right*Mathf.Cos(a)+forward*Mathf.Sin(a))*(radius+12)/planet.Radius).normalized;
                label.transform.position=planet.transform.TransformPoint(direction*(planet.EvaluateBaseHeight(direction)+8));
                var text=label.GetComponent<TextMesh>();text.font=font;text.fontSize=48;text.characterSize=2.8f;text.anchor=TextAnchor.MiddleCenter;
                text.text=((char)('A'+s)).ToString();text.color=line.startColor;label.GetComponent<Renderer>().sharedMaterial=font.material;outlineLabels.Add(label.transform);
            }
        }
        private void UpdateExcavationOutline()
        {
            if(outlineRoot==null)return;outlineRoot.gameObject.SetActive(Overlay!=0);
            if(Camera.main!=null)foreach(var label in outlineLabels)label.rotation=Camera.main.transform.rotation;
        }
        private float Excavate(SurfaceDeposit deposit,int mine,float requested)
        {
            if(conflict.active)return 0;
            var state=deposit.Excavation;var sector=MiningRules.NextSector(state);if(sector<0)return 0;
            for(var i=0;i<protectedBuildings.Count;i++)
            {
                var building=protectedBuildings[i];if(building==null || building==buildings[mine])continue;
                Coordinates(deposit,planet.transform.InverseTransformPoint(building.transform.position).normalized,out var distance,out var bearing);
                if(!MiningRules.Threatens(state,sector,distance,bearing,Footprint(building)))continue;
                var index=Array.IndexOf(buildings,building);var core=index<0;
                if(core) for(var c=0;c<settlement.CoreBuildings.Count;c++)if(settlement.CoreBuildings[c]==building){index=c;break;}
                conflict=new MiningConflict{active=true,deposit=deposit.Id,sector=sector,mine=mine,building=index,core=core};
                GetComponent<ColonyClock>().MiningDecisionPending=true;
                GetComponent<NewGameMenu>()?.AppendEventLog($"Mine expansion held: sector {(char)('A'+sector)} approaches {building.DisplayName}.");
                return 0;
            }
            var amount=MiningRules.Extract(state,sector,requested);
            deposit.Remaining=MiningRules.Remaining(state);
            if(!workedDeposits.Contains(deposit)){workedDeposits.Add(deposit);IndexExcavation(deposit);}
            dirtyDeposits.Add(deposit);return amount;
        }
        private ManagedBuilding ThreatenedBuilding() => !conflict.active ? null : conflict.core
            ? (conflict.building<settlement.CoreBuildings.Count?settlement.CoreBuildings[conflict.building]:null)
            : (conflict.building<buildings.Length?buildings[conflict.building]:null);

        public string ConflictReport()
        {
            var building=ThreatenedBuilding();if(building==null)return "Expansion target is no longer present.";
            var deposit=deposits[conflict.deposit];var state=deposit.Excavation;
            return $"Survey #{deposit.Id} / {MiningRules.Geology(deposit.Kind)}\nSector {(char)('A'+conflict.sector)} approaches {building.DisplayName}.\n\n"+
                $"Limiting this direction keeps {state.capacity[conflict.sector]-state.removed[conflict.sector]:0.0} lots in the ground. Other open sectors can continue.\n\n"+
                (conflict.core?"This is a core facility. Its foundation must be protected; limit this sector.":
                "Demolition removes this facility, its jobs, housing and stored materials. Cargo involving it is lost. There is no refund. The next cut is checked again for other neighbours.");
        }
        public bool ResolveMiningConflict(bool demolish)
        {
            if(!conflict.active || demolish && conflict.core)return false;
            var building=ThreatenedBuilding();var deposit=deposits[conflict.deposit];var sector=conflict.sector;
            if(demolish && (building==null || !placement.Demolish(building)))return false;
            if(!demolish)MiningRules.Limit(deposit.Excavation,sector);
            conflict=new MiningConflict();GetComponent<ColonyClock>().MiningDecisionPending=false;
            GetComponent<NewGameMenu>()?.CloseMiningPanel();Synchronize();
            RefreshProtectedBuildings();
            foreach(var worked in workedDeposits){worked.renderedRadii=null;dirtyDeposits.Add(worked);}
            if(outlineRoot!=null)ShowExcavationOutline(deposit);
            GetComponent<NewGameMenu>()?.AppendEventLog(demolish?"Facility demolished; mine expansion will be checked again.":$"Sector {(char)('A'+sector)} limited; remaining ore is protected.");
            return true;
        }
        private void PresentMiningDecision()
        {
            var menu=GetComponent<NewGameMenu>();
            if(conflict.active && menu!=null && menu.CanPresentMiningDecision)menu.ShowMiningDecision(ConflictReport(),!conflict.core);
        }
        private void Coordinates(SurfaceDeposit deposit,Vector3 direction,out float distance,out float bearing)
        {
            Basis(deposit.Direction,out var right,out var forward);
            var offset=(direction-deposit.Direction)*planet.Radius;
            var x=Vector3.Dot(offset,right);var y=Vector3.Dot(offset,forward);
            distance=Mathf.Sqrt(x*x+y*y);bearing=Mathf.Repeat(Mathf.Atan2(y,x)*Mathf.Rad2Deg,360);
        }
        private static void Basis(Vector3 n,out Vector3 right,out Vector3 forward)
        { right=Vector3.Cross(n,Mathf.Abs(n.y)>.95f?Vector3.right:Vector3.up).normalized;forward=Vector3.Cross(n,right); }
        private static float Footprint(ManagedBuilding building) => new Vector2(building.transform.lossyScale.x,building.transform.lossyScale.z).magnitude*.5f;

        private Vector3Int ExcavationTile(Vector3 direction) => new Vector3Int(Mathf.FloorToInt(direction.x*planet.Radius/256),
            Mathf.FloorToInt(direction.y*planet.Radius/256),Mathf.FloorToInt(direction.z*planet.Radius/256));
        private void IndexExcavation(SurfaceDeposit deposit)
        {
            var extent=Vector3.one*(deposit.Excavation.majorRadius+25)/planet.Radius;
            var min=ExcavationTile(deposit.Direction-extent);var max=ExcavationTile(deposit.Direction+extent);
            for(var x=min.x;x<=max.x;x++)for(var y=min.y;y<=max.y;y++)for(var z=min.z;z<=max.z;z++)
            {
                var key=new Vector3Int(x,y,z);
                if(!excavationTiles.TryGetValue(key,out var list)){list=new List<SurfaceDeposit>();excavationTiles.Add(key,list);}
                list.Add(deposit);
            }
        }

        private float ExcavatedHeight(Vector3 direction,float height)
        {
            var cut=0f;
            if(!excavationTiles.TryGetValue(ExcavationTile(direction),out var nearby))return height;
            foreach(var deposit in nearby)
            {
                var state=deposit.Excavation;
                if((direction-deposit.Direction).sqrMagnitude*planet.Radius*planet.Radius>state.majorRadius*state.majorRadius*1.1f)continue;
                Coordinates(deposit,direction,out var distance,out var bearing);
                var angle=Mathf.Repeat(bearing-state.bearing+22.5f,360)/45;
                var sector=(int)angle;var radius=MiningRules.RadiusAt(state,sector,bearing);var depth=MiningRules.Depth(state,sector);
                // Blend the edge towards the adjacent sector so directional limits leave a stable wall.
                var fraction=angle-sector;var neighbour=(sector+(fraction<.5f?7:1))%8;
                var blend=Mathf.SmoothStep(0,1,Mathf.Clamp01(Mathf.Min(fraction,1-fraction)*8));
                radius=Mathf.Lerp(Mathf.Min(radius,MiningRules.RadiusAt(state,neighbour,bearing)),radius,blend);
                depth=Mathf.Lerp(Mathf.Min(depth,MiningRules.Depth(state,neighbour)),depth,blend);
                var drop=Mathf.Min(depth,Mathf.Max(0,(radius-distance)*.65f));
                // Flat working benches separated by slopes, instead of a smooth crater bowl.
                var terrace=Mathf.Floor(drop/4)*4+Mathf.SmoothStep(0,1,Mathf.Clamp01((drop%4-2)*.5f))*4;
                cut=Mathf.Max(cut,Mathf.Min(depth,terrace));
            }
            if(cut<=0)return height;
            foreach(var building in protectedBuildings)
            {
                if(building==null)continue;
                var foundation=planet.transform.InverseTransformPoint(building.transform.position);
                var n=foundation.normalized;
                var distance=(direction-n).magnitude*planet.Radius;
                // Retain foundations/pillars, including a mine constructed over the deposit.
                var retainedDepth=Mathf.Max(0,height-foundation.magnitude);
                cut=Mathf.Min(cut,Mathf.Lerp(retainedDepth,cut,Mathf.Clamp01((distance-Footprint(building)-4)/10)));
                if(cut<=0)return height;
            }
            return height-cut;
        }
        private void FlushExcavations()
        {
            if(dirtyDeposits.Count==0)return;
            var refresh=new HashSet<SurfaceDeposit>();
            foreach(var deposit in dirtyDeposits)
            {
                var changed=deposit.renderedRadii==null;
                for(var s=0;s<8 && !changed;s++)changed=Mathf.Abs(deposit.renderedRadii[s]-MiningRules.Radius(deposit.Excavation,s))>.75f
                    || deposit.renderedDepths[s]!=MiningRules.Depth(deposit.Excavation,s);
                if(!changed)continue;
                planet.RefreshExcavation(deposit.Direction,deposit.Excavation.majorRadius+25);
                refresh.Add(deposit);
                // A nearby pit mesh must also follow the newly lowered shared heightfield.
                foreach(var neighbour in workedDeposits)
                    if((neighbour.Direction-deposit.Direction).magnitude*planet.Radius<neighbour.Excavation.majorRadius+deposit.Excavation.majorRadius+25)refresh.Add(neighbour);
                deposit.renderedRadii=new float[8];deposit.renderedDepths=new float[8];
                for(var s=0;s<8;s++){deposit.renderedRadii[s]=MiningRules.Radius(deposit.Excavation,s);deposit.renderedDepths[s]=MiningRules.Depth(deposit.Excavation,s);}
            }
            if(refresh.Count>0)planet.RebuildExcavatedSurface();
            foreach(var deposit in refresh)BuildPitMesh(deposit);
            dirtyDeposits.Clear();
        }
        private void BuildPitMesh(SurfaceDeposit deposit)
        {
            if(deposit.pit==null)
            {
                var go=new GameObject("Excavation benches - survey "+deposit.Id,typeof(MeshFilter),typeof(MeshRenderer));
                go.transform.SetParent(visualRoot,false);deposit.pit=go.transform;
                deposit.pitMesh=new Mesh{name="Terraced excavation "+deposit.Id};go.GetComponent<MeshFilter>().sharedMesh=deposit.pitMesh;
                go.GetComponent<Renderer>().sharedMaterial=planet.TerrainMaterial;
            }
            const int resolution=65;var vertices=new Vector3[resolution*resolution];var colors=new Color[vertices.Length];var cuts=new bool[vertices.Length];var triangles=new List<int>();
            Basis(deposit.Direction,out var right,out var forward);var extent=deposit.Excavation.majorRadius+12;
            for(var y=0;y<resolution;y++)for(var x=0;x<resolution;x++)
            {
                var n=(deposit.Direction+(right*(x/(float)(resolution-1)*2-1)+forward*(y/(float)(resolution-1)*2-1))*extent/planet.Radius).normalized;
                var height=planet.EvaluateBaseHeight(n);var surface=ExcavatedHeight(n,height);var i=x+y*resolution;
                vertices[i]=n*(surface+.12f);
                cuts[i]=height-surface>.02f;
                var band=Mathf.Floor((height-surface)/4)%2;
                colors[i]=height-surface>.2f?Color.Lerp(new Color(.25f,.21f,.17f),DepositColor(deposit.Kind),band==0?.25f:.58f):planet.BiomeColor(n);
            }
            for(var y=0;y<resolution-1;y++)for(var x=0;x<resolution-1;x++)
            {
                var i=x+y*resolution;
                if(cuts[i]||cuts[i+1]||cuts[i+resolution]){triangles.Add(i);triangles.Add(i+1);triangles.Add(i+resolution);}
                if(cuts[i+1]||cuts[i+resolution+1]||cuts[i+resolution]){triangles.Add(i+1);triangles.Add(i+resolution+1);triangles.Add(i+resolution);}
            }
            deposit.pitMesh.Clear();deposit.pitMesh.vertices=vertices;deposit.pitMesh.colors=colors;deposit.pitMesh.SetTriangles(triangles,0);deposit.pitMesh.RecalculateNormals();
        }
        private void RemapBuildings(ManagedBuilding[] current)
        {
            var old=buildings;var previous=stocks.ToArray();stocks.Clear();
            foreach(var building in current)
            { var index=Array.IndexOf(old,building);stocks.Add(index>=0?previous[index]:new FacilityStock()); }
            for(var i=flights.Count-1;i>=0;i--)
            {
                var flight=flights[i];var state=flight.state;
                var source=Array.IndexOf(current,old[state.source]);var target=state.target<0?state.target:Array.IndexOf(current,old[state.target]);
                if(source<0 || state.target>=0&&target<0)
                {ReleaseFlight(flight);flights.RemoveAt(i);continue;}
                state.source=source;state.target=target;flight.state=state;
            }
            buildings=current;routes.Clear();
            incoming=new float[buildings.Length*7];foreach(var group in consumers)group.Clear();
            for(var i=0;i<buildings.Length;i++)for(var k=0;k<7;k++)
                if(IndustryRules.Accepts(buildings[i].Id,(MaterialKind)k))consumers[k].Add(i);
            RefreshProtectedBuildings();
        }
        private void ClearExcavation()
        {
            if(planet!=null)planet.ExcavationHeight=null;
            foreach(var deposit in deposits)if(deposit.pitMesh!=null)Destroy(deposit.pitMesh);
            workedDeposits.Clear();dirtyDeposits.Clear();protectedBuildings.Clear();conflict=new MiningConflict();
            excavationTiles.Clear();
            outlineRoot=null;outlineLabels.Clear();
            var clock=GetComponent<ColonyClock>();if(clock!=null)clock.MiningDecisionPending=false;
        }
    }
}
