using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class BookmarkGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        Check(!DemoPlayback.IsActive, "annotation fixture starts without a replay world");
        var memory = new MemoryBookmarks();
        using var transport = new TheatrePlaybackController(new NoStudio());
        using var pages = new RmlUiPageManager(host);
        using var page = new TheatrePlaybackPagePresenter(host, pages, transport, memory);
        page.Open(); Frame();
        var playback = page.Document;
        Check(!host.FocusDocument(playback, "replay_bookmarks"), "production inactive playback keeps its bookmark entry disabled");
        // Inject a page-bound command against the fake annotation owner only.
        // No active playback state or real RenderWindow lease is fabricated.
        Check(page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.Bookmarks, playback, 1)),
            "injected synthetic annotation owner opens the actual native panel");
        Frame();
        var panel = pages.Top;
        Check(panel != playback && host.FocusedElement() == "replay_bookmark_name",
            "native bookmark panel owns foreground and editable name focus");
        Check(!page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.Back, playback, ulong.MaxValue)),
            "background playback cannot leave under the annotation panel");
        host.SetField(panel, "replay_bookmark_name", "SYNTHETIC SAVED MOMENT");
        Click("replay_bookmark_add", TheatrePlaybackAction.BookmarkAdd);
        Check(memory.Additions == 1 && memory.Rows.Length == 2 && memory.Rows[1].Frame == 120
            && host.ReadField(panel, "replay_bookmark_name") == "", "actual native Add delegates a captured fake playhead and clears successful name");
        Click("replay_bookmark_next", TheatrePlaybackAction.BookmarkNext);
        Click("replay_bookmark_jump", TheatrePlaybackAction.BookmarkJump);
        Check(memory.LastSeek == 120, "actual native bookmark navigation seeks only the injected owner");
        Capture("bookmarks");
        Click("replay_bookmark_remove", TheatrePlaybackAction.BookmarkRemove);
        var confirmation = pages.Top;
        Check(confirmation != panel && memory.Removals == 0, "native removal opens confirmation without mutating memory");
        Check(!page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.BookmarkConfirmRemove, panel, ulong.MaxValue)),
            "background panel cannot confirm destructive annotation action");
        Capture("remove-confirmation");
        Click("replay_bookmark_remove_cancel", TheatrePlaybackAction.BookmarkCancel);
        Check(!host.IsAlive(confirmation) && pages.Top == panel && memory.Removals == 0,
            "native cancel retires confirmation and preserves all bookmarks");

        Click("replay_bookmark_remove", TheatrePlaybackAction.BookmarkRemove);
        confirmation = pages.Top;
        memory.Revision = "EXTERNAL-SYNTHETIC-REVISION";
        Click("replay_bookmark_remove_confirm", TheatrePlaybackAction.BookmarkConfirmRemove);
        Check(pages.Top == confirmation && memory.Removals == 0,
            "changed fake persisted revision refuses native confirmation and retains review UI");
        Check(page.Back() && pages.Top == panel && !host.IsAlive(confirmation),
            "presenter Back cancels exact removal before returning to panel");
        Click("replay_bookmark_refresh", TheatrePlaybackAction.BookmarkRefresh);
        Click("replay_bookmark_remove", TheatrePlaybackAction.BookmarkRemove);
        confirmation = pages.Top;
        Click("replay_bookmark_remove_confirm", TheatrePlaybackAction.BookmarkConfirmRemove);
        Check(memory.Removals == 1 && memory.Rows.Length == 1 && pages.Top == panel,
            "fresh native confirmation removes exactly one injected bookmark");
        Check(!page.HandleIntent(new(RmlUiIntentKind.ReplayAction, (int)TheatrePlaybackAction.BookmarkConfirmRemove, confirmation, ulong.MaxValue))
            && memory.Removals == 1, "retired confirmation cannot remove another bookmark");
        memory.Current = false; Frame();
        Check(!host.FocusDocument(panel, "replay_bookmark_add") && !host.FocusDocument(panel, "replay_bookmark_remove"),
            "owner replacement disables native annotation mutations");
        Click("replay_bookmark_close", TheatrePlaybackAction.BookmarkCancel);
        Check(pages.Top == playback && !host.IsAlive(panel) && !page.Back(), "closing annotations returns to the same playback page");
        page.Dispose();
        Check(!DemoPlayback.IsActive && memory.Removals == 1 && GL.GetError() == ErrorCode.NoError,
            "native bookmark fixture ends without real playback, sidecar files or GL errors");

        void Frame()
        {
            page.Present(); host.Update();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(.2f, .3f, .4f, 1);
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
            Check(host.FocusDocument(target, id), "actual native bookmark focus: " + id);
            Settle();
            Check(host.TryGetElementBounds(target, id, out float x, out float y, out float w, out float h)
                && w > 0 && h > 0 && x + w / 2 >= 0 && x + w / 2 < width
                && y + h / 2 >= 0 && y + h / 2 < height, "reachable native bookmark geometry: " + id);
            host.DiscardIntents();
            host.Input.PointerButton(0, x + w / 2, y + h / 2, true);
            host.Input.PointerButton(0, x + w / 2, y + h / 2, false); host.Update();
            bool received = false;
            while (host.TryTakeIntent(out var intent))
            {
                bool accepted = page.HandleIntent(intent);
                if (intent.Kind != RmlUiIntentKind.ReplayAction || intent.Argument != (int)expected) continue;
                Check(accepted && intent.Document == target, "actual native bookmark action: " + id);
                Check(!page.HandleIntent(intent), "duplicate bookmark sequence rejected: " + id);
                received = true;
            }
            Check(received, "expected native bookmark intent emitted: " + id); Frame();
        }
        void Capture(string name)
        {
            Settle(); var pixels = FinalCompositeCapture.Read(width, height);
            string directory = Path.GetFullPath(Path.Combine("artifacts", "rmlui-bookmarks-rml49"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"synthetic-{name}-{width}x{height}.ppm");
            using var file = File.Create(path);
            file.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC MEMORY BOOKMARKS - NO REPLAY FILES OR WORLD\n{width} {height}\n255\n"));
            for (int row = height - 1; row >= 0; row--) file.Write(pixels, row * width * 3, width * 3);
            FinalCompositeCapture.WriteEvidence(path, width, height,
                "Actual native bookmark modal with injected in-memory annotations; no real replay file, world or scene acceptance.");
        }
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("RML BOOKMARK GL PASS " + message);
    }
    private sealed class NoStudio : ITheatrePlaybackBackend
    {
        public bool OpenStudio(string path, out string? error) => throw new InvalidOperationException("No actual Studio launch permitted.");
    }
    private sealed class MemoryBookmarks : ITheatreBookmarkBackend
    {
        internal string Revision = "V1";
        internal bool Current = true;
        internal int Additions, Removals;
        internal uint? LastSeek;
        internal ImmutableArray<TheatreBookmarkEntry> Rows = ImmutableArray.Create(new TheatreBookmarkEntry(
            Guid.Parse("00000000-0000-0000-0000-000000000001"), 60, "SYNTHETIC FIRST MOMENT", DateTime.UnixEpoch));
        public TheatreBookmarkContext Capture() => new(1, "SYNTHETIC.ppdemo", "SYNTHETIC.ppdemo", "RECORDING", Revision, 120, 600);
        public bool IsCurrent(TheatreBookmarkContext context) => Current && context.SceneGeneration == 1;
        public ImmutableArray<TheatreBookmarkEntry> Read(TheatreBookmarkContext context) { Validate(context); return Rows; }
        public void Add(TheatreBookmarkContext context, string name)
        {
            Validate(context); Rows = Rows.Add(new(Guid.NewGuid(), context.Frame, name, DateTime.UnixEpoch.AddDays(1)));
            Additions++; Revision = Guid.NewGuid().ToString();
        }
        public void Remove(TheatreBookmarkContext context, TheatreBookmarkEntry exact)
        {
            Validate(context);
            if (!Rows.Contains(exact)) throw new InvalidOperationException("Exact synthetic bookmark changed.");
            Rows = Rows.Remove(exact); Removals++; Revision = Guid.NewGuid().ToString();
        }
        public void Seek(TheatreBookmarkContext context, TheatreBookmarkEntry exact)
        { Validate(context); LastSeek = exact.Frame; }
        private void Validate(TheatreBookmarkContext context)
        {
            if (!IsCurrent(context) || context.AnnotationRevision != Revision) throw new InvalidOperationException("Synthetic owner or revision changed.");
        }
    }
}
