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
        [SerializeField] private int baseResolution = 48;
        [SerializeField] private int maxDepth = 5;
        [SerializeField] private float splitDistance = 2400f;
        [SerializeField] private float splitFalloff = 1.8f;
        [SerializeField] private float lodUpdateInterval = 0.25f;

        public float Radius => radius;
        public int BaseResolution => Mathf.Max(8, baseResolution);
        public int MaxDepth => Mathf.Max(0, maxDepth);
        public float SplitDistance => splitDistance;
        public float SplitFalloff => Mathf.Max(1.01f, splitFalloff);
        public float LodUpdateInterval => Mathf.Max(0.05f, lodUpdateInterval);
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

        private Vector3 continentOffset;
        private Vector3 ridgeOffset;
        private float continentScale;
        private float ridgeScale;
        private float ridgeWeight;

        public void BuildPlanet(int seed, Material material)
        {
            Seed = seed;
            TerrainMaterial = material;

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
            float updateInterval)
        {
            radius = Mathf.Max(100f, targetRadius);
            baseResolution = Mathf.Max(8, baseRes);
            maxDepth = Mathf.Max(0, depthLimit);
            splitDistance = Mathf.Max(radius * 0.2f, distance);
            splitFalloff = Mathf.Max(1.01f, falloff);
            lodUpdateInterval = Mathf.Max(0.05f, updateInterval);
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
                resolution = Mathf.Max(8, resolution / 2);
            }

            return resolution;
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
    }
}
