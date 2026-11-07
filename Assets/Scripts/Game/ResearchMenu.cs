using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Sim;

namespace WH30K.UI
{
    public partial class NewGameMenu
    {
        private GameObject researchPanel;
        private Button researchOpenButton, researchProjectButton, researchBuildButton, researchPreviousButton, researchNextButton;
        private readonly Button[] researchBranches = new Button[3];
        private Text researchBalanceText, researchNameText, researchDescriptionText, researchProgressText;
        private ResearchBranch researchBranch;
        private int researchProjectIndex;
        private ResearchSystem Research => GetComponent<ResearchSystem>();

        private void BuildResearchPanel(Transform parent)
        {
            var logPanel = hudPanel.transform.Find("OperationsLog");
            if (logPanel != null)
            {
                researchOpenButton = Button("OpenResearch", logPanel, new Vector2(16, -189), "RESEARCH / TECH DATA", out _, 200, 23);
                researchOpenButton.onClick.AddListener(OpenResearchPanel);
            }
            researchPanel = CommandUi.Screen("ResearchPanel", parent, 0.72f);
            var card = Panel("ResearchCard", researchPanel.transform, new Vector2(690, 484), Vector2.zero, TextAnchor.MiddleCenter);
            Accent(card, CommandUi.Signal);
            Label("ResearchTag", card.transform, "MECHANICUM COGNITORIUM  /  SELECT A PROJECT", 11, new Vector2(28,-18), 610, 22, Gold);
            Button("CloseResearch", card.transform, new Vector2(636,-12), "X", out _, 28, 26).onClick.AddListener(CloseResearchPanel);
            researchBalanceText=Label("ResearchBalance",card.transform,"",11,new Vector2(28,-52),630,36,Muted);
            var branches=new[]{ResearchBranch.Survival,ResearchBranch.Industry,ResearchBranch.Exploration};
            for(var i=0;i<branches.Length;i++)
            {
                var branch=branches[i];
                researchBranches[i]=Button("ResearchBranch"+branch,card.transform,new Vector2(28+i*210,-80),ResearchRules.BranchName(branch),out _,198,32);
                researchBranches[i].onClick.AddListener(()=>SelectResearchBranch(branch));
            }
            researchNameText=Label("ResearchProjectName",card.transform,"",22,new Vector2(28,-130),630,36,Ivory);
            researchDescriptionText=Label("ResearchProjectDescription",card.transform,"",14,new Vector2(28,-179),630,118,Ivory);
            researchDescriptionText.verticalOverflow=VerticalWrapMode.Overflow;
            researchProgressText=Label("ResearchProjectStatus",card.transform,"",12,new Vector2(28,-310),630,56,Gold);
            researchPreviousButton=Button("ResearchPrevious",card.transform,new Vector2(28,-377),"< PREVIOUS",out _,145,34);
            researchPreviousButton.onClick.AddListener(()=>CycleResearchProject(-1));
            researchNextButton=Button("ResearchNext",card.transform,new Vector2(185,-377),"NEXT >",out _,145,34);
            researchNextButton.onClick.AddListener(()=>CycleResearchProject(1));
            researchProjectButton=Button("ResearchStart",card.transform,new Vector2(346,-377),"COMMIT PROJECT",out _,314,34);
            researchProjectButton.onClick.AddListener(CommitResearchProject);
            researchBuildButton=Button("BuildCognitorium",card.transform,new Vector2(28,-423),"BUILD COGNITORIUM",out _,632,32);
            researchBuildButton.onClick.AddListener(BuildCognitorium);
            researchPanel.SetActive(false);
        }

        private void OpenResearchPanel()
        {
            if(!GameSettings.HasActiveGame||researchPanel==null)return;
            researchPanel.SetActive(true);HideOutputTooltip();RefreshResearchPanel();
            if(clock!=null)clock.DecisionPending=true;
        }

        private void CloseResearchPanel()
        {
            if(researchPanel!=null)researchPanel.SetActive(false);
            if(clock!=null)clock.DecisionPending=HasOpenEvent||(newGamePanel!=null&&newGamePanel.activeSelf);
        }

        private void SelectResearchBranch(ResearchBranch branch)
        {
            researchBranch=branch;researchProjectIndex=ResearchRules.BranchIndex(branch);RefreshResearchPanel();
        }

        private void CycleResearchProject(int direction)
        {
            var first=ResearchRules.BranchIndex(researchBranch);var count=ResearchRules.BranchCount(researchBranch);
            if(count<=0)return;
            var relative=researchProjectIndex-first;
            researchProjectIndex=first+((relative+(direction<0?-1:1)+count)%count);
            RefreshResearchPanel();
        }

        private void CommitResearchProject()
        {
            var project=ResearchRules.ProjectAt(researchProjectIndex);if(project==null||Research==null)return;
            if(Research.ActiveProjectId==project.Id)Research.SelectProject(string.Empty);
            else Research.SelectProject(project.Id);
            RefreshResearchPanel();
        }

        private void BuildCognitorium()
        {
            if(buildingPlacement==null)return;
            CloseResearchPanel();SetCatalogGroup(5);selection.Select(null);
            if(buildingPlacement.SelectStructure("Cognitorium"))buildingPlacement.BeginPlacement();
        }

        private void RefreshResearchPanel()
        {
            if(researchOpenButton==null||Research==null)return;
            var completed=0;foreach(var completedProject in ResearchRules.Projects)if(Research.IsCompleted(completedProject.Id))completed++;
            var label=researchOpenButton.GetComponentInChildren<Text>();
            if(label!=null)label.text=$"RESEARCH  {completed}/{ResearchRules.ProjectCount}";
            if(researchPanel==null||!researchPanel.activeSelf)return;
            researchBalanceText.text=$"INSIGHT {Research.Insight:0.0} / {ResearchRules.MaximumInsight:0}     GENERATION {Research.CurrentRate:0.000}/s\nEXPLORATOR: {ResearchRules.ShipInsightPerSecond:0.00}/s WHILE ACTIVE     COGNITORIUM: +{ResearchRules.BaseInsightPerSecond:0.00}/s AT FULL STAFFING AND POWER";
            for(var i=0;i<researchBranches.Length;i++)Tone(researchBranches[i],(int)researchBranch==i);
            var project=ResearchRules.ProjectAt(researchProjectIndex);
            if(project==null)return;
            researchNameText.text=project.Name.ToUpperInvariant()+"  /  "+ResearchRules.BranchName(project.Branch);
            researchDescriptionText.text=project.Description+"\n\nRESEARCH EFFECT  /  "+project.Effect;
            var progress=ResearchRules.Progress(Research.State,project.Id);
            if(Research.IsCompleted(project.Id))researchProgressText.text=$"COMPLETED  /  {project.Cost:0} INSIGHT INVESTED";
            else if(!ResearchRules.IsUnlocked(Research.State,project))researchProgressText.text=$"LOCKED  /  COMPLETE {ResearchRules.Project(project.Prerequisite)?.Name} FIRST\nPROJECT COST  /  {project.Cost:0} INSIGHT";
            else
            {
                var active=Research.ActiveProjectId==project.Id;
                var remaining=project.Cost-progress;
                var time=Research.CurrentRate>0?remaining/Research.CurrentRate:float.PositiveInfinity;
                researchProgressText.text=$"{(active?"ACTIVE":"AVAILABLE")}  /  {progress:0.0} OF {project.Cost:0} INSIGHT"+
                    (active?Research.CurrentRate>0?$"  /  {time/ColonyCalendar.SecondsPerMonth:0.0} GAME MONTHS REMAINING":"  /  WAITING FOR ACTIVE VESSEL OR COGNITORIUM":"  /  PROJECT COST {project.Cost:0} INSIGHT");
            }
            var buttonText=researchProjectButton.GetComponentInChildren<Text>();
            var unlocked=ResearchRules.IsUnlocked(Research.State,project)&&!Research.IsCompleted(project.Id);
            researchProjectButton.interactable=unlocked||Research.ActiveProjectId==project.Id;
            if(buttonText!=null)buttonText.text=Research.IsCompleted(project.Id)?"PROJECT COMPLETE":Research.ActiveProjectId==project.Id?"PAUSE PROJECT":unlocked?"COMMIT PROJECT":"PREREQUISITE REQUIRED";
            researchPreviousButton.interactable=researchNextButton.interactable=ResearchRules.BranchCount(researchBranch)>1;
            researchBuildButton.interactable=true;
            var buildText=researchBuildButton.GetComponentInChildren<Text>();
            if(buildText!=null)buildText.text=HasPlacedCognitorium()?"COGNITORIUM ESTABLISHED  /  BUILD ANOTHER":"BUILD COGNITORIUM";
        }

        private bool HasPlacedCognitorium()
        {
            if(buildingPlacement==null)return false;
            foreach(var building in buildingPlacement.Buildings)if(building!=null&&building.Id=="Cognitorium")return true;
            return false;
        }
    }
}
