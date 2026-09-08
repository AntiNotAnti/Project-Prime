using MphRead.Identity;

namespace FruityPrime.Server.Shared;

/// <summary>Frozen placement/report identity checks shared by artifact producer and Node ingestion.</summary>
public static class MatchReportBinding
{
    public static void Validate(MatchSpec spec, uint wireMatchId, MatchReportV1 report)
    {
        spec.Validate();
        if (!report.IsValid) throw new ArgumentException(InvalidEvidenceCategory(report));
        if (report.MatchId != spec.MatchId.Value || report.WireMatchId != wireMatchId
            || report.ServerId != spec.NodeId.Value || report.ServerIncarnation != spec.NodeIncarnation
            || report.BuildVersion != spec.Content.BuildVersion || report.ProtocolVersion != spec.Content.ProtocolVersion
            || report.ContentHash != spec.Content.ContentHash || report.TrustClass != spec.TrustClass
            || report.Rules != spec.Rules || report.TournamentId != spec.TournamentId?.ToString("D")
            || report.RoundId != spec.RoundId?.ToString("D")) throw new ArgumentException("Report does not match frozen launch identity.");
        foreach (var participant in report.Participants)
        foreach (var span in participant.Spans)
        {
            var seat = spec.Roster.FirstOrDefault(s => s.SeatId == span.Slot && s.Role != SeatRole.Observer);
            if (seat == null && participant.Kind == ParticipantKind.Bot && spec.BotFillPolicy == BotFillPolicy.FillVacancies) continue;
            if (seat == null || participant.PlayerId != seat.PlayerId
                || (participant.Kind == ParticipantKind.Bot) != (seat.Role == SeatRole.Bot)
                || span.Hunter != seat.Hunter
                // In free-for-all gameplay TeamIndex is the seat, not the lobby's two-team preference.
                || span.TeamIndex != (spec.Rules.Teams ? seat.Team : seat.SeatId))
                throw new ArgumentException("Report participant is outside the frozen roster.");
        }
    }
    private static string InvalidEvidenceCategory(MatchReportV1 report)
    {
        if (report.Participants.IsDefaultOrEmpty || report.Participants.Length > MatchReportV1.MaximumParticipants)
            return "Invalid report participant collection.";
        foreach (var participant in report.Participants)
        {
            if (participant == null || participant.Spans.IsDefaultOrEmpty) return "Invalid report participant identity or spans.";
            ulong played = 0;
            foreach (var span in participant.Spans)
            {
                if (!span.LeftTick.HasValue || !span.ExitReason.HasValue) return "Invalid report unclosed participant span.";
                if (span.PlayedTicks > unchecked(span.LeftTick.Value - span.JoinedTick))
                    return "Invalid report span: played ticks exceed exclusive duration.";
                played += span.PlayedTicks;
            }
            if (played != participant.PlayedTicks) return "Invalid report participant played tick sum.";
            var metrics = participant.Metrics;
            if (metrics == null || metrics.BeamKills.IsDefault || metrics.BeamKills.Length != 9
                || !float.IsFinite(metrics.ModeTimeSeconds) || metrics.Kills < 0 || metrics.Deaths < 0 || metrics.Assists < 0
                || metrics.DamageDealt < 0 || metrics.HeadshotKills < 0 || metrics.LongestKillStreak < 0
                || metrics.BipedKills < 0 || metrics.AltFormKills < 0 || metrics.Shots < 0 || metrics.Hits < 0
                || metrics.BeamKills.Any(value => value < 0)) return "Invalid report participant metrics.";
        }
        return "Invalid report envelope, participant identity, or outcome.";
    }

}
