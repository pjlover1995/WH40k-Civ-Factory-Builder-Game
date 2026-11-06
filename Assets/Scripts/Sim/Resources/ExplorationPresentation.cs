using System;
using System.Collections.Generic;
using UnityEngine;
using WH30K.Planet;

namespace WH30K.Sim.Resources
{
    public sealed partial class IndustrySystem
    {
        private struct SurveyCap { public Vector3 direction; public float radius; }
        private readonly List<SurveyCap> surveyCaps=new List<SurveyCap>();
        private readonly Mesh[] discoverySiteMeshes=new Mesh[4];
        private bool[] exploredCells=new bool[ExplorationRules.SurveyCellCount];
        private Vector3[] surveyCellDirections;
        private Color[] surveyColors;
        private int exploredCellCount;
        private Mesh surveyCoverageMesh;
        private MeshRenderer surveyCoverageRenderer;
        public float ExploredFraction=>exploredCellCount/(float)ExplorationRules.SurveyCellCount;

        private void PrepareSurveyCaps()
        {
            surveyCaps.Clear();
            foreach(var building in surveyors)
            {
                if(building==null||building.State.offline)continue;
                surveyCaps.Add(new SurveyCap
                {
                    direction=planet.transform.InverseTransformPoint(building.transform.position).normalized,
                    radius=building.SurveyRadius*(research!=null?research.SurveyRadiusMultiplier:1f)
                });
            }
        }

        private void ConfigureSurveyPresentation(IndustrySnapshot saved)
        {
            exploredCells=saved?.exploredCells!=null?(bool[])saved.exploredCells.Clone():new bool[ExplorationRules.SurveyCellCount];
            surveyCellDirections=new Vector3[exploredCells.Length];
            var vertices=new Vector3[exploredCells.Length*4];var triangles=new int[exploredCells.Length*6];
            surveyColors=new Color[vertices.Length];exploredCellCount=0;
            for(var i=0;i<exploredCells.Length;i++)
            {
                ExplorationRules.SurveyCellDirection(i,out var x,out var y,out var z);
                var n=new Vector3(x,y,z);surveyCellDirections[i]=n;
                var point=n*(Mathf.Max(planet.SeaLevel,planet.EvaluateSurfacePoint(n).magnitude)+18);
                var right=Vector3.Cross(n,Mathf.Abs(n.y)>.95f?Vector3.right:Vector3.up).normalized*planet.Radius*.006f;
                var forward=Vector3.Cross(n,right).normalized*right.magnitude;
                var v=i*4;vertices[v]=point-right-forward;vertices[v+1]=point-right+forward;
                vertices[v+2]=point+right-forward;vertices[v+3]=point+right+forward;
                var t=i*6;triangles[t]=v;triangles[t+1]=v+1;triangles[t+2]=v+2;
                triangles[t+3]=v+2;triangles[t+4]=v+1;triangles[t+5]=v+3;
                SetSurveyCellColor(i);if(exploredCells[i])exploredCellCount++;
            }
            var go=new GameObject("Regional survey coverage",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(visualRoot,false);
            surveyCoverageMesh=new Mesh{name="Regional survey markers"};surveyCoverageMesh.MarkDynamic();
            surveyCoverageMesh.vertices=vertices;surveyCoverageMesh.triangles=triangles;surveyCoverageMesh.colors=surveyColors;surveyCoverageMesh.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh=surveyCoverageMesh;surveyCoverageRenderer=go.GetComponent<MeshRenderer>();
            var material=new Material(Shader.Find("Sprites/Default"));materials.Add(material);
            surveyCoverageRenderer.sharedMaterial=material;surveyCoverageRenderer.enabled=false;
            foreach(var discovery in discoveries)BuildDiscoverySite(discovery);
        }

        private void SetSurveyCellColor(int cell)
        {
            var color=exploredCells[cell]?new Color(.15f,.8f,.7f,.7f):new Color(.38f,.42f,.47f,.3f);
            for(var vertex=0;vertex<4;vertex++)surveyColors[cell*4+vertex]=color;
        }

        private void UpdateSurveyCoverage()
        {
            if(surveyCellDirections==null||surveyCaps.Count==0)return;
            var changed=false;
            for(var i=0;i<exploredCells.Length;i++)
            {
                if(exploredCells[i]||!Surveyed(surveyCellDirections[i]))continue;
                exploredCells[i]=true;exploredCellCount++;SetSurveyCellColor(i);changed=true;
            }
            // A single color upload per survey pass; the terrain geometry remains cached.
            if(changed)surveyCoverageMesh.colors=surveyColors;
        }

        private void BuildDiscoverySite(SurfaceDiscovery discovery)
        {
            var kind=discovery.State.kind;
            if(discoverySiteMeshes[kind]==null)discoverySiteMeshes[kind]=CreateDiscoveryMesh((DiscoveryKind)kind);
            var go=new GameObject("Surface site - "+DiscoveryName((DiscoveryKind)kind),typeof(MeshFilter),typeof(MeshRenderer));
            go.transform.SetParent(visualRoot,false);
            go.transform.SetPositionAndRotation(discovery.Position,planet.transform.rotation*Quaternion.FromToRotation(Vector3.up,discovery.Direction));
            go.GetComponent<MeshFilter>().sharedMesh=discoverySiteMeshes[kind];
            go.GetComponent<MeshRenderer>().sharedMaterial=materials[IndustryRules.MaterialCount+kind];
            discovery.Site=go.transform;go.SetActive(false);
        }

        private static Mesh CreateDiscoveryMesh(DiscoveryKind kind)
        {
            var parts=new List<CombineInstance>();
            if(kind==DiscoveryKind.AncientRuins)
            {
                SitePart(parts,WorldArt.Cube,new Vector3(0,1,0),new Vector3(48,2,38));
                for(var x=-1;x<=1;x+=2)for(var z=-1;z<=1;z+=2)
                    SitePart(parts,WorldArt.Cube,new Vector3(x*18,10+z*2,z*13),new Vector3(5,20+z*4,5));
                SitePart(parts,WorldArt.Cube,new Vector3(0,22,-13),new Vector3(40,4,6));
            }
            else if(kind==DiscoveryKind.ImperialInstallation)
            {
                SitePart(parts,WorldArt.Cube,new Vector3(0,4,0),new Vector3(42,8,34));
                SitePart(parts,WorldArt.Cube,new Vector3(-13,16,-8),new Vector3(8,24,8));
                SitePart(parts,WorldArt.Cone,new Vector3(-13,28,-8),new Vector3(9,10,9));
                SitePart(parts,WorldArt.Cube,new Vector3(10,10,0),new Vector3(15,4,25));
            }
            else if(kind==DiscoveryKind.HazardousRegion)
            {
                for(var i=0;i<5;i++)
                {var angle=i*Mathf.PI*2/5;SitePart(parts,WorldArt.Cone,new Vector3(Mathf.Cos(angle)*16,0,Mathf.Sin(angle)*16),new Vector3(12,9+i*2,12));}
            }
            else
            {
                SitePart(parts,WorldArt.Cube,new Vector3(0,2,0),new Vector3(30,4,30));
                SitePart(parts,WorldArt.Octahedron,new Vector3(0,17,0),new Vector3(12,25,12));
                for(var x=-1;x<=1;x+=2)SitePart(parts,WorldArt.Cube,new Vector3(x*12,9,0),new Vector3(3,18,3));
            }
            var mesh=new Mesh{name="Discovery site - "+kind};mesh.CombineMeshes(parts.ToArray(),true,true);mesh.RecalculateBounds();return mesh;
        }
        private static void SitePart(List<CombineInstance> parts,Mesh mesh,Vector3 position,Vector3 scale)
            =>parts.Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.TRS(position,Quaternion.identity,scale)});

        private void ClearSurveyPresentation()
        {
            if(surveyCoverageMesh!=null)Destroy(surveyCoverageMesh);
            for(var i=0;i<discoverySiteMeshes.Length;i++){if(discoverySiteMeshes[i]!=null)Destroy(discoverySiteMeshes[i]);discoverySiteMeshes[i]=null;}
            surveyCoverageMesh=null;surveyCoverageRenderer=null;surveyCellDirections=null;surveyColors=null;
            exploredCells=new bool[ExplorationRules.SurveyCellCount];exploredCellCount=0;surveyCaps.Clear();
        }
    }
}
