using OpenTK.Mathematics;

namespace MphRead.Hud.Radar;

public enum RadarContactType { Enemy, Teammate, Objective, PrimeHunter }
public enum RadarObjective { None, Flag, Base, Node, Defender }
public enum RadarElevation { Same, Above, Below }

/// <summary>A contact already admitted by the mode's authoritative reveal policy.</summary>
public readonly record struct RadarContact(RadarContactType Type, Vector3 Position,
    int Team, RadarObjective Objective, float Visibility, uint AgeTicks = 0);

public readonly record struct RadarPoint(RadarContact Contact, Vector2 RelativePosition,
    RadarElevation Elevation, bool Clamped);
