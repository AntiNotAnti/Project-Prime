using System.Globalization;
using System.Text.Json;

namespace MphRead;

/// <summary>
/// Compares normalized oracle artifacts by earliest checkpoint. Timing axes and
/// semantic state remain separate, and missing or incompatible input is always
/// a failed comparison rather than an implicit pass.
/// </summary>
public static class OracleComparer
{
    public static OracleComparison Compare(OracleArtifact expected, OracleArtifact actual,
        OracleNormalizer? normalizer = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        normalizer ??= OracleJson.DefaultNormalizer(expected.Manifest.Source.ReferenceRevision);

        var diagnostics = new List<string>();
        if (expected.Manifest.SchemaVersion != OracleJson.SchemaVersion
            || actual.Manifest.SchemaVersion != OracleJson.SchemaVersion
            || expected.SchemaVersion != OracleJson.SchemaVersion
            || actual.SchemaVersion != OracleJson.SchemaVersion)
            diagnostics.Add("Artifact schema is unsupported.");
        if (!StringComparer.Ordinal.Equals(expected.Manifest.ScenarioId, actual.Manifest.ScenarioId))
            diagnostics.Add("Artifact scenario identities differ.");
        if (!StringComparer.Ordinal.Equals(expected.Manifest.Source.ReferenceRevision,
            actual.Manifest.Source.ReferenceRevision)
            || !StringComparer.Ordinal.Equals(expected.Manifest.Source.ReferenceRevision,
                normalizer.Revision))
            diagnostics.Add("Artifact and normalizer reference revisions are incompatible.");
        if (!StringComparer.Ordinal.Equals(expected.Manifest.Source.ProjectRevision,
            actual.Manifest.Source.ProjectRevision))
            diagnostics.Add("Artifact Project Prime source revisions differ.");
        if (!StringComparer.OrdinalIgnoreCase.Equals(expected.Manifest.Source.ReferenceAggregateSha256,
            actual.Manifest.Source.ReferenceAggregateSha256))
            diagnostics.Add("Artifact reference aggregate identities differ.");
        if (!StringComparer.Ordinal.Equals(expected.Manifest.RomIdentity,
            actual.Manifest.RomIdentity))
            diagnostics.Add("Artifact complete-ROM identities differ.");
        if (!StringComparer.Ordinal.Equals(expected.Manifest.Result, "normalized")
            || !StringComparer.Ordinal.Equals(actual.Manifest.Result, "normalized"))
            diagnostics.Add("Only normalized oracle artifacts can be compared.");
        if (!StringComparer.Ordinal.Equals(expected.Manifest.ScenarioId, actual.Manifest.ScenarioId)
            || expected.Manifest.Seed != actual.Manifest.Seed)
            diagnostics.Add("Artifact scenario seed or identity differs.");
        if (normalizer.SchemaVersion != OracleJson.SchemaVersion)
            diagnostics.Add("Normalizer schema is unsupported.");
        if (diagnostics.Count > 0)
            return Incompatible(expected.Manifest.ScenarioId, diagnostics);

        OracleDivergence? divergence = null;
        var previousActualValues = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        int comparedCheckpoints = Math.Min(expected.Checkpoints.Count, actual.Checkpoints.Count);
        for (int i = 0; i < comparedCheckpoints && divergence == null; i++)
        {
            OracleCheckpoint left = expected.Checkpoints[i];
            OracleCheckpoint right = actual.Checkpoints[i];
            if (left.VBlank != right.VBlank)
            {
                divergence = Divergence(i, left.VBlank, "timing.vblank", left.VBlank.ToString(CultureInfo.InvariantCulture),
                    right.VBlank.ToString(CultureInfo.InvariantCulture), "VBlank checkpoint differs.");
                break;
            }
            if (left.SemanticEventIndex != right.SemanticEventIndex)
            {
                divergence = Divergence(i, left.VBlank, "timing.semanticEventIndex",
                    left.SemanticEventIndex.ToString(CultureInfo.InvariantCulture),
                    right.SemanticEventIndex.ToString(CultureInfo.InvariantCulture),
                    "Semantic event checkpoint differs.");
                break;
            }
            OracleFieldRule? timingRule = RuleFor(normalizer, "timing.elapsedMilliseconds");
            double elapsedTolerance = timingRule?.AbsoluteTolerance ?? 0;
            if (timingRule?.Mode == OracleComparisonMode.Tolerant
                ? Math.Abs(left.ElapsedMilliseconds - right.ElapsedMilliseconds) > elapsedTolerance
                : left.ElapsedMilliseconds != right.ElapsedMilliseconds)
            {
                divergence = Divergence(i, left.VBlank, "timing.elapsedMilliseconds",
                    NumberText(left.ElapsedMilliseconds), NumberText(right.ElapsedMilliseconds),
                    "Elapsed game time differs.");
                break;
            }

            var leftKeys = left.State.Keys.ToHashSet(StringComparer.Ordinal);
            var rightKeys = right.State.Keys.ToHashSet(StringComparer.Ordinal);
            foreach (string key in leftKeys.Union(rightKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (!left.State.TryGetValue(key, out JsonElement leftValue))
                {
                    divergence = Divergence(i, left.VBlank, key, "<missing>", right.State[key].GetRawText(),
                        "Expected semantic state field is missing.");
                    break;
                }
                if (!right.State.TryGetValue(key, out JsonElement rightValue))
                {
                    divergence = Divergence(i, left.VBlank, key, leftValue.GetRawText(), "<missing>",
                        "Actual semantic state field is missing.");
                    break;
                }
                divergence = CompareValue(leftValue, rightValue, key, i, left.VBlank, normalizer,
                    previousActualValues);
                if (divergence != null) break;
            }
        }
        if (divergence == null && expected.Checkpoints.Count != actual.Checkpoints.Count)
        {
            int index = comparedCheckpoints;
            int vblank = index < expected.Checkpoints.Count
                ? expected.Checkpoints[index].VBlank : actual.Checkpoints[index].VBlank;
            divergence = Divergence(index, vblank, "checkpoints", expected.Checkpoints.Count.ToString(CultureInfo.InvariantCulture),
                actual.Checkpoints.Count.ToString(CultureInfo.InvariantCulture), "Checkpoint count differs.");
        }

        int comparedEvents = Math.Min(expected.Events.Count, actual.Events.Count);
        for (int i = 0; i < comparedEvents && divergence == null; i++)
        {
            OracleEvent left = expected.Events[i];
            OracleEvent right = actual.Events[i];
            if (left.Index != right.Index || !StringComparer.Ordinal.Equals(left.Name, right.Name)
                || left.VBlank != right.VBlank || left.ElapsedMilliseconds != right.ElapsedMilliseconds)
            {
                divergence = Divergence(-1, left.VBlank, $"events/{i}",
                    EventText(left), EventText(right), "Semantic event sequence differs.");
            }
        }
        if (divergence == null && expected.Events.Count != actual.Events.Count)
        {
            divergence = Divergence(-1, 0, "events", expected.Events.Count.ToString(CultureInfo.InvariantCulture),
                actual.Events.Count.ToString(CultureInfo.InvariantCulture), "Semantic event count differs.");
        }
        return new OracleComparison(OracleJson.SchemaVersion, expected.Manifest.ScenarioId,
            true, divergence == null, comparedCheckpoints, comparedEvents, divergence, diagnostics);
    }

    private static OracleDivergence? CompareValue(JsonElement expected, JsonElement actual,
        string path, int checkpointIndex, int vblank, OracleNormalizer normalizer,
        Dictionary<string, JsonElement> previousActualValues)
    {
        OracleFieldRule? rule = RuleFor(normalizer, path);
        if (expected.ValueKind != actual.ValueKind)
            return Divergence(checkpointIndex, vblank, path, expected.GetRawText(), actual.GetRawText(),
                "Normalized value kinds differ.");

        if (rule?.Mode == OracleComparisonMode.Invariant)
        {
            string invariant = rule.Invariant!;
            if (invariant is "finite" && actual.ValueKind == JsonValueKind.Number
                && actual.TryGetDouble(out double finite) && Double.IsFinite(finite))
            {
                previousActualValues[path] = actual.Clone();
                return null;
            }
            if (invariant is "nonNegative" && actual.ValueKind == JsonValueKind.Number
                && actual.TryGetDouble(out double nonNegative) && Double.IsFinite(nonNegative)
                && nonNegative >= 0)
            {
                previousActualValues[path] = actual.Clone();
                return null;
            }
            if (invariant is "nonEmpty" && actual.ValueKind == JsonValueKind.String
                && !String.IsNullOrWhiteSpace(actual.GetString()))
            {
                previousActualValues[path] = actual.Clone();
                return null;
            }
            if (invariant is "nonDecreasing"
                && previousActualValues.TryGetValue(path, out JsonElement previousActual)
                && previousActual.ValueKind == JsonValueKind.Number
                && actual.ValueKind == JsonValueKind.Number
                && previousActual.TryGetDouble(out double previous)
                && actual.TryGetDouble(out double current) && current >= previous)
            {
                previousActualValues[path] = actual.Clone();
                return null;
            }
            if (invariant is "nonDecreasing" && !previousActualValues.ContainsKey(path)
                && actual.ValueKind == JsonValueKind.Number)
            {
                previousActualValues[path] = actual.Clone();
                return null;
            }
            return Divergence(checkpointIndex, vblank, path, expected.GetRawText(), actual.GetRawText(),
                $"Invariant '{invariant}' is not satisfied.");
        }

        if (expected.ValueKind == JsonValueKind.Object)
        {
            var expectedProperties = expected.EnumerateObject().ToDictionary(p => p.Name, p => p.Value,
                StringComparer.Ordinal);
            var actualProperties = actual.EnumerateObject().ToDictionary(p => p.Name, p => p.Value,
                StringComparer.Ordinal);
            foreach (string name in expectedProperties.Keys.Union(actualProperties.Keys, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal))
            {
                if (!expectedProperties.TryGetValue(name, out JsonElement left))
                    return Divergence(checkpointIndex, vblank, path + "." + name, "<missing>",
                        actualProperties[name].GetRawText(), "Expected normalized value is missing.");
                if (!actualProperties.TryGetValue(name, out JsonElement right))
                    return Divergence(checkpointIndex, vblank, path + "." + name, left.GetRawText(),
                        "<missing>", "Actual normalized value is missing.");
                OracleDivergence? difference = CompareValue(left, right, path + "." + name,
                    checkpointIndex, vblank, normalizer, previousActualValues);
                if (difference != null) return difference;
            }
            return null;
        }
        if (expected.ValueKind == JsonValueKind.Array)
        {
            if (expected.GetArrayLength() != actual.GetArrayLength())
                return Divergence(checkpointIndex, vblank, path, expected.GetRawText(), actual.GetRawText(),
                    "Normalized array lengths differ.");
            int index = 0;
            foreach (JsonElement left in expected.EnumerateArray())
            {
                JsonElement right = actual[index];
                OracleDivergence? difference = CompareValue(left, right, $"{path}/{index}",
                    checkpointIndex, vblank, normalizer, previousActualValues);
                if (difference != null) return difference;
                index++;
            }
            return null;
        }
        if (expected.ValueKind == JsonValueKind.Number && actual.ValueKind == JsonValueKind.Number
            && expected.TryGetDouble(out double leftNumber) && actual.TryGetDouble(out double rightNumber))
        {
            if (rule?.Mode == OracleComparisonMode.Tolerant
                && rule.AbsoluteTolerance is double tolerance
                && Math.Abs(leftNumber - rightNumber) <= tolerance)
            {
                previousActualValues[path] = actual.Clone();
                return null;
            }
            if (rule?.Mode == OracleComparisonMode.Tolerant)
                return Divergence(checkpointIndex, vblank, path, NumberText(leftNumber), NumberText(rightNumber),
                    $"Numeric tolerance {rule.AbsoluteTolerance!.Value.ToString("G9", CultureInfo.InvariantCulture)} exceeded.");
            if (leftNumber == rightNumber)
            {
                previousActualValues[path] = actual.Clone();
                return null;
            }
        }
        else if (StringComparer.Ordinal.Equals(expected.GetRawText(), actual.GetRawText()))
        {
            previousActualValues[path] = actual.Clone();
            return null;
        }
        return Divergence(checkpointIndex, vblank, path, expected.GetRawText(), actual.GetRawText(),
            "Normalized value differs.");
    }

    private static OracleFieldRule? RuleFor(OracleNormalizer normalizer, string path)
    {
        OracleFieldRule? exact = normalizer.Fields.FirstOrDefault(rule =>
            StringComparer.Ordinal.Equals(rule.Path, path));
        if (exact != null) return exact;
        int separator = path.LastIndexOf('.');
        while (separator > 0)
        {
            string prefix = path[..separator];
            OracleFieldRule? parent = normalizer.Fields.FirstOrDefault(rule =>
                StringComparer.Ordinal.Equals(rule.Path, prefix));
            if (parent != null) return parent;
            separator = prefix.LastIndexOf('.');
        }
        return normalizer.Fields.FirstOrDefault(rule => rule.Path == "*");
    }

    private static OracleComparison Incompatible(string scenarioId, IReadOnlyList<string> diagnostics)
        => new(OracleJson.SchemaVersion, scenarioId, false, false,
            0, 0, null, diagnostics);

    private static OracleDivergence Divergence(int checkpointIndex, int vblank, string path,
        string expected, string actual, string reason)
        => new(checkpointIndex, vblank, path, expected, actual, reason);

    private static string NumberText(double value)
        => value.ToString("R", CultureInfo.InvariantCulture);

    private static string EventText(OracleEvent value)
        => $"{value.Index}:{value.Name}@{value.VBlank}/{NumberText(value.ElapsedMilliseconds)}";
}
