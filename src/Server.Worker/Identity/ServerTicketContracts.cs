using System;
using System.Net;
using MphRead.Identity;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Network;

public readonly record struct TicketIdentity(PlayerId? PlayerId, Guid TicketId, long ExpiresAt, bool TrustedObserver = false,
    Guid? GuestSessionId = null, byte? ReservedSeat = null, bool WorkerAdmission = false, byte? ReservedTeam = null,
    Guid NodeSessionId = default, HandoffGeneration HandoffGeneration = default);

public readonly record struct ValidatedTicketJoin(IPEndPoint Endpoint, JoinPacket Join, TicketIdentity? Identity);

/// <summary>One bounded admission authority owned by one match or standalone host.</summary>
public interface IServerTicketAuthority : IDisposable
{
    Guid ServerId { get; }
    Guid SessionId { get; }
    bool RequireTickets { get; }
    bool Submit(IPEndPoint endpoint, in JoinPacket join);
    bool TryRead(out ValidatedTicketJoin result);

    /// <summary>Returns a temporary copy for MAC verification; callers must zero it.</summary>
    bool TryGetAdmissionKey(Guid admissionId, out byte[] key)
    {
        key = Array.Empty<byte>();
        return false;
    }

    /// <summary>Checks the public lease binding before ticket work is queued.</summary>
    bool ValidateAdmissionJoin(Guid admissionId, in JoinPacket join) => false;

    /// <summary>Checks the ticket identity against the installed lease.</summary>
    bool ValidateAdmissionIdentity(Guid admissionId, in JoinPacket join, in TicketIdentity identity) => false;
}
