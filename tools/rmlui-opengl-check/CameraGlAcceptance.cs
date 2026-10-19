using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
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

internal static class CameraGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        Check(!DemoPlayback.IsActive, "camera fixture begins without a replay, world or session");
        var memory = new MemoryCamera();
        using var transport = new TheatrePlaybackController(new NoStudio());
        using var pages = new RmlUiPageManager(host);
        using var page = new TheatrePlaybackPagePresenter(host, pages, transport, camera: memory);
        page.Open(); Frame(); var playback = page.Document;
        Check(!host.FocusDocument(playback, "replay_camera_editor"), "production inactive toolbar disables camera editing");
        Check(page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.CameraEditor, playback, 1)),
            "synthetic injected editor opens actual RmlUi camera panel");
        Frame(); var panel = pages.Top;
        Check(panel != playback && host.FocusedElement() == "replay_camera_refresh", "camera panel owns foreground focus");
        Check(!host.FocusDocument(panel, "replay_camera_tools_open"), "basic camera backend without optional tools disables native tools entry");
        Check(!host.FocusDocument(panel, "replay_camera_remove") && !host.FocusDocument(panel, "replay_camera_preview"),
            "empty synthetic track disables destructive and jump controls");
        Click("replay_camera_free", TheatrePlaybackAction.CameraFree);
        Click("replay_camera_refresh", TheatrePlaybackAction.CameraRefresh);
        Click("replay_camera_fov_more", TheatrePlaybackAction.CameraFovMore);
        Click("replay_camera_refresh", TheatrePlaybackAction.CameraRefresh);
        Click("replay_camera_roll_less", TheatrePlaybackAction.CameraRollLess);
        Click("replay_camera_refresh", TheatrePlaybackAction.CameraRefresh);
        Check(memory.FreeCalls == 1 && memory.Context.Pose is { Fov: 85, Roll: -5 }, "native lens and mode controls edit existing injected camera owner");
        Click("replay_camera_add", TheatrePlaybackAction.CameraAdd);
        Check(memory.Writes == 1 && memory.Context.Keys.Length == 1 && memory.Context.Keys[0].Frame == 60,
            "native Add persists exact reviewed frame through fake writer");
        Click("replay_camera_add", TheatrePlaybackAction.CameraAdd); var confirm = pages.Top;
        Check(confirm != panel && memory.Writes == 1, "occupied-frame Add opens exact replacement confirmation");
        Check(!page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.CameraConfirm, panel, ulong.MaxValue)),
            "background camera panel cannot confirm a destructive action");
        Capture("replace-confirmation");
        Click("replay_camera_confirm_cancel", TheatrePlaybackAction.CameraCancelConfirm);
        Check(!host.IsAlive(confirm) && pages.Top == panel && memory.Writes == 1, "cancel preserves saved key and retires exact confirmation");
        Click("replay_camera_fov_more", TheatrePlaybackAction.CameraFovMore);
        Click("replay_camera_refresh", TheatrePlaybackAction.CameraRefresh);
        Click("replay_camera_update", TheatrePlaybackAction.CameraUpdate); confirm = pages.Top;
        memory.FailWrite = true;
        Click("replay_camera_confirm_apply", TheatrePlaybackAction.CameraConfirm);
        Check(pages.Top == confirm && memory.Writes == 1 && memory.Context.Keys[0].Pose.Fov == 85,
            "failed native replacement retains confirmation and original persisted pose");
        memory.FailWrite = false;
        Click("replay_camera_confirm_apply", TheatrePlaybackAction.CameraConfirm);
        Check(pages.Top == panel && memory.Writes == 2 && memory.Context.Keys[0].Pose.Fov == 90,
            "retry commits the same captured replacement once");
        Check(!page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.CameraConfirm, confirm, ulong.MaxValue)),
            "retired replacement document cannot repeat a write");
        Click("replay_camera_remove", TheatrePlaybackAction.CameraRemove); confirm = pages.Top;
        memory.Revision = "EXTERNAL-SYNTHETIC-CAMERA-REVISION";
        Click("replay_camera_confirm_apply", TheatrePlaybackAction.CameraConfirm);
        Check(pages.Top == confirm && memory.Writes == 2 && !host.FocusDocument(confirm, "replay_camera_confirm_apply"),
            "external camera revision refuses removal and clears native confirmation authority");
        Click("replay_camera_confirm_cancel", TheatrePlaybackAction.CameraCancelConfirm);
        Click("replay_camera_refresh", TheatrePlaybackAction.CameraRefresh);
        Click("replay_camera_preview", TheatrePlaybackAction.CameraPreview);
        Check(memory.Previews == 1 && !host.FocusDocument(panel, "replay_camera_add"),
            "jump uses fake existing seek owner and invalidates reviewed pose");
        memory.Context = memory.Context with { Transport = new object(), PresentationScene = new object() };
        Click("replay_camera_refresh", TheatrePlaybackAction.CameraRefresh);
        Click("replay_camera_track", TheatrePlaybackAction.CameraTrack);
        Click("replay_camera_refresh", TheatrePlaybackAction.CameraRefresh);
        Check(memory.Context.TrackPlaying, "native track preview toggles existing owner without creating another clock");
        Click("replay_camera_remove", TheatrePlaybackAction.CameraRemove);
        Capture("remove-confirmation");
        Click("replay_camera_confirm_apply", TheatrePlaybackAction.CameraConfirm);
        Check(memory.Writes == 3 && memory.Context.Keys.IsEmpty && pages.Top == panel, "fresh exact removal deletes only selected fake key");
        Click("replay_camera_previous", TheatrePlaybackAction.CameraPrevious, disabled: true);
        memory.Context = memory.Context with { SceneGeneration = 2 }; Frame();
        Check(!host.FocusDocument(panel, "replay_camera_add"), "changed scene owner disables camera persistence");
        Click("replay_camera_close", TheatrePlaybackAction.CameraClose);
        Check(pages.Top == playback && !host.IsAlive(panel) && !page.Back(), "camera close returns to same playback page and retires controller");
        page.Dispose();
        Check(!DemoPlayback.IsActive && GL.GetError() == ErrorCode.NoError, "camera fixture ends without real recording, playback or GL errors");

        void Frame()
        {
            memory.Present(); // Simulated picture revision belongs to the injected fake, never the production scene cache.
            page.Present(); host.Update();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.2f, .3f, .4f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle()
        { var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(10); } while (timer.ElapsedMilliseconds < 200); }
        void Click(string id, TheatrePlaybackAction expected, bool disabled = false)
        {
            Frame(); var target = pages.Top;
            if (disabled) { Check(!host.FocusDocument(target, id), "native disabled camera action: " + id); return; }
            Check(host.FocusDocument(target, id), "actual native camera focus: " + id); Settle();
            Check(host.TryGetElementBounds(target, id, out float x, out float y, out float w, out float h)
                && w > 0 && h > 0 && x + w / 2 >= 0 && x + w / 2 < width
                && y + h / 2 >= 0 && y + h / 2 < height, "reachable native camera geometry: " + id);
            host.DiscardIntents();
            host.Input.PointerButton(0, x + w / 2, y + h / 2, true);
            host.Input.PointerButton(0, x + w / 2, y + h / 2, false); host.Update();
            bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                bool accepted = page.HandleIntent(intent);
                if (intent.Kind != RmlUiIntentKind.ReplayAction || intent.Argument != (int)expected) continue;
                Check(accepted && intent.Document == target, "actual native camera action: " + id);
                Check(!page.HandleIntent(intent), "duplicate native camera sequence refused: " + id); received = true;
            }
            Check(received, "expected camera intent emitted: " + id); Frame();
        }
        void Capture(string name)
        {
            Settle(); var pixels = FinalCompositeCapture.Read(width, height);
            string directory = Path.GetFullPath(Path.Combine("artifacts", "rmlui-camera-rml54")); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"synthetic-{name}-{width}x{height}.ppm");
            using var file = File.Create(path);
            file.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC MEMORY CAMERA TRACK - NO REPLAY FILES OR WORLD\n{width} {height}\n255\n"));
            for (int row = height - 1; row >= 0; row--) file.Write(pixels, row * width * 3, width * 3);
            FinalCompositeCapture.WriteEvidence(path, width, height, "Actual native camera editor and exact confirmation; injected in-memory pose/track only, no replay scene acceptance.");
        }
    }
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); Console.WriteLine("RML CAMERA GL PASS " + message); }
    private sealed class NoStudio : ITheatrePlaybackBackend
    { public bool OpenStudio(string path, out string? error) => throw new InvalidOperationException("No actual Studio launch permitted."); }
    private sealed class MemoryCamera : ITheatreCameraBackend
    {
        private static readonly TheatreCameraPose Pose = new(1, 2, 3, 0, 0, 0, 1, 80, 0, -1, 2, 3);
        internal TheatreCameraContext Context = new(1, new object(), new object(), 1, "SYNTHETIC.ppdemo", "SYNTHETIC.ppdemo",
            "SYNTHETIC-RECORDING", "0", 60, 600, true, Pose, ImmutableArray<TheatreCameraKey>.Empty, false) { PresentationRevision = 1 };
        internal string Revision = "0";
        internal int Writes, FreeCalls, Previews;
        internal bool FailWrite;
        public TheatreCameraContext Capture() { Context = Context with { CameraRevision = Revision }; return Context; }
        public void Present() => Context = Context with { PresentationRevision = Context.PresentationRevision + 1 };
        public bool IsCurrent(TheatreCameraContext c) => Context.SettledPaused && Context.SceneGeneration == c.SceneGeneration
            && Context.Frame == c.Frame && Context.SeekGeneration == c.SeekGeneration && Context.Pose == c.Pose
            && ReferenceEquals(Context.Transport, c.Transport) && ReferenceEquals(Context.PresentationScene, c.PresentationScene)
            && Context.TrackPlaying == c.TrackPlaying && Context.Keys.SequenceEqual(c.Keys);
        private void Validate(TheatreCameraContext c)
        { if (!IsCurrent(c) || c.CameraRevision != Revision) throw new TheatreCameraStaleException("Synthetic camera sidecar changed. Cancel and refresh."); }
        public void SetFreeCamera(TheatreCameraContext c) { Validate(c); FreeCalls++; }
        public void AdjustLens(TheatreCameraContext c, float fov, float roll)
        { Validate(c); Context = Context with { Pose = Context.Pose with { Fov = Math.Clamp(Context.Pose.Fov + fov, 20, 140), Roll = Math.Clamp(Context.Pose.Roll + roll, -180, 180) } }; }
        public void Preview(TheatreCameraContext c, TheatreCameraKey key)
        { Validate(c); Previews++; Context = Context with { SeekGeneration = c.SeekGeneration + 1, Frame = key.Frame, Pose = key.Pose }; }
        public void ToggleTrack(TheatreCameraContext c) { Validate(c); Context = Context with { TrackPlaying = !Context.TrackPlaying }; }
        public void Commit(TheatreCameraEdit exact)
        {
            Validate(exact.Context); if (FailWrite) throw new IOException("Synthetic locked camera track.");
            var keys = Context.Keys.Where(key => key.Frame != exact.Key.Frame).ToList();
            if (exact.Change != TheatreCameraChange.Remove) keys.Add(exact.Key);
            Writes++; Revision = Guid.NewGuid().ToString();
            Context = Context with { Keys = keys.OrderBy(key => key.Frame).ToImmutableArray(), CameraRevision = Revision };
        }
    }
}
