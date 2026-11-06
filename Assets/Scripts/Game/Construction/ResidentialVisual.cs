using System.Collections.Generic;
using UnityEngine;
using WH30K.Sim;

namespace WH30K.Gameplay.Construction
{
    /// <summary>Shared module meshes; roofs are regenerated only when the layout changes.</summary>
    public sealed class ResidentialVisual : MonoBehaviour
    {
        [SerializeField] private ResidentialDevelopment development;
        private GameObject architecture,selection,construction;
        private string selectionSignature,pendingSignature;
        private readonly Dictionary<int,GameObject> completedObjects=new Dictionary<int,GameObject>();
        private readonly Dictionary<int,string> completedSignatures=new Dictionary<int,string>();
        private readonly HashSet<(int,int,int)> floorStarts=new HashSet<(int,int,int)>();
        private LineRenderer workPlane;
        private Bounds workBounds;
        private readonly Vector3[] workCorners=new Vector3[4];
        public int ModuleObjectsBuilt {get;private set;}
        private static readonly Dictionary<string,Mesh> meshes=new Dictionary<string,Mesh>();
        private static bool recoveredMeshes;
        private static readonly Dictionary<string,Material[]> palettes=new Dictionary<string,Material[]>();
        private const string MeshPrefix="WH30K Residential / ";
        private static Material boundaryMaterial,ghostMaterial,preferredMaterial,waitingMaterial,constructionMaterial;
        public void Configure(ResidentialDevelopment owner){development=owner;Rebuild(true);}
        private void Remove(ref GameObject go){if(go!=null){go.SetActive(false);Destroy(go);}go=null;}
        public void Rebuild(bool force=false)
        {
            if(force || architecture==null)
            {
                // Domain reload and restore rebuild derived objects once, preserving all saved placements.
                for(var i=transform.childCount-1;i>=0;i--)
                    if(transform.GetChild(i).name=="Residential architecture" || transform.GetChild(i).name=="Residential selection")
                    {var old=transform.GetChild(i).gameObject;old.SetActive(false);Destroy(old);}
                architecture=new GameObject("Residential architecture");architecture.transform.SetParent(transform,false);
                completedObjects.Clear();completedSignatures.Clear();selection=null;selectionSignature=null;
                construction=null;pendingSignature=null;workPlane=null;
            }
            var state=development.Data;var lib=ResidentialDevelopment.Library;
            floorStarts.Clear();
            foreach(var m in state.modules)foreach(var c in ResidentialRules.BodyCells(m,lib))floorStarts.Add((c.x,c.z,c.bottom));
            foreach(var m in state.modules)
            {
                var covered=Coverage(m);var covers=new List<string>(covered);covers.Sort(System.StringComparer.Ordinal);
                var signature=ResidentialRules.Key(m)+":"+string.Join(",",covers)+":"+(m.floor==0?development.SurfaceRevision:0);
                if(completedSignatures.TryGetValue(m.serial,out var previous) && previous==signature
                    && completedObjects.TryGetValue(m.serial,out var existing) && existing!=null)continue;
                if(completedObjects.TryGetValue(m.serial,out var old))Remove(ref old);
                completedObjects[m.serial]=BuildModule(m,false,covered);completedSignatures[m.serial]=signature;
            }
            var next=state.project!=null?ResidentialRules.Key(state.project.placement)+":"+development.SurfaceRevision:null;
            if(next!=pendingSignature)
            {
                Remove(ref construction);workPlane=null;pendingSignature=next;
                if(state.project!=null)construction=BuildModule(state.project.placement,true,Coverage(state.project.placement));
            }
            GetComponent<MeshRenderer>().enabled=false;
            var collider=GetComponent<BoxCollider>();if(collider!=null)collider.enabled=false;
            UpdateConstruction();RefreshSelection();
        }
        private HashSet<string> Coverage(HabPlacement placement)
        {
            var covered=new HashSet<string>();
            foreach(var c in ResidentialRules.BodyCells(placement,ResidentialDevelopment.Library))
                if(floorStarts.Contains((c.x,c.z,c.top)))covered.Add((c.x-placement.x)+":"+(c.z-placement.z));
            return covered;
        }
        private GameObject BuildModule(HabPlacement placement,bool pending,HashSet<string> covered)
        {
            ModuleObjectsBuilt++;
            var lib=ResidentialDevelopment.Library;var d=lib.Find(placement.design);
            var go=new GameObject(d.name+(pending?" (construction)":""),typeof(MeshFilter),typeof(MeshRenderer));
            go.transform.SetParent(architecture.transform,false);
            go.transform.localPosition=new Vector3(placement.x*.2f,placement.floor*.12f,placement.z*.2f);
            // Geometry lives in block coordinates so neighbouring roofs and doors are resolved consistently.
            var cells=ResidentialRules.BodyCells(placement,lib);
            for(var i=0;i<cells.Count;i++) {var c=cells[i];cells[i]=new HabCell(c.x-placement.x,c.z-placement.z,c.bottom-placement.floor,c.top-placement.floor);}
            // Normalize coverage into design-relative coordinates for mesh sharing across blocks/placements.
            var mesh=ModuleMesh(d,cells,covered,placement.rotation,out var owned);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            if(owned)go.AddComponent<ResidentialOwnedMesh>();
            go.GetComponent<MeshRenderer>().sharedMaterials=pending?Repeat(Material(ref constructionMaterial,new Color(1,.63f,.16f,.5f))):DesignMaterials(d,placement.serial%3);
            if(!pending)
            {
                var selector=go.AddComponent<ResidentialModuleSelection>();selector.Block=GetComponent<ManagedBuilding>();selector.Serial=placement.serial;
                // Exact per-column picking; empty courts never select a phantom bounding box.
                foreach(var c in cells){var col=go.AddComponent<BoxCollider>();var bounds=ResidentialDevelopment.CellBounds(c);col.center=bounds.center;col.size=bounds.size;}
                if(placement.floor==0)AddFoundations(go,cells,placement);
            }
            if(placement.linkLength>0)BuildGallery(go,placement,pending);
            return go;
        }
        public static Material[] DesignMaterials(HabDesign design,int variation=0)
        {
            var key="photo-v1:"+design.id+":"+design.wallColor+":"+design.roofColor+":"+design.trimColor+":"+variation;
            if(palettes.TryGetValue(key,out var found) && found[0]!=null)return found;
            var materials=(Material[])BuildingVisuals.ArchitectureMaterials.Clone();
            var colors=new[]{design.wallColor,null,design.roofColor,design.trimColor};
            for(var i=0;i<4;i++)if(colors[i]!=null && ColorUtility.TryParseHtmlString(colors[i],out var color))
            {
                Color.RGBToHSV(color,out var hue,out var saturation,out var value);
                color=Color.HSVToRGB(Mathf.Repeat(hue+(variation-1)*.025f,1),saturation,Mathf.Clamp01(value+(variation-1)*.06f));
                var name="WH30K Residential palette / "+key+" / "+i;
                Material material=null;
                foreach(var existing in UnityEngine.Resources.FindObjectsOfTypeAll<Material>())if(existing.name==name){material=existing;break;}
                if(material==null)material=new Material(materials[i]){name=name,color=color,enableInstancing=true};
                material.SetFloat("_TintStrength", .62f);
                materials[i]=material;
            }
            palettes[key]=materials;return materials;
        }
        public static Mesh PreviewCoreMesh => ModuleMesh(ResidentialRules.Core,
            ResidentialRules.Cells(new HabPlacement{design="res-core"},ResidentialDevelopment.Library),new HashSet<string>(),0,out _);
        public static Mesh DesignMesh(HabDesign design)=>ModuleMesh(design,
            ResidentialRules.Cells(new HabPlacement{design=design.id},ResidentialDevelopment.Library),new HashSet<string>(),0,out _);
        private static Mesh ModuleMesh(HabDesign d,List<HabCell> cells,HashSet<string> covered,int rotation,out bool owned)
        {
            if(!recoveredMeshes)
            {
                recoveredMeshes=true;
                foreach(var existing in UnityEngine.Resources.FindObjectsOfTypeAll<Mesh>())
                    if(existing.name.StartsWith(MeshPrefix,System.StringComparison.Ordinal) && meshes.Count<512)
                        meshes[existing.name.Substring(MeshPrefix.Length)]=existing;
            }
            var covers=new List<string>(covered);covers.Sort(System.StringComparer.Ordinal);
            var geometry="";foreach(var column in d.columns)geometry+=column.x+","+column.z+","+column.height+";";
            var signature="v3:"+d.id+":"+rotation+":"+(d.enclosure>=.5f)+":"+d.roofStyle+":"+d.facadeStyle+":"+geometry+":"+string.Join(",",covers);
            owned=false;
            if(!meshes.TryGetValue(signature,out var mesh) || mesh==null)
            {
                var b=new HabMeshBuilder();
                foreach(var c in cells)
                {
                    var bounds=ResidentialDevelopment.CellBounds(c);var floor=ResidentialRules.Deck+c.bottom*ResidentialRules.FloorHeight;
                    var top=ResidentialRules.Deck+c.top*ResidentialRules.FloorHeight;
                    var exposed=!covered.Contains(c.x+":"+c.z);
                    var pitched=exposed && d.roofStyle>0;
                    // Keep roof finish and stone ceiling on separate planes; coplanar faces caused speckling.
                    var roofBand=exposed?(pitched?.036f:.028f):0;
                    // Walls stop at their structural top; a removable thin parapet never supports a floor.
                    b.Box(0,new Vector3(bounds.center.x,top-roofBand-.008f,bounds.center.z),new Vector3(.198f,.016f,.198f));
                    b.Box(1,new Vector3(bounds.center.x,floor+.007f,bounds.center.z),new Vector3(.2f,.014f,.2f));
                    for(var level=c.bottom;level<c.top;level++)
                    {
                        var last=level==c.top-1;var shortened=last?roofBand:0;
                        var y=ResidentialRules.Deck+(level+.5f)*ResidentialRules.FloorHeight-shortened*.5f;
                        for(var side=0;side<4;side++)
                        {
                            var dx=side==0?1:side==2?-1:0;var dz=side==1?1:side==3?-1:0;
                            if(cells.Exists(a=>a.x==c.x+dx && a.z==c.z+dz && a.bottom<=level && a.top>level))continue;
                            var at=new Vector3(bounds.center.x+dx*.094f,y,bounds.center.z+dz*.094f);
                            var wallSize=dx!=0?new Vector3(.012f,.12f-shortened,.2f):new Vector3(.2f,.12f-shortened,.012f);
                            if(level==c.bottom)
                            {
                                // Two jambs and a lintel leave a usable entrance through the facade.
                                var offset=dx!=0?Vector3.forward:Vector3.right;
                                b.Box(0,at+offset*.063f,dx!=0?new Vector3(.012f,.12f,.074f):new Vector3(.074f,.12f,.012f));
                                b.Box(0,at-offset*.063f,dx!=0?new Vector3(.012f,.12f,.074f):new Vector3(.074f,.12f,.012f));
                                b.Box(0,at+Vector3.up*.042f,dx!=0?new Vector3(.012f,.036f,.052f):new Vector3(.052f,.036f,.012f));
                            }
                            else if(d.enclosure<.5f)
                            {
                                b.Box(0,at-Vector3.up*.042f,dx!=0?new Vector3(.012f,.035f,.2f):new Vector3(.2f,.035f,.012f));
                                b.Box(1,at,dx!=0?new Vector3(.015f,.12f,.015f):new Vector3(.015f,.12f,.015f));
                            }
                            else b.Box(0,at,wallSize);
                            b.Box(6,at,dx!=0?new Vector3(.005f,.077f,.058f):new Vector3(.058f,.077f,.005f));
                            b.Box(d.residentClass==1?5:4,at+new Vector3(dx*.003f,0,dz*.003f),dx!=0?new Vector3(.004f,.05f,.028f):new Vector3(.028f,.05f,.004f));
                            // Pointed stone canopy and a buttress give the common gothic industrial grammar.
                            var along=dx!=0?Vector3.forward:Vector3.right;
                            var apex=at+Vector3.up*(pitched && last?.036f:.055f);
                            for(var archSide=-1;archSide<=1;archSide+=2)
                            {
                                var spring=at+along*(archSide*.034f)+Vector3.up*.027f;
                                b.Box(3,(spring+apex)*.5f,new Vector3(.008f,(apex-spring).magnitude,.008f),Quaternion.FromToRotation(Vector3.up,apex-spring));
                            }
                            var across=dx!=0?Vector3.forward:Vector3.right;
                            var supportAt=at-new Vector3(dx*.008f,0,dz*.008f);
                            if(d.facadeStyle==0)
                                for(var sign=-1;sign<=1;sign+=2)b.Box(1,supportAt+across*(sign*.082f),new Vector3(.019f,.12f-shortened,.019f));
                            else if(d.facadeStyle==1)
                                b.Box(3,at-Vector3.up*.04f,dx!=0?new Vector3(.012f,.017f,.15f):new Vector3(.15f,.017f,.012f));
                            else if(d.facadeStyle==2)
                            {
                                b.Box(0,at-Vector3.up*.047f,dx!=0?new Vector3(.012f,.022f,.18f):new Vector3(.18f,.022f,.012f));
                                b.Box(3,at-Vector3.up*.022f,dx!=0?new Vector3(.009f,.009f,.17f):new Vector3(.17f,.009f,.009f));
                            }
                            else
                                for(var sign=-1;sign<=1;sign+=2)b.Box(0,supportAt+across*(sign*.076f),new Vector3(.022f,.12f-shortened,.022f));
                        }
                        if(level>c.bottom)b.Box(1,new Vector3(bounds.center.x,y-.058f,bounds.center.z),new Vector3(.202f,.008f,.202f));
                    }
                    if(exposed)
                    {
                        BuildRoof(b,d.roofStyle,bounds.center.x,bounds.center.z,top);
                        // Parapets stay inside the footprint and below the next structural deck.
                        for(var side=0;side<4;side++)
                        {
                            var dx=side==0?1:side==2?-1:0;var dz=side==1?1:side==3?-1:0;
                            if(cells.Exists(a=>a.x==c.x+dx && a.z==c.z+dz && a.top>=c.top))continue;
                            if(d.roofStyle>0)continue;
                            b.Box(3,new Vector3(bounds.center.x+dx*.091f,top-.012f,bounds.center.z+dz*.091f),
                                dx!=0?new Vector3(.012f,.024f,.19f):new Vector3(.19f,.024f,.012f));
                        }
                    }
                }
                // Entrances on each exposed ground edge. Adjacent modules meet flush at this exact floor.
                foreach(var c in cells)for(var side=0;side<4;side++)
                {
                    var dx=side==0?1:side==2?-1:0;var dz=side==1?1:side==3?-1:0;
                    if(cells.Exists(a=>a.x==c.x+dx && a.z==c.z+dz))continue;
                    var center=new Vector3((c.x+.5f)*.2f+dx*.094f,ResidentialRules.Deck+c.bottom*.12f+.039f,(c.z+.5f)*.2f+dz*.094f);
                    b.Box(6,center,dx!=0?new Vector3(.005f,.078f,.042f):new Vector3(.042f,.078f,.005f));
                    b.Box(3,center+new Vector3(dx*.003f,-.035f,dz*.003f),dx!=0?new Vector3(.008f,.008f,.05f):new Vector3(.05f,.008f,.008f));
                }
                // Every module has a shaft connecting all occupied floors and its roof. It ends below the support plane.
                foreach(var access in cells)
                {
                    var accessBounds=ResidentialDevelopment.CellBounds(access);
                    b.Box(1,accessBounds.center,new Vector3(.025f,accessBounds.size.y,.025f));
                }
                mesh=b.Build(d.name);
                if(meshes.Count<512){mesh.name=MeshPrefix+signature;meshes[signature]=mesh;}else owned=true;
            }
            return mesh;
        }
        private void AddFoundations(GameObject parent,List<HabCell> cells,HabPlacement placement)
        {
            var b=new HabMeshBuilder();
            foreach(var c in cells)
            {
                var x=(c.x+.5f)*.2f;var z=(c.z+.5f)*.2f;
                var bottom=development.GroundLocal(x+placement.x*.2f,z+placement.z*.2f)-.018f;var top=ResidentialRules.Deck;
                if(bottom>=top)continue;
                b.Box(1,new Vector3(x,(top+bottom)*.5f,z),new Vector3(.045f,top-bottom,.045f));
                b.Box(0,new Vector3(x,top-.012f,z),new Vector3(.2f,.024f,.2f));
            }
            var go=new GameObject("Terrain foundations",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent.transform,false);
            go.GetComponent<MeshFilter>().sharedMesh=b.Build("Site foundations");go.AddComponent<ResidentialOwnedMesh>();
            go.GetComponent<MeshRenderer>().sharedMaterials=parent.GetComponent<MeshRenderer>().sharedMaterials;
        }
        private static void BuildRoof(HabMeshBuilder b,int style,float x,float z,float top)
        {
            var at=new Vector3(x,top-.036f,z);
            if(style==1)b.Gable(2,at,.198f,.198f,.036f);
            else if(style==2)b.Hip(2,at,.198f,.198f,.036f);
            else if(style==3)
            {
                b.Gable(2,at+Vector3.left*.05f,.098f,.198f,.036f);
                b.Gable(2,at+Vector3.right*.05f,.098f,.198f,.024f);
            }
            else if(style==4)
            {
                b.Box(2,new Vector3(x,top-.032f,z),new Vector3(.198f,.008f,.198f));
                b.Box(0,new Vector3(x-.038f,top-.02f,z),new Vector3(.116f,.028f,.16f));
                b.Hip(2,new Vector3(x-.038f,top-.006f,z),.116f,.16f,.006f);
                b.Box(3,new Vector3(x+.075f,top-.02f,z),new Vector3(.009f,.024f,.19f));
            }
            else b.Box(2,new Vector3(x,top-.024f,z),new Vector3(.198f,.008f,.198f));
        }
        private void BuildGallery(GameObject parent,HabPlacement p,bool pending)
        {
            var b=new HabMeshBuilder();var dx=p.linkDirection==0?1:p.linkDirection==2?-1:0;
            var dz=p.linkDirection==1?1:p.linkDirection==3?-1:0;
            var go=new GameObject("Covered connecting gallery",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent.transform,false);
            for(var i=0;i<p.linkLength;i++)
            {
                var x=(p.linkX+dx*i-p.x+.5f)*.2f;var z=(p.linkZ+dz*i-p.z+.5f)*.2f;
                var size=dx!=0?new Vector3(.2f,.012f,.075f):new Vector3(.075f,.012f,.2f);
                b.Box(0,new Vector3(x,ResidentialRules.Deck+.006f,z),size);
                b.Box(2,new Vector3(x,ResidentialRules.Deck+.112f,z),size);
                for(var side=-1;side<=1;side+=2)
                {
                    var offset=dx!=0?new Vector3(0,0,side*.034f):new Vector3(side*.034f,0,0);
                    b.Box(1,new Vector3(x,ResidentialRules.Deck+.058f,z)+offset,dx!=0?new Vector3(.2f,.008f,.008f):new Vector3(.008f,.008f,.2f));
                    b.Box(3,new Vector3(x,ResidentialRules.Deck+.058f,z)+offset,new Vector3(.008f,.108f,.008f));
                }
                if(!pending)
                {
                    var ground=development.GroundLocal(x+p.x*.2f,z+p.z*.2f)-.018f;
                    b.Box(1,new Vector3(x,(ground+ResidentialRules.Deck)*.5f,z),new Vector3(.025f,Mathf.Max(.01f,ResidentialRules.Deck-ground),.025f));
                    var col=go.AddComponent<BoxCollider>();col.center=new Vector3(x,ResidentialRules.Deck+.06f,z);
                    col.size=dx!=0?new Vector3(.2f,.12f,.075f):new Vector3(.075f,.12f,.2f);
                }
            }
            go.GetComponent<MeshFilter>().sharedMesh=b.Build("Residential gallery");go.AddComponent<ResidentialOwnedMesh>();
            go.GetComponent<MeshRenderer>().sharedMaterials=parent.GetComponent<MeshRenderer>().sharedMaterials;
            if(!pending){var selector=go.AddComponent<ResidentialModuleSelection>();selector.Block=GetComponent<ManagedBuilding>();selector.Serial=p.serial;}
        }
        public void UpdateConstruction()
        {
            if(construction==null || development.Data.project==null)return;
            // Keep the entire volume at its final location. A luminous work plane rises through the wire cage.
            var p=development.Data.project;var progress=Mathf.Clamp01(p.work/p.duration);
            if(workPlane==null)
            {
                var go=new GameObject("Work plane",typeof(LineRenderer));go.transform.SetParent(construction.transform,false);
                workPlane=go.GetComponent<LineRenderer>();workPlane.useWorldSpace=false;workPlane.loop=true;workPlane.positionCount=4;workPlane.widthMultiplier=.008f;
                workPlane.sharedMaterial=Material(ref preferredMaterial,new Color(1,1,.6f,.8f));
                var cells=ResidentialRules.Cells(p.placement,ResidentialDevelopment.Library);workBounds=ResidentialDevelopment.CellBounds(cells[0]);
                foreach(var c in cells)workBounds.Encapsulate(ResidentialDevelopment.CellBounds(c));
                workBounds.center-=construction.transform.localPosition;
            }
            var min=workBounds.min;var max=workBounds.max;var y=Mathf.Lerp(min.y,max.y,progress);
            workCorners[0]=new Vector3(min.x,y,min.z);workCorners[1]=new Vector3(max.x,y,min.z);
            workCorners[2]=new Vector3(max.x,y,max.z);workCorners[3]=new Vector3(min.x,y,max.z);
            workPlane.SetPositions(workCorners);
        }
        private static Material Material(ref Material cached,Color color)
        {
            if(cached==null)
            {
                var name="WH30K Residential material / "+ColorUtility.ToHtmlStringRGBA(color);
                foreach(var existing in UnityEngine.Resources.FindObjectsOfTypeAll<Material>())if(existing.name==name){cached=existing;break;}
                if(cached==null)cached=new Material(Shader.Find("Sprites/Default")){name=name,color=color};
            }
            return cached;
        }
        private static Material[] Repeat(Material material)=>new[]{material,material,material,material,material,material,material};
        public void RefreshSelection()
        {
            if(!development.Selected){Remove(ref selection);selectionSignature=null;return;}
            var signature=development.Data.modules.Count+":"+development.SurfaceRevision;
            if(development.Data.project!=null)signature+=ResidentialRules.Key(development.Data.project.placement);
            else foreach(var c in development.Candidates)signature+=ResidentialRules.Key(c.placement)+c.affordable;
            if(selection!=null && signature==selectionSignature)return;
            selectionSignature=signature;Remove(ref selection);
            selection=new GameObject("Residential selection");selection.transform.SetParent(transform,false);
            var occupied=new HashSet<Vector2Int>();
            foreach(var m in development.Data.modules)foreach(var c in ResidentialRules.Cells(m,ResidentialDevelopment.Library))occupied.Add(new Vector2Int(c.x,c.z));
            // Join directed exposed cell edges into continuous loops, retaining courtyards and concave wings.
            var edges=new Dictionary<Vector2Int,List<Vector2Int>>();
            foreach(var c in occupied)
            {
                if(!occupied.Contains(c+Vector2Int.down))Edge(edges,c,c+Vector2Int.right);
                if(!occupied.Contains(c+Vector2Int.right))Edge(edges,c+Vector2Int.right,c+Vector2Int.one);
                if(!occupied.Contains(c+Vector2Int.up))Edge(edges,c+Vector2Int.one,c+Vector2Int.up);
                if(!occupied.Contains(c+Vector2Int.left))Edge(edges,c+Vector2Int.up,c);
            }
            while(edges.Count>0)
            {
                var it=edges.GetEnumerator();it.MoveNext();var start=it.Current.Key;var at=start;var points=new List<Vector3>();
                var guard=0;
                do
                {
                    var x=at.x*.2f;var z=at.y*.2f;points.Add(new Vector3(x,Mathf.Max(.012f,development.GroundLocal(x,z)+.012f),z));
                    if(!edges.TryGetValue(at,out var nexts))break;var next=nexts[0];nexts.RemoveAt(0);if(nexts.Count==0)edges.Remove(at);at=next;
                }while(at!=start && guard++<4096);
                DrawLine("Developed pale yellow perimeter",points,true,Material(ref boundaryMaterial,new Color(1,.96f,.58f,.7f)),.012f);
            }
            var suggestions=development.Data.project!=null?new[]{new HabCandidate {placement=development.Data.project.placement,affordable=true}}:development.Candidates;
            var index=0;
            var ghosts=new HabMeshBuilder();
            foreach(var candidate in suggestions)
            {
                var mat=!candidate.affordable?2:index==0?0:1;
                var footprint=ResidentialRules.Cells(candidate.placement,ResidentialDevelopment.Library);
                foreach(var c in footprint)
                {
                    var b=ResidentialDevelopment.CellBounds(c);var y=c.bottom>0?b.min.y+.005f:Mathf.Max(.015f,development.GroundLocal(b.center.x,b.center.z)+.02f);
                    var corners=new[]{new Vector3(b.min.x,y,b.min.z),new Vector3(b.max.x,y,b.min.z),new Vector3(b.max.x,y,b.max.z),new Vector3(b.min.x,y,b.max.z)};
                    for(var side=0;side<4;side++)
                    {
                        var dx=side==1?1:side==3?-1:0;var dz=side==2?1:side==0?-1:0;
                        if(footprint.Exists(a=>a.x==c.x+dx && a.z==c.z+dz))continue;
                        var a=corners[side];var e=corners[(side+1)%4];
                        LineBox(ghosts,mat,Vector3.Lerp(a,e,.12f),Vector3.Lerp(a,e,.68f),.006f);
                    }
                    if(c.bottom>0 && c.x==footprint[0].x && c.z==footprint[0].z)
                        LineBox(ghosts,mat,b.center-Vector3.up*b.extents.y,b.center+Vector3.up*b.extents.y,.006f);
                }
                index++;if(index>=5)break;
            }
            var ghostObject=new GameObject("Tentative extensions and supported floors",typeof(MeshFilter),typeof(MeshRenderer));ghostObject.transform.SetParent(selection.transform,false);
            ghostObject.GetComponent<MeshFilter>().sharedMesh=ghosts.Build("Batched residential suggestions");ghostObject.AddComponent<ResidentialOwnedMesh>();
            var preferred=Material(ref preferredMaterial,new Color(1,1,.6f,.65f));var faint=Material(ref ghostMaterial,new Color(1,.97f,.65f,.2f));var waiting=Material(ref waitingMaterial,new Color(.9f,.6f,.3f,.22f));
            ghostObject.GetComponent<MeshRenderer>().sharedMaterials=new[]{preferred,faint,waiting,faint,faint,faint,faint};
        }
        private static void LineBox(HabMeshBuilder mesh,int material,Vector3 a,Vector3 b,float width)
        {mesh.Box(material,(a+b)*.5f,new Vector3(width,(b-a).magnitude,width),Quaternion.FromToRotation(Vector3.up,b-a));}
        private static void Edge(Dictionary<Vector2Int,List<Vector2Int>> edges,Vector2Int a,Vector2Int b)
        {if(!edges.TryGetValue(a,out var list))edges[a]=list=new List<Vector2Int>();list.Add(b);}
        private void DrawLine(string name,IEnumerable<Vector3> points,bool loop,Material material,float width)
        {
            var go=new GameObject(name,typeof(LineRenderer));go.transform.SetParent(selection.transform,false);
            var l=go.GetComponent<LineRenderer>();l.useWorldSpace=false;l.sharedMaterial=material;l.loop=loop;l.widthMultiplier=width;
            var array=new List<Vector3>(points).ToArray();l.positionCount=array.Length;l.SetPositions(array);
        }
    }
    public sealed class HabMeshBuilder
    {
        private readonly List<Vector3> vertices=new List<Vector3>();
        private readonly List<Vector2> uvs=new List<Vector2>();
        private readonly List<int>[] triangles={new List<int>(),new List<int>(),new List<int>(),new List<int>(),new List<int>(),new List<int>(),new List<int>()};
        public void Box(int material,Vector3 center,Vector3 size)=>Box(material,center,size,Quaternion.identity);
        public void Box(int material,Vector3 center,Vector3 size,Quaternion rotation)
        {
            if(material<=1 && Mathf.Min(size.x,Mathf.Min(size.y,size.z))>.08f)
            {ArchitectureGeometry.BevelBox(material,center,size,rotation,.0015f,Quad);return;}
            var h=size*.5f;var p=new Vector3[8];
            for(var i=0;i<8;i++)p[i]=center+rotation*new Vector3((i&1)==0?-h.x:h.x,(i&2)==0?-h.y:h.y,(i&4)==0?-h.z:h.z);
            // Outward winding: bottom, top, front, back, left, right.
            Quad(material,p[0],p[2],p[3],p[1]);Quad(material,p[4],p[5],p[7],p[6]);
            Quad(material,p[0],p[1],p[5],p[4]);Quad(material,p[2],p[6],p[7],p[3]);
            Quad(material,p[0],p[4],p[6],p[2]);Quad(material,p[1],p[3],p[7],p[5]);
        }
        private void Quad(int material,Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            var n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);
            AddUvs(a,b,c);uvs.Add(Uv(d,Vector3.Cross(b-a,c-a)));
            triangles[material].AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
        }
        private void Triangle(int material,Vector3 a,Vector3 b,Vector3 c)
        {var n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);AddUvs(a,b,c);triangles[material].AddRange(new[]{n,n+1,n+2});}
        private void AddUvs(Vector3 a,Vector3 b,Vector3 c)
        {var normal=Vector3.Cross(b-a,c-a);uvs.Add(Uv(a,normal));uvs.Add(Uv(b,normal));uvs.Add(Uv(c,normal));}
        private static Vector2 Uv(Vector3 p,Vector3 normal)
        {
            if(Mathf.Abs(normal.y)>=Mathf.Abs(normal.x) && Mathf.Abs(normal.y)>=Mathf.Abs(normal.z))return new Vector2(p.x,p.z)*20;
            return Mathf.Abs(normal.x)>=Mathf.Abs(normal.z)?new Vector2(p.z,p.y)*20:new Vector2(p.x,p.y)*20;
        }
        public void Gable(int material,Vector3 at,float width,float depth,float height)
        {
            var a=at+new Vector3(-width*.5f,0,-depth*.5f);var b=at+new Vector3(width*.5f,0,-depth*.5f);var c=at+new Vector3(0,height,-depth*.5f);
            var d=a+Vector3.forward*depth;var e=b+Vector3.forward*depth;var f=c+Vector3.forward*depth;
            Triangle(material,a,c,b);Triangle(material,d,e,f);Quad(material,a,b,e,d);Quad(material,b,c,f,e);Quad(material,c,a,d,f);
        }
        public void Hip(int material,Vector3 at,float width,float depth,float height)
        {
            var a=at+new Vector3(-width*.5f,0,-depth*.5f);var b=at+new Vector3(width*.5f,0,-depth*.5f);
            var c=at+new Vector3(width*.5f,0,depth*.5f);var d=at+new Vector3(-width*.5f,0,depth*.5f);var peak=at+Vector3.up*height;
            Quad(material,a,b,c,d);Triangle(material,a,peak,b);Triangle(material,b,peak,c);Triangle(material,c,peak,d);Triangle(material,d,peak,a);
        }
        public Mesh Build(string name)
        {
            var mesh=new Mesh{name=name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetUVs(0,uvs);mesh.subMeshCount=7;
            for(var i=0;i<7;i++)mesh.SetTriangles(triangles[i],i);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
