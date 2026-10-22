using CorpsePilot;
if (!args.Contains("--enable-experimental-physics", StringComparer.Ordinal))
{
    Console.Error.WriteLine("This non-shipping pilot requires --enable-experimental-physics.");
    return 2;
}
string output = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "corpse-pilot-results";
int checks = 0;
PilotContractChecks.Run((pass, message) => { checks++; if (!pass) throw new InvalidOperationException(message); });
AffineBindingChecks.Run((pass, message) => { checks++; if (!pass) throw new InvalidOperationException(message); });
HumanoidMapChecks.Run((pass, message) => { checks++; if (!pass) throw new InvalidOperationException(message); });
StaticMeshChecks.Run((pass, message) => { checks++; if (!pass) throw new InvalidOperationException(message); });
HumanoidFitChecks.Run((pass, message) => { checks++; if (!pass) throw new InvalidOperationException(message); });
Console.WriteLine($"PASS {checks} standalone contracts");
PilotBenchmarks.Run(output);
Console.WriteLine($"PASS benchmarks: {output}");
return 0;
