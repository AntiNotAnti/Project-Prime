using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace MphRead.Mods.Import.Retro;

/// <summary>Bounded big-endian local model primitives, based on Second Hunt's MIT Be reader.</summary>
public static class Be
{
    public static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o, 4));
    public static int I32(byte[] b, int o) => BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(o, 4));
    public static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o, 2));
    public static short I16(byte[] b, int o) => BinaryPrimitives.ReadInt16BigEndian(b.AsSpan(o, 2));
    public static float F32(byte[] b, int o) => BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(o, 4));

    public static int IndexOf(byte[] b, ReadOnlySpan<byte> needle, int start)
    {
        if (start < 0 || start > b.Length) return -1;
        int result = b.AsSpan(start).IndexOf(needle);
        return result < 0 ? -1 : start + result;
    }
    public static int Index(byte[] b, ReadOnlySpan<byte> needle, int start)
    {
        int at = IndexOf(b, needle, start);
        if (at < 0) throw new InvalidDataException("Retro subsection is missing.");
        return at;
    }
    public static string CString(byte[] b, int at, out int end)
    {
        end = Index(b, new byte[]{0}, at);
        return Encoding.Latin1.GetString(b, at, end - at);
    }
    public static byte[] Slice(byte[] b, int start, int n)
    {
        if (start < 0 || start > b.Length || n < 0)
            throw new InvalidDataException("Retro resource slice outside source.");
        int m = Math.Min(n, b.Length - start);
        return b.AsSpan(start, m).ToArray();
    }
}
