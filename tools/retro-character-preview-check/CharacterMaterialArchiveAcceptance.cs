using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;
using ArchiveOwner = MphRead.Mods.Import.RetroCharacterMaterialArchive;

// Invented CMDL material records, opaque CSKR binding and tiny TXTR tiles.
// Transport preservation is not semantic GX or runtime capability admission.
internal static class CharacterMaterialArchiveAcceptance
{
    private static int _checks;
    internal static void Run()
    {
        _checks = 0;
        var f = Fixture(); string input = InputHash(f); var calls = new List<uint>();
        byte[] encoded = ArchiveOwner.Encode(Source(false), f.Cmdl, f.Cskr, f.Geometry,
            id => { calls.Add(id); return f.Textures[id]; });
        Check(calls.SequenceEqual(new uint[] { 0x11, 0x22, 0x33 }), "every distinct referenced local slot fetched once in canonical ID order, including unused TEV texture");
        Check(encoded.SequenceEqual(ArchiveOwner.Encode(Source(false), f.Cmdl, f.Cskr, f.Geometry,
            id => f.Textures.Reverse().ToDictionary(p => p.Key, p => p.Value)[id])), "resource dictionary order cannot affect deterministic archive bytes");
        Check(input == InputHash(f), "encode preserves original caller bytes");
        Check(encoded.Length == 1035 && Sha(encoded) == "3fb3aae28302ad02d03cc84f050cc742c51bc4866877347d47fa7a94c2e7f937",
            "synthetic Echoes v1 archive stays byte-identical to its pre-Prime-extension baseline");
        Inspect(encoded, f);
        Isolation(encoded, f);
        Bindings(f);
        Corruption(encoded, f);
        BudgetsAndCancellation(f);
        PrimeProfilesAndDialect(encoded, f);
        Console.WriteLine($"PASS: raw character material/TXTR transport ({_checks} checks; own synthetic bytes, no material/runtime activation)");
    }

    private static void Inspect(byte[] bytes, Data f)
    {
        Check(bytes.AsSpan(0, 4).SequenceEqual("PPCM"u8) && Le(bytes, 4) == 1 && Le(bytes, 8) == 256 && Le(bytes, 12) == bytes.Length, "independent exact header/version/archive extent");
        Check(Le(bytes, 16) == 1 && Le(bytes, 20) == 2 && Le(bytes, 24) == 3 && Le(bytes, 28) == f.Set.Length, "independent material/texture counts and set extent");
        Check(Le(bytes, 32) == 0xd2c3805b && Le(bytes, 36) == 0xb9ec2f8d && Le(bytes, 40) == 0x4abc8a50 && Le(bytes, 44) == 0x39900a11, "normal body reference IDs occupy distinct header fields");
        Check(bytes.AsSpan(48, 32).SequenceEqual(SHA256.HashData(f.Cmdl)) && bytes.AsSpan(80, 32).SequenceEqual(SHA256.HashData(f.Cskr))
            && bytes.AsSpan(112, 32).SequenceEqual(Convert.FromHexString(f.Geometry)) && bytes.AsSpan(144, 32).SequenceEqual(SHA256.HashData(f.Set)), "full CMDL/opaque CSKR/geometry/exact raw set digests");
        Check(bytes.AsSpan(176, 20).SequenceEqual(Convert.FromHexString(Source(false).SourceFingerprint)) && bytes.AsSpan(196, 6).SequenceEqual("G2ME01"u8)
            && bytes.AsSpan(202, 2).IndexOfAnyExcept((byte)0) < 0 && bytes.AsSpan(204, 32).SequenceEqual(SHA256.HashData(Encoding.UTF8.GetBytes(RetroTextureSource.PixelContract)))
            && bytes.AsSpan(236, 20).IndexOfAnyExcept((byte)0) < 0, "edition, pixel contract and reserved header fields have exact independent locations");
        int materialAt = 256 + 3 * 112, next = materialAt + f.Set.Length;
        Check(bytes.AsSpan(materialAt, f.Set.Length).SequenceEqual(f.Set), "entire raw material set including unused texture IDs and opaque set tail survives");
        for (int i = 0; i < 3; i++)
        {
            uint id = new uint[] { 0x11, 0x22, 0x33 }[i]; byte[] raw = f.Textures[id]; int at = 256 + i * 112;
            int width = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(4)), height = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(6));
            Check(Le(bytes, at) == id && Le(bytes, at + 4) == Be(raw, 0) && Le(bytes, at + 8) == width && Le(bytes, at + 12) == height
                && Le(bytes, at + 16) == Be(raw, 8) && Le(bytes, at + 20) == next && Le(bytes, at + 24) == raw.Length && Le(bytes, at + 28) == width * height * 4,
                "independent sorted texture directory has exact contiguous ranges/dimensions/mip count");
            Check(bytes.AsSpan(at + 32, 32).SequenceEqual(SHA256.HashData(raw)) && bytes.AsSpan(next, raw.Length).SequenceEqual(raw)
                && bytes.AsSpan(at + 96, 16).IndexOfAnyExcept((byte)0) < 0, "all raw TXTR bytes, source hashes and reserved entry fields survive");
            next += raw.Length;
        }
        Check(next == bytes.Length, "no directory gap or unaccounted archive suffix");
        var archive = ArchiveOwner.Read(bytes);
        Check(archive.Binding == Source(false) && archive.CmdlSha256 == Sha(f.Cmdl) && archive.CskrSha256 == Sha(f.Cskr)
            && archive.GeometrySha256 == f.Geometry && archive.MaterialSetSha256 == Sha(f.Set) && archive.Sha256 == Sha(bytes), "detached archive exposes exact transport identities");
        Check(archive.Programs.Count == 2 && archive.Textures.Count == 3 && archive.DecodedBytes == 384, "all programs and referenced local slots represented once");
        Check(archive.RawProgram(0).SequenceEqual(f.Records[0]) && archive.RawProgram(1).SequenceEqual(f.Records[1])
            && archive.Programs.Select(p => p.RawSha256).SequenceEqual(f.Records.Select(Sha)), "raw stages, bindings, reserved bits and per-record opaque tails retained exactly");
        Check(archive.Programs[0].TextureIds.SequenceEqual(new uint[] { 0x22, 0x33, 0x22 }) && archive.Programs[1].TextureIds.SequenceEqual(new uint[] { 0x11 }), "set index, local slot and resource ID namespaces remain distinct with duplicate local references");
        for (int i = 0; i < 2; i++)
        {
            var p = archive.Programs[i];
            Check(bytes.AsSpan(p.Offset, p.Length).SequenceEqual(f.Records[i]) && p.Ordinal == i, "program byte ranges refer to the exact retained record");
        }
        var material = archive.ReadProgram(0);
        Check(material.Tex.SequenceEqual(new[] { 2, 0, 2 }) && material.TexIds.SequenceEqual(new uint[] { 0x22, 0x33, 0x22 }) && material.Flags == (Cmdl.Konst | Cmdl.IndStage | 0x80000080u)
            && material.VertexAttributes == 0x81003f0f && material.EchoesUnknown!.SequenceEqual(new uint[] { 0x11223344, 0x55667788 }), "copy-out retains raw material identity, optional fields and local references");
        Check(material.Konst.Length == 1 && material.Konst[0].SequenceEqual(new byte[] { 17, 34, 51, 68 }) && material.IndTex == 0xabcd0123
            && material.Channels.SequenceEqual(new uint[] { 0x3000, 0x3001 }) && material.BlendDst == 5 && material.BlendSrc == 4, "opaque indirect reference, konst, channel and blend data retained without runtime interpretation");
        Check(material.Stages.Length == 2 && material.Stages[0].Tex == 2 && material.Stages[0].TexCoord == 0 && material.Stages[1].Tex == 0 && material.Stages[1].TexCoord == 1
            && material.Stages[0].RawColorOp == 0xaabb0773 && material.Stages[0].RawAlphaOp == 0x11220700
            && material.Stages[0].ColorIn.SequenceEqual(new[] { 31, 30, 29, 28 }) && material.Stages[0].AlphaIn.SequenceEqual(new[] { 0, 1, 2, 3 }), "unsupported TEV operations/selectors and differing texture/generator slots are preserved, not silently rewritten or semantically admitted");
        Check(material.TexGens[1].Type == 5 && material.TexGens[1].Src == 7 && material.TexGens[1].Mtx == 9 && material.TexGens[1].Post == 12 && material.TexGens[1].Normalize,
            "raw texgen source/matrix/post/normalize are independent fields");
        Check(material.UvAnims.Length == 2 && material.UvAnims[0].Mode == 0 && material.UvAnims[0].Params.Length == 0 && material.UvAnims[1].Mode == 2
            && material.UvAnims[1].Params.SequenceEqual(new double[] { -0d, .25, -.5, 2 })
            && BitConverter.DoubleToInt64Bits(material.UvAnims[1].Params[0]) == long.MinValue, "animation parameters retain exact negative-zero source sign and all four values");
        foreach (var t in archive.Textures)
            Check(archive.RawTexture(t.ResourceId).SequenceEqual(f.Textures[t.ResourceId]) && t.SourceSha256 == Sha(f.Textures[t.ResourceId])
                && t.RgbaSha256 == Sha(archive.DecodeTexture(t.ResourceId).Pixels), "raw and decoded texture identities agree for every slot including a TEV-unused image");
        var rgb = archive.DecodeTexture(0x11);
        Pixel(rgb, 0, 0, new byte[] { 0, 0, 0, 0 }, "RGB5A3 transparent texel");
        Pixel(rgb, 1, 0, new byte[] { 17, 34, 51, 146 }, "RGB5A3 fractional alpha stays straight");
        Pixel(rgb, 2, 0, new byte[] { 255, 255, 255, 255 }, "RGB5A3 opaque texel");
        var cmpr = archive.DecodeTexture(0x22);
        Pixel(cmpr, 0, 0, new byte[] { 0, 0, 255, 255 }, "CMPR first endpoint");
        Pixel(cmpr, 2, 0, new byte[] { 127, 0, 127, 255 }, "CMPR three-color average");
        Pixel(cmpr, 3, 0, new byte[] { 127, 0, 127, 0 }, "CMPR transparent RGB is neither zeroed nor premultiplied");
        Refuses<ArgumentOutOfRangeException>(() => archive.ReadProgram(-1), "negative program ordinal");
        Refuses<ArgumentOutOfRangeException>(() => archive.RawProgram(2).ToArray(), "program ordinal outside inventory");
        Refuses<InvalidDataException>(() => archive.DecodeTexture(0x44), "unreferenced set-table entry is not a transported local texture slot");
    }

    private static void Isolation(byte[] encoded, Data original)
    {
        var f = Fixture(); byte[] before = (byte[])encoded.Clone(); int callbacks = 0;
        byte[] mutated = ArchiveOwner.Encode(Source(false), f.Cmdl, f.Cskr, f.Geometry, id =>
        {
            if (++callbacks == 1) { Array.Clear(f.Cmdl); Array.Clear(f.Cskr); }
            if (callbacks == 2) Array.Clear(f.Textures[0x11]); // already returned and captured
            return f.Textures[id];
        });
        Check(mutated.SequenceEqual(encoded), "source arrays and previously returned texture arrays are captured before later caller callbacks mutate them");
        byte[] caller = (byte[])encoded.Clone(); var archive = ArchiveOwner.Read(caller); Array.Clear(caller);
        Check(archive.Sha256 == Sha(before) && archive.RawProgram(0).SequenceEqual(original.Records[0]) && archive.RawTexture(0x22).SequenceEqual(original.Textures[0x22]), "reader retains no mutable caller archive memory");
        var changed = archive.ReadProgram(0); changed.Flags = 0; changed.Tex[0] = 0; changed.TexIds[0] = 0; changed.Konst[0][0] = 0;
        changed.Channels[0] = 0; changed.Stages[0].ColorIn[0] = 0; changed.Stages[0].RawColorOp = 0; changed.TexGens[0] = default; changed.UvAnims[1].Params[1] = 123;
        var fresh = archive.ReadProgram(0);
        Check(fresh.Tex[0] == 2 && fresh.TexIds[0] == 0x22 && fresh.Konst[0][0] == 17 && fresh.Channels[0] == 0x3000
            && fresh.Stages[0].ColorIn[0] == 31 && fresh.Stages[0].RawColorOp == 0xaabb0773 && fresh.TexGens[0].Src == 4 && fresh.UvAnims[1].Params[1] == .25,
            "every mutable nested parser object is fresh copy-out, never live snapshot state");
        Check(archive.RawProgram(0).SequenceEqual(original.Records[0]), "mutating parsed output cannot alter raw opaque bytes");
    }

    private static void Bindings(Data f)
    {
        var final = Source(true); var archive = ArchiveOwner.Read(ArchiveOwner.Encode(final, f.Cmdl, f.Cskr, f.Geometry, id => f.Textures[id]));
        Check(archive.Binding == final && archive.Programs.Count == 2 && archive.GeometrySha256 == f.Geometry,
            "Final has distinct exact references; arbitrary synthetic hashes/program count remain transport, not known-source semantic authority");
        foreach (var source in new[] { Source(false) with { Cmdl = final.Cmdl }, Source(false) with { Cskr = final.Cskr }, Source(false) with { Cinf = 1 },
            Source(false) with { Ancs = 1 }, Source(false) with { CharacterId = "darksamus" }, Source(false) with { SourceFingerprint = new string('a', 40) } })
            Refuses<InvalidDataException>(() => ArchiveOwner.Encode(source, f.Cmdl, f.Cskr, f.Geometry, id => f.Textures[id]), "cross-bound/unknown appearance or source edition refuses");
        Refuses<InvalidDataException>(() => ArchiveOwner.Encode(Source(false), f.Cmdl, f.Cskr, "not-a-hash", id => f.Textures[id]), "geometry binding must be a canonical digest");
    }

    private static void Corruption(byte[] encoded, Data f)
    {
        void Bad(Action<byte[]> edit, string why) { byte[] bytes = (byte[])encoded.Clone(); edit(bytes); Refuses<InvalidDataException>(() => ArchiveOwner.Read(bytes), why); }
        foreach (int at in new[] { 0, 4, 8, 12, 16, 32, 36, 40, 44, 176, 196, 202, 204, 236 }) Bad(b => b[at] ^= 0x55, "header contract corruption at " + at);
        foreach (var (at, value) in new[] { (20, 0u), (20, 33u), (24, 0u), (24, 65u), (24, uint.MaxValue), (28, 0u), (28, 2_097_153u) })
            Bad(b => PutLe(b, at, value), "bounded header directory count/extent");
        foreach (int field in new[] { 0, 4, 8, 12, 16, 20, 24, 28, 32, 64, 96 }) Bad(b => b[256 + field] ^= 0x55, "entry identity/layout/hash/reserved corruption at " + field);
        Bad(b => PutLe(b, 256 + 112, 0x11), "duplicate directory resource ID");
        Bad(b => PutLe(b, 256 + 112 + 20, Le(b, 256 + 20)), "overlapping payload extents");
        Bad(b => PutLe(b, 256 + 20, Le(b, 256 + 20) + 1), "payload gap");
        Bad(b => b[^1] ^= 1, "raw texture payload corruption");
        int materialAt = 256 + 3 * 112;
        Bad(b => b[materialAt + f.Set.Length - 1] ^= 1, "opaque material-set tail participates in integrity hash");
        void BadSet(Action<byte[], int> edit, string why) => Bad(b => { edit(b, materialAt); SHA256.HashData(b.AsSpan(materialAt, f.Set.Length)).CopyTo(b.AsSpan(144, 32)); }, why);
        BadSet((b, a) => PutBe(b, a, 65), "material texture-table count cap before allocation");
        BadSet((b, a) => PutBe(b, a + 4, 0), "zero set resource ID");
        BadSet((b, a) => PutBe(b, a + 20, 33), "material program count cap before allocation");
        BadSet((b, a) => PutBe(b, a + 24, 0), "empty/reversed material interval");
        BadSet((b, a) => PutBe(b, a + 24, uint.MaxValue), "material interval cannot reach following texture payload");
        BadSet((b, a) => PutBe(b, a + 32 + 8, 4), "local texture table index cannot be supplied by following records");
        for (int length = 0; length <= 256; length++)
        { byte[] prefix = encoded[..length]; Refuses<InvalidDataException>(() => ArchiveOwner.Read(prefix), "truncated header extent"); }
        foreach (int length in new[] { materialAt - 1, materialAt + f.Set.Length - 1, encoded.Length - 1 })
        {
            byte[] prefix = encoded[..length]; PutLe(prefix, 12, (uint)length);
            Refuses<InvalidDataException>(() => ArchiveOwner.Read(prefix), "consistent total header cannot hide truncated directory/set/payload");
        }
        byte[] appended = encoded.Append((byte)0).ToArray(); PutLe(appended, 12, (uint)appended.Length);
        Refuses<InvalidDataException>(() => ArchiveOwner.Read(appended), "unaccounted trailing byte refuses");
    }

    private static void BudgetsAndCancellation(Data f)
    {
        foreach (uint version in new uint[] { 2, 3, 5 })
        {
            byte[] wrong = (byte[])f.Cmdl.Clone(); PutBe(wrong, 4, version);
            Refuses<InvalidDataException>(() => ArchiveOwner.Encode(Source(false), wrong, f.Cskr, f.Geometry, id => f.Textures[id]), "only known-profile CMDL4 extraction envelope supported");
        }
        int callbacks = 0;
        byte[] oversized = new byte[ArchiveOwner.MaxArchiveBytes + 1]; f.Cmdl.CopyTo(oversized, 0);
        Refuses<InvalidDataException>(() => ArchiveOwner.Encode(Source(false), oversized, f.Cskr, f.Geometry, _ => { callbacks++; return f.Textures[0x11]; }), "oversized CMDL refuses before resource callbacks");
        Check(callbacks == 0, "invalid source envelope never opens a resource");
        Refuses<InvalidDataException>(() => ArchiveOwner.Read(oversized), "complete archive cap checked before capture");
        byte[] hugeSet = new byte[ArchiveOwner.MaxMaterialSetBytes + 1]; f.Set.CopyTo(hugeSet, 0);
        Refuses<InvalidDataException>(() => ArchiveOwner.Encode(Source(false), WrapCmdl(hugeSet), f.Cskr, f.Geometry, _ => throw new InvalidOperationException("must not open")), "raw material set byte budget");
        byte[] largeRecord = new byte[Cmdl.MaxMaterialBytes + 1]; f.Records[0].CopyTo(largeRecord, 0);
        Refuses<InvalidDataException>(() => ArchiveOwner.Encode(Source(false), WrapCmdl(Set(new uint[] { 0x11, 0x22, 0x33, 0x44 }, new[] { largeRecord })), f.Cskr, f.Geometry,
            _ => throw new InvalidOperationException("must not open")), "one record cannot consume surrounding set beyond parser byte cap");
        Refuses<InvalidDataException>(() => ArchiveOwner.Encode(Source(false), f.Cmdl, Array.Empty<byte>(), f.Geometry, id => f.Textures[id]), "empty CSKR source binding");
        Refuses<InvalidDataException>(() => ArchiveOwner.Encode(Source(false), f.Cmdl, new byte[ArchiveOwner.MaxCskrBytes + 1], f.Geometry,
            _ => throw new InvalidOperationException("must not open")), "opaque CSKR source capture byte cap");
        Refuses<InvalidDataException>(() => ArchiveOwner.Encode(Source(false), f.Cmdl, f.Cskr, f.Geometry, _ => new byte[ArchiveOwner.MaxTextureBytes + 1]), "encoded TXTR source cap");
        byte[] giantImage = Texture(10, 4096, 4096, payload: false);
        Refuses<InvalidDataException>(() => ArchiveOwner.Encode(Source(false), f.Cmdl, f.Cskr, f.Geometry, _ => giantImage), "decoded image budget precedes tiled payload access/allocation");
        byte[] unknownFormat = Texture(9, 4, 4);
        Refuses<NotSupportedException>(() => ArchiveOwner.Encode(Source(false), f.Cmdl, f.Cskr, f.Geometry, _ => unknownFormat), "unknown TXTR format refuses without substitution");
        Refuses<InvalidDataException>(() => ArchiveOwner.Encode(Source(false), f.Cmdl, f.Cskr, f.Geometry, _ => null!), "missing texture refuses");
        byte[] five = Set(new uint[] { 1, 2, 3, 4, 5 }, new[] { Material(new[] { 0, 1, 2, 3, 4 }, rich: false) });
        byte[] big = Texture(10, 1024, 1024);
        Refuses<InvalidDataException>(() => ArchiveOwner.Encode(Source(false), WrapCmdl(five), f.Cskr, f.Geometry, _ => big), "aggregate decoded-image budget includes all referenced local slots");
        byte[] aggregate = ManualArchive(five, big, f);
        Refuses<InvalidDataException>(() => ArchiveOwner.Read(aggregate), "reader independently enforces aggregate decoded budget");
        for (int cancelAt = 0; cancelAt <= 3; cancelAt++)
        {
            using var cancellation = new CancellationTokenSource(); int calls = 0; byte[]? output = null; string before = InputHash(f);
            if (cancelAt == 0) cancellation.Cancel();
            Refuses<OperationCanceledException>(() => output = ArchiveOwner.Encode(Source(false), f.Cmdl, f.Cskr, f.Geometry,
                id => { if (++calls == cancelAt) cancellation.Cancel(); return f.Textures[id]; }, cancellation.Token), "pre/mid/final-callback cancellation returns no partial archive");
            Check(output == null && InputHash(f) == before && (cancelAt != 0 || calls == 0), "cancellation neither publishes bytes nor mutates inputs");
            Refuses<OperationCanceledException>(() => ArchiveOwner.Read(new byte[1], cancellation.Token), "read cancellation is checked before malformed-input processing");
        }
        byte[]? partial = null; int opened = 0;
        Refuses<IOException>(() => partial = ArchiveOwner.Encode(Source(false), f.Cmdl, f.Cskr, f.Geometry,
            id => ++opened == 2 ? throw new IOException("own fixture failure") : f.Textures[id]), "late resource failure returns no partial archive");
        Check(partial == null, "failure exposes no partially encoded component");
    }

    private static void PrimeProfilesAndDialect(byte[] echoesBytes, Data echoes)
    {
        // Only record syntax differs: omit Echoes' two words and use CMDL2.
        // Invented records intentionally retain unsupported GX operations;
        // successful authoring transport grants no renderer capability.
        var f = Fixture(echoes: false); string input = InputHash(f);
        var profiles = new (string Id, uint Profile, uint Cmdl, uint Cskr)[]
        {
            ("trooper-power", 3, 0xf8691b86, 0x9346b450),
            ("trooper-ice", 4, 0x45e6de5b, 0x2ec9718d),
            ("trooper-wave", 5, 0x5eb5adfa, 0x359a022c),
            ("trooper-plasma", 6, 0x202ac74c, 0x4b05689a)
        };
        ArchiveOwner.Source Binding((string Id, uint Profile, uint Cmdl, uint Cskr) p) => new(p.Id, p.Cmdl, p.Cskr, 0x0c9669bc, 0x8169653a,
            RetroCharacterPreviewExporter.BundleSpec(p.Id, ExternalSourceEdition.PrimeUsaRevision2).SourceFingerprint);
        byte[] Encode(ArchiveOwner.Source source, byte[]? cmdl = null) => ArchiveOwner.Encode(source, cmdl ?? f.Cmdl, f.Cskr, f.Geometry, id => f.Textures[id]);
        void Bad(byte[] original, Action<byte[]> edit, string why)
        { byte[] bytes = (byte[])original.Clone(); edit(bytes); Refuses<InvalidDataException>(() => ArchiveOwner.Read(bytes), why); }
        var hashes = new HashSet<string>();
        foreach (var p in profiles)
        {
            var source = Binding(p); byte[] bytes = Encode(source); var archive = ArchiveOwner.Read(bytes);
            Check(Le(bytes, 4) == 1 && Le(bytes, 8) == 256 && Le(bytes, 16) == p.Profile
                && bytes.AsSpan(196, 6).SequenceEqual("GM8E01"u8), p.Id + " uses its closed profile ID and Prime game within unchanged v1 transport");
            Check(Le(bytes, 32) == p.Cmdl && Le(bytes, 36) == p.Cskr && Le(bytes, 40) == 0x0c9669bc && Le(bytes, 44) == 0x8169653a
                && bytes.AsSpan(176, 20).SequenceEqual(Convert.FromHexString(source.SourceFingerprint)) && archive.Binding == source,
                p.Id + " retains separate exact Rev2 source/reference authority");
            Check(bytes.SequenceEqual(Encode(source)) && hashes.Add(Sha(bytes)) && InputHash(f) == input,
                p.Id + " is deterministic, separately bound and does not mutate invented inputs");
            Check(archive.Programs.Count == 2 && archive.Textures.Count == 3 && archive.DecodedBytes == 384
                && archive.MaterialSetSha256 == Sha(f.Set) && archive.CmdlSha256 == Sha(f.Cmdl)
                && archive.RawProgram(0).SequenceEqual(f.Records[0]) && archive.RawProgram(1).SequenceEqual(f.Records[1]),
                p.Id + " preserves all Prime record bytes and transport digests");
            var material = archive.ReadProgram(0);
            Check(material.EchoesUnknown == null && material.Group == 0xaabbccdd && material.VertexAttributes == 0x81003f0f
                && material.TexIds.SequenceEqual(new uint[] { 0x22, 0x33, 0x22 }) && material.Konst[0].SequenceEqual(new byte[] { 17, 34, 51, 68 })
                && material.IndTex == 0xabcd0123 && material.Channels.SequenceEqual(new uint[] { 0x3000, 0x3001 })
                && material.Stages[0].RawColorOp == 0xaabb0773 && material.Stages[0].ColorIn.SequenceEqual(new[] { 31, 30, 29, 28 })
                && material.TexGens[1].Post == 12 && material.UvAnims[1].Params[1] == .25
                && BitConverter.DoubleToInt64Bits(material.UvAnims[1].Params[0]) == long.MinValue,
                p.Id + " parses Prime dialect offsets without inserting Echoes words or interpreting unsupported GX");
            material.Konst[0][0] = 0; material.Stages[0].ColorIn[0] = 0; material.UvAnims[1].Params[1] = 123;
            var fresh = archive.ReadProgram(0);
            Check(fresh.Konst[0][0] == 17 && fresh.Stages[0].ColorIn[0] == 31 && fresh.UvAnims[1].Params[1] == .25,
                p.Id + " parsed nested Prime records are detached copy-out");
            foreach (var t in archive.Textures)
                Check(archive.RawTexture(t.ResourceId).SequenceEqual(f.Textures[t.ResourceId]) && t.SourceSha256 == Sha(f.Textures[t.ResourceId])
                    && t.RgbaSha256 == Sha(archive.DecodeTexture(t.ResourceId).Pixels), p.Id + " uses the same raw/TXTR pixel contract");
            foreach (uint version in new uint[] { 0, 1, 3, 4, 5 })
            {
                byte[] wrong = (byte[])f.Cmdl.Clone(); PutBe(wrong, 4, version);
                Refuses<InvalidDataException>(() => Encode(source, wrong), p.Id + " rejects non-CMDL2 envelopes");
            }
            var next = profiles[(int)(p.Profile - 2) % profiles.Length]; var other = Binding(next);
            foreach (var wrong in new[] { source with { Cmdl = other.Cmdl }, source with { Cskr = other.Cskr },
                source with { Cinf = Source(false).Cinf }, source with { Ancs = Source(false).Ancs },
                source with { SourceFingerprint = Source(false).SourceFingerprint }, source with { SourceFingerprint = new string('a', 40) },
                source with { SourceFingerprint = RetroCharacterPreviewExporter.BundleSpec(p.Id).SourceFingerprint },
                source with { CharacterId = "trooper" } })
                Refuses<InvalidDataException>(() => Encode(wrong), p.Id + " rejects mixed parts, unknown appearance, Echoes/legacy/unknown source editions");
            Bad(bytes, b => "G2ME01"u8.CopyTo(b.AsSpan(196)), p.Id + " reader rejects Echoes game under Prime profile");
            Bad(bytes, b => PutLe(b, 16, next.Profile),
                p.Id + " reader rejects another Prime profile with unchanged resource bindings");
            Bad(bytes, b => PutLe(b, 16, 1), p.Id + " reader rejects Echoes profile under Prime game");
            Bad(bytes, b => PutLe(b, 16, 7), p.Id + " reader rejects unassigned profile IDs");
            Bad(bytes, b => Convert.FromHexString(Source(false).SourceFingerprint).CopyTo(b, 176), p.Id + " reader revalidates source fingerprint");
        }
        Check(hashes.Count == 4 && f.Records[0].Length + 8 == echoes.Records[0].Length && f.Records[1].Length + 8 == echoes.Records[1].Length,
            "all four profiles remain distinct; invented Prime records differ by exactly two omitted Echoes words");
        // Relabeling the CMDL envelope alone must not change the material dialect.
        byte[] primeAsEchoes = (byte[])f.Cmdl.Clone(); PutBe(primeAsEchoes, 4, 4);
        byte[] echoesAsPrime = (byte[])echoes.Cmdl.Clone(); PutBe(echoesAsPrime, 4, 2);
        Refuses<InvalidDataException>(() => ArchiveOwner.Encode(Source(false), primeAsEchoes, f.Cskr, f.Geometry, id => f.Textures[id]), "CMDL4 envelope cannot smuggle Prime records into Echoes parser");
        Refuses<InvalidDataException>(() => Encode(Binding(profiles[0]), echoesAsPrime), "CMDL2 envelope cannot smuggle Echoes records into Prime parser");
        void Rebind(byte[] bytes, uint profile, ArchiveOwner.Source source, bool isEchoes)
        {
            PutLe(bytes, 16, profile); PutLe(bytes, 32, source.Cmdl); PutLe(bytes, 36, source.Cskr);
            PutLe(bytes, 40, source.Cinf); PutLe(bytes, 44, source.Ancs);
            Convert.FromHexString(source.SourceFingerprint).CopyTo(bytes, 176);
            (isEchoes ? "G2ME01"u8 : "GM8E01"u8).CopyTo(bytes.AsSpan(196));
        }
        Bad(echoesBytes, b => Rebind(b, 3, Binding(profiles[0]), isEchoes: false),
            "valid Prime header authority cannot make reader parse unchanged Echoes records as Prime");
        Bad(Encode(Binding(profiles[0])), b => Rebind(b, 1, Source(false), isEchoes: true),
            "valid Echoes header authority cannot make reader parse unchanged Prime records as Echoes");
        Bad(echoesBytes, b => "GM8E01"u8.CopyTo(b.AsSpan(196)), "existing Echoes archive rejects Prime game header");
        Bad(echoesBytes, b => PutLe(b, 16, 3), "existing Echoes archive rejects Prime profile header");
    }

    private sealed record Data(byte[] Cmdl, byte[] Cskr, string Geometry, byte[] Set, byte[][] Records, Dictionary<uint, byte[]> Textures);
    private static Data Fixture(bool echoes = true)
    {
        byte[][] records = { Material(new[] { 2, 0, 2 }, rich: true, echoes), Material(new[] { 1 }, rich: false, echoes) };
        byte[] set = Set(new uint[] { 0x33, 0x11, 0x22, 0x44 }, records);
        byte[] rgb = Texture(8, 4, 4); PutBe16(rgb, 12, 0); PutBe16(rgb, 14, 0x4123); PutBe16(rgb, 16, 0xffff);
        byte[] cmpr = Texture(10, 8, 8); PutBe16(cmpr, 12, 0x001f); PutBe16(cmpr, 14, 0xf800);
        for (int i = 16; i < 20; i++) cmpr[i] = 0x1b;
        // A second complete tiled mip and aligned FF tail must survive even
        // though this transport's decoder exposes only top-level pixels.
        byte[] padded = Texture(8, 4, 4); byte[] ff = new byte[96]; padded.CopyTo(ff, 0); PutBe(ff, 8, 2);
        ff.AsSpan(44, 32).Fill(0x3c); ff.AsSpan(76).Fill(0xff);
        return new(WrapCmdl(set, echoes ? 4u : 2u), new byte[] { 9, 8, 7, 6, 5 }, Sha(new byte[] { 1, 3, 5, 7 }), set, records,
            new() { [0x33] = ff, [0x11] = rgb, [0x22] = cmpr });
    }
    private static ArchiveOwner.Source Source(bool final) => new(final ? "darksamus-overcharged-echoes" : "darksamus-echoes", final ? 0xfaa12c05u : 0xd2c3805bu,
        final ? 0x918e83d3u : 0xb9ec2f8du, 0x4abc8a50, 0x39900a11, RetroCharacterPreviewExporter.BundleSpec("darksamus-echoes").SourceFingerprint);
    private static byte[] WrapCmdl(byte[] set, uint version = 4)
    {
        var result = new byte[64 + set.Length + 5]; PutBe(result, 0, 0xdeadbabe); PutBe(result, 4, version); PutBe(result, 8, 5);
        PutBe(result, 0x24, 2); PutBe(result, 0x28, 1); PutBe(result, 0x2c, (uint)set.Length); PutBe(result, 0x30, 5);
        set.CopyTo(result, 64); result.AsSpan(64 + set.Length).Fill(0x91); return result;
    }
    private static byte[] Set(uint[] table, byte[][] records)
    {
        using var output = new MemoryStream(); void U(uint value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, value); output.Write(b); }
        U((uint)table.Length); foreach (uint id in table) U(id); U((uint)records.Length); int end = 0;
        foreach (var record in records) { end += record.Length; U((uint)end); }
        foreach (var record in records) output.Write(record); output.Write(new byte[] { 0xba, 0xbe, 0x55, 0x66, 0x77 }); return output.ToArray();
    }
    private static byte[] Material(int[] slots, bool rich, bool echoes = true)
    {
        using var output = new MemoryStream();
        void U(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); output.Write(b); }
        void H(ushort v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); output.Write(b); }
        U(rich ? Cmdl.Konst | Cmdl.IndStage | 0x80000080u : 0x80); U((uint)slots.Length); foreach (int slot in slots) U((uint)slot);
        U(0x81003f0f); if (echoes) { U(0x11223344); U(0x55667788); } U(0xaabbccdd);
        if (rich) { U(1); output.Write(new byte[] { 17, 34, 51, 68 }); }
        H(5); H(4); if (rich) U(0xabcd0123); U(rich ? 2u : 0); if (rich) { U(0x3000); U(0x3001); }
        U(rich ? 2u : 0);
        if (rich)
        {
            U(0xf0000000u | Pack(31, 30, 29, 28)); U(0xab000000u | Pack(0, 1, 2, 3)); U(0xaabb0773); U(0x11220700); output.Write(new byte[] { 0x99, 0x11, 0x22, 0x33 });
            U(Pack(15, 12, 8, 15)); U(Pack(7, 7, 7, 6)); U(0x100); U(0x700); output.Write(new byte[] { 0x88, 0x44, 0x55, 0x66 });
            output.Write(new byte[] { 0xaa, 0xbb, 2, 0, 0xcc, 0xdd, 0, 1 });
        }
        U(rich ? 2u : 0); if (rich) { U((4u << 4) | (30u << 9) | (61u << 15)); U(0xf0000000u | 5u | (7u << 4) | (9u << 9) | (1u << 14) | (12u << 15)); }
        U(rich ? 28u : 4u); U(rich ? 2u : 0);
        if (rich) { U(0); U(2); U(0x80000000); U(0x3e800000); U(0xbf000000); U(0x40000000); }
        output.Write(new byte[] { 0xde, 0xad, 0xbe, 0xef, 0x7a }); return output.ToArray();
    }
    private static uint Pack(int a, int b, int c, int d) => (uint)(a | b << 5 | c << 10 | d << 15);
    private static byte[] Texture(uint format, int width, int height, bool payload = true)
    {
        int tile = format == 10 ? 8 : 4;
        int length = payload ? checked(((width + tile - 1) / tile) * ((height + tile - 1) / tile) * 32) : 0;
        byte[] bytes = new byte[12 + length]; PutBe(bytes, 0, format); PutBe16(bytes, 4, (ushort)width); PutBe16(bytes, 6, (ushort)height); PutBe(bytes, 8, 1); return bytes;
    }
    private static byte[] ManualArchive(byte[] set, byte[] raw, Data f)
    {
        // Independent malformed-budget fixture: five valid1024x1024 CMPR
        // images (4MiB each). Their selector0 pixels are opaque black.
        int count = 5, payload = 256 + count * 112 + set.Length; byte[] bytes = new byte[payload + count * raw.Length];
        "PPCM"u8.CopyTo(bytes); PutLe(bytes, 4, 1); PutLe(bytes, 8, 256); PutLe(bytes, 12, (uint)bytes.Length); PutLe(bytes, 16, 1); PutLe(bytes, 20, 1); PutLe(bytes, 24, 5); PutLe(bytes, 28, (uint)set.Length);
        var source = Source(false); PutLe(bytes, 32, source.Cmdl); PutLe(bytes, 36, source.Cskr); PutLe(bytes, 40, source.Cinf); PutLe(bytes, 44, source.Ancs);
        SHA256.HashData(WrapCmdl(set)).CopyTo(bytes, 48); SHA256.HashData(f.Cskr).CopyTo(bytes, 80); Convert.FromHexString(f.Geometry).CopyTo(bytes, 112); SHA256.HashData(set).CopyTo(bytes, 144);
        Convert.FromHexString(source.SourceFingerprint).CopyTo(bytes, 176); "G2ME01"u8.CopyTo(bytes.AsSpan(196)); SHA256.HashData(Encoding.UTF8.GetBytes(RetroTextureSource.PixelContract)).CopyTo(bytes, 204); set.CopyTo(bytes, 256 + count * 112);
        byte[] block = new byte[4096]; for (int i = 3; i < block.Length; i += 4) block[i] = 255;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); for (int i = 0; i < 1024; i++) hash.AppendData(block); byte[] rgbaHash = hash.GetHashAndReset();
        for (int i = 0; i < count; i++)
        {
            int row = 256 + i * 112, at = payload + i * raw.Length;
            PutLe(bytes, row, (uint)(i + 1)); PutLe(bytes, row + 4, 10); PutLe(bytes, row + 8, 1024); PutLe(bytes, row + 12, 1024); PutLe(bytes, row + 16, 1);
            PutLe(bytes, row + 20, (uint)at); PutLe(bytes, row + 24, (uint)raw.Length); PutLe(bytes, row + 28, 4 * 1024 * 1024); SHA256.HashData(raw).CopyTo(bytes, row + 32); rgbaHash.CopyTo(bytes, row + 64); raw.CopyTo(bytes, at);
        }
        return bytes;
    }
    private static void Pixel(RetroTextureSource.Decoded image, int x, int y, byte[] expected, string why) => Check(image.Pixels.Slice((y * image.Layout.Width + x) * 4, 4).SequenceEqual(expected), why);
    private static string InputHash(Data f)
    { using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); h.AppendData(f.Cmdl); h.AppendData(f.Cskr); foreach (var p in f.Textures.OrderBy(p => p.Key)) h.AppendData(p.Value); return Convert.ToHexStringLower(h.GetHashAndReset()); }
    private static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Sha(byte[] bytes) => Sha(bytes.AsSpan());
    private static uint Le(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at, 4));
    private static uint Be(byte[] b, int at) => BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at, 4));
    private static void PutLe(byte[] b, int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at, 4), v);
    private static void PutBe(byte[] b, int at, uint v) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at, 4), v);
    private static void PutBe16(byte[] b, int at, ushort v) => BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at, 2), v);
    private static void Check(bool value, string why) { if (!value) throw new InvalidOperationException("Character material archive fixture: " + why); _checks++; }
    private static void Refuses<T>(Action action, string why) where T : Exception { try { action(); } catch (T) { _checks++; return; } throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + why); }
}
