using UnityEngine;
using WH30K.Game;
using WH30K.Sim.Environment;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;
using WH30K.UI;

namespace WH30K.Sim.Events
{
    public class ColonyEventSystem : MonoBehaviour
    {
        private NewGameMenu menu;
        private ResourceSystem resourceSystem;
        private EnvironmentState environmentState;
        private Settlement settlement;
        private EventSchedule schedule;
        public bool HasActiveSession => schedule != null;
        private float SafetyCost => 60f * resourceSystem.ResourceYieldMultiplier;

        public void ConfigureMenu(NewGameMenu newMenu) => menu = newMenu;

        public void BeginSession(GameSettings.DifficultyDefinition definition, ResourceSystem resources,
            EnvironmentState environment, Settlement settlementInstance, int seed, EventSnapshot snapshot = default)
        {
            EndSession();
            resourceSystem = resources;
            environmentState = environment;
            settlement = settlementInstance;
            schedule = new EventSchedule(seed, definition.eventFrequencyMultiplier, snapshot);
            if (schedule.AwaitingChoice) TriggerIndustrialPolicyEvent();
        }

        public void EndSession()
        {
            schedule = null;
            menu?.ShowEventPanel(false);
        }

        public EventSnapshot CreateSnapshot() => schedule != null ? schedule.CreateSnapshot() : default;

        private void Update()
        {
            if (schedule == null) return;
            if (schedule.Advance(ColonyClock.DeltaTime)) TriggerIndustrialPolicyEvent();
            if (schedule.AwaitingChoice) menu?.SetEventChoiceAInteractable(resourceSystem.CanAfford(SafetyCost));
        }

        private void TriggerIndustrialPolicyEvent()
        {
            menu?.PromptEvent("Industrial Priorities",
                "Factory prefects demand higher output while the air scrubbers struggle to keep pace. " +
                "Fund permanent cleaner operating practices, or raise output and industrial emissions?",
                $"Safety retrofits (-{SafetyCost:0} stockpile)\nLower output and emissions",
                OnSafetyChosen, "Overclock furnaces\nMore output and emissions", OnOverdriveChosen);
            menu?.SetEventChoiceAInteractable(resourceSystem.CanAfford(SafetyCost));
        }

        private void OnSafetyChosen()
        {
            if (schedule == null || !schedule.AwaitingChoice) return;
            if (!resourceSystem.TrySpend(SafetyCost))
            {
                TriggerIndustrialPolicyEvent();
                return;
            }
            schedule.Resolve();
            settlement.AdjustPolicy(-0.08f, -0.05f, null, -0.15f);
            environmentState.ApplyImmediateImpact(new EnvironmentImpact
            {
                co2Delta = -7f, o2Delta = 0.35f, waterDelta = -2.5f, temperatureDelta = -0.04f
            });
            settlement.RefreshReadout();
            menu?.AppendEventLog("Safety retrofits reduced industrial emissions and output.");
        }

        private void OnOverdriveChosen()
        {
            if (schedule == null || !schedule.Resolve()) return;
            resourceSystem.ModifyStockpile(45f * resourceSystem.ResourceYieldMultiplier);
            settlement.AdjustPolicy(0.12f, 0.08f, null, 0.15f);
            environmentState.ApplyImmediateImpact(new EnvironmentImpact
            {
                co2Delta = 10f, o2Delta = -0.4f, waterDelta = 4f, temperatureDelta = 0.09f
            });
            settlement.RefreshReadout();
            menu?.AppendEventLog("Furnace overdrive increased output and industrial emissions.");
        }

#if UNITY_EDITOR
        public void TriggerDebugEventNow()
        {
            if (schedule == null) return;
            schedule.TriggerNow();
            TriggerIndustrialPolicyEvent();
        }
#endif
    }
}
