#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Planet;
using WH30K.Sim;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;
using WH30K.UI;

public static class FarmEstateSceneChecks
{
    [MenuItem("WH30K/Run Farm Estate Checks (Play Mode)")]
    public static void Run()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Enter Play Mode first.");
        var bootstrap=UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        var original=GameSettings.HasActiveGame&&!bootstrap.ChoosingLanding?bootstrap.CaptureSaveJson():null;
        var terrain=bootstrap.NewWorldTerrain;var count=0;
        try {RunGameplay(bootstrap,(ok,label)=>{if(!ok)throw new InvalidOperationException("Farm estate FAIL: "+label);count++;Debug.Log("Farm estate PASS: "+label);});Debug.Log($"Farm estate: {count} actual scene checks passed.");}
        finally {bootstrap.ConfigureNewWorldTerrain(terrain);if(original!=null && !bootstrap.TryLoadSaveJson(original))Debug.LogError("Could not restore estate-check colony backup.");}
    }
    private static void NewWorld(PlanetBootstrap bootstrap)
    {bootstrap.ConfigureNewWorldTerrain(new TerrainSettings());bootstrap.BeginNewGame(1234,GameSettings.Difficulty.Standard,legacyCore:true);GameplayChecks.FundConstruction(bootstrap);}
    private static ManagedBuilding PlaceFirstFarm(PlanetBootstrap bootstrap)
    {
        var placement=bootstrap.GetComponent<BuildingPlacementController>();var normal=bootstrap.GetComponent<Settlement>().CreateSnapshot().normal;
        var tangent=Vector3.Cross(normal,Mathf.Abs(normal.y)>.95f?Vector3.right:Vector3.up).normalized;var forward=Vector3.Cross(normal,tangent);
        placement.SelectStructure("Farm");
        for(var i=0;i<180;i++)
        {
            var angle=i*2.39996323f;var n=(normal+(tangent*Mathf.Cos(angle)+forward*Mathf.Sin(angle))*(280+i*2)/bootstrap.ActivePlanet.Radius).normalized;
            if(placement.TryPlaceDirection(n))return placement.Buildings.Last();
        }
        return null;
    }
    private static ManagedBuilding PlaceAt(PlanetBootstrap bootstrap,string id,ManagedBuilding basis,float x,float z)
    {
        var placement=bootstrap.GetComponent<BuildingPlacementController>();placement.SelectStructure(id);
        var point=basis.transform.position+basis.transform.right*x+basis.transform.forward*z;
        var direction=bootstrap.ActivePlanet.transform.InverseTransformPoint(point).normalized;
        return placement.TryPlaceDirection(direction)?placement.Buildings.Last():null;
    }
    public static void RunGameplay(PlanetBootstrap bootstrap,Action<bool,string> check)
    {
        NewWorld(bootstrap);
        var placement=bootstrap.GetComponent<BuildingPlacementController>();var industry=bootstrap.GetComponent<IndustrySystem>();
        var settlement=bootstrap.GetComponent<Settlement>();var menu=bootstrap.GetComponent<NewGameMenu>();var resources=bootstrap.GetComponent<ResourceSystem>();
        T UI<T>(string name)where T:Component=>menu.GetComponentsInChildren<T>(true).First(t=>t.name==name);
        var farm=PlaceFirstFarm(bootstrap);check(farm!=null,"real construction places a farm for land-ownership review");
        var parcel=FarmLayoutRules.Parcel(farm.FarmLayoutSeed,10);var center=FarmLayoutRules.Center(parcel);ManagedBuilding pump=null;
        foreach(var offset in new[]{Vector2.zero,new Vector2(3,0),new Vector2(-3,0),new Vector2(0,3),new Vector2(0,-3)})
        {pump=PlaceAt(bootstrap,"GroundwaterPump",farm,center.x*farm.transform.lossyScale.x+offset.x,center.z*farm.transform.lossyScale.z+offset.y);if(pump!=null)break;}
        check(pump!=null,"a normal facility can occupy a future unclaimed field site");
        check(!settlement.PlanFarmParcel(farm,parcel,out _),"the occupied future parcel is rejected by the shared territory planner");
        var unchanged=farm.State.farm.addedFields;var advanced=0;
        while(advanced<12 && farm.DevAddFarmField())advanced++;
        check(advanced>=0 && advanced<12 && farm.State.farm.addedFields==unchanged+advanced,
            "the developer action skips time but stops before another facility's occupied territory");
        var blocked=farm.State.farm.addedFields;
        check(!farm.DevAddFarmField() && farm.State.farm.addedFields==blocked,"repeated blocked expansion cannot add output or consume another facility's land");
        var used=FarmRules.Parcel(farm.State.farm,farm.FarmLayoutSeed,0);var usedCenter=FarmLayoutRules.Center(used);
        check(PlaceAt(bootstrap,"GroundwaterPump",farm,usedCenter.x*farm.transform.lossyScale.x,usedCenter.z*farm.transform.lossyScale.z)==null,
            "later construction respects the farm's claimed field footprint");
        ManagedBuilding factory=null;var rejects=false;
        foreach(var offset in new[]{new Vector2(48,-38),new Vector2(48,38),new Vector2(-48,38),new Vector2(-48,-38)})
        {
            factory=PlaceAt(bootstrap,"Factory",farm,offset.x,offset.y);if(factory==null)continue;
            var state=factory.State;state.improving=true;state.growthPlan=2;factory.Restore(state);
            rejects=!settlement.CanExpand(factory);if(rejects)break;placement.Demolish(factory);
        }
        check(rejects,"a different facility's adaptive expansion cannot invade completed farm territory");

        NewWorld(bootstrap);farm=PlaceFirstFarm(bootstrap);
        ManagedBuilding neighbour=null;
        foreach(var distance in new[]{92f,96f,88f,100f}){neighbour=PlaceAt(bootstrap,"Farm",farm,distance,0);if(neighbour!=null)break;}
        check(neighbour!=null && farm.DisplayName=="Farm 1" && neighbour.DisplayName=="Farm 2","same-type buildings receive distinct numbered default names");
        bootstrap.GetComponent<BuildingSelection>().Select(farm);
        UI<InputField>("BuildingName").text="Farm 2";UI<InputField>("BuildingName").onEndEdit.Invoke("Farm 2");
        check(farm.DisplayName=="Farm 1","the actual editable name field rejects duplicate facility names");
        UI<InputField>("BuildingName").text="Harvest Estate";UI<InputField>("BuildingName").onEndEdit.Invoke("Harvest Estate");
        check(farm.DisplayName=="Harvest Estate","editing the inspector name changes the actual saved building identity");
        var initial=industry.Capture();initial.facilities[0].quantities[6]=20;initial.facilities[1].quantities[6]=30;
        industry.Configure(bootstrap.ActivePlanet,settlement.CreateSnapshot().normal,initial);
        var countBefore=placement.Buildings.Length;var foodBefore=resources.Food;
        for(var step=0;step<35 && industry.FarmSites(farm).Count()==1;step++)if(!farm.DevAddFarmField())break;
        industry.Simulate(0);
        Debug.Log($"Farm merge review: fields {farm.State.farm.addedFields}, members {industry.FarmSites(farm).Count()}, boundary contact {settlement.FarmSitesTouch(farm,neighbour)}, next plan {settlement.PlanFarmParcel(farm,FarmLayoutRules.Parcel(farm.FarmLayoutSeed,FarmRules.Fields(farm.State.farm.addedFields)),out _)}");
        check(industry.FarmSites(farm).Count()==2 && neighbour.EntityRoot==farm && placement.Buildings.Length==countBefore,
            "touching compatible sites become one farm entity without deleting their placed fields or facilities");
        check(industry.Capture().facilities.Sum(s=>s.quantities[6])==50 && resources.Food==foodBefore,
            "merging preserves physical inventory and cannot duplicate colony food");
        var jobs=placement.Buildings.Where(b=>b.Id=="Farm").Sum(b=>b.Economy.jobs);
        check(Mathf.Abs(jobs-16*(2+placement.Buildings.Where(b=>b.Id=="Farm").Sum(b=>b.State.farm.addedFields)*.2f))<.001f,
            "merged farm sites contribute their earned field economy exactly once");
        bootstrap.GetComponent<BuildingSelection>().Select(neighbour);
        check(menu.InspectedBuilding==farm && neighbour.DisplayName=="Harvest Estate","clicking either member selects the one named estate inspector");
        farm.SetFocus(2);check(industry.FarmSites(farm).All(s=>s.State.focus==2),"one entity's fertilizer control applies to all its member sites");
        farm.ToggleOperation();check(industry.FarmSites(farm).All(s=>s.State.offline),"one suspension command pauses the complete farm entity");farm.ToggleOperation();
        var farmId=farm.State.instanceId;var neighbourId=neighbour.State.instanceId;var fieldCount=placement.Buildings.Sum(b=>FarmRules.Fields(b.State.farm.addedFields));
        var json=bootstrap.CaptureSaveJson();check(bootstrap.TryLoadSaveJson(json),"a named, merged farm restores through the full save loader");
        farm=placement.Buildings.First(b=>b.State.instanceId==farmId);neighbour=placement.Buildings.First(b=>b.State.instanceId==neighbourId);industry.Simulate(0);
        check(farm.DisplayName=="Harvest Estate" && neighbour.EntityRoot==farm && placement.Buildings.Sum(b=>FarmRules.Fields(b.State.farm.addedFields))==fieldCount,
            "reload preserves the entity name, merged membership and every field claim");
        var malformed=JsonUtility.FromJson<GameplayChecks.ReviewSave>(json);malformed.structures[1].operation.instanceName="Harvest Estate";
        var planet=bootstrap.ActivePlanet;check(!bootstrap.TryLoadSaveJson(JsonUtility.ToJson(malformed)) && bootstrap.ActivePlanet==planet,
            "duplicate saved names reject atomically before replacing an active colony");
        check(placement.Demolish(neighbour) && placement.Buildings.Length==0,"demolishing the unified farm removes all member sites and remaps cargo once");

        NewWorld(bootstrap);farm=PlaceFirstFarm(bootstrap);neighbour=PlaceAt(bootstrap,"Farm",farm,92,0);
        check(neighbour!=null,"a second farm is available for different-technology boundary review");
        neighbour.State.farm.technologyLevel=1;settlement.NotifyResidentialGeometry();industry.Simulate(0);
        for(var step=0;step<20;step++)if(!farm.DevAddFarmField())break;
        check(industry.FarmSites(farm).Count()==1 && industry.FarmSites(neighbour).Count()==1 && neighbour.EntityRoot==neighbour,
            "farms at different technologies remain separate and cannot grow over each other's land");

        NewWorld(bootstrap);farm=PlaceFirstFarm(bootstrap);industry.Simulate(0);
        var stock=industry.Capture();stock.facilities[0].quantities[6]=100;stock.facilities[0].quantities[9]=100;
        industry.Configure(bootstrap.ActivePlanet,settlement.CreateSnapshot().normal,stock);
        var reserve=resources.Food;var population=settlement.Population;industry.Simulate(1);
        check(industry.FoodTransportCount>0 && Mathf.Abs(resources.Food-(reserve-population/150))<.001f,
            "food is removed from the farm into a real transport and is not credited before arrival");
        var hauler=bootstrap.GetComponentsInChildren<Transform>().First(t=>t.name.StartsWith("Food transport /"));var start=hauler.position;
        check(hauler.Find("Food crate")!=null && hauler.Find("Food crate").gameObject.activeSelf,"the animated food hauler visibly carries a food load");
        industry.Simulate(1);check(Vector3.Distance(start,hauler.position)>1,"food transport advances visibly toward the colony main building");
        var pending=industry.Capture().cargo.Where(c=>c.food).ToArray();json=bootstrap.CaptureSaveJson();
        check(bootstrap.TryLoadSaveJson(json) && industry.Capture().cargo.Where(c=>c.food).Select(c=>(c.amount,c.progress)).SequenceEqual(pending.Select(c=>(c.amount,c.progress))),
            "food amounts and exact travel progress survive saving while shipments are airborne");
        var beforePause=industry.Capture().cargo.Where(c=>c.food).Select(c=>c.progress).ToArray();industry.Simulate(0);
        check(industry.Capture().cargo.Where(c=>c.food).Select(c=>c.progress).SequenceEqual(beforePause),"paused simulation freezes food animation and delivery");
        industry.Simulate(10);
        var after=industry.Capture();var allFood=resources.Food+after.facilities.Sum(s=>s.food)+after.cargo.Where(c=>c.food).Sum(c=>c.amount);
        var operatingFraction=industry.OperatingFraction(placement.Buildings[0]);
        check(Mathf.Abs(allFood-(reserve+12*(FarmRules.BaseFoodPerMonth*operatingFraction-population)/150))<.02f,
            "produced, buffered, in-flight, delivered and eaten food reconcile without duplicated arrivals");
        check(resources.Food>reserve-12*population/150,"food reaches the colony reserve only when its hauler arrives");
        bootstrap.GetComponent<BuildingSelection>().Select(placement.Buildings[0]);CaptureTransportReview(bootstrap,"transport");
    }
    private static void CaptureTransportReview(PlanetBootstrap bootstrap,string stage)
    {
        // Include both the producing farm and the actual founding main building in the render.
        var camera=Camera.main;var orbit=camera.GetComponent<WH30K.CameraSystem.SimpleOrbitCamera>();
        var position=camera.transform.position;var rotation=camera.transform.rotation;var near=camera.nearClipPlane;var aspect=camera.aspect;var target=camera.targetTexture;var active=RenderTexture.active;
        var canvas=bootstrap.GetComponent<NewGameMenu>().GetComponentInChildren<Canvas>();var mode=canvas.renderMode;var oldCamera=canvas.worldCamera;var plane=canvas.planeDistance;
        var all=bootstrap.GetComponent<Settlement>().AllBuildings.ToArray();var center=all.Select(b=>b.transform.position).Aggregate(Vector3.zero,(a,b)=>a+b)/all.Length;
        var normal=(center-bootstrap.ActivePlanet.transform.position).normalized;var forward=all[0].transform.forward;var span=all.Max(b=>Vector3.Distance(center,b.transform.position))+90;
        var directory=System.IO.Path.Combine(".utmp","farm-estate-review");System.IO.Directory.CreateDirectory(directory);
        try
        {
            foreach(var outline in bootstrap.GetComponentsInChildren<FarmTerritoryOutline>())outline.Refresh();
            foreach(var method in new[]{"RefreshMaterials","RefreshBuilding","RefreshClock"})typeof(NewGameMenu).GetMethod(method,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(bootstrap.GetComponent<NewGameMenu>(),null);
            orbit.enabled=false;camera.nearClipPlane=.1f;camera.transform.position=center+normal*span*1.3f-forward*span*1.6f;
            camera.transform.rotation=Quaternion.LookRotation(center-camera.transform.position,normal);
            for(var i=0;i<180;i++)bootstrap.ActivePlanet.RefreshLodAtPosition(camera.transform.position);
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=2;
            foreach(var resolution in new[]{new Vector2Int(1280,800),new Vector2Int(1920,1080)})
            {
                var texture=new RenderTexture(resolution.x,resolution.y,24);texture.Create();camera.targetTexture=texture;camera.aspect=resolution.x/(float)resolution.y;
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=texture;
                var image=new Texture2D(resolution.x,resolution.y,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,resolution.x,resolution.y),0,0);image.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory,stage+"-"+resolution.x+"x"+resolution.y+".png"),image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);camera.targetTexture=null;RenderTexture.active=active;texture.Release();UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        finally{camera.targetTexture=target;camera.nearClipPlane=near;camera.aspect=aspect;RenderTexture.active=active;camera.transform.SetPositionAndRotation(position,rotation);orbit.enabled=true;canvas.renderMode=mode;canvas.worldCamera=oldCamera;canvas.planeDistance=plane;}
    }
}
#endif
