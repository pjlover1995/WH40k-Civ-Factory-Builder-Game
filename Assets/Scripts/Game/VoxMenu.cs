using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Sim;
using WH30K.Sim.Resources;

namespace WH30K.UI
{
    public partial class NewGameMenu
    {
        private const int VoxHistoryLimit = 5;
        private const int VoxQueueLimit = 8;
        private readonly VoxAlertPolicy voxAlerts = new VoxAlertPolicy();
        private readonly Queue<VoxAlert> pendingVox = new Queue<VoxAlert>();
        private readonly List<VoxAlert> recentVox = new List<VoxAlert>();
        private CommandUi.PortraitCard voxCard;
        private GameObject voxLog;
        private Text voxLogContent;
        private Texture2D captainPortrait, priestPortrait;
        private VoxAlert displayedVox;
        private double nextVoxSample;
        private float voxDisplaySeconds;
        private bool voxSessionStarted, voxCardVisible;

        private void BuildVox(Transform parent)
        {
            captainPortrait = Resources.Load<Texture2D>("Vox/Captain");
            priestPortrait = Resources.Load<Texture2D>("Vox/MechanicumPriest");
            voxCard = CommandUi.CreatePortraitCard(parent, font);
            voxLog = Panel("VoxLog", parent, new Vector2(580, 510), Vector2.zero, TextAnchor.MiddleCenter);
            Accent(voxLog, CommandUi.Signal);
            Label("VoxLogTitle", voxLog.transform, "VOX  /  RECENT TRANSMISSIONS", 17,
                new Vector2(20, -15), 530, 26, Gold);
            voxLogContent = Label("VoxHistory", voxLog.transform, "", 14, new Vector2(20, -60), 540, 386, Ivory);
            voxLogContent.supportRichText = false;
            Button("CloseVoxLog", voxLog.transform, new Vector2(20, -452), "CLOSE", out _, 156, 36)
                .onClick.AddListener(() => voxLog.SetActive(false));
            voxLog.SetActive(false);
        }

        private void ToggleVoxLog()
        {
            if (voxLog.activeSelf) { voxLog.SetActive(false); return; }
            UpdateVoxLog();
            voxLog.SetActive(true);
            voxLog.transform.SetAsLastSibling();
        }

        private void UpdateVoxLog()
        {
            if (recentVox.Count == 0) { voxLogContent.text = "No transmissions received in this session."; return; }
            var transcript = new StringBuilder();
            // Keep the latest five messages readable without hiding a scroll area.
            for (var i = recentVox.Count - 1; i >= 0; i--)
            {
                var message = recentVox[i];
                transcript.Append(Speaker(message)).Append(" / ").AppendLine(message.Title);
                transcript.AppendLine(message.Body).AppendLine();
            }
            voxLogContent.text = transcript.ToString();
        }

        private bool VoxSessionActive => GameSettings.HasActiveGame && bootstrap != null && !bootstrap.ChoosingLanding && hudPanel != null && hudPanel.activeSelf;

        public void ReportVoxResearch(string projectName)
        {
            if (EnsureVoxSession()) voxAlerts.PushResearchCompleted(projectName);
        }

        public void ReportVoxSurvey(string finding)
        {
            if (EnsureVoxSession()) voxAlerts.PushSurveyFinding(finding);
        }

        private void ResetVox()
        {
            voxSessionStarted = false;
            voxAlerts.Reset();
            pendingVox.Clear();
            recentVox.Clear();
            voxDisplaySeconds = 0;
            HideVoxCard();
            if (voxLog != null) voxLog.SetActive(false);
        }

        private bool EnsureVoxSession()
        {
            if (!VoxSessionActive) return false;
            if (voxSessionStarted) return true;
            voxSessionStarted = true;
            var now = clock.Snapshot.simulationSeconds;
            voxAlerts.Reset(now);
            nextVoxSample = now + 5;
            return true;
        }

        private void HideVoxCard()
        {
            voxCard?.Hide();
            voxCardVisible = false;
        }

        private void RefreshVox()
        {
            if (!EnsureVoxSession())
            {
                HideVoxCard();
                return;
            }
            var now = clock.Snapshot.simulationSeconds;
            if (now >= nextVoxSample)
            {
                nextVoxSample = now + 5;
                ReceiveVox(voxAlerts.Sample(now, ReadVoxSignals()));
            }

            else if (voxAlerts.HasPendingEvents) ReceiveVox(voxAlerts.DrainEvents());

            if (HasOpenEvent || clock.DecisionPending || clock.MiningDecisionPending ||
                newGamePanel.activeSelf || voxLog.activeSelf)
            {
                HideVoxCard();
                return;
            }

            voxDisplaySeconds = Mathf.Max(0, voxDisplaySeconds - Time.unscaledDeltaTime);
            if (voxDisplaySeconds == 0)
            {
                if (pendingVox.Count == 0) { HideVoxCard(); return; }
                displayedVox = pendingVox.Dequeue();
                voxDisplaySeconds = 8;
                voxCardVisible = false;
            }

            var canvasScaler = hudPanel.GetComponentInParent<CanvasScaler>();
            var canvasWidth = canvasScaler == null ? Screen.width : Screen.width / Mathf.Max(canvasScaler.scaleFactor, 0.001f);
            SetVoxViewportLayout(canvasWidth);
            if (voxCardVisible) return;
            voxCardVisible = true;
            var portrait = displayedVox.Officer == VoxOfficer.Captain ? captainPortrait : priestPortrait;
            var heading = Speaker(displayedVox) + (displayedVox.Urgent ? "  /  URGENT" : "  /  " + displayedVox.Title);
            voxCard.Show(portrait, heading,
                displayedVox.Body, displayedVox.Urgent ? CommandUi.Critical
                    : displayedVox.Officer == VoxOfficer.Captain ? CommandUi.Brass : CommandUi.Signal);
        }

        internal void SetVoxViewportLayout(float canvasWidth)
        {
            if (voxCard == null) return;
            voxCard.SetTopOffset(foundingTracker != null && foundingTracker.activeSelf ? -218 : -110);
            voxCard.SetShipInspectorLayout(shipPanel != null && shipPanel.activeSelf, canvasWidth);
        }

        private void ReceiveVox(List<VoxAlert> messages)
        {
            foreach (var message in messages)
            {
                if (pendingVox.Count == VoxQueueLimit) pendingVox.Dequeue();
                pendingVox.Enqueue(message);
                if (recentVox.Count == VoxHistoryLimit) recentVox.RemoveAt(0);
                recentVox.Add(message);
            }
            if (messages.Count > 0 && voxLog.activeSelf) UpdateVoxLog();
        }

        private VoxAlertSnapshot ReadVoxSignals()
        {
            var report = resourceSystem.CreateSnapshot();
            var population = report.Population;
            var months = (float)ColonyCalendar.SecondsPerMonth;
            var housing = settlement.ReportedHousingCapacity;
            string manufacturing = null;
            var industrial = Industry;
            var blocked = industrial != null && industrial.TryGetVoxManufacturingBottleneck(out manufacturing);
            var needSupplies = false;
            if (industrial != null && !industrial.HasInfiniteConstructionSupplies)
            {
                var supply = industrial.ConstructionSupplyTotals().basic;
                industrial.FillRates(null, materialConsumed);
                var monthlySpend = materialConsumed[(int)MaterialKind.Components] * months;
                needSupplies = monthlySpend > .001f && supply < monthlySpend;
            }
            return new VoxAlertSnapshot
            {
                HousingPressure = population > 0 && population >= housing * .9f,
                FoodShortage = population > 0 && report.Food < FarmRules.MonthlyDemand(population) * 2f,
                WaterShortage = population > 0 && report.Water < WaterRules.Demand(population) * months,
                ManufacturingBottleneck = blocked,
                ManufacturingName = manufacturing,
                ConstructionSupplyNeed = needSupplies,
                ConstructionSupplyName = "components"
            };
        }

        private static string Speaker(VoxAlert alert)
            => alert.Officer == VoxOfficer.Captain ? "CAPTAIN" : "MECHANICUM PRIEST";
    }
}
