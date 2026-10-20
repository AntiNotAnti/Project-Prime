using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen
{
    public sealed record MapBuildFingerprint(int CompilerVersion, int ProjectFormat,
        string RecipeHash, string SourceHash, string TextureHash, string ConfigurationHash)
    {
        // Bump when compiler output or build-relevant defaults change.
        public const int CurrentCompilerVersion = 10;
        public const int SourceGeometryCompilerVersion = 11;

        public static MapBuildFingerprint Create(MapDefinition definition)
        {
            // In-memory edits are the recipe. The file on disk may be older,
            // differently formatted or absent for an unsaved editor document.
            string recipe = HashText(definition.Serialize());
            var dependencies = MapDependencyAnalyzer.Analyze(definition);
            string Select(string kind) => HashText(JsonSerializer.Serialize(
                System.Linq.Enumerable.Where(dependencies, d => d.Kind == kind)));
            return new(definition.HasSourceGeometry ? SourceGeometryCompilerVersion : CurrentCompilerVersion, definition.FormatVersion, recipe,
                Select("source"), Select("textures"), HashText(JsonSerializer.Serialize(dependencies)));
        }

        public string ContentKey => HashText(JsonSerializer.Serialize(new
        { CompilerVersion, ProjectFormat, RecipeHash, SourceHash, TextureHash, ConfigurationHash }));

        public static string HashText(string value) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        public static string HashFile(string? path)
        {
            if (path == null || !File.Exists(path))
            {
                return "missing";
            }
            using Stream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
    }

    public sealed class MapBuildManifest
    {
        public MapBuildFingerprint? Fingerprint { get; set; }
        public string[] OutputHashes { get; set; } = Array.Empty<string>();

        public static bool IsCurrent(MapDefinition definition, MapOutputSet outputs)
        {
            if (!outputs.Complete || !File.Exists(outputs.Manifest)) return false;
            try
            {
                var manifest = JsonSerializer.Deserialize<MapBuildManifest>(File.ReadAllText(outputs.Manifest));
                if (manifest?.Fingerprint != MapBuildFingerprint.Create(definition)
                    || manifest.OutputHashes?.Length != outputs.Files.Count()) return false;
                int index = 0;
                foreach (string file in outputs.Files)
                {
                    if (manifest.OutputHashes[index++] != MapBuildFingerprint.HashFile(file)) return false;
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        public static void Write(MapDefinition definition, MapOutputSet outputs, MapBuildFingerprint? fingerprint = null)
        {
            var manifest = new MapBuildManifest { Fingerprint = fingerprint ?? MapBuildFingerprint.Create(definition) };
            manifest.OutputHashes = System.Linq.Enumerable.ToArray(
                System.Linq.Enumerable.Select(outputs.Files, file => MapBuildFingerprint.HashFile(file)));
            AtomicFile.Write(outputs.Manifest, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest)));
        }
    }

    internal static class AtomicFile
    {
        public static void Write(string path, byte[] bytes)
        {
            string full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, full, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        /// <summary>
        /// Stream immutable build output into a same-volume temporary file and
        /// atomically publish it. Avoid full-file allocations when copying
        /// potentially large mesh/node/texture runtime outputs.
        /// </summary>
        /// <summary>
        /// Bounded asynchronous copy for hosted map archives. Cancellation
        /// never publishes a partially staged package at the destination.
        /// </summary>
        public static async Task<long> CopyAsync(string source, string destination,
            CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            string full = Path.GetFullPath(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                long length;
                await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    length = input.Length;
                    await input.CopyToAsync(output, 65536, cancellation).ConfigureAwait(false);
                    await output.FlushAsync(cancellation).ConfigureAwait(false);
                    if (output.Length != length)
                        throw new IOException("Build output changed length during atomic copy.");
                }
                cancellation.ThrowIfCancellationRequested();
                File.Move(temporary, full, overwrite: true);
                return length;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        public static long Copy(string source, string destination)
        {
            string full = Path.GetFullPath(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                long length;
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 65536, FileOptions.SequentialScan))
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 65536, FileOptions.SequentialScan))
                {
                    length = input.Length;
                    input.CopyTo(output, 65536);
                    output.Flush();
                    if (output.Length != length)
                        throw new IOException("Build output changed length during atomic copy.");
                }
                File.Move(temporary, full, overwrite: true);
                return length;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
