using UnityEngine;
using UnityEngine.EventSystems;
using WH30K.UI;
using WH30K.CameraSystem;
using WH30K.Sim.Resources;

namespace WH30K.Gameplay.Construction
{
    public sealed class BuildingSelection : MonoBehaviour
    {
        public ManagedBuilding Selected { get; private set; }
        private NewGameMenu menu;
        private BuildingPlacementController placement;
        private LineRenderer ring;
        private Material ringMaterial;
        private GameObject farmOutline;
        private void Start() { menu = GetComponent<NewGameMenu>(); placement = GetComponent<BuildingPlacementController>(); }
        private void Update()
        {
            if (menu == null || placement == null || placement.IsPlacing || menu.HasOpenEvent) return;
            var earthworks = GetComponent<EarthworksController>();
            if (earthworks != null && earthworks.IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Select(null); return; }
            if (!Input.GetMouseButtonDown(0) || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) return;
            SelectAtScreenPosition(Input.mousePosition);
        }
        public void SelectAtScreenPosition(Vector2 screenPosition)
        {
            var camera = Camera.main;
            var earthworks = GetComponent<EarthworksController>();
            ManagedBuilding building = null;
            if (!SimpleOrbitCamera.TryScreenRay(camera, screenPosition, out var ray)) return;
            if (Physics.Raycast(ray, out var hit, camera.farClipPlane, camera.cullingMask))
            {
                var marker = hit.collider.GetComponent<DiscoveryMarker>();
                if (marker != null) { Select(null); marker.Inspect(); return; }
                var dozer=hit.collider.GetComponentInParent<BulldozerVehicle>();
                if (dozer != null)
                {
                    var world=GetComponent<PlanetBootstrap>().ActivePlanet;
                    var outward=(dozer.transform.position-world.transform.position).normalized;
                    if(Vector3.Dot(outward,camera.transform.position-dozer.transform.position)>0)
                        earthworks?.Open(WH30K.Sim.EarthworkMode.Move);
                    return;
                }
                var module=hit.collider.GetComponent<ResidentialModuleSelection>();
                building = module!=null?module.Block:hit.collider.GetComponentInParent<ManagedBuilding>();
                if(module!=null && building!=null)building.Residential.SelectedModule=module.Serial;
                if (building != null)
                {
                    var planet = GetComponent<PlanetBootstrap>().ActivePlanet;
                    var normal = (building.transform.position - planet.transform.position).normalized;
                    if (Vector3.Dot(normal, camera.transform.position - building.transform.position) <= 0) building = null;
                }
            }
            Select(building);
        }
        public void Select(ManagedBuilding building)
        {
            if(building!=null && building.Id=="Farm")building=building.EntityRoot??building;
            if(Selected!=null && Selected!=building)Selected.Residential?.SetSelected(false);
            Selected = building;
            if (ring != null) Destroy(ring.gameObject);
            if(farmOutline!=null)Destroy(farmOutline);farmOutline=null;
            ring = null;
            if(building!=null && building.Id=="Farm")
            {
                farmOutline=new GameObject("Farm estate territory");farmOutline.transform.SetParent(building.transform,false);
                farmOutline.AddComponent<FarmTerritoryOutline>().Configure(building,GetComponent<WH30K.Sim.Settlements.Settlement>(),GetComponent<WH30K.Sim.Resources.IndustrySystem>());
            }
            else if(building!=null && building.Residential!=null)building.Residential.SetSelected(true);
            else if (building != null)
            {
                var go = new GameObject("Selected building perimeter", typeof(LineRenderer));
                go.transform.SetParent(building.transform, false);
                ring = go.GetComponent<LineRenderer>(); ring.useWorldSpace = false;
                ring.loop = true; ring.positionCount = building.Id == "ColonyShip" ? 4 : 48; ring.widthMultiplier = 0.035f;
                if (ringMaterial == null) ringMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.88f, 0.71f, 0.34f) };
                ring.sharedMaterial = ringMaterial;
                if (building.Id == "ColonyShip")
                {
                    var bounds = building.GrowthBounds();
                    for (var i = 0; i < 4; i++) ring.SetPosition(i, new Vector3(i < 2 ? bounds.min.x : bounds.max.x,
                        bounds.min.y + .003f, i == 0 || i == 3 ? bounds.min.z : bounds.max.z));
                    ring.widthMultiplier = .004f;
                }
                else for (var i = 0; i < 48; i++)
                {
                    var angle = i * Mathf.PI * 2 / 48;
                    ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * 0.72f, 0.025f, Mathf.Sin(angle) * 0.72f));
                }
            }
            (menu != null ? menu : GetComponent<NewGameMenu>()).ShowBuilding(building);
        }
        private void OnDestroy() { if (ringMaterial != null) Destroy(ringMaterial); }
    }
}
