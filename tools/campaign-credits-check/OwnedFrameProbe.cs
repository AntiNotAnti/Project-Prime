using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Campaign;
using Source = MphRead.Mods.Campaign.CampaignCreditsSource;
using ModelSource = MphRead.Mods.Campaign.CampaignCreditsModelSource;
using Binding = MphRead.Mods.Campaign.CampaignCreditsAnimationBinding;
using Duration = MphRead.Mods.Campaign.CampaignCreditsAnimationDuration;
using FontSource = MphRead.Mods.Campaign.CampaignCreditsFontSource;
using BackgroundSource = MphRead.Mods.Campaign.CampaignCreditsBackgroundSource;
using Catalog = MphRead.Mods.Campaign.CampaignCreditsCatalog;
using FrameOwner = MphRead.Mods.Campaign.CampaignCreditsFrameOwner;
using Sampler = MphRead.Mods.Campaign.CampaignCreditsWidgetSampler;

// Opt-in owned AMHE1 CPU frame traversal. Only numeric metadata and source/tool
// hashes are printed. No game text, vertices, glyphs, pixels, model files, saves,
// GL resources, audio or screenshot output belongs to this probe.
internal static class OwnedFrameProbe
{
    private const int Arm9Maximum = 12 * 1024 * 1024;
    private const int WidthOffset = 0xC020C, VerticalOffset = 0xC03EC, EmbeddedFontOffset = 0xC7F5C;
    private const string MetricsSha256 = "1619a5a096d200058a3781424e7d68a287821ad4fe435a6b45e00cffab3b5a95";
    private sealed record Pin(string FileId, string Role, int Bytes, int Maximum, string Sha256);
    private static readonly Pin[] Pins =
    {
        new("logo_screen/MAYA/flare2_Model.bin", "Model", 2564, ModelSource.MaxInputBytes, "c9cc6ec7c38d97c0b56fa2724fc6e6e070022497e346c06344351e3b58df9c6c"),
        new("logo_screen/MAYA/flare3_Model.bin", "Model", 2564, ModelSource.MaxInputBytes, "efeff3196f27de722818892607f5955894b1538d174a6c53ade478f7f28e4a55"),
        new("logo_screen/MAYA/logo2_Model.bin", "Model", 42640, ModelSource.MaxInputBytes, "7682bffcfe9fec8300bf4a44d5ebcd16f5431383ebddc4d6ac0cdf381ec897d6"),
        new("logo_screen/MAYA/logo3_Model.bin", "Model", 38632, ModelSource.MaxInputBytes, "2393ea89063137312d71f86edc9f5cd6cbae526ad320863bda7636718f871acd"),
        new("main menu/toplogoR_Model.bin", "Model", 900, ModelSource.MaxInputBytes, "d5133b37d5d9dedb1104a13c150cf94a160675ee28f025c4a6833f4e9b7e45ce"),
        new("tostart/logofinal_Model.bin", "Model", 54100, ModelSource.MaxInputBytes, "a0ce0bcb402b2952e51fbaf586583c6c321d7e00dc480a1a97f8af1aecebfabc"),
        new("gameoptions/ok_Model.bin", "Model", 1684, ModelSource.MaxInputBytes, "2724cb0c747292f8cd55a9abbb4644e382e706a0367dcc1b187fdb0915609a99"),
        new("models/hudfont_Model.bin", "FontModel", 33412, ModelSource.MaxInputBytes, "3f89b432c47b96e52d0837fdd1fa2eb1165df9309665640a0bea02208300e7ef"),
        new("gameoptions/ok_Hidden_Idle_Anim.bin", "Animation", 304, Duration.MaxInputBytes, "193971d218b7342e45052fcabd0910d0d66095fb34d9b4a995a758f7ce114b9e"),
        new("gameoptions/ok_Idle_Anim.bin", "Animation", 296, Duration.MaxInputBytes, "998d81b6885fac0cf8e553677db59d5f3814202ce2ae6de0f91a026e738ddd0c"),
        new("gameoptions/ok_Idle_Hidden_Anim.bin", "Animation", 300, Duration.MaxInputBytes, "175bb330503edbaa513a99cbb37007b705dd93b73efa0f5deed6b4ede3caea29"),
        new("gameoptions/ok_Selected_Anim.bin", "Animation", 296, Duration.MaxInputBytes, "998d81b6885fac0cf8e553677db59d5f3814202ce2ae6de0f91a026e738ddd0c"),
        new("gameoptions/ok_Selected_Hidden_Anim.bin", "Animation", 320, Duration.MaxInputBytes, "a0dabc9b0e7152f4aada1ba5bb3dc3f92b3170b7c1f115812bbbef222b01a443"),
        new("logo_screen/MAYA/flare2_Hidden_Idle_Anim.bin", "Animation", 1352, Duration.MaxInputBytes, "a1c9af11acafd6ed560f05938cf927cba21f5d388335d8885db70f53872d3d8a"),
        new("logo_screen/MAYA/flare2_Idle_Hidden_Anim.bin", "Animation", 400, Duration.MaxInputBytes, "9fd3cbdb70b524ea11c70f4443e11e51a7fa65715ab86ba42cebbf6f3b52cc3d"),
        new("logo_screen/MAYA/flare3_Hidden_Idle_Anim.bin", "Animation", 1352, Duration.MaxInputBytes, "f3dca70097f7fe550734df84e3153337c9b99b090fc4762d47a20de7700f6ca7"),
        new("logo_screen/MAYA/flare3_Idle_Hidden_Anim.bin", "Animation", 400, Duration.MaxInputBytes, "728d246aa884c3403dd3e2dea60206bd34c1db5197a17b6c108737be73a3e361"),
        new("logo_screen/MAYA/logo2_Hidden_Idle_Anim.bin", "Animation", 2400, Duration.MaxInputBytes, "1d87b65e1bd1cf5a111a7e26bbb6254f391cdde44d7eb6b1ac45f1bc581d7e10"),
        new("logo_screen/MAYA/logo2_Idle_Hidden_Anim.bin", "Animation", 1636, Duration.MaxInputBytes, "f34f6366534cbc119a418902e221d99819b9a9e49c910ce79b74d06dc8369376"),
        new("logo_screen/MAYA/logo3_Hidden_Idle_Anim.bin", "Animation", 2400, Duration.MaxInputBytes, "eddd200c2f864ebfe1fc751986e023b9940f61f7ac8dd6cb82533b63e55927f4"),
        new("logo_screen/MAYA/logo3_Idle_Hidden_Anim.bin", "Animation", 1636, Duration.MaxInputBytes, "0c33ea91069518c193563deca2d44dd88c4e2d632d09eb3377620f2077d83af8"),
        new("main menu/toplogoR_Hidden_Idle_Anim.bin", "Animation", 352, Duration.MaxInputBytes, "fd3ae105e15af2b57e672664454f79b4d35c3b3785a34b43559cedf1f1f6e21e"),
        new("main menu/toplogoR_Idle_Anim.bin", "Animation", 296, Duration.MaxInputBytes, "99f3e05d430d4526dd4f2e37ab10db0b6d076629233028de2e5c6ab865c1af49"),
        new("main menu/toplogoR_Idle_Hidden_Anim.bin", "Animation", 344, Duration.MaxInputBytes, "09c4d9edfdd3c33f4feaba369ed2433b097a7b3be6ee33129eff4430cab192bc"),
        new("tostart/logofinal_Hidden_Idle_Anim.bin", "Animation", 4692, Duration.MaxInputBytes, "58b44907bf0731b67d7299b8227f215140745e72517a2ff853aa91510e30b06b"),
        new("tostart/logofinal_Idle_Anim.bin", "Animation", 1256, Duration.MaxInputBytes, "922ba68c9412dd4a56289aeed390c6644274743f5cbc76ebb96b693baf85b72f"),
        new("_bin/arm9.bin", "Arm9", 909912, Arm9Maximum, "1b70b078ec1b026004c89272acf619e7510e60fd294aa776b7bda48733ae850b"),
        new("sourceimages/bg/good1.bin", "Background", 49664, BackgroundSource.EncodedBytes, "257122d6e9cacfec2e67ce5d2cacb0fec2e77580dc24478c4e3950dd93b61783"),
        new("sourceimages/bg/good2.bin", "Background", 49664, BackgroundSource.EncodedBytes, "46c7fca5db4627bb5149ff3bf362a52cac2ba81aeba8fdfa192a170d7c5299ef"),
        new("sourceimages/bg/bad1.bin", "Background", 49664, BackgroundSource.EncodedBytes, "fec414ff7557c6194ffe041e10d4d2d2da22414438052fae3f398af5702418f2"),
        new("sourceimages/bg/bad2.bin", "Background", 49664, BackgroundSource.EncodedBytes, "ef92fd690be9b62556c0c3f1b1e90642f8f6f5967c4886f172ac1edbfdfd495f"),
        new("frontend/metroidhunters.bin", "Marm", 512232, Source.MaxInputBytes, "1140e510b420a9da5f4547154f0c164511a697ccaf5a249c0652625806a9d059"),
        new("frontend/metroidhunters_text_en.bin", "Text", 30604, Source.MaxInputBytes, "742e6819be3e9f8b55be65e66cc8a4d9a8aefc0a93d2f1e4813bb663d262d114"),
    };
    private sealed class InputProof
    {
        internal required Pin Pin;
        internal required string Path;
        internal required string Before;
        internal string? After;
    }
    private sealed class PageProof
    {
        public int Page { get; init; }
        public int Samples { get; set; }
        public long TriangleSamples { get; set; }
        public int MaximumTriangles { get; set; }
        public int MaximumBackgrounds { get; set; }
    }

    internal static int Run(string ownedRoot)
    {
        string stage = "Initialize";
        int checks = 0, repeatedCaptures = 0, immutableSnapshots = 0;
        var inputs = new Dictionary<string, InputProof>(StringComparer.Ordinal);
        var branches = new List<object>();
        var pinByPath = Pins.ToDictionary(p => p.FileId, StringComparer.Ordinal);
        Catalog? catalog = null;
        Dictionary<string, object[]>? cachesBefore = null, cachesAfter = null;
        bool cachesUnchanged = false;
        string? errorType = null, errorMessageSha256 = null;
        string corePath = typeof(ModelSource).Assembly.Location, toolPath = typeof(OwnedFrameProbe).Assembly.Location;
        string? coreBefore = null, coreAfter = null, toolBefore = null, toolAfter = null;
        try
        {
            coreBefore = HashFile(corePath, 256 * 1024 * 1024);
            toolBefore = HashFile(toolPath, 256 * 1024 * 1024);
            cachesBefore = SnapshotCaches();
            string root = Path.GetFullPath(ownedRoot);
            stage = "ReadFrontend";
            byte[] marm = ReadTracked(root, "frontend/metroidhunters.bin");
            byte[] localized = ReadTracked(root, "frontend/metroidhunters_text_en.bin");
            var document = Source.Read(marm, localized);
            Array.Clear(marm); Array.Clear(localized);
            Check(document.Pages.Select(p => p.Index).SequenceEqual(new[] { 6, 7, 8, 9, 10, 11, 12, 49, 50 })
                && document.Widgets.Count == 39 && document.Strings.Count == 48, "Selected frontend inventory differs.");
            Check(document.Widgets.Values.All(w => w.Files.Count == 2 && w.Files[0].Path != null && w.Files[1].Path != null),
                "Selected widget path inventory differs.");
            string[] modelPaths = document.Widgets.Values.Select(w => w.Files[0].Path!).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            string[] animationPaths = document.Widgets.Values.Select(w => w.Files[1].Path!).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            Check(modelPaths.SequenceEqual(Pins.Where(p => p.Role == "Model").Select(p => p.FileId).Order(StringComparer.Ordinal))
                && animationPaths.SequenceEqual(Pins.Where(p => p.Role == "Animation").Select(p => p.FileId).Order(StringComparer.Ordinal)),
                "Referenced model/animation paths differ from the fixed owned profile.");
            stage = "ReadAdmittedWidgetModels";
            var models = new Dictionary<string, ModelSource.Snapshot>(StringComparer.Ordinal);
            foreach (string path in modelPaths)
            {
                byte[] bytes = ReadTracked(root, path);
                models.Add(path, ModelSource.Read(bytes));
                Array.Clear(bytes);
            }
            stage = "ReadBoundWidgetAnimations";
            var animations = new Dictionary<string, Binding.Snapshot>(StringComparer.Ordinal);
            foreach (string path in animationPaths)
            {
                string[] owners = document.Widgets.Values.Where(w => w.Files[1].Path == path)
                    .Select(w => w.Files[0].Path!).Distinct(StringComparer.Ordinal).ToArray();
                Check(owners.Length == 1, "Animation is associated with conflicting model paths.");
                byte[] bytes = ReadTracked(root, path);
                var duration = Duration.Read(bytes);
                animations.Add(path, Binding.Read(models[owners[0]], duration, bytes));
                Array.Clear(bytes);
            }
            stage = "ReadDetachedFont";
            byte[] arm9 = ReadTracked(root, "_bin/arm9.bin"), fontBytes = ReadTracked(root, "models/hudfont_Model.bin");
            Check(arm9.AsSpan(EmbeddedFontOffset, fontBytes.Length).SequenceEqual(fontBytes),
                "Font model differs from its pinned ARM9 slice.");
            var font = FontSource.Read(ModelSource.Read(fontBytes), arm9.AsSpan(WidthOffset, FontSource.GlyphCount),
                arm9.AsSpan(VerticalOffset, FontSource.GlyphCount));
            Check(font.MetricsSha256 == MetricsSha256 && font.GlyphCount == 480, "Font metric identity differs.");
            Array.Clear(arm9); Array.Clear(fontBytes);
            stage = "ReadDetachedBackgrounds";
            var images = new Dictionary<string, BackgroundSource.Snapshot>(StringComparer.Ordinal);
            foreach (Pin pin in Pins.Where(p => p.Role == "Background"))
            {
                byte[] bytes = ReadTracked(root, pin.FileId);
                images.Add(pin.FileId, BackgroundSource.Read(bytes));
                Array.Clear(bytes);
            }
            stage = "CreatePureCatalog";
            catalog = new Catalog(document, models, animations, font, new Catalog.Backgrounds(
                images["sourceimages/bg/good1.bin"], images["sourceimages/bg/good2.bin"],
                images["sourceimages/bg/bad1.bin"], images["sourceimages/bg/bad2.bin"]));
            Check(catalog.Document.MarmSha256 == document.MarmSha256 && catalog.Document.TextSha256 == document.TextSha256
                && catalog.Frames.Frames.Count == 39 && catalog.Widgets.Count == 39 && catalog.Models.Count == 8
                && catalog.TexturePairs.Count > 0 && catalog.Font.MetricsSha256 == MetricsSha256,
                "Catalog inventory or bound source identity differs.");
            // Caller dictionaries can be discarded without changing catalog ownership.
            models.Clear(); animations.Clear(); images.Clear();
            foreach (bool good in new[] { false, true })
            {
                stage = good ? "GoodCompleteFrameTraversal" : "BadCompleteFrameTraversal";
                branches.Add(VerifyBranch(catalog, good));
            }
            Check(inputs.Count == 33 && branches.Count == 2, "Full owned frame probe inventory differs.");
        }
        catch (Exception error)
        {
            errorType = error.GetType().Name;
            errorMessageSha256 = Hash(Encoding.UTF8.GetBytes(error.Message));
        }
        finally
        {
            foreach (InputProof input in inputs.Values)
            {
                try { input.After = HashFile(input.Path, input.Pin.Maximum); }
                catch (Exception error) { RecordFailure(error); }
            }
            try
            {
                coreAfter = HashFile(corePath, 256 * 1024 * 1024);
                toolAfter = HashFile(toolPath, 256 * 1024 * 1024);
                cachesAfter = SnapshotCaches();
                cachesUnchanged = cachesBefore != null && cachesBefore.Count == cachesAfter.Count
                    && cachesBefore.All(p => cachesAfter.TryGetValue(p.Key, out var values) && p.Value.SequenceEqual(values));
            }
            catch (Exception error) { RecordFailure(error); }
        }
        bool sourceUnchanged = inputs.Count == 33 && inputs.Values.All(i => i.Before == i.After);
        bool assembliesUnchanged = coreBefore != null && toolBefore != null && coreBefore == coreAfter && toolBefore == toolAfter;
        bool passed = errorType == null && sourceUnchanged && assembliesUnchanged && cachesUnchanged && branches.Count == 2;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Schema = 1, Edition = "AMHE1", Passed = passed, Stage = stage, Checks = checks,
            ErrorType = errorType, ErrorMessageSha256 = errorMessageSha256,
            MetadataOutputOnly = true, CpuFrameTransportOnly = true,
            TimelineBranches = branches.Count, RepeatedCaptureChecks = repeatedCaptures,
            ImmutableSnapshotChecks = immutableSnapshots,
            Catalog = catalog == null ? null : new { ModelCount = catalog.Models.Count,
                WidgetCount = catalog.Widgets.Count, TexturePairCount = catalog.TexturePairs.Count,
                catalog.Budget, catalog.Budget.SamplerPixels,
                FontModelSha256 = catalog.Font.ModelSha256, FontMetricsSha256 = catalog.Font.MetricsSha256 },
            MarmSha256 = catalog?.Document.MarmSha256, TextSha256 = catalog?.Document.TextSha256,
            CachesUnchanged = cachesUnchanged,
            CacheCountsBefore = cachesBefore?.ToDictionary(p => p.Key, p => p.Value.Length),
            CacheCountsAfter = cachesAfter?.ToDictionary(p => p.Key, p => p.Value.Length),
            SourceUnchanged = sourceUnchanged, CoreAssemblySha256Before = coreBefore, CoreAssemblySha256After = coreAfter,
            ToolAssemblySha256Before = toolBefore, ToolAssemblySha256After = toolAfter, AssembliesUnchanged = assembliesUnchanged,
            RenderingActivated = false, GLActivated = false, SceneCreated = false,
            UiAudioOrSaveTouched = false, SourcePayloadsWritten = false,
            GameTextExported = false, CoordinatesExported = false, PixelsExported = false,
            Branches = branches,
            SourceInputs = inputs.Values.OrderBy(i => i.Pin.FileId, StringComparer.Ordinal).Select(i => new
                { i.Pin.FileId, i.Pin.Role, i.Pin.Bytes, Sha256Before = i.Before, Sha256After = i.After }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;

        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidDataException(message);
        }
        void RecordFailure(Exception error)
        {
            errorType ??= error.GetType().Name;
            errorMessageSha256 ??= Hash(Encoding.UTF8.GetBytes(error.Message));
        }
        byte[] ReadTracked(string root, string fileId)
        {
            Check(pinByPath.TryGetValue(fileId, out Pin? pin), "Unpinned owned source path was requested.");
            if (pin == null) throw new InvalidDataException("Owned input pin is absent.");
            string path = Resolve(root, fileId), before = HashFile(path, pin.Maximum);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Check(stream.Length == pin.Bytes && before == pin.Sha256, "Pinned owned source identity differs.");
            Check(inputs.TryAdd(fileId, new() { Pin = pin, Path = path, Before = before }), "Owned source was admitted twice.");
            byte[] bytes = new byte[pin.Bytes]; stream.ReadExactly(bytes);
            Check(stream.ReadByte() == -1 && stream.Length == pin.Bytes && Hash(bytes) == before,
                "Owned input changed during bounded admission.");
            return bytes;
        }
        object VerifyBranch(Catalog admitted, bool good)
        {
            using var timeline = new CampaignCreditsTimeline(admitted.Document, admitted.Frames, good);
            // Invented immutable result values exercise substitution without constructing,
            // reading or writing a live StorySave or capturing actual user statistics.
            var receipt = new CampaignEndingSummary(1, good ? CampaignEndingKind.Complete : CampaignEndingKind.Incomplete,
                true, 73, 8, 12, 34, 56) { PlayTicks = 3661L * 60, GoreaTicks = 185L * 60 };
            var owner = new FrameOwner(admitted, timeline, receipt);
            var pages = new Dictionary<int, PageProof>();
            int samples = 0, maximumTriangles = 0, maximumBackgrounds = 0;
            long triangleSamples = 0;
            bool ownershipReturned = false;
            Action? verifyRetainedGeometry = null;
            try
            {
                Check(owner.Textures.Count == admitted.TexturePairs.Count && admitted.TexturePairs.All(pair =>
                    owner.Textures.TryGetValue(pair.Key, out var pixels) && pixels.Width == pair.Width
                        && pixels.Height == pair.Height && pixels.Rgba.Length == checked(pair.Width * pair.Height * 4)),
                    "Decoded CPU texture inventory differs from admitted pair identity or dimensions.");
                var initial = owner.Capture();
                var initialTriangles = initial.Triangles.ToArray(); var initialBackgrounds = initial.Backgrounds.ToArray();
                Check(initial.Page == 6 && initial.PageFrame == 0 && !initial.Closed && initial.Failure == null,
                    "Initial CPU frame differs from the original timeline.");
                for (int tick = 0; tick <= 3000; tick++)
                {
                    if (tick > 0) Check(timeline.Tick(), "Timeline failed before complete CPU frame traversal.");
                    var state = timeline.Snapshot();
                    var frame = owner.Capture();
                    Check(frame.Page == state.Page && frame.PageFrame == state.PageFrame && frame.Closed == state.Closed
                        && frame.Failure == state.Failure && !frame.Closed && frame.Failure == null,
                        "CPU frame diverged from its existing timeline owner.");
                    Check(frame.Backgrounds.Count == (frame.Page is 49 or 50 ? 2 : 0),
                        "Credits/results CPU background count differs.");
                    Check(frame.Triangles.Count <= FrameOwner.MaxFrameTriangles
                        && frame.Triangles.All(triangle => Finite(triangle.A) && Finite(triangle.B) && Finite(triangle.C)
                            && float.IsFinite(triangle.Depth) && Unit(triangle.Color.X) && Unit(triangle.Color.Y)
                            && Unit(triangle.Color.Z) && Unit(triangle.Color.W) && triangle.Color.W > 0
                            && ((triangle.Texture.TextureId == -1 && triangle.Texture.PaletteId == -1)
                                || owner.Textures.ContainsKey(triangle.Texture))),
                        "CPU frame geometry has a nonfinite/domain/budget or unknown texture binding.");
                    for (int triangle = 1; triangle < frame.Triangles.Count; triangle++)
                        Check(frame.Triangles[triangle - 1].Depth <= frame.Triangles[triangle].Depth,
                            "CPU source frame is not in finite far-to-near depth order.");
                    Check(frame.Backgrounds.All(background => float.IsFinite(background.X) && float.IsFinite(background.Y)
                        && background.Width == 256 && background.Height == 192 && background.Image.Width == 256
                        && background.Image.Height == 192), "CPU background rectangle or image dimensions differ.");
                    if (frame.Page is 49 or 50)
                    {
                        string stem = good ? "good" : "bad";
                        Check(frame.Page == (good ? 49 : 50)
                            && frame.Backgrounds[0].Image.SourceSha256 == pinByPath[$"sourceimages/bg/{stem}1.bin"].Sha256
                            && frame.Backgrounds[1].Image.SourceSha256 == pinByPath[$"sourceimages/bg/{stem}2.bin"].Sha256,
                            "Results selected a background from the wrong ending or screen.");
                    }
                    if (!pages.TryGetValue(frame.Page, out PageProof? page)) pages.Add(frame.Page, page = new() { Page = frame.Page });
                    page.Samples++; page.TriangleSamples += frame.Triangles.Count;
                    page.MaximumTriangles = Math.Max(page.MaximumTriangles, frame.Triangles.Count);
                    page.MaximumBackgrounds = Math.Max(page.MaximumBackgrounds, frame.Backgrounds.Count);
                    maximumTriangles = Math.Max(maximumTriangles, frame.Triangles.Count);
                    maximumBackgrounds = Math.Max(maximumBackgrounds, frame.Backgrounds.Count);
                    triangleSamples += frame.Triangles.Count; samples++;
                    if (verifyRetainedGeometry == null && frame.Triangles.Count > 0)
                    {
                        var retained = frame;
                        var retainedTriangles = frame.Triangles.ToArray();
                        var retainedBackgrounds = frame.Backgrounds.ToArray();
                        verifyRetainedGeometry = () => Check(retained.Triangles.SequenceEqual(retainedTriangles)
                            && retained.Backgrounds.SequenceEqual(retainedBackgrounds),
                            "Advancing source age mutated a retained nonempty CPU frame.");
                    }
                    if (tick % 37 == 0 || tick == 3000)
                    {
                        var savedTriangles = frame.Triangles.ToArray(); var savedBackgrounds = frame.Backgrounds.ToArray();
                        var repeated = owner.Capture();
                        Check(repeated.Page == frame.Page && repeated.PageFrame == frame.PageFrame
                            && repeated.Triangles.SequenceEqual(savedTriangles) && repeated.Backgrounds.SequenceEqual(savedBackgrounds),
                            "Capture without a timeline tick changed CPU output.");
                        repeatedCaptures++;
                        Check(frame.Triangles.SequenceEqual(savedTriangles) && frame.Backgrounds.SequenceEqual(savedBackgrounds)
                            && initial.Triangles.SequenceEqual(initialTriangles) && initial.Backgrounds.SequenceEqual(initialBackgrounds),
                            "A later capture mutated an already returned CPU snapshot.");
                        verifyRetainedGeometry?.Invoke();
                        immutableSnapshots++;
                    }
                }
                var final = timeline.Snapshot();
                Check(pages.Keys.Order().SequenceEqual(new[] { 6, 7, 8, 9, 10, 11, 12, good ? 49 : 50 }.Order())
                    && samples == 3001 && maximumTriangles > 0 && maximumBackgrounds == 2
                    && verifyRetainedGeometry != null
                    && final.Page == (good ? 49 : 50) && final.PageFrame == 390 && final.PendingPage == null && final.SettledFrame >= 0,
                    "Complete source pages or settled results CPU frames were not sampled.");
            }
            finally { owner.Dispose(); }
            // Frame retirement must not tick, close, dispose or consume return from the timeline.
            var afterDispose = timeline.Snapshot();
            Check(!afterDispose.Closed && afterDispose.PageFrame == 390 && timeline.Tick()
                && timeline.Snapshot().PageFrame == 391 && !timeline.TryTakeReturn(),
                "Frame disposal took ownership of the independent timeline.");
            int button = good ? 12 : 11, resultPage = good ? 49 : 50;
            Check(timeline.InvokeItemAction(resultPage, button, 0) && timeline.TryTakeReturn() && !timeline.TryTakeReturn(),
                "Independent timeline acknowledgment was changed by frame disposal.");
            ownershipReturned = true;
            return new { GoodEnding = good, ResultsPage = resultPage, TimelineTicks = 3000, FrameSamples = samples,
                TriangleSamples = triangleSamples, MaximumTriangles = maximumTriangles, MaximumBackgrounds = maximumBackgrounds,
                Pages = pages.Values.OrderBy(p => p.Page).ToArray(), InventedReceiptUsed = true,
                TimelineOwnershipPreserved = ownershipReturned, ReturnConsumedExactlyOnce = true };
        }
    }
    private static bool Unit(float value) => float.IsFinite(value) && value is >= 0 and <= 1;
    private static bool Finite(Sampler.Vertex vertex) => float.IsFinite(vertex.Position.X)
        && float.IsFinite(vertex.Position.Y) && float.IsFinite(vertex.Position.Z)
        && float.IsFinite(vertex.Texcoord.X) && float.IsFinite(vertex.Texcoord.Y);
    private static Dictionary<string, object[]> SnapshotCaches()
    {
        var result = new Dictionary<string, object[]>(StringComparer.Ordinal);
        foreach (string name in new[] { "_modelCache", "_fhModelCache", "_retiredRoomModels", "_effects", "_particleDefs" })
        {
            var field = typeof(Read).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidDataException("CPU cache witness is unavailable.");
            var values = field.GetValue(null) as IEnumerable
                ?? throw new InvalidDataException("CPU cache witness has unsupported shape.");
            result.Add(name, values.Cast<object>().ToArray());
        }
        return result;
    }
    private static string Resolve(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Owned input escaped its supplied root.");
        return full;
    }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string HashFile(string path, int maximum)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 1 || stream.Length > maximum) throw new InvalidDataException("Owned hash input exceeds its extent.");
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
