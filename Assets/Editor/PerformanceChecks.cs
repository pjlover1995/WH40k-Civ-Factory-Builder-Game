#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using WH30K.CameraSystem;
using WH30K.Game;
using WH30K.Gameplay;
using WH30K.Gameplay.Construction;
using WH30K.Sim;
using WH30K.Sim.Events;
using WH30K.Sim.Resources;
using WH30K.Sim.Settlements;

// Repeatable editor measurements. Identical views/settings are required for comparisons.
// Frame times and allocations include the editor, so these are not standalone FPS claims.
public static class PerformanceChecks
{
    private static IEnumerator routine;
    private static PlanetBootstrap bootstrap;
    private static string prior, output;
    private static int previousFrame, priorVsync, priorLimit;
    private static readonly List<string> rows=new List<string>();
    [MenuItem("WH30K/Performance/Measure Baseline %&o")]
    public static void Baseline()=>Begin("baseline");
    [MenuItem("WH30K/Performance/Measure Optimized %&j")]
    public static void Optimized()=>Begin("optimized");
    [MenuItem("WH30K/Performance/Cancel Measurement")]
    public static void Cancel(){if(routine!=null)Finish();}
    private static void Begin(string name)
    {
        if(!Application.isPlaying || routine!=null)throw new InvalidOperationException("Enter Play Mode; allow the current measurement to finish.");
        bootstrap=UnityEngine.Object.FindObjectOfType<PlanetBootstrap>();
        var disk=Path.Combine(Application.persistentDataPath,"wh30k_vslice_save.json");
        prior=GameSettings.HasActiveGame?bootstrap.CaptureSaveJson():File.Exists(disk)?File.ReadAllText(disk):null;
        var directory=Path.Combine(Directory.GetParent(Application.dataPath).FullName,".utmp","performance");Directory.CreateDirectory(directory);
        if(prior!=null)File.WriteAllText(Path.Combine(directory,"colony-before-measurement.json"),prior);
        output=Path.Combine(directory,name+".csv");rows.Clear();
        rows.Add("scenario,frames,mean_ms,p50_ms,p95_ms,p99_ms,max_ms,mean_fps,bytes_per_frame,gen0_collections,draw_calls,triangles");
        priorVsync=QualitySettings.vSyncCount;priorLimit=Application.targetFrameRate;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
        routine=Run();previousFrame=-1;EditorApplication.update+=Step;
        Debug.Log($"WH30K performance measurement started: {name}; {SystemInfo.graphicsDeviceName}; {Screen.width}x{Screen.height}; editor DX11, VSync off.");
    }
    private static void Step()
    {
        if(!Application.isPlaying){Finish();return;}
        if(previousFrame==Time.frameCount)return;previousFrame=Time.frameCount;
        try{if(!routine.MoveNext())Finish();}catch(Exception error){Debug.LogException(error);Finish();}
    }
    private static void Finish()
    {
        EditorApplication.update-=Step;routine=null;
        QualitySettings.vSyncCount=priorVsync;Application.targetFrameRate=priorLimit;
        if(Application.isPlaying && prior!=null && !bootstrap.TryLoadSaveJson(prior))Debug.LogError("Performance test could not restore colony. Use the measurement backup.");
        prior=null;if(output!=null){File.WriteAllLines(output,rows);Debug.Log("WH30K performance report: "+output);}output=null;
    }
    private static IEnumerator Run()
    {
        bootstrap.BeginNewGame(1234,GameSettings.Difficulty.Standard);
        bootstrap.GetComponent<ColonyEventSystem>().EndSession();
        var clock=bootstrap.GetComponent<ColonyClock>();clock.TogglePause();
        var orbit=Camera.main.GetComponent<SimpleOrbitCamera>();
        var colony=bootstrap.GetComponent<Settlement>().CreateSnapshot().normal;
        var measure=Measure("orbit",orbit,false);while(measure.MoveNext())yield return null;
        orbit.FocusLocation(colony);measure=Measure("surface",orbit,false);while(measure.MoveNext())yield return null;
        measure=Measure("surface_traversal",orbit,true);while(measure.MoveNext())yield return null;
        orbit.FocusLocation(colony);
        var placement=bootstrap.GetComponent<BuildingPlacementController>();bootstrap.GetComponent<ResourceSystem>().ModifyStockpile(1000000);
        var right=Vector3.Cross(colony,Mathf.Abs(colony.y)>.95f?Vector3.right:Vector3.up).normalized;var forward=Vector3.Cross(colony,right);
        var ids=new[]{"Outpost","Mine","Smelter","Works"};
        for(var i=0;i<192;i++)
        {
            placement.SelectStructure(ids[i%4]);
            var n=(colony+(right*((i%16-7.5f)*65)+forward*((i/16-5.5f)*65))/bootstrap.ActivePlanet.Radius).normalized;
            placement.TryPlaceDirection(n);
        }
        var industry=bootstrap.GetComponent<IndustrySystem>();var saved=industry.Capture();var cargo=new List<CargoStock>();
        for(var i=0;i<placement.Buildings.Length;i++)if(placement.Buildings[i].Id=="Works")
        {saved.facilities[i].quantities[6]=60;for(var j=0;j<2&&cargo.Count<64;j++)cargo.Add(new CargoStock{source=i,target=-1,kind=6,amount=1,progress=j*.25f});}
        saved.cargo=cargo.ToArray();industry.Configure(bootstrap.ActivePlanet,colony,saved);clock.TogglePause();
        measure=Measure("industry_192",orbit,false);while(measure.MoveNext())yield return null;
        // Direct allocation check isolates the building registry from editor redraw allocations.
        var bytes=GC.GetAllocatedBytesForCurrentThread();var timer=System.Diagnostics.Stopwatch.StartNew();
        var count=0;for(var i=0;i<10000;i++)count+=placement.Buildings.Length;
        timer.Stop();Debug.Log($"Building registry 10000 reads: {timer.Elapsed.TotalMilliseconds:0.00} ms, reported bytes {GC.GetAllocatedBytesForCurrentThread()-bytes} (zero can mean unavailable on Mono), checksum {count}.");
    }
    private static IEnumerator Measure(string name,SimpleOrbitCamera orbit,bool moving)
    {
        // Keep this fixture compilable on the pre-optimization revision as well.
        var budgetProperty=bootstrap.ActivePlanet.GetType().GetProperty("PatchesCreatedLastUpdate");
        var readBudget=budgetProperty==null?null:(Func<int>)Delegate.CreateDelegate(typeof(Func<int>),bootstrap.ActivePlanet,budgetProperty.GetGetMethod());
        var warmEnd=Time.realtimeSinceStartupAsDouble+3;
        while(Time.realtimeSinceStartupAsDouble<warmEnd){if(moving)orbit.Navigate(new Vector2(1,.25f),Time.unscaledDeltaTime);yield return null;}
        var values=new List<double>(16384);var start=Time.realtimeSinceStartupAsDouble;var last=start;
        var allocated=GC.GetAllocatedBytesForCurrentThread();var collections=GC.CollectionCount(0);double calls=0,triangles=0;
        while(Time.realtimeSinceStartupAsDouble-start<8)
        {
            if(moving)orbit.Navigate(new Vector2(1,.25f),Time.unscaledDeltaTime);
            yield return null;
            if(readBudget!=null && readBudget()>8)
                throw new InvalidOperationException("Terrain subdivision exceeded its eight-patch frame budget.");
            var now=Time.realtimeSinceStartupAsDouble;values.Add((now-last)*1000);last=now;
            calls+=UnityStats.drawCalls;triangles+=UnityStats.triangles;
        }
        var totalBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;var gen0=GC.CollectionCount(0)-collections;
        values.Sort();double sum=0;foreach(var value in values)sum+=value;var mean=sum/values.Count;
        var row=string.Format(CultureInfo.InvariantCulture,"{0},{1},{2:F3},{3:F3},{4:F3},{5:F3},{6:F3},{7:F2},{8:F0},{9},{10:F0},{11:F0}",
            name,values.Count,mean,values[values.Count/2],values[(int)((values.Count-1)*.95)],values[(int)((values.Count-1)*.99)],values[values.Count-1],1000/mean,totalBytes/(double)values.Count,gen0,calls/values.Count,triangles/values.Count);
        rows.Add(row);Debug.Log("PERFORMANCE "+row);
    }
}
#endif
