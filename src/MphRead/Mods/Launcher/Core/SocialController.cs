using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Owns native Social presentation, cancellation and stale-action witnesses; services own authority.</summary>
public sealed class SocialController : IDisposable
{
    public const int PageSize = 8;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly Guid _lifetime = Guid.NewGuid();
    private readonly ISocialBackend _backend;
    private readonly ConcurrentQueue<(int Generation, SocialData? Data, SocialRow? Lookup, SocialBackendResult? Action, string? Error)> _completed = new();
    private readonly object _completionLock = new();
    private readonly Queue<SocialJoinRequest> _joins = new();
    private CancellationTokenSource? _request;
    private SocialData _data;
    private SocialRow? _lookup;
    private SocialViewSnapshot _snapshot = new();
    private SocialConfirmation? _confirmation;
    private SocialTab _tab;
    private string _filter = "", _selected = "", _status = "", _error = "";
    private bool _busy, _cancellable, _disposed;
    private int _generation, _changed, _page;
    private ulong _revision = 1, _version;

    public SocialController(ISocialBackend? backend = null)
    {
#if !MPHREAD_SERVER
        _backend = backend ?? new LauncherSocialBackend();
#else
        _backend = backend ?? throw new InvalidOperationException("Social requires a graphical client backend.");
#endif
        _data = _backend.Current(); _status = _data.Status;
        _backend.Changed += OnChanged;
        Publish();
    }
    public SocialViewSnapshot Snapshot { get { EnsureOwner(); return _snapshot; } }
    private void OnChanged() => Interlocked.Exchange(ref _changed, 1);

    public void Pump()
    {
        EnsureOwner(); if (_disposed) return;
        if (Interlocked.Exchange(ref _changed, 0) != 0) Adopt(_backend.Current());
        else
        {
            SocialData current = _backend.Current();
            if (current.IsSessionActive != _data.IsSessionActive || current.CanInviteToLobby != _data.CanInviteToLobby)
                Adopt(current);
        }
        while (_completed.TryDequeue(out var item))
        {
            if (item.Generation != _generation) { item.Action?.Join?.Dispose(); continue; }
            _busy = _cancellable = false;
            if (item.Error is { } error) { _error = _status = error; continue; }
            if (item.Data is { } data) { Adopt(data); _status = data.Status; }
            if (item.Lookup is { } lookup) { _lookup = lookup; _tab = SocialTab.Players; _page = 0; _selected = lookup.Key; _revision++; _status = "PRIME ID FOUND"; }
            if (item.Action is { } action)
            {
                if (action.Data is { } resultData) Adopt(resultData);
                _status = Friendly(action.Status); _error = action.Success ? "" : _status;
                if (action.Join is { } join)
                {
                    if (action.Success && !_backend.Current().IsSessionActive) _joins.Enqueue(join);
                    else { join.Dispose(); _error = _status = "Leave your current session before joining another lobby."; }
                }
            }
        }
        Publish();
    }

    public SocialActionResult Refresh() => Act(() =>
    {
        if (_busy && !_cancellable) return Reject("A Social action is already in progress.");
        CancelRequest(); _confirmation = null; Begin("SYNCING SOCIAL", true, async token => (await _backend.LoadAsync(token).ConfigureAwait(false), null, null));
        return SocialActionResult.Ok;
    });
    public SocialActionResult Cancel() => Act(() =>
    {
        if (_busy && !_cancellable) return Reject("This action has already been sent. Wait for its result.");
        CancelRequest(); _status = "SOCIAL REQUEST CANCELLED"; return SocialActionResult.Ok;
    });
    public SocialActionResult SetTab(SocialTab tab) => Act(() =>
    {
        if (!Enum.IsDefined(tab)) return Reject("Unknown Social section.");
        _tab = tab; _page = 0; _selected = ""; _lookup = null; _confirmation = null; return SocialActionResult.Ok;
    });
    public SocialActionResult SetFilter(string filter) => Act(() =>
    {
        if (filter.Length > 48) return Reject("Social search accepts at most 48 characters.");
        _filter = filter.Trim(); _page = 0; _selected = ""; _confirmation = null; return SocialActionResult.Ok;
    });
    public SocialActionResult Lookup(string primeId) => Act(() =>
    {
        primeId = primeId.Trim().ToUpperInvariant();
        if (!ValidPrimeId(primeId)) return Reject("Enter a full Prime ID such as PP-1234-5678-9ABC-DEF0-1234.");
        if (_busy) return Reject("A Social action is already in progress.");
        _lookup = null;
        Begin("LOOKING UP PRIME ID", true, async token =>
        {
            SocialRow? found = await _backend.LookupAsync(primeId, token).ConfigureAwait(false);
            if (found == null) throw new InvalidOperationException("Prime ID was not found.");
            return (null, found, null);
        });
        return SocialActionResult.Ok;
    });
    public SocialActionResult ChangePage(int delta) => Act(() =>
    {
        if (delta is not (-1 or 1) || _page + delta < 0 || _page + delta >= _snapshot.PageCount)
            return Reject("No more Social rows on this page.");
        _page += delta; _selected = ""; _confirmation = null; return SocialActionResult.Ok;
    });
    public SocialActionResult SelectRow(int visibleIndex) => Act(() =>
    {
        if (visibleIndex < 0 || visibleIndex >= _snapshot.VisibleRows.Length) return Reject("That Social row is no longer displayed.");
        _selected = _snapshot.VisibleRows[visibleIndex].Key; _confirmation = null; return SocialActionResult.Ok;
    });
    public SocialIntent Intent(SocialCommand command)
    {
        EnsureOwner();
        return new(_lifetime, _revision, command, _selected, _data.Party?.PartyId ?? "",
            _data.Travel?.TravelId ?? "", _data.Travel?.Revision ?? 0, _data.Reservation?.RequestId ?? "");
    }
    public SocialActionResult Dispatch(SocialIntent intent) => Act(() => DispatchCore(intent, false));
    public SocialActionResult Confirm() => Act(() =>
    {
        if (_confirmation is not { } confirmation) return Reject("No Social action needs confirmation.");
        _confirmation = null; return DispatchCore(confirmation.Intent, true);
    });
    public SocialActionResult DismissConfirmation() => Act(() => { _confirmation = null; return SocialActionResult.Ok; });
    public SocialActionResult SetPrivacy(SocialPrivacy privacy) => Act(() =>
    {
        if (privacy.Presence is < 0 or > 2 || privacy.Activity is < 0 or > 2 || privacy.Invites is < 0 or > 2)
            return Reject("Unknown Social privacy option.");
        _backend.SavePrivacy(privacy); Adopt(_backend.Current()); _status = "PRIVACY SAVED; SYNCING"; return SocialActionResult.Ok;
    });
    public bool TryTakeJoin(out SocialJoinRequest? request)
    {
        EnsureOwner(); request = _joins.Count > 0 ? _joins.Dequeue() : null; return request != null;
    }

    private SocialActionResult DispatchCore(SocialIntent intent, bool confirmed)
    {
        if (Interlocked.Exchange(ref _changed, 0) != 0) Adopt(_backend.Current());
        else
        {
            SocialData current = _backend.Current();
            if (current.IsSessionActive != _data.IsSessionActive || current.CanInviteToLobby != _data.CanInviteToLobby) Adopt(current);
        }
        if (!Enum.IsDefined(intent.Command)) return Reject("Unknown Social command.");
        if (intent.Lifetime != _lifetime || intent.DataRevision != _revision
            || intent.PartyId != (_data.Party?.PartyId ?? "") || intent.TravelId != (_data.Travel?.TravelId ?? "")
            || intent.TravelRevision != (_data.Travel?.Revision ?? 0) || intent.ReservationId != (_data.Reservation?.RequestId ?? ""))
            return Reject("Social state changed. Review the current player or party and try again.");
        if (_busy) return Reject("A Social action is already in progress.");
        SocialRow? target = Rows().FirstOrDefault(row => row.Key == intent.TargetKey);
        if (!Allowed(intent.Command, target)) return Reject("This Social action is unavailable for the selected player or party.");
        if (NeedsConfirmation(intent.Command) && !confirmed)
        { _confirmation = new(intent, Friendly(intent.Command.ToString()) + (target == null ? "?" : " — " + target.DisplayName + "?")); return SocialActionResult.Ok; }
        _confirmation = null;
        Begin("SENDING SOCIAL ACTION", IsJoin(intent.Command), async token => (null, null, await _backend.ExecuteAsync(intent, target, token).ConfigureAwait(false)));
        return SocialActionResult.Ok;
    }
    private bool Allowed(SocialCommand command, SocialRow? target)
    {
        if (!_data.Connected) return false;
        if (target?.ExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow) return false;
        return command switch
        {
            SocialCommand.KickPartyMember or SocialCommand.PromotePartyMember => _data.Party?.IsLeader == true
                && target?.Allows(command) == true && target.PrimeId != _data.SelfPrimeId && target.PrimeId != _data.Party.LeaderPrimeId,
            SocialCommand.SendGameInvite => _data.CanInviteToLobby && target?.Allows(command) == true,
            SocialCommand.InviteToParty => (_data.Party == null || _data.Party is { IsLeader: true, MemberCount: < 8 }) && target?.Allows(command) == true,
            SocialCommand.LeaveParty => _data.Party != null,
            SocialCommand.DisbandParty => _data.Party?.IsLeader == true,
            SocialCommand.InvitePartyToLobby => _data.Party != null && _data.CanInviteToLobby,
            SocialCommand.FollowPartyTravel => !_data.IsSessionActive && _data.Travel is { IsLeader: false } travel
                && travel.ExpiresAt > DateTimeOffset.UtcNow && travel.SelfStatus is "pending" or "joined",
            SocialCommand.JoinPartyLeader => !_data.IsSessionActive && _data.Party is { IsLeader: false },
            SocialCommand.DeclinePartyTravel => _data.Travel is { IsLeader: false } travel
                && travel.ExpiresAt > DateTimeOffset.UtcNow && travel.SelfStatus == "pending",
            SocialCommand.CancelReservation => _data.Party?.IsLeader == true && _data.Reservation != null,
            _ => target?.Allows(command) == true && (!IsJoin(command) || !_data.IsSessionActive)
        };
    }
    private static bool IsJoin(SocialCommand command) => command is SocialCommand.JoinFriend or SocialCommand.AcceptGameInvite or SocialCommand.FollowPartyTravel or SocialCommand.JoinPartyLeader;
    private static bool NeedsConfirmation(SocialCommand command) => command is SocialCommand.RemoveFriend or SocialCommand.BlockPlayer
        or SocialCommand.LeaveParty or SocialCommand.DisbandParty or SocialCommand.KickPartyMember or SocialCommand.PromotePartyMember;
    private void Adopt(SocialData data) { _data = data; _revision++; }
    private void Begin(string status, bool cancellable, Func<CancellationToken, Task<(SocialData?, SocialRow?, SocialBackendResult?)>> operation)
    {
        _request?.Dispose(); _request = new(); int generation = ++_generation;
        _busy = true; _cancellable = cancellable; _status = status; CancellationToken token = _request.Token;
        _ = RunAsync();
        async Task RunAsync()
        {
            SocialBackendResult? result = null;
            try
            {
                var data = await operation(token).ConfigureAwait(false); result = data.Item3;
                Complete((generation, data.Item1, data.Item2, result, null));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { result?.Join?.Dispose(); }
            catch (Exception ex) { Complete((generation, null, null, null, ex.Message)); }
        }
    }
    private void Complete((int Generation, SocialData? Data, SocialRow? Lookup, SocialBackendResult? Action, string? Error) item)
    { lock (_completionLock) { if (_disposed) item.Action?.Join?.Dispose(); else _completed.Enqueue(item); } }
    private void CancelRequest() { _request?.Cancel(); _generation++; _busy = _cancellable = false; }
    private IEnumerable<SocialRow> Rows() => _lookup == null || _data.Rows.Any(row => row.Key == _lookup.Key)
        ? _data.Rows : _data.Rows.Add(_lookup);
    private void Publish()
    {
        var rows = Rows().Where(row => row.Tab == _tab && (_filter.Length == 0
            || row.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase) || row.PrimeId.Contains(_filter, StringComparison.OrdinalIgnoreCase)
            || row.Relation.Contains(_filter, StringComparison.OrdinalIgnoreCase) || row.Detail.Contains(_filter, StringComparison.OrdinalIgnoreCase))).ToImmutableArray();
        int pages = Math.Max(1, (rows.Length + PageSize - 1) / PageSize); _page = Math.Min(_page, pages - 1);
        var visible = rows.Skip(_page * PageSize).Take(PageSize).ToImmutableArray();
        SocialRow? selected = visible.FirstOrDefault(row => row.Key == _selected);
        if (selected == null) _selected = "";
        _snapshot = new() { Lifetime = _lifetime, Version = ++_version, DataRevision = _revision, Tab = _tab,
            Filter = _filter, PageIndex = _page, PageCount = pages, TotalRows = rows.Length, VisibleRows = visible,
            SelectedRow = selected, Data = _data, Busy = _busy, CanCancel = _busy && _cancellable,
            AvailableCommands = !_busy && !_disposed ? Enum.GetValues<SocialCommand>().Where(command => Allowed(command, selected)).ToImmutableArray() : ImmutableArray<SocialCommand>.Empty,
            Status = _status, CommandError = _error, PendingConfirmation = _confirmation };
    }
    private SocialActionResult Act(Func<SocialActionResult> action)
    {
        EnsureOwner(); if (_disposed) return new(false, "Social page was closed.");
        SocialActionResult result;
        try { result = action(); } catch (Exception ex) { result = new(false, ex.Message); }
        _error = result.Success ? "" : result.Message; Publish(); return result;
    }
    private static SocialActionResult Reject(string message) => new(false, message);
    private void EnsureOwner() { if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("SocialController belongs to its UI owner thread."); }
    public void Dispose()
    {
        EnsureOwner(); if (_disposed) return;
        lock (_completionLock) _disposed = true;
        _backend.Changed -= OnChanged; CancelRequest(); _request?.Dispose(); _backend.Dispose();
        while (_completed.TryDequeue(out var item)) item.Action?.Join?.Dispose();
        while (_joins.Count > 0) _joins.Dequeue().Dispose();
    }
    public static bool ValidPrimeId(string value)
    {
        if (value.Length != 27 || !value.StartsWith("PP-", StringComparison.OrdinalIgnoreCase)) return false;
        for (int i = 3; i < value.Length; i++)
            if (i is 7 or 12 or 17 or 22 ? value[i] != '-' : !Uri.IsHexDigit(value[i])) return false;
        return true;
    }
    private static string Friendly(string status) => status.Replace('_', ' ');
}
