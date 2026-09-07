namespace MphRead.Mods.UI.Theme;

public static class UiScreenLayout
{
    public static int BrowserColumns(UiLayoutMode mode) => mode == UiLayoutMode.Compact ? 1 : 2;
    public static int CardColumns(UiLayoutMode mode) => mode switch
    {
        UiLayoutMode.Compact => 1,
        UiLayoutMode.Medium => 2,
        _ => 3
    };
}
