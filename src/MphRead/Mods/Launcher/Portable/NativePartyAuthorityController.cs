using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

/// <summary>Only authoritative server snapshots may change party, invitation or presence state.</summary>
public enum NativePartyAction { Invite, AcceptInvite, DeclineInvite, CancelInvite, Leave, Kick, Promote, Disband }
public enum NativePresence { Unknown, Offline, Online, InLobby, InMatch }
public sealed record NativePartyMember(string PrimeId, string DisplayName, NativePresence Presence);
public sealed record NativePartyInvite(string InviteId, string PartyId, string FromPrimeId,
    string ToPrimeId, DateTimeOffset ExpiresAt);
public sealed record NativePartySnapshot(string SelfPrimeId, string PartyId, string LeaderPrimeId,
    long ServerRevision, NativePartyMember[] Members, NativePartyInvite[] Incoming,
    NativePartyInvite[] Outgoing)
{
    public NativePartyTravel? Travel { get; init; }
    public NativePartyReservation? Reservation { get; init; }
    [JsonIgnore]
    internal object? AccountWitness { get; init; }
}
public sealed record NativePartyCommand(NativePartyAction Action, long ExpectedRevision,
    string PartyId, string TargetPrimeId, string InviteId, string ExpectedSelfPrimeId = "");
public interface INativePartyAuthority
{
    bool IsCurrent(NativePartySnapshot snapshot) => true;
    Task<NativePartySnapshot> ReadAsync(CancellationToken cancellation);
    Task<bool> ExecuteAsync(NativePartyCommand command, CancellationToken cancellation);
}
public sealed record NativePartyView(bool Connected, bool Busy, string Status,
    long Revision, NativePartySnapshot? Snapshot);

/// <summary>
/// An opt-in UI-thread projection for the authenticated Party edge service.
/// No guessed membership, online state, invitation, local optimistic write, or
/// direct network session mutation. All commands are guarded by exact snapshot
/// revision and reread after server acceptance.
/// </summary>
public sealed class NativePartyAuthorityController : IDisposable
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly INativePartyAuthority _authority;
    private readonly ConcurrentQueue<(int Generation, NativePartyTravelResult? Result, bool Failed)> _results = new();
    private CancellationTokenSource? _cancel;
    private int _generation;
    private long _revision = 1;
    private NativePartySnapshot? _snapshot;
    private NativePartySnapshot? _accountSnapshot;
    private NativePartyJoinTarget? _joinTarget;
    private bool _busy, _disposed;
    private string _status = "PARTY SERVICE NOT CONNECTED";

    public NativePartyAuthorityController(INativePartyAuthority authority)
        => _authority = authority ?? throw new ArgumentNullException(nameof(authority));

    private void CheckOwner()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativePartyAuthorityController));
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Party projection must run on the UI owner thread.");
        if (_accountSnapshot != null && !_authority.IsCurrent(_accountSnapshot)) RetireAccount();
    }

    private void RetireAccount()
    {
        Retire();
        _snapshot = _accountSnapshot = null;
        _busy = false;
        _status = "PARTY ACCOUNT CHANGED // REFRESH TO VERIFY";
        ++_revision;
    }

    private void Retire()
    {
        _joinTarget = null;
        ++_generation;
        try { _cancel?.Cancel(); } catch (ObjectDisposedException) { }
        _cancel = null;
    }

    private void Start(Func<CancellationToken, Task<NativePartyTravelResult>> work, string status)
    {
        Retire();
        int generation = _generation;
        var cancellation = new CancellationTokenSource();
        _cancel = cancellation;
        _busy = true;
        _status = status;
        _revision++;
        _ = Task.Run(async () =>
        {
            try
            {
                NativePartyTravelResult result = await work(cancellation.Token).ConfigureAwait(false);
                if (!cancellation.IsCancellationRequested)
                    _results.Enqueue((generation, result, false));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                if (!cancellation.IsCancellationRequested)
                    _results.Enqueue((generation, null, true));
            }
            finally { cancellation.Dispose(); }
        });
    }

    public void Refresh(bool preserveCurrent = false)
    {
        CheckOwner();
        // CheckOwner retires any prior account before a quiet Home refresh.
        // Explicit page refreshes keep the original clear-while-verifying policy.
        if (!preserveCurrent) _snapshot = _accountSnapshot = null;
        Start(async token => new(await _authority.ReadAsync(token).ConfigureAwait(false), null), "VERIFYING PARTY SERVICE");
    }

    public bool Command(NativePartyAction action, string targetPrimeId = "", string inviteId = "")
    {
        CheckOwner();
        if (!Can(action, targetPrimeId, inviteId) || _snapshot is not { } state) return false;
        string party = action is NativePartyAction.AcceptInvite or NativePartyAction.DeclineInvite
            ? state.Incoming.Single(x => x.InviteId == inviteId).PartyId : state.PartyId;
        var command = new NativePartyCommand(action, state.ServerRevision, party,
            targetPrimeId.ToUpperInvariant(), inviteId, state.SelfPrimeId);
        // UI permissions are advisory. The server independently authorizes
        // the captured caller/party/revision/invitation atomically.
        _snapshot = null;
        Start(async token =>
        {
            if (!await _authority.ExecuteAsync(command, token).ConfigureAwait(false))
                throw new InvalidOperationException("Party mutation rejected.");
            token.ThrowIfCancellationRequested();
            NativePartySnapshot next = await _authority.ReadAsync(token).ConfigureAwait(false);
            if (!next.SelfPrimeId.Equals(command.ExpectedSelfPrimeId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Party account changed.");
            return new(next, null);
        }, "VERIFYING PARTY CHANGE");
        return true;
    }

    public bool CanTravel(NativePartyTravelAction action, NativePartyLocalLobby? local = null)
    {
        CheckOwner();
        if (!Enum.IsDefined(action) || _busy || _snapshot is not { } state || _authority is not INativePartyTravelAuthority
            || state.PartyId.Length == 0 || state.Members.Length < 2) return false;
        bool leader = state.LeaderPrimeId == state.SelfPrimeId;
        return action switch
        {
            NativePartyTravelAction.Publish => leader && local is { MatchId: > 0, AuthorityEpoch: > 0, RoomKey.Length: > 0 and <= 128 },
            NativePartyTravelAction.Clear => leader && state.Travel != null,
            NativePartyTravelAction.Reserve => leader && state.Reservation is not { Active: true } && state.Travel is { } destination
                && destination.ExpiresAt > DateTimeOffset.UtcNow && local is { MatchId: > 0, AuthorityEpoch: > 0 }
                && local.AuthorityEpoch == destination.AuthorityEpoch && local.RoomKey == destination.RoomKey,
            NativePartyTravelAction.CancelReservation => state.Reservation is { Active: true } reservation
                && reservation.ExpiresAt > DateTimeOffset.UtcNow && (leader || reservation.Status == "reserved"),
            _ => !leader && state.Travel is { } travel && travel.ExpiresAt > DateTimeOffset.UtcNow
        };
    }

    public bool Travel(NativePartyTravelAction action, NativePartyLocalLobby? local = null)
    {
        CheckOwner();
        if (!CanTravel(action, local) || _snapshot is not { } state
            || _authority is not INativePartyTravelAuthority authority) return false;
        var command = new NativePartyTravelCommand(action, state.SelfPrimeId, state.PartyId,
            state.ServerRevision, state.Travel?.TravelId ?? "", state.Travel?.Revision ?? 0, local)
            { AccountWitness = state.AccountWitness, Reservation = state.Reservation };
        _snapshot = null;
        Start(async token =>
        {
            NativePartyTravelResult result = await authority.TravelAsync(command, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (result.Snapshot.SelfPrimeId != command.ExpectedSelfPrimeId || result.Snapshot.PartyId != command.PartyId
                || result.Snapshot.ServerRevision != command.ExpectedRevision)
                throw new InvalidOperationException("Party identity or revision changed.");
            return result;
        }, "VERIFYING PARTY TRAVEL");
        return true;
    }

    public bool TryTakeJoin(out NativePartyJoinTarget target)
    {
        CheckOwner();
        target = _joinTarget!;
        _joinTarget = null;
        return target != null && _snapshot != null && _authority.IsCurrent(_snapshot)
            && NativePartyTravelValidation.ValidTarget(target, DateTimeOffset.UtcNow);
    }

    public bool Can(NativePartyAction action, string targetPrimeId = "", string inviteId = "")
    {
        CheckOwner();
        if (_busy || _snapshot is not { } state || state.ServerRevision < 0) return false;
        bool owner = state.LeaderPrimeId.Equals(state.SelfPrimeId, StringComparison.OrdinalIgnoreCase);
        bool member = state.Members.Any(x => x.PrimeId.Equals(state.SelfPrimeId, StringComparison.OrdinalIgnoreCase));
        bool targetMember = state.Members.Any(x => x.PrimeId.Equals(targetPrimeId, StringComparison.OrdinalIgnoreCase));
        bool validTarget = NativeSocialFriendsController.ValidPrimeId(targetPrimeId)
            && !targetPrimeId.Equals(state.SelfPrimeId, StringComparison.OrdinalIgnoreCase);
        bool allowed = action switch
        {
            NativePartyAction.Invite => (state.PartyId.Length == 0 || owner && member)
                && validTarget && !targetMember && state.Members.Length < 8,
            NativePartyAction.AcceptInvite or NativePartyAction.DeclineInvite =>
                state.Incoming.Any(x => x.InviteId == inviteId && x.PartyId.Length > 0
                    && x.ToPrimeId.Equals(state.SelfPrimeId, StringComparison.OrdinalIgnoreCase)
                    && x.ExpiresAt > DateTimeOffset.UtcNow),
            NativePartyAction.CancelInvite => owner && state.Outgoing.Any(x =>
                x.InviteId == inviteId && x.PartyId == state.PartyId
                && x.FromPrimeId.Equals(state.SelfPrimeId, StringComparison.OrdinalIgnoreCase)),
            NativePartyAction.Leave => member,
            NativePartyAction.Disband => owner && member,
            NativePartyAction.Kick or NativePartyAction.Promote => owner && member && targetMember
                && validTarget,
            _ => false
        };
        return allowed;
    }

    public static bool ValidSnapshot(NativePartySnapshot? state)
    {
        if (state == null || !NativeSocialFriendsController.ValidPrimeId(state.SelfPrimeId)
            || state.ServerRevision < 0 || state.ServerRevision > 9007199254740991L
            || state.PartyId == null || state.LeaderPrimeId == null
            || state.Members == null || state.Incoming == null || state.Outgoing == null
            || state.Members.Length > 8 || state.Incoming.Length > 256 || state.Outgoing.Length > 256)
            return false;
        bool Eq(string a, string b) => String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        bool party = state.PartyId.Length != 0;
        if (party ? !Guid.TryParse(state.PartyId, out _) || state.Members.Length == 0
            || !NativeSocialFriendsController.ValidPrimeId(state.LeaderPrimeId)
            || !state.Members.Any(m => m != null && Eq(m.PrimeId, state.SelfPrimeId))
            || !state.Members.Any(m => m != null && Eq(m.PrimeId, state.LeaderPrimeId))
            : state.Members.Length != 0 || state.LeaderPrimeId.Length != 0)
            return false;
        if (state.Members.Any(m => m == null || !NativeSocialFriendsController.ValidPrimeId(m.PrimeId)
            || String.IsNullOrWhiteSpace(m.DisplayName) || m.DisplayName.Length > 128
            || !Enum.IsDefined(m.Presence))
            || state.Members.Select(m => m.PrimeId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Members.Length)
            return false;
        if (state.Travel is { } travel && (!party || !Guid.TryParse(travel.TravelId, out _)
            || !Guid.TryParse(travel.LobbyId, out _) || travel.Revision is < 1 or > 1000000
            || !Eq(travel.LeaderPrimeId, state.LeaderPrimeId) || travel.IsLeader != Eq(state.SelfPrimeId, state.LeaderPrimeId)
            || travel.AuthorityEpoch == 0 || travel.RoomKey is not { Length: > 0 and <= 128 }
            || travel.ServerName is not { Length: <= 96 } || travel.SelfStatus is not ("pending" or "following" or "joined" or "declined"))) return false;
        if (state.Reservation is { } reservation && (!party || state.Travel is not { } destination
            || !NativePartyReservationValidation.Valid(reservation) || reservation.TravelId != destination.TravelId
            || reservation.TravelRevision != destination.Revision || reservation.AuthorityEpoch != destination.AuthorityEpoch)) return false;
        bool Invites(NativePartyInvite[] rows, bool incoming) => rows.All(i => i != null
            && Guid.TryParse(i.InviteId, out _) && Guid.TryParse(i.PartyId, out _)
            && NativeSocialFriendsController.ValidPrimeId(i.FromPrimeId)
            && NativeSocialFriendsController.ValidPrimeId(i.ToPrimeId)
            && !Eq(i.FromPrimeId, i.ToPrimeId)
            && Eq(incoming ? i.ToPrimeId : i.FromPrimeId, state.SelfPrimeId)
            && (incoming || party && Eq(i.PartyId, state.PartyId)))
            && rows.Select(i => i.InviteId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == rows.Length;
        return Invites(state.Incoming, true) && Invites(state.Outgoing, false)
            && !state.Incoming.Select(i => i.InviteId).Intersect(state.Outgoing.Select(i => i.InviteId),
                StringComparer.OrdinalIgnoreCase).Any();
    }

    public NativePartyView Tick()
    {
        CheckOwner();
        while (_results.TryDequeue(out var item))
        {
            if (item.Generation != _generation) continue;
            _busy = false;
            _cancel = null;
            NativePartySnapshot? state = item.Result?.Snapshot;
            bool valid = !item.Failed && ValidSnapshot(state) && _authority.IsCurrent(state!);
            _snapshot = valid ? state : null;
            _accountSnapshot = _snapshot;
            _joinTarget = valid && item.Result?.JoinTarget is { } target
                && NativePartyTravelValidation.ValidTarget(target, DateTimeOffset.UtcNow)
                && target.SelfPrimeId == state!.SelfPrimeId && target.PartyId == state.PartyId
                && target.PartyRevision == state.ServerRevision ? target : null;
            _status = valid ? "SERVER-VERIFIED PARTY STATE" : "PARTY UNAVAILABLE // VERIFY ACCOUNT AND SERVICE";
            ++_revision;
        }
        if (_snapshot?.Travel is { } travel && travel.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _snapshot = _snapshot with { Travel = null, Reservation = null };
            _accountSnapshot = _snapshot;
            _joinTarget = null;
            ++_revision;
        }
        if (_snapshot?.Reservation is { } reservation && reservation.ExpiresAt <= DateTimeOffset.UtcNow)
        { _snapshot = _snapshot with { Reservation = null }; _accountSnapshot = _snapshot; ++_revision; }
        return new(_snapshot != null, _busy, _status, _revision, _snapshot);
    }

    public void Cancel()
    {
        CheckOwner();
        Retire();
        _snapshot = _accountSnapshot = null;
        _busy = false;
        _status = "PARTY REQUEST CANCELLED";
        ++_revision;
    }

    public void Dispose()
    {
        if (_disposed) return;
        CheckOwner();
        Retire();
        _snapshot = _accountSnapshot = null;
        _disposed = true;
        while (_results.TryDequeue(out _)) { }
    }
}
