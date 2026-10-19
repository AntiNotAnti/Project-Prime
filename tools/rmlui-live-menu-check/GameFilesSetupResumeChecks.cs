using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class GameFilesSetupResumeChecks
{
    internal static void Run()
    {
        foreach (int route in new[] { -1, (int)RmlUiRouteArgument.Offline, (int)RmlUiRouteArgument.Training })
        {
            var refused = new Stub { GameFilesReady = false };
            using (var controller = new RmlUiMenuController(refused))
                Check(!Open(controller, route) && refused.ConfigureGameFilesCalls == 1 && refused.LegacyCreateCalls == 0
                    && refused.NavigationCalls == 0, "missing files request only native setup capability, refusal has no legacy handoff");
            var service = new Stub { GameFilesReady = false, ConfigureGameFilesAccepted = true };
            using (var controller = new RmlUiMenuController(service))
            {
                Check(Open(controller, route) && !Flag(controller, "play_create_mode") && !Flag(controller, "offline_mode"),
                    "accepted setup waits for explicit completion");
                service.GameFilesReady = true;
                Check(!Flag(controller, "play_create_mode") && !Flag(controller, "offline_mode"), "readiness polling cannot resume setup target");
                controller.GameFilesSetupClosed(true);
                Check(route == -1 ? Flag(controller, "play_create_mode")
                    : Flag(controller, "offline_mode") && Flag(controller, "offline_training_mode") == (route == (int)RmlUiRouteArgument.Training),
                    "installed close resumes exact native Create, Offline or Training target");
                controller.Dispatch(RmlUiIntentKind.PlayCancel);
                var before = controller.Tick();
                controller.GameFilesSetupClosed(true);
                Check(Flag(controller, "play_create_mode") == before.Bindings["play_create_mode"].Boolean
                    && service.ConfigureGameFilesCalls == 1 && service.NavigationCalls == 0 && service.LegacyCreateCalls == 0,
                    "setup resume is consumed once without legacy handoff");
            }
            foreach (int retire in new[] { 0, 1, 2, 3 })
            {
                var service2 = new Stub { GameFilesReady = false, ConfigureGameFilesAccepted = true };
                using var controller = new RmlUiMenuController(service2);
                Check(Open(controller, route), "fresh native setup target accepted");
                if (retire == 0) controller.Dispatch(RmlUiIntentKind.PlayCancel);
                if (retire == 1) controller.GameFilesSetupClosed(false);
                if (retire == 2) controller.GameFilesSetupClosed(true); // Installed signal without ready files is consumed.
                if (retire == 3) service2.LobbyView = Stub.EmptyLobby with { Active = true };
                service2.GameFilesReady = true;
                controller.GameFilesSetupClosed(true);
                service2.LobbyView = Stub.EmptyLobby;
                controller.GameFilesSetupClosed(true);
                Check(!Flag(controller, "play_create_mode") && !Flag(controller, "offline_mode"),
                    "canceled, unready or changed-session setup completion cannot revive native target");
            }
        }
    }
    private static bool Open(RmlUiMenuController controller, int route) => route == -1
        ? controller.Dispatch(RmlUiIntentKind.PlayCreateOpen) : controller.Dispatch(RmlUiIntentKind.Navigate, route);
    private static bool Flag(RmlUiMenuController controller, string key) => controller.Tick().Bindings[key].Boolean;
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("NATIVE SETUP RESUME PASS " + label); }
}
