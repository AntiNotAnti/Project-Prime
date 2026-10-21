using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Hunters;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Platform;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

/// <summary>Actual cartridge meshes through the existing RmlUi stage; all identities are local fixture data.</summary>
internal static partial class NativeHunterStageGlAcceptance
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Scope = "Actual native OpenGL RmlUi Home/picker/eight-pad stage with user-owned cartridge meshes and EXPLICIT FIXTURE identities. Fresh private userdata; no existing player settings/saves, account, presence, network, match or application loop. Scene constructors initialize temporary story state with save slot zero. Canonical hashes cover an inactive sentinel scene and its RNG, not active-world Player.Draw restoration, device performance or full release qualification.";
    private static int _checks;
    private static readonly List<object> Rows = new();

    internal static int Run(string assets, int requestedWidth, int requestedHeight)
    {
        // Set before any AppPaths/LauncherPrefs statics or window resources load.
        string userData = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
        if (!Path.IsPathFullyQualified(userData))
            throw new ArgumentException("--native-hunters requires an explicit absolute isolated PROJECT_PRIME_USER_DATA.");
        userData = Path.GetFullPath(userData);
        string canonical = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "Project Prime");
        if (userData.TrimEnd(Path.DirectorySeparatorChar) == canonical.TrimEnd(Path.DirectorySeparatorChar)
            || userData.StartsWith(canonical + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(Path.Combine(userData, "paths.txt"))
            || Directory.EnumerateFiles(userData, "*", SearchOption.AllDirectories)
                .Any(path => path != Path.Combine(userData, "paths.txt")))
            throw new ArgumentException("Supply a fresh isolated directory containing only normalized paths.txt, never the real player directory.");
        string root = Path.GetFullPath(assets);
        LoadedNativeModuleCheck.Expected? expectedNative = OperatingSystem.IsMacOS()
            ? LoadedNativeModuleCheck.CaptureExpectedPackage(AppContext.BaseDirectory) : null;
        string originalDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(userData);
        Paths.UpdatePaths(); Paths.ChooseMphPath();
        Check(Paths.MphKey == Ver.AMHE1 && Directory.Exists(Paths.FileSystem), "configured actual AMHE1 source exists");
        Check(!NetSession.Active && !DemoPlayback.IsActive && !Headless.Active, "fresh process has no network/replay/headless owner");
        Check(Menu.SaveSlot == 0, "fresh process uses the no-save story slot");
        // Never enter the live launcher bridge, even if inherited from a developer shell.
        Environment.SetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE", "0");
        string output = Path.Combine(userData, "native-hunters");
        Directory.CreateDirectory(output);
        string status = "INCOMPLETE";
        object? hardware = null;
        RenderWindow? window = null;
        Scene? sentinel = null;
        Scene? previewScene = null;
        RmlUiHost? host = null;
        RmlUiLauncherPages? layout = null;
        var sceneField = typeof(RenderWindow).GetField("_scene", Hidden)!;
        var loadout = CosmeticPreview.Loadout;
        var mode = CosmeticPreview.Mode;
        float yaw = CosmeticPreview.Yaw, zoom = CosmeticPreview.Zoom;
        bool loop = CosmeticPreview.LoopDeath;
        int death = CosmeticPreview.DeathRequest;
        bool hd = RenderOptions.CharacterModelReplacements;
        try
        {
            GraphicsBackendPolicy.Configure("opengl");
            RenderOptions.CharacterModelReplacements = false;
            // Normal diagnostic window avoids the shell's geometry/settings ownership.
            // No Run/OnRenderFrame: neither launcher nor live services are pumped.
            window = new RenderWindow();
            Check(Menu.SaveSlot == 0 && !Directory.Exists("Savedata"), "window initialization reads/writes no player save slot");
            window.MinimumSize = new(64, 64);
            // Match the existing GL tool contract: CLI dimensions are client
            // units, while all rendering and evidence use measured pixels.
            window.ClientSize = new(requestedWidth, requestedHeight);
            int width = window.FramebufferSize.X, height = window.FramebufferSize.Y;
            float density = (float)width / Math.Max(1,window.ClientSize.X);
            Check(width > 0 && height > 0, "actual RenderWindow framebuffer exists");
            window.Title = "Project Prime LOCAL ASSET FIXTURE";
            hardware = new { vendor = GL.GetString(StringName.Vendor), gpu = GL.GetString(StringName.Renderer),
                glVersion = GL.GetString(StringName.Version), width, height, requestedWidth, requestedHeight, density };
            sentinel = window.Scene;
            sentinel.GameState.Mode = GameMode.Battle;
            sentinel.GameState.PointGoal = 137; sentinel.GameState.MatchTime = 421;
            sentinel.Random.SetRng1(123456); sentinel.Random.SetRng2(654321);
            // Retain the inactive scene to witness state isolation. Detaching is a
            // test-only setup seam; the production stage requires HasScene false.
            sceneField.SetValue(window, null);
            string canonicalBefore = Canonical(sentinel);
            host = new RmlUiHost();
            Check(host.Initialize(width, height, density, root, RmlUiRenderBackend.OpenGl), "actual native RmlUi host initializes at production framebuffer density");
            if (expectedNative != null)
                LoadedNativeModuleCheck.WriteNew(Path.Combine(output, "loaded-native-module.json"),
                    LoadedNativeModuleCheck.RequireLoaded(expectedNative));
            layout = new RmlUiLauncherPages(host);
            var home = layout.ShowBaseline(RmlUiMenuPage.Home);
            layout.SetBool("home_feature_available", true);
            layout.SetText("home_feature_title", "LOCAL DISPATCH FIXTURE");
            layout.SetText("home_feature_summary", "LOCAL UI COMPOSITION // NO LIVE NEWS OR ACCOUNT");
            layout.SetText("player_name", "LOCAL ASSET FIXTURE"); layout.Flush(); host.Update(); layout.AfterUpdate();
            if (host.TryGetElementBounds(home, "home_feature_open", out float newsX, out float newsY,
                out float newsW, out float newsH) && newsW > 0 && newsH > 0)
                Check(host.TryGetElementBounds(home, "home_feature_open_label", out float labelX, out float labelY,
                    out float labelW, out float labelH) && labelW > 0 && labelH > 0
                    && RmlUiPreviewViewport.IsContained(labelX, labelY, labelW, labelH, newsX, newsY, newsW, newsH),
                    "visible Home dispatch action has a laid-out readable label inside its button");

            for (int hunterId = 0; hunterId < 7; hunterId++)
            for (int suit = 0; suit < 4; suit++)
            {
                Hunter hunter = (Hunter)hunterId;
                var hub = new HubSnapshot("LOCAL FIXTURE", hunter, hunter, suit, true, "LOCAL GL FIXTURE");
                Clear(width, height);
                RmlUiHunterStageRenderer.Draw(window, host, RmlUiMenuPage.Home, false, hub, default, null, width, height,
                    homeDocument: home);
                previewScene = PreviewScene();
                Check(!window.HasScene && Canonical(sentinel) == canonicalBefore, "Home preserves inactive canonical state/RNG");
                var witness = Preview(previewScene, hunter, suit, SkinContext.Biped);
                var homeGeometry = RequireHomeGeometry(host, home, width, height);
                StagePostconditions(width, height);
                NativeOverlay(host, width, height);
                byte[] homePixels = FinalCompositeCapture.Read(width, height);
                Check(homePixels.Length == width * height * 3 && homePixels.Max() > homePixels.Min(),
                    "actual Home composite has a complete nonuniform framebuffer for " + hunter);
                string path = Capture(output, $"home-{hunter}-{suit}", width, height);
                Rows.Add(new { page = "Home", hunter = hunter.ToString(), suit, witness, homeGeometry, canonicalBefore,
                    canonicalAfter = Canonical(sentinel), capture = path });
            }
            Scene stageScene = PreviewScene();

            // Open only the shipped document, bypassing the presenter that owns equip/account services.
            var picker = layout.Manager.OpenPage(new("asset-picker", "pages/hunters/native-picker.rml", "hunter_choice0"));
            host.Update();
            // A retained map/photo is represented by a saturated sentinel in
            // the actual backbuffer. Hunters must repaint every pixel before
            // its translucent native panel, even after a clipped retained pass.
            Clear(width, height);
            GL.ClearColor(1, 0, 1, 1); GL.Clear(ClearBufferMask.ColorBufferBit);
            LauncherBackdrop.Set(LauncherBackdropScene.MapEditor);
            GL.Viewport(0, 0, 8, 8); GL.Enable(EnableCap.ScissorTest); GL.Scissor(0, 0, 4, 4);
            RmlUiHunterStageRenderer.Draw(window, host, RmlUiMenuPage.Home, false,
                new HubSnapshot("LOCAL FIXTURE", Hunter.Samus, Hunter.Samus, 0, true, "LOCAL GL FIXTURE"),
                default, null, width, height, LauncherBackdropScene.Home);
            Check(LauncherBackdrop.Scene == LauncherBackdropScene.Home && !window.HasScene
                && Canonical(sentinel) == canonicalBefore,
                "Hunters explicitly replaces retained map background with original Home chamber without changing canonical scene");
            StagePostconditions(width, height);
            byte[] chamber = FinalCompositeCapture.Read(width, height);
            int retainedPhotoPixels = 0;
            for (int pixel = 0; pixel < chamber.Length; pixel += 3)
                if (chamber[pixel] > 250 && chamber[pixel + 1] < 5 && chamber[pixel + 2] > 250) retainedPhotoPixels++;
            Check(retainedPhotoPixels == 0,
                "opaque full-frame Hunters chamber leaves no retained photo sentinel through inherited viewport/scissor");
            NativeOverlay(host, width, height);
            using (var chamberPreview = new LauncherHunterPreviewController())
                Check(RmlUiHunterStageRenderer.DrawPicker(window, host, picker, Hunter.Samus, 0,
                    CosmeticLoadout.Default, chamberPreview.Current(), width, height),
                    "real Hunter preview remains after native panel over repainted procedural chamber");
            StagePostconditions(width, height);
            Rows.Add(new { page = "HuntersChamber", retainedPhotoPixels,
                capture = Capture(output, "picker-procedural-chamber", width, height) });
            if (height / density <= 500)
            {
                // Exercise the real scrolling and pointer path without a
                // presenter, so Apply/Save/Back cannot write equip/account state.
                (float X, float Y, float Width, float Height) Rect(string id)
                {
                    Check(host.TryGetElementBounds(picker, id, out float x, out float y, out float w, out float h),
                        "small native picker bounds exist: " + id);
                    return (x, y, w, h);
                }
                void Settle()
                {
                    for (int step = 0; step < 24; step++) { Thread.Sleep(20); host.Update(); }
                }
                var previewRect = Rect("native_hunter_preview");
                void PreviewStillContained()
                {
                    Check(Rect("native_hunter_preview") == previewRect,
                        "left-pane scrolling/focus leaves actual preview rectangle fixed");
                    foreach (string clipId in new[] { "native_hunter_panel", "stage" })
                    {
                        var clip = Rect(clipId);
                        Check(RmlUiPreviewViewport.IsContained(previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height,
                            clip.X, clip.Y, clip.Width, clip.Height), "small preview remains inside " + clipId);
                    }
                }
                foreach (var target in new[] { (Id: "hunter_apply", Kind: RmlUiIntentKind.HunterApply),
                    (Id: "hunter_discard", Kind: RmlUiIntentKind.HunterResetPreview),
                    (Id: "hunter_cancel", Kind: RmlUiIntentKind.HunterCancel) })
                {
                    host.DiscardIntents();
                    Check(host.FocusDocument(picker, target.Id), "small picker footer focuses: " + target.Id);
                    Settle();
                    var button = Rect(target.Id); var pane = Rect("native_hunter_picker");
                    Console.WriteLine("NATIVE PICKER FOOTER BOUNDS " + JsonSerializer.Serialize(new {target.Id,
                        button=new {button.X,button.Y,button.Width,button.Height},
                        pane=new {pane.X,pane.Y,pane.Width,pane.Height},
                        previewRect=new {previewRect.X,previewRect.Y,previewRect.Width,previewRect.Height}}));
                    Clear(width,height); NativeOverlay(host,width,height);
                    Capture(output,"picker-footer-focus-"+target.Id,width,height);
                    // Native UI bounds are fractional at Retina density. The model's
                    // strict clip remains unchanged; buttons allow one raster pixel.
                    Check(button.X >= pane.X - 1 && button.Y >= pane.Y - 1
                        && button.X + button.Width <= pane.X + pane.Width + 1
                        && button.Y + button.Height <= pane.Y + pane.Height + 1,
                        "focused footer is wholly reachable inside scrolling left pane: " + target.Id);
                    double px = button.X + button.Width / 2, py = button.Y + button.Height / 2;
                    host.Input.PointerMoved(px, py);
                    Check(host.HoveredElement() == target.Id, "actual native pointer hits footer: " + target.Id);
                    host.Input.PointerButton(0, px, py, true); host.Input.PointerButton(0, px, py, false); host.Update();
                    Check(host.TryTakeIntent(out var intent) && intent.Document == picker && intent.Kind == target.Kind
                        && intent.Argument == 0 && layout.Manager.Accept(intent) && !host.TryTakeIntent(out _),
                        "actual native footer pointer emits exactly its fixture-owned intent: " + target.Id);
                    PreviewStillContained();
                    Clear(width, height); NativeOverlay(host, width, height);
                    Check(RmlUiHunterStageRenderer.DrawPicker(window, host, picker, Hunter.Samus, 0,
                        CosmeticLoadout.Default, new(SkinContext.Biped, 0, 1, false, 0), width, height),
                        "actual model remains drawable with reachable footer: " + target.Id);
                    Check(Canonical(sentinel) == canonicalBefore, "footer preview preserves inactive canonical fields/RNG");
                    StagePostconditions(width, height);
                    Rows.Add(new { page = "PickerFooter", element = target.Id, pointerX = px, pointerY = py,
                        button = new { button.X, button.Y, button.Width, button.Height },
                        pane = new { pane.X, pane.Y, pane.Width, pane.Height },
                        capture = Capture(output, "picker-footer-" + target.Id, width, height) });
                }
                foreach (string id in new[] { "hunter_preview_biped","hunter_preview_weapon","hunter_preview_alt",
                    "hunter_rotate_left","hunter_rotate_right","hunter_zoom_out","hunter_zoom_in",
                    "hunter_preview_death","hunter_loop_death","hunter_compare_native","hunter_preview_reset" })
                {
                    Check(host.FocusDocument(picker,id),"short preview control takes native focus: "+id); Settle();
                    var button=Rect(id);var pane=Rect("native_hunter_preview_panel");
                    Check(button.X>=pane.X-1&&button.Y>=pane.Y-1
                        &&button.X+button.Width<=pane.X+pane.Width+1&&button.Y+button.Height<=pane.Y+pane.Height+1,
                        "short preview control remains wholly inside its stable column: "+id);
                    PreviewStillContained();
                }
                Check(host.FocusDocument(picker, "hunter_choice0"), "restore initial small picker selection focus"); Settle();
                var beforeWheel = Rect("hunter_choice0"); var wheelPane = Rect("native_hunter_options");
                host.Input.PointerMoved(wheelPane.X + wheelPane.Width / 2, wheelPane.Y + wheelPane.Height / 2);
                host.Input.PointerWheel(-3); Settle();
                Check(Rect("hunter_choice0").Y < beforeWheel.Y, "actual native wheel scrolls left-pane content");
                PreviewStillContained();
                Check(host.FocusDocument(picker, "hunter_choice0"), "restore selection focus after native wheel"); Settle();
                Rows.Add(new { page = "PickerSmallInput", footer = "Equip/Discard/Back actual native focus, hit-test and pointer",
                    wheel = "actual native wheel changes only left content", previewRect = new {
                        previewRect.X, previewRect.Y, previewRect.Width, previewRect.Height } });
            }
            for (int hunterId = 0; hunterId < 7; hunterId++)
            for (int suit = 0; suit < 4; suit++)
            {
                Hunter hunter = (Hunter)hunterId;
                using var controls = new LauncherHunterPreviewController();
                host.SetText(picker, "native_hunter_name", hunter + " // LOCAL FIXTURE SUIT " + (suit + 1));
                host.SetText(picker, "native_hunter_status", "ASSET FIXTURE // NO EQUIP OR ACCOUNT WRITES");
                void Action(string id, RmlUiIntentKind kind, int argument, Func<bool> apply)
                {
                    host.DiscardIntents();
                    Check(host.FocusDocument(picker, id), "actual native pose control focuses: " + id);
                    host.Input.Key(2, true); host.Input.Key(2, false); host.Update();
                    int accepted = 0;
                    while (host.TryTakeIntent(out var intent))
                        if (intent.Document == picker && intent.Kind == kind && intent.Argument == argument
                            && layout.Manager.Accept(intent)) accepted++;
                    Check(accepted == 1 && apply(), "one native pose intent reaches the existing local preview controller: " + id);
                }
                void Draw(string pose, bool capture = false)
                {
                    // Use the production preview's existing 60-Hz wall clock.
                    // Several fixture actions can otherwise precede its next
                    // owned tick and still observe the previous death/form.
                    Thread.Sleep(20);
                    Clear(width, height); host.Update(); NativeOverlay(host, width, height);
                    byte[] background = capture ? FinalCompositeCapture.Read(width, height) : Array.Empty<byte>();
                    string before = Canonical(sentinel);
                    var beforeCosmetics = (CosmeticPreview.Loadout, CosmeticPreview.Mode, CosmeticPreview.Yaw,
                        CosmeticPreview.Zoom, CosmeticPreview.LoopDeath, CosmeticPreview.DeathRequest);
                    uint rng1 = Rng.Rng1, rng2 = Rng.Rng2;
                    bool drawn = RmlUiHunterStageRenderer.DrawPicker(window, host, picker, hunter, suit,
                        CosmeticLoadout.Default, controls.Current(), width, height);
                    if (!drawn)
                    {
                        object Bounds(string id)
                        {
                            bool found = host.TryGetElementBounds(picker, id, out float x, out float y, out float w, out float h);
                            return new { id, found, x, y, width = w, height = h };
                        }
                        var gate = new { window.HasScene, host.Active, visible = host.IsVisible(picker),
                            currentInput = host.CurrentInputDocument, document = picker, preview = controls.Current(),
                            bounds = new[] { Bounds("native_hunter_preview"), Bounds("native_hunter_panel"), Bounds("stage") } };
                        Rows.Add(new { page = "PickerFailure", hunter = hunter.ToString(), suit, pose, gate,
                            capture = Capture(output, $"picker-failure-{hunter}-{suit}-{pose}", width, height) });
                        Console.WriteLine("NATIVE HUNTER PICKER GATE " + JsonSerializer.Serialize(gate));
                    }
                    Check(drawn, $"actual picker draws {hunter}/{suit}/{pose}");
                    Check(Canonical(sentinel) == before && Rng.Rng1 == rng1 && Rng.Rng2 == rng2,
                        "picker leaves canonical owner and RNG unchanged");
                    Check(beforeCosmetics == (CosmeticPreview.Loadout, CosmeticPreview.Mode, CosmeticPreview.Yaw,
                        CosmeticPreview.Zoom, CosmeticPreview.LoopDeath, CosmeticPreview.DeathRequest), "picker restores all temporary cosmetic controls");
                    // The existing death.classic presentation deliberately
                    // suppresses body meshes while its death request is active.
                    var witness = Preview(stageScene, hunter, suit, controls.Current().Mode,
                        requireMeshes: controls.Current().DeathRequest == 0);
                    StagePostconditions(width, height);
                    string? path = null;
                    if (capture)
                    {
                        byte[] painted = FinalCompositeCapture.Read(width, height);
                        Check(PixelChanges(background, painted) > 8, "actual Hunter changes native picker framebuffer pixels");
                        path = Capture(output, $"picker-{hunter}-{suit}-{pose}", width, height);
                    }
                    Rows.Add(new { page = "Picker", hunter = hunter.ToString(), suit, pose, controls = controls.Current(),
                        witness, canonicalBefore = before, canonicalAfter = Canonical(sentinel), rng1, rng2, capture = path });
                }
                Draw("biped", true);
                Action("hunter_rotate_left", RmlUiIntentKind.HunterRotate, 0, () => controls.Rotate(0)); Draw("rotate-left");
                Action("hunter_rotate_right", RmlUiIntentKind.HunterRotate, 1, () => controls.Rotate(1));
                Action("hunter_zoom_in", RmlUiIntentKind.HunterZoom, 1, () => controls.Zoom(1)); Draw("zoom-in", true);
                Action("hunter_zoom_out", RmlUiIntentKind.HunterZoom, 0, () => controls.Zoom(0));
                Action("hunter_preview_weapon", RmlUiIntentKind.HunterPreviewMode, 1, () => controls.SelectMode(1)); Draw("weapon", true);
                Action("hunter_preview_alt", RmlUiIntentKind.HunterPreviewMode, 2, () => controls.SelectMode(2)); Draw("alt", true);
                Action("hunter_preview_death", RmlUiIntentKind.HunterPreviewDeath, 0, controls.PreviewDeath); Draw("death");
                Action("hunter_loop_death", RmlUiIntentKind.HunterLoopDeath, 0, controls.ToggleLoopDeath); Draw("loop-death");
                Action("hunter_preview_reset", RmlUiIntentKind.HunterPreviewReset, 0, controls.Reset); Draw("reset");
                Action("hunter_loop_death", RmlUiIntentKind.HunterLoopDeath, 0, controls.ToggleLoopDeath); Draw("loop-off");
            }

            layout.Manager.ClosePage();
            // Exercise the real presenter and native events with explicit local
            // persistence services. Equip writes only these in-memory fixture stores.
            var localChoice = new LocalChoiceStore();
            var localCosmetics = new LocalCosmetics();
            using (var presenter = new NativeHunterPagePresenter(layout.Manager, localChoice, localCosmetics, host))
            {
                presenter.Open(); host.Update();
                var document = presenter.PreviewDocument;
                bool PickerAction(string id, RmlUiIntentKind kind, bool pointer = false)
                {
                    host.DiscardIntents();
                    Check(host.FocusDocument(document, id), "native presenter control focuses: " + id);
                    if (pointer)
                    {
                        for (int step = 0; step < 24; step++) { Thread.Sleep(20); host.Update(); }
                        Check(host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h)
                            && w > 0 && h > 0, "native comparison/default pointer bounds exist: " + id);
                        double px = x + w / 2, py = y + h / 2;
                        host.Input.PointerMoved(px, py);
                        Check(host.HoveredElement() == id, "actual pointer hits comparison/default control: " + id);
                        host.Input.PointerButton(0, px, py, true); host.Input.PointerButton(0, px, py, false);
                    }
                    else { host.Input.Key(2, true); host.Input.Key(2, false); }
                    host.Update();
                    int delivered = 0;
                    bool handled = false;
                    while (host.TryTakeIntent(out var intent))
                        if (intent.Document == document && intent.Kind == kind)
                        {
                            Check(RmlUiPageCommandRouting.Hunter(intent.Kind),
                                "actual native Hunter command reaches the production page routing predicate: " + id);
                            handled = presenter.Handle(intent); delivered++;
                        }
                    Check(delivered == 1, "one native event reaches the actual picker presenter: " + id);
                    return handled;
                }
                void DrawComparison(string label)
                {
                    Thread.Sleep(20);
                    host.Update(); Clear(width, height); NativeOverlay(host, width, height);
                    byte[] background = FinalCompositeCapture.Read(width, height);
                    var selection = presenter.PreviewSelection;
                    var beforeCosmetics = (CosmeticPreview.Loadout, CosmeticPreview.Mode, CosmeticPreview.Yaw,
                        CosmeticPreview.Zoom, CosmeticPreview.LoopDeath, CosmeticPreview.DeathRequest);
                    uint rng1 = Rng.Rng1, rng2 = Rng.Rng2;
                    Check(RmlUiHunterStageRenderer.DrawPicker(window, host, document, selection.Hunter,
                        selection.Suit, selection.Cosmetics, presenter.PreviewState, width, height),
                        "presenter comparison selection draws through existing engine stage: " + label);
                    Check(Canonical(sentinel) == canonicalBefore && Rng.Rng1 == rng1 && Rng.Rng2 == rng2
                        && beforeCosmetics == (CosmeticPreview.Loadout, CosmeticPreview.Mode, CosmeticPreview.Yaw,
                            CosmeticPreview.Zoom, CosmeticPreview.LoopDeath, CosmeticPreview.DeathRequest),
                        "comparison/default preview restores canonical state, RNG and cosmetic globals: " + label);
                    StagePostconditions(width, height);
                    var witness = Preview(stageScene, selection.Hunter, selection.Suit, presenter.PreviewState.Mode);
                    Check(PixelChanges(background, FinalCompositeCapture.Read(width, height)) > 8,
                        "actual comparison/default Hunter changes native framebuffer pixels: " + label);
                    Rows.Add(new { page = "PickerCompareNative", label, selection.Hunter, selection.Suit,
                        selection.Cosmetics, preview = presenter.PreviewState, witness,
                        identityWrites = localChoice.Writes, accountCalls = localCosmetics.Writes,
                        capture = Capture(output, "picker-compare-" + label, width, height) });
                }
                var custom = localCosmetics.Capture(Hunter.Samus);
                Check(custom != CosmeticLoadout.Default && presenter.PreviewSelection.Cosmetics == custom
                    && presenter.CanNavigateAway, "native comparison fixture starts with custom equipped cosmetics");
                Check(host.CurrentInputDocument == document && layout.Manager.Top == document,
                    "preview gesture fixture owns the actual foreground input document");
                Check(host.TryGetElementBounds(document, "native_hunter_preview", out float gx, out float gy,
                    out float gw, out float gh) && gw > 0 && gh > 0,
                    "gesture fixture measures the actual engine preview rectangle");
                float cx = gx + gw / 2, cy = gy + gh / 2;
                var gestureStart = presenter.PreviewState;
                Check(!presenter.PreviewPointerButton(gx - 1, cy, true)
                    && !presenter.PreviewPointerMove(cx, cy)
                    && !presenter.PreviewWheel(gx + gw, cy, 1)
                    && presenter.PreviewState == gestureStart,
                    "outside drag and exact right-edge wheel do not change preview state");
                Check(presenter.PreviewPointerButton(cx, cy, true)
                    && presenter.PreviewPointerMove(cx + gw / 8, cy)
                    && presenter.PreviewState.Yaw != gestureStart.Yaw,
                    "framebuffer-space drag inside measured preview rotates the real presenter");
                var released = presenter.PreviewState;
                Check(presenter.PreviewPointerButton(cx, cy, false)
                    && !presenter.PreviewPointerMove(cx + gw / 4, cy)
                    && presenter.PreviewState == released,
                    "pointer release ends preview rotation immediately");
                Check(presenter.PreviewWheel(cx, cy, 1)
                    && presenter.PreviewState.Zoom > gestureStart.Zoom,
                    "wheel inside the measured preview changes presenter zoom");
                Check(presenter.PreviewPointerButton(cx, cy, true), "start preview drag before modal cover");
                var covered = presenter.PreviewState;
                var cover = layout.Manager.OpenModal(new("gesture-cover", "pages/social/confirm.rml", "social_confirm_cancel"));
                host.Update(); presenter.Pump();
                Check(host.CurrentInputDocument == cover && layout.Manager.Top == cover
                    && !presenter.PreviewPointerMove(cx + gw / 8, cy)
                    && !presenter.PreviewWheel(cx, cy, 1)
                    && !presenter.PreviewPointerButton(cx, cy, true)
                    && presenter.PreviewState == covered,
                    "modal foreground blocks preview wheel/drag and cancels the held gesture");
                Check(layout.Manager.CloseModal(), "close fixture modal cover"); host.Update();
                Check(!presenter.PreviewPointerMove(cx + gw / 4, cy)
                    && presenter.PreviewState == covered,
                    "restoring the picker does not resume a drag cancelled by modal cover");
                Check(presenter.PreviewPointerButton(cx, cy, true), "start explicit capture-cancel fixture");
                presenter.CancelPreviewGesture();
                Check(!presenter.PreviewPointerMove(cx + gw / 8, cy)
                    && presenter.PreviewState == covered,
                    "explicit preview gesture cancellation releases drag ownership");
                Check(PickerAction("hunter_preview_reset", RmlUiIntentKind.HunterPreviewReset),
                    "restore preview orientation after gesture checks");
                DrawComparison("custom");
                Check(PickerAction("hunter_compare_native", RmlUiIntentKind.HunterCompareNative)
                    && presenter.PreviewState.CompareNative
                    && presenter.PreviewSelection.Cosmetics == CosmeticLoadout.Default && presenter.CanNavigateAway,
                    "actual Compare Native displays defaults without dirtying the equipped custom draft");
                DrawComparison("native");
                Check(PickerAction("hunter_compare_native", RmlUiIntentKind.HunterCompareNative, pointer: true)
                    && !presenter.PreviewState.CompareNative && presenter.PreviewSelection.Cosmetics == custom,
                    "second actual Compare Native restores the exact unchanged custom draft");
                DrawComparison("restored");
                Check(PickerAction("hunter_compare_native", RmlUiIntentKind.HunterCompareNative)
                    && PickerAction("hunter_category1", RmlUiIntentKind.HunterCosmeticCategory, pointer: true)
                    && PickerAction("hunter_armor_option1", RmlUiIntentKind.HunterArmorSelect, pointer: true)
                    && !presenter.PreviewState.CompareNative && !presenter.CanNavigateAway,
                    "a real cosmetic edit exits comparison and stages an unsaved draft");
                var unsavedCosmetics = presenter.PreviewSelection.Cosmetics;
                Check(!host.FocusDocument(document, "hunter_choice1"),
                    "unsaved cosmetics visibly disable switching to another Hunter");
                Check(NativeSemanticReadback.Read(host, document).Label("hunter_armor_description").Length > 0,
                    "real cosmetic catalog description is readable in native picker");
                Check(unsavedCosmetics != custom
                    && PickerAction("hunter_compare_native", RmlUiIntentKind.HunterCompareNative)
                    && presenter.PreviewSelection.Cosmetics == CosmeticLoadout.Default && !presenter.CanNavigateAway
                    && PickerAction("hunter_compare_native", RmlUiIntentKind.HunterCompareNative)
                    && presenter.PreviewSelection.Cosmetics == unsavedCosmetics && !presenter.CanNavigateAway,
                    "comparison preserves the exact unsaved cosmetic draft and its navigation guard");
                Check(PickerAction("hunter_discard", RmlUiIntentKind.HunterResetPreview)
                    && presenter.PreviewSelection.Cosmetics == custom && presenter.CanNavigateAway,
                    "cosmetic Discard restores the equipped custom loadout after comparison");
                Check(PickerAction("hunter_compare_native", RmlUiIntentKind.HunterCompareNative)
                    && PickerAction("hunter_rotate_right", RmlUiIntentKind.HunterRotate)
                    && PickerAction("hunter_zoom_in", RmlUiIntentKind.HunterZoom)
                    && PickerAction("hunter_loadout_default", RmlUiIntentKind.HunterLoadoutDefault, pointer: true)
                    && !presenter.PreviewState.CompareNative
                    && presenter.PreviewState.Yaw == 0 && presenter.PreviewState.Zoom == 1
                    && presenter.PreviewSelection.Cosmetics == CosmeticLoadout.Default
                    && !presenter.CanNavigateAway && localCosmetics.Capture(Hunter.Samus) == custom,
                    "actual Reset Loadout stages defaults, clears comparison and preserves persisted cosmetics");
                DrawComparison("default-draft");
                Check(!PickerAction("hunter_cancel", RmlUiIntentKind.HunterCancel) && !presenter.Closed,
                    "default cosmetic draft guards actual Back navigation");
                Check(PickerAction("hunter_discard", RmlUiIntentKind.HunterResetPreview)
                    && presenter.CanNavigateAway && presenter.PreviewSelection.Cosmetics == custom,
                    "Discard reverses Reset Loadout to the exact equipped loadout");
                Check(localChoice.Writes == 0 && localCosmetics.Writes == 0,
                    "Compare and Reset Loadout call no identity persistence or account service");
                Check(PickerAction("hunter_compare_native", RmlUiIntentKind.HunterCompareNative)
                    && PickerAction("hunter_preview_weapon", RmlUiIntentKind.HunterPreviewMode)
                    && !presenter.PreviewState.CompareNative && presenter.PreviewState.Mode == SkinContext.ViewModel,
                    "actual preview form change returns from comparison to draft cosmetics");
                Check(PickerAction("hunter_compare_native", RmlUiIntentKind.HunterCompareNative)
                    && PickerAction("hunter_choice1", RmlUiIntentKind.HunterSelect)
                    && !presenter.CanNavigateAway && presenter.PreviewSelection.Hunter == Hunter.Kanden
                    && !presenter.PreviewState.CompareNative,
                    "native Hunter selection clears comparison and owns an unsaved local draft");
                PickerAction("hunter_cancel", RmlUiIntentKind.HunterCancel);
                Check(!presenter.Closed && !presenter.OpenFullLicenseRequested && !presenter.CanNavigateAway,
                    "Back preserves the unsaved draft and its originating page");
                Check(typeof(NativeHunterPagePresenter).GetField("_navigationMessage", Hidden)?.GetValue(presenter)
                    is string { Length: > 0 }, "blocked navigation publishes an explicit notice");
                Check(!presenter.PrepareNavigation(), "header navigation uses the same visible unsaved-draft guard");
                Clear(width, height); host.Update(); NativeOverlay(host, width, height);
                Rows.Add(new { page = "PickerUnsavedNavigation", account = "NO ACCOUNT SERVICE", writes = localChoice.Writes,
                    capture = Capture(output, "picker-unsaved-navigation", width, height) });
                PickerAction("hunter_discard", RmlUiIntentKind.HunterResetPreview);
                Check(presenter.CanNavigateAway && presenter.PreviewSelection.Hunter == Hunter.Samus,
                    "Discard restores the original local choice before leaving");
                Check(PickerAction("hunter_suit1", RmlUiIntentKind.HunterSuit)
                    && PickerAction("hunter_category1", RmlUiIntentKind.HunterCosmeticCategory, pointer: true)
                    && PickerAction("hunter_armor_option1", RmlUiIntentKind.HunterArmorSelect),
                    "keyboard suit/catalog selection and pointer category selection stage one loadout");
                var equippedDraft = presenter.PreviewSelection;
                Check(equippedDraft.Suit == 1 && equippedDraft.Cosmetics.ArmorEffectKey == "armor.lightning"
                    && equippedDraft.Cosmetics != custom && !presenter.CanNavigateAway,
                    "direct catalog index1 chooses Lightning rather than fixture Phase index8");
                Check(PickerAction("hunter_apply", RmlUiIntentKind.HunterApply, pointer: true),
                    "one actual Equip Loadout pointer action reaches unified persistence");
                presenter.AfterUpdate(); host.Update();
                Check(host.FocusedElement() == "hunter_category1",
                    "Equip immediately restores enabled catalog focus while persistence is in flight");
                for (int step = 0; step < 100 && !presenter.CanNavigateAway; step++)
                { Thread.Sleep(10); presenter.Pump(); host.Update(); }
                Check(presenter.CanNavigateAway && localChoice.Writes == 1 && localCosmetics.Writes == 1
                    && localChoice.Capture().Hunter == equippedDraft.Hunter
                    && localChoice.Capture().Suit == equippedDraft.Suit
                    && localCosmetics.Capture(equippedDraft.Hunter) == equippedDraft.Cosmetics,
                    "unified Equip persists exact identity and cosmetics once to private fixture services");
                PickerAction("hunter_cancel", RmlUiIntentKind.HunterCancel);
                Check(presenter.Closed && localChoice.Writes == 1 && localCosmetics.Writes == 1,
                    "clean Back after Equip closes without additional persistence");
            }
            LobbyIdentityCards(window, host, layout, width, height, output);
            QuitAlignedScreens(host, layout, width, height, output);
            foreach (var backdrop in new[] {
                (Name:"community", Path:"pages/community/native-discover.rml", Scene:LauncherBackdropScene.MapEditor, Mood:"community-forge"),
                (Name:"studios", Path:"pages/studio/native-entry.rml", Scene:LauncherBackdropScene.ReplayStudio, Mood:"studio-technical") })
            {
                var document = layout.Manager.OpenPage(new("background-" + backdrop.Name, backdrop.Path, "nav_play"));
                if (backdrop.Name == "studios")
                {
                    host.SetBool(document,"visible:native_studio_chooser",true);
                }
                else
                {
                    host.SetText(document,"native_community_status","LOCAL BACKGROUND FIXTURE // NO ACCOUNT OR NETWORK");
                    host.SetText(document,"native_community_title","SYNTHETIC COMMUNITY ARENA");
                    host.SetText(document,"native_community_description","Owned procedural forge background with native Community panels.");
                }
                host.Update(); Clear(width,height);
                RmlUiHunterStageRenderer.Draw(window,host,RmlUiMenuPage.Home,false,
                    new("LOCAL BACKGROUND FIXTURE",Hunter.Trace,Hunter.Trace,0,true,"LOCAL"),
                    default,null,width,height,backdrop.Scene);
                Check(LauncherBackdrop.Scene==backdrop.Scene && LauncherPresentation.Motion.Activity.Name==backdrop.Mood,
                    "native background uses its explicit route rather than the retained Home page: " + backdrop.Name);
                Check(Canonical(sentinel)==canonicalBefore && !window.HasScene,
                    "background-only composition preserves canonical state: " + backdrop.Name);
                StagePostconditions(width,height); NativeOverlay(host,width,height);
                Rows.Add(new {page="NativeBackground",route=backdrop.Name,ambience=backdrop.Mood,
                    scope="Actual procedural background and authored DOM only; no account/backend or live data",
                    capture=Capture(output,"background-"+backdrop.Name,width,height)});
                layout.Manager.ClosePage();
            }
            var lobbyDocument = layout.ShowBaseline(RmlUiMenuPage.Lobby);
            layout.SetText("lobby_name", "EIGHT EXPLICIT FIXTURE PADS");
            layout.SetText("lobby_map", "MP3 PROVING GROUND // NAME ONLY");
            layout.SetText("lobby_status", "NO NETWORK OR AUTHENTICATED PLAYERS"); layout.Flush(); host.Update(); layout.AfterUpdate();
            var people = Enumerable.Range(0, 8).Select(i => new RmlUiAdminPlayer((byte)i, (ushort)(10 + i),
                "FIXTURE " + i, (Hunter)(i % 7), (byte)(i % 4), -1, true, false, 0, 0, 0)).ToArray();
            var roster = new RmlUiAdminSnapshot(1, 1, 1, false, 0, 7, false, false, 0, 8,
                false, "FIXTURE", "", Enumerable.Range(0, 7).Select(i => (Hunter)i).ToArray(), people, Array.Empty<string>());
            var lobby = new RmlUiLobbyView(true, false, false, false, true, false, false,
                "FIXTURE", "MP3 PROVING GROUND", "Battle", "8 FIXTURE PADS", "NO NETWORK", 8, Array.Empty<RmlUiLobbyPlayer>());
            var lobbyHub = new HubSnapshot("FIXTURE 7", Hunter.Samus, Hunter.Samus, 0, true, "LOCAL GL FIXTURE");
            var plan = RmlUiHunterStagePlan.Build(lobbyHub, lobby, roster);
            Check(plan.Count == 8 && plan.Formation[0].NetSlot == 7 && plan.Formation[0].Local,
                "explicit local slot 7 sorts to front pad without network identity");
            layout.SetText("lobby_brief_title", "EXPLICIT FIXTURE LOBBY");
            for (int pad = 0; pad < 8; pad++)
            {
                layout.SetBool($"slot{pad}_occupied", true);
                layout.SetBool($"slot{pad}_ready", true);
                layout.SetBool($"slot{pad}_local", pad == 0);
                layout.SetText($"slot{pad}_name", "FIXTURE " + (pad + 1));
                layout.SetText($"slot{pad}_hunter", plan.Formation[pad].Hunter.ToString().ToUpperInvariant());
                layout.SetText($"slot{pad}_state", "READY");
            }
            layout.Flush(); host.Update(); layout.AfterUpdate();
            Clear(width, height);
            RmlUiHunterStageRenderer.Draw(window, host, RmlUiMenuPage.Lobby, false, lobbyHub, lobby, roster, width, height);
            Check(Canonical(sentinel) == canonicalBefore, "eight-pad drawing preserves inactive canonical state/RNG");
            var slots = (HunterPreviewEntity?[])typeof(Scene).GetField("_launcherSlotPreviews", Hidden)!.GetValue(stageScene)!;
            for (int pad = 0; pad < 8; pad++)
            {
                Check(slots[pad] is { Ready: true } actor && actor.Shown == plan.Formation[pad].Hunter
                    && actor.ShownSuit == plan.Formation[pad].Suit, "actual cached mesh matches fixture formation pad " + pad);
                Check(LauncherLobbyVisuals.IdentityAt(pad) == plan.Formation[pad].Identity,
                    "native nameplate and actual model share fixture identity " + pad);
            }
            StagePostconditions(width, height); NativeOverlay(host, width, height);
            Rows.Add(new { page = "Lobby", fixture = "explicit-eight-pad-roster", localFrontSlot = 7,
                canonicalBefore, canonicalAfter = Canonical(sentinel), capture = Capture(output, "lobby-eight-pads", width, height) });
            Check(!NetSession.Active && !DemoPlayback.IsActive, "fixture ends without starting network or replay");
            Check(Menu.SaveSlot == 0 && !Directory.Exists("Savedata"), "stage initialization and draws create no settings/save directory");
            if (expectedNative != null)
                LoadedNativeModuleCheck.WriteNew(Path.Combine(output, "loaded-native-module-post.json"),
                    LoadedNativeModuleCheck.RequireLoaded(expectedNative));
            status = "LOCAL_GL_FIXTURE_PASS";
            Console.WriteLine($"NATIVE HUNTER STAGE {_checks} checks passed; actual local assets, no device/performance acceptance");
            return 0;
        }
        finally
        {
            try
            {
                layout?.Dispose(); host?.Dispose(); RmlUiHunterStageRenderer.Hide();
                previewScene?.UnloadGl();
                typeof(LauncherHunter).GetField("_scene", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
                LauncherHunter.Reset();
                if (window != null) { sceneField.SetValue(window, sentinel); window.EndScene(); window.Dispose(); }
                MphRead.Sound.AudioLifetime.Shutdown();
                Read.ClearCache();
            }
            finally
            {
                RenderOptions.CharacterModelReplacements = hd;
                CosmeticPreview.Loadout = loadout; CosmeticPreview.Mode = mode; CosmeticPreview.Yaw = yaw;
                CosmeticPreview.Zoom = zoom; CosmeticPreview.LoopDeath = loop; CosmeticPreview.DeathRequest = death;
                File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new {
                    schema = 1, status, scope = Scope, checks = _checks, hardware,
                    clientSha256 = Hash(File.ReadAllBytes(typeof(Scene).Assembly.Location)),
                    pathsFileSha256 = Hash(File.ReadAllBytes(Path.Combine(userData, "paths.txt"))), rows = Rows
                }, new JsonSerializerOptions { WriteIndented = true }));
                Directory.SetCurrentDirectory(originalDirectory);
            }
        }
    }

    private static Scene PreviewScene() => (Scene)typeof(LauncherHunter).GetField("_scene",
        BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static object Preview(Scene scene, Hunter hunter, int suit, SkinContext expectedMode, bool requireMeshes = true)
    {
        var actor = (HunterPreviewEntity)typeof(Scene).GetField("_activePreview", Hidden)!.GetValue(scene)!;
        var model = (ModelInstance)typeof(HunterPreviewEntity).GetField("_model", Hidden)!.GetValue(actor)!;
        var items = (List<RenderItem>)typeof(Scene).GetField("_previewItems", Hidden)!.GetValue(scene)!;
        int meshes = items.Count(item => item.Type == RenderItemType.Mesh);
        int index = expectedMode == SkinContext.ViewModel ? 3 : expectedMode == SkinContext.AltForm ? 2 : 0;
        bool matches = actor.Ready && actor.Shown == hunter && actor.ShownSuit == suit && actor.Mode == expectedMode
            && model.Model.Name == Metadata.HunterModels[hunter][index] && (!requireMeshes || meshes > 0);
        if (!matches) Console.WriteLine("NATIVE HUNTER MODEL WITNESS " + JsonSerializer.Serialize(new {
            expectedHunter = hunter, expectedSuit = suit, expectedMode, expectedModel = Metadata.HunterModels[hunter][index],
            actor.Ready, actor.Shown, actor.ShownSuit, actor.Mode, model = model.Model.Name, meshes,
            items = items.Select(item => item.Type.ToString()).ToArray() }));
        Check(matches,
            "actual native model, suit, form and nonempty mesh submissions agree");
        return new { model = model.Model.Name, meshes, requireMeshes, form = actor.Mode.ToString() };
    }

    private static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException("NATIVE HUNTER STAGE: " + label); _checks++; }
    private static object RequireHomeGeometry(RmlUiHost host, RmlUiDocumentToken document, int width, int height)
    {
        Check(host.TryGetElementBounds(document, "home_left_rail", out float lx, out float ly, out float lw, out float lh)
            && host.TryGetElementBounds(document, "home_right_rail", out _, out _, out _, out _)
            && host.TryGetElementBounds(document, "stage", out _, out _, out _, out _),
            "actual Home composition exposes both scrolling rails and stage bounds");
        host.TryGetElementBounds(document, "home_right_rail", out float rx, out float ry, out float rw, out float rh);
        host.TryGetElementBounds(document, "stage", out float sx, out float sy, out float sw, out float sh);
        var measured = RmlUiHunterStageRenderer.ReadHomeLayout(host, document, width, height);
        Check(measured.HasValue, "actual Home rails admit a bounded shared stage layout");
        var geometry = measured!.Value;
        const float tolerance = .00001f;
        float gapLeft = (lx + lw) / width, gapRight = rx / width;
        float gapCenter = (gapLeft + gapRight) * .5f;
        Check(Math.Abs(geometry.Left - gapLeft) < tolerance && Math.Abs(geometry.Right - gapRight) < tolerance
            && Math.Abs(geometry.CenterX - gapCenter) < tolerance
            && Math.Abs((LauncherHunter.Left + LauncherHunter.Right) * .5f - gapCenter) < tolerance
            && Math.Abs(geometry.Pad(0).X - gapCenter) < tolerance,
            "actual rail gap, shared Home layout, engine Hunter viewport and hero pedestal have one center");
        Check(Math.Abs(LauncherHunter.Left - geometry.HunterLeft) < tolerance
            && Math.Abs(LauncherHunter.Right - geometry.HunterRight) < tolerance
            && Math.Abs(LauncherHunter.Top - geometry.HunterTop) < tolerance
            && Math.Abs(LauncherHunter.Bottom - geometry.HunterBottom) < tolerance
            && Math.Abs(Scene.PreviewLeft - geometry.HunterLeft) < tolerance
            && Math.Abs(Scene.PreviewRight - geometry.HunterRight) < tolerance
            && Math.Abs(Scene.PreviewTop - geometry.HunterTop) < tolerance
            && Math.Abs(Scene.PreviewBottom - geometry.HunterBottom) < tolerance,
            "actual engine preview consumes the shared Home viewport on all four edges");
        var profile = LauncherMenuStage.Current;
        float horizontalScale = (LauncherHunter.Right - LauncherHunter.Left) / (profile.HunterRight - profile.HunterLeft);
        float verticalScale = (LauncherHunter.Bottom - LauncherHunter.Top) / (profile.HunterBottom - profile.HunterTop);
        Check(Math.Abs(horizontalScale - verticalScale) < tolerance
            && Math.Abs(horizontalScale - geometry.Scale) < tolerance,
            "Home uniformly scales the existing Hunter viewport to preserve its camera aspect");

        int program = (int)typeof(LauncherBackdropComposer)
            .GetField("_program", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Check(program != 0 && GL.IsProgram(program), "Home geometry is submitted to the real chamber program");
        int modeLocation = GL.GetUniformLocation(program, "home_layout_mode");
        Check(modeLocation >= 0, "real chamber exposes the Home layout mode uniform");
        GL.GetUniform(program, modeLocation, out float homeMode);
        Check(homeMode == 1, "actual native Home selects measured rail geometry in its chamber shader");
        const float visibleScale = 1.025f;
        var pads = Enumerable.Range(0, 4).Select(index =>
        {
            var pad = geometry.Pad(index);
            int location = GL.GetUniformLocation(program, "home_pad_geometry[" + index + "]");
            Check(location >= 0, "real chamber exposes Home pedestal geometry " + index);
            float[] uploaded = new float[4];
            GL.GetUniform(program, location, uploaded);
            Check(Math.Abs(uploaded[0] - pad.X) < tolerance && Math.Abs(uploaded[1] - pad.Y) < tolerance
                && Math.Abs(uploaded[2] - pad.RadiusX) < tolerance && Math.Abs(uploaded[3] - pad.RadiusY) < tolerance,
                "actual Home shader pedestal matches shared geometry " + index);
            float left = pad.X - pad.RadiusX * visibleScale, right = pad.X + pad.RadiusX * visibleScale;
            float top = pad.Y - pad.RadiusY * visibleScale, bottom = pad.Y + pad.RadiusY * visibleScale;
            Check(left > gapLeft && right < gapRight && top >= sy / height && bottom <= (sy + sh) / height,
                "complete visible Home pedestal stays between actual rails and within stage " + index);
            return new { index, x = pad.X, y = pad.Y, radiusX = pad.RadiusX, radiusY = pad.RadiusY,
                left, right, top, bottom, uploaded };
        }).ToArray();
        for (int a = 0; a < pads.Length; a++)
        for (int b = a + 1; b < pads.Length; b++)
            Check(pads[a].right < pads[b].left || pads[b].right < pads[a].left
                || pads[a].bottom < pads[b].top || pads[b].bottom < pads[a].top,
                "complete visible Home pedestal bounds are separate: " + a + "/" + b);
        Check(pads[1].y - .32f * geometry.Scale > geometry.StageTop + .03f
            && pads[1].top > LauncherHunter.Top && pads[1].top > Scene.PreviewTop,
            "rear Home floor platform leaves occupied Hunter headroom below the header");
        // Geometry-only occupied-seat envelope: no fabricated remote Hunter is
        // rendered. Preserve the authored viewport aspect at a far-seat height
        // and reserve a complete body above each of the three real pad centers.
        float partyHeight = .32f * geometry.Scale;
        float partyWidth = partyHeight * (profile.HunterRight - profile.HunterLeft)
            / (profile.HunterBottom - profile.HunterTop);
        var hypotheticalPartyViewports = pads.Skip(1).Select(pad =>
        {
            float left = pad.x - partyWidth * .5f, right = pad.x + partyWidth * .5f;
            float top = pad.y - partyHeight, bottom = pad.y;
            Check(top > geometry.StageTop + .03f && bottom <= geometry.StageBottom
                && left > gapLeft && right < gapRight,
                "hypothetical occupied Home party viewport fits below header between rails: " + pad.index);
            return new { pad.index, left, right, top, bottom };
        }).ToArray();
        Check(LauncherBackdropComposer.HomeLayout == null,
            "actual Home draw retires temporary compositor geometry after uploading its uniforms");
        return new { gapLeft, gapRight, gapCenter, scale = geometry.Scale,
            rails = new[] { new { x = lx, y = ly, width = lw, height = lh }, new { x = rx, y = ry, width = rw, height = rh } },
            stage = new { x = sx, y = sy, width = sw, height = sh },
            hunter = new { left = LauncherHunter.Left, top = LauncherHunter.Top, right = LauncherHunter.Right,
                bottom = LauncherHunter.Bottom, horizontalScale, verticalScale }, homeMode, pads, hypotheticalPartyViewports };
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Canonical(Scene scene)
    {
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
        var state = scene.GameState;
        writer.Write(scene.FrameCount); writer.Write((int)state.Mode); writer.Write(state.PointGoal);
        writer.Write(state.TimeGoal); writer.Write(state.MatchTime); writer.Write((int)state.MatchState);
        writer.Write(state.Teams); writer.Write(state.TeamCount); writer.Write(scene.Random.Rng1); writer.Write(scene.Random.Rng2);
        foreach (int value in state.Kills.Concat(state.Deaths).Concat(state.Standings)) writer.Write(value);
        foreach (var player in scene.Players.Items)
        {
            writer.Write((int)player.Hunter); writer.Write(player.Health); writer.Write((uint)player.Flags1); writer.Write((uint)player.Flags2);
            foreach (var vector in new[] { player.Position, player.Speed, player.FacingVector })
            { writer.Write(vector.X); writer.Write(vector.Y); writer.Write(vector.Z); }
        }
        writer.Flush(); return Hash(bytes.ToArray());
    }
    private static void Clear(int width, int height)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
        GL.Disable(EnableCap.ScissorTest); GL.DepthMask(true); GL.ClearColor(.04f, .06f, .09f, 1);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
    }
    private static int PixelChanges(byte[] before, byte[] after)
    {
        int count = 0;
        for (int at = 0; at < Math.Min(before.Length, after.Length); at += 3)
            if (Math.Abs(before[at] - after[at]) + Math.Abs(before[at + 1] - after[at + 1])
                + Math.Abs(before[at + 2] - after[at + 2]) > 20) count++;
        return count;
    }
    private static string Capture(string output, string name, int width, int height)
    {
        string path = Path.Combine(output, name + ".png");
        GL.Finish(); Check(ScreenCapture.SaveWindow(width, height, path), "private native PNG capture succeeds");
        FinalCompositeCapture.WriteEvidence(path, width, height, Scope);
        return Path.GetFileName(path);
    }
    private static void StagePostconditions(int width, int height)
    {
        var viewport = new int[4]; GL.GetInteger(GetPName.Viewport, viewport);
        Check(GL.GetInteger(GetPName.CurrentProgram) == 0 && GL.GetInteger(GetPName.ActiveTexture) == (int)TextureUnit.Texture0
            && GL.GetInteger(GetPName.DrawFramebufferBinding) == 0 && GL.GetInteger(GetPName.ReadFramebufferBinding) == 0
            && viewport.SequenceEqual(new[] { 0, 0, width, height }) && !GL.IsEnabled(EnableCap.ScissorTest)
            && GL.GetInteger(GetPName.DepthWritemask) != 0, "stage restores its documented compositor-boundary GL state");
        Check(!LauncherHunter.Wanted && LauncherHunter.CanPresent == null && !Scene.LauncherPreview
            && !Scene.LauncherPreviewFormation && !Scene.LauncherPreviewTransparent,
            "stage clears temporary preview ownership");
        Check(GL.GetError() == ErrorCode.NoError, "native stage has no GL error");
    }
    private static string GlState()
    {
        var viewport = new int[4]; GL.GetInteger(GetPName.Viewport, viewport);
        return String.Join(",", viewport) + ":" + String.Join(",", new[] {
            GL.GetInteger(GetPName.CurrentProgram), GL.GetInteger(GetPName.ActiveTexture),
            GL.GetInteger(GetPName.DrawFramebufferBinding), GL.GetInteger(GetPName.ReadFramebufferBinding),
            GL.GetInteger(GetPName.DepthWritemask), GL.IsEnabled(EnableCap.ScissorTest) ? 1 : 0,
            GL.IsEnabled(EnableCap.DepthTest) ? 1 : 0, GL.IsEnabled(EnableCap.Blend) ? 1 : 0,
            GL.GetInteger(GetPName.TextureBinding2D), GL.GetInteger(GetPName.MatrixMode),
            GL.GetInteger(GetPName.DepthFunc), GL.GetInteger(GetPName.BlendSrcRgb), GL.GetInteger(GetPName.BlendDstRgb),
            GL.IsEnabled(EnableCap.CullFace) ? 1 : 0, GL.IsEnabled(EnableCap.StencilTest) ? 1 : 0,
            GL.IsEnabled(EnableCap.AlphaTest) ? 1 : 0 });
    }
    private static void NativeOverlay(RmlUiHost host, int width, int height)
    {
        string before = GlState(); RmlUiOpenGlFrame.Draw(host, width, height);
        Check(GlState() == before, "native UI composite preserves stage GL state");
    }

    private sealed class LocalChoiceStore : ILauncherHunterChoiceStore
    {
        private LauncherHunterChoice _choice = new(Hunter.Samus, 0);
        internal int Writes { get; private set; }
        public LauncherHunterChoice Capture() => _choice;
        public void Save(LauncherHunterChoice choice) { Writes++; _choice = choice; }
    }
    private sealed class LocalCosmetics : ILauncherCosmeticEquipService
    {
        internal int Writes { get; private set; }
        private readonly Dictionary<Hunter, CosmeticLoadout> _equipped = new();
        public CosmeticLoadout Capture(Hunter hunter) => _equipped.TryGetValue(hunter, out var equipped)
            ? equipped : new("skin." + hunter.ToString().ToLowerInvariant() + ".obsidian", "armor.phase", "death.cryo_shatter");
        public bool PendingSync(Hunter hunter) => false;
        public Task<string> EquipAsync(Hunter hunter, CosmeticLoadout loadout, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Writes++; _equipped[hunter] = loadout;
            return Task.FromResult("LOCAL FIXTURE EQUIPPED");
        }
    }
}
