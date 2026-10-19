using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class AdventureSaveAsyncAcceptance
{
    internal static void Run()
    {
        string hash = new('A', 64);
        var source = new RmlUiAdventureSlot(1, true, false, "SYNTHETIC ARCHIVES", 2, 79, 99, hash);
        var services = new Stub { AdventureSaveAsync = true, AdventureSlots = new[] { source,
            new RmlUiAdventureSlot(2, false, false, "", 0, 0, 0, "EMPTY"),
            new RmlUiAdventureSlot(3, false, true, "", 0, 0, 0, new string('B', 64)) } };
        var pending = new TaskCompletionSource<RmlUiAdventureSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        RmlUiAdventureSaveChange captured = default;
        int requests = 0;
        services.AsyncAdventureSave = (change, cancellation) => { captured = change; token = cancellation; requests++; return pending.Task; };
        using var controller = new RmlUiMenuController(services);
        bool Flag(string key) => controller.Tick().Bindings.TryGetValue(key, out var v) && v.Boolean;
        string Status() => controller.Tick().Bindings["adventure_launch_status"].Text;
        void Begin()
        {
            pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Check(controller.Dispatch(RmlUiIntentKind.AdventureCopy) && controller.Dispatch(RmlUiIntentKind.AdventureConfirmCopy), "async exact proposal admits one request");
        }
        void Drain() { for (int i = 0; i < 100 && controller.Busy; i++) { controller.Tick(); Thread.Sleep(5); } controller.Tick(); }
        controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Adventure);
        Begin();
        Check(controller.Busy && Flag("adventure_save_busy") && !Flag("adventure_confirmation_open")
            && requests == 1 && services.AdventureSaveChangeAttempts == 0 && Status().Contains("PENDING"),
            "pending async admission never invokes synchronous writer or claims success");
        Check(!Flag("adventure_can_copy") && !Flag("adventure_can_archive") && !Flag("adventure_can_resume")
            && !Flag("adventure_can_new_run"), "pending save disables every new mutation and launch affordance");
        foreach (var intent in new[] { new RmlUiIntent(RmlUiIntentKind.AdventureSelectSlot, 3, default, 1),
            new(RmlUiIntentKind.AdventureCopyDestination, 3, default, 1), new(RmlUiIntentKind.AdventureNextHunter, 0, default, 1),
            new(RmlUiIntentKind.AdventureNewRun, 0, default, 1), new(RmlUiIntentKind.AdventureContinue, 0, default, 1),
            new(RmlUiIntentKind.AdventureConfirmCopy, 0, default, 1), new(RmlUiIntentKind.CampaignShipAction, 10, default, 1) })
            Check(!controller.Dispatch(intent.Kind, intent.Argument), "pending exact save cannot change source or launch " + intent.Kind);
        var fields = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Adventure, controller.Tick().Bindings);
        Check(fields["visible:adventure_cancel_save_work"].Boolean && fields["disabled:adventure_slot1"].Boolean
            && fields["disabled:adventure_next_hunter"].Boolean, "pending native view preserves accessible cancellation and fences slot/hunter focus");
        Check(controller.Dispatch(RmlUiIntentKind.AdventureCancelSaveChange) && token.IsCancellationRequested
            && !Flag("adventure_save_busy") && Status().Contains("NOT UNDONE"), "in-flight cancellation retires authority without a rollback claim");
        pending.SetResult(new(captured, true, "FORBIDDEN STALE SUCCESS")); Thread.Sleep(15); controller.Tick();
        Check(!Status().Contains("FORBIDDEN") && services.AdventureSaveChanges.Count == 0, "late canceled completion cannot publish success or mutate fake saves");
        Begin(); var refreshTask = pending; var refreshChange = captured;
        Check(controller.Dispatch(RmlUiIntentKind.AdventureRefresh) && token.IsCancellationRequested && !controller.Busy,
            "explicit Refresh cancels queued save work before reading current slots");
        refreshTask.SetResult(new(refreshChange, true, "FORBIDDEN REFRESH SUCCESS")); Thread.Sleep(15); controller.Tick();
        Check(!Status().Contains("FORBIDDEN"), "retired refresh epoch cannot publish old result");
        Begin(); var oldTask = pending; var oldChange = captured;
        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home) && token.IsCancellationRequested,
            "route retirement cancels old save epoch");
        controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Adventure);
        oldTask.SetResult(new(oldChange, true, "FORBIDDEN ROUTE SUCCESS")); Thread.Sleep(15); controller.Tick();
        Check(!Status().Contains("FORBIDDEN") && !Flag("adventure_save_busy"), "reopened Adventure rejects retired route completion");
        Begin(); pending.SetResult(new(captured with { Source = source with { Fingerprint = new string('C', 64) } }, true, "WRONG CHANGE")); Drain();
        Check(Status().Contains("UNVERIFIED") && !Status().Contains("WRONG CHANGE"), "same-epoch response must name exact captured source/destination");
        Begin(); pending.SetResult(new(captured, true, "UNBACKED VERIFIED")); Drain();
        Check(Status().Contains("UNVERIFIED") && !Status().Contains("UNBACKED"), "successful response without fresh exact resulting bytes cannot claim verification");
        Begin(); pending.SetException(new InvalidOperationException("PRIVATE PATH OR SESSION DETAIL")); Drain();
        Check(Status().Contains("UNVERIFIED") && !Status().Contains("PRIVATE"), "writer exception body is not exposed by async save work");
        Begin(); pending.SetResult(new(captured, false, "SYNTHETIC OWNER REFUSED STALE DESTINATION")); Drain();
        Check(Status().Contains("REFUSED") && !controller.Busy && services.AdventureSaveChanges.Count == 0,
            "owner refusal is presented only after completion and permits explicit retry");
        Begin(); Check(services.ManageAdventureSave(captured, out string verified), "synthetic existing owner verifies exact queued bytes");
        pending.SetResult(new(captured, true, verified)); Drain();
        Check(Status() == verified && services.AdventureSaveChanges.Count == 1 && !controller.Busy,
            "only exact completed result plus actual fresh slot readback reports verified copy");
        controller.Dispatch(RmlUiIntentKind.AdventureCopyDestination, 3);
        Begin(); Check(services.ManageAdventureSave(captured, out string committedStatus), "synthetic owner may finish before cancellation reaches UI authority");
        Check(controller.Dispatch(RmlUiIntentKind.AdventureCancelSaveChange) && services.AdventureSlots[2].Fingerprint == hash
            && Status().Contains("NOT UNDONE") && controller.Tick().Bindings["adventure_slot3_label"].Text.Contains("SYNTHETIC ARCHIVES"),
            "cancellation after effect preserves observed new bytes and explicitly refuses a rollback claim");
        pending.SetResult(new(captured, true, committedStatus)); Thread.Sleep(15); controller.Tick();
        Check(Status().Contains("NOT UNDONE") && services.AdventureSaveChanges.Count == 2,
            "post-effect canceled completion cannot repeat writer or replace retirement status with success");
        Begin(); var disposedTask = pending; var disposedChange = captured; controller.Dispose();
        Check(token.IsCancellationRequested, "controller disposal cancels queued owner callback");
        disposedTask.SetResult(new(disposedChange, true, "FORBIDDEN DISPOSE SUCCESS"));
    }
    private static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); Console.WriteLine("RML ADVENTURE ASYNC PASS " + label); }
}
