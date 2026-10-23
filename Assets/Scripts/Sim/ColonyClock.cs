using UnityEngine;

namespace WH30K.Sim
{
    [DefaultExecutionOrder(-500)]
    public sealed class ColonyClock : MonoBehaviour
    {
        private static ColonyClock instance;
        private ClockSnapshot state;
        private bool active;
        public bool DecisionPending { get; set; }
        public bool Paused => state.paused || DecisionPending;
        public int Speed => state.speed == 0 ? 1 : state.speed;
        public static float DeltaTime { get; private set; }
        public ClockSnapshot Snapshot => state;
        private void Awake() { instance = this; }
        public void Begin() { state = new ClockSnapshot { speed = 1 }; active = true; DecisionPending = false; DeltaTime = 0f; }
        public void Restore(ClockSnapshot saved)
        {
            state = saved; if (state.speed == 0) state.speed = 1;
            active = true; DecisionPending = false; DeltaTime = 0f;
        }
        public void SetSpeed(int speed)
        {
            if (speed != 1 && speed != 3 && speed != 10) return;
            state.speed = speed; state.paused = false;
        }
        public void TogglePause() { state.paused = !state.paused; }
        private void Update()
        {
            DeltaTime = active && !Paused ? Time.unscaledDeltaTime * Speed : 0f;
            if (active) state = ColonyCalendar.Advance(state, Time.unscaledDeltaTime, DecisionPending);
        }
        private void OnDestroy() { if (instance == this) { instance = null; DeltaTime = 0f; } }
    }
}
