#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Sim;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;
using WH30K.UI;

public static class FacilitySceneChecks
{
    [MenuItem("WH30K/Run Facility Checks (Play Mode)")]
    public static void Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode in WH30K_VS01 first.");
        var bootstrap = UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        var original = GameSettings.HasActiveGame && !bootstrap.ChoosingLanding ? bootstrap.CaptureSaveJson() : null;
        var terrain = bootstrap.NewWorldTerrain; var count = 0;
        try
        {
            RunGameplay(bootstrap, (ok, label) => {
                if (!ok) throw new InvalidOperationException("Facility FAIL: " + label);
                count++; Debug.Log("Facility PASS: " + label);
            });
            Debug.Log($"Facility: {count} actual scene checks passed in Unity {Application.unityVersion}.");
        }
        finally
        {
            bootstrap.ConfigureNewWorldTerrain(terrain);
            if (original != null && !bootstrap.TryLoadSaveJson(original)) Debug.LogError("Could not restore original in-memory colony after facility checks.");
        }
    }
    public static void RunGameplay(PlanetBootstrap bootstrap, Action<bool, string> check)
    {
        bootstrap.ConfigureNewWorldTerrain(new WH30K.Planet.TerrainSettings());
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard);
        var placement = bootstrap.GetComponent<BuildingPlacementController>();
        var industry = bootstrap.GetComponent<IndustrySystem>(); var menu = bootstrap.GetComponent<NewGameMenu>();
        var clock = bootstrap.GetComponent<ColonyClock>(); var normal = bootstrap.GetComponent<Settlement>().CreateSnapshot().normal;
        foreach (var id in new[] { "Manufactorum", "StorageDepot" })
            check(PlaceNear(bootstrap, id, normal), "real placement creates " + id + " on the starter grid");
        var factory = placement.Buildings.First(b => b.Id == "Manufactorum");
        var depot = placement.Buildings.First(b => b.Id == "StorageDepot");
        GameplayChecks.FundConstruction(bootstrap);
        var factoryIndex = Array.IndexOf(placement.Buildings, factory); var depotIndex = Array.IndexOf(placement.Buildings, depot);
        T UI<T>(string name) where T : Component => menu.GetComponentsInChildren<T>(true).First(t => t.name == name);
        bootstrap.GetComponent<BuildingSelection>().Select(factory);
        check(menu.FacilityPanelOpen && clock.DecisionPending, "selecting a manufactorum opens its modal and pauses simulation");
        check(!industry.SetProduct(factory,1,2), "advanced recipes are locked before material-funded technology upgrades");
        UI<Button>("TierManufactorum").onClick.Invoke(); UI<Button>("TierManufactorum").onClick.Invoke();
        check(factory.State.level==2 && ManufactorumRules.Scale(factory.State.manufactorum)==1, "real technology buttons spend materials and unlock Tier III without adding capacity");
        // This synchronous scene harness opens and clicks in one frame. Run the real UGUI Start hook
        // that an interactive dropdown receives before its first user click (it initializes its tween runner).
        foreach (var choice in menu.GetComponentsInChildren<Dropdown>(true)) choice.SendMessage("Start", SendMessageOptions.DontRequireReceiver);
        var dropdown = UI<Dropdown>("Product1"); dropdown.Show(); Canvas.ForceUpdateCanvases();
        check(dropdown.transform.Find("Dropdown List") != null, "the actual runtime dropdown template opens a selectable product list");
        dropdown.Hide();
        for (var slot = 1; slot < 4; slot++) UI<Dropdown>("Product" + slot).value = slot + 1;
        UI<Slider>("BufferSlider").value = 5;
        var config = industry.FacilitySettings(factory);
        check(FacilityRules.ActiveCount(config) == 4 && config.bufferLimit == 5,
            "real dropdowns and buffer slider update the simulation's four product selections");
        UI<Dropdown>("Product1").value = 1;
        check(UI<Dropdown>("Product1").value == 2 && industry.FacilitySettings(factory).products[1] == 1,
            "duplicate product selection restores the previous valid dropdown value");
        Canvas.ForceUpdateCanvases();
        var card = UI<RectTransform>("FacilityCard"); var text = UI<Text>("FacilityStatistics");
        var corners = new Vector3[4]; card.GetWorldCorners(corners);
        var canvas = menu.GetComponentInChildren<Canvas>(); var canvasRect = (RectTransform)canvas.transform;
        check(corners.All(c => canvasRect.rect.Contains((Vector2)canvasRect.InverseTransformPoint(c)))
            && text.preferredHeight <= text.rectTransform.rect.height + .1f, "facility popup and statistics fit the current target canvas");
        UI<Button>("CloseFacility").onClick.Invoke();
        check(!menu.FacilityPanelOpen && !clock.DecisionPending && !clock.Snapshot.paused,
            "closing the popup releases its temporary pause and preserves the player's previous clock state");
        bootstrap.GetComponent<BuildingSelection>().Select(depot);
        UI<Dropdown>("DepotMode").value = 1; UI<Dropdown>("DepotItem").value = (int)MaterialKind.Components;
        UI<Slider>("DepotLimitSlider").value = 10;
        check(industry.FacilitySettings(depot).designated && industry.FacilitySettings(depot).storageLimits[6] == 10,
            "depot mode, material dropdown and reservation slider configure the actual depot");
        var savedJson = bootstrap.CaptureSaveJson();
        System.IO.File.WriteAllText(".utmp/manufactorum-facility-save.json",savedJson);
        check(bootstrap.TryLoadSaveJson(savedJson) && !menu.FacilityPanelOpen && !clock.DecisionPending,
            "saving and loading while a popup is open closes stale UI and restores the real colony");
        factory = placement.Buildings[factoryIndex]; depot = placement.Buildings[depotIndex];
        check(industry.FacilitySettings(factory).products.SequenceEqual(config.products)
            && industry.FacilitySettings(factory).bufferLimit == 5 && industry.FacilitySettings(depot).storageLimits[6] == 10,
            "JsonUtility persistence retains product choices, buffer and designated depot settings");
        var saved = industry.Capture();
        saved.facilities[factoryIndex].quantities = new float[] { 100, 0, 100, 100, 0, 0, 0, 100, 0, 0, 0, 0 };
        industry.Configure(bootstrap.ActivePlanet, normal, saved); industry.Simulate(100);
        check(industry.Capture().producedTotals[6] > 0 && industry.Capture().producedTotals[5] > 0
            && industry.Capture().producedTotals[8] > 0 && industry.Capture().producedTotals[4] > 0,
            "actual industry simulation manufactures all four selected outputs on the ship grid");
        saved = industry.Capture(); saved.cargo = Array.Empty<CargoStock>();
        saved.expeditionCargo = null; // Isolate depot reservations from the separate ship surplus route.
        saved.facilities[factoryIndex].quantities = new float[IndustryRules.MaterialCount];
        saved.facilities[factoryIndex].quantities[6] = saved.facilities[factoryIndex].quantities[5] = 5;
        for (var slot = 0; slot < 4; slot++) FacilityRules.SelectProduct(saved.facilities[factoryIndex].configuration, slot, -1);
        saved.facilities[depotIndex].quantities = new float[IndustryRules.MaterialCount];
        industry.Configure(bootstrap.ActivePlanet, normal, saved);
        Invoke(industry, "Dispatch"); var flights = industry.Capture().cargo;
        check(flights.Length == 1 && flights[0].kind == 6 && flights[0].target == depotIndex,
            "designated depot routing admits components and excludes unallocated refined metal");
        industry.SetDepotLimit(depot, MaterialKind.Components, 1);
        Invoke(industry, "AdvanceCargo", 100f); Invoke(industry, "Dispatch");
        check(industry.Capture().facilities[depotIndex].quantities[6] == 3 && industry.CargoCount == 0,
            "cargo already dispatched arrives exactly once after lowering a cap and further receipts stop");
        saved = industry.Capture(); saved.facilities[depotIndex].quantities = new float[IndustryRules.MaterialCount];
        saved.facilities[depotIndex].quantities[0] = 1198; saved.facilities[depotIndex].configuration.designated = false;
        saved.facilities[factoryIndex].quantities[6] = saved.facilities[factoryIndex].quantities[5] = 5;
        industry.Configure(bootstrap.ActivePlanet, normal, saved); Invoke(industry, "Dispatch");
        check(industry.Capture().cargo.Sum(c => c.target == depotIndex ? c.amount : 0) == 2,
            "general depot dispatch reserves its last two units across competing material kinds");
        saved = industry.Capture(); saved.facilityVersion = 0;
        saved.expeditionCargo = new float[IndustryRules.MaterialCount]; // Restore a valid empty ship store for save acceptance.
        foreach (var stock in saved.facilities) stock.configuration = null;
        saved.facilities[factoryIndex].work = 3;
        industry.Configure(bootstrap.ActivePlanet, normal, saved);
        config = industry.FacilitySettings(factory);
        check(config.products[0] == 0 && FacilityRules.ActiveCount(config) == 1 && config.productWork[0] == 3
            && !industry.FacilitySettings(depot).designated, "legacy inventories migrate to default components, exact partial work and general depots");
        var legacyJson = bootstrap.CaptureSaveJson();
        legacyJson = System.Text.RegularExpressions.Regex.Replace(legacyJson, "\"facilityVersion\"\\s*:\\s*1", "\"facilityVersion\":0");
        legacyJson = System.Text.RegularExpressions.Regex.Replace(legacyJson, "\"configuration\"\\s*:\\s*\\{[^}]*\\}", "\"configuration\":null");
        check(bootstrap.TryLoadSaveJson(legacyJson), "legacy configuration version loads through the actual save validator");
        factory = placement.Buildings[factoryIndex]; menu.OpenFacilityPanel(factory);
        UI<Dropdown>("Product0").value = 5; UI<Dropdown>("Product1").value = 6;
        UI<Dropdown>("Product2").value = 0; UI<Dropdown>("Product3").value = 0;
        check(industry.FacilitySettings(factory).products[0] == 4 && industry.FacilitySettings(factory).products[1] == 5,
            "the real product dropdowns expose both appended fertilizer recipes");
        UI<Button>("CloseFacility").onClick.Invoke();
        saved = industry.Capture(); saved.cargo = Array.Empty<CargoStock>();
        saved.facilities[factoryIndex].quantities = new float[IndustryRules.MaterialCount];
        foreach (var ingredient in new[] { 2, 3, 4, 7, 9 }) saved.facilities[factoryIndex].quantities[ingredient] = 30;
        industry.Configure(bootstrap.ActivePlanet, normal, saved); industry.Simulate(100);
        check(industry.Capture().producedTotals[10] > 0 && industry.Capture().producedTotals[11] > 0,
            "actual networked industry produces intermediate and advanced fertilizer from existing streams");
    }
    private static void Invoke(IndustrySystem industry, string method, params object[] arguments)
        => typeof(IndustrySystem).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(industry, arguments);
    private static bool PlaceNear(PlanetBootstrap bootstrap, string id, Vector3 normal)
    {
        var placement = bootstrap.GetComponent<BuildingPlacementController>(); if (!placement.SelectStructure(id)) return false;
        var tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) > .95f ? Vector3.right : Vector3.up).normalized;
        var second = Vector3.Cross(normal, tangent);
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var angle = attempt * 2.39996323f; var distance = 140 + attempt * 4;
            if (placement.TryPlaceDirection((normal + (tangent * Mathf.Cos(angle) + second * Mathf.Sin(angle)) * distance / bootstrap.ActivePlanet.Radius).normalized)) return true;
        }
        return false;
    }
    public static void RunBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
        SessionState.SetBool("WH30K.FacilityBatch", true); EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod]
    private static void ResumeBatch()
    {
        EditorApplication.playModeStateChanged += state => {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("WH30K.FacilityBatch", false)) return;
            SessionState.SetBool("WH30K.FacilityBatch", false);
            EditorApplication.delayCall += () => {
                try { Run(); GameplayChecks.Run(); SceneCheckExit.Exit(0); }
                catch (Exception error) { Debug.LogException(error); SceneCheckExit.Exit(1); }
            };
        };
    }
}
#endif
