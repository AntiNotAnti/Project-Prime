using System;
using MphRead.Entities;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

namespace MphRead.Mods.HeroModes;

/// <summary>Mode-scoped team rooms. Physical architecture still owns ordinary wall collision.</summary>
public static class CtzSpawnRooms
{
    public static PlayerEntity? Owner(EntityBase? source) => source switch
    {
        PlayerEntity player => player,
        BeamProjectileEntity beam => Owner(beam.Owner),
        BombEntity bomb => bomb.Owner,
        HalfturretEntity turret => turret.Owner,
        _ => null
    };
    public static int TeamForSide(Scene scene, int side) => HeroRules.IsPayload(scene.GameState.Mode) ? side : side ^ ((scene.CaptureZone.State.Round - 1) & 1);
    private static System.Collections.Generic.List<CtzSpawnRoomDefinition>? Rooms(Scene scene) => HeroRules.IsPayload(scene.GameState.Mode) ? scene.Payload.Rooms(scene) : CtzRuntime.Definition(scene)?.SpawnRooms;
    public static bool InsideFriendly(PlayerEntity player) => HeroRules.Active(player.OwningScene.GameState.Mode)
        && Rooms(player.OwningScene)?.Exists(room =>
            TeamForSide(player.OwningScene,room.Side)==player.TeamIndex && room.Contains(player.Position)) == true;
    public static bool ProtectsFrom(PlayerEntity target, EntityBase? source)
    {
        if (!HeroRules.Active(target.OwningScene.GameState.Mode)) return false;
        var owner=Owner(source);
        return owner != null && owner != target && owner.TeamIndex != target.TeamIndex && InsideFriendly(target);
    }
    public static bool BlocksMovement(PlayerEntity player, Vector3 from, Vector3 to)
    {
        if (!HeroRules.Active(player.OwningScene.GameState.Mode) || player.Health<=0 || !player.ModIsInPlay) return false;
        var rooms=Rooms(player.OwningScene);
        if(rooms==null) return false;
        foreach(var room in rooms)
        {
            if(TeamForSide(player.OwningScene,room.Side)==player.TeamIndex) continue;
            // Expand by the collision body, not camera/head height. A complete segment test prevents tunnelling.
            if(room.Contains(from) && (HeroRules.IsPayload(player.OwningScene.GameState.Mode) || !room.Contains(to))) continue;
            if(Segment(room,from,to,.65f,2.2f,out _,out _)) return true;
        }
        return false;
    }
    public static bool BlocksSegment(Scene scene,int firingTeam,Vector3 from,Vector3 to,
        out float distance,out Vector3 normal,float radius=0)
    {
        distance=2; normal=Vector3.Zero;
        if(!HeroRules.Active(scene.GameState.Mode)) return false;
        var rooms=Rooms(scene);
        if(rooms==null) return false;
        foreach(var room in rooms)
        {
            if(TeamForSide(scene,room.Side)==firingTeam) continue;
            if(Segment(room,from,to,Math.Max(0,radius),Math.Max(0,radius),out float t,out var n) && t<distance)
            { distance=t;normal=n; }
        }
        return distance<=1;
    }
    internal static bool Segment(CtzSpawnRoomDefinition room,Vector3 from,Vector3 to,float radius,float height,
        out float enter,out Vector3 normal)
    {
        enter=0;float exit=1;normal=Vector3.Zero;
        if(!room.Valid || !CtzRules.Finite(from) || !CtzRules.Finite(to)) return false;
        var min=new Vector3(room.Min[0]-radius,room.Min[1]-height,room.Min[2]-radius);
        var max=new Vector3(room.Max[0]+radius,room.Max[1]+radius,room.Max[2]+radius);
        var delta=to-from;
        for(int axis=0;axis<3;axis++)
        {
            if(Math.Abs(delta[axis])<.000001f)
            { if(from[axis]<min[axis] || from[axis]>max[axis]) return false; continue; }
            float a=(min[axis]-from[axis])/delta[axis], b=(max[axis]-from[axis])/delta[axis];
            var n=Vector3.Zero;n[axis]=-Math.Sign(delta[axis]);
            if(a>b) (a,b)=(b,a);
            if(a>=enter) { enter=a;normal=n; }
            exit=Math.Min(exit,b);
            if(enter>exit) return false;
        }
        if(exit<0 || enter>1) return false;
        enter=Math.Max(0,enter);
        if(normal==Vector3.Zero) normal=delta.LengthSquared>0 ? -delta.Normalized() : Vector3.UnitX;
        return true;
    }
}
