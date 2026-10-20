using MphRead;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Hunters;

// Exercise the real presenter against shipped markup. A permissive fake native
// bridge cannot detect a presenter publishing a binding to a removed element.
internal static class HunterPresenterContractAcceptance
{
    internal static int Run(string assetRoot)
    {
        assetRoot = Path.GetFullPath(assetRoot);
        if (!File.Exists(Path.Combine(assetRoot, "pages", "hunters", "native-picker.rml")))
            throw new ArgumentException("Pass the published RmlUi asset directory.");
        string? previous = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        string isolated = Path.Combine(Path.GetTempPath(), "prime-hunter-presenter-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolated);
        Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", isolated);
        try
        {
            using var host = new RmlUiHost();
            Check(host.Initialize(1280, 720, 1, assetRoot, RmlUiRenderBackend.DrawList), "actual native CPU backend initialized");
            using var pages = new RmlUiPageManager(host);
            int refreshes = 0;
            foreach (Hunter hunter in LauncherHunterChoiceController.Playable)
            {
                var choice = new ChoiceStore(hunter);
                var cosmetics = new CosmeticService();
                using var presenter = new NativeHunterPagePresenter(pages, choice, cosmetics, host);
                var home = pages.OpenPage(new("hunter-contract-home", "pages/home/home.rml", "nav_hunters"));
                host.SetBool(home, "class:@document:reduce-motion", true);
                host.SetBool(home, "class:screen:reduce-motion", true);
                host.Update();
                Check(host.TryGetElementBounds(home, "nav_hunters", out float x, out float y, out float w, out float h)
                    && w > 0 && h > 0, "shipped Home header has a native Hunters target");
                host.DiscardIntents();
                host.Input.PointerButton(0, x + w / 2, y + h / 2, true);
                host.Input.PointerButton(0, x + w / 2, y + h / 2, false);
                host.Update();
                Check(host.TryTakeIntent(out var route) && route.Kind == RmlUiIntentKind.Navigate
                    && route.Argument == (int)RmlUiRouteArgument.HunterLicense && pages.Accept(route),
                    "actual native Home Hunters click emits the accepted production route");
                presenter.Open();
                host.Update();
                Check(host.IsAlive(presenter.PreviewDocument), "real presenter opens shipped picker for " + hunter);
                ulong sequence = 1;
                foreach (int category in Enumerable.Range(0, 5))
                {
                    Check(presenter.Handle(new(RmlUiIntentKind.HunterCosmeticCategory, category, presenter.PreviewDocument, sequence++)), "category publication " + category);
                    host.Update(); refreshes++;
                }
                for (int item = 0; item < 9; item++)
                {
                    Check(presenter.Handle(new(RmlUiIntentKind.HunterBeamNext, 0, presenter.PreviewDocument, sequence++)), "cycle-only beam publication " + item);
                    Check(presenter.Handle(new(RmlUiIntentKind.HunterBeamColorNext, 0, presenter.PreviewDocument, sequence++)), "cycle-only color publication " + item);
                    host.Update(); refreshes += 2;
                }
                for (int suit = 0; suit < 4; suit++)
                {
                    Check(presenter.Handle(new(RmlUiIntentKind.HunterSuit, suit, presenter.PreviewDocument, sequence++)), "suit publication " + suit);
                    host.Update(); refreshes++;
                }
                Check(choice.Writes == 0 && cosmetics.Writes == 0, "publication does not save identity or contact an account");
            }
            Console.WriteLine($"HUNTER PRESENTER CONTRACT PASS all 7 Hunters; {refreshes} category/beam/color/suit publications; actual native strict binding targets; no GL window, game files, account or player writes.");
            return 0;
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", previous);
            Directory.Delete(isolated, recursive: true);
        }
    }

    private static void Check(bool pass, string label)
    {
        if (!pass) throw new InvalidOperationException("HUNTER PRESENTER CONTRACT: " + label);
        Console.WriteLine("HUNTER PRESENTER CONTRACT PASS " + label);
    }

    private sealed class ChoiceStore(Hunter hunter) : ILauncherHunterChoiceStore
    {
        public int Writes;
        public LauncherHunterChoice Capture() => new(hunter, 0);
        public void Save(LauncherHunterChoice choice) => Writes++;
    }

    private sealed class CosmeticService : ILauncherCosmeticEquipService
    {
        public int Writes;
        public CosmeticLoadout Capture(Hunter hunter) => CosmeticLoadout.Default;
        public bool PendingSync(Hunter hunter) => false;
        public Task<string> EquipAsync(Hunter hunter, CosmeticLoadout loadout, CancellationToken token)
        { Writes++; return Task.FromResult("SYNTHETIC CONTRACT SERVICE"); }
    }
}
