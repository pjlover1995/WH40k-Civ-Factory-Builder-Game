using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WH30K.Sim;

namespace WH30K.Planet
{
    public sealed class PlanetarySystem : MonoBehaviour
    {
        public const int CelestialLayer = 30;
        private LODPlanet planet;
        private Light sun;
        private Vector3 landingNormal, axis, initialSun;
        private Transform star, moon, satellite, gasGiant;
        private float lastOrbitSeconds = float.NaN;
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
            lastOrbitSeconds=float.NaN;
            UpdateOrbit();
        }
        private Transform Body(string name,float radius,Color color,bool luminous)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name=name;go.layer=CelestialLayer;Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform,false);go.transform.localScale=Vector3.one*radius*2;
            var material=new Material(luminous?Shader.Find("Unlit/Color"):Resources.Load<Shader>("WH30K/CelestialBody")) {color=color};
            materials.Add(material);go.GetComponent<Renderer>().sharedMaterial=material;
            go.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            return go.transform;
        }
        private void Update()
        {
            if(planet==null)return;
            UpdateOrbit();
        }
        private void UpdateOrbit()
        {
            var seconds=(float)ColonyClock.SimulationSeconds;
            if(seconds==lastOrbitSeconds)return;lastOrbitSeconds=seconds;
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
        private void OnDestroy() { foreach(var material in materials) Destroy(material); }
    }
}
