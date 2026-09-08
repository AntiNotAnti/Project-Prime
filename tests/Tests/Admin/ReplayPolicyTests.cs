using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using MphRead.Replay;
using MphRead.Reporting;
using Xunit;

namespace MphRead.Tests.Admin;

[Collection("Match baseline globals")]
public sealed class ReplayPolicyTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void IdentifiedDuelRequiresReplayStorage(bool authenticated, bool reported)
    {
        var duel = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 2).With(rulesetPreset: RulesetPreset.Duel);
        Assert.Throws<ArgumentException>(() => ServerReplayPolicy.Validate(duel, authenticated, reported, null));
        Assert.True(ServerReplayPolicy.Validate(duel, authenticated, reported, "/tmp/replay-policy-test"));
        Assert.False(ServerReplayPolicy.Validate(duel, false, false, null));
        Assert.False(ServerReplayPolicy.Validate(duel.With(rulesetPreset: RulesetPreset.Classic), true, true, null));
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public async Task RealContentReplayGateHoldsCountdownUntilStorageReadyAndFailsClosed()
    {
        using var saved = ServerContent.PreserveContext("AMHE1");
        string root = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY") ?? Directory.GetCurrentDirectory();
        while (!Directory.Exists(Path.Combine(root, "AMHE1"))) root = Directory.GetParent(root)?.FullName ?? throw new DirectoryNotFoundException("AMHE1 required.");
        ServerContent.Open(Path.Combine(root, "AMHE1"), "AMHE1");
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1);
        using var simulation = new ServerSimulation(rules);
        using var transport = new NetTransport(0);
        var network = new ServerNetwork(transport, rules);
        var connection = new NetConnection(100, new IPEndPoint(IPAddress.Loopback, 23001), 1, 0); connection.Ready(1);
        var peer = new ServerPeer(connection, new JoinPacket { Nonce = 99, Name = "ReplayTest", Hunter = Hunter.Samus }, 0, 0);
        ((ServerPeer?[])typeof(ServerNetwork).GetField("_peers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!)[0] = peer;
        ((ServerPeer?[])typeof(ServerNetwork).GetField("_connections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!)[0] = peer;
        simulation.ReplayMayStart = ServerReplayPolicy.MayStart(true, null);
        simulation.Step(network, 0); Assert.Equal(MatchPhase.WaitingForPlayers, simulation.Scene.Match.Phase);
        string directory = Directory.CreateTempSubdirectory("prime-replay-policy-").FullName;
        var recording = new ServerReplaySession(directory);
        try
        {
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!recording.Ready && DateTime.UtcNow < until) await Task.Delay(5);
            Assert.True(recording.Ready, recording.Status.Error);
            simulation.Reports = new MatchParticipantLedger(Guid.NewGuid(), Guid.NewGuid(), "replay-policy-test");
            simulation.Reports.ConfigureRoundIdentity(null, null, recording.ReplayId);
            simulation.ReplayMayStart = ServerReplayPolicy.MayStart(true, recording);
            simulation.Step(network, 1); Assert.Equal(MatchPhase.Countdown, simulation.Scene.Match.Phase);
            simulation.ReplayMayStart = false;
            simulation.Step(network, 2); Assert.Equal(MatchPhase.WaitingForPlayers, simulation.Scene.Match.Phase);
            simulation.ReplayMayStart = true;
            simulation.Step(network, 3); simulation.Step(network, 183);
            Assert.Equal(MatchPhase.Playing, simulation.Scene.Match.Phase);
            simulation.Scene.Match.CaptureResult(183);
            var report = simulation.Reports.Complete(simulation.Scene, 183);
            Assert.NotNull(report); Assert.Equal(recording.ReplayId, report.ReplayId);
            Assert.Null(report.TournamentId); Assert.Null(report.RoundId);
            recording.Complete(); await recording.Completion;
            Assert.Throws<InvalidOperationException>(() => ServerReplayPolicy.MayStart(true, recording));
        }
        finally { recording.Complete(); await recording.Completion; Directory.Delete(directory, true); }
    }
}
