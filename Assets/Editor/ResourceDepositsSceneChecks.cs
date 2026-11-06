using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WH30K.CameraSystem;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Sim.Settlements;
using WH30K.Sim;
using WH30K.Sim.Resources;
using WH30K.UI;

public static class ResourceDepositsSceneChecks
{
    private const string BatchKey = "WH30K.ResourceDeposits.Batch";
    public static void RunBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
        SessionState.SetBool(BatchKey, true);
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.isPlaying = true;
    }
    [InitializeOnLoadMethod]
    private static void ResumeBatch()
    {
        if (!SessionState.GetBool(BatchKey, false)) return;
        EditorApplication.playModeStateChanged += OnPlayMode;
        if (EditorApplication.isPlaying) EditorApplication.delayCall += FinishBatch;
    }
    private static void OnPlayMode(PlayModeStateChange state)
    { if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += FinishBatch; }
    private static void FinishBatch()
    {
        if (!SessionState.GetBool(BatchKey, false)) return;
        SessionState.EraseBool(BatchKey);
        try { Run(); SceneCheckExit.Exit(0); }
        catch (Exception error) { Debug.LogException(error); SceneCheckExit.Exit(1); }
    }
    [MenuItem("WH30K/Run Resource Deposits Checks (Play Mode)")]
    public static void Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode in WH30K_VS01 first.");
        var bootstrap = UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        var original = GameSettings.HasActiveGame && !bootstrap.ChoosingLanding ? bootstrap.CaptureSaveJson() : null;
        var terrain = bootstrap.NewWorldTerrain;
        var count = 0;
        void Check(bool ok, string label)
        { if (!ok) throw new InvalidOperationException("Resource deposits FAIL: " + label); count++; Debug.Log("Resource deposits PASS: " + label); }
        try
        {
            bootstrap.ConfigureNewWorldTerrain(new WH30K.Planet.TerrainSettings());
            bootstrap.BeginNewGame(8123, GameSettings.Difficulty.Standard);
            var industry = bootstrap.GetComponent<IndustrySystem>();
            var placement = bootstrap.GetComponent<BuildingPlacementController>();
            var colony = bootstrap.GetComponent<Settlement>();
            var planet = bootstrap.ActivePlanet;
            foreach (var deposit in industry.Deposits) deposit.Discovered = true;
            foreach (var kind in new[] { MaterialKind.IronOre, MaterialKind.CopperOre, MaterialKind.Crystal })
            {
                var deposit = industry.Deposits.First(d => d.Kind == kind && d.Remaining > 0);
                Check(industry.DefaultExtractionFocus("Mine", deposit.Position) == (kind == MaterialKind.CopperOre ? 1 : kind == MaterialKind.Crystal ? 2 : 0),
                    "placement automatically selects " + kind + " from the deposit beneath it");
            }
            SurfaceDeposit source = null;
            placement.SelectStructure("Mine");
            foreach (var deposit in industry.Deposits.Where(d => d.Kind == MaterialKind.CopperOre).OrderByDescending(d => d.Position.magnitude))
            {
                if (Vector3.Distance(deposit.Position, colony.CoreBuildings[0].transform.position) < 500) continue;
                if (planet.EvaluateBaseHeight(deposit.Direction) - deposit.Excavation.depth <= planet.SeaLevel + 15) continue;
                if (!placement.TryPlaceDirection(deposit.Direction)) continue;
                source = industry.DepositFor(placement.Buildings.Last()); break;
            }
            Check(source != null, "a real extractor is placed at a surveyed source");
            var mine = placement.Buildings[0];
            Check(mine.State.focus == 1 && source.Kind == MaterialKind.CopperOre,
                "new mine binds to copper without selecting a mineral");
            var mineMenu = bootstrap.GetComponent<NewGameMenu>();
            mineMenu.ShowBuilding(mine);
            Check(mineMenu.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("BuildingFocus")).All(b => !b.gameObject.activeSelf),
                "mine inspector needs no mineral selection controls");
            ShipSceneChecks.CaptureReview(bootstrap, mine, "automatic-mine");
            var mineralLabel = mineMenu.GetComponentsInChildren<Text>().First(t => t.name == "FocusDescription");
            Check(mineralLabel.text.Contains("Copper extraction") && mineralLabel.preferredHeight <= mineralLabel.rectTransform.rect.height + 1,
                "automatic mineral readout identifies copper and fits the inspector");
            mineMenu.ShowBuilding(null);
            var normal = planet.transform.InverseTransformPoint(mine.transform.position).normalized;
            var right = Vector3.Cross(normal, Mathf.Abs(normal.y) > .95f ? Vector3.right : Vector3.up).normalized;
            var forward = Vector3.Cross(normal, right);
            placement.SelectStructure("PromethiumGenerator");
            var powered = false;
            for (var i = 0; i < 64; i++)
            {
                var angle = i * Mathf.PI / 8; var distance = 70 + i / 16 * 20;
                if (!placement.TryPlaceDirection((normal + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * distance / planet.Radius).normalized)) continue;
                powered = true; break;
            }
            Check(powered, "a real generator supplies the remote extraction network");
            var saved = industry.Capture();
            saved.facilities[1].quantities[(int)MaterialKind.Promethium] = 80;
            industry.Configure(planet, colony.CreateSnapshot().normal, saved);
            source = industry.Deposits[source.Id];
            industry.Simulate(20);
            RunGameplay(bootstrap, source, Check);
            var initial = source.InitialVolume; var remaining = source.Remaining; var id = source.Id;
            Check(bootstrap.TryLoadSaveJson(bootstrap.CaptureSaveJson()), "colony with partially used deposit loads through actual save validation");
            Check(placement.Buildings[0].State.focus == 1 && industry.DepositFor(placement.Buildings[0])?.Kind == MaterialKind.CopperOre,
                "automatically selected copper persists through save and reload");
            planet = bootstrap.ActivePlanet;
            source = industry.Deposits[id];
            Check(source.InitialVolume == initial && source.Remaining == remaining && source.Usage.MonthlyRate(industry.DepositUsageSeconds) == 0,
                "load retains exact start/remaining reserves and starts fresh usage observations");
            placement.Buildings[0].ToggleOperation(); industry.Simulate(31);
            Check(source.Remaining == remaining && source.Usage.MonthlyRate(industry.DepositUsageSeconds) == 0,
                "offline extraction preserves reserves and has no finite forecast");
            saved = industry.Capture();
            for (var i = 0; i < saved.deposits.Length; i++) saved.deposits[i].initialVolume = 0;
            industry.Configure(planet, colony.CreateSnapshot().normal, saved);
            source = industry.Deposits[id];
            Check(source.InitialVolume == MiningRules.Capacity(source.Excavation) && source.Remaining == remaining
                && industry.Deposits.Where(d => d.Excavation == null).All(d => d.InitialVolume == 0),
                "legacy start-volume migration restores geological capacity and retains unknown biomass/fuel starts without refilling");
            saved = industry.Capture();
            MiningRules.ExtractSolid(saved.deposits[id].excavation, float.MaxValue);
            saved.deposits[id].remaining = 0;
            industry.Configure(planet, colony.CreateSnapshot().normal, saved);
            var menu = bootstrap.GetComponent<NewGameMenu>();
            var open = menu.GetComponentsInChildren<Button>(true).First(b => b.name == "OpenResourceDeposits");
            var panel = menu.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "ResourceDepositsPane").gameObject;
            panel.SetActive(false); open.onClick.Invoke();
            Check(panel.GetComponentsInChildren<Text>().Any(t => t.text == "EXHAUSTED"),
                "exhausted assigned mineral source remains in the live pane with an explicit exhausted status");
            mine = placement.Buildings[0]; var position = mine.transform.position;
            mine.transform.position += mine.transform.up * 1000;
            typeof(NewGameMenu).GetMethod("RefreshResourceDepositsPane", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
            Check(panel.GetComponentsInChildren<Text>().Any(t => t.name == "NoExtractionDeposits")
                && !panel.GetComponentsInChildren<Button>().Any(b => b.name.StartsWith("ExtractionDeposit")),
                "no assigned sources shows the empty explanation and hides stale deposit rows");
            mine.transform.position = position; panel.SetActive(false);
            Debug.Log($"Resource deposits: {count} actual scene checks passed in Unity {Application.unityVersion}.");
        }
        finally
        {
            bootstrap.ConfigureNewWorldTerrain(terrain);
            if (original != null && !bootstrap.TryLoadSaveJson(original)) Debug.LogError("Could not restore original in-memory colony.");
        }
    }

    // Called by the solid-mining acceptance fixture after real powered extraction.
    public static void RunGameplay(PlanetBootstrap bootstrap, SurfaceDeposit source, Action<bool, string> check)
    {
        const string preference = "WH30K.ResourceDeposits.Pinned";
        var hadPreference = PlayerPrefs.HasKey(preference); var oldPreference = PlayerPrefs.GetInt(preference);
        var menu = bootstrap.GetComponent<NewGameMenu>();
        var industry = bootstrap.GetComponent<IndustrySystem>();
        var buttons = menu.GetComponentsInChildren<Button>(true);
        var panel = menu.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "ResourceDepositsPane").gameObject;
        var open = buttons.First(b => b.name == "OpenResourceDeposits");
        var pin = buttons.First(b => b.name == "PinResourceDeposits");
        var close = buttons.First(b => b.name == "CloseResourceDeposits");
        var pinField = typeof(NewGameMenu).GetField("resourceDepositsPinned", BindingFlags.NonPublic | BindingFlags.Instance);
        try
        {
            if ((bool)pinField.GetValue(menu)) pin.onClick.Invoke();
            panel.SetActive(false);
            var before = bootstrap.CaptureSaveJson();
            open.onClick.Invoke();
            check(panel.activeInHierarchy && !bootstrap.GetComponent<ColonyClock>().DecisionPending,
                "resource pane opens through the actual HUD control without pausing simulation");
            var rows = new List<SurfaceDeposit>(); industry.FillExtractionDeposits(rows);
            check(rows.Count == 1 && rows[0] == source, "only the actual assigned deposit appears once in the extraction list");
            var rate = source.Usage.MonthlyRate(industry.DepositUsageSeconds);
            check(rate > 0 && source.InitialVolume > source.Remaining && DepositUsage.LifeMonths(source.Remaining, rate) > 0,
                "actual powered removal supplies the monthly rate and remaining-life forecast");
            var cells = panel.GetComponentsInChildren<Text>();
            check(cells.Any(t => t.name == "DepositCell1" && t.text == source.InitialVolume.ToString("N1"))
                && cells.Any(t => t.name == "DepositCell2" && t.text == source.Remaining.ToString("N1"))
                && cells.Any(t => t.name == "DepositCell4" && t.text == $"{DepositUsage.LifeMonths(source.Remaining, rate):N1} months"),
                "visible start, remaining and monthly-life cells reconcile with actual simulation values");
            pin.onClick.Invoke();
            check((bool)pinField.GetValue(menu) && PlayerPrefs.GetInt(preference) == 1 && panel.GetComponent<RectTransform>().anchorMin == new Vector2(0, 1),
                "pin control docks to the HUD and persists the preference");
            check(bootstrap.CaptureSaveJson() == before, "opening and pinning the pane cannot mutate simulation or colony save state");
            menu.ShowBuilding(bootstrap.GetComponent<BuildingPlacementController>().Buildings[0]);
            check(panel.activeSelf, "pinned resource pane remains visible when a building is selected");
            menu.ShowBuilding(null);
            var row = panel.GetComponentsInChildren<Button>().First(b => b.name == "ExtractionDeposit0");
            using (var render = new OutputTooltipSceneChecks.BatchUiRender(menu.GetComponentInChildren<Canvas>()))
            {
                Canvas.ForceUpdateCanvases();
                var rect = row.GetComponent<RectTransform>();
                var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(render.Camera, rect.TransformPoint(rect.rect.center)) };
                var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
                check(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(row.transform), "deposit row is reachable by actual UI raycasting");
                ExecuteEvents.Execute(row.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            }
            check(Vector3.Distance(Camera.main.GetComponent<SimpleOrbitCamera>().NavigationDirection, source.Direction) < .00001f
                && industry.Overlay == (int)source.Kind + 2 && panel.activeSelf,
                "clicking a pinned row focuses the actual deposit and resource overlay while retaining the pane");
            ShipSceneChecks.CaptureReview(bootstrap, bootstrap.GetComponent<WH30K.Sim.Settlements.Settlement>().CoreBuildings[0], "resource-deposits");
            check(panel.GetComponentsInChildren<Text>().All(t => t.preferredHeight <= t.rectTransform.rect.height + 1),
                "resource pane title, notes, headings and row text fit their vertical bounds");
            close.onClick.Invoke(); open.onClick.Invoke();
            check(panel.activeSelf && (bool)pinField.GetValue(menu), "closed pinned pane reopens with its docking preference");
            pin.onClick.Invoke(); row.onClick.Invoke();
            check(!panel.activeSelf, "visiting a deposit from the unpinned pane dismisses the window");
        }
        finally
        {
            pinField.SetValue(menu, oldPreference != 0);
            typeof(NewGameMenu).GetMethod("PositionResourceDepositsPane", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
            panel.SetActive(false);
            if (hadPreference) PlayerPrefs.SetInt(preference, oldPreference); else PlayerPrefs.DeleteKey(preference);
            PlayerPrefs.Save();
        }
    }
}
