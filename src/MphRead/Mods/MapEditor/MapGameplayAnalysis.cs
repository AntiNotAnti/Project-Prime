using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MphRead.Formats;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapEditor;

public sealed record MapSpawnAnalysis(Guid Id, int Team, float? NearestEnemyDistance, int VisibleEnemySpawns,
    float? NearestWeaponDistance, float? WeaponRouteLength, int? NavigationRegion);
public sealed record MapPickupAnalysis(Guid Id, string Type, float RespawnSeconds, float? NearestSpawnDistance, float? RouteLength);
public sealed record MapJumpAnalysis(Guid Id, IReadOnlyList<Vector3> Trajectory, bool CenterlineObstructed, bool TargetBelowKillPlane);
public sealed record MapGameplayReport(IReadOnlyList<MapSpawnAnalysis> Spawns, IReadOnlyList<MapPickupAnalysis> Pickups,
    IReadOnlyList<MapJumpAnalysis> Jumps, IReadOnlyList<MapDiagnostic> Diagnostics, int NavigationRegions, int UnreachableRegions);

/// <summary>Read-only measurements over canonical compiled collision and navigation. No authoring state is changed.</summary>
public static class MapGameplayAnalysis
{
    private static readonly HashSet<string> Weapons=new(StringComparer.OrdinalIgnoreCase)
    {"VoltDriver","Battlehammer","Imperialist","Judicator","Magmaul","ShockCoil","OmegaCannon","AffinityWeapon"};
    public static MapGameplayReport Analyze(MapDefinition definition, IReadOnlyList<MapPreviewFace> collision,
        MapNodePacker.NavigationGraph? navigation=null, CancellationToken cancellation=default)
    {
        var diagnostics=new List<MapDiagnostic>(); var spawnRows=new List<MapSpawnAnalysis>();var pickupRows=new List<MapPickupAnalysis>();var jumpRows=new List<MapJumpAnalysis>();
        Vector3[] spawns=definition.Spawns.Select(spawn=>V(spawn.Position)).ToArray();
        int[] spawnNodes=spawns.Select(position=>NearestNode(navigation,position)).ToArray();
        HashSet<int> reached=spawnNodes.Where(node=>node>=0).Select(node=>navigation!.Components[node]).ToHashSet();
        int regions=navigation?.Components.Distinct().Count()??0;
        int unreachable=navigation?.Components.Distinct().Count(region=>!reached.Contains(region))??0;
        if(unreachable>0) diagnostics.Add(new("FP-STUDIO-GAMEPLAY-001",MapDiagnosticSeverity.Warning,$"{unreachable} navigation regions have no authored spawn. Inspect their connections before adding bot routes."));
        var weaponItems=definition.Items.Where(item=>Weapons.Contains(item.Type)).ToArray();
        for(int i=0;i<definition.Spawns.Count;i++)
        {
            cancellation.ThrowIfCancellationRequested();var spawn=definition.Spawns[i];Vector3 position=spawns[i];
            int[] enemies=Enumerable.Range(0,spawns.Length).Where(index=>index!=i &&
                (spawn.Team<0 || definition.Spawns[index].Team<0 || definition.Spawns[index].Team!=spawn.Team)).ToArray();
            float? nearest=Minimum(enemies.Select(index=>Vector3.Distance(position,spawns[index])));
            int visible=enemies.Count(index=>!SegmentBlocked(position+Vector3.UnitY*1.2f,spawns[index]+Vector3.UnitY*1.2f,collision,cancellation));
            float? weaponDistance=Minimum(weaponItems.Select(item=>Vector3.Distance(position,V(item.Position))));
            float? weaponRoute=Minimum(weaponItems.Select(item=>RouteLength(navigation,position,V(item.Position))).OfType<float>());
            int node=spawnNodes[i];
            spawnRows.Add(new(spawn.Id,spawn.Team,nearest,visible,weaponDistance,weaponRoute,node<0?null:navigation!.Components[node]));
            if(nearest<12 && visible>0)diagnostics.Add(new("FP-STUDIO-GAMEPLAY-002",MapDiagnosticSeverity.Warning,$"Spawn has {visible} enemy spawns in direct line of sight; nearest is {nearest:0.0} units away.",spawn.Id));
            if(position.Y<definition.KillHeight)diagnostics.Add(new("FP-STUDIO-GAMEPLAY-003",MapDiagnosticSeverity.Warning,"Spawn is below the authored kill plane.",spawn.Id));
            if(navigation!=null && node<0) diagnostics.Add(new("FP-STUDIO-GAMEPLAY-004",MapDiagnosticSeverity.Warning,"Spawn is more than 12 units from any navigation node.",spawn.Id));
            if(weaponItems.Length>0 && navigation!=null && weaponRoute==null)diagnostics.Add(new("FP-STUDIO-GAMEPLAY-005",MapDiagnosticSeverity.Warning,"No weapon pickup is reachable from this spawn in the compiled navigation graph.",spawn.Id));
        }
        foreach(var item in definition.Items)
        {
            cancellation.ThrowIfCancellationRequested();Vector3 position=V(item.Position);
            float? route=Minimum(spawns.Select(spawn=>RouteLength(navigation,spawn,position)).OfType<float>());
            // Authoring item intervals use the engine's original 30 Hz frame units.
            pickupRows.Add(new(item.Id,item.Type,item.SpawnInterval/30f,Minimum(spawns.Select(spawn=>Vector3.Distance(position,spawn))),route));
            if(navigation!=null && route==null)diagnostics.Add(new("FP-STUDIO-GAMEPLAY-006",MapDiagnosticSeverity.Warning,"Pickup has no route from an authored spawn in the compiled navigation graph.",item.Id));
        }
        foreach(var pad in definition.JumpPads)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var (direction,speed)=MapBuilder.SolveJumpPad(pad);Vector3 start=V(pad.Position),velocity=direction*speed;
                float gravity=-Fixed.ToFloat(Metadata.PlayerValues[(int)Hunter.Samus].BipedGravity);
                float duration=Math.Clamp(Math.Max(30,(int)pad.ControlLockTime),1,180);
                if(pad.Target is {Length:3} target)
                { Vector3 delta=V(target)-start;float horizontal=new Vector3(delta.X,0,delta.Z).Length;float rate=new Vector3(velocity.X,0,velocity.Z).Length; if(rate>1e-5f)duration=Math.Clamp(horizontal/rate,1,240); }
                var trajectory=new List<Vector3>{start};bool blocked=false;
                for(int frame=1;frame<=MathF.Ceiling(duration);frame++)
                {
                    float time=MathF.Min(frame,duration),fall=MathF.Max(0,time-pad.ControlLockTime);
                    Vector3 next=start+velocity*time-Vector3.UnitY*(.5f*gravity*fall*fall);
                    blocked|=SegmentBlocked(trajectory[^1],next,collision,cancellation);trajectory.Add(next);
                }
                bool below=pad.Target is {Length:3} landing && landing[1]<definition.KillHeight;
                jumpRows.Add(new(pad.Id,trajectory,blocked,below));
                if(blocked)diagnostics.Add(new("FP-STUDIO-GAMEPLAY-007",MapDiagnosticSeverity.Warning,"Jump pad trajectory centerline intersects compiled collision. Run the map audit for player capsule clearance.",pad.Id));
                if(below)diagnostics.Add(new("FP-STUDIO-GAMEPLAY-008",MapDiagnosticSeverity.Warning,"Jump pad target is below the authored kill plane.",pad.Id));
            }
            catch(Exception ex) when(ex is ProgramException or ArgumentException)
            {diagnostics.Add(new("FP-STUDIO-GAMEPLAY-009",MapDiagnosticSeverity.Warning,"Jump pad cannot be solved: "+ex.Message,pad.Id));}
        }
        var modes=new MapValidationResult();MapModeValidator.Validate(definition,modes);diagnostics.AddRange(modes.Diagnostics);
        return new(spawnRows,pickupRows,jumpRows,diagnostics,regions,unreachable);
    }
    private static Vector3 V(float[] value) => value.Length==3 ? new(value[0],value[1],value[2]) : throw new ArgumentException("Gameplay positions require three coordinates.");
    private static float? Minimum(IEnumerable<float> values) { float[] all=values.ToArray();return all.Length==0?null:all.Min(); }
    private static int NearestNode(MapNodePacker.NavigationGraph? graph,Vector3 point)
    {
        if(graph==null || graph.Positions.Length==0)return -1;int nearest=-1;float best=12*12;
        for(int i=0;i<graph.Positions.Length;i++){float distance=(graph.Positions[i]-point).LengthSquared;if(distance<=best){best=distance;nearest=i;}}
        return nearest;
    }
    public static float? RouteLength(MapNodePacker.NavigationGraph? graph,Vector3 from,Vector3 to)
    {
        int start=NearestNode(graph,from),end=NearestNode(graph,to);if(graph==null || start<0 || end<0)return null;
        int[] path=MapNavigationInspection.Find(graph,start,end);if(path.Length==0)return null;
        float length=Vector3.Distance(from,graph.Positions[start])+Vector3.Distance(to,graph.Positions[end]);
        for(int i=1;i<path.Length;i++)length+=Vector3.Distance(graph.Positions[path[i-1]],graph.Positions[path[i]]);return length;
    }
    public static bool SegmentBlocked(Vector3 from,Vector3 to,IReadOnlyList<MapPreviewFace> collision,CancellationToken cancellation=default)
    {
        Vector3 delta=to-from;
        foreach(var face in collision)
        {
            cancellation.ThrowIfCancellationRequested();
            for(int i=1;i+1<face.Points.Length;i++)
            {
                Vector3 a=face.Points[0],edge1=face.Points[i]-a,edge2=face.Points[i+1]-a;
                Vector3 cross=Vector3.Cross(delta,edge2);float det=Vector3.Dot(edge1,cross);if(MathF.Abs(det)<1e-7f)continue;
                float inv=1/det;Vector3 offset=from-a;float u=Vector3.Dot(offset,cross)*inv;if(u<0||u>1)continue;
                Vector3 q=Vector3.Cross(offset,edge1);float v=Vector3.Dot(delta,q)*inv;if(v<0||u+v>1)continue;
                float distance=Vector3.Dot(edge2,q)*inv;if(distance>1e-4f && distance<.9999f)return true;
            }
        }
        return false;
    }
}
