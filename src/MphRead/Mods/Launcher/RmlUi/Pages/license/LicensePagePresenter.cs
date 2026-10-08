#if MPHREAD_RMLUI_POC
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.License;

/// <summary>One persistent document keeps account inputs across tab, sync and error transitions.</summary>
public sealed class LicensePagePresenter : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly LicenseController _controller;
    private RmlUiDocumentToken _document;
    private ulong _presentedVersion, _credentialsEpoch;
    private long _bindingVersion;
    private bool _disposed;
    public RmlUiDocumentToken Document => _document;
    public LicenseController Controller => _controller;
    public event Action? Closed;
    public event Action? CustomizationRequested;

    public LicensePagePresenter(RmlUiHost host, RmlUiPageManager pages, LicenseController controller)
    {
        (_host, _pages, _controller) = (host, pages, controller);
        _controller.CustomizationRequested += OnCustomization;
    }
    public void Open(bool loadProfile = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _document = _pages.OpenPage(new("license", "pages/license/license.rml", "license_overview"));
        if (loadProfile) _controller.Refresh();
        Refresh();
    }
    public bool Handle(in RmlUiIntent intent)
    {
        if (_disposed || intent.Kind != RmlUiIntentKind.LicenseAction || intent.Document != _document || !_pages.Accept(intent)) return false;
        if (intent.Argument is >= 0 and <= 8) _controller.SelectFace((LicenseFace)intent.Argument);
        else switch (intent.Argument)
        {
            case 9: case 19: _controller.Refresh(); break;
            case 10: _controller.CancelRefresh(); break;
            case 11: _controller.ChangeHistoryPage(-1); break;
            case 12: _controller.ChangeHistoryPage(1); break;
            case 13: Submit(LicenseAccountAction.SendVerification); break;
            case 14: Submit(LicenseAccountAction.FinishPassword); break;
            case 15: Submit(LicenseAccountAction.Recover); break;
            case 16: Submit(LicenseAccountAction.LinkGoogle); break;
            case 17: Submit(LicenseAccountAction.LinkGitHub); break;
            case 18: Submit(LicenseAccountAction.LinkDiscord); break;
            case 20: Closed?.Invoke(); break;
            default: return false;
        }
        Refresh(); return true;
    }
    private void Submit(LicenseAccountAction action)
    {
        // Credentials never travel through a binding snapshot or intent payload.
        if (_controller.SetCredentials(_host.ReadField(_document, "license_email"),
            _host.ReadField(_document, "license_password"), _host.ReadField(_document, "license_confirmation")).Accepted)
            _controller.PerformAccount(action);
    }
    public void Refresh()
    {
        if (_disposed || _document == default) return;
        if (!_host.IsAlive(_document) || _pages.Page != _document) { Dispose(); return; }
        _controller.Pump();
        LicenseViewSnapshot s = _controller.Snapshot;
        if (s.Version == _presentedVersion) return;
        if (s.CredentialsClearedEpoch != _credentialsEpoch)
        {
            _host.SetField(_document, "license_password", ""); _host.SetField(_document, "license_confirmation", "");
            _credentialsEpoch = s.CredentialsClearedEpoch;
        }
        var b = new Dictionary<string, RmlUiBindingValue>();
        LicenseProfile p = s.Data.Profile;
        Text(b, "license_player", p.DisplayName.Length == 0 ? "PLAYER" : p.DisplayName);
        Text(b, "license_id", HunterId(p.PlayerId));
        Text(b, "license_rank", s.Rank);
        Text(b, "license_favorite", p.FavoriteHunter is >= 0 and < 7 ? ((Hunter)p.FavoriteHunter).ToString() : $"Unknown Hunter ({p.FavoriteHunter})");
        Text(b, "license_created", p.CreatedAt?.ToLocalTime().ToString("MMM yyyy", CultureInfo.InvariantCulture) ?? "LOCAL PROFILE");
        Text(b, "license_status", s.Loading ? "SYNCING LICENSE…" : s.Data.Status);
        Text(b, "license_error", s.CommandError);
        Text(b, "license_security_status", s.SecurityBusy ? "Contacting account service…" : s.SecurityMessage);
        Text(b, "license_account_kind", s.Data.Account.IsSecure ? "SECURED LICENSE" : "GUEST LICENSE");
        Text(b, "license_account_email", s.Data.Account.Email.Length > 0 ? s.Data.Account.Email : "NO EMAIL LINKED");
        Text(b, "license_account_providers", s.Data.Account.Providers.Length > 0 ? String.Join(" / ", s.Data.Account.Providers).ToUpperInvariant() : "NONE");
        Text(b, "license_recent_form", s.RecentForm);
        Text(b, "license_metrics", String.Join("\n", s.CareerMetrics.Select(metric => metric.Label + "  //  " + metric.Value)));
        Text(b, "license_overview_metrics", String.Join("\n", s.CareerMetrics.Where(metric => metric.Label is "MATCHES" or "WINS" or "WIN RATE" or "K / D" or "PLAY TIME")
            .Select(metric => metric.Label + "  //  " + metric.Value)));
        Text(b, "license_comparison_metrics", String.Join("\n", s.Comparison.Select(metric => metric.Label + "  //  " + metric.Value)));
        Text(b, "license_comparison_reason", s.PublicComparisonUnavailableReason);
        Text(b, "license_emblems_reason", s.EmblemsUnavailableReason);
        Text(b, "license_titles_reason", s.TitlesUnavailableReason);
        Text(b, "license_cosmetic_count", s.Data.Cosmetics.Length + " cosmetic loadouts on this profile.");
        for (int face = 0; face < 9; face++)
        {
            Bool(b, "visible:license_face_" + face, face == (int)s.Face);
            Bool(b, "class:license_" + ((LicenseFace)face).ToString().ToLowerInvariant() + ":selected", face == (int)s.Face);
        }
        Text(b, "license_history_page", $"LATEST {s.Data.Matches.Length} ACCEPTED MATCHES // PAGE {s.HistoryPageIndex + 1} OF {s.HistoryPageCount}");
        Bool(b, "visible:license_history_empty", s.Data.Matches.IsEmpty);
        for (int row = 0; row < LicenseController.HistoryPageSize; row++)
        {
            Bool(b, "visible:license_match_" + row, row < s.History.Length);
            if (row >= s.History.Length) continue;
            LicenseMatch match = s.History[row];
            Text(b, "license_match_label_" + row, (match.Tied ? "TIE" : match.Won ? "WIN" : "LOSS")
                + " // " + RoomName(match.RoomKey) + " // " + ModeName(match.Mode));
            Text(b, "license_match_detail_" + row, $"{match.Kills} K / {match.Deaths} D / {match.Assists} A // {LicenseController.FormatTicks(match.PlayedTicks)}"
                + " // " + (match.CareerEligible && match.Eligible ? "CAREER" : "UNRANKED")
                + (match.RatingStatus.Length > 0 ? " // " + match.RatingStatus : "")
                + (match.PlayedAt.HasValue ? " // " + match.PlayedAt.Value.ToLocalTime().ToString("g", CultureInfo.InvariantCulture) : ""));
        }
        for (int i = 0; i < s.Achievements.Length; i++)
            Text(b, "license_achievement_" + i, s.Achievements[i].Name + " // " + s.Achievements[i].Detail
                + " // " + (s.Achievements[i].Earned ? "UNLOCKED" : "LOCKED"));
        Bool(b, "disabled:license_previous_page", s.HistoryPageIndex == 0);
        Bool(b, "disabled:license_next_page", s.HistoryPageIndex + 1 >= s.HistoryPageCount);
        Bool(b, "disabled:license_refresh", s.SecurityBusy);
        Bool(b, "disabled:license_cancel_refresh", !s.Loading || s.SecurityBusy);
        foreach (string id in new[] { "send_verification", "finish_password", "refresh_account" }) Bool(b, "disabled:license_" + id, s.SecurityBusy);
        Bool(b, "disabled:license_recover", !s.CanRecover);
        foreach (string provider in new[] { "google", "github", "discord" })
            Bool(b, "disabled:license_link_" + provider, s.SecurityBusy || s.Data.Account.Providers.Contains(provider, StringComparer.OrdinalIgnoreCase));
        Text(b, "license_recovery_reason", !s.Data.Connected ? "Sync this license before recovery so its career can be checked."
            : s.Data.Account.IsSecure ? "This license is already secured."
            : s.Data.Stats.GamesPlayed > 0 ? "This guest has career history. Secure it before switching identities."
            : "You can recover an existing secured license on this empty guest.");
        _pages.Present(_document, ++_bindingVersion, b);
        _presentedVersion = s.Version;
    }
    private void OnCustomization() => CustomizationRequested?.Invoke();
    public void Dispose()
    {
        if (_disposed) return;
        _host.VerifyOwnerThread();
        _controller.CustomizationRequested -= OnCustomization;
        _controller.Dispose();
        if (_pages.Page == _document && !_pages.ClosePage()) throw new InvalidOperationException("The Hunter License page could not close.");
        _document = default; _disposed = true;
    }
    private static void Text(Dictionary<string, RmlUiBindingValue> b, string id, string value) => b[id] = RmlUiBindingValue.FromText(value);
    private static void Bool(Dictionary<string, RmlUiBindingValue> b, string id, bool value) => b[id] = RmlUiBindingValue.FromBoolean(value);
    private static string HunterId(string id) { string compact = id.Replace("-", "", StringComparison.Ordinal); return compact.Length == 0 ? "#--------" : "#" + compact[..Math.Min(8, compact.Length)].ToUpperInvariant(); }
    private static string RoomName(string key) { try { return Metadata.GetRoomByName(key).Item1?.InGameName ?? key; } catch { return key.Length == 0 ? "UNKNOWN MAP" : key; } }
    private static string ModeName(int mode) => mode is >= 0 and <= 255 && Enum.IsDefined((GameMode)(byte)mode) ? ((GameMode)(byte)mode).ToString() : $"MODE {mode}";
}
#endif
