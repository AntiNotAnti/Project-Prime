using System;
using System.Globalization;
using System.IO;
using System.Text;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;

internal static class PlaybackAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Path.Combine("artifacts", "rmlui-playback-rml48"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "README.txt"),
            "Actual native RmlUi OpenGL playback page with the production transport controller INACTIVE and an injected Studio boundary. No recording, replay world, scene, live runtime routing or account is loaded. Viewport controller focus/release is exercised explicitly; the production RenderWindow input adapter is not exercised.\n");
        Check(!DemoPlayback.IsActive, "fixture starts without an active recording or replay world");
        var backend = new StudioBoundary();
        using var controller = new TheatrePlaybackController(backend);
        using var pages = new RmlUiPageManager(host);
        using var page = new TheatrePlaybackPagePresenter(host, pages, controller);
        host.Input.SetFramebufferScale(1, 1);
        page.Open();
        var document = page.Document;
        Frame();
        Check(page.IsOpen && pages.Top == document, "actual playback document owns the foreground");
        var state = controller.Snapshot();
        Check(!state.Active && state.Frame == 0 && state.Duration == 0 && state.State == "Inactive",
            "production inactive snapshot supplies zero transport time");
        Bounds("replay_time");
        Bounds("replay_inactive");
        Check(double.TryParse(host.ReadField(document, "replay_position"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double position) && double.IsFinite(position) && position == 0,
            "actual native seek field displays zero");
        foreach (string id in new[] { "pause", "jump_back", "jump_forward", "restart", "step", "rate",
            "camera", "previous_player", "next_player", "position", "studio", "fullscreen" })
            Check(!host.FocusDocument(document, "replay_" + id), "inactive native control cannot focus: " + id);
        Check(backend.Calls == 0, "inactive page does not launch Studio");
        foreach (string id in new[] { "replay_bookmarks", "replay_camera_editor", "replay_export_open",
            "replay_range_controls", "replay_range_summary", "replay_mark_in", "replay_mark_out", "replay_ranges" })
            Check(!host.TryGetElementBounds(document, id, out _, out _, out _, out _),
                "viewer omits unavailable advanced control instead of leaving disabled geometry: " + id);
        ulong capabilitySequence = 1000;
        foreach (TheatrePlaybackAction action in Enum.GetValues<TheatrePlaybackAction>())
            if (action >= TheatrePlaybackAction.Bookmarks)
                Check(!page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)action, document, capabilitySequence++))
                    && pages.Top == document,
                    "typed advanced action cannot bypass missing viewer capability: " + action);
        Check(!page.TryOpenEditor() && pages.Top == document,
            "direct editor-open request cannot bypass absent editor capability");

        var box = Bounds("replay_viewport");
        Check(box.W > width / 2 && box.H > 20 && box.X >= 0 && box.Y >= 0
            && box.X + box.W <= width && box.Y + box.H <= height,
            "actual native viewport has bounded positive geometry");
        double px = box.X + box.W / 4, py = box.Y + box.H / 4;
        host.Input.PointerButton(0, px, py, true);
        host.Input.PointerButton(0, px, py, false);
        host.Update();
        Check(host.FocusedElement() == "replay_viewport", "actual pointer focuses the replay viewport");
        page.Viewport.Focus();
        Check(page.Viewport.KeyDown(Keys.W), "explicit focused viewport controller owns movement input");
        Check(page.Viewport.KeyDown(Keys.Escape) && !page.Viewport.Focused,
            "viewport controller Escape releases movement ownership");
        Check(pages.Top == document && !page.TryOpenEditor(),
            "viewport movement and release never open an unavailable replay editor");
        Check(host.FocusDocument(document, "replay_viewport"), "native viewport accepts keyboard focus");
        host.Input.Key(1, true); host.Input.Key(1, false); host.Update();
        Check(host.FocusedElement() == "replay_back", "native Tab skips disabled transport controls to Library");
        host.DiscardIntents();
        host.Input.Key(2, true); host.Input.Key(2, false); host.Update();
        ConsumeBack("keyboard");
        Check(page.TryTakeEngineCommand(out var back) && back == TheatrePlaybackAction.Back
            && !page.TryTakeEngineCommand(out _), "Library keyboard action transfers one unapplied shell command");

        Check(host.FocusDocument(document, "replay_back"), "Library remains focusable while inactive");
        Frame();
        var backBox = Bounds("replay_back");
        host.DiscardIntents();
        host.Input.PointerButton(0, backBox.X + backBox.W / 2, backBox.Y + backBox.H / 2, true);
        host.Input.PointerButton(0, backBox.X + backBox.W / 2, backBox.Y + backBox.H / 2, false);
        host.Update();
        ConsumeBack("pointer");
        Check(page.TryTakeEngineCommand(out back) && back == TheatrePlaybackAction.Back,
            "Library pointer action transfers an unapplied shell command");
        Check(controller.Dispatch(TheatrePlaybackAction.Fullscreen)
            && page.TryTakeEngineCommand(out var fullscreen) && fullscreen == TheatrePlaybackAction.Fullscreen,
            "explicit Fullscreen command stays with the shell while inactive UI disables its button");

        page.Viewport.Release();
        host.Input.PointerMoved(0, 0);
        Frame();
        var pixels = FinalCompositeCapture.Read(width, height);
        int transparent = 0;
        // Sample the upper viewport interior, away from its border and inactive message.
        for (int row = (int)box.Y + 6; row < (int)(box.Y + box.H / 3); row += 5)
            for (int col = (int)box.X + 6; col < (int)(box.X + box.W) - 6; col += 5)
            {
                int pixel = ((height - row - 1) * width + col) * 3;
                if (Math.Abs(pixels[pixel] - 51) <= 2 && Math.Abs(pixels[pixel + 1] - 77) <= 2
                    && Math.Abs(pixels[pixel + 2] - 102) <= 2) transparent++;
            }
        Check(transparent > 20, "transparent native viewport preserves actual pre-rendered clear pixels");
        string path = Path.Combine(output, $"inactive-playback-{width}x{height}.ppm");
        using (var file = File.Create(path))
        {
            file.Write(Encoding.ASCII.GetBytes($"P6\n# INACTIVE PRODUCTION PLAYBACK - NO WORLD OR RECORDING\n{width} {height}\n255\n"));
            for (int row = height - 1; row >= 0; row--) file.Write(pixels, row * width * 3, width * 3);
        }
        FinalCompositeCapture.WriteEvidence(path, width, height,
            "Actual native GL inactive playback document; no recording, scene or production RenderWindow routing acceptance.");
        page.Dispose();
        Check(!page.IsOpen && !host.IsAlive(document)
            && !page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.Back, document, ulong.MaxValue)),
            "retired playback document rejects late shell commands");
        Check(!DemoPlayback.IsActive && backend.Calls == 0, "fixture leaves playback inactive and Studio untouched");
        Check(GL.GetError() == ErrorCode.NoError, "native playback fixture leaves no OpenGL error");
        Console.WriteLine("RML PLAYBACK GL inactive-only screenshot: " + path);
        BookmarkGlAcceptance.Run(host, width, height);
        RangeGlAcceptance.Run(host, width, height);
        CameraGlAcceptance.Run(host, width, height);
        CameraToolsGlAcceptance.Run(host, width, height);
        ExportGlAcceptance.Run(host, width, height);
        EditorGlAcceptance.Run(host, width, height);

        void Frame()
        {
            page.Present(); host.Update();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest);
            GL.ClearColor(.2f, .3f, .4f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        (float X, float Y, float W, float H) Bounds(string id)
        {
            Check(host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h)
                && w > 0 && h > 0, "actual native geometry: " + id);
            return (x, y, w, h);
        }
        void ConsumeBack(string source)
        {
            bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                bool accepted = page.HandleIntent(intent);
                if (intent.Kind != RmlUiIntentKind.ReplayAction || intent.Argument != (int)TheatrePlaybackAction.Back) continue;
                Check(accepted && intent.Document == document, "actual " + source + " Library intent reaches playback presenter");
                Check(!page.HandleIntent(intent), "duplicate " + source + " Library sequence is rejected");
                received = true;
            }
            Check(received, "native " + source + " Library action is emitted");
        }
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("RML PLAYBACK GL PASS " + message);
    }
    private sealed class StudioBoundary : ITheatrePlaybackBackend
    {
        internal int Calls;
        public bool OpenStudio(string path, out string? error)
        {
            Calls++; error = "Inactive fixture must never launch Studio."; return false;
        }
    }
}
