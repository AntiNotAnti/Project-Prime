using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.Import;

// Asset-free synthetic disc/FST tests. Optional --probe inspects a user's LOCAL
// image and emits only identifiers and compatibility statuses, never game bytes.
try
{
    if (args.Length == 2 && args[0] == "--probe")
    {
        using var image = GameCubeDiscReader.Open(args[1]);
        Console.WriteLine("Edition: " + image.GameId + " (" + image.Game + "), revision " + image.Revision);
        Console.WriteLine("Files: " + image.Files.Count);
        foreach (var check in ExternalSourceRequirements.VerifyKnown(image))
            Console.WriteLine(check.Path + ": " + check.Status);
        return 0;
    }
    if (args.Length == 4 && args[0] == "--probe-resource")
    {
        using var disc = GameCubeDiscReader.Open(args[1]);
        var requirement = ExternalSourceRequirements.For(disc.Game)
            .FirstOrDefault(x => x.Path.Equals(args[2], StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(requirement.Path) ||
            ExternalSourceRequirements.Verify(disc, requirement).Status != ExternalArchiveStatus.Verified)
            throw new InvalidDataException("Source PAK does not match the pinned local recipe.");
        using var stream = disc.OpenRead(args[2]);
        using var pak = new RetroPakReader(stream);
        uint id = Convert.ToUInt32(args[3], 16);
        var entry = pak.Entry(id);
        byte[] resource = pak.ReadResource(id);
        Console.WriteLine(entry.Type + " " + id.ToString("x8") + ": " + resource.Length + " bytes decoded locally (not saved)");
        return 0;
    }
    if (args.Length == 2 && args[0] == "--probe-arenas")
    {
        using var disc = GameCubeDiscReader.Open(args[1]);
        if (disc.Game != ExternalGameKind.Echoes)
            throw new InvalidDataException("Echoes GameCube source required.");
        var requirement = ExternalSourceRequirements.For(disc.Game)
            .Single(x => x.Path == "Metroid6.pak");
        if (ExternalSourceRequirements.Verify(disc, requirement).Status != ExternalArchiveStatus.Verified)
            throw new InvalidDataException("Echoes source does not match the pinned Metroid6.pak recipe.");
        using var stream = disc.OpenRead(requirement.Path);
        using var pak = new RetroPakReader(stream);
        foreach (var arena in EchoesArenaSources.All)
        {
            var entry = pak.Entry(arena.ResourceId);
            if (entry.Type != "MREA") throw new InvalidDataException("Arena source is not MREA: " + arena.Key);
            var parsed = EchoesMreaSections.Parse(pak.ReadResource(arena.ResourceId));
            Console.WriteLine(arena.Name + ": " + parsed.SectionCount + " sections, " +
                parsed.ClusterCount + " clusters, " + parsed.ModelCount + " models (not saved)");
        }
        return 0;
    }
    if (args.Length == 2 && args[0] == "--probe-surfaces")
    {
        using var disc = GameCubeDiscReader.Open(args[1]);
        if (disc.Game != ExternalGameKind.Echoes)
            throw new InvalidDataException("The Echoes source is required.");
        var required = ExternalSourceRequirements.For(disc.Game).Single(x => x.Path == "Metroid6.pak");
        if (ExternalSourceRequirements.Verify(disc, required).Status != ExternalArchiveStatus.Verified)
            throw new InvalidDataException("The local Echoes archive does not match the pinned recipe.");
        using var image = disc.OpenRead(required.Path);
        using var pak = new RetroPakReader(image);
        foreach (var arena in EchoesArenaSources.All)
        {
            var sections = EchoesMreaSections.Parse(pak.ReadResource(arena.ResourceId));
            var surfaces = EchoesCollisionSource.Read(sections);
            var objects = EchoesScriptSource.Read(sections);
            int spawns = objects.Count(x => x.Type == "SPWN" && x.Active && x.HasTransform);
            Console.WriteLine(arena.Name + ": triangles=" + surfaces.TriangleCount +
                ", vertices=" + surfaces.VertexCount + ", spawns=" + spawns);
        }
        return 0;
    }
    if (args.Length != 0)
    {
        Console.Error.WriteLine("Usage: external-import-check [--probe <local.iso|local.gcm>]");
        return 2;
    }
    SelfTest();
    RetroPakFixtureTests.Run();
    EchoesMreaFixtureTests.Run();
    EchoesSurfaceFixtureTests.Run();
    Console.WriteLine("PASS: local GameCube source bounds, files, SHA-1 receipts and negative fixtures");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex);
    return 1;
}

static void SelfTest()
{
    const int fst = 0x500;
    byte[] disc = Fixture("G2ME01");
    using var input = new MemoryStream(disc, writable: false);
    using (var reader = new GameCubeDiscReader(input, leaveOpen: true))
    {
        Check(reader.Game == ExternalGameKind.Echoes && reader.GameId == "G2ME01", "correct source");
        Check(reader.Files.Count == 2 && reader.TryGetFile("assets/demo.bin", out var nested)
            && nested.Length == 3, "nested FST entries");
        Check(reader.ReadSmallFile("METROID6.PAK").SequenceEqual(new byte[] { 16, 32, 48, 64 }), "case insensitive lookup");
        Check(reader.ReadSmallFile("Assets/demo.bin").SequenceEqual(new byte[] { 7, 8, 9 }), "nested range");
        Check(reader.Sha1("Metroid6.pak") ==
            Convert.ToHexString(SHA1.HashData(new byte[] { 16, 32, 48, 64 })).ToLowerInvariant(), "streamed SHA1");
        using (Stream window = reader.OpenRead("Metroid6.pak"))
        {
            Check(window.CanSeek && !window.CanWrite, "read-only range");
            Check(window.Seek(2, SeekOrigin.Begin) == 2 && window.ReadByte() == 48 &&
                window.ReadByte() == 64 && window.ReadByte() == -1, "bounded seek and read");
            Expect<NotSupportedException>(() => window.WriteByte(1), "read-only source");
            Expect<ArgumentOutOfRangeException>(() => window.Seek(-1, SeekOrigin.Begin), "negative seek");
        }
        Expect<InvalidDataException>(() => reader.ReadSmallFile("Metroid6.pak", 3), "bounded allocation");
        Expect<ArgumentException>(() => reader.TryGetFile("../Metroid6.pak", out _), "path traversal");
        var good = ExternalSourceRequirements.Verify(reader,
            new ExternalArchiveRequirement("Metroid6.pak", reader.Sha1("Metroid6.pak")));
        Check(good.Status == ExternalArchiveStatus.Verified, "matching recipe");
        Check(ExternalSourceRequirements.Verify(reader,
            new ExternalArchiveRequirement("missing.pak", good.ExpectedSha1)).Status == ExternalArchiveStatus.Missing,
            "absent archive");
        Check(ExternalSourceRequirements.VerifyKnown(reader).All(x => x.Status != ExternalArchiveStatus.Verified),
            "fixture never passes real archive SHA1");
        Expect<ArgumentException>(() => ExternalSourceRequirements.Verify(reader,
            new ExternalArchiveRequirement("Metroid6.pak", "invalid")), "malformed hash");
    }
    Check(input.CanRead, "caller retains ownership");

    byte[] invalid = Fixture("GM8E01");
    Put(invalid, 0x1c, 0);
    Expect<InvalidDataException>(() => Open(invalid), "wrong magic");
    Expect<NotSupportedException>(() => Open(Fixture("G2MJ01")), "unverified edition");

    invalid = Fixture("G2ME01");
    Put(invalid, 0x428, uint.MaxValue);
    Expect<InvalidDataException>(() => Open(invalid), "FST budget");

    invalid = Fixture("G2ME01");
    Put(invalid, fst + 8, uint.MaxValue);
    Expect<InvalidDataException>(() => Open(invalid), "entry count overflow");

    invalid = Fixture("G2ME01");
    Put(invalid, fst + 12 + 8, 1);
    Expect<InvalidDataException>(() => Open(invalid), "directory cycle");

    invalid = Fixture("G2ME01");
    Put(invalid, fst + 36 + 4, 0x3000);
    Expect<InvalidDataException>(() => Open(invalid), "file escapes disc bounds");

    invalid = Fixture("G2ME01");
    int names = fst + 48;
    invalid[names] = (byte)'.'; invalid[names + 1] = (byte)'.'; invalid[names + 2] = 0;
    Expect<InvalidDataException>(() => Open(invalid), "FST traversal component");

    invalid = Fixture("G2ME01");
    invalid[names] = (byte)'/';
    Expect<InvalidDataException>(() => Open(invalid), "invalid separator");

    invalid = Fixture("G2ME01");
    Put(invalid, fst + 24, 0x00fffffe);
    Expect<InvalidDataException>(() => Open(invalid), "filename out of range");

    using var owner = new MemoryStream(Fixture("G2ME01"), writable: false);
    var source = new GameCubeDiscReader(owner);
    Stream lease = source.OpenRead("Metroid6.pak");
    source.Dispose();
    Expect<ObjectDisposedException>(() => lease.ReadByte(), "leased stream cannot outlive disposed image");
    lease.Dispose();
}

static GameCubeDiscReader Open(byte[] b) =>
    new(new MemoryStream(b, writable: false), leaveOpen: false);

static byte[] Fixture(string game)
{
    var b = new byte[0x2000];
    Encoding.ASCII.GetBytes(game).CopyTo(b, 0);
    Put(b, 0x1c, GameCubeDiscReader.Magic);
    Encoding.ASCII.GetBytes("Test only").CopyTo(b, 0x20);
    Put(b, 0x424, 0x500);
    const string names = "Assets\0demo.bin\0Metroid6.pak\0";
    Put(b, 0x428, (uint)(48 + Encoding.ASCII.GetByteCount(names)));
    const int f = 0x500;
    Put(b, f, 0x01000000); Put(b, f + 8, 4);
    Put(b, f + 12, 0x01000000); Put(b, f + 16, 0); Put(b, f + 20, 3);
    Put(b, f + 24, 7); Put(b, f + 28, 0x1100); Put(b, f + 32, 3);
    Put(b, f + 36, 16); Put(b, f + 40, 0x1000); Put(b, f + 44, 4);
    Encoding.ASCII.GetBytes(names).CopyTo(b, f + 48);
    b[0x1000] = 16; b[0x1001] = 32; b[0x1002] = 48; b[0x1003] = 64;
    b[0x1100] = 7; b[0x1101] = 8; b[0x1102] = 9;
    return b;
}

static void Put(byte[] b, int offset, uint value) =>
    BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(offset, 4), value);

static void Check(bool ok, string message)
{
    if (!ok) throw new InvalidOperationException(message);
}
static void Expect<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name + ": " + message);
}
