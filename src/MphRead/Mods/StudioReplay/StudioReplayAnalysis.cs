#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using MphRead.Entities;
using MphRead.Formats;

namespace MphRead.Mods.StudioReplay;

public sealed record StudioReplayHeatSample(uint Frame, int Slot, Vector3 Position, string Kind, float Weight = 1, Vector3? Direction = null);
public sealed record StudioReplayAnalysis(uint DurationFrames, IReadOnlyList<StudioReplayHeatSample> Samples,
    IReadOnlyList<StudioReplayEvent> Events, string SourceHash);
public sealed record StudioReplayComparison(uint Frame, bool GameplayEqual, bool PresentationEqual, bool FullGraphEqual,
    IReadOnlyList<StudioReplayPoseDifference> Players);
public sealed record StudioReplayPoseDifference(int Slot, Vector3 PositionA, Vector3 PositionB, float Distance);

public sealed partial class StudioReplayPlayer
{
    /// <summary>Read only recorded packet state on a detached worker. No Scene,
    /// simulation step, mutable editing state or graphics resource is needed.</summary>
    public Task<StudioReplayAnalysis> AnalyzeAsync(CancellationToken cancellation = default)
    {
        RequireOwner();
        if (_player is not { Ready: true } owner) throw new InvalidOperationException("Wait for replay preparation before analyzing recorded gameplay.");
        // Copy immutable map geometry on the owner. The worker only decodes
        // packets and tests value volumes; it never steps or constructs a Scene.
        var objectives = new List<(int Id, CollisionVolume Volume)>();
        var pickups = new Dictionary<short, Vector3>();
        GameMode mapMode = owner.Current.Scene.GameState.Mode;
        foreach (var entity in owner.Current.Scene.Entities)
        {
            if (entity is NodeDefenseEntity node && (owner.Current.Scene.GameState.IsHardpoint || mapMode is GameMode.Defender or GameMode.DefenderTeams or GameMode.Nodes or GameMode.NodesTeams)) objectives.Add((node.Id, node.Volume));
            else if (entity is FlagBaseEntity flag && mapMode is GameMode.Capture or GameMode.Bounty or GameMode.BountyTeams or GameMode.Headhunter) objectives.Add((flag.Id, CollisionVolume.Move(flag.Data.Volume, flag.Position)));
            else if (entity is ItemSpawnEntity spawn) pickups[(short)spawn.Id] = Public(spawn.Position);
        }
        string path = _playbackPath ?? LogicalPath;
        string sourceHash = _playbackContentHash ?? throw new InvalidOperationException("The immutable recording identity is unavailable.");
        var jobResources = RetainJobResources();
        // Always admit the cleanup delegate, including an already-canceled job.
        return Task.Run(() =>
        {
            using var ownedResources = jobResources;
            cancellation.ThrowIfCancellationRequested();
            var host = new PassiveReplaySessionHost();
            using var reader = new ReplayPlaybackSession(host);
            if (!reader.JoinDetached(path, cancellation)) throw new IOException(reader.LastError ?? "Cannot analyze replay.");
            var samples = new List<StudioReplayHeatSample>();
            var events = reader.Events.GroupBy(e => e.Frame).ToDictionary(g => g.Key, g => g.ToArray());
            var spawnFrames = new Dictionary<int, (uint Frame, Vector3 Position)>();
            var health = new Dictionary<short, HealthSpawnState>();
            var resources = new Dictionary<int, ReplayResourceState>();
            var lastRoutes = new Dictionary<int, uint>();
            var lives = new Dictionary<int, (ushort Generation, ushort Life)>();
            uint last = uint.MaxValue;
            while (!reader.AtEnd)
            {
                cancellation.ThrowIfCancellationRequested(); reader.PumpFrame();
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
                    var position = Public(player.Position);
                    bool spawned = (player.Flags & PlayerState.FlagSpawned) != 0 && player.Health > 0;
                    if (frame % 6 == 0 && spawned)
                    {
                        samples.Add(new(frame, slot, position, "Movement", .1f));
                        if (player.CurrentWeapon == (byte)BeamType.Imperialist && (player.Flags & PlayerState.FlagZoomed) != 0)
                            samples.Add(new(frame, slot, position, "Imperialist sightlines", .1f, Public(player.Facing)));
                        int hunter = Math.Clamp(player.Hunter, (byte)0, (byte)7);
                        var center = player.Position + PlayerEntity.PlayerVolumes[hunter, (player.Flags & PlayerState.FlagAltForm) != 0 ? 2 : 0].SpherePosition;
                        bool hardpoint = (GameMode)(host.State.Configuration?.Match.Mode ?? (GameMode)(host.State.Match?.Mode ?? 0)) is GameMode.Hardpoint or GameMode.HardpointTeams;
                        if (objectives.Any(area => (!hardpoint || host.State.AuthorityWorld?.ActiveHardpointId == area.Id) && area.Volume.TestPoint(center))
                            || host.State.AuthorityWorld is { } world && (world.Prime.Slot == slot && host.State.MatchesLife(slot,world.Prime.Generation,world.Prime.Life)
                                || world.Flags.Any(flag => flag.Carrier.Slot == slot && host.State.MatchesLife(slot,flag.Carrier.Generation,flag.Carrier.Life))))
                            samples.Add(new(frame, slot, position, "Objective presence", .1f));
                        if (lastRoutes.TryGetValue(slot, out uint routeStart) && frame >= routeStart && frame - routeStart <= 600)
                            samples.Add(new(frame, slot, position, "Pickup routes", .1f));
                    }
                    if (events.TryGetValue(frame, out var frameEvents)) foreach (var marker in frameEvents)
                    {
                        if ((marker.Type == ReplayEventType.Damage && marker.TargetSlot == slot || marker.Type == ReplayEventType.PlayerDeath && marker.ActorSlot == slot)
                            && spawnFrames.TryGetValue(slot, out var spawn) && frame >= spawn.Frame && frame - spawn.Frame <= 180)
                            samples.Add(new(frame, slot, spawn.Position, "Spawn pressure", marker.Type == ReplayEventType.Damage ? Math.Max(1, marker.Value) : 1));
                        if (marker.ActorSlot != slot) continue;
                        if (marker.Type == ReplayEventType.PlayerSpawn) spawnFrames[slot] = (frame, position);
                        string kind = marker.Type switch
                        {
                            ReplayEventType.PlayerDeath => "Deaths", ReplayEventType.Kill => "Kills", ReplayEventType.Damage => "Damage",
                            ReplayEventType.PlayerSpawn => "Spawns", ReplayEventType.WeaponFired => "Weapons", ReplayEventType.RelicPickup or ReplayEventType.TokenConfirmed or ReplayEventType.TokenBanked => "Pickups",
                            ReplayEventType.Objective or ReplayEventType.FlagCapture or ReplayEventType.NodeCapture or ReplayEventType.HardpointCaptured or ReplayEventType.PrimeChanged => "Objectives",
                            _ => "Events"
                        };
                        samples.Add(new(frame, slot, position, kind, marker.Type == ReplayEventType.Damage ? Math.Max(1, marker.Value) : 1,
                            marker.Type == ReplayEventType.WeaponFired ? Public(player.Facing) : null));
                        if (marker.Type == ReplayEventType.WeaponFired && marker.Value == (int)BeamType.Imperialist)
                            samples.Add(new(frame, slot, position, "Imperialist sightlines", 1, Public(player.Facing)));
                        if (kind == "Pickups") { lastRoutes[slot] = frame; samples.Add(new(frame, slot, position, "Pickup routes")); }
                    }
                    foreach (var fact in host.State.ShotFacts.Where(f => f.ShooterSlot == slot))
                        samples.Add(new(frame, slot, Public(fact.ImpactPoint), "Engagements", Math.Max(1, fact.Damage)));
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
                    { lastRoutes[state.PickerSlot] = frame; samples.Add(new(frame,state.PickerSlot,pickup.Value,"Pickups")); samples.Add(new(frame,state.PickerSlot,pickup.Value,"Pickup routes")); }
                    health[pickup.Key] = state;
                }
                if (host.State.AuthorityWorld is { } authority)
                    foreach (var resource in authority.Resources)
                    {
                        int slot = resource.Actor.Slot;
                        if (slot >= 8 || !host.State.MatchesLife(slot,resource.Actor.Generation,resource.Actor.Life)) continue;
                        if (resources.TryGetValue(slot,out var previous) && previous.Actor == resource.Actor
                            && (resource.Ua > previous.Ua || resource.Missiles > previous.Missiles || (resource.Weapons & ~previous.Weapons) != 0)
                            && host.State.TryGetPlayer(slot,out var actor))
                        { lastRoutes[slot] = frame; samples.Add(new(frame,slot,Public(actor.Position),"Pickup routes")); }
                        resources[slot] = resource;
                    }
                if (samples.Count > 500_000) throw new InvalidDataException("Replay analytics exceeds the bounded sample budget. Analyze a shorter clip.");
            }
            return new StudioReplayAnalysis(reader.LastFrame, samples.ToArray(), reader.Events.Select(e => new StudioReplayEvent(e.Frame, e.Type.ToString(), e.ActorSlot, e.TargetSlot, e.Value)).ToArray(),
                sourceHash);
        });
    }
    public static StudioReplayComparison Compare(StudioReplayWorldSnapshot left, StudioReplayWorldSnapshot right)
    {
        if (left.Frame != right.Frame) throw new ArgumentException("Comparison requires the same simulation frame.");
        return new(left.Frame, left.GameplayHash == right.GameplayHash, left.PresentationHash == right.PresentationHash,
            left.FullGraphHash == right.FullGraphHash,
            left.Players.Join(right.Players, p => p.Slot, p => p.Slot, (a, b) => new StudioReplayPoseDifference(a.Slot, a.Position, b.Position,
                Vector3.Distance(a.Position, b.Position))).ToArray());
    }
}

#endif
