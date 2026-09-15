using System;
using System.Globalization;
using System.Text;

namespace MphRead.Mods.Network;

/// <summary>
/// Availability is carried independently for each diagnostic domain. A zero
/// is a valid measured value (for example, a zero-rewind shot), so callers
/// must not use zero as an unavailable sentinel.
/// </summary>
public enum HitRegistrationDiagnosticAvailability : byte
{
    Unavailable,
    Available
}

/// <summary>
/// The scope is deliberately explicit in the formatted line. Window metrics
/// are not a per-shot join: client presentation, server authority, and client
/// feedback may have been collected by different bounded owners.
/// </summary>
public enum HitRegistrationDiagnosticScope : byte
{
    Window
}

/// <summary>Presentation mode without a dependency on the presentation project.</summary>
public enum HitRegistrationPresentationMode : byte
{
    Unavailable,
    Startup,
    Interpolated,
    HistoryHold,
    Extrapolated,
    ExtrapolationHold
}

/// <summary>Bounded client-view facts used by unified hit-registration diagnostics.</summary>
public readonly record struct HitRegistrationClientDiagnostic(
    HitRegistrationDiagnosticAvailability Availability,
    double? PresentedTick,
    double? SnapshotAgeMs,
    double? InterpolationDelayTicks,
    HitRegistrationPresentationMode PresentationMode,
    double? PresentedPoseError,
    long? PresentedPoseSamples,
    long? PresentedPoseUnavailable)
{
    public static HitRegistrationClientDiagnostic Unavailable => new(
        HitRegistrationDiagnosticAvailability.Unavailable, null, null, null,
        HitRegistrationPresentationMode.Unavailable, null, null, null);

    public bool IsAvailable => Availability == HitRegistrationDiagnosticAvailability.Available;
}

/// <summary>Bounded server-view facts used by unified hit-registration diagnostics.</summary>
public readonly record struct HitRegistrationAuthorityDiagnostic(
    HitRegistrationDiagnosticAvailability Availability,
    uint? ServerTick,
    double? RequestedRewindTicks,
    double? ServedRewindTicks,
    bool? RewindClamped,
    bool? HistoryMiss,
    double? RewindPositionError,
    long? ClampedShots,
    long? HistoryMisses,
    long? ClampHistoryUnavailable,
    long? ZeroRewindShots)
{
    public static HitRegistrationAuthorityDiagnostic Unavailable => new(
        HitRegistrationDiagnosticAvailability.Unavailable, null, null, null,
        null, null, null, null, null, null, null);

    public bool IsAvailable => Availability == HitRegistrationDiagnosticAvailability.Available;

    /// <summary>
    /// Builds one authority-domain observation from the existing server timing
    /// result. The history result is optional because a timing decision does
    /// not itself prove that a historical collider lookup was attempted.
    /// </summary>
    public static HitRegistrationAuthorityDiagnostic FromTiming(
        uint serverTick, uint viewServerTick, uint servedRewindTicks,
        bool rewindClamped, bool? historyMiss = null,
        double? rewindPositionError = null)
    {
        uint rawRequested = unchecked(serverTick - viewServerTick);
        double requested = rawRequested < 0x80000000u ? rawRequested : 0;
        return new(HitRegistrationDiagnosticAvailability.Available, serverTick,
            requested, servedRewindTicks, rewindClamped, historyMiss,
            FiniteNonNegative(rewindPositionError), rewindClamped ? 1 : 0,
            historyMiss.HasValue ? historyMiss.Value ? 1 : 0 : null,
            null, servedRewindTicks == 0 ? 1 : 0);
    }

    private static double? FiniteNonNegative(double? value)
        => value.HasValue && Double.IsFinite(value.Value) && value.Value >= 0
            ? value : null;
}

/// <summary>Bounded client feedback facts used by unified hit-registration diagnostics.</summary>
public readonly record struct HitRegistrationFeedbackDiagnostic(
    HitRegistrationDiagnosticAvailability Availability,
    long? Predicted,
    long? Confirmed,
    long? Denied,
    long? AuthoritativeUnpredicted,
    int? Pending,
    long? PredictedHeadshots,
    long? ConfirmedHeadshots,
    long? AuthoritativeHeadshotCues,
    long? PredictedBodies,
    long? ConfirmedBodies,
    long? HeadshotsDowngraded,
    long? HeadshotsPromoted,
    long? HeadshotsDenied,
    long? AuthoritativeHeadshotsUnpredicted,
    long? KillPromotions,
    long? ContinuousPredicted,
    long? ContinuousConfirmed,
    long? ContinuousDenied,
    long? ContinuousAuthoritativeUnpredicted,
    int? ContinuousPending)
{
    public static HitRegistrationFeedbackDiagnostic Unavailable => new(
        HitRegistrationDiagnosticAvailability.Unavailable, null, null, null,
        null, null, null, null, null, null, null, null, null, null, null,
        null,
        null, null, null, null, null);

    public bool IsAvailable => Availability == HitRegistrationDiagnosticAvailability.Available;
}

/// <summary>
/// Immutable, window-scoped union of the existing client, authority, and
/// feedback observations. It contains no entity references or mutable metric
/// owners and does not claim that independently collected domains describe one
/// shot.
/// </summary>
public readonly record struct HitRegistrationDiagnosticSnapshot(
    HitRegistrationDiagnosticScope Scope,
    HitRegistrationClientDiagnostic Client,
    HitRegistrationAuthorityDiagnostic Authority,
    HitRegistrationFeedbackDiagnostic Feedback,
    double? RttMs,
    double? JitterMs)
{
    public static HitRegistrationDiagnosticSnapshot Unavailable => new(
        HitRegistrationDiagnosticScope.Window,
        HitRegistrationClientDiagnostic.Unavailable,
        HitRegistrationAuthorityDiagnostic.Unavailable,
        HitRegistrationFeedbackDiagnostic.Unavailable, null, null);

    public double? PresentedTick => Client.PresentedTick;
    public uint? ServerTick => Authority.ServerTick;
}

/// <summary>
/// Stable, concise, single-line machine-readable formatting for
/// <see cref="HitRegistrationDiagnosticSnapshot"/>. Formatting is a
/// diagnostics/reporting operation; it is not intended for an unconditional
/// simulation or rendering hot path.
/// </summary>
public static class HitRegistrationDiagnosticFormatter
{
    public static string Format(in HitRegistrationDiagnosticSnapshot value)
    {
        var output = new StringBuilder(512);
        output.Append("HITREG scope=").Append(Scope(value.Scope));

        output.Append(" client=").Append(Availability(value.Client.Availability));
        output.Append(" presented_tick=").Append(Number(value.Client.PresentedTick));
        output.Append(" snapshot_age_ms=").Append(Number(value.Client.SnapshotAgeMs));
        output.Append(" interpolation_delay_ticks=").Append(Number(value.Client.InterpolationDelayTicks));
        output.Append(" mode=").Append(Mode(value.Client.PresentationMode));
        output.Append(" pose_error=").Append(Number(value.Client.PresentedPoseError));
        output.Append(" pose_samples=").Append(Count(value.Client.PresentedPoseSamples));
        output.Append(" pose_unavailable=").Append(Count(value.Client.PresentedPoseUnavailable));

        output.Append(" authority=").Append(Availability(value.Authority.Availability));
        output.Append(" server_tick=").Append(Count(value.Authority.ServerTick));
        output.Append(" rewind=").Append(Number(value.Authority.RequestedRewindTicks));
        output.Append('/').Append(Number(value.Authority.ServedRewindTicks));
        output.Append(" clamp=").Append(Boolean(value.Authority.RewindClamped));
        output.Append(" history_miss=").Append(Boolean(value.Authority.HistoryMiss));
        output.Append(" rewind_error=").Append(Number(value.Authority.RewindPositionError));
        output.Append(" clamp_shots=").Append(Count(value.Authority.ClampedShots));
        output.Append(" history_misses=").Append(Count(value.Authority.HistoryMisses));
        output.Append(" clamp_history_unavailable=").Append(Count(value.Authority.ClampHistoryUnavailable));
        output.Append(" zero_rewind=").Append(Count(value.Authority.ZeroRewindShots));

        output.Append(" feedback=").Append(Availability(value.Feedback.Availability));
        output.Append(" predicted=").Append(Count(value.Feedback.Predicted));
        output.Append(" confirmed=").Append(Count(value.Feedback.Confirmed));
        output.Append(" denied=").Append(Count(value.Feedback.Denied));
        output.Append(" authority_only=").Append(Count(value.Feedback.AuthoritativeUnpredicted));
        output.Append(" pending=").Append(Count(value.Feedback.Pending));
        output.Append(" headshot=").Append(Count(value.Feedback.PredictedHeadshots));
        output.Append('/').Append(Count(value.Feedback.ConfirmedHeadshots));
        output.Append(" headshot_authority=").Append(Count(value.Feedback.AuthoritativeHeadshotCues));
        output.Append(" body=").Append(Count(value.Feedback.PredictedBodies));
        output.Append('/').Append(Count(value.Feedback.ConfirmedBodies));
        output.Append(" headshot_downgraded=").Append(Count(value.Feedback.HeadshotsDowngraded));
        output.Append(" headshot_promoted=").Append(Count(value.Feedback.HeadshotsPromoted));
        output.Append(" headshot_denied=").Append(Count(value.Feedback.HeadshotsDenied));
        output.Append(" headshot_authority_only=").Append(Count(value.Feedback.AuthoritativeHeadshotsUnpredicted));
        output.Append(" kills=").Append(Count(value.Feedback.KillPromotions));
        output.Append(" continuous=").Append(Count(value.Feedback.ContinuousPredicted));
        output.Append('/').Append(Count(value.Feedback.ContinuousConfirmed));
        output.Append('/').Append(Count(value.Feedback.ContinuousDenied));
        output.Append('/').Append(Count(value.Feedback.ContinuousAuthoritativeUnpredicted));
        output.Append('/').Append(Count(value.Feedback.ContinuousPending));

        output.Append(" rtt_ms=").Append(Number(value.RttMs));
        output.Append(" jitter_ms=").Append(Number(value.JitterMs));
        return output.ToString();
    }

    private static string Availability(HitRegistrationDiagnosticAvailability value)
        => value == HitRegistrationDiagnosticAvailability.Available ? "available" : "unavailable";

    private static string Scope(HitRegistrationDiagnosticScope value)
        => "window";

    private static string Mode(HitRegistrationPresentationMode value)
        => value switch
        {
            HitRegistrationPresentationMode.Startup => "startup",
            HitRegistrationPresentationMode.Interpolated => "interpolated",
            HitRegistrationPresentationMode.HistoryHold => "history_hold",
            HitRegistrationPresentationMode.Extrapolated => "extrapolated",
            HitRegistrationPresentationMode.ExtrapolationHold => "extrapolation_hold",
            _ => "na"
        };

    private static string Boolean(bool? value)
        => value.HasValue ? value.Value ? "1" : "0" : "na";

    private static string Count(long? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "na";

    private static string Count(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "na";

    private static string Count(uint? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "na";

    private static string Number(double? value)
        => value.HasValue && Double.IsFinite(value.Value) && value.Value >= 0
            ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "na";
}
