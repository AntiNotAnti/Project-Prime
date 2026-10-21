using System;
using System.IO;
using System.Threading;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class MapSelectionPreferenceChecks
{
    internal static void Run()
    {
        string oldDirectory = LauncherPrefs.Directory;
        string oldMap = LauncherPrefs.LastSelectedMap;
        string sandbox = Path.Combine(Path.GetTempPath(),
            "project-prime-map-prefs-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(sandbox);
        try
        {
            LauncherPrefs.Directory = sandbox;
            LauncherPrefs.LastSelectedMap = "";
            string[] rooms = { "MP1 SANCTORUS", "MP3 PROVING GROUND", "CUSTOM TEST ARENA" };

            Check(LauncherPrefs.PreferredMap(rooms) == LauncherPrefs.DefaultMap,
                "Combat Hall is first-use default even when the list begins with another map");

            using (var create = new RmlUiMenuController(new Stub { BuiltInMaps = rooms }))
            {
                Check(create.Dispatch(RmlUiIntentKind.PlayCreateOpen)
                    && Text(create, "play_create_map") == LauncherPrefs.DefaultMap,
                    "new native lobby opens with Combat Hall selected");
            }

            var services = new Stub { BuiltInMaps = rooms };
            using (var controller = new RmlUiMenuController(services))
            {
                Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline)
                    && Text(controller, "offline_arena_value") == LauncherPrefs.DefaultMap,
                    "new native bot setup opens with Combat Hall selected");
                Check(controller.Dispatch(RmlUiIntentKind.OfflineOpenArena)
                    && controller.Dispatch(RmlUiIntentKind.OfflineArenaSearch, arenaSearch: "sanctorus")
                    && controller.Dispatch(RmlUiIntentKind.OfflineSelectArena, 0)
                    && Text(controller, "offline_arena_value") == "MP1 SANCTORUS"
                    && LauncherPrefs.LastSelectedMap == "MP1 SANCTORUS",
                    "changing maps updates the active draft and preferred selection");
                Check(File.ReadAllText(Path.Combine(sandbox, "launcher.txt"))
                    .Contains("last_map=MP1 SANCTORUS", StringComparison.Ordinal),
                    "explicit map selection is persisted to launcher preferences");

                Check(controller.Dispatch(RmlUiIntentKind.OfflineLaunchMatch),
                    "selected map enters offline validation");
                services.OfflineValidationPending.SetResult("");
                for (int i = 0; i < 150 && services.OfflineLaunches == 0; i++)
                {
                    controller.Tick();
                    Thread.Sleep(5);
                }
                Check(services.OfflineLaunches == 1
                    && services.LastOffline.RoomKey == "MP1 SANCTORUS"
                    && controller.OfflineSessionClosed()
                    && Text(controller, "offline_arena_value") == "MP1 SANCTORUS",
                    "return from a finished round retains the explicitly chosen map");
            }

            LauncherPrefs.LastSelectedMap = "";
            LauncherPrefs.Load();
            Check(LauncherPrefs.LastSelectedMap == "MP1 SANCTORUS",
                "saved map is restored from disk rather than only from menu memory");

            using (var reopened = new RmlUiMenuController(new Stub { BuiltInMaps = rooms }))
            {
                Check(reopened.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline)
                    && Text(reopened, "offline_arena_value") == "MP1 SANCTORUS",
                    "new menu instance restores the last chosen arena");
                Check(reopened.Dispatch(RmlUiIntentKind.PlayCreateOpen)
                    && Text(reopened, "play_create_map") == "MP1 SANCTORUS",
                    "native lobby creation also restores the saved arena");
            }

            var previous = new LaunchPlan { Kind = LaunchKind.Offline, RoomKey = "MP1 SANCTORUS" };
            Check(OfflineRematch.TryPlan(previous, null, out var unchanged)
                && unchanged.RoomKey == "MP1 SANCTORUS"
                && OfflineRematch.TryPlan(previous, LauncherPrefs.DefaultMap, out var changed)
                && changed.RoomKey == LauncherPrefs.DefaultMap,
                "offline rematches keep the selected map or apply the next-round choice");

            LauncherPrefs.RememberMap("UNINSTALLED CUSTOM MAP");
            Check(LauncherPrefs.PreferredMap(rooms) == LauncherPrefs.DefaultMap
                && LauncherPrefs.LastSelectedMap == "UNINSTALLED CUSTOM MAP",
                "a missing saved map safely falls back to Combat Hall without erasing the preference");
            var draft = new RmlUiOfflineDraft(rooms, Hunter.Samus, 0, 1, 1);
            Check(draft.Current.RoomKey == LauncherPrefs.DefaultMap,
                "the native offline draft uses the same missing-map fallback");
        }
        finally
        {
            LauncherPrefs.Directory = oldDirectory;
            LauncherPrefs.LastSelectedMap = oldMap;
            try { System.IO.Directory.Delete(sandbox, recursive: true); }
            catch (IOException) { }
        }
    }

    private static string Text(RmlUiMenuController controller, string key)
        => controller.Tick().Bindings.TryGetValue(key, out var value) ? value.Text : "";

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        Console.WriteLine("MAP PREF PASS " + label);
    }
}
