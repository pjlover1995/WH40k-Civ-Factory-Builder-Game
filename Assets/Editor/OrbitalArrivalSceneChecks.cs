using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Sim.Settlements;

public static class OrbitalArrivalSceneChecks
{
    private const string Key = "WH30K.OrbitalArrivalChecks";
    private static IEnumerator routine;
    private static int checks;

    [MenuItem("WH30K/Run Orbital Arrival Checks (Play Mode)")]
    public static void RunBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.playModeStateChanged += State;
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void Resume()
    { if (SessionState.GetBool(Key, false)) EditorApplication.playModeStateChanged += State; }

    private static void State(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += StartChecks;
    }

    private static void StartChecks()
    {
        if (!SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        routine = Run();
        EditorApplication.update += Step;
    }

    private static void Step()
    {
        try { if (routine != null && routine.MoveNext()) return; Finish(0); }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Step;
        (routine as IDisposable)?.Dispose();
        routine = null;
        Debug.Log($"Orbital arrival: {checks} Play Mode checks passed; result={code}.");
        SceneCheckExit.Exit(code);
    }

    private static void Check(bool value, string note)
    {
        if (!value) throw new InvalidOperationException("Orbital arrival FAIL: " + note);
        checks++;
        Debug.Log("Orbital arrival PASS: " + note);
    }

    private static IEnumerator Run()
    {
        var bootstrap = UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard, chooseLanding: true, cinematicArrival: true);
        var mainVessel = GameObject.Find("Damaged main vessel in stable orbit");
        Check(mainVessel != null && mainVessel.GetComponent<OrbitalVesselArrival>() != null,
            "new expedition spawns the separate damaged main vessel");
        Check(bootstrap.ChoosingLanding && !FindObject("LandingSelection").activeSelf
            && !FindObject("DiscoveryPanel").activeSelf,
            "landing selection waits behind the warp-exit sequence and captain report");
        var arrivalDeadline = Time.realtimeSinceStartup + 4.5f;
        while (Time.realtimeSinceStartup < arrivalDeadline) yield return null;
        var radial = bootstrap.ActivePlanet.transform.InverseTransformPoint(mainVessel.transform.position).magnitude;
        var arrival = mainVessel.GetComponent<OrbitalVesselArrival>();
        var altitude = (float)typeof(OrbitalVesselArrival).GetField("orbitalAltitude", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(arrival);
        var arrived = !(bool)typeof(OrbitalVesselArrival).GetField("animating", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(arrival);
        Check(Mathf.Abs(radial - (bootstrap.ActivePlanet.Radius + 12000f)) < 3f,
            $"the damaged main vessel settles into a stable 12 km orbit (measured {radial:0.0} m, target {bootstrap.ActivePlanet.Radius + 12000f:0.0} m, configured altitude {altitude:0.0} m, arrived {arrived})");
        Check(GameObject.Find("DiscoveryPanel").activeSelf
            && GameObject.Find("DiscoveryTitle").GetComponent<Text>().text.Contains("CAPTAIN"),
            "the captain explains the vessel damage and tender deployment");
        Check(!bootstrap.ConfirmLanding(), "the player cannot bypass the captain's landing briefing");
        GameObject.Find("DiscoveryAction").GetComponent<Button>().onClick.Invoke();
        Check(!FindObject("DiscoveryPanel").activeSelf && FindObject("LandingSelection").activeSelf,
            "acknowledging the captain opens the surveyed landing choices");
        GameObject.Find("LandColonyShip").GetComponent<Button>().onClick.Invoke();
        var tender = UnityEngine.Object.FindObjectOfType<LandingVesselDescent>();
        Check(!bootstrap.ChoosingLanding && tender != null && tender.IsDescending,
            "site confirmation deploys the separate landing vessel into its descent animation");
        var descentDeadline = Time.realtimeSinceStartup + 3.7f;
        while (Time.realtimeSinceStartup < descentDeadline) yield return null;
        Check(!tender.IsDescending, "the landing vessel completes its surface descent");
    }

    private static GameObject FindObject(string name)
        => Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(value => value.name == name);
}
