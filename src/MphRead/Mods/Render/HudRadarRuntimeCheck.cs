using System;
using System.IO;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

/// <summary>Optional integration diagnostic using existing recorded mode fixtures.</summary>
internal static class HudRadarRuntimeCheck
{
    internal static int Run(string directory)
    {
        var original=HudProfiles.CopyCurrent();
        try
        {
            Headless.Enter();
            string? room=directory.StartsWith("room:",StringComparison.Ordinal) ? directory[5..] : null;
            if(room!=null)
            {
                directory=Path.Combine(Path.GetTempPath(),"prime-radar-fixtures-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                Console.WriteLine("RADARCHECK fixtures: "+directory);
            }
            var profile=HudProfiles.CopyCurrent(); HudRadarStyles.Apply(profile,HudRadarStyle.Scanner); HudProfiles.Publish(profile);
            var style=HudProfiles.Runtime.Radar;
            foreach(string mode in new[] {"Bounty","BountyTeams","Capture","Defender","DefenderTeams","Nodes","NodesTeams","PrimeHunter"})
            {
                string path=Path.Combine(directory,mode+".ppdemo");
                if(room!=null)
                {
                    string selected=SelectFixtureRoom(room,Enum.Parse<GameMode>(mode));
                    Console.WriteLine($"RADARCHECK {mode} fixture room: {selected}");
                    ReplayWorldCoverageCheck.Write(path,selected,Enum.Parse<GameMode>(mode),new Vector3(0,1,0));
                }
                using var world=new PassiveReplayScene(path,new Vector2i(256,192));
                int steps=0, objectives=0, hunters=0, pickups=0; long bytes=0;
                while(steps<600 && world.Step())
                {
                    steps++;
                    var player=world.Scene.Players.Main;
                    // The generic synthetic actor fixture has no Prime winner.
                    // Seed that authorized mode state before measuring collection.
                    if(room!=null && mode=="PrimeHunter") world.Scene.GameState.PrimeHunter=1;
                    string hash=ReplayStateHash.Compute(world.Scene,world.Session.CurrentFrame);
                    long allocated=GC.GetAllocatedBytesForCurrentThread();
                    var contacts=player.CollectRadarContacts(style);
                    if(steps>16) bytes+=GC.GetAllocatedBytesForCurrentThread()-allocated;
                    foreach(var contact in contacts)
                    {
                        if(contact.Kind==RadarContactKind.Hunter)
                        {
                            hunters++;
                            var other=world.Scene.Players.Items[contact.StableId];
                            Require(other!=player && other.Health>0 && (other.LoadFlags & LoadFlags.Spawned)!=0,"ineligible hunter");
                        }
                        else if(contact.Kind is RadarContactKind.Weapon or RadarContactKind.Powerup) pickups++;
                        else objectives++;
                    }
                    var hidden=player.CollectRadarContacts(style with { Hunters=false,Weapons=false,Powerups=false,Objectives=false });
                    Require(hidden.IsEmpty,"filters must remove all contacts");
                    Require(hash==ReplayStateHash.Compute(world.Scene,world.Session.CurrentFrame),"collection changed gameplay hash");
                }
                Require(steps>16,"fixture too short");
                int flags=0,bases=0,nodes=0;
                foreach(var entity in world.Scene.Entities)
                { if(entity is OctolithFlagEntity) flags++; if(entity is FlagBaseEntity) bases++; if(entity is NodeDefenseEntity) nodes++; }
                Require(objectives>0,$"no authorized objectives observed in {mode}; actual={world.Scene.GameState.Mode}, flags={flags}, bases={bases}, nodes={nodes}, enabled={style.Objectives}");
                Require(bytes==0,"warmed contact collection allocated "+bytes+" bytes in "+mode);
                Console.WriteLine($"RADARCHECK {mode}: {steps} frames, {objectives} objective samples, {hunters} hunters, {pickups} pickups, 0 collection bytes, unchanged gameplay hashes");
            }
            return 0;
        }
        catch(Exception exception)
        {
            Console.Error.WriteLine("RADARCHECK failed: "+exception);
            return 1;
        }
        finally { HudProfiles.Publish(original); }
    }
    private static string SelectFixtureRoom(string preferred,GameMode mode)
    {
        if(mode==GameMode.PrimeHunter || HasObjectives(preferred,mode)) return preferred;
        foreach(string room in ThumbnailGenerator.MultiplayerRooms()) if(HasObjectives(room,mode)) return room;
        throw new InvalidOperationException("No installed room has objective entities for "+mode);
    }
    private static bool HasObjectives(string room,GameMode mode)
    {
        var (metadata,_)=Metadata.GetRoomByName(room);
        if(metadata?.EntityPath==null) return false;
        int layer=Metadata.GetMultiplayerEntityLayer(mode,4);
        var kind=mode is GameMode.Bounty or GameMode.BountyTeams or GameMode.Capture ? EntityType.OctolithFlag : EntityType.NodeDefense;
        foreach(var entity in Read.GetEntities(metadata.EntityPath,layer,metadata.FirstHunt)) if(entity.Type==kind) return true;
        return false;
    }
    private static void Require(bool condition,string message)
    { if(!condition) throw new InvalidOperationException(message); }
}
