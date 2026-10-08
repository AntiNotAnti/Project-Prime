#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Core;

public enum MatchResultsAction { Close, Search, PreviousPage, NextPage, Rematch, ClearSearch, SelectMap }
public enum MatchResultsEffect { None, Close, Rematch }
public readonly record struct MatchResultsMap(string Key, string Name, int Votes)
{
    public int CatalogIndex { get; init; } = -1;
}
public sealed record MatchResultsFacts(bool Available, bool Online, bool Persistent, bool BallotOpen,
    int Countdown, string NextRoom, string Picked, string Leader, int Eligible, ImmutableArray<MatchResultsMap> Maps);
public sealed record MatchResultsSnapshot(Guid Lifetime, long Revision, MatchResultsFacts Facts,
    string Query, int Page, ImmutableArray<MatchResultsMap> Rows, int PageCount, string Error, bool Closed);
public readonly record struct MatchResultsIntent(Guid Lifetime, long Revision, MatchResultsAction Action, string MapKey = "");
public readonly record struct MatchResultsResult(bool Accepted, MatchResultsEffect Effect = MatchResultsEffect.None, string Error = "");

public interface IMatchResultsBackend
{
    MatchResultsFacts Capture();
    string Choose(string roomKey);
    string Rematch();
}

/// <summary>The engine retains the scoreboard and the existing ballot/rematch services retain authority.</summary>
public sealed class MatchResultsController : IDisposable
{
    private sealed class Backend : IMatchResultsBackend
    {
        public MatchResultsFacts Capture() => new(EndScreen.PanelAvailable, NetSession.Active, NetSession.PersistentLobby,
            MapPick.Open && !NetSession.PersistentLobby, GameState.MatchState == MatchState.Ending
                ? (int)Math.Max(0, Math.Ceiling(GameState.MatchTime)) : -1,
            EndScreen.NextRoomName, MapPick.Picked, MapPick.Leader, MapPick.Eligible,
            MapPick.Order.ToArray().Select(key => new MatchResultsMap(key, MapPick.NameOf(key), MapPick.VotesFor(key))).ToImmutableArray());
        public string Choose(string roomKey)
        {
            if (!EndScreen.PanelAvailable || NetSession.PersistentLobby || !MapPick.Available)
                return "The next arena ballot is no longer available.";
            int index = MapPick.IndexOf(roomKey);
            if (index < 0) return "That arena left the ballot. Select another arena.";
            MapPick.Choose(index);
            return "";
        }
        public string Rematch() => !NetSession.Active && EndScreen.PanelAvailable && OfflineRematch.Continue()
            ? "" : "The next offline match could not be started.";
    }
    private readonly IMatchResultsBackend _backend;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private ImmutableArray<string> _order = ImmutableArray<string>.Empty;
    private MatchResultsSnapshot _state;
    private bool _dispatching;
    public MatchResultsController() : this(new Backend()) { }
    public MatchResultsController(IMatchResultsBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _state = new(Guid.NewGuid(), 0, backend.Capture(), "", 0, ImmutableArray<MatchResultsMap>.Empty, 1, "", false);
        Refresh();
    }
    public MatchResultsSnapshot Snapshot() { CheckOwner(); return _state; }
    public MatchResultsIntent Intent(MatchResultsAction action, int row = -1)
    {
        CheckOwner();
        return new(_state.Lifetime, _state.Revision, action,
            action == MatchResultsAction.SelectMap && row >= 0 && row < _order.Length ? _order[row] : "");
    }
    public void Refresh()
    {
        CheckOwner();
        if (_state.Closed) return;
        MatchResultsFacts facts = _backend.Capture();
        // The queued numeric action retains a stable key while visible tally
        // order changes. Expired catalog identities are never reassigned.
        foreach (var map in facts.Maps) if (!_order.Contains(map.Key, StringComparer.OrdinalIgnoreCase)) _order = _order.Add(map.Key);
        Project(_state with { Facts = facts });
    }
    public MatchResultsResult Dispatch(MatchResultsIntent intent, string query = "")
    {
        CheckOwner();
        if (_state.Closed || _dispatching || intent.Lifetime != _state.Lifetime || intent.Revision != _state.Revision || !Enum.IsDefined(intent.Action))
            return new(false, Error: "The results changed. Try the action again.");
        _dispatching = true;
        try
        {
            MatchResultsFacts current = _backend.Capture();
            if (!current.Available) return Reject("The results screen has ended.");
            switch (intent.Action)
            {
                case MatchResultsAction.Close:
                    _state = _state with { Closed = true, Revision = _state.Revision + 1 };
                    return new(true, MatchResultsEffect.Close);
                case MatchResultsAction.Search:
                    Project(_state with { Query = (query ?? "").Trim(), Page = 0, Error = "", Facts = current }); return new(true);
                case MatchResultsAction.ClearSearch:
                    Project(_state with { Query = "", Page = 0, Error = "", Facts = current }); return new(true);
                case MatchResultsAction.PreviousPage:
                case MatchResultsAction.NextPage:
                    Project(_state with { Page = _state.Page + (intent.Action == MatchResultsAction.PreviousPage ? -1 : 1), Error = "", Facts = current }); return new(true);
                case MatchResultsAction.SelectMap:
                    if (current.Persistent || !current.BallotOpen || intent.MapKey.Length == 0 || !current.Maps.Any(map => String.Equals(map.Key, intent.MapKey, StringComparison.OrdinalIgnoreCase)))
                        return Reject("That arena is no longer available in the ballot.");
                    string chooseError = _backend.Choose(intent.MapKey);
                    if (chooseError.Length > 0) return Reject(chooseError);
                    Project(_state with { Facts = _backend.Capture(), Error = "" }); return new(true);
                case MatchResultsAction.Rematch:
                    if (current.Online || current.Persistent) return Reject("The server controls the next match in this session.");
                    string rematchError = _backend.Rematch();
                    if (rematchError.Length > 0) return Reject(rematchError);
                    _state = _state with { Closed = true, Revision = _state.Revision + 1 };
                    return new(true, MatchResultsEffect.Rematch);
                default: return Reject("That results action is unavailable.");
            }
        }
        catch (Exception error) { return Reject(error.Message); }
        finally { _dispatching = false; }
    }
    public void Dispose() { CheckOwner(); if (!_state.Closed) _state = _state with { Closed = true, Revision = _state.Revision + 1 }; }
    private MatchResultsResult Reject(string error) { Project(_state with { Error = error }); return new(false, Error: error); }
    private void Project(MatchResultsSnapshot state)
    {
        var maps = state.Facts.Maps.Where(map => map.Key != null && (state.Query.Length == 0 || map.Key.Contains(state.Query, StringComparison.OrdinalIgnoreCase) || map.Name.Contains(state.Query, StringComparison.OrdinalIgnoreCase)))
            .Select(map => map with { CatalogIndex = CatalogIndex(map.Key) }).ToArray();
        int pages = Math.Max(1, (maps.Length + 7) / 8), page = Math.Clamp(state.Page, 0, pages - 1);
        var rows = maps.Skip(page * 8).Take(8).ToImmutableArray();
        if (SameFacts(state.Facts, _state.Facts) && state.Query == _state.Query && page == _state.Page && state.Error == _state.Error && rows.SequenceEqual(_state.Rows) && pages == _state.PageCount && _state.Revision > 0) return;
        _state = state with { Page = page, PageCount = pages, Rows = rows, Revision = _state.Revision + 1 };
    }
    private static bool SameFacts(MatchResultsFacts a, MatchResultsFacts b) => a.Maps.SequenceEqual(b.Maps)
        && (a with { Maps = ImmutableArray<MatchResultsMap>.Empty }) == (b with { Maps = ImmutableArray<MatchResultsMap>.Empty });
    private int CatalogIndex(string key)
    {
        for (int index = 0; index < _order.Length; index++) if (String.Equals(_order[index], key, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }
    private void CheckOwner() { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Results decisions belong to the engine thread."); }
}
#endif
