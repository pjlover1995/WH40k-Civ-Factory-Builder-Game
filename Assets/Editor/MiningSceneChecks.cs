using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Sim;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;
using WH30K.UI;

public static class MiningSceneChecks
{
    private const string BatchKey="WH30K.SolidMining.Batch";
    public static void RunBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
        SessionState.SetBool(BatchKey,true);EditorApplication.playModeStateChanged+=BatchState;EditorApplication.isPlaying=true;
    }
    [InitializeOnLoadMethod]
    private static void ResumeBatch()
    {
        if(!SessionState.GetBool(BatchKey,false))return;
        EditorApplication.playModeStateChanged+=BatchState;
        if(EditorApplication.isPlaying)EditorApplication.delayCall+=FinishBatch;
    }
    private static void BatchState(PlayModeStateChange state)
    {if(state==PlayModeStateChange.EnteredPlayMode)EditorApplication.delayCall+=FinishBatch;}
    private static void FinishBatch()
    {
        if(!SessionState.GetBool(BatchKey,false))return;SessionState.EraseBool(BatchKey);
        try{Run();SceneCheckExit.Exit(0);}catch(Exception error){Debug.LogException(error);SceneCheckExit.Exit(1);}
    }
    [MenuItem("WH30K/Run Solid Mining Checks (Play Mode)")]
    public static void Run()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Open WH30K_VS01 and enter Play Mode.");
        var bootstrap=UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        var original=GameSettings.HasActiveGame&&!bootstrap.ChoosingLanding?bootstrap.CaptureSaveJson():null;
        var terrain=bootstrap.NewWorldTerrain;var count=0;
        try
        {
            bootstrap.ConfigureNewWorldTerrain(new WH30K.Planet.TerrainSettings());
            RunGameplay(bootstrap,(ok,label)=>{if(!ok)throw new InvalidOperationException("Solid mining FAIL: "+label);count++;Debug.Log("Solid mining PASS: "+label);});
            Debug.Log($"Solid mining: {count} actual scene checks passed in Unity {Application.unityVersion}.");
        }
        finally{bootstrap.ConfigureNewWorldTerrain(terrain);if(original!=null&&!bootstrap.TryLoadSaveJson(original))Debug.LogError("Could not restore original in-memory colony.");}
    }
    public static void RunGameplay(PlanetBootstrap bootstrap,Action<bool,string> check)
    {
        bootstrap.BeginNewGame(8123,GameSettings.Difficulty.Standard);
        var industry=bootstrap.GetComponent<IndustrySystem>();var placement=bootstrap.GetComponent<BuildingPlacementController>();
        var settlement=bootstrap.GetComponent<Settlement>();var planet=bootstrap.ActivePlanet;
        var normal=settlement.CreateSnapshot().normal;
        foreach(var deposit in industry.Deposits)deposit.Discovered=true;
        SurfaceDeposit chosen=null;
        placement.SelectStructure("Mine");
        foreach(var deposit in industry.Deposits.Where(d=>d.Kind==MaterialKind.IronOre).OrderByDescending(d=>d.Position.magnitude))
        {
            if(Vector3.Distance(deposit.Position,settlement.CoreBuildings[0].transform.position)<500)continue;
            if(planet.EvaluateBaseHeight(deposit.Direction)-deposit.Excavation.depth<=planet.SeaLevel+15)continue;
            if(!placement.TryPlaceDirection(deposit.Direction))continue;
            chosen=industry.DepositFor(placement.Buildings.Last());break;
        }
        check(chosen!=null,"a mineral click constructs an actual mine at a clear rim site");
        var mine=placement.Buildings[0];var anchor=mine.transform.position;var id=chosen.Id;
        check(industry.ClearOfMineralVeins(anchor,mine.FootprintRadius),"mine footprint lies beyond the full surveyed mineral body");
        placement.SelectStructure("Factory");
        check(!placement.TryPlaceDirection(chosen.Direction),"new buildings cannot obstruct a future solid pit");
        placement.SelectStructure("PromethiumGenerator");var generatorPlaced=false;
        var n=planet.transform.InverseTransformPoint(anchor).normalized;
        var right=Vector3.Cross(n,Mathf.Abs(n.y)>.95f?Vector3.right:Vector3.up).normalized;var forward=Vector3.Cross(n,right);
        for(var i=0;i<64;i++)
        {
            var a=i*Mathf.PI/8;var distance=70+i/16*20;
            if(!placement.TryPlaceDirection((n+(right*Mathf.Cos(a)+forward*Mathf.Sin(a))*distance/planet.Radius).normalized))continue;
            generatorPlaced=true;break;
        }
        check(generatorPlaced,"a nearby fueled generator can supply the remote mine network");
        var saved=industry.Capture();
        MiningRules.ExtractSolid(saved.deposits[id].excavation,MiningRules.Capacity(saved.deposits[id].excavation)*.35f);
        saved.deposits[id].remaining=MiningRules.Remaining(saved.deposits[id].excavation);
        saved.facilities[1].quantities[(int)MaterialKind.Promethium]=80;
        industry.Configure(planet,normal,saved);chosen=industry.Deposits[id];
        check(industry.PitHaulerCount==2 && chosen.Excavation.layoutVersion==1,"restoring a worked solid vein reconstructs the ramp and two haul vehicles");
        var root=planet.transform.Find("Geological survey and cargo/Haul ramp and vehicles - survey "+id);
        var truck=root.GetChild(0);var road=root.GetComponent<MeshFilter>().sharedMesh;
        check(road.vertexCount>1000 && road.subMeshCount==2,"graded road and paired tyre tracks have actual terrain mesh geometry");
        var roadTolerance=Mathf.Max(.08f,planet.Radius*3e-7f);
        check(road.vertices.All(v=>Mathf.Abs(v.magnitude-planet.EvaluateSurfacePoint(v.normalized).magnitude-.2f)<roadTolerance),
            "every road and track vertex follows the shared excavated terrain heightfield");
        var reserve=chosen.Remaining;var position=truck.position;
        industry.Simulate(20);
        check(chosen.Remaining<reserve && Vector3.Distance(truck.position,position)>.1f && !industry.HasMiningDecision,
            "powered production removes solid reserves and moves haul trucks without directional restrictions");
        ResourceDepositsSceneChecks.RunGameplay(bootstrap,chosen,check);
        position=truck.position;reserve=chosen.Remaining;industry.Simulate(0);
        check(truck.position==position && chosen.Remaining==reserve,"zero simulated time stops extraction and vehicle travel together");
        mine.ToggleOperation();position=truck.position;reserve=chosen.Remaining;industry.Simulate(20);
        check(truck.position==position && chosen.Remaining==reserve,"suspending the mine parks trucks and preserves ore");
        mine.ToggleOperation();
        var menu=bootstrap.GetComponent<NewGameMenu>();menu.OpenExcavationPlan(mine);
        check(!menu.GetComponentsInChildren<Button>(true).Any(b=>b.gameObject.activeInHierarchy && b.name.StartsWith("Sector")),
            "the new solid excavation plan exposes no directional growth limits");
        menu.CloseMiningPanel();
        var snapshot=JsonUtility.ToJson(industry.Capture());var json=bootstrap.CaptureSaveJson();
        var loaded=bootstrap.TryLoadSaveJson(json);
        check(loaded && GameplayChecks.EquivalentIndustryJson(JsonUtility.ToJson(industry.Capture()),snapshot)
            && Vector3.Distance(placement.Buildings[0].transform.position,anchor)<.01f && industry.PitHaulerCount==2,
            "full scene save/load preserves pit geometry, exact reserves and rim foundation while reconstructing haulage");
        check(industry.Deposits.Where(d=>d.Kind==MaterialKind.Biomass||d.Kind==MaterialKind.Promethium).All(d=>d.Excavation==null),
            "biomass and liquid fuel never acquire mineral pits");
        saved=industry.Capture();MiningRules.ExtractSolid(saved.deposits[id].excavation,float.MaxValue);saved.deposits[id].remaining=0;
        industry.Configure(bootstrap.ActivePlanet,normal,saved);reserve=industry.Deposits[id].Remaining;
        root=bootstrap.ActivePlanet.transform.Find("Geological survey and cargo/Haul ramp and vehicles - survey "+id);truck=root.GetChild(0);position=truck.position;
        industry.Simulate(30);
        check(industry.Deposits[id].Remaining==reserve && truck.position==position && placement.Buildings[0].transform.position==anchor,
            "an exhausted pit remains, the rim building stays fixed and vehicles cease working");
    }
}
