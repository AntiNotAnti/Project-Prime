using MphRead;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Hunters;
using System.Reflection;

// Exercise the real presenter against shipped markup. A permissive fake native
// bridge cannot detect a presenter publishing a binding to a removed element.
internal static class HunterPresenterContractAcceptance
{
    internal static int Run(string assetRoot)
    {
        int viewportWidth = 1280, viewportHeight = 720;
        string? requestedSize = Environment.GetEnvironmentVariable("PRIME_HUNTER_CONTRACT_SIZE");
        if (!string.IsNullOrWhiteSpace(requestedSize))
        {
            string[] dimensions = requestedSize.Split('x');
            if (dimensions.Length != 2
                || !int.TryParse(dimensions[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out viewportWidth)
                || !int.TryParse(dimensions[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out viewportHeight)
                || viewportWidth is < 320 or > 4096 || viewportHeight is < 320 or > 4096)
                throw new ArgumentException("PRIME_HUNTER_CONTRACT_SIZE must be WIDTHxHEIGHT with each dimension from 320 through 4096.");
        }
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
            Check(host.Initialize(viewportWidth, viewportHeight, 1, assetRoot, RmlUiRenderBackend.DrawList),
                $"actual native CPU backend initialized at {viewportWidth}x{viewportHeight}");
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
                if (hunter == Hunter.Samus && LocalBodyAppearanceRules.Available)
                {
                    Check(host.FocusDocument(presenter.PreviewDocument, "hunter_body1"), "local body choice accepts native focus");
                    host.Update(); host.DiscardIntents();
                    Check(host.TryGetElementBounds(presenter.PreviewDocument, "hunter_body1", out float bx, out float by,
                        out float bw, out float bh) && bw > 0 && bh > 0, "local body choice has native hit geometry");
                    host.Input.PointerButton(0, bx + bw / 2, by + bh / 2, true);
                    host.Input.PointerButton(0, bx + bw / 2, by + bh / 2, false);
                    host.Update();
                    Check(host.TryTakeIntent(out var body) && body.Kind == RmlUiIntentKind.HunterBodySelect
                        && body.Argument == 1 && presenter.Handle(body), "native body click reaches the existing Hunters draft owner");
                    sequence = body.Sequence + 1;
                    Check(!presenter.CanNavigateAway && choice.Writes == 0, "body selection is an unsaved local draft");
                    Check(presenter.Handle(new(RmlUiIntentKind.HunterBodySelect, 2, presenter.PreviewDocument, sequence++)),
                        "Overcharged is a separate finite body choice");
                    Check(presenter.Handle(new(RmlUiIntentKind.HunterResetPreview, 0, presenter.PreviewDocument, sequence++))
                        && presenter.CanNavigateAway && choice.Writes == 0, "discard restores native body without a profile write");
                    host.Update(); refreshes += 3;
                }
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
            if (LocalBodyAppearanceRules.Available) BodyPersistence(host, pages, isolated, viewportWidth, viewportHeight);
            Console.WriteLine($"HUNTER PRESENTER CONTRACT PASS viewport {viewportWidth}x{viewportHeight}; all 7 Hunters; {refreshes} category/beam/color/suit publications; actual native strict binding targets and isolated Weavel body Apply/Discard/reopen persistence; no GL window, game files, account or real player writes.");
            return 0;
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", previous);
            string target = Path.GetFullPath(isolated);
            string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(target).StartsWith("prime-hunter-presenter-contract-", StringComparison.Ordinal))
                throw new InvalidOperationException("Presenter fixture cleanup escaped its isolated temporary directory.");
            Directory.Delete(target, recursive: true);
        }
    }

    private static void Check(bool pass, string label)
    {
        if (!pass) throw new InvalidOperationException("HUNTER PRESENTER CONTRACT: " + label);
        Console.WriteLine("HUNTER PRESENTER CONTRACT PASS " + label);
    }

    private static void BodyPersistence(RmlUiHost host, RmlUiPageManager pages, string isolated, int viewportWidth, int viewportHeight)
    {
        string oldDirectory = LauncherPrefs.Directory;
        var oldChoice = new LauncherHunterChoice(LauncherPrefs.LastHunter, LauncherPrefs.LastColor, LauncherPrefs.LocalBody);
        string directory = Path.Combine(isolated, "saved-hunters"); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "launcher.txt");
        var store = new PersistedChoiceStore(); var cosmetics = new CosmeticService();
        try
        {
            LauncherPrefs.Directory = directory; LauncherPrefs.LastHunter = Hunter.Weavel;
            LauncherPrefs.LastColor = 0; LauncherPrefs.LocalBody = LocalBodyAppearance.Native;
            Check(LauncherPrefs.TrySave(), "fixture creates only its isolated launcher preference file");
            foreach (var body in new[] { LocalBodyAppearance.TrooperPower, LocalBodyAppearance.TrooperIce,
                LocalBodyAppearance.TrooperWave, LocalBodyAppearance.TrooperPlasma, LocalBodyAppearance.Native })
            {
                int writes = store.Writes; byte[] saved = File.ReadAllBytes(path);
                using (var presenter = new NativeHunterPagePresenter(pages, store, cosmetics, host))
                {
                    presenter.Open(); host.Update();
                    Click(presenter, "hunter_body" + (int)body, RmlUiIntentKind.HunterBodySelect, (int)body);
                    Check(Draft(presenter).Body == body && !presenter.CanNavigateAway && store.Writes == writes
                        && File.ReadAllBytes(path).AsSpan().SequenceEqual(saved), "native Weavel body click stages without persistence: " + body);
                    Click(presenter, "hunter_apply", RmlUiIntentKind.HunterApply, 0);
                    Check(store.Writes == writes + 1 && store.Capture() == new LauncherHunterChoice(Hunter.Weavel, 0, body)
                        && presenter.CanNavigateAway && cosmetics.Writes == 0
                        && File.ReadAllLines(path).Contains("local_body=" + body), "actual Apply saves exact Weavel body through launcher preferences: " + body);
                    Check(!host.FocusDocument(presenter.PreviewDocument, "hunter_apply") && store.Writes == writes + 1,
                        "clean Apply is natively disabled after its one persisted write: " + body);
                }
                // Reload disk through the real preferences reader before creating
                // a new presenter; a retained in-memory fake cannot pass this.
                LauncherPrefs.LastHunter = Hunter.Samus; LauncherPrefs.LastColor = 3; LauncherPrefs.LocalBody = LocalBodyAppearance.Native;
                LauncherPrefs.Load();
                Check(store.Capture() == new LauncherHunterChoice(Hunter.Weavel, 0, body), "real preference reload retains the exact saved Trooper: " + body);
                using (var reopened = new NativeHunterPagePresenter(pages, store, cosmetics, host))
                {
                    reopened.Open(); host.Update();
                    Check(Draft(reopened) == store.Capture() && reopened.CanNavigateAway && store.Writes == writes + 1,
                        "reopened native presenter captures the persisted body: " + body);
                    var other = body is LocalBodyAppearance.TrooperPlasma or LocalBodyAppearance.Native
                        ? LocalBodyAppearance.TrooperPower : (LocalBodyAppearance)((int)body + 1);
                    byte[] applied = File.ReadAllBytes(path);
                    Click(reopened, "hunter_body" + (int)other, RmlUiIntentKind.HunterBodySelect, (int)other);
                    Check(!reopened.CanNavigateAway && Draft(reopened).Body == other, "another Trooper remains an unsaved draft after reopen");
                    Click(reopened, "hunter_discard", RmlUiIntentKind.HunterResetPreview, 0);
                    Check(reopened.CanNavigateAway && Draft(reopened).Body == body && store.Writes == writes + 1
                        && File.ReadAllBytes(path).AsSpan().SequenceEqual(applied), "native Discard restores saved Trooper without rewriting preferences: " + body);
                    Check(!host.FocusDocument(reopened.PreviewDocument, "hunter_body1") && !host.FocusDocument(reopened.PreviewDocument, "hunter_body2"),
                        "Weavel cannot focus either Samus-only imported body button");
                }
            }
            Check(store.Writes == 5 && cosmetics.Writes == 0, "four Trooper and one Native Apply make five local identity writes and no cosmetic/account request");
        }
        finally
        {
            LauncherPrefs.Directory = oldDirectory; LauncherPrefs.LastHunter = oldChoice.Hunter;
            LauncherPrefs.LastColor = oldChoice.Suit; LauncherPrefs.LocalBody = oldChoice.Body;
        }
        void Click(NativeHunterPagePresenter presenter, string id, RmlUiIntentKind kind, int argument)
        {
            var document = presenter.PreviewDocument;
            host.SetBool(document, "class:@document:reduce-motion", true);
            host.SetBool(document, "class:screen:reduce-motion", true);
            Check(host.FocusDocument(document, id), "actual persisted-loadout control focuses: " + id);
            for (int n = 0; n < 8; n++) { Thread.Sleep(15); host.Update(); }
            Check(host.TryGetElementBounds(document, id, out float x, out float y, out float width, out float height)
                && width > 0 && height > 0, "actual persisted-loadout control has native hit bounds: " + id);
            Check(float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(width) && float.IsFinite(height)
                && x + width / 2 >= 0 && x + width / 2 < viewportWidth
                && y + height / 2 >= 0 && y + height / 2 < viewportHeight,
                "native focus/scroll exposes the control hit center inside the requested viewport: " + id);
            host.DiscardIntents();
            host.Input.PointerMoved(x + width/2, y + height/2);
            host.Input.PointerButton(0, x + width/2, y + height/2, true);
            host.Input.PointerButton(0, x + width/2, y + height/2, false); host.Update();
            Check(host.TryTakeIntent(out var intent) && intent.Document == document && intent.Kind == kind && intent.Argument == argument
                && presenter.Handle(intent) && !host.TryTakeIntent(out _), "actual native click emits and consumes exactly one loadout command: " + id);
            Check(!presenter.Handle(intent), "duplicate native command sequence cannot write or apply twice: " + id);
            host.Update();
        }
        static LauncherHunterChoice Draft(NativeHunterPagePresenter presenter)
            => ((LauncherHunterChoiceController)typeof(NativeHunterPagePresenter).GetField("_controller", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(presenter)!).Current().Choice;
    }

    private sealed class PersistedChoiceStore : ILauncherHunterChoiceStore
    {
        private readonly LauncherHunterChoiceStore _actual = new();
        internal int Writes;
        public LauncherHunterChoice Capture() => _actual.Capture();
        public void Save(LauncherHunterChoice choice) { _actual.Save(choice); Writes++; }
    }

    private sealed class ChoiceStore(Hunter hunter) : ILauncherHunterChoiceStore
    {
        public int Writes;
        private LauncherHunterChoice _saved = new(hunter, 0);
        public LauncherHunterChoice Capture() => _saved;
        public void Save(LauncherHunterChoice choice) { _saved = choice; Writes++; }
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
