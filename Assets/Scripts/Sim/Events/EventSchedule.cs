using System;

namespace WH30K.Sim.Events
{
    [Serializable]
    public struct EventSnapshot
    {
        public bool initialized;
        public uint randomState;
        public float remainingSeconds;
        public bool awaitingChoice;
    }

    // Save the actual timer and PRNG state so loading cannot reset or skip a decision.
    public sealed class EventSchedule
    {
        // TODO (review later): reconsider recurring priority transmissions and their pacing.
        // Temporarily disabled; retain timer/PRNG/pending-choice saves so they can resume when re-enabled.
        public const bool PriorityTransmissionsEnabled = false;
        private EventSnapshot state;
        private readonly float frequency;
        private readonly bool enabled;
        public bool AwaitingChoice => enabled && state.awaitingChoice;

        public EventSchedule(int seed, float frequency, EventSnapshot snapshot = default,
            bool enabled = PriorityTransmissionsEnabled)
        {
            this.enabled = enabled;
            this.frequency = Math.Max(0.25f, frequency);
            state = snapshot;
            if (!state.initialized)
            {
                state.initialized = true;
                state.randomState = unchecked((uint)(seed ^ 0x5EC7));
                if (state.randomState == 0) state.randomState = 1;
                ScheduleNext();
            }
        }

        public bool Advance(float seconds)
        {
            if (!enabled || state.awaitingChoice) return false;
            state.remainingSeconds = Math.Max(0f, state.remainingSeconds - Math.Max(0f, seconds));
            if (state.remainingSeconds > 0f) return false;
            state.awaitingChoice = true;
            return true;
        }

        public bool Resolve()
        {
            if (!enabled || !state.awaitingChoice) return false;
            ScheduleNext();
            return true;
        }

        public void TriggerNow()
        {
            if (!enabled) return;
            state.remainingSeconds = 0f;
            state.awaitingChoice = true;
        }

        public EventSnapshot CreateSnapshot() => state;

        public static bool IsValid(EventSnapshot snapshot)
        {
            return !snapshot.initialized || (snapshot.randomState != 0
                && ColonyRules.IsFinite(snapshot.remainingSeconds) && snapshot.remainingSeconds >= 0f
                && snapshot.remainingSeconds <= 180f);
        }

        private void ScheduleNext()
        {
            var value = state.randomState;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state.randomState = value;
            state.remainingSeconds = (18f + 27f * (value / (float)uint.MaxValue)) / frequency;
            state.awaitingChoice = false;
        }
    }
}
