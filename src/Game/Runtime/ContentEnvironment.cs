using MphRead.Mods.Network;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using MphRead.Mods.MapGen;

namespace MphRead
{
    /// <summary>
    /// Opens either an extracted development filesystem or a verified CPU
    /// content package. Never extracts, writes paths.txt, downloads assets or
    /// falls back to an unrelated local installation.
    /// </summary>
    public static class ContentEnvironment
    {
        public static object SyncRoot { get; } = new();
        public static long Generation { get; private set; }
        private static int _activeLeases;
        private static WorkerContent? _worker;
        private static ServerContentManifest? _manifest;
        private static MatchContentSnapshot? _matchContent;
        private static readonly AsyncLocal<MatchContentSnapshot?> _ambientMatchContent = new();

        public static MatchContentSnapshot? CurrentMatchContent
        {
            get { lock (SyncRoot) return _ambientMatchContent.Value ?? _matchContent; }
        }

        public static void RequireMutableContext()
        {
            if (_activeLeases != 0)
                throw new InvalidOperationException("Worker content cannot change while a scene lease is active.");
        }

        internal static void ContextChanged()
        {
            RequireMutableContext();
            _worker = null;
            _matchContent = null;
            Generation++;
            ClearContentCaches();
        }

        internal static WorkerContentLease AcquireContent()
        {
            lock (SyncRoot)
            {
                _ = Paths.AllPaths;
                Read.ServerMode = true;
                _worker ??= new WorkerContent(Paths.MphKey, Paths.FileSystem, Paths.FhFileSystem, Paths.FhKey, _manifest);
                _activeLeases++;
                return new WorkerContentLease(_worker);
            }
        }

        internal static void ReleaseContent() => _activeLeases--;
        internal static byte[] ReadResource(string path)
        {
            lock (SyncRoot)
            {
                MatchContentSnapshot? selected = _ambientMatchContent.Value ?? _matchContent;
                if (selected?.MapMount.Exists(path) == true)
                    return selected.MapMount.Read(path).ToArray();
                return _worker != null ? _worker.ReadBytes(path) : File.ReadAllBytes(path);
            }
        }

        internal static bool ResourceExists(string path)
        {
            lock (SyncRoot)
            {
                MatchContentSnapshot? selected = _ambientMatchContent.Value ?? _matchContent;
                return selected?.MapMount.Exists(path) == true
                    || (_worker != null ? _worker.ContainsResource(path) : File.Exists(path));
            }
        }

        internal static ReadOnlySpan<ServerContentScenario> ValidatedScenarios
            => _manifest?.Scenarios ?? Array.Empty<ServerContentScenario>();
        /// <summary>Computes browser compatibility without acquiring a scene or changing read mode.</summary>
        public static (string Version, string ContentHash) GetContentIdentity()
        {
            lock (SyncRoot)
            {
                _ = Paths.AllPaths;
                var content = _worker ?? new WorkerContent(Paths.MphKey, Paths.FileSystem, Paths.FhFileSystem, Paths.FhKey, _manifest);
                return (content.Version, content.ContentHash);
            }
        }

        public static byte[] ReadBytes(string path) => ContentFiles.ReadBytes(path);

        public static MatchContentSnapshot MountMap(MapContentIdentity identity, string buildFingerprint,
            string cacheDirectory, MapDefinition definition, string gameplayIdentity)
        {
            lock (SyncRoot)
            {
                RequireMutableContext();
                _ = Paths.AllPaths;
                _worker ??= new WorkerContent(Paths.MphKey, Paths.FileSystem, Paths.FhFileSystem, Paths.FhKey, _manifest);
                _matchContent = CreateMapSnapshot(identity, buildFingerprint, cacheDirectory,
                    definition, gameplayIdentity);
                Generation++;
                ClearContentCaches();
                return _matchContent;
            }
        }

        /// <summary>Creates an immutable per-match overlay without changing process-global content.</summary>
        public static MatchContentSnapshot CreateMapSnapshot(MapContentIdentity identity,
            string buildFingerprint, string cacheDirectory, MapDefinition definition,
            string gameplayIdentity)
        {
            lock (SyncRoot)
            {
                _ = Paths.AllPaths;
                _worker ??= new WorkerContent(Paths.MphKey, Paths.FileSystem, Paths.FhFileSystem,
                    Paths.FhKey, _manifest);
                var mount = new MapContentMount(identity, buildFingerprint, cacheDirectory, definition);
                return MatchContentSnapshot.Create(_worker.ContentHash, identity, gameplayIdentity, mount);
            }
        }

        /// <summary>
        /// Selects content only for the current async/thread execution context. Worker matches use
        /// this around construction and ticks so simultaneous instances cannot see another map.
        /// </summary>
        public static IDisposable UseMatchContent(MatchContentSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            MatchContentSnapshot? previous = _ambientMatchContent.Value;
            _ambientMatchContent.Value = snapshot;
            return new AmbientContentScope(snapshot, previous);
        }

        private sealed class AmbientContentScope(MatchContentSnapshot selected,
            MatchContentSnapshot? previous) : IDisposable
        {
            private bool _disposed;
            public void Dispose()
            {
                if (_disposed) return;
                if (!ReferenceEquals(_ambientMatchContent.Value, selected))
                    throw new InvalidOperationException("Match content scopes must be disposed in owner order.");
                _ambientMatchContent.Value = previous;
                _disposed = true;
            }
        }

        public static void UnmountMap()
        {
            lock (SyncRoot)
            {
                RequireMutableContext();
                if (_matchContent == null) return;
                _matchContent = null;
                Generation++;
                ClearContentCaches();
            }
        }
        internal static void RecordModel(string path) => ContentFiles.RecordModel(path);
        internal static IDisposable TraceReads(Action<string, byte[]> record, Action<string>? recordModel = null)
            => ContentFiles.ObserveReads(record, recordModel);

        internal static IDisposable PreserveContext(string version)
        {
            lock (SyncRoot)
            { RequireMutableContext(); return new ContentContext(version); }
        }

        private sealed class ContentContext(string version) : IDisposable
        {
            private readonly string _version = Paths.MphKey;
            private readonly bool _serverMode = Read.ServerMode;
            private readonly string _path = Paths.AllPaths[version];
            private readonly ServerContentManifest? _savedManifest = _manifest;

            public void Dispose()
            {
                lock (ContentEnvironment.SyncRoot)
                {
                    ContentEnvironment.RequireMutableContext();
                    Read.ServerMode = _serverMode;
                    Paths.SetPath(version, _path);
                    Paths.MphKey = _version;
                    _manifest = _savedManifest;
                    ClearContentCaches();

                }
            }
        }

        private static void ClearContentCaches()
        {
            Read.ClearCache();
            Text.Strings.ClearCache();
            Formats.AiPersonality.ClearCache();
            Formats.Collision.Collision.ClearCache();
        }

        public static int ResolveRoomPlayerCount(int? requested, int fallback)
        {
            if (_manifest == null)
            { return requested ?? fallback; }
            if (requested.HasValue && requested.Value != _manifest.RoomPlayerCount)
            {
                throw new ProgramException("The server content package uses a different entity-layer layout.");
            }
            return _manifest.RoomPlayerCount;
        }

        public static void RequireRoom(string room, GameMode mode)
        {
            if (_manifest != null && !_manifest.Scenarios.Any(s => s.Room == room && s.Mode == mode))
            {
                throw new ProgramException($"The server content package does not support {room}/{mode}.");
            }
        }

        public static void Open(string directory, string version)
        {
            lock (ContentEnvironment.SyncRoot)
            {
                ContentEnvironment.RequireMutableContext();
                if (version is not ("AMHE0" or "AMHE1" or "AMHP0" or "AMHP1"
                    or "AMHJ0" or "AMHJ1" or "AMHK0"))
                {
                    throw new ProgramException("Unsupported server data version: " + version);
                }
                string path = Path.GetFullPath(directory);
                _matchContent = null;
                if (File.Exists(Path.Combine(path, ServerContentPackage.ManifestName)))
                {
                    _manifest = ServerContentPackage.Validate(path, version);
                    ClearContentCaches();
                    Paths.SetPath(version, path);
                    Paths.MphKey = version;
                    return;
                }
                foreach (string required in new[] { "_bin/arm9.bin", "models", "levels" })
                {
                    string entry = Path.Combine(path, required);
                    if (!File.Exists(entry) && !Directory.Exists(entry))
                    {
                        throw new ProgramException($"Server data is missing {required} in {path}. "
                            + "Use the extracted game directory, not the .nds file.");
                    }
                }
                _manifest = null;
                ClearContentCaches();
                Paths.SetPath(version, path);
                Paths.MphKey = version;

            }
        }
    }
}
