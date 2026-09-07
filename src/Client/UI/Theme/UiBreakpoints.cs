namespace MphRead.Mods.UI.Theme;

public enum UiLayoutMode
{
    Compact,
    Medium,
    Wide
}

public static class UiBreakpoints
{
    public const double CompactMax = 719;
    public const double MediumMax = 1099;

    public static UiLayoutMode FromWidth(double width)
    {
        if (double.IsNaN(width) || width <= CompactMax)
        {
            return UiLayoutMode.Compact;
        }
        return width <= MediumMax ? UiLayoutMode.Medium : UiLayoutMode.Wide;
    }
}
