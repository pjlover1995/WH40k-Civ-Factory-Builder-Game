using System;
using System.IO;
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

public static class ManufactorumSceneChecks
{
    private const string Key="WH30K.ManufactorumBatch";
    public static void RunBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
        SessionState.SetBool(Key,true); EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] private static void Resume()
    {
        EditorApplication.playModeStateChanged+=s=> {
            if(s!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key,false))return;
            SessionState.EraseBool(Key);EditorApplication.delayCall+=()=> {
                try {Run();SceneCheckExit.Exit(0);}catch(Exception e){Debug.LogException(e);SceneCheckExit.Exit(1);}
            };
        };
    }
    [MenuItem("WH30K/Run Manufactorum Checks (Play Mode)")]
    public static void Run()
    {
        var bootstrap=UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        var original=GameSettings.HasActiveGame && !bootstrap.ChoosingLanding?bootstrap.CaptureSaveJson():null;
        var terrain=bootstrap.NewWorldTerrain;var count=0;
        void Check(bool ok,string text){if(!ok)throw new Exception("Manufactorum FAIL: "+text);count++;Debug.Log("Manufactorum PASS: "+text);}
        try
        {
            FacilitySceneChecks.RunGameplay(bootstrap,Check);
            bootstrap.ConfigureNewWorldTerrain(new WH30K.Planet.TerrainSettings());
            bootstrap.BeginNewGame(1234,GameSettings.Difficulty.Standard,legacyCore:true);
            GameplayChecks.FundConstruction(bootstrap);
            var placement=bootstrap.GetComponent<BuildingPlacementController>();
            var industry=bootstrap.GetComponent<IndustrySystem>();var menu=bootstrap.GetComponent<NewGameMenu>();
            var colony=bootstrap.GetComponent<Settlement>();var normal=colony.CreateSnapshot().normal;
            var tangent=Vector3.Cross(normal,Mathf.Abs(normal.y)>.95f?Vector3.right:Vector3.up).normalized;
            var second=Vector3.Cross(normal,tangent);ManagedBuilding factory=null;
            for(var i=0;i<100;i++)
            {
                placement.SelectStructure("Manufactorum");var angle=i*2.39996323f;
                var direction=(normal+(tangent*Mathf.Cos(angle)+second*Mathf.Sin(angle))*(450+i*15)/bootstrap.ActivePlanet.Radius).normalized;
                if(!placement.TryPlaceDirection(direction))continue;
                factory=placement.Buildings.Last();
                if(ManufactorumRules.Candidate(factory.State.manufactorum,factory.Industrial.SiteClear)!=null)break;
                placement.Demolish(factory);factory=null;
            }
            Check(factory!=null,"actual planetary terrain supports a clear connected warehouse site");
            var firstSite=ManufactorumRules.Candidate(factory.State.manufactorum,factory.Industrial.SiteClear);
            var blockerPoint=factory.transform.TransformPoint(new Vector3(firstSite.x*2.1f,0,firstSite.z*1.7f));
            placement.SelectStructure("StorageDepot");
            Check(placement.TryPlaceDirection(bootstrap.ActivePlanet.transform.InverseTransformPoint(blockerPoint).normalized),"an existing depot occupies the preferred industrial frontier");
            Check(!factory.Industrial.SiteClear(firstSite),"warehouse clearance rejects the real existing depot footprint");
            var before=industry.ConstructionSupplyTotals();var jobs=factory.Economy.jobs;
            menu.OpenFacilityPanel(factory);
            Button UI(string n)=>menu.GetComponentsInChildren<Button>(true).First(b=>b.name==n);
            UI("ExpandManufactorum").onClick.Invoke();
            var data=factory.State.manufactorum;
            Check(data.project!=null && data.bays.Count==0 && factory.Economy.jobs==jobs,"real expansion button reserves a project without granting unfinished capacity");
            var after=industry.ConstructionSupplyTotals();
            Check(Mathf.Abs(before.basic-after.basic-20)<.001f && Mathf.Abs(before.advanced-after.advanced-2)<.001f,"warehouse approval spends exactly 20 components and 2 plasteel");
            Check(!ManufactorumRules.Same(data.project,firstSite),"player expansion chooses another connected direction around an existing building");
            var reserved=data.project;var point=factory.transform.TransformPoint(new Vector3(reserved.x*2.1f,0,reserved.z*1.7f));
            placement.SelectStructure("StorageDepot");
            Check(!placement.TryPlaceDirection(bootstrap.ActivePlanet.transform.InverseTransformPoint(point).normalized),"new buildings cannot occupy an unfinished warehouse reservation");
            factory.ToggleOperation();factory.SimulateImprovement(30,1);
            Check(data.work==0,"suspension freezes an approved warehouse project");factory.ToggleOperation();
            factory.SimulateImprovement(23,1);var cargoBeforeLoad=industry.ConstructionSupplyTotals();var json=bootstrap.CaptureSaveJson();
            Check(bootstrap.TryLoadSaveJson(json),"actual JsonUtility save reload accepts the paid warehouse project");
            factory=placement.Buildings.First(b=>b.Id=="Manufactorum");data=factory.State.manufactorum;
            Check(data.work==23 && data.project!=null && data.bays.Count==0 && industry.ConstructionSupplyTotals()==cargoBeforeLoad,"reload retains exact construction work and reservation without refilling cargo");
            factory.SimulateImprovement(37,1);
            Check(data.bays.Count==1 && data.project==null && factory.State.level==0 && factory.Economy.jobs==jobs*2,"warehouse completion doubles real staffing capacity independently of technology tier");
            Check(factory.GetComponentsInChildren<MeshFilter>().Any(m=>m.sharedMesh.name.Contains("heavy industrial warehouse")),"scene builds a full-scale shared warehouse mesh and machinery");
            Check(!industry.SetProduct(factory,1,2),"expanded Tier I warehouse cannot select advanced plasteel technology");
            var capacity=ManufactorumRules.Scale(data);
            menu.OpenFacilityPanel(factory);before=industry.ConstructionSupplyTotals();UI("TierManufactorum").onClick.Invoke();after=industry.ConstructionSupplyTotals();
            Check(factory.State.level==1 && capacity==ManufactorumRules.Scale(data) && Mathf.Abs(before.basic-after.basic-12)<.001f && Mathf.Abs(before.advanced-after.advanced-6)<.001f,"real Tier II control spends advanced materials without expanding capacity");
            Check(industry.SetProduct(factory,1,2),"material-funded Tier II unlocks actual plasteel recipe selection");
            ShipSceneChecks.CaptureReview(bootstrap,factory,"manufactorum-controls");
            var stats=menu.GetComponentsInChildren<Text>(true).First(t=>t.name=="FacilityStatistics");
            Check(stats.preferredHeight<=stats.rectTransform.rect.height+1,"expanded factory report fits its popup");
            menu.CloseFacilityPanel();
            VerifyProduction(bootstrap,factory,Check);
            menu.OpenFacilityPanel(factory);UI("TierManufactorum").onClick.Invoke();
            UI("ExpandManufactorum").onClick.Invoke();
            Check(factory.State.level==2 && data.project!=null,"Tier III still permits another player-approved warehouse expansion");
            menu.CloseFacilityPanel();factory.SimulateImprovement(60,1);
            Check(data.bays.Count==2 && ManufactorumRules.Scale(data)==3 && factory.Economy.jobs==jobs*3,"repeated scene expansion adds another full warehouse without a tier cap");
            menu.OpenFacilityPanel(factory);ShipSceneChecks.CaptureReview(bootstrap,factory,"manufactorum-controls");menu.CloseFacilityPanel();
            Capture(factory);
            Check(bootstrap.TryLoadSaveJson(bootstrap.CaptureSaveJson()),"completed warehouse and technology tier survive full save reload");
            factory=placement.Buildings.First(b=>b.Id=="Manufactorum");
            var legacy=factory.State;legacy.manufactorum=null;legacy.level=1;legacy.improving=true;legacy.improvementWork=20;
            factory.Restore(legacy);var inherited=factory.State.manufactorum.inheritedCapacity;
            factory.SimulateImprovement(100,1);
            Check(factory.State.level==2 && !factory.State.improving && factory.State.manufactorum.inheritedCapacity==inherited+.25f,"historical paid upgrade completes its original tier and capacity exactly once");
            factory.SimulateImprovement(1000,1);
            Check(!factory.State.improving && factory.State.manufactorum.bays.Count==0,"historical automatic milestones never create unapproved warehouse expansion");
            Debug.Log($"Manufactorum: {count} actual scene checks passed in Unity {Application.unityVersion}.");
        }
        finally {bootstrap.ConfigureNewWorldTerrain(terrain);if(original!=null)bootstrap.TryLoadSaveJson(original);}
    }
    private static void VerifyProduction(PlanetBootstrap bootstrap,ManagedBuilding factory,Action<bool,string> check)
    {
        var placement=bootstrap.GetComponent<BuildingPlacementController>();var industry=bootstrap.GetComponent<IndustrySystem>();
        var colony=bootstrap.GetComponent<Settlement>();var resources=bootstrap.GetComponent<ResourceSystem>();
        ManagedBuilding generator=null;
        for(var i=0;i<30;i++)
        {
            var angle=i*2.39996323f;var point=factory.transform.position+(factory.transform.right*Mathf.Cos(angle)+factory.transform.forward*Mathf.Sin(angle))*600;
            placement.SelectStructure("PromethiumGenerator");
            if(!placement.TryPlaceDirection(bootstrap.ActivePlanet.transform.InverseTransformPoint(point).normalized))continue;
            generator=placement.Buildings.Last();break;
        }
        check(generator!=null,"warehouse output fixture has a real nearby fuelled generator");
        foreach(var core in colony.CoreBuildings)if(!core.State.offline)core.ToggleOperation();
        var workerState=resources.CreateSnapshot();workerState.Workforce=10000;resources.LoadFromSnapshot(workerState,GameSettings.GetDefinition(GameSettings.Difficulty.Standard));
        var fi=Array.IndexOf(placement.Buildings,factory);var gi=Array.IndexOf(placement.Buildings,generator);
        var saved=industry.Capture();saved.cargo=Array.Empty<CargoStock>();
        saved.facilities[fi].configuration=new FacilityConfiguration();saved.facilities[fi].quantities=new float[IndustryRules.MaterialCount];
        saved.facilities[fi].quantities[0]=100;saved.facilities[fi].quantities[2]=100;saved.facilities[fi].quantities[3]=100;
        saved.facilities[gi].quantities[(int)MaterialKind.Promethium]=100;
        industry.Configure(bootstrap.ActivePlanet,colony.CreateSnapshot().normal,saved);
        var produced=industry.Capture().producedTotals[6];var consumed=industry.Capture().consumedTotals[6];
        industry.Simulate(5);var after=industry.Capture();
        check(Mathf.Abs(industry.OperatingFraction(factory)-2)<.001f && after.producedTotals[6]-produced==2,"adequately staffed and powered warehouse doubles real recipe output");
        check(Mathf.Abs(after.consumedTotals[6]-consumed-5f/150)<.001f,"expanded industry spends physical components for its real operating upkeep");
        check(industry.PowerDemand>=16,"warehouse power demand includes doubled 14 MW factory load and generator auxiliary load");
        var visual=factory.GetComponent<ManufactorumVisual>();var gantry=factory.GetComponentsInChildren<Transform>().First(t=>t.name=="Travelling gantry crane");
        var position=gantry.localPosition;visual.SimulateAnimation(0);check(gantry.localPosition==position,"paused animation step leaves machinery stationary");
        visual.SimulateAnimation(1);check(gantry.localPosition!=position,"powered industrial animation moves the actual gantry transform");
        position=gantry.localPosition;factory.ToggleOperation();visual.SimulateAnimation(1);
        check(gantry.localPosition==position,"suspension freezes actual industrial machinery");factory.ToggleOperation();
        saved=industry.Capture();saved.cargo=Array.Empty<CargoStock>();saved.expeditionCargo[6]=0;
        foreach(var stock in saved.facilities)stock.quantities[6]=0;
        saved.facilities[fi].configuration=new FacilityConfiguration();saved.facilities[fi].configuration.products[0]=1;
        industry.Configure(bootstrap.ActivePlanet,colony.CreateSnapshot().normal,saved);produced=industry.Capture().producedTotals[5];industry.Simulate(5);
        check(industry.Capture().producedTotals[5]-produced==1,"without component upkeep only founding capacity manufactures despite an expanded building");
        GameplayChecks.FundConstruction(bootstrap);
    }
    private static void Capture(ManagedBuilding factory)
    {
        Directory.CreateDirectory(".utmp/manufactorum-review");
        foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(1920,1080)})
        {
            var utility=new PreviewRenderUtility();var root=new GameObject("Warehouse visual review");
            try
            {
                root.transform.localScale=factory.transform.lossyScale;
                foreach(var source in factory.GetComponentsInChildren<MeshFilter>())
                {
                    var go=new GameObject(source.name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform,false);
                    go.transform.localPosition=factory.transform.InverseTransformPoint(source.transform.position);
                    go.transform.localRotation=Quaternion.Inverse(factory.transform.rotation)*source.transform.rotation;
                    var scale=source.transform.lossyScale;var basis=factory.transform.lossyScale;
                    go.transform.localScale=new Vector3(scale.x/basis.x,scale.y/basis.y,scale.z/basis.z);
                    go.GetComponent<MeshFilter>().sharedMesh=source.sharedMesh;go.GetComponent<MeshRenderer>().sharedMaterials=source.GetComponent<MeshRenderer>().sharedMaterials;
                }
                utility.AddSingleGO(root);var bounds=new Bounds(Vector3.zero,Vector3.one);
                foreach(var r in root.GetComponentsInChildren<Renderer>())bounds.Encapsulate(r.bounds);
                utility.ambientColor=new Color(.3f,.32f,.36f);utility.lights[0].intensity=1.4f;utility.lights[0].transform.rotation=Quaternion.Euler(40,-30,0);
                utility.lights[1].intensity=.5f;utility.lights[1].transform.rotation=Quaternion.Euler(25,140,0);
                var radius=bounds.extents.magnitude;utility.camera.transform.position=bounds.center+new Vector3(1,1.1f,-1.8f).normalized*radius*3;
                utility.camera.transform.LookAt(bounds.center);utility.camera.nearClipPlane=.1f;utility.camera.farClipPlane=radius*10;
                utility.camera.orthographic=true;utility.camera.orthographicSize=radius*.9f;
                utility.camera.backgroundColor=new Color(.09f,.11f,.14f);utility.camera.clearFlags=CameraClearFlags.SolidColor;
                utility.BeginStaticPreview(new Rect(0,0,size.x,size.y));utility.Render(true);var image=utility.EndStaticPreview();
                File.WriteAllBytes($".utmp/manufactorum-review/warehouse-{size.x}x{size.y}.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
            }
            finally {utility.Cleanup();if(root!=null)UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
