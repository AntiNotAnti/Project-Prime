using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Application destinations. A destination does not imply that a native presenter is ready.</summary>
internal enum LauncherPage
{
    Home, Play, Lobby, Hunters, Community, Theatre, Offline, Adventure, License, Settings, StudioLaunch, Social, News
}

internal readonly record struct LauncherRoute(LauncherPage Page, string? Item = null);

internal enum LauncherPresentation { RmlUi, Legacy, Unavailable }

/// <summary>Canonical routes and compatibility aliases, independent of either UI toolkit.</summary>
internal static class LauncherRouteCatalog
{
    private static readonly IReadOnlyList<LauncherPage> _tabs = Array.AsReadOnly(new[]
    {
        LauncherPage.Home, LauncherPage.Play, LauncherPage.License, LauncherPage.Theatre,
        LauncherPage.StudioLaunch, LauncherPage.Offline, LauncherPage.Settings
    });

    // Preserve the actual shipped tab order until additional pages achieve parity.
    public static IReadOnlyList<LauncherPage> Tabs => _tabs;

    public static string Name(LauncherPage page) => page switch
    {
        LauncherPage.Home => "home", LauncherPage.Play => "play", LauncherPage.Lobby => "lobby",
        LauncherPage.Hunters => "hunters", LauncherPage.Community => "community",
        LauncherPage.Theatre => "theatre", LauncherPage.Offline => "offline",
        LauncherPage.Adventure => "adventure", LauncherPage.License => "license",
        LauncherPage.Settings => "settings", LauncherPage.StudioLaunch => "studio", LauncherPage.Social => "social",
        LauncherPage.News => "news",
        _ => throw new ArgumentOutOfRangeException(nameof(page))
    };

    public static bool IsValid(LauncherRoute route) => Enum.IsDefined(route.Page)
        && (route.Item == null || route.Item.Length <= 4096 && route.Item.IndexOf('\0') < 0);

    public static string DeepLink(LauncherRoute route)
    {
        if (!IsValid(route)) throw new ArgumentException("Invalid launcher route.", nameof(route));
        string link = "prime://launcher/" + Name(route.Page);
        return route.Item == null ? link : link + "?item=" + Uri.EscapeDataString(route.Item);
    }

    public static bool TryParse(string? value, out LauncherRoute route)
    {
        route = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32768) return false;
        string name = value.Trim();
        string? item = null;
        if (name.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(name, UriKind.Absolute, out Uri? uri)
                || uri.Scheme != "prime" || uri.Host != "launcher" || uri.Port != -1
                || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
            name = uri.AbsolutePath;
            if (name.Length < 2 || name[0] != '/' || name.IndexOf('/', 1) >= 0) return false;
            name = name[1..];
            if (uri.Query.Length > 0)
            {
                if (!uri.Query.StartsWith("?item=", StringComparison.Ordinal)
                    || uri.Query.Contains('&')) return false;
                item = Uri.UnescapeDataString(uri.Query[6..]);
            }
        }
        LauncherPage? page = name.ToLowerInvariant() switch
        {
            "home" => LauncherPage.Home, "news" => LauncherPage.News,
            "play" => LauncherPage.Play, "lobby" => LauncherPage.Lobby,
            "hunters" => LauncherPage.Hunters, "community" => LauncherPage.Community,
            "theatre" => LauncherPage.Theatre, "offline" => LauncherPage.Offline,
            "adventure" => LauncherPage.Adventure,
            "license" or "hunterlicense" or "hunter-license" => LauncherPage.License,
            "settings" => LauncherPage.Settings,
            "social" or "party" => LauncherPage.Social,
            "studio" or "studiolaunch" or "forge" => LauncherPage.StudioLaunch,
            _ => null
        };
        if (page == null) return false;
        var parsed = new LauncherRoute(page.Value, item);
        if (!IsValid(parsed)) return false;
        route = parsed;
        return true;
    }
}
