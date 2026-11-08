using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Sim;

namespace WH30K.UI
{
    public partial class NewGameMenu
    {
        private GameObject mainMenuPanel, saveBrowserPanel, settingsPanel, exitConfirmPanel, deleteConfirmPanel;
        private Transform saveRows;
        private Text saveSelectionText, menuStatusText;
        private Button loadSelectedSaveButton, deleteSelectedSaveButton;
        private string selectedSavePath, pendingDeletePath;
        private int pendingResolution, pendingVSync, pendingFrameRate, pendingQuality, pendingTextureQuality, pendingShadows, pendingMenuAnimation;
        private bool pendingFullscreen;
        private bool mainMenuRequestedVisible;
        private float pendingMasterVolume;
        private Resolution[] availableResolutions;
        private readonly int[] frameRates = { 30, 60, 120, -1 };
        private CinematicMenuEnvironment menuEnvironment;
        private Font authorityFont;
        private bool authorityFontResolved;
        private Texture2D authorityCrest;
        private static readonly Color Brass = CommandUi.Brass;
        private static readonly Color Parchment = CommandUi.Parchment;
        private readonly Dictionary<GameObject, Coroutine> panelFades = new Dictionary<GameObject, Coroutine>();

        [Serializable]
        private sealed class SaveListingHeader
        {
            public int version;
            public int seed;
            public GameSettings.Difficulty difficulty;
            public ClockSnapshot clock;
        }

        private void BuildCinematicMainMenu(Transform parent)
        {
            mainMenuPanel = new GameObject("CinematicMainMenu", typeof(RectTransform), typeof(CanvasGroup));
            mainMenuPanel.transform.SetParent(parent, false);
            var full = mainMenuPanel.GetComponent<RectTransform>();
            full.anchorMin = Vector2.zero; full.anchorMax = Vector2.one; full.offsetMin = full.offsetMax = Vector2.zero;
            var environment = new GameObject("ForgeWorldVista", typeof(RectTransform));
            environment.transform.SetParent(parent, false);
            menuEnvironment = environment.AddComponent<CinematicMenuEnvironment>();
            menuEnvironment.SetAnimationQuality(PlayerPrefs.GetInt("WH30K_MenuAnimation", 1));

            var identity = Panel("AuthorityTitlePlate", environment.transform, new Vector2(660, 100),
                new Vector2(22, -18), TextAnchor.UpperLeft, false);
            identity.GetComponent<Image>().enabled = false;
            AddCrest(identity.transform, new Vector2(0, -16), 116);
            MenuLabel("MainIdentity", identity.transform, "WH30K / COLONY COMMAND", 29, new Vector2(126, -13), 514, 40, Parchment);
            MenuLabel("MainSubtitle", identity.transform, "PLANETARY EXPEDITION AUTHORITY", 15, new Vector2(128, -52), 500, 28, Parchment);
            Ornament("IdentityRule", identity.transform, new Vector2(128, -79), new Vector2(405, 1), Brass);
            environment.SetActive(false);

            var strip = Panel("CommandConsole", mainMenuPanel.transform, new Vector2(934, 86), new Vector2(0, 21), TextAnchor.LowerCenter, false);
            StyleMenuCard(strip);
            var labels = new[] { "CONTINUE COLONY", "NEW EXPEDITION", "LOAD GAME", "SETTINGS", "EXIT GAME" };
            var widths = new[] { 186f, 196f, 162f, 156f, 154f };
            var actions = new Action[] { ContinueColony, () => ShowNewGamePanel(true), OpenSaveBrowser, OpenSettings, OpenExitConfirmation };
            var x = 16f;
            for (var i = 0; i < labels.Length; i++)
            {
                var button = MenuButton("Menu_" + i, strip.transform, new Vector2(x, -13), labels[i], widths[i], 43, i == 1);
                var action = actions[i]; button.onClick.AddListener(() => action());
                x += widths[i] + 12;
            }
            menuStatusText = MenuLabel("MenuStatus", strip.transform, "", 10, new Vector2(16, -63), 902, 17, Parchment);
            menuStatusText.alignment = TextAnchor.MiddleCenter;
            BuildSaveBrowser(parent);
            BuildSettings(parent);
            BuildConfirmations(parent);
            RefreshContinueState();
        }

        private void RefreshMenuBackdrop()
        {
            if (menuEnvironment != null)
                menuEnvironment.gameObject.SetActive(mainMenuRequestedVisible || NewGameScreenRequestedVisible);
        }

        private Font AuthorityFont
        {
            get
            {
                if (!authorityFontResolved)
                {
                    authorityFontResolved = true;
                    var candidates = new[] { "Georgia", "Times New Roman", "Liberation Serif", "DejaVu Serif" };
                    var installed = Font.GetOSInstalledFontNames();
                    var family = candidates.FirstOrDefault(installed.Contains);
                    if (family != null) authorityFont = Font.CreateDynamicFontFromOSFont(family, 32);
                }
                return authorityFont != null ? authorityFont : font;
            }
        }

        private Text MenuLabel(string name, Transform parent, string text, int size, Vector2 position,
            float width, float height, Color color)
        {
            var label = Label(name, parent, text, size, position, width, height, color);
            label.font = AuthorityFont;
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = Color.black * .95f;
            shadow.effectDistance = new Vector2(1, -1);
            return label;
        }

        private void AddCrest(Transform parent, Vector2 position, float width)
        {
            if (authorityCrest == null) authorityCrest = Resources.Load<Texture2D>("Menu/AuthorityCrest");
            if (authorityCrest == null) return;
            AddMenuTexture("AuthorityCrest", parent, authorityCrest, position,
                new Vector2(width, width * authorityCrest.height / authorityCrest.width));
        }

        private static void AddMenuTexture(string name, Transform parent, Texture2D texture, Vector2 position, Vector2 size)
        {
            var art = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            art.transform.SetParent(parent, false);
            var image = art.GetComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private RectTransform Ornament(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
        {
            var piece = Panel(name, parent, size, pos, TextAnchor.UpperLeft, false);
            var image = piece.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image.rectTransform;
        }

        private void BrassFrame(GameObject panel, Color edge)
        {
            var rect = panel.GetComponent<RectTransform>();
            var w = rect.sizeDelta.x;
            var h = rect.sizeDelta.y;
            Ornament("UpperBrass", panel.transform, Vector2.zero, new Vector2(w, 2), edge);
            Ornament("LowerBrass", panel.transform, new Vector2(0, 2 - h), new Vector2(w, 2), edge);
            Ornament("LeftBrass", panel.transform, Vector2.zero, new Vector2(2, h), edge);
            Ornament("RightBrass", panel.transform, new Vector2(w - 2, 0), new Vector2(2, h), edge);
            for (var i = 0; i < 4; i++)
            {
                var pin = Ornament("BrassRivet", panel.transform, new Vector2(i % 2 == 0 ? 5 : w - 9, i < 2 ? -5 : 9 - h),
                    new Vector2(4, 4), edge);
                pin.localEulerAngles = new Vector3(0, 0, 45);
            }
        }

        private void StyleMenuCard(GameObject card)
        {
            var cardTint = CommandUi.Iron; cardTint.a = .965f;
            card.GetComponent<Image>().color = cardTint;
            var shadow = card.AddComponent<Shadow>();
            shadow.effectColor = Color.black * .72f;
            shadow.effectDistance = new Vector2(4, -7);
            BrassFrame(card, Brass);
            var size = card.GetComponent<RectTransform>().sizeDelta;
            var trim = Ornament("InsetTrim", card.transform, new Vector2(6, -6), size - new Vector2(12, 12), Color.clear);
            BrassFrame(trim.gameObject, CommandUi.Brass);
        }

        private Button MenuButton(string name, Transform parent, Vector2 position, string text, float width, float height, bool primary = false)
        {
            var button = Button(name, parent, position, text, out var label, width, height);
            SkinMenuButton(button, primary);
            label.fontSize = 16;
            return button;
        }

        private void SkinMenuButton(Button button, bool primary = false)
        {
            var label = button.GetComponentInChildren<Text>();
            label.font = AuthorityFont;
            label.color = primary ? CommandUi.Parchment : CommandUi.SecondaryText;
            var outline = button.GetComponent<Outline>();
            outline.effectColor = CommandUi.Brass;
            BrassFrame(button.gameObject, primary ? CommandUi.Brass : CommandUi.SecondaryText);
            var width = ((RectTransform)button.transform).sizeDelta.x;
            Ornament("BevelHighlight", button.transform, new Vector2(7, -3), new Vector2(width - 14, 1),
                primary ? CommandUi.Parchment : CommandUi.Brass);
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            CommandUi.Tone(button, primary);
        }

        private void ContinueColony()
        {
            try
            {
                var path = bootstrap.DefaultSaveFilePath;
                if (bootstrap.CanLoadSaveFile(path)) bootstrap.LoadFromFilePath(path);
            }
            catch (Exception e) { Debug.LogWarning($"Could not access the latest colony save: {e.Message}"); }
        }

        private void RefreshContinueState()
        {
            if (mainMenuPanel == null) return;
            var button = mainMenuPanel.transform.Find("CommandConsole/Menu_0")?.GetComponent<Button>();
            var available = false;
            try { available = bootstrap.CanLoadSaveFile(bootstrap.DefaultSaveFilePath); }
            catch (Exception) { available = false; }
            if (button != null) button.interactable = available;
            if (menuStatusText != null)
                menuStatusText.text = available ? "LAST COLONY AVAILABLE  /  RESUME OR DEPLOY A NEW EXPEDITION" : "NO VALID COLONY RECORD  /  AWAITING EXPEDITION AUTHORITY";
        }

        public void ShowMainMenu(bool visible)
        {
            if (mainMenuPanel == null) return;
            mainMenuRequestedVisible = visible;
            RefreshMenuBackdrop();
            FadePanel(mainMenuPanel, visible);
            if (visible) { ShowHud(false); ShowNewGamePanel(false); RefreshContinueState(); }
            if (visible && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(mainMenuPanel.transform.Find("CommandConsole/Menu_1")?.gameObject);
            if (clock != null) clock.DecisionPending = visible || NewGameScreenRequestedVisible || HasOpenEvent;
        }

        internal void OpenExitConfirmation()
        {
            FadePanel(exitConfirmPanel, true);
            FocusMenuControl(exitConfirmPanel, "CancelAction");
        }

        private void FocusMenuControl(GameObject panel, string control)
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(panel.transform.Find("ConfirmationCard/" + control)?.gameObject);
        }

        private void BuildSaveBrowser(Transform parent)
        {
            saveBrowserPanel = MakeModalRoot("SaveBrowser", parent);
            var card = Panel("SaveBrowserCard", saveBrowserPanel.transform, new Vector2(760, 520), Vector2.zero, TextAnchor.MiddleCenter);
            StyleMenuCard(card);
            MenuLabel("SaveBrowserTitle", card.transform, "COLONY ARCHIVE", 25, new Vector2(28, -20), 660, 38, Ivory);
            MenuLabel("SaveBrowserSub", card.transform, "RECORDED EXPEDITIONS  /  SELECT A COMPATIBLE TRANSMISSION", 10, new Vector2(30, -61), 680, 20, Gold);
            var viewport = new GameObject("SaveListViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewport.transform.SetParent(card.transform, false);
            var vr = viewport.GetComponent<RectTransform>(); vr.anchorMin = vr.anchorMax = new Vector2(0, 1); vr.pivot = new Vector2(0, 1); vr.anchoredPosition = new Vector2(30, -96); vr.sizeDelta = new Vector2(700, 285);
            var archiveTint = CommandUi.Field; archiveTint.a = .7f;
            viewport.GetComponent<Image>().color = archiveTint;
            var content = new GameObject("SaveList", typeof(RectTransform)); content.transform.SetParent(viewport.transform, false);
            var cr = content.GetComponent<RectTransform>(); cr.anchorMin = new Vector2(0, 1); cr.anchorMax = new Vector2(1, 1); cr.pivot = new Vector2(.5f, 1); cr.sizeDelta = new Vector2(0, 0);
            saveRows = content.transform;
            var scroll = viewport.GetComponent<ScrollRect>(); scroll.viewport = vr; scroll.content = cr; scroll.horizontal = false; scroll.vertical = true;
            saveSelectionText = Label("SelectedSaveDetails", card.transform, "Select a save to view its recorded details.", 12, new Vector2(30, -391), 700, 40, Muted);
            loadSelectedSaveButton = Button("LoadSelectedSave", card.transform, new Vector2(30, -456), "LOAD SELECTED", out _, 175, 38);
            loadSelectedSaveButton.onClick.AddListener(LoadSelectedSave);
            deleteSelectedSaveButton = Button("DeleteSelectedSave", card.transform, new Vector2(218, -456), "DELETE", out _, 130, 38);
            deleteSelectedSaveButton.onClick.AddListener(AskDeleteSave);
            Button("RefreshSaves", card.transform, new Vector2(360, -456), "REFRESH", out _, 130, 38).onClick.AddListener(RefreshSaveList);
            Button("OpenSaveFolder", card.transform, new Vector2(502, -456), "OPEN SAVE FOLDER", out _, 198, 38).onClick.AddListener(OpenSaveFolder);
            Button("CloseSaveBrowser", card.transform, new Vector2(660, -18), "X", out _, 36, 30).onClick.AddListener(CloseSaveBrowser);
            foreach (var button in card.GetComponentsInChildren<Button>(true)) SkinMenuButton(button);
            saveBrowserPanel.SetActive(false);
        }

        private GameObject MakeModalRoot(string name, Transform parent)
        {
            var root = CommandUi.Screen(name, parent, .78f);
            root.AddComponent<CanvasGroup>();
            root.GetComponent<CanvasGroup>().alpha = 0;
            return root;
        }

        private void OpenSaveBrowser()
        {
            FadePanel(mainMenuPanel, false); FadePanel(saveBrowserPanel, true); RefreshSaveList();
            var focus = saveRows.childCount > 0 ? saveRows.GetChild(0).gameObject : saveBrowserPanel.transform.Find("SaveBrowserCard/RefreshSaves")?.gameObject;
            if (focus != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(focus);
        }

        private void CloseSaveBrowser()
        {
            if (saveBrowserPanel != null) FadePanel(saveBrowserPanel, false);
            ShowMainMenu(true);
        }

        private void RefreshSaveList()
        {
            foreach (Transform child in saveRows) Destroy(child.gameObject);
            selectedSavePath = null;
            loadSelectedSaveButton.interactable = deleteSelectedSaveButton.interactable = false;
            string[] files;
            try { files = Directory.GetFiles(bootstrap.SaveDirectoryPath, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).ToArray(); }
            catch (Exception)
            {
                saveSelectionText.text = "The colony archive directory is unavailable. Check its access permissions and refresh.";
                return;
            }
            if (files.Length == 0)
            {
                var empty = Label("NoSaves", saveRows, "NO RECORDED EXPEDITIONS FOUND", 12, Vector2.zero, 650, 30, Muted);
                empty.rectTransform.anchorMin = empty.rectTransform.anchorMax = new Vector2(0, 1); empty.rectTransform.pivot = new Vector2(0, 1); empty.rectTransform.anchoredPosition = new Vector2(12, -12);
                return;
            }
            for (var i = 0; i < files.Length; i++)
            {
                var path = files[i]; SaveListingHeader header = null; string detail;
                try { header = JsonUtility.FromJson<SaveListingHeader>(File.ReadAllText(path)); } catch (Exception) { }
                if (header != null && header.version > 0)
                {
                    var played = TimeSpan.FromSeconds(Math.Max(0, header.clock.playedSeconds));
                    var difficulty = Enum.IsDefined(typeof(GameSettings.Difficulty), header.difficulty)
                        ? GameSettings.GetDefinition(header.difficulty).displayName.ToUpperInvariant() : "UNKNOWN CONDITIONS";
                    detail = $"SCHEMA {header.version}   /   SEED {header.seed}   /   {difficulty}   /   PLAYED {(int)played.TotalHours:00}:{played.Minutes:00}";
                }
                else detail = "UNREADABLE OR UNRECOGNIZED SAVE  /  LOAD VALIDATION WILL BE REQUIRED";
                var button = Button("SaveRow_" + i, saveRows, new Vector2(12, -8 - i * 68),
                    Path.GetFileNameWithoutExtension(path), out var fileLabel, 670, 62);
                var row = button.gameObject;
                fileLabel.name = "SaveFile";
                fileLabel.alignment = TextAnchor.UpperLeft;
                fileLabel.rectTransform.anchoredPosition = new Vector2(12, -7);
                fileLabel.rectTransform.sizeDelta = new Vector2(630, 20);
                var captured = path;
                button.onClick.AddListener(() => SelectSave(captured));
                Label("SaveMetadata", row.transform, $"{File.GetLastWriteTime(path):dd MMM yyyy  HH:mm}   /   {detail}", 9, new Vector2(12, -32), 642, 17, Gold);
            }
            var rect = saveRows.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(0, files.Length * 68 + 8);
        }

        private void SelectSave(string path)
        {
            selectedSavePath = path;
            loadSelectedSaveButton.interactable = deleteSelectedSaveButton.interactable = true;
            try
            {
                var header = JsonUtility.FromJson<SaveListingHeader>(File.ReadAllText(path));
                if (header == null || header.version <= 0) saveSelectionText.text = "This file could not be read as a colony save. The loader will validate it before replacing the current session.";
                else saveSelectionText.text = $"{Path.GetFileName(path)}   /   VERSION {header.version}   /   SEED {header.seed}   /   LAST UPDATED {File.GetLastWriteTime(path):dd MMM yyyy HH:mm}";
            }
            catch (Exception) { saveSelectionText.text = "The selected file is inaccessible or malformed."; }
        }

        private void LoadSelectedSave()
        {
            if (string.IsNullOrEmpty(selectedSavePath)) return;
            if (bootstrap.LoadFromFilePath(selectedSavePath)) saveBrowserPanel.SetActive(false);
        }

        private void AskDeleteSave()
        {
            if (string.IsNullOrEmpty(selectedSavePath)) return;
            pendingDeletePath = selectedSavePath;
            FadePanel(deleteConfirmPanel, true); FocusMenuControl(deleteConfirmPanel, "CancelAction");
        }

        private void DeleteSelectedSave()
        {
            try { if (!string.IsNullOrEmpty(pendingDeletePath) && File.Exists(pendingDeletePath)) File.Delete(pendingDeletePath); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Debug.LogWarning($"Delete failed: {e.Message}"); }
            pendingDeletePath = null; FadePanel(deleteConfirmPanel, false); RefreshSaveList();
        }

        private void OpenSaveFolder()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(bootstrap.SaveDirectoryPath) { UseShellExecute = true }); }
            catch (Exception e) { Debug.LogWarning($"Could not open save folder: {e.Message}"); }
        }

        private void BuildSettings(Transform parent)
        {
            settingsPanel = MakeModalRoot("SettingsPanel", parent);
            var card = Panel("SettingsCard", settingsPanel.transform, new Vector2(660, 590), Vector2.zero, TextAnchor.MiddleCenter);
            StyleMenuCard(card);
            MenuLabel("SettingsTitle", card.transform, "SYSTEM CONFIGURATION", 24, new Vector2(28, -20), 590, 38, Ivory);
            MenuLabel("SettingsCategories", card.transform, "DISPLAY  /  GRAPHICS  /  AUDIO", 10, new Vector2(30, -61), 590, 20, Gold);
            AddSettingsRow(card.transform, "Resolution", -108, () => CycleResolution());
            AddSettingsRow(card.transform, "Display mode", -153, () => pendingFullscreen = !pendingFullscreen);
            AddSettingsRow(card.transform, "Vertical sync", -198, () => pendingVSync = (pendingVSync + 1) % 3);
            AddSettingsRow(card.transform, "Frame rate limit", -243, () => pendingFrameRate = (pendingFrameRate + 1) % frameRates.Length);
            AddSettingsRow(card.transform, "Graphics quality", -288, () => pendingQuality = (pendingQuality + 1) % QualitySettings.names.Length);
            AddSettingsRow(card.transform, "Texture quality", -333, () => pendingTextureQuality = (pendingTextureQuality + 1) % 4);
            AddSettingsRow(card.transform, "Shadows", -378, () => pendingShadows = (pendingShadows + 1) % 3);
            AddSettingsRow(card.transform, "Menu animation", -423, () => pendingMenuAnimation = (pendingMenuAnimation + 1) % 3);
            Label("MasterVolumeLabel", card.transform, "MASTER VOLUME", 11, new Vector2(30, -465), 185, 24, Ivory);
            var sliderGo = new GameObject("MasterVolume", typeof(RectTransform), typeof(Slider)); sliderGo.transform.SetParent(card.transform, false);
            var sr = sliderGo.GetComponent<RectTransform>(); sr.anchorMin = sr.anchorMax = new Vector2(0, 1); sr.pivot = new Vector2(0, 1); sr.anchoredPosition = new Vector2(250, -464); sr.sizeDelta = new Vector2(360, 24);
            var track = Panel("Track", sliderGo.transform, new Vector2(360, 8), new Vector2(0, -8), TextAnchor.UpperLeft, false); track.GetComponent<Image>().color = ButtonBase;
            var fill = Panel("Fill", track.transform, new Vector2(360, 8), Vector2.zero, TextAnchor.UpperLeft, false); fill.GetComponent<Image>().color = Gold;
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            var travel = CommandUi.Panel("HandleTravel", sliderGo.transform, new Vector2(342, 24), new Vector2(9, 0), TextAnchor.UpperLeft, false, false);
            travel.GetComponent<Image>().enabled = false;
            var handle = Panel("Handle", travel.transform, new Vector2(18, 24), Vector2.zero, TextAnchor.MiddleCenter, false);
            var slider = sliderGo.GetComponent<Slider>(); slider.minValue = 0; slider.maxValue = 1; slider.value = PlayerPrefs.GetFloat("WH30K_MasterVolume", 1f); pendingMasterVolume = slider.value;
            slider.fillRect = fillRect; slider.handleRect = handle.GetComponent<RectTransform>();
            CommandUi.StyleControl(slider, handle.GetComponent<Image>(), useBrassHandle: true);
            slider.onValueChanged.AddListener(v => pendingMasterVolume = v);
            Button("SettingsApply", card.transform, new Vector2(30, -520), "APPLY", out _, 140, 38).onClick.AddListener(ApplySettings);
            Button("SettingsCancel", card.transform, new Vector2(184, -520), "CANCEL", out _, 140, 38).onClick.AddListener(CloseSettings);
            Button("SettingsDefaults", card.transform, new Vector2(338, -520), "RESTORE DEFAULTS", out _, 170, 38).onClick.AddListener(RestoreSettingsDefaults);
            Button("SettingsClose", card.transform, new Vector2(590, -18), "X", out _, 36, 30).onClick.AddListener(CloseSettings);
            foreach (var button in card.GetComponentsInChildren<Button>(true)) SkinMenuButton(button);
            settingsPanel.SetActive(false); LoadSettingsValues();
        }

        private void AddSettingsRow(Transform parent, string title, float y, Action cycle)
        {
            Label("Setting_" + title, parent, title.ToUpperInvariant(), 11, new Vector2(30, y), 190, 28, Ivory);
            var button = Button("SettingValue_" + title, parent, new Vector2(250, y + 2), "", out var label, 360, 30);
            label.color = Gold;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button.onClick.AddListener(() => { cycle(); RefreshSettingsLabels(); });
            button.name = "SettingValue_" + title.Replace(" ", "");
        }

        private void OpenSettings()
        {
            FadePanel(mainMenuPanel, false); LoadSettingsValues(); FadePanel(settingsPanel, true); RefreshSettingsLabels();
            EventSystem.current?.SetSelectedGameObject(settingsPanel.transform.Find("SettingsCard/SettingValue_Resolution")?.gameObject);
        }

        private void LoadSettingsValues()
        {
            availableResolutions = Screen.resolutions;
            pendingResolution = Mathf.Clamp(PlayerPrefs.GetInt("WH30K_ResolutionIndex", availableResolutions.Length - 1), 0, Mathf.Max(0, availableResolutions.Length - 1));
            pendingFullscreen = PlayerPrefs.GetInt("WH30K_Fullscreen", Screen.fullScreen ? 1 : 0) != 0;
            pendingVSync = Mathf.Clamp(PlayerPrefs.GetInt("WH30K_VSync", QualitySettings.vSyncCount), 0, 2);
            pendingFrameRate = Mathf.Clamp(PlayerPrefs.GetInt("WH30K_FrameRate", 1), 0, frameRates.Length - 1);
            pendingQuality = Mathf.Clamp(PlayerPrefs.GetInt("WH30K_Quality", QualitySettings.GetQualityLevel()), 0, QualitySettings.names.Length - 1);
            pendingTextureQuality = Mathf.Clamp(PlayerPrefs.GetInt("WH30K_TextureQuality", QualitySettings.masterTextureLimit), 0, 3);
            pendingShadows = Mathf.Clamp(PlayerPrefs.GetInt("WH30K_Shadows", (int)QualitySettings.shadows), 0, 2);
            pendingMenuAnimation = Mathf.Clamp(PlayerPrefs.GetInt("WH30K_MenuAnimation", 1), 0, 2);
            pendingMasterVolume = PlayerPrefs.GetFloat("WH30K_MasterVolume", AudioListener.volume);
            QualitySettings.vSyncCount = pendingVSync; Application.targetFrameRate = frameRates[pendingFrameRate];
            QualitySettings.SetQualityLevel(pendingQuality, true); QualitySettings.masterTextureLimit = pendingTextureQuality;
            QualitySettings.shadows = (ShadowQuality)pendingShadows; AudioListener.volume = pendingMasterVolume;
            menuEnvironment?.SetAnimationQuality(pendingMenuAnimation);
            if (PlayerPrefs.HasKey("WH30K_Fullscreen") && availableResolutions.Length > 0)
            {
                var r = availableResolutions[pendingResolution]; Screen.SetResolution(r.width, r.height, pendingFullscreen);
            }
        }

        private void RefreshSettingsLabels()
        {
            SetSettingValue("Resolution", ResolutionLabel()); SetSettingValue("Displaymode", pendingFullscreen ? "FULLSCREEN" : "WINDOWED");
            SetSettingValue("Verticalsync", pendingVSync == 0 ? "OFF" : pendingVSync == 1 ? "1 FRAME" : "2 FRAMES");
            SetSettingValue("Frameratelimit", frameRates[pendingFrameRate] < 0 ? "UNLIMITED" : frameRates[pendingFrameRate] + " FPS");
            SetSettingValue("Graphicsquality", QualitySettings.names[pendingQuality]);
            SetSettingValue("Texturequality", new[] { "FULL", "HALF", "QUARTER", "EIGHTH" }[pendingTextureQuality]);
            SetSettingValue("Shadows", new[] { "OFF", "HARD ONLY", "ALL" }[pendingShadows]);
            SetSettingValue("Menuanimation", new[] { "LOW", "MEDIUM", "HIGH" }[pendingMenuAnimation]);
        }

        private void SetSettingValue(string name, string value)
        {
            var button = settingsPanel.transform.Find("SettingsCard/SettingValue_" + name)?.GetComponent<Button>();
            if (button != null && button.GetComponentInChildren<Text>() != null) button.GetComponentInChildren<Text>().text = value;
        }

        private string ResolutionLabel()
        {
            if (availableResolutions == null || availableResolutions.Length == 0) return Screen.width + " x " + Screen.height;
            var r = availableResolutions[pendingResolution]; return $"{r.width} x {r.height}";
        }

        private void CycleResolution() { if (availableResolutions.Length > 0) pendingResolution = (pendingResolution + 1) % availableResolutions.Length; }

        private void ApplySettings()
        {
            QualitySettings.vSyncCount = pendingVSync; Application.targetFrameRate = frameRates[pendingFrameRate];
            QualitySettings.SetQualityLevel(pendingQuality, true); QualitySettings.masterTextureLimit = pendingTextureQuality;
            QualitySettings.shadows = (ShadowQuality)pendingShadows; AudioListener.volume = pendingMasterVolume;
            menuEnvironment?.SetAnimationQuality(pendingMenuAnimation);
            if (availableResolutions.Length > 0) { var r = availableResolutions[pendingResolution]; Screen.SetResolution(r.width, r.height, pendingFullscreen); }
            else Screen.fullScreen = pendingFullscreen;
            PlayerPrefs.SetInt("WH30K_ResolutionIndex", pendingResolution); PlayerPrefs.SetInt("WH30K_Fullscreen", pendingFullscreen ? 1 : 0);
            PlayerPrefs.SetInt("WH30K_VSync", pendingVSync); PlayerPrefs.SetInt("WH30K_FrameRate", pendingFrameRate); PlayerPrefs.SetInt("WH30K_Quality", pendingQuality);
            PlayerPrefs.SetInt("WH30K_TextureQuality", pendingTextureQuality); PlayerPrefs.SetInt("WH30K_Shadows", pendingShadows); PlayerPrefs.SetInt("WH30K_MenuAnimation", pendingMenuAnimation);
            PlayerPrefs.SetFloat("WH30K_MasterVolume", pendingMasterVolume); PlayerPrefs.Save(); CloseSettings();
        }

        private void RestoreSettingsDefaults()
        {
            pendingResolution = Mathf.Max(0, availableResolutions.Length - 1); pendingFullscreen = true; pendingVSync = 1; pendingFrameRate = 1;
            pendingQuality = Mathf.Max(0, QualitySettings.names.Length - 1); pendingTextureQuality = 0; pendingShadows = 2;
            pendingMenuAnimation = 1; pendingMasterVolume = 1f; RefreshSettingsLabels();
            var slider = settingsPanel.transform.Find("SettingsCard/MasterVolume")?.GetComponent<Slider>(); if (slider != null) slider.value = 1f;
        }

        private void CloseSettings()
        {
            if (settingsPanel != null) FadePanel(settingsPanel, false);
            ShowMainMenu(true);
        }

        private void FadePanel(GameObject panel, bool show)
        {
            if (panel == null) return;
            if (panelFades.TryGetValue(panel, out var running) && running != null) StopCoroutine(running);
            var group = panel.GetComponent<CanvasGroup>();
            if (group == null) { panel.SetActive(show); return; }
            if (show)
            {
                panel.SetActive(true); group.interactable = false; group.blocksRaycasts = false;
                if (group.alpha >= 1f) { group.interactable = true; group.blocksRaycasts = true; return; }
            }
            else if (!panel.activeSelf) return;
            panelFades[panel] = StartCoroutine(FadePanelRoutine(panel, group, show));
        }

        private IEnumerator FadePanelRoutine(GameObject panel, CanvasGroup group, bool show)
        {
            var start = group.alpha; var target = show ? 1f : 0f; const float duration = .18f; var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime; var t = Mathf.Clamp01(elapsed / duration); t = t * t * (3f - 2f * t);
                group.alpha = Mathf.Lerp(start, target, t); yield return null;
            }
            group.alpha = target; group.interactable = show; group.blocksRaycasts = show;
            if (!show) panel.SetActive(false);
            panelFades.Remove(panel);
        }

        private void BuildConfirmations(Transform parent)
        {
            exitConfirmPanel = BuildConfirmation(parent, "ExitConfirm", "END TRANSMISSION?", "Exit the application and return to the void?", "EXIT", () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
            deleteConfirmPanel = BuildConfirmation(parent, "DeleteConfirm", "DELETE COLONY RECORD?", "This permanently removes the selected save file.", "DELETE RECORD", DeleteSelectedSave);
        }

        private GameObject BuildConfirmation(Transform parent, string name, string title, string body, string confirm, Action action)
        {
            var root = MakeModalRoot(name, parent); var card = Panel("ConfirmationCard", root.transform, new Vector2(500, 245), Vector2.zero, TextAnchor.MiddleCenter); StyleMenuCard(card);
            MenuLabel("ConfirmationTitle", card.transform, title, 21, new Vector2(24, -24), 450, 34, Ivory);
            Label("ConfirmationBody", card.transform, body, 13, new Vector2(24, -78), 450, 56, Muted);
            var accept = Button("ConfirmAction", card.transform, new Vector2(24, -168), confirm, out _, 210, 40);
            accept.navigation = new Navigation { mode = Navigation.Mode.Automatic }; accept.onClick.AddListener(() => action());
            var cancel = Button("CancelAction", card.transform, new Vector2(254, -168), "CANCEL", out _, 210, 40);
            cancel.navigation = new Navigation { mode = Navigation.Mode.Automatic }; cancel.onClick.AddListener(() => FadePanel(root, false));
            SkinMenuButton(accept, true); SkinMenuButton(cancel);
            root.SetActive(false); return root;
        }
    }
}
