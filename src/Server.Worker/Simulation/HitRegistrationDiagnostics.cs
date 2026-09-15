using System;

namespace MphRead.Mods.Network;

/// <summary>
/// Server-side adapter for the shared hit-registration diagnostic DTO. Reads
/// the existing bounded lag-compensation telemetry and never runs a collision
/// query, changes gameplay state, or schedules work.
/// </summary>
public static class ServerHitRegistrationDiagnostics
{
    public static HitRegistrationAuthorityDiagnostic Capture(ServerCombat combat)
    {
        ArgumentNullException.ThrowIfNull(combat);

        bool hasTiming = combat.ShotsEligible > 0
            || combat.RequestedRewindTicks.Count > 0
            || combat.ValidatedRewindTicks.Count > 0;
        long historyMisses = Math.Max(0, combat.History.Missing);
        long clampHistoryUnavailable = Math.Max(0, combat.ClampHistoryUnavailable);
        bool hasHistoryLookup = combat.History.Queries > 0;
        bool hasHistoryObservation = hasHistoryLookup || clampHistoryUnavailable > 0;
        bool available = hasTiming || hasHistoryObservation;
        if (!available)
            return HitRegistrationAuthorityDiagnostic.Unavailable;

        double? requested = combat.RequestedRewindTicks.Count == 0
            ? null : combat.RequestedRewindTicks.Mean;
        double? served = combat.ValidatedRewindTicks.Count == 0
            ? null : combat.ValidatedRewindTicks.Mean;
        double? positionError = combat.ClampPositionError.Count == 0
            ? null : combat.ClampPositionError.Mean;
        long? zeroRewind = hasTiming
            ? Math.Max(0, combat.ShotsEligible - combat.ShotsRewound) : null;
        return new(HitRegistrationDiagnosticAvailability.Available, combat.Tick,
            requested, served, hasTiming ? combat.ShotsClamped > 0 : null,
            hasHistoryLookup ? historyMisses > 0 : null, positionError,
            hasTiming ? combat.ShotsClamped : null,
            hasHistoryLookup ? historyMisses : null,
            hasTiming || clampHistoryUnavailable > 0 ? clampHistoryUnavailable : null,
            zeroRewind);
    }

    /// <summary>
    /// Adapts the existing lag-compensation timing result for a single
    /// explicitly observed shot. The optional history result remains unknown
    /// until a history lookup actually occurs.
    /// </summary>
    public static HitRegistrationAuthorityDiagnostic FromTiming(
        uint serverTick, uint viewServerTick, LagCompensationTime timing,
        bool? historyMiss = null, double? rewindPositionError = null)
        => HitRegistrationAuthorityDiagnostic.FromTiming(serverTick, viewServerTick,
            timing.RewindTicks, timing.Clamped, historyMiss, rewindPositionError);
}
