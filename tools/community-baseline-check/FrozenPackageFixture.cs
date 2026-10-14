using System.Globalization;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MphRead.Mods.MapGen;

// The reviewed main baseline was authored with protocol 53. Runtime package
// publication still uses the current protocol and its unmodified archive hash.
// This separate fixture pins only that authored compatibility metadata; all
// other manifest bytes, project/assets and ZIP encoding must match the golden.
internal static class FrozenPackageFixture
{
    internal const int Protocol = 53;

    internal sealed record Result(int Protocol, string PackageHash, bool EncodingRoundTripExact, bool UntouchedZipBytesExact,
        bool ContentMutationRejected, bool ManifestMutationRejected, bool WrongProtocolRejected,
        bool FrozenProtocolMutationRejected);

    internal static Result Verify(string currentPackage, string root, string golden, int runtimeProtocol)
    {
        if (runtimeProtocol < Protocol)
            throw new InvalidDataException("Runtime predates the reviewed frozen fixture.");
        var entries = Read(currentPackage);
        // Reconstruct the current archive BEFORE pinning any metadata. This
        // detects packaging order/metadata/encoding regressions rather than
        // hiding them behind the fixture's independent ZIP writer.
        string roundTrip = Path.Combine(root, "current-encoding-roundtrip.ppmap");
        Write(entries, roundTrip);
        if (!File.ReadAllBytes(currentPackage).SequenceEqual(File.ReadAllBytes(roundTrip)))
            throw new InvalidDataException("Fixture writer no longer reproduces exact production ZIP encoding.");

        entries["manifest.json"] = Pin(entries["manifest.json"], runtimeProtocol);
        string reference = Path.Combine(root, "frozen-protocol53.ppmap");
        Write(entries, reference);
        VerifyWireDelta(currentPackage, reference);
        RequireGolden(reference, golden);
        var currentIdentity = MapContentIdentity.FromPackage(currentPackage);
        var frozenIdentity = MapContentIdentity.FromPackage(reference);
        if (currentIdentity with { PackageHash = frozenIdentity.PackageHash } != frozenIdentity)
            throw new InvalidDataException("Pinned compatibility metadata changed map/content identity.");
        if (currentIdentity.Matches(currentIdentity with
            { PackageHash = currentIdentity.PackageHash with { A = currentIdentity.PackageHash.A ^ 1 } }))
            throw new InvalidDataException("Public identity accepted a different exact package hash.");

        var changedContent = new SortedDictionary<string, byte[]>(entries, StringComparer.Ordinal);
        byte[] tile = (byte[])entries["tile.tex"].Clone();
        tile[^1] ^= 1;
        changedContent["tile.tex"] = tile;
        string corrupt = Path.Combine(root, "negative-content.ppmap");
        Write(changedContent, corrupt);
        ExpectInvalid(() => RequireGolden(corrupt, golden));
        ExpectInvalid(() => { using var rejected = new MapPackageReader(corrupt); });

        // An unknown manifest property remains valid for the public strict
        // reader, but must still fail exact immutable archive identity. Pin()
        // preserves raw JSON instead of dropping unknown fields by reserializing.
        var changedManifest = new SortedDictionary<string, byte[]>(entries, StringComparer.Ordinal);
        string json = Encoding.UTF8.GetString(entries["manifest.json"]);
        int end = json.LastIndexOf('}');
        changedManifest["manifest.json"] = Encoding.UTF8.GetBytes(
            json.Insert(end, ",\n  \"fixtureMutation\": true\n"));
        string metadata = Path.Combine(root, "negative-manifest.ppmap");
        Write(changedManifest, metadata);
        using (var valid = new MapPackageReader(metadata)) { _ = valid.ReadProject(); }
        ExpectInvalid(() => RequireGolden(metadata, golden));
        ExpectInvalid(() => Pin(Read(currentPackage)["manifest.json"], runtimeProtocol + 1));
        var wrongMinimum = new SortedDictionary<string, byte[]>(entries, StringComparer.Ordinal);
        wrongMinimum["manifest.json"] = Encoding.UTF8.GetBytes(json.Replace(
            "\"minimumProtocol\": 53", "\"minimumProtocol\": 52", StringComparison.Ordinal));
        string wrongProtocol = Path.Combine(root, "negative-minimum-protocol.ppmap");
        Write(wrongMinimum, wrongProtocol);
        using (var valid = new MapPackageReader(wrongProtocol)) { _ = valid.ReadProject(); }
        ExpectInvalid(() => RequireGolden(wrongProtocol, golden));
        return new(Protocol, MapBuildFingerprint.HashFile(reference), true, true, true, true, true, true);
    }

    private static byte[] Pin(byte[] manifest, int expectedProtocol)
    {
        using var parsed = JsonDocument.Parse(manifest);
        var properties = parsed.RootElement.EnumerateObject()
            .Where(p => p.Name == "minimumProtocol").ToArray();
        if (properties.Length != 1 || properties[0].Value.GetInt32() != expectedProtocol)
            throw new InvalidDataException("Production manifest does not declare the runtime protocol.");
        string json = Encoding.UTF8.GetString(manifest);
        string marker = "\"minimumProtocol\": " + expectedProtocol.ToString(CultureInfo.InvariantCulture);
        int start = json.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0 || json.IndexOf(marker, start + marker.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidDataException("Frozen manifest protocol spelling changed.");
        return Encoding.UTF8.GetBytes(json.Remove(start, marker.Length).Insert(start,
            "\"minimumProtocol\": " + Protocol.ToString(CultureInfo.InvariantCulture)));
    }

    private static SortedDictionary<string, byte[]> Read(string package)
    {
        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        using var archive = ZipFile.OpenRead(package);
        foreach (var entry in archive.Entries)
        {
            using var input = entry.Open();
            using var bytes = new MemoryStream();
            input.CopyTo(bytes);
            entries.Add(entry.FullName, bytes.ToArray());
        }
        return entries;
    }

    // Test-fixture ZIP32 wire proof. Archive comments, extras and flags are part
    // of metadata equality. Only local-header offsets necessarily move when the
    // manifest's compressed size changes; every untouched compressed byte stays.
    private sealed record WireEntry(string Name, byte[] Central, byte[] Local, byte[] Compressed);

    private static void VerifyWireDelta(string current, string frozen)
    {
        var before = Wire(File.ReadAllBytes(current));
        var after = Wire(File.ReadAllBytes(frozen));
        if (before.Length != after.Length)
            throw new InvalidDataException("Frozen fixture changed ZIP entry count.");
        for (int i = 0; i < before.Length; i++)
        {
            var a = before[i]; var b = after[i];
            bool manifest = a.Name == "manifest.json";
            if (a.Name != b.Name)
                throw new InvalidDataException("Frozen fixture changed ZIP entry order.");
            // A relocated entry's central directory pointer is the only allowed
            // difference for an untouched entry. Manifest CRC/lengths also move.
            a.Central.AsSpan(42, 4).Clear(); b.Central.AsSpan(42, 4).Clear();
            if (manifest)
            {
                a.Central.AsSpan(16, 12).Clear(); b.Central.AsSpan(16, 12).Clear();
                a.Local.AsSpan(14, 12).Clear(); b.Local.AsSpan(14, 12).Clear();
            }
            if (!a.Central.SequenceEqual(b.Central) || !a.Local.SequenceEqual(b.Local)
                || (!manifest && !a.Compressed.SequenceEqual(b.Compressed)))
                throw new InvalidDataException("Frozen fixture changed unrelated ZIP bytes: " + a.Name);
        }
    }

    private static WireEntry[] Wire(byte[] bytes)
    {
        static ushort U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at, 2));
        static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at, 4));
        int end = bytes.Length - 22;
        if (end < 0 || U32(bytes, end) != 0x06054b50 || U16(bytes, end + 20) != 0
            || U16(bytes, end + 4) != 0 || U16(bytes, end + 6) != 0
            || U16(bytes, end + 8) != U16(bytes, end + 10))
            throw new InvalidDataException("Fixture ZIP footer/encoding changed.");
        int count = U16(bytes, end + 10), cursor = checked((int)U32(bytes, end + 16));
        var entries = new List<WireEntry>();
        int expectedLocal = 0;
        for (int i = 0; i < count; i++)
        {
            if (U32(bytes, cursor) != 0x02014b50 || (U16(bytes, cursor + 8) & 8) != 0)
                throw new InvalidDataException("Fixture ZIP central header/descriptor policy changed.");
            int local = checked((int)U32(bytes, cursor + 42));
            if (local != expectedLocal || U32(bytes, local) != 0x04034b50)
                throw new InvalidDataException("Fixture ZIP local entry order changed.");
            int centralLength = 46 + U16(bytes, cursor + 28) + U16(bytes, cursor + 30) + U16(bytes, cursor + 32);
            int localLength = 30 + U16(bytes, local + 26) + U16(bytes, local + 28);
            int compressedLength = checked((int)U32(bytes, cursor + 20));
            string name = Encoding.UTF8.GetString(bytes, cursor + 46, U16(bytes, cursor + 28));
            entries.Add(new(name, bytes.AsSpan(cursor, centralLength).ToArray(),
                bytes.AsSpan(local, localLength).ToArray(), bytes.AsSpan(local + localLength, compressedLength).ToArray()));
            expectedLocal = checked(local + localLength + compressedLength);
            cursor += centralLength;
        }
        if (expectedLocal != U32(bytes, end + 16) || cursor != end
            || cursor - expectedLocal != U32(bytes, end + 12))
            throw new InvalidDataException("Fixture ZIP offsets/central size changed.");
        return entries.ToArray();
    }

    private static void Write(SortedDictionary<string, byte[]> entries, string path)
    {
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var pair in entries)
        {
            var entry = archive.CreateEntry(pair.Key, CompressionLevel.SmallestSize);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            entry.ExternalAttributes = 0;
            using var target = entry.Open();
            target.Write(pair.Value);
        }
    }

    private static void RequireGolden(string package, string golden)
    {
        string expected = File.ReadAllText(golden).Trim();
        string hash = MapBuildFingerprint.HashFile(package);
        if (!MapCommunityClient.ValidHash(expected) || hash != expected)
            throw new InvalidDataException("Frozen community archive differs from " + Path.GetFileName(golden)
                + "; expected " + expected + ", received " + hash);
    }

    private static void ExpectInvalid(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Negative frozen-fixture control unexpectedly passed.");
    }
}
