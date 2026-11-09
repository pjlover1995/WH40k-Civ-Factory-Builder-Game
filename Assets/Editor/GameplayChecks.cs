#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
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
    private const string RuntimeShaders="Assets/Scenes/Resources/WH30K/RuntimeShaders";
    [MenuItem("WH30K/Prepare Runtime Shader References")]
    public static void PrepareRuntimeShaderReferences()
    {
        if(!AssetDatabase.IsValidFolder(RuntimeShaders))AssetDatabase.CreateFolder("Assets/Scenes/Resources/WH30K","RuntimeShaders");
        var names=new[]{"StandardOpaque","StandardEmission","UnlitColor","SpriteRoute"};
        var shaders=new[]{"Standard","Standard","Unlit/Color","Sprites/Default"};
        for(var i=0;i<names.Length;i++)
        {
            var path=RuntimeShaders+"/"+names[i]+".mat";
            if(AssetDatabase.LoadAssetAtPath<Material>(path)!=null)continue;
            var material=new Material(Shader.Find(shaders[i])){enableInstancing=i<2};
            if(i==1){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",Color.white);}
            AssetDatabase.CreateAsset(material,path);
        }
        // All renderers are created at runtime, so scene analysis cannot discover instancing.
        var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var stripping=settings.FindProperty("m_InstancingStripping");
        // Unity 2022 serializes this internal enum as an integer: 0 unused, 1 all, 2 keep all.
        stripping.intValue=2;settings.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
        Debug.Log("Runtime shader references prepared; instancing variants retained for procedural buildings.");
    }
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

    [MenuItem("WH30K/Review Excavation Test World %&m")]
    public static void ReviewExcavation() => ReviewMiningFixture("excavation-review-fixture.json");
    [MenuItem("WH30K/Review Mining Decision Test World %&n")]
    public static void ReviewMiningDecision() => ReviewMiningFixture("excavation-conflict-fixture.json");
    private static void ReviewMiningFixture(string name)
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Enter Play Mode and run gameplay checks first.");
        var bootstrap=UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        var file=Path.Combine(Directory.GetParent(Application.dataPath).FullName,".utmp",name);
        if(!File.Exists(file) || !bootstrap.TryLoadSaveJson(File.ReadAllText(file)))throw new InvalidOperationException("Run gameplay checks to generate this review fixture.");
        var placement=bootstrap.GetComponent<BuildingPlacementController>();var mine=placement.Buildings[0];
        var industry=bootstrap.GetComponent<IndustrySystem>();var deposit=industry.DepositFor(mine);
        industry.ShowExcavationOutline(deposit);industry.SetOverlay(2);
        Camera.main.GetComponent<SimpleOrbitCamera>().FocusLocation(deposit.Direction);
        bootstrap.GetComponent<NewGameMenu>().ShowBuilding(mine);
        bootstrap.GetComponent<NewGameMenu>().AppendEventLog("Temporary mining test world. F9 restores your disk colony; avoid saving over it.");
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
        if (!saved.events.initialized && bootstrap.GetComponent<NewGameMenu>().HasNarrativeEvent)
        {
            var schedule = new EventSchedule(saved.seed, GameSettings.GetDefinition(saved.difficulty).eventFrequencyMultiplier, enabled: true);
            schedule.TriggerNow(); saved.events = schedule.CreateSnapshot();
        }
        return JsonUtility.ToJson(saved, true);
    }

    // Editor-only recovery DTO: dictionaries and readonly lists do not survive script hot reload.
    [Serializable]
    internal class ReviewSave
    {
        public int version, terrainVersion, seed, gridResolution;
        public float planetRadius;
        public TerrainSettings terrainSettings;
        public ClockSnapshot clock;
        public GameSettings.Difficulty difficulty;
        public EnvironmentSnapshot environment;
        public ResourceSnapshot resources;
        public SettlementSnapshot settlement;
        public PlacedStructureSnapshot[] structures;
        public EventSnapshot events;
        public IndustrySnapshot industry;
        public ResearchSnapshot research;
        public EarthworksSnapshot earthworks;
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
        var priorTerrain = bootstrap.NewWorldTerrain;
        if (priorColony != null) WriteReviewBackup(priorColony);
        try { bootstrap.ConfigureNewWorldTerrain(new TerrainSettings { radius=6000 }); RunChecks(bootstrap); }
        finally
        {
            bootstrap.ConfigureNewWorldTerrain(priorTerrain);
            if (priorColony != null && !bootstrap.TryLoadSaveJson(priorColony))
                Debug.LogError("Could not restore the review colony. Its JSON backup is in .utmp/colony-before-gameplay-checks.json.");
        }
    }

    private static void RunChecks(PlanetBootstrap bootstrap)
    {
        checks = 0;
        var timer=System.Diagnostics.Stopwatch.StartNew();
        ArchitectureAppearanceChecks.Run();
        LandingSceneChecks.RunGameplay(bootstrap,Check);
        TerrainSceneChecks.RunGameplay(bootstrap);
        EarthworksSceneChecks.RunGameplay(bootstrap,Check);
        OutputTooltipSceneChecks.RunGameplay(bootstrap,Check);
        FacilitySceneChecks.RunGameplay(bootstrap,Check);
        FarmSceneChecks.RunGameplay(bootstrap,Check);
        FarmEstateSceneChecks.RunGameplay(bootstrap,Check);
        MiningSceneChecks.RunGameplay(bootstrap,Check);
        bootstrap.ConfigureNewWorldTerrain(new TerrainSettings { radius=6000 });
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard, legacyCore: true);
        FundConstruction(bootstrap);
        var planet = bootstrap.ActivePlanet;
        Check(Camera.main != null && !Camera.main.GetComponent<SimpleOrbitCamera>().IsSurfaceView
            && Camera.main.transform.position.magnitude > planet.Radius * 2f,
            "new worlds open in a free planetary view rather than a locked building view");
        Check(Mathf.Abs(Camera.main.projectionMatrix.determinant) > 0.00001f,
            "camera has a valid perspective projection for rendering the world");
        Check(planet.Radius == 6000f && planet.transform.Find("Ocean") != null
            && planet.transform.Find("Cloud layer") != null && planet.transform.Find("Atmospheric scattering") != null,
            "larger planet includes ocean, clouds and atmosphere");
        foreach (var id in new[] { "HabBlock", "Factory", "Utility", "Outpost", "Mine", "Quarry", "Harvester", "Smelter", "Works",
            "PromethiumExtractor", "PromethiumGenerator", "Refinery", "Manufactorum", "StorageDepot", "GroundwaterPump", "DesalinationPump", "Cognitorium" })
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
        Check(settlement.Population > 0f
            && Mathf.Abs(settlement.Population - settlement.CreateSnapshot().population) < 0.001f,
            "water demand reads the live settlement population");
        var waterBefore = resources.Water;
        var waterDemand = WaterRules.Demand(settlement.Population);
        var waterIndustry = bootstrap.GetComponent<IndustrySystem>();
        waterIndustry.Simulate(0f);
        Check(resources.Water == waterBefore, "zero simulation time consumes no colony water");
        waterIndustry.Simulate(2f);
        Check(Mathf.Abs(resources.Water - (waterBefore - waterDemand * 2f)) < 0.001f,
            "industry consumes the population's exact water demand for elapsed simulation time");
        Check(settlement.CoreBuildings[0].transform.localScale.y < 40f,
            "architecture uses the reduced planetary building scale");
        var orbit = Camera.main.GetComponent<SimpleOrbitCamera>();
        orbit.FocusSettlement(settlement.CreateSnapshot().normal);
        var previousDirection = orbit.NavigationDirection;
        orbit.Navigate(Vector2.right, 3f);
        Check(Vector3.Distance(previousDirection, orbit.NavigationDirection) > 0.001f,
            "surface navigation moves freely away from the colony");
        Check(placement.SelectStructure("Outpost"), "outpost asset is available");
        FundConstruction(bootstrap, Array.Find(Resources.LoadAll<BuildableStructure>("Buildings"),d=>d.Id=="Outpost").ConstructionSupplies);
        var suppliesBefore=waterIndustry.Totals()[(int)MaterialKind.Components];
        var before = resources.CreateSnapshot();
        Check(PlaceFirstAvailable(placement), "place an outpost on real terrain");
        var built = placement.CreateSnapshot()[0];
        var after = resources.CreateSnapshot();
        Check(before.Stockpile == after.Stockpile && Math.Abs(waterIndustry.Totals()[(int)MaterialKind.Components]-suppliesBefore
            +Array.Find(Resources.LoadAll<BuildableStructure>("Buildings"),d=>d.Id=="Outpost").ConstructionSupplies)<.001f, "placement debits physical construction cargo exactly once");
        Check(after.ProductionPerCycle > before.ProductionPerCycle, "placement changes production immediately");
        Check(!placement.TryPlaceCell(new PlanetGridCell(built.face, built.x, built.y)), "occupied cell cannot be rebuilt");
        Check(resources.Stockpile == after.Stockpile, "failed placement does not charge");
        placement.SelectStructure("Factory");
        Check(!PlaceFirstAvailable(placement) && placement.CreateSnapshot().Length == 1,
            "unaffordable factory cannot be built anywhere");
        Check(resources.Stockpile == after.Stockpile, "unaffordable factory does not charge");

        events.TriggerDebugEventNow();
        Check(!bootstrap.GetComponent<NewGameMenu>().HasNarrativeEvent && !events.CreateSnapshot().awaitingChoice,
            "disabled priority transmissions ignore the debug trigger");
        var pendingTransmission = JsonUtility.FromJson<ReviewSave>(bootstrap.CaptureSaveJson());
        pendingTransmission.events.awaitingChoice = true;
        var json = JsonUtility.ToJson(pendingTransmission);
        File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName,".utmp","checks-save-roundtrip.json"),json);
        Check(bootstrap.TryLoadSaveJson(json), "complete game loads from JsonUtility JSON");
        Check(bootstrap.ActivePlanet.TerrainVersion == 3 && bootstrap.ActivePlanet.Radius == 6000,
            "new saves retain the hybrid terrain recipe and 6000-metre radius");
        var loaded = placement.CreateSnapshot();
        Check(loaded.Length == 1 && loaded[0].id == built.id && loaded[0].face == built.face
            && loaded[0].x == built.x && loaded[0].y == built.y, "building identity and occupied cell restored");
        Check(Mathf.Abs(resources.Stockpile - after.Stockpile) < 0.01f
            && Mathf.Abs(resources.CreateSnapshot().ProductionPerCycle - after.ProductionPerCycle) < 0.01f,
            "load neither charges again nor loses economic effects");
        Check(events.CreateSnapshot().awaitingChoice && !bootstrap.GetComponent<NewGameMenu>().HasNarrativeEvent
            && !bootstrap.GetComponent<ColonyClock>().DecisionPending,
            "saved priority transmission remains pending without showing a modal or pausing play");
        Check(bootstrap.TryLoadSaveJson(json) && placement.CreateSnapshot().Length == 1
            && Mathf.Abs(resources.CreateSnapshot().ProductionPerCycle - after.ProductionPerCycle) < 0.01f,
            "repeated loads do not duplicate buildings or production");
        Check(!bootstrap.TryLoadSaveJson("{\"version\":99}") && placement.CreateSnapshot().Length == 1,
            "invalid save leaves the running colony intact");
        var saveButton = GameObject.Find("SaveButton").GetComponent<Button>();
        var loadButton = GameObject.Find("HudLoadButton").GetComponent<Button>();
        Check(saveButton.isActiveAndEnabled && saveButton.interactable && loadButton.isActiveAndEnabled,
            "save and load are accessible during play");
        var money = resources.Stockpile;
        events.TriggerDebugEventNow();
        Check(events.CreateSnapshot().awaitingChoice && resources.Stockpile == money
            && !bootstrap.GetComponent<NewGameMenu>().HasNarrativeEvent,
            "debug trigger cannot show or charge a dormant saved transmission");

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
        ResidentialSceneChecks.RunGameplay(bootstrap,Check);
        hab=settlement.CoreBuildings[0];selection.Select(hab);
        GameObject.Find("ToggleBuildingOperation").GetComponent<Button>().onClick.Invoke();
        Check(hab.State.offline && hab.Economy.capacity == 0 && hab.Economy.upkeep == 0,
            "suspending a building removes its economic effects");
        GameObject.Find("ToggleBuildingOperation").GetComponent<Button>().onClick.Invoke();
        Check(!hab.State.offline && hab.Economy.capacity > originalCapacity,
            "resuming a building restores its focused upgraded capacity");
        var clock = bootstrap.GetComponent<ColonyClock>();
        clock.Restore(new ClockSnapshot { simulationSeconds = 1950, playedSeconds = 900, speed = 3, paused = true });
        Check(bootstrap.TryLoadSaveJson(bootstrap.CaptureSaveJson())
            && settlement.CoreBuildings[0].State.level == 2 && settlement.CoreBuildings[0].State.focus == 1
            && clock.Snapshot.simulationSeconds == 1950 && clock.Snapshot.playedSeconds == 900
            && clock.Speed == 3 && clock.Paused,
            "building settings, calendar, real play time, speed and pause survive save/load");
        clock.SetSpeed(1);
        RunExplorationChecks(bootstrap);
        RunIndustryChecks(bootstrap);
        RunMiningChecks(bootstrap);
        RunPerformanceRegressions(bootstrap);
        Debug.Log($"WH30K gameplay checks: {checks} passed in {timer.Elapsed.TotalSeconds:0.0}s. Inspect camera, layout and terrain visually as well.");
    }

    internal static void RunPerformanceRegressions(PlanetBootstrap bootstrap)
    {
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard, legacyCore: true);
        FundConstruction(bootstrap);
        bootstrap.GetComponent<ColonyEventSystem>().EndSession();
        var placement=bootstrap.GetComponent<BuildingPlacementController>();var industry=bootstrap.GetComponent<IndustrySystem>();
        var resources=bootstrap.GetComponent<ResourceSystem>();resources.ModifyStockpile(100000);
        var normal=bootstrap.GetComponent<Settlement>().CreateSnapshot().normal;var planet=bootstrap.ActivePlanet;
        var shaderAssets=UnityEngine.Resources.LoadAll<Material>("WH30K/RuntimeShaders");
        Check(shaderAssets.Length==5&&Array.TrueForAll(shaderAssets,m=>m.shader!=null)
            &&Array.Exists(shaderAssets,m=>m.name=="StandardCutout"&&m.IsKeywordEnabled("_ALPHATEST_ON")),"build includes Standard cutout foliage, opaque, emission, unlit and route shader variants");
        var graphics=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var instancing=graphics.FindProperty("m_InstancingStripping");
        Check(instancing.intValue==2,"player builds retain instancing variants for renderers created at runtime");
        Check(Vector3.Dot(-RenderSettings.sun.transform.forward,planet.GetComponent<PlanetarySystem>().SunDirection)>.999f,
            "landing-site lighting agrees with the orbital sun before the first paused frame");
        var validNormals=true;var maxSurfaceError=0f;var roundTripError=0f;
        var faces=new[]{Vector3.up,Vector3.down,Vector3.left,Vector3.right,Vector3.forward,Vector3.back};
        for(var face=0;face<faces.Length;face++)
        {
            var patch=planet.transform.Find("Patch_"+face);
            var mesh=patch.GetComponent<MeshFilter>().sharedMesh;var vertices=mesh.vertices;var normals=mesh.normals;var uv=mesh.uv;
            var up=faces[face];var axisA=new Vector3(up.y,up.z,-up.x).normalized;var axisB=Vector3.Cross(up,axisA).normalized;
            var resolution=Mathf.RoundToInt(Mathf.Sqrt(vertices.Length+8))-2;
            for(var i=0;i<resolution*resolution;i++)
            {
                if(!ColonyRules.IsFinite(normals[i].sqrMagnitude)||Mathf.Abs(normals[i].sqrMagnitude-1)>.01f||Vector3.Dot(normals[i],vertices[i])<=0)validNormals=false;
                var direction=(up+(uv[i].x-.5f)*2*axisA+(uv[i].y-.5f)*2*axisB).normalized;
                maxSurfaceError=Mathf.Max(maxSurfaceError,Vector3.Distance(vertices[i],planet.EvaluateSurfacePoint(direction)));
                roundTripError=Mathf.Max(roundTripError,Vector3.Distance(vertices[i],planet.EvaluateSurfacePoint(vertices[i].normalized)));
            }
        }
        Check(validNormals,"terrain patch normals remain finite, normalized and outward after reusing neighbour samples");
        Check(maxSurfaceError<.001f,"optimized terrain retains original heightfield samples (error "+maxSurfaceError+" m; renormalized vertex round-trip "+roundTripError+" m)");
        var empty=placement.Buildings;
        Check(ReferenceEquals(empty,placement.Buildings),"stationary building queries reuse the registry");
        Check(PlaceNear(placement,"Works",normal,planet.Radius,220),"transport regression facility can be placed");
        var first=placement.Buildings;var works=first[0];
        Check(!ReferenceEquals(empty,first)&&ReferenceEquals(first,placement.Buildings),"construction invalidates the registry once and keeps its order");
        foreach(var material in works.GetComponent<Renderer>().sharedMaterials)
            Check(material.enableInstancing,"shared architecture material enables GPU instancing: "+material.name);
        var saved=industry.Capture();saved.facilities[0].quantities[6]=3;
        saved.cargo=new[]{new CargoStock{source=0,target=-1,kind=6,amount=1,progress=.25f}};
        industry.Configure(planet,normal,saved);works.ToggleOperation();var money=resources.Stockpile;
        industry.Simulate(100);
        Check(industry.CargoCount==0&&industry.CargoVisualCount==1&&resources.Stockpile==money+1,"arriving cargo delivers once and retains its visual for reuse");
        works.ToggleOperation();industry.Simulate(1);
        Check(industry.CargoCount==1&&industry.CargoVisualCount==1,"the next dispatch reuses the existing hauler and route renderer");
        foreach(var line in planet.GetComponentsInChildren<LineRenderer>(true))if(line.name=="Cargo route")
        {
            var clear=true;
            for(var i=0;i<line.positionCount-1;i++)
            {
                var point=planet.transform.InverseTransformPoint(Vector3.Lerp(line.GetPosition(i),line.GetPosition(i+1),.5f));
                if(point.magnitude<planet.EvaluateSurfacePoint(point.normalized).magnitude+1)clear=false;
            }
            Check(clear,"cached transport segments clear the terrain between samples");
        }
        Check(placement.Demolish(works),"transport regression facility can be removed");
        Check(PlaceNear(placement,"Works",normal,planet.Radius,220),"a same-count replacement can be built");
        var replaced=placement.Buildings;
        Check(!ReferenceEquals(first,replaced)&&replaced.Length==first.Length,"same-count replacements invalidate the building registry");
        var replacedStock=industry.Capture();
        Check(replacedStock.facilities[0].quantities[6]==0&&industry.CargoCount==0,"replacement clears the removed inventory and linked cargo even when the count is unchanged");
        var json=bootstrap.CaptureSaveJson();Check(bootstrap.TryLoadSaveJson(json),"optimized registries and transport preserve save compatibility");
        Check(!ReferenceEquals(replaced,placement.Buildings)&&ReferenceEquals(placement.Buildings,placement.Buildings),"loading rebuilds and then reuses the ordered registry");
        RunGeneratorDispatchChecks(bootstrap);
    }

    private static void RunGeneratorDispatchChecks(PlanetBootstrap bootstrap)
    {
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard, legacyCore: true);
        FundConstruction(bootstrap);
        var placement=bootstrap.GetComponent<BuildingPlacementController>();
        var industry=bootstrap.GetComponent<IndustrySystem>();var settlement=bootstrap.GetComponent<Settlement>();
        var resources=bootstrap.GetComponent<ResourceSystem>();var planet=bootstrap.ActivePlanet;
        var normal=settlement.CreateSnapshot().normal;
        Check(PlaceNear(placement,"PromethiumGenerator",normal,planet.Radius,260)
            &&PlaceNear(placement,"PromethiumGenerator",normal,planet.Radius,380)
            &&PlaceNear(placement,"Refinery",normal,planet.Radius,500),"two-generator dispatch fixture can be constructed");
        foreach(var core in settlement.CoreBuildings)core.ToggleOperation();
        var workers=resources.CreateSnapshot();workers.Workforce=10000;
        resources.LoadFromSnapshot(workers,GameSettings.GetDefinition(GameSettings.Difficulty.Standard));
        var saved=industry.Capture();
        saved.facilities[0].quantities[(int)MaterialKind.Promethium]=.0006f;
        saved.facilities[1].quantities[(int)MaterialKind.Promethium]=100;
        industry.Configure(planet,normal,saved);industry.Simulate(.5f);
        var after=industry.Capture();
        Check(Mathf.Abs(industry.PowerDemand-10)<.0001f && industry.OperatingFraction(placement.Buildings[2])>.999f,
            "a nearly empty generator and a supplied generator jointly power the real ten-MW network");
        Check(after.facilities[0].quantities[(int)MaterialKind.Promethium]<.000001f
            &&Mathf.Abs(100-after.facilities[1].quantities[(int)MaterialKind.Promethium]-.0294f)<.00001f,
            "live dispatch charges the second generator for all load the first cannot supply, including trace fuel");
        industry.Simulate(0);
        Check(EquivalentIndustryJson(JsonUtility.ToJson(after),JsonUtility.ToJson(industry.Capture())),
            "paused generator dispatch preserves inventory and statistics");
        var json=bootstrap.CaptureSaveJson();
        Check(bootstrap.TryLoadSaveJson(json),"corrected generator fuel accounting survives the existing save schema");
    }

    private static void RunExplorationChecks(PlanetBootstrap bootstrap)
    {
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard, legacyCore: true);
        FundConstruction(bootstrap);
        bootstrap.GetComponent<ColonyEventSystem>().EndSession();
        var industry=bootstrap.GetComponent<IndustrySystem>();var placement=bootstrap.GetComponent<BuildingPlacementController>();
        var resources=bootstrap.GetComponent<ResourceSystem>();var menu=bootstrap.GetComponent<NewGameMenu>();var planet=bootstrap.ActivePlanet;
        var colony=bootstrap.GetComponent<Settlement>().CreateSnapshot().normal;
        Check(industry.DiscoveredCount==0&&industry.ExploredFraction==0,"new planets hide discoveries and start with an unexplored survey map");
        var definitions=UnityEngine.Resources.LoadAll<DiscoveryDefinition>("Discoveries");
        Check(definitions.Length==4&&Array.TrueForAll(definitions,d=>ExplorationRules.ValidProfile(d.Rewards)),"all four discovery definitions load with configured rewards");
        resources.ModifyStockpile(10000);
        Check(PlaceNear(placement,"Outpost",colony,planet.Radius,180),"a survey outpost can be constructed near the landing site");
        industry.SurveyNow();
        Check(industry.DiscoveredCount>0&&industry.DiscoveredCount<industry.DiscoveryCount&&industry.ExploredFraction>0,"outpost survey reveals a local region while remote discoveries stay hidden");
        var snapshot=industry.Capture();var site=Array.Find(snapshot.discoveries,d=>d.discovered&&!d.resolved&&d.kind==(int)DiscoveryKind.AncientRuins);
        Check(site.discovered,"the landing region contains an accessible ruin expedition");
        menu.SetEventChoiceAInteractable(false);industry.SelectDiscovery(site.id);
        Check(menu.HasOpenEvent&&GameObject.Find("DiscoveryAction").GetComponent<Button>().interactable,"a discovered site opens the functional inspection panel");
        GameObject.Find("DiscoveryAction").GetComponent<Button>().onClick.Invoke();
        var safeChoice=GameObject.Find("ChoiceA").GetComponent<Button>();
        Check(safeChoice.interactable,"discovery choices reset a previous event's unaffordable-button state");
        resources.TrySpend(10); // Leave physical storage room for the recovery award.
        var before=resources.Stockpile;safeChoice.onClick.Invoke();
        var resolved=Array.Find(industry.Capture().discoveries,d=>d.id==site.id);
        Check(resolved.resolved&&resolved.outcomeRecorded&&Mathf.Abs(resources.Stockpile-before-resolved.rewardReceived*ColonyRules.LegacyProjectSupplyScale)<.01f,"the investigation applies and records its exact physical supply reward once");
        Check(!GameObject.Find("DiscoveryAction").GetComponent<Button>().interactable,"a resolved site's action is disabled while its outcome remains inspectable");
        var coverage=industry.ExploredFraction;var json=bootstrap.CaptureSaveJson();
        Check(bootstrap.TryLoadSaveJson(json)&&industry.ExploredFraction==coverage&&!menu.HasOpenEvent,"survey coverage survives full save/load and stale discovery callbacks close");
        resolved=Array.Find(industry.Capture().discoveries,d=>d.id==site.id);
        Check(resolved.outcomeRecorded&&resolved.rewardReceived>0,"the recorded expedition outcome survives JsonUtility save/load");
        placement.Buildings[0].ToggleOperation();industry.SurveyNow();
        Check(industry.ExploredFraction==coverage&&industry.DiscoveredCount>0,"suspending the survey outpost preserves charted territory and discoveries");
    }

    internal static void RunDiscoveryMarkerChecks(PlanetBootstrap bootstrap,Action<bool,string> check)
    {
        if(bootstrap==null||check==null)throw new ArgumentNullException(bootstrap==null?nameof(bootstrap):nameof(check));
        var priorColony=CaptureReviewColony(bootstrap);var priorTerrain=bootstrap.NewWorldTerrain;
        try
        {
            bootstrap.ConfigureNewWorldTerrain(new TerrainSettings{radius=6000});
            bootstrap.BeginNewGame(1234,GameSettings.Difficulty.Standard,legacyCore:true);
            FundConstruction(bootstrap);
            bootstrap.GetComponent<ColonyEventSystem>().EndSession();
            var industry=bootstrap.GetComponent<IndustrySystem>();
            var placement=bootstrap.GetComponent<BuildingPlacementController>();
            var resources=bootstrap.GetComponent<ResourceSystem>();
            var planet=bootstrap.ActivePlanet;
            var colony=bootstrap.GetComponent<Settlement>().CreateSnapshot().normal;
            resources.ModifyStockpile(10000);
            check(PlaceNear(placement,"Outpost",colony,planet.Radius,180),"a real survey outpost can be built for the discovery-pin fixture");
            industry.SurveyNow();
            var site=Array.Find(industry.Capture().discoveries,d=>d.discovered&&!d.resolved&&d.kind==(int)DiscoveryKind.AncientRuins);
            check(site.discovered,"the survey reveals an unresolved ruin for pin visibility and location checks");

            var internalDiscoveries=typeof(IndustrySystem).GetField("discoveries",BindingFlags.Instance|BindingFlags.NonPublic);
            Transform siteMarker=null;
            if(internalDiscoveries?.GetValue(industry) is System.Collections.IEnumerable discoveries)
                foreach(var item in discoveries)
                {
                    var stateField=item.GetType().GetField("State");var markerField=item.GetType().GetField("Marker");
                    if(stateField?.GetValue(item) is DiscoveryRecord state&&state.id==site.id)
                    {siteMarker=markerField?.GetValue(item) as Transform;break;}
                }
            check(siteMarker!=null,"the discovered ruin owns its expected scene marker");
            if(siteMarker==null)throw new InvalidOperationException("The discovered ruin marker fixture could not be found.");

            var orbit=Camera.main.GetComponent<SimpleOrbitCamera>();
            orbit.FrameTarget();RefreshDiscoveryMarkers(industry);
            check(!siteMarker.gameObject.activeSelf,"discovered-site pins stay hidden in the orbital overview");
            CaptureDiscoveryMarkerView(Camera.main,"orbit");

            var direction=new Vector3(site.x,site.y,site.z).normalized;
            orbit.FocusLocation(direction);RefreshDiscoveryMarkers(industry);
            check(orbit.IsSurfaceView&&siteMarker.gameObject.activeSelf,"focusing a discovered site into surface mode reveals its pin");
            CaptureDiscoveryMarkerView(Camera.main,"close-ground");
            orbit.Zoom(-.5f);RefreshDiscoveryMarkers(industry);
            check(orbit.IsSurfaceView&&siteMarker.gameObject.activeSelf,"discovered-site pins remain visible while zoomed out to the regional view");
            CaptureDiscoveryMarkerView(Camera.main,"regional");

            orbit.FrameTarget();RefreshDiscoveryMarkers(industry);
            var sitesButton=GameObject.Find("DiscoveryStatus")?.GetComponent<Button>();
            sitesButton?.onClick.Invoke();
            check(!orbit.IsSurfaceView&&GameObject.Find("DiscoveryPanel")?.activeSelf==true,
                "the SITES browser opens while the camera remains in Orbit");
            var details=GameObject.Find("DiscoveryDetails")?.GetComponent<Text>();
            var next=GameObject.Find("NextDiscovery")?.GetComponent<Button>();
            var expected=$"Survey site {site.id+1:000}";
            for(var i=0;i<industry.DiscoveredCount&&details!=null&&next!=null&&!details.text.Contains(expected);i++)next.onClick.Invoke();
            check(details!=null&&details.text.Contains(expected),"the SITES browser can select the discovered ruin from Orbit");
            GameObject.Find("LocateDiscovery")?.GetComponent<Button>()?.onClick.Invoke();
            check(orbit.IsSurfaceView&&Vector3.Angle(orbit.NavigationDirection,direction)<.01f,
                "LOCATE closes the browser and focuses the selected discovered site");
            RefreshDiscoveryMarkers(industry);
            check(siteMarker.gameObject.activeSelf,"the selected site's pin remains visible after browser focus");
        }
        finally
        {
            bootstrap.ConfigureNewWorldTerrain(priorTerrain);
            if(priorColony!=null&&!bootstrap.TryLoadSaveJson(priorColony))
                Debug.LogError("Could not restore the colony after discovery marker checks.");
        }
    }

    private static void RefreshDiscoveryMarkers(IndustrySystem industry)
    {
        var timer=typeof(IndustrySystem).GetField("surveyTimer",BindingFlags.Instance|BindingFlags.NonPublic);
        var update=typeof(IndustrySystem).GetMethod("UpdateSurvey",BindingFlags.Instance|BindingFlags.NonPublic);
        if(timer!=null)timer.SetValue(industry,1f);
        if(update==null)throw new MissingMethodException(typeof(IndustrySystem).FullName,"UpdateSurvey");
        update.Invoke(industry,null);
    }

    private static void CaptureDiscoveryMarkerView(Camera camera,string name)
    {
        var directory=Path.Combine(".utmp","discovery-marker-review");Directory.CreateDirectory(directory);
        var target=camera.targetTexture;var active=RenderTexture.active;var aspect=camera.aspect;
        var texture=new RenderTexture(1280,800,24);texture.Create();
        var image=new Texture2D(1280,800,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=texture;camera.aspect=1280f/800f;
            camera.Render();RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(directory,name+".png"),image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture=target;camera.aspect=aspect;RenderTexture.active=active;
            texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(image);
        }
    }

    internal static void RunIndustryChecks(PlanetBootstrap bootstrap)
    {
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard, legacyCore: true);
        FundConstruction(bootstrap);
        var planet=bootstrap.ActivePlanet;var resources=bootstrap.GetComponent<ResourceSystem>();
        var placement=bootstrap.GetComponent<BuildingPlacementController>();var industry=bootstrap.GetComponent<IndustrySystem>();
        // Isolate industrial transport/recipes from the separate population-growth test.
        var settlement=bootstrap.GetComponent<Settlement>();var staffed=settlement.CreateSnapshot();
        staffed.population=2000;staffed.workforce=1240;
        settlement.LoadFromSnapshot(staffed,planet,GameSettings.GetDefinition(GameSettings.Difficulty.Standard),resources,bootstrap.GetComponent<EnvironmentState>());
        settlement.RefreshReadout();
        var colony=bootstrap.GetComponent<Settlement>().CreateSnapshot().normal;
        // Industrial and excavation fixtures are charted; exploration is checked separately above.
        foreach(var deposit in industry.Deposits)deposit.Discovered=true;
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
        foreach(var kind in new[]{MaterialKind.IronOre,MaterialKind.Crystal,MaterialKind.Stone,MaterialKind.Biomass,MaterialKind.Promethium})
        {
            var deposit=industry.NearestDeposit(origin,kind);
            Check(deposit!=null,"geological survey contains "+IndustryRules.Name(kind));
            var id=kind==MaterialKind.Stone?"Quarry":kind==MaterialKind.Biomass?"Harvester":kind==MaterialKind.Promethium?"PromethiumExtractor":"Mine";
            Check(PlaceNear(placement,id,deposit.Direction,planet.Radius),id+" can be deployed near its resource catchment");
            if(id=="Mine") Check(industry.DepositFor(placement.Buildings[placement.Buildings.Length-1])?.Kind==kind,
                id+" automatically extracts the mineral at its placement without a focus selection");
        }
        Check(PlaceNear(placement,"Smelter",colony,planet.Radius,125),"smelter is available through construction");
        Check(PlaceNear(placement,"Works",colony,planet.Radius,200),"fabrication works is available through construction");
        var structureIds=Array.ConvertAll(UnityEngine.Resources.LoadAll<WH30K.Gameplay.Construction.BuildableStructure>("Buildings"),d=>d.Id);
        var profiles=UnityEngine.Resources.LoadAll<IndustrialBuildingProfile>("Industry");
        Check(Array.Exists(structureIds,id=>id=="PromethiumGenerator")&&Array.Exists(structureIds,id=>id=="Refinery")
            &&Array.Exists(structureIds,id=>id=="Manufactorum")&&Array.Exists(structureIds,id=>id=="StorageDepot")
            &&Array.Exists(profiles,p=>p.BuildingId=="Refinery")&&Array.Exists(profiles,p=>p.BuildingId=="Manufactorum")
            &&Array.Exists(profiles,p=>p.BuildingId=="PromethiumGenerator")&&Array.Exists(profiles,p=>p.BuildingId=="PromethiumExtractor")
            &&Array.Exists(profiles,p=>p.BuildingId=="StorageDepot"),"industrial construction and all new profile ScriptableObjects load from Resources");
        var fuelDeposit=industry.NearestDeposit(origin,MaterialKind.Promethium);
        var fuelExtractor=Array.Find(placement.Buildings,b=>b.Id=="PromethiumExtractor");
        var ironExtractor=Array.Find(placement.Buildings,b=>b.Id=="Mine"&&b.State.focus==0);
        Check(PlaceNear(placement,"PromethiumGenerator",colony,planet.Radius,520),"promethium generator can be constructed");
        Check(PlaceNear(placement,"Refinery",colony,planet.Radius,360),"promethium refinery can be constructed");
        Check(PlaceNear(placement,"Manufactorum",colony,planet.Radius,680),"manufactorum can be constructed");
        Check(PlaceNear(placement,"StorageDepot",colony,planet.Radius,920),"storage depot can be constructed");
        ConnectLogisticsToCore(placement,planet,industry,fuelExtractor.transform.position,origin);
        ConnectLogisticsToCore(placement,planet,industry,ironExtractor.transform.position,origin);
        var refinery=Array.Find(placement.Buildings,b=>b.Id=="Refinery");
        Check(industry.Status(refinery).Contains("LOGISTICS NETWORK: CONNECTED"),"storage relays connect remote extractors to the core network");
        var beforeMoney=resources.Stockpile;
        var fueled=industry.Capture();
        fueled.facilities[Array.FindIndex(placement.Buildings,b=>b.Id=="PromethiumGenerator")].quantities[(int)MaterialKind.Promethium]=120;
        // Prove the refinery's actual atomic recipe independently of which nearby
        // consumer wins scarce fuel while this deliberately overloaded grid starts.
        var refineryStock=fueled.facilities[Array.IndexOf(placement.Buildings,refinery)];
        refineryStock.quantities[(int)MaterialKind.IronOre]=6;
        refineryStock.quantities[(int)MaterialKind.Promethium]=3;
        industry.Configure(planet,colony,fueled);
        for(var step=0;step<220;step++){if(industry.HasMiningDecision)industry.ResolveMiningConflict(false);industry.Simulate(10);}
        Check(resources.Stockpile>beforeMoney,"extraction, hauler arrivals, smelting and fabrication replenish usable physical construction supplies");
        var totals=industry.Totals();
        Check(industry.Capture().producedTotals[(int)MaterialKind.Promethium]>0&&totals[(int)MaterialKind.Plasteel]>0
            &&industry.PowerGenerated>0&&industry.WorkersRequired>0,"promethium extraction, fuelled generation, refining and staffed power demand feed the industrial chain");
        var snapshot=industry.Capture();var depleted=false;foreach(var deposit in snapshot.deposits) if(deposit.excavation!=null && Array.Exists(deposit.excavation.removed,v=>v>0))depleted=true;
        Check(snapshot.producedTotals[(int)MaterialKind.Promethium]>0&&snapshot.producedTotals[(int)MaterialKind.Plasteel]>0
            &&snapshot.consumedTotals[(int)MaterialKind.Promethium]>0&&snapshot.productionRates.Length==IndustryRules.MaterialCount,
            "saved production and consumption totals track actual operating chains");
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
        Check(EquivalentIndustryJson(JsonUtility.ToJson(industry.Capture()),serialized),"facility stocks, work progress, deposit depletion and in-flight cargo survive loading");
        var loaded=placement.CreateSnapshot()[0];
        Check(loaded.freePlacement && loaded.nx==free.nx && loaded.ny==free.ny && loaded.nz==free.nz,"ocean construction retains its exact free placement after load");
        industry.SetOverlay(1);Camera.main.GetComponent<SimpleOrbitCamera>().FocusSettlement(colony);
    }

    internal static void RunMiningChecks(PlanetBootstrap bootstrap)
    {
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard, legacyCore: true);
        FundConstruction(bootstrap);
        var planet=bootstrap.ActivePlanet;var industry=bootstrap.GetComponent<IndustrySystem>();var placement=bootstrap.GetComponent<BuildingPlacementController>();
        var resources=bootstrap.GetComponent<ResourceSystem>();var clock=bootstrap.GetComponent<ColonyClock>();var menu=bootstrap.GetComponent<NewGameMenu>();
        resources.ModifyStockpile(100000);
        foreach(var deposit in industry.Deposits)deposit.Discovered=true;
        SurfaceDeposit chosen=null;ManagedBuilding mine=null;
        // This fixture deliberately exercises legacy directional decisions and pillar saves.
        foreach(var deposit in industry.Deposits)if(deposit.Excavation!=null)deposit.Excavation.layoutVersion=0;
        foreach(var deposit in industry.Deposits)
        {
            if(deposit.Kind!=MaterialKind.IronOre)continue;
            var far=true;foreach(var core in bootstrap.GetComponent<Settlement>().CoreBuildings)
                if(Vector3.Distance(core.transform.position,deposit.Position)<450)far=false;
            if(!far)continue;
            var right=Vector3.Cross(deposit.Direction,Mathf.Abs(deposit.Direction.y)>.95f?Vector3.right:Vector3.up).normalized;
            var forward=Vector3.Cross(deposit.Direction,right);var angle=deposit.Excavation.bearing*Mathf.Deg2Rad;
            var along=right*Mathf.Cos(angle)+forward*Mathf.Sin(angle);
            placement.SelectStructure("Mine");
            if(!placement.TryPlaceDirection((deposit.Direction-along*150/planet.Radius).normalized))continue;
            mine=placement.Buildings[placement.Buildings.Length-1];
            chosen=deposit;
            var definition=Array.Find(UnityEngine.Resources.LoadAll<BuildableStructure>("Buildings"),d=>d.Id=="Outpost");
            var distance=32+new Vector2(definition.Dimensions.x,definition.Dimensions.z).magnitude*.5f+17;
            placement.SelectStructure("Outpost");
            Check(placement.TryPlaceDirection((deposit.Direction+along*distance/planet.Radius).normalized),"neighbouring facility can be placed ahead of a future mine cut");
            break;
        }
        Check(chosen!=null && mine!=null,"mining test has an isolated iron ore body and an actual mine");
        // This isolated historical mine needs a real finite-fuel local power source.
        var mineDirection=planet.transform.InverseTransformPoint(mine.transform.position).normalized;
        var away=((mineDirection-chosen.Direction)*planet.Radius).normalized;
        Check(PlaceNear(placement,"PromethiumGenerator",(chosen.Direction+away*350/planet.Radius).normalized,planet.Radius),
            "legacy cut fixture has a local generator outside the mineral body");
        var startup=industry.Capture();startup.facilities[2].quantities[(int)MaterialKind.Promethium]=120;
        var chosenId=chosen.Id;industry.Configure(planet,bootstrap.GetComponent<Settlement>().CreateSnapshot().normal,startup);
        chosen=industry.Deposits[chosenId];
        industry.Simulate(1);Check(industry.OperatingFraction(mine)>0,"finite local fuel powers the isolated legacy mine");
        var original=chosen.Remaining;var outpost=placement.Buildings[1];
        for(var step=0;step<1200 && !industry.HasMiningDecision;step++)industry.Simulate(10);
        Check(industry.HasMiningDecision && chosen.Remaining<original && placement.Buildings.Length==3,
            "expanding a working pit detects its neighbour before demolishing or undermining it");
        var pending=industry.PendingConflict;var before=chosen.Remaining;
        industry.Simulate(500);
        Check(clock.Paused && chosen.Remaining==before,"an unanswered mining decision pauses time and all further extraction");
        Check(IndustryRules.Valid(industry.Capture(),3),"conflict interruption leaves partial work and industrial state valid");
        var json=bootstrap.CaptureSaveJson();var exact=JsonUtility.ToJson(industry.Capture());
        File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName,".utmp","excavation-conflict-fixture.json"),json);
        Check(bootstrap.TryLoadSaveJson(json) && EquivalentIndustryJson(JsonUtility.ToJson(industry.Capture()),exact) && industry.HasMiningDecision,
            "complete save/load restores exact benches, reserves and the pending expansion decision");
        planet=bootstrap.ActivePlanet;chosen=(SurfaceDeposit)industry.Deposits[pending.deposit];mine=placement.Buildings[0];
        Check(industry.ResolveMiningConflict(false) && placement.Buildings.Length==3 && (chosen.Excavation.blocked&1<<pending.sector)!=0,
            "limiting a direction protects the facility and marks that ore inaccessible");
        Check(MiningRules.Remaining(chosen.Excavation,true)<chosen.Remaining,"directional limits reduce the accessible geological reserve");
        var rightAxis=Vector3.Cross(chosen.Direction,Mathf.Abs(chosen.Direction.y)>.95f?Vector3.right:Vector3.up).normalized;
        var forwardAxis=Vector3.Cross(chosen.Direction,rightAxis);
        var workedSector=Array.FindIndex(chosen.Excavation.removed,amount=>amount>0);
        var cutAngle=(chosen.Excavation.bearing+workedSector*45)*Mathf.Deg2Rad;
        var sample=(chosen.Direction+(rightAxis*Mathf.Cos(cutAngle)+forwardAxis*Mathf.Sin(cutAngle))*20/planet.Radius).normalized;
        Check(planet.EvaluateSurfacePoint(sample).magnitude<planet.EvaluateBaseHeight(sample)-.5f
            && planet.transform.Find("Geological survey and cargo/Excavation benches - survey "+chosen.Id)!=null,
            "excavation lowers the actual heightfield and creates a rendered terraced mine mesh");
        var limitJson=bootstrap.CaptureSaveJson();Check(bootstrap.TryLoadSaveJson(limitJson)
            && (industry.Deposits[pending.deposit].Excavation.blocked&1<<pending.sector)!=0,"protected mining directions survive the full persistence path");
        planet=bootstrap.ActivePlanet;chosen=(SurfaceDeposit)industry.Deposits[pending.deposit];mine=placement.Buildings[0];
        industry.ToggleSector(mine,pending.sector);
        for(var step=0;step<100 && !industry.HasMiningDecision;step++)industry.Simulate(10);
        Check(industry.HasMiningDecision,"reopening a protected direction checks the threatened facility again");
        // Add an unaffected facility after the threatened index; verify demolition remaps its stocks and cargo.
        Check(PlaceNear(placement,"Smelter",chosen.Direction,planet.Radius,210),"a later-index facility is available for demolition remapping checks");
        var saved=industry.Capture();saved.facilities[3].quantities[5]=7;
        saved.cargo=new[]{new CargoStock{source=0,target=3,kind=0,amount=2,progress=.25f},new CargoStock{source=0,target=1,kind=0,amount=1,progress=.1f}};
        // Configure is the same restore implementation used by the bootstrap after document validation.
        industry.Configure(planet,bootstrap.GetComponent<Settlement>().CreateSnapshot().normal,saved);
        Check(industry.ResolveMiningConflict(true) && placement.Buildings.Length==3 && placement.Buildings[2].Id=="Smelter",
            "demolition removes only the chosen facility and retains later buildings");
        var remapped=industry.Capture();
        Check(remapped.facilities[2].quantities[5]==7 && remapped.cargo.Length==1 && remapped.cargo[0].target==2 && IndustryRules.Valid(remapped,3),
            "demolition keeps unaffected inventory and cargo, remaps endpoints and removes linked shipments");
        Check(!industry.ResolveMiningConflict(true),"the same demolition decision cannot execute twice");
        saved=industry.Capture();var nextSector=MiningRules.NextSector(saved.deposits[pending.deposit].excavation);
        saved.conflict=new MiningConflict{active=true,core=true,deposit=pending.deposit,sector=nextSector,mine=0,building=0};
        industry.Configure(planet,bootstrap.GetComponent<Settlement>().CreateSnapshot().normal,saved);
        menu.PromptEvent("Decision arbitration check", "Shared decision UI fixture", "A", () => { }, "B", () => { });
        Check(!menu.CanPresentMiningDecision && menu.HasNarrativeEvent && industry.HasMiningDecision,
            "narrative and mining decisions remain separate and retain both unanswered choices");
        Check(!industry.ResolveMiningConflict(true) && industry.HasMiningDecision,"core facilities cannot be demolished by an expansion decision");
        Check(industry.ResolveMiningConflict(false) && clock.Paused && menu.HasNarrativeEvent,
            "limiting a core-facing cut preserves the narrative decision and its pause");
        // Start again from the post-demolition industrial state to make the review fixture independent of the narrative test.
        saved.conflict=new MiningConflict();industry.Configure(planet,bootstrap.GetComponent<Settlement>().CreateSnapshot().normal,saved);
        bootstrap.GetComponent<ColonyEventSystem>().EndSession();
        saved=industry.Capture();var deepBody=saved.deposits[pending.deposit].excavation;
        for(var s=0;s<8;s++)deepBody.removed[s]=deepBody.capacity[s]*.68f;
        saved.deposits[pending.deposit].remaining=MiningRules.Remaining(deepBody);
        industry.Configure(planet,bootstrap.GetComponent<Settlement>().CreateSnapshot().normal,saved);
        chosen=(SurfaceDeposit)industry.Deposits[pending.deposit];
        placement.SelectStructure("Utility");
        Check(placement.TryPlaceDirection(chosen.Direction),"construction can occupy a worked mine floor when no building overlaps");
        var foundation=placement.Buildings[3].transform.position;
        Check(foundation.magnitude<planet.EvaluateBaseHeight(chosen.Direction)-4,"a facility in a deep pit uses the excavated ground elevation");
        var floorSave=bootstrap.CaptureSaveJson();
        Check(bootstrap.TryLoadSaveJson(floorSave) && Vector3.Distance(placement.Buildings[3].transform.position,foundation)<.01f,
            "loading retains foundation elevation inside excavated ground");
        planet=bootstrap.ActivePlanet;
        var oldSave=JsonUtility.FromJson<ReviewSave>(floorSave);oldSave.version=5;oldSave.terrainVersion=2;
        for(var d=0;d<oldSave.industry.deposits.Length;d++)oldSave.industry.deposits[d].remaining=1234.5f+d%100;
        var migrationTimer=System.Diagnostics.Stopwatch.StartNew();
        Check(bootstrap.TryLoadSaveJson(JsonUtility.ToJson(oldSave)) && Mathf.Abs(industry.Deposits[0].Remaining-1234.5f)<.001f
            && IndustryRules.Valid(industry.Capture(),placement.Buildings.Length),"version-five migration preserves finite remaining quantities and creates valid excavation state");
        Debug.Log($"Bulk excavation migration: {oldSave.industry.deposits.Length} deposits in {migrationTimer.Elapsed.TotalSeconds:0.00}s.");
        Check(bootstrap.TryLoadSaveJson(floorSave),"the full deep-pit review world restores after a legacy migration check");
        planet=bootstrap.ActivePlanet;
        // Retain a reproducible local review fixture without touching the player's normal save.
        if(!clock.Paused)clock.TogglePause();
        var directory=Path.Combine(Directory.GetParent(Application.dataPath).FullName,".utmp");
        File.WriteAllText(Path.Combine(directory,"excavation-review-fixture.json"),bootstrap.CaptureSaveJson());
        industry.ShowExcavationOutline(industry.Deposits[pending.deposit]);industry.SetOverlay(2);
        Camera.main.GetComponent<SimpleOrbitCamera>().FocusLocation(industry.Deposits[pending.deposit].Direction);
        menu.ShowBuilding(placement.Buildings[0]);
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

    private static void ConnectLogisticsToCore(BuildingPlacementController placement,LODPlanet planet,IndustrySystem industry,
        Vector3 sourcePosition,Vector3 colonyNormal)
    {
        var from=planet.transform.InverseTransformPoint(sourcePosition).normalized;var to=colonyNormal.normalized;
        var distance=Vector3.Angle(from,to)*Mathf.Deg2Rad*planet.Radius;var links=Mathf.CeilToInt(distance/1400f);
        for(var step=1;step<links;step++)
        {
            var direction=Vector3.Slerp(from,to,step/(float)links).normalized;
            placement.SelectStructure("StorageDepot");
            if(placement.TryPlaceDirection(direction))continue;
            var side=Vector3.Cross(direction,Mathf.Abs(direction.y)>.95f?Vector3.right:Vector3.up).normalized;
            var placed=false;for(var offset=0;offset<4&&!placed;offset++)
            {
                var angle=offset*Mathf.PI*.5f;var shifted=(direction+(side*Mathf.Cos(angle)+Vector3.Cross(direction,side)*Mathf.Sin(angle))*55/planet.Radius).normalized;
                placed=placement.TryPlaceDirection(shifted);
            }
            if(!placed)throw new InvalidOperationException("Could not place a logistics relay between an extractor and the colony.");
        }
    }

    private static bool PlaceFirstAvailable(BuildingPlacementController placement)
    {
        for (var face = 0; face < 6; face++)
            for (var x = 0; x < placement.GridResolution; x += 8)
                for (var y = 0; y < placement.GridResolution; y += 8)
                    if (placement.TryPlaceCell(new PlanetGridCell(face, x, y))) return true;
        return false;
    }

    // Explicit fixture supplies: gameplay construction spends materials rather than credits.
    internal static void FundConstruction(PlanetBootstrap bootstrap,float components=1900,float plasteel=1900)
    {
        var industry=bootstrap.GetComponent<IndustrySystem>();var saved=industry.Capture();
        saved.expeditionCargo=ExpeditionRules.Manifest(components,plasteel);
        industry.Configure(bootstrap.ActivePlanet,bootstrap.GetComponent<Settlement>().CreateSnapshot().normal,saved);
    }

    internal static bool EquivalentIndustryJson(string left,string right)
    {
        var a=JsonUtility.FromJson<IndustrySnapshot>(left);var b=JsonUtility.FromJson<IndustrySnapshot>(right);
        if(a.deposits.Length!=b.deposits.Length)return false;
        for(var i=0;i<a.deposits.Length;i++)
        {
            var first=a.deposits[i].excavation;var second=b.deposits[i].excavation;
            if(first==null || second==null)continue;
            // JsonUtility's decimal formatting changes the final bit of a double
            // extraction carry. Check that sub-picounit difference separately;
            // quantities, reserves, positions and every other field remain exact.
            if(Math.Abs(first.extractionCarry-second.extractionCarry)>1e-12)return false;
            first.extractionCarry=second.extractionCarry=0;
        }
        return JsonUtility.ToJson(a)==JsonUtility.ToJson(b);
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
