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

        public void Configure(LODPlanet planet)
        {
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
            Shell("Cloud layer", planet.Radius + 135f, MakeMaterial("PlanetClouds"), null);
            Shell("Atmospheric scattering", planet.Radius * 1.022f, MakeMaterial("PlanetAtmosphere"), null);
            var camera = Camera.main;
            if (camera != null) camera.clearFlags = CameraClearFlags.Skybox;
        }

        public void LightLandingSite(Vector3 normal)
        {
            if (sun == null) return;
            var tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
            sun.transform.rotation = Quaternion.LookRotation(-(normal + tangent * 0.9f).normalized,
                Mathf.Abs(normal.y) > 0.95f ? Vector3.forward : Vector3.up);
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
            if (RenderSettings.skybox == orbitalSky) RenderSettings.skybox = priorSky;
            foreach (var material in materials) if (material != null) Destroy(material);
            foreach (var mesh in meshes) if (mesh != null) Destroy(mesh);
        }
    }
}
