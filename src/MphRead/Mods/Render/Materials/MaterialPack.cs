using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReFuel.Stb;
using MphRead;
using MphRead.Mods.Render;

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
        return new($"model/{Identifier(name)}/texture/{texture}/palette/{palette}/recolor/{recolor}");
    }
    public static string Identifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Missing asset name.");
        // Escape underscores too so encoded punctuation cannot collide with literal text.
        return string.Concat(System.Text.Encoding.UTF8.GetBytes(name.ToLowerInvariant()).Select(b =>
            char.IsAsciiLetterOrDigit((char)b) || b is (byte)'-' or (byte)'.'
                ? ((char)b).ToString() : "_" + b.ToString("x2")));
    }
    public static MaterialAssetKey Scoped(string scope, int texture, int palette, int recolor)
    {
        if (texture < 0 || palette < 0 || recolor < 0) throw new ArgumentOutOfRangeException(nameof(texture));
        return new($"{scope}/texture/{texture}/palette/{palette}/recolor/{recolor}");
    }
    public static string RoomScope(RoomMetadata metadata) => metadata.MaterialMapId != Guid.Empty
        ? "map/" + metadata.MaterialMapId.ToString("N") : "room/" + Identifier(metadata.Name);
    public static MaterialAssetKey Authored(Guid map, Guid material, int recolor = 0)
    {
        if (map == Guid.Empty || material == Guid.Empty || recolor < 0) throw new ArgumentException("Missing authored material identity.");
        return new($"map/{map:N}/material/{material:N}/recolor/{recolor}");
    }
    internal static IReadOnlyDictionary<int, string> AuthoredScopes(Guid map, (Guid Id, string Name)[]? authored, IReadOnlyList<Material> compiled)
    {
        var result = new Dictionary<int, string>();
        if (map == Guid.Empty || authored == null || authored.Length > compiled.Count) return result;
        // The compiler appends authored materials after the imported texture pack.
        // Match names before trusting the layout, so stale/mismatched generated files
        // fall back to their original pair keys instead of overriding the wrong surface.
        int offset = compiled.Count - authored.Length;
        if (authored.Where((value, i) => value.Name != compiled[offset + i].Name).Any()) return result;
        for (int i = 0; i < authored.Length; i++)
            if (authored[i].Id != Guid.Empty) result[offset + i] = $"map/{map:N}/material/{authored[i].Id:N}";
        return result;
    }
    public static string EffectScope(string sharedParticleModel) => "effect/model/" + Identifier(sharedParticleModel);
    public static MaterialAssetKey ForModel(MphRead.Model model, int texture, int palette, int recolor)
        => model.MaterialAssetScope is { } scope && !scope.StartsWith("effect/model/", StringComparison.Ordinal)
            ? Scoped(scope, texture, palette, recolor)
            : Model(model.Name, texture, palette, recolor);
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TextureAlphaMode? AlphaMode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? AlphaCutoff { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? AlphaFactor { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TextureTransferFunction? AlbedoTransfer { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TextureTransferFunction? NormalTransfer { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TextureTransferFunction? SpecularRoughnessTransfer { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TextureTransferFunction? EmissiveTransfer { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RepeatMode? WrapS { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RepeatMode? WrapT { get; set; }
}
public sealed class MaterialImage
{
    private readonly Func<Stream>? _open;
    private readonly Func<byte[]>? _readRgba;
    private readonly bool _factoryCanDecodeOffThread;
    private readonly TextureAssetSemantics? _semanticsOverride;
    public TextureTransferFunction? TransferOverride { get; }
    public TextureAlphaPolicy AlphaPolicy { get; }
    internal TextureWrapPolicy? WrapPolicy { get; }
    internal float AlphaFactor { get; }
    public string Path { get; }
    public int Width { get; }
    public int Height { get; }
    internal bool IsRawRgba => _readRgba != null;
    public bool IsFileBacked => _open == null && _readRgba == null;
    public bool CanDecodeOffThread => IsFileBacked || _readRgba != null || _factoryCanDecodeOffThread;
    public MaterialImage(string path, int width, int height) : this(path, width, height, null, false) { }
    internal MaterialImage(string path, int width, int height, Func<Stream>? open,
        bool canDecodeOffThread = false, TextureTransferFunction? transferOverride = null,
        TextureAlphaPolicy? alphaPolicy = null, TextureWrapPolicy? wrapPolicy = null,
        float alphaFactor = 1f, TextureAssetSemantics? semanticsOverride = null)
    {
        Path = path;
        Width = width;
        Height = height;
        _open = open;
        _factoryCanDecodeOffThread = canDecodeOffThread;
        TransferOverride = transferOverride;
        AlphaPolicy = alphaPolicy ?? TextureAlphaPolicy.Legacy;
        AlphaPolicy.Validate();
        WrapPolicy = wrapPolicy?.Validate();
        if (!float.IsFinite(alphaFactor) || alphaFactor < 0 || alphaFactor > 1)
            throw new ArgumentOutOfRangeException(nameof(alphaFactor));
        AlphaFactor = alphaFactor;
        _semanticsOverride = semanticsOverride;
    }
    private MaterialImage(string path, int width, int height, Func<byte[]> readRgba)
        : this(path, width, height, null, true)
        => _readRgba = readRgba;

    internal static MaterialImage FromRgbaFactory(string path, int width, int height,
        Func<byte[]> readRgba)
    {
        ArgumentNullException.ThrowIfNull(readRgba);
        if (width <= 0 || height <= 0 || width > ModernTextureAsset.MaximumDimension
            || height > ModernTextureAsset.MaximumDimension
            || (long)width * height > ModernTextureAsset.MaximumPixels)
            throw new InvalidDataException("Texture dimensions exceed limits.");
        return new MaterialImage(path, width, height, readRgba);
    }

    public Stream OpenRead()
    {
        if (_readRgba != null)
            throw new NotSupportedException("Raw RGBA material images have no encoded stream.");
        return _open?.Invoke() ?? File.OpenRead(Path);
    }

    internal PreparedTextureAsset Prepare(TextureAssetClass assetClass, TextureAssetChannel channel,
        int maximumDimension, bool linearFiltering = false)
    {
        TextureAssetSemantics semantics = _semanticsOverride ?? TextureAssetSemantics.ForChannel(channel,
            alphaPolicy: channel == TextureAssetChannel.Albedo ? AlphaPolicy : null,
            wrap: WrapPolicy, transferOverride: TransferOverride,
            alphaFactor: AlphaFactor);
        if (semantics.Channel != channel)
            throw new InvalidDataException("Prepared material semantics do not match the requested channel.");
        if (_readRgba != null)
        {
            ModernTextureAsset rgba = ReadRgba(assetClass, channel, semantics);
            if (!linearFiltering) return rgba.Fit(maximumDimension);
            ModernTextureAsset fitted = ColorTexturePolicy.FitForLinearFiltering(rgba, maximumDimension, semantics);
            return ColorTexturePolicy.PrepareForLinearFiltering(fitted, semantics.Alpha, semantics.Wrap);
        }
        using Stream stream = OpenRead();
        return PreparedTextureCodec.Decode(stream, Path, assetClass, channel, maximumDimension,
            semantics, linearFiltering);
    }

    internal ModernTextureAsset ReadRgba(TextureAssetClass assetClass, TextureAssetChannel channel,
        TextureAssetSemantics? semantics = null)
    {
        if (_readRgba != null)
        {
            byte[] pixels = _readRgba();
            return ModernTextureAsset.FromRgba(Path, assetClass, channel, Width, Height, pixels, semantics);
        }
        using Stream stream = OpenRead();
        return PreparedTextureCodec.DecodeRgba(stream, Path, assetClass, channel,
            semantics: semantics);
    }

    internal MaterialImage WithTexturePolicy(TextureTransferFunction? transfer,
        TextureAlphaPolicy alpha, TextureWrapPolicy? wrap = null, float alphaFactor = 1f)
    {
        if (_readRgba != null)
            return new MaterialImage(Path, Width, Height, _readRgba, transfer, alpha, wrap, alphaFactor);
        return new MaterialImage(Path, Width, Height, _open, _factoryCanDecodeOffThread,
            transfer, alpha, wrap, alphaFactor);
    }

    internal MaterialImage WithSemantics(TextureAssetSemantics semantics)
    {
        ArgumentNullException.ThrowIfNull(semantics);
        if (_readRgba != null)
            return new MaterialImage(Path, Width, Height, _readRgba, semantics.Transfer,
                semantics.Alpha, semantics.Wrap, semantics.AlphaFactor, semantics);
        return new MaterialImage(Path, Width, Height, _open, _factoryCanDecodeOffThread,
            semantics.Transfer, semantics.Alpha, semantics.Wrap, semantics.AlphaFactor, semantics);
    }

    private MaterialImage(string path, int width, int height, Func<byte[]> readRgba,
        TextureTransferFunction? transfer, TextureAlphaPolicy alpha, TextureWrapPolicy? wrap,
        float alphaFactor = 1f, TextureAssetSemantics? semanticsOverride = null)
        : this(path, width, height, null, true, transfer, alpha, wrap, alphaFactor, semanticsOverride)
        => _readRgba = readRgba;
}
/// <summary>Backend-independent map semantics: normal XYZ, specular R / roughness G, emissive RGB.</summary>
public sealed record ResolvedMaterial(MaterialAssetKey Key, MaterialImage? Albedo,
    MaterialImage? Normal, MaterialImage? SpecularRoughness, MaterialImage? Emissive,
    TextureAlphaPolicy AlphaPolicy = default, float AlphaFactor = 1f,
    RepeatMode? WrapS = null, RepeatMode? WrapT = null);
public sealed record MaterialPackIssue(string Key, string Channel, string Message, bool Error);

public sealed class MaterialPack
{
    public const int MaximumManifestBytes = 2 * 1024 * 1024;
    public const int MaximumImageBytes = 256 * 1024 * 1024;
    public const long MaximumPackBytes = 1024L * 1024 * 1024;
    public const int MaximumDimension = 8192;
    public const long MaximumPixels = 64L * 1024 * 1024;
    public const int MaximumMaterials = 8192;
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
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
            TextureAlphaPolicy alphaPolicy = entry.AlphaMode is { } alphaMode
                ? new TextureAlphaPolicy(alphaMode, entry.AlphaCutoff ?? 0.5f)
                : new TextureAlphaPolicy(TextureAlphaMode.Legacy, entry.AlphaCutoff ?? 0.5f);
            if (entry.WrapS.HasValue != entry.WrapT.HasValue)
                throw new InvalidDataException("WrapS and WrapT must be provided together: " + key + ".");
            if (entry.WrapS is { } wrapS && (!Enum.IsDefined(wrapS) || !Enum.IsDefined(entry.WrapT!.Value)))
                throw new InvalidDataException("Invalid texture wrap mode: " + key + ".");
            TextureWrapPolicy? wrapPolicy = entry.WrapS is { } s
                ? new TextureWrapPolicy(s, entry.WrapT!.Value) : null;
            float alphaFactor = entry.AlphaFactor ?? 1f;
            if (!float.IsFinite(alphaFactor) || alphaFactor < 0f || alphaFactor > 1f)
                throw new InvalidDataException("Alpha factor must be finite and between zero and one: " + key + ".");
            ValidateTransferOverride(entry.NormalTransfer, TextureAssetChannel.Normal, key);
            ValidateTransferOverride(entry.SpecularRoughnessTransfer, TextureAssetChannel.Material, key);
            MaterialImage? Read(string? relative, string channel, TextureTransferFunction? transfer,
                TextureAlphaPolicy alpha, float channelAlphaFactor = 1f)
            {
                if (string.IsNullOrEmpty(relative)) return null;
                // Unsafe references are manifest errors even for optional channels.
                string path = ContainedPath(root, relative);
                string extension = Path.GetExtension(path);
                if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".ktx2", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Material images must be PNG or KTX2: " + relative);
                if (File.Exists(path) && files.Add(path))
                {
                    total = checked(total + new FileInfo(path).Length);
                    if (total > MaximumPackBytes) throw new InvalidDataException("Pack exceeds byte limit.");
                }
                try
                {
                    return ValidateImageMetadata(path)?.WithTexturePolicy(transfer, alpha,
                        wrapPolicy, channelAlphaFactor);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                {
                    pack._issues.Add(new(key.Value, channel, ex.Message, channel == "albedo"));
                    return null;
                }
            }
            pack._materials.Add(key.Value, new(key,
                Read(entry.Albedo, "albedo", entry.AlbedoTransfer, alphaPolicy, alphaFactor),
                Read(entry.Normal, "normal", entry.NormalTransfer, TextureAlphaPolicy.Legacy),
                Read(entry.SpecularRoughness, "specularRoughness", entry.SpecularRoughnessTransfer, TextureAlphaPolicy.Legacy),
                Read(entry.Emissive, "emissive", entry.EmissiveTransfer, TextureAlphaPolicy.Legacy),
                alphaPolicy, alphaFactor, entry.WrapS, entry.WrapT));
        }
        return pack;
    }

    private static void ValidateTransferOverride(TextureTransferFunction? transfer,
        TextureAssetChannel channel, MaterialAssetKey key)
    {
        if (transfer is { } value && !Enum.IsDefined(value))
            throw new InvalidDataException("Invalid transfer function for " + key + ".");
        if ((channel is TextureAssetChannel.Normal or TextureAssetChannel.Material or TextureAssetChannel.Mask)
            && transfer == TextureTransferFunction.Srgb)
            throw new InvalidDataException("Data texture channels must use linear transfer: " + key + ".");
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

    /// <summary>
    /// Validate a portable authored image. Runtime manifest loading uses the
    /// metadata-only path below so large HD assets are not decoded twice before
    /// their first GPU upload.
    /// </summary>
    public static MaterialImage ValidateImage(string path)
    {
        bool ktx2 = Path.GetExtension(path).Equals(".ktx2", StringComparison.OrdinalIgnoreCase);
        MaterialImage image = ValidateImageMetadata(path, validateChunks: !ktx2);
        using var stream = File.OpenRead(path);
        if (ktx2)
        {
            PreparedTextureCodec.ValidateKtx2(stream);
            return image;
        }
#if ANDROID
        using var options = new Android.Graphics.BitmapFactory.Options { InScaled = false, InPremultiplied = false };
        using var decoded = Android.Graphics.BitmapFactory.DecodeStream(stream, null, options);
        if (decoded == null || decoded.Width != image.Width || decoded.Height != image.Height)
            throw new InvalidDataException("Malformed PNG image.");
#else
        using StbImage decoded = StbImage.Load(stream, StbiImageFormat.Rgba);
        if (decoded.Width != image.Width || decoded.Height != image.Height || decoded.ImagePointer == IntPtr.Zero)
            throw new InvalidDataException("Malformed PNG image.");
#endif
        return image;
    }

    private static MaterialImage ValidateImageMetadata(string path, bool validateChunks = false)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumImageBytes) throw new InvalidDataException("Image exceeds byte limit.");

        if (Path.GetExtension(path).Equals(".ktx2", StringComparison.OrdinalIgnoreCase))
        {
            Span<byte> header = stackalloc byte[28];
            stream.ReadExactly(header);
            (int width, int height) = ModernTextureAsset.ProbeDimensions(header);
            if ((long)width * height > MaximumPixels)
                throw new InvalidDataException("Image dimensions exceed limits.");
            return new(path, width, height);
        }

        Span<byte> pngHeader = stackalloc byte[24];
        stream.ReadExactly(pngHeader);
        ReadOnlySpan<byte> signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (!pngHeader[..8].SequenceEqual(signature)
            || BinaryPrimitives.ReadInt32BigEndian(pngHeader[8..12]) != 13
            || !pngHeader[12..16].SequenceEqual("IHDR"u8))
            throw new InvalidDataException("Malformed PNG header.");
        int pngWidth = BinaryPrimitives.ReadInt32BigEndian(pngHeader[16..20]);
        int pngHeight = BinaryPrimitives.ReadInt32BigEndian(pngHeader[20..24]);
        if (pngWidth <= 0 || pngHeight <= 0 || pngWidth > MaximumDimension || pngHeight > MaximumDimension
            || (long)pngWidth * pngHeight > MaximumPixels)
            throw new InvalidDataException("Image dimensions exceed limits.");
        // Runtime manifest discovery only needs a bounded, trustworthy header.
        // CRC-walking an 8K PNG byte-by-byte before decoding it later can dwarf
        // the actual renderer startup cost. Authoring/import validation still
        // requests the strict chunk/CRC pass before accepting an asset.
        if (validateChunks) ValidatePngChunks(stream);
        return new(path, pngWidth, pngHeight);
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
