using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using MphRead.Mods.Launcher.Theme;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>Factories and native controls shared by Prime routes and overlays.</summary>
internal static class PrimeControlFactory
{
    public static AvaloniaButton Button(string label, Action? action = null,
        bool primary = false, bool quiet = false)
    {
        var button = new PrimeButton(label, action);
        button.Classes.Add("prime-button");
        if (primary) button.Classes.Add("prime-primary");
        if (quiet) button.Classes.Add("prime-quiet");
        return button;
    }

    /// <summary>Shared compact heading with an optional system kicker.</summary>
    public static PrimePageHeading PageHeading(string title, string? kicker = null,
        string? subtitle = null, Control? trailingContent = null)
        => new(title, kicker, subtitle, trailingContent);

    public static PrimeSectionPanel SectionPanel(Control child)
        => new(child);

    public static PrimeDivider Divider() => new();

    public static PrimeSelectedRow SelectedRow(Control child, bool selected)
        => new(child, selected);

    public static PrimePreviewStage PreviewStage(Control? child = null)
        => new(child);

    public static PrimeStatTile StatTile(string label, string value, string? detail = null)
        => new(label, value, detail);

    /// <summary>Creates a named surface-level panel for reusable route content.</summary>
    public static PrimePanel Panel(Control child, bool secondary = false,
        bool compact = false)
        => new(child, secondary, compact);

    public static PrimeCompactPanel CompactPanel(Control child)
        => new(child);

    public static PrimeHeroCard HeroCard(Control child) => new(child);

    public static PrimeActionCard ActionCard(Control child, bool selected = false)
        => new(child, selected);

    public static PrimeTechFrame TechFrame(Control child) => new(child);

    public static PrimeStatRail StatRail(params PrimeStatTile[] tiles)
        => new(tiles);

    public static PrimeStatRail StatRail(
        IEnumerable<(string Label, string Value)> stats)
        => new(stats);

    public static PrimeStatRail StatRail(
        IEnumerable<(string Label, string Value, string? Detail)> stats)
        => new(stats);

    public static PrimeEmptyState EmptyState(string? title, string? body,
        Control? primaryAction = null, Control? secondaryAction = null,
        string? glyph = null, bool compact = false)
        => new(title, body, primaryAction, secondaryAction, glyph, compact);
}

/// <summary>
/// Small code-native brand mark. It deliberately uses vector strokes so the
/// same asset remains crisp at desktop and Android densities.
/// </summary>
internal sealed class PrimeDiamond : Control
{
    public static readonly StyledProperty<IBrush?> StrokeBrushProperty =
        AvaloniaProperty.Register<PrimeDiamond, IBrush?>(nameof(StrokeBrush));

    public IBrush? StrokeBrush
    {
        get => GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public double StrokeThickness { get; set; } = 2;

    static PrimeDiamond() => AffectsRender<PrimeDiamond>(StrokeBrushProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 2 || Bounds.Height <= 2) return;
        var pen = new Pen(StrokeBrush ?? Brushes.Transparent, StrokeThickness);
        Point center = new(Bounds.Width / 2, Bounds.Height / 2);
        double radius = Math.Min(Bounds.Width, Bounds.Height) * 0.38;
        Point top = new(center.X, center.Y - radius);
        Point right = new(center.X + radius, center.Y);
        Point bottom = new(center.X, center.Y + radius);
        Point left = new(center.X - radius, center.Y);
        context.DrawLine(pen, top, right);
        context.DrawLine(pen, right, bottom);
        context.DrawLine(pen, bottom, left);
        context.DrawLine(pen, left, top);

        double inner = radius * 0.48;
        Point innerTop = new(center.X, center.Y - inner);
        Point innerRight = new(center.X + inner, center.Y);
        Point innerBottom = new(center.X, center.Y + inner);
        Point innerLeft = new(center.X - inner, center.Y);
        context.DrawLine(pen, innerTop, innerRight);
        context.DrawLine(pen, innerRight, innerBottom);
        context.DrawLine(pen, innerBottom, innerLeft);
        context.DrawLine(pen, innerLeft, innerTop);
    }
}

/// <summary>Static cyan dot-grid surface used behind shell pages.</summary>
internal sealed class PrimeDotGrid : Control
{
    public static readonly StyledProperty<IBrush?> DotBrushProperty =
        AvaloniaProperty.Register<PrimeDotGrid, IBrush?>(nameof(DotBrush));
    public static readonly StyledProperty<double> DotSpacingProperty =
        AvaloniaProperty.Register<PrimeDotGrid, double>(nameof(DotSpacing), 24);
    public static readonly StyledProperty<double> DotRadiusProperty =
        AvaloniaProperty.Register<PrimeDotGrid, double>(nameof(DotRadius), 0.8);

    public IBrush? DotBrush
    {
        get => GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    public double DotSpacing
    {
        get => GetValue(DotSpacingProperty);
        set => SetValue(DotSpacingProperty, value);
    }

    public double DotRadius
    {
        get => GetValue(DotRadiusProperty);
        set => SetValue(DotRadiusProperty, value);
    }

    static PrimeDotGrid() => AffectsRender<PrimeDotGrid>(DotBrushProperty,
        DotSpacingProperty, DotRadiusProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double spacing = Math.Max(8, DotSpacing);
        double radius = Math.Max(0.25, DotRadius);
        IBrush brush = DotBrush ?? Brushes.Transparent;
        for (double y = spacing / 2; y < Bounds.Height; y += spacing)
        {
            for (double x = spacing / 2; x < Bounds.Width; x += spacing)
                context.DrawEllipse(brush, null, new Point(x, y), radius, radius);
        }
    }
}

/// <summary>
/// Reusable page heading with an explicit trailing slot. Text is kept as
/// properties rather than being rediscovered through child traversal so route
/// refreshes can update copy without depending on a particular visual tree.
/// </summary>
internal sealed class PrimePageHeading : Grid
{
    private readonly TextBlock _kickerText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Classes = { "prime-kicker" }
    };
    private readonly TextBlock _titleText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Classes = { "prime-title" }
    };
    private readonly TextBlock _subtitleText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Classes = { "prime-muted" }
    };
    private readonly Border _trailingHost = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center
    };

    public PrimePageHeading(string title, string? kicker, string? subtitle,
        Control? trailingContent = null)
    {
        Classes.Add("prime-page-heading");
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        RowDefinitions = new RowDefinitions("Auto,Auto,Auto");
        RowSpacing = 4;
        HorizontalAlignment = HorizontalAlignment.Stretch;

        Children.Add(_kickerText);
        Grid.SetRow(_kickerText, 0);
        Children.Add(_trailingHost);
        Grid.SetRow(_trailingHost, 0);
        Grid.SetColumn(_trailingHost, 1);
        Children.Add(_titleText);
        Grid.SetRow(_titleText, 1);
        Grid.SetColumnSpan(_titleText, 2);
        Children.Add(_subtitleText);
        Grid.SetRow(_subtitleText, 2);
        Grid.SetColumnSpan(_subtitleText, 2);

        Update(title, kicker, subtitle, trailingContent);
    }

    public string Title
    {
        get => _titleText.Text ?? String.Empty;
        set => _titleText.Text = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string? Kicker
    {
        get => _kickerText.IsVisible ? _kickerText.Text : null;
        set
        {
            _kickerText.Text = value;
            _kickerText.IsVisible = !String.IsNullOrWhiteSpace(value);
        }
    }

    public string? Subtitle
    {
        get => _subtitleText.IsVisible ? _subtitleText.Text : null;
        set
        {
            _subtitleText.Text = value;
            _subtitleText.IsVisible = !String.IsNullOrWhiteSpace(value);
        }
    }

    public Control? TrailingContent
    {
        get => _trailingHost.Child;
        set
        {
            _trailingHost.Child = value;
            _trailingHost.IsVisible = value != null;
        }
    }

    public void Update(string title, string? kicker = null, string? subtitle = null,
        Control? trailingContent = null)
    {
        Title = title;
        Kicker = kicker;
        Subtitle = subtitle;
        TrailingContent = trailingContent;
    }
}

/// <summary>
/// Signature Project Prime framing for high-priority content. Corner and rail
/// decorations are ordinary static controls, so content remains the only hit
/// target and the frame adds no render-clock or animation work.
/// </summary>
internal sealed class PrimeTechFrame : Grid
{
    private Control _child = null!;
    private readonly List<Control> _decorations = new();

    public PrimeTechFrame(Control child)
    {
        Classes.Add("prime-tech-frame");
        Child = child;
        AddDecoration("prime-tech-frame-rail prime-tech-frame-top",
            HorizontalAlignment.Stretch, VerticalAlignment.Top, Double.NaN, 1,
            new Thickness(18, 0, 18, 0));
        AddDecoration("prime-tech-frame-rail prime-tech-frame-left",
            HorizontalAlignment.Left, VerticalAlignment.Stretch, 2, Double.NaN,
            new Thickness(0, 18, 0, 18));
        AddDecoration("prime-tech-frame-corner prime-tech-frame-corner-top-left",
            HorizontalAlignment.Left, VerticalAlignment.Top, 20, 2,
            new Thickness(0));
        AddDecoration("prime-tech-frame-corner prime-tech-frame-corner-top-left",
            HorizontalAlignment.Left, VerticalAlignment.Top, 2, 20,
            new Thickness(0));
        AddDecoration("prime-tech-frame-corner prime-tech-frame-corner-bottom-right",
            HorizontalAlignment.Right, VerticalAlignment.Bottom, 20, 2,
            new Thickness(0));
        AddDecoration("prime-tech-frame-corner prime-tech-frame-corner-bottom-right",
            HorizontalAlignment.Right, VerticalAlignment.Bottom, 2, 20,
            new Thickness(0));
    }

    public Control Child
    {
        get => _child;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_child, value)) return;
            if (_child?.Parent == this) Children.Remove(_child);
            _child = value;
            Children.Insert(0, _child);
        }
    }

    public Control Content
    {
        get => _child;
        set => Child = value;
    }

    public IReadOnlyList<Control> Decorations => _decorations;

    private void AddDecoration(string classes, HorizontalAlignment horizontal,
        VerticalAlignment vertical, double width, double height, Thickness margin)
    {
        var decoration = new Border
        {
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Width = width,
            Height = height,
            Margin = margin,
            IsHitTestVisible = false,
            Focusable = false
        };
        foreach (string @class in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            decoration.Classes.Add(@class);
        AutomationProperties.SetAccessibilityView(decoration,
            AccessibilityView.Raw);
        decoration.ZIndex = 1;
        _decorations.Add(decoration);
        Children.Add(decoration);
    }
}

/// <summary>Compact horizontal/wrapping summary of related values.</summary>
internal sealed class PrimeStatRail : WrapPanel
{
    private readonly List<PrimeStatTile> _tiles = new();

    public PrimeStatRail(IEnumerable<PrimeStatTile> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        Classes.Add("prime-stat-rail");
        Orientation = Orientation.Horizontal;
        ItemSpacing = 8;
        LineSpacing = 8;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        int index = 0;
        foreach (PrimeStatTile tile in tiles)
        {
            ArgumentNullException.ThrowIfNull(tile);
            tile.Classes.Add("prime-stat-rail-item");
            if (index++ == 0)
                tile.Classes.Add("prime-stat-rail-first");
            _tiles.Add(tile);
            Children.Add(tile);
        }
    }

    public PrimeStatRail(IEnumerable<(string Label, string Value)> stats)
        : this(CreateTiles(stats)) { }

    public PrimeStatRail(
        IEnumerable<(string Label, string Value, string? Detail)> stats)
        : this(CreateTiles(stats)) { }

    public IReadOnlyList<PrimeStatTile> Tiles => _tiles;

    private static IEnumerable<PrimeStatTile> CreateTiles(
        IEnumerable<(string Label, string Value)> stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        foreach ((string label, string value) in stats)
            yield return new PrimeStatTile(label, value);
    }

    private static IEnumerable<PrimeStatTile> CreateTiles(
        IEnumerable<(string Label, string Value, string? Detail)> stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        foreach ((string label, string value, string? detail) in stats)
            yield return new PrimeStatTile(label, value, detail);
    }
}

/// <summary>Reusable section panel with the shell's thinner edge treatment.</summary>
internal class PrimeSectionPanel : Border
{
    public PrimeSectionPanel(Control child)
    {
        Child = child;
        Classes.Add("prime-section-panel");
    }
}

internal sealed class PrimeDivider : Border
{
    public PrimeDivider()
    {
        Height = 1;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Classes.Add("prime-divider");
    }
}

internal sealed class PrimeSelectedRow : Border
{
    private bool _selected;

    public PrimeSelectedRow(Control child, bool selected)
    {
        Child = child;
        Classes.Add("prime-selected-row");
        SetSelected(selected);
    }

    public bool IsSelected => _selected;

    /// <summary>
    /// Changes persistent selection without touching focus or layout geometry.
    /// </summary>
    public void SetSelected(bool selected)
    {
        _selected = selected;
        if (selected) Classes.Add("prime-selected");
        else Classes.Remove("prime-selected");
    }
}

internal sealed class PrimePreviewStage : Border
{
    public PrimePreviewStage(Control? child = null)
    {
        Child = child;
        Classes.Add("prime-preview-stage");
    }
}

internal sealed class PrimeStatTile : Border
{
    public PrimeStatTile(string label, string value, string? detail = null)
    {
        Classes.Add("prime-stat-tile");
        var content = new StackPanel { Spacing = 3 };
        content.Children.Add(new TextBlock { Text = label, Classes = { "prime-label" } });
        content.Children.Add(new TextBlock { Text = value, Classes = { "prime-stat-value" } });
        if (!string.IsNullOrWhiteSpace(detail))
            content.Children.Add(new TextBlock { Text = detail, Classes = { "prime-muted" } });
        Child = content;
    }
}

internal sealed class PrimeButton : AvaloniaButton
{
    private readonly Action? _action;

    public PrimeButton(string label, Action? action)
    {
        Content = label;
        _action = action;
        Transitions = PrimeMotion.CreateInteractiveTransitions();
        if (action != null) Click += (_, _) => action();
    }

    public void Invoke() => _action?.Invoke();
}

internal sealed class PrimeCard : Border
{
    public PrimeCard(Control child)
    {
        Child = child;
        Classes.Add("prime-card");
    }
}

internal class PrimePanel : Border
{
    public PrimePanel(Control child, bool secondary, bool compact)
    {
        Child = child;
        Classes.Add("prime-panel");
        if (secondary) Classes.Add("prime-panel-secondary");
        if (compact) Classes.Add("prime-compact-panel");
    }
}

internal sealed class PrimeCompactPanel : PrimePanel
{
    public PrimeCompactPanel(Control child)
        : base(child, secondary: true, compact: true) { }
}

internal sealed class PrimeHeroCard : Border
{
    public PrimeHeroCard(Control child)
    {
        Child = child;
        Classes.Add("prime-hero-card");
    }
}

internal sealed class PrimeActionCard : Border
{
    public PrimeActionCard(Control child, bool selected)
    {
        Child = child;
        Classes.Add("prime-action-card");
        Transitions = PrimeMotion.CreateSurfaceTransitions();
        if (selected) Classes.Add("prime-selected");
    }

    public bool IsSelected => Classes.Contains("prime-selected");

    public void SetSelected(bool selected)
    {
        if (selected) Classes.Add("prime-selected");
        else Classes.Remove("prime-selected");
    }
}

internal sealed class PrimeStatusChip : Border
{
    private readonly TextBlock _text;

    public PrimeStatusChip(string text, IBrush? foreground = null)
    {
        Classes.Add("prime-status-chip");
        _text = new TextBlock { Text = text, FontSize = 12 };
        _text.Classes.Add("prime-muted");
        if (foreground != null) _text.Foreground = foreground;
        Padding = new Avalonia.Thickness(8, 4);
        CornerRadius = new Avalonia.CornerRadius(4);
        Child = _text;
    }

    public PrimeStatusChip(string text, PrimeStatusKind kind)
        : this(text)
    {
        SetStatus(text, kind);
    }

    public void SetStatus(string text, PrimeStatusKind kind)
    {
        if (String.IsNullOrWhiteSpace(text))
            throw new ArgumentException("A status label is required.", nameof(text));
        Text = text.Trim();
        PrimeAccessibility.SetStatus(this, Text, kind);
        PrimeAccessibility.SetStatus(_text, Text, kind);
    }

    public string Text
    {
        get => _text.Text ?? "";
        set => _text.Text = value;
    }
}

internal sealed class PrimeEmptyState : StackPanel
{
    public PrimeEmptyState(string message)
        : this(null, message, null, null, null, compact: true) { }

    public PrimeEmptyState(string? title, string? body,
        Control? primaryAction = null, Control? secondaryAction = null,
        string? glyph = null, bool compact = false)
    {
        Title = String.IsNullOrWhiteSpace(title) ? null : title.Trim();
        Body = String.IsNullOrWhiteSpace(body) ? null : body.Trim();
        PrimaryAction = primaryAction;
        SecondaryAction = secondaryAction;
        Glyph = String.IsNullOrWhiteSpace(glyph) ? null : glyph.Trim();
        Compact = compact;

        Spacing = compact ? 4 : 8;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Classes.Add("prime-empty-state");
        if (compact) Classes.Add("prime-compact");

        if (Glyph != null)
        {
            var glyphBlock = new TextBlock
            {
                Text = Glyph,
                TextWrapping = TextWrapping.NoWrap,
                Classes = { "prime-empty-glyph" }
            };
            Children.Add(glyphBlock);
        }
        if (Title != null)
        {
            Children.Add(new TextBlock
            {
                Text = Title,
                TextWrapping = TextWrapping.Wrap,
                Classes = { "prime-empty-title" }
            });
        }
        if (Body != null)
        {
            Children.Add(new TextBlock
            {
                Text = Body,
                TextWrapping = TextWrapping.Wrap,
                Classes = { "prime-empty-body", "prime-muted" }
            });
        }
        if (PrimaryAction != null || SecondaryAction != null)
        {
            var actions = new WrapPanel { Orientation = Orientation.Horizontal,
                ItemSpacing = 8, LineSpacing = 4 };
            if (PrimaryAction != null) actions.Children.Add(PrimaryAction);
            if (SecondaryAction != null) actions.Children.Add(SecondaryAction);
            Children.Add(actions);
        }
    }

    public string? Title { get; }
    public string? Body { get; }
    public string? Glyph { get; }
    public bool Compact { get; }
    public Control? PrimaryAction { get; }
    public Control? SecondaryAction { get; }
}

internal sealed class PrimeLocalImage : Image
{
    private Bitmap? _bitmap;

    public PrimeLocalImage(string path, double height = 180)
    {
        _bitmap = new Bitmap(path);
        Source = _bitmap;
        Height = height;
        Stretch = Stretch.Uniform;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }
}
