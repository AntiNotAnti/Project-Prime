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
        private static ServerContentManifest? _manifest;

        internal static ReadOnlySpan<ServerContentScenario> ValidatedScenarios
            => _manifest?.Scenarios ?? Array.Empty<ServerContentScenario>();
        public static byte[] ReadBytes(string path) => ContentFiles.ReadBytes(path);
        internal static void RecordModel(string path) => ContentFiles.RecordModel(path);
        internal static IDisposable TraceReads(Action<string, byte[]> record, Action<string>? recordModel = null)
            => ContentFiles.ObserveReads(record, recordModel);

        internal static IDisposable PreserveContext(string version) => new ContentContext(version);

        private sealed class ContentContext(string version) : IDisposable
        {
            private readonly string _version = Paths.MphKey;
            private readonly string _path = Paths.AllPaths[version];
            private readonly ServerContentManifest? _savedManifest = _manifest;

            public void Dispose()
            {
                Paths.SetPath(version, _path);
                Paths.MphKey = _version;
                _manifest = _savedManifest;
                ClearContentCaches();
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
            if (_manifest == null) { return requested ?? fallback; }
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
