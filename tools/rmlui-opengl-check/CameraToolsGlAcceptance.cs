using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

/// <summary>Actual native controls; all camera files, playback, source ownership and publication results are synthetic.</summary>
internal static class CameraToolsGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        Check(!DemoPlayback.IsActive, "camera tools begin without a replay session or world");
        var memory = new MemoryCamera();
        using var transport = new TheatrePlaybackController(new NoStudio());
        using var pages = new RmlUiPageManager(host);
        using var page = new TheatrePlaybackPagePresenter(host, pages, transport, camera: memory);
        page.Open(); Frame(); var playback = page.Document;
        Check(page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.CameraEditor, playback, 1)),
            "synthetic camera owner opens retained native basic panel");
        Frame(); var basic = pages.Top;
        Click("replay_camera_tools_open", TheatrePlaybackAction.CameraToolsOpen);
        var panel = pages.Top;
        Check(panel != basic && host.FocusedElement() == "replay_camera_tools_refresh", "optional tools owns a foreground native document");
        Click("replay_camera_tools_next", TheatrePlaybackAction.CameraToolsNext);
        Click("replay_camera_tools_toggle", TheatrePlaybackAction.CameraToolsToggleSelection);
        Click("replay_camera_tools_next", TheatrePlaybackAction.CameraToolsNext);
        Click("replay_camera_tools_range", TheatrePlaybackAction.CameraToolsRangeSelection);
        Click("replay_camera_tools_all", TheatrePlaybackAction.CameraToolsSelectAll);
        Click("replay_camera_tools_copy", TheatrePlaybackAction.CameraToolsCopy);
        EditDestination("200");
        Click("replay_camera_tools_move", TheatrePlaybackAction.CameraToolsMove); var confirm = pages.Top;
        Check(confirm != panel && memory.Writes == 0, "native group move requires exact full-track confirmation");
        Check(!page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.CameraToolsConfirm, panel, ulong.MaxValue)),
            "background tools document cannot confirm the reviewed group move");
        Capture("group-review"); memory.Fail = true;
        Click("replay_camera_tools_confirm_apply", TheatrePlaybackAction.CameraToolsConfirm);
        Check(pages.Top == confirm && memory.Writes == 0, "sharing failure preserves exact native confirmation and group draft");
        memory.Fail = false; Click("replay_camera_tools_confirm_apply", TheatrePlaybackAction.CameraToolsConfirm);
        Check(pages.Top == panel && memory.Context.Camera.Keys.Select(key => key.Frame).SequenceEqual(new uint[] { 200, 210, 230 }),
            "same reviewed group retries without changing spacing or adding a clock");
        Check(!host.FocusDocument(panel, "replay_camera_tools_remove"), "saved group cannot authorize another edit before explicit refresh");
        Check(!page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.CameraToolsConfirm, confirm, ulong.MaxValue)),
            "retired confirmation cannot repeat a camera write");
        Click("replay_camera_tools_refresh", TheatrePlaybackAction.CameraToolsRefresh);
        Click("replay_camera_tools_all", TheatrePlaybackAction.CameraToolsSelectAll);
        Click("replay_camera_tools_copy", TheatrePlaybackAction.CameraToolsCopy);
        Click("replay_camera_tools_clear", TheatrePlaybackAction.CameraToolsClearSelection);
        EditDestination("300"); Click("replay_camera_tools_paste", TheatrePlaybackAction.CameraToolsPaste);
        Check(pages.Top != panel && memory.Writes == 1, "native clipboard can stage group paste with no current selection");
        Click("replay_camera_tools_confirm_cancel", TheatrePlaybackAction.CameraToolsCancel);
        Check(memory.Context.Camera.Keys.Length == 3, "cancelled paste preserves the original retained keys");
        Click("replay_camera_tools_nearest", TheatrePlaybackAction.CameraToolsNearest);
        foreach (var command in new[]
        {
            ("snap_event", TheatrePlaybackAction.CameraToolsSnapEvent),
            ("snap_kill", TheatrePlaybackAction.CameraToolsSnapKill),
            ("snap_bookmark", TheatrePlaybackAction.CameraToolsSnapBookmark),
            ("interpolation", TheatrePlaybackAction.CameraToolsInterpolation),
            ("ease", TheatrePlaybackAction.CameraToolsEase),
            ("look_at", TheatrePlaybackAction.CameraToolsLookAt)
        })
        {
            Click("replay_camera_tools_" + command.Item1, command.Item2);
            Check(pages.Top != panel, "native reviewed change opens exact confirmation: " + command.Item1);
            Click("replay_camera_tools_confirm_cancel", TheatrePlaybackAction.CameraToolsCancel);
        }
        Click("replay_camera_tools_duplicate", TheatrePlaybackAction.CameraToolsDuplicate);
        // No preceding key exists at this reviewed playhead after the move; the bounded command reports an error.
        Check(pages.Top == panel && memory.Writes == 1, "invalid preceding-key duplication cannot create a fabricated pose");
        foreach (var command in new[]
        {
            ("speed", TheatrePlaybackAction.CameraToolsConstantSpeed),
            ("collision", TheatrePlaybackAction.CameraToolsCollision),
            ("director", TheatrePlaybackAction.CameraToolsDirector)
        })
        {
            Click("replay_camera_tools_" + command.Item1, command.Item2);
            Check(!host.FocusDocument(panel, "replay_camera_tools_remove"), "runtime motion requires newer picture and explicit review: " + command.Item1);
            Click("replay_camera_tools_refresh", TheatrePlaybackAction.CameraToolsRefresh);
        }
        Check(memory.Context.Motion is { ConstantSpeed: true, Collision: false, Director: true }, "native options use the same injected runtime camera owner");
        Click("replay_camera_tools_preview", TheatrePlaybackAction.CameraToolsPreview);
        Check(memory.Previews == 1 && !host.FocusDocument(panel, "replay_camera_tools_move"), "native preview delegates existing seek and retires old picture authority");
        Click("replay_camera_tools_refresh", TheatrePlaybackAction.CameraToolsRefresh);
        Click("replay_camera_tools_all", TheatrePlaybackAction.CameraToolsSelectAll);
        Click("replay_camera_tools_remove", TheatrePlaybackAction.CameraToolsRemoveSelected); confirm = pages.Top;
        memory.Current = false; Frame();
        Check(!host.FocusDocument(confirm, "replay_camera_tools_confirm_apply"), "changed live owner disables reviewed batch deletion");
        Capture("stale-owner");
        Click("replay_camera_tools_confirm_cancel", TheatrePlaybackAction.CameraToolsCancel);
        Check(memory.Context.Camera.Keys.Length == 3, "stale cancelled removal cannot affect replacement ownership");
        memory.Current = true; Click("replay_camera_tools_refresh", TheatrePlaybackAction.CameraToolsRefresh);
        Click("replay_camera_tools_all", TheatrePlaybackAction.CameraToolsSelectAll);
        Click("replay_camera_tools_remove", TheatrePlaybackAction.CameraToolsRemoveSelected);
        memory.Unverified = true; Click("replay_camera_tools_confirm_apply", TheatrePlaybackAction.CameraToolsConfirm);
        Check(pages.Top == panel && !host.FocusDocument(panel, "replay_camera_tools_remove") && memory.Writes == 2,
            "unverified publication receipt closes old confirmation and blocks blind repeat");
        Capture("unverified-publication");
        Click("replay_camera_tools_close", TheatrePlaybackAction.CameraToolsClose);
        Check(pages.Top == basic && !host.IsAlive(panel), "tools close returns to original basic camera and retires clipboard ownership");
        Click("replay_camera_close", TheatrePlaybackAction.CameraClose);
        Check(pages.Top == playback && !page.Back(), "tools leave the same native playback page and no modal lease behind");
        page.Dispose(); Check(!DemoPlayback.IsActive && GL.GetError() == ErrorCode.NoError, "tools end with no real recording, scene, encoder or GL errors");

        void Frame()
        {
            memory.Publish(); page.Present(); host.Update();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.2f, .3f, .4f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle() { var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(10); } while (timer.ElapsedMilliseconds < 200); }
        void EditDestination(string value)
        {
            Check(host.FocusDocument(panel, "replay_camera_tools_destination"), "native destination text field is editable");
            host.SetField(panel, "replay_camera_tools_destination", value); Frame();
            Check(host.ReadField(panel, "replay_camera_tools_destination") == value, "native destination preserves exact staged frame");
        }
        void Click(string id, TheatrePlaybackAction expected)
        {
            Frame(); var target = pages.Top;
            Check(host.FocusDocument(target, id), "actual tools control focus: " + id); Settle();
            Check(host.TryGetElementBounds(target, id, out float x, out float y, out float w, out float h)
                && w > 0 && h > 0 && x + w / 2 >= 0 && x + w / 2 < width && y + h / 2 >= 0 && y + h / 2 < height,
                "reachable tools geometry: " + id);
            host.DiscardIntents(); host.Input.PointerButton(0, x + w / 2, y + h / 2, true);
            host.Input.PointerButton(0, x + w / 2, y + h / 2, false); host.Update(); bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                bool accepted = page.HandleIntent(intent);
                if (intent.Kind != RmlUiIntentKind.ReplayAction || intent.Argument != (int)expected) continue;
                Check(accepted && intent.Document == target, "actual bounded native tools action: " + id);
                Check(!page.HandleIntent(intent), "duplicate native tools sequence refused: " + id); received = true;
            }
            Check(received, "expected native tools intent emitted: " + id); Frame();
        }
        void Capture(string name)
        {
            Settle(); var pixels = FinalCompositeCapture.Read(width, height);
            string directory = Path.GetFullPath(Path.Combine("artifacts", "rmlui-camera-tools-rml61")); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"synthetic-{name}-{width}x{height}.ppm");
            using var file = File.Create(path);
            file.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC CAMERA TOOLS - NO REPLAY FILES OR WORLD\n{width} {height}\n255\n"));
            for (int row = height - 1; row >= 0; row--) file.Write(pixels, row * width * 3, width * 3);
            FinalCompositeCapture.WriteEvidence(path, width, height, "Actual native camera tools with injected memory keys and synthetic write/recovery receipts; no world playback or actual file publication.");
        }
    }
    private static void Check(bool value, string text)
    { if (!value) throw new InvalidOperationException(text); Console.WriteLine("RML CAMERA TOOLS GL PASS " + text); }
    private sealed class NoStudio : ITheatrePlaybackBackend
    { public bool OpenStudio(string path, out string? error) => throw new InvalidOperationException("No legacy Studio launch permitted."); }
    private sealed class MemoryCamera : ITheatreCameraBackend, ITheatreCameraToolsBackend
    {
        private static readonly TheatreCameraPose Pose = new(1, 2, 3, 0, 0, 0, 1, 80, 0, -1, 0, 0);
        internal TheatreCameraToolsContext Context = new(new(1, new object(), new object(), 0, "SYNTHETIC.ppdemo", "SYNTHETIC.ppdemo",
            "SYNTHETIC-SOURCE", "CAMERA", 50, 600, true, Pose,
            ImmutableArray.Create(new TheatreCameraKey(10, Pose), new TheatreCameraKey(20, Pose), new TheatreCameraKey(40, Pose)), false)
            { PresentationRevision = 1 }, new object(), "SOURCE", "ANNOTATIONS", ImmutableArray.Create(
                new TheatreCameraMarker("event", 45, TheatreCameraMarkerKind.Event), new TheatreCameraMarker("kill", 55, TheatreCameraMarkerKind.Kill),
                new TheatreCameraMarker("bookmark", 35, TheatreCameraMarkerKind.Bookmark)), ImmutableArray.Create(1, 3), new(false, true, false), new object());
        internal bool Current = true, Fail, Unverified;
        internal int Writes, Previews;
        internal void Publish() => Context = Context with { Camera = Context.Camera with { PresentationRevision = Context.Camera.PresentationRevision + 1 } };
        public TheatreCameraContext Capture() => Context.Camera;
        public bool IsCurrent(TheatreCameraContext context) => Current && context.Keys.SequenceEqual(Context.Camera.Keys)
            && context.Transport == Context.Camera.Transport && context.SeekGeneration == Context.Camera.SeekGeneration;
        public void SetFreeCamera(TheatreCameraContext context) => throw new InvalidOperationException("Tools fixture does not move real cameras.");
        public void AdjustLens(TheatreCameraContext context, float fov, float roll) => throw new InvalidOperationException("Tools fixture does not change real lens settings.");
        public void Preview(TheatreCameraContext context, TheatreCameraKey key) => throw new InvalidOperationException("Use synthetic tools boundary.");
        public void ToggleTrack(TheatreCameraContext context) => throw new InvalidOperationException("Tools fixture does not own real track playback.");
        public void Commit(TheatreCameraEdit edit) => throw new InvalidOperationException("Tools fixture forbids basic persistence.");
        public TheatreCameraToolsContext CaptureTools() => Context;
        public void ValidateReviewedTools(TheatreCameraToolsContext context) { if (!ToolsCurrent(context)) throw new TheatreCameraStaleException("Original reviewed camera owner changed."); }
        public bool ToolsCurrent(TheatreCameraToolsContext context) => Current && context.Owner == Context.Owner
            && context.SourceRevision == Context.SourceRevision && IsCurrent(context.Camera) && context.Motion == Context.Motion;
        public TheatreCameraToolsResult CommitTools(TheatreCameraToolsEdit edit)
        {
            if (!ToolsCurrent(edit.Context)) throw new TheatreCameraStaleException("Synthetic original camera owner changed.");
            if (Fail) throw new IOException("Synthetic camera sidecar is busy. Retry the same reviewed group.");
            Context = Context with { Camera = Context.Camera with { Keys = edit.After } }; Writes++;
            return new(!Unverified, Unverified ? "Synthetic publication is unverified; the raced bytes are preserved in recovery. Inspect and refresh before editing." : "");
        }
        public void PreviewTools(TheatreCameraToolsContext context, TheatreCameraKey focused)
        { Previews++; Context = Context with { Camera = Context.Camera with { Frame = focused.Frame, SeekGeneration = Context.Camera.SeekGeneration + 1 } }; }
        public void ChangeMotion(TheatreCameraToolsContext context, TheatreCameraMotionChange change)
        {
            Context = Context with { Motion = change switch
            {
                TheatreCameraMotionChange.ConstantSpeed => Context.Motion with { ConstantSpeed = !Context.Motion.ConstantSpeed },
                TheatreCameraMotionChange.Collision => Context.Motion with { Collision = !Context.Motion.Collision },
                _ => Context.Motion with { Director = !Context.Motion.Director }
            } };
        }
    }
}
