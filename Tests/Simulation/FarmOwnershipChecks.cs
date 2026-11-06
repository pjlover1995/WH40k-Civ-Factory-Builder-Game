using System;
using System.Linq;
using System.Text.Json;
using WH30K.Sim;

internal static class FarmOwnershipChecks
{
    private static FarmPoint[] Box(float x,float z,float width,float depth) => new[]{new FarmPoint(x,z),new FarmPoint(x,z+depth),new FarmPoint(x+width,z+depth),new FarmPoint(x+width,z)};
    public static void Run(Action<bool,string> check)
    {
        var a=Box(0,0,10,10);var edge=Box(10,0,10,10);var overlap=Box(9,0,10,10);
        check(TerritoryRules.Overlaps(a,overlap) && !TerritoryRules.Overlaps(a,edge),"land ownership rejects overlapping ground area while permitting a shared boundary");
        check(TerritoryRules.Touches(a,edge) && !TerritoryRules.Touches(a,Box(11,0,10,10)),"farm merging uses actual boundary contact rather than broad bounding circles");
        var remaining=TerritoryRules.SubtractLargest(a,Box(7,-2,8,14));
        check(Math.Abs(FarmLayoutRules.Area(remaining)-70)<.001f && !TerritoryRules.Overlaps(remaining,Box(7,-2,8,14)),
            "a new farm parcel clips against occupied compatible farm land without duplicate territory");
        check(TerritoryRules.SubtractLargest(a,Box(-1,-1,12,12)).Length==0,"a completely occupied expansion cannot manufacture a second field on the same land");
        check(FacilityIdentityRules.NextName("Farm",new[]{"Farm 1","Farm 2"})=="Farm 3"
            && FacilityIdentityRules.NextName("Farm",new[]{"farm 1"})=="Farm 2","default facility names are unique numbered names with case-insensitive collision checks");
        check(FacilityIdentityRules.ValidName("Harvest Estate") && !FacilityIdentityRules.ValidName(" Bad ")
            && !FacilityIdentityRules.ValidName("Bad\nName") && !FacilityIdentityRules.ValidName(new string('x',41)),"editable facility names have bounded, printable saved values");
        var first=Guid.NewGuid().ToString("N");var second=Guid.NewGuid().ToString("N");
        var types=new[]{"Farm","Farm"};var states=new[]{new BuildingOperationState{instanceId=first,instanceName="Farm 1",farm=new FarmState{entityId=first}},
            new BuildingOperationState{instanceId=second,instanceName="Farm 2",farm=new FarmState{entityId=first}}};
        check(FacilityIdentityRules.ValidCollection(types,states,true),"a combined farm saves separate physical sites under one canonical entity identity");
        states[1].farm.technologyLevel=1;
        check(!FacilityIdentityRules.CompatibleFarms(states[0].farm,states[1].farm) && !FacilityIdentityRules.ValidCollection(types,states,true),"different farm technologies cannot be merged or restored as an invalid common entity");
        states[1].farm.technologyLevel=0;states[1].farm.entityId=Guid.NewGuid().ToString("N");
        check(!FacilityIdentityRules.ValidCollection(types,states,true),"orphaned farm entity references are rejected before replacing a colony");
        states[1].farm.entityId=first;states[0].farm.entityId=second;
        check(!FacilityIdentityRules.ValidCollection(types,states,true),"cyclic farm membership is rejected");
        states[0].farm.entityId=first;states[1].instanceName="FARM 1";
        check(!FacilityIdentityRules.ValidCollection(types,states,true),"duplicate names within one facility type are rejected in saves");
        check(FacilityIdentityRules.ValidCollection(types,new[]{new BuildingOperationState(),new BuildingOperationState()},false),"older saves can migrate missing names and identities");
        var parcel=Box(.3f,-.1f,.1f,.2f);var farm=new FarmState{addedFields=1,layoutSeed=123,claimedParcels=new[]{new FarmParcel{field=5,polygon=parcel}}};
        var options=new JsonSerializerOptions{IncludeFields=true};var loaded=JsonSerializer.Deserialize<FarmState>(JsonSerializer.Serialize(farm,options),options);
        check(FarmRules.Valid(loaded) && FarmRules.Parcel(loaded,123,5).SequenceEqual(parcel),"claimed field polygons survive saves instead of reverting to their pre-contact shape");
        var clone=FarmRules.Clone(farm);clone.claimedParcels[0].polygon[0].x=.9f;
        check(farm.claimedParcels[0].polygon[0].x==.3f,"saved territory snapshots do not alias mutable live polygons");
        loaded.claimedParcels[0].polygon[0].x=float.NaN;
        check(!FarmRules.Valid(loaded),"malformed ownership polygons are rejected");
        var snapshot=new IndustrySnapshot{facilities=new[]{new FacilityStock()},deposits=Array.Empty<DepositStock>(),
            cargo=new[]{new CargoStock{food=true,source=0,target=-2,amount=30,progress=.4f}}};
        check(IndustryRules.Valid(snapshot,1),"colony-bound food cargo has a bounded persisted amount and travel progress");
        snapshot.cargo[0].amount=31;check(!IndustryRules.Valid(snapshot,1),"oversized food deliveries are rejected");
        snapshot.cargo[0].amount=30;snapshot.cargo[0].food=false;check(!IndustryRules.Valid(snapshot,1),"food destinations cannot be used by ordinary material cargo");
        snapshot.expeditionCargo=new float[IndustryRules.MaterialCount];
        snapshot.cargo=new[]{new CargoStock{source=0,target=-3,kind=(int)MaterialKind.Metal,amount=3,progress=.4f}};
        check(IndustryRules.Valid(snapshot,1),"ship-bound physical cargo remains valid while travelling to the shared store");
        snapshot.expeditionCargo=null;
        check(!IndustryRules.Valid(snapshot,1),"ship cargo requires a real saved shared destination");
        snapshot.expeditionCargo=new float[IndustryRules.MaterialCount];snapshot.cargo[0].target=-4;
        check(!IndustryRules.Valid(snapshot,1),"unknown cargo destinations remain invalid");
        snapshot.cargo[0].target=-3;snapshot.cargo[0].food=true;
        check(!IndustryRules.Valid(snapshot,1),"food cannot bypass its dedicated colony delivery destination");
        snapshot.cargo=Array.Empty<CargoStock>();snapshot.facilities[0].food=601;
        check(!IndustryRules.Valid(snapshot,1),"local farm food buffers cannot exceed physical capacity in saved state");
    }
}
