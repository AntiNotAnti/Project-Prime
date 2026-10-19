using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Windowing.GraphicsLibraryFramework;

internal sealed partial class ReplayWindowAcceptance
{
    private readonly List<object> _headerTransitions = new();
    private int _headerActionOrdinal;
    private long _lastHeaderFocusAttempt = -500;
    private sealed record HeaderRoute(string Name, string Button, RmlUiMenuPage? Baseline, string? NativeField);
    private sealed record HeaderLease(object Owner, RmlUiDocumentToken Document, RmlUiDocumentToken Page,
        object Menu, object? Library);
    private static readonly HeaderRoute[] MainHeaderRoutes =
    {
        new("Play", "nav_play", RmlUiMenuPage.Home, null),
        new("Hunters", "nav_hunters", null, "_hunterChoice"),
        new("Community", "nav_community", null, "_community"),
        new("Theatre", "nav_theatre", RmlUiMenuPage.Theatre, null),
        new("Studio", "nav_studio", null, "_studio")
    };

    // This fixture never requests a creator/account action, import, install, or preview.
    // Only discovery reads are substituted; native documents, routing and input remain actual.
    private sealed class EmptyHeaderCommunityReader : INativeCommunityReader
    {
        public Task<NativeCommunityPage> BrowseAsync(string query, string sort, int page, CancellationToken cancellation)
            => cancellation.IsCancellationRequested ? Task.FromCanceled<NativeCommunityPage>(cancellation)
                : Task.FromResult(new NativeCommunityPage(Array.Empty<NativeCommunityEntry>(), 0, page, 8));
    }

    private void PlanMainHeaderNavigation()
    {
        HeaderRoute home = MainHeaderRoutes[0], theatre = MainHeaderRoutes[3];
        Wait("Main header matrix begins at actual Home", () => HeaderRouteReady(home));
        PlanHeaderArrowAdjacency();
        // Each destination is first exercised directly from Home, including its active-tab case.
        foreach (HeaderRoute destination in MainHeaderRoutes)
        {
            PlanHeaderTransition(home, "Home setup for " + destination.Name);
            PlanHeaderTransition(destination, "Home to " + destination.Name);
            PlanHeaderSameTab(destination);
        }
        // Explicitly preserve the reported reproduction paths as named acceptance evidence.
        foreach (HeaderRoute native in new[] { MainHeaderRoutes[1], MainHeaderRoutes[2], MainHeaderRoutes[4] })
        {
            PlanHeaderTransition(theatre, "Theatre regression setup for " + native.Name);
            PlanHeaderTransition(native, "Theatre to " + native.Name);
            PlanHeaderTransition(home, "Theatre via " + native.Name + " PLAY returns Home");
            Do("capture Theatre via " + native.Name + " PLAY result", () => Capture("header-theatre-" + native.Name.ToLowerInvariant() + "-play"));
        }
        // All 25 directed pairs include the five same-destination pairs. Reset the
        // source before each pair, so source ownership is established, not inferred.
        foreach (HeaderRoute source in MainHeaderRoutes)
            foreach (HeaderRoute destination in MainHeaderRoutes)
            {
                PlanHeaderTransition(source, "matrix source " + source.Name + " before " + destination.Name);
                if (source == destination) PlanHeaderSameTab(source);
                else PlanHeaderTransition(destination, "matrix " + source.Name + " to " + destination.Name);
            }
        PlanHeaderTransition(home, "Home before dirty Settings header guard");
        PlanDirtySettingsHeaderGuard();
        PlanHeaderTransition(home, "Main header matrix finishes at actual Home");
        PlanNativeTrainingFailureReturn();
    }

    private void PlanHeaderTransition(HeaderRoute destination, string name)
    {
        bool pointer = ++_headerActionOrdinal % 2 == 1;
        DoHeader("native header " + (pointer ? "pointer" : "keyboard") + " action: " + name, () => PressHeader(destination.Button, pointer));
        Wait("native foreground: " + name, () =>
        {
            if (!HeaderRouteReady(destination)) return false;
            HeaderLease lease = CurrentHeaderLease(destination);
            _headerTransitions.Add(new { Case = name, Destination = destination.Name, Input = pointer ? "production-pointer" : "native-keyboard", Swap = _observedFrames,
                lease.Document, lease.Page, Owner = Id(lease.Owner), Menu = Id(lease.Menu), Library = Id(lease.Library) });
            return true;
        });
    }

    private void PlanHeaderSameTab(HeaderRoute route)
    {
        HeaderLease? before = null;
        DoHeader("retain exact " + route.Name + " active-tab lease", () => before = CurrentHeaderLease(route));
        for (int repeat = 1; repeat <= 2; repeat++)
        {
            int click = repeat;
            bool pointer = repeat == 1;
            DoHeader("repeat actual " + route.Name + " header " + (pointer ? "pointer" : "keyboard") + " click " + click, () => PressHeader(route.Button, pointer));
            WaitHeaderFrames(3, route.Name + " repeat " + click + " preserves exact page/document/owner", () =>
            {
                Require(HeaderRouteReady(route), "active tab retains " + route.Name + " foreground");
                HeaderLease after = CurrentHeaderLease(route);
                Require(before != null && ReferenceEquals(before.Owner, after.Owner)
                    && before.Document == after.Document && before.Page == after.Page
                    && ReferenceEquals(before.Menu, after.Menu) && ReferenceEquals(before.Library, after.Library),
                    "same-tab click preserves exact native route, document and controller/library owners");
                _headerTransitions.Add(new { Case = "same-tab", Destination = route.Name, Repeat = click,
                    Input = pointer ? "production-pointer" : "native-keyboard", Swap = _observedFrames,
                    after.Document, after.Page, Owner = Id(after.Owner), Preserved = true });
            });
        }
    }

    // Header automation alone may reacquire an externally unfocused window.
    // General replay focus-loss qualification continues to use its original helpers.
    private bool HeaderWindowFocused()
    {
        if (_window is not { } window) return false;
        if (window.IsFocused) return true;
        if (_elapsed.ElapsedMilliseconds - _lastHeaderFocusAttempt >= 500)
        {
            window.Focus();
            _lastHeaderFocusAttempt = _elapsed.ElapsedMilliseconds;
        }
        return false;
    }

    private void DoHeader(string name, Action action)
        => Wait(name, () => { if (!HeaderWindowFocused()) return false; action(); return true; }, 12000);

    private void WaitHeaderFrames(int count, string name, Action check)
    {
        int left = count;
        Wait(name, () =>
        {
            if (!HeaderWindowFocused()) { left = count; return false; }
            if (--left > 0) return false;
            check(); return true;
        }, 12000);
    }

    private void PressHeader(string id, bool pointer)
    {
        if (!pointer) { Activate(id); return; }
        var host = Host!;
        var window = _window!;
        Require(host.TryGetElementBounds(host.CurrentInputDocument, id, out float x, out float y, out float width, out float height)
            && width > 0 && height > 0 && window.FramebufferSize.X > 0 && window.FramebufferSize.Y > 0,
            "live native pointer target bounds: " + id);
        double clientX = (x + width / 2) * window.ClientSize.X / window.FramebufferSize.X;
        double clientY = (y + height / 2) * window.ClientSize.Y / window.FramebufferSize.Y;
        // No focus shortcut: production mapping and native hit testing select the button.
        Require(RmlUiOpenGlSmoke.PointerMoved(clientX, clientY)
            && RmlUiOpenGlSmoke.PointerButton(MouseButton.Left, clientX, clientY, true)
            && RmlUiOpenGlSmoke.PointerButton(MouseButton.Left, clientX, clientY, false),
            "production native pointer route accepts header click: " + id);
    }

    private void PlanHeaderArrowAdjacency()
    {
        // app-shell.rcss explicitly authors these nav-right/nav-left targets;
        // key codes 7/8 are native Left/Right in RmlUiDesktopInput.
        DoHeader("focus actual Community header for authored arrow adjacency", () => Focus("nav_community"));
        foreach (var edge in new[] { (Key: 8, Target: "nav_theatre"), (Key: 8, Target: "nav_studio"),
            (Key: 7, Target: "nav_theatre"), (Key: 7, Target: "nav_community") })
        {
            DoHeader("native header arrow to " + edge.Target, () => { Host!.Input.Key(edge.Key, true); Host.Input.Key(edge.Key, false); });
            WaitHeaderFrames(2, "authored native header focus reaches " + edge.Target, () =>
            {
                Require(Host!.FocusedElement() == edge.Target && HeaderRouteReady(MainHeaderRoutes[0]),
                    "native Left/Right follows authored Community, Theatre, Studio adjacency without activating a route");
                _headerTransitions.Add(new { Case = "authored-focus-adjacency", Input = "native-keyboard-arrow",
                    Key = edge.Key, Focus = Host.FocusedElement(), Swap = _observedFrames });
            });
        }
    }

    private bool HeaderRouteReady(HeaderRoute route)
    {
        if (!HeaderWindowFocused()) return false;
        var host = Host;
        var pages = ReadStatic<RmlUiPageManager>("_pages");
        var layout = ReadStatic<RmlUiLauncherPages>("_launcherPages");
        if (host is not { Active: true } || pages == null || layout == null
            || pages.Page == default || pages.Top != pages.Page || host.CurrentInputDocument != pages.Page
            || !host.IsVisible(pages.Page)) return false;
        if (route.Baseline is { } baseline)
        {
            if (layout.Suspended || layout.Page != baseline) return false;
        }
        else if (!layout.Suspended || ReadStatic<object>(route.NativeField!) == null) return false;
        foreach (string field in new[] { "_hunterChoice", "_community", "_studio", "_quickSettings", "_fullSettings", "_hudEditor", "_news", "_social", "_party", "_license", "_studioEditor" })
            if (field != route.NativeField && ReadStatic<object>(field) != null) return false;
        Require(!DemoPlayback.IsActive && !_window!.HasScene && _workspaceRequests.Count == 0,
            "header navigation retains the sole Shell window without playback, scene or child-app request");
        return host.TryGetElementBounds(pages.Page, route.Button, out _, out _, out float width, out float height)
            && width > 0 && height > 0;
    }

    private HeaderLease CurrentHeaderLease(HeaderRoute route)
    {
        Require(HeaderRouteReady(route), "current header route is " + route.Name);
        var pages = ReadStatic<RmlUiPageManager>("_pages")!;
        var menu = ReadStatic<RmlUiMenuController>("_controller")!;
        object owner = route.NativeField == null ? ReadStatic<RmlUiLauncherPages>("_launcherPages")!
            : ReadStatic<object>(route.NativeField)!;
        return new(owner, Host!.CurrentInputDocument, pages.Page, menu, Read<RmlUiTheatreIndex>(menu, "_theatre"));
    }

    private void PlanDirtySettingsHeaderGuard()
    {
        NativeFullSettingsPage? settings = null;
        NativeFullSettingsController? controller = null;
        NativeSettingRow? reduceMotion = null;
        Dictionary<NativeSettingRow, string>? originalRows = null, liveRows = null;
        QuickLauncherPreferences original = default, staged = default;
        string stagedMotion = "", cycleId = "";
        RmlUiDocumentToken document = default;
        string preferencesPath = Path.Combine(LauncherPrefs.Directory, "launcher.txt");
        byte[]? preferencesBefore = null;
        bool preferencesExisted = false;
        DoHeader("open actual full Settings before staging header draft", () => Activate("header_settings"));
        Wait("full Settings owns foreground for header guard", () =>
        {
            if (!HeaderWindowFocused()) return false;
            settings = ReadStatic<NativeFullSettingsPage>("_fullSettings");
            controller = Read<NativeFullSettingsController>(settings, "_controller");
            if (settings == null || controller == null || !settings.OwnsForeground || Host!.CurrentInputDocument != settings.Document) return false;
            original = new(LauncherPrefs.ReduceMotion, LauncherPrefs.AutoUpdate, LauncherPrefs.DebugLogs);
            originalRows = controller.Rows.ToDictionary(row => row, row => row.Value);
            liveRows = controller.Rows.ToDictionary(row => row, row => row.Read());
            reduceMotion = controller.Rows.Single(row => row.Category == "Display" && row.Group == "Accessibility" && row.Label == "Reduce Motion");
            Require(reduceMotion.Choices.Length > 1,
                "full Settings exposes actual Display / Accessibility / Reduce Motion choice");
            document = settings.Document;
            preferencesExisted = File.Exists(preferencesPath);
            preferencesBefore = preferencesExisted ? File.ReadAllBytes(preferencesPath) : null;
            return true;
        });
        DoHeader("find Reduce Motion through actual full Settings search", () =>
        {
            Host!.SetField(document, "native_settings_search", "Reduce Motion");
            PressSettingsSemanticButton("FIND SETTING");
        });
        Wait("actual Find Setting focuses the visible Reduce Motion cycle", () =>
        {
            if (!HeaderWindowFocused()) return false;
            var visible = Read<NativeSettingRow[]>(settings, "_visible");
            if (visible == null) return false;
            int slot = Array.IndexOf(visible, reduceMotion!);
            if (slot < 0) return false;
            cycleId = "native_settings_cycle_" + slot;
            return Host!.FocusedElement() == cycleId
                && Host.TryGetElementBounds(document, cycleId, out _, out _, out float width, out float height) && width > 0 && height > 0;
        });
        DoHeader("stage actual full Settings Reduce Motion choice without applying", () => Activate(cycleId));
        Wait("full Settings has staged native Reduce Motion draft", () =>
        {
            if (!HeaderWindowFocused() || settings!.CanNavigateAway || !controller!.Dirty) return false;
            stagedMotion = reduceMotion!.Value;
            staged = original with { ReduceMotion = bool.Parse(stagedMotion) };
            Require(staged.ReduceMotion != original.ReduceMotion, "actual choice changes only its detached draft");
            return true;
        });
        foreach (HeaderRoute destination in MainHeaderRoutes)
        {
            DoHeader("dirty Settings attempts actual " + destination.Name + " header", () => Activate(destination.Button));
            WaitHeaderFrames(3, "dirty Settings blocks " + destination.Name + " with visible guidance and intact draft", () =>
            {
                Require(ReferenceEquals(settings, ReadStatic<NativeFullSettingsPage>("_fullSettings"))
                    && settings!.OwnsForeground && Host!.CurrentInputDocument == document && controller!.Dirty
                    && reduceMotion!.Value == stagedMotion && !settings.CanNavigateAway
                    && originalRows!.All(pair => pair.Key.Value == (ReferenceEquals(pair.Key, reduceMotion) ? stagedMotion : pair.Value)),
                    "blocked header retains exact settings owner, document and staged options");
                var semantic = NativeSemanticReadback.Read(Host!, document);
                Require(semantic.ContainsText("Unsaved changes") && semantic.ContainsText("Apply Changes or Discard before leaving"),
                    "native rendered settings status explains blocked header navigation");
                Require(new QuickLauncherPreferences(LauncherPrefs.ReduceMotion, LauncherPrefs.AutoUpdate, LauncherPrefs.DebugLogs) == original
                    && liveRows!.All(pair => pair.Key.Read() == pair.Value),
                    "blocked header does not publish staged preferences");
            });
        }
        DoHeader("dirty Settings attempts actual Versions footer", () => Activate("build_version"));
        WaitHeaderFrames(3, "dirty Settings refuses Versions before catalog or install owner opens", () =>
        {
            Require(ReferenceEquals(settings, ReadStatic<NativeFullSettingsPage>("_fullSettings"))
                && settings!.OwnsForeground && settings.Document == document && controller!.Dirty
                && ReadStatic<object>("_versions") == null && ReadStatic<RmlUiPageManager>("_pages")!.ModalCount == 0
                && originalRows!.All(pair => pair.Key.Value == (ReferenceEquals(pair.Key, reduceMotion) ? stagedMotion : pair.Value))
                && liveRows!.All(pair => pair.Key.Read() == pair.Value),
                "Versions footer preserves exact full Settings draft and opens no catalog modal");
            Require(NativeSemanticReadback.Read(Host!, document).ContainsText("Apply Changes or Discard before leaving"),
                "Versions footer reports actual native unsaved-settings guidance");
        });
        Do("capture actual dirty Settings header notice", () => Capture("header-settings-dirty-navigation"));
        DoHeader("discard actual full Settings draft without applying or saving", () => PressSettingsSemanticButton("DISCARD"));
        Wait("discard restores original Settings without persistence", () =>
        {
            if (!HeaderWindowFocused() || !settings!.CanNavigateAway || controller!.Dirty) return false;
            Require(originalRows!.All(pair => pair.Key.Value == pair.Value) && liveRows!.All(pair => pair.Key.Read() == pair.Value)
                && new QuickLauncherPreferences(LauncherPrefs.ReduceMotion, LauncherPrefs.AutoUpdate, LauncherPrefs.DebugLogs) == original,
                "discard restores every original draft row and preserves live preferences");
            Require(File.Exists(preferencesPath) == preferencesExisted
                && (!preferencesExisted || File.ReadAllBytes(preferencesPath).AsSpan().SequenceEqual(preferencesBefore!)),
                "toggle, blocked header clicks and discard leave preference file bytes unchanged");
            return true;
        });
    }

    private void PlanNativeTrainingFailureReturn()
    {
        const string failure = "SYNTHETIC-RML97-TRAINING-FAILURE-NO-LAUNCH";
        object? playbackOwner = null;
        object? front = null;
        DoHeader("deliver synthetic launch failure through retained training owner", () =>
        {
            playbackOwner = DemoPlayback.PlaybackOwner;
            var retained = RetainedUpdateFront()!; front = retained;
            retained.ShowTrainingLaunchFailure(failure);
        });
        Wait("synthetic training launch failure stays on native Aim Lab with visible status", () =>
        {
            if (!HeaderWindowFocused()) return false;
            var pages = ReadStatic<RmlUiPageManager>("_pages")!;
            if (ReadStatic<RmlUiLauncherPages>("_launcherPages") is not { Suspended: false, Page: RmlUiMenuPage.Offline }
                || Host!.CurrentInputDocument != pages.Page) return false;
            Require(ReferenceEquals(front, RetainedUpdateFront()) && !RetainedUpdateFront()!.Prime.Overlays.IsOpen
                && !DemoPlayback.IsActive && ReferenceEquals(playbackOwner, DemoPlayback.PlaybackOwner) && !_window!.HasScene
                && NativeSemanticReadback.Read(Host!, pages.Page).ContainsText(failure),
                "synthetic failure retains native training route/status without legacy overlay or gameplay launch");
            return true;
        });
        PlanHeaderTransition(MainHeaderRoutes[0], "Home after synthetic native training failure");
    }

    private void PressSettingsSemanticButton(string label)
    {
        var host = Host!;
        var document = host.CurrentInputDocument;
        using var snapshot = JsonDocument.Parse(NativeSemanticReadback.Read(host, document).RawJson);
        var root = snapshot.RootElement;
        var button = root.GetProperty("nodes").EnumerateArray().Single(node => node.GetProperty("label").GetString() == label);
        Require(AccessibilityAction(document.Generation, document.DocumentId, root.GetProperty("revision").GetUInt64(),
            button.GetProperty("key").GetString()!, 1) == 1, "actual native semantic settings button: " + label);
    }
}
