using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Sim;
using WH30K.Sim.Resources;

namespace WH30K.UI
{
    public partial class NewGameMenu
    {
        private const string DepositPinPreference = "WH30K.ResourceDeposits.Pinned";
        private GameObject resourceDepositsPanel;
        private RectTransform resourceDepositContent;
        private Text resourceDepositPinLabel, resourceDepositEmpty;
        private bool resourceDepositsPinned;
        private readonly List<SurfaceDeposit> extractionDeposits = new List<SurfaceDeposit>();
        private readonly List<Button> resourceDepositRows = new List<Button>();
        private readonly List<Text[]> resourceDepositCells = new List<Text[]>();

        private void BuildResourceDepositsPane()
        {
            resourceDepositsPinned = PlayerPrefs.GetInt(DepositPinPreference, 0) != 0;
            var open = hudPanel.transform.Find("OperationsLog/VisitDeposit").GetComponent<Button>();
            open.name = "OpenResourceDeposits";
            open.GetComponentInChildren<Text>().text = "RESOURCE DEPOSITS";
            open.onClick.RemoveAllListeners();
            open.onClick.AddListener(() =>
            {
                resourceDepositsPanel.SetActive(!resourceDepositsPanel.activeSelf);
                PositionResourceDepositsPane(); RefreshResourceDepositsPane();
            });
            resourceDepositsPanel = Panel("ResourceDepositsPane", hudPanel.transform, new Vector2(660, 420), Vector2.zero, TextAnchor.MiddleCenter);
            Accent(resourceDepositsPanel, Gold);
            Label("ResourceDepositsTitle", resourceDepositsPanel.transform, "EXTRACTION / RESOURCE DEPOSITS", 17, new Vector2(16, -14), 440, 26, Ivory);
            Button("PinResourceDeposits", resourceDepositsPanel.transform, new Vector2(468, -12), "", out resourceDepositPinLabel, 138, 28)
                .onClick.AddListener(() =>
                {
                    resourceDepositsPinned = !resourceDepositsPinned;
                    PlayerPrefs.SetInt(DepositPinPreference, resourceDepositsPinned ? 1 : 0); PlayerPrefs.Save();
                    PositionResourceDepositsPane();
                });
            Button("CloseResourceDeposits", resourceDepositsPanel.transform, new Vector2(618, -12), "X", out _, 26, 28)
                .onClick.AddListener(() => resourceDepositsPanel.SetActive(false));
            Label("ResourceDepositsNote", resourceDepositsPanel.transform,
                "Life = remaining reserve / actual extraction per month (last 30 simulated seconds).\nIncludes assigned sources while idle. Click a row to visit. Time continues while open.",
                12, new Vector2(16, -49), 628, 44, Muted);
            var headings = new[] { "DEPOSIT", "START", "REMAINING", "USE / MONTH", "LIFE / MONTHS" };
            var positions = new[] { 16f, 222f, 314f, 416f, 528f };
            var widths = new[] { 200f, 86f, 96f, 106f, 116f };
            for (var i = 0; i < headings.Length; i++)
                Label("DepositHeading" + i, resourceDepositsPanel.transform, headings[i], 10, new Vector2(positions[i], -101), widths[i], 20, Gold);
            var viewport = new GameObject("DepositViewport", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            viewport.transform.SetParent(resourceDepositsPanel.transform, false);
            var rect = viewport.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(12, -126); rect.sizeDelta = new Vector2(636, 254);
            viewport.GetComponent<Image>().color = Surface;
            viewport.GetComponent<Mask>().showMaskGraphic = true;
            resourceDepositContent = new GameObject("DepositRows", typeof(RectTransform)).GetComponent<RectTransform>();
            resourceDepositContent.SetParent(rect, false);
            resourceDepositContent.anchorMin = resourceDepositContent.anchorMax = resourceDepositContent.pivot = new Vector2(0, 1);
            resourceDepositContent.sizeDelta = new Vector2(636, 0);
            var scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = rect; scroll.content = resourceDepositContent; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 36;
            resourceDepositEmpty = Label("NoExtractionDeposits", resourceDepositsPanel.transform,
                "No deposits assigned. Build a mine, quarry, harvester or fuel extractor near a surveyed source.",
                14, new Vector2(26, -148), 600, 70, Muted);
            resourceDepositEmpty.raycastTarget = false;
            Label("ResourceDepositsFooter", resourceDepositsPanel.transform,
                "Scroll for more sources. START is in reserve units; older unsaved start volumes show UNKNOWN.",
                11, new Vector2(16, -390), 628, 22, Muted);
            PositionResourceDepositsPane();
            resourceDepositsPanel.SetActive(resourceDepositsPinned);
        }

        private void PositionResourceDepositsPane()
        {
            var rect = resourceDepositsPanel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = resourceDepositsPinned ? new Vector2(0, 1) : new Vector2(.5f, .5f);
            rect.anchoredPosition = resourceDepositsPinned ? new Vector2(280, -116) : Vector2.zero;
            resourceDepositPinLabel.text = resourceDepositsPinned ? "UNPIN FROM HUD" : "PIN TO HUD";
        }

        private void RefreshResourceDepositsPane()
        {
            if (resourceDepositsPanel == null || !resourceDepositsPanel.activeInHierarchy || Industry == null) return;
            Industry.FillExtractionDeposits(extractionDeposits);
            resourceDepositEmpty.gameObject.SetActive(extractionDeposits.Count == 0);
            for (var i = 0; i < extractionDeposits.Count; i++)
            {
                if (i == resourceDepositRows.Count)
                {
                    var index = i;
                    var row = Button("ExtractionDeposit" + i, resourceDepositContent, new Vector2(0, -i * 54), "", out _, 636, 50);
                    row.onClick.AddListener(() =>
                    {
                        if (index >= extractionDeposits.Count || !GameSettings.HasActiveGame) return;
                        var deposit = extractionDeposits[index];
                        Industry.SetOverlay((int)deposit.Kind + 2); Orbit?.FocusLocation(deposit.Direction);
                        if (!resourceDepositsPinned) resourceDepositsPanel.SetActive(false);
                    });
                    resourceDepositRows.Add(row);
                    var x = new[] { 4f, 210f, 302f, 404f, 516f };
                    var widths = new[] { 200f, 86f, 96f, 106f, 116f };
                    var cells = new Text[5];
                    for (var c = 0; c < cells.Length; c++)
                    {
                        cells[c] = Label("DepositCell" + c, row.transform, "", 12, new Vector2(x[c], -8), widths[c], 38, Ivory);
                        cells[c].raycastTarget = false;
                    }
                    resourceDepositCells.Add(cells);
                }
                resourceDepositRows[i].gameObject.SetActive(true);
                var source = extractionDeposits[i]; var values = resourceDepositCells[i];
                var rate = source.Usage.MonthlyRate(Industry.DepositUsageSeconds);
                values[0].text = $"{IndustryRules.Name(source.Kind)}\nSurvey #{source.Id:000}";
                values[1].text = source.InitialVolume > 0 ? source.InitialVolume.ToString("N1") : "UNKNOWN";
                values[2].text = source.Remaining.ToString("N1");
                values[3].text = rate.ToString("N1");
                values[4].text = source.Remaining <= .0001f ? "EXHAUSTED" : rate <= 0 ? "No recent\nextraction" : $"{DepositUsage.LifeMonths(source.Remaining, rate):N1} months";
            }
            for (var i = extractionDeposits.Count; i < resourceDepositRows.Count; i++) resourceDepositRows[i].gameObject.SetActive(false);
            resourceDepositContent.sizeDelta = new Vector2(636, extractionDeposits.Count * 54);
        }
    }
}
