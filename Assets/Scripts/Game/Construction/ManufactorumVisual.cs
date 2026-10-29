using System.Collections.Generic;
using UnityEngine;
using WH30K.Sim;
using WH30K.Sim.Resources;

namespace WH30K.Gameplay.Construction
{
    // Repeated bays share photographic architecture materials and meshes. Animation uses game time.
    public sealed class ManufactorumVisual : MonoBehaviour
    {
        [SerializeField] private ManagedBuilding owner;
        private IndustrySystem industry;
        private GameObject root;
        private Transform pending;
        private readonly List<Transform> cranes = new List<Transform>(), fans = new List<Transform>(), loads = new List<Transform>();
        private float phase;
        private static Mesh warehouse, crane, fan, load;
        public void Configure(ManagedBuilding building)
        { owner = building; industry = FindObjectOfType<IndustrySystem>(); Rebuild(); }
        public void Rebuild()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
                if (transform.GetChild(i).name == "Manufactorum warehouse complex")
                { var old = transform.GetChild(i).gameObject; old.SetActive(false); Destroy(old); }
            root = new GameObject("Manufactorum warehouse complex"); root.transform.SetParent(transform, false);
            cranes.Clear(); fans.Clear(); loads.Clear(); pending = null; EnsureMeshes();
            foreach (var bay in owner.State.manufactorum.bays) AddBay(bay, false);
            if (owner.State.manufactorum.project != null) AddBay(owner.State.manufactorum.project, true);
            // Machinery also animates on an unexpanded founding facility.
            AddMachines(root.transform, new Vector3(0, .45f, 0), .4f);
            UpdateConstruction();
        }
        private Transform MeshObject(string name, Mesh mesh, Transform parent)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = owner.GetComponent<MeshRenderer>().sharedMaterials;
            return go.transform;
        }
        private void AddBay(IndustrialBay bay, bool project)
        {
            var group = new GameObject(project ? "Reserved warehouse / construction" : "Operational warehouse").transform;
            group.SetParent(root.transform, false); group.localPosition = new Vector3(bay.x * 2.1f, 0, bay.z * 1.7f);
            if(System.Math.Abs((long)bay.x)+System.Math.Abs((long)bay.z)==1)
            {
                var bridge=MeshObject("Enclosed core service gallery", load,group);
                bridge.localPosition=new Vector3(-bay.x*1.34f,.12f,-bay.z*1.02f);
                bridge.localScale=bay.x!=0?new Vector3(5.5f,2,1.4f):new Vector3(1.4f,2,6);
            }
            var body = MeshObject("Gothic heavy assembly warehouse", warehouse, group);
            // Selection and demolition use the same entity as the founding hall.
            var collider = body.gameObject.AddComponent<BoxCollider>(); collider.center = warehouse.bounds.center; collider.size = warehouse.bounds.size;
            if (project) pending = group;
            else AddMachines(group, Vector3.zero, 1);
        }
        private void AddMachines(Transform parent, Vector3 offset, float scale)
        {
            var gantry = MeshObject("Travelling gantry crane", crane, parent); gantry.localPosition = offset; gantry.localScale = Vector3.one * scale; cranes.Add(gantry);
            var rotor = MeshObject("Rotating extractor fan", fan, parent); rotor.localPosition = offset + new Vector3(.55f,.63f,.3f) * scale; rotor.localScale = Vector3.one * scale; fans.Add(rotor);
            var pallet = MeshObject("Conveyor material pallet", load, parent); pallet.localPosition = offset + new Vector3(0,.17f,-.64f)*scale; pallet.localScale = Vector3.one * scale; loads.Add(pallet);
        }
        public void UpdateConstruction()
        {
            if (pending == null) return;
            pending.localScale = new Vector3(1, Mathf.Lerp(.04f,1,owner.State.manufactorum.work / ManufactorumRules.Duration),1);
        }
        private void Update()
        {
            if (owner == null) return;
            if(root == null) { Configure(owner); return; }
            SimulateAnimation(ColonyClock.DeltaTime);
        }
        public void SimulateAnimation(float dt)
        {
            if (!ColonyRules.IsFinite(dt) || dt <= 0 || industry == null || owner.State.offline) return;
            var operation = industry.OperatingFraction(owner);
            if (operation <= 0) return;
            phase = Mathf.Repeat(phase + dt * Mathf.Min(1,operation) * .45f, Mathf.PI * 2);
            for (var i=0;i<cranes.Count;i++)
            {
                var t=cranes[i]; var p=t.localPosition; p.x=Mathf.Sin(phase+i*.7f)*.55f*t.localScale.x; t.localPosition=p;
                fans[i].localRotation=Quaternion.Euler(0,phase*Mathf.Rad2Deg*4,0);
                p=loads[i].localPosition; p.x=(Mathf.Repeat(phase/(Mathf.PI*2)+i*.17f,1)*1.3f-.65f)*loads[i].localScale.x; loads[i].localPosition=p;
            }
        }
        private static void EnsureMeshes()
        {
            if (warehouse != null) return;
            var b = new AdaptiveBuildingVisual.AdaptiveMeshBuilder();
            void Box(int material, Vector3 center, Vector3 size) => b.Box(material,center,size,Quaternion.identity);
            // 288 x 180 m main hall within a 336 x 255 m reserved service parcel.
            Box(0,new Vector3(0,-.17f,0),new Vector3(1.94f,.46f,1.52f));
            Box(1,new Vector3(0,.27f,0),new Vector3(1.8f,.42f,1.2f));
            for(var i=-4;i<=4;i++)
            {
                Box(0,new Vector3(i*.2f,.3f,0),new Vector3(.045f,.49f,1.26f));
                Box(3,new Vector3(i*.2f,.55f,0),new Vector3(.028f,.035f,1.30f));
                for(var side=-1;side<=1;side+=2)
                {
                    Box(6,new Vector3(i*.2f,.29f,side*.635f),new Vector3(.11f,.16f,.02f));
                    Box(4,new Vector3(i*.2f,.38f,side*.65f),new Vector3(.095f,.022f,.02f));
                    b.Box(3,new Vector3(i*.2f,.405f,side*.65f),new Vector3(.083f,.018f,.022f),Quaternion.Euler(0,0,side*38));
                }
            }
            for(var side=-1;side<=1;side+=2)
                b.Box(2,new Vector3(0,.55f,side*.32f),new Vector3(1.86f,.035f,.68f),Quaternion.Euler(side*12,0,0));
            Box(3,new Vector3(0,.62f,0),new Vector3(1.88f,.035f,.06f));
            Box(1,new Vector3(0,.12f,-.68f),new Vector3(1.6f,.05f,.12f));
            Box(3,new Vector3(0,.7f,.48f),new Vector3(1.7f,.035f,.035f));
            for(var side=-1;side<=1;side+=2)
            {
                Box(1,new Vector3(side*.78f,.4f,.48f),new Vector3(.035f,.64f,.035f));
                Box(1,new Vector3(side*.85f,.72f,-.45f),new Vector3(.075f,.68f,.075f));
                Box(3,new Vector3(side*.85f,1.04f,-.45f),new Vector3(.11f,.035f,.11f));
                Box(1,new Vector3(side*.70f,.22f,.69f),new Vector3(.22f,.32f,.12f));
            }
            warehouse=b.Build("shared heavy industrial warehouse");
            b=new AdaptiveBuildingVisual.AdaptiveMeshBuilder();
            Box(3,new Vector3(0,.74f,.48f),new Vector3(.12f,.06f,.36f));
            Box(1,new Vector3(0,.58f,.39f),new Vector3(.018f,.30f,.018f));
            Box(3,new Vector3(0,.43f,.39f),new Vector3(.07f,.04f,.04f));
            crane=b.Build("shared industrial gantry");
            b=new AdaptiveBuildingVisual.AdaptiveMeshBuilder();
            Box(1,Vector3.zero,new Vector3(.24f,.02f,.05f));
            Box(1,Vector3.zero,new Vector3(.05f,.02f,.24f));
            fan=b.Build("shared extraction rotor");
            b=new AdaptiveBuildingVisual.AdaptiveMeshBuilder();
            Box(3,Vector3.zero,new Vector3(.13f,.065f,.09f));
            load=b.Build("shared conveyor pallet");
        }
    }
}
