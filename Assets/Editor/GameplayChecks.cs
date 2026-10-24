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
using WH30K.Planet;

// Runs against the real scene, materials, UI, JsonUtility and placement controller.
// Checks replace the world temporarily, then restore the active colony without changing its disk save.
public static class GameplayChecks
{
    private static int checks;
    private const string ReloadBackupKey="WH30K.ColonyBeforeScriptReload";
    static GameplayChecks()
    {
        AssemblyReloadEvents.beforeAssemblyReload += () =>
        {
            if(!Application.isPlaying || !GameSettings.HasActiveGame)return;
            var bootstrap=UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();if(bootstrap==null)return;
            try { var json=bootstrap.CaptureSaveJson();SessionState.SetString(ReloadBackupKey,json);WriteReviewBackup(json); }
            catch(Exception error) { Debug.LogWarning("Colony backup before script reload failed: "+error.Message); }
        };
    }

    [MenuItem("WH30K/Build Review Player %&k")]
    public static void BuildReviewPlayer()
    {
        if(Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before building.");
        var directory=Path.Combine(Directory.GetParent(Application.dataPath).FullName,".utmp","ReviewPlayer");
        Directory.CreateDirectory(directory);
        var report=UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions
        {
            scenes=new[]{"Assets/Scenes/Prototype/WH30K_VS01.unity"},
            locationPathName=Path.Combine(directory,"WH30K Colony.exe"),
            target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development
        });
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new InvalidOperationException("Review player build failed: "+report.summary.result);
        Debug.Log("WH30K review player build succeeded: "+directory);
    }

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
        var beforeReload=SessionState.GetString(ReloadBackupKey,"");
        if(!GameSettings.HasActiveGame && !string.IsNullOrEmpty(beforeReload))return beforeReload;
        SessionState.EraseString(ReloadBackupKey);
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
                var n=bootstrap.ActivePlanet.transform.InverseTransformPoint(building.transform.position).normalized;
                placed.Add(new PlacedStructureSnapshot { id = building.Id, face = cell.Face,
                    x = cell.X, y = cell.Y, operation = building.State, freePlacement=true,nx=n.x,ny=n.y,nz=n.z });
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
        public IndustrySnapshot industry;
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
        foreach (var id in new[] { "HabBlock", "Factory", "Utility", "Outpost", "Mine", "Quarry", "Harvester", "Smelter", "Works" })
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
        Check(bootstrap.ActivePlanet.TerrainVersion == 2, "new saves retain the varied terrain recipe");
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
        RunIndustryChecks(bootstrap);
        Debug.Log($"WH30K gameplay checks: {checks} passed. Inspect camera, layout and terrain visually as well.");
    }

    private static void RunIndustryChecks(PlanetBootstrap bootstrap)
    {
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard);
        var planet=bootstrap.ActivePlanet;var resources=bootstrap.GetComponent<ResourceSystem>();
        var placement=bootstrap.GetComponent<BuildingPlacementController>();var industry=bootstrap.GetComponent<IndustrySystem>();
        var colony=bootstrap.GetComponent<Settlement>().CreateSnapshot().normal;
        var biomes=new HashSet<string>();var low=float.MaxValue;var high=float.MinValue;var water=Vector3.zero;
        for(var i=0;i<512;i++)
        {
            var y=1-2*(i+.5f)/512;var a=i*2.39996323f;var r=Mathf.Sqrt(1-y*y);var n=new Vector3(Mathf.Cos(a)*r,y,Mathf.Sin(a)*r);
            var height=planet.EvaluateSurfacePoint(n).magnitude;low=Mathf.Min(low,height);high=Mathf.Max(high,height);biomes.Add(planet.Biome(n));
            if(height<planet.SeaLevel-15)water=n;
        }
        Check(high-low>400 && biomes.Count>=4,"new geography has meaningful relief and multiple climate biomes");
        Check(planet.GetComponent<PlanetarySystem>()!=null && planet.transform.Find("Moon - Selene")!=null
            && planet.transform.Find("Moon - Cinder")!=null && planet.transform.Find("Sun - Helios")!=null,
            "sun and two orbiting moons are present in the actual world");
        resources.ModifyStockpile(100000);
        placement.SelectStructure("Outpost");
        Check(water.sqrMagnitude>.9f && placement.TryPlaceDirection(water),"free placement accepts ocean sites with supports");
        Check(!placement.TryPlaceDirection(water),"overlapping free placement is blocked");
        var free=placement.CreateSnapshot()[0];
        Check(free.freePlacement && Mathf.Abs(new Vector3(free.nx,free.ny,free.nz).magnitude-1)<.001f,"free sites save their exact surface direction");
        var origin=planet.transform.TransformPoint(planet.EvaluateSurfacePoint(colony));
        foreach(var kind in new[]{MaterialKind.IronOre,MaterialKind.Crystal,MaterialKind.Stone,MaterialKind.Biomass})
        {
            var deposit=industry.NearestDeposit(origin,kind);
            Check(deposit!=null,"geological survey contains "+IndustryRules.Name(kind));
            var id=kind==MaterialKind.Stone?"Quarry":kind==MaterialKind.Biomass?"Harvester":"Mine";
            Check(PlaceNear(placement,id,deposit.Direction,planet.Radius),id+" can be deployed near its resource catchment");
            if(kind==MaterialKind.Crystal) placement.Buildings[placement.Buildings.Length-1].SetFocus(2);
        }
        Check(PlaceNear(placement,"Smelter",colony,planet.Radius,125),"smelter is available through construction");
        Check(PlaceNear(placement,"Works",colony,planet.Radius,200),"fabrication works is available through construction");
        var beforeMoney=resources.Stockpile;
        for(var step=0;step<220;step++)industry.Simulate(10);
        Check(resources.Stockpile>beforeMoney,"extraction, hauler arrivals, smelting and fabrication deliver components to the core factory for credits");
        var snapshot=industry.Capture();var depleted=false;foreach(var deposit in snapshot.deposits) if(deposit.remaining<2000)depleted=true;
        Check(depleted,"extraction actually depletes geological deposits");
        var ironMine=placement.Buildings[1];ironMine.ToggleOperation();
        var depositBefore=industry.NearestDeposit(ironMine.transform.position,MaterialKind.IronOre);var remaining=depositBefore.Remaining;
        industry.Simulate(100);
        Check(depositBefore.Remaining==remaining,"suspending an extractor stops consumption of its deposit");
        ironMine.ToggleOperation();industry.Simulate(10);
        snapshot=industry.Capture();
        Check(IndustryRules.Valid(snapshot,placement.Buildings.Length),"live industry state including cargo fits the save validation rules");
        var serialized=JsonUtility.ToJson(snapshot);
        Check(bootstrap.TryLoadSaveJson(bootstrap.CaptureSaveJson()),"free placements and production chains load through the complete save path");
        Check(JsonUtility.ToJson(industry.Capture())==serialized,"facility stocks, work progress, deposit depletion and in-flight cargo survive loading exactly");
        var loaded=placement.CreateSnapshot()[0];
        Check(loaded.freePlacement && loaded.nx==free.nx && loaded.ny==free.ny && loaded.nz==free.nz,"ocean construction retains its exact free placement after load");
        industry.SetOverlay(1);Camera.main.GetComponent<SimpleOrbitCamera>().FocusSettlement(colony);
    }

    private static bool PlaceNear(BuildingPlacementController placement,string id,Vector3 normal,float radius,float distance=0)
    {
        placement.SelectStructure(id);var tangent=Vector3.Cross(normal,Mathf.Abs(normal.y)>.95f?Vector3.right:Vector3.up).normalized;
        var second=Vector3.Cross(normal,tangent);
        for(var i=0;i<16;i++)
        {var angle=i*Mathf.PI/8;var range=distance+i*5;var n=(normal+(tangent*Mathf.Cos(angle)+second*Mathf.Sin(angle))*range/radius).normalized;
            if(placement.TryPlaceDirection(n))return true;}
        return false;
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
