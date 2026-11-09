using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.CameraSystem;
using WH30K.Sim;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;
using WH30K.UI;

public static class ShipSceneChecks
{
    [MenuItem("WH30K/Run Planet Scale Acceptance (Play Mode)")]
    public static void RunPlanetScaleAcceptance()
    {
        if (!Application.isPlaying)
        {
            SessionState.SetBool("WH30K.ShipChecks.Live", true);
            RunBatch();
            return;
        }
        var bootstrap = UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        var hadColony = GameSettings.HasActiveGame && !bootstrap.ChoosingLanding;
        Run();
        LandingSceneChecks.Run();
        if (!hadColony) bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard, chooseLanding: true);
        Debug.Log("Half Earth planet scale acceptance completed.");
    }

    private const string BatchKey = "WH30K.ShipChecks.Batch";
    public static void RunAcceptanceBatch()
    {
        SessionState.SetBool("WH30K.ShipChecks.Landing", true); RunBatch();
    }
    public static void RunBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
        SessionState.SetBool(BatchKey, true);
        EditorApplication.playModeStateChanged += BatchState;
        EditorApplication.isPlaying = true;
    }
    [InitializeOnLoadMethod]
    private static void ResumeBatch()
    {
        if (!SessionState.GetBool(BatchKey, false)) return;
        EditorApplication.playModeStateChanged += BatchState;
        if (EditorApplication.isPlaying) EditorApplication.delayCall += FinishBatch;
    }
    private static void BatchState(PlayModeStateChange state)
    { if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += FinishBatch; }
    private static void FinishBatch()
    {
        if (!SessionState.GetBool(BatchKey, false)) return;
        SessionState.EraseBool(BatchKey);
        var live = SessionState.GetBool("WH30K.ShipChecks.Live", false);
        SessionState.EraseBool("WH30K.ShipChecks.Live");
        try
        {
            if (live) { RunPlanetScaleAcceptance(); return; }
            Run();
            if (SessionState.GetBool("WH30K.ShipChecks.Landing", false))
            { SessionState.EraseBool("WH30K.ShipChecks.Landing"); LandingSceneChecks.Run(); }
            SceneCheckExit.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); if (!live) SceneCheckExit.Exit(1); }
    }
    [MenuItem("WH30K/Run Explorator Ship Checks (Play Mode)")]
    public static void Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode in WH30K_VS01.");
        var bootstrap = UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        var original = GameSettings.HasActiveGame && !bootstrap.ChoosingLanding ? bootstrap.CaptureSaveJson() : null;
        var terrain = bootstrap.NewWorldTerrain;
        var checks = 0;
        void Check(bool ok, string label)
        { if (!ok) throw new InvalidOperationException("Ship FAIL: " + label); checks++; Debug.Log("Ship PASS: " + label); }
        try
        {
            bootstrap.ConfigureNewWorldTerrain(new WH30K.Planet.TerrainSettings());
            var settlement = bootstrap.GetComponent<Settlement>();
            var resources = bootstrap.GetComponent<ResourceSystem>();
            var industry = bootstrap.GetComponent<IndustrySystem>();
            var selection = bootstrap.GetComponent<BuildingSelection>();
            var menu = bootstrap.GetComponent<NewGameMenu>();
            foreach (var difficulty in GameSettings.GetAllDefinitions())
            {
                bootstrap.BeginNewGame(1234, difficulty.difficulty);
                Check(settlement.Population == ShipRules.StartingCrew && resources.Food == ShipRules.StartingFood
                    && settlement.CoreBuildings[0].Economy.capacity == ShipRules.CrewCapacity, difficulty.displayName + " starts with 1,000 crew, 1,500 tender berths and scaled food");
            }
            bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard);
            var directStartSave = bootstrap.CaptureSaveJson();
            Check(bootstrap.TryLoadSaveJson(directStartSave) && settlement.ShipLayoutVersion == 2
                && industry.Capture().tenderLogistics != null && industry.TenderTripsRemaining == 0,
                "direct landed starts save/load with no orbital stock or unearned return runs");
            bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard, chooseLanding: true);
            Check(bootstrap.ValidLandingSite(bootstrap.LandingDirection), "suggested landing supports the complete 500 m tender");
            Check(bootstrap.ConfirmLanding(), "actual landing confirmation deploys the orbital tender");
            Check(GameObject.Find("ColonyGuide") != null && bootstrap.GetComponent<ColonyClock>().Paused,
                "touchdown offers the optional guide and pauses until it is dismissed");
            GameObject.Find("DeclineColonyGuide").GetComponent<Button>().onClick.Invoke();
            Check(!menu.HasOpenEvent && !bootstrap.GetComponent<ColonyClock>().Paused,
                "declining the guide releases the simulation and engineering controls");
            var ship = settlement.CoreBuildings[0];
            var planet = bootstrap.ActivePlanet;
            var landingGround = planet.transform.TransformPoint(planet.EvaluateSurfacePoint(bootstrap.LandingDirection));
            var shipContactUp = planet.transform.TransformDirection(bootstrap.LandingDirection).normalized;
            var landingDescent = ship.GetComponent<LandingVesselDescent>();
            var landingPose = landingDescent != null ? landingDescent.LandingTargetPosition : ship.transform.position;
            var contactGap = Vector3.Dot(landingPose - shipContactUp * (ShipRules.Height * .5f) - landingGround, shipContactUp);
            Check(Mathf.Abs(contactGap) < .5f, "tender touchdown target aligns its landing pads with the selected ground plane (gap=" + contactGap.ToString("0.00") + " m)");
            Check(planet.Radius == WH30K.Planet.PlanetScale.DefaultRadius && planet.Radius == 3185500f,
                "new expedition uses half Earth's mean radius: 3,185.5 km");
            Check(bootstrap.GetComponent<BuildingPlacementController>().GridResolution == 135915,
                "construction grid retains local metric spacing on the larger sphere");
            var orbit = Camera.main.GetComponent<SimpleOrbitCamera>();
            orbit.FocusSettlement(bootstrap.LandingDirection);
            Check(orbit.IsSurfaceView && Vector3.Distance(orbit.transform.position, ship.transform.position) < 2000,
                "F frames the 500 m tender at a local distance");
            var placement = bootstrap.GetComponent<BuildingPlacementController>();
            var landingNormal = settlement.CreateSnapshot().normal;
            var landingSurface = planet.transform.TransformPoint(planet.EvaluateSurfacePoint(landingNormal));
            var landingUp = planet.transform.TransformDirection(landingNormal).normalized;
            var hit = placement.TryRaycastPlanet(new Ray(landingSurface + landingUp * 1500, -landingUp), out var picked);
            Check(hit && planet.SurfaceDistance(picked, bootstrap.LandingDirection) < 2,
                "local surface picking resolves the intended metre-scale landing direction");
            for (var i = 0; i < 48; i++) planet.RefreshLodAtPosition(orbit.transform.position);
            Check(planet.LiveMeshCount > 6 && planet.PatchesCreatedLastUpdate <= 4,
                "half Earth terrain refines around the colony with the bounded split budget");
            var mesh = ship.GetComponent<MeshFilter>().sharedMesh;
            var authoredTender = Resources.Load<GameObject>("Architecture/TenderHull");
            var sourceRenderer = authoredTender != null ? authoredTender.GetComponentInChildren<MeshRenderer>() : null;
            if (sourceRenderer != null) Debug.Log("Tender FBX source material order: " + string.Join(" | ", sourceRenderer.sharedMaterials.Select((material, index) => index + ":" + material.name)));
            Debug.Log("Tender runtime material order: " + string.Join(" | ", ship.GetComponent<MeshRenderer>().sharedMaterials.Select((material, index) => index + ":" + material.name)));
            var runtimeSlots = ship.GetComponent<MeshRenderer>().sharedMaterials;
            Check(sourceRenderer != null && sourceRenderer.sharedMaterials.Select(material => material.name)
                .SequenceEqual(runtimeSlots.Select(material => material.name)),
                "runtime tender material names follow Unity's imported FBX submesh order");
            Check(authoredTender != null && mesh == authoredTender.GetComponentInChildren<MeshFilter>()?.sharedMesh,
                "deployed tender uses the authored high-detail FBX rather than the procedural fallback");
            Debug.Log("Tender FBX import: vertices=" + mesh?.vertexCount + ", submeshes=" + mesh?.subMeshCount);
            Check(mesh != null && mesh.vertexCount >= 30000 && mesh.subMeshCount == 7,
                "authored tender retains detailed 30k-plus-vertex geometry and all seven material groups");
            Check(mesh != null && Mathf.Abs(mesh.bounds.size.z * ship.transform.lossyScale.z - 500) < 1, "rendered tender is exactly 500 m in world units (bounds=" + mesh?.bounds.size + ", scale=" + ship.transform.lossyScale + ", result=" + (mesh != null ? mesh.bounds.size.z * ship.transform.lossyScale.z : 0) + ")");
            var rawVertices = mesh.vertices;
            var vertices = rawVertices.Select(v => Vector3.Scale(v, BuildingVisuals.ShipDimensions)).ToArray();
            Check(Mathf.Abs(mesh.bounds.size.x * ShipRules.Beam - ShipRules.Beam) < 1
                && Mathf.Abs(mesh.bounds.size.y * ShipRules.Height - ShipRules.Height) < 1,
                "tender stays within the 90 m beam and 120 m height envelope");
            Check(vertices.Min(v => v.y) >= -60 && vertices.Max(v => v.y) <= 60,
                "deployed tender uses a level, non-crash landing pose");
            Check(ship.GetComponent<LandingVesselDescent>() != null,
                "the landed tender carries its surface descent animation");
            Check(mesh.normals.All(n => !float.IsNaN(n.x) && !float.IsNaN(n.y) && !float.IsNaN(n.z)
                && Mathf.Abs(n.magnitude - 1) < .001f), "posed hull lighting normals remain finite unit vectors");
            var previewMaterial = new Material(Shader.Find("Standard"));
            var preview = BuildingVisuals.Create("LandingVessel", "Ship preview check", null, BuildingVisuals.ShipDimensions, previewMaterial);
            Check(preview.GetComponent<MeshFilter>().sharedMesh == mesh, "landing preview and deployed tender use identical geometry");
            UnityEngine.Object.DestroyImmediate(preview); UnityEngine.Object.DestroyImmediate(previewMaterial);
            Check(mesh.subMeshCount == 7 && mesh.GetIndexCount(6) > 0 && mesh.GetIndexCount(5) > 0,
                "tender armour, engines and visual layers are real mesh geometry");
            var atlas = UnityEngine.Resources.Load<Texture2D>("Architecture/TenderSurfaceAtlas");
            var shipMaterials = ship.GetComponent<Renderer>().sharedMaterials;
            Check(atlas != null && shipMaterials.Length == 7 && shipMaterials.Take(4).All(m => m != null && m.GetTexture("_MainTex") == atlas),
                "the tender uses its original photographic armour atlas across its physically based surface materials");
            var legacyShip = BuildingVisuals.Create("ColonyShip", "Legacy Explorator visual check", null, BuildingVisuals.LegacyShipDimensions);
            var legacyAtlas = UnityEngine.Resources.Load<Texture2D>("Architecture/ExploratorSurfaceAtlas");
            Check(legacyAtlas != null && legacyShip.GetComponent<Renderer>().sharedMaterials.Take(4)
                .All(m => m != null && m.GetTexture("_MainTex") == legacyAtlas),
                "legacy Explorators keep their original photographic surface atlas");
            UnityEngine.Object.DestroyImmediate(legacyShip);
            Check(!ship.SetFocus(2) && ship.Economy.capacity == ShipRules.CrewCapacity && !ship.CanUpgrade, "ship cannot exceed physical berth limit");
            var center = ship.transform.position;
            Check(settlement.OverlapsStartingBuildings(center + ship.transform.forward * 220, new Vector3(20, 30, 20), ship.transform.rotation),
                "construction protects the tender bow footprint from overlap");
            Check(!settlement.OverlapsStartingBuildings(center + ship.transform.right * 80, new Vector3(20, 30, 20), ship.transform.rotation),
                "construction can occupy clear land alongside the long hull");
            Check(bootstrap.GetComponent<EarthworksController>().HasVehicle, "dozer can deploy alongside the hull");
            var dozer = bootstrap.GetComponent<EarthworksController>();
            var start = planet.transform.InverseTransformPoint(dozer.Vehicle.position).normalized;
            var drove = false;
            for (var attempt = 0; attempt < 16 && !drove; attempt++)
            {
                var angle = attempt * Mathf.PI / 8;
                var tangent = Quaternion.FromToRotation(Vector3.up, start) * new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                var destination = (start + tangent * (36 / planet.Radius)).normalized;
                if (!dozer.IssueCommand(destination, EarthworkMode.Move)) continue;
                dozer.Advance(1);
                var moved = planet.SurfaceDistance(start, planet.transform.InverseTransformPoint(dozer.Vehicle.position).normalized);
                drove = moved > 6 && moved < 12;
                dozer.Stop();
            }
            Check(drove, "a one-second dozer command moves about nine metres without teleporting on the large sphere");
            Check(EarthworksRules.Valid(dozer.Capture()), "local dozer movement retains a unit tangent heading that can be saved and restored");
            industry.Simulate(1);
            Check(industry.PowerGenerated == ShipRules.ReactorMW, "onboard reactor supplies power before any facility is built");
            Check(resources.Food < ShipRules.StartingFood, "crew consumes the finite embarked food through the actual simulation");
            selection.Select(ship);
            Check(GameObject.Find("ShipHud") != null && GameObject.Find("ShipValue0").GetComponent<Text>().text.Contains("500"),
                "ship selection opens its dedicated table HUD");
            Check(GameObject.Find("ShipValue1").GetComponent<Text>().text.Contains("1,000")
                && GameObject.Find("ShipValue3").GetComponent<Text>().text.Contains("500"), "HUD shows crew and expansion headroom");
            Physics.SyncTransforms();
            var collider = ship.GetComponent<Collider>();
            Check(collider.Raycast(new Ray(ship.GetComponent<Renderer>().bounds.center + ship.transform.right * 4000, -ship.transform.right), out _, 5000),
                "the rendered vessel has a pickable hull collider");
            CaptureReview(bootstrap, ship, "ship-hud");
            CaptureTenderSiteReview(bootstrap, ship);
            CaptureHullReview();
            GameObject.Find("ToggleShipOperation").GetComponent<Button>().onClick.Invoke(); industry.Simulate(1);
            menu.ShowBuilding(ship);
            Check(ship.State.offline && industry.PowerGenerated == 0 && GameObject.Find("ShipValue4").GetComponent<Text>().text.Contains("OFFLINE"),
                "HUD suspension switches off actual onboard generation");
            var food = resources.Food;
            industry.Simulate(1);
            Check(resources.Food < food, "suspension does not stop crew eating");
            var json = bootstrap.CaptureSaveJson();
            var loadedShip = bootstrap.TryLoadSaveJson(json);
            var loadedJson = loadedShip ? bootstrap.CaptureSaveJson() : "rejected";
            if (loadedJson != json)
            { File.WriteAllText(".utmp/ship-before.json", json); File.WriteAllText(".utmp/ship-after.json", loadedJson); }
            Check(loadedShip && loadedJson == json, "current schema round-trips depleted food, population, ship identity and suspension exactly");
            var oldHeading=JsonUtility.FromJson<GameplayChecks.ReviewSave>(json);
            oldHeading.earthworks.forwardX=oldHeading.earthworks.forwardY=oldHeading.earthworks.forwardZ=0;
            Check(bootstrap.TryLoadSaveJson(JsonUtility.ToJson(oldHeading)) && EarthworksRules.Valid(dozer.Capture())
                && resources.Food==oldHeading.resources.Food && settlement.Population==oldHeading.settlement.population,
                "earlier zero-heading large-world saves recover the vehicle without replacing crew or supplies");
            ship = settlement.CoreBuildings[0]; ship.ToggleOperation();
            resources.ConsumeFood(FarmRules.FoodCapacity);
            json = bootstrap.CaptureSaveJson();
            Check(bootstrap.TryLoadSaveJson(json) && resources.Food == 0, "reload never refills empty ship food");
            var legacy = JsonUtility.FromJson<GameplayChecks.ReviewSave>(json);
            legacy.version = 14; legacy.settlement.shipLayoutVersion = 0;
            Check(bootstrap.TryLoadSaveJson(JsonUtility.ToJson(legacy)) && !settlement.HasExplorator && resources.Food == 0
                && settlement.CoreBuildings[0].Economy.capacity == 240 && Mathf.Abs(settlement.CoreBuildings[0].transform.lossyScale.z - 76) < .001f,
                "schema 14 retains its compact tender, saved crew and exhausted food");
            Check(bootstrap.TryLoadSaveJson(bootstrap.CaptureSaveJson()), "migrated tender remains loadable on a second save");
            legacy.planetRadius = 6000; legacy.terrainSettings.radius = 6000; legacy.gridResolution = 256;
            Check(bootstrap.TryLoadSaveJson(JsonUtility.ToJson(legacy)) && bootstrap.ActivePlanet.Radius == 6000
                && bootstrap.TryLoadSaveJson(bootstrap.CaptureSaveJson()) && bootstrap.ActivePlanet.Radius == 6000,
                "historical 6 km worlds keep their stored size across load and resave");
            var invalid = JsonUtility.FromJson<GameplayChecks.ReviewSave>(json); invalid.settlement.shipLayoutVersion = 99;
            var active = bootstrap.ActivePlanet;
            Check(!bootstrap.TryLoadSaveJson(JsonUtility.ToJson(invalid)) && bootstrap.ActivePlanet == active,
                "unsupported ship layout is rejected before replacing the colony");
            Debug.Log($"Explorator: {checks} actual scene checks passed in Unity {Application.unityVersion}.");
        }
        finally
        {
            bootstrap.ConfigureNewWorldTerrain(terrain);
            if (original != null && !bootstrap.TryLoadSaveJson(original)) Debug.LogError("Could not restore the original in-memory colony.");
        }
    }

    internal static void CaptureReview(PlanetBootstrap bootstrap, ManagedBuilding ship, string stage)
    {
        var camera = Camera.main; var orbit = camera.GetComponent<SimpleOrbitCamera>();
        var position = camera.transform.position; var rotation = camera.transform.rotation;
        var near = camera.nearClipPlane; var far = camera.farClipPlane; var aspect = camera.aspect;
        var target = camera.targetTexture; var active = RenderTexture.active;
        var canvas = bootstrap.GetComponent<NewGameMenu>().GetComponentInChildren<Canvas>();
        var mode = canvas.renderMode; var worldCamera = canvas.worldCamera; var plane = canvas.planeDistance;
        var canvasRect = (RectTransform)canvas.transform;
        var canvasPosition = canvasRect.localPosition; var canvasRotation = canvasRect.localRotation;
        var canvasScale = canvasRect.localScale; var canvasSize = canvasRect.sizeDelta;
        var uiTransforms = canvas.GetComponentsInChildren<Transform>(true);
        var uiLayers = uiTransforms.Select(t => t.gameObject.layer).ToArray();
        var mask = camera.cullingMask;
        var uiObject = new GameObject("Review UI camera");
        var uiCamera = uiObject.AddComponent<Camera>();
        uiCamera.enabled = false; uiCamera.clearFlags = CameraClearFlags.Depth;
        uiCamera.cullingMask = 1 << 31; uiCamera.nearClipPlane = .1f; uiCamera.farClipPlane = 10;
        var directory = Path.Combine(".utmp", "ship-review"); Directory.CreateDirectory(directory);
        try
        {
            if (orbit != null) orbit.enabled = false;
            var bounds = ship.GetComponent<Renderer>().bounds;
            camera.nearClipPlane = .1f; camera.farClipPlane = 10000;
            camera.transform.position = bounds.center + ship.transform.up * 420 + ship.transform.right * 650 + ship.transform.forward * 310;
            var look = Quaternion.LookRotation(bounds.center - camera.transform.position, ship.transform.up);
            // Offset toward camera-right, leaving the model in the clear space left of the telemetry card.
            camera.transform.rotation = Quaternion.LookRotation(bounds.center + look * Vector3.right * 260 - camera.transform.position, ship.transform.up);
            for (var step = 0; step < 120; step++) bootstrap.ActivePlanet.RefreshLodAtPosition(camera.transform.position);
            foreach (var method in new[] { "RefreshClock", "RefreshMaterials" })
                typeof(NewGameMenu).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(bootstrap.GetComponent<NewGameMenu>(), null);
            // Keep UI vertices near the origin; placing a two-metre canvas beside a
            // camera millions of metres from the origin destroys small glyph triangles.
            foreach (var item in uiTransforms) item.gameObject.layer = 31;
            camera.cullingMask = mask & ~(1 << 31);
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 2;
            foreach (var resolution in new[] { new Vector2Int(1280, 800), new Vector2Int(1920, 1080) })
            {
                var texture = new RenderTexture(resolution.x, resolution.y, 24); texture.Create();
                camera.targetTexture = uiCamera.targetTexture = texture;
                camera.aspect = uiCamera.aspect = resolution.x / (float)resolution.y;
                Canvas.ForceUpdateCanvases(); camera.Render(); uiCamera.Render(); RenderTexture.active = texture;
                var png = new Texture2D(resolution.x, resolution.y, TextureFormat.RGB24, false);
                png.ReadPixels(new Rect(0, 0, resolution.x, resolution.y), 0, 0); png.Apply();
                File.WriteAllBytes(Path.Combine(directory, stage + "-" + resolution.x + "x" + resolution.y + ".png"), png.EncodeToPNG());
                var visibleShipHud = GameObject.Find("ShipHud");
                foreach (var label in canvas.GetComponentsInChildren<Text>().Where(t => t.transform.parent.name=="CommandHeader"
                    || visibleShipHud != null && t.transform.IsChildOf(visibleShipHud.transform)))
                    if (label.preferredHeight > label.rectTransform.rect.height + 1 || label.preferredWidth > label.rectTransform.rect.width + 1)
                        throw new InvalidOperationException("Ship HUD text exceeds its cell: " + label.name);
                UnityEngine.Object.DestroyImmediate(png); camera.targetTexture = uiCamera.targetTexture = null; RenderTexture.active = active;
                texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        finally
        {
            camera.targetTexture = target; RenderTexture.active = active; camera.aspect = aspect;
            camera.nearClipPlane = near; camera.farClipPlane = far; camera.transform.SetPositionAndRotation(position, rotation);
            if (orbit != null) orbit.enabled = true;
            canvas.renderMode = mode; canvas.worldCamera = worldCamera; canvas.planeDistance = plane;
            canvasRect.sizeDelta = canvasSize; canvasRect.localPosition = canvasPosition;
            canvasRect.localRotation = canvasRotation; canvasRect.localScale = canvasScale;
            for (var i = 0; i < uiTransforms.Length; i++) uiTransforms[i].gameObject.layer = uiLayers[i];
            camera.cullingMask = mask;
            UnityEngine.Object.DestroyImmediate(uiObject);
            Canvas.ForceUpdateCanvases();
        }
    }

    private static void CaptureHullReview()
    {
        var directory = Path.Combine(".utmp", "ship-review"); Directory.CreateDirectory(directory);
        foreach (var resolution in new[] { new Vector2Int(1280, 800), new Vector2Int(1920, 1080) })
        {
            var utility = new PreviewRenderUtility(); GameObject hull = null;
            try
            {
                hull = BuildingVisuals.Create("LandingVessel", "Landing tender review", null, BuildingVisuals.ShipDimensions);
                utility.AddSingleGO(hull); utility.ambientColor = new Color(.3f, .32f, .35f);
                utility.lights[0].intensity = 1.5f; utility.lights[0].transform.rotation = Quaternion.Euler(45, -35, 0);
                utility.lights[1].intensity = .8f; utility.lights[1].transform.rotation = Quaternion.Euler(25, 140, 0);
                utility.camera.transform.position = new Vector3(550, 260, 410);
                utility.camera.transform.LookAt(new Vector3(0, 30, 0));
                utility.camera.nearClipPlane = .1f; utility.camera.farClipPlane = 3000; utility.camera.fieldOfView = 38;
                utility.BeginStaticPreview(new Rect(0, 0, resolution.x, resolution.y)); utility.Render(true);
                var png = utility.EndStaticPreview();
                File.WriteAllBytes(Path.Combine(directory, "landing-tender-hull-" + resolution.x + "x" + resolution.y + ".png"), png.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(png);
                foreach (var view in new[] { "port", "starboard", "engines" })
                {
                    utility.camera.transform.position = view == "port" ? new Vector3(-650, 190, 180)
                        : view == "starboard" ? new Vector3(650, 190, 180) : new Vector3(250, 260, -700);
                    utility.camera.transform.LookAt(new Vector3(0, 30, 0));
                    utility.BeginStaticPreview(new Rect(0, 0, resolution.x, resolution.y)); utility.Render(true);
                    png = utility.EndStaticPreview();
                    File.WriteAllBytes(Path.Combine(directory, "landing-tender-" + view + "-" + resolution.x + "x" + resolution.y + ".png"), png.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(png);
                }
            }
            finally { utility.Cleanup(); if (hull != null) UnityEngine.Object.DestroyImmediate(hull); }
        }
    }

    private static void CaptureTenderSiteReview(PlanetBootstrap bootstrap, ManagedBuilding ship)
    {
        var camera = Camera.main; var orbit = camera.GetComponent<SimpleOrbitCamera>();
        var position = camera.transform.position; var rotation = camera.transform.rotation;
        var near = camera.nearClipPlane; var far = camera.farClipPlane; var aspect = camera.aspect;
        var target = camera.targetTexture; var active = RenderTexture.active;
        var canvas = bootstrap.GetComponent<NewGameMenu>().GetComponentInChildren<Canvas>();
        var showUi = canvas.enabled; var orbitEnabled = orbit.enabled;
        var sun = UnityEngine.Object.FindObjectsOfType<Light>().FirstOrDefault(l => l.type == LightType.Directional && l.enabled);
        var sunRotation = sun != null ? sun.transform.rotation : Quaternion.identity;
        var sunIntensity = sun != null ? sun.intensity : 0;
        try
        {
            canvas.enabled = false; orbit.enabled = false;
            camera.nearClipPlane = .1f; camera.farClipPlane = 10000;
            foreach (var side in new[] { -1, 1 })
            {
                // A controlled daylight review makes damage/contact readable on both flanks.
                if (sun != null)
                {
                    sun.transform.rotation = Quaternion.LookRotation(-ship.transform.up - ship.transform.right * side * .7f + ship.transform.forward * .25f, ship.transform.forward);
                    sun.intensity = 1.4f;
                }
                var center = ship.GetComponent<Renderer>().bounds.center;
                camera.transform.position = center + ship.transform.up * 150 + ship.transform.right * (side * 450) + ship.transform.forward * 240;
                camera.transform.rotation = Quaternion.LookRotation(center - camera.transform.position, ship.transform.up);
                for (var step = 0; step < 120; step++) bootstrap.ActivePlanet.RefreshLodAtPosition(camera.transform.position);
                foreach (var size in new[] { new Vector2Int(1280, 800), new Vector2Int(1920, 1080) })
                {
                    var rt = new RenderTexture(size.x, size.y, 24); rt.Create();
                    try
                    {
                        camera.targetTexture = rt; camera.aspect = size.x / (float)size.y;
                        camera.Render(); RenderTexture.active = rt;
                        var png = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                        png.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); png.Apply();
                        File.WriteAllBytes(Path.Combine(".utmp", "ship-review", "tender-" + (side < 0 ? "port" : "starboard") + "-" + size.x + "x" + size.y + ".png"), png.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(png);
                    }
                    finally { camera.targetTexture = target; RenderTexture.active = active; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                }
            }
        }
        finally
        {
            camera.targetTexture = target; RenderTexture.active = active; camera.aspect = aspect;
            camera.nearClipPlane = near; camera.farClipPlane = far; camera.transform.SetPositionAndRotation(position, rotation);
            canvas.enabled = showUi; orbit.enabled = orbitEnabled;
            if (sun != null) { sun.transform.rotation = sunRotation; sun.intensity = sunIntensity; }
        }
    }
}
