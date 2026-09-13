using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using MphRead.Mods.Launcher.Theme;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Shared visual contract for the code-drawn controls that still make up the
/// Settings surface. The controls keep their input and persistence behavior;
/// this type only centralizes the small set of paint roles and geometry rules
/// they need to agree on.
/// </summary>
internal static class PrimeLegacyControlVisuals
{
    internal const double RowHeight = PrimeTouchTargets.MinimumDip;
    internal const double SubtitledRowHeight = 54;
    internal const double FieldHeight = PrimeTouchTargets.MinimumDip;
    internal const double LabelInset = 4;
    internal const double LabelValueGap = 12;
    internal const double MarkerRailWidth = 3;
    internal const double ControlRadius = 4;
    internal const double MinimumValueColumn = 72;
    internal const double MaximumValueColumn = 180;
    internal const double ValuePadding = 16;
    internal const double MinimumControlWidth = 72;
    internal const double FocusMarkerThickness = 1;

    internal static readonly IBrush HoverSurfaceBrush = GuiTheme.PanelLightBrush;
    internal static readonly IBrush FocusSurfaceBrush = new SolidColorBrush(
        Color.FromArgb(72, GuiTheme.PanelLight.R, GuiTheme.PanelLight.G,
            GuiTheme.PanelLight.B));
    internal static readonly IBrush SelectedSurfaceBrush = GuiTheme.BrandSurfaceBrush;
    internal static readonly IBrush SelectedBrush = GuiTheme.AccentBrush;
    internal static readonly IBrush FocusBrush = GuiTheme.TechStrongBrush;
    internal static readonly IBrush EdgeBrush = GuiTheme.EdgeBrush;
    internal static readonly IBrush TextBrush = GuiTheme.TextBrush;
    internal static readonly IBrush MutedTextBrush = GuiTheme.TextDimBrush;
    internal static readonly IBrush DisabledTextBrush = new SolidColorBrush(
        Color.FromRgb(100, 108, 118));
    internal static readonly IBrush DisabledSurfaceBrush = new SolidColorBrush(
        Color.FromRgb(34, 40, 48));
    internal static readonly IBrush DisabledTrackBrush = new SolidColorBrush(
        Color.FromRgb(70, 76, 90));
    internal static readonly IBrush DisabledKnobBrush = new SolidColorBrush(
        Color.FromRgb(120, 126, 140));
    internal static readonly IBrush OnBrush = GuiTheme.InkBrush;

    private static readonly Pen FocusPen = new(FocusBrush, FocusMarkerThickness);
    private static readonly Pen EdgePen = new(EdgeBrush, 1);

    internal static double MeasureLabel(string text, double size = 13,
        double tracking = 0)
        => Math.Max(0, TrackedText.Measure(text, size, tracking));

    internal static IBrush Brush(Color color)
        => color == GuiTheme.Accent ? SelectedBrush : new SolidColorBrush(color);

    internal static IBrush AccentBrush(Color color) => Brush(color);

    internal static double MeasureValueColumn(IEnumerable<string> values,
        double size = 13, double minimum = MinimumValueColumn,
        double maximum = MaximumValueColumn)
    {
        double widest = 0;
        foreach (string value in values)
        {
            if (value == null)
            {
                continue;
            }
            widest = Math.Max(widest, TrackedText.Make(value, size,
                bold: false, TextBrush).Width);
        }
        return Math.Clamp(widest + ValuePadding, minimum, maximum);
    }

    internal static double ResolveLabelColumn(string label,
        double requested = 0, double minimum = 96, double maximum = 220)
    {
        double measured = MeasureLabel(label, size: 12);
        return Math.Clamp(Math.Max(requested, measured + LabelValueGap),
            minimum, maximum);
    }

    internal static void DrawHitSurface(DrawingContext context, Rect bounds)
        => context.FillRectangle(Brushes.Transparent, bounds);

    internal static void DrawHoverSurface(DrawingContext context, Rect bounds,
        bool hovered, bool pressed, bool enabled)
    {
        if (!enabled || !hovered && !pressed)
        {
            return;
        }
        context.FillRectangle(HoverSurfaceBrush, bounds, (float)ControlRadius);
    }

    internal static void DrawFocusedSurface(DrawingContext context, Rect bounds,
        bool focused, bool enabled)
    {
        if (!focused || !enabled)
        {
            return;
        }
        context.FillRectangle(FocusSurfaceBrush, bounds, (float)ControlRadius);
    }

    internal static void DrawFocusMarker(DrawingContext context, Rect bounds,
        bool focused, bool enabled)
    {
        if (!focused || !enabled || bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }
        Rect marker = new(bounds.X + .5, bounds.Y + .5,
            Math.Max(0, bounds.Width - 1), Math.Max(0, bounds.Height - 1));
        context.DrawRectangle(null, FocusPen, new RoundedRect(marker,
            (float)ControlRadius));
    }

    internal static void DrawSelectedRail(DrawingContext context, Rect bounds,
        bool selected, bool enabled, Color? accent = null)
    {
        if (!selected || !enabled)
        {
            return;
        }
        IBrush brush = accent is Color custom && custom != GuiTheme.Accent
            ? new SolidColorBrush(custom) : SelectedBrush;
        context.FillRectangle(brush,
            new Rect(bounds.X, bounds.Y, MarkerRailWidth, bounds.Height));
    }

    internal static void DrawControlOutline(DrawingContext context, Rect bounds,
        bool focused, bool active, bool enabled)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }
        IBrush brush = !enabled ? DisabledTrackBrush
            : active ? GuiTheme.WarmBrush
            : focused ? FocusBrush : EdgeBrush;
        context.DrawRectangle(enabled ? HoverSurfaceBrush : DisabledSurfaceBrush,
            new Pen(brush, 1), new RoundedRect(bounds, (float)ControlRadius));
    }

    internal static void DrawEdge(DrawingContext context, Rect bounds)
        => context.DrawRectangle(null, EdgePen, new RoundedRect(bounds,
            (float)ControlRadius));
}
