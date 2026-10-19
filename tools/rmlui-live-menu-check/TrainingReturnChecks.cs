using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Training;

internal static class TrainingReturnChecks
{
    internal static void Run()
    {
        // Exact launch equality requires a fixed seed: production intentionally
        // rolls a fresh seed through AimTrainerDefinition.Retry for random drills.
        var service = new Stub
        {
            TrainingDefault = AimTrainerDefinition.Default with { FixedSeed = true, Seed = 23 }
        };
        using var controller = new RmlUiMenuController(service);
        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Training)
            && controller.Dispatch(RmlUiIntentKind.OfflineChoice, 8)
            && controller.Dispatch(RmlUiIntentKind.OfflineLaunchTraining)
            && service.TrainingLaunches == 1, "native training launches selected drill");
        var first = service.LastTraining;
        Check(!controller.Dispatch(RmlUiIntentKind.OfflineLaunchTraining), "pending training launch remains protected from duplicate clicks");
        Check(controller.TrainingSessionClosed() && Flag(controller, "offline_training_mode")
            && Flag(controller, "offline_training_can_start") && !Flag(controller, "offline_training_open")
            && controller.Dispatch(RmlUiIntentKind.OfflineLaunchTraining)
            && service.TrainingLaunches == 2 && service.LastTraining == first,
            "actual scene return unlocks native training and retains drill for another launch");
        service.LobbyView = Stub.EmptyLobby with { Active = true };
        Check(!controller.TrainingSessionClosed(), "active lobby blocks training return mutation");
        service.LobbyView = Stub.EmptyLobby;
        Check(controller.Dispatch(RmlUiIntentKind.PlayJoin, address: "127.0.0.1:7777")
            && !controller.TrainingSessionClosed(), "active admission blocks training return mutation");

        var absentService = new Stub();
        using var absent = new RmlUiMenuController(absentService);
        Check(absent.TrainingSessionClosed() && Flag(absent, "offline_training_mode")
            && Flag(absent, "offline_training_can_start"), "scene return can restore a missing native training workspace");
        Check(absent.Dispatch(RmlUiIntentKind.PlayCreateOpen) && absent.Dispatch(RmlUiIntentKind.PlayCreate)
            && !absent.TrainingSessionClosed(), "authoritative allocation blocks training return mutation");
    }
    private static bool Flag(RmlUiMenuController controller, string key) => controller.Tick().Bindings[key].Boolean;
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("TRAINING RETURN PASS " + label); }
}
