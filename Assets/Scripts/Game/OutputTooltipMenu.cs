using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Sim;

namespace WH30K.UI
{
    public partial class NewGameMenu
    {
        private GameObject outputTooltip;
        private Text outputTooltipText;
        private RectTransform outputTooltipAnchor;
        private OutputHoverTopic outputTooltipTopic;

        private void AddOutputHover(Transform parent, string name, float x, float width, OutputHoverTopic topic = OutputHoverTopic.Colony)
        {
            var target = Panel(name + "Hover", parent, new Vector2(width, 76), new Vector2(x, -10), TextAnchor.UpperLeft, false);
            target.GetComponent<Image>().color = Color.clear;
            target.AddComponent<ColonyOutputHover>().Configure(this, topic);
        }

        private void BuildOutputTooltip()
        {
            outputTooltip = Panel("ColonyOutputTooltip", hudPanel.transform, new Vector2(500, 540), Vector2.zero, TextAnchor.UpperLeft);
            outputTooltip.GetComponent<Image>().raycastTarget = false;
            outputTooltipText = Label("OutputExplanation", outputTooltip.transform, "", 13, new Vector2(16, -14), 468, 512, Ivory);
            outputTooltipText.verticalOverflow = VerticalWrapMode.Overflow;
            outputTooltip.SetActive(false);
        }

        internal void ShowOutputTooltip(RectTransform anchor, OutputHoverTopic topic)
        {
            if (!CanShowOutputTooltip()) return;
            outputTooltipAnchor = anchor;
            outputTooltipTopic = topic;
            outputTooltip.SetActive(true);
            outputTooltip.transform.SetAsLastSibling();
            RefreshOutputTooltip();
        }

        public void HideOutputTooltip()
        {
            outputTooltipAnchor = null;
            if (outputTooltip != null) outputTooltip.SetActive(false);
        }

        private bool CanShowOutputTooltip() => outputTooltip != null && hudPanel.activeInHierarchy
            && GameSettings.HasActiveGame && settlement != null && settlement.HasActiveSimulation
            && !HasOpenEvent && (newGamePanel == null || !newGamePanel.activeSelf);

        private void RefreshOutputTooltip()
        {
            if (outputTooltipAnchor == null) return;
            if (!CanShowOutputTooltip()) { HideOutputTooltip(); return; }
            outputTooltipText.text = outputTooltipTopic switch
            {
                OutputHoverTopic.Food => Industry.SupplyOutlook.Describe(true, resourceSystem.Food, FarmRules.MonthlyDemand(settlement.Population)),
                OutputHoverTopic.Water => Industry.SupplyOutlook.Describe(false, resourceSystem.Water,
                    WaterRules.Demand(settlement.Population) * (float)ColonyCalendar.SecondsPerMonth),
                _ => settlement.OutputExplanation
            };
            var hud = (RectTransform)hudPanel.transform;
            var rect = (RectTransform)outputTooltip.transform;
            var width = Mathf.Min(500, hud.rect.width - 24);
            outputTooltipText.rectTransform.sizeDelta = new Vector2(width - 32, 1000);
            outputTooltipText.fontSize = 13;
            while (outputTooltipText.preferredHeight > hud.rect.height - 52 && outputTooltipText.fontSize > 11)
                outputTooltipText.fontSize--;
            var height = Mathf.Ceil(outputTooltipText.preferredHeight) + 32;
            rect.sizeDelta = new Vector2(width, height);
            outputTooltipText.rectTransform.sizeDelta = new Vector2(width - 32, height - 28);
            // Anchor below the hovered metric, and clamp in canvas units for CanvasScaler.
            var corner = outputTooltipAnchor.TransformPoint(new Vector3(outputTooltipAnchor.rect.xMin, outputTooltipAnchor.rect.yMin, 0));
            var local = hud.InverseTransformPoint(corner);
            var x = Mathf.Clamp(local.x - hud.rect.xMin, 12, Mathf.Max(12, hud.rect.width - width - 12));
            var y = Mathf.Clamp(hud.rect.yMax - local.y + 8, 12, Mathf.Max(12, hud.rect.height - height - 12));
            rect.anchoredPosition = new Vector2(x, -y);
        }
    }
}
