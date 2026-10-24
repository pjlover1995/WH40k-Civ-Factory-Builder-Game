using UnityEngine;
using WH30K.Sim;
using WH30K.Sim.Environment;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;

namespace WH30K.Gameplay.Construction
{
    public sealed class ManagedBuilding : MonoBehaviour
    {
        private BuildingEconomy basicEconomy;
        private EnvironmentImpact basicImpact;
        private ResourceSystem resources;
        private Settlement settlement;
        private BuildingEconomy economy;
        private EnvironmentImpact impact;
        public string Id { get; private set; }
        public string DisplayName { get; private set; }
        public BuildingOperationState State { get; private set; }
        public BuildingEconomy Economy => economy;
        public EnvironmentImpact Impact => impact;
        private void RefreshOperation()
        {economy=BuildingOperationRules.Economy(Id,basicEconomy,State);impact=BuildingOperationRules.Impact(Id,basicImpact,State);}
        public float UpgradeCost => BuildingOperationRules.UpgradeCost(State);
        public bool CanUpgrade => State.level < 2 && resources != null && resources.CanAfford(UpgradeCost);

        public void Configure(string id, string title, BuildingEconomy economy, EnvironmentImpact impact,
            ResourceSystem stockpile, Settlement owner, BuildingOperationState state = default)
        {
            Id = id; DisplayName = title; basicEconomy = economy; basicImpact = impact;
            resources = stockpile; settlement = owner; State = state;
            RefreshOperation();
            var bounds = GetComponent<MeshFilter>().sharedMesh.bounds;
            var collider = gameObject.AddComponent<BoxCollider>();
            collider.center = bounds.center; collider.size = bounds.size;
        }
        public void Restore(BuildingOperationState state) { State = state; RefreshOperation(); }
        public bool SetFocus(int focus)
        {
            if (focus < 0 || focus > 2) return false;
            var state = State; state.focus = focus; State = state;
            RefreshOperation();
            settlement.RefreshReadout(); return true;
        }
        public bool Upgrade()
        {
            if (!CanUpgrade || !resources.TrySpend(UpgradeCost)) return false;
            var state = State; state.level++; State = state;
            RefreshOperation();
            settlement.RefreshReadout(); return true;
        }
        public void ToggleOperation()
        {
            var state = State; state.offline = !state.offline; State = state;
            RefreshOperation();
            settlement.RefreshReadout();
        }
    }
}
