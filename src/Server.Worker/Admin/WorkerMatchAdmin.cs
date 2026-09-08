using System.Linq;
using FruityPrime.Server.Shared;
using MphRead.Identity;
using MphRead.Mods.Network;

namespace MphRead.Admin;

public sealed record WorkerMatchAdminResult(bool Applied, string? Code = null, string? Message = null);

/// <summary>Invoked only on the match lane, after authenticated Node IPC routing.</summary>
public static class WorkerMatchAdmin
{
    public static WorkerMatchAdminResult Apply(MatchInstance match, MatchAdminCommand command)
    {
        if (command.MatchId.Value != match.MatchId) return new(false, "match", "The command targets another match.");
        if (match.State != MatchInstanceState.Running) return new(false, "phase", "The match is not running.");
        switch (command.Action)
        {
            case AdminAction.Pause:
            case AdminAction.Resume:
                return new(false, "node_owned", "Between-round pause and resume belong to the Node lobby; active simulation pause is unsupported.");
            case AdminAction.EndMatch:
                if (match.Simulation.Scene.Match.Phase != MatchPhase.Playing) return new(false, "phase", "Only a playing match can end through this command.");
                match.Simulation.Scene.Match.ForceEndGame = true;
                return new(true);
            case AdminAction.KickSeat:
                var seat = match.Spec.Roster.FirstOrDefault(s => s.SeatId == command.SeatId);
                if (seat == null) return new(false, "seat", "Unknown frozen roster seat.");
                if (seat.Role == SeatRole.Bot) return new(false, "unsupported", "Frozen bot roster changes require a new Node match specification.");
                var peer = match.Network.AllConnections.ToArray().FirstOrDefault(p => p != null && (seat.PlayerId is { } player
                    ? p.PlayerId == player && p.GuestSessionId == null
                    : seat.GuestSessionId is { } guest && p.GuestSessionId == guest && p.PlayerId == null));
                if (peer == null) return new(false, "seat", "The seat is not connected.");
                match.Network.Remove(peer.ConnectionIndex, allowReconnect: false, reason: ParticipantExitReason.ExplicitLeave);
                return new(true);
            default: return new(false, "unsupported", "Unsupported match admin action.");
        }
    }
}
