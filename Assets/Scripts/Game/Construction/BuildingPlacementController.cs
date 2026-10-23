using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using WH30K.Gameplay;
using WH30K.Planet;
using WH30K.UI;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;

namespace WH30K.Gameplay.Construction
{
    /// <summary>
    /// Handles user interaction for placing structures onto the procedural planet surface.
    /// </summary>
    [RequireComponent(typeof(PlanetBootstrap))]
    public class BuildingPlacementController : MonoBehaviour
    {
        [Header("Grid")]
        [SerializeField] private int cellsPerFace = 256;
        [SerializeField] private float gridElevationOffset = 6f;

        [Header("Preview Colours")]
        [SerializeField] private Color validPreviewColor = new Color(0.5f, 1f, 0.5f, 0.6f);
        [SerializeField] private Color invalidPreviewColor = new Color(1f, 0.35f, 0.35f, 0.6f);
        [SerializeField] private Color allowedGridColor = new Color(0.6f, 1f, 0.6f, 0.35f);
        [SerializeField] private Color blockedGridColor = new Color(1f, 0.4f, 0.4f, 0.35f);

        private readonly Dictionary<PlanetGridCell, PlacedStructure> placedStructures = new Dictionary<PlanetGridCell, PlacedStructure>();

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
        private ResourceSystem resources;
        private Settlement settlement;

        private bool isPlacing;

        public bool IsPlacing => isPlacing;
        public string ActiveStructureName => activeStructure != null
            ? $"{activeStructure.DisplayName} ({activeStructure.ConstructionCost:0})" : "Structure";
        public int GridResolution => grid != null ? grid.CellsPerFace : cellsPerFace;
        public string ActiveStructureDetails
        {
            get
            {
                if (activeStructure == null) return string.Empty;
                var economy = activeStructure.Economy;
                return $"Housing +{economy.capacity:0} | Jobs +{economy.jobs:0}\n" +
                       $"Output/worker {economy.outputPerWorker:0.0} | Upkeep {economy.upkeep:0}\n" +
                       $"CO2/s at full staffing: {activeStructure.IndustryImpact.co2Delta:+0.00;-0.00;0}";
            }
        }

        private void Awake()
        {
            cellsPerFace = Mathf.Clamp(cellsPerFace, 8, 256);
            resources = GetComponent<ResourceSystem>();
            settlement = GetComponent<Settlement>();
            availableStructures = Resources.LoadAll<BuildableStructure>("Buildings");
            if (availableStructures == null || availableStructures.Length == 0)
            {
                Debug.LogWarning("No BuildableStructure assets found in Resources/Buildings. Using default runtime definition.");
                var fallback = ScriptableObject.CreateInstance<BuildableStructure>();
                fallback.hideFlags = HideFlags.HideAndDontSave;
                availableStructures = new[] { fallback };
            }

            Array.Sort(availableStructures, (a, b) => string.CompareOrdinal(a.Id, b.Id));
            activeStructure = Array.Find(availableStructures, definition => definition.Id == "Outpost") ?? availableStructures[0];
            mainCamera = Camera.main;
            menu?.UpdateBuildButtonState(ActiveStructureName, false);
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

        public void AttachToPlanet(LODPlanet newPlanet, int resolution = 0)
        {
            if (resolution != 0) cellsPerFace = Mathf.Clamp(resolution, 8, 256);
            planet = newPlanet;
            grid = planet != null ? new PlanetGrid(planet, Mathf.Max(8, cellsPerFace)) : null;
            RebuildVisualizer();
            RebuildStructureRoot();
            ClearPreviewInstance();
            placedStructures.Clear();
            RefreshEconomy();
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
            RefreshEconomy();
            CancelPlacement();
        }

        public void CycleStructure()
        {
            var index = Array.IndexOf(availableStructures, activeStructure);
            SelectStructure(availableStructures[(index + 1) % availableStructures.Length].Id);
        }

        public bool SelectStructure(string id)
        {
            var definition = Array.Find(availableStructures, item => item.Id == id);
            if (definition == null) return false;
            CancelPlacement();
            ClearPreviewInstance();
            activeStructure = definition;
            menu?.UpdateBuildButtonState(ActiveStructureName, false);
            return true;
        }

        public PlacedStructureSnapshot[] CreateSnapshot()
        {
            var snapshots = new List<PlacedStructureSnapshot>();
            foreach (var entry in placedStructures.Values)
                snapshots.Add(new PlacedStructureSnapshot
                {
                    id = entry.Definition.Id, face = entry.Cell.Face, x = entry.Cell.X, y = entry.Cell.Y,
                    operation = entry.Instance.GetComponent<ManagedBuilding>().State
                });
            return snapshots.ToArray();
        }

        public bool ValidateSnapshot(PlacedStructureSnapshot[] snapshots, int resolution, out string error)
        {
            var ids = new HashSet<string>();
            foreach (var definition in availableStructures) ids.Add(definition.Id);
            return ConstructionSave.Validate(snapshots, resolution, ids, out error);
        }

        public void RestoreSnapshot(PlacedStructureSnapshot[] snapshots)
        {
            ResetPlacement();
            if (snapshots != null)
                foreach (var snapshot in snapshots)
                {
                    var definition = Array.Find(availableStructures, item => item.Id == snapshot.id);
                    var cell = new PlanetGridCell(snapshot.face, snapshot.x, snapshot.y);
                    SpawnStructure(grid.CalculateFrame(cell), definition).Restore(snapshot.operation);
                }
            RefreshEconomy();
        }

        private void RefreshEconomy()
        {
            var definitions = new List<ManagedBuilding>();
            foreach (var entry in placedStructures.Values) definitions.Add(entry.Instance.GetComponent<ManagedBuilding>());
            settlement?.SetConstructedBuildings(definitions);
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

            // Cancel works even when the pointer is off the planet or over a panel.
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPlacement();
                return;
            }
            if ((EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                || (menu != null && menu.HasOpenEvent))
            {
                if (previewInstance != null) previewInstance.SetActive(false);
                visualizer?.Hide();
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
                TryPlaceCell(frame.Cell);
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
            var adjustedRadius = planet.Radius + 700f;
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
            var start = Mathf.Max(0f, (-b - sqrt) / (2f * a));
            var end = (-b + sqrt) / (2f * a);
            if (end <= start) return false;
            var previous = start;
            // Intersect the generated height field, rather than an oversized imaginary sphere.
            for (var i = 1; i <= 128; i++)
            {
                var t = Mathf.Lerp(start, end, i / 128f);
                var local = planet.transform.InverseTransformPoint(ray.GetPoint(t));
                if (local.magnitude <= planet.EvaluateSurfacePoint(local.normalized).magnitude)
                {
                    for (var j = 0; j < 14; j++)
                    {
                        var mid = (previous + t) * 0.5f;
                        local = planet.transform.InverseTransformPoint(ray.GetPoint(mid));
                        if (local.magnitude <= planet.EvaluateSurfacePoint(local.normalized).magnitude) t = mid;
                        else previous = mid;
                    }
                    direction = (ray.GetPoint(t) - center).normalized;
                    return true;
                }
                previous = t;
            }
            return false;
        }

        private bool ValidatePlacement(CellFrame frame)
        {
            var localPosition = planet.transform.InverseTransformPoint(frame.Center);
            var aboveSeaLevel = localPosition.magnitude >= planet.SeaLevel;
            var unoccupied = !placedStructures.ContainsKey(frame.Cell);
            if (!aboveSeaLevel || !unoccupied || placedStructures.Count >= 4096 || activeStructure == null || resources == null
                || !resources.CanAfford(activeStructure.ConstructionCost)
                || settlement.OverlapsStartingBuildings(frame.Center, activeStructure.Dimensions)) return false;
            var radius = FootprintRadius(activeStructure);
            foreach (var entry in placedStructures.Values)
                if (Vector3.Distance(frame.Center, grid.GetCellCenterWorld(entry.Cell))
                    < radius + FootprintRadius(entry.Definition)) return false;
            return true;
        }

        private static float FootprintRadius(BuildableStructure definition)
            => new Vector2(definition.Dimensions.x, definition.Dimensions.z).magnitude * 0.5f;

        private void UpdatePreview(CellFrame frame, bool placementValid)
        {
            EnsurePreviewInstance();
            if (previewInstance == null)
            {
                return;
            }

            var dimensions = activeStructure.Dimensions;
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
            previewInstance.transform.SetPositionAndRotation(frame.Center, rotation);
            previewInstance.transform.localScale = dimensions;
            var validColor = activeStructure != null ? activeStructure.PreviewColor : validPreviewColor;
            previewRenderer.sharedMaterial.color = placementValid ? validColor : invalidPreviewColor;
            previewInstance.SetActive(true);
        }

        public bool TryPlaceCell(PlanetGridCell cell)
        {
            if (grid == null || cell.X < 0 || cell.Y < 0
                || cell.X >= grid.CellsPerFace || cell.Y >= grid.CellsPerFace) return false;
            var frame = grid.CalculateFrame(cell);
            if (!ValidatePlacement(frame) || !resources.TrySpend(activeStructure.ConstructionCost)) return false;
            SpawnStructure(frame, activeStructure);
            RefreshEconomy();
            menu?.AppendEventLog($"Built {activeStructure.DisplayName} (-{activeStructure.ConstructionCost:0} stockpile).");
            return true;
        }

        private ManagedBuilding SpawnStructure(CellFrame frame, BuildableStructure definition)
        {
            var dimensions = definition.Dimensions;
            var instance = BuildingVisuals.Create(definition.Id, definition.DisplayName,
                structureRoot != null ? structureRoot : planet.transform, dimensions);
            var forward = frame.Forward.sqrMagnitude > 0.001f
                ? frame.Forward
                : Vector3.Cross(frame.Normal, Vector3.right);
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.Cross(frame.Normal, Vector3.forward);
            }

            var rotation = Quaternion.LookRotation(forward.normalized, frame.Normal);
            instance.transform.SetPositionAndRotation(frame.Center, rotation);
            instance.transform.localScale = dimensions;

            var managed = instance.AddComponent<ManagedBuilding>();
            managed.Configure(definition.Id, definition.DisplayName, definition.Economy, definition.IndustryImpact, resources, settlement);

            placedStructures[frame.Cell] = new PlacedStructure(frame.Cell, definition, instance);
            return managed;
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

            previewInstance = BuildingVisuals.Create(activeStructure.Id, "BuildingPreview", planet.transform,
                activeStructure.Dimensions, previewMaterial);

            previewRenderer = previewInstance.GetComponent<MeshRenderer>();
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
