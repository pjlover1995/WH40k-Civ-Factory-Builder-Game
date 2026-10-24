using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WH30K.Gameplay.Construction;

namespace WH30K.Planet
{
    // Deterministic local vegetation tiles, batched into a few meshes rather than thousands of objects.
    public sealed class PlanetEcology : MonoBehaviour
    {
        private LODPlanet planet;
        private Vector3 lastDirection;
        private float timer;
        private Transform root;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private Material[] materials;
        private int lastBuildingCount;
        private int lastSurfaceRevision;
        public void Configure(LODPlanet world)
        {
            planet = world;
            materials = new[] { Surface("Bark", new Color(.19f,.14f,.09f)), Surface("Canopy", new Color(.12f,.26f,.12f)),
                Surface("Weathered outcrop",new Color(.35f,.34f,.30f)), Surface("Wetland pool",new Color(.08f,.17f,.13f)) };
            materials[3].SetFloat("_Glossiness", .88f);
        }
        private static Material Surface(string name, Color color) => new Material(Shader.Find("Standard")) { name = name, color = color };
        private void Update()
        {
            if (planet == null || Camera.main == null) return;
            timer += Time.unscaledDeltaTime; if (timer < 1) return; timer = 0;
            var cameraPosition = planet.transform.InverseTransformPoint(Camera.main.transform.position);
            var direction = cameraPosition.normalized;
            if (cameraPosition.magnitude - planet.EvaluateSurfacePoint(direction).magnitude > 2400)
            { if (root != null) root.gameObject.SetActive(false); return; }
            if (root != null) root.gameObject.SetActive(true);
            var count = FindObjectsOfType<ManagedBuilding>().Length;
            if (root == null || Vector3.Distance(lastDirection, direction) * planet.Radius > 170 || count != lastBuildingCount || lastSurfaceRevision != planet.SurfaceRevision)
            { lastDirection = direction; lastBuildingCount = count; lastSurfaceRevision = planet.SurfaceRevision; Generate(direction); }
        }
        private void Generate(Vector3 direction)
        {
            if (root != null) Destroy(root.gameObject);
            foreach (var mesh in meshes) Destroy(mesh); meshes.Clear();
            root = new GameObject("Local forests, reeds and outcrops").transform; root.SetParent(transform, false);
            var groups = new List<CombineInstance>[4]; for(var i=0;i<4;i++) groups[i]=new List<CombineInstance>();
            var grid = new PlanetGrid(planet, 1024); var cell = grid.GetCellFromDirection(direction);
            var buildings = FindObjectsOfType<ManagedBuilding>();
            for(var y=-20;y<=20;y++) for(var x=-20;x<=20;x++)
            {
                var sample = grid.OffsetCell(cell,x,y); var n=grid.GetDirection(sample);
                var hash = unchecked(planet.Seed ^ sample.X*73856093 ^ sample.Y*19349663 ^ sample.Face*83492791);
                var random = new System.Random(hash);
                var east=Vector3.Cross(n,Mathf.Abs(n.y)>.95f?Vector3.right:Vector3.up).normalized;var north=Vector3.Cross(n,east);
                n=(n+(east*((float)random.NextDouble()-.5f)+north*((float)random.NextDouble()-.5f))*18/planet.Radius).normalized;
                var position=planet.EvaluateSurfacePoint(n);
                if(planet.EvaluateBaseHeight(n)-position.magnitude>.5f) continue;
                if(position.magnitude<planet.SeaLevel+2) continue;
                var occupied=false; foreach(var building in buildings)
                    if(Vector3.Distance(building.transform.position,transform.TransformPoint(position))<26) {occupied=true;break;}
                if(occupied) continue;
                var biome=planet.Biome(n); var rotation=Quaternion.FromToRotation(Vector3.up,n)*Quaternion.Euler(0,(float)random.NextDouble()*360,0);
                var chance=random.NextDouble(); var height=7+(float)random.NextDouble()*10;
                if((biome=="Forest" && chance<.8) || (biome=="Grassland" && chance<.12) || (biome=="Swamp" && chance<.24))
                {
                    Add(groups[0],WorldArt.Cube,position+n*height*.4f,rotation,new Vector3(1,height*.8f,1));
                    if(biome=="Swamp" || (biome=="Forest" && chance<.4))
                    {
                        for(var crown=0;crown<3;crown++)
                            Add(groups[1],WorldArt.Octahedron,position+n*height*.8f+rotation*new Vector3((crown-1)*height*.2f,crown%2*height*.18f,0),rotation,
                                new Vector3(height*.7f,height*.65f,height*.7f));
                    }
                    else for(var level=0;level<3;level++)
                            Add(groups[1],WorldArt.Cone,position+n*(height*.45f+level*height*.19f),rotation,
                                new Vector3(height*(.56f-level*.1f),height*.52f,height*(.56f-level*.1f)));
                }
                else if(biome=="Swamp" && chance<.82)
                {
                    for(var reed=0;reed<4;reed++) Add(groups[1],WorldArt.Cube,
                        position+rotation*new Vector3(reed*.6f,1.5f,reed%2),rotation,new Vector3(.18f,3+reed*.5f,.18f));
                    if(chance<.4) Add(groups[3],WorldArt.Octahedron,position,rotation,new Vector3(12,.35f,9));
                }
                else if(chance<.13 || (biome=="Mountain range" && chance<.7))
                    Add(groups[2],WorldArt.Octahedron,position+n*1.4f,rotation,new Vector3(4+(float)chance*9,4+(float)chance*5,5));
            }
            for(var i=0;i<groups.Length;i++)
            {
                if(groups[i].Count==0) continue;
                var mesh=new Mesh { name="Batched ecology",indexFormat=IndexFormat.UInt32 };
                mesh.CombineMeshes(groups[i].ToArray(),true,true); meshes.Add(mesh);
                var go=new GameObject(materials[i].name,typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(root,false);
                go.GetComponent<MeshFilter>().sharedMesh=mesh; go.GetComponent<MeshRenderer>().sharedMaterial=materials[i];
            }
        }
        private static void Add(List<CombineInstance> group,Mesh mesh,Vector3 p,Quaternion q,Vector3 scale)
            => group.Add(new CombineInstance { mesh=mesh,transform=Matrix4x4.TRS(p,q,scale) });
        private void OnDestroy()
        { foreach(var mesh in meshes) if(mesh!=null) Destroy(mesh); if(materials!=null) foreach(var mat in materials) Destroy(mat); }
    }

    public static class WorldArt
    {
        private static Mesh cube,cone,octahedron,hauler;
        public static Mesh Cube { get { if(cube==null) {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);cube=go.GetComponent<MeshFilter>().sharedMesh;Object.Destroy(go);} return cube; } }
        public static Mesh Cone { get { if(cone==null) cone=MakeCone();return cone; } }
        public static Mesh Octahedron { get { if(octahedron==null) octahedron=MakeOctahedron();return octahedron; } }
        public static Mesh Hauler
        {
            get
            {
                if(hauler!=null)return hauler;
                var parts=new List<CombineInstance>();
                parts.Add(new CombineInstance{mesh=Cube,transform=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(1.5f,.6f,3))});
                parts.Add(new CombineInstance{mesh=Cube,transform=Matrix4x4.TRS(new Vector3(0,.35f,1.1f),Quaternion.identity,new Vector3(1,.7f,1))});
                parts.Add(new CombineInstance{mesh=Cube,transform=Matrix4x4.TRS(new Vector3(0,0,-.2f),Quaternion.identity,new Vector3(4,.2f,.8f))});
                for(var side=-1;side<=1;side+=2)parts.Add(new CombineInstance{mesh=Cube,transform=Matrix4x4.TRS(new Vector3(side*1.5f,0,-.5f),Quaternion.identity,new Vector3(.5f,.5f,1.6f))});
                parts.Add(new CombineInstance{mesh=Cube,transform=Matrix4x4.TRS(new Vector3(0,.5f,-.7f),Quaternion.identity,new Vector3(1.3f,.7f,1.3f))});
                hauler=new Mesh{name="Twin-engine cargo hauler"};hauler.CombineMeshes(parts.ToArray(),true,true);return hauler;
            }
        }
        private static Mesh MakeCone()
        {
            var vertices=new List<Vector3>(); var triangles=new List<int>();
            for(var i=0;i<8;i++)
            {
                var a=i*Mathf.PI/4;var b=(i+1)*Mathf.PI/4;
                Triangle(vertices,triangles,Vector3.up,new Vector3(Mathf.Sin(a)*.5f,0,Mathf.Cos(a)*.5f),new Vector3(Mathf.Sin(b)*.5f,0,Mathf.Cos(b)*.5f));
            }
            return Mesh(vertices,triangles);
        }
        private static Mesh MakeOctahedron()
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            var ring=new[]{Vector3.forward*.5f,Vector3.right*.5f,Vector3.back*.5f,Vector3.left*.5f};
            for(var i=0;i<4;i++) { Triangle(vertices,triangles,Vector3.up*.5f,ring[i],ring[(i+1)%4]);Triangle(vertices,triangles,Vector3.down*.5f,ring[(i+1)%4],ring[i]); }
            return Mesh(vertices,triangles);
        }
        private static void Triangle(List<Vector3> vertices,List<int> indices,Vector3 a,Vector3 b,Vector3 c)
        { var i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);indices.Add(i);indices.Add(i+1);indices.Add(i+2); }
        private static Mesh Mesh(List<Vector3> vertices,List<int> indices)
        { var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh; }
    }
}
