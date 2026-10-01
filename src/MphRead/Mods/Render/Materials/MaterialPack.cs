using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReFuel.Stb;

namespace MphRead.Mods.Render.Materials;

/// <summary>Portable identity; never contains a source machine's filesystem path.</summary>
public readonly record struct MaterialAssetKey
{
    public string Value { get; }
    public MaterialAssetKey(string value)
    {
        value = value.Trim().ToLowerInvariant();
        if (value.Length is 0 or > 512 || value.Split('/').Any(part => part.Length == 0 || part is "." or ".."
            || part.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))))
            throw new InvalidDataException("Invalid material key.");
        Value = value;
    }
    public static MaterialAssetKey Model(string name, int texture, int palette, int recolor)
    {
        if (texture < 0 || palette < 0 || recolor < 0) throw new ArgumentOutOfRangeException(nameof(texture));
        // Encode every non-key byte, including '~', rather than collapsing distinct asset names.
        string slug = string.Concat(System.Text.Encoding.UTF8.GetBytes(name.ToLowerInvariant()).Select(b =>
            char.IsAsciiLetterOrDigit((char)b) || b is (byte)'-' or (byte)'.'
                ? ((char)b).ToString() : "_" + b.ToString("x2")));
        return new($"model/{slug}/texture/{texture}/palette/{palette}/recolor/{recolor}");
    }
    public override string ToString() => Value;
}

public sealed class MaterialPackManifest
{
    [JsonRequired]
    public int Format { get; set; } = 1;
    public string Id { get; set; } = "my-pack";
    public string Name { get; set; } = "My Pack";
    public List<MaterialPackEntry> Materials { get; set; } = new();
}
public sealed class MaterialPackEntry
{
    public string Key { get; set; } = "";
    public string? Albedo { get; set; }
    public string? Normal { get; set; }
    public string? SpecularRoughness { get; set; }
    public string? Emissive { get; set; }
}
public sealed record MaterialImage(string Path, int Width, int Height);
/// <summary>Backend-independent map semantics: normal XYZ, specular R / roughness G, emissive RGB.</summary>
public sealed record ResolvedMaterial(MaterialAssetKey Key, MaterialImage? Albedo,
    MaterialImage? Normal, MaterialImage? SpecularRoughness, MaterialImage? Emissive);
public sealed record MaterialPackIssue(string Key, string Channel, string Message, bool Error);

public sealed class MaterialPack
{
    public const int MaximumManifestBytes = 2 * 1024 * 1024;
    public const int MaximumImageBytes = 32 * 1024 * 1024;
    public const long MaximumPackBytes = 256L * 1024 * 1024;
    public const int MaximumDimension = 8192;
    public const long MaximumPixels = 16L * 1024 * 1024;
    public const int MaximumMaterials = 8192;
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16
    };
    public MaterialPackManifest Manifest { get; }
    public IReadOnlyList<MaterialPackIssue> Issues => _issues;
    private readonly List<MaterialPackIssue> _issues = new();
    private readonly Dictionary<string, ResolvedMaterial> _materials = new(StringComparer.Ordinal);
    private MaterialPack(MaterialPackManifest manifest) => Manifest = manifest;
    public bool TryResolve(MaterialAssetKey key, out ResolvedMaterial material) => _materials.TryGetValue(key.Value, out material!);

    public static MaterialPack Load(string directory)
    {
        string root = Path.GetFullPath(directory);
        string manifestPath = ContainedPath(root, "materials.json");
        ValidatePackBudget(root);
        if (new FileInfo(manifestPath).Length > MaximumManifestBytes) throw new InvalidDataException("Manifest exceeds limit.");
        byte[] bytes = File.ReadAllBytes(manifestPath);
        // Strict JSON UTF-8 and duplicate property checks precede object deserialization.
        using (JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 }))
            RejectDuplicateProperties(document.RootElement);
        var manifest = JsonSerializer.Deserialize<MaterialPackManifest>(bytes, Json)
            ?? throw new InvalidDataException("Missing manifest.");
        if (manifest.Format != 1) throw new InvalidDataException("Unsupported manifest version.");
        if (string.IsNullOrWhiteSpace(manifest.Id) || manifest.Id.Length > 128 || string.IsNullOrWhiteSpace(manifest.Name)
            || manifest.Name.Length > 256 || manifest.Materials == null || manifest.Materials.Count > MaximumMaterials)
            throw new InvalidDataException("Invalid manifest metadata or material count.");
        var pack = new MaterialPack(manifest);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        long total = bytes.Length;
        foreach (var entry in manifest.Materials)
        {
            if (entry == null || entry.Key == null) throw new InvalidDataException("Missing material key.");
            var key = new MaterialAssetKey(entry.Key);
            if (!keys.Add(key.Value)) throw new InvalidDataException("Duplicate material key: " + key);
            entry.Key = key.Value;
            MaterialImage? Read(string? relative, string channel)
            {
                if (string.IsNullOrEmpty(relative)) return null;
                // Unsafe references are manifest errors even for optional channels.
                string path = ContainedPath(root, relative);
                if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Only PNG material images are supported: " + relative);
                if (File.Exists(path) && files.Add(path))
                {
                    total = checked(total + new FileInfo(path).Length);
                    if (total > MaximumPackBytes) throw new InvalidDataException("Pack exceeds byte limit.");
                }
                try { return ValidateImage(path); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                {
                    pack._issues.Add(new(key.Value, channel, ex.Message, channel == "albedo"));
                    return null;
                }
            }
            pack._materials.Add(key.Value, new(key, Read(entry.Albedo, "albedo"), Read(entry.Normal, "normal"),
                Read(entry.SpecularRoughness, "specularRoughness"), Read(entry.Emissive, "emissive")));
        }
        return pack;
    }

    internal static void ValidatePackBudget(string root, long additionalBytes = 0)
    {
        var pending = new Stack<string>(); pending.Push(root);
        long bytes = additionalBytes; int count = 0;
        while (pending.Count > 0)
            foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (++count > 32768) throw new InvalidDataException("Pack contains too many entries.");
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked pack assets are not allowed.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                else
                {
                    bytes = checked(bytes + new FileInfo(path).Length);
                    if (bytes > MaximumPackBytes) throw new InvalidDataException("Pack exceeds byte limit.");
                }
            }
    }

    public static string ContainedPath(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.Contains('\\')
            || relative.Contains(':') || relative.Split('/').Any(p => p.Length == 0 || p is "." or ".."))
            throw new InvalidDataException("Unsafe material path: " + relative);
        root = Path.GetFullPath(root);
        // Refuse links at the pack root and inside it before decoding images.
        if (new DirectoryInfo(root).LinkTarget != null) throw new InvalidDataException("Linked pack directories are not allowed.");
        string path = root;
        foreach (string part in relative.Split('/'))
        {
            path = Path.Combine(path, part);
            if (new FileInfo(path).LinkTarget != null || new DirectoryInfo(path).LinkTarget != null)
                throw new InvalidDataException("Linked material paths are not allowed.");
        }
        return path;
    }

    public static MaterialImage ValidateImage(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumImageBytes) throw new InvalidDataException("Image exceeds byte limit.");
        Span<byte> header = stackalloc byte[24];
        stream.ReadExactly(header);
        ReadOnlySpan<byte> signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (!header[..8].SequenceEqual(signature) || BinaryPrimitives.ReadInt32BigEndian(header[8..12]) != 13
            || !header[12..16].SequenceEqual("IHDR"u8)) throw new InvalidDataException("Malformed PNG header.");
        int width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        int height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
        if (width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension
            || (long)width * height > MaximumPixels) throw new InvalidDataException("Image dimensions exceed limits.");
        ValidatePngChunks(stream);
        stream.Position = 0;
        using StbImage decoded = StbImage.Load(stream, StbiImageFormat.Rgba);
        if (decoded.Width != width || decoded.Height != height || decoded.ImagePointer == IntPtr.Zero)
            throw new InvalidDataException("Malformed PNG image.");
        return new(path, width, height);
    }

    private static void ValidatePngChunks(Stream stream)
    {
        stream.Position = 8;
        Span<byte> head = stackalloc byte[8];
        Span<byte> buffer = stackalloc byte[8192];
        Span<byte> expected = stackalloc byte[4];
        bool data = false;
        while (stream.Position < stream.Length)
        {
            stream.ReadExactly(head);
            uint size = BinaryPrimitives.ReadUInt32BigEndian(head[..4]);
            if (size > stream.Length - stream.Position - 4) throw new InvalidDataException("Truncated PNG chunk.");
            uint crc = uint.MaxValue;
            static uint Append(uint state, ReadOnlySpan<byte> bytes)
            {
                foreach (byte value in bytes)
                {
                    state ^= value;
                    for (int bit = 0; bit < 8; bit++) state = (state >> 1) ^ ((state & 1) != 0 ? 0xedb88320u : 0);
                }
                return state;
            }
            crc = Append(crc, head[4..]);
            uint remaining = size;
            while (remaining > 0)
            {
                int read = (int)Math.Min(remaining, (uint)buffer.Length);
                stream.ReadExactly(buffer[..read]); crc = Append(crc, buffer[..read]); remaining -= (uint)read;
            }
            stream.ReadExactly(expected);
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(expected)) throw new InvalidDataException("PNG checksum mismatch.");
            if (head[4..].SequenceEqual("IDAT"u8)) data = true;
            if (head[4..].SequenceEqual("IEND"u8))
            {
                if (size != 0 || !data || stream.Position != stream.Length) throw new InvalidDataException("Invalid PNG end.");
                return;
            }
        }
        throw new InvalidDataException("Missing PNG end.");
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }
}
