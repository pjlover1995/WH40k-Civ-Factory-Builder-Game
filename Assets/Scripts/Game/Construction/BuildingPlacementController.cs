using System.Collections.Generic;
using UnityEngine;
using WH30K.Gameplay;
using WH30K.Planet;
using WH30K.UI;

namespace WH30K.Gameplay.Construction
{
    /// <summary>
    /// Handles user interaction for placing structures onto the procedural planet surface.
    /// </summary>
    [RequireComponent(typeof(PlanetBootstrap))]
    public class BuildingPlacementController : MonoBehaviour
    {
        [Header("Grid")]
        [SerializeField] private int cellsPerFace = 96;
        [SerializeField] private float gridElevationOffset = 6f;

        [Header("Preview Colours")]
        [SerializeField] private Color validPreviewColor = new Color(0.5f, 1f, 0.5f, 0.6f);
        [SerializeField] private Color invalidPreviewColor = new Color(1f, 0.35f, 0.35f, 0.6f);
        [SerializeField] private Color allowedGridColor = new Color(0.6f, 1f, 0.6f, 0.35f);
        [SerializeField] private Color blockedGridColor = new Color(1f, 0.4f, 0.4f, 0.35f);

        private readonly Dictionary<PlanetGridCell, PlacedStructure> placedStructures = new Dictionary<PlanetGridCell, PlacedStructure>();
        private static readonly Dictionary<Color, Material> MaterialCache = new Dictionary<Color, Material>();

        private LODPlanet planet;
        private PlanetGrid grid;
        private PlanetGridVisualizer visualizer;
        private BuildableStructure[] availableStructures;
        private BuildableStructure activeStructure;
        private GameObject previewInstance;
        private MeshRenderer previewRenderer;
        private Material previewMaterial;
        private Material gridAllowedMaterial;
        private Material gridBlockedMaterial;
        private Transform structureRoot;
        private NewGameMenu menu;
        private Camera mainCamera;

        private bool isPlacing;

        public bool IsPlacing => isPlacing;
        public string ActiveStructureName => activeStructure != null ? activeStructure.DisplayName : "Structure";

        private void Awake()
        {
            availableStructures = Resources.LoadAll<BuildableStructure>("Buildings");
            if (availableStructures == null || availableStructures.Length == 0)
            {
                Debug.LogWarning("No BuildableStructure assets found in Resources/Buildings. Using default runtime definition.");
                var fallback = ScriptableObject.CreateInstance<BuildableStructure>();
                fallback.hideFlags = HideFlags.HideAndDontSave;
                availableStructures = new[] { fallback };
            }

            activeStructure = availableStructures[0];
            mainCamera = Camera.main;
        }

        private void OnDisable()
        {
            CancelPlacement();
        }

        public void ConfigureMenu(NewGameMenu newMenu)
        {
            menu = newMenu;
            menu?.UpdateBuildButtonState(ActiveStructureName, false);
        }

        public void AttachToPlanet(LODPlanet newPlanet)
        {
            planet = newPlanet;
            grid = planet != null ? new PlanetGrid(planet, Mathf.Max(8, cellsPerFace)) : null;
            RebuildVisualizer();
            RebuildStructureRoot();
            ClearPreviewInstance();
            placedStructures.Clear();
            isPlacing = false;
            menu?.UpdateBuildButtonState(ActiveStructureName, false);
        }

        public void ResetPlacement()
        {
            foreach (var structure in placedStructures.Values)
            {
                if (structure.Instance != null)
                {
                    Destroy(structure.Instance);
                }
            }

            placedStructures.Clear();
            CancelPlacement();
        }

        public void BeginPlacement()
        {
            if (planet == null || grid == null)
            {
                Debug.LogWarning("Cannot begin placement without an active planet.");
                return;
            }

            EnsurePreviewInstance();
            isPlacing = true;
            menu?.UpdateBuildButtonState(ActiveStructureName, true);
        }

        public void CancelPlacement()
        {
            if (!isPlacing && previewInstance == null)
            {
                return;
            }

            isPlacing = false;
            if (previewInstance != null)
            {
                previewInstance.SetActive(false);
            }

            visualizer?.Hide();
            menu?.UpdateBuildButtonState(ActiveStructureName, false);
        }

        private void Update()
        {
            if (!isPlacing || planet == null || grid == null)
            {
                return;
            }

            if (mainCamera == null)
            {
                mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    return;
                }
            }

            if (!TrySamplePlacementCell(out var frame))
            {
                if (previewInstance != null)
                {
                    previewInstance.SetActive(false);
                }

                visualizer?.Hide();
                return;
            }

            var placementValid = ValidatePlacement(frame);
            UpdatePreview(frame, placementValid);
            visualizer?.ShowArea(grid, frame.Cell, placementValid);

            if (placementValid && Input.GetMouseButtonDown(0))
            {
                PlaceStructure(frame);
            }
            else if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPlacement();
            }
        }

        private bool TrySamplePlacementCell(out CellFrame frame)
        {
            frame = default;
            var ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (!TryRaycastPlanet(ray, out var direction))
            {
                return false;
            }

            var localDirection = planet.transform.InverseTransformDirection(direction);
            var cell = grid.GetCellFromDirection(localDirection);
            frame = grid.CalculateFrame(cell);
            return true;
        }

        private bool TryRaycastPlanet(Ray ray, out Vector3 direction)
        {
            direction = Vector3.zero;
            var center = planet.transform.position;
            var adjustedRadius = planet.Radius + 1200f;
            var oc = ray.origin - center;
            var a = Vector3.Dot(ray.direction, ray.direction);
            var b = 2f * Vector3.Dot(oc, ray.direction);
            var c = Vector3.Dot(oc, oc) - adjustedRadius * adjustedRadius;
            var discriminant = b * b - 4f * a * c;
            if (discriminant < 0f)
            {
                return false;
            }

            var sqrt = Mathf.Sqrt(discriminant);
            var t = (-b - sqrt) / (2f * a);
            if (t < 0f)
            {
                t = (-b + sqrt) / (2f * a);
                if (t < 0f)
                {
                    return false;
                }
            }

            var hitPoint = ray.origin + ray.direction * t;
            direction = (hitPoint - center).normalized;
            return true;
        }

        private bool ValidatePlacement(CellFrame frame)
        {
            var localPosition = planet.transform.InverseTransformPoint(frame.Center);
            var aboveSeaLevel = localPosition.magnitude >= planet.SeaLevel;
            var unoccupied = !placedStructures.ContainsKey(frame.Cell);
            return aboveSeaLevel && unoccupied;
        }

        private void UpdatePreview(CellFrame frame, bool placementValid)
        {
            EnsurePreviewInstance();
            if (previewInstance == null)
            {
                return;
            }

            var dimensions = activeStructure.Dimensions;
            var offset = frame.Normal * (dimensions.y * 0.5f);
            var forward = frame.Forward.sqrMagnitude > 0.001f
                ? frame.Forward
                : Vector3.ProjectOnPlane(mainCamera.transform.forward, frame.Normal).normalized;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.Cross(frame.Normal, Vector3.right);
                if (forward.sqrMagnitude < 0.001f)
                {
                    forward = Vector3.Cross(frame.Normal, Vector3.forward);
                }
            }

            var rotation = Quaternion.LookRotation(forward.normalized, frame.Normal);
            previewInstance.transform.SetPositionAndRotation(frame.Center + offset, rotation);
            previewInstance.transform.localScale = dimensions;
            var validColor = activeStructure != null ? activeStructure.PreviewColor : validPreviewColor;
            previewRenderer.sharedMaterial.color = placementValid ? validColor : invalidPreviewColor;
            previewInstance.SetActive(true);
        }

        private void PlaceStructure(CellFrame frame)
        {
            var instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
            instance.name = activeStructure.DisplayName;
            instance.transform.SetParent(structureRoot != null ? structureRoot : planet.transform, false);
            var collider = instance.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var dimensions = activeStructure.Dimensions;
            var offset = frame.Normal * (dimensions.y * 0.5f);
            var forward = frame.Forward.sqrMagnitude > 0.001f
                ? frame.Forward
                : Vector3.Cross(frame.Normal, Vector3.right);
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.Cross(frame.Normal, Vector3.forward);
            }

            var rotation = Quaternion.LookRotation(forward.normalized, frame.Normal);
            instance.transform.SetPositionAndRotation(frame.Center + offset, rotation);
            instance.transform.localScale = dimensions;

            var renderer = instance.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetMaterial(activeStructure.StructureColor);

            placedStructures[frame.Cell] = new PlacedStructure(frame.Cell, activeStructure, instance);
            menu?.AppendEventLog($"Placed {activeStructure.DisplayName} at {frame.Cell}.");
        }

        private void EnsurePreviewInstance()
        {
            if (previewInstance != null)
            {
                return;
            }

            if (planet == null)
            {
                return;
            }

            var shader = Shader.Find("Unlit/Color");
            previewMaterial = new Material(shader)
            {
                color = validPreviewColor,
                name = "BuildingPreviewMaterial"
            };

            previewInstance = GameObject.CreatePrimitive(PrimitiveType.Cube);
            previewInstance.name = "BuildingPreview";
            previewInstance.transform.SetParent(planet.transform, false);
            var collider = previewInstance.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            previewRenderer = previewInstance.GetComponent<MeshRenderer>();
            previewRenderer.sharedMaterial = previewMaterial;
            previewInstance.SetActive(false);
        }

        private void ClearPreviewInstance()
        {
            if (previewInstance != null)
            {
                Destroy(previewInstance);
                previewInstance = null;
                previewRenderer = null;
            }

            if (previewMaterial != null)
            {
                Destroy(previewMaterial);
                previewMaterial = null;
            }
        }

        private void RebuildVisualizer()
        {
            visualizer?.Dispose();
            visualizer = null;

            if (planet == null)
            {
                return;
            }

            EnsureGridMaterials();
            visualizer = new PlanetGridVisualizer(planet.transform, gridAllowedMaterial, gridBlockedMaterial, gridElevationOffset);
        }

        private void RebuildStructureRoot()
        {
            if (structureRoot != null)
            {
                Destroy(structureRoot.gameObject);
            }

            if (planet == null)
            {
                structureRoot = null;
                return;
            }

            var rootGO = new GameObject("PlacedStructures");
            structureRoot = rootGO.transform;
            structureRoot.SetParent(planet.transform, false);
        }

        private void EnsureGridMaterials()
        {
            var shader = Shader.Find("Unlit/Color");
            if (gridAllowedMaterial == null)
            {
                gridAllowedMaterial = new Material(shader)
                {
                    color = allowedGridColor,
                    name = "GridAllowedMaterial"
                };
            }

            if (gridBlockedMaterial == null)
            {
                gridBlockedMaterial = new Material(shader)
                {
                    color = blockedGridColor,
                    name = "GridBlockedMaterial"
                };
            }
        }

        private static Material GetMaterial(Color color)
        {
            if (!MaterialCache.TryGetValue(color, out var material) || material == null)
            {
                material = new Material(Shader.Find("Standard")) { color = color };
                MaterialCache[color] = material;
            }

            return material;
        }

        private struct PlacedStructure
        {
            public readonly PlanetGridCell Cell;
            public readonly BuildableStructure Definition;
            public readonly GameObject Instance;

            public PlacedStructure(PlanetGridCell cell, BuildableStructure definition, GameObject instance)
            {
                Cell = cell;
                Definition = definition;
                Instance = instance;
            }
        }
    }
}
