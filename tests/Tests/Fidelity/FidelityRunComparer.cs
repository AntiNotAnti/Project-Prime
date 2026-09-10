using System;
using System.Collections.Generic;
using System.Globalization;

namespace MphRead.Tests.Fidelity;

internal enum FidelityComparisonMode { Exact, Tolerant, InvariantNondecreasing }

internal sealed record FidelityFieldRule(string Field, FidelityComparisonMode Mode,
    double Tolerance = 0, string Unit = "", string Reason = "");

internal sealed record FidelityDivergence(string CaseId, uint Tick, string Field,
    string Expected, string Actual, IReadOnlyList<string> PrecedingValues,
    double? Tolerance, string Unit, string Reason, string SourceCommit,
    string ReferenceDigest, uint Seed1, uint Seed2, string ReproductionCommand);

internal static class FidelityRunComparer
{
    public static FidelityDivergence? FirstDivergence(FidelityRunResult expected,
        FidelityRunResult actual, IReadOnlyList<FidelityFieldRule> rules)
    {
        if (expected.CaseId != actual.CaseId) throw new ArgumentException("Fidelity case IDs differ.");
        if (rules.Count is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(rules));
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (FidelityFieldRule rule in rules)
        {
            if (String.IsNullOrWhiteSpace(rule.Field) || !fields.Add(rule.Field))
                throw new ArgumentException("Fidelity comparison fields must be non-empty and unique.");
        }
        int count = Math.Min(expected.Observations.Count, actual.Observations.Count);
        FidelityDivergence? first = null;
        foreach (FidelityFieldRule rule in rules)
        {
            if (rule.Mode == FidelityComparisonMode.Tolerant
                && (!Double.IsFinite(rule.Tolerance) || rule.Tolerance < 0
                    || String.IsNullOrWhiteSpace(rule.Unit) || String.IsNullOrWhiteSpace(rule.Reason)))
                throw new ArgumentException("Tolerant fields require a finite tolerance, unit, and reason.");
            if (rule.Mode == FidelityComparisonMode.InvariantNondecreasing)
            {
                double? previous = null;
                for (int i = 0; i < actual.Observations.Count; i++)
                {
                    double value = Numeric(actual.Observations[i], rule.Field);
                    if (previous.HasValue && value < previous.Value)
                        first = Earlier(first, Report(actual, actual.Observations[i].Tick, rule,
                            $">={previous.Value:R}", value.ToString("R", CultureInfo.InvariantCulture), i));
                    previous = value;
                    if (first?.Field == rule.Field) break;
                }
                continue;
            }
            for (int i = 0; i < count; i++)
            {
                FidelityObservation left = expected.Observations[i];
                FidelityObservation right = actual.Observations[i];
                if (left.Tick != right.Tick)
                {
                    first = Earlier(first, Report(actual, right.Tick, rule,
                        left.Tick.ToString(), right.Tick.ToString(), i));
                    break;
                }
                if (rule.Mode == FidelityComparisonMode.Exact)
                {
                    string expectedValue = Value(left, rule.Field);
                    string actualValue = Value(right, rule.Field);
                    if (!StringComparer.Ordinal.Equals(expectedValue, actualValue))
                    {
                        first = Earlier(first, Report(actual, right.Tick, rule, expectedValue, actualValue, i));
                        break;
                    }
                }
                else
                {
                    double expectedValue = Numeric(left, rule.Field);
                    double actualValue = Numeric(right, rule.Field);
                    if (Math.Abs(expectedValue - actualValue) > rule.Tolerance)
                    {
                        first = Earlier(first, Report(actual, right.Tick, rule,
                            expectedValue.ToString("R", CultureInfo.InvariantCulture),
                            actualValue.ToString("R", CultureInfo.InvariantCulture), i));
                        break;
                    }
                }
            }
        }
        if (expected.Observations.Count != actual.Observations.Count)
        {
            uint tick = actual.Observations.Count == 0 ? 0 : actual.Observations[^1].Tick;
            first = Earlier(first, Report(actual, tick, rules[0], expected.Observations.Count.ToString(),
                actual.Observations.Count.ToString(), Math.Max(0, actual.Observations.Count - 1)));
        }
        return first;
    }

    private static FidelityDivergence Earlier(FidelityDivergence? current, FidelityDivergence candidate)
        => current == null || candidate.Tick < current.Tick ? candidate : current;

    private static string Value(FidelityObservation observation, string field)
    {
        if (observation.Exact.TryGetValue(field, out string? exact)) return exact;
        if (observation.Numeric.TryGetValue(field, out double numeric))
            return BitConverter.DoubleToInt64Bits(numeric).ToString("x16", CultureInfo.InvariantCulture);
        throw new ArgumentException($"Unknown fidelity observation field: {field}");
    }

    private static double Numeric(FidelityObservation observation, string field)
        => observation.Numeric.TryGetValue(field, out double value)
            ? value : throw new ArgumentException($"Fidelity field is not numeric: {field}");

    private static FidelityDivergence Report(FidelityRunResult run, uint tick,
        FidelityFieldRule rule, string expected, string actual, int index)
    {
        var preceding = new List<string>();
        for (int i = Math.Max(0, index - 3); i < index; i++)
        {
            FidelityObservation observation = run.Observations[i];
            preceding.Add($"tick {observation.Tick}: {rule.Field}={Value(observation, rule.Field)}");
        }
        return new FidelityDivergence(run.CaseId, tick, rule.Field, expected, actual,
            preceding.AsReadOnly(), rule.Mode == FidelityComparisonMode.Tolerant ? rule.Tolerance : null,
            rule.Unit, rule.Reason, run.SourceCommit, run.ReferenceDigest,
            run.Seed1, run.Seed2, run.ReproductionCommand);
    }
}
