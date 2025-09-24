using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay;
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
        private const float DebugPanelHeight = 400f;

        private PlanetBootstrap bootstrap;
        private ResourceSystem resourceSystem;
        private EnvironmentState environmentState;
        private ColonyEventSystem colonyEventSystem;
        private Settlement settlement;

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

        private GameObject debugPanel;
        private Toggle spawnMarkersToggle;
        private Slider markerCountSlider;
        private Text markerCountValueLabel;
        private Slider markerScaleSlider;
        private Text markerScaleValueLabel;
        private Slider markerRedSlider;
        private Slider markerGreenSlider;
        private Slider markerBlueSlider;
        private Text markerRedValueLabel;
        private Text markerGreenValueLabel;
        private Text markerBlueValueLabel;
        private Button regenerateMarkersButton;
        private bool suppressDebugControlCallbacks;

        private readonly List<string> eventLogEntries = new List<string>();
        private GameSettings.Difficulty selectedDifficulty = GameSettings.Difficulty.Standard;

        private Action eventChoiceAHandler;
        private Action eventChoiceBHandler;

        private Font defaultFont;

        private void Awake()
        {
            bootstrap = GetComponent<PlanetBootstrap>();
            resourceSystem = GetComponent<ResourceSystem>();
            environmentState = GetComponent<EnvironmentState>();
            colonyEventSystem = GetComponent<ColonyEventSystem>();
            settlement = GetComponent<Settlement>();

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
            BuildDebugPanel(canvas.transform);
            BuildHudPanel(canvas.transform);
            BuildEventPanel(canvas.transform);
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

        private void BuildDebugPanel(Transform parent)
        {
            debugPanel = CreatePanel("DebugPanel", parent, new Vector2(PanelWidth, DebugPanelHeight),
                new Vector2(10f, -(PanelHeight + 20f)), TextAnchor.UpperLeft);

            CreateLabel("DebugTitle", debugPanel.transform, "Surface Markers", 18, TextAnchor.UpperLeft,
                new Vector2(0f, -10f), PanelWidth - 30f, 24f);

            spawnMarkersToggle = CreateToggle("SpawnMarkersToggle", debugPanel.transform, new Vector2(0f, -50f),
                "Show debug markers");
            spawnMarkersToggle.onValueChanged.AddListener(OnSpawnMarkersToggleChanged);

            CreateLabel("MarkerCountLabel", debugPanel.transform, "Marker count", 14, TextAnchor.UpperLeft,
                new Vector2(0f, -90f), PanelWidth - 30f, 20f);
            markerCountValueLabel = CreateLabel("MarkerCountValue", debugPanel.transform, "0", 14, TextAnchor.MiddleRight,
                new Vector2(PanelWidth - 70f, -90f), 60f, 20f);
            markerCountSlider = CreateSlider("MarkerCountSlider", debugPanel.transform, new Vector2(0f, -120f),
                PanelWidth - 40f);
            markerCountSlider.minValue = 0f;
            markerCountSlider.maxValue = 64f;
            markerCountSlider.wholeNumbers = true;
            markerCountSlider.onValueChanged.AddListener(OnMarkerCountSliderChanged);

            CreateLabel("MarkerScaleLabel", debugPanel.transform, "Marker scale", 14, TextAnchor.UpperLeft,
                new Vector2(0f, -160f), PanelWidth - 30f, 20f);
            markerScaleValueLabel = CreateLabel("MarkerScaleValue", debugPanel.transform, "0", 14, TextAnchor.MiddleRight,
                new Vector2(PanelWidth - 70f, -160f), 60f, 20f);
            markerScaleSlider = CreateSlider("MarkerScaleSlider", debugPanel.transform, new Vector2(0f, -190f),
                PanelWidth - 40f);
            markerScaleSlider.minValue = 5f;
            markerScaleSlider.maxValue = 150f;
            markerScaleSlider.wholeNumbers = false;
            markerScaleSlider.value = markerScaleSlider.minValue;
            markerScaleSlider.onValueChanged.AddListener(OnMarkerScaleSliderChanged);

            CreateLabel("MarkerColorLabel", debugPanel.transform, "Marker color", 14, TextAnchor.UpperLeft,
                new Vector2(0f, -230f), PanelWidth - 30f, 20f);
            markerRedSlider = CreateColorSlider("MarkerColorRSlider", debugPanel.transform, new Vector2(30f, -260f),
                out markerRedValueLabel, "R");
            markerGreenSlider = CreateColorSlider("MarkerColorGSlider", debugPanel.transform,
                new Vector2(30f, -290f), out markerGreenValueLabel, "G");
            markerBlueSlider = CreateColorSlider("MarkerColorBSlider", debugPanel.transform, new Vector2(30f, -320f),
                out markerBlueValueLabel, "B");
            markerRedSlider.onValueChanged.AddListener(OnMarkerColorSliderChanged);
            markerGreenSlider.onValueChanged.AddListener(OnMarkerColorSliderChanged);
            markerBlueSlider.onValueChanged.AddListener(OnMarkerColorSliderChanged);

            regenerateMarkersButton = CreateButton("RegenerateMarkersButton", debugPanel.transform,
                new Vector2(0f, -360f), "Respawn markers", out _, PanelWidth - 40f);
            regenerateMarkersButton.onClick.AddListener(OnRegenerateMarkersClicked);

            UpdateMarkerCountLabel(markerCountSlider.value);
            UpdateMarkerScaleLabel(markerScaleSlider.value);
            UpdateMarkerColorLabels();
            UpdateDebugMarkerControlInteractivity();
        }

        private void BuildHudPanel(Transform parent)
        {
            hudPanel = CreatePanel("HudPanel", parent, new Vector2(HudPanelWidth, 260f),
                new Vector2(-10f, -10f), TextAnchor.UpperRight);

            resourceText = CreateLabel("ResourceReadout", hudPanel.transform, string.Empty, 14, TextAnchor.UpperLeft,
                new Vector2(-HudPanelWidth + 10f, -20f), HudPanelWidth - 20f);

            environmentText = CreateLabel("EnvironmentReadout", hudPanel.transform, string.Empty, 14, TextAnchor.UpperLeft,
                new Vector2(-HudPanelWidth + 10f, -120f), HudPanelWidth - 20f);

            eventLogText = CreateLabel("EventLog", hudPanel.transform, "Log:\n", 12, TextAnchor.UpperLeft,
                new Vector2(-HudPanelWidth + 10f, -200f), HudPanelWidth - 20f, 120f);
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
                    break;
                case TextAnchor.UpperRight:
                    rect.anchorMin = new Vector2(1f, 1f);
                    rect.anchorMax = new Vector2(1f, 1f);
                    break;
                case TextAnchor.MiddleCenter:
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    break;
                default:
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
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

        private InputField CreateInputField(string name, Transform parent, Vector2 anchoredPosition)
        {
            var inputGO = new GameObject(name, typeof(RectTransform), typeof(Image));
            inputGO.transform.SetParent(parent, false);
            var rect = inputGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(PanelWidth - 40f, 32f);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
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
            placeholder.text = "Random";

            var inputField = inputGO.AddComponent<InputField>();
            inputField.textComponent = text;
            inputField.placeholder = placeholder;
            inputField.contentType = InputField.ContentType.IntegerNumber;
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

        private Toggle CreateToggle(string name, Transform parent, Vector2 anchoredPosition, string label)
        {
            var toggleGO = new GameObject(name, typeof(RectTransform));
            toggleGO.transform.SetParent(parent, false);
            var rect = toggleGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(24f, 24f);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;

            var backgroundGO = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundGO.transform.SetParent(toggleGO.transform, false);
            var backgroundRect = backgroundGO.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            var backgroundImage = backgroundGO.GetComponent<Image>();
            backgroundImage.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

            var checkmarkGO = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkmarkGO.transform.SetParent(backgroundGO.transform, false);
            var checkmarkRect = checkmarkGO.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = new Vector2(0.25f, 0.25f);
            checkmarkRect.anchorMax = new Vector2(0.75f, 0.75f);
            checkmarkRect.offsetMin = Vector2.zero;
            checkmarkRect.offsetMax = Vector2.zero;
            var checkmarkImage = checkmarkGO.GetComponent<Image>();
            checkmarkImage.color = new Color(0.35f, 0.8f, 0.35f, 0.95f);
            checkmarkImage.raycastTarget = false;

            var toggle = toggleGO.AddComponent<Toggle>();
            toggle.targetGraphic = backgroundImage;
            toggle.graphic = checkmarkImage;

            var colors = toggle.colors;
            colors.normalColor = backgroundImage.color;
            colors.highlightedColor = new Color(0.3f, 0.3f, 0.3f, 0.95f);
            colors.pressedColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            toggle.colors = colors;

            CreateLabel($"{name}Label", parent, label, 14, TextAnchor.MiddleLeft,
                new Vector2(anchoredPosition.x + 30f, anchoredPosition.y), PanelWidth - 70f, 24f);

            return toggle;
        }

        private Slider CreateSlider(string name, Transform parent, Vector2 anchoredPosition, float width)
        {
            var sliderGO = new GameObject(name, typeof(RectTransform));
            sliderGO.transform.SetParent(parent, false);
            var rect = sliderGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, 24f);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;

            var backgroundGO = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundGO.transform.SetParent(sliderGO.transform, false);
            var backgroundRect = backgroundGO.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            var backgroundImage = backgroundGO.GetComponent<Image>();
            backgroundImage.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            var fillAreaGO = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaGO.transform.SetParent(sliderGO.transform, false);
            var fillAreaRect = fillAreaGO.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.75f);
            fillAreaRect.offsetMin = new Vector2(6f, 0f);
            fillAreaRect.offsetMax = new Vector2(-16f, 0f);

            var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGO.transform.SetParent(fillAreaGO.transform, false);
            var fillRect = fillGO.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImage = fillGO.GetComponent<Image>();
            fillImage.color = new Color(0.35f, 0.55f, 0.95f, 0.9f);
            fillImage.raycastTarget = false;

            var handleAreaGO = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleAreaGO.transform.SetParent(sliderGO.transform, false);
            var handleAreaRect = handleAreaGO.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = new Vector2(0f, 0f);
            handleAreaRect.anchorMax = new Vector2(1f, 1f);
            handleAreaRect.offsetMin = new Vector2(6f, 0f);
            handleAreaRect.offsetMax = new Vector2(-6f, 0f);

            var handleGO = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGO.transform.SetParent(handleAreaGO.transform, false);
            var handleRect = handleGO.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(16f, 28f);
            handleRect.anchorMin = new Vector2(0f, 0.5f);
            handleRect.anchorMax = new Vector2(0f, 0.5f);
            handleRect.anchoredPosition = Vector2.zero;
            var handleImage = handleGO.GetComponent<Image>();
            handleImage.color = Color.white;

            var slider = sliderGO.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.transition = Selectable.Transition.ColorTint;

            var colors = slider.colors;
            colors.normalColor = handleImage.color;
            colors.highlightedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            slider.colors = colors;

            return slider;
        }

        private Slider CreateColorSlider(string name, Transform parent, Vector2 anchoredPosition, out Text valueLabel,
            string channelLabel)
        {
            CreateLabel($"{name}Label", parent, channelLabel, 14, TextAnchor.MiddleLeft,
                new Vector2(anchoredPosition.x - 30f, anchoredPosition.y), 24f, 20f);
            valueLabel = CreateLabel($"{name}Value", parent, "0", 14, TextAnchor.MiddleRight,
                new Vector2(PanelWidth - 70f, anchoredPosition.y), 60f, 20f);
            var slider = CreateSlider(name, parent, anchoredPosition, PanelWidth - 110f);
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            return slider;
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

        private void OnSpawnMarkersToggleChanged(bool isOn)
        {
            UpdateDebugMarkerControlInteractivity();
            if (suppressDebugControlCallbacks)
            {
                return;
            }

            bootstrap?.SetSpawnDebugMarkers(isOn);
        }

        private void OnMarkerCountSliderChanged(float value)
        {
            UpdateMarkerCountLabel(value);
            if (suppressDebugControlCallbacks)
            {
                return;
            }

            bootstrap?.SetDebugMarkerCount(Mathf.RoundToInt(value));
        }

        private void OnMarkerScaleSliderChanged(float value)
        {
            UpdateMarkerScaleLabel(value);
            if (suppressDebugControlCallbacks)
            {
                return;
            }

            bootstrap?.SetDebugMarkerScale(value);
        }

        private void OnMarkerColorSliderChanged(float value)
        {
            UpdateMarkerColorLabels();
            if (suppressDebugControlCallbacks)
            {
                return;
            }

            var color = new Color(
                markerRedSlider != null ? markerRedSlider.value : 0f,
                markerGreenSlider != null ? markerGreenSlider.value : 0f,
                markerBlueSlider != null ? markerBlueSlider.value : 0f);
            bootstrap?.SetDebugMarkerColor(color);
        }

        private void OnRegenerateMarkersClicked()
        {
            if (suppressDebugControlCallbacks)
            {
                return;
            }

            bootstrap?.RegenerateDebugMarkers();
        }

        private void UpdateMarkerCountLabel(float value)
        {
            if (markerCountValueLabel != null)
            {
                markerCountValueLabel.text = Mathf.RoundToInt(value).ToString();
            }
        }

        private void UpdateMarkerScaleLabel(float value)
        {
            if (markerScaleValueLabel != null)
            {
                markerScaleValueLabel.text = value.ToString("0.0");
            }
        }

        private void UpdateMarkerColorLabels()
        {
            if (markerRedValueLabel != null && markerRedSlider != null)
            {
                markerRedValueLabel.text = Mathf.RoundToInt(markerRedSlider.value * 255f).ToString();
            }

            if (markerGreenValueLabel != null && markerGreenSlider != null)
            {
                markerGreenValueLabel.text = Mathf.RoundToInt(markerGreenSlider.value * 255f).ToString();
            }

            if (markerBlueValueLabel != null && markerBlueSlider != null)
            {
                markerBlueValueLabel.text = Mathf.RoundToInt(markerBlueSlider.value * 255f).ToString();
            }
        }

        private void UpdateDebugMarkerControlInteractivity()
        {
            var interactive = spawnMarkersToggle != null && spawnMarkersToggle.isOn;

            if (markerCountSlider != null)
            {
                markerCountSlider.interactable = interactive;
            }

            if (markerScaleSlider != null)
            {
                markerScaleSlider.interactable = interactive;
            }

            if (markerRedSlider != null)
            {
                markerRedSlider.interactable = interactive;
            }

            if (markerGreenSlider != null)
            {
                markerGreenSlider.interactable = interactive;
            }

            if (markerBlueSlider != null)
            {
                markerBlueSlider.interactable = interactive;
            }

            if (regenerateMarkersButton != null)
            {
                regenerateMarkersButton.interactable = interactive;
            }
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
        }

        public void ShowEventPanel(bool visible)
        {
            eventPanel.SetActive(visible);
        }

        public void InitializeDebugMarkerControls(bool spawn, int count, float scale, Color color)
        {
            if (spawnMarkersToggle == null)
            {
                return;
            }

            suppressDebugControlCallbacks = true;

            spawnMarkersToggle.isOn = spawn;

            if (markerCountSlider != null)
            {
                markerCountSlider.value = Mathf.Clamp(count, markerCountSlider.minValue, markerCountSlider.maxValue);
                UpdateMarkerCountLabel(markerCountSlider.value);
            }

            if (markerScaleSlider != null)
            {
                markerScaleSlider.value = Mathf.Clamp(scale, markerScaleSlider.minValue, markerScaleSlider.maxValue);
                UpdateMarkerScaleLabel(markerScaleSlider.value);
            }

            if (markerRedSlider != null)
            {
                markerRedSlider.value = Mathf.Clamp01(color.r);
            }

            if (markerGreenSlider != null)
            {
                markerGreenSlider.value = Mathf.Clamp01(color.g);
            }

            if (markerBlueSlider != null)
            {
                markerBlueSlider.value = Mathf.Clamp01(color.b);
            }

            UpdateMarkerColorLabels();

            suppressDebugControlCallbacks = false;
            UpdateDebugMarkerControlInteractivity();
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
    }
}
