using System.Text.Json;

namespace MphRead;

/// <summary>
/// The only source identity accepted by the retail oracle at this stage.
/// AMHE0 observations are useful hypotheses, but they are never silently
/// promoted to the AMHE1 comparison target.
/// </summary>
public sealed record OracleSourceIdentity(
    string ProjectRevision,
    string ReferenceRevision,
    string ReferenceAnchorPath,
    string ReferenceAnchorSha256,
    string ReferenceAggregateSha256);

public sealed record OracleRomIdentity(
    string Identity,
    string GameCode,
    byte RomVersion,
    long Length);

public sealed record OracleAction(
    int At,
    string Action,
    IReadOnlyDictionary<string, double> Values);

/// <summary>
/// A requested observation point. Timing is deliberately represented as three
/// independent coordinates: a VBlank counter, game elapsed time, and a
/// semantic event index. An adapter may omit the latter two for a scenario
/// that only schedules VBlank checkpoints; recorded artifacts must provide all
/// three.
/// </summary>
public sealed record OracleCheckpointRequest(
    int VBlank,
    double? ElapsedMilliseconds,
    int? SemanticEventIndex);

public sealed record OracleScenario(
    int SchemaVersion,
    string Id,
    OracleSourceIdentity Source,
    string Hunter,
    string Map,
    uint Seed,
    IReadOnlyList<OracleAction> Actions,
    IReadOnlyList<OracleCheckpointRequest> Checkpoints);

public sealed record OracleArtifactManifest(
    int SchemaVersion,
    string ScenarioId,
    OracleSourceIdentity Source,
    string OracleImplementationRevision,
    string RomIdentity,
    string HostOs,
    string OracleExecutableIdentity,
    string ScenarioHash,
    uint Seed,
    DateTimeOffset RunTimestamp,
    string Result);

/// <summary>
/// A complete normalized observation. The manifest carries provenance and
/// adapter identity; checkpoint and event streams remain separate so timing
/// and semantic ordering cannot be accidentally collapsed into one value.
/// </summary>
public sealed record OracleArtifact(
    int SchemaVersion,
    OracleArtifactManifest Manifest,
    IReadOnlyList<OracleCheckpoint> Checkpoints,
    IReadOnlyList<OracleEvent> Events);

/// <summary>One normalized checkpoint emitted by an external oracle adapter.</summary>
public sealed record OracleCheckpoint(
    int VBlank,
    double ElapsedMilliseconds,
    int SemanticEventIndex,
    IReadOnlyDictionary<string, JsonElement> State);

public sealed record OracleEvent(
    int Index,
    string Name,
    int VBlank,
    double ElapsedMilliseconds);

public enum OracleComparisonMode
{
    Exact,
    Tolerant,
    Invariant
}

public sealed record OracleFieldRule(
    string Path,
    OracleComparisonMode Mode,
    double? AbsoluteTolerance,
    string? Invariant);

public sealed record OracleNormalizer(
    int SchemaVersion,
    string Revision,
    IReadOnlyList<OracleFieldRule> Fields);

public sealed record OracleDivergence(
    int CheckpointIndex,
    int VBlank,
    string Path,
    string Expected,
    string Actual,
    string Reason);

/// <summary>
/// Comparison output is itself normalized and bounded. A false result is
/// never accompanied by a claim that the missing/incompatible side matched.
/// </summary>
public sealed record OracleComparison(
    int SchemaVersion,
    string ScenarioId,
    bool Compatible,
    bool Equal,
    int ComparedCheckpoints,
    int ComparedEvents,
    OracleDivergence? FirstDivergence,
    IReadOnlyList<string> Diagnostics);

public sealed record OracleRecordResult(
    string RunDirectory,
    OracleArtifact Artifact,
    int AdapterExitCode,
    bool TimedOut);

public sealed record OracleScenarioSummary(
    string Id,
    string Hunter,
    string Map,
    int ActionCount,
    int CheckpointCount,
    string ReferenceRevision,
    string ProjectRevision);
