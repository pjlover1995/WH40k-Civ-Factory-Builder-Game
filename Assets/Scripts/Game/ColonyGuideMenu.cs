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
        private GameObject foundingTracker;
        private Text foundingTrackerTitle, foundingTrackerDetail, foundingToggleLabel;
        private string guideRecoveryNotice;
        private readonly HashSet<string> guideBuiltThisSession = new HashSet<string>();

        private void BuildColonyGuide(Transform parent)
        {
            foundingTracker=Panel("FoundingObjectivesTracker",hudPanel.transform,new Vector2(470,92),new Vector2(0,-116),TextAnchor.UpperCenter);
            var trackerRect=foundingTracker.GetComponent<RectTransform>();
            trackerRect.anchorMin=trackerRect.anchorMax=trackerRect.pivot=new Vector2(.5f,1);
            Accent(foundingTracker,Gold);
            foundingTrackerTitle=Label("FoundingObjectiveTitle",foundingTracker.transform,"",12,new Vector2(12,-9),444,22,Gold);
            foundingTrackerDetail=Label("FoundingObjectiveDetail",foundingTracker.transform,"",11,new Vector2(12,-33),444,24,Ivory);
            Button("ReviewFoundingObjectives",foundingTracker.transform,new Vector2(12,-62),"OBJECTIVES / DETAILS",out _,266,23).onClick.AddListener(()=>{guidePage=0;ShowGuide();});
            Button("HideFoundingObjectives",foundingTracker.transform,new Vector2(290,-62),"HIDE",out _,166,23).onClick.AddListener(()=>{Industry?.SetFoundingObjectives(false);RefreshFoundingObjectives();});
            foundingTracker.SetActive(false);
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
            Button("ToggleFoundingObjectives",card.transform,new Vector2(422,-44),"TRACK OBJECTIVES",out foundingToggleLabel,218,25)
                .onClick.AddListener(()=>{if(Industry!=null)Industry.SetFoundingObjectives(!Industry.Founding.enabled);RefreshFoundingObjectives();});
            guideTitleText = Label("GuideTitle", card.transform, "", 24, new Vector2(28, -76), 640, 38, Ivory);
            guideBodyText = Label("GuideBody", card.transform, "", 14, new Vector2(28, -126), 640, 220, Ivory);
            guideBodyText.verticalOverflow = VerticalWrapMode.Truncate;
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
            guideBodyText.text = "The Explorator carries a reactor, food and construction cargo, but water and supplies are finite. Optional live objectives recognise working water supply, delivered farm food and manufactured components, then a full month of balanced supplies. Complete the route to locate an orbital cargo canister.\n\nBEGIN enables a small objective tracker during play. Browse all 18 facilities and select buildings from their reference pages. Hide the tracker or close the guide at any time; reopen COLONY GUIDE from the command bar.";
            guideStatusText.text = "The guide pauses the simulation while open. It does not spend resources or place buildings.";
            guidePanel.SetActive(true);
            HideOutputTooltip();
            if (clock != null) clock.DecisionPending = true;
        }

        private void ShowGuide()
        {
            if (!GameSettings.HasActiveGame || guidePanel == null) return;
            guideRecoveryNotice=null;
            guideOfferControls.SetActive(false);
            guideWalkthroughControls.SetActive(true);
            SetGuidePage(Mathf.Clamp(guidePage, 0, ColonyGuideContent.PageCount - 1));
            guidePanel.SetActive(true);
            HideOutputTooltip();
            if (clock != null) clock.DecisionPending = true;
        }

        private void BeginGuide()
        {
            Industry?.SetFoundingObjectives(true);
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
            guideStepText.text = guidePage == 0 || guidePage == finalPage ? "LIVE FOUNDING OBJECTIVES" :
                $"BUILDING REFERENCE  /  {guidePage} OF 18";
            var entry = ColonyGuideContent.EntryAtPage(guidePage);
            if (guidePage == 0)
            {
                SetFoundingGuideSummary();
            }
            else if (guidePage == finalPage)
            {
                SetFoundingGuideSummary();
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
            if(guidePage==0 || guidePage==ColonyGuideContent.PageCount-1)
            {
                var recovered=Industry!=null?Industry.RecoverFoundingCanister():0;
                guideRecoveryNotice=recovered>0?$"Recovered {recovered:0.##} components from the orbital canister.":"Storage is full. Contents remain in the canister; make room and recover again.";
                SetGuidePage(guidePage);
                return;
            }
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

        public void ResetGuideForNewExpedition() {guideBuiltThisSession.Clear();guideRecoveryNotice=null;}

        private void SetFoundingGuideSummary()
        {
            var p=Industry!=null?Industry.Founding:default;
            guideTitleText.text=p.completed?"Colony established":"Establish your colony";
            string Mark(bool done)=>done?"[DONE]":"[  ]";
            guideBodyText.fontSize=14;
            guideBodyText.text=$"{Mark(p.waterEstablished)} Produce water   {Mark(p.foodDelivered)} Deliver farm food\n{Mark(p.componentsMade)} Make components   {Mark(p.completed)} Balance supplies for a month\n\n{FoundingObjectives.Title(p)}\n{FoundingObjectives.Detail(p)}";
            guideStatusText.text=guideRecoveryNotice??"Progress is saved. Close the guide to let simulation run. NEXT browses building instructions.";
            guideBuildButton.gameObject.SetActive(p.completed && p.rewardRecovered<FoundingObjectives.Reward);
            guideBuildButton.GetComponentInChildren<Text>().text="RECOVER CANISTER CONTENTS";
        }

        private void RefreshFoundingObjectives()
        {
            if(foundingTracker==null)return;
            var p=Industry!=null?Industry.Founding:default;
            foundingTracker.SetActive(GameSettings.HasActiveGame && bootstrap!=null && !bootstrap.ChoosingLanding && p.enabled);
            foundingTrackerTitle.text=FoundingObjectives.Title(p);
            foundingTrackerDetail.text=p.completed?(p.rewardRecovered<FoundingObjectives.Reward?"A balanced month achieved. Review objectives for the canister reward.":"Canister recovered. Keep monitoring supplies as your colony grows."):
                FoundingObjectives.Stage(p)==3?$"Month observation: {p.seconds:0}/{FoundingObjectives.Duration:0}s. Open details for supply balances.":"Requires actual production/delivery. Open details for the next step.";
            if(foundingToggleLabel!=null)foundingToggleLabel.text=p.enabled?"HIDE OBJECTIVE TRACKER":"TRACK OBJECTIVES";
            if(guidePanel!=null && guidePanel.activeSelf && !guideOfferControls.activeSelf && (guidePage==0 || guidePage==ColonyGuideContent.PageCount-1))SetGuidePage(guidePage);
        }
    }
}
