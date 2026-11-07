using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.CameraSystem;
using WH30K.Sim;
using WH30K.Sim.Environment;
using WH30K.Sim.Events;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;

namespace WH30K.UI
{
    [RequireComponent(typeof(PlanetBootstrap))]
    public partial class NewGameMenu : MonoBehaviour
    {
        private static readonly Color Gold = CommandUi.Brass;
        private static readonly Color Ivory = CommandUi.Parchment;
        private static readonly Color Muted = CommandUi.SecondaryText;
        private static readonly Color Surface = CommandUi.Iron;
        private static readonly Color ButtonBase = CommandUi.Field;
        private static readonly Color ForgeRed = CommandUi.Oxblood;
        private PlanetBootstrap bootstrap;
        private ResourceSystem resourceSystem;
        private EnvironmentState environmentState;
        private ColonyEventSystem colonyEventSystem;
        private Settlement settlement;
        private BuildingPlacementController buildingPlacement;
        private BuildingSelection selection;
        private ColonyClock clock;
        private Canvas canvas;
        private Font font;
        private GameObject newGamePanel, hudPanel, eventPanel, inspector, discoveryPanel;
        private InputField seedInput;
        private Text difficultyLabel, stockpileValue, populationValue, netValue, workforceValue, waterSupplyValue;
        private Text dateText, clockText, environmentText, eventLogText, buildButtonLabel, structureDetails;
        private Text eventTitleText, eventBodyText, choiceALabel, choiceBLabel, buildingTitle, buildingStats, focusDescription;
        private Text upgradeLabel, operationLabel, statusText, growthPlanLabel, growthModeLabel;
        private Text materialText, surveyLabel;
        private Button discoveryButton, discoveryActionButton, discoveryLocateButton;
        private Text discoveryTitleText, discoveryBodyText, discoveryActionLabel;
        private Action discoveryAction, discoveryLocateAction;
        private Button saveButton, loadButton, buildButton, upgradeButton, operationButton, pauseButton, colonyMarker, growthPlanButton, growthModeButton;
        private Button choiceA, choiceB;
        private readonly Button[] focuses = new Button[3];
        private readonly Button[] catalog = new Button[4];
        private readonly Button[] speedButtons = new Button[3];
        private readonly List<string> log = new List<string>();
        private readonly string[] catalogIds = { "Outpost", "HabBlock", "Factory", "Utility" };
        private string[] visibleIds = { "Outpost", "HabBlock", "Factory", "Utility" };
        private readonly string[][] catalogGroups = { new[] { "Outpost", "HabBlock", "Factory", "Utility" }, new[] { "Mine", "Quarry", "Harvester", "PromethiumExtractor" }, new[] { "PromethiumGenerator", "StorageDepot", "GroundwaterPump", "DesalinationPump" }, new[] { "Smelter", "Works", "Refinery", "Manufactorum" }, new[] { "Farm" }, new[] { "Cognitorium" } };
        private IndustrySystem industry;
        private IndustrySystem Industry => industry!=null?industry:industry=GetComponent<IndustrySystem>();
        private SimpleOrbitCamera orbit;
        private readonly float[] materialTotals=new float[IndustryRules.MaterialCount];
        private readonly float[] materialProduced=new float[IndustryRules.MaterialCount],materialConsumed=new float[IndustryRules.MaterialCount];
        private static readonly int[] Speeds={1,3,10};
        private static readonly string[] SurveyLabels={"OFF","ALL / ROUTES","IRON (RUST)","COPPER (GOLD)","STONE (GREY)","BIOMASS (GREEN)","CRYSTAL (BLUE)","PROMETHIUM (OCHRE)"};
        private readonly Dictionary<Button,bool> buttonTones=new Dictionary<Button,bool>();
        private readonly string[] months = { "JANUARY", "FEBRUARY", "MARCH", "APRIL", "MAY", "JUNE", "JULY", "AUGUST", "SEPTEMBER", "OCTOBER", "NOVEMBER", "DECEMBER" };
        private InputField buildingNameInput;
        private bool refreshingBuildingName;
        private ManagedBuilding selectedBuilding;
        private Action choiceAHandler, choiceBHandler;
        private Image habitabilityFill;
        private float refreshTimer;
#if UNITY_EDITOR
        private GameObject debugPanel;
#endif
        public bool HasOpenEvent => bootstrap != null && bootstrap.ChoosingLanding || eventPanel != null && eventPanel.activeSelf || discoveryPanel!=null&&discoveryPanel.activeSelf || guidePanel!=null&&guidePanel.activeSelf || researchPanel!=null&&researchPanel.activeSelf || MiningPanelOpen || FacilityPanelOpen;
        public bool HasNarrativeEvent => eventPanel != null && eventPanel.activeSelf;
        public ManagedBuilding InspectedBuilding => selectedBuilding;
        private SimpleOrbitCamera Orbit => orbit!=null?orbit:orbit=Camera.main!=null?Camera.main.GetComponent<SimpleOrbitCamera>():null;

        private void Awake()
        {
            bootstrap = GetComponent<PlanetBootstrap>();
            resourceSystem = GetComponent<ResourceSystem>(); environmentState = GetComponent<EnvironmentState>();
            colonyEventSystem = GetComponent<ColonyEventSystem>(); settlement = GetComponent<Settlement>();
            buildingPlacement = GetComponent<BuildingPlacementController>();
            clock = GetComponent<ColonyClock>() ?? gameObject.AddComponent<ColonyClock>();
            selection = GetComponent<BuildingSelection>() ?? gameObject.AddComponent<BuildingSelection>();
            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("UnityEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
            seedInput.text = UnityEngine.Random.Range(0, int.MaxValue).ToString();
            ShowNewGamePanel(true); ShowHud(false); ShowEventPanel(false);
            ConfigureSystems();
        }

        public void ConfigureSystems()
        {
            bootstrap.ConfigureMenu(this); resourceSystem.ConfigureMenu(this);
            environmentState.ConfigureMenu(this); colonyEventSystem.ConfigureMenu(this);
            settlement.ConfigureMenu(this); buildingPlacement.ConfigureMenu(this);
        }

        private void BuildUI()
        {
            var root = new GameObject("UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            hudPanel = new GameObject("HudPanel", typeof(RectTransform)); hudPanel.transform.SetParent(root.transform, false);
            var hudRect = hudPanel.GetComponent<RectTransform>();
            hudRect.anchorMin = Vector2.zero; hudRect.anchorMax = Vector2.one;
            hudRect.offsetMin = hudRect.offsetMax = Vector2.zero;
            BuildHeader(hudPanel.transform);
            BuildPlanetPanel(hudPanel.transform);
            BuildConstruction(hudPanel.transform);
            BuildInspector(hudPanel.transform);
            BuildShipHud(hudPanel.transform);
            BuildEarthworks(hudPanel.transform);
            var footer = Panel("NavigationBar", hudPanel.transform, new Vector2(0, 28), Vector2.zero, TextAnchor.LowerCenter);
            StretchWidth(footer, 0);
            statusText = Label("NavigationHelp", footer.transform,
                "WASD MOVE   MMB PAN   RMB ROTATE   WHEEL ZOOM   SHIFT FAST   F COLONY   HOME ORBIT   SPACE PAUSE   |   30 MIN / YEAR AT 1x",
                10, new Vector2(18, -7), 1220, 20, Muted);
            colonyMarker = Button("ColonyLocator", hudPanel.transform, Vector2.zero, "COLONY PRIMUS  [F]", out _, 160, 30);
            var markerRect = colonyMarker.GetComponent<RectTransform>();
            markerRect.anchorMin = markerRect.anchorMax = new Vector2(0.5f, 0.5f); markerRect.pivot = new Vector2(0.5f, 0.5f);
            colonyMarker.onClick.AddListener(() => Orbit?.FocusSettlement(settlement.CreateSnapshot().normal));
            BuildOutputTooltip();
            BuildNewGame(root.transform);
            BuildLanding(root.transform);
            BuildEvent(root.transform);
            BuildDiscovery(root.transform);
            BuildMiningPanel(root.transform);
            BuildFacilityPanel(root.transform);
            BuildColonyGuide(root.transform);
            BuildResearchPanel(root.transform);
            BuildResourceDepositsPane();
#if UNITY_EDITOR
            BuildDebug(root.transform);
#endif
        }

        private void BuildHeader(Transform parent)
        {
            var header = Panel("CommandHeader", parent, new Vector2(0, 88), new Vector2(0, -12), TextAnchor.UpperLeft);
            StretchWidth(header, 16);
            Accent(header, ForgeRed);
            Label("IdentityTag", header.transform, "ADEPTUS MECHANICUS  /  M31", 10, new Vector2(18, -13), 205, 18, Gold);
            Label("Identity", header.transform, "FORGE WORLD", 18, new Vector2(18, -34), 205, 28, Ivory);
            Metric(header.transform, "Stockpile", "FOOD / RESERVE", 218, out stockpileValue, 136);
            Metric(header.transform, "WaterSupply", "WATER / RESERVE", 358, out waterSupplyValue, 136);
            Metric(header.transform, "Net", "BUILD SUPPLIES [?]", 498, out netValue, 158);
            Metric(header.transform, "Population", "POPULATION [?]", 660, out populationValue, 130);
            Metric(header.transform, "Workforce", "WORKERS / HEALTH", 794, out workforceValue, 112);
            stockpileValue.fontSize = waterSupplyValue.fontSize = netValue.fontSize = 15;
            workforceValue.fontSize = 14;
            AddOutputHover(header.transform, "Population", 660, 130);
            AddOutputHover(header.transform, "Net", 498, 158);
            AddOutputHover(header.transform, "Workforce", 794, 112);
            var time = Panel("TimeControl", header.transform, new Vector2(326, 76), new Vector2(-8, -6), TextAnchor.UpperRight, false);
            dateText = Label("CalendarDate", time.transform, "", 16, new Vector2(8, -2), 234, 23, Ivory);
            clockText = Label("PlayedTime", time.transform, "", 10, new Vector2(8, -25), 238, 17, Muted);
            pauseButton = Button("PauseSimulation", time.transform, new Vector2(8, -45), "II", out _, 43, 25);
            pauseButton.onClick.AddListener(() => clock.TogglePause());
            var speeds = Speeds;
            for (var i = 0; i < speeds.Length; i++)
            {
                var speed = speeds[i];
                speedButtons[i] = Button("Speed" + speed, time.transform, new Vector2(58 + i * 54, -45), speed + "x", out _, 47, 25);
                speedButtons[i].onClick.AddListener(() => clock.SetSpeed(speed));
            }
            saveButton = Button("SaveButton", time.transform, new Vector2(248, -4), "SAVE", out _, 68, 27);
            saveButton.onClick.AddListener(() => bootstrap.SaveToFile());
            loadButton = Button("HudLoadButton", time.transform, new Vector2(248, -42), "LOAD", out _, 68, 27);
            loadButton.onClick.AddListener(() => bootstrap.LoadFromFile());
        }

        private void Metric(Transform header, string name, string title, float x, out Text value, float width)
        {
            Label(name + "Title", header, title, 10, new Vector2(x, -18), width, 16, Gold);
            value = Label(name + "Value", header, "0", 23, new Vector2(x, -41), width, 35, Ivory);
        }

        private void BuildPlanetPanel(Transform parent)
        {
            var panel = Panel("PlanetStatus", parent, new Vector2(248, 236), new Vector2(16, -116), TextAnchor.UpperLeft);
            Accent(panel, Gold);
            Label("PlanetStatusTitle", panel.transform, "PLANETARY VITALS", 10, new Vector2(16, -17), 150, 20, Gold);
            environmentText = Label("EnvironmentReadout", panel.transform, "", 14, new Vector2(16, -49), 202, 119, Ivory);
            var track = Panel("HabitabilityTrack", panel.transform, new Vector2(200, 4), new Vector2(16, -176), TextAnchor.UpperLeft, false);
            track.GetComponent<Image>().color = ButtonBase;
            var fill = Panel("HabitabilityFill", track.transform, new Vector2(200, 4), Vector2.zero, TextAnchor.UpperLeft, false);
            habitabilityFill = fill.GetComponent<Image>(); habitabilityFill.color = Gold;
            Button("ViewColony", panel.transform, new Vector2(16, -198), "COLONY  [F]", out _, 96, 26)
                .onClick.AddListener(() => Orbit?.FocusSettlement(settlement.CreateSnapshot().normal));
            Button("ViewPlanet", panel.transform, new Vector2(120, -198), "ORBIT", out _, 96, 26)
                .onClick.AddListener(() => Orbit?.FrameTarget());
            var journal = Panel("OperationsLog", parent, new Vector2(248, 230), new Vector2(16, -366), TextAnchor.UpperLeft);
            Accent(journal, ForgeRed);
            Label("JournalTitle", journal.transform, "LOGISTICA / MANIFEST", 11, new Vector2(16, -13), 212, 20, Gold);
            materialText = Label("MaterialInventory", journal.transform, "", 10, new Vector2(16, -37), 206, 70, Ivory);
            Button("SurveyOverlay", journal.transform, new Vector2(16, -108), "SURVEY: OFF", out surveyLabel, 200, 23)
                .onClick.AddListener(() => { if(Industry!=null) Industry.SetOverlay((Industry.Overlay+1)%8); });
            Button("VisitDeposit", journal.transform, new Vector2(16, -135), "VISIT NEAREST DEPOSIT", out _, 200, 23)
                .onClick.AddListener(VisitDeposit);
            discoveryButton=Button("DiscoveryStatus",journal.transform,new Vector2(16,-162),"SITES 0 / 0",out _,200,23);
            discoveryButton.onClick.AddListener(OpenDiscoveries);
            eventLogText = Label("EventLog", journal.transform, "", 10, new Vector2(16, -218), 200, 14, Muted);
            Button("OpenExpedition", panel.transform, new Vector2(132, -12), "NEW WORLD", out var expeditionLabel, 87, 24)
                .onClick.AddListener(() => ShowNewGamePanel(true));
            expeditionLabel.fontSize=11;
        }

        private void BuildConstruction(Transform parent)
        {
            var deck = Panel("ConstructionDeck", parent, new Vector2(1000, 166), new Vector2(0, 38), TextAnchor.LowerCenter);
            Accent(deck, ForgeRed);
            Label("ConstructionTitle", deck.transform, "PRODUCTION & CONSTRUCTION", 10, new Vector2(16, -11), 300, 18, Gold);
            Button("OpenEarthworks",deck.transform,new Vector2(710,-9),"EARTHWORKS",out var earthworkLabel,132,27)
                .onClick.AddListener(()=>Earthworks.Open(EarthworkMode.Move));
            earthworkLabel.color=EarthworksController.ColorFor(EarthworkMode.Flatten);
            Button("OpenBulldoze",deck.transform,new Vector2(850,-9),"BULLDOZE",out var bulldozeLabel,132,27)
                .onClick.AddListener(()=>Earthworks.Open(EarthworkMode.Demolish));
            bulldozeLabel.color=EarthworksController.ColorFor(EarthworkMode.Demolish);
            var categoryNames=new[]{"COLONY","EXTRACTION","UTILITIES","PROCESSING","FOOD","RESEARCH"};
            for(var g=0;g<catalogGroups.Length;g++)
            {
                var group=g;Button("CatalogGroup"+g,deck.transform,new Vector2(210+g*82,-9),categoryNames[g],out var categoryLabel,78,27)
                    .onClick.AddListener(()=>SetCatalogGroup(group));
                categoryLabel.fontSize=10;
            }
            for (var i = 0; i < 4; i++)
            {
                var index=i;
                var id = catalogIds[i];
                catalog[i] = Button("BuildCard" + id, deck.transform, new Vector2(16 + i * 171, -49),
                    ConstructionCard(id), out var label, 161, 80);
                label.fontSize = 12; label.lineSpacing = 1.1f;
                catalog[i].onClick.AddListener(() => { selection.Select(null); buildingPlacement.SelectStructure(visibleIds[index]); });
            }
            structureDetails = Label("StructureDetails", deck.transform, "", 11, new Vector2(710, -44), 272, 68, Muted);
            buildButton = Button("BuildButton", deck.transform, new Vector2(710, -122), "DEPLOY STRUCTURE", out buildButtonLabel, 272, 29);
            buildButton.onClick.AddListener(() =>
            {
                selection.Select(null);
                if (buildingPlacement.IsPlacing) buildingPlacement.CancelPlacement();
                else buildingPlacement.BeginPlacement();
            });
        }

        private void BuildInspector(Transform parent)
        {
            inspector = Panel("BuildingInspector", parent, new Vector2(310, 558), new Vector2(-16, -104), TextAnchor.UpperRight);
            Accent(inspector, Gold);
            Label("InspectorTag", inspector.transform, "MAGOS / STRUCTURE DATA", 10, new Vector2(18, -16), 260, 20, Gold);
            Button("CloseInspector", inspector.transform, new Vector2(252, -12), "X", out _, 26, 26)
                .onClick.AddListener(() => selection.Select(null));
            Label("BuildingNameCaption",inspector.transform,"NAME / EDIT",10,new Vector2(18,-42),260,16,Gold);
            var nameBox=Panel("BuildingName",inspector.transform,new Vector2(258,38),new Vector2(18,-60),TextAnchor.UpperLeft);
            buildingNameInput=nameBox.AddComponent<InputField>();buildingNameInput.characterLimit=FacilityIdentityRules.NameLimit;
            buildingTitle=Label("SelectedBuildingTitle",nameBox.transform,"",19,new Vector2(6,-4),246,30,Ivory);
            buildingTitle.supportRichText=false;buildingNameInput.textComponent=buildingTitle;
            buildingNameInput.onEndEdit.AddListener(value=>{
                if(refreshingBuildingName || selectedBuilding==null)return;
                if(!selectedBuilding.Rename(value))AppendEventLog("Use a unique, non-empty building name (up to 40 characters).");
                RefreshBuilding();
            });
            buildingStats = Label("SelectedBuildingStats", inspector.transform, "", 13, new Vector2(18, -110), 258, 160, Ivory);
            var statsViewport=new GameObject("Building detail scroll (mouse wheel)",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));
            statsViewport.GetComponent<Image>().color=Color.clear;
            statsViewport.transform.SetParent(inspector.transform,false);
            var viewportRect=statsViewport.GetComponent<RectTransform>();viewportRect.anchorMin=viewportRect.anchorMax=new Vector2(0,1);
            viewportRect.pivot=new Vector2(0,1);viewportRect.anchoredPosition=new Vector2(18,-110);viewportRect.sizeDelta=new Vector2(258,154);
            buildingStats.transform.SetParent(statsViewport.transform,false);
            var statsRect=buildingStats.rectTransform;statsRect.anchorMin=statsRect.anchorMax=new Vector2(0,1);statsRect.pivot=new Vector2(0,1);
            statsRect.anchoredPosition=Vector2.zero;statsRect.sizeDelta=new Vector2(258,160);
            buildingStats.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var statsScroll=statsViewport.GetComponent<ScrollRect>();statsScroll.viewport=viewportRect;statsScroll.content=statsRect;
            statsScroll.horizontal=false;statsScroll.movementType=ScrollRect.MovementType.Clamped;statsScroll.scrollSensitivity=18;
            Label("FocusTag", inspector.transform, "OPERATIONAL FOCUS", 10, new Vector2(18, -268), 258, 20, Gold);
            for (var i = 0; i < 3; i++)
            {
                var focus = i;
                focuses[i] = Button("BuildingFocus" + i, inspector.transform, new Vector2(18, -291 - i * 33), "", out _, 258, 28);
                focuses[i].onClick.AddListener(() => { if (selectedBuilding != null) selectedBuilding.SetFocus(focus); RefreshBuilding(); });
            }
            focusDescription = Label("FocusDescription", inspector.transform, "", 11, new Vector2(18, -386), 258, 38, Muted);
            growthPlanButton = Button("GrowthPlan", inspector.transform, new Vector2(18, -424), "", out growthPlanLabel, 258, 27);
            growthPlanButton.onClick.AddListener(() => { if (selectedBuilding != null && !selectedBuilding.State.improving) selectedBuilding.SetGrowthPlan(selectedBuilding.State.growthPlan == 0 ? 1 : selectedBuilding.State.growthPlan == 1 ? 2 : 0); RefreshBuilding(); });
            growthModeButton = Button("GrowthMode", inspector.transform, new Vector2(18, -454), "", out growthModeLabel, 258, 27);
            growthModeButton.onClick.AddListener(() => { selectedBuilding?.ToggleAutomaticImprovement(); RefreshBuilding(); });
            upgradeButton = Button("UpgradeBuilding", inspector.transform, new Vector2(18, -485), "", out upgradeLabel, 124, 30);
            upgradeButton.onClick.AddListener(() =>
            {
                if(selectedBuilding!=null)
                {
                    if(selectedBuilding.Upgrade())AppendEventLog(selectedBuilding.DisplayName+" expanded.");
                    else if(selectedBuilding.Id=="Farm")AppendEventLog("No free, suitable expansion territory for "+selectedBuilding.DisplayName+".");
                }
                RefreshBuilding();
            });
            operationButton = Button("ToggleBuildingOperation", inspector.transform, new Vector2(152, -485), "", out operationLabel, 124, 30);
            operationButton.onClick.AddListener(() => { if (selectedBuilding != null) selectedBuilding.ToggleOperation(); RefreshBuilding(); });
            excavationButton = Button("ExcavationPlan", inspector.transform, new Vector2(18, -519), "EXCAVATION PLAN", out _, 258, 26);
            excavationButton.onClick.AddListener(() => {
                if(selectedBuilding!=null && selectedBuilding.Residential!=null){selectedBuilding.Residential.SelectedModule=-1;RefreshBuilding();}
                else if(selectedBuilding!=null && (selectedBuilding.Id=="Manufactorum" || selectedBuilding.Id=="StorageDepot")) OpenFacilityPanel(selectedBuilding);
                else OpenExcavationPlan(selectedBuilding);
            });
            inspector.SetActive(false);
        }

        private void BuildNewGame(Transform parent)
        {
            newGamePanel = CommandUi.Screen("NewGamePanel", parent, 1);
            Label("MainIdentity", newGamePanel.transform, "WH30K  /  COLONY COMMAND", 20, new Vector2(34, -32), 600, 36, Ivory);
            Label("MainSubtitle", newGamePanel.transform, "PLANETARY EXPEDITION AUTHORITY", 11, new Vector2(35, -78), 600, 24, Gold);
            var card = Panel("ExpeditionCard", newGamePanel.transform, new Vector2(540, 408), Vector2.zero, TextAnchor.MiddleCenter);
            Accent(card, Gold);
            Label("ExpeditionTag", card.transform, "EXPEDITION AUTHORITY  /  M31", 11, new Vector2(34, -30), 470, 24, Gold);
            Label("ExpeditionTitle", card.transform, "ESTABLISH A NEW WORLD", 27, new Vector2(34, -67), 475, 46, Ivory);
            Label("ExpeditionSubline", card.transform, "Explore. Establish. Endure.", 14, new Vector2(34, -119), 470, 30, Muted);
            Label("SeedTag", card.transform, "GENERATION SEED", 10, new Vector2(34, -176), 470, 20, Gold);
            var input = Panel("SeedInput", card.transform, new Vector2(472, 36), new Vector2(34, -203), TextAnchor.UpperLeft, false);
            input.GetComponent<Image>().color = ButtonBase;
            seedInput = input.AddComponent<InputField>();
            var inputText = Label("SeedText", input.transform, "", 15, new Vector2(12, -8), 448, 26, Ivory);
            seedInput.textComponent = inputText; seedInput.contentType = InputField.ContentType.IntegerNumber;
            difficultyLabel = Label("LandingModeHint", card.transform,
                "LANDING CATEGORY AND STARTING CONDITIONS ARE SELECTED ON THE PLANET",
                11, new Vector2(34, -255), 472, 36, Gold);
            Button("LoadButton", card.transform, new Vector2(34, -326), "RESUME COLONY", out _, 226, 44)
                .onClick.AddListener(() =>
                {
                    if(GameSettings.HasActiveGame) {ShowNewGamePanel(false);ShowHud(true);}
                    else bootstrap.LoadFromFile();
                });
            var begin = Button("StartButton", card.transform, new Vector2(276, -326), "BEGIN EXPEDITION", out _, 230, 44);
            MarkPrimary(begin);
            begin.onClick.AddListener(() =>
                {
                    var seed = int.TryParse(seedInput.text, out var parsed) ? parsed : UnityEngine.Random.Range(0, int.MaxValue);
                    bootstrap.BeginNewGame(seed, GameSettings.Difficulty.Standard, chooseLanding: true);
                });
        }

        private void BuildEvent(Transform parent)
        {
            eventPanel = CommandUi.Screen("EventPanel", parent, .72f);
            var modal = Panel("DecisionCard", eventPanel.transform, new Vector2(620, 330), Vector2.zero, TextAnchor.MiddleCenter);
            Accent(modal, CommandUi.Warning);
            Label("DecisionTag", modal.transform, "PRIORITY TRANSMISSION  /  DECISION REQUIRED", 11, new Vector2(26, -22), 566, 25, Gold);
            eventTitleText = Label("EventTitle", modal.transform, "", 26, new Vector2(26, -63), 566, 42, Ivory);
            eventBodyText = Label("EventBody", modal.transform, "", 15, new Vector2(26, -121), 566, 98, Ivory);
            choiceA = Button("ChoiceA", modal.transform, new Vector2(26, -239), "", out choiceALabel, 273, 64);
            choiceB = Button("ChoiceB", modal.transform, new Vector2(321, -239), "", out choiceBLabel, 273, 64);
            choiceALabel.fontSize = choiceBLabel.fontSize = 13;
            choiceA.onClick.AddListener(() => choiceAHandler?.Invoke()); choiceB.onClick.AddListener(() => choiceBHandler?.Invoke());
        }

        private void BuildDiscovery(Transform parent)
        {
            discoveryPanel = CommandUi.Screen("DiscoveryPanel", parent, .72f);
            var card=Panel("DiscoveryCard",discoveryPanel.transform,new Vector2(620,452),Vector2.zero,TextAnchor.MiddleCenter);
            Accent(card, CommandUi.Signal);
            Label("DiscoveryTag",card.transform,"EXPEDITION RECORD  /  SURVEY INTELLIGENCE",11,new Vector2(28,-24),564,22,Gold);
            discoveryTitleText=Label("DiscoveryTitle",card.transform,"",22,new Vector2(28,-57),564,58,Ivory);
            discoveryBodyText=Label("DiscoveryDetails",card.transform,"",14,new Vector2(28,-123),564,204,Ivory);
            Button("PreviousDiscovery",card.transform,new Vector2(28,-340),"< PREVIOUS",out _,162,30).onClick.AddListener(()=>Industry?.BrowseDiscoveredLocation(-1));
            discoveryLocateButton=Button("LocateDiscovery",card.transform,new Vector2(205,-340),"LOCATE SITE",out _,210,30);
            discoveryLocateButton.onClick.AddListener(()=>{var action=discoveryLocateAction;ResetDiscoveryPanel();action?.Invoke();});
            Button("NextDiscovery",card.transform,new Vector2(430,-340),"NEXT >",out _,162,30).onClick.AddListener(()=>Industry?.BrowseDiscoveredLocation(1));
            discoveryActionButton=Button("DiscoveryAction",card.transform,new Vector2(28,-388),"",out discoveryActionLabel,350,38);
            discoveryActionButton.onClick.AddListener(()=>{var action=discoveryAction;ResetDiscoveryPanel();action?.Invoke();});
            Button("CloseDiscovery",card.transform,new Vector2(394,-388),"CLOSE",out _,198,38).onClick.AddListener(CloseDiscoveryPanel);
            discoveryPanel.SetActive(false);
        }

        private void Update()
        {
            RefreshLanding();
            RefreshEarthworks();
            if(FacilityPanelOpen && Input.GetKeyDown(KeyCode.Escape)){CloseFacilityPanel();return;}
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var typing = selected != null && selected.activeInHierarchy && selected.GetComponent<InputField>() != null;
            if (!typing)
            {
                if (Input.GetKeyDown(KeyCode.Escape) && researchPanel != null && researchPanel.activeSelf) { CloseResearchPanel(); return; }
                if (Input.GetKeyDown(KeyCode.Escape) && guidePanel != null && guidePanel.activeSelf) { CloseGuide(); return; }
                if (Input.GetKeyDown(KeyCode.Escape) && resourceDepositsPanel.activeSelf && !resourceDepositsPinned) { resourceDepositsPanel.SetActive(false); return; }
                if (Input.GetKeyDown(KeyCode.F5) && GameSettings.HasActiveGame) bootstrap.SaveToFile();
                if (Input.GetKeyDown(KeyCode.F9)) bootstrap.LoadFromFile();
                if (Input.GetKeyDown(KeyCode.Space) && GameSettings.HasActiveGame && !bootstrap.ChoosingLanding) clock.TogglePause();
#if UNITY_EDITOR
                if (Input.GetKeyDown(KeyCode.F12)) debugPanel.SetActive(!debugPanel.activeSelf);
#endif
            }
            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer >= 0.15f) { refreshTimer = 0; RefreshClock(); RefreshBuilding(); RefreshMaterials(); RefreshOutputTooltip(); RefreshFacilityPanel(); RefreshResearchPanel(); RefreshResourceDepositsPane(); RefreshFoundingObjectives(); }
            UpdateColonyMarker();
        }

        private void RefreshClock()
        {
            var snapshot = clock.Snapshot;
            dateText.text = $"{ColonyCalendar.Day(snapshot.simulationSeconds):00} {months[ColonyCalendar.Month(snapshot.simulationSeconds) - 1]}  /  YEAR {ColonyCalendar.Year(snapshot.simulationSeconds):000}";
            var played = TimeSpan.FromSeconds(snapshot.playedSeconds);
            var mode = clock.DecisionPending || clock.MiningDecisionPending ? "DECISION PENDING" : clock.Paused ? "PAUSED" : clock.Speed + "x SPEED";
            clockText.text = $"{mode}   |   PLAYED {(int)played.TotalHours:00}:{played.Minutes:00}:{played.Seconds:00}";
            Tone(pauseButton, clock.Paused);
            var speeds = Speeds;
            for (var i = 0; i < speeds.Length; i++) Tone(speedButtons[i], !clock.Paused && clock.Speed == speeds[i]);
        }

        private void UpdateColonyMarker()
        {
            if (colonyMarker == null || !GameSettings.HasActiveGame || bootstrap.ActivePlanet == null || Orbit == null)
            { if (colonyMarker != null) colonyMarker.gameObject.SetActive(false); return; }
            var world = Orbit.ColonyWorldPosition;
            var camera = Camera.main;
            var screen = camera.WorldToScreenPoint(world);
            var normal = (world - bootstrap.ActivePlanet.transform.position).normalized;
            var visible = screen.z > 0 && Vector3.Dot(normal, camera.transform.position - world) > 0
                && Vector3.Distance(camera.transform.position, world) > 1200;
            colonyMarker.gameObject.SetActive(visible);
            if (visible && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas.GetComponent<RectTransform>(), screen, null, out var local))
                colonyMarker.GetComponent<RectTransform>().anchoredPosition = local;
        }

        public void ShowBuilding(ManagedBuilding building)
        {
            if (building != selectedBuilding && building != null && resourceDepositsPanel != null && !resourceDepositsPinned) resourceDepositsPanel.SetActive(false);
            if(FacilityPanelOpen && facilityBuilding!=building)CloseFacilityPanel();
            if(building!=null && building.Id=="Farm")building=building.EntityRoot??building;
            selectedBuilding = building; inspector.SetActive(building != null); RefreshBuilding();
            if(building!=null && (building.Id=="Manufactorum" || building.Id=="StorageDepot")) OpenFacilityPanel(building);
        }
        private void RefreshBuilding()
        {
            var selectingShip = selectedBuilding != null && selectedBuilding.Id == "ColonyShip";
            if (shipPanel != null) shipPanel.SetActive(selectingShip);
            if (selectingShip) { inspector.SetActive(false); RefreshShipHud(); return; }
            if (selectedBuilding == null) { inspector.SetActive(false); return; }
            inspector.SetActive(true);
            var state = selectedBuilding.State; var e = selectedBuilding.Economy; var impact = selectedBuilding.Impact;
            if(!buildingNameInput.isFocused){refreshingBuildingName=true;buildingNameInput.SetTextWithoutNotify(selectedBuilding.DisplayName);refreshingBuildingName=false;}
            if(selectedBuilding.Residential==null)buildingStats.text = $"TIER {state.level + 1}   /   {(state.offline ? "OFFLINE" : "OPERATIONAL")}\nHousing {e.capacity:0}     Jobs {e.jobs:0}\nCO2 impact  {impact.co2Delta:+0.00;-0.00;0} / s at full staffing\n{selectedBuilding.ImprovementStatus}";
            if(selectedBuilding.SurveyRadius>0)buildingStats.text+=$"\nSurvey radius {selectedBuilding.SurveyRadius:0} m\nRevealed sites {Industry?.DiscoveredCount??0}/{Industry?.DiscoveryCount??0}";
            if(IndustryRules.Facility(selectedBuilding.Id) && Industry!=null)
                buildingStats.text=$"TIER {state.level+1} / {(state.offline?"OFFLINE":"OPERATIONAL")}   Jobs {e.jobs:0}\n"+Industry.Status(selectedBuilding)+"\n"+selectedBuilding.ImprovementStatus;
            if(selectedBuilding.Id=="Farm" && Industry!=null)buildingStats.text=Industry.Status(selectedBuilding);
            if(selectedBuilding.Id=="DevSupply")
                buildingStats.text=$"DEVELOPER TEST SOURCE / {(state.offline?"OFF":"ON")}\nUnlimited construction supplies and advanced supplies while active.\nUse { (state.offline?"RESUME":"SUSPEND") } below to toggle.";
            for (var i = 0; i < 3; i++)
            {
                focuses[i].gameObject.SetActive(selectedBuilding.Id != "Mine");
                focuses[i].GetComponentInChildren<Text>().text = BuildingOperationRules.FocusName(selectedBuilding.Id, i);
                Tone(focuses[i], state.focus == i);
                focuses[i].interactable = selectedBuilding.Id != "ColonyShip";
            }
            focusDescription.text = selectedBuilding.Id == "Mine"
                ? BuildingOperationRules.FocusName("Mine", state.focus) + "\nSet automatically at placement."
                : BuildingOperationRules.FocusDescription(selectedBuilding.Id, state.focus);
            upgradeButton.interactable = selectedBuilding.CanUpgrade;
            upgradeLabel.text = state.level >= 2 ? "MAX TIER" : state.improving ? "IN PROGRESS" : $"IMPROVE  {selectedBuilding.UpgradeCost * ColonyRules.LegacyProjectSupplyScale:0.##} SUP";
            growthPlanLabel.text = "EXPANSION: " + AdaptiveBuildingRules.PlanName(state.growthPlan) + "  [CHANGE]";
            growthModeLabel.text = state.manualImprovements ? "MILESTONE: MANUAL" : "MILESTONE: AUTOMATIC";
            growthPlanButton.interactable = state.level < 2 && !state.improving;
            growthModeButton.interactable = state.level < 2 && !state.improving;
            upgradeButton.gameObject.SetActive(selectedBuilding.Id != "ColonyShip" && selectedBuilding.Id != "Farm");
#if UNITY_EDITOR
            if(selectedBuilding.Id=="Farm") { upgradeButton.gameObject.SetActive(true); upgradeButton.interactable=Industry.HasFarmCapacity(selectedBuilding); upgradeLabel.text="DEV: ADD FIELD"; }
#endif
            growthPlanButton.gameObject.SetActive(selectedBuilding.Id != "ColonyShip" && selectedBuilding.Id != "Farm");
            growthModeButton.gameObject.SetActive(selectedBuilding.Id != "ColonyShip" && selectedBuilding.Id != "Farm");
            if(selectedBuilding.Residential!=null)
            {
                var residential=selectedBuilding.Residential;
                buildingStats.fontSize=11;buildingStats.text=residential.Status;
                upgradeLabel.text=residential.Data.project!=null?"CONSTRUCTING":"APPROVE";
                growthPlanLabel.text="PREFER: "+(state.growthPlan==1?"UPWARD":state.growthPlan==2?"OUTWARD":"BALANCED");
                growthModeLabel.text=state.manualImprovements?"DEVELOPMENT: MANUAL":"DEVELOPMENT: AUTOMATIC";
                growthPlanButton.interactable=residential.Data.project==null;
                growthModeButton.interactable=true;
            }
            else buildingStats.fontSize=13;
            if(selectedBuilding.Id=="Manufactorum")
            {
                upgradeLabel.text=state.manufactorum.project!=null?"CONSTRUCTING":"EXPAND";
                growthPlanButton.gameObject.SetActive(false); growthModeButton.gameObject.SetActive(false);
            }
            operationLabel.text = state.offline ? "RESUME" : "SUSPEND";
            excavationButton.gameObject.SetActive(selectedBuilding.Id=="Mine" || selectedBuilding.Id=="Quarry" || selectedBuilding.Residential!=null || selectedBuilding.Id=="Manufactorum" || selectedBuilding.Id=="StorageDepot");
            excavationButton.GetComponentInChildren<Text>().text=selectedBuilding.Id=="Manufactorum"?"SELECT PRODUCTION":selectedBuilding.Id=="StorageDepot"?"CONFIGURE STORAGE":selectedBuilding.Residential!=null?"WHOLE BLOCK DETAILS":"EXCAVATION PLAN";
        }

        internal void UpdateBuildButtonState(string displayName, bool placementActive)
        {
            if (buildButtonLabel != null) buildButtonLabel.text = placementActive ? "CANCEL DEPLOYMENT  [ESC]" : "DEPLOY STRUCTURE";
            if (structureDetails != null && buildingPlacement != null) structureDetails.text = displayName + "\n" + buildingPlacement.ActiveStructureDetails;
            Tone(buildButton, placementActive);
            for (var i = 0; i < catalog.Length; i++)
                if (catalog[i] != null) Tone(catalog[i], buildingPlacement != null && i<visibleIds.Length && buildingPlacement.ActiveStructureId==visibleIds[i]);
        }
        private void SetCatalogGroup(int group)
        {
            visibleIds=catalogGroups[group];var definitions=Resources.LoadAll<BuildableStructure>("Buildings");
            for(var i=0;i<4;i++)
            {
                catalog[i].gameObject.SetActive(i<visibleIds.Length);if(i>=visibleIds.Length)continue;
                var definition=Array.Find(definitions,d=>d.Id==visibleIds[i]);
                catalog[i].GetComponentInChildren<Text>().text=ConstructionCard(definition.Id);
            }
            selection.Select(null);buildingPlacement.SelectStructure(visibleIds[0]);
        }
        private string ConstructionCard(string id)
        {
            var definition = Array.Find(Resources.LoadAll<BuildableStructure>("Buildings"), d => d.Id == id);
            if (definition == null) return id;
            return definition.DisplayName.ToUpperInvariant()+$"\n{definition.ConstructionSupplies:0.#} SUPPLIES"+
                (definition.AdvancedSupplies > 0 ? $" / {definition.AdvancedSupplies:0.#} PLAS" : "") +
                (Industry != null && !Industry.CanAffordConstruction(definition.ConstructionSupplies,definition.AdvancedSupplies) ? "\nNEED SUPPLIES" : "\nREADY TO PLACE");
        }
        private void RefreshMaterials()
        {
            if(materialText==null || Industry==null || bootstrap.ActivePlanet==null)return;
            Industry.FillTotals(materialTotals);var total=materialTotals;var biome=Orbit!=null?bootstrap.ActivePlanet.Biome(Orbit.NavigationDirection):"";
            Industry.FillRates(materialProduced,materialConsumed);
            UpdateResourceReadout(resourceSystem.CreateSnapshot());
            for(var i=0;i<catalog.Length && i<visibleIds.Length;i++)catalog[i].GetComponentInChildren<Text>().text=ConstructionCard(visibleIds[i]);
            UpdateBuildButtonState(buildingPlacement.ActiveStructureName,buildingPlacement.IsPlacing);
            materialText.text=$"FOOD {resourceSystem.Food:0}  NEED {FarmRules.MonthlyDemand(resourceSystem.CreateSnapshot().Population):0}/mo  W {resourceSystem.Water:0}\nFE {total[0]:0}  CU {total[1]:0}  ST {total[2]:0}  BIO {total[3]:0}\nMET {total[5]:0}  PART {total[6]:0}  PLAS {total[8]:0}  CRY {total[4]:0}\nFERT B {total[9]:0}  I {total[10]:0}  A {total[11]:0}  FUEL {total[7]:0}\nPOWER {Industry.PowerGenerated:0.#}/{Industry.PowerDemand:0.#}  STAFF {Industry.WorkersAvailable:0}/{Industry.WorkersRequired:0}\nCARGO {Industry.CargoCount} / FOOD {Industry.FoodTransportCount}  {biome}";
            surveyLabel.text=Industry.Overlay==1 && Industry.LogisticsBuildPending ? "ROUTES: PREPARING"
                : Industry.Overlay==1 && Industry.LogisticsVisualTruncated ? "ROUTES: DISPLAY LIMIT"
                : "SURVEY: "+SurveyLabels[Industry.Overlay];
            if(discoveryButton!=null){var label=discoveryButton.GetComponentInChildren<Text>();label.fontSize=11;label.text=$"SITES {Industry.DiscoveredCount}/{Industry.DiscoveryCount}  /  AREA {Industry.ExploredFraction:P0}";}
        }
        private void VisitDeposit()
        {
            if(Industry==null || Orbit==null || bootstrap.ActivePlanet==null)return;
            var kind=Industry.Overlay==7?MaterialKind.Promethium:(MaterialKind)Mathf.Clamp(Industry.Overlay-2,0,4);
            var world=bootstrap.ActivePlanet.transform.TransformPoint(bootstrap.ActivePlanet.EvaluateSurfacePoint(Orbit.NavigationDirection));
            var deposit=Industry.NearestDeposit(world,kind);
            if(deposit!=null){Industry.SetOverlay(kind==MaterialKind.Promethium?7:(int)kind+2);Orbit.FocusLocation(deposit.Direction);AppendEventLog($"Survey: {IndustryRules.Name(kind)}, {deposit.Remaining:0} units.");}
        }
        private void OpenDiscoveries()
        {
            if(Industry==null)return;
            Industry.SetOverlay(1);
            Industry.BrowseDiscoveredLocation(1);
        }
        public void ShowDiscoveryPanel(string title,string body,string actionLabel,Action action,Action locate=null)
        {
            HideOutputTooltip();
            discoveryTitleText.text=title;discoveryBodyText.text=body;discoveryActionLabel.text=actionLabel;
            discoveryAction=action;discoveryActionButton.interactable=action!=null;discoveryPanel.SetActive(true);
            discoveryLocateAction=locate;discoveryLocateButton.interactable=locate!=null;
            if(clock!=null)clock.DecisionPending=true;
        }
        public void ResetDiscoveryPanel()
        {
            if(discoveryPanel!=null)discoveryPanel.SetActive(false);
            discoveryAction=null;discoveryLocateAction=null;
            if(clock!=null)clock.DecisionPending=HasOpenEvent||(newGamePanel!=null&&newGamePanel.activeSelf);
        }
        private void CloseDiscoveryPanel()=>ResetDiscoveryPanel();
        // Session UI must not retain a modal, callback or selected object from a replaced world.
        public void ResetSessionUi()
        {
            CloseResearchPanel(); CloseGuide(); CloseFacilityPanel(); CloseMiningPanel();
            ResetDiscoveryPanel(); ShowEventPanel(false); HideOutputTooltip();
            ShowLandingPanel(false); ResetGuideForNewExpedition();
            selectedBuilding = null;
            if (shipPanel != null) shipPanel.SetActive(false);
            if (inspector != null) inspector.SetActive(false);
        }
        public void FocusLocation(Vector3 direction){Orbit?.FocusLocation(direction);}
        public void ShowNewGamePanel(bool visible) { if (visible) { HideOutputTooltip(); CloseFacilityPanel(); } newGamePanel.SetActive(visible); saveButton.interactable = GameSettings.HasActiveGame; if(clock!=null)clock.DecisionPending=visible||HasOpenEvent; }
        public void ShowHud(bool visible) { hudPanel.SetActive(visible); if (visible) { selection.Select(null); RefreshResourceDepositsPane(); } }
        public void ShowEventPanel(bool visible) { if (visible) HideOutputTooltip(); eventPanel.SetActive(visible); if (clock != null) clock.DecisionPending = HasOpenEvent || (newGamePanel!=null && newGamePanel.activeSelf); }
        public void SetEventChoiceAInteractable(bool affordable) { choiceA.interactable = affordable; }
        public void SetSeed(int seed) { seedInput.text = seed.ToString(); }
        public void SetDifficulty(GameSettings.Difficulty difficulty)
        {
            if (difficultyLabel != null)
                difficultyLabel.text = "LAST EXPEDITION  /  " + GameSettings.GetDefinition(difficulty).displayName.ToUpperInvariant();
        }
        public void UpdateResourceReadout(ResourceSnapshot state)
        {
            stockpileValue.text = ColonyRules.SupplyLabel(state.Food, FarmRules.MonthlyDemand(state.Population)) + $" / {state.Food:0}";
            waterSupplyValue.text = ColonyRules.SupplyLabel(state.Water, WaterRules.Demand(state.Population) * (float)ColonyCalendar.SecondsPerMonth) + $" / {state.Water:0}";
            populationValue.text = state.Population.ToString("N0");
            workforceValue.text = $"{state.Workforce:0} / {ColonyRules.PopulationHealth:P0}";
            if (Industry != null)
            {
                var supplies = Industry.ConstructionSupplyTotals();
                Industry.FillRates(materialProduced, materialConsumed);
                netValue.text = ColonyRules.SupplyLabel(supplies.basic, materialConsumed[6] * (float)ColonyCalendar.SecondsPerMonth)
                    + $" / {supplies.basic:0.#}\n{ supplies.advanced:0.#} plasteel";
            }
            RefreshOutputTooltip();
        }
        public void UpdateEnvironmentReadout(EnvironmentSnapshot state)
        {
            environmentText.text = $"Habitability   {environmentState.Habitability:P0}\n\nCO2   {state.Co2:0.0} ppm\nOxygen   {state.O2:0.0}%\nWater pollution   {state.WaterPollution:0.0}\nTemperature   {state.GlobalTemperatureOffset:+0.00;-0.00;0.00} C";
            habitabilityFill.GetComponent<RectTransform>().sizeDelta = new Vector2(200 * environmentState.Habitability, 4);
        }
        public void AppendEventLog(string entry)
        {
            log.Add(entry); while (log.Count > 2) log.RemoveAt(0);
            eventLogText.text = entry;
        }
        public void PromptEvent(string title, string body, string optionAText, Action optionA, string optionBText, Action optionB)
        {
            choiceA.interactable=choiceB.interactable=true;
            eventTitleText.text = title; eventBodyText.text = body; choiceALabel.text = optionAText; choiceBLabel.text = optionBText;
            choiceAHandler = () => { ShowEventPanel(false); optionA?.Invoke(); };
            choiceBHandler = () => { ShowEventPanel(false); optionB?.Invoke(); };
            ShowEventPanel(true);
        }

        private GameObject Panel(string name, Transform parent, Vector2 size, Vector2 position, TextAnchor anchor, bool border = true)
            => CommandUi.Panel(name, parent, size, position, anchor, border);

        private void Accent(GameObject panel, Color color) => CommandUi.Accent(panel, color);
        private void StretchWidth(GameObject panel, float inset)
        {
            var rect = panel.GetComponent<RectTransform>();
            var y = rect.anchorMin.y; rect.anchorMin = new Vector2(0, y); rect.anchorMax = new Vector2(1, y);
            rect.pivot = new Vector2(0.5f, rect.pivot.y); rect.sizeDelta = new Vector2(-inset * 2, rect.sizeDelta.y);
            rect.anchoredPosition = new Vector2(0, rect.anchoredPosition.y);
        }
        private Text Label(string name, Transform parent, string content, int size, Vector2 position, float width, float height, Color color)
            => CommandUi.Label(name, parent, font, content, size, position, width, height, color);

        private Button Button(string name, Transform parent, Vector2 position, string content, out Text label, float width, float height = 34)
            => CommandUi.Button(name, parent, font, position, content, out label, width, height);

        private static void MarkPrimary(Button button) => CommandUi.Tone(button, true);

        private void Tone(Button button, bool selected)
        {
            if (button == null || buttonTones.TryGetValue(button, out var priorTone) && priorTone == selected) return;
            buttonTones[button] = selected;
            CommandUi.Tone(button, selected);
        }
#if UNITY_EDITOR
        private void BuildDebug(Transform parent)
        {
            debugPanel = Panel("DebugPanel", parent, new Vector2(232, 206), new Vector2(16, -132), TextAnchor.UpperLeft);
            Label("DebugTitle", debugPanel.transform, "DEVELOPER TOOLS  [F12]", 11, new Vector2(16, -16), 206, 20, Gold);
            Button("AdvanceTick", debugPanel.transform, new Vector2(16, -52), "ADVANCE SETTLEMENT TICK", out _, 200)
                .onClick.AddListener(() => settlement.DebugRunImmediateTick());
            Button("DevSupply", debugPanel.transform, new Vector2(16, -88), "PLACE INFINITE SUPPLY NEXUS", out _, 200)
                .onClick.AddListener(() => buildingPlacement.SelectStructure("DevSupply"));
            Button("AddStockpile", debugPanel.transform, new Vector2(16, -124), "+100 BUILD SUPPLIES", out _, 200)
                .onClick.AddListener(() => resourceSystem.ModifyStockpile(100));
            var triggerEvent = Button("TriggerEvent", debugPanel.transform, new Vector2(16, -160), "TRIGGER POLICY EVENT", out _, 200);
            triggerEvent.interactable = EventSchedule.PriorityTransmissionsEnabled;
            triggerEvent.onClick.AddListener(() => colonyEventSystem.TriggerDebugEventNow());
            debugPanel.SetActive(false);
        }
#endif
    }
}
