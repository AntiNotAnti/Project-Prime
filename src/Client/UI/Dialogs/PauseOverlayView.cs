using System;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Dialogs;

/// <summary>Shared pause/session menu language; opening it never pauses server simulation.</summary>
public sealed class PauseOverlayView : Border
{
    private readonly IPauseOverlayController _controller;
    private readonly ErrorBanner _feedback = new() { IsVisible = false };
    private readonly CancellationTokenSource _lifetime = new();

    public PauseOverlayView(IPauseOverlayController controller)
    {
        _controller = controller;
        Background = UiColors.ScrimBrush;
        Padding = new Thickness(UiSpacing.Space5);
        AutomationProperties.SetName(this, controller.IsAuthoritativeMatch
            ? "Session menu. Match continues while open."
            : "Pause menu");
        var actions = new StackPanel { Spacing = UiSpacing.Space2, MaxWidth = 420 };
        actions.Children.Add(new TextBlock
        {
            Text = "SESSION",
            Foreground = UiColors.TextBrush,
            FontFamily = UiTypography.Family,
            FontSize = UiTypography.TextDisplay
        });
        if (controller.IsAuthoritativeMatch)
        {
            var status = new StatusBadge();
            status.SetStatus("Match continues while this menu is open", UiStatusTone.Warning);
            actions.Children.Add(status);
        }
        foreach (PauseAction action in Enum.GetValues<PauseAction>())
        {
            var button = new SecondaryButton
            {
                Content = Label(action), AccessibleName = Label(action)
            };
            button.Click += async (_, _) => await InvokeAsync(action);
            actions.Children.Add(button);
        }
        actions.Children.Add(_feedback);
        Child = actions;
        KeyboardNavigation.SetTabNavigation(actions, KeyboardNavigationMode.Cycle);
        DetachedFromVisualTree += (_, _) => _lifetime.Cancel();
    }

    private async System.Threading.Tasks.Task InvokeAsync(PauseAction action)
    {
        try
        {
            UiActionResult result = await _controller.InvokeAsync(action, _lifetime.Token);
            _feedback.IsVisible = !result.Succeeded;
            if (!result.Succeeded) _feedback.Message = result.Message;
        }
        catch (Exception error)
        {
            _feedback.Message = AsyncScreenState.FriendlyFailure(error, Label(action));
            _feedback.IsVisible = true;
        }
    }

    private static string Label(PauseAction action) => action switch
    {
        PauseAction.HunterLicense => "Hunter License",
        PauseAction.MutePlayers => "Mute Players",
        PauseAction.LeaveMatch => "Leave Match",
        PauseAction.LeaveServer => "Leave Server",
        _ => action.ToString()
    };
}
