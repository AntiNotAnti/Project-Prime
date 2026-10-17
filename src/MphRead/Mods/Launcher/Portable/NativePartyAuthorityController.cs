using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher;

/// <summary>Only authoritative server snapshots may change party, invitation or presence state.</summary>
public enum NativePartyAction { Invite, AcceptInvite, DeclineInvite, CancelInvite, Leave, Kick, Promote }
public enum NativePresence { Unknown, Offline, Online, InLobby, InMatch }
public sealed record NativePartyMember(string PrimeId, string DisplayName, NativePresence Presence);
public sealed record NativePartyInvite(string InviteId, string PartyId, string FromPrimeId,
    string ToPrimeId, DateTimeOffset ExpiresAt);
public sealed record NativePartySnapshot(string SelfPrimeId, string PartyId, string LeaderPrimeId,
    long ServerRevision, NativePartyMember[] Members, NativePartyInvite[] Incoming,
    NativePartyInvite[] Outgoing);
public sealed record NativePartyCommand(NativePartyAction Action, long ExpectedRevision,
    string PartyId, string TargetPrimeId, string InviteId);
public interface INativePartyAuthority
{
    Task<NativePartySnapshot> ReadAsync(CancellationToken cancellation);
    Task<bool> ExecuteAsync(NativePartyCommand command, CancellationToken cancellation);
}
public sealed record NativePartyView(bool Connected, bool Busy, string Status,
    long Revision, NativePartySnapshot? Snapshot);

/// <summary>
/// An opt-in UI-thread projection for the future authenticated Party edge service.
/// No guessed membership, online state, invitation, local optimistic write, or
/// direct network session mutation. All commands are guarded by exact snapshot
/// revision and reread after server acceptance.
/// </summary>
public sealed class NativePartyAuthorityController : IDisposable
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly INativePartyAuthority _authority;
    private readonly ConcurrentQueue<(int Generation, NativePartySnapshot? Snapshot, bool Failed)> _results = new();
    private CancellationTokenSource? _cancel;
    private int _generation;
    private long _revision = 1;
    private NativePartySnapshot? _snapshot;
    private bool _busy, _disposed;
    private string _status = "PARTY SERVICE NOT CONNECTED";

    public NativePartyAuthorityController(INativePartyAuthority authority)
        => _authority = authority ?? throw new ArgumentNullException(nameof(authority));

    private void CheckOwner()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativePartyAuthorityController));
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Party projection must run on the UI owner thread.");
    }

    private void Retire()
    {
        ++_generation;
        try { _cancel?.Cancel(); } catch (ObjectDisposedException) { }
        _cancel = null;
    }

    private void Start(Func<CancellationToken, Task<NativePartySnapshot>> work, string status)
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
                NativePartySnapshot snapshot = await work(cancellation.Token).ConfigureAwait(false);
                if (!cancellation.IsCancellationRequested)
                    _results.Enqueue((generation, snapshot, false));
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

    public void Refresh()
    {
        CheckOwner();
        // Never render a prior account's private party after a refresh or auth swap.
        _snapshot = null;
        Start(token => _authority.ReadAsync(token), "VERIFYING PARTY SERVICE");
    }

    public bool Command(NativePartyAction action, string targetPrimeId = "", string inviteId = "")
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
            NativePartyAction.Invite => owner && member && validTarget && !targetMember && state.Members.Length < 8,
            NativePartyAction.AcceptInvite or NativePartyAction.DeclineInvite =>
                state.Incoming.Any(x => x.InviteId == inviteId && x.PartyId.Length > 0
                    && x.ToPrimeId.Equals(state.SelfPrimeId, StringComparison.OrdinalIgnoreCase)
                    && x.ExpiresAt > DateTimeOffset.UtcNow),
            NativePartyAction.CancelInvite => owner && state.Outgoing.Any(x =>
                x.InviteId == inviteId && x.PartyId == state.PartyId
                && x.FromPrimeId.Equals(state.SelfPrimeId, StringComparison.OrdinalIgnoreCase)),
            NativePartyAction.Leave => member && !owner,
            NativePartyAction.Kick or NativePartyAction.Promote => owner && member && targetMember
                && validTarget,
            _ => false
        };
        if (!allowed) return false;
        var command = new NativePartyCommand(action, state.ServerRevision, state.PartyId,
            targetPrimeId.ToUpperInvariant(), inviteId);
        // UI permissions are advisory. The server must independently authorize
        // the exact caller/party/revision/invite and handle races atomically.
        _snapshot = null;
        Start(async token =>
        {
            if (!await _authority.ExecuteAsync(command, token).ConfigureAwait(false))
                throw new InvalidOperationException("Party mutation rejected.");
            return await _authority.ReadAsync(token).ConfigureAwait(false);
        }, "VERIFYING PARTY CHANGE");
        return true;
    }

    public NativePartyView Tick()
    {
        CheckOwner();
        while (_results.TryDequeue(out var item))
        {
            if (item.Generation != _generation) continue;
            _busy = false;
            _cancel = null;
            NativePartySnapshot? state = item.Snapshot;
            bool valid = !item.Failed && state != null
                && NativeSocialFriendsController.ValidPrimeId(state.SelfPrimeId)
                && state.ServerRevision >= 0 && state.Members != null
                && state.Incoming != null && state.Outgoing != null
                && state.Members.Length <= 8
                && state.Members.All(x => NativeSocialFriendsController.ValidPrimeId(x.PrimeId));
            _snapshot = valid ? state : null;
            _status = valid ? "SERVER-VERIFIED PARTY STATE" : "PARTY UNAVAILABLE // VERIFY ACCOUNT AND SERVICE";
            ++_revision;
        }
        return new(_snapshot != null, _busy, _status, _revision, _snapshot);
    }

    public void Cancel()
    {
        CheckOwner();
        Retire();
        _snapshot = null;
        _busy = false;
        _status = "PARTY REQUEST CANCELLED";
        ++_revision;
    }

    public void Dispose()
    {
        if (_disposed) return;
        CheckOwner();
        Retire();
        _snapshot = null;
        _disposed = true;
        while (_results.TryDequeue(out _)) { }
    }
}
