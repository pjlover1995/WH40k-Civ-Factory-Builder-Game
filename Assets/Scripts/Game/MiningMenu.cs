using UnityEngine;
using UnityEngine.UI;
using WH30K.Gameplay.Construction;
using WH30K.Sim;

namespace WH30K.UI
{
    public partial class NewGameMenu
    {
        private GameObject miningPanel;
        private Text miningTitle, miningBody;
        private Button excavationButton, miningDemolish, miningLimit, miningClose, miningVisit, miningViewConflict;
        private RectTransform miningCard;
        private Button miningReturn;
        private bool inspectingConflict;
        private readonly Button[] miningSectors = new Button[8];
        private ManagedBuilding plannedMine;
        private bool miningIsDecision;
        private bool MiningPanelOpen => inspectingConflict || miningPanel != null && miningPanel.activeSelf;
        public bool CanPresentMiningDecision => !MiningPanelOpen && !eventPanel.activeSelf && !newGamePanel.activeSelf;
        public bool MiningDecisionVisible => MiningPanelOpen && miningIsDecision;
        private void BuildMiningPanel(Transform parent)
        {
            miningReturn=Button("ReturnToMiningDecision",hudPanel.transform,Vector2.zero,"RESOLVE MINE EXPANSION  /  TIME PAUSED",out _,360,40);
            var returnRect=miningReturn.GetComponent<RectTransform>();returnRect.anchorMin=returnRect.anchorMax=returnRect.pivot=new Vector2(.5f,1);returnRect.anchoredPosition=new Vector2(0,-132);
            miningReturn.GetComponentInChildren<Text>().fontSize=12;
            miningReturn.onClick.AddListener(()=>
            {
                inspectingConflict=false;miningReturn.gameObject.SetActive(false);
                if(Industry.HasMiningDecision)ShowMiningDecision(Industry.ConflictReport(),!Industry.PendingConflict.core);
                else CloseMiningPanel();
            });
            miningReturn.gameObject.SetActive(false);
            miningPanel=new GameObject("MiningPanel",typeof(RectTransform),typeof(Image));miningPanel.transform.SetParent(parent,false);
            var rect=miningPanel.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            miningPanel.GetComponent<Image>().color=new Color(.005f,.008f,.012f,.70f);
            var card=Panel("ExcavationCard",miningPanel.transform,new Vector2(720,640),Vector2.zero,TextAnchor.MiddleCenter);
            miningCard=card.GetComponent<RectTransform>();
            Label("MiningTag",card.transform,"GEOLOGICAL AUTHORITY  /  EXCAVATION CONTROL",11,new Vector2(26,-20),668,22,Gold);
            miningTitle=Label("MiningTitle",card.transform,"",25,new Vector2(26,-54),668,46,Ivory);
            miningBody=Label("MiningReport",card.transform,"",14,new Vector2(26,-112),668,236,Ivory);
            for(var s=0;s<8;s++)
            {
                var sector=s;
                miningSectors[s]=Button("Sector"+(char)('A'+s),card.transform,new Vector2(26+(s%2)*342,-362-(s/2)*43),"",out _,326,35);
                miningSectors[s].onClick.AddListener(()=>{Industry.ToggleSector(plannedMine,sector);RefreshExcavationPlan();});
                miningSectors[s].GetComponentInChildren<Text>().fontSize=12;
            }
            miningViewConflict=Button("InspectMiningConflict",card.transform,new Vector2(550,-58),"VIEW SITE",out _,144,30);
            miningViewConflict.onClick.AddListener(()=>
            {
                var conflict=Industry.PendingConflict;if(!conflict.active)return;
                var deposit=Industry.Deposits[conflict.deposit];Industry.ShowExcavationOutline(deposit);Industry.SetOverlay((int)deposit.Kind+2);Orbit?.FocusLocation(deposit.Direction);
                inspectingConflict=true;miningPanel.SetActive(false);miningReturn.gameObject.SetActive(true);
            });
            miningDemolish=Button("DemolishThreatenedFacility",card.transform,new Vector2(26,-330),"DEMOLISH FACILITY\nStored materials and linked cargo lost",out _,326,58);
            miningDemolish.GetComponentInChildren<Text>().fontSize=13;
            miningDemolish.onClick.AddListener(()=>Industry.ResolveMiningConflict(true));
            miningLimit=Button("LimitMineDirection",card.transform,new Vector2(368,-330),"LIMIT THIS DIRECTION\nKeep facility and protect its ore",out _,326,58);
            miningLimit.GetComponentInChildren<Text>().fontSize=13;
            miningLimit.onClick.AddListener(()=>Industry.ResolveMiningConflict(false));
            miningClose=Button("CloseExcavationPlan",card.transform,new Vector2(368,-566),"CLOSE PLAN",out _,326,40);
            miningClose.onClick.AddListener(CloseMiningPanel);
            miningVisit=Button("VisitExcavation",card.transform,new Vector2(26,-566),"INSPECT CUTS ON PLANET",out _,326,40);
            miningVisit.onClick.AddListener(()=>
            {
                var deposit=Industry.DepositFor(plannedMine);if(deposit!=null){Industry.SetOverlay((int)deposit.Kind+2);Orbit?.FocusLocation(deposit.Direction);Industry.ShowExcavationOutline(deposit);}
                CloseMiningPanel();
            });
            miningPanel.SetActive(false);
        }
        public void OpenExcavationPlan(ManagedBuilding building)
        {
            if(building==null || HasOpenEvent || newGamePanel.activeSelf)return;
            plannedMine=building;miningIsDecision=false;RefreshExcavationPlan();ShowMiningPanel();
        }
        private void RefreshExcavationPlan()
        {
            miningTitle.text="EXCAVATION PLAN";miningBody.text=Industry.ExcavationReport(plannedMine);
            miningCard.sizeDelta=new Vector2(720,640);miningViewConflict.gameObject.SetActive(false);
            var deposit=Industry.DepositFor(plannedMine);
            for(var s=0;s<8;s++)
            {
                miningSectors[s].gameObject.SetActive(deposit!=null);
                if(deposit==null)continue;
                var state=deposit.Excavation;var limited=(state.blocked&1<<s)!=0;
                miningSectors[s].GetComponentInChildren<Text>().text=$"{(char)('A'+s)}  /  {Mathf.Repeat(state.bearing+s*45,360):0} deg   {(limited?"LIMITED":"OPEN")}   {state.capacity[s]-state.removed[s]:0} lots";
                Tone(miningSectors[s],limited);
            }
            miningDemolish.gameObject.SetActive(false);miningLimit.gameObject.SetActive(false);
            miningClose.gameObject.SetActive(true);miningVisit.gameObject.SetActive(deposit!=null);
        }
        public void ShowMiningDecision(string report,bool canDemolish)
        {
            miningIsDecision=true;miningTitle.text="MINE EXPANSION HELD";miningBody.text=report;
            miningCard.sizeDelta=new Vector2(720,420);miningViewConflict.gameObject.SetActive(true);
            foreach(var button in miningSectors)button.gameObject.SetActive(false);
            miningDemolish.gameObject.SetActive(true);miningDemolish.interactable=canDemolish;miningLimit.gameObject.SetActive(true);
            miningClose.gameObject.SetActive(false);miningVisit.gameObject.SetActive(false);ShowMiningPanel();
        }
        private void ShowMiningPanel()
        {
            buildingPlacement.CancelPlacement();miningPanel.SetActive(true);miningPanel.transform.SetAsLastSibling();
            clock.DecisionPending=true;
        }
        public void CloseMiningPanel()
        {
            inspectingConflict=false;if(miningReturn!=null)miningReturn.gameObject.SetActive(false);
            if(miningPanel!=null)miningPanel.SetActive(false);
            if(clock!=null)clock.DecisionPending=eventPanel!=null&&eventPanel.activeSelf || newGamePanel!=null&&newGamePanel.activeSelf;
        }
    }
}
