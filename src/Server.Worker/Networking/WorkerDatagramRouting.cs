using System;
using System.Threading;

namespace MphRead.Mods.Network;

/// <summary>Parsed routing metadata, not a new wire format. Zero WireMatchId means absent on an established header.</summary>
public readonly record struct WorkerDatagramRoute(uint WireMatchId, ulong ConnectionId, bool IsJoin,
    Guid AdmissionId = default);

/// <summary>
/// Stable Worker-only state for one established connection route. The hub is
/// the sole ingress writer; queued reservations are released by the exact
/// object retained in each match mailbox rather than by re-looking up an ID.
/// </summary>
internal sealed class ConnectionRoute
{
    internal const int MaximumQueued = 64;

    public MatchDatagramTransport Match { get; }
    private NetRateLimit _ingress;
    private int _queued;

    public ConnectionRoute(MatchDatagramTransport match, double now)
    {
        Match = match;
        _ingress = new NetRateLimit(240, 360, now);
    }

    public bool TakeIngress(double now) => _ingress.Take(now);

    public bool TryReserve()
    {
        while (true)
        {
            int current = Volatile.Read(ref _queued);
            if (current >= MaximumQueued) return false;
            if (Interlocked.CompareExchange(ref _queued, current + 1, current) == current)
                return true;
        }
    }

    public void ReleaseReservation()
    {
        int remaining = Interlocked.Decrement(ref _queued);
        if (remaining < 0)
        {
            Interlocked.Increment(ref _queued);
            throw new InvalidOperationException("Connection ingress reservation underflow.");
        }
    }

    public int Queued => Volatile.Read(ref _queued);
}

internal readonly struct RoutedReceivedPacket
{
    public readonly ReceivedPacket Packet;
    public readonly ConnectionRoute? Route;

    public RoutedReceivedPacket(in ReceivedPacket packet, ConnectionRoute? route)
    {
        Packet = packet;
        Route = route;
    }

    public void ReleaseReservation() => Route?.ReleaseReservation();
}

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
            ReadOnlySpan<byte> body = datagram[NetHeader.Size..];
            ReadOnlySpan<byte> unsignedBody = body.Length >= NetAuthentication.TagSize
                ? body[..^NetAuthentication.TagSize] : body;
            if (JoinPacket.TryReadAdmissionId(unsignedBody, out Guid admissionId))
            {
                route = new(0, 0, true, admissionId);
                return true;
            }
            // The explicit disabled/test seam still routes a legacy join by
            // its wire match. Enabled workers reject this fallback in the hub.
            if (!JoinPacket.TryRead(body, out JoinPacket join)
                || join.Protocol != NetHeader.Version || join.WireMatchId == 0) return false;
            route = new(join.WireMatchId, 0, true);
        }
        else route = new(0, header.ConnectionId, false);
        return true;
    }
}
