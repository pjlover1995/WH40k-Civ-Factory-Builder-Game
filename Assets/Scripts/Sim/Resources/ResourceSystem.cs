using System;
using UnityEngine;
using WH30K.Game;
using WH30K.Sim.Settlements;
using WH30K.UI;

namespace WH30K.Sim.Resources
{
    [Serializable]
    public struct ResourceSnapshot
    {
        public float Population;
        public float Workforce;
        public float ProductionPerCycle;
        public float UpkeepPerCycle;
        public float NetProduction;
        public float Stockpile;
        public float Water;
        public int WaterVersion;
        public float Food;
        public int FoodVersion;
    }

    /// <summary>
    /// Tracks resource stockpiles and exposes a simple interface for the settlement simulation.
    /// </summary>
    public class ResourceSystem : MonoBehaviour
    {
        private NewGameMenu menu;
        private float stockpile;
        private float water;
        private float food;
        private float population;
        private float workforce;
        private float productionPerCycle;
        private float upkeepPerCycle;
        private float netProduction;
        private GameSettings.DifficultyDefinition currentDifficulty;

        public float ResourceYieldMultiplier => currentDifficulty.resourceYieldMultiplier;
        // Stockpile in snapshots is retained only to round-trip historical saves.
        private IndustrySystem Industry => GetComponent<IndustrySystem>();
        public float Stockpile => Industry != null ? Industry.ConstructionSupplyTotals().basic : 0;
        public float Water => water;
        public float Food => food;
        public const float WaterCapacity = WaterRules.Capacity;
        public bool CanAfford(float cost) => Industry != null && Industry.CanAffordConstruction(cost, 0);

        public bool TrySpend(float cost)
        {
            if (Industry == null || !Industry.TrySpendConstruction(cost, 0)) return false;
            PushUpdate();
            return true;
        }

        public void ConfigureMenu(NewGameMenu newMenu)
        {
            menu = newMenu;
            PushUpdate();
        }

        public void ResetForNewGame(GameSettings.DifficultyDefinition definition, bool explorator = false)
        {
            currentDifficulty = definition;
            stockpile = 0; // Credits are retired; physical inventories are authoritative.
            water = WaterRules.StartingReserve * definition.resourceYieldMultiplier;
            food = explorator ? ShipRules.StartingFood : FarmRules.StartingFood * definition.resourceYieldMultiplier;
            population = 0f;
            workforce = 0f;
            productionPerCycle = 0f;
            upkeepPerCycle = 0f;
            netProduction = 0f;
            PushUpdate();
        }

        public void ApplySettlementReport(SettlementReport report)
        {
            population = report.population;
            workforce = report.workforce;
            productionPerCycle = report.production;
            upkeepPerCycle = report.upkeep;
            netProduction = report.net;
            PushUpdate();
        }

        public void ModifyStockpile(float delta)
        {
            if (delta > 0) Industry?.AddRecoveredSupplies(delta);
            else if (delta < 0) TrySpend(-delta);
            PushUpdate();
        }

        public float ConsumeWater(float amount)
        {
            var used = Mathf.Min(water, Mathf.Max(0f, amount));
            water -= used;
            return used;
        }

        public float AddWater(float amount)
        {
            var added = Mathf.Min(Mathf.Max(0f, amount), WaterCapacity - water);
            water += added;
            return added;
        }

        public float AddFood(float amount) { var added = Mathf.Min(Mathf.Max(0, amount), FarmRules.FoodCapacity - food); food += added; return added; }
        public float ConsumeFood(float amount) { var used = Mathf.Min(food, Mathf.Max(0, amount)); food -= used; return used; }

        public ResourceSnapshot CreateSnapshot()
        {
            return new ResourceSnapshot
            {
                Population = population,
                Workforce = workforce,
                ProductionPerCycle = productionPerCycle,
                UpkeepPerCycle = upkeepPerCycle,
                NetProduction = netProduction,
                Stockpile = stockpile,
                Water = water,
                WaterVersion = 1, Food = food, FoodVersion = 1
            };
        }

        public void LoadFromSnapshot(ResourceSnapshot snapshot, GameSettings.DifficultyDefinition definition)
        {
            currentDifficulty = definition;
            population = snapshot.Population;
            workforce = snapshot.Workforce;
            productionPerCycle = snapshot.ProductionPerCycle;
            upkeepPerCycle = snapshot.UpkeepPerCycle;
            netProduction = snapshot.NetProduction;
            stockpile = snapshot.Stockpile;
            food = snapshot.FoodVersion > 0 ? snapshot.Food : FarmRules.StartingFood * definition.resourceYieldMultiplier;
            water = snapshot.WaterVersion > 0 ? Mathf.Clamp(snapshot.Water, 0f, WaterCapacity) : WaterRules.StartingReserve * definition.resourceYieldMultiplier;
            PushUpdate();
        }

        private void PushUpdate()
        {
            menu?.UpdateResourceReadout(CreateSnapshot());
        }
    }
}
