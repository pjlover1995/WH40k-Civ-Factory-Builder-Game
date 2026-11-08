using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WH30K.CameraSystem;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Sim.Settlements;
using WH30K.UI;

public static class MainMenuSceneChecks
{
    private const string BatchKey = "WH30K.MainMenuChecks.Batch";
    private static int checks;
    private static bool passed;

    [MenuItem("WH30K/Run Cinematic Main Menu Checks (Play Mode)")]
    public static void RunBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
        SessionState.SetBool(BatchKey, true);
        EditorApplication.playModeStateChanged += BatchState;
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void ResumeBatch()
    {
        if (!SessionState.GetBool(BatchKey, false)) return;
        EditorApplication.playModeStateChanged += BatchState;
        if (EditorApplication.isPlaying) EditorApplication.delayCall += FinishBatch;
    }

    private static void BatchState(PlayModeStateChange state)
    { if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += FinishBatch; }

    private static void FinishBatch()
    {
        if (!SessionState.GetBool(BatchKey, false)) return;
        SessionState.EraseBool(BatchKey); EditorApplication.playModeStateChanged -= BatchState;
        var menu = UnityEngine.Object.FindObjectOfType<NewGameMenu>();
        if (menu == null) { Debug.LogError("Main menu check could not find NewGameMenu."); SceneCheckExit.Exit(1); return; }
        menu.StartCoroutine(CheckMenuFlow(menu));
    }

    private static IEnumerator CheckMenuFlow(NewGameMenu menu)
    {
        yield return null;
        checks = 0;
        passed = true;
        CheckPortraitCardAction();
        Check(GameObject.Find("CinematicMainMenu")?.activeSelf == true, "title menu is the initial screen");
        var vista = GameObject.Find("ForgeWorldVista");
        var plate = GameObject.Find("ForgeWorldPlate")?.GetComponent<RawImage>();
        Check(plate != null && plate.texture != null && !plate.raycastTarget, "the reference plate loads and passes input to the live menu");
        Check(plate != null && plate.material.shader.name == "WH30K/Menu/ForgeVista" && plate.material.shader.isSupported,
            "the built-in pipeline uses the forge plate shader");
        Check(FindButton("Menu_0")?.navigation.mode == Navigation.Mode.Automatic, "continue action exposes keyboard/gamepad navigation");
        var bootstrap = menu.GetComponent<PlanetBootstrap>();
        Check(FindButton("Menu_0")?.interactable == bootstrap.CanLoadSaveFile(bootstrap.DefaultSaveFilePath), "continue availability uses the full migration-aware save validator");
        Check(FindButton("Menu_1")?.navigation.mode == Navigation.Mode.Automatic, "main actions expose keyboard/gamepad navigation");
        CaptureReview(menu, "title");

        Check(SubmitButton(FindButton("Menu_1")), "keyboard/gamepad submit reaches the selected new expedition button through EventSystem");
        yield return new WaitForSecondsRealtime(.24f);
        Check(GameObject.Find("NewGamePanel")?.activeSelf == true, "new expedition opens its separate configuration screen");
        Check(vista != null && vista.activeInHierarchy && GameObject.Find("ExpeditionCard")?.transform.Find("AuthorityCrest") != null,
            "expedition uses the same plate and the independent brass crest");
        var seed = GameObject.Find("SeedInput")?.GetComponent<InputField>();
        if (seed != null) seed.text = "-1";
        FindButton("RandomSeed")?.onClick.Invoke();
        Check(seed != null && int.TryParse(seed.text, out var generated) && generated >= 0, "randomise replaces the seed in the live input");
        CaptureReview(menu, "expedition");
        Check(PointerClickButton(FindButton("BackToAuthority")), "pointer click returns from expedition configuration through EventSystem");
        yield return new WaitForSecondsRealtime(.24f);
        Check(GameObject.Find("CinematicMainMenu")?.activeSelf == true, "expedition configuration returns to the title menu");

        FindButton("Menu_2")?.onClick.Invoke();
        yield return new WaitForSecondsRealtime(.24f);
        Check(GameObject.Find("SaveBrowser")?.activeSelf == true, "load game opens the save browser");
        Check(FindButton("OpenSaveFolder") != null, "save browser exposes its persistent folder action");
        if (Environment.GetCommandLineArgs().Contains("-wh30k-panel-review")) CaptureReview(menu, "save-picker");
        FindButton("CloseSaveBrowser")?.onClick.Invoke();
        yield return new WaitForSecondsRealtime(.24f);
        Check(GameObject.Find("CinematicMainMenu")?.activeSelf == true, "save browser returns to the title menu");

        FindButton("Menu_3")?.onClick.Invoke();
        yield return new WaitForSecondsRealtime(.24f);
        Check(GameObject.Find("SettingsPanel")?.activeSelf == true, "settings opens from the main console");
        var volume = GameObject.Find("MasterVolume")?.GetComponent<Slider>();
        Check(volume != null, "master volume is connected to a persisted audio control");
        if (volume != null)
        {
            var previousVolume = volume.value;
            foreach (var value in new[] { volume.minValue, volume.maxValue })
            {
                volume.SetValueWithoutNotify(value);
                Canvas.ForceUpdateCanvases();
                var control = (RectTransform)volume.transform;
                var fill = RectTransformUtility.CalculateRelativeRectTransformBounds(control, volume.fillRect);
                var handle = RectTransformUtility.CalculateRelativeRectTransformBounds(control, volume.handleRect);
                Check(fill.max.x <= control.rect.xMax + 1f && handle.min.x >= control.rect.xMin - 1f &&
                    handle.max.x <= control.rect.xMax + 1f, $"volume visuals stay inside the control at {value}");
            }
            volume.SetValueWithoutNotify(previousVolume);
        }
        Check(FindButton("SettingValue_Resolution") != null, "resolution control is available");
        Check(FindButton("SettingValue_Texturequality") != null, "texture quality control is available");
        Check(FindButton("SettingValue_Shadows") != null, "shadow mode control is available");
        Check(FindButton("SettingValue_Menuanimation") != null, "menu animation density control is available");
        if (Environment.GetCommandLineArgs().Contains("-wh30k-panel-review")) CaptureReview(menu, "settings");
        FindButton("SettingsCancel")?.onClick.Invoke();
        yield return new WaitForSecondsRealtime(.24f);
        Check(GameObject.Find("CinematicMainMenu")?.activeSelf == true, "settings cancel returns to the title menu");
        menu.ShowMainMenu(false);
        Check(vista != null && !vista.activeSelf, "the menu art and animation stop when leaving the menu");

        menu.ShowMainMenu(true);
        yield return new WaitForSecondsRealtime(.24f);
        FindButton("Menu_1")?.onClick.Invoke();
        yield return new WaitForSecondsRealtime(.24f);
        var fixedSeed = GameObject.Find("SeedInput")?.GetComponent<InputField>();
        if (fixedSeed != null) fixedSeed.text = "1234";
        FindButton("StartButton")?.onClick.Invoke();
        var arrivalDeadline = Time.realtimeSinceStartup + 4.5f;
        while (Time.realtimeSinceStartup < arrivalDeadline) yield return null;
        Check(GameObject.Find("DiscoveryPanel")?.activeSelf == true
            && GameObject.Find("DiscoveryTitle")?.GetComponent<Text>().text.Contains("CAPTAIN") == true,
            "warp exit opens the captain's stable-orbit damage report");
        FindButton("DiscoveryAction")?.onClick.Invoke();
        yield return null;
        Check(bootstrap.ChoosingLanding && GameObject.Find("LandingSelection") != null,
            "acknowledging the captain opens the themed landing card");
        Check(Camera.main.GetComponent<SimpleOrbitCamera>().IsSurfaceView,
            "landing selection frames the surveyed surface beneath its transparent site panel");
        var landingPanel = GameObject.Find("LandingSelection");
        var tenderPreview = GameObject.Find("Landing vessel preview");
        Check(landingPanel.GetComponent<Image>().color.a < .6f && tenderPreview != null
            && tenderPreview.GetComponent<Renderer>().sharedMaterial.GetFloat("_Mode") == 3f
            && tenderPreview.GetComponent<Renderer>().sharedMaterial.color.a < .25f,
            "the landing panel and tender preview remain translucent over the surface");
        for (var frame = 0; frame < 60; frame++) yield return null;
        CaptureReview(menu, "landing", true);
        FindButton("LandColonyShip")?.onClick.Invoke();
        yield return new WaitForSecondsRealtime(.35f);
        FindButton("DeclineColonyGuide")?.onClick.Invoke();
        yield return new WaitForSecondsRealtime(.35f);
        Check(GameObject.Find("CommandHeader") != null && GameObject.Find("ConstructionDeck") != null,
            "touchdown reaches the themed colony HUD");
        menu.ReportVoxResearch("Field Survey");
        yield return new WaitForSecondsRealtime(.30f);
        Check(GameObject.Find("VoxPortraitCard") != null, "the themed HUD displays a vox portrait");
        CaptureReview(menu, "colony-vox", true);

        if (Environment.GetCommandLineArgs().Contains("-wh30k-panel-review")) yield return CheckPanels(menu, bootstrap);

        Debug.Log($"Cinematic main menu checks finished: {checks} assertions.");
        SceneCheckExit.Exit(passed ? 0 : 1);
    }

    private static IEnumerator CheckPanels(NewGameMenu menu, PlanetBootstrap bootstrap)
    {
        var settlement = bootstrap.GetComponent<Settlement>();
        var placement = bootstrap.GetComponent<BuildingPlacementController>();
        var selection = bootstrap.GetComponent<BuildingSelection>();
        var site = settlement.CreateSnapshot().normal;
        foreach (var id in new[] { "HabBlock", "Manufactorum", "StorageDepot" })
            Check(LandingSceneChecks.PlaceNear(bootstrap, id, site), "placed review fixture " + id);
        yield return null;

        void Record(string stage, string root)
        {
            var panel = GameObject.Find(root);
            Check(panel != null, stage + " opens its live panel");
            if (panel == null) return;
            Canvas.ForceUpdateCanvases();
            var textFits = true;
            foreach (var label in panel.GetComponentsInChildren<Text>())
            {
                if (string.IsNullOrWhiteSpace(label.text) || label.resizeTextForBestFit ||
                    label.verticalOverflow != VerticalWrapMode.Truncate) continue;
                var rect = label.rectTransform.rect;
                var settings = label.GetGenerationSettings(new Vector2(rect.width, 0f));
                settings.verticalOverflow = VerticalWrapMode.Overflow;
                var requiredHeight = label.cachedTextGeneratorForLayout.GetPreferredHeight(label.text, settings) / label.pixelsPerUnit;
                if (requiredHeight > rect.height + 2)
                {
                    textFits = false;
                    Debug.LogWarning($"UI review overflow: {stage}/{label.name}: {requiredHeight:0.#} > {rect.height:0.#}");
                }
            }
            Check(textFits, stage + " text fits its populated panel");
            CaptureReview(menu, stage, true, panel.GetComponentInParent<Canvas>());
        }

        var hab = placement.Buildings.FirstOrDefault(b => b.Id == "HabBlock");
        selection.Select(hab);
        yield return null;
        Record("building-inspector", "BuildingInspector");
        selection.Select(settlement.CoreBuildings[0]);
        yield return null;
        var shipRect = GameObject.Find("ShipHud")?.GetComponent<RectTransform>();
        var voxRect = GameObject.Find("VoxPortraitCard")?.GetComponent<RectTransform>();
        Check(shipRect != null && voxRect != null && !RectsOverlap(shipRect, voxRect),
            "ship inspector leaves the vox advisory clear at the reviewed resolution");
        var voxHeading = GameObject.Find("VoxSpeaker")?.GetComponent<Text>();
        Check(voxHeading != null && voxHeading.preferredHeight <= voxHeading.rectTransform.rect.height + 2f,
            "vox speaker and alert heading fit beside the ship inspector");
        Record("ship", "ShipHud");
        selection.Select(null);

        foreach (var id in new[] { "Manufactorum", "StorageDepot" })
        {
            menu.OpenFacilityPanel(placement.Buildings.FirstOrDefault(b => b.Id == id));
            yield return null;
            Record(id.ToLowerInvariant(), "FacilityCard");
            menu.CloseFacilityPanel();
        }
        FindButton("OpenResearch")?.onClick.Invoke();
        yield return null;
        Record("research", "ResearchCard");
        FindButton("CloseResearch")?.onClick.Invoke();
        FindButton("OpenColonyGuide")?.onClick.Invoke();
        yield return null;
        Record("guide", "GuideCard");
        menu.ResetSessionUi();

        var resources = GameObject.Find("OperationsLog")?.GetComponentsInChildren<Button>()
            .FirstOrDefault(b => b.GetComponentInChildren<Text>()?.text == "RESOURCE DEPOSITS");
        resources?.onClick.Invoke();
        yield return null;
        Record("resource-deposits", "ResourceDepositsPane");
        FindButton("CloseResourceDeposits")?.onClick.Invoke();
        menu.ShowDiscoveryPanel("SURVEY REPORT", "The survey team has charted a new location. Review the site from the colony map. The orbital instruments recorded a broad mineral seam beneath a fractured ridge, with shallow water traces along the eastern shelf. Several deposits overlap the preferred route, and the western approach crosses loose basalt. Command should compare the projected haul with the extra construction and maintenance cost before committing the dozer. The data remains available from the survey panel after this transmission closes.",
            "REVIEW SITE", () => { });
        yield return null;
        Record("survey", "DiscoveryCard");
        menu.ResetDiscoveryPanel();

        menu.ShowMiningDecision("The proposed cut reaches an occupied facility. Choose whether to keep the facility or proceed.",
            true);
        yield return null;
        Record("mining-decision", "ExcavationCard");
        menu.CloseMiningPanel();
        bootstrap.GetComponent<EarthworksController>().Open(WH30K.Sim.EarthworkMode.Flatten);
        yield return null;
        Record("earthworks", "EarthworksHUD");
        FindButton("CloseEarthworks")?.onClick.Invoke();

        menu.PromptEvent("COLONY TRANSMISSION", "Several districts report damaged water manifolds after the last pressure cycle. The attached survey identifies a mineral seam near the eastern ridge, but reaching it would delay repairs and require the dozer to cross unstable basalt. The council asks Command to preserve the current reserve until the manifolds are secure, while the Mechanicum recommends issuing a measured supply shipment now so the extraction crew can begin work before the weather front arrives. The decision will be recorded for the next review.",
            "SEND THE SUPPLIES", () => { }, "RETAIN THE RESERVE", () => { });
        yield return null;
        Record("event", "DecisionCard");
        if (Environment.GetCommandLineArgs().Contains("-wh30k-panel-review")) CaptureReview(menu, "event-long-copy", true);
        menu.ShowEventPanel(false);
        yield return CheckVisibleSaveBrowser(menu, bootstrap);
        var save = bootstrap.CaptureSaveJson();
        Check(!string.IsNullOrWhiteSpace(save) && bootstrap.TryLoadSaveJson(save), "built colony saves and reloads in memory");
        Check(placement.Buildings.Any(b => b.Id == "Manufactorum") && placement.Buildings.Any(b => b.Id == "HabBlock"),
            "save reload restores the reviewed buildings");
    }
    private static IEnumerator CheckVisibleSaveBrowser(NewGameMenu menu, PlanetBootstrap bootstrap)
    {
        string fixturePath = null;
        string previousSaveName = null;
        var saveNameField = typeof(PlanetBootstrap).GetField("saveFileName", BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            var fixtureSeed = 1700000000 + UnityEngine.Random.Range(0, 100000000);
            var fileName = "ui-panel-review-" + Guid.NewGuid().ToString("N") + ".json";
            previousSaveName = saveNameField?.GetValue(bootstrap) as string;
            Check(saveNameField != null && !string.IsNullOrWhiteSpace(previousSaveName),
                "save fixture redirects the production action to a disposable filename");
            if (saveNameField == null || string.IsNullOrWhiteSpace(previousSaveName)) yield break;
            saveNameField?.SetValue(bootstrap, fileName);
            fixturePath = Path.Combine(bootstrap.SaveDirectoryPath, fileName);
            Check(PointerClickButton(FindButton("SaveButton")), "pointer click saves the colony through the live HUD control");
            yield return new WaitForSecondsRealtime(.2f);
            Check(File.Exists(fixturePath) && bootstrap.CanLoadSaveFile(fixturePath),
                "live Save action writes a complete fixture accepted by the production validator");
            if (!File.Exists(fixturePath)) yield break;

            var fixtureJson = File.ReadAllText(fixturePath);
            var seedPattern = new Regex("(\\\"seed\\\"\\s*:\\s*)-?\\d+");
            Check(seedPattern.IsMatch(fixtureJson), "in-memory colony fixture contains its save identity");
            fixtureJson = seedPattern.Replace(fixtureJson, match => match.Groups[1].Value + fixtureSeed, 1);
            File.WriteAllText(fixturePath, fixtureJson);
            Check(bootstrap.CanLoadSaveFile(fixturePath), "unique disposable save fixture passes the production save validator");

            menu.ShowMainMenu(true);
            yield return new WaitForSecondsRealtime(.24f);
            Check(PointerClickButton(FindButton("Menu_2")), "pointer click opens the live save browser through EventSystem");
            yield return new WaitForSecondsRealtime(.24f);
            var saveBrowser = GameObject.Find("SaveBrowser");
            var fileLabel = Path.GetFileNameWithoutExtension(fixturePath);
            var row = saveBrowser?.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(button => button.GetComponentInChildren<Text>()?.text == fileLabel);
            Check(row != null, "visible save browser lists the uniquely named disposable fixture");
            if (row != null)
                Check(PointerClickButton(row), "pointer click selects the fixture row through EventSystem");
            var details = GameObject.Find("SelectedSaveDetails")?.GetComponent<Text>();
            Check(FindButton("LoadSelectedSave")?.interactable == true && details != null && details.text.Contains(fileLabel),
                "selecting the fixture row enables load and shows its selected identity");
            Check(PointerClickButton(FindButton("LoadSelectedSave")), "pointer click loads the selected fixture through EventSystem");
            yield return new WaitForSecondsRealtime(.35f);
            Check(GameSettings.CurrentSeed == fixtureSeed, "save browser loads the unique fixture identity into the active colony");
            Check(saveBrowser != null && !saveBrowser.activeSelf, "successful UI load closes the save browser");
        }
        finally
        {
            if (!string.IsNullOrEmpty(fixturePath) && File.Exists(fixturePath)) File.Delete(fixturePath);
            if (saveNameField != null && !string.IsNullOrWhiteSpace(previousSaveName))
                saveNameField.SetValue(bootstrap, previousSaveName);
        }
    }

    private static bool SubmitButton(Button button)
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null || button == null || !button.interactable) return false;
        eventSystem.SetSelectedGameObject(button.gameObject);
        return ExecuteEvents.Execute(button.gameObject, new BaseEventData(eventSystem), ExecuteEvents.submitHandler);
    }

    private static bool PointerClickButton(Button button)
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null || button == null || !button.interactable) return false;
        var rect = button.transform as RectTransform;
        if (rect == null) return false;
        var canvas = button.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var pointer = new PointerEventData(eventSystem)
        {
            button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center))
        };
        var hits = new System.Collections.Generic.List<RaycastResult>();
        eventSystem.RaycastAll(pointer, hits);
        var hit = hits.FirstOrDefault(result => ExecuteEvents.GetEventHandler<IPointerClickHandler>(result.gameObject) == button.gameObject);
        if (hit.gameObject == null) return false;
        ExecuteEvents.ExecuteHierarchy(hit.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
        pointer.pointerPress = ExecuteEvents.ExecuteHierarchy(hit.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        if (pointer.pointerPress == null)
            pointer.pointerPress = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject);
        pointer.rawPointerPress = hit.gameObject;
        pointer.eligibleForClick = pointer.pointerPress != null;
        if (pointer.pointerPress != null) ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler);
        var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject);
        var dispatched = pointer.eligibleForClick && clickHandler == pointer.pointerPress &&
            ExecuteEvents.Execute(clickHandler, pointer, ExecuteEvents.pointerClickHandler);
        pointer.pointerPress = null;
        pointer.rawPointerPress = null;
        pointer.eligibleForClick = false;
        return dispatched;
    }

    private static Button FindButton(string name) => GameObject.Find(name)?.GetComponent<Button>();

    private static void CheckPortraitCardAction()
    {
        var fixture = new GameObject("PortraitCardAcceptanceFixture", typeof(RectTransform), typeof(Canvas));
        try
        {
            var factory = typeof(NewGameMenu).Assembly.GetType("WH30K.UI.CommandUi")
                ?.GetMethod("CreatePortraitCard", BindingFlags.Static | BindingFlags.NonPublic);
            Check(factory != null, "shared portrait-card factory exists");
            if (factory == null) return;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var card = factory.Invoke(null, new object[] { fixture.transform, font });
            var type = card.GetType();
            var root = type.GetField("root", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(card) as GameObject;
            var show = type.GetMethod("Show", BindingFlags.Instance | BindingFlags.NonPublic);
            var action = root?.transform.Find("VoxAction")?.GetComponent<Button>();
            var critical = (Color)type.Assembly.GetType("WH30K.UI.CommandUi")
                .GetField("Critical", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var portrait = Resources.Load<Texture2D>("Vox/Captain");
            var calls = 0;
            show.Invoke(card, new object[] { portrait, "CAPTAIN / URGENT", "Review the new survey.", critical,
                (Action)(() => calls++), "OPEN SURVEY" });
            Check(root?.transform.Find("VoxSpeaker")?.GetComponent<Text>()?.text == "CAPTAIN / URGENT"
                && root.transform.Find("VoxMessage")?.GetComponent<Text>()?.text == "Review the new survey.",
                "portrait card displays speaker role and message");
            Check(root.transform.Find("PortraitFrame/Portrait")?.GetComponent<RawImage>()?.texture == portrait
                && root.transform.Find("VoxSpeaker")?.GetComponent<Text>()?.color == critical,
                "portrait and critical priority are presented with the message");
            Check(root.GetComponent<Image>().raycastTarget == false
                && root.transform.Find("PortraitFrame/Portrait")?.GetComponent<RawImage>()?.raycastTarget == false,
                "passive portrait-card surfaces leave world input available");
            Check(action != null && action.gameObject.activeSelf
                && action.GetComponentInChildren<Text>()?.text == "OPEN SURVEY",
                "relevant optional card action appears with its label");
            action.onClick.Invoke();
            Check(calls == 1, "optional portrait-card action invokes its callback");
        }
        finally { UnityEngine.Object.DestroyImmediate(fixture); }
    }

    private static bool RectsOverlapAtCanvasWidth(RectTransform a, RectTransform b, float canvasWidth)
    {
        var aReferenceX = canvasWidth * a.anchorMin.x;
        var aLeft = aReferenceX + a.anchoredPosition.x - a.rect.width * a.pivot.x;
        var aRight = aLeft + a.rect.width;
        var bReferenceX = canvasWidth * b.anchorMin.x;
        var bLeft = bReferenceX + b.anchoredPosition.x - b.rect.width * b.pivot.x;
        var bRight = bLeft + b.rect.width;
        return aLeft < bRight && aRight > bLeft;
    }

    private static bool RectsOverlap(RectTransform a, RectTransform b)
    {
        var ac = new Vector3[4]; var bc = new Vector3[4];
        a.GetWorldCorners(ac); b.GetWorldCorners(bc);
        return ac[0].x < bc[2].x && ac[2].x > bc[0].x && ac[0].y < bc[2].y && ac[2].y > bc[0].y;
    }

    private static void CaptureReview(NewGameMenu menu, string stage, bool showWorld = false, Canvas selectedCanvas = null)
    {
        var canvas = selectedCanvas != null ? selectedCanvas : menu.GetComponentInChildren<Canvas>();
        var scaler = canvas.GetComponent<CanvasScaler>();
        var camera = Camera.main;
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var previousAspect = camera.aspect;
        var previousClear = camera.clearFlags;
        var previousColor = camera.backgroundColor;
        var previousMask = camera.cullingMask;
        var previousMode = canvas.renderMode;
        var previousCamera = canvas.worldCamera;
        var previousDistance = canvas.planeDistance;
        var directory = Path.Combine(".utmp", "main-menu-reference-review");
        Directory.CreateDirectory(directory);
        Camera uiCamera = null;
        GameObject uiCameraRoot = null;
        Transform[] uiTransforms = null;
        int[] originalLayers = null;
        const int captureLayer = 5;
        try
        {
            scaler.enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            if (showWorld)
            {
                uiTransforms = canvas.GetComponentsInChildren<Transform>(true);
                originalLayers = new int[uiTransforms.Length];
                for (var i = 0; i < uiTransforms.Length; i++)
                {
                    originalLayers[i] = uiTransforms[i].gameObject.layer;
                    uiTransforms[i].gameObject.layer = captureLayer;
                }
                uiCameraRoot = new GameObject("UI Review Camera", typeof(Camera));
                uiCamera = uiCameraRoot.GetComponent<Camera>();
                uiCamera.enabled = false;
                uiCamera.clearFlags = CameraClearFlags.Depth;
                uiCamera.cullingMask = 1 << captureLayer;
                uiCamera.nearClipPlane = .1f;
                uiCamera.farClipPlane = 20;
                camera.cullingMask = previousMask & ~(1 << captureLayer);
            }
            canvas.worldCamera = uiCamera == null ? camera : uiCamera;
            canvas.planeDistance = canvas.worldCamera.nearClipPlane + 2f;
            if (!showWorld)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.magenta;
                camera.cullingMask = 1 << canvas.gameObject.layer;
            }
            foreach (var size in new[] { new Vector2Int(1280, 800), new Vector2Int(1920, 1080), new Vector2Int(2560, 890) })
            {
                var target = new RenderTexture(size.x, size.y, 24);
                var pixels = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                try
                {
                    camera.targetTexture = target;
                    camera.aspect = size.x / (float)size.y;
                    var widthScale = size.x / scaler.referenceResolution.x;
                    var heightScale = size.y / scaler.referenceResolution.y;
                    canvas.scaleFactor = scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand
                        ? Mathf.Min(widthScale, heightScale)
                        : Mathf.Pow(widthScale, 1f - scaler.matchWidthOrHeight) * Mathf.Pow(heightScale, scaler.matchWidthOrHeight);
                    if (stage == "ship")
                    {
                        typeof(NewGameMenu).GetMethod("SetVoxViewportLayout", BindingFlags.Instance | BindingFlags.NonPublic)
                            ?.Invoke(menu, new object[] { size.x / canvas.scaleFactor });
                        Canvas.ForceUpdateCanvases();
                        var shipRect = GameObject.Find("ShipHud")?.GetComponent<RectTransform>();
                        var voxRect = GameObject.Find("VoxPortraitCard")?.GetComponent<RectTransform>();
                        var heading = GameObject.Find("VoxSpeaker")?.GetComponent<Text>();
                        // The RenderTexture changes size; the live Game View canvas does not.
                        // Compare anchored panel bounds against the target viewport dimensions.
                        var targetCanvasWidth = size.x / canvas.scaleFactor;
                        Check(shipRect != null && voxRect != null
                            && !RectsOverlapAtCanvasWidth(shipRect, voxRect, targetCanvasWidth),
                            $"ship inspector leaves the vox advisory clear at {size.x}x{size.y}");
                        Check(heading != null && heading.preferredHeight <= heading.rectTransform.rect.height + 2f,
                            $"vox heading fits beside the ship inspector at {size.x}x{size.y}");
                    }
                    if (uiCamera != null)
                    {
                        uiCamera.targetTexture = target;
                        uiCamera.aspect = camera.aspect;
                    }
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    if (uiCamera != null) uiCamera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                    var covered = true;
                    foreach (var corner in new[] { new Vector2Int(1, 1), new Vector2Int(1, size.y - 2),
                        new Vector2Int(size.x - 2, 1), new Vector2Int(size.x - 2, size.y - 2) })
                    {
                        var color = pixels.GetPixel(corner.x, corner.y);
                        covered &= !(color.r > .95f && color.b > .95f && color.g < .05f);
                    }
                    if (!showWorld) Check(covered, $"art covers all four corners on {stage} at {size.x}x{size.y}");
                    File.WriteAllBytes(Path.Combine(directory, $"{stage}-{size.x}x{size.y}.png"), pixels.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = previousTarget;
                    if (uiCamera != null) uiCamera.targetTexture = null;
                    RenderTexture.active = previousActive;
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                    UnityEngine.Object.DestroyImmediate(pixels);
                }
            }
        }
        catch (Exception e)
        {
            Check(false, $"could not render {stage}: {e}");
        }
        finally
        {
            camera.targetTexture = previousTarget; camera.aspect = previousAspect;
            camera.clearFlags = previousClear; camera.backgroundColor = previousColor; camera.cullingMask = previousMask;
            canvas.renderMode = previousMode; canvas.worldCamera = previousCamera; canvas.planeDistance = previousDistance;
            scaler.enabled = true;
            if (uiCameraRoot != null) UnityEngine.Object.DestroyImmediate(uiCameraRoot);
            if (uiTransforms != null)
                for (var i = 0; i < uiTransforms.Length; i++) uiTransforms[i].gameObject.layer = originalLayers[i];
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) { passed = false; Debug.LogWarning("Main menu FAIL: " + label); return; }
        checks++; Debug.Log("Main menu PASS: " + label);
    }
}
