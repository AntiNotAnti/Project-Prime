using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Presentation;

namespace MphRead.Mods.Launcher.Gui;

public enum HunterSection
{
    Overview,
    Hunters,
    Arsenal,
    Career,
    Matches
}

public enum PrimeShellBreakpoint
{
    Mobile,
    Compact,
    Wide
}

/// <summary>
/// Pure route and breakpoint policy shared by the shell and capture tests.
/// Player-facing labels live here so responsive layouts cannot regress to
/// cryptic abbreviations.
/// </summary>
public static class PrimeRoutePresentation
{
    public static string PlayerFacingNetworkError(string? message, string fallback)
    {
        string value = message?.Trim() ?? "";
        if (value.Length == 0) return fallback;
        return value.Contains("Node", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Worker", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Backend", StringComparison.OrdinalIgnoreCase)
            || value.Contains("handoff", StringComparison.OrdinalIgnoreCase)
            || value.Contains("exception", StringComparison.OrdinalIgnoreCase)
            || value.Contains("stack trace", StringComparison.OrdinalIgnoreCase)
            || value.Contains("password", StringComparison.OrdinalIgnoreCase)
            || value.Contains("token", StringComparison.OrdinalIgnoreCase)
            || value.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || value.Contains("http://", StringComparison.OrdinalIgnoreCase)
            || value.Contains("https://", StringComparison.OrdinalIgnoreCase)
                ? fallback : value;
    }

    public const double MobileUpperBound = 720;
    public const double WideLowerBound = 1000;

    public static IReadOnlyList<PrimeRoute> MobilePrimary { get; } =
        new[] { PrimeRoute.Play, PrimeRoute.Hunter, PrimeRoute.Rankings };

    public static IReadOnlyList<string> MoreItems { get; } =
        new[] { "Maps", "Theatre", "Settings", "Account", "Connection", "About" };

    public static PrimeShellBreakpoint Breakpoint(double width)
    {
        if (Double.IsNaN(width) || Double.IsInfinity(width) || width < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (width < MobileUpperBound) return PrimeShellBreakpoint.Mobile;
        return width < WideLowerBound ? PrimeShellBreakpoint.Compact : PrimeShellBreakpoint.Wide;
    }

    public static string NavigationLabel(PrimeRoute route, PrimeShellBreakpoint breakpoint)
        => (route, breakpoint) switch
        {
            (PrimeRoute.Rankings, PrimeShellBreakpoint.Compact or PrimeShellBreakpoint.Mobile)
                => "Ranks",
            (PrimeRoute.Theatre, PrimeShellBreakpoint.Compact) => "Replays",
            _ => PrimeRouteInfo.Label(route)
        };

    public static PrimeRoute Normalize(PrimeRoute route)
        => route is PrimeRoute.HunterLicense or PrimeRoute.Armory
            ? PrimeRoute.Hunter : route;

    public static string GatewaySummary(GatewayPhase phase, string? detail)
    {
        string value = detail?.Trim() ?? "";
        if ((phase is GatewayPhase.Gateway or GatewayPhase.Failed)
            && value.Contains("saved session", StringComparison.OrdinalIgnoreCase))
        {
            bool invalid = value.Contains("no longer valid",
                StringComparison.OrdinalIgnoreCase);
            return invalid
                ? "Your saved session is no longer valid. Sign in or continue as a guest."
                : "No saved session. Sign in or continue as a guest.";
        }
        return phase switch
        {
            GatewayPhase.SigningIn => "Signing in…",
            GatewayPhase.Registering => "Creating your account…",
            GatewayPhase.Confirming => "Confirming your email…",
            GatewayPhase.Failed => "Could not complete that request. Check your details and try again.",
            _ => value.Length == 0 ? "Choose how you want to play." : value
        };
    }

    public static string GatewayDetails(string? detail)
    {
        string value = String.Join(' ', (detail ?? "").Split(
            ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (value.Length == 0)
            return "No additional details were provided.";
        string[] sensitiveOrTechnical =
        {
            "exception", "password", "token", "authorization", "http://", "https://"
        };
        foreach (string marker in sensitiveOrTechnical)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return "Technical details were omitted. See diagnostic logs.";
        }
        return value.Length <= 240 ? value : value[..240] + "…";
    }
}

/// <summary>
/// Bounded presentation state that survives route tree rebuilds. Service and
/// controller data remain in their existing owners; this stores only view
/// selection and scroll position.
/// </summary>
public sealed class PrimeRouteViewState
{
    private readonly Dictionary<PrimeRoute, Vector> _scrollOffsets = new();
    private bool _guestHunterEntryPrepared;

    public HunterSection HunterSection { get; private set; } = HunterSection.Overview;

    public void SelectHunterSection(HunterSection section) => HunterSection = section;

    /// <summary>
    /// Guests enter on locally available Arsenal data. Once they explicitly
    /// choose another Hunter tab, route rebuilds preserve that choice.
    /// </summary>
    public HunterSection PrepareHunterEntry(bool signedIn)
    {
        if (!signedIn && !_guestHunterEntryPrepared)
        {
            HunterSection = HunterSection.Arsenal;
            _guestHunterEntryPrepared = true;
        }
        return HunterSection;
    }

    public void ResetGuestHunterEntry() => _guestHunterEntryPrepared = false;

    public void CaptureScroll(PrimeRoute route, Vector offset)
    {
        if (!IsFinite(offset.X) || !IsFinite(offset.Y) || offset.X < 0 || offset.Y < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        _scrollOffsets[PrimeRoutePresentation.Normalize(route)] = offset;
    }

    public Vector ScrollFor(PrimeRoute route)
        => _scrollOffsets.GetValueOrDefault(PrimeRoutePresentation.Normalize(route));

    private static bool IsFinite(double value)
        => !Double.IsNaN(value) && !Double.IsInfinity(value);
}

public sealed record PrimeMatchHistoryPresentation(string Outcome, string Mission,
    string CombatLine, string RatingLine, string DateLine)
{
    public static PrimeMatchHistoryPresentation From(MatchHistoryEntry match)
    {
        ArgumentNullException.ThrowIfNull(match);
        string rating = match.RatingStatus switch
        {
            "applied" => "Official match",
            "ineligible" => "Not rating eligible",
            _ when String.IsNullOrWhiteSpace(match.RatingStatus)
                => match.Eligible ? "Official match" : "Not rating eligible",
            _ => match.RatingStatus
        };
        return new PrimeMatchHistoryPresentation(
            PrimeGameText.OutcomeLabel(match.Outcome).ToUpperInvariant(),
            $"{PrimeGameText.MapName(match.RoomKey)} · {PrimeGameText.ModeLabel(match.Mode)}",
            $"{match.Kills} K / {match.Deaths} D / {match.Assists} A · {match.Damage} damage",
            rating,
            match.EndedAt.ToString("MMM d, yyyy", CultureInfo.InvariantCulture));
    }
}

public sealed record PrimeReplayPresentation(string Title, string FileName,
    string RecordedLine, string Size)
{
    public static PrimeReplayPresentation From(PrimeReplayEntry replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        return new PrimeReplayPresentation(
            String.IsNullOrWhiteSpace(replay.Room) ? replay.FileName : replay.Room,
            replay.FileName,
            replay.Recorded.ToString("MMM d, yyyy · HH:mm", CultureInfo.InvariantCulture),
            replay.Size);
    }
}
