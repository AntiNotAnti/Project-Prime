#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;

namespace MphRead.Mods.Launcher.RmlUi.Pages.License;

/// <summary>Only publicly presentable local-career fields are projected.</summary>
internal sealed class NativeLicenseOverviewPage : IDisposable
{
    private readonly RmlUiPageManager _pages;
    private readonly NativeLicenseOverviewController _controller;
    private readonly RmlUiHost? _host;
    private RmlUiDocumentToken _page;
    private long _revision;
    private long _sourceRevision;
    private int _cardTab;
    private bool _cardDirty = true;
    private readonly RmlUiLicenseArtworkCache _portraits = new();
    private readonly RmlUiLicenseArtworkCache _stars = new(rankArtwork: true);
    private string _starImage = "";
    private string _portraitImage = "";
    private bool _disposed;
    private bool _overlay;
    public bool IsOverlay => _overlay;
    public bool Closed { get; private set; }

    public NativeLicenseOverviewPage(RmlUiPageManager pages, INativeLicenseOverviewReader reader, RmlUiHost? host = null)
    {
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _controller = new NativeLicenseOverviewController(reader);
        _host = host;
    }

    public void Open(bool overlay = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_page != default) return;
        _overlay = overlay;
        var spec = new RmlUiPageSpec("native-license",
            "pages/license/native-overview.rml", "native_license_tab_overview");
        _page = overlay ? _pages.OpenModal(spec) : _pages.OpenPage(spec);
        _controller.Refresh();
        Refresh();
    }

    public bool Handle(in RmlUiIntent intent)
    {
        if (_disposed || Closed || intent.Kind != RmlUiIntentKind.LicenseAction
            || _page != intent.Document || _pages.Top != _page
            || !_pages.Accept(intent)) return false;
        switch (intent.Argument)
        {
            case >= 25 and <= 29: _cardTab = intent.Argument - 25; _cardDirty = true; ClearCredentials(); break;
            case 0: _controller.SetFace(NativeLicenseFace.Overview); break;
            case 2: _controller.SetFace(NativeLicenseFace.Stats); break;
            case 3: _controller.SetFace(NativeLicenseFace.History); break;
            case 4: _controller.SetFace(NativeLicenseFace.Achievements); break;
            case 5: _controller.SetFace(NativeLicenseFace.Emblems); break;
            case 6: _controller.SetFace(NativeLicenseFace.Titles); break;
            case 7: _controller.SetFace(NativeLicenseFace.Comparison); break;
            case 9: _controller.Refresh(); break;
            case 10: _controller.Cancel(); break;
            case 11: _controller.Page(-1); break;
            case 12: _controller.Page(1); break;
            case 8: _controller.SetFace(NativeLicenseFace.Account); break;
            case 19: _controller.Refresh(); break;
            case 13: SubmitAccount(NativeLicenseAccountAction.SendVerification); break;
            case 14: SubmitAccount(NativeLicenseAccountAction.SetPassword); break;
            case 15: SubmitAccount(NativeLicenseAccountAction.Recover); break;
            case 16: SubmitAccount(NativeLicenseAccountAction.LinkGoogle); break;
            case 17: SubmitAccount(NativeLicenseAccountAction.LinkGithub); break;
            case 18: SubmitAccount(NativeLicenseAccountAction.LinkDiscord); break;
            case 20: Closed = true; break;
            case 21: _controller.HidePublicProfile(); break;
            case 22: _controller.RequestShowPublicProfile(); break;
            case 23: _controller.ConfirmShowPublicProfile(); break;
            case 24: _controller.CancelShowPublicProfile(); break;
            default: return false;
        }
        if (intent.Argument is <= 8 or 9 or 10 or 19 or 20 or 21 or 22 or 23 or 24) ClearCredentials();
        Refresh();
        return true;
    }

    public void Back()
    {
        if (_disposed || Closed) return;
        ClearCredentials();
        if (_controller.Current().ConfirmPublic) _controller.CancelShowPublicProfile();
        else Closed = true;
        Refresh();
    }

    private void SubmitAccount(NativeLicenseAccountAction action)
    {
        if (_host == null) return;
        string email = "", password = "", confirmation = "";
        try
        {
            if (action is NativeLicenseAccountAction.SendVerification or NativeLicenseAccountAction.Recover)
                email = _host.ReadField(_page, "native_license_email", 1024);
            if (action is NativeLicenseAccountAction.SetPassword or NativeLicenseAccountAction.Recover)
            {
                password = _host.ReadField(_page, "native_license_password", 512);
                confirmation = _host.ReadField(_page, "native_license_confirmation", 512);
            }
        }
        finally { ClearCredentials(); }
        _controller.SubmitAccount(action, email, password, confirmation);
    }

    private void ClearCredentials()
    {
        if (_host == null || _page == default) return;
        foreach (string id in new[] { "native_license_email", "native_license_password", "native_license_confirmation" })
            _host.SetField(_page, id, "");
    }

    public void Refresh()
    {
        if (_disposed || _page == default) return;
        if (_host != null && !_host.IsAlive(_page)) { Closed = true; return; }
        var state = _controller.Tick();
        string portrait = _portraits.Read(state.Overview.Verified ? state.Overview.FavoriteHunter : "");
        if (portrait != _portraitImage) { _portraitImage = portrait; _cardDirty = true; }
        int displayTier = NativeLicenseOverviewBindings.DisplayStarTier(state.Overview);
        string star = _stars.Read(displayTier > 0 ? displayTier.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
        if (star != _starImage) { _starImage = star; _cardDirty = true; }
        if (state.Revision <= _sourceRevision && !_cardDirty) return;
        if (!state.Overview.Verified && !state.Busy) ClearCredentials();
        var bindings = new Dictionary<string, RmlUiBindingValue>(NativeLicenseOverviewBindings.Create(state, _cardTab));
        bindings["image:native_license_portrait"] = RmlUiBindingValue.FromText(portrait);
        bindings["visible:native_license_portrait"] = RmlUiBindingValue.FromBoolean(portrait.Length != 0);
        bindings["visible:native_license_portrait_label"] = RmlUiBindingValue.FromBoolean(portrait.Length == 0);
        bindings["visible:native_license_star_images"] = RmlUiBindingValue.FromBoolean(displayTier > 0 && star.Length != 0);
        bindings["visible:native_license_stars_fallback"] = RmlUiBindingValue.FromBoolean(displayTier > 0 && star.Length == 0);
        for (int i = 1; i <= 5; i++)
            bindings["image:native_license_star" + i] = RmlUiBindingValue.FromText(star);
        _pages.Present(_page, ++_revision, bindings);
        _sourceRevision = state.Revision;
        _cardDirty = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        ClearCredentials();
        _disposed = true;
        _controller.Dispose();
        _portraits.Dispose();
        _stars.Dispose();
        if (_page != default && _overlay && _pages.Top == _page) _pages.CloseModal();
        else if (_page != default && !_overlay && _pages.Page == _page) _pages.ClosePage();
    }
}

/// <summary>
/// Existing authenticated Hunter License is the only credential source. Its
/// private account objects are never exposed to the native document.
/// </summary>
internal sealed class CurrentHunterLicenseOverviewReader : INativeLicenseOverviewReader, INativeLicensePrivacyWriter,
    INativeLicensePublicProfileWriter, INativeLicenseAccountWriter
{
    private readonly object _privacyGate = new();
    private long _lease;
    private Guid _account;
    private object? _accountWitness;
    // One captured visibility operation may overlap its own same-session
    // refresh. Its numeric lease still maps to the original private witness.
    private long _visibilityLease;
    private object? _visibilityWitness;
    private readonly Func<object?, bool> _isCurrentAccountLease;
    private readonly Func<CancellationToken, Task<HunterLicenseSnapshot>> _read;
    private readonly Func<Guid, CancellationToken, Task<bool>> _hide;
    private readonly Func<Guid, CancellationToken, Task<bool>> _show;
    private readonly Func<Guid, NativeLicenseAccountAction, string, string, CancellationToken, Task<bool>> _accountAction;

    public CurrentHunterLicenseOverviewReader()
        : this(HunterLicenseClient.LoadAsync, HunterLicenseClient.HidePublicProfileAsync,
            HunterLicenseClient.ShowPublicProfileAsync, HunterLicenseClient.ExecuteNativeAccountAsync,
            HunterLicenseClient.IsNativeAccountLeaseCurrent) { }

    internal CurrentHunterLicenseOverviewReader(
        Func<CancellationToken, Task<HunterLicenseSnapshot>> read,
        Func<Guid, CancellationToken, Task<bool>> hide,
        Func<Guid, CancellationToken, Task<bool>>? show = null,
        Func<Guid, NativeLicenseAccountAction, string, string, CancellationToken, Task<bool>>? accountAction = null,
        Func<object?, bool>? isCurrentAccountLease = null)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _hide = hide ?? throw new ArgumentNullException(nameof(hide));
        _show = show ?? ((_, _) => Task.FromResult(false));
        _accountAction = accountAction ?? ((_, _, _, _, _) => Task.FromResult(false));
        _isCurrentAccountLease = isCurrentAccountLease ?? (_ => true);
    }

    public Task<NativeLicenseOverview> LoadAsync(CancellationToken cancellation)
    {
        long lease;
        lock (_privacyGate)
        {
            // A cancelled older worker may be scheduled after a newer load.
            // It must not retire the newer load's privacy lease.
            cancellation.ThrowIfCancellationRequested();
            lease = ++_lease;
            _account = Guid.Empty;
            _accountWitness = null;
        }
        return Task.Run(async () =>
        {
            HunterLicenseSnapshot latest = await _read(cancellation)
                .ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            // A disconnected local fallback has no authenticated career authority.
            if (!latest.Connected || !_isCurrentAccountLease(latest.AccountWitness)) return default;
            var p = latest.Profile;
            var s = latest.Stats;
            int hunterId = p.FavoriteHunter;
            string hunter = hunterId is >= 0 and <= 6
                ? ((Hunter)hunterId).ToString().ToUpperInvariant() : "UNKNOWN";
            string visibility = latest.PublicVisible == true ? "PUBLIC"
                : latest.PublicVisible == false ? "PRIVATE" : "UNAVAILABLE";
            long writableLease = 0;
            lock (_privacyGate)
            {
                if (_lease == lease && Guid.TryParse(p.PlayerId, out Guid account)
                    && account != Guid.Empty && _isCurrentAccountLease(latest.AccountWitness))
                {
                    _account = account;
                    _accountWitness = latest.AccountWitness;
                    writableLease = lease;
                }
            }
            return new NativeLicenseOverview(
                latest.Connected, p.DisplayName, hunter, latest.Status, visibility,
                p.RatingPoints, p.RatingTier.HasValue
                    ? "TIER " + p.RatingTier.Value : "UNRANKED",
                s.GamesPlayed, s.Wins, s.Losses, s.Kills, s.Deaths,
                s.Assists, Math.Min(latest.Matches?.Count ?? 0, NativeLicenseOverviewController.HistoryLimit))
            {
                RatingTierNumber = p.RatingTier,
                PrivacyLease = writableLease,
                Account = new(latest.Account.IsSecure, latest.Account.Email.Length > 0,
                    latest.Account.Providers.Contains("google", StringComparer.OrdinalIgnoreCase),
                    latest.Account.Providers.Contains("github", StringComparer.OrdinalIgnoreCase),
                    latest.Account.Providers.Contains("discord", StringComparer.OrdinalIgnoreCase)),
                Career = new(s.Ties, s.Damage, s.PlayedTicks, s.Headshots,
                    s.LongestKillStreak, s.CurrentWinStreak, s.LongestWinStreak,
                    s.OctolithScores, s.NodesCaptured, s.KillsAsPrime),
                History = (latest.Matches ?? new()).Take(NativeLicenseOverviewController.HistoryLimit)
                    .Select(m => new NativeLicenseMatch(RoomName(m.RoomKey), ModeName(m.Mode),
                        m.PlayedAt, m.Won, m.Tied, m.Eligible, m.CareerEligible,
                        m.Kills, m.Deaths, m.Assists, m.Damage, m.PlayedTicks)).ToArray()
            };
        }, cancellation);
    }

    public Task<bool> HidePublicProfileAsync(long privacyLease, CancellationToken cancellationToken)
        => WriteWithLease(privacyLease, id => _hide(id, cancellationToken), cancellationToken);

    public Task<bool> ShowPublicProfileAsync(long privacyLease, CancellationToken cancellationToken)
        => WriteWithLease(privacyLease, id => _show(id, cancellationToken), cancellationToken);

    public Task<bool> ExecuteAccountAsync(long lease, NativeLicenseAccountAction action,
        string email, string password, CancellationToken cancellationToken)
        => WriteWithLease(lease, id => _accountAction(id, action, email, password, cancellationToken), cancellationToken,
            validateAfterWrite: false);

    public bool IsCurrent(NativeLicenseOverview overview)
    {
        lock (_privacyGate)
            return overview.Verified && overview.PrivacyLease > 0
                && (overview.PrivacyLease == _lease && _account != Guid.Empty && _isCurrentAccountLease(_accountWitness)
                    || overview.PrivacyLease == _visibilityLease && _isCurrentAccountLease(_visibilityWitness));
    }

    private Task<bool> WriteWithLease(long lease, Func<Guid, Task<bool>> write, CancellationToken cancellation,
        bool validateAfterWrite = true)
    {
        cancellation.ThrowIfCancellationRequested();
        Guid expectedAccount;
        object? expectedWitness;
        lock (_privacyGate)
        {
            if (lease <= 0 || lease != _lease || _account == Guid.Empty
                || !_isCurrentAccountLease(_accountWitness))
                return Task.FromResult(false);
            expectedAccount = _account;
            expectedWitness = _accountWitness;
            if (validateAfterWrite)
            {
                _visibilityLease = lease;
                _visibilityWitness = expectedWitness;
            }
        }
        return Complete();

        async Task<bool> Complete()
        {
            bool result = await write(expectedAccount).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            // Visibility writes must still belong to the captured session after
            // the existing server readback. Account recovery deliberately changes
            // sessions and instead publishes only a fresh, separately leased load.
            return result && (!validateAfterWrite || _isCurrentAccountLease(expectedWitness));
        }
    }

    private static string RoomName(string key)
    {
        if (String.IsNullOrWhiteSpace(key)) return "UNKNOWN MAP";
        return LauncherRoomDisplayName.Get(key);
    }

    private static string ModeName(int mode)
        => mode is >= byte.MinValue and <= byte.MaxValue && Enum.IsDefined((GameMode)(byte)mode)
            ? ((GameMode)(byte)mode).ToString() : "MODE " + mode;
}
#endif
