using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WH30K.Gameplay.Construction;

public static class ArchitectureAppearanceChecks
{
    private const string BatchKey = "WH30K.ArchitectureAppearanceChecks.Batch";
    [MenuItem("WH30K/Run Building Appearance Checks")]
    public static void Run()
    {
        var template = Resources.Load<Material>("Architecture/BuildingSurface");
        Require(template != null && template.shader != null && template.shader.isSupported
            && !ShaderUtil.ShaderHasError(template.shader), "architecture shader imported without errors");
        Require(template.mainTexture != null && template.mainTexture.width >= 1024
            && template.mainTexture.height == template.mainTexture.width,
            "photographic atlas imported at full square resolution");
        var shared = BuildingVisuals.ArchitectureMaterials;
        Require(shared.Length == 7 && shared[0].mainTexture == template.mainTexture
            && shared[1].mainTexture == template.mainTexture && shared[2].mainTexture == template.mainTexture
            && shared[3].mainTexture == template.mainTexture && shared[6].mainTexture == template.mainTexture,
            "all five opaque material groups share the retained atlas");
        var root = new GameObject("Temporary architecture checks");
        var preview = new Material(Shader.Find("Unlit/Color"));
        try
        {
            foreach (var definition in Resources.LoadAll<BuildableStructure>("Buildings"))
            {
                var go = BuildingVisuals.Create(definition.Id, definition.name, root.transform, definition.Dimensions);
                var actual = go.GetComponent<MeshRenderer>().sharedMaterials;
                var footprint=go.GetComponent<MeshFilter>().sharedMesh.bounds;
                Require(definition.Id=="Farm" || footprint.min.x>=-.501f && footprint.max.x<=.501f
                    && footprint.min.z>=-.501f && footprint.max.z<=.501f,
                    definition.Id+" mesh stays within its declared construction footprint: "+footprint);
                Require(actual.SequenceEqual(definition.Id == "Farm" ? BuildingVisuals.FarmMaterials : shared) && go.GetComponent<MeshFilter>().sharedMesh.subMeshCount == 7,
                    definition.Id + " uses the shared architecture surfaces");
                var ghost = BuildingVisuals.Create(definition.Id, "Preview", root.transform, Vector3.one, preview);
                Require(ghost.GetComponent<MeshFilter>().sharedMesh == go.GetComponent<MeshFilter>().sharedMesh
                    && ghost.GetComponent<MeshRenderer>().sharedMaterials.All(m => m == preview),
                    definition.Id + " preview retains matching geometry and its placement colour");
            }
            foreach (var design in ResidentialDevelopment.Library.designs)
            {
                var palette = ResidentialVisual.DesignMaterials(design);
                Require(palette[0].shader == template.shader && palette[0].mainTexture == template.mainTexture
                    && palette[2].shader == template.shader && palette[3].shader == template.shader,
                    design.id + " retains photographic walls, roof and trim under its palette");
            }
            // Check the actual production bevel helper, including rotation, both triangle windings
            // and bounds. Degenerate corner triangles otherwise produce invalid bump frames.
            var rotation = Quaternion.Euler(13, 37, -9); var center = new Vector3(2, 4, -1);
            var faces = 0;
            ArchitectureGeometry.BevelBox(0, center, new Vector3(2, 3, 4), rotation, .03f, (m, a, b, c, d) =>
            {
                foreach (var p in new[] { a, b, c, d })
                {
                    var local = Quaternion.Inverse(rotation) * (p - center);
                    Require(Mathf.Abs(local.x) <= 1.0001f && Mathf.Abs(local.y) <= 1.5001f
                        && Mathf.Abs(local.z) <= 2.0001f, "bevel stays within the occupied box");
                }
                Require(Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3 - center) > 0
                    && Vector3.Dot(Vector3.Cross(c - a, d - a), (a + c + d) / 3 - center) > 0,
                    "bevel triangles face outward and have nonzero area");
                faces++;
            });
            Require(faces == 26, "bevel has closed faces, edge strips and corner caps");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(preview); }
        Debug.Log("Architecture import/material/geometry checks passed. Visual and player acceptance are separate.");
    }

    private static void Require(bool condition, string label)
    { if (!condition) throw new InvalidOperationException("Architecture FAIL: " + label); }

    [MenuItem("WH30K/Capture Building Material Review")]
    public static void CaptureReview()
    {
        var definitions = Resources.LoadAll<BuildableStructure>("Buildings").OrderBy(d => d.Id).ToArray();
        var directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".utmp", "architecture-review");
        Directory.CreateDirectory(directory);
        foreach (var resolution in new[] { new Vector2Int(1280, 800), new Vector2Int(1920, 1080) })
        {
            var utility = new PreviewRenderUtility();
            var root = new GameObject("Building material contact sheet");
            try
            {
                var columns = 4; var rows = Mathf.CeilToInt(definitions.Length / (float)columns);
                for (var i = 0; i < definitions.Length; i++)
                {
                    var go = BuildingVisuals.Create(definitions[i].Id, definitions[i].name, root.transform,
                        definitions[i].Dimensions);
                    go.transform.localPosition = new Vector3((i % columns - 1.5f) * 34, 0, (i / columns - (rows - 1) * .5f) * 40);
                }
                utility.AddSingleGO(root);
                utility.ambientColor = new Color(.22f, .24f, .27f);
                utility.lights[0].intensity = 1.1f;
                utility.lights[0].transform.rotation = Quaternion.Euler(42, -32, 0);
                utility.lights[1].intensity = .3f;
                utility.lights[1].transform.rotation = Quaternion.Euler(20, 140, 0);
                utility.camera.transform.position = new Vector3(120, 145, -235);
                utility.camera.transform.LookAt(new Vector3(0, 10, 0));
                utility.camera.orthographic = true;
                utility.camera.orthographicSize = 105;
                utility.camera.nearClipPlane = .1f; utility.camera.farClipPlane = 700;
                utility.camera.clearFlags = CameraClearFlags.SolidColor;
                utility.camera.backgroundColor = new Color(.1f, .115f, .135f);
                utility.BeginStaticPreview(new Rect(0, 0, resolution.x, resolution.y));
                utility.Render(true);
                var image = utility.EndStaticPreview();
                File.WriteAllBytes(Path.Combine(directory, resolution.x + "x" + resolution.y + ".png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally { utility.Cleanup(); if (root != null) UnityEngine.Object.DestroyImmediate(root); }
        }
        File.WriteAllLines(Path.Combine(directory, "building-order.txt"), definitions.Select(d => d.Id));
        Debug.Log("Building review renders saved: " + directory + ". Review close-ups and the actual scene separately.");
    }

    // Run with graphics enabled; after material checks, existing gameplay checks exercise the scene.
    public static void RunBatch()
    {
        try
        {
            Run(); CaptureReview();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
            SessionState.SetBool(BatchKey, true);
            EditorApplication.playModeStateChanged += BatchPlayState;
            EditorApplication.isPlaying = true;
        }
        catch (Exception error) { Debug.LogException(error); SceneCheckExit.Exit(1); }
    }
    [InitializeOnLoadMethod]
    private static void ResumeBatch()
    {
        if (!SessionState.GetBool(BatchKey, false)) return;
        EditorApplication.playModeStateChanged += BatchPlayState;
        if (EditorApplication.isPlaying) EditorApplication.delayCall += FinishBatch;
    }
    private static void BatchPlayState(PlayModeStateChange state)
    { if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += FinishBatch; }
    private static void FinishBatch()
    {
        if (!SessionState.GetBool(BatchKey, false)) return;
        SessionState.EraseBool(BatchKey);
        try
        {
            var args=System.Environment.GetCommandLineArgs();var index=Array.IndexOf(args,"-wh30k-check");
            var suite=index>=0 && index+1<args.Length?args[index+1]:"all";
            var bootstrap=UnityEngine.Object.FindObjectOfType<WH30K.Gameplay.PlanetBootstrap>();var count=0;
            void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(suite+" FAIL: "+label);count++;Debug.Log(suite+" PASS: "+label);}
            switch(suite)
            {
                case "terrain": TerrainSceneChecks.RunGameplay(bootstrap); break;
                case "earthworks": EarthworksSceneChecks.RunGameplay(bootstrap,Check); break;
                case "facility": FacilitySceneChecks.RunGameplay(bootstrap,Check); break;
                case "supplies": OutputTooltipSceneChecks.RunGameplay(bootstrap,Check); OutputTooltipSceneChecks.RunSupplies(bootstrap,Check); break;
                case "output": OutputTooltipSceneChecks.RunGameplay(bootstrap,Check); break;
                case "farm": FarmSceneChecks.RunGameplay(bootstrap,Check); break;
                case "estate": FarmEstateSceneChecks.RunGameplay(bootstrap,Check); break;
                case "mining": MiningSceneChecks.Run(); break;
                case "industry": bootstrap.ConfigureNewWorldTerrain(new WH30K.Planet.TerrainSettings{radius=6000});GameplayChecks.RunIndustryChecks(bootstrap);break;
                case "shoreline": TerrainSceneChecks.RunShorelineReview(bootstrap,Check);break;
                case "legacy-mining": bootstrap.ConfigureNewWorldTerrain(new WH30K.Planet.TerrainSettings{radius=6000});GameplayChecks.RunMiningChecks(bootstrap);break;
                case "performance": bootstrap.ConfigureNewWorldTerrain(new WH30K.Planet.TerrainSettings{radius=6000});GameplayChecks.RunPerformanceRegressions(bootstrap);break;
                default: GameplayChecks.Run(); ResidentialSceneChecks.Run(); break;
            }
            Debug.Log($"Scene suite {suite} completed ({count} routed checks).");SceneCheckExit.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); SceneCheckExit.Exit(1); }
    }
}
