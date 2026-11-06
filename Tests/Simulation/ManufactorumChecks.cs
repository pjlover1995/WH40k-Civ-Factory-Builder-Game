using System;
using System.Text.Json;
using WH30K.Sim;

internal static class ManufactorumChecks
{
    public static void Run(Action<bool,string> check)
    {
        var state = new ManufactorumState();
        check(ManufactorumRules.NormalizeLoaded(new ManufactorumState { version=0, inheritedCapacity=0 })==null, "Unity zero-filled optional industrial classes normalize to historical missing data");
        var emptyProject=new ManufactorumState { project=new IndustrialBay() };
        check(ManufactorumRules.Valid(emptyProject) && emptyProject.project==null, "Unity zero-filled null industrial projects normalize without inventing a reservation");
        check(ManufactorumRules.Valid(null) && ManufactorumRules.Valid(state), "old missing industrial data and empty founding complexes remain valid");
        for(var i=0;i<100;i++)
        {
            state.project=ManufactorumRules.Candidate(state,b=>true);
            ManufactorumRules.Advance(state,60,false,true);
        }
        check(state.bays.Count==100 && ManufactorumRules.Scale(state)==101 && ManufactorumRules.Valid(state), "one hundred connected warehouse expansions exceed historical residential and industrial caps");
        check(ManufactorumRules.Candidate(state,b=>false)==null, "no industrial expansion can choose blocked land");
        var site=ManufactorumRules.Candidate(state,b=>b.x<0);
        check(site!=null && site.x<0 && !ManufactorumRules.Occupied(state,site), "industrial frontier search finds alternative connected land without duplicate sites");
        state.project=site;
        check(!ManufactorumRules.Advance(state,20,true,true) && state.work==0, "suspended industrial construction retains reserved site without progress");
        check(!ManufactorumRules.Advance(state,20,false,false) && state.work==0, "obstructed industrial projects retain paid work and never complete over buildings");
        ManufactorumRules.Advance(state,21,false,true);
        var json=JsonSerializer.Serialize(state,new JsonSerializerOptions{IncludeFields=true});
        state=JsonSerializer.Deserialize<ManufactorumState>(json,new JsonSerializerOptions{IncludeFields=true});
        check(state.work==21 && state.project.x==site.x && state.bays.Count==100 && ManufactorumRules.Valid(state), "industrial save round trip preserves partial paid project and completed geometry");
        check(!ManufactorumRules.Advance(state,float.NaN,false,true) && state.work==21, "invalid simulation time cannot damage industrial project saves");
        check(ManufactorumRules.Advance(state,100,false,true) && state.bays.Count==101 && state.project==null && state.work==0, "warehouse capacity becomes available exactly once on completion");
        var operation=new BuildingOperationState{manufactorum=new ManufactorumState()};
        operation.manufactorum.bays.Add(new IndustrialBay{x=1});
        var basic=new BuildingEconomy{jobs=35,upkeep=18};
        var expanded=BuildingOperationRules.Economy("Manufactorum",basic,operation);
        check(expanded.jobs==70 && expanded.upkeep==36 && ManufactorumRules.ProductionScale(operation)==2, "warehouse expansion doubles actual staff demand, upkeep and base production capacity");
        operation.level=2;
        check(ManufactorumRules.ProductionScale(operation)==2 && BuildingOperationRules.Economy("Manufactorum",basic,operation).jobs==70, "technology tiers do not add industrial capacity or staff demand");
        check(ManufactorumRules.RequiredTier(0)==1 && ManufactorumRules.RequiredTier(1)==1 && ManufactorumRules.RequiredTier(2)==2 && ManufactorumRules.RequiredTier(3)==2 && ManufactorumRules.RequiredTier(5)==3, "physical technology tiers unlock advanced manufacturing recipes independently of warehouse count");
        check(ManufactorumRules.Upkeep(operation.manufactorum,(float)ColonyCalendar.SecondsPerMonth,1)==1 && ManufactorumRules.Upkeep(operation.manufactorum,0,1)==0 && ManufactorumRules.Upkeep(operation.manufactorum,10,0)==0, "each added warehouse costs one physical component per fully operated month with no paused or unpowered charge");
        operation.offline=true;
        check(BuildingOperationRules.Economy("Manufactorum",basic,operation).jobs==0, "suspending a warehouse complex releases expanded workforce demand");
        state.bays.Add(state.bays[0]); check(!ManufactorumRules.Valid(state), "industrial saves reject overlapping duplicate warehouses");
        state=new ManufactorumState();state.bays.Add(new IndustrialBay{x=2});check(!ManufactorumRules.Valid(state), "industrial saves reject disconnected warehouse placements");
        state=new ManufactorumState{project=new IndustrialBay(),work=2};check(!ManufactorumRules.Valid(state), "industrial saves reject reserved warehouse overlap with the founding hall");
        state=new ManufactorumState{work=2};check(!ManufactorumRules.Valid(state), "industrial saves reject orphaned paid construction progress");
    }
}
