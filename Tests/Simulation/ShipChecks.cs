using System;
using WH30K.Sim;

internal static class ShipChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(ShipRules.Length == 500 && ShipRules.Beam == 90 && ShipRules.Height == 120
            && ShipRules.StartingCrew == 1000 && ShipRules.CrewCapacity == 1500,
            "new expeditions use a 500 m tender with 1,000 starting crew and 1,500 physical berths");
        check(ShipRules.LegacyLength == 3000 && ShipRules.LegacyCrewCapacity == 10000
            && ShipRules.LegacyReactorMW == 18,
            "historical 3 km Explorator dimensions, reactor and berths remain defined for old saves");
        check(ShipRules.StartingFood <= FarmRules.FoodCapacity && ShipRules.EnduranceMonths(ShipRules.StartingFood, ShipRules.CrewCapacity) == 12,
            "finite embarked food fits storage and feeds full ship capacity for one calendar year");
        check(ShipRules.EnduranceMonths(ShipRules.StartingFood, ShipRules.StartingCrew) == 18,
            "starting crew has eighteen months of food before growth or resupply");
        var food = ShipRules.StartingFood;
        for (var second = 0; second < ColonyCalendar.SecondsPerYear; second++)
            food -= FarmRules.MonthlyDemand(ShipRules.CrewCapacity) / (float)ColonyCalendar.SecondsPerMonth;
        check(Math.Abs(food) < 2, "continuous full-crew consumption lasts one actual game year");
        check(ShipRules.Occupied(1000, 0) == 1000 && ShipRules.Occupied(3000, 0) == 1500
            && ShipRules.Occupied(1500, 1500) == 750, "crew occupancy shares colony population and cannot exceed tender berths");
        var basic = new BuildingEconomy { capacity = ShipRules.CrewCapacity, jobs = 12, outputPerWorker = 1, upkeep = 1 };
        for (var focus = 0; focus < 3; focus++)
            check(BuildingOperationRules.Economy("ColonyShip", basic, new BuildingOperationState { focus = focus, level = 2 }).capacity == ShipRules.CrewCapacity,
                "ship focus/tier cannot create extra berths " + focus);
        check(BuildingOperationRules.Economy("ColonyShip", basic, new BuildingOperationState { offline = true }).capacity == 0,
            "suspending the ship removes its active housing");
        check(ShipRules.ReactorMW == 3 && ShipRules.EnduranceMonths(0, 1000) == 0,
            "onboard generation is positive and empty food reports zero endurance");
        var logistics = new TenderLogisticsState
        {
            orbitalCrew = TenderLogisticsRules.MainVesselCrew - ShipRules.StartingCrew,
            orbitalFood = TenderLogisticsRules.MainVesselRations - ShipRules.StartingFood,
            orbitalSupplies = new float[IndustryRules.MaterialCount]
        };
        check(TenderLogisticsRules.Valid(logistics) && TenderLogisticsRules.TripsRemaining(logistics) == 4,
            "new orbital logistics start with four valid return runs");
        check(TenderLogisticsRules.NormalizeOptional(new TenderLogisticsState()) == null,
            "empty optional logistics objects from historical JSON normalize to no orbital manifest");
        check(TenderLogisticsRules.NormalizeOptional(new TenderLogisticsState { orbitalSupplies = Array.Empty<float>() }) == null,
            "zero-length optional logistics arrays from historical JSON normalize to no orbital manifest");
        logistics.tripsUsed = TenderLogisticsRules.MaximumTrips;
        check(TenderLogisticsRules.Valid(logistics) && TenderLogisticsRules.TripsRemaining(logistics) == 0,
            "four completed runs exhaust the saved fuel allotment");
        logistics.tripsUsed++;
        check(!TenderLogisticsRules.Valid(logistics), "saved logistics reject a fifth completed run");
        logistics.tripsUsed = 0; logistics.orbitalCrew = float.NaN;
        check(!TenderLogisticsRules.Valid(logistics), "invalid orbital crew stock cannot enter a save");
    }
}
