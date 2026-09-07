using System;

namespace MphRead.Mods.Network;

/// <summary>Simulation-thread adapter: only connected human player slots are eligible.</summary>
public sealed class ServerVoting
{
    private readonly ServerNetwork _network;
    private readonly Func<ServerSimulation> _simulation;
    private readonly ServerVoteOptions _options;
    private readonly MapRotation? _rotation;
    private readonly ServerVoteSession _ballot;
    private readonly ulong[] _sentIdentity = new ulong[8];
    private readonly uint[] _sentRevision = new uint[8], _sentUpdate = new uint[8];
    public bool LobbyChoiceReady { get; private set; }
    public ServerVoteSession Ballot => _ballot;
    public ServerVoting(ServerNetwork network, Func<ServerSimulation> simulation, ServerVoteOptions options, MapRotation? rotation, uint seed)
    {
        _network = network; _simulation = simulation; _options = options; _rotation = rotation;
        _ballot = new(seed);
        network.IntermissionVoteReceived = Receive;
    }
    private bool Receive(ServerPeer peer, IntermissionVoteRequest request)
    {
        var simulation = _simulation();
        if (peer.IsObserver || peer.IsBot || peer.Connection.State is not (NetConnectionState.Ready or NetConnectionState.Playing)
            || _network.Phase != simulation.Scene.Match.Phase
            || simulation.Scene.Match.Phase != MatchPhase.Intermission
                && !(simulation.VoteLobbyHold && simulation.Scene.Match.Phase == MatchPhase.WaitingForPlayers)) return false;
        return _ballot.Cast(peer.Slot, peer.Connection.Id, request, _network.Tick);
    }
    public void Tick(uint tick)
    {
        var simulation = _simulation(); var match = simulation.Scene.Match;
        if (match.Phase != MatchPhase.Intermission && !(simulation.VoteLobbyHold && match.Phase == MatchPhase.WaitingForPlayers)) return;
        if (_ballot.MatchId != _network.MatchId || _ballot.PhaseRevision != match.PhaseRevision || _ballot.Phase != match.Phase)
        {
            _ballot.Begin(_network.MatchId, match.PhaseRevision, match.Phase, match.PhaseEndTick,
                _options.Policy, _rotation, match.Rules);
            Array.Clear(_sentIdentity); Array.Clear(_sentRevision); Array.Clear(_sentUpdate);
        }
        Span<ulong> eligible = stackalloc ulong[8]; eligible.Clear();
        foreach (ServerPeer? peer in _network.Peers)
            if (peer != null && !peer.IsObserver && !peer.IsBot && peer.Connection.State is NetConnectionState.Ready or NetConnectionState.Playing)
                eligible[peer.Slot] = peer.Connection.Id;
        _ballot.SetEligible(eligible);
        if (_ballot.DeadlineReached(tick))
        {
            VoteResolution choice = _ballot.Resolve();
            if (simulation.VoteLobbyHold)
            {
                if (choice.Kind == IntermissionChoice.Rematch) simulation.VoteLobbyHold = false;
                else LobbyChoiceReady = true;
            }
        }
        Span<byte> bytes = stackalloc byte[IntermissionBallot.MaximumSize];
        bool published = true;
        foreach (ServerPeer? peer in _network.Peers)
        {
            if (peer == null || peer.IsObserver || peer.IsBot || eligible[peer.Slot] == 0) continue;
            int slot = peer.Slot;
            if (_sentIdentity[slot] == peer.Connection.Id && _sentRevision[slot] == _ballot.Revision && _sentUpdate[slot] == _ballot.UpdateRevision) continue;
            int length = _ballot.Snapshot(peer.Slot, peer.Connection.Id).Write(bytes);
            if (peer.Connection.Reliable.TryEnqueue(ReliableEventType.IntermissionBallot, bytes[..length], out _))
            { _sentIdentity[slot] = peer.Connection.Id; _sentRevision[slot] = _ballot.Revision; _sentUpdate[slot] = _ballot.UpdateRevision; }
            else published = false;
        }
        if (published) _ballot.MarkPublished();
    }
    public VoteResolution ResolveForRotation() => _ballot.MatchId == _network.MatchId
        && (_ballot.Phase == MatchPhase.Intermission || LobbyChoiceReady)
        ? _ballot.Resolve() : new(IntermissionChoice.NextMap, -1);
    public void NewMatch() { LobbyChoiceReady = false; }
}
