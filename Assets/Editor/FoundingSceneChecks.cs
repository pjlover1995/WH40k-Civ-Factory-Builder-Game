using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Sim;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;
using WH30K.UI;

public static class FoundingSceneChecks
{
    private const string Key="WH30K.FoundingBatch";
    public static void RunBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Prototype/WH30K_VS01.unity");
        SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
    }
    [InitializeOnLoadMethod] private static void Resume()
    {
        EditorApplication.playModeStateChanged+=s=>{
            if(s!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key,false))return;
            SessionState.EraseBool(Key);EditorApplication.delayCall+=()=>{
                try{Run();SceneCheckExit.Exit(0);}catch(Exception e){Debug.LogException(e);SceneCheckExit.Exit(1);}
            };
        };
    }
    [MenuItem("WH30K/Run Founding Objective Checks (Play Mode)")]
    public static void Run()
    {
        var bootstrap=UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        var original=GameSettings.HasActiveGame && !bootstrap.ChoosingLanding?bootstrap.CaptureSaveJson():null;
        var terrain=bootstrap.NewWorldTerrain;var checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception("Founding FAIL: "+text);checks++;Debug.Log("Founding PASS: "+text);}
        try
        {
            bootstrap.ConfigureNewWorldTerrain(new WH30K.Planet.TerrainSettings());
            bootstrap.BeginNewGame(1234,GameSettings.Difficulty.Standard,legacyCore:true);
            var industry=bootstrap.GetComponent<IndustrySystem>();var menu=bootstrap.GetComponent<NewGameMenu>();
            var colony=bootstrap.GetComponent<Settlement>();var placement=bootstrap.GetComponent<BuildingPlacementController>();
            var resources=bootstrap.GetComponent<ResourceSystem>();var clock=bootstrap.GetComponent<ColonyClock>();
            var normal=colony.CreateSnapshot().normal;
            T UI<T>(string name) where T:Component=>menu.GetComponentsInChildren<T>(true).First(t=>t.name==name);
            void Click(string name)=>UI<Button>(name).onClick.Invoke();
            void Refresh()=>typeof(NewGameMenu).GetMethod("RefreshFoundingObjectives",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(menu,null);
            Check(!industry.Founding.completed && industry.RecoverFoundingCanister()==0,"fresh colony has no earned reward");
            menu.ShowGuideOffer();Click("DeclineColonyGuide");Refresh();
            Check(!industry.Founding.enabled && !UI<RectTransform>("FoundingObjectivesTracker").gameObject.activeSelf,"declining leaves optional tracker hidden");
            Click("OpenColonyGuide");Click("ToggleFoundingObjectives");Refresh();
            Check(industry.Founding.enabled && clock.DecisionPending,"guide can enable optional live objectives while preserving its pause");
            Click("CloseColonyGuide");Refresh();
            Check(!clock.DecisionPending && UI<RectTransform>("FoundingObjectivesTracker").gameObject.activeSelf,"tracker remains visible without pausing play");
            GameplayChecks.FundConstruction(bootstrap);
            var tangent=Vector3.Cross(normal,Mathf.Abs(normal.y)>.95f?Vector3.right:Vector3.up).normalized;
            var second=Vector3.Cross(normal,tangent);
            foreach(var id in new[]{"GroundwaterPump","Farm","Manufactorum"})
            {
                var placed=false;placement.SelectStructure(id);
                for(var i=0;i<100;i++)
                {
                    var a=i*2.39996323f;
                    if(!placement.TryPlaceDirection((normal+(tangent*Mathf.Cos(a)+second*Mathf.Sin(a))*(170+i*4)/bootstrap.ActivePlanet.Radius).normalized))continue;
                    placed=true;break;
                }
                Check(placed,"actual scene placement creates "+id);
            }
            Check(!industry.Founding.waterEstablished && !industry.Founding.foodDelivered && !industry.Founding.componentsMade,"construction alone completes no production milestone");
            var saved=industry.Capture();
            for(var i=0;i<placement.Buildings.Length;i++)
            {
                var q=saved.facilities[i].quantities;
                if(placement.Buildings[i].Id=="Farm"){q[6]=50;q[9]=100;}
                if(placement.Buildings[i].Id=="Manufactorum"){q[0]=100;q[2]=100;q[3]=100;}
            }
            industry.Configure(bootstrap.ActivePlanet,normal,saved);
            var report=resources.CreateSnapshot();report.Workforce=6000;resources.LoadFromSnapshot(report,GameSettings.GetDefinition(GameSettings.Difficulty.Standard));
            industry.Simulate(90);
            Check(industry.Founding.waterEstablished,"powered staffed pump completes actual water objective");
            Check(industry.Founding.foodDelivered,"real farm cargo arriving completes delivery objective");
            Check(industry.Founding.componentsMade,"real recipe production completes component objective");
            Check(industry.TrySpendConstruction(1,0) && industry.Founding.pendingComponentUse>=1,"between-tick construction spending enters the saved balance observation");
            Click("ReviewFoundingObjectives");Refresh();
            Canvas.ForceUpdateCanvases();
            Check(UI<Text>("GuideBody").preferredHeight<=UI<Text>("GuideBody").rectTransform.rect.height+1,"long balance objective text fits its card");
            ShipSceneChecks.CaptureReview(bootstrap,colony.CoreBuildings[0],"founding-balance");
            Click("CloseColonyGuide");
            var snapshot=industry.Capture();var before=industry.ConstructionSupplyTotals();
            var json=bootstrap.CaptureSaveJson();
            Check(bootstrap.TryLoadSaveJson(json),"normal JsonUtility save loader accepts objective progress");
            Check(JsonUtility.ToJson(industry.Founding)==JsonUtility.ToJson(snapshot.founding) && industry.ConstructionSupplyTotals()==before,"reload retains exact progress, tracker and cargo without refilling");
            var seconds=industry.Founding.seconds;industry.Simulate(0);
            Check(industry.Founding.seconds==seconds,"zero simulated time does not advance objectives");
            industry.Simulate(170);Refresh();
            Check(industry.Founding.completed && industry.Founding.rewardRecovered==0,"actual full production and delivery month reveals an unclaimed canister");
            Click("ReviewFoundingObjectives");Refresh();
            Check(UI<Text>("GuideBody").text.Contains("orbital cargo canister") && UI<Button>("GuideBuild").gameObject.activeSelf,"objective overview presents narrative find and recovery action");
            ShipSceneChecks.CaptureReview(bootstrap,colony.CoreBuildings[0],"founding-canister");
            Canvas.ForceUpdateCanvases();
            foreach(var name in new[]{"GuideBody","GuideTitle","GuideStatus","FoundingObjectiveTitle","FoundingObjectiveDetail"})
            {
                var text=UI<Text>(name);
                Check(text.preferredHeight<=text.rectTransform.rect.height+1,"objective text fits: "+name);
            }
            snapshot=industry.Capture();snapshot.expeditionCargo[6]=IndustrySystem.ShipStoragePerMaterial-7;
            foreach(var f in snapshot.facilities)f.quantities[6]=IndustryRules.StorageLimit;
            industry.Configure(bootstrap.ActivePlanet,normal,snapshot);before=industry.ConstructionSupplyTotals();
            using(var render=new OutputTooltipSceneChecks.BatchUiRender(menu.GetComponentInChildren<Canvas>()))
            {
                var button=UI<Button>("GuideBuild");var rect=button.GetComponent<RectTransform>();
                var pointer=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(render.Camera,rect.TransformPoint(rect.rect.center))};
                var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
                Check(hits.Count>0 && hits[0].gameObject.transform.IsChildOf(button.transform),"canister recovery button is reachable through real UGUI raycasting");
                ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
            }
            Check(Mathf.Abs(industry.Founding.rewardRecovered-7)<.001f && Mathf.Abs(industry.ConstructionSupplyTotals().basic-before.basic-7)<.001f,"recovery adds only available physical room and saves canister remainder");
            Click("GuideBuild");Check(Mathf.Abs(industry.Founding.rewardRecovered-7)<.001f,"full storage cannot duplicate or discard remainder");
            json=bootstrap.CaptureSaveJson();Check(bootstrap.TryLoadSaveJson(json) && Mathf.Abs(industry.Founding.rewardRecovered-7)<.001f,"partial reward claim survives disk-format save reload");
            Check(industry.TrySpendConstruction(13,0),"ordinary construction spending frees reward storage");
            Check(Mathf.Abs(industry.RecoverFoundingCanister()-13)<.001f && industry.RecoverFoundingCanister()==0,"remaining reward can be recovered exactly once");
            json=bootstrap.CaptureSaveJson();Check(bootstrap.TryLoadSaveJson(json) && industry.RecoverFoundingCanister()==0,"fully claimed canister cannot be replayed after load");
            var bad=Regex.Replace(json,"\"rewardRecovered\":\\s*[0-9.]+","\"rewardRecovered\":21");
            Check(bad!=json && !bootstrap.TryLoadSaveJson(bad) && industry.Founding.rewardRecovered==20,"invalid overclaimed reward rejects without replacing the active colony");
            snapshot=industry.Capture();snapshot.founding=default;industry.Configure(bootstrap.ActivePlanet,normal,snapshot);
            json=Regex.Replace(bootstrap.CaptureSaveJson(),"\"founding\":\\s*\\{[^}]*\\},?","");
            Check(bootstrap.TryLoadSaveJson(json) && !industry.Founding.completed && industry.Founding.rewardRecovered==0,"missing legacy objective state grants no free supplies or completion");
            Refresh();Click("OpenColonyGuide");ShipSceneChecks.CaptureReview(bootstrap,colony.CoreBuildings[0],"founding-start");
            Click("CloseColonyGuide");industry.SetFoundingObjectives(true);Refresh();
            ShipSceneChecks.CaptureReview(bootstrap,colony.CoreBuildings[0],"founding-tracker");
            bootstrap.BeginNewGame(5678,GameSettings.Difficulty.Standard,legacyCore:true);
            Check(!industry.Founding.completed && industry.Founding.seconds==0 && !industry.Founding.enabled,"new expedition clears prior objective and reward state");
            Debug.Log($"Founding: {checks} scene checks passed.");
        }
        finally{bootstrap.ConfigureNewWorldTerrain(terrain);if(original!=null && !bootstrap.TryLoadSaveJson(original))Debug.LogError("Could not restore original colony after founding checks.");}
    }
}
