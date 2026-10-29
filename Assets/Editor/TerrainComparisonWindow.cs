using UnityEditor;
using UnityEngine;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Planet;

/// <summary>Explicit regeneration only. Edit-mode previews never touch colony state or disk saves.</summary>
public sealed class TerrainComparisonWindow : EditorWindow
{
    [SerializeField] private int seed = 1234;
    [SerializeField] private TerrainSettings settings = new TerrainSettings();
    [SerializeField] private TerrainOverlay overlay;
    [SerializeField] private string restoreColony;
    private LODPlanet preview;
    private Material previewMaterial, oceanMaterial;
    private Mesh oceanMesh;
    private double nextUpdate;
    private Vector2 scroll;

    [MenuItem("WH30K/Terrain Comparison")]
    public static void Open() => GetWindow<TerrainComparisonWindow>("Terrain Comparison");
    private void OnEnable()
    {
        EditorApplication.update += TickPreview;
        EditorApplication.playModeStateChanged += ChangingMode;
    }
    private void OnDisable()
    {
        EditorApplication.update -= TickPreview;
        EditorApplication.playModeStateChanged -= ChangingMode;
        ClearPreview();
    }
    private void ChangingMode(PlayModeStateChange _) => ClearPreview();

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Planetary Terrain V2 · recipe 3", EditorStyles.boldLabel);
        seed = EditorGUILayout.IntField("Planet seed", seed);
        EditorGUILayout.BeginHorizontal();
        foreach (var comparisonSeed in new[] { 1234, 1376271481, 282164432 })
            if (GUILayout.Button(comparisonSeed.ToString())) seed = comparisonSeed;
        EditorGUILayout.EndHorizontal();
        settings.method = (TerrainMethod)EditorGUILayout.EnumPopup("Generation method", settings.method);
        settings.radius = EditorGUILayout.Slider("Radius (metres)", settings.radius, 1000, PlanetScale.MaximumRadius);
        settings.seaLevel = EditorGUILayout.Slider("Sea offset (metres)", settings.seaLevel, -settings.radius * .08f, settings.radius * .08f);
        settings.continentalCoverage = EditorGUILayout.Slider("Continental coverage", settings.continentalCoverage, .15f, .85f);
        settings.mountainIntensity = EditorGUILayout.Slider("Mountain intensity", settings.mountainIntensity, 0, 2);
        settings.roughness = EditorGUILayout.Slider("Terrain roughness", settings.roughness, 0, 1);
        settings.temperature = EditorGUILayout.Slider("Temperature", settings.temperature, 0, 1);
        settings.moisture = EditorGUILayout.Slider("Moisture", settings.moisture, 0, 1);
        settings.forestCoverage = EditorGUILayout.Slider("Forest coverage", settings.forestCoverage, 0, 1);
        settings.desertCoverage = EditorGUILayout.Slider("Desert coverage", settings.desertCoverage, 0, 1);
        settings.snowTemperature = EditorGUILayout.Slider("Snow threshold", settings.snowTemperature, .05f, .45f);
        var nextOverlay = (TerrainOverlay)EditorGUILayout.EnumPopup("Debug overlay", overlay);
        if (nextOverlay != overlay)
        {
            overlay = nextOverlay;
            if (preview != null) preview.SetDebugOverlay(overlay);
            if (Application.isPlaying) FindObjectOfType<PlanetBootstrap>()?.ActivePlanet?.SetDebugOverlay(overlay);
            SceneView.RepaintAll();
        }
        EditorGUILayout.HelpBox("Controls are staged until Regenerate. Coverage controls bias land and climate; they are not exact area percentages. Biome overlay shows blended climate colors. Textures are optional slots on the PlanetTerrain material; defaults are procedural art.", MessageType.Info);
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button("Regenerate Scene-view Preview")) RegeneratePreview();
            if (GUILayout.Button("Clear Preview")) ClearPreview();
        }
        var bootstrap = Application.isPlaying ? FindObjectOfType<PlanetBootstrap>() : null;
        using (new EditorGUI.DisabledScope(bootstrap == null))
        {
            if (GUILayout.Button("Start Temporary Gameplay World"))
            {
                // Capture once, allowing many comparisons before returning to the review colony.
                if (string.IsNullOrEmpty(restoreColony) && GameSettings.HasActiveGame)
                    restoreColony = bootstrap.CaptureSaveJson();
                bootstrap.ConfigureNewWorldTerrain(settings);
                bootstrap.BeginNewGame(seed, GameSettings.Difficulty.Standard);
                bootstrap.ActivePlanet.SetDebugOverlay(overlay);
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(restoreColony)))
                if (GUILayout.Button("Restore Previous Colony") && bootstrap.TryLoadSaveJson(restoreColony))
                    restoreColony = null;
        }
        if (Application.isPlaying)
            EditorGUILayout.HelpBox("Temporary worlds replace the active session. Restore Previous Colony returns the captured session. These controls never write disk saves; avoid F5 in a comparison world. Restore before leaving Play Mode or closing this window.", MessageType.Info);
        var planet = preview != null ? preview : bootstrap != null ? bootstrap.ActivePlanet : null;
        if (planet != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Built seed {planet.Seed} · radius {planet.Radius:N0} m · recipe {planet.TerrainVersion}");
            EditorGUILayout.LabelField($"Root meshes: {planet.RootGenerationMilliseconds:F1} ms (excludes ocean/ecology)");
            EditorGUILayout.LabelField($"Last LOD update: {planet.LastLodMilliseconds:F1} ms");
            EditorGUILayout.LabelField($"Live terrain meshes: {planet.LiveMeshCount:N0} · triangles: {planet.LiveTriangleCount:N0}");
            EditorGUILayout.LabelField($"New patches in last update: {planet.PatchesCreatedLastUpdate} / 4");
        }
        EditorGUILayout.EndScrollView();
    }

    private void RegeneratePreview()
    {
        ClearPreview();
        var source = Resources.Load<Material>("WH30K/PlanetTerrain");
        if (source == null) { Debug.LogError("Missing PlanetTerrain material."); return; }
        previewMaterial = Instantiate(source);
        var root = new GameObject("Terrain Comparison (temporary)") { hideFlags = HideFlags.HideAndDontSave };
        preview = root.AddComponent<LODPlanet>();
        preview.ApplyConfiguration(settings.radius, 33, 6, settings.radius * .2f, 1.8f, .25f, 65, 10);
        preview.BuildPlanet(seed, previewMaterial, 3, settings);
        preview.SetDebugOverlay(overlay);
        var ocean = new GameObject("Preview ocean", typeof(MeshFilter), typeof(MeshRenderer));
        ocean.name = "Preview ocean"; ocean.transform.SetParent(root.transform, false);
        oceanMesh = MakeOcean(preview.SeaLevel);
        ocean.GetComponent<MeshFilter>().sharedMesh = oceanMesh;
        oceanMaterial = new Material(Shader.Find("Standard"));
        oceanMaterial.color = new Color(.035f, .16f, .24f);
        ocean.GetComponent<Renderer>().sharedMaterial = oceanMaterial;
        var scene = SceneView.lastActiveSceneView ?? GetWindow<SceneView>();
        scene.LookAt(Vector3.zero, Quaternion.Euler(25, 135, 0), settings.radius * 2.6f);
        scene.Repaint();
    }

    private static Mesh MakeOcean(float radius)
    {
        const int columns = 128, rows = 64;
        var vertices = new Vector3[(columns + 1) * (rows + 1)]; var normals = new Vector3[vertices.Length];
        var indices = new int[columns * rows * 6]; var t = 0;
        for (var y = 0; y <= rows; y++) for (var x = 0; x <= columns; x++)
        {
            var theta = y * Mathf.PI / rows; var phi = x * Mathf.PI * 2 / columns;
            var i = x + y * (columns + 1);
            normals[i] = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
            vertices[i] = normals[i] * radius;
            if (y == rows || x == columns) continue;
            indices[t++] = i; indices[t++] = i + 1; indices[t++] = i + columns + 1;
            indices[t++] = i + 1; indices[t++] = i + columns + 2; indices[t++] = i + columns + 1;
        }
        return new Mesh { name = "Terrain preview ocean", vertices = vertices, normals = normals, triangles = indices };
    }

    private void TickPreview()
    {
        if (preview == null || EditorApplication.timeSinceStartup < nextUpdate) return;
        nextUpdate = EditorApplication.timeSinceStartup + .1;
        var scene = SceneView.lastActiveSceneView;
        if (scene == null || scene.camera == null) return;
        preview.RefreshLodAtPosition(scene.camera.transform.position);
        scene.Repaint(); Repaint();
    }

    private void ClearPreview()
    {
        if (preview != null) {preview.ReleaseGeneratedMeshes();DestroyImmediate(preview.gameObject);}
        if (previewMaterial != null) DestroyImmediate(previewMaterial);
        if (oceanMaterial != null) DestroyImmediate(oceanMaterial);
        if (oceanMesh != null) DestroyImmediate(oceanMesh);
        preview = null;
    }
}
