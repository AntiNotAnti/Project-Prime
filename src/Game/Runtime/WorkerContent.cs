using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Network;

namespace MphRead
{
    /// <summary>
    /// One immutable worker resource view. Bytes are captured before scene admission,
    /// so replacing files on disk cannot change a running match. Parsed caches publish
    /// templates under ContentEnvironment.SyncRoot and return independent runtime state.
    /// </summary>
    public sealed class WorkerContent
    {
        private readonly FrozenDictionary<string, byte[]> _files;
        public string Version { get; }
        public string ContentHash { get; }
        public string Directory { get; }
        public IReadOnlyList<ServerContentScenario> SupportedScenarios { get; }
        public IReadOnlyList<string> SupportedRooms { get; }

        internal WorkerContent(string version, string directory, string firstHuntDirectory, string firstHuntVersion,
            ServerContentManifest? manifest)
        {
            Version = version;
            Directory = String.IsNullOrEmpty(directory) ? "" : Path.GetFullPath(directory);
            SupportedScenarios = Array.AsReadOnly(manifest?.Scenarios.ToArray() ?? Array.Empty<ServerContentScenario>());
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes(version + "\n"));
            foreach ((string role, string root, string dataVersion) in new[]
                { ("mph", directory, version), ("fh", firstHuntDirectory, firstHuntVersion) })
            {
                hash.AppendData(Encoding.UTF8.GetBytes(role + "\0" + dataVersion + "\0"));
                if (String.IsNullOrEmpty(root) || !System.IO.Directory.Exists(root)) continue;
                string fullRoot = Path.GetFullPath(root);
                foreach (string file in System.IO.Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal))
                {
                    string relative = Path.GetRelativePath(fullRoot, file);
                    // On Unix a literal backslash is a filename character, while game paths
                    // normalize it to a separator. Such a file has no unambiguous resource key.
                    if (Path.DirectorySeparatorChar != '\\' && relative.Contains('\\'))
                        throw new InvalidDataException("Content contains an ambiguous resource path.");
                    string fullPath = Path.GetFullPath(file);
                    if (!files.TryGetValue(fullPath, out byte[]? bytes))
                    {
                        bytes = File.ReadAllBytes(file);
                        files.Add(fullPath, bytes);
                    }
                    hash.AppendData(Encoding.UTF8.GetBytes(relative.Replace('\\', '/') + "\0"));
                    hash.AppendData(SHA256.HashData(bytes));
                }
            }
            if (manifest != null)
            {
                string manifestPath = Path.GetFullPath(Path.Combine(Directory, ServerContentPackage.ManifestName));
                if (!files.TryGetValue(manifestPath, out byte[]? manifestBytes)
                    || JsonSerializer.Serialize(JsonSerializer.Deserialize<ServerContentManifest>(manifestBytes)) != JsonSerializer.Serialize(manifest))
                    throw new InvalidDataException("Content manifest changed during worker capture.");
                var expected = manifest.Files.Select(entry => Path.GetFullPath(Path.Combine(Directory, entry.Path))).ToHashSet(StringComparer.Ordinal);
                expected.Add(manifestPath);
                foreach (string captured in files.Keys)
                {
                    string relative = Path.GetRelativePath(Directory, captured);
                    if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar)
                        && !expected.Contains(captured))
                        throw new InvalidDataException("An unverified resource appeared during worker capture.");
                }
                foreach (ServerContentFile entry in manifest.Files)
                {
                    string file = Path.GetFullPath(Path.Combine(Directory, entry.Path));
                    if (!files.TryGetValue(file, out byte[]? bytes) || bytes.LongLength != entry.Bytes
                        || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Content changed during worker capture: " + entry.Path);
                }
            }
            _files = files.ToFrozenDictionary(StringComparer.Ordinal);
            ContentHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            SupportedRooms = Array.AsReadOnly(manifest?.Rooms.ToArray() ?? Metadata.RoomList
                .Where(room => !room.FirstHunt && _files.ContainsKey(Path.GetFullPath(Paths.Combine(Directory, room.ModelPath))))
                .Select(room => room.Name).ToArray());
        }

        internal byte[] ReadBytes(string path)
        {
            if (!_files.TryGetValue(Path.GetFullPath(path), out byte[]? bytes))
                throw new FileNotFoundException("The resource is outside the immutable worker content view.", path);
            return (byte[])bytes.Clone();
        }
    }

    public sealed class WorkerContentLease : IDisposable
    {
        public WorkerContent Content { get; }
        private bool _disposed;
        internal WorkerContentLease(WorkerContent content) => Content = content;
        public void Dispose()
        {
            lock (ContentEnvironment.SyncRoot)
            {
                if (_disposed) return;
                _disposed = true;
                ContentEnvironment.ReleaseContent();
            }
        }
    }
}
