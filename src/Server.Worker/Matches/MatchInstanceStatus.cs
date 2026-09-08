using System;
using MphRead.Identity;
using MphRead.Telemetry;

namespace MphRead.Mods.Network;

public enum MatchInstanceState { Created, Running, Completed, Stopped, Failed, Disposed }
public enum MatchStopReason { Requested, HostShutdown, Replaced, AdmissionTimeout }
public sealed record MatchInstanceStatus(Guid MatchId, uint WireMatchId, MatchInstanceState State,
    uint Tick, MatchPhase Phase, int PlayerCount, string? Error);
public sealed record MatchCompletion(Guid MatchId, uint WireMatchId, uint Tick, MatchResult? Result,
    MatchReportV1? Report, MatchTelemetry? Telemetry, MatchStopReason? StopReason);
public sealed record MatchFailure(Guid MatchId, uint WireMatchId, uint Tick, Exception Error);
