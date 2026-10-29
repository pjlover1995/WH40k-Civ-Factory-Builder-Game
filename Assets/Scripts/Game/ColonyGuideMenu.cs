using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay.Construction;
using WH30K.Sim;

namespace WH30K.UI
{
    public partial class NewGameMenu
    {
        private GameObject guidePanel, guideOfferControls, guideWalkthroughControls;
        private Text guideStepText, guideTitleText, guideBodyText, guideStatusText;
        private Button guideBuildButton, guidePreviousButton, guideNextButton;
        private int guidePage;
        private readonly HashSet<string> guideBuiltThisSession = new HashSet<string>();

        private void BuildColonyGuide(Transform parent)
        {
            var commandHeader = hudPanel.transform.Find("CommandHeader");
            if (commandHeader != null)
            {
                var guideButton=Button("OpenColonyGuide", commandHeader, new Vector2(18, -63), "COLONY GUIDE", out var guideLabel, 180, 20);
                guideLabel.fontSize=11;guideButton.onClick.AddListener(ShowGuide);
            }

            guidePanel = new GameObject("ColonyGuide", typeof(RectTransform), typeof(Image));
            guidePanel.transform.SetParent(parent, false);
            var full = guidePanel.GetComponent<RectTransform>();
            full.anchorMin = Vector2.zero; full.anchorMax = Vector2.one; full.offsetMin = full.offsetMax = Vector2.zero;
            guidePanel.GetComponent<Image>().color = new Color(0.005f, 0.008f, 0.012f, 0.72f);
            var card = Panel("GuideCard", guidePanel.transform, new Vector2(700, 462), Vector2.zero, TextAnchor.MiddleCenter);
            Accent(card, Gold);
            Label("GuideTag", card.transform, "EXPEDITION AUTHORITY  /  COLONY ESTABLISHMENT", 11, new Vector2(28, -18), 600, 22, Gold);
            Button("CloseColonyGuide", card.transform, new Vector2(644, -12), "X", out _, 30, 28).onClick.AddListener(CloseGuide);
            guideStepText = Label("GuideProgress", card.transform, "", 10, new Vector2(28, -48), 620, 20, Muted);
            guideTitleText = Label("GuideTitle", card.transform, "", 24, new Vector2(28, -76), 640, 38, Ivory);
            guideBodyText = Label("GuideBody", card.transform, "", 15, new Vector2(28, -126), 640, 240, Ivory);
            guideBodyText.verticalOverflow = VerticalWrapMode.Overflow;
            guideStatusText = Label("GuideStatus", card.transform, "", 11, new Vector2(28, -354), 640, 24, Gold);

            guideOfferControls = new GameObject("GuideOfferControls", typeof(RectTransform));
            guideOfferControls.transform.SetParent(card.transform, false);
            guideOfferControls.GetComponent<RectTransform>().anchorMin = guideOfferControls.GetComponent<RectTransform>().anchorMax = guideOfferControls.GetComponent<RectTransform>().pivot = new Vector2(0, 1);
            guideOfferControls.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -390);
            Button("BeginColonyGuide", guideOfferControls.transform, new Vector2(28, -6), "BEGIN GUIDED ESTABLISHMENT", out _, 314, 42)
                .onClick.AddListener(BeginGuide);
            Button("DeclineColonyGuide", guideOfferControls.transform, new Vector2(354, -6), "NOT NOW", out _, 260, 42)
                .onClick.AddListener(CloseGuide);

            guideWalkthroughControls = new GameObject("GuideWalkthroughControls", typeof(RectTransform));
            guideWalkthroughControls.transform.SetParent(card.transform, false);
            var controlsRect = guideWalkthroughControls.GetComponent<RectTransform>();
            controlsRect.anchorMin = controlsRect.anchorMax = controlsRect.pivot = new Vector2(0, 1);
            controlsRect.anchoredPosition = new Vector2(0, -390);
            guidePreviousButton = Button("GuidePrevious", guideWalkthroughControls.transform, new Vector2(28, -6), "< PREVIOUS", out _, 142, 40);
            guidePreviousButton.onClick.AddListener(() => SetGuidePage(guidePage - 1));
            guideNextButton = Button("GuideNext", guideWalkthroughControls.transform, new Vector2(184, -6), "NEXT >", out _, 142, 40);
            guideNextButton.onClick.AddListener(() => SetGuidePage(guidePage + 1));
            guideBuildButton = Button("GuideBuild", guideWalkthroughControls.transform, new Vector2(354, -6), "BUILD THIS", out _, 286, 40);
            guideBuildButton.onClick.AddListener(BuildGuideStructure);
            guidePanel.SetActive(false);
        }

        public void ShowGuideOffer()
        {
            if (guidePanel == null) return;
            guidePage = 0;
            guideOfferControls.SetActive(true);
            guideWalkthroughControls.SetActive(false);
            guideStepText.text = "OPTIONAL FIELD INSTRUCTION";
            guideTitleText.text = "Would you like a guided colony?";
            guideBodyText.text = "The Explorator carries a reactor, food and construction cargo, but water and supplies are finite. This walkthrough explains the founding sequence, shows how all 18 facilities work, and lets you select a building directly from each page. You can close it at any time and reopen COLONY GUIDE from the command bar.";
            guideStatusText.text = "The guide pauses the simulation while open. It does not spend resources or place buildings.";
            guidePanel.SetActive(true);
            HideOutputTooltip();
            if (clock != null) clock.DecisionPending = true;
        }

        private void ShowGuide()
        {
            if (!GameSettings.HasActiveGame || guidePanel == null) return;
            guideOfferControls.SetActive(false);
            guideWalkthroughControls.SetActive(true);
            SetGuidePage(Mathf.Clamp(guidePage, 0, ColonyGuideContent.PageCount - 1));
            guidePanel.SetActive(true);
            HideOutputTooltip();
            if (clock != null) clock.DecisionPending = true;
        }

        private void BeginGuide()
        {
            guideOfferControls.SetActive(false);
            guideWalkthroughControls.SetActive(true);
            SetGuidePage(0);
        }

        private void CloseGuide()
        {
            if (guidePanel != null) guidePanel.SetActive(false);
            if (clock != null) clock.DecisionPending = HasOpenEvent || (newGamePanel != null && newGamePanel.activeSelf);
        }

        private void SetGuidePage(int page)
        {
            guidePage = Mathf.Clamp(page, 0, ColonyGuideContent.PageCount - 1);
            var finalPage = ColonyGuideContent.PageCount - 1;
            guideStepText.text = guidePage == 0 ? "FOUNDING ROUTE  /  0 OF 18 FACILITIES" :
                guidePage == finalPage ? "FOUNDING ROUTE  /  COMPLETE" :
                $"BUILDING REFERENCE  /  {guidePage} OF 18";
            var entry = ColonyGuideContent.EntryAtPage(guidePage);
            if (guidePage == 0)
            {
                guideTitleText.text = "Begin with the ship";
                guideBodyText.text = "Your landed Explorator is the colony's first home and power station: 6,000 crew, 10,000 berths, an 18 MW reactor and 120,000 food. Keep its reactor active while demand fits the grid. Food and water are shared colony reserves, and both continue to run down during normal play.\n\nFounding order: one connected groundwater pump, then one connected farm (6,000 food/month at full operation). Ship components and reclaimed crew waste feed the farm. Next connect an iron mine + stone quarry + biomass harvester to a Manufactorum on its default components recipe. This replaces construction supplies without smelting. Place linked facilities within logistics range; use their inspector to check staffing and power. Add generation before power demand exceeds the reactor; use housing, depots, outposts and scrubbers as your colony grows.";
                guideStatusText.text = "Use NEXT to begin the route, or browse every player-buildable facility in order.";
                guideBuildButton.gameObject.SetActive(false);
            }
            else if (guidePage == finalPage)
            {
                guideTitleText.text = "A functioning colony";
                guideBodyText.text = "Watch FOOD, WATER and BUILD SUPPLIES in the top banner. Growth needs housing, healthy conditions and reserves: full growth requires at least 3 months food and 1 month water. Health is fixed at 100% for now. For material independence: iron mine + stone quarry + biomass harvester -> Manufactorum -> construction supplies. Smelter/Works is an optional longer route. Add fuel extraction and generation before the ship reactor becomes a bottleneck.\n\nUse the planetary vitals, material inventory, power, staffing and cargo readouts to find the next constraint. Survey outposts, scrubbers, desalination, refining and depots address specific needs as the settlement expands.";
                guideStatusText.text = "You can revisit any page with PREVIOUS / NEXT or select BUILD THIS to start placement.";
                guideBuildButton.gameObject.SetActive(false);
            }
            else
            {
                var label = entry.Id == "GroundwaterPump" || entry.Id == "Farm" ? "FOUNDING PRIORITY" :
                    entry.Essential ? "SELF-SUPPLY LINK" : "EXPANSION OPTION";
                guideTitleText.text = entry.Name + "  /  " + label;
                guideBodyText.text = entry.HowItWorks + "\n\nROLE IN THE COLONY\n" + entry.ColonyRole + "\n\nCONSTRUCTION PAGE\n" + ColonyGuideContent.CategoryName(entry.CatalogGroup);
                var alreadyBuilt = guideBuiltThisSession.Contains(entry.Id) || HasPlacedStructure(entry.Id);
                guideStatusText.text = alreadyBuilt ? "STATUS  /  A " + entry.Name.ToUpperInvariant() + " IS PRESENT" :
                    label == "FOUNDING PRIORITY" ? "STATUS  /  BUILD EARLY; SHIP STORES ARE FINITE" :
                    entry.Essential ? "STATUS  /  NEEDED TO REPLACE FINITE SHIP CARGO OVER TIME" : "STATUS  /  BUILD WHEN THIS NEED ARISES";
                guideBuildButton.gameObject.SetActive(true);
                var buildLabel = guideBuildButton.GetComponentInChildren<Text>();
                if (buildLabel != null) buildLabel.text = alreadyBuilt ? "PLACE ANOTHER" : "SELECT & PLACE THIS BUILDING";
            }
            guidePreviousButton.interactable = guidePage > 0;
            guideNextButton.interactable = guidePage < finalPage;
        }

        private bool HasPlacedStructure(string id)
        {
            if (buildingPlacement == null) return false;
            foreach (var building in buildingPlacement.Buildings)
                if (building != null && building.Id == id) return true;
            return false;
        }

        private void BuildGuideStructure()
        {
            var entry = ColonyGuideContent.EntryAtPage(guidePage);
            if (entry == null || buildingPlacement == null) return;
            SetCatalogGroup(entry.CatalogGroup);
            selection.Select(null);
            if (buildingPlacement.SelectStructure(entry.Id))
            {
                CloseGuide();
                buildingPlacement.BeginPlacement();
            }
        }

        public void RecordGuideConstruction(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            guideBuiltThisSession.Add(id);
            if (guidePanel != null && guidePanel.activeSelf && !guideOfferControls.activeSelf) SetGuidePage(guidePage);
        }

        public void ResetGuideForNewExpedition() => guideBuiltThisSession.Clear();
    }
}
