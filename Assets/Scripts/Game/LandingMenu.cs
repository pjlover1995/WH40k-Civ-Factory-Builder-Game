using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay.Construction;

namespace WH30K.UI
{
    public partial class NewGameMenu
    {
        private GameObject landingPanel, landingGhost;
        private Material landingMaterial;
        private Text landingReadout;
        private Button landButton;
        private readonly Button[] landingSiteButtons = new Button[10];
        private readonly Text[] landingSiteLabels = new Text[10];

        private void BuildLanding(Transform parent)
        {
            landingPanel = Panel("LandingSelection", parent, new Vector2(940, 590), Vector2.zero, TextAnchor.MiddleCenter);
            Label("LandingTitle", landingPanel.transform, "COLONY SHIP / SELECT A LANDING CATEGORY", 20, new Vector2(24, -18), 892, 28, Gold);
            Label("LandingHelp", landingPanel.transform,
                "Two scanned sites for each mode. Harder sites have rougher nearby ground and smaller surface reserves.\n"
                + "Each site includes basic deposits for the starter chain; compare the local survey before touchdown.\n"
                + "The 3 km hull is checked against dry ground and landing clearance.",
                12, new Vector2(24, -50), 892, 58, Ivory);
            for (var i = 0; i < landingSiteButtons.Length; i++)
            {
                var siteIndex = i;
                var row = i / 2;
                var column = i % 2;
                landingSiteButtons[i] = Button("LandingSiteOption" + i, landingPanel.transform,
                    new Vector2(24 + column * 458, -126 - row * 53), "", out landingSiteLabels[i], 434, 44);
                landingSiteLabels[i].fontSize = 11;
                landingSiteButtons[i].onClick.AddListener(() => bootstrap.SelectLandingSite(siteIndex));
            }
            landingReadout = Label("LandingResources", landingPanel.transform, "", 12, new Vector2(24, -397), 892, 78, Ivory);
            Button("LandingSurvey", landingPanel.transform, new Vector2(24, -514), "CYCLE RESOURCE FILTER", out _, 260, 34)
                .onClick.AddListener(() => Industry?.SetOverlay(Industry.Overlay >= 7 ? 1 : Industry.Overlay + 1));
            landButton = Button("LandColonyShip", landingPanel.transform, new Vector2(634, -514), "LAND COLONY SHIP", out _, 282, 34);
            MarkPrimary(landButton);
            landButton.onClick.AddListener(() => bootstrap.ConfirmLanding());
            landingPanel.SetActive(false);
        }

        public void ShowLandingPanel(bool visible)
        {
            if (landingPanel != null) landingPanel.SetActive(visible);
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
                var name = GameSettings.GetDefinition(site.category).displayName.ToUpperInvariant();
                var number = site.variant == 0 ? "I" : "II";
                landingSiteLabels[i].text = $"{name}  /  SITE {number}\n{site.localRelief:0} m local relief   /   {site.reserveMultiplier:0.00}x local reserves";
                Tone(landingSiteButtons[i], i == selectedIndex);
            }
        }

        private void RefreshLanding()
        {
            if (!bootstrap.ChoosingLanding) return;
            var planet = bootstrap.ActivePlanet;
            var valid = bootstrap.SelectedLandingSiteIndex >= 0;
            landButton.interactable = valid;
            if (landingGhost == null)
            {
                if (landingMaterial == null) landingMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(.2f, .85f, .95f) }; // World preview material
                landingGhost = BuildingVisuals.Create("ColonyShip", "Colony ship landing preview", planet.transform, BuildingVisuals.ShipDimensions, landingMaterial);
            }
            landingGhost.SetActive(valid);
            var n = bootstrap.LandingDirection;
            landingGhost.transform.SetPositionAndRotation(planet.transform.TransformPoint(planet.EvaluateSurfacePoint(n)),
                planet.transform.rotation * Quaternion.FromToRotation(Vector3.up, n));
        }

        private void OnDestroy()
        {
            if (landingMaterial != null) Destroy(landingMaterial);
            if (authorityFont != null) Destroy(authorityFont);
        }
    }
}
