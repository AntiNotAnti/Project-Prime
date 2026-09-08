using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class ServerSimulationIsolationTests
{
    private const int Ticks = 600;

    [Trait("RequiresGameContent", "true")]
    [Theory]
    [InlineData("MP1 SANCTORUS", MatchMode.Battle)]
    [InlineData("MP1 SANCTORUS", MatchMode.Defender)]
    [InlineData("MP4 HIGHGROUND", MatchMode.Battle)]
    public void RealBotMatchTraceSurvivesBusyOtherMatchConstructionResetAndClose(string otherRoom, MatchMode mode)
    {
        using var context = OpenContent();
        string[] expected = RunAlone(mode);
        using var first = new Harness("MP1 SANCTORUS", mode);
        Harness? other = null;
        try
        {
            for (uint tick = 0; tick < Ticks; tick++)
            {
                if (tick % 200 == 0)
                {
                    string before = Trace(first.Simulation);
                    if (other != null)
                    {
                        other.Simulation.Scene.Match.CaptureResult(tick);
                        other.Step(tick);
                        Assert.Equal(before, Trace(first.Simulation));
                        other.Dispose();
                    }
                    other = new Harness(otherRoom, mode);
                    // Initial Step exercises the real countdown reset before world stepping.
                    other.Step(0);
                    Assert.Equal(before, Trace(first.Simulation));
                }
                for (int draw = 0; draw < 1000; draw++) other!.Simulation.Scene.Random.GetRandomInt2(100);
                other!.Step(tick + 1);
                first.Step(tick);
                Assert.Equal(expected[tick], Trace(first.Simulation));
            }
            Assert.True(first.Simulation.Scene.FrameCount > 0);
            Assert.Equal(3, first.Simulation.Bots.Count);
        }
        finally { other?.Dispose(); }
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void SixteenRealScenesSteppedConcurrentlyMatchSerialBotTrace()
    {
        using var context = OpenContent();
        string[] expected = RunAlone();
        var matches = new List<Harness>();
        try
        {
            for (int i = 0; i < 16; i++) matches.Add(new Harness("MP1 SANCTORUS"));
            for (uint tick = 0; tick < Ticks; tick++)
            {
                uint current = tick;
                Parallel.ForEach(matches, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(8, Environment.ProcessorCount) }, match =>
                {
                    match.Step(current);
                    Assert.Equal(expected[current], Trace(match.Simulation));
                    if (current == Ticks - 1) Assert.True(match.Simulation.Scene.FrameCount > 0);
                });
            }
        }
        finally { foreach (var match in matches) match.Dispose(); }
    }

    private static string[] RunAlone(MatchMode mode = MatchMode.Battle)
    {
        using var match = new Harness("MP1 SANCTORUS", mode);
        return Enumerable.Range(0, Ticks).Select(tick => { match.Step((uint)tick); return Trace(match.Simulation); }).ToArray();
    }

    private static IDisposable OpenContent()
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
        var context = ServerContent.PreserveContext("AMHE1");
        try { ServerContent.Open(data, "AMHE1"); return context; }
        catch { context.Dispose(); throw; }
    }

    private static string Trace(ServerSimulation simulation)
    {
        var scene = simulation.Scene;
        // Transport identities are deliberately absent: bot identities are allocated
        // cryptographically, and are not deterministic simulation state.
        var states = simulation.States.ToArray().Select(state =>
        {
            state.ConnectionId = 0;
            byte[] bytes = new byte[SnapshotPlayer.Size];
            state.Write(bytes);
            return Convert.ToHexString(bytes);
        }).ToArray();
        var world = new WorldStateCapture();
        world.Capture(scene, 1, 1, 0);
        var worldBytes = world.Records.ToArray().Select(record =>
        {
            byte[] bytes = new byte[WorldRecord.Size];
            record.Write(bytes);
            return Convert.ToHexString(bytes);
        }).ToArray();
        var counters = typeof(MatchRuntime).GetProperties(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(p => p.PropertyType.IsArray).OrderBy(p => p.Name)
            .ToDictionary(p => p.Name, p => ((Array)p.GetValue(scene.Match)!).Cast<object>().ToArray());
        return JsonSerializer.Serialize(new
        {
            scene.FrameCount, scene.LiveFrames, scene.Random.Rng1, scene.Random.Rng2,
            scene.Match.Phase, scene.Match.Period, scene.Match.MatchTime, scene.Match.PrimeHunter,
            States = states, World = worldBytes, Counters = counters,
            scene.BotRuntimeState.VisibilityIndex1, scene.BotRuntimeState.VisibilityIndex2,
            Visibility = scene.BotRuntimeState.PlayerVisibility.Cast<bool>().ToArray()
        }, new JsonSerializerOptions { IncludeFields = true });
    }

    private sealed class Harness : IDisposable
    {
        public ServerSimulation Simulation { get; }
        private readonly ServerNetwork _network;
        public Harness(string room, MatchMode mode = MatchMode.Battle)
        {
            Simulation = new ServerSimulation(new MatchRules(mode, room), botFill: new(4), rng1: 12345, rng2: 67890);
            _network = new ServerNetwork(new SilentTransport(), Simulation.Scene.Match.Rules);
            var connection = new NetConnection(100, new IPEndPoint(IPAddress.Loopback, 10001), 1, 0);
            connection.Ready(1);
            var peer = new ServerPeer(connection, new JoinPacket { Hunter = Hunter.Samus, Name = "Human", Nonce = 100 }, 0, 0);
            ((ServerPeer?[])typeof(ServerNetwork).GetField("_peers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_network)!)[0] = peer;
        }
        public void Step(uint tick)
        {
            InputButtons buttons = tick % 60 < 30 ? InputButtons.Forward | InputButtons.Shoot : InputButtons.Left;
            _network.Peers[0]!.Inputs.Receive(new[] { new InputCommand(tick, tick, tick, buttons, InputButtons.None,
                -OpenTK.Mathematics.Vector3.UnitZ, InputCommand.NoWeapon) }, tick);
            Simulation.Step(_network, tick);
        }
        public void Dispose() => Simulation.Dispose();
    }

    private sealed class SilentTransport : INetTransport
    {
        public int LocalPort => 0;
        public long PacketsDropped => 0;
        public int QueuedPackets => 0;
        public int HeldIncomingPackets => 0;
        public int HeldOutgoingPackets => 0;
        public NetTrafficMetrics Metrics { get; } = new();
        public void Dispose() { }
        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> datagram = default) { }
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { }
        public void AnswerPingsImmediately() { }
        public IEnumerable<ReceivedPacket> Drain() => Array.Empty<ReceivedPacket>();
        public void EnqueueForPlayback(byte[] data, int length) => throw new NotSupportedException();
        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> payload, long extraHoldTicks = 0) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram, long extraHoldTicks) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram) { }
    }
}
