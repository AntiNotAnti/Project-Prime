using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using MphRead.Mods.MapGen;

namespace ProjectPrime.MapPlatform.Tests;

[Collection("map compiler")]
public sealed class MapTextureBakeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        "project-prime-map-texture-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Func<ReadOnlyMemory<byte>, RgbImage>? _previousDecoder;

    public MapTextureBakeTests()
    {
        Directory.CreateDirectory(_directory);
        _previousDecoder = MapImageDecoding.Decoder;
        // Synthetic one-byte images keep these tests independent of external
        // image libraries and let the selected alias be observed in the pack.
        MapImageDecoding.Decoder = encoded =>
        {
            byte red = encoded.Span.Length == 0 ? (byte)0 : encoded.Span[0];
            return new RgbImage(1, 1, new byte[] { red, 0, 0 });
        };
    }

    [Fact]
    public void ShaderAliasesPreferDirectQerThenStageAndKeepArchivePrecedence()
    {
        string firstArchive = Archive("first.pk3", [
            ("scripts/aliases.shader", """
                // Comments can contain braces and fake commands: { map $bad }
                textures/test/direct
                {
                    qer_editorimage "textures/test/qer.jpg"
                    { map textures/test/stage.png }
                }
                textures/test/qer
                {
                    /* qer_editorimage $commented_out */
                    qer_editorimage "TEXTURES\\TEST\\QER.TGA"
                    {
                        map textures/test/stage.png
                    }
                }
                textures/test/stage
                {
                    {
                        map "$lightmap"
                        clampMap "textures/test/STAGE.PNG"
                        animMap 5 textures/test/anim.jpg
                    }
                }
                textures/test/anim
                {
                    {
                        animMap 2 "textures/test/ANIM.JPG"
                    }
                }
                textures/test/virtual
                {
                    qer_editorimage "$lightmap"
                    {
                        map "$whiteimage"
                    }
                }
                textures/test/duplicate
                {
                    qer_editorimage textures/test/first.png
                    qer_editorimage textures/test/second.png
                    { map textures/test/stage.png }
                }
                textures/test/prefer-direct
                {
                    qer_editorimage textures/test/first.png
                }
                textures/test/patch
                {
                    {
                        map textures/test/patch.png
                    }
                }
                """, null),
            // Q3 packs routinely name .tga in the shader while shipping .jpg.
            ("TEXTURES\\TEST\\QER.JPG", null, [22]),
            ("textures/test/STAGE.PNG", null, [33]),
            ("textures/test/ANIM.JPG", null, [44]),
            ("textures/test/first.png", null, [55]),
            ("textures/test/second.png", null, [66]),
            ("textures/test/patch.png", null, [77]),
            ("textures/test/direct.PNG", null, [11]),
            ("textures/test/direct.png", null, [12])
        ]);
        string secondArchive = Archive("second.pk3", [
            // The first archive's case-variant duplicate remains authoritative.
            ("textures/test/direct.png", null, [99]),
            // Direct image lookup beats qer_editorimage even when it is in a
            // later archive, because lookup category is part of the contract.
            ("textures/test/prefer-direct.PNG", null, [88]),
            // A duplicate shader definition in a later archive is ignored.
            ("scripts/override.shader", """
                textures/test/duplicate
                {
                    qer_editorimage textures/test/second.png
                }
                """, null)
        ]);

        Q3Bsp bsp = Q3Bsp.Load(CreateBsp([
            ("textures/test/direct", 1),
            ("textures/test/qer", 1),
            ("textures/test/stage", 1),
            ("textures/test/anim", 1),
            ("textures/test/virtual", 1),
            ("textures/test/duplicate", 1),
            ("textures/test/prefer-direct", 1),
            ("textures/test/patch", 2)
        ], 0, 1, 2, 3, 4, 5, 6, 7, 0));
        string output = Path.Combine(_directory, "aliases.tex");

        MapTextureBake.Result result = MapTextureBake.Bake(bsp,
            [firstArchive, secondArchive], output, size: 1);

        Assert.Equal(7, result.Baked);
        Assert.Contains("textures/test/virtual", result.Missing);
        MapTexturePack pack = MapTexturePack.Load(output);
        Assert.Equal([0, 1, 2, 3, 5, 6, 7], pack.Entries.Select(e => e.SourceIndex));
        AssertRed(pack, 0, 11); // direct image, first archive wins duplicate path
        AssertRed(pack, 1, 22); // quoted explicit .tga token resolves shipped .jpg
        AssertRed(pack, 2, 33); // clampmap after a virtual map reference
        AssertRed(pack, 3, 44); // animMap skips its frequency token
        AssertRed(pack, 5, 55); // duplicate qer token and shader definition
        AssertRed(pack, 6, 88); // direct image beats qer alias
        AssertRed(pack, 7, 77); // type-2 solid patch surfaces are still baked
    }

    [Fact]
    public void MissingShaderReferencesAreReportedOnceForRepeatedFaces()
    {
        string archive = Archive("missing.pk3", [
            ("scripts/missing.shader", """
                textures/test/missing
                {
                    // Only virtual images are not representative files.
                    qer_editorimage "$lightmap"
                    { animMap 4 "$lightmap" }
                }
                """, null)
        ]);
        Q3Bsp bsp = Q3Bsp.Load(CreateBsp([
            ("textures/test/missing", 1)
        ], 0, 0, 0));

        MapTextureBake.Result result = MapTextureBake.Bake(bsp,
            [archive], Path.Combine(_directory, "missing.tex"), size: 1);

        Assert.Equal(0, result.Baked);
        Assert.Single(result.Missing);
        Assert.Equal("textures/test/missing", result.Missing[0]);
    }

    private static void AssertRed(MapTexturePack pack, int sourceIndex, byte expected)
    {
        MapTexturePack.Entry entry = pack.Entries.Single(value => value.SourceIndex == sourceIndex);
        ushort colour = entry.Palette[entry.Pixels[0]];
        Assert.Equal((ushort)(expected >> 3), (ushort)(colour & 0x1F));
    }

    private string Archive(string name,
        IEnumerable<(string Path, string? Text, byte[]? Bytes)> files)
    {
        string path = Path.Combine(_directory, name);
        using (FileStream stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach ((string entryPath, string? text, byte[]? bytes) in files)
            {
                ZipArchiveEntry entry = archive.CreateEntry(entryPath);
                using Stream target = entry.Open();
                byte[] data = text == null ? bytes! : Encoding.UTF8.GetBytes(text);
                target.Write(data);
            }
        }
        return path;
    }

    private static byte[] CreateBsp((string Name, int Type)[] textures, params int[] faceTextures)
    {
        byte[][] lumps = new byte[17][];
        lumps[1] = TextureLump(textures);
        lumps[13] = FaceLump(textures, faceTextures);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("IBSP"));
        writer.Write(46);
        for (int i = 0; i < 17; i++)
        {
            writer.Write(0);
            writer.Write(0);
        }

        var locations = new (int Offset, int Length)[17];
        for (int i = 0; i < lumps.Length; i++)
        {
            if (lumps[i] is not { Length: > 0 }) continue;
            locations[i] = ((int)stream.Position, lumps[i].Length);
            writer.Write(lumps[i]);
        }

        for (int i = 0; i < locations.Length; i++)
        {
            stream.Position = 8 + i * 8;
            writer.Write(locations[i].Offset);
            writer.Write(locations[i].Length);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] TextureLump((string Name, int Type)[] textures)
    {
        byte[] lump = new byte[textures.Length * 72];
        for (int i = 0; i < textures.Length; i++)
        {
            byte[] name = Encoding.ASCII.GetBytes(textures[i].Name);
            name.AsSpan(0, Math.Min(name.Length, 63)).CopyTo(lump.AsSpan(i * 72, 64));
            BinaryPrimitives.WriteInt32LittleEndian(lump.AsSpan(i * 72 + 64, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(lump.AsSpan(i * 72 + 68, 4), 0);
        }
        return lump;
    }

    private static byte[] FaceLump((string Name, int Type)[] textures, int[] faceTextures)
    {
        if (faceTextures.Length == 0)
            faceTextures = Enumerable.Range(0, textures.Length).ToArray();
        byte[] lump = new byte[faceTextures.Length * 104];
        for (int i = 0; i < faceTextures.Length; i++)
        {
            int offset = i * 104;
            BinaryPrimitives.WriteInt32LittleEndian(lump.AsSpan(offset, 4), faceTextures[i]);
            BinaryPrimitives.WriteInt32LittleEndian(lump.AsSpan(offset + 8, 4),
                textures[faceTextures[i]].Type);
            BinaryPrimitives.WriteInt32LittleEndian(lump.AsSpan(offset + 24, 4), 3);
        }
        return lump;
    }

    public void Dispose()
    {
        MapImageDecoding.Decoder = _previousDecoder;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
