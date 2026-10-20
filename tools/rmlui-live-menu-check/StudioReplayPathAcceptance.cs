using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class StudioReplayPathAcceptance
{
    private static string PathFor(string name) => Path.Combine(Path.GetTempPath(), "ProjectPrime-RmlUi-Synthetic", name);
    private static RmlUiReplayEntry Entry(string path, int n = 0) => new(path, "DEEP LINK " + n, "SYNTHETIC", "MP1 SANCTORUS",
        100 + n, 200, 300, false, false, false, true);
    internal static void Run()
    {
        var entries = Enumerable.Range(0, 17).Select(i => Entry(PathFor("replay-" + i + ".ppdemo"), i)).ToArray();
        var service = new Stub { TheatreRows = entries };
        using var controller = new RmlUiMenuController(service);
        Check(controller.OpenStudioReplayPath(entries[0].Path) && controller.Busy
            && !controller.OpenStudioReplayPath(entries[1].Path), "Studio path starts one library scan and refuses duplicate pending target");
        Wait(controller, () => service.TheatreReadCalls == 1);
        service.TheatreReadPending.SetResult(entries);
        Wait(controller, () => service.TheatreValidations == 1);
        Check(service.LastValidatedReplay.Path == entries[0].Path && service.ReplayLaunches == 0,
            "refreshed bounded library selects exact requested entry beyond first page and starts normal preflight");
        service.TheatreValidationPending.SetResult("");
        Wait(controller, () => service.ReplayLaunches == 1);
        Check(service.LastReplay.Path == entries[0].Path && service.LastReplayOpenEditor
            && !controller.OpenStudioReplayPath(entries[1].Path), "exact preflight opens inline editor once and existing playback cannot be replaced");

        var initial = new Stub { TheatreRows = new[] { Entry(PathFor("virtual.ppclip")) } };
        using var duringRead = new RmlUiMenuController(initial);
        duringRead.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Theatre);
        Check(duringRead.OpenStudioReplayPath(initial.TheatreRows[0].Path), "startup path attaches to existing initial library read");
        initial.TheatreReadPending.SetResult(initial.TheatreRows);
        Wait(duringRead, () => initial.TheatreValidations == 1);
        Check(initial.TheatreReadCalls == 1 && initial.LastValidatedReplay.Path.EndsWith(".ppclip", StringComparison.Ordinal),
            "logical clip path uses one normal library scan and preflight without import or resolution bypass");
        Check(!duringRead.OpenStudioReplayPath(initial.TheatreRows[0].Path), "active verification refuses competing deep link");

        foreach (bool navigate in new[] { false, true })
        {
            var canceled = new Stub { TheatreRows = new[] { Entry(PathFor("canceled.ppdemo")) } };
            using var canceledController = new RmlUiMenuController(canceled);
            Check(canceledController.OpenStudioReplayPath(canceled.TheatreRows[0].Path), "cancel fixture accepts pending startup target");
            if (navigate) canceledController.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home);
            else canceledController.Dispatch(RmlUiIntentKind.TheatreAction, 10);
            canceled.TheatreReadPending.TrySetResult(canceled.TheatreRows);
            for (int i = 0; i < 20; i++) { canceledController.Tick(); Thread.Sleep(1); }
            Check(canceled.TheatreValidations == 0 && canceled.ReplayLaunches == 0,
                "cancel or route change retires pending startup target despite late scan completion");
        }
        foreach (bool unplayable in new[] { false, true })
        {
            var unavailable = new Stub { TheatreRows = unplayable
                ? new[] { Entry(PathFor("missing.ppdemo")) with { Playable = false } } : Array.Empty<RmlUiReplayEntry>() };
            using var absent = new RmlUiMenuController(unavailable);
            Check(absent.OpenStudioReplayPath(PathFor("missing.ppdemo")), "requested source awaits authoritative library scan");
            unavailable.TheatreReadPending.SetResult(unavailable.TheatreRows);
            Wait(absent, () => !absent.Busy);
            Check(unavailable.TheatreValidations == 0 && unavailable.ReplayLaunches == 0
                && Text(absent, "theatre_status").Contains("NOT A PLAYABLE ENTRY", StringComparison.Ordinal),
                "missing/nonlibrary or unplayable entry produces explicit refusal without import");
        }
        foreach (bool changed in new[] { false, true })
        {
            var guarded = new Stub { TheatreRows = new[] { Entry(PathFor("guarded.ppdemo")) } };
            using var guardedController = new RmlUiMenuController(guarded);
            guardedController.OpenStudioReplayPath(guarded.TheatreRows[0].Path);
            guarded.TheatreReadPending.SetResult(guarded.TheatreRows);
            Wait(guardedController, () => guarded.TheatreValidations == 1);
            if (changed) guarded.TheatreRows = new[] { guarded.TheatreRows[0] with { LastWriteTicks = 301 } };
            guarded.TheatreValidationPending.SetResult(changed ? "" : "SYNTHETIC PROTOCOL REFUSAL");
            Wait(guardedController, () => !guardedController.Busy);
            Check(guarded.ReplayLaunches == 0 && guardedController.Tick().Bindings["theatre_watch_enabled"].Boolean,
                "startup obeys protocol refusal and changed-source launch checks while retaining library");
        }
        var invalid = new Stub();
        using var rejected = new RmlUiMenuController(invalid);
        foreach (string path in new[] { "relative.ppdemo", PathFor("not-replay.txt"), "", "/synthetic/bad\0.ppdemo" })
            Check(!rejected.OpenStudioReplayPath(path), "startup rejects malformed or unsupported logical path");
        Check(!rejected.OpenStudioReplayPath(PathFor("recover.ppdemo"), recover: true)
            && invalid.TheatreReadCalls == 0 && invalid.TheatreWorkCalls == 0, "recovery startup requires explicit review and performs no silent modification");
        invalid.ReplayEditingAllowed = false;
        Check(!rejected.OpenStudioReplayPath(PathFor("main.ppdemo")) && invalid.TheatreReadCalls == 0,
            "Main role cannot enter Studio startup editing path");
        rejected.ReportStudioLaunch(false, "SYNTHETIC STUDIO REFUSAL");
        Check(rejected.Tick().Bindings.Values.Any(value => value.Text == "SYNTHETIC STUDIO REFUSAL"), "external Studio refusal is visible through existing native status");
    }
    private static string Text(RmlUiMenuController controller, string key) => controller.Tick().Bindings[key].Text;
    private static void Wait(RmlUiMenuController controller, Func<bool> done)
    {
        for (int i = 0; i < 600; i++) { controller.Tick(); if (done()) return; Thread.Sleep(5); }
        throw new TimeoutException("Studio replay path completion");
    }
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("STUDIO REPLAY PATH PASS " + label); }
}
