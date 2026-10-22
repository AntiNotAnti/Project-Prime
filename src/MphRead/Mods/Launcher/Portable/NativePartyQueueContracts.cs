using System;

namespace MphRead.Mods.Launcher;

public enum NativePartyQueuePhase { None, Verifying, Waiting, Activating, Reserved, Admitting, Expired, Rejected, Cancelled }
public enum NativePartyQueueCommand { Accept, Decline, Leave }

/// <summary>Immutable presentation of one existing join attempt. A reserved seat
/// does not imply admission. Commands require the exact captured snapshot.</summary>
public sealed record NativePartyQueueSnapshot(long AttemptId, uint Revision,
    NativePartyQueuePhase Phase, string Status, int Position, int QueueLength,
    int ReservedCount, int RemainingSeconds, bool CanAccept, bool CanDecline)
{
    public static NativePartyQueueSnapshot Empty { get; } = new(0, 0,
        NativePartyQueuePhase.None, "", 0, 0, 0, 0, false, false);
    public bool Active => Phase is NativePartyQueuePhase.Verifying or NativePartyQueuePhase.Waiting
        or NativePartyQueuePhase.Activating or NativePartyQueuePhase.Reserved or NativePartyQueuePhase.Admitting;
}
