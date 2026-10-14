using System;
using System.Collections.Generic;
using MphRead.Entities;

namespace MphRead.Mods.Network;

/// <summary>Event-driven timed effects, independent of the fast player lane.</summary>
internal static class NetGrantNetwork
{
    private static NetRetainedDelivery _delivery = new(2048);
    private static readonly TimedGrantFact?[,] Latest = new TimedGrantFact?[8, 3];
    private static uint _revision;
    internal static TimedGrantPresentation Presentation { get; private set; } = new();
    internal sealed class Peer
    {
        internal NetRetainedDelivery Store = null!;
        internal NetRetainedDelivery.Cursor Cursor = null!;
        internal readonly Queue<byte[]> Baseline = new();
        internal double Since = double.NaN;
    }
    internal static void Reset()
    { _delivery = new(2048); Array.Clear(Latest); _revision = 0; Presentation = new(); }
    private static uint Revision()
    {
        if (_revision == uint.MaxValue) throw new ProgramException("Timed grant revision exhausted.");
        return ++_revision;
    }
    internal static void BeginLife(ShotKey life, uint room, uint now)
    {
        if (!NetSession.IsAuthority) return;
        for (int type = 0; type < 3; type++)
            if (Latest[life.ShooterSlot, type] is not { } fact || fact.Life != life)
                Publish(new(life, room, Revision(), (TimedPowerUp)type, 0, -1, 0, 0, 0, TimedGrantEnd.Retired, now));
    }
    internal static void Changed(TimedGrantRecord grant)
    {
        NetCampaignEvidence.TimedGrant(grant);
        Publish(new(grant.Life, grant.Spawn.Room, Revision(), grant.Type, grant.Sequence,
            grant.Spawn.SpawnId, grant.Spawn.Generation, grant.ContactFrame, grant.EndFrame,
            grant.EndReason, NetSession.NetFrame));
    }
    private static void Publish(TimedGrantFact fact)
    {
        if (!fact.IsValid) throw new ProgramException("Invalid authority timed grant transition.");
        Latest[fact.Life.ShooterSlot, (int)fact.Type] = fact;
        Span<byte> bytes = stackalloc byte[TimedGrantFact.Size]; fact.Write(bytes);
        _delivery.Append(PacketType.TimedGrant, bytes);
        ReplayCapture.AcceptTimedGrant(fact);
        NetCampaignEvidence.TimedGrantState(fact, "authority");
    }
    internal static Peer Join()
    {
        // Refresh the authority clock and revision at bootstrap. Existing peers
        // see the same immutable revision; reconnect never rewrites a grant start.
        for (int slot = 0; slot < 8; slot++) for (int type = 0; type < 3; type++)
            if (Latest[slot, type] is { } fact)
                Publish(fact with { Revision = Revision(), AuthorityFrame = NetSession.NetFrame, Resume = true });
        var peer = new Peer { Store = _delivery, Cursor = _delivery.Join() };
        var baseline = new List<TimedGrantFact>();
        foreach (var fact in Latest) if (fact.HasValue) baseline.Add(fact.Value);
        int perPacket = NetFactBatch.MaximumPayload / TimedGrantFact.Size;
        for (int at = 0; at < baseline.Count; at += perPacket)
        {
            int count = Math.Min(perPacket, baseline.Count - at);
            byte[] bytes = new byte[count * TimedGrantFact.Size];
            for (int i = 0; i < count; i++) baseline[at + i].Write(bytes.AsSpan(i * TimedGrantFact.Size, TimedGrantFact.Size));
            peer.Baseline.Enqueue(bytes);
        }
        if (peer.Baseline.Count != 0) peer.Since = NetPickupNetwork.Now;
        return peer;
    }
    internal static int Pending(Peer peer) => ReferenceEquals(peer.Store, _delivery)
        ? peer.Baseline.Count + peer.Store.Pending(peer.Cursor) : 1;
    internal static bool Pump(Peer peer, NetRetainedDelivery.BatchSink send)
    {
        if (!ReferenceEquals(peer.Store, _delivery)) return false;
        if (Pending(peer) == 0) peer.Since = double.NaN;
        else if (double.IsNaN(peer.Since)) peer.Since = NetPickupNetwork.Now;
        else if (NetPickupNetwork.Now - peer.Since >= NetPickupNetwork.DeadlineSeconds) return false;
        while (peer.Baseline.TryPeek(out var bytes))
        { if (!send(PacketType.TimedGrant, bytes)) return true; peer.Baseline.Dequeue(); }
        if (!peer.Store.PumpBatched(peer.Cursor, send, budget: 64)) return false;
        // A new transition may precede every pump. Retire a completed delivery
        // deadline here, rather than requiring a later pump to begin empty.
        if (Pending(peer) == 0) peer.Since = double.NaN;
        return true;
    }
    internal static bool Receive(TimedGrantFact fact)
    {
        if (!fact.IsValid || !NetSession.MatchesStream(fact.Life.MatchId, fact.Life.AuthorityEpoch)) return false;
        try
        {
            if (!Presentation.Accept(fact, NetSession.NetFrame)) return false;
        }
        catch (System.IO.InvalidDataException)
        { NetPickupNetwork.ClientFailure?.Invoke("Authority grant state contradicted its retained revision."); return false; }
        ReplayCapture.AcceptTimedGrant(fact);
        NetCampaignEvidence.TimedGrantState(fact, "received"); return true;
    }
    internal static bool ObserveItemRoom(PickupItemFact fact)
        => fact.IsValid && NetSession.MatchesStream(fact.Spawn.Match, fact.Spawn.Epoch)
            && Presentation.ObserveRoom(fact.Spawn.Epoch, fact.Spawn.Match, fact.Spawn.Room);
    internal static void BeforePlayer(PlayerEntity player)
    {
        if (!NetSession.IsClient || NetSession.IsAuthority || player.OwningScene.Services.IsReplica) return;
        uint frame = NetSession.NetFrame;
        if (NetSession.LastSnapshotFrame != 0)
            Presentation.ObserveClock(unchecked(NetSession.LastSnapshotFrame + (uint)NetSession.SnapshotAge), frame);
        Presentation.Advance(frame, life => NetPlayerLifecycle.Matches(life.ShooterSlot, life.Generation, life.LifeId)
            && life.ShooterSlot < player.OwningScene.Players.Items.Count
            && player.OwningScene.Players.Items[life.ShooterSlot].Health > 0);
        player.ModProjectTimedGrants(Presentation, ShotKey.For(player.SlotIndex, 0), frame, quiet: false);
    }
}
