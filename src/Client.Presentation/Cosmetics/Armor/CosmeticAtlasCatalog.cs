using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MphRead.Cosmetics.Presentation;

public readonly record struct CosmeticAtlasSprite(
    string Key, int X, int Y, int Width, int Height)
{
    public float U0(int atlasWidth) => (float)X / atlasWidth;
    public float V0(int atlasHeight) => (float)Y / atlasHeight;
    public float U1(int atlasWidth) => (float)(X + Width) / atlasWidth;
    public float V1(int atlasHeight) => (float)(Y + Height) / atlasHeight;
}

/// <summary>Validated metadata for the one packaged Project Prime VFX atlas.</summary>
public sealed class CosmeticAtlasCatalog
{
    public const string OfficialManifestRelativePath
        = "Assets/Cosmetics/project-prime-vfx-atlas.json";
    public const string OfficialManifestUri
        = "avares://ProjectPrime.Client.Presentation/Assets/Cosmetics/project-prime-vfx-atlas.json";
    public const string OfficialAssetUri
        = "avares://ProjectPrime.Client.Presentation/Assets/Cosmetics/project-prime-vfx-atlas.png";
    public const int MaximumManifestBytes = 64 * 1024;
    public const int MaximumSprites = 64;
    public const int MaximumDimension = 4096;

    private readonly IReadOnlyDictionary<string, CosmeticAtlasSprite> _sprites;

    private CosmeticAtlasCatalog(string image, int width, int height, int insetPixels,
        IReadOnlyDictionary<string, CosmeticAtlasSprite> sprites)
    {
        Image = image;
        Width = width;
        Height = height;
        InsetPixels = insetPixels;
        _sprites = sprites;
    }

    public string Image { get; }
    public int Width { get; }
    public int Height { get; }
    public int InsetPixels { get; }
    public int Count => _sprites.Count;

    public bool TryResolve(string key, out CosmeticAtlasSprite sprite)
        => _sprites.TryGetValue(key, out sprite);

    public static CosmeticAtlasCatalog Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead) throw new ArgumentException("Atlas manifest is not readable.", nameof(stream));
        if (stream.CanSeek && stream.Length > MaximumManifestBytes)
            throw new InvalidDataException("Cosmetic atlas manifest exceeds its size limit.");

        using var bounded = new MemoryStream(capacity: 4096);
        Span<byte> buffer = stackalloc byte[4096];
        int total = 0;
        while (true)
        {
            int read = stream.Read(buffer);
            if (read == 0) break;
            total += read;
            if (total > MaximumManifestBytes)
                throw new InvalidDataException("Cosmetic atlas manifest exceeds its size limit.");
            bounded.Write(buffer[..read]);
        }
        bounded.Position = 0;
        CosmeticAtlasManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(bounded,
                CosmeticAtlasJsonContext.Default.CosmeticAtlasManifest);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Cosmetic atlas manifest is malformed.", error);
        }
        if (manifest == null || manifest.Format != 1
            || manifest.Width is < 1 or > MaximumDimension
            || manifest.Height is < 1 or > MaximumDimension
            || String.IsNullOrWhiteSpace(manifest.Image)
            || Path.GetFileName(manifest.Image) != manifest.Image
            || !manifest.Image.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Cosmetic atlas header is invalid.");
        }
        int insetPixels = manifest.Sampling?.InsetPixels ?? 0;
        if (insetPixels is < 0 or > 16)
            throw new InvalidDataException("Cosmetic atlas sampling inset is invalid.");
        if (manifest.Sprites == null || manifest.Sprites.Length > MaximumSprites)
            throw new InvalidDataException("Cosmetic atlas contains too many sprites.");

        var sprites = new Dictionary<string, CosmeticAtlasSprite>(StringComparer.Ordinal);
        foreach (CosmeticAtlasSpriteDefinition source in manifest.Sprites)
        {
            if (String.IsNullOrWhiteSpace(source.Key)
                || source.X < 0 || source.Y < 0 || source.Width <= 0 || source.Height <= 0
                || source.Width <= insetPixels * 2 || source.Height <= insetPixels * 2
                || source.X + source.Width > manifest.Width
                || source.Y + source.Height > manifest.Height)
            {
                throw new InvalidDataException("Cosmetic atlas sprite is invalid.");
            }
            var sprite = new CosmeticAtlasSprite(source.Key, source.X, source.Y,
                source.Width, source.Height);
            if (!sprites.TryAdd(source.Key, sprite))
                throw new InvalidDataException($"Duplicate cosmetic atlas sprite '{source.Key}'.");
        }
        return new CosmeticAtlasCatalog(manifest.Image, manifest.Width,
            manifest.Height, insetPixels,
            new ReadOnlyDictionary<string, CosmeticAtlasSprite>(sprites));
    }

    // Keep the manifest contract explicit for trimmed and obfuscated clients. The
    // catalog is optional presentation data, but its schema and validation limits
    // must remain identical on desktop and Android.
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CosmeticAtlasManifest))]
internal partial class CosmeticAtlasJsonContext : JsonSerializerContext;

internal sealed record CosmeticAtlasManifest(int Format, string Image, int Width,
    int Height, CosmeticAtlasSampling? Sampling,
    CosmeticAtlasSpriteDefinition[]? Sprites);

internal sealed record CosmeticAtlasSampling(int InsetPixels);

internal sealed record CosmeticAtlasSpriteDefinition(string Key, int X, int Y,
    int Width, int Height);
