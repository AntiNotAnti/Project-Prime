using System;

namespace MphRead.Mods.Network;

/// <summary>
/// Adapters from the existing bounded client telemetry to the shared
/// hit-registration diagnostic snapshot. The adapter copies values only; it
/// does not query gameplay state or create a cross-domain shot correlation.
/// </summary>
public static class HitRegistrationDiagnostics
{
    public static HitRegistrationClientDiagnostic CaptureClient(
        SnapshotInterpolationMetrics interpolation,
        PresentedCollisionMetrics? presented = null,
        double? presentedTick = null,
        double? snapshotAgeMs = null,
        SnapshotPresentationMode? presentationMode = null)
    {
        bool hasPresentation = interpolation.PresentedFrames > 0
            || presentedTick.HasValue || presented.HasValue;
        if (!hasPresentation)
            return HitRegistrationClientDiagnostic.Unavailable;

        double? delay = FiniteNonNegative(interpolation.DelayTicks);
        double? poseError = null;
        long? poseSamples = null;
        long? poseUnavailable = null;
        if (presented is { } metrics)
        {
            poseSamples = metrics.PresentedPoseSamples;
            poseError = metrics.PresentedPoseErrorMean;
            poseUnavailable = metrics.PresentedPoseUnavailable;
        }
        return new(HitRegistrationDiagnosticAvailability.Available,
            FiniteNonNegative(presentedTick), FiniteNonNegative(snapshotAgeMs), delay,
            ToPresentationMode(presentationMode), FiniteNonNegative(poseError),
            poseSamples, poseUnavailable);
    }

    public static HitRegistrationFeedbackDiagnostic CaptureFeedback(
        HitPredictionMetrics metrics)
    {
        long predictedBodies = NonNegativeDifference(metrics.Predicted,
            metrics.PredictedHeadshots);
        // ConfirmedHeadshots is the prediction/authority agreement count.
        // AuthoritativeHeadshotCues includes promoted body predictions and is
        // therefore the correct denominator for the confirmed body split.
        long confirmedBodies = NonNegativeDifference(metrics.Confirmed,
            metrics.AuthoritativeHeadshotCues);
        return new(HitRegistrationDiagnosticAvailability.Available,
            metrics.Predicted, metrics.Confirmed, metrics.Denied,
            metrics.AuthoritativeUnpredicted, metrics.Pending,
            metrics.PredictedHeadshots, metrics.ConfirmedHeadshots,
            metrics.AuthoritativeHeadshotCues, predictedBodies, confirmedBodies,
            metrics.HeadshotsDowngraded, metrics.HeadshotsPromoted,
            metrics.HeadshotsDenied, metrics.AuthoritativeHeadshotsUnpredicted,
            metrics.KillPromotions, metrics.ContinuousPredicted,
            metrics.ContinuousConfirmed, metrics.ContinuousDenied,
            metrics.ContinuousAuthoritativeUnpredicted, metrics.ContinuousPending);
    }

    /// <summary>
    /// Captures the sampled client window used by the existing authoritative
    /// network report. Authority fields intentionally remain unavailable here:
    /// server rewind decisions are not present in the client metrics and must
    /// not be inferred from a presented tick.
    /// </summary>
    public static HitRegistrationDiagnosticSnapshot CaptureWindow(
        uint? serverTick,
        SnapshotInterpolationMetrics interpolation,
        PresentedCollisionMetrics? presented,
        HitPredictionMetrics? feedback,
        double? presentedTick = null,
        double? snapshotAgeMs = null,
        SnapshotPresentationMode? presentationMode = null,
        double? rttMs = null,
        double? jitterMs = null)
    {
        HitRegistrationClientDiagnostic client = CaptureClient(interpolation,
            presented, presentedTick, snapshotAgeMs, presentationMode);
        HitRegistrationFeedbackDiagnostic feedbackSnapshot = feedback.HasValue
            ? CaptureFeedback(feedback.Value)
            : HitRegistrationFeedbackDiagnostic.Unavailable;
        // A snapshot's server tick is an observed authority timestamp. It is
        // safe to expose that timestamp while leaving the server's per-shot
        // rewind fields unavailable; the timestamp alone is not a rewind join.
        HitRegistrationAuthorityDiagnostic authority = serverTick.HasValue
            ? new(HitRegistrationDiagnosticAvailability.Available, serverTick,
                null, null, null, null, null, null, null, null, null)
            : HitRegistrationAuthorityDiagnostic.Unavailable;
        return new(HitRegistrationDiagnosticScope.Window, client,
            authority, feedbackSnapshot,
            FiniteNonNegative(rttMs), FiniteNonNegative(jitterMs));
    }

    private static HitRegistrationPresentationMode ToPresentationMode(
        SnapshotPresentationMode? value)
        => value switch
        {
            SnapshotPresentationMode.Startup => HitRegistrationPresentationMode.Startup,
            SnapshotPresentationMode.Interpolated => HitRegistrationPresentationMode.Interpolated,
            SnapshotPresentationMode.HistoryHold => HitRegistrationPresentationMode.HistoryHold,
            SnapshotPresentationMode.Extrapolated => HitRegistrationPresentationMode.Extrapolated,
            SnapshotPresentationMode.ExtrapolationHold => HitRegistrationPresentationMode.ExtrapolationHold,
            _ => HitRegistrationPresentationMode.Unavailable
        };

    private static long NonNegativeDifference(long value, long subtrahend)
        => value >= subtrahend ? value - subtrahend : 0;

    private static double? FiniteNonNegative(double? value)
        => value.HasValue && Double.IsFinite(value.Value) && value.Value >= 0
            ? value : null;
}
