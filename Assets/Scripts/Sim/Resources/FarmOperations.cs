using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using WH30K.Gameplay.Construction;

namespace WH30K.Sim.Resources
{
    public sealed partial class IndustrySystem
    {
        private readonly Dictionary<ManagedBuilding,float> farmOperations=new Dictionary<ManagedBuilding,float>();
        private void SimulateFarms(float seconds, float staffing)
        {
            var housing = 0f;
            foreach (var b in settlement.AllBuildings) housing += b.Economy.capacity;
            float Waste(ManagedBuilding b) => b.State.offline ? 0 : FarmRules.WastePerMonth(b.Id,
                housing > 0 ? settlement.Population * b.Economy.capacity / housing : 0, b.Economy.jobs)
                * staffing * seconds / (float)ColonyCalendar.SecondsPerMonth;
            void StoreWaste(int index, float amount)
            {
                var stored = Mathf.Min(amount, Mathf.Max(0, DeliveryRoom(index, MaterialKind.BasicFertilizer)));
                stocks[index].quantities[(int)MaterialKind.BasicFertilizer] += stored;
                RecordProduction((int)MaterialKind.BasicFertilizer, stored);
            }
            for (var i = 0; i < buildings.Length; i++) StoreWaste(i, Waste(buildings[i]));
            // Founding buildings have no player cargo index: collect their waste into the nearest linked receiver.
            foreach (var core in settlement.CoreBuildings)
            {
                var amount = Waste(core); if (amount <= 0) continue;
                var target = -1; var nearest = float.MaxValue;
                for (var i = 0; i < buildings.Length; i++)
                {
                    if (!connectedToCore[i] || buildings[i].State.offline || !AcceptsInput(i, MaterialKind.BasicFertilizer)
                        || DeliveryRoom(i, MaterialKind.BasicFertilizer) <= 0) continue;
                    var distance = (core.transform.position - buildings[i].transform.position).sqrMagnitude;
                    if (distance < nearest) { target = i; nearest = distance; }
                }
                if (target >= 0) StoreWaste(target, amount);
            }
            farmOperations.Clear();var reserved=UndeliveredFood();
            for(var i=0;i<buildings.Length;i++)
            {
                var farm=buildings[i];if(farm.Id!="Farm")continue;
                var state=farm.State.farm;var root=farm.EntityRoot??farm;var operation=0f;var grade=-1;var food=0f;
                var inputs=FarmInputs(root);var beforeParts=inputs[6];
                var beforeBasic=inputs[9];var beforeIntermediate=inputs[10];var beforeAdvanced=inputs[11];
                if(!farm.State.offline)
                {
                    var component=networkComponent[i];var power=component>=0?networkPowerFraction[component]:0;
                    var room=Mathf.Min(FarmRules.FoodBufferCapacity-stocks[i].food,Mathf.Max(0,FarmRules.FoodCapacity-resources.Food-reserved));
                    var researchYield=research!=null?research.FarmFoodMultiplier:1f;
                    food=FarmRules.Produce(inputs,state,root.State.focus,seconds,staffing,power,room/researchYield,out grade,out operation)*researchYield;
                    stocks[i].food+=food;reserved+=food;
                }
                SpendFarmInput(root,6,beforeParts-inputs[6]);
                SpendFarmInput(root,9,beforeBasic-inputs[9]);
                SpendFarmInput(root,10,beforeIntermediate-inputs[10]);
                SpendFarmInput(root,11,beforeAdvanced-inputs[11]);
                farmOperations[root]=farmOperations.TryGetValue(root,out var previous)?Mathf.Min(previous,operation):operation;
            }
            foreach(var pair in farmOperations)
            {
                var root=pair.Key;if(root.EntityRoot!=root)continue;
                var progress=new FarmState{productiveSeconds=root.State.farm.productiveSeconds};
                var ready=progress.productiveSeconds+seconds>=FarmRules.GrowthSeconds;
                var target=ready && pair.Value>=.9999f && root.FarmExpansionDue(seconds,progress.productiveSeconds<FarmRules.GrowthSeconds)?NextFarmExpansion(root):null;
                var completed=FarmRules.AdvanceGrowth(progress,seconds,pair.Value>=.9999f,!ready||target!=null);
                root.State.farm.productiveSeconds=progress.productiveSeconds;
                if(completed){target.State.farm.addedFields++;target.CommitFarmParcel();target.FarmChanged();}
            }
            resources.ConsumeFood(FarmRules.MonthlyDemand(settlement.Population)*seconds/(float)ColonyCalendar.SecondsPerMonth);
        }
        private string FarmReport(int index)
        {
            var root=buildings[index].EntityRoot??buildings[index];var sites=FarmSites(root).ToArray();
            var totalFields=sites.Sum(s=>FarmRules.Fields(s.State.farm.addedFields));
            var scale=sites.Sum(s=>FarmRules.Scale(s.State.farm.addedFields));
            var staff=WorkersRequired>0?Mathf.Clamp01(WorkersAvailable/WorkersRequired):0;
            var foodRate=0f;var powerMin=1f;var parts=0f;var fertilizer=new float[3];var shared=FarmInputs(root);
            foreach(var site in sites)
            {
                var siteIndex=System.Array.IndexOf(buildings,site);var component=networkComponent[siteIndex];var power=component>=0?networkPowerFraction[component]:0;
                powerMin=Mathf.Min(powerMin,power);var stock=(float[])stocks[siteIndex].quantities.Clone();parts+=stock[6];
                for(var grade=0;grade<3;grade++)fertilizer[grade]+=stock[9+grade];
                if(!site.State.offline)
                {
                    var researchYield=research!=null?research.FarmFoodMultiplier:1f;
                    foodRate+=FarmRules.Produce(shared,site.State.farm,root.State.focus,1,staff,power,
                        Mathf.Min(FarmRules.FoodBufferCapacity-stocks[siteIndex].food,Mathf.Max(0,FarmRules.FoodCapacity-resources.Food-UndeliveredFood()))/researchYield,out _,out _)*150*researchYield;
                }
            }
            return $"{root.DisplayName} / TECH {root.State.farm.technologyLevel} / {sites.Length} sites\n"+
                $"{totalFields} fields / {sites.Sum(s=>FarmRules.Buildings(s.State.farm.addedFields))} support buildings\n"+
                $"Jobs {sites.Sum(s=>s.Economy.jobs):0.#}\n"+
                $"Food {foodRate:0.#}/month / basic potential {FarmRules.BaseFoodPerMonth*scale:0.#}\n"+
                $"Upkeep/month: {scale:0.#} parts + {6*scale:0.#} fertilizer\n"+
                $"Local: parts {parts:0.#} / fertilizer {fertilizer[0]:0.#}, {fertilizer[1]:0.#}, {fertilizer[2]:0.#}\n"+
                $"Staff {staff:P0} / power {powerMin:P0} / {(root.State.offline?"SUSPENDED":"ACTIVE")}\n"+
                $"Food awaiting transport {FarmStoredFood(root):0.#} / colony-bound cargo {FoodInTransit:0.#}\n"+
                $"Growth {root.State.farm.productiveSeconds/150:0.0}/6 months / claimed land protected";
        }
        public bool CanFarmLayout(Transform root, int added, ManagedBuilding owner = null, bool development = false)
        {
            if (planet == null) return false;
            bool Clear(float x, float z, float width, float depth, float height)
            {
                var min = float.MaxValue; var max = float.MinValue;
                for (var a = -1; a <= 1; a++) for (var b = -1; b <= 1; b++)
                {
                    var world = root.TransformPoint(new Vector3(x + a * width * .5f, 0, z + b * depth * .5f));
                    var direction = planet.transform.InverseTransformPoint(world).normalized;
                    var surface = planet.EvaluateSurfacePoint(direction);
                    if (!development && surface.magnitude <= planet.SeaLevel + 1) return false;
                    var ground = root.InverseTransformPoint(planet.transform.TransformPoint(surface)).y;
                    min = Mathf.Min(min, ground); max = Mathf.Max(max, ground);
                    if (!ClearOfMineralVeins(planet.transform.TransformPoint(surface), 2)) return false;
                }
                if (!development && (max - min) * root.lossyScale.y > 4) return false;
                var cell = new Bounds(new Vector3(x, (min + max + height) * .5f, z), new Vector3(width, max - min + height + .08f, depth));
                // Ownership is checked against the actual polygon below; elevation cannot bypass land claims.
                return true;
            }
            // Only validate new pieces during expansion; previously accepted fields stay in place.
            var first = owner == null ? 0 : FarmRules.Fields(owner.State.farm.addedFields);
            var anchor = planet.transform.InverseTransformPoint(root.position).normalized;
            var seed = owner != null ? owner.FarmLayoutSeed : FarmLayoutRules.Seed(planet.Seed, anchor.x, anchor.y, anchor.z);
            for (var field = first; field < FarmRules.Fields(added); field++)
            {
                var parcel = FarmLayoutRules.Parcel(seed, field);
                if(owner!=null && !settlement.PlanFarmParcel(owner,parcel,out parcel))return false;
                if(!settlement.TerritoryClear(root,parcel,owner))return false;
                if(owner!=null)owner.PlannedParcel=parcel;
                var min = new Vector2(float.MaxValue, float.MaxValue); var max = new Vector2(float.MinValue, float.MinValue);
                foreach (var point in parcel) { min = Vector2.Min(min, new Vector2(point.x, point.z)); max = Vector2.Max(max, new Vector2(point.x, point.z)); }
                // Conservative bounds protect the complete irregular parcel, including its corners/headlands.
                if (!Clear((min.x+max.x)*.5f,(min.y+max.y)*.5f,max.x-min.x,max.y-min.y,.04f)) return false;
            }
            for(var barn=0;barn<FarmRules.Buildings(FarmRules.MaxAddedFields);barn++)
            {
                var patchStart=FarmRules.BuildingPatch(barn)*10;
                var newYard=patchStart>=first && patchStart<FarmRules.Fields(added);
                var newBuilding=barn>=(owner==null?0:FarmRules.Buildings(owner.State.farm.addedFields)) && barn<FarmRules.Buildings(added);
                if(!newYard && !newBuilding)continue;
                FarmRules.BuildingPosition(barn,out var x,out var z);
                var yard=WH30K.Sim.Settlements.Settlement.LandRect(new Bounds(new Vector3(x,0,z),new Vector3(.46f,1,.30f)));
                if(!settlement.TerritoryClear(root,yard,owner) || !Clear(x,z,.46f,.30f,.48f))return false;
            }
            return true;
        }
    }
}
