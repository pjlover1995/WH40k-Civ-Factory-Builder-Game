using System;
using WH30K.Gameplay.Construction;

namespace WH30K.Sim
{
    // Keep the original seven indices stable because version-6 saves store them as arrays.
    public enum MaterialKind { IronOre, CopperOre, Stone, Biomass, Crystal, Metal, Components, Promethium, Plasteel, BasicFertilizer, IntermediateFertilizer, AdvancedFertilizer }
    [Serializable] public class FacilityStock { public float[] quantities = new float[IndustryRules.MaterialCount]; public float work, food; public FacilityConfiguration configuration; }
    [Serializable] public struct DepositStock { public int id, kind; public float remaining, initialVolume; public bool discovered; public ExcavationState excavation; public float x, y, z; }
    [Serializable] public struct CargoStock { public bool food; public int source, target, kind; public float amount, progress; }
    [Serializable] public class IndustrySnapshot
    {
        public FoundingProgress founding;
        public FacilityStock[] facilities; public DepositStock[] deposits; public CargoStock[] cargo;
        public float[] producedTotals, consumedTotals, productionRates, consumptionRates;
        public DiscoveryRecord[] discoveries;
        public bool[] exploredCells;
        public float[] expeditionCargo;
        public int facilityVersion;
        public int geologyVersion; public int materialVersion; public int explorationVersion; public MiningConflict conflict;
    }

    public static class IndustryRules
    {
        public const int LegacyMaterialCount = 7;
        public const int PreviousMaterialCount = 9;
        public const int MaterialCount = 12;
        public const float StorageLimit = 120f;
        public static float Capacity(MaterialKind kind, float storageBonus = 0f)
            => StorageLimit + (float)Math.Max(0, storageBonus);
        public static float PowerFraction(float generation, float demand)
            => !ColonyRules.IsFinite(generation) || !ColonyRules.IsFinite(demand) || demand <= 0 ? 0
                : (float)Math.Max(0, Math.Min(1, generation / demand));
        public static float FuelLimitedPower(float output,float fuelPerSecond,float availableFuel,float staffing,float seconds)
        {
            if(!ColonyRules.IsFinite(output)||!ColonyRules.IsFinite(fuelPerSecond)||!ColonyRules.IsFinite(availableFuel)
                ||!ColonyRules.IsFinite(staffing)||!ColonyRules.IsFinite(seconds)||output<=0||availableFuel<=0||seconds<=0)return 0;
            var staffed=Math.Max(0,Math.Min(1,staffing))*output;
            return fuelPerSecond<=0?staffed:(float)Math.Min(staffed,availableFuel*output/(fuelPerSecond*seconds));
        }
        // Dispatch only power that this generator can actually sustain. A nearly empty
        // generator must leave the rest of the network's load for the next generator.
        public static float GeneratorFuelForLoad(float output, float fuelPerSecond, float availableFuel,
            float staffing, float seconds, ref float remainingLoad)
        {
            if (!ColonyRules.IsFinite(remainingLoad) || remainingLoad <= 0) return 0;
            var power = Math.Min(remainingLoad, FuelLimitedPower(output, fuelPerSecond, availableFuel, staffing, seconds));
            if (power <= 0) return 0;
            remainingLoad = Math.Max(0, remainingLoad - power);
            return fuelPerSecond <= 0 ? 0 : Math.Min(availableFuel, fuelPerSecond * (power / output) * seconds);
        }
        public static bool ShareLogistics(int sourceComponent, int targetComponent)
            => sourceComponent >= 0 && sourceComponent == targetComponent;
        public static bool ValidStock(float[] stock)
        {
            if (stock == null || stock.Length != MaterialCount && stock.Length != LegacyMaterialCount && stock.Length != PreviousMaterialCount) return false;
            foreach (var value in stock)
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0
                    || value > (stock.Length == LegacyMaterialCount ? StorageLimit : StorageLimit * 16)) return false;
            return true;
        }
        public static float[] UpgradeStock(float[] legacy)
        {
            if (legacy != null && legacy.Length == MaterialCount) return legacy;
            var expanded = new float[MaterialCount];
            if (legacy != null) Array.Copy(legacy, expanded, Math.Min(legacy.Length, MaterialCount));
            return expanded;
        }
        public static string Name(MaterialKind kind)
        {
            switch (kind)
            {
                case MaterialKind.IronOre: return "Iron ore";
                case MaterialKind.CopperOre: return "Copper ore";
                case MaterialKind.Stone: return "Stone";
                case MaterialKind.Biomass: return "Biomass";
                case MaterialKind.Crystal: return "Crystals";
                case MaterialKind.Metal: return "Refined metal";
                case MaterialKind.Components: return "Manufactured components";
                case MaterialKind.Promethium: return "Promethium";
                case MaterialKind.Plasteel: return "Plasteel";
                case MaterialKind.BasicFertilizer: return "Basic fertilizer / reclaimed waste";
                case MaterialKind.IntermediateFertilizer: return "Intermediate fertilizer / compost blend";
                default: return "Advanced fertilizer / mineral nutrient";
            }
        }
        public static bool Extractor(string id) => id == "Mine" || id == "Quarry" || id == "Harvester" || id == "PromethiumExtractor";
        public static bool Facility(string id) => Extractor(id) || id == "Smelter" || id == "Works" || id == "Refinery"
            || id == "Manufactorum" || id == "PromethiumGenerator" || id == "StorageDepot"
            || id == "GroundwaterPump" || id == "DesalinationPump" || id == "Farm" || id == "Cognitorium";
        public static bool Accepts(string id, MaterialKind kind)
            => id == "Smelter" ? kind == MaterialKind.IronOre || kind == MaterialKind.CopperOre || kind == MaterialKind.Biomass
            : id == "Works" && (kind == MaterialKind.Metal || kind == MaterialKind.Stone || kind == MaterialKind.Crystal)
            || id == "Refinery" && (kind == MaterialKind.IronOre || kind == MaterialKind.Promethium)
            || id == "Manufactorum" && kind == MaterialKind.Plasteel
            || id == "Farm" && (kind == MaterialKind.Components || kind >= MaterialKind.BasicFertilizer)
            || id == "PromethiumGenerator" && kind == MaterialKind.Promethium || id == "StorageDepot";
        public static bool CanExtract(string id, MaterialKind kind)
            => id == "Mine" ? kind == MaterialKind.IronOre || kind == MaterialKind.CopperOre || kind == MaterialKind.Crystal
            : id == "Quarry" ? kind == MaterialKind.Stone : id == "Harvester" ? kind == MaterialKind.Biomass
            : id == "PromethiumExtractor" && kind == MaterialKind.Promethium;
        // Atomic recipes: no inputs are consumed until every ingredient and output space is present.
        public static bool Process(string id, float[] stock)
        {
            if (!ValidStock(stock)) return false;
            if (id == "Smelter")
            {
                var ore = stock[0] >= 2 ? 0 : 1;
                if (stock[ore] < 2 || stock[3] < 1 || stock[5] > StorageLimit - 1) return false;
                stock[ore] -= 2; stock[3]--; stock[5]++; return true;
            }
            if (id == "Works")
            {
                if (stock[5] < 2 || stock[2] < 1 || stock[4] < 0.5f || stock[6] > StorageLimit - 1) return false;
                stock[5] -= 2; stock[2]--; stock[4] -= 0.5f; stock[6]++; return true;
            }
            if (id == "Refinery")
            {
                if (stock.Length < MaterialCount) return false;
                if (stock[0] < 2 || stock[7] < 1 || stock[8] > StorageLimit - 1) return false;
                stock[0] -= 2; stock[7]--; stock[8]++; return true;
            }
            if (id == "Manufactorum")
            {
                if (stock.Length < MaterialCount) return false;
                if (stock[8] < 1 || stock[6] > StorageLimit - 1) return false;
                stock[8]--; stock[6]++; return true;
            }
            if (id == "PromethiumGenerator")
            {
                if (stock.Length < MaterialCount) return false;
                if (stock[7] < 0.2f) return false;
                stock[7] -= 0.2f; return true;
            }
            return false;
        }
        public static double DayAngle(double simulationSeconds) => simulationSeconds % 180.0 / 180.0 * 360.0;
        public static bool Valid(IndustrySnapshot snapshot, int buildingCount)
        {
            if(snapshot==null) return true;
            if(!FoundingObjectives.Valid(snapshot.founding)) return false;
            if(snapshot.expeditionCargo != null && snapshot.expeditionCargo.Length > 0 && !ValidStock(snapshot.expeditionCargo)) return false;
            if(snapshot.facilities==null || snapshot.facilities.Length!=buildingCount || snapshot.deposits==null || snapshot.deposits.Length>4096
                || snapshot.cargo==null || snapshot.cargo.Length>64) return false;
            if(snapshot.facilityVersion<0 || snapshot.facilityVersion>1)return false;
            foreach(var facility in snapshot.facilities)
                if(facility==null || !ValidStock(facility.quantities)
                    || snapshot.facilityVersion>0 && (facility.configuration==null || !FacilityRules.Valid(facility.configuration))
                    || (snapshot.materialVersion == 1 && facility.quantities.Length != PreviousMaterialCount && facility.quantities.Length != MaterialCount || snapshot.materialVersion == 2 && facility.quantities.Length != MaterialCount)
                    || snapshot.materialVersion >= 2 && snapshot.facilityVersion > 0 && facility.configuration.storageLimits.Length != MaterialCount
                    || !ColonyRules.IsFinite(facility.food) || facility.food<0 || facility.food>FarmRules.FoodBufferCapacity
                    || !ColonyRules.IsFinite(facility.work) || facility.work<0 || facility.work>5) return false;
            if(snapshot.materialVersion>0 && (!ValidStatistics(snapshot.producedTotals, snapshot.materialVersion) || !ValidStatistics(snapshot.consumedTotals, snapshot.materialVersion)
                || !ValidStatistics(snapshot.productionRates, snapshot.materialVersion) || !ValidStatistics(snapshot.consumptionRates, snapshot.materialVersion))) return false;
            if(snapshot.geologyVersion<0 || snapshot.geologyVersion>4 || snapshot.materialVersion<0 || snapshot.materialVersion>2
                || snapshot.explorationVersion<0 || snapshot.explorationVersion>ExplorationRules.LayoutVersion
                || snapshot.explorationVersion>0 && snapshot.discoveries==null
                || snapshot.exploredCells!=null&&snapshot.exploredCells.Length!=ExplorationRules.SurveyCellCount
                || !ExplorationRules.Valid(snapshot.discoveries)) return false;
            var ids=new System.Collections.Generic.HashSet<int>();
            foreach(var deposit in snapshot.deposits)
            {
                var excavation = deposit.kind == (int)MaterialKind.Biomass || deposit.kind == (int)MaterialKind.Promethium
                    ? MiningRules.NormalizeOptional(deposit.excavation) : deposit.excavation;
                if(snapshot.geologyVersion>=4 && (deposit.id != ids.Count || !ConstructionSave.ValidDirection(deposit.x, deposit.y, deposit.z))) return false;
                if(deposit.id<0 || deposit.id>=4096 || !ids.Add(deposit.id) || !ColonyRules.IsFinite(deposit.remaining) || deposit.remaining<0 || deposit.remaining>20000) return false;
                if(!ColonyRules.IsFinite(deposit.initialVolume) || deposit.initialVolume < 0 || deposit.initialVolume > 20000
                    || deposit.initialVolume > 0 && deposit.initialVolume + .02f < deposit.remaining) return false;
                if(snapshot.geologyVersion==0 && deposit.remaining>2000) return false;
                if(snapshot.geologyVersion==1 && (deposit.kind<0 || deposit.kind>4 || deposit.kind!=3 && deposit.excavation==null)) return false;
                if(snapshot.geologyVersion>=2 && (deposit.kind<0 || deposit.kind>7 || deposit.kind==5 || deposit.kind==6
                    || deposit.kind!=3 && deposit.kind!=7 && deposit.excavation==null)) return false;
                if(excavation!=null && (!MiningRules.Valid(excavation)
                    || Math.Abs(MiningRules.Remaining(excavation)-deposit.remaining)>.02f)) return false;
            }
            var conflict=snapshot.conflict;
            if(conflict!=null && conflict.active)
            {
                if(conflict.mine<0 || conflict.mine>=buildingCount || conflict.building<0
                    || conflict.building>=(conflict.core?3:buildingCount) || !conflict.core && conflict.mine==conflict.building
                    || conflict.sector<0 || conflict.sector>=8 || !ids.Contains(conflict.deposit)) return false;
                var record=Array.Find(snapshot.deposits,d=>d.id==conflict.deposit);
                if(record.excavation==null || record.excavation.layoutVersion==1 || (record.excavation.blocked & 1<<conflict.sector)!=0) return false;
            }
            var incoming=new float[buildingCount,MaterialCount];
            foreach(var cargo in snapshot.cargo)
            {
                if(cargo.source<0 || cargo.source>=buildingCount || !ColonyRules.IsFinite(cargo.amount) || cargo.amount<=0
                    || !ColonyRules.IsFinite(cargo.progress) || cargo.progress<0 || cargo.progress>1)return false;
                if(cargo.food)
                { if(cargo.target!=-2 || cargo.kind!=0 || cargo.amount>FarmRules.FoodCargoCapacity)return false; continue; }
                if(cargo.target < -3 || cargo.target == -2 || cargo.target>=buildingCount || cargo.target==cargo.source || cargo.kind<0 || cargo.kind>=MaterialCount || cargo.amount>4)return false;
                if(cargo.target == -3)
                {
                    if(snapshot.expeditionCargo == null || snapshot.expeditionCargo.Length != MaterialCount) return false;
                    continue;
                }
                if(cargo.target>=0) incoming[cargo.target,cargo.kind]+=cargo.amount;
                else if(cargo.kind!=(int)MaterialKind.Components) return false;
            }
            var capacity=snapshot.materialVersion>0?StorageLimit*16:StorageLimit;
            for(var b=0;b<buildingCount;b++) for(var k=0;k<MaterialCount;k++)
                if((k < snapshot.facilities[b].quantities.Length ? snapshot.facilities[b].quantities[k] : 0)+incoming[b,k]>capacity+.001f) return false;
            return true;
        }
        private static bool ValidStatistics(float[] values, int version)
        {
            if(values==null || values.Length!=MaterialCount && (version >= 2 || values.Length!=PreviousMaterialCount))return false;
            foreach(var value in values)if(!ColonyRules.IsFinite(value)||value<0||value>1e9f)return false;
            return true;
        }
    }
}
