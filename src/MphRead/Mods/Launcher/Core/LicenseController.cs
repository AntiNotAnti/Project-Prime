using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Cancellable account/read-model owner. Workers enqueue copied data; only Pump publishes it.</summary>
public sealed class LicenseController : IDisposable
{
    public const int HistoryPageSize = 8;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Guid _lifetime = Guid.NewGuid();
    private readonly ILicenseBackend _backend;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _loadCancellation;
    private readonly ConcurrentQueue<(int Generation, LicenseData? Data, LicenseActionResult? Action, LicenseAccountAction? Kind)> _completed = new();
    private LicenseData _data;
    private LicenseViewSnapshot _snapshot = new();
    private LicenseFace _face;
    private bool _loading, _securityBusy, _disposed;
    private int _generation, _historyPage;
    private ulong _version, _credentialsClearedEpoch;
    private string _email = "", _password = "", _confirmation = "", _securityMessage = "", _error = "";
    public event Action? CustomizationRequested;

    public LicenseController(ILicenseBackend? backend = null)
    {
        _backend = backend ?? new LauncherLicenseBackend();
        _data = Normalize(_backend.LocalSnapshot());
        RefreshSnapshot();
    }
    public LicenseViewSnapshot Snapshot { get { EnsureOwner(); return _snapshot; } }

    public void Pump()
    {
        EnsureOwner();
        if (_disposed) return;
        while (_completed.TryDequeue(out var completion))
        {
            if (completion.Generation != _generation) continue;
            if (completion.Data is { } data)
            {
                _loading = false;
                data = Normalize(data);
                // Preserve the last accepted career on a transient outage instead of displaying invented zeroes.
                _data = !data.Connected && _data.Profile.PlayerId.Length > 0
                    ? _data with { Connected = false, Status = data.Status } : data;
                _historyPage = Math.Min(_historyPage, PageCount - 1);
            }
            if (completion.Action is { } action)
            {
                _securityBusy = false; _securityMessage = action.Message;
                _error = action.Success ? "" : action.Message;
                if (action.Success && completion.Kind is LicenseAccountAction.FinishPassword or LicenseAccountAction.Recover)
                {
                    _password = _confirmation = ""; _credentialsClearedEpoch++;
                    Refresh();
                }
            }
        }
        RefreshSnapshot();
    }

    public LobbyActionResult SelectFace(LicenseFace face) => Action(() =>
    {
        if (!Enum.IsDefined(face)) return Reject("Unknown Hunter License section.");
        _face = face;
        if (face == LicenseFace.Customization) CustomizationRequested?.Invoke();
        return LobbyActionResult.Ok;
    });
    public LobbyActionResult ChangeHistoryPage(int delta) => Action(() =>
    {
        int next = _historyPage + delta;
        if (delta is not (-1 or 1) || next < 0 || next >= PageCount) return Reject("No more matches on this page.");
        _historyPage = next; return LobbyActionResult.Ok;
    });

    public LobbyActionResult Refresh() => Action(() =>
    {
        if (_securityBusy) return Reject("Wait for the current account action to finish.");
        CancelLoad();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _loading = true;
        int generation = _generation;
        _ = CompleteLoadAsync(generation, _loadCancellation.Token);
        return LobbyActionResult.Ok;
    });
    public LobbyActionResult CancelRefresh() => Action(() =>
    {
        if (_securityBusy) return Reject("Wait for the current account action to finish.");
        CancelLoad(); _securityMessage = "Profile load cancelled. Your last record is retained."; return LobbyActionResult.Ok;
    });

    /// <summary>Entered values stay private and independent; confirmation is never copied from password.</summary>
    public LobbyActionResult SetCredentials(string email, string password, string confirmation) => Action(() =>
    {
        if (email.Length > 254 || password.Length > 128 || confirmation.Length > 128)
            return Reject("Email and password exceed the existing account input limits.");
        _email = email.Trim(); _password = password; _confirmation = confirmation;
        return LobbyActionResult.Ok;
    });

    public LobbyActionResult PerformAccount(LicenseAccountAction kind) => Action(() =>
    {
        if (_securityBusy) return Reject("Wait for the current account action to finish.");
        if (!Enum.IsDefined(kind)) return Reject("Unknown account action.");
        if (kind is LicenseAccountAction.SendVerification or LicenseAccountAction.Recover)
            if (!LooksLikeEmail(_email)) return Reject("Enter a valid email address.");
        if (kind is LicenseAccountAction.FinishPassword or LicenseAccountAction.Recover)
        {
            if (_password != _confirmation) return Reject("Both password fields must match.");
            if (_password.Length < (kind == LicenseAccountAction.FinishPassword ? 8 : 1))
                return Reject(kind == LicenseAccountAction.FinishPassword ? "Use at least 8 characters for the password." : "Enter the license password.");
        }
        if (kind == LicenseAccountAction.Recover && !CanRecover)
            return Reject(!_data.Connected ? "Sync this license before recovering an existing account."
                : _data.Stats.GamesPlayed > 0 ? "This guest has career history. Secure it before switching identities."
                : "This license is already secured.");
        string provider = kind switch { LicenseAccountAction.LinkGoogle => "google", LicenseAccountAction.LinkGitHub => "github",
            LicenseAccountAction.LinkDiscord => "discord", _ => "" };
        if (provider.Length > 0 && _data.Account.Providers.Contains(provider, StringComparer.OrdinalIgnoreCase))
            return Reject("That sign-in provider is already linked.");
        CancelLoad(); _securityBusy = true; _securityMessage = "Contacting account service…";
        int generation = _generation;
        _ = CompleteAccountAsync(generation, kind, _email, _password, _lifetimeCancellation.Token);
        return LobbyActionResult.Ok;
    });

    private async Task CompleteLoadAsync(int generation, CancellationToken token)
    {
        LicenseData data;
        try { data = await _backend.LoadAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        catch (Exception) { data = new() { Status = "OFFLINE // PROFILE LOAD FAILED; RETRY SYNC" }; }
        if (!token.IsCancellationRequested) _completed.Enqueue((generation, data, null, null));
    }
    private async Task CompleteAccountAsync(int generation, LicenseAccountAction kind, string email, string password, CancellationToken token)
    {
        LicenseActionResult result;
        try { result = await _backend.AccountAsync(kind, email, password, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        catch (Exception) { result = new(false, "The account service could not complete this action. Retry when connected."); }
        if (!token.IsCancellationRequested) _completed.Enqueue((generation, null, result, kind));
    }

    private int PageCount => Math.Max(1, (_data.Matches.Length + HistoryPageSize - 1) / HistoryPageSize);
    private bool CanRecover => _data.Connected && !_data.Account.IsSecure && _data.Stats.GamesPlayed == 0 && !_securityBusy;
    private void CancelLoad()
    { _generation++; _loading = false; _loadCancellation?.Cancel(); _loadCancellation?.Dispose(); _loadCancellation = null; }
    private LobbyActionResult Action(Func<LobbyActionResult> action)
    {
        EnsureOwner();
        if (_disposed) return Reject("This Hunter License has closed.");
        LobbyActionResult result = action(); _error = result.Accepted ? "" : result.Message;
        RefreshSnapshot(); return result;
    }
    private static LobbyActionResult Reject(string message) => LobbyActionResult.Reject(message);
    private static bool LooksLikeEmail(string value)
    { int at = value.IndexOf('@'); return at > 0 && at < value.Length - 3 && value.IndexOf('.', at + 2) > at + 1 && value.Length <= 254; }
    private static LicenseData Normalize(LicenseData data) => data with
    {
        Matches = data.Matches.IsDefault ? ImmutableArray<LicenseMatch>.Empty : data.Matches.Take(25).ToImmutableArray(),
        Cosmetics = data.Cosmetics.IsDefault ? ImmutableArray<LicenseCosmetic>.Empty : data.Cosmetics,
        Account = data.Account with { Providers = data.Account.Providers.IsDefault ? ImmutableArray<string>.Empty : data.Account.Providers }
    };
    private void RefreshSnapshot()
    {
        LicenseStats s = _data.Stats;
        var metrics = ImmutableArray.Create(
            new LicenseMetric("MATCHES", s.GamesPlayed.ToString("N0")), new("WINS", s.Wins.ToString("N0")),
            new("LOSSES", s.Losses.ToString("N0")), new("TIES", s.Ties.ToString("N0")), new("WIN RATE", Percent(s.Wins, s.GamesPlayed)),
            new("KILLS", s.Kills.ToString("N0")), new("DEATHS", s.Deaths.ToString("N0")), new("ASSISTS", s.Assists.ToString("N0")),
            new("HEADSHOTS", s.Headshots.ToString("N0")), new("K / D", Ratio(s.Kills, s.Deaths)), new("DAMAGE", s.Damage.ToString("N0")),
            new("BEST KILL STREAK", s.LongestKillStreak.ToString("N0")), new("CURRENT WIN STREAK", s.CurrentWinStreak.ToString("N0")),
            new("LONGEST WIN STREAK", s.LongestWinStreak.ToString("N0")), new("OCTOLITH SCORES", s.OctolithScores.ToString("N0")),
            new("NODES CAPTURED", s.NodesCaptured.ToString("N0")), new("KILLS AS PRIME", s.KillsAsPrime.ToString("N0")),
            new("PLAY TIME", FormatTicks(s.PlayedTicks)), new("AVG / MATCH", s.GamesPlayed == 0 ? "0m" : FormatTicks(s.PlayedTicks / s.GamesPlayed)));
        double games = Math.Max(1, s.GamesPlayed);
        var comparison = ImmutableArray.Create(new LicenseMetric("KILLS / MATCH", (s.Kills / games).ToString("0.00", CultureInfo.InvariantCulture)),
            new("DEATHS / MATCH", (s.Deaths / games).ToString("0.00", CultureInfo.InvariantCulture)),
            new("ASSISTS / MATCH", (s.Assists / games).ToString("0.00", CultureInfo.InvariantCulture)),
            new("DAMAGE / MATCH", (s.Damage / games).ToString("N0", CultureInfo.InvariantCulture)), new("WIN RATE", Percent(s.Wins, s.GamesPlayed)));
        var achievements = ImmutableArray.Create(new LicenseAchievement("FIRST HUNT", "Complete 1 career match", s.GamesPlayed >= 1),
            new("FIRST VICTORY", "Win 1 career match", s.Wins >= 1), new("CENTURION", "Reach 100 career kills", s.Kills >= 100),
            new("HEADHUNTER", "Land 50 career headshot kills", s.Headshots >= 50), new("ACE", "Win 25 career matches", s.Wins >= 25),
            new("VETERAN", "Complete 50 career matches", s.GamesPlayed >= 50), new("LEGACY", "Complete 250 career matches", s.GamesPlayed >= 250));
        var recent = _data.Matches.Take(5).ToArray();
        LicenseViewSnapshot next = new()
        {
            Lifetime = _lifetime, Version = _version, Face = _face, Data = _data, Loading = _loading, SecurityBusy = _securityBusy,
            SecurityMessage = _securityMessage, CommandError = _error, HasPasswordInput = _password.Length > 0,
            CanRecover = CanRecover, CredentialsClearedEpoch = _credentialsClearedEpoch, HistoryPageIndex = _historyPage,
            HistoryPageCount = PageCount, History = _data.Matches.Skip(_historyPage * HistoryPageSize).Take(HistoryPageSize).ToImmutableArray(),
            CareerMetrics = metrics, Comparison = comparison, Achievements = achievements,
            Rank = _data.Profile.RatingTier.HasValue ? $"TIER {_data.Profile.RatingTier} // {_data.Profile.RatingPoints:N0} RP"
                : _data.Profile.RatingPoints > 0 ? $"{_data.Profile.RatingPoints:N0} RP" : "UNRANKED",
            RecentForm = recent.Length == 0 ? "No accepted matches yet." : String.Join(" / ", recent.Select(match => match.Tied ? "T" : match.Won ? "W" : "L"))
                + $" // {Ratio(recent.Sum(match => match.Kills), recent.Sum(match => match.Deaths))} K/D // {recent.Sum(match => match.Assists)} A // {recent.Sum(match => match.Damage):N0} DMG"
        };
        bool unchanged = next.History.SequenceEqual(_snapshot.History) && next.CareerMetrics.SequenceEqual(_snapshot.CareerMetrics)
            && next.Comparison.SequenceEqual(_snapshot.Comparison) && next.Achievements.SequenceEqual(_snapshot.Achievements)
            && (next with { History = _snapshot.History, CareerMetrics = _snapshot.CareerMetrics,
                Comparison = _snapshot.Comparison, Achievements = _snapshot.Achievements }) == _snapshot;
        if (!unchanged) _snapshot = next with { Version = ++_version };
    }
    public static string Ratio(long kills, long deaths) => deaths == 0 ? kills == 0 ? "0.00" : "∞" : ((double)kills / deaths).ToString("0.00", CultureInfo.InvariantCulture);
    public static string Percent(long value, long total) => total == 0 ? "0.0%" : ((double)value / total).ToString("P1", CultureInfo.InvariantCulture);
    public static string FormatTicks(long ticks)
    { long seconds = Math.Max(0, ticks) / 60; return seconds >= 3600 ? $"{seconds / 3600}h {seconds / 60 % 60:00}m" : $"{seconds / 60}m {seconds % 60:00}s"; }
    private void EnsureOwner()
    { if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("Hunter License must be accessed on its owner thread."); }
    public void Dispose()
    {
        EnsureOwner(); if (_disposed) return;
        _disposed = true; CancelLoad(); _lifetimeCancellation.Cancel(); _lifetimeCancellation.Dispose();
        _email = _password = _confirmation = ""; _securityBusy = false;
        while (_completed.TryDequeue(out _)) { }
        CustomizationRequested = null;
        _snapshot = _snapshot with { Loading = false, SecurityBusy = false, CanRecover = false, HasPasswordInput = false };
    }
}
