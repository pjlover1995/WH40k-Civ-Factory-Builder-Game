using UnityEngine;
using WH30K.Planet;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;

namespace WH30K.Gameplay.Construction
{
    public sealed class FarmTerritoryOutline : MonoBehaviour
    {
        private ManagedBuilding root;private Settlement settlement;private IndustrySystem industry;private Material material;
        private int revision=-1,surface=-1;
        public void Configure(ManagedBuilding farm,Settlement owner,IndustrySystem logistics)
        {root=farm;settlement=owner;industry=logistics;material=new Material(Shader.Find("Sprites/Default")){color=new Color(1,.79f,.20f,.8f)};Rebuild();}
        private void Update()
        {
            var planet=root!=null?root.GetComponentInParent<LODPlanet>():null;
            if(planet!=null && (revision!=settlement.ResidentialGeometryRevision || surface!=planet.SurfaceRevision))Rebuild();
        }
        public void Refresh() => Rebuild();
        private void Rebuild()
        {
            foreach(Transform child in transform)Destroy(child.gameObject);
            foreach(var site in industry.FarmSites(root))
            {
                var planet=site.GetComponentInParent<LODPlanet>();
                foreach(var polygon in settlement.FarmLand(site))
                {
                    var points=new System.Collections.Generic.List<Vector3>();
                    for(var edge=0;edge<polygon.Length;edge++)
                    {
                        var a=polygon[edge];var b=polygon[(edge+1)%polygon.Length];
                        var count=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z))*site.transform.lossyScale.x/3));
                        for(var step=0;step<count;step++)
                        {
                            var t=step/(float)count;var p=site.transform.TransformPoint(new Vector3(Mathf.Lerp(a.x,b.x,t),0,Mathf.Lerp(a.z,b.z,t)));
                            var n=planet.transform.InverseTransformPoint(p).normalized;
                            points.Add(planet.transform.TransformPoint(planet.EvaluateSurfacePoint(n)+n*.3f));
                        }
                    }
                    var go=new GameObject("Claimed farm territory",typeof(LineRenderer));go.transform.SetParent(transform,false);
                    var line=go.GetComponent<LineRenderer>();line.sharedMaterial=material;line.useWorldSpace=true;line.loop=true;line.widthMultiplier=.20f;
                    line.positionCount=points.Count;line.SetPositions(points.ToArray());
                }
                surface=planet.SurfaceRevision;
            }
            revision=settlement.ResidentialGeometryRevision;
        }
        private void OnDestroy(){if(material!=null)Destroy(material);}
    }
}
