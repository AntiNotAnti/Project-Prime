using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class RangeGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        Check(!DemoPlayback.IsActive, "range fixture starts without playback or a replay world");
        var memory = new MemoryRanges();
        using var transport = new TheatrePlaybackController(new NoStudio());
        using var pages = new RmlUiPageManager(host);
        using var page = new TheatrePlaybackPagePresenter(host, pages, transport, ranges: memory);
        page.Open(); Frame();
        var playback = page.Document;
        Check(!host.FocusDocument(playback, "replay_ranges") && !host.FocusDocument(playback, "replay_mark_in"),
            "production inactive transport keeps range toolbar disabled");
        Check(page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.Ranges, playback, 1)),
            "injected fake range owner opens native panel without fabricating active playback");
        Frame(); var panel = pages.Top;
        Check(panel != playback && host.FocusedElement() == "replay_range_name", "actual range panel owns editable focus");
        Check(!host.FocusDocument(panel, "replay_range_highlight") && !host.FocusDocument(panel, "replay_range_virtual"),
            "missing marks disable both native creation actions");
        Click("replay_range_mark_in", TheatrePlaybackAction.MarkIn);
        memory.Context = memory.Context with { Replay = memory.Context.Replay with { Frame = 180 } };
        Click("replay_range_mark_out", TheatrePlaybackAction.MarkOut);
        Check(memory.Context.MarkIn == 60 && memory.Context.MarkOut == 180, "native Mark In/Out delegate to the existing injected transport selection");
        host.SetField(panel, "replay_range_name", "SYNTHETIC RETRY NAME"); memory.FailWrite = true;
        Click("replay_range_highlight", TheatrePlaybackAction.RangeSaveHighlight);
        Check(memory.Highlights.Count == 0 && host.ReadField(panel, "replay_range_name") == "SYNTHETIC RETRY NAME",
            "backend failure retains entered native name and creates no highlight");
        Capture("retry"); memory.FailWrite = false;
        Click("replay_range_highlight", TheatrePlaybackAction.RangeSaveHighlight);
        Check(memory.Highlights.Count == 1 && memory.Highlights[0] == ("SYNTHETIC RETRY NAME", 60u, 180u)
            && host.ReadField(panel, "replay_range_name") == "", "retry creates the exact reviewed highlight once and clears successful name");
        host.SetField(panel, "replay_range_name", "SYNTHETIC CLIP");
        memory.Context = memory.Context with { MarkOut = 240 };
        Frame();
        Check(memory.Clips.Count == 0 && host.ReadField(panel, "replay_range_name") == "SYNTHETIC CLIP"
            && !host.FocusDocument(panel, "replay_range_virtual"),
            "changed fake marks clear native creation authority while retaining entered name");
        Click("replay_range_refresh", TheatrePlaybackAction.RangeRefresh);
        Check(host.ReadField(panel, "replay_range_name") == "SYNTHETIC CLIP", "explicit selection refresh preserves typed retry name");
        Click("replay_range_virtual", TheatrePlaybackAction.RangeCreateVirtual);
        Check(memory.Clips.Count == 1 && memory.Clips[0] == ("SYNTHETIC CLIP", 60u, 240u),
            "native virtual creation delegates captured reviewed marks only to fake writer");
        Capture("created");
        Check(!page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.RangeCreateVirtual, playback, ulong.MaxValue))
            && memory.Clips.Count == 1, "background playback cannot create a clip under its range modal");
        host.SetField(panel, "replay_range_name", "SYNTHETIC MATERIALIZED CLIP");
        Click("replay_range_materialized", TheatrePlaybackAction.RangeSaveMaterialized);
        Check(memory.ClipJobs.Count == 1 && !host.FocusDocument(panel, "replay_range_name")
            && !host.FocusDocument(panel, "replay_range_mark_in"),
            "materialized native save captures one fake job and locks name/selection while busy");
        Capture("materialized-saving");
        Click("replay_range_cancel_save", TheatrePlaybackAction.RangeCancelMaterialized);
        Check(memory.ClipJobs[0].Cancellation.IsCancellationRequested
            && host.ReadField(panel, "replay_range_name") == "SYNTHETIC MATERIALIZED CLIP",
            "actual Cancel reaches captured operation and retains name");
        memory.ClipJobs[0].Result.SetResult(new(true, "SYNTHETIC-LATE-CANCELLED.ppdemo", "")); Settle();
        Check(host.ReadField(panel, "replay_range_name") == "SYNTHETIC MATERIALIZED CLIP",
            "late cancelled success cannot clear native name field");
        Click("replay_range_materialized", TheatrePlaybackAction.RangeSaveMaterialized);
        memory.ClipJobs[1].Result.SetResult(new(false, "", "SYNTHETIC LOCKED CLIP DESTINATION")); Settle();
        Check(host.ReadField(panel, "replay_range_name") == "SYNTHETIC MATERIALIZED CLIP"
            && host.FocusDocument(panel, "replay_range_materialized"), "failed materialized save preserves name and enables retry");
        Click("replay_range_materialized", TheatrePlaybackAction.RangeSaveMaterialized);
        memory.Context = memory.Context with { Replay = memory.Context.Replay with { Frame = 300 } };
        memory.ClipJobs[2].Result.SetResult(new(true, "SYNTHETIC-ORIGINAL-TRANSPORT-OUTPUT.ppdemo", "")); Settle();
        Check(memory.ClipJobs.Count == 3 && host.ReadField(panel, "replay_range_name") == ""
            && !host.FocusDocument(panel, "replay_range_cancel_save"),
            "completed captured fake operation clears its own name once and retires native Cancel");
        Capture("materialized-saved");
        memory.Current = false; Frame();
        Check(!host.FocusDocument(panel, "replay_range_highlight") && !host.FocusDocument(panel, "replay_range_mark_in"),
            "changed scene owner disables native range mutations");
        Click("replay_range_close", TheatrePlaybackAction.RangeCancel);
        Check(!host.IsAlive(panel) && pages.Top == playback, "native close returns to same playback page");
        page.Dispose();
        Check(!DemoPlayback.IsActive && memory.Highlights.Count == 1 && memory.Clips.Count == 1
            && GL.GetError() == ErrorCode.NoError, "range fixture leaves real files/playback untouched and no GL errors");

        void Frame()
        {
            page.Present(); host.Update(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.2f, .3f, .4f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
        void Settle()
        {
            var timer = Stopwatch.StartNew();
            do { Frame(); Thread.Sleep(10); } while (timer.ElapsedMilliseconds < 200);
        }
        void Click(string id, TheatrePlaybackAction expected)
        {
            Frame(); var target = pages.Top;
            Check(host.FocusDocument(target, id), "actual native range focus: " + id); Settle();
            Check(host.TryGetElementBounds(target, id, out float x, out float y, out float w, out float h)
                && w > 0 && h > 0 && x + w / 2 >= 0 && x + w / 2 < width && y + h / 2 >= 0 && y + h / 2 < height,
                "reachable actual native range geometry: " + id);
            host.DiscardIntents(); host.Input.PointerButton(0, x + w / 2, y + h / 2, true);
            host.Input.PointerButton(0, x + w / 2, y + h / 2, false); host.Update();
            bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                bool accepted = page.HandleIntent(intent);
                if (intent.Kind != RmlUiIntentKind.ReplayAction || intent.Argument != (int)expected) continue;
                Check(accepted && intent.Document == target, "actual native range action: " + id);
                Check(!page.HandleIntent(intent), "duplicate native range sequence refused: " + id); received = true;
            }
            Check(received, "expected native range intent emitted: " + id); Frame();
        }
        void Capture(string name)
        {
            Settle(); var pixels = FinalCompositeCapture.Read(width, height);
            string directory = Path.GetFullPath(Path.Combine("artifacts", "rmlui-ranges-rml52")); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"synthetic-{name}-{width}x{height}.ppm");
            using var file = File.Create(path);
            file.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC RANGE WRITERS - NO FILES, REPLAY CLOCK OR WORLD\n{width} {height}\n255\n"));
            for (int row = height - 1; row >= 0; row--) file.Write(pixels, row * width * 3, width * 3);
            FinalCompositeCapture.WriteEvidence(path, width, height, "Actual native range panel with fake marks/annotation/virtual/materialized clip writers only; no real replay or file acceptance.");
        }
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("RML RANGE GL PASS " + message);
    }
    private sealed class NoStudio : ITheatrePlaybackBackend
    {
        public bool OpenStudio(string path, out string? error) => throw new InvalidOperationException("Studio must remain untouched.");
    }
    private sealed class MemoryRanges : ITheatreRangeBackend
    {
        internal bool Current = true, FailWrite;
        internal TheatreRangeContext Context = new(new(1, "SYNTHETIC.ppdemo", "SYNTHETIC.ppdemo", "RECORDING", "V1", 60, 600), null, null);
        internal readonly List<(string Name, uint Start, uint End)> Highlights = new(), Clips = new();
        internal sealed record ClipJob(CancellationToken Cancellation, TaskCompletionSource<TheatreMaterializedClipResult> Result);
        internal readonly List<ClipJob> ClipJobs = new();
        public bool MaterializedClipsAvailable => true;
        public TheatreRangeContext Capture() => Context;
        public bool IsCurrent(TheatreRangeContext context) => Current && context.Replay.SceneGeneration == 1;
        public TheatreRangeSelection ReadSelection(TheatreRangeContext context)
        {
            if (!IsCurrent(context)) throw new InvalidOperationException("Synthetic range owner changed.");
            return new(Context.MarkIn, Context.MarkOut, Context.Replay.Duration);
        }
        public void MarkIn(TheatreRangeContext context) { Validate(context); Context = Context with { MarkIn = context.Replay.Frame }; }
        public void MarkOut(TheatreRangeContext context) { Validate(context); Context = Context with { MarkOut = context.Replay.Frame }; }
        public string SaveHighlight(TheatreRangeContext context, string name)
        {
            Validate(context); if (FailWrite) throw new IOException("SYNTHETIC LOCKED SIDECAR");
            Highlights.Add((name, context.MarkIn!.Value, context.MarkOut!.Value));
            Context = Context with { Replay = Context.Replay with { AnnotationRevision = Guid.NewGuid().ToString() } }; return name;
        }
        public string CreateVirtualClip(TheatreRangeContext context, string name)
        {
            Validate(context); Clips.Add((name, context.MarkIn!.Value, context.MarkOut!.Value)); return "SYNTHETIC-NEW.ppclip";
        }
        public Task<TheatreMaterializedClipResult> SaveMaterializedClip(TheatreRangeContext context, string name, CancellationToken cancellation)
        {
            Validate(context);
            var result = new TaskCompletionSource<TheatreMaterializedClipResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            ClipJobs.Add(new(cancellation, result)); return result.Task;
        }
        private void Validate(TheatreRangeContext context)
        {
            if (!IsCurrent(context) || context.MarkIn != Context.MarkIn || context.MarkOut != Context.MarkOut
                || context.Replay.AnnotationRevision != Context.Replay.AnnotationRevision)
                throw new InvalidOperationException("Synthetic range witness changed.");
        }
    }
}
