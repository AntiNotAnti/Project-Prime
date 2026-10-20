using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Campaign;
using MphRead.Mods.Import;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Adventure;
using MphRead.Mods.Launcher.RmlUi.Render;
using OpenTK.Graphics.OpenGL;
using Source = MphRead.Mods.Campaign.CampaignCreditsSource;
using Catalog = MphRead.Mods.Campaign.CampaignCreditsCatalog;
using Phase = MphRead.Mods.Launcher.RmlUi.Pages.Adventure.CampaignCreditsPresentation.Phase;

// Uses the caller's host/context and the real ending page. Readback never leaves
// memory except as aggregate counts/hashes. No screenshots or source output IO.
internal static class CreditsPresentationGlAcceptance
{
    private sealed record Bounds(float X, float Y, float Width, float Height);
    private sealed record Pixels(int LitPixels, string Sha256, int Width, int Height);
    private sealed class InputProof
    {
        internal required CampaignCreditsLoad.Input Pin;
        internal required string Before;
        internal string? After;
    }

    internal static void Run(RmlUiHost host, int width, int height, string assetRoot, string? ownedRoot = null)
    {
        int checks = 0, draws = 0;
        bool owned = ownedRoot != null;
        string? errorType = null, errorMessageSha256 = null;
        string? coreBefore = null, coreAfter = null, toolBefore = null, toolAfter = null;
        string corePath = typeof(Source).Assembly.Location, toolPath = typeof(CreditsPresentationGlAcceptance).Assembly.Location;
        var inputs = new List<InputProof>(); var branches = new List<object>();
        using var pages = new RmlUiPageManager(host);
        NativeCampaignEndingPage? page = null;
        string root = owned ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownedRoot!)) : "";
        try
        {
            Check(width >= 320 && height >= 240 && Directory.Exists(assetRoot), "existing viewport/assets are available");
            Check(host.Active && host.SupportsCanvas, "actual host exposes the native canvas capability");
            host.Input.SetFramebufferScale(1, 1);
            coreBefore = HashAssembly(corePath); toolBefore = HashAssembly(toolPath);
            Catalog catalog;
            if (owned)
            {
                foreach (var pin in CampaignCreditsLoad.Inputs)
                {
                    string hash = HashSource(root, pin); inputs.Add(new() { Pin = pin, Before = hash });
                    Check(hash == pin.Sha256, "owned input matches its exact production source pin");
                }
                Check(inputs.Count == 33, "owned inventory contains exactly33 tracked inputs");
                catalog = CampaignCreditsLoad.Read(new(root, "AMHE1", Language.English), CancellationToken.None);
            }
            else catalog = SyntheticCatalog();

            foreach (bool good in new[] { false, true })
            {
                var receipt = CreditsFrameFixtures.Summary(good) with { SaveCommitted = true };
                var audio = new SilentAudio(); var job = new CompletedJob(catalog);
                double now = 0; bool current = true; int loads = 0;
                page = new(host, pages, receipt, audio, original: true,
                    load: () => { loads++; return job; }, receiptCurrent: () => current, now: () => now);
                page.Open(); var document = page.Document;
                Check(loads == 1 && page.CreditsPhase == Phase.Credits && page.CreditsPage == 6,
                    "real page adopts one complete catalog and starts source page6");
                Check(audio.Tracks.SequenceEqual(new[] { SeqId.CREDITS }) && audio.Stops == 0,
                    "credits entry starts exactly the existing CREDITS sequence through silent sink");
                Check(!page.CreditsCanAcknowledge && !page.TryTakeReturn(out _), "credits cannot manufacture results acknowledgment");
                Frame(); CheckCanvasLayout();
                Check(!HasBounds("campaign_ending_source_ok"), "original OK is hidden before results");
                Check(!page.Handle(new(RmlUiIntentKind.ResultsAction, 0, document, ulong.MaxValue)),
                    "early source acknowledgment is refused without consuming a future native sequence");

                // Exercise actual host modal ownership before any logical ticks;
                // suspended time must not alter the independently pinned journey.
                var modal = pages.OpenModal(new("credits-fence", "pages/social/confirm.rml", "social_confirm_cancel"));
                now = 100; page.Refresh(); host.Update();
                Check(!page.Back() && !page.Handle(new(RmlUiIntentKind.ResultsAction, 0, document, ulong.MaxValue)),
                    "real foreground modal fences both application cancel and source acknowledgment");
                pages.CloseModal(); Check(!host.IsAlive(modal), "modal document retires before source resume");
                now = 200; page.Refresh();
                int firstResults = -1, maximumCreditLit = 0, lastPage = 6;
                var readbacks = new List<object>(); var creditsHashes = new HashSet<string>(StringComparer.Ordinal);
                int endTick = owned ? 2740 : 120;
                for (int tick = 1; tick <= endTick; tick++)
                {
                    now = 200 + tick / 30d; page.Refresh();
                    Check(page.CreditsPhase is Phase.Credits or Phase.Results,
                        "every scheduled source tick stays admitted without fallback");
                    if (page.CreditsPhase == Phase.Results && firstResults < 0) firstResults = tick;
                    if (tick % 30 == 0 || page.CreditsPage != lastPage || tick == endTick)
                    {
                        Frame(); CheckCanvasLayout();
                        var pixels = ReadCanvas();
                        if (page.CreditsPhase == Phase.Credits)
                        { maximumCreditLit = Math.Max(maximumCreditLit, pixels.LitPixels); creditsHashes.Add(pixels.Sha256); }
                        if (page.CreditsPage != lastPage || tick == endTick || tick == 30)
                            readbacks.Add(new { Tick = tick, Page = page.CreditsPage, pixels.LitPixels, pixels.Sha256, pixels.Width, pixels.Height });
                    }
                    lastPage = page.CreditsPage;
                }
                Check(maximumCreditLit > 0, "actual canvas contains nonblack credits glyph/widget pixels");
                Check(page.CreditsPhase == Phase.Results && page.CreditsPage == (good ? 49 : 50)
                    && page.CreditsCanAcknowledge && !page.Closed, "captured ending selects its actual source results and live OK action");
                if (owned) Check(firstResults == 2610, "owned source reaches results at independently pinned tick2610 despite modal pause");
                else Check(firstResults is > 0 and <= 120, "invented callback12 selects results within its bounded short journey");
                Check(audio.Tracks.SequenceEqual(new[] { SeqId.CREDITS, SeqId.RESULTS }) && audio.Stops == 1,
                    "page switches CREDITS to RESULTS exactly once and retires the prior music epoch");
                var resultPixels = ReadCanvas();
                Check(resultPixels.LitPixels > 0 && readbacks.Count > 1,
                    "actual results background/text produces measured canvas pixels");
                Check(!creditsHashes.Contains(resultPixels.Sha256), "results readback differs from the observed credits pictures");
                Bounds ok = Element("campaign_ending_source_ok");
                Check(ok.Width > 0 && ok.Height > 0 && ok.X >= 0 && ok.Y >= 0
                    && ok.X + ok.Width <= width + 1 && ok.Y + ok.Height <= height + 1,
                    "source OK retains positive visible bounds inside the actual framebuffer");
                Check(host.FocusedElement() == "campaign_ending_source_ok", "source results moves real native focus onto OK");
                Drain(); double cx = ok.X + ok.Width / 2, cy = ok.Y + ok.Height / 2;
                host.Input.PointerButton(0, cx, cy, true); host.Input.PointerButton(0, cx, cy, false); host.Update();
                int acknowledgments = 0;
                while (host.TryTakeIntent(out var intent))
                {
                    if (intent.Kind != RmlUiIntentKind.ResultsAction || intent.Argument != 0) continue;
                    Check(intent.Document == document && page.Handle(intent), "actual native OK pointer intent invokes the source acknowledgment");
                    Check(!page.Handle(intent), "duplicate acknowledgment cannot return the same ending twice");
                    acknowledgments++;
                }
                Check(acknowledgments == 1 && page.Closed && audio.Stops == 2 && job.Disposals == 1,
                    "actual acknowledgment closes the page and releases presentation, catalog job and music once");
                Check(page.TryTakeReturn(out var returned) && returned == receipt && !page.TryTakeReturn(out _),
                    "application return contains exactly the immutable displayed receipt once");
                page.Dispose(); page.Refresh();
                Check(!host.IsAlive(document) && audio.Stops == 2 && job.Disposals == 1,
                    "document disposal retires the canvas without stale publication or duplicate audio cleanup");
                page = null;
                branches.Add(new { GoodEnding = good, ResultsPage = good ? 49 : 50, SimulatedTicks = endTick,
                    FirstResultsTick = firstResults, MaximumCreditLitPixels = maximumCreditLit,
                    ResultsLitPixels = resultPixels.LitPixels, ResultsPixelSha256 = resultPixels.Sha256,
                    Acknowledgments = acknowledgments, AudioStarts = audio.Tracks.Count, audio.Stops,
                    ImmutableReceiptReturnedOnce = true, Readbacks = readbacks });
            }
            if (!owned) FallbackAndLoadingCancellation(catalog);
            Check(GL.GetError() == ErrorCode.NoError, "real native credits path leaves no GL error");
        }
        catch (Exception error) { RecordFailure(error); }
        finally
        {
            try { page?.Dispose(); } catch (Exception error) { RecordFailure(error); }
            foreach (var input in inputs)
                try { input.After = HashSource(root, input.Pin); } catch (Exception error) { RecordFailure(error); }
            try { coreAfter = HashAssembly(corePath); toolAfter = HashAssembly(toolPath); }
            catch (Exception error) { RecordFailure(error); }
        }
        bool sourceUnchanged = !owned || (inputs.Count == 33 && inputs.All(p => p.Before == p.After));
        bool binariesUnchanged = coreBefore != null && toolBefore != null && coreBefore == coreAfter && toolBefore == toolAfter;
        bool passed = errorType == null && branches.Count == 2 && sourceUnchanged && binariesUnchanged;
        Console.WriteLine(JsonSerializer.Serialize(new { Schema = 1, Passed = passed, OwnedSource = owned, Checks = checks,
            Width = width, Height = height, Draws = draws, ErrorType = errorType, ErrorMessageSha256 = errorMessageSha256,
            MetadataOutputOnly = true, ExistingHostAndContext = true, RealEndingPageUsed = true,
            InjectedClockAndSilentAudio = true, SourceUnchanged = sourceUnchanged, BinariesUnchanged = binariesUnchanged,
            RenderingActivated = true, GLActivated = true, SceneCreated = false,
            CoreSha256Before = coreBefore, CoreSha256After = coreAfter, ToolSha256Before = toolBefore, ToolSha256After = toolAfter,
            SourcePayloadsWritten = false, GameTextExported = false, PixelsExported = false,
            ScreenshotsWritten = false, SavesTouched = false, Branches = branches,
            SourceInputs = inputs.Select(p => new { FileId = p.Pin.Path, p.Pin.Bytes, Sha256Before = p.Before, Sha256After = p.After }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (!passed) throw new InvalidOperationException("Credits presentation GL acceptance failed; see metadata receipt.");

        void Check(bool condition, string label) { checks++; if (!condition) throw new InvalidOperationException(label); }
        void RecordFailure(Exception error)
        { if (errorType == null) { errorType = error.GetType().Name; errorMessageSha256 = Hash(Encoding.UTF8.GetBytes(error.Message)); } }
        bool HasBounds(string id) => host.TryGetElementBounds(page!.Document, id, out _, out _, out float w, out float h) && w > 0 && h > 0;
        Bounds Element(string id)
        {
            Check(host.TryGetElementBounds(page!.Document, id, out float x, out float y, out float w, out float h), "current document exposes measured element bounds");
            return new(x, y, w, h);
        }
        void CheckCanvasLayout()
        {
            Bounds canvas = Element("campaign_ending_source_canvas");
            Check(canvas.Width > 0 && canvas.Height > 0 && canvas.X >= 0 && canvas.Y >= 0
                && canvas.X + canvas.Width <= width + 1 && canvas.Y + canvas.Height <= height + 1,
                "source canvas has positive measured bounds inside the actual framebuffer");
        }
        void Frame()
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
            GL.Disable(EnableCap.ScissorTest); GL.ClearColor(0, 0, 0, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            host.Update(); RmlUiOpenGlFrame.Draw(host, width, height); draws++;
            page?.Refresh();
        }
        Pixels ReadCanvas()
        {
            Bounds b = Element("campaign_ending_source_canvas"); float scale = MathF.Min(b.Width / 256, b.Height / 384);
            int x = (int)MathF.Ceiling(b.X + (b.Width - 256 * scale) / 2), y = (int)MathF.Ceiling(b.Y + (b.Height - 384 * scale) / 2);
            int w = (int)MathF.Floor(256 * scale) - 1, h = (int)MathF.Floor(384 * scale) - 1;
            Check(w > 0 && h > 0 && x >= 0 && y >= 0 && x + w <= width && y + h <= height,
                "aspect-fitted source pixel region lies inside native canvas");
            byte[] rgb = new byte[checked(w * h * 3)]; int alignment = GL.GetInteger(GetPName.PackAlignment);
            try { GL.PixelStore(PixelStoreParameter.PackAlignment, 1); GL.ReadPixels(x, height - y - h, w, h, PixelFormat.Rgb, PixelType.UnsignedByte, rgb); }
            finally { GL.PixelStore(PixelStoreParameter.PackAlignment, alignment); }
            int lit = 0; for (int i = 0; i < rgb.Length; i += 3) if (rgb[i] > 20 || rgb[i + 1] > 20 || rgb[i + 2] > 20) lit++;
            string hash = Hash(rgb); Array.Clear(rgb); return new(lit, hash, w, h);
        }
        void Drain() { for (int i = 0; host.TryTakeIntent(out _); i++) if (i > 256) throw new InvalidOperationException("Native intent queue is unbounded."); }
        void FallbackAndLoadingCancellation(Catalog catalog)
        {
            double now = 0; int loads = 0; var audio = new SilentAudio();
            page = new(host, pages, CreditsFrameFixtures.Summary(), audio, original: false,
                load: () => { loads++; throw new InvalidOperationException("OFF must never load"); }, now: () => now);
            page.Open(); Frame();
            Check(loads == 0 && page.CreditsPhase == null && !HasBounds("campaign_ending_source_canvas")
                && audio.Tracks.SequenceEqual(new[] { SeqId.RESULTS }), "default OFF performs no catalog load/canvas activation and keeps existing results audio");
            Check(page.Back() && page.TryTakeReturn(out _) && !page.TryTakeReturn(out _), "legacy summary return remains once-only");
            page.Dispose(); page = null;
            audio = new(); page = new(host, pages, CreditsFrameFixtures.Summary(), audio, original: true, load: () => null, now: () => now);
            page.Open(); Frame();
            Check(page.CreditsPhase == Phase.Fallback && !HasBounds("campaign_ending_source_canvas") && !page.Closed
                && audio.Tracks.SequenceEqual(new[] { SeqId.RESULTS }), "unavailable source falls back wholly without consuming the receipt");
            page.Dispose(); page = null;
            var pending = new PendingJob(); audio = new();
            page = new(host, pages, CreditsFrameFixtures.Summary(), audio, original: true, load: () => pending, now: () => now);
            page.Open(); Frame(); Check(page.CreditsPhase == Phase.Loading && audio.Tracks.Count == 0, "pending source remains silent and cancelable");
            Check(page.Back() && pending.Disposals == 1 && page.TryTakeReturn(out _) && !page.TryTakeReturn(out _), "loading cancellation retires the job and returns once");
            pending.Complete(catalog); now = 1000; page.Refresh(); Frame();
            Check(page.Closed && page.CreditsPhase == Phase.Retired && audio.Tracks.Count == 0 && !page.TryTakeReturn(out _),
                "late canceled completion cannot publish canvas, start audio or emit a stale return");
            page.Dispose(); page = null;
        }
    }

    private static Catalog SyntheticCatalog()
    {
        var fixture = CreditsFrameFixtures.Make(navigate: true, animate: false);
        var widget = CreditsRenderFixture.Model(); CreditsRenderFixture.Word(widget.Bytes, 0, 5);
        CreditsRenderFixture.Half(widget.Bytes, widget.At["paletteData"] + 2, 0x7fff);
        var model = CampaignCreditsModelSource.Read(widget.Bytes);
        var atlas = CreditsRenderFixture.Model(format: 6, width: 128, height: 256);
        CreditsRenderFixture.Half(atlas.Bytes, atlas.At["paletteData"] + 2, 0x7fff);
        var font = CampaignCreditsFontSource.Read(CampaignCreditsModelSource.Read(atlas.Bytes), Enumerable.Repeat((byte)4, 480).ToArray(), new byte[480]);
        var pages = fixture.Document.Pages.Select(p =>
        {
            if (p.Index == 6) return p with { Timers = p.Timers.Select(t => t with { DelayFx = 60 * 4096 }).ToArray() };
            if (p.Index is not (49 or 50)) return p;
            var items = p.Items.ToArray(); var action = new Source.Action(1, 0, 0, 0, 0, 0, 0, 0,
                new[] { new Source.Call(0, 206) }, ushort.MaxValue, 255, 0);
            items[^1] = items[^1] with { Actions = new[] { action } }; return p with { Items = items };
        }).ToArray();
        var d = fixture.Document;
        var document = new Source.Document(d.MarmSha256, d.TextSha256, d.HeaderField4, d.HeaderField6, d.HeaderField7,
            d.HeaderField16, d.GlobalPageCount, d.GlobalWidgetCount, d.GlobalStringCount, d.PointerAndRecordVisits,
            pages, d.Widgets.ToDictionary(p => p.Key, p => p.Value), d.Strings.ToDictionary(p => p.Key, p => p.Value));
        CampaignCreditsBackgroundSource.Snapshot Picture(ushort color)
        { byte[] b = new byte[CampaignCreditsBackgroundSource.EncodedBytes]; CreditsRenderFixture.Half(b, 0, color); return CampaignCreditsBackgroundSource.Read(b); }
        return CreditsFrameFixtures.Create(fixture with { Document = document,
            Models = fixture.Models.Keys.ToDictionary(p => p, _ => model, StringComparer.Ordinal), Font = font,
            Pictures = new[] { Picture(0x03e0), Picture(0x7c00), Picture(0x001f), Picture(0x03ff) } });
    }
    private sealed class CompletedJob(Catalog catalog) : ICampaignCreditsLoadJob
    {
        public Task<Catalog> Completion { get; } = Task.FromResult(catalog);
        internal int Disposals;
        public bool Current => Disposals == 0;
        public void Dispose() { Disposals++; }
    }
    private sealed class PendingJob : ICampaignCreditsLoadJob
    {
        private readonly TaskCompletionSource<Catalog> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Catalog> Completion => _completion.Task;
        internal int Disposals;
        public bool Current => Disposals == 0;
        public void Complete(Catalog catalog) => _completion.SetResult(catalog);
        public void Dispose() { Disposals++; }
    }
    private sealed class SilentAudio : ICampaignAudioSink
    {
        internal readonly List<SeqId> Tracks = new(); internal int Stops;
        private long _epoch;
        public long Start(SeqId sequence) { Tracks.Add(sequence); return _epoch = Tracks.Count; }
        public bool Owns(SeqId sequence, long epoch) => epoch != 0 && epoch == _epoch && Tracks[^1] == sequence;
        public void Tick() { }
        public void Stop(SeqId sequence, long epoch)
        { if (!Owns(sequence, epoch)) throw new InvalidOperationException("Silent audio epoch mismatch."); Stops++; _epoch = 0; }
        public void Cue(MphRead.Mods.Sound.UiFeedbackCue cue)
        { if (cue != MphRead.Mods.Sound.UiFeedbackCue.CampaignEnding) throw new InvalidOperationException("Unexpected synthetic ending cue."); }
    }
    private static string HashSource(string root, CampaignCreditsLoad.Input pin)
    {
        string path = Path.GetFullPath(Path.Combine(root, pin.Path.Replace('/', Path.DirectorySeparatorChar)));
        LocalImportBundle.CheckPath(path, root); using var stream = CampaignCreditsSourceFile.Open(path, CancellationToken.None);
        if (pin.Bytes is < 1 or > CampaignCreditsLoad.MaxReadBytes || stream.Length != pin.Bytes) throw new InvalidDataException("Owned hash extent differs.");
        string hash = HashStream(stream, pin.Bytes); LocalImportBundle.CheckPath(path, root); return hash;
    }
    private static string HashAssembly(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > 268435456) throw new InvalidDataException("Binary hash extent is invalid.");
        return HashStream(stream, stream.Length);
    }
    private static string HashStream(Stream stream, long length)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[65536];
        try
        {
            long left = length;
            while (left > 0) { int n = stream.Read(buffer, 0, (int)Math.Min(left, buffer.Length)); if (n == 0) throw new EndOfStreamException(); hash.AppendData(buffer, 0, n); left -= n; }
            if (stream.ReadByte() != -1 || stream.Length != length) throw new InvalidDataException("Hash input changed extent.");
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally { Array.Clear(buffer); }
    }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
