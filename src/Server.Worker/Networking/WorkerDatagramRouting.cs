using System;

namespace MphRead.Mods.Network;

/// <summary>Parsed routing metadata, not a new wire format. Zero WireMatchId means absent on an established header.</summary>
public readonly record struct WorkerDatagramRoute(uint WireMatchId, ulong ConnectionId, bool IsJoin);

/// <summary>Routing parser seam; ticket authentication remains at admission. Reject malformed framing here.</summary>
public interface IWorkerDatagramRouter
{
    bool TryRoute(ReadOnlySpan<byte> datagram, out WorkerDatagramRoute route);
    uint? LegacySingleMatchId => null;
}

/// <summary>
/// Legacy clients omit WireMatchId. This explicit compatibility adapter is restricted to one match.
/// Established InputBundle match/phase validation remains in ServerNetwork, before ACK/state mutation.
/// Current headers have no incarnation field; connection registrations belong to the hub incarnation.
/// </summary>
public sealed class LegacySingleMatchRouter : IWorkerDatagramRouter
{
    public uint? LegacySingleMatchId { get; }
    public LegacySingleMatchRouter(uint wireMatchId)
    {
        if (wireMatchId == 0) throw new ArgumentOutOfRangeException(nameof(wireMatchId));
        LegacySingleMatchId = wireMatchId;
    }
    public bool TryRoute(ReadOnlySpan<byte> datagram, out WorkerDatagramRoute route)
    {
        route = default;
        if (datagram.Length == 2 && datagram[0] == (byte)PacketType.StatusQuery)
        { route = new(LegacySingleMatchId!.Value, 0, true); return true; }
        if (!NetHeader.TryRead(datagram, out NetHeader header)) return false;
        if (header.Type == NetMessageType.Join)
        {
            if (!JoinPacket.TryRead(datagram[NetHeader.Size..], out JoinPacket join)
                || join.Protocol != NetHeader.Version || (join.WireMatchId != 0 && join.WireMatchId != LegacySingleMatchId)) return false;
            route = new(LegacySingleMatchId!.Value, 0, true);
        }
        else route = new(0, header.ConnectionId, false);
        return true;
    }
}

/// <summary>Optional transport lifecycle used by ServerNetwork without changing reliable protocol behavior.</summary>
public interface IMatchConnectionRoutes
{
    ulong AllocateConnectionId();
    void RemoveConnection(ulong connectionId);
}

/// <summary>Production routed joins use JoinPacket's explicit uint WireMatchId extension.</summary>
public sealed class RoutedMatchDatagramRouter : IWorkerDatagramRouter
{
    public bool TryRoute(ReadOnlySpan<byte> datagram, out WorkerDatagramRoute route)
    {
        route = default;
        if (!NetHeader.TryRead(datagram, out NetHeader header)) return false;
        if (header.Type == NetMessageType.Join)
        {
            if (!JoinPacket.TryRead(datagram[NetHeader.Size..], out JoinPacket join)
                || join.Protocol != NetHeader.Version || join.WireMatchId == 0) return false;
            route = new(join.WireMatchId, 0, true);
        }
        else route = new(0, header.ConnectionId, false);
        return true;
    }
}
