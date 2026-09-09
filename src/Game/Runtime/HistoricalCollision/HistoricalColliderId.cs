namespace MphRead.Runtime.HistoricalCollision;

/// <summary>
/// Stable identity for a dynamic collision source. Entity list position is
/// deliberately not part of the identity: a reused entity id receives a new
/// generation before it can produce callbacks or side effects.
/// </summary>
public readonly record struct HistoricalColliderId(int EntityId, uint Generation)
{
    public bool IsValid => EntityId >= 0 && Generation != 0;

    public static HistoricalColliderId None => default;
}
