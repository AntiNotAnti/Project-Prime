using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Import.Retro;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Materials;

// Generated TXTR/bank bytes only. These CPU boundary tests do not qualify
// retail media, sampler policy, GPU upload, appearance or gameplay.
internal static class SourceBankMaterialSnapshotTests
{
    private static int checks;
    private static readonly byte[] RedGreen = { 255, 0, 0, 255, 0, 255, 0, 255 };

    internal static void Run()
    {
        checks = 0;
        string root = Path.Combine(Path.GetTempPath(), "prime-source-material-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            DetachedCapture(root);
            Integrity(root);
            ReferenceRefusal(root);
            LinkedPaths(root);
            PrivatePackagesAndCopies(root);
            ProceduralPacker(root);
            RawAndEncodedImages();
            Console.WriteLine($"SOURCE BANK MATERIAL PASS {checks} synthetic checks; detached CPU images/private references only, no GPU authority.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void DetachedCapture(string root)
    {
        var definition = Definition(Path.Combine(root, "deferred"));
        var key = Key(definition);
        var snapshot = SourceBankMaterialSnapshot.Capture(definition)!;
        Check(!Directory.Exists(definition.BaseDirectory), "Capture performs no file or directory I/O");
        var image = snapshot.Resolve(key)!.Albedo!;
        Expect<DirectoryNotFoundException>(() => Read(image), "missing directory fails only during decode");
        // Failed Lazy verification is sticky; use a fresh capture before installing
        // the file to prove capture itself neither probes nor caches missing paths.
        snapshot = SourceBankMaterialSnapshot.Capture(definition)!;
        Write(definition, Good());
        Check(Read(snapshot.Resolve(key)!.Albedo!).Pixels.SequenceEqual(RedGreen), "a bank installed after Capture is verified at first read");

        definition = Definition(Path.Combine(root, "detached"));
        Write(definition, Good()); key = Key(definition);
        snapshot = SourceBankMaterialSnapshot.Capture(definition)!;
        var material = definition.Materials[0]; var reference = material.AlbedoBank!;
        reference.Bank = "absent.bin"; reference.BankSha256 = new string('a', 64);
        reference.RgbaSha256 = new string('c', 64); reference.ResourceId = 0x90;
        reference.Width = 1; reference.Height = 2;
        material.Id = Guid.NewGuid(); material.ProceduralWhite = false;
        material.AlbedoBank = null; material.Albedo = "other.png";
        definition.MapId = Guid.NewGuid(); definition.BaseDirectory = Path.Combine(root, "other-root");
        definition.BundlePath = "other.ppmap"; definition.Assets.Clear(); definition.Materials.Clear();
        image = snapshot.Resolve(key)!.Albedo!;
        Check(Read(image).Pixels.SequenceEqual(RedGreen) && image.Width == 2 && image.Height == 1,
            "mutable definition, nested reference, material identity, asset list and root cannot retarget captured decode");
        Check(snapshot.Resolve(MaterialAssetKey.Authored(definition.MapId, material.Id)) == null,
            "new DTO identities cannot resolve the previous model snapshot");
        Check(snapshot.Resolve(new MaterialAssetKey(key.Value.Replace("recolor/0", "recolor/7")))?.Albedo == image,
            "valid recolors retain the same captured source image");
        Check(snapshot.Resolve(new MaterialAssetKey("map/absent/material/absent/recolor/0")) == null
            && snapshot.Resolve(new MaterialAssetKey(key.Value.Replace("recolor/0", "recolor/nope"))) == null
            && snapshot.Resolve(new MaterialAssetKey("map/absent")) == null,
            "unmatched or malformed scoped keys do not resolve another material");
        byte[] first = Read(image).Pixels; first[0] = 0;
        Check(Read(image).Pixels.SequenceEqual(RedGreen), "consumer mutation cannot contaminate a later decoded image");
        Check(image.IsRawRgba && !image.IsFileBacked && image.CanDecodeOffThread
            && !image.Path.Contains(root, StringComparison.Ordinal), "captured image exposes portable content identity and worker decode ownership");
        Expect<NotSupportedException>(() => image.OpenRead(), "raw pixels cannot masquerade as an encoded image stream");
    }

    private static void Integrity(string root)
    {
        var definition = Definition(Path.Combine(root, "wrong-hash")); Write(definition, Good());
        definition.Materials[0].AlbedoBank!.BankSha256 = new string('0', 64);
        var image = Image(definition);
        Expect<InvalidDataException>(() => Read(image), "first verification checks expected whole-bank fingerprint");

        byte[] corrupt = Good(); corrupt[^12] ^= 1;
        definition = Definition(Path.Combine(root, "corrupt-pixels"), corrupt); Write(definition, corrupt);
        Expect<InvalidDataException>(() => Read(Image(definition)), "selected pixel corruption fails even with the correct whole-bank hash");

        foreach (bool warm in new[] { false, true })
        foreach (string change in new[] { "header", "directory", "resigned-image" })
        {
            definition = Definition(Path.Combine(root, $"swap-{warm}-{change}")); Write(definition, Good());
            image = Image(definition);
            if (warm) Check(Read(image).Pixels.SequenceEqual(RedGreen), "warm Lazy metadata before " + change);
            byte[] swapped = Good();
            if (change == "header") swapped[92] ^= 1; // Still a valid source SHA, same length.
            else if (change == "directory") { swapped[160 + 32] ^= 1; RehashDirectory(swapped); }
            else
            {
                swapped[^12] ^= 1;
                SHA256.HashData(swapped.AsSpan(swapped.Length - 12, 8)).CopyTo(swapped, 160 + 64);
                RehashDirectory(swapped);
            }
            // These are valid alternate generations, not merely broken hashes.
            using (var stream = new MemoryStream(swapped))
            {
                var bank = EchoesSourceTextureBank.Inspect(stream);
                Check(EchoesSourceTextureBank.ReadImage(stream, bank, 0x20).Length == 8,
                    "alternate same-sized " + change + " generation independently validates");
            }
            Write(definition, swapped);
            Expect<InvalidDataException>(() => Read(image), (warm ? "cached" : "first") + " read refuses changed " + change + " generation");
        }

        foreach (Action<MapSourceBankImage> mutate in new Action<MapSourceBankImage>[]
        { r => r.ResourceId = 0x21, r => r.Width = 1, r => r.Height = 2, r => r.RgbaSha256 = new string('0', 64) })
        {
            definition = Definition(Path.Combine(root, "mismatch-" + Guid.NewGuid().ToString("N")));
            Write(definition, Good()); mutate(definition.Materials[0].AlbedoBank!);
            Expect<InvalidDataException>(() => Read(Image(definition)), "selected resource/dimensions/pixel digest must match captured bank entry");
        }
        foreach (byte alpha in new byte[] { 0, 127, 254 })
        {
            byte[] translucent = Good(); translucent[^9] = alpha;
            SHA256.HashData(translucent.AsSpan(translucent.Length - 12, 8)).CopyTo(translucent, 224);
            RehashDirectory(translucent);
            definition = Definition(Path.Combine(root, "alpha-" + alpha), translucent);
            definition.Materials[0].AlbedoBank!.RgbaSha256 = Hash(translucent.AsSpan(translucent.Length - 12, 8));
            Write(definition, translucent);
            using (var stream = new MemoryStream(translucent))
            {
                var bank = EchoesSourceTextureBank.Inspect(stream);
                Check(EchoesSourceTextureBank.ReadImage(stream, bank, 0x20)[3] == alpha,
                    "storage preserves source alpha " + alpha + " without classifying its material");
            }
            var ex = Expect<InvalidDataException>(() => Read(Image(definition)), "initial opaque material binding refuses alpha " + alpha);
            Check(ex.Message.Contains("requires opaque pixels", StringComparison.Ordinal), "nonopaque refusal comes from binding policy");
        }
    }

    private static void ReferenceRefusal(string root)
    {
        var mutations = new Action<MapDefinition>[] {
            d => d.MapId = Guid.Empty, d => d.BundlePath = "pack.ppmap", d => d.Assets.Clear(),
            d => d.Assets[0].Kind = "texture", d => d.Materials[0].Id = Guid.Empty,
            d => d.Materials[0].ProceduralWhite = false, d => d.Materials[0].Texture = "native.bin",
            d => d.Materials[0].Albedo = "image.png", d => d.Materials[0].Normal = "image.png",
            d => d.Materials[0].SpecularRoughness = "image.png", d => d.Materials[0].Emissive = "image.png",
            d => d.Materials[0].Animation = new(), d => d.Materials[0].AlbedoBank!.Bank = "../escape.bin",
            d => d.Materials[0].AlbedoBank!.Bank = "wrong.BIN",
            d => d.Materials[0].AlbedoBank!.BankSha256 = "short", d => d.Materials[0].AlbedoBank!.RgbaSha256 = new string('z', 64),
            d => d.Materials[0].AlbedoBank!.ResourceId = 0, d => d.Materials[0].AlbedoBank!.Width = 0,
            d => d.Materials[0].AlbedoBank!.Height = 4097,
            d => { d.Materials[0].AlbedoBank!.Width = 4096; d.Materials[0].AlbedoBank!.Height = 4096; },
            d => d.Materials.Add(MapProjectSerializer.Clone(d).Materials[0])
        };
        for (int i = 0; i < mutations.Length; i++)
        {
            var definition = Definition(Path.Combine(root, "invalid-" + i)); mutations[i](definition);
            Expect<InvalidDataException>(() => SourceBankMaterialSnapshot.Capture(definition), "invalid or ambiguous private material reference " + i);
        }
        Check(SourceBankMaterialSnapshot.Capture(new MapDefinition()) == null, "ordinary materials need no source snapshot");
    }

    private static void LinkedPaths(string root)
    {
        string real = Path.Combine(root, "real");
        var definition = Definition(Path.Combine(real, "nested")); Write(definition, Good());
        string link = Path.Combine(root, "ancestor-link"); Directory.CreateSymbolicLink(link, real);
        try
        {
            definition.BaseDirectory = Path.Combine(link, "nested");
            var image = Image(definition); // Capture still performs no I/O.
            Expect<InvalidDataException>(() => Read(image), "read rejects linked ancestor above chosen project root");
        }
        finally { Directory.Delete(link); }
        definition.BaseDirectory = Path.Combine(real, "nested");
        string bankPath = Path.Combine(definition.BaseDirectory!, "textures", "source.bin");
        string actual = bankPath + ".real"; File.Move(bankPath, actual); File.CreateSymbolicLink(bankPath, actual);
        try { Expect<InvalidDataException>(() => Read(Image(definition)), "read rejects linked bank component"); }
        finally { File.Delete(bankPath); }
    }

    private static void PrivatePackagesAndCopies(string root)
    {
        var definition = Definition(Path.Combine(root, "private"));
        definition.LocalSourceOnly = false; definition.LocalSourceFingerprint = null;
        Check(definition.RequiresPrivateSource, "source bank reference retains private fence after flags are stripped");
        string forbidden = Path.Combine(root, "forbidden.ppmap");
        Expect<InvalidDataException>(() => MapPackageBuilder.Build(definition, forbidden), "private source reference cannot enter package build");
        Check(!File.Exists(forbidden), "private rejection produces no package");
        var onlyAsset = MapProjectSerializer.Clone(definition); onlyAsset.Materials.Clear();
        Check(onlyAsset.RequiresPrivateSource, "declared bank alone retains private fence");
        Expect<InvalidDataException>(() => MapPackageBuilder.Build(onlyAsset, forbidden), "declared bank alone cannot enter package build");
        var onlyReference = MapProjectSerializer.Clone(definition); onlyReference.Assets.Clear();
        foreach (var candidate in new[] { definition, onlyAsset, onlyReference })
        {
            string legacy = Path.Combine(root, "untrusted-" + Guid.NewGuid().ToString("N") + ".ppmap");
            using (var archive = ZipFile.Open(legacy, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("project.json").Open(), new UTF8Encoding(false)))
                writer.Write(candidate.Serialize());
            var ex = Expect<InvalidDataException>(() => { using var package = new MapPackageReader(legacy); }, "untrusted package cannot import a private bank/reference");
            Check(ex.Message.Contains("locally imported commercial game assets", StringComparison.Ordinal), "package fails at private-source guard itself");
        }
        var serializedClone = MapProjectSerializer.Clone(definition);
        var buildSnapshot = MapBuildSnapshot.Capture(definition);
        var worker = buildSnapshot.CreateDefinition();
        string expectedBank = definition.Materials[0].AlbedoBank!.BankSha256;
        definition.Materials[0].AlbedoBank!.BankSha256 = new string('c', 64);
        definition.Materials[0].ProceduralWhite = false; definition.Assets[0].Path = "retarget.bin";
        Check(serializedClone.Materials[0].AlbedoBank!.BankSha256 == expectedBank && serializedClone.Materials[0].ProceduralWhite
            && worker.Materials[0].AlbedoBank!.BankSha256 == expectedBank && worker.Materials[0].ProceduralWhite,
            "serialization and build snapshots detach new nested reference and fallback fields");
        worker.Materials[0].AlbedoBank!.Bank = "worker-mutated.bin";
        Check(buildSnapshot.CreateDefinition().Materials[0].AlbedoBank!.Bank == "textures/source.bin",
            "each worker gets a fresh source reference graph");
        using var json = JsonDocument.Parse(new MapDefinition { Materials = new() { new MapMaterial() } }.Serialize());
        var ordinary = json.RootElement.GetProperty("materials")[0];
        Check(!ordinary.TryGetProperty("albedoBank", out _) && !ordinary.TryGetProperty("proceduralWhite", out _),
            "new defaults do not change ordinary serialized material recipes");
        Check(serializedClone.Serialize().Contains("\"albedoBank\"", StringComparison.Ordinal)
            && serializedClone.Serialize().Contains("\"proceduralWhite\": true", StringComparison.Ordinal),
            "nondefault private references survive serialization explicitly");
    }

    private static void RawAndEncodedImages()
    {
        Expect<ArgumentNullException>(() => MaterialImage.FromRgbaFactory("raw", 1, 1, null!), "null factory refused");
        foreach (var dimensions in new[] { (0, 1), (1, 0), (-1, 1), (8193, 1), (1, int.MaxValue) })
            Expect<InvalidDataException>(() => MaterialImage.FromRgbaFactory("raw", dimensions.Item1, dimensions.Item2, () => Array.Empty<byte>()), "raw factory dimensions bounded");
        int reads = 0;
        byte[] pixels = { 255, 0, 0, 20, 0, 255, 0, 220 };
        var image = MaterialImage.FromRgbaFactory("raw/synthetic", 2, 1, () => { reads++; return (byte[])pixels.Clone(); });
        Check(reads == 0 && image.CanDecodeOffThread && image.IsRawRgba && !image.IsFileBacked, "raw factory is lazy and worker-decodable");
        var first = Read(image); first.Pixels[0] = 0;
        Check(Read(image).Pixels.SequenceEqual(pixels) && reads == 2, "each raw decode invokes fresh pixel ownership");
        foreach (TextureAssetChannel channel in Enum.GetValues<TextureAssetChannel>())
        {
            var decoded = image.ReadRgba(TextureAssetClass.Hunter, channel);
            Check(decoded.Width == 2 && decoded.Height == 1 && decoded.Channel == channel
                && decoded.AssetClass == TextureAssetClass.Hunter && decoded.Key == "raw/synthetic"
                && decoded.Pixels.SequenceEqual(pixels), "raw full-size bytes and metadata retained for " + channel);
            var fitted = (ModernTextureAsset)image.Prepare(TextureAssetClass.World, channel, 1);
            // Red and green average to (128,128,0), with independent alpha.
            Check(fitted.Width == 1 && fitted.Height == 1 && fitted.Channel == channel
                && fitted.Pixels.SequenceEqual(new byte[] { 128, 128, 0, 120 }), "channel-aware bounded Fit for " + channel);
        }
        var normals = MaterialImage.FromRgbaFactory("raw/normals", 2, 1,
            () => new byte[] { 255, 128, 128, 20, 128, 255, 128, 220 });
        Check(((ModernTextureAsset)normals.Prepare(TextureAssetClass.World, TextureAssetChannel.Albedo, 1))
            .Pixels.SequenceEqual(new byte[] { 192, 192, 128, 120 }), "ordinary channels preserve interpolated RGB without normal renormalization");
        Check(((ModernTextureAsset)normals.Prepare(TextureAssetClass.World, TextureAssetChannel.Normal, 1))
            .Pixels.SequenceEqual(new byte[] { 218, 218, 128, 120 }), "Normal Fit renormalizes the interpolated vector while preserving independent alpha");
        Expect<NotSupportedException>(() => image.OpenRead(), "raw factory refuses encoded OpenRead");
        foreach (byte[]? malformed in new byte[]?[] { null, Array.Empty<byte>(), new byte[3], new byte[5] })
        {
            var invalid = MaterialImage.FromRgbaFactory("raw/invalid", 1, 1, () => malformed!);
            Expect<InvalidDataException>(() => Read(invalid), "factory result must match exact RGBA byte count");
            Expect<InvalidDataException>(() => invalid.Prepare(TextureAssetClass.World, TextureAssetChannel.Albedo, 1), "Prepare validates malformed raw pixels too");
        }
        byte[] tga = new byte[26]; tga[2] = 2; tga[12] = 2; tga[14] = 1; tga[16] = 32; tga[17] = 0x28;
        new byte[] { 0, 0, 255, 20, 0, 255, 0, 220 }.CopyTo(tga, 18);
        var encoded = new MaterialImage("synthetic.tga", 2, 1, () => new MemoryStream(tga, writable: false), true);
        Check(!encoded.IsRawRgba && !encoded.IsFileBacked && encoded.CanDecodeOffThread,
            "encoded factory keeps existing stream ownership mode");
        using (var stream = encoded.OpenRead()) Check(stream.ReadByte() == 0, "encoded stream remains available");
        Check(Read(encoded).Pixels.SequenceEqual(pixels), "existing encoded TGA decode retains top-first channels and alpha");
        Check(((ModernTextureAsset)encoded.Prepare(TextureAssetClass.World, TextureAssetChannel.Albedo, 2)).Pixels.SequenceEqual(pixels),
            "existing encoded Prepare path still decodes unchanged dimensions");
    }

    private static void ProceduralPacker(string root)
    {
        var definition = new MapDefinition {
            FormatVersion = 2, MapId = Guid.NewGuid(), Name = "SYNTHETIC WHITE",
            TextureSource = "NO SUCH SOURCE ROOM - SYNTHETIC TEST MUST NOT READ A ROM",
            BaseDirectory = root,
            Materials = new() { new MapMaterial { Id = Guid.NewGuid(), Name = "white", ProceduralWhite = true } },
            Brushes = new() { new MapBrush { Id = Guid.NewGuid(), Min = new[] { -8f, -1f, -8f }, Max = new[] { 8f, 0f, 8f } } },
            Spawns = new() { new MapSpawn { Id = Guid.NewGuid(), Position = new[] { -2f, 2f, 0f } },
                new MapSpawn { Id = Guid.NewGuid(), Position = new[] { 2f, 2f, 0f } } }
        };
        Check(!definition.RequiresPrivateSource, "ordinary procedural white remains a public source-free material");
        var build = typeof(MapPacker).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "BuildModel" && m.GetParameters().Length == 4
                && m.GetParameters()[1].ParameterType == typeof(Dictionary<string, MapFlipbookBinding>));
        var built = MapBuilder.Build(definition);
        var packed = ((byte[], int))build.Invoke(null, new object?[] { built,
            new Dictionary<string, MapFlipbookBinding>(), null, definition.Materials })!;
        Check(packed.Item1.Length > 64 && packed.Item2 > 0,
            "procedural fallback packs real native model geometry despite impossible borrowed-room identity");
        var repeated = ((byte[], int))build.Invoke(null, new object?[] { built,
            new Dictionary<string, MapFlipbookBinding>(), null, definition.Materials })!;
        Check(packed.Item1.SequenceEqual(repeated.Item1) && packed.Item2 == repeated.Item2,
            "procedural model packing is deterministic without extracted game content");
        string packagePath = Path.Combine(root, "public-white.ppmap");
        MapPackageBuilder.Build(definition, packagePath);
        using var package = new MapPackageReader(packagePath);
        Check(package.Manifest?.MapId == definition.MapId,
            "public procedural project builds and strictly reads a source-free package");
    }

    private static MapDefinition Definition(string root, byte[]? bytes = null)
    {
        bytes ??= Good();
        return new MapDefinition {
            FormatVersion = 2, MapId = Guid.NewGuid(), Name = "SYNTHETIC BANK", BaseDirectory = root,
            Assets = new() { new MapAsset { Path = "textures/source.bin", Kind = "source-texture-bank" } },
            Materials = new() { new MapMaterial { Id = Guid.NewGuid(), ProceduralWhite = true,
                AlbedoBank = new() { Bank = "textures/source.bin", BankSha256 = Hash(bytes), ResourceId = 0x20,
                    RgbaSha256 = Hash(RedGreen), Width = 2, Height = 1 } } }
        };
    }
    private static MaterialAssetKey Key(MapDefinition definition) => MaterialAssetKey.Authored(definition.MapId, definition.Materials[0].Id);
    private static MaterialImage Image(MapDefinition definition) => SourceBankMaterialSnapshot.Capture(definition)!.Resolve(Key(definition))!.Albedo!;
    private static ModernTextureAsset Read(MaterialImage image) => image.ReadRgba(TextureAssetClass.World, TextureAssetChannel.Albedo);
    private static void Write(MapDefinition definition, byte[] bytes)
    {
        string path = Path.Combine(definition.BaseDirectory!, "textures/source.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
    }
    private static byte[] Good()
    {
        byte[] txtr = new byte[44]; BinaryPrimitives.WriteUInt32BigEndian(txtr, 8);
        BinaryPrimitives.WriteUInt16BigEndian(txtr.AsSpan(4), 2); BinaryPrimitives.WriteUInt16BigEndian(txtr.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32BigEndian(txtr.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16BigEndian(txtr.AsSpan(12), 0xfc00); BinaryPrimitives.WriteUInt16BigEndian(txtr.AsSpan(14), 0x83e0);
        var decoded = RetroTextureSource.DecodeTop(txtr);
        // A second 1x1 image makes replacement and exact selected range checks meaningful.
        byte[] small = new byte[44]; BinaryPrimitives.WriteUInt32BigEndian(small, 10);
        BinaryPrimitives.WriteUInt16BigEndian(small.AsSpan(4), 1); BinaryPrimitives.WriteUInt16BigEndian(small.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32BigEndian(small.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16BigEndian(small.AsSpan(12), 0x001f); BinaryPrimitives.WriteUInt16BigEndian(small.AsSpan(14), 0x001f);
        return EchoesSourceTextureBank.Encode(new(0x02690de1, new string('b', 64)), new[] {
            new EchoesSourceTextureBank.Input(0x20, Hash(txtr), decoded),
            new EchoesSourceTextureBank.Input(0x90, Hash(small), RetroTextureSource.DecodeTop(small)) });
    }
    private static void RehashDirectory(byte[] bytes) => SHA256.HashData(bytes.AsSpan(160, 224)).CopyTo(bytes, 124);
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Check(bool condition, string why)
    { checks++; if (!condition) throw new InvalidOperationException("Source bank material: " + why); }
    private static T Expect<T>(Action action, string why) where T : Exception
    {
        checks++;
        try { action(); } catch (T ex) { return ex; }
        throw new InvalidOperationException("Source bank material expected " + typeof(T).Name + ": " + why);
    }
}
