using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Import;
using MphRead.Mods.Platform;
using MphRead.Mods.Render;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Render.Characters;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
using MphRead.Mods.Launcher.RmlUi.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Actual owned source page + asynchronous selection + existing native stage.
// Requires a fresh externally isolated userdata/cwd with read-only AMHE1 paths.
// Only metadata is printed; no framebuffer/source/pose arrays are saved.
internal static class CharacterSourceGlAcceptance
{
    internal static int Run(string assets, string rvz, int requestedWidth, int requestedHeight)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA")))
            throw new InvalidOperationException("Use an explicitly isolated PROJECT_PRIME_USER_DATA.");
        string root = RetroCharacterPreviewExporter.DefaultRoot;
        if (Directory.Exists(root)) throw new InvalidOperationException("Source page probe requires an empty isolated import inventory.");
        string before = Hash(rvz), core = Hash(typeof(Scene).Assembly.Location), tool = Hash(typeof(CharacterSourceGlAcceptance).Assembly.Location);
        int checks = 0, intents = 0; bool cleaned = false; string? failure = null;
        var stageHashes = new List<string>();
        void Check(bool value, string why) { if (!value) { failure = why; throw new InvalidOperationException(why); } checks++; }
        try
        {
            ConsoleSetup.Run(); Paths.UpdatePaths(); Paths.MphKey = Ver.AMHE1;
            Check(Directory.Exists(Paths.FileSystem), "Configured read-only AMHE1 root is available.");
            RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz, selectedIds: new[] { "darksamus-echoes", "darksamus-overcharged-echoes" });
            GraphicsBackendPolicy.Configure("opengl"); RenderOptions.CharacterModelReplacements = false;
            RenderOptions.BrightSkins = false; RenderOptions.ShowCustomCosmetics = false;
            using var window = new RenderWindow(shell: true);
            window.ClientSize = new(requestedWidth, requestedHeight); window.Context.MakeCurrent();
            for (int attempt = 0; attempt < 3 && (window.FramebufferSize.X != requestedWidth || window.FramebufferSize.Y != requestedHeight); attempt++)
            {
                var client = window.ClientSize; var framebuffer = window.FramebufferSize;
                Check(framebuffer.X > 0 && framebuffer.Y > 0 && client.X > 0 && client.Y > 0, "Native size conversion has positive dimensions.");
                window.ClientSize = new(Math.Max(1, (int)Math.Round((double)requestedWidth * client.X / framebuffer.X)),
                    Math.Max(1, (int)Math.Round((double)requestedHeight * client.Y / framebuffer.Y)));
                GLFW.PollEvents();
            }
            int width = window.FramebufferSize.X, height = window.FramebufferSize.Y;
            Check(width == requestedWidth && height == requestedHeight, "Actual framebuffer equals the qualified layout dimensions.");
            using var host = new RmlUiHost();
            Check(host.Initialize(width, height, 1, Path.GetFullPath(assets)), "Actual native host initializes.");
            using var pages = new RmlUiPageManager(host);
            try
            {
                foreach (string sourceId in new[] { "darksamus-echoes", "darksamus-overcharged-echoes" })
                {
                    bool final = sourceId == "darksamus-overcharged-echoes";
                    int pageStartChecks = checks, pageStartIntents = intents, pageStartHashes = stageHashes.Count;
                    using var page = new NativeCharacterSourcePreviewPage(host, pages, sourceId, asModal: false);
                    try
                    {
                        page.Open();
                        var wait = Stopwatch.StartNew();
                        while (page.Request == null && wait.Elapsed < TimeSpan.FromSeconds(60))
                        { page.Tick(); host.Update(); host.Render(width, height); Thread.Sleep(10); }
                        Check(page.Request != null && page.OwnsForeground, "Actual asynchronous page loader publishes a foreground source selection.");
                        Check(page.Request!.Part == CharacterModelPart.Biped && page.Preview.Mode == SkinContext.Biped,
                            "Native source page opens in body mode with no cannon selection by default.");
                        var selection = (CharacterSourcePreviewSelection)typeof(NativeCharacterSourcePreviewPage)
                            .GetField("_selection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                        Check(selection.SelectedAppearance == new PrivateCharacterAppearance("darksamus", final ? "MP2DarkSamusFinal" : "MP2DarkSamus")
                            && selection.HasCannon == !final && page.Request!.Appearance == selection.SelectedAppearance,
                            "Page captures its own exact appearance and cannon capability.");
                        if (final)
                        {
                            Check(!host.FocusDocument(page.Document, "source_cannon"), "Final cannon control is natively disabled.");
                            // Reuse an actual native sequence rather than poisoning
                            // the page's replay fence with an invented maximum.
                            Click("source_body", RmlUiIntentKind.HunterPreviewMode, 0, expectedHandled: false, routedArgument: 1);
                            Check(page.Request!.Part == CharacterModelPart.Biped && page.Preview.Mode == SkinContext.Biped,
                                "Final rejects a forged cannon argument without changing body mode.");
                            Check(!selection.Pack.TryResolvePrivate(Hunter.Samus, selection.SelectedAppearance, CharacterModelPart.ViewModel, 0, out _),
                                "Final pack has no borrowed normal cannon asset.");
                        }
                        Draw(); Layout();
                        var first = page.Request!;
                        Click("source_pause", RmlUiIntentKind.SettingsAction, 248);
                        page.Tick(); double paused = page.Request!.Seconds, pausedMaterial = page.Request.MaterialSeconds;
                        Thread.Sleep(30); page.Tick();
                        Check(page.Request!.Seconds == paused && page.Request.MaterialSeconds == pausedMaterial, "Translated Pause freezes pose and material clocks.");
                        Click("source_next", RmlUiIntentKind.SettingsAction, 247);
                        Check(page.Request!.ClipOrdinal != first.ClipOrdinal && page.Request.Seconds == 0, "Translated Next selects a different exact source root.");
                        Draw();
                        Click("source_prev", RmlUiIntentKind.SettingsAction, 246);
                        Check(page.Request!.ClipOrdinal == first.ClipOrdinal, "Translated Previous restores the original root.");
                        Click("source_rotate_right", RmlUiIntentKind.HunterRotate, 1);
                        Check(page.Preview.Yaw != 0, "Translated Rotate changes the existing preview controller.");
                        Draw();
                        Click("source_zoom_in", RmlUiIntentKind.HunterZoom, 1);
                        Check(page.Preview.Zoom > 1, "Translated Zoom changes the existing preview controller.");
                        Draw();
                        Click("source_pause", RmlUiIntentKind.SettingsAction, 248);
                        Thread.Sleep(20); page.Tick();
                        var body = page.Request!;
                        Check(body.Part == CharacterModelPart.Biped && body.Seconds > 0 && body.MaterialSeconds > pausedMaterial, "Body pose and material clocks advance after Play.");
                        Draw();
                        if (!final)
                        {
                            Click("source_cannon", RmlUiIntentKind.HunterPreviewMode, 1);
                            Check(page.Request is { Part: CharacterModelPart.ViewModel, ClipOrdinal: 32, Seconds: 0 } cannon
                                && page.Preview.Mode == SkinContext.ViewModel && ReferenceEquals(cannon.Owner, body.Owner)
                                && ReferenceEquals(cannon.Pack, body.Pack), "Native Cannon selects the fixed root32/framezero in the same captured pack/owner.");
                            foreach (string id in new[] { "source_prev", "source_next", "source_pause", "source_restart" })
                                Check(!host.FocusDocument(page.Document, id), "Cannon disables native clip controls: " + id);
                            Thread.Sleep(20); page.Tick();
                            Check(page.Request is { Part: CharacterModelPart.ViewModel, ClipOrdinal: 32, Seconds: 0 }, "Cannon has no advancing animation clock.");
                            Draw();
                            Click("source_rotate_right", RmlUiIntentKind.HunterRotate, 1);
                            Check(page.Preview.Yaw != 0 && page.Preview.Mode == SkinContext.ViewModel, "Native Cannon keeps the existing rotation control.");
                            Draw();
                            Click("source_body", RmlUiIntentKind.HunterPreviewMode, 0);
                            Check(page.Request is { Part: CharacterModelPart.Biped } returned
                                && returned.ClipOrdinal == body.ClipOrdinal && ReferenceEquals(returned.Owner, body.Owner)
                                && ReferenceEquals(returned.Pack, body.Pack) && page.Preview.Mode == SkinContext.Biped,
                                "Native Body restores the selected clip in the same captured generation.");
                            double resumed = page.Request!.Seconds;
                            Thread.Sleep(20); page.Tick();
                            Check(page.Request!.Seconds > resumed, "Body clock resumes after returning from fixed cannon.");
                            Draw(); Check(host.FocusDocument(page.Document, "source_prev"), "Native focus restores enabled body clip controls."); host.Update(); Layout();
                        }
                        var beforeHidden = page.Request!;
                        pages.SetVisible(false); page.Tick(); host.Update();
                        Check(!page.OwnsForeground && page.Request == null && SourceAttachment() == null,
                            "Hidden source page releases its native attachment and publishes no request.");
                        Check(!page.Handle(new(RmlUiIntentKind.HunterRotate, 1, page.Document, ulong.MaxValue))
                            && !RmlUiHunterStageRenderer.DrawSourcePicker(window, host, page.Document, page.Preview, beforeHidden, width, height),
                            "Hidden source document rejects delayed input and stale stage submission.");
                        pages.SetVisible(true); page.Tick();
                        Check(page.Request is { } visible && visible.Seconds == beforeHidden.Seconds
                            && visible.MaterialSeconds == beforeHidden.MaterialSeconds && ReferenceEquals(visible.Owner, beforeHidden.Owner),
                            "Visibility resume keeps exact owner and clocks without hidden catch-up.");
                        Draw(); Layout();
                        Click("source_back", RmlUiIntentKind.HunterCancel, 0);
                        Check(page.Closed && page.Request == null && SourceAttachment() == null, "Translated Back closes source ownership and clears native source attachment.");
                        var retired = page.Document; var retiredPreview = page.Preview;
                        page.Dispose(); host.Update();
                        Check(!host.IsAlive(retired) && !page.Handle(new(RmlUiIntentKind.HunterRotate, 1, retired, ulong.MaxValue))
                            && !RmlUiHunterStageRenderer.DrawSourcePicker(window, host, retired, retiredPreview, beforeHidden, width, height),
                            "Disposed source document cannot revive native draw ownership or consume stale intent.");
                        Console.WriteLine(JsonSerializer.Serialize(new { Scope = "Actual native source RML page, async selection, source-program body draw and translated controls; preview lighting only",
                            SourceCharacterId = sourceId, SourcePrograms = final ? 9 : 7, BodyTriangles = final ? 12734 : 6340,
                            RequestedFramebufferWidth = requestedWidth, RequestedFramebufferHeight = requestedHeight,
                            ActualFramebufferWidth = width, ActualFramebufferHeight = height,
                            Checks = checks - pageStartChecks, TranslatedIntents = intents - pageStartIntents, PixelSha256 = stageHashes.Skip(pageStartHashes).ToArray(),
                            BodyCannonBody = !final, FixedCannonClipOrdinal = final ? (int?)null : 32, FinalCannonRefused = final, GlobalReplacementsDisabled = !RenderOptions.CharacterModelReplacements }));

                        void Draw()
                        {
                            host.Update();
                            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
                            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(0, 0, 0, 1); GL.ClearDepth(1.0);
                            GL.DepthMask(true); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                            host.Render(width, height);
                            Check(page.Request is { } request && RmlUiHunterStageRenderer.DrawSourcePicker(window, host, page.Document,
                                page.Preview, request, width, height), "Actual native source rectangle renders through the shared launcher stage.");
                            CheckSubmittedGeometry();
                            page.ReportDraw(true); GL.Finish();
                            Check(!RenderOptions.CharacterModelReplacements && GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError,
                                "Native page draw is GL-clean without enabling global replacements.");
                            byte[] pixels = new byte[checked(width * height * 4)];
                            int old = GL.GetInteger(GetPName.PackAlignment);
                            try
                            {
                                GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
                                GL.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                                Check(GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError, "Native page frame readback succeeds.");
                                stageHashes.Add(Convert.ToHexStringLower(SHA256.HashData(pixels)));
                            }
                            finally { Array.Clear(pixels); GL.PixelStore(PixelStoreParameter.PackAlignment, old); }
                        }
                        Scene StageScene() => (Scene)typeof(LauncherHunter).GetField("_scene", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                        HunterPreviewEntity Actor() => (HunterPreviewEntity)typeof(Scene).GetField("_activePreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(StageScene())!;
                        object? SourceAttachment() => typeof(HunterPreviewEntity).GetField("_sourceRigPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Actor());
                        void CheckSubmittedGeometry()
                        {
                            var scene = StageScene();
                            var request = page.Request ?? throw new InvalidOperationException("Current source request required for submission witness.");
                            var items = (List<RenderItem>)typeof(Scene).GetField("_previewItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                            // Observe existing resource owners without calling TryGet after
                            // the picker has restored its temporary capability scope.
                            object table = typeof(CharacterModelRuntime).GetField("_scenes", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                            object?[] arguments = { scene, null };
                            Check((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, arguments)!, "Actual stage owns character resources.");
                            object resources = arguments[1]!;
                            var models = (System.Collections.IDictionary)resources.GetType().GetField("SourceModels")!.GetValue(resources)!;
                            var candidates = models.Values.Cast<object>().Select(entry => (CharacterWeightedRenderModel)entry.GetType().GetProperty("Model")!.GetValue(entry)!)
                                .Where(model => model.Asset.PrivateAppearance == request.Appearance && model.Asset.Part == request.Part).ToArray();
                            Check(candidates.Length == 1 && SourceAttachment() != null, "Exactly one live source model matches the current page appearance and part.");
                            var model = candidates.Single();
                            Check(model.Segments.Count > 0 && items.Count(item => item.Type == RenderItemType.Mesh && model.Segments.Any(segment => segment.ListId == item.ListId)) == model.Segments.Count,
                                "Every current source segment has an actual native stage mesh submission.");
                            if (request.Part == CharacterModelPart.ViewModel)
                            {
                                Check(!final && model.IndexCount == 392 * 3 && model.SourceCannon?.IsGenerated == true
                                    && model.Segments.All(segment => segment.SourceGx == null && segment.AlbedoBinding.HasValue),
                                    "Normal cannon remains its fixed392-triangle diagnostic-material cut.");
                                Check(items.All(item => item.SourceGx == null), "Fixed cannon does not borrow body GX programs.");
                            }
                            else
                            {
                                int programs = final ? 9 : 7, triangles = final ? 12734 : 6340;
                                var submissions = items.Where(item => item.SourceGx != null).ToArray();
                                Check(model.IndexCount == triangles * 3 && model.Segments.All(segment => segment.SourceGx != null)
                                    && model.Segments.Select(segment => segment.SourceGx!.Program).Distinct().Count() == programs,
                                    "Complete source body triangle inventory and every independent material program are retained.");
                                Check(submissions.Length == model.Segments.Count && submissions.Select(item => item.SourceGx!.Binding.Program).Distinct().Count() == programs
                                    && submissions.All(item => ReferenceEquals(item.SourceGx!.Binding.Owner, scene)
                                        && GL.IsProgram(item.SourceGx.Binding.ProgramId) && item.SourceGx.Binding.Textures.All(GL.IsTexture)),
                                    "Actual body submissions bind all source programs and resident source textures in this same Scene.");
                            }
                        }
                        void Layout()
                        {
                            var stage = Box("source_stage"); var panel = Box("source_panel"); var preview = Box("source_preview");
                            Check(Inside(stage, Box("stage")) && Inside(stage, (0f, 0f, (float)width, (float)height)) && Inside(panel, stage) && Inside(preview, panel),
                                "Source stage/panel/model rectangle remain contained in actual native layout.");
                            foreach (string id in new[] { "source_back", "source_clip", "source_status", "source_render_status" })
                                Check(Inside(Box(id), (0f, 0f, (float)width, (float)height)), "Native source control/status is on screen: " + id);
                            Check(Inside(Box("source_back"), Box("stage")), "Back stays inside the app-shell content stage above the footer.");
                        }
                        (float X, float Y, float W, float H) Box(string id)
                        {
                            Check(host.TryGetElementBounds(page.Document, id, out float x, out float y, out float w, out float h)
                                && float.IsFinite(x + y + w + h) && w > 0 && h > 0, "Actual native element has finite visible bounds: " + id);
                            return (x, y, w, h);
                        }
                        void Click(string id, RmlUiIntentKind expected, int argument, bool expectedHandled = true, int? routedArgument = null)
                        {
                            Check(host.FocusDocument(page.Document, id), "Native focus scrolls the actual target into view: " + id);
                            host.Update(); var box = Box(id);
                            if (id != "source_back") Check(Inside(box, Box("source_panel")), "Native scroll brings the clicked control inside its panel: " + id);
                            Check(Inside(box, (0f, 0f, (float)width, (float)height)), "Clicked actual native control is on screen: " + id);
                            while (host.TryTakeIntent(out _)) { }
                            double x = box.X + box.W / 2, y = box.Y + box.H / 2;
                            host.Input.PointerButton(0, x, y, true); host.Input.PointerButton(0, x, y, false); host.Update();
                            bool found = false;
                            while (host.TryTakeIntent(out var intent))
                            {
                                if (intent.Kind != expected || intent.Argument != argument) continue;
                                var routed = routedArgument.HasValue ? intent with { Argument = routedArgument.Value } : intent;
                                Check(intent.Document == page.Document && page.Handle(routed) == expectedHandled,
                                    expectedHandled ? "Real translated native intent is owned by this source page." : "Current native sequence with an unsupported cannon argument is refused.");
                                found = true; intents++;
                            }
                            if (!found) Console.WriteLine(JsonSerializer.Serialize(new { UiClickFailure = id, Focused = host.FocusedElement(), Hovered = host.HoveredElement(), Bounds = new { box.X, box.Y, box.W, box.H }, Panel = Box("source_panel") }));
                            Check(found, "Actual native click emits expected translated intent: " + id);
                        }
                    }
                    finally
                    {
                        page.Dispose(); LauncherHunter.Reset();
                    }
                }
            }
            finally
            {
                var field = typeof(LauncherHunter).GetField("_scene", BindingFlags.Static | BindingFlags.NonPublic)!;
                if (field.GetValue(null) is Scene scene) { try { scene.UnloadGl(); } finally { field.SetValue(null, null); } }
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            cleaned = !Directory.Exists(root);
            bool unchanged = before == Hash(rvz) && core == Hash(typeof(Scene).Assembly.Location) && tool == Hash(typeof(CharacterSourceGlAcceptance).Assembly.Location);
            Console.WriteLine(JsonSerializer.Serialize(new { Cleanup = cleaned, InputsAndAssembliesUnchanged = unchanged,
                SourceSha256 = before, CoreSha256 = core, ToolSha256 = tool, FailedCheck = failure }));
            if (!cleaned || !unchanged) throw new InvalidOperationException("Native source page probe cleanup or input identity failed.");
        }
        return 0;
    }
    private static bool Inside((float X, float Y, float W, float H) a, (float X, float Y, float W, float H) b)
        => a.X >= b.X - 1 && a.Y >= b.Y - 1 && a.X + a.W <= b.X + b.W + 1 && a.Y + a.H <= b.Y + b.H + 1;
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(input)); }
}
