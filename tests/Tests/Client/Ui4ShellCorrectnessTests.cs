using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Mods;
using MphRead.Mods.Launcher.Gui;
using Xunit;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class Ui4ShellCorrectnessTests
{
    [Fact]
    public void NotificationsDeduplicateByKeyAndRespectRouteAndGlobalScopes()
    {
        using var state = new PrimeShellState();
        state.Navigator.NavigateRoot(PrimeRoute.Play);
        state.NotifyRoute("browse-error", PrimeNotificationKind.Error, "Try again.");
        PrimeNotification first = Assert.IsType<PrimeNotification>(state.Notification);
        Assert.Equal(PrimeNotificationScope.Route, first.Scope);
        Assert.Equal(PrimeRoute.Play, first.Route!.Value);
        Assert.Null(first.ExpiresAt);

        state.NotifyRoute("browse-error", PrimeNotificationKind.Error, "Try again.");
        Assert.Same(first, state.Notification);

        state.Navigator.NavigateRoot(PrimeRoute.Hunter);
        Assert.Null(state.Notification);
        state.NotifyRoute("stale", PrimeNotificationKind.Warning, "Old route.",
            PrimeRoute.Play);
        Assert.Null(state.Notification);

        state.NotifyGlobal("update", PrimeNotificationKind.Info, "Update ready.");
        Assert.Equal(PrimeNotificationScope.Global, state.Notification!.Scope);
        state.NotifyTransient("saved", PrimeNotificationKind.Success, "Saved.",
            TimeSpan.FromMinutes(1));
        Assert.Equal("saved", state.Notification?.Key);
        state.DismissNotification();
        Assert.Equal("update", state.Notification?.Key);
        state.Navigator.NavigateRoot(PrimeRoute.Rankings);
        Assert.Equal("update", state.Notification?.Key);
        state.DismissNotification();
        Assert.Null(state.Notification);
    }

    [Fact]
    public void TransientNotificationExpiresThroughTheSingleStateOwner()
    {
        using var state = new PrimeShellState();
        using var expired = new ManualResetEventSlim();
        state.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PrimeShellState.Notification)
                && state.Notification is null)
                expired.Set();
        };

        state.NotifyTransient("saved", PrimeNotificationKind.Success, "Saved.",
            TimeSpan.FromMilliseconds(25));

        Assert.True(expired.Wait(TimeSpan.FromSeconds(2)));
        Assert.Null(state.Notification);
    }

    [Fact]
    public void GuestHunterFirstEntryDefaultsToArsenalAndThenPreservesChoice()
    {
        var state = new PrimeRouteViewState();

        Assert.Equal(HunterSection.Arsenal, state.PrepareHunterEntry(signedIn: false));
        state.SelectHunterSection(HunterSection.Matches);
        Assert.Equal(HunterSection.Matches, state.PrepareHunterEntry(signedIn: false));
        state.ResetGuestHunterEntry();
        Assert.Equal(HunterSection.Arsenal, state.PrepareHunterEntry(signedIn: false));
    }

    [AvaloniaFact]
    public void GuestHunterAccountTabsStayInlineWithoutQueriesOrToasts()
    {
        var capture = new PrimeShellCaptureState(
            Identity: PrimeShellCaptureIdentity.Guest);
        PrimeShellView shell = PrimeShellView.CreateCapture(new MenuSettings(),
            Array.Empty<string>(), PrimeRoute.Hunter, capture);
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            PrimeTabStrip tabs = Assert.Single(shell.GetVisualDescendants()
                .OfType<PrimeTabStrip>());
            Assert.Equal("Arsenal", tabs.SelectedTab.Label);

            tabs.Tabs.Single(tab => tab.Label == "Career").Invoke();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("Sign in to view this tab", VisibleCopy(shell));
            Assert.Contains("Sign In", VisibleCopy(shell));
            Assert.False(Find<Border>(shell, "NotificationBar").IsVisible);
            Assert.Null(ShellState(shell).Notification);
        }
        finally
        {
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void RootRoutesHaveNoGenericVisualBackButBackInputStillUsesHistory()
    {
        PrimeShellView shell = PrimeShellView.CreateCapture(new MenuSettings(),
            Array.Empty<string>(), PrimeRoute.Play);
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            typeof(PrimeShellView).GetMethod("Navigate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell,
                [PrimeRoute.Hunter]);
            Dispatcher.UIThread.RunJobs();

            Panel actionBar = Find<Panel>(shell, "ActionBar");
            Assert.DoesNotContain(actionBar.GetVisualDescendants()
                .OfType<AvaloniaButton>(), button => Equals(button.Content, "Back"));
            Assert.True(shell.GoBack());
            Assert.Equal(PrimeRoute.Play, shell.CurrentRoute);
        }
        finally
        {
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void SignedOutGatewayKeepsOneEntryPromptAndNoIdleFooterTicker()
    {
        PrimeShellView shell = PrimeShellView.CreateCapture(new MenuSettings(),
            Array.Empty<string>(), PrimeRoute.Gateway);
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            Assert.False(Find<AvaloniaButton>(shell, "AccountButton").IsVisible);
            Assert.False(Find<TextBlock>(shell, "StatusText").IsVisible);
            Assert.Equal(1, VisibleCopy(shell).Count(value => value ==
                "Sign in, create an account, or continue as a guest."));
        }
        finally
        {
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void ToastIsCenteredDismissibleAndDoesNotTakeFocus()
    {
        PrimeShellView shell = PrimeShellView.CreateCapture(new MenuSettings(),
            Array.Empty<string>(), PrimeRoute.Play);
        var window = new Window { Width = 940, Height = 560, Content = shell };
        window.Show();
        try
        {
            AvaloniaButton focused = shell.GetVisualDescendants().OfType<AvaloniaButton>()
                .First(button => button.Focusable && button.IsEffectivelyVisible);
            Assert.True(focused.Focus());
            ShellState(shell).NotifyGlobal("notice", PrimeNotificationKind.Info,
                "Connection restored.");
            Dispatcher.UIThread.RunJobs();

            Border toast = Find<Border>(shell, "NotificationBar");
            AvaloniaButton dismiss = Find<AvaloniaButton>(shell,
                "NotificationDismissButton");
            Assert.True(toast.IsVisible);
            Assert.Equal(HorizontalAlignment.Center, toast.HorizontalAlignment);
            Assert.True(dismiss.Focusable);
            Assert.True(focused.IsFocused);

            dismiss.RaiseEvent(new RoutedEventArgs(AvaloniaButton.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(toast.IsVisible);
        }
        finally
        {
            window.Content = null;
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void HunterPreviewFailureUsesCompactHeight()
    {
        Assert.Equal(180, PrimeShellView.HunterPreviewHeight(compactFailure: true));
        Assert.Equal(360, PrimeShellView.HunterPreviewHeight(compactFailure: false));
    }

    [Theory]
    [InlineData("The Node connection failed.")]
    [InlineData("Invalid Worker handoff.")]
    [InlineData("The Backend rejected the ticket.")]
    public void TechnicalConnectionErrorsUsePlayerFacingFallback(string technical)
    {
        const string fallback = "Could not join the match. Try again.";

        Assert.Equal(fallback,
            PrimeRoutePresentation.PlayerFacingNetworkError(technical, fallback));
        Assert.Equal("Connection timed out. Try again.",
            PrimeRoutePresentation.PlayerFacingNetworkError(
                "Connection timed out. Try again.", fallback));
    }

    [Fact]
    public void LegacyPlayerSurfacesDoNotRestoreTopologyCopy()
    {
        AssertSourceExcludes("src/Client.Presentation/Launcher/Gui/SettingsView.cs",
            "\"Backend service\"", "best available Node");
        AssertSourceExcludes("src/Client.Presentation/Launcher/Gui/NodeBrowserView.cs",
            "\"Server Nodes\"", "\"Refresh Nodes\"", "compatible Nodes",
            "Joining Worker", "Connected to Node", "Disconnect Node",
            "Resume Node", "Retry Worker", "This Node");
        AssertSourceExcludes("src/Client.Presentation/Launcher/Gui/AccountView.cs",
            "Backend configured");
        AssertSourceExcludes("src/Client/Launcher/Gui/PostMatchWindow.cs",
            "Node connection lost. Reconnect to continue.");
        AssertSourceExcludes(
            "src/Client.Core/Launcher/Shell/Results/PostMatchResultsModel.cs",
            "Node's immutable Worker result");
        AssertSourceExcludes("src/Client/Launcher/Gui/UiCapture.cs",
            "Project Prime Backend", "capture Backend", "The Node directory",
            "Node connection lost while", "Worker Recovery Room",
            "Worker handoff failed", "Node is preparing");
        AssertSourceExcludes("src/Client.Presentation/Launcher/Shell/PlayController.cs",
            "Node disconnected", "selected Node session", "The Node confirmed",
            "The Node did not confirm", "The Node rejected", "Node control origin",
            "hosted by this Node", "No Node ballot", "No Worker handoff",
            "Worker connection cancelled", "Allocating Worker", "browsing Nodes",
            "Configure a Backend", "Connect to a Node", "Select a Node",
            "This Node did not advertise", "This Node advertises");
    }

    private static PrimeShellState ShellState(PrimeShellView shell)
        => Assert.IsType<PrimeShellState>(typeof(PrimeShellView)
            .GetField("_shell", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(shell));

    private static T Find<T>(Control root, string name) where T : Control
        => root.GetVisualDescendants().OfType<T>().Single(control =>
            String.Equals(control.Name, name, StringComparison.Ordinal));

    private static IReadOnlyList<string> VisibleCopy(Control root)
        => root.GetVisualDescendants().Select(control => control switch
        {
            TextBlock text when text.IsEffectivelyVisible => text.Text,
            AvaloniaButton { Content: string content } button
                when button.IsEffectivelyVisible
                => content,
            _ => null
        }).Where(value => !String.IsNullOrWhiteSpace(value))
            .Select(value => value!).ToArray();

    private static void AssertSourceExcludes(string relativePath,
        params string[] forbidden)
    {
        string source = ReadRepositoryFile(relativePath);
        Assert.All(forbidden, value => Assert.DoesNotContain(value, source,
            StringComparison.Ordinal));
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        string? root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "Game.sln")))
            root = Directory.GetParent(root)?.FullName;
        return File.ReadAllText(Path.Combine(root ?? throw new InvalidOperationException(
            "Repository root was not found."), relativePath));
    }
}
