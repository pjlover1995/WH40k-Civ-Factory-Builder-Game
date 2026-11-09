using System;
using System.Collections.Generic;

namespace WH30K.Sim
{
    public sealed class ReserveOutlook
    {
        private const double WindowSeconds = ColonyCalendar.SecondsPerMonth;
        private readonly Queue<(double end, double duration, float food, float water)> samples =
            new Queue<(double, double, float, float)>();
        private double elapsed, foodTotal, waterTotal;

        public bool Ready => elapsed >= WindowSeconds;
        private double ObservedSeconds => Math.Min(elapsed, WindowSeconds);
        public float FoodPerMonth => (float)Math.Max(0, foodTotal);
        public float WaterPerMonth
        {
            get
            {
                if (samples.Count == 0) return 0;
                var first = samples.Peek();
                var outside = Math.Max(0, elapsed - WindowSeconds - (first.end - first.duration));
                return (float)Math.Max(0, waterTotal - first.water * (outside / first.duration));
            }
        }

        public void Record(float seconds, float food, float water)
        {
            if (seconds <= 0) return;
            elapsed += seconds;
            if (food > 0 || water > 0)
            {
                samples.Enqueue((elapsed, seconds, food, water));
                foodTotal += food;
                waterTotal += water;
            }
            while (samples.Count > 0 && samples.Peek().end <= elapsed - WindowSeconds)
            {
                var expired = samples.Dequeue();
                foodTotal -= expired.food;
                waterTotal -= expired.water;
            }
        }

        public void Reset()
        {
            samples.Clear();
            elapsed = foodTotal = waterTotal = 0;
        }

        public string Describe(bool food, float reserve, float demand)
        {
            var resource = food ? "FOOD" : "WATER";
            var source = food ? "Farm food delivered" : "Pump water added";
            var coverage = $"Stored {reserve:0.#} units | Reserve coverage {ColonyRules.SupplyLabel(reserve, demand)}\n"
                + $"Current demand {demand:0.#} / month\n"
                + "Reserve coverage uses stored supplies alone.";
            if (!Ready)
                return $"<b>{resource} OUTLOOK</b>\n{coverage}\n\n"
                    + $"Measuring inflow ({Math.Floor(ObservedSeconds):0} / {WindowSeconds:0} simulated seconds).";

            var inflow = food ? FoodPerMonth : WaterPerMonth;
            var net = Math.Round(inflow - demand, 1);
            var status = reserve <= 0 ? "Reserve is empty."
                : net < 0 ? $"Estimated time to empty {ColonyRules.SupplyLabel(reserve, demand - inflow)} at these rates."
                : net > 0 ? "Recent inflow exceeds demand." : "Recent inflow is roughly in balance with demand.";
            return $"<b>{resource} OUTLOOK</b>\n{coverage}\n\n"
                + $"{source} {inflow:0.#} / month\nNet change {net:+0.0;-0.0;0.0} / month\n{status}\n"
                + "Inflow uses the last simulated month. Orbital resupply is excluded.";
        }
    }
}
