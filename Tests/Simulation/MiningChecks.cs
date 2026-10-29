using System;
using System.Linq;
using System.Text.Json;
using WH30K.Sim;

internal static class MiningChecks
{
    public static void Run(Action<bool,string> check)
    {
        var options=new JsonSerializerOptions{IncludeFields=true};
        foreach(var kind in new[]{MaterialKind.IronOre,MaterialKind.CopperOre,MaterialKind.Crystal,MaterialKind.Stone})
        {
            var bodies=Enumerable.Range(0,80).Select(id=>MiningRules.Create(8123,id,kind)).ToArray();
            check(bodies.All(MiningRules.Valid) && bodies.All(s=>s.layoutVersion==1),kind+" creates valid unrestricted solid veins");
            var ratios=bodies.Select(s=>s.majorRadius*s.minorRadius*s.depth/MiningRules.Capacity(s)).ToArray();
            check(ratios.All(r=>Math.Abs(r-100)<.001f),kind+" surveyed vein volume is proportional to its finite reserve");
            var surface=Enumerable.Range(0,80).Select(id=>ExpeditionRules.SurfaceVein(8123,id,kind)).ToArray();
            check(surface.All(MiningRules.Valid) && surface.Select(s=>s.majorRadius).Distinct().Count()>10
                && surface.All(s=>Math.Abs(s.majorRadius*s.minorRadius*s.depth/MiningRules.Capacity(s)-100)<.001),
                kind+" small surface deposits retain reserve-scaled volumes and distinct physical sizes");
        }
        var state=MiningRules.Create(8123,12,MaterialKind.IronOre);var capacity=MiningRules.Capacity(state);
        check(MiningRules.PitScale(state)==0 && MiningRules.Radius(state,0)==0 && MiningRules.SolidCutDepth(state,0,0)==0,
            "an untouched vein has no pre-dug pit");
        var samples=new float[25*25];var monotonic=true;var withinVein=true;var ramps=true;
        for(var step=1;step<=20;step++)
        {
            MiningRules.ExtractSolid(state,capacity/20);
            for(var y=0;y<25;y++)for(var x=0;x<25;x++)
            {
                var px=(x/12f-1)*state.majorRadius;var py=(y/12f-1)*state.minorRadius;
                var drop=MiningRules.SolidCutDepth(state,px,py);var index=x+y*25;
                monotonic &= drop+.0001f>=samples[index];samples[index]=drop;
                if(px*px/(state.majorRadius*state.majorRadius)+py*py/(state.minorRadius*state.minorRadius)>=1)withinVein &= drop==0;
            }
            var previous=-1f;
            for(var i=0;i<=64;i++)
            {
                MiningRules.HaulPoint(state,i/64f,out var x,out var y,out var expected);
                var drop=MiningRules.SolidCutDepth(state,x,y);
                ramps &= Math.Abs(drop-expected)<.015 && drop+.0001f>=previous;previous=drop;
            }
        }
        check(monotonic,"all sampled pit surfaces only descend during growth; moving ramps never refill older cuts");
        check(withinVein,"excavation never extends beyond the surveyed mineral ellipse");
        check(ramps,"the haul ramp meets the surface, descends continuously and reaches the working floor at every growth stage");
        check(Math.Abs(MiningRules.Remaining(state))<.01 && Math.Abs(MiningRules.PitScale(state)-1)<.0001
            && MiningRules.Radius(state,0)<=state.majorRadius+.001 && MiningRules.Depth(state,0)<=state.depth,
            "depletion reaches the complete vein without an artificial directional cap or growth beyond geology");
        check(MiningRules.ExtractSolid(state,10)<.01 && MiningRules.SolidCutDepth(state,0,0)>0,
            "an exhausted vein produces no further ore and retains its excavated floor");
        state=MiningRules.Create(8123,12,MaterialKind.Crystal);capacity=MiningRules.Capacity(state);
        check(MiningRules.ExtractSolid(state,float.NaN)==0 && MiningRules.ExtractSolid(state,-1)==0 && MiningRules.Remaining(state)==capacity,
            "invalid solid extraction cannot corrupt reserves");
        MiningRules.ExtractSolid(state,capacity*.37f);state.accessAngle=234;
        var saved=JsonSerializer.Serialize(state,options);var loaded=JsonSerializer.Deserialize<ExcavationState>(saved,options);
        check(MiningRules.Valid(loaded) && JsonSerializer.Serialize(loaded,options)==saved
            && MiningRules.SolidCutDepth(loaded,10,4)==MiningRules.SolidCutDepth(state,10,4),
            "save reload retains exact solid reserves, excavation geometry and haul-ramp alignment");
        var clear=true;
        for(var bearing=0; bearing<360; bearing+=5)
        {
            var extent=MiningRules.VeinRadius(state,bearing);
            clear &= !MiningRules.OutsideVein(state,extent,bearing,14)
                && MiningRules.OutsideVein(state,extent*(1+27/Math.Min(state.majorRadius,state.minorRadius)),bearing,14);
        }
        check(clear,"rim placement protects the complete footprint and oblique sides of narrow veins");
        loaded.layoutVersion=4;check(!MiningRules.Valid(loaded),"unknown saved excavation versions are rejected");
        loaded.layoutVersion=1;loaded.accessAngle=float.NaN;check(!MiningRules.Valid(loaded),"nonfinite saved haul-ramp alignment is rejected");
        loaded.accessAngle=0;loaded.blocked=1;check(!MiningRules.Valid(loaded),"solid pits cannot accidentally acquire inaccessible directional reserves");
        var legacy=MiningRules.Create(42,0,MaterialKind.IronOre,1456);
        MiningRules.Limit(legacy,3);var legacyJson=JsonSerializer.Serialize(legacy,options);
        check(legacy.layoutVersion==0 && MiningRules.Valid(legacy) && Math.Abs(MiningRules.Remaining(legacy)-1456)<.001
            && JsonSerializer.Deserialize<ExcavationState>(legacyJson,options).blocked==8,
            "legacy saves retain their reserves, geometry and existing protection choices without relocation or refill");
        var gradual=MiningRules.Create(99,4,MaterialKind.Stone);var reserve=MiningRules.Remaining(gradual);var produced=0f;
        for(var i=0;i<20000 && MiningRules.Remaining(gradual)>.0001f;i++)produced+=MiningRules.ExtractSolid(gradual,1);
        check(Math.Abs(produced+MiningRules.Remaining(gradual)-reserve)<.02f,
            "thousands of small mining cycles conserve finite ore through full depletion");
    }
}
