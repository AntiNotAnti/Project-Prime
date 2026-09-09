using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Routes owned by the native Project Prime shell. Gateway is the conditional
/// entry state; the persistent header contains the six product destinations.
/// </summary>
public enum PrimeRoute
{
    Gateway,
    Play,
    HunterLicense,
    Armory,
    Theater,
    Rankings,
    Settings
}

public static class PrimeRouteInfo
{
    private static readonly IReadOnlyList<PrimeRoute> AuthenticatedRoutes =
        new[]
        {
            PrimeRoute.Play,
            PrimeRoute.HunterLicense,
            PrimeRoute.Armory,
            PrimeRoute.Theater,
            PrimeRoute.Rankings,
            PrimeRoute.Settings
        };

    public static IReadOnlyList<PrimeRoute> Navigation => AuthenticatedRoutes;

    public static IReadOnlyList<PrimeRoute> Authenticated => AuthenticatedRoutes;

    public static string Label(PrimeRoute route) => route switch
    {
        PrimeRoute.Gateway => "Gateway",
        PrimeRoute.Play => "Play",
        PrimeRoute.HunterLicense => "Hunter License",
        PrimeRoute.Armory => "Armory",
        PrimeRoute.Theater => "Theater",
        PrimeRoute.Rankings => "Rankings",
        PrimeRoute.Settings => "Settings",
        _ => route.ToString()
    };

    public static string CompactLabel(PrimeRoute route) => route switch
    {
        PrimeRoute.HunterLicense => "License",
        PrimeRoute.Armory => "Arms",
        PrimeRoute.Theater => "Replays",
        PrimeRoute.Rankings => "Ranks",
        PrimeRoute.Settings => "Setup",
        _ => Label(route)
    };

    public static bool IsAuthenticated(PrimeRoute route)
        => route is PrimeRoute.Play or PrimeRoute.HunterLicense or PrimeRoute.Armory
            or PrimeRoute.Theater or PrimeRoute.Rankings or PrimeRoute.Settings;

    public static bool TryParse(string? value, out PrimeRoute route)
    {
        if (Enum.TryParse(value, ignoreCase: true, out route)
            && (route == PrimeRoute.Gateway || IsAuthenticated(route)))
        {
            return true;
        }
        route = PrimeRoute.Gateway;
        return false;
    }
}
