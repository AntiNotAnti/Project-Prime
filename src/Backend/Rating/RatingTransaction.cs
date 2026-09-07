using System.Collections.Immutable;

namespace MphRead.Backend.Rating;

public enum RatingPairResult
{
    Tie,
    Win,
    Loss
}

/// <summary>A replayable pair result using both participants' transaction-frozen inputs.</summary>
public sealed record RatingPairContribution(
    Guid OpponentPlayerId,
    int OpponentPointsBefore,
    int OpponentTierBefore,
    RatingPairResult Result,
    int Delta);

/// <summary>Immutable output for one participant. AppliedDelta can differ from NormalizedDelta only at a bound.</summary>
public sealed record RatingTransaction(
    Guid PlayerId,
    int PointsBefore,
    int TierBefore,
    ImmutableArray<RatingPairContribution> PairContributions,
    int OpponentCount,
    int RawDelta,
    int NormalizedDelta,
    int AppliedDelta,
    int PointsAfter,
    int TierAfter,
    RatingPolicyVersion PolicyVersion);
