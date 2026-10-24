using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using WH30K.Planet;
using WH30K.Gameplay.Construction;
using WH30K.Sim.Settlements;

namespace WH30K.Sim.Resources
{
    public sealed class SurfaceDeposit
    {
        public int Id; public MaterialKind Kind; public Vector3 Direction, Position; public float Remaining=2000;
        internal Transform outcrop, marker;
    }
    // Cargo is removed at dispatch and delivered only on arrival. Production never reads remote stock.
    public sealed class IndustrySystem : MonoBehaviour
    {
        private LODPlanet planet;
        private BuildingPlacementController placement;
        private Settlement settlement;
        private ResourceSystem resources;
        private readonly List<SurfaceDeposit> deposits=new List<SurfaceDeposit>();
        private readonly List<FacilityStock> stocks=new List<FacilityStock>();
        private readonly List<Flight> flights=new List<Flight>();
        private ManagedBuilding[] buildings=Array.Empty<ManagedBuilding>();
        private Transform visualRoot;
        private Material haulerMaterial, routeMaterial;
        private readonly List<Material> materials=new List<Material>();
        private float dispatchTimer;
        public int Overlay {get;private set;}
        public IReadOnlyList<SurfaceDeposit> Deposits=>deposits;
        public int CargoCount=>flights.Count;
        public void Configure(LODPlanet world, Vector3 colonyNormal, IndustrySnapshot saved=null)
        {
            Clear();planet=world;placement=GetComponent<BuildingPlacementController>();settlement=GetComponent<Settlement>();resources=GetComponent<ResourceSystem>();
            visualRoot=new GameObject("Geological survey and cargo").transform;visualRoot.SetParent(planet.transform,false);
            for(var k=0;k<5;k++) materials.Add(new Material(Shader.Find("Standard")) {color=DepositColor((MaterialKind)k)});
            haulerMaterial=new Material(Shader.Find("Standard")) {color=new Color(.56f,.38f,.13f)};
            routeMaterial=new Material(Shader.Find("Sprites/Default"));materials.Add(haulerMaterial);materials.Add(routeMaterial);
            GenerateDeposits(colonyNormal);
            if(saved!=null) foreach(var record in saved.deposits)
                if(record.id<deposits.Count) deposits[record.id].Remaining=record.remaining;
            Synchronize();
            if(saved!=null)
            {
                stocks.Clear();stocks.AddRange(saved.facilities);
                foreach(var cargo in saved.cargo) AddFlight(cargo);
            }
            Overlay=0;
        }
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
        }
        private bool Suitable(MaterialKind kind,Vector3 n)
        {
            var height=planet.EvaluateSurfacePoint(n).magnitude-planet.SeaLevel;var wet=planet.Moisture(n);
            if(height<5 || Mathf.Abs(n.y)>.86f) return false;
            switch(kind)
            {case MaterialKind.IronOre:return height>45;case MaterialKind.CopperOre:return height>20 && wet<.56f;
                case MaterialKind.Crystal:return height>120;case MaterialKind.Biomass:return wet>.49f && height<220;default:return height>12;}
        }
        private void AddDeposit(MaterialKind kind,Vector3 n)
        {
            var deposit=new SurfaceDeposit {Id=deposits.Count,Kind=kind,Direction=n};deposits.Add(deposit);
            var point=planet.EvaluateSurfacePoint(n);var rotation=Quaternion.FromToRotation(Vector3.up,n);
            deposit.Position=planet.transform.TransformPoint(point);
            var outcrop=new GameObject(IndustryRules.Name(kind)+" outcrop",typeof(MeshFilter),typeof(MeshRenderer));outcrop.transform.SetParent(visualRoot,false);
            outcrop.transform.SetPositionAndRotation(planet.transform.TransformPoint(point+n*2),planet.transform.rotation*rotation);
            outcrop.transform.localScale=kind==MaterialKind.Crystal?new Vector3(7,18,7):new Vector3(16,8,12);
            outcrop.GetComponent<MeshFilter>().sharedMesh=kind==MaterialKind.Biomass?WorldArt.Cone:WorldArt.Octahedron;
            outcrop.GetComponent<Renderer>().sharedMaterial=materials[(int)kind];deposit.outcrop=outcrop.transform;
            var marker=new GameObject("Survey pin - "+IndustryRules.Name(kind),typeof(MeshFilter),typeof(MeshRenderer));marker.transform.SetParent(visualRoot,false);
            marker.GetComponent<MeshFilter>().sharedMesh=WorldArt.Octahedron;marker.GetComponent<Renderer>().sharedMaterial=materials[(int)kind];
            deposit.marker=marker.transform;marker.SetActive(false);
        }
        public static Color DepositColor(MaterialKind kind)
        {
            switch(kind) {case MaterialKind.IronOre:return new Color(.77f,.30f,.19f);case MaterialKind.CopperOre:return new Color(.94f,.61f,.21f);
                case MaterialKind.Stone:return new Color(.67f,.69f,.66f);case MaterialKind.Biomass:return new Color(.27f,.72f,.28f);default:return new Color(.23f,.75f,.92f);}
        }
        public void SetOverlay(int mode) {Overlay=Mathf.Clamp(mode,0,6);}
        public SurfaceDeposit NearestDeposit(Vector3 worldPosition,MaterialKind kind)
        {
            SurfaceDeposit best=null;var distance=float.MaxValue;
            foreach(var deposit in deposits) if(deposit.Kind==kind && deposit.Remaining>0)
            {var d=Vector3.Distance(deposit.Position,worldPosition);if(d<distance){best=deposit;distance=d;}}
            return best;
        }
        public float[] Totals()
        {var total=new float[7];foreach(var stock in stocks) for(var k=0;k<7;k++) total[k]+=stock.quantities[k];foreach(var flight in flights) total[flight.state.kind]+=flight.state.amount;return total;}
        public string Status(ManagedBuilding building)
        {
            Synchronize();var index=Array.IndexOf(buildings,building);if(index<0 || !IndustryRules.Facility(building.Id)) return "";
            var text=new StringBuilder();
            if(IndustryRules.Extractor(building.Id))
            {
                var kind=ExtractionKind(building);var deposit=NearestDeposit(building.transform.position,kind);
                var distance=deposit==null?float.MaxValue:Vector3.Distance(building.transform.position,deposit.Position);
                text.Append(distance<=250?$"{IndustryRules.Name(kind)}: {deposit.Remaining:0} remaining\n":$"No {IndustryRules.Name(kind).ToLowerInvariant()} within 250 m.\n");
            }
            else text.Append(building.Id=="Smelter"?"2 ore + 1 biomass -> 1 metal\n":"2 metal + 1 stone + 0.5 crystal -> 1 component\n");
            for(var k=0;k<7;k++) if(stocks[index].quantities[k]>0) text.Append($"{IndustryRules.Name((MaterialKind)k)} {stocks[index].quantities[k]:0.#}  ");
            text.Append("\nCargo haulers dispatch automatically to consumers.");return text.ToString();
        }
        private MaterialKind ExtractionKind(ManagedBuilding building) => building.Id=="Quarry"?MaterialKind.Stone:building.Id=="Harvester"?MaterialKind.Biomass:
            building.State.focus==1?MaterialKind.CopperOre:building.State.focus==2?MaterialKind.Crystal:MaterialKind.IronOre;
        private void Synchronize()
        {
            if(placement==null) return;var current=placement.Buildings;
            if(current.Length==buildings.Length) return;buildings=current;
            while(stocks.Count<buildings.Length) stocks.Add(new FacilityStock());
            if(stocks.Count>buildings.Length) stocks.RemoveRange(buildings.Length,stocks.Count-buildings.Length);
        }
        private void Update()
        {
            Simulate(ColonyClock.DeltaTime);if(planet!=null) UpdateSurvey();
        }
        public void Simulate(float dt)
        {
            if(planet==null || !ColonyRules.IsFinite(dt) || dt<0) return;Synchronize();
            if(dt>0)
            {
                var jobs=0f;foreach(var building in settlement.CoreBuildings) jobs+=building.Economy.jobs;foreach(var building in buildings) jobs+=building.Economy.jobs;
                var staffing=jobs>0?Mathf.Clamp01(resources.CreateSnapshot().Workforce/jobs):0;
                for(var i=0;i<buildings.Length;i++)
                {
                    var building=buildings[i];if(!IndustryRules.Facility(building.Id) || building.State.offline) continue;
                    var focus=building.Id=="Mine"?1:building.State.focus==1?1.4f:building.State.focus==2?.75f:1;
                    stocks[i].work+=dt*staffing*(1+building.State.level*.25f)*focus;
                    while(stocks[i].work>=5)
                    {
                        stocks[i].work-=5;
                        if(IndustryRules.Extractor(building.Id))
                        {
                            var kind=ExtractionKind(building);var deposit=NearestDeposit(building.transform.position,kind);
                            if(deposit!=null && deposit.Remaining>=1 && stocks[i].quantities[(int)kind]<IndustryRules.StorageLimit
                                && Vector3.Distance(building.transform.position,deposit.Position)<=250)
                            {deposit.Remaining--;stocks[i].quantities[(int)kind]++;}
                        }
                        else IndustryRules.Process(building.Id,stocks[i].quantities);
                    }
                }
                AdvanceCargo(dt);dispatchTimer+=dt;if(dispatchTimer>=1){dispatchTimer%=1;Dispatch();}
            }
        }
        private void Dispatch()
        {
            for(var source=0;source<buildings.Length && flights.Count<64;source++)
            {
                if(buildings[source].State.offline) continue;
                for(var k=0;k<7 && flights.Count<64;k++)
                {
                    var id=buildings[source].Id;
                    if(!(id=="Mine"&&(k==0||k==1||k==4) || id=="Quarry"&&k==2 || id=="Harvester"&&k==3 || id=="Smelter"&&k==5 || id=="Works"&&k==6)) continue;
                    var kind=(MaterialKind)k;var amount=Mathf.Min(3,stocks[source].quantities[k]);if(amount<.5f) continue;
                    var target=-2;var distance=float.MaxValue;
                    if(kind==MaterialKind.Components && CoreFactory()!=null) target=-1;
                    else for(var i=0;i<buildings.Length;i++)
                    {
                        if(i==source || buildings[i].State.offline || !IndustryRules.Accepts(buildings[i].Id,kind)) continue;
                        var demand=(kind==MaterialKind.Biomass||kind==MaterialKind.Stone?8:kind==MaterialKind.Crystal?4:16)-stocks[i].quantities[k]-Incoming(i,k);
                        var d=Vector3.Distance(buildings[source].transform.position,buildings[i].transform.position);
                        if(demand>=.5f && d<distance){target=i;distance=d;}
                    }
                    if(target==-2) continue;
                    if(target>=0) amount=Mathf.Min(amount,IndustryRules.StorageLimit-stocks[target].quantities[k]-Incoming(target,k));
                    if(amount<.5f) continue;
                    stocks[source].quantities[k]-=amount;AddFlight(new CargoStock{source=source,target=target,kind=k,amount=amount});
                }
            }
        }
        private ManagedBuilding CoreFactory()
        {foreach(var building in settlement.CoreBuildings)if(building.Id=="Factory"&&!building.State.offline)return building;return null;}
        private float Incoming(int target,int kind) {var total=0f;foreach(var flight in flights)if(flight.state.target==target&&flight.state.kind==kind)total+=flight.state.amount;return total;}
        private Vector3 Destination(CargoStock cargo)
        {if(cargo.target>=0)return buildings[cargo.target].transform.position;foreach(var building in settlement.CoreBuildings)if(building.Id=="Factory")return building.transform.position;return buildings[cargo.source].transform.position;}
        private void AddFlight(CargoStock state)
        {
            var ship=new GameObject("Cargo hauler - "+IndustryRules.Name((MaterialKind)state.kind),typeof(MeshFilter),typeof(MeshRenderer));ship.transform.SetParent(visualRoot,false);
            ship.GetComponent<MeshFilter>().sharedMesh=WorldArt.Hauler;ship.GetComponent<Renderer>().sharedMaterial=haulerMaterial;ship.transform.localScale=Vector3.one*2;
            ship.transform.position=FlightPoint(state,state.progress);
            var route=new GameObject("Cargo route",typeof(LineRenderer));route.transform.SetParent(visualRoot,false);
            var line=route.GetComponent<LineRenderer>();line.sharedMaterial=routeMaterial;line.startColor=line.endColor=new Color(.8f,.65f,.28f,.35f);line.widthMultiplier=1.2f;line.positionCount=25;
            for(var i=0;i<25;i++)line.SetPosition(i,FlightPoint(state,i/24f));
            flights.Add(new Flight{state=state,ship=ship.transform,line=line});
        }
        private Vector3 FlightPoint(CargoStock cargo,float t)
        {
            var a=planet.transform.InverseTransformPoint(buildings[cargo.source].transform.position).normalized;
            var b=planet.transform.InverseTransformPoint(Destination(cargo)).normalized;
            var n=Vector3.Slerp(a,b,t).normalized;
            var height=Mathf.Max(planet.SeaLevel,planet.EvaluateSurfacePoint(n).magnitude)+12+Mathf.Sin(t*Mathf.PI)*45;
            return planet.transform.TransformPoint(n*height);
        }
        private void AdvanceCargo(float seconds)
        {
            for(var i=flights.Count-1;i>=0;i--)
            {
                var flight=flights[i];var state=flight.state;
                var a=planet.transform.InverseTransformPoint(buildings[state.source].transform.position).normalized;
                var b=planet.transform.InverseTransformPoint(Destination(state)).normalized;
                var duration=Mathf.Max(3,Vector3.Angle(a,b)*Mathf.Deg2Rad*planet.Radius/80);
                state.progress=Mathf.Clamp01(state.progress+seconds/duration);flight.state=state;
                var point=FlightPoint(state,state.progress);var next=FlightPoint(state,Mathf.Min(1,state.progress+.01f));
                flight.ship.position=point;if((next-point).sqrMagnitude>.001f)flight.ship.rotation=Quaternion.LookRotation(next-point,(point-planet.transform.position).normalized);
                if(state.progress<1 || (state.target==-1 && CoreFactory()==null)) continue;
                if(state.target==-1) resources.ModifyStockpile(state.amount*25);
                else stocks[state.target].quantities[state.kind]+=state.amount;
                Destroy(flight.ship.gameObject);Destroy(flight.line.gameObject);flights.RemoveAt(i);
            }
        }
        private void UpdateSurvey()
        {
            if(Camera.main==null)return;var camera=Camera.main.transform.position;
            foreach(var deposit in deposits)
            {
                var world=deposit.Position;
                var distance=Vector3.Distance(world,camera);var visible=Vector3.Dot(world-planet.transform.position,camera-world)>0;
                deposit.outcrop.gameObject.SetActive(visible&&distance<2000&&deposit.Remaining>0);
                var show=visible&&deposit.Remaining>0&&(Overlay==1||Overlay==(int)deposit.Kind+2);deposit.marker.gameObject.SetActive(show);
                if(show){var scale=Mathf.Clamp(distance*.012f,7,260);deposit.marker.position=world+planet.transform.TransformDirection(deposit.Direction)*(scale+45);
                    deposit.marker.rotation=Quaternion.FromToRotation(Vector3.up,planet.transform.TransformDirection(deposit.Direction));deposit.marker.localScale=new Vector3(scale,scale*2,scale);}
            }
            foreach(var flight in flights)flight.line.enabled=Overlay!=0;
        }
        public IndustrySnapshot Capture()
        {
            Synchronize();var savedStocks=new FacilityStock[stocks.Count];for(var i=0;i<stocks.Count;i++)savedStocks[i]=new FacilityStock{quantities=(float[])stocks[i].quantities.Clone(),work=stocks[i].work};
            var savedDeposits=new DepositStock[deposits.Count];for(var i=0;i<deposits.Count;i++)savedDeposits[i]=new DepositStock{id=i,remaining=deposits[i].Remaining};
            return new IndustrySnapshot{facilities=savedStocks,deposits=savedDeposits,cargo=flights.ConvertAll(f=>f.state).ToArray()};
        }
        private void Clear()
        {if(visualRoot!=null)Destroy(visualRoot.gameObject);foreach(var material in materials)if(material!=null)Destroy(material);materials.Clear();deposits.Clear();stocks.Clear();flights.Clear();buildings=Array.Empty<ManagedBuilding>();dispatchTimer=0;}
        private void OnDestroy(){Clear();}
        private sealed class Flight{public CargoStock state;public Transform ship;public LineRenderer line;}
    }
}
