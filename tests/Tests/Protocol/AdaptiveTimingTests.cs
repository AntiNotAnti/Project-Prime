using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class AdaptiveTimingTests
{
    private static InputCommand Command(uint sequence, InputButtons pressed = InputButtons.None)
        => new(sequence, sequence, sequence, InputButtons.Forward, pressed,
            -Vector3.UnitZ, InputCommand.NoWeapon);

    [Fact]
    public void DisabledControllerPreservesTheCompatibilityPolicy()
    {
        var controller = new ServerNetworkTimingController(enabled: false);

        Assert.Equal(NetworkTimingProfile.Compatibility, controller.Active);
        Assert.Equal(6, controller.RewindPresentationDelayTicks);
        Assert.False(controller.TrySelectOffer(100, 25, 0, out _, out _));
        Assert.False(controller.TryAcknowledge(1));
    }

    [Fact]
    public void EnabledServerReliablyEstablishesTheInitialProfileBeforeInputPlayoutChanges()
    {
        using var serverTransport = new NetTransport(0);
        using var clientTransport = new NetTransport(0);
        var server = new ServerNetwork(serverTransport, "MP1 SANCTORUS", GameMode.Battle);
        server.ConfigureTiming(adaptiveTiming: true, adaptiveInputPlayout: true);
        using var client = new NetClient(clientTransport,
            new IPEndPoint(IPAddress.Loopback, serverTransport.LocalPort),
            "TIMING", Hunter.Samus);

        PumpUntil(() => client.HasTimingProfile);
        NetworkTimingProfile profile = client.TimingProfile;
        Assert.Equal(NetworkTimingProfile.Create(profile.Revision,
            NetworkTimingLevel.Recovery), profile);
        ServerPeer peer = Assert.IsType<ServerPeer>(server.Peers[client.Accepted.Slot]);
        Assert.Equal(NetworkTimingProfile.Compatibility, peer.Timing.Active);
        Assert.Equal(NetworkTimingProfile.CompatibilityInputPlayoutTicks,
            peer.Inputs.InputPlayoutTicks);

        Assert.True(client.AcknowledgeTimingProfile(profile.Revision));
        PumpUntil(() => peer.Timing.Active.Revision == profile.Revision);
        Assert.Equal(profile, peer.Timing.Active);
        Assert.Equal(profile.InputPlayoutTicks, peer.Inputs.InputPlayoutTicks);

        void PumpUntil(Func<bool> complete)
        {
            var timeout = Stopwatch.StartNew();
            while (!complete() && timeout.ElapsedMilliseconds < 3000)
            {
                server.Poll((uint)(timeout.Elapsed.TotalSeconds * 60));
                client.Poll();
                Thread.Sleep(1);
            }
            Assert.True(complete(), "Timed out establishing the adaptive timing profile.");
        }
    }

    [Fact]
    public void FrameTelemetryRequiresTheExplicitAdaptiveTimingV2Flag()
    {
        using var transport = new NetTransport(0);
        var server = new ServerNetwork(transport, "MP1 SANCTORUS", GameMode.Battle);
        Assert.Throws<ArgumentException>(() => server.ConfigureTiming(
            adaptiveTiming: false, adaptiveInputPlayout: false,
            reliableAdaptiveRto: false, adaptiveTimingV2: true));
        server.ConfigureTiming(adaptiveTiming: true, adaptiveInputPlayout: false,
            reliableAdaptiveRto: false, adaptiveTimingV2: true);
        Assert.True(server.AdaptiveTimingEnabled);
        Assert.True(server.AdaptiveTimingV2Enabled);
    }

    [Fact]
    public void ControllerRequiresCleanDwellAndAcknowledgedCompletionBeforeTightening()
    {
        var controller = new ServerNetworkTimingController(enabled: true);

        Assert.True(controller.TrySelectOffer(0, 25, 0,
            out NetworkTimingProfile initial, out _));
        Assert.Equal(NetworkTimingProfile.Create(initial.Revision, NetworkTimingLevel.Recovery), initial);
        controller.MarkOffered(initial, 0);
        Assert.True(controller.TryAcknowledge(initial.Revision));
        Assert.False(controller.TrySelectOffer(1, 25, 0, out _, out _));
        Assert.False(controller.TrySelectOffer(8.99, 25, 0, out _, out _));
        Assert.True(controller.TrySelectOffer(10, 25, 0, out NetworkTimingProfile offer,
            out bool replaceTimedOut));
        Assert.False(replaceTimedOut);
        Assert.Equal(5, offer.PresentationDelayTicks);
        Assert.Equal(6, controller.RewindPresentationDelayTicks);

        controller.MarkOffered(offer, 10);
        Assert.False(controller.TryAcknowledge(offer.Revision + 1));
        Assert.Equal(initial, controller.Active);
        Assert.True(controller.TryAcknowledge(offer.Revision));
        Assert.Equal(offer, controller.Active);
        Assert.Equal(5, controller.RewindPresentationDelayTicks);
    }

    [Fact]
    public void LostCompletionAcknowledgementProducesABoundedFallbackProposal()
    {
        var controller = new ServerNetworkTimingController(enabled: true);
        Assert.True(controller.TrySelectOffer(0, 25, 0, out NetworkTimingProfile offer,
            out _));
        controller.MarkOffered(offer, 0);

        Assert.False(controller.TrySelectOffer(4.99, 25, 0, out _, out _));
        Assert.True(controller.TrySelectOffer(5, 25, 0,
            out NetworkTimingProfile fallback, out bool replaceTimedOut));
        Assert.True(replaceTimedOut);
        Assert.NotEqual(offer.Revision, fallback.Revision);
        Assert.Equal(NetworkTimingProfile.Create(fallback.Revision, NetworkTimingLevel.Recovery), fallback);
        Assert.Equal(6, controller.RewindPresentationDelayTicks);
    }

    [Fact]
    public void ClientInstabilityCannotExpandTheRewindAllowance()
    {
        var controller = new ServerNetworkTimingController(enabled: true);
        Assert.True(controller.TrySelectOffer(0, 120, 0,
            out NetworkTimingProfile initial, out _));
        controller.MarkOffered(initial, 0);
        Assert.True(controller.TryAcknowledge(initial.Revision));
        Assert.False(controller.TrySelectOffer(1, 120, 0, out _, out _));
        Assert.True(controller.TrySelectOffer(10, 120, 0,
            out NetworkTimingProfile first, out _));
        controller.MarkOffered(first, 10);
        Assert.True(controller.TryAcknowledge(first.Revision));
        Assert.False(controller.TrySelectOffer(11, 120, 0, out _, out _));
        Assert.True(controller.TrySelectOffer(20, 120, 0,
            out NetworkTimingProfile normal, out _));
        controller.MarkOffered(normal, 20);
        Assert.True(controller.TryAcknowledge(normal.Revision));
        Assert.Equal(4, controller.Active.PresentationDelayTicks);
        Assert.Equal(4, controller.RewindPresentationDelayTicks);

        var telemetry = new NetworkTimingTelemetry(normal.Revision, 4, 20, 1, 20, 333, 150);
        Assert.True(controller.ObserveTelemetry(telemetry, 21));
        Assert.True(controller.TrySelectOffer(21, 120, 0,
            out NetworkTimingProfile saferPresentation, out _));
        Assert.Equal(5, saferPresentation.PresentationDelayTicks);
        controller.MarkOffered(saferPresentation, 21);
        Assert.True(controller.TryAcknowledge(saferPresentation.Revision));
        Assert.Equal(5, controller.Active.PresentationDelayTicks);
        Assert.Equal(4, controller.RewindPresentationDelayTicks);
        Assert.False(controller.ObserveTelemetry(telemetry with { ProfileRevision = 99 }, 21.5));
    }

    [Fact]
    public void OneIsolatedUnderrunDoesNotEscalateAStablePresentation()
    {
        var controller = new ServerNetworkTimingController(enabled: true);
        Assert.True(controller.TrySelectOffer(0, 120, 0,
            out NetworkTimingProfile initial, out _));
        controller.MarkOffered(initial, 0);
        Assert.True(controller.TryAcknowledge(initial.Revision));
        Assert.False(controller.TrySelectOffer(1, 120, 0, out _, out _));
        Assert.True(controller.TrySelectOffer(10, 120, 0,
            out NetworkTimingProfile first, out _));
        controller.MarkOffered(first, 10);
        Assert.True(controller.TryAcknowledge(first.Revision));
        Assert.False(controller.TrySelectOffer(11, 120, 0, out _, out _));
        Assert.True(controller.TrySelectOffer(20, 120, 0,
            out NetworkTimingProfile normal, out _));
        controller.MarkOffered(normal, 20);
        Assert.True(controller.TryAcknowledge(normal.Revision));

        var telemetry = new NetworkTimingTelemetry(normal.Revision, 4, 60, 1, 0, 333, 0);
        Assert.True(controller.ObserveTelemetry(telemetry, 21));
        Assert.False(controller.TrySelectOffer(21, 120, 0, out _, out _));
        Assert.Equal(1 / 60.0, controller.LastUnderrunRate, 6);
        Assert.Equal(0, controller.LastExtrapolationRate);
        Assert.Equal(TimingTelemetryBand.Degraded, controller.LastTelemetryBand);
    }

    [Fact]
    public void TelemetryBandsUseStrictCleanRecoveryBoundaries()
    {
        var controller = new ServerNetworkTimingController(enabled: true);
        Assert.True(controller.TrySelectOffer(0, 25, 0, out NetworkTimingProfile initial, out _));
        controller.MarkOffered(initial, 0);
        Assert.True(controller.TryAcknowledge(initial.Revision));

        NetworkTimingTelemetry telemetry = new(initial.Revision, 6, 400, 0, 0, 333, 0);
        Assert.True(controller.ObserveTelemetry(telemetry, 1));
        Assert.Equal(TimingTelemetryBand.Excellent, controller.LastTelemetryBand);
        Assert.False(controller.TrySelectOffer(1, 25, 0, out _, out _));

        Assert.True(controller.ObserveTelemetry(telemetry with { PresentedFrames = 400, SnapshotUnderruns = 1 }, 2));
        Assert.Equal(TimingTelemetryBand.Healthy, controller.LastTelemetryBand);
        Assert.False(controller.TrySelectOffer(10, 25, 0, out _, out _));

        Assert.True(controller.ObserveTelemetry(telemetry with { SnapshotUnderruns = 4 }, 11));
        Assert.Equal(TimingTelemetryBand.Degraded, controller.LastTelemetryBand);
        Assert.False(controller.TrySelectOffer(20, 25, 0, out _, out _));

        Assert.True(controller.ObserveTelemetry(telemetry with { SnapshotUnderruns = 8 }, 21));
        Assert.Equal(TimingTelemetryBand.Unstable, controller.LastTelemetryBand);
    }

    [Fact]
    public void EqualFrameRatesProduceTheSameDecisionRegardlessOfPlayerCount()
    {
        static (ServerNetworkTimingController Controller, NetworkTimingProfile Profile) Ready()
        {
            var controller = new ServerNetworkTimingController(enabled: true);
            Assert.True(controller.TrySelectOffer(0, 120, 0,
                out NetworkTimingProfile initial, out _));
            controller.MarkOffered(initial, 0);
            Assert.True(controller.TryAcknowledge(initial.Revision));
            Assert.False(controller.TrySelectOffer(1, 120, 0, out _, out _));
            Assert.True(controller.TrySelectOffer(10, 120, 0,
                out NetworkTimingProfile first, out _));
            controller.MarkOffered(first, 10);
            Assert.True(controller.TryAcknowledge(first.Revision));
            Assert.False(controller.TrySelectOffer(11, 120, 0, out _, out _));
            Assert.True(controller.TrySelectOffer(20, 120, 0,
                out NetworkTimingProfile normal, out _));
            controller.MarkOffered(normal, 20);
            Assert.True(controller.TryAcknowledge(normal.Revision));
            return (controller, normal);
        }

        (ServerNetworkTimingController twoPlayer, NetworkTimingProfile twoProfile) = Ready();
        (ServerNetworkTimingController eightPlayer, NetworkTimingProfile eightProfile) = Ready();
        Assert.True(twoPlayer.ObserveTelemetry(
            new NetworkTimingTelemetry(twoProfile.Revision, 4, 60, 2, 0, 333, 0), 21));
        Assert.True(eightPlayer.ObserveTelemetry(
            new NetworkTimingTelemetry(eightProfile.Revision, 4, 240, 8, 0, 333, 0), 21));
        Assert.Equal(
            twoPlayer.TrySelectOffer(21, 120, 0, out NetworkTimingProfile twoOffer, out bool twoTimedOut),
            eightPlayer.TrySelectOffer(21, 120, 0, out NetworkTimingProfile eightOffer, out bool eightTimedOut));
        Assert.False(twoTimedOut);
        Assert.False(eightTimedOut);
        Assert.Equal(twoOffer.PresentationDelayTicks, eightOffer.PresentationDelayTicks);
    }

    [Fact]
    public void TrustedRttDeteriorationCanWidenTheRewindAllowance()
    {
        var controller = new ServerNetworkTimingController(enabled: true);
        Assert.True(controller.TrySelectOffer(0, 25, 0,
            out NetworkTimingProfile initial, out _));
        controller.MarkOffered(initial, 0);
        Assert.True(controller.TryAcknowledge(initial.Revision));
        Assert.Equal(6, controller.RewindPresentationDelayTicks);

        // Clean RTT gradually tightens the active policy and its trusted cap.
        Assert.False(controller.TrySelectOffer(1, 25, 0, out _, out _));
        Assert.True(controller.TrySelectOffer(10, 25, 0, out NetworkTimingProfile tighter, out _));
        controller.MarkOffered(tighter, 10);
        Assert.True(controller.TryAcknowledge(tighter.Revision));
        Assert.Equal(5, controller.RewindPresentationDelayTicks);

        // A trusted RTT deterioration may widen the security allowance before
        // the adjacent timing offer is acknowledged.
        Assert.True(controller.TrySelectOffer(11, 300, 0, out NetworkTimingProfile safer, out _));
        Assert.Equal(6, controller.RewindPresentationDelayTicks);
        controller.MarkOffered(safer, 11);
        Assert.True(controller.TryAcknowledge(safer.Revision));
        Assert.Equal(6, controller.RewindPresentationDelayTicks);
    }

    [Fact]
    public void InputPolicyIsStampedAtAdmissionAndSurvivesLaterProfileChanges()
    {
        var stream = new ServerInputStream();
        stream.ConfigurePlayout(1);
        stream.Receive(new[] { Command(10, InputButtons.Jump) }, 0,
            rewindPresentationDelayTicks: 6);
        stream.Receive(new[] { Command(11, InputButtons.Morph) }, 1,
            rewindPresentationDelayTicks: 2);

        Assert.Equal(2, stream.BufferedCommands);
        Assert.Equal(2, stream.MaximumBufferedCommands);
        _ = stream.Take(0, out _);
        InputCommand first = stream.Take(1, out byte firstDelay);
        InputCommand second = stream.Take(2, out byte secondDelay);

        Assert.Equal(10u, first.Sequence);
        Assert.Equal(6, firstDelay);
        Assert.Equal(11u, second.Sequence);
        Assert.Equal(2, secondDelay);
        Assert.Equal(0, stream.BufferedCommands);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ConfiguredInputPlayoutIsStartupAndGapTolerance(byte playoutTicks)
    {
        var stream = new ServerInputStream();
        stream.ConfigurePlayout(playoutTicks);
        stream.Receive(new[] { Command(20) }, 0, 4);

        for (uint tick = 0; tick < playoutTicks; tick++)
            Assert.False(stream.Take(tick).Equals(Command(20)));
        Assert.Equal(Command(20), stream.Take(playoutTicks));
        Assert.Equal(playoutTicks, stream.InputPlayoutTicks);
    }

    [Fact]
    public void ReducingInputPlayoutDoesNotRewriteAnEstablishedQueue()
    {
        var stream = new ServerInputStream();
        stream.ConfigurePlayout(3);
        stream.Receive(new[] { Command(30), Command(31), Command(32), Command(33) }, 0, 5);

        stream.ConfigurePlayout(1);

        Assert.Equal(4, stream.BufferedCommands);
        Assert.Equal(4, stream.MaximumBufferedCommands);
        Assert.Equal(1, stream.InputPlayoutTicks);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(144)]
    public void PresentationDelaySlewUsesElapsedTimeInsteadOfFrameCount(int renderRate)
    {
        var history = new SnapshotInterpolation(delayTicks: 6);
        long start = 1;
        history.SetTargetDelay(2, start);
        for (int frame = 1; frame <= renderRate; frame++)
        {
            long now = start + (long)Math.Round(frame * Stopwatch.Frequency / (double)renderRate);
            history.AdvanceDelay(now);
        }

        Assert.InRange(history.DelayTicks, 4.999, 5.001);
        Assert.False(history.DelayTransitionComplete);
    }

    [Fact]
    public void AuthorizedPresentationDelayChangesTheBudgetButNeverTheGlobalCap()
    {
        LagCompensationTime shallow = LagCompensationPolicy.ResolveTick(100, 1, 100, 2);
        LagCompensationTime compatibility = LagCompensationPolicy.ResolveTick(100, 1, 100, 6);
        LagCompensationTime capped = LagCompensationPolicy.ResolveTick(100, 1, 10_000, 6);

        Assert.Equal(7u, shallow.AllowedTicks);
        Assert.Equal(11u, compatibility.AllowedTicks);
        Assert.Equal(LagCompensationPolicy.MaxRewindTicks, capped.AllowedTicks);
        Assert.Equal(LagCompensationPolicy.MaxRewindTicks, capped.RewindTicks);
    }
}
