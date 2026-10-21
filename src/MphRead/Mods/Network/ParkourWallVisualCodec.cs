using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

// Protocol85 presentation nibbles. Mechanical grapple bytes remain strict and
// unchanged; the enclosing native state owns this separate cosmetic channel.
public static class ParkourWallVisualCodec
{
    public static bool Valid(byte cue) => cue <= 12;
    public static byte PackIntent(byte mantle, byte wall)
    {
        if (mantle > 15 || !Valid(wall)) throw new ArgumentException("Invalid Parkour visual cue.");
        return (byte)(mantle | wall << 4);
    }
    internal static byte Encode(int kind, Vector3 normal)
    {
        if (kind is < 1 or > 3 || !float.IsFinite(normal.LengthSquared) || normal.Xz.LengthSquared < .001f) return 0;
        int direction = MathF.Abs(normal.X) >= MathF.Abs(normal.Z) ? (normal.X >= 0 ? 0 : 1) : (normal.Z >= 0 ? 2 : 3);
        return (byte)(1 + (kind - 1) * 4 + direction);
    }
    internal static int Kind(byte cue) => cue is >= 1 and <= 12 ? (cue - 1) / 4 + 1 : 0;
    internal static Vector3 Normal(byte cue) => cue is >= 1 and <= 12 ? ((cue - 1) % 4) switch
    { 0 => Vector3.UnitX, 1 => -Vector3.UnitX, 2 => Vector3.UnitZ, _ => -Vector3.UnitZ } : Vector3.Zero;
    public static bool TryWriteGrapple(in PlayerGrappleState grapple, byte wall, Span<byte> destination, out int written)
    {
        written = 0;
        if (!Valid(wall) || !grapple.TryWrite(destination, out written)) return false;
        destination[0] |= (byte)(wall << 4);
        return true;
    }
    public static bool TryReadGrapple(ReadOnlySpan<byte> source, out PlayerGrappleState grapple, out byte wall)
    {
        grapple = default; wall = 0;
        if (source.Length != PlayerGrappleState.Size || (source[0] & 14) != 0 || !Valid((byte)(source[0] >> 4))) return false;
        Span<byte> mechanical = stackalloc byte[PlayerGrappleState.Size];
        source.CopyTo(mechanical); mechanical[0] &= 1;
        if (!PlayerGrappleState.TryRead(mechanical, out grapple)) return false;
        wall = (byte)(source[0] >> 4); return true;
    }
}
