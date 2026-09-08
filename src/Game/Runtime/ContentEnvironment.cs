using MphRead.Mods.Network;
using System;
using System.IO;
using System.Linq;

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

        public static void RequireMutableContext()
        {
            if (_activeLeases != 0)
                throw new InvalidOperationException("Worker content cannot change while a scene lease is active.");
        }

        internal static void ContextChanged()
        {
            RequireMutableContext();
            _worker = null;
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
                return _worker != null ? _worker.ReadBytes(path) : File.ReadAllBytes(path);
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
