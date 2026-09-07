using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.UI.Adapters;
using MphRead.Mods.UI.State;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class G6PrivateLobbyIntegrationTests
{
    [Fact]
    public void PrivateDraftMapsEverySupportedRuleGroupIntoValidatedRules()
    {
        var draft = new PrivateMatchDraft
        {
            Name = "Competitive room", MapId = "Sanctorus", Mode = "TeamBattle",
            MaxPlayers = 6, Bots = 2, BotSkill = 2, MaxSpectators = 8,
            TimeLimitSeconds = 600, ScoreGoal = 25, ObjectiveTimeSeconds = 90,
            StartingLives = 3, FriendlyFire = true, AffinityWeapons = true,
            RadarPolicy = "Enabled", SpawnPolicy = "Enhanced", DamageLevel = 2,
            OvertimePolicy = "ModeDefault", LateJoinPolicy = "SpectateUntilNextMatch",
            Preset = "Competitive", ReadyRequired = true, PublicListing = true
        };

        Assert.Empty(draft.Validate());
        Assert.True(LauncherPrivateMatchController.TryBuildRules(draft, out MatchRules? rules,
            out string error), error);
        Assert.NotNull(rules);
        Assert.Equal(MatchMode.TeamBattle, rules.Mode);
        Assert.Equal(TimeSpan.FromMinutes(10), rules.TimeLimit);
        Assert.Equal(25, rules.ScoreGoal);
        Assert.Equal(TimeSpan.FromSeconds(90), rules.ObjectiveTimeGoal);
        Assert.Equal(3, rules.StartingLives);
        Assert.True(rules.FriendlyFire);
        Assert.True(rules.AffinityWeapons);
        Assert.Equal(RadarPolicy.Enabled, rules.RadarPolicy);
        Assert.Equal(SpawnPolicy.Enhanced, rules.SpawnPolicy);
        Assert.Equal(OvertimePolicy.ModeDefault, rules.OvertimePolicy);
        Assert.Equal(LateJoinPolicy.SpectateUntilNextMatch, rules.LateJoinPolicy);
        Assert.Equal(RulesetPreset.Competitive, rules.RulesetPreset);
        Assert.Equal(RankingEligibility.Unranked, rules.RankingEligibility);
    }

    [Fact]
    public void DuelPresetRejectsInvalidModeAndCapacityBeforeHosting()
    {
        var draft = new PrivateMatchDraft
        {
            MapId = "Sanctorus", Mode = "TeamBattle", Preset = "Duel", MaxPlayers = 8
        };

        Assert.Contains(draft.Validate(), error => error.Contains("Duel requires", StringComparison.Ordinal));
    }

    [Fact]
    public void LobbyUsesExplicitSoloAndTeamLabelsAndSeparatesSpectators()
    {
        UiLobbySnapshot teamLobby = Snapshot("TeamBattle");
        UiLobbyMember player = teamLobby.Members[0];
        UiLobbyMember observer = teamLobby.Members[1];

        Assert.True(teamLobby.UsesTeams);
        Assert.Equal("Green", teamLobby.TeamLabel(player));
        Assert.Equal("Spectator", teamLobby.TeamLabel(observer));
        Assert.Equal("Solo", Snapshot("Battle").TeamLabel(player));
    }

    [Fact]
    public async Task LeaveServerIsAClientLifecycleActionAndDoesNotWaitForRevision()
    {
        var controller = new UnchangedLobbyController(Snapshot("Battle"));
        var model = new LobbyScreenModel(controller);

        UiActionResult result = await model.RequestAsync(
            new UiLobbyCommand(UiLobbyAction.LeaveServer));

        Assert.True(result.Succeeded);
        Assert.Equal(UiLobbyAction.LeaveServer, controller.LastAction);
        Assert.Equal(UiLoadState.Ready, model.Status.State);
    }

    private static UiLobbySnapshot Snapshot(string mode)
        => new(7, 3, "Open", "PersistentLobby", "Sanctorus", mode, "Classic",
            ImmutableArray.Create(
                new UiLobbyMember("Player", "Samus", 1, true, false, false, false,
                    false, true, true, true, 20, true),
                new UiLobbyMember("Observer", "Trace", 0, false, false, true, false,
                    false, false, false, false, 40)),
            ImmutableArray<UiLobbyChatLine>.Empty,
            new Dictionary<UiLobbyAction, string>());

    private sealed class UnchangedLobbyController(UiLobbySnapshot snapshot)
        : ILobbyScreenController
    {
        public UiLobbySnapshot? Snapshot { get; } = snapshot;
        public UiLobbyAction? LastAction { get; private set; }
        public event Action? Changed { add { } remove { } }
        public Task<UiActionResult> RequestAsync(UiLobbyCommand command, uint expectedRevision,
            CancellationToken cancellationToken)
        {
            LastAction = command.Action;
            return Task.FromResult(UiActionResult.Success());
        }
    }
}
