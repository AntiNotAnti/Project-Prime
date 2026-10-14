using System;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Entities;

namespace MphRead.Mods.Network;

/// <summary>Owner-thread, bounded A2 discovery and claim delivery. Reliable UDP
/// retries admitted bytes; application retries recover a lost terminal response.
/// Presentation reconciliation has its own bounded state and life fences.</summary>
internal static class NetPickupNetwork
{
    internal const int PendingPerSlot = 16;
    internal const double DeadlineSeconds = 15;
    private sealed class Pending
    {
        internal PickupClaim Claim;
        internal double Created, LastSent = double.NegativeInfinity;
        internal int Sends;
        internal uint ReceivedFrame;
        internal double Rtt, Jitter;
    }
    private sealed record Answer(PickupVerdict Verdict, double Created);
    internal sealed class PeerState
    {
        internal NetGrantNetwork.Peer Grants = null!;
        internal NetRetainedDelivery Store = null!;
        internal NetRetainedDelivery.Cursor Cursor = null!;
        internal readonly Queue<byte[]> Baseline = new();
        internal double Started, PendingSince = double.NaN;
    }
    private static readonly Dictionary<PickupClaimKey, Pending> Owners = new();
    private static readonly Dictionary<PowerUpSpawnKey, PickupItemFact> KnownItems = new();
    internal static IReadOnlyDictionary<PowerUpSpawnKey, PickupItemFact> ItemFacts => KnownItems;
    private static readonly Dictionary<PickupClaimKey, Pending>[] Incoming = new Dictionary<PickupClaimKey, Pending>[8];
    private static readonly Queue<Answer>[] Answers = new Queue<Answer>[8];
    private static readonly bool[] Failed = new bool[8];
    private static readonly ushort[] DeliveryGeneration = new ushort[8];
    private static readonly bool[] CloseAfterAnswers = new bool[8];
    private static NetRetainedDelivery _discovery = new(2048);
    private static uint _sequence;
    private static uint _room;
    internal static Func<PickupClaim, bool>? SendClaim;
    internal static Action<PickupVerdict>? Terminal;
    internal static Action<string>? ClientFailure;
    internal static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    internal static int PendingCount
    {
        get
        {
            int count = Owners.Count;
            for (int i = 0; i < 8; i++) count += Incoming[i].Count + Answers[i].Count;
            return count;
        }
    }
    static NetPickupNetwork()
    { for (int i = 0; i < 8; i++) { Incoming[i] = new(); Answers[i] = new(); } }

    internal static void Reset()
    {
        NetSession.ResetPickupReadiness();
        NetGrantNetwork.Reset();
        Owners.Clear(); KnownItems.Clear(); _room = 0;
        // Preserve process claim sequence across same-match reconnect/scene reload.
        _discovery = new(2048);
        for (int i = 0; i < 8; i++) { Incoming[i].Clear(); Answers[i].Clear(); Failed[i] = false; DeliveryGeneration[i] = 0; CloseAfterAnswers[i] = false; }
    }
    internal static void ForgetSlot(int slot)
    {
        if ((uint)slot >= 8) return;
        Incoming[slot].Clear(); Answers[slot].Clear(); Failed[slot] = false; CloseAfterAnswers[slot] = false;
        DeliveryGeneration[slot] = 0;
        var remove = new List<PickupClaimKey>();
        foreach (var key in Owners.Keys) if (key.Slot == slot) remove.Add(key);
        foreach (var key in remove) Owners.Remove(key);
    }
    private static void SynchronizeSlot(int slot)
    {
        if ((uint)slot >= 8 || DeliveryGeneration[slot] == NetPlayerLifecycle.Generation(slot)) return;
        DeliveryGeneration[slot] = NetPlayerLifecycle.Generation(slot);
        Incoming[slot].Clear(); Answers[slot].Clear(); Failed[slot] = false; CloseAfterAnswers[slot] = false;
    }
    internal static bool FailedSlot(int slot)
    { SynchronizeSlot(slot); return (uint)slot < 8 && Failed[slot]; }
    internal static void PublishItem(PickupItemFact fact)
    {
        if (!NetSession.IsAuthority) return;
        Span<byte> bytes = stackalloc byte[PickupItemFact.Size]; fact.Write(bytes);
        _discovery.Append(PacketType.PickupItem, bytes);
        ReplayCapture.AcceptPickupItem(fact);
        NetCampaignEvidence.PickupItem(fact, "authority");
    }
    internal static PeerState Join()
    {
        var peer = new PeerState { Grants = NetGrantNetwork.Join(), Store = _discovery, Cursor = _discovery.Join(), Started = Now };
        var items = NetPowerUpLedger.CaptureItems();
        int perPacket = NetFactBatch.MaximumPayload / PickupItemFact.Size;
        for (int at = 0; at < items.Length; at += perPacket)
        {
            int count = Math.Min(perPacket, items.Length - at);
            byte[] bytes = new byte[count * PickupItemFact.Size];
            for (int i = 0; i < count; i++) items[at + i].Write(bytes.AsSpan(i * PickupItemFact.Size, PickupItemFact.Size));
            peer.Baseline.Enqueue(bytes);
        }
        return peer;
    }
    internal static bool Current(PeerState peer) => ReferenceEquals(peer.Store, _discovery);
    internal static int PeerPending(PeerState peer) => Current(peer) ? peer.Baseline.Count + peer.Store.Pending(peer.Cursor) + NetGrantNetwork.Pending(peer.Grants) : 1;
    internal static bool Pump(PeerState peer, int slot, NetRetainedDelivery.BatchSink send)
    {
        if (!Current(peer) || FailedSlot(slot) || !NetGrantNetwork.Pump(peer.Grants, send)) return false;
        if ((uint)slot < 8)
            foreach (var request in Incoming[slot].Values)
                if (Now - request.Created >= DeadlineSeconds) return false;
        if (peer.Baseline.Count > 0 && Now - peer.Started >= DeadlineSeconds) return false;
        if ((uint)slot < 8 && Answers[slot].TryPeek(out var oldest)
            && Now - oldest.Created >= DeadlineSeconds) return false;
        if (peer.Store.Pending(peer.Cursor) == 0) peer.PendingSince = double.NaN;
        else if (double.IsNaN(peer.PendingSince)) peer.PendingSince = Now;
        else if (Now - peer.PendingSince >= DeadlineSeconds) return false;
        while (peer.Baseline.TryPeek(out var bytes))
        { if (!send(PacketType.PickupItem, bytes)) return true; peer.Baseline.Dequeue(); }
        if (!peer.Store.PumpBatched(peer.Cursor, send, budget: 64)) return false;
        if ((uint)slot >= 8) return true;
        var queue = Answers[slot];
        Span<byte> verdictBytes = stackalloc byte[PickupVerdict.Size];
        while (queue.TryPeek(out var answer))
        {
            if (answer.Verdict.Claim.Key.Generation != NetPlayerLifecycle.Generation(slot))
            { queue.Dequeue(); continue; }
            if (Now - answer.Created >= DeadlineSeconds) return false;
            answer.Verdict.Write(verdictBytes);
            if (!send(PacketType.PickupVerdict, verdictBytes)) return true;
            queue.Dequeue();
        }
        return !CloseAfterAnswers[slot];
    }
    internal static bool Receive(int sender, PickupClaim claim, double rtt, double jitter)
    {
        if ((uint)sender >= 8 || claim.Key.Slot != sender || !claim.IsValid) return true;
        if (!NetSession.MatchesStream(claim.Key.Match, claim.Key.Epoch)) return true;
        SynchronizeSlot(sender);
        if (CloseAfterAnswers[sender]) return false;
        if (NetPowerUpLedger.Claims?.TryTerminal(claim.Key, out var cached) == true) claim = cached.Claim;
        var pending = Incoming[sender];
        if (pending.ContainsKey(claim.Key)) return true;
        if (pending.Count >= PendingPerSlot) { Failed[sender] = true; return false; }
        NetCampaignEvidence.PickupClaim(claim, "received");
        pending.Add(claim.Key, new() { Claim = claim, Created = Now, ReceivedFrame = NetSession.NetFrame,
            Rtt = rtt, Jitter = jitter });
        return true;
    }
    internal static void AfterSimulation()
    {
        if (!NetSession.IsAuthority)
        {
            PumpOwner();
            if (NetBombs.LiveScene is { } clientScene) TimedPickupProjection.ApplyDynamic(clientScene);
            return;
        }
        if (NetBombs.LiveScene is not { } scene || scene.Services.IsReplica) return;
        // Active actors arbitrate during their native pickup phase, before the
        // trailing Double Damage decrement and form/presentation maintenance.
        // Actors that no longer run that phase can only retire/refuse claims.
        for (int slot = 0; slot < 8; slot++)
            if (slot >= scene.Players.Items.Count || scene.Players.Items[slot].Health == 0
                || !scene.Players.Items[slot].ModIsInPlay) ProcessSlot(slot, scene);
    }
    internal static void ProcessPlayer(PlayerEntity player)
    {
        if (NetSession.IsAuthority && !player.OwningScene.Services.IsReplica)
            ProcessSlot(player.SlotIndex, player.OwningScene);
    }
    private static void ProcessSlot(int slot, Scene scene)
    {
        if ((uint)slot >= 8) return;
        var pending = Incoming[slot];
        if (CloseAfterAnswers[slot]) return;
        if (pending.Count == 0) return;
        var done = new List<PickupClaimKey>();
        foreach (var pair in pending)
        {
            var request = pair.Value;
            if (Now - request.Created >= DeadlineSeconds) { Failed[slot] = true; break; }
            if (Answers[slot].Count >= PendingPerSlot * 2) break;
            // Claims describe post-step contact; the following pre-step pose
            // arrives on a separate input lane. Give that actual observation
            // a finite chance to arrive, without delaying cached terminals.
            if (NetPowerUpLedger.Claims is { } claims
                && !claims.TryTerminal(request.Claim.Key, out _)
                && !claims.CanProveContact(request.Claim, NetSession.NetFrame, request.Rtt, request.Jitter)
                && unchecked((int)(NetSession.NetFrame - request.ReceivedFrame)) < 24) continue;
            var verdict = NetPowerUpLedger.JudgeClaim(request.Claim, slot, NetSession.NetFrame,
                request.Rtt, request.Jitter, scene);
            ReplayCapture.AcceptPickupVerdict(verdict);
            NetCampaignEvidence.PickupVerdict(verdict, "authority");
            Answers[slot].Enqueue(new(verdict, Now));
            done.Add(pair.Key);
            if (verdict.Result == PickupResult.Capacity
                && NetPowerUpLedger.Claims?.TryTerminal(pair.Key, out _) != true)
            { CloseAfterAnswers[slot] = true; break; }
        }
        foreach (var key in done) pending.Remove(key);
        if (CloseAfterAnswers[slot]) pending.Clear();
    }

    internal static bool ReceiveItem(PickupItemFact fact)
    {
        if (!NetSession.MatchesStream(fact.Spawn.Match, fact.Spawn.Epoch) || !fact.IsValid) return false;
        if (_room != 0 && fact.Spawn.Room < _room) return false;
        if (!NetGrantNetwork.ObserveItemRoom(fact)) return false;
        if (_room != fact.Spawn.Room)
        { KnownItems.Clear(); Owners.Clear(); _room = fact.Spawn.Room; }
        var accepted = PickupItemHistory.Accept(KnownItems, fact);
        if (accepted == PickupItemAcceptance.Unchanged) return false;
        if (accepted is PickupItemAcceptance.Conflict or PickupItemAcceptance.Capacity)
        { ClientFailure?.Invoke("Pickup discovery exceeded or contradicted its recovery budget."); return false; }
        ReplayCapture.AcceptPickupItem(fact);
        NetCampaignEvidence.PickupItem(fact, "received");
        return true;
    }
    internal static void OwnerContact(PlayerEntity player, ItemInstanceEntity item)
    {
        if (!NetSession.IsClient || NetSession.IsAuthority || player.OwningScene.Services.IsReplica
            || player.SlotIndex != NetSession.LocalSlot || !NetSession.WorldIsReady
            || !NetPowerUpLedger.TryType(item.ItemType, out var type)) return;
        if (item.ModPickupSpawn is not { } key || !KnownItems.TryGetValue(key, out var known)
            || !known.Available || known.Type != type || NetGrantNetwork.Presentation.Hides(key, NetSession.NetFrame)) return;
        foreach (var pending in Owners.Values)
            if (pending.Claim.Spawn == known.Spawn && pending.Claim.Key.LifeKey == ShotKey.For(player.SlotIndex, 0)) return;
        if (Owners.Count >= PendingPerSlot || _sequence == uint.MaxValue)
        { ClientFailure?.Invoke("Pickup claim capacity exceeded; reconnect for fresh admission."); return; }
        var life = ShotKey.For(player.SlotIndex, 0);
        var claim = new PickupClaim(new(life.AuthorityEpoch, life.MatchId, life.ShooterSlot,
            life.Generation, life.LifeId, ++_sequence), known.Spawn, type, NetSession.NetFrame, player.Position);
        if (!claim.IsValid) return;
        Owners.Add(claim.Key, new() { Claim = claim, Created = Now });
        NetCampaignEvidence.PickupClaim(claim, "offered");
        if (NetGrantNetwork.Presentation.Predict(claim, NetSession.NetFrame))
        {
            ReplayCapture.AcceptPickupPrediction(claim, NetSession.NetFrame);
            item.ModPickupHidden = true;
            player.ModProjectTimedGrants(NetGrantNetwork.Presentation, life, NetSession.NetFrame, quiet: false);
        }
    }
    internal static void PumpOwner()
    {
        if (!NetSession.IsClient || NetSession.IsAuthority) return;
        var retired = new List<PickupClaimKey>();
        foreach (var pair in new List<KeyValuePair<PickupClaimKey, Pending>>(Owners))
        {
            var pending = pair.Value;
            // Death retires presentation, not the delivery obligation for an
            // already offered claim. Keep its exact life until the terminal or
            // deadline; only a replacement connection loses this ownership.
            if (NetPlayerLifecycle.Generation(pair.Key.Slot) != pair.Key.Generation)
            { retired.Add(pair.Key); continue; }
            double now = Now;
            if (now - pending.Created >= DeadlineSeconds)
            { ClientFailure?.Invoke("Pickup verdict delivery exceeded its deadline."); return; }
            if (pending.Sends >= 6 || now - pending.LastSent < .5) continue;
            if (SendClaim?.Invoke(pending.Claim) == true)
            { pending.LastSent = now; pending.Sends++; NetCampaignEvidence.PickupClaim(pending.Claim, "sent"); }
        }
        foreach (var key in retired) Owners.Remove(key);
    }
    internal static bool ReceiveVerdict(PickupVerdict verdict)
    {
        if (!verdict.IsValid || !NetSession.MatchesStream(verdict.Claim.Key.Match, verdict.Claim.Key.Epoch)
            || verdict.Claim.Key.Slot != NetSession.LocalSlot
            || !Owners.TryGetValue(verdict.Claim.Key, out var pending) || pending.Claim != verdict.Claim) return false;
        Owners.Remove(verdict.Claim.Key);
        NetGrantNetwork.Presentation.Settle(verdict, NetSession.NetFrame);
        ReplayCapture.AcceptPickupVerdict(verdict);
        NetCampaignEvidence.PickupVerdict(verdict, "received");
        Terminal?.Invoke(verdict); return true;
    }
}
