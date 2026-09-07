using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using MphRead.Identity;

namespace MphRead.Mods.Network;

public static class LobbyMatchSummary
{
    public static MatchSummaryPacket Create(uint sessionId, uint lobbyRevision, MatchResult result,
        MatchSummaryFlags flags = MatchSummaryFlags.None, MatchReportV1? report = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (report != null && (!report.IsValid || report.WireMatchId != result.MatchId))
            throw new ArgumentException("Match report must be valid and belong to the completed match.", nameof(report));
        ImmutableArray<MatchSummaryRow> reportRows = report == null
            ? ImmutableArray<MatchSummaryRow>.Empty : CreateReportRows(result.Rules.Mode, report);
        if (!reportRows.IsDefaultOrEmpty) return CreatePacket(sessionId, lobbyRevision, result, flags, reportRows);

        var active = result.Players.Where(player => player.Active).Take(MatchSummaryPacket.MaximumRows).ToArray();
        var placements = new Dictionary<int, byte>();
        byte place = 1;
        foreach (int slot in result.ResultSlots)
        {
            if (active.Any(player => player.Slot == slot) && !placements.ContainsKey(slot))
                placements.Add(slot, place++);
        }
        foreach (PlayerMatchResult player in active)
            if (!placements.ContainsKey(player.Slot)) placements.Add(player.Slot, place++);

        var rows = ImmutableArray.CreateBuilder<MatchSummaryRow>(active.Length);
        foreach (PlayerMatchResult player in active.OrderBy(player => placements[player.Slot]))
        {
            ObjectiveValues(result.Rules.Mode, player, out int primary, out int secondary, out int tertiary);
            rows.Add(new MatchSummaryRow((byte)player.Slot, player.Nickname, player.Hunter,
                (byte)Math.Clamp(player.TeamIndex, 0, MatchSummaryPacket.MaximumRows - 1),
                placements[player.Slot], player.IsBot, player.Points, player.Kills, player.Deaths,
                player.Assists, player.DamageDealt, player.HeadshotKills, primary, secondary, tertiary));
        }
        return CreatePacket(sessionId, lobbyRevision, result, flags, rows.MoveToImmutable());
    }

    private static ImmutableArray<MatchSummaryRow> CreateReportRows(MatchMode mode, MatchReportV1 report)
    {
        var participants = report.Participants
            .Where(participant => participant.StartedMatch || participant.PlayedTicks != 0)
            .Select((participant, order) => (Participant: participant, Order: order))
            .OrderBy(value => value.Participant.Outcome == ParticipantOutcome.Finished ? 0 : 1)
            .ThenBy(value => value.Participant.Metrics.Standing <= 0
                ? Int32.MaxValue : value.Participant.Metrics.Standing)
            .ThenBy(value => value.Order)
            .Take(MatchSummaryPacket.MaximumRows)
            .ToArray();
        var rows = ImmutableArray.CreateBuilder<MatchSummaryRow>(participants.Length);
        byte placement = 1;
        foreach ((MatchReportParticipant participant, _) in participants)
        {
            MatchParticipationSpan span = participant.Spans[^1];
            MatchReportMetrics metrics = participant.Metrics;
            ObjectiveValues(mode, metrics, out int primary, out int secondary, out int tertiary);
            rows.Add(new MatchSummaryRow(span.Slot, participant.DisplayName, span.Hunter,
                (byte)Math.Clamp(span.TeamIndex, 0, MatchSummaryPacket.MaximumRows - 1),
                placement++, participant.Kind == ParticipantKind.Bot, metrics.Points, metrics.Kills,
                metrics.Deaths, metrics.Assists, metrics.DamageDealt, metrics.HeadshotKills,
                primary, secondary, tertiary));
        }
        return rows.MoveToImmutable();
    }

    private static MatchSummaryPacket CreatePacket(uint sessionId, uint lobbyRevision, MatchResult result,
        MatchSummaryFlags flags, ImmutableArray<MatchSummaryRow> rows)
    {
        uint duration = result.CompletedAtSimulationTime <= 0 ? 0
            : (uint)Math.Min(UInt32.MaxValue, Math.Round(result.CompletedAtSimulationTime));
        return new MatchSummaryPacket(sessionId, lobbyRevision, result.MatchId, duration,
            result.Rules.Mode, result.EndReason, flags, result.Rules.RoomKey, rows);
    }

    private static void ObjectiveValues(MatchMode mode, PlayerMatchResult player,
        out int primary, out int secondary, out int tertiary)
    {
        primary = secondary = tertiary = 0;
        switch (mode)
        {
            case MatchMode.Capture:
            case MatchMode.Bounty:
            case MatchMode.TeamBounty:
                primary = player.OctolithScores;
                secondary = player.OctolithStops;
                tertiary = player.OctolithDrops;
                break;
            case MatchMode.Nodes:
            case MatchMode.TeamNodes:
                primary = player.NodesCaptured;
                secondary = player.NodesLost;
                break;
            case MatchMode.PrimeHunter:
                primary = player.KillsAsPrime;
                secondary = player.PrimesKilled;
                break;
        }
    }

    private static void ObjectiveValues(MatchMode mode, MatchReportMetrics metrics,
        out int primary, out int secondary, out int tertiary)
    {
        primary = secondary = tertiary = 0;
        switch (mode)
        {
            case MatchMode.Capture:
            case MatchMode.Bounty:
            case MatchMode.TeamBounty:
                primary = metrics.OctolithScores;
                secondary = metrics.OctolithStops;
                tertiary = metrics.OctolithDrops;
                break;
            case MatchMode.Nodes:
            case MatchMode.TeamNodes:
                primary = metrics.NodesCaptured;
                secondary = metrics.NodesLost;
                break;
            case MatchMode.PrimeHunter:
                primary = metrics.KillsAsPrime;
                secondary = metrics.PrimesKilled;
                break;
        }
    }
}
