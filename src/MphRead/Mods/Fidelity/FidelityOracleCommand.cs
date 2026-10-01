using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.Fidelity;

internal static class FidelityOracleCommand
{
    internal static int Run(string[] args)
    {
        try
        {
            int index = Array.IndexOf(args, "-fidelityoracle");
            string command = args.ElementAtOrDefault(index + 1) ?? "list";
            string? Option(string name) { int at = Array.IndexOf(args, name); return at < 0 ? null : args.ElementAtOrDefault(at + 1); }
            if (command == "list")
            {
                Console.WriteLine(JsonSerializer.Serialize(FidelityScenarios.All.Concat(FidelityContentScenarios.All), FidelityOracle.Json)); return 0;
            }
            string id = args.ElementAtOrDefault(index + 2) ?? throw new ArgumentException("Choose a scenario or all.");
            bool allowContent = args.Contains("-allow-content");
            var catalog = FidelityScenarios.All.Concat(FidelityContentScenarios.All);
            var scenarios = id == "all" ? catalog.Where(s => allowContent || s.Content == "none").ToArray() : catalog.Where(s => s.Id == id).ToArray();
            if (scenarios.Length == 0) throw new ArgumentException("Unknown scenario.");
            string directory = Path.GetFullPath(Option("-baselines") ?? Path.Combine(AppContext.BaseDirectory, "fidelity-baselines"), ConsoleSetup.LaunchDirectory);
            string revision = Option("-engine-revision") ?? "working-tree";
            int hz = int.Parse(Option("-presentation-hz") ?? "60", System.Globalization.CultureInfo.InvariantCulture);
            if (command == "record" && (!args.Contains("--developer-record") || revision == "working-tree"
                || Environment.GetEnvironmentVariable("CI") is { Length: > 0 }))
                throw new InvalidOperationException("Baseline recording is developer-only: pass --developer-record and -engine-revision REV outside CI. Review the resulting diff.");
            if (command is not ("run" or "verify" or "record")) throw new ArgumentException("Use list, run, verify or record.");
            int failures = 0;
            foreach (var scenario in scenarios)
            {
                // Asset loading has useful diagnostics, but stdout belongs to machine-readable results.
                var stdout = Console.Out;
                FidelityResult result;
                try
                {
                    Console.SetOut(Console.Error);
                    result = FidelityOracle.Run(scenario.Id, revision, hz, allowContent);
                }
                finally { Console.SetOut(stdout); }
                string baseline = Path.Combine(directory, scenario.Id + ".json");
                if (command == "record")
                {
                    Directory.CreateDirectory(directory);
                    string temporary = baseline + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { File.WriteAllText(temporary, JsonSerializer.Serialize(result, FidelityOracle.Json)); File.Move(temporary, baseline, true); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    Console.WriteLine("RECORDED (review required) " + baseline);
                }
                else if (command == "run") Console.WriteLine(JsonSerializer.Serialize(result, FidelityOracle.Json));
                else
                {
                    var difference = FidelityOracle.Compare(FidelityOracle.Read(baseline), result);
                    if (difference == null) Console.WriteLine($"PASS {scenario.Id} {scenario.Tier} {result.CanonicalHash}");
                    else
                    {
                        failures++;
                        Console.Error.WriteLine($"First divergence: {scenario.Id}, tick {difference.Tick}, {difference.Field}: expected {difference.Expected ?? "<missing>"}, actual {difference.Actual ?? "<missing>"}");
                        Console.WriteLine(JsonSerializer.Serialize(difference, FidelityOracle.Json));
                    }
                }
            }
            return failures == 0 ? 0 : 1;
        }
        catch (Exception ex) { Console.Error.WriteLine("Fidelity oracle: " + ex.Message); return 1; }
    }
}
