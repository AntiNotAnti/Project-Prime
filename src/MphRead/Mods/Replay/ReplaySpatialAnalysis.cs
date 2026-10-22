#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;
namespace MphRead.Mods.Replay;
internal sealed record ReplaySpatialGeometry((int Id,CollisionVolume Volume)[] Objectives,KeyValuePair<short,Vector3>[] Pickups);
internal static class ReplaySpatialAnalysis
{
    private static readonly Dictionary<string,TheatreSpatialKind> Kinds=new()
    {
        ["Movement"]=TheatreSpatialKind.Movement,
        ["Deaths"]=TheatreSpatialKind.Deaths,
        ["Kills"]=TheatreSpatialKind.Kills,
        ["Damage"]=TheatreSpatialKind.Damage,
        ["Weapons"]=TheatreSpatialKind.Weapons,
        ["Spawns"]=TheatreSpatialKind.Spawns,
        ["Objectives"]=TheatreSpatialKind.Objectives,
        ["Pickups"]=TheatreSpatialKind.Pickups,
        ["Engagements"]=TheatreSpatialKind.Engagements,
        ["Spawn pressure"]=TheatreSpatialKind.SpawnPressure,
        ["Objective presence"]=TheatreSpatialKind.ObjectivePresence,
        ["Imperialist sightlines"]=TheatreSpatialKind.ImperialistSightlines,
        ["Pickup routes"]=TheatreSpatialKind.PickupRoutes,
    };
    internal static ReplaySpatialGeometry Capture(Scene scene)
    {
        var objectives=new List<(int,CollisionVolume)>();var pickups=new List<KeyValuePair<short,Vector3>>();
        var mapMode=scene.GameState.Mode;
        foreach(var entity in scene.Entities)
        {
            if(entity is NodeDefenseEntity node&&(scene.GameState.IsHardpoint||mapMode is GameMode.Defender or GameMode.DefenderTeams or GameMode.Nodes or GameMode.NodesTeams))objectives.Add((node.Id,node.Volume));
            else if(entity is FlagBaseEntity flag&&mapMode is GameMode.Capture or GameMode.Bounty or GameMode.BountyTeams or GameMode.Headhunter)objectives.Add((flag.Id,CollisionVolume.Move(flag.Data.Volume,flag.Position)));
            else if(entity is ItemSpawnEntity spawn)pickups.Add(new((short)spawn.Id,spawn.Position));
        }
        return new(objectives.ToArray(),pickups.ToArray());
    }
    internal static TheatreSpatialAnalysis Read(string path,CancellationToken cancellation,ReplaySpatialGeometry? geometry=null)
    {
        var objectives=geometry?.Objectives??Array.Empty<(int Id,CollisionVolume Volume)>();
        var pickups=geometry?.Pickups??Array.Empty<KeyValuePair<short,Vector3>>();
        var host=new PassiveReplaySessionHost();using var reader=new ReplayPlaybackSession(host);
        if(!reader.JoinDetached(path,cancellation))throw new InvalidDataException(reader.LastError);
            var samples = new List<TheatreSpatialSample>();
            void Add(uint frame,int slot,OpenTK.Mathematics.Vector3 position,string kind,float weight=1,OpenTK.Mathematics.Vector3? direction=null)
            {
                if(!Kinds.TryGetValue(kind,out var category))return;
                samples.Add(new(frame,slot,position.X,position.Y,position.Z,category,weight,
                    direction is{} facing?new TheatreSpatialDirection(facing.X,facing.Y,facing.Z):null));
            }
            var events = reader.Events.GroupBy(e => e.Frame).ToDictionary(g => g.Key, g => g.ToArray());
            var spawnFrames = new Dictionary<int, (uint Frame, Vector3 Position)>();
            var health = new Dictionary<short, HealthSpawnState>();
            var resources = new Dictionary<int, (ushort Generation,ushort Life,ushort Ua,ushort Missiles)>();
            var lastRoutes = new Dictionary<int, uint>();
            var lives = new Dictionary<int, (ushort Generation, ushort Life)>();
            uint last = uint.MaxValue;
            while (!reader.AtEnd)
            {
                cancellation.ThrowIfCancellationRequested(); reader.PumpFrame();host.State.DiscardDetachedDrawPackets();
                if (reader.LastResult != ReplayOpenResult.Success) throw new IOException(reader.LastError);
                if(reader.IsWarming)continue;
                uint frame = reader.CurrentFrame;
                if (frame == last) break; last = frame;
                for (int slot = 0; slot < 8; slot++)
                {
                    if (!host.State.TryGetPlayer(slot, out var player)) continue;
                    var life = (player.SlotGeneration,player.LifeId);
                    if(lives.TryGetValue(slot,out var previousLife) && previousLife != life) { spawnFrames.Remove(slot); lastRoutes.Remove(slot); }
                    lives[slot] = life;
                    var position = (player.Position);
                    bool spawned = (player.Flags & PlayerState.FlagSpawned) != 0 && player.Health > 0;
                    if (frame % 6 == 0 && spawned)
                    {
                        Add(frame, slot, position, "Movement", .1f);
                        if (player.CurrentWeapon == (byte)BeamType.Imperialist && (player.Flags & PlayerState.FlagZoomed) != 0)
                            Add(frame, slot, position, "Imperialist sightlines", .1f, (player.Facing));
                        var occupant=host.State.Occupant(slot);
                        int hunter=(int)occupant.Hunter;
                        bool knownHunter=occupant.Generation!=0&&occupant.Generation==player.SlotGeneration
                            &&hunter>=0&&hunter<PlayerEntity.PlayerVolumes.GetLength(0);
                        // Missing/stale appearance cannot supply a fabricated collision center.
                        // Carrier/Prime facts below remain independently usable for this life.
                        var center=knownHunter?player.Position+PlayerEntity.PlayerVolumes[hunter,(player.Flags&PlayerState.FlagAltForm)!=0?2:0].SpherePosition:player.Position;
                        bool hardpoint = (GameMode)(host.State.Configuration?.Match.Mode ?? (GameMode)(host.State.Match?.Mode ?? 0)) is GameMode.Hardpoint or GameMode.HardpointTeams;
                        if (knownHunter&&objectives.Any(area => (!hardpoint || host.State.AuthorityWorld?.ActiveHardpointId == area.Id) && area.Volume.TestPoint(center))
                            || host.State.AuthorityWorld is { } world && (world.Prime.Slot == slot && host.State.MatchesLife(slot,world.Prime.Generation,world.Prime.Life)
                                || world.Flags.Any(flag => flag.Carrier.Slot == slot && host.State.MatchesLife(slot,flag.Carrier.Generation,flag.Carrier.Life))))
                            Add(frame, slot, position, "Objective presence", .1f);
                        if (lastRoutes.TryGetValue(slot, out uint routeStart) && TheatreSpatialProjection.WithinWindow(frame,routeStart,600))
                            Add(frame, slot, position, "Pickup routes", .1f);
                    }
                    if (events.TryGetValue(frame, out var frameEvents)) foreach (var marker in frameEvents)
                    {
                        if ((marker.Type == ReplayEventType.Damage && marker.TargetSlot == slot || marker.Type == ReplayEventType.PlayerDeath && marker.ActorSlot == slot)
                            && spawnFrames.TryGetValue(slot, out var spawn) && TheatreSpatialProjection.WithinWindow(frame,spawn.Frame,180))
                            Add(frame, slot, spawn.Position, "Spawn pressure", marker.Type == ReplayEventType.Damage ? Math.Max(1, marker.Value) : 1);
                        if (marker.ActorSlot != slot) continue;
                        if (marker.Type == ReplayEventType.PlayerSpawn) spawnFrames[slot] = (frame, position);
                        string kind = marker.Type switch
                        {
                            ReplayEventType.PlayerDeath => "Deaths", ReplayEventType.Kill => "Kills", ReplayEventType.Damage => "Damage",
                            ReplayEventType.PlayerSpawn => "Spawns", ReplayEventType.WeaponFired => "Weapons", ReplayEventType.RelicPickup or ReplayEventType.TokenConfirmed or ReplayEventType.TokenBanked => "Pickups",
                            ReplayEventType.Objective or ReplayEventType.FlagCapture or ReplayEventType.NodeCapture or ReplayEventType.HardpointCaptured or ReplayEventType.PrimeChanged => "Objectives",
                            _ => "Events"
                        };
                        Add(frame, slot, position, kind, marker.Type == ReplayEventType.Damage ? Math.Max(1, marker.Value) : 1,
                            marker.Type == ReplayEventType.WeaponFired ? (player.Facing) : null);
                        if (marker.Type == ReplayEventType.WeaponFired && marker.Value == (int)BeamType.Imperialist)
                            Add(frame, slot, position, "Imperialist sightlines", 1, (player.Facing));
                        if (kind == "Pickups") { lastRoutes[slot] = frame; Add(frame, slot, position, "Pickup routes"); }
                    }
                    foreach (var fact in host.State.ShotFacts.Where(f => f.ShooterSlot == slot))
                        Add(frame, slot, (fact.ImpactPoint), "Engagements", Math.Max(1, fact.Damage));
                }
                foreach (var pickup in pickups)
                {
                    HealthSpawnState state;
                    if (!host.State.TryGetHealthSpawn(pickup.Key, out state))
                    {
                        if(host.State.AuthorityWorld is not { } world || !world.Pickups.Any(entry=>entry.Id==pickup.Key))continue;
                        state = world.Pickups.First(entry=>entry.Id==pickup.Key).State;
                    }
                    if (health.TryGetValue(pickup.Key, out var previous) && previous.Available && !state.Available && state.PickerSlot is >= 0 and < 8)
                    { lastRoutes[state.PickerSlot] = frame; Add(frame,state.PickerSlot,pickup.Value,"Pickups"); Add(frame,state.PickerSlot,pickup.Value,"Pickup routes"); }
                    health[pickup.Key] = state;
                }
                // Current recordings carry ammo in accepted intents rather than the old
                // authority resource tail. Compare only fresh intents in the exact same life.
                for(int slot=0;slot<8;slot++)
                {
                    if(!host.State.TryGetPlayer(slot,out var actor)||!host.State.TryGetIntent(slot,out var intent)||host.State.IntentAge(slot)!=0)continue;
                    var current=(actor.SlotGeneration,actor.LifeId,intent.AmmoUa,intent.AmmoMissiles);
                    if(resources.TryGetValue(slot,out var previous)&&previous.Generation==current.SlotGeneration&&previous.Life==current.LifeId
                        &&(current.AmmoUa>previous.Ua||current.AmmoMissiles>previous.Missiles))
                    {lastRoutes[slot]=frame;Add(frame,slot,actor.Position,"Pickup routes");}
                    resources[slot]=current;
                }
                if (samples.Count > 500_000) throw new InvalidDataException("Replay analytics exceeds the bounded sample budget. Analyze a shorter clip.");
            }
        cancellation.ThrowIfCancellationRequested();return new(reader.LastFrame,samples.ToImmutableArray());
    }
}
#endif
