using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using WH30K.CameraSystem;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Planet;
using WH30K.Sim;
using WH30K.Sim.Settlements;

public static class CameraSceneChecks
{
    private const string Key = "WH30K.CameraChecks";
    private static IEnumerator routine;
    private static int frame = -1, checks;
    private static readonly List<string> errors = new List<string>();
    public static void RunBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
        SessionState.SetBool(Key, true); EditorApplication.playModeStateChanged += State;
        EditorApplication.isPlaying = true;
    }
    [InitializeOnLoadMethod]
    private static void Resume()
    { if (SessionState.GetBool(Key, false)) EditorApplication.playModeStateChanged += State; }
    private static void State(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key); EditorApplication.delayCall += () => {
            errors.Clear(); checks = 0; Application.logMessageReceived += Log;
            routine = Run(); EditorApplication.update += Step;
        };
    }
    private static void Log(string message, string stack, LogType kind)
    { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) errors.Add(message); }
    private static void Step()
    {
        if (frame == Time.frameCount) return; frame = Time.frameCount;
        try { if (routine.MoveNext()) return; Finish(0); }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    private static void Finish(int code)
    {
        EditorApplication.update -= Step; (routine as IDisposable)?.Dispose(); routine = null;
        Application.logMessageReceived -= Log;
        Debug.Log($"Camera checks: {checks} passed, {errors.Count} captured errors; result={code}.");
        SceneCheckExit.Exit(code);
    }
    private static void Check(bool ok, string label)
    { if (!ok) throw new InvalidOperationException("Camera FAIL: " + label); checks++; Debug.Log("Camera PASS: " + label); }
    private static IEnumerator Run()
    {
        var diagnostic = Environment.GetCommandLineArgs().Contains("-wh30k-camera-diagnostic");
        var bootstrap = UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        bootstrap.ConfigureNewWorldTerrain(new TerrainSettings());
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard);
        bootstrap.GetComponent<ColonyClock>().TogglePause();
        var planet = bootstrap.ActivePlanet;
        planet.GetComponent<PlanetEcology>().enabled = false;
        var normal = bootstrap.GetComponent<Settlement>().CreateSnapshot().normal;
        var camera = Camera.main; var orbit = camera.GetComponent<SimpleOrbitCamera>();
        if (!diagnostic)
        {
            Check(bootstrap.GetComponent<ResearchSystem>() != null, "research component is wired in the saved scene");
            Check(orbit.DistantCamera != null && orbit.DistantCamera.eventMask == 0 && camera.eventMask == 0,
                "both cameras disable legacy automatic mouse picking; gameplay uses explicit rays");
        }
        orbit.FocusSettlement(normal);
        yield return null;
        for (var view = 0; view < 3; view++)
        {
            if (view == 1) orbit.FrameTarget();
            if (view == 2) { orbit.FocusSettlement(normal); orbit.Zoom(1); }
            yield return null;
            foreach (var candidate in Camera.allCameras)
            {
                Debug.Log($"Ray probe: {candidate.name}; near={candidate.nearClipPlane:R}; far={candidate.farClipPlane:R}; events={candidate.eventMask}");
                var before = errors.Count;
                foreach (var uv in new[] { new Vector2(.5f,.5f), new Vector2(.01f,.01f), new Vector2(.99f,.99f) })
                {
                    var p = candidate.ViewportToScreenPoint(uv);
                    var ray = candidate.ScreenPointToRay(p);
                    if (!diagnostic) Check(Finite(ray.origin) && Finite(ray.direction), "finite camera ray in view " + view + "/" + candidate.name);
                }
                Debug.Log($"Ray probe result: {candidate.name}; errors={errors.Count-before}");
            }
        }
        orbit.FocusSettlement(normal);
        yield return null;
        for (var i = 0; i < 80; i++) { planet.RefreshLodAtPosition(camera.transform.position); if (i % 8 == 0) yield return null; }
        var ship = bootstrap.GetComponent<Settlement>().CoreBuildings[0].GetComponent<Renderer>();
        var shipLod = ship.GetComponent<LODGroup>();
        Check(shipLod != null && shipLod.GetLODs().Length == 2,
            "deployed Explorator includes near and orbital renderers");
        var distantHull = shipLod.GetLODs()[1].renderers[0].GetComponent<MeshFilter>().sharedMesh;
        Check(distantHull.vertexCount < ship.GetComponent<MeshFilter>().sharedMesh.vertexCount / 10 &&
            Mathf.Abs(distantHull.bounds.size.z * ship.transform.lossyScale.z - 3000f) < 1f,
            "distant hull keeps the 3 km length with less than a tenth of detailed vertices");
        if (!diagnostic)
        {
            var mesh = ship.GetComponent<MeshFilter>().sharedMesh;
            Check(mesh.normals.Length == mesh.vertexCount && mesh.normals.All(n => Finite(n) && Mathf.Abs(n.magnitude-1)<.001f),
                "deployed hull retains finite unit lighting normals after its asymmetric pose");
        }
        var baseImage = Capture(camera, "daylight-default");
        if (diagnostic)
        {
            ship.receiveShadows = false; Capture(camera, "daylight-no-shadow"); ship.receiveShadows = true;
            var materials = ship.sharedMaterials;
            var relief = materials.Select(m => m.HasProperty("_Relief") ? m.GetFloat("_Relief") : 0).ToArray();
            for (var i=0;i<materials.Length;i++) if(materials[i].HasProperty("_Relief"))materials[i].SetFloat("_Relief",0);
            Capture(camera, "daylight-no-relief");
            for (var i=0;i<materials.Length;i++) if(materials[i].HasProperty("_Relief"))materials[i].SetFloat("_Relief",relief[i]);
            var standard = new Material(Shader.Find("Standard")) { color = new Color(.6f,.3f,.2f) };
            ship.sharedMaterials = Enumerable.Repeat(standard, materials.Length).ToArray();
            Capture(camera, "daylight-standard");
            var unlit = new Material(Shader.Find("Unlit/Color")) { color = Color.white };
            ship.sharedMaterials = Enumerable.Repeat(unlit, materials.Length).ToArray();
            Capture(camera, "daylight-unlit-mask");
            ship.sharedMaterials = materials;
            ship.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Capture(camera, "daylight-no-cast");
            ship.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            UnityEngine.Object.DestroyImmediate(standard); UnityEngine.Object.DestroyImmediate(unlit);
            for (var pose=0;pose<24;pose++)
            {
                orbit.FocusSettlement(normal); orbit.Navigate(new Vector2(pose%3-1,pose%5-2), pose*10);
                orbit.Zoom((pose-12)*.2f);
                yield return null;
                foreach(var candidate in Camera.allCameras)
                {
                    var before=errors.Count;
                    for(var x=1;x<8;x++)for(var y=1;y<6;y++)candidate.ScreenPointToRay(candidate.ViewportToScreenPoint(new Vector2(x/8f,y/6f)));
                    if(errors.Count>before)Debug.Log("Failing camera: "+candidate.name+"; pose="+pose);
                }
            }
        }
        else
        {
            foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(1920,1080)})
            {
                var pixels = Capture(camera, "daylight-"+size.x+"x"+size.y, size.x, size.y);
                var materials = ship.sharedMaterials;
                var maskMaterial = new Material(Shader.Find("Unlit/Color")){color=Color.magenta};
                Color32[] mask;
                try { ship.sharedMaterials=Enumerable.Repeat(maskMaterial,materials.Length).ToArray();mask=Capture(camera,"hull-mask-"+size.x,size.x,size.y); }
                finally { ship.sharedMaterials=materials;UnityEngine.Object.DestroyImmediate(maskMaterial); }
                var count=0;var lit=0;
                for(var i=0;i<mask.Length;i++)if(mask[i].r>250&&mask[i].b>250&&mask[i].g<5)
                {count++;if(pixels[i].r+pixels[i].g+pixels[i].b>15)lit++;}
                Check(count>10000 && lit>count*.85f, "daylit hull renders coloured surfaces instead of a black silhouette at "+size.x+"x"+size.y);
            }
            for(var pose=0;pose<24;pose++)
            {
                orbit.FocusSettlement(normal); orbit.Navigate(new Vector2(pose%3-1,pose%5-2),pose*10);
                orbit.Zoom((pose-12)*.2f);
                yield return null;
                ProbeLegacyMouse(camera);
            }
            Check(errors.Count == 0, "zoom/pan and native cursor dispatch complete without frustum or bounds errors");
        }
            foreach (var metres in new[] { 6000f, 18000f, 50000f })
            {
                orbit.FocusSettlement(normal);
                typeof(SimpleOrbitCamera).GetField("distance", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(orbit, metres);
                typeof(SimpleOrbitCamera).GetMethod("UpdateCameraTransform", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(orbit, null);
                for (var refine = 0; refine < 80; refine++)
                { planet.RefreshLodAtPosition(camera.transform.position); if (refine % 8 == 0) yield return null; }
                yield return null;
                Capture(camera, "zoom-" + metres);
                Check(shipLod.GetLODs()[1].renderers[0].bounds.size.z > 0,
                    "finite distant silhouette bounds at " + metres + " metres");
            }
        Debug.Log("Camera diagnostic completed; default render mean=" + baseImage.Average(c=>(c.r+c.g+c.b)/765f));
    }
    private static void ProbeLegacyMouse(Camera camera)
    {
        // Exercise the dispatcher named in the user's stack trace, including the exact
        // screenshot cursor positions. Restore the engine's input source in all cases.
        var type=typeof(Input).Assembly.GetType("UnityEngine.SendMouseEvents");
        var get=type.GetField("s_GetMouseState",BindingFlags.Static|BindingFlags.Public);
        var send=type.GetMethod("DoSendMouseEvents",BindingFlags.Static|BindingFlags.NonPublic);
        var used=type.GetField("s_MouseUsed",BindingFlags.Static|BindingFlags.NonPublic);
        var prior=get.GetValue(null);
        try
        {
            foreach(var uv in new[]{new Vector2(.43875f,.5377778f),new Vector2(.7304167f,.7407407f),new Vector2(.5f,.5f),new Vector2(.99f,.99f)})
            {
                var p=(Vector2)camera.ViewportToScreenPoint(uv);
                get.SetValue(null,new Func<KeyValuePair<int,Vector2>>(()=>new KeyValuePair<int,Vector2>(0,p)));
                used.SetValue(null,false); send.Invoke(null,new object[]{0});
            }
        }
        finally { get.SetValue(null,prior); }
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    private static Color32[] Capture(Camera camera, string name, int width=1280,int height=800)
    {
        var path = ".utmp/camera-review"; Directory.CreateDirectory(path);
        var target = camera.targetTexture; var active = RenderTexture.active;
        var clear = camera.clearFlags; var bg = camera.backgroundColor; var aspect = camera.aspect;
        var texture = new RenderTexture(width,height,24); texture.Create();
        var png = new Texture2D(width,height,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=texture;camera.aspect=width/(float)height;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.1f,.15f);
            camera.Render();RenderTexture.active=texture;png.ReadPixels(new Rect(0,0,width,height),0,0);png.Apply();
            File.WriteAllBytes(Path.Combine(path,name+".png"),png.EncodeToPNG());
            return png.GetPixels32();
        }
        finally
        {
            camera.targetTexture=target;camera.aspect=aspect;camera.clearFlags=clear;camera.backgroundColor=bg;RenderTexture.active=active;
            texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(png);
        }
    }
}
