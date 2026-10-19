using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Pages.Community;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

/// <summary>Opt-in headless package qualification; does not allocate or contact a server.</summary>
internal static class ActualCommunityHostingAcceptance
{
    internal static int Run(string sourcePackage)
    {
        const string scope = "Headless actual private import, installed Community lobby request qualification, independent server-package snapshot and cleanup. No server allocation, network, launch, or GL acceptance.";
        string supplied = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
        Check(Path.IsPathFullyQualified(supplied) && Directory.Exists(supplied), "explicit private user-data root exists");
        string root = Path.GetFullPath(supplied), source = Path.GetFullPath(sourcePackage);
        Check(Path.IsPathFullyQualified(sourcePackage) && File.Exists(source), "explicit source package exists");
        Check(File.Exists(Path.Combine(root, "paths.txt")), "private owned-file configuration exists");
        string work = Path.Combine(root, "actual-host-acceptance");
        Check(!Directory.Exists(work), "hosting fixture is fresh");
        Directory.CreateDirectory(work);
        string previousRoot = GameFiles.Root, previousCwd = Directory.GetCurrentDirectory();
        string previousMaps = CustomRooms.MapDirectory, previousUserMaps = CustomRooms.UserMapDirectory;
        string previousNamespace = CustomRooms.RuntimeNamespace;
        string sourceHash = Sha(source);
        string? preparedLibrary = null;
        object? evidence = null;
        try
        {
            GameFiles.Root = root; Directory.SetCurrentDirectory(root);
            Check(GameFiles.Ready, "private owned files are configured");
            string owned = Path.GetFullPath(Paths.FileSystem);
            Check(Within(owned, root) && !Within(root, owned), "runtime filesystem is inside private owned clone");
            Check(Within(Path.GetFullPath(HostedMapRequests.CacheDirectory), root), "hosted package cache stays inside private user data");
            foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
                Check(new FileInfo(path).LinkTarget == null, "private fixture contains no symbolic links");
            CustomRooms.MapDirectory = Path.Combine(work, "empty-catalog");
            CustomRooms.UserMapDirectory = Path.Combine(work, "installed");
            CustomRooms.RuntimeNamespace = "rml93-actual-host";
            Directory.CreateDirectory(CustomRooms.MapDirectory); Directory.CreateDirectory(CustomRooms.UserMapDirectory);
            var identity = MapContentIdentity.FromPackage(source);
            Check(!Metadata.RoomMetadata.ContainsKey(identity.RoomKey), "source map starts unregistered");
            var importer = new NativeCommunityImportService();
            object context = importer.CaptureContext() ?? throw new InvalidOperationException("No private import context");
            using (var imported = importer.PrepareAsync(source, false, context, CancellationToken.None).GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("No prepared private import")) imported.Commit();
            var installed = CustomRooms.Installed;
            Check(installed.HasExact(identity) && installed.TryGet(identity.MapId, out _), "actual private import registers exact package");
            var definition = CustomRooms.Definitions.Single(d => d.MapId == identity.MapId);
            QualifyInstalledFastPath(identity, definition, root);
            var target = new NativeCommunityHostSelection(root, identity);
            var request = new LobbyCreationRequest("Private qualification", "Pilot", identity.RoomKey,
                GameMode.Battle, MatchFormat.Auto, Hunter.Samus, false, target, 4);
            Check(request.ValidateForm(out string formError), "exact package form validates: " + formError);
            Check(request.Validate(out string validationError), "installed package Battle layer validates: " + validationError);
            Check(!(request with { RoomKey = identity.RoomKey + " mismatch" }).ValidateForm(out _), "room mismatch is refused");
            Check(!(request with { CommunityMap = target with { Identity = identity with { PackageHash = new MapHash256(11, 12, 13, 14) } } }).Validate(out _),
                "stale installed archive hash is refused");
            // Fast-path owner registration republishes definitions; mutate the current published instance.
            definition = CustomRooms.Definitions.Single(d => d.MapId == identity.MapId);
            bool localOnly = definition.LocalSourceOnly;
            try
            {
                definition.LocalSourceOnly = true;
                Check(!request.Validate(out _), "registered local-source-only map is refused for online hosting");
            }
            finally { definition.LocalSourceOnly = localOnly; }
            GameFiles.Root = Path.Combine(root, "changed-root");
            Check(!request.ValidateForm(out _), "changed configured root is refused");
            GameFiles.Root = root;

            string installedPath = Path.Combine(CustomRooms.UserMapDirectory, identity.MapId.ToString("N") + ".ppmap");
            string[] before = Libraries();
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel(); bool rejected = false;
                try { LobbyCreationService.PrepareLocalPackage(request, canceled.Token); }
                catch (OperationCanceledException) { rejected = true; }
                Check(rejected && before.SequenceEqual(Libraries()), "pre-canceled package preparation leaves no library");
            }
            var prepared = LobbyCreationService.PrepareLocalPackage(request, CancellationToken.None);
            preparedLibrary = prepared.LibraryPath ?? throw new InvalidOperationException("No prepared library");
            var archive = prepared.Archives.Single();
            Check(Within(preparedLibrary, root) && archive.PackagePath != installedPath
                && archive.Identity == identity && archive.RoomKey == identity.RoomKey
                && MapContentIdentity.FromPackage(archive.PackagePath).Matches(identity)
                && Sha(archive.PackagePath) == sourceHash, "server library contains independent exact installed bytes");
            File.WriteAllText(installedPath, "private installed archive replaced after snapshot");
            Check(Sha(archive.PackagePath) == sourceHash && Sha(source) == sourceHash,
                "later private installed archive replacement cannot alter prepared server snapshot or caller source");
            HostedMapRequests.TryDeleteDirectory(preparedLibrary);
            Check(!Directory.Exists(preparedLibrary), "server package library cleanup removes snapshot");
            preparedLibrary = null;
            bool tamperedRejected = false;
            try
            {
                var unexpected = LobbyCreationService.PrepareLocalPackage(request, CancellationToken.None);
                if (unexpected.LibraryPath is { } path) HostedMapRequests.TryDeleteDirectory(path);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException)
            { tamperedRejected = true; }
            Check(tamperedRejected && before.SequenceEqual(Libraries()), "changed installed bytes reject preparation and clean partial library");
            File.Copy(source, installedPath, overwrite: true);
            Check(Sha(installedPath) == sourceHash && Sha(source) == sourceHash, "private installed archive restored and caller source preserved");
            evidence = new { identity.RoomKey, identity.MapId, contentHash = identity.ContentHash.ToString(),
                packageHash = identity.PackageHash.ToString(), installedPath, capacity = request.MaxPlayers };
            Save(true, null); return 0;
        }
        catch (Exception error) { Save(false, error.ToString()); throw; }
        finally
        {
            if (preparedLibrary != null) HostedMapRequests.TryDeleteDirectory(preparedLibrary);
            GameFiles.Root = previousRoot; CustomRooms.MapDirectory = previousMaps;
            CustomRooms.UserMapDirectory = previousUserMaps; CustomRooms.RuntimeNamespace = previousNamespace;
            Directory.SetCurrentDirectory(previousCwd);
        }
        string[] Libraries()
        {
            string directory = Path.Combine(HostedMapRequests.CacheDirectory, "native-lobbies");
            return Directory.Exists(directory) ? Directory.GetDirectories(directory).OrderBy(p => p, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
        }
        void Save(bool passed, string? error) => File.WriteAllText(Path.Combine(work, "summary.json"), JsonSerializer.Serialize(
            new { passed, scope, root, source, sourceHash, evidence, error }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static bool Within(string path, string root) => path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)
        + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    private static void QualifyInstalledFastPath(MapContentIdentity identity, MapDefinition definition, string root)
    {
        var selected = new NativeCommunityEntry(identity.MapId, definition.Name, "Private fixture",
            identity.PackageHash.ToString(), 1, "Battle", 2, 4)
        { Installed = true, RoomKey = identity.RoomKey, Version = definition.Version ?? "" };
        string? previousEndpoint = Environment.GetEnvironmentVariable("PROJECT_PRIME_MAP_COMMUNITY");
        try
        {
            // Any accidental download-path entry fails URI validation before HTTP is possible.
            Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_COMMUNITY", "not-a-community-endpoint");
            var installer = new NativeCommunityInstaller();
            var result = installer.InstallAsync(selected, CancellationToken.None, root).GetAwaiter().GetResult();
            Check(result.MapId == identity.MapId && result.PackageHash == selected.LatestHash
                && result.Identity == identity && result.Definition.MapId == identity.MapId
                && result.Definition.Name == identity.RoomKey && result.Definition.Version == definition.Version
                && result.Definition.BundlePath != null && Sha(result.Definition.BundlePath) == selected.LatestHash,
                "installed fast path returns exact package and definition without a usable HTTP endpoint");
            int count = CustomRooms.Definitions.Count(d => d.MapId == identity.MapId);
            installer.Register(result); installer.Register(result);
            Check(CustomRooms.Installed.HasExact(identity)
                && CustomRooms.Definitions.Count(d => d.MapId == identity.MapId) == count,
                "owner registration is idempotent and preserves exact installed identity");
            Refused(selected with { LatestHash = new MapHash256(11, 12, 13, 14).ToString() }, root,
                "installed fast path refuses changed selected hash");
            Refused(selected with { RoomKey = selected.RoomKey + " mismatch" }, root,
                "installed fast path refuses changed selected room");
            Refused(selected, Path.Combine(root, "retired-root"), "installed fast path refuses stale expected root");
            void Refused(NativeCommunityEntry entry, string expectedRoot, string label)
            {
                bool rejected = false;
                try { installer.InstallAsync(entry, CancellationToken.None, expectedRoot).GetAwaiter().GetResult(); }
                catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { rejected = true; }
                Check(rejected && CustomRooms.Installed.HasExact(identity), label);
            }
        }
        finally { Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_COMMUNITY", previousEndpoint); }
    }
    private static string Sha(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("ACTUAL COMMUNITY HOSTING PASS " + label); }
}
