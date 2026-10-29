using UnityEngine;
using UnityEngine.UI;
using WH30K.Sim;
using WH30K.Sim.Resources;

namespace WH30K.UI
{
    public partial class NewGameMenu
    {
        private GameObject shipPanel;
        private Text shipTitle, shipDamage, shipFoodNote;
        private readonly Text[] shipValues = new Text[11];
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
            shipPanel.SetActive(false);
        }

        private void RefreshShipHud()
        {
            var ship = selectedBuilding;
            if (ship == null || ship.Id != "ColonyShip") return;
            var modern = settlement.HasExplorator;
            var capacity = modern ? ShipRules.CrewCapacity : 240;
            var otherHousing = 0f;
            foreach (var building in settlement.AllBuildings) if (building != ship) otherHousing += building.Economy.capacity;
            var crew = ship.State.offline ? 0 : ShipRules.Occupied(settlement.Population, otherHousing, capacity);
            shipTitle.text = ship.DisplayName;
            var length = ship.GetComponent<MeshFilter>().sharedMesh.bounds.size.z * ship.transform.lossyScale.z;
            shipValues[0].text = modern ? "Explorator / 3,000 m" : $"Legacy colony tender / {length:0} m";
            shipValues[1].text = $"{crew:N0}  /  colony {settlement.Population:N0}";
            shipValues[2].text = capacity.ToString("N0");
            shipValues[3].text = $"{Mathf.Max(0, capacity - crew):N0} / {(ship.State.offline ? "offline" : "available")}";
            shipValues[4].text = $"{(ship.State.offline ? 0 : ShipRules.ReactorMW):0} / {ShipRules.ReactorMW:0} MW  {(ship.State.offline ? "OFFLINE" : "ONLINE")}";
            shipValues[5].text = $"{Industry?.PowerGenerated ?? 0:0.#} / {Industry?.PowerDemand ?? 0:0.#} MW";
            shipValues[6].text = $"{resourceSystem.Food:N0} / {FarmRules.FoodCapacity:N0}";
            shipValues[7].text = $"{FarmRules.MonthlyDemand(settlement.Population):N0} food / month";
            shipValues[8].text = $"{ShipRules.EnduranceMonths(resourceSystem.Food, settlement.Population):0.0} months (without resupply)";
            shipValues[9].text = $"{resourceSystem.Water:N0} / {ResourceSystem.WaterCapacity:N0}";
            shipValues[10].text = Industry == null || !Industry.HasExpeditionCargo ? "No manifest" : $"{Industry.ShipCargo(MaterialKind.Components):0.#} parts / {Industry.ShipCargo(MaterialKind.Plasteel):0.#} plasteel";
            shipDamage.text = modern
                ? "Starboard / lower keel             Impact breach / crushed and buried plating\nPort outer drive                      Burnt-out exhaust / grounded, hull mostly intact\nDorsal plating                        Patch repairs / berths and reactor intact"
                : "Historical tender                    Original compact hull and saved supplies retained\nNew expeditions embark aboard the 3 km Explorator vessel.";
            shipOperationLabel.text = ship.State.offline ? "RESUME REACTOR" : "SUSPEND SHIP";
            shipFoodNote.text = modern ? "Embarked: 120,000 food = 12 months at 10,000 crew; 20 at 6,000.\nCrew shares colony population; food use continues while suspended."
                : "Historical crew, berths and food are retained on load.\nStart a new expedition to deploy the 3 km Explorator.";
        }
    }
}
