using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using MphRead.Mods.UI.Components;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Navigation;

/// <summary>In-shell modal surface. It never creates a platform window.</summary>
public sealed class UiModalHost : Border
{
    private readonly ContentControl _content;
    private readonly SecondaryButton _close;

    public UiModalHost()
    {
        IsVisible = false;
        Background = UiColors.ScrimBrush;
        Padding = new Thickness(UiSpacing.Space5);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "Dialog");

        _content = new ContentControl();
        _close = new SecondaryButton { Content = "Close", AccessibleName = "Close dialog" };
        var dialog = new Border
        {
            Background = UiColors.PanelRaisedBrush,
            BorderBrush = UiColors.EdgeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(UiMetrics.RadiusLarge),
            Padding = new Thickness(UiSpacing.Space5),
            MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Spacing = UiSpacing.Space4,
                Children = { _content, _close }
            }
        };
        KeyboardNavigation.SetTabNavigation(dialog, KeyboardNavigationMode.Cycle);
        Child = dialog;
    }

    public void Bind(UiRouter router)
    {
        _close.Click += (_, _) => router.CloseModal();
        router.Changed += (_, e) =>
        {
            if (e.Kind == UiNavigationChangeKind.ModalOpened && e.Modal is { } modal)
            {
                Show(modal);
            }
            else if (e.Kind == UiNavigationChangeKind.ModalClosed)
            {
                Hide();
            }
        };
    }

    public bool TryMoveFocus(int direction)
    {
        Control[] controls = FocusableControls();
        if (controls.Length == 0) return false;
        int current = System.Array.FindIndex(controls, control => control.IsFocused);
        int next = current < 0 ? 0 : (current + direction + controls.Length) % controls.Length;
        return controls[next].Focus();
    }

    public bool TryActivateFocused()
    {
        Control? focused = FocusableControls().FirstOrDefault(control => control.IsFocused);
        return focused is not null && UiFocusCoordinator.TryActivate(focused);
    }

    public bool TryAdjustFocusedSelection(int delta)
    {
        Control? focused = FocusableControls().FirstOrDefault(control => control.IsFocused);
        return focused is not null && UiFocusCoordinator.TryAdjustSelection(focused, delta);
    }

    private Control[] FocusableControls() => this.GetVisualDescendants().OfType<Control>()
        .Where(control => control.Focusable && control.IsVisible && control.IsEffectivelyEnabled)
        .ToArray();

    private void Show(UiModalState modal)
    {
        _content.Content = modal.Content is Control control
            ? control
            : new TextBlock
            {
                Text = modal.Content?.ToString() ?? modal.Id,
                Foreground = UiColors.TextBrush,
                FontFamily = UiTypography.Family,
                FontSize = UiTypography.TextBody,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
        AutomationProperties.SetName(this, $"{modal.Id} dialog");
        IsVisible = true;
        _close.Focus();
    }

    private void Hide()
    {
        IsVisible = false;
        _content.Content = null;
    }
}
