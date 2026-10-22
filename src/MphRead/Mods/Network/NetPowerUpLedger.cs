using System;
using System.Collections.Generic;
using MphRead.Entities;

namespace MphRead.Mods.Network;

/// <summary>Native authority reservations and contact proof adapter. Client
/// presentation is replicated separately from source damage enforcement.</summary>
internal static class NetPowerUpLedger
{
    private static readonly Dictionary<ItemInstanceEntity, PowerUpSpawnKey> Items = new();
    private static NetTimedGrants? _ledger;
    private static NetPickupClaims? _claims;
    internal static NetPickupClaims? Claims => _claims;
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
        Items.Clear(); _ledger = null; _claims = null; NetPickupNetwork.Reset();
    }
    internal static void Reset()
    {
        _ledger?.CloseAll(NetSession.NetFrame, TimedGrantEnd.Session);
        // Empty-server suspension can resume the same epoch/match. Keep the
        // process room incarnation monotonic across session teardown as well.
        Items.Clear(); _ledger = null; _claims = null; NetPickupNetwork.Reset();
    }
    private static NetTimedGrants Ensure()
    {
        if (_ledger == null || _epoch != NetSession.AuthorityEpoch || _match != NetSession.CurrentMatchId)
        {
            Items.Clear(); _epoch = NetSession.AuthorityEpoch; _match = NetSession.CurrentMatchId;
            _ledger = new(_epoch, _match, _room, NetGrantNetwork.Changed);
            _claims = new(_epoch, _match, _room, _ledger, NetCampaignEvidence.PickupProof);
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
        NetPickupNetwork.PublishItem(new(key, type, NetSession.NetFrame, item.DespawnTimer != 0 && item.TokenId == 0, item.Position));
        return true;
    }
    internal static void Remove(ItemInstanceEntity item)
    {
        if (Items.Remove(item, out var key))
        {
            if (TryType(item.ItemType, out var type))
                NetPickupNetwork.PublishItem(new(key, type, NetSession.NetFrame, false, item.Position));
            _ledger?.RemoveSpawn(key);
        }
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
        if (!TryType(item.ItemType, out var type) || player.Health == 0 || !player.ModIsInPlay
            || player.OwningScene.Services.DisablePowerups || item.DespawnTimer == 0
            || item.OwningScene != player.OwningScene) return false;
        var ledger = Ensure();
        if (!Items.TryGetValue(item, out var key)) return false;
        var life = ShotKey.For(player.SlotIndex, 0);
        ledger.BeginLife(life, NetSession.NetFrame);
        NetGrantNetwork.BeginLife(life, _room, NetSession.NetFrame);
        uint? source = SourceFrame(player);
        // Preserve the input phase even for a native callback that reaches this
        // adapter outside the usual player loop (including same-frame renewal).
        if (source is uint observed) ledger.ObserveInput(life, observed, NetSession.NetFrame);
        // Save the actual native contact before consumption, separately from an
        // immutable first input observation that may precede this movement step.
        var pose = source is uint contactSource ? player.ModPickupContactPose(contactSource, NetSession.NetFrame) : default;
        var available = new PickupItemFact(key, type,
            NetSession.NetFrame, item.DespawnTimer != 0 && item.TokenId == 0, item.Position);
        bool granted = ledger.TryGrant(key, life, NetSession.NetFrame, source, out var grant) == TimedGrantResult.Granted;
        if (granted && source.HasValue && grant != null)
            _claims?.ObserveNativeReservation(grant, pose, available);
        if (granted)
            NetPickupNetwork.PublishItem(new(key, type, NetSession.NetFrame, false, item.Position));
        return granted;
    }

    internal static PickupItemFact[] CaptureItems()
    {
        var facts = new List<PickupItemFact>(Items.Count);
        foreach (var pair in Items)
        {
            var item = pair.Key;
            if (TryType(item.ItemType, out var type))
                facts.Add(new(pair.Value, type, NetSession.NetFrame, item.DespawnTimer != 0 && item.TokenId == 0, item.Position));
        }
        return facts.ToArray();
    }

    internal static void ObserveContact(PlayerEntity player)
    {
        if (!Authority(player.OwningScene) || SourceFrame(player) is not uint source) return;
        Ensure();
        _claims!.Observe(player.ModPickupContactPose(source, NetSession.NetFrame), CaptureItems());
    }

    internal static PickupVerdict JudgeClaim(in PickupClaim claim, int senderSlot, uint now,
        double rtt, double jitter, Scene scene)
    {
        var request = claim;
        PickupVerdict Invalid() => new(request, PickupResult.Invalid, now, 0, 0, 0, 0);
        if (!Authority(scene) || (uint)senderSlot >= scene.Players.Items.Count) return Invalid();
        var player = scene.Players.Items[senderSlot];
        Ensure();
        TimedGrantResult Commit(in PickupClaim contact, uint decision, out TimedGrantRecord? grant)
        {
            grant = null;
            if (decision != NetSession.NetFrame || player.Health == 0 || !player.ModIsInPlay
                || !player.ModPickupContactPose(0, decision).CanPickup) return TimedGrantResult.WrongLife;
            ItemInstanceEntity? item = null;
            foreach (var pair in Items) if (pair.Value == contact.Spawn) { item = pair.Key; break; }
            if (item == null || item.DespawnTimer == 0 || item.TokenId > 0 || item.OwningScene != scene
                || !TryType(item.ItemType, out var type) || type != contact.Type) return TimedGrantResult.Gone;
            var life = ShotKey.For(senderSlot, 0);
            _ledger!.BeginLife(life, decision);
            NetGrantNetwork.BeginLife(life, _room, decision);
            // Activation belongs to current input time. The older contact proof
            // never rewrites immutable A1 source observations or native timers.
            uint? source = SourceFrame(player);
            if (source is uint observed) _ledger.ObserveInput(life, observed, decision);
            var result = _ledger.TryGrant(contact.Spawn, life, decision, source, out grant);
            if (result == TimedGrantResult.Granted)
            {
                NetPickupNetwork.PublishItem(new(contact.Spawn, type, decision, false, item.Position));
                player.ApplyNativeTimedPickup(item);
            }
            return result;
        }
        return _claims!.Judge(claim, senderSlot, ShotKey.For(senderSlot, 0),
            player.Health > 0 && player.ModIsInPlay, now, rtt, jitter, Commit);
    }

    internal static void ObserveInput(PlayerEntity player)
    {
        if (!Authority(player.OwningScene) || !player.ModIsInPlay || SourceFrame(player) is not uint source) return;
        var ledger = Ensure();
        var life = ShotKey.For(player.SlotIndex, 0);
        ledger.BeginLife(life, NetSession.NetFrame);
        NetGrantNetwork.BeginLife(life, _room, NetSession.NetFrame);
        ledger.ObserveInput(life, source, NetSession.NetFrame);
    }
    internal static void RetireSlot(int slot, TimedGrantEnd reason)
        => _ledger?.RetireSlot(slot, NetSession.NetFrame, reason);
    internal static void Advance()
    {
        if (NetSession.IsAuthority) _ledger?.Advance(NetSession.NetFrame);
    }
}
