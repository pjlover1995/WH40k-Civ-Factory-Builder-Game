using System;
using System.Collections.Generic;
using UnityEngine;

namespace WH30K.Sim.Resources
{
    public sealed partial class IndustrySystem
    {
        private sealed class PitHaulage
        {
            public Transform root;
            public Mesh mesh;
            public readonly List<Vector3> path = new List<Vector3>();
            public readonly List<float> lengths = new List<float>();
            public readonly List<Transform> trucks = new List<Transform>();
            public readonly float[] travelled=new float[2];
            public bool operating, flooded;
        }
        private readonly Dictionary<SurfaceDeposit,PitHaulage> pitHaulage = new Dictionary<SurfaceDeposit,PitHaulage>();
        private Material haulRoadMaterial, tyreTrackMaterial;
        public int PitHaulerCount
        { get { var count=0;foreach(var haul in pitHaulage.Values)count+=haul.trucks.Count;return count; } }

        private void ActivatePitHaulers(SurfaceDeposit deposit,float amount)
        {
            if(pitHaulage.TryGetValue(deposit,out var haul) && amount>0)haul.operating=true;
        }
        private void ResetPitOperations()
        { foreach(var haul in pitHaulage.Values)haul.operating=false; }
        private void MarkPitOperating(WH30K.Gameplay.Construction.ManagedBuilding building,float work)
        {
            if(work<=0 || (building.Id!="Mine" && building.Id!="Quarry"))return;
            var deposit=DepositFor(building);if(deposit==null || deposit.Remaining<=0)return;
            var index=Array.IndexOf(buildings,building);
            if(index<0 || stocks[index].quantities[(int)deposit.Kind]>=StoreCapacity(index,deposit.Kind))return;
            if(pitHaulage.TryGetValue(deposit,out var haul))haul.operating=true;
        }
        private Vector3 PitDirection(SurfaceDeposit deposit,float x,float y)
        {
            Basis(deposit.Direction,out var right,out var forward);
            var bearing=deposit.Excavation.bearing*Mathf.Deg2Rad;
            var tangent=right*(x*Mathf.Cos(bearing)-y*Mathf.Sin(bearing))
                +forward*(x*Mathf.Sin(bearing)+y*Mathf.Cos(bearing));
            return (deposit.Direction+tangent/planet.Radius).normalized;
        }
        private Vector3 RoadSurface(Vector3 direction)
            => planet.EvaluateSurfacePoint(direction)+direction*.18f;
        private void RebuildPitRoad(SurfaceDeposit deposit)
        {
            var state=deposit.Excavation;
            if(state.layoutVersion!=1 || MiningRules.RoadWidth(state)<1)return;
            if(!pitHaulage.TryGetValue(deposit,out var haul))
            {
                haul=new PitHaulage();pitHaulage.Add(deposit,haul);
                haul.root=new GameObject("Haul ramp and vehicles - survey "+deposit.Id).transform;
                haul.root.SetParent(visualRoot,false);
                haul.mesh=new Mesh{name="Graded haul road "+deposit.Id};
                haul.root.gameObject.AddComponent<MeshFilter>().sharedMesh=haul.mesh;
                var renderer=haul.root.gameObject.AddComponent<MeshRenderer>();
                if(haulRoadMaterial==null)
                {
                    haulRoadMaterial=new Material(Shader.Find("Standard")){color=new Color(.26f,.22f,.17f),enableInstancing=true};
                    tyreTrackMaterial=new Material(Shader.Find("Standard")){color=new Color(.095f,.085f,.07f),enableInstancing=true};
                    materials.Add(haulRoadMaterial);materials.Add(tyreTrackMaterial);
                }
                renderer.sharedMaterials=new[]{haulRoadMaterial,tyreTrackMaterial};
                haul.trucks.Add(CreatePitTruck(haul.root));haul.trucks.Add(CreatePitTruck(haul.root));
                haul.operating=true;
            }
            var oldLength=haul.lengths.Count>0?haul.lengths[haul.lengths.Count-1]:0;
            var returning=new bool[2];
            for(var i=0;i<2;i++)returning[i]=oldLength>0 && Mathf.Repeat(haul.travelled[i],oldLength*2)>oldLength;
            haul.path.Clear();haul.lengths.Clear();haul.flooded=false;
            const int samples=256;
            var directions=new List<Vector3>();
            AppendRimApproach(deposit,directions);
            for(var i=0;i<=samples;i++)
            {
                MiningRules.HaulPoint(state,i/(float)samples,out var x,out var y,out _);
                directions.Add(PitDirection(deposit,x,y));
            }
            var vertices=new List<Vector3>();var road=new List<int>();var tracks=new List<int>();
            var width=MiningRules.RoadWidth(state);
            for(var i=0;i<directions.Count;i++)
            {
                var direction=directions[i];
                var point=RoadSurface(direction);
                if(point.magnitude<=planet.SeaLevel+.5f)haul.flooded=true;
                haul.path.Add(point);
                haul.lengths.Add(i==0?0:haul.lengths[i-1]+Vector3.Distance(point,haul.path[i-1]));
                // Sample each edge against the exact same excavated heightfield as the pit.
                var tangent=((i==directions.Count-1?direction-directions[i-1]:directions[i+1]-direction)*planet.Radius).normalized;
                var side=Vector3.Cross(direction,tangent).normalized;
                AddRoadPair(vertices,direction,side,-width*.5f,width*.5f,.01f);
                AddRoadPair(vertices,direction,side,-width*.28f,-width*.15f,.035f);
                AddRoadPair(vertices,direction,side,width*.15f,width*.28f,.035f);
                if(i==0)continue;
                for(var strip=0;strip<3;strip++)
                {
                    var start=(i-1)*6+strip*2;var list=strip==0?road:tracks;
                    list.Add(start);list.Add(start+6);list.Add(start+1);
                    list.Add(start+1);list.Add(start+6);list.Add(start+7);
                }
            }
            haul.mesh.Clear();haul.mesh.SetVertices(vertices);haul.mesh.subMeshCount=2;
            haul.mesh.SetTriangles(road,0);haul.mesh.SetTriangles(tracks,1);haul.mesh.RecalculateNormals();
            var length=haul.lengths[haul.lengths.Count-1];
            for(var truckIndex=0;truckIndex<2;truckIndex++)
            {
                if(oldLength<=0){haul.travelled[truckIndex]=truckIndex*length;continue;}
                var position=haul.trucks[truckIndex].localPosition;var closest=float.MaxValue;var along=0f;
                for(var i=1;i<haul.path.Count;i++)
                {
                    var segment=haul.path[i]-haul.path[i-1];
                    var t=Mathf.Clamp01(Vector3.Dot(position-haul.path[i-1],segment)/Mathf.Max(.0001f,segment.sqrMagnitude));
                    var distance=(position-(haul.path[i-1]+segment*t)).sqrMagnitude;
                    if(distance>=closest)continue;
                    closest=distance;along=Mathf.Lerp(haul.lengths[i-1],haul.lengths[i],t);
                }
                haul.travelled[truckIndex]=returning[truckIndex]?2*length-along:along;
            }
            PositionPitTrucks(haul,width);
        }
        private void AppendRimApproach(SurfaceDeposit deposit,List<Vector3> directions)
        {
            WH30K.Gameplay.Construction.ManagedBuilding mine=null;
            foreach(var building in buildings)
                if(DepositFor(building)==deposit && (mine==null || Vector3.Distance(building.transform.position,deposit.Position)<Vector3.Distance(mine.transform.position,deposit.Position)))mine=building;
            if(mine==null)return;
            var state=deposit.Excavation;
            var mineDirection=planet.transform.InverseTransformPoint(mine.transform.position).normalized;
            Coordinates(deposit,mineDirection,out var distance,out var bearing);
            var angle=(bearing-state.bearing)*Mathf.Deg2Rad;
            var startAngle=Mathf.Atan2(Mathf.Sin(angle)/state.minorRadius,Mathf.Cos(angle)/state.majorRadius);
            MiningRules.HaulPoint(state,0,out var portalX,out var portalY,out _);
            var endAngle=Mathf.Atan2(portalY/state.minorRadius,portalX/state.majorRadius);
            var arc=Mathf.DeltaAngle(startAngle*Mathf.Rad2Deg,endAngle*Mathf.Rad2Deg)*Mathf.Deg2Rad;
            var radius=MiningRules.PitRadiusScale(state)+MiningRules.RoadWidth(state)/state.minorRadius+.03f;
            var start=PitDirection(deposit,state.majorRadius*radius*Mathf.Cos(startAngle),state.minorRadius*radius*Mathf.Sin(startAngle));
            var clear=Mathf.Clamp01((mine.FootprintRadius+3)/Mathf.Max(1,distance));
            var entrance=Vector3.Slerp(mineDirection,deposit.Direction,clear).normalized;
            for(var i=0;i<9;i++)directions.Add(Vector3.Slerp(entrance,start,i/8f).normalized);
            for(var i=1;i<=48;i++)
            {
                var a=startAngle+arc*i/48;
                directions.Add(PitDirection(deposit,state.majorRadius*radius*Mathf.Cos(a),state.minorRadius*radius*Mathf.Sin(a)));
            }
            var portal=PitDirection(deposit,portalX,portalY);
            var arcEnd=directions[directions.Count-1];
            for(var i=1;i<8;i++)directions.Add(Vector3.Slerp(arcEnd,portal,i/8f).normalized);
        }
        private void AddRoadPair(List<Vector3> vertices,Vector3 direction,Vector3 side,float left,float right,float lift)
        {
            foreach(var offset in new[]{left,right})
            {
                var n=(direction+side*offset/planet.Radius).normalized;
                vertices.Add(RoadSurface(n)+n*lift);
            }
        }
        private Transform CreatePitTruck(Transform parent)
        {
            var root=new GameObject("Mine haul truck").transform;root.SetParent(parent,false);
            TruckBox(root,"Chassis",new Vector3(0,.48f,0),new Vector3(1.25f,.26f,2.7f),tyreTrackMaterial);
            TruckBox(root,"Armoured cab",new Vector3(0,1,.87f),new Vector3(1.22f,.85f,.83f),haulerMaterial);
            TruckBox(root,"Tipper bed",new Vector3(0,.9f,-.45f),new Vector3(1.5f,.6f,1.7f),haulerMaterial);
            TruckBox(root,"Ore load",new Vector3(0,1.24f,-.45f),new Vector3(1.2f,.15f,1.4f),haulRoadMaterial);
            TruckBox(root,"Cab glazing",new Vector3(0,1.13f,1.3f),new Vector3(.98f,.29f,.04f),tyreTrackMaterial);
            for(var side=-1;side<=1;side+=2)for(var axle=-1;axle<=1;axle++)
            {
                var wheel=GameObject.CreatePrimitive(PrimitiveType.Cylinder);wheel.name="Haul truck wheel";
                Destroy(wheel.GetComponent<Collider>());wheel.transform.SetParent(root,false);
                wheel.transform.localPosition=new Vector3(side*.7f,.36f,axle*.91f);
                wheel.transform.localRotation=Quaternion.Euler(0,0,90);wheel.transform.localScale=new Vector3(.69f,.18f,.69f);
                wheel.GetComponent<Renderer>().sharedMaterial=tyreTrackMaterial;
            }
            return root;
        }
        private static void TruckBox(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;
            Destroy(go.GetComponent<Collider>());go.transform.SetParent(parent,false);go.transform.localPosition=position;
            go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=material;
        }
        private void AdvancePitHaulers(float dt)
        {
            foreach(var pair in pitHaulage)
            {
                var haul=pair.Value;
                if(haul.operating && !haul.flooded && pair.Key.Remaining>0)
                    for(var i=0;i<2;i++)haul.travelled[i]+=Mathf.Max(0,dt)*4;
                PositionPitTrucks(haul,MiningRules.RoadWidth(pair.Key.Excavation));
            }
        }
        private void PositionPitTrucks(PitHaulage haul,float width)
        {
            if(haul.path.Count<2)return;
            var length=haul.lengths[haul.lengths.Count-1];if(length<1)return;
            for(var truckIndex=0;truckIndex<haul.trucks.Count;truckIndex++)
            {
                var truck=haul.trucks[truckIndex];
                var scale=Mathf.Clamp(width*.28f,.35f,1.5f);
                // Separate outbound/return lanes and stagger trips along the same graded road.
                var cycle=Mathf.Repeat(haul.travelled[truckIndex],length*2);
                var returning=cycle>length;var distance=returning?length*2-cycle:cycle;
                var index=haul.lengths.BinarySearch(distance);if(index<0)index=~index;
                index=Mathf.Clamp(index,1,haul.path.Count-1);
                var t=Mathf.InverseLerp(haul.lengths[index-1],haul.lengths[index],distance);
                var centre=Vector3.Lerp(haul.path[index-1],haul.path[index],t);
                var forward=(haul.path[index]-haul.path[index-1]).normalized*(returning?-1:1);
                var side=Vector3.Cross(centre.normalized,forward).normalized;
                var n=(centre+side*width*.23f).normalized;
                truck.localPosition=RoadSurface(n)+n*.04f;
                truck.localRotation=Quaternion.LookRotation(forward,Vector3.Cross(forward,side).normalized);
                truck.localScale=Vector3.one*scale;
                var visible=!haul.flooded && (Camera.main==null || Vector3.Distance(Camera.main.transform.position,planet.transform.TransformPoint(centre))<2400);
                if(truck.gameObject.activeSelf!=visible)truck.gameObject.SetActive(visible);
            }
        }
        private void ClearPitRoads()
        {
            foreach(var haul in pitHaulage.Values)if(haul.mesh!=null)Destroy(haul.mesh);
            pitHaulage.Clear();haulRoadMaterial=null;tyreTrackMaterial=null;
        }
    }
}
