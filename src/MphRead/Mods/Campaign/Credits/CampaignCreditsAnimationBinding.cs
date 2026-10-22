using System;
using System.Collections.Generic;
using System.IO;
using static MphRead.Mods.Campaign.CampaignCreditsModelSource;

namespace MphRead.Mods.Campaign;

/// <summary>Exact-byte, model-bound proof for the credits node/material animation profile.
/// Broader duration admission alone cannot authorize animation evaluation.</summary>
public static class CampaignCreditsAnimationBinding
{
    public sealed class Snapshot
    {
        private readonly byte[] _bytes;
        public string SourceSha256 { get; }
        public string ModelSha256 { get; }
        public int FrameCount { get; }
        public int NodeAnimationCount { get; }
        public int MaterialAnimationCount { get; }
        internal ReadOnlyMemory<byte> Memory => _bytes;
        internal Snapshot(byte[] bytes, CampaignCreditsModelSource.Snapshot model, int frames, int materialCount)
        {
            _bytes = bytes; SourceSha256 = Hash(bytes); ModelSha256 = model.SourceSha256;
            FrameCount = frames; NodeAnimationCount = model.NodeCount; MaterialAnimationCount = materialCount;
        }
    }

    public static Snapshot Read(CampaignCreditsModelSource.Snapshot model,
        CampaignCreditsAnimationDuration.Snapshot duration, ReadOnlySpan<byte> source)
    {
        ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(duration);
        Require(source.Length is >= 24 and <= CampaignCreditsAnimationDuration.MaxInputBytes,
            "Credits bound animation extent is unsupported.");
        byte[] owned = source.ToArray(); ReadOnlySpan<byte> b = owned;
        // Revalidate the detached bytes; a duration captured from mutable caller storage is not an ownership proof.
        var exact = CampaignCreditsAnimationDuration.Read(b);
        Require(exact.Sha256 == duration.Sha256 && exact.ByteLength == duration.ByteLength,
            "Credits animation differs from its admitted duration identity.");
        Require(exact.SlotCount == 1 && exact.NodeFrames > 0 && exact.NodeFrames == exact.MaterialFrames
            && exact.TexcoordFrames == 0 && exact.TextureFrames == 0,
            "Credits bound animation supports one matching node/material slot.");
        uint nodeOffset = U32(b, (int)U32(b, 0)), materialOffset = U32(b, (int)U32(b, 8));
        Require(nodeOffset >= 24 && materialOffset >= 24 && U32(b, (int)U32(b, 4)) == 0
            && U32(b, (int)U32(b, 12)) == 0 && U32(b, (int)U32(b, 16)) == 0,
            "Credits bound animation has unsupported channel groups.");
        var nodeGroup = b.Slice((int)nodeOffset, 20); var materialGroup = b.Slice((int)materialOffset, 20);
        uint scale = U32(nodeGroup, 4), rotation = U32(nodeGroup, 8), translation = U32(nodeGroup, 12), nodeRecords = U32(nodeGroup, 16);
        Require(scale >= 24 && (nodeOffset - nodeRecords) / 48 == model.NodeCount,
            "Credits node animation ordinals differ from the model.");
        int[] lengths = { (int)(rotation - scale) / 4, (int)(translation - rotation) / 2, (int)(nodeRecords - translation) / 4 };
        for (uint at = scale; at < rotation; at += 4)
            Require(I32(b, (int)at) is >= -65536 and <= 65536, "Credits animated scale exceeds its bound.");
        for (int i = 0; i < model.NodeCount; i++)
        {
            var node = b.Slice((int)nodeRecords + i * 48, 48);
            Require(node[3] == 0, "Credits node animation flags are unsupported.");
            for (int group = 0; group < 3; group++) for (int axis = 0; axis < 3; axis++)
                Channel(node, group * 16 + axis, group * 16 + 4 + axis * 2, group * 16 + 10 + axis * 2,
                    lengths[group], exact.NodeFrames);
        }
        uint colors = U32(materialGroup, 4), materialRecords = U32(materialGroup, 12), materialCount = U32(materialGroup, 8);
        Require(colors >= 24 && materialCount >= 1 && materialCount <= model.MaterialCount,
            "Credits material animation count or color span is unsupported.");
        int colorCount = (int)(materialRecords - colors);
        foreach (byte color in b.Slice((int)colors, colorCount))
            Require(color <= 31, "Credits animation color exceeds five-bit range.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < materialCount; i++)
        {
            var material = b.Slice((int)materialRecords + i * 140, 140); string name = Name(material);
            Require(model.HasMaterial(name) && names.Add(name), "Credits animated material name is missing or duplicated.");
            for (int group = 0; group < 3; group++) for (int axis = 0; axis < 3; axis++)
                Channel(material, 68 + group * 16 + axis, 72 + group * 16 + axis * 2,
                    78 + group * 16 + axis * 2, colorCount, exact.MaterialFrames);
            Channel(material, 132, 134, 136, colorCount, exact.MaterialFrames);
        }
        return new(owned, model, exact.NodeFrames, (int)materialCount);
    }

    private static void Channel(ReadOnlySpan<byte> record, int blendAt, int lengthAt, int startAt, int poolLength, int frames)
    {
        int length = U16(record, lengthAt), start = U16(record, startAt);
        // Blend1 samples frame directly unless length1 short-circuits to the constant.
        // Prove BOTH the declared channel extent and every runtime frame, even when
        // a shared LUT contains extra bytes belonging to an adjacent channel.
        Require(record[blendAt] == 1 && length > 0 && start + length <= poolLength && (length == 1 || length >= frames),
            "Credits animation channel has an unsupported blend or invalid declared/runtime sample span.");
    }
}
