using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Components;

public abstract class UiActionButton : ContentControl
{
    private readonly IBrush _normalBorder;
    private bool _pressed;

    protected UiActionButton(IBrush background, IBrush foreground, IBrush border)
    {
        _normalBorder = border;
        Background = background;
        Foreground = foreground;
        BorderBrush = border;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(UiMetrics.RadiusSmall);
        Padding = new Thickness(UiSpacing.Space4, UiSpacing.Space3);
        MinHeight = UiMetrics.MinimumTouchTarget;
        FontFamily = UiTypography.Family;
        FontSize = UiTypography.TextBody;
        FontWeight = FontWeight.SemiBold;
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
        Focusable = true;

        GotFocus += (_, _) =>
        {
            BorderBrush = UiColors.AccentBrush;
            BorderThickness = new Thickness(UiMetrics.FocusThickness);
        };
        LostFocus += (_, _) =>
        {
            BorderBrush = _normalBorder;
            BorderThickness = new Thickness(1);
        };
    }

    public event System.EventHandler? Click;

    /// <summary>Invoke the same action as a pointer release or Enter/Space press.</summary>
    public bool TryInvoke()
    {
        if (!IsEffectivelyEnabled) return false;
        Click?.Invoke(this, System.EventArgs.Empty);
        return true;
    }

    public string AccessibleName
    {
        set => AutomationProperties.SetName(this, value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEffectivelyEnabled) return;
        _pressed = true;
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pressed) return;
        _pressed = false;
        e.Pointer.Capture(null);
        if (IsEffectivelyEnabled && new Rect(Bounds.Size).Contains(e.GetPosition(this)))
        {
            TryInvoke();
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsEffectivelyEnabled && e.Key is Key.Enter or Key.Space)
        {
            TryInvoke();
            e.Handled = true;
        }
    }
}

public sealed class PrimaryButton : UiActionButton
{
    public PrimaryButton() : base(UiColors.AccentBrush, UiColors.InkBrush, UiColors.AccentBrush) { }
}

public sealed class SecondaryButton : UiActionButton
{
    public SecondaryButton() : base(UiColors.PanelRaisedBrush, UiColors.TextBrush, UiColors.EdgeBrush) { }
}
