using System;
using System.Collections.Generic;

namespace WH30K.Gameplay.Construction
{
    [Serializable]
    public struct PlacedStructureSnapshot
    {
        public string id;
        public int face;
        public int x;
        public int y;
        public WH30K.Sim.BuildingOperationState operation;
    }

    public static class ConstructionSave
    {
        public static bool Validate(PlacedStructureSnapshot[] structures, int resolution,
            ISet<string> knownIds, out string error)
        {
            error = null;
            if (resolution < 8 || resolution > 256)
            {
                error = "Unsupported construction grid resolution.";
                return false;
            }
            if (structures == null) return true; // Saves from the original prototype have no placements.
            if (structures.Length > 4096)
            {
                error = "Too many saved structures.";
                return false;
            }
            var occupied = new HashSet<string>();
            foreach (var structure in structures)
            {
                if (string.IsNullOrEmpty(structure.id) || !knownIds.Contains(structure.id)
                    || !WH30K.Sim.BuildingOperationRules.Valid(structure.operation)
                    || structure.face < 0 || structure.face > 5 || structure.x < 0 || structure.x >= resolution
                    || structure.y < 0 || structure.y >= resolution
                    || !occupied.Add($"{structure.face}:{structure.x}:{structure.y}"))
                {
                    error = "Save contains an unknown building, invalid cell, or duplicate placement.";
                    return false;
                }
            }
            return true;
        }
    }
}
