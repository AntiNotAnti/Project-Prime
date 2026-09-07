using System.Collections.Immutable;
using MphRead.Identity;

namespace MphRead.Backend.Rating;

public enum RatingIneligibilityReason
{
    None,
    LegacyReport,
    UnsupportedReportSchema,
    CommunityServer,
    PrivateServer,
    TournamentRatingDisabled,
    PracticeServer,
    UnsupportedTrustClass,
    NonOfficialRules,
    IncompleteMatch,
    BotParticipant,
    GuestParticipant,
    InvalidRoster,
    InvalidParticipant,
    InvalidPoints,
    InvalidStandings,
    NoOpposingPair
}

public readonly record struct RatingEligibility(bool IsEligible, RatingIneligibilityReason Reason)
{
    public static RatingEligibility Eligible => new(true, RatingIneligibilityReason.None);
    public static RatingEligibility Ineligible(RatingIneligibilityReason reason) => new(false, reason);
}

/// <summary>
/// Frozen rating input. Participants may include spectators, but only StartedMatch entries form the
/// official roster. Callers must read PointsBefore for that roster together in their database transaction.
/// </summary>
public sealed record RatingMatchInput(
    int ReportSchemaVersion,
    MatchTrustClass TrustClass,
    RankingEligibility RankingEligibility,
    MatchEndReason EndReason,
    bool Teams,
    ImmutableArray<RatingParticipantInput> Participants);

public sealed record RatingParticipantInput(
    Guid? PlayerId,
    ParticipantKind Kind,
    bool StartedMatch,
    ParticipantOutcome Outcome,
    int PointsBefore,
    int Standing,
    int TeamStanding,
    int? TeamId = null);

public sealed record RatingCalculation(
    RatingPolicyVersion PolicyVersion,
    RatingEligibility Eligibility,
    ImmutableArray<RatingTransaction> Transactions)
{
    public bool IsEligible => Eligibility.IsEligible;
}

public abstract class RatingPolicy
{
    // PairwiseNormalizedV1 consumes the current immutable MatchReportV1 contract.
    // Frozen contract for PairwiseNormalizedV1. A future report schema requires a new
    // policy/version decision and must not reinterpret this persisted ledger.
    public const int CurrentReportSchemaVersion = 2;
    public abstract RatingPolicyVersion Version { get; }
    public abstract RatingCalculation Calculate(RatingMatchInput input);

    protected RatingCalculation Ineligible(RatingIneligibilityReason reason)
        => new(Version, RatingEligibility.Ineligible(reason), []);
}
