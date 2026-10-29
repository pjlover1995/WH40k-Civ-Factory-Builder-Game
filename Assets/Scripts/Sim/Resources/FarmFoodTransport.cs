using System;
using UnityEngine;
using WH30K.Gameplay.Construction;

namespace WH30K.Sim.Resources
{
    public sealed partial class IndustrySystem
    {
        private ManagedBuilding ColonyFoodDestination()
        {
            foreach(var core in settlement.CoreBuildings)if(core.Id=="ColonyShip")return core;
            foreach(var core in settlement.CoreBuildings)if(core.Id=="Factory")return core;
            return settlement.CoreBuildings.Count>0?settlement.CoreBuildings[0]:null;
        }
        public float FoodInTransit { get { var sum=0f;foreach(var flight in flights)if(flight.state.food)sum+=flight.state.amount;return sum; } }
        public int FoodTransportCount { get { var sum=0;foreach(var flight in flights)if(flight.state.food)sum++;return sum; } }
        private float UndeliveredFood()
        {var sum=FoodInTransit;foreach(var stock in stocks)sum+=stock.food;return sum;}
        private void DispatchFarmFood()
        {
            if(ColonyFoodDestination()==null)return;
            for(var i=0;i<buildings.Length && flights.Count<64;i++)
            {
                if(buildings[i].Id!="Farm" || buildings[i].State.offline || !connectedToCore[i])continue;
                while(stocks[i].food>=.1f && flights.Count<64)
                {
                    var amount=Mathf.Min(FarmRules.FoodCargoCapacity,stocks[i].food);stocks[i].food-=amount;
                    AddFlight(new CargoStock{food=true,source=i,target=-2,kind=0,amount=amount});
                }
            }
        }
        public bool HasFarmCapacity(ManagedBuilding root)
        {foreach(var site in FarmSites(root))if(site.State.farm.addedFields<FarmRules.MaxAddedFields)return true;return false;}
    }
}
