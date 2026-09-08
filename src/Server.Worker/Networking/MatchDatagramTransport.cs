using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace MphRead.Mods.Network;

/// <summary>Bounded mailbox transport. One simulation reader; sends are copied once and flushed by the hub.</summary>
public sealed class MatchDatagramTransport : INetTransport, IMatchConnectionRoutes
{
    private readonly WorkerNetworkHub _hub;
    private readonly object _gate = new();
    private readonly Queue<ReceivedPacket> _inbound = new();
    private readonly Queue<Outbound> _control = new();
    private readonly Queue<Outbound> _updates = new();
    private NetKeepAlive[] _keepAlives = Array.Empty<NetKeepAlive>();
    private long _nextKeepAlive;
    private bool _disposed;
    private int _reading;
    private readonly int _capacity;
    private readonly int _budget;
    private sealed record Outbound(IPEndPoint Target, byte[] Bytes, long HoldTicks);
    public uint WireMatchId { get; }
    public int MaxConnections { get; }
    public Guid WorkerIncarnation { get; }
    public int LocalPort => _hub.LocalPort;
    public NetTrafficMetrics Metrics { get; } = new();
    public long PacketsDropped => Metrics.QueueDrops;
    public int QueuedPackets { get { lock (_gate) return _inbound.Count; } }
    public int HeldIncomingPackets => 0;
    public int HeldOutgoingPackets { get { lock (_gate) return _control.Count + _updates.Count; } }

    internal MatchDatagramTransport(WorkerNetworkHub hub, uint wireMatchId, int capacity, int budget, int maxConnections)
    {
        if (capacity is < 1 or > 65536 || budget < 1 || budget > capacity) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maxConnections is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(maxConnections));
        MaxConnections = maxConnections;
        _hub = hub; WireMatchId = wireMatchId; WorkerIncarnation = hub.Incarnation; _capacity = capacity; _budget = budget;
    }
    public ulong AllocateConnectionId() => _hub.AllocateConnection(this, WorkerIncarnation);
    public void RemoveConnection(ulong connectionId) => _hub.RemoveConnection(this, connectionId);

    internal bool Enqueue(in ReceivedPacket packet)
    {
        lock (_gate)
        {
            if (_disposed || _inbound.Count >= _capacity) { Metrics.DropQueued(); return false; }
            // Physical Drain transfers ownership of the datagram array; no receive-side recopy.
            _inbound.Enqueue(packet); Metrics.Received(packet.Length); return true;
        }
    }

    public IEnumerable<ReceivedPacket> Drain()
    {
        if (Interlocked.Exchange(ref _reading, 1) != 0) throw new InvalidOperationException("Match transport has one simulation reader.");
        try
        {
            for (int i = 0; i < _budget; i++)
            {
                ReceivedPacket packet;
                lock (_gate)
                {
                    if (_disposed || _inbound.Count == 0) yield break;
                    packet = _inbound.Dequeue();
                }
                yield return packet;
            }
        }
        finally { Volatile.Write(ref _reading, 0); }
    }

    public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> payload, long extraHoldTicks = 0)
    {
        if (payload.Length >= NetConfig.MaxPacketSize) { Metrics.Reject(); return; }
        Span<byte> bytes = stackalloc byte[payload.Length + 1]; bytes[0] = (byte)type; payload.CopyTo(bytes[1..]);
        SendDatagram(target, bytes, extraHoldTicks);
    }
    public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram) => SendDatagram(target, datagram, 0);
    public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram, long extraHoldTicks)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (datagram.IsEmpty || datagram.Length > NetConfig.MaxPacketSize || extraHoldTicks < 0) { Metrics.Reject(); return; }
        lock (_gate)
        {
            if (_disposed) return;
            bool update = NetHeader.TryRead(datagram, out var header) && header.Type is NetMessageType.Snapshot or NetMessageType.World;
            if (_control.Count + _updates.Count >= _capacity)
            {
                Metrics.DropQueued();
                if (update || _updates.Count == 0) return;
                _updates.Dequeue(); // Preserve reliable/control work under snapshot pressure.
            }
            var item = new Outbound(new IPEndPoint(target.Address, target.Port), datagram.ToArray(), extraHoldTicks);
            (update ? _updates : _control).Enqueue(item);
        }
    }

    internal void Flush(INetTransport physical, NetTrafficMetrics hubMetrics)
    {
        lock (_gate)
        {
            if (_disposed) return;
            for (int i = 0; i < _budget && _control.Count + _updates.Count > 0; i++)
            {
                var item = (_control.Count > 0 ? _control : _updates).Dequeue();
                SendPhysical(physical, hubMetrics, item.Target, item.Bytes, item.HoldTicks);
            }
            long now = Stopwatch.GetTimestamp();
            if (now >= _nextKeepAlive)
            {
                _nextKeepAlive = now + Stopwatch.Frequency;
                foreach (var keepAlive in _keepAlives) SendPhysical(physical, hubMetrics, keepAlive.Endpoint, keepAlive.Datagram.Span, 0);
            }
        }
    }
    private void SendPhysical(INetTransport physical, NetTrafficMetrics hubMetrics, IPEndPoint target, ReadOnlySpan<byte> bytes, long hold)
    {
        try { physical.SendDatagram(target, bytes, hold); Metrics.Sent(bytes.Length); hubMetrics.Sent(bytes.Length); }
        catch (System.Net.Sockets.SocketException) { Metrics.SendFailed(); hubMetrics.SendFailed(); }
    }

    public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> datagram = default)
    {
        if (target == null) SetKeepAlives(ReadOnlySpan<NetKeepAlive>.Empty);
        else SetKeepAlives(new[] { new NetKeepAlive(target, datagram.ToArray()) });
    }
    public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries)
    {
        if (entries.Length > MaxConnections) throw new ArgumentOutOfRangeException(nameof(entries));
        var copies = new NetKeepAlive[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.Endpoint == null || entry.Datagram.Length != NetHeader.Size || !NetHeader.TryRead(entry.Datagram.Span, out var header) || header.Type != NetMessageType.KeepAlive)
                throw new ArgumentException("Invalid authoritative keepalive.", nameof(entries));
            copies[i] = new(new IPEndPoint(entry.Endpoint.Address, entry.Endpoint.Port), entry.Datagram.ToArray());
        }
        lock (_gate) { if (!_disposed) _keepAlives = copies; }
    }
    public void AnswerPingsImmediately() { /* The physical receive owner answers legacy pings. */ }
    public void EnqueueForPlayback(byte[] data, int length) => throw new NotSupportedException("Playback cannot bypass worker routing.");
    internal void CloseFromHub()
    {
        lock (_gate) { _disposed = true; _inbound.Clear(); _control.Clear(); _updates.Clear(); _keepAlives = Array.Empty<NetKeepAlive>(); }
    }
    public void Dispose() { CloseFromHub(); _hub.Unregister(this); }
}
