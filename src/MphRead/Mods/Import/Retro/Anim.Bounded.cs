using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MphRead.Mods.Import.Retro;

public sealed partial class Anim
{
    public const int MaximumResourceBytes = 16 * 1024 * 1024;
    public const int MaximumKeys = 20000, MaximumChannels = 256, MaximumScalars = 4_000_000;

    public const long MaximumDecodeWork = (MaximumChannels + 1L) * MaximumKeys;

    // Admission for complete imported animation resources. Reuses the existing
    // decoder unchanged after bounding every source-driven allocation and loop.
    public static Anim LoadBounded(byte[] source, bool echoes, long maximumScalars = MaximumScalars)
        => LoadBounded(source, echoes, maximumScalars, MaximumDecodeWork, out _);
    internal static Anim LoadBounded(byte[] source, bool echoes, long maximumScalars, long maximumWork, out long decodeWork)
    {
        ArgumentNullException.ThrowIfNull(source);
        Require(source.Length is >= 4 and <= MaximumResourceBytes, "ANIM byte budget exceeded.");
        source = source.ToArray(); // Preflight and decoder consume one private byte snapshot.
        var admission = Preflight(source, echoes); decodeWork = admission.Work;
        Require(maximumScalars is >= 0 and <= MaximumScalars && admission.Scalars <= maximumScalars,
            "ANIM remaining decoded scalar budget exceeded before decode.");
        Require(maximumWork is >= 0 and <= MaximumDecodeWork && decodeWork <= maximumWork,
            "ANIM remaining decoded work budget exceeded before decode.");
        Anim result;
        try { result = Load(source, echoes); }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException or KeyNotFoundException)
        { throw new InvalidDataException("Invalid bounded ANIM resource.", e); }
        long scalars = 0;
        foreach (var (table, width) in new[] { (result.Rot, 4), (result.Trans, 3), (result.Scale, 3) })
        {
            Require(table.Count <= MaximumChannels, "ANIM channel budget exceeded.");
            scalars += (long)table.Count * width * result.Keys;
            foreach (var keys in table.Values)
                Require(keys.Count == result.Keys && keys.All(k => k.Length == width && k.All(x => double.IsFinite(x) && Math.Abs(x) <= 100000)),
                    "ANIM full channels contain invalid values or key extents.");
        }
        Require(scalars <= MaximumScalars, "ANIM decoded scalar budget exceeded.");
        Require(result.Rot.Values.All(keys => keys.All(q => Math.Abs(q.Sum(x => x * x) - 1) <= .001)),
            "ANIM rotations must be finite unit quaternions within source quantization tolerance.");
        return result;
    }
    internal static (long Scalars, long Work) MeasureBounded(byte[] source, bool echoes)
    {
        Require(source.Length is >= 4 and <= MaximumResourceBytes, "ANIM byte budget exceeded.");
        return Preflight(source, echoes);
    }
    private static (long Scalars, long Work) Preflight(byte[] bytes, bool echoes)
    {
        int at = 0;
        void Span(long size)
        {
            Require(size >= 0 && size <= bytes.Length - (long)at, "ANIM encoded extent exceeds its resource.");
            at = checked(at + (int)size);
        }
        uint Word() { int p = at; Span(4); return BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(p, 4)); }
        ushort Half() { int p = at; Span(2); return BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(p, 2)); }
        byte Byte() { int p = at; Span(1); return bytes[p]; }
        double Float() => BitConverter.Int32BitsToSingle(unchecked((int)Word()));
        int Count(int maximum) { uint value = Word(); Require(value <= maximum, "ANIM declared count exceeds its allocation budget."); return (int)value; }
        static void Timing(double duration, double tick, int keys)
            => Require(double.IsFinite(duration) && duration is > 0 and <= 120 && double.IsFinite(tick) && tick > 0
                && keys is >= 1 and <= MaximumKeys, "ANIM timing exceeds its supported bound.");
        uint kind = Word();
        if (kind == 2)
        {
            Span(4); if (!echoes) Span(4); Span(echoes ? 2 : 4);
            double duration = Float(), tick = Float(); Span(8);
            uint divisor = Word(); double translation = Float(), scale = echoes ? Float() : 1;
            Require(double.IsFinite(translation) && double.IsFinite(scale), "ANIM quantization factors must be finite.");
            int channels = Count(MaximumChannels); Span(4); int keys = Count(MaximumKeys); Timing(duration, tick, keys);
            int flags = at; Span(((keys + 31L) / 32) * 4);
            int present = 0;
            for (int i = 1; i < keys; i++)
                if ((BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(flags + i / 32 * 4, 4)) & (1u << (i % 32))) != 0) present++;
            Span(echoes ? 4 : 8);
            long width = 0, bitsPerPresent = 0; bool rotates = false;
            var bones = new HashSet<uint>();
            for (int i = 0; i < channels; i++)
            {
                uint bone = echoes ? Byte() : Word(); Require(bones.Add(bone), "ANIM repeats a source channel bone.");
                for (int channel = 0; channel < (echoes ? 3 : 2); channel++)
                {
                    if (Half() == 0) continue;
                    width += channel == 0 ? 4 : 3;
                    if (channel == 0) { rotates = true; bitsPerPresent++; }
                    for (int axis = 0; axis < 3; axis++) { Span(2); byte bits = Byte(); Require(bits <= 32, "ANIM signed delta width is unsupported."); bitsPerPresent += bits; }
                }
            }
            Require(!rotates || divisor != 0, "ANIM rotation divisor is zero.");
            Require(width * keys <= MaximumScalars, "ANIM full-key scalar budget exceeded before decode.");
            Span(((bitsPerPresent * present + 31) / 32) * 4);
            return (width * keys, (channels + 1L) * keys);
        }
        else if (kind == 0)
        {
            double duration = Float(); Span(4); double tick = Float(); Span(4);
            int keys = Count(MaximumKeys); Span(4); Timing(duration, tick, keys);
            int boneCount = Count(MaximumChannels), boneStart = at; Span(boneCount);
            var rotations = new List<byte>();
            List<byte> Indices()
            {
                int count = Count(MaximumChannels); var result = new List<byte>(count);
                for (int i = 0; i < count; i++) result.Add(Byte());
                return result;
            }
            if (echoes) rotations = Indices();
            else for (int i = 0; i < boneCount; i++) if (bytes[boneStart + i] != 255) rotations.Add(bytes[boneStart + i]);
            var translations = Indices(); var scales = echoes ? Indices() : new List<byte>();
            int nr = rotations.Count(i => i != 255), nt = translations.Count(i => i != 255), ns = scales.Count(i => i != 255);
            Require(rotations.All(i => i == 255 || i < nr) && translations.All(i => i == 255 || i < nt) && scales.All(i => i == 255 || i < ns),
                "ANIM channel index exceeds its encoded table.");
            Require((long)keys * (nr * 4 + nt * 3 + ns * 3) <= MaximumScalars, "ANIM full-key scalar budget exceeded before decode.");
            if (echoes) { Span(4); Span((long)ns * keys * 12); }
            Span(4); Span((long)nr * keys * 16); Span(4); Span((long)nt * keys * 12);
            return ((long)keys * (nr * 4 + nt * 3 + ns * 3), (nr + nt + ns + 1L) * keys);
        }
        else throw new InvalidDataException("ANIM encoding is unsupported.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
