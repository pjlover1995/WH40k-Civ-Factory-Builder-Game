using System;
using WH30K.Sim;
using WH30K.Sim.Environment;

internal static class OutputChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var buildings = new[] { new BuildingEconomy { capacity = 10000, jobs = 100, outputPerWorker = 2, upkeep = 20 } };
        ColonyTick Tick(float food, float water, float health=1, float people=6000, float seconds=150, float habitat=1)
            => ColonyRules.CalculateSupportedTick(buildings, people, .62f, 1, 1, habitat, food, water, health, seconds);
        var healthy = Tick(120000,600);
        check(healthy.population>6000 && healthy.population<6200, "growth is gradual over one month rather than racing to all ship berths");
        check(Tick(6000,600).population<healthy.population && Tick(120000,30).population<healthy.population,
            "low food and low water independently slow growth before supplies are exhausted");
        check(Tick(0,600).population<6000 && Tick(120000,0).population<6000,
            "empty food or water causes population decline even when housing remains available");
        check(Tick(120000,600,.7f).population<healthy.population && Tick(120000,600,1,6000,150,.7f).population<healthy.population,
            "population health and environmental habitability independently constrain growth");
        check(Tick(120000,600,1,10000).population==10000, "supplies never allow growth beyond active housing");
        check(Tick(120000,600,1,6000,0).population==6000, "paused and read-only reports do not grow population");
        check(Tick(0,0,0,0).population==0, "an empty colony cannot create colonists from no supplies or health");
        var split=6000f;
        for(var i=0;i<150;i++)split=Tick(120000,600,1,split,1).population;
        check(Math.Abs(split-healthy.population)<.05f, "growth with sufficient reserves is stable across tick subdivision");
        check(ColonyRules.MonthsOfSupply(120000,6000)==20 && ColonyRules.MonthsOfSupply(0,6000)==0,
            "food reserve months use current population demand and preserve an exhausted reserve");
        check(float.IsPositiveInfinity(ColonyRules.MonthsOfSupply(20,0)) && ColonyRules.SupplyLabel(20,0)=="NO DEMAND",
            "unused construction supplies do not display an invented depletion date");
        check(WaterRules.GroundwaterPerSecond>WaterRules.Demand(10000), "one full groundwater pump supports the ship maximum occupancy");
        check(FarmRules.BaseFoodPerMonth>=FarmRules.MonthlyDemand(ShipRules.StartingCrew), "one fully supplied founding farm supports the starting crew");
        var environment=new EnvironmentSnapshot {Co2=600,O2=19.5f,WaterPollution=10};
        var description=ColonyOutputExplanation.Describe(healthy,environment,600,120000);
        check(description.Contains("3 months food") && description.Contains("Health") && description.Contains("NO DEMAND")
            && !description.Contains("credit"), "hover explains reserve arithmetic, growth conditions and physical supplies without currency");
        description=ColonyOutputExplanation.Describe(Tick(0,0),environment,0,0);
        check(description.Contains("Water reserve empty") && description.Contains("Food reserve empty") && description.Contains("Population is falling"),
            "empty reserve diagnostics state both shortages and their population effect");
        check(ColonyRules.LegacyProjectSupplyScale==.01f, "saved project work and paid fractions retain their units with explicit 100-to-1 physical supply pricing");
        CheckReserveOutlook(check);
    }

    private static void CheckReserveOutlook(Action<bool, string> check)
    {
        var outlook = new ReserveOutlook();
        outlook.Record(149.5f, 500, 299);
        check(!outlook.Ready && outlook.Describe(true, 1800, 1200).Contains("Measuring inflow (149 / 150"),
            "an incomplete month displays gross coverage and collection progress without a forecast");
        outlook.Record(.5f, 100, 1);
        var food = outlook.Describe(true, 1800, 1200);
        check(outlook.Ready && food.Contains("Reserve coverage 1.5 mo")
            && food.Contains("Farm food delivered 600") && food.Contains("Net change -600.0")
            && food.Contains("Estimated time to empty 3.0 mo"),
            "gross reserve coverage and net depletion use distinct demand and deficit rates");
        check(outlook.Describe(false, 100, 300).Contains("roughly in balance")
            && outlook.Describe(false, 100, 250).Contains("exceeds demand")
            && !outlook.Describe(false, 100, 250).Contains("time to empty"),
            "balanced and surplus reserves show their status without a depletion estimate");
        check(outlook.Describe(true, 0, 1200).Contains("Reserve is empty."),
            "an exhausted reserve reports its current state");
        outlook.Record(0, 200, 200);
        check(outlook.Describe(true, 1800, 1200) == food, "a paused tick and repeated reads do not change the outlook");
        outlook.Record(.5f, 0, 0);
        check(outlook.WaterPerMonth == 299 && outlook.FoodPerMonth == 600,
            "water is prorated at the window boundary while discrete food deliveries remain whole");
        outlook.Record(149.5f, 0, 0);
        check(outlook.WaterPerMonth == 0 && outlook.FoodPerMonth == 0,
            "stopped production and deliveries expire from the completed window");
        outlook.Reset();
        check(!outlook.Ready && outlook.FoodPerMonth == 0 && outlook.WaterPerMonth == 0,
            "a newly configured colony has no inherited outlook observations");
    }
}
