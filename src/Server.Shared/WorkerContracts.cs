using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using MphRead;
using MphRead.Mods.Network;

namespace ProjectPrime.Server.Shared;

public enum WorkerStatus { Starting, Ready, Draining, Faulted, Stopped }
public enum MatchStatus { Starting, Ready, Running, Completed, Failed, Interrupted }
public enum PersistenceHealth { Healthy = 1, Degraded = 2, Unavailable = 3 }
public sealed record WorkerCapacity(int MatchLimit, int PlayerLimit, int ActiveMatches, int ActivePlayers)
{
    public void Validate()
    {
        if (MatchLimit is < 1 or > 1024 || PlayerLimit is < 1 or > 8192
            || ActiveMatches < 0 || ActiveMatches > MatchLimit || ActivePlayers < 0 || ActivePlayers > PlayerLimit)
            throw new ArgumentException("Invalid worker capacity.");
    }
}
public sealed record WorkerHealth(WorkerStatus Status, long UptimeMilliseconds, double TickP99Milliseconds, long WorkingSetBytes, WorkerDiagnostics? Diagnostics = null)
{
    public void Validate()
    {
        ContractGuard.Defined(Status);
        if (UptimeMilliseconds < 0 || !double.IsFinite(TickP99Milliseconds) || TickP99Milliseconds < 0 || WorkingSetBytes < 0)
            throw new ArgumentException("Invalid worker health.");
        Diagnostics?.Validate();
    }
}
public sealed record MatchPlacement(MatchId MatchId, WireMatchId WireMatchId, WorkerId WorkerId, Guid WorkerIncarnation,
    string Host, ushort Port, bool UdpAuthenticationEnabled = true)
{
    public void Validate()
    {
        ContractGuard.Id(MatchId.Value); ContractGuard.Id(WorkerId.Value); ContractGuard.Id(WorkerIncarnation);
        ContractGuard.Text(Host, 253);
        if (WireMatchId.Value == 0 || Port == 0) throw new ArgumentException("Wire match identity and port are required.");
    }
}
public sealed record NodeMatchSummary(MatchId MatchId, WireMatchId WireMatchId, LobbyId LobbyId, WorkerId WorkerId, Guid WorkerIncarnation,
    MatchStatus Status, string MapKey, int Players, int Observers)
{
    public void Validate()
    {
        ContractGuard.Id(MatchId.Value); ContractGuard.Id(LobbyId.Value); ContractGuard.Id(WorkerId.Value);
        ContractGuard.Id(WorkerIncarnation);
        if (WireMatchId.Value == 0) throw new ArgumentException("Wire match identity is required.");
        ContractGuard.Defined(Status); ContractGuard.Text(MapKey, 128);
        if (Players is < 0 or > MultiplayerLimits.MaxPlayers
            || Observers is < 0 or > MultiplayerLimits.MaxObservers
            || Players + Observers > MultiplayerLimits.MaxHumanConnections)
            throw new ArgumentException("Invalid match summary counts.");
    }
}

public abstract record WorkerMessage;
public abstract record WorkerCommand : WorkerMessage;
public abstract record WorkerEvent : WorkerMessage;
public sealed record WorkerConfigure(NodeId NodeId, Guid NodeIncarnation, WorkerCapacity Capacity) : WorkerCommand;
public sealed record CreateMatch(MatchSpec Spec) : WorkerCommand;
[method: JsonConstructor]
public sealed record CancelMatch(MatchId MatchId, string OperationId, string Reason) : WorkerCommand
{ }
public sealed record Drain(string Reason) : WorkerCommand;
public sealed record Shutdown(string Reason) : WorkerCommand;
public enum AdminAction { Pause, Resume, EndMatch, KickSeat, LagCompHistory, LagCompDynamic, LagCompClear }
public sealed record MatchAdminCommand(MatchId MatchId, AdminAction Action, byte? SeatId) : WorkerCommand;
/// <summary>Public verification material only; private signing keys remain with Node.</summary>
public sealed record UpdateNodeSigningKey(string KeyId, string PublicKey) : WorkerCommand;
/// <summary>
/// Installs one short-lived UDP admission secret in the owning match. The
/// command is control-plane-only; the secret is deliberately absent from the
/// acknowledgement and all diagnostic string representations.
/// </summary>
public sealed record InstallAdmissionKey(
    Guid AdmissionId, Guid TicketId, Guid NodeSessionId,
    NodeId NodeId, Guid NodeIncarnation,
    MatchId MatchId, WireMatchId WireMatchId,
    WorkerId WorkerId, Guid WorkerIncarnation,
    byte SeatId, ulong JoinNonce, long ExpiresAt, string AdmissionKey,
    HandoffGeneration HandoffGeneration) : WorkerCommand
{
    public override string ToString()
        => $"InstallAdmissionKey {{ AdmissionId = {AdmissionId}, TicketId = {TicketId}, MatchId = {MatchId}, SeatId = {SeatId} }}";
}
/// <summary>First message on a new local connection. Transport must verify and consume StartupToken before accepting commands.</summary>
public sealed record WorkerContentIdentity(string ContentVersion, string ContentHash, string BuildVersion, byte ProtocolVersion)
{
    public void Validate()
    {
        ContractGuard.Text(ContentVersion, 128); ContractGuard.Text(ContentHash, 128); ContractGuard.Text(BuildVersion, 128);
        if (ProtocolVersion == 0) throw new ArgumentException("Protocol version is required.");
    }
}

/// <summary>Validation and canonical encoding rules for the phase-A UDP admission secret.</summary>
public static class AdmissionKeyRules
{
    public const int ByteLength = 32;
    public const int Base64Length = 44;

    public static byte[] Decode(string value)
    {
        if (value is null || value.Length != Base64Length)
            throw new ArgumentException("Admission key must be a 32-byte canonical base64 value.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(value); }
        catch (FormatException error) { throw new ArgumentException("Admission key is not valid base64.", error); }
        if (bytes.Length != ByteLength || !String.Equals(Convert.ToBase64String(bytes), value, StringComparison.Ordinal))
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new ArgumentException("Admission key must be a 32-byte canonical base64 value.");
        }
        return bytes;
    }

    public static void Validate(string value)
    {
        byte[] bytes = Decode(value);
        CryptographicOperations.ZeroMemory(bytes);
    }
}

/// <summary>
/// Content discovery returned by the Worker before the Node is configured.
/// This stays outside Worker IPC: the authenticated Worker hello carries only
/// the immutable identity used by scheduling, while launchers use this
/// descriptor to construct the Node's map/mode catalog.
/// </summary>
public sealed record ContentDescription(string ContentVersion, string ContentHash, string BuildVersion,
    byte ProtocolVersion, ServerContentScenario[] Scenarios)
{
    public WorkerContentIdentity Identity => new(ContentVersion, ContentHash, BuildVersion, ProtocolVersion);

    public ContentDescription Canonicalized()
        => this with { Scenarios = Canonicalize(Scenarios) };

    public void Validate()
    {
        Identity.Validate();
        _ = Canonicalize(Scenarios);
    }

    public static ServerContentScenario[] Canonicalize(IEnumerable<ServerContentScenario> scenarios)
    {
        if (scenarios == null) throw new ArgumentNullException(nameof(scenarios));
        ServerContentScenario[] entries = scenarios.ToArray();
        if (entries.Length is < 1 or > 768)
            throw new ArgumentException("Content description must contain 1–768 supported map/mode pairs.");

        var maps = new HashSet<string>(StringComparer.Ordinal);
        var pairs = new HashSet<ServerContentScenario>();
        foreach (ServerContentScenario? scenario in entries)
        {
            if (scenario == null || !MapKeyText(scenario.Room)
                || !IsKnownMultiplayerMode(scenario.Mode) || !pairs.Add(scenario))
                throw new ArgumentException("Content description contains an invalid or duplicate map/mode pair.");
            maps.Add(scenario.Room);
        }
        if (maps.Count > 256)
            throw new ArgumentException("Content description supports at most 256 maps.");
        return entries.OrderBy(scenario => scenario.Room, StringComparer.Ordinal)
            .ThenBy(scenario => (byte)scenario.Mode).ToArray();
    }

    public static bool IsKnownMultiplayerMode(GameMode mode)
        => mode is GameMode.Battle or GameMode.BattleTeams or GameMode.Survival or GameMode.SurvivalTeams
            or GameMode.Capture or GameMode.Bounty or GameMode.BountyTeams or GameMode.Nodes or GameMode.NodesTeams
            or GameMode.Defender or GameMode.DefenderTeams or GameMode.PrimeHunter;

    public static bool MapKeyText(string? value)
        => value is { Length: > 0 and <= 128 } && value.Any(c => !char.IsWhiteSpace(c))
            && value.All(c => c is >= ' ' and <= '~');
}

public sealed record WorkerHello(WorkerId WorkerId, Guid WorkerIncarnation, NodeId NodeId, string StartupToken, string BuildVersion,
    WorkerContentIdentity? Content = null) : WorkerEvent;
public sealed record WorkerReady(WorkerId WorkerId, Guid WorkerIncarnation, WorkerCapacity Capacity) : WorkerEvent;
public sealed record WorkerHeartbeat(WorkerId WorkerId, Guid WorkerIncarnation, WorkerCapacity Capacity, WorkerHealth Health) : WorkerEvent;
public sealed record MatchReady(MatchPlacement Placement) : WorkerEvent;
public sealed record MatchStarted(MatchId MatchId) : WorkerEvent;
public sealed record MatchCompleted(MatchCompletionSummary Summary) : WorkerEvent;
public sealed record MatchFailed(MatchId MatchId, string Reason) : WorkerEvent;
public sealed record MatchInterrupted(MatchId MatchId, string Reason) : WorkerEvent;
/// <summary>Opaque report reference, never an executable path or an unbounded embedded report.</summary>
public sealed record MatchReportReady(MatchId MatchId, Guid ReportId, WorkerId WorkerId,
    Guid WorkerIncarnation, string PayloadHash, int PayloadBytes) : WorkerEvent
{
    public const int MaximumPayloadBytes = 512 * 1024;
    public void Validate()
    {
        ContractGuard.Id(MatchId.Value); ContractGuard.Id(ReportId); ContractGuard.Id(WorkerId.Value);
        ContractGuard.Id(WorkerIncarnation);
        if (PayloadBytes is < 1 or > MaximumPayloadBytes || PayloadHash is not { Length: 64 }
            || PayloadHash.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Invalid report artifact metadata.");
    }
}
/// <summary>Finite, secret-free reasons why an intended report artifact could
/// not be made available. This is a disposition of the report artifact only;
/// it never changes an already recorded gameplay terminal.</summary>
public enum ArtifactFailureCode
{
    QueueExhausted = 1,
    PersistenceFailed = 2,
    DeadlineExceeded = 3,
    Shutdown = 4,
    WorkerLost = 5
}

/// <summary>Explicit report-unavailable disposition paired with a completed
/// gameplay terminal. ReportId remains the stable intended artifact identity,
/// even when the artifact was not durable.</summary>
public sealed record MatchReportUnavailable(MatchId MatchId, Guid ReportId,
    WorkerId WorkerId, Guid WorkerIncarnation, ArtifactFailureCode FailureCode) : WorkerEvent
{
    public void Validate()
    {
        ContractGuard.Id(MatchId.Value); ContractGuard.Id(ReportId);
        ContractGuard.Id(WorkerId.Value); ContractGuard.Id(WorkerIncarnation);
        ContractGuard.Defined(FailureCode);
    }
}
public sealed record WorkerDraining(WorkerId WorkerId, Guid WorkerIncarnation) : WorkerEvent;
public sealed record WorkerFault(WorkerId WorkerId, Guid WorkerIncarnation, string Reason) : WorkerEvent;
public sealed record NodeSigningKeyUpdated(WorkerId WorkerId,
    Guid WorkerIncarnation, string KeyId) : WorkerEvent;
/// <summary>Positive acknowledgement for an idempotent match cancellation.</summary>
public sealed record MatchCancelAccepted(WorkerId WorkerId, Guid WorkerIncarnation,
    MatchId MatchId, string OperationId, bool AlreadyAccepted = false) : WorkerEvent;
public sealed record MatchCancelRejected(WorkerId WorkerId, Guid WorkerIncarnation,
    MatchId MatchId, string OperationId, string Reason) : WorkerEvent;
/// <summary>Positive acknowledgement that a Worker match owns the admission key.</summary>
public sealed record AdmissionKeyInstalled(
    Guid AdmissionId, Guid TicketId, Guid NodeSessionId,
    NodeId NodeId, Guid NodeIncarnation,
    MatchId MatchId, WireMatchId WireMatchId,
    WorkerId WorkerId, Guid WorkerIncarnation,
    byte SeatId, ulong JoinNonce, long ExpiresAt,
    HandoffGeneration HandoffGeneration) : WorkerEvent;
/// <summary>Bounded, non-secret rejection of an admission-key installation.</summary>
public sealed record AdmissionKeyInstallFailed(Guid AdmissionId, MatchId MatchId, string Reason) : WorkerEvent;

/// <summary>Retires one exact admission lease; it can never remove a newer lease.</summary>
public sealed record RetireAdmission(
    MatchId MatchId, Guid NodeSessionId, byte SeatId,
    HandoffGeneration HandoffGeneration, Guid AdmissionId,
    WorkerId WorkerId, Guid WorkerIncarnation) : WorkerCommand;

public sealed record AdmissionRetired(
    MatchId MatchId, Guid AdmissionId, byte SeatId,
    HandoffGeneration HandoffGeneration, WorkerId WorkerId,
    Guid WorkerIncarnation, bool AlreadyRetired = false) : WorkerEvent;

public sealed record AdmissionRetireFailed(
    MatchId MatchId, Guid AdmissionId, byte SeatId,
    HandoffGeneration HandoffGeneration, WorkerId WorkerId,
    Guid WorkerIncarnation, string Reason) : WorkerEvent;

public sealed record WorkerLaneHealth(int LaneId, int Matches, long Ticks, long CatchUpTicks, long DroppedTicks,
    double P50Milliseconds, double P95Milliseconds, double P99Milliseconds, double MaxMilliseconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double P999Milliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long DeadlineMisses = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long CommandQueueHighWater = 0);
public sealed record WorkerDiagnostics(ImmutableArray<WorkerLaneHealth> Lanes, double CpuPercent, long ManagedHeapBytes,
    int Gen0Collections, int Gen1Collections, int Gen2Collections, long PacketsReceived, long PacketsSent,
    long BytesReceived, long BytesSent, long QueueDrops, long PacketsRejected, ImmutableArray<WorkerMatchHealth> Matches = default, int TotalMatches = 0, bool MatchesTruncated = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double AllocationBytesPerSecond = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double NetworkLoopP99Milliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double NetworkLoopP999Milliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long NetworkQueueHighWater = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long NetworkLoopSampleCount = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long PreAuthIngressDrops = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long AdmissionIngressDrops = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long EstablishedIngressDrops = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long PerConnectionQuotaDrops = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long MaximumConnectionIngressDepth = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long CriticalTransportDrops = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long TimingTelemetryObservedConnections = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long TimingTelemetryStaleConnections = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double TimingTelemetryMaxAgeSeconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long TimingTelemetryStaleIntervals = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long TimingDownshiftBlocked = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long NetworkWakeups = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long ImmediateRepumps = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long IdleWaits = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long ReceiveToRouteSampleCount = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double ReceiveToRouteP50Milliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double ReceiveToRouteP95Milliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double ReceiveToRouteP99Milliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double ReceiveToRouteP999Milliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double ReceiveToRouteMaxMilliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long OutboundEnqueueToSendSampleCount = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double OutboundEnqueueToSendP50Milliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double OutboundEnqueueToSendP95Milliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double OutboundEnqueueToSendP99Milliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double OutboundEnqueueToSendP999Milliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double OutboundEnqueueToSendMaxMilliseconds = 0,
    // Append-only worker lifetime facts. These are intentionally low-cardinality
    // counters rather than per-match identities; a pool can use them to choose
    // a replacement without receiving the match history itself.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long LifetimeMatchesAccepted = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int IdentityHistoryUsed = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int IdentityHistoryCapacity = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ActiveAdmissions = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] PersistenceHealth PersistenceHealth = PersistenceHealth.Healthy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ActiveArtifacts = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int QueuedArtifacts = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ExecutingArtifacts = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long ArtifactFailures = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long ArtifactCompleted = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double ArtifactDurationAverageMilliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long TerminalConflicts = 0)
{
    // The IPC frame is 64 KiB and diagnostics serialize as JSON. This cap is
    // derived from the maximum-shape heartbeat (64 lanes plus 32 fully
    // populated match entries), including the worst-case JSON escaping of the
    // 32-character Phase/State fields. Keep headroom below the frame ceiling
    // so non-default telemetry cannot make a valid heartbeat unencodable.
    public const int MaximumMatchSamples = 32;
    private const int LegacyMaximumMatchSamples = 128;

    public void Validate()
    {
        if (Lanes.IsDefault || Lanes.Length is < 1 or > 64 || !double.IsFinite(CpuPercent) || CpuPercent is < 0 or > 100
            || ManagedHeapBytes < 0 || Gen0Collections < 0 || Gen1Collections < 0 || Gen2Collections < 0
            || PacketsReceived < 0 || PacketsSent < 0 || BytesReceived < 0 || BytesSent < 0 || QueueDrops < 0 || PacketsRejected < 0
            || !double.IsFinite(AllocationBytesPerSecond) || AllocationBytesPerSecond < 0
            || !double.IsFinite(NetworkLoopP99Milliseconds) || NetworkLoopP99Milliseconds < 0
            || !double.IsFinite(NetworkLoopP999Milliseconds) || NetworkLoopP999Milliseconds < 0
            || PreAuthIngressDrops < 0 || AdmissionIngressDrops < 0
            || EstablishedIngressDrops < 0 || PerConnectionQuotaDrops < 0
            || MaximumConnectionIngressDepth < 0 || CriticalTransportDrops < 0
            || TimingTelemetryObservedConnections < 0 || TimingTelemetryStaleConnections < 0
            || !double.IsFinite(TimingTelemetryMaxAgeSeconds) || TimingTelemetryMaxAgeSeconds < 0
            || TimingTelemetryStaleIntervals < 0 || TimingDownshiftBlocked < 0
            || LifetimeMatchesAccepted < 0 || IdentityHistoryUsed < 0
            || IdentityHistoryCapacity < 0 || IdentityHistoryUsed > IdentityHistoryCapacity
            || ActiveAdmissions < 0 || ActiveAdmissions > 65536
            || !Enum.IsDefined(PersistenceHealth) || ActiveArtifacts < 0 || ActiveArtifacts > 65536
            || QueuedArtifacts < 0 || QueuedArtifacts > 65536 || ExecutingArtifacts < 0 || ExecutingArtifacts > 65536
            || ArtifactFailures < 0 || ArtifactCompleted < 0
            || !double.IsFinite(ArtifactDurationAverageMilliseconds) || ArtifactDurationAverageMilliseconds < 0
            || TerminalConflicts < 0
            || NetworkLoopQueueBoundsInvalid() || NetworkLoopAgeBoundsInvalid())
            throw new ArgumentException("Invalid worker diagnostics.");
        if (!Matches.IsDefault)
        {
            if (Matches.Length > LegacyMaximumMatchSamples || TotalMatches < Matches.Length || TotalMatches > 1024
                || Matches.Length > MaximumMatchSamples && Matches.Any(match => match is null || !IsLegacyMatch(match))
                || MatchesTruncated != (TotalMatches > Matches.Length)) throw new ArgumentException("Invalid match diagnostic sample.");
            var matchIds = new HashSet<MatchId>();
            foreach (var match in Matches)
            {
                if (match is null || !matchIds.Add(match.MatchId) || match.WireMatchId.Value == 0)
                    throw new ArgumentException("Invalid match diagnostics.");
                ContractGuard.Id(match.MatchId.Value); ContractGuard.Text(match.Phase, 32); ContractGuard.Text(match.State, 32);
                if (match.TickSamples < 0 || match.DeadlineMisses < 0 || match.ProcessGen0Collections < 0
                    || match.ProcessGen1Collections < 0 || match.ProcessGen2Collections < 0
                    || match.ObserverRetainedFrames < 0 || match.ObserverRetainedBytes < 0
                    || match.ReplayQueueDepth < 0 || match.ReplayQueueHighWater < 0
                    || match.StatusPublicationCount < 0
                    || !double.IsFinite(match.TickP50Milliseconds) || !double.IsFinite(match.TickP95Milliseconds)
                    || !double.IsFinite(match.TickP99Milliseconds) || !double.IsFinite(match.TickP999Milliseconds)
                    || !double.IsFinite(match.TickMaxMilliseconds) || !double.IsFinite(match.AllocatedBytesPerTick)
                    || !double.IsFinite(match.AllocatedBytesPerSecond)
                    || !double.IsFinite(match.StatusPublicationCadenceHz)
                    || match.TickP50Milliseconds < 0 || match.TickP95Milliseconds < 0
                    || match.TickP99Milliseconds < 0 || match.TickP999Milliseconds < 0 || match.TickMaxMilliseconds < 0
                    || match.AllocatedBytesPerTick < 0 || match.AllocatedBytesPerSecond < 0
                    || match.StatusPublicationCadenceHz < 0
                    || match.TickP95Milliseconds < match.TickP50Milliseconds
                    || match.TickP99Milliseconds < match.TickP95Milliseconds
                    || match.TickMaxMilliseconds < match.TickP99Milliseconds
                    || match.TickP999Milliseconds > 0 && (match.TickP999Milliseconds < match.TickP99Milliseconds
                        || match.TickMaxMilliseconds < match.TickP999Milliseconds))
                    throw new ArgumentException("Invalid match performance diagnostics.");
            }
        }
        var ids = new HashSet<int>();
        foreach (var lane in Lanes)
            if (lane is null || lane.LaneId < 0 || !ids.Add(lane.LaneId) || lane.Matches is < 0 or > 1024
                || lane.Ticks < 0 || lane.CatchUpTicks < 0 || lane.DroppedTicks < 0
                || lane.DeadlineMisses < 0 || lane.CommandQueueHighWater < 0
                || !double.IsFinite(lane.P50Milliseconds) || !double.IsFinite(lane.P95Milliseconds)
                || !double.IsFinite(lane.P99Milliseconds) || !double.IsFinite(lane.P999Milliseconds)
                || !double.IsFinite(lane.MaxMilliseconds)
                || lane.P50Milliseconds < 0 || lane.P95Milliseconds < lane.P50Milliseconds
                || lane.P99Milliseconds < lane.P95Milliseconds || lane.MaxMilliseconds < lane.P99Milliseconds
                || lane.P999Milliseconds > 0 && (lane.P999Milliseconds < lane.P99Milliseconds
                    || lane.MaxMilliseconds < lane.P999Milliseconds))
                throw new ArgumentException("Invalid lane diagnostics.");
    }

    private bool NetworkLoopQueueBoundsInvalid()
        => NetworkQueueHighWater < 0 || NetworkLoopSampleCount < 0
            || NetworkLoopSampleCount > 0 && NetworkLoopP999Milliseconds < NetworkLoopP99Milliseconds;

    private bool NetworkLoopAgeBoundsInvalid()
        => NetworkWakeups < 0 || ImmediateRepumps < 0 || IdleWaits < 0
            || ReceiveToRouteSampleCount < 0 || OutboundEnqueueToSendSampleCount < 0
            || !double.IsFinite(ReceiveToRouteP50Milliseconds) || ReceiveToRouteP50Milliseconds < 0
            || !double.IsFinite(ReceiveToRouteP95Milliseconds) || ReceiveToRouteP95Milliseconds < ReceiveToRouteP50Milliseconds
            || !double.IsFinite(ReceiveToRouteP99Milliseconds) || ReceiveToRouteP99Milliseconds < ReceiveToRouteP95Milliseconds
            || !double.IsFinite(ReceiveToRouteP999Milliseconds) || ReceiveToRouteP999Milliseconds < ReceiveToRouteP99Milliseconds
            || !double.IsFinite(ReceiveToRouteMaxMilliseconds) || ReceiveToRouteMaxMilliseconds < ReceiveToRouteP999Milliseconds
            || !double.IsFinite(OutboundEnqueueToSendP50Milliseconds) || OutboundEnqueueToSendP50Milliseconds < 0
            || !double.IsFinite(OutboundEnqueueToSendP95Milliseconds) || OutboundEnqueueToSendP95Milliseconds < OutboundEnqueueToSendP50Milliseconds
            || !double.IsFinite(OutboundEnqueueToSendP99Milliseconds) || OutboundEnqueueToSendP99Milliseconds < OutboundEnqueueToSendP95Milliseconds
            || !double.IsFinite(OutboundEnqueueToSendP999Milliseconds) || OutboundEnqueueToSendP999Milliseconds < OutboundEnqueueToSendP99Milliseconds
            || !double.IsFinite(OutboundEnqueueToSendMaxMilliseconds) || OutboundEnqueueToSendMaxMilliseconds < OutboundEnqueueToSendP999Milliseconds;

    private static bool IsLegacyMatch(WorkerMatchHealth match)
        => match.TickSamples == 0 && match.DeadlineMisses == 0
            && match.TickP50Milliseconds == 0 && match.TickP95Milliseconds == 0
            && match.TickP99Milliseconds == 0 && match.TickP999Milliseconds == 0
            && match.TickMaxMilliseconds == 0 && match.AllocatedBytesPerTick == 0
            && match.AllocatedBytesPerSecond == 0 && match.ProcessGen0Collections == 0
            && match.ProcessGen1Collections == 0 && match.ProcessGen2Collections == 0
            && match.ObserverRetainedFrames == 0 && match.ObserverRetainedBytes == 0
            && match.ReplayQueueDepth == 0 && match.ReplayQueueHighWater == 0
            && !match.ReplayQueueOverflowed && match.StatusPublicationCount == 0
            && match.StatusPublicationCadenceHz == 0;
}
public sealed record MatchAdminResult(MatchId MatchId, AdminAction Action, bool Applied, string? Code, string? Message) : WorkerEvent;

public sealed record WorkerMatchHealth(MatchId MatchId, WireMatchId WireMatchId, uint Tick, string Phase, string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long TickSamples = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long DeadlineMisses = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double TickP50Milliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double TickP95Milliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double TickP99Milliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double TickP999Milliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double TickMaxMilliseconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double AllocatedBytesPerTick = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double AllocatedBytesPerSecond = 0,
    [property: JsonPropertyName("gen0Collections"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long ProcessGen0Collections = 0,
    [property: JsonPropertyName("gen1Collections"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long ProcessGen1Collections = 0,
    [property: JsonPropertyName("gen2Collections"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long ProcessGen2Collections = 0,
    // Append-only operational measurements. Defaults keep older heartbeat
    // readers and the legacy diagnostic shape wire-compatible.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long ObserverRetainedFrames = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long ObserverRetainedBytes = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long ReplayQueueDepth = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long ReplayQueueHighWater = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ReplayQueueOverflowed = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long StatusPublicationCount = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double StatusPublicationCadenceHz = 0);
