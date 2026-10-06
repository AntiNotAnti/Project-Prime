using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MphRead;
using MphRead.Mods.MapGen;

internal static class Program
{
    private const string Room = "PUBLICATION_CHECK";
    private static readonly string[] RuntimeFiles = ["model.bin", "animation.bin", "collision.bin", "entities.bin", "nodes.bin", "map.build.json"];
    private static int _assertions;

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length > 0) return await Child(args);
            string root = Path.Combine(Path.GetTempPath(), "prime-map-publication-" + Guid.NewGuid().ToString("N"));
            try { await Run(root); }
            finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            Console.WriteLine($"PASS: {_assertions} map publication assertions, including separate-process reader/install/retry/crash gates.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"HResult {ex.HResult:x8}: {ex}"); return 1; }
    }

    private static void Configure(string root)
    {
        Paths.FileSystem = Path.Combine(root, "runtime");
        Directory.CreateDirectory(Paths.FileSystem);
        Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", Path.Combine(root, "user-data"));
        CustomRooms.RuntimeNamespace = "";
    }

    private static async Task<int> Child(string[] args)
    {
        Configure(args[1]);
        if (args[0] == "--reader")
        {
            bool privateReader = args[3].StartsWith("private-", StringComparison.Ordinal);
            using var privateBytes = privateReader ? File.OpenRead(Path.Combine(args[1], "private-history", "model.bin")) : null;
            string? privateHash = privateBytes == null ? null : Hash(privateBytes);
            if (privateReader)
                MphRead.Mods.StudioReplay.StudioReplayResources.Current = new(args[2]);
            if (args[3].EndsWith("preparation", StringComparison.Ordinal))
            {
                using var preparation = MapRuntimeUsage.AcquirePreparation(args[2]);
                Console.WriteLine("READY");
                await Console.In.ReadLineAsync();
            }
            else
            {
                var scene = new Scene();
                MapRuntimeUsage.Track(scene, args[2]);
                Console.WriteLine("READY");
                await Console.In.ReadLineAsync();
                MapRuntimeUsage.Release(scene);
                GC.KeepAlive(scene);
            }
            if (privateBytes != null)
            {
                privateBytes.Position = 0;
                if (Hash(privateBytes) != privateHash) throw new InvalidDataException("Private historical room bytes changed during game publication.");
            }
            return 0;
        }
        if (args[0] != "--publish") throw new InvalidOperationException("Unknown child command.");
        string package = args[2], expected = args[3], room = args[4];
        if (Hash(package) != expected) throw new InvalidDataException("Package hash does not match the request.");
        string stageRoot = Path.Combine(args[1], ".publisher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stageRoot);
        try
        {
            using var publication = new MapFilePublication();
            string stagedPackage = publication.Stage(package, Path.Combine(args[1], "installed", "map.ppmap"), default);
            if (Hash(stagedPackage) != expected) throw new InvalidDataException("Staged package hash changed.");
            using (var archive = ZipFile.OpenRead(stagedPackage))
            {
                using var recipe = new StreamReader(archive.GetEntry("room.txt")!.Open());
                if (recipe.ReadToEnd() != room) throw new InvalidDataException("Room identity does not match the request.");
                foreach (string name in RuntimeFiles)
                {
                    string extracted = Path.Combine(stageRoot, name);
                    archive.GetEntry(name)!.ExtractToFile(extracted);
                    publication.Stage(extracted, Path.Combine(Paths.FileSystem, name), default);
                }
            }
            try
            {
                lock (MapRuntimeUsage.Gate)
                {
                    MapRuntimeUsage.RequireInstallationAllowed(room);
                    using var lease = MapPublicationLease.AcquirePublication(Paths.FileSystem, CustomRooms.RuntimeNamespace, room);
                    publication.Commit(default);
                }
            }
            catch (MapPublicationBusyException) { Console.WriteLine("DEFERRED"); return 20; }
            Console.WriteLine("INSTALLED " + Hash(Path.Combine(args[1], "installed", "map.ppmap")));
            return 0;
        }
        finally { Directory.Delete(stageRoot, recursive: true); }
    }

    private static async Task Run(string root)
    {
        Configure(root);
        AliasResolutionChecks.Run(root, Check);
        PrivateRuntimeOwnershipChecks.Run(root, Check);
        string oldPackage = CreatePackage(root, "old", 1), rebuiltPackage = CreatePackage(root, "rebuilt", 2);
        string rebuiltHash = Hash(rebuiltPackage);
        Check(Hash(oldPackage) != rebuiltHash, "rebuilt same-name immutable package has a new exact hash");
        var first = await Publish(root, oldPackage, Hash(oldPackage));
        Check(first.Exit == 0, "initial fixture installation succeeds");
        Dictionary<string, string> initial = RuntimeHashes(root);

        foreach (string mode in new[] { "preparation", "scene" })
        {
            using var reader = Start("--reader", root, Room.ToLowerInvariant(), mode);
            await Ready(reader);
            var clock = Stopwatch.StartNew();
            var deferred = await Publish(root, rebuiltPackage, rebuiltHash);
            Check(deferred.Exit == 20 && deferred.Output.Trim() == "DEFERRED", mode + " in process A defers process B installation");
            Check(clock.Elapsed < TimeSpan.FromSeconds(10), "busy publication returns promptly");
            Check(RuntimeHashes(root).OrderBy(x => x.Key).SequenceEqual(initial.OrderBy(x => x.Key)), "all existing runtime and package bytes remain identical");
            await reader.StandardInput.WriteLineAsync("release");
            await reader.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(reader.ExitCode == 0, mode + " releases normally");
            var installed = await Publish(root, rebuiltPackage, rebuiltHash);
            Check(installed.Exit == 0 && installed.Output.Trim() == "INSTALLED " + rebuiltHash, "retry installs the requested exact package hash");
            Check(Hash(Path.Combine(root, "installed", "map.ppmap")) == rebuiltHash, "game package path contains exact requested archive bytes");
            using (var archive = ZipFile.OpenRead(rebuiltPackage))
                foreach (string name in RuntimeFiles)
                {
                    using var entry = archive.GetEntry(name)!.Open();
                    Check(Hash(entry) == Hash(Path.Combine(Paths.FileSystem, name)), "installed " + name + " matches requested package output");
                }
            // Restore the earlier installed version for the next ownership scenario.
            var restored = await Publish(root, oldPackage, Hash(oldPackage));
            Check(restored.Exit == 0, "fixture version restored after lease release");
        }

        using (var reader = Start("--reader", root, Room, "scene"))
        {
            await Ready(reader);
            reader.Kill(entireProcessTree: true);
            await reader.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var afterCrash = await Publish(root, rebuiltPackage, rebuiltHash);
            Check(afterCrash.Exit == 0, "kernel releases scene lease when owning game process crashes");
        }

        string privateRuntime = Path.Combine(root, "private-history", "model.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(privateRuntime)!);
        File.WriteAllText(privateRuntime, "immutable earlier same-room historical runtime");
        string privateHash = Hash(privateRuntime);
        foreach (string mode in new[] { "private-preparation", "private-scene" })
        {
            using var reader = Start("--reader", root, Room, mode);
            await Ready(reader);
            var independent = await Publish(root, oldPackage, Hash(oldPackage));
            Check(independent.Exit == 0, mode + " consuming its scoped private same-room package does not block game publication");
            Check(Hash(privateRuntime) == privateHash, "game publication preserves private historical room bytes");
            await reader.StandardInput.WriteLineAsync("release");
            await reader.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(reader.ExitCode == 0, "private scoped reader releases independently");
        }

        using (var preparation = MapRuntimeUsage.AcquirePreparation(Room))
        {
            var scene = new Scene();
            MapRuntimeUsage.Track(scene, Room);
            Busy(() => MapPublicationLease.AcquirePublication(Paths.FileSystem, "", Room), "independent same-process readers hold OS lease");
            MapRuntimeUsage.Release(scene);
            Busy(() => MapPublicationLease.AcquirePublication(Paths.FileSystem, "", Room), "remaining preparation still protects after scene closes");
            Check(MapRuntimeUsage.IsInUse(Room), "original in-process preparation fence remains active");
            using var otherRoom = MapPublicationLease.AcquirePublication(Paths.FileSystem, "", "ANOTHER_ROOM");
            Check(true, "unrelated room has an independent publication identity");
            using var hostedRoom = MapPublicationLease.AcquirePublication(Paths.FileSystem, "isolated-host", Room);
            Check(true, "host namespace has an independent publication identity");
        }
        Check(!MapRuntimeUsage.IsInUse(Room), "in-process usage releases after last reader");
        using (var publisher = MapPublicationLease.AcquirePublication(Paths.FileSystem, "", Room))
            Busy(() => MapPublicationLease.AcquireReader(Paths.FileSystem, "", Room), "readers cannot enter during publication");
        using (var released = MapPublicationLease.AcquirePublication(Paths.FileSystem, "", Room)) Check(true, "publication releases without deleting durable lease file");
        string[] locks = Directory.GetFiles(MapPublicationLease.CoordinationDirectory, "*.lock");
        Check(locks.Length == 3, "stable installation/namespace/room lease paths remain durable");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            bool observed = false;
            try { using var lease = MapPublicationLease.AcquirePublication(Paths.FileSystem, "", Room, cancellation: cancelled.Token); }
            catch (OperationCanceledException) { observed = true; }
            Check(observed, "cancelled request does not acquire a publication lease");
        }
        if (!OperatingSystem.IsWindows())
        {
            string alias = Path.Combine(root, "runtime-alias");
            Directory.CreateSymbolicLink(alias, Paths.FileSystem);
            using var reader = MapPublicationLease.AcquireReader(alias, "", Room);
            Busy(() => MapPublicationLease.AcquirePublication(Paths.FileSystem, "", Room), "symlink aliases resolve to one installation identity");
        }
        string configuredRoot = Paths.FileSystem;
        try
        {
            Paths.FileSystem = "";
            using var policyLease = MapRuntimeUsage.AcquirePreparation(Room);
            Check(MapRuntimeUsage.IsInUse(Room), "ownership policy works before game files are configured");
            Busy(() => MapPublicationLease.AcquirePublication(AppContext.BaseDirectory, "", Room), "unconfigured policy uses the stable executable installation identity");
        }
        finally { Paths.FileSystem = configuredRoot; }
    }

    private static Process Start(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (string argument in args) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("Could not start test child process.");
    }

    private static async Task Ready(Process child)
    {
        string? ready = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        if (ready != "READY") throw new IOException("Reader failed to acquire lease: " + await child.StandardError.ReadToEndAsync());
        Check(true, "separate game reader process reports acquired lease");
    }

    private static async Task<(int Exit, string Output)> Publish(string root, string package, string hash)
    {
        using var child = Start("--publish", root, package, hash, Room);
        Task<string> output = child.StandardOutput.ReadToEndAsync(), error = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        string errors = await error;
        if (child.ExitCode is not (0 or 20)) throw new IOException("Publisher child failed: " + errors);
        return (child.ExitCode, await output);
    }

    private static string CreatePackage(string root, string name, int revision)
    {
        string path = Path.Combine(root, name + ".ppmap");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string file in RuntimeFiles.Append("room.txt"))
        {
            var entry = archive.CreateEntry(file, CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(file == "room.txt" ? Room : $"{Room} {file} exact revision {revision}");
        }
        return path;
    }

    private static Dictionary<string, string> RuntimeHashes(string root)
        => RuntimeFiles.ToDictionary(name => name, name => Hash(Path.Combine(Paths.FileSystem, name)))
            .Append(new KeyValuePair<string, string>("installed-package", Hash(Path.Combine(root, "installed", "map.ppmap"))))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    private static string Hash(string path) { using var file = File.OpenRead(path); return Hash(file); }
    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    private static void Busy(Func<MapPublicationLease> acquire, string message)
    {
        try { using var unexpected = acquire(); }
        catch (MapPublicationBusyException) { Check(true, message); return; }
        throw new InvalidOperationException(message);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _assertions++;
    }
}
