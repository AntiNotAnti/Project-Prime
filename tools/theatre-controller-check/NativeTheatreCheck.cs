#if MPHREAD_RMLUI_POC
using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

internal static class NativeTheatreCheck
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ElementBounds(ulong document, [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
        out float x, out float y, out float width, out float height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CommandCount();
    [StructLayout(LayoutKind.Sequential)] private struct NativeTexture
    { public uint Size, Width, Height, Reserved; public ulong Handle; public nint Pixels; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Texture(int index, ref NativeTexture texture);
    [StructLayout(LayoutKind.Sequential)] private struct NativeDrawCommand
    {
        public uint Size, Kind; public ulong Geometry, Texture; public float TranslationX, TranslationY;
        public int X, Y, Width, Height; public uint Enabled, Operation;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public float[] Transform;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DrawCommand(int index, ref NativeDrawCommand command);
    public static void Run(string library, string assets)
    {
        nint module = NativeLibrary.Load(Path.GetFullPath(library));
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly, (name, _, _) => name == "ProjectPrime.RmlUi.Native" ? module : 0);
            var bounds = Marshal.GetDelegateForFunctionPointer<ElementBounds>(NativeLibrary.GetExport(module, "pp_rmlui_document_element_bounds"));
            var commands = Marshal.GetDelegateForFunctionPointer<CommandCount>(NativeLibrary.GetExport(module, "pp_rmlui_draw_command_count"));
            var textureCount = Marshal.GetDelegateForFunctionPointer<CommandCount>(NativeLibrary.GetExport(module, "pp_rmlui_draw_texture_count"));
            var texture = Marshal.GetDelegateForFunctionPointer<Texture>(NativeLibrary.GetExport(module, "pp_rmlui_draw_texture"));
            var draw = Marshal.GetDelegateForFunctionPointer<DrawCommand>(NativeLibrary.GetExport(module, "pp_rmlui_draw_command"));
            int checks = 0;
            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("Theatre native: " + message); checks++; }
            string imageDirectory = Path.Combine(Path.GetTempPath(), "prime-theatre-images-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(imageDirectory);
            try
            {
                string png = Path.Combine(imageDirectory, "recorded-preview.png");
                File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5VQAAAAASUVORK5CYII="));
                var images = new TheatreImageCache(Path.Combine(imageDirectory, "cache"));
                string tga = images.Load(png, CancellationToken.None);
                byte[] encoded = File.ReadAllBytes(tga);
                Check(encoded.Length == 21 && encoded[2] == 2 && encoded[12] == 1 && encoded[14] == 1 && encoded[16] == 24 && encoded[17] == 0x20,
                    "authoritative PNG becomes uncompressed top-left RGB TGA accepted by native renderer");
                Check(images.Load(png, CancellationToken.None) == tga, "thumbnail cache keys include source identity and reuses conversion");
                foreach (var viewport in new[] { (1280, 720, 1f), (1920, 1080, 1f), (960, 540, 1f), (1920, 1080, 2f) })
                {
                    using var host = new RmlUiHost();
                    Check(host.Initialize(viewport.Item1, viewport.Item2, viewport.Item3, Path.GetFullPath(assets), RmlUiRenderBackend.DrawList), "native initialization");
                    using var pages = new RmlUiPageManager(host);
                    var backend = new TheatreBoundary();
                    backend.Entries = backend.Entries.SetItem(0, backend.Entries[0] with { PreviewPaths = System.Collections.Immutable.ImmutableArray.Create(png) });
                    using var controller = new TheatreController(backend, manageStorage: false);
                    using var page = new TheatrePagePresenter(host, pages, controller, images);
                    page.Open(); host.Update();
                    Check(page.IsOpen && host.FocusedElement() == "theatre_search", "library opens with native input focus");
                    Check(bounds(page.Document.DocumentId, "theatre_title", out _, out float titleY, out float titleWidth, out float titleHeight) != 0
                        && titleWidth > 0 && titleHeight > 0
                        && bounds(page.Document.DocumentId, "theatre_summary", out _, out float summaryY, out float summaryWidth, out float summaryHeight) != 0
                        && summaryWidth > 0 && summaryHeight > 0 && summaryY >= titleY + titleHeight - 1,
                        "native block heading and summary have positive widths and vertical separation");
                    Check(controller.Snapshot().TotalCount == 12, "full library is retained behind paged presentation");
                    Check(bounds(page.Document.DocumentId, "injected_action", out _, out _, out _, out _) == 0, "untrusted metadata remains escaped text");
                    Check(bounds(page.Document.DocumentId, "theatre_entry0", out float x, out float y, out float width, out float height) != 0
                        && width > 0 && height >= 32 * viewport.Item3 && x >= 0 && x + width <= viewport.Item1 + 1,
                        $"native library row is responsive ({x},{y} {width}x{height}, {viewport})");
                    bool uploaded = false, drawn = false;
                    ulong previewTexture = 0;
                    var imageDeadline = System.Diagnostics.Stopwatch.StartNew();
                    do
                    {
                        page.Present(); host.Update(); host.Render(viewport.Item1, viewport.Item2);
                        for (int i = 0; i < textureCount(); i++)
                        {
                            var candidate = new NativeTexture { Size = (uint)Marshal.SizeOf<NativeTexture>() };
                            if (texture(i, ref candidate) != 0 && candidate.Width == 1 && candidate.Height == 1 && candidate.Pixels != 0)
                            {
                                byte[] rgba = new byte[4]; Marshal.Copy(candidate.Pixels, rgba, 0, 4);
                                uploaded |= rgba[0] == encoded[20] && rgba[1] == encoded[19] && rgba[2] == encoded[18] && rgba[3] == 255;
                                if (uploaded) previewTexture = candidate.Handle;
                            }
                        }
                        if (!uploaded) Thread.Sleep(1);
                    } while (!uploaded && imageDeadline.Elapsed < TimeSpan.FromSeconds(3));
                    Check(uploaded, "asynchronous authoritative thumbnail binds and uploads exact decoded pixels to native renderer");
                    Check(host.FocusDocument(page.Document, "theatre_thumbnail0"), "thumbnail action has real native focus");
                    host.Update(); host.Render(viewport.Item1, viewport.Item2);
                    for (int i = 0; i < commands(); i++)
                    {
                        var frameCommand = new NativeDrawCommand { Size = (uint)Marshal.SizeOf<NativeDrawCommand>(), Transform = new float[16] };
                        drawn |= draw(i, ref frameCommand) != 0 && frameCommand.Texture == previewTexture && frameCommand.Geometry != 0;
                    }
                    Check(drawn, "thumbnail pixels participate in native geometry draw submission");
                    void Activate(RmlUiDocumentToken document, string id, RmlUiIntentKind kind)
                    {
                        Check(host.FocusDocument(document, id), "focus " + id);
                        host.Input.Key(2, true); host.Input.Key(2, false); host.Update();
                        Check(host.TryTakeIntent(out var intent) && intent.Kind == kind && intent.Document == document && page.HandleIntent(intent), "actual native action " + id);
                        Check(!host.TryTakeIntent(out _), "native action emits once");
                    }
                    Activate(page.Document, "theatre_next_page", RmlUiIntentKind.TheatreAction);
                    Activate(page.Document, "theatre_entry3", RmlUiIntentKind.TheatreEntry);
                    Check(controller.Snapshot().Selected?.Path == "entry-11.ppdemo", "native page can select final archive");
                    host.SetField(page.Document, "theatre_search", "λ😀");
                    Activate(page.Document, "theatre_search_submit", RmlUiIntentKind.TheatreAction);
                    Check(controller.Snapshot().FilteredCount == 1 && host.ReadField(page.Document, "theatre_search") == "λ😀", "Unicode native field submits through filter contract");
                    Activate(page.Document, "theatre_clear_search", RmlUiIntentKind.TheatreAction);
                    Activate(page.Document, "theatre_delete", RmlUiIntentKind.TheatreAction);
                    var modal = page.ConfirmationDocument;
                    var modalCancellation = host.DocumentCancellation(modal);
                    Check(modal != default && host.FocusedElement() == "theatre_cancel_delete", "destructive native modal opens with cancel focus");
                    Check(!page.HandleIntent(new(RmlUiIntentKind.TheatreAction, (int)TheatreAction.Watch, page.Document, ulong.MaxValue)), "covered archive cannot dispatch actions through delete modal");
                    Activate(modal, "theatre_cancel_delete", RmlUiIntentKind.TheatreAction);
                    Check(!host.IsAlive(modal) && modalCancellation.IsCancellationRequested && backend.Executions.Count == 0, "cancel delete retires document without disk mutation");
                    Activate(page.Document, "theatre_delete", RmlUiIntentKind.TheatreAction);
                    modal = page.ConfirmationDocument;
                    Check(host.Back() && page.Present() && !controller.Snapshot().ConfirmDelete && !host.IsAlive(modal), "host Back reconciles modal controller state");
                    Activate(page.Document, "theatre_watch", RmlUiIntentKind.TheatreAction);
                    Check(!host.FocusDocument(page.Document, "theatre_watch"), "busy preparation disables native duplicate activation");
                    Activate(page.Document, "theatre_cancel_job", RmlUiIntentKind.TheatreAction);
                    backend.Launches[^1].SetResult(new(new MphRead.Mods.Launcher.LaunchPlan { Kind = MphRead.Mods.Launcher.LaunchKind.Demo, DemoPath = "late.ppdemo" }, ""));
                    Thread.Sleep(5); page.Present();
                    Check(!page.TryTakeLaunch(out _), "cancelled native preparation cannot queue stale engine launch");
                    host.Render(viewport.Item1, viewport.Item2); Check(commands() > 0, "library emits native draw commands");
                    var document = page.Document; var cancellation = host.DocumentCancellation(document);
                    Check(page.Close() && cancellation.IsCancellationRequested, "library document cancellation follows page close");
                    using var playbackController = new TheatrePlaybackController(backend);
                    using var playback = new TheatrePlaybackPagePresenter(host, pages, playbackController);
                    playback.Open(); host.Update();
                    Check(playback.IsOpen && !host.FocusDocument(playback.Document, "replay_pause"), "inactive authoritative playback disables native controls");
                    Check(bounds(playback.Document.DocumentId, "replay_viewport", out x, out y, out width, out height) != 0 && width > 0 && height > 0
                        && x >= 0 && y >= 0 && x + width <= viewport.Item1 + 1 && y + height <= viewport.Item2 + 1,
                        $"engine viewport has responsive native document bounds ({x},{y} {width}x{height}, {viewport})");
                    Check(host.FocusDocument(playback.Document, "replay_back"), "back remains available on inactive or ended playback");
                    host.Input.Key(2, true); host.Input.Key(2, false); host.Update();
                    Check(host.TryTakeIntent(out var back) && back.Kind == RmlUiIntentKind.ReplayAction && playback.HandleIntent(back)
                        && playback.TryTakeEngineCommand(out var command) && command == TheatrePlaybackAction.Back && !playback.TryTakeEngineCommand(out _),
                        "native replay Back hands one command to engine owner");
                    host.Render(viewport.Item1, viewport.Item2); Check(commands() > 0, "playback controls emit native draw commands");
                }
            }
            finally { Directory.Delete(imageDirectory, recursive: true); }
            Console.WriteLine($"THEATRE NATIVE PASS {checks} checks (real DOM/library actions/modal/focus/viewport/image cache; 720p,1080p,small,2x density).");
        }
        finally { NativeLibrary.Free(module); }
    }
}
#endif
