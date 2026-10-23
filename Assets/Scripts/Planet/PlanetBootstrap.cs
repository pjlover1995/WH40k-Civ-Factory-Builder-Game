using System;
using System.IO;
using UnityEngine;
using WH30K.CameraSystem;
using WH30K.Game;
using WH30K.Planet;
using WH30K.Gameplay.Construction;
using WH30K.Sim.Environment;
using WH30K.Sim.Events;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;
using WH30K.UI;

namespace WH30K.Gameplay
{
    /// <summary>
    /// High-level orchestrator that wires the procedural planet, settlement simulation, UI and persistence together.
    /// </summary>
    [RequireComponent(typeof(NewGameMenu))]
    [RequireComponent(typeof(ResourceSystem))]
    [RequireComponent(typeof(EnvironmentState))]
    [RequireComponent(typeof(ColonyEventSystem))]
    [RequireComponent(typeof(Settlement))]
    [RequireComponent(typeof(BuildingPlacementController))]
    public class PlanetBootstrap : MonoBehaviour
    {
        [Header("Planet")]
        [SerializeField] private float planetRadius = 12000f;
        [SerializeField] private int basePatchResolution = 32;
        [SerializeField] private int maximumDepth = 6;
        [SerializeField] private int maxPatchResolution = 1024;
        [SerializeField] private float targetTriangleArea = 10f;
        [SerializeField] private float splitDistance = 2400f;
        [SerializeField] private float splitFalloff = 1.8f;
        [SerializeField] private float lodUpdateInterval = 0.25f;
        [SerializeField] private string terrainMaterialResource = "WH30K/PlanetTerrain";
        [SerializeField] private string saveFileName = "wh30k_vslice_save.json";

#if UNITY_EDITOR
        [Header("Debug")]
        [SerializeField] private bool spawnDebugMarkers = false;
        [SerializeField] private int debugMarkerCount = 16;
        [SerializeField] private float debugMarkerScale = 25f;
        [SerializeField] private Color debugMarkerColor = Color.magenta;
#endif

        private LODPlanet planet;
        private Material terrainMaterialInstance;
        private NewGameMenu menu;
        private ResourceSystem resourceSystem;
        private EnvironmentState environmentState;
        private ColonyEventSystem colonyEventSystem;
        private Settlement settlement;
        private SimpleOrbitCamera orbitCamera;
        private BuildingPlacementController buildingPlacement;
        public LODPlanet ActivePlanet => planet;

#if UNITY_EDITOR
        private Transform debugMarkerRoot;
        private Material debugMarkerMaterial;
        private bool hasLastSurfacePoint;
        private Vector3 lastSurfacePosition;
        private Vector3 lastSurfaceNormal;
#endif

        private const int DefaultMaxPatchResolution = 1024;

        private string SavePath
        {
            get
            {
                var directory = Application.persistentDataPath;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                return Path.Combine(directory, saveFileName);
            }
        }

        private void Awake()
        {
            EnforcePatchResolutionCap();

            menu = GetComponent<NewGameMenu>();
            resourceSystem = GetComponent<ResourceSystem>();
            environmentState = GetComponent<EnvironmentState>();
            colonyEventSystem = GetComponent<ColonyEventSystem>();
            settlement = GetComponent<Settlement>();
            orbitCamera = UnityEngine.Camera.main != null
                ? UnityEngine.Camera.main.GetComponent<SimpleOrbitCamera>()
                : null;
            buildingPlacement = GetComponent<BuildingPlacementController>();

            LoadTerrainMaterial();
        }

        private void OnValidate()
        {
            EnforcePatchResolutionCap();
        }

        private void EnforcePatchResolutionCap()
        {
            if (maxPatchResolution < DefaultMaxPatchResolution)
            {
                maxPatchResolution = DefaultMaxPatchResolution;
            }
        }

        private void Start()
        {
            if (GameSettings.HasActiveGame)
            {
                BeginNewGame(GameSettings.CurrentSeed, GameSettings.CurrentDifficulty);
            }
        }

        internal void ConfigureMenu(NewGameMenu newGameMenu)
        {
            menu = newGameMenu;
        }

        public void BeginNewGame(int seed, GameSettings.Difficulty difficulty)
        {
            ResolveOrbitCamera();
            colonyEventSystem.EndSession();
            var definition = GameSettings.GetDefinition(difficulty);
            GameSettings.StartNewGame(seed, difficulty);
            var planetRoot = BuildPlanet(seed);
            if (orbitCamera != null && planetRoot != null)
            {
                orbitCamera.SetTarget(planetRoot, planet.Radius);
            }

            buildingPlacement?.AttachToPlanet(planet);

            resourceSystem.ResetForNewGame(definition);
            environmentState.ResetForNewGame(definition);

            const int landAttempts = 128;
            var random = new System.Random(seed ^ 0x51C0FFEE);
            if (!planet.TryFindLandPoint(landAttempts, random, out var surfacePosition, out var surfaceNormal))
            {
                surfacePosition = planet.transform.position + Vector3.up * planet.Radius;
                surfaceNormal = (surfacePosition - planet.transform.position).sqrMagnitude > 0.001f
                    ? (surfacePosition - planet.transform.position).normalized
                    : Vector3.up;
            }

#if UNITY_EDITOR
            lastSurfacePosition = surfacePosition;
            lastSurfaceNormal = surfaceNormal;
            hasLastSurfacePoint = true;
#endif

            settlement.BeginNewGame(definition, planet, surfacePosition, surfaceNormal, resourceSystem, environmentState);
            planet.GetComponent<PlanetPresentation>().LightLandingSite(surfaceNormal);
            orbitCamera?.FocusSettlement(surfaceNormal);
            colonyEventSystem.BeginSession(definition, resourceSystem, environmentState, settlement, seed);

#if UNITY_EDITOR
            if (spawnDebugMarkers)
            {
                SpawnDebugMarkers();
            }
#endif

            if (menu != null)
            {
                menu.SetSeed(seed);
                menu.SetDifficulty(difficulty);
                menu.ShowNewGamePanel(false);
                menu.ShowHud(true);
                menu.ShowEventPanel(false);
                menu.AppendEventLog($"Initiated expedition under '{definition.displayName}' conditions.");
            }
        }

        public string CaptureSaveJson()
        {
            if (!GameSettings.HasActiveGame) throw new InvalidOperationException("No active game to save.");
            return JsonUtility.ToJson(new GameSaveData
            {
                version = 3,
                terrainVersion = planet.TerrainVersion,
                seed = GameSettings.CurrentSeed,
                difficulty = GameSettings.CurrentDifficulty,
                environment = environmentState.CreateSnapshot(),
                resources = resourceSystem.CreateSnapshot(),
                settlement = settlement.CreateSnapshot(),
                gridResolution = buildingPlacement.GridResolution,
                structures = buildingPlacement.CreateSnapshot(),
                events = colonyEventSystem.CreateSnapshot()
            }, true);
        }

        public void SaveToFile()
        {
            if (!GameSettings.HasActiveGame) return;
            try
            {
                var path = SavePath;
                var temporaryPath = path + ".tmp";
                File.WriteAllText(temporaryPath, CaptureSaveJson());
                if (File.Exists(path)) File.Replace(temporaryPath, path, path + ".bak");
                else File.Move(temporaryPath, path);
                menu?.AppendEventLog("Game saved, including buildings and pending decisions.");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"Save failed: {exception.Message}");
                menu?.AppendEventLog("Could not save. Check the save folder is writable.");
            }
        }

        public void LoadFromFile()
        {
            try
            {
                if (!File.Exists(SavePath))
                {
                    menu?.AppendEventLog("No save file found.");
                    return;
                }
                TryLoadSaveJson(File.ReadAllText(SavePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"Load failed: {exception.Message}");
                menu?.AppendEventLog("Could not read the save file.");
            }
        }

        public bool TryLoadSaveJson(string json)
        {
            GameSaveData save;
            try
            {
                save = JsonUtility.FromJson<GameSaveData>(json);
                if (!ValidateSave(save, out var error))
                {
                    menu?.AppendEventLog($"Load rejected: {error}");
                    return false;
                }
            }
            catch (ArgumentException)
            {
                menu?.AppendEventLog("Load rejected: invalid save JSON.");
                return false;
            }

            // Validate the whole document before replacing the running session.
            ResolveOrbitCamera();
            colonyEventSystem.EndSession();
            GameSettings.ApplyLoadedState(save.seed, save.difficulty);
            var definition = GameSettings.GetDefinition(save.difficulty);
            var planetRoot = BuildPlanet(save.seed, save.version < 3 ? 0 : save.terrainVersion);
            if (orbitCamera != null) orbitCamera.SetTarget(planetRoot, planet.Radius);
            buildingPlacement.AttachToPlanet(planet, save.gridResolution == 0 ? 96 : save.gridResolution);
            resourceSystem.LoadFromSnapshot(save.resources, definition);
            environmentState.LoadFromSnapshot(save.environment, definition);
            settlement.LoadFromSnapshot(save.settlement, planet, definition, resourceSystem, environmentState);
            var loadedNormal = settlement.CreateSnapshot().normal;
            planet.GetComponent<PlanetPresentation>().LightLandingSite(loadedNormal);
            orbitCamera?.FocusSettlement(loadedNormal);
            buildingPlacement.RestoreSnapshot(save.structures);
            settlement.RefreshReadout();

            if (menu != null)
            {
                menu.SetSeed(save.seed);
                menu.SetDifficulty(save.difficulty);
                menu.ShowNewGamePanel(false);
                menu.ShowHud(true);
                menu.AppendEventLog("Game loaded. Buildings and economic effects restored.");
            }
            colonyEventSystem.BeginSession(definition, resourceSystem, environmentState, settlement, save.seed, save.events);
            return true;
        }

        private bool ValidateSave(GameSaveData save, out string error)
        {
            error = "Unsupported or incomplete save.";
            if (save == null || save.version < 0 || save.version > 3
                || save.terrainVersion < 0 || save.terrainVersion > 1
                || !Enum.IsDefined(typeof(GameSettings.Difficulty), save.difficulty)) return false;
            var colony = save.settlement;
            var resources = save.resources;
            var environment = save.environment;
            var values = new[]
            {
                colony.population, colony.workforce, colony.productionModifier, colony.upkeepModifier,
                colony.industryModifier, colony.tickTimer, colony.position.x, colony.position.y, colony.position.z,
                colony.normal.x, colony.normal.y, colony.normal.z,
                resources.Population, resources.Workforce, resources.Stockpile, resources.ProductionPerCycle,
                resources.UpkeepPerCycle, resources.NetProduction,
                environment.Co2, environment.O2, environment.WaterPollution, environment.GlobalTemperatureOffset
            };
            foreach (var value in values)
                if (!WH30K.Sim.ColonyRules.IsFinite(value)) return false;
            if (colony.population <= 0f || colony.population > 10000000f || colony.workforce < 0f
                || colony.position.sqrMagnitude < 1f || colony.productionModifier < 0.25f
                || colony.productionModifier > 3f || colony.upkeepModifier < 0.1f || colony.upkeepModifier > 3f
                || colony.industryModifier < 0f || colony.industryModifier > 3f
                || colony.tickTimer < 0f || colony.tickTimer > 60f
                || resources.Stockpile < 0f || resources.Population < 0f || resources.Workforce < 0f
                || environment.Co2 < 0f || environment.O2 < 0f || environment.WaterPollution < 0f
                || !EventSchedule.IsValid(save.events)) return false;
            if (colony.buildings != null)
            {
                if (colony.buildings.Length > 4096) return false;
                foreach (var id in colony.buildings)
                    if (id != "HabBlock" && id != "Factory" && id != "Utility") return false;
            }
            return buildingPlacement.ValidateSnapshot(save.structures,
                save.gridResolution == 0 ? 96 : save.gridResolution, out error);
        }

        private void ResolveOrbitCamera()
        {
            // The scene camera may not be enabled yet when GameRoot.Awake runs.
            var mainCamera = UnityEngine.Camera.main;
            orbitCamera = mainCamera != null ? mainCamera.GetComponent<SimpleOrbitCamera>() : null;
        }

        private Transform BuildPlanet(int seed, int terrainVersion = 1)
        {
            if (planet != null)
            {
                Destroy(planet.gameObject);
            }

            var planetGO = new GameObject("Planet");
            planetGO.transform.SetParent(transform, false);
            planet = planetGO.AddComponent<LODPlanet>();
            planet.ApplyConfiguration(planetRadius, basePatchResolution, maximumDepth, splitDistance, splitFalloff,
                lodUpdateInterval, maxPatchResolution, targetTriangleArea);
            planet.BuildPlanet(seed, terrainMaterialInstance, terrainVersion);
            planetGO.AddComponent<PlanetPresentation>().Configure(planet);
            planet.SetCamera(UnityEngine.Camera.main);
            return planetGO.transform;
        }

        private void LoadTerrainMaterial()
        {
            if (!string.IsNullOrEmpty(terrainMaterialResource))
            {
                var loaded = Resources.Load<Material>(terrainMaterialResource);
                if (loaded != null)
                {
                    terrainMaterialInstance = Instantiate(loaded);
                    return;
                }
            }

            terrainMaterialInstance = new Material(Shader.Find("Standard"));
            Debug.LogWarning("Fallback Standard shader material created for planet terrain. Resource missing?");
        }

#if UNITY_EDITOR
        private void DestroyDebugMarkerRoot()
        {
            if (debugMarkerRoot == null)
            {
                return;
            }

            Destroy(debugMarkerRoot.gameObject);
            debugMarkerRoot = null;
        }

        private void SpawnDebugMarkers()
        {
            if (planet == null)
            {
                return;
            }

            DestroyDebugMarkerRoot();

            if (!hasLastSurfacePoint && debugMarkerCount <= 0)
            {
                return;
            }

            debugMarkerRoot = new GameObject("DebugMarkers").transform;
            debugMarkerRoot.SetParent(planet.transform, false);

            if (debugMarkerMaterial == null)
            {
                debugMarkerMaterial = new Material(Shader.Find("Standard"))
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            debugMarkerMaterial.color = debugMarkerColor;

            var markerIndex = 0;

            if (hasLastSurfacePoint)
            {
                CreateDebugMarker($"DebugMarker_{markerIndex:00}", lastSurfacePosition, lastSurfaceNormal);
                markerIndex++;
            }

            if (debugMarkerCount <= 0)
            {
                return;
            }

            var goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (var i = 0; i < debugMarkerCount; i++)
            {
                var t = (i + 0.5f) / Math.Max(1, debugMarkerCount);
                var inclination = Mathf.Acos(1f - 2f * t);
                var azimuth = goldenAngle * i;

                var direction = new Vector3(
                    Mathf.Sin(inclination) * Mathf.Cos(azimuth),
                    Mathf.Cos(inclination),
                    Mathf.Sin(inclination) * Mathf.Sin(azimuth));

                var surfacePoint = planet.EvaluateSurfacePoint(direction);
                var surfaceNormal = surfacePoint.sqrMagnitude > 0.001f
                    ? surfacePoint.normalized
                    : Vector3.up;

                CreateDebugMarker($"DebugMarker_{markerIndex:00}", surfacePoint, surfaceNormal);
                markerIndex++;
            }
        }

        private void CreateDebugMarker(string name, Vector3 localSurfacePoint, Vector3 localSurfaceNormal)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = name;
            marker.transform.SetParent(debugMarkerRoot, false);
            marker.transform.localPosition = localSurfacePoint;
            marker.transform.localScale = Vector3.one * debugMarkerScale;

            var worldNormal = planet.transform.TransformDirection(localSurfaceNormal).normalized;
            marker.transform.rotation = Quaternion.FromToRotation(Vector3.up, worldNormal);

            var collider = marker.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }

            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = debugMarkerMaterial;
            }
        }

        public bool DebugMarkersEnabled
        {
            get => spawnDebugMarkers;
            set
            {
                if (spawnDebugMarkers == value)
                {
                    return;
                }

                spawnDebugMarkers = value;
                if (!spawnDebugMarkers)
                {
                    DestroyDebugMarkerRoot();
                }
                else if (planet != null)
                {
                    SpawnDebugMarkers();
                }
            }
        }

        public int DebugMarkerCount
        {
            get => debugMarkerCount;
            set
            {
                var clamped = Mathf.Max(0, value);
                if (debugMarkerCount == clamped)
                {
                    return;
                }

                debugMarkerCount = clamped;
                if (spawnDebugMarkers && planet != null)
                {
                    SpawnDebugMarkers();
                }
            }
        }

        public float DebugMarkerScale
        {
            get => debugMarkerScale;
            set
            {
                var clamped = Mathf.Max(0.01f, value);
                if (Mathf.Approximately(debugMarkerScale, clamped))
                {
                    return;
                }

                debugMarkerScale = clamped;
                if (debugMarkerRoot == null)
                {
                    return;
                }

                foreach (Transform child in debugMarkerRoot)
                {
                    child.localScale = Vector3.one * debugMarkerScale;
                }
            }
        }

        public Color DebugMarkerColor
        {
            get => debugMarkerColor;
            set
            {
                debugMarkerColor = value;
                if (debugMarkerMaterial != null)
                {
                    debugMarkerMaterial.color = debugMarkerColor;
                }
            }
        }

        public void DebugRespawnMarkers()
        {
            if (planet == null)
            {
                return;
            }

            SpawnDebugMarkers();
        }

        public void DebugClearMarkers()
        {
            DestroyDebugMarkerRoot();
        }

        public bool HasDebugMarkers => debugMarkerRoot != null && debugMarkerRoot.childCount > 0;
#endif


        [Serializable]
        private class GameSaveData
        {
            public int version;
            public int terrainVersion;
            public int seed;
            public GameSettings.Difficulty difficulty;
            public EnvironmentSnapshot environment;
            public ResourceSnapshot resources;
            public SettlementSnapshot settlement;
            public int gridResolution;
            public PlacedStructureSnapshot[] structures;
            public EventSnapshot events;
        }
    }
}
