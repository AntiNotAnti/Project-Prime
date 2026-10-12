using System;
using System.Collections.Generic;
using MphRead.Entities;

namespace MphRead.Mods.Network;

public static partial class NetSession
{
    internal static SnapshotSink? TurretOutcomeSink;
    private static NetRetainedDelivery _hostTurretDelivery = new(2048);
    private static readonly Queue<TurretFireOutcome> TurretOutcomes = new();
    private static readonly Dictionary<ShotKey, TurretFireOutcome> TurretTerminals = new();
    private static readonly Queue<ShotKey> TurretTerminalOrder = new();
    internal static Func<int>? TurretDeliveryPending;
    internal static int PendingTurretOutcomes
    {
        get
        {
            int pending = TurretOutcomes.Count + (TurretDeliveryPending?.Invoke() ?? 0);
            if (IsHost) foreach (var peer in _peers)
                if (peer.TurretCursor != null) pending += _hostTurretDelivery.Pending(peer.TurretCursor);
            return pending;
        }
    }
    internal static void ResetTurretOutcomes()
    {
        TurretOutcomes.Clear(); TurretTerminals.Clear(); TurretTerminalOrder.Clear();
        _hostTurretDelivery = new(2048);
        foreach (var peer in _peers) peer.TurretCursor = _hostTurretDelivery.Join();
    }
    // Transport also deduplicates reliable envelopes. Retain full terminal identities
    // across life changes so queued old-life outcomes cannot replay an emission.
    private static bool RememberTurretOutcome(in TurretFireOutcome fact)
    {
        if (TurretTerminals.TryGetValue(fact.Key, out var previous))
        {
            if (previous != fact) throw new InvalidOperationException("Conflicting authority turret outcomes.");
            return false;
        }
        if (TurretTerminalOrder.Count >= 4096) TurretTerminals.Remove(TurretTerminalOrder.Dequeue());
        TurretTerminals.Add(fact.Key, fact); TurretTerminalOrder.Enqueue(fact.Key); return true;
    }
    internal static void PublishTurretOutcome(in TurretFireOutcome fact)
    {
        if (!IsAuthority) return;
        Span<byte> payload = stackalloc byte[TurretFireOutcome.Size]; fact.Write(payload);
        if (!RememberTurretOutcome(fact)) return;
        NetCampaignEvidence.TurretOutcome(fact, "authority");
        ReplayCapture.AcceptTurretOutcome(fact);
        if (IsHost) _hostTurretDelivery.Append(PacketType.TurretFireOutcome, payload);
        else TurretOutcomeSink?.Invoke(payload);
    }
    internal static void RefuseTurret(ShotKey key, FireEvent fire, TurretFireOutcomeReason reason)
        => PublishTurretOutcome(new(key, NetFrame, TurretFireOutcomeStatus.Refused, reason, fire, default, default));
    private static void HandleTurretOutcome(ReceivedPacket packet)
    {
        if (!WorldIsReady || !NetFactBatch.Validate(PacketType.TurretFireOutcome, packet.Payload)) return;
        for (int at = 0; at < packet.Payload.Length; at += TurretFireOutcome.Size)
        {
            if (!TurretFireOutcome.TryRead(packet.Payload.Slice(at, TurretFireOutcome.Size), out var fact)
                || fact.Key.AuthorityEpoch != AuthorityEpoch || fact.Key.MatchId != CurrentMatchId) continue;
            if (!RememberTurretOutcome(fact)) continue;
            if (TurretOutcomes.Count >= 2048)
            { LastError = "Turret outcome queue exceeded recovery budget."; Stop(); return; }
            TurretOutcomes.Enqueue(fact);
        }
    }
    internal static void ApplyTurretOutcomes(Scene scene)
    {
        if (!WorldIsReady || FreezeGameplay) return;
        while (TurretOutcomes.TryDequeue(out var fact))
        {
            ReplayCapture.AcceptTurretOutcome(fact);
            NetCampaignEvidence.TurretOutcome(fact, "received");
            int slot = fact.Key.ShooterSlot;
            if (fact.Status == TurretFireOutcomeStatus.Refused)
            {
                int cancelled = 0;
                foreach (var beam in scene.GetBeamProjectileEntities())
                    if (beam.Lifespan > 0 && beam.ModLaunchKey == fact.Key)
                    { beam.Destroy(); cancelled++; }
                NetCampaignEvidence.TurretOutcome(fact, "refusal-applied", cancelled);
            }
            else if (slot != LocalSlot)
            {
                // Retired occupants cannot borrow the replacement's entity.
                if (NetPlayerLifecycle.Generation(slot) != fact.Key.Generation || slot >= scene.Players.Items.Count) continue;
                var player = scene.Players.Items[slot];
                var fire = fact.Fire with { Origin = fact.EmissionOrigin, Direction = fact.EmissionDirection };
                if (NetFireEvents.PlayAccepted(player, fact.Key, fire) == BeamResultFlags.NoSpawn)
                { LastError = "Authority turret emission could not be presented."; Stop(); return; }
            }
        }
    }
    private static void PumpHostTurretOutcomes()
    {
        for (int i = _peers.Count - 1; i >= 0; i--)
        {
            var peer = _peers[i];
            if (peer.TurretCursor == null) continue;
            if (!_hostTurretDelivery.PumpBatched(peer.TurretCursor, (type, payload) =>
                _transport?.TrySendSemantic(peer.EndPoint, type, payload) == true))
            {
                _transport?.Send(peer.EndPoint, PacketType.Bye, ReadOnlySpan<byte>.Empty);
                _peers.RemoveAt(i); NetPlayerLifecycle.SetOccupant(peer.SlotIndex, 0);
                BroadcastHostControl();
            }
        }
    }
}
