using System;
using System.Text;
using UnityEngine;
using WH30K.Gameplay.Construction;

namespace WH30K.Sim.Resources
{
    public sealed partial class IndustrySystem
    {
        private FacilityConfiguration Configuration(int index)
        {
            if (stocks[index].configuration == null)
            {
                stocks[index].configuration = new FacilityConfiguration();
                if (buildings[index].Id == "Manufactorum")
                { stocks[index].configuration.productWork[0] = stocks[index].work; stocks[index].work = 0; }
            }
            return stocks[index].configuration;
        }
        public FacilityConfiguration FacilitySettings(ManagedBuilding building)
        {
            Synchronize(); var index = Array.IndexOf(buildings, building);
            return index < 0 || building == null || building.Id != "Manufactorum" && building.Id != "StorageDepot"
                ? null : FacilityRules.Clone(Configuration(index));
        }
        public bool SetProduct(ManagedBuilding building, int slot, int product)
        {
            Synchronize(); var index = Array.IndexOf(buildings, building);
            if (index < 0 || building.Id != "Manufactorum" || product >= 0 && product < FacilityRules.ProductCount && ManufactorumRules.RequiredTier(product) > building.State.level + 1 || !FacilityRules.SelectProduct(Configuration(index), slot, product)) return false;
            RebuildConsumers(); return true;
        }
        public void SetBufferLimit(ManagedBuilding building, float limit)
        {
            Synchronize(); var index = Array.IndexOf(buildings, building);
            if (index >= 0 && building.Id == "Manufactorum" && ColonyRules.IsFinite(limit))
                Configuration(index).bufferLimit = Mathf.Clamp(Mathf.Round(limit), 1, IndustryRules.StorageLimit);
        }
        public void SetDepotMode(ManagedBuilding building, bool designated)
        {
            Synchronize(); var index = Array.IndexOf(buildings, building);
            if (index >= 0 && building.Id == "StorageDepot") Configuration(index).designated = designated;
        }
        public bool SetDepotLimit(ManagedBuilding building, MaterialKind kind, float limit)
        {
            Synchronize(); var index = Array.IndexOf(buildings, building);
            return index >= 0 && building.Id == "StorageDepot" && FacilityRules.SetStorageLimit(Configuration(index), kind, limit);
        }
        private void RebuildConsumers()
        {
            foreach (var group in consumers) group.Clear();
            for (var i = 0; i < buildings.Length; i++) for (var k = 0; k < IndustryRules.MaterialCount; k++)
                if (AcceptsInput(i, (MaterialKind)k)) consumers[k].Add(i);
        }
        private void RefreshIncoming()
        {
            Array.Clear(incoming, 0, incoming.Length);
            foreach (var flight in flights) if (flight.state.target >= 0)
                incoming[flight.state.target * IndustryRules.MaterialCount + flight.state.kind] += flight.state.amount;
        }
        private float DeliveryRoom(int index, MaterialKind kind)
        {
            var k = (int)kind;
            if (buildings[index].Id != "StorageDepot")
                return Mathf.Max(0, StoreCapacity(index, kind) - stocks[index].quantities[k] - Incoming(index, k));
            var reserved = 0f;
            for (var material = 0; material < IndustryRules.MaterialCount; material++) reserved += Incoming(index, material);
            return FacilityRules.DepotRoom(Configuration(index), stocks[index].quantities, kind, Incoming(index, k), reserved);
        }
        private string FacilitySummary(int index)
        {
            var config = Configuration(index); var text = new StringBuilder();
            if (buildings[index].Id == "StorageDepot")
            {
                text.Append($"{(config.designated ? "DESIGNATED / reserved item limits" : "GENERAL / first come, first served")}\nStored {FacilityRules.Total(stocks[index].quantities):0.#} / {FacilityRules.DepotCapacity:0} units\n");
                if (FacilityRules.Total(stocks[index].quantities) >= FacilityRules.DepotCapacity) text.Append("STORAGE FULL / awaiting collection\n");
            }
            else
            {
                var count = FacilityRules.ActiveCount(config);
                text.Append($"{count}/4 products / {(count > 0 ? 100f / count : 0):0.#}% work each\nBuffer {config.bufferLimit:0} units per product\n");
                for (var slot = 0; slot < 4; slot++) if (config.products[slot] >= 0)
                    text.Append($"{IndustryRules.Name(FacilityRules.Output(config.products[slot]))}: {FacilityRules.ProductStatus(config, stocks[index].quantities, slot, Incoming(index, (int)FacilityRules.Output(config.products[slot])))}\n");
            }
            return text.ToString();
        }
        public string FacilityReport(ManagedBuilding building)
        {
            Synchronize(); var index = Array.IndexOf(buildings, building); if (index < 0) return "Facility unavailable";
            var config = Configuration(index); var component = networkComponent[index];
            var power = component >= 0 && component < networkPowerFraction.Length ? networkPowerFraction[component] : 0;
            var staffing = WorkersRequired > 0 ? Mathf.Clamp01(WorkersAvailable / WorkersRequired) : 1;
            var focus = building.State.focus == 1 ? 1.4f : building.State.focus == 2 ? .75f : 1;
            var speed = building.State.offline ? 0 : staffing * power * (building.Id == "Manufactorum" ? ManufactorumRules.ProductionScale(building.State) : 1 + building.State.level * .25f) * focus;
            var profile = Profile(building.Id); var count = FacilityRules.ActiveCount(config);
            var text = new StringBuilder($"TIER {building.State.level + 1} / {(building.State.offline ? "SUSPENDED" : "ACTIVE")}   Jobs {building.Economy.jobs:0}\n");
            text.Append($"Staffing {staffing:P0}   Network power {power:P0}   Demand {((profile != null ? profile.PowerDemand : 0) * (building.Id == "Manufactorum" ? ManufactorumRules.ProductionScale(building.State) : 1)):0.#} MW\n");
            var peers = 0;
            for (var i = 0; i < buildings.Length; i++) if (i != index && networkComponent[i] == component) peers++;
            text.Append($"Logistics: {(peers > 0 || connectedToCore[index] ? "CONNECTED" : "ISOLATED")}   CO2 { ((profile != null ? profile.OperatingImpact.co2Delta : 0) * (building.Id == "Manufactorum" ? ManufactorumRules.ProductionScale(building.State) : 1)):0.##}/s at full operation\n");
            text.Append(building.Id == "Manufactorum" ? $"Effective output per line: {(count > 0 ? speed / (5 * count) : 0):0.000}/s when supplied\n" : "Capacity shared across all materials; incoming cargo reserves space.\n");
            for (var k = 0; k < IndustryRules.MaterialCount; k++)
                if (stocks[index].quantities[k] > 0 || Incoming(index, k) > 0)
                    text.Append($"{IndustryRules.Name((MaterialKind)k)} {stocks[index].quantities[k]:0.#} (+{Incoming(index, k):0.#} incoming)   ");
            text.Append("\n" + FacilitySummary(index));
            return text.ToString();
        }
    }
}
