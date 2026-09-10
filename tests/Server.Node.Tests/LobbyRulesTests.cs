using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Shared;
using MphRead;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class LobbyRulesTests
{
    private static LobbyIdentity Person(string name) => new(Guid.NewGuid(), Guid.NewGuid(), name);

    [Fact]
    public void StructuredRulesAreNormalizedBeforeMutationAndFreezeIntoMatchSpec()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner");
        var lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Rules", LobbyVisibility.Public));
        var rules = new LobbyRulesOptions(TimeLimitSeconds: 600, ScoreGoal: 11, DamageLevel: 2,
            FriendlyFire: true, AffinityWeapons: true, PlayerRadar: true,
            KillcamPolicy: KillcamPolicy.PostRound);

        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle, Rules: rules));

        Assert.Equal(rules, lobby.Rules);
        Assert.Equal(600, lobby.TimeLimitSeconds);
        Assert.Equal(11, lobby.PointGoal);
        lobby = (LobbySnapshot)manager.Execute(owner, new LobbySetReady(true, lobby.Revision));
        MatchSpec spec = manager.PrepareMatch(owner.SessionId, lobby.Revision,
            new("unit", "hash", "1", "test", 8), new(Guid.NewGuid()), Guid.NewGuid());

        Assert.Equal(TimeSpan.FromMinutes(10), spec.Rules.TimeLimit);
        Assert.Equal(11, spec.Rules.ScoreGoal);
        Assert.Equal(2, spec.Rules.DamageLevel);
        Assert.True(spec.Rules.FriendlyFire);
        Assert.True(spec.Rules.AffinityWeapons);
        Assert.True(spec.Rules.PlayerRadar);
        Assert.Equal(KillcamPolicy.PostRound, spec.Rules.KillcamPolicy);
    }

    [Fact]
    public void LegacyAndStructuredDisagreementFailsWithoutLobbyMutation()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Rules", LobbyVisibility.Public));
        var conflicting = new LobbyRulesOptions(TimeLimitSeconds: 601, ScoreGoal: 11);

        var error = Assert.Throws<LobbyCommandException>(() => manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle, TimeLimitSeconds: 600,
                PointGoal: 11, Rules: conflicting)));

        Assert.Equal("invalid", error.Code);
        LobbySnapshot unchanged = manager.ForSession(owner.SessionId)!;
        Assert.Equal(lobby.Revision, unchanged.Revision);
        Assert.Equal("", unchanged.MapKey);
        Assert.Equal(LobbyRulesOptions.Empty, unchanged.Rules);
    }

    [Theory]
    [InlineData(MatchMode.Survival, 4, null, 4, null)]
    [InlineData(MatchMode.Defender, null, 120, null, 120)]
    public void ModeSpecificGoalsMapToTheAuthoritativeMatchRules(MatchMode mode,
        int? startingLives, int? objectiveSeconds, int? expectedLives, int? expectedObjectiveSeconds)
    {
        var manager = new LobbyManager();
        var owner = Person("Owner");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Rules", LobbyVisibility.Public));
        var rules = new LobbyRulesOptions(StartingLives: startingLives,
            ObjectiveTimeGoalSeconds: objectiveSeconds);
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", mode, Rules: rules));
        lobby = (LobbySnapshot)manager.Execute(owner, new LobbySetReady(true, lobby.Revision));

        MatchSpec spec = manager.PrepareMatch(owner.SessionId, lobby.Revision,
            new("unit", "hash", "1", "test", 8), new(Guid.NewGuid()), Guid.NewGuid());

        Assert.Equal(expectedLives, spec.Rules.IsSurvival ? spec.Rules.StartingLives : null);
        Assert.Equal(expectedObjectiveSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            spec.Rules.ObjectiveTimeGoal);
        Assert.Equal(mode is MatchMode.Survival ? expectedLives : null, lobby.PointGoal);
        Assert.Equal(mode is MatchMode.Defender ? expectedObjectiveSeconds : null, lobby.Rules?.ObjectiveTimeGoalSeconds);
    }

    [Fact]
    public void CanonicalRulesRemainEquivalentAcrossRematchContinuation()
    {
        var manager = new LobbyManager
        {
            ContentCatalog = new NodeContentCatalog([
                new ContentIdentity("unit", "hash", "1", "test", 8),
                new ContentIdentity("unit2", "hash", "1", "test", 8)])
        };
        var owner = Person("Owner");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Rules", LobbyVisibility.Public));
        var rules = new LobbyRulesOptions(TimeLimitSeconds: 420, ScoreGoal: 13,
            DamageLevel: 0, FriendlyFire: true, AffinityWeapons: true, PlayerRadar: true);
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle, Rules: rules));
        lobby = (LobbySnapshot)manager.Execute(owner, new LobbySetReady(true, lobby.Revision));
        MatchSpec first = manager.PrepareMatch(owner.SessionId, lobby.Revision,
            manager.ContentCatalog.Get("unit"), new(Guid.NewGuid()), Guid.NewGuid());
        manager.MatchEnded(first.MatchId, false);

        NodeRoundSnapshot round = manager.RoundForSession(owner.SessionId)!;
        manager.Execute(owner, new LobbyVoteCast(round.Lobby.Revision, round.BallotRevision, 1));
        var continuation = Assert.Single(manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid()));

        Assert.Equal(first.Rules, continuation.Spec.Rules);
        Assert.Equal(first.Content.MapKey, continuation.Spec.Content.MapKey);
    }

    [Fact]
    public void TournamentModeChangeProjectsPriorModeSpecificRules()
    {
        var manager = new LobbyManager
        {
            ContentCatalog = new NodeContentCatalog([
                new ContentIdentity("unit", "hash", "1", "test", 8),
                new ContentIdentity("unit2", "hash", "1", "test", 8)])
        };
        var owner = Person("Owner");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Rules", LobbyVisibility.Public));
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Survival,
                Rules: new LobbyRulesOptions(TimeLimitSeconds: 600, StartingLives: 4, DamageLevel: 0)));
        Guid tournament = Guid.NewGuid(), round = Guid.NewGuid();
        manager.Execute(owner, new LobbyTournamentIdentity(lobby.Revision, tournament, round));
        lobby = ((NodeRoundSnapshot)manager.Execute(owner,
            new LobbyTournamentControl(lobby.Revision + 1, TournamentControl.Resume))).Lobby;
        lobby = (LobbySnapshot)manager.Execute(owner, new LobbySetReady(true, lobby.Revision));
        MatchSpec first = manager.PrepareMatch(owner.SessionId, lobby.Revision,
            manager.ContentCatalog.Get("unit"), new(Guid.NewGuid()), Guid.NewGuid());
        manager.MatchEnded(first.MatchId, false);
        lobby = manager.RoundForSession(owner.SessionId)!.Lobby;

        manager.Execute(owner, new LobbyTournamentSelectNext(lobby.Revision, Guid.NewGuid(), "unit2", MatchMode.Battle));
        lobby = manager.RoundForSession(owner.SessionId)!.Lobby;
        Assert.Equal(MatchMode.Battle, lobby.Mode);
        Assert.Null(lobby.Rules!.StartingLives);
        Assert.Equal(600, lobby.Rules.TimeLimitSeconds);
        Assert.Equal(0, lobby.Rules.DamageLevel);

        lobby = ((NodeRoundSnapshot)manager.Execute(owner,
            new LobbyTournamentControl(lobby.Revision, TournamentControl.Resume))).Lobby;
        lobby = (LobbySnapshot)manager.Execute(owner, new LobbySetReady(true, lobby.Revision));
        MatchSpec next = manager.PrepareMatch(owner.SessionId, lobby.Revision,
            manager.ContentCatalog.Get("unit2"), new(Guid.NewGuid()), Guid.NewGuid());
        Assert.Equal(TimeSpan.FromMinutes(10), next.Rules.TimeLimit);
        Assert.Equal(7, next.Rules.ScoreGoal);
        Assert.Equal(0, next.Rules.StartingLives);
        Assert.Equal(0, next.Rules.DamageLevel);
    }

    [Fact]
    public void PublicListCarriesBoundedAuthoritativeMatchMetadata()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Rules", LobbyVisibility.Public));
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Survival, Rules:
                new LobbyRulesOptions(TimeLimitSeconds: 600, StartingLives: 4)));

        var list = (LobbyListSnapshot)manager.Execute(owner, new LobbyList());
        LobbyListEntry row = Assert.Single(list.Lobbies);
        Assert.Equal("unit", row.MapKey);
        Assert.Equal(MatchMode.Survival, row.Mode);
        Assert.Equal(600, row.TimeLimitSeconds);
        Assert.Equal(4, row.PointGoal);
        Assert.Null(row.ObjectiveTimeGoalSeconds);
        Assert.Equal(LobbySeatPolicy.ImmediateSeat, row.SeatPolicy);
        Assert.True(NodeControlCodec.Write("lobby.list", 1, null, list).Length < NodeControlCodec.MaximumFrameBytes);
    }

    [Fact]
    public void InapplicableAndOutOfRangeRulesFailClosed()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Rules", LobbyVisibility.Public));

        Assert.Throws<LobbyCommandException>(() => manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle, Rules:
                new LobbyRulesOptions(StartingLives: 2))));
        Assert.Throws<LobbyCommandException>(() => manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle, Rules:
                new LobbyRulesOptions(TimeLimitSeconds: 0))));
        Assert.Throws<LobbyCommandException>(() => manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle, Rules:
                new LobbyRulesOptions(OctolithReset: true))));
        Assert.Equal(lobby.Revision, manager.ForSession(owner.SessionId)!.Revision);
    }
}
