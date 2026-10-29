using System;
using UnityEngine;
using WH30K.Sim.Environment;

namespace WH30K.Sim
{
    [Serializable]
    public struct RecipeAmount
    {
        public MaterialKind material;
        [Min(0)] public float amount;
    }

    /// <summary>Data-driven industrial recipe and operating requirements for a building ID.</summary>
    [CreateAssetMenu(menuName = "WH30K/Industry/Building Profile", fileName = "IndustrialBuildingProfile")]
    public sealed class IndustrialBuildingProfile : ScriptableObject
    {
        [SerializeField] private string buildingId;
        [SerializeField, Min(0)] private float powerDemand;
        [SerializeField, Min(0)] private float powerOutput;
        [SerializeField, Min(0)] private float generatorFuelPerSecond;
        [SerializeField, Min(0)] private float extractionPerWorkCycle = 1;
        [SerializeField, Min(0)] private float localStorageCapacity = 120;
        [SerializeField] private RecipeAmount[] inputs = Array.Empty<RecipeAmount>();
        [SerializeField] private RecipeAmount[] outputs = Array.Empty<RecipeAmount>();
        [SerializeField] private EnvironmentImpact operatingImpact;

        public string BuildingId => buildingId;
        public float PowerDemand => powerDemand;
        public float PowerOutput => powerOutput;
        public float GeneratorFuelPerSecond => generatorFuelPerSecond;
        public float ExtractionPerWorkCycle => extractionPerWorkCycle;
        public float LocalStorageCapacity => localStorageCapacity;
        public RecipeAmount[] Inputs => inputs;
        public RecipeAmount[] Outputs => outputs;
        public EnvironmentImpact OperatingImpact => operatingImpact;

        public bool TryProcess(float[] stock, float outputCapacity)
        {
            if (stock == null || stock.Length != IndustryRules.MaterialCount || inputs == null || outputs == null)
                return false;
            for (var i = 0; i < inputs.Length; i++)
                if (inputs[i].amount < 0 || stock[(int)inputs[i].material] + .0001f < inputs[i].amount) return false;
            for (var i = 0; i < outputs.Length; i++)
                if (outputs[i].amount < 0 || stock[(int)outputs[i].material] + outputs[i].amount > outputCapacity + .0001f) return false;
            for (var i = 0; i < inputs.Length; i++) stock[(int)inputs[i].material] -= inputs[i].amount;
            for (var i = 0; i < outputs.Length; i++) stock[(int)outputs[i].material] += outputs[i].amount;
            return inputs.Length > 0 || outputs.Length > 0;
        }
    }
}
