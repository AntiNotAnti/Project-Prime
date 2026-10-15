using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MphRead.Mods.MapEditor;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Prime Radiant's native polygon primitive. The payload uses canonical Prime
/// coordinates; only the editor view applies (x*u,-z*u,y*u). Shared vertices,
/// polygon rings, source UVs and authoring metadata are never reconstructed
/// from planes or from the runtime triangle mesh.
/// </summary>
public static class PrimeMeshInterchange
{
    public const int MaximumDecodedBytes = 16 * 1024 * 1024;
    // Radiant's map tokenizer has a 1024 byte token budget. Leave room for
    // quotes and terminators, and emit base64 only at complete quartet edges.
    public const int MaximumChunkCharacters = 768;
    public const int MaximumChunks = ((MaximumDecodedBytes + 2) / 3 * 4 + MaximumChunkCharacters - 1) / MaximumChunkCharacters;
    private static readonly JsonSerializerOptions Options = new(MapPackageReader.JsonOptions)
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 48,
        AllowOutOfOrderMetadataProperties = true
    };
    private static readonly Regex Token = new("\\G\\s*(?:\"(?<chunk>[A-Za-z0-9+/=]*)\"|(?<word>[A-Za-z0-9]+))", RegexOptions.CultureInvariant);
    public sealed record Material(string Name, float TexScale, int Width, int Height);
    private sealed record Payload(int Schema, float Units, MapGeometry Mesh, Material[] Materials,
        MapGeometry? OriginalGeometry = null, string? OriginalMeshSha256 = null,
        bool Reference = false, MapBrush? OriginalLegacyBrush = null);
    public sealed record Parsed(MapGeometry Geometry, Material[] Materials, bool ConvertedToMesh)
    {
        public bool Reference { get; init; }
        public MapBrush? LegacyBrush { get; init; }
    }

    public static bool Required(MapGeometry geometry) => geometry is MapMesh or MapWedge or MapPrism
        // Ordinary Quake primitives have no channels for these Prime fields.
        // An adjunct can restore an existing ID, but Duplicate has a new ID;
        // carry the complete payload so copies retain their authored metadata.
        || geometry.Label != "Geometry" || geometry.Terrain != "Metal" || geometry.Shade != 1
        || geometry.Hidden || geometry.Locked || (geometry is not MapBezierPatch && !geometry.Solid)
        || geometry.Damaging || geometry.FaceUv.Count != 0
        || geometry.Uv.Rotation != 0 || geometry.Uv.Offset.Any(v => v != 0)
        || geometry.Uv.Scale.Any(v => v != 1)
        || geometry.Transform.Rotation.Length != 4
        || geometry.Transform.Rotation.Where((v, i) => v != (i == 3 ? 1 : 0)).Any()
        || (geometry is MapConvexBrush or MapBezierPatch
            && (geometry.Transform.Position.Any(v => v != 0) || geometry.Transform.Scale.Any(v => v != 1)));

    public static void Write(MapGeometry geometry, MapDefinition definition, float units, StringBuilder output,
        Material[]? describedMaterials = null, bool reference = false, MapBrush? originalLegacyBrush = null)
    {
        if (geometry.Material < 0 || geometry.Material >= definition.Materials.Count)
            throw new InvalidDataException("Prime mesh has no bound native material.");
        MapMesh mesh = geometry as MapMesh ?? MapMeshEditing.Convert(geometry, definition.Materials[geometry.Material].TexScale);
        // Conversion is only for parameterized primitives. Keep their complete
        // original source so a pure editor save remains the exact original kind.
        if (geometry is not MapMesh) mesh.Label = geometry.Label;
        var materials = describedMaterials ?? DescribeMaterials(definition);
        Validate(mesh, materials.Length);
        var payload = new Payload(1, units, mesh, materials,
            geometry is MapMesh ? null : geometry,
            geometry is MapMesh && originalLegacyBrush == null ? null : MeshHash(mesh), reference, originalLegacyBrush);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Options);
        if (bytes.Length > MaximumDecodedBytes)
            throw new InvalidDataException("Prime mesh exceeds the 16 MiB decoded primitive budget.");
        string encoded = Convert.ToBase64String(bytes);
        output.AppendLine("primeMeshDef");
        output.AppendLine("{");
        output.AppendLine("version 1");
        output.AppendLine("data " + ((encoded.Length + MaximumChunkCharacters - 1) / MaximumChunkCharacters).ToString(CultureInfo.InvariantCulture));
        for (int offset = 0; offset < encoded.Length; offset += MaximumChunkCharacters)
            output.AppendLine("\"" + encoded.Substring(offset, Math.Min(MaximumChunkCharacters, encoded.Length - offset)) + "\"");
        output.AppendLine("}");
    }

    public static Parsed Parse(IReadOnlyList<string> body, float expectedUnits, Guid containerId)
    {
        if (containerId == Guid.Empty) throw new InvalidDataException("primeMeshDef requires a durable geometry container identity.");
        // Do not join an unbounded input before enforcing its encoded budget.
        int maximumEncoded = (MaximumDecodedBytes + 2) / 3 * 4;
        long textLength = body.Sum(line => (long)line.Length + 1);
        if (textLength > maximumEncoded + MaximumChunks * 8L + 128)
            throw new InvalidDataException("Prime mesh encoded data exceeds its primitive budget.");
        string source = string.Join("\n", body);
        int cursor = 0;
        (string Text, bool Quoted) Next()
        {
            Match match = Token.Match(source, cursor);
            if (!match.Success || match.Index != cursor)
                throw new InvalidDataException("Malformed primeMeshDef token.");
            cursor += match.Length;
            bool quoted = match.Groups["chunk"].Success;
            return (quoted ? match.Groups["chunk"].Value : match.Groups["word"].Value, quoted);
        }
        if (Next() != ("version", false) || Next() != ("1", false) || Next() != ("data", false))
            throw new InvalidDataException("Unsupported primeMeshDef grammar or version.");
        var countToken = Next();
        if (countToken.Quoted || !int.TryParse(countToken.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            || count is < 1 || count > MaximumChunks)
            throw new InvalidDataException("Prime mesh chunk count exceeds its bounded data budget.");
        var encoded = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            var chunk = Next();
            if (!chunk.Quoted || chunk.Text.Length is < 1 or > MaximumChunkCharacters
                || chunk.Text.Length % 4 != 0 || (i + 1 != count && chunk.Text.Contains('='))
                || encoded.Length + chunk.Text.Length > maximumEncoded)
                throw new InvalidDataException("Invalid or oversized Prime mesh base64 chunk.");
            encoded.Append(chunk.Text);
        }
        if (!string.IsNullOrWhiteSpace(source[cursor..]))
            throw new InvalidDataException("Unexpected data following primeMeshDef payload.");
        int padding = encoded[^1] == '=' ? (encoded.Length > 1 && encoded[^2] == '=' ? 2 : 1) : 0;
        if ((long)encoded.Length / 4 * 3 - padding > MaximumDecodedBytes)
            throw new InvalidDataException("Prime mesh decoded payload exceeds 16 MiB.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(encoded.ToString()); }
        catch (FormatException ex) { throw new InvalidDataException("Invalid Prime mesh base64 data.", ex); }
        if (bytes.Length > MaximumDecodedBytes) throw new InvalidDataException("Prime mesh decoded payload exceeds 16 MiB.");
        Payload payload;
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 48 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Prime mesh payload must be a JSON object.");
            RequireUniqueProperties(document.RootElement);
            foreach (string required in new[] { "schema", "units", "mesh", "materials" })
                if (!document.RootElement.TryGetProperty(required, out _))
                    throw new InvalidDataException("Prime mesh payload is missing " + required + ".");
            payload = JsonSerializer.Deserialize<Payload>(bytes, Options)
                ?? throw new InvalidDataException("Missing Prime mesh payload.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid canonical Prime mesh JSON: " + ex.Message, ex); }
        if (payload.Schema != 1 || !float.IsFinite(payload.Units) || payload.Units != expectedUnits
            || payload.Mesh is not MapMesh mesh || payload.Materials == null || payload.Materials.Length is < 1 or > 65535)
            throw new InvalidDataException("Unsupported Prime mesh schema, units, geometry or material table.");
        foreach (Material material in payload.Materials)
            if (material == null || string.IsNullOrWhiteSpace(material.Name) || material.Name.Length > 128
                || material.Name.Any(char.IsControl) || !float.IsFinite(material.TexScale) || material.TexScale <= 0
                || material.Width is < 1 or > 16384 || material.Height is < 1 or > 16384)
                throw new InvalidDataException("Invalid Prime mesh material description.");
        Validate(mesh, payload.Materials.Length);
        MapGeometry result = mesh;
        bool converted = false;
        MapBrush? restoredLegacy = null;
        if (payload.OriginalLegacyBrush != null)
        {
            if (payload.OriginalGeometry != null || payload.Reference
                || payload.OriginalMeshSha256 is not { Length: 64 } legacyHash || legacyHash.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("Invalid original legacy brush recovery metadata.");
            MapMesh derived = PrimeLegacyBrush.ToMesh(payload.OriginalLegacyBrush,
                payload.Materials.Select(m => new MapMaterial { Name = m.Name, TexScale = m.TexScale }).ToList());
            if (!MeshHash(derived).Equals(legacyHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Original legacy brush does not match its recovery mesh fingerprint.");
            if (MeshHash(mesh).Equals(legacyHash, StringComparison.OrdinalIgnoreCase))
            {
                restoredLegacy = payload.OriginalLegacyBrush;
                restoredLegacy.Id = containerId;
            }
            else converted = true;
        }
        else if (payload.OriginalGeometry != null || payload.OriginalMeshSha256 != null)
        {
            if (payload.OriginalGeometry is not (MapBox or MapWedge or MapPrism or MapConvexBrush or MapBezierPatch)
                || payload.OriginalMeshSha256 is not { Length: 64 } digest || digest.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("Invalid original Prime geometry recovery metadata.");
            // Verify the recovery metadata describes the actual original shape,
            // not an unrelated geometry object hidden behind an arbitrary hash.
            MapGeometry original = payload.OriginalGeometry;
            if (original.Material < 0 || original.Material >= payload.Materials.Length)
                throw new InvalidDataException("Original Prime geometry material is out of range.");
            MapMesh derived;
            try { derived = MapMeshEditing.Convert(original, payload.Materials[original.Material].TexScale); }
            catch (Exception ex) when (ex is MapAuthoringException or ArgumentException or NullReferenceException or IndexOutOfRangeException)
            { throw new InvalidDataException("Invalid original Prime geometry recovery shape.", ex); }
            derived.Label = original.Label;
            Validate(derived, payload.Materials.Length);
            if (!string.Equals(MeshHash(derived), digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Original Prime geometry does not match its recovery mesh fingerprint.");
            if (string.Equals(MeshHash(mesh), digest, StringComparison.OrdinalIgnoreCase)) result = original;
            else converted = true;
        }
        // The native editor regenerates prime_id for Duplicate. The copied
        // payload's old ID is deliberately subordinate to that container ID.
        result.Id = containerId;
        if (payload.Reference && (mesh.Hidden || !mesh.Locked || mesh.Solid || payload.OriginalGeometry != null))
            throw new InvalidDataException("Reference geometry must remain visible, locked and non-solid.");
        return new(result, payload.Materials, converted) { Reference = payload.Reference, LegacyBrush = restoredLegacy };
    }

    private static string MeshHash(MapMesh mesh)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes<MapGeometry>(mesh, Options));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
                if (property.Name != "id") property.WriteTo(writer);
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void RequireUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate Prime mesh JSON property: " + property.Name);
                RequireUniqueProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement child in value.EnumerateArray()) RequireUniqueProperties(child);
    }

    private static void Validate(MapMesh mesh, int materials)
    {
        if (mesh.Vertices == null || mesh.Vertices.Count is < 3 or > 65535 || mesh.Faces == null || mesh.Faces.Count is < 1 or > 65535
            || mesh.Vertices.Any(vertex => vertex == null || vertex.Length != 3 || vertex.Any(v => !float.IsFinite(v)))
            || mesh.Faces.Any(face => face == null || face.Length is < 3 or > 32 || face.Any(i => i < 0 || i >= mesh.Vertices.Count) || face.Distinct().Count() != face.Length))
            throw new InvalidDataException("Prime mesh vertices or polygon indices are invalid or over budget.");
        if (mesh.Material < 0 || mesh.Material >= materials || mesh.FaceMaterials == null || mesh.FaceMaterials.Count > mesh.Faces.Count
            || mesh.FaceMaterials.Any(material => material < 0 || material >= materials)
            || mesh.FaceTexcoords == null || mesh.FaceTexcoords.Count > mesh.Faces.Count
            || mesh.FaceTexcoords.Where((uv, index) => uv != null && (uv.Length != mesh.Faces[index].Length
                || uv.Any(point => point == null || point.Length != 2 || point.Any(v => !float.IsFinite(v))))).Any())
            throw new InvalidDataException("Prime mesh face material or UV channels are invalid.");
        if (mesh.Transform == null || mesh.Transform.Position is not { Length: 3 } || mesh.Transform.Scale is not { Length: 3 }
            || mesh.Transform.Rotation is not { Length: 4 } || mesh.Transform.Position.Concat(mesh.Transform.Scale).Concat(mesh.Transform.Rotation).Any(v => !float.IsFinite(v))
            || mesh.Transform.Scale.Any(v => v <= 0) || Math.Abs(mesh.Transform.Rotation.Sum(v => v * v) - 1) > .001f
            || !float.IsFinite(mesh.Shade) || mesh.Slipperiness is < 0 or > 3 || mesh.Label == null || mesh.Layer == null || mesh.Terrain == null)
            throw new InvalidDataException("Prime mesh transform or authoring metadata is invalid.");
        static bool UvValid(MapUv? uv) => uv != null && uv.Scale is { Length: 2 } && uv.Offset is { Length: 2 }
            && uv.Scale.All(v => float.IsFinite(v) && v > 0) && uv.Offset.All(float.IsFinite) && float.IsFinite(uv.Rotation);
        if (!UvValid(mesh.Uv) || mesh.FaceUv == null || mesh.FaceUv.Any(pair => pair.Key < 0 || pair.Key >= mesh.Faces.Count || !UvValid(pair.Value))
            || mesh.QuakeFaces != null)
            throw new InvalidDataException("Prime mesh projection metadata is invalid.");
    }

    public static Material[] DescribeMaterials(MapDefinition definition)
    {
        var descriptions = definition.Materials.Select(value => Describe(value, definition.BaseDirectory)).ToArray();
        // Borrowed native materials have no asset path. Resolve their canonical
        // coordinate dimensions too; HD pixel sizes must never change texel UVs.
        var resolved = MapViewportMaterials.Resolve(definition);
        for (int i = 0; i < descriptions.Length; i++)
            if (resolved.TryGetValue((false, i), out var texture))
            {
                int width = texture.CoordinateWidth > 0 ? texture.CoordinateWidth : texture.Width;
                int height = texture.CoordinateHeight > 0 ? texture.CoordinateHeight : texture.Height;
                if (width is >= 1 and <= 16384 && height is >= 1 and <= 16384)
                    descriptions[i] = descriptions[i] with { Width = width, Height = height };
            }
        return descriptions;
    }

    private static Material Describe(MapMaterial material, string? baseDirectory)
    {
        int width = 64, height = 64;
        // Texture dimensions are a viewport hint. Never rewrite or generate an
        // asset while preparing source, and retain safe defaults if unavailable.
        if (baseDirectory != null && material.Texture != null)
        {
            string path = Path.GetFullPath(Path.Combine(baseDirectory, material.Texture));
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                Span<byte> header = stackalloc byte[14];
                if (stream.Read(header) == header.Length && header[..4].SequenceEqual("FPTX"u8))
                {
                    int w = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[10..12]);
                    int h = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[12..14]);
                    if (w is >= 1 and <= 16384 && h is >= 1 and <= 16384) { width = w; height = h; }
                }
            }
        }
        return new(material.Name, material.TexScale, width, height);
    }
}
