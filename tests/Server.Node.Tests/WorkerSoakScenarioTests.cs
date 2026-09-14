using ProjectPrime.Server.Shared;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.WorkerSoak;
using MphRead;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class WorkerSoakScenarioTests
{
    [Fact]
    public async Task AuthenticatedSoakActorsRefuseKeylessPlacements()
    {
        var placement = new MatchPlacement(new(Guid.NewGuid()), new(1), new(Guid.NewGuid()),
            Guid.NewGuid(), "127.0.0.1", 1, UdpAuthenticationEnabled: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SoakClientActor.CreateAsync(
            default!, placement, Array.Empty<LobbyIdentity>(), null!));
    }

    [Fact]
    public void RosterValidationKeepsActiveAndObserverBoundsExplicit()
    {
        var roster = new SoakRosterOptions(2, 3, 4);
        roster.Validate();
        Assert.Equal(5, roster.ActiveSeats);
        Assert.Equal(9, roster.RosterSeats);
        Assert.Throws<ArgumentException>(() => new SoakRosterOptions(0, 1, 0).Validate());
        Assert.Throws<ArgumentException>(() => new SoakRosterOptions(8, 1, 0).Validate());
    }

    [Fact]
    public void ScenarioFlagsAndRematchBoundsAreParsedWithoutImplicitFaults()
    {
        var options = new Dictionary<string, string>
        {
            ["--seconds"] = "180", ["--matches"] = "4", ["--workers"] = "2", ["--lanes"] = "2",
            ["--round-seconds"] = "30", ["--crash-seconds"] = "120", ["--outages"] = "false",
            ["--reconnects"] = "false", ["--rematches"] = "true", ["--rematch-every"] = "2",
            ["--players"] = "2", ["--bots"] = "1", ["--observers"] = "2"
        };
        var scenario = SoakScenarioOptions.From(options);
        Assert.True(scenario.CrashesEnabled);
        Assert.False(scenario.OutagesEnabled);
        Assert.False(scenario.ReconnectsEnabled);
        Assert.True(scenario.RematchesEnabled);
        Assert.Equal(8, scenario.TargetMatches);
        Assert.Equal(32, scenario.TargetClientPeers);
        var invalid = new Dictionary<string, string>(options) { ["--rematch-every"] = "0" };
        Assert.Throws<ArgumentException>(() => SoakScenarioOptions.From(invalid));
    }

    [Fact]
    public void RoundTargetSupportsTheBoundedThousandRoundMode()
    {
        var scenario = SoakScenarioOptions.From(new Dictionary<string, string>
        {
            ["--rounds"] = "1000", ["--matches"] = "4", ["--workers"] = "2",
            ["--lanes"] = "2", ["--round-seconds"] = "1", ["--outages"] = "false",
            ["--reconnects"] = "false", ["--rematches"] = "false"
        });

        Assert.Equal(1000, scenario.Rounds);
        Assert.Equal(1000, scenario.TargetRounds);
        var invalidRounds = new Dictionary<string, string>
        {
            ["--rounds"] = "1001", ["--matches"] = "1", ["--workers"] = "1", ["--lanes"] = "1"
        };
        Assert.Throws<ArgumentException>(() => SoakScenarioOptions.From(invalidRounds));
    }

    [Fact]
    public void RoundsModeDoesNotDrainAtPlacementBoundary()
    {
        var rounds = SoakScenarioOptions.From(new Dictionary<string, string>
        {
            ["--rounds"] = "1", ["--seconds"] = "1", ["--matches"] = "1", ["--workers"] = "1",
            ["--lanes"] = "1", ["--outages"] = "false", ["--reconnects"] = "false",
            ["--rematches"] = "false"
        });
        var wallClock = SoakScenarioOptions.From(new Dictionary<string, string>
        {
            ["--seconds"] = "1", ["--matches"] = "1", ["--workers"] = "1", ["--lanes"] = "1",
            ["--outages"] = "false", ["--reconnects"] = "false", ["--rematches"] = "false"
        });

        Assert.False(rounds.IsWallClockDrainDue(10));
        Assert.True(wallClock.IsWallClockDrainDue(1));
    }

    [Fact]
    public void ControlClientOnlyModesFailClosedAtTheInProcessBoundary()
    {
        var options = new Dictionary<string, string>
        {
            ["--rounds"] = "1", ["--matches"] = "1", ["--workers"] = "1",
            ["--lanes"] = "1", ["--random-control-reconnect"] = "true"
        };

        ArgumentException error = Assert.Throws<ArgumentException>(() => SoakScenarioOptions.From(options));
        Assert.Contains("real WSS control client", error.Message);
    }

    [Fact]
    public void SupportedSoakStressModesAreParsedAndBounded()
    {
        var scenario = SoakScenarioOptions.From(new Dictionary<string, string>
        {
            ["--rounds"] = "1000", ["--matches"] = "1", ["--workers"] = "1", ["--lanes"] = "1",
            ["--lobby-churn"] = "true", ["--chat-during-start"] = "true",
            ["--artifact-delay-ms"] = "25", ["--artifact-failure-rate"] = ".25",
            ["--transition-rate"] = ".5", ["--map-change-rate"] = ".75", ["--seed"] = "42"
        });

        Assert.True(scenario.LobbyChurnEnabled);
        Assert.True(scenario.ChatDuringStartEnabled);
        Assert.Equal(25, scenario.ArtifactDelayMilliseconds);
        Assert.Equal(.25, scenario.ArtifactFailureRate);
        Assert.Equal(.5, scenario.TransitionRate);
        Assert.Equal(.75, scenario.MapChangeRate);
        Assert.Equal(42, scenario.Seed);
    }

    [Fact]
    public void DriverSelectsOnlyTheAuthoritativeRequestedContinuation()
    {
        var options = new[]
        {
            new LobbyVoteEntry(1, LobbyVoteChoice.Rematch, "other", MatchMode.Battle, 0),
            new LobbyVoteEntry(2, LobbyVoteChoice.NextMap, "target", MatchMode.Nodes, 0),
            new LobbyVoteEntry(3, LobbyVoteChoice.ReturnToLobby, "target", MatchMode.Nodes, 0)
        };

        Assert.Equal((byte)2, SoakLobbyDriver.SelectContinuation(options, "target", MatchMode.Nodes).Id);
        Assert.Throws<InvalidOperationException>(() =>
            SoakLobbyDriver.SelectContinuation(options, "missing", MatchMode.Nodes));
    }

    [Fact]
    public void DriverSelectsSeededActiveTransitionCommand()
    {
        var mapChange = new Random(42);
        var restart = new Random(42);

        Assert.Equal(MatchTransitionChoice.ChangeMap,
            SoakLobbyDriver.SelectActiveTransition(mapChange, mapChangeRate: 1));
        Assert.Equal(MatchTransitionChoice.Restart,
            SoakLobbyDriver.SelectActiveTransition(restart, mapChangeRate: 0));
    }

    [Fact]
    public void SelectedActiveTransitionExecutesThroughTheNodeCommandBoundary()
    {
        var manager = new LobbyManager();
        var content = new ContentIdentity("soak-map", "hash", "1", "test", 8);
        manager.ContentCatalog = new NodeContentCatalog([content]);
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "SoakOwner");
        var lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyCreate("Soak", LobbyVisibility.Public, PlayerLimit: 1));
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, content.MapKey, MatchMode.Battle));
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbySetReady(true, lobby.Revision));
        MatchSpec match = manager.PrepareMatch(owner.SessionId, lobby.Revision, content,
            new(Guid.NewGuid()), Guid.NewGuid());
        Assert.True(manager.MatchReady(new(match.MatchId, new(1), new(Guid.NewGuid()),
            Guid.NewGuid(), "127.0.0.1", 1)));

        MatchTransitionChoice choice = SoakLobbyDriver.SelectActiveTransition(new(42), 0);
        var state = Assert.IsType<NodeMatchTransitionVoteSnapshot>(manager.Execute(owner,
            new LobbyMatchTransitionPropose(manager.ForSession(owner.SessionId)!.Revision,
                match.MatchId.Value, choice)));
        Assert.Equal(MatchTransitionChoice.Restart, state.Choice);
        Assert.Equal(MatchTransitionVoteState.Approved, state.State);
        Assert.True(manager.TryGetApprovedMatchTransition(match.MatchId.Value, out _));
    }

    [Fact]
    public void RecoveryRequirementsFailClosedWhenItsInjectionIsDisabled()
    {
        var scenario = SoakScenarioOptions.From(new Dictionary<string, string>
        {
            ["--seconds"] = "30", ["--matches"] = "1", ["--workers"] = "1", ["--lanes"] = "1",
            ["--outages"] = "false", ["--reconnects"] = "false", ["--rematches"] = "false"
        });
        Assert.Throws<ArgumentException>(() => new SoakRequirements(false, true, false, true, false, false).Validate(scenario));
        Assert.Throws<ArgumentException>(() => new SoakRequirements(false, true, false, false, false, true).Validate(scenario));
    }

    [Fact]
    public void OccupancyUsesReportedPeerStateAndLabelsTurnoverAndDisruption()
    {
        var actors = new[]
        {
            new SoakClientSnapshot(10, 2, 0, 20, 8, 1, 0, 2, 1, 2, 2, 100, true, true, 1),
            new SoakClientSnapshot(10, 2, 0, 20, 8, 1, 0, 1, 2, 2, 2, 100, true, true, 1)
        };
        var normal = SoakOccupancy.Sample(actors, activeMatches: 2, targetMatches: 2, targetClientPeers: 4,
            turnover: false, disruption: false);
        Assert.Equal(1, normal.Logical);
        Assert.Equal(.75, normal.PlayingPeers);
        Assert.Equal(.75, normal.ConnectedPeers);
        Assert.Equal(SoakOccupancyPhase.Normal, normal.Phase);
        Assert.Equal(SoakOccupancyPhase.Turnover,
            SoakOccupancy.Sample(actors, 2, 2, 4, turnover: true, disruption: false).Phase);
        Assert.Equal(SoakOccupancyPhase.Disruption,
            SoakOccupancy.Sample(actors, 2, 2, 4, turnover: true, disruption: true).Phase);
    }

    [Fact]
    public void StaleLeaseRollbackCannotRemoveReusedActiveIdentities()
    {
        Guid playerId = Guid.NewGuid();
        Guid observerId = Guid.NewGuid();
        var first = new SoakIdentityLease([playerId], [observerId], [(playerId, observerId)]);
        var second = new SoakIdentityLease([playerId], [observerId], [(playerId, observerId)]);
        var active = new SoakActiveIdentityRegistry();

        Assert.NotSame(first, second); // Equal participant values still belong to separate leases.
        Assert.True(active.TryClaim(first, [playerId, observerId]));
        Assert.True(active.TryRelease(first, [playerId, observerId]));
        Assert.True(active.TryClaim(second, [playerId, observerId]));

        Assert.False(active.TryRelease(first, [playerId, observerId]));
        Assert.Equal(2, active.Count);
        Assert.True(active.TryRelease(second, [playerId, observerId]));
        Assert.Equal(0, active.Count);
    }

    [Fact]
    public void ReconnectRecoveryCountsEveryPeerOnlyAfterFreshProgress()
    {
        var reconnects = new SoakReconnectTracker();
        reconnects.Begin(1);
        reconnects.Begin(2);

        reconnects.Observe(1, connected: true, hasFreshProgress: false);
        reconnects.Observe(2, connected: false, hasFreshProgress: true);
        Assert.Equal(2, reconnects.Attempts);
        Assert.Equal(0, reconnects.Recovered);

        reconnects.Observe(1, connected: true, hasFreshProgress: true);
        reconnects.Observe(1, connected: true, hasFreshProgress: true);
        reconnects.Observe(2, connected: true, hasFreshProgress: true);
        Assert.Equal(2, reconnects.Recovered);

        reconnects.Begin(1);
        reconnects.Begin(2);
        reconnects.Observe(1, connected: true, hasFreshProgress: true);
        Assert.Equal(3, reconnects.Recovered);
        Assert.Equal(4, reconnects.Attempts);
    }

    [Fact]
    public void ObserverlessRosterOnlyRequiresPlayerTraffic()
    {
        var scenario = SoakScenarioOptions.From(new Dictionary<string, string>
        {
            ["--seconds"] = "30", ["--matches"] = "1", ["--workers"] = "1", ["--lanes"] = "1",
            ["--outages"] = "false", ["--reconnects"] = "false", ["--rematches"] = "false",
            ["--players"] = "4", ["--bots"] = "0", ["--observers"] = "0"
        });

        Assert.True(scenario.HasRequiredTraffic(inputs: 1, observerSnapshots: 0, playingSnapshots: 1));
        Assert.False(scenario.HasRequiredTraffic(inputs: 0, observerSnapshots: 0, playingSnapshots: 1));
        Assert.False(scenario.HasRequiredTraffic(inputs: 1, observerSnapshots: 0, playingSnapshots: 0));
    }

    [Fact]
    public void CrashVictimIsExcludedFromReconnectInjection()
    {
        Assert.False(SoakRecoveryPolicy.ShouldReconnect(crashPlanned: true, sameWorkerAsCrashVictim: true));
        Assert.True(SoakRecoveryPolicy.ShouldReconnect(crashPlanned: true, sameWorkerAsCrashVictim: false));
        Assert.True(SoakRecoveryPolicy.ShouldReconnect(crashPlanned: false, sameWorkerAsCrashVictim: true));
        Assert.False(SoakRecoveryPolicy.ShouldInterruptOnWorkerExit(terminalCompleted: true));
        Assert.True(SoakRecoveryPolicy.ShouldInterruptOnWorkerExit(terminalCompleted: false));
    }

    [Fact]
    public void ReconnectBatchRequiresPlayingFreshProgressAndAuthoritativeClockHeadroom()
    {
        var ready = new SoakReconnectPeerEvidence(1, "Player", 0, "Playing", 20, true, true, true, 1200);
        var second = ready with { SeatId = 2, Role = "Observer" };
        Assert.True(SoakRecoveryPolicy.IsReconnectBatchReady([ready, second]));
        Assert.False(SoakRecoveryPolicy.IsReconnectBatchReady([ready with { RemainingSeconds = 9 }, second]));
        Assert.False(SoakRecoveryPolicy.IsReconnectBatchReady([ready with { Phase = "Ready", Playing = false }, second]));
        Assert.False(SoakRecoveryPolicy.IsReconnectPeerReady(true, true, false, 20));
        Assert.False(SoakRecoveryPolicy.IsReconnectPeerReady(true, true, true, null));
    }

    [Fact]
    public void PendingReconnectGenerationCannotBeOverwrittenAndFinalEvidenceIsRetained()
    {
        var tracker = new SoakReconnectTracker();
        var injection = new SoakReconnectPeerEvidence(3, "Player", 0, "Playing", 18, true, true, true, 900);
        int generation = tracker.Begin(3, injection);
        Assert.Equal(generation, tracker.Pending.Single().Generation);
        Assert.Throws<InvalidOperationException>(() => tracker.Begin(3, injection));
        var pending = Assert.Throws<InvalidDataException>(() => tracker.EnsureNoPending("match-a"));
        Assert.Contains("seat=3/generation=1/phase=Playing/remaining=18.000/finalEvidence=none", pending.Message);

        tracker.Observe(3, connected: true, hasFreshProgress: true,
            injection with { Phase = "Playing", RemainingSeconds = 16, ServerTick = 930 });

        Assert.Equal(1, tracker.Attempts);
        Assert.Equal(1, tracker.Recovered);
        Assert.Empty(tracker.Pending);
        var evidence = Assert.Single(tracker.RecoveryEvidence);
        Assert.Equal(generation, evidence.Injection.Generation);
        Assert.Equal(generation, evidence.Final.Generation);
        Assert.Equal((uint)930, evidence.Final.ServerTick);
        tracker.EnsureNoPending("match-a");
    }
}
