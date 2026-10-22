using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MphRead.Mods.Diagnostics
{
    /// <summary>
    /// Development-time inventory for the inherited 30 Hz -> 60 Hz conversion sites.
    /// This command never rewrites source. It classifies marked sites so nonlinear
    /// movement/collision conversions can be reviewed before timer-only conversions.
    /// </summary>
    internal static class FpsConversionAudit
    {
        private static readonly Regex Marker = new(
            @"(?:sk)?todo:\s*FPS stuff", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> P0Files = new(StringComparer.OrdinalIgnoreCase)
        {
            "src/MphRead/Entities/Players/PlayerInput.cs",
            "src/MphRead/Entities/Players/PlayerCollision.cs",
            "src/MphRead/Entities/Players/PlayerProcess.cs",
            "src/MphRead/Entities/Players/PlayerCamera.cs",
            "src/MphRead/Entities/Players/PlayerEntity.cs"
        };

        private sealed record Site(string File, int Line, string Category, string Risk, string Priority, string Code);

        public static int Run(string? outputPath)
        {
            // Keep the math/comparison contract executable. The source inventory is
            // useful only if the transforms that motivate it still compose exactly.
            if (MovementShadowCheck.Run() != 0)
            {
                return 1;
            }

            string? root = FindRepositoryRoot();
            if (root == null)
            {
                Console.Error.WriteLine("[fpsconvertaudit] repository root not found; run from a Project Prime checkout");
                return 2;
            }

            string sourceRoot = Path.Combine(root, "src");
            var sites = new List<Site>();
            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                string relative = Normalize(Path.GetRelativePath(root, file));
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!Marker.IsMatch(lines[i]))
                    {
                        continue;
                    }

                    string context = string.Join(" ", lines.Skip(Math.Max(0, i - 1)).Take(Math.Min(3, lines.Length - Math.Max(0, i - 1))));
                    (string category, string risk) = Classify(relative, lines[i], context);
                    string priority = P0Files.Contains(relative) ? "P0" : PriorityFor(relative, category, risk);
                    sites.Add(new Site(relative, i + 1, category, risk, priority, lines[i].Trim()));
                }
            }

            sites.Sort((a, b) =>
            {
                int cmp = RiskRank(a.Risk).CompareTo(RiskRank(b.Risk));
                if (cmp != 0) return cmp;
                cmp = PriorityRank(a.Priority).CompareTo(PriorityRank(b.Priority));
                if (cmp != 0) return cmp;
                cmp = string.Compare(a.File, b.File, StringComparison.OrdinalIgnoreCase);
                return cmp != 0 ? cmp : a.Line.CompareTo(b.Line);
            });

            Console.WriteLine($"[fpsconvertaudit] root={root}");
            Console.WriteLine($"[fpsconvertaudit] total={sites.Count} p0={sites.Count(s => s.Priority == "P0")}");

            foreach (IGrouping<string, Site> group in sites.GroupBy(s => s.Risk).OrderBy(g => RiskRank(g.Key)))
            {
                Console.WriteLine($"[fpsconvertaudit] risk {group.Key}={group.Count()}");
            }
            foreach (IGrouping<string, Site> group in sites.GroupBy(s => s.Category).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
            {
                Console.WriteLine($"[fpsconvertaudit] category {group.Key}={group.Count()}");
            }

            PrintMathHazards();

            foreach (Site site in sites)
            {
                Console.WriteLine($"[fpsconvertaudit] {site.Risk,-6} {site.Priority,-2} {site.Category,-24} {site.File}:{site.Line} {site.Code}");
            }

            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                string fullPath = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
                File.WriteAllText(fullPath, BuildTsv(sites));
                Console.WriteLine($"[fpsconvertaudit] wrote {fullPath}");
            }
            return 0;
        }

        private static (string Category, string Risk) Classify(string file, string line, string context)
        {
            string text = (context + " " + line).ToLowerInvariant();
            string code = line.ToLowerInvariant();

            if (code.Contains("position") && code.Contains("speed") && code.Contains("/ 2"))
            {
                return ("position-integration", "HIGH");
            }
            if (code.Contains("_gravity") && code.Contains("/ 2"))
            {
                return ("gravity", "HIGH");
            }
            if (code.Contains("speed += (speedmul - speed) / 2")
                || code.Contains("*= 0.9f")
                || (code.Contains("+=") && code.Contains("-") && code.Contains("/ 2") && (code.Contains("factor") || code.Contains("diff"))))
            {
                return ("damping-interpolation", "HIGH");
            }
            if (file.EndsWith("/PlayerCollision.cs", StringComparison.OrdinalIgnoreCase)
                && !LooksLikeTimer(code))
            {
                return ("collision", "HIGH");
            }
            if (text.Contains("random") || text.Contains("probab"))
            {
                return ("random-per-frame", "MEDIUM");
            }
            if (file.EndsWith("/PlayerCamera.cs", StringComparison.OrdinalIgnoreCase))
            {
                return ("camera", code.Contains("camswitch") || LooksLikeTimer(code) ? "LOW" : "MEDIUM");
            }
            if (code.Contains("speed") || code.Contains("velocity") || code.Contains("accel")
                || code.Contains("traction") || code.Contains("knockback") || code.Contains("hcap"))
            {
                return ("velocity-acceleration", "MEDIUM");
            }
            if (code.Contains("anim") || code.Contains("framecount % 2") || code.Contains("updateanim"))
            {
                return ("animation-cadence", "LOW");
            }
            if (LooksLikeTimer(code))
            {
                return ("timer-counter", "LOW");
            }
            if (text.Contains("sfx") || text.Contains("alpha") || text.Contains("hud") || text.Contains("effect"))
            {
                return ("effects-presentation", "LOW");
            }
            if (file.Contains("/Enemies/", StringComparison.OrdinalIgnoreCase))
            {
                return ("ai", "MEDIUM");
            }
            if (code.Contains("/ 2") || code.Contains("* 2"))
            {
                return ("linear-manual", "REVIEW");
            }
            return ("unknown-manual", "REVIEW");
        }

        private static bool LooksLikeTimer(string code)
        {
            return code.Contains("timer") || code.Contains("cooldown") || code.Contains("time")
                || code.Contains("charge") || code.Contains("delay") || code.Contains("respawn")
                || code.Contains("invuln") || code.Contains("startup") || code.Contains("refill")
                || code.Contains("frames") || code.Contains("framecount");
        }

        private static string PriorityFor(string file, string category, string risk)
        {
            if (risk == "HIGH")
            {
                return "P1";
            }
            if (category == "timer-counter" && (file.Contains("Weapon", StringComparison.OrdinalIgnoreCase)
                || file.Contains("/Enemies/", StringComparison.OrdinalIgnoreCase)
                || file.Contains("Player", StringComparison.OrdinalIgnoreCase)))
            {
                return "P1";
            }
            return risk == "MEDIUM" ? "P2" : "P3";
        }

        private static int RiskRank(string risk) => risk switch
        {
            "HIGH" => 0,
            "MEDIUM" => 1,
            "REVIEW" => 2,
            _ => 3
        };

        private static int PriorityRank(string priority) => priority switch
        {
            "P0" => 0,
            "P1" => 1,
            "P2" => 2,
            _ => 3
        };

        private static void PrintMathHazards()
        {
            const float nativeLerp = 0.3f;
            float exactLerp = NativeStepMath.HalfStepLerp(nativeLerp);
            float naiveHalf = 1 - nativeLerp / 2;
            float naiveEffectiveLerp = 1 - naiveHalf * naiveHalf;
            const float altAirGravity = -245 / 4096f;
            var currentGravity = NativeStepMath.CurrentGravityPair(0, 0, altAirGravity);
            var nativeGravity = NativeStepMath.NativeSemiImplicit(0, 0, altAirGravity);
            float currentPairDisplacement = currentGravity.Position;
            float nativePairDisplacement = nativeGravity.Position;

            Console.WriteLine("[fpsconvertaudit] math exact-multiplier: F60=sqrt(F30)");
            Console.WriteLine("[fpsconvertaudit] math exact-lerp: A60=1-sqrt(1-A30)");
            Console.WriteLine(FormattableString.Invariant(
                $"[fpsconvertaudit] math facing A30=0.3 naivePair={naiveEffectiveLerp:0.000000} exactHalf={exactLerp:0.000000}"));
            Console.WriteLine(FormattableString.Invariant(
                $"[fpsconvertaudit] math alt-air gravity pair native={nativePairDisplacement:0.000000} current={currentPairDisplacement:0.000000} delta={currentPairDisplacement - nativePairDisplacement:0.000000}"));
        }

        private static string BuildTsv(IEnumerable<Site> sites)
        {
            var sb = new StringBuilder("risk\tpriority\tcategory\tfile\tline\tcode\n");
            foreach (Site site in sites)
            {
                string code = site.Code.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                sb.Append(site.Risk).Append('\t')
                    .Append(site.Priority).Append('\t')
                    .Append(site.Category).Append('\t')
                    .Append(site.File).Append('\t')
                    .Append(site.Line).Append('\t')
                    .Append(code).Append('\n');
            }
            return sb.ToString();
        }

        private static string? FindRepositoryRoot()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                DirectoryInfo? current = new DirectoryInfo(start);
                for (int i = 0; current != null && i < 12; i++, current = current.Parent)
                {
                    if (File.Exists(Path.Combine(current.FullName, "src", "MphRead", "MphRead.csproj")))
                    {
                        return current.FullName;
                    }
                }
            }
            return null;
        }

        private static string Normalize(string path) => path.Replace('\\', '/');
    }
}
