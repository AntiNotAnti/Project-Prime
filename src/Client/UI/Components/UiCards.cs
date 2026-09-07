using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.UI.Theme;

namespace MphRead.Mods.UI.Components;

public class FocusCard : UiActionButton
{
    public FocusCard() : base(UiColors.PanelBrush, UiColors.TextBrush, UiColors.EdgeBrush)
    {
        CornerRadius = new CornerRadius(UiMetrics.RadiusMedium);
        Padding = new Thickness(UiSpacing.Space4);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }
}

public abstract class SummaryCard : FocusCard
{
    private readonly TextBlock _title;
    private readonly TextBlock _detail;

    protected SummaryCard(string kind)
    {
        _title = new TextBlock
        {
            FontFamily = UiTypography.Family,
            FontSize = UiTypography.TextBody,
            FontWeight = FontWeight.SemiBold,
            Foreground = UiColors.TextBrush
        };
        _detail = new TextBlock
        {
            FontFamily = UiTypography.Family,
            FontSize = UiTypography.TextSmall,
            Foreground = UiColors.TextMutedBrush,
            TextWrapping = TextWrapping.Wrap
        };
        ContentPanel = new StackPanel { Spacing = UiSpacing.Space1, Children = { _title, _detail } };
        Content = ContentPanel;
        AutomationProperties.SetItemType(this, kind);
    }

    protected StackPanel ContentPanel { get; }

    public string Title
    {
        get => _title.Text ?? string.Empty;
        set
        {
            _title.Text = value;
            AutomationProperties.SetName(this, value);
        }
    }

    public string Detail
    {
        get => _detail.Text ?? string.Empty;
        set => _detail.Text = value;
    }
}

public sealed class PlayerRow : SummaryCard { public PlayerRow() : base("Player") { } }
public sealed class HunterCard : SummaryCard { public HunterCard() : base("Hunter") { } }
public sealed class MapCard : SummaryCard
{
    private readonly Image _preview;
    private readonly TextBlock _placeholder;

    public MapCard() : base("Map")
    {
        _preview = new Image
        {
            Height = 108,
            Stretch = Stretch.UniformToFill,
            IsVisible = false
        };
        _placeholder = new TextBlock
        {
            Text = "Preview unavailable",
            FontFamily = UiTypography.Family,
            FontSize = UiTypography.TextSmall,
            Foreground = UiColors.TextMutedBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var frame = new Border
        {
            Height = 108,
            Background = UiColors.InkBrush,
            BorderBrush = UiColors.EdgeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(UiMetrics.RadiusSmall),
            ClipToBounds = true,
            Child = new Grid { Children = { _placeholder, _preview } }
        };
        ContentPanel.Children.Insert(0, frame);
    }

    public bool HasPreview => _preview.Source is not null;

    public void ShowPreview(IImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _preview.Source = image;
        _preview.IsVisible = true;
        _placeholder.IsVisible = false;
    }

    public void ShowPlaceholder()
    {
        _preview.Source = null;
        _preview.IsVisible = false;
        _placeholder.IsVisible = true;
    }
}
public sealed class ServerCard : SummaryCard { public ServerCard() : base("Server") { } }
