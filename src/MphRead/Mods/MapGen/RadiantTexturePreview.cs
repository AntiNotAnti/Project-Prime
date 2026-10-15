using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods.MapEditor;
using ReFuel.Stb;
using ReFuel.Stb.Native;

namespace MphRead.Mods.MapGen;

/// <summary>Disposable editor images; canonical project assets and shader bindings stay authoritative.</summary>
public static class RadiantTexturePreview
{
    private const long MaximumPixels = 32L * 1024 * 1024;
    private const long MaximumBytes = 128L * 1024 * 1024;

    public static string Prepare(MapDefinition definition, string source)
        => Prepare(definition, source, out _);

    public static string Prepare(MapDefinition definition, string source, out IReadOnlyList<string> unavailableMaterials)
    {
        var unavailable = new List<string>();
        unavailableMaterials = unavailable;
        ArgumentNullException.ThrowIfNull(definition);
        string directory = Path.GetDirectoryName(Path.GetFullPath(source))!;
        RequirePlainAncestors(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        string root = Path.Combine(directory, "radiant-preview-assets");
        RequirePlainTree(root);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var relativeNames = new List<string>();
        foreach (var material in definition.Materials)
        {
            string name = material.Name;
            if (name.StartsWith("textures/", StringComparison.Ordinal)) name = name[9..];
            if (name.Length == 0 || name.Length > 128)
                throw new InvalidDataException("Invalid Radiant preview material name.");
            foreach (string segment in name.Split('/'))
            {
                if (segment.Length == 0 || segment is "." or "..")
                    throw new InvalidDataException("Radiant preview material path is unsafe.");
                foreach (char ch in segment)
                    if (!(ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z'
                        or >= '0' and <= '9' or '_' or '-' or '.'))
                        throw new InvalidDataException("Radiant preview material name is not portable.");
            }
            string relative = "textures/" + name + ".png";
            if (!names.Add(relative))
                throw new InvalidDataException("Radiant preview material names collide: " + material.Name);
            string prefix = "";
            foreach (string segment in relative.Split('/'))
            {
                prefix = prefix.Length == 0 ? segment : prefix + "/" + segment;
                if (spellings.TryGetValue(prefix, out string? previous) && previous != prefix)
                    throw new InvalidDataException("Radiant preview path casing collides: " + material.Name);
                spellings[prefix] = prefix;
            }
            relativeNames.Add(relative);
        }
        string staging = root + ".staging-" + Guid.NewGuid().ToString("N");
        string backup = root + ".backup-" + Guid.NewGuid().ToString("N");
        bool movedOld = false;
        bool published = false;
        try
        {
            Directory.CreateDirectory(staging);
            var decoded = MapViewportMaterials.Resolve(definition);
            long pixelsTotal = 0, bytesTotal = 0;
            for (int i = 0; i < relativeNames.Count; i++)
            {
                if (!decoded.TryGetValue((false, i), out var material))
                {
                    unavailable.Add(definition.Materials[i].Name);
                    material = MapViewportMaterials.Checker;
                }
                // HD replacements retain the native texel coordinate dimensions.
                // Bake to those dimensions so Radiant's texture size cannot change UV scale.
                int width = material.CoordinateWidth > 0 ? material.CoordinateWidth : material.Width;
                int height = material.CoordinateHeight > 0 ? material.CoordinateHeight : material.Height;
                long count = (long)width * height;
                if (width is < 1 or > 8192 || height is < 1 or > 8192
                    || material.Width is < 1 or > 8192 || material.Height is < 1 or > 8192
                    || material.Pixels.LongLength != (long)material.Width * material.Height
                    || count > MaximumPixels - pixelsTotal)
                    throw new InvalidDataException("Radiant texture preview exceeds its pixel limit.");
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
                if (encoded.Length > MaximumBytes - bytesTotal)
                    throw new InvalidDataException("Radiant texture preview exceeds its output limit.");
                bytesTotal += encoded.Length;
                string output = Path.Combine(staging, relativeNames[i].Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                RequirePlainAncestors(Path.GetDirectoryName(output)!);
                using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                encoded.Position = 0; encoded.CopyTo(file); file.Flush(true);
            }
            var references = RadiantReferenceScene.Prepare(definition, source);
            RadiantReferenceScene.WritePreviewImages(staging, references.Images, pixelsTotal, bytesTotal);
            RequirePlainTree(root);
            if (Directory.Exists(root)) { Directory.Move(root, backup); movedOld = true; }
            try { Directory.Move(staging, root); }
            catch
            {
                if (movedOld) Directory.Move(backup, root);
                throw;
            }
            published = true;
            return root + Path.DirectorySeparatorChar;
        }
        finally
        {
            DeletePlainTree(staging);
            // If restoration failed, retain the backup for recovery.
            if (published) DeletePlainTree(backup);
        }
    }

    private static void RequirePlainAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0 && !IsMacSystemAlias(current))
                throw new IOException("Radiant preview paths cannot contain symbolic links.");
        }
    }

    internal static bool IsMacSystemAlias(string path)
    {
        // macOS exposes its ordinary temporary directory through these fixed
        // system aliases. Workspace owners and generated children remain plain.
        if (!OperatingSystem.IsMacOS() || path is not ("/var" or "/tmp")) return false;
        return new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName == "/private" + path;
    }

    private static void RequirePlainTree(string path)
    {
        RequirePlainAncestors(path);
        if (File.Exists(path)) throw new IOException("Radiant preview root must be a directory.");
        if (!Directory.Exists(path)) return;
        foreach (string entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Radiant preview tree cannot contain symbolic links.");
            if ((attributes & FileAttributes.Directory) != 0) RequirePlainTree(entry);
        }
    }

    private static void DeletePlainTree(string path)
    {
        if (!Directory.Exists(path)) return;
        RequirePlainTree(path);
        Directory.Delete(path, recursive: true);
    }
}
