using UnityEngine;
using UnityEngine.EventSystems;
using WH30K.UI;

namespace WH30K.Gameplay.Construction
{
    public sealed class BuildingSelection : MonoBehaviour
    {
        public ManagedBuilding Selected { get; private set; }
        private NewGameMenu menu;
        private BuildingPlacementController placement;
        private LineRenderer ring;
        private Material ringMaterial;
        private void Start() { menu = GetComponent<NewGameMenu>(); placement = GetComponent<BuildingPlacementController>(); }
        private void Update()
        {
            if (menu == null || placement == null || placement.IsPlacing || menu.HasOpenEvent) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Select(null); return; }
            if (!Input.GetMouseButtonDown(0) || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) return;
            var camera = Camera.main;
            ManagedBuilding building = null;
            if (camera != null && Physics.Raycast(camera.ScreenPointToRay(Input.mousePosition), out var hit, camera.farClipPlane))
            {
                building = hit.collider.GetComponent<ManagedBuilding>();
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
            Selected = building;
            if (ring != null) Destroy(ring.gameObject);
            ring = null;
            if (building != null)
            {
                var go = new GameObject("Selected building perimeter", typeof(LineRenderer));
                go.transform.SetParent(building.transform, false);
                ring = go.GetComponent<LineRenderer>(); ring.useWorldSpace = false;
                ring.loop = true; ring.positionCount = 48; ring.widthMultiplier = 0.035f;
                if (ringMaterial == null) ringMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.88f, 0.71f, 0.34f) };
                ring.sharedMaterial = ringMaterial;
                for (var i = 0; i < 48; i++)
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
