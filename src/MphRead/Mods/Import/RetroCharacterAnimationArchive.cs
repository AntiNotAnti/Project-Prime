using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.Import.Retro;

namespace MphRead.Mods.Import;

// One exact, part-bound bank of raw ANIM resources, published by the existing
// transactional bundle. Source graphs remain graphs; one shared leaf is never
// substituted for a blend, sequence, random, transition or nonzero play time.
public static class RetroCharacterAnimationArchive
{
    public const int MaximumBytes = 64 * 1024 * 1024, MaximumResources = 2048;
    private static ReadOnlySpan<byte> Magic => "PPANIM1\0"u8;
    public sealed record Resource(uint Id, int Bytes, string Sha256);
    public sealed record Root(int Ordinal, string Name, string Kind, bool CharacterDeclared, bool StrictPlay, uint? ResourceId);
    internal sealed record Metadata(int Format, bool Echoes, string SourceGameId, string SourceCharacterId, string? SourcePart,
        string SourceCharacterName, uint SourceAncsId, string SourceAncsSha256, string SourceGraphSha256,
        uint Cmdl, uint Cskr, uint Cinf, string SourceCinfSha256, Root[] Roots);
    internal sealed record Entry(Resource Resource, int Offset);

    public sealed class Snapshot
    {
        private readonly byte[] _bytes;
        private readonly Entry[] _entries;
        private readonly Root[] _roots;
        private readonly bool _echoes;
        public string SourceSha256 { get; }
        public int ByteLength => _bytes.Length;
        public IReadOnlyList<Resource> Resources { get; }
        public IReadOnlyList<Root> Roots { get; }
        private Snapshot(byte[] bytes, Entry[] entries, Metadata metadata)
        {
            _bytes = bytes; _entries = entries; _echoes = metadata.Echoes; _roots = metadata.Roots.ToArray();
            SourceSha256 = Sha(bytes); Resources = Array.AsReadOnly(entries.Select(e => e.Resource).ToArray()); Roots = Array.AsReadOnly(_roots);
        }
        public Clip OpenResource(uint id)
        {
            var entry = _entries.SingleOrDefault(e => e.Resource.Id == id) ?? throw new InvalidDataException("ANIM resource is outside this source bank.");
            return new(Anim.LoadBounded(_bytes.AsSpan(entry.Offset, entry.Resource.Bytes).ToArray(), _echoes), entry.Resource);
        }
        public Clip OpenStrictPrimitive(int ordinal)
        {
            if ((uint)ordinal >= _roots.Length) throw new ArgumentOutOfRangeException(nameof(ordinal));
            var root = _roots[ordinal];
            if (!root.StrictPlay || root.ResourceId == null)
                throw new NotSupportedException("Source root is not an exact zero-time primitive; graph semantics remain unsupported.");
            return OpenResource(root.ResourceId.Value);
        }
        internal static Snapshot Capture(byte[] bytes, Entry[] entries, Metadata metadata) => new(bytes, entries, metadata);
    }
    // Captured full temporal keys, no file IO/hash/clock during sampling. Scale
    // stays explicit; rigid render consumers must refuse nonidentity scale.
    public sealed class Clip
    {
        private readonly Anim _anim;
        public Resource Source { get; }
        public int KeyCount => _anim.Keys;
        public int RotationChannels => _anim.Rot.Count;
        public int TranslationChannels => _anim.Trans.Count;
        public int ScaleChannels => _anim.Scale.Count;
        public double Duration => _anim.Duration;
        public double Tick => _anim.Tick;
        public bool RigidScale { get; }
        internal Clip(Anim anim, Resource source)
        { _anim = anim; Source = source; RigidScale = anim.Scale.Values.All(keys => keys.All(k => k.All(x => Math.Abs(x - 1) <= .00001))); }
        internal Anim CaptureAnimation()
        {
            var result = new Anim { Duration = Duration, Tick = Tick, Keys = KeyCount };
            foreach (var (input, output) in new[] { (_anim.Rot, result.Rot), (_anim.Trans, result.Trans), (_anim.Scale, result.Scale) })
                foreach (var channel in input) output.Add(channel.Key, channel.Value.Select(k => k.ToArray()).ToList());
            return result;
        }
        public Pose Sample(double seconds)
        {
            if (!double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            Dictionary<uint, double[]> rotations, translations;
            if (KeyCount == 1)
            {
                rotations = _anim.Rot.ToDictionary(p => p.Key, p => p.Value[0].ToArray());
                translations = _anim.Trans.ToDictionary(p => p.Key, p => p.Value[0].ToArray());
            }
            else _anim.Sample(seconds, out rotations, out translations);
            double time = Math.Clamp(seconds, 0, Duration); if (time >= 1e-7) time -= 1e-7;
            int lo = KeyCount == 1 ? 0 : time / Tick >= KeyCount - 1 ? KeyCount - 2 : (int)(time / Tick);
            double fraction = KeyCount == 1 ? 0 : time / Tick >= KeyCount - 1 ? 1 : time % Tick / Tick;
            var scales = _anim.Scale.ToDictionary(p => p.Key, p => p.Value[lo].Select((x, i) => KeyCount == 1 ? x
                : x + (p.Value[lo + 1][i] - x) * fraction).ToArray());
            return new(rotations, translations, scales);
        }
    }
    public sealed class Pose
    {
        public IReadOnlyDictionary<uint, IReadOnlyList<double>> Rotations { get; }
        public IReadOnlyDictionary<uint, IReadOnlyList<double>> Translations { get; }
        public IReadOnlyDictionary<uint, IReadOnlyList<double>> Scales { get; }
        internal Pose(Dictionary<uint, double[]> rotations, Dictionary<uint, double[]> translations, Dictionary<uint, double[]> scales)
        {
            static IReadOnlyDictionary<uint, IReadOnlyList<double>> Freeze(Dictionary<uint, double[]> values)
                => new ReadOnlyDictionary<uint, IReadOnlyList<double>>(values.ToDictionary(p => p.Key, p => (IReadOnlyList<double>)Array.AsReadOnly(p.Value)));
            Rotations = Freeze(rotations); Translations = Freeze(translations); Scales = Freeze(scales);
        }
    }
    private static Metadata Describe(RetroAnimationGraphProof.Capture graph, byte[] cinf, bool echoes)
    {
        var proof = graph.Metadata;
        var parsed = Ancs.Parse(graph.SourceSnapshot);
        var character = parsed.Characters.Single(c => c.Name == proof.SourceCharacterName);
        Require(character.Cmdl == proof.Cmdl && character.Cskr == proof.Cskr && character.Cinf == proof.Cinf
            && cinf.Length is > 0 and <= 1024 * 1024, "Animation bank part/skeleton identity is invalid.");
        var declared = new HashSet<uint>(character.AnimNames.Select(n => n.Index));
        if (character.UsedAnims != null) declared.UnionWith(character.UsedAnims);
        var roots = proof.Roots.Select(root =>
        {
            var node = proof.Nodes[root.Node];
            bool strict = node.Type == 0 && node.Kind == "play" && node.Children.Length == 0 && node.PlayTimeBits == 0 && node.PlayTimeTypeRaw == 2;
            return new Root(root.Ordinal, root.Name, node.Kind, declared.Contains((uint)root.Ordinal), strict, strict ? node.AnimId : null);
        }).ToArray();
        return new(1, echoes, proof.SourceGameId, proof.SourceCharacterId, proof.SourcePart, proof.SourceCharacterName,
            proof.SourceAncsId, proof.SourceAncsSha256, Sha(graph.Descriptor), proof.Cmdl, proof.Cskr, proof.Cinf, Sha(cinf), roots);
    }
    private static uint[] ResourceIds(RetroAnimationGraphProof.Capture graph)
        => graph.Metadata.Nodes.Where(n => n.Type == 0).Select(n => n.AnimId).Distinct().OrderBy(id => id).ToArray();

    public static byte[] Build(RetroAnimationGraphProof.Capture graph, byte[] cinf, bool echoes, Func<uint, byte[]> resource,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(cinf); ArgumentNullException.ThrowIfNull(resource);
        Require(cinf.Length is > 0 and <= 1024 * 1024, "Animation bank skeleton byte budget exceeded.");
        cancellation.ThrowIfCancellationRequested(); graph = FreezeGraph(graph); cinf = cinf.ToArray(); var metadata = Describe(graph, cinf, echoes);
        byte[] description = JsonSerializer.SerializeToUtf8Bytes(metadata); uint[] ids = ResourceIds(graph);
        Require(ids.Length is > 0 and <= MaximumResources && description.Length <= 1024 * 1024, "Animation bank inventory exceeds its bound.");
        using var output = new BoundedStream(); output.Write(Magic);
        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(description.Length); writer.Write(ids.Length); writer.Write(description);
        long remainingScalars = 32_000_000, remainingWork = 64_000_000;
        foreach (uint id in ids)
        {
            cancellation.ThrowIfCancellationRequested(); byte[] bytes = resource(id);
            Require(bytes.Length is >= 4 and <= Anim.MaximumResourceBytes && output.Length + 40L + bytes.Length <= MaximumBytes,
                "Animation bank raw resource exceeds its aggregate budget.");
            bytes = bytes.ToArray();
            // Qualify allocation/work before decoding; every source key survives.
            var decoded = Anim.LoadBounded(bytes, echoes, Math.Min(Anim.MaximumScalars, remainingScalars), Math.Min(Anim.MaximumDecodeWork, remainingWork), out long work);
            remainingWork -= work;
            remainingScalars -= (long)decoded.Keys * (decoded.Rot.Count * 4 + decoded.Trans.Count * 3 + decoded.Scale.Count * 3);
            writer.Write(id); writer.Write(bytes.Length); writer.Write(SHA256.HashData(bytes)); writer.Write(bytes);
        }
        cancellation.ThrowIfCancellationRequested(); return output.ToArray();
    }
    public static Snapshot Read(ReadOnlySpan<byte> source, RetroAnimationGraphProof.Capture graph, byte[] cinf, bool echoes)
    {
        Require(source.Length is >= 16 and <= MaximumBytes, "Animation bank byte budget exceeded.");
        Require(cinf.Length is > 0 and <= 1024 * 1024, "Animation bank skeleton byte budget exceeded.");
        graph = FreezeGraph(graph); cinf = cinf.ToArray();
        byte[] bytes = source.ToArray(); Require(bytes.AsSpan(0, 8).SequenceEqual(Magic), "Animation bank signature is invalid.");
        int descriptionLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)), count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
        var metadata = Describe(graph, cinf, echoes); var expected = JsonSerializer.SerializeToUtf8Bytes(metadata); uint[] ids = ResourceIds(graph);
        Require(descriptionLength is > 0 and <= 1024 * 1024 && descriptionLength <= bytes.Length - 16 && count == ids.Length
            && count is > 0 and <= MaximumResources && bytes.AsSpan(16, descriptionLength).SequenceEqual(expected),
            "Animation bank source/part/graph metadata does not reproduce from its source.");
        int at = 16 + descriptionLength; var entries = new Entry[count];
        long remainingScalars = 32_000_000, remainingWork = 64_000_000;
        for (int i = 0; i < count; i++)
        {
            Require(bytes.Length - at >= 40, "Animation bank resource header is truncated.");
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)); int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4));
            Require(id == ids[i] && length is >= 4 and <= Anim.MaximumResourceBytes && length <= bytes.Length - at - 40,
                "Animation bank resource identity/extent is invalid.");
            var raw = bytes.AsSpan(at + 40, length);
            Require(SHA256.HashData(raw).AsSpan().SequenceEqual(bytes.AsSpan(at + 8, 32)), "Animation bank resource digest changed.");
            var admission = Anim.MeasureBounded(raw.ToArray(), echoes);
            Require(admission.Scalars <= remainingScalars && admission.Work <= remainingWork,
                "Animation bank aggregate decoded budget exceeded before publication.");
            remainingScalars -= admission.Scalars; remainingWork -= admission.Work;
            entries[i] = new(new(id, length, Sha(raw)), at + 40); at += 40 + length;
        }
        Require(at == bytes.Length, "Animation bank has an unaccounted tail.");
        return Snapshot.Capture(bytes, entries, metadata);
    }
    private static RetroAnimationGraphProof.Capture FreezeGraph(RetroAnimationGraphProof.Capture graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Require(graph.Descriptor.Length is > 0 and <= RetroAnimationGraphProof.MaximumProofBytes
            && graph.SourceSnapshot.Length is >= 8 and <= RetroAnimationIndex.MaxAncsBytes, "Animation bank graph input exceeds its bounds.");
        byte[] source = graph.SourceSnapshot.ToArray(), description = graph.Descriptor.ToArray();
        return new(source, description, RetroAnimationGraphProof.Verify(description, source));
    }
    private static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private sealed class BoundedStream : MemoryStream
    {
        private void Reserve(int count) { if (count < 0 || Length > MaximumBytes - count) throw new InvalidDataException("Animation bank exceeds64MiB."); }
        public override void Write(byte[] buffer, int offset, int count) { Reserve(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Reserve(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Reserve(1); base.WriteByte(value); }
    }
}
