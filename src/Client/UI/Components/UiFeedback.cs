using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Components;

public enum UiStatusTone
{
    Neutral,
    Good,
    Warning,
    Error
}

public sealed class StatusBadge : Border
{
    private readonly TextBlock _text;

    public StatusBadge()
    {
        CornerRadius = new CornerRadius(UiMetrics.RadiusSmall);
        Padding = new Thickness(UiSpacing.Space2, UiSpacing.Space1);
        _text = new TextBlock
        {
            FontFamily = UiTypography.Family,
            FontSize = UiTypography.TextSmall,
            FontWeight = FontWeight.SemiBold
        };
        Child = _text;
        SetStatus("Offline", UiStatusTone.Neutral);
    }

    public void SetStatus(string label, UiStatusTone tone)
    {
        _text.Text = label;
        AutomationProperties.SetName(this, $"Status: {label}");
        AutomationProperties.SetItemStatus(this, label);
        Background = tone switch
        {
            UiStatusTone.Good => UiColors.SuccessBrush,
            UiStatusTone.Warning => UiColors.WarningBrush,
            UiStatusTone.Error => UiColors.DangerBrush,
            _ => UiColors.PanelRaisedBrush
        };
        _text.Foreground = tone == UiStatusTone.Neutral ? UiColors.TextBrush : UiColors.InkBrush;
    }
}

public sealed class ErrorBanner : Border
{
    private readonly TextBlock _text;

    public ErrorBanner()
    {
        Background = UiColors.PanelRaisedBrush;
        BorderBrush = UiColors.DangerBrush;
        BorderThickness = new Thickness(2, 0, 0, 0);
        Padding = new Thickness(UiSpacing.Space4);
        _text = new TextBlock
        {
            Foreground = UiColors.TextBrush,
            FontFamily = UiTypography.Family,
            FontSize = UiTypography.TextBody,
            TextWrapping = TextWrapping.Wrap
        };
        Child = _text;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Assertive);
    }

    public string Message
    {
        get => _text.Text ?? string.Empty;
        set
        {
            _text.Text = value;
            AutomationProperties.SetName(this, $"Error: {value}");
        }
    }
}

public sealed class LoadingIndicator : StackPanel
{
    private readonly TextBlock _label;

    public LoadingIndicator()
    {
        Orientation = Orientation.Horizontal;
        Spacing = UiSpacing.Space3;
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(new ProgressBar { IsIndeterminate = true, Width = 96, Height = 4 });
        _label = new TextBlock
        {
            Text = "Loading",
            Foreground = UiColors.TextMutedBrush,
            FontFamily = UiTypography.Family,
            FontSize = UiTypography.TextBody
        };
        Children.Add(_label);
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        Label = "Loading";
    }

    public string Label
    {
        get => _label.Text ?? string.Empty;
        set
        {
            _label.Text = value;
            AutomationProperties.SetName(this, value);
        }
    }
}

public sealed class EmptyState : Border
{
    private readonly TextBlock _title;
    private readonly TextBlock _detail;

    public EmptyState()
    {
        Background = UiColors.PanelBrush;
        BorderBrush = UiColors.EdgeBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(UiMetrics.RadiusMedium);
        Padding = new Thickness(UiSpacing.Space6);
        _title = new TextBlock
        {
            Foreground = UiColors.TextBrush,
            FontFamily = UiTypography.Family,
            FontSize = UiTypography.TextHeading,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _detail = new TextBlock
        {
            Foreground = UiColors.TextMutedBrush,
            FontFamily = UiTypography.Family,
            FontSize = UiTypography.TextBody,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Child = new StackPanel { Spacing = UiSpacing.Space2, Children = { _title, _detail } };
    }

    public void SetContent(string title, string detail)
    {
        _title.Text = title;
        _detail.Text = detail;
        AutomationProperties.SetName(this, $"{title}. {detail}");
    }
}
