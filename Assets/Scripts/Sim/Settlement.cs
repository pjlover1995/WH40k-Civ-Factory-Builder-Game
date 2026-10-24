using System;
using System.Collections.Generic;
using UnityEngine;
using WH30K.Game;
using WH30K.Planet;
using WH30K.Sim.Buildings;
using WH30K.Sim.Environment;
using WH30K.Sim.Resources;
using WH30K.UI;
using WH30K.Gameplay.Construction;

namespace WH30K.Sim.Settlements
{
    [Serializable]
    public struct SettlementSnapshot
    {
        public float population;
        public float workforce;
        public float productionModifier;
        public float upkeepModifier;
        public Vector3 position;
        public Vector3 normal;
        public string[] buildings;
        public float industryModifier;
        public float tickTimer;
        public BuildingOperationState[] operations;
    }

    public struct SettlementReport
    {
        public float population;
        public float workforce;
        public float production;
        public float upkeep;
        public float net;
        public float tickInterval;
    }

    /// <summary>
    /// Handles the initial settlement, building instantiation, and the lightweight resource loop.
    /// </summary>
    public class Settlement : MonoBehaviour
    {
        [SerializeField] private float tickInterval = 1f;
        [SerializeField] private float populationGrowthRate = 0.05f;
        [SerializeField] private float workforceRatio = 0.62f;
        [SerializeField] private float buildingSpacing = 220f;

        private readonly List<SettlementBuilding> buildings = new List<SettlementBuilding>();
        private readonly List<GameObject> buildingVisuals = new List<GameObject>();
        private readonly List<ManagedBuilding> constructedBuildings = new List<ManagedBuilding>();
        private readonly List<ManagedBuilding> coreBuildings = new List<ManagedBuilding>();
        public IReadOnlyList<ManagedBuilding> CoreBuildings => coreBuildings;
        private readonly List<BuildingEconomy> economy = new List<BuildingEconomy>();

        private LODPlanet planet;
        private ResourceSystem resourceSystem;
        private EnvironmentState environmentState;
        private NewGameMenu menu;
        private Transform settlementAnchor;
        private GameObject serviceRoads;
        private Mesh serviceRoadMesh;
        private GameSettings.DifficultyDefinition currentDifficulty;

        private float population;
        private float workforce;
        private float productionModifier = 1f;
        private float upkeepModifier = 1f;
        private float industryModifier = 1f;
        private float tickTimer;
        private Vector3 storedPosition;
        private Vector3 storedNormal;

        public void ConfigureMenu(NewGameMenu newMenu)
        {
            menu = newMenu;
        }

        public bool HasActiveSimulation => planet != null && resourceSystem != null && environmentState != null;

        public void BeginNewGame(GameSettings.DifficultyDefinition definition, LODPlanet planetInstance,
            Vector3 surfacePosition, Vector3 surfaceNormal, ResourceSystem resources, EnvironmentState environment)
        {
            currentDifficulty = definition;
            planet = planetInstance;
            resourceSystem = resources;
            environmentState = environment;
            productionModifier = definition.resourceYieldMultiplier;
            upkeepModifier = Mathf.Lerp(1f, 1.3f, Mathf.InverseLerp(1f, 1.4f, definition.environmentHarshnessMultiplier));
            industryModifier = 1f;
            constructedBuildings.Clear();
            population = Mathf.Max(120f, 160f * definition.resourceYieldMultiplier);
            workforce = population * workforceRatio;
            tickTimer = 0f;
            storedPosition = surfacePosition;
            storedNormal = surfaceNormal;

            ClearVisuals();
            EnsureAnchor(surfacePosition, surfaceNormal);
            RebuildBuildings(new[] { "HabBlock", "Factory", "Utility" });
            RefreshReadout();
            menu?.AppendEventLog("Settlement core deployed on new world.");
        }

        public void LoadFromSnapshot(SettlementSnapshot snapshot, LODPlanet planetInstance,
            GameSettings.DifficultyDefinition definition, ResourceSystem resources, EnvironmentState environment)
        {
            currentDifficulty = definition;
            planet = planetInstance;
            resourceSystem = resources;
            environmentState = environment;
            productionModifier = snapshot.productionModifier;
            upkeepModifier = snapshot.upkeepModifier;
            industryModifier = snapshot.industryModifier > 0f ? snapshot.industryModifier : 1f;
            constructedBuildings.Clear();
            population = snapshot.population;
            workforce = snapshot.workforce;
            tickTimer = Mathf.Clamp(snapshot.tickTimer, 0f, tickInterval);
            storedNormal = snapshot.normal.sqrMagnitude > 0.01f ? snapshot.normal.normalized : snapshot.position.normalized;
            // Saves made before a radius/terrain upgrade must still sit on the current surface.
            storedPosition = planet.transform.TransformPoint(planet.EvaluateSurfacePoint(storedNormal));

            ClearVisuals();
            EnsureAnchor(storedPosition, storedNormal);
            RebuildBuildings(snapshot.buildings != null && snapshot.buildings.Length > 0
                ? snapshot.buildings
                : new[] { "HabBlock", "Factory", "Utility" });
            if (snapshot.operations != null)
                for (var i = 0; i < coreBuildings.Count && i < snapshot.operations.Length; i++)
                    coreBuildings[i].Restore(snapshot.operations[i]);
        }

        private void Update()
        {
            if (planet == null || resourceSystem == null || environmentState == null)
            {
                return;
            }

            tickTimer += ColonyClock.DeltaTime;
            var steps = 0;
            while (tickTimer >= tickInterval && steps++ < 8)
            {
                tickTimer -= tickInterval;
                RunTick();
            }
        }

        public SettlementSnapshot CreateSnapshot()
        {
            var ids = new string[buildings.Count];
            var operations = new BuildingOperationState[coreBuildings.Count];
            for (var i = 0; i < operations.Length; i++) operations[i] = coreBuildings[i].State;
            for (var i = 0; i < buildings.Count; i++)
            {
                ids[i] = buildings[i].Id;
            }

            return new SettlementSnapshot
            {
                population = population,
                workforce = workforce,
                productionModifier = productionModifier,
                upkeepModifier = upkeepModifier,
                position = storedPosition,
                normal = storedNormal,
                buildings = ids,
                industryModifier = industryModifier,
                tickTimer = tickTimer,
                operations = operations
            };
        }

        public void AdjustPolicy(float productionDelta, float upkeepDelta, string message, float industryDelta = 0f)
        {
            productionModifier = Mathf.Clamp(productionModifier + productionDelta, 0.25f, 3f);
            upkeepModifier = Mathf.Clamp(upkeepModifier + upkeepDelta, 0.1f, 3f);
            industryModifier = Mathf.Clamp(industryModifier + industryDelta, 0.25f, 3f);
            RefreshReadout();
            if (!string.IsNullOrEmpty(message))
            {
                menu?.AppendEventLog(message);
            }
        }

#if UNITY_EDITOR
        public void DebugRunImmediateTick()
        {
            if (!HasActiveSimulation)
            {
                return;
            }

            RunTick();
        }
#endif

        private void RunTick()
        {
            var tick = CalculateEconomy(tickInterval);
            population = tick.population;
            workforce = tick.workforce;
            var totalImpact = EnvironmentImpact.Zero;
            foreach (var building in coreBuildings)
            {
                totalImpact += EnvironmentRules.ApplyPolicy(building.Impact.Scale(tick.utilisation), industryModifier);
            }
            foreach (var building in constructedBuildings)
                totalImpact += EnvironmentRules.ApplyPolicy(building.Impact.Scale(tick.utilisation), industryModifier);
            environmentState.ApplyIndustryImpact(totalImpact, tickInterval);
            PublishReport(tick, tickInterval);
        }

        private ColonyTick CalculateEconomy(float seconds)
        {
            economy.Clear();
            foreach (var building in coreBuildings) economy.Add(building.Economy);
            foreach (var building in constructedBuildings) economy.Add(building.Economy);
            return ColonyRules.CalculateTick(economy, population, populationGrowthRate, workforceRatio,
                productionModifier, upkeepModifier, environmentState.Habitability, seconds);
        }

        private void PublishReport(ColonyTick tick, float seconds)
        {
            resourceSystem.ApplySettlementReport(new SettlementReport
            {
                population = tick.population, workforce = tick.workforce,
                production = tick.production, upkeep = tick.upkeep,
                net = tick.production - tick.upkeep, tickInterval = seconds
            });
        }

        public void SetConstructedBuildings(IEnumerable<ManagedBuilding> definitions)
        {
            constructedBuildings.Clear();
            constructedBuildings.AddRange(definitions);
            RefreshReadout();
        }

        public void RefreshReadout()
        {
            if (!HasActiveSimulation) return;
            var tick = CalculateEconomy(0f);
            workforce = tick.workforce;
            PublishReport(tick, 0f);
        }

        public bool OverlapsStartingBuildings(Vector3 center, Vector3 dimensions)
        {
            var radius = new Vector2(dimensions.x, dimensions.z).magnitude * 0.5f;
            foreach (var visual in buildingVisuals)
                if (visual != null && Vector3.Distance(center, visual.transform.position) < radius
                    + new Vector2(visual.transform.localScale.x, visual.transform.localScale.z).magnitude * 0.5f)
                    return true;
            return false;
        }

        private void RebuildBuildings(IEnumerable<string> buildingIds)
        {
            buildings.Clear();
            foreach (var id in buildingIds)
            {
                var building = CreateBuilding(id);
                if (building != null)
                {
                    buildings.Add(building);
                }
            }

            if (buildings.Count == 0)
            {
                buildings.Add(new HabBlock());
                buildings.Add(new Factory());
                buildings.Add(new Utility());
            }

            SpawnVisuals();
        }

        private SettlementBuilding CreateBuilding(string id)
        {
            switch (id)
            {
                case "HabBlock":
                    return new HabBlock();
                case "Factory":
                    return new Factory();
                case "Utility":
                    return new Utility();
                default:
                    Debug.LogWarning($"Unknown building id '{id}', defaulting to Hab Block.");
                    return new HabBlock();
            }
        }

        private void SpawnVisuals()
        {
            ClearVisuals();
            if (settlementAnchor == null)
            {
                return;
            }

            var count = buildings.Count;
            for (var i = 0; i < count; i++)
            {
                var building = buildings[i];
                var dimensions = building.Id == "Factory" ? new Vector3(155f, 175f, 135f)
                    : building.Id == "Utility" ? new Vector3(125f, 145f, 115f) : new Vector3(120f, 195f, 120f);
                var visual = BuildingVisuals.Create(building.Id, building.DisplayName, settlementAnchor, dimensions * BuildingVisuals.WorldScale);
                var angle = (Mathf.PI * 2f / Mathf.Max(1, count)) * i;
                var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * buildingSpacing * BuildingVisuals.WorldScale;
                var localDirection = planet.transform.InverseTransformPoint(settlementAnchor.TransformPoint(offset)).normalized;
                var ground = planet.transform.TransformPoint(planet.EvaluateSurfacePoint(localDirection));
                var normal = planet.transform.TransformDirection(localDirection);
                var forward = Vector3.ProjectOnPlane(settlementAnchor.forward, normal).normalized;
                visual.transform.SetPositionAndRotation(ground, Quaternion.LookRotation(forward, normal));
                buildingVisuals.Add(visual);
                var managed = visual.AddComponent<ManagedBuilding>();
                managed.Configure(building.Id, building.DisplayName, new BuildingEconomy
                {
                    capacity = building.PopulationCapacity, jobs = building.WorkforceSlots,
                    outputPerWorker = building.ProductionPerWorker, upkeep = building.UpkeepPerCycle
                }, building.EnvironmentImpact, resourceSystem, this);
                coreBuildings.Add(managed);
            }
            BuildServiceRoads();
        }

        private void BuildServiceRoads()
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var roadTriangles = new List<int>();
            var markingTriangles = new List<int>();
            foreach (var visual in buildingVisuals)
            {
                var end = settlementAnchor.InverseTransformPoint(visual.transform.position);
                end.y = 0;
                for (var section = 0; section < 12; section++)
                {
                    var a = end * (section / 12f); var b = end * ((section + 1) / 12f);
                    AddRoadStrip(a, b, 26f * BuildingVisuals.WorldScale, 0.6f, roadTriangles, vertices, normals, uv);
                    if (section % 2 == 0)
                        AddRoadStrip(a, Vector3.Lerp(a, b, 0.6f), 1.5f * BuildingVisuals.WorldScale, 0.7f,
                            markingTriangles, vertices, normals, uv);
                }
            }
            serviceRoadMesh = new Mesh { name = "Surface-conforming service causeways" };
            serviceRoadMesh.SetVertices(vertices); serviceRoadMesh.SetNormals(normals); serviceRoadMesh.SetUVs(0, uv);
            serviceRoadMesh.subMeshCount = 2;
            serviceRoadMesh.SetTriangles(roadTriangles, 0); serviceRoadMesh.SetTriangles(markingTriangles, 1);
            serviceRoadMesh.RecalculateBounds();
            serviceRoads = new GameObject("Core service causeways", typeof(MeshFilter), typeof(MeshRenderer));
            serviceRoads.transform.SetParent(planet.transform, false);
            serviceRoads.GetComponent<MeshFilter>().sharedMesh = serviceRoadMesh;
            serviceRoads.GetComponent<MeshRenderer>().sharedMaterials = BuildingVisuals.ServiceRoadMaterials;
            serviceRoads.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void AddRoadStrip(Vector3 a, Vector3 b, float width, float elevation, List<int> triangles,
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv)
        {
            var side = Vector3.Cross(Vector3.up, (b - a).normalized) * width * 0.5f;
            var start = vertices.Count;
            foreach (var p in new[] { a - side, b - side, b + side, a + side })
            {
                var direction = planet.transform.InverseTransformPoint(settlementAnchor.TransformPoint(p)).normalized;
                vertices.Add(planet.EvaluateSurfacePoint(direction) + direction * elevation);
                normals.Add(direction);
            }
            uv.AddRange(new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        private void ClearVisuals()
        {
            if (serviceRoads != null) Destroy(serviceRoads);
            if (serviceRoadMesh != null) Destroy(serviceRoadMesh);
            foreach (var visual in buildingVisuals)
            {
                if (visual != null)
                {
                    Destroy(visual);
                }
            }

            buildingVisuals.Clear();
            coreBuildings.Clear();
        }

        private void OnDestroy()
        {
            if (serviceRoadMesh != null) Destroy(serviceRoadMesh);
        }

        private void EnsureAnchor(Vector3 position, Vector3 normal)
        {
            // A prior planet may already be queued for destruction during new/load.
            // Create a fresh anchor instead of reparenting part of that doomed hierarchy.
            if (settlementAnchor != null && settlementAnchor.parent != planet.transform)
            {
                Destroy(settlementAnchor.gameObject);
                settlementAnchor = null;
            }
            if (settlementAnchor == null)
            {
                var anchorGO = new GameObject("SettlementAnchor");
                settlementAnchor = anchorGO.transform;
            }

            settlementAnchor.SetParent(planet.transform, false);
            settlementAnchor.position = position;
            settlementAnchor.rotation = Quaternion.FromToRotation(Vector3.up, normal.normalized);
        }

    }
}
