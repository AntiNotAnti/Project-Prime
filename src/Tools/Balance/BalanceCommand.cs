using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Identity;
using MphRead.Telemetry;

namespace MphRead;

internal static class BalanceCommand
{
    internal sealed record HunterSummary(Hunter Hunter, int Picks, int EligibleResults, int Wins, int Ties,
        long Kills, long Deaths, long Damage, double MeasuredMinutes, double? WinRate, double? PickRate,
        double? KillDeathRatio, double? DamagePerMinute, bool SufficientResults);
    internal sealed record TeamSummary(int Team, int Matches, int Wins, int Ties, double? WinRate);
    internal sealed record ReportSummary(int Format, string Cohort, string Build, string Content, string Room,
        MatchMode Mode, string Ruleset, string ClaimedTrust, int Matches, int MinimumMatches, bool SufficientMatches,
        double AverageDurationSeconds, int ExcludedChangingHunterMetrics, HunterSummary[] Hunters,
        TeamSummary[] Teams, long[] WeaponKills);
    private sealed class HunterCounts
    { public int Picks, Results, Wins, Ties; public long Kills, Deaths, Damage, Ticks; }
    internal static string Cohort(MatchReportV1 report)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { report.BuildVersion, report.ContentHash, report.Rules, report.TrustClass }))));
    internal static ReportSummary Aggregate(IReadOnlyList<MatchReportV1> reports, int minimum)
    {
        if (reports.Count == 0 || minimum < 1) throw new ArgumentException("Nonempty cohort and positive minimum required.");
        string key = Cohort(reports[0]);
        if (reports.Any(r => !r.IsValid || Cohort(r) != key || r.TrustClass == MatchTrustClass.Practice || r.Participants.Any(p => p.Kind == ParticipantKind.Bot)))
            throw new InvalidDataException("Cohort must contain valid, comparable, human-only reports.");
        var hunters = new Dictionary<Hunter, HunterCounts>(); var teams = new Dictionary<int, (int Matches,int Wins,int Ties)>();
        long[] weapons = new long[9]; int changing = 0, picks = 0;
        foreach (var report in reports)
        {
            var finished = report.Participants.Where(p => p.Outcome == ParticipantOutcome.Finished).ToArray();
            var winners = finished.Where(p => (report.Rules.Teams ? p.Metrics.TeamStanding : p.Metrics.Standing) == 0).ToArray();
            bool tied = report.Rules.Teams ? winners.Select(p => p.Spans[^1].TeamIndex).Distinct().Count() > 1 : winners.Length > 1;
            foreach (var participant in report.Participants)
            {
                var selected = participant.Spans.Select(s => s.Hunter).Distinct().ToArray();
                foreach (Hunter hunter in selected)
                { if (!hunters.TryGetValue(hunter, out var h)) hunters.Add(hunter,h = new()); h.Picks++; picks++; }
                for (int w = 0; w < 9; w++) weapons[w] += participant.Metrics.BeamKills[w];
                if (selected.Length != 1) { changing++; continue; }
                var value = hunters[selected[0]]; value.Kills += participant.Metrics.Kills; value.Deaths += participant.Metrics.Deaths;
                value.Damage += participant.Metrics.DamageDealt; value.Ticks += participant.PlayedTicks;
                if (participant.Outcome != ParticipantOutcome.Finished) continue;
                value.Results++;
                if (winners.Contains(participant)) { if (tied) value.Ties++; else value.Wins++; }
            }
            if (report.Rules.Teams && finished.All(p => p.Spans.Select(s => s.TeamIndex).Distinct().Count() == 1))
            {
                var present = finished.Select(p => p.Spans[^1].TeamIndex).Distinct().ToArray();
                if (present.Length == 2)
                    foreach (int team in present)
                    {
                        var v = teams.GetValueOrDefault(team); v.Matches++;
                        if (winners.Any(p => p.Spans[^1].TeamIndex == team)) { if (tied) v.Ties++; else v.Wins++; }
                        teams[team] = v;
                    }
            }
        }
        var first = reports[0];
        return new(1,key,first.BuildVersion,first.ContentHash ?? "unavailable",first.Rules.RoomKey,first.Rules.Mode,first.Ruleset,
            first.TrustClass.ToString(),reports.Count,minimum,reports.Count >= minimum,reports.Average(r => r.PlayedTicks / 60d),changing,
            hunters.OrderBy(p=>p.Key).Select(p=>new HunterSummary(p.Key,p.Value.Picks,p.Value.Results,p.Value.Wins,p.Value.Ties,
                p.Value.Kills,p.Value.Deaths,p.Value.Damage,p.Value.Ticks / 3600d,
                p.Value.Results == 0 ? null : (double)p.Value.Wins/p.Value.Results,
                picks == 0 ? null : (double)p.Value.Picks/picks,p.Value.Deaths == 0 ? null : (double)p.Value.Kills/p.Value.Deaths,
                p.Value.Ticks == 0 ? null : p.Value.Damage * 3600d/p.Value.Ticks,p.Value.Results >= minimum)).ToArray(),
            teams.OrderBy(p=>p.Key).Select(p=>new TeamSummary(p.Key,p.Value.Matches,p.Value.Wins,p.Value.Ties,
                p.Value.Matches == 0 ? null : (double)p.Value.Wins/p.Value.Matches)).ToArray(),weapons);
    }
    public static int Run(string[] args)
    {
        if (args.Length is < 3 or > 5) throw new ArgumentException("Tools balance REPORT_DIRECTORY OUTPUT_PREFIX [TELEMETRY_DIRECTORY] [MIN_MATCHES=30]");
        int minimum = args.Length == 5 ? int.Parse(args[4]) : 30;
        if (minimum is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(minimum));
        var reports = new Dictionary<Guid,(string Hash,MatchReportV1 Report)>(); var hashes = new Dictionary<Guid,string>(); long bytes = 0; int excluded = 0, fileCount = 0;
        foreach (string file in Directory.EnumerateFiles(args[1],"*.json").Take(1025).OrderBy(p => p, StringComparer.Ordinal))
        {
            if (++fileCount > 1024) throw new InvalidDataException("At most 1024 report files per run.");
            long length = new FileInfo(file).Length; bytes += length;
            if (length > 1024 * 1024 || bytes > 128 * 1024 * 1024) throw new InvalidDataException("Report input exceeds bounded bytes.");
            byte[] body = File.ReadAllBytes(file);
            var report = JsonSerializer.Deserialize<MatchReportV1>(body) ?? throw new InvalidDataException("Missing report.");
            if (!report.IsValid) throw new InvalidDataException("Invalid report: " + Path.GetFileName(file));
            string hash = Convert.ToHexString(SHA256.HashData(body));
            if (hashes.TryGetValue(report.MatchId,out var existing))
            { if (existing != hash) throw new InvalidDataException("Conflicting match UUID."); continue; }
            hashes.Add(report.MatchId,hash);
            if (report.TrustClass == MatchTrustClass.Practice || report.Participants.Any(p=>p.Kind == ParticipantKind.Bot)) { excluded++; continue; }
            reports.Add(report.MatchId,(hash,report));
        }
        var summaries = reports.Values.Select(p=>p.Report).OrderBy(r=>r.MatchId).GroupBy(Cohort).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>Aggregate(g.ToArray(),minimum)).ToArray();
        int excludedTelemetry = 0;
        var telemetry = args.Length >= 4 ? ReadTelemetry(args[3], reports, minimum, out excludedTelemetry) : Array.Empty<TelemetrySummary>();
        string prefix = Path.GetFullPath(args[2]); Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);
        File.WriteAllText(prefix+".json",JsonSerializer.Serialize(new { format=1, validation="Local schema validation only; reporter trust is not authenticated by this tool.",
            excludedBotOrPracticeMatches=excluded, excludedTelemetry, cohorts=summaries, telemetry },new JsonSerializerOptions{WriteIndented=true}));
        var md = new StringBuilder("# Competitive balance evidence\n\nNo balance changes are proposed automatically. Inputs are locally validated raw reports; claimed server trust is not authenticated here.\n\n");
        md.AppendLine($"Accepted matches: {reports.Count}. Excluded bot/Practice matches: {excluded}. Minimum descriptive sample threshold: {minimum} (not a statistical significance test).\n");
        foreach (var group in summaries)
        {
            md.AppendLine($"## {group.Room} / {group.Mode} / {group.Ruleset}\n\nCohort `{group.Cohort}`; build `{group.Build}`; content `{group.Content}`; claimed trust {group.ClaimedTrust}. {group.Matches} matches: {(group.SufficientMatches ? "threshold met" : "INSUFFICIENT SAMPLE")}. Mean duration {group.AverageDurationSeconds:F1} seconds.\n");
            md.AppendLine("| Hunter | Picks | Finished results | Wins | Ties | K/D | Damage/min | Evidence |\n|---|---:|---:|---:|---:|---:|---:|---|");
            foreach (var h in group.Hunters) md.AppendLine($"| {h.Hunter} | {h.Picks} | {h.EligibleResults} | {h.Wins} | {h.Ties} | {h.KillDeathRatio?.ToString("F2") ?? "unavailable"} | {h.DamagePerMinute?.ToString("F1") ?? "unavailable"} | {(h.SufficientResults ? "threshold met" : "insufficient")} |");
            md.AppendLine($"\nChanging-Hunter participant totals excluded from per-Hunter K/D/damage/wins: {group.ExcludedChangingHunterMetrics}. Pick rate counts distinct Hunter selections per participant; it is not time share. Departures contribute measured K/D and damage but never a terminal win. Ties are separate from wins. Team results describe team index, not a physical spawn side or causal bias.\n");
        }
        md.AppendLine($"Joined complete lossless telemetry: {telemetry.Sum(t => t.Matches)}; unmatched, incomplete or lossy captures excluded: {excludedTelemetry}. Overtime frequency is reported separately per comparable cohort. Detailed weapon pickup/damage and spawn danger denominators are in the JSON. Missing telemetry means unavailable, never zero.\n");
        md.AppendLine("See docs/G5_BALANCE_CHANGE_TEMPLATE.md before proposing a versioned change. Mixed builds/rules/content/trust remain separate cohorts. No live samples are bundled with this tool.");
        File.WriteAllText(prefix+".md",md.ToString()); return 0;
    }
    internal sealed record WeaponSummary(byte Weapon,long Pickups,long Kills,long Damage,double? PickupsPerMinute,double? DamageShare);
    internal sealed record TelemetrySummary(string Cohort,int Matches,int OvertimeMatches,double? OvertimeFrequency,
        WeaponSummary[] Weapons,int Spawns,int Eligible3,int Deaths3,int Eligible5,int Deaths5,int Eligible10,int Deaths10,int MinimumMatches,bool SufficientMatches);
    private sealed class TelemetryCounts
    {
        public int Matches, Overtime, Spawns, Eligible3,Deaths3,Eligible5,Deaths5,Eligible10,Deaths10;
        public double Minutes;
        public Dictionary<byte,(long Pickups,long Kills,long Damage)> Weapons = new();
    }
    private static TelemetrySummary[] ReadTelemetry(string directory, Dictionary<Guid,(string Hash,MatchReportV1 Report)> reports, int minimum, out int excluded)
    {
        excluded = 0;
        var groups = new Dictionary<string,TelemetryCounts>(); var seen = new HashSet<Guid>(); int files = 0;
        foreach(string file in Directory.EnumerateFiles(directory,"*.telemetry.json.gz").Take(1025).OrderBy(p=>p,StringComparer.Ordinal))
        {
            if (++files > 1024) throw new InvalidDataException("At most 1024 telemetry files per run.");
            var match = TelemetryCommand.Read(file);
            if (!reports.TryGetValue(match.Id,out var source)) { excluded++; continue; }
            if (!seen.Add(match.Id)) throw new InvalidDataException("Duplicate telemetry match UUID.");
            if (!match.Completed || match.DroppedEvents != 0 || match.Room != source.Report.Rules.RoomKey || match.Mode != source.Report.Rules.Mode)
                { excluded++; continue; }
            string cohort = Cohort(source.Report);
            if (!groups.TryGetValue(cohort,out var total)) groups.Add(cohort,total=new());
            total.Matches++; total.Minutes += source.Report.PlayedTicks/3600d;
            bool overtime = false; var spawns = new Dictionary<(byte,uint),TelemetryEvent>();
            var deaths = new Dictionary<(byte,uint),uint>();
            foreach(var e in match.Events)
            {
                if (e.Kind == TelemetryKind.Spawn) spawns[(e.Slot,e.Life)] = e;
                if (e.Kind == TelemetryKind.Death) deaths.TryAdd((e.Slot,e.Life),e.Tick);
                if (e.Kind == TelemetryKind.World && e.Value == (int)WorldSignalKind.OvertimeStarted) overtime=true;
                byte weaponId = e.Weapon;
                if (e.Kind == TelemetryKind.World && e.Value == (int)WorldSignalKind.PickupConsumed)
                {
                    if (TelemetryCommand.PickupWeapon(e.Weapon) is not byte picked) continue;
                    weaponId = picked;
                }
                var weapon = total.Weapons.GetValueOrDefault(weaponId);
                if (e.Kind == TelemetryKind.Kill) weapon.Kills++;
                if (e.Kind == TelemetryKind.Damage) weapon.Damage += e.Value;
                if (e.Kind == TelemetryKind.World && e.Value == (int)WorldSignalKind.PickupConsumed) weapon.Pickups++;
                if (weapon != default) total.Weapons[weaponId] = weapon;
            }
            if (overtime) total.Overtime++;
            foreach (var entry in spawns)
            {
                total.Spawns++; uint remaining = unchecked(match.EndTick-entry.Value.Tick);
                uint? died = deaths.TryGetValue(entry.Key,out uint death) ? unchecked(death-entry.Value.Tick) : null;
                void Count(uint window,ref int eligible,ref int fatal)
                { if (died <= window) { eligible++; fatal++; } else if (remaining >= window) eligible++; }
                Count(180,ref total.Eligible3,ref total.Deaths3); Count(300,ref total.Eligible5,ref total.Deaths5); Count(600,ref total.Eligible10,ref total.Deaths10);
            }
        }
        return groups.OrderBy(p=>p.Key).Select(p=>
        {
            var t=p.Value; long damage=t.Weapons.Values.Sum(v=>v.Damage);
            return new TelemetrySummary(p.Key,t.Matches,t.Overtime,(double)t.Overtime/t.Matches,
                t.Weapons.OrderBy(w=>w.Key).Select(w=>new WeaponSummary(w.Key,w.Value.Pickups,w.Value.Kills,w.Value.Damage,
                    t.Minutes==0?null:w.Value.Pickups/t.Minutes,damage==0?null:(double)w.Value.Damage/damage)).ToArray(),
                t.Spawns,t.Eligible3,t.Deaths3,t.Eligible5,t.Deaths5,t.Eligible10,t.Deaths10,minimum,t.Matches >= minimum);
        }).ToArray();
    }
}
