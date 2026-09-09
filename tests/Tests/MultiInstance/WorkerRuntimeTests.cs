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
    [Theory]
    [InlineData(WorkerLagCompensationMode.Off, false, false, false)]
    [InlineData(WorkerLagCompensationMode.Players, true, true, false)]
    [InlineData(WorkerLagCompensationMode.Dynamic, true, true, true)]
    public void WorkerLagCompensationModeMapsToMatchRuntime(
        WorkerLagCompensationMode mode, bool players, bool projectiles, bool dynamic)
    {
        var options = new WorkerOptions { LagCompensationMode = mode };
        options.Validate();

        var resolved = options.ResolveLagCompensation();

        Assert.Equal(players, resolved.LagCompEnabled);
        Assert.Equal(projectiles, resolved.ProjectileCatchUpEnabled);
        Assert.Equal(dynamic, resolved.HistoricalDynamicCollisionEnabled);
    }

    [Fact]
    public void WorkerLagCompensationModeDefaultsToPlayersAndParsingIsExplicit()
    {
        Assert.Equal(WorkerLagCompensationMode.Players, new WorkerOptions().LagCompensationMode);
        Assert.Equal(WorkerLagCompensationMode.Off, WorkerOptions.ParseLagCompensationMode("off"));
        Assert.Equal(WorkerLagCompensationMode.Players, WorkerOptions.ParseLagCompensationMode("players"));
        Assert.Equal(WorkerLagCompensationMode.Dynamic, WorkerOptions.ParseLagCompensationMode("dynamic"));
        Assert.Throws<ArgumentException>(() => WorkerOptions.ParseLagCompensationMode("on"));
        Assert.Throws<ArgumentException>(() => new WorkerOptions
        {
            LagCompensationMode = (WorkerLagCompensationMode)99
        }.Validate());
    }

    [Fact]
    public void DeveloperValidationFixtureDefaultsOffAndCliIsStrict()
    {
        Assert.Equal(DeveloperValidationFixtureId.None, new WorkerOptions().ValidationFixture);
        Assert.Equal(DeveloperValidationFixtureId.None,
            WorkerOptions.ParseValidationFixture("none"));
        Assert.Equal(DeveloperValidationFixtureId.Unit1Rm1Dynamic,
            WorkerOptions.ParseValidationFixture("unit1-rm1-dynamic"));
        Assert.Throws<ArgumentException>(() => WorkerOptions.ParseValidationFixture("Unit1Rm1Dynamic"));
        Assert.Throws<ArgumentException>(() => FruityPrime.Server.Worker.Program.ParseArguments(
            ["--validation-fixture", "unit1-rm1-dynamic", "--validation-fixture", "none"]));
        Assert.Throws<ArgumentException>(() => FruityPrime.Server.Worker.Program.ParseArguments(
            ["--fixture-path", "/tmp/arbitrary"]));

        new WorkerOptions
        {
            ValidationFixture = DeveloperValidationFixtureId.Unit1Rm1Dynamic
        }.Validate();
        Assert.Throws<ArgumentException>(() => new WorkerOptions
        {
            ValidationFixture = DeveloperValidationFixtureId.Unit1Rm1Dynamic,
            AdvertisedHost = "192.0.2.1"
        }.Validate());
        Assert.Throws<ArgumentException>(() => new WorkerOptions
        {
            ValidationFixture = DeveloperValidationFixtureId.Unit1Rm1Dynamic,
            SimulationLanes = 2,
            MaxMatches = 2
        }.Validate());
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void DeveloperValidationFixtureIsFingerprintBoundAndAbsentFromGlobalCatalogs()
    {
        using var context = ContentEnvironment.PreserveContext("AMHE1");
        ContentEnvironment.Open(Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1"), "AMHE1");
        using var content = ContentEnvironment.AcquireContent();
        DeveloperValidationFixtureDescriptor descriptor
            = DeveloperValidationFixtures.Require(DeveloperValidationFixtureId.Unit1Rm1Dynamic);
        DeveloperValidationFixtureContentEvidence evidence
            = DeveloperValidationFixtures.ValidateCurrentContent(descriptor.Id);

        Assert.Equal("c6678a005510980d182363fc1d4adf940942f8c3fcb1c28baffb149b4b61bbec",
            descriptor.Fingerprint);
        Assert.Equal((2, 10, 11, 3, 20), (evidence.ActivePlayerSpawns,
            evidence.Doors, evidence.ForceFields, evidence.Platforms, evidence.Objects));
        Assert.Equal(46, evidence.AdmittedEntities);
        Assert.True(evidence.RawEntities > evidence.AdmittedEntities);
        Assert.Equal(evidence.RawEntities - evidence.AdmittedEntities, evidence.SkippedEntities);
        Assert.True(evidence.DynamicEntities > 0);
        Assert.DoesNotContain(Metadata.RoomList, room => room.Name == descriptor.MapKey);
        Assert.DoesNotContain(descriptor.MapKey, content.Content.SupportedRooms);
        Assert.All(descriptor.Resources, resource =>
        {
            Assert.False(Path.IsPathRooted(resource.Path));
            Assert.DoesNotContain("..", resource.Path.Split('/'));
        });

        Assert.Throws<ProgramException>(() => DeveloperValidationFixtures.ValidateContent(
            descriptor, "AMHE0", _ => throw new InvalidOperationException()));
        Assert.Throws<ProgramException>(() => DeveloperValidationFixtures.ValidateContent(
            descriptor, "AMHE1", _ => throw new FileNotFoundException()));
        Assert.Throws<ProgramException>(() => DeveloperValidationFixtures.ValidateContent(
            descriptor, "AMHE1", resource => new byte[checked((int)descriptor.Resources
                .Single(item => item.Path == resource).Bytes)]));
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public async Task DeveloperValidationFixtureRejectsWrongIdentityAndSecondDistinctMatch()
    {
        using var context = ContentEnvironment.PreserveContext("AMHE1");
        ContentEnvironment.Open(Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1"), "AMHE1");
        using var content = ContentEnvironment.AcquireContent();
        using var storage = new ValidationFixtureStorage();
        var options = new WorkerOptions
        {
            ValidationFixture = DeveloperValidationFixtureId.Unit1Rm1Dynamic,
            PlacementP99Milliseconds = 10000,
            PlacementCpuPercent = 100,
            MinimumMemoryHeadroomBytes = 0,
            ReplayDirectory = storage.Replays,
            ArtifactDirectory = storage.Artifacts
        };
        var hub = new WorkerNetworkHub(new SilentTransport(), options.Incarnation,
            new RoutedMatchDatagramRouter(), 1);
        await using var runtime = new WorkerRuntime(options, content.Content, hub);
        using var signer = new WorkerAdmissionIssuer("fixture-test");
        await runtime.UpdateSigningKeyAsync(new(signer.KeyId, signer.ExportPublicKey()));
        MatchSpec spec = ValidationFixtureSpec(content.Content, options, 0);
        Assert.Throws<ArgumentException>(() => runtime.Configure(new(spec.NodeId, spec.NodeIncarnation,
            new WorkerCapacity(1, 1, 0, 0))));
        _ = runtime.Configure(new(spec.NodeId, spec.NodeIncarnation,
            new WorkerCapacity(1, 2, 0, 0)));

        foreach (ContentIdentity wrong in new[]
        {
            spec.Content with { ContentVersion = "AMHE0" },
            spec.Content with { ContentHash = new string('0', 64) },
            spec.Content with { BuildVersion = "wrong-build" },
            spec.Content with { ProtocolVersion = (byte)(NetHeader.Version + 1) }
        })
            Assert.IsType<MatchFailed>(await runtime.CreateAsync(spec with
                { MatchId = new(Guid.NewGuid()), Content = wrong }));

        MatchSpec wrongMap = spec with
        {
            MatchId = new(Guid.NewGuid()),
            Rules = spec.Rules.With(roomKey: "NOT THE FIXTURE"),
            Content = spec.Content with { MapKey = "NOT THE FIXTURE" }
        };
        MatchSpec[] wrongShape =
        [
            wrongMap,
            spec with { MatchId = new(Guid.NewGuid()), Rules = spec.Rules.With(mode: MatchMode.Defender) },
            spec with { MatchId = new(Guid.NewGuid()), Rules = spec.Rules.With(maxPlayers: 3) },
            spec with { MatchId = new(Guid.NewGuid()), TrustClass = MatchTrustClass.Practice },
            spec with { MatchId = new(Guid.NewGuid()), TournamentId = Guid.NewGuid(), RoundId = Guid.NewGuid() },
            spec with { MatchId = new(Guid.NewGuid()), Roster = spec.Roster.SetItem(1,
                spec.Roster[1] with { Hunter = Hunter.Spire }) },
            spec with { MatchId = new(Guid.NewGuid()), BotFillPolicy = FruityPrime.Server.Shared.BotFillPolicy.Disabled },
            spec with { MatchId = new(Guid.NewGuid()), ObserverPolicy = ObserverPolicy.Allowed },
            spec with { MatchId = new(Guid.NewGuid()), ReplayPolicy = ReplayPolicy.Disabled },
            spec with { MatchId = new(Guid.NewGuid()), TelemetryPolicy = TelemetryPolicy.Disabled }
        ];
        foreach (MatchSpec wrong in wrongShape)
            Assert.IsType<MatchFailed>(await runtime.CreateAsync(wrong));
        Assert.Throws<ArgumentException>(() =>
        {
            _ = runtime.CreateAsync(spec with
            {
                MatchId = new(Guid.NewGuid()),
                Rules = spec.Rules.With(roomKey: "RULES ONLY")
            });
        });

        WorkerEvent response = await runtime.CreateAsync(spec);
        Assert.True(response is MatchReady, (response as MatchFailed)?.Reason);
        MatchReady ready = (MatchReady)response;
        Assert.Equal(ready, await runtime.CreateAsync(spec));
        int[] colliders = await runtime.InvokeMatchAsync(spec.MatchId, match => new[]
        {
            match.Simulation.Combat.HistoricalCollisionRegistry.CountKind(MphRead.Runtime.HistoricalCollision.HistoricalColliderKind.Door),
            match.Simulation.Combat.HistoricalCollisionRegistry.CountKind(MphRead.Runtime.HistoricalCollision.HistoricalColliderKind.ForceField),
            match.Simulation.Combat.HistoricalCollisionRegistry.CountKind(MphRead.Runtime.HistoricalCollision.HistoricalColliderKind.Object),
            match.Simulation.Combat.HistoricalCollisionRegistry.CountKind(MphRead.Runtime.HistoricalCollision.HistoricalColliderKind.Platform)
        });
        Assert.Equal(new[] { 10, 11, 9, 3 }, colliders);
        Assert.IsType<MatchFailed>(await runtime.CreateAsync(
            ValidationFixtureSpec(content.Content, options, 1)));
    }

    [Trait("RequiresGameContent", "true")]
    [Theory]
    [InlineData(WorkerLagCompensationMode.Off, false, false, false)]
    [InlineData(WorkerLagCompensationMode.Players, true, true, false)]
    [InlineData(WorkerLagCompensationMode.Dynamic, true, true, true)]
    public async Task WorkerRuntimeAppliesLagCompensationModeToCreatedMatch(
        WorkerLagCompensationMode mode, bool players, bool projectiles, bool dynamic)
    {
        using var context = ContentEnvironment.PreserveContext("AMHE1");
        ContentEnvironment.Open(Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1"), "AMHE1");
        using var content = ContentEnvironment.AcquireContent();
        var options = new WorkerOptions
        {
            LagCompensationMode = mode,
            PlacementP99Milliseconds = 10000,
            PlacementCpuPercent = 100,
            MinimumMemoryHeadroomBytes = 0
        };
        var hub = new WorkerNetworkHub(new SilentTransport(), options.Incarnation,
            new RoutedMatchDatagramRouter(), 1);
        await using var runtime = new WorkerRuntime(options, content.Content, hub);
        using var signer = new WorkerAdmissionIssuer("lag-mode-test");
        await runtime.UpdateSigningKeyAsync(new(signer.KeyId, signer.ExportPublicKey()));
        MatchSpec spec = Spec(content.Content, options, 0);

        Assert.IsType<MatchReady>(await runtime.CreateAsync(spec));
        var actual = await runtime.InvokeMatchAsync(spec.MatchId, match =>
            (match.Simulation.Combat.LagCompEnabled,
                match.Simulation.Combat.ProjectileCatchUpEnabled,
                match.Simulation.Combat.HistoricalDynamicCollisionEnabled));

        Assert.Equal((players, projectiles, dynamic), actual);
    }

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

    private static MatchSpec ValidationFixtureSpec(WorkerContent content,
        WorkerOptions options, int index)
    {
        DeveloperValidationFixtureDescriptor descriptor
            = DeveloperValidationFixtures.Require(options.ValidationFixture);
        return new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), Guid.NewGuid(),
            new MatchRules(MatchMode.Battle, descriptor.MapKey, maxPlayers: 2,
                timeLimit: TimeSpan.FromMinutes(5)),
            new(descriptor.MapKey, content.ContentHash, content.Version,
                options.BuildVersion, NetHeader.Version),
            MatchTrustClass.Community, null, null,
            ImmutableArray.Create(
                new RosterSeat(0, new(Guid.NewGuid()), null, "WanProbe", Hunter.Noxus, 0,
                    SeatRole.Player, false),
                new RosterSeat(1, null, null, "Bot1", Hunter.Samus, 1,
                    SeatRole.Bot, false)),
            FruityPrime.Server.Shared.BotFillPolicy.FillVacancies,
            ObserverPolicy.Disabled, ReplayPolicy.Record, TelemetryPolicy.Record,
            (uint)(index + 900), (uint)(index + 1200));
    }

    private sealed class ValidationFixtureStorage : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            "prime-validation-fixture-" + Guid.NewGuid().ToString("N"));
        public string Replays { get; }
        public string Artifacts { get; }

        public ValidationFixtureStorage()
        {
            Replays = Path.Combine(_root, "replays");
            Artifacts = Path.Combine(_root, "artifacts");
            Directory.CreateDirectory(Replays);
            Directory.CreateDirectory(Artifacts);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

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
