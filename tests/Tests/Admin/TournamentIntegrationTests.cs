using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using MphRead.Admin;
using MphRead.Combat;
using MphRead.Mods.Network;
using MphRead.Replay;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Admin;

[Collection("Match baseline globals")]
public sealed class TournamentIntegrationTests
{
    [Fact]
    public void RealSimulationRequiresReadyCheckAndCancelsPrestartWithoutFreezingPlaying()
    {
        using var saved = ServerContent.PreserveContext("AMHE1");
        string root = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY") ?? Directory.GetCurrentDirectory();
        while (!Directory.Exists(Path.Combine(root, "AMHE1"))) root = Directory.GetParent(root)?.FullName ?? throw new DirectoryNotFoundException("AMHE1 required.");
        ServerContent.Open(Path.Combine(root, "AMHE1"), "AMHE1");
        var rules = new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 1);
        using var simulation = new ServerSimulation(rules);
        using var transport = new NetTransport(0);
        var network = new ServerNetwork(transport, rules);
        var connection = new NetConnection(100, new IPEndPoint(IPAddress.Loopback, 23001), 1, 0);
        connection.Ready(1);
        var peer = new ServerPeer(connection, new JoinPacket { Nonce = 99, Name = "AdminTest", Hunter = Hunter.Samus }, 0, 0);
        ((ServerPeer?[])typeof(ServerNetwork).GetField("_peers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!)[0] = peer;
        ((ServerPeer?[])typeof(ServerNetwork).GetField("_connections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(network)!)[0] = peer;
        var admin = new TournamentAdmin(() => simulation, network, (_, _) => rules, _ => { }, () => null);
        Guid Submit(AdminCommandKind kind, ulong? id = null)
        {
            var command = new AdminCommand(Guid.NewGuid(), kind, DateTimeOffset.UtcNow, ConnectionId: id);
            Assert.Equal("queued", admin.Commands.Submit(command).State); return command.RequestId;
        }
        admin.BeforeStep(0, null); simulation.Step(network, 0);
        Assert.Equal(MatchPhase.WaitingForPlayers, simulation.Scene.Match.Phase);
        var unchangedStatus = admin.Status; admin.BeforeStep(1, null); Assert.Same(unchangedStatus, admin.Status);
        Submit(AdminCommandKind.ReadyCheck); Submit(AdminCommandKind.ConfirmReady, 100);
        admin.Commands.Submit(new(Guid.NewGuid(), AdminCommandKind.SetRoundIdentity, DateTimeOffset.UtcNow, TournamentId: "cup-1", RoundId: "round-1"));
        admin.BeforeStep(1, null); simulation.Step(network, 1);
        Submit(AdminCommandKind.StartCountdown); admin.BeforeStep(2, null); simulation.Step(network, 2);
        Assert.Equal(MatchPhase.Countdown, simulation.Scene.Match.Phase);
        Submit(AdminCommandKind.CancelBeforeStart); admin.BeforeStep(3, null); simulation.Step(network, 3);
        Assert.Equal(MatchPhase.WaitingForPlayers, simulation.Scene.Match.Phase);
        Submit(AdminCommandKind.ConfirmReady, 100); Submit(AdminCommandKind.StartCountdown);
        admin.BeforeStep(4, null); simulation.Step(network, 4);
        admin.BeforeStep(184, null); simulation.Step(network, 184);
        Assert.Equal(MatchPhase.Playing, simulation.Scene.Match.Phase);
        ulong before = simulation.Scene.FrameCount;
        Submit(AdminCommandKind.PauseBetweenRounds); var rejected = Submit(AdminCommandKind.CancelBeforeStart);
        admin.BeforeStep(185, null); simulation.Step(network, 185);
        Assert.Equal(MatchPhase.Playing, simulation.Scene.Match.Phase);
        Assert.True(simulation.Scene.FrameCount > before); Assert.False(admin.RotationAllowed);
        Assert.Equal("rejected", admin.Commands.Find(rejected)!.State);
    }

    [Fact]
    public async Task ServerRecordingProducesIndexedObserverBaselineAndExplicitStorageFailure()
    {
        using var state = new MatchBaselineTests.State(); state.Configure(GameMode.Battle);
        string directory = Directory.CreateTempSubdirectory("prime-server-replay-test-").FullName;
        try
        {
            byte[] snapshot = new byte[NetConfig.MaxPacketSize];
            int size = new SnapshotPacket(100, 1, 1, 0, false, 1, 2).Write(snapshot, Array.Empty<SnapshotPlayer>());
            Array.Resize(ref snapshot, size);
            byte[] world = new byte[WorldPacket.MaxSize];
            var match = new WorldRecord(WorldRecordKind.Match, 255, 0, 0, Vector3.Zero,
                (uint)GameMode.Battle, (uint)MatchPhase.Ending, 0, uint.MaxValue, 1);
            size = WorldPacket.Write(world, 1, 1, 100, new[] { match }, 0); Array.Resize(ref world, size);
            byte[] roster = new byte[4 + SessionRosterPacket.MaxSize]; BinaryPrimitives.WriteUInt32LittleEndian(roster, 1);
            size = SessionRosterPacket.Write(roster.AsSpan(4), 1, new[] { new NetRosterEntry(0, 10, Hunter.Samus, 0, "OldKiller"), new NetRosterEntry(1, 20, Hunter.Kanden, 1, "Victim") }); Array.Resize(ref roster, size + 4);
            state.Scene.Match.PhaseRevision = 1;
            var kill = new KillEvent(1, 100, 1, 1, new(0, 10, 1), new(1, 20, 1), 0, KillEventFlags.Headshot, []);
            byte[] killPayload = new byte[4 + KillEvent.Size]; BinaryPrimitives.WriteUInt32LittleEndian(killPayload, 1); kill.Write(killPayload.AsSpan(4));
            var frame = new ObserverFrame(100, 1, state.Scene.Match.Rules, snapshot, new[] { world }, roster, 1, true, true, new[] { new ObserverEvent(ReliableEventType.Kill, killPayload) });
            var recording = new ServerReplaySession(directory);
            recording.Capture(frame, state.Scene);
            recording.Capture(frame with { Tick = 101, Events = Array.Empty<ObserverEvent>() }, state.Scene);
            recording.Complete(); await recording.Completion;
            Assert.Equal("complete", recording.Status.State);
            using var reader = DemoReader.Open(Path.Combine(directory, recording.ReplayId.ToString("D") + DemoFile.Extension));
            Assert.NotNull(reader); Assert.Equal(DemoFile.IndexedFormatVersion, reader.FormatVersion);
            var records = reader.Seek(0, out uint restored); Assert.NotNull(records); Assert.Equal((uint)0, restored);
            Assert.Contains(records, r => r.Data.Length == 2 && r.Data[0] == (byte)DemoRecordKind.Perspective && r.Data[1] == 255);
            var clock = Assert.Single(records, r => r.Data[0] == (byte)DemoRecordKind.Clock);
            Assert.Equal(state.Scene.FrameCount, BinaryPrimitives.ReadUInt64LittleEndian(clock.Data.AsSpan(1)));
            Assert.Contains(reader.Index, e => e.Marker == ReplayMarker.MatchEnd);
            Assert.Contains(reader.Index, e => (e.Marker & (ReplayMarker.Kill | ReplayMarker.Headshot)) == (ReplayMarker.Kill | ReplayMarker.Headshot));
            var observerFeedback = new ServerReplayFeedback();
            observerFeedback.Bind(frame, 1); observerFeedback.Apply(frame);
            observerFeedback.Bind(frame with { Tick = 4100, Events = Array.Empty<ObserverEvent>() }, 1);
            var fragments = observerFeedback.Checkpoint(); Assert.NotEmpty(fragments);
            byte[] feedbackBytes = new byte[BinaryPrimitives.ReadInt32LittleEndian(fragments[0].AsSpan(5))];
            foreach (var fragment in fragments)
            {
                int offset = BinaryPrimitives.ReadUInt16LittleEndian(fragment.AsSpan(1)) * (NetConfig.MaxPacketSize - 9);
                fragment.AsSpan(9).CopyTo(feedbackBytes.AsSpan(offset));
            }
            var feedback = new CombatFeedback(); Assert.True(ReplayFeedbackState.Restore(feedbackBytes, feedback, new WorldFeedback()));
            Assert.Equal(CombatActor.None, feedback.Local); Assert.Equal(1, feedback.FeedCount);
            Assert.Contains("OldKiller", feedback.FeedAt(0).Text); Assert.False(feedback.Process(kill));
            using var linear = DemoReader.Open(Path.Combine(directory, recording.ReplayId.ToString("D") + DemoFile.Extension));
            Assert.Equal((byte)DemoRecordKind.Match, linear!.ReadNext()!.Value.Data[0]);
            bool sawLaterSnapshot = false;
            while (linear.ReadNext() is { } record) if (record.Frame == 0 && record.Data[0] == (byte)DemoRecordKind.Snapshot) sawLaterSnapshot = true;
            Assert.True(sawLaterSnapshot);
            var gap = new ServerReplaySession(directory);
            gap.Capture(frame, state.Scene);
            gap.Capture(frame with { Tick = 102 }, state.Scene);
            gap.Complete(); await gap.Completion;
            Assert.Equal("failed", gap.Status.State);
            string blocker = Path.Combine(directory, "file"); File.WriteAllText(blocker, "not a directory");
            var failed = new ServerReplaySession(blocker); failed.Complete(); await failed.Completion;
            Assert.Equal("failed", failed.Status.State);
        }
        finally { Directory.Delete(directory, true); }
    }
}
