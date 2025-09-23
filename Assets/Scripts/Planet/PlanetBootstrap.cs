using System;
using System.IO;
using UnityEngine;
using WH30K.CameraSystem;
using WH30K.Game;
using WH30K.Planet;
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
    public class PlanetBootstrap : MonoBehaviour
    {
        [Header("Planet")]
        [SerializeField] private float planetRadius = 3000f;
        [SerializeField] private int basePatchResolution = 32;
        [SerializeField] private int maximumDepth = 6;
        [SerializeField] private int maxPatchResolution = 1024;
        [SerializeField] private float targetTriangleArea = 10f;
        [SerializeField] private float splitDistance = 2400f;
        [SerializeField] private float splitFalloff = 1.8f;
        [SerializeField] private float lodUpdateInterval = 0.25f;
        [SerializeField] private string terrainMaterialResource = "WH30K/PlanetTerrain";
        [SerializeField] private string saveFileName = "wh30k_vslice_save.json";

        private LODPlanet planet;
        private Material terrainMaterialInstance;
        private NewGameMenu menu;
        private ResourceSystem resourceSystem;
        private EnvironmentState environmentState;
        private ColonyEventSystem colonyEventSystem;
        private Settlement settlement;
        private SimpleOrbitCamera orbitCamera;

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
            menu = GetComponent<NewGameMenu>();
            resourceSystem = GetComponent<ResourceSystem>();
            environmentState = GetComponent<EnvironmentState>();
            colonyEventSystem = GetComponent<ColonyEventSystem>();
            settlement = GetComponent<Settlement>();
            orbitCamera = UnityEngine.Camera.main != null
                ? UnityEngine.Camera.main.GetComponent<SimpleOrbitCamera>()
                : null;

            LoadTerrainMaterial();
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
            var definition = GameSettings.GetDefinition(difficulty);
            GameSettings.StartNewGame(seed, difficulty);
            var planetRoot = BuildPlanet(seed);
            if (orbitCamera != null && planetRoot != null)
            {
                orbitCamera.SetTarget(planetRoot, planet.Radius);
            }

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

            settlement.BeginNewGame(definition, planet, surfacePosition, surfaceNormal, resourceSystem, environmentState);
            colonyEventSystem.BeginSession(definition, resourceSystem, environmentState, settlement, seed);

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

        public void SaveToFile()
        {
            if (!GameSettings.HasActiveGame)
            {
                Debug.LogWarning("Cannot save without an active game.");
                return;
            }

            var save = new GameSaveData
            {
                seed = GameSettings.CurrentSeed,
                difficulty = GameSettings.CurrentDifficulty,
                environment = environmentState.CreateSnapshot(),
                resources = resourceSystem.CreateSnapshot(),
                settlement = settlement.CreateSnapshot()
            };

            var json = JsonUtility.ToJson(save, true);
            File.WriteAllText(SavePath, json);
            menu?.AppendEventLog("State saved to disk.");
        }

        public void LoadFromFile()
        {
            if (!File.Exists(SavePath))
            {
                Debug.LogWarning($"Save file not found at {SavePath}");
                menu?.AppendEventLog("No save file found.");
                return;
            }

            var json = File.ReadAllText(SavePath);
            var save = JsonUtility.FromJson<GameSaveData>(json);
            if (save == null)
            {
                Debug.LogError("Failed to parse save file.");
                return;
            }

            GameSettings.ApplyLoadedState(save.seed, save.difficulty);

            var definition = GameSettings.GetDefinition(save.difficulty);
            var planetRoot = BuildPlanet(save.seed);
            if (orbitCamera != null && planetRoot != null)
            {
                orbitCamera.SetTarget(planetRoot, planet.Radius);
            }

            resourceSystem.LoadFromSnapshot(save.resources, definition);
            environmentState.LoadFromSnapshot(save.environment, definition);
            settlement.LoadFromSnapshot(save.settlement, planet, definition, resourceSystem, environmentState);
            colonyEventSystem.BeginSession(definition, resourceSystem, environmentState, settlement, save.seed);

            if (menu != null)
            {
                menu.SetSeed(save.seed);
                menu.SetDifficulty(save.difficulty);
                menu.ShowNewGamePanel(false);
                menu.ShowHud(true);
                menu.ShowEventPanel(false);
                menu.AppendEventLog("State loaded from disk.");
            }
        }

        private Transform BuildPlanet(int seed)
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
            planet.BuildPlanet(seed, terrainMaterialInstance);
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


        [Serializable]
        private class GameSaveData
        {
            public int seed;
            public GameSettings.Difficulty difficulty;
            public EnvironmentSnapshot environment;
            public ResourceSnapshot resources;
            public SettlementSnapshot settlement;
        }
    }
}
