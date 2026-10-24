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
    public class NewGameMenu : MonoBehaviour
    {
        private static readonly Color Gold = new Color(0.80f, 0.65f, 0.38f);
        private static readonly Color Ivory = new Color(0.88f, 0.90f, 0.87f);
        private static readonly Color Muted = new Color(0.49f, 0.57f, 0.60f);
        private static readonly Color Surface = new Color(0.025f, 0.040f, 0.052f, 0.96f);
        private static readonly Color ButtonBase = new Color(0.065f, 0.092f, 0.108f, 1f);
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
        private GameObject newGamePanel, hudPanel, eventPanel, inspector;
        private InputField seedInput;
        private Text difficultyLabel, stockpileValue, populationValue, netValue, workforceValue;
        private Text dateText, clockText, environmentText, eventLogText, buildButtonLabel, structureDetails;
        private Text eventTitleText, eventBodyText, choiceALabel, choiceBLabel, buildingTitle, buildingStats, focusDescription;
        private Text upgradeLabel, operationLabel, statusText;
        private Text materialText, surveyLabel;
        private Button saveButton, loadButton, buildButton, upgradeButton, operationButton, pauseButton, colonyMarker;
        private Button choiceA, choiceB;
        private readonly Button[] focuses = new Button[3];
        private readonly Button[] catalog = new Button[4];
        private readonly Button[] speedButtons = new Button[3];
        private readonly List<string> log = new List<string>();
        private readonly string[] catalogIds = { "Outpost", "HabBlock", "Factory", "Utility" };
        private readonly string[] catalogLabels = { "FRONTIER OUTPOST\n250  /  LOGISTICS", "HAB BLOCK\n180  /  POPULATION", "MACRO FACTORY\n320  /  INDUSTRY", "AIR SCRUBBER\n220  /  ENVIRONMENT" };
        private string[] visibleIds = { "Outpost", "HabBlock", "Factory", "Utility" };
        private readonly string[][] catalogGroups = { new[] { "Outpost", "HabBlock", "Factory", "Utility" }, new[] { "Mine", "Quarry", "Harvester" }, new[] { "Smelter", "Works" } };
        private IndustrySystem Industry => GetComponent<IndustrySystem>();
        private readonly string[] months = { "JANUARY", "FEBRUARY", "MARCH", "APRIL", "MAY", "JUNE", "JULY", "AUGUST", "SEPTEMBER", "OCTOBER", "NOVEMBER", "DECEMBER" };
        private ManagedBuilding selectedBuilding;
        private GameSettings.Difficulty selectedDifficulty = GameSettings.Difficulty.Standard;
        private Action choiceAHandler, choiceBHandler;
        private Image habitabilityFill;
        private float refreshTimer;
#if UNITY_EDITOR
        private GameObject debugPanel;
#endif
        public bool HasOpenEvent => eventPanel != null && eventPanel.activeSelf;
        public ManagedBuilding InspectedBuilding => selectedBuilding;
        private SimpleOrbitCamera Orbit => Camera.main != null ? Camera.main.GetComponent<SimpleOrbitCamera>() : null;

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
            scaler.referenceResolution = new Vector2(1280, 800); scaler.matchWidthOrHeight = 1;
            hudPanel = new GameObject("HudPanel", typeof(RectTransform)); hudPanel.transform.SetParent(root.transform, false);
            var hudRect = hudPanel.GetComponent<RectTransform>();
            hudRect.anchorMin = Vector2.zero; hudRect.anchorMax = Vector2.one;
            hudRect.offsetMin = hudRect.offsetMax = Vector2.zero;
            BuildHeader(hudPanel.transform);
            BuildPlanetPanel(hudPanel.transform);
            BuildConstruction(hudPanel.transform);
            BuildInspector(hudPanel.transform);
            var footer = Panel("NavigationBar", hudPanel.transform, new Vector2(0, 28), Vector2.zero, TextAnchor.LowerCenter);
            StretchWidth(footer, 0);
            statusText = Label("NavigationHelp", footer.transform,
                "WASD MOVE   MMB PAN   RMB ROTATE   WHEEL ZOOM   SHIFT FAST   F COLONY   HOME ORBIT   SPACE PAUSE   |   30 MIN / YEAR AT 1x",
                10, new Vector2(18, -7), 1220, 20, Muted);
            colonyMarker = Button("ColonyLocator", hudPanel.transform, Vector2.zero, "COLONY PRIMUS  [F]", out _, 160, 30);
            var markerRect = colonyMarker.GetComponent<RectTransform>();
            markerRect.anchorMin = markerRect.anchorMax = new Vector2(0.5f, 0.5f); markerRect.pivot = new Vector2(0.5f, 0.5f);
            colonyMarker.onClick.AddListener(() => Orbit?.FocusSettlement(settlement.CreateSnapshot().normal));
            BuildNewGame(root.transform);
            BuildEvent(root.transform);
#if UNITY_EDITOR
            BuildDebug(root.transform);
#endif
        }

        private void BuildHeader(Transform parent)
        {
            var header = Panel("CommandHeader", parent, new Vector2(0, 98), new Vector2(0, -16), TextAnchor.UpperLeft);
            StretchWidth(header, 16);
            Label("IdentityTag", header.transform, "EXPEDITION AUTHORITY  /  M31", 10, new Vector2(18, -17), 205, 18, Gold);
            Label("Identity", header.transform, "COLONY COMMAND", 19, new Vector2(18, -40), 210, 30, Ivory);
            Metric(header.transform, "Stockpile", "STOCKPILE", 236, out stockpileValue, 176);
            Metric(header.transform, "Population", "POPULATION", 426, out populationValue, 135);
            Metric(header.transform, "Net", "NET OUTPUT / s", 572, out netValue, 140);
            Metric(header.transform, "Workforce", "WORKFORCE", 722, out workforceValue, 130);
            var time = Panel("TimeControl", header.transform, new Vector2(326, 82), new Vector2(-10, -8), TextAnchor.UpperRight, false);
            dateText = Label("CalendarDate", time.transform, "", 17, new Vector2(8, -4), 234, 24, Ivory);
            clockText = Label("PlayedTime", time.transform, "", 10, new Vector2(8, -30), 238, 18, Muted);
            pauseButton = Button("PauseSimulation", time.transform, new Vector2(8, -50), "II", out _, 43, 26);
            pauseButton.onClick.AddListener(() => clock.TogglePause());
            var speeds = new[] { 1, 3, 10 };
            for (var i = 0; i < speeds.Length; i++)
            {
                var speed = speeds[i];
                speedButtons[i] = Button("Speed" + speed, time.transform, new Vector2(58 + i * 54, -50), speed + "x", out _, 47, 26);
                speedButtons[i].onClick.AddListener(() => clock.SetSpeed(speed));
            }
            saveButton = Button("SaveButton", time.transform, new Vector2(248, -6), "SAVE", out _, 68, 29);
            saveButton.onClick.AddListener(() => bootstrap.SaveToFile());
            loadButton = Button("HudLoadButton", time.transform, new Vector2(248, -46), "LOAD", out _, 68, 29);
            loadButton.onClick.AddListener(() => bootstrap.LoadFromFile());
        }

        private void Metric(Transform header, string name, string title, float x, out Text value, float width)
        {
            Label(name + "Title", header, title, 10, new Vector2(x, -18), width, 16, Gold);
            value = Label(name + "Value", header, "0", 23, new Vector2(x, -41), width, 35, Ivory);
        }

        private void BuildPlanetPanel(Transform parent)
        {
            var panel = Panel("PlanetStatus", parent, new Vector2(232, 236), new Vector2(16, -132), TextAnchor.UpperLeft);
            Label("PlanetStatusTitle", panel.transform, "PLANET STATUS", 10, new Vector2(16, -17), 110, 20, Gold);
            environmentText = Label("EnvironmentReadout", panel.transform, "", 14, new Vector2(16, -49), 202, 119, Ivory);
            var track = Panel("HabitabilityTrack", panel.transform, new Vector2(200, 4), new Vector2(16, -176), TextAnchor.UpperLeft, false);
            track.GetComponent<Image>().color = ButtonBase;
            var fill = Panel("HabitabilityFill", track.transform, new Vector2(200, 4), Vector2.zero, TextAnchor.UpperLeft, false);
            habitabilityFill = fill.GetComponent<Image>(); habitabilityFill.color = Gold;
            Button("ViewColony", panel.transform, new Vector2(16, -198), "COLONY  [F]", out _, 96, 26)
                .onClick.AddListener(() => Orbit?.FocusSettlement(settlement.CreateSnapshot().normal));
            Button("ViewPlanet", panel.transform, new Vector2(120, -198), "ORBIT", out _, 96, 26)
                .onClick.AddListener(() => Orbit?.FrameTarget());
            var journal = Panel("OperationsLog", parent, new Vector2(232, 202), new Vector2(16, -384), TextAnchor.UpperLeft);
            Label("JournalTitle", journal.transform, "RESOURCE DIRECTORATE", 11, new Vector2(16, -13), 204, 20, Gold);
            materialText = Label("MaterialInventory", journal.transform, "", 13, new Vector2(16, -37), 206, 80, Ivory);
            Button("SurveyOverlay", journal.transform, new Vector2(16, -122), "SURVEY: OFF", out surveyLabel, 200, 25)
                .onClick.AddListener(() => { if(Industry!=null) Industry.SetOverlay((Industry.Overlay+1)%7); });
            Button("VisitDeposit", journal.transform, new Vector2(16, -151), "VISIT NEAREST DEPOSIT", out _, 200, 25)
                .onClick.AddListener(VisitDeposit);
            eventLogText = Label("EventLog", journal.transform, "", 10, new Vector2(16, -183), 200, 16, Muted);
            Button("OpenExpedition", panel.transform, new Vector2(132, -12), "NEW WORLD", out _, 87, 24)
                .onClick.AddListener(() => ShowNewGamePanel(true));
        }

        private void BuildConstruction(Transform parent)
        {
            var deck = Panel("ConstructionDeck", parent, new Vector2(1000, 166), new Vector2(0, 44), TextAnchor.LowerCenter);
            Label("ConstructionTitle", deck.transform, "CONSTRUCTION AUTHORITY", 10, new Vector2(16, -11), 300, 18, Gold);
            for(var g=0;g<3;g++)
            {
                var group=g;Button("CatalogGroup"+g,deck.transform,new Vector2(302+g*127,-9),new[]{"COLONY","EXTRACTION","PROCESSING"}[g],out _,119,27)
                    .onClick.AddListener(()=>SetCatalogGroup(group));
            }
            for (var i = 0; i < 4; i++)
            {
                var index=i;
                var id = catalogIds[i];
                catalog[i] = Button("BuildCard" + id, deck.transform, new Vector2(16 + i * 171, -49),
                    catalogLabels[i], out var label, 161, 80);
                label.fontSize = 14; label.lineSpacing = 1.4f;
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
            inspector = Panel("BuildingInspector", parent, new Vector2(294, 454), new Vector2(-16, -132), TextAnchor.UpperRight);
            Label("InspectorTag", inspector.transform, "STRUCTURE DIRECTORATE", 10, new Vector2(18, -16), 230, 20, Gold);
            Button("CloseInspector", inspector.transform, new Vector2(252, -12), "X", out _, 26, 26)
                .onClick.AddListener(() => selection.Select(null));
            buildingTitle = Label("SelectedBuildingTitle", inspector.transform, "", 22, new Vector2(18, -47), 260, 57, Ivory);
            buildingStats = Label("SelectedBuildingStats", inspector.transform, "", 13, new Vector2(18, -110), 258, 104, Ivory);
            Label("FocusTag", inspector.transform, "OPERATIONAL FOCUS", 10, new Vector2(18, -206), 258, 20, Gold);
            for (var i = 0; i < 3; i++)
            {
                var focus = i;
                focuses[i] = Button("BuildingFocus" + i, inspector.transform, new Vector2(18, -232 - i * 35), "", out _, 258, 29);
                focuses[i].onClick.AddListener(() => { if (selectedBuilding != null) selectedBuilding.SetFocus(focus); RefreshBuilding(); });
            }
            focusDescription = Label("FocusDescription", inspector.transform, "", 11, new Vector2(18, -340), 258, 48, Muted);
            upgradeButton = Button("UpgradeBuilding", inspector.transform, new Vector2(18, -402), "", out upgradeLabel, 124, 34);
            upgradeButton.onClick.AddListener(() =>
            {
                if (selectedBuilding != null && selectedBuilding.Upgrade()) AppendEventLog(selectedBuilding.DisplayName + " upgraded.");
                RefreshBuilding();
            });
            operationButton = Button("ToggleBuildingOperation", inspector.transform, new Vector2(152, -402), "", out operationLabel, 124, 34);
            operationButton.onClick.AddListener(() => { if (selectedBuilding != null) selectedBuilding.ToggleOperation(); RefreshBuilding(); });
            inspector.SetActive(false);
        }

        private void BuildNewGame(Transform parent)
        {
            newGamePanel = Panel("NewGamePanel", parent, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter, false);
            var backdrop = newGamePanel.GetComponent<RectTransform>();
            backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
            newGamePanel.GetComponent<Image>().color = new Color(0.009f, 0.017f, 0.025f, 1);
            Label("MainIdentity", newGamePanel.transform, "WH30K  /  COLONY COMMAND", 20, new Vector2(34, -32), 600, 36, Ivory);
            Label("MainSubtitle", newGamePanel.transform, "PLANETARY EXPEDITION AUTHORITY", 11, new Vector2(35, -78), 600, 24, Gold);
            var card = Panel("ExpeditionCard", newGamePanel.transform, new Vector2(540, 408), Vector2.zero, TextAnchor.MiddleCenter);
            Label("ExpeditionTag", card.transform, "EXPEDITION AUTHORITY  /  M31", 11, new Vector2(34, -30), 470, 24, Gold);
            Label("ExpeditionTitle", card.transform, "ESTABLISH A NEW WORLD", 27, new Vector2(34, -67), 475, 46, Ivory);
            Label("ExpeditionSubline", card.transform, "Explore. Establish. Endure.", 14, new Vector2(34, -119), 470, 30, Muted);
            Label("SeedTag", card.transform, "GENERATION SEED", 10, new Vector2(34, -176), 470, 20, Gold);
            var input = Panel("SeedInput", card.transform, new Vector2(472, 36), new Vector2(34, -203), TextAnchor.UpperLeft, false);
            input.GetComponent<Image>().color = ButtonBase;
            seedInput = input.AddComponent<InputField>();
            var inputText = Label("SeedText", input.transform, "", 15, new Vector2(12, -8), 448, 26, Ivory);
            seedInput.textComponent = inputText; seedInput.contentType = InputField.ContentType.IntegerNumber;
            Button("DifficultyButton", card.transform, new Vector2(34, -255), "STANDARD", out difficultyLabel, 472, 36)
                .onClick.AddListener(() =>
                {
                    var definitions = GameSettings.GetAllDefinitions();
                    var next = ((int)selectedDifficulty + 1) % definitions.Count;
                    SetDifficulty(definitions[next].difficulty);
                });
            Button("LoadButton", card.transform, new Vector2(34, -326), "RESUME COLONY", out _, 226, 44)
                .onClick.AddListener(() =>
                {
                    if(GameSettings.HasActiveGame) {ShowNewGamePanel(false);ShowHud(true);}
                    else bootstrap.LoadFromFile();
                });
            Button("StartButton", card.transform, new Vector2(276, -326), "BEGIN EXPEDITION", out _, 230, 44)
                .onClick.AddListener(() =>
                {
                    var seed = int.TryParse(seedInput.text, out var parsed) ? parsed : UnityEngine.Random.Range(0, int.MaxValue);
                    bootstrap.BeginNewGame(seed, selectedDifficulty);
                });
        }

        private void BuildEvent(Transform parent)
        {
            eventPanel = new GameObject("EventPanel", typeof(RectTransform), typeof(Image));
            eventPanel.transform.SetParent(parent, false);
            var rect = eventPanel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            eventPanel.GetComponent<Image>().color = new Color(0.005f, 0.008f, 0.012f, 0.62f);
            var modal = Panel("DecisionCard", eventPanel.transform, new Vector2(620, 330), Vector2.zero, TextAnchor.MiddleCenter);
            Label("DecisionTag", modal.transform, "PRIORITY TRANSMISSION  /  DECISION REQUIRED", 11, new Vector2(26, -22), 566, 25, Gold);
            eventTitleText = Label("EventTitle", modal.transform, "", 26, new Vector2(26, -63), 566, 42, Ivory);
            eventBodyText = Label("EventBody", modal.transform, "", 15, new Vector2(26, -121), 566, 98, Ivory);
            choiceA = Button("ChoiceA", modal.transform, new Vector2(26, -239), "", out choiceALabel, 273, 64);
            choiceB = Button("ChoiceB", modal.transform, new Vector2(321, -239), "", out choiceBLabel, 273, 64);
            choiceALabel.fontSize = choiceBLabel.fontSize = 13;
            choiceA.onClick.AddListener(() => choiceAHandler?.Invoke()); choiceB.onClick.AddListener(() => choiceBHandler?.Invoke());
        }

        private void Update()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var typing = selected != null && selected.activeInHierarchy && selected.GetComponent<InputField>() != null;
            if (!typing)
            {
                if (Input.GetKeyDown(KeyCode.F5) && GameSettings.HasActiveGame) bootstrap.SaveToFile();
                if (Input.GetKeyDown(KeyCode.F9)) bootstrap.LoadFromFile();
                if (Input.GetKeyDown(KeyCode.Space) && GameSettings.HasActiveGame) clock.TogglePause();
#if UNITY_EDITOR
                if (Input.GetKeyDown(KeyCode.F12)) debugPanel.SetActive(!debugPanel.activeSelf);
#endif
            }
            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer >= 0.15f) { refreshTimer = 0; RefreshClock(); RefreshBuilding(); RefreshMaterials(); }
            UpdateColonyMarker();
        }

        private void RefreshClock()
        {
            var snapshot = clock.Snapshot;
            dateText.text = $"{ColonyCalendar.Day(snapshot.simulationSeconds):00} {months[ColonyCalendar.Month(snapshot.simulationSeconds) - 1]}  /  YEAR {ColonyCalendar.Year(snapshot.simulationSeconds):000}";
            var played = TimeSpan.FromSeconds(snapshot.playedSeconds);
            var mode = clock.DecisionPending ? "DECISION PENDING" : clock.Paused ? "PAUSED" : clock.Speed + "x SPEED";
            clockText.text = $"{mode}   |   PLAYED {(int)played.TotalHours:00}:{played.Minutes:00}:{played.Seconds:00}";
            Tone(pauseButton, clock.Paused);
            var speeds = new[] { 1, 3, 10 };
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
            selectedBuilding = building; inspector.SetActive(building != null); RefreshBuilding();
        }
        private void RefreshBuilding()
        {
            if (selectedBuilding == null) { inspector.SetActive(false); return; }
            inspector.SetActive(true);
            var state = selectedBuilding.State; var e = selectedBuilding.Economy; var impact = selectedBuilding.Impact;
            buildingTitle.text = selectedBuilding.DisplayName;
            buildingStats.text = $"TIER {state.level + 1}   /   {(state.offline ? "OFFLINE" : "OPERATIONAL")}\nHousing {e.capacity:0}     Jobs {e.jobs:0}\nOutput / worker  {e.outputPerWorker:0.00}\nUpkeep  {e.upkeep:0.0} / s\nCO2 impact  {impact.co2Delta:+0.00;-0.00;0} / s at full staffing";
            if(IndustryRules.Facility(selectedBuilding.Id) && Industry!=null)
                buildingStats.text=$"TIER {state.level+1} / {(state.offline?"OFFLINE":"OPERATIONAL")}   Jobs {e.jobs:0}\nUpkeep {e.upkeep:0.#}/s\n"+Industry.Status(selectedBuilding);
            for (var i = 0; i < 3; i++)
            {
                focuses[i].GetComponentInChildren<Text>().text = BuildingOperationRules.FocusName(selectedBuilding.Id, i);
                Tone(focuses[i], state.focus == i);
            }
            focusDescription.text = BuildingOperationRules.FocusDescription(selectedBuilding.Id, state.focus);
            upgradeButton.interactable = selectedBuilding.CanUpgrade;
            upgradeLabel.text = state.level >= 2 ? "MAXIMUM TIER" : $"UPGRADE  {selectedBuilding.UpgradeCost:0}";
            operationLabel.text = state.offline ? "RESUME" : "SUSPEND";
        }

        internal void UpdateBuildButtonState(string displayName, bool placementActive)
        {
            if (buildButtonLabel != null) buildButtonLabel.text = placementActive ? "CANCEL DEPLOYMENT  [ESC]" : "DEPLOY STRUCTURE";
            if (structureDetails != null && buildingPlacement != null) structureDetails.text = displayName + "\n" + buildingPlacement.ActiveStructureDetails.Split('\n')[0]+"\nFree placement / terrain supports";
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
                catalog[i].GetComponentInChildren<Text>().text=definition.DisplayName.ToUpperInvariant()+$"\n{definition.ConstructionCost:0}  /  "+(group==1?"EXTRACTION":group==2?"PROCESSING":"COLONY");
            }
            selection.Select(null);buildingPlacement.SelectStructure(visibleIds[0]);
        }
        private void RefreshMaterials()
        {
            if(materialText==null || Industry==null || bootstrap.ActivePlanet==null)return;
            var total=Industry.Totals();var biome=Orbit!=null?bootstrap.ActivePlanet.Biome(Orbit.NavigationDirection):"";
            materialText.text=$"ORE  Fe {total[0]:0}   Cu {total[1]:0}\nSTONE {total[2]:0}   CRYSTAL {total[4]:0}\nBIO {total[3]:0}   METAL {total[5]:0}\nPARTS {total[6]:0}   CARGO {Industry.CargoCount}\n{biome}";
            surveyLabel.text="SURVEY: "+new[]{"OFF","ALL / ROUTES","IRON (RUST)","COPPER (GOLD)","STONE (GREY)","BIOMASS (GREEN)","CRYSTAL (BLUE)"}[Industry.Overlay];
        }
        private void VisitDeposit()
        {
            if(Industry==null || Orbit==null || bootstrap.ActivePlanet==null)return;
            var kind=(MaterialKind)Mathf.Clamp(Industry.Overlay-2,0,4);
            var world=bootstrap.ActivePlanet.transform.TransformPoint(bootstrap.ActivePlanet.EvaluateSurfacePoint(Orbit.NavigationDirection));
            var deposit=Industry.NearestDeposit(world,kind);
            if(deposit!=null){Industry.SetOverlay((int)kind+2);Orbit.FocusLocation(deposit.Direction);AppendEventLog($"Survey: {IndustryRules.Name(kind)}, {deposit.Remaining:0} units.");}
        }
        public void ShowNewGamePanel(bool visible) { newGamePanel.SetActive(visible); saveButton.interactable = GameSettings.HasActiveGame; if(clock!=null)clock.DecisionPending=visible||HasOpenEvent; }
        public void ShowHud(bool visible) { hudPanel.SetActive(visible); if (visible) selection.Select(null); }
        public void ShowEventPanel(bool visible) { eventPanel.SetActive(visible); if (clock != null) clock.DecisionPending = visible || (newGamePanel!=null && newGamePanel.activeSelf); }
        public void SetEventChoiceAInteractable(bool affordable) { choiceA.interactable = affordable; }
        public void SetSeed(int seed) { seedInput.text = seed.ToString(); }
        public void SetDifficulty(GameSettings.Difficulty difficulty)
        {
            selectedDifficulty = difficulty; difficultyLabel.text = "CONDITIONS  /  " + GameSettings.GetDefinition(difficulty).displayName.ToUpperInvariant();
        }
        public void UpdateResourceReadout(ResourceSnapshot state)
        {
            stockpileValue.text = state.Stockpile.ToString("N0"); populationValue.text = state.Population.ToString("N0");
            netValue.text = state.NetProduction.ToString("+0.0;-0.0;0.0"); netValue.color = state.NetProduction >= 0 ? new Color(0.52f, 0.77f, 0.66f) : new Color(0.9f, 0.43f, 0.35f);
            workforceValue.text = state.Workforce.ToString("N0");
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
            eventTitleText.text = title; eventBodyText.text = body; choiceALabel.text = optionAText; choiceBLabel.text = optionBText;
            choiceAHandler = () => { ShowEventPanel(false); optionA?.Invoke(); };
            choiceBHandler = () => { ShowEventPanel(false); optionB?.Invoke(); };
            ShowEventPanel(true);
        }

        private GameObject Panel(string name, Transform parent, Vector2 size, Vector2 position, TextAnchor anchor, bool border = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = size;
            var pivot = anchor == TextAnchor.UpperRight ? new Vector2(1, 1)
                : anchor == TextAnchor.UpperLeft ? new Vector2(0, 1)
                : anchor == TextAnchor.LowerCenter ? new Vector2(0.5f, 0) : new Vector2(0.5f, 0.5f);
            rect.anchorMin = rect.anchorMax = rect.pivot = pivot; rect.anchoredPosition = position;
            go.GetComponent<Image>().color = Surface;
            if (border) { var edge = go.AddComponent<Outline>(); edge.effectColor = new Color(0.39f, 0.34f, 0.23f, 0.7f); edge.effectDistance = new Vector2(1, -1); }
            return go;
        }
        private void StretchWidth(GameObject panel, float inset)
        {
            var rect = panel.GetComponent<RectTransform>();
            var y = rect.anchorMin.y; rect.anchorMin = new Vector2(0, y); rect.anchorMax = new Vector2(1, y);
            rect.pivot = new Vector2(0.5f, rect.pivot.y); rect.sizeDelta = new Vector2(-inset * 2, rect.sizeDelta.y);
            rect.anchoredPosition = new Vector2(0, rect.anchoredPosition.y);
        }
        private Text Label(string name, Transform parent, string content, int size, Vector2 position, float width, float height, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = position;
            var text = go.GetComponent<Text>(); text.font = font; text.fontSize = size; text.color = color; text.text = content;
            text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate; text.supportRichText = true;
            return text;
        }
        private Button Button(string name, Transform parent, Vector2 position, string content, out Text label, float width, float height = 34)
        {
            var go = Panel(name, parent, new Vector2(width, height), position, TextAnchor.UpperLeft);
            go.GetComponent<Image>().color = Color.white;
            var button = go.AddComponent<Button>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors; colors.normalColor = ButtonBase;
            colors.highlightedColor = new Color(0.16f, 0.20f, 0.21f); colors.pressedColor = new Color(0.26f, 0.23f, 0.16f);
            colors.disabledColor = new Color(0.035f, 0.045f, 0.05f); colors.fadeDuration = 0.12f;
            button.colors = colors;
            label = Label("Label", go.transform, content, 14, Vector2.zero, width, height, Ivory);
            label.alignment = TextAnchor.MiddleCenter;
            return button;
        }
        private void Tone(Button button, bool selected)
        {
            if (button == null) return;
            var colors = button.colors; colors.normalColor = selected ? new Color(0.24f, 0.20f, 0.12f) : ButtonBase;
            button.colors = colors;
            var edge = button.GetComponent<Outline>(); if (edge != null) edge.effectColor = selected ? Gold : new Color(0.2f, 0.27f, 0.29f, 0.65f);
        }
#if UNITY_EDITOR
        private void BuildDebug(Transform parent)
        {
            debugPanel = Panel("DebugPanel", parent, new Vector2(232, 206), new Vector2(16, -132), TextAnchor.UpperLeft);
            Label("DebugTitle", debugPanel.transform, "DEVELOPER TOOLS  [F12]", 11, new Vector2(16, -16), 206, 20, Gold);
            Button("AdvanceTick", debugPanel.transform, new Vector2(16, -52), "ADVANCE SETTLEMENT TICK", out _, 200)
                .onClick.AddListener(() => settlement.DebugRunImmediateTick());
            Button("AddStockpile", debugPanel.transform, new Vector2(16, -96), "+100 STOCKPILE", out _, 200)
                .onClick.AddListener(() => resourceSystem.ModifyStockpile(100));
            Button("TriggerEvent", debugPanel.transform, new Vector2(16, -140), "TRIGGER POLICY EVENT", out _, 200)
                .onClick.AddListener(() => colonyEventSystem.TriggerDebugEventNow());
            debugPanel.SetActive(false);
        }
#endif
    }
}
