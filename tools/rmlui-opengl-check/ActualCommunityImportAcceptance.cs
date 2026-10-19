using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Pages.Community;
using MphRead.Mods.MapGen;

/// <summary>Explicit opt-in real importer against a caller-provided independent private owned-file clone.</summary>
internal static class ActualCommunityImportAcceptance
{
    internal static int Run(string sourcePackage)
    {
        const string scope = "Actual NativeCommunityImportService/controller, private mutable-source snapshot, exact installation, owner registration and five runtime outputs; explicit independent owned-file clone inside fresh private user data. No picker, HTTP, canonical writes or GL.";
        string supplied = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
        Check(Path.IsPathFullyQualified(supplied) && Directory.Exists(supplied), "explicit absolute private user-data directory exists");
        string root = Path.GetFullPath(supplied), source = Path.GetFullPath(sourcePackage);
        Check(Path.IsPathFullyQualified(sourcePackage) && File.Exists(source), "explicit absolute source package exists");
        Check(File.Exists(Path.Combine(root, "paths.txt")), "private user data supplies configured owned files");
        string work = Path.Combine(root, "actual-import-acceptance");
        Check(!Directory.Exists(work), "actual import case is fresh and never overwrites a prior run");
        Directory.CreateDirectory(work);
        string previousRoot = GameFiles.Root, previousCwd = Directory.GetCurrentDirectory();
        string previousMaps = CustomRooms.MapDirectory, previousUserMaps = CustomRooms.UserMapDirectory;
        string previousNamespace = CustomRooms.RuntimeNamespace;
        var evidence = new List<object>();
        string sourceHash = Sha(source);
        try
        {
            GameFiles.Root = root; Directory.SetCurrentDirectory(root);
            Check(GameFiles.Ready, "private configured game files are ready");
            string owned = Path.GetFullPath(Paths.FileSystem);
            Check(Within(owned, root) && !Within(root, owned), "all configured runtime writes remain inside private owned clone");
            foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
                CheckNoLink(path);
            CustomRooms.MapDirectory = Path.Combine(work, "empty-catalog");
            CustomRooms.UserMapDirectory = Path.Combine(work, "installed");
            CustomRooms.RuntimeNamespace = "rml87-actual-import";
            Directory.CreateDirectory(CustomRooms.MapDirectory); Directory.CreateDirectory(CustomRooms.UserMapDirectory);
            var identity = MapContentIdentity.FromPackage(source);
            var definition = MapDefinition.Load(source);
            string[] outputs = MapOutputSet.Create(definition, CustomRooms.ArchiveDirectory(definition),
                CustomRooms.EntityDirectory(), CustomRooms.NodeDirectory()).Files.ToArray();
            Check(outputs.Length == 5 && outputs.All(p => Within(Path.GetFullPath(p), root)), "five destination runtime outputs are private");
            Check(outputs.All(p => !File.Exists(p)), "fresh runtime namespace has no published map outputs");
            Check(!Metadata.RoomMetadata.ContainsKey(identity.RoomKey), "private catalog has no registered source map before import");
            string mutable = Path.Combine(work, "mutable-source.ppmap"); File.Copy(source, mutable);
            var service = new NativeCommunityImportService();
            object context = service.CaptureContext() ?? throw new InvalidOperationException("Private game file context unavailable");
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel(); bool rejected = false;
                try { using var ignored = service.PrepareAsync(mutable, false, context, canceled.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { rejected = true; }
                Check(rejected && !Directory.EnumerateFiles(CustomRooms.UserMapDirectory).Any() && outputs.All(p => !File.Exists(p)),
                    "pre-canceled real preparation publishes neither archive nor runtime files");
            }
            int owner = Environment.CurrentManagedThreadId;
            using (var controller = new NativeCommunityImportController(service))
            {
                Check(controller.Prepare("\"" + mutable + "\"", false), "real service accepts an explicitly quoted private source path");
                var review = Wait(controller, s => !s.Busy);
                Check(review.CanConfirm && review.PackageHash == sourceHash && review.Name == identity.RoomKey,
                    "actual prepared review preserves exact source package hash and runtime name");
                Check(!Directory.EnumerateFiles(CustomRooms.UserMapDirectory).Any() && outputs.All(p => !File.Exists(p))
                    && !Metadata.RoomMetadata.ContainsKey(identity.RoomKey),
                    "real preparation creates no installed archive runtime output or metadata registration before confirmation");
                File.WriteAllText(mutable, "Deliberately replaced private caller copy after review");
                string modifiedHash = Sha(mutable);
                Check(modifiedHash != sourceHash && Sha(source) == sourceHash, "only private mutable source changes after review");
                Check(Environment.CurrentManagedThreadId == owner && controller.Confirm(), "explicit owner confirmation commits the captured private snapshot");
                Check(controller.Tick().Imported && !controller.Confirm(), "completed real import cannot publish twice");
                string installed = Path.Combine(CustomRooms.UserMapDirectory, identity.MapId.ToString("N") + ".ppmap");
                Check(File.Exists(installed) && Sha(installed) == sourceHash && MapContentIdentity.FromPackage(installed).Matches(identity),
                    "installed archive matches original reviewed snapshot despite later caller-source replacement");
                Check(CustomRooms.Installed.HasExact(identity) && CustomRooms.Definitions.Any(d => d.MapId == identity.MapId)
                    && Metadata.RoomMetadata.ContainsKey(identity.RoomKey),
                    "real owner commit registers exact map in installed registry and gameplay metadata");
                Check(outputs.All(File.Exists) && !CustomRooms.NeedsGenerating(CustomRooms.Definitions.Single(d => d.MapId == identity.MapId)),
                    "real installation publishes all five validated runtime outputs");
                evidence.Add(new { review.Name, review.Version, review.PackageHash, sourceHash, modifiedHash, installed,
                    installedHash = Sha(installed), ownerThread = owner,
                    runtime = outputs.Select(p => new { path = p, bytes = new FileInfo(p).Length, sha256 = Sha(p) }).ToArray() });
                var installedBefore = Inventory(CustomRooms.UserMapDirectory);
                var runtimeBefore = outputs.Select(Sha).ToArray();
                Check(controller.Prepare(source, false), "real package can be prepared for root-retirement qualification");
                Check(Wait(controller, s => !s.Busy).CanConfirm, "second real preparation reaches review");
                GameFiles.Root = Path.Combine(root, "retired-root-not-configured");
                var retired = controller.Tick();
                Check(!retired.CanConfirm && !retired.Imported && !controller.Confirm(), "configured root change retires real preparation and blocks commit");
                GameFiles.Root = root;
                Check(installedBefore.SequenceEqual(Inventory(CustomRooms.UserMapDirectory)) && runtimeBefore.SequenceEqual(outputs.Select(Sha)),
                    "retired real preparation cannot alter installed archive or runtime output bytes");
            }
            Check(Sha(source) == sourceHash && File.ReadAllText(mutable).StartsWith("Deliberately replaced", StringComparison.Ordinal),
                "disposal preserves original caller package and the intentionally modified private source");
            Save(true, null);
            return 0;
        }
        catch (Exception error) { Save(false, error.ToString()); throw; }
        finally
        {
            GameFiles.Root = previousRoot; CustomRooms.MapDirectory = previousMaps;
            CustomRooms.UserMapDirectory = previousUserMaps; CustomRooms.RuntimeNamespace = previousNamespace;
            Directory.SetCurrentDirectory(previousCwd);
        }
        void Save(bool passed, string? error) => File.WriteAllText(Path.Combine(work, "summary.json"), JsonSerializer.Serialize(
            new { passed, scope, userData = root, source, sourceHash, evidence, error }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static NativeCommunityImportController.View Wait(NativeCommunityImportController controller,
        Func<NativeCommunityImportController.View, bool> ready)
    {
        var timer = Stopwatch.StartNew();
        do { var view = controller.Tick(); if (ready(view)) return view; Thread.Sleep(10); }
        while (timer.Elapsed < TimeSpan.FromSeconds(120));
        throw new TimeoutException("Actual Community import did not complete within two minutes");
    }
    private static string Sha(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static string[] Inventory(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .OrderBy(p => p, StringComparer.Ordinal).Select(p => Path.GetRelativePath(root, p) + ":" + Sha(p)).ToArray();
    private static bool Within(string path, string root) => path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)
        + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    private static void CheckNoLink(string path)
    { if (new FileInfo(path).LinkTarget != null) throw new InvalidOperationException("Private owned fixture must contain no symbolic links"); }
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("ACTUAL COMMUNITY IMPORT PASS " + label); }
}
