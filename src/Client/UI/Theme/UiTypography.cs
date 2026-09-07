using Avalonia.Media;

namespace MphRead.Mods.UI.Theme;

public static class UiTypography
{
    public static readonly FontFamily Family =
        new("avares://Avalonia.Fonts.Inter/Assets#Inter");

    public const double TextMinimum = 12;
    public const double TextSmall = 13;
    public const double TextBody = 16;
    public const double TextHeading = 22;
    public const double TextDisplay = 36;

    public static double Scale(double size, double uiScale, bool largeText)
        => System.Math.Max(TextMinimum, size * uiScale * (largeText ? 1.15 : 1));
}
