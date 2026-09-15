using MphRead.Identity;

namespace MphRead.Backend.Matches;

public static class ReportValidation
{
    public const int MaximumBytes = 512 * 1024;
    public const uint MaximumTicks = int.MaxValue; // Strictly below uint half-range for unambiguous tick order.
    public const float MaximumModeSeconds = MaximumTicks / 60f;
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(MaximumTicks / 60d);

    public static bool Validate(MatchReportV1 report, Guid serverId, MatchTrustClass trust, DateTimeOffset now)
    {
        if (!report.IsValid) return false;
        try { EnhancedHuntersRankingPolicy.Validate(report.Rules); }
        catch (ArgumentException) { return false; }
        if (report.ServerId != serverId
            || report.PlayedTicks > MaximumTicks || report.Rules.RoomKey.Length > 128
            || report.EndedAtUtc > now.AddMinutes(5) || report.EndedAtUtc - report.StartedAtUtc > MaximumDuration
            || report.Participants.Count(x => x.StartedMatch) > report.Rules.MaxPlayers) return false;
        foreach (var participant in report.Participants)
        {
            if (participant.PlayedTicks > report.PlayedTicks
                || participant.Spans.Any(x => !PlayableHunterCatalog.IsPlayable(x.Hunter))
                || participant.DisplayName.Any(char.IsControl)) return false;
            var m = participant.Metrics;
            int[] counts = [m.Kills, m.Deaths, m.Assists, m.DamageDealt, m.LongestKillStreak, m.HeadshotKills,
                m.Suicides, m.FriendlyKills, m.OctolithScores, m.OctolithDrops, m.OctolithStops, m.NodesCaptured,
                m.NodesLost, m.KillsAsPrime, m.PrimesKilled, m.BeamDamageMax, m.BeamDamageDealt,
                m.DamageCount, m.AltDamageCount, m.KillStreak];
            if (counts.Any(x => x < 0)
                || m.ModeTimeSeconds is < 0 or > MaximumModeSeconds || m.Standing is < 0 or > 7
                || m.TeamStanding is < 0 or > 7 || m.HeadshotKills > m.Kills
                || m.BipedKills > m.Kills || m.AltFormKills > m.Kills
                || m.BipedKills.HasValue && m.AltFormKills.HasValue && (long)m.BipedKills.Value + m.AltFormKills.Value > m.Kills
                || m.BeamKills.Sum(kills => (long)kills) > m.Kills
                || (m.Shots.HasValue && m.Hits > m.Shots)) return false;
            if (participant.Spans.Sum(s => (long)s.PlayedTicks) != participant.PlayedTicks) return false;
            uint? previousEnd = null;
            foreach (var span in participant.Spans)
            {
                uint duration = unchecked(span.LeftTick!.Value - span.JoinedTick);
                if (span.PlayedTicks > duration || duration > MaximumTicks || (previousEnd.HasValue
                    && unchecked(span.JoinedTick - previousEnd.Value) > MaximumTicks)) return false;
                previousEnd = span.LeftTick;
            }
        }
        // Registered IDs never attach to bots or guests (shared validator), and an active slot
        // cannot be claimed by two participants at the same time, including across tick wrap.
        var spans = report.Participants.SelectMany(p => p.Spans).ToArray();
        uint origin = spans[0].JoinedTick;
        // Signed distances from one anchor distinguish a legitimate uint wrap from an
        // impossible interval. The complete report must remain inside the signed tick half-range.
        long earliest = 0, latest = 0;
        foreach (var group in spans.GroupBy(s => s.Slot))
        {
            long? previousEnd = null;
            foreach (var span in group.OrderBy(s => unchecked((int)(s.JoinedTick - origin))))
            {
                long start = unchecked((int)(span.JoinedTick - origin));
                long end = start + unchecked(span.LeftTick!.Value - span.JoinedTick);
                if (Math.Abs(start) > MaximumTicks || Math.Abs(end) > MaximumTicks
                    || (previousEnd.HasValue && start < previousEnd.Value)) return false;
                earliest = Math.Min(earliest, start); latest = Math.Max(latest, end);
                if (latest - earliest > MaximumTicks) return false;
                previousEnd = end;
            }
        }
        return true;
    }
}
