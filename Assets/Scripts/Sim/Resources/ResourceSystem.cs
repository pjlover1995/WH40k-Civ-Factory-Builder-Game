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
    }

    /// <summary>
    /// Tracks resource stockpiles and exposes a simple interface for the settlement simulation.
    /// </summary>
    public class ResourceSystem : MonoBehaviour
    {
        private NewGameMenu menu;
        private float stockpile;
        private float population;
        private float workforce;
        private float productionPerCycle;
        private float upkeepPerCycle;
        private float netProduction;
        private GameSettings.DifficultyDefinition currentDifficulty;

        public float ResourceYieldMultiplier => currentDifficulty.resourceYieldMultiplier;

        public void ConfigureMenu(NewGameMenu newMenu)
        {
            menu = newMenu;
            PushUpdate();
        }

        public void ResetForNewGame(GameSettings.DifficultyDefinition definition)
        {
            currentDifficulty = definition;
            stockpile = 500f * definition.resourceYieldMultiplier;
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
            stockpile = Mathf.Max(0f, stockpile + report.net * report.tickInterval);
            PushUpdate();
        }

        public void ModifyStockpile(float delta)
        {
            stockpile = Mathf.Max(0f, stockpile + delta);
            PushUpdate();
        }

        public ResourceSnapshot CreateSnapshot()
        {
            return new ResourceSnapshot
            {
                Population = population,
                Workforce = workforce,
                ProductionPerCycle = productionPerCycle,
                UpkeepPerCycle = upkeepPerCycle,
                NetProduction = netProduction,
                Stockpile = stockpile
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
            PushUpdate();
        }

        private void PushUpdate()
        {
            menu?.UpdateResourceReadout(CreateSnapshot());
        }
    }
}
