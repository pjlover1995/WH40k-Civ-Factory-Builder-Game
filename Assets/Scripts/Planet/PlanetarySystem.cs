using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WH30K.Sim;

namespace WH30K.Planet
{
    public sealed class PlanetarySystem : MonoBehaviour
    {
        private LODPlanet planet;
        private Light sun;
        private Vector3 landingNormal, axis, initialSun;
        private Transform star, moon, satellite, gasGiant, localWater;
        private Vector3 lastWaterDirection;
        private Mesh waterMesh;
        private readonly List<Material> materials=new List<Material>();
        public Vector3 SunDirection { get; private set; }
        public void Configure(LODPlanet world)
        {
            planet=world;sun=RenderSettings.sun;
            star=Body("Sun - Helios",planet.Radius*.22f,new Color(1,.77f,.36f),true);
            moon=Body("Moon - Selene",planet.Radius*.19f,new Color(.58f,.59f,.56f),false);
            satellite=Body("Moon - Cinder",planet.Radius*.065f,new Color(.34f,.27f,.22f),false);
            gasGiant=Body("Outer world - Aurelia",planet.Radius*.32f,new Color(.65f,.43f,.26f),false);
            gasGiant.GetComponent<Renderer>().sharedMaterial.SetFloat("_Gas",1);
            SetLandingSite(Vector3.up);
        }
        public void SetLandingSite(Vector3 normal)
        {
            landingNormal=normal.normalized;
            var tangent=Vector3.Cross(normal,Mathf.Abs(normal.y)>.95f?Vector3.right:Vector3.up).normalized;
            initialSun=(normal-tangent*.9f).normalized;axis=Vector3.Cross(normal,tangent).normalized;
            UpdateOrbit();
        }
        private Transform Body(string name,float radius,Color color,bool luminous)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name=name;Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform,false);go.transform.localScale=Vector3.one*radius*2;
            var material=new Material(luminous?Shader.Find("Unlit/Color"):Resources.Load<Shader>("WH30K/CelestialBody")) {color=color};
            materials.Add(material);go.GetComponent<Renderer>().sharedMaterial=material;
            go.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            return go.transform;
        }
        private void Update() { if(planet!=null) { UpdateOrbit(); UpdateLocalWater(); } }
        private void UpdateOrbit()
        {
            var seconds=(float)ColonyClock.SimulationSeconds;
            Shader.SetGlobalFloat("_PlanetSimulationTime",seconds);
            SunDirection=Quaternion.AngleAxis((float)IndustryRules.DayAngle(seconds),axis)*initialSun;
            if(sun!=null) sun.transform.rotation=Quaternion.LookRotation(-SunDirection,axis);
            star.localPosition=SunDirection*planet.Radius*8;
            var axisA=Vector3.Cross(landingNormal,axis).normalized;var phase=seconds*Mathf.PI*2/900+.8f;
            moon.localPosition=(landingNormal*Mathf.Sin(phase)+axisA*Mathf.Cos(phase))*planet.Radius*2.6f;
            satellite.localPosition=(axis*Mathf.Sin(phase*2.7f)+axisA*Mathf.Cos(phase*2.7f))*planet.Radius*1.8f;
            gasGiant.localPosition=(axisA*.8f+axis*.5f-landingNormal*.3f).normalized*planet.Radius*7;
            var daylight=Mathf.Clamp01(Vector3.Dot(landingNormal,SunDirection)+.15f);
            RenderSettings.ambientSkyColor=Color.Lerp(new Color(.16f,.19f,.25f),new Color(.35f,.41f,.48f),daylight);
            RenderSettings.ambientEquatorColor=Color.Lerp(new Color(.11f,.13f,.17f),new Color(.25f,.27f,.29f),daylight);
        }
        private void UpdateLocalWater()
        {
            if(Camera.main==null) return;
            var position=transform.InverseTransformPoint(Camera.main.transform.position);var n=position.normalized;
            var nearby=position.magnitude-planet.Radius<1800 && planet.EvaluateSurfacePoint(n).magnitude<planet.SeaLevel+35;
            if(localWater!=null) localWater.gameObject.SetActive(nearby);
            if(!nearby || (localWater!=null && Vector3.Distance(lastWaterDirection,n)*planet.Radius<180)) return;
            lastWaterDirection=n;
            if(localWater==null)
            {
                var go=new GameObject("Detailed wave surface",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(transform,false);localWater=go.transform;
                waterMesh=new Mesh {name="Local spherical water",indexFormat=IndexFormat.UInt32};
                go.GetComponent<MeshFilter>().sharedMesh=waterMesh;go.GetComponent<MeshRenderer>().sharedMaterial=transform.Find("Ocean").GetComponent<Renderer>().sharedMaterial;
                go.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            }
            const int size=80;var vertices=new Vector3[(size+1)*(size+1)];var normals=new Vector3[vertices.Length];var colors=new Color[vertices.Length];
            var triangles=new int[size*size*6];var right=Vector3.Cross(n,Mathf.Abs(n.y)>.95f?Vector3.right:Vector3.up).normalized;var forward=Vector3.Cross(n,right);
            for(var y=0;y<=size;y++) for(var x=0;x<=size;x++)
            {var i=y*(size+1)+x;var d=(n+(right*(x-size*.5f)+forward*(y-size*.5f))*10/planet.Radius).normalized;
                vertices[i]=d*(planet.SeaLevel+.08f);normals[i]=d;var depth=Mathf.Clamp01((planet.SeaLevel-planet.EvaluateSurfacePoint(d).magnitude)/110);colors[i]=new Color(depth,depth,depth,1);}
            var t=0;for(var y=0;y<size;y++) for(var x=0;x<size;x++)
            {var i=y*(size+1)+x;triangles[t++]=i;triangles[t++]=i+size+2;triangles[t++]=i+size+1;triangles[t++]=i;triangles[t++]=i+1;triangles[t++]=i+size+2;}
            waterMesh.Clear();waterMesh.vertices=vertices;waterMesh.normals=normals;waterMesh.colors=colors;waterMesh.triangles=triangles;waterMesh.RecalculateBounds();
        }
        private void OnDestroy() { if(waterMesh!=null) Destroy(waterMesh);foreach(var material in materials) Destroy(material); }
    }
}
