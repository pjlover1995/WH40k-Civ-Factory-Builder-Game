using System;
using WH30K.Gameplay.Construction;

namespace WH30K.Sim
{
    public enum EarthworkMode { Move, Flatten, Raise, Lower, Demolish }

    [Serializable]
    public struct TerrainEditStamp
    {
        public float x, y, z, radius, value;
        public int mode;
    }

    [Serializable]
    public sealed class EarthworksSnapshot
    {
        public bool hasVehicle;
        public float x, y, z, forwardX, forwardY, forwardZ;
        public bool travelling;
        public float targetX, targetY, targetZ;
        public int command;
        public float radius, value, work;
        public TerrainEditStamp[] edits;
    }

    // Pure save and height rules, shared by the rendered surface, picking and tests.
    public static class EarthworksRules
    {
        public const int MaxEdits = 512;
        public const float WorkSeconds = 4, Speed = 9, VehicleRadius = 12, Reach = 50;

        // Compatibility for saves produced by Unity's short-vector normalization bug.
        // Repair only an exactly zero heading; other malformed state remains rejected.
        public static bool RecoverZeroHeading(EarthworksSnapshot s)
        {
            if(s==null || !s.hasVehicle || s.forwardX!=0 || s.forwardY!=0 || s.forwardZ!=0
                || !ConstructionSave.ValidDirection(s.x,s.y,s.z))return false;
            double x=0,y=0,z=0;
            if(ConstructionSave.ValidDirection(s.targetX,s.targetY,s.targetZ))
            {
                var dot=((double)s.x*s.targetX+(double)s.y*s.targetY+(double)s.z*s.targetZ)
                    /((double)s.x*s.x+(double)s.y*s.y+(double)s.z*s.z);
                x=s.targetX-s.x*dot;y=s.targetY-s.y*dot;z=s.targetZ-s.z*dot;
            }
            var length=Math.Sqrt(x*x+y*y+z*z);
            if(length<1e-12)
            {
                if(Math.Abs(s.y)>.95f){x=0;y=s.z;z=-s.y;}
                else{x=-s.z;y=0;z=s.x;}
                length=Math.Sqrt(x*x+y*y+z*z);
            }
            s.forwardX=(float)(x/length);s.forwardY=(float)(y/length);s.forwardZ=(float)(z/length);
            return true;
        }

        public static float SurfaceDistance(float ax,float ay,float az,float bx,float by,float bz,float radius)
        {
            double x=ax-bx,y=ay-by,z=az-bz;
            return (float)(2*radius*Math.Asin(Math.Min(1,Math.Sqrt(x*x+y*y+z*z)*.5)));
        }

        public static float Weight(float distance, float radius)
        {
            var t = Math.Max(0, Math.Min(1, (radius - distance) / (radius * .35f)));
            return t * t * (3 - 2 * t);
        }

        public static float Apply(float height, TerrainEditStamp edit, float distance)
        {
            var w = Weight(distance, edit.radius);
            return edit.mode == (int)EarthworkMode.Flatten ? height + (edit.value - height) * w
                : height + edit.value * w;
        }

        public static float ClampHeight(float original,float edited,float seaLevel)
            => edited == original ? original : Math.Max(Math.Max(seaLevel+2,original-80),Math.Min(original+80,edited));

        public static bool ValidEdit(TerrainEditStamp s)
            => ConstructionSave.ValidDirection(s.x, s.y, s.z)
                && ColonyRules.IsFinite(s.radius) && s.radius >= 16 && s.radius <= 64
                && ColonyRules.IsFinite(s.value)
                && (s.mode == (int)EarthworkMode.Flatten ? s.value >= 100 && s.value <= WH30K.Planet.PlanetScale.MaximumSurfaceRadius
                    : s.mode == (int)EarthworkMode.Raise ? s.value >= 2 && s.value <= 8
                    : s.mode == (int)EarthworkMode.Lower && s.value <= -2 && s.value >= -8);

        public static bool Valid(EarthworksSnapshot s)
        {
            if (s == null) return true; // Schemas 0–9 have no earthworks.
            if (s.edits != null)
            {
                if (s.edits.Length > MaxEdits) return false;
                foreach (var edit in s.edits) if (!ValidEdit(edit)) return false;
            }
            if (!s.hasVehicle) return !s.travelling;
            if (!ConstructionSave.ValidDirection(s.x, s.y, s.z)
                || !ConstructionSave.ValidDirection(s.forwardX, s.forwardY, s.forwardZ)
                || Math.Abs(s.x*s.forwardX+s.y*s.forwardY+s.z*s.forwardZ) > .02f) return false;
            if (!s.travelling) return true;
            if (!ConstructionSave.ValidDirection(s.targetX, s.targetY, s.targetZ)
                || !ColonyRules.IsFinite(s.work) || s.work < 0 || s.work > WorkSeconds
                || s.command < 0 || s.command > (int)EarthworkMode.Lower) return false;
            return s.command == 0 || ValidEdit(new TerrainEditStamp { x=s.targetX, y=s.targetY, z=s.targetZ,
                radius=s.radius, value=s.value, mode=s.command });
        }
    }
}
