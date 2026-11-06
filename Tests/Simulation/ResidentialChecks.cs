using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WH30K.Sim;

internal static class ResidentialChecks
{
    public static void Run(Action<bool,string> check,string root,JsonSerializerOptions json)
    {
        var lib=JsonSerializer.Deserialize<ResidentialLibrary>(File.ReadAllText(Path.Combine(root,"Assets/Resources/ResidentialModules.json")),json);
        check(lib.Valid(),"residential configuration has bounded costs, class shares, geometry and development limits");
        check(lib.designs.Length==60 && Enumerable.Range(0,3).All(c=>lib.designs.Count(d=>d.residentClass==c)==20),"residential library has exactly twenty habitation designs per class");
        check(lib.designs.Select(d=>d.id).Distinct().Count()==60 && lib.designs.All(d=>d.capacity>0 && d.cost>0 && d.work>0 && d.upper && d.lift && d.columns.Length>0),"all sixty stable IDs carry costs, access, support and housing metadata");
        string Signature(HabDesign d)
        {
            var rotations=new List<string>();
            for(var r=0;r<4;r++)
            {
                var columns=d.columns.Select(c=>{ResidentialRules.Rotate(c.x,c.z,r,out var x,out var z);return new HabColumn{x=x,z=z,height=c.height};}).ToArray();
                var minx=columns.Min(c=>c.x);var minz=columns.Min(c=>c.z);
                rotations.Add(string.Join(";",columns.Select(c=>$"{c.x-minx},{c.z-minz},{c.height}").OrderBy(s=>s,StringComparer.Ordinal)));
            }
            return rotations.OrderBy(s=>s,StringComparer.Ordinal).First();
        }
        check(lib.designs.Select(Signature).Distinct().Count()==60,"sixty occupied-volume designs are unique even after rotation and translation");
        check(lib.designs.All(d=>d.columns.Select(c=>(c.x,c.z)).Distinct().Count()==d.columns.Length && d.columns.All(c=>c.height>0 && c.height<=4)),"module footprints contain exact, nonduplicated supported columns");
        check(lib.designs.All(d=>Connected(d.columns)),"every habitation footprint has connected usable floor space");
        var partition=ResidentialRules.Split(1000,lib);
        check(Math.Abs(partition.Sum()-1000)<.001 && partition[1]==180 && partition[2]==40,"resident classes partition total population without extra residents or workers");
        var demand=ResidentialRules.Demand(1000,620,1,new[]{700f,170,20},lib);
        check(demand.All(d=>d>0) && demand[0]>demand[1] && ResidentialRules.Demand(100,62,1,new[]{1000f,1000,1000},lib).Sum()==0,"demand uses class shortfall, population headroom and real employment");
        var input=new ResidentialInputs{demand=new[]{100f,100,100},credits=10000,staffing=1,production=1,services=1,habitability=1,co2=410,water=4};
        var s=ResidentialRules.Create(1234,"test");
        var before=JsonSerializer.Serialize(s,json);
        var candidates=ResidentialRules.Candidates(s,lib,input,null);
        check(candidates.Count>0 && candidates.Count<=5 && candidates.All(c=>ResidentialRules.Validate(s,c.placement,lib,null,out _)),"every displayed residential preview passes construction validation");
        check(before==JsonSerializer.Serialize(s,json),"candidate evaluation does not consume generation state or modify layout");
        check(ResidentialRules.Key(candidates[0].placement)==ResidentialRules.Key(ResidentialRules.Candidates(ResidentialRules.Create(1234,"test"),lib,input,null)[0].placement),"same seed and geometry select the same preferred addition");
        var noGround=ResidentialRules.Candidates(s,lib,input,c=>c.bottom>0);
        check(noGround.Count>0 && noGround[0].placement.floor>0,"blocked outward footprints choose an accessible supported upper addition");
        check(ResidentialRules.Candidates(s,lib,input,c=>false).Count==0,"no valid extension reaches a safe temporary development limit");
        var unsupported=new HabPlacement{design="worker-01",owner="test",serial=1,parent=0,x=4,z=4,floor=3};
        check(!ResidentialRules.Validate(s,unsupported,lib,null,out var missing) && missing.Contains("Unsupported"),"unsupported upper volumes are rejected");
        unsupported.floor=0;
        check(!ResidentialRules.Validate(s,unsupported,lib,null,out missing) && missing.Contains("entrance"),"detached ground housing cannot seal or invent a connection");
        unsupported.x=unsupported.z=0;
        check(!ResidentialRules.Validate(s,unsupported,lib,null,out missing) && missing.Contains("Occupied"),"exact occupied columns reject intersecting rooms");
        var poor=input;poor.credits=0;
        check(!ResidentialRules.Request(s,candidates[0],lib,poor,null),"unfunded new projects cannot start");
        check(ResidentialRules.Request(s,candidates[0],lib,input,null) && s.project.paid==0,"manual approval reserves the same candidate with no upfront double debit");
        var cost=s.project.cost;var duration=s.project.duration;
        var debit=ResidentialRules.Advance(s,lib,duration/4,cost,1,false,null,out var done);
        check(!done && Math.Abs(debit-cost/4)<.001 && ResidentialRules.AddedHousing(s,lib).Sum()==0,"partly built modules consume exact work credits but provide no premature housing");
        var work=s.project.work;
        check(ResidentialRules.Advance(s,lib,10,0,1,false,null,out done)==0 && s.project.work==work,"credit shortage safely pauses residential work");
        check(ResidentialRules.Advance(s,lib,10,cost,1,true,null,out done)==0 && s.project.work==work,"suspension pauses both residential payment and work");
        check(ResidentialRules.Advance(s,lib,10,cost,0,false,null,out done)==0 && s.project.work==work,"actual workforce or production shortage pauses work");
        check(ResidentialRules.Advance(s,lib,10,cost,1,false,c=>false,out done)==0 && s.project.work==work,"new occupancy or terrain obstruction pauses before spending");
        var restored=JsonSerializer.Deserialize<ResidentialState>(JsonSerializer.Serialize(s,json),json);
        check(ResidentialRules.ValidSave(restored) && restored.project.paid==s.project.paid && restored.project.work==s.project.work,"save/load preserves paid credits, remaining work, ownership and chosen site");
        var remaining=ResidentialRules.Advance(restored,lib,duration,cost,1,false,null,out done);
        var housing=ResidentialRules.AddedHousing(restored,lib).Sum();
        check(done && Math.Abs(debit+remaining-cost)<.001 && housing==lib.Find(candidates[0].placement.design).capacity,"completion delivers housing exactly once for the exact total price");
        check(ResidentialRules.Advance(restored,lib,100,cost,1,false,null,out done)==0 && !done && ResidentialRules.AddedHousing(restored,lib).Sum()==housing,"completed projects cannot charge or credit housing again");
        var again=JsonSerializer.Deserialize<ResidentialState>(JsonSerializer.Serialize(restored,json),json);
        check(ResidentialRules.ValidSave(again) && ResidentialRules.Key(again.modules[1])==ResidentialRules.Key(restored.modules[1]) && ResidentialRules.AddedHousing(again,lib).Sum()==housing,"completed module positions and housing survive another save/load");
        check(ResidentialRules.Key(ResidentialRules.Candidates(again,lib,input,null)[0].placement)==ResidentialRules.Key(ResidentialRules.Candidates(restored,lib,input,null)[0].placement),"deterministic continuation retains sequence and completed decisions after load");
        var environment=input;environment.habitability=.25f;environment.co2=1600;environment.water=50;environment.temperature=6;
        // Restrict to matching geometry and capacity so only actual environment preference changes ranking.
        var fixture=new ResidentialLibrary{designs=new[]{Clone(lib.designs[0],"worker-01",1,json),Clone(lib.designs[0],"worker-02",0,json)}};
        var mild=ResidentialRules.Candidates(s=ResidentialRules.Create(12,"test"),fixture,input,null)[0];
        var harsh=ResidentialRules.Candidates(s,fixture,environment,null)[0];
        check(mild.placement.design=="worker-02" && harsh.placement.design=="worker-01","real global habitability, CO2, water and temperature favour enclosed shelter in harsh conditions");
        var legacy=new BuildingOperationState{level=1,improving=true,improvementWork=30,growthPlan=2,firstExpansion=5,manualImprovements=true};
        var oldPrice=AdaptiveBuildingRules.Cost(legacy);var oldDuration=AdaptiveBuildingRules.Duration(legacy);
        var migrated=ResidentialRules.Migrate(ref legacy,123,"legacy");
        check(legacy.level==1 && legacy.manualImprovements && !legacy.improving && migrated.legacyPaid==oldPrice*30/oldDuration && migrated.legacyWork==30,"legacy migration retains completed investment, manual mode, pending cost and paid work");
        check(ReferenceEquals(migrated,ResidentialRules.Migrate(ref legacy,456,"different")),"migration is idempotent and preserves block identity");
        var legacyOptions=ResidentialRules.Candidates(migrated,lib,input,null);
        check(ResidentialRules.Request(migrated,legacyOptions[0],lib,poor,null) && migrated.project.paid==oldPrice*30/oldDuration,"legacy work reserves a legal replacement site without charging again even when credits are empty");
        var legacyRemaining=ResidentialRules.Advance(migrated,lib,oldDuration,oldPrice,1,false,null,out done);
        check(done && Math.Abs(legacyRemaining-oldPrice*(1-30/oldDuration))<.001 && ResidentialRules.AddedHousing(migrated,lib).Sum()==0,"legacy replacement does not duplicate housing already credited through its retained tier economy");
        var corrupt=JsonSerializer.Deserialize<ResidentialState>(JsonSerializer.Serialize(restored,json),json);corrupt.modules[1].owner="other";
        check(!ResidentialRules.ValidSave(corrupt),"save validation rejects foreign module ownership");
        corrupt=JsonSerializer.Deserialize<ResidentialState>(JsonSerializer.Serialize(restored,json),json);corrupt.modules[1].design="worker-99";
        check(!ResidentialRules.ValidSave(corrupt),"save validation rejects unknown module IDs");
        corrupt=JsonSerializer.Deserialize<ResidentialState>(JsonSerializer.Serialize(restored,json),json);corrupt.modules[1].parent=999;
        check(!ResidentialRules.ValidSave(corrupt),"save validation rejects broken parent connections");
        check(ResidentialRules.ValidSave(null) && ResidentialRules.ValidSave(new ResidentialState{version=0}),"older saves without residential fields remain valid");
        // Infill between independent supports remains possible: full bounding rectangles do not claim empty courts.
        var courtyard=lib.Find("worker-08");
        check(!courtyard.columns.Any(c=>c.x==1 && c.z==1) && courtyard.columns.Length==8,"courtyard footprint preserves its genuinely empty centre");
        var small=JsonSerializer.Deserialize<ResidentialLibrary>(JsonSerializer.Serialize(lib,json),json);small.moduleLimit=1;
        check(ResidentialRules.Candidates(ResidentialRules.Create(1,"cap"),small,input,null).Count==0,"development limit bounds searches and completed module counts");
        var galleryCenter=AdaptiveBuildingRules.GalleryLocalCenter(.5f,.67f,.82f);
        check(Math.Abs(.82f+galleryCenter-.085f-.5f)<.00001f && Math.Abs(.82f+galleryCenter+.085f-.67f)<.00001f,
            "shared annex gallery endpoints resolve from annex coordinates to the actual block edge and entrance");
        check(ResidentialRules.BlockSeed(1234,"first")!=ResidentialRules.BlockSeed(1234,"second"),"different block identities receive stable independent variation within one world");
        var profileSave=JsonSerializer.Deserialize<ResidentialState>(JsonSerializer.Serialize(restored,json),json);
        check(profileSave.modules[1].housing==restored.modules[1].housing && profileSave.modules[1].residentClass==restored.modules[1].residentClass,
            "delivered class and housing allocation are explicitly persisted per module");
        var fractional=ResidentialRules.Create(77,"fractional");
        var fractionalOption=ResidentialRules.Candidates(fractional,lib,input,null)[0];
        ResidentialRules.Request(fractional,fractionalOption,lib,input,null);
        var fractionalCost=fractional.project.cost;double fractionalDebit=0;
        for(var step=0;step<20000 && fractional.project!=null;step++)
            fractionalDebit+=ResidentialRules.Advance(fractional,lib,.017f,1000,.7f,false,null,out _);
        check(fractional.project==null && Math.Abs(fractionalDebit-fractionalCost)<.001,
            "many small partial payments cannot strand finished work or overcharge the project");
        string Grow(out HashSet<int> classes)
        {
            var block=ResidentialRules.Create(1234,"mixed");classes=new HashSet<int>();
            for(var step=0;step<18;step++)
            {
                var provision=ResidentialRules.Split(320,lib);var added=ResidentialRules.AddedHousing(block,lib);
                for(var c=0;c<3;c++)provision[c]+=added[c];
                var context=input;context.demand=ResidentialRules.Demand(500,310,1,provision,lib);
                var options=ResidentialRules.Candidates(block,lib,context,null);
                if(options.Count==0)break;
                if(!ResidentialRules.Request(block,options[0],lib,context,null))break;
                ResidentialRules.Advance(block,lib,1000,10000,1,false,null,out _);
            }
            foreach(var module in block.modules)if(module.residentClass>=0)classes.Add(module.residentClass);
            return string.Join(";",block.modules.Select(ResidentialRules.Key));
        }
        var mixed=Grow(out var mixedClasses);
        check(mixedClasses.SetEquals(new[]{0,1,2}),"incremental demand-driven development creates mixed worker, administration and leadership blocks");
        check(mixed==Grow(out _),"an entire sequence of demand-driven growth decisions is deterministic");
        var infill=ResidentialRules.Create(99,"infill");
        infill.modules.Add(new HabPlacement{design="worker-08",owner="infill",serial=1,parent=0,complete=true,x=2,z=0,residentClass=0,housing=80});
        infill.sequence=1;
        var linkOptions=ResidentialRules.Candidates(infill,lib,input,c=>c.bottom==0 && c.x==3 && c.z==1);
        check(linkOptions.Count>0 && linkOptions[0].placement.design=="res-link",
            "a one-cell courtyard gap admits connected infill circulation without inventing another habitation design");
        check(ResidentialRules.ValidLayout(restored,lib) && ResidentialRules.ValidLayout(again,lib),"the save-loader's full replay validates completed layout geometry and explicit class state");
        var overlapping=JsonSerializer.Deserialize<ResidentialState>(JsonSerializer.Serialize(restored,json),json);
        overlapping.modules[1].x=overlapping.modules[1].z=overlapping.modules[1].floor=0;
        check(!ResidentialRules.ValidLayout(overlapping,lib),"full save replay rejects a module moved into an occupied core");
        check(ResidentialRules.Services(80,100,100,lib)==1 && ResidentialRules.Services(0,100,100,lib)==lib.basicServices
            && Math.Abs(ResidentialRules.Services(80,25,100,lib)-.25f)<.00001f,
            "active nearby utility jobs and real global power supply determine service access");
        var constrained=input;constrained.production=.3f;constrained.services=.7f;constrained.staffing=.8f;
        check(ResidentialRules.Labour(constrained)==.3f,"residential work is bounded by actual staffing, production and service availability");
        var unityEmpty=new ResidentialState{project=new HabProject{placement=new HabPlacement()}};
        check(ResidentialRules.ValidSave(unityEmpty) && ResidentialRules.ValidLayout(unityEmpty,lib),
            "Unity-style all-empty inline objects normalize as absent legacy residential fields");
        var unityNullProject=ResidentialRules.Create(4,"unity-null");unityNullProject.project=new HabProject{placement=new HabPlacement()};
        check(ResidentialRules.ValidLayout(unityNullProject,lib) && unityNullProject.project==null,
            "Unity-style empty null-project objects cannot invent construction after a completed save");
        var assets=new[]{"Assets/Resources/ResidentialModules.json","Assets/Scripts/Sim/ResidentialRules.cs",
            "Assets/Scripts/Game/Construction/ResidentialDevelopment.cs","Assets/Scripts/Game/Construction/ResidentialVisual.cs",
            "Assets/Scripts/Game/Construction/ResidentialModuleSelection.cs","Assets/Scripts/Game/Construction/ResidentialOwnedMesh.cs","Assets/Editor/ResidentialSceneChecks.cs"};
        check(assets.All(a=>File.Exists(Path.Combine(root,a+".meta")) && System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(Path.Combine(root,a+".meta")),@"(?m)^guid: [0-9a-f]{32}\s*$")),
            "every new residential Unity resource and script includes persistent GUID metadata");
        var completedLegacy=new BuildingOperationState{level=2,focus=2,firstExpansion=2,secondExpansion=5};
        var legacyEconomy=new BuildingEconomy{capacity=320,jobs=40,upkeep=8,outputPerWorker=.15f};
        var economyBefore=BuildingOperationRules.Economy("HabBlock",legacyEconomy,completedLegacy);
        ResidentialRules.Migrate(ref completedLegacy,1,"old-complete");var economyAfter=BuildingOperationRules.Economy("HabBlock",legacyEconomy,completedLegacy);
        check(economyBefore.capacity==economyAfter.capacity && economyBefore.jobs==economyAfter.jobs && economyBefore.upkeep==economyAfter.upkeep
            && completedLegacy.residential.modules.Count==1 && ResidentialRules.AddedHousing(completedLegacy.residential,lib).Sum()==0,
            "completed historical radial tiers preserve their complete focused economy without duplicate replacement housing");
        var spaced=ResidentialRules.Create(1234,"spaced");var spacedOptions=ResidentialRules.Candidates(spaced,lib,input,null);
        check(spacedOptions[0].placement.linkLength==lib.spacing && spacedOptions[0].placement.floor==0,
            "open terrain favours separated residences with a covered connecting gallery");
        var spacedPlacement=spacedOptions[0].placement;
        var passage=ResidentialRules.Cells(spacedPlacement,lib).Where(c=>c.passage).ToArray();
        var body=ResidentialRules.BodyCells(spacedPlacement,lib);
        check(passage.Length==2 && passage.All(c=>!body.Any(b=>b.x==c.x && b.z==c.z))
            && ResidentialRules.Validate(spaced,spacedPlacement,lib,null,out _),
            "gallery footprints occupy the gap and meet actual parent and residence entrances");
        var broken=JsonSerializer.Deserialize<HabPlacement>(JsonSerializer.Serialize(spacedPlacement,json),json);broken.linkX+=1;
        check(!ResidentialRules.Validate(spaced,broken,lib,null,out _),"misaligned gallery endpoints cannot create a detached residence");
        ResidentialRules.Request(spaced,spacedOptions[0],lib,input,null);
        var initialKey=ResidentialRules.Key(spaced.project.placement);var approvedDesign=spaced.project.placement.design;
        var approvedCost=spaced.project.cost;var approvedDuration=spaced.project.duration;
        var firstDebit=ResidentialRules.Advance(spaced,lib,approvedDuration/3,10000,1,false,null,out _);
        var paidBefore=spaced.project.paid;var workBefore=spaced.project.work;
        var blocked=ResidentialRules.Cells(spaced.project.placement,lib).Select(c=>(c.x,c.z)).ToHashSet();
        bool RedirectSite(HabCell c)=>c.bottom==0 && !blocked.Contains((c.x,c.z));
        var noCurrentDemand=input;noCurrentDemand.demand=new float[3];noCurrentDemand.credits=0;
        check(ResidentialRules.Replan(spaced,lib,noCurrentDemand,RedirectSite)
            && ResidentialRules.Key(spaced.project.placement)!=initialKey && spaced.project.placement.design==approvedDesign,
            "an encroached project chooses another ground direction even with zero current demand and credits");
        check(spaced.project.paid==paidBefore && spaced.project.work==workBefore && spaced.project.cost==approvedCost
            && spaced.project.duration==approvedDuration && spaced.project.relocations==1,
            "redirecting preserves the approved design, locked price, paid credits and remaining work");
        var redirectedSave=JsonSerializer.Deserialize<ResidentialState>(JsonSerializer.Serialize(spaced,json),json);
        check(ResidentialRules.ValidLayout(redirectedSave,lib) && redirectedSave.project.placement.linkLength>0 && redirectedSave.project.relocations==1,
            "redirected gallery layout and paid work survive a validated save/load");
        var redirectedKey=ResidentialRules.Key(spaced.project.placement);
        check(!ResidentialRules.Replan(spaced,lib,input,c=>false) && ResidentialRules.Key(spaced.project.placement)==redirectedKey
            && spaced.project.paid==paidBefore,"no alternative site retains the original project and investment safely");
        var remainingDebit=ResidentialRules.Advance(spaced,lib,approvedDuration,10000,1,false,RedirectSite,out var redirectedComplete);
        check(redirectedComplete && Math.Abs(firstDebit+remainingDebit-approvedCost)<.001 && spaced.modules.Count==2,
            "a redirected project completes once for its original total price");
        var nextVaried=ResidentialRules.Candidates(spaced,lib,input,null)[0];
        check(nextVaried.placement.design!=approvedDesign,"growth scores discourage repeating the same design when alternatives fit");
        check(lib.designs.Select(d=>d.wallColor+":"+d.roofColor+":"+d.trimColor).Distinct().Count()==60
            && Enumerable.Range(0,3).All(c=>lib.designs.Where(d=>d.residentClass==c).Select(d=>(d.roofStyle,d.facadeStyle)).Distinct().Count()==20),
            "sixty designs have unique colour palettes and twenty roof/facade combinations in each class");
        var legacyAttached=JsonSerializer.Deserialize<ResidentialState>(JsonSerializer.Serialize(restored,json),json);
        check(ResidentialRules.ValidLayout(legacyAttached,lib),"existing attached module saves remain valid without moving completed buildings");
        var tight=ResidentialRules.Candidates(ResidentialRules.Create(11,"tight"),lib,input,
            c=>c.bottom==0 && c.x>=2 && c.x<=5 && c.z==-2);
        check(tight.Count>0 && tight[0].placement.linkLength==0,
            "a genuinely constrained site can use compact infill instead of claiming an impossible gap");
        var bridge=ResidentialRules.Cells(spaced.modules[1],lib).First(c=>c.passage);
        var overBridge=new HabPlacement{design="res-link",owner=spaced.blockId,serial=2,parent=1,x=bridge.x,z=bridge.z,floor=1};
        check(!ResidentialRules.Validate(spaced,overBridge,lib,null,out var bridgeReason) && bridgeReason.Contains("Unsupported"),
            "narrow connecting galleries cannot be treated as load-bearing residential roofs");
        var roofRescue=ResidentialRules.Create(18,"roof-rescue");
        var longhouse=ResidentialRules.Candidates(roofRescue,lib,input,null,5,"worker-03")[0];
        ResidentialRules.Request(roofRescue,longhouse,lib,input,null);
        check(ResidentialRules.Replan(roofRescue,lib,noCurrentDemand,c=>c.bottom>0) && roofRescue.project.placement.floor>0
            && ResidentialRules.Validate(roofRescue,roofRescue.project.placement,lib,c=>c.bottom>0,out _),
            "blocked longhouses can align to the real full roof edge rather than assuming its origin is a support surface");
        var growthMix=new List<int>();
        for(var preference=0;preference<3;preference++)
        {
            var block=ResidentialRules.Create(1234,"bidirectional-"+preference);var context=input;
            context.preference=preference;context.demand=new[]{10000f,10000f,10000f};
            var expectedHousing=0f;var paidTotal=0f;var quotedTotal=0f;var sound=true;
            for(var step=0;step<6;step++)
            {
                var choices=ResidentialRules.Candidates(block,lib,context,null);
                if(choices.Count==0 || !ResidentialRules.Request(block,choices[0],lib,context,null)){sound=false;break;}
                var project=block.project;quotedTotal+=project.cost;
                paidTotal+=ResidentialRules.Advance(block,lib,project.duration/2,100000,1,false,null,out _);
                sound &= ResidentialRules.AddedHousing(block,lib).Sum()==expectedHousing;
                paidTotal+=ResidentialRules.Advance(block,lib,project.duration,100000,1,false,null,out var finished);
                expectedHousing+=project.placement.housing;
                sound &= finished && ResidentialRules.AddedHousing(block,lib).Sum()==expectedHousing;
            }
            growthMix.Add(block.modules.Count(m=>m.serial>0 && m.floor>0));
            check(sound && block.modules.Count==7 && block.modules.Any(m=>m.serial>0 && m.floor==0)
                && block.modules.Any(m=>m.floor>0) && ResidentialRules.ValidLayout(block,lib),
                "preference "+preference+" combines outward wings and supported upper floors in one valid block");
            check(sound && expectedHousing>0 && Math.Abs(paidTotal-quotedTotal)<.01f,
                "preference "+preference+" increases internal housing exactly once for every completed floor or wing, at its exact price");
            var roundtrip=JsonSerializer.Deserialize<ResidentialState>(JsonSerializer.Serialize(block,json),json);
            check(ResidentialRules.ValidLayout(roundtrip,lib) && ResidentialRules.AddedHousing(roundtrip,lib).Sum()==expectedHousing,
                "mixed horizontal/vertical capacity and placements survive save/load for preference "+preference);
        }
        check(growthMix[1]>growthMix[0] && growthMix[0]>growthMix[2],
            "upward, balanced and outward preferences produce different mixes while retaining both directions");
        var incrementalBlock=ResidentialRules.Create(321,"incremental");
        var beforeSearch=JsonSerializer.Serialize(incrementalBlock,json);var siteCalls=0;
        var job=ResidentialRules.BeginCandidates(incrementalBlock,lib,input,c=>{siteCalls++;return true;});
        var completeInOneStep=job.Step();
        check(!completeInOneStep && siteCalls<=128*66 && job.Result.Count==0,
            "a background planner yields after bounded work and publishes no unfinished previews");
        var slices=1;while(!job.Step())slices++;
        var synchronous=ResidentialRules.Candidates(incrementalBlock,lib,input,null);
        check(slices<256 && job.Result.Select(c=>ResidentialRules.Key(c.placement)).SequenceEqual(synchronous.Select(c=>ResidentialRules.Key(c.placement)))
            && beforeSearch==JsonSerializer.Serialize(incrementalBlock,json),
            "budgeted planning matches synchronous results without changing reservations, capacity or saved state");
        // Allocation regression, not a machine-dependent timing or Unity frame-rate assertion.
        var allocatedBefore=GC.GetAllocatedBytesForCurrentThread();
        var perfBlock=ResidentialRules.Create(1234,"allocation");
        for(var step=0;step<6;step++)
        {
            var options=ResidentialRules.Candidates(perfBlock,lib,input,null);
            if(options.Count==0)break;
            ResidentialRules.Request(perfBlock,options[0],lib,input,null);
            ResidentialRules.Advance(perfBlock,lib,10000,100000,1,false,null,out _);
        }
        var allocated=GC.GetAllocatedBytesForCurrentThread()-allocatedBefore;
        check(allocated<32L*1024*1024,"six growing-block planning passes allocate less than 32 MiB after warmup");
    }
    public static void Benchmark(string root,JsonSerializerOptions json)
    {
        var lib=JsonSerializer.Deserialize<ResidentialLibrary>(File.ReadAllText(Path.Combine(root,"Assets/Resources/ResidentialModules.json")),json);
        var input=new ResidentialInputs{credits=100000,demand=new[]{10000f,10000f,10000f},staffing=1,production=1,services=1,habitability=1};
        void Sequence()
        {
            var block=ResidentialRules.Create(1234,"bench");
            for(var i=0;i<6;i++)
            {
                var options=ResidentialRules.Candidates(block,lib,input,null);
                if(options.Count==0 || !ResidentialRules.Request(block,options[0],lib,input,null))throw new Exception("Benchmark could not reserve addition "+i);
                ResidentialRules.Advance(block,lib,10000,100000,1,false,null,out var complete);
                if(!complete)throw new Exception("Benchmark did not complete addition "+i);
            }
        }
        Sequence();var allocated=GC.GetAllocatedBytesForCurrentThread();var timer=System.Diagnostics.Stopwatch.StartNew();
        for(var i=0;i<3;i++)Sequence();timer.Stop();
        var bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
        Console.WriteLine($"Residential benchmark: three six-addition sequences, {timer.Elapsed.TotalMilliseconds:F1} ms, {bytes/1048576.0:F2} MiB allocated. Pure .NET rules; no Unity rendering or scene collision timings.");
    }
    private static HabDesign Clone(HabDesign source,string id,float enclosure,JsonSerializerOptions json)
    {var d=JsonSerializer.Deserialize<HabDesign>(JsonSerializer.Serialize(source,json),json);d.id=id;d.enclosure=enclosure;return d;}
    private static bool Connected(HabColumn[] cells)
    {
        var reached=new HashSet<(int,int)>{(cells[0].x,cells[0].z)};var changed=true;
        while(changed){changed=false;foreach(var c in cells)if(!reached.Contains((c.x,c.z)) && reached.Any(a=>Math.Abs(a.Item1-c.x)+Math.Abs(a.Item2-c.z)==1)){reached.Add((c.x,c.z));changed=true;}}
        return reached.Count==cells.Length;
    }
}
