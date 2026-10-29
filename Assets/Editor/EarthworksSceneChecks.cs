using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Sim;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;

public static class EarthworksSceneChecks
{
    private const string BatchKey="WH30K.EarthworksChecks.Batch";
    public static void RunBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
        SessionState.SetBool(BatchKey,true); EditorApplication.playModeStateChanged+=BatchPlayState; EditorApplication.isPlaying=true;
    }
    [InitializeOnLoadMethod]
    private static void ResumeBatch()
    {
        if(!SessionState.GetBool(BatchKey,false))return;
        EditorApplication.playModeStateChanged+=BatchPlayState;
        if(EditorApplication.isPlaying)EditorApplication.delayCall+=FinishBatch;
    }
    private static void BatchPlayState(PlayModeStateChange state)
    { if(state==PlayModeStateChange.EnteredPlayMode)EditorApplication.delayCall+=FinishBatch; }
    private static void FinishBatch()
    {
        if(!SessionState.GetBool(BatchKey,false))return;
        SessionState.EraseBool(BatchKey);
        try{Run();SceneCheckExit.Exit(0);}catch(Exception error){Debug.LogException(error);SceneCheckExit.Exit(1);}
    }
    [MenuItem("WH30K/Run Earthworks Checks (Play Mode)")]
    public static void Run()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Open WH30K_VS01 and enter Play Mode.");
        var bootstrap=UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        var original=GameSettings.HasActiveGame?bootstrap.CaptureSaveJson():null;
        var settings=bootstrap.NewWorldTerrain; var count=0;
        try
        {
            RunGameplay(bootstrap,(ok,label)=>{if(!ok)throw new InvalidOperationException("Earthworks FAIL: "+label);count++;Debug.Log("Earthworks PASS: "+label);});
            Debug.Log($"Earthworks: {count} actual scene checks passed in Unity {Application.unityVersion}.");
        }
        finally
        {
            bootstrap.ConfigureNewWorldTerrain(settings);
            if(original!=null && !bootstrap.TryLoadSaveJson(original))Debug.LogError("Could not restore the original in-memory colony.");
        }
    }

    public static void RunGameplay(PlanetBootstrap bootstrap,Action<bool,string> check)
    {
        bootstrap.ConfigureNewWorldTerrain(new WH30K.Planet.TerrainSettings { radius=6000 });
        bootstrap.BeginNewGame(1234, GameSettings.Difficulty.Standard, legacyCore: true);
        GameplayChecks.FundConstruction(bootstrap);
        var tools=bootstrap.GetComponent<EarthworksController>(); var placement=bootstrap.GetComponent<BuildingPlacementController>();
        var settlement=bootstrap.GetComponent<Settlement>(); var industry=bootstrap.GetComponent<IndustrySystem>();
        var planet=bootstrap.ActivePlanet;
        check(tools!=null && tools.HasVehicle && tools.Vehicle.GetComponent<BulldozerVehicle>()!=null,"bootstrap supplies a selectable tracked dozer");
        check(tools.Vehicle.GetComponentsInChildren<Collider>().Length>30 && tools.Vehicle.Find("Wide grading blade")!=null,
            "building-sized vehicle has track, cab and blade geometry with selectable colliders");
        var ui=bootstrap.GetComponent<WH30K.UI.NewGameMenu>();
        var buttons=ui.GetComponentsInChildren<Button>(true);
        buttons.First(b=>b.name=="OpenEarthworks").onClick.Invoke();
        check(tools.IsOpen && tools.Mode==EarthworkMode.Move,"construction deck opens earthworks control mode");
        buttons.First(b=>b.name=="EarthworkRaise").onClick.Invoke();
        check(tools.Mode==EarthworkMode.Raise,"dedicated HUD dispatches the actual raise mode");
        check(EarthworksController.ColorFor(EarthworkMode.Raise)!=EarthworksController.ColorFor(EarthworkMode.Flatten)
            && planet.TerrainMaterial.HasProperty("_EarthworkBrush"),"terrain shader and HUD support distinct brush colours");
        var core=settlement.CoreBuildings[0]; tools.Open(EarthworkMode.Demolish); tools.SelectDemolitionTarget(core);
        check(!tools.CanDemolish && !tools.RemoveTarget() && !placement.Demolish(core),"bulldoze and backend both protect core facilities");
        var home=settlement.CreateSnapshot().normal;
        check(!tools.IssueCommand(home,EarthworkMode.Flatten),"earthworks cannot undercut an existing building");
        var legacyDto=JsonUtility.FromJson<GameplayChecks.ReviewSave>(bootstrap.CaptureSaveJson());
        legacyDto.version=9;legacyDto.industry.materialVersion=1;legacyDto.industry.geologyVersion=3;
        var legacy=JsonUtility.ToJson(legacyDto);
        check(bootstrap.TryLoadSaveJson(legacy) && tools.HasVehicle && tools.EditCount==0,"schema-9 migration adds a dozer without edits or building changes");
        planet=bootstrap.ActivePlanet;
        // Locate a legal dry fixture area through the same command validation as clicks.
        var n=Vector3.zero;
        for(var i=0;i<4096;i++)
        {
            var y=1-2*(i+.5f)/4096; var a=i*2.399963f;
            var candidate=new Vector3(Mathf.Sqrt(1-y*y)*Mathf.Cos(a),y,Mathf.Sqrt(1-y*y)*Mathf.Sin(a));
            if(tools.IssueCommand(candidate,EarthworkMode.Move) && tools.IssueCommand(candidate,EarthworkMode.Raise,24,4)){n=candidate;break;}
        }
        check(n.sqrMagnitude>.99f,"click command validation finds dry, clear engineering ground");
        var facing=Vector3.Cross(n,Mathf.Abs(n.y)>.95f?Vector3.right:Vector3.up).normalized;
        tools.Configure(planet,home,new EarthworksSnapshot{hasVehicle=true,x=n.x,y=n.y,z=n.z,forwardX=facing.x,forwardY=facing.y,forwardZ=facing.z});
        var before=planet.EvaluateSurfacePoint(n).magnitude; var revision=planet.SurfaceRevision;
        check(tools.IssueCommand(n,EarthworkMode.Raise,24,4),"raise command accepted at selected point");
        tools.Advance(0);check(tools.EditCount==0 && tools.Travelling,"zero simulation time pauses the complete command");
        tools.Advance(2);check(tools.EditCount==0 && tools.Capture().work==2,"grade does not change before work completes");
        var pending=bootstrap.CaptureSaveJson();check(bootstrap.TryLoadSaveJson(pending),"pending terrain command loads through full validation");
        planet=bootstrap.ActivePlanet;
        check(tools.Capture().work==2 && tools.Travelling,"exact pending work and destination survive loading");
        tools.Advance(2);
        check(tools.EditCount==1 && !tools.Travelling && Mathf.Abs(planet.EvaluateSurfacePoint(n).magnitude-before-4)<.01f,
            "completed dozer work raises the shared rendered and picked surface once");
        check(planet.SurfaceRevision>revision && tools.IsWorked(n),"work invalidates terrain and ecology geometry");
        check(tools.IssueCommand(n,EarthworkMode.Lower,24,4),"lower command uses the same validated point");tools.Advance(4);
        check(Mathf.Abs(planet.EvaluateSurfacePoint(n).magnitude-before)<.01f,"lower removes the prior four-metre grade increase");
        check(tools.IssueCommand(n,EarthworkMode.Flatten,24),"flatten command captures current centre grade");tools.Advance(4);
        var near=(n+facing*6/planet.Radius).normalized;
        check(Mathf.Abs(planet.EvaluateSurfacePoint(near).magnitude-planet.EvaluateSurfacePoint(n).magnitude)<.02f,
            "flatten levels multiple actual height samples about the point");
        var completed=bootstrap.CaptureSaveJson();check(bootstrap.TryLoadSaveJson(completed) && tools.EditCount==3,
            "ordered edits replay on the planet after full save/load");
        planet=bootstrap.ActivePlanet;
        check(Mathf.Abs(planet.EvaluateSurfacePoint(n).magnitude-before)<.02f,"load preserves exact worked grade without applying edits twice");
        var corrupt=Regex.Replace(completed,"\"forwardX\"\\s*:\\s*[-+0-9.eE]+","\"forwardX\": 5");
        check(!bootstrap.TryLoadSaveJson(corrupt) && tools.EditCount==3,"corrupt vehicle state is rejected without replacing the colony");
        // Short clear movement on the spherical surface, tested independently from terrain work.
        var move=(n+facing*10/planet.Radius).normalized;
        check(tools.IssueCommand(move,EarthworkMode.Move),"selected vehicle accepts a point movement command");
        var oldPosition=tools.Vehicle.position;tools.Advance(.5f);
        check(Vector3.Distance(oldPosition,tools.Vehicle.position)>1 && tools.Travelling,"vehicle advances visibly instead of teleporting");
        var moving=bootstrap.CaptureSaveJson();check(bootstrap.TryLoadSaveJson(moving) && tools.Travelling,"in-flight ground movement survives a save/load");
        tools.Stop();var stopped=tools.Vehicle.position;tools.Advance(10);
        check(Vector3.Distance(stopped,tools.Vehicle.position)<.01f,"stop cancels travel and pending work");
        tools.Close();
        // Exercise demolition via the tool and real confirmation button with inventory remapping.
        bootstrap.GetComponent<ResourceSystem>().ModifyStockpile(10000);
        var built=0;var anchor=settlement.CreateSnapshot().normal;
        var side=Vector3.Cross(anchor,Vector3.up).normalized;
        foreach(var id in new[]{"Factory","Utility"})
        {
            placement.SelectStructure(id);
            for(var i=1;i<100;i++)if(placement.TryPlaceDirection((anchor+side*(150+built*250+i*10)/bootstrap.ActivePlanet.Radius).normalized)){built++;break;}
        }
        check(built==2,"temporary facilities are constructed for bulldoze accounting");
        var savedIndustry=industry.Capture();savedIndustry.facilities[0].quantities[0]=3;savedIndustry.facilities[1].quantities[0]=7;
        savedIndustry.cargo=new[]{new CargoStock{source=0,target=1,kind=0,amount=2,progress=.25f},
            new CargoStock{source=1,target=-1,kind=6,amount=1,progress=.25f}};
        industry.Configure(bootstrap.ActivePlanet,anchor,savedIndustry);
        var credits=bootstrap.GetComponent<ResourceSystem>().Stockpile;
        var jobs=settlement.AllBuildings.Sum(b=>b.Economy.jobs);var removedJobs=placement.Buildings[0].Economy.jobs;
        tools.Open(EarthworkMode.Demolish);tools.SelectDemolitionTarget(placement.Buildings[0]);
        buttons.First(b=>b.name=="ConfirmBulldoze").onClick.Invoke();
        check(placement.Buildings.Length==1 && placement.Buildings[0].Id=="Utility" && industry.Capture().facilities[0].quantities[0]==7,
            "HUD demolition removes target stock and preserves the next facility's inventory");
        check(bootstrap.GetComponent<ResourceSystem>().Stockpile==credits,"bulldozing grants no duplicate construction refund");
        var remapped=industry.Capture();
        check(remapped.cargo.Length==1 && remapped.cargo[0].source==0 && remapped.cargo[0].target==-1,
            "demolition discards linked cargo and remaps unaffected deliveries immediately");
        check(Mathf.Abs(settlement.AllBuildings.Sum(b=>b.Economy.jobs)-jobs+removedJobs)<.01f,
            "demolition removes the target's actual economic contribution exactly once");
        check(bootstrap.TryLoadSaveJson(bootstrap.CaptureSaveJson()) && placement.Buildings.Length==1,"removed structures stay removed after saving and loading");
    }
}
