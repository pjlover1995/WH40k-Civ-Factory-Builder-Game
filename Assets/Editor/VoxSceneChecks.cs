using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Sim;
using WH30K.Sim.Resources;
using WH30K.UI;

public static class VoxSceneChecks
{
    private const string BatchKey = "WH30K.VoxChecks.Batch";

    [MenuItem("WH30K/Run Vox Officer Checks (Play Mode)")]
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
        try { Run(); SceneCheckExit.Exit(0); }
        catch (Exception error) { Debug.LogException(error); SceneCheckExit.Exit(1); }
    }

    [MenuItem("WH30K/Run Vox Officer Checks (Play Mode)", true)]
    private static bool ValidateRunBatch() => !EditorApplication.isPlayingOrWillChangePlaymode;

    public static void Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode in WH30K_VS01.");
        var bootstrap = UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        if (bootstrap == null) throw new InvalidOperationException("Prototype scene has no PlanetBootstrap.");
        var original = GameSettings.HasActiveGame && !bootstrap.ChoosingLanding ? bootstrap.CaptureSaveJson() : null;
        var terrain = bootstrap.NewWorldTerrain;
        var checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Vox FAIL: " + label);
            checks++; Debug.Log("Vox PASS: " + label);
        }
        try
        {
            bootstrap.ConfigureNewWorldTerrain(new WH30K.Planet.TerrainSettings());
            bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard);
            var menu = bootstrap.GetComponent<NewGameMenu>();
            var clock = bootstrap.GetComponent<ColonyClock>();
            var resources = bootstrap.GetComponent<ResourceSystem>();
            var card = FindUi(menu, "VoxPortraitCard");
            var captain = Resources.Load<Texture2D>("Vox/Captain");
            var priest = Resources.Load<Texture2D>("Vox/MechanicumPriest");
            Check(captain != null && priest != null, "both officer portraits import from Resources");
            Check(card != null && !card.activeSelf, "portrait card is wired into the real HUD and starts hidden");
            var portrait = card != null ? card.GetComponentInChildren<RawImage>(true) : null;
            Check(portrait != null && !portrait.raycastTarget, "portrait image does not intercept UI clicks");
            typeof(NewGameMenu).GetMethod("ToggleVoxLog", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, null);

            menu.ReportVoxResearch("Noosphere Cartography");
            menu.ReportVoxSurvey("an iron bearing ridge");
            SetClock(clock, 0);
            Refresh(menu);
            var history = GameObject.Find("VoxHistory")?.GetComponent<Text>();
            Check(history != null && history.text.Contains("Research completed") && history.text.Contains("Survey finding"),
                "explicit research and survey reports enter the session transcript even with the clock paused");
            Check(history.text.Contains("MECHANICUM PRIEST") && history.text.Contains("Noosphere Cartography")
                && history.text.Contains("iron bearing ridge"), "priest event copy and officer identity appear in transcript");

            bootstrap.BeginNewGame(5678, GameSettings.Difficulty.Standard);
            menu = bootstrap.GetComponent<NewGameMenu>();
            clock = bootstrap.GetComponent<ColonyClock>();
            resources = bootstrap.GetComponent<ResourceSystem>();
            card = FindUi(menu, "VoxPortraitCard");
            resources.ConsumeFood(float.MaxValue);
            resources.ConsumeWater(float.MaxValue);
            SetClock(clock, 0);
            Refresh(menu); // establish the session origin and fresh-session grace period
            SetClock(clock, 100);
            Refresh(menu); // begin observing both shortage conditions
            SetClock(clock, 145);
            Refresh(menu); // sustain both shortages for 45 simulation seconds
            var heading = GameObject.Find("VoxSpeaker")?.GetComponent<Text>();
            Check(card != null && card.activeSelf && heading != null && heading.text.StartsWith("CAPTAIN"),
                "sustained food and water shortages produce a captain transmission from simulation clock fixtures");
            var tracker = FindUi(menu, "FoundingObjectivesTracker");
            tracker.SetActive(true);
            Refresh(menu);
            Check(tracker.activeInHierarchy && !RectsOverlap((RectTransform)card.transform, (RectTransform)tracker.transform),
                    "visible Vox card stays clear of the active founding objectives tracker");

            var eventPanel = FindUi(menu, "EventPanel");
            if (eventPanel != null) eventPanel.SetActive(true);
            Refresh(menu);
            Check(card != null && !card.activeSelf, "open narrative modal suppresses the officer card");
            if (eventPanel != null) eventPanel.SetActive(false);

            var toggleLog = typeof(NewGameMenu).GetMethod("ToggleVoxLog", BindingFlags.Instance | BindingFlags.NonPublic);
            var voxButton = FindUi(menu, "OpenVoxLog").GetComponent<Button>();
            var researchButton = FindUi(menu, "OpenResearch").GetComponent<Button>();
            Canvas.ForceUpdateCanvases();
            Check(!RectsOverlap((RectTransform)voxButton.transform, (RectTransform)researchButton.transform),
                "research and vox log have separate hit areas");
            voxButton.onClick.Invoke();
            var log = GameObject.Find("VoxLog");
            Check(log != null && log.activeSelf, "vox transcript can be opened from the HUD");
            bootstrap.BeginNewGame(7890, GameSettings.Difficulty.Standard);
            Check(log != null && !log.activeSelf, "starting a new expedition closes the previous session transcript");
            toggleLog.Invoke(menu, null);
            history = GameObject.Find("VoxHistory")?.GetComponent<Text>();
            Check(log != null && log.activeSelf && history != null && history.text == "No transmissions received in this session.",
                "opening the new session transcript shows no transmissions from the prior expedition");
            Debug.Log($"Vox: {checks} scene checks passed in Unity {Application.unityVersion}.");
        }
        finally
        {
            bootstrap.ConfigureNewWorldTerrain(terrain);
            if (original != null && !bootstrap.TryLoadSaveJson(original)) Debug.LogError("Could not restore the in-memory colony after Vox checks.");
        }
    }

    private static GameObject FindUi(NewGameMenu menu, string name)
        => Array.Find(menu.GetComponentsInChildren<RectTransform>(true), item => item.name == name)?.gameObject;

    private static void SetClock(ColonyClock clock, double seconds)
        => clock.Restore(new ClockSnapshot { simulationSeconds = seconds, playedSeconds = 0, speed = 1, paused = true });

    private static bool RectsOverlap(RectTransform a, RectTransform b)
    {
        var ac = new Vector3[4]; var bc = new Vector3[4];
        a.GetWorldCorners(ac); b.GetWorldCorners(bc);
        return ac[0].x < bc[2].x && ac[2].x > bc[0].x && ac[0].y < bc[2].y && ac[2].y > bc[0].y;
    }

    private static void Refresh(NewGameMenu menu)
        => typeof(NewGameMenu).GetMethod("RefreshVox", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, null);
}
