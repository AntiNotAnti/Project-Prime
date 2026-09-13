using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MphRead;
using MphRead.Mods.Launcher.Presentation;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// A small, deterministic preview surface for routes that do not have a
/// decoded model image yet. It is deliberately composed from native controls
/// and vector strokes: a missing preview must still communicate what is being
/// inspected without starting a renderer or showing an empty rectangle.
/// </summary>
internal sealed class HunterPreviewFallback : Border
{
    private HunterPreviewFallback(string title, string kicker, string detail,
        int seed, bool compact = false)
    {
        Classes.Add("prime-technical-preview");
        Background = GuiTheme.InkBrush;
        Padding = new Thickness(compact ? 10 : 16);
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        if (compact)
        {
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                RowSpacing = 4,
                Children =
                {
                    new HunterPreviewGlyph(seed),
                    Label(title, "prime-card-heading")
                }
            };
            Grid compactGrid = (Grid)Child;
            Grid.SetRow((Control)compactGrid.Children[0], 0);
            Grid.SetRow((Control)compactGrid.Children[1], 1);
            return;
        }
        Child = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto,Auto"),
            RowSpacing = 8,
            Children =
            {
                new HunterPreviewGlyph(seed),
                Label(kicker, "prime-kicker"),
                Label(title, "prime-title"),
                Label(detail, "prime-muted")
            }
        };
        Grid grid = (Grid)Child;
        Grid.SetRow((Control)grid.Children[0], 0);
        Grid.SetRow((Control)grid.Children[1], 1);
        Grid.SetRow((Control)grid.Children[2], 2);
        Grid.SetRow((Control)grid.Children[3], 3);
    }

    internal static HunterPreviewFallback ForHunter(Hunter hunter)
        => new(
            PrimeGameText.HunterLabel(hunter),
            "HUNTER PROFILE",
            "Local preview art keeps this hunter visible while the full image is unavailable.",
            StableSeed(hunter.ToString()));

    internal static HunterPreviewFallback ForHunterCompact(Hunter hunter)
        => new(
            PrimeGameText.HunterLabel(hunter),
            "HUNTER PROFILE",
            String.Empty,
            StableSeed(hunter.ToString()),
            compact: true);

    internal static HunterPreviewFallback ForWeapon(PrimeWeaponDetails weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        string detail = $"Affinity: {weapon.AffinityHunters}\n"
            + $"Charge: {weapon.ChargedDamage.ToString(CultureInfo.InvariantCulture)} damage"
            + $" · {weapon.ChargedProjectiles.ToString(CultureInfo.InvariantCulture)} projectiles";
        return new HunterPreviewFallback(weapon.Name, "ARSENAL PROFILE", detail,
            StableSeed(weapon.Beam.ToString()));
    }

    private static TextBlock Label(string text, string style)
        => new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Classes = { style },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

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

/// <summary>Vector silhouette shared by the Hunter and Arsenal fallbacks.</summary>
internal sealed class HunterPreviewGlyph : Control
{
    private readonly int _seed;

    internal HunterPreviewGlyph(int seed)
    {
        _seed = seed;
        Classes.Add("prime-preview-glyph");
        MinHeight = 64;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width < 12 || Bounds.Height < 12) return;

        Rect area = Bounds.Deflate(Math.Max(4, Math.Min(Bounds.Width,
            Bounds.Height) * 0.08));
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(76,
            GuiTheme.Tech.R, GuiTheme.Tech.G, GuiTheme.Tech.B)), 1);
        double spacing = Math.Max(16, Math.Min(28, area.Width / 9));
        for (double x = area.Left; x <= area.Right; x += spacing)
            context.DrawLine(gridPen, new Point(x, area.Top),
                new Point(x, area.Bottom));
        for (double y = area.Top; y <= area.Bottom; y += spacing)
            context.DrawLine(gridPen, new Point(area.Left, y),
                new Point(area.Right, y));

        var amber = new Pen(GuiTheme.BrandBrush, 2);
        double centerX = area.Center.X;
        double shoulder = Math.Max(10, area.Width * (0.16 + (_seed % 3) * 0.04));
        double body = Math.Max(12, area.Height * 0.26);
        Point top = new(centerX, area.Top + area.Height * 0.12);
        Point left = new(centerX - shoulder, area.Top + area.Height * 0.43);
        Point right = new(centerX + shoulder, area.Top + area.Height * 0.43);
        Point bottom = new(centerX, area.Bottom - area.Height * 0.1);
        context.DrawLine(amber, top, left);
        context.DrawLine(amber, top, right);
        context.DrawLine(amber, left, bottom);
        context.DrawLine(amber, right, bottom);
        context.DrawLine(amber,
            new Point(centerX - body, area.Center.Y),
            new Point(centerX + body, area.Center.Y));
        context.DrawLine(amber,
            new Point(centerX, area.Center.Y - body),
            new Point(centerX, area.Center.Y + body));
        context.DrawEllipse(null, amber, new Point(centerX, area.Center.Y),
            Math.Max(8, body * 0.8), Math.Max(8, body * 0.8));
    }
}
