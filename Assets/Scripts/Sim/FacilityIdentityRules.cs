using System;
using System.Collections.Generic;

namespace WH30K.Sim
{
    public static class FacilityIdentityRules
    {
        public const int NameLimit = 40;
        public static bool ValidName(string name) => string.IsNullOrEmpty(name) || name.Length <= NameLimit
            && name.Trim() == name && !string.IsNullOrWhiteSpace(name) && !HasControl(name);
        private static bool HasControl(string value) { foreach (var c in value) if (char.IsControl(c)) return true; return false; }
        public static string NextName(string prefix, IEnumerable<string> names)
        {
            var used = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            for (var serial = 1; serial <= 10000; serial++) if (!used.Contains(prefix + " " + serial)) return prefix + " " + serial;
            return prefix + " " + Guid.NewGuid().ToString("N").Substring(0,8);
        }
        public static bool CompatibleFarms(FarmState a, FarmState b) => a != null && b != null && a.technologyLevel == b.technologyLevel;
        public static bool ValidCollection(string[] types,BuildingOperationState[] states,bool requireIdentity)
        {
            if(types==null || states==null || types.Length!=states.Length)return false;
            var identities=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(var i=0;i<states.Length;i++)
            {
                if(!BuildingOperationRules.Valid(states[i]))return false;
                var id=states[i].instanceId;var name=states[i].instanceName;
                if(requireIdentity && (string.IsNullOrEmpty(id)||string.IsNullOrEmpty(name)))return false;
                if(requireIdentity && types[i]=="Farm" && (states[i].farm==null || string.IsNullOrEmpty(states[i].farm.entityId)))return false;
                if(!string.IsNullOrEmpty(id) && !identities.TryAdd(Guid.Parse(id).ToString("N"),i))return false;
                if(!string.IsNullOrEmpty(name) && !names.Add(types[i]+"/"+name))return false;
            }
            for(var i=0;i<states.Length;i++)if(types[i]=="Farm" && !string.IsNullOrEmpty(states[i].farm?.entityId))
            {
                if(!identities.TryGetValue(Guid.Parse(states[i].farm.entityId).ToString("N"),out var root) || types[root]!="Farm"
                    || !CompatibleFarms(states[i].farm,states[root].farm))return false;
                if(!string.IsNullOrEmpty(states[root].farm.entityId) && Guid.Parse(states[root].farm.entityId)!=Guid.Parse(states[root].instanceId))return false;
            }
            return true;
        }
    }
}
