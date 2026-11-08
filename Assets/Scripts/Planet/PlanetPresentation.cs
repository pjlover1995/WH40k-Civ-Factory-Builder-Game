using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WH30K.Planet
{
    /// <summary>Non-colliding ocean, weather and atmosphere shells; owns their runtime assets.</summary>
    public sealed class PlanetPresentation : MonoBehaviour
    {
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private Material orbitalSky;
        private Material priorSky;
        private Light sun;
        private LODPlanet planet;
        private Camera viewCamera;
        private float fogStart, fogEnd;
        private bool visibilityLimited;
        private bool atmosphericPerspectiveEnabled = true;

        public float FogStartDistance => fogStart;
        public float FogEndDistance => fogEnd;
        public bool VisibilityLimited => visibilityLimited;
        public void SetAtmosphericPerspectiveEnabled(bool enabled)
        {
            atmosphericPerspectiveEnabled = enabled;
            UpdateAtmosphericPerspective();
        }

        public void Configure(LODPlanet planet)
        {
            this.planet = planet;
            viewCamera = Camera.main;
            priorSky = RenderSettings.skybox;
            orbitalSky = MakeMaterial("OrbitalSky");
            RenderSettings.skybox = orbitalSky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.35f, 0.41f, 0.48f);
            RenderSettings.ambientEquatorColor = new Color(0.25f, 0.27f, 0.29f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.15f, 0.13f);
            RenderSettings.fog = false;
            Shader.SetGlobalVector("_WorldPlanetCenterRadius", new Vector4(transform.position.x,
                transform.position.y, transform.position.z, planet.Radius));
            sun = RenderSettings.sun;
            if (sun == null)
                foreach (var light in FindObjectsOfType<Light>())
                    if (light.type == LightType.Directional) { sun = light; break; }
            if (sun != null)
            {
                sun.color = new Color(1f, 0.94f, 0.83f);
                sun.intensity = 1.25f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.8f;
                sun.shadowBias = 0.03f;
                sun.shadowNormalBias = 0.2f;
                RenderSettings.sun = sun;
            }
            QualitySettings.shadowDistance = 2400f;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.High;
            Shell("Ocean", planet.SeaLevel, MakeMaterial("PlanetOcean"), planet);
            Shell("Cloud layer", planet.Radius + (planet.Radius > 50000 ? 12000f : 135f), MakeMaterial("PlanetClouds"), null);
            Shell("Atmospheric scattering", planet.Radius * 1.022f, MakeMaterial("PlanetAtmosphere"), null);
            gameObject.AddComponent<PlanetEcology>().Configure(planet);
            gameObject.AddComponent<PlanetarySystem>().Configure(planet);
            var camera = Camera.main;
            if (camera != null) camera.clearFlags = CameraClearFlags.Skybox;
            UpdateAtmosphericPerspective();
        }

        private void LateUpdate()
        {
            UpdateAtmosphericPerspective();
        }

        private void UpdateAtmosphericPerspective()
        {
            if (planet == null) return;
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera == null) return;
            if (!atmosphericPerspectiveEnabled)
            {
                visibilityLimited = false;
                fogStart = fogEnd = 0;
                Shader.SetGlobalVector("_PlanetAtmosphericFog", Vector4.zero);
                return;
            }

            // Surface views lose detail into a blue-grey haze before the geometric horizon.
            // As the camera climbs, the fade range widens; above 150 km, orbital planet views
            // retain their natural horizon and far clipping.
            var radialDistance = Vector3.Distance(viewCamera.transform.position, planet.transform.position);
            var altitude = Mathf.Max(0, radialDistance - planet.Radius);
            visibilityLimited = altitude < 150000f;
            if (!visibilityLimited)
            {
                fogStart = fogEnd = 0;
                Shader.SetGlobalVector("_PlanetAtmosphericFog", Vector4.zero);
                return;
            }

            var altitudeBlend = Mathf.SmoothStep(0, 1, altitude / 150000f);
            fogStart = Mathf.Lerp(2500f, 80000f, altitudeBlend);
            fogEnd = Mathf.Lerp(8000f, 180000f, altitudeBlend);
            Shader.SetGlobalVector("_PlanetAtmosphericFog", new Vector4(fogStart, fogEnd, 1, 0));
            Shader.SetGlobalColor("_PlanetAtmosphericFogColor", new Color(.42f, .55f, .64f, 1));

            // Keep the main camera just beyond the atmospheric fade. This also lets normal
            // frustum culling skip far terrain patches instead of shading invisible horizons.
            // SimpleOrbitCamera recalculates its geometric horizon clip each frame; LateUpdate
            // applies this nearer visibility cap after its camera update.
            var visibilityFarClip = fogEnd + 2500f;
            if (viewCamera.farClipPlane > visibilityFarClip)
                viewCamera.farClipPlane = visibilityFarClip;
        }

        public void LightLandingSite(Vector3 normal)
        {
            GetComponent<PlanetarySystem>()?.SetLandingSite(normal);
        }

        private Material MakeMaterial(string resource)
        {
            var shader = Resources.Load<Shader>("WH30K/" + resource);
            if (shader == null) throw new System.InvalidOperationException("Missing planet shader: " + resource);
            var material = new Material(shader) { name = resource + " runtime" };
            if (material.HasProperty("_PlanetCenter")) material.SetVector("_PlanetCenter", transform.position);
            materials.Add(material);
            return material;
        }

        private void Shell(string name, float radius, Material material, LODPlanet ocean)
        {
            const int segments = 192, rings = 96;
            var vertices = new Vector3[(segments + 1) * (rings + 1)];
            var normals = new Vector3[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new int[segments * rings * 6];
            for (var y = 0; y <= rings; y++)
                for (var x = 0; x <= segments; x++)
                {
                    var theta = y * Mathf.PI / rings; var phi = x * Mathf.PI * 2 / segments;
                    var n = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    var i = y * (segments + 1) + x;
                    vertices[i] = n * radius; normals[i] = n;
                    var depth = ocean != null ? Mathf.Clamp01((radius - ocean.EvaluateHeight(n)) / 110f) : 0f;
                    colors[i] = new Color(depth, depth, depth, 1);
                }
            var t = 0;
            for (var y = 0; y < rings; y++)
                for (var x = 0; x < segments; x++)
                {
                    var i = y * (segments + 1) + x;
                    triangles[t++] = i; triangles[t++] = i + segments + 2; triangles[t++] = i + segments + 1;
                    triangles[t++] = i; triangles[t++] = i + 1; triangles[t++] = i + segments + 2;
                }
            var mesh = new Mesh { name = name + " sphere", vertices = vertices, normals = normals, colors = colors, triangles = triangles };
            mesh.RecalculateBounds(); meshes.Add(mesh);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void OnDestroy()
        {
            Shader.SetGlobalVector("_PlanetAtmosphericFog", Vector4.zero);
            if (RenderSettings.skybox == orbitalSky) RenderSettings.skybox = priorSky;
            foreach (var material in materials) if (material != null) Destroy(material);
            foreach (var mesh in meshes) if (mesh != null) Destroy(mesh);
        }
    }
}
