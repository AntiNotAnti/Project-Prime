using System;
using OpenTK.Mathematics;

namespace MphRead.Hud.Radar;

/// <summary>Fixed-capacity prepared HUD input. Draw code has no world access.</summary>
public sealed class RadarFrame
{
    public const int Capacity = 64;
    private readonly RadarContact[] _contacts = new RadarContact[Capacity];
    private int _count;
    public Vector3 Origin { get; private set; }
    public Vector3 Facing { get; private set; }
    public ulong Tick { get; private set; }
    public int Dropped { get; private set; }
    public ReadOnlySpan<RadarContact> Contacts => _contacts.AsSpan(0, _count);

    public void Begin(Vector3 origin, Vector3 facing, ulong tick)
    {
        Origin = Finite(origin) ? origin : Vector3.Zero;
        Facing = Finite(facing) ? facing : -Vector3.UnitZ;
        Tick = tick;
        _count = Dropped = 0;
    }

    public bool AddApproved(in RadarContact contact)
    {
        if (!float.IsFinite(contact.Position.X) || !float.IsFinite(contact.Position.Y)
            || !float.IsFinite(contact.Position.Z) || !float.IsFinite(contact.Visibility)
            || contact.Visibility <= 0) return false;
        if (_count == Capacity) { Dropped++; return false; }
        _contacts[_count++] = contact with { Visibility = Math.Clamp(contact.Visibility, 0, 1) };
        return true;
    }

    private static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
