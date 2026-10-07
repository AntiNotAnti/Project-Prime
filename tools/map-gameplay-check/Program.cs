using System.Collections.Immutable;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

MphRead.AvaloniaShared.StudioGameAssets.InitializeAuthoringProcess();
Headless.Enter();
int checks = 0;
try
{
    var map = new MapDefinition {Name="GAMEPLAY_CHECK",FormatVersion=2,MapId=Guid.NewGuid(),KillHeight=-2};
    var a = new MapSpawn {Id=Guid.NewGuid(),Position=[0f,0,0],Team=-1};
    var b = new MapSpawn {Id=Guid.NewGuid(),Position=[10f,0,0],Team=-1};
    map.Spawns.AddRange([a,b]);
    var weapon = new MapItem {Id=Guid.NewGuid(),Position=[5f,0,0],Type="Imperialist",SpawnInterval=90};
    var isolated = new MapItem {Id=Guid.NewGuid(),Position=[100f,0,0],Type="HealthSmall",SpawnInterval=300};
    map.Items.AddRange([weapon,isolated]);
    var navigation = new MapNodePacker.NavigationGraph([], [new(0,0,0),new(5,0,0),new(10,0,0),new(100,0,0)], [[1],[0,2],[1],[]], [0,0,0,1],2);
    string before = map.Serialize();
    var open = MapGameplayAnalysis.Analyze(map,[],navigation);
    Check(open.Spawns.All(row=>row.NearestEnemyDistance==10 && row.VisibleEnemySpawns==1),"neutral FFA spawns are potential rivals with measured distance and sightlines");
    Check(open.Spawns.All(row=>row.NearestWeaponDistance==5 && row.WeaponRouteLength==5),"weapon distance and compiled graph route agree on a known straight path");
    Check(open.Pickups.Single(row=>row.Id==weapon.Id).RespawnSeconds==3,"pickup interval uses canonical 30 Hz frames");
    Check(open.Pickups.Single(row=>row.Id==isolated.Id).RouteLength is null,"disconnected pickup has no invented route");
    Check(open.NavigationRegions==2 && open.UnreachableRegions==1,"only the region with authored spawns is reached");
    Check(open.Diagnostics.Any(issue=>issue.Code=="FP-STUDIO-GAMEPLAY-001") && open.Diagnostics.Any(issue=>issue.Code=="FP-STUDIO-GAMEPLAY-006" && issue.ObjectId==isolated.Id),"unreachable region and pickup diagnostics retain exact identities");
    Check(MapGameplayAnalysis.RouteLength(navigation,new(0,0,0),new(10,0,0))==10,"route measures actual graph edges");
    Check(MapGameplayAnalysis.RouteLength(navigation,new(0,0,0),new(100,0,0)) is null,"route cannot cross disconnected components");
    Check(MapGameplayAnalysis.RouteLength(navigation,new(-20,0,0),new(10,0,0)) is null,"route rejects points outside the bounded node attachment distance");
    Check(map.Serialize()==before,"analysis leaves canonical source bytes unchanged");

    var wall = new MapPreviewFace([new(5,-5,-5),new(5,5,-5),new(5,5,5),new(5,-5,5)],1,0,-1,ImmutableArray<Vector2>.Empty);
    var hidden = MapGameplayAnalysis.Analyze(map,[wall],navigation);
    Check(hidden.Spawns.All(row=>row.NearestEnemyDistance==10 && row.VisibleEnemySpawns==0),"compiled collision blocks both directions of a spawn sightline");
    Check(!hidden.Diagnostics.Any(issue=>issue.Code=="FP-STUDIO-GAMEPLAY-002"),"occluded spawns do not receive an exposed-spawn warning");
    Check(MapGameplayAnalysis.SegmentBlocked(new(0,1.2f,0),new(10,1.2f,0),[wall]),"segment triangle test detects a crossing");
    Check(!MapGameplayAnalysis.SegmentBlocked(new(0,1.2f,6),new(10,1.2f,6),[wall]),"line of sight outside the wall remains clear");
    Check(!MapGameplayAnalysis.SegmentBlocked(new(5,1.2f,0),new(10,1.2f,0),[wall]),"a segment starting on the wall does not count its own endpoint");
    Check(map.Serialize()==before,"occlusion analysis remains read-only");

    a.Team=b.Team=0;
    var allies=MapGameplayAnalysis.Analyze(map,[],navigation);
    Check(allies.Spawns.All(row=>row.NearestEnemyDistance is null && row.VisibleEnemySpawns==0),"same nonnegative team excludes ally spawn pairs");
    b.Team=1;
    Check(MapGameplayAnalysis.Analyze(map,[],navigation).Spawns.All(row=>row.NearestEnemyDistance==10),"opposing team spawns remain rivals");
    b.Team=-1;
    Check(MapGameplayAnalysis.Analyze(map,[],navigation).Spawns.All(row=>row.NearestEnemyDistance==10),"neutral versus assigned team remains a possible opponent");
    a.Position=[0f,-3,0];
    Check(MapGameplayAnalysis.Analyze(map,[],navigation).Diagnostics.Any(issue=>issue.Code=="FP-STUDIO-GAMEPLAY-003" && issue.ObjectId==a.Id),"below-plane spawn warning names the authored spawn");
    a.Position=[0f,0,0];
    var pad=new MapJumpPad {Id=Guid.NewGuid(),Position=[0f,0,0],Target=[10f,1.5f,0],ControlLockTime=30};
    map.JumpPads.Add(pad);
    string withPad=map.Serialize();
    var jump=MapGameplayAnalysis.Analyze(map,[wall],navigation).Jumps.Single();
    Check(jump.Trajectory.Count>10 && jump.Trajectory[0]==Vector3.Zero,"jump uses canonical solver and begins at its authored origin");
    Check(jump.CenterlineObstructed,"jump centerline intersects the known compiled wall");
    Check(map.Serialize()==withPad,"jump feasibility proposal leaves the source unchanged");
    using var cancelled=new CancellationTokenSource();cancelled.Cancel();
    bool cancellation=false;try{MapGameplayAnalysis.Analyze(map,[wall],navigation,cancelled.Token);}catch(OperationCanceledException){cancellation=true;}
    Check(cancellation && map.Serialize()==withPad,"cancelled analysis cannot mutate or publish the source");
    Console.WriteLine($"map-gameplay-check: {checks} assertions passed");
    return 0;
}
catch(Exception error){Console.Error.WriteLine(error);return 1;}
void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks++;}
