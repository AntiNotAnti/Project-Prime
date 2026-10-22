using MphRead.Mods.Launcher.RmlUi.Host;

internal static class AdventureAcceptance
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var capabilities = RmlUiLauncherCapabilities.ConfiguredLocalAdventure;
        var native = new FakeBridge();
        using var host = new RmlUiHost(native);
        check(host.Initialize(640, 360, 1, root, RmlUiRenderBackend.DrawList), "Adventure-capable local host initializes");
        using var pages = new RmlUiLauncherPages(host, capabilities: capabilities);
        var home = pages.ShowBaseline();
        check(!native.Bools.ContainsKey((home.DocumentId, "visible:drawer_adventure"))
            && !native.Bools.ContainsKey((home.DocumentId, "visible:home_drawer_adventure")), "Adventure capability retains authored native Adventure choices");
        check(native.Bools.GetValueOrDefault((home.DocumentId, "visible:nav_community"), 1) == 0,
            "Adventure capability preserves unsupported online/community restriction");
        check(pages.HandleIntent(new(RmlUiIntentKind.StageSelect, 3, home, 1), out var stage)
            && stage.Kind == RmlUiIntentKind.StageSelect && pages.SelectedActivityIndex == 3,
            "Adventure activity is explicitly selectable by the configured local host");
        check(pages.HandleIntent(new(RmlUiIntentKind.HomeDeploy, 0, home, 2), out var deploy)
            && deploy.Kind == RmlUiIntentKind.Navigate && deploy.Argument == (int)RmlUiRouteArgument.Adventure,
            "native Home deploy carries Adventure route to the existing controller");
        pages.SetBool("adventure_mode", true); pages.SetBool("home_mode", false);
        pages.SetBool("adventure_can_copy", false); pages.SetBool("adventure_can_archive", false);
        pages.Flush(); host.Update(); pages.AfterUpdate();
        var savePage = pages.Document;
        check(pages.Page == RmlUiMenuPage.Adventure && native.OpenedPaths.Last().EndsWith("pages/adventure/saves.rml", StringComparison.Ordinal),
            "Adventure model opens the real authored saves route under local-only restriction");
        foreach (var kind in new[] { RmlUiIntentKind.AdventureContinue, RmlUiIntentKind.AdventureNewRun,
            RmlUiIntentKind.AdventureRefresh, RmlUiIntentKind.AdventureNextHunter })
            check(capabilities.Allows(new(kind, 0, savePage, 3)), "Adventure capability admits existing launch/navigation family " + kind);
        foreach (var kind in new[] { RmlUiIntentKind.AdventureCopy, RmlUiIntentKind.AdventureArchive,
            RmlUiIntentKind.AdventureConfirmCopy, RmlUiIntentKind.AdventureConfirmArchive, RmlUiIntentKind.AdventureCancelSaveChange })
            check(!capabilities.Allows(new(kind, 0, savePage, 3)), "unowned Android byte-management family stays rejected " + kind);
        check(!capabilities.Allows(new(RmlUiIntentKind.AdventureCopyDestination, 2, savePage, 3)), "unowned copy destination cannot be forged");
        check(capabilities.Allows(new(RmlUiIntentKind.CampaignShipAction, 10, savePage, 3))
            && !capabilities.Allows(new(RmlUiIntentKind.CampaignShipAction, 2, savePage, 3)), "only existing ship-open action passes parent launcher capability");
        pages.SetBool("adventure_confirmation_open", true); pages.Flush(); host.Update(); pages.AfterUpdate();
        var confirmation = pages.Manager.Top;
        check(confirmation != savePage && pages.Back(out var cancel) && cancel.Kind == RmlUiIntentKind.AdventureCancelNewRun,
            "New Run confirmation retains native modal and Back cancellation owner");
        check(pages.HandleIntent(new(RmlUiIntentKind.AdventureContinue, 0, savePage, 4), out var covered) && covered.Kind == 0,
            "covered native Adventure page cannot launch through confirmation");
        pages.SetBool("adventure_confirmation_open", false); pages.SetBool("adventure_can_new_run", true);
        pages.Flush(); host.Update(); pages.AfterUpdate();
        check(!host.IsAlive(confirmation) && native.Focuses.Last().Element == "adventure_new_run",
            "native New Run cancellation retires document and defers source focus restoration");
        check(!pages.HandleIntent(new(RmlUiIntentKind.AdventureConfirmNewRun, 0, confirmation, 5), out _),
            "retired Adventure confirmation cannot start a later scene");
        var management = RmlUiLauncherCapabilities.ConfiguredLocalAdventureSaves;
        foreach (var kind in new[] { RmlUiIntentKind.AdventureCopy, RmlUiIntentKind.AdventureArchive,
            RmlUiIntentKind.AdventureConfirmCopy, RmlUiIntentKind.AdventureConfirmArchive, RmlUiIntentKind.AdventureCancelSaveChange })
            check(management.Allows(new(kind, 0, savePage, 6)), "explicit save-owner capability admits only validated management family " + kind);
        check(management.Allows(new(RmlUiIntentKind.AdventureCopyDestination, 3, savePage, 6))
            && !management.Allows(new(RmlUiIntentKind.AdventureCopyDestination, 0, savePage, 6)), "management opt-in retains finite destination validation");
    }
}
