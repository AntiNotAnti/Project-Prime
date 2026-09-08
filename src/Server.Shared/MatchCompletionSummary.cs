using System.Collections.Immutable;
using MphRead;
using MphRead.Identity;

namespace FruityPrime.Server.Shared;

/// <summary>Bounded presentation outcome, not a replacement for the authoritative report.
/// Points remain signed because game scoring may deduct points.</summary>
public sealed record PlayerOutcomeSummary(Guid ParticipantId, PlayerId? PlayerId, ParticipantKind Kind,
    string DisplayName, ParticipantOutcome Outcome, int Standing, int TeamStanding, int Points, int Kills, int Deaths)
{
    public void Validate()
    {
        ContractGuard.Id(ParticipantId); ContractGuard.Defined(Kind); ContractGuard.Defined(Outcome);
        ContractGuard.Text(DisplayName, 64);
        if (Kind == ParticipantKind.RegisteredHuman ? !PlayerId.HasValue || PlayerId.Value.IsEmpty : PlayerId.HasValue)
            throw new ArgumentException("Outcome identity does not match participant kind.");
        if (Standing is < 0 or > 8 || TeamStanding is < 0 or > 8 || Kills < 0 || Deaths < 0)
            throw new ArgumentException("Invalid outcome counters.");
    }
}

/// <summary>Immutable Node-facing completion payload. Artifact UUIDs are opaque references,
/// not paths or credentials. Runtime population and delivery belong to Worker/Node integration.</summary>
public sealed record MatchCompletionSummary(MatchId MatchId, LobbyId LobbyId, MatchEndReason EndReason,
    ImmutableArray<PlayerOutcomeSummary> Players, Guid? ReplayId, Guid? TelemetryId, Guid ReportId)
{
    public const int MaxOutcomes = 32;

    public void Validate()
    {
        ContractGuard.Id(MatchId.Value); ContractGuard.Id(LobbyId.Value); ContractGuard.Id(ReportId);
        ContractGuard.Defined(EndReason);
        if (ReplayId == Guid.Empty || TelemetryId == Guid.Empty) throw new ArgumentException("Invalid artifact reference.");
        if (Players.IsDefault || Players.Length > MaxOutcomes) throw new ArgumentException("Invalid completion outcome count.");
        var participants = new HashSet<Guid>(); var accounts = new HashSet<PlayerId>();
        foreach (var player in Players)
        {
            if (player is null) throw new ArgumentException("Null completion outcome.");
            player.Validate();
            if (!participants.Add(player.ParticipantId) || player.PlayerId is { } account && !accounts.Add(account))
                throw new ArgumentException("Duplicate completion participant.");
        }
    }
}
