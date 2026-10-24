using System;
using UnityEngine;

namespace WH30K.Planet
{
    /// <summary>
    /// Manages the life-cycle of a cube-sphere LOD planet. Responsible for spawning the patch hierarchy
    /// and feeding shared parameters to each patch.
    /// </summary>
    public class LODPlanet : MonoBehaviour
    {
        [SerializeField] private float radius = 12000f;
        [SerializeField] private int baseResolution = 32;
        [SerializeField] private int maxDepth = 6;
        [SerializeField] private int maxPatchResolution = 1024;
        [SerializeField] private float targetTriangleArea = 10f;
        [SerializeField] private float splitDistance = 2400f;
        [SerializeField] private float splitFalloff = 1.8f;
        [SerializeField] private float lodUpdateInterval = 0.25f;

        public float Radius => radius;
        public int BaseResolution => Mathf.Max(8, baseResolution);
        public int MaxDepth => Mathf.Max(0, computedMaxDepth);
        public int MaxPatchResolution => Mathf.Clamp(maxPatchResolution, BaseResolution, 4096);
        public float SplitDistance => splitDistance;
        public float SplitFalloff => Mathf.Max(1.01f, splitFalloff);
        public float LodUpdateInterval => Mathf.Max(0.05f, lodUpdateInterval);
        public float TargetTriangleArea => Mathf.Max(0.5f, targetTriangleArea);
        public int Seed { get; private set; }
        public Material TerrainMaterial { get; private set; }
        public int TerrainVersion { get; private set; } = 1;
        public float SeaLevel => radius - (TerrainVersion == 0 ? 120f : 20f);

        private readonly Vector3[] faceDirections =
        {
            Vector3.up,
            Vector3.down,
            Vector3.left,
            Vector3.right,
            Vector3.forward,
            Vector3.back
        };

        private LODPatch[] rootPatches;
        private Camera referenceCamera;
        private float elapsed;

        private int computedMaxDepth;

        private Vector3 continentOffset;
        private Vector3 ridgeOffset;
        private float continentScale;
        private float ridgeScale;
        private float ridgeWeight;

        public void BuildPlanet(int seed, Material material, int terrainVersion = 2)
        {
            Seed = seed;
            TerrainVersion = terrainVersion;
            TerrainMaterial = material;

            computedMaxDepth = CalculateEffectiveMaxDepth();
            UpdateTerrainMaterialParameters();

            continentOffset = GenerateOffset(seed, 17);
            ridgeOffset = GenerateOffset(seed, 83);
            continentScale = TerrainVersion == 0
                ? Mathf.Lerp(0.65f, 1.35f, PseudoRandom(seed * 1.11f))
                : Mathf.Lerp(1.7f, 2.3f, PseudoRandom(seed * 1.11f));
            ridgeScale = TerrainVersion == 0
                ? Mathf.Lerp(2.6f, 3.8f, PseudoRandom(seed * 2.77f))
                : Mathf.Lerp(9f, 13f, PseudoRandom(seed * 2.77f));
            ridgeWeight = Mathf.Lerp(0.35f, 0.65f, PseudoRandom(seed * 3.91f));

            ClearExisting();
            rootPatches = new LODPatch[faceDirections.Length];

            for (var i = 0; i < faceDirections.Length; i++)
            {
                var patchGO = new GameObject($"Patch_{i}");
                patchGO.transform.SetParent(transform, false);
                var patch = patchGO.AddComponent<LODPatch>();
                patch.Initialize(this, faceDirections[i], Vector2.zero, Vector2.one, 0);
                rootPatches[i] = patch;
            }
        }

        private const int DefaultPatchResolutionCap = 1024;

        public void ApplyConfiguration(float targetRadius, int baseRes, int depthLimit, float distance, float falloff,
            float updateInterval, int patchResolutionCap, float desiredTriangleArea)
        {
            radius = Mathf.Max(100f, targetRadius);
            baseResolution = Mathf.Max(8, baseRes);
            maxDepth = Mathf.Max(0, depthLimit);
            var effectivePatchCap = patchResolutionCap <= baseResolution
                ? DefaultPatchResolutionCap
                : patchResolutionCap;
            maxPatchResolution = Mathf.Max(baseResolution, effectivePatchCap);
            targetTriangleArea = Mathf.Max(0.5f, desiredTriangleArea);
            splitDistance = Mathf.Max(radius * 0.2f, distance);
            splitFalloff = Mathf.Max(1.01f, falloff);
            lodUpdateInterval = Mathf.Max(0.05f, updateInterval);

            computedMaxDepth = CalculateEffectiveMaxDepth();
            UpdateTerrainMaterialParameters();
        }

        private void ClearExisting()
        {
            if (rootPatches == null)
            {
                return;
            }

            for (var i = 0; i < rootPatches.Length; i++)
            {
                if (rootPatches[i] != null)
                {
                    Destroy(rootPatches[i].gameObject);
                }
            }
        }

        private void Update()
        {
            if (rootPatches == null || rootPatches.Length == 0)
            {
                return;
            }

            if (referenceCamera == null)
            {
                referenceCamera = Camera.main;
                if (referenceCamera == null)
                {
                    return;
                }
            }

            elapsed += Time.deltaTime;
            if (elapsed < LodUpdateInterval)
            {
                return;
            }

            elapsed = 0f;
            var cameraPosition = referenceCamera.transform.position;
            foreach (var patch in rootPatches)
            {
                patch?.RefreshLOD(cameraPosition);
            }
        }

        internal int GetResolutionForDepth(int depth)
        {
            // Subdivision already doubles density. Doubling each child's resolution as well
            // made a close view allocate million-vertex patches on the larger planet.
            return Mathf.Clamp(BaseResolution, 8, MaxPatchResolution);
        }

        internal float EvaluateHeight(Vector3 pointOnUnitSphere)
        {
            if (TerrainVersion == 0)
            {
                // Preserve the exact old seed-to-coastline mapping when expanding a saved world.
                var oldContinent = Mathf.Pow(Mathf.PerlinNoise(pointOnUnitSphere.x * continentScale + continentOffset.x,
                    pointOnUnitSphere.y * continentScale + continentOffset.y), 3f);
                var oldRidge = Mathf.PerlinNoise(pointOnUnitSphere.z * ridgeScale + ridgeOffset.x,
                    pointOnUnitSphere.x * ridgeScale + ridgeOffset.y);
                oldRidge = Mathf.Pow(1f - Mathf.Abs(oldRidge * 2f - 1f), 3.2f);
                return radius + oldContinent * 520f + oldRidge * 380f * ridgeWeight - 210f;
            }
            var continents = Noise3(pointOnUnitSphere * continentScale + continentOffset) * 0.7f
                + Noise3(pointOnUnitSphere * continentScale * 2.1f + continentOffset * 0.37f) * 0.3f;
            var ridged = Noise3(pointOnUnitSphere * ridgeScale + ridgeOffset);
            ridged = 1f - Mathf.Abs(ridged * 2f - 1f);
            var inland = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.51f, 0.65f, continents));
            var mountains = Mathf.Pow(ridged, 5f) * 150f * ridgeWeight * inland;
            var detail = (Noise3(pointOnUnitSphere * 42f + ridgeOffset) - 0.5f) * 8f * inland;
            var elevation = (continents - 0.52f) * 650f - 20f + mountains + detail;
            if (TerrainVersion >= 2)
            {
                var ranges = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.43f, 0.66f,
                    Noise3(pointOnUnitSphere * 7.7f + ridgeOffset * 0.31f)));
                var peaks = Mathf.Pow(ridged, 3.1f) * ranges * inland * 1050f;
                var hills = (Noise3(pointOnUnitSphere * 65f + continentOffset) - 0.45f) * 100f * inland;
                elevation += peaks + hills;
            }
            return radius + elevation;
        }

        public float Moisture(Vector3 direction) => Noise3(direction.normalized * 8.5f + continentOffset * 0.63f);
        public string Biome(Vector3 direction)
        {
            var n = direction.normalized; var height = EvaluateHeight(n) - SeaLevel;
            if (height < 0) return "Ocean";
            if (Mathf.Abs(n.y) > 0.83f || height > 650) return "Alpine / ice";
            if (height > 220) return "Mountain range";
            var wet = Moisture(n);
            if (height < 48 && wet > 0.49f) return "Swamp";
            if (wet > 0.51f) return "Forest";
            if (wet < 0.43f && Mathf.Abs(n.y) < 0.65f) return "Arid hills";
            return height > 80 ? "Rolling hills" : "Grassland";
        }

        public Color BiomeColor(Vector3 direction)
        {
            switch (Biome(direction))
            {
                case "Swamp": return new Color(0.14f, 0.23f, 0.12f);
                case "Forest": return new Color(0.10f, 0.24f, 0.13f);
                case "Arid hills": return new Color(0.54f, 0.39f, 0.23f);
                case "Mountain range": return new Color(0.34f, 0.33f, 0.30f);
                case "Alpine / ice": return new Color(0.80f, 0.86f, 0.87f);
                default: return new Color(0.28f, 0.37f, 0.17f);
            }
        }

        private static float Noise3(Vector3 p)
            => (Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y, p.z) + Mathf.PerlinNoise(p.z, p.x)) / 3f;

        internal Vector3 EvaluateSurfaceNormal(Vector3 direction)
        {
            var n = direction.normalized;
            var a = Vector3.Cross(n, Mathf.Abs(n.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
            var b = Vector3.Cross(n, a);
            const float epsilon = 0.001f;
            var da = EvaluateSurfacePoint(n + a * epsilon) - EvaluateSurfacePoint(n - a * epsilon);
            var db = EvaluateSurfacePoint(n + b * epsilon) - EvaluateSurfacePoint(n - b * epsilon);
            return Vector3.Cross(da, db).normalized;
        }

        public Vector3 EvaluateSurfacePoint(Vector3 direction)
        {
            var normalised = direction.normalized;
            var height = EvaluateHeight(normalised);
            return normalised * height;
        }

        internal bool TryFindLandPoint(int attempts, System.Random random, out Vector3 position, out Vector3 normal)
        {
            for (var i = 0; i < attempts; i++)
            {
                var direction = new Vector3((float)random.NextDouble() * 2f - 1f,
                    (float)random.NextDouble() * 2f - 1f,
                    (float)random.NextDouble() * 2f - 1f);
                if (direction.sqrMagnitude < 0.001f)
                {
                    continue;
                }

                var point = EvaluateSurfacePoint(direction);
                var height = point.magnitude;
                if (height < SeaLevel + 12f)
                {
                    continue;
                }

                position = point;
                normal = point.normalized;
                return true;
            }

            position = Vector3.zero;
            normal = Vector3.up;
            return false;
        }

        private static Vector3 GenerateOffset(int seed, int salt)
        {
            var random = new System.Random(seed ^ salt);
            return new Vector3((float)random.NextDouble() * 1000f, (float)random.NextDouble() * 1000f,
                (float)random.NextDouble() * 1000f);
        }

        private static float PseudoRandom(double seed)
        {
            return (float)(new System.Random((int)(seed * 1000d) ^ 9176).NextDouble());
        }

        public void SetCamera(Camera camera)
        {
            referenceCamera = camera;
        }

        private int CalculateEffectiveMaxDepth()
        {
            var baseDepth = Mathf.Max(0, maxDepth);
            var targetArea = TargetTriangleArea;
            var depth = baseDepth;

            for (var safety = 0; safety < 12; safety++)
            {
                var estimated = EstimateTriangleArea(depth);
                if (estimated <= targetArea)
                {
                    break;
                }

                depth++;
            }

            return depth;
        }

        private float EstimateTriangleArea(int depth)
        {
            var patchArea = (4f * Mathf.PI * radius * radius) / 6f / Mathf.Pow(4f, depth);
            var resolution = EstimateResolutionForDepth(depth);
            var triangles = Mathf.Max(1, (resolution - 1) * (resolution - 1) * 2);
            return patchArea / triangles;
        }

        private int EstimateResolutionForDepth(int depth)
        {
            return Mathf.Clamp(BaseResolution, 8, MaxPatchResolution);
        }

        private void UpdateTerrainMaterialParameters()
        {
            if (TerrainMaterial == null)
            {
                return;
            }

            if (TerrainMaterial.HasProperty("_PlanetRadius"))
            {
                TerrainMaterial.SetFloat("_PlanetRadius", radius);
            }

            if (TerrainMaterial.HasProperty("_PlanetCenter"))
                TerrainMaterial.SetVector("_PlanetCenter", transform.position);

            if (TerrainMaterial.HasProperty("_SeaLevel"))
            {
                TerrainMaterial.SetFloat("_SeaLevel", SeaLevel);
            }

            if (TerrainMaterial.HasProperty("_TargetDetailArea"))
            {
                TerrainMaterial.SetFloat("_TargetDetailArea", TargetTriangleArea);
            }
        }
    }
}
