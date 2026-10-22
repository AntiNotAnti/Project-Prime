using System;
using System.Numerics;
namespace MphRead.Mods.Render.Hud;

public enum RadarContactKind { Hunter, Weapon, Powerup, Objective, ObjectiveBase, Node, PrimeHunter }
public enum RadarContactRelation { Neutral, Friendly, Enemy }
[Flags]
public enum RadarContactFlags { None = 0, OutOfRange = 1, Carried = 2, Revealed = 4, Important = 8 }
public readonly record struct RadarContact(RadarContactKind Kind, Vector3 Position, float Heading,
    int StableId, int TeamIndex, RadarContactRelation Relation, Vector4 Color, float Alpha = 1,
    RadarContactFlags Flags = RadarContactFlags.None);
public enum RadarElevation { Level, Above, Below }
public readonly record struct RadarProjectedContact(Vector2 Position, float Bearing, float DistanceFraction,
    RadarElevation Elevation, bool OutOfRange);
