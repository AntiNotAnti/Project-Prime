using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

internal static class ReplayMapPreparationChecks
{
    internal static void Run(Action<bool, string> check, string folder)
    {
        string maps = CustomRooms.MapDirectory, library = CustomRooms.UserMapDirectory;
        string? runtime = CustomRooms.GeneratedRuntimeRoot;
        string? community = Environment.GetEnvironmentVariable("PROJECT_PRIME_MAP_COMMUNITY");
        string root = Path.Combine(folder, "map-preparation"); Directory.CreateDirectory(root);
        try
        {
            CustomRooms.MapDirectory = Path.Combine(root, "maps"); Directory.CreateDirectory(CustomRooms.MapDirectory);
            CustomRooms.UserMapDirectory = Path.Combine(root, "installed"); Directory.CreateDirectory(CustomRooms.UserMapDirectory);
            CustomRooms.GeneratedRuntimeRoot = Path.Combine(root, "runtime");
            // A self-contained procedural asset avoids extracted cartridge data.
            using (var texture = new BinaryWriter(File.Create(Path.Combine(root, "tile.tex"))))
            {
                texture.Write(Encoding.ASCII.GetBytes("FPTX")); texture.Write((ushort)1); texture.Write((ushort)1);
                texture.Write((ushort)0); texture.Write((ushort)8); texture.Write((ushort)8); texture.Write((ushort)1);
                texture.Write((ushort)0); texture.Write((ushort)32767); texture.Write(new byte[64]);
            }
            var definition = new MapDefinition { FormatVersion = 2, MapId = Guid.NewGuid(), Name = "REPLAY_PREPARATION_CHECK", Version = "1", BaseDirectory = root };
            definition.Materials.Add(new() { Texture = "tile.tex" }); definition.Assets.Add(new() { Path = "tile.tex" });
            definition.Geometry.Add(new MapBox { Transform = new() { Position = [0f, -1, 0], Scale = [8f, 1, 8] } });
            definition.Spawns.Add(new() { Position = [0f, 2, 0] });
            string package = MapPackageBuilder.Build(definition, Path.Combine(root, "source.ppmap"));
            var identity = MapContentIdentity.FromPackage(package); byte[] bytes = File.ReadAllBytes(package);
            var previous = new MapDefinition { FormatVersion = 2, MapId = Guid.NewGuid(), Name = "REPLAY_PREPARATION_PREEXISTING", Version = "1", BaseDirectory = root };
            previous.Materials.Add(new() { Texture = "tile.tex" }); previous.Assets.Add(new() { Path = "tile.tex" });
            previous.Geometry.Add(new MapBox { Transform = new() { Position = [0f, -1, 0], Scale = [8f, 1, 8] } });
            previous.Spawns.Add(new() { Position = [0f, 2, 0] });
            MapPackageBuilder.Build(previous, Path.Combine(CustomRooms.UserMapDirectory, previous.MapId.ToString("N") + MapBundle.Extension));
            var outputs = MapOutputSet.Create(definition, CustomRooms.ArchiveDirectory(definition),
                CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory());
            // Existing published files must survive both a canceled download and
            // a completely prepared transaction that never reaches owner adoption.
            int sentinel = 0;
            foreach (string file in outputs.Files.Append(outputs.Manifest))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllBytes(file, Encoding.UTF8.GetBytes("published-before-preparation-" + sentinel++));
            }
            var published = new PublicationBaseline(CustomRooms.UserMapDirectory, CustomRooms.GeneratedRuntimeRoot);
            int rooms = Metadata.RoomList.Count;
            using (var requested = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var service = new PackageService(bytes, identity.PackageHash.ToString(), requested, release))
            {
                Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_COMMUNITY", service.Address);
                string replay = Write(root, "cancel", identity, service.Address);
                using var job = ReplayPreparationJob.File(replay);
                check(requested.Wait(TimeSpan.FromSeconds(10)), "replay map preparation reaches the configured local download handler");
                check(published.Unchanged(rooms), "map download cannot publish a library archive or runtime room before owner adoption");
                job.Dispose(); Wait(() => job.Completed, "map download cancellation");
                release.Set(); service.Complete();
                check(service.ExactPath && published.Unchanged(rooms), "canceled delayed map download uses exact hash path and leaves publication unchanged");
                check(Exclusive(replay), "canceled map download releases its detached replay reader");
            }
            using (var requested = new ManualResetEventSlim())
            using (var serve = new ManualResetEventSlim(true))
            using (var preparedReady = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var service = new PackageService(bytes, identity.PackageHash.ToString(), requested, serve))
            {
                Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_COMMUNITY", service.Address);
                string replay = Write(root, "late", identity, service.Address); string? snapshot = null;
                PreparedMapInstallation? preparedMap = null;
                using var job = ReplayPreparationJob.Start(_ =>
                {
                    // Deliberately lose the cancellation race after private
                    // verification/build. Job ownership must dispose the result.
                    var source = PreparedReplaySource.File(replay);
                    try
                    {
                        preparedMap = (PreparedMapInstallation?)typeof(PreparedReplaySource)
                            .GetField("_preparedMap", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source);
                        snapshot = (string?)typeof(PreparedMapInstallation)
                            .GetField("_snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preparedMap);
                        preparedReady.Set(); release.Wait(); return source;
                    }
                    catch { source.Dispose(); throw; }
                });
                try
                {
                    check(preparedReady.Wait(TimeSpan.FromSeconds(30)), "late completion fixture finishes real private package verification and build");
                    service.Complete();
                    var stages = Stages(preparedMap!);
                    string destination = Path.Combine(CustomRooms.UserMapDirectory, identity.MapId.ToString("N") + MapBundle.Extension);
                    check(stages.Select(stage => stage.Destination).OrderBy(path => path, StringComparer.Ordinal)
                        .SequenceEqual(outputs.Files.Append(outputs.Manifest).Append(destination).OrderBy(path => path, StringComparer.Ordinal)),
                        "prepared map stages only its exact archive and complete runtime destination set");
                    check(snapshot != null && File.Exists(snapshot) && published.Unchanged(rooms, stages),
                        "completed detached map retains only a private archive lease before adoption");
                    check(stages.All(stage => !Exclusive(stage.Path)), "prepared publication payload leases reject overwrite before adoption");
                    check(MapContentIdentity.FromPackage(stages.Single(stage => stage.Destination == destination).Path).Matches(identity),
                        "private staged archive retains the exact verified map identity");
                    string unexpected = Path.Combine(CustomRooms.UserMapDirectory, ".map-stage-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(unexpected);
                    check(!published.Unchanged(rooms, stages), "unowned hidden staging directories cannot satisfy the unpublished oracle");
                    Directory.Delete(unexpected);
                    string unexpectedPayload = Path.Combine(Path.GetDirectoryName(stages[0].Path)!, "unowned.bin");
                    File.WriteAllBytes(unexpectedPayload, [1]);
                    check(!published.Unchanged(rooms, stages), "unowned private payloads cannot satisfy the unpublished oracle");
                    File.Delete(unexpectedPayload);
                    File.WriteAllBytes(destination, bytes);
                    check(!published.Unchanged(rooms, stages), "a visible installed archive cannot satisfy the unpublished oracle before adoption");
                    File.Delete(destination);
                    byte[] previousRuntime = File.ReadAllBytes(outputs.Model);
                    File.WriteAllBytes(outputs.Model, [0]);
                    check(!published.Unchanged(rooms, stages), "changed existing runtime bytes cannot satisfy the unpublished oracle");
                    File.WriteAllBytes(outputs.Model, previousRuntime);
                    job.Dispose(); release.Set();
                    Wait(() => job.Completed && snapshot != null && !File.Exists(snapshot) && Exclusive(replay)
                        && stages.All(stage => !Directory.Exists(Path.GetDirectoryName(stage.Path))), "abandoned map result cleanup");
                    check(published.Unchanged(rooms) && snapshot != null && !File.Exists(snapshot), "obsolete completed map preparation disposes its archive without installing or registering");
                    check(stages.All(stage => !File.Exists(stage.Path) && !Directory.Exists(Path.GetDirectoryName(stage.Path))),
                        "abandoned prepared map removes every exact private publication directory and payload");
                    check(Exclusive(replay), "late map completion cleanup releases its reader lease");
                }
                finally { release.Set(); }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_COMMUNITY", community);
            CustomRooms.MapDirectory = maps; CustomRooms.UserMapDirectory = library;
            CustomRooms.GeneratedRuntimeRoot = runtime;
        }
    }
    private sealed record StageLease(string Path, string Destination);
    private static StageLease[] Stages(PreparedMapInstallation prepared)
    {
        var publication = typeof(PreparedMapInstallation).GetField("_publication", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(prepared)!;
        var files = (IEnumerable)publication.GetType().GetField("_files", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(publication)!;
        return files.Cast<object>().Select(file => new StageLease(
            (string)file.GetType().GetProperty("Stage")!.GetValue(file)!,
            (string)file.GetType().GetProperty("Destination")!.GetValue(file)!)).ToArray();
    }
    private sealed class PublicationBaseline
    {
        private readonly string[] _roots;
        private readonly HashSet<string> _entries;
        private readonly Dictionary<string, byte[]> _files;
        internal PublicationBaseline(params string[] roots)
        {
            _roots = roots.Select(Path.GetFullPath).ToArray();
            _entries = Entries();
            _files = _entries.Where(File.Exists).ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        }
        private HashSet<string> Entries() => _roots.SelectMany(root => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
            .ToHashSet(StringComparer.Ordinal);
        internal bool Unchanged(int rooms, StageLease[]? stages = null)
        {
            if (Metadata.RoomList.Count != rooms || _files.Any(file => !File.Exists(file.Key) || !File.ReadAllBytes(file.Key).SequenceEqual(file.Value))) return false;
            var expected = new HashSet<string>(_entries, StringComparer.Ordinal);
            foreach (var stage in stages ?? [])
            {
                string directory = Path.GetDirectoryName(stage.Path)!;
                string name = Path.GetFileName(directory);
                if (!name.StartsWith(".map-stage-", StringComparison.Ordinal) || !Guid.TryParseExact(name[11..], "N", out _)
                    || Path.GetDirectoryName(directory) != Path.GetDirectoryName(stage.Destination)
                    || !Directory.Exists(directory) || !File.Exists(stage.Path)
                    || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0
                    || (File.GetAttributes(stage.Path) & FileAttributes.ReparsePoint) != 0) return false;
                if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(directory)
                    != (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute)) return false;
                expected.Add(directory); expected.Add(stage.Path);
                // StageInstallation creates its validated manifest alongside the
                // first model payload before staging the final manifest entry.
                if (stage.Destination.EndsWith("_Model.bin", StringComparison.Ordinal)) expected.Add(Path.Combine(directory, "manifest.json"));
            }
            return expected.SetEquals(Entries());
        }
    }
    private static string Write(string root, string name, MapContentIdentity identity, string address)
    {
        var match = new MatchStatePacket { RoomKey = identity.RoomKey, NextRoomKey = "", Mode = (byte)GameMode.Battle,
            MatchId = 1, AuthorityEpoch = 1, PlayerCount = 8, Flags = MatchStatePacket.FlagInProgress };
        var config = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, Phase = SessionPhase.InMatch, MaxPlayers = 8,
            Revision = 1, OwnerSlot = 0, MapDownloadSource = address, WorldProfile = MphRead.Mods.Multiplayer.MatchWorldProfile.Resolve(8),
            Match = new MatchDefinition { RoomKey = identity.RoomKey, Mode = GameMode.Battle,
                MapIdentity = new(identity.MapId, identity.ContentHash, identity.PackageHash, NetworkMapFlags.Custom | NetworkMapFlags.Downloadable) } };
        byte[] m = new byte[1 + MatchStatePacket.Size]; m[0] = (byte)PacketType.MatchState; match.Write(m.AsSpan(1));
        byte[] c = new byte[1 + SessionStatePacket.Size]; c[0] = (byte)PacketType.SessionState; config.Write(c.AsSpan(1));
        string path = Path.Combine(root, name + ".ppdemo");
        using var writer = new ReplayWriterV3(path, new ReplayMetadata { RoomKey = identity.RoomKey, Mode = GameMode.Battle,
            Bootstrap = new ReplayBootstrap { Packets = [m, c] } }); writer.WriteRecord(1, m); return path;
    }
    private static bool Exclusive(string path)
    { try { using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; } catch (IOException) { return false; } }
    private static void Wait(Func<bool> complete, string operation)
    { var timer = Stopwatch.StartNew(); while (!complete()) { if (timer.Elapsed.TotalSeconds > 30) throw new TimeoutException(operation); Thread.Sleep(1); } }
    private sealed class PackageService : IDisposable
    {
        private readonly HttpListener _listener = new(); private readonly Task _task; private readonly ManualResetEventSlim _release;
        internal string Address { get; } internal bool ExactPath { get; private set; }
        internal PackageService(byte[] bytes, string hash, ManualResetEventSlim requested, ManualResetEventSlim release)
        {
            using var port = new TcpListener(IPAddress.Loopback, 0); port.Start();
            Address = "http://127.0.0.1:" + ((IPEndPoint)port.LocalEndpoint).Port + "/"; port.Stop();
            _release = release; _listener.Prefixes.Add(Address); _listener.Start();
            _task = Task.Run(async () =>
            {
                var context = await _listener.GetContextAsync(); ExactPath = context.Request.RawUrl == "/packages/" + hash;
                requested.Set(); release.Wait();
                try
                {
                    context.Response.StatusCode = 200; context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                }
                catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException) { }
                finally { context.Response.Close(); }
            });
        }
        internal void Complete() => _task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        public void Dispose() { _release.Set(); _listener.Close(); try { Complete(); } catch (HttpListenerException) { } catch (ObjectDisposedException) { } }
    }
}
