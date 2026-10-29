using UnityEditor;

// Leave Play Mode and finish deferred scene/render cleanup before terminating a batch.
public static class SceneCheckExit
{
    private const string Pending = "WH30K.SceneChecks.ExitPending";
    private const string Code = "WH30K.SceneChecks.ExitCode";

    public static void Exit(int code)
    {
        SessionState.SetBool(Pending, true); SessionState.SetInt(Code, code);
        EditorApplication.playModeStateChanged -= OnState;
        EditorApplication.playModeStateChanged += OnState;
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.isPlaying = false;
        else EditorApplication.delayCall += Finish;
    }
    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        EditorApplication.playModeStateChanged += OnState;
        if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.delayCall += Finish;
    }
    private static void OnState(PlayModeStateChange state)
    { if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Finish; }
    private static void Finish()
    {
        if (!SessionState.GetBool(Pending, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var code = SessionState.GetInt(Code, 1); SessionState.EraseBool(Pending); SessionState.EraseInt(Code);
        EditorApplication.playModeStateChanged -= OnState;
        EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}
