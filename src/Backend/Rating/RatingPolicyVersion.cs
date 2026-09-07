namespace MphRead.Backend.Rating;

/// <summary>Persisted with every rating calculation so historical results remain replayable.</summary>
public enum RatingPolicyVersion
{
    PairwiseNormalizedV1 = 1
}
