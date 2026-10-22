using System;
using System.Collections.Generic;
using System.Net;

namespace MphRead.Mods.Network;

public sealed record NetEndpointBudgetSnapshot(string Endpoint, NetConnectionSnapshot Connection,
    NetReliableSnapshot Reliable, NetWireSnapshot Wire);
public sealed record NetTransportBudgetSnapshot(NetWireSnapshot Wire, NetTransportSnapshot Queue,
    NetTransportContentionSnapshot Contention, NetEndpointBudgetSnapshot[] Endpoints)
{
    public object? Cosmetics { get; init; }
    public long IntentByteRejections { get { long total = 0; foreach (var endpoint in Endpoints) total += endpoint.Connection.IntentByteRejections; return total; } }
}
public readonly record struct NetAdmissionBudget(string Lane, int PacketsPerSecond, int Burst, int? BytesPerSecond = null, int? ByteBurst = null)
{
    // Upper bound for one shared lane, not a target or a separate per-type allowance.
    public int MaximumDatagramBytes => NetConfig.MaxPacketSize;
    public long MaximumBytesPerSecond => BytesPerSecond ?? (long)PacketsPerSecond * MaximumDatagramBytes;
}

public sealed partial class NetTransport
{
    public static NetAdmissionBudget[] AdmissionBudgets => new[]
    {
        new NetAdmissionBudget("Intent", NetConnection.IntentRate, NetConnection.IntentBurst, NetConnection.IntentBytesPerSecond, NetConnection.IntentByteBurst),
        new NetAdmissionBudget("Realtime", NetConnection.StateRate, NetConnection.StateBurst),
        new NetAdmissionBudget("Critical", NetConnection.ControlRate, NetConnection.ControlBurst),
        new NetAdmissionBudget("Background", NetConnection.BackgroundRate, NetConnection.BackgroundBurst),
        new NetAdmissionBudget("Admitted authority facts", NetConnection.AuthorityFactRate, NetConnection.AuthorityFactBurst),
        new NetAdmissionBudget("Optional cosmetic impacts", NetConnection.CosmeticRate, NetConnection.CosmeticBurst,
            NetConnection.CosmeticBytes, NetConnection.CosmeticByteBurst),
        new NetAdmissionBudget("Discovery (shared transport)", 300, 128)
    };

    private void RecordWireReceived(IPEndPoint? sender, ReadOnlySpan<byte> datagram)
    {
        // The aggregate includes malformed/discovery/unknown traffic. Endpoint
        // counters exist only for admitted connections: no attacker-keyed table.
        // Count at the socket, before injected loss/duplication, exactly once.
        Wire.Received(datagram);
        if (sender == null || !NetHeader.TryRead(datagram, out var header)) return;
        long stamp = EnterConnectionLock();
        try
        {
            if (_connections.TryGetValue(sender, out var peer) && peer.Accepts(sender, header))
                peer.Wire.Received(datagram);
        }
        finally { ExitConnectionLock(stamp); }
    }

    private void RecordDequeued(in ReceivedPacket packet, long now)
    {
        Wire.Dequeued(packet.Type, packet.WireArrivedAt, now);
        packet.Wire?.Dequeued(packet.Type, packet.WireArrivedAt, now);
    }

    public NetTransportBudgetSnapshot CaptureBudget()
    {
        // Snapshot small connection metadata under the existing lock; format
        // addresses and allocate category/histogram reports after releasing it.
        var peers = new List<(IPEndPoint Endpoint, NetConnectionSnapshot Connection,
            NetReliableSnapshot Reliable, NetWireTelemetry Wire)>();
        long stamp = EnterConnectionLock();
        try
        {
            foreach (var peer in _connections.Values)
                peers.Add((peer.Endpoint, peer.Capture(), peer.Reliable.Capture(NowMilliseconds), peer.Wire));
        }
        finally { ExitConnectionLock(stamp); }
        var endpoints = new NetEndpointBudgetSnapshot[peers.Count];
        for (int i = 0; i < peers.Count; i++)
        {
            var peer = peers[i];
            endpoints[i] = new(peer.Endpoint.ToString(), peer.Connection, peer.Reliable, peer.Wire.Capture());
        }
        object cosmetics;
        lock (_heldLock) cosmetics = new { Queued = _liveInbox.CosmeticCount,
            HighWater = _liveInbox.CosmeticHighWater, Drops = CosmeticDrops,
            HeldIn = _cosmeticHeldIn.Count, HeldOut = _cosmeticHeldOut.Count,
            HeldInDrops = _cosmeticHeldIn.Dropped, HeldOutDrops = _cosmeticHeldOut.Dropped };
        return new(Wire.Capture(), Telemetry.Capture(), ContentionStats(), endpoints) { Cosmetics = cosmetics };
    }
}
