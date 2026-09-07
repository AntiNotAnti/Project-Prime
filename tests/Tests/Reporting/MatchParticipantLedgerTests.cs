using System;
using System.Net;
using System.Reflection;
using MphRead.Identity;
using MphRead.Mods.Network;
using MphRead.Reporting;
using Xunit;
namespace MphRead.Tests.Reporting;
[Collection("Match baseline globals")]
public sealed class MatchParticipantLedgerTests
{
    private static ServerPeer Peer(byte slot, ulong connection, string name)
    {
        var conn = new NetConnection(connection, new IPEndPoint(IPAddress.Loopback, 2000 + slot), 1, 0);
        conn.Ready(1); conn.StartPlaying();
        return new ServerPeer(conn, new JoinPacket { Nonce = connection, Name = name, Hunter = Hunter.Samus }, slot, 0) { HasParticipated = true, TeamIndex = slot };
    }
    private static void SetPeer(ServerNetwork network, ServerPeer peer)
    {
        var peers = (ServerPeer?[])typeof(ServerNetwork).GetField("_peers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!;
        peers[peer.Slot] = peer;
        typeof(ServerNetwork).GetProperty(nameof(ServerNetwork.Count))!.SetValue(network, Array.FindAll(peers, candidate => candidate != null).Length);
    }
    [Fact]
    public void DisconnectedOfficialParticipantForfeitsIfMatchCompletesDuringGrace()
    {
        using var state = new MatchBaselineTests.State(); var match = state.Configure(GameMode.Battle);
        match.MatchId = 1; state.Activate(0, 0);
        using var transport = (INetTransport)Activator.CreateInstance(typeof(ServerNetwork).Assembly.GetType("MphRead.Mods.Network.UdpTransport")!, new object[] { 0 })!; var network = new ServerNetwork(transport, match.Rules);
        var original = Peer(0, 100, "Original"); SetPeer(network, original);
        var ledger = new MatchParticipantLedger(Guid.NewGuid(), Guid.NewGuid(), "build", () => DateTimeOffset.UnixEpoch);
        ledger.BeginPlaying(state.Scene, network, 10); ledger.RecordPlayedStep(10);
        match.Players[0].Kills = 7; match.Players[0].DamageDealt = 321;
        ledger.Leave(state.Scene, original, ParticipantExitReason.Timeout, 11);
        NetScoreboard.ForgetSlot(state.Scene, 0);
        var replacement = Peer(0, 200, "Replacement"); SetPeer(network, replacement);
        ledger.Activate(state.Scene, replacement, 12); ledger.RecordPlayedStep(12); match.Players[0].Kills = 2;
        match.CaptureResult(20);
        var report = ledger.Complete(state.Scene, 13)!;
        Assert.True(report.IsValid); Assert.Equal(2, report.Participants.Length);
        Assert.Equal("Original", report.Participants[0].DisplayName); Assert.Equal(7, report.Participants[0].Metrics.Kills);
        Assert.Equal(321, report.Participants[0].Metrics.DamageDealt);
        Assert.Equal(ParticipantOutcome.Forfeited, report.Participants[0].Outcome);
        Assert.Equal(ParticipantOutcomeReason.MatchCompletedWhileDisconnected, report.Participants[0].OutcomeReason);
        Assert.Equal(ParticipantExitReason.Timeout, report.Participants[0].Spans[0].ExitReason);
        Assert.Equal(2, report.Participants[1].Metrics.Kills); Assert.False(report.Participants[1].StartedMatch);
        MatchSummaryPacket summary = LobbyMatchSummary.Create(77, 1, match.Result!, report: report);
        Assert.Equal(2, summary.Rows.Length);
        MatchSummaryRow departed = Assert.Single(summary.Rows, row => row.Name == "Original");
        Assert.Equal(7, departed.Kills);
        Assert.Equal(321, departed.Damage);
        MatchSummaryRow finisher = Assert.Single(summary.Rows,
            row => row.Name == "Replacement" && row.Kills == 2);
        Assert.True(finisher.Placement < departed.Placement);
        match.Players[0].Kills = 999; ledger.Leave(state.Scene, replacement, ParticipantExitReason.ExplicitLeave, 14);
        Assert.Same(report, ledger.Complete(state.Scene, 14)); Assert.Equal(2, report.Participants[1].Metrics.Kills);
    }
    [Fact]
    public void RegisteredReconnectWithinGraceKeepsOneLogicalParticipantAndMeasuredTotals()
    {
        using var state = new MatchBaselineTests.State(); var match = state.Configure(GameMode.Battle);
        match.MatchId = 1; state.Activate(0, 0);
        using var transport = (INetTransport)Activator.CreateInstance(typeof(ServerNetwork).Assembly.GetType("MphRead.Mods.Network.UdpTransport")!, new object[] { 0 })!; var network = new ServerNetwork(transport, match.Rules);
        var original = Peer(0, 100, "Account"); original.PlayerId = new(Guid.NewGuid()); SetPeer(network, original);
        var ledger = new MatchParticipantLedger(Guid.NewGuid(), Guid.NewGuid(), "build");
        ledger.BeginPlaying(state.Scene, network, 10); ledger.RecordPlayedStep(10); match.Players[0].Kills = 7;
        ledger.Leave(state.Scene, original, ParticipantExitReason.Disconnected, 11);
        var returning = Peer(0, 200, "Renamed"); returning.PlayerId = original.PlayerId; SetPeer(network, returning);
        returning.ReturningParticipant = true;
        returning.ReturningFromConnectionId = original.Connection.Id;
        ledger.Activate(state.Scene, returning, 20); ledger.RecordPlayedStep(20); match.Players[0].Kills = 8;
        match.CaptureResult(20); var report = ledger.Complete(state.Scene, 20)!;
        Assert.True(report.IsValid); Assert.Single(report.Participants); var participant = report.Participants[0];
        Assert.Equal(original.PlayerId, participant.PlayerId); Assert.Equal(8, participant.Metrics.Kills);
        Assert.Equal(ParticipantOutcome.Finished, participant.Outcome);
        Assert.Equal(ParticipantOutcomeReason.Completed, participant.OutcomeReason);
        Assert.Equal(2u, participant.PlayedTicks); Assert.Equal(2, participant.Spans.Length);
        Assert.Equal(1u, participant.Spans[0].PlayedTicks); Assert.Equal(1u, participant.Spans[1].PlayedTicks);
        Assert.Equal(21u, participant.Spans[1].LeftTick);
        foreach (var span in participant.Spans) Assert.True(span.PlayedTicks <= unchecked(span.LeftTick!.Value - span.JoinedTick));
    }

    [Fact]
    public void ExplicitLeaveNeverReservesReconnectAndRemainsImmediateForfeit()
    {
        using var state = new MatchBaselineTests.State(); var match = state.Configure(GameMode.Battle);
        match.MatchId = 1; state.Activate(0, 0);
        using var transport = (INetTransport)Activator.CreateInstance(typeof(ServerNetwork).Assembly.GetType("MphRead.Mods.Network.UdpTransport")!, new object[] { 0 })!;
        var network = new ServerNetwork(transport, match.Rules) { Phase = MatchPhase.Playing };
        var original = Peer(0, 100, "Leaver"); original.PlayerId = new(Guid.NewGuid()); SetPeer(network, original);
        var ledger = new MatchParticipantLedger(Guid.NewGuid(), Guid.NewGuid(), "build");
        ledger.BeginPlaying(state.Scene, network, 10); ledger.RecordPlayedStep(10);
        network.ParticipantLeaving = (peer, reason, tick) => ledger.Leave(state.Scene, peer, reason, tick);

        network.Poll(11);
        network.Remove(0, reason: ParticipantExitReason.ExplicitLeave);

        Assert.False(network.HasReconnectReservation(0));
        match.CaptureResult(20);
        MatchReportParticipant participant = Assert.Single(ledger.Complete(state.Scene, 12)!.Participants);
        Assert.Equal(ParticipantOutcome.Forfeited, participant.Outcome);
        Assert.Equal(ParticipantOutcomeReason.ExplicitLeave, participant.OutcomeReason);
    }

    [Fact]
    public void ReconnectReservationExpiresAtExactlyEighteenHundredTicks()
    {
        using var state = new MatchBaselineTests.State(); var match = state.Configure(GameMode.Battle);
        match.MatchId = 1; state.Activate(0, 0);
        using var transport = (INetTransport)Activator.CreateInstance(typeof(ServerNetwork).Assembly.GetType("MphRead.Mods.Network.UdpTransport")!, new object[] { 0 })!;
        var network = new ServerNetwork(transport, match.Rules) { Phase = MatchPhase.Playing };
        var original = Peer(0, 100, "TimedOut"); original.PlayerId = new(Guid.NewGuid()); SetPeer(network, original);
        var ledger = new MatchParticipantLedger(Guid.NewGuid(), Guid.NewGuid(), "build");
        ledger.BeginPlaying(state.Scene, network, 100); ledger.RecordPlayedStep(100);
        var departures = new System.Collections.Generic.List<(ParticipantExitReason Reason, uint Tick)>();
        network.ParticipantLeaving = (peer, reason, tick) =>
        {
            departures.Add((reason, tick));
            ledger.Leave(state.Scene, peer, reason, tick);
        };

        network.Poll(100);
        network.Remove(0, reason: ParticipantExitReason.Disconnected);
        network.Poll(1899);
        Assert.True(network.HasReconnectReservation(0));
        Assert.Single(departures);

        network.Poll(1900);
        Assert.False(network.HasReconnectReservation(0));
        Assert.Equal((ParticipantExitReason.ReconnectGraceExpired, 1900u), departures[1]);
        match.CaptureResult(20);
        MatchReportParticipant participant = Assert.Single(ledger.Complete(state.Scene, 1900)!.Participants);
        Assert.Equal(ParticipantOutcome.Forfeited, participant.Outcome);
        Assert.Equal(ParticipantOutcomeReason.ReconnectGraceExpired, participant.OutcomeReason);
    }

    [Fact]
    public void SchemaTwoRequiresExplicitConsistentOutcomeReasonWhileSchemaOneStaysLegacy()
    {
        using var state = new MatchBaselineTests.State(); var match = state.Configure(GameMode.Battle);
        match.MatchId = 1; state.Activate(0, 0);
        using var transport = (INetTransport)Activator.CreateInstance(typeof(ServerNetwork).Assembly.GetType("MphRead.Mods.Network.UdpTransport")!, new object[] { 0 })!;
        var network = new ServerNetwork(transport, match.Rules);
        var peer = Peer(0, 100, "Schema"); SetPeer(network, peer);
        var ledger = new MatchParticipantLedger(Guid.NewGuid(), Guid.NewGuid(), "build", () => DateTimeOffset.UnixEpoch);
        ledger.BeginPlaying(state.Scene, network, 10); ledger.RecordPlayedStep(10);
        match.CaptureResult(20);
        MatchReportV1 current = ledger.Complete(state.Scene, 11)!;

        Assert.Equal(2, current.SchemaVersion);
        Assert.True(current.IsValid);
        Assert.False((current with { Participants = [current.Participants[0] with
            { OutcomeReason = ParticipantOutcomeReason.LegacyUnspecified }] }).IsValid);
        MatchReportV1 legacy = current with
        {
            SchemaVersion = MatchReportV1.LegacySchema,
            Participants = [current.Participants[0] with { OutcomeReason = ParticipantOutcomeReason.LegacyUnspecified }]
        };
        Assert.True(legacy.IsValid);
        Assert.False((legacy with { Participants = current.Participants }).IsValid);
    }
}
