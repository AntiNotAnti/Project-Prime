using MphRead.Entities;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class HitRegistrationDiagnosticsTests
{
    private static readonly CombatActor Local = new(0, 10, 1);
    private static readonly CombatActor Target = new(1, 20, 1);

    [Fact]
    public void WindowKeepsPresentedAndAuthorityTimesExplicitlySeparate()
    {
        SnapshotInterpolationMetrics interpolation = new(
            InterpolatedSamples: 1, ExtrapolatedSamples: 0,
            SnapshotUnderrunSamples: 0, HeldSamples: 0,
            PresentedFrames: 1, InterpolatedFrames: 1,
            UnderrunFrames: 0, ExtrapolatedFrames: 0, HeldFrames: 0,
            MaximumExtrapolationTicks: 0, DelayTicks: 6.25, TargetDelayTicks: 6.25);
        HitRegistrationClientDiagnostic client = HitRegistrationDiagnostics.CaptureClient(
            interpolation, presentedTick: 18422.375);
        HitRegistrationAuthorityDiagnostic authority
            = HitRegistrationAuthorityDiagnostic.FromTiming(
                serverTick: 18429, viewServerTick: 18422, servedRewindTicks: 6,
                rewindClamped: false, historyMiss: false);
        var snapshot = new HitRegistrationDiagnosticSnapshot(
            HitRegistrationDiagnosticScope.Window, client, authority,
            HitRegistrationFeedbackDiagnostic.Unavailable, 108, 11);

        string line = HitRegistrationDiagnosticFormatter.Format(in snapshot);

        Assert.Contains("scope=window", line);
        Assert.Contains("presented_tick=18422.375", line);
        Assert.Contains("server_tick=18429", line);
        Assert.Contains("rewind=7/6", line);
        Assert.Contains("clamp=0", line);
        Assert.Contains("history_miss=0", line);
        Assert.Contains("rtt_ms=108", line);
        Assert.Contains("jitter_ms=11", line);
    }

    [Fact]
    public void MissingDomainsAndFieldsUseNaInsteadOfZeroSentinels()
    {
        HitRegistrationDiagnosticSnapshot snapshot = HitRegistrationDiagnosticSnapshot.Unavailable;
        string line = HitRegistrationDiagnosticFormatter.Format(in snapshot);

        Assert.Contains("client=unavailable", line);
        Assert.Contains("presented_tick=na", line);
        Assert.Contains("authority=unavailable", line);
        Assert.Contains("server_tick=na", line);
        Assert.Contains("rewind=na/na", line);
        Assert.Contains("clamp=na", line);
        Assert.Contains("history_miss=na", line);
        Assert.Contains("feedback=unavailable", line);
        Assert.Contains("headshot=na/na", line);
        Assert.Contains("body=na/na", line);
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
    }

    [Fact]
    public void ClampAndHistoryMissRemainVisibleWhileZeroRewindRemainsAvailable()
    {
        LagCompensationTime clamped = LagCompensationPolicy.ResolveTick(100, 80, 0);
        HitRegistrationAuthorityDiagnostic authority = ServerHitRegistrationDiagnostics.FromTiming(
            100, 80, clamped, historyMiss: true, rewindPositionError: 0.041);
        var clampedSnapshot = new HitRegistrationDiagnosticSnapshot(
            HitRegistrationDiagnosticScope.Window,
            HitRegistrationClientDiagnostic.Unavailable, authority,
            HitRegistrationFeedbackDiagnostic.Unavailable, null, null);
        string clampedLine = HitRegistrationDiagnosticFormatter.Format(in clampedSnapshot);

        Assert.Contains("rewind=20/6", clampedLine);
        Assert.Contains("clamp=1", clampedLine);
        Assert.Contains("clamp_shots=1", clampedLine);
        Assert.Contains("history_miss=1", clampedLine);
        Assert.Contains("rewind_error=0.041", clampedLine);
        Assert.Contains("zero_rewind=0", clampedLine);

        LagCompensationTime zero = LagCompensationPolicy.ResolveTick(100, 100, 0);
        HitRegistrationAuthorityDiagnostic zeroAuthority = ServerHitRegistrationDiagnostics.FromTiming(
            100, 100, zero);
        var zeroSnapshot = new HitRegistrationDiagnosticSnapshot(
            HitRegistrationDiagnosticScope.Window,
            HitRegistrationClientDiagnostic.Unavailable, zeroAuthority,
            HitRegistrationFeedbackDiagnostic.Unavailable, null, null);
        string zeroLine = HitRegistrationDiagnosticFormatter.Format(in zeroSnapshot);

        Assert.Contains("rewind=0/0", zeroLine);
        Assert.Contains("clamp=0", zeroLine);
        Assert.Contains("clamp_shots=0", zeroLine);
        Assert.Contains("history_miss=na", zeroLine);
        Assert.Contains("zero_rewind=1", zeroLine);
    }

    [Fact]
    public void ServerSnapshotUsesHistoryMissTelemetryWithoutQueryingHistory()
    {
        var combat = new ServerCombat();
        combat.BeginTick(42);
        Assert.False(combat.History.TryGet(0, 4, 42, 2, out _));
        long queries = combat.History.Queries;
        long misses = combat.History.Missing;

        HitRegistrationAuthorityDiagnostic authority
            = ServerHitRegistrationDiagnostics.Capture(combat);
        var snapshot = new HitRegistrationDiagnosticSnapshot(
            HitRegistrationDiagnosticScope.Window,
            HitRegistrationClientDiagnostic.Unavailable, authority,
            HitRegistrationFeedbackDiagnostic.Unavailable, null, null);
        string line = HitRegistrationDiagnosticFormatter.Format(in snapshot);

        Assert.Equal(queries, combat.History.Queries);
        Assert.Equal(misses, combat.History.Missing);
        Assert.Contains("authority=available server_tick=42 rewind=na/na", line);
        Assert.Contains("history_miss=1", line);
        Assert.Contains("history_misses=1", line);
        Assert.Contains("clamp_history_unavailable=na", line);
    }

    [Fact]
    public void HeadshotAndBodyFeedbackSplitsReusePredictionMetrics()
    {
        var feedback = new PredictedHitFeedback();
        feedback.SetContext(1, Local);
        feedback.ObserveShot(new CombatShot(Local, 1, 0, 0, 0, 0));
        feedback.ObserveDamageAttempt(new CombatShot(Local, 1, 0, 0, 0, 0), Target,
            1, DamageFlags.Headshot, continuous: false);
        feedback.Confirm(new CombatEvent(1, 1, 1, CombatEventKind.Damage, 1,
            CombatEventFlags.Headshot, Local, Target, 90, 10,
            OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Vector3.UnitZ, 0, 0, 0));
        feedback.ObserveShot(new CombatShot(Local, 2, 0, 0, 0, 0));
        feedback.ObserveDamageAttempt(new CombatShot(Local, 2, 0, 0, 0, 0), Target,
            1, DamageFlags.None, continuous: false);
        feedback.Confirm(new CombatEvent(2, 1, 2, CombatEventKind.Damage, 1,
            CombatEventFlags.None, Local, Target, 80, 10,
            OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Vector3.UnitZ, 0, 0, 0));

        HitRegistrationFeedbackDiagnostic diagnostic
            = HitRegistrationDiagnostics.CaptureFeedback(feedback.Metrics);
        var snapshot = new HitRegistrationDiagnosticSnapshot(
            HitRegistrationDiagnosticScope.Window,
            HitRegistrationClientDiagnostic.Unavailable,
            HitRegistrationAuthorityDiagnostic.Unavailable,
            diagnostic, null, null);
        string line = HitRegistrationDiagnosticFormatter.Format(in snapshot);

        Assert.Contains("predicted=2", line);
        Assert.Contains("confirmed=2", line);
        Assert.Contains("headshot=1/1", line);
        Assert.Contains("headshot_authority=1", line);
        Assert.Contains("body=1/1", line);
    }

    [Fact]
    public void ClientWindowDoesNotInferRewindFromPresentedAndObservedServerTicks()
    {
        SnapshotInterpolationMetrics interpolation = new(
            0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 6, 6);
        HitRegistrationDiagnosticSnapshot snapshot = HitRegistrationDiagnostics.CaptureWindow(
            serverTick: 100, interpolation: interpolation, presented: null, feedback: null,
            presentedTick: 90);

        string line = HitRegistrationDiagnosticFormatter.Format(in snapshot);

        Assert.Equal(HitRegistrationDiagnosticScope.Window, snapshot.Scope);
        Assert.Equal(100u, snapshot.ServerTick);
        Assert.Equal(90, snapshot.PresentedTick);
        Assert.Contains("authority=available server_tick=100 rewind=na/na", line);
        Assert.Contains("feedback=unavailable", line);
    }

    [Fact]
    public void FormattingDoesNotMutateTheCapturedMetrics()
    {
        var feedback = new PredictedHitFeedback();
        feedback.SetContext(1, Local);
        CombatShot shot = new(Local, 3, 0, 0, 0, 0);
        feedback.ObserveShot(shot);
        feedback.ObserveDamageAttempt(shot, Target, 1, DamageFlags.Headshot, false);
        HitPredictionMetrics before = feedback.Metrics;
        HitRegistrationFeedbackDiagnostic diagnostic
            = HitRegistrationDiagnostics.CaptureFeedback(before);
        var snapshot = new HitRegistrationDiagnosticSnapshot(
            HitRegistrationDiagnosticScope.Window,
            HitRegistrationClientDiagnostic.Unavailable,
            HitRegistrationAuthorityDiagnostic.Unavailable,
            diagnostic, null, null);

        _ = HitRegistrationDiagnosticFormatter.Format(in snapshot);

        Assert.Equal(before, feedback.Metrics);
        Assert.Equal(diagnostic, snapshot.Feedback);
    }
}
