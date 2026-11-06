using System;
using System.Collections.Generic;

namespace WH30K.Sim
{
    // Actual removal over the last 30 simulated seconds, independent of game speed.
    public sealed class DepositUsage
    {
        public const double WindowSeconds = 30;
        private readonly Queue<(double time, float amount)> samples = new Queue<(double, float)>();
        public void Record(double time, float amount)
        {
            while (samples.Count > 0 && samples.Peek().time <= time - WindowSeconds) samples.Dequeue();
            if (amount > 0 && ColonyRules.IsFinite(amount)) samples.Enqueue((time, amount));
        }
        public float MonthlyRate(double time)
        {
            while (samples.Count > 0 && samples.Peek().time <= time - WindowSeconds) samples.Dequeue();
            double total = 0;
            foreach (var sample in samples) total += sample.amount;
            return time <= 0 ? 0 : (float)(total / Math.Min(time, WindowSeconds) * ColonyCalendar.SecondsPerMonth);
        }
        public static float LifeMonths(float remaining, float monthlyRate)
            => remaining <= 0 ? 0 : monthlyRate > 0 ? remaining / monthlyRate : float.PositiveInfinity;
    }
}
