using OpenTK.Mathematics;

namespace MphRead.Hud.Radar;

public enum RadarContactType { Enemy, Teammate, Objective, PrimeHunter, Resource }
public enum RadarObjective { None, Flag, Base, Node, Defender }
public enum RadarResource
{
    None,
    Weapon,
    Ammo,
    Health,
    Powerup,
    AffinityWeapon,
    OmegaCannon
}
public enum RadarElevation { Same, Above, Below }
public enum RadarMarkerShape { Diamond, Square, Triangle, DoubleDiamond }

/// <summary>A contact already admitted by the mode's authoritative reveal policy.</summary>
public readonly record struct RadarContact(RadarContactType Type, Vector3 Position,
    int Team, RadarObjective Objective, float Visibility, uint AgeTicks = 0,
    bool AffinityEmphasis = false, RadarResource Resource = RadarResource.None,
    uint RespawnTicks = 0);

public readonly record struct RadarPoint(RadarContact Contact, Vector2 RelativePosition,
    RadarElevation Elevation, bool Clamped);
