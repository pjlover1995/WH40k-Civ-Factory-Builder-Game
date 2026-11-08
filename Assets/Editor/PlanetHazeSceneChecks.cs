using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WH30K.CameraSystem;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Planet;
using WH30K.Sim;
using WH30K.Sim.Settlements;

public static class PlanetHazeSceneChecks
{
    private const string Key = "WH30K.PlanetHazeChecks";
    private static IEnumerator routine;
    private static int frame = -1, checks;
    private static readonly List<string> errors = new List<string>();

    [MenuItem("WH30K/Run Planet Haze and Distant LOD Checks (Play Mode)")]
    public static void RunBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.playModeStateChanged += State;
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if (SessionState.GetBool(Key, false)) EditorApplication.playModeStateChanged += State;
    }

    private static void State(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        EditorApplication.delayCall += () =>
        {
            errors.Clear(); checks = 0; Application.logMessageReceived += Log;
            routine = Run(); EditorApplication.update += Step;
        };
    }

    private static void Log(string message, string stack, LogType kind)
    {
        if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) errors.Add(message);
    }

    private static void Step()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        try { if (routine.MoveNext()) return; Finish(0); }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Step; (routine as IDisposable)?.Dispose(); routine = null;
        Application.logMessageReceived -= Log;
        Debug.Log($"Planet haze checks: {checks} passed, {errors.Count} captured errors; result={code}.");
        SceneCheckExit.Exit(code);
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Planet haze FAIL: " + label);
        checks++; Debug.Log("Planet haze PASS: " + label);
    }

    private static IEnumerator Run()
    {
        var bootstrap = UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        if (bootstrap == null) throw new InvalidOperationException("Prototype scene has no PlanetBootstrap.");
        bootstrap.ConfigureNewWorldTerrain(new TerrainSettings());
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard);
        var planet = bootstrap.ActivePlanet;
        var presentation = planet.GetComponent<PlanetPresentation>();
        var camera = Camera.main;
        var orbit = camera.GetComponent<SimpleOrbitCamera>();
        planet.GetComponent<PlanetEcology>().enabled = false;
        bootstrap.GetComponent<ColonyClock>().TogglePause();
        var normal = bootstrap.GetComponent<Settlement>().CreateSnapshot().normal;
        orbit.FocusSettlement(normal);
        var distance = typeof(SimpleOrbitCamera).GetField("distance", BindingFlags.NonPublic | BindingFlags.Instance);
        var updateCamera = typeof(SimpleOrbitCamera).GetMethod("UpdateCameraTransform", BindingFlags.NonPublic | BindingFlags.Instance);
        if (distance == null || updateCamera == null) throw new InvalidOperationException("Orbit camera test controls are unavailable.");
        distance.SetValue(orbit, 1800f); updateCamera.Invoke(orbit, null);
        for (var i = 0; i < 80; i++)
        {
            planet.RefreshLodAtPosition(camera.transform.position);
            yield return null;
        }
        yield return null;

        Check(planet.FarViewRootCellCount == 64,
            "large-world far terrain uses a 64-cell root rather than the previous 128-cell root");
        var oldRootTriangles = 6 * (2 * 128 * 128 + 8 * 128);
        Check(planet.FarViewRootTriangleCount < oldRootTriangles * .30f,
            $"far-view root terrain workload is reduced by at least 70% ({planet.FarViewRootTriangleCount:N0} vs {oldRootTriangles:N0} triangles)");

        presentation.SetAtmosphericPerspectiveEnabled(false);
        distance.SetValue(orbit, 1801f); updateCamera.Invoke(orbit, null);
        distance.SetValue(orbit, 1800f); updateCamera.Invoke(orbit, null);
        yield return null;
        var baselineFarClip = camera.farClipPlane;
        Capture(camera, "surface-clear-1280x800", 1280, 800);
        var baseline = SampleFrames(60); while (baseline.MoveNext()) yield return null;
        var baselineMetrics = ReadMetric;
        var baselineTriangles = planet.VisibleTerrainTriangleCount;

        presentation.SetAtmosphericPerspectiveEnabled(true);
        yield return null;
        yield return null;
        var hazeFarClip = camera.farClipPlane;
        Check(presentation.VisibilityLimited && presentation.FogEndDistance > presentation.FogStartDistance,
            "surface view activates a bounded atmospheric fade");
        Check(hazeFarClip <= presentation.FogEndDistance + 2501f && hazeFarClip < baselineFarClip * .5f,
            $"fog-end clipping culls the far horizon range ({baselineFarClip:N0} m to {hazeFarClip:N0} m)");
        Capture(camera, "surface-haze-1280x800", 1280, 800);
        Capture(camera, "surface-haze-1920x1080", 1920, 1080);
        var haze = SampleFrames(60); while (haze.MoveNext()) yield return null;
        var hazeMetrics = ReadMetric;
        var hazeTriangles = planet.VisibleTerrainTriangleCount;
        Debug.Log($"Planet haze workload: baseline far clip={baselineFarClip:0} m, visible triangles={baselineTriangles:N0}, frame mean/p95={baselineMetrics.mean:0.00}/{baselineMetrics.p95:0.00} ms, CPU mean={baselineMetrics.cpuMean:0.00} ms, GPU mean={baselineMetrics.gpuMean:0.00} ms ({baselineMetrics.timedFrames} samples); haze far clip={hazeFarClip:0} m, visible triangles={hazeTriangles:N0}, frame mean/p95={hazeMetrics.mean:0.00}/{hazeMetrics.p95:0.00} ms, CPU mean={hazeMetrics.cpuMean:0.00} ms, GPU mean={hazeMetrics.gpuMean:0.00} ms ({hazeMetrics.timedFrames} samples); LOD refresh={planet.LastLodMilliseconds:0.000} ms, far roots={planet.FarViewRootTriangleCount:N0} triangles.");

        distance.SetValue(orbit, 200000f); updateCamera.Invoke(orbit, null);
        yield return null;
        Check(!presentation.VisibilityLimited,
            "atmospheric clipping releases at high orbital altitude");
        Check(errors.Count == 0, "planet haze checks complete without engine errors");
    }

    private static (float mean, float p95, float cpuMean, float gpuMean, int timedFrames) ReadMetric;

    private static IEnumerator SampleFrames(int count)
    {
        var samples = new List<float>(count);
        var cpu = new List<float>(count);
        var gpu = new List<float>(count);
        for (var i = 0; i < count; i++)
        {
            FrameTimingManager.CaptureFrameTimings();
            yield return null;
            samples.Add(Time.unscaledDeltaTime * 1000f);
            var timing = new FrameTiming[1];
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                if (timing[0].cpuFrameTime > 0) cpu.Add((float)timing[0].cpuFrameTime);
                if (timing[0].gpuFrameTime > 0) gpu.Add((float)timing[0].gpuFrameTime);
            }
        }
        samples.Sort();
        ReadMetric = (samples.Average(), samples[Mathf.Clamp(Mathf.CeilToInt(samples.Count * .95f) - 1, 0, samples.Count - 1)],
            cpu.Count > 0 ? cpu.Average() : 0, gpu.Count > 0 ? gpu.Average() : 0, Math.Min(cpu.Count, gpu.Count));
    }

    private static void Capture(Camera camera, string name, int width, int height)
    {
        const string folder = ".utmp/planet-haze-review";
        Directory.CreateDirectory(folder);
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        var oldAspect = camera.aspect; var oldFlags = camera.clearFlags; var oldBackground = camera.backgroundColor;
        var target = new RenderTexture(width, height, 24); target.Create();
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.aspect = width / (float)height;
            camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = oldTarget; camera.aspect = oldAspect;
            camera.clearFlags = oldFlags; camera.backgroundColor = oldBackground; RenderTexture.active = oldActive;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
