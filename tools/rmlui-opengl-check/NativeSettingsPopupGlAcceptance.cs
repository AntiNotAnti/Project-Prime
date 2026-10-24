using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Real RmlUi/OpenGL layout and input, with detached popup rows and a fake display
// callback. Integrated visual autosave checks commit only in the required private
// user-data process. No maintenance, network or game assets are used.
internal static class NativeSettingsPopupGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string root = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? "";
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Popup fixture requires private PROJECT_PRIME_USER_DATA.");
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        Directory.SetCurrentDirectory(root);
        if (!String.Equals(Path.GetFullPath(Path.Combine("Savedata", "settings.json")), Path.Combine(root, "Savedata", "settings.json"), StringComparison.OrdinalIgnoreCase)
            || !String.Equals(Path.GetFullPath(LauncherPrefs.Directory), root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fixture game settings and launcher preferences must stay in the explicit private root.");
        string density = Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_CHECK_DENSITY") ?? "1";
        string output = Path.Combine(root, "native-settings-popups", $"{width}x{height}-density-{density}");
        Directory.CreateDirectory(output);
        int checks = 0;
        var geometry = new List<object>();
        var modes = new List<WindowStartMode>();
        using var pages = new RmlUiPageManager(host);
        using var page = new NativeFullSettingsPage(host, pages, new MenuSettings(), modes.Add);
        var controller = (NativeFullSettingsController)typeof(NativeFullSettingsPage).GetField("_controller", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        controller.AutoApply = false;
        host.Input.SetFramebufferScale(1, 1);
        page.Open(); Settle(); Capture("overview");
        var keyboard = controller.Rows.Where(row => row.KeyboardBinding && row.Group == "Keyboard bindings").Take(2).ToArray();
        Check(keyboard.Length == 2, "fixture uses canonical keyboard binding owners");
        controller.Stage(keyboard[0], "F1"); controller.Stage(keyboard[1], "F2");
        controller.BeginCapture(keyboard[0]); Settle();
        RmlUiDocumentToken popup = pages.Top;
        Check(popup != page.Document && page.OwnsForeground && host.CurrentInputDocument == popup && pages.ModalCount == 1,
            "capture uses a real foreground native modal over Settings");
        Check(!page.Handle(new(RmlUiIntentKind.SettingsCategory, 1, page.Document, 90000)),
            "background Settings document cannot dispatch while capture owns input");
        Check(!host.FocusDocument(page.Document, "native_settings_search"), "native modal blocks focus on underlying settings fields");
        PromptAboveActions("native_settings_capture_text", new[] { "native_settings_capture_cancel" });
        BackgroundBlocked(); Capture("capture");
        Check(page.KeyDown(Keys.F2, default), "keyboard proposal reaches active native capture"); Settle();
        Check(controller.BindingConflict && pages.Top == popup && page.OwnsForeground,
            "capture transitions to binding conflict in the same modal owner");
        PromptAboveActions("native_settings_confirmation_text", new[] { "native_settings_confirm", "native_settings_conflict_replace", "native_settings_conflict_keep", "native_settings_confirmation_cancel" });
        Capture("binding-conflict");
        Check(page.Back() && !controller.BindingConflict && !page.Closed, "Escape/Back dismisses conflict before leaving Settings"); Settle();
        Check(pages.Top == page.Document && pages.ModalCount == 0, "dismissing conflict restores the Settings document");

        controller.BeginCapture(keyboard[0]); Settle();
        var cancel = Box(pages.Top, "native_settings_capture_cancel");
        host.Input.PointerMoved(cancel.X + cancel.W / 2, cancel.Y + cancel.H / 2); host.Update();
        Check(!page.MouseDown(MouseButton.Left) && controller.Capturing,
            "clicking Cancel reaches the popup control instead of binding Mouse Left");
        host.DiscardIntents(); Click(cancel);
        Check(host.TryTakeIntent(out var intent) && intent.Document == pages.Top && intent.Argument == 24 && page.Handle(intent),
            "actual Cancel click emits and accepts the exact foreground popup intent"); Settle();
        Check(!controller.Capturing && pages.Top == page.Document && keyboard[0].Value == "F1", "Cancel retires only the capture modal and preserves the original binding");

        controller.BeginCapture(keyboard[0]); Settle();
        var coveredPopup = pages.Top;
        var cover = pages.OpenModal(new("popup-capture-cover", "pages/common/quit.rml", "quit_cancel")); Settle();
        Check(!controller.Capturing && !page.KeyDown(Keys.Delete, default) && pages.Top == cover && host.IsAlive(coveredPopup),
            "another foreground modal cancels capture without retiring the new foreground document");
        Check(pages.CloseModal(), "fixture retires its own cover without deciding Quit"); Settle();
        Check(pages.Top == page.Document && pages.ModalCount == 0 && keyboard[0].Value == "F1",
            "returning from a cover retires cancelled capture and preserves its binding");

        controller.Discard(); page.Refresh();
        var window = controller.Rows.Single(row => row.Group == "Window");
        controller.Stage(window, window.Choices.First(value => value != window.Saved));
        Check(controller.Apply() && controller.ConfirmingVideo, "fake display callback opens guarded display confirmation");
        Settle();
        Check(controller.CanKeepVideo && pages.Top != page.Document && host.CurrentInputDocument == pages.Top,
            "usable popup frame acknowledges display trial while retaining input ownership");
        PromptAboveActions("native_settings_video_countdown", new[] { "native_settings_keep_video", "native_settings_revert_video" });
        Capture("display-confirmation");
        Check(page.Back() && !controller.ConfirmingVideo && !page.Closed, "Escape/Back reverts display before leaving Settings"); Settle();
        Check(modes.Count == 2 && pages.Top == page.Document, "display trial reverts through the fake callback and restores Settings");
        controller.Discard(); page.Refresh();

        // Per-setting help is a native modal, so it owns focus and prevents
        // the Settings form from capturing input or auto-saving underneath it.
        host.DiscardIntents(); Click(Box(page.Document, "native_settings_category_0"));
        Check(host.TryTakeIntent(out var displayIntent) && displayIntent.Document == page.Document && displayIntent.Kind == RmlUiIntentKind.SettingsCategory
            && page.Handle(displayIntent), "actual Display category click opens its settings sections"); Settle();
        host.DiscardIntents(); Click(Box(page.Document, "native_settings_subpage_0"));
        Check(host.TryTakeIntent(out var viewIntent) && viewIntent.Document == page.Document && viewIntent.Argument == 232
            && page.Handle(viewIntent), "actual View section click exposes display settings"); Settle();
        var visibleRows = (NativeSettingRow[])typeof(NativeFullSettingsPage).GetField("_visible", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        int weaponHelpIndex = Array.FindIndex(visibleRows, row => row.Label == "Weapon presentation" && row.HasHelp);
        Check(weaponHelpIndex >= 0, "weapon presentation exposes its setting help button");
        Reveal(page.Document,"native_settings_info_"+weaponHelpIndex);
        host.DiscardIntents(); Click(Box(page.Document, "native_settings_info_" + weaponHelpIndex));
        Check(host.TryTakeIntent(out var helpIntent) && helpIntent.Document == page.Document && helpIntent.Argument == 700 + weaponHelpIndex
            && page.Handle(helpIntent), "actual setting info click opens the foreground help dialog"); Settle();
        RmlUiDocumentToken helpDocument = pages.Top;
        Check(helpDocument != page.Document && pages.ModalCount == 1 && host.CurrentInputDocument == helpDocument,
            "setting help opens as a foreground modal document over Settings");
        Check(!host.FocusDocument(page.Document, "native_settings_search"), "help modal blocks focus on the parent Settings document");
        Check(!page.KeyDown(Keys.F2, default) && !controller.Capturing, "help modal prevents parent Settings from capturing gameplay input");
        var showFps = controller.Rows.Single(row => row.Label == "Show FPS");
        string savedFps = showFps.Saved;
        controller.Stage(showFps, savedFps.Equals("on", StringComparison.OrdinalIgnoreCase) ? "off" : "on");
        controller.AutoApply = true;
        Settle(500);
        Check(controller.AutoApplyPending && showFps.Saved == savedFps, "help modal pauses automatic saving of the underlying Settings draft");
        Contained(helpDocument,"native_settings_help_close");Capture("setting-help");
        var helpClose = Box(helpDocument, "native_settings_help_close");
        host.Input.PointerMoved(helpClose.X + helpClose.W / 2, helpClose.Y + helpClose.H / 2); host.Update();
        Check(!page.MouseDown(MouseButton.Left), "help close click is left for native RmlUi dispatch instead of consumed as capture input");
        Check(!page.ScrollBinding(true), "help scrolling is left for native RmlUi dispatch instead of consumed as binding input");
        host.DiscardIntents(); Click(helpClose);
        controller.AutoApply = false;
        Check(host.TryTakeIntent(out var helpCloseClick) && helpCloseClick.Document == helpDocument && helpCloseClick.Argument == 247
            && page.Handle(helpCloseClick), "actual help Close click emits and accepts its close intent"); Settle();
        controller.Discard(); page.Refresh();
        Check(showFps.Value == savedFps && !controller.Dirty, "closing help retains the underlying draft without committing it");
        Check(pages.Top == page.Document && pages.ModalCount == 0, "native Close click retires help and restores Settings focus");

        Reveal(page.Document,"native_settings_info_"+weaponHelpIndex);
        host.DiscardIntents(); Click(Box(page.Document, "native_settings_info_" + weaponHelpIndex));
        Check(host.TryTakeIntent(out var keyboardHelpIntent) && keyboardHelpIntent.Argument == 700 + weaponHelpIndex
            && page.Handle(keyboardHelpIntent), "setting help can be reopened for keyboard interaction"); Settle();
        helpDocument = pages.Top;
        Check(helpDocument != page.Document && host.CurrentInputDocument == helpDocument
            && !host.FocusDocument(page.Document, "native_settings_search"), "keyboard help modal retains focus away from parent Settings");
        Check(!page.KeyDown(Keys.Tab, default), "Tab reaches native RmlUi while setting help is open");
        host.Input.Key(1, true); host.Input.Key(1, false); host.Update();
        Check(pages.Top == helpDocument && host.CurrentInputDocument == helpDocument
            && host.FocusedElement() == "native_settings_help_close", "native Tab traversal stays inside the help modal");
        Check(host.FocusDocument(helpDocument, "native_settings_help_close"), "help Close remains keyboard-focusable"); Settle();
        Check(!page.KeyDown(Keys.Enter, default), "Enter reaches native RmlUi while setting help is open");
        host.Input.Key(2, true); host.Input.Key(2, false); host.Update();
        Check(host.TryTakeIntent(out var helpEnter) && helpEnter.Document == helpDocument && helpEnter.Argument == 247
            && page.Handle(helpEnter), "native Enter activates the focused help Close button"); Settle();
        Check(pages.Top == page.Document && pages.ModalCount == 0, "keyboard activation closes help and restores Settings focus");

        // Non-settings dialogs share the semantic block defaults as well.
        var quit = pages.OpenModal(new("popup-quit-fixture", "pages/common/quit.rml", "quit_cancel")); Settle();
        var heading = Box(quit, "quit_title"); var message = Box(quit, "quit_message"); var actions = Box(quit, "update_actions");
        Check(message.Y >= heading.Bottom - 1 && actions.Y >= message.Bottom - 1, "shared theme keeps Quit heading, message and actions in separate blocks");
        Contained(quit, "update_card"); Contained(quit, "quit_cancel"); Contained(quit, "quit_confirm"); Capture("quit-dialog");
        Check(pages.CloseModal(), "fixture closes Quit without requesting application exit");
        var update = pages.OpenModal(new("popup-update-fixture", "pages/updates/available.rml", "update_later"));
        host.SetText(update, "update_installed", "1.0.0"); host.SetText(update, "update_available", "1.0.1"); Settle();
        var intro = Box(update, "update_intro"); var installed = Box(update, "update_installed");
        var description = Box(update, "update_description"); var updateActions = Box(update, "update_actions");
        Check(installed.Y >= intro.Bottom - 1 && updateActions.Y >= description.Bottom - 1,
            "shared theme keeps Update paragraphs separate from versions and actions");
        Contained(update, "update_card"); Reveal(update, "update_confirm"); Contained(update, "update_confirm"); Capture("update-dialog");
        Check(pages.CloseModal(), "fixture closes Update without updater side effects");
        page.Dispose();
        pages.OpenPage(new("settings-fixture-workspace", "pages/studio/native-entry.rml", "nav_play"));
        SettingsOrganizationAcceptance.Run(host, pages, new MenuSettings(), name => Capture("organization-" + name), width, height);
        NativeNestedAutoApplyAcceptance.Run(host, pages, name => Capture(name));
        NativeLiveSettingsPreviewAcceptance.Run(host,pages,width,height,name=>Capture(name));
        Check(GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError, "popup render leaves no GL error");
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { passed = true, checks, width, height, density,
            privateDirectory = Directory.GetCurrentDirectory(), settingsPath = Path.GetFullPath(Path.Combine("Savedata", "settings.json")), launcherPreferenceDirectory = LauncherPrefs.Directory, geometry }, new JsonSerializerOptions { WriteIndented = true }));

        void Frame()
        {
            page.Refresh(); host.Update();
            host.SetBool(pages.Top, "class:@document:reduce-motion", true); host.Update();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.025f, .05f, .08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height); page.AcknowledgeUsableFrame();
        }
        void Settle(int milliseconds = 80)
        {
            var watch = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(5); } while (watch.ElapsedMilliseconds < milliseconds);
        }
        Rect Box(RmlUiDocumentToken document, string id)
        {
            bool exists = host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h);
            geometry.Add(new { id, x, y, w, h }); Check(exists && w > 0 && h > 0, "native element has positive layout: " + id);
            return new(x, y, w, h);
        }
        void Contained(RmlUiDocumentToken document, string id)
        {
            var box = Box(document, id); Check(box.X >= -1 && box.Y >= -1 && box.Right <= width + 1 && box.Bottom <= height + 1, "element fits framebuffer: " + id);
        }
        void PromptAboveActions(string prompt, string[] actions)
        {
            var text = Box(pages.Top, prompt); Contained(pages.Top, prompt);
            foreach (string action in actions)
            {
                Reveal(pages.Top, action); var box = Box(pages.Top, action);
                Check(box.Y >= text.Bottom - 1, "prompt does not overlap action: " + action);
                Check(box.W >= 100 * float.Parse(density, System.Globalization.CultureInfo.InvariantCulture), "action has a readable label width: " + action);
                Contained(pages.Top, action);
            }
        }
        void Reveal(RmlUiDocumentToken document, string id)
        {
            Check(host.FocusDocument(document, id), "modal control remains focusable: " + id); Settle();
        }
        void BackgroundBlocked()
        {
            var background = Box(page.Document, "native_settings_category_1"); host.DiscardIntents(); Click(background);
            Check(!host.TryTakeIntent(out _), "native backdrop blocks actual pointer activation of underlying settings tabs");
            for (int i = 0; i < 8; i++) { host.Input.Key(1, true); host.Input.Key(1, false); host.Update(); }
            Check(host.CurrentInputDocument == pages.Top && host.FocusedElement().StartsWith("native_settings_capture_", StringComparison.Ordinal), "native Tab traversal stays inside capture modal");
        }
        void Click(Rect box)
        {
            float x = box.X + box.W / 2, y = box.Y + box.H / 2;
            host.Input.PointerMoved(x, y); host.Input.PointerButton(0, x, y, true); host.Input.PointerButton(0, x, y, false); host.Update();
        }
        void Capture(string name)
        {
            Frame(); string path = Path.Combine(output, name + ".png");
            Check(ScreenCapture.SaveWindow(width, height, path), "capture " + name);
            File.WriteAllText(path + ".semantic.json", NativeSemanticReadback.Read(host, pages.Top).RawJson);
        }
        void Check(bool passed, string name)
        {
            if (!passed) { try { CaptureFailure(); } catch { } throw new InvalidOperationException("SETTINGS POPUP GL: " + name); }
            checks++; Console.WriteLine("SETTINGS POPUP GL PASS " + name);
        }
        void CaptureFailure()
        {
            ScreenCapture.SaveWindow(width, height, Path.Combine(output, "failure.png"));
            File.WriteAllText(Path.Combine(output, "failure.semantic.json"), NativeSemanticReadback.Read(host, pages.Top).RawJson);
            File.WriteAllText(Path.Combine(output, "failure.geometry.json"), JsonSerializer.Serialize(geometry, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private readonly record struct Rect(float X, float Y, float W, float H)
    { internal float Right => X + W; internal float Bottom => Y + H; }
}
