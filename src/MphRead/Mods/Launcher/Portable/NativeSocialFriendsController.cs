using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

public enum NativeSocialRelation { Player, Friend, IncomingRequest, OutgoingRequest, Blocked }
public enum NativeSocialFriendAction { SendRequest, AcceptRequest, DeclineRequest,
    CancelRequest, RemoveFriend, BlockPlayer, UnblockPlayer }

public sealed record NativeSocialPerson(string PrimeId, string DisplayName,
    NativeSocialRelation Relation)
{
    public DateTimeOffset? RecentLastSeen { get; init; }
    public int RecentEncounters { get; init; }
}
public sealed record NativeSocialRecentPlayer(
    [property: JsonPropertyName("prime_id")] string PrimeId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("last_seen")] DateTimeOffset LastSeen,
    [property: JsonPropertyName("encounters")] int Encounters);
public static class NativeSocialRecentPlayers
{
    public static NativeSocialRecentPlayer[] Verify(string self, IReadOnlyList<NativeSocialRecentPlayer>? rows)
    {
        if (!NativeSocialFriendsController.ValidPrimeId(self) || rows == null || rows.Count > 30)
            throw new InvalidOperationException("Recent players unavailable.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            if (row == null || !NativeSocialFriendsController.ValidPrimeId(row.PrimeId)
                || row.PrimeId.Equals(self, StringComparison.OrdinalIgnoreCase) || !ids.Add(row.PrimeId)
                || String.IsNullOrWhiteSpace(row.DisplayName) || row.DisplayName.Length > 128
                || row.DisplayName.Any(Char.IsControl) || row.LastSeen == default
                || row.LastSeen > DateTimeOffset.UtcNow.AddMinutes(5) || row.Encounters is < 1 or > 1000000)
                throw new InvalidOperationException("Recent players unavailable.");
        return rows.Select(r => r with { PrimeId = r.PrimeId.ToUpperInvariant() }).ToArray();
    }
}
public sealed record NativeSocialDirectory(string SelfPrimeId, NativeSocialPerson[] People)
{
    public bool InvitationsAvailable { get; init; }
    public bool RecentAvailable { get; init; }
    public NativeSocialRecentPlayer[] RecentPlayers { get; init; } = Array.Empty<NativeSocialRecentPlayer>();
    public NativeSocialInvitation[] Invitations { get; init; } = Array.Empty<NativeSocialInvitation>();
    [JsonIgnore]
    internal object? AccountWitness { get; init; }
}
public readonly record struct NativeSocialMutation(bool Accepted, string Status);

public interface INativeSocialFriendsService
{
    Task<NativeSocialLobbyResult> ExecuteLobbyAsync(NativeSocialLobbyCommand command, CancellationToken cancellation)
        => throw new InvalidOperationException("Lobby invitations are unavailable.");
    bool IsCurrent(NativeSocialDirectory directory) => true;
    Task<NativeSocialDirectory> ReadAsync(CancellationToken cancellation);
    Task<NativeSocialPerson?> LookupAsync(string primeId, CancellationToken cancellation);
    Task<NativeSocialMutation> ExecuteAsync(NativeSocialFriendAction action, string primeId,
        CancellationToken cancellation);
    Task<NativeSocialMutation> ExecuteForAsync(NativeSocialFriendAction action, string primeId,
        string expectedSelf, CancellationToken cancellation)
        => ExecuteAsync(action, primeId, cancellation);
}

public sealed record NativeSocialView(long Revision, bool Busy, string Status, string SelfPrimeId,
    string Tab, int Page, int Pages, int Total, NativeSocialPerson[] Rows,
    NativeSocialPerson? Selected, string Confirmation, bool Connected)
{
    public bool InvitationsAvailable { get; init; }
    public bool RecentAvailable { get; init; }
    public NativeSocialInvitation[] InvitationRows { get; init; } = Array.Empty<NativeSocialInvitation>();
    public NativeSocialInvitation? SelectedInvitation { get; init; }
}

/// <summary>
/// A narrowly scoped, owner-thread view of server-authored friends, requests
/// and block state. HTTP never runs on the renderer; confirmations bind exact
/// ID+relation+data epoch; no access token or fabricated online count exists.
/// </summary>
public sealed partial class NativeSocialFriendsController : IDisposable
{
    public const int PageSize = 8;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly INativeSocialFriendsService _service;
    private readonly ConcurrentQueue<(int Generation, NativeSocialDirectory? Data,
        NativeSocialPerson? Found, NativeSocialJoinTarget? JoinTarget, bool LookupCompleted, string? Error)> _completed = new();
    private CancellationTokenSource? _active;
    private int _generation;
    private long _revision = 1, _dataEpoch = 1;
    private bool _busy, _disposed, _connected;
    private string _tab = "FRIENDS", _filter = "", _selectedId = "", _selfId = "",
        _status = "AUTHENTICATED SOCIAL DIRECTORY NOT YET LOADED";
    private int _page;
    private NativeSocialPerson[] _people = Array.Empty<NativeSocialPerson>();
    private NativeSocialDirectory? _directory;
    private NativeSocialPerson? _lookup;
    private (NativeSocialFriendAction Action, NativeSocialPerson Target, long Epoch)? _confirm;

    public NativeSocialFriendsController(INativeSocialFriendsService service, TimeProvider? clock = null)
    { _service = service ?? throw new ArgumentNullException(nameof(service)); _clock = clock ?? TimeProvider.System; }
    private void CheckOwner()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSocialFriendsController));
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Native Social belongs to the UI owner thread.");
        RetireChangedAccount();
    }

    private void ClearPrivateData()
    {
        _directory = null;
        _pendingJoin = null; _selectedInvite = "";
        _invitationBoundary = DateTimeOffset.MaxValue;
        _people = Array.Empty<NativeSocialPerson>();
        _lookup = null;
        _selectedId = _selfId = _filter = "";
        _connected = false;
        _confirm = null;
        _page = 0;
        _dataEpoch++;
    }

    private void RetireChangedAccount()
    {
        if (_directory == null || _service.IsCurrent(_directory)) return;
        RetireAccount();
    }

    private void RetireAccount()
    {
        CancelCore();
        ClearPrivateData();
        _busy = false;
        _status = "SOCIAL ACCOUNT CHANGED // REFRESH TO VERIFY";
        _revision++;
    }

    public static bool ValidPrimeId(string value)
    {
        if (value == null || value.Length != 27
            || !value.StartsWith("PP-", StringComparison.OrdinalIgnoreCase)) return false;
        for (int i = 3; i < value.Length; i++)
            if (i is 7 or 12 or 17 or 22 ? value[i] != '-' : !Uri.IsHexDigit(value[i]))
                return false;
        return true;
    }

    public NativeSocialView Current()
    {
        CheckOwner();
        var visible = Filtered();
        bool invites = _tab == "INVITES";
        var invitations = invites ? Invitations() : Array.Empty<NativeSocialInvitation>();
        int total = invites ? invitations.Length : visible.Length;
        _page = Math.Clamp(_page, 0, Math.Max(0, (total - 1) / PageSize));
        var rows = visible.Skip(_page * PageSize).Take(PageSize).ToArray();
        var inviteRows = invitations.Skip(_page * PageSize).Take(PageSize).ToArray();
        NativeSocialPerson? selected = visible.Concat(_people).Concat(
            _lookup is { } found ? new[] { found } : Array.Empty<NativeSocialPerson>())
            .FirstOrDefault(x => String.Equals(x.PrimeId, _selectedId,
                StringComparison.OrdinalIgnoreCase));
        return new(_revision, _busy, _status, _selfId, _tab,
            _page, Math.Max(1, (total + PageSize - 1) / PageSize),
            total, rows, invites ? null : selected, _confirm is { } pending
                ? (pending.Action == NativeSocialFriendAction.RemoveFriend
                    ? "REMOVE THIS FRIEND?" : "BLOCK THIS PLAYER AND END THE RELATIONSHIP?")
                : "", _connected)
        {
            InvitationsAvailable = _directory?.InvitationsAvailable == true,
            RecentAvailable = _directory?.RecentAvailable == true,
            InvitationRows = inviteRows,
            SelectedInvitation = inviteRows.FirstOrDefault(i => i.InviteId == _selectedInvite)
        };
    }

    private NativeSocialPerson[] Filtered()
    {
        IEnumerable<NativeSocialPerson> source = _tab switch
        {
            "FRIENDS" => _people.Where(x => x.Relation == NativeSocialRelation.Friend),
            "REQUESTS" => _people.Where(x => x.Relation is
                NativeSocialRelation.IncomingRequest or NativeSocialRelation.OutgoingRequest),
            "BLOCKED" => _people.Where(x => x.Relation == NativeSocialRelation.Blocked),
            "PLAYERS" => _lookup is { } item ? new[] { item } : Array.Empty<NativeSocialPerson>(),
            "RECENT" => Recent(),
            _ => Array.Empty<NativeSocialPerson>()
        };
        if (_filter.Length != 0)
            source = source.Where(x =>
                x.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || x.PrimeId.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        if (_tab == "RECENT") return source.OrderByDescending(x => x.RecentLastSeen)
            .ThenBy(x => x.PrimeId, StringComparer.OrdinalIgnoreCase).ToArray();
        return source.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PrimeId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private NativeSocialPerson[] Recent() => _directory?.RecentAvailable != true ? Array.Empty<NativeSocialPerson>()
        : _directory.RecentPlayers.Select(row =>
        {
            var known = _people.FirstOrDefault(p => p.PrimeId.Equals(row.PrimeId, StringComparison.OrdinalIgnoreCase));
            return (known ?? new NativeSocialPerson(row.PrimeId, row.DisplayName, NativeSocialRelation.Player))
                with { RecentLastSeen = row.LastSeen, RecentEncounters = row.Encounters };
        }).Where(person => person.Relation != NativeSocialRelation.Blocked).ToArray();

    private void Begin(string status, Func<CancellationToken,
        Task<(NativeSocialDirectory?, NativeSocialPerson?, NativeSocialJoinTarget?)>> work, bool lookup = false)
    {
        CheckOwner();
        CancelCore();
        int gen = _generation;
        var cancel = new CancellationTokenSource();
        _active = cancel;
        _busy = true;
        _status = status;
        _revision++;
        _ = Task.Run(async () =>
        {
            try
            {
                var data = await work(cancel.Token).ConfigureAwait(false);
                cancel.Token.ThrowIfCancellationRequested();
                _completed.Enqueue((gen, data.Item1, data.Item2, data.Item3, lookup, null));
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            { /* cancelled work never publishes */ }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // Service errors may contain private HTTP response context.
                // The UI displays no untrusted response body or account token.
                _completed.Enqueue((gen, null, null, null, lookup,
                    "SOCIAL UNAVAILABLE // CHECK ACCOUNT OR SERVICE CONNECTION"));
            }
            finally { cancel.Dispose(); }
        });
    }

    public void Refresh()
    {
        CheckOwner();
        // A refresh can follow an account change. Retire old private rows
        // immediately rather than showing a previous identity until HTTPS ends.
        ClearPrivateData();
        Begin("SYNCING VERIFIED FRIENDS AND REQUESTS",
            async token => (await _service.ReadAsync(token).ConfigureAwait(false), null, null));
    }

    public void Cancel()
    {
        CheckOwner(); CancelCore();
        _busy = false;
        _status = "SOCIAL REQUEST CANCELLED";
        _revision++;
    }

    private void CancelCore()
    {
        _pendingJoin = null;
        _generation++;
        try { _active?.Cancel(); }
        catch (ObjectDisposedException) { }
        _active = null;
    }

    public bool SetTab(int index)
    {
        CheckOwner();
        string next = index switch { 0 => "FRIENDS", 1 => "PLAYERS", 2 => "REQUESTS",
            3 => "INVITES", 5 => "RECENT", 6 => "BLOCKED", _ => "" };
        if (next.Length == 0 || _busy) return false;
        if (next == "INVITES" && _directory?.InvitationsAvailable != true) return false;
        if (next == "RECENT" && _directory?.RecentAvailable != true)
        { _status = "RECENT PLAYERS UNAVAILABLE // REFRESH TO VERIFY"; _revision++; return false; }
        _selectedInvite = "";
        _selectedId = "";
        _tab = next; _page = 0; _confirm = null; _revision++; return true;
    }

    public bool Search(string query)
    {
        CheckOwner();
        if (_busy || query == null || query.Length > 128) return false;
        _filter = query.Trim(); _page = 0; _revision++; return true;
    }

    public bool Lookup(string primeId)
    {
        CheckOwner();
        if (_busy || !ValidPrimeId(primeId)) return false;
        string id = primeId.Trim().ToUpperInvariant();
        Begin("LOOKING UP ACCOUNT ID", async token =>
            (null, await _service.LookupAsync(id, token).ConfigureAwait(false), null), lookup: true);
        return true;
    }

    public bool Page(int direction)
    {
        CheckOwner();
        if (_busy || direction is not (-1 or 1)) return false;
        int count = Current().Pages;
        int target = _page + direction;
        if (target < 0 || target >= count) return false;
        _page = target; _revision++; return true;
    }

    public bool Select(int row)
    {
        CheckOwner();
        if (_busy || row is < 0 or >= PageSize) return false;
        if (_tab == "INVITES")
        {
            var invitation = Invitations().Skip(_page * PageSize).Skip(row).FirstOrDefault();
            if (invitation == null) return false;
            _selectedInvite = invitation.InviteId; _selectedId = "";
            _confirm = null; _revision++; return true;
        }
        var chosen = Filtered().Skip(_page * PageSize).Skip(row).FirstOrDefault();
        if (chosen == null) return false;
        _selectedId = chosen.PrimeId;
        _confirm = null; _revision++;
        return true;
    }

    public bool Can(NativeSocialFriendAction command)
    {
        CheckOwner();
        NativeSocialPerson? item = Current().Selected;
        if (_busy || !_connected || item == null) return false;
        return command switch
        {
            NativeSocialFriendAction.SendRequest => item.Relation == NativeSocialRelation.Player,
            NativeSocialFriendAction.AcceptRequest or NativeSocialFriendAction.DeclineRequest
                => item.Relation == NativeSocialRelation.IncomingRequest,
            NativeSocialFriendAction.CancelRequest
                => item.Relation == NativeSocialRelation.OutgoingRequest,
            NativeSocialFriendAction.RemoveFriend
                => item.Relation == NativeSocialRelation.Friend,
            NativeSocialFriendAction.BlockPlayer
                => item.Relation is not NativeSocialRelation.Blocked,
            NativeSocialFriendAction.UnblockPlayer
                => item.Relation == NativeSocialRelation.Blocked,
            _ => false
        };
    }

    public bool Command(NativeSocialFriendAction command)
    {
        CheckOwner();
        if (!Can(command)) return false;
        NativeSocialPerson? selected = Current().Selected;
        if (selected == null) return false;
        if (command is NativeSocialFriendAction.RemoveFriend or NativeSocialFriendAction.BlockPlayer)
        {
            _confirm = (command, selected, _dataEpoch);
            _revision++;
            return true;
        }
        return Submit(command, selected);
    }

    public bool Confirm()
    {
        CheckOwner();
        if (_confirm is not { } pending || pending.Epoch != _dataEpoch
            || _busy || !Can(pending.Action) || Current().Selected != pending.Target)
        {
            _confirm = null; _revision++; return false;
        }
        _confirm = null;
        return Submit(pending.Action, pending.Target);
    }

    public bool Dismiss()
    {
        CheckOwner();
        if (_confirm == null) return false;
        _confirm = null; _revision++; return true;
    }

    private bool Submit(NativeSocialFriendAction action, NativeSocialPerson selected)
    {
        string id = selected.PrimeId;
        string expectedSelf = _selfId;
        Begin("SENDING AUTHENTICATED SOCIAL ACTION", async token =>
        {
            NativeSocialMutation outcome = await _service.ExecuteForAsync(action, id, expectedSelf, token)
                .ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!outcome.Accepted)
                throw new InvalidOperationException("Social action was not accepted.");
            NativeSocialDirectory reread = await _service.ReadAsync(token).ConfigureAwait(false);
            if (!String.Equals(reread.SelfPrimeId, expectedSelf, StringComparison.Ordinal))
                throw new InvalidOperationException("Social account changed.");
            return (reread, null, null);
        });
        return true;
    }

    public NativeSocialView Tick()
    {
        CheckOwner();
        if (_clock.GetUtcNow() >= _invitationBoundary)
        { NextInvitationBoundary(); _revision++; }
        while (_completed.TryDequeue(out var item))
        {
            if (item.Generation != _generation) continue;
            _busy = false;
            _active = null;
            if (item.Error != null)
            {
                // Drop stale private data rather than displaying another
                // identity's cached roster when auth or network refresh fails.
                ClearPrivateData();
                _status = item.Error; _revision++; continue;
            }
            if (item.Data is { } data)
            {
                if (!_service.IsCurrent(data))
                {
                    RetireAccount();
                    continue;
                }
                if (!ValidPrimeId(data.SelfPrimeId))
                {
                    _status = "SOCIAL SERVICE RETURNED NO VERIFIED ACCOUNT ID";
                    ClearPrivateData();
                    _revision++; continue;
                }
                string self = data.SelfPrimeId.ToUpperInvariant();
                _selfId = self;
                _connected = true;
                _people = data.People.Where(x =>
                    ValidPrimeId(x.PrimeId) &&
                    !x.PrimeId.Equals(self, StringComparison.OrdinalIgnoreCase)
                    && x.DisplayName != null && x.DisplayName.Length <= 128)
                    .OrderByDescending(x => x.Relation == NativeSocialRelation.Blocked ? 4
                        : x.Relation == NativeSocialRelation.Friend ? 3 : x.Relation == NativeSocialRelation.IncomingRequest ? 2
                        : x.Relation == NativeSocialRelation.OutgoingRequest ? 1 : 0)
                    .DistinctBy(x => x.PrimeId, StringComparer.OrdinalIgnoreCase)
                    .Take(1024).ToArray();
                NativeSocialRecentPlayer[] recent = Array.Empty<NativeSocialRecentPlayer>();
                bool recentAvailable = data.RecentAvailable;
                if (recentAvailable)
                {
                    try { recent = NativeSocialRecentPlayers.Verify(self, data.RecentPlayers); }
                    catch (InvalidOperationException) { recentAvailable = false; }
                }
                _directory = data with { SelfPrimeId = self, People = _people, RecentAvailable = recentAvailable, RecentPlayers = recent,
                    Invitations = (data.Invitations ?? Array.Empty<NativeSocialInvitation>()).Where(i => NativeSocialInvitationValidation.Valid(i)
                        && !i.PrimeId.Equals(self, StringComparison.OrdinalIgnoreCase))
                        .DistinctBy(i => i.InviteId, StringComparer.OrdinalIgnoreCase).Take(1024).ToArray() };
                NextInvitationBoundary();
                _dataEpoch++;
                _confirm = null;
                if (!_people.Any(x => x.PrimeId.Equals(_selectedId,
                    StringComparison.OrdinalIgnoreCase))) _selectedId = "";
                _status = "SERVER-VERIFIED SOCIAL DIRECTORY // " + _people.Length + " ENTRIES"
                    + (recentAvailable ? "" : " // RECENT PLAYERS UNAVAILABLE");
            }
            if (item.JoinTarget is { } target && _directory != null
                && ReferenceEquals(target.AccountWitness, _directory.AccountWitness)
                && target.SelfPrimeId == _selfId
                && NativeSocialInvitationValidation.ValidTarget(target, _clock.GetUtcNow()))
                _pendingJoin = target;
            if (item.LookupCompleted && item.Found == null)
            {
                _lookup = null; _selectedId = ""; _tab = "PLAYERS";
                _status = "ACCOUNT ID NOT FOUND";
            }
            if (item.Found is { } found)
            {
                if (ValidPrimeId(found.PrimeId) && !found.PrimeId.Equals(_selfId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    _lookup = found with { Relation = NativeSocialRelation.Player };
                    _tab = "PLAYERS"; _filter = ""; _selectedId = found.PrimeId;
                    _status = "ACCOUNT FOUND // REVIEW BEFORE REQUEST";
                }
                else _status = "NO VERIFIED PLAYER MATCH";
            }
            _page = 0; _revision++;
        }
        return Current();
    }

    public void Dispose()
    {
        if (_disposed) return;
        CheckOwner();
        CancelCore(); _busy = false; _disposed = true;
        while (_completed.TryDequeue(out _)) { }
    }
}
