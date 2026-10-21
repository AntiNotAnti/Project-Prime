using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MphRead.Mods.Import.Retro;

namespace MphRead.Mods.Import;

/// <summary>
/// Private body-material authoring transport. Retains complete raw material-set
/// records (including opaque bytes) and every referenced TXTR. This is neither
/// a shader nor a runtime capability. Geometry/UV/lighting/pass qualification
/// remains with the existing character and Scene owners.
/// </summary>
public static class RetroCharacterMaterialArchive
{
    public const int HeaderBytes = 256, EntryBytes = 112;
    public const int MaxArchiveBytes = 32 * 1024 * 1024;
    public const int MaxMaterialSetBytes = 2 * 1024 * 1024;
    public const int MaxMaterials = 32, MaxTextures = 64;
    public const int MaxTextureBytes = 4 * 1024 * 1024;
    public const int MaxCskrBytes = 4 * 1024 * 1024;
    public const int MaxDecodedImageBytes = 4 * 1024 * 1024;
    public const int MaxDecodedBytes = 16 * 1024 * 1024;
    public const string Contract = "retro-character-raw-materials-txtr-authoring-v1";
    private static readonly byte[] PixelHash = SHA256.HashData(Encoding.UTF8.GetBytes(RetroTextureSource.PixelContract));

    public sealed record Source(string CharacterId, uint Cmdl, uint Cskr, uint Cinf, uint Ancs, string SourceFingerprint);
    public sealed record Texture(uint ResourceId, uint Format, int Width, int Height, int MipCount,
        int Offset, int Length, int DecodedBytes, string SourceSha256, string RgbaSha256);
    public sealed record Program(int Ordinal, int Offset, int Length, string RawSha256, IReadOnlyList<uint> TextureIds);

    public sealed class Archive
    {
        private readonly byte[] _bytes;
        private readonly uint[] _textureTable;
        public Source Binding { get; }
        public string CmdlSha256 { get; }
        public string CskrSha256 { get; }
        public string GeometrySha256 { get; }
        public string MaterialSetSha256 { get; }
        public string Sha256 { get; }
        public IReadOnlyList<Program> Programs { get; }
        public IReadOnlyList<Texture> Textures { get; }
        public int DecodedBytes { get; }
        internal Archive(byte[] bytes, Source binding, uint[] table, Program[] programs, Texture[] textures)
        {
            _bytes = bytes; Binding = binding; _textureTable = table;
            Programs = Array.AsReadOnly(programs); Textures = Array.AsReadOnly(textures);
            CmdlSha256 = HashAt(bytes, 48); CskrSha256 = HashAt(bytes, 80);
            GeometrySha256 = HashAt(bytes, 112); MaterialSetSha256 = HashAt(bytes, 144);
            Sha256 = Hash(bytes); DecodedBytes = textures.Sum(t => t.DecodedBytes);
        }
        // Mutable parser types are deliberate detached copies, never live state
        // exposed from this snapshot. No unsupported selector is rewritten.
        public Cmdl.Material ReadProgram(int ordinal)
        {
            if ((uint)ordinal >= Programs.Count) throw new ArgumentOutOfRangeException(nameof(ordinal));
            Program p = Programs[ordinal];
            var material = Cmdl.ReadMaterial(_bytes, p.Offset, p.Offset + p.Length, echoes: IsEchoes(Binding.CharacterId));
            material.TexIds = material.Tex.Select(t => _textureTable[t]).ToArray();
            return material;
        }
        public ReadOnlySpan<byte> RawProgram(int ordinal)
        {
            if ((uint)ordinal >= Programs.Count) throw new ArgumentOutOfRangeException(nameof(ordinal));
            Program p = Programs[ordinal]; return _bytes.AsSpan(p.Offset, p.Length);
        }
        public ReadOnlySpan<byte> RawTexture(uint resourceId)
        {
            Texture t = RequireTexture(resourceId); return _bytes.AsSpan(t.Offset, t.Length);
        }
        public RetroTextureSource.Decoded DecodeTexture(uint resourceId)
        {
            Texture t = RequireTexture(resourceId);
            var image = RetroTextureSource.DecodeTop(_bytes.AsSpan(t.Offset, t.Length), MaxDecodedImageBytes);
            Require(Hash(image.Pixels) == t.RgbaSha256, "Character material decoded pixels changed.");
            return image;
        }
        private Texture RequireTexture(uint id) => Textures.SingleOrDefault(t => t.ResourceId == id)
            ?? throw new InvalidDataException("Texture is absent from the character material archive.");
    }

    public static byte[] Encode(Source source, byte[] cmdl, byte[] cskr, string geometrySha256,
        Func<uint, byte[]> resource, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested(); CheckSource(source);
        ArgumentNullException.ThrowIfNull(cmdl); ArgumentNullException.ThrowIfNull(cskr); ArgumentNullException.ThrowIfNull(resource);
        Require(cmdl.Length is >= 64 and <= MaxArchiveBytes
            && cskr.Length is > 0 and <= MaxCskrBytes && Hex(geometrySha256, 64), "Invalid character material source binding.");
        // Capture caller arrays before resource callbacks can mutate them.
        byte[] model = cmdl.ToArray(), skin = cskr.ToArray();
        bool echoes = IsEchoes(source.CharacterId);
        var section = MaterialSection(model, echoes);
        byte[] materials = model.AsSpan(section.Start, section.Length).ToArray();
        var parsed = ParseSet(materials, 0, echoes);
        uint[] ids = parsed.Programs.SelectMany(p => p.TextureIds).Distinct().Order().ToArray();
        Require(ids.Length is > 0 and <= MaxTextures, "Character texture count exceeds its limit.");
        long next = HeaderBytes + (long)ids.Length * EntryBytes + materials.Length, decodedTotal = 0;
        var captured = new byte[ids.Length][];
        var textures = new Texture[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            byte[] raw = resource(ids[i]) ?? throw new InvalidDataException("Missing character TXTR.");
            Require(raw.Length is >= 12 and <= MaxTextureBytes, "Character TXTR exceeds its source budget.");
            raw = raw.ToArray();
            var layout = RetroTextureSource.Inspect(raw, MaxDecodedImageBytes);
            decodedTotal += layout.TopRgbaBytes;
            Require(decodedTotal <= MaxDecodedBytes && next + raw.Length <= MaxArchiveBytes,
                "Character material archive exceeds its aggregate budget.");
            textures[i] = new(ids[i], layout.Format, layout.Width, layout.Height, layout.MipCount,
                (int)next, raw.Length, layout.TopRgbaBytes, Hash(raw), "");
            captured[i] = raw; next += raw.Length;
        }
        // Admit every source extent and the aggregate pixel work before decoding.
        for (int i = 0; i < textures.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var image = RetroTextureSource.DecodeTop(captured[i], MaxDecodedImageBytes);
            textures[i] = textures[i] with { RgbaSha256 = Hash(image.Pixels) };
        }
        cancellation.ThrowIfCancellationRequested();
        byte[] output = new byte[(int)next]; Span<byte> h = output.AsSpan(0, HeaderBytes);
        "PPCM"u8.CopyTo(h); Put(h, 4, 1); Put(h, 8, HeaderBytes); Put(h, 12, output.Length);
        Put(h, 16, Profile(source.CharacterId)); Put(h, 20, parsed.Programs.Length);
        Put(h, 24, ids.Length); Put(h, 28, materials.Length);
        Put(h, 32, source.Cmdl); Put(h, 36, source.Cskr); Put(h, 40, source.Cinf); Put(h, 44, source.Ancs);
        SHA256.HashData(model).CopyTo(h[48..]); SHA256.HashData(skin).CopyTo(h[80..]);
        Convert.FromHexString(geometrySha256).CopyTo(h[112..]); SHA256.HashData(materials).CopyTo(h[144..]);
        Convert.FromHexString(source.SourceFingerprint).CopyTo(h[176..]);
        Encoding.ASCII.GetBytes(echoes ? "G2ME01" : "GM8E01").CopyTo(h[196..]); PixelHash.CopyTo(h[204..]);
        materials.CopyTo(output.AsSpan(HeaderBytes + ids.Length * EntryBytes));
        for (int i = 0; i < textures.Length; i++)
        {
            Texture t = textures[i]; Span<byte> row = output.AsSpan(HeaderBytes + i * EntryBytes, EntryBytes);
            Put(row, 0, t.ResourceId); Put(row, 4, t.Format); Put(row, 8, t.Width); Put(row, 12, t.Height);
            Put(row, 16, t.MipCount); Put(row, 20, t.Offset); Put(row, 24, t.Length); Put(row, 28, t.DecodedBytes);
            Convert.FromHexString(t.SourceSha256).CopyTo(row[32..]); Convert.FromHexString(t.RgbaSha256).CopyTo(row[64..]);
            captured[i].CopyTo(output.AsSpan(t.Offset, t.Length));
        }
        return output;
    }

    /// <summary>Complete bounded validation and detached capture; owns no file or GPU state.</summary>
    public static Archive Read(ReadOnlySpan<byte> input, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        Require(input.Length is > HeaderBytes and <= MaxArchiveBytes, "Character material archive exceeds its byte bounds.");
        // Validate only this detached snapshot; caller memory never becomes
        // authoritative between header validation and payload capture.
        byte[] bytes = input.ToArray(); input = bytes;
        Require(input[..4].SequenceEqual("PPCM"u8) && U32(input, 4) == 1 && U32(input, 8) == HeaderBytes
            && U32(input, 12) == input.Length
            && Zero(input.Slice(202, 2)) && input.Slice(204, 32).SequenceEqual(PixelHash) && Zero(input.Slice(236, 20)),
            "Unsupported character material archive header or pixel contract.");
        string id = U32(input, 16) switch { 1 => "darksamus-echoes", 2 => "darksamus-overcharged-echoes",
            3 => "trooper-power", 4 => "trooper-ice", 5 => "trooper-wave", 6 => "trooper-plasma", 7 => "darksamus-echoes-ball", 8 => "darksamus-overcharged-echoes-ball",
            _ => throw new InvalidDataException("Unknown character material profile.") };
        bool echoes = IsEchoes(id);
        Require(input.Slice(196, 6).SequenceEqual(echoes ? "G2ME01"u8 : "GM8E01"u8),
            "Character material profile differs from its source game.");
        var binding = new Source(id, U32(input, 32), U32(input, 36), U32(input, 40), U32(input, 44),
            Convert.ToHexStringLower(input.Slice(176, 20)));
        CheckSource(binding);
        int programs = Int(input, 20), count = Int(input, 24), materialBytes = Int(input, 28);
        Require(programs is > 0 and <= MaxMaterials && count is > 0 and <= MaxTextures
            && materialBytes is > 0 and <= MaxMaterialSetBytes, "Character material directory count exceeds its bounds.");
        int materialAt = HeaderBytes + count * EntryBytes;
        Require(materialAt + (long)materialBytes <= input.Length, "Truncated character material directory.");
        Require(Hash(bytes.AsSpan(materialAt, materialBytes)) == HashAt(bytes, 144), "Character material-set integrity failed.");
        var parsed = ParseSet(bytes.AsSpan(materialAt, materialBytes), materialAt, echoes);
        Require(parsed.Programs.Length == programs, "Character program count differs from the material set.");
        uint[] needed = parsed.Programs.SelectMany(p => p.TextureIds).Distinct().Order().ToArray();
        Require(needed.Length == count, "Character texture directory does not cover exactly every referenced slot.");
        var textures = new Texture[count]; long next = materialAt + materialBytes, decoded = 0;
        for (int i = 0; i < count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> row = bytes.AsSpan(HeaderBytes + i * EntryBytes, EntryBytes);
            uint resource = U32(row, 0); int offset = Int(row, 20), length = Int(row, 24), rgbaBytes = Int(row, 28);
            Require(resource == needed[i] && resource != 0 && Zero(row[96..]) && offset == next
                && length is >= 12 and <= MaxTextureBytes && next + length <= bytes.Length,
                "Invalid character texture identity or contiguous extent.");
            var raw = bytes.AsSpan(offset, length); var layout = RetroTextureSource.Inspect(raw, MaxDecodedImageBytes);
            decoded += layout.TopRgbaBytes;
            Require(decoded <= MaxDecodedBytes && layout.Format == U32(row, 4) && layout.Width == Int(row, 8)
                && layout.Height == Int(row, 12) && layout.MipCount == Int(row, 16) && layout.TopRgbaBytes == rgbaBytes
                && Hash(raw) == HashAt(row, 32), "Character texture metadata, budget or integrity failed.");
            textures[i] = new(resource, layout.Format, layout.Width, layout.Height, layout.MipCount,
                offset, length, rgbaBytes, HashAt(row, 32), HashAt(row, 64));
            next += length;
        }
        Require(next == bytes.Length, "Unexpected character material trailing data.");
        // Every directory row/source/mip/budget has passed before allocating any
        // decoded image. Check even slots unused by the executed TEV stages.
        foreach (Texture t in textures)
        {
            cancellation.ThrowIfCancellationRequested();
            var image = RetroTextureSource.DecodeTop(bytes.AsSpan(t.Offset, t.Length), MaxDecodedImageBytes);
            Require(Hash(image.Pixels) == t.RgbaSha256, "Character texture pixel integrity failed.");
        }
        cancellation.ThrowIfCancellationRequested();
        return new(bytes, binding, parsed.Table, parsed.Programs, textures);
    }

    // Only the first material set of a bounded, edition-bound CMDL is captured. This
    // does not turn the legacy complete CMDL parser into an untrusted reader.
    private static (int Start, int Length) MaterialSection(ReadOnlySpan<byte> cmdl, bool echoes)
    {
        Require(cmdl.Length is >= 64 and <= MaxArchiveBytes && Be32(cmdl, 0) == 0xdeadbabe
            && Be32(cmdl, 4) == (echoes ? 4u : 2u), "Expected an edition-bound character body CMDL.");
        uint sections = Be32(cmdl, 0x24), sets = Be32(cmdl, 0x28);
        Require(sections is > 0 and <= 1024 && sets is > 0 and <= 16 && sets <= sections
            && 0x2c + 4L * sections <= cmdl.Length, "Invalid character CMDL section directory.");
        long at = (0x2c + 4L * sections + 31) & ~31L;
        int first = (int)at, firstLength = 0;
        for (int i = 0; i < sections; i++)
        {
            uint size = Be32(cmdl, 0x2c + i * 4); if (i == 0) firstLength = checked((int)Math.Min(size, int.MaxValue));
            at += size; Require(at <= cmdl.Length, "Truncated character CMDL section.");
        }
        Require(firstLength is > 0 and <= MaxMaterialSetBytes, "Character material set exceeds its byte budget.");
        return (first, firstLength);
    }

    private static (uint[] Table, Program[] Programs) ParseSet(ReadOnlySpan<byte> set, int archiveOffset, bool echoes)
    {
        Require(set.Length is >= 8 and <= MaxMaterialSetBytes, "Invalid character material-set extent.");
        uint textureCount = Be32(set, 0);
        Require(textureCount is > 0 and <= MaxTextures && 8L + textureCount * 4L <= set.Length, "Invalid character material texture table.");
        var table = new uint[(int)textureCount];
        for (int i = 0; i < table.Length; i++) { table[i] = Be32(set, 4 + i * 4); Require(table[i] != 0, "Zero character texture resource."); }
        int countAt = 4 + table.Length * 4; uint count = Be32(set, countAt);
        Require(count is > 0 and <= MaxMaterials && countAt + 4L + count * 4L <= set.Length, "Invalid character material count.");
        int baseAt = countAt + 4 + (int)count * 4, previous = 0;
        byte[] bounded = set.ToArray(); var programs = new Program[(int)count];
        for (int i = 0; i < programs.Length; i++)
        {
            uint end = Be32(set, countAt + 4 + i * 4);
            Require(end > previous && end <= set.Length - baseAt && end - previous <= Cmdl.MaxMaterialBytes,
                "Invalid character material record extent.");
            int start = baseAt + previous, length = (int)end - previous;
            Cmdl.Material material = Cmdl.ReadMaterial(bounded, start, start + length, echoes);
            var ids = new uint[material.Tex.Length];
            for (int t = 0; t < ids.Length; t++)
            { Require((uint)material.Tex[t] < table.Length, "Character material local slot is outside its texture table."); ids[t] = table[material.Tex[t]]; }
            programs[i] = new(i, archiveOffset + start, length, Hash(set.Slice(start, length)), Array.AsReadOnly(ids));
            previous = (int)end;
        }
        // Preserve all opaque set-tail bytes verbatim. No render semantics are
        // assigned to them or to currently uninterpreted material selectors.
        return (table, programs);
    }

    private static void CheckSource(Source source)
    {
        ArgumentNullException.ThrowIfNull(source);
        bool echoes = IsEchoes(source.CharacterId);
        bool ball = source.CharacterId is "darksamus-echoes-ball" or "darksamus-overcharged-echoes-ball";
        (uint cmdl, uint cskr) = source.CharacterId switch
        {
            "darksamus-echoes" => (0xd2c3805bu, 0xb9ec2f8du),
            "darksamus-overcharged-echoes" => (0xfaa12c05u, 0x918e83d3u),
            "darksamus-echoes-ball" => (0x2f2c1e6fu, 0x4403b1b9u),
            "darksamus-overcharged-echoes-ball" => (0x2f2c1e6fu, 0x4403b1b9u),
            "trooper-power" => (0xf8691b86u, 0x9346b450u),
            "trooper-ice" => (0x45e6de5bu, 0x2ec9718du),
            "trooper-wave" => (0x5eb5adfau, 0x359a022cu),
            "trooper-plasma" => (0x202ac74cu, 0x4b05689au),
            _ => throw new InvalidDataException("Unsupported character material appearance.")
        };
        string expected = RetroCharacterPreviewExporter.BundleSpec(ball ? source.CharacterId[..^5] : source.CharacterId,
            echoes ? ExternalSourceEdition.OriginalRecipe : ExternalSourceEdition.PrimeUsaRevision2).SourceFingerprint;
        Require(source.Cmdl == cmdl && source.Cskr == cskr && source.Cinf == (ball ? 0xc7dbc44au : echoes ? 0x4abc8a50u : 0x0c9669bcu)
            && source.Ancs == (ball ? 0x654a3f97u : echoes ? 0x39900a11u : 0x8169653au)
            && source.SourceFingerprint == expected, "Unsupported character material source edition or part.");
    }
    private static bool IsEchoes(string id) => Profile(id) is 1 or 2 or 7 or 8;
    private static int Profile(string id) => id switch { "darksamus-echoes" => 1, "darksamus-overcharged-echoes" => 2,
        "trooper-power" => 3, "trooper-ice" => 4, "trooper-wave" => 5, "trooper-plasma" => 6, "darksamus-echoes-ball" => 7,
        "darksamus-overcharged-echoes-ball" => 8,
        _ => throw new InvalidDataException("Unsupported character material appearance.") };
    private static bool Hex(string? s, int length) => s?.Length == length && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Zero(ReadOnlySpan<byte> bytes) { foreach (byte b in bytes) if (b != 0) return false; return true; }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string HashAt(ReadOnlySpan<byte> bytes, int at) => Convert.ToHexStringLower(bytes.Slice(at, 32));
    private static uint Be32(ReadOnlySpan<byte> bytes, int at) { Require(at >= 0 && at + 4L <= bytes.Length, "Truncated character material integer."); return BinaryPrimitives.ReadUInt32BigEndian(bytes[at..]); }
    private static uint U32(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]);
    private static int Int(ReadOnlySpan<byte> bytes, int at) { uint n = U32(bytes, at); Require(n <= int.MaxValue, "Character material integer exceeds its range."); return (int)n; }
    private static void Put(Span<byte> bytes, int at, int value) => Put(bytes, at, checked((uint)value));
    private static void Put(Span<byte> bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes[at..], value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
