using System;

namespace WH30K.Sim
{
    [Serializable]
    public sealed class FarmState
    {
        public int addedFields;
        public int technologyLevel;
        public string entityId;
        public FarmParcel[] claimedParcels;
        public int layoutSeed; // Additive: old farms derive a seed once from their saved surface position.
        public float productiveSeconds;
    }

    [Serializable]
    public sealed class FarmParcel { public int field; public FarmPoint[] polygon; }

    /// <summary>Standard farm technology. Future technology tiers should provide separate growth rules here.</summary>
    public static class FarmRules
    {
        public const int MaxAddedFields = 120;
        public const int FirstSatelliteAt = 24;
        public const int SatelliteInterval = 40;
        public const int FieldsPerPatch = 10;
        public const float GrowthSeconds = (float)ColonyCalendar.SecondsPerMonth * 6;
        public const float BaseFoodPerMonth = 6000;
        public const float FoodPerColonistPerMonth = 1;
        public const float StartingFood = 960;
        public const float FoodCapacity = 140000;
        public const float FertilizerPerMonth = 6;
        public const float ComponentsPerMonth = 1;
        public const float FoodBufferCapacity = 600;
        public const float FoodCargoCapacity = 30;
        public static float Scale(int fields) => 1 + fields * .2f;
        public static int Fields(int added) => 5 + added;
        public static int Buildings(int added) => added < FirstSatelliteAt ? 1 : 2 + (added - FirstSatelliteAt) / SatelliteInterval;
        public static int BuildingAt(int index) => index == 0 ? 0 : FirstSatelliteAt + (index - 1) * SatelliteInterval;
        public static int BuildingPatch(int index) => index == 0 ? 0 : (Fields(BuildingAt(index)) - 1) / FieldsPerPatch;
        public static bool HasBuildingYard(int patch)
        {
            for (var index = 0; index < Buildings(MaxAddedFields); index++) if (BuildingPatch(index) == patch) return true;
            return false;
        }
        public static string BuildingDesign(int index) => index == 0 ? "Founding barn" : index == 1 ? "L-wing storehouse"
            : index == 2 ? "Tower granary" : "Machinery workshop";
        public static float MonthlyDemand(float population) => Math.Max(0, population) * FoodPerColonistPerMonth;
        public static float Multiplier(int fertilizer) => fertilizer < 0 ? 0 : 1 + fertilizer * .25f;
        public static bool Valid(FarmState state) => state == null || state.addedFields >= 0 && state.addedFields <= MaxAddedFields
            && state.technologyLevel >= 0 && state.technologyLevel <= 16 && (string.IsNullOrEmpty(state.entityId) || Guid.TryParse(state.entityId,out _))
            && ValidParcels(state) && ColonyRules.IsFinite(state.productiveSeconds) && state.productiveSeconds >= 0 && state.productiveSeconds <= GrowthSeconds;
        private static bool ValidParcels(FarmState state)
        {
            if(state.claimedParcels==null)return true;
            if(state.claimedParcels.Length>Fields(state.addedFields))return false;
            var used=new System.Collections.Generic.HashSet<int>();
            foreach(var parcel in state.claimedParcels)if(parcel==null || parcel.field<0 || parcel.field>=Fields(state.addedFields)
                || !used.Add(parcel.field) || !TerritoryRules.ValidPolygon(parcel.polygon))return false;
            return true;
        }
        public static FarmPoint[] Parcel(FarmState state,int seed,int field)
        {
            if(state?.claimedParcels!=null)foreach(var parcel in state.claimedParcels)if(parcel.field==field)return parcel.polygon;
            return FarmLayoutRules.Parcel(seed,field);
        }
        public static FarmState Clone(FarmState state)
        {
            if(state==null)return new FarmState();
            FarmParcel[] parcels=null;
            if(state.claimedParcels!=null){parcels=new FarmParcel[state.claimedParcels.Length];for(var i=0;i<parcels.Length;i++)
                parcels[i]=new FarmParcel{field=state.claimedParcels[i].field,polygon=(FarmPoint[])state.claimedParcels[i].polygon.Clone()};}
            return new FarmState{layoutSeed=state.layoutSeed,addedFields=state.addedFields,productiveSeconds=state.productiveSeconds,
                technologyLevel=state.technologyLevel,entityId=Guid.TryParse(state.entityId,out var entity)?entity.ToString("N"):state.entityId,claimedParcels=parcels};
        }
        public static bool AdvanceGrowth(FarmState state, float seconds, bool fullOperation, bool space)
        {
            if (!ColonyRules.IsFinite(seconds) || seconds <= 0) return false;
            if (!fullOperation) { state.productiveSeconds = 0; return false; }
            state.productiveSeconds = Math.Min(GrowthSeconds, state.productiveSeconds + seconds);
            if (state.productiveSeconds < GrowthSeconds || !space || state.addedFields >= MaxAddedFields) return false;
            state.addedFields++; state.productiveSeconds = 0; return true;
        }
        // Each grade is a complete fertilizer formulation; stronger grades replace basic waste, not stack with it.
        public static float Produce(float[] stock, FarmState state, int maximumGrade, float seconds, float staffing,
            float power, float foodRoom, out int grade, out float operation)
        {
            grade = -1; operation = 0;
            if (seconds <= 0 || !ColonyRules.IsFinite(seconds) || foodRoom <= 0) return 0;
            var scale = Scale(state.addedFields);
            var months = seconds / (float)ColonyCalendar.SecondsPerMonth;
            var fertilizer = FertilizerPerMonth * scale * months;
            var components = ComponentsPerMonth * scale * months;
            var available = Math.Min(Math.Max(0, Math.Min(1, staffing)), Math.Max(0, Math.Min(1, power)));
            available = Math.Min(available, stock[(int)MaterialKind.Components] / components);
            // Prefer a complete lower-grade supply over an under-supplied higher-grade one.
            for (var candidate = Math.Min(2, maximumGrade); candidate >= 0; candidate--)
                if (stock[(int)MaterialKind.BasicFertilizer + candidate] >= fertilizer * available && available > 0)
                { grade = candidate; break; }
            if (grade < 0)
                for (var candidate = Math.Min(2, maximumGrade); candidate >= 0; candidate--)
                    if (stock[(int)MaterialKind.BasicFertilizer + candidate] > 0) { grade = candidate; break; }
            if (grade < 0 || available <= 0) return 0;
            operation = Math.Min(available, stock[(int)MaterialKind.BasicFertilizer + grade] / fertilizer);
            var potential = BaseFoodPerMonth * scale * months * Multiplier(grade);
            operation = Math.Min(operation, foodRoom / potential);
            stock[(int)MaterialKind.Components] = Math.Max(0, stock[(int)MaterialKind.Components] - components * operation);
            stock[(int)MaterialKind.BasicFertilizer + grade] = Math.Max(0, stock[(int)MaterialKind.BasicFertilizer + grade] - fertilizer * operation);
            return potential * operation;
        }
        public static float WastePerMonth(string id, float population, float jobs)
            => id == "HabBlock" || id == "ColonyShip" ? Math.Max(0, population) * .06f
            : id == "Outpost" || id == "Factory" || IndustryRules.Facility(id) && id != "Farm" && id != "StorageDepot"
                ? Math.Max(0, jobs) * .12f : 0;
        public static void FieldPosition(int field, out float x, out float z)
        {
            var center = FarmLayoutRules.Center(FarmLayoutRules.Parcel(0, field)); x = center.x; z = center.z;
        }
        public static void ClusterPosition(int cluster, out float x, out float z)
        {
            // Expand in connected 2x2 blocks; the first four existing patch anchors stay unchanged.
            var block = cluster / 4; var cell = cluster % 4;
            x = (cell % 2 + (block % 2) * 2) * .98f;
            z = (cell / 2 + (block / 2) * 2) * .98f;
        }
        public static int PreviousPatch(int patch)
        {
            ClusterPosition(patch, out var x, out var z);
            for (var i = patch - 1; i >= 0; i--)
            {
                ClusterPosition(i, out var px, out var pz);
                if (Math.Abs(px - (x - .98f)) < .001f && Math.Abs(pz - z) < .001f) return i;
            }
            for (var i = patch - 1; i >= 0; i--)
            {
                ClusterPosition(i, out var px, out var pz);
                if (Math.Abs(px - x) < .001f && Math.Abs(pz - (z - .98f)) < .001f) return i;
            }
            return -1;
        }
        public static void BuildingPosition(int index, out float x, out float z) => ClusterPosition(BuildingPatch(index), out x, out z);
    }
}
