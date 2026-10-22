using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class OfflineReturnChecks
{
    internal static void Run()
    {
        var services = new Stub();
        using var controller = new RmlUiMenuController(services);

        Check(!controller.OfflineSessionClosed(), "no offline scene cannot fabricate a launch return");
        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline)
            && controller.Dispatch(RmlUiIntentKind.OfflineChoice, 5),
            "bot setup opens with an editable Hunter selection");
        string hunter = Text(controller, "offline_hunter_value");

        services.OfflineValidationPending = Pending();
        Check(controller.Dispatch(RmlUiIntentKind.OfflineLaunchMatch)
            && !controller.Dispatch(RmlUiIntentKind.OfflineLaunchMatch)
            && !controller.OfflineSessionClosed() && !Flag(controller, "offline_can_start"),
            "pending map validation cannot be mistaken for a completed match");

        services.OfflineValidationPending.SetResult("");
        WaitFor(controller, () => services.OfflineLaunches == 1);
        var first = services.LastOffline;
        Check(!Flag(controller, "offline_can_start")
            && !controller.Dispatch(RmlUiIntentKind.OfflineLaunchMatch)
            && services.OfflineLaunches == 1,
            "accepted bot launch stays single-flight before scene return");

        Check(controller.OfflineSessionClosed()
            && controller.OfflineOpen && Flag(controller, "offline_can_start")
            && Text(controller, "offline_hunter_value") == hunter
            && Text(controller, "offline_arena_value") == LauncherRoomDisplayName.Get(first.RoomKey)
            && !Disabled(controller, "offline_start")
            && !controller.OfflineSessionClosed(),
            "scene teardown immediately enables Start without losing the setup or reopening Play");

        services.OfflineValidationPending = Pending();
        Check(controller.Dispatch(RmlUiIntentKind.OfflineLaunchMatch)
            && !Flag(controller, "offline_can_start"),
            "second match starts directly from the same bot setup");
        services.OfflineValidationPending.SetResult("");
        WaitFor(controller, () => services.OfflineLaunches == 2);
        Check(services.LastOffline == first
            && !controller.Dispatch(RmlUiIntentKind.OfflineLaunchMatch),
            "second launch preserves the selected match and remains duplicate-safe");

        services.LobbyView = Stub.EmptyLobby with { Active = true };
        Check(!controller.OfflineSessionClosed() && !Flag(controller, "offline_can_start"),
            "active network lobby cannot release a bot launch latch");
        services.LobbyView = Stub.EmptyLobby;
        Check(controller.OfflineSessionClosed() && Flag(controller, "offline_can_start"),
            "return to the offline menu re-arms Start again after another round");

        services.OfflineValidationPending = Pending();
        Check(controller.Dispatch(RmlUiIntentKind.OfflineLaunchMatch),
            "third consecutive match can be started without navigating away");
        services.OfflineValidationPending.SetResult("");
        WaitFor(controller, () => services.OfflineLaunches == 3);
        Check(services.LastOffline == first && !Flag(controller, "offline_can_start"),
            "three rounds retain the bot setup and keep the active launch latched");

        // The installed arena catalog may change after a round. A missing
        // arena should be replaced with a valid choice on the same screen.
        // Force a disappearing last choice: an earlier selector in this same test
        // process may have saved a different valid arena to launcher preferences.
        string previousMap = LauncherPrefs.LastSelectedMap;
        LauncherPrefs.LastSelectedMap = "AD2 ALINOS PERCH";
        try
        {
        var changing = new Stub { BuiltInMaps = new[] { "AD2 ALINOS PERCH", "MP1 SANCTORUS" } };
        using var returnController = new RmlUiMenuController(changing);
        Check(returnController.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline)
            && Text(returnController, "offline_arena_value") == LauncherRoomDisplayName.Get("AD2 ALINOS PERCH"),
            "offline draft initially selects an arena that was available");
        changing.OfflineValidationPending = Pending();
        Check(returnController.Dispatch(RmlUiIntentKind.OfflineLaunchMatch),
            "matching arena can begin validation");
        changing.OfflineValidationPending.SetResult("");
        WaitFor(returnController, () => changing.OfflineLaunches == 1);
        changing.BuiltInMaps = new[] { "MP1 SANCTORUS" };
        Check(returnController.OfflineSessionClosed()
            && Text(returnController, "offline_arena_value") == LauncherRoomDisplayName.Get("MP1 SANCTORUS")
            && Flag(returnController, "offline_can_start")
            && !Disabled(returnController, "offline_start"),
            "bot match return replaces a now-unavailable arena without leaving Offline Battle");
        }
        finally { LauncherPrefs.LastSelectedMap = previousMap; }
    }

    private static TaskCompletionSource<string> Pending()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void WaitFor(RmlUiMenuController controller, Func<bool> condition)
    {
        for (int i = 0; i < 150; i++)
        {
            controller.Tick();
            if (condition()) return;
            Thread.Sleep(10);
        }
        throw new TimeoutException("Native bot match validation did not complete.");
    }

    private static bool Flag(RmlUiMenuController controller, string key)
        => controller.Tick().Bindings.TryGetValue(key, out var value) && value.Boolean;

    private static string Text(RmlUiMenuController controller, string key)
        => controller.Tick().Bindings.TryGetValue(key, out var value) ? value.Text : "";

    private static bool Disabled(RmlUiMenuController controller, string id)
    {
        var bindings = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Offline, controller.Tick().Bindings);
        return bindings.TryGetValue("disabled:" + id, out var value) && value.Boolean;
    }

    private static void Check(bool pass, string label)
    {
        if (!pass) throw new InvalidOperationException(label);
        Console.WriteLine("BOT MATCH RETURN PASS " + label);
    }
}
