using UnityEngine;
using WH30K.Sim;
using WH30K.Sim.Environment;

namespace WH30K.Gameplay.Construction
{
    /// <summary>
    /// Definition for a buildable structure that can be deployed onto the planetary grid.
    /// ScriptableObject assets using this definition are discovered at runtime via Resources.
    /// </summary>
    [CreateAssetMenu(menuName = "WH30K/Construction/Buildable Structure", fileName = "BuildableStructure")]
    public class BuildableStructure : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string id = "Outpost";
        [SerializeField] private string displayName = "Frontier Outpost";

        [Header("Economy")]
        [SerializeField] private float constructionCost = 250f;
        [SerializeField] private float populationCapacity = 160f;
        [SerializeField] private float workforceSlots = 60f;
        [SerializeField] private float productionPerWorker = 1.4f;
        [SerializeField] private float upkeepPerCycle = 8f;
        [SerializeField] private EnvironmentImpact environmentImpact = new EnvironmentImpact
        {
            co2Delta = 0.2f, o2Delta = -0.001f, waterDelta = 0.015f, temperatureDelta = 0.0001f
        };

        [Header("Presentation")]
        [SerializeField] private Vector3 dimensions = new Vector3(40f, 60f, 40f);
        [SerializeField] private Color previewColor = new Color(0.45f, 0.95f, 0.45f, 0.6f);
        [SerializeField] private Color structureColor = new Color(0.6f, 0.6f, 0.72f, 1f);

        /// <summary>
        /// Unique identifier for the structure. Falls back to the asset name when not provided.
        /// </summary>
        public string Id => string.IsNullOrWhiteSpace(id) ? name : id;

        /// <summary>
        /// Human readable name shown in UI elements.
        /// </summary>
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
        public float ConstructionCost => Mathf.Max(0f, constructionCost);
        public BuildingEconomy Economy => new BuildingEconomy
        {
            capacity = Mathf.Max(0f, populationCapacity), jobs = Mathf.Max(0f, workforceSlots),
            outputPerWorker = Mathf.Max(0f, productionPerWorker), upkeep = Mathf.Max(0f, upkeepPerCycle)
        };
        public EnvironmentImpact IndustryImpact => environmentImpact;

        /// <summary>
        /// Returns the structure's dimensions, clamped to a sensible minimum to avoid degenerate visuals.
        /// The value represents width (x), height (y) and depth (z) in Unity units.
        /// </summary>
        public Vector3 Dimensions
        {
            get
            {
                return new Vector3(
                    Mathf.Max(20f, dimensions.x),
                    Mathf.Max(20f, dimensions.y),
                    Mathf.Max(20f, dimensions.z));
            }
        }

        /// <summary>
        /// Semi-transparent colour used for the holographic preview while positioning the structure.
        /// </summary>
        public Color PreviewColor => previewColor;

        /// <summary>
        /// Colour applied to the placed structure's material.
        /// </summary>
        public Color StructureColor => structureColor;
    }
}
