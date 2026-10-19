using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class TheatreWorkAcceptance
{
    internal static void Run()
    {
        AdmissionAndResults();
        ImportSnapshotAndCancellation();
        DeleteConsent();
        FailureAndLifetime();
        TheatreEditorLaunchAcceptance.Run();
        TheatreRoleAcceptance.Run();
        StudioReplayPathAcceptance.Run();
    }

    private static void AdmissionAndResults()
    {
        var service = new Stub();
        using var controller = new RmlUiMenuController(service);
        Check(!Action(controller, 8), "closed Theatre cannot start a replay operation");
        Open(controller, service, select: false);
        controller.Dispatch(RmlUiIntentKind.TheatreAction, 0, theatreSearch: "NO MATCHING FIXTURE");
        foreach (int action in new[] { 8, 9, 11, 12, 13, 14, 15 })
            Check(!Action(controller, action, new(Name: "RENAMED")),
                "file operations require a selected library entry: " + action);
        controller.Dispatch(RmlUiIntentKind.TheatreAction, 23);
        Check(controller.Dispatch(RmlUiIntentKind.TheatreEntry, 1) && !Action(controller, 9),
            "recovery rejects a selected replay without recoverable metadata");
        controller.Dispatch(RmlUiIntentKind.TheatreEntry, 0);
        foreach (string name in new[] { "", "   ", new string('x', 101) })
            Check(!Action(controller, 12, new(Name: name)), "empty or overlong rename never reaches service");
        Check(!Action(controller, 13, new(Tags: new string('x', 1025)))
            && !Action(controller, 13, new(Collections: new string('x', 1025)))
            && !Action(controller, 20, new(ImportPath: ""))
            && !Action(controller, 20, new(ImportPath: new string('x', 4097)))
            && service.TheatreWorkCalls == 0,
            "overlong organization metadata and absent or oversized import path are rejected");
        Check(Action(controller, 17) && service.RevealCalls == 1 && service.TheatreWorkCalls == 0,
            "reveal invokes only existing folder action");

        foreach (var operation in new[] { (Action: 8, Work: RmlUiReplayWork.Validate),
            (Action: 9, Work: RmlUiReplayWork.Recover), (Action: 11, Work: RmlUiReplayWork.Export),
            (Action: 12, Work: RmlUiReplayWork.Rename), (Action: 13, Work: RmlUiReplayWork.Organize) })
        {
            Open(controller, service);
            var selected = service.TheatreRows[0];
            var inputs = new RmlUiReplayInputs("NEW NAME", "tag-one,tag-two", "collection-one", "/untrusted/not-selected.ppdemo");
            Prepare(service);
            int before = service.TheatreWorkCalls;
            Check(Action(controller, operation.Action, inputs) && controller.Busy
                && !Action(controller, operation.Action, inputs)
                && !controller.Dispatch(RmlUiIntentKind.TheatreEntry, 1),
                "each replay job is single-flight and fences selection while busy: " + operation.Work);
            Wait(controller, _ => Volatile.Read(ref service.TheatreWorkCalls) == before + 1);
            Check(service.LastTheatreWork.Work == operation.Work
                && service.LastTheatreWork.Entry == selected && service.LastTheatreWork.Inputs == inputs
                && service.LastTheatreWork.Entry?.Path != inputs.ImportPath
                && service.TheatreWorkThread != Environment.CurrentManagedThreadId,
                "file work captures library path, bytes and write stamp off-thread: " + operation.Work);
            Check(Text(controller.Tick(), "theatre_selected_title") == selected.Title,
                "pending file work does not optimistically rename, recover or remove the selected replay");
            var refreshed = selected with { Title = "REFRESHED " + operation.Work, Bytes = selected.Bytes + 1,
                LastWriteTicks = selected.LastWriteTicks + 1, Tags = "confirmed-tag" };
            service.TheatreWorkPending.SetResult(new(true, "SYNTHETIC WORK CONFIRMED", new[] { refreshed }));
            var complete = Wait(controller, _ => !controller.Busy);
            Check(Text(complete, "theatre_selected_title") == refreshed.Title
                && controller.TheatreInputs.Tags == "confirmed-tag"
                && Text(complete, "theatre_status") == "SYNTHETIC WORK CONFIRMED",
                "successful replay result replaces library metadata from authoritative result: " + operation.Work);
        }

        Open(controller, service, select: false);
        controller.Dispatch(RmlUiIntentKind.TheatreAction, 0, theatreSearch: "NO MATCHING FIXTURE");
        Prepare(service);
        int importsBefore = service.TheatreWorkCalls;
        Check(Action(controller, 20, new(ImportPath: "/synthetic/import.ppdemo")),
            "explicit import path action can start without a selected recording");
        Wait(controller, _ => service.TheatreWorkCalls == importsBefore + 1);
        Check(service.LastTheatreWork.Work == RmlUiReplayWork.Import && service.LastTheatreWork.Entry == null
            && service.LastTheatreWork.Inputs.ImportPath == "/synthetic/import.ppdemo",
            "only the explicit import action forwards user path without substituting a library recording");
        service.TheatreWorkPending.SetResult(new(true, "IMPORTED FIXTURE", Rows()));
        Wait(controller, _ => !controller.Busy);
        Check(!Action(controller, 18) && service.LegacyTheatreHandoffs == 0,
            "editor launch after import still requires a selected replay and has no legacy fallback");
        controller.Dispatch(RmlUiIntentKind.TheatreAction, 23);
        Check(controller.Dispatch(RmlUiIntentKind.TheatreEntry, 0), "imported library can select an exact editor source");
        service.TheatreValidationPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Check(Action(controller, 18) && controller.Busy && service.ReplayLaunches == 0,
            "editor launch waits for selected replay verification");
        service.TheatreValidationPending.SetResult("");
        Wait(controller, _ => service.ReplayLaunches == 1);
        Check(service.LastReplayOpenEditor && service.LastReplay == service.LastValidatedReplay && service.LegacyTheatreHandoffs == 0,
            "editor launch uses native existing-player presentation flag after exact verification");
    }

    private static void ImportSnapshotAndCancellation()
    {
        Check(RmlUiIntentRegistry.TryParseLegacy("theatre:import-path", default, 1, out var import)
            && import.Kind == RmlUiIntentKind.TheatreAction && import.Argument == 20,
            "authored import-path action resolves to its current argument rather than the older alias");
        var service = new Stub();
        using var controller = new RmlUiMenuController(service);
        Open(controller, service, select: false);
        Prepare(service);
        var submitted = new RmlUiReplayInputs(ImportPath: "/synthetic/current-field.ppdemo");
        var changedField = submitted with { ImportPath = "/synthetic/later-field.ppdemo" };
        int before = service.TheatreWorkCalls;
        Check(Action(controller, import.Argument, submitted) && controller.Busy
            && !Action(controller, import.Argument, changedField),
            "visible import action captures current field inputs and refuses another import while busy");
        Wait(controller, _ => service.TheatreWorkCalls == before + 1);
        Check(service.LastTheatreWork.Work == RmlUiReplayWork.Import
            && service.LastTheatreWork.Inputs == submitted && service.LastTheatreWork.Inputs != changedField
            && service.TheatreWorkThread != Environment.CurrentManagedThreadId,
            "import job owns the submitted path snapshot on the existing worker boundary");
        var cancelled = service.TheatreWorkPending;
        var cancelledToken = service.LastTheatreWorkToken;
        Check(Action(controller, 10) && !controller.Busy && cancelledToken.IsCancellationRequested,
            "native Cancel Scan / Check cancels the exact current import request");
        Prepare(service);
        Check(Action(controller, import.Argument, changedField), "a new import can use the field edited after cancellation");
        Wait(controller, _ => service.TheatreWorkCalls == before + 2);
        Check(service.LastTheatreWork.Inputs == changedField,
            "replacement import captures the newly submitted field instead of the cancelled path");
        cancelled.SetResult(new(true, "LATE CANCELLED IMPORT", Rows()));
        Drain(controller);
        Check(controller.Busy && Text(controller.Tick(), "theatre_status") != "LATE CANCELLED IMPORT",
            "cancelled import completion cannot replace the current import owner or status");
        service.TheatreWorkPending.SetResult(new(true, "CURRENT IMPORT COMPLETE", Rows()));
        Wait(controller, state => !controller.Busy && Text(state, "theatre_status") == "CURRENT IMPORT COMPLETE");
        Prepare(service);
        Check(Action(controller, 19, submitted), "older import action remains a compatible alias");
        Wait(controller, _ => service.TheatreWorkCalls == before + 3);
        Check(service.LastTheatreWork.Work == RmlUiReplayWork.Import
            && service.LastTheatreWork.Inputs == submitted && Action(controller, 10),
            "import alias uses the same request snapshot and cancellation owner");
    }

    private static void DeleteConsent()
    {
        var service = new Stub();
        using var controller = new RmlUiMenuController(service);
        Open(controller, service);
        Check(!Action(controller, 15) && Action(controller, 14)
            && Flag(controller.Tick(), "theatre_delete_confirmation") && service.TheatreWorkCalls == 0,
            "delete requires an explicit arm step that never deletes optimistically");
        Check(Action(controller, 16) && !Flag(controller.Tick(), "theatre_delete_confirmation")
            && !Action(controller, 15) && service.TheatreWorkCalls == 0,
            "cancelled delete consent cannot be reused");
        Action(controller, 14);
        Check(controller.Dispatch(RmlUiIntentKind.TheatreEntry, 1)
            && !Flag(controller.Tick(), "theatre_delete_confirmation") && !Action(controller, 15),
            "changed selection retires consent before another recording can be deleted");
        controller.Dispatch(RmlUiIntentKind.TheatreEntry, 0);
        var expected = service.TheatreRows[0];
        Prepare(service);
        Action(controller, 14);
        Check(Action(controller, 15) && !Action(controller, 15),
            "confirmed delete starts exactly one file operation");
        Wait(controller, _ => service.TheatreWorkCalls == 1);
        Check(service.LastTheatreWork.Work == RmlUiReplayWork.Delete && service.LastTheatreWork.Entry == expected
            && Text(controller.Tick(), "theatre_selected_title") == expected.Title,
            "delete submission carries exactly the consented recording stamp and retains data pending result");
        service.TheatreWorkPending.SetResult(new(true, "DELETED FIXTURE", service.TheatreRows.Skip(1).ToArray()));
        var deleted = Wait(controller, _ => !controller.Busy);
        Check(Text(deleted, "theatre_selected_title") == service.TheatreRows[1].Title
            && Text(deleted, "theatre_entry0_title") == service.TheatreRows[1].Title
            && !Flag(deleted, "theatre_delete_confirmation"),
            "confirmed deletion selects a shown replacement from the returned index and clears consent");
    }

    private static void FailureAndLifetime()
    {
        var service = new Stub();
        using var controller = new RmlUiMenuController(service);
        foreach (bool throws in new[] { false, true })
        {
            Open(controller, service);
            Prepare(service);
            int count = service.TheatreWorkCalls;
            Action(controller, 8);
            Wait(controller, _ => service.TheatreWorkCalls == count + 1);
            if (throws) service.TheatreWorkPending.SetException(new InvalidOperationException("SYNTHETIC FILE FAILURE"));
            else service.TheatreWorkPending.SetResult(new(false, "SYNTHETIC REFUSAL", new[] { Rows()[0] with { Title = "INVALID RESULT" } }));
            var failed = Wait(controller, _ => !controller.Busy);
            Check(Text(failed, "theatre_selected_title") == Rows()[0].Title
                && Text(failed, "theatre_status") != "SYNTHETIC WORK CONFIRMED",
                "false or throwing file operation cannot apply optimistic result entries");
        }

        Open(controller, service);
        Prepare(service);
        int calls = service.TheatreWorkCalls;
        Action(controller, 11);
        Wait(controller, _ => service.TheatreWorkCalls == calls + 1);
        var stale = service.TheatreWorkPending;
        CancellationToken staleToken = service.LastTheatreWorkToken;
        Check(Action(controller, 10) && !controller.Busy && staleToken.IsCancellationRequested,
            "file operation cancellation immediately retires the request epoch");
        service.TheatreReadPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Check(Action(controller, 5), "fresh library scan can supersede cancelled file work");
        var freshRows = new[] { Rows()[0] with { Title = "NEWER LIBRARY", LastWriteTicks = 9999 } };
        service.TheatreReadPending.SetResult(freshRows);
        Wait(controller, state => !controller.Busy && Text(state, "theatre_entry0_title") == "NEWER LIBRARY");
        stale.SetResult(new(true, "LATE OLD FILE RESULT", Rows()));
        Drain(controller);
        Check(Text(controller.Tick(), "theatre_entry0_title") == "NEWER LIBRARY"
            && Text(controller.Tick(), "theatre_status") != "LATE OLD FILE RESULT",
            "late cancelled file result cannot repaint superseding library scan");

        Open(controller, service);
        Prepare(service);
        calls = service.TheatreWorkCalls;
        Action(controller, 8);
        Wait(controller, _ => service.TheatreWorkCalls == calls + 1);
        var away = service.TheatreWorkPending;
        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home)
            && !controller.TheatreOpen && service.LastTheatreWorkToken.IsCancellationRequested,
            "leaving route retires pending replay work");
        away.SetResult(new(true, "ROUTE-AWAY RESULT", Rows()));
        Drain(controller);
        Check(!Flag(controller.Tick(), "theatre_mode") && Text(controller.Tick(), "theatre_status") == "",
            "late replay work cannot reopen or repaint a departed route");
    }

    private static void Open(RmlUiMenuController controller, Stub service, bool select = true)
    {
        controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home);
        service.TheatreRows = Rows();
        service.TheatreReadPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Theatre),
            "isolated native Theatre opens against synthetic library service");
        service.TheatreReadPending.SetResult(service.TheatreRows);
        Wait(controller, state => !controller.Busy && Flag(state, "theatre_entry0_present"));
        if (select) Check(controller.Dispatch(RmlUiIntentKind.TheatreEntry, 0), "synthetic library recording selected");
    }

    private static RmlUiReplayEntry[] Rows() => new[]
    {
        new RmlUiReplayEntry("/synthetic/library/first.ppdemo", "FIRST LIBRARY RECORDING", "fixture", "Map", 200, 1234, 5678,
            false, false, true, true),
        new RmlUiReplayEntry("/synthetic/library/second.ppdemo", "SECOND LIBRARY RECORDING", "fixture", "Map", 100, 4321, 8765,
            false, false, false, true)
    };
    private static void Prepare(Stub service)
        => service.TheatreWorkPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static bool Action(RmlUiMenuController controller, int action, RmlUiReplayInputs inputs = default)
        => controller.Dispatch(RmlUiIntentKind.TheatreAction, action, theatreInputs: inputs);
    private static string Text(RmlUiMenuSnapshot state, string key)
        => state.Bindings.TryGetValue(key, out var value) ? value.Text : "";
    private static bool Flag(RmlUiMenuSnapshot state, string key)
        => state.Bindings.TryGetValue(key, out var value) && value.Boolean;
    private static RmlUiMenuSnapshot Wait(RmlUiMenuController controller, Func<RmlUiMenuSnapshot, bool> predicate)
    {
        for (int i = 0; i < 300; i++)
        {
            var snapshot = controller.Tick();
            if (predicate(snapshot)) return snapshot;
            Thread.Sleep(10);
        }
        throw new TimeoutException("Theatre file work fixture did not complete");
    }
    private static void Drain(RmlUiMenuController controller)
    {
        for (int i = 0; i < 20; i++) { controller.Tick(); Thread.Sleep(5); }
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("RML THEATRE WORK PASS " + label);
    }
}
