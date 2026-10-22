using System.Reflection;
using Avalonia.Threading;
using MphRead;
using MphRead.Editor;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using MphRead.Utility;
using OpenTK.Mathematics;

// Reproduce first-run extraction after the front screen already exists. Only
// generated entity data is needed; no ROM, cartridge assets, GL or network.
internal static class OfflineArenaCatalogChecks
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run()
    {
        string root = Directory.CreateTempSubdirectory("prime-arena-catalog-").FullName;
        string cwd = Directory.GetCurrentDirectory();
        string? oldUser = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        string oldRoot = GameFiles.Root, oldPrefs = LauncherPrefs.Directory;
        var oldThumbnail = ThumbnailHost.Current;
        var frontField = typeof(Shell).GetField("_front", PrivateStatic)!;
        var roomsField = typeof(Shell).GetField("_rooms", PrivateStatic)!;
        object? oldFront = frontField.GetValue(null), oldRooms = roomsField.GetValue(null);
        StartScreen? front = null;
        int checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("ARENA CATALOG: " + message);
            checks++; Console.WriteLine("ARENA CATALOG PASS " + message);
        }
        try
        {
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", root);
            Directory.SetCurrentDirectory(root);
            GameFiles.Root = root; LauncherPrefs.Directory = root;
            ThumbnailHost.Current = new NoThumbnails();
            Check(!GameFiles.Ready, "fresh install begins without configured cartridge files");
            Check(GuiLauncher.EnsureSetup(requireDisplay: false), "headless production front screen initializes");
            string[] startupRooms = { "PRIME AIM LAB" };
            Dispatcher.UIThread.Invoke(() =>
            {
                front = new StartScreen(new MenuSettings(), startupRooms);
                typeof(StartScreen).GetField("_nativeStartupPending", PrivateInstance)!.SetValue(front, false);
            });
            frontField.SetValue(null, front); roomsField.SetValue(null, startupRooms);
            using var services = new RmlUiLiveMenuServices();
            Check(services.BuiltInArenaKeys().Count == 0 && services.OfflineArenaKeys().Count == 0,
                "pre-extraction startup catalog has no installed native arenas");

            string assets = Path.Combine(root, "generated-extraction");
            Directory.CreateDirectory(assets);
            byte[] entities = Repack.PackEntities(new EntityEditorBase[]
            {
                new PlayerSpawnEntityEditor { Id = 1, LayerMask = ushort.MaxValue,
                    Active = true, Up = Vector3.UnitY, Facing = Vector3.UnitZ, NodeName = "rmMain" }
            });
            string[] arenas = { "MP1 SANCTORUS", "MP3 PROVING GROUND" };
            foreach (string arena in arenas)
            {
                string path = Paths.Combine(assets, Metadata.RoomMetadata[arena].EntityPath!);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, entities);
            }
            File.WriteAllLines(Path.Combine(root, "paths.txt"), new[] { "0.35.1.0", "AMHE1=" + assets, "Export=" });
            Check(GameFiles.Ready, "generated extraction becomes ready in the same running launcher");
            Dispatcher.UIThread.Invoke(() => front!.RmlUiGameFilesCompleted());
            Check(arenas.All(arena => front!.RmlUiRoomKeys.Contains(arena)),
                "setup completion refreshes the existing front-screen catalog");
            Check(ReferenceEquals(roomsField.GetValue(null), startupRooms),
                "regression retains the old Shell snapshot to expose stale native adapters");
            Check(arenas.All(arena => services.BuiltInArenaKeys().Contains(arena)),
                "online native arena catalog immediately sees newly extracted arenas");
            Check(arenas.All(arena => services.OfflineArenaKeys().Contains(arena)),
                "bot lobby immediately sees newly extracted arenas without restarting");
            var draft = new RmlUiOfflineDraft(services.OfflineArenaKeys(), Hunter.Samus, 0, 2, 1);
            Check(draft.Valid && draft.OpenArena() && draft.SearchArena("Data Shrine")
                && draft.ArenaCount == 1 && draft.ArenaChoice(0) == "MP1 SANCTORUS",
                "native offline picker uses the repaired catalog and player-facing arena names");

            string removed = Paths.Combine(assets, Metadata.RoomMetadata[arenas[0]].EntityPath!);
            File.Delete(removed);
            Check(!services.OfflineArenaKeys().Contains(arenas[0]) && services.OfflineArenaKeys().Contains(arenas[1]),
                "removed entity data is still excluded without hiding other installed arenas");
            Dispatcher.UIThread.Invoke(() => front!.RmlUiGameFilesCompleted());
            Check(!services.BuiltInArenaKeys().Contains(arenas[0]) && services.BuiltInArenaKeys().Contains(arenas[1]),
                "another setup refresh replaces the native catalog rather than retaining old arenas");
            File.WriteAllBytes(removed, entities);
            Dispatcher.UIThread.Invoke(() => front!.RmlUiGameFilesCompleted());
            Check(arenas.All(arena => services.OfflineArenaKeys().Contains(arena)),
                "repairing the arena restores it through the existing setup-completion owner");

            CheckCustomPackage(front!, services, root, Check);

            // Optional read-only validation against an operator's existing
            // extraction. Only paths.txt is copied into this private test root.
            if (Environment.GetEnvironmentVariable("PROJECT_PRIME_ARENA_CATALOG_OWNED_PATHS") is { Length: > 0 } owned)
            {
                owned = Path.GetFullPath(owned);
                string owner = Path.GetDirectoryName(owned)!;
                string[] configured = File.ReadAllLines(owned).Select(line =>
                {
                    int split = line.IndexOf('=');
                    if (split < 0) return line;
                    string key = line[..split].Trim(), value = line[(split + 1)..].Trim();
                    return key + "=" + (key == "Export" ? Path.Combine(root, "exports")
                        : value.Length == 0 ? "" : Path.GetFullPath(value, owner));
                }).ToArray();
                File.WriteAllLines(Path.Combine(root, "paths.txt"), configured);
                Check(GameFiles.Ready, "owned extraction is available through private copied path configuration");
                Dispatcher.UIThread.Invoke(() => front!.RmlUiGameFilesCompleted());
                string[] installed = ThumbnailGenerator.MultiplayerRooms().Where(Metadata.IsBuiltInRoom).ToArray();
                Check(installed.Length > 0 && installed.All(arena => services.BuiltInArenaKeys().Contains(arena))
                    && installed.All(arena => services.OfflineArenaKeys().Contains(arena)),
                    $"actual owned extraction exposes all {installed.Length} installed native arenas to online and bot menus");
            }
            Console.WriteLine($"ARENA CATALOG PASS {checks} checks; generated entity data, isolated preferences, no GL or transport.");
        }
        finally
        {
            frontField.SetValue(null, oldFront); roomsField.SetValue(null, oldRooms);
            front?.Dispose(); ThumbnailHost.Current = oldThumbnail;
            GameFiles.Root = oldRoot; LauncherPrefs.Directory = oldPrefs;
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", oldUser);
            Directory.SetCurrentDirectory(cwd);
        }
    }

    private static void CheckCustomPackage(StartScreen front, RmlUiLiveMenuServices services,
        string root, Action<bool, string> check)
    {
        var idsField = typeof(Metadata).GetField("_roomIds", PrivateStatic)!;
        var definitionsField = typeof(CustomRooms).GetField("_definitions", PrivateStatic)!;
        var installedField = typeof(CustomRooms).GetField("_installed", PrivateStatic)!;
        var localField = typeof(Shell).GetField("_offlineLocalSourceRooms", PrivateStatic)!;
        object? oldIds = idsField.GetValue(null), oldDefinitions = definitionsField.GetValue(null),
            oldInstalled = installedField.GetValue(null), oldLocal = localField.GetValue(null);
        var oldList = Metadata.RoomList;
        var oldMetadata = Metadata.RoomMetadata;
        string[] oldRooms = front.RmlUiRoomKeys;
        try
        {
            var authored = MapTemplates.Create("OFFLINE PACKAGE CHECK", true).Definition;
            authored.InGameName = "Installed Custom Arena";
            string package = MapPackageBuilder.Build(authored, Path.Combine(root, "offline-check.ppmap"));
            var installed = MapDefinition.Load(package);
            var privateMap = MapTemplates.Create("OFFLINE PRIVATE CHECK", true).Definition;
            privateMap.LocalSourceOnly = true;
            const string unknown = "OFFLINE UNKNOWN CHECK";
            Dispatcher.UIThread.Invoke(() =>
            {
                Metadata.RegisterDownloadedMap(installed);
                Metadata.RegisterDownloadedMap(privateMap);
                front.RefreshDeferredRooms(ThumbnailGenerator.MultiplayerRooms().Append(unknown).ToArray());
            });
            check(installed.BundlePath == package && !ThumbnailGenerator.HasInstalledEntityFile(installed.Name),
                "installed package fixture has no generated runtime entities");
            check(front.RmlUiRoomKeys.Contains(installed.Name) && !services.BuiltInArenaKeys().Contains(installed.Name),
                "custom package is registered in the current front catalog without becoming built-in");
            check(services.OfflineArenaKeys().Contains(installed.Name),
                "installed custom package appears in the offline catalog before runtime generation");
            var draft = new RmlUiOfflineDraft(services.OfflineArenaKeys(), Hunter.Samus, 0, 2, 1);
            check(draft.OpenArena() && draft.SearchArena("Installed Custom Arena")
                && draft.ArenaCount == 1 && draft.ArenaChoice(0) == installed.Name
                && draft.SelectArena(0) && draft.Current.RoomKey == installed.Name && draft.Valid,
                "offline picker finds the installed custom display name and selects its exact runtime key");
            check(!ThumbnailGenerator.HasInstalledEntityFile(installed.Name),
                "enumerating and selecting custom maps does not compile runtime entities");
            check(!services.OfflineArenaKeys().Contains(privateMap.Name) && !services.OfflineArenaKeys().Contains(unknown),
                "unqualified private source and unknown room hints remain excluded");
            localField.SetValue(null, new[] { privateMap.Name });
            check(services.OfflineArenaKeys().Contains(privateMap.Name),
                "existing qualified local-source admission remains available");
        }
        finally
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                idsField.SetValue(null, oldIds);
                typeof(Metadata).GetProperty(nameof(Metadata.RoomList))!.SetValue(null, oldList);
                typeof(Metadata).GetProperty(nameof(Metadata.RoomMetadata))!.SetValue(null, oldMetadata);
                definitionsField.SetValue(null, oldDefinitions);
                installedField.SetValue(null, oldInstalled);
                localField.SetValue(null, oldLocal);
                front.RefreshDeferredRooms(oldRooms);
            });
        }
    }

    private sealed class NoThumbnails : IThumbnailHost
    {
        public Task<int> RenderAsync(IReadOnlyList<string> rooms, Action<string> report) => Task.FromResult(0);
    }
}
