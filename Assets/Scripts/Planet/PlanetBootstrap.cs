using System;
using System.Collections.Generic;
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
using WH30K.Sim;

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
    [RequireComponent(typeof(ResearchSystem))]
    public class PlanetBootstrap : MonoBehaviour
    {
        [Header("Planet")]
        [SerializeField] private float planetRadius = PlanetScale.DefaultRadius;
        [SerializeField] private TerrainSettings terrainSettings = new TerrainSettings();
        public TerrainSettings NewWorldTerrain
        {
            get { var settings = terrainSettings.Copy(); settings.radius = planetRadius; return settings; }
        }
        public void ConfigureNewWorldTerrain(TerrainSettings settings)
        {
            if (!TerrainSettings.Valid(settings)) throw new ArgumentException("Invalid terrain settings.");
            terrainSettings = settings.Copy(); planetRadius = settings.radius;
        }
        [SerializeField] private int basePatchResolution = 32;
        [SerializeField] private int maximumDepth = 6;
        [SerializeField] private int maxPatchResolution = 1024;
        [SerializeField] private float targetTriangleArea = 10f;
        [SerializeField] private float splitDistance = 1200f;
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
        private ColonyClock clock;
        private IndustrySystem industry;
        private ResearchSystem research;
        private EarthworksController earthworks;
        private readonly List<GameSettings.LandingSite> landingSites = new List<GameSettings.LandingSite>();
        private int selectedLandingSiteIndex = -1;
        private GameObject orbitalMainVessel;
        private bool awaitingCaptainBriefing;
        private float landingCrewAllocation = ShipRules.StartingCrew;
        private float landingRationAllocation = ShipRules.StartingFood;
        private float landingSupplyFraction = 1f;
        public LODPlanet ActivePlanet => planet;
        public bool ChoosingLanding { get; private set; }
        public Vector3 LandingDirection { get; private set; }
        public IReadOnlyList<GameSettings.LandingSite> LandingSites => landingSites;
        public int SelectedLandingSiteIndex => selectedLandingSiteIndex;
        public float LandingCrewAllocation => landingCrewAllocation;
        public float LandingRationAllocation => landingRationAllocation;
        public float LandingSupplyFraction => landingSupplyFraction;

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
            clock = GetComponent<ColonyClock>() ?? gameObject.AddComponent<ColonyClock>();
            industry = GetComponent<IndustrySystem>() ?? gameObject.AddComponent<IndustrySystem>();
            research = GetComponent<ResearchSystem>() ?? gameObject.AddComponent<ResearchSystem>();
            earthworks = GetComponent<EarthworksController>() ?? gameObject.AddComponent<EarthworksController>();

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
                BeginNewGame(GameSettings.CurrentSeed, GameSettings.CurrentDifficulty, chooseLanding: true);
            }
        }

        internal void ConfigureMenu(NewGameMenu newGameMenu)
        {
            menu = newGameMenu;
        }

        public void BeginNewGame(int seed, GameSettings.Difficulty difficulty, bool chooseLanding = false,
            bool legacyCore = false, bool cinematicArrival = false)
        {
            ChoosingLanding = false;
            awaitingCaptainBriefing = false;
            landingSites.Clear();
            selectedLandingSiteIndex = -1;
            landingCrewAllocation = ShipRules.StartingCrew;
            landingRationAllocation = ShipRules.StartingFood;
            landingSupplyFraction = 1f;
            if (orbitalMainVessel != null) { Destroy(orbitalMainVessel); orbitalMainVessel = null; }
            menu?.ResetSessionUi();
            ResolveOrbitCamera();
            colonyEventSystem.EndSession();
            clock.Begin();
            var definition = GameSettings.GetDefinition(difficulty);
            GameSettings.StartNewGame(seed, difficulty);
            var planetRoot = BuildPlanet(seed);
            if (orbitCamera != null && planetRoot != null)
            {
                orbitCamera.SetTarget(planetRoot, planet.Radius);
            }

            buildingPlacement?.AttachToPlanet(planet);

            resourceSystem.ResetForNewGame(definition, !legacyCore);
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

            if (chooseLanding && !legacyCore) GenerateLandingSites(seed, surfaceNormal);

            settlement.BeginNewGame(definition, planet, surfacePosition, surfaceNormal, resourceSystem, environmentState, !legacyCore);
            planet.GetComponent<PlanetPresentation>().LightLandingSite(surfaceNormal);
            orbitCamera?.SetColonyLocation(surfaceNormal, settlement.HasExplorator
                ? (settlement.ShipLayoutVersion >= 2 ? ShipRules.Length : ShipRules.LegacyLength) * 1.3f : 220);
            colonyEventSystem.BeginSession(definition, resourceSystem, environmentState, settlement, seed);
            industry.Configure(planet, surfaceNormal);
            research.Configure(buildingPlacement, industry);
            if (!legacyCore)
            {
                if (chooseLanding && landingSites.Count > 0) industry.PrepareOrbitalExpedition(landingSites);
                else industry.PrepareImmediateLanding(surfaceNormal);
            }
            earthworks.Configure(planet, surfaceNormal, spawnVehicle: !chooseLanding);

#if UNITY_EDITOR
            if (spawnDebugMarkers)
            {
                SpawnDebugMarkers();
            }
#endif

            if (menu != null)
            {
                menu.ShowMainMenu(false);
                menu.SetSeed(seed);
                menu.SetDifficulty(difficulty);
                menu.ShowNewGamePanel(false);
                menu.ShowHud(true);
                menu.ShowEventPanel(false);
                menu.AppendEventLog($"Initiated expedition under '{definition.displayName}' conditions.");
            }
            if (chooseLanding)
            {
                ChoosingLanding = true;
                selectedLandingSiteIndex = FindLandingSite(GameSettings.Difficulty.Standard, 0);
                if (selectedLandingSiteIndex < 0 && landingSites.Count > 0) selectedLandingSiteIndex = 0;
                LandingDirection = selectedLandingSiteIndex >= 0 ? landingSites[selectedLandingSiteIndex].direction : SuggestedLanding(surfaceNormal);
                CreateOrbitalMainVessel(LandingDirection);
                orbitCamera?.SetColonyLocation(LandingDirection, ShipRules.Length * 3.6f);
                settlement.SetLandingPreview(true);
                clock.DecisionPending = true;
                menu?.ShowHud(false); menu?.ShowLandingPanel(!cinematicArrival);
                menu?.UpdateLandingOptions(landingSites, selectedLandingSiteIndex);
                menu?.UpdateLandingLoadout();
                menu?.UpdateLandingReadout(SelectedLandingSiteIndex >= 0 ? LandingSummary(landingSites[selectedLandingSiteIndex]) : LandingSummary(LandingDirection));
                if (cinematicArrival)
                {
                    awaitingCaptainBriefing = true;
                    orbitCamera?.FocusOrbitalArrival(LandingDirection);
                    var arrival = orbitalMainVessel.GetComponent<OrbitalVesselArrival>();
                    arrival.Begin(planet.transform, LandingDirection, planet.Radius, 12000f, () =>
                        menu?.ShowDiscoveryPanel("CAPTAIN / WARP EXIT REPORT",
                            "The main vessel suffered critical drive and keel damage during warp translation. We are stable in orbit, but the hull cannot survive atmospheric entry.\n\nThe landing tender is intact. Select a surveyed surface site, choose its crew and supply loadout, then deploy the tender.",
                            "PROCEED TO LANDING SITES", () =>
                            {
                                awaitingCaptainBriefing = false;
                                clock.DecisionPending = true;
                                orbitCamera?.SetColonyLocation(LandingDirection, ShipRules.Length * 3.6f);
                                orbitCamera?.FocusSettlement(LandingDirection);
                                menu?.ShowLandingPanel(true);
                                menu?.UpdateLandingOptions(landingSites, selectedLandingSiteIndex);
                                menu?.UpdateLandingLoadout();
                                if (SelectedLandingSiteIndex >= 0) menu?.UpdateLandingReadout(LandingSummary(landingSites[selectedLandingSiteIndex]));
                            }));
                }
                else
                {
                    orbitCamera?.SetColonyLocation(LandingDirection, ShipRules.Length * 3.6f);
                    orbitCamera?.FocusSettlement(LandingDirection);
                }
            }
        }

        private void CreateOrbitalMainVessel(Vector3 direction)
        {
            if (orbitalMainVessel != null) Destroy(orbitalMainVessel);
            if (planet == null) return;
            orbitalMainVessel = BuildingVisuals.Create("ColonyShip", "Damaged main vessel in stable orbit", planet.transform,
                BuildingVisuals.LegacyShipDimensions);
            orbitalMainVessel.AddComponent<OrbitalVesselArrival>()
                .SetOrbit(planet.transform, direction, planet.Radius, 12000f);
        }

        private void GenerateLandingSites(int seed, Vector3 fallback)
        {
            var candidates = new List<(Vector3 direction, float relief)>();
            var random = new System.Random(unchecked(seed ^ 0x72FC91A3));
            for (var attempt = 0; attempt < 3000 && candidates.Count < 120; attempt++)
            {
                var y = (float)random.NextDouble() * 2f - 1f;
                var angle = (float)random.NextDouble() * Mathf.PI * 2f;
                var planar = Mathf.Sqrt(1f - y * y);
                var direction = new Vector3(planar * Mathf.Cos(angle), y, planar * Mathf.Sin(angle));
                if (planet.EvaluateSurfacePoint(direction).magnitude <= planet.SeaLevel + 8f) continue;
                if (!ValidLandingSite(direction)) continue;
                candidates.Add((direction, MeasureLandingRelief(direction)));
            }

            if (candidates.Count == 0 && ValidLandingSite(fallback)) candidates.Add((fallback.normalized, MeasureLandingRelief(fallback)));
            candidates.Sort((a, b) => a.relief.CompareTo(b.relief));
            var categories = new[]
            {
                GameSettings.Difficulty.Easy, GameSettings.Difficulty.Standard,
                GameSettings.Difficulty.ExtremeHard
            };
            var selected = new List<Vector3>(categories.Length * 2);
            for (var slot = 0; slot < categories.Length * 2 && candidates.Count > 0; slot++)
            {
                var target = (slot + .5f) / (categories.Length * 2f) * (candidates.Count - 1);
                var chosen = -1; var bestDelta = float.MaxValue;
                for (var i = 0; i < candidates.Count; i++)
                {
                    var separated = true;
                    foreach (var prior in selected)
                        if (planet.SurfaceDistance(prior, candidates[i].direction) < 80000f) { separated = false; break; }
                    if (!separated) continue;
                    var delta = Mathf.Abs(i - target);
                    if (delta < bestDelta) { bestDelta = delta; chosen = i; }
                }
                if (chosen < 0)
                {
                    chosen = Mathf.Clamp(Mathf.RoundToInt(target), 0, candidates.Count - 1);
                }
                var candidate = candidates[chosen];
                selected.Add(candidate.direction);
                var categoryIndex = slot / 2;
                var category = categories[categoryIndex];
                landingSites.Add(new GameSettings.LandingSite
                {
                    category = category,
                    variant = slot % 2,
                    direction = candidate.direction,
                    localRelief = candidate.relief,
                    reserveMultiplier = GameSettings.GetLandingReserveMultiplier(category)
                });
            }
        }

        private float MeasureLandingRelief(Vector3 direction)
        {
            var right = Vector3.Cross(direction, Mathf.Abs(direction.y) > .95f ? Vector3.right : Vector3.up).normalized;
            var forward = Vector3.Cross(direction, right);
            var minimum = planet.EvaluateSurfacePoint(direction).magnitude;
            var maximum = minimum;
            for (var i = 0; i < 16; i++)
            {
                var angle = i * Mathf.PI * 2f / 16f;
                var sampleDirection = (direction + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * (900f / planet.Radius)).normalized;
                var height = planet.EvaluateSurfacePoint(sampleDirection).magnitude;
                minimum = Mathf.Min(minimum, height);
                maximum = Mathf.Max(maximum, height);
            }
            return maximum - minimum;
        }

        private int FindLandingSite(GameSettings.Difficulty category, int variant)
        {
            for (var i = 0; i < landingSites.Count; i++)
                if (landingSites[i].category == category && landingSites[i].variant == variant) return i;
            return -1;
        }

        private Vector3 SuggestedLanding(Vector3 fallback)
        {
            var best = float.MaxValue; var result = fallback;
            foreach (var candidate in industry.Deposits)
            {
                if (!candidate.Discovered) continue;
                // Approach from either broadside instead of placing a 3-km hull over the vein.
                var tangent = Quaternion.FromToRotation(Vector3.up, candidate.Direction) * Vector3.right;
                for (var side = -1; side <= 1; side += 2)
                {
                    var site = (candidate.Direction + tangent * (side * 450f / planet.Radius)).normalized;
                    if (!ValidLandingSite(site)) continue;
                    var frame = Quaternion.FromToRotation(Vector3.up, site);
                    var score = 0f;
                    foreach (var kind in new[] { MaterialKind.IronOre, MaterialKind.Stone, MaterialKind.Biomass })
                    {
                        var nearest = float.MaxValue;
                        foreach (var deposit in industry.Deposits)
                            if (deposit.Discovered && deposit.Kind == kind)
                            {
                                var offset = Quaternion.Inverse(frame) * ((deposit.Direction - site) * planet.Radius);
                                var margin = deposit.Excavation != null ? deposit.Excavation.majorRadius + 30 : 40;
                                if (Mathf.Abs(offset.x) < ShipRules.Beam * .5f + margin && Mathf.Abs(offset.z) < ShipRules.Length * .5f + margin) continue;
                                nearest = Mathf.Min(nearest, planet.SurfaceDistance(site, deposit.Direction));
                            }
                        score = Mathf.Max(score, nearest);
                    }
                    if (score < best) { best = score; result = site; }
                }
            }
            // A sparse scan must not leave the automatic suggestion on an invalid cradle site.
            if (!ValidLandingSite(result))
            {
                var random = new System.Random(planet.Seed ^ 0x315A);
                for (var attempt = 0; attempt < 512; attempt++)
                {
                    var n = new Vector3((float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1).normalized;
                    if (ValidLandingSite(n)) { result = n; break; }
                }
            }
            return result;
        }

        public bool SelectLandingSite(Vector3 direction)
        {
            if (!ChoosingLanding || !ConstructionSave.ValidDirection(direction.x, direction.y, direction.z)) return false;
            direction.Normalize();
            for (var i = 0; i < landingSites.Count; i++)
                if (planet.SurfaceDistance(direction, landingSites[i].direction) <= 1f)
                    return SelectLandingSite(i);
            return false;
        }

        public bool SelectLandingSite(int index)
        {
            if (!ChoosingLanding || awaitingCaptainBriefing || index < 0 || index >= landingSites.Count) return false;
            var site = landingSites[index];
            if (!ValidLandingSite(site.direction)) return false;
            selectedLandingSiteIndex = index;
            LandingDirection = site.direction;
            orbitCamera?.SetColonyLocation(LandingDirection, ShipRules.Length * 3.6f);
            orbitCamera?.FocusSettlement(LandingDirection);
            menu?.UpdateLandingReadout(LandingSummary(site));
            menu?.UpdateLandingOptions(landingSites, selectedLandingSiteIndex);
            return true;
        }

        public bool SetLandingLoadout(float crew, float rations, float supplyFraction)
        {
            if (!ChoosingLanding || awaitingCaptainBriefing || !industry.CanApplyInitialLandingLoadout(crew, rations, supplyFraction)) return false;
            landingCrewAllocation = crew;
            landingRationAllocation = rations;
            landingSupplyFraction = supplyFraction;
            menu?.UpdateLandingLoadout();
            return true;
        }

        public bool ValidLandingSite(Vector3 direction)
        {
            if (planet == null || !ConstructionSave.ValidDirection(direction.x, direction.y, direction.z)) return false;
            direction.Normalize();
            var height = planet.EvaluateSurfacePoint(direction).magnitude;
            // Same tangent orientation as preview / deployed ship. Check the complete 3-km hull,
            // allowing curvature beneath the raised keel, with a gentle central cradle site.
            var rotation = Quaternion.FromToRotation(Vector3.up, direction);
            for (var z = -6; z <= 6; z++)
                for (var x = -1; x <= 1; x++)
                {
                    var n = (direction * height + rotation * new Vector3(x * ShipRules.Beam * .5f, 0, z * ShipRules.Length / 12)).normalized;
                    var samplePoint = planet.EvaluateSurfacePoint(n);
                    if (samplePoint.magnitude <= planet.SeaLevel + 5 || Vector3.Dot(samplePoint, direction) - height > 125) return false;
                }
            var right = Vector3.Cross(direction, Mathf.Abs(direction.y) > .95f ? Vector3.right : Vector3.up).normalized;
            var ahead = Vector3.Cross(direction, right);
            for (var i = 0; i < 16; i++)
            {
                var a = i * Mathf.PI * 2 / 16;
                var n = (direction + (right * Mathf.Cos(a) + ahead * Mathf.Sin(a)) * 65 / planet.Radius).normalized;
                var sample = planet.EvaluateSurfacePoint(n).magnitude;
                if (sample <= planet.SeaLevel + 5 || Mathf.Abs(sample - height) > 18) return false;
            }
            return height > planet.SeaLevel + 5;
        }

        public string LandingSummary(Vector3 direction)
        {
            var text = new System.Text.StringBuilder($"{planet.Biome(direction)} / reserves within 1,200 m\n");
            var totals = new float[IndustryRules.MaterialCount];
            foreach (var deposit in industry.Deposits)
                if (deposit.Discovered && planet.SurfaceDistance(direction, deposit.Direction) <= 1200)
                    totals[(int)deposit.Kind] += deposit.Remaining;
            foreach (var kind in new[] { MaterialKind.IronOre, MaterialKind.CopperOre, MaterialKind.Stone, MaterialKind.Biomass, MaterialKind.Crystal, MaterialKind.Promethium })
                text.Append($"{IndustryRules.Name(kind)} {totals[(int)kind]:0}   ");
            return text.ToString();
        }

        public string LandingSummary(GameSettings.LandingSite site)
        {
            var text = new System.Text.StringBuilder($"{LandingTierLabel(site.category)} / {planet.Biome(site.direction)} / local relief {site.localRelief:0} m / reserves within 1,200 m\n");
            var totals = new float[IndustryRules.MaterialCount];
            foreach (var deposit in industry.Deposits)
                if (deposit.Discovered && planet.SurfaceDistance(site.direction, deposit.Direction) <= 1200)
                    totals[(int)deposit.Kind] += deposit.Remaining;
            foreach (var kind in new[] { MaterialKind.IronOre, MaterialKind.CopperOre, MaterialKind.Stone, MaterialKind.Biomass, MaterialKind.Crystal, MaterialKind.Promethium })
                text.Append($"{IndustryRules.Name(kind)} {totals[(int)kind]:0}   ");
            return text.ToString();
        }

        private static string LandingTierLabel(GameSettings.Difficulty difficulty)
            => difficulty == GameSettings.Difficulty.Easy ? "EASY"
                : difficulty == GameSettings.Difficulty.ExtremeHard || difficulty == GameSettings.Difficulty.Hard ? "HARD" : "MEDIUM";

        public bool ConfirmLanding()
        {
            if (!ChoosingLanding || awaitingCaptainBriefing || selectedLandingSiteIndex < 0 || selectedLandingSiteIndex >= landingSites.Count
                || !ValidLandingSite(LandingDirection)
                || !industry.CanApplyInitialLandingLoadout(landingCrewAllocation, landingRationAllocation, landingSupplyFraction)) return false;
            ChoosingLanding = false;
            var site = landingSites[selectedLandingSiteIndex];
            var difficulty = site.category;
            GameSettings.StartNewGame(GameSettings.CurrentSeed, difficulty);
            var definition = GameSettings.GetDefinition(difficulty);
            resourceSystem.ResetForNewGame(definition, true);
            environmentState.ResetForNewGame(definition);
            colonyEventSystem.EndSession();
            var point = planet.transform.TransformPoint(planet.EvaluateSurfacePoint(LandingDirection));
            settlement.BeginNewGame(definition, planet, point, LandingDirection, resourceSystem, environmentState);
            if (!industry.ApplyInitialLandingLoadout(landingCrewAllocation, landingRationAllocation, landingSupplyFraction)) return false;
            colonyEventSystem.BeginSession(definition, resourceSystem, environmentState, settlement, GameSettings.CurrentSeed);
            settlement.SetLandingPreview(false);
            settlement.BeginLanderDescent();
            planet.GetComponent<PlanetPresentation>().LightLandingSite(LandingDirection);
            orbitCamera?.SetColonyLocation(LandingDirection, ShipRules.Length * 1.3f);
            orbitCamera?.FocusSettlement(LandingDirection);
            earthworks.Configure(planet, LandingDirection);
            menu?.SetDifficulty(difficulty);
            menu?.ShowLandingPanel(false); menu?.ShowHud(true);
            clock.Begin();
            menu?.AppendEventLog("Colony ship landed. Cargo funds every building plus three mines. Start extraction, water and manufacturing; outposts reveal richer deposits.");
            menu?.ResetGuideForNewExpedition();
            menu?.ShowGuideOffer();
            return true;
        }

        public string CaptureSaveJson()
        {
            if (!GameSettings.HasActiveGame || ChoosingLanding) throw new InvalidOperationException("Land the colony ship before saving.");
            return JsonUtility.ToJson(new GameSaveData
            {
                version = 16,
                terrainVersion = planet.TerrainVersion,
                planetRadius = planet.Radius,
                terrainSettings = planet.TerrainVersion >= 3 ? planet.GenerationSettings : null,
                clock = clock.Snapshot,
                seed = GameSettings.CurrentSeed,
                difficulty = GameSettings.CurrentDifficulty,
                environment = environmentState.CreateSnapshot(),
                resources = resourceSystem.CreateSnapshot(),
                settlement = settlement.CreateSnapshot(),
                gridResolution = buildingPlacement.GridResolution,
                structures = buildingPlacement.CreateSnapshot(),
                events = colonyEventSystem.CreateSnapshot(),
                industry = industry.Capture(),
                research = research.Capture(),
                earthworks = earthworks.Capture()
            }, true);
        }

        public void SaveToFile()
        {
            if (!GameSettings.HasActiveGame || ChoosingLanding) return;
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

        public string SaveDirectoryPath
        {
            get
            {
                var directory = Application.persistentDataPath;
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                return directory;
            }
        }

        public string DefaultSaveFilePath => SavePath;

        public bool CanLoadSaveFile(string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
                    && TryPrepareSave(File.ReadAllText(path), out _, out _, out _);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                return false;
            }
        }

        public bool LoadFromFilePath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
                return TryLoadSaveJson(File.ReadAllText(path));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"Load failed: {exception.Message}");
                menu?.AppendEventLog("Could not read the selected save file.");
                return false;
            }
        }

        public bool TryLoadSaveJson(string json)
        {
            if (!TryPrepareSave(json, out var save, out var recoveredDozerHeading, out var error))
            {
                menu?.AppendEventLog("Load rejected: " + error);
                return false;
            }

            // Validate the whole document before replacing the running session.
            ChoosingLanding = false;
            menu?.ResetSessionUi();
            ResolveOrbitCamera();
            colonyEventSystem.EndSession();
            GameSettings.ApplyLoadedState(save.seed, save.difficulty);
            var definition = GameSettings.GetDefinition(save.difficulty);
            // Schemas through 8 used a 12 km sphere. Preserve their metric grid spacing,
            // excavations and free directions rather than shrinking an established colony.
            var planetRoot = BuildPlanet(save.seed, save.version < 3 ? 0 : save.terrainVersion,
                save.terrainSettings, save.version < 9 ? 12000f : save.planetRadius);
            if (orbitCamera != null) orbitCamera.SetTarget(planetRoot, planet.Radius);
            buildingPlacement.AttachToPlanet(planet, save.gridResolution == 0 ? 96 : save.gridResolution);
            resourceSystem.LoadFromSnapshot(save.resources, definition);
            environmentState.LoadFromSnapshot(save.environment, definition);
            // Install earthworks before reconstructing foundations and geological presentation.
            earthworks.Configure(planet, save.settlement.normal, save.earthworks, false);
            settlement.LoadFromSnapshot(save.settlement, planet, definition, resourceSystem, environmentState);
            if (settlement.ShipLayoutVersion >= 2) CreateOrbitalMainVessel(save.settlement.normal);
            else if (orbitalMainVessel != null) { Destroy(orbitalMainVessel); orbitalMainVessel = null; }
            var loadedNormal = settlement.CreateSnapshot().normal;
            planet.GetComponent<PlanetPresentation>().LightLandingSite(loadedNormal);
            orbitCamera?.SetColonyLocation(loadedNormal, settlement.HasExplorator
                ? (settlement.ShipLayoutVersion >= 2 ? ShipRules.Length : ShipRules.LegacyLength) * 1.3f : 220);
            buildingPlacement.RestoreSnapshot(save.structures);
            settlement.RefreshReadout();
            clock.Restore(save.clock);
            industry.Configure(planet, loadedNormal, save.industry);
            research.Configure(buildingPlacement, industry, save.version >= 16 ? save.research : null);
            if (save.version < 10) earthworks.Deploy();

            if (menu != null)
            {
                menu.ShowMainMenu(false);
                menu.SetSeed(save.seed);
                menu.SetDifficulty(save.difficulty);
                menu.ShowNewGamePanel(false);
                menu.ShowHud(true);
                menu.AppendEventLog("Game loaded. Buildings and economic effects restored.");
                if (recoveredDozerHeading) menu.AppendEventLog("Recovered the dozer's heading from an earlier large-world save.");
            }
            colonyEventSystem.BeginSession(definition, resourceSystem, environmentState, settlement, save.seed, save.events);
            return true;
        }

        private bool TryPrepareSave(string json, out GameSaveData save, out bool recoveredDozerHeading, out string error)
        {
            save = null;
            recoveredDozerHeading = false;
            error = "invalid save JSON.";
            try
            {
                save = JsonUtility.FromJson<GameSaveData>(json);
                if (save != null && save.version < 10) save.earthworks = null;
                if(save!=null && save.version>=10 && save.planetRadius>50000)
                    recoveredDozerHeading=EarthworksRules.RecoverZeroHeading(save.earthworks);
                if (save != null && save.version < 15) save.settlement.shipLayoutVersion = 0;
                if (save != null && save.version < 16) save.research = null;
                if (save?.industry != null)
                    save.industry.tenderLogistics = TenderLogisticsRules.NormalizeOptional(save.industry.tenderLogistics);
                // JsonUtility may create an empty nested object for a missing field in older JSON.
                if(save != null && save.version < 5) save.industry = null;
                if (save?.industry?.expeditionCargo != null && save.industry.expeditionCargo.Length == 0)
                    save.industry.expeditionCargo = null; // JsonUtility's representation of an absent legacy ship manifest.
                if(save != null && save.version < 6 && save.industry != null)
                {
                    save.industry.geologyVersion=0;save.industry.conflict=null;
                    if(save.industry.deposits!=null)for(var i=0;i<save.industry.deposits.Length;i++)save.industry.deposits[i].excavation=null;
                }
                if(save?.industry?.deposits!=null && save.industry.geologyVersion==1)
                    for(var i=0;i<save.industry.deposits.Length;i++)
                        if(save.industry.deposits[i].kind==(int)MaterialKind.Biomass)save.industry.deposits[i].excavation=null;
                if (save?.industry?.deposits != null)
                    for (var i = 0; i < save.industry.deposits.Length; i++)
                        if (save.industry.deposits[i].kind == (int)MaterialKind.Biomass || save.industry.deposits[i].kind == (int)MaterialKind.Promethium)
                            save.industry.deposits[i].excavation = MiningRules.NormalizeOptional(save.industry.deposits[i].excavation);
                return ValidateSave(save, out error);
            }
            catch (Exception)
            {
                save = null;
                recoveredDozerHeading = false;
                error = "invalid save JSON.";
                return false;
            }
        }

        private bool ValidateSave(GameSaveData save, out string error)
        {
            error = "Unsupported or incomplete save.";
            if (save == null || save.version < 0 || save.version > 16
                || save.version >= 16 && !ResearchRules.Valid(save.research)
                || save.version >= 10 && (save.earthworks == null || !EarthworksRules.Valid(save.earthworks))
                || save.version==7 && (save.industry==null || save.industry.materialVersion!=1 || save.industry.geologyVersion!=2)
                || save.version>=8 && (save.industry==null || save.industry.materialVersion!=(save.version>=13?2:1) || save.industry.geologyVersion!=(save.version>=12?4:3) || save.industry.explorationVersion!=ExplorationRules.LayoutVersion)
                || save.version==6 && (save.industry==null || save.industry.geologyVersion!=1)
                || save.terrainVersion < 0 || save.terrainVersion > (save.version >= 9 ? 3 : 2)
                || save.version >= 9 && (!ColonyRules.IsFinite(save.planetRadius) || save.planetRadius < 1000 || save.planetRadius > PlanetScale.MaximumRadius
                    || save.terrainVersion >= 3 && (!TerrainSettings.Valid(save.terrainSettings) || save.planetRadius != save.terrainSettings.radius))
                || !ColonyCalendar.Valid(save.clock)
                || !IndustryRules.Valid(save.industry, save.structures != null ? save.structures.Length : 0)
                || !Enum.IsDefined(typeof(GameSettings.Difficulty), save.difficulty)) return false;
            var colony = save.settlement;
            if (colony.shipLayoutVersion < 0 || colony.shipLayoutVersion > 2
                || save.version < 15 && colony.shipLayoutVersion != 0
                || save.version < 16 && colony.shipLayoutVersion > 1
                || colony.shipLayoutVersion >= 2 && (save.industry?.tenderLogistics == null || !save.industry.tenderLogistics.initialLoadoutApplied)
                || colony.shipLayoutVersion > 0 && (colony.buildings == null || colony.buildings.Length != 1 || colony.buildings[0] != "ColonyShip")) return false;
            var resources = save.resources;
            var environment = save.environment;
            var values = new[]
            {
                colony.population, colony.workforce, colony.productionModifier, colony.upkeepModifier,
                colony.industryModifier, colony.tickTimer, colony.position.x, colony.position.y, colony.position.z,
                colony.normal.x, colony.normal.y, colony.normal.z,
                resources.Population, resources.Workforce, resources.Stockpile, resources.Water, resources.ProductionPerCycle,
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
                || !ColonyRules.IsFinite(resources.Food) || resources.Food < 0 || resources.Food > FarmRules.FoodCapacity
                || resources.FoodVersion < 0 || resources.FoodVersion > 1 || save.version >= 13 && resources.FoodVersion != 1
                || resources.Stockpile < 0f || resources.Water < 0f || resources.Water > ResourceSystem.WaterCapacity
                || resources.WaterVersion < 0 || resources.WaterVersion > 1 || resources.Population < 0f || resources.Workforce < 0f
                || save.version >= 11 && resources.WaterVersion != 1
                || environment.Co2 < 0f || environment.O2 < 0f || environment.WaterPollution < 0f
                || !EventSchedule.IsValid(save.events)) return false;
            if (colony.buildings != null)
            {
                if (colony.buildings.Length > 4096) return false;
                foreach (var id in colony.buildings)
                    if (id != "HabBlock" && id != "Factory" && id != "Utility" && (id != "ColonyShip" || save.version < 12)) return false;
                if (Array.IndexOf(colony.buildings, "ColonyShip") >= 0
                    && (colony.buildings.Length != 1 || save.industry?.expeditionCargo == null)) return false;
            }
            if (save.industry?.conflict != null && save.industry.conflict.active && save.industry.conflict.core
                && save.industry.conflict.building >= (colony.buildings != null && colony.buildings.Length > 0 ? colony.buildings.Length : 3)) return false;
            if (colony.operations != null)
            {
                if (colony.operations.Length != (colony.buildings != null && colony.buildings.Length > 0 ? colony.buildings.Length : 3)) return false;
                foreach (var operation in colony.operations) if (!BuildingOperationRules.Valid(operation)
                    || !ResidentialDevelopment.ValidateLoaded(operation.residential)) return false;
            }
            if(save.structures!=null)foreach(var structure in save.structures)
                if(!ResidentialDevelopment.ValidateLoaded(structure.operation.residential))return false;
            var identityTypes=new System.Collections.Generic.List<string>();var identityStates=new System.Collections.Generic.List<BuildingOperationState>();
            if(colony.operations!=null)for(var i=0;i<colony.operations.Length;i++)
            {identityTypes.Add(colony.buildings!=null && colony.buildings.Length>0?colony.buildings[i]:new[]{"HabBlock","Factory","Utility"}[i]);identityStates.Add(colony.operations[i]);}
            if(save.structures!=null)foreach(var structure in save.structures){identityTypes.Add(structure.id);identityStates.Add(structure.operation);}
            if(!FacilityIdentityRules.ValidCollection(identityTypes.ToArray(),identityStates.ToArray(),save.version>=14))return false;
            if(save.industry!=null)
            {
                var pendingFood=0f;foreach(var stock in save.industry.facilities)pendingFood+=stock.food;
                foreach(var cargo in save.industry.cargo)if(cargo.food)
                {if(save.structures==null || save.structures[cargo.source].id!="Farm")return false;pendingFood+=cargo.amount;}
                if(pendingFood+resources.Food>FarmRules.FoodCapacity+.01f)return false;
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

        private Transform BuildPlanet(int seed, int terrainVersion = 3, TerrainSettings savedSettings = null, float savedRadius = 0)
        {
            if (planet != null)
            {
                planet.gameObject.SetActive(false);
                Destroy(planet.gameObject);
            }

            var planetGO = new GameObject("Planet");
            planetGO.transform.SetParent(transform, false);
            planet = planetGO.AddComponent<LODPlanet>();
            var settings = (savedSettings ?? terrainSettings).Copy();
            var radius = savedRadius > 0 ? savedRadius : planetRadius;
            if (terrainVersion >= 3)
            {
                settings.radius = radius;
                // Fresh expeditions get a seed-stable planetary climate and landform profile.
                // Customized recipes and loaded saves remain exact and are never re-rolled.
                if (savedSettings == null && TerrainSettings.IsDefaultRecipe(settings))
                    settings = TerrainSettings.ForNewWorld(seed, settings);
            }
            planet.ApplyConfiguration(radius, basePatchResolution, maximumDepth, splitDistance * radius / 6000f, splitFalloff,
                lodUpdateInterval, maxPatchResolution, targetTriangleArea);
            planet.BuildPlanet(seed, terrainMaterialInstance, terrainVersion, settings);
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

        private void OnDestroy()
        {
            if (terrainMaterialInstance != null) Destroy(terrainMaterialInstance);
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
            public float planetRadius;
            public TerrainSettings terrainSettings;
            public ClockSnapshot clock;
            public int seed;
            public GameSettings.Difficulty difficulty;
            public EnvironmentSnapshot environment;
            public ResourceSnapshot resources;
            public SettlementSnapshot settlement;
            public int gridResolution;
            public PlacedStructureSnapshot[] structures;
            public EventSnapshot events;
            public IndustrySnapshot industry;
            public ResearchSnapshot research;
            public EarthworksSnapshot earthworks;
        }
    }
}
