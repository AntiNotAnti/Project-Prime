using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MphRead.Mods.Physics
{
    public enum FpsCategory
    {
        TimerCounter, AnimationCadence, LinearIncrement, Velocity, Acceleration,
        PositionIntegration, Gravity, Damping, Interpolation, SpeedCap, Collision,
        Slope, Knockback, ProbabilityPerFrame, Camera, Effects, AI, Projectile, Unknown
    }

    public sealed record FpsAuditSite(string Id, string File, int Line, string Method,
        string Expression, string Category, string Risk, string? Native30Expression,
        string Current60Expression, string ExpectedEquivalent, string Notes, string Status)
    {
        private bool PlayerPhysics => File.Contains("/Players/") && !File.Contains("PlayerAi")
            && !File.Contains("Halfturret") && !File.Contains("PlayerCombat") && !File.Contains("PlayerHud") && !File.Contains("PlayerCamera");
        public string Cohort => Risk == "CRITICAL" && PlayerPhysics ? "01-critical-player"
            : Risk == "HIGH" && PlayerPhysics ? "02-high-player"
            : Risk == "HIGH" && (File.Contains("Combat") || File.Contains("GameState")) ? "03-high-combat"
            : (Risk is "CRITICAL" or "HIGH") && (File.Contains("Enemy") || File.Contains("PlayerAi")) ? "05-high-ai"
            : File.Contains("Camera") || File.Contains("Hud") || Category is "Camera" or "Effects" ? "06-presentation"
            : Risk is "CRITICAL" or "HIGH" ? "04-high-entity"
            : "07-review";
        public string? CandidateProof => Category switch
        {
            "TimerCounter" => "wall-clock-duration-and-edge-inclusivity",
            "AnimationCadence" => "every-second-tick-cadence-and-start-phase",
            "LinearIncrement" => "constant-linear-rate-half-step",
            _ => null
        };
    }

    /// <summary>Conservative lexical inventory, not a proof of native semantics.</summary>
    public static class FpsConversionAudit
    {
        private static readonly Regex Marker = new(@"\bFPS\s+stuff\??", RegexOptions.IgnoreCase);
        private static readonly Regex Suspicious = new(@"(?:[/\*]=\s*(?:2\b|0\.\d+)|/\s*2(?:f?\b)|\*\s*0\.\d+f?\b)");
        private static readonly Regex Method = new(@"^\s*(?:public|private|protected|internal)\s+(?:(?:static|override|virtual|sealed|async|unsafe|new)\s+)*[\w<>\[\]?,.]+\s+(\w+)\s*\(");
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public static string FindRoot()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                for (DirectoryInfo? directory = new(start); directory != null; directory = directory.Parent)
                    if (Directory.Exists(Path.Combine(directory.FullName, "src", "MphRead"))) return directory.FullName;
            throw new DirectoryNotFoundException("Source checkout not found. Supply -fpsroot with the repository path.");
        }

        public static IReadOnlyList<FpsAuditSite> Scan(string root)
        {
            string source = Path.Combine(root, "src", "MphRead");
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Expected repository root containing src/MphRead: " + root);
            var result = new List<FpsAuditSite>();
            foreach (string path in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (relative.Split('/').Any(p => p is "bin" or "obj") || relative.Contains("/Mods/Physics/")) continue;
                string[] lines = File.ReadAllLines(path);
                string method = "<unresolved>";
                bool block = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = StripCommentsAndStrings(lines[i], ref block);
                    Match match = Method.Match(code);
                    if (match.Success) method = match.Groups[1].Value;
                    bool marked = Marker.IsMatch(lines[i]);
                    bool gameplay = relative.Contains("/Entities/") || relative.Contains("/Camera") || relative.Contains("/Effects/");
                    if (!marked && !(gameplay && Suspicious.IsMatch(code))) continue;
                    string expression = lines[i].Trim();
                    string context = string.Join(" ", lines.Skip(Math.Max(0, i - 2)).Take(Math.Min(5, lines.Length - Math.Max(0, i - 2))));
                    FpsCategory category = Classify(code.Length == 0 ? context : code, relative);
                    string risk = category switch
                    {
                        FpsCategory.PositionIntegration or FpsCategory.Gravity or FpsCategory.Collision or FpsCategory.Slope => "CRITICAL",
                        FpsCategory.Damping or FpsCategory.Interpolation or FpsCategory.ProbabilityPerFrame => "HIGH",
                        _ => "REVIEW"
                    };
                    // Location-independent content fingerprint; occurrence disambiguates identical statements.
                    string seed = relative + "\n" + method + "\n" + expression;
                    string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant()[..16];
                    int occurrence = result.Count(s => s.Id.StartsWith(hash + "-", StringComparison.Ordinal));
                    string expected = category switch
                    {
                        FpsCategory.Damping => "For an isolated constant factor F: sqrt(F); verify impulses and integration order separately.",
                        FpsCategory.Interpolation => "For a fixed target and amount A: 1-sqrt(1-A); moving targets require trajectory evidence.",
                        FpsCategory.Gravity or FpsCategory.PositionIntegration => "Compare integrated position, velocity and collision at even 60 Hz boundaries.",
                        FpsCategory.TimerCounter => "Verify elapsed nativeFrames/30 seconds and first/last tick inclusivity.",
                        FpsCategory.SpeedCap => "Absolute caps are state; audit cap decay separately.",
                        _ => "Requires native source or recorded reference and contextual review."
                    };
                    result.Add(new(hash + "-" + occurrence, relative, i + 1, method, expression,
                        category.ToString(), risk, null, expression, expected,
                        (marked ? "Explicit FPS marker. " : "Unmarked lexical candidate; may be a false positive. ")
                        + "Category/risk are triage only; method is nearest lexical declaration. Native expression is not inferred.", "NeedsResearch"));
                }
            }
            return result;
        }

        internal static string StripCommentsAndStrings(string line, ref bool block)
        {
            var output = new StringBuilder();
            bool quoted = false;
            char quote = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                char next = i + 1 < line.Length ? line[i + 1] : '\0';
                if (block) { if (c == '*' && next == '/') { block = false; i++; } continue; }
                if (quoted) { if (c == '\\') i++; else if (c == quote) quoted = false; continue; }
                if (c == '/' && next == '/') break;
                if (c == '/' && next == '*') { block = true; i++; continue; }
                if (c is '"' or '\'') { quoted = true; quote = c; output.Append(' '); continue; }
                output.Append(c);
            }
            return output.ToString().Trim();
        }

        private static FpsCategory Classify(string code, string path)
        {
            string c = code.ToLowerInvariant();
            if (c.Contains("position") && (c.Contains("speed") || c.Contains("velocity"))) return FpsCategory.PositionIntegration;
            if (c.Contains("gravity") || c.Contains("grav")) return FpsCategory.Gravity;
            if (c.Contains("speedmul") || c.Contains("speedfactor") || c.Contains("friction") || c.Contains("damping")) return FpsCategory.Damping;
            if (Regex.IsMatch(c, @"\*=\s*0\.\d+")) return FpsCategory.Damping;
            if (c.Contains("collision") || c.Contains("pushout")) return FpsCategory.Collision;
            if (c.Contains("slope")) return FpsCategory.Slope;
            if (c.Contains("speedcap")) return FpsCategory.SpeedCap;
            if (c.Contains("random") || c.Contains("rng")) return FpsCategory.ProbabilityPerFrame;
            if (c.Contains("timer") || c.Contains("cooldown") || c.Contains("time") || c.Contains("chargelevel")) return FpsCategory.TimerCounter;
            if (c.Contains("lerp") || (c.Contains("+=") && c.Contains(" - "))) return FpsCategory.Interpolation;
            if (c.Contains("knockback")) return FpsCategory.Knockback;
            if (c.Contains("accel") || c.Contains("traction")) return FpsCategory.Acceleration;
            if (path.Contains("Camera") || c.Contains("viewtilt") || c.Contains("viewbob")) return FpsCategory.Camera;
            if (c.Contains("anim") || c.Contains("frame")) return FpsCategory.AnimationCadence;
            if (c.Contains("speed") || c.Contains("velocity")) return FpsCategory.Velocity;
            if (path.Contains("Enemy") || path.Contains("PlayerAi")) return FpsCategory.AI;
            if (path.Contains("Beam") || path.Contains("Bomb") || path.Contains("Projectile")) return FpsCategory.Projectile;
            if (c.Contains("alpha") || c.Contains("effect") || c.Contains("particle")) return FpsCategory.Effects;
            if (c.Contains("+=") || c.Contains("-=")) return FpsCategory.LinearIncrement;
            return FpsCategory.Unknown;
        }

        public static int Run(string root, string output)
        {
            var sites = Scan(Path.GetFullPath(root)).OrderBy(s => s.Cohort, StringComparer.Ordinal).ThenBy(s => s.File, StringComparer.Ordinal).ThenBy(s => s.Line).ToArray();
            if (sites.Length == 0) throw new InvalidOperationException("No FPS sites found; check source root.");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "fps-conversion-report.json"), JsonSerializer.Serialize(sites, Json));
            File.WriteAllText(Path.Combine(output, "fps-cohorts.json"), JsonSerializer.Serialize(
                sites.GroupBy(s => s.Cohort).Select(g => new { cohort = g.Key, count = g.Count(),
                    sites = g.Select(s => new { s.Id, s.File, s.Line, s.Risk, s.Category, s.CandidateProof, s.Status }) }), Json));
            string[] header = { "id", "file", "line", "method", "expression", "category", "risk", "native30Expression", "current60Expression", "expectedEquivalent", "notes", "status" };
            using (var csv = new StreamWriter(Path.Combine(output, "fps-conversion-report.csv")))
            {
                csv.WriteLine(string.Join(",", header));
                foreach (var s in sites) csv.WriteLine(string.Join(",", new[] { s.Id, s.File, s.Line.ToString(System.Globalization.CultureInfo.InvariantCulture), s.Method, s.Expression, s.Category, s.Risk, s.Native30Expression ?? "", s.Current60Expression, s.ExpectedEquivalent, s.Notes, s.Status }.Select(Csv)));
            }
            var md = new StringBuilder("# FPS conversion inventory\n\nLexical triage only. NeedsResearch is not a parity pass. Native expressions require evidence.\n\n");
            foreach (var group in sites.GroupBy(s => s.Risk).OrderBy(g => g.Key)) md.AppendLine($"- {group.Key}: {group.Count()}");
            md.AppendLine("\n| File:line | Method | Category | Risk | Expression | Status |\n|---|---|---|---|---|---|");
            foreach (var s in sites) md.AppendLine($"| {Escape(s.File)}:{s.Line} | {Escape(s.Method)} | {s.Category} | {s.Risk} | {Escape(s.Expression)} | {s.Status} |");
            File.WriteAllText(Path.Combine(output, "fps-conversion-report.md"), md.ToString());
            Console.WriteLine($"[fpsconvertaudit] {sites.Length} sites inventoried; native parity remains unverified. Reports: {Path.GetFullPath(output)}");
            return 0;
        }
        private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        private static string Escape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace("|", "&#124;").Replace("`", "&#96;");
    }
}
