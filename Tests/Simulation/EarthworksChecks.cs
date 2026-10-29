using System;
using System.Text.Json;
using WH30K.Sim;

internal static class EarthworksChecks
{
    public static void Run(Action<bool,string> check,JsonSerializerOptions options)
    {
        var angle=.00001;
        check(Math.Abs(EarthworksRules.SurfaceDistance(1,0,0,(float)Math.Cos(angle),(float)Math.Sin(angle),0,6000)-.06f)<.00001,
            "spherical movement retains centimetre precision near arrival on a six-kilometre planet");
        check(Math.Abs(EarthworksRules.SurfaceDistance(1,0,0,-1,0,0,6000)-(float)Math.PI*6000)<.01,
            "spherical travel distance remains finite for antipodal destinations");
        var flatten=new TerrainEditStamp{x=1,radius=24,value=6200,mode=(int)EarthworkMode.Flatten};
        check(EarthworksRules.ValidEdit(flatten),"earthworks accepts finite spherical flatten brushes");
        check(EarthworksRules.Apply(6180,flatten,0)==6200 && EarthworksRules.Apply(6210,flatten,12)==6200,
            "flatten produces a constant radial grade throughout its inner area");
        check(EarthworksRules.Apply(6180,flatten,24)==6180 && EarthworksRules.Apply(6180,flatten,100)==6180,
            "earthworks leaves all terrain outside the brush unchanged");
        var feather=EarthworksRules.Apply(6180,flatten,21);
        check(feather>6180 && feather<6200 && Math.Abs(EarthworksRules.Weight(23.999f,24))<.000001,
            "brush feather joins natural terrain continuously without a vertical rim");
        var raise=flatten;raise.mode=(int)EarthworkMode.Raise;raise.value=4;
        var lower=raise;lower.mode=(int)EarthworkMode.Lower;lower.value=-4;
        check(EarthworksRules.Apply(6190,raise,0)==6194 && EarthworksRules.Apply(6210,raise,0)==6214,
            "raising preserves existing relief while increasing grade");
        check(EarthworksRules.Apply(EarthworksRules.Apply(6190,raise,19),lower,19)==6190,
            "equal raise and lower commands cancel before safety clamping");
        check(EarthworksRules.Apply(EarthworksRules.Apply(6190,raise,0),flatten,0)==6200
            && EarthworksRules.Apply(EarthworksRules.Apply(6190,flatten,0),raise,0)==6204,
            "overlapping terrain commands replay in saved chronological order");
        check(EarthworksRules.ClampHeight(6200,6500,5980)==6280 && EarthworksRules.ClampHeight(6200,6000,5980)==6120,
            "repeated work is bounded to eighty metres from the original grade");
        check(EarthworksRules.ClampHeight(5990,5900,5980)==5982 && EarthworksRules.ClampHeight(5900,5900,5980)==5900,
            "lowering cannot breach water while unaffected ocean samples stay unchanged");
        check(EarthworksRules.Valid(null) && EarthworksRules.Valid(new EarthworksSnapshot()),
            "legacy saves and intentionally absent vehicles remain valid");
        var state=new EarthworksSnapshot{hasVehicle=true,x=1,forwardZ=1,travelling=true,targetX=1,
            command=(int)EarthworkMode.Flatten,radius=24,value=6200,work=2,edits=new[]{raise,flatten,lower}};
        var json=JsonSerializer.Serialize(state,options);
        var roundTrip=JsonSerializer.Deserialize<EarthworksSnapshot>(json,options);
        check(EarthworksRules.Valid(roundTrip) && roundTrip.work==2 && roundTrip.edits.Length==3
            && roundTrip.edits[1].value==6200 && roundTrip.travelling,
            "vehicle position, facing, pending command, work time and ordered edits survive JSON");
        state.work=float.NaN;check(!EarthworksRules.Valid(state),"nonfinite pending work is rejected before session replacement");state.work=2;
        state.forwardX=1;check(!EarthworksRules.Valid(state),"vehicle facing must be unit length and tangent to the planet");state.forwardX=0;
        var brokenHeading=new EarthworksSnapshot{hasVehicle=true,x=1,travelling=true,targetX=1,targetY=.000003f,command=0,work=2};
        check(!EarthworksRules.Valid(brokenHeading) && EarthworksRules.RecoverZeroHeading(brokenHeading) && EarthworksRules.Valid(brokenHeading)
            && brokenHeading.forwardY>.999f && brokenHeading.work==2 && brokenHeading.travelling,
            "known zero-heading saves recover a tangent toward the destination without losing command work");
        var parkedHeading=new EarthworksSnapshot{hasVehicle=true,y=1};
        check(EarthworksRules.RecoverZeroHeading(parkedHeading) && EarthworksRules.Valid(parkedHeading),
            "a stopped dozer with the historical zero heading receives a stable polar-safe tangent");
        parkedHeading.forwardX=5;
        check(!EarthworksRules.RecoverZeroHeading(parkedHeading) && !EarthworksRules.Valid(parkedHeading),
            "heading recovery does not accept nonzero corrupt vehicle directions");
        state.targetX=0;check(!EarthworksRules.Valid(state),"invalid saved movement destinations are rejected");state.targetX=1;
        state.command=(int)EarthworkMode.Demolish;check(!EarthworksRules.Valid(state),"demolition cannot be replayed as a saved vehicle command");state.command=1;
        state.edits=new TerrainEditStamp[EarthworksRules.MaxEdits+1];check(!EarthworksRules.Valid(state),"unbounded terrain edit histories are rejected");
        var invalid=flatten;invalid.value=float.PositiveInfinity;check(!EarthworksRules.ValidEdit(invalid),"infinite flatten grades are rejected");
        invalid=raise;invalid.value=-4;check(!EarthworksRules.ValidEdit(invalid),"raise cannot smuggle a lowering operation");
        invalid=lower;invalid.radius=0;check(!EarthworksRules.ValidEdit(invalid),"zero and oversized brush radii are rejected");
        invalid=flatten;invalid.radius=65;check(!EarthworksRules.ValidEdit(invalid),"saved brush radii respect gameplay limits");
    }
}
