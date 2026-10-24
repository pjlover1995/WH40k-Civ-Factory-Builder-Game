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
        public bool freePlacement;
        public float nx, ny, nz;
        public bool hasElevation;
        public float elevation;
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
                    || structure.hasElevation && (!WH30K.Sim.ColonyRules.IsFinite(structure.elevation) || structure.elevation<100 || structure.elevation>100000)
                    || structure.face < 0 || structure.face > 5 || structure.x < 0 || structure.x >= resolution
                    || structure.y < 0 || structure.y >= resolution
                    || (structure.freePlacement ? !ValidDirection(structure.nx, structure.ny, structure.nz)
                        || !occupied.Add($"p:{Math.Round(structure.nx,6)}:{Math.Round(structure.ny,6)}:{Math.Round(structure.nz,6)}")
                        : !occupied.Add($"{structure.face}:{structure.x}:{structure.y}")))
                {
                    error = "Save contains an unknown building, invalid cell, or duplicate placement.";
                    return false;
                }
            }
            return true;
        }

        public static bool ValidDirection(float x, float y, float z)
        {
            var length = x * x + y * y + z * z;
            return !float.IsNaN(length) && !float.IsInfinity(length) && length > 0.99f && length < 1.01f;
        }
    }
}
