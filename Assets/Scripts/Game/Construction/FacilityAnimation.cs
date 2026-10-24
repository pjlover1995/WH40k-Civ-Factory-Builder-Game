using UnityEngine;
using WH30K.Planet;
using WH30K.Sim;

namespace WH30K.Gameplay.Construction
{
    public sealed class FacilityAnimation : MonoBehaviour
    {
        private Transform rotor;
        private float phase;
        private ManagedBuilding building;
        public void Configure(string id)
        {
            var go=new GameObject("Working machinery",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(transform,false);
            go.GetComponent<MeshFilter>().sharedMesh=WorldArt.Cube;go.GetComponent<Renderer>().sharedMaterial=BuildingVisuals.ServiceRoadMaterials[1];
            go.transform.localPosition=new Vector3(.10f,.72f,-.12f);go.transform.localScale=new Vector3(.28f,.025f,.08f);rotor=go.transform;
        }
        private void Update()
        {
            if(ColonyClock.DeltaTime<=0)return;
            if(building==null)building=GetComponent<ManagedBuilding>();
            if(building==null||building.State.offline||rotor==null)return;
            phase+=ColonyClock.DeltaTime*80;rotor.localRotation=Quaternion.Euler(0,phase,0);
        }
    }
}
