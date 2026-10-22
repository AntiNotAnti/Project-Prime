using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackgroundSource = MphRead.Mods.Campaign.CampaignCreditsBackgroundSource;
using FontSource = MphRead.Mods.Campaign.CampaignCreditsFontSource;
using ModelSource = MphRead.Mods.Campaign.CampaignCreditsModelSource;
using Source = MphRead.Mods.Campaign.CampaignCreditsSource;
using Layout = MphRead.Mods.Campaign.CampaignCreditsTextLayout;

// Opt-in, read-only AMHE1 CPU bitmap/font admission. Only metadata leaves this
// probe; the independent Python struct/floor oracle is pinned below, not read at runtime.
internal static class OwnedBitmapProbe
{
    private const int Arm9Maximum = 12 * 1024 * 1024;
    private const int WidthOffset = 0xC020C, VerticalOffset = 0xC03EC, EmbeddedFontOffset = 0xC7F5C;
    private const string WidthsSha256 = "0d9b1ceea061faef29e326e3c7cea2adad62acd1c8bb18415eb614e29b507851";
    private const string OffsetsSha256 = "4a62e6363c173c9b294c39a263c2cd457bff36c3fc74ddac596626f43e95fb0c";
    private const string CombinedMetricsSha256 = "1619a5a096d200058a3781424e7d68a287821ad4fe435a6b45e00cffab3b5a95";
    private sealed record Pin(string FileId, string Role, int Bytes, int Maximum, string Sha256, string? RgbaSha256 = null);
    private static readonly Pin[] Pins =
    {
        new("_bin/arm9.bin", "Arm9", 909912, Arm9Maximum,
            "1b70b078ec1b026004c89272acf619e7510e60fd294aa776b7bda48733ae850b"),
        new("models/hudfont_Model.bin", "FontModel", 33412, ModelSource.MaxInputBytes,
            "3f89b432c47b96e52d0837fdd1fa2eb1165df9309665640a0bea02208300e7ef"),
        new("sourceimages/bg/good1.bin", "Background", 49664, BackgroundSource.EncodedBytes,
            "257122d6e9cacfec2e67ce5d2cacb0fec2e77580dc24478c4e3950dd93b61783",
            "eb4ef07c7c6c3df9a786f40e4ea60f5d281138fa1db94fb6b4f801d7eb92e8ba"),
        new("sourceimages/bg/good2.bin", "Background", 49664, BackgroundSource.EncodedBytes,
            "46c7fca5db4627bb5149ff3bf362a52cac2ba81aeba8fdfa192a170d7c5299ef",
            "33080e54673d19b5a6de378f1f3a11f16fb786d2f419f3b34efea96b1c226ea8"),
        new("sourceimages/bg/bad1.bin", "Background", 49664, BackgroundSource.EncodedBytes,
            "fec414ff7557c6194ffe041e10d4d2d2da22414438052fae3f398af5702418f2",
            "cd7a437c115ec6e84af3123fb89d7a6ddcca494b892ba1bc31a1fc7e182a1bac"),
        new("sourceimages/bg/bad2.bin", "Background", 49664, BackgroundSource.EncodedBytes,
            "ef92fd690be9b62556c0c3f1b1e90642f8f6f5967c4886f172ac1edbfdfd495f",
            "afb1a386a47becdaa17c2042142652d31fc590fb6c266c09990816fb0a67df49"),
        new("frontend/metroidhunters.bin", "Marm", 512232, Source.MaxInputBytes,
            "1140e510b420a9da5f4547154f0c164511a697ccaf5a249c0652625806a9d059"),
        new("frontend/metroidhunters_text_en.bin", "Text", 30604, Source.MaxInputBytes,
            "742e6819be3e9f8b55be65e66cc8a4d9a8aefc0a93d2f1e4813bb663d262d114")
    };
    private sealed class InputProof
    {
        internal required Pin Pin;
        internal required string Path;
        internal required string Before;
        internal string? After;
        internal long Bytes;
    }

    internal static int Run(string ownedRoot)
    {
        string stage = "Initialize";
        int checks = 0, glyphLookups = 0, pixelLookups = 0;
        int textStates = 0, textLines = 0, glyphQuads = 0, maximumStateQuads = 0, colorSamples = 0;
        string? marmSha256 = null, textSha256 = null;
        var inputs = new List<InputProof>();
        var backgrounds = new List<object>();
        FontSource.Snapshot? font = null;
        string? assemblyBefore = null, assemblyAfter = null, errorType = null, errorMessageSha256 = null;
        string assemblyPath = typeof(BackgroundSource).Assembly.Location;
        try
        {
            assemblyBefore = HashFile(assemblyPath, 256 * 1024 * 1024);
            string root = Path.GetFullPath(ownedRoot);
            stage = "ReadArm9AndFontModel";
            byte[] arm9 = ReadTracked(root, Pins[0]);
            byte[] fontBytes = ReadTracked(root, Pins[1]);
            Check(arm9.AsSpan(EmbeddedFontOffset, fontBytes.Length).SequenceEqual(fontBytes)
                && Hash(arm9.AsSpan(EmbeddedFontOffset, fontBytes.Length)) == Pins[1].Sha256,
                "Extracted font differs from its exact ARM9 embedded slice.");
            byte[] widths = arm9.AsSpan(WidthOffset, FontSource.GlyphCount).ToArray();
            byte[] offsets = arm9.AsSpan(VerticalOffset, FontSource.GlyphCount).ToArray();
            Check(Hash(widths) == WidthsSha256 && Hash(offsets) == OffsetsSha256,
                "Pinned individual glyph metric identities differ.");
            ModelSource.Snapshot model = ModelSource.Read(fontBytes);
            Check(model.SourceSha256 == Pins[1].Sha256 && model.ByteLength == 33412
                && model.NodeCount == 1 && model.MaterialCount == 1 && model.MeshCount == 1
                && model.TextureCount == 1 && model.MatrixCount == 1 && model.PixelCount == 32768
                && model.VertexCount == 4 && model.TriangleCount == 2 && model.MaximumNodeDepth == 1,
                "Pinned font atlas model metadata differs.");
            stage = "BindDetachedFontMetrics";
            font = FontSource.Read(model, widths, offsets);
            Check(font.ModelSha256 == Pins[1].Sha256 && font.MetricsSha256 == CombinedMetricsSha256
                && font.GlyphCount == 480 && FontSource.Columns == 16 && FontSource.Rows == 32
                && FontSource.CellSize == 8, "Admitted font identity or cell layout differs.");
            // The public metric lookups must remain detached from mutable caller arrays.
            Array.Clear(widths); Array.Clear(offsets); Array.Clear(fontBytes);
            for (int glyph = 0; glyph < 480; glyph++)
            {
                Check(font.Width(glyph) == arm9[WidthOffset + glyph]
                    && font.VerticalOffset(glyph) == unchecked((sbyte)arm9[VerticalOffset + glyph]),
                    "Detached glyph lookup differs from the raw width/signed-offset entry.");
                glyphLookups++;
            }
            stage = "DecodeBackgrounds";
            foreach (Pin pin in Pins.Skip(2).Take(4))
            {
                byte[] encoded = ReadTracked(root, pin);
                BackgroundSource.Snapshot background = BackgroundSource.Read(encoded);
                Array.Clear(encoded);
                Check(background.SourceSha256 == pin.Sha256 && background.ByteLength == 49664
                    && background.Width == 256 && background.Height == 192 && background.PixelCount == 49152
                    && background.Rgba.Length == 196608, "Admitted background identity or dimensions differ.");
                string decodedSha256 = Hash(background.Rgba.Span);
                Check(decodedSha256 == pin.RgbaSha256, "Every decoded RGBA byte differs from the independent floor-conversion oracle.");
                bool pixelsMatch = true;
                var rgba = background.Rgba.Span;
                for (int y = 0; y < background.Height; y++) for (int x = 0; x < background.Width; x++)
                {
                    var pixel = background.PixelAt(x, y);
                    int at = (y * background.Width + x) * 4;
                    pixelsMatch &= pixel.Red == rgba[at] && pixel.Green == rgba[at + 1]
                        && pixel.Blue == rgba[at + 2] && pixel.Alpha == rgba[at + 3] && pixel.Alpha == 255;
                    pixelLookups++;
                }
                Check(pixelsMatch, "Public background pixel lookup differs from admitted opaque RGBA storage.");
                backgrounds.Add(new { pin.FileId, background.SourceSha256, background.ByteLength,
                    background.Width, background.Height, background.PixelCount,
                    RgbaBytes = background.Rgba.Length, RgbaSha256 = decodedSha256,
                    IndependentOracleMatched = true, PixelLookups = background.PixelCount });
            }
            stage = "LayoutSelectedTextStates";
            var document = Source.Read(ReadTracked(root, Pins[6]), ReadTracked(root, Pins[7]));
            marmSha256 = document.MarmSha256; textSha256 = document.TextSha256;
            Check(marmSha256 == Pins[6].Sha256 && textSha256 == Pins[7].Sha256
                && document.Pages.Select(p => p.Index).SequenceEqual(new[] { 6, 7, 8, 9, 10, 11, 12, 49, 50 })
                && document.Pages.Sum(p => p.Items.Count) == 74 && document.Strings.Count == 48,
                "Pinned selected frontend identity or inventory differs.");
            foreach (var page in document.Pages) foreach (var item in page.Items) foreach (var state in item.States)
            {
                if (state.Text is not { } style) continue;
                string text = document.Strings[style.StringId].ContentLatin1;
                var layout = Layout.Build(font, text, style, item.XFx, item.YFx, item.DepthFx);
                Check(layout.FontModelSha256 == font.ModelSha256 && layout.FontMetricsSha256 == font.MetricsSha256
                    && layout.TextSha256 == Hash(Encoding.Latin1.GetBytes(text)),
                    "Text layout lost its detached font or content identity.");
                Check(text.Length <= Layout.MaxTextBytes && layout.LineCount is >= 1 and <= Layout.MaxLines
                    && layout.LineWidths.All(w => w >= 0 && w <= text.Length * 9)
                    && layout.Glyphs.Count <= text.Length, "Text layout exceeded its bounded geometry inventory.");
                Check(layout.Glyphs.All(g => g.GlyphId is >= 0 and < 480
                    && float.IsFinite(g.X) && float.IsFinite(g.Y) && float.IsFinite(g.Depth)
                    && Unit(g.U0) && Unit(g.V0) && Unit(g.U1) && Unit(g.V1)
                    && g.U1 > g.U0 && g.V1 > g.V0), "Text glyph coordinate, atlas index or UV is invalid.");
                long duration = (style.DurationFx + 4095L) / 4096;
                foreach (long frame in new[] { 0L, 1L, Math.Max(0, duration - 1), duration, duration + 1, long.MaxValue }.Distinct())
                {
                    var color = layout.ColorAt(state.Code, frame);
                    Check(Unit(color.X) && Unit(color.Y) && Unit(color.Z) && Unit(color.W),
                        "Pure text fade/pulse color escaped finite unit RGBA range.");
                    colorSamples++;
                }
                textStates++; textLines += layout.LineCount; glyphQuads += layout.Glyphs.Count;
                maximumStateQuads = Math.Max(maximumStateQuads, layout.Glyphs.Count);
            }
            Check(inputs.Count == 8 && backgrounds.Count == 4 && glyphLookups == 480 && pixelLookups == 196608
                && textStates > 0 && glyphQuads > 0, "Owned bitmap/font/text inventory differs.");
        }
        catch (Exception error)
        {
            errorType = error.GetType().Name;
            errorMessageSha256 = Hash(Encoding.UTF8.GetBytes(error.Message));
        }
        finally
        {
            foreach (InputProof input in inputs)
            {
                try { input.After = HashFile(input.Path, input.Pin.Maximum); }
                catch (Exception error)
                {
                    errorType ??= error.GetType().Name;
                    errorMessageSha256 ??= Hash(Encoding.UTF8.GetBytes(error.Message));
                }
            }
            try { assemblyAfter = HashFile(assemblyPath, 256 * 1024 * 1024); }
            catch (Exception error)
            {
                errorType ??= error.GetType().Name;
                errorMessageSha256 ??= Hash(Encoding.UTF8.GetBytes(error.Message));
            }
        }
        bool sourceUnchanged = inputs.All(i => i.Before == i.After);
        bool assemblyUnchanged = assemblyBefore != null && assemblyBefore == assemblyAfter;
        bool passed = errorType == null && sourceUnchanged && assemblyUnchanged
            && inputs.Count == 8 && backgrounds.Count == 4 && glyphLookups == 480 && pixelLookups == 196608
            && textStates > 0 && glyphQuads > 0;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Schema = 1, Edition = "AMHE1", Passed = passed, Stage = stage, Checks = checks,
            ErrorType = errorType, ErrorMessageSha256 = errorMessageSha256,
            MetadataOutputOnly = true, CpuBackgroundsDecoded = backgrounds.Count,
            FontMetricsBound = font != null, GlyphLookups = glyphLookups, PixelLookups = pixelLookups,
            PureTextLayoutsEvaluated = textStates, TextLines = textLines, GlyphQuads = glyphQuads,
            GlyphTriangles = glyphQuads * 2, MaximumStateGlyphQuads = maximumStateQuads,
            TextColorSamples = colorSamples, MarmSha256 = marmSha256, TextResourceSha256 = textSha256,
            TimelineAdvanced = false, TextLayoutPixelParityClaimed = false,
            IndependentOracle = "python-struct-le-rgb5-floor255-opaque-v1",
            RenderingActivated = false, GLActivated = false, SceneCreated = false,
            GlobalFontTouched = false, UiAudioOrSaveTouched = false,
            SourcePayloadsWritten = false, ImagesExported = false, MetricsArraysExported = false,
            SourceUnchanged = sourceUnchanged, AssemblySha256Before = assemblyBefore,
            AssemblySha256After = assemblyAfter, AssemblyUnchanged = assemblyUnchanged,
            Font = font == null ? null : new { font.ModelSha256, font.MetricsSha256, font.GlyphCount,
                WidthsSha256, OffsetsSha256, EmbeddedFontSha256 = Pins[1].Sha256,
                WidthSliceOffset = WidthOffset, VerticalSliceOffset = VerticalOffset,
                EmbeddedFontSliceOffset = EmbeddedFontOffset, EmbeddedFontSliceBytes = 33412 },
            Backgrounds = backgrounds,
            SourceInputs = inputs.Select(i => new { i.Pin.FileId, i.Pin.Role, i.Bytes,
                Sha256Before = i.Before, Sha256After = i.After }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;

        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidDataException(message);
        }
        byte[] ReadTracked(string root, Pin pin)
        {
            string path = Resolve(root, pin.FileId);
            string before = HashFile(path, pin.Maximum);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = stream.Length;
            if (length < 1 || length > pin.Maximum) throw new InvalidDataException("Owned input exceeds its bounded extent.");
            inputs.Add(new() { Pin = pin, Path = path, Before = before, Bytes = length });
            Check(length == pin.Bytes && before == pin.Sha256, "Pinned owned input identity differs.");
            byte[] bytes = new byte[(int)length];
            stream.ReadExactly(bytes);
            Check(stream.ReadByte() == -1 && stream.Length == length && Hash(bytes) == before,
                "Owned input changed during bounded admission.");
            return bytes;
        }
    }
    private static bool Unit(float value) => float.IsFinite(value) && value is >= 0 and <= 1;
    private static string Resolve(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Owned source path escaped its supplied root.");
        return full;
    }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string HashFile(string path, int maximum)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 1 || stream.Length > maximum)
            throw new InvalidDataException("Owned hash input exceeds its bounded extent.");
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
