using System.Collections.Generic;
using MphRead.Entities;

namespace MphRead.Mods.Network;

internal static class TimedPickupProjection
{
    internal static bool TrySource(Scene scene, out IReadOnlyDictionary<PowerUpSpawnKey, PickupItemFact> items,
        out TimedGrantPresentation presentation, out uint frame)
    {
        if (scene.Services is ReplaySceneServices replay && replay.State.TimedGrants.HasCoverage)
        { items = replay.State.PickupItems; presentation = replay.State.TimedGrants; frame = replay.Session.RecordingFrame; return true; }
        items = NetPickupNetwork.ItemFacts; presentation = NetGrantNetwork.Presentation; frame = NetSession.NetFrame;
        return !scene.Services.IsReplica && NetSession.IsClient && !NetSession.IsAuthority;
    }
    internal static bool Apply(ItemSpawnEntity spawn, ItemType type)
    {
        if (!NetPowerUpLedger.TryType(type, out var timed)
            || !TrySource(spawn.OwningScene, out var items, out var presentation, out uint frame)) return false;
        PickupItemFact? latest = null;
        foreach (var fact in items.Values)
            if (fact.Spawn.SpawnId == spawn.Id && fact.Type == timed && (latest == null
                || fact.Spawn.Room > latest.Value.Spawn.Room || fact.Spawn.Room == latest.Value.Spawn.Room
                && fact.Spawn.Generation > latest.Value.Spawn.Generation)) latest = fact;
        spawn.ModApplyTimedPickup(latest, presentation, frame);
        return true;
    }
    internal static void ApplyDynamic(Scene scene)
    {
        if (!TrySource(scene, out var facts, out var presentation, out uint frame)) return;
        var existing = new Dictionary<PowerUpSpawnKey, ItemInstanceEntity>();
        foreach (var item in scene.GetItemInstanceEntities())
            if (item.Owner == null && item.TokenId == 0 && NetPowerUpLedger.TryType(item.ItemType, out _))
            {
                if (item.ModPickupSpawn is not { } key) item.DespawnTimer = 0;
                else if (!existing.TryAdd(key, item)) item.DespawnTimer = 0;
            }
        foreach (var fact in facts.Values)
        {
            if (fact.Spawn.SpawnId != -1 || !fact.Available) continue;
            existing.Remove(fact.Spawn, out var item);
            if (item == null)
            {
                var type = fact.Type switch { TimedPowerUp.DoubleDamage => ItemType.DoubleDamage,
                    TimedPowerUp.Cloak => ItemType.Cloak, _ => ItemType.Deathalt };
                item = new ItemInstanceEntity(new(fact.Position, type, -1), scene.Room!.GetNodeRefByPosition(fact.Position), scene)
                    { ModPickupSpawn = fact.Spawn };
                scene.AddEntity(item);
            }
            item.ModPickupHidden = presentation.Hides(fact.Spawn, frame);
        }
        foreach (var item in existing.Values) item.DespawnTimer = 0;
    }
    internal static void Update(ItemInstanceEntity item)
    {
        if (item.ModPickupSpawn is not { } key
            || !TrySource(item.OwningScene, out var items, out var presentation, out uint frame)) return;
        if (items.TryGetValue(key, out var fact) && !fact.Available) item.DespawnTimer = 0;
        item.ModPickupHidden = presentation.Hides(key, frame);
    }
}
