#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Sim;
using WH30K.Sim.Environment;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;

public static class ResidentialSceneChecks
{
    private const string BatchKey="WH30K.ResidentialChecks.Batch";
    public static void RunBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
        SessionState.SetBool(BatchKey,true);EditorApplication.playModeStateChanged-=BatchPlayState;
        EditorApplication.playModeStateChanged+=BatchPlayState;EditorApplication.isPlaying=true;
    }
    [InitializeOnLoadMethod]
    private static void ResumeBatch()
    {
        if(!SessionState.GetBool(BatchKey,false))return;
        EditorApplication.playModeStateChanged+=BatchPlayState;
        if(EditorApplication.isPlaying)EditorApplication.delayCall+=FinishBatch;
    }
    private static void BatchPlayState(PlayModeStateChange state)
    {if(state==PlayModeStateChange.EnteredPlayMode)EditorApplication.delayCall+=FinishBatch;}
    private static void FinishBatch()
    {
        if(!SessionState.GetBool(BatchKey,false))return;
        SessionState.EraseBool(BatchKey);
        try{Run();SceneCheckExit.Exit(0);}
        catch(Exception error){Debug.LogException(error);SceneCheckExit.Exit(1);}
    }
    [MenuItem("WH30K/Run Residential Checks (Play Mode)")]
    public static void Run()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Open WH30K_VS01 and enter Play Mode.");
        var bootstrap=UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        var original=GameSettings.HasActiveGame?bootstrap.CaptureSaveJson():null;var count=0;
        try
        {
            bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard, legacyCore: true);
            RunGameplay(bootstrap,(ok,label)=>{if(!ok)throw new InvalidOperationException("Residential FAIL: "+label);count++;Debug.Log("Residential PASS: "+label);});
            Debug.Log($"Residential: {count} actual scene checks passed in Unity {Application.unityVersion}.");
        }
        finally{if(original!=null && !bootstrap.TryLoadSaveJson(original))Debug.LogError("Residential checks could not restore the original in-memory colony.");}
    }
    public static void RunGameplay(PlanetBootstrap bootstrap,Action<bool,string> check)
    {
        var settlement=bootstrap.GetComponent<Settlement>();var resources=bootstrap.GetComponent<ResourceSystem>();
        var environment=bootstrap.GetComponent<EnvironmentState>();var selection=bootstrap.GetComponent<BuildingSelection>();
        var snapshot=settlement.CreateSnapshot();snapshot.population=2000;snapshot.workforce=1240;
        var op=snapshot.operations[0];op.manualImprovements=true;op.focus=1;snapshot.operations[0]=op;
        settlement.LoadFromSnapshot(snapshot,bootstrap.ActivePlanet,GameSettings.GetDefinition(environment.Difficulty),resources,environment);
        settlement.RefreshReadout();resources.ModifyStockpile(20000);
        var hab=settlement.CoreBuildings[0];var res=hab.Residential;hab.SetGrowthPlan(1);res.RefreshCandidates();selection.Select(hab);
        check(res!=null && hab.DisplayName=="Residential Block 1" && hab.GetComponent<AdaptiveBuildingVisual>()==null,
            "core scene wiring uses residential modules and the stable HabBlock identity");
        check(!hab.GetComponent<MeshRenderer>().enabled && !hab.GetComponent<BoxCollider>().enabled
            && hab.GetComponentsInChildren<ResidentialModuleSelection>().Length==1,"legacy hab mesh/collider do not render or pick over the founding module");
        check(res.Candidates.Count>0 && res.Candidates.All(c=>ResidentialRules.Validate(res.Data,c.placement,ResidentialDevelopment.Library,null,out _)),
            "live preview candidates have valid module connections and support");
        var boundary=hab.GetComponentsInChildren<LineRenderer>().FirstOrDefault(l=>l.name=="Developed pale yellow perimeter");
        check(boundary!=null && boundary.loop && boundary.positionCount>=4 && boundary.positionCount!=48,
            "selected residential block shows a continuous occupied-cell perimeter instead of the circle");
        var baseCapacity=hab.Economy.capacity;var balance=resources.Stockpile;
        GameObject.Find("UpgradeBuilding").GetComponent<Button>().onClick.Invoke();
        check(res.Data.project!=null && res.Data.project.paid==0 && resources.Stockpile==balance,"inspector approval starts a funded project without an upfront charge");
        var cost=res.Data.project.cost;var duration=res.Data.project.duration;
        hab.SimulateImprovement(duration/4,1);
        check(Mathf.Abs(res.Data.project.work-duration/4)<.01f && Mathf.Abs(resources.Stockpile-balance+cost * ColonyRules.LegacyProjectSupplyScale/4)<.01f
            && hab.Economy.capacity==baseCapacity,"scene work debits only paid progress and waits to deliver housing");
        var pendingJson=bootstrap.CaptureSaveJson();
        check(bootstrap.TryLoadSaveJson(pendingJson),"actual JsonUtility and save loader accept an in-progress residential project");
        hab=settlement.CoreBuildings[0];res=hab.Residential;selection.Select(hab);
        check(res.Data.project!=null && Mathf.Abs(res.Data.project.paid-cost/4)<.01f && Mathf.Abs(res.Data.project.work-duration/4)<.01f,
            "scene reload preserves exact residential payment, chosen placement and work");
        hab.ToggleOperation();var paused=res.Data.project.work;hab.SimulateImprovement(20,1);
        check(res.Data.project.work==paused && hab.Economy.capacity==0,"suspension pauses construction and removes residential provision");
        hab.ToggleOperation();hab.SimulateImprovement(duration*4,1);
        check(res.Data.project==null && res.Data.modules.Count==2 && hab.Economy.capacity>baseCapacity
            && Mathf.Abs(resources.Stockpile-balance+cost * ColonyRules.LegacyProjectSupplyScale)<.02f,"completed scene housing is counted once and charges its exact cost");
        var provided=settlement.ResidentialProvision();
        check(Mathf.Abs(provided.Sum()-settlement.AllBuildings.Sum(b=>b.Economy.capacity))<.01f,"class provision sums to the actual colony housing economy");
        var module=hab.GetComponentsInChildren<ResidentialModuleSelection>().First(m=>m.Serial==1);
        res.SelectedModule=module.Serial;
        check(res.Status.Contains(ResidentialRules.ClassName(res.Data.modules[1].residentClass)) && module.GetComponents<BoxCollider>().Length>0,
            "individual module picking retains class, capacity, role, connection and block context");
        selection.Select(null);check(!res.Selected,"deselection hides the residential boundary and suggestions");
        var visual=hab.GetComponent<ResidentialVisual>();var built=visual.ModuleObjectsBuilt;
        var founding=hab.GetComponentsInChildren<ResidentialModuleSelection>().First(m=>m.Serial==0).gameObject;
        visual.Rebuild();visual.Rebuild();
        check(visual.ModuleObjectsBuilt==built && founding==hab.GetComponentsInChildren<ResidentialModuleSelection>().First(m=>m.Serial==0).gameObject,
            "unchanged visual refreshes retain completed meshes, foundations and picking objects");
        var query=new Bounds(new Vector3(30,10,30),Vector3.one*.01f);
        var firstClear=settlement.ResidentialCellClear(hab,query);var volumeBuilds=settlement.ResidentialVolumeBuilds;
        check(settlement.ResidentialCellClear(hab,query)==firstClear && settlement.ResidentialVolumeBuilds==volumeBuilds,
            "repeated collision queries reuse the colony geometry snapshot");
        settlement.NotifyResidentialGeometry();settlement.ResidentialCellClear(hab,query);
        check(settlement.ResidentialVolumeBuilds==volumeBuilds+1,"geometry changes invalidate cached collision volumes before the next query");
        for(var addition=0;addition<4;addition++)
        {
            res.RefreshCandidates();var before=hab.Economy.capacity;
            check(hab.Upgrade(),"mixed scene growth reserves another demanded supported addition");
            var additionProject=res.Data.project;var expected=additionProject.placement.housing*hab.HousingFocusFactor;
            built=visual.ModuleObjectsBuilt;
            for(var frame=0;frame<100;frame++)visual.UpdateConstruction();
            check(visual.ModuleObjectsBuilt==built,"construction progress reuses its module and work-plane objects");
            hab.SimulateImprovement(additionProject.duration*4,1);
            check(res.Data.project==null && Mathf.Abs(hab.Economy.capacity-before-expected)<.01f,
                "each completed upper floor or outward wing increases actual scene housing by its focused capacity");
        }
        check(res.Data.modules.Any(m=>m.serial>0 && m.floor==0) && res.Data.modules.Any(m=>m.floor>0),
            "a single real scene block develops both outward and upward under the upward preference");
        check(Mathf.Abs(settlement.ResidentialProvision().Sum()-settlement.AllBuildings.Sum(item=>item.Economy.capacity))<.01f,
            "mixed scene growth retains exact colony capacity and class totals");
        // Known geometry regressions are checked numerically in Unity using the actual mesh builder.
        var b=new HabMeshBuilder();b.Box(0,Vector3.zero,Vector3.one);var mesh=b.Build("Face orientation check");
        var vertices=mesh.vertices;var indices=mesh.triangles;var outward=true;
        for(var i=0;i<indices.Length;i+=3)
        {
            var a=vertices[indices[i]];var c=vertices[indices[i+1]];var d=vertices[indices[i+2]];
            if(Vector3.Dot(Vector3.Cross(c-a,d-a),(a+c+d)/3)<=0)outward=false;
        }
        check(outward,"all residential box triangles face outward");UnityEngine.Object.Destroy(mesh);
        foreach(var definition in ResidentialDevelopment.Library.designs)
        {
            var cells=ResidentialRules.Cells(new HabPlacement{design=definition.id},ResidentialDevelopment.Library);
            var designMesh=ResidentialVisual.DesignMesh(definition);
            var designVertices=designMesh.vertices;
            check(designMesh.uv.Length==designMesh.vertexCount && designMesh.uv.Distinct().Count()>1,
                definition.id+" carries real architectural texture coordinates");
            check(cells.Count==definition.columns.Length && cells.All(c=>c.top>c.bottom)
                && designVertices.All(v=>cells.Any(c=>v.x>=c.x*.2f-.003f && v.x<=(c.x+1)*.2f+.003f
                    && v.z>=c.z*.2f-.003f && v.z<=(c.z+1)*.2f+.003f))
                && designMesh.bounds.max.y<=ResidentialRules.Deck+cells.Max(c=>c.top)*ResidentialRules.FloorHeight+.003f,
                definition.id+" generated architecture stays inside its exact occupied footprint and structural height");
            if(definition.roofStyle==0)
                check(designMesh.GetTriangles(0).Max(i=>designVertices[i].y)<designMesh.GetTriangles(2).Max(i=>designVertices[i].y)-.001f,
                    definition.id+" roof finish is separated from the stone ceiling");
        }
        check(ResidentialDevelopment.Library.designs.Select(d=>ResidentialVisual.DesignMaterials(d)[0].color).Distinct().Count()==60,
            "all sixty generated designs use distinct actual wall materials");
        var pendingState=hab.State;pendingState.residential.project=null;hab.Restore(pendingState);hab.Residential.RefreshCandidates();
        hab.Upgrade();var project=hab.State.residential.project;
        check(project!=null,"redirect fixture reserves a real scene project");
        if(project!=null)
        {
            hab.SimulateImprovement(project.duration*.2f,1);
            var savedPaid=project.paid;var savedWork=project.work;
            // Move only the pending reservation, not completed structures, into the founding occupied volume.
            project.placement.x=project.placement.z=project.placement.floor=0;project.placement.linkLength=0;
            var invalidKey=ResidentialRules.Key(project.placement);
            hab.Residential.Invalidate();hab.SimulateImprovement(.25f,1);
            check(project.relocations==1 && ResidentialRules.Key(project.placement)!=invalidKey
                && project.paid>=savedPaid && project.work>=savedWork,
                "a real blocked project redirects automatically without losing paid work");
        }
        // Legacy tiers consolidate inside the existing founding footprint; queued investment moves to a legal candidate.
        var legacy=new BuildingOperationState{level=1,focus=1,manualImprovements=true,improving=true,improvementWork=30,growthPlan=1};
        var oldCost=AdaptiveBuildingRules.Cost(legacy);var oldDuration=AdaptiveBuildingRules.Duration(legacy);
        hab.Restore(legacy);res=hab.Residential;res.RefreshCandidates();
        check(res.Data.modules.Count==1 && res.Data.legacyPaid==oldCost*30/oldDuration && hab.State.level==1,
            "scene migration retains historical completed tier and pending paid work without rendering old annexes");
        var legacyBalance=resources.Stockpile;
        check(hab.Upgrade(),"migrated queued work finds a legal supported site through normal approval");
        hab.SimulateImprovement(oldDuration*4,1);
        check(hab.State.level==2 && res.Data.project==null && ResidentialRules.AddedHousing(res.Data,ResidentialDevelopment.Library).Sum()==0
            && Mathf.Abs(resources.Stockpile-legacyBalance+oldCost*(1-30/oldDuration)*ColonyRules.LegacyProjectSupplyScale)<.02f,
            "legacy completion preserves its exact remaining price and grants its original economic tier once");
        check(bootstrap.TryLoadSaveJson(bootstrap.CaptureSaveJson()) && settlement.CoreBuildings[0].State.residential.modules.Count==2,
            "completed migrated residential state survives the real loader");
        hab=settlement.CoreBuildings[0];op=hab.State;op.manualImprovements=false;hab.Restore(op);hab.Residential.RefreshCandidates();
        hab.SimulateImprovement(.5f,1);
        check(hab.State.residential.project!=null,"automatic residential mode commissions the same funded demand-driven candidate set");
    }
}
#endif
