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
        [SerializeField] private float radius = 3000f;
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
        public float SeaLevel => radius - 120f;

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

        public void BuildPlanet(int seed, Material material)
        {
            Seed = seed;
            TerrainMaterial = material;

            computedMaxDepth = CalculateEffectiveMaxDepth();
            UpdateTerrainMaterialParameters();

            continentOffset = GenerateOffset(seed, 17);
            ridgeOffset = GenerateOffset(seed, 83);
            continentScale = Mathf.Lerp(0.65f, 1.35f, PseudoRandom(seed * 1.11f));
            ridgeScale = Mathf.Lerp(2.6f, 3.8f, PseudoRandom(seed * 2.77f));
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

        public void ApplyConfiguration(float targetRadius, int baseRes, int depthLimit, float distance, float falloff,
            float updateInterval, int patchResolutionCap, float desiredTriangleArea)
        {
            radius = Mathf.Max(100f, targetRadius);
            baseResolution = Mathf.Max(8, baseRes);
            maxDepth = Mathf.Max(0, depthLimit);
            maxPatchResolution = Mathf.Max(baseResolution, patchResolutionCap);
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
            var resolution = BaseResolution;
            for (var i = 0; i < depth; i++)
            {
                if (resolution >= MaxPatchResolution)
                {
                    return MaxPatchResolution;
                }

                resolution = Mathf.Min(MaxPatchResolution, resolution * 2);
            }

            return Mathf.Clamp(resolution, 8, MaxPatchResolution);
        }

        internal float EvaluateHeight(Vector3 pointOnUnitSphere)
        {
            var continents = Mathf.PerlinNoise(pointOnUnitSphere.x * continentScale + continentOffset.x,
                pointOnUnitSphere.y * continentScale + continentOffset.y);
            continents = Mathf.Pow(continents, 3f);

            var ridged = Mathf.PerlinNoise(pointOnUnitSphere.z * ridgeScale + ridgeOffset.x,
                pointOnUnitSphere.x * ridgeScale + ridgeOffset.y);
            ridged = 1f - Mathf.Abs(ridged * 2f - 1f);
            ridged = Mathf.Pow(ridged, 3.2f);

            var elevation = continents * 520f + ridged * 380f * ridgeWeight - 210f;
            return radius + elevation;
        }

        internal Vector3 EvaluateSurfacePoint(Vector3 direction)
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
                if (height < SeaLevel)
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
            var resolution = BaseResolution;
            for (var i = 0; i < depth; i++)
            {
                resolution = Mathf.Min(MaxPatchResolution, resolution * 2);
            }

            return Mathf.Clamp(resolution, 8, MaxPatchResolution);
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
