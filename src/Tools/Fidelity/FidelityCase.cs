using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MphRead;

public enum FidelityStatus { Candidate, Reproducing, ReferenceConfirmed, MismatchConfirmed, FixInProgress, Fixed, IntentionalDeviation, UnableToReproduce, BlockedByEvidence, Rejected }
public enum FidelityDomain { Simulation, Movement, Collision, Camera, Weapon, Projectile, Damage, Affliction, Hunter, AltForm, Pickup, MapEntity, Objective, GameMode, Spawn, Animation, Audio, UIFeedback, Networking, Replay, Bot }
public enum FidelitySeverity { P0, P1, P2, P3 }
public enum FidelityEvidenceLevel { E0, E1, E2, E3, E4 }

public sealed record FidelityScheduledAction(string Id, int Tick, string ParticipantId,
    string Type, IReadOnlyDictionary<string, double>? Values);
public sealed record FidelityExpectedObservation(string Id, int Tick, string EntityId,
    string Field, double? NumericValue, string? ExactValue);
public sealed record FidelityTolerance(string Field, double Absolute, string Unit, string Reason);
public sealed record FidelityScenario(string Map, string Mode, string Ruleset, int PlayerCount,
    uint Seed, int MaxTicks, IReadOnlyList<FidelityScheduledAction> Actions);
public sealed record FidelityCaseDocument(int SchemaVersion, string Id, string Title,
    FidelityDomain Domain, FidelitySeverity Severity, FidelityStatus Status,
    string ReferenceRevision, string ReferenceContentDigest, FidelityEvidenceLevel EvidenceLevel,
    IReadOnlyDictionary<string, string> Environment, IReadOnlyList<string> Preconditions,
    FidelityScenario Scenario, IReadOnlyList<FidelityExpectedObservation> ExpectedObservations,
    IReadOnlyList<FidelityTolerance> Tolerances, bool IntentionalDeviation,
    IReadOnlyList<string> AffectedRulesets, IReadOnlyList<string> AffectedRoles,
    IReadOnlyList<string> RegressionTests, IReadOnlyList<string> EvidenceLinks, string Decision);

public static partial class FidelityCaseParser
{
    public const int SchemaVersion = 1;
    public const int MaximumFixtureBytes = 256 * 1024;
    public const int MaximumTicks = 60 * 60 * 60;
    public const int MaximumActions = 4096;
    public const int MaximumObservations = 8192;
    private const int MaximumText = 1024;
    private static readonly JsonSerializerOptions Json = CreateOptions();

    [GeneratedRegex("^FID-[A-Z]+-[0-9]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CaseIdPattern();

    public static FidelityCaseDocument Parse(ReadOnlySpan<byte> utf8,
        string expectedReferenceRevision, string expectedReferenceDigest)
    {
        if (utf8.Length is < 2 or > MaximumFixtureBytes)
            throw new InvalidDataException($"Fidelity fixture must be between 2 and {MaximumFixtureBytes} bytes.");
        FidelityCaseDocument value;
        try
        {
            value = JsonSerializer.Deserialize<FidelityCaseDocument>(utf8, Json)
                ?? throw new InvalidDataException("Fidelity fixture is null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Fidelity fixture JSON is malformed or unsupported.", ex);
        }
        Validate(value, expectedReferenceRevision, expectedReferenceDigest);
        return value;
    }

    public static FidelityCaseDocument Load(string fixtureRoot, string relativePath,
        string expectedReferenceRevision, string expectedReferenceDigest)
    {
        string root = Path.GetFullPath(fixtureRoot);
        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists || rootInfo.LinkTarget != null || (rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Fidelity fixture root must be a non-symbolic directory.");
        string normalized = relativePath.Replace('\\', '/');
        if (String.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized)
            || normalized.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Fidelity fixture path must be normalized and relative.");
        string path = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("Fidelity fixture path escapes its root.");
        var info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget != null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Fidelity fixture must be a regular, non-symbolic file.");
        if (info.Length is < 2 or > MaximumFixtureBytes)
            throw new InvalidDataException($"Fidelity fixture must be between 2 and {MaximumFixtureBytes} bytes.");
        return Parse(File.ReadAllBytes(path), expectedReferenceRevision, expectedReferenceDigest);
    }

    public static string Serialize(FidelityCaseDocument value) => JsonSerializer.Serialize(value, Json) + "\n";

    private static void Validate(FidelityCaseDocument value, string expectedRevision, string expectedDigest)
    {
        if (value.SchemaVersion != SchemaVersion) Fail("Unsupported fidelity case schema version.");
        if (!CaseIdPattern().IsMatch(value.Id ?? "")) Fail("Fidelity case ID is invalid.");
        Text(value.Title, nameof(value.Title)); Text(value.ReferenceRevision, nameof(value.ReferenceRevision));
        Digest(value.ReferenceContentDigest, nameof(value.ReferenceContentDigest));
        if (!StringComparer.Ordinal.Equals(value.ReferenceRevision, expectedRevision)
            || !StringComparer.OrdinalIgnoreCase.Equals(value.ReferenceContentDigest, expectedDigest))
            Fail("Fidelity case reference identity does not match the active reference.");
        if (value.Environment == null || value.Environment.Count > 32) Fail("Environment exceeds its bound.");
        foreach ((string key, string item) in value.Environment) { Text(key, "environment key", 128); Text(item, "environment value"); }
        TextList(value.Preconditions, nameof(value.Preconditions), 64);
        if (value.Scenario == null) Fail("Scenario is required.");
        Text(value.Scenario.Map, "scenario map", 128); Text(value.Scenario.Mode, "scenario mode", 64); Text(value.Scenario.Ruleset, "scenario ruleset", 64);
        if (value.Scenario.PlayerCount is < 1 or > 8) Fail("Scenario player count is outside 1-8.");
        if (value.Scenario.MaxTicks is < 1 or > MaximumTicks) Fail("Scenario tick count is outside its bound.");
        if (value.Scenario.Actions == null || value.Scenario.Actions.Count > MaximumActions) Fail("Scenario actions exceed their bound.");
        Unique(value.Scenario.Actions.Select(action => action.Id), "action");
        foreach (FidelityScheduledAction action in value.Scenario.Actions)
        {
            Text(action.Id, "action ID", 128); Text(action.ParticipantId, "participant ID", 128); Text(action.Type, "action type", 128);
            if (action.Tick < 0 || action.Tick >= value.Scenario.MaxTicks) Fail("Action tick is outside the scenario.");
            if (action.Values == null || action.Values.Count > 32) Fail("Action values exceed their bound.");
            foreach ((string key, double item) in action.Values)
            { Text(key, "action value key", 128); if (!Double.IsFinite(item)) Fail("Action numeric values must be finite."); }
        }
        if (value.ExpectedObservations == null || value.ExpectedObservations.Count > MaximumObservations) Fail("Expected observations exceed their bound.");
        Unique(value.ExpectedObservations.Select(observation => observation.Id), "observation");
        foreach (FidelityExpectedObservation observation in value.ExpectedObservations)
        {
            Text(observation.Id, "observation ID", 128); Text(observation.EntityId, "entity ID", 128); Text(observation.Field, "observation field", 128);
            if (observation.Tick < 0 || observation.Tick >= value.Scenario.MaxTicks) Fail("Observation tick is outside the scenario.");
            if (observation.NumericValue is double number && !Double.IsFinite(number)) Fail("Observation values must be finite.");
            if ((observation.NumericValue == null) == (observation.ExactValue == null)) Fail("An observation must have exactly one numeric or exact value.");
            if (observation.ExactValue != null) Text(observation.ExactValue, "observation exact value");
        }
        if (value.Tolerances == null || value.Tolerances.Count > 128) Fail("Tolerances exceed their bound.");
        Unique(value.Tolerances.Select(tolerance => tolerance.Field), "tolerance field");
        foreach (FidelityTolerance tolerance in value.Tolerances)
        {
            Text(tolerance.Field, "tolerance field", 128); Text(tolerance.Unit, "tolerance unit", 64); Text(tolerance.Reason, "tolerance reason");
            if (!Double.IsFinite(tolerance.Absolute) || tolerance.Absolute < 0) Fail("Tolerance must be finite and non-negative.");
        }
        TextList(value.AffectedRulesets, nameof(value.AffectedRulesets), 8); TextList(value.AffectedRoles, nameof(value.AffectedRoles), 16);
        TextList(value.RegressionTests, nameof(value.RegressionTests), 128); TextList(value.EvidenceLinks, nameof(value.EvidenceLinks), 128);
        if (value.Decision == null || value.Decision.Length > 16 * 1024) Fail("Decision exceeds its bound.");
    }

    private static void TextList(IReadOnlyList<string>? values, string name, int maximum)
    { if (values == null || values.Count > maximum) Fail($"{name} exceeds its bound."); foreach (string value in values) Text(value, name); }
    private static void Unique(IEnumerable<string> values, string name)
    { var seen = new HashSet<string>(StringComparer.Ordinal); foreach (string value in values) if (!seen.Add(value ?? "")) Fail($"Duplicate {name} ID: {value}"); }
    private static void Text(string? value, string name, int maximum = MaximumText)
    { if (String.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(Char.IsControl)) Fail($"{name} is missing, too long, or contains control characters."); }
    private static void Digest(string? value, string name)
    { if (value == null || value.Length != 64 || !value.All(Uri.IsHexDigit)) Fail($"{name} is not a SHA-256 digest."); }
    [DoesNotReturn] private static void Fail(string message) => throw new InvalidDataException(message);
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, AllowDuplicateProperties = false };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
