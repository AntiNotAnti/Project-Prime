using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

/// <summary>Never contains account tokens, emails, UUIDs or credentials.</summary>
public readonly record struct NativeLicenseOverview(
    bool Verified, string DisplayName, string FavoriteHunter, string Status,
    string PublicVisibility, int RatingPoints, string RatingTier,
    long MatchesPlayed, long Wins, long Losses, long Kills, long Deaths,
    long Assists, int RecentMatches)
{
    // Nullable server tier distinguishes an unrated license from a rated license at zero points.
    public int? RatingTierNumber { get; init; }
    public NativeLicenseCareerStats? Career { get; init; }
    public IReadOnlyList<NativeLicenseMatch> History { get; init; } = Array.Empty<NativeLicenseMatch>();
    // Opaque, process-local adapter lease. Contains no account identifier and
    // is never projected into RML or accepted as a UI-supplied action argument.
    public long PrivacyLease { get; init; }
    public NativeLicenseAccountInfo? Account { get; init; }
}

public sealed record NativeLicenseAccountInfo(bool Secure, bool EmailLinked,
    bool GoogleLinked, bool GithubLinked, bool DiscordLinked);

public enum NativeLicenseAccountAction { SendVerification, SetPassword, Recover, LinkGoogle, LinkGithub, LinkDiscord }

public interface INativeLicenseAccountWriter
{
    Task<bool> ExecuteAccountAsync(long lease, NativeLicenseAccountAction action,
        string email, string password, CancellationToken cancellationToken);
}

public sealed record NativeLicenseCareerStats(long Ties, long Damage, long PlayedTicks,
    long Headshots, long LongestKillStreak, long CurrentWinStreak, long LongestWinStreak,
    long OctolithScores, long NodesCaptured, long KillsAsPrime);

/// <summary>Display-only match facts. No account, match UUID or credential is projected.</summary>
public sealed record NativeLicenseMatch(string Room, string Mode, DateTimeOffset? PlayedAt,
    bool Won, bool Tied, bool Eligible, bool CareerEligible, long Kills, long Deaths,
    long Assists, long Damage, long PlayedTicks);

public enum NativeLicenseFace { Overview, Stats, History, Achievements, Comparison, Account, Emblems, Titles }

public interface INativeLicenseOverviewReader
{
    bool IsCurrent(NativeLicenseOverview overview) => true;
    Task<NativeLicenseOverview> LoadAsync(CancellationToken cancellationToken);
}

public interface INativeLicensePrivacyWriter
{
    /// <summary>Confirm only after same-account private write and server reread.</summary>
    Task<bool> HidePublicProfileAsync(long privacyLease, CancellationToken cancellationToken);
}

public interface INativeLicensePublicProfileWriter
{
    Task<bool> ShowPublicProfileAsync(long privacyLease, CancellationToken cancellationToken);
}

/// <summary>
/// Native Hunter License career and fixed privacy opt-out. An authenticated result is
/// delivered on the UI owner thread, fenced by request epoch and page lifetime.
/// </summary>
public sealed class NativeLicenseOverviewController : IDisposable
{
    public const int PageSize = 8, HistoryLimit = 25;
    private readonly record struct Completion(long Epoch, NativeLicenseOverview? Data,
        string? Error, bool PrivacySave = false, string? AccountStatus = null);
    private readonly INativeLicenseOverviewReader _reader;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Completion> _complete = new();
    private CancellationTokenSource? _work;
    private long _epoch, _revision = 1;
    private bool _busy, _disposed, _savingPrivacy;
    private bool _confirmPublic;
    private bool _accountWork, _publishing;
    private string _status = "SYNC YOUR HUNTER LICENSE TO VIEW VERIFIED CAREER DATA";
    private NativeLicenseOverview _current;
    private NativeLicenseOverview _privacyAuthority;
    private NativeLicenseFace _face;
    private int _page;
    private Snapshot _snapshot;

    public NativeLicenseOverviewController(INativeLicenseOverviewReader reader)
        => _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public readonly record struct Snapshot(long Revision, bool Busy,
        NativeLicenseOverview Overview, string Status, NativeLicenseFace Face,
        int Page, int Pages, int Total, IReadOnlyList<NativeLicenseMatch> Rows,
        bool CanHideProfile, bool SavingPrivacy, bool ConfirmPublic = false);

    public Snapshot Current()
    {
        Check();
        if (_snapshot.Revision == _revision) return _snapshot;
        var history = _current.History ?? Array.Empty<NativeLicenseMatch>();
        return _snapshot = new(_revision, _busy, _current, _status, _face, _page,
            Math.Max(1, (history.Count + PageSize - 1) / PageSize), history.Count,
            Array.AsReadOnly(history.Skip(_page * PageSize).Take(PageSize).ToArray()),
            !_busy && _reader is INativeLicensePrivacyWriter && _current.Verified
                && _current.PrivacyLease > 0 && _current.PublicVisibility is "PUBLIC" or "PRIVATE",
            _savingPrivacy, _confirmPublic);
    }

    public bool SetFace(NativeLicenseFace face)
    {
        Check();
        if (!Enum.IsDefined(face)) return false;
        bool changed = _face != face || _confirmPublic;
        _confirmPublic = false;
        if (changed) { _face = face; _revision++; }
        return true;
    }

    public bool Page(int direction)
    {
        Check();
        if (_busy || !_current.Verified || _face != NativeLicenseFace.History
            || direction is not (-1 or 1)) return false;
        int next = _page + direction;
        if (next < 0 || next >= Current().Pages) return false;
        _page = next; _revision++; return true;
    }

    public void Refresh()
    {
        Check();
        CancelWork();
        // An explicit sync may follow account recovery or a session change.
        // Without an identity lease, retaining the previous account is unsafe.
        _current = default;
        _page = 0;
        _savingPrivacy = false;
        _accountWork = false;
        _confirmPublic = false;
        long epoch = _epoch;
        var cts = new CancellationTokenSource();
        _work = cts;
        _busy = true;
        _status = "CONTACTING THE LICENSE SERVICE";
        _revision++;
        _ = Task.Run(() => Run(epoch, cts));
    }

    public bool RequestShowPublicProfile()
    {
        Check();
        if (_busy || !_current.Verified || _current.PublicVisibility != "PRIVATE"
            || _current.PrivacyLease <= 0 || _reader is not INativeLicensePublicProfileWriter) return false;
        _confirmPublic = true; _revision++; return true;
    }

    public bool CancelShowPublicProfile()
    {
        Check();
        if (!_confirmPublic) return false;
        _confirmPublic = false; _revision++; return true;
    }

    public bool ConfirmShowPublicProfile()
    {
        Check();
        if (!_confirmPublic) return false;
        return SavePrivacy(true);
    }

    public bool HidePublicProfile() => SavePrivacy(false);

    private bool SavePrivacy(bool visible)
    {
        Check();
        if (!Current().CanHideProfile || _reader is not INativeLicensePrivacyWriter writer
            || visible && _reader is not INativeLicensePublicProfileWriter) return false;
        NativeLicenseOverview loaded = _current;
        CancelWork();
        long epoch = _epoch;
        var cts = new CancellationTokenSource();
        _work = cts;
        _current = default;
        _privacyAuthority = loaded;
        _page = 0;
        _busy = _savingPrivacy = true;
        _publishing = visible;
        _confirmPublic = false;
        string failure = visible ? "PUBLIC SAVE NOT CONFIRMED // SYNC LICENSE BEFORE RETRY"
            : "PRIVATE SAVE NOT CONFIRMED // SYNC LICENSE BEFORE RETRY";
        _status = visible ? "SAVING PUBLIC PREFERENCE // WAITING FOR SERVER VERIFICATION"
            : "SAVING PRIVATE PREFERENCE // WAITING FOR SERVER VERIFICATION";
        _revision++;
        _ = Task.Run(async () =>
        {
            try
            {
                bool confirmed = visible
                    ? await ((INativeLicensePublicProfileWriter)_reader).ShowPublicProfileAsync(loaded.PrivacyLease, cts.Token).ConfigureAwait(false)
                    : await writer.HidePublicProfileAsync(loaded.PrivacyLease, cts.Token).ConfigureAwait(false);
                cts.Token.ThrowIfCancellationRequested();
                _complete.Enqueue(confirmed
                    ? new(epoch, loaded with { PublicVisibility = visible ? "PUBLIC" : "PRIVATE" }, null, true,
                        visible ? "PUBLIC PREFERENCE VERIFIED // HUNTER NAME AND CAREER VISIBLE" : null)
                    : new(epoch, null, failure, true));
            }
            catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
            {
                // Cancellation can arrive after persistence. Never promise a rollback.
                _complete.Enqueue(new(epoch, null, failure, true));
            }
            finally { cts.Dispose(); }
        });
        return true;
    }

    public bool SubmitAccount(NativeLicenseAccountAction action, string email,
        string password, string confirmation)
    {
        Check();
        NativeLicenseOverview loaded = _current;
        if (_busy || _face != NativeLicenseFace.Account || !loaded.Verified
            || loaded.PrivacyLease <= 0 || loaded.Account is not { } account
            || _reader is not INativeLicenseAccountWriter writer || !Enum.IsDefined(action)) return false;
        bool usesEmail = action is NativeLicenseAccountAction.SendVerification or NativeLicenseAccountAction.Recover;
        bool usesPassword = action is NativeLicenseAccountAction.SetPassword or NativeLicenseAccountAction.Recover;
        email = usesEmail ? email.Trim() : "";
        password = usesPassword ? password : "";
        string? invalid = usesEmail && (email.Length > 254 || email.Length < 3
            || !email.Contains('@') || email.Any(Char.IsWhiteSpace)) ? "ENTER A VALID EMAIL ADDRESS"
            : usesPassword && (password.Length < (action == NativeLicenseAccountAction.SetPassword ? 8 : 1)
                || password.Length > 128 || password != confirmation)
                ? action == NativeLicenseAccountAction.SetPassword
                    ? "USE 8–128 CHARACTERS AND MATCH BOTH PASSWORD FIELDS"
                    : "ENTER THE EXISTING PASSWORD AND MATCH BOTH PASSWORD FIELDS"
            : action == NativeLicenseAccountAction.SetPassword && (!account.EmailLinked || !account.Secure)
                ? "VERIFY A LINKED EMAIL BEFORE SETTING A PASSWORD"
            : action == NativeLicenseAccountAction.Recover && (account.Secure || loaded.MatchesPlayed != 0)
                ? "RECOVERY REQUIRES AN EMPTY GUEST LICENSE // SECURE YOUR CURRENT CAREER FIRST"
            : action == NativeLicenseAccountAction.LinkGoogle && account.GoogleLinked
                || action == NativeLicenseAccountAction.LinkGithub && account.GithubLinked
                || action == NativeLicenseAccountAction.LinkDiscord && account.DiscordLinked
                ? "THAT PROVIDER IS ALREADY LINKED" : null;
        if (invalid != null) { _status = invalid; _revision++; return false; }
        CancelWork();
        _confirmPublic = false;
        long epoch = _epoch;
        var cts = new CancellationTokenSource();
        _work = cts;
        _current = default; _page = 0;
        _busy = true; _savingPrivacy = false;
        _accountWork = true;
        _status = "CONTACTING ACCOUNT SERVICE // FIELDS CLEARED";
        _revision++;
        _ = Task.Run(async () =>
        {
            try
            {
                bool accepted = await writer.ExecuteAccountAsync(loaded.PrivacyLease,
                    action, email, password, cts.Token).ConfigureAwait(false);
                cts.Token.ThrowIfCancellationRequested();
                if (!accepted)
                    _complete.Enqueue(new(epoch, null, "ACCOUNT ACTION NOT CONFIRMED // SYNC BEFORE RETRY"));
                else
                {
                    NativeLicenseOverview refreshed = await _reader.LoadAsync(cts.Token).ConfigureAwait(false);
                    cts.Token.ThrowIfCancellationRequested();
                    string status = action switch
                    {
                        NativeLicenseAccountAction.SendVerification => "VERIFICATION REQUEST ACCEPTED // COMPLETE EMAIL VERIFICATION THEN SYNC",
                        NativeLicenseAccountAction.SetPassword => "PASSWORD UPDATE ACCEPTED // LICENSE REFRESHED",
                        NativeLicenseAccountAction.Recover => "LICENSE RECOVERED // CAREER REFRESHED",
                        _ => "BROWSER LINK STARTED // COMPLETE PROVIDER FLOW THEN SYNC"
                    };
                    _complete.Enqueue(new(epoch, Freeze(refreshed), null, AccountStatus: status));
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
            {
                _complete.Enqueue(new(epoch, null, "ACCOUNT ACTION NOT CONFIRMED // SYNC BEFORE RETRY"));
            }
            finally { cts.Dispose(); }
        });
        return true;
    }

    private async Task Run(long epoch, CancellationTokenSource cts)
    {
        try
        {
            NativeLicenseOverview overview = await _reader.LoadAsync(cts.Token)
                .ConfigureAwait(false);
            _complete.Enqueue(new(epoch, Freeze(overview), null));
        }
        catch (OperationCanceledException)
        {
            _complete.Enqueue(new(epoch, null, "LICENSE SYNC CANCELLED"));
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            // HTTP exception bodies can contain private account context.
            _complete.Enqueue(new(epoch, null, "LICENSE SYNC UNAVAILABLE // CHECK ACCOUNT OR SERVICE CONNECTION"));
        }
        finally { cts.Dispose(); }
    }

    public Snapshot Tick()
    {
        Check();
        while (_complete.TryDequeue(out Completion next))
        {
            if (next.Epoch != _epoch) continue;
            _work = null;
            _busy = false;
            _savingPrivacy = false;
            _accountWork = false;
            _privacyAuthority = default;
            if (next.Data is { } record)
            {
                if (record.Verified && !IsCurrent(record))
                {
                    RetireAccount();
                    continue;
                }
                _current = record.Verified ? record : default;
                _status = record.Verified
                    ? next.AccountStatus ?? (next.PrivacySave ? "PRIVATE PREFERENCE VERIFIED // PUBLIC HUNTER PROFILE HIDDEN"
                        : "VERIFIED CAREER RECORD // SERVER ACCEPTED MATCHES"
                        )
                    : "OFFLINE // CAREER DATA NOT VERIFIED";
            }
            else
            {
                // Never retain a previous account or claim verified zero totals.
                _current = default;
                _status = next.Error ?? "LICENSE SERVICE UNAVAILABLE";
            }
            _revision++;
        }
        return Current();
    }

    public void Cancel()
    {
        Check();
        CancelWork();
        _busy = false;
        _status = _savingPrivacy ? (_publishing ? "PUBLIC SAVE NOT CONFIRMED // SYNC LICENSE BEFORE RETRY"
            : "PRIVATE SAVE NOT CONFIRMED // SYNC LICENSE BEFORE RETRY")
            : _accountWork ? "ACCOUNT ACTION NOT CONFIRMED // SYNC BEFORE RETRY"
            : "LICENSE SYNC CANCELLED";
        _savingPrivacy = false;
        _accountWork = false;
        _confirmPublic = false;
        _revision++;
    }

    private void CancelWork()
    {
        _epoch++;
        try { _work?.Cancel(); }
        catch (ObjectDisposedException) { }
        _work = null;
        _privacyAuthority = default;
    }

    private bool IsCurrent(NativeLicenseOverview overview)
    {
        try { return _reader.IsCurrent(overview); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { return false; }
    }

    private void RetireAccount()
    {
        CancelWork();
        _current = default; _page = 0;
        _busy = _savingPrivacy = _accountWork = _confirmPublic = _publishing = false;
        _status = "LICENSE ACCOUNT CHANGED // SYNC TO VERIFY";
        _revision++;
    }

    private static NativeLicenseOverview Freeze(NativeLicenseOverview value)
    {
        if (!value.Verified) return default;
        bool Invalid(params long[] counts) => counts.Any(x => x < 0);
        if (Invalid(value.MatchesPlayed, value.Wins, value.Losses, value.Kills,
            value.Deaths, value.Assists, value.RecentMatches, value.RatingPoints, value.PrivacyLease))
            throw new InvalidOperationException("Invalid career totals.");
        if (value.Career is { } s && Invalid(s.Ties, s.Damage, s.PlayedTicks,
            s.Headshots, s.LongestKillStreak, s.CurrentWinStreak, s.LongestWinStreak,
            s.OctolithScores, s.NodesCaptured, s.KillsAsPrime))
            throw new InvalidOperationException("Invalid detailed totals.");
        var history = value.History ?? Array.Empty<NativeLicenseMatch>();
        if (history.Count > HistoryLimit || history.Any(m => m == null
            || Invalid(m.Kills, m.Deaths, m.Assists, m.Damage, m.PlayedTicks)))
            throw new InvalidOperationException("Invalid career history.");
        return value with
        {
            PublicVisibility = value.PublicVisibility is "PUBLIC" or "PRIVATE"
                ? value.PublicVisibility : "UNAVAILABLE",
            History = Array.AsReadOnly(history.ToArray())
        };
    }

    private void Check()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Hunter License UI is owned by the render thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_current.Verified && !IsCurrent(_current)
            || _privacyAuthority.Verified && !IsCurrent(_privacyAuthority))
            RetireAccount();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Check();
        Cancel();
        _disposed = true;
        while (_complete.TryDequeue(out _)) { }
    }
}
