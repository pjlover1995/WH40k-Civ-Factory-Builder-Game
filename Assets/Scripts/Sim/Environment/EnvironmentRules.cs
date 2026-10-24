using System;

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

    public static class EnvironmentRules
    {
        public static EnvironmentImpact ApplyPolicy(EnvironmentImpact impact, float modifier)
        {
            return new EnvironmentImpact
            {
                co2Delta = impact.co2Delta * (impact.co2Delta > 0f ? modifier : 1f),
                o2Delta = impact.o2Delta * (impact.o2Delta < 0f ? modifier : 1f),
                waterDelta = impact.waterDelta * (impact.waterDelta > 0f ? modifier : 1f),
                temperatureDelta = impact.temperatureDelta * (impact.temperatureDelta > 0f ? modifier : 1f)
            };
        }

        public static EnvironmentSnapshot ApplyIndustry(EnvironmentSnapshot state, EnvironmentImpact impact,
            float seconds, float harshness)
        {
            impact.co2Delta *= seconds * harshness;
            impact.o2Delta *= seconds * 0.5f;
            impact.waterDelta *= seconds * harshness;
            impact.temperatureDelta *= seconds * 0.5f;
            return ApplyImmediate(state, impact);
        }

        public static EnvironmentSnapshot ApplyImmediate(EnvironmentSnapshot state, EnvironmentImpact impact)
        {
            state.Co2 = Math.Max(0f, state.Co2 + impact.co2Delta);
            state.O2 = Math.Max(0f, state.O2 + impact.o2Delta);
            state.WaterPollution = Math.Max(0f, state.WaterPollution + impact.waterDelta);
            state.GlobalTemperatureOffset += impact.temperatureDelta;
            return state;
        }
    }
}
