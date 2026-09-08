using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FruityPrime.Server.Shared;
using FruityPrime.Server.Worker;
using FruityPrime.Server.Worker.Simulation;
using MphRead.Identity;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class WorkerRuntimeTests
{
    [Trait("RequiresGameContent", "true")]
    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 2)]
    [InlineData(16, 4)]
    public async Task RealBotWorldsRemainIndependentOnDedicatedLanes(int count, int lanes)
    {
        using var context = ContentEnvironment.PreserveContext("AMHE1");
        ContentEnvironment.Open(Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY") ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1"), "AMHE1");
        using var content = ContentEnvironment.AcquireContent();
        var options = new WorkerOptions { MaxMatches = count, SimulationLanes = lanes, MaxMatchesPerLane = count,
            PlacementP99Milliseconds = 10000, PlacementCpuPercent = 100, MinimumMemoryHeadroomBytes = 0 };
        var hub = new WorkerNetworkHub(new SilentTransport(), options.Incarnation, new RoutedMatchDatagramRouter(), count);
        await using var runtime = new WorkerRuntime(options, content.Content, hub);
        using var signer = new WorkerAdmissionIssuer("test-key");
        await runtime.UpdateSigningKeyAsync(new(signer.KeyId, signer.ExportPublicKey()));
        var specs = Enumerable.Range(0, count).Select(i => Spec(content.Content, options, i)).ToArray();
        var replies = await Task.WhenAll(specs.Select(runtime.CreateAsync));
        Assert.All(replies, reply => Assert.IsType<MatchReady>(reply));
        Assert.Equal(count, replies.Cast<MatchReady>().Select(reply => reply.Placement.WireMatchId).Distinct().Count());
        Assert.Equal(count, runtime.Heartbeat().Capacity.ActiveMatches);
        var worlds = await Task.WhenAll(specs.Select(spec => runtime.InvokeMatchAsync(spec.MatchId, match => match.Simulation.Scene)));
        Assert.Equal(count, worlds.Distinct().Count());
        for (int i = 0; i < count; i++)
        {
            int value = i + 100;
            await runtime.InvokeMatchAsync(specs[i].MatchId, match => { match.Simulation.Scene.Roster.Nicknames[0] = value.ToString(); return true; });
        }
        var names = await Task.WhenAll(specs.Select(spec => runtime.InvokeMatchAsync(spec.MatchId, match => match.Simulation.Scene.Roster.Nicknames[0])));
        Assert.Equal(Enumerable.Range(100, count).Select(value => value.ToString()), names);
        Assert.All(await Task.WhenAll(specs.Select(spec => runtime.GetStatusAsync(spec.MatchId))), status => Assert.Equal(2, status!.PlayerCount));
        Assert.Equal(replies[0], await runtime.CreateAsync(specs[0]));
        Assert.IsType<MatchFailed>(await runtime.CreateAsync(specs[0] with { Rng1Seed = 9 }));
        runtime.Drain();
        Assert.IsType<MatchFailed>(await runtime.CreateAsync(Spec(content.Content, options, 100)));
        await runtime.CancelAsync(specs[0].MatchId);
        // Queue a barrier on every survivor after cancellation; only the selected world may terminate.
        foreach (var spec in specs.Skip(1))
            Assert.Equal(MatchInstanceState.Running, (await runtime.GetStatusAsync(spec.MatchId))!.State);
    }

    [Fact]
    public async Task LaneShutdownCompletesEveryAcceptedCommandAndRejectsOwnerDisposal()
    {
        var lane = new SimulationLane(0, 4096);
        await lane.InvokeAsync(() => { Assert.Throws<InvalidOperationException>(lane.Dispose); return true; });
        var commands = Enumerable.Range(0, 500).Select(_ => Task.Run(async () =>
        {
            try { Assert.Equal(lane.OwnerThreadId, await lane.InvokeAsync(() => Environment.CurrentManagedThreadId)); }
            catch (ObjectDisposedException) { }
        })).ToArray();
        await Task.Run(lane.Dispose);
        await Task.WhenAll(commands).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => lane.InvokeAsync(() => 1));
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public async Task FaultyTerminalCallbackDoesNotKillTheOtherWorldOrLane()
    {
        using var context = ContentEnvironment.PreserveContext("AMHE1");
        ContentEnvironment.Open(Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY") ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1"), "AMHE1");
        using var content = ContentEnvironment.AcquireContent();
        var options = new WorkerOptions();
        using var lane = new SimulationLane(0, 32);
        MatchInstance? survivor = null;
        await lane.InvokeAsync(() =>
        {
            var stopped = new MatchInstance(new(Spec(content.Content, options, 0), 1), new SilentTransport());
            survivor = new MatchInstance(new(Spec(content.Content, options, 1), 2), new SilentTransport());
            stopped.Start(); survivor.Start();
            lane.Add(stopped, _ => throw new InvalidOperationException("Injected completion handler failure"));
            lane.Add(survivor, match => match.Dispose());
            stopped.RequestStop(MatchStopReason.Requested);
            return true;
        });
        await Task.Delay(80);
        Assert.True(await lane.InvokeAsync(() => survivor!.NextTick > 0));
        Assert.IsType<InvalidOperationException>(lane.LastCallbackFailure);
    }

    [Fact]
    public async Task StartupTokenReadIsBoundedAndRequiresOneToken()
    {
        string token = new('a', 64);
        Assert.Equal(token, await FruityPrime.Server.Worker.Program.ReadStartupTokenAsync(new StringReader(token + "\n"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => FruityPrime.Server.Worker.Program.ReadStartupTokenAsync(new StringReader(new string('a', 10000)), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => FruityPrime.Server.Worker.Program.ReadStartupTokenAsync(new StringReader(token + "\nextra"), CancellationToken.None));
    }

    private static MatchSpec Spec(WorkerContent content, WorkerOptions options, int index) => new(new(Guid.NewGuid()), new(Guid.NewGuid()),
        new(Guid.NewGuid()), Guid.NewGuid(), new MatchRules(index % 3 == 0 ? MatchMode.Defender : MatchMode.Battle, index % 2 == 0 ? "MP1 SANCTORUS" : "MP2 HARVESTER", maxPlayers: 2, timeLimit: TimeSpan.FromMinutes(5)),
        new(index % 2 == 0 ? "MP1 SANCTORUS" : "MP2 HARVESTER", content.ContentHash, content.Version, options.BuildVersion, NetHeader.Version), MatchTrustClass.Community,
        null, null, ImmutableArray.Create(new RosterSeat(0, null, null, "Spire", Hunter.Spire, 0, SeatRole.Bot, false),
            new RosterSeat(1, null, null, "Samus", Hunter.Samus, 1, SeatRole.Bot, false)),
        FruityPrime.Server.Shared.BotFillPolicy.Disabled, ObserverPolicy.Disabled, ReplayPolicy.Disabled, TelemetryPolicy.Disabled,
        (uint)(index + 123), (uint)(index + 456));

    private sealed class SilentTransport : INetTransport
    {
        public int LocalPort => 27666;
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
