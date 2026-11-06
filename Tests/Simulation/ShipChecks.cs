using System;
using WH30K.Sim;

internal static class ShipChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(ShipRules.Length == 3000 && ShipRules.StartingCrew == 6000 && ShipRules.CrewCapacity == 10000,
            "Explorator is 3 km with 6,000 starting crew and 10,000 physical berths");
        check(ShipRules.StartingFood <= FarmRules.FoodCapacity && ShipRules.EnduranceMonths(ShipRules.StartingFood, ShipRules.CrewCapacity) == 12,
            "finite embarked food fits storage and feeds full ship capacity for one calendar year");
        check(ShipRules.EnduranceMonths(ShipRules.StartingFood, ShipRules.StartingCrew) == 20,
            "starting crew has twenty months of food before growth or resupply");
        var food = ShipRules.StartingFood;
        for (var second = 0; second < ColonyCalendar.SecondsPerYear; second++)
            food -= FarmRules.MonthlyDemand(ShipRules.CrewCapacity) / (float)ColonyCalendar.SecondsPerMonth;
        check(Math.Abs(food) < 2, "continuous full-crew consumption lasts one actual game year");
        check(ShipRules.Occupied(6000, 0) == 6000 && ShipRules.Occupied(20000, 0) == 10000
            && ShipRules.Occupied(10000, 10000) == 5000, "crew occupancy shares colony population and cannot exceed berths");
        var basic = new BuildingEconomy { capacity = ShipRules.CrewCapacity, jobs = 12, outputPerWorker = 1, upkeep = 1 };
        for (var focus = 0; focus < 3; focus++)
            check(BuildingOperationRules.Economy("ColonyShip", basic, new BuildingOperationState { focus = focus, level = 2 }).capacity == ShipRules.CrewCapacity,
                "ship focus/tier cannot create extra berths " + focus);
        check(BuildingOperationRules.Economy("ColonyShip", basic, new BuildingOperationState { offline = true }).capacity == 0,
            "suspending the ship removes its active housing");
        check(ShipRules.ReactorMW > 0 && ShipRules.EnduranceMonths(0, 6000) == 0,
            "onboard generation is positive and empty food reports zero endurance");
    }
}
