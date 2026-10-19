using System.Xml.Linq;
using MphRead;
using MphRead.Droid;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class HunterAcceptance
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var native = new FakeBridge();
        using var host = new RmlUiHost(native);
        check(host.Initialize(1280, 720, 1, root, RmlUiRenderBackend.DrawList), "Hunter fake host initializes");
        using var manager = new RmlUiPageManager(host);
        ulong sequence = 0;
        RmlUiIntent Intent(string action, RmlUiDocumentToken document)
        {
            check(RmlUiIntentRegistry.TryParseLegacy(action, document, ++sequence, out var intent), "Hunter action decodes: " + action);
            return intent;
        }
        bool Send(RmlUiAndroidLocalHunterPage page, string action, RmlUiDocumentToken document = default)
            => page.Handle(Intent(action, document == default ? manager.Page : document));
        string Status() => native.Strings[(manager.Page.DocumentId, "local_hunter_status")];
        void Selected(int hunter, int suit)
        {
            for (int i = 0; i < 7; i++) check(native.Bools[(manager.Page.DocumentId, "class:local_hunter" + i + ":selected")] == (i == hunter ? 1 : 0), "actual Hunter selected CSS binding " + i);
            for (int i = 0; i < 4; i++) check(native.Bools[(manager.Page.DocumentId, "class:local_suit" + i + ":selected")] == (i == suit ? 1 : 0), "actual suit selected CSS binding " + i);
        }

        var store = new ChoiceStore(new(Hunter.Samus, 0));
        using (var page = new RmlUiAndroidLocalHunterPage(manager, store))
        {
            page.Open();
            check(manager.PageKey == "android-local-hunter", "actual local Hunter document opens");
            check(native.Strings[(manager.Page.DocumentId, "local_hunter_name")] == "SAMUS", "default Hunter name published");
            Selected(0, 0);
            check(native.Bools[(manager.Page.DocumentId, "disabled:local_apply")] == 1, "clean default disables Apply");
            check(Send(page, "hunter:select:6") && Send(page, "hunter:suit:3"), "Hunter and suit draft choices accepted");
            Selected(6, 3);
            check(store.Saved == new LauncherHunterChoice(Hunter.Samus, 0) && store.Attempts == 0, "draft never saves implicitly");
            check(!page.Back() && !page.Closed && Status().Contains("APPLY OR DISCARD"), "pending draft refuses platform Back with explicit notice");
            check(!Send(page, "hunter:cancel") && !page.Closed, "pending draft also refuses RML Back");
            check(Send(page, "hunter:reset-preview") && Status() == "HUNTER SELECTION DISCARDED", "explicit local discard resets draft");
            Selected(0, 0);
            check(store.Attempts == 0 && Send(page, "hunter:cancel") && page.Closed, "discard permits Back without persistence");
        }

        using (var page = new RmlUiAndroidLocalHunterPage(manager, store))
        {
            page.Open();
            check(Send(page, "hunter:select:2") && Send(page, "hunter:suit:1"), "persisted selection draft accepts choices");
            var document = manager.Page;
            check(!Send(page, "hunter:apply", new(document.Generation + 1, document.DocumentId)) && store.Attempts == 0,
                "foreign generation cannot persist draft");
            var modal = manager.OpenModal(new("fixture-modal", "pages/offline/arena.rml"));
            check(!Send(page, "hunter:apply", document) && store.Attempts == 0, "background Hunter document cannot persist through modal");
            manager.CloseModal();
            var apply = Intent("hunter:apply", document);
            check(page.Handle(apply) && store.Saved == new LauncherHunterChoice(Hunter.Trace, 1) && store.Attempts == 1,
                "actual controller persists selected Hunter and suit through injected store");
            check(Status() == "HUNTER AND SUIT EQUIPPED", "successful persistence publishes equipped status");
            Selected(2, 1);
            check(!page.Handle(apply) && store.Attempts == 1, "duplicate Apply cannot write twice");
            check(page.Back() && page.Closed, "saved profile permits Back");
            check(!page.Handle(Intent("hunter:apply", document)) && store.Attempts == 1, "closed presenter rejects late Apply");
        }

        store.ThrowOnSave = true;
        using (var page = new RmlUiAndroidLocalHunterPage(manager, store))
        {
            page.Open(); Selected(2, 1);
            check(Send(page, "hunter:select:4"), "failed-store draft choice accepted");
            check(!Send(page, "hunter:apply") && store.Saved == new LauncherHunterChoice(Hunter.Trace, 1), "store failure does not change saved profile");
            check(Status().StartsWith("COULD NOT EQUIP:") && !Status().Contains("EQUIPPED"), "store failure never claims equipped");
            check(!page.Back(), "store failure leaves draft pending");
            check(Send(page, "hunter:reset-preview") && page.Back(), "failed draft can discard then return");
        }
        store.ThrowOnSave = false;
        using (var page = new RmlUiAndroidLocalHunterPage(manager, store))
        {
            page.Open(); var retired = manager.Page;
            Send(page, "hunter:select:5");
            int before = store.Attempts;
            manager.OpenPage(RmlUiMenuPages.Home);
            check(!page.Handle(Intent("hunter:apply", retired)) && store.Attempts == before,
                "retired Hunter document cannot persist after route replacement");
        }
        using (var page = new RmlUiAndroidLocalHunterPage(manager, store))
        {
            page.Open(); Send(page, "hunter:suit:3");
            store.Saved = new(Hunter.Spire, 2);
            int before = store.Attempts;
            check(!Send(page, "hunter:apply") && store.Attempts == before && Status().Contains("PROFILE CHANGED ELSEWHERE"),
                "concurrent profile change is rejected before persistence");
            check(Send(page, "hunter:reset-preview"), "discard reloads current profile");
            Selected(5, 2);
            check(page.Back(), "reloaded profile is clean after discard");
        }

        // Exercise the presenter's actual default Store adapter against preference stubs.
        LauncherPrefs.LastHunter = Hunter.Noxus; LauncherPrefs.LastColor = 2; LauncherPrefs.SaveAttempts = 0;
        using (var page = new RmlUiAndroidLocalHunterPage(manager))
        {
            page.Open(); Selected(4, 2);
            check(Send(page, "hunter:select:1") && Send(page, "hunter:apply")
                && LauncherPrefs.LastHunter == Hunter.Kanden && LauncherPrefs.LastColor == 2 && LauncherPrefs.SaveAttempts == 1,
                "default Store adapter captures and saves local preferences");
        }
        LauncherPrefs.SaveSucceeds = false;
        using (var page = new RmlUiAndroidLocalHunterPage(manager))
        {
            page.Open(); Send(page, "hunter:suit:0");
            check(!Send(page, "hunter:apply") && LauncherPrefs.LastHunter == Hunter.Kanden && LauncherPrefs.LastColor == 2,
                "default Store adapter rolls preferences back on failed TrySave");
            check(Status().StartsWith("COULD NOT EQUIP:") && !page.Back(), "default store failure retains actionable unsaved draft");
        }
        LauncherPrefs.SaveSucceeds = true;

        var xml = XDocument.Load(Path.Combine(root, "src/MphRead/Mods/Launcher/RmlUi/Pages/hunters/local-picker.rml"));
        check(!xml.Descendants().Any(e => e.Name.LocalName is "input" or "textarea" or "canvas" or "object" or "iframe" or "model"),
            "local-picker contains no text input or rendered preview element");
        check(!xml.Descendants().Attributes().Any(a => a.Name.LocalName.Contains("preview") || a.Name.LocalName.Contains("model")),
            "local-picker contains no preview/model binding");
        int actions = 0;
        foreach (var attr in xml.Descendants().Attributes("data-action"))
        {
            var intent = Intent(attr.Value, host.HomeDocument);
            check(intent.Kind is RmlUiIntentKind.HunterSelect or RmlUiIntentKind.HunterSuit or RmlUiIntentKind.HunterApply
                or RmlUiIntentKind.HunterResetPreview or RmlUiIntentKind.HunterCancel,
                "local-picker action has no cosmetics, account, or 3D operation: " + attr.Value);
            check(RmlUiLauncherCapabilities.ConfiguredLocal.Allows(intent), "actual local-picker action passes production capability");
            actions++;
        }
        check(actions == 14, "local-picker exposes seven Hunters, four suits, Apply, Discard, Back");
    }

    private sealed class ChoiceStore(LauncherHunterChoice initial) : ILauncherHunterChoiceStore
    {
        internal LauncherHunterChoice Saved = initial;
        internal int Attempts;
        internal bool ThrowOnSave;
        public LauncherHunterChoice Capture() => Saved;
        public void Save(LauncherHunterChoice choice)
        {
            Attempts++;
            if (ThrowOnSave) throw new InvalidOperationException("Fixture disk failure");
            Saved = choice;
        }
    }
}
