using System;
using System.Collections.Generic;
using System.Text;
#if UNITY_EDITOR
using System.Globalization;
#endif
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Sim.Environment;
using WH30K.Sim.Events;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;

namespace WH30K.UI
{
    /// <summary>
    /// Runtime generated UI that exposes a lightweight "New Game" menu along with HUD readouts.
    /// Programmatic creation keeps the scene authoring burden low while still enabling designer tweaking
    /// by editing this single script.
    /// </summary>
    [RequireComponent(typeof(PlanetBootstrap))]
    public class NewGameMenu : MonoBehaviour
    {
        private const float PanelWidth = 320f;
        private const float PanelHeight = 210f;
        private const float HudPanelWidth = 260f;
#if UNITY_EDITOR
        private const float DebugPanelWidth = 260f;
#endif

        private PlanetBootstrap bootstrap;
        private ResourceSystem resourceSystem;
        private EnvironmentState environmentState;
        private ColonyEventSystem colonyEventSystem;
        private Settlement settlement;
        private BuildingPlacementController buildingPlacement;

        private Canvas canvas;
        private GameObject newGamePanel;
        private InputField seedInput;
        private Button startButton;
        private Button saveButton;
        private Button loadButton;
        private Button difficultyButton;
        private Text difficultyLabel;
        private GameObject hudPanel;
        private Text resourceText;
        private Text environmentText;
        private Text eventLogText;
        private GameObject eventPanel;
        private Text eventTitleText;
        private Text eventBodyText;
        private Text eventChoiceALabel;
        private Text eventChoiceBLabel;
        private Button eventChoiceAButton;
        private Button eventChoiceBButton;
        private Button buildButton;
        private Text buildButtonLabel;

        private readonly List<string> eventLogEntries = new List<string>();
        private GameSettings.Difficulty selectedDifficulty = GameSettings.Difficulty.Standard;

        private Action eventChoiceAHandler;
        private Action eventChoiceBHandler;

        private Font defaultFont;

#if UNITY_EDITOR
        private GameObject debugPanel;
        private Button debugMarkersToggleButton;
        private Text debugMarkersToggleLabel;
        private InputField debugMarkerCountInput;
        private InputField debugMarkerScaleInput;
        private Button debugMarkersRespawnButton;
        private Button debugMarkersClearButton;
        private Button debugAdvanceTickButton;
        private Button debugAddStockpileButton;
        private Button debugTriggerEventButton;
#endif

        private void Awake()
        {
            bootstrap = GetComponent<PlanetBootstrap>();
            resourceSystem = GetComponent<ResourceSystem>();
            environmentState = GetComponent<EnvironmentState>();
            colonyEventSystem = GetComponent<ColonyEventSystem>();
            settlement = GetComponent<Settlement>();
            buildingPlacement = GetComponent<BuildingPlacementController>();

            EnsureEventSystemExists();
            BuildUI();
            ConfigureDefaultValues();
            ConfigureSystems();
        }

        public void ConfigureSystems()
        {
            bootstrap.ConfigureMenu(this);
            resourceSystem.ConfigureMenu(this);
            environmentState.ConfigureMenu(this);
            colonyEventSystem.ConfigureMenu(this);
            settlement.ConfigureMenu(this);
            buildingPlacement?.ConfigureMenu(this);
        }

        private void EnsureEventSystemExists()
        {
            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var eventSystemGO = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystemGO.name = "UnityEventSystem";
        }

        private void BuildUI()
        {
            defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

            var canvasGO = new GameObject("UI");
            canvasGO.transform.SetParent(transform, false);
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGO.AddComponent<GraphicRaycaster>();

            BuildNewGamePanel(canvas.transform);
            BuildHudPanel(canvas.transform);
            BuildEventPanel(canvas.transform);
#if UNITY_EDITOR
            BuildDebugPanel(canvas.transform);
#endif
        }

        private void ConfigureDefaultValues()
        {
            var randomSeed = UnityEngine.Random.Range(0, int.MaxValue);
            seedInput.text = randomSeed.ToString();
            selectedDifficulty = GameSettings.Difficulty.Standard;
            UpdateDifficultyButtonLabel();
            ShowNewGamePanel(true);
            ShowHud(false);
            ShowEventPanel(false);
#if UNITY_EDITOR
            UpdateDebugPanelState();
#endif
        }

        private void BuildNewGamePanel(Transform parent)
        {
            newGamePanel = CreatePanel("NewGamePanel", parent, new Vector2(PanelWidth, PanelHeight),
                new Vector2(10f, -10f), TextAnchor.UpperLeft);

            CreateLabel("Title", newGamePanel.transform, "New Game", 20, TextAnchor.UpperLeft,
                new Vector2(0f, -10f), PanelWidth - 30f);

            CreateLabel("SeedLabel", newGamePanel.transform, "Seed", 14, TextAnchor.UpperLeft,
                new Vector2(0f, -50f), PanelWidth - 30f);
            seedInput = CreateInputField("SeedInput", newGamePanel.transform, new Vector2(0f, -80f));

            CreateLabel("DifficultyLabel", newGamePanel.transform, "Difficulty", 14, TextAnchor.UpperLeft,
                new Vector2(0f, -120f), PanelWidth - 30f);
            difficultyButton = CreateButton("DifficultyButton", newGamePanel.transform, new Vector2(0f, -150f),
                "Standard", out difficultyLabel, PanelWidth - 40f);
            difficultyButton.onClick.AddListener(CycleDifficulty);

            startButton = CreateButton("StartButton", newGamePanel.transform, new Vector2(0f, -190f),
                "Begin", out _, (PanelWidth - 50f) * 0.5f);
            startButton.onClick.AddListener(OnStartClicked);

            saveButton = CreateButton("SaveButton", newGamePanel.transform,
                new Vector2((PanelWidth * 0.5f) + 5f, -190f), "Save", out _, (PanelWidth - 50f) * 0.5f);
            saveButton.onClick.AddListener(OnSaveClicked);

            loadButton = CreateButton("LoadButton", newGamePanel.transform,
                new Vector2((PanelWidth * 0.5f) + 5f, -150f), "Load", out _, (PanelWidth - 50f) * 0.5f);
            loadButton.onClick.AddListener(OnLoadClicked);
        }

        private void BuildHudPanel(Transform parent)
        {
            hudPanel = CreatePanel("HudPanel", parent, new Vector2(HudPanelWidth, 320f),
                new Vector2(-10f, -10f), TextAnchor.UpperRight);

            resourceText = CreateLabel("ResourceReadout", hudPanel.transform, string.Empty, 14, TextAnchor.UpperLeft,
                new Vector2(-HudPanelWidth + 10f, -20f), HudPanelWidth - 20f);

            environmentText = CreateLabel("EnvironmentReadout", hudPanel.transform, string.Empty, 14, TextAnchor.UpperLeft,
                new Vector2(-HudPanelWidth + 10f, -120f), HudPanelWidth - 20f);

            eventLogText = CreateLabel("EventLog", hudPanel.transform, "Log:\n", 12, TextAnchor.UpperLeft,
                new Vector2(-HudPanelWidth + 10f, -200f), HudPanelWidth - 20f, 120f);

            buildButton = CreateButton("BuildButton", hudPanel.transform, new Vector2(-HudPanelWidth + 10f, -260f),
                "Place Structure", out buildButtonLabel, HudPanelWidth - 20f);
            buildButton.onClick.AddListener(OnBuildButtonClicked);
        }

        private void BuildEventPanel(Transform parent)
        {
            eventPanel = CreatePanel("EventPanel", parent, new Vector2(460f, 200f), new Vector2(0f, 0f), TextAnchor.MiddleCenter);

            eventTitleText = CreateLabel("EventTitle", eventPanel.transform, string.Empty, 18, TextAnchor.UpperCenter,
                new Vector2(0f, -15f), 420f);
            eventBodyText = CreateLabel("EventBody", eventPanel.transform, string.Empty, 14, TextAnchor.UpperLeft,
                new Vector2(-220f, -60f), 420f, 120f);

            eventChoiceAButton = CreateButton("ChoiceA", eventPanel.transform, new Vector2(-140f, -150f),
                "Choice A", out eventChoiceALabel, 180f);
            eventChoiceBButton = CreateButton("ChoiceB", eventPanel.transform, new Vector2(140f, -150f),
                "Choice B", out eventChoiceBLabel, 180f);

            eventChoiceAButton.onClick.AddListener(() => eventChoiceAHandler?.Invoke());
            eventChoiceBButton.onClick.AddListener(() => eventChoiceBHandler?.Invoke());
        }

        private GameObject CreatePanel(string name, Transform parent, Vector2 size, Vector2 anchoredPosition, TextAnchor anchor)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            switch (anchor)
            {
                case TextAnchor.UpperLeft:
                    rect.anchorMin = new Vector2(0f, 1f);
                    rect.anchorMax = new Vector2(0f, 1f);
                    rect.pivot = new Vector2(0f, 1f);
                    break;
                case TextAnchor.UpperRight:
                    rect.anchorMin = new Vector2(1f, 1f);
                    rect.anchorMax = new Vector2(1f, 1f);
                    rect.pivot = new Vector2(1f, 1f);
                    break;
                case TextAnchor.MiddleCenter:
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    break;
                default:
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    break;
            }

            rect.anchoredPosition = anchoredPosition;
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            return panel;
        }

        private Text CreateLabel(string name, Transform parent, string content, int fontSize, TextAnchor alignment,
            Vector2 anchoredPosition, float width, float height = 60f)
        {
            var labelGO = new GameObject(name, typeof(RectTransform));
            labelGO.transform.SetParent(parent, false);
            var rect = labelGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;

            var text = labelGO.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.font = defaultFont;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private InputField CreateInputField(string name, Transform parent, Vector2 anchoredPosition, float width = PanelWidth - 40f,
            InputField.ContentType contentType = InputField.ContentType.IntegerNumber, string placeholderText = "Random")
        {
            var inputGO = new GameObject(name, typeof(RectTransform), typeof(Image));
            inputGO.transform.SetParent(parent, false);
            var rect = inputGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, 32f);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;

            var image = inputGO.GetComponent<Image>();
            image.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(inputGO.transform, false);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 6f);
            textRect.offsetMax = new Vector2(-10f, -6f);

            var text = textGO.AddComponent<Text>();
            text.font = defaultFont;
            text.fontSize = 14;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;

            var placeholderGO = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGO.transform.SetParent(inputGO.transform, false);
            var placeholderRect = placeholderGO.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(10f, 6f);
            placeholderRect.offsetMax = new Vector2(-10f, -6f);

            var placeholder = placeholderGO.AddComponent<Text>();
            placeholder.font = defaultFont;
            placeholder.fontSize = 14;
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.text = placeholderText;

            var inputField = inputGO.AddComponent<InputField>();
            inputField.textComponent = text;
            inputField.placeholder = placeholder;
            inputField.contentType = contentType;
            return inputField;
        }

        private Button CreateButton(string name, Transform parent, Vector2 anchoredPosition, string label,
            out Text labelText, float width)
        {
            var buttonGO = new GameObject(name, typeof(RectTransform), typeof(Image));
            buttonGO.transform.SetParent(parent, false);
            var rect = buttonGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, 36f);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;

            var image = buttonGO.GetComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

            var button = buttonGO.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = image.color;
            colors.highlightedColor = new Color(0.3f, 0.3f, 0.3f, 0.95f);
            colors.pressedColor = new Color(0.1f, 0.1f, 0.1f, 1f);
            button.colors = colors;

            var textGO = new GameObject("Label", typeof(RectTransform));
            textGO.transform.SetParent(buttonGO.transform, false);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            labelText = textGO.AddComponent<Text>();
            labelText.font = defaultFont;
            labelText.fontSize = 16;
            labelText.color = Color.white;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.text = label;

            return button;
        }

        private void CycleDifficulty()
        {
            var order = GameSettings.GetAllDefinitions();
            var index = 0;
            for (var i = 0; i < order.Count; i++)
            {
                if (order[i].difficulty == selectedDifficulty)
                {
                    index = i;
                    break;
                }
            }

            index = (index + 1) % order.Count;
            selectedDifficulty = order[index].difficulty;
            UpdateDifficultyButtonLabel();
        }

        private void UpdateDifficultyButtonLabel()
        {
            var definition = GameSettings.GetDefinition(selectedDifficulty);
            difficultyLabel.text = definition.displayName;
        }

        private void OnStartClicked()
        {
            var seed = UnityEngine.Random.Range(0, int.MaxValue);
            if (!string.IsNullOrWhiteSpace(seedInput.text) && int.TryParse(seedInput.text, out var parsedSeed))
            {
                seed = parsedSeed;
            }

            bootstrap.BeginNewGame(seed, selectedDifficulty);
        }

        private void OnSaveClicked()
        {
            bootstrap.SaveToFile();
        }

        private void OnLoadClicked()
        {
            bootstrap.LoadFromFile();
        }

        private void OnBuildButtonClicked()
        {
            if (buildingPlacement == null)
            {
                return;
            }

            if (buildingPlacement.IsPlacing)
            {
                buildingPlacement.CancelPlacement();
            }
            else
            {
                buildingPlacement.BeginPlacement();
            }
        }

        internal void UpdateBuildButtonState(string displayName, bool placementActive)
        {
            if (buildButtonLabel != null)
            {
                buildButtonLabel.text = placementActive ? "Cancel Placement" : $"Place: {displayName}";
            }

            if (buildButton == null)
            {
                return;
            }

            var image = buildButton.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

            var baseColor = placementActive
                ? new Color(0.45f, 0.2f, 0.2f, 0.95f)
                : new Color(0.2f, 0.2f, 0.2f, 0.9f);
            image.color = baseColor;

            var colors = buildButton.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = placementActive
                ? new Color(0.55f, 0.3f, 0.3f, 1f)
                : new Color(0.3f, 0.3f, 0.3f, 0.95f);
            colors.pressedColor = placementActive
                ? new Color(0.35f, 0.15f, 0.15f, 1f)
                : new Color(0.1f, 0.1f, 0.1f, 1f);
            buildButton.colors = colors;
        }

        public void ShowNewGamePanel(bool visible)
        {
            newGamePanel.SetActive(visible);
            saveButton.interactable = !visible;
            loadButton.interactable = true;
        }

        public void ShowHud(bool visible)
        {
            hudPanel.SetActive(visible);
#if UNITY_EDITOR
            UpdateDebugPanelState();
#endif
        }

        public void ShowEventPanel(bool visible)
        {
            eventPanel.SetActive(visible);
        }


        public void SetDifficulty(GameSettings.Difficulty difficulty)
        {
            selectedDifficulty = difficulty;
            UpdateDifficultyButtonLabel();
        }

        public void SetSeed(int seed)
        {
            seedInput.text = seed.ToString();
        }

        public void UpdateResourceReadout(ResourceSnapshot snapshot)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Resources");
            builder.AppendLine($"Population: {snapshot.Population:0}");
            builder.AppendLine($"Workforce: {snapshot.Workforce:0}");
            builder.AppendLine($"Production: {snapshot.ProductionPerCycle:0.0}");
            builder.AppendLine($"Upkeep: {snapshot.UpkeepPerCycle:0.0}");
            builder.AppendLine($"Net: {snapshot.NetProduction:0.0}");
            builder.Append($"Stockpile: {snapshot.Stockpile:0.0}");
            resourceText.text = builder.ToString();
        }

        public void UpdateEnvironmentReadout(EnvironmentSnapshot snapshot)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Environment");
            builder.AppendLine($"CO₂ Level: {snapshot.Co2:0.0}");
            builder.AppendLine($"O₂ Level: {snapshot.O2:0.0}");
            builder.AppendLine($"Water Pollution: {snapshot.WaterPollution:0.0}");
            builder.Append($"Δ Global Temp: {snapshot.GlobalTemperatureOffset:0.00}°C");
            environmentText.text = builder.ToString();
        }

        public void AppendEventLog(string logEntry)
        {
            eventLogEntries.Add(logEntry);
            while (eventLogEntries.Count > 6)
            {
                eventLogEntries.RemoveAt(0);
            }

            var builder = new StringBuilder();
            builder.AppendLine("Log:");
            foreach (var entry in eventLogEntries)
            {
                builder.AppendLine(entry);
            }

            eventLogText.text = builder.ToString();
        }

        public void PromptEvent(string title, string body, string optionAText, Action optionA, string optionBText, Action optionB)
        {
            eventTitleText.text = title;
            eventBodyText.text = body;
            eventChoiceALabel.text = optionAText;
            eventChoiceBLabel.text = optionBText;

            eventChoiceAHandler = () =>
            {
                ShowEventPanel(false);
                optionA?.Invoke();
            };

            eventChoiceBHandler = () =>
            {
                ShowEventPanel(false);
                optionB?.Invoke();
            };

            ShowEventPanel(true);
        }

#if UNITY_EDITOR
        private void BuildDebugPanel(Transform parent)
        {
            debugPanel = CreatePanel("DebugPanel", parent, new Vector2(DebugPanelWidth, 320f),
                new Vector2(10f, -PanelHeight - 30f), TextAnchor.UpperLeft);

            CreateLabel("DebugTitle", debugPanel.transform, "Debug Tools", 18, TextAnchor.UpperLeft,
                new Vector2(0f, -12f), DebugPanelWidth - 20f);

            debugMarkersToggleButton = CreateButton("ToggleMarkers", debugPanel.transform, new Vector2(0f, -46f),
                "Debug Markers", out debugMarkersToggleLabel, DebugPanelWidth - 40f);
            debugMarkersToggleButton.onClick.AddListener(ToggleDebugMarkers);

            debugMarkerCountInput = CreateInputField("MarkerCount", debugPanel.transform, new Vector2(0f, -86f),
                DebugPanelWidth - 40f, InputField.ContentType.IntegerNumber, "Marker Count");
            debugMarkerCountInput.onEndEdit.AddListener(OnDebugMarkerCountChanged);

            debugMarkerScaleInput = CreateInputField("MarkerScale", debugPanel.transform, new Vector2(0f, -126f),
                DebugPanelWidth - 40f, InputField.ContentType.DecimalNumber, "Marker Scale");
            debugMarkerScaleInput.onEndEdit.AddListener(OnDebugMarkerScaleChanged);

            debugMarkersRespawnButton = CreateButton("RespawnMarkers", debugPanel.transform, new Vector2(0f, -166f),
                "Respawn Markers", out _, (DebugPanelWidth - 50f) * 0.5f);
            debugMarkersRespawnButton.onClick.AddListener(OnDebugMarkersRespawnClicked);

            debugMarkersClearButton = CreateButton("ClearMarkers", debugPanel.transform,
                new Vector2((DebugPanelWidth * 0.5f) + 5f, -166f), "Clear Markers", out _, (DebugPanelWidth - 50f) * 0.5f);
            debugMarkersClearButton.onClick.AddListener(OnDebugMarkersClearClicked);

            debugAdvanceTickButton = CreateButton("AdvanceTick", debugPanel.transform, new Vector2(0f, -206f),
                "Advance Settlement Tick", out _, DebugPanelWidth - 40f);
            debugAdvanceTickButton.onClick.AddListener(OnDebugAdvanceTickClicked);

            debugAddStockpileButton = CreateButton("AddStockpile", debugPanel.transform, new Vector2(0f, -246f),
                "Inject +100 Stockpile", out _, DebugPanelWidth - 40f);
            debugAddStockpileButton.onClick.AddListener(OnDebugAddStockpileClicked);

            debugTriggerEventButton = CreateButton("TriggerEvent", debugPanel.transform, new Vector2(0f, -286f),
                "Trigger Event", out _, DebugPanelWidth - 40f);
            debugTriggerEventButton.onClick.AddListener(OnDebugTriggerEventClicked);

            UpdateDebugPanelState();
        }

        private void UpdateDebugPanelState()
        {
            if (debugPanel == null)
            {
                return;
            }

            var hasActiveGame = GameSettings.HasActiveGame;
            var markersEnabled = hasActiveGame && bootstrap != null && bootstrap.DebugMarkersEnabled;

            if (debugMarkersToggleLabel != null)
            {
                var label = markersEnabled ? "Debug Markers: On" : "Debug Markers: Off";
                if (debugMarkersToggleLabel.text != label)
                {
                    debugMarkersToggleLabel.text = label;
                }
            }

            if (bootstrap != null)
            {
                var countText = bootstrap.DebugMarkerCount.ToString(CultureInfo.InvariantCulture);
                if (debugMarkerCountInput != null && debugMarkerCountInput.text != countText)
                {
                    debugMarkerCountInput.text = countText;
                }

                var scaleText = bootstrap.DebugMarkerScale.ToString("0.##", CultureInfo.InvariantCulture);
                if (debugMarkerScaleInput != null && debugMarkerScaleInput.text != scaleText)
                {
                    debugMarkerScaleInput.text = scaleText;
                }
            }

            if (debugMarkersToggleButton != null)
            {
                debugMarkersToggleButton.interactable = hasActiveGame && bootstrap != null;
            }

            if (debugMarkerCountInput != null)
            {
                debugMarkerCountInput.interactable = hasActiveGame && bootstrap != null;
            }

            if (debugMarkerScaleInput != null)
            {
                debugMarkerScaleInput.interactable = hasActiveGame && bootstrap != null;
            }

            if (debugMarkersRespawnButton != null)
            {
                debugMarkersRespawnButton.interactable = hasActiveGame && bootstrap != null && bootstrap.DebugMarkersEnabled;
            }

            if (debugMarkersClearButton != null)
            {
                var hasMarkers = hasActiveGame && bootstrap != null && bootstrap.HasDebugMarkers;
                debugMarkersClearButton.interactable = hasMarkers;
            }

            if (debugAdvanceTickButton != null)
            {
                debugAdvanceTickButton.interactable = hasActiveGame && settlement != null && settlement.HasActiveSimulation;
            }

            if (debugAddStockpileButton != null)
            {
                debugAddStockpileButton.interactable = hasActiveGame && resourceSystem != null;
            }

            if (debugTriggerEventButton != null)
            {
                debugTriggerEventButton.interactable = hasActiveGame && colonyEventSystem != null && colonyEventSystem.HasActiveSession;
            }
        }

        private void ToggleDebugMarkers()
        {
            if (!GameSettings.HasActiveGame || bootstrap == null)
            {
                UpdateDebugPanelState();
                return;
            }

            bootstrap.DebugMarkersEnabled = !bootstrap.DebugMarkersEnabled;
            UpdateDebugPanelState();
        }

        private void OnDebugMarkerCountChanged(string value)
        {
            if (!GameSettings.HasActiveGame || bootstrap == null)
            {
                UpdateDebugPanelState();
                return;
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            {
                bootstrap.DebugMarkerCount = count;
            }

            UpdateDebugPanelState();
        }

        private void OnDebugMarkerScaleChanged(string value)
        {
            if (!GameSettings.HasActiveGame || bootstrap == null)
            {
                UpdateDebugPanelState();
                return;
            }

            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
            {
                bootstrap.DebugMarkerScale = scale;
            }

            UpdateDebugPanelState();
        }

        private void OnDebugMarkersRespawnClicked()
        {
            if (!GameSettings.HasActiveGame || bootstrap == null)
            {
                UpdateDebugPanelState();
                return;
            }

            bootstrap.DebugMarkersEnabled = true;
            bootstrap.DebugRespawnMarkers();
            UpdateDebugPanelState();
        }

        private void OnDebugMarkersClearClicked()
        {
            if (!GameSettings.HasActiveGame || bootstrap == null)
            {
                UpdateDebugPanelState();
                return;
            }

            bootstrap.DebugClearMarkers();
            UpdateDebugPanelState();
        }

        private void OnDebugAdvanceTickClicked()
        {
            if (!GameSettings.HasActiveGame || settlement == null || !settlement.HasActiveSimulation)
            {
                UpdateDebugPanelState();
                return;
            }

            settlement.DebugRunImmediateTick();
            AppendEventLog("[DEBUG] Advanced settlement tick.");
            UpdateDebugPanelState();
        }

        private void OnDebugAddStockpileClicked()
        {
            if (!GameSettings.HasActiveGame || resourceSystem == null)
            {
                UpdateDebugPanelState();
                return;
            }

            resourceSystem.ModifyStockpile(100f);
            AppendEventLog("[DEBUG] Injected +100 stockpile.");
            UpdateDebugPanelState();
        }

        private void OnDebugTriggerEventClicked()
        {
            if (!GameSettings.HasActiveGame || colonyEventSystem == null || !colonyEventSystem.HasActiveSession)
            {
                UpdateDebugPanelState();
                return;
            }

            colonyEventSystem.TriggerDebugEventNow();
            AppendEventLog("[DEBUG] Manually triggered event.");
            UpdateDebugPanelState();
        }
#endif
    }
}
