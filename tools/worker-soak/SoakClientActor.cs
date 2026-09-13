using System.Net;
using System.Globalization;
using System.Security.Cryptography;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace ProjectPrime.WorkerSoak;

public sealed record SoakClientFailure(byte SeatId, string Role, string Phase, string State, string Reason);

public sealed record SoakClientSnapshot(long PlayerSnapshots, long ObserverSnapshots, long WorldPackets,
    long InputsSent, long PlayingSnapshots, long Reconnects, int Failures,
    int ConnectedPeers, int PlayingPeers, int ProgressPeers, int TotalPeers, uint MaximumServerTick,
    bool AllPeersConnected, bool HasProgress, int ReconnectsRecovered);

public sealed record SoakPeerState(string Role, string ConnectionState, string Phase, bool Connected,
    bool Playing, bool HasProgress, uint ServerTick, long Snapshots, long InputsSent);

/// <summary>Counts reconnects and recoveries on the same peer unit. A recovery
/// requires that peer to reconnect and observe fresh server progress.</summary>
public sealed class SoakReconnectTracker
{
    private readonly Dictionary<byte, int> _generations = [];
    private readonly Dictionary<byte, int> _recovered = [];
    private readonly Dictionary<byte, SoakReconnectPeerEvidence> _injections = [];
    private readonly List<SoakReconnectRecoveryEvidence> _recoveryEvidence = [];
    private int _nextGeneration;

    public long Attempts { get; private set; }
    public long Recovered { get; private set; }
    public bool HasPending => _generations.Any(pair => _recovered.GetValueOrDefault(pair.Key) != pair.Value);
    public IReadOnlyList<SoakReconnectPeerEvidence> Pending => _injections
        .Where(pair => _generations.GetValueOrDefault(pair.Key) != _recovered.GetValueOrDefault(pair.Key))
        .Select(pair => pair.Value).ToArray();
    public IReadOnlyList<SoakReconnectRecoveryEvidence> RecoveryEvidence => _recoveryEvidence.ToArray();

    public int Begin(byte seatId)
        => Begin(seatId, null);

    public int Begin(byte seatId, SoakReconnectPeerEvidence? injection)
    {
        if (_generations.TryGetValue(seatId, out int previous)
            && _recovered.GetValueOrDefault(seatId) != previous)
            throw new InvalidOperationException($"Reconnect seat {seatId} already has pending generation {previous}.");
        int generation = checked(++_nextGeneration);
        _generations[seatId] = generation;
        _injections[seatId] = (injection ?? new SoakReconnectPeerEvidence(seatId, "Unknown", 0,
            "Unknown", null, false, false, false, 0)) with { Generation = generation };
        Attempts++;
        return generation;
    }

    public void Observe(byte seatId, bool connected, bool hasFreshProgress)
        => Observe(seatId, connected, hasFreshProgress, null);

    public void Observe(byte seatId, bool connected, bool hasFreshProgress, SoakReconnectPeerEvidence? finalEvidence)
    {
        if (!connected || !hasFreshProgress || !_generations.TryGetValue(seatId, out int generation)
            || _recovered.GetValueOrDefault(seatId) == generation) return;
        _recovered[seatId] = generation;
        Recovered++;
        SoakReconnectPeerEvidence injection = _injections[seatId];
        _recoveryEvidence.Add(new(injection, (finalEvidence ?? injection with
        {
            Connected = connected, FreshProgress = hasFreshProgress
        }) with { Generation = generation }));
    }

    public void EnsureNoPending(string matchId)
    {
        if (HasPending || Attempts != Recovered)
        {
            string details = string.Join(", ", Pending.Select(peer =>
                $"seat={peer.SeatId}/generation={peer.Generation}/phase={peer.Phase}"
                + $"/remaining={(peer.RemainingSeconds?.ToString("F3", CultureInfo.InvariantCulture) ?? "null")}"
                + "/finalEvidence=none"));
            throw new InvalidDataException($"Match {matchId} retired with pending reconnect generations "
                + $"(attempts={Attempts}, recovered={Recovered}, pending=[{details}]).");
        }
    }
}

/// <summary>Single actor-thread owner of real UDP clients; all tickets come from the
/// Node signer and frozen roster. It does not create scenes or fake packets.</summary>
public sealed class SoakClientActor : IDisposable
{
    private sealed class AdmissionGrant(ulong nonce, string ticket, Guid admissionId, byte[] key)
    {
        public ulong Nonce { get; } = nonce;
        public string Ticket { get; } = ticket;
        public Guid AdmissionId { get; } = admissionId;
        public byte[] Key { get; } = key;
    }

    private sealed class Peer(RosterSeat seat, NetTransport transport, NetClient client)
    {
        public RosterSeat Seat { get; } = seat;
        public NetTransport Transport { get; } = transport;
        public NetClient Client { get; } = client;
        public uint Sequence;
        public uint PhaseRevision = 1;
        public MatchPhase Phase;
        public long PreviousSnapshots;
        public bool FailureCounted;
        public uint MaximumServerTick;
        public bool HasProgress;
        public bool HasPlayingProgress;
        public uint LastPlayingServerTick;
        public double? AuthoritativeRemainingSeconds;
    }
    private readonly MatchSpec _spec;
    private readonly MatchPlacement _placement;
    private readonly WorkerAdmissionIssuer _issuer;
    private readonly WorkerScheduler _scheduler;
    private readonly IReadOnlyList<LobbyIdentity> _participants;
    private readonly List<Peer> _peers = [];
    private readonly SoakReconnectTracker _reconnects = new();
    private long _worldPackets, _inputs, _playingSnapshots;
    private int _failures;

    private SoakClientActor(MatchSpec spec, MatchPlacement placement, WorkerAdmissionIssuer issuer,
        WorkerScheduler scheduler, IReadOnlyList<LobbyIdentity> participants)
    {
        _spec = spec; _placement = placement; _issuer = issuer; _scheduler = scheduler;
        _participants = participants.ToArray();
    }

    /// <summary>
    /// Creates authenticated gameplay peers only after the Node-issued ticket
    /// and its matching Worker admission key have both been installed. The
    /// old ticket-only constructor was deliberately removed: authenticated
    /// Workers must never be silently downgraded by the soak harness.
    /// </summary>
    public static async Task<SoakClientActor> CreateAsync(MatchSpec spec, MatchPlacement placement,
        WorkerAdmissionIssuer issuer, WorkerScheduler scheduler, IReadOnlyList<LobbyIdentity> participants,
        CancellationToken cancellationToken = default)
    {
        if (!placement.UdpAuthenticationEnabled)
            throw new InvalidOperationException("Worker soak requires authenticated UDP admission.");
        ArgumentNullException.ThrowIfNull(participants);
        if (participants.Count == 0) throw new ArgumentException("At least one Node participant is required.", nameof(participants));
        var actor = new SoakClientActor(spec, placement, issuer, scheduler, participants);
        try
        {
            await actor.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return actor;
        }
        catch
        {
            actor.Dispose();
            throw;
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        foreach (RosterSeat seat in _spec.Roster.Where(s => s.Role is SeatRole.Player or SeatRole.Observer))
        {
            AdmissionGrant grant = await CreateAdmissionAsync(seat, cancellationToken).ConfigureAwait(false);
            NetTransport? transport = null;
            try
            {
                transport = new NetTransport(0);
                NetClient client = new(transport, new IPEndPoint(IPAddress.Parse(_placement.Host), _placement.Port),
                    seat.DisplayName, seat.Hunter, grant.Nonce, grant.Ticket, seat.Role == SeatRole.Observer,
                    _placement.WireMatchId.Value, grant.AdmissionId, grant.Key,
                    udpAuthenticationEnabled: true);
                var peer = new Peer(seat, transport, client);
                _peers.Add(peer);
                ConfigureClient(peer);
                transport = null; // The peer now owns the transport.
            }
            finally
            {
                transport?.Dispose();
                CryptographicOperations.ZeroMemory(grant.Key);
            }
        }
    }

    private void ConfigureClient(Peer peer)
    {
        NetClient client = peer.Client;
        client.WorldPacketValidator = WorldPacket.TryValidate;
        client.WorldPacketReceived = bytes =>
        {
            _worldPackets++;
            for (int offset = WorldPacket.HeaderSize; offset < bytes.Length; offset += WorldRecord.Size)
            {
                if (!WorldRecord.TryRead(bytes.Slice(offset, WorldRecord.Size), out var record)) continue;
                if (record.Kind == WorldRecordKind.Lifecycle) peer.PhaseRevision = record.C;
                if (record.Kind == WorldRecordKind.Match)
                {
                    peer.Phase = (MatchPhase)record.B;
                    peer.AuthoritativeRemainingSeconds = record.Position.X;
                }
            }
        };
    }

    private async Task<AdmissionGrant> CreateAdmissionAsync(RosterSeat seat,
        CancellationToken cancellationToken)
    {
        LobbyIdentity participant = ParticipantFor(seat);
        ulong nonce = NetConnection.NewIdentity();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long expires = checked(now + 120);
        Guid ticketId = Guid.NewGuid();
        Guid admissionId = Guid.NewGuid();
        byte[] key = RandomNumberGenerator.GetBytes(AdmissionKeyRules.ByteLength);
        try
        {
            Guid nodeSessionId = participant.SessionId;
            var claims = new WorkerAdmissionClaims(_spec.NodeId, _spec.NodeIncarnation,
                _placement.WorkerId, _placement.WorkerIncarnation, _spec.LobbyId, _spec.MatchId,
                _placement.WireMatchId, nodeSessionId, seat.PlayerId, seat.GuestSessionId,
                seat.Role, seat.SeatId, seat.DisplayName, nonce, now, expires, ticketId,
                HandoffGeneration.Initial);
            string ticket = _issuer.Issue(claims);
            var install = new InstallAdmissionKey(admissionId, ticketId, nodeSessionId,
                _spec.NodeId, _spec.NodeIncarnation, _spec.MatchId, _placement.WireMatchId,
                _placement.WorkerId, _placement.WorkerIncarnation, seat.SeatId, nonce, expires,
                Convert.ToBase64String(key), HandoffGeneration.Initial);
            await _scheduler.InstallAdmissionKeyAsync(install, cancellationToken).ConfigureAwait(false);
            return new(nonce, ticket, admissionId, key);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }

    private LobbyIdentity ParticipantFor(RosterSeat seat)
    {
        LobbyIdentity? participant = _participants.FirstOrDefault(candidate =>
            seat.PlayerId is { } player
                ? candidate.PlayerId == player.Value
                : candidate.GuestSessionId == seat.GuestSessionId);
        return participant ?? throw new InvalidOperationException(
            $"No Node session was supplied for authenticated seat {seat.SeatId}.");
    }
    public void Tick()
    {
        foreach (var peer in _peers)
        {
            var client = peer.Client;
            client.Poll();
            if (client.Failure != null && !peer.FailureCounted) { peer.FailureCounted = true; _failures++; }
            if (client.State == NetConnectionState.Loading) client.Ready(_placement.WireMatchId.Value);
            while (client.TryDequeueEvent(out _)) { } // Consume bounded presentation events; no renderer is created.
            if (peer.Phase == MatchPhase.Playing) _playingSnapshots += client.SnapshotsReceived - peer.PreviousSnapshots;
            peer.PreviousSnapshots = client.SnapshotsReceived;
            if (client.HasSnapshot)
            {
                uint tick = client.Snapshot.ServerTick;
                bool freshTick = tick > peer.MaximumServerTick;
                peer.MaximumServerTick = Math.Max(peer.MaximumServerTick, tick);
                peer.HasProgress |= tick > 0;
                if (peer.Phase == MatchPhase.Playing && freshTick && tick > 0)
                {
                    peer.HasPlayingProgress = true;
                    peer.LastPlayingServerTick = tick;
                }
            }
            _reconnects.Observe(peer.Seat.SeatId, IsConnected(client), peer.HasPlayingProgress,
                Evidence(peer, client));
            if (peer.Seat.Role == SeatRole.Player && client.State is NetConnectionState.Ready or NetConnectionState.Playing)
            {
                uint sequence = ++peer.Sequence;
                InputButtons buttons = sequence % 120 < 60 ? InputButtons.Forward | InputButtons.Shoot : InputButtons.Right;
                if (client.SendInputs(new[] { new InputCommand(sequence, sequence, client.HasSnapshot ? client.Snapshot.ServerTick : 0,
                    buttons, InputButtons.None, -Vector3.UnitZ, InputCommand.NoWeapon) }, peer.PhaseRevision)) _inputs++;
            }
        }
    }
    public SoakReconnectReadiness ReconnectReadiness(
        double recoveryDeadlineSeconds = SoakRecoveryPolicy.ReconnectRecoveryDeadlineSeconds)
    {
        var evidence = _peers.Select(peer => Evidence(peer, peer.Client)).ToArray();
        string reason = _reconnects.HasPending ? "pending-generation"
            : evidence.Any(peer => !peer.Connected) ? "peer-not-connected"
            : evidence.Any(peer => !peer.Playing) ? "peer-not-playing"
            : evidence.Any(peer => !peer.FreshProgress) ? "peer-has-no-fresh-playing-progress"
            : evidence.Any(peer => peer.RemainingSeconds is not { } remaining
                || !double.IsFinite(remaining) || remaining < recoveryDeadlineSeconds)
                ? "insufficient-authoritative-remaining-time" : "ready";
        return new(!_reconnects.HasPending
            && SoakRecoveryPolicy.IsReconnectBatchReady(evidence, recoveryDeadlineSeconds), reason,
            recoveryDeadlineSeconds, evidence);
    }

    public async Task<SoakReconnectBatch> ReconnectAsync(
        double recoveryDeadlineSeconds = SoakRecoveryPolicy.ReconnectRecoveryDeadlineSeconds,
        CancellationToken cancellationToken = default)
    {
        SoakReconnectReadiness readiness = ReconnectReadiness(recoveryDeadlineSeconds);
        if (!readiness.Ready)
            throw new InvalidOperationException($"Reconnect injection was not ready: {readiness.Reason}.");

        var peers = _peers.ToArray();
        var generations = new Dictionary<byte, int>(peers.Length);
        foreach (var peer in peers)
            generations[peer.Seat.SeatId] = _reconnects.Begin(peer.Seat.SeatId,
                readiness.Peers.Single(evidence => evidence.SeatId == peer.Seat.SeatId));

        var injected = new List<SoakReconnectPeerEvidence>(peers.Length);
        foreach (var peer in peers)
        {
            AdmissionGrant grant = await CreateAdmissionAsync(peer.Seat, cancellationToken).ConfigureAwait(false);
            // The server keeps an observer reservation until it receives the
            // client's disconnect or its timeout expires. Flush that explicit
            // close before replacing the client session so a fresh observer
            // ticket can be admitted during the same reconnect generation.
            try
            {
                peer.Client.Disconnect();
                peer.Client.Reconnect(grant.Nonce, grant.Ticket, grant.AdmissionId, grant.Key);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(grant.Key);
            }
            peer.Sequence = 0; peer.PhaseRevision = 1; peer.Phase = MatchPhase.WaitingForPlayers;
            peer.FailureCounted = false;
            peer.MaximumServerTick = 0; peer.HasProgress = false; peer.HasPlayingProgress = false;
            peer.LastPlayingServerTick = 0; peer.AuthoritativeRemainingSeconds = null;
            injected.Add(readiness.Peers.Single(evidence => evidence.SeatId == peer.Seat.SeatId)
                with { Generation = generations[peer.Seat.SeatId] });
        }
        return new(_spec.MatchId.Value, recoveryDeadlineSeconds, injected);
    }

    public IReadOnlyList<SoakReconnectPeerEvidence> PendingReconnects => _reconnects.Pending;
    public IReadOnlyList<SoakReconnectRecoveryEvidence> ReconnectRecoveryEvidence => _reconnects.RecoveryEvidence;
    public long ReconnectAttempts => _reconnects.Attempts;
    public long ReconnectRecoveries => _reconnects.Recovered;
    public IReadOnlyList<SoakClientFailure> Failures => _peers.Where(peer => peer.Client.Failure != null)
        .Select(peer => new SoakClientFailure(peer.Seat.SeatId, peer.Seat.Role.ToString(), peer.Phase.ToString(),
            peer.Client.State.ToString(), SanitizeFailure(peer.Client.Failure))).ToArray();
    private static string SanitizeFailure(string? reason) => new((reason ?? "Unknown failure").Take(256)
        .Select(character => char.IsControl(character) ? ' ' : character).ToArray());
    public IReadOnlyList<SoakPeerState> Peers => _peers.Select(peer => new SoakPeerState(peer.Seat.Role.ToString(),
        peer.Client.State.ToString(), peer.Phase.ToString(), IsConnected(peer.Client), peer.Client.State == NetConnectionState.Playing,
        peer.HasProgress, peer.MaximumServerTick, peer.Client.SnapshotsReceived, peer.Sequence)).ToArray();
    public bool AllPeersConnected => _peers.Count > 0 && _peers.All(peer => IsConnected(peer.Client));
    public bool HasProgress => _peers.Any(peer => peer.HasProgress);
    public int ConnectedPeers => _peers.Count(peer => IsConnected(peer.Client));
    public int PlayingPeers => _peers.Count(peer => peer.Client.State == NetConnectionState.Playing
        && IsConnected(peer.Client) && peer.HasProgress);
    public int ProgressPeers => _peers.Count(peer => peer.HasProgress);
    private static bool IsConnected(NetClient client) => client.Failure == null
        && client.State is NetConnectionState.Loading or NetConnectionState.Ready or NetConnectionState.Playing;
    public SoakClientSnapshot Snapshot => new(
        _peers.Where(p => p.Seat.Role == SeatRole.Player).Sum(p => p.Client.SnapshotsReceived),
        _peers.Where(p => p.Seat.Role == SeatRole.Observer).Sum(p => p.Client.SnapshotsReceived),
        _worldPackets, _inputs, _playingSnapshots, _reconnects.Attempts, _failures, ConnectedPeers, PlayingPeers,
        ProgressPeers, _peers.Count, _peers.Count == 0 ? 0 : _peers.Max(peer => peer.MaximumServerTick),
        AllPeersConnected, HasProgress, checked((int)_reconnects.Recovered));
    private static SoakReconnectPeerEvidence Evidence(Peer peer, NetClient client)
        => new(peer.Seat.SeatId, peer.Seat.Role.ToString(), 0, peer.Phase.ToString(),
            peer.AuthoritativeRemainingSeconds, IsConnected(client),
            client.State == NetConnectionState.Playing && peer.Phase == MatchPhase.Playing,
            peer.HasPlayingProgress, peer.MaximumServerTick);

    public void Dispose()
    {
        Exception? pending = null;
        try { _reconnects.EnsureNoPending(_spec.MatchId.Value.ToString("N")); }
        catch (Exception error) { pending = error; }
        finally
        {
            foreach (var peer in _peers) { peer.Client.Dispose(); peer.Transport.Dispose(); }
            _peers.Clear();
        }
        if (pending != null) throw pending;
    }
}
