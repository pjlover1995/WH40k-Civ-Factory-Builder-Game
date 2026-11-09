using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using WH30K.Gameplay.Construction;
using WH30K.Sim.Settlements;

namespace WH30K.Planet
{
    // Deterministic local vegetation; overlapping tiles reuse trees, and retired tiles release them.
    public sealed class PlanetEcology : MonoBehaviour
    {
        private LODPlanet planet;
        private Vector3 lastDirection;
        private float timer;
        private Transform root;
        private Transform treeRoot;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly Dictionary<PlanetGridCell,TreeVisual> treeInstances=new Dictionary<PlanetGridCell,TreeVisual>();
        private readonly List<PlanetGridCell> retiredTrees=new List<PlanetGridCell>();
        public int ResidentTreeCount => treeInstances.Count;
        private Camera viewCamera;
        private EarthworksController earthworks;
        private Material[] materials;
        private Material[] treeMaterials;
        private BuildingPlacementController placement;
        private Settlement settlement;
        private ManagedBuilding[] lastBuildings;
        private IEnumerator generation;
        private int lastSurfaceRevision;
        private int lastGrowthYear=-1;
        private int lastTreeStride;
        public void Configure(LODPlanet world)
        {
            planet = world;
            placement=FindObjectOfType<BuildingPlacementController>();settlement=FindObjectOfType<Settlement>();
            viewCamera=Camera.main;earthworks=placement!=null?placement.GetComponent<EarthworksController>():null;
            materials = new[] { Surface("Bark", new Color(.19f,.14f,.09f)), Surface("Canopy", new Color(.12f,.26f,.12f)),
                Surface("Weathered outcrop",new Color(.35f,.34f,.30f)), Surface("Wetland pool",new Color(.08f,.17f,.13f)) };
            materials[3].SetFloat("_Glossiness", .88f);
            treeMaterials=new[]{TreeMaterial("TemperateBroadleaf"),TreeMaterial("Conifer"),TreeMaterial("WetlandCypress")};
        }
        private static Material Surface(string name, Color color) => new Material(Shader.Find("Standard")) { name = name, color = color };
        private static Material TreeMaterial(string assetName)
        {
            var reference=Resources.Load<Material>("WH30K/RuntimeShaders/StandardCutout");
            var material=reference!=null?new Material(reference):new Material(Shader.Find("Standard"));
            material.name=assetName+" photographic foliage";material.enableInstancing=true;
            material.mainTexture=Resources.Load<Texture2D>("Ecology/"+assetName);
            material.SetFloat("_Mode",1);material.SetFloat("_Cutoff",.22f);material.SetFloat("_SrcBlend",(float)BlendMode.One);
            material.SetFloat("_DstBlend",(float)BlendMode.Zero);material.SetFloat("_ZWrite",1);
            material.SetOverrideTag("RenderType","TransparentCutout");
            material.EnableKeyword("_ALPHATEST_ON");material.DisableKeyword("_ALPHABLEND_ON");material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue=(int)RenderQueue.AlphaTest;
            return material;
        }
        private void Update()
        {
            if (planet == null || viewCamera == null) return;
            if(generation!=null){if(!generation.MoveNext())generation=null;return;}
            timer += Time.unscaledDeltaTime; if (timer < 1) return; timer = 0;
            var cameraPosition = planet.transform.InverseTransformPoint(viewCamera.transform.position);
            var direction = cameraPosition.normalized;
            var orbit = viewCamera.GetComponent<WH30K.CameraSystem.SimpleOrbitCamera>();
            if (orbit != null && orbit.IsSurfaceView) direction = orbit.NavigationDirection;
            var viewDistance = Vector3.Distance(cameraPosition, planet.EvaluateSurfacePoint(direction));
            var treeStride = Mathf.Clamp(Mathf.CeilToInt(viewDistance / 700f), 1, 8);
            if (cameraPosition.magnitude - planet.EvaluateSurfacePoint(direction).magnitude > 12000)
            { if (root != null) root.gameObject.SetActive(false);if(treeRoot!=null)treeRoot.gameObject.SetActive(false);return; }
            if (root != null) root.gameObject.SetActive(true);if(treeRoot!=null)treeRoot.gameObject.SetActive(true);
            var buildings=placement!=null?placement.Buildings:null;
            var growthYear=(int)(WH30K.Sim.ColonyClock.SimulationSeconds/WH30K.Sim.ColonyCalendar.SecondsPerYear);
            if(growthYear!=lastGrowthYear)UpdateTreeGrowth(growthYear);
            if (root == null || treeStride != lastTreeStride || Vector3.Distance(lastDirection, direction) * planet.Radius > 170 || !ReferenceEquals(buildings,lastBuildings) || lastSurfaceRevision != planet.SurfaceRevision || growthYear!=lastGrowthYear)
            { lastDirection = direction; lastTreeStride = treeStride; lastBuildings = buildings; lastSurfaceRevision = planet.SurfaceRevision;lastGrowthYear=growthYear; generation=Generate(direction); }
        }
        private IEnumerator Generate(Vector3 direction)
        {
            var groups = new List<CombineInstance>[4]; for(var i=0;i<4;i++) groups[i]=new List<CombineInstance>();
            var grid = new PlanetGrid(planet, WH30K.Sim.TreeEcologyRules.CellResolution(planet.Radius)); var cell = grid.GetCellFromDirection(direction);
            if(treeRoot==null){treeRoot=new GameObject("Placed photographic trees").transform;treeRoot.SetParent(transform,false);}
            var visibleTrees=new HashSet<PlanetGridCell>();
            var buildings=settlement!=null?settlement.AllBuildings:null;
            const int tileRadius=WH30K.Sim.TreeEcologyRules.TileRadius;
            for(var y=-tileRadius;y<=tileRadius;y++)
            {
            for(var x=-tileRadius;x<=tileRadius;x++)
            {
                // A sparse distant set covers the full colony without increasing the object cap.
                var sample = grid.OffsetCell(cell,x*lastTreeStride,y*lastTreeStride); var n=grid.GetDirection(sample);
                // The grid address, rather than the camera tile, is the permanent identity
                // of this patch of ecology. Rebuilding a nearby mesh therefore reproduces
                // exactly the same trees at exactly the same world coordinates.
                var hash = WH30K.Sim.TreeEcologyRules.StableHash(planet.Seed, sample.Face, sample.X, sample.Y);
                var random = new System.Random(hash);
                var east=Vector3.Cross(n,Mathf.Abs(n.y)>.95f?Vector3.right:Vector3.up).normalized;var north=Vector3.Cross(n,east);
                n=(n+(east*((float)random.NextDouble()-.5f)+north*((float)random.NextDouble()-.5f))*18/planet.Radius).normalized;
                var position=planet.EvaluateSurfacePoint(n);
                if (earthworks != null && earthworks.IsWorked(n)) continue;
                if(planet.EvaluateBaseHeight(n)-position.magnitude>.5f) continue;
                if(position.magnitude<planet.SeaLevel+2) continue;
                var occupied=false;
                if(buildings!=null)foreach(var building in buildings)
                {
                    var worldPosition=transform.TransformPoint(position);
                    var clearance=building.Id=="ColonyShip"?building.ShipFootprintDistance(worldPosition)
                        : Vector3.Distance(building.transform.position,worldPosition)-building.ReservedFootprintRadius;
                    if(clearance<4) {occupied=true;break;}
                }
                if(occupied) continue;
                var biome=planet.Biome(n,position.magnitude); var rotation=Quaternion.FromToRotation(Vector3.up,n)*Quaternion.Euler(0,(float)random.NextDouble()*360,0);
                var chance=random.NextDouble();
                if((biome=="Forest" && chance<.8) || (biome=="Grassland" && chance<.12) || (biome=="Swamp" && chance<.24))
                {
                    var maturity=WH30K.Sim.TreeEcologyRules.Maturity(planet.Seed,sample.Face,sample.X,sample.Y,WH30K.Sim.ColonyClock.SimulationSeconds);
                    var species=biome=="Swamp"?2:biome=="Forest"&&chance>=.4?1:0;
                    visibleTrees.Add(sample);
                    PlaceTree(sample,sample.Face,sample.X,sample.Y,species,position,rotation,
                        7+(float)random.NextDouble()*10,maturity);
                }
                else if(biome=="Swamp" && chance<.82)
                {
                    for(var reed=0;reed<4;reed++) Add(groups[1],WorldArt.Cube,
                        position+rotation*new Vector3(reed*.6f,1.5f,reed%2),rotation,new Vector3(.18f,3+reed*.5f,.18f));
                    if(chance<.4) Add(groups[3],WorldArt.Octahedron,position,rotation,new Vector3(12,.35f,9));
                }
                else if(chance<.07 || ((biome=="Mountain range" || biome=="Rocky highlands") && chance<.7))
                    Add(groups[2],WorldArt.Octahedron,position+n*1.4f,rotation,new Vector3(4+(float)chance*9,4+(float)chance*5,5));
            }
            if((y+20)%4==3)yield return null;
            }
            // Commit once per tile, keeping the previous forest visible while rows are generated.
            // Never retain every tree visited during a long surface traversal.
            retiredTrees.Clear();
            foreach(var pair in treeInstances)if(!visibleTrees.Contains(pair.Key))
            { pair.Value.objectRoot.gameObject.SetActive(false);Destroy(pair.Value.objectRoot.gameObject);retiredTrees.Add(pair.Key); }
            foreach(var key in retiredTrees)treeInstances.Remove(key);
            // Keep the previous tile visible until its replacement is complete.
            if (root != null) Destroy(root.gameObject);
            foreach (var mesh in meshes) Destroy(mesh); meshes.Clear();
            root = new GameObject("Local forests, reeds and outcrops").transform; root.SetParent(transform, false);
            for(var i=0;i<groups.Length;i++)
            {
                if(groups[i].Count==0) continue;
                var mesh=new Mesh { name="Batched ecology",indexFormat=IndexFormat.UInt32 };
                mesh.CombineMeshes(groups[i].ToArray(),true,true); meshes.Add(mesh);
                var go=new GameObject(materials[i].name,typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(root,false);
                go.GetComponent<MeshFilter>().sharedMesh=mesh; go.GetComponent<MeshRenderer>().sharedMaterial=materials[i];
            }
        }
        private void PlaceTree(PlanetGridCell id,int face,int x,int y,int species,Vector3 position,Quaternion rotation,float matureHeight,float maturity)
        {
            if(!treeInstances.TryGetValue(id,out var tree))
            {
                var go=new GameObject("Placed tree / "+new[]{"broadleaf","conifer","wetland cypress"}[species],typeof(MeshFilter),typeof(MeshRenderer));
                go.transform.SetParent(treeRoot,false);go.GetComponent<MeshFilter>().sharedMesh=WorldArt.TreeCard;
                go.GetComponent<MeshRenderer>().sharedMaterial=treeMaterials[species];tree=new TreeVisual{objectRoot=go.transform,face=face,x=x,y=y,species=species};treeInstances[id]=tree;
            }
            tree.matureHeight=matureHeight;tree.objectRoot.SetPositionAndRotation(transform.TransformPoint(position),transform.rotation*rotation);
            ApplyGrowth(tree,maturity);tree.objectRoot.gameObject.SetActive(true);
        }
        private void UpdateTreeGrowth(int year)
        {lastGrowthYear=year;foreach(var tree in treeInstances.Values)ApplyGrowth(tree,WH30K.Sim.TreeEcologyRules.Maturity(planet.Seed,tree.face,tree.x,tree.y,WH30K.Sim.ColonyClock.SimulationSeconds));}
        private static void ApplyGrowth(TreeVisual tree,float maturity)
        {var height=tree.matureHeight*(.42f+.58f*maturity);var width=height*(tree.species==1?.58f:tree.species==2?.86f:.82f);tree.objectRoot.localScale=new Vector3(width,height,width);}
        private sealed class TreeVisual{public Transform objectRoot;public int face,x,y,species;public float matureHeight;}
        private static void Add(List<CombineInstance> group,Mesh mesh,Vector3 p,Quaternion q,Vector3 scale)
            => group.Add(new CombineInstance { mesh=mesh,transform=Matrix4x4.TRS(p,q,scale) });
        private void OnDestroy()
        { foreach(var mesh in meshes) if(mesh!=null) Destroy(mesh); if(materials!=null) foreach(var mat in materials) if(mat!=null)Destroy(mat);if(treeMaterials!=null)foreach(var mat in treeMaterials)if(mat!=null)Destroy(mat); }
    }

    public static class WorldArt
    {
        private static Mesh cube,cone,octahedron,hauler,cylinder,sphere,treeCard;
        public static Mesh Cube { get { if(cube==null) {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);cube=go.GetComponent<MeshFilter>().sharedMesh;Object.Destroy(go);} return cube; } }
        public static Mesh Cone { get { if(cone==null) cone=MakeCone();return cone; } }
        public static Mesh Cylinder { get { if(cylinder==null) {var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);cylinder=go.GetComponent<MeshFilter>().sharedMesh;Object.Destroy(go);} return cylinder; } }
        public static Mesh Sphere { get { if(sphere==null) {var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere=go.GetComponent<MeshFilter>().sharedMesh;Object.Destroy(go);} return sphere; } }
        public static Mesh TreeCard { get { if(treeCard==null)treeCard=MakeTreeCard();return treeCard; } }
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
        private static Mesh MakeTreeCard()
        {
            var vertices=new[]{new Vector3(-.5f,0,0),new Vector3(.5f,0,0),new Vector3(.5f,1,0),new Vector3(-.5f,1,0),
                new Vector3(0,0,-.5f),new Vector3(0,0,.5f),new Vector3(0,1,.5f),new Vector3(0,1,-.5f)};
            var triangles=new[]{0,2,1,0,3,2,4,6,5,4,7,6};var uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up,Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
            // Standard always culls back faces. Duplicate reversed faces with their own
            // normals so the foliage remains visible and lit from every viewing direction.
            var doubleVertices=new Vector3[vertices.Length*2];var doubleUv=new Vector2[uv.Length*2];
            var doubleTriangles=new int[triangles.Length*2];
            for(var i=0;i<vertices.Length;i++){doubleVertices[i]=doubleVertices[i+vertices.Length]=vertices[i];doubleUv[i]=doubleUv[i+uv.Length]=uv[i];}
            for(var i=0;i<triangles.Length;i+=3)
            {doubleTriangles[i]=triangles[i];doubleTriangles[i+1]=triangles[i+1];doubleTriangles[i+2]=triangles[i+2];
                doubleTriangles[triangles.Length+i]=triangles[i]+vertices.Length;doubleTriangles[triangles.Length+i+1]=triangles[i+2]+vertices.Length;doubleTriangles[triangles.Length+i+2]=triangles[i+1]+vertices.Length;}
            var mesh=new Mesh{name="Crossed photographic tree cards"};mesh.vertices=doubleVertices;mesh.triangles=doubleTriangles;mesh.uv=doubleUv;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static void Triangle(List<Vector3> vertices,List<int> indices,Vector3 a,Vector3 b,Vector3 c)
        { var i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);indices.Add(i);indices.Add(i+1);indices.Add(i+2); }
        private static Mesh Mesh(List<Vector3> vertices,List<int> indices)
        { var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh; }
    }
}
