using MphRead.Mods.Fidelity;
using System.Globalization;

if (args.Length != 0) return FidelityOracleCommand.Run(new[] { "-fidelityoracle" }.Concat(args).ToArray());
int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
try { FidelityOracle.Run("movement.walk", "test"); throw new Exception("F2 ran without explicit opt-in"); }
catch (InvalidOperationException) { Check(true, "content scenarios require explicit opt-in before loading assets"); }
var reference = FidelityOracle.Run("simulation.fixed-step", "test", 60);
foreach (int hz in new[] { 30, 60, 120, 144, 240, 500, 997 })
    Check(FidelityOracle.Compare(reference, FidelityOracle.Run("simulation.fixed-step", "different-revision", hz)) == null, $"clock/RNG normalized values at {hz} presentation Hz");
var actual = FidelityOracle.Run("simulation.rng", "test");
var expected = FidelityOracle.Run("simulation.rng", "test");
actual.Checkpoints[186].Values["rng1"]++;
var difference = FidelityOracle.Compare(expected, actual);
Check(difference?.Tick == 187 && difference.Field == "rng1", "first divergence identifies exact tick and field");
actual = FidelityOracle.Run("simulation.rng", "test"); actual.Checkpoints.RemoveAt(10);
Check(FidelityOracle.Compare(expected, actual)?.Tick == 12, "missing checkpoint fails verification");
Check(FidelityOracle.Compare(expected, expected with { Scenario = expected.Scenario with { Version = 2 } })?.Field == "scenario contract", "scenario changes cannot silently reuse baselines");
Check(FidelityOracle.Normalize(0.5 / 4096) == 1 && FidelityOracle.Normalize(-0.5 / 4096) == -1, "explicit midpoint normalization");
try { FidelityOracle.Normalize(double.NaN); throw new Exception("NaN accepted"); } catch (InvalidDataException) { checks++; }
var priorCulture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
    Check(FidelityOracle.Run("simulation.rng", "test").CanonicalHash == expected.CanonicalHash, "canonical hash is culture independent");
}
finally { CultureInfo.CurrentCulture = priorCulture; }
foreach (var scenario in FidelityScenarios.All)
    Check(FidelityOracle.Run(scenario.Id, "first").CanonicalHash == FidelityOracle.Run(scenario.Id, "second").CanonicalHash, "repeatability " + scenario.Id);
string? ci = Environment.GetEnvironmentVariable("CI");
try
{
    Environment.SetEnvironmentVariable("CI", "true");
    Check(FidelityOracleCommand.Run(new[] { "-fidelityoracle", "record", "all", "--developer-record", "-engine-revision", "test" }) != 0, "CI cannot record baselines");
}
finally { Environment.SetEnvironmentVariable("CI", ci); }
Console.WriteLine($"{checks} fidelity framework checks passed (F0/F1 only).");
return 0;
