using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MphRead;

public sealed record FidelityProvenance(string SourcePath, long Offset, long Length,
    string Sha256, string Method);
public sealed record FidelityExtractRecord(string Key, string Value, string ContentType,
    FidelityProvenance Provenance);
public sealed record FidelityExtractReport(int SchemaVersion, string Domain, string ContentVersion,
    string ReferenceDigest, IReadOnlyList<FidelityExtractRecord> Records);

public static partial class FidelityReferenceExtractor
{
    public const int SchemaVersion = 1;
    public static readonly IReadOnlyList<string> Domains =
        ["binary-layout", "multiplayer-archives", "audio-sequences"];

    [GeneratedRegex("^_bin/overlay9_[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex OverlayPath();
    [GeneratedRegex("^archives/(commonMP|ctf1|mp(?:[1-9]|1[0-4]))\\.arc$",
        RegexOptions.CultureInvariant)]
    private static partial Regex MultiplayerArchivePath();

    public static FidelityExtractReport Extract(string referenceDirectory, string domain,
        FidelityReferenceIdentity? identity = null, FidelityManifestLimits? limits = null)
    {
        if (!Domains.Contains(domain, StringComparer.Ordinal))
            throw new NotSupportedException($"Unsupported fidelity extraction domain: {domain}");
        FidelityReferenceManifest manifest = FidelityManifestBuilder.Build(referenceDirectory, identity, limits);
        IEnumerable<FidelityManifestFile> selected = domain switch
        {
            "binary-layout" => manifest.Files.Where(file => file.Path == "_bin/arm9.bin"
                || OverlayPath().IsMatch(file.Path)),
            "multiplayer-archives" => manifest.Files.Where(file => MultiplayerArchivePath().IsMatch(file.Path)),
            "audio-sequences" => manifest.Files.Where(file => file.Path.StartsWith("_seq/", StringComparison.Ordinal)
                && (file.Path.EndsWith(".minincsf", StringComparison.OrdinalIgnoreCase)
                    || file.Path.EndsWith(".ncsflib", StringComparison.OrdinalIgnoreCase))),
            _ => throw new InvalidOperationException("Unreachable fidelity extraction domain.")
        };
        FidelityExtractRecord[] records = selected.OrderBy(file => file.Path, StringComparer.Ordinal)
            .Select(file => new FidelityExtractRecord(file.Path, DisplayValue(domain, file.Path), file.ContentType,
                new FidelityProvenance(file.Path, 0, file.Size, file.Sha256, "sha256-manifest-v1")))
            .ToArray();
        if (records.Length == 0)
            throw new InvalidDataException($"The validated reference contains no supported {domain} inputs.");
        return new FidelityExtractReport(SchemaVersion, domain, manifest.ContentVersion,
            manifest.AggregateSha256, records);
    }

    private static string DisplayValue(string domain, string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (domain == "audio-sequences")
        {
            int separator = name.IndexOf(" - ", StringComparison.Ordinal);
            if (separator >= 0 && separator + 3 < name.Length)
                return name[(separator + 3)..];
        }
        return name;
    }
}

public sealed record FidelityTrialStep(string Id, int Tick, string ParticipantId, string Type,
    IReadOnlyDictionary<string, double> Values);
public sealed record FidelityTrialWorksheet(int SchemaVersion, string CaseId, string Title,
    string ReferenceRevision, string ReferenceContentDigest, uint Seed, int MaximumTicks,
    IReadOnlyList<string> Preconditions, IReadOnlyList<FidelityTrialStep> Steps,
    IReadOnlyList<FidelityExpectedObservation> ExpectedObservations,
    IReadOnlyList<FidelityTolerance> Tolerances, string Result, string Notes);

public static class FidelityTrialWorksheetBuilder
{
    public const int SchemaVersion = 1;
    public static FidelityTrialWorksheet Build(FidelityCaseDocument value) => new(SchemaVersion,
        value.Id, value.Title, value.ReferenceRevision, value.ReferenceContentDigest,
        value.Scenario.Seed, value.Scenario.MaxTicks, value.Preconditions,
        value.Scenario.Actions.Select(action => new FidelityTrialStep(action.Id, action.Tick,
            action.ParticipantId, action.Type, action.Values ?? new Dictionary<string, double>())).ToArray(),
        value.ExpectedObservations, value.Tolerances, "not-run",
        "Record bounded reference or Project Prime observations without changing expected values.");
}

public sealed record FidelityJsonDifference(int SchemaVersion, string CaseId, bool Equal,
    string? Path, string? Expected, string? Actual);

public static class FidelityJsonComparer
{
    public const int SchemaVersion = 1;
    public const int MaximumDocumentBytes = 2 * 1024 * 1024;

    public static FidelityJsonDifference Compare(string caseId, ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> actual)
    {
        ValidateCaseId(caseId);
        using JsonDocument left = Parse(expected, "expected");
        using JsonDocument right = Parse(actual, "actual");
        (string path, string expectedValue, string actualValue)? difference
            = FirstDifference(left.RootElement, right.RootElement, "$", 0);
        return difference == null
            ? new FidelityJsonDifference(SchemaVersion, caseId, true, null, null, null)
            : new FidelityJsonDifference(SchemaVersion, caseId, false, difference.Value.path,
                difference.Value.expectedValue, difference.Value.actualValue);
    }

    private static JsonDocument Parse(ReadOnlySpan<byte> utf8, string side)
    {
        if (utf8.Length is < 2 or > MaximumDocumentBytes)
            throw new InvalidDataException($"The {side} normalized report exceeds its byte bound.");
        try
        {
            JsonDocument document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
            ValidateUniqueProperties(document.RootElement, "$", 0);
            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The {side} normalized report is malformed.", ex);
        }
    }

    private static void ValidateUniqueProperties(JsonElement element, string path, int depth)
    {
        if (depth > 64) throw new InvalidDataException("Normalized report nesting exceeds its bound.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Duplicate normalized property at {path}: {property.Name}");
                ValidateUniqueProperties(property.Value, path + "/" + Escape(property.Name), depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
                ValidateUniqueProperties(item, path + "/" + index++, depth + 1);
        }
    }

    private static (string, string, string)? FirstDifference(JsonElement left, JsonElement right,
        string path, int depth)
    {
        if (depth > 64) throw new InvalidDataException("Normalized report nesting exceeds its bound.");
        if (left.ValueKind != right.ValueKind)
            return (path, left.GetRawText(), right.GetRawText());
        if (left.ValueKind == JsonValueKind.Object)
        {
            var leftProperties = left.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
            var rightProperties = right.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
            foreach (string name in leftProperties.Keys.Union(rightProperties.Keys, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal))
            {
                if (!leftProperties.TryGetValue(name, out JsonElement leftValue))
                    return (path + "/" + Escape(name), "<missing>", rightProperties[name].GetRawText());
                if (!rightProperties.TryGetValue(name, out JsonElement rightValue))
                    return (path + "/" + Escape(name), leftValue.GetRawText(), "<missing>");
                var difference = FirstDifference(leftValue, rightValue, path + "/" + Escape(name), depth + 1);
                if (difference != null) return difference;
            }
            return null;
        }
        if (left.ValueKind == JsonValueKind.Array)
        {
            JsonElement.ArrayEnumerator leftItems = left.EnumerateArray();
            JsonElement.ArrayEnumerator rightItems = right.EnumerateArray();
            int index = 0;
            while (true)
            {
                bool haveLeft = leftItems.MoveNext(); bool haveRight = rightItems.MoveNext();
                if (!haveLeft || !haveRight)
                    return haveLeft == haveRight ? null : (path + "/" + index,
                        haveLeft ? leftItems.Current.GetRawText() : "<missing>",
                        haveRight ? rightItems.Current.GetRawText() : "<missing>");
                var difference = FirstDifference(leftItems.Current, rightItems.Current,
                    path + "/" + index, depth + 1);
                if (difference != null) return difference;
                index++;
            }
        }
        return StringComparer.Ordinal.Equals(left.GetRawText(), right.GetRawText())
            ? null : (path, left.GetRawText(), right.GetRawText());
    }

    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);
    private static void ValidateCaseId(string value)
    {
        if (String.IsNullOrWhiteSpace(value) || value.Length > 128
            || !Regex.IsMatch(value, "^FID-[A-Z]+-[0-9]{3}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Fidelity comparison case ID is invalid.");
    }
}

public sealed record FidelityCaseSummary(string Id, FidelityDomain Domain, FidelitySeverity Severity,
    FidelityStatus Status, FidelityEvidenceLevel EvidenceLevel);
public sealed record FidelityProgramReport(int SchemaVersion, string ReferenceRevision,
    string ReferenceContentDigest, IReadOnlyList<FidelityCaseSummary> Cases,
    IReadOnlyDictionary<string, int> StatusCounts, IReadOnlyDictionary<string, int> DomainCounts,
    IReadOnlyDictionary<string, int> SeverityCounts);

public static class FidelityProgramReportBuilder
{
    public const int SchemaVersion = 1;
    public const int MaximumCases = 1000;
    public static FidelityProgramReport Build(string fixtureDirectory, string referenceRevision,
        string referenceDigest)
    {
        string root = Path.GetFullPath(fixtureDirectory);
        var info = new DirectoryInfo(root);
        if (!info.Exists || info.LinkTarget != null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Fidelity fixture directory must be non-symbolic.");
        string[] files = Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal).ToArray();
        if (files.Length is < 1 or > MaximumCases)
            throw new InvalidDataException($"Fidelity report requires between 1 and {MaximumCases} cases.");
        var cases = new List<FidelityCaseSummary>(files.Length);
        foreach (string file in files)
        {
            var fileInfo = new FileInfo(file);
            if (fileInfo.LinkTarget != null || (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Symbolic fidelity case files are not allowed.");
            FidelityCaseDocument value = FidelityCaseParser.Load(root, Path.GetFileName(file),
                referenceRevision, referenceDigest);
            cases.Add(new(value.Id, value.Domain, value.Severity, value.Status, value.EvidenceLevel));
        }
        cases.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        return new FidelityProgramReport(SchemaVersion, referenceRevision, referenceDigest, cases,
            Counts(cases.Select(value => value.Status.ToString())),
            Counts(cases.Select(value => value.Domain.ToString())),
            Counts(cases.Select(value => value.Severity.ToString())));
    }

    private static IReadOnlyDictionary<string, int> Counts(IEnumerable<string> values)
        => new SortedDictionary<string, int>(values.GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal), StringComparer.Ordinal);
}

public static class FidelityJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options) + "\n";
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
