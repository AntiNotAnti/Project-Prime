using System.Collections.Immutable;
using MphRead;
using MphRead.Mods.Network;

namespace FruityPrime.Server.Shared;

public enum WorkerStatus { Starting, Ready, Draining, Faulted, Stopped }
public enum MatchStatus { Starting, Ready, Running, Completed, Failed, Interrupted }
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
public sealed record MatchPlacement(MatchId MatchId, WireMatchId WireMatchId, WorkerId WorkerId, Guid WorkerIncarnation, string Host, ushort Port)
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
        if (Players is < 0 or > 8 || Observers is < 0 or > 32 || Players + Observers > 32)
            throw new ArgumentException("Invalid match summary counts.");
    }
}

public abstract record WorkerMessage;
public abstract record WorkerCommand : WorkerMessage;
public abstract record WorkerEvent : WorkerMessage;
public sealed record WorkerConfigure(NodeId NodeId, Guid NodeIncarnation, WorkerCapacity Capacity) : WorkerCommand;
public sealed record CreateMatch(MatchSpec Spec) : WorkerCommand;
public sealed record CancelMatch(MatchId MatchId, string Reason) : WorkerCommand;
public sealed record Drain(string Reason) : WorkerCommand;
public sealed record Shutdown(string Reason) : WorkerCommand;
public enum AdminAction { Pause, Resume, EndMatch, KickSeat, LagCompHistory, LagCompDynamic, LagCompClear }
public sealed record MatchAdminCommand(MatchId MatchId, AdminAction Action, byte? SeatId) : WorkerCommand;
/// <summary>Public verification material only; private signing keys remain with Node.</summary>
public sealed record UpdateNodeSigningKey(string KeyId, string PublicKey) : WorkerCommand;
/// <summary>First message on a new local connection. Transport must verify and consume StartupToken before accepting commands.</summary>
public sealed record WorkerContentIdentity(string ContentVersion, string ContentHash, string BuildVersion, byte ProtocolVersion)
{
    public void Validate()
    {
        ContractGuard.Text(ContentVersion, 128); ContractGuard.Text(ContentHash, 128); ContractGuard.Text(BuildVersion, 128);
        if (ProtocolVersion == 0) throw new ArgumentException("Protocol version is required.");
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
public sealed record WorkerDraining(WorkerId WorkerId, Guid WorkerIncarnation) : WorkerEvent;
public sealed record WorkerFault(WorkerId WorkerId, Guid WorkerIncarnation, string Reason) : WorkerEvent;

public sealed record WorkerLaneHealth(int LaneId, int Matches, long Ticks, long CatchUpTicks, long DroppedTicks,
    double P50Milliseconds, double P95Milliseconds, double P99Milliseconds, double MaxMilliseconds);
public sealed record WorkerDiagnostics(ImmutableArray<WorkerLaneHealth> Lanes, double CpuPercent, long ManagedHeapBytes,
    int Gen0Collections, int Gen1Collections, int Gen2Collections, long PacketsReceived, long PacketsSent,
    long BytesReceived, long BytesSent, long QueueDrops, long PacketsRejected, ImmutableArray<WorkerMatchHealth> Matches = default, int TotalMatches = 0, bool MatchesTruncated = false)
{
    public void Validate()
    {
        if (Lanes.IsDefault || Lanes.Length is < 1 or > 64 || !double.IsFinite(CpuPercent) || CpuPercent is < 0 or > 100
            || ManagedHeapBytes < 0 || Gen0Collections < 0 || Gen1Collections < 0 || Gen2Collections < 0
            || PacketsReceived < 0 || PacketsSent < 0 || BytesReceived < 0 || BytesSent < 0 || QueueDrops < 0 || PacketsRejected < 0)
            throw new ArgumentException("Invalid worker diagnostics.");
        if (!Matches.IsDefault)
        {
            if (Matches.Length > 128 || TotalMatches < Matches.Length || TotalMatches > 1024
                || MatchesTruncated != (TotalMatches > Matches.Length)) throw new ArgumentException("Invalid match diagnostic sample.");
            var matchIds = new HashSet<MatchId>();
            foreach (var match in Matches)
            {
                if (match is null || !matchIds.Add(match.MatchId) || match.WireMatchId.Value == 0)
                    throw new ArgumentException("Invalid match diagnostics.");
                ContractGuard.Id(match.MatchId.Value); ContractGuard.Text(match.Phase, 32); ContractGuard.Text(match.State, 32);
            }
        }
        var ids = new HashSet<int>();
        foreach (var lane in Lanes)
            if (lane is null || lane.LaneId < 0 || !ids.Add(lane.LaneId) || lane.Matches is < 0 or > 1024
                || lane.Ticks < 0 || lane.CatchUpTicks < 0 || lane.DroppedTicks < 0
                || !double.IsFinite(lane.P50Milliseconds) || !double.IsFinite(lane.P95Milliseconds)
                || !double.IsFinite(lane.P99Milliseconds) || !double.IsFinite(lane.MaxMilliseconds)
                || lane.P50Milliseconds < 0 || lane.P95Milliseconds < lane.P50Milliseconds
                || lane.P99Milliseconds < lane.P95Milliseconds || lane.MaxMilliseconds < lane.P99Milliseconds)
                throw new ArgumentException("Invalid lane diagnostics.");
    }
}
public sealed record MatchAdminResult(MatchId MatchId, AdminAction Action, bool Applied, string? Code, string? Message) : WorkerEvent;

public sealed record WorkerMatchHealth(MatchId MatchId, WireMatchId WireMatchId, uint Tick, string Phase, string State);
