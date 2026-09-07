using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using MphRead.Mods.UI.AppShell;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.Screens;
using MphRead.Mods.UI.Screens.Lobby;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;
using Xunit;

namespace MphRead.Tests.Client;

/// <summary>
/// Deterministic G6 layout and controller acceptance checks. These are logical
/// viewport tests; device and high-refresh acceptance remains an external gate.
/// </summary>
public sealed class UiAcceptanceTests
{
    private static readonly UiRoute[] AcceptanceRoutes =
    [
        UiRoute.Home,
        UiRoute.Play,
        UiRoute.Lobby,
        UiRoute.ServerBrowser,
        UiRoute.HunterLicense,
        UiRoute.PostMatch,
        UiRoute.Replays,
        UiRoute.Settings
    ];

    public static IEnumerable<object[]> AcceptanceViewports()
    {
        yield return [1280, 720, UiLayoutMode.Wide];
        yield return [1920, 1080, UiLayoutMode.Wide];
        yield return [2560, 1440, UiLayoutMode.Wide];
        yield return [3440, 1440, UiLayoutMode.Wide];
        yield return [360, 640, UiLayoutMode.Compact];
        yield return [768, 1024, UiLayoutMode.Medium];
    }

    [Theory]
    [MemberData(nameof(AcceptanceViewports))]
    public void AcceptanceViewportsUseStableResponsiveModes(int width, int height,
        UiLayoutMode expected)
    {
        using var state = new AppShellState();

        state.SetViewport(width);

        Assert.Equal(expected, state.LayoutMode);
        Assert.Equal(expected, UiBreakpoints.FromWidth(width));
        Assert.True(height > 0);
    }

    [Theory]
    [MemberData(nameof(AcceptanceViewports))]
    public void EveryRequiredScreenHasAnAccessibleInitialFocusAtEachViewport(int width,
        int height, UiLayoutMode expected)
    {
        using var state = new AppShellState();
        var factory = new UiScreenFactory(state.Router, new UiScreenServices
        {
            Lobby = new FixtureLobbyController(LobbySnapshotFor("Battle", teamMode: false)),
            PostMatch = new FixturePostMatchController(PostMatchSummaryFor())
        });
        state.SetViewport(width);

        foreach (UiRoute route in AcceptanceRoutes)
        {
            Control screen = factory.GetScreen(route);
            screen.Measure(new Size(width, height));
            screen.Arrange(new Rect(0, 0, width, height));

            var focus = Assert.IsAssignableFrom<IUiFocusSource>(screen);
            Assert.False(string.IsNullOrWhiteSpace(focus.InitialFocusKey),
                $"{route} did not publish an initial focus key.");
            Assert.Contains(focus.InitialFocusKey, focus.FocusTargets.Keys);
            Assert.NotEmpty(focus.FocusTargets);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(screen)),
                $"{route} did not publish an automation name.");

            foreach ((string key, Control control) in focus.FocusTargets)
            {
                Assert.True(control.Focusable,
                    $"{route} focus target '{key}' is not focusable.");
            }
        }

        Assert.Equal((double)width, state.ViewportWidth);
        Assert.Equal(expected, UiBreakpoints.FromWidth(state.ViewportWidth));
    }

    [Fact]
    public void LobbyCaptureFixturesCoverFfaTeamsAndFullObserverRoster()
    {
        UiLobbySnapshot ffa = LobbySnapshotFor("Battle", teamMode: false);
        UiLobbySnapshot teams = LobbySnapshotFor("Teams", teamMode: true);

        Assert.Equal(8, ffa.Members.Count(member => !member.Observer));
        Assert.Equal(16, ffa.Members.Count(member => member.Observer));
        Assert.All(ffa.Members.Where(member => !member.Observer), member =>
            Assert.Equal(0, member.Team));
        Assert.Equal(8, teams.Members.Count(member => !member.Observer));
        Assert.Equal(16, teams.Members.Count(member => member.Observer));
        Assert.Contains(teams.Members.Where(member => !member.Observer), member => member.Team == 0);
        Assert.Contains(teams.Members.Where(member => !member.Observer), member => member.Team == 1);

        foreach (UiLobbySnapshot snapshot in new[] { ffa, teams })
        {
            var view = new LobbyScreenView(new FixtureLobbyController(snapshot));
            view.Measure(new Size(1920, 1080));
            view.Arrange(new Rect(0, 0, 1920, 1080));
            Assert.Contains("lobby:hunter", view.FocusTargets.Keys);
            Assert.Contains("lobby:ready", view.FocusTargets.Keys);
            Assert.Equal(1, LobbyScreenModel.SectionColumns(UiLayoutMode.Compact));
            Assert.Equal(3, LobbyScreenModel.SectionColumns(UiLayoutMode.Wide));
        }
    }

    [Fact]
    public void ControllerPathUsesStableProductionFocusKeysWithoutMouse()
    {
        var policy = new UiFocusNavigationPolicy();
        string[] path =
        [
            "home:play",
            "play:private",
            "private:create",
            "lobby:hunter",
            "lobby:ready"
        ];
        policy.ConnectVertical(path);

        for (int index = 0; index < path.Length - 1; index++)
        {
            Assert.True(policy.TryMove(path[index], UiFocusDirection.Down, out string next));
            Assert.Equal(path[index + 1], next);
        }

        Assert.True(policy.TryMove("lobby:ready", UiFocusDirection.Up, out string previous));
        Assert.Equal("lobby:hunter", previous);
        Assert.False(policy.TryMove("lobby:ready", UiFocusDirection.Down, out _));
    }

    private static UiLobbySnapshot LobbySnapshotFor(string mode, bool teamMode)
    {
        var members = ImmutableArray.CreateBuilder<UiLobbyMember>(24);
        for (int index = 0; index < 8; index++)
        {
            members.Add(new UiLobbyMember($"Hunter {index + 1}", index % 2 == 0 ? "Samus" : "Noxus",
                teamMode ? index % 2 : 0, Ready: index < 4, Loading: false, Observer: false,
                DisconnectedGrace: false, Bot: index >= 6, Host: index == 0, Admin: index == 0,
                RatingEligible: index < 6, PingMs: 20 + index, Local: index == 0));
        }
        for (int index = 0; index < 16; index++)
        {
            members.Add(new UiLobbyMember($"Observer {index + 1}", "Trace", 0, Ready: false,
                Loading: false, Observer: true, DisconnectedGrace: index == 0, Bot: false,
                Host: false, Admin: false, RatingEligible: false, PingMs: 40 + index));
        }
        return new UiLobbySnapshot(42, 7, "Open", "Persistent", "Sanctorus", mode,
            "Classic · 8 players · 07:00 · FF Off · Radar On · Spawn Default · Late join Off",
            members.MoveToImmutable(),
            ImmutableArray.Create(new UiLobbyChatLine("Host", "Welcome Hunters.", false, true, true)),
            new Dictionary<UiLobbyAction, string>());
    }

    private static UiPostMatchSummary PostMatchSummaryFor()
    {
        var rows = ImmutableArray.CreateBuilder<UiPostMatchRow>(8);
        for (int index = 0; index < 8; index++)
        {
            rows.Add(new UiPostMatchRow(index + 1, $"Hunter {index + 1}",
                index % 2 == 0 ? "Samus" : "Noxus", index % 2, Bot: index >= 6,
                Points: 7 - index, Kills: 12 - index, Deaths: 3 + index, Assists: index,
                Damage: 1200 - index * 75, Headshots: index + 1,
                ObjectivePrimary: index, ObjectiveSecondary: 0, ObjectiveTertiary: 0));
        }
        return new UiPostMatchSummary(42, 8, 9, TimeSpan.FromMinutes(7), "Sanctorus",
            "TeamBattle", "ScoreLimit", rows.MoveToImmutable(), RatingUpdateState.Updated,
            RatingDelta: 18, RatingPoints: 1518);
    }

    private sealed class FixtureLobbyController(UiLobbySnapshot snapshot) : ILobbyScreenController
    {
        public UiLobbySnapshot? Snapshot { get; } = snapshot;
        public event Action? Changed { add { } remove { } }

        public Task<UiActionResult> RequestAsync(UiLobbyCommand command, uint expectedRevision,
            CancellationToken cancellationToken)
            => Task.FromResult(UiActionResult.Success());
    }

    private sealed class FixturePostMatchController(UiPostMatchSummary summary)
        : IPostMatchScreenController
    {
        public UiPostMatchSummary? Summary { get; } = summary;
        public event Action? Changed { add { } remove { } }

        public Task<UiPostMatchSummary> AwaitRatingAsync(uint matchId,
            CancellationToken cancellationToken) => Task.FromResult(Summary!);

        public Task<UiActionResult> InvokeAsync(PostMatchAction action,
            CancellationToken cancellationToken) => Task.FromResult(UiActionResult.Success());
    }

}
