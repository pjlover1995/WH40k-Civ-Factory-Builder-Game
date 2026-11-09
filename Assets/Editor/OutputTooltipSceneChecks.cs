#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Sim;
using WH30K.Sim.Environment;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;
using WH30K.UI;

public static class OutputTooltipSceneChecks
{
    [MenuItem("WH30K/Run Output Tooltip Checks (Play Mode)")]
    public static void Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode in WH30K_VS01 first.");
        var bootstrap = UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        if (bootstrap == null) throw new InvalidOperationException("Open WH30K_VS01 first.");
        var original = GameSettings.HasActiveGame && !bootstrap.ChoosingLanding ? bootstrap.CaptureSaveJson() : null;
        var count = 0;
        try
        {
            RunGameplay(bootstrap, (ok, name) => {
                if (!ok) throw new InvalidOperationException("Output tooltip FAIL: " + name);
                count++; Debug.Log("Output tooltip PASS: " + name);
            });
            Debug.Log($"Output tooltip: {count} actual scene checks passed in Unity {Application.unityVersion}.");
        }
        finally
        {
            if (original != null && !bootstrap.TryLoadSaveJson(original))
                Debug.LogError("Could not restore the original in-memory colony after output tooltip checks.");
        }
    }

    public static void RunGameplay(PlanetBootstrap bootstrap, Action<bool, string> check)
    {
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard, legacyCore: true);
        var menu = bootstrap.GetComponent<NewGameMenu>();
        var canvas = menu.GetComponentInChildren<Canvas>();
        FinishMenuFadeOut(canvas);
        using var uiRender = new BatchUiRender(canvas);
        var settlement = bootstrap.GetComponent<Settlement>();
        var resources = bootstrap.GetComponent<ResourceSystem>();
        var environment = bootstrap.GetComponent<EnvironmentState>();
        var clock = bootstrap.GetComponent<ColonyClock>();
        if (!clock.Paused) clock.TogglePause();
        var targets = menu.GetComponentsInChildren<ColonyOutputHover>(true);
        check(targets.Length == 5, "food, water, supplies, population and workforce have hover targets in the actual runtime HUD");
        var target = targets.First(t => t.name == "NetHover");
        var tooltip = menu.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "ColonyOutputTooltip");
        var text = tooltip.GetComponentInChildren<Text>(true);
        var before = bootstrap.CaptureSaveJson();
        var pointer = new PointerEventData(EventSystem.current);
        Canvas.ForceUpdateCanvases();
        var targetRect = (RectTransform)target.transform;
        var netPoint = RectTransformUtility.WorldToScreenPoint(uiRender.Camera, targetRect.TransformPoint(targetRect.rect.center));
        pointer.position = netPoint;
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        check(hits.Count > 0 && hits[0].gameObject == target.gameObject,
            "the net metric is reachable by a real UI raycast across its title/value hover area");
        var reserveTargets = new[] { (target: targets.First(t => t.name == "StockpileHover"), title: "FOOD"),
            (target: targets.First(t => t.name == "WaterSupplyHover"), title: "WATER") };
        foreach (var metric in reserveTargets)
        {
            var reserveRect = (RectTransform)metric.target.transform;
            var titleRect = (RectTransform)reserveRect.parent.Find(metric.target.name.Replace("Hover", "Title"));
            pointer.position = RectTransformUtility.WorldToScreenPoint(uiRender.Camera, titleRect.TransformPoint(titleRect.rect.center));
            EventSystem.current.RaycastAll(pointer, hits);
            check(hits.Count > 0 && hits[0].gameObject == metric.target.gameObject,
                metric.title + " reserve accepts a real UI raycast over its title. Hit order: "
                + string.Join(", ", hits.Select(hit => hit.gameObject.name)));
        }
        pointer.position = netPoint;
        ExecuteEvents.Execute(target.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
        Canvas.ForceUpdateCanvases();
        var report = resources.CreateSnapshot();
        check(tooltip.gameObject.activeInHierarchy && text.text == settlement.OutputExplanation
            && text.text.Contains("COLONY SUPPLIES & GROWTH"),
            "pointer enter opens a breakdown that reconciles with the displayed simulation report while paused");
        check(bootstrap.CaptureSaveJson() == before, "hovering cannot change stockpiles, population, time or saved state");
        CheckLayout(tooltip, text, check);
        check(tooltip.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget),
            "the explanation cannot intercept the pointer or obstruct underlying UI controls");
        ExecuteEvents.Execute(target.gameObject, pointer, ExecuteEvents.pointerExitHandler);
        check(!tooltip.gameObject.activeSelf, "pointer exit dismisses the explanation");
        ExecuteEvents.Execute(target.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
        target.gameObject.SetActive(false);
        check(!tooltip.gameObject.activeSelf, "disabling the hover target cannot leave a stale panel open");
        target.gameObject.SetActive(true);
        ExecuteEvents.Execute(target.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
        resources.ConsumeWater(resources.Water);
        environment.ApplyImmediateImpact(new EnvironmentImpact { co2Delta = 1500, o2Delta = -5, waterDelta = 60, temperatureDelta = 8 });
        settlement.RefreshReadout();
        check(text.text.Contains("Water reserve empty") && text.text.Contains("- CO2") && text.text.Contains("- Oxygen")
            && text.text.Contains("- Water pollution") && text.text.Contains("- Temperature"),
            "an already-open hover refreshes with all current water and environmental constraints");
        CheckLayout(tooltip, text, check);
        var stressed = bootstrap.CaptureSaveJson();
        check(bootstrap.TryLoadSaveJson(stressed), "a colony with growth constraints reloads through existing save migration");
        ExecuteEvents.Execute(target.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
        check(text.text.Contains("Water reserve empty") && text.text == settlement.OutputExplanation,
            "hover diagnostics rebuild from saved colony conditions without a schema change");
        menu.ShowNewGamePanel(true);
        ExecuteEvents.Execute(target.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
        check(!tooltip.gameObject.activeSelf, "new expedition modal dismisses and suppresses the hover panel");
        menu.ShowNewGamePanel(false);
        check(bootstrap.TryLoadSaveJson(before), "the healthy paused fixture restores after the hover checks");
        FinishMenuFadeOut(canvas);
        foreach (var metric in reserveTargets)
        {
            ExecuteEvents.Execute(metric.target.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            check(text.text.Contains(metric.title + " OUTLOOK") && text.text.Contains("Measuring inflow")
                && text.text.Contains("Reserve coverage"), metric.title + " outlook starts a fresh observation on load");
            CheckLayout(tooltip, text, check);
            ExecuteEvents.Execute(metric.target.gameObject, pointer, ExecuteEvents.pointerExitHandler);
        }
    }

    public static void RunSupplies(PlanetBootstrap bootstrap, Action<bool,string> check)
    {
        bootstrap.ConfigureNewWorldTerrain(new WH30K.Planet.TerrainSettings());
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard, chooseLanding: true);
        check(bootstrap.ConfirmLanding(), "the selected medium site deploys a tender with orbital supply runs");
        GameObject.Find("DeclineColonyGuide").GetComponent<Button>().onClick.Invoke();
        var colony=bootstrap.GetComponent<Settlement>();
        var industry=bootstrap.GetComponent<IndustrySystem>();
        var resources=bootstrap.GetComponent<ResourceSystem>();
        var placement=bootstrap.GetComponent<WH30K.Gameplay.Construction.BuildingPlacementController>();
        var menu=bootstrap.GetComponent<NewGameMenu>();
        FinishMenuFadeOut(menu.GetComponentInChildren<Canvas>());
        var normal=colony.CreateSnapshot().normal;
        check(LandingSceneChecks.PlaceNear(bootstrap,"GroundwaterPump",normal),"normal placement builds the founding pump");
        check(LandingSceneChecks.PlaceNear(bootstrap,"Farm",normal),"normal placement builds the founding farm");
        var supplies=industry.ConstructionSupplyTotals().basic;
        var water=resources.Water; var food=resources.Food;
        industry.Simulate(150);
        check(industry.SupplyOutlook.Ready && industry.SupplyOutlook.WaterPerMonth > 0
            && industry.SupplyOutlook.FoodPerMonth > 0 && industry.SupplyOutlook.FoodPerMonth < FarmRules.FoodCapacity,
            "a full simulated month measures actual operating pump water and delivered farm food");
        check(resources.Water>water,"one connected pump replenishes water against current colony demand");
        check(resources.Food>food-500 && industry.FoodTransportCount>0,
            "founding farm receives ship components and crew waste without injected inventory and feeds the actual colony");
        check(industry.ConstructionSupplyTotals().basic<supplies,"farm operation consumes finite physical supplies");
        var farmDelivered = industry.SupplyOutlook.FoodPerMonth;
        var storedFood = resources.Food;
        check(industry.TryOrbitalSupplyRun(0, 100, 0, 0) && resources.Food == storedFood + 100
            && industry.SupplyOutlook.FoodPerMonth == farmDelivered,
            "a real orbital ration run increases reserves without counting as farm deliveries");
        CheckReadyReserveHovers(bootstrap, colony, menu, check);
        var oldCurrency=resources.CreateSnapshot().Stockpile;
        colony.DebugRunImmediateTick();
        check(resources.CreateSnapshot().Stockpile==oldCurrency,"settlement output cannot generate or charge retired currency");
        check(!industry.TrySpendConstruction(float.NaN,0) && !industry.TrySpendConstruction(-1,0),"invalid construction costs cannot corrupt physical inventories");
        var pump=placement.Buildings.First(b=>b.Id=="GroundwaterPump");
        check(pump.Upgrade(),"player approval begins a pump improvement using construction supplies");
        var beforeUpgrade=industry.ConstructionSupplyTotals().basic;
        pump.SimulateImprovement(10,1);
        check(pump.State.improvementWork>0 && industry.ConstructionSupplyTotals().basic<beforeUpgrade,
            "improvement work spends physical components rather than the archived currency balance");
        var save=bootstrap.CaptureSaveJson();
        var supplyBefore=industry.ConstructionSupplyTotals();
        check(bootstrap.TryLoadSaveJson(save) && industry.ConstructionSupplyTotals()==supplyBefore,
            "save reload preserves exact physical supplies with no currency conversion or refill");
        check(!industry.SupplyOutlook.Ready && industry.SupplyOutlook.FoodPerMonth == 0 && industry.SupplyOutlook.WaterPerMonth == 0,
            "loading a measured colony discards only its outlook observations");
        menu.UpdateResourceReadout(resources.CreateSnapshot());
        var labels=menu.GetComponentsInChildren<Text>(true);
        check(labels.Any(t=>t.name=="StockpileValue" && t.text.Contains("mo"))
            && labels.Any(t=>t.name=="WaterSupplyValue" && t.text.Contains("mo"))
            && labels.Any(t=>t.name=="NetValue" && t.text.Contains("plasteel"))
            && labels.All(t=>!t.text.Contains("CREDITS")),"runtime command banner exposes food/water months and physical construction supplies");
        ShipSceneChecks.CaptureReview(bootstrap,colony.CoreBuildings[0],"supplies");
        check(menu.GetComponentsInChildren<Text>().Where(t=>t.transform.parent.name=="CommandHeader")
            .All(t=>t.preferredWidth<=t.rectTransform.rect.width+1 && t.preferredHeight<=t.rectTransform.rect.height+1),
            "all command banner labels fit their cells at the review resolutions");
        check(LandingSceneChecks.PlaceNear(bootstrap,"Manufactorum",normal),"renewable supply building is available through normal construction");
        var factory=placement.Buildings.Last();
        check(industry.FacilitySettings(factory).products[0]==0,"new Manufactorum defaults to the direct raw-material construction recipe");
        var physical=industry.Capture();
        physical.facilities[Array.IndexOf(placement.Buildings,factory)].quantities[(int)MaterialKind.Metal]=3;
        industry.Configure(bootstrap.ActivePlanet,colony.CreateSnapshot().normal,physical);
        industry.Simulate(1);
        check(industry.Capture().cargo.Any(c=>c.target==-3 && c.kind==(int)MaterialKind.Metal),"surplus material dispatches a real ship-bound hauler");
        check(bootstrap.TryLoadSaveJson(bootstrap.CaptureSaveJson())
            && industry.Capture().cargo.Any(c=>c.target==-3 && c.kind==(int)MaterialKind.Metal),
            "save/load preserves an actual travelling ship-bound supply hauler");
    }

    private static void FinishMenuFadeOut(Canvas canvas)
    {
        // This fixture begins and checks colonies in the same frame, before the cinematic fade can finish.
        foreach (var panelName in new[] { "CinematicMainMenu", "NewGamePanel" })
        {
            var panel = canvas.transform.Find(panelName);
            var group = panel.GetComponent<CanvasGroup>();
            group.alpha = 0;
            group.interactable = false;
            group.blocksRaycasts = false;
            panel.gameObject.SetActive(false);
        }
    }

    private static void CheckReadyReserveHovers(PlanetBootstrap bootstrap, Settlement colony, NewGameMenu menu, Action<bool, string> check)
    {
        var tooltip = menu.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "ColonyOutputTooltip");
        var text = tooltip.GetComponentInChildren<Text>(true);
        var pointer = new PointerEventData(EventSystem.current);
        var targets = menu.GetComponentsInChildren<ColonyOutputHover>();
        var before = bootstrap.CaptureSaveJson();
        foreach (var metric in new[] {
            (name: "StockpileHover", source: "Farm food delivered", capture: "reserve-outlook-food"),
            (name: "WaterSupplyHover", source: "Pump water added", capture: "reserve-outlook-water") })
        {
            var target = targets.First(t => t.name == metric.name);
            ExecuteEvents.Execute(target.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            check(tooltip.gameObject.activeInHierarchy && text.text.Contains(metric.source)
                && text.text.Contains("Current demand") && text.text.Contains("Net change"),
                metric.source + " is shown alongside current demand and net change after measurement");
            CheckLayout(tooltip, text, check);
            ShipSceneChecks.CaptureReview(bootstrap, colony.CoreBuildings[0], metric.capture);
            ExecuteEvents.Execute(target.gameObject, pointer, ExecuteEvents.pointerExitHandler);
        }
        check(bootstrap.CaptureSaveJson() == before, "reading the measured reserve outlook cannot change saved colony state");
    }

    private static void CheckLayout(RectTransform tooltip, Text text, Action<bool, string> check)
    {
        Canvas.ForceUpdateCanvases();
        var hud = (RectTransform)tooltip.parent;
        var corners = new Vector3[4]; tooltip.GetWorldCorners(corners);
        check(corners.All(c => hud.rect.Contains((Vector2)hud.InverseTransformPoint(c)))
            && text.preferredHeight <= text.rectTransform.rect.height + .1f,
            "the complete hover explanation fits inside the current HUD canvas without clipped text");
    }

    // Batch mode has no automatically rendered Game view. Until a real UI render,
    // UGUI graphics have depth -1 and GraphicRaycaster correctly skips all of them.
    internal sealed class BatchUiRender : IDisposable
    {
        public Camera Camera { get; }
        private readonly Canvas canvas;
        private readonly RenderMode mode;
        private readonly Camera priorCamera;
        private readonly float plane;
        private readonly Vector3 position,scale;
        private readonly Quaternion rotation;
        private readonly Vector2 size;
        private readonly RenderTexture texture;
        public BatchUiRender(Canvas target)
        {
            if(!Application.isBatchMode)return;
            canvas=target;mode=canvas.renderMode;priorCamera=canvas.worldCamera;plane=canvas.planeDistance;
            var rect=(RectTransform)canvas.transform;position=rect.localPosition;rotation=rect.localRotation;scale=rect.localScale;size=rect.sizeDelta;
            Camera=new GameObject("Batch UI raycast camera").AddComponent<Camera>();Camera.enabled=false;
            Camera.nearClipPlane=.1f;Camera.farClipPlane=10;
            texture=new RenderTexture(Screen.width,Screen.height,24);texture.Create();Camera.targetTexture=texture;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=Camera;canvas.planeDistance=2;
            Canvas.ForceUpdateCanvases();Camera.Render();
        }
        public void Dispose()
        {
            if(canvas==null)return;
            canvas.renderMode=mode;canvas.worldCamera=priorCamera;canvas.planeDistance=plane;
            var rect=(RectTransform)canvas.transform;rect.sizeDelta=size;rect.localPosition=position;rect.localRotation=rotation;rect.localScale=scale;
            Camera.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(Camera.gameObject);
            Canvas.ForceUpdateCanvases();
        }
    }

    public static void RunBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
        SessionState.SetBool("WH30K.OutputTooltipBatch", true);
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void ResumeBatch()
    {
        EditorApplication.playModeStateChanged += state => {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("WH30K.OutputTooltipBatch", false)) return;
            SessionState.SetBool("WH30K.OutputTooltipBatch", false);
            EditorApplication.delayCall += () => {
                try { Run(); GameplayChecks.Run(); SceneCheckExit.Exit(0); }
                catch (Exception error) { Debug.LogException(error); SceneCheckExit.Exit(1); }
            };
        };
    }
}
#endif
