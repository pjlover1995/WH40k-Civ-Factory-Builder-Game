#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WH30K.CameraSystem;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Planet;
using WH30K.Sim;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;
using WH30K.UI;

public static class FarmSceneChecks
{
    [MenuItem("WH30K/Run Farm Checks (Play Mode)")]
    public static void Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode in WH30K_VS01 first.");
        var bootstrap = UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        var original = GameSettings.HasActiveGame && !bootstrap.ChoosingLanding ? bootstrap.CaptureSaveJson() : null;
        var terrain = bootstrap.NewWorldTerrain; var count = 0;
        try
        {
            RunGameplay(bootstrap, (ok, label) => {
                if (!ok) throw new InvalidOperationException("Farm FAIL: " + label);
                count++; Debug.Log("Farm PASS: " + label);
            });
            Debug.Log($"Farm: {count} actual scene checks passed in Unity {Application.unityVersion}.");
        }
        finally
        {
            bootstrap.ConfigureNewWorldTerrain(terrain);
            if (original != null && !bootstrap.TryLoadSaveJson(original)) Debug.LogError("Could not restore original in-memory colony after farm checks.");
        }
    }
    public static void RunGameplay(PlanetBootstrap bootstrap, Action<bool, string> check)
    {
        bootstrap.ConfigureNewWorldTerrain(new TerrainSettings());
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard);
        var legacy = JsonUtility.FromJson<GameplayChecks.ReviewSave>(bootstrap.CaptureSaveJson());
        var placement = bootstrap.GetComponent<BuildingPlacementController>();
        var industry = bootstrap.GetComponent<IndustrySystem>(); var resources = bootstrap.GetComponent<ResourceSystem>();
        var settlement = bootstrap.GetComponent<Settlement>(); var menu = bootstrap.GetComponent<NewGameMenu>();
        var normal = settlement.CreateSnapshot().normal;
        var tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) > .95f ? Vector3.right : Vector3.up).normalized;
        var second = Vector3.Cross(normal, tangent); ManagedBuilding farm = null;
        placement.SelectStructure("Farm");
        for (var attempt = 0; attempt < 256; attempt++)
        {
            var angle = attempt * 2.39996323f; var distance = 180 + attempt * 3;
            var direction = (normal + (tangent * Mathf.Cos(angle) + second * Mathf.Sin(angle)) * distance / bootstrap.ActivePlanet.Radius).normalized;
            if (!placement.TryPlaceDirection(direction)) continue;
            farm = placement.Buildings.Last();
            if (industry.CanFarmLayout(farm.transform, 1, farm) && industry.CanFarmLayout(farm.transform,24,farm,true)) break;
            placement.Demolish(farm); farm = null;
        }
        check(farm != null && farm.Id == "Farm" && farm.State.farm.addedFields == 0,
            "the real catalog places a standard farm with a clear first expansion");
        var index = Array.IndexOf(placement.Buildings, farm);
        var mesh = farm.GetComponent<MeshFilter>().sharedMesh;
        check(mesh.subMeshCount == 7 && mesh.vertexCount > 1000 && farm.GetComponent<FarmVisual>() != null
            && farm.GetComponent<AdaptiveBuildingVisual>() == null, "farms use terrain-grounded fields and barns rather than generic tier crowns");
        var fieldVertices = mesh.vertices;
        check(new[] { 5,6 }.All(group => {
            var triangles=mesh.GetTriangles(group);
            return Enumerable.Range(0,triangles.Length/3).All(i => Vector3.Cross(fieldVertices[triangles[i*3+1]]-fieldVertices[triangles[i*3]],
                fieldVertices[triangles[i*3+2]]-fieldVertices[triangles[i*3]]).y>0);
        }), "irregular soil and clipped crop rows have nondegenerate upward-facing triangles");
        var savedLayoutSeed=farm.FarmLayoutSeed; var savedParcel=FarmLayoutRules.Parcel(savedLayoutSeed,0).ToArray();
        placement.BeginPlacement();
        var previewFrame=new CellFrame(new PlanetGridCell(0,0,0),farm.transform.position,farm.transform.up,farm.transform.right,farm.transform.forward,10,10);
        var privateInstance=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        typeof(BuildingPlacementController).GetMethod("UpdatePreview",privateInstance).Invoke(placement,new object[]{previewFrame,true});
        var ghost=(GameObject)typeof(BuildingPlacementController).GetField("previewInstance",privateInstance).GetValue(placement);
        var ghostVertices=ghost.GetComponent<MeshFilter>().sharedMesh.vertices;
        check(ghostVertices.Length==fieldVertices.Length && ghostVertices.Select((v,i)=>(v-fieldVertices[i]).sqrMagnitude).All(d=>d<1e-8f),
            "the actual placement preview uses the same seeded terrain-following parcels as its built farm");
        placement.CancelPlacement();
        var atlas = Resources.Load<Material>("Architecture/BuildingSurface").mainTexture;
        check(farm.GetComponent<MeshRenderer>().sharedMaterials.Where((m, i) => i != 4).All(m => m.mainTexture == atlas),
            "farm buildings, soil and crops retain the photographic architecture atlas");
        var snapshot = resources.CreateSnapshot(); snapshot.Food = 60000;
        resources.LoadFromSnapshot(snapshot, GameSettings.GetDefinition(GameSettings.Difficulty.Standard));
        var saved = industry.Capture(); saved.facilities[index].quantities[6] = saved.facilities[index].quantities[9] = 100;
        industry.Configure(bootstrap.ActivePlanet, normal, saved);
        var foodBefore = resources.Food; industry.Simulate(0);
        check(resources.Food == foodBefore && farm.State.farm.productiveSeconds == 0, "paused industry changes neither food nor farm qualification");
        industry.Simulate(449);
        check(farm.State.farm.productiveSeconds == 449 && resources.Food > foodBefore-FarmRules.MonthlyDemand(settlement.Population)*449/(float)ColonyCalendar.SecondsPerMonth+1,
            "powered supplied farms deliver real food against crew consumption while accumulating full-operation months");
        typeof(NewGameMenu).GetMethod("SetCatalogGroup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(menu, new object[] { 4 });
        bootstrap.GetComponent<BuildingSelection>().Select(farm);
        CaptureReview(bootstrap, farm, "standard");
        var foodSaved = resources.Food; var json = bootstrap.CaptureSaveJson();
        System.IO.Directory.CreateDirectory(".utmp"); System.IO.File.WriteAllText(".utmp/farm-scene-snapshot.json", json);
        var diagnostic = JsonUtility.FromJson<GameplayChecks.ReviewSave>(json);
        Debug.Log($"Farm save diagnostics: version {diagnostic.version}, materials {diagnostic.industry.materialVersion}, industry {IndustryRules.Valid(diagnostic.industry, diagnostic.structures.Length)}, building {BuildingOperationRules.Valid(diagnostic.structures[index].operation)}, earthworks {EarthworksRules.Valid(diagnostic.earthworks)}, calendar {ColonyCalendar.Valid(diagnostic.clock)}, residential {ResidentialDevelopment.ValidateLoaded(diagnostic.structures[index].operation.residential)}");
        check(bootstrap.TryLoadSaveJson(json), "schema-14 farm colony restores through the production save loader");
        farm = placement.Buildings[index];
        check(farm.FarmLayoutSeed==savedLayoutSeed && FarmLayoutRules.Parcel(farm.FarmLayoutSeed,0).SequenceEqual(savedParcel),
            "JsonUtility reload retains the farm's exact individual polygon boundaries");
        check(farm.State.farm.productiveSeconds == 449 && resources.Food == foodSaved,
            "save reload retains exact food and partial six-month qualification");
        industry.Simulate(451);
        check(farm.State.farm.addedFields == 1 && farm.State.farm.productiveSeconds == 0 && Mathf.Abs(farm.Economy.jobs - 19.2f) < .001f,
            "six actual calendar months complete one field and increase employment by twenty percent");
        bootstrap.GetComponent<BuildingSelection>().Select(farm);
        var button = menu.GetComponentsInChildren<Button>(true).First(b => b.name == "UpgradeBuilding");
        check(button.gameObject.activeSelf && button.interactable && button.GetComponentInChildren<Text>().text == "DEV: ADD FIELD",
            "selecting a farm exposes its development field button");
        var vertices = farm.GetComponent<MeshFilter>().sharedMesh.vertexCount;
        button.onClick.Invoke(); button.onClick.Invoke();
        check(farm.State.farm.addedFields == 3 && farm.GetComponent<MeshFilter>().sharedMesh.vertexCount > vertices
            && Mathf.Abs(farm.Economy.jobs - 25.6f) < .001f, "each real dev-button click adds one visible field and its economy immediately");
        for (var click = 0; click < 20; click++) button.onClick.Invoke();
        check(farm.State.farm.addedFields == 23 && FarmRules.Buildings(23) == 1,
            "twenty-three added fields remain supported by the founding building");
        button.onClick.Invoke();
        check(farm.State.farm.addedFields == 24 && FarmRules.Buildings(24) == 2,
            "the twenty-fourth added field creates the first distinct satellite storehouse");
        CaptureReview(bootstrap, farm, "expanded");
        json = bootstrap.CaptureSaveJson(); check(bootstrap.TryLoadSaveJson(json) && placement.Buildings[index].State.farm.addedFields == 24,
            "the expanded field count and delayed satellite persist");
        var olderFarm=JsonUtility.FromJson<GameplayChecks.ReviewSave>(json); olderFarm.structures[index].operation.farm.layoutSeed=0;
        check(bootstrap.TryLoadSaveJson(JsonUtility.ToJson(olderFarm)) && placement.Buildings[index].FarmLayoutSeed==savedLayoutSeed
            && placement.Buildings[index].State.farm.addedFields==24,
            "pre-layout-seed farms migrate without losing the expanded fields or changing the save schema");
        var supportMeshes=Enumerable.Range(0,FarmRules.Buildings(FarmRules.MaxAddedFields)).Select(i=>BuildingVisuals.FarmSupportMesh(i,savedLayoutSeed)).ToArray();
        try
        {
            check(supportMeshes.Select(m=>m.vertexCount+"/"+m.bounds.size).Distinct().Count()==4,
                "the founding barn, L-storehouse, tower granary and machinery workshop have distinct production geometry");
            check(supportMeshes.All(m=>Mathf.Abs(m.bounds.min.x)<=.23f && Mathf.Abs(m.bounds.max.x)<=.23f
                && Mathf.Abs(m.bounds.min.z)<=.15f && Mathf.Abs(m.bounds.max.z)<=.15f && m.bounds.max.y<=.48f),
                "all unique support designs fit the courtyard and automatic-expansion clearance envelope");
        }
        finally { foreach(var supportMesh in supportMeshes)UnityEngine.Object.DestroyImmediate(supportMesh); }
        CaptureSupportReview(savedLayoutSeed);
        farm = placement.Buildings[index]; farm.ToggleOperation(); foodBefore = resources.Food; industry.Simulate(150);
        check(resources.Food < foodBefore && farm.State.farm.productiveSeconds == 0,
            "suspended farms produce nothing while monthly population demand continues");
        resources.ConsumeFood(FarmRules.FoodCapacity); json = bootstrap.CaptureSaveJson();
        check(bootstrap.TryLoadSaveJson(json) && resources.Food == 0, "a saved empty food reserve remains empty");
        check(settlement.OutputExplanation.Contains("Food reserve empty"), "actual settlement output explains the food-shortage habitability penalty");
        legacy.version = 12; legacy.industry.materialVersion = 1; legacy.resources.Food = 0; legacy.resources.FoodVersion = 0;
        legacy.industry.producedTotals = new float[9]; legacy.industry.consumedTotals = new float[9];
        legacy.industry.productionRates = new float[9]; legacy.industry.consumptionRates = new float[9];
        if (legacy.industry.expeditionCargo != null) legacy.industry.expeditionCargo = legacy.industry.expeditionCargo.Take(9).ToArray();
        check(bootstrap.TryLoadSaveJson(JsonUtility.ToJson(legacy)) && resources.Food == FarmRules.StartingFood,
            "a real pre-farm colony migrates with a finite initial food reserve");
        json = bootstrap.CaptureSaveJson();
        check(bootstrap.TryLoadSaveJson(json) && industry.Capture().producedTotals.Length == 12,
            "migrated legacy stock and statistics resave as the complete new inventory");
    }
    private static void CaptureReview(PlanetBootstrap bootstrap, ManagedBuilding farm, string stage)
    {
        var camera = Camera.main; var orbit = camera.GetComponent<SimpleOrbitCamera>();
        var position = camera.transform.position; var rotation = camera.transform.rotation; var near = camera.nearClipPlane;
        var aspect = camera.aspect; var target = camera.targetTexture; var active = RenderTexture.active;
        var canvas = bootstrap.GetComponent<NewGameMenu>().GetComponentInChildren<Canvas>();
        var mode = canvas.renderMode; var worldCamera = canvas.worldCamera; var distance = canvas.planeDistance;
        var bounds = farm.GetComponent<Renderer>().bounds;
        var range = Mathf.Max(90, bounds.size.magnitude * 1.55f);
        var directory = System.IO.Path.Combine(".utmp", "farm-review"); System.IO.Directory.CreateDirectory(directory);
        try
        {
            if (orbit != null) orbit.enabled = false;
            foreach(var outline in bootstrap.GetComponentsInChildren<FarmTerritoryOutline>())outline.Refresh();
            camera.nearClipPlane = .1f;
            camera.transform.SetPositionAndRotation(bounds.center + farm.transform.up * range * .7f - farm.transform.forward * range * .8f + farm.transform.right * range * .4f,
                Quaternion.identity);
            camera.transform.rotation = Quaternion.LookRotation(bounds.center - camera.transform.position, farm.transform.up);
            for (var step = 0; step < 180; step++) bootstrap.ActivePlanet.RefreshLodAtPosition(camera.transform.position);
            typeof(NewGameMenu).GetMethod("RefreshClock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(bootstrap.GetComponent<NewGameMenu>(), null);
            typeof(NewGameMenu).GetMethod("RefreshMaterials", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(bootstrap.GetComponent<NewGameMenu>(), null);
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            foreach (var resolution in new[] { new Vector2Int(1280, 800), new Vector2Int(1920, 1080) })
            {
                var texture = new RenderTexture(resolution.x, resolution.y, 24); texture.Create();
                camera.targetTexture = texture; camera.aspect = resolution.x / (float)resolution.y;
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = texture;
                var png = new Texture2D(resolution.x, resolution.y, TextureFormat.RGB24, false);
                png.ReadPixels(new Rect(0, 0, resolution.x, resolution.y), 0, 0); png.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, stage + "-" + resolution.x + "x" + resolution.y + ".png"), png.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(png); camera.targetTexture = null; RenderTexture.active = active;
                texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        finally
        {
            camera.targetTexture = target; RenderTexture.active = active; camera.aspect = aspect; camera.nearClipPlane = near;
            camera.transform.SetPositionAndRotation(position, rotation); if (orbit != null) orbit.enabled = true;
            canvas.renderMode = mode; canvas.worldCamera = worldCamera; canvas.planeDistance = distance;
        }
    }
    private static void CaptureSupportReview(int seed)
    {
        var directory=System.IO.Path.Combine(".utmp","farm-review"); System.IO.Directory.CreateDirectory(directory);
        foreach(var resolution in new[]{new Vector2Int(1280,800),new Vector2Int(1920,1080)})
        {
            var utility=new PreviewRenderUtility(); var root=new GameObject("Farm support design review");
            var meshes=new System.Collections.Generic.List<Mesh>();
            try
            {
                for(var design=0;design<4;design++)
                {
                    var go=new GameObject(FarmRules.BuildingDesign(design),typeof(MeshFilter),typeof(MeshRenderer));
                    go.transform.SetParent(root.transform,false); go.transform.localPosition=new Vector3((design%2-.5f)*42,0,(design/2-.5f)*38);
                    go.transform.localScale=new Vector3(64,20,64);
                    var mesh=BuildingVisuals.FarmSupportMesh(design,seed); meshes.Add(mesh); go.GetComponent<MeshFilter>().sharedMesh=mesh;
                    go.GetComponent<MeshRenderer>().sharedMaterials=BuildingVisuals.FarmMaterials;
                }
                utility.AddSingleGO(root); utility.ambientColor=new Color(.28f,.31f,.34f);
                utility.lights[0].intensity=1.2f; utility.lights[0].transform.rotation=Quaternion.Euler(45,-35,0); utility.lights[1].intensity=.5f;
                utility.camera.transform.position=new Vector3(68,65,-100); utility.camera.transform.LookAt(new Vector3(0,3,0));
                utility.camera.nearClipPlane=.1f; utility.camera.farClipPlane=600; utility.camera.fieldOfView=38;
                utility.BeginStaticPreview(new Rect(0,0,resolution.x,resolution.y)); utility.Render(true);
                var png=utility.EndStaticPreview(); System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory,"support-designs-"+resolution.x+"x"+resolution.y+".png"),png.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(png);
            }
            finally { utility.Cleanup(); if(root!=null)UnityEngine.Object.DestroyImmediate(root); foreach(var mesh in meshes)if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
    public static void RunBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
        SessionState.SetBool("WH30K.FarmBatch", true); EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]
    private static void ResumeBatch()
    {
        EditorApplication.playModeStateChanged += state => {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("WH30K.FarmBatch", false)) return;
            SessionState.SetBool("WH30K.FarmBatch", false);
            EditorApplication.delayCall += () => {
                try
                {
                    Run(); FarmEstateSceneChecks.Run(); FacilitySceneChecks.Run(); ArchitectureAppearanceChecks.Run();
                    // Let deferred mesh/scene/UI destruction and GPU work finish before terminating the graphics editor.
                    var frames = 0;
                    EditorApplication.CallbackFunction finish = null;
                    finish = () => { if (++frames < 10) return; EditorApplication.update -= finish; SceneCheckExit.Exit(0); };
                    EditorApplication.update += finish;
                }
                catch (Exception error) { Debug.LogException(error); SceneCheckExit.Exit(1); }
            };
        };
    }
}
#endif
