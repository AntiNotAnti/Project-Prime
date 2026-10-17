#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.License;

/// <summary>Only publicly presentable local-career fields are projected.</summary>
internal sealed class NativeLicenseOverviewPage : IDisposable
{
    private readonly RmlUiPageManager _pages;
    private readonly NativeLicenseOverviewController _controller;
    private RmlUiDocumentToken _page;
    private long _revision;
    private bool _disposed;
    public bool Closed { get; private set; }
    public bool OpenFullLicenseRequested { get; private set; }

    public NativeLicenseOverviewPage(RmlUiPageManager pages, INativeLicenseOverviewReader reader)
    {
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _controller = new NativeLicenseOverviewController(reader);
    }

    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _page = _pages.OpenPage(new("native-license",
            "pages/license/native-overview.rml", "native_license_refresh"));
        _controller.Refresh();
        Refresh();
    }

    public bool Handle(in RmlUiIntent intent)
    {
        if (_disposed || intent.Kind != RmlUiIntentKind.LicenseAction
            || _page != intent.Document || _pages.Top != _page
            || !_pages.Accept(intent)) return false;
        switch (intent.Argument)
        {
            case 9: _controller.Refresh(); break;
            case 8: OpenFullLicenseRequested = true; break;
            case 20: Closed = true; break;
            default: return false;
        }
        Refresh();
        return true;
    }

    public void Refresh()
    {
        if (_disposed || _page == default) return;
        var state = _controller.Tick();
        if (state.Revision <= _revision) return;
        NativeLicenseOverview o = state.Overview;
        string verified(long amount) => o.Verified ? amount.ToString("N0") : "NOT VERIFIED";
        var data = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal)
        {
            ["native_license_status"] = RmlUiBindingValue.FromText(state.Status),
            ["native_license_name"] = RmlUiBindingValue.FromText(
                String.IsNullOrWhiteSpace(o.DisplayName) ? "LOCAL HUNTER" : o.DisplayName),
            ["native_license_hunter"] = RmlUiBindingValue.FromText(
                "FAVORITE HUNTER // " + (o.FavoriteHunter ?? "UNKNOWN")),
            ["native_license_visibility"] = RmlUiBindingValue.FromText(
                "PUBLIC VISIBILITY // " + (o.Verified ? o.PublicVisibility : "UNKNOWN")),
            ["native_license_rank"] = RmlUiBindingValue.FromText(o.Verified
                ? "RANK // " + o.RatingTier + " // " + o.RatingPoints.ToString("N0") + " POINTS"
                : "RANK // UNVERIFIED"),
            ["native_license_matches"] = RmlUiBindingValue.FromText(verified(o.MatchesPlayed)),
            ["native_license_wins"] = RmlUiBindingValue.FromText(verified(o.Wins)),
            ["native_license_losses"] = RmlUiBindingValue.FromText(verified(o.Losses)),
            ["native_license_kills"] = RmlUiBindingValue.FromText(verified(o.Kills)),
            ["native_license_deaths"] = RmlUiBindingValue.FromText(verified(o.Deaths)),
            ["native_license_assists"] = RmlUiBindingValue.FromText(verified(o.Assists)),
            ["native_license_recent"] = RmlUiBindingValue.FromText(o.Verified
                ? "RECENT MATCHES // " + o.RecentMatches
                : "RECENT MATCHES // NOT VERIFIED"),
            ["disabled:native_license_refresh"] = RmlUiBindingValue.FromBoolean(state.Busy)
        };
        _pages.Present(_page, state.Revision, data);
        _revision = state.Revision;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.Dispose();
        if (_page != default && _pages.Page == _page) _pages.ClosePage();
    }
}

/// <summary>
/// Existing authenticated Hunter License is the only credential source. Its
/// private account objects are never exposed to the native document.
/// </summary>
internal sealed class CurrentHunterLicenseOverviewReader : INativeLicenseOverviewReader
{
    public Task<NativeLicenseOverview> LoadAsync(CancellationToken cancellation)
        => Task.Run(async () =>
        {
            HunterLicenseSnapshot latest = await HunterLicenseClient.LoadAsync(cancellation)
                .ConfigureAwait(false);
            var p = latest.Profile;
            var s = latest.Stats;
            int hunterId = p.FavoriteHunter;
            string hunter = hunterId is >= 0 and <= 6
                ? ((Hunter)hunterId).ToString().ToUpperInvariant() : "UNKNOWN";
            string visibility = latest.PublicVisible == true ? "PUBLIC"
                : latest.PublicVisible == false ? "PRIVATE" : "UNAVAILABLE";
            return new NativeLicenseOverview(
                latest.Connected, p.DisplayName, hunter, latest.Status, visibility,
                p.RatingPoints, p.RatingTier.HasValue
                    ? "TIER " + p.RatingTier.Value : "UNRANKED",
                s.GamesPlayed, s.Wins, s.Losses, s.Kills, s.Deaths,
                s.Assists, latest.Matches?.Count ?? 0);
        }, cancellation);
}
#endif
