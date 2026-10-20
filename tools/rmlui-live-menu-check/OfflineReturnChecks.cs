using System;
using System.Threading;
using System.Threading.Tasks;
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
            && Text(controller, "offline_arena_value") == first.RoomKey
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
