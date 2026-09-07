using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Screens.Play;

public sealed class PlayScreenView : ScreenViewBase
{
    private readonly IPlayScreenController _controller;
    private readonly UiRouter _router;
    private readonly StackPanel _actions;
    private readonly ErrorBanner _feedback = new() { IsVisible = false };
    private readonly List<(Grid Grid, Control Action)> _cards = [];
    private CancellationTokenSource? _request;

    public PlayScreenView(UiRouter router, IPlayScreenController controller)
        : base("Play", "Choose how you want to enter an authoritative session.", "play:quick")
    {
        _router = router;
        _controller = controller;
        _actions = new StackPanel { Spacing = UiSpacing.Space3 };
        AddAsync("play:quick", "Quick Play", "Find the best compatible casual server.",
            controller.QuickPlayAsync, primary: true);
        AddRanked();
        AddRoute("play:browser", "Server Browser", "Choose a public server and role.",
            UiRoute.ServerBrowser);
        AddRoute("play:private", "Private Match", "Create a configurable private lobby.",
            UiRoute.PrivateMatch);
        AddAsync("play:practice", "Practice", "Start a private localhost lobby with server bots.",
            controller.StartPracticeAsync);
        Body.Content = new StackPanel { Spacing = UiSpacing.Space3, Children = { _actions, _feedback } };
    }

    protected override void OnLayoutModeChanged(UiLayoutMode mode)
    {
        bool compact = mode == UiLayoutMode.Compact;
        foreach ((Grid grid, Control action) in _cards)
        {
            grid.ColumnDefinitions = compact ? new ColumnDefinitions("*")
                : new ColumnDefinitions("*,Auto");
            grid.RowDefinitions = compact ? new RowDefinitions("Auto,Auto")
                : new RowDefinitions("Auto");
            Grid.SetColumn(action, compact ? 0 : 1);
            Grid.SetRow(action, compact ? 1 : 0);
            action.Margin = compact ? new Avalonia.Thickness(0, UiSpacing.Space2, 0, 0) : default;
        }
    }

    private void AddRanked()
    {
        RankedAvailability availability = _controller.Ranked;
        var button = new SecondaryButton
        {
            Content = availability.Available ? "Ranked" : "Ranked — Unavailable",
            AccessibleName = availability.Available ? "Start Ranked" : $"Ranked unavailable. {availability.Reason}",
            IsEnabled = availability.Available
        };
        button.Click += async (_, _) => await RunAsync(_controller.StartRankedAsync);
        _actions.Children.Add(Card("Ranked", availability.Available
            ? "Join a verified competitive server using the active rating policy."
            : availability.Reason, Register("play:ranked", button)));
    }

    private void AddRoute(string key, string title, string description, UiRoute route)
    {
        var button = Register(key, new SecondaryButton
        {
            Content = title,
            AccessibleName = $"Open {title}"
        });
        button.Click += (_, _) => _router.Navigate(route, sourceFocusKey: key);
        _actions.Children.Add(Card(title, description, button));
    }

    private void AddAsync(string key, string title, string description,
        Func<CancellationToken, System.Threading.Tasks.Task<UiActionResult>> action,
        bool primary = false)
    {
        UiActionButton button = primary ? new PrimaryButton() : new SecondaryButton();
        button.Content = title;
        button.AccessibleName = title;
        button.Click += async (_, _) => await RunAsync(action);
        _actions.Children.Add(Card(title, description, Register(key, button)));
    }

    private async System.Threading.Tasks.Task RunAsync(
        Func<CancellationToken, System.Threading.Tasks.Task<UiActionResult>> action)
    {
        _request?.Cancel();
        _request?.Dispose();
        _request = CancellationTokenSource.CreateLinkedTokenSource(ScreenCancellation);
        try
        {
            UiActionResult result = await action(_request.Token);
            _feedback.IsVisible = !result.Succeeded;
            if (!result.Succeeded) _feedback.Message = result.Message;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            _feedback.Message = AsyncScreenState.FriendlyFailure(error, "Play");
            _feedback.IsVisible = true;
        }
    }

    private Border Card(string title, string description, Control action)
    {
        var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = UiSpacing.Space4 };
        panel.Children.Add(new StackPanel
        {
            Spacing = UiSpacing.Space1,
            Children = { Text(title, size: UiTypography.TextHeading), Text(description, muted: true) }
        });
        panel.Children.Add(action);
        Grid.SetColumn(action, 1);
        _cards.Add((panel, action));
        return new Border
        {
            Background = UiColors.PanelBrush,
            BorderBrush = UiColors.EdgeBrush,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(UiMetrics.RadiusMedium),
            Padding = new Avalonia.Thickness(UiSpacing.Space4),
            Child = panel
        };
    }
}
