namespace MphRead.Runtime.HistoricalCollision;

/// <summary>
/// Collision sources that may be represented in a lag-compensated query.
/// Static room geometry is included as a source kind for diagnostics, but is
/// never copied into the dynamic history ring.
/// </summary>
public enum HistoricalColliderKind : byte
{
    None = 0,
    StaticRoom = 1,
    Door = 2,
    ForceField = 3,
    Object = 4,
    Platform = 5
}
