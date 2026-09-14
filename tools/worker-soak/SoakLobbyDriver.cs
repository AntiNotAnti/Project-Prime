using System.Collections.Concurrent;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using MphRead;

namespace ProjectPrime.WorkerSoak;

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
        public IReadOnlyList<LobbyIdentity> Participants { get; } = players.Concat(observers).ToArray();
        public SoakIdentityLease IdentityLease { get; } = identityLease;
        public Task<bool> Completion { get; } = completion;
        public IReadOnlyList<HistoryPoint> History => history.ToArray();
        internal List<HistoryPoint> MutableHistory => history;
    }

    private readonly LobbyManager _lobbies;
    private readonly NodeMatchCoordinator _coordinator;
    private readonly WorkerScheduler _scheduler;
    private readonly ConcurrentDictionary<MatchId, TaskCompletionSource<bool>> _completions = [];
    private readonly Func<int, int, CancellationToken, Task<SoakIdentityLease>>? _identities;
    private readonly Action<SoakIdentityLease>? _releaseIdentities;
    private readonly SoakRosterOptions _roster;
    private readonly bool _lobbyChurn;
    private readonly bool _chatDuringStart;
    private readonly SoakActiveIdentityRegistry _activePlayers = new();

    public int ActiveIdentityCount => _activePlayers.Count;
    public NodeMatchCoordinatorRetentionSnapshot CoordinatorRetention => _coordinator.RetentionSnapshot;

    public SoakLobbyDriver(WorkerScheduler scheduler, WorkerManager manager, WorkerAdmissionIssuer signer,
        NodeContentCatalog catalog, SoakRosterOptions roster, ReplayPolicy replayPolicy = ReplayPolicy.Record,
        Func<int, int, CancellationToken, Task<SoakIdentityLease>>? identities = null,
        Action<SoakIdentityLease>? releaseIdentities = null,
        bool lobbyChurn = false, bool chatDuringStart = false)
    {
        roster.Validate();
        if (!Enum.IsDefined(replayPolicy)) throw new ArgumentOutOfRangeException(nameof(replayPolicy));
        _lobbies = new(replayPolicy: replayPolicy);
        _scheduler = scheduler; _roster = roster;
        _identities = identities; _releaseIdentities = releaseIdentities;
        _lobbyChurn = lobbyChurn; _chatDuringStart = chatDuringStart;
        _coordinator = new(_lobbies, scheduler, manager, signer, catalog);
        _scheduler.Ended += OnEnded;
        _scheduler.TransitionEnded += OnEnded;
    }

    public async Task<Round> StartAsync(string map, MatchMode mode, int seconds, CancellationToken cancellationToken)
    {
        if (_identities == null) throw new InvalidOperationException("The Node-domain soak requires Backend-seeded participant identities.");
        SoakIdentityLease lease = await _identities(_roster.Players, _roster.Observers, cancellationToken);
        var players = lease.PlayerIds.Select((id, index) => new LobbyIdentity(Guid.NewGuid(), id, "SoakHuman" + index.ToString("D2"))).ToArray();
        var observers = lease.ObserverIds.Select((id, index) => new LobbyIdentity(Guid.NewGuid(), id, "SoakObserver" + index.ToString("D2"))).ToArray();
        var identities = players.Concat(observers).ToArray();
        if (!_activePlayers.TryClaim(lease, identities.Select(identity => identity.PlayerId!.Value)))
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
            if (_lobbyChurn)
            {
                LobbyIdentity churned = observers.FirstOrDefault() ?? players.Skip(1).FirstOrDefault()
                    ?? throw new InvalidOperationException("--lobby-churn requires a non-owner participant.");
                LobbySnapshot memberSnapshot = _lobbies.ForSession(churned.SessionId)
                    ?? throw new InvalidOperationException("Churn participant is not in the soak lobby.");
                _lobbies.Execute(churned, new LobbyLeave(memberSnapshot.Revision));
                snapshot = _lobbies.ForSession(owner.SessionId)
                    ?? throw new InvalidOperationException("Soak lobby disappeared during churn.");
                Record(history, "churn.leave", snapshot);
                snapshot = (LobbySnapshot)_lobbies.Execute(churned,
                    new LobbyJoin(snapshot.LobbyId, snapshot.Revision, churned == observers.FirstOrDefault()));
                Record(history, "churn.join", snapshot);
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

    /// <summary>Completes one durable round, resolves the authoritative return
    /// ballot, and optionally releases the same lobby identities.</summary>
    public async Task<Round> RematchAsync(Round previous, string map, MatchMode mode, int seconds,
        CancellationToken cancellationToken)
    {
        try
        {
            bool interrupted = await previous.Completion.WaitAsync(cancellationToken);
            LobbySnapshot terminal = _lobbies.ForSession(previous.Owner.SessionId)
                ?? throw new InvalidOperationException("Completion lost the lobby.");
            Require(!interrupted && terminal.Phase == LobbyPhase.PostMatch
                && terminal.CurrentMatchId == previous.Spec.MatchId.Value,
                "A rematch requires the authoritative completed post-match boundary.");
            Record(previous.MutableHistory, "completed", terminal);

            // The Node owns ballot resolution and continuation preparation. The
            // soak only submits the same player votes a control client would
            // submit; it never retires the old placement or creates the next
            // MatchSpec itself.
            var ballot = (NodeRoundSnapshot)await _coordinator.ExecuteAsync(previous.Owner,
                new LobbyRoundStatus(terminal.Revision), cancellationToken).WaitAsync(cancellationToken);
            LobbyVoteEntry selected = SelectContinuation(ballot.Options, map, mode);
            NodeRoundSnapshot vote = ballot;
            foreach (LobbyIdentity player in previous.Players)
            {
                vote = (NodeRoundSnapshot)await _coordinator.ExecuteAsync(player,
                    new LobbyVoteCast(vote.Lobby.Revision, ballot.BallotRevision, selected.Id), cancellationToken)
                    .WaitAsync(cancellationToken);
            }
            Record(previous.MutableHistory, "continuation.vote", vote.Lobby);
            _completions.TryRemove(previous.Spec.MatchId, out _);

            Round replacement = await WaitForContinuationAsync(previous, map, mode, seconds,
                previous.MutableHistory, cancellationToken);
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
            // A rematch owns the old lease until its replacement is fully admitted.
            // If continuation preparation fails, the coordinator performs the
            // authoritative recovery boundary; this cleanup only disconnects the
            // soak identities and releases their Backend lease.
            DisconnectAll(previous.Players, previous.Observers);
            _coordinator.ReconcileMembership();
            Release(previous.IdentityLease, previous.Players, previous.Observers);
            throw;
        }
    }

    /// <summary>Requests an active Restart or ChangeMap through the Node's
    /// transition ballot. The old placement is cancelled and retired only by
    /// the coordinator; this method waits for the Node-owned fresh handoff.</summary>
    public async Task<Round> TransitionAsync(Round previous, string map,
        MatchTransitionChoice choice, int seconds, CancellationToken cancellationToken)
    {
        try
        {
            LobbySnapshot current = _lobbies.ForSession(previous.Owner.SessionId)
                ?? throw new InvalidOperationException("Active transition lost the lobby.");
            Require(current.Phase == LobbyPhase.InMatch
                && current.CurrentMatchId == previous.Spec.MatchId.Value,
                "An active transition requires the current frozen match.");
            if (choice == MatchTransitionChoice.ChangeMap && map == previous.Spec.Content.MapKey)
                throw new InvalidOperationException("ChangeMap requires a different hosted map.");

            var proposal = (NodeMatchTransitionVoteSnapshot)await _coordinator.ExecuteAsync(previous.Owner,
                new LobbyMatchTransitionPropose(current.Revision, previous.Spec.MatchId.Value, choice,
                    choice == MatchTransitionChoice.ChangeMap ? map : null), cancellationToken)
                .WaitAsync(cancellationToken);
            NodeMatchTransitionVoteSnapshot vote = proposal;
            foreach (LobbyIdentity player in previous.Players.Skip(1))
            {
                if (vote.State != MatchTransitionVoteState.Pending) break;
                LobbySnapshot voterLobby = _lobbies.ForSession(player.SessionId)
                    ?? throw new InvalidOperationException("Transition voter left the lobby.");
                vote = (NodeMatchTransitionVoteSnapshot)await _coordinator.ExecuteAsync(player,
                    new LobbyMatchTransitionVote(voterLobby.Revision, previous.Spec.MatchId.Value,
                        vote.BallotRevision, true), cancellationToken)
                    .WaitAsync(cancellationToken);
            }
            if (vote.State != MatchTransitionVoteState.Approved)
                throw new InvalidOperationException($"Active transition ballot did not approve: {vote.State}.");

            bool interrupted = await previous.Completion.WaitAsync(cancellationToken);
            Require(interrupted, "Active transition did not receive the authoritative cancellation terminal.");
            _completions.TryRemove(previous.Spec.MatchId, out _);
            LobbySnapshot terminal = _lobbies.ForSession(previous.Owner.SessionId)
                ?? throw new InvalidOperationException("Transition completion lost the lobby.");
            Record(previous.MutableHistory, "transition.completed", terminal);
            Round replacement = await WaitForContinuationAsync(previous, map, previous.Spec.Rules.Mode,
                seconds, previous.MutableHistory, cancellationToken);
            Require(replacement.Spec.MatchId != previous.Spec.MatchId,
                "Active transition reused the old MatchId.");
            return replacement;
        }
        catch
        {
            DisconnectAll(previous.Players, previous.Observers);
            _coordinator.ReconcileMembership();
            Release(previous.IdentityLease, previous.Players, previous.Observers);
            throw;
        }
    }

    internal static MatchTransitionChoice SelectActiveTransition(Random random, double mapChangeRate)
    {
        ArgumentNullException.ThrowIfNull(random);
        return random.NextDouble() < mapChangeRate
            ? MatchTransitionChoice.ChangeMap : MatchTransitionChoice.Restart;
    }

    /// <summary>Runs the Node's single owned post-match continuation loop. The
    /// caller must retain this task until shutdown so automatic continuations
    /// cannot be mistaken for an optional helper.</summary>
    public Task RunContinuationsAsync(CancellationToken cancellationToken)
        => _coordinator.RunContinuationsAsync(cancellationToken);

    /// <summary>Returns the Node-owned handoff for one frozen participant. A
    /// reconnect requests a fresh generation through the same coordinator
    /// command used by a real control client.</summary>
    public async Task<NodeMatchHandoff> GetHandoffAsync(LobbyIdentity identity,
        bool forceFresh, CancellationToken cancellationToken)
    {
        identity.Validate();
        if (forceFresh)
        {
            LobbySnapshot snapshot = _lobbies.ForSession(identity.SessionId)
                ?? throw new InvalidOperationException("Handoff participant left the soak lobby.");
            if (snapshot.CurrentMatchId is not { } matchId)
                throw new InvalidOperationException("Handoff participant has no active match.");
            return (NodeMatchHandoff)await _coordinator.ExecuteAsync(identity,
                new NodeMatchRejoin(matchId), cancellationToken).ConfigureAwait(false);
        }
        return _coordinator.ForSession(identity.SessionId) as NodeMatchHandoff
            ?? throw new InvalidOperationException("Node did not publish an authoritative handoff.");
    }

    internal static LobbyVoteEntry SelectContinuation(IReadOnlyList<LobbyVoteEntry> options,
        string map, MatchMode mode)
    {
        LobbyVoteEntry? selected = options.FirstOrDefault(option =>
            option.MapKey == map && option.Mode == mode
            && option.Choice is LobbyVoteChoice.Rematch or LobbyVoteChoice.NextMap or LobbyVoteChoice.Map);
        return selected ?? throw new InvalidOperationException(
            $"The authoritative post-match ballot has no continuation for map '{map}' and mode '{mode}'.");
    }

    private async Task<Round> WaitForContinuationAsync(Round previous, string map, MatchMode mode,
        int seconds, List<HistoryPoint> history, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, seconds + 30)));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            LobbySnapshot? snapshot = _lobbies.ForSession(previous.Owner.SessionId);
            if (snapshot is { Phase: LobbyPhase.InMatch, CurrentMatchId: { } current }
                && current != previous.Spec.MatchId.Value
                && _scheduler.TryGetAssignment(new(current), out WorkerMatchAssignment? assignment)
                && assignment?.Placement is { } placement)
            {
                MatchSpec spec = assignment.Spec;
                Require(spec.Content.MapKey == map && spec.Rules.Mode == mode
                    && spec.Rules.TimeLimit == TimeSpan.FromSeconds(seconds),
                    "Node continuation changed the requested frozen settings.");
                var handoff = _coordinator.ForSession(previous.Owner.SessionId) as NodeMatchHandoff;
                Require(handoff?.MatchId == current
                    && handoff.WireMatchId == placement.WireMatchId.Value,
                    "Node continuation did not mint the fresh routed handoff.");
                Record(history, "continuation.started", snapshot);
                Task<bool> completion = _completions.GetOrAdd(new(current),
                    _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                return new(spec, placement, previous.Owner, previous.Players,
                    previous.Observers, previous.IdentityLease, completion, history);
            }
            await Task.Delay(10, timeout.Token).ConfigureAwait(false);
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
        Task<object> start = _coordinator.ExecuteAsync(owner, new LobbyStart(snapshot.Revision), cancellationToken);
        if (_chatDuringStart)
        {
            using var chatTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            chatTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            LobbySnapshot? starting = null;
            while (!start.IsCompleted)
            {
                starting = _lobbies.ForSession(owner.SessionId);
                if (starting?.Phase == LobbyPhase.StartingMatch) break;
                await Task.Delay(1, chatTimeout.Token).ConfigureAwait(false);
            }
            starting ??= _lobbies.ForSession(owner.SessionId);
            if (starting?.Phase != LobbyPhase.StartingMatch)
                throw new InvalidOperationException("--chat-during-start could not observe the StartingMatch boundary.");
            try
            {
                snapshot = (LobbySnapshot)await _coordinator.ExecuteAsync(owner,
                    new LobbyChat("soak-start", starting.Revision), chatTimeout.Token).WaitAsync(chatTimeout.Token);
            }
            catch (LobbyCommandException error) when (error.Code == "stale_revision")
            {
                LobbySnapshot retry = _lobbies.ForSession(owner.SessionId)
                    ?? throw new InvalidOperationException("Soak lobby disappeared during startup chat.");
                snapshot = (LobbySnapshot)await _coordinator.ExecuteAsync(owner,
                    new LobbyChat("soak-start", retry.Revision), chatTimeout.Token).WaitAsync(chatTimeout.Token);
            }
            Record(history, "chat.starting", snapshot);
        }
        snapshot = (LobbySnapshot)await start.WaitAsync(cancellationToken);
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
        Require(interrupted
                ? terminal.Phase == LobbyPhase.Open && terminal.CurrentMatchId == null
                : terminal.Phase == LobbyPhase.PostMatch && terminal.CurrentMatchId == round.Spec.MatchId.Value,
            "Terminal Worker event did not publish the expected lobby lifecycle state.");
        Record(round.MutableHistory, interrupted ? "interrupted" : "completed", terminal);
        LobbySnapshot reopened;
        if (interrupted)
        {
            // Interrupted rounds are reopened immediately and have no Node
            // intermission ballot.
            reopened = terminal;
        }
        else
        {
            var ballot = (NodeRoundSnapshot)await _coordinator.ExecuteAsync(round.Owner,
                new LobbyRoundStatus(terminal.Revision));
            LobbyVoteEntry returnOption = ballot.Options.Single(option =>
                option.Choice == LobbyVoteChoice.ReturnToLobby);
            NodeRoundSnapshot vote = ballot;
            foreach (LobbyIdentity player in round.Players)
            {
                vote = (NodeRoundSnapshot)await _coordinator.ExecuteAsync(player,
                    new LobbyVoteCast(vote.Lobby.Revision, ballot.BallotRevision,
                        returnOption.Id));
            }
            reopened = vote.Lobby;
        }
        Require(reopened.LobbyId == round.LobbyId && reopened.Phase == LobbyPhase.Open && reopened.CurrentMatchId == null
            && reopened.Members.All(m => !m.Ready), "Round completion did not reopen the same lobby with fresh readiness.");
        Record(round.MutableHistory, "round.open", reopened);
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
            _coordinator.ClearSessionMatchState(round.Owner.SessionId);
        }
        foreach (var member in round.Players.Concat(round.Observers)) _coordinator.ClearSessionMatchState(member.SessionId);
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
        var identities = players.Concat(observers).Select(identity => identity.PlayerId!.Value).ToArray();
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
    public void Dispose()
    {
        _scheduler.Ended -= OnEnded;
        _scheduler.TransitionEnded -= OnEnded;
        _coordinator.Dispose();
    }
}
