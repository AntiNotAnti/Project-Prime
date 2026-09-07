namespace MphRead.Mods.UI.Navigation;

public enum UiRoute
{
    Home,
    Play,
    Lobby,
    ServerBrowser,
    PrivateMatch,
    HunterLicense,
    Replays,
    Settings,
    PostMatch,
    Account
}

public static class UiRouteInfo
{
    public static string Label(UiRoute route) => route switch
    {
        UiRoute.Home => "Home",
        UiRoute.Play => "Play",
        UiRoute.Lobby => "Lobby",
        UiRoute.ServerBrowser => "Server Browser",
        UiRoute.PrivateMatch => "Private Match",
        UiRoute.HunterLicense => "Hunter License",
        UiRoute.Replays => "Replays",
        UiRoute.Settings => "Settings",
        UiRoute.PostMatch => "Post-match Results",
        UiRoute.Account => "Account",
        _ => route.ToString()
    };

    public static string CompactLabel(UiRoute route) => route switch
    {
        UiRoute.HunterLicense => "License",
        _ => Label(route)
    };

    public static bool IsTopLevel(UiRoute route) => route is UiRoute.Home or UiRoute.Play
        or UiRoute.HunterLicense or UiRoute.Replays or UiRoute.Settings;
}
