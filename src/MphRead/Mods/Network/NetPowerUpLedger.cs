using System.Collections.Generic;
using MphRead.Entities;

namespace MphRead.Mods.Network;

/// <summary>Native authority adapter for A1. No claim wire, client reconciliation,
/// damage enforcement or replica checkpoint state is introduced here.</summary>
internal static class NetPowerUpLedger
{
    private static readonly Dictionary<ItemInstanceEntity, PowerUpSpawnKey> Items = new();
    private static NetTimedGrants? _ledger;
    private static ulong _epoch;
    private static ushort _match;
    private static uint _room;
    internal static NetTimedGrants? Ledger => _ledger;
    private static bool Authority(Scene scene) => NetSession.Active && NetSession.IsAuthority
        && !scene.Services.IsReplica && scene.GameState.Multiplayer;

    internal static bool TryType(ItemType item, out TimedPowerUp type)
    {
        type = item switch
        {
            ItemType.DoubleDamage => TimedPowerUp.DoubleDamage,
            ItemType.Cloak => TimedPowerUp.Cloak,
            ItemType.Deathalt => TimedPowerUp.DeathAlt,
            _ => (TimedPowerUp)255
        };
        return (uint)type < 3;
    }

    internal static void BeginRoom()
    {
        if (_room == uint.MaxValue) throw new ProgramException("Power-up room identity exhausted.");
        _ledger?.CloseAll(NetSession.NetFrame, TimedGrantEnd.Room);
        _room++;
        Items.Clear(); _ledger = null;
    }
    internal static void Reset()
    {
        _ledger?.CloseAll(NetSession.NetFrame, TimedGrantEnd.Session);
        // Empty-server suspension can resume the same epoch/match. Keep the
        // process room incarnation monotonic across session teardown as well.
        Items.Clear(); _ledger = null;
    }
    private static NetTimedGrants Ensure()
    {
        if (_ledger == null || _epoch != NetSession.AuthorityEpoch || _match != NetSession.CurrentMatchId)
        {
            Items.Clear(); _epoch = NetSession.AuthorityEpoch; _match = NetSession.CurrentMatchId;
            _ledger = new(_epoch, _match, _room, NetCampaignEvidence.TimedGrant);
        }
        return _ledger;
    }

    internal static bool Register(ItemInstanceEntity item)
    {
        if (!Authority(item.OwningScene) || !TryType(item.ItemType, out var type)) return true;
        var ledger = Ensure();
        if (Items.ContainsKey(item)) return true;
        // Native _spawnCount is a quota and can decrease. This monotonic identity
        // is assigned to each actual item incarnation, including dynamic drops.
        if (!ledger.RegisterSpawn(item.Owner?.Id ?? -1, type, out var key))
            return false;
        Items.Add(item, key);
        return true;
    }
    internal static void Remove(ItemInstanceEntity item)
    {
        if (Items.Remove(item, out var key)) _ledger?.RemoveSpawn(key);
    }
    internal static bool TrySpawnKey(ItemInstanceEntity item, out PowerUpSpawnKey key)
        => Items.TryGetValue(item, out key);

    private static uint? SourceFrame(PlayerEntity player)
    {
        int slot = player.SlotIndex;
        if (player.IsBot || slot == NetSession.LocalSlot) return NetSession.NetFrame;
        if ((uint)slot >= 8 || !NetSession.RemoteIntentValid[slot] || NetSession.RemoteIntentAge(slot) > 30)
            return null;
        var intent = NetSession.RemoteIntents[slot];
        return NetSession.MatchesStream(intent.MatchId, intent.AuthorityEpoch)
            && NetPlayerLifecycle.Matches(slot, intent.SlotGeneration, intent.LifeId) ? intent.Frame : null;
    }

    internal static bool TryNativeContact(PlayerEntity player, ItemInstanceEntity item)
    {
        if (!Authority(player.OwningScene)) return true; // A1 preserves offline and client prediction.
        if (!TryType(item.ItemType, out _) || player.Health == 0 || !player.ModIsInPlay
            || player.OwningScene.Services.DisablePowerups || item.DespawnTimer == 0
            || item.OwningScene != player.OwningScene) return false;
        var ledger = Ensure();
        if (!Items.TryGetValue(item, out var key)) return false;
        var life = ShotKey.For(player.SlotIndex, 0);
        ledger.BeginLife(life, NetSession.NetFrame);
        uint? source = SourceFrame(player);
        // Preserve the input phase even for a native callback that reaches this
        // adapter outside the usual player loop (including same-frame renewal).
        if (source is uint observed) ledger.ObserveInput(life, observed, NetSession.NetFrame);
        return ledger.TryGrant(key, life, NetSession.NetFrame, source, out _)
            == TimedGrantResult.Granted;
    }

    internal static void ObserveInput(PlayerEntity player)
    {
        if (!Authority(player.OwningScene) || !player.ModIsInPlay || SourceFrame(player) is not uint source) return;
        var ledger = Ensure();
        var life = ShotKey.For(player.SlotIndex, 0);
        ledger.BeginLife(life, NetSession.NetFrame);
        ledger.ObserveInput(life, source, NetSession.NetFrame);
    }
    internal static void RetireSlot(int slot, TimedGrantEnd reason)
        => _ledger?.RetireSlot(slot, NetSession.NetFrame, reason);
    internal static void Advance()
    {
        if (NetSession.IsAuthority) _ledger?.Advance(NetSession.NetFrame);
    }
}
