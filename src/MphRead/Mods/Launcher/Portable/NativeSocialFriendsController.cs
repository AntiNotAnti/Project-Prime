using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

public enum NativeSocialRelation { Player, Friend, IncomingRequest, OutgoingRequest, Blocked }
public enum NativeSocialFriendAction { SendRequest, AcceptRequest, DeclineRequest,
    CancelRequest, RemoveFriend, BlockPlayer, UnblockPlayer }

public sealed record NativeSocialPerson(string PrimeId, string DisplayName,
    NativeSocialRelation Relation);
public sealed record NativeSocialDirectory(string SelfPrimeId, NativeSocialPerson[] People);
public readonly record struct NativeSocialMutation(bool Accepted, string Status);

public interface INativeSocialFriendsService
{
    Task<NativeSocialDirectory> ReadAsync(CancellationToken cancellation);
    Task<NativeSocialPerson?> LookupAsync(string primeId, CancellationToken cancellation);
    Task<NativeSocialMutation> ExecuteAsync(NativeSocialFriendAction action, string primeId,
        CancellationToken cancellation);
}

public sealed record NativeSocialView(long Revision, bool Busy, string Status, string SelfPrimeId,
    string Tab, int Page, int Pages, int Total, NativeSocialPerson[] Rows,
    NativeSocialPerson? Selected, string Confirmation, bool Connected);

/// <summary>
/// A narrowly scoped, owner-thread view of server-authored friends, requests
/// and block state. HTTP never runs on the renderer; confirmations bind exact
/// ID+relation+data epoch; no access token or fabricated online count exists.
/// </summary>
public sealed class NativeSocialFriendsController : IDisposable
{
    public const int PageSize = 8;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly INativeSocialFriendsService _service;
    private readonly ConcurrentQueue<(int Generation, NativeSocialDirectory? Data,
        NativeSocialPerson? Found, bool LookupCompleted, string? Error)> _completed = new();
    private CancellationTokenSource? _active;
    private int _generation;
    private long _revision = 1, _dataEpoch = 1;
    private bool _busy, _disposed, _connected;
    private string _tab = "FRIENDS", _filter = "", _selectedId = "", _selfId = "",
        _status = "AUTHENTICATED SOCIAL DIRECTORY NOT YET LOADED";
    private int _page;
    private NativeSocialPerson[] _people = Array.Empty<NativeSocialPerson>();
    private NativeSocialPerson? _lookup;
    private (NativeSocialFriendAction Action, NativeSocialPerson Target, long Epoch)? _confirm;

    public NativeSocialFriendsController(INativeSocialFriendsService service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));
    private void CheckOwner()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSocialFriendsController));
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Native Social belongs to the UI owner thread.");
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
        var rows = visible.Skip(_page * PageSize).Take(PageSize).ToArray();
        NativeSocialPerson? selected = _people.Concat(
            _lookup is { } found ? new[] { found } : Array.Empty<NativeSocialPerson>())
            .FirstOrDefault(x => String.Equals(x.PrimeId, _selectedId,
                StringComparison.OrdinalIgnoreCase));
        return new(_revision, _busy, _status, _selfId, _tab,
            _page, Math.Max(1, (visible.Length + PageSize - 1) / PageSize),
            visible.Length, rows, selected, _confirm is { } pending
                ? (pending.Action == NativeSocialFriendAction.RemoveFriend
                    ? "REMOVE THIS FRIEND?" : "BLOCK THIS PLAYER AND END THE RELATIONSHIP?")
                : "", _connected);
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
            _ => Array.Empty<NativeSocialPerson>()
        };
        if (_filter.Length != 0)
            source = source.Where(x =>
                x.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                || x.PrimeId.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        return source.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PrimeId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void Begin(string status, Func<CancellationToken,
        Task<(NativeSocialDirectory?, NativeSocialPerson?)>> work, bool lookup = false)
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
                _completed.Enqueue((gen, data.Item1, data.Item2, lookup, null));
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            { /* cancelled work never publishes */ }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // Service errors may contain private HTTP response context.
                // The UI displays no untrusted response body or account token.
                _completed.Enqueue((gen, null, null, lookup,
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
        _confirm = null;
        _people = Array.Empty<NativeSocialPerson>();
        _lookup = null;
        _selectedId = _selfId = "";
        _connected = false;
        _dataEpoch++;
        Begin("SYNCING VERIFIED FRIENDS AND REQUESTS",
            async token => (await _service.ReadAsync(token).ConfigureAwait(false), null));
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
        _generation++;
        try { _active?.Cancel(); }
        catch (ObjectDisposedException) { }
        _active = null;
    }

    public bool SetTab(int index)
    {
        CheckOwner();
        string next = index switch { 0 => "FRIENDS", 1 => "PLAYERS", 2 => "REQUESTS",
            6 => "BLOCKED", _ => "" };
        if (next.Length == 0 || _busy) return false;
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
            (null, await _service.LookupAsync(id, token).ConfigureAwait(false)), lookup: true);
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
        NativeSocialPerson selected = Current().Selected!;
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
        Begin("SENDING AUTHENTICATED SOCIAL ACTION", async token =>
        {
            NativeSocialMutation outcome = await _service.ExecuteAsync(action, id, token)
                .ConfigureAwait(false);
            if (!outcome.Accepted)
                throw new InvalidOperationException("Social action was not accepted.");
            return (await _service.ReadAsync(token).ConfigureAwait(false), null);
        });
        return true;
    }

    public NativeSocialView Tick()
    {
        CheckOwner();
        while (_completed.TryDequeue(out var item))
        {
            if (item.Generation != _generation) continue;
            _busy = false;
            _active = null;
            if (item.Error != null)
            {
                // Drop stale private data rather than displaying another
                // identity's cached roster when auth or network refresh fails.
                _people = Array.Empty<NativeSocialPerson>();
                _lookup = null; _selectedId = ""; _connected = false;
                _confirm = null; _selfId = ""; _dataEpoch++;
                _status = item.Error; _revision++; continue;
            }
            if (item.Data is { } data)
            {
                if (!ValidPrimeId(data.SelfPrimeId))
                {
                    _status = "SOCIAL SERVICE RETURNED NO VERIFIED ACCOUNT ID";
                    _people = Array.Empty<NativeSocialPerson>();
                    _connected = false; _selfId = ""; _confirm = null;
                    _dataEpoch++; _revision++; continue;
                }
                string self = data.SelfPrimeId.ToUpperInvariant();
                _selfId = self;
                _connected = true;
                _people = data.People.Where(x =>
                    ValidPrimeId(x.PrimeId) &&
                    !x.PrimeId.Equals(self, StringComparison.OrdinalIgnoreCase)
                    && x.DisplayName != null && x.DisplayName.Length <= 128)
                    .DistinctBy(x => x.PrimeId, StringComparer.OrdinalIgnoreCase)
                    .Take(1024).ToArray();
                _dataEpoch++;
                _confirm = null;
                if (!_people.Any(x => x.PrimeId.Equals(_selectedId,
                    StringComparison.OrdinalIgnoreCase))) _selectedId = "";
                _status = "SERVER-VERIFIED SOCIAL DIRECTORY // " + _people.Length + " ENTRIES";
            }
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
