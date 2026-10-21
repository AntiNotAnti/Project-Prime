using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MphRead.Mods.CustomWeapons.Packages;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Explicit non-executable asset intake. Parsing verifies actual bytes rather
/// than trusting a creator-supplied extension, MIME or JSON metadata. The
/// engine's existing runtime asset decoders remain separate and authoritative.
/// </summary>
public static class CommunityAssetValidator
{
    public const long MaxTextureBytes = 64L * 1024 * 1024;
    public const long MaxModelBytes = 128L * 1024 * 1024;
    public const long MaxDescriptorBytes = 2L * 1024 * 1024;
    public const long MaxWeaponBytes = WeaponPackageLimits.MaximumArchiveBytes;
    public const string WeaponMimeType = "application/vnd.projectprime.ppweapon";

    public static long Limit(string kind) => CommunityContentKinds.Normalize(kind) switch
    {
        CommunityContentKinds.Texture => MaxTextureBytes,
        CommunityContentKinds.Model => MaxModelBytes,
        CommunityContentKinds.Weapon => MaxWeaponBytes,
        CommunityContentKinds.Material or CommunityContentKinds.Prefab => MaxDescriptorBytes,
        _ => throw new InvalidDataException("Gameplay maps must use existing .ppmap validation.")
    };

    public static void Validate(string file, string kind, string mime)
    {
        long limit = Limit(kind);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.SequentialScan);
        if (stream.Length is <= 0 || stream.Length > limit)
            throw new InvalidDataException("Typed Community asset exceeds its size budget.");

        switch (CommunityContentKinds.Normalize(kind))
        {
            case CommunityContentKinds.Weapon:
                if (mime != WeaponMimeType)
                    throw new InvalidDataException("Unsupported weapon package media type.");
                byte[] archive = new byte[checked((int)stream.Length)];
                stream.ReadExactly(archive);
                _ = WeaponPackage.Parse(archive);
                break;
            case CommunityContentKinds.Texture:
                Texture(stream, mime);
                break;
            case CommunityContentKinds.Model:
                Model(stream, mime);
                break;
            case CommunityContentKinds.Material:
            case CommunityContentKinds.Prefab:
                Descriptor(stream, kind, mime);
                break;
            default:
                throw new InvalidDataException("Only non-gameplay assets may use v3 publishing.");
        }
    }

    private static void Texture(FileStream stream, string mime)
    {
        Span<byte> header = stackalloc byte[80];
        int n = stream.Read(header);
        if (mime == "image/png")
        {
            ReadOnlySpan<byte> signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (n < 33 || !header[..8].SequenceEqual(signature)
                || !header.Slice(12, 4).SequenceEqual("IHDR"u8))
                throw new InvalidDataException("Invalid PNG signature/IHDR.");
            int width = BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4));
            int height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4));
            Dimensions(width, height);
            if (header[24] is not (1 or 2 or 4 or 8 or 16))
                throw new InvalidDataException("Unsupported PNG bit depth.");
            return;
        }
        if (mime == "image/jpeg")
        {
            if (n < 4 || header[0] != 0xff || header[1] != 0xd8)
                throw new InvalidDataException("Invalid JPEG start marker.");
            stream.Seek(-2, SeekOrigin.End);
            Span<byte> end = stackalloc byte[2];
            stream.ReadExactly(end);
            if (end[0] != 0xff || end[1] != 0xd9)
                throw new InvalidDataException("JPEG is incomplete.");
            return;
        }
        if (mime == "image/ktx2")
        {
            ReadOnlySpan<byte> signature = new byte[] { 0xab, 0x4b, 0x54, 0x58, 0x20, 0x32, 0x30,
                0xbb, 0x0d, 0x0a, 0x1a, 0x0a };
            if (n < 80 || !header[..12].SequenceEqual(signature))
                throw new InvalidDataException("Invalid KTX2 header.");
            int w = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(20, 4));
            int h = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(24, 4));
            Dimensions(w, h);
            int levels = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(40, 4));
            if (levels is < 1 or > 16)
                throw new InvalidDataException("Invalid KTX2 mip level count.");
            return;
        }
        if (mime == "image/x-tga")
        {
            if (n < 18 || header[1] != 0 || header[2] != 2
                || header[16] is not (24 or 32))
                throw new InvalidDataException("Only uncompressed 24/32-bit TGA supported.");
            int w = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(12, 2));
            int h = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(14, 2));
            Dimensions(w, h);
            return;
        }
        throw new InvalidDataException("Unknown texture media type.");
    }

    private static void Dimensions(int width, int height)
    {
        if (width is < 1 or > 8192 || height is < 1 or > 8192
            || (long)width * height > 64L * 1024 * 1024)
            throw new InvalidDataException("Texture dimensions exceed the sandbox budget.");
    }

    private static void Model(FileStream stream, string mime)
    {
        if (mime == "model/gltf-binary")
        {
            Span<byte> prefix = stackalloc byte[20];
            stream.ReadExactly(prefix);
            if (!prefix[..4].SequenceEqual("glTF"u8)
                || BinaryPrimitives.ReadUInt32LittleEndian(prefix.Slice(4, 4)) != 2
                || BinaryPrimitives.ReadUInt32LittleEndian(prefix.Slice(8, 4))
                    != (uint)stream.Length)
                throw new InvalidDataException("Invalid glTF v2 GLB container.");
            int jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                prefix.Slice(12, 4));
            if (jsonLength is <= 0 or > 8 * 1024 * 1024
                || !prefix.Slice(16, 4).SequenceEqual("JSON"u8))
                throw new InvalidDataException("Invalid GLB JSON chunk.");
            byte[] json = new byte[jsonLength];
            stream.ReadExactly(json);
            ModelJson(json);
            return;
        }
        if (mime == "model/gltf+json")
        {
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
            { MaxDepth = 32, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            ValidateGltf(doc.RootElement);
            return;
        }
        throw new InvalidDataException("Unsupported 3D model media type.");
    }

    private static void ModelJson(byte[] json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        ValidateGltf(doc.RootElement);
    }

    private static void ValidateGltf(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("asset", out JsonElement asset)
            || !asset.TryGetProperty("version", out JsonElement version)
            || version.GetString() != "2.0")
            throw new InvalidDataException("Only glTF 2.0 models are supported.");

        foreach (string category in new[] { "nodes", "meshes", "textures", "materials",
            "images", "buffers", "bufferViews", "accessors" })
            if (root.TryGetProperty(category, out JsonElement list)
                && (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 8192))
                throw new InvalidDataException("glTF scene graph exceeds the node/material budget.");

        // External references, HTML/data URLs, and custom extensions are
        // disabled until the dedicated Studio import trust policy is in place.
        if (root.TryGetProperty("extensionsUsed", out _)
            || root.TryGetProperty("extensionsRequired", out _))
            throw new InvalidDataException("Unreviewed glTF extensions are not published.");
        foreach (string category in new[] { "buffers", "images" })
            if (root.TryGetProperty(category, out JsonElement items))
                foreach (JsonElement item in items.EnumerateArray())
                    if (item.TryGetProperty("uri", out _))
                        throw new InvalidDataException("External glTF resource URIs are not permitted.");
    }

    private static void Descriptor(FileStream stream, string kind, string mime)
    {
        if (kind == CommunityContentKinds.Material
            && mime != "application/vnd.projectprime.material+json"
            || kind == CommunityContentKinds.Prefab
            && mime != "application/vnd.projectprime.prefab+json")
            throw new InvalidDataException("Unsupported descriptor media type.");
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 24 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("A typed descriptor must be an object.");
        void Visit(JsonElement element, int nodes)
        {
            if (nodes > 25000) throw new InvalidDataException("Descriptor complexity exceeded.");
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name is "script" or "javascript" or "shaderSource"
                        or "executable" or "plugin" or "url" or "externalUrl")
                        throw new InvalidDataException("Executable/remote descriptor properties are prohibited.");
                    Visit(property.Value, nodes + 1);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (JsonElement value in element.EnumerateArray())
                    Visit(value, nodes + 1);
            else if (element.ValueKind == JsonValueKind.String
                && element.GetString() is string s
                && s.Length > 16384)
                throw new InvalidDataException("Descriptor string exceeds its budget.");
        }
        Visit(doc.RootElement, 0);
    }
}
