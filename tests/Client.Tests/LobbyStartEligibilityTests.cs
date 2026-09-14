using System;
using System.Collections.Immutable;
using MphRead;
using MphRead.Mods.Launcher.Gui;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Tests;

public sealed class LobbyStartEligibilityTests
{
    [Fact]
    public void HumanOnTeamOneAndBotFillTheLeastOccupiedTeam()
    {
        LobbySnapshot snapshot = Snapshot(
            MatchMode.TeamBattle,
            botCount: 1,
            members: [Member(team: 1)]);

        LobbyStartEligibility result = LobbyStartEligibility.Evaluate(snapshot);

        Assert.True(result.CanStart);
        Assert.Equal(2, LobbyStartEligibility.RepresentedTeams(snapshot));
    }

    [Fact]
    public void FourTeamMixedRosterAssignsBotsToLowestIndexLeastOccupiedTeams()
    {
        LobbySnapshot snapshot = Snapshot(
            MatchMode.TeamBattle,
            botCount: 2,
            teamCount: 4,
            members:
            [
                Member(team: 0),
                Member(team: 0),
                Member(team: 2)
            ]);

        LobbyStartEligibility result = LobbyStartEligibility.Evaluate(snapshot);

        Assert.True(result.CanStart);
        Assert.Equal(4, LobbyStartEligibility.RepresentedTeams(snapshot));
    }

    [Fact]
    public void MissingConfiguredTeamIsRejected()
    {
        LobbySnapshot snapshot = Snapshot(
            MatchMode.TeamBattle,
            teamCount: 4,
            members:
            [
                Member(team: 0),
                Member(team: 1),
                Member(team: 2)
            ]);

        LobbyStartEligibility result = LobbyStartEligibility.Evaluate(snapshot);

        Assert.False(result.CanStart);
        Assert.Contains("every configured team", result.Message, StringComparison.Ordinal);
    }

    private static LobbySnapshot Snapshot(
        MatchMode mode,
        int botCount = 0,
        int? teamCount = null,
        params LobbyMember[] members)
    {
        Guid owner = members.Length == 0 ? Guid.NewGuid() : members[0].SessionId;
        LobbyRulesOptions? rules = teamCount is { } count
            ? new LobbyRulesOptions(TeamCount: count)
            : null;
        return new LobbySnapshot(
            Guid.NewGuid(), "Room", LobbyVisibility.Public, owner,
            LobbyPhase.Open, 1, 8, 16, members.ToImmutableArray(), [],
            "MP1 SANCTORUS", mode, BotCount: botCount, Rules: rules);
    }

    private static LobbyMember Member(byte team)
    {
        Guid session = Guid.NewGuid();
        return new(session, Guid.NewGuid(), $"Hunter{team}", Hunter.Samus,
            team, Ready: true, Observer: false);
    }
}
