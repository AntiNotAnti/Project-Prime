using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace MphRead.Mods.Render.Characters;

// One captured source program, not a generic GX renderer or a pack authority.
internal sealed class CharacterSourceMaterial4Recipe
{
    internal const string MaterialsSha = "63e3e2ce858993eb92e1740057f386b2b0f665598cbba098d70da47e39acb287";
    internal const string ImageSha = "275f188635d67b471134563a7e9cc18fdcff53a4efdd6c0ba3da54d18dc0b753";
    internal const uint TextureResource = 0x8c40ec03;
    internal const string MaterialName = "retro_material_4";
    internal const int PaletteLimit = 32;
    private readonly byte[] _image;
    private CharacterSourceMaterial4Recipe(byte[] image) => _image = image;
    internal static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static void Require(bool value, string message)
    { if (!value) throw new InvalidDataException(message); }
    internal static CharacterSourceMaterial4Recipe Read(byte[] materials, byte[] image)
    {
        Require(materials.Length is > 0 and <= 1024 * 1024 && image.Length == 7012,
            "Bounded source material and texture snapshots required.");
        materials=(byte[])materials.Clone();image=(byte[])image.Clone();
        Require(Sha(materials) == MaterialsSha,
            "Exact seven-program source CMDL snapshot required.");
        Require(image.Length == 7012 && Sha(image) == ImageSha, "Exact source material4 TXTR PNG required.");
        using var doc = JsonDocument.Parse(materials, new JsonDocumentOptions { MaxDepth = 24 });
        var root = doc.RootElement;
        Require(root.GetProperty("Version").GetInt32() == 4 && root.GetProperty("MaterialSets").GetInt32() == 1,
            "Exact original Echoes material set required.");
        var all = root.GetProperty("materials"); Require(all.GetArrayLength() == 7, "Whole source material inventory required.");
        var m = all[4];
        Require(m.GetProperty("Flags").GetUInt32() == 0x14092 && m.GetProperty("BlendSrc").GetInt32() == 1
            && m.GetProperty("BlendDst").GetInt32() == 1 && m.GetProperty("IndTex").ValueKind == JsonValueKind.Null
            && m.GetProperty("Channels").EnumerateArray().Select(x=>x.GetUInt32()).SequenceEqual(new uint[]{0x3000})
            && m.GetProperty("TexIds").EnumerateArray().Select(x=>x.GetUInt32()).SequenceEqual(new[]{TextureResource})
            && m.GetProperty("Konst").GetArrayLength() == 0 && m.GetProperty("UvAnims").GetArrayLength() == 0,
            "Only captured unlit additive material4 is supported.");
        var stages=m.GetProperty("Stages"); Require(stages.GetArrayLength()==1,"No stage truncation permitted.");
        var s=stages[0];
        Require(s.GetProperty("ColorIn").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(new[]{15,12,8,15})
            && s.GetProperty("AlphaIn").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(new[]{7,7,7,6})
            && s.GetProperty("RawColorOp").GetUInt32()==0x100 && s.GetProperty("RawAlphaOp").GetUInt32()==0x100
            && s.GetProperty("Tex").GetInt32()==0 && s.GetProperty("TexCoord").GetInt32()==0
            && s.GetProperty("KAlpha").GetInt32()==0,"Exact material4 TEV selectors/operations required.");
        var t=m.GetProperty("TexGens"); Require(t.GetArrayLength()==1,"Only source primary UV generator supported.");
        var g=t[0]; Require(g.GetProperty("Type").GetInt32()==0 && g.GetProperty("Src").GetInt32()==4
            && g.GetProperty("Mtx").GetInt32()==30 && g.GetProperty("Post").GetInt32()==61
            && !g.GetProperty("Normalize").GetBoolean(),"Exact source identity UV generator required.");
        return new(image);
    }
    internal byte[] CopyImage() => (byte[])_image.Clone();
    internal static (float R,float G,float B,float A) EvaluateSample(byte r,byte g,byte b,byte a)
        => (r/255f,g/255f,b/255f,1f); // The source K-alpha selector0 is ONE; texture alpha is still retained.
    internal const string FragmentShader = "#version 120\nuniform sampler2D source_tex; varying vec2 texcoord;\nvoid main(){ vec4 source_rgba=texture2D(source_tex,texcoord); gl_FragColor=vec4(source_rgba.rgb,1.0); }\n";
}
