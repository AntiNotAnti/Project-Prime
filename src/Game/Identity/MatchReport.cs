using System;
using System.Linq;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MphRead.Identity;

public enum MatchTrustClass { Community = 0, Private = 1, VerifiedCasual = 2, Ranked = 3, Tournament = 4, Practice = 5 }
public enum ParticipantKind { Guest, RegisteredHuman, Bot }
public enum ParticipantExitReason { Disconnected, ExplicitLeave, Timeout, Backpressure, Replaced, Completed, ServerStopped }
public enum ParticipantOutcome { Finished, Departed, Forfeited }

/// <summary>Measured facts only. Null optional telemetry is unavailable, not zero.</summary>
public sealed record MatchReportMetrics(int Standing, int TeamStanding, int Points, int Kills, int Deaths,
    int Assists, int DamageDealt, int LongestKillStreak, int HeadshotKills, int Suicides, int FriendlyKills,
    int OctolithScores, int OctolithDrops, int OctolithStops, int NodesCaptured, int NodesLost,
    int KillsAsPrime, int PrimesKilled, float ModeTimeSeconds, int BeamDamageMax, int BeamDamageDealt,
    int DamageCount, int AltDamageCount, int KillStreak, ImmutableArray<int> BeamKills,
    int? BipedKills = null, int? AltFormKills = null, int? Shots = null, int? Hits = null);

public readonly record struct MatchParticipationSpan(uint JoinedTick, uint? LeftTick, ParticipantExitReason? ExitReason,
    byte Slot, Hunter Hunter, int TeamIndex, uint PlayedTicks = 0);

public sealed record MatchReportParticipant(Guid ParticipantId, PlayerId? PlayerId, ParticipantKind Kind,
    string DisplayName, bool StartedMatch, ParticipantOutcome Outcome, uint PlayedTicks,
    ImmutableArray<MatchParticipationSpan> Spans, MatchReportMetrics Metrics);

/// <summary>Immutable database-free authority envelope. UUID is distinct from WireMatchId.
/// TrustClass is a claim which Backend must validate against the authenticated reporter.</summary>
public sealed record MatchReportV1(int SchemaVersion, Guid MatchId, uint WireMatchId, Guid ServerId,
    Guid ServerIncarnation, string BuildVersion, int ProtocolVersion, string? ContentHash,
    MatchTrustClass TrustClass, string Ruleset, string? Variant, MatchRules Rules,
    DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc, uint PlayedTicks, MatchEndReason EndReason,
    ImmutableArray<MatchReportParticipant> Participants, string? TournamentId = null, string? RoundId = null, Guid? ReplayId = null)
{
    public const int CurrentSchema = 1;
    public const int MaximumParticipants = 256;
    [JsonIgnore]
    public bool IsValid => SchemaVersion == CurrentSchema && MatchId != Guid.Empty && ServerId != Guid.Empty
        && ServerIncarnation != Guid.Empty && WireMatchId != 0 && ProtocolVersion > 0
        && BuildVersion is { Length: > 0 and <= 128 } && Ruleset is { Length: > 0 and <= 64 }
        && Variant?.Length is not > 64 && ContentHash?.Length is not > 128
        && (TournamentId == null || ValidExternalId(TournamentId)) && (RoundId == null || ValidExternalId(RoundId))
        && (TournamentId == null) == (RoundId == null) && ReplayId != Guid.Empty
        && Rules != null && StartedAtUtc.Offset == TimeSpan.Zero && EndedAtUtc.Offset == TimeSpan.Zero
        && EndedAtUtc >= StartedAtUtc && Enum.IsDefined(TrustClass) && Enum.IsDefined(EndReason)
        && !Participants.IsDefault && Participants.Length is > 0 and <= MaximumParticipants && ValidateParticipants();
    public static bool ValidExternalId(string value) => value.Length is > 0 and <= 64
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    private bool ValidateParticipants()
    {
        var ids = new HashSet<Guid>();
        var accounts = new HashSet<PlayerId>();
        foreach (MatchReportParticipant p in Participants)
        {
            if (p == null || p.ParticipantId == Guid.Empty || !ids.Add(p.ParticipantId)
                || !Enum.IsDefined(p.Kind) || !Enum.IsDefined(p.Outcome) || p.DisplayName is not { Length: > 0 and <= 64 }
                || p.PlayerId is { IsEmpty: true } || p.Kind == ParticipantKind.RegisteredHuman && !p.PlayerId.HasValue
                || p.Kind != ParticipantKind.RegisteredHuman && p.PlayerId.HasValue
                || p.PlayerId is { } account && !accounts.Add(account)
                || p.Spans.IsDefaultOrEmpty || p.Spans.Length > 256 || p.Metrics == null) return false;
            ulong played = 0;
            foreach (var span in p.Spans)
            {
                played += span.PlayedTicks;
                if (span.Slot >= 8 || span.TeamIndex is < 0 or > 7 || !Enum.IsDefined(span.Hunter)
                    || !span.LeftTick.HasValue || !span.ExitReason.HasValue || !Enum.IsDefined(span.ExitReason.Value)
                    || span.PlayedTicks > unchecked(span.LeftTick.Value - span.JoinedTick)) return false;
            }
            if (played != p.PlayedTicks) return false;
            var m = p.Metrics;
            if (m.BeamKills.IsDefault || m.BeamKills.Length != 9 || !float.IsFinite(m.ModeTimeSeconds)
                || m.Kills < 0 || m.Deaths < 0 || m.Assists < 0 || m.DamageDealt < 0
                || m.HeadshotKills < 0 || m.LongestKillStreak < 0 || m.BipedKills < 0 || m.AltFormKills < 0
                || m.Shots < 0 || m.Hits < 0) return false;
            foreach (int kills in m.BeamKills) if (kills < 0) return false;
        }
        return true;
    }
}
