using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Presentation;

/// <summary>
/// Deterministic code-native map art used when authored previews are absent.
/// The fallback has no bitmap or renderer dependency and derives its route
/// geometry from the title, so every route presents the same map consistently.
/// </summary>
internal class PrimeMapFallback : Control
{
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(70,
        GuiTheme.Tech.R, GuiTheme.Tech.G, GuiTheme.Tech.B)), 1);
    private static readonly Pen AmberPen = new(GuiTheme.BrandBrush, 2);
    private readonly string _title;
    private readonly MapInstallSource _source;
    private readonly int _seed;
    private readonly FormattedText _titleText;
    private readonly FormattedText _sourceText;

    internal PrimeMapFallback(string title, MapInstallSource source)
    {
        _title = String.IsNullOrWhiteSpace(title) ? "Map" : title.Trim();
        _source = source;
        _seed = StableSeed(_title);
        _titleText = TrackedText.Make(_title.ToUpperInvariant(), 14,
            bold: true, GuiTheme.TextBrush);
        string sourceLabel = _source switch
        {
            MapInstallSource.LocalProject or MapInstallSource.LegacyRecipe => "LOCAL MAP",
            _ => "PROJECT PRIME MAP"
        };
        _sourceText = TrackedText.Make(sourceLabel, 9, bold: true,
            GuiTheme.TechBrush);
        Classes.Add("prime-map-preview-fallback");
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width < 8 || Bounds.Height < 8) return;

        Rect area = new(0, 0, Bounds.Width, Bounds.Height);
        context.DrawRectangle(GuiTheme.InkBrush, null, area);
        double spacing = Math.Max(18, Math.Min(30, area.Width / 10));
        for (double x = spacing / 2; x < area.Width; x += spacing)
            context.DrawLine(GridPen, new Point(x, 0), new Point(x, area.Height));
        for (double y = spacing / 2; y < area.Height; y += spacing)
            context.DrawLine(GridPen, new Point(0, y), new Point(area.Width, y));

        double inset = Math.Max(14, Math.Min(area.Width, area.Height) * 0.12);
        Rect route = area.Deflate(inset);
        double bend = 0.2 + (_seed % 4) * 0.1;
        Point first = new(route.Left, route.Top + route.Height * bend);
        Point second = new(route.Left + route.Width * 0.35, route.Top);
        Point third = new(route.Right, route.Top + route.Height * 0.32);
        Point fourth = new(route.Left + route.Width * 0.68, route.Bottom);
        Point fifth = new(route.Left, route.Top + route.Height * 0.74);
        context.DrawLine(AmberPen, first, second);
        context.DrawLine(AmberPen, second, third);
        context.DrawLine(AmberPen, third, fourth);
        context.DrawLine(AmberPen, fourth, fifth);
        context.DrawLine(AmberPen, fifth, first);

        _titleText.MaxTextWidth = Math.Max(40, area.Width - 24);
        _titleText.Trimming = TextTrimming.CharacterEllipsis;
        context.DrawText(_titleText, new Point(12, Math.Max(8, area.Height - 40)));
        context.DrawText(_sourceText, new Point(12, Math.Max(8, area.Height - 20)));
    }

    private static int StableSeed(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char character in value)
                hash = hash * 31 + character;
            return Math.Abs(hash == Int32.MinValue ? Int32.MaxValue : hash);
        }
    }
}
