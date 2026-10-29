using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Sim;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;

public static class LandingSceneChecks
{
    private const string BatchKey = "WH30K.LandingChecks.Batch";
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
    [MenuItem("WH30K/Run Landing Checks (Play Mode)")]
    public static void Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Open WH30K_VS01 and enter Play Mode.");
        var bootstrap = UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        var original = GameSettings.HasActiveGame && !bootstrap.ChoosingLanding ? bootstrap.CaptureSaveJson() : null;
        var terrain = bootstrap.NewWorldTerrain;
        var count = 0;
        try
        {
            RunGameplay(bootstrap, (ok, label) => {
                if (!ok) throw new InvalidOperationException("Landing FAIL: " + label);
                count++; Debug.Log("Landing PASS: " + label);
            });
            Debug.Log($"Landing: {count} actual scene checks passed in Unity {Application.unityVersion}.");
        }
        finally
        {
            bootstrap.ConfigureNewWorldTerrain(terrain);
            if (original != null && !bootstrap.TryLoadSaveJson(original)) Debug.LogError("Could not restore the original in-memory colony.");
        }
    }

    public static void RunGameplay(PlanetBootstrap bootstrap, Action<bool, string> check)
    {
        bootstrap.ConfigureNewWorldTerrain(new WH30K.Planet.TerrainSettings());
        var industry = bootstrap.GetComponent<IndustrySystem>();
        var settlement = bootstrap.GetComponent<Settlement>();
        var placement = bootstrap.GetComponent<BuildingPlacementController>();
        var clock = bootstrap.GetComponent<ColonyClock>();
        var definitions = UnityEngine.Resources.LoadAll<BuildableStructure>("Buildings").Where(d => !d.DeveloperOnly).ToArray();
        var costs = definitions.Sum(d => d.ConstructionSupplies * (d.Id == "Mine" ? 3 : 1));
        var advanced = definitions.Sum(d => d.AdvancedSupplies * (d.Id == "Mine" ? 3 : 1));
        foreach (var difficulty in GameSettings.GetAllDefinitions())
        {
            bootstrap.BeginNewGame(1234, difficulty.difficulty);
            check(industry.CanAffordConstruction(costs, advanced) && !industry.HasInfiniteConstructionSupplies,
                difficulty.displayName + " carries the complete finite manifest without the developer source");
        }
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard, chooseLanding: true);
        var planet = bootstrap.ActivePlanet;
        check(bootstrap.ChoosingLanding && clock.Paused && GameObject.Find("LandingSelection") != null,
            "expedition UI opens the paused planetary landing selection");
        check(settlement.CoreBuildings.Count == 1 && settlement.CoreBuildings[0].Id == "ColonyShip"
            && !settlement.CoreBuildings[0].gameObject.activeInHierarchy && placement.Buildings.Length == 0,
            "the colony awaits its ship with no prebuilt settlement facilities");
        var survey = industry.Capture();
        var suggested = bootstrap.LandingDirection;
        check(bootstrap.LandingSites.Count == 10 && Enum.GetValues(typeof(GameSettings.Difficulty)).Cast<GameSettings.Difficulty>()
            .Where(d => d != GameSettings.Difficulty.Grim).All(category => bootstrap.LandingSites.Count(site => site.category == category) == 2),
            "the orbital survey offers two seed-stable candidate sites for each of the five landing categories");
        check(bootstrap.LandingSites.Select(site => site.reserveMultiplier).SequenceEqual(new[] { 1.7f, 1.7f, 1.35f, 1.35f, 1f, 1f, .72f, .72f, .48f, .48f }),
            "landing reserve multipliers step down from Easy through Extreme Hard");
        foreach (var site in bootstrap.LandingSites)
            foreach (var kind in new[] { MaterialKind.IronOre, MaterialKind.CopperOre, MaterialKind.Stone, MaterialKind.Biomass, MaterialKind.Crystal, MaterialKind.Promethium })
                check(industry.Deposits.Any(d => d.Discovered && d.Kind == kind && Mathf.Abs(d.ReserveMultiplier - site.reserveMultiplier) < .001f
                    && planet.SurfaceDistance(site.direction, d.Direction) < 1300),
                    site.category + " site " + (site.variant + 1) + " has nearby " + kind + " starter reserves");
        check(survey.deposits.Any(d => d.discovered) && survey.deposits.Any(d => !d.discovered)
            && survey.deposits.Where(d => d.discovered).All(d => d.remaining > 0f && d.remaining <= 306.01f),
            "small real surface reserves are charted while richer geology stays hidden");
        foreach (var kind in new[] { MaterialKind.IronOre, MaterialKind.CopperOre, MaterialKind.Stone, MaterialKind.Biomass, MaterialKind.Crystal, MaterialKind.Promethium })
            check(survey.deposits.Any(d => d.kind == (int)kind && d.discovered), kind + " is visible before building an outpost");
        var hardSiteIndex = Enumerable.Range(0, bootstrap.LandingSites.Count)
            .First(i => bootstrap.LandingSites[i].category == GameSettings.Difficulty.Hard);
        var hardSite = bootstrap.LandingSites[hardSiteIndex];
        check(bootstrap.SelectLandingSite(hardSiteIndex) && Vector3.Angle(hardSite.direction, bootstrap.LandingDirection) < .001f,
            "a category card selects its scanned site and moves the ship preview");
        check(!bootstrap.SelectLandingSite(industry.Deposits.First(d => d.Discovered && d.Kind == MaterialKind.IronOre).Direction),
            "arbitrary surface clicks cannot move the preview away from the offered landing sites");
        var blocked = false;
        try { bootstrap.CaptureSaveJson(); } catch (InvalidOperationException) { blocked = true; }
        check(blocked, "unfinished landing previews cannot overwrite a colony save");
        var selected = bootstrap.LandingDirection;
        GameObject.Find("LandColonyShip").GetComponent<Button>().onClick.Invoke();
        check(GameObject.Find("ColonyGuide") != null && clock.Paused && GameSettings.CurrentDifficulty == GameSettings.Difficulty.Hard,
            "touchdown applies the selected landing category and offers the optional paused guide");
        GameObject.Find("DeclineColonyGuide").GetComponent<Button>().onClick.Invoke();
        check(!bootstrap.ChoosingLanding && !clock.Paused && settlement.CoreBuildings.Count == 1
            && settlement.CoreBuildings[0].Id == "ColonyShip" && settlement.CoreBuildings[0].gameObject.activeInHierarchy
            && Vector3.Angle(settlement.CreateSnapshot().normal, selected) < .001f,
            "LAND COLONY SHIP deploys the actual ship and starts the colony at the selected site");
        check(industry.Capture().deposits.Select(d => (d.x, d.y, d.z, d.remaining, d.discovered))
            .SequenceEqual(survey.deposits.Select(d => (d.x, d.y, d.z, d.remaining, d.discovered))),
            "touchdown retains every previewed deposit direction and reserve");
        var before = industry.Totals()[6];
        check(PlaceNear(bootstrap, "HabBlock", selected), "starter cargo constructs the first residential block through normal placement");
        check(Mathf.Abs(industry.Totals()[6] - (before - definitions.First(d => d.Id == "HabBlock").ConstructionSupplies)) < .001f,
            "real construction debits the finite ship cargo");
        var save = bootstrap.CaptureSaveJson();
        var landedLoaded = bootstrap.TryLoadSaveJson(save);
        var loadedSave = landedLoaded ? bootstrap.CaptureSaveJson() : "rejected";
        if (loadedSave != save) { System.IO.File.WriteAllText(".utmp/landing-before.json", save); System.IO.File.WriteAllText(".utmp/landing-after.json", loadedSave); }
        check(landedLoaded && loadedSave == save,
            "schema 16 restores the ship, placement, exact survey, cargo and paused-time state");
        check(PlaceNear(bootstrap, "StorageDepot", selected) && PlaceNear(bootstrap, "Manufactorum", selected)
            && PlaceNear(bootstrap, "GroundwaterPump", selected),
            "the manifest establishes storage, basic manufacturing and fresh water near the ship");
        var shipPosition = settlement.CoreBuildings[0].transform.position;
        foreach (var kind in new[] { MaterialKind.IronOre, MaterialKind.Stone, MaterialKind.Biomass })
        {
            var deposit = industry.NearestDeposit(shipPosition, kind);
            check(deposit != null && PlaceNear(bootstrap, kind == MaterialKind.IronOre ? "Mine" : kind == MaterialKind.Stone ? "Quarry" : "Harvester", deposit.Direction),
                kind + " can be extracted from the initial orbital survey without an outpost");
        }
        for (var step = 0; step < 200; step++)
        {
            if (industry.HasMiningDecision) industry.ResolveMiningConflict(false);
            industry.Simulate(5);
        }
        check(industry.Capture().producedTotals[(int)MaterialKind.Components] > 0 && industry.PowerGenerated > 0,
            "finite surface extraction and actual haulers supply the configured Manufactorum recipe using the ship grid");
        check(bootstrap.TryLoadSaveJson(save), "the pre-production fixture restores before the exhaustion check");
        var stock = industry.Capture().expeditionCargo;
        check(industry.TrySpendConstruction(stock[6], stock[8]) && !industry.CanAffordConstruction(1, 0),
            "using the remaining manifest leaves no infinite construction source");
        check(bootstrap.TryLoadSaveJson(bootstrap.CaptureSaveJson()) && !industry.CanAffordConstruction(1, 0),
            "reload does not refill an exhausted manifest");
        var invalid = JsonUtility.FromJson<GameplayChecks.ReviewSave>(bootstrap.CaptureSaveJson());
        invalid.industry.deposits[0].x = invalid.industry.deposits[0].y = invalid.industry.deposits[0].z = 0;
        var active = bootstrap.ActivePlanet;
        check(!bootstrap.TryLoadSaveJson(JsonUtility.ToJson(invalid)) && bootstrap.ActivePlanet == active,
            "invalid deposit geometry is rejected before replacing the active colony");

        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard, legacyCore: true);
        var legacy = JsonUtility.FromJson<GameplayChecks.ReviewSave>(bootstrap.CaptureSaveJson());
        legacy.version = 11; legacy.industry.materialVersion = 1; legacy.industry.geologyVersion = 3;
        check(bootstrap.TryLoadSaveJson(JsonUtility.ToJson(legacy)) && settlement.CoreBuildings.Count == 3
            && industry.Capture().expeditionCargo == null && settlement.CoreBuildings.All(b => b.Id != "ColonyShip"),
            "schema 11 retains its established core and receives no replacement ship or free cargo");
    }
    internal static bool PlaceNear(PlanetBootstrap bootstrap, string id, Vector3 direction)
    {
        var placement = bootstrap.GetComponent<BuildingPlacementController>();
        if (!placement.SelectStructure(id)) return false;
        var right = Vector3.Cross(direction, Mathf.Abs(direction.y) > .95f ? Vector3.right : Vector3.up).normalized;
        var forward = Vector3.Cross(direction, right);
        for (var attempt = 0; attempt < 96; attempt++)
        {
            var angle = attempt * 2.39996323f;
            var extraction = id == "Mine" || id == "Quarry" || id == "Harvester";
            var range = extraction ? 100 + attempt % 32 * 4 : 360 + attempt * 6;
            var n = (direction + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * range / bootstrap.ActivePlanet.Radius).normalized;
            if (placement.TryPlaceDirection(n)) return true;
        }
        return false;
    }
}
