using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Mods.MapEditor;
using ReFuel.Stb;
using ReFuel.Stb.Native;

namespace MphRead.Mods.MapGen;

/// <summary>
/// A disposable Radiant view of retained source architecture. These meshes
/// never become project geometry or material bindings when source is applied.
/// </summary>
public static class RadiantReferenceScene
{
    public const string ShaderPrefix = "prime-reference/";
    private const long MaximumPreviewPixels = 32L * 1024 * 1024;
    private const long MaximumPreviewBytes = 128L * 1024 * 1024;
    private static readonly JsonSerializerOptions HashOptions = new(MapPackageReader.JsonOptions)
        { WriteIndented = false };

    public sealed record Identity(Guid Id, string PayloadHash);
    public sealed record Image(string Shader, MapViewportMaterial Material);
    public sealed record Prepared(string Source, string DependencyHash,
        Identity[] Identities, Image[] Images);
    private sealed record HashPayload(float Units, MapGeometry Mesh,
        PrimeMeshInterchange.Material[] Materials);

    public static bool IsReferenceShader(string shader)
        => shader.StartsWith(ShaderPrefix, StringComparison.OrdinalIgnoreCase)
            || shader.StartsWith("textures/" + ShaderPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Hash semantic source data after parsing, rather than JSON spelling or
    /// base64 wrapping, which the native editor can legitimately normalize.
    /// Call before mapping any payload material into the owned project table.
    /// </summary>
    public static string PayloadHash(MapGeometry mesh, IReadOnlyList<PrimeMeshInterchange.Material> materials, float units)
        => MapBuildFingerprint.HashText(JsonSerializer.Serialize(
            new HashPayload(units, mesh, materials.ToArray()), HashOptions));

    public static string PayloadHash(PrimeMeshInterchange.Parsed parsed, float units)
        => PayloadHash(parsed.Geometry, parsed.Materials, units);

    /// <summary>Retained configuration and exact external source contents.</summary>
    public static string DependencyHash(MapDefinition definition, string? source = null)
    {
        if (definition.Import == null && definition.NativeRoom == null)
            return MapBuildFingerprint.HashText("no retained architecture");
        var bakeInputs = new List<MapDependency>();
        // Missing packs are derived from the selected source and nearby PK3s.
        // Their content must participate even though the derived workspace
        // pack deliberately does not become a canonical project dependency.
        if (definition.Import is { } import && !string.IsNullOrEmpty(import.Textures)
            && import.LoadTexturePack() == null && import.Resolve() is { } archiveSource)
            foreach (string archive in MapTextureBake.DiscoverArchives(archiveSource))
                bakeInputs.Add(new("reference-bake", Path.GetFullPath(archive), MapBuildFingerprint.HashFile(archive)));
        return MapBuildFingerprint.HashText(JsonSerializer.Serialize(new
        {
            definition.Import, definition.NativeRoom, definition.Collision,
            definition.TextureSource, definition.Brushes, definition.Materials,
            definition.Assets, definition.ModelSources, definition.Audio,
            Dependencies = MapDependencyAnalyzer.Analyze(definition), BakeInputs = bakeInputs
        }, HashOptions));
    }

    public static Prepared Prepare(MapDefinition definition, string source, float units = QuakeMapInterchange.DefaultUnits)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.MapId == Guid.Empty || !float.IsFinite(units) || units is < 1 or > 1024)
            throw new InvalidDataException("Radiant reference scene requires a map identity and bounded unit scale.");
        string baseline = DependencyHash(definition);
        if (definition.NativeRoom == null && definition.Import == null)
            return new("", baseline, Array.Empty<Identity>(), Array.Empty<Image>());

        var reference = MapProjectSerializer.Clone(definition);
        reference.Geometry.Clear();
        reference.Brushes.Clear();
        reference.Spawns.Clear(); reference.Items.Clear(); reference.JumpPads.Clear();
        reference.NavigationLinks.Clear(); reference.Q3Triggers = null;
        // Import builders also require runtime entities. This private sentinel
        // is never exported; reference preparation consumes only imported faces.
        reference.Spawns.Add(new MapSpawn { Id = Guid.NewGuid() });
        string? privatePack = null;
        try
        {
            if (reference.Import is { } import && !string.IsNullOrEmpty(import.Textures)
                && import.LoadTexturePack() == null)
            {
                // Q3Import.Build can bake a missing pack. Resolve the original
                // archive first, then redirect ONLY the clone's bake target.
                string original = Path.GetFullPath(import.Resolve()
                    ?? throw new FileNotFoundException("Radiant reference source is unavailable.", import.Source));
                string directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, "radiant-reference-cache");
                RequirePlainAncestors(directory);
                if (File.Exists(directory)) throw new IOException("Radiant reference cache must be a directory.");
                Directory.CreateDirectory(directory);
                RequirePlainAncestors(directory);
                privatePack = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".fptx");
                import.Source = original;
                import.BundlePath = null;
                import.Textures = privatePack;
            }

            BuiltMap built;
            IReadOnlyDictionary<(bool Imported, int Index), MapViewportMaterial> decoded;
            lock (MapCompiler.ContentReadLock)
            {
                built = reference.NativeRoom != null ? NativeRoomImport.Build(reference)
                    : Q3Import.Build(reference, verbose: false);
                decoded = MapViewportMaterials.Resolve(reference);
            }
            if (built.ImportedFaceCount < 0 || built.ImportedFaceCount > built.Faces.Count)
                throw new InvalidDataException("Radiant reference source returned an invalid imported face count.");
            var output = new StringBuilder();
            var identities = new List<Identity>();
            var images = new List<Image>();
            foreach (var group in built.Faces.Take(built.ImportedFaceCount).GroupBy(face => face.Material).OrderBy(group => group.Key))
            {
                if (!decoded.TryGetValue((true, group.Key), out MapViewportMaterial? texture))
                    throw new InvalidDataException("Could not decode retained source material " + group.Key + " for Radiant.");
                int width = texture.CoordinateWidth > 0 ? texture.CoordinateWidth : texture.Width;
                int height = texture.CoordinateHeight > 0 ? texture.CoordinateHeight : texture.Height;
                if (width is < 1 or > 8192 || height is < 1 or > 8192)
                    throw new InvalidDataException("Retained source material dimensions exceed Radiant preview bounds.");
                string shader = ShaderPrefix + definition.MapId.ToString("N") + "/" + group.Key.ToString(CultureInfo.InvariantCulture);
                images.Add(new(shader, texture));
                var descriptions = new[] { new PrimeMeshInterchange.Material(shader, 1, width, height) };
                // One material per primitive keeps both the descriptor table
                // and each decoded primitive comfortably below 16 MiB.
                var materials = new MapDefinition { Materials = new() { new MapMaterial { Name = shader, TexScale = 1 } } };
                int chunk = 0;
                MapMesh mesh = NewMesh(chunk);
                foreach (BuiltFace face in group)
                {
                    if (face.Points.Length is < 3 or > 32 || face.Texcoords.Length != face.Points.Length)
                        throw new InvalidDataException("Retained source polygon exceeds Prime mesh corner bounds or has incomplete UVs.");
                    if (mesh.Vertices.Count + face.Points.Length > 65535 || mesh.Faces.Count == 65535)
                    {
                        Write(); mesh = NewMesh(++chunk);
                    }
                    int first = mesh.Vertices.Count;
                    // Preserve every original polygon corner and UV without
                    // precision welding or a triangle reconstruction.
                    mesh.Vertices.AddRange(face.Points.Select(point => new[] { point.X, point.Y, point.Z }));
                    mesh.Faces.Add(Enumerable.Range(first, face.Points.Length).ToArray());
                    mesh.FaceTexcoords.Add(face.Texcoords.Select(uv => new[] { uv.X, uv.Y }).ToArray());
                }
                if (mesh.Faces.Count > 0) Write();

                MapMesh NewMesh(int part) => new()
                {
                    Id = StableId(definition.MapId, group.Key, part),
                    Label = "Retained source architecture", Layer = "Source reference",
                    Locked = true, Solid = false, Material = 0
                };
                void Write()
                {
                    output.AppendLine("{");
                    output.AppendLine("\"classname\" \"func_group\"");
                    output.AppendLine("\"prime_geometry\" \"1\"");
                    output.AppendLine("\"prime_reference\" \"1\"");
                    output.AppendLine("\"prime_id\" \"" + mesh.Id + "\"");
                    output.AppendLine("{");
                    PrimeMeshInterchange.Write(mesh, materials, units, output, descriptions, reference: true);
                    output.AppendLine("}"); output.AppendLine("}");
                    identities.Add(new(mesh.Id, PayloadHash(mesh, descriptions, units)));
                }
            }
            if (DependencyHash(definition) != baseline)
                throw new IOException("Retained source dependencies changed while preparing Radiant. Retry after source files finish saving.");
            return new(output.ToString(), baseline, identities.ToArray(), images.ToArray());
        }
        finally
        {
            if (privatePack != null)
            {
                RequirePlainAncestors(privatePack);
                if (File.Exists(privatePack)) File.Delete(privatePack);
            }
        }
    }

    /// <summary>Append reference images to a caller-owned atomic preview staging tree.</summary>
    public static (long Pixels, long Bytes) WritePreviewImages(string stagingRoot, IReadOnlyList<Image> images,
        long pixelsTotal = 0, long bytesTotal = 0)
    {
        RequirePlainAncestors(stagingRoot);
        foreach (Image image in images)
        {
            // Shader names are generated here, never derived from source paths.
            string[] segments = image.Shader.Split('/');
            if (segments.Length != 3 || segments[0] != ShaderPrefix.TrimEnd('/')
                || !Guid.TryParseExact(segments[1], "N", out _) || !int.TryParse(segments[2], NumberStyles.None,
                    CultureInfo.InvariantCulture, out int materialIndex) || materialIndex < 0)
                throw new InvalidDataException("Invalid reserved Radiant reference image path.");
            var material = image.Material;
            int width = material.CoordinateWidth > 0 ? material.CoordinateWidth : material.Width;
            int height = material.CoordinateHeight > 0 ? material.CoordinateHeight : material.Height;
            long count = (long)width * height;
            if (width is < 1 or > 8192 || height is < 1 or > 8192
                || material.Width is < 1 or > 8192 || material.Height is < 1 or > 8192
                || material.Pixels.LongLength != (long)material.Width * material.Height
                || count > MaximumPreviewPixels - pixelsTotal)
                throw new InvalidDataException("Radiant reference texture preview exceeds its pixel limit.");
            pixelsTotal += count;
            byte[] rgba = new byte[checked((int)count * 4)];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var pixel = material.Pixels[(int)((long)y * material.Height / height) * material.Width
                        + (int)((long)x * material.Width / width)];
                    int offset = (y * width + x) * 4;
                    rgba[offset] = pixel.Red; rgba[offset + 1] = pixel.Green;
                    rgba[offset + 2] = pixel.Blue; rgba[offset + 3] = pixel.Alpha;
                }
            using var encoded = new MemoryStream();
            PngEncoder.Write<byte>(rgba, width, height, StbiImageFormat.Rgba, encoded);
            if (encoded.Length > MaximumPreviewBytes - bytesTotal)
                throw new InvalidDataException("Radiant reference texture preview exceeds its output limit.");
            bytesTotal += encoded.Length;
            string path = Path.Combine(stagingRoot, "textures", image.Shader.Replace('/', Path.DirectorySeparatorChar) + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            RequirePlainAncestors(path);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            encoded.Position = 0; encoded.CopyTo(file); file.Flush(true);
        }
        return (pixelsTotal, bytesTotal);
    }

    private static Guid StableId(Guid mapId, int material, int chunk)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("prime-reference:" + mapId.ToString("N")
            + ":" + material.ToString(CultureInfo.InvariantCulture) + ":" + chunk.ToString(CultureInfo.InvariantCulture)));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static void RequirePlainAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0 && !RadiantTexturePreview.IsMacSystemAlias(current))
                throw new IOException("Radiant reference paths cannot contain symbolic links.");
        }
    }
}
