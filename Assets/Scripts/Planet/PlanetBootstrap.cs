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
        [SerializeField] private int basePatchResolution = 48;
        [SerializeField] private int maximumDepth = 5;
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

            var planetTransform = BuildPlanet(seed);
            resourceSystem.ResetForNewGame(definition);
            environmentState.ResetForNewGame(definition);

            var random = new System.Random(seed);
            if (!planet.TryFindLandPoint(128, random, out var settlementPosition, out var settlementNormal))
            {
                settlementPosition = planet.EvaluateSurfacePoint(Vector3.up);
                settlementNormal = settlementPosition.normalized;
            }

            settlement.BeginNewGame(definition, planet, settlementPosition, settlementNormal, resourceSystem,
                environmentState);
            colonyEventSystem.BeginSession(definition, resourceSystem, environmentState, settlement, seed);

            menu.SetSeed(seed);
            menu.SetDifficulty(difficulty);
            menu.ShowNewGamePanel(false);
            menu.ShowHud(true);
            menu.AppendEventLog($"New colony established on seed {seed} ({definition.displayName}).");

            if (orbitCamera != null)
            {
                orbitCamera.SetTarget(planetTransform, planetRadius);
                orbitCamera.FrameTarget();
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
            menu.AppendEventLog("State saved to disk.");
        }

        public void LoadFromFile()
        {
            if (!File.Exists(SavePath))
            {
                Debug.LogWarning($"Save file not found at {SavePath}");
                menu.AppendEventLog("No save file found.");
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
            var planetTransform = BuildPlanet(save.seed);

            var definition = GameSettings.GetDefinition(save.difficulty);
            resourceSystem.LoadFromSnapshot(save.resources, definition);
            environmentState.LoadFromSnapshot(save.environment, definition);
            settlement.LoadFromSnapshot(save.settlement, planet, definition, resourceSystem, environmentState);
            colonyEventSystem.BeginSession(definition, resourceSystem, environmentState, settlement, save.seed);

            menu.SetSeed(save.seed);
            menu.SetDifficulty(save.difficulty);
            menu.ShowNewGamePanel(false);
            menu.ShowHud(true);
            menu.AppendEventLog("Loaded previous session.");

            if (orbitCamera != null)
            {
                orbitCamera.SetTarget(planetTransform, planetRadius);
                orbitCamera.FrameTarget();
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
                lodUpdateInterval);
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
