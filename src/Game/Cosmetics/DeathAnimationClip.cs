using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Cosmetics;

/// <summary>
/// Strict, preprocessed presentation animation. Tracks contain local deltas
/// from the captured live pose; they are never installed into gameplay
/// animation state.
/// </summary>
public sealed class DeathAnimationClip
{
    public const int SignatureSize = 32;
    public const float MaximumDuration = 3;

    private readonly byte[] _skeletonSignature;
    private readonly ReadOnlyCollection<DeathAnimationTrack> _tracks;

    public DeathAnimationClip(string key, Hunter hunter,
        ReadOnlySpan<byte> skeletonSignature, float duration,
        IEnumerable<DeathAnimationTrack> tracks, float blendDuration = 0.12f)
    {
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) > 128)
            throw new ArgumentException("Animation key must contain 1-128 UTF-8 bytes.", nameof(key));
        if (skeletonSignature.Length != SignatureSize)
            throw new ArgumentException("Skeleton signature must be SHA-256.", nameof(skeletonSignature));
        if (!float.IsFinite(duration) || duration <= 0 || duration > MaximumDuration)
            throw new ArgumentOutOfRangeException(nameof(duration));
        if (!float.IsFinite(blendDuration) || blendDuration < 0 || blendDuration > duration)
            throw new ArgumentOutOfRangeException(nameof(blendDuration));

        Key = key;
        Hunter = hunter;
        Duration = duration;
        BlendDuration = blendDuration;
        _skeletonSignature = skeletonSignature.ToArray();
        DeathAnimationTrack[] values = tracks is DeathAnimationTrack[] array
            ? (DeathAnimationTrack[])array.Clone() : new List<DeathAnimationTrack>(tracks).ToArray();
        if (values.Length > DeathAnimationCodec.MaximumTracks)
            throw new ArgumentException("Animation contains too many tracks.", nameof(tracks));
        Array.Sort(values, static (left, right) => left.NodeIndex.CompareTo(right.NodeIndex));
        for (int i = 1; i < values.Length; i++)
            if (values[i - 1].NodeIndex == values[i].NodeIndex)
                throw new ArgumentException("Animation contains duplicate node tracks.", nameof(tracks));
        _tracks = Array.AsReadOnly(values);
    }

    public string Key { get; }
    public Hunter Hunter { get; }
    public ReadOnlyMemory<byte> SkeletonSignature => _skeletonSignature;
    public float Duration { get; }
    public float BlendDuration { get; }
    public IReadOnlyList<DeathAnimationTrack> Tracks => _tracks;

    public bool Matches(Hunter hunter, ReadOnlySpan<byte> skeletonSignature)
        => hunter == Hunter && skeletonSignature.Length == SignatureSize
            && CryptographicOperations.FixedTimeEquals(_skeletonSignature, skeletonSignature);
}

public sealed class DeathAnimationTrack
{
    private readonly ReadOnlyCollection<CompressedDeathKeyframe> _frames;

    public DeathAnimationTrack(ushort nodeIndex, IEnumerable<CompressedDeathKeyframe> frames)
    {
        NodeIndex = nodeIndex;
        CompressedDeathKeyframe[] values = frames is CompressedDeathKeyframe[] array
            ? (CompressedDeathKeyframe[])array.Clone() : new List<CompressedDeathKeyframe>(frames).ToArray();
        if (values.Length == 0 || values.Length > DeathAnimationCodec.MaximumFramesPerTrack)
            throw new ArgumentException("A track must contain a bounded non-empty frame set.", nameof(frames));
        Array.Sort(values, static (left, right) => left.TimeMilliseconds.CompareTo(right.TimeMilliseconds));
        for (int i = 0; i < values.Length; i++)
        {
            if (!values[i].IsValid || i > 0
                && values[i - 1].TimeMilliseconds == values[i].TimeMilliseconds)
                throw new ArgumentException("Track contains an invalid or duplicate keyframe.", nameof(frames));
        }
        _frames = Array.AsReadOnly(values);
    }

    public ushort NodeIndex { get; }
    public IReadOnlyList<CompressedDeathKeyframe> Frames => _frames;
}

public readonly record struct CompressedDeathKeyframe(ushort TimeMilliseconds,
    Vector3 Translation, Quaternion Rotation, Vector3 Scale)
{
    public bool IsValid => Finite(Translation) && Finite(Scale)
        && float.IsFinite(Rotation.X) && float.IsFinite(Rotation.Y)
        && float.IsFinite(Rotation.Z) && float.IsFinite(Rotation.W)
        && Rotation.LengthSquared > 0.000001f
        && Scale.X > 0 && Scale.Y > 0 && Scale.Z > 0;

    private static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    public Matrix4 ToMatrix()
        => Matrix4.CreateScale(Scale)
            * Matrix4.CreateFromQuaternion(Rotation.Normalized())
            * Matrix4.CreateTranslation(Translation);
}

public static class DeathAnimationSkeleton
{
    public static byte[] ComputeSignature(Hunter hunter, IReadOnlyList<Node> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var canonical = new DeathSkeletonNode[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
            canonical[i] = new DeathSkeletonNode(nodes[i].Name, nodes[i].ParentIndex);
        return ComputeSignature(hunter, canonical);
    }

    public static byte[] ComputeSignature(Hunter hunter,
        IReadOnlyList<DeathSkeletonNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (hunter > Hunter.Guardian || nodes.Count is < 1 or > DeathAnimationCodec.MaximumTracks)
            throw new ArgumentException("Skeleton is outside the cooked animation bounds.", nameof(nodes));
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)hunter);
            writer.Write(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(nodes[i].Name)
                    || Encoding.UTF8.GetByteCount(nodes[i].Name) > 128
                    || nodes[i].ParentIndex < -1 || nodes[i].ParentIndex >= nodes.Count
                    || nodes[i].ParentIndex == i)
                    throw new ArgumentException("Skeleton hierarchy is not canonical.", nameof(nodes));
                byte[] name = Encoding.UTF8.GetBytes(nodes[i].Name);
                writer.Write(name.Length);
                writer.Write(name);
                writer.Write(nodes[i].ParentIndex);
            }
        }
        return SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }
}

public readonly record struct DeathSkeletonNode(string Name, int ParentIndex);

public static class DeathAnimationCodec
{
    private const uint Magic = 0x31414450; // PDA1, little-endian
    private const byte Version = 1;
    public const int MaximumTracks = 128;
    public const int MaximumFramesPerTrack = 512;
    public const int MaximumCookedBytes = 1024 * 1024;

    public static byte[] Write(DeathAnimationClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write((byte)clip.Hunter);
            writer.Write(checked((ushort)MathF.Round(clip.Duration * 1000)));
            writer.Write(checked((ushort)MathF.Round(clip.BlendDuration * 1000)));
            writer.Write(clip.SkeletonSignature.Span);
            byte[] key = Encoding.UTF8.GetBytes(clip.Key);
            writer.Write(checked((ushort)key.Length));
            writer.Write(checked((ushort)clip.Tracks.Count));
            writer.Write(key);
            foreach (DeathAnimationTrack track in clip.Tracks)
            {
                writer.Write(track.NodeIndex);
                writer.Write(checked((ushort)track.Frames.Count));
                foreach (CompressedDeathKeyframe frame in track.Frames)
                {
                    writer.Write(frame.TimeMilliseconds);
                    Write(writer, frame.Translation);
                    writer.Write(frame.Rotation.X); writer.Write(frame.Rotation.Y);
                    writer.Write(frame.Rotation.Z); writer.Write(frame.Rotation.W);
                    Write(writer, frame.Scale);
                }
            }
        }
        if (stream.Length > MaximumCookedBytes)
            throw new InvalidDataException("Cooked death animation exceeds the runtime limit.");
        return stream.ToArray();
    }

    public static bool TryRead(ReadOnlySpan<byte> bytes, int skeletonNodeCount,
        out DeathAnimationClip? clip)
    {
        clip = null;
        if (bytes.Length < 46 || bytes.Length > MaximumCookedBytes || skeletonNodeCount < 0)
            return false;
        var reader = new SpanReader(bytes);
        if (!reader.TryUInt32(out uint magic) || magic != Magic
            || !reader.TryByte(out byte version) || version != Version
            || !reader.TryByte(out byte hunterRaw)
            || !PlayableHunterCatalog.IsPlayable((Hunter)hunterRaw)
            || !reader.TryUInt16(out ushort durationMs) || durationMs == 0
            || durationMs > DeathAnimationClip.MaximumDuration * 1000
            || !reader.TryUInt16(out ushort blendMs) || blendMs > durationMs
            || !reader.TryBytes(DeathAnimationClip.SignatureSize, out ReadOnlySpan<byte> signature)
            || !reader.TryUInt16(out ushort keyLength) || keyLength == 0 || keyLength > 128
            || !reader.TryUInt16(out ushort trackCount) || trackCount > MaximumTracks
            || !reader.TryBytes(keyLength, out ReadOnlySpan<byte> keyBytes))
            return false;
        string key;
        try { key = new UTF8Encoding(false, true).GetString(keyBytes); }
        catch (DecoderFallbackException) { return false; }

        var tracks = new DeathAnimationTrack[trackCount];
        int previousNode = -1;
        try
        {
            for (int trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
            {
                if (!reader.TryUInt16(out ushort nodeIndex) || nodeIndex >= skeletonNodeCount
                    || nodeIndex <= previousNode
                    || !reader.TryUInt16(out ushort frameCount) || frameCount == 0
                    || frameCount > MaximumFramesPerTrack)
                    return false;
                previousNode = nodeIndex;
                var frames = new CompressedDeathKeyframe[frameCount];
                ushort previousTime = 0;
                for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
                {
                    if (!reader.TryUInt16(out ushort time)
                        || time > durationMs || frameIndex > 0 && time <= previousTime
                        || !reader.TryVector3(out Vector3 translation)
                        || !reader.TryQuaternion(out Quaternion rotation)
                        || !reader.TryVector3(out Vector3 scale))
                        return false;
                    var frame = new CompressedDeathKeyframe(time, translation, rotation, scale);
                    if (!frame.IsValid) return false;
                    frames[frameIndex] = frame;
                    previousTime = time;
                }
                tracks[trackIndex] = new DeathAnimationTrack(nodeIndex, frames);
            }
            if (!reader.End) return false;
            clip = new DeathAnimationClip(key, (Hunter)hunterRaw, signature,
                durationMs / 1000f, tracks, blendMs / 1000f);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private static void Write(BinaryWriter writer, Vector3 value)
    { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); }

    private ref struct SpanReader
    {
        private ReadOnlySpan<byte> _remaining;
        public SpanReader(ReadOnlySpan<byte> bytes) => _remaining = bytes;
        public bool End => _remaining.IsEmpty;
        public bool TryByte(out byte value)
        { if (_remaining.IsEmpty) { value = 0; return false; } value = _remaining[0]; _remaining = _remaining[1..]; return true; }
        public bool TryUInt16(out ushort value)
        { if (_remaining.Length < 2) { value = 0; return false; } value = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(_remaining); _remaining = _remaining[2..]; return true; }
        public bool TryUInt32(out uint value)
        { if (_remaining.Length < 4) { value = 0; return false; } value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(_remaining); _remaining = _remaining[4..]; return true; }
        public bool TrySingle(out float value)
        { if (!TryUInt32(out uint raw)) { value = 0; return false; } value = BitConverter.UInt32BitsToSingle(raw); return true; }
        public bool TryBytes(int count, out ReadOnlySpan<byte> value)
        { if (_remaining.Length < count) { value = default; return false; } value = _remaining[..count]; _remaining = _remaining[count..]; return true; }
        public bool TryVector3(out Vector3 value)
        {
            float x = 0, y = 0, z = 0;
            bool result = TrySingle(out x) && TrySingle(out y) && TrySingle(out z);
            value = new(x, y, z);
            return result;
        }
        public bool TryQuaternion(out Quaternion value)
        {
            float x = 0, y = 0, z = 0, w = 0;
            bool result = TrySingle(out x) && TrySingle(out y)
                && TrySingle(out z) && TrySingle(out w);
            value = new(x, y, z, w);
            return result;
        }
    }
}
