using System.Collections;
using UnityEngine;
using WH30K.Game;
using WH30K.Sim.Environment;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;
using WH30K.UI;

namespace WH30K.Sim.Events
{
    /// <summary>
    /// Lightweight event driver that surfaces narrative choices and applies their systemic impacts.
    /// </summary>
    public class ColonyEventSystem : MonoBehaviour
    {
        private const int EventSeedSalt = 0x5EC7;

        private NewGameMenu menu;
        private ResourceSystem resourceSystem;
        private EnvironmentState environmentState;
        private Settlement settlement;
        private GameSettings.DifficultyDefinition difficulty;
        private Coroutine eventCoroutine;
        private System.Random rng;

        public bool HasActiveSession => rng != null;

        public void ConfigureMenu(NewGameMenu newMenu)
        {
            menu = newMenu;
        }

        public void BeginSession(GameSettings.DifficultyDefinition definition, ResourceSystem resources,
            EnvironmentState environment, Settlement settlementInstance, int seed)
        {
            EndSession();

            difficulty = definition;
            resourceSystem = resources;
            environmentState = environment;
            settlement = settlementInstance;
            rng = new System.Random(seed ^ EventSeedSalt);
            eventCoroutine = StartCoroutine(EventRoutine());
        }

        public void EndSession()
        {
            if (eventCoroutine != null)
            {
                StopCoroutine(eventCoroutine);
                eventCoroutine = null;
            }

            menu?.ShowEventPanel(false);
            rng = null;
        }

        private IEnumerator EventRoutine()
        {
            var randomValue = rng != null ? (float)rng.NextDouble() : UnityEngine.Random.value;
            var delay = Mathf.Lerp(18f, 45f, randomValue);
            delay /= Mathf.Max(0.25f, difficulty.eventFrequencyMultiplier);
            yield return new WaitForSeconds(delay);
            TriggerIndustrialPolicyEvent();
            eventCoroutine = null;
        }

        private void TriggerIndustrialPolicyEvent()
        {
            const string title = "Industrial Priorities";
            var body = "Factory prefects demand higher output while the air scrubbers struggle to keep pace. " +
                       "Will you divert resources to safety retrofits or order the furnaces to burn hotter?";

            var safetyOption = "Fund safety retrofits (-Stockpile, -Production, cleaner air)";
            var overdriveOption = "Overclock furnaces (+Stockpile, +Production, harsher environment)";

            menu?.PromptEvent(title, body, safetyOption, OnSafetyChosen, overdriveOption, OnOverdriveChosen);
        }

        private void OnSafetyChosen()
        {
            resourceSystem.ModifyStockpile(-60f * resourceSystem.ResourceYieldMultiplier);
            settlement.AdjustPolicy(-0.08f, -0.05f, null);
            environmentState.ApplyImmediateImpact(new EnvironmentImpact
            {
                co2Delta = -7f,
                o2Delta = 0.35f,
                waterDelta = -2.5f,
                temperatureDelta = -0.04f
            });
            menu?.AppendEventLog("Mandated safety upgrades across the industrial sector.");
            ScheduleNextEvent();
        }

        private void OnOverdriveChosen()
        {
            resourceSystem.ModifyStockpile(45f * resourceSystem.ResourceYieldMultiplier);
            settlement.AdjustPolicy(0.12f, 0.08f, null);
            environmentState.ApplyImmediateImpact(new EnvironmentImpact
            {
                co2Delta = 10f,
                o2Delta = -0.4f,
                waterDelta = 4f,
                temperatureDelta = 0.09f
            });
            menu?.AppendEventLog("Ordered furnaces into overdrive to meet production quotas.");
            ScheduleNextEvent();
        }

        private void ScheduleNextEvent()
        {
            if (rng == null || !isActiveAndEnabled)
            {
                return;
            }

            if (eventCoroutine != null)
            {
                StopCoroutine(eventCoroutine);
            }

            eventCoroutine = StartCoroutine(EventRoutine());
        }

#if UNITY_EDITOR
        public void TriggerDebugEventNow()
        {
            if (!HasActiveSession)
            {
                return;
            }

            if (eventCoroutine != null)
            {
                StopCoroutine(eventCoroutine);
                eventCoroutine = null;
            }

            TriggerIndustrialPolicyEvent();
        }
#endif
    }
}
