#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Sim.Environment;
using WH30K.Sim.Events;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;

// Runs against the real scene, materials, UI, JsonUtility and placement controller.
// This deliberately resets the current in-memory colony; it never writes the save file.
public static class GameplayChecks
{
    private static int checks;

    [MenuItem("WH30K/Reset Colony and Run Gameplay Checks (Play Mode)")]
    public static void Run()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Open WH30K_VS01, enter Play Mode, then run these checks.");
        var bootstrap = UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        if (bootstrap == null) throw new InvalidOperationException("Open the WH30K_VS01 scene first.");
        checks = 0;
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard);
        var placement = bootstrap.GetComponent<BuildingPlacementController>();
        var resources = bootstrap.GetComponent<ResourceSystem>();
        var events = bootstrap.GetComponent<ColonyEventSystem>();
        var settlement = bootstrap.GetComponent<Settlement>();
        var environment = bootstrap.GetComponent<EnvironmentState>();
        Check(placement.SelectStructure("Outpost"), "outpost asset is available");
        var before = resources.CreateSnapshot();
        Check(PlaceFirstAvailable(placement), "place an outpost on real terrain");
        var built = placement.CreateSnapshot()[0];
        var after = resources.CreateSnapshot();
        Check(Mathf.Abs(before.Stockpile - after.Stockpile - 250f) < 0.01f, "placement pays exactly once");
        Check(after.ProductionPerCycle > before.ProductionPerCycle, "placement changes production immediately");
        Check(!placement.TryPlaceCell(new PlanetGridCell(built.face, built.x, built.y)), "occupied cell cannot be rebuilt");
        Check(resources.Stockpile == after.Stockpile, "failed placement does not charge");
        placement.SelectStructure("Factory");
        Check(!PlaceFirstAvailable(placement) && placement.CreateSnapshot().Length == 1,
            "unaffordable factory cannot be built anywhere");
        Check(resources.Stockpile == after.Stockpile, "unaffordable factory does not charge");

        events.TriggerDebugEventNow();
        var json = bootstrap.CaptureSaveJson();
        Check(bootstrap.TryLoadSaveJson(json), "complete game loads from JsonUtility JSON");
        var loaded = placement.CreateSnapshot();
        Check(loaded.Length == 1 && loaded[0].id == built.id && loaded[0].face == built.face
            && loaded[0].x == built.x && loaded[0].y == built.y, "building identity and occupied cell restored");
        Check(Mathf.Abs(resources.Stockpile - after.Stockpile) < 0.01f
            && Mathf.Abs(resources.CreateSnapshot().ProductionPerCycle - after.ProductionPerCycle) < 0.01f,
            "load neither charges again nor loses economic effects");
        Check(events.CreateSnapshot().awaitingChoice, "pending event remains pending after load");
        Check(bootstrap.TryLoadSaveJson(json) && placement.CreateSnapshot().Length == 1
            && Mathf.Abs(resources.CreateSnapshot().ProductionPerCycle - after.ProductionPerCycle) < 0.01f,
            "repeated loads do not duplicate buildings or production");
        Check(!bootstrap.TryLoadSaveJson("{\"version\":99}") && placement.CreateSnapshot().Length == 1,
            "invalid save leaves the running colony intact");
        var saveButton = GameObject.Find("SaveButton").GetComponent<Button>();
        var loadButton = GameObject.Find("HudLoadButton").GetComponent<Button>();
        Check(saveButton.isActiveAndEnabled && saveButton.interactable && loadButton.isActiveAndEnabled,
            "save and load are accessible during play");
        var choice = GameObject.Find("ChoiceA").GetComponent<Button>();
        var money = resources.Stockpile;
        choice.onClick.Invoke();
        Check(!events.CreateSnapshot().awaitingChoice && Mathf.Abs(resources.Stockpile - money + 60f) < 0.01f,
            "safety decision pays its cost and resumes the event timer");
        choice.onClick.Invoke();
        Check(Mathf.Abs(resources.Stockpile - money + 60f) < 0.01f, "decision cannot charge twice");

        var legacy = new LegacySave
        {
            seed = 1234, difficulty = GameSettings.Difficulty.Standard,
            settlement = settlement.CreateSnapshot(), resources = resources.CreateSnapshot(),
            environment = environment.CreateSnapshot()
        };
        legacy.settlement.industryModifier = 0f;
        Check(bootstrap.TryLoadSaveJson(JsonUtility.ToJson(legacy)) && placement.CreateSnapshot().Length == 0,
            "original prototype save format remains loadable");
        Debug.Log($"WH30K gameplay checks: {checks} passed. Inspect camera, layout and terrain visually as well.");
    }

    private static bool PlaceFirstAvailable(BuildingPlacementController placement)
    {
        for (var face = 0; face < 6; face++)
            for (var x = 0; x < placement.GridResolution; x += 8)
                for (var y = 0; y < placement.GridResolution; y += 8)
                    if (placement.TryPlaceCell(new PlanetGridCell(face, x, y))) return true;
        return false;
    }

    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException("Gameplay check failed: " + message);
        checks++;
        Debug.Log("PASS: " + message);
    }

    [Serializable]
    private class LegacySave
    {
        public int seed;
        public GameSettings.Difficulty difficulty;
        public SettlementSnapshot settlement;
        public ResourceSnapshot resources;
        public EnvironmentSnapshot environment;
    }
}
#endif
