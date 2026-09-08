using System.Net;
using System.Globalization;
using FruityPrime.Server.Shared;
using MphRead;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace FruityPrime.WorkerSoak;

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
    private readonly List<Peer> _peers = [];
    private readonly SoakReconnectTracker _reconnects = new();
    private long _worldPackets, _inputs, _playingSnapshots;
    private int _failures;
    public SoakClientActor(MatchSpec spec, MatchPlacement placement, WorkerAdmissionIssuer issuer)
    {
        _spec = spec; _placement = placement; _issuer = issuer;
        try
        {
            foreach (var seat in spec.Roster.Where(s => s.Role is SeatRole.Player or SeatRole.Observer))
            {
                var grant = Ticket(seat);
                var endpoint = new IPEndPoint(IPAddress.Parse(placement.Host), placement.Port);
                var transport = new NetTransport(0);
                NetClient client;
                try { client = new NetClient(transport, endpoint, seat.DisplayName, seat.Hunter, grant.Nonce,
                    grant.Ticket, seat.Role == SeatRole.Observer, placement.WireMatchId.Value); }
                catch { transport.Dispose(); throw; }
                var peer = new Peer(seat, transport, client);
                _peers.Add(peer);
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
        }
        catch { Dispose(); throw; }
    }
    private (ulong Nonce, string Ticket) Ticket(RosterSeat seat)
    {
        ulong nonce = NetConnection.NewIdentity(); long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new WorkerAdmissionClaims(_spec.NodeId, _spec.NodeIncarnation, _placement.WorkerId, _placement.WorkerIncarnation,
            _spec.LobbyId, _spec.MatchId, _placement.WireMatchId, seat.GuestSessionId ?? seat.PlayerId!.Value.Value,
            seat.PlayerId, seat.GuestSessionId, seat.Role, seat.SeatId, seat.DisplayName, nonce, now, now + 120, Guid.NewGuid());
        return (nonce, _issuer.Issue(claims));
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

    public SoakReconnectBatch Reconnect(
        double recoveryDeadlineSeconds = SoakRecoveryPolicy.ReconnectRecoveryDeadlineSeconds)
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
            var grant = Ticket(peer.Seat);
            // The server keeps an observer reservation until it receives the
            // client's disconnect or its timeout expires. Flush that explicit
            // close before replacing the client session so a fresh observer
            // ticket can be admitted during the same reconnect generation.
            peer.Client.Disconnect();
            peer.Client.Reconnect(grant.Nonce, grant.Ticket);
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
