using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
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

// Runs across real frames so deferred destruction, ecology streaming and UI layout are exercised.
public static class ReliabilitySceneChecks
{
    private const string BatchKey = "WH30K.Reliability.Batch";
    private static IEnumerator routine;
    private static int frame = -1, checks;
    private static string original;
    private static TerrainSettings originalTerrain;
    private static PlanetBootstrap bootstrap;

    public static void RunBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
        SessionState.SetBool(BatchKey, true);
        EditorApplication.playModeStateChanged += PlayState;
        EditorApplication.isPlaying = true;
    }
    [InitializeOnLoadMethod]
    private static void Resume()
    { if (SessionState.GetBool(BatchKey, false)) EditorApplication.playModeStateChanged += PlayState; }
    private static void PlayState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(BatchKey, false)) return;
        SessionState.EraseBool(BatchKey); EditorApplication.delayCall += Run;
    }
    [MenuItem("WH30K/Run Reliability Checks (Play Mode)")]
    public static void Run()
    {
        if (!Application.isPlaying || routine != null) throw new InvalidOperationException("Enter Play Mode and allow current checks to finish.");
        bootstrap = UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        original = GameSettings.HasActiveGame && !bootstrap.ChoosingLanding ? bootstrap.CaptureSaveJson() : null;
        originalTerrain = bootstrap.NewWorldTerrain; checks = 0;
        routine = Checks(); EditorApplication.update += Step;
    }
    private static void Check(bool ok, string label)
    { if (!ok) throw new InvalidOperationException("Reliability FAIL: " + label); checks++; Debug.Log("Reliability PASS: " + label); }
    private static void Step()
    {
        if (frame == Time.frameCount) return; frame = Time.frameCount;
        try { if (!routine.MoveNext()) Finish(0); }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    private static void Finish(int result)
    {
        EditorApplication.update -= Step; (routine as IDisposable)?.Dispose(); routine = null;
        if(bootstrap.ActivePlanet!=null)bootstrap.ActivePlanet.GetComponent<PlanetEcology>().enabled=true;
        if(Camera.main!=null)Camera.main.GetComponent<SimpleOrbitCamera>().enabled=true;
        bootstrap.ConfigureNewWorldTerrain(originalTerrain);
        if (original != null && !bootstrap.TryLoadSaveJson(original)) { Debug.LogError("Reliability checks could not restore the original colony."); result = 1; }
        Debug.Log($"Reliability: {checks} real-frame checks completed; result={result}.");
        if (Application.isBatchMode) SceneCheckExit.Exit(result);
    }
    private static void Click(NewGameMenu menu, string name)
        => menu.GetComponentsInChildren<Button>(true).First(b => b.name == name).onClick.Invoke();

    private static IEnumerator Checks()
    {
        bootstrap.ConfigureNewWorldTerrain(new TerrainSettings());
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard);
        var menu = bootstrap.GetComponent<NewGameMenu>(); var clock = bootstrap.GetComponent<ColonyClock>();
        var research = bootstrap.GetComponent<ResearchSystem>();
        var industry = bootstrap.GetComponent<IndustrySystem>();
        var normal = bootstrap.GetComponent<Settlement>().CreateSnapshot().normal;
        var orbit = Camera.main.GetComponent<SimpleOrbitCamera>();
        clock.TogglePause(); orbit.FocusLocation(normal);
        yield return null;
        var position = orbit.transform.position;
        orbit.Navigate(Vector2.right, .02f);
        typeof(SimpleOrbitCamera).GetMethod("UpdateCameraTransform", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(orbit, null);
        var moved = Vector3.Distance(position, orbit.transform.position);
        Check(moved > .1f && moved < 10, "a sub-ten-metre navigation step updates the camera without waiting for a thirty-metre jump");

        Check(research.SelectProject("aquifer-lenses"), "actual research system accepts an unlocked project");
        var insight = research.Insight;
        industry.Simulate(1);
        Check(research.CurrentRate == ResearchRules.ShipInsightPerSecond && research.Insight < insight,
            "active Explorator funds the selected research project through industry simulation");
        var saved = bootstrap.CaptureSaveJson();
        Click(menu, "OpenResearch");
        Check(menu.HasOpenEvent && clock.DecisionPending, "research panel pauses the simulation");
        CapturePanel(menu, "research");
        Check(bootstrap.TryLoadSaveJson(saved) && !menu.HasOpenEvent && !clock.DecisionPending && clock.Paused,
            "load dismisses old research UI while retaining the saved player pause state");
        Check(research.ActiveProjectId == "aquifer-lenses", "selected research survives actual JsonUtility load");
        Click(menu, "OpenColonyGuide");
        Canvas.ForceUpdateCanvases();
        var next=menu.GetComponentsInChildren<Button>(true).First(b=>b.name=="GuideNext");
        var title=menu.GetComponentsInChildren<Text>(true).First(t=>t.name=="GuideTag");
        Check(next.transform.position.y<title.transform.position.y-200,
            "guide navigation occupies its footer without obscuring the title");
        CapturePanel(menu, "guide");
        Click(menu,"GuideNext");
        Click(menu,"GuideBuild");
        var placement=bootstrap.GetComponent<BuildingPlacementController>();
        Check(placement.IsPlacing && placement.ActiveStructureId==ColonyGuideContent.EntryAtPage(1).Id && !menu.HasOpenEvent && !clock.DecisionPending,
            "guide BUILD THIS closes its modal and begins the referenced catalog placement");
        placement.CancelPlacement();Click(menu,"OpenColonyGuide");
        bootstrap.BeginNewGame(5678, GameSettings.Difficulty.Standard);
        Check(!menu.HasOpenEvent && !clock.Paused, "new expedition clears old guide and modal pause state");
        Click(menu, "OpenResearch");
        Check(!bootstrap.TryLoadSaveJson("{\"version\":999}") && menu.HasOpenEvent && clock.Paused,
            "rejected save preserves the active session and its open modal");
        Click(menu, "CloseResearch");
        Click(menu,"OpenResearch");Click(menu,"BuildCognitorium");
        Check(placement.IsPlacing && placement.ActiveStructureId=="Cognitorium" && !menu.HasOpenEvent,
            "research build control opens Cognitorium placement through the actual catalog");
        placement.CancelPlacement();
        clock.TogglePause();
        var planet = bootstrap.ActivePlanet;
        orbit.FocusLocation(bootstrap.GetComponent<Settlement>().CreateSnapshot().normal);
        // Freeze the interactive camera while directly stepping deterministic ecology tiles.
        orbit.enabled = false;
        var ecology = planet.GetComponent<PlanetEcology>();
        ecology.enabled = false;
        var generate = typeof(PlanetEcology).GetMethod("Generate", BindingFlags.Instance | BindingFlags.NonPublic);
        var forest = Vector3.zero;
        for (var i = 0; i < 512; i++)
        {
            var y = 1 - 2 * (i + .5f) / 512; var a = i * 2.39996323f;
            var n = new Vector3(Mathf.Sqrt(1-y*y)*Mathf.Cos(a),y,Mathf.Sqrt(1-y*y)*Mathf.Sin(a));
            if (planet.Biome(n) == "Forest") { forest = n; break; }
        }
        Check(forest.sqrMagnitude > .9f, "fixture locates a real forest biome");
        var tangent = Vector3.Cross(forest, Vector3.up).normalized;
        var peak = 0;
        for (var tile = 0; tile < 5; tile++)
        {
            var n = (forest + tangent * (tile * 900 / planet.Radius)).normalized;
            var generation = (IEnumerator)generate.Invoke(ecology, new object[] { n });
            while (generation.MoveNext()) { peak = Math.Max(peak, ecology.ResidentTreeCount); yield return null; }
            Check(ecology.ResidentTreeCount > 0 && ecology.ResidentTreeCount <= TreeEcologyRules.MaximumResidentTrees,
                "forest tile " + tile + " releases retired trees and retains only its bounded local population");
            var tree = planet.transform.Find("Placed photographic trees").Cast<Transform>().First(t=>t.gameObject.activeSelf);
            var local = tree.localPosition.normalized;
            var from = tree.GetInstanceID();
            generation = (IEnumerator)generate.Invoke(ecology, new object[] { n });
            while (generation.MoveNext()) yield return null;
            Check(tree != null && tree.GetInstanceID() == from && planet.SurfaceDistance(tree.localPosition.normalized, local) < .01f,
                "overlapping rebuild reuses the same tree instance and position for tile " + tile);
            yield return null;
        }
        Check(peak <= TreeEcologyRules.MaximumResidentTrees * 2, "in-progress streaming is bounded to the previous and replacement tiles");
        var card = WorldArt.TreeCard;
        Check(card.vertexCount == 16 && card.triangles.Length == 24 && Vector3.Dot(card.normals[0],card.normals[8]) < -.99f,
            "crossed foliage has separately lit front and back faces");
        var cutout = Resources.Load<Material>("WH30K/RuntimeShaders/StandardCutout");
        Check(cutout != null && cutout.IsKeywordEnabled("_ALPHATEST_ON") && cutout.enableInstancing && !ShaderUtil.ShaderHasError(cutout.shader),
            "player Resources retain the imported cutout and instancing shader variant");
        var trees=planet.transform.Find("Placed photographic trees").Cast<Transform>().Where(t=>t.gameObject.activeSelf).ToArray();
        var treeCenter=trees[trees.Length/2].position;
        var up=(treeCenter-planet.transform.position).normalized;
        planet.GetComponent<PlanetarySystem>().SetLandingSite(up);
        Camera.main.transform.position=treeCenter+up*65+Vector3.Cross(up,Vector3.up).normalized*90;
        Camera.main.transform.LookAt(treeCenter+up*8,up);
        for(var step=0;step<96;step++){planet.RefreshLodAtPosition(Camera.main.transform.position);yield return null;}
        CaptureForest();
        ecology.enabled = true; orbit.enabled = true;
        Debug.Log($"Forest retention: peak {peak}, final {ecology.ResidentTreeCount}; five tiles plus overlapping rebuilds.");
        var logistics=CheckLogisticsStreaming();while(logistics.MoveNext())yield return null;
    }

    private static IEnumerator CheckLogisticsStreaming()
    {
        bootstrap.ConfigureNewWorldTerrain(new TerrainSettings{radius=6000});
        bootstrap.BeginNewGame(1234,GameSettings.Difficulty.Standard,legacyCore:true);
        var clock=bootstrap.GetComponent<ColonyClock>();clock.TogglePause();
        var planet=bootstrap.ActivePlanet;planet.GetComponent<PlanetEcology>().enabled=false;
        var placement=bootstrap.GetComponent<BuildingPlacementController>();
        var industry=bootstrap.GetComponent<IndustrySystem>();
        var n=bootstrap.GetComponent<Settlement>().CreateSnapshot().normal;
        var right=Vector3.Cross(n,Vector3.up).normalized;var forward=Vector3.Cross(n,right);
        GameplayChecks.FundConstruction(bootstrap);
        for(var i=0;i<96;i++)
        {
            placement.SelectStructure("Works");
            placement.TryPlaceDirection((n+(right*((i%12-5.5f)*65)+forward*((i/12-3.5f)*65))/planet.Radius).normalized);
        }
        Check(placement.Buildings.Length>=90,"route fixture constructs at least ninety real logistics nodes");
        placement.CancelPlacement();industry.Simulate(0);industry.SetOverlay(1);
        Check(industry.LogisticsBuildPending && industry.LogisticsSegmentsLastUpdate==0,
            "opening Routes queues geometry without synchronous terrain sampling");
        yield return null;
        Check(industry.LogisticsBuildPending && industry.LogisticsSegmentsLastUpdate<=IndustrySystem.LogisticsSegmentsPerFrame,
            "dense Routes yields after a bounded first frame even while paused");
        industry.SetOverlay(0);yield return null;
        Check(industry.LogisticsSegmentsLastUpdate==0 && industry.LogisticsPairsLastUpdate==0,
            "hiding Routes cancels its pending work");
        industry.SetOverlay(1);
        // Invalidate an unfinished search with both a registry and heightfield revision.
        yield return null;
        var removed=placement.Buildings[0];Check(placement.Demolish(removed),"construction registry can change during a route build");
        industry.Simulate(0);planet.RefreshExcavation(n,0);
        var saved=JsonUtility.ToJson(industry.Capture());var elapsed=System.Diagnostics.Stopwatch.StartNew();
        var frames=0;
        while(industry.LogisticsBuildPending)
        {
            if(++frames>2000)throw new InvalidOperationException("Routes failed to complete after invalidation.");
            yield return null;
            if(industry.LogisticsSegmentsLastUpdate>IndustrySystem.LogisticsSegmentsPerFrame
                ||industry.LogisticsPairsLastUpdate>IndustrySystem.LogisticsPairsPerFrame)
                throw new InvalidOperationException("Routes exceeded its per-frame sampling/pair budget.");
        }
        var filter=planet.GetComponentsInChildren<MeshFilter>(true).First(f=>f.sharedMesh!=null && f.sharedMesh.name=="Industrial logistics network");
        var mesh=filter.sharedMesh;var nodes=placement.Buildings;var expected=0;
        for(var a=0;a<nodes.Length;a++)for(var b=a+1;b<nodes.Length;b++)
        {
            if((nodes[a].transform.position-nodes[b].transform.position).sqrMagnitude>850f*850f)continue;
            var from=planet.transform.InverseTransformPoint(nodes[a].transform.position).normalized;
            var to=planet.transform.InverseTransformPoint(nodes[b].transform.position).normalized;
            expected+=Mathf.Clamp(Mathf.CeilToInt(planet.SurfaceDistance(from,to)/100),1,32)*4;
        }
        Check(mesh.vertexCount==expected && mesh.vertexCount>10000 && !industry.LogisticsVisualTruncated,
            "published routes exactly match the complete current registry after demolition and terrain invalidation");
        Check(mesh.vertexCount<=IndustrySystem.LogisticsSegmentLimit*4 && mesh.indexFormat==UnityEngine.Rendering.IndexFormat.UInt32,
            "route buffers retain a bounded display size and 32-bit indices");
        Check(filter.GetComponent<Renderer>().enabled && clock.Paused
            && GameplayChecks.EquivalentIndustryJson(saved,JsonUtility.ToJson(industry.Capture())),
            "paused visual streaming leaves inventory, cargo, geology and production unchanged");
        var count=mesh.vertexCount;industry.SetOverlay(0);industry.SetOverlay(1);yield return null;
        Check(!industry.LogisticsBuildPending && mesh.vertexCount==count && industry.LogisticsSegmentsLastUpdate==0,
            "reopening an unchanged overlay reuses its finished mesh");
        Debug.Log($"Routes streaming: {nodes.Length} nodes, {expected} vertices, {frames} frames, {elapsed.Elapsed.TotalMilliseconds:0.0} ms elapsed including intervening frames.");
        industry.SetOverlay(0);
        for(var i=96;i<288;i++)
        {
            if(i%64==32)GameplayChecks.FundConstruction(bootstrap);
            placement.SelectStructure("Works");
            placement.TryPlaceDirection((n+(right*((i%12-5.5f)*65)+forward*((i/12-3.5f)*65))/planet.Radius).normalized);
        }
        placement.CancelPlacement();industry.Simulate(0);industry.SetOverlay(1);
        var menu=bootstrap.GetComponent<NewGameMenu>();
        var refresh=typeof(NewGameMenu).GetMethod("RefreshMaterials",BindingFlags.Instance|BindingFlags.NonPublic);
        refresh.Invoke(menu,null);
        Check(RouteStatusFits(menu,"ROUTES: PREPARING"),
            "the actual HUD reports a pending route build without clipping its status label");
        CapturePanel(menu,"routes-pending");
        frames=0;
        while(industry.LogisticsBuildPending)
        {
            if(++frames>2000)throw new InvalidOperationException("Capped route fixture did not finish.");
            yield return null;
            if(industry.LogisticsSegmentsLastUpdate>IndustrySystem.LogisticsSegmentsPerFrame
                ||industry.LogisticsPairsLastUpdate>IndustrySystem.LogisticsPairsPerFrame)
                throw new InvalidOperationException("Capped routes exceeded their per-frame budget.");
        }
        filter=planet.GetComponentsInChildren<MeshFilter>(true).First(f=>f.sharedMesh!=null && f.sharedMesh.name=="Industrial logistics network");
        Check(placement.Buildings.Length>=270 && industry.LogisticsVisualTruncated
            &&filter.sharedMesh.vertexCount==IndustrySystem.LogisticsSegmentLimit*4,
            "a very dense graph stops exactly at its decorative mesh limit");
        refresh.Invoke(menu,null);
        Check(RouteStatusFits(menu,"ROUTES: DISPLAY LIMIT"),
            "the HUD identifies a capped route display without interrupting the colony or clipping its label");
        CapturePanel(menu,"routes-capped");
        Debug.Log($"Routes display cap: {placement.Buildings.Length} nodes, {filter.sharedMesh.vertexCount} vertices, {frames} frames.");
        var json=bootstrap.CaptureSaveJson();Check(bootstrap.TryLoadSaveJson(json),"saving and reloading does not depend on visual build state");
        Check(industry.LogisticsBuildPending,"newly loaded worlds invalidate the previous route cache");
    }

    private static bool RouteStatusFits(NewGameMenu menu,string expected)
    {
        Canvas.ForceUpdateCanvases();
        var label=menu.GetComponentsInChildren<Text>(true).FirstOrDefault(t=>t.text==expected);
        return label!=null && label.preferredWidth<=label.rectTransform.rect.width+.5f
            &&label.preferredHeight<=label.rectTransform.rect.height+.5f;
    }

    private static void CaptureForest()
    {
        var camera=Camera.main;var target=camera.targetTexture;var active=RenderTexture.active;
        var clear=camera.clearFlags;var background=camera.backgroundColor;var aspect=camera.aspect;
        try
        {
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.16f,.19f);
            foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(1920,1080)})
            {
                var texture=new RenderTexture(size.x,size.y,24);texture.Create();camera.targetTexture=texture;camera.aspect=size.x/(float)size.y;
                camera.Render();RenderTexture.active=texture;var png=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);
                png.ReadPixels(new Rect(0,0,size.x,size.y),0,0);png.Apply();
                File.WriteAllBytes($".utmp/reliability-review/forest-{size.x}x{size.y}.png",png.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(png);camera.targetTexture=null;RenderTexture.active=active;texture.Release();UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        finally{camera.targetTexture=target;RenderTexture.active=active;camera.clearFlags=clear;camera.backgroundColor=background;camera.aspect=aspect;}
    }

    private static void CapturePanel(NewGameMenu menu, string name)
    {
        var canvas = menu.GetComponentInChildren<Canvas>();
        var transforms = canvas.GetComponentsInChildren<Transform>(true);
        var layers = transforms.Select(t => t.gameObject.layer).ToArray();
        var mode=canvas.renderMode;var worldCamera=canvas.worldCamera;var plane=canvas.planeDistance;
        var go=new GameObject("Reliability UI review");var camera=go.AddComponent<Camera>();
        camera.enabled=false;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=new Color(.04f,.05f,.06f);camera.nearClipPlane=.1f;camera.farClipPlane=10;
        var active=RenderTexture.active;
        try
        {
            foreach(var t in transforms)t.gameObject.layer=31;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=2;
            Directory.CreateDirectory(".utmp/reliability-review");
            foreach(var resolution in new[]{new Vector2Int(1280,800),new Vector2Int(1920,1080)})
            {
                var texture=new RenderTexture(resolution.x,resolution.y,24);texture.Create();
                camera.targetTexture=texture;camera.aspect=resolution.x/(float)resolution.y;
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=texture;
                var png=new Texture2D(resolution.x,resolution.y,TextureFormat.RGB24,false);
                png.ReadPixels(new Rect(0,0,resolution.x,resolution.y),0,0);png.Apply();
                File.WriteAllBytes($".utmp/reliability-review/{name}-{resolution.x}x{resolution.y}.png",png.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(png);camera.targetTexture=null;RenderTexture.active=active;
                texture.Release();UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        finally
        {
            RenderTexture.active=active;canvas.renderMode=mode;canvas.worldCamera=worldCamera;canvas.planeDistance=plane;
            for(var i=0;i<transforms.Length;i++)transforms[i].gameObject.layer=layers[i];
            UnityEngine.Object.DestroyImmediate(go);Canvas.ForceUpdateCanvases();
        }
    }
}
