using System.Collections.Concurrent;
using FruityPrime.Server.Node.Lobbies;
using FruityPrime.Server.Node.Workers;
using FruityPrime.Server.Shared;
using MphRead;

namespace FruityPrime.WorkerSoak;

/// <summary>Tracks active lobby identities by lease instance rather than by value.
/// A late rollback must not remove IDs that have already been re-rented to another
/// lease with the same participant values.</summary>
public sealed class SoakActiveIdentityRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, SoakIdentityLease> _owners = [];

    public int Count
    {
        get { lock (_gate) return _owners.Count; }
    }

    public bool TryClaim(SoakIdentityLease lease, IEnumerable<Guid> identityIds)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var ids = identityIds.ToArray();
        if (ids.Length == 0 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length) return false;
        lock (_gate)
        {
            if (ids.Any(id => _owners.TryGetValue(id, out var owner) && !ReferenceEquals(owner, lease))) return false;
            foreach (Guid id in ids) _owners[id] = lease;
            return true;
        }
    }

    public bool TryRelease(SoakIdentityLease lease, IEnumerable<Guid> identityIds)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var ids = identityIds.ToArray();
        if (ids.Length == 0 || ids.Distinct().Count() != ids.Length) return false;
        lock (_gate)
        {
            if (ids.Any(id => !_owners.TryGetValue(id, out var owner) || !ReferenceEquals(owner, lease))) return false;
            foreach (Guid id in ids) _owners.Remove(id);
            return true;
        }
    }
}

/// <summary>Actual Node lobby authority workload; WorkerScheduler is the sole Worker event reader.</summary>
internal sealed class SoakLobbyDriver : IDisposable
{
    internal sealed record HistoryPoint(string Operation, long Revision, LobbyPhase Phase, string Map, Guid? MatchId);
    internal sealed class Round(MatchSpec spec, MatchPlacement placement, LobbyIdentity owner,
        IReadOnlyList<LobbyIdentity> players, IReadOnlyList<LobbyIdentity> observers,
        SoakIdentityLease identityLease, Task<bool> completion, List<HistoryPoint> history)
    {
        public MatchSpec Spec { get; } = spec;
        public MatchPlacement Placement { get; } = placement;
        public Guid LobbyId => Spec.LobbyId.Value;
        public LobbyIdentity Owner { get; } = owner;
        public IReadOnlyList<LobbyIdentity> Players { get; } = players;
        public IReadOnlyList<LobbyIdentity> Observers { get; } = observers;
        public SoakIdentityLease IdentityLease { get; } = identityLease;
        public Task<bool> Completion { get; } = completion;
        public IReadOnlyList<HistoryPoint> History => history.ToArray();
        internal List<HistoryPoint> MutableHistory => history;
    }

    private readonly LobbyManager _lobbies = new();
    private readonly NodeMatchCoordinator _coordinator;
    private readonly WorkerScheduler _scheduler;
    private readonly ConcurrentDictionary<MatchId, TaskCompletionSource<bool>> _completions = [];
    private readonly Func<int, int, CancellationToken, Task<SoakIdentityLease>>? _identities;
    private readonly Action<SoakIdentityLease>? _releaseIdentities;
    private readonly SoakRosterOptions _roster;
    private readonly SoakActiveIdentityRegistry _activePlayers = new();

    public SoakLobbyDriver(WorkerScheduler scheduler, WorkerManager manager, WorkerAdmissionIssuer signer,
        NodeContentCatalog catalog, SoakRosterOptions roster,
        Func<int, int, CancellationToken, Task<SoakIdentityLease>>? identities = null,
        Action<SoakIdentityLease>? releaseIdentities = null)
    {
        roster.Validate();
        _scheduler = scheduler; _roster = roster;
        _identities = identities; _releaseIdentities = releaseIdentities;
        _coordinator = new(_lobbies, scheduler, manager, signer, catalog);
        _scheduler.Ended += OnEnded;
    }

    public async Task<Round> StartAsync(string map, MatchMode mode, int seconds, CancellationToken cancellationToken)
    {
        if (_identities == null) throw new InvalidOperationException("The Node-domain soak requires Backend-seeded participant identities.");
        SoakIdentityLease lease = await _identities(_roster.Players, _roster.Observers, cancellationToken);
        var players = lease.PlayerIds.Select((id, index) => new LobbyIdentity(Guid.NewGuid(), id, "SoakHuman" + index.ToString("D2"))).ToArray();
        var observers = lease.ObserverIds.Select((id, index) => new LobbyIdentity(Guid.NewGuid(), id, "SoakObserver" + index.ToString("D2"))).ToArray();
        var identities = players.Concat(observers).ToArray();
        if (!_activePlayers.TryClaim(lease, identities.Select(identity => identity.PlayerId)))
        {
            _releaseIdentities?.Invoke(lease);
            throw new InvalidOperationException("Soak identity pool returned an empty or already active participant.");
        }
        var owner = players[0];
        var history = new List<HistoryPoint>();
        try
        {
            var snapshot = (LobbySnapshot)_lobbies.Execute(owner, new LobbyCreate("Soak lobby", LobbyVisibility.Public,
                _roster.ActiveSeats, _roster.Observers));
            Record(history, "create", snapshot);
            foreach (var player in players.Skip(1))
            {
                snapshot = (LobbySnapshot)_lobbies.Execute(player, new LobbyJoin(snapshot.LobbyId, snapshot.Revision, false));
                Record(history, "player.join", snapshot);
            }
            foreach (var observer in observers)
            {
                snapshot = (LobbySnapshot)_lobbies.Execute(observer, new LobbyJoin(snapshot.LobbyId, snapshot.Revision, true));
                Record(history, "observer.join", snapshot);
            }
            return await StartOnLobby(owner, players, observers, lease, map, mode, seconds, history, cancellationToken);
        }
        catch
        {
            DisconnectAll(players, observers);
            _coordinator.ReconcileMembership();
            Release(lease, players, observers);
            throw;
        }
    }

    /// <summary>Completes one durable round, reopens the same lobby, and starts one fresh round
    /// using the same sessions and identity lease. The old scheduler placement is forgotten before
    /// the next CreateMatch is issued.</summary>
    public async Task<Round> RematchAsync(Round previous, string map, MatchMode mode, int seconds,
        CancellationToken cancellationToken)
    {
        Round? replacement = null;
        try
        {
            await CompleteAndReturnAsync(previous, cancellationToken, leave: false);
            if (!_scheduler.ForgetMatch(previous.Spec.MatchId))
                throw new InvalidOperationException("Completed rematch still owns its prior scheduler placement.");
            replacement = await StartOnLobby(previous.Owner, previous.Players, previous.Observers, previous.IdentityLease,
                map, mode, seconds, previous.MutableHistory, cancellationToken);
            Require(replacement.LobbyId == previous.LobbyId, "Rematch replaced the lobby identity.");
            Require(replacement.Spec.MatchId != previous.Spec.MatchId &&
                (replacement.Placement.WorkerId != previous.Placement.WorkerId
                    || replacement.Placement.WorkerIncarnation != previous.Placement.WorkerIncarnation
                    || replacement.Placement.WireMatchId != previous.Placement.WireMatchId),
                "Rematch reused an old gameplay identity.");
            Require(replacement.Players.Select(member => member.SessionId).SequenceEqual(previous.Players.Select(member => member.SessionId))
                && replacement.Observers.Select(member => member.SessionId).SequenceEqual(previous.Observers.Select(member => member.SessionId)),
                "Rematch changed the participant session set.");
            return replacement;
        }
        catch
        {
            if (replacement is { } failedReplacement)
                _scheduler.CancelMatch(failedReplacement.Spec.MatchId, "Rematch verification failed.");
            // A rematch owns the old lease until its replacement is fully admitted. If
            // any step fails, leave every session and reconcile before releasing it.
            DisconnectAll(previous.Players, previous.Observers);
            _coordinator.ReconcileMembership();
            if (replacement is { } replacementToForget)
            {
                try { await replacementToForget.Completion.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (TimeoutException) { }
                _scheduler.ForgetMatch(replacementToForget.Spec.MatchId);
            }
            _scheduler.ForgetMatch(previous.Spec.MatchId);
            Release(previous.IdentityLease, previous.Players, previous.Observers);
            throw;
        }
    }

    private async Task<Round> StartOnLobby(LobbyIdentity owner, IReadOnlyList<LobbyIdentity> players,
        IReadOnlyList<LobbyIdentity> observers, SoakIdentityLease lease, string map, MatchMode mode,
        int seconds, List<HistoryPoint> history, CancellationToken cancellationToken)
    {
        var snapshot = _lobbies.ForSession(owner.SessionId) ?? throw new InvalidOperationException("Soak lobby disappeared.");
        snapshot = (LobbySnapshot)_lobbies.Execute(owner, new LobbyConfigure(snapshot.Revision, map, mode,
            BotCount: _roster.Bots, TimeLimitSeconds: seconds));
        Record(history, "configure", snapshot);
        for (int index = 0; index < players.Count; index++)
        {
            var player = players[index];
            snapshot = (LobbySnapshot)_lobbies.Execute(player, new LobbySelectHunter(index == 0 ? Hunter.Kanden : Hunter.Samus, snapshot.Revision));
            Record(history, "hunter", snapshot);
            snapshot = (LobbySnapshot)_lobbies.Execute(player, new LobbySetReady(true, snapshot.Revision));
            Record(history, "ready", snapshot);
        }
        snapshot = (LobbySnapshot)await _coordinator.ExecuteAsync(owner, new LobbyStart(snapshot.Revision)).WaitAsync(cancellationToken);
        Record(history, "start", snapshot);
        Require(snapshot.Phase == LobbyPhase.InMatch && snapshot.CurrentMatchId.HasValue, "Lobby did not enter InMatch.");
        var id = new MatchId(snapshot.CurrentMatchId!.Value);
        if (!_scheduler.TryGetAssignment(id, out var assignment) || assignment?.Placement == null)
            throw new InvalidOperationException("Coordinator started without a scheduler placement.");
        var spec = assignment.Spec;
        Require(spec.Roster.Count(s => s.Role == SeatRole.Bot) == _roster.Bots
            && spec.Roster.Count(s => s.Role == SeatRole.Player) == _roster.Players
            && spec.Roster.Count(s => s.Role == SeatRole.Observer) == _roster.Observers,
            "Frozen soak roster does not contain the requested roles.");
        Require(spec.Content.MapKey == map && spec.Rules.Mode == mode && spec.Rules.TimeLimit == TimeSpan.FromSeconds(seconds),
            "Frozen map/mode/time differs from the authoritative lobby settings.");
        var handoff = _coordinator.ForSession(owner.SessionId) as NodeMatchHandoff;
        Require(handoff?.MatchId == id.Value && handoff.WireMatchId == assignment.Placement.WireMatchId.Value,
            "Node did not mint the owner's routed handoff.");
        Task<bool> completion = _completions.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        return new(spec, assignment.Placement, owner, players, observers, lease, completion, history);
    }

    public async Task CompleteAndReturnAsync(Round round, CancellationToken cancellationToken, bool leave = true)
    {
        bool interrupted = await round.Completion.WaitAsync(cancellationToken);
        var terminal = _lobbies.ForSession(round.Owner.SessionId) ?? throw new InvalidOperationException("Completion lost the lobby.");
        Require(terminal.Phase == LobbyPhase.PostMatch && terminal.CurrentMatchId == round.Spec.MatchId.Value,
            "Terminal Worker event did not return the owning lobby to PostMatch.");
        Record(round.MutableHistory, interrupted ? "interrupted" : "completed", terminal);
        var reopened = (LobbySnapshot)await _coordinator.ExecuteAsync(round.Owner, new LobbyRematch(terminal.Revision));
        Require(reopened.LobbyId == round.LobbyId && reopened.Phase == LobbyPhase.Open && reopened.CurrentMatchId == null
            && reopened.Members.All(m => !m.Ready), "Rematch did not reopen the same lobby with fresh readiness.");
        Record(round.MutableHistory, "rematch.open", reopened);
        _completions.TryRemove(round.Spec.MatchId, out _);
        if (!leave) return;
        DisconnectMembers(round);
        Release(round.IdentityLease, round.Players, round.Observers);
    }

    private void DisconnectMembers(Round round)
    {
        foreach (var member in round.Observers.Concat(round.Players.Skip(1)))
        {
            var snapshot = _lobbies.ForSession(member.SessionId);
            if (snapshot != null)
            {
                _lobbies.Execute(member, new LobbyLeave(snapshot.Revision));
                Record(round.MutableHistory, member.DisplayName + ".leave", _lobbies.ForSession(round.Owner.SessionId)!);
            }
        }
        var ownerSnapshot = _lobbies.ForSession(round.Owner.SessionId);
        if (ownerSnapshot != null)
        {
            _lobbies.Execute(round.Owner, new LobbyLeave(ownerSnapshot.Revision));
            _coordinator.ForgetSession(round.Owner.SessionId);
        }
        foreach (var member in round.Players.Concat(round.Observers)) _coordinator.ForgetSession(member.SessionId);
        Require(_lobbies.ForSession(round.Owner.SessionId) == null
            && round.Players.Concat(round.Observers).All(member => _lobbies.ForSession(member.SessionId) == null),
            "Leave leaked lobby membership.");
    }

    private void DisconnectAll(IEnumerable<LobbyIdentity> players, IEnumerable<LobbyIdentity> observers)
    {
        foreach (var member in observers.Concat(players).ToArray()) _lobbies.Disconnect(member.SessionId);
    }

    private void Release(SoakIdentityLease lease, IEnumerable<LobbyIdentity> players, IEnumerable<LobbyIdentity> observers)
    {
        var identities = players.Concat(observers).Select(identity => identity.PlayerId).ToArray();
        // A stale rollback is allowed to become a no-op. In particular, do not
        // invoke the Backend release callback after these IDs belong to a newer
        // lease, because that callback would otherwise release the newer lease.
        if (_activePlayers.TryRelease(lease, identities)) _releaseIdentities?.Invoke(lease);
    }

    private void OnEnded(MatchId id, bool interrupted)
        => _completions.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(interrupted);

    private static void Record(List<HistoryPoint> history, string operation, LobbySnapshot snapshot)
    {
        Require(history.Count == 0 || snapshot.Revision >= history[^1].Revision, "Lobby revision moved backwards.");
        history.Add(new(operation, snapshot.Revision, snapshot.Phase, snapshot.MapKey, snapshot.CurrentMatchId));
        if (history.Count > 256) history.RemoveAt(0);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public void Dispose() { _scheduler.Ended -= OnEnded; _coordinator.Dispose(); }
}
