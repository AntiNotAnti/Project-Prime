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

internal static class ExportGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        Check(!DemoPlayback.IsActive, "export fixture starts without any replay world or encoder");
        var memory = new MemoryExports();
        using var transport = new TheatrePlaybackController(new NoStudio());
        using var pages = new RmlUiPageManager(host);
        using var page = new TheatrePlaybackPagePresenter(host, pages, transport, exports: memory);
        page.Open(); Frame(); var playback = page.Document;
        Check(!host.FocusDocument(playback, "replay_export_open"), "inactive production playback disables export entry");
        Check(page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.ExportOpen, playback, 1)),
            "injected memory export owner opens actual native export panel");
        Frame(); var panel = pages.Top;
        Check(panel != playback && host.FocusedElement() == "replay_export_refresh", "native export modal owns foreground focus");
        Click("replay_export_preset", TheatrePlaybackAction.ExportPreset);
        Click("replay_export_resolution", TheatrePlaybackAction.ExportResolution);
        Click("replay_export_fps", TheatrePlaybackAction.ExportFps);
        Click("replay_export_game_hud", TheatrePlaybackAction.ExportGameHud);
        Click("replay_export_replay_overlay", TheatrePlaybackAction.ExportReplayOverlay);
        Click("replay_export_encoder", TheatrePlaybackAction.ExportEncoder);
        Click("replay_export_source", TheatrePlaybackAction.ExportSource);
        Click("replay_export_camera", TheatrePlaybackAction.ExportCamera);
        memory.FailWrite = true;
        Click("replay_export_queue", TheatrePlaybackAction.ExportQueue);
        Check(memory.Accepted.Count == 0, "native manifest failure accepts no synthetic job");
        memory.FailWrite = false;
        Click("replay_export_queue", TheatrePlaybackAction.ExportQueue);
        Check(memory.Accepted.Single().Options is { Preset: 1, Resolution: 2, Fps: 30, GameHud: true, ReplayOverlay: true,
            Encoder: "h264_videotoolbox", Source: TheatreExportSource.FullReplay, Camera: TheatreExportCamera.Chase },
            "all native export options survive failure and reach fake queue unchanged on retry");
        Click("replay_export_source", TheatrePlaybackAction.ExportSource);
        Click("replay_export_queue", TheatrePlaybackAction.ExportQueue);
        Check(memory.Accepted.Count == 2 && memory.Accepted[1].Options.Source == TheatreExportSource.ExistingReel,
            "native existing-reel choice queues captured fake reel source");
        Click("replay_export_next", TheatrePlaybackAction.ExportNext);
        Click("replay_export_previous", TheatrePlaybackAction.ExportPrevious);
        Click("replay_export_clear_pending", TheatrePlaybackAction.ExportClearPending); var confirmation = pages.Top;
        Check(confirmation != panel && memory.Pending.Length == 2, "clear opens exact pending-set confirmation before mutation");
        Check(!page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.ExportConfirm, panel, ulong.MaxValue)),
            "background export panel cannot confirm destructive queue action");
        memory.AddPending(); Frame();
        Check(!host.FocusDocument(confirmation, "replay_export_confirm_apply") && memory.Pending.Length == 3,
            "newly admitted job disables stale pending-clear confirmation");
        Capture("stale-clear");
        Click("replay_export_confirm_cancel", TheatrePlaybackAction.ExportCancelConfirm);
        Click("replay_export_clear_pending", TheatrePlaybackAction.ExportClearPending);
        Click("replay_export_confirm_apply", TheatrePlaybackAction.ExportConfirm);
        Check(memory.Pending.IsEmpty && pages.Top == panel, "fresh exact native confirmation clears reviewed set only");
        memory.Active = memory.NewJob(); memory.Revision++;
        Click("replay_export_cancel_active", TheatrePlaybackAction.ExportCancelActive); confirmation = pages.Top;
        memory.CompletedOutput = "SYNTHETIC-ORIGINAL-COMPLETE.mp4"; memory.Active = null; memory.Revision++; Frame();
        Check(!host.FocusDocument(confirmation, "replay_export_confirm_apply") && memory.Cancellations == 0,
            "original completion before native Cancel cannot be overwritten by stale action");
        Click("replay_export_confirm_cancel", TheatrePlaybackAction.ExportCancelConfirm);
        memory.Active = memory.NewJob(); memory.Revision++;
        Click("replay_export_cancel_active", TheatrePlaybackAction.ExportCancelActive);
        Capture("cancel-active");
        Click("replay_export_confirm_apply", TheatrePlaybackAction.ExportConfirm);
        Check(memory.Cancellations == 1 && memory.Active == null, "fresh native Cancel targets exactly reviewed fake active job");
        memory.Retry = memory.NewJob(); memory.Revision++;
        Click("replay_export_retry", TheatrePlaybackAction.ExportRetry); confirmation = pages.Top;
        Click("replay_export_confirm_apply", TheatrePlaybackAction.ExportConfirm);
        Check(memory.Pending.Length == 1 && memory.Pending[0].Token == memory.Retry.Token,
            "native Retry keeps original manifest token rather than current draft options");
        Check(!page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.ExportConfirm, confirmation, ulong.MaxValue)),
            "retired confirmation cannot retry same job twice");
        Click("replay_export_open_output", TheatrePlaybackAction.ExportOpenOutput);
        Check(memory.OpenedOutput == "SYNTHETIC-ORIGINAL-COMPLETE.mp4", "native output action uses exact completed fake output only");
        memory.Current = false; Frame();
        Check(!host.FocusDocument(panel, "replay_export_queue") && memory.Pending.Length == 1,
            "changed replay authority blocks new exports while preserving accepted queued work");
        Click("replay_export_close", TheatrePlaybackAction.ExportClose);
        Check(pages.Top == playback && !host.IsAlive(panel) && memory.Pending.Length == 1 && !page.Back(),
            "closing native export UI leaves accepted queue job owned independently");
        page.Dispose();
        Check(!DemoPlayback.IsActive && GL.GetError() == ErrorCode.NoError,
            "export fixture ends without recordings, accounts, encoder processes or GL errors");

        void Frame()
        {
            page.Present(); host.Update();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.2f, .3f, .4f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle()
        { var timer = Stopwatch.StartNew(); do { Frame(); Thread.Sleep(10); } while (timer.ElapsedMilliseconds < 200); }
        void Click(string id, TheatrePlaybackAction expected)
        {
            Frame(); var target = pages.Top;
            Check(host.FocusDocument(target, id), "actual native export focus: " + id); Settle();
            Check(host.TryGetElementBounds(target, id, out float x, out float y, out float w, out float h)
                && w > 0 && h > 0 && x + w / 2 >= 0 && x + w / 2 < width
                && y + h / 2 >= 0 && y + h / 2 < height, "reachable native export geometry: " + id);
            host.DiscardIntents(); host.Input.PointerButton(0, x + w / 2, y + h / 2, true);
            host.Input.PointerButton(0, x + w / 2, y + h / 2, false); host.Update(); bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                bool accepted = page.HandleIntent(intent);
                if (intent.Kind != RmlUiIntentKind.ReplayAction || intent.Argument != (int)expected) continue;
                Check(accepted && intent.Document == target, "actual native export action: " + id);
                Check(!page.HandleIntent(intent), "duplicate native export sequence refused: " + id); received = true;
            }
            Check(received, "expected export intent emitted: " + id); Frame();
        }
        void Capture(string name)
        {
            Settle(); var pixels = FinalCompositeCapture.Read(width, height);
            string directory = Path.GetFullPath(Path.Combine("artifacts", "rmlui-export-rml58")); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"synthetic-{name}-{width}x{height}.ppm");
            using var file = File.Create(path);
            file.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC EXPORT QUEUE - NO REPLAY WORLD OR ENCODER\n{width} {height}\n255\n"));
            for (int row = height - 1; row >= 0; row--) file.Write(pixels, row * width * 3, width * 3);
            FinalCompositeCapture.WriteEvidence(path, width, height,
                "Actual native video export panel and exact queue confirmations with injected memory jobs; no real movie export or replay scene acceptance.");
        }
    }
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); Console.WriteLine("RML EXPORT GL PASS " + message); }
    private sealed class NoStudio : ITheatrePlaybackBackend
    { public bool OpenStudio(string path, out string? error) => throw new InvalidOperationException("No actual Studio launch permitted."); }
    private sealed class MemoryExports : ITheatreExportBackend
    {
        internal TheatreExportContext Context = new(1, new object(), "SYNTHETIC.ppdemo", "SYNTHETIC.ppdemo",
            "SOURCE", "CAMERA", "ANNOTATIONS", "REEL", 600, 60, 180, 1, new object());
        internal bool Current = true, FailWrite;
        internal int Cancellations;
        internal long Revision;
        internal TheatreExportJob? Active, Retry;
        internal string CompletedOutput = "", OpenedOutput = "";
        internal List<(TheatreExportContext Context, TheatreExportOptions Options)> Accepted = new();
        internal ImmutableArray<TheatreExportJob> Pending = ImmutableArray<TheatreExportJob>.Empty;
        public ImmutableArray<TheatreExportPreset> Presets => ImmutableArray.Create(new TheatreExportPreset("Balanced", 1, 60, false, false), new TheatreExportPreset("Cinema", 1, 24, false, false));
        public ImmutableArray<string> Encoders() => ImmutableArray.Create("libx264", "h264_videotoolbox");
        public TheatreExportContext Capture() => Context;
        public bool IsCurrent(TheatreExportContext context) => Current && ReferenceEquals(context.PlaybackOwner, Context.PlaybackOwner);
        public TheatreExportQueueState Queue() => new(Revision, Pending, Active, Retry, Active == null ? "Idle" : "Rendering",
            "SYNTHETIC QUEUE", Active == null ? 0 : .4f, ImmutableArray<string>.Empty, CompletedOutput);
        public TheatreExportJob NewJob() => new(Guid.NewGuid(), new object(), "SYNTHETIC.ppdemo", "SYNTHETIC-export/replay.mp4", "REVIEWED FRAMES 60–180 // 1080p60");
        public void AddPending() { Pending = Pending.Add(NewJob()); Revision++; }
        public void Enqueue(TheatreExportContext context, TheatreExportOptions options)
        { if (FailWrite) throw new IOException("SYNTHETIC LOCKED MANIFEST"); Accepted.Add((context, options)); AddPending(); }
        public bool CancelExact(TheatreExportQueueState reviewed)
        { if (reviewed.Revision != Revision || reviewed.Active != Active) return false; Cancellations++; Active = null; Revision++; return true; }
        public bool RetryExact(TheatreExportQueueState reviewed)
        { if (reviewed.Revision != Revision || reviewed.Retry != Retry) return false; Pending = Pending.Add(reviewed.Retry!); Revision++; return true; }
        public bool ClearExact(TheatreExportQueueState reviewed)
        { if (reviewed.Revision != Revision || !reviewed.Pending.SequenceEqual(Pending)) return false; Pending = ImmutableArray<TheatreExportJob>.Empty; Revision++; return true; }
        public void OpenOutput(string output) => OpenedOutput = output;
    }
}
