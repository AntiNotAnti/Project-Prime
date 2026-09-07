using System;
using MphRead.Identity;

namespace MphRead.Mods.Network;

/// <summary>Binds lobby authority to the existing reliable connection owner.</summary>
public sealed class ServerLobbyNetwork
{
    private readonly ServerLobby _lobby;
    private readonly ServerNetwork _network;
    private readonly Func<ServerBotManager?>? _botManager;
    private bool _botRosterDirty = true;

    public ServerLobbyNetwork(ServerLobby lobby, ServerNetwork network,
        Func<ServerBotManager?>? botManager = null)
    {
        _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _botManager = botManager;
        network.Lobby = lobby;
        network.LobbyPeerAdmitted = Admit;
        network.LobbyPeerLeaving = Leave;
        network.LobbyRequestReceived = Receive;
        network.LobbyChatReceived = ReceiveChat;
    }

    public void Open(uint tick)
    {
        _network.EnterLobby(_lobby.Runtime.SessionId, _lobby.Runtime.Draft.Current, tick);
        _botRosterDirty = true;
        ReconcileBots();
        foreach (ServerPeer? peer in _network.AllConnections)
            if (peer != null) _lobby.ResetPublication(peer.Connection.Id);
        SynchronizePeers();
    }

    public void OpenAfterMatch(MatchSummaryPacket summary, uint tick)
    {
        if (!_lobby.EnterAfterMatch(summary))
            throw new InvalidOperationException("The completed match summary did not belong to this lobby session.");
        Open(tick);
    }

    public bool TryStart(uint tick, out MatchRules? rules)
    {
        if (!_lobby.TryConsumeStart(out rules) || rules == null) return false;
        SynchronizePeers();
        uint matchId = unchecked(_network.MatchId + 1);
        if (matchId == 0) matchId = 1;
        _network.StartLobbyMatch(matchId, rules, tick);
        SynchronizePeers();
        return true;
    }

    public void Tick(uint tick, ServerBotManager? bots = null)
    {
        uint revision = _lobby.Runtime.Revision;
        _lobby.ExpireReservations(tick);
        if (_lobby.Runtime.Revision != revision) _botRosterDirty = true;
        ReconcileBots(bots);
        if (!_network.LobbyAdmissionOpen) return;

        Span<byte> bytes = stackalloc byte[ReliableChannel.MaxPayloadSize];
        foreach (ServerPeer? peer in _network.AllConnections)
        {
            if (peer == null || peer.Connection.State != NetConnectionState.Lobby
                || peer.Connection.Reliable.PendingCount != 0) continue;
            if (_lobby.PendingMatchSummary(peer.Connection.Id, out MatchSummaryPacket? summary))
            {
                int length = summary!.Write(bytes);
                if (peer.Connection.Reliable.TryEnqueue(ReliableEventType.MatchSummary, bytes[..length], out _))
                    _lobby.MarkMatchSummaryPublished(peer.Connection.Id, summary.MatchId);
                continue;
            }
            if (_lobby.PendingSnapshot(peer.Connection.Id, out LobbySnapshotPacket? snapshot))
            {
                int length = snapshot!.Write(bytes);
                if (peer.Connection.Reliable.TryEnqueue(ReliableEventType.LobbySnapshot, bytes[..length], out _))
                    _lobby.MarkSnapshotPublished(peer.Connection.Id, snapshot.Revision);
            }
        }
    }

    public MatchSummaryPacket PublishSummary(MatchResult result, MatchSummaryFlags flags = MatchSummaryFlags.None,
        MatchReportV1? report = null)
    {
        if (!_lobby.EnterAfterMatch(result, flags, report) || _lobby.LastMatchSummary == null)
            throw new InvalidOperationException("The completed match could not enter this lobby session.");
        MatchSummaryPacket summary = _lobby.LastMatchSummary;
        Span<byte> bytes = stackalloc byte[MatchSummaryPacket.MaximumSize];
        int length = summary.Write(bytes);
        foreach (ServerPeer? peer in _network.AllConnections)
        {
            if (peer == null || peer.Connection.State == NetConnectionState.Disconnecting) continue;
            if (peer.Connection.Reliable.TryEnqueue(ReliableEventType.MatchSummary, bytes[..length], out _))
                _lobby.MarkMatchSummaryPublished(peer.Connection.Id, summary.MatchId);
        }
        return summary;
    }

    private void Admit(ServerPeer peer)
    {
        if (_lobby.Runtime.TryFind(peer.Connection.Id, out _))
        {
            _lobby.RefreshPeer(peer);
            return;
        }
        ulong previous = peer.ReturningFromConnectionId;
        LobbyAdmissionResult result = _lobby.AddPeer(peer, _network.Tick,
            previousConnectionId: previous, reconnectAuthorized: previous != 0,
            hostAuthorized: peer.LobbyOwnerAuthorized);
        if (!result.Accepted)
        {
            _network.Remove(peer.ConnectionIndex, allowReconnect: false,
                reason: ParticipantExitReason.ExplicitLeave);
            return;
        }
        SynchronizePeer(peer);
        _botRosterDirty = true;
    }

    private void Leave(ServerPeer peer, ParticipantExitReason reason)
    {
        bool reconnect = !peer.IsObserver && reason is ParticipantExitReason.Disconnected
            or ParticipantExitReason.Timeout or ParticipantExitReason.Backpressure;
        _lobby.RemovePeer(peer, _network.Tick, reconnect);
        _botRosterDirty = true;
    }

    private bool Receive(ServerPeer peer, LobbyRequestPacket request)
    {
        LobbyCommandResult result = _lobby.ReceiveRequest(peer, request);
        if (result.Accepted)
        {
            _network.RefreshLobbyDraft(_lobby.Runtime.Draft.Current);
            _botRosterDirty = true;
            ReconcileBots();
            SynchronizePeers();
        }
        return EnqueueFeedback(peer, result.Feedback);
    }

    private void ReconcileBots(ServerBotManager? supplied = null)
    {
        if (!_botRosterDirty || !_network.LobbyAdmissionOpen) return;
        ServerBotManager? bots = supplied ?? _botManager?.Invoke();
        if (bots == null) return;
        bots.ApplyLobbyPolicy(_lobby.BotPolicy);
        bots.UpdateLobby(_network);
        _lobby.SyncBots(bots.Participants);
        _botRosterDirty = false;
    }

    private bool ReceiveChat(ServerPeer peer, LobbyChatRequestPacket request)
    {
        if (!_network.TryAcceptLobbyChat(peer)) return false;
        LobbyChatDispatch result = _lobby.ReceiveChat(peer, request);
        if (!result.Accepted) return EnqueueFeedback(peer, result.Feedback);
        Span<byte> bytes = stackalloc byte[LobbyChatPacket.Size];
        result.Chat!.Value.Write(bytes);
        foreach (ulong connectionId in result.Recipients)
        {
            ServerPeer? recipient = _network.Find(connectionId);
            if (recipient == null || !recipient.Connection.Reliable.TryEnqueue(
                ReliableEventType.LobbyChat, bytes, out _)) continue;
        }
        return true;
    }

    private bool EnqueueFeedback(ServerPeer peer, LobbyFeedbackPacket feedback)
    {
        Span<byte> bytes = stackalloc byte[LobbyFeedbackPacket.Size];
        feedback.Write(bytes);
        return peer.Connection.Reliable.TryEnqueue(ReliableEventType.LobbyFeedback, bytes, out _);
    }

    private void SynchronizePeers()
    {
        foreach (ServerPeer? peer in _network.AllConnections) if (peer != null) SynchronizePeer(peer);
    }

    private void SynchronizePeer(ServerPeer peer)
    {
        if (!_lobby.Runtime.TryFind(peer.Connection.Id, out LobbyPlayer member)) return;
        peer.Hunter = member.Hunter;
        peer.TeamIndex = member.Team;
        _lobby.RefreshPeer(peer);
        _network.InvalidateRoster();
    }
}
