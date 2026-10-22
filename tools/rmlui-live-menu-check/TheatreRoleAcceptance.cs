using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class TheatreRoleAcceptance
{
    private static RmlUiReplayEntry Replay => new("/synthetic/role.ppdemo", "ROLE REPLAY", "SYNTHETIC", "MP1 SANCTORUS",
        100, 200, 300, false, false, true, true);
    internal static void Run()
    {
        var service = new Stub { ReplayEditingAllowed = false };
        using var controller = new RmlUiMenuController(service);
        Open(controller, service);
        var bindings = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Theatre, controller.Tick().Bindings);
        Check(Text(controller, "theatre_studio") == "OPEN IN PRIME STUDIO"
            && !bindings["visible:theatre_recover"].Boolean && !bindings["visible:theatre_export"].Boolean
            && !controller.Dispatch(RmlUiIntentKind.TheatreAction, 9)
            && !controller.Dispatch(RmlUiIntentKind.TheatreAction, 11) && service.TheatreWorkCalls == 0,
            "main Theatre labels external editor and hides/refuses advanced recovery and export");
        Check(controller.Dispatch(RmlUiIntentKind.TheatreAction, 18) && service.StudioReplayCalls == 1
            && service.LastStudioReplay == Replay && service.ReplayLaunches == 0 && service.TheatreValidations == 0
            && !controller.Busy && Flag(controller, "theatre_watch_enabled"),
            "main editor action forwards exact logical entry without playback validation, scene or launched state");
        service.StudioReplayAccepted = false;
        Check(!controller.Dispatch(RmlUiIntentKind.TheatreAction, 18) && service.StudioReplayCalls == 2
            && Flag(controller, "theatre_watch_enabled") && Text(controller, "theatre_status") == "PRIME STUDIO HANDOFF REFUSED",
            "external failure retains reusable selected replay and bounded status");
        service.StudioReplayAccepted = true;
        service.TheatreRows = new[] { Replay with { Bytes = 999 } };
        Check(!controller.Dispatch(RmlUiIntentKind.TheatreAction, 18) && service.LastStudioReplay == Replay
            && service.ReplayLaunches == 0 && Flag(controller, "theatre_watch_enabled"),
            "external service receives captured source witness and refuses changed source without opening scene");
        service.TheatreRows = new[] { Replay };
        Check(controller.Dispatch(RmlUiIntentKind.TheatreAction, 6), "basic main watch uses original validation path");
        Wait(controller, () => service.TheatreValidations == 1);
        int opens = service.StudioReplayCalls;
        Check(!controller.Dispatch(RmlUiIntentKind.TheatreAction, 18) && service.StudioReplayCalls == opens,
            "current watch validation fences external editor handoff");
        Check(controller.Dispatch(RmlUiIntentKind.TheatreAction, 10), "main cancels original pending watch verification");
        service.TheatreValidationPending.TrySetResult("");
        for (int i = 0; i < 20; i++) { controller.Tick(); Thread.Sleep(1); }
        Check(service.ReplayLaunches == 0 && service.StudioReplayCalls == opens
            && controller.Dispatch(RmlUiIntentKind.TheatreAction, 18), "late canceled verification cannot launch while external editor remains reusable");
        controller.Dispatch(RmlUiIntentKind.TheatreAction, 0, theatreSearch: "NO MATCH");
        opens = service.StudioReplayCalls;
        Check(!controller.Dispatch(RmlUiIntentKind.TheatreAction, 18) && service.StudioReplayCalls == opens,
            "filtered-away selection cannot become external editor target");

        var studioService = new Stub { ReplayEditingAllowed = true };
        using var studio = new RmlUiMenuController(studioService);
        Open(studio, studioService);
        Check(Text(studio, "theatre_studio") == "EDIT REPLAY" && Flag(studio, "theatre_advanced_tools")
            && studio.Dispatch(RmlUiIntentKind.TheatreAction, 18), "Studio role retains inline replay edit path");
        Wait(studio, () => studioService.TheatreValidations == 1);
        studioService.TheatreValidationPending.SetResult("");
        Wait(studio, () => studioService.ReplayLaunches == 1);
        Check(studioService.LastReplayOpenEditor && studioService.StudioReplayCalls == 0
            && !Flag(studio, "theatre_watch_enabled"), "Studio validates exact replay then launches existing inline editor once");
    }
    private static void Open(RmlUiMenuController controller, Stub service)
    {
        service.TheatreRows = new[] { Replay };
        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Theatre), "role fixture opens native Theatre");
        Wait(controller, () => service.TheatreReadCalls == 1);
        service.TheatreReadPending.SetResult(service.TheatreRows);
        Wait(controller, () => !controller.Busy);
        Check(controller.Dispatch(RmlUiIntentKind.TheatreEntry, 0), "role fixture selects exact entry");
    }
    private static void Wait(RmlUiMenuController controller, Func<bool> done)
    {
        for (int i = 0; i < 600; i++) { controller.Tick(); if (done()) return; Thread.Sleep(5); }
        throw new TimeoutException("Theatre role completion");
    }
    private static bool Flag(RmlUiMenuController controller, string key) => controller.Tick().Bindings[key].Boolean;
    private static string Text(RmlUiMenuController controller, string key) => controller.Tick().Bindings[key].Text;
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("THEATRE ROLE PASS " + label); }
}
