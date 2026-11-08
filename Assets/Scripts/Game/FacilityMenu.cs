using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WH30K.Sim;
using WH30K.Gameplay.Construction;

namespace WH30K.UI
{
    public partial class NewGameMenu
    {
        private GameObject facilityPanel, productionControls, depotControls;
        private Text facilityTitle, facilityStats, bufferLabel, allocationLabel, depotInventory;
        private readonly Dropdown[] productChoices = new Dropdown[4];
        private readonly Text[] productDetails = new Text[4];
        private Dropdown depotMode, depotItem;
        private Slider bufferSlider, depotSlider;
        private ManagedBuilding facilityBuilding;
        private bool refreshingFacility;
        private Button expandManufactorum, tierManufactorum;
        private Text tierManufactorumLabel, expansionLabel;
        public bool FacilityPanelOpen => facilityPanel != null && facilityPanel.activeSelf;

        private void BuildFacilityPanel(Transform parent)
        {
            facilityPanel = CommandUi.Screen("FacilityPanel", parent, 0.82f);
            var card = Panel("FacilityCard", facilityPanel.transform, new Vector2(940, 740), Vector2.zero, TextAnchor.MiddleCenter);
            facilityTitle = Label("FacilityTitle", card.transform, "", 23, new Vector2(24, -18), 890, 34, Gold);
            facilityStats = Label("FacilityStatistics", card.transform, "", 13, new Vector2(24, -60), 892, 226, Ivory);
            productionControls = Panel("ProductionControls", card.transform, new Vector2(892, 380), new Vector2(24, -294), TextAnchor.UpperLeft, false);
            Label("ProductionHelp", productionControls.transform, "PRODUCTS / Equal work shares: 1 = 100%  |  2 = 50%  |  3 = 33.3%  |  4 = 25%", 13, Vector2.zero, 892, 24, Gold);
            var products = new List<string> { "Disabled" };
            for (var product = 0; product < FacilityRules.ProductCount; product++) products.Add(IndustryRules.Name(FacilityRules.Output(product)) + " / Tier " + ManufactorumRules.RequiredTier(product));
            for (var slot = 0; slot < 4; slot++)
            {
                var line = slot;
                productChoices[slot] = FacilityDropdown("Product" + slot, productionControls.transform, new Vector2(0, -32 - slot * 66), 290, products);
                productDetails[slot] = Label("ProductDetails" + slot, productionControls.transform, "", 13, new Vector2(310, -32 - slot * 66), 582, 60, Ivory);
                productChoices[slot].onValueChanged.AddListener(value => {
                    if (refreshingFacility) return;
                    Industry.SetProduct(facilityBuilding, line, value - 1); RefreshFacilityPanel();
                });
            }
            bufferLabel = Label("BufferLimit", productionControls.transform, "", 13, new Vector2(0, -306), 540, 22, Gold);
            bufferSlider = FacilitySlider("BufferSlider", productionControls.transform, new Vector2(560, -302), 320, 1, IndustryRules.StorageLimit);
            bufferSlider.onValueChanged.AddListener(value => { if (!refreshingFacility) { Industry.SetBufferLimit(facilityBuilding, value); RefreshFacilityPanel(); } });
            expandManufactorum = Button("ExpandManufactorum", productionControls.transform, new Vector2(0,-342), "", out expansionLabel, 382, 34);
            expandManufactorum.onClick.AddListener(() => { facilityBuilding?.Industrial?.Request(); RefreshFacilityPanel(); });
            tierManufactorum = Button("TierManufactorum", productionControls.transform, new Vector2(394,-342), "", out tierManufactorumLabel, 498, 34);
            tierManufactorum.onClick.AddListener(() => { facilityBuilding?.Industrial?.UpgradeTechnology(); RefreshFacilityPanel(); });

            depotControls = Panel("DepotControls", card.transform, new Vector2(892, 380), new Vector2(24, -294), TextAnchor.UpperLeft, false);
            Label("DepotModeLabel", depotControls.transform, "STORAGE MODE", 13, Vector2.zero, 400, 24, Gold);
            depotMode = FacilityDropdown("DepotMode", depotControls.transform, new Vector2(440, 0), 440, new List<string> { "General / first come, first served", "Designated / reserved item limits" });
            depotMode.onValueChanged.AddListener(value => { if (!refreshingFacility) { Industry.SetDepotMode(facilityBuilding, value == 1); RefreshFacilityPanel(); } });
            Label("DepotItemLabel", depotControls.transform, "ITEM TO CONFIGURE", 13, new Vector2(0, -60), 400, 24, Gold);
            var items = new List<string>();
            for (var k = 0; k < IndustryRules.MaterialCount; k++) items.Add(IndustryRules.Name((MaterialKind)k));
            depotItem = FacilityDropdown("DepotItem", depotControls.transform, new Vector2(440, -60), 440, items);
            depotItem.onValueChanged.AddListener(_ => { if (!refreshingFacility) RefreshFacilityPanel(); });
            allocationLabel = Label("DepotAllocation", depotControls.transform, "", 13, new Vector2(0, -116), 540, 48, Gold);
            depotSlider = FacilitySlider("DepotLimitSlider", depotControls.transform, new Vector2(560, -120), 320, 0, FacilityRules.DepotCapacity);
            depotSlider.onValueChanged.AddListener(value => {
                if (refreshingFacility) return;
                Industry.SetDepotLimit(facilityBuilding, (MaterialKind)depotItem.value, value); RefreshFacilityPanel();
            });
            depotInventory = Label("DepotReservations", depotControls.transform, "", 14, new Vector2(0, -182), 892, 148, Ivory);
            Label("DepotNote", depotControls.transform, "Limits reserve capacity, not deliveries. Zero excludes an item in Designated mode. Existing stock and dispatched cargo are retained; surplus drains before new deliveries.", 13, new Vector2(0, -338), 892, 44, Ivory);
            Button("CloseFacility", card.transform, new Vector2(640, -688), "CLOSE  [ESC]", out _, 276, 32).onClick.AddListener(CloseFacilityPanel);
            facilityPanel.SetActive(false);
        }
        public void OpenFacilityPanel(ManagedBuilding building)
        {
            if (building == null || building.Id != "Manufactorum" && building.Id != "StorageDepot" || HasOpenEvent || newGamePanel.activeSelf) return;
            HideOutputTooltip(); buildingPlacement.CancelPlacement(); facilityBuilding = building;
            facilityPanel.SetActive(true); facilityPanel.transform.SetAsLastSibling();
            clock.DecisionPending = true; RefreshFacilityPanel();
        }
        public void CloseFacilityPanel()
        {
            if (facilityPanel != null)
            {
                foreach (var dropdown in facilityPanel.GetComponentsInChildren<Dropdown>(true)) dropdown.Hide();
                facilityPanel.SetActive(false);
            }
            facilityBuilding = null;
            if (clock != null) clock.DecisionPending = HasOpenEvent || newGamePanel != null && newGamePanel.activeSelf;
        }
        private void RefreshFacilityPanel()
        {
            if (!FacilityPanelOpen) return;
            if (facilityBuilding == null) { CloseFacilityPanel(); return; }
            var config = Industry.FacilitySettings(facilityBuilding);
            if (config == null) { CloseFacilityPanel(); return; }
            refreshingFacility = true;
            var factory = facilityBuilding.Id == "Manufactorum";
            facilityTitle.text = factory ? "MANUFACTORUM / PRODUCTION SELECTION" : "DEPOT / STORAGE CONFIGURATION";
            facilityStats.text = Industry.FacilityReport(facilityBuilding) + (factory ? "\n" + facilityBuilding.Industrial.Status : "");
            facilityStats.fontSize = 13;
            while (facilityStats.preferredHeight > facilityStats.rectTransform.rect.height && facilityStats.fontSize > 11) facilityStats.fontSize--;
            productionControls.SetActive(factory); depotControls.SetActive(!factory);
            if (factory)
            {
                var level = facilityBuilding.State.level;
                expandManufactorum.interactable = facilityBuilding.Industrial.CanExpand;
                expansionLabel.text = config != null && facilityBuilding.State.manufactorum.project != null ? "WAREHOUSE CONSTRUCTING" : "EXPAND / 20 SUP + 2 PLASTEEL";
                tierManufactorum.interactable = level < 2 && !facilityBuilding.State.offline && !facilityBuilding.State.improving && facilityBuilding.State.manufactorum.project == null;
                tierManufactorumLabel.text = level >= 2 ? "TIER III / ALL CURRENT TECHNOLOGIES" : $"TECH TIER {level+2} / {ManufactorumRules.TierSupplies(level):0} SUP + {ManufactorumRules.TierPlasteel(level):0} PLAS";
                var count = FacilityRules.ActiveCount(config);
                for (var slot = 0; slot < 4; slot++)
                {
                    var product = config.products[slot]; productChoices[slot].SetValueWithoutNotify(product + 1);
                    productDetails[slot].text = product < 0 ? "Line disabled / recipes require listed technology tier" : $"{100f / count:0.#}% work share / 1 unit per {5 * count} effective seconds\n" + FacilityRules.Recipe(product);
                }
                bufferSlider.SetValueWithoutNotify(config.bufferLimit); bufferLabel.text = $"INTERNAL BUFFER / {config.bufferLimit:0} units per product";
            }
            else
            {
                depotMode.SetValueWithoutNotify(config.designated ? 1 : 0);
                depotSlider.interactable = config.designated; depotItem.interactable = config.designated;
                depotSlider.maxValue = FacilityRules.DepotCapacity - FacilityRules.Allocated(config) + config.storageLimits[depotItem.value];
                depotSlider.SetValueWithoutNotify(config.storageLimits[depotItem.value]);
                allocationLabel.text = $"{IndustryRules.Name((MaterialKind)depotItem.value)} / reserve {config.storageLimits[depotItem.value]:0} units\nUnassigned capacity {FacilityRules.DepotCapacity - FacilityRules.Allocated(config):0} / {FacilityRules.DepotCapacity:0}";
                depotInventory.text = config.designated ? "ITEM RESERVATIONS\n" : "SAVED RESERVATIONS / applied in Designated mode\n";
                for (var k = 0; k < IndustryRules.MaterialCount; k++)
                    depotInventory.text += $"{IndustryRules.Name((MaterialKind)k)}: {config.storageLimits[k]:0}" + (k % 2 == 0 ? "     |     " : "\n");
            }
            refreshingFacility = false;
        }
        private Slider FacilitySlider(string name, Transform parent, Vector2 position, float width, float min, float max)
        {
            var go = Panel(name, parent, new Vector2(width, 28), position, TextAnchor.UpperLeft, false);
            var slider = go.AddComponent<Slider>(); slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = true;
            var handle = Panel("Handle", go.transform, new Vector2(18, 28), Vector2.zero, TextAnchor.MiddleCenter, false);
            slider.handleRect = (RectTransform)handle.transform;
            CommandUi.StyleControl(slider, handle.GetComponent<Image>(), useBrassHandle: true);
            return slider;
        }
        private Dropdown FacilityDropdown(string name, Transform parent, Vector2 position, float width, List<string> options)
        {
            var go = Panel(name, parent, new Vector2(width, 32), position, TextAnchor.UpperLeft);
            var dropdown = go.AddComponent<Dropdown>();
            CommandUi.StyleControl(dropdown, go.GetComponent<Image>());
            dropdown.captionText = Label("Selection", go.transform, "", 13, new Vector2(10, -5), width - 40, 24, Ivory);
            Label("Arrow", go.transform, "v", 13, new Vector2(width - 24, -5), 20, 24, Gold);
            var template = Panel("Template", go.transform, new Vector2(width, 196), new Vector2(0, -34), TextAnchor.UpperLeft);
            var scroll = template.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Panel("Viewport", template.transform, new Vector2(width, 196), Vector2.zero, TextAnchor.UpperLeft, false);
            viewport.AddComponent<RectMask2D>(); scroll.viewport = (RectTransform)viewport.transform;
            var content = Panel("Content", viewport.transform, new Vector2(width, 32), Vector2.zero, TextAnchor.UpperLeft, false);
            scroll.content = (RectTransform)content.transform;
            var item = Panel("Item", content.transform, new Vector2(width, 32), Vector2.zero, TextAnchor.UpperLeft, false);
            var toggle = item.AddComponent<Toggle>();
            CommandUi.StyleControl(toggle, item.GetComponent<Image>());
            var checkmark = Panel("Selected", item.transform, new Vector2(5, 24), new Vector2(2, -4), TextAnchor.UpperLeft, false);
            checkmark.GetComponent<Image>().color = Gold; toggle.graphic = checkmark.GetComponent<Image>();
            dropdown.itemText = Label("ItemLabel", item.transform, "", 13, new Vector2(12, -5), width - 20, 24, Ivory);
            dropdown.template = (RectTransform)template.transform; dropdown.AddOptions(options); template.SetActive(false);
            return dropdown;
        }
    }
}
