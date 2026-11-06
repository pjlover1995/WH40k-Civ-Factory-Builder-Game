using System;
using System.Collections.Generic;

namespace WH30K.Sim
{
    [Serializable] public sealed class IndustrialBay { public int x, z; }
    [Serializable] public sealed class ManufactorumState
    {
        public int version = 1;
        public List<IndustrialBay> bays = new List<IndustrialBay>();
        public IndustrialBay project;
        public float work;
        // Historical pending generic improvements finish with their original paid work/cost.
        public float inheritedCapacity = 1;
    }

    public static class ManufactorumRules
    {
        public const float ExpansionSupplies = 20, ExpansionPlasteel = 2, Duration = 60;
        public static float Scale(ManufactorumState state) => (state?.inheritedCapacity ?? 1) + (state?.bays.Count ?? 0);
        public static float ProductionScale(BuildingOperationState state)
            => state.manufactorum == null ? 1 + state.level * .25f : Scale(state.manufactorum);
        public static float Upkeep(ManufactorumState state, float seconds, float operation)
            => state == null || !ColonyRules.IsFinite(seconds) || !ColonyRules.IsFinite(operation) || seconds <= 0 ? 0 : state.bays.Count * seconds * Math.Max(0, Math.Min(1, operation)) / (float)ColonyCalendar.SecondsPerMonth;
        public static int RequiredTier(int product) => product == 5 ? 3 : product == 2 || product == 3 ? 2 : 1;
        public static float TierSupplies(int level) => 12 * (level + 1);
        public static float TierPlasteel(int level) => 6 * (level + 1);
        public static bool Same(IndustrialBay a, IndustrialBay b) => a != null && b != null && a.x == b.x && a.z == b.z;
        public static bool Adjacent(IndustrialBay a, IndustrialBay b)
            => Math.Abs((long)a.x - b.x) + Math.Abs((long)a.z - b.z) == 1;
        public static bool Occupied(ManufactorumState state, IndustrialBay bay)
            => bay.x == 0 && bay.z == 0 || state.bays.Exists(b => Same(b, bay));
        // Search only the connected frontier. There is no gameplay bay count or expansion tier cap.
        public static IndustrialBay Candidate(ManufactorumState state, Func<IndustrialBay, bool> clear)
        {
            var seen = new HashSet<(int,int)>();
            var parents = new List<IndustrialBay> { new IndustrialBay() }; parents.AddRange(state.bays);
            foreach (var parent in parents)
                foreach (var d in new[] { (1,0), (0,1), (-1,0), (0,-1) })
                {
                    var x = (long)parent.x + d.Item1; var z = (long)parent.z + d.Item2;
                    if (x <= int.MinValue || x >= int.MaxValue || z <= int.MinValue || z >= int.MaxValue) continue;
                    var bay = new IndustrialBay { x = (int)x, z = (int)z };
                    if (seen.Add((bay.x,bay.z)) && !Occupied(state,bay) && clear(bay)) return bay;
                }
            return null;
        }
        public static bool Advance(ManufactorumState state, float seconds, bool offline, bool clear)
        {
            if (state.project == null || offline || !clear || !ColonyRules.IsFinite(seconds) || seconds <= 0) return false;
            state.work = Math.Min(Duration, state.work + seconds);
            if (state.work < Duration) return false;
            state.bays.Add(state.project); state.project = null; state.work = 0; return true;
        }
        public static ManufactorumState NormalizeLoaded(ManufactorumState state)
        {
            if(state == null)return null;
            if(state.project != null && state.project.x == 0 && state.project.z == 0 && state.work == 0)state.project = null;
            if(state.version == 0 && (state.bays == null || state.bays.Count == 0) && state.project == null && state.work == 0
                && state.inheritedCapacity == 0)return null;
            return state;
        }
        public static bool Valid(ManufactorumState state)
        {
            state = NormalizeLoaded(state);
            if (state == null) return true;
            if (state.version != 1 || !ColonyRules.IsFinite(state.inheritedCapacity) || state.inheritedCapacity < 1 || state.inheritedCapacity > 1.5f || state.bays == null || !ColonyRules.IsFinite(state.work) || state.work < 0 || state.work >= Duration
                || state.project == null && (state.work != 0)) return false;
            var connected = new HashSet<(int,int)> { (0,0) };
            foreach (var bay in state.bays)
            {
                if (bay == null || !NearConnected(bay,connected) || !connected.Add((bay.x,bay.z))) return false;
            }
            return state.project == null || !connected.Contains((state.project.x,state.project.z)) && NearConnected(state.project,connected);
        }
        private static bool NearConnected(IndustrialBay bay, HashSet<(int,int)> cells)
            => bay.x != int.MinValue && bay.x != int.MaxValue && bay.z != int.MinValue && bay.z != int.MaxValue
                && (cells.Contains((bay.x-1,bay.z)) || cells.Contains((bay.x+1,bay.z)) || cells.Contains((bay.x,bay.z-1)) || cells.Contains((bay.x,bay.z+1)));
    }
}
