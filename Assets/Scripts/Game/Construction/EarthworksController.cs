using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using WH30K.Game;
using WH30K.Planet;
using WH30K.Sim;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;
using WH30K.UI;

namespace WH30K.Gameplay.Construction
{
    public sealed class EarthworksController : MonoBehaviour
    {
        private LODPlanet planet;
        private BuildingPlacementController placement;
        private Settlement settlement;
        private IndustrySystem industry;
        private NewGameMenu menu;
        private Transform vehicle, root;
        private LineRenderer brush, destination, selectedRing;
        private readonly List<Material> materials = new List<Material>();
        private readonly List<TerrainEditStamp> edits = new List<TerrainEditStamp>();
        private readonly Dictionary<Vector3Int, List<TerrainEditStamp>> tiles = new Dictionary<Vector3Int, List<TerrainEditStamp>>();
        private EarthworksSnapshot command = new EarthworksSnapshot();
        private Vector3 direction, forward;
        private Vector3 positionedDirection, positionedForward;
        private int positionedRevision = -1;
        private ManagedBuilding demolitionTarget;
        private int openedFrame = -1;
        private readonly List<Transform> wheels = new List<Transform>();
        private bool hoverValid;
        public bool IsOpen { get; private set; }
        public EarthworkMode Mode { get; private set; }
        public float BrushRadius { get; private set; } = 24;
        public float StepHeight { get; private set; } = 4;
        public bool HasVehicle => vehicle != null;
        public Transform Vehicle => vehicle;
        public bool Travelling => command.travelling;
        public int EditCount => edits.Count;
        public string Status { get; private set; } = "Select the tracked dozer or choose a tool.";
        public string DemolitionName => demolitionTarget != null ? demolitionTarget.DisplayName : "Click a player-built building.";
        public bool CanDemolish => demolitionTarget != null && Array.IndexOf(placement.Buildings, demolitionTarget) >= 0;
        public Color ToolColor => ColorFor(Mode);
        public static Color ColorFor(EarthworkMode mode) => mode == EarthworkMode.Flatten ? new Color(.12f,.88f,.94f)
            : mode == EarthworkMode.Raise ? new Color(1,.66f,.14f)
            : mode == EarthworkMode.Lower ? new Color(.74f,.43f,1)
            : mode == EarthworkMode.Demolish ? new Color(1,.28f,.21f) : new Color(.31f,.85f,.64f);

        public void Configure(LODPlanet world, Vector3 colony, EarthworksSnapshot saved = null, bool spawnVehicle = true)
        {
            Close();
            if (planet != null) planet.EarthworksHeight = null;
            if (root != null) { root.gameObject.SetActive(false); Destroy(root.gameObject); }
            foreach (var material in materials) Destroy(material);
            materials.Clear(); edits.Clear(); tiles.Clear(); wheels.Clear(); vehicle = null;
            positionedRevision = -1;
            planet = world; placement = GetComponent<BuildingPlacementController>();
            settlement = GetComponent<Settlement>(); industry = GetComponent<IndustrySystem>(); menu = GetComponent<NewGameMenu>();
            root = new GameObject("Earthworks and tracked engineering vehicle").transform;
            root.SetParent(planet.transform, false);
            command = saved != null ? JsonUtility.FromJson<EarthworksSnapshot>(JsonUtility.ToJson(saved)) : new EarthworksSnapshot();
            if (saved?.edits != null) foreach (var edit in saved.edits) { edits.Add(edit); Index(edit); }
            planet.EarthworksHeight = EditedHeight;
            foreach (var edit in edits) planet.RefreshExcavation(new Vector3(edit.x, edit.y, edit.z), edit.radius + 4);
            planet.RebuildExcavatedSurface();
            if (saved != null && saved.hasVehicle)
            {
                direction = new Vector3(saved.x,saved.y,saved.z).normalized;
                forward = new Vector3(saved.forwardX,saved.forwardY,saved.forwardZ).normalized;
                CreateVehicle();
            }
            else if (spawnVehicle && saved == null) SpawnNear(colony);
            brush = MakeLine("Earthworks brush footprint"); destination = MakeLine("Dozer command destination");
            selectedRing = MakeLine("Selected dozer perimeter");
            Status = command.travelling ? "Saved command restored." : HasVehicle ? "Dozer ready. Select it or open EARTHWORKS." : "No safe parking site. Open EARTHWORKS to deploy.";
        }

        public EarthworksSnapshot Capture()
        {
            var result = JsonUtility.FromJson<EarthworksSnapshot>(JsonUtility.ToJson(command));
            result.hasVehicle = HasVehicle;
            result.x=direction.x; result.y=direction.y; result.z=direction.z;
            result.forwardX=forward.x; result.forwardY=forward.y; result.forwardZ=forward.z;
            result.edits=edits.ToArray();
            return result;
        }

        public void Open(EarthworkMode mode)
        {
            if (planet == null) return;
            placement.CancelPlacement(); GetComponent<BuildingSelection>()?.Select(null);
            IsOpen = true; Mode = mode; demolitionTarget = null; openedFrame = Time.frameCount;
            Status = mode == EarthworkMode.Demolish ? "Click a building, then REMOVE. Core facilities are protected."
                : "Click terrain to issue one command. Dozer works on arrival; right-drag still rotates the camera.";
        }
        public void Close()
        {
            IsOpen = false; demolitionTarget = null;
            if (brush != null) brush.gameObject.SetActive(false);
            if (selectedRing != null) selectedRing.gameObject.SetActive(false);
            if (planet?.TerrainMaterial != null) planet.TerrainMaterial.SetVector("_EarthworkBrush", Vector4.zero);
        }
        public void SetMode(EarthworkMode mode) { Mode=mode; demolitionTarget=null; }
        public void CycleRadius() { BrushRadius=BrushRadius==16?24:BrushRadius==24?40:BrushRadius==40?64:16; }
        public void CycleStep() { StepHeight=StepHeight==2?4:StepHeight==4?8:2; }
        public void Stop() { command.travelling=false; Status="Dozer stopped. Terrain already completed is retained."; }
        public bool Deploy()
        {
            if (HasVehicle || planet == null) return false;
            return SpawnNear(settlement.CreateSnapshot().normal);
        }

        private bool SpawnNear(Vector3 colony)
        {
            Basis(colony,out var right,out var ahead);
            for (var i=0;i<256;i++)
            {
                var angle=i*2.399963f; var range=70+(i/16)*22;
                var n=(colony+(right*Mathf.Cos(angle)+ahead*Mathf.Sin(angle))*range/planet.Radius).normalized;
                if (!Driveable(n,out _)) continue;
                direction=n; forward=Vector3.ProjectOnPlane(ahead,n).normalized;
                CreateVehicle(); return true;
            }
            Status="No clear land near the colony for the dozer."; return false;
        }

        private void Update()
        {
            if (planet == null || !GameSettings.HasActiveGame) return;
            if (IsOpen && (placement.IsPlacing || Input.GetKeyDown(KeyCode.Escape))) Close();
            Advance(ColonyClock.DeltaTime);
            if(selectedRing!=null)
            {
                selectedRing.gameObject.SetActive(IsOpen && HasVehicle && Mode!=EarthworkMode.Demolish);
                if(selectedRing.gameObject.activeSelf)DrawCircle(selectedRing,direction,ColorFor(EarthworkMode.Move),22);
            }
            if (destination != null)
            {
                destination.gameObject.SetActive(command.travelling);
                if (command.travelling) DrawCircle(destination,Target,ModeForCommandColor(),command.command==0?14:command.radius);
            }
            if (!IsOpen || menu.HasOpenEvent)
            { HideBrush(); return; }
            if(openedFrame==Time.frameCount)return;
            var overUI=EventSystem.current!=null && EventSystem.current.IsPointerOverGameObject();
            if (Mode == EarthworkMode.Demolish)
            {
                HideBrush();
                if (!overUI && Input.GetMouseButtonDown(0))
                {
                    SelectDemolitionTarget(HitBuilding());
                }
                if (demolitionTarget!=null) DrawCircle(brush,planet.transform.InverseTransformPoint(demolitionTarget.transform.position).normalized,
                    CanDemolish?ToolColor:Color.gray,demolitionTarget.ReservedFootprintRadius+2);
                return;
            }
            if (overUI || !WH30K.CameraSystem.SimpleOrbitCamera.TryScreenRay(Camera.main,Input.mousePosition,out var ray)
                || !placement.TryRaycastPlanet(ray,out var worldDirection))
            { HideBrush(); return; }
            var n=planet.transform.InverseTransformDirection(worldDirection).normalized;
            hoverValid=Mode==EarthworkMode.Move?Driveable(n,out _):CanEdit(n,BrushRadius,out _);
            DrawCircle(brush,n,hoverValid?ToolColor:ColorFor(EarthworkMode.Demolish),Mode==EarthworkMode.Move?14:BrushRadius);
            var material=planet.TerrainMaterial;
            if (material!=null)
            {
                var world=planet.transform.TransformDirection(n);
                material.SetVector("_EarthworkBrush",new Vector4(world.x,world.y,world.z,Mode==EarthworkMode.Move?14:BrushRadius));
                material.SetColor("_EarthworkColor",hoverValid?ToolColor:ColorFor(EarthworkMode.Demolish));
            }
            if (Input.GetMouseButtonDown(0)) IssueCommand(n,Mode,BrushRadius,StepHeight);
        }

        private Color ModeForCommandColor() => ColorFor((EarthworkMode)command.command);
        private Vector3 Target => new Vector3(command.targetX,command.targetY,command.targetZ);
        private ManagedBuilding HitBuilding()
        {
            if (!WH30K.CameraSystem.SimpleOrbitCamera.TryScreenRay(Camera.main,Input.mousePosition,out var ray)
                || !Physics.Raycast(ray,out var hit,Camera.main.farClipPlane)) return null;
            var building=hit.collider.GetComponentInParent<ManagedBuilding>();
            if(building==null)return null;
            var outward=(building.transform.position-planet.transform.position).normalized;
            return Vector3.Dot(outward,Camera.main.transform.position-building.transform.position)>0?building:null;
        }

        public void SelectDemolitionTarget(ManagedBuilding building)
        {
            demolitionTarget=building;
            Status=building==null?"Click a player-built building.":CanDemolish?"Removal loses jobs, housing, local stock and linked cargo. No refund.":"Core facility protected. Choose a player-built building.";
        }

        public bool RemoveTarget()
        {
            if (!CanDemolish || menu.HasOpenEvent || GetComponent<ColonyClock>().MiningDecisionPending) return false;
            var target=demolitionTarget; var name=target.DisplayName;
            GetComponent<BuildingSelection>()?.Select(null); demolitionTarget=null;
            if (!placement.Demolish(target)) return false;
            Status=name+" removed. No refund."; menu.AppendEventLog(Status); return true;
        }

        public bool IssueCommand(Vector3 n, EarthworkMode mode, float radius=24, float step=4)
        {
            if (!HasVehicle || !ConstructionSave.ValidDirection(n.x,n.y,n.z) || menu.HasOpenEvent
                || mode< EarthworkMode.Move || mode>EarthworkMode.Lower) { Status="Select or deploy the dozer first."; return false; }
            if (mode==EarthworkMode.Move)
            {
                if (!Driveable(n,out var reason)) { Status=reason; return false; }
            }
            else if (!CanEdit(n,radius,out var reason)) { Status=reason; return false; }
            var value=mode==EarthworkMode.Flatten?planet.EvaluateSurfacePoint(n).magnitude:mode==EarthworkMode.Lower?-step:step;
            if (mode!=EarthworkMode.Move && !EarthworksRules.ValidEdit(new TerrainEditStamp{x=n.x,y=n.y,z=n.z,radius=radius,value=value,mode=(int)mode})) return false;
            command.travelling=true; command.targetX=n.x; command.targetY=n.y; command.targetZ=n.z;
            command.command=(int)mode; command.radius=radius; command.value=value; command.work=0;
            Status=mode==EarthworkMode.Move?"Moving to marked point.":"Moving into working range. Four simulation seconds per earthwork command.";
            return true;
        }

        // Also exercised by the real-scene checks. Substeps prevent high-speed/pause tunnelling.
        public void Advance(float seconds)
        {
            if (!HasVehicle) return;
            PositionVehicle();
            if (!command.travelling || !ColonyRules.IsFinite(seconds) || seconds<=0) return;
            var target=Target;
            var distance=EarthworksRules.SurfaceDistance(direction.x,direction.y,direction.z,target.x,target.y,target.z,planet.Radius);
            var stop=command.command==0?.5f:Mathf.Min(EarthworksRules.Reach,command.radius+EarthworksRules.VehicleRadius+4);
            var travel=Mathf.Min(Mathf.Max(0,distance-stop),EarthworksRules.Speed*seconds);
            var moved=0f;
            while (moved<travel)
            {
                var stride=Mathf.Min(2,travel-moved);
                // A float acos cannot resolve metre-sized angles on a continental sphere.
                var remaining=EarthworksRules.SurfaceDistance(direction.x,direction.y,direction.z,target.x,target.y,target.z,planet.Radius);
                var next=Vector3.Lerp(direction,target,Mathf.Min(1,stride/Mathf.Max(.001f,remaining))).normalized;
                if (!Driveable(next,out var reason)) { Stop(); Status="Route blocked: "+reason+" Click a clear waypoint."; return; }
                // Unity normalizes vectors shorter than 1e-5 to zero. Unit-sphere
                // deltas for local journeys are smaller than that on large worlds.
                var heading=Vector3.ProjectOnPlane((target-direction)*planet.Radius,direction);
                if(heading.sqrMagnitude>.000001f)forward=heading.normalized;
                direction=next; forward=Vector3.ProjectOnPlane(forward,direction).normalized; moved+=stride;
            }
            foreach(var wheel in wheels)wheel.Rotate(Vector3.up,-moved/1.75f*Mathf.Rad2Deg,Space.Self);
            PositionVehicle();
            if (distance-travel>stop+.05f) return;
            if (command.command==0) { command.travelling=false; Status="Destination reached."; return; }
            if (!CanEdit(target,command.radius,out var failure)) { Stop(); Status=failure; return; }
            command.work+=Mathf.Max(0,seconds-travel/EarthworksRules.Speed);
            Status=$"Earthworks {Mathf.Clamp01(command.work/EarthworksRules.WorkSeconds):P0} / {edits.Count}/{EarthworksRules.MaxEdits} completed areas";
            if (command.work<EarthworksRules.WorkSeconds) return;
            var edit=new TerrainEditStamp {x=target.x,y=target.y,z=target.z,radius=command.radius,value=command.value,mode=command.command};
            edits.Add(edit); Index(edit); command.travelling=false;
            planet.RefreshExcavation(target,edit.radius+4); planet.RebuildExcavatedSurface();
            settlement.NotifyResidentialGeometry(); PositionVehicle();
            Status=$"{(EarthworkMode)edit.mode} complete / {edit.radius:0} m radius. Click another point to continue.";
            menu.AppendEventLog(Status);
        }

        private bool CanEdit(Vector3 n,float radius,out string reason)
        {
            reason="";
            if (!ColonyRules.IsFinite(radius) || radius<16 || radius>64) { reason="Brush radius must be 16–64 m."; return false; }
            if (edits.Count>=EarthworksRules.MaxEdits) { reason="Earthworks area limit reached (512 commands)."; return false; }
            foreach (var b in settlement.AllBuildings)
                if (b.Id == "ColonyShip" ? b.ShipFootprintDistance(planet.transform.TransformPoint(planet.EvaluateSurfacePoint(n))) < radius + 8
                    : (planet.transform.InverseTransformPoint(b.transform.position).normalized-n).magnitude*planet.Radius<radius+b.ReservedFootprintRadius+8)
                { reason="Brush overlaps a building or reserved development. Move the point or reduce radius."; return false; }
            foreach (var deposit in industry.Deposits)
                if (deposit.Excavation!=null && (deposit.Direction-n).magnitude*planet.Radius<radius+deposit.Excavation.majorRadius+16)
                { reason="Surveyed mineral body / excavation protected. Work outside its extent."; return false; }
            Basis(n,out var right,out var ahead);
            // Reject the whole brush if any sampled area could breach the waterline, even at the maximum cut.
            for (var i=0;i<17;i++)
            {
                var a=i*Mathf.PI/8;
                var p=i==16?n:(n+(right*Mathf.Cos(a)+ahead*Mathf.Sin(a))*radius/planet.Radius).normalized;
                if (planet.EvaluateSurfacePoint(p).magnitude<planet.SeaLevel+10)
                { reason="Earthworks require dry land, at least 10 m above sea level."; return false; }
            }
            return true;
        }

        private bool Driveable(Vector3 n,out string reason)
        {
            reason=""; var h=planet.EvaluateSurfacePoint(n).magnitude;
            if (h<planet.SeaLevel+2) { reason="Water is impassable."; return false; }
            foreach (var b in settlement.AllBuildings)
                if (b.Id == "ColonyShip" ? b.ShipFootprintDistance(planet.transform.TransformPoint(planet.EvaluateSurfacePoint(n))) < EarthworksRules.VehicleRadius + 3
                    : (planet.transform.InverseTransformPoint(b.transform.position).normalized-n).magnitude*planet.Radius<b.ReservedFootprintRadius+EarthworksRules.VehicleRadius+3)
                { reason="Building or reserved development in the way."; return false; }
            Basis(n,out var right,out var ahead);
            foreach (var offset in new[]{right,-right,ahead,-ahead})
                if (Mathf.Abs(planet.EvaluateSurfacePoint(n+offset*8/planet.Radius).magnitude-h)>5)
                { reason="Slope too steep for tracks."; return false; }
            return true;
        }

        private Vector3Int Tile(Vector3 n) => new Vector3Int(Mathf.FloorToInt(n.x*planet.Radius/128),Mathf.FloorToInt(n.y*planet.Radius/128),Mathf.FloorToInt(n.z*planet.Radius/128));
        private void Index(TerrainEditStamp edit)
        {
            var n=new Vector3(edit.x,edit.y,edit.z); var extent=Vector3.one*(edit.radius+2)/planet.Radius;
            var min=Tile(n-extent); var max=Tile(n+extent);
            for(var x=min.x;x<=max.x;x++)for(var y=min.y;y<=max.y;y++)for(var z=min.z;z<=max.z;z++)
            {
                var tile=new Vector3Int(x,y,z);
                if(!tiles.TryGetValue(tile,out var list))tiles[tile]=list=new List<TerrainEditStamp>();
                list.Add(edit);
            }
        }
        private float EditedHeight(Vector3 n,float height)
        {
            if(!tiles.TryGetValue(Tile(n),out var list))return height;
            var original=height;
            foreach(var edit in list)
                height=EarthworksRules.Apply(height,edit,(n-new Vector3(edit.x,edit.y,edit.z)).magnitude*planet.Radius);
            return EarthworksRules.ClampHeight(original,height,planet.SeaLevel);
        }
        public bool IsWorked(Vector3 n)
        {
            if(!tiles.TryGetValue(Tile(n),out var list))return false;
            foreach(var edit in list)if((n-new Vector3(edit.x,edit.y,edit.z)).magnitude*planet.Radius<edit.radius)return true;
            return false;
        }
        private static void Basis(Vector3 n,out Vector3 right,out Vector3 ahead)
        { right=Vector3.Cross(n,Mathf.Abs(n.y)>.95f?Vector3.right:Vector3.up).normalized; ahead=Vector3.Cross(n,right); }
        private void PositionVehicle()
        {
            if(vehicle==null)return;
            if(positionedRevision==planet.SurfaceRevision && positionedDirection.Equals(direction) && positionedForward.Equals(forward))return;
            positionedRevision=planet.SurfaceRevision;positionedDirection=direction;positionedForward=forward;
            var up=planet.transform.TransformDirection(direction);
            var tangent=planet.transform.TransformDirection(forward);
            // Tracks ride on the highest of four support samples rather than sinking on a grade.
            var h=planet.EvaluateSurfacePoint(direction).magnitude;
            var side=Vector3.Cross(direction,forward).normalized;
            foreach(var offset in new[]{side*8+forward*10,side*8-forward*10,-side*8+forward*10,-side*8-forward*10})
                h=Mathf.Max(h,planet.EvaluateSurfacePoint(direction+offset/planet.Radius).magnitude);
            vehicle.SetPositionAndRotation(planet.transform.TransformPoint(direction*(h+.35f)),Quaternion.LookRotation(tangent,up));
        }
        private Material Surface(string name,Color color)
        {
            var m=new Material(Shader.Find("Standard")){name=name,color=color,enableInstancing=true}; materials.Add(m); return m;
        }
        private void CreateVehicle()
        {
            vehicle=new GameObject("Praetor heavy tracked earthmover",typeof(BulldozerVehicle)).transform; vehicle.SetParent(root,false);
            var yellow=Surface("Dozer ochre armour",new Color(.76f,.48f,.10f));
            var track=Surface("Dozer forged iron tracks",new Color(.10f,.12f,.13f));
            var steel=Surface("Dozer blade and track shoes",new Color(.36f,.40f,.39f));
            var glass=Surface("Dozer cyan cab glass",new Color(.10f,.38f,.43f));
            Part("Armoured chassis",new Vector3(0,4,0),new Vector3(17,5,23),yellow);
            Part("Engine hood",new Vector3(0,8,-5),new Vector3(13,5,11),yellow);
            Part("Cab",new Vector3(0,9,4),new Vector3(9,9,8),yellow);
            Part("Windshield",new Vector3(0,10,8.1f),new Vector3(7,4,.3f),glass);
            Part("Cab roof",new Vector3(0,14,4),new Vector3(11,1,10),track);
            Part("Exhaust stack",new Vector3(6,11,-5),new Vector3(1.2f,9,1.2f),track);
            for(var side=-1;side<=1;side+=2)
            {
                Part("Continuous track",new Vector3(side*9,2.5f,0),new Vector3(5,5,27),track);
                for(var i=0;i<12;i++)
                {
                    var z=-12+i*2.15f;
                    Part("Track shoe",new Vector3(side*9,5.1f,z),new Vector3(5.2f,.5f,1.5f),steel);
                    Part("Track shoe",new Vector3(side*9,.3f,z),new Vector3(5.2f,.5f,1.5f),steel);
                }
                for(var i=0;i<5;i++)
                {
                    var wheel=GameObject.CreatePrimitive(PrimitiveType.Cylinder); wheel.name="Tracked road wheel";
                    wheel.transform.SetParent(vehicle,false); wheel.transform.localPosition=new Vector3(side*11.55f,2.5f,-10+i*5);
                    wheel.transform.localRotation=Quaternion.Euler(0,0,90); wheel.transform.localScale=new Vector3(3.5f,.3f,3.5f);
                    wheel.GetComponent<Renderer>().sharedMaterial=steel; wheels.Add(wheel.transform);
                }
                Part("Blade hydraulic arm",new Vector3(side*6,3,12),new Vector3(1.6f,2,13),steel);
                Part("Cab side glass",new Vector3(side*4.6f,10,4),new Vector3(.3f,4,6),glass);
            }
            Part("Wide grading blade",new Vector3(0,3,18),new Vector3(27,6,2),yellow);
            Part("Blade cutting edge",new Vector3(0,.7f,19.1f),new Vector3(28,1.3f,1),steel);
            for(var x=-12;x<=12;x+=3) Part("Blade hazard stripe",new Vector3(x,5.3f,19.1f),new Vector3(1.4f,1.2f,.2f),track);
            PositionVehicle();
        }
        private void Part(string name,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.SetParent(vehicle,false);
            go.transform.localPosition=position; go.transform.localScale=scale; go.GetComponent<Renderer>().sharedMaterial=material;
        }
        private LineRenderer MakeLine(string name)
        {
            var go=new GameObject(name,typeof(LineRenderer)); go.transform.SetParent(root,false);
            var line=go.GetComponent<LineRenderer>(); var material=new Material(Shader.Find("Unlit/Color")); materials.Add(material);
            line.sharedMaterial=material; line.useWorldSpace=true; line.loop=true; line.positionCount=64; line.widthMultiplier=.65f;
            go.SetActive(false); return line;
        }
        private void DrawCircle(LineRenderer line,Vector3 n,Color color,float radius)
        {
            if(line==null)return;
            line.gameObject.SetActive(true); line.sharedMaterial.color=color;
            Basis(n,out var right,out var ahead);
            for(var i=0;i<64;i++)
            {
                var a=i*Mathf.PI*2/64; var p=(n+(right*Mathf.Cos(a)+ahead*Mathf.Sin(a))*radius/planet.Radius).normalized;
                line.SetPosition(i,planet.transform.TransformPoint(planet.EvaluateSurfacePoint(p)+p*.8f));
            }
        }
        private void HideBrush()
        {
            if(brush!=null)brush.gameObject.SetActive(false);
            if(planet?.TerrainMaterial!=null)planet.TerrainMaterial.SetVector("_EarthworkBrush",Vector4.zero);
        }
        private void OnDestroy()
        {
            if(planet!=null)planet.EarthworksHeight=null;
            if(root!=null)Destroy(root.gameObject);
            foreach(var material in materials)if(material!=null)Destroy(material);
        }
    }
}
