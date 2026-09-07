using Microsoft.EntityFrameworkCore;
using MphRead.Backend.Data;
using MphRead.Identity;

namespace MphRead.Backend.Matches;

public static class CareerProjection
{
    // Internal projection scope, never a claimed server trust class.
    public const int OfficialScope = -1;
    public static bool IsOfficial(MatchTrustClass trust) => trust is MatchTrustClass.VerifiedCasual or MatchTrustClass.Ranked;

    public static bool IsEligible(MatchReportV1 report) => report.EndReason is not (MatchEndReason.Forced or MatchEndReason.InvalidTeams)
        && report.TrustClass != MatchTrustClass.Practice;

    public static async Task ApplyAsync(BackendDbContext db, AcceptedMatch accepted, MatchReportV1 report, CancellationToken ct)
    {
        foreach (var participant in report.Participants.Where(p => p.PlayerId.HasValue))
        {
            Guid playerId = participant.PlayerId!.Value.Value;
            var m = participant.Metrics;
            bool finished = participant.Outcome == ParticipantOutcome.Finished;
            int standing = report.Rules.Teams ? m.TeamStanding : m.Standing;
            var opponents = report.Participants.Where(p => p.ParticipantId != participant.ParticipantId
                && (!report.Rules.Teams || p.Spans[0].TeamIndex != participant.Spans[0].TeamIndex)).ToArray();
            bool tied = finished && standing == 0 && opponents.Any(p => p.Outcome == ParticipantOutcome.Finished
                && (report.Rules.Teams ? p.Metrics.TeamStanding : p.Metrics.Standing) == standing);
            bool won = finished && standing == 0 && !tied && opponents.Length > 0;
            db.Participations.Add(new CareerParticipation
            {
                MatchId = report.MatchId, PlayerId = playerId, ProcessingOrder = accepted.ProcessingOrder,
                Eligible = accepted.CareerEligible, Won = won, Tied = tied, Outcome = (int)participant.Outcome,
                PlayedTicks = participant.PlayedTicks, Kills = m.Kills, Deaths = m.Deaths, Assists = m.Assists, Damage = m.DamageDealt
            });
            if (!accepted.CareerEligible) continue;
            foreach (int trust in IsOfficial(report.TrustClass) ? new[] { (int)report.TrustClass, OfficialScope } : new[] { (int)report.TrustClass })
            {
                foreach (var dimension in new[] { ("career", "all"), ("map", report.Rules.RoomKey), ("mode", ((int)report.Rules.Mode).ToString()) })
                {
                    var aggregate = await GetAsync(db, playerId, trust, dimension.Item1, dimension.Item2, ct);
                    aggregate.Matches++; aggregate.Wins += won ? 1 : 0; aggregate.Ties += tied ? 1 : 0;
                    aggregate.PlayedTicks += participant.PlayedTicks; aggregate.Kills += m.Kills;
                    aggregate.Deaths += m.Deaths; aggregate.Assists += m.Assists; aggregate.Damage += m.DamageDealt;
                    aggregate.OctolithScores += m.OctolithScores; aggregate.NodesCaptured += m.NodesCaptured; aggregate.KillsAsPrime += m.KillsAsPrime;
                    aggregate.HeadshotKills += m.HeadshotKills; aggregate.BipedKills += m.BipedKills; aggregate.AltFormKills += m.AltFormKills;
                    aggregate.LongestKillStreak = Math.Max(aggregate.LongestKillStreak, m.LongestKillStreak);
                    aggregate.CurrentWinStreak = won ? aggregate.CurrentWinStreak + 1 : 0;
                    aggregate.LongestWinStreak = Math.Max(aggregate.LongestWinStreak, aggregate.CurrentWinStreak);
                    aggregate.OutcomeSamples++;
                }
                // A report gives cumulative combat counters, not counters for each hunter interval.
                foreach (var group in participant.Spans.GroupBy(s => s.Hunter))
                {
                    var aggregate = await GetAsync(db, playerId, trust, "hunter", ((int)group.Key).ToString(), ct);
                    aggregate.Matches++;
                    aggregate.PlayedTicks += group.Sum(s => (long)s.PlayedTicks);
                    if (participant.Spans.All(s => s.Hunter == group.Key))
                    {
                        aggregate.OutcomeSamples++; aggregate.Wins += won ? 1 : 0; aggregate.Ties += tied ? 1 : 0;
                        aggregate.Kills += m.Kills; aggregate.Deaths += m.Deaths; aggregate.Assists += m.Assists;
                        aggregate.Damage += m.DamageDealt; aggregate.OctolithScores += m.OctolithScores; aggregate.NodesCaptured += m.NodesCaptured; aggregate.KillsAsPrime += m.KillsAsPrime;
                        aggregate.HeadshotKills += m.HeadshotKills;
                        aggregate.BipedKills += m.BipedKills; aggregate.AltFormKills += m.AltFormKills;
                    }
                }
                for (int i = 0; i < m.BeamKills.Length; i++)
                {
                    var aggregate = await GetAsync(db, playerId, trust, "weapon", i.ToString(), ct);
                    aggregate.Matches++; aggregate.Kills += m.BeamKills[i];
                }
            }
        }
    }

    private static async Task<CareerAggregate> GetAsync(BackendDbContext db, Guid player, int trust,
        string dimension, string key, CancellationToken ct)
    {
        var value = await db.Aggregates.FindAsync([player, trust, dimension, key], ct);
        if (value != null) return value;
        value = new() { PlayerId = player, TrustClass = trust, Dimension = dimension, Key = key };
        db.Aggregates.Add(value); return value;
    }
}
