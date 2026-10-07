using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private MapGameplayReport? _gameplayReport;
    private DocumentStateId? _gameplayState;
    private MapNodePacker.NavigationGraph? _gameplayNavigation;
    private Task AnalyzeGameplay() => Work("Analyzing gameplay",async(project,token)=>
    {
        var analysis=await _services.BuildScheduler.AnalyzeAsync(MapBuildSnapshot.Capture(project),navigation:true,cancellation:token);
        var graph=analysis.CreateNavigation();
        var report=await Task.Run(()=>MapGameplayAnalysis.Analyze(project.Definition,analysis.CollisionFaces,graph,token),token);
        GuardJob(token); _gameplayReport=report;_gameplayState=_document!.CurrentStateId;_gameplayNavigation=graph;
        var validation=analysis.Validation();validation.Diagnostics.AddRange(report.Diagnostics);Problems(validation);
        if(_viewport!=null){_viewport.Navigation=graph;_viewport.InvalidateVisual();}
        ShowInspectorPage("Gameplay analysis");
    });
    private void GameplayInspector()
    {
        _inspector.Children.Clear();_inspector.Children.Add(Text("GAMEPLAY ANALYSIS"));
        AddButton(_inspector,"Analyze collision and navigation",()=>_=AnalyzeGameplay());
        if(_gameplayReport is not {} report){_inspector.Children.Add(Text("Measure authored spawns, pickups, routes and jump-pad trajectories against compiled collision. Diagnostics do not change the map."));return;}
        if(_gameplayState!=_document?.CurrentStateId)_inspector.Children.Add(Text("Results describe an earlier document state. Analyze again after edits."));
        _inspector.Children.Add(Text($"Navigation · {report.NavigationRegions} regions · {report.UnreachableRegions} without authored spawns"));
        foreach(var spawn in report.Spawns)
        {
            _inspector.Children.Add(Text($"Spawn team {spawn.Team} · enemy {Distance(spawn.NearestEnemyDistance)} · {spawn.VisibleEnemySpawns} enemy spawn sightlines\nWeapon {Distance(spawn.NearestWeaponDistance)} · route {Distance(spawn.WeaponRouteLength)} · region {spawn.NavigationRegion?.ToString()??"none"}"));
            AddButton(_inspector,"Select spawn",()=>SelectGameplayObject(spawn.Id));
        }
        foreach(var item in report.Pickups)
        {
            _inspector.Children.Add(Text($"{item.Type} · respawn {item.RespawnSeconds:0.0}s\nNearest spawn {Distance(item.NearestSpawnDistance)} · route {Distance(item.RouteLength)}"));
            _inspector.Children.Add(new ProgressBar {Minimum=0,Maximum=60,Value=Math.Min(60,item.RespawnSeconds),Height=5});
            AddButton(_inspector,"Select pickup",()=>SelectGameplayObject(item.Id));
        }
        foreach(var jump in report.Jumps)
        {
            _inspector.Children.Add(Text($"Jump pad · {jump.Trajectory.Count} trajectory samples · "+(jump.CenterlineObstructed?"centerline obstructed":"centerline clear")+(jump.TargetBelowKillPlane?" · target below kill plane":"")));
            AddButton(_inspector,"Select jump pad",()=>SelectGameplayObject(jump.Id));
        }
        foreach(var issue in report.Diagnostics)_inspector.Children.Add(Text(issue.Severity+" · "+issue.Message));
        _inspector.Children.Add(Text("Bot route preview"));
        var start=new TextBox {Text="0",Watermark="Start node"};var end=new TextBox {Text="1",Watermark="Destination node"};
        _inspector.Children.Add(start);_inspector.Children.Add(end);
        AddButton(_inspector,"Preview bot route",()=> {try {if(_gameplayNavigation is not {} graph || _viewport==null)throw new InvalidOperationException("Generate navigation first.");var path=MapNavigationInspection.Find(graph,int.Parse(start.Text??"0",CultureInfo.InvariantCulture),int.Parse(end.Text??"1",CultureInfo.InvariantCulture));_viewport.Navigation=graph;_viewport.NavigationPath=path;_viewport.InvalidateVisual();_status.Text=path.Length==0?"Destination is unreachable.":$"Bot route preview · {path.Length} nodes";}catch(Exception ex){Failure(ex);} });
        static string Distance(float? value)=>value is {} number?$"{number:0.0} units":"unreachable / unavailable";
    }
    private void SelectGameplayObject(Guid id)
    {if(_document==null)return;_document.Selection.Clear();_document.Selection.Add(id);_document.ActiveObjectId=id;_document.SelectionChanged();RefreshHierarchy();_viewport?.FrameSelection();}
    private void StructuralDiffInspector()
    {
        _inspector.Children.Clear();_inspector.Children.Add(Text("STRUCTURAL MAP DIFF"));
        _inspector.Children.Add(Text("Compare the open map with a saved project or immutable package. Changes are grouped by stable object and material identity."));
        AddButton(_inspector,"Choose comparison map…",()=>Browse("Compare current map with",false,path=>_=Work("Comparing maps",async(project,token)=>
        {
            var comparison=await Task.Run(()=>MapProjectSerializer.Load(path),token);
            var diff=await Task.Run(()=>MapStructuralDiff.Compare(project,comparison),token);GuardJob(token);
            var panel=new StackPanel {Spacing=8};panel.Children.Add(Text("CURRENT MAP → "+System.IO.Path.GetFileName(path)));
            if(diff.IsEmpty)panel.Children.Add(Text("No structural differences."));
            foreach(var category in diff.Changes.GroupBy(change=>change.Category))
            {panel.Children.Add(Text(category.Key+" · "+category.Count()+" changed fields"));foreach(var change in category.Take(300))panel.Children.Add(Text($"{change.Kind} · {change.Label} · {change.Field}\n{change.Before??"(absent)"} → {change.After??"(absent)"}"));if(category.Count()>300)panel.Children.Add(Text($"{category.Count()-300} additional fields. Export the full report to inspect every change."));}
            AddButton(panel,"Export complete diff…",()=>Browse("Export structural diff",true,destination=> {try {AtomicFile.Write(destination,System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(diff,new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));_status.Text="Structural diff exported.";}catch(Exception ex){Failure(ex);} },".json"));
            AddButton(panel,"Close",Dismiss);Modal(new ScrollViewer {Content=panel,MaxHeight=520});
        }),".json",".ppmap"));
    }
}
