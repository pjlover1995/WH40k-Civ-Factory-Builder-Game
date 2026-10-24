using System;

namespace WH30K.Sim
{
    public enum MaterialKind { IronOre, CopperOre, Stone, Biomass, Crystal, Metal, Components }
    [Serializable] public class FacilityStock { public float[] quantities = new float[7]; public float work; }
    [Serializable] public struct DepositStock { public int id, kind; public float remaining; public ExcavationState excavation; }
    [Serializable] public struct CargoStock { public int source, target, kind; public float amount, progress; }
    [Serializable] public class IndustrySnapshot
    { public FacilityStock[] facilities; public DepositStock[] deposits; public CargoStock[] cargo; public int geologyVersion; public MiningConflict conflict; }

    public static class IndustryRules
    {
        public const int MaterialCount = 7;
        public const float StorageLimit = 120f;
        public static bool ValidStock(float[] stock)
        {
            if (stock == null || stock.Length != MaterialCount) return false;
            foreach (var value in stock)
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > StorageLimit) return false;
            return true;
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
                default: return "Components";
            }
        }
        public static bool Extractor(string id) => id == "Mine" || id == "Quarry" || id == "Harvester";
        public static bool Facility(string id) => Extractor(id) || id == "Smelter" || id == "Works";
        public static bool Accepts(string id, MaterialKind kind)
            => id == "Smelter" ? kind == MaterialKind.IronOre || kind == MaterialKind.CopperOre || kind == MaterialKind.Biomass
            : id == "Works" && (kind == MaterialKind.Metal || kind == MaterialKind.Stone || kind == MaterialKind.Crystal);
        public static bool CanExtract(string id, MaterialKind kind)
            => id == "Mine" ? kind == MaterialKind.IronOre || kind == MaterialKind.CopperOre || kind == MaterialKind.Crystal
            : id == "Quarry" ? kind == MaterialKind.Stone : id == "Harvester" && kind == MaterialKind.Biomass;
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
            return false;
        }
        public static double DayAngle(double simulationSeconds) => simulationSeconds % 180.0 / 180.0 * 360.0;
        public static bool Valid(IndustrySnapshot snapshot, int buildingCount)
        {
            if(snapshot==null) return true;
            if(snapshot.facilities==null || snapshot.facilities.Length!=buildingCount || snapshot.deposits==null || snapshot.deposits.Length>4096
                || snapshot.cargo==null || snapshot.cargo.Length>64) return false;
            foreach(var facility in snapshot.facilities)
                if(facility==null || !ValidStock(facility.quantities) || !ColonyRules.IsFinite(facility.work) || facility.work<0 || facility.work>5) return false;
            if(snapshot.geologyVersion<0 || snapshot.geologyVersion>1) return false;
            var ids=new System.Collections.Generic.HashSet<int>();
            foreach(var deposit in snapshot.deposits)
            {
                if(deposit.id<0 || deposit.id>=4096 || !ids.Add(deposit.id) || !ColonyRules.IsFinite(deposit.remaining) || deposit.remaining<0 || deposit.remaining>20000) return false;
                if(snapshot.geologyVersion==0 && deposit.remaining>2000) return false;
                if(snapshot.geologyVersion==1 && (deposit.kind<0 || deposit.kind>4 || deposit.kind!=3 && deposit.excavation==null)) return false;
                if(deposit.excavation!=null && (!MiningRules.Valid(deposit.excavation)
                    || Math.Abs(MiningRules.Remaining(deposit.excavation)-deposit.remaining)>.02f)) return false;
            }
            var conflict=snapshot.conflict;
            if(conflict!=null && conflict.active)
            {
                if(conflict.mine<0 || conflict.mine>=buildingCount || conflict.building<0
                    || conflict.building>=(conflict.core?3:buildingCount) || !conflict.core && conflict.mine==conflict.building
                    || conflict.sector<0 || conflict.sector>=8 || !ids.Contains(conflict.deposit)) return false;
                var record=Array.Find(snapshot.deposits,d=>d.id==conflict.deposit);
                if(record.excavation==null || (record.excavation.blocked & 1<<conflict.sector)!=0) return false;
            }
            var incoming=new float[buildingCount,MaterialCount];
            foreach(var cargo in snapshot.cargo)
            {
                if(cargo.source<0 || cargo.source>=buildingCount || cargo.target < -1 || cargo.target>=buildingCount || cargo.target==cargo.source
                    || cargo.kind<0 || cargo.kind>=MaterialCount || !ColonyRules.IsFinite(cargo.amount) || cargo.amount<=0 || cargo.amount>4
                    || !ColonyRules.IsFinite(cargo.progress) || cargo.progress<0 || cargo.progress>1) return false;
                if(cargo.target>=0) incoming[cargo.target,cargo.kind]+=cargo.amount;
                else if(cargo.kind!=(int)MaterialKind.Components) return false;
            }
            for(var b=0;b<buildingCount;b++) for(var k=0;k<MaterialCount;k++)
                if(snapshot.facilities[b].quantities[k]+incoming[b,k]>StorageLimit+.001f) return false;
            return true;
        }
    }
}
