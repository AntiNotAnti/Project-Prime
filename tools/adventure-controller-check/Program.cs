using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
}

var backend = new AdventureBoundary();
using (var controller = new AdventureController(backend))
{
    var first = controller.Snapshot();
    controller.Refresh();
    Check(ReferenceEquals(first, controller.Snapshot()), "unchanged save scans retain their snapshot and revision");
    backend.Slots[1] = backend.Slots[1] with { Health = 72 };
    Check(first.Slots[1].Health == 99, "backend changes cannot mutate previously captured save summaries");
    controller.Refresh();
    Check(controller.Snapshot().Slots[1].Health == 72 && controller.Snapshot().Revision > first.Revision,
        "changed authoritative summaries produce a new revision");
    var stale = controller.Intent(AdventureIntentKind.Continue);
    Check(controller.SelectSlot(2).Accepted && controller.Snapshot().CanContinue, "selecting a used slot enables Continue");
    Check(!controller.Dispatch(stale).Accepted && backend.Created.Count == 0,
        "a choice from the previous revision cannot launch the newly selected slot");
    Check(!controller.Dispatch(controller.Intent(AdventureIntentKind.Continue) with { Lifetime = Guid.NewGuid() }).Accepted,
        "another page lifetime cannot submit a choice");
    Check(controller.RequestContinue().Accepted, "Continue prepares a launch through the existing authority boundary");
    Check(!controller.RequestContinue().Accepted && backend.Created.Count == 1, "double click cannot queue a second launch");
    Check(controller.TryTakeLaunch(out var launch) && launch.SaveSlot == 2 && !launch.NewGame
        && launch.Kind == LaunchKind.Adventure, "Continue hands the chosen slot to the engine once");
    Check(!controller.TryTakeLaunch(out _) && !controller.RequestNewRun().Accepted,
        "the consumed handoff stays busy until the engine reports failure or the page leaves");
    controller.ReportLaunchFailure("The extracted files could not be read (levels\\entities\\mp1_Ent.bin)");
    Check(controller.Snapshot().Error == "The extracted files could not be read (levels\\entities\\mp1_Ent.bin)"
        && controller.Snapshot().CanContinue, "engine failure text survives unchanged and releases the busy state");
    Check(controller.SelectHunter(Hunter.Trace).Accepted && controller.RequestNewRun().Accepted,
        "a new run in a used slot opens confirmation");
    Check(controller.Snapshot().ConfirmOverwrite && backend.Created.Count == 1,
        "requesting overwrite cannot persist a choice or issue a launch before confirmation");
    Check(!controller.SelectSlot(3).Accepted && !controller.SelectHunter(Hunter.Kanden).Accepted,
        "the pending overwrite freezes its slot and hunter");
    Check(controller.CancelConfirmation().Accepted && !controller.TryTakeLaunch(out _) && backend.Created.Count == 1,
        "cancelling overwrite never touches the launch authority");
    Check(controller.RequestNewRun().Accepted && controller.ConfirmNewRun().Accepted,
        "a confirmed overwrite prepares a fresh Adventure launch");
    Check(controller.TryTakeLaunch(out launch) && launch.SaveSlot == 2 && launch.NewGame && launch.Hunter == Hunter.Trace,
        "confirmation carries the exact captured slot and Hunter into the engine");
    controller.CancelPending();
    Check(controller.SelectSlot(1).Accepted && controller.RequestNewRun().Accepted
        && !controller.Snapshot().ConfirmOverwrite && controller.TryTakeLaunch(out launch) && launch.NewGame,
        "an empty slot can start fresh without an overwrite prompt");
    controller.CancelPending();
    Check(!controller.SelectSlot(0).Accepted && !controller.SelectSlot(4).Accepted,
        "nonexistent save slot indices cannot reach the launch authority");
    Check(!controller.SelectHunter(Hunter.Guardian).Accepted, "nonplayable Hunters cannot enter Adventure selection");
    Check(Task.Run(() =>
    {
        try { controller.RequestNewRun(); }
        catch (InvalidOperationException) { return true; }
        return false;
    }).GetAwaiter().GetResult(), "worker threads cannot mutate engine owned menu choices");
}

backend = new AdventureBoundary { Problem = "No game files yet" };
using (var controller = new AdventureController(backend))
{
    Check(controller.Snapshot().FileProblem == "No game files yet" && !controller.Snapshot().CanNewRun,
        "the real file validation message disables launch");
    Check(!controller.RequestNewRun().Accepted && backend.Created.Count == 0,
        "missing game files cannot cause preference persistence or an engine handoff");
    backend.Problem = null;
    controller.SelectSlot(2);
    controller.RequestNewRun();
    backend.Problem = "The extracted files are missing -- set up again";
    Check(!controller.ConfirmNewRun().Accepted && controller.Snapshot().ConfirmOverwrite
        && controller.Snapshot().Error == backend.Problem && backend.Created.Count == 0,
        "files disappearing while a confirmation is open preserve the exact failure and block launch");
    controller.CancelConfirmation();
    backend.Problem = null;
    backend.CreateFailure = "settings.json could not be written";
    Check(!controller.RequestContinue().Accepted && controller.Snapshot().Error == backend.CreateFailure
        && !controller.TryTakeLaunch(out _) && !controller.Snapshot().LaunchPending,
        "persistence failure returns its exact message without a partial handoff");
    backend.CreateFailure = null;
    Check(controller.RequestContinue().Accepted && controller.TryTakeLaunch(out _), "a failed choice can be retried");
}

backend = new AdventureBoundary();
using (var controller = new AdventureController(backend, Hunter.Trace, lowTier: true))
{
    Check(controller.Snapshot().Hunter == Hunter.Kanden
        && controller.Snapshot().HunterChoices.Contains(Hunter.Random)
        && !controller.Snapshot().HunterChoices.Contains(Hunter.Trace),
        "the legacy offline Hunter pool and Random menu entry are retained");
    backend.ReadFailure = "Savedata could not be read";
    controller.Refresh();
    Check(controller.Snapshot().ReadFailed && controller.Snapshot().Error == backend.ReadFailure
        && !controller.Snapshot().CanNewRun, "failed slot inspection cannot silently assume an empty slot");
    backend.ReadFailure = null;
    controller.Refresh();
    Check(controller.RequestNewRun().Accepted, "slot inspection can recover without recreating a controller");
    controller.Dispose();
    Check(!controller.TryTakeLaunch(out _) && controller.Snapshot().Closed,
        "retiring a page discards unconsumed launches");
    Check(!controller.RequestNewRun().Accepted, "a retired controller cannot launch again");
}

Console.WriteLine($"ADVENTURE CONTRACT PASS {checks} checks (no game content, save writes, native renderer, or network).");
#if MPHREAD_RMLUI_POC
if (args.Length > 0)
{
    if (args.Length != 3 || args[0] != "--native")
        throw new ArgumentException("Use --native <native-library> <packaged-rmlui-root>.");
    NativeAdventureCheck.Run(args[1], args[2]);
}
#endif

sealed class AdventureBoundary : IAdventureBackend
{
    public List<AdventureSave.SlotInfo> Slots { get; } = new()
    {
        new() { Slot = 1, Area = "", Used = false },
        new() { Slot = 2, Area = "Celestial Archives", Used = true, Octoliths = 3, Health = 99, HealthMax = 199 },
        new() { Slot = 3, Area = "", Used = false }
    };
    public List<LaunchPlan> Created { get; } = new();
    public string? Problem { get; set; }
    public string? CreateFailure { get; set; }
    public string? ReadFailure { get; set; }
    public IReadOnlyList<AdventureSave.SlotInfo> ReadSlots()
    {
        if (ReadFailure != null) throw new IOException(ReadFailure);
        return Slots;
    }
    public string? GameFileProblem() => Problem;
    public LaunchPlan CreateLaunch(byte slot, bool newGame, Hunter hunter)
    {
        if (CreateFailure != null) throw new IOException(CreateFailure);
        var plan = new LaunchPlan { Kind = LaunchKind.Adventure, SaveSlot = slot, NewGame = newGame, Hunter = hunter };
        Created.Add(plan);
        return plan;
    }
}
