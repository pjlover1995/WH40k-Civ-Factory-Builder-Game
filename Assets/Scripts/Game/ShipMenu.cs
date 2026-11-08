using UnityEngine;
using UnityEngine.UI;
using WH30K.Sim;
using WH30K.Sim.Resources;

namespace WH30K.UI
{
    public partial class NewGameMenu
    {
        private GameObject shipPanel;
        private GameObject shipSupplyPanel;
        private Text shipTitle, shipDamage, shipFoodNote;
        private readonly Text[] shipValues = new Text[11];
        private readonly InputField[] shipSupplyInputs = new InputField[4];
        private Text shipSupplyError;
        private Text shipOperationLabel;

        private void BuildShipHud(Transform parent)
        {
            shipPanel = Panel("ShipHud", parent, new Vector2(620, 558), new Vector2(-16, -114), TextAnchor.UpperRight);
            Label("ShipHudTag", shipPanel.transform, "EXPLORATOR / VESSEL TELEMETRY", 11, new Vector2(20, -14), 550, 22, Gold);
            shipTitle = Label("ShipHudTitle", shipPanel.transform, "", 22, new Vector2(20, -38), 550, 34, Ivory);
            Button("CloseShipHud", shipPanel.transform, new Vector2(574, -12), "X", out _, 26, 26)
                .onClick.AddListener(() => selection.Select(null));
            Label("ShipTableHeadings", shipPanel.transform, "SYSTEM                                      LIVE STATUS / CAPACITY", 11, new Vector2(20, -82), 580, 24, Gold);
            var names = new[] { "Hull / overall length", "Crew aboard (estimated)", "Maximum crew / berths", "Unoccupied berths", "Onboard reactor", "Colony grid supply / demand", "Food reserve (shared)", "Colony food demand", "Food endurance / current demand", "Water reserve (shared)", "Construction cargo" };
            for (var row = 0; row < names.Length; row++)
            {
                var y = -109 - row * 23;
                Label("ShipMetric" + row, shipPanel.transform, names[row], 12, new Vector2(20, y), 254, 22, Muted);
                shipValues[row] = Label("ShipValue" + row, shipPanel.transform, "", 13, new Vector2(280, y), 320, 22, Ivory);
            }
            Label("ShipDamageHeadings", shipPanel.transform, "DAMAGE LOCATION                  CONDITION / EFFECT", 11, new Vector2(20, -373), 580, 22, Gold);
            shipDamage = Label("ShipDamageTable", shipPanel.transform, "", 12, new Vector2(20, -399), 580, 78, Ivory);
            shipFoodNote = Label("ShipFoodNote", shipPanel.transform, "", 11, new Vector2(20, -483), 412, 48, Muted);
            Button("ToggleShipOperation", shipPanel.transform, new Vector2(449, -502), "", out shipOperationLabel, 150, 34)
                .onClick.AddListener(() => { selectedBuilding?.ToggleOperation(); RefreshBuilding(); });
            Button("OpenOrbitalSupply", shipPanel.transform, new Vector2(20, -520), "ORBITAL RESUPPLY", out _, 206, 30)
                .onClick.AddListener(OpenTenderSupplyPanel);
            shipPanel.SetActive(false);

            shipSupplyPanel = new GameObject("OrbitalSupplyPanel", typeof(RectTransform), typeof(Image));
            shipSupplyPanel.transform.SetParent(parent, false);
            var full = shipSupplyPanel.GetComponent<RectTransform>();
            full.anchorMin = Vector2.zero; full.anchorMax = Vector2.one; full.offsetMin = full.offsetMax = Vector2.zero;
            var supplyBackdrop = CommandUi.Iron;
            supplyBackdrop.a = .78f;
            shipSupplyPanel.GetComponent<Image>().color = supplyBackdrop;
            var supplyCard = Panel("OrbitalSupplyCard", shipSupplyPanel.transform, new Vector2(520, 390), Vector2.zero, TextAnchor.MiddleCenter);
            Label("OrbitalSupplyTitle", supplyCard.transform, "TENDER ORBITAL RUN", 18, new Vector2(24, -20), 472, 30, Gold);
            Label("OrbitalSupplyHint", supplyCard.transform, "Choose a mix. Each completed return uses one of four fuel allotments.",
                12, new Vector2(24, -56), 472, 42, Ivory);
            var labels = new[] { "Crew", "Rations", "Components", "Plasteel" };
            for (var i = 0; i < labels.Length; i++)
            {
                var y = -112 - i * 47;
                Label("OrbitalSupplyLabel" + i, supplyCard.transform, labels[i], 13, new Vector2(30, y), 180, 28, Ivory);
                shipSupplyInputs[i] = SupplyInput(supplyCard.transform, "OrbitalSupplyValue" + i, new Vector2(245, y), "0");
            }
            shipSupplyError = Label("OrbitalSupplyError", supplyCard.transform, "", 11, new Vector2(30, -292), 460, 44, Gold);
            Button("ConfirmOrbitalSupply", supplyCard.transform, new Vector2(30, -350), "DEPART AND RETURN", out _, 250, 34)
                .onClick.AddListener(ConfirmTenderSupplyRun);
            Button("CancelOrbitalSupply", supplyCard.transform, new Vector2(300, -350), "CANCEL", out _, 190, 34)
                .onClick.AddListener(() => shipSupplyPanel.SetActive(false));
            shipSupplyPanel.SetActive(false);
        }

        private InputField SupplyInput(Transform parent, string name, Vector2 position, string value)
        {
            var background = Panel(name + "Box", parent, new Vector2(190, 30), position, TextAnchor.UpperLeft, false);
            var supplyField = CommandUi.Field;
            supplyField.a = .95f;
            background.GetComponent<Image>().color = supplyField;
            var text = Label(name, background.transform, value, 13, new Vector2(8, -4), 172, 22, Ivory);
            var input = background.AddComponent<InputField>();
            input.textComponent = text; input.contentType = InputField.ContentType.DecimalNumber;
            input.text = value;
            return input;
        }

        private void OpenTenderSupplyPanel()
        {
            if (Industry == null || Industry.TenderTripsRemaining <= 0) return;
            foreach (var input in shipSupplyInputs) input.text = "0";
            shipSupplyError.text = $"ORBITAL STOCK  /  CREW {Industry.OrbitalCrewAvailable:N0}  /  RATIONS {Industry.OrbitalRationsAvailable:N0}\n"
                + $"COMPONENTS {Industry.OrbitalSupplyAvailable(MaterialKind.Components):0.#}  /  PLAS {Industry.OrbitalSupplyAvailable(MaterialKind.Plasteel):0.#}  /  RUNS {Industry.TenderTripsRemaining}";
            shipSupplyPanel.SetActive(true);
        }

        private void ConfirmTenderSupplyRun()
        {
            float Read(int index) => float.TryParse(shipSupplyInputs[index].text, out var value) ? value : float.NaN;
            if (Industry != null && Industry.TryOrbitalSupplyRun(Read(0), Read(1), Read(2), Read(3)))
            {
                shipSupplyPanel.SetActive(false);
                AppendEventLog($"Landing tender completed orbital supply run. {Industry.TenderTripsRemaining} fuel runs remain.");
                RefreshShipHud(); RefreshMaterials();
                return;
            }
            shipSupplyError.text = "TRANSFER REJECTED  /  CHECK STOCK, STORAGE, CREW CAPACITY AND RUNS";
        }

        private void RefreshShipHud()
        {
            var ship = selectedBuilding;
            if (ship == null || ship.Id != "ColonyShip") return;
            var modern = settlement.HasExplorator;
            var tender = settlement.ShipLayoutVersion >= 2;
            var capacity = tender ? ShipRules.CrewCapacity : modern ? ShipRules.LegacyCrewCapacity : 240;
            var otherHousing = 0f;
            foreach (var building in settlement.AllBuildings) if (building != ship) otherHousing += building.Economy.capacity;
            var crew = ship.State.offline ? 0 : ShipRules.Occupied(settlement.Population, otherHousing, capacity);
            shipTitle.text = ship.DisplayName;
            var length = ship.GetComponent<MeshFilter>().sharedMesh.bounds.size.z * ship.transform.lossyScale.z;
            shipValues[0].text = tender ? "Landing tender / 500 m" : modern ? "Explorator / 3,000 m" : $"Legacy colony tender / {length:0} m";
            shipValues[1].text = $"{crew:N0}  /  colony {settlement.Population:N0}";
            shipValues[2].text = capacity.ToString("N0");
            shipValues[3].text = $"{Mathf.Max(0, capacity - crew):N0} / {(ship.State.offline ? "offline" : "available")}";
            var reactor = tender ? ShipRules.ReactorMW : modern ? ShipRules.LegacyReactorMW : 0f;
            shipValues[4].text = $"{(ship.State.offline ? 0 : reactor):0} / {reactor:0} MW  {(ship.State.offline ? "OFFLINE" : "ONLINE")}";
            shipValues[5].text = $"{Industry?.PowerGenerated ?? 0:0.#} / {Industry?.PowerDemand ?? 0:0.#} MW";
            shipValues[6].text = $"{resourceSystem.Food:N0} / {FarmRules.FoodCapacity:N0}";
            shipValues[7].text = $"{FarmRules.MonthlyDemand(settlement.Population):N0} food / month";
            shipValues[8].text = $"{ShipRules.EnduranceMonths(resourceSystem.Food, settlement.Population):0.0} months (without resupply)";
            shipValues[9].text = $"{resourceSystem.Water:N0} / {ResourceSystem.WaterCapacity:N0}";
            shipValues[10].text = Industry == null || !Industry.HasExpeditionCargo ? "No manifest" : $"{Industry.ShipCargo(MaterialKind.Components):0.#} parts / {Industry.ShipCargo(MaterialKind.Plasteel):0.#} plasteel";
            shipDamage.text = tender
                ? "Orbital damage                         Drive and keel failure / stable orbit only\nAtmospheric entry                    Main vessel cannot land / tender required\nLanding tender                         500 m hull / descent systems nominal"
                : modern
                ? "Starboard / lower keel             Impact breach / crushed and buried plating\nPort outer drive                      Burnt-out exhaust / grounded, hull mostly intact\nDorsal plating                        Patch repairs / berths and reactor intact"
                : "Historical tender                    Original compact hull and saved supplies retained\nNew expeditions embark aboard the 3 km Explorator vessel.";
            shipOperationLabel.text = ship.State.offline ? "RESUME REACTOR" : "SUSPEND SHIP";
            shipFoodNote.text = tender
                ? $"Orbital loadout supplied. Runs left: {Industry?.TenderTripsRemaining ?? 0}. Food use continues while suspended."
                : modern ? "Embarked: 120,000 food = 12 months at 10,000 crew; 20 at 6,000.\nCrew shares colony population; food use continues while suspended."
                : "Historical crew, berths and food are retained on load.\nStart a new expedition to deploy the 3 km Explorator.";
        }
    }
}
