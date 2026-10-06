using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;

internal static class Program
{
    private const string Room = "GENERATION_FENCE_CHECK";
    private static int _checks;

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 0) return await Child(args);
            string root = Path.Combine(Path.GetTempPath(), "prime-real-map-generation-" + Guid.NewGuid().ToString("N"));
            try { await Run(root); }
            finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            Console.WriteLine($"PASS: {_checks} canonical map generation assertions with a real RoomEntity scene in another process.");
            return 0;
        }
        catch (MapPublicationBusyException) when (args.Length != 0) { Console.WriteLine("DEFERRED"); return 20; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Configure(string root)
    {
        Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", Path.Combine(root, "user-data"));
        Paths.SetPath(Paths.MphKey, Path.Combine(root, "runtime"));
        Directory.CreateDirectory(Paths.FileSystem);
        CustomRooms.MapDirectory = Path.Combine(root, "maps");
        CustomRooms.UserMapDirectory = Path.Combine(root, "installed");
        Directory.CreateDirectory(CustomRooms.MapDirectory);
        Headless.Enter();
    }

    private static MapOutputSet Outputs(MapDefinition definition) => MapOutputSet.Create(definition,
        CustomRooms.ArchiveDirectory(definition), CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory());

    private static async Task<int> Child(string[] args)
    {
        Configure(args[1]);
        var definition = MapDefinition.Load(args[2]);
        if (args[0] == "--hold-writer")
        {
            using var writer = MapPublicationLease.AcquirePublication(args.Length > 4 ? args[4] : Paths.FileSystem, args[3], Room);
            Console.WriteLine("READY"); await Console.In.ReadLineAsync(); return 0;
        }
        if (args[0] == "--probe-writer")
        {
            Console.WriteLine("READY"); await Console.In.ReadLineAsync();
            int entered = 0;
            for (int index = 0; index < 10000; index++)
            {
                try { using var writer = MapPublicationLease.AcquirePublication(Paths.FileSystem, "", Room); entered++; }
                catch (MapPublicationBusyException) { }
            }
            Console.WriteLine("ENTERED " + entered); return entered == 0 ? 0 : 1;
        }
        if (args[0] == "--reader")
        {
            if (args[3] == "preparation")
            {
                using var reader = (IDisposable)typeof(MapRuntimeUsage).GetMethod("AcquirePreparation", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [Room])!;
                Console.WriteLine("READY");
                await Console.In.ReadLineAsync();
                return 0;
            }
            typeof(Metadata).GetMethod("RegisterDownloadedMap", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [definition]);
            var metadata = Metadata.GetRoomByName(Room).Item1 ?? throw new IOException("Generated room metadata was not registered.");
            var scene = new Scene(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, initializeRuntime: false);
            var room = new RoomEntity(scene);
            // This is the production scene admission boundary and canonical
            // generated model/collision reader, with GPU work suppressed only
            // by the game's normal headless policy.
            _ = SceneSetup.SetUpRoom(GameMode.Battle, 0, BossFlags.Unspecified, -1, 0, metadata, room, scene, isRoomTransition: false);
            if (room.RoomCollision.Count == 0 || Read.GetRoomModelInstance(Room).Model.Meshes.Count == 0)
                throw new IOException("The real scene did not decode generated room resources.");
            Console.WriteLine("READY");
            var track = typeof(MapRuntimeUsage).GetMethod("Track", BindingFlags.Static | BindingFlags.NonPublic)!
                .CreateDelegate<Action<Scene, string>>();
            string? command;
            while ((command = await Console.In.ReadLineAsync()) != null && command != "release")
            {
                if (command == "repeat")
                {
                    for (int index = 0; index < 10000; index++) track(scene, Room);
                    Console.WriteLine("REPEATED");
                }
                else if (command.StartsWith("namespace ", StringComparison.Ordinal) || command.StartsWith("root ", StringComparison.Ordinal))
                {
                    string oldRoot = Paths.FileSystem, oldNamespace = CustomRooms.RuntimeNamespace;
                    try
                    {
                        if (command.StartsWith("namespace ", StringComparison.Ordinal)) CustomRooms.RuntimeNamespace = command[10..];
                        else Paths.SetPath(Paths.MphKey, command[5..]);
                        track(scene, Room); Console.WriteLine("SWITCH_ENTERED");
                    }
                    catch (MapPublicationBusyException) { Console.WriteLine("SWITCH_DEFERRED"); }
                    finally { CustomRooms.RuntimeNamespace = oldNamespace; Paths.SetPath(Paths.MphKey, oldRoot); }
                }
                else throw new ArgumentException("Unknown reader fixture command.");
            }
            scene.DoCleanup();
            GC.KeepAlive(room);
            return 0;
        }
        if (args[0] != "--generate") throw new ArgumentException("Unknown generation fixture command.");
        string mode = args[3];
        try
        {
            if (mode == "missing")
            {
                CustomRooms.GenerateMissing(Room);
                if (CustomRooms.WhyUnplayable(Room) != null) return 20;
            }
            else if (mode == "command")
            {
                if (MapCommands.Run("mapbuild", args[2], output: null) != 0) return 20;
            }
            else if (mode == "raw" || mode == "private")
            {
                var compiled = MapCompiler.Compile(definition);
                MapCompiler.ThrowIfInvalid(compiled.Validation);
                if (compiled.Map == null) throw new IOException("Canonical compiler returned no map.");
                if (mode == "private")
                {
                    string privateRoot = Path.Combine(args[1], "private-output");
                    MapPacker.Generate(compiled.Map, privateRoot, privateRoot, privateRoot, verbose: false);
                }
                else MapPacker.Generate(compiled.Map, CustomRooms.ArchiveDirectory(definition),
                    CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory(), verbose: false);
            }
            else if (mode == "recipe")
                MapPacker.Generate(definition, CustomRooms.ArchiveDirectory(definition),
                    CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory(), verbose: false);
            else if (mode == "install" || mode == "alias" || mode == "mixed")
            {
                var build = await MapBuildScheduler.Shared.BuildAsync(MapBuildSnapshot.Capture(definition));
                MapCompiler.ThrowIfInvalid(build.Validation());
                string archive = CustomRooms.ArchiveDirectory(definition);
                if (mode == "alias") archive = Path.Combine(args[1], "runtime-alias", "_archives", Room.ToLowerInvariant());
                string nodes = mode == "mixed" ? Path.Combine(args[1], "private-nodes") : CustomRooms.NodeDirectory();
                MapBuildScheduler.Install(build, definition, archive, CustomRooms.EntityDirectory(), nodes);
            }
            else throw new ArgumentException("Unknown generation mode.");
        }
        catch (MapPublicationBusyException) { Console.WriteLine("DEFERRED"); return 20; }
        catch (IOException) when (mode == "mixed") { Console.WriteLine("INVALID_DESTINATION"); return 21; }
        Console.WriteLine("GENERATED");
        return 0;
    }

    private static async Task Run(string root)
    {
        Configure(root);
        string texturePath = Path.Combine(root, "tile.tex");
        using (var texture = new BinaryWriter(File.Create(texturePath)))
        {
            texture.Write(Encoding.ASCII.GetBytes("FPTX")); texture.Write((ushort)1); texture.Write((ushort)1);
            texture.Write((ushort)0); texture.Write((ushort)8); texture.Write((ushort)8); texture.Write((ushort)1);
            texture.Write((ushort)0); texture.Write((ushort)32767); texture.Write(new byte[64]);
        }
        var recipe = new MapDefinition { FormatVersion = 2, MapId = Guid.NewGuid(), Name = Room, Version = "1", BaseDirectory = root };
        recipe.Materials.Add(new() { Texture = "tile.tex" }); recipe.Assets.Add(new() { Path = "tile.tex" });
        recipe.Geometry.Add(new MapBox { Transform = new() { Position = [0f, -1, 0], Scale = [8f, 1, 8] } });
        recipe.Spawns.Add(new() { Position = [0f, 2, 0] });
        string firstPackage = MapPackageBuilder.Build(recipe, Path.Combine(root, "first.ppmap"));
        recipe.Version = "2"; recipe.Geometry[0].Transform.Scale = [10f, 1, 8];
        string nextPackage = MapPackageBuilder.Build(recipe, Path.Combine(root, "next.ppmap"));
        string packageHash = Hash(nextPackage);
        File.Copy(nextPackage, Path.Combine(CustomRooms.MapDirectory, "desired.ppmap"));
        var first = MapDefinition.Load(firstPackage); var next = MapDefinition.Load(nextPackage);
        string expectedRoot = Path.Combine(root, "expected-private");
        MapPacker.Generate(next, expectedRoot, expectedRoot, expectedRoot, verbose: false);
        var expected = MapOutputSet.Create(next, expectedRoot, expectedRoot, expectedRoot);
        // The canonical spawn reader loads its editor placeholder even without
        // graphics. Supply a second copy of our own generated model so this
        // real scene fixture remains independent of extracted game assets.
        string placeholderDirectory = Path.Combine(Paths.FileSystem, "models");
        Directory.CreateDirectory(placeholderDirectory);
        File.Copy(expected.Model, Path.Combine(placeholderDirectory, "pick_wpn_missile_Model.bin"));
        Check(Hash(firstPackage) != packageHash, "real immutable packages have distinct exact hashes");

        foreach (string readerMode in new[] { "scene", "preparation" })
        foreach (string generationMode in new[] { "install", "recipe", "raw", "command", "missing" })
        {
            MapPacker.Generate(first, CustomRooms.ArchiveDirectory(first), CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory(), verbose: false);
            var before = Hashes(Outputs(first));
            using var reader = Start("--reader", root, firstPackage, readerMode);
            await Ready(reader);
            var elapsed = Stopwatch.StartNew();
            var rejected = await Generate(root, nextPackage, generationMode);
            Check(rejected == 20, readerMode + " rejects actual " + generationMode + " generation in a separate process");
            Check(elapsed.Elapsed < TimeSpan.FromSeconds(15), "live reader rejection is bounded without an upgrade wait");
            Check(before.SequenceEqual(Hashes(Outputs(first))), "every model/animation/collision/entity/node/manifest byte stays identical on rejection");
            Check(Hash(nextPackage) == packageHash, "generation preserves the exact submitted immutable package");
            await reader.StandardInput.WriteLineAsync("release");
            await reader.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Check(reader.ExitCode == 0, "real reader closes its scene/preparation normally");
            Check(await Generate(root, nextPackage, generationMode) == 0, "released reader permits canonical generation retry");
            Check(MapBuildManifest.IsCurrent(next, Outputs(next)), "retry commits complete outputs with the exact desired source fingerprint");
            Check(Hashes(expected).SequenceEqual(Hashes(Outputs(next))), "retry runtime bytes equal all privately compiled expected bytes");
        }

        MapPacker.Generate(first, CustomRooms.ArchiveDirectory(first), CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory(), verbose: false);
        var activeBytes = Hashes(Outputs(first));
        using (var scene = Start("--reader", root, firstPackage, "scene"))
        {
            await Ready(scene);
            Check(await Generate(root, nextPackage, "private") == 0, "same-room private canonical compilation remains available beside an active game scene");
            Check(activeBytes.SequenceEqual(Hashes(Outputs(first))), "private build preserves every active game runtime byte");
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(Path.Combine(root, "runtime-alias"), Paths.FileSystem);
                Check(await Generate(root, nextPackage, "alias") == 20, "runtime directory alias shares the real scene publication fence");
            }
            Check(await Generate(root, nextPackage, "mixed") == 21, "mixed game/private output set fails before publication");
            Check(activeBytes.SequenceEqual(Hashes(Outputs(first))), "alias/mixed rejection preserves every runtime byte");
            scene.Kill(entireProcessTree: true);
            await scene.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        Check(await Generate(root, nextPackage, "install") == 0, "kernel releases actual scene ownership after process termination");

        using (var scene = Start("--reader", root, nextPackage, "scene"))
        {
            await Ready(scene);
            using (var nextNamespace = Start("--hold-writer", root, nextPackage, "next-host"))
            {
                await Ready(nextNamespace);
                await scene.StandardInput.WriteLineAsync("namespace next-host");
                Check(await scene.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) == "SWITCH_DEFERRED", "same-room new host namespace uses a distinct admission identity");
                Busy(() => MapPublicationLease.AcquirePublication(Paths.FileSystem, "", Room), "failed namespace admission retains the current scene's shared lease");
                await nextNamespace.StandardInput.WriteLineAsync("release"); await nextNamespace.WaitForExitAsync();
            }
            string nextRoot = Path.Combine(root, "another-runtime"); Directory.CreateDirectory(nextRoot);
            using (var changedRoot = Start("--hold-writer", root, nextPackage, "", nextRoot))
            {
                await Ready(changedRoot);
                await scene.StandardInput.WriteLineAsync("root " + nextRoot);
                Check(await scene.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) == "SWITCH_DEFERRED", "same-room new physical runtime root uses a distinct admission identity");
                Busy(() => MapPublicationLease.AcquirePublication(Paths.FileSystem, "", Room), "failed runtime-root admission retains old model/collision ownership");
                await changedRoot.StandardInput.WriteLineAsync("release"); await changedRoot.WaitForExitAsync();
            }
            using (var writerProbe = Start("--probe-writer", root, nextPackage))
            {
                await Ready(writerProbe);
                await writerProbe.StandardInput.WriteLineAsync("probe");
                await scene.StandardInput.WriteLineAsync("repeat");
                Check(await scene.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)) == "REPEATED", "real scene repeatedly tracks the identical source without changing ownership");
                string? result = await writerProbe.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
                await writerProbe.WaitForExitAsync();
                Check(result == "ENTERED 0" && writerProbe.ExitCode == 0, "ten thousand independent writer attempts never enter during repeated same-source scene tracking");
            }
            await scene.StandardInput.WriteLineAsync("release"); await scene.WaitForExitAsync();
            Check(scene.ExitCode == 0, "identity transition fixture closes its real scene normally");
        }

        // Poisoning fixture collision bytes while a production writer is held
        // proves admission checks that lease before attempting collision decode.
        // The complete old set is restored before releasing the writer.
        var outputSet = Outputs(next); var saved = outputSet.Files.Append(outputSet.Manifest).ToDictionary(path => path, File.ReadAllBytes);
        using (var writer = Start("--hold-writer", root, nextPackage, ""))
        {
            await Ready(writer); File.WriteAllText(outputSet.Collision, "intentionally unreadable fixture collision during exclusive publication");
            using var reader = Start("--reader", root, nextPackage, "scene");
            Task<string> output = reader.StandardOutput.ReadToEndAsync(), error = reader.StandardError.ReadToEndAsync();
            await reader.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Check(reader.ExitCode == 20 && (await output).Contains("DEFERRED", StringComparison.Ordinal)
                && string.IsNullOrEmpty(await error), "scene admission defers before reading poisoned collision/model bytes while another process publishes");
            foreach (var file in saved) File.WriteAllBytes(file.Key, file.Value);
            await writer.StandardInput.WriteLineAsync("release"); await writer.WaitForExitAsync();
        }
        Check(MapBuildManifest.IsCurrent(next, Outputs(next)), "complete runtime set stays exact after writer admission barrier fixture");

        // A fresh prewarm must be able to generate before taking its reader,
        // rather than attempting an exclusive upgrade while holding that reader.
        MapPacker.Generate(first, CustomRooms.ArchiveDirectory(first), CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory(), verbose: false);
        typeof(Metadata).GetMethod("RegisterDownloadedMap", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [next]);
        Check(RoomPrewarm.Begin(Room), "fresh desired room starts real prewarm");
        bool ready = await Task.Run(() => RoomPrewarm.JoinForLoad(Room)).WaitAsync(TimeSpan.FromSeconds(15));
        Check(ready && MapBuildManifest.IsCurrent(next, Outputs(next)), "prewarm generates then holds shared admission and validates desired complete outputs without deadlock");
        RoomPrewarm.Clear();
    }

    private static string[] Hashes(MapOutputSet outputs) => outputs.Files.Append(outputs.Manifest).Select(Hash).ToArray();
    private static string Hash(string path) { using var source = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(source)); }
    private static Process Start(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (string arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new IOException("Could not start canonical generation fixture child.");
    }
    private static async Task Ready(Process reader)
    {
        string? line;
        do { line = await reader.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        while (line != null && line != "READY");
        if (line == null) throw new IOException("Actual scene/preparation failed: " + await reader.StandardError.ReadToEndAsync());
        Check(true, "separate canonical scene/preparation acquired its live reader lease");
    }
    private static async Task<int> Generate(string root, string package, string mode)
    {
        using var child = Start("--generate", root, package, mode);
        Task<string> output = child.StandardOutput.ReadToEndAsync(), errors = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        if (child.ExitCode is not (0 or 20 or 21)) throw new IOException("Actual generation failed: " + await errors + await output);
        return child.ExitCode;
    }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _checks++; Console.WriteLine("PASS " + description);
    }
    private static void Busy(Func<MapPublicationLease> acquire, string description)
    {
        try { using var writer = acquire(); }
        catch (MapPublicationBusyException) { Check(true, description); return; }
        throw new InvalidOperationException(description);
    }
}
