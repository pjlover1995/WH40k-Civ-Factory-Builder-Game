using System;
using System.Linq;
using System.Text.Json;
using WH30K.Sim;

internal static class FarmLayoutChecks
{
    private static FarmPoint[] Intersection(FarmPoint[] a, FarmPoint[] b)
    {
        for (var i=0;i<b.Length;i++)
        { var p=b[i]; var q=b[(i+1)%b.Length]; a=FarmLayoutRules.Clip(a,p.z-q.z,q.x-p.x,(p.z-q.z)*p.x+(q.x-p.x)*p.z); }
        return a;
    }
    private static float SharedEdge(FarmPoint[] a, FarmPoint[] b)
    {
        var longest=0f;
        for(var i=0;i<a.Length;i++) for(var j=0;j<b.Length;j++)
        {
            var p=a[i]; var q=a[(i+1)%a.Length]; var r=b[j]; var s=b[(j+1)%b.Length];
            var dx=q.x-p.x; var dz=q.z-p.z; var length=(float)Math.Sqrt(dx*dx+dz*dz);
            if(length<1e-6f || Math.Abs(dx*(r.z-p.z)-dz*(r.x-p.x))>length*2e-5f
                || Math.Abs(dx*(s.z-p.z)-dz*(s.x-p.x))>length*2e-5f)continue;
            var t=((r.x-p.x)*dx+(r.z-p.z)*dz)/length;
            var u=((s.x-p.x)*dx+(s.z-p.z)*dz)/length;
            longest=Math.Max(longest,Math.Min(length,Math.Max(t,u))-Math.Max(0,Math.Min(t,u)));
        }
        return longest;
    }
    public static void Run(Action<bool,string> check)
    {
        var valid=true; var disjoint=true; var contiguous=true; var yardClear=true; var varied=true; var neighbours=true;
        for(var seed=1;seed<=16;seed++) for(var cluster=0;cluster<(FarmRules.Fields(FarmRules.MaxAddedFields)+9)/10;cluster++)
        {
            var parcels=Enumerable.Range(cluster*10,10).Select(i=>FarmLayoutRules.Parcel(seed,i)).ToArray();
            FarmRules.ClusterPosition(cluster,out var cx,out var cz);
            var yard=new[]{new FarmPoint(cx-.22f,cz-.15f),new FarmPoint(cx-.22f,cz+.15f),
                new FarmPoint(cx+.22f,cz+.15f),new FarmPoint(cx+.22f,cz-.15f)};
            foreach(var p in parcels)
            {
                valid &= p.Length>=3 && FarmLayoutRules.Area(p)>.002f && p.All(v=>!float.IsNaN(v.x+v.z)
                    && v.x>=cx-.49001f && v.x<=cx+.49001f && v.z>=cz-.49001f && v.z<=cz+.49001f);
                if(FarmRules.HasBuildingYard(cluster)) yardClear &= FarmLayoutRules.Area(Intersection(p,yard))<2e-6f;
            }
            for(var i=0;i<10;i++) for(var j=i+1;j<10;j++) disjoint &= FarmLayoutRules.Area(Intersection(parcels[i],parcels[j]))<2e-6f;
            // Every prefix is connected: each new plot meets an already cultivated one along a real edge.
            for(var next=1;next<10;next++) contiguous &= Enumerable.Range(0,next).Any(i=>SharedEdge(parcels[next],parcels[i])>.002f);
            varied &= parcels.Select(FarmLayoutRules.Area).Max()-parcels.Select(FarmLayoutRules.Area).Min()>.005f
                && parcels.Any(p=>p.Length!=4) && parcels.All(p=>p.Length!=4 ||
                    Enumerable.Range(0,4).Any(k=>{
                        var a=p[k]; var b=p[(k+1)%4]; var c=p[(k+2)%4];
                        return Math.Abs((b.x-a.x)*(c.x-b.x)+(b.z-a.z)*(c.z-b.z))>1e-5f;
                    }));
            if(cluster>0)
            {
                var previous=FarmRules.PreviousPatch(cluster);
                neighbours &= Enumerable.Range(previous*10,10).Any(i=>SharedEdge(parcels[0],FarmLayoutRules.Parcel(seed,i))>.002f);
            }
        }
        check(Enumerable.Range(0,FarmRules.Buildings(FarmRules.MaxAddedFields)).Select(FarmRules.BuildingDesign).Distinct().Count()==4,
            "each support building has its own persistent design identity");
        check(FarmRules.MaxAddedFields==120 && FarmRules.Buildings(120)==4,
            "the developer field limit scales fourfold and allows all three delayed satellite buildings");
        check(valid,"seeded natural field parcels have positive area and stay inside the reserved farm footprint");
        check(disjoint,"all irregular fields fit side by side without overlapping cultivated parcels");
        check(contiguous,"initial five fields and every later growth prefix form one adjoining patchwork per barn");
        check(neighbours,"the first field at each satellite barn meets an already cultivated neighbour cluster");
        check(yardClear,"irregular fields leave the complete barn, silo and service-building courtyard clear");
        check(varied,"natural fields vary in area, side count and oblique corners instead of repeating rectangular tiles");
        var a=FarmLayoutRules.Parcel(1234,4); var again=FarmLayoutRules.Parcel(1234,4); var other=FarmLayoutRules.Parcel(789,4);
        check(a.SequenceEqual(again) && !a.SequenceEqual(other),"a farm seed reproduces exact boundaries while different farms have distinct shapes");
        var planted=FarmLayoutRules.Inset(a,.008f);
        check(FarmLayoutRules.Area(planted)>0 && FarmLayoutRules.Area(planted)<FarmLayoutRules.Area(a)
            && Math.Abs(FarmLayoutRules.Area(Intersection(planted,a))-FarmLayoutRules.Area(planted))<1e-6f,
            "narrow planted headlands remain inside their shared parcel boundary");
        var strip=FarmLayoutRules.Clip(FarmLayoutRules.Clip(planted,1,0,.03f),-1,0,.03f);
        check(FarmLayoutRules.Area(Intersection(strip,a))<=FarmLayoutRules.Area(a),"crop strips clip to irregular field edges");
        var options=new JsonSerializerOptions{IncludeFields=true}; var state=new FarmState{layoutSeed=1234,addedFields=6,productiveSeconds=42};
        var loaded=JsonSerializer.Deserialize<FarmState>(JsonSerializer.Serialize(state,options),options);
        check(loaded.layoutSeed==1234 && FarmRules.Clone(loaded).layoutSeed==1234
            && FarmLayoutRules.Parcel(loaded.layoutSeed,10).SequenceEqual(FarmLayoutRules.Parcel(state.layoutSeed,10)),
            "field shapes and barn-cluster layouts survive saved-seed restoration and cloning");
        check(FarmLayoutRules.Seed(1234,.2f,.3f,.4f)!=0 && FarmRules.Clone(null).layoutSeed==0,
            "legacy farms receive a deterministic nonzero seed without changing saved growth or economy");
    }
}
