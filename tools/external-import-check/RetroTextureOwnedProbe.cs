using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;

// Optional read-only owned-source acceptance. No decoded pixels or source
// payloads are written, and this has no renderer, material or GPU authority.
internal static class RetroTextureOwnedProbe
{
    private const int ImageOutputBudget = 2 * 1024 * 1024;
    private const long AggregateOutputBudget = 128L * 1024 * 1024;
    private const int MaximumTextureEntriesPerArena = 16384;
    private sealed record Admitted(int Ordinal, uint Resource, RetroTextureSource.Layout Layout,
        int ResourceBytes, long PakBytes, bool Compressed, string SourceSha256);
    private sealed record ArenaInput(EchoesArenaSources.Arena Arena, Admitted[] Entries);
    private static readonly int[] Bits3 = ExpansionTable(3), Bits4 = ExpansionTable(4),
        Bits5 = ExpansionTable(5), Bits6 = ExpansionTable(6);

    internal static int Run(string ownedSource)
    {
        string? before = null, after = null, gameId = null, archiveSha1 = null;
        long admittedBytes = 0;
        var arenas = new List<object>();
        int decodedEntries = 0;
        long comparedPixels = 0;
        string? error = null;
        try
        {
            before = HashFile(ownedSource);
            using var disc = GameCubeDiscReader.Open(ownedSource);
            gameId = disc.GameId;
            Require(disc.Game == ExternalGameKind.Echoes && disc.GameId == "G2ME01"
                && disc.Revision == 0 && disc.DiscNumber == 0,
                "The pinned USA revision-0 Echoes GameCube source is required.");
            var requirement = ExternalSourceRequirements.For(disc.Game).Single(r => r.Path == "Metroid6.pak");
            var proof = ExternalSourceRequirements.Verify(disc, requirement);
            archiveSha1 = proof.ActualSha1;
            Require(proof.Status == ExternalArchiveStatus.Verified, "Pinned Echoes Metroid6.pak verification failed.");
            using var archive = disc.OpenRead(requirement.Path);
            using var pak = new RetroPakReader(archive);
            var admitted = new List<ArenaInput>();

            // Admission is a complete first pass: no top RGBA allocation happens
            // until ALL map entries, including duplicates, fit the job budget.
            foreach (var arena in EchoesArenaSources.All)
            {
                Require(pak.Contains(arena.ResourceId) && pak.Entry(arena.ResourceId).Type == "MREA",
                    "Known arena MREA is absent or has the wrong resource type.");
                var area = EchoesMreaSections.Parse(pak.ReadResource(arena.ResourceId));
                Require(area.GeometrySection >= 0, "Arena material section is absent.");
                ReadOnlySpan<byte> materialSet = area.Section(area.GeometrySection).Span;
                Require(materialSet.Length >= 4, "Arena texture table is truncated.");
                uint count = BinaryPrimitives.ReadUInt32BigEndian(materialSet);
                Require(count <= MaximumTextureEntriesPerArena && 4L + 4L * count <= materialSet.Length,
                    "Arena texture table exceeds its count or source bounds.");
                var entries = new Admitted[(int)count];
                for (int i = 0; i < entries.Length; i++)
                {
                    uint id = BinaryPrimitives.ReadUInt32BigEndian(materialSet.Slice(4 + 4 * i, 4));
                    Require(pak.Contains(id) && pak.Entry(id).Type == "TXTR", "Material entry is not a TXTR resource.");
                    var entry = pak.Entry(id);
                    Require(entry.Length <= RetroTextureSource.MaxSourceBytes, "Encoded PAK entry exceeds the probe budget.");
                    byte[] source = pak.ReadResource(id);
                    var layout = RetroTextureSource.Inspect(source, ImageOutputBudget);
                    admittedBytes = checked(admittedBytes + layout.TopRgbaBytes);
                    Require(admittedBytes <= AggregateOutputBudget, "Aggregate top-RGBA admission budget exceeded.");
                    entries[i] = new(i, id, layout, source.Length, entry.Length, entry.Compressed, Hash(source));
                }
                admitted.Add(new(arena, entries));
            }
            Require(admitted.Count == 6 && admitted.Sum(a => a.Entries.Length) == 390
                && admitted.SelectMany(a => a.Entries).Select(e => e.Resource).Distinct().Count() == 385
                && admittedBytes == 104884224,
                "Owned six-arena inventory differs from the pinned 390-entry/385-resource witness.");

            // Decode one entry at a time. Neither source nor RGBA arrays are
            // retained in the report or accumulated across the six arenas.
            foreach (var arena in admitted)
            {
                var images = new List<object>();
                foreach (var entry in arena.Entries)
                {
                    byte[] source = pak.ReadResource(entry.Resource);
                    Require(source.Length == entry.ResourceBytes && Hash(source) == entry.SourceSha256,
                        "Source resource changed after admission.");
                    var decoded = RetroTextureSource.DecodeTop(source, ImageOutputBudget);
                    Require(SameLayout(entry.Layout, decoded.Layout), "Decoded layout differs from admission.");
                    string pixelsSha = Hash(decoded.Pixels);
                    string oracleSha = CompareCoordinateOracle(source, decoded);
                    Require(pixelsSha == oracleSha, "Independent coordinate-oracle hash differs.");
                    comparedPixels += (long)entry.Layout.Width * entry.Layout.Height;
                    decodedEntries++;
                    images.Add(new
                    {
                        entry.Ordinal, ResourceId = entry.Resource.ToString("x8"), entry.Layout.Format,
                        FormatName = entry.Layout.Format == 10 ? "CMPR" : "RGB5A3",
                        entry.Layout.Width, entry.Layout.Height, entry.Layout.MipCount,
                        EncodedResourceBytes = entry.ResourceBytes, EncodedPakBytes = entry.PakBytes,
                        entry.Compressed, entry.SourceSha256, entry.Layout.TopRgbaBytes,
                        entry.Layout.PayloadEnd, entry.Layout.PaddingBytes,
                        PaddingPolicy = entry.Layout.PaddingBytes == 0 ? "exact-complete-mip-chain" : "exact-align32-uniform-ff",
                        Mips = entry.Layout.Mips, TopRgbaSha256 = pixelsSha, OracleRgbaSha256 = oracleSha,
                        AllTopPixelsCompared = true
                    });
                }
                arenas.Add(new
                {
                    arena.Arena.Key, arena.Arena.Name, MreaResourceId = arena.Arena.ResourceId.ToString("x8"),
                    Entries = arena.Entries.Length,
                    EncodedResourceBytes = arena.Entries.Sum(e => (long)e.ResourceBytes),
                    TopRgbaBytes = arena.Entries.Sum(e => (long)e.Layout.TopRgbaBytes),
                    Formats = arena.Entries.GroupBy(e => e.Layout.Format).ToDictionary(g => g.Key.ToString(), g => g.Count()),
                    MipCounts = arena.Entries.GroupBy(e => e.Layout.MipCount).ToDictionary(g => g.Key.ToString(), g => g.Count()),
                    ExactEnds = arena.Entries.Count(e => e.Layout.PaddingBytes == 0),
                    AlignedFfEnds = arena.Entries.Count(e => e.Layout.PaddingBytes != 0), Images = images
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or OverflowException or KeyNotFoundException)
        { error = ex.GetType().Name + ": " + ex.Message; }
        finally
        {
            try { after = HashFile(ownedSource); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { error ??= ex.GetType().Name + ": " + ex.Message; }
        }
        bool unchanged = before != null && before == after;
        bool passed = error == null && unchanged && decodedEntries == 390;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Schema = 1, Passed = passed, Error = error, GameId = gameId, ArchiveSha1 = archiveSha1,
            RetroTextureSource.PixelContract, ImageOutputBudget, AggregateOutputBudget,
            AdmittedTopRgbaBytes = admittedBytes, DecodedEntries = decodedEntries, UniqueResources = passed ? 385 : (int?)null,
            ComparedTopPixels = comparedPixels, SourceSha256Before = before, SourceSha256After = after,
            SourceUnchanged = unchanged, Oracle = "independent-logical-texel-addressing-all-top-pixels-v1",
            HardwareAcceptance = false, GpuAcceptance = false, MaterialRenderingAcceptance = false,
            SourcePayloadsWritten = false, Arenas = arenas
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }

    private static bool SameLayout(RetroTextureSource.Layout a, RetroTextureSource.Layout b) =>
        a.Format == b.Format && a.Width == b.Width && a.Height == b.Height && a.MipCount == b.MipCount
        && a.PayloadEnd == b.PayloadEnd && a.PaddingBytes == b.PaddingBytes && a.TopRgbaBytes == b.TopRgbaBytes
        && a.Mips.SequenceEqual(b.Mips);

    // This oracle walks logical raster coordinates, directly addressing the
    // encoded word/subblock. It shares no tile-loop, palette or channel helper
    // with the production decoder. The equations implement the named factual
    // pixel contract; no external decoder implementation is transplanted.
    private static string CompareCoordinateOracle(ReadOnlySpan<byte> source, RetroTextureSource.Decoded decoded)
    {
        var layout = decoded.Layout;
        var row = new byte[checked(layout.Width * 4)];
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int y = 0; y < layout.Height; y++)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                uint color = layout.Format == 10 ? CmprAt(source, layout.Width, x, y) : Rgb5A3At(source, layout.Width, x, y);
                for (int channel = 0; channel < 4; channel++) row[x * 4 + channel] = (byte)(color >> (8 * channel));
            }
            Require(row.AsSpan().SequenceEqual(decoded.Pixels.Slice(y * row.Length, row.Length)),
                "Source pixel differential failed at logical row " + y + ".");
            digest.AppendData(row);
        }
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static uint CmprAt(ReadOnlySpan<byte> source, int width, int x, int y)
    {
        int tile = (y / 8) * ((width + 7) / 8) + x / 8;
        int block = 2 * ((y % 8) / 4) + (x % 8) / 4;
        int at = checked(12 + 32 * tile + 8 * block);
        int first = Word(source, at), second = Word(source, at + 2);
        int choice = (source[at + 4 + y % 4] >> (6 - 2 * (x % 4))) & 3;
        uint result = choice == 3 && first <= second ? 0u : 0xff000000u;
        for (int channel = 0; channel < 3; channel++)
        {
            int a = EndpointChannel(first, channel), b = EndpointChannel(second, channel);
            int value = choice switch
            {
                0 => a, 1 => b,
                2 => first > second ? (5 * a + 3 * b) / 8 : (a + b) / 2,
                _ => first > second ? (3 * a + 5 * b) / 8 : (a + b) / 2
            };
            result |= (uint)value << (8 * channel);
        }
        return result;
    }

    private static uint Rgb5A3At(ReadOnlySpan<byte> source, int width, int x, int y)
    {
        int tile = (y / 4) * ((width + 3) / 4) + x / 4;
        int at = checked(12 + 32 * tile + 2 * (4 * (y % 4) + x % 4));
        int word = Word(source, at);
        bool opaque = word >= 0x8000;
        int[] expansion = opaque ? Bits5 : Bits4;
        int bits = opaque ? 5 : 4;
        uint result = (uint)(opaque ? 255 : Bits3[word / 4096]) << 24;
        for (int channel = 0; channel < 3; channel++)
            result |= (uint)expansion[(word >> ((2 - channel) * bits)) & ((1 << bits) - 1)] << (8 * channel);
        return result;
    }

    private static int EndpointChannel(int word, int channel) => channel switch
    {
        0 => Bits5[word / 2048], 1 => Bits6[(word / 32) % 64], _ => Bits5[word % 32]
    };

    private static int Word(ReadOnlySpan<byte> source, int at) => 256 * source[at] + source[at + 1];
    private static int[] ExpansionTable(int bits)
    {
        var table = new int[1 << bits];
        for (int value = 0; value < table.Length; value++)
            for (int outputBit = 0; outputBit < 8; outputBit++)
                table[value] = 2 * table[value] + ((value >> (bits - 1 - outputBit % bits)) & 1);
        return table;
    }

    private static string HashFile(string path) { using var input = File.OpenRead(path); return HashStream(input); }
    private static string HashStream(Stream input) => Convert.ToHexStringLower(SHA256.HashData(input));
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Require(bool condition, string detail)
    { if (!condition) throw new InvalidDataException(detail); }
}
