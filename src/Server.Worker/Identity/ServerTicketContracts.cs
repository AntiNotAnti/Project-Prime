using System.Net;
using MphRead.Identity;

namespace MphRead.Mods.Network;

public readonly record struct TicketIdentity(PlayerId? PlayerId, Guid TicketId, long ExpiresAt, bool TrustedObserver = false,
    Guid? GuestSessionId = null, byte? ReservedSeat = null, bool WorkerAdmission = false, byte? ReservedTeam = null);

public readonly record struct ValidatedTicketJoin(IPEndPoint Endpoint, JoinPacket Join, TicketIdentity? Identity);

/// <summary>One bounded admission authority owned by one match or standalone host.</summary>
public interface IServerTicketAuthority : IDisposable
{
    Guid ServerId { get; }
    Guid SessionId { get; }
    bool RequireTickets { get; }
    bool Submit(IPEndPoint endpoint, in JoinPacket join);
    bool TryRead(out ValidatedTicketJoin result);
}
