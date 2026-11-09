using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay.Construction;
using WH30K.Sim;

namespace WH30K.UI
{
    public partial class NewGameMenu
    {
        private GameObject landingPanel, landingGhost, comparisonPanel;
        private Material landingMaterial;
        private Sprite landingPortraitSprite;
        private Text landingReadout;
        private readonly Text[] landingLoadoutValues = new Text[3];
        private Button landButton;
        private readonly Button[] landingSiteButtons = new Button[6];
        private readonly Text[] landingSiteLabels = new Text[6];

        private void BuildLanding(Transform parent)
        {
            landingPanel = Panel("LandingSelection", parent, new Vector2(390, 748), new Vector2(-24, -28), TextAnchor.UpperRight);
            var landingPanelTint = CommandUi.Iron;
            landingPanelTint.a = .48f;
            landingPanel.GetComponent<Image>().color = landingPanelTint;
            Label("LandingTitle", landingPanel.transform, "LANDING ZONE", 18, new Vector2(18, -14), 348, 28, Gold);
            Label("LandingHelp", landingPanel.transform, "Select a surveyed site", 12, new Vector2(18, -45), 348, 22, Ivory);
            var portraitTexture = Resources.Load<Texture2D>("Architecture/LandingTenderPortrait");
            if (portraitTexture != null)
            {
                landingPortraitSprite = Sprite.Create(portraitTexture, new Rect(0, 0, portraitTexture.width, portraitTexture.height),
                    new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
                var portraitObject = new GameObject("LandingTenderPhotorealRender", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                portraitObject.transform.SetParent(landingPanel.transform, false);
                var portraitRect = (RectTransform)portraitObject.transform;
                portraitRect.anchorMin = portraitRect.anchorMax = new Vector2(0, 1);
                portraitRect.pivot = new Vector2(0, 1);
                portraitRect.anchoredPosition = new Vector2(18, -70);
                portraitRect.sizeDelta = new Vector2(350, 130);
                var portrait = portraitObject.GetComponent<Image>();
                portrait.sprite = landingPortraitSprite;
                portrait.preserveAspect = true;
                portrait.raycastTarget = false;
            }
            for (var i = 0; i < landingSiteButtons.Length; i++)
            {
                var siteIndex = i;
                landingSiteButtons[i] = Button("LandingSiteOption" + i, landingPanel.transform,
                    new Vector2(18, -210 - i * 31), "", out landingSiteLabels[i], 350, 28);
                landingSiteLabels[i].fontSize = 13;
                landingSiteButtons[i].onClick.AddListener(() => bootstrap.SelectLandingSite(siteIndex));
            }
            var compareButton = Button("LandingDifficultyDropdown", landingPanel.transform, new Vector2(18, -396), "SITE COMPARISON  ▾", out _, 350, 30);
            comparisonPanel = Panel("LandingDifficultyOptions", landingPanel.transform, new Vector2(350, 90), new Vector2(18, -429), TextAnchor.UpperLeft, false);
            var comparisonPanelTint = CommandUi.Iron;
            comparisonPanelTint.a = .76f;
            comparisonPanel.GetComponent<Image>().color = comparisonPanelTint;
            Label("LandingDifficultyDetails", comparisonPanel.transform,
                "EASY  ·  gentler ground, larger reserves, milder events\nMEDIUM  ·  balanced terrain, reserves and conditions\nHARD  ·  rougher ground, smaller reserves, harsher events",
                11, new Vector2(10, -6), 330, 78, Ivory);
            comparisonPanel.SetActive(false);
            compareButton.onClick.AddListener(() => comparisonPanel.SetActive(!comparisonPanel.activeSelf));
            Label("LandingLoadoutTitle", landingPanel.transform, "TENDER LOADOUT", 13, new Vector2(18, -524), 220, 22, Gold);
            BuildLandingLoadoutRow(0, "Crew", -552);
            BuildLandingLoadoutRow(1, "Construction", -588);
            BuildLandingLoadoutRow(2, "Rations", -624);
            landingReadout = Label("LandingResources", landingPanel.transform, "", 10, new Vector2(18, -660), 350, 40, Ivory);
            Button("LandingSurvey", landingPanel.transform, new Vector2(18, -704), "CYCLE RESOURCE FILTER", out _, 168, 32)
                .onClick.AddListener(() => Industry?.SetOverlay(Industry.Overlay >= 7 ? 1 : Industry.Overlay + 1));
            landButton = Button("LandColonyShip", landingPanel.transform, new Vector2(198, -704), "DEPLOY LANDER", out _, 170, 32);
            landButton.onClick.AddListener(() => bootstrap.ConfirmLanding());
            landingPanel.SetActive(false);
        }

        private void BuildLandingLoadoutRow(int index, string label, float y)
        {
            Label("LandingLoadoutLabel" + index, landingPanel.transform, label, 11, new Vector2(18, y), 126, 24, Ivory);
            Button("LandingLoadoutMinus" + index, landingPanel.transform, new Vector2(206, y), "−", out _, 30, 26)
                .onClick.AddListener(() => AdjustLandingLoadout(index, -1));
            landingLoadoutValues[index] = Label("LandingLoadoutValue" + index, landingPanel.transform, "", 11,
                new Vector2(242, y - 2), 80, 24, Gold);
            Button("LandingLoadoutPlus" + index, landingPanel.transform, new Vector2(326, y), "+", out _, 30, 26)
                .onClick.AddListener(() => AdjustLandingLoadout(index, 1));
        }

        private void AdjustLandingLoadout(int index, int direction)
        {
            var crew = bootstrap.LandingCrewAllocation;
            var rations = bootstrap.LandingRationAllocation;
            var supplies = bootstrap.LandingSupplyFraction;
            if (index == 0) crew = Mathf.Clamp(crew + direction * 250f, 250, ShipRules.CrewCapacity);
            else if (index == 1) supplies = Mathf.Clamp01(supplies + direction * .25f);
            else rations = Mathf.Clamp(rations + direction * 6000f, 0, 60000);
            bootstrap.SetLandingLoadout(crew, rations, supplies);
        }

        public void UpdateLandingLoadout()
        {
            if (bootstrap == null || landingLoadoutValues[0] == null) return;
            landingLoadoutValues[0].text = $"{bootstrap.LandingCrewAllocation:N0} crew";
            landingLoadoutValues[1].text = $"{bootstrap.LandingSupplyFraction:P0}";
            landingLoadoutValues[2].text = $"{bootstrap.LandingRationAllocation:N0}";
        }

        public void ShowLandingPanel(bool visible)
        {
            if (landingPanel != null) landingPanel.SetActive(visible);
            if (comparisonPanel != null) comparisonPanel.SetActive(false);
            if (!visible && landingGhost != null) { landingGhost.SetActive(false); Destroy(landingGhost); landingGhost = null; }
        }

        public void UpdateLandingReadout(string text)
        {
            if (landingReadout != null) landingReadout.text = text;
        }

        public void UpdateLandingOptions(System.Collections.Generic.IReadOnlyList<GameSettings.LandingSite> sites, int selectedIndex)
        {
            for (var i = 0; i < landingSiteButtons.Length; i++)
            {
                var available = sites != null && i < sites.Count;
                landingSiteButtons[i].gameObject.SetActive(available);
                landingSiteButtons[i].interactable = available;
                if (!available) continue;
                var site = sites[i];
                var name = LandingTierName(site.category);
                var number = site.variant == 0 ? "I" : "II";
                landingSiteLabels[i].text = $"{name}  /  {number}";
                Tone(landingSiteButtons[i], i == selectedIndex);
            }
        }

        private static string LandingTierName(GameSettings.Difficulty difficulty)
        {
            if (difficulty == GameSettings.Difficulty.Easy) return "EASY";
            if (difficulty == GameSettings.Difficulty.ExtremeHard || difficulty == GameSettings.Difficulty.Hard) return "HARD";
            return "MEDIUM";
        }

        private void RefreshLanding()
        {
            if (!bootstrap.ChoosingLanding) return;
            var planet = bootstrap.ActivePlanet;
            var valid = bootstrap.SelectedLandingSiteIndex >= 0;
            landButton.interactable = valid;
            if (landingGhost == null)
            {
                if (landingMaterial == null)
                {
                    var previewTint = CommandUi.Signal;
                    previewTint.a = .16f;
                    landingMaterial = new Material(Shader.Find("Standard")) { color = previewTint }; // World preview material tint follows the shared palette.
                    landingMaterial.SetFloat("_Mode", 3f);
                    landingMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    landingMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    landingMaterial.SetInt("_ZWrite", 0);
                    landingMaterial.SetOverrideTag("RenderType", "Transparent");
                    landingMaterial.EnableKeyword("_ALPHABLEND_ON");
                    landingMaterial.DisableKeyword("_ALPHATEST_ON");
                    landingMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    landingMaterial.renderQueue = 3000;
                }
                landingGhost = BuildingVisuals.Create("LandingVessel", "Landing vessel preview", planet.transform, BuildingVisuals.ShipDimensions, landingMaterial);
            }
            landingGhost.SetActive(valid);
            var n = bootstrap.LandingDirection;
            var up = planet.transform.TransformDirection(n).normalized;
            var restPosition = planet.transform.TransformPoint(planet.EvaluateSurfacePoint(n)) + up * (WH30K.Sim.ShipRules.Height * .5f);
            landingGhost.transform.SetPositionAndRotation(restPosition,
                planet.transform.rotation * Quaternion.FromToRotation(Vector3.up, n));
        }

        private void OnDestroy()
        {
            if (landingMaterial != null) Destroy(landingMaterial);
            if (authorityFont != null) Destroy(authorityFont);
            if (landingPortraitSprite != null) Destroy(landingPortraitSprite);
        }
    }
}
