using System.Numerics;

namespace MphRead.CorpsePhysics;

public enum CorpseQuality { Off, Low, Medium, High }
public readonly record struct CorpseContext(ulong Scene, ushort Match, ulong Epoch, byte Slot,
    ushort Generation, uint Life, long Timeline)
{
    public bool IsValid => Scene != 0 && Slot < 8 && Life != 0
        && (Match == 0 ? Epoch == 0 && Generation == 0 : Epoch != 0 && Generation != 0);
}
// Opaque accepted-event digest supplied by the presentation owner. The backend
// never authenticates a shot, changes a death or sends this onto a game wire.
public readonly record struct CorpseEvent(ulong A, ulong B, ulong C, ulong D)
{ public bool IsValid => (A | B | C | D) != 0; }
public readonly record struct CorpseKey(CorpseContext Context, CorpseEvent Event)
{ public bool IsValid => Context.IsValid && Event.IsValid; }
public readonly record struct CorpseHandle(ulong World, byte Slot, uint Generation)
{ public bool IsValid => World != 0 && Slot < 8 && Generation != 0; }
public readonly record struct CorpsePose(Vector3 Position, Quaternion Orientation)
{
    public bool IsValid => Limits.Position(Position) && Limits.UnitQuaternion(Orientation);
    public Matrix4x4 Matrix => Matrix4x4.CreateFromQuaternion(Orientation) * Matrix4x4.CreateTranslation(Position);
    public static CorpsePose Interpolate(CorpsePose before, CorpsePose after, float alpha)
        => new(Vector3.Lerp(before.Position, after.Position, alpha), Quaternion.Slerp(before.Orientation, after.Orientation, alpha));
}
public readonly record struct CorpsePart(CorpsePose Rest, Vector3 Size, float Mass, int Parent,
    Vector3 Joint, float Swing, float Twist)
{
    // Common rest-space joint frame for both connected bodies. Local Z is
    // the twist axis; identity retains the original primitive pilot policy.
    public Quaternion JointBasis { get; init; } = Quaternion.Identity;
}

public sealed class CorpseRig
{
    public const int PartCount = 15;
    private readonly CorpsePart[] _parts;
    public string Name { get; }
    public ReadOnlySpan<CorpsePart> Parts => _parts;
    public CorpseRig(string name, ReadOnlySpan<CorpsePart> parts)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || parts.Length != PartCount)
            throw new ArgumentException("A named, fifteen-part reference rig is required.");
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (!p.Rest.IsValid || !Limits.Finite(p.Size) || p.Size.X is < .03f or > 2
                || p.Size.Y is < .03f or > 2 || p.Size.Z is < .03f or > 2
                || !float.IsFinite(p.Mass) || p.Mass is < .5f or > 4
                || p.Parent < -1 || p.Parent >= i || (i == 0 ? p.Parent != -1 : p.Parent < 0)
                || !Limits.Position(p.Joint) || !Limits.UnitQuaternion(p.JointBasis)
                || !float.IsFinite(p.Swing) || p.Swing is < .05f or > 1.5f
                || !float.IsFinite(p.Twist) || p.Twist is < .01f or > .8f
                || p.Rest.Position.LengthSquared() > 16 || p.Joint.LengthSquared() > 16
                || (p.Joint - p.Rest.Position).LengthSquared() > 1
                || p.Parent >= 0 && (p.Joint - parts[p.Parent].Rest.Position).LengthSquared() > 2.25f)
                throw new ArgumentException("Invalid finite part, tree, mass, size or joint limit.");
        }
        Name = name; _parts = parts.ToArray();
        for (int i = 0; i < _parts.Length; i++)
            _parts[i] = _parts[i] with { Rest = _parts[i].Rest with { Orientation = Quaternion.Normalize(_parts[i].Rest.Orientation) },
                JointBasis = Quaternion.Normalize(_parts[i].JointBasis) };
    }
}
// Right-handed, counterclockwise when viewed from the collidable outside.
public readonly record struct StaticTriangle(Vector3 A, Vector3 B, Vector3 C);
public sealed class CorpseStaticWorld
{
    public const int MaximumTriangles = 128;
    private readonly StaticTriangle[] _triangles;
    public ReadOnlySpan<StaticTriangle> Triangles => _triangles;
    public CorpseStaticWorld(ReadOnlySpan<StaticTriangle> triangles)
    {
        if (triangles.Length is < 1 or > MaximumTriangles) throw new ArgumentException("Static triangle budget exceeded.");
        foreach (var t in triangles)
            if (!Limits.Position(t.A) || !Limits.Position(t.B) || !Limits.Position(t.C)
                || Vector3.Cross(t.B - t.A, t.C - t.A).LengthSquared() < .000001f
                || (t.A - t.B).LengthSquared() > 4096 || (t.B - t.C).LengthSquared() > 4096 || (t.C - t.A).LengthSquared() > 4096)
                throw new ArgumentException("Invalid, degenerate or oversized collision triangle.");
        _triangles = triangles.ToArray();
    }
}
public sealed class CorpseSpawnDescriptor
{
    public CorpseKey Key { get; }
    public CorpseRig Rig { get; }
    public CorpsePose Origin { get; }
    public Vector3 AcceptedDirection { get; }
    public Vector3 InheritedVelocity { get; }
    public Vector3? ImpactPoint { get; }
    public uint Seed { get; }
    public uint SpawnTick { get; }
    public uint Lifetime { get; }
    public CorpseSpawnDescriptor(CorpseKey key, CorpseRig rig, CorpsePose origin,
        Vector3 acceptedDirection, Vector3 inheritedVelocity, Vector3? impactPoint, uint seed, uint spawnTick, uint lifetime = 180)
    {
        ArgumentNullException.ThrowIfNull(rig);
        if (!key.IsValid || !origin.IsValid || !Limits.Finite(acceptedDirection) || !Limits.Finite(inheritedVelocity)
            || inheritedVelocity.LengthSquared() > 16 || lifetime is < 1 or > 240
            || impactPoint is {} point && (!Limits.Position(point) || (point - origin.Position).LengthSquared() > 16))
            throw new ArgumentException("Invalid corpse presentation descriptor.");
        float length = acceptedDirection.LengthSquared();
        if (!float.IsFinite(length)) throw new ArgumentException("Nonfinite accepted direction.");
        Key = key; Rig = rig; Origin = origin with { Orientation = Quaternion.Normalize(origin.Orientation) };
        AcceptedDirection = length > 1e-8f ? Vector3.Normalize(acceptedDirection) : Vector3.Zero;
        InheritedVelocity = inheritedVelocity; ImpactPoint = impactPoint; Seed = seed; SpawnTick = spawnTick; Lifetime = lifetime;
    }
    public CorpsePose WorldPart(int i)
    {
        var p = Rig.Parts[i].Rest;
        return new(Origin.Position + Vector3.Transform(p.Position, Origin.Orientation),
            Quaternion.Normalize(Origin.Orientation * p.Orientation));
    }
}
public readonly record struct CorpseStatistics(int ActiveCorpses, int DynamicBodies, int JointConstraints,
    long ReservedPoolBytes, ulong FixedSteps, ulong Spawned, ulong Retired, ulong Refused, ulong Faults);
public interface IRagdollPresentationBackend : IDisposable
{
    string Name { get; }
    CorpseStatistics Statistics { get; }
    bool TrySpawn(CorpseSpawnDescriptor spawn, out CorpseHandle handle);
    bool Step(uint fixedTick);
    bool Sample(CorpseHandle handle, float alpha, Span<CorpsePose> destination);
    bool Retire(CorpseHandle handle);
    void Clear();
}
internal static class Limits
{
    internal static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    internal static bool Position(Vector3 v) => Finite(v) && MathF.Abs(v.X) <= 8192 && MathF.Abs(v.Y) <= 8192 && MathF.Abs(v.Z) <= 8192;
    internal static bool UnitQuaternion(Quaternion q) => float.IsFinite(q.LengthSquared()) && MathF.Abs(q.LengthSquared() - 1) < .001f;
    internal static float Alpha(float alpha) => float.IsFinite(alpha) ? Math.Clamp(alpha, 0, 1) : 0;
    internal static int Capacity(CorpseQuality quality) => quality switch { CorpseQuality.Low => 2, CorpseQuality.Medium => 4, CorpseQuality.High => 8, _ => 0 };
}
