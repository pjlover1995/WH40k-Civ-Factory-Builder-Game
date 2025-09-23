using System;
using UnityEngine;
using WH30K.Game;
using WH30K.UI;

namespace WH30K.Sim.Environment
{
    [Serializable]
    public struct EnvironmentSnapshot
    {
        public float Co2;
        public float O2;
        public float WaterPollution;
        public float GlobalTemperatureOffset;
    }

    [Serializable]
    public struct EnvironmentImpact
    {
        public float co2Delta;
        public float o2Delta;
        public float waterDelta;
        public float temperatureDelta;

        public static EnvironmentImpact Zero => new EnvironmentImpact();

        public EnvironmentImpact Scale(float factor)
        {
            return new EnvironmentImpact
            {
                co2Delta = co2Delta * factor,
                o2Delta = o2Delta * factor,
                waterDelta = waterDelta * factor,
                temperatureDelta = temperatureDelta * factor
            };
        }

        public static EnvironmentImpact operator +(EnvironmentImpact a, EnvironmentImpact b)
        {
            return new EnvironmentImpact
            {
                co2Delta = a.co2Delta + b.co2Delta,
                o2Delta = a.o2Delta + b.o2Delta,
                waterDelta = a.waterDelta + b.waterDelta,
                temperatureDelta = a.temperatureDelta + b.temperatureDelta
            };
        }
    }

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

        public void ConfigureMenu(NewGameMenu newMenu)
        {
            menu = newMenu;
            PushUpdate();
        }

        public void ResetForNewGame(GameSettings.DifficultyDefinition definition)
        {
            currentDifficulty = definition;
            co2 = 410f * definition.environmentHarshnessMultiplier;
            o2 = 20.8f / Mathf.Max(0.5f, definition.environmentHarshnessMultiplier * 0.85f);
            waterPollution = 4f * definition.environmentHarshnessMultiplier;
            globalTempOffset = 0f;
            PushUpdate();
        }

        public void ApplyIndustryImpact(EnvironmentImpact impact, float deltaTime)
        {
            var harshness = currentDifficulty.environmentHarshnessMultiplier;
            co2 = Mathf.Max(0f, co2 + impact.co2Delta * deltaTime * harshness);
            o2 = Mathf.Max(0f, o2 + impact.o2Delta * deltaTime * 0.5f);
            waterPollution = Mathf.Max(0f, waterPollution + impact.waterDelta * deltaTime * harshness);
            globalTempOffset += impact.temperatureDelta * deltaTime * 0.5f;
            PushUpdate();
        }

        public void ApplyImmediateImpact(EnvironmentImpact impact)
        {
            co2 = Mathf.Max(0f, co2 + impact.co2Delta);
            o2 = Mathf.Max(0f, o2 + impact.o2Delta);
            waterPollution = Mathf.Max(0f, waterPollution + impact.waterDelta);
            globalTempOffset += impact.temperatureDelta;
            PushUpdate();
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
