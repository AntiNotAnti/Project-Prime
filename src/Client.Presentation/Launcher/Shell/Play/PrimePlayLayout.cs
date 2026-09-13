using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace MphRead.Mods.Launcher.Gui;

internal enum PrimeContentLayout
{
    Mobile,
    Compact,
    Wide
}

internal enum PrimePlayLane
{
    Full,
    Left,
    Right
}

internal enum PrimePlayStage
{
    Always,
    One,
    Two
}

internal static class PrimePlayLayout
{
    internal const double CompactWidth = 720;
    internal const double WideWidth = 1080;

    internal static PrimeContentLayout ResolveContentLayout(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return PrimeContentLayout.Mobile;
        if (width < CompactWidth) return PrimeContentLayout.Mobile;
        if (width < WideWidth) return PrimeContentLayout.Compact;
        return PrimeContentLayout.Wide;
    }
}

/// <summary>
/// Responsive Play composition that owns one stable set of controls. Breakpoint
/// changes only alter placement and layout classes; children are never rebuilt
/// or reparented while a route instance is alive.
/// </summary>
internal sealed class PrimePlayResponsivePanel : Panel
{
    public static readonly AttachedProperty<PrimePlayLane> LaneProperty =
        AvaloniaProperty.RegisterAttached<PrimePlayResponsivePanel, Control, PrimePlayLane>(
            "Lane", PrimePlayLane.Full);
    public static readonly AttachedProperty<int> MobileOrderProperty =
        AvaloniaProperty.RegisterAttached<PrimePlayResponsivePanel, Control, int>(
            "MobileOrder", 0);
    public static readonly AttachedProperty<PrimePlayStage> StageProperty =
        AvaloniaProperty.RegisterAttached<PrimePlayResponsivePanel, Control, PrimePlayStage>(
            "Stage", PrimePlayStage.Always);
    public static readonly AttachedProperty<bool> HideInWideProperty =
        AvaloniaProperty.RegisterAttached<PrimePlayResponsivePanel, Control, bool>(
            "HideInWide", false);

    private readonly double _wideLeftWeight;
    private readonly double _wideRightWeight;
    private readonly bool _compactColumns;
    private PrimeContentLayout? _layout;
    private int _currentStep = 1;
    private ScrollViewer? _pageScroller;
    private Size _viewportSize;

    public PrimePlayResponsivePanel(double wideLeftWeight = 7,
        double wideRightWeight = 4, bool compactColumns = false)
    {
        _wideLeftWeight = Math.Max(1, wideLeftWeight);
        _wideRightWeight = Math.Max(1, wideRightWeight);
        _compactColumns = compactColumns;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
    }

    public double Spacing { get; set; } = 14;

    public event Action<PrimeContentLayout>? LayoutChanged;

    public event Action<int>? CurrentStepChanged;

    public event Action<Size>? ViewportChanged;

    public PrimeContentLayout Layout => _layout ?? PrimeContentLayout.Mobile;

    public Size ViewportSize => _viewportSize;

    internal int LayoutTransitionCount { get; private set; }

    public int CurrentStep
    {
        get => _currentStep;
        set
        {
            int normalized = value == 2 ? 2 : 1;
            if (_currentStep == normalized) return;
            _currentStep = normalized;
            ApplyChildVisibility(Layout);
            CurrentStepChanged?.Invoke(normalized);
            InvalidateMeasure();
        }
    }

    public static void SetLane(Control control, PrimePlayLane lane)
        => control.SetValue(LaneProperty, lane);

    public static PrimePlayLane GetLane(Control control)
        => control.GetValue(LaneProperty);

    public static void SetMobileOrder(Control control, int order)
        => control.SetValue(MobileOrderProperty, order);

    public static int GetMobileOrder(Control control)
        => control.GetValue(MobileOrderProperty);

    public static void SetStage(Control control, PrimePlayStage stage)
        => control.SetValue(StageProperty, stage);

    public static PrimePlayStage GetStage(Control control)
        => control.GetValue(StageProperty);

    public static void SetHideInWide(Control control, bool hide)
        => control.SetValue(HideInWideProperty, hide);

    public static bool GetHideInWide(Control control)
        => control.GetValue(HideInWideProperty);

    internal void ApplyLayout(double width)
    {
        PrimeContentLayout next = PrimePlayLayout.ResolveContentLayout(width);
        if (_layout == next) return;
        if (_layout == PrimeContentLayout.Wide && next != PrimeContentLayout.Wide
            && ActiveEditorStage() is { } activeStage)
            CurrentStep = activeStage == PrimePlayStage.Two ? 2 : 1;
        _layout = next;
        LayoutTransitionCount++;
        Classes.Remove("prime-layout-mobile");
        Classes.Remove("prime-layout-compact");
        Classes.Remove("prime-layout-wide");
        Classes.Add(next switch
        {
            PrimeContentLayout.Wide => "prime-layout-wide",
            PrimeContentLayout.Compact => "prime-layout-compact",
            _ => "prime-layout-mobile"
        });
        ApplyChildVisibility(next);
        LayoutChanged?.Invoke(next);
        InvalidateMeasure();
    }

    internal void ApplyViewport(Size viewport)
    {
        Size next = NormalizeViewport(viewport);
        if (_viewportSize == next) return;
        _viewportSize = next;
        ViewportChanged?.Invoke(next);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _pageScroller = this.GetVisualAncestors().OfType<ScrollViewer>()
            .FirstOrDefault(scroller => scroller.Name == "PageScroller");
        if (_pageScroller is null) return;
        _pageScroller.SizeChanged += PageScrollerSizeChanged;
        _pageScroller.ScrollChanged += PageScrollerScrollChanged;
        ApplyViewport(_pageScroller.Viewport);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_pageScroller is not null)
        {
            _pageScroller.SizeChanged -= PageScrollerSizeChanged;
            _pageScroller.ScrollChanged -= PageScrollerScrollChanged;
            _pageScroller = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = ResolveWidth(availableSize.Width);
        ApplyLayout(width);
        double measuredWidth = double.IsFinite(availableSize.Width)
            ? Math.Max(0, availableSize.Width) : Math.Max(0, Bounds.Width);
        PrimeContentLayout layout = Layout;
        bool columns = layout == PrimeContentLayout.Wide
            || layout == PrimeContentLayout.Compact && _compactColumns;
        if (!columns)
            return MeasureStack(VisibleChildren(), measuredWidth);

        IReadOnlyList<Control> full = LaneChildren(PrimePlayLane.Full);
        IReadOnlyList<Control> left = LaneChildren(PrimePlayLane.Left);
        IReadOnlyList<Control> right = LaneChildren(PrimePlayLane.Right);
        double fullHeight = MeasureLane(full, measuredWidth);
        (double leftWidth, double rightWidth) = ColumnWidths(measuredWidth, layout);
        double leftHeight = MeasureLane(left, leftWidth);
        double rightHeight = MeasureLane(right, rightWidth);
        double splitHeight = Math.Max(leftHeight, rightHeight);
        double joinSpacing = full.Count > 0 && splitHeight > 0 ? Spacing : 0;
        return new Size(measuredWidth, fullHeight + joinSpacing + splitHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        ApplyLayout(finalSize.Width);
        PrimeContentLayout layout = Layout;
        bool columns = layout == PrimeContentLayout.Wide
            || layout == PrimeContentLayout.Compact && _compactColumns;
        if (!columns)
        {
            ArrangeLane(VisibleChildren(), 0, 0, finalSize.Width);
            return finalSize;
        }

        IReadOnlyList<Control> full = LaneChildren(PrimePlayLane.Full);
        double y = ArrangeLane(full, 0, 0, finalSize.Width);
        IReadOnlyList<Control> left = LaneChildren(PrimePlayLane.Left);
        IReadOnlyList<Control> right = LaneChildren(PrimePlayLane.Right);
        if (full.Count > 0 && (left.Count > 0 || right.Count > 0)) y += Spacing;
        (double leftWidth, double rightWidth) = ColumnWidths(finalSize.Width, layout);
        ArrangeLane(left, 0, y, leftWidth);
        double rightX = left.Count > 0 && right.Count > 0
            ? leftWidth + Spacing : 0;
        ArrangeLane(right, rightX, y, rightWidth);
        return finalSize;
    }

    private double ResolveWidth(double availableWidth)
        => double.IsFinite(availableWidth) && availableWidth > 0
            ? availableWidth
            : double.IsFinite(Bounds.Width) && Bounds.Width > 0 ? Bounds.Width : 0;

    private PrimePlayStage? ActiveEditorStage()
    {
        Control? focused = TopLevel.GetTopLevel(this)?.FocusManager?
            .GetFocusedElement() as Control;
        foreach (Control child in Children)
        {
            PrimePlayStage stage = GetStage(child);
            if (stage == PrimePlayStage.Always) continue;
            bool containsFocus = focused is not null
                && (ReferenceEquals(child, focused)
                    || focused.GetVisualAncestors().Any(ancestor => ReferenceEquals(
                        ancestor, child)));
            bool containsOpenCombo = (child as ComboBox)?.IsDropDownOpen == true
                || child.GetVisualDescendants().OfType<ComboBox>()
                    .Any(combo => combo.IsDropDownOpen);
            if (containsFocus || containsOpenCombo) return stage;
        }
        return null;
    }

    private void PageScrollerSizeChanged(object? sender, SizeChangedEventArgs args)
        => ObservePageScrollerViewport();

    private void PageScrollerScrollChanged(object? sender, ScrollChangedEventArgs args)
        => ObservePageScrollerViewport();

    private void ObservePageScrollerViewport()
    {
        if (_pageScroller is not null) ApplyViewport(_pageScroller.Viewport);
    }

    private static Size NormalizeViewport(Size viewport)
        => new(double.IsFinite(viewport.Width) ? Math.Max(0, viewport.Width) : 0,
            double.IsFinite(viewport.Height) ? Math.Max(0, viewport.Height) : 0);

    private IEnumerable<Control> VisibleChildren()
        => Children.Where(child => child.IsVisible)
            .OrderBy(GetMobileOrder);

    private IReadOnlyList<Control> LaneChildren(PrimePlayLane lane)
        => Children.Where(child => child.IsVisible && GetLane(child) == lane)
            .OrderBy(GetMobileOrder).ToArray();

    private Size MeasureStack(IEnumerable<Control> controls, double width)
    {
        Control[] items = controls.ToArray();
        double height = MeasureLane(items, width);
        return new Size(width, height);
    }

    private double MeasureLane(IReadOnlyList<Control> controls, double width)
    {
        double height = 0;
        for (int index = 0; index < controls.Count; index++)
        {
            Control child = controls[index];
            child.Measure(new Size(Math.Max(0, width), double.PositiveInfinity));
            height += child.DesiredSize.Height;
            if (index + 1 < controls.Count) height += Spacing;
        }
        return height;
    }

    private double ArrangeLane(IEnumerable<Control> controls, double x, double y,
        double width)
    {
        Control[] items = controls.ToArray();
        double cursor = y;
        for (int index = 0; index < items.Length; index++)
        {
            Control child = items[index];
            child.Arrange(new Rect(x, cursor, Math.Max(0, width),
                child.DesiredSize.Height));
            cursor += child.DesiredSize.Height;
            if (index + 1 < items.Length) cursor += Spacing;
        }
        return cursor;
    }

    private (double Left, double Right) ColumnWidths(double width,
        PrimeContentLayout layout)
    {
        double usable = Math.Max(0, width - Spacing);
        if (layout == PrimeContentLayout.Compact)
            return (usable / 2, usable / 2);
        double total = _wideLeftWeight + _wideRightWeight;
        return (usable * _wideLeftWeight / total,
            usable * _wideRightWeight / total);
    }

    private void ApplyChildVisibility(PrimeContentLayout layout)
    {
        foreach (Control child in Children)
        {
            PrimePlayStage stage = GetStage(child);
            bool stageVisible = layout == PrimeContentLayout.Wide
                || stage == PrimePlayStage.Always
                || stage == PrimePlayStage.One && CurrentStep == 1
                || stage == PrimePlayStage.Two && CurrentStep == 2;
            child.IsVisible = stageVisible
                && !(layout == PrimeContentLayout.Wide && GetHideInWide(child));
        }
    }
}

/// <summary>Compact responsive field grid with stable editor instances.</summary>
internal sealed class PrimeFieldGrid : Panel
{
    public PrimeFieldGrid(int maximumColumns = 3, double minimumColumnWidth = 180)
    {
        MaximumColumns = Math.Max(1, maximumColumns);
        MinimumColumnWidth = Math.Max(1, minimumColumnWidth);
    }

    public int MaximumColumns { get; }
    public double MinimumColumnWidth { get; }
    public double ColumnSpacing { get; set; } = 12;
    public double RowSpacing { get; set; } = 10;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width)
            ? Math.Max(0, availableSize.Width) : Math.Max(0, Bounds.Width);
        int columns = ResolveColumns(width);
        double cellWidth = CellWidth(width, columns);
        double[] rowHeights = MeasureRows(columns, cellWidth);
        return new Size(width, rowHeights.Sum()
            + Math.Max(0, rowHeights.Length - 1) * RowSpacing);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = ResolveColumns(finalSize.Width);
        double cellWidth = CellWidth(finalSize.Width, columns);
        double[] rowHeights = Children
            .Select((_, index) => index / columns)
            .Distinct()
            .Select(row => Children.Skip(row * columns).Take(columns)
                .Max(child => child.DesiredSize.Height)).ToArray();
        double y = 0;
        for (int index = 0; index < Children.Count; index++)
        {
            int row = index / columns;
            int column = index % columns;
            double x = column * (cellWidth + ColumnSpacing);
            Children[index].Arrange(new Rect(x, y, cellWidth, rowHeights[row]));
            if (column == columns - 1 || index == Children.Count - 1)
                y += rowHeights[row] + RowSpacing;
        }
        return finalSize;
    }

    private int ResolveColumns(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return 1;
        int fit = (int)Math.Floor((width + ColumnSpacing)
            / (MinimumColumnWidth + ColumnSpacing));
        return Math.Clamp(fit, 1, MaximumColumns);
    }

    private double CellWidth(double width, int columns)
        => Math.Max(0, (width - Math.Max(0, columns - 1) * ColumnSpacing) / columns);

    private double[] MeasureRows(int columns, double cellWidth)
    {
        int rows = (Children.Count + columns - 1) / columns;
        var heights = new double[rows];
        for (int index = 0; index < Children.Count; index++)
        {
            Control child = Children[index];
            child.Measure(new Size(cellWidth, double.PositiveInfinity));
            int row = index / columns;
            heights[row] = Math.Max(heights[row], child.DesiredSize.Height);
        }
        return heights;
    }
}
