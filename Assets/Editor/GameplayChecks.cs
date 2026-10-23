#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Sim.Environment;
using WH30K.Sim.Events;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;
using WH30K.CameraSystem;
using WH30K.Sim;
using WH30K.UI;

// Runs against the real scene, materials, UI, JsonUtility and placement controller.
// Checks replace the world temporarily, then restore the active colony without changing its disk save.
public static class GameplayChecks
{
    private static int checks;

    [MenuItem("WH30K/Back Up Active Colony (Play Mode) %&b")]
    public static void BackUpActiveColony()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        var bootstrap = UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        var json = CaptureReviewColony(bootstrap);
        if (json == null) throw new InvalidOperationException("No active colony to back up.");
        WriteReviewBackup(json);
        Debug.Log("Review colony backed up to .utmp/colony-before-gameplay-checks.json.");
    }

    private static string CaptureReviewColony(PlanetBootstrap bootstrap)
    {
        if (bootstrap == null || bootstrap.ActivePlanet == null) return null;
        // Editor script recompilation resets static settings while retaining scene components.
        if (!GameSettings.HasActiveGame)
            GameSettings.ApplyLoadedState(bootstrap.ActivePlanet.Seed,
                bootstrap.GetComponent<EnvironmentState>().Difficulty);
        var saved = JsonUtility.FromJson<ReviewSave>(bootstrap.CaptureSaveJson());
        var anchor = bootstrap.ActivePlanet.transform.Find("SettlementAnchor");
        if (saved.settlement.buildings.Length == 0 && anchor != null)
        {
            var core = anchor.GetComponentsInChildren<ManagedBuilding>();
            saved.settlement.buildings = Array.ConvertAll(core, building => building.Id);
            saved.settlement.operations = Array.ConvertAll(core, building => building.State);
        }
        var placedRoot = bootstrap.ActivePlanet.transform.Find("PlacedStructures");
        if (saved.structures.Length == 0 && placedRoot != null)
        {
            var grid = new PlanetGrid(bootstrap.ActivePlanet, saved.gridResolution);
            var placed = new List<PlacedStructureSnapshot>();
            foreach (var building in placedRoot.GetComponentsInChildren<ManagedBuilding>())
            {
                var cell = grid.GetCellFromWorld(building.transform.position);
                placed.Add(new PlacedStructureSnapshot { id = building.Id, face = cell.Face,
                    x = cell.X, y = cell.Y, operation = building.State });
            }
            saved.structures = placed.ToArray();
        }
        if (!saved.events.initialized && bootstrap.GetComponent<NewGameMenu>().HasOpenEvent)
        {
            var schedule = new EventSchedule(saved.seed, GameSettings.GetDefinition(saved.difficulty).eventFrequencyMultiplier);
            schedule.TriggerNow(); saved.events = schedule.CreateSnapshot();
        }
        return JsonUtility.ToJson(saved, true);
    }

    // Editor-only recovery DTO: dictionaries and readonly lists do not survive script hot reload.
    [Serializable]
    private class ReviewSave
    {
        public int version, terrainVersion, seed, gridResolution;
        public ClockSnapshot clock;
        public GameSettings.Difficulty difficulty;
        public EnvironmentSnapshot environment;
        public ResourceSnapshot resources;
        public SettlementSnapshot settlement;
        public PlacedStructureSnapshot[] structures;
        public EventSnapshot events;
    }

    private static void WriteReviewBackup(string json)
    {
        var directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".utmp");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "colony-before-gameplay-checks.json"), json);
    }

    [MenuItem("WH30K/Run Gameplay Checks (Play Mode) %&g")]
    public static void Run()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Open WH30K_VS01, enter Play Mode, then run these checks.");
        var bootstrap = UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        if (bootstrap == null) throw new InvalidOperationException("Open the WH30K_VS01 scene first.");
        var priorColony = CaptureReviewColony(bootstrap);
        if (priorColony != null) WriteReviewBackup(priorColony);
        try { RunChecks(bootstrap); }
        finally
        {
            if (priorColony != null && !bootstrap.TryLoadSaveJson(priorColony))
                Debug.LogError("Could not restore the review colony. Its JSON backup is in .utmp/colony-before-gameplay-checks.json.");
        }
    }

    private static void RunChecks(PlanetBootstrap bootstrap)
    {
        checks = 0;
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard);
        var planet = bootstrap.ActivePlanet;
        Check(Camera.main != null && !Camera.main.GetComponent<SimpleOrbitCamera>().IsSurfaceView
            && Camera.main.transform.position.magnitude > planet.Radius * 2f,
            "new worlds open in a free planetary view rather than a locked building view");
        Check(Mathf.Abs(Camera.main.projectionMatrix.determinant) > 0.00001f,
            "camera has a valid perspective projection for rendering the world");
        Check(planet.Radius == 12000f && planet.transform.Find("Ocean") != null
            && planet.transform.Find("Cloud layer") != null && planet.transform.Find("Atmospheric scattering") != null,
            "larger planet includes ocean, clouds and atmosphere");
        foreach (var id in new[] { "HabBlock", "Factory", "Utility", "Outpost" })
        {
            var visual = BuildingVisuals.Create(id, "VisualCheck", bootstrap.transform, Vector3.one);
            var mesh = visual.GetComponent<MeshFilter>().sharedMesh;
            Check(mesh.vertexCount > 500 && mesh.vertexCount < 20000 && mesh.subMeshCount == 7
                && mesh.bounds.size.x <= 1.001f && mesh.bounds.size.z <= 1.001f
                && mesh.bounds.max.y <= 1.01f && mesh.bounds.min.y >= -0.05f,
                id + " detailed mesh stays inside its placement footprint and geometry budget (vertices="
                    + mesh.vertexCount + ", bounds=" + mesh.bounds + ")");
            UnityEngine.Object.Destroy(visual);
        }
        var placement = bootstrap.GetComponent<BuildingPlacementController>();
        var resources = bootstrap.GetComponent<ResourceSystem>();
        var events = bootstrap.GetComponent<ColonyEventSystem>();
        var settlement = bootstrap.GetComponent<Settlement>();
        var environment = bootstrap.GetComponent<EnvironmentState>();
        Check(settlement.CoreBuildings[0].transform.localScale.y < 40f,
            "architecture uses the reduced planetary building scale");
        var orbit = Camera.main.GetComponent<SimpleOrbitCamera>();
        orbit.FocusSettlement(settlement.CreateSnapshot().normal);
        var previousDirection = orbit.NavigationDirection;
        orbit.Navigate(Vector2.right, 3f);
        Check(Vector3.Distance(previousDirection, orbit.NavigationDirection) > 0.001f,
            "surface navigation moves freely away from the colony");
        Check(placement.SelectStructure("Outpost"), "outpost asset is available");
        var before = resources.CreateSnapshot();
        Check(PlaceFirstAvailable(placement), "place an outpost on real terrain");
        var built = placement.CreateSnapshot()[0];
        var after = resources.CreateSnapshot();
        Check(Mathf.Abs(before.Stockpile - after.Stockpile - 250f) < 0.01f, "placement pays exactly once");
        Check(after.ProductionPerCycle > before.ProductionPerCycle, "placement changes production immediately");
        Check(!placement.TryPlaceCell(new PlanetGridCell(built.face, built.x, built.y)), "occupied cell cannot be rebuilt");
        Check(resources.Stockpile == after.Stockpile, "failed placement does not charge");
        placement.SelectStructure("Factory");
        Check(!PlaceFirstAvailable(placement) && placement.CreateSnapshot().Length == 1,
            "unaffordable factory cannot be built anywhere");
        Check(resources.Stockpile == after.Stockpile, "unaffordable factory does not charge");

        events.TriggerDebugEventNow();
        var json = bootstrap.CaptureSaveJson();
        Check(bootstrap.TryLoadSaveJson(json), "complete game loads from JsonUtility JSON");
        Check(bootstrap.ActivePlanet.TerrainVersion == 1, "new saves retain the new terrain recipe");
        var loaded = placement.CreateSnapshot();
        Check(loaded.Length == 1 && loaded[0].id == built.id && loaded[0].face == built.face
            && loaded[0].x == built.x && loaded[0].y == built.y, "building identity and occupied cell restored");
        Check(Mathf.Abs(resources.Stockpile - after.Stockpile) < 0.01f
            && Mathf.Abs(resources.CreateSnapshot().ProductionPerCycle - after.ProductionPerCycle) < 0.01f,
            "load neither charges again nor loses economic effects");
        Check(events.CreateSnapshot().awaitingChoice, "pending event remains pending after load");
        Check(bootstrap.TryLoadSaveJson(json) && placement.CreateSnapshot().Length == 1
            && Mathf.Abs(resources.CreateSnapshot().ProductionPerCycle - after.ProductionPerCycle) < 0.01f,
            "repeated loads do not duplicate buildings or production");
        Check(!bootstrap.TryLoadSaveJson("{\"version\":99}") && placement.CreateSnapshot().Length == 1,
            "invalid save leaves the running colony intact");
        var saveButton = GameObject.Find("SaveButton").GetComponent<Button>();
        var loadButton = GameObject.Find("HudLoadButton").GetComponent<Button>();
        Check(saveButton.isActiveAndEnabled && saveButton.interactable && loadButton.isActiveAndEnabled,
            "save and load are accessible during play");
        var choice = GameObject.Find("ChoiceA").GetComponent<Button>();
        var money = resources.Stockpile;
        choice.onClick.Invoke();
        Check(!events.CreateSnapshot().awaitingChoice && Mathf.Abs(resources.Stockpile - money + 60f) < 0.01f,
            "safety decision pays its cost and resumes the event timer");
        choice.onClick.Invoke();
        Check(Mathf.Abs(resources.Stockpile - money + 60f) < 0.01f, "decision cannot charge twice");

        var legacy = new LegacySave
        {
            seed = 1234, difficulty = GameSettings.Difficulty.Standard,
            settlement = settlement.CreateSnapshot(), resources = resources.CreateSnapshot(),
            environment = environment.CreateSnapshot()
        };
        legacy.settlement.industryModifier = 0f;
        // Recorded from the original 3,000-unit seed-1234 world, before the terrain update.
        legacy.settlement.normal = new Vector3(-0.895445645f, 0.301881015f, 0.327177197f);
        legacy.settlement.position = legacy.settlement.normal.normalized * 3000f;
        Check(bootstrap.TryLoadSaveJson(JsonUtility.ToJson(legacy)) && placement.CreateSnapshot().Length == 0,
            "original prototype save format remains loadable");
        Check(Mathf.Abs(settlement.CreateSnapshot().position.magnitude
            - bootstrap.ActivePlanet.EvaluateSurfacePoint(legacy.settlement.normal).magnitude) < 0.1f,
            "old-radius settlement save is reprojected onto the larger terrain");
        Check(settlement.CreateSnapshot().position.magnitude >= bootstrap.ActivePlanet.SeaLevel,
            "a recorded original colony site remains on land after migration");
        Check(bootstrap.ActivePlanet.TerrainVersion == 0 && bootstrap.TryLoadSaveJson(bootstrap.CaptureSaveJson())
            && bootstrap.ActivePlanet.TerrainVersion == 0,
            "old terrain recipe and coastlines survive migration and a second save/load");
        var selection = bootstrap.GetComponent<BuildingSelection>();
        var menu = bootstrap.GetComponent<NewGameMenu>();
        var hab = settlement.CoreBuildings[0];
        selection.Select(hab);
        Check(menu.InspectedBuilding == hab && GameObject.Find("BuildingInspector") != null,
            "selecting a building opens its own operational menu");
        var originalCapacity = hab.Economy.capacity;
        GameObject.Find("BuildingFocus1").GetComponent<Button>().onClick.Invoke();
        Check(hab.State.focus == 1 && hab.Economy.capacity > originalCapacity,
            "housing focus button changes the actual building capacity");
        var upgradeBalance = resources.Stockpile;
        var upgradeCost = hab.UpgradeCost;
        GameObject.Find("UpgradeBuilding").GetComponent<Button>().onClick.Invoke();
        Check(hab.State.level == 1 && Mathf.Abs(resources.Stockpile - upgradeBalance + upgradeCost) < 0.01f,
            "building upgrade button pays exactly once and advances the tier");
        GameObject.Find("ToggleBuildingOperation").GetComponent<Button>().onClick.Invoke();
        Check(hab.State.offline && hab.Economy.capacity == 0 && hab.Economy.upkeep == 0,
            "suspending a building removes its economic effects");
        GameObject.Find("ToggleBuildingOperation").GetComponent<Button>().onClick.Invoke();
        Check(!hab.State.offline && hab.Economy.capacity > originalCapacity,
            "resuming a building restores its focused upgraded capacity");
        var clock = bootstrap.GetComponent<ColonyClock>();
        clock.Restore(new ClockSnapshot { simulationSeconds = 1950, playedSeconds = 900, speed = 3, paused = true });
        Check(bootstrap.TryLoadSaveJson(bootstrap.CaptureSaveJson())
            && settlement.CoreBuildings[0].State.level == 1 && settlement.CoreBuildings[0].State.focus == 1
            && clock.Snapshot.simulationSeconds == 1950 && clock.Snapshot.playedSeconds == 900
            && clock.Speed == 3 && clock.Paused,
            "building settings, calendar, real play time, speed and pause survive save/load");
        clock.SetSpeed(1);
        Debug.Log($"WH30K gameplay checks: {checks} passed. Inspect camera, layout and terrain visually as well.");
    }

    private static bool PlaceFirstAvailable(BuildingPlacementController placement)
    {
        for (var face = 0; face < 6; face++)
            for (var x = 0; x < placement.GridResolution; x += 8)
                for (var y = 0; y < placement.GridResolution; y += 8)
                    if (placement.TryPlaceCell(new PlanetGridCell(face, x, y))) return true;
        return false;
    }

    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException("Gameplay check failed: " + message);
        checks++;
        Debug.Log("PASS: " + message);
    }

    [Serializable]
    private class LegacySave
    {
        public int seed;
        public GameSettings.Difficulty difficulty;
        public SettlementSnapshot settlement;
        public ResourceSnapshot resources;
        public EnvironmentSnapshot environment;
    }
}
#endif
