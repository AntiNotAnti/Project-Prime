using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Settings;
using MphRead.Mods.Launcher.RmlUi.Setup;
using MphRead.Mods.Network;
using MphRead.Mods.Render.Hud;
using MphRead.Mods.StudioIntegration;
using ProjectPrime.Studio.Protocol;

static class Program
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int _checks;
    private static string _fixture = "";
    private static RmlUiHost Host => RmlUiPrototype.Runtime;
    private static RmlUiLauncherPages Pages => RmlUiPrototype.Pages!;
    private static SettingsPagePresenter Settings => Get<SettingsPagePresenter>("_nativeSettings")!;
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }

    static int Main(string[] args)
    {
        if (args.Length != 2)
        { Console.Error.WriteLine("Usage: rmlui-shell-routing-check <native library> <rmlui assets>"); return 2; }
        string library = Path.GetFullPath(args[0]), assets = Path.GetFullPath(args[1]);
        _fixture = Directory.CreateTempSubdirectory("prime-shell-routing-").FullName;
        // Set these before touching any engine static. Never copy real user data.
        Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", _fixture);
        Environment.SetEnvironmentVariable("PROJECT_PRIME_UI_PERF", Path.Combine(_fixture, "unused-perf.json"));
        string previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_fixture);
        try { return Run(library, assets); }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.SetCurrentDirectory(previous); Directory.Delete(_fixture, true); }
    }

    private static int Run(string library, string assets)
    {
        byte[] actualAssembly = File.ReadAllBytes(typeof(GameState).Assembly.Location);
        foreach (string guard in new[]
        {
            "Live account authentication is disabled in UI performance diagnostics.",
            "Live account requests are disabled in UI performance diagnostics."
        })
        {
            if (actualAssembly.AsSpan().IndexOf(Encoding.Unicode.GetBytes(guard)) < 0)
                throw new InvalidOperationException("Unverified actual client assembly: central diagnostic account guard missing.");
        }
        LauncherPrefs.Directory = GameFiles.Root = _fixture;
        Paths.SetPath("Export", Path.Combine(_fixture, "export"));
        LauncherPrefs.DebugLogs = false; LauncherPrefs.ReplayAutoPrune = false;
        HudProfiles.Load(Path.Combine(_fixture, "Savedata", "hud-profiles"));
        var oldTicket = NetSession.IdentityTicketSourceForChecks;
        NetSession.IdentityTicketSourceForChecks = NoTicket;
        nint native = NativeLibrary.Load(library);
        NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,
            (name, _, _) => name == "ProjectPrime.RmlUi.Native" ? native : 0);
        try
        {
            Check(LauncherUiPerformance.Enabled, "diagnostic account/Social/update suppression enabled before initialization");
            Check(!GameFiles.Ready, "fixture contains no game data or copied installation");
            Check(Host.Initialize(1280, 720, 1, assets, RmlUiRenderBackend.DrawList), "actual native host initializes");
            // Narrow harness seam: attach the real composition without constructing a
            // RenderWindow, starting background work, or claiming scene acceptance.
            Put(typeof(RmlUiPrototype), "_pages", new RmlUiLauncherPages(Host));
            Put(typeof(RmlUiPrototype), "_active", true);
            Put(typeof(RmlUiPrototype), "_visible", true);
            Put(typeof(RmlUiPrototype), "_width", 1280);
            Put(typeof(RmlUiPrototype), "_height", 720);
            Put(typeof(RmlUiPrototype), "_density", 1f);
            Put(typeof(Shell), "<Active>k__BackingField", true);
            Invoke(typeof(Shell), "WireNativePages");
            Home();
            HuntersPageRouting();
            PresentationPolicyChurn();
            foreach (LauncherPage destination in new[] { LauncherPage.Home, LauncherPage.Offline })
                foreach (bool save in new[] { false, true }) DirtyRoute(destination, save);
            RequiredSetup();
            BusySetup();
            DeferredStudioCancelled();
            DeferredStudioDecisionCancelled();
            foreach (bool save in new[] { false, true }) DirtyStudio(save);
            Check(Get<object>("_nativeUpdates") == null, "diagnostic polling never starts update HTTP");
            Check(!NetSession.Active && !NetHostSession.Running, "routing fixture never starts gameplay/network transport");
            Check(!Directory.EnumerateFiles(_fixture, "*", SearchOption.AllDirectories)
                .Any(p => Path.GetFileName(p).Contains("session", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(p).Contains("ticket", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(p).Contains("token", StringComparison.OrdinalIgnoreCase)), "fixture never writes account session/ticket/token files");
            Console.WriteLine($"Shared Shell native routing PASS: {_checks} assertions; real DOM/intents and engine Settings persistence, reflective presenter initialization, no RenderWindow/gameplay or production account operations.");
            return 0;
        }
        finally
        {
            Invoke(typeof(Shell), "RetireNativePages", true);
            RmlUiPrototype.Shutdown();
            Invoke(typeof(Shell), "ReleaseApplicationRouter");
            Put(typeof(Shell), "<Active>k__BackingField", false);
            NetSession.Stop(); NetSession.IdentityTicketSourceForChecks = oldTicket;
            NativeLibrary.Free(native);
        }
    }

    private static void HuntersPageRouting()
    {
        Home(); Click("nav_hunters", mouse: true);
        var hunters = Pages.Manager.Page;
        Check(Shell.ApplicationRouter.Current.Page == LauncherPage.Hunters
            && Pages.Manager.PageKey == "hunters" && Pages.Manager.ModalCount == 0,
            "Hunters top navigation opens a full page without a modal");
        Click("hunter_choice2"); Click("hunter_suit1");
        Click("header_settings", mouse: true);
        Check(Pages.Manager.PageKey == "settings" && !Host.IsAlive(hunters)
            && Get<object>("_nativeHunters") == null, "Hunters page retires when its shared Settings navigation is used");
        Home(); Click("nav_hunters", mouse: true); hunters = Pages.Manager.Page;
        Click("hunter_close", mouse: true);
        Check(Pages.Manager.PageKey == "home" && !Host.IsAlive(hunters), "Hunters Back returns to a fresh Home page");
        Home(); Click("nav_hunters"); hunters = Pages.Manager.Page;
        Check((bool)Invoke(typeof(Shell), "BackNativePage")!, "Escape/back handled by the full Hunters page"); Pump();
        Check(Pages.Manager.PageKey == "home" && !Host.IsAlive(hunters), "Escape/back retires the Hunters page and restores Home");
    }

    private static void DirtyRoute(LauncherPage destination, bool save)
    {
        Home(); Click("header_settings");
        var oldDocument = Settings.Document;
        float original = InputSettings.MouseSensitivity;
        float desired = Math.Abs(original - .37f) < .001f ? .42f : .37f;
        StageMouse(desired);
        if (destination == LauncherPage.Offline)
        {
            // Rebind the shipped navigation button through the production action
            // setter, so both logical route destinations exercise native packets.
            Host.SetText(oldDocument, "action:nav_play", "route:offline");
        }
        Click("nav_play", mouse: true);
        Check(Shell.ApplicationRouter.Current.Page == LauncherPage.Settings
            && Pages.Manager.Page == oldDocument && Pages.Manager.ModalCount == 1, "dirty navigation retains Settings until a decision");
        Check(Settings.Controller.Dirty && InputSettings.MouseSensitivity == original, "native edit stays detached until Apply");
        Click(save ? "settings_close_apply" : "settings_close_discard", mouse: true);
        Check(Shell.ApplicationRouter.Current.Page == destination, $"{(save ? "Apply" : "Discard")} reaches logical {destination} route");
        Check(Get<object>("_nativeSettings") == null && !Host.IsAlive(oldDocument), "old Settings document and Shell adapter retire together");
        Check(Math.Abs(InputSettings.MouseSensitivity - (save ? desired : original)) < .0001f, "decision commits or discards authoritative input value");
        if (save) Check(File.Exists(Path.Combine(_fixture, "controls.txt")), "Apply persists only into isolated controls file");
        if (destination == LauncherPage.Offline)
        {
            Check(Get<object>("_nativeOffline") != null && Pages.Manager.PageKey == "offline", "queued route opens actual Offline document");
            Click("nav_play", mouse: true);
        }
        Check(Pages.Manager.PageKey == "home", "actual Home document is current after return");
        Click("header_settings", mouse: true);
        Check(Get<object>("_nativeSettings") != null && Pages.Manager.PageKey == "settings", "future Home control opens fresh Settings without swallowed input");
        Console.WriteLine($"Shell route decision passed: {destination}, {(save ? "Apply" : "Discard")}.");
    }

    private static void PresentationPolicyChurn()
    {
        Home(); var page = Pages.Manager.Page;
        var policies = (IDictionary)Get<object>("_nativePresentationPolicy")!;
        int peak = policies.Count;
        for (int cycle = 0; cycle < 100; cycle++)
        {
            var modal = Pages.Manager.OpenModal(new("policy-churn-" + cycle,
                "pages/settings/unsaved.rml", "settings_close_cancel"));
            Host.Update(); Host.Render(1280, 720);
            Invoke(typeof(Shell), "ApplyNativePresentationPolicy");
            Check(policies.Contains(modal) && Host.IsVisible(modal), "actual Shell policy applies to current native modal");
            peak = Math.Max(peak, policies.Count);
            Check(Pages.Manager.CloseModal(), "actual native modal closes");
            Invoke(typeof(Shell), "ApplyNativePresentationPolicy");
            Check(!Host.IsAlive(modal) && Pages.Manager.Page == page && Pages.Manager.PageKey == "home",
                "modal retirement preserves same persistent native page");
        }
        Console.WriteLine($"Shell actual policy churn:100 modals on same page, cache peak={peak}, retained={policies.Count}.");
        Check(peak <= 3 && policies.Count <= 3, "actual Shell presentation policy cache stays bounded under modal churn");
        for (int idle = 0; idle < 50; idle++) Invoke(typeof(Shell), "ApplyNativePresentationPolicy");
        Check(policies.Count <= 3, "known-document idle policy checks retain bounded cache");
    }

    private static void RequiredSetup()
    {
        Home();
        Check((bool)Invoke(typeof(Shell), "OpenNativeSetupPage", true, true)!, "required Setup opens actual native page");
        var setup = Get<SetupPagePresenter>("_nativeSetup")!;
        Check(!setup.Controller.CanLeave && !setup.Controller.Busy, "missing files make mandatory Setup non-leavable");
        GuardStudio(setup, "mandatory");
        Home();
    }

    private static void BusySetup()
    {
        Home();
        Check((bool)Invoke(typeof(Shell), "OpenNativeSetupPage", false, true)!, "optional Setup opens actual native page");
        var setup = Get<SetupPagePresenter>("_nativeSetup")!;
        Check(setup.Controller.CanLeave, "optional idle Setup can leave");
        // Deterministic pending-operation seam: no fake extraction/download result,
        // and no invocation of real picker/updater services.
        var pending = new TaskCompletionSource<SetupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Put(setup.Controller.GetType(), "_operation", pending.Task, setup.Controller);
        Check(setup.Controller.Busy && !setup.Controller.CanLeave, "pending Setup operation blocks leaving");
        GuardStudio(setup, "busy");
        pending.SetResult(new(false, "Harness operation retired.")); setup.Controller.Tick();
        Home();
    }

    private static void GuardStudio(SetupPagePresenter setup, string label)
    {
        var oldDocument = setup.Document;
        string? oldRoom = Get<MenuSettings>("_settings")!.RoomKey;
        Check(!Shell.QueueExternalStudioPlaytest("fixture-uninstalled-room", new StudioPlaytestOptions()), label + " Setup rejects actual Studio IPC playtest");
        Check(!Shell.ExternalStudioLaunchPending && Get<MenuSettings>("_settings")!.RoomKey == oldRoom, "rejected Studio IPC cannot mutate launch or room");
        Click("nav_play", mouse: true);
        Check(Get<SetupPagePresenter>("_nativeSetup") == setup && Pages.Manager.Page == oldDocument
            && Shell.ApplicationRouter.Current.Item == "setup", label + " Setup also rejects actual native navigation");
    }

    private static void DirtyStudio(bool save)
    {
        Home(); Click("header_settings");
        var oldDocument = Settings.Document;
        float original = InputSettings.MouseSensitivity;
        float desired = Math.Abs(original - .53f) < .001f ? .61f : .53f;
        StageMouse(desired);
        string? oldRoom = Get<MenuSettings>("_settings")!.RoomKey;
        string room = "fixture-studio-" + (save ? "apply" : "discard");
        Check(Shell.QueueExternalStudioPlaytest(room, new StudioPlaytestOptions()), "dirty Settings accepts deferred Studio IPC request");
        Guid ipc = TrackStudio(room);
        Check(PollStudio(ipc).PlaytestState == StudioPlaytestState.Accepted, "actual IPC polling retains deferred Accepted request");
        Pump();
        Check(Shell.ExternalStudioLaunchPending && PendingPlan == null && Get<MenuSettings>("_settings")!.RoomKey == oldRoom
            && Pages.Manager.ModalCount == 1, "Studio IPC waits for Settings decision before any launch mutation");
        Click(save ? "settings_close_apply" : "settings_close_discard", mouse: true);
        var plan = PendingPlan;
        Check(plan is { IsPlaytest: true, Kind: LaunchKind.Offline } accepted && accepted.RoomKey == room,
            "approved Settings decision queues actual Studio playtest exactly once");
        Check(Math.Abs(InputSettings.MouseSensitivity - (save ? desired : original)) < .0001f, "Studio decision preserves Settings transaction");
        Check(!Host.IsAlive(oldDocument), "Studio approval retires original Settings document");
        Check(StopStudio(ipc).PlaytestState == StudioPlaytestState.Ended, "actual Studio Stop IPC ends current accepted request");
        Check(!Shell.ExternalStudioLaunchPending, "Studio cancellation retires only queued playtest before scene launch");
        Pump();
        Check(Get<object>("_nativeSettings") == null && Pages.Manager.PageKey == "home",
            "cancelled Studio approval leaves a usable native Home document");
        Click("header_settings", mouse: true);
        Check(Pages.Manager.PageKey == "settings", "Home controls remain usable after cancelled Studio request");
        Console.WriteLine("Shell Studio dirty decision passed: " + (save ? "Apply" : "Discard") + ".");
    }

    private static void DeferredStudioCancelled()
    {
        Home(); Click("header_settings"); StageMouse(.73f);
        const string room = "fixture-cancel-before-decision";
        Check(Shell.QueueExternalStudioPlaytest(room, new StudioPlaytestOptions()), "Studio request waits in dirty Settings decision");
        Guid ipc = TrackStudio(room);
        Check(PollStudio(ipc).PlaytestState == StudioPlaytestState.Accepted, "IPC keeps accepted playtest while dirty decision is pending");
        Pump();
        Check(Pages.Manager.ModalCount == 1 && PendingPlan == null && Shell.ExternalStudioLaunchPending,
            "deferred request is visible to IPC polling while awaiting Settings approval");
        Check(StopStudio(ipc).PlaytestState == StudioPlaytestState.Ended, "actual Stop IPC cancels request before decision");
        Click("settings_close_discard", mouse: true);
        Check(!Shell.ExternalStudioLaunchPending, "Stop cancels deferred Studio request before Settings approval");
        Check(Get<object>("_nativeSettings") == null && Pages.Manager.PageKey == "home",
            "cancelled deferred request restores native Home after Settings decision");
        Click("header_settings", mouse: true);
        Check(Pages.Manager.PageKey == "settings", "Home control works after deferred Studio cancellation");
        Home();
    }

    private static void DeferredStudioDecisionCancelled()
    {
        Home(); Click("header_settings"); StageMouse(.81f);
        var document = Settings.Document;
        Check(Shell.QueueExternalStudioPlaytest("fixture-cancel-dialog", new StudioPlaytestOptions()),
            "Studio IPC can wait for Settings decision");
        Guid ipc = TrackStudio("fixture-cancel-dialog");
        Check(PollStudio(ipc).PlaytestState == StudioPlaytestState.Accepted, "IPC accepted state survives before dialog Cancel");
        Pump(); Click("settings_close_cancel", mouse: true);
        Check(!Shell.ExternalStudioLaunchPending && PendingPlan == null, "return to editing cancels deferred Studio launch ownership");
        Check(PollStudio(ipc).PlaytestState == StudioPlaytestState.Rejected, "actual IPC polling retires cancelled dialog request");
        Check(Settings.Document == document && Host.IsAlive(document) && Settings.Controller.Dirty
            && Pages.Manager.ModalCount == 0, "Cancel keeps editable Settings draft and document");
        Click("nav_play", mouse: true); Click("settings_close_discard", mouse: true);
        Check(PendingPlan == null && Pages.Manager.PageKey == "home", "later navigation cannot revive cancelled Studio callback");
    }

    private static void StageMouse(float value)
    {
        Click("settings_controls_tab");
        Host.SetField(Settings.Document, "settings_search", "MouseSensitivity"); Click("settings_search_button");
        var fields = Settings.Controller.Snapshot().Fields;
        Check(fields.Count == 1 && fields[0].Definition.Id == "input.MouseSensitivity", "actual native search selects authoritative sensitivity field");
        Host.SetField(Settings.Document, "settings_value_0", value.ToString(CultureInfo.InvariantCulture));
        Check(!Settings.Controller.Dirty, "native text edit has not prematurely entered committed controller state");
    }

    private static void Home()
    {
        Invoke(typeof(Shell), "RetireNativePages", true);
        Shell.ApplicationRouter.Reset(new(LauncherPage.Home));
        Pages.ShowBaseline(RmlUiMenuPage.Home); RmlUiPrototype.Show(); Pump();
    }

    private static void Pump()
    {
        Invoke(typeof(Shell), "TickNativePages"); Pages.Flush(); Host.Update(); Pages.AfterUpdate(); Host.Render(1280, 720);
    }

    private static void Click(string id, bool mouse = false)
    {
        Pump(); var document = Pages.Manager.Top;
        Check(Host.FocusDocument(document, id), "actual native control focuses: " + id);
        Check(Host.TryGetElementBounds(document, id, out float x, out float y, out float width, out float height)
            && width > 0 && height > 0, "actual native control has usable bounds: " + id);
        if (mouse)
        {
            Host.Input.PointerMoved(x + width / 2, y + height / 2);
            Host.Input.PointerButton(0, x + width / 2, y + height / 2, true);
            Host.Input.PointerButton(0, x + width / 2, y + height / 2, false);
        }
        else { Host.Input.Key(2, true); Host.Input.Key(2, false); }
        Host.Update();
        Invoke(typeof(RmlUiPrototype), "DrainActions");
        Check(RmlUiPrototype.TryTakeIntent(out var intent) && intent.Document == document, "native DOM publishes current-document typed action: " + id);
        Check((bool)Invoke(typeof(Shell), "HandleNativePageIntent", intent)!, "actual Shell dispatches native typed action: " + id);
        Check(!RmlUiPrototype.TryTakeIntent(out _), "native action is dispatched once"); Pump();
    }

    private static T? Get<T>(string name) where T : class => (T?)typeof(Shell).GetField(name, Static)!.GetValue(null);
    private static LaunchPlan? PendingPlan => (LaunchPlan?)typeof(Shell).GetField("_pending", Static)!.GetValue(null);
    private static Dictionary<Guid, StudioGameResult> StudioRecords =>
        (Dictionary<Guid, StudioGameResult>)typeof(GameStudioIntegration).GetField("Playtests", Static)!.GetValue(null)!;
    private static Guid TrackStudio(string room)
    {
        // Match the immutable Accepted registration in the real broker Launch
        // method without publishing a map or starting an authenticated endpoint.
        Guid id = Guid.NewGuid(); StudioRecords.Clear();
        StudioRecords.Add(id, new(true, PlaytestId: id, PlaytestState: StudioPlaytestState.Accepted));
        Put(typeof(GameStudioIntegration), "_currentPlaytest", id);
        Put(typeof(GameStudioIntegration), "_currentRoom", room);
        return id;
    }
    private static StudioGameResult PollStudio(Guid id)
    {
        // The fixture has no RenderWindow. Polling the real status/cancellation
        // code is safe because no owner work, scene or map publication is queued.
        Invoke(typeof(GameStudioIntegration), "PumpOwnerThread", (object?)null);
        return StudioRecords[id];
    }
    private static StudioGameResult StopStudio(Guid id) =>
        (StudioGameResult)Invoke(typeof(GameStudioIntegration), "Playtest", id, true)!;
    private static void Put(Type type, string name, object? value, object? target = null)
        => type.GetField(name, target == null ? Static : Instance)!.SetValue(target, value);
    private static object? Invoke(Type type, string name, params object?[] values)
    {
        try { return type.GetMethod(name, Static)!.Invoke(null, values); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static bool NoTicket(uint id, out string ticket) { ticket = ""; return false; }
}
