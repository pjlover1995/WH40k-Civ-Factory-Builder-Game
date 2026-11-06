using System;

namespace WH30K.Sim
{
    [Serializable]
    public struct ClockSnapshot
    {
        public double simulationSeconds;
        public double playedSeconds;
        public int speed;
        public bool paused;
    }

    public static class ColonyCalendar
    {
        public const double SecondsPerMonth = 150d;
        public const double SecondsPerYear = SecondsPerMonth * 12d;
        public static int Year(double seconds) => (int)(seconds / SecondsPerYear) + 1;
        public static int Month(double seconds) => (int)(seconds / SecondsPerMonth) % 12 + 1;
        public static int Day(double seconds) => (int)(seconds / (SecondsPerMonth / 30d)) % 30 + 1;
        public static bool Valid(ClockSnapshot state)
            => !double.IsNaN(state.simulationSeconds) && !double.IsInfinity(state.simulationSeconds)
            && !double.IsNaN(state.playedSeconds) && !double.IsInfinity(state.playedSeconds)
            && state.simulationSeconds >= 0d && state.simulationSeconds < 1e11
            && state.playedSeconds >= 0d && state.playedSeconds < 1e11
            && (state.speed == 0 || state.speed == 1 || state.speed == 3 || state.speed == 10);

        public static ClockSnapshot Advance(ClockSnapshot state, double realSeconds, bool decisionPending)
        {
            if (!Valid(state) || double.IsNaN(realSeconds) || double.IsInfinity(realSeconds) || realSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(realSeconds));
            state.playedSeconds += realSeconds;
            if (!state.paused && !decisionPending)
                state.simulationSeconds += realSeconds * (state.speed == 0 ? 1 : state.speed);
            return state;
        }
    }
}
