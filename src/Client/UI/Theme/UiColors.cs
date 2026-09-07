using Avalonia.Media;

namespace MphRead.Mods.UI.Theme;

/// <summary>Shared Prime Hunters UI colors. Status meaning always has a text label.</summary>
public static class UiColors
{
    public static readonly Color Ink = Color.FromRgb(10, 12, 16);
    public static readonly Color Panel = Color.FromRgb(18, 21, 28);
    public static readonly Color PanelRaised = Color.FromRgb(26, 31, 41);
    public static readonly Color Edge = Color.FromRgb(38, 46, 60);
    public static readonly Color Text = Color.FromRgb(230, 234, 242);
    public static readonly Color TextMuted = Color.FromRgb(155, 165, 184);
    public static readonly Color Accent = Color.FromRgb(41, 197, 255);
    public static readonly Color Warning = Color.FromRgb(255, 179, 71);
    public static readonly Color Success = Color.FromRgb(110, 231, 135);
    public static readonly Color Danger = Color.FromRgb(255, 107, 107);

    public static readonly IBrush InkBrush = Frozen(Ink);
    public static readonly IBrush PanelBrush = Frozen(Panel);
    public static readonly IBrush PanelRaisedBrush = Frozen(PanelRaised);
    public static readonly IBrush EdgeBrush = Frozen(Edge);
    public static readonly IBrush TextBrush = Frozen(Text);
    public static readonly IBrush TextMutedBrush = Frozen(TextMuted);
    public static readonly IBrush AccentBrush = Frozen(Accent);
    public static readonly IBrush WarningBrush = Frozen(Warning);
    public static readonly IBrush SuccessBrush = Frozen(Success);
    public static readonly IBrush DangerBrush = Frozen(Danger);
    public static readonly IBrush ScrimBrush = Frozen(Color.FromArgb(210, Ink.R, Ink.G, Ink.B));

    public static UiTeamPalette TeamPalette(UiColorVisionMode mode) => mode switch
    {
        UiColorVisionMode.Deuteranopia => new(Color.FromRgb(45, 156, 219), Color.FromRgb(230, 159, 0)),
        UiColorVisionMode.Protanopia => new(Color.FromRgb(86, 180, 233), Color.FromRgb(213, 94, 0)),
        UiColorVisionMode.Tritanopia => new(Color.FromRgb(0, 158, 115), Color.FromRgb(204, 121, 167)),
        _ => new(Color.FromRgb(67, 147, 255), Color.FromRgb(255, 91, 91))
    };

    private static SolidColorBrush Frozen(Color color)
    {
        return new SolidColorBrush(color);
    }
}

public enum UiColorVisionMode
{
    Standard,
    Deuteranopia,
    Protanopia,
    Tritanopia
}

public readonly record struct UiTeamPalette(Color Alpha, Color Beta);
