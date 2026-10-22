using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private sealed class PendingVoteMap
    {
        internal readonly Peer? Proposer;
        internal readonly uint ClientId;
        internal readonly string Room, OriginalRoom;
        internal readonly GameMode Mode;
        internal readonly NetworkMapIdentity RequiredMap;
        internal readonly ushort MatchId;
        internal readonly ulong Epoch;
        internal readonly bool FinalCommit;
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly Task<MapVoteReadiness.Result> Work;

        internal PendingVoteMap(Peer? proposer, string room, GameMode mode,
            NetworkMapIdentity requiredMap, string originalRoom, ushort matchId,
            ulong epoch, bool finalCommit)
        {
            Proposer = proposer;
            ClientId = proposer?.ClientId ?? 0;
            Room = room;
            Mode = mode;
            RequiredMap = requiredMap;
            OriginalRoom = originalRoom;
            MatchId = matchId;
            Epoch = epoch;
            FinalCommit = finalCommit;
            var token = Cancellation.Token;
            Work = Task.Run(() => MapVoteReadiness.Prepare(room, requiredMap, token), token);
        }
    }

    private PendingVoteMap? _pendingVoteMap;
    private NetworkMapIdentity _approvedVoteMap;

    private void CancelPendingVoteMap()
    {
        if (_pendingVoteMap is not { } pending) return;
        _pendingVoteMap = null;
        pending.Cancellation.Cancel();
        if (pending.Work.IsCompleted)
            pending.Cancellation.Dispose();
        else
            _ = pending.Work.ContinueWith(task =>
            {
                _ = task.Exception; // observe only faults, if any; Prepare catches expected failures
                pending.Cancellation.Dispose();
            }, TaskScheduler.Default);
    }

    private void PumpPendingVoteMap(double now)
    {
        if (_pendingVoteMap is not { } pending || !pending.Work.IsCompleted)
            return;
        _pendingVoteMap = null;
        MapVoteReadiness.Result result;
        try { result = pending.Work.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { result = MapVoteReadiness.Result.Refuse("Map preparation cancelled."); }
        catch (Exception ex) { result = MapVoteReadiness.Result.Refuse("Map preparation failed: " + ex.Message); }
        finally { pending.Cancellation.Dispose(); }

        // A cancelled, timed-out or superseded worker may finish after the
        // server has moved to another match. Never install its vote late.
        if (!_running || _phase != SessionPhase.InMatch
            || _matchId != pending.MatchId || _authorityEpoch != pending.Epoch
            || !CurrentDefinition.RoomKey.Equals(pending.OriginalRoom, StringComparison.OrdinalIgnoreCase))
            return;

        if (!result.Ready)
        {
            Log("[vote] rejected " + pending.Room + ": " + result.Reason);
            if (pending.FinalCommit)
            {
                _voteResult = VoteStatePacket.StateFailed;
                Announce("map vote failed: " + result.Reason);
                BroadcastVoteState(now);
            }
            else if (pending.Proposer != null && _peers.Contains(pending.Proposer))
                Tell(pending.Proposer, "map cannot be voted in: " + result.Reason);
            return;
        }

        if (pending.FinalCommit)
        {
            Log("[vote] verified map " + pending.Room + " for rotation");
            _voteResult = VoteStatePacket.StatePassed;
            Announce("map verified -- changing to " + Launcher.LauncherRoomDisplayName.Get(pending.Room));
            BroadcastVoteState(now);
            _rotation.PlayNext(pending.Room, pending.Mode);
            if (SessionPolicy == ServerSessionPolicy.Lobby)
                EndMatch(now, "map vote passed");
            else
                AdvanceMap(now);
            return;
        }

        Peer? peer = pending.Proposer;
        if (peer == null || !_peers.Contains(peer) || peer.ClientId != pending.ClientId
            || _voteRunning || !AllowMapVotes || _peers.Count < VoteMinimumPlayers
            || now - _voteResolvedAt < VoteCooldownSeconds
            || now - peer.LastProposal < ProposalCooldownSeconds)
            return;

        // The received immutable identity is a generation fence: do not open
        // a ballot if its catalog identity was replaced while the worker ran.
        try
        {
            if (NetworkMapIdentity.ForRoom(pending.Room) != pending.RequiredMap)
            {
                Tell(peer, "the map identity changed during preparation");
                return;
            }
            if (!Mods.Multiplayer.MapModeCapabilities.Supports(pending.Room,
                pending.Mode, LobbyRules.ResolveWorldProfile(CurrentDefinition, _maxPlayers), out string why))
            {
                Tell(peer, why);
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
            or InvalidOperationException)
        {
            Tell(peer, "the map is no longer available: " + ex.Message);
            return;
        }
        BeginPreparedVote(peer, pending.Room, pending.Mode, pending.RequiredMap, now);
    }
}
