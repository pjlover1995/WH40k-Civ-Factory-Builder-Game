using System;
using System.Text.Json;
using WH30K.Sim;

internal static class FoundingChecks
{
    public static void Run(Action<bool,string> check)
    {
        var p=new FoundingProgress();
        check(FoundingObjectives.Valid(p) && FoundingObjectives.Stage(p)==0 && !p.enabled,"old saves get optional empty founding progress and no reward");
        FoundingObjectives.Observe(ref p,0,10,1,10,1,10,1,true);
        check(FoundingObjectives.Stage(p)==0,"paused objectives cannot advance");
        FoundingObjectives.Observe(ref p,1,0,1,0,1,0,0,true);
        check(FoundingObjectives.Stage(p)==0,"stored starting supplies and placed buildings do not complete objectives");
        FoundingObjectives.Observe(ref p,1,2,1,0,1,0,0,true);
        check(p.waterEstablished && FoundingObjectives.Stage(p)==1,"working water production completes water milestone");
        FoundingObjectives.Observe(ref p,1,0,1,2,1,0,0,true);
        check(p.foodDelivered && FoundingObjectives.Stage(p)==2,"actual delivered farm food completes food milestone");
        FoundingObjectives.Observe(ref p,1,0,1,0,1,1,0,true);
        check(p.componentsMade && p.seconds==0,"manufacturing qualifies but does not backdate balance observation");
        var established=p;
        for(var i=0;i<75;i++)FoundingObjectives.Observe(ref p,1,2,1,2,1,1,.5f,true);
        p.enabled=true; p.pendingComponentUse=3;
        var json=JsonSerializer.Serialize(p,new JsonSerializerOptions{IncludeFields=true});
        var restored=JsonSerializer.Deserialize<FoundingProgress>(json,new JsonSerializerOptions{IncludeFields=true});
        check(restored.seconds==75 && restored.enabled && restored.pendingComponentUse==3 && FoundingObjectives.Valid(restored),"partial objective window and tracker preference serialize exactly");
        p=restored;
        for(var i=0;i<75;i++)FoundingObjectives.Observe(ref p,1,2,1,2,1,1,.5f,true);
        check(p.completed && FoundingObjectives.Stage(p)==4 && p.rewardRecovered==0,"full balanced simulation month earns unclaimed canister");
        FoundingObjectives.Observe(ref p,1,0,100,0,100,0,100,false);
        check(p.completed,"achieved milestone remains recognised after later shortages");
        p=established;
        for(var i=0;i<150;i++)FoundingObjectives.Observe(ref p,1,2,1,0,1,1,0,true);
        check(!p.completed && p.seconds==0,"large food reserves cannot substitute for actual food deliveries");
        p=established;
        for(var i=0;i<150;i++)FoundingObjectives.Observe(ref p,1,0,1,2,1,1,0,true);
        check(!p.completed,"stored water cannot substitute for pumping demand");
        p=established;
        for(var i=0;i<150;i++)FoundingObjectives.Observe(ref p,1,2,1,2,1,1,2,true);
        check(!p.completed && p.seconds==0,"component spending must be replaced by manufacturing");
        p=established;
        FoundingObjectives.Observe(ref p,40,80,40,80,40,40,0,true);
        FoundingObjectives.Observe(ref p,1,2,1,2,1,1,0,false);
        check(p.seconds==0 && !p.completed && p.componentsMade,"empty essential reserve resets window without deleting milestones");
        p=established;p.seconds=float.NaN;
        check(!FoundingObjectives.Valid(p),"invalid saved objective counters reject");
        p=established;p.rewardRecovered=1;
        check(!FoundingObjectives.Valid(p),"canister cannot be claimed before achievement");
        p=established;p.completed=true;p.rewardRecovered=FoundingObjectives.Reward+1;
        check(!FoundingObjectives.Valid(p),"overclaimed canister saves reject");
    }
}
