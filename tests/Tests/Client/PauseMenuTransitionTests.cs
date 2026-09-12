using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Mods.Launcher.Gui;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class PauseMenuTransitionTests
{
    [AvaloniaFact]
    public void ConfirmationCancelErrorsAndPlatformViewsShareTheActionBoundary()
    {
        var actions = new FakeActions
        {
            AvailableTransitionMaps = ["next-map"],
            CurrentMapKey = "current-map"
        };
        var desktop = new PauseMenuView(offerWindowMode: true,
            transitionActions: actions);
        var android = new PauseMenuView(offerWindowMode: false,
            transitionActions: actions);
        // PauseMenuView is intentionally presentation-only; its host owns the
        // Changed subscription. Mirror that host boundary here and drain the
        // UI dispatcher so this test remains deterministic under headless
        // Avalonia.
        actions.Changed += (_, _) =>
            Dispatcher.UIThread.Post(desktop.RefreshTransitionPresentation);
        var window = new Window { Width = 940, Height = 720, Content = desktop };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Same(actions, desktop.TransitionActions);
            Assert.Same(actions, android.TransitionActions);
            Assert.True(desktop.TransitionMenuVisible);
            Assert.True(android.TransitionMenuVisible);

            MenuEntry restart = Assert.Single(desktop.GetVisualDescendants()
                .OfType<MenuEntry>(), entry => entry.Title == "Restart match");
            MenuEntry confirm = Assert.Single(desktop.GetVisualDescendants()
                .OfType<MenuEntry>(), entry => entry.Title == "Confirm");
            MenuEntry voteYes = Assert.Single(desktop.GetVisualDescendants()
                .OfType<MenuEntry>(), entry => entry.Title == "Vote yes");

            int proposals = 0;
            int mapChanges = 0;
            bool? vote = null;
            desktop.RestartMatchRequested += (_, _) => proposals++;
            desktop.ChangeMapRequested += (_, _) => mapChanges++;
            desktop.TransitionVoteRequested += accept => vote = accept;
            desktop.BeginRestartConfirmation();
            Assert.True(desktop.TransitionConfirmationVisible);
            Assert.True(confirm.IsFocused);
            desktop.CancelPendingTransition();
            Assert.False(desktop.TransitionConfirmationVisible);
            Assert.True(restart.IsFocused);
            Assert.Equal(0, proposals);

            desktop.BeginRestartConfirmation();
            desktop.ConfirmPendingTransition();
            Assert.Equal(1, proposals);

            desktop.BeginChangeMapConfirmation();
            Assert.True(desktop.TransitionConfirmationVisible);
            desktop.ConfirmPendingTransition();
            Assert.Equal(1, mapChanges);

            actions.TransitionRequestInFlight = true;
            actions.RaiseChanged();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Sending transition request", String.Join("\n", Text(desktop)));
            actions.TransitionRequestInFlight = false;
            actions.TransitionError = "The Node rejected the request.";
            actions.RaiseChanged();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("The Node rejected the request.", Text(desktop));

            actions.TransitionError = null;
            actions.TransitionVote = new NodeMatchTransitionVoteSnapshot(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(),
                "Host", MatchTransitionChoice.Restart, "current-map", MatchMode.Battle,
                3, 1, 0, 2, DateTimeOffset.UtcNow.AddSeconds(30));
            actions.RaiseChanged();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("A lobby vote is active.", Text(desktop));
            Assert.True(voteYes.IsVisible);
            ((IControllerNavigable)voteYes).ControllerActivate();
            Assert.True(vote == true);

            actions.TransitionVote = new NodeMatchTransitionVoteSnapshot(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid(),
                "Host", MatchTransitionChoice.Restart, "current-map", MatchMode.Battle,
                3, 3, 0, 2, DateTimeOffset.UtcNow.AddSeconds(30),
                State: MatchTransitionVoteState.Rejected);
            actions.RaiseChanged();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Transition vote rejected.", Text(desktop));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static string[] Text(Control root)
        => root.GetVisualDescendants().OfType<TextBlock>()
            .Select(block => block.Text)
            .Where(text => !String.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToArray();

    private sealed class FakeActions : IMatchTransitionMenuActions
    {
        public bool TransitionMenuSupported { get; set; } = true;
        public bool TransitionRequestInFlight { get; set; }
        public string? TransitionError { get; set; }
        public NodeMatchTransitionVoteSnapshot? TransitionVote { get; set; }
        public string? CurrentMapKey { get; set; }
        public IReadOnlyList<string> AvailableTransitionMaps { get; set; }
            = Array.Empty<string>();
        public event EventHandler? Changed;

        public Task RequestRestartMatchAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task RequestChangeMapAsync(string mapKey,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task RequestTransitionVoteAsync(bool accept,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
