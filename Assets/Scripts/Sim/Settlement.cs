using System;
using System.Collections.Generic;
using UnityEngine;
using WH30K.Game;
using WH30K.Planet;
using WH30K.Sim.Buildings;
using WH30K.Sim.Environment;
using WH30K.Sim.Resources;
using WH30K.UI;

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
        private static readonly Dictionary<Color, Material> MaterialCache = new Dictionary<Color, Material>();

        private LODPlanet planet;
        private ResourceSystem resourceSystem;
        private EnvironmentState environmentState;
        private NewGameMenu menu;
        private Transform settlementAnchor;
        private GameSettings.DifficultyDefinition currentDifficulty;

        private float population;
        private float workforce;
        private float productionModifier = 1f;
        private float upkeepModifier = 1f;
        private float tickTimer;
        private Vector3 storedPosition;
        private Vector3 storedNormal;

        public void ConfigureMenu(NewGameMenu newMenu)
        {
            menu = newMenu;
        }

        public void BeginNewGame(GameSettings.DifficultyDefinition definition, LODPlanet planetInstance,
            Vector3 surfacePosition, Vector3 surfaceNormal, ResourceSystem resources, EnvironmentState environment)
        {
            currentDifficulty = definition;
            planet = planetInstance;
            resourceSystem = resources;
            environmentState = environment;
            productionModifier = definition.resourceYieldMultiplier;
            upkeepModifier = Mathf.Lerp(1f, 1.3f, Mathf.InverseLerp(1f, 1.4f, definition.environmentHarshnessMultiplier));
            population = Mathf.Max(120f, 160f * definition.resourceYieldMultiplier);
            workforce = population * workforceRatio;
            tickTimer = 0f;
            storedPosition = surfacePosition;
            storedNormal = surfaceNormal;

            ClearVisuals();
            EnsureAnchor(surfacePosition, surfaceNormal);
            RebuildBuildings(new[] { "HabBlock", "Factory", "Utility" });
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
            population = snapshot.population;
            workforce = snapshot.workforce;
            tickTimer = 0f;
            storedPosition = snapshot.position;
            storedNormal = snapshot.normal.sqrMagnitude > 0.01f ? snapshot.normal.normalized : snapshot.position.normalized;

            ClearVisuals();
            EnsureAnchor(storedPosition, storedNormal);
            RebuildBuildings(snapshot.buildings != null && snapshot.buildings.Length > 0
                ? snapshot.buildings
                : new[] { "HabBlock", "Factory", "Utility" });
        }

        private void Update()
        {
            if (planet == null || resourceSystem == null || environmentState == null)
            {
                return;
            }

            tickTimer += Time.deltaTime;
            if (tickTimer < tickInterval)
            {
                return;
            }

            tickTimer -= tickInterval;
            RunTick();
        }

        public SettlementSnapshot CreateSnapshot()
        {
            var ids = new string[buildings.Count];
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
                buildings = ids
            };
        }

        public void AdjustPolicy(float productionDelta, float upkeepDelta, string message)
        {
            productionModifier = Mathf.Clamp(productionModifier + productionDelta, 0.25f, 3f);
            upkeepModifier = Mathf.Clamp(upkeepModifier + upkeepDelta, 0.1f, 3f);
            if (!string.IsNullOrEmpty(message))
            {
                menu?.AppendEventLog(message);
            }
        }

        private void RunTick()
        {
            var capacity = 0f;
            var workforceCapacity = 0f;
            foreach (var building in buildings)
            {
                capacity += building.PopulationCapacity;
                workforceCapacity += building.WorkforceSlots;
            }

            var growthTarget = Mathf.Min(capacity, population + (capacity - population) * populationGrowthRate);
            population = Mathf.MoveTowards(population, growthTarget, populationGrowthRate * capacity * tickInterval);
            workforce = Mathf.Min(workforceCapacity, population * workforceRatio);

            var availableWorkers = workforce;
            var production = 0f;
            var upkeep = 0f;
            var totalImpact = EnvironmentImpact.Zero;

            foreach (var building in buildings)
            {
                var assigned = Mathf.Min(building.WorkforceSlots, availableWorkers);
                availableWorkers -= assigned;
                production += assigned * building.ProductionPerWorker;
                upkeep += building.UpkeepPerCycle;
                if (building.WorkforceSlots > 0f)
                {
                    var utilisation = assigned / Mathf.Max(1f, building.WorkforceSlots);
                    totalImpact += building.EnvironmentImpact.Scale(utilisation);
                }
            }

            production *= productionModifier;
            upkeep *= upkeepModifier;
            var net = production - upkeep;

            resourceSystem.ApplySettlementReport(new SettlementReport
            {
                population = population,
                workforce = workforce,
                production = production,
                upkeep = upkeep,
                net = net,
                tickInterval = tickInterval
            });

            environmentState.ApplyIndustryImpact(totalImpact, tickInterval);
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
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = building.DisplayName;
                visual.transform.SetParent(settlementAnchor, false);
                visual.transform.localScale = new Vector3(120f, 160f, 120f);
                var angle = (Mathf.PI * 2f / Mathf.Max(1, count)) * i;
                var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * buildingSpacing;
                visual.transform.localPosition = offset + Vector3.up * 80f;
                var collider = visual.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }
                var renderer = visual.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = GetMaterial(building.DisplayColor);
                buildingVisuals.Add(visual);
            }
        }

        private void ClearVisuals()
        {
            foreach (var visual in buildingVisuals)
            {
                if (visual != null)
                {
                    Destroy(visual);
                }
            }

            buildingVisuals.Clear();
        }

        private void EnsureAnchor(Vector3 position, Vector3 normal)
        {
            if (settlementAnchor == null)
            {
                var anchorGO = new GameObject("SettlementAnchor");
                settlementAnchor = anchorGO.transform;
            }

            settlementAnchor.SetParent(planet.transform, false);
            settlementAnchor.position = position;
            settlementAnchor.rotation = Quaternion.FromToRotation(Vector3.up, normal.normalized);
        }

        private static Material GetMaterial(Color color)
        {
            if (!MaterialCache.TryGetValue(color, out var material) || material == null)
            {
                material = new Material(Shader.Find("Standard")) { color = color };
                MaterialCache[color] = material;
            }

            return material;
        }
    }
}
