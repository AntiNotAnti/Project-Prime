using System;

namespace MphRead.Mods.Launcher;

/// <summary>Backend intent only. Reserved also requires a current live-server
/// hold; this record never establishes admission.</summary>
public sealed record NativePartyReservation(string RequestId, long OperationGeneration,
    long PartyRevision, string TravelId, int TravelRevision, ushort MatchId,
    ulong AuthorityEpoch, DateTimeOffset ExpiresAt, string Status, int RequestedCount,
    bool CancellationPending)
{
    public bool Active => Status is "pending" or "reserved";
}
public static class NativePartyReservationValidation
{
    public static bool Valid(NativePartyReservation value)
        => Guid.TryParse(value.RequestId, out var request) && request != Guid.Empty
        && value.OperationGeneration == 1 && value.PartyRevision is >= 0 and <= 9007199254740991
        && Guid.TryParse(value.TravelId, out var travel) && travel != Guid.Empty && value.TravelRevision is >= 1 and <= 1000000
        && value.MatchId != 0 && value.AuthorityEpoch != 0 && value.RequestedCount is >= 1 and <= 7
        && value.ExpiresAt > DateTimeOffset.UnixEpoch && value.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(3)
        && value.Status is "pending" or "reserved" or "cancelled" or "expired" or "rejected" or "completed";
}
