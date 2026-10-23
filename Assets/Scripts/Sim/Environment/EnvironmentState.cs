using System;
using UnityEngine;
using WH30K.Game;
using WH30K.UI;

namespace WH30K.Sim.Environment
{

    /// <summary>
    /// Aggregates environmental scalar values and pushes them to the HUD.
    /// </summary>
    public class EnvironmentState : MonoBehaviour
    {
        private NewGameMenu menu;
        private GameSettings.DifficultyDefinition currentDifficulty;

        private float co2;
        private float o2;
        private float waterPollution;
        private float globalTempOffset;

        public float Habitability => ColonyRules.Habitability(co2, o2, waterPollution, globalTempOffset);

        public void ConfigureMenu(NewGameMenu newMenu)
        {
            menu = newMenu;
            PushUpdate();
        }

        public void ResetForNewGame(GameSettings.DifficultyDefinition definition)
        {
            currentDifficulty = definition;
            co2 = 410f * definition.environmentHarshnessMultiplier;
            o2 = 20.8f - (definition.environmentHarshnessMultiplier - 1f) * 1.5f;
            waterPollution = 4f * definition.environmentHarshnessMultiplier;
            globalTempOffset = 0f;
            PushUpdate();
        }

        public void ApplyIndustryImpact(EnvironmentImpact impact, float deltaTime)
        {
            LoadFromSnapshot(EnvironmentRules.ApplyIndustry(CreateSnapshot(), impact, deltaTime,
                currentDifficulty.environmentHarshnessMultiplier), currentDifficulty);
        }

        public void ApplyImmediateImpact(EnvironmentImpact impact)
        {
            LoadFromSnapshot(EnvironmentRules.ApplyImmediate(CreateSnapshot(), impact), currentDifficulty);
        }

        public EnvironmentSnapshot CreateSnapshot()
        {
            return new EnvironmentSnapshot
            {
                Co2 = co2,
                O2 = o2,
                WaterPollution = waterPollution,
                GlobalTemperatureOffset = globalTempOffset
            };
        }

        public void LoadFromSnapshot(EnvironmentSnapshot snapshot, GameSettings.DifficultyDefinition definition)
        {
            currentDifficulty = definition;
            co2 = snapshot.Co2;
            o2 = snapshot.O2;
            waterPollution = snapshot.WaterPollution;
            globalTempOffset = snapshot.GlobalTemperatureOffset;
            PushUpdate();
        }

        private void PushUpdate()
        {
            menu?.UpdateEnvironmentReadout(CreateSnapshot());
        }
    }
}
