using MphRead.Entities;
using System.Collections.Generic;

namespace MphRead.Mods.Network;

/// <summary>Apply the server's complete objective facts without changing player movement.
/// The existing world stream fences match, authority and tick before publishing a frame.</summary>
internal static class NetObjectiveSync
{
    private static Scene? _appliedScene;
    private static uint _appliedTick;
    private static ushort _appliedMatch;
    private static ulong _appliedEpoch;
    private static ushort _pickupMatch;
    private static ulong _pickupEpoch;
    private static ushort _predictionGeneration, _predictionLife;
    private static readonly Dictionary<short, HealthSpawnState> _pickups = new();
    private static readonly Dictionary<int, uint> _predictedPickups = new();
    internal static void Reset()
    {
        _appliedScene = null; _appliedTick = 0; _appliedMatch = _pickupMatch = 0;
        _appliedEpoch = _pickupEpoch = 0; _predictionGeneration = _predictionLife = 0;
        _pickups.Clear(); _predictedPickups.Clear();
    }
    private static void Fence()
    {
        if (_pickupMatch != NetSession.CurrentMatchId || _pickupEpoch != NetSession.AuthorityEpoch)
        {
            _pickupMatch = NetSession.CurrentMatchId; _pickupEpoch = NetSession.AuthorityEpoch;
            _pickups.Clear(); _predictedPickups.Clear();
        }
        ushort generation = NetPlayerLifecycle.Generation(NetSession.LocalSlot), life = NetPlayerLifecycle.Get(NetSession.LocalSlot);
        if (_predictionGeneration != generation || _predictionLife != life)
        { _predictionGeneration = generation; _predictionLife = life; _predictedPickups.Clear(); }
    }
    internal static bool TryGetPickup(short id, out HealthSpawnState state)
    {
        Fence();
        if (!_pickups.TryGetValue(id, out state)) return false;
        if (_predictedPickups.ContainsKey(id)) state = state with { Available = false };
        return true;
    }
    internal static void PredictPickup(Scene scene, ItemInstanceEntity item)
    {
        if (!IsClient(scene)) return;
        Fence();
        if (_predictedPickups.Count < ReplayAuthorityWorld.MaximumEntities)
            _predictedPickups[item.Owner?.Id ?? item.Id] = scene.Services.PlayerReplication.Frame;
    }
    private static void ApplyEntities(Scene scene, ReplayAuthorityWorld world)
    {
        Fence();
        if (NetSession.LocalSlot >= 0 && NetSession.LocalSlot < world.Resources.Length
            && world.Resources[NetSession.LocalSlot].Actor.Slot == NetSession.LocalSlot
            && Resolve(scene, world.Resources[NetSession.LocalSlot].Actor) != null)
        {
            uint ack = world.Resources[NetSession.LocalSlot].AcknowledgedFrame;
            var completed = new List<int>();
            foreach (var pickup in _predictedPickups)
                if (NetLifecycleTracker.Newer(ack, pickup.Value)) completed.Add(pickup.Key);
            foreach (int id in completed) _predictedPickups.Remove(id);
        }
        _pickups.Clear();
        foreach (var pickup in world.Pickups)
        {
            _pickups[pickup.Id] = pickup.State;
            if (scene.TryGetEntity(pickup.Id, out var entity) && entity is ItemSpawnEntity target
                && !Multiplayer.MapResourceRules.IsHealth(target.Data.ItemType)
                && !_predictedPickups.ContainsKey(pickup.Id))
                target.ModApplyNetworkHealthState(pickup.State, feedback: false);
        }
        foreach (var door in world.Doors)
            if (scene.TryGetEntity(door.Id, out var entity) && entity is DoorEntity target)
            {
                target.Flags = door.Flags; target.ConnectorInactive = door.ConnectorInactive;
                if (target.Portal != null) target.Portal.Active = door.Portal;
                if (target.ConnectorCollision != null) target.ConnectorCollision.Active = door.Collision;
            }
        var existing = new Dictionary<int, ItemInstanceEntity>();
        foreach (var item in scene.GetItemInstanceEntities())
            if (item.Owner == null && item.TokenId == 0)
            {
                if (item.Id >= -1) item.DespawnTimer = 0;
                else existing[-2 - item.Id] = item;
            }
        foreach (var drop in world.Drops)
        {
            existing.Remove(drop.Identity, out var item);
            if (_predictedPickups.ContainsKey(-2 - drop.Identity)) continue;
            if (item != null && item.ItemType != drop.Type) continue; // Immutable drop identities cannot change type.
            if (item == null)
            { item = ItemInstanceEntity.CreateLiveAuthorityDrop(drop, scene); scene.AddEntity(item); }
            item.Position = drop.Position; item.DespawnTimer = drop.DespawnTimer;
        }
        foreach (var item in existing.Values) item.DespawnTimer = 0;
    }
    internal static bool IsClient(Scene scene) => !scene.Services.IsReplica
        && NetSession.IsClient && !NetSession.IsAuthority;

    internal static PlayerEntity? Resolve(Scene scene, ReplayActorRef actor) => actor.Slot < PlayerEntity.SlotCapacity
        && NetPlayerLifecycle.Matches(actor.Slot, actor.Generation, actor.Life)
        && scene.Players.Items[actor.Slot].LoadFlags.TestFlag(LoadFlags.Active)
        ? scene.Players.Items[actor.Slot] : null;

    internal static void Apply(Scene scene, ReplayAuthorityWorld world, bool bootstrap = false)
    {
        if (!IsClient(scene) || (!bootstrap && !NetRoomChange.GameplayReady)
            || (!bootstrap && !NetSession.ObjectiveTickIsCurrent(world.Tick))
            || world.MatchId != NetSession.CurrentMatchId || world.Epoch != NetSession.AuthorityEpoch
            || scene.Room?.Meta.Name != NetSession.ServerMatch?.RoomKey) return;
        if (!bootstrap && _appliedScene == scene && _appliedMatch == world.MatchId && _appliedEpoch == world.Epoch
            && !NetLifecycleTracker.Newer(world.Tick, _appliedTick)) return;
        if (_appliedScene != scene || _appliedMatch != world.MatchId || _appliedEpoch != world.Epoch)
        {
            // Dynamic drop identities belong to an authority epoch. A new
            // baseline may reuse a number for a different native item.
            var obsolete = new List<ItemInstanceEntity>();
            foreach (var item in scene.GetItemInstanceEntities())
                if (item.Owner == null && item.TokenId == 0 && item.Id < -1) obsolete.Add(item);
            foreach (var item in obsolete)
            { item.Destroy(); scene.RemoveEntity(item); }
        }
        _appliedScene = scene; _appliedTick = world.Tick; _appliedMatch = world.MatchId; _appliedEpoch = world.Epoch;
        ApplyEntities(scene, world);
        foreach (var resource in world.Resources) Resolve(scene, resource.Actor)?.ApplyNetworkResources(resource);
        world.ApplyTokens(scene);
        foreach (var ammo in world.Chamber)
            Resolve(scene, ammo.Actor)?.ApplyChamberAmmo(ammo.Ammo, ammo.AcknowledgedFrame);
        scene.GameState.ActiveHardpointId = world.ActiveHardpointId;
        scene.GameState.HardpointTicksRemaining = world.HardpointTicksRemaining;
        scene.GameState.PrimeHunter = Resolve(scene, world.Prime)?.SlotIndex ?? -1;
        world.FlagScores.CopyTo(scene.GameState.OctolithScores, 0);
        world.NodesCaptured.CopyTo(scene.GameState.NodesCaptured, 0);
        foreach (var flag in world.Flags)
            if (scene.TryGetEntity(flag.Id, out var entity) && entity is OctolithFlagEntity target)
                target.ApplyLiveAuthority(flag);
        foreach (var node in world.Nodes)
            if (scene.TryGetEntity(node.Id, out var entity) && entity is NodeDefenseEntity target)
                target.ApplyLiveAuthority(node);
    }
}
