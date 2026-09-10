using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Routes owned by the native Project Prime shell. Gateway is the conditional
/// entry state. Settings is a global shell destination, not primary navigation.
/// </summary>
public enum PrimeRoute
{
    Gateway,
    Play,
    Hunter,
    Theatre,
    Rankings,
    Settings,
    // Kept as internal migration routes for persisted capture/test values.
    // Neither value is exposed by PrimeRouteInfo.Navigation.
    HunterLicense,
    Armory
}

public static class PrimeRouteInfo
{
    private static readonly IReadOnlyList<PrimeRoute> NavigationRoutes =
        new[]
        {
            PrimeRoute.Play,
            PrimeRoute.Hunter,
            PrimeRoute.Rankings,
            PrimeRoute.Theatre
        };

    private static readonly IReadOnlyList<PrimeRoute> AuthenticatedRoutes =
        NavigationRoutes.Concat(new[] { PrimeRoute.Settings }).ToArray();

    public static IReadOnlyList<PrimeRoute> Navigation => NavigationRoutes;

    public static IReadOnlyList<PrimeRoute> Authenticated => AuthenticatedRoutes;

    public static string Label(PrimeRoute route) => route switch
    {
        PrimeRoute.Gateway => "Gateway",
        PrimeRoute.Play => "Play",
        PrimeRoute.Hunter => "Hunter",
        PrimeRoute.HunterLicense => "Hunter",
        PrimeRoute.Armory => "Hunter",
        PrimeRoute.Theatre => "Theatre",
        PrimeRoute.Rankings => "Rankings",
        PrimeRoute.Settings => "Settings",
        _ => route.ToString()
    };

    public static string CompactLabel(PrimeRoute route) => route switch
    {
        PrimeRoute.Hunter => "Hunter",
        PrimeRoute.HunterLicense => "Hunter",
        PrimeRoute.Armory => "Hunter",
        PrimeRoute.Theatre => "Replays",
        PrimeRoute.Rankings => "Ranks",
        PrimeRoute.Settings => "Setup",
        _ => Label(route)
    };

    public static bool IsAuthenticated(PrimeRoute route)
        => route is PrimeRoute.Play or PrimeRoute.Hunter or PrimeRoute.HunterLicense
            or PrimeRoute.Armory or PrimeRoute.Theatre or PrimeRoute.Rankings
            or PrimeRoute.Settings;

    public static bool TryParse(string? value, out PrimeRoute route)
    {
        if (Enum.TryParse(value, ignoreCase: true, out route)
            && (route == PrimeRoute.Gateway || IsAuthenticated(route)))
        {
            route = PrimeRoutePresentation.Normalize(route);
            return true;
        }
        route = PrimeRoute.Gateway;
        return false;
    }
}
