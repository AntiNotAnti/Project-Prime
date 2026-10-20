using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;

namespace MphRead.Mods.Campaign;

/// <summary>Duration metadata only; does not admit animation evaluation, models or LUT indices.</summary>
public static class CampaignCreditsAnimationDuration
{
    public const int MaxInputBytes = 4 * 1024 * 1024, MaxSlots = 64, MaxFrames = 18000, MaxRecordsPerGroup = 4096;
    public sealed class Snapshot
    {
        public string Sha256 { get; }
        public int ByteLength { get; }
        public int SlotCount { get; }
        public ushort HeaderPadding { get; }
        public int NodeFrames { get; }
        public int MaterialFrames { get; }
        public int TexcoordFrames { get; }
        public int TextureFrames { get; }
        public int WidgetFrames => Math.Max(1, Math.Max(Math.Max(NodeFrames, MaterialFrames), Math.Max(TexcoordFrames, TextureFrames)));
        internal Snapshot(ReadOnlySpan<byte> bytes, int slots, int node, int material, int texcoord, int texture)
        {
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)); ByteLength = bytes.Length;
            SlotCount = slots; HeaderPadding = U16(bytes, 22); NodeFrames = node;
            MaterialFrames = material; TexcoordFrames = texcoord; TextureFrames = texture;
        }
    }

    public static Snapshot Read(ReadOnlySpan<byte> bytes)
    {
        Require(bytes.Length is >= 24 and <= MaxInputBytes, "Credits animation extent is unsupported.");
        int slots = U16(bytes, 20);
        Require(slots is >= 1 and <= MaxSlots, "Credits animation slot count is unsupported.");
        Span<int> first = stackalloc int[5];
        first.Clear();
        for (int channel = 0; channel < 5; channel++)
        {
            uint table = U32(bytes, channel * 4);
            Require(table >= 24, "Credits animation pointer table overlaps its header.");
            var pointers = Range(bytes, table, (ulong)slots * 4);
            for (int slot = 0; slot < slots; slot++)
            {
                uint offset = U32(pointers, slot * 4);
                if (channel == 1) { Require(offset == 0, "Credits unused animation channel is unsupported."); continue; }
                if (offset == 0) continue;
                Require(offset >= 24, "Credits animation group overlaps its header.");
                var group = Range(bytes, offset, channel is 0 or 2 ? 20UL : channel == 3 ? 28UL : 32UL);
                uint frames = channel == 4 ? U16(group, 0) : U32(group, 0);
                Require(frames <= MaxFrames, "Credits animation duration exceeds its bound.");
                if (slot == 0) first[channel] = (int)frames;
                if (channel == 0)
                {
                    uint animation = U32(group, 16);
                    OrderedLuts(bytes, U32(group, 4), U32(group, 8), U32(group, 12), animation);
                    Require(animation <= offset && (offset - animation) % 48 == 0,
                        "Credits node animation records have an invalid extent.");
                    Records(bytes, animation, (offset - animation) / 48, 48);
                }
                else if (channel == 2)
                {
                    uint color = U32(group, 4), animation = U32(group, 12);
                    Require(color <= animation, "Credits material color span is reversed.");
                    Range(bytes, color, (ulong)animation - color);
                    Records(bytes, animation, U32(group, 8), 140);
                }
                else if (channel == 3)
                {
                    uint animation = U32(group, 20);
                    OrderedLuts(bytes, U32(group, 4), U32(group, 8), U32(group, 12), animation);
                    Records(bytes, animation, U32(group, 16), 60);
                }
                else
                {
                    Range(bytes, U32(group, 12), (ulong)U16(group, 2) * 2);
                    Range(bytes, U32(group, 16), (ulong)U16(group, 4) * 2);
                    Range(bytes, U32(group, 20), (ulong)U16(group, 6) * 2);
                    Records(bytes, U32(group, 24), U16(group, 8), 44);
                }
            }
        }
        return new(bytes, slots, first[0], first[2], first[3], first[4]);
    }
    private static void OrderedLuts(ReadOnlySpan<byte> bytes, uint scale, uint rotate, uint translate, uint animation)
    {
        Require(scale <= rotate && rotate <= translate && translate <= animation
            && (rotate - scale) % 4 == 0 && (translate - rotate) % 2 == 0 && (animation - translate) % 4 == 0,
            "Credits animation LUT ranges are reversed or misaligned.");
        Range(bytes, scale, (ulong)animation - scale);
    }
    private static void Records(ReadOnlySpan<byte> bytes, uint offset, uint count, int size)
    {
        Require(count <= MaxRecordsPerGroup, "Credits animation record count exceeds its bound.");
        Range(bytes, offset, (ulong)count * (uint)size);
    }
    private static ReadOnlySpan<byte> Range(ReadOnlySpan<byte> bytes, uint offset, ulong length)
    {
        Require((ulong)offset + length <= (ulong)bytes.Length, "Credits animation range is outside its input.");
        return bytes.Slice((int)offset, (int)length);
    }
    private static ushort U16(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(at, 2));
    private static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(at, 4));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}

/// <summary>Detached widget durations bound to the exact admitted frontend documents.</summary>
public sealed class CampaignCreditsFrameTable
{
    public string MarmSha256 { get; }
    public string TextSha256 { get; }
    public IReadOnlyDictionary<int, int> Frames { get; }
    public IReadOnlyDictionary<string, CampaignCreditsAnimationDuration.Snapshot> Animations { get; }
    private CampaignCreditsFrameTable(CampaignCreditsSource.Document document, Dictionary<int, int> frames,
        Dictionary<string, CampaignCreditsAnimationDuration.Snapshot> animations)
    {
        MarmSha256 = document.MarmSha256; TextSha256 = document.TextSha256;
        Frames = new ReadOnlyDictionary<int, int>(frames);
        Animations = new ReadOnlyDictionary<string, CampaignCreditsAnimationDuration.Snapshot>(animations);
    }
    public static CampaignCreditsFrameTable Bind(CampaignCreditsSource.Document document,
        IReadOnlyDictionary<string, CampaignCreditsAnimationDuration.Snapshot> animations)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(animations);
        var supplied = new Dictionary<string, CampaignCreditsAnimationDuration.Snapshot>(StringComparer.Ordinal);
        int visits = 0;
        foreach (var pair in animations)
        {
            if (++visits > CampaignCreditsSource.MaxReferencedWidgets || pair.Key == null || pair.Value == null
                || !supplied.TryAdd(pair.Key, pair.Value)) throw new InvalidDataException("Credits animation inventory is invalid or exceeds its bound.");
        }
        var frames = new Dictionary<int, int>();
        var retained = new Dictionary<string, CampaignCreditsAnimationDuration.Snapshot>(StringComparer.Ordinal);
        foreach (var pair in document.Widgets)
        {
            var files = pair.Value.Files;
            string? path = files.Count == 2 ? files[1].Path : null;
            int count = 1;
            if (path != null)
            {
                if (!supplied.TryGetValue(path, out var snapshot)) throw new InvalidDataException("Credits required animation duration is unavailable.");
                count = snapshot.WidgetFrames; retained.TryAdd(path, snapshot);
            }
            frames.Add(pair.Key, count);
        }
        return new(document, frames, retained);
    }
}
