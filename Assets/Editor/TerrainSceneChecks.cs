using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WH30K.CameraSystem;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Planet;

public static class TerrainSceneChecks
{
    private static int checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("Terrain FAIL: " + name);
        checks++; Debug.Log("Terrain PASS: " + name);
    }

    [MenuItem("WH30K/Run Terrain Geometry Checks")]
    public static void RunGeometry()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run geometry checks outside Play Mode.");
        checks = 0;
        var material = UnityEngine.Object.Instantiate(Resources.Load<Material>("WH30K/PlanetTerrain"));
        var oceanShader = Resources.Load<Shader>("WH30K/PlanetOcean");
        var oceanMaterial = oceanShader != null ? new Material(oceanShader) : null;
        var root = new GameObject("Terrain geometry checks") { hideFlags = HideFlags.HideAndDontSave };
        var planet = root.AddComponent<LODPlanet>();
        try
        {
            foreach (TerrainMethod method in Enum.GetValues(typeof(TerrainMethod)))
            {
                planet.ApplyConfiguration(6000, 33, 6, 1200, 1.8f, .25f, 65, 10);
                planet.BuildPlanet(1234, material, 3, new TerrainSettings { radius=6000, method = method });
                Check(planet.LiveMeshCount == 6 && planet.GetComponentsInChildren<MeshFilter>().Length == 6,
                    method + " builds only six coarse root meshes");
                ValidateMeshes(planet);
                var oldMeshes = root.GetComponentsInChildren<MeshFilter>();
                var near = planet.EvaluateSurfacePoint(new Vector3(1, .23f, .71f)) + new Vector3(1, .23f, .71f).normalized * 80;
                for (var i = 0; i < 12; i++)
                {
                    planet.RefreshLodAtPosition(near);
                    Check(planet.PatchesCreatedLastUpdate <= 4, "LOD creation stays within one atomic four-child split");
                }
                Check(planet.LiveMeshCount > 6, method + " refines geometry near the surface camera");
                ValidateMeshes(planet);
                planet.RefreshLodAtPosition(Vector3.one * 1000000);
                Check(planet.LiveMeshCount == 6, method + " releases child meshes after returning to orbit");
                var oldMesh = oldMeshes[0].sharedMesh;
                planet.BuildPlanet(1234, material, 3, new TerrainSettings { radius=6000, method = method });
                Check(oldMesh == null && planet.LiveMeshCount == 6, method + " regeneration destroys replaced mesh assets");
            }
            // Import validates the changed shader on the actual graphics backend when available.
            Check(!ShaderUtil.ShaderHasError(material.shader), "terrain shader has no reported import errors");
            Check(oceanShader != null && oceanMaterial != null
                && oceanMaterial.GetFloat("_ShoreClearance") > 1.45f
                && !ShaderUtil.ShaderHasError(oceanShader),
                "ocean shader compiles and keeps its shoreline clearance above the maximum wave displacement");
            Debug.Log($"Terrain geometry: {checks} checks passed in Unity {Application.unityVersion}.");
        }
        finally
        {
            planet.ReleaseGeneratedMeshes(); UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(material);
            if (oceanMaterial != null) UnityEngine.Object.DestroyImmediate(oceanMaterial);
        }
    }

    private static void ValidateMeshes(LODPlanet planet)
    {
        var positions = new Dictionary<Vector3Int, Vector3>();
        var edgeNormals = new Dictionary<Vector3Int, Vector3>();
        var shared = 0; var valid = true;
        foreach (var filter in planet.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = filter.sharedMesh; var vertices = mesh.vertices; var normals = mesh.normals;
            var resolution = Mathf.RoundToInt(Mathf.Sqrt(vertices.Length + 8)) - 2;
            var surfaceCount = resolution * resolution;
            valid &= mesh.vertexCount <= 4481 && mesh.triangles.Length % 3 == 0;
            for (var y = 0; y < resolution; y++) for (var x = 0; x < resolution; x++)
            {
                var i = x + y * resolution; var n = vertices[i].normalized;
                valid &= Vector3.Distance(vertices[i], planet.EvaluateSurfacePoint(n)) < .025f
                    && Mathf.Abs(normals[i].magnitude - 1) < .001f && Vector3.Dot(normals[i], n) > 0;
                if (x != 0 && y != 0 && x != resolution - 1 && y != resolution - 1) continue;
                var key = new Vector3Int(Mathf.RoundToInt(n.x * 100000), Mathf.RoundToInt(n.y * 100000), Mathf.RoundToInt(n.z * 100000));
                if (positions.TryGetValue(key, out var previous))
                {
                    shared++; valid &= Vector3.Distance(previous, vertices[i]) < .025f
                        && Vector3.Angle(edgeNormals[key], normals[i]) < .1f;
                }
                else { positions[key] = vertices[i]; edgeNormals[key] = normals[i]; }
            }
            for (var i = surfaceCount; i < vertices.Length; i++) valid &= vertices[i].magnitude < planet.Radius - 600;
        }
        Check(valid && shared > 100, "actual cube-face/LOD mesh samples, outward normals and sealing skirts agree");
    }

    // Called from the existing save-protecting Play Mode suite. These are full bootstrap worlds,
    // including natural settlement anchors, placement, resources, climate and JsonUtility saves.
    public static void RunGameplay(PlanetBootstrap bootstrap)
    {
        var original = bootstrap.NewWorldTerrain;
        try
        {
            foreach (TerrainMethod method in Enum.GetValues(typeof(TerrainMethod)))
            {
                var settings = new TerrainSettings { radius=6000, method = method, seaLevel = -31, continentalCoverage = .61f,
                    roughness = .77f, temperature = .42f, moisture = .63f, forestCoverage = .67f };
                bootstrap.ConfigureNewWorldTerrain(settings);
                bootstrap.BeginNewGame(1376271481, GameSettings.Difficulty.Standard, legacyCore: true);
                GameplayChecks.FundConstruction(bootstrap);
                var planet = bootstrap.ActivePlanet; var n = new Vector3(1, .23f, .71f).normalized;
                var orbit = Camera.main.GetComponent<SimpleOrbitCamera>();
                orbit.Zoom(2f);
                var cameraDirection = planet.transform.InverseTransformPoint(orbit.transform.position).normalized;
                Check(orbit.IsSurfaceView && Vector3.Distance(orbit.transform.position, planet.transform.position)
                    >= Mathf.Max(planet.SeaLevel, planet.EvaluateSurfacePoint(cameraDirection).magnitude) + 44,
                    method + " zooms from orbit above the natural surface without clipping terrain");
                orbit.FrameTarget();
                var position = planet.EvaluateSurfacePoint(n);
                var placement = bootstrap.GetComponent<BuildingPlacementController>();
                placement.SelectStructure("Outpost");
                Check(placement.TryPlaceDirection(n), method + " accepts natural free-direction construction");
                var building = placement.CreateSnapshot()[0];
                var json = bootstrap.CaptureSaveJson();
                var dto = JsonUtility.FromJson<GameplayChecks.ReviewSave>(json);
                Check(dto.version == 16 && dto.terrainVersion == 3 && dto.terrainSettings.method == method
                    && dto.terrainSettings.roughness == settings.roughness, method + " serializes generation controls with the current schema");
                var loaded=bootstrap.TryLoadSaveJson(json);
                Check(loaded && Vector3.Distance(bootstrap.ActivePlanet.EvaluateSurfacePoint(n), position) < .001f
                    && bootstrap.ActivePlanet.SeaLevel == 5969 && bootstrap.ActivePlanet.GenerationSettings.forestCoverage == .67f,
                    method + " restores exact terrain, sea level and climate settings");
                var restored = placement.CreateSnapshot()[0];
                Check(restored.id == building.id && restored.freePlacement && restored.nx == building.nx && restored.ny == building.ny
                    && restored.nz == building.nz && bootstrap.ActivePlanet.Radius == 6000,
                    method + " retains construction IDs and natural surface directions on load");
                var active = bootstrap.ActivePlanet;
                dto.terrainSettings.method = (TerrainMethod)99;
                Check(!bootstrap.TryLoadSaveJson(JsonUtility.ToJson(dto)) && bootstrap.ActivePlanet == active,
                    method + " rejects invalid settings before replacing the active colony");
                dto.terrainSettings = settings; dto.version = 8; dto.industry.materialVersion = 1; dto.terrainVersion = 2;
                dto.industry.geologyVersion = 3;
                // This is a terrain migration fixture, without recorded geology. Applying
                // recipe-3 deposit IDs to a recipe-2 world invents mismatched historical veins.
                dto.industry.deposits=Array.Empty<WH30K.Sim.DepositStock>();
                Check(bootstrap.TryLoadSaveJson(JsonUtility.ToJson(dto)) && bootstrap.ActivePlanet.Radius == 12000
                    && bootstrap.ActivePlanet.TerrainVersion == 2 && bootstrap.TryLoadSaveJson(bootstrap.CaptureSaveJson())
                    && bootstrap.ActivePlanet.Radius == 12000, "schema-8 terrain keeps its 12 km scale through migration and resave");
            }
        }
        finally { bootstrap.ConfigureNewWorldTerrain(original); }
    }

    public static void RunShorelineReview(PlanetBootstrap bootstrap,Action<bool,string> check)
    {
        if(bootstrap==null||check==null)throw new ArgumentNullException(bootstrap==null?nameof(bootstrap):nameof(check));
        var original=bootstrap.NewWorldTerrain;
        try
        {
            bootstrap.ConfigureNewWorldTerrain(new TerrainSettings{radius=PlanetScale.DefaultRadius});
            bootstrap.BeginNewGame(1234,GameSettings.Difficulty.Standard,legacyCore:true);
            var planet=bootstrap.ActivePlanet;var direction=FindShallowCoast(planet,out var depth);
            check(direction.sqrMagnitude>.9f&&depth>=0&&depth<=2.5f,
                $"the review world has sampled coastline within the 2.5 m ocean mask (depth={depth:0.###} m)");
            var camera=Camera.main;var orbit=camera.GetComponent<SimpleOrbitCamera>();
            orbit.FrameTarget();CaptureShorelineView(planet,camera,"orbit");
            orbit.FocusLocation(direction);CaptureShorelineView(planet,camera,"close-ground");
            check(orbit.IsSurfaceView,"coast focus enters close-ground view");
            orbit.Zoom(-.5f);CaptureShorelineView(planet,camera,"regional");
            check(orbit.IsSurfaceView,"coast zoom-out remains in regional view");
            Debug.Log($"Shoreline review renders saved under .utmp/shoreline-review; sampled water depth={depth:0.###} m.");
        }
        finally { bootstrap.ConfigureNewWorldTerrain(original); }
    }

    private static Vector3 FindShallowCoast(LODPlanet planet,out float depth)
    {
        var best=Vector3.zero;depth=float.PositiveInfinity;
        const int samples=160000;
        for(var i=0;i<samples;i++)
        {
            var y=1-2*(i+.5f)/samples;var angle=i*2.39996323f;var r=Mathf.Sqrt(1-y*y);
            var direction=new Vector3(Mathf.Cos(angle)*r,y,Mathf.Sin(angle)*r);
            var height=planet.SeaLevel-planet.EvaluateSurfacePoint(direction).magnitude;
            if(height<0&&-height<depth){depth=-height;best=direction;}
            if(height<=0&&height>=-2.5f){depth=-height;return direction;}
        }
        return best;
    }

    private static void CaptureShorelineView(LODPlanet planet,Camera camera,string name)
    {
        for(var i=0;i<80;i++)planet.RefreshLodAtPosition(camera.transform.position);
        var directory=Path.Combine(".utmp","shoreline-review");Directory.CreateDirectory(directory);
        var target=camera.targetTexture;var active=RenderTexture.active;var aspect=camera.aspect;
        var texture=new RenderTexture(1280,800,24);texture.Create();var image=new Texture2D(1280,800,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=texture;camera.aspect=1280f/800f;camera.Render();RenderTexture.active=texture;
            image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(directory,name+".png"),image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture=target;camera.aspect=aspect;RenderTexture.active=active;
            texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
