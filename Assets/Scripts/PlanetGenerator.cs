using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WH40kCivFactoryBuilderGame
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LODGroup))]
    public class PlanetGenerator : MonoBehaviour
    {
        [Header("Scale")]
        [SerializeField]
        private float planetRadius = 6371f;

        [SerializeField]
        private int seed = 1337;

        [Header("Elevation")]
        [SerializeField]
        private float oceanDepth = 6f;

        [SerializeField]
        private float continentHeight = 2.4f;

        [SerializeField]
        private float mountainHeight = 7.5f;

        [SerializeField, Range(0.3f, 0.7f)]
        private float oceanLevel = 0.47f;

        [SerializeField, Range(0.05f, 1f)]
        private float detailStrength = 0.45f;

        [Header("Noise")]
        [SerializeField]
        private float continentFrequency = 0.75f;

        [SerializeField]
        private float detailFrequency = 3.5f;

        [SerializeField]
        private float mountainFrequency = 7f;

        [SerializeField, Min(1)]
        private int continentOctaves = 4;

        [SerializeField, Min(1)]
        private int detailOctaves = 6;

        [SerializeField, Min(1)]
        private int mountainOctaves = 4;

        [SerializeField, Range(1.2f, 3f)]
        private float lacunarity = 2f;

        [SerializeField, Range(0.1f, 1f)]
        private float persistence = 0.5f;

        [Header("LOD")]
        [SerializeField]
        private int[] lodResolutions = new[] { 96, 192, 384 };

        [SerializeField]
        private bool recalculateEveryEdit = true;

        [Header("Surface Texture")]
        [SerializeField]
        private int textureWidth = 4096;

        [SerializeField]
        private int textureHeight = 2048;

        [SerializeField]
        private Color deepWaterColor = new Color(0.015f, 0.05f, 0.2f);

        [SerializeField]
        private Color shallowWaterColor = new Color(0.078f, 0.35f, 0.6f);

        [SerializeField]
        private Color beachColor = new Color(0.92f, 0.85f, 0.62f);

        [SerializeField]
        private Color plainsColor = new Color(0.22f, 0.6f, 0.25f);

        [SerializeField]
        private Color forestColor = new Color(0.12f, 0.38f, 0.18f);

        [SerializeField]
        private Color mountainColor = new Color(0.45f, 0.4f, 0.37f);

        [SerializeField]
        private Color snowColor = Color.white;

        private readonly Vector3[] faceDirections =
        {
            Vector3.up,
            Vector3.down,
            Vector3.left,
            Vector3.right,
            Vector3.forward,
            Vector3.back
        };

        private readonly List<Mesh> generatedMeshes = new List<Mesh>();
        private Material planetMaterial;
        private Texture2D planetTexture;
        private Vector3 noiseOffset;

        private struct PlanetSample
        {
            public float height;
            public bool isLand;
            public float normalizedHeight;
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                QueuePlanetGeneration(force: true);
                return;
            }
#endif

            GeneratePlanet();
        }

        private void OnValidate()
        {
            if (!recalculateEveryEdit)
            {
                return;
            }

#if UNITY_EDITOR
            QueuePlanetGeneration();
#else
            GeneratePlanet();
#endif
        }

        public void GeneratePlanet()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (!Application.isPlaying && !recalculateEveryEdit)
            {
                return;
            }

            if (lodResolutions == null || lodResolutions.Length == 0)
            {
                lodResolutions = new[] { 96, 192, 384 };
            }

            UpdateNoiseOffset();
            ClearGeneratedChildren();
            DisposeMeshes();
            DisposeTexture();

            var lodGroup = GetComponent<LODGroup>();
            if (lodGroup == null)
            {
                lodGroup = gameObject.AddComponent<LODGroup>();
            }

            lodGroup.fadeMode = LODFadeMode.CrossFade;
            lodGroup.animateCrossFading = true;

            EnsureMaterial();
            planetTexture = CreateSurfaceTexture(textureWidth, Mathf.Max(2, textureHeight));
            planetMaterial.mainTexture = planetTexture;

            var lods = new List<LOD>();
            for (int i = 0; i < lodResolutions.Length; i++)
            {
                int resolution = Mathf.Max(8, lodResolutions[i]);
                var mesh = BuildPlanetMesh(resolution);
                generatedMeshes.Add(mesh);

                var lodObject = new GameObject($"Planet_LOD_{resolution}");
                lodObject.transform.SetParent(transform, false);
                var meshFilter = lodObject.AddComponent<MeshFilter>();
                var meshRenderer = lodObject.AddComponent<MeshRenderer>();
                meshFilter.sharedMesh = mesh;
                meshRenderer.sharedMaterial = planetMaterial;
                meshRenderer.shadowCastingMode = ShadowCastingMode.On;
                meshRenderer.receiveShadows = true;

                float transitionHeight = lodResolutions.Length == 1
                    ? 0.01f
                    : Mathf.Lerp(0.6f, 0.05f, i / (float)(lodResolutions.Length - 1));

                lods.Add(new LOD(transitionHeight, new[] { meshRenderer }));
            }

            lodGroup.SetLODs(lods.ToArray());
            lodGroup.RecalculateBounds();
        }

#if UNITY_EDITOR
        private bool queuedGeneration;
        private bool queuedGenerationForce;

        private void OnDisable()
        {
            CancelQueuedGeneration();
        }

        private void QueuePlanetGeneration(bool force = false)
        {
            if (queuedGeneration)
            {
                queuedGenerationForce |= force;
                return;
            }

            queuedGeneration = true;
            queuedGenerationForce = force;
            EditorApplication.delayCall += HandleDelayedGeneration;
        }

        private void HandleDelayedGeneration()
        {
            EditorApplication.delayCall -= HandleDelayedGeneration;

            bool force = queuedGenerationForce;
            queuedGeneration = false;
            queuedGenerationForce = false;

            if (this == null)
            {
                return;
            }

            if (!force && !recalculateEveryEdit)
            {
                return;
            }

            GeneratePlanet();
        }

        private void CancelQueuedGeneration()
        {
            if (!queuedGeneration)
            {
                return;
            }

            EditorApplication.delayCall -= HandleDelayedGeneration;
            queuedGeneration = false;
            queuedGenerationForce = false;
        }
#endif

        private void EnsureMaterial()
        {
            if (planetMaterial != null)
            {
                return;
            }

            var shader = Shader.Find("Standard");
            planetMaterial = new Material(shader)
            {
                name = "Procedural Planet Material",
                hideFlags = HideFlags.DontSave
            };
            planetMaterial.SetFloat("_Glossiness", 0.2f);
            planetMaterial.SetFloat("_Metallic", 0.1f);
        }

        private void UpdateNoiseOffset()
        {
            const float goldenRatio = 1.61803398875f;
            float offsetSeed = Mathf.Abs(seed) + 0.1234f;
            noiseOffset = new Vector3(
                offsetSeed * 1.193f,
                offsetSeed * goldenRatio,
                offsetSeed * 2.241f);
        }

        private void ClearGeneratedChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }

        private void DisposeMeshes()
        {
            for (int i = 0; i < generatedMeshes.Count; i++)
            {
                var mesh = generatedMeshes[i];
                if (mesh == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(mesh);
                }
                else
                {
                    DestroyImmediate(mesh);
                }
            }

            generatedMeshes.Clear();
        }

        private void DisposeTexture()
        {
            if (planetTexture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(planetTexture);
            }
            else
            {
                DestroyImmediate(planetTexture);
            }

            planetTexture = null;
        }

        private Mesh BuildPlanetMesh(int resolution)
        {
            var mesh = new Mesh
            {
                name = $"PlanetMesh_{resolution}"
            };

            if (resolution * resolution * faceDirections.Length > 65535)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            var vertices = new List<Vector3>(resolution * resolution * faceDirections.Length);
            var uvs = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>((resolution - 1) * (resolution - 1) * 6 * faceDirections.Length);

            foreach (var localUp in faceDirections)
            {
                GenerateTerrainFace(localUp, resolution, vertices, uvs, triangles);
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        private void GenerateTerrainFace(Vector3 localUp, int resolution, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            Vector3 axisA = new Vector3(localUp.y, localUp.z, localUp.x);
            Vector3 axisB = Vector3.Cross(localUp, axisA);
            int vertexStartIndex = vertices.Count;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 percent = new Vector2(x / (float)(resolution - 1), y / (float)(resolution - 1));
                    Vector3 pointOnCube = localUp
                                          + (percent.x - 0.5f) * 2f * axisA
                                          + (percent.y - 0.5f) * 2f * axisB;

                    Vector3 pointOnSphere = pointOnCube.normalized;
                    PlanetSample sample = SamplePoint(pointOnSphere);
                    float surfaceRadius = planetRadius + sample.height;
                    vertices.Add(pointOnSphere * surfaceRadius);

                    float longitude = Mathf.Atan2(pointOnSphere.x, pointOnSphere.z);
                    float latitude = Mathf.Asin(pointOnSphere.y);
                    Vector2 uv = new Vector2(
                        (longitude / (2f * Mathf.PI)) + 0.5f,
                        (latitude / Mathf.PI) + 0.5f);

                    uvs.Add(uv);
                }
            }

            for (int y = 0; y < resolution - 1; y++)
            {
                for (int x = 0; x < resolution - 1; x++)
                {
                    int current = vertexStartIndex + x + y * resolution;
                    int next = current + resolution;

                    triangles.Add(current);
                    triangles.Add(current + 1);
                    triangles.Add(next);

                    triangles.Add(current + 1);
                    triangles.Add(next + 1);
                    triangles.Add(next);
                }
            }
        }

        private PlanetSample SamplePoint(Vector3 pointOnUnitSphere)
        {
            float continentValue = FractalNoise(pointOnUnitSphere, continentFrequency, continentOctaves, persistence, lacunarity, false);
            float landMask = Mathf.Clamp01((continentValue - oceanLevel) / Mathf.Max(0.0001f, 1f - oceanLevel));
            float waterMask = Mathf.Clamp01((oceanLevel - continentValue) / Mathf.Max(0.0001f, oceanLevel));

            float detailValue = FractalNoise(pointOnUnitSphere, detailFrequency, detailOctaves, persistence, lacunarity, false);
            float mountainValue = FractalNoise(pointOnUnitSphere, mountainFrequency, mountainOctaves, persistence, lacunarity, true);

            bool isLand = landMask > 0f;
            float height;

            if (isLand)
            {
                float baseLand = landMask * continentHeight;
                float detailContribution = (detailValue - 0.5f) * 2f * detailStrength * continentHeight * landMask;
                height = baseLand + detailContribution;
                height = Mathf.Max(0f, height);
                height += mountainValue * mountainHeight * landMask;
            }
            else
            {
                height = -waterMask * oceanDepth;
            }

            float normalizedHeight = Mathf.InverseLerp(-oceanDepth, continentHeight + mountainHeight, height);

            return new PlanetSample
            {
                height = height,
                isLand = isLand,
                normalizedHeight = normalizedHeight
            };
        }

        private float FractalNoise(Vector3 point, float baseFrequency, int octaves, float persistenceFactor, float lacunarityFactor, bool ridged)
        {
            float amplitude = 1f;
            float totalAmplitude = 0f;
            float frequency = Mathf.Max(0.0001f, baseFrequency);
            float value = 0f;

            for (int i = 0; i < octaves; i++)
            {
                Vector3 samplePoint = point * frequency + noiseOffset;
                float noiseValue = Perlin3D(samplePoint);
                if (ridged)
                {
                    noiseValue = 1f - Mathf.Abs(noiseValue * 2f - 1f);
                    noiseValue *= noiseValue;
                }

                value += noiseValue * amplitude;
                totalAmplitude += amplitude;

                amplitude *= persistenceFactor;
                frequency *= lacunarityFactor;
            }

            if (totalAmplitude > 0f)
            {
                value /= totalAmplitude;
            }

            return Mathf.Clamp01(value);
        }

        private float Perlin3D(Vector3 point)
        {
            float ab = Mathf.PerlinNoise(point.x, point.y);
            float bc = Mathf.PerlinNoise(point.y, point.z);
            float ca = Mathf.PerlinNoise(point.z, point.x);
            return (ab + bc + ca) / 3f;
        }

        private Texture2D CreateSurfaceTexture(int width, int height)
        {
            if (width < 2)
            {
                width = 2;
            }

            if (height < 2)
            {
                height = 2;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                name = "ProceduralPlanetTexture",
                hideFlags = HideFlags.DontSave
            };

            var colors = new Color[width * height];

            for (int y = 0; y < height; y++)
            {
                float v = y / (float)(height - 1);
                float latitude = Mathf.Lerp(-Mathf.PI / 2f, Mathf.PI / 2f, v);
                float sinLat = Mathf.Sin(latitude);
                float cosLat = Mathf.Cos(latitude);

                for (int x = 0; x < width; x++)
                {
                    float u = x / (float)(width - 1);
                    float longitude = Mathf.Lerp(-Mathf.PI, Mathf.PI, u);
                    float sinLon = Mathf.Sin(longitude);
                    float cosLon = Mathf.Cos(longitude);

                    Vector3 pointOnSphere = new Vector3(
                        cosLat * sinLon,
                        sinLat,
                        cosLat * cosLon);

                    PlanetSample sample = SamplePoint(pointOnSphere);
                    colors[x + y * width] = EvaluateSurfaceColor(sample);
                }
            }

            texture.SetPixels(colors);
            texture.Apply(true, false);
            return texture;
        }

        private Color EvaluateSurfaceColor(PlanetSample sample)
        {
            if (!sample.isLand)
            {
                float waterT = Mathf.InverseLerp(-oceanDepth, 0f, sample.height);
                waterT = Mathf.SmoothStep(0f, 1f, waterT);
                return Color.Lerp(deepWaterColor, shallowWaterColor, waterT);
            }

            float normalizedLand = Mathf.InverseLerp(0f, continentHeight + mountainHeight, sample.height);

            if (normalizedLand < 0.2f)
            {
                float t = normalizedLand / 0.2f;
                return Color.Lerp(beachColor, plainsColor, t);
            }

            if (normalizedLand < 0.45f)
            {
                float t = (normalizedLand - 0.2f) / 0.25f;
                return Color.Lerp(plainsColor, forestColor, t);
            }

            if (normalizedLand < 0.75f)
            {
                float t = (normalizedLand - 0.45f) / 0.3f;
                return Color.Lerp(forestColor, mountainColor, t);
            }

            float snowT = Mathf.Clamp01((normalizedLand - 0.75f) / 0.25f);
            return Color.Lerp(mountainColor, snowColor, snowT);
        }
    }
}
