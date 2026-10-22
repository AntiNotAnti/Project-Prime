using System;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class AdventureSaveAcceptance
{
    internal static void Run()
    {
        string first = new('A', 64), second = new('B', 64);
        var services = new Stub { AdventureSlots = new[]
        {
            new RmlUiAdventureSlot(1, true, false, "SYNTHETIC ARCHIVES", 2, 79, 99, first),
            new RmlUiAdventureSlot(2, false, false, "", 0, 0, 0, "EMPTY"),
            new RmlUiAdventureSlot(3, false, true, "", 0, 0, 0, second)
        } };
        using var controller = new RmlUiMenuController(services);
        bool Flag(string key) => controller.Tick().Bindings.TryGetValue(key, out var value) && value.Boolean;
        string Text(string key) => controller.Tick().Bindings.TryGetValue(key, out var value) ? value.Text : "";
        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Adventure)
            && Flag("adventure_can_copy") && Flag("adventure_can_archive"), "native byte management uses real selected source metadata");
        Check(!controller.Dispatch(RmlUiIntentKind.AdventureCopyDestination, 1)
            && controller.Dispatch(RmlUiIntentKind.AdventureCopyDestination, 3)
            && controller.Dispatch(RmlUiIntentKind.AdventureCopy) && Flag("adventure_copy_confirmation")
            && Text("adventure_confirmation_description").Contains(first) && Text("adventure_confirmation_description").Contains(second)
            && services.AdventureSaveChanges.Count == 0, "copy captures source and occupied unreadable destination before any write");
        Check(!controller.Dispatch(RmlUiIntentKind.AdventureContinue) && !controller.Dispatch(RmlUiIntentKind.AdventureNewRun)
            && !controller.Dispatch(RmlUiIntentKind.AdventureNextHunter) && !controller.Dispatch(RmlUiIntentKind.AdventureConfirmArchive),
            "save confirmation rejects background launch, selection and wrong mutation kind");
        var projected = RmlUiMenuPageBindings.Project(RmlUiMenuPage.AdventureConfirm, controller.Tick().Bindings);
        Check(projected["visible:adventure_confirm_copy"].Boolean && !projected["visible:adventure_confirm_new_run"].Boolean
            && projected["visible:adventure_cancel_save_change"].Boolean,
            "same native modal projects only the captured save-management confirmation actions");
        Check(controller.Dispatch(RmlUiIntentKind.AdventureCancelSaveChange) && !Flag("adventure_confirmation_open")
            && !controller.Dispatch(RmlUiIntentKind.AdventureConfirmCopy) && services.AdventureSaveChanges.Count == 0,
            "cancel retires captured copy without service mutation");
        controller.Dispatch(RmlUiIntentKind.AdventureCopy);
        services.AdventureSlots[2] = services.AdventureSlots[2] with { Fingerprint = new string('C', 64) };
        Check(!controller.Dispatch(RmlUiIntentKind.AdventureConfirmCopy) && services.AdventureSaveChanges.Count == 0
            && Text("adventure_launch_status").Contains("REFUSED") && !Flag("adventure_confirmation_open"),
            "fresh destination mismatch rejects old confirmation without optimistic copy state");
        Check(controller.Dispatch(RmlUiIntentKind.AdventureCopy) && controller.Dispatch(RmlUiIntentKind.AdventureConfirmCopy)
            && services.AdventureSaveChanges.Count == 1 && services.AdventureSaveChanges[0].Destination?.Fingerprint == new string('C', 64)
            && !controller.Dispatch(RmlUiIntentKind.AdventureConfirmCopy), "new exact confirmation emits one existing-owner copy command");
        controller.Dispatch(RmlUiIntentKind.AdventureCopy);
        Check(controller.Dispatch(RmlUiIntentKind.AdventureRefresh) && !Flag("adventure_confirmation_open")
            && !controller.Dispatch(RmlUiIntentKind.AdventureConfirmCopy) && services.AdventureSaveChanges.Count == 1,
            "explicit refresh retires management confirmation without writing");
        controller.Dispatch(RmlUiIntentKind.AdventureSelectSlot, 3);
        controller.Dispatch(RmlUiIntentKind.AdventureArchive);
        services.AdventureSaveManagementAvailable = false;
        int attempts = services.AdventureSaveChangeAttempts;
        Check(!controller.Dispatch(RmlUiIntentKind.AdventureConfirmArchive) && services.AdventureSaveChangeAttempts == attempts
            && services.AdventureSaveChanges.Count == 1 && !Flag("adventure_can_archive"),
            "active or pending owner refusal consumes proposal without entering any save writer");
        services.AdventureSaveManagementAvailable = true;
        Check(controller.Dispatch(RmlUiIntentKind.AdventureArchive) && controller.Dispatch(RmlUiIntentKind.AdventureConfirmArchive)
            && services.AdventureSaveChanges.Count == 2 && services.AdventureSlots[2].Fingerprint == "EMPTY"
            && !controller.Dispatch(RmlUiIntentKind.AdventureConfirmArchive), "recoverable archive emits once and refreshes actual resulting slots");
        controller.Dispatch(RmlUiIntentKind.AdventureSelectSlot, 1); controller.Dispatch(RmlUiIntentKind.AdventureArchive);
        Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home)
            && !controller.Dispatch(RmlUiIntentKind.AdventureConfirmArchive) && services.AdventureSaveChanges.Count == 2,
            "page retirement makes captured save command unusable");
        AdventureSaveAsyncAcceptance.Run();
#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
        AndroidWorkAcceptance.Run();
#endif
    }
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("RML ADVENTURE SAVE PASS " + label); }
}
