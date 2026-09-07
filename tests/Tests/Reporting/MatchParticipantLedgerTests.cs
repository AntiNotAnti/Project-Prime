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
        => ((ServerPeer?[])typeof(ServerNetwork).GetField("_peers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!)[peer.Slot] = peer;
    [Fact]
    public void DisconnectThenSlotReuseKeepsOriginalFactsAndFreezesTerminalReport()
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
        Assert.Equal(MatchReportV1.LegacySchema, report.SchemaVersion);
        Assert.True(report.IsValid); Assert.Equal(2, report.Participants.Length);
        Assert.Equal("Original", report.Participants[0].DisplayName); Assert.Equal(7, report.Participants[0].Metrics.Kills);
        Assert.Equal(321, report.Participants[0].Metrics.DamageDealt);
        Assert.Equal(ParticipantOutcome.Departed, report.Participants[0].Outcome);
        Assert.Equal(ParticipantExitReason.Timeout, report.Participants[0].Spans[0].ExitReason);
        Assert.Equal(2, report.Participants[1].Metrics.Kills); Assert.False(report.Participants[1].StartedMatch);
        match.Players[0].Kills = 999; ledger.Leave(state.Scene, replacement, ParticipantExitReason.ExplicitLeave, 14);
        Assert.Same(report, ledger.Complete(state.Scene, 14)); Assert.Equal(2, report.Participants[1].Metrics.Kills);
    }
    [Fact]
    public void RegisteredReturnWithResetCountersKeepsOneLogicalParticipantAndAddsMeasuredTotals()
    {
        using var state = new MatchBaselineTests.State(); var match = state.Configure(GameMode.Battle);
        match.MatchId = 1; state.Activate(0, 0);
        using var transport = (INetTransport)Activator.CreateInstance(typeof(ServerNetwork).Assembly.GetType("MphRead.Mods.Network.UdpTransport")!, new object[] { 0 })!; var network = new ServerNetwork(transport, match.Rules);
        var original = Peer(0, 100, "Account"); original.PlayerId = new(Guid.NewGuid()); SetPeer(network, original);
        var ledger = new MatchParticipantLedger(Guid.NewGuid(), Guid.NewGuid(), "build");
        ledger.BeginPlaying(state.Scene, network, 10); ledger.RecordPlayedStep(10); match.Players[0].Kills = 7;
        ledger.Leave(state.Scene, original, ParticipantExitReason.ExplicitLeave, 11); NetScoreboard.ForgetSlot(state.Scene, 0);
        var returning = Peer(0, 200, "Renamed"); returning.PlayerId = original.PlayerId; SetPeer(network, returning);
        ledger.Activate(state.Scene, returning, 20); ledger.RecordPlayedStep(20); match.Players[0].Kills = 2;
        match.CaptureResult(20); var report = ledger.Complete(state.Scene, 20)!;
        Assert.True(report.IsValid); Assert.Single(report.Participants); var participant = report.Participants[0];
        Assert.Equal(original.PlayerId, participant.PlayerId); Assert.Equal(9, participant.Metrics.Kills);
        Assert.Equal(2u, participant.PlayedTicks); Assert.Equal(2, participant.Spans.Length);
        Assert.Equal(1u, participant.Spans[0].PlayedTicks); Assert.Equal(1u, participant.Spans[1].PlayedTicks);
        Assert.Equal(21u, participant.Spans[1].LeftTick);
        foreach (var span in participant.Spans) Assert.True(span.PlayedTicks <= unchecked(span.LeftTick!.Value - span.JoinedTick));
    }
}
