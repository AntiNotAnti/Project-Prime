using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Mods;
using MphRead.Mods.Launcher.Gui;
using Xunit;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class MatchTransitionTests
{
    [Fact]
    public void StageContractContainsOnlyTheApprovedTruthfulMilestones()
        => Assert.Equal([
            "Preparing",
            "Connecting",
            "PreparingContent",
            "LoadingArena",
            "PreparingPlayers",
            "Finalizing",
            "EnteringMatch",
            "LoadingNextRound",
            "ReturningToLobby",
            "Reconnecting",
            "Failed"
        ], Enum.GetNames<MatchTransitionStage>());

    [AvaloniaFact]
    public void ViewShowsReportedFactsAndOnlyChecksOffObservedStages()
    {
        using var view = new MatchTransitionView(new MatchTransitionState(
            MatchTransitionStage.LoadingArena, "Alinos Gateway", "Battle",
            "Samus", "Loading the selected arena bundle."), reducedMotion: true);
        var window = new Window { Width = 940, Height = 720, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(MatchTransitionStage.LoadingArena, view.State.Stage);
            Assert.Equal([MatchTransitionStage.LoadingArena], view.ObservedStages);
            Assert.True(view.StageMotionSpec.IsImmediate);
            Assert.False(view.FailureVisible);
            Assert.Contains("Loading Arena", Copy(view));
            Assert.Contains("Alinos Gateway", Copy(view));
            Assert.Contains("Battle", Copy(view));
            Assert.Contains("Samus", Copy(view));
            Assert.Contains("Loading the selected arena bundle.", Copy(view));
            Assert.DoesNotContain(Copy(view), value => value.Contains('%'));

            view.Update(view.State with
            {
                Stage = MatchTransitionStage.PreparingPlayers,
                Detail = "Waiting for the frozen roster."
            });
            Assert.Equal([
                MatchTransitionStage.LoadingArena,
                MatchTransitionStage.PreparingPlayers
            ], view.ObservedStages);
            Assert.Equal(2, view.GetVisualDescendants().OfType<Border>()
                .Count(border => border.Classes.Contains(
                    "match-transition-stage")));
            Assert.Contains("Waiting for the frozen roster.", Copy(view));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FailureIsImmediateAndOffersReturnToLobby()
    {
        using var view = new MatchTransitionView(new MatchTransitionState(
            MatchTransitionStage.Connecting, Detail: "Contacting the match server."),
            reducedMotion: false);
        var window = new Window { Width = 940, Height = 720, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(view.StageMotionSpec.Duration.TotalMilliseconds, 150, 220);

            bool requested = false;
            view.ReturnToLobbyRequested += (_, _) => requested = true;
            view.Update(view.State with
            {
                Stage = MatchTransitionStage.Failed,
                Detail = "The server rejected the reservation."
            });
            Dispatcher.UIThread.RunJobs();

            Assert.True(view.FailureVisible);
            Assert.Equal(MatchTransitionStage.Failed, view.State.Stage);
            Assert.Contains("The server rejected the reservation.", Copy(view));
            AvaloniaButton button = view.GetVisualDescendants()
                .OfType<AvaloniaButton>().Single(candidate
                    => Equals(candidate.Content, "Return to Lobby"));
            Assert.Same(button, window.FocusManager?.GetFocusedElement());
            button.RaiseEvent(new RoutedEventArgs(AvaloniaButton.ClickEvent));
            Assert.True(requested);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ShellOverlayUpdatesAndClosesWithoutChangingTitleOrRoute()
    {
        PrimeShellView shell = PrimeShellView.CreateCapture(new MenuSettings(),
            Array.Empty<string>(), PrimeRoute.Gateway);
        var window = new Window { Width = 940, Height = 720, Content = shell };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            PrimeRoute route = shell.CurrentRoute;
            PrimeTitleScreenPhase title = shell.TitlePhase;
            int returnRequests = 0;
            shell.MatchTransitionReturnToLobbyRequested += (_, _) =>
                returnRequests++;

            shell.ShowMatchTransition(new MatchTransitionState(
                MatchTransitionStage.Preparing, "Combat Hall", "Prime Hunter",
                "Kanden", "Freezing the match request."));
            MatchTransitionView transition = Assert.IsType<MatchTransitionView>(
                shell.ActiveMatchTransition);
            Assert.Equal(route, shell.CurrentRoute);
            Assert.Equal(title, shell.TitlePhase);

            shell.UpdateMatchTransition(transition.State with
            {
                Stage = MatchTransitionStage.Connecting,
                Detail = "Connecting to the assigned server."
            });
            Assert.Same(transition, shell.ActiveMatchTransition);
            Assert.Equal(MatchTransitionStage.Connecting, transition.State.Stage);

            shell.FailMatchTransition("The connection timed out.");
            Dispatcher.UIThread.RunJobs();
            Assert.True(transition.FailureVisible);
            Assert.Equal(MatchTransitionStage.Failed, transition.State.Stage);
            transition.GetVisualDescendants().OfType<AvaloniaButton>()
                .Single(button => Equals(button.Content, "Return to Lobby"))
                .RaiseEvent(new RoutedEventArgs(AvaloniaButton.ClickEvent));

            Assert.Equal(1, returnRequests);
            Assert.Null(shell.ActiveMatchTransition);
            Assert.Equal(route, shell.CurrentRoute);
            Assert.Equal(title, shell.TitlePhase);
        }
        finally
        {
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void BackCannotDismissCoordinatorOwnedMatchTransition()
    {
        PrimeShellView shell = PrimeShellView.CreateCapture(new MenuSettings(),
            Array.Empty<string>(), PrimeRoute.Gateway);
        var window = new Window { Width = 940, Height = 720, Content = shell };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            shell.ShowMatchTransition(new MatchTransitionState(
                MatchTransitionStage.Preparing, Detail: "Preparing content."));
            MatchTransitionView transition = Assert.IsType<MatchTransitionView>(
                shell.ActiveMatchTransition);

            Assert.True(shell.GoBack());
            Assert.Same(transition, shell.ActiveMatchTransition);

            shell.FailMatchTransition("Could not prepare the arena.");
            Assert.True(shell.GoBack());
            Assert.Same(transition, shell.ActiveMatchTransition);
            Assert.True(transition.FailureVisible);
        }
        finally
        {
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static string[] Copy(Control root)
        => root.GetVisualDescendants().OfType<TextBlock>()
            .Select(text => text.Text)
            .Where(text => !String.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToArray();
}
