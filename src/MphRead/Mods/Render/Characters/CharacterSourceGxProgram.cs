using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;

namespace MphRead.Mods.Render.Characters;

// Floating TEV preview, not fixed-point hardware emulation. The closed runtime
// admission below is separate from the pure compiler used by invented fixtures.
internal sealed class CharacterSourceGxProgram
{
    internal const string Policy = "echoes-body-gx-preview-v1-raw-normal-zero-registers-white-channels-linear-top-repeat";
    internal const string TrooperPolicy = "prime-trooper-gx-preview-v1-raw-normal-profile-c1-white-channels-linear-top-repeat";
    internal enum Pass { Opaque, Alpha, Additive }
    internal sealed record Stage(int A, int B, int C, int D, int Aa, int Ab, int Ac, int Ad,
        int ColorDestination, int AlphaDestination, int Texture, int Coordinate, int Raster);
    internal sealed record Generator(int Source, int Primary, bool Normalize, int Post);
    internal sealed record Animation(uint Mode, double S, double T, double RateS, double RateT);
    internal IReadOnlyList<uint> Textures { get; }
    internal IReadOnlyList<Stage> Stages { get; }
    internal IReadOnlyList<Generator> Generators { get; }
    internal IReadOnlyList<Animation> Animations { get; }
    internal bool Lit { get; }
    internal bool SourceDepthWrite { get; }
    internal bool SourceDepthSorting { get; }
    internal Pass DrawPass { get; }
    internal int InitialRegisterDependencies { get; }
    internal Vector4 InitialRegister1 { get; }
    internal float PresentationAlphaScale { get; }
    internal string Vertex120 { get; }
    internal string Fragment120 { get; }
    internal string Vertex300 { get; }
    internal string Fragment300 { get; }

    private CharacterSourceGxProgram(Cmdl.Material m, Vector4 initialRegister1, float presentationAlphaScale)
    {
        Require(float.IsFinite(initialRegister1.X) && float.IsFinite(initialRegister1.Y)
            && float.IsFinite(initialRegister1.Z) && float.IsFinite(initialRegister1.W)
            && initialRegister1.X is >= 0 and <= 1 && initialRegister1.Y is >= 0 and <= 1
            && initialRegister1.Z is >= 0 and <= 1 && initialRegister1.W == 0,
            "Source C1 requires a finite unit RGB value and zero alpha.");
        InitialRegister1 = initialRegister1;
        Require(m.TexIds.Length is > 0 and <= 4 && m.Stages.Length is > 0 and <= 4
            && m.TexGens.Length is > 0 and <= 4 && m.UvAnims.Length <= 3
            && m.Konst.Length == 0 && m.IndTex == null && (m.Flags & (Cmdl.IndStage | Cmdl.AlphaTest)) == 0,
            "Unsupported body GX inventory; no partial program fallback.");
        Require(m.Channels.Length == 1 && m.Channels[0] is 0x3000 or 0x3001,
            "Unsupported compressed body channel controls.");
        // Register ambient/material are white. A named, camera-space directional
        // preview light replaces unavailable original actor/room draw state.
        Lit = (m.Channels[0] & 1) != 0;
        SourceDepthWrite = (m.Flags & Cmdl.DepthWrite) != 0;
        SourceDepthSorting = (m.Flags & Cmdl.DepthSort) != 0;
        DrawPass = (m.BlendSrc, m.BlendDst) switch
        {
            (1, 0) => Pass.Opaque, (4, 5) => Pass.Alpha, (1, 1) => Pass.Additive,
            _ => throw new InvalidDataException("Unsupported body framebuffer blend factors.")
        };
        Require(float.IsFinite(presentationAlphaScale) && presentationAlphaScale is >= 0 and <= 1
            && (presentationAlphaScale == 1 || DrawPass == Pass.Alpha), "Only an Alpha pass accepts presentation shell alpha.");
        PresentationAlphaScale = presentationAlphaScale;
        Require(SourceDepthWrite && SourceDepthSorting == (DrawPass != Pass.Opaque), "Unsupported body depth policy.");
        Textures = Array.AsReadOnly(m.TexIds.ToArray());
        Generators = Array.AsReadOnly(m.TexGens.Select(g =>
        {
            Require(g.Type == 0 && g.Src is 1 or 4 or 5 or 6 && g.Mtx is 0 or 3 or 6 or 30
                && g.Post is 0 or 3 or 6 or 61, "Unsupported body texgen.");
            return new Generator(g.Src, g.Mtx == 30 ? -1 : g.Mtx / 3, g.Normalize, g.Post == 61 ? -1 : g.Post / 3);
        }).ToArray());
        Animations = Array.AsReadOnly(m.UvAnims.Select(a =>
        {
            Require(a.Mode == 0 && a.Params.Length == 0 || a.Mode == 2 && a.Params.Length == 4
                && a.Params.All(v => double.IsFinite(v) && Math.Abs(v) <= 10000), "Unsupported body UV animation.");
            return a.Mode == 0 ? new Animation(0, 0, 0, 0, 0) : new Animation(2, a.Params[0], a.Params[1], a.Params[2], a.Params[3]);
        }).ToArray());
        Stages = Array.AsReadOnly(m.Stages.Select(s =>
        {
            Require(s.RawColorOp is 0x100 or 0x700 && s.RawAlphaOp is 0x100 or 0x700
                && s.KAlpha == 0 && s.KColor == 0 && s.Ras is 4 or 6 or 255
                && s.ColorIn.Length == 4 && s.ColorIn.All(v => v is 0 or 4 or 6 or 8 or 10 or 12 or 15)
                && s.AlphaIn.Length == 4 && s.AlphaIn.All(v => v is 0 or 4 or 6 or 7)
                && (s.Tex == 255 || s.Tex >= 0 && s.Tex < Textures.Count)
                && (s.Tex == 255 || s.TexCoord >= 0 && s.TexCoord < Generators.Count), "Unsupported body TEV stage.");
            return new Stage(s.ColorIn[0], s.ColorIn[1], s.ColorIn[2], s.ColorIn[3],
                s.AlphaIn[0], s.AlphaIn[1], s.AlphaIn[2], s.AlphaIn[3],
                Cmdl.TevOp.From(s.RawColorOp).Out, Cmdl.TevOp.From(s.RawAlphaOp).Out, s.Tex, s.TexCoord, s.Ras);
        }).ToArray());
        // Effective dependency walk, RGB/alpha separately, with exact ZERO/ONE
        // simplification of the mix. A register initialized by an earlier stage
        // contributes its own initial dependencies, not stale driver contents.
        int[] rgb = { 1, 2, 4, 8 }, alpha = { 16, 32, 64, 128 };
        foreach (var s in Stages)
        {
            int C(int v) => v == 0 ? rgb[0] : v == 4 ? rgb[2] : v == 6 ? rgb[3] : 0;
            int A(int v) => v == 0 ? alpha[0] : 0;
            int cr = C(s.D) | (s.A == s.B ? C(s.A) : s.C == 15 ? C(s.A) : s.C == 12 ? C(s.B) : C(s.A) | C(s.B) | C(s.C));
            int ar = A(s.Ad) | (s.Aa == s.Ab ? A(s.Aa) : s.Ac == 7 ? A(s.Aa) : s.Ac == 6 ? A(s.Ab) : A(s.Aa) | A(s.Ab) | A(s.Ac));
            rgb[s.ColorDestination] = cr; alpha[s.AlphaDestination] = ar;
        }
        InitialRegisterDependencies = rgb[0] | alpha[0];
        Vertex120 = Vertex(es: false); Fragment120 = Fragment(es: false);
        Vertex300 = Vertex(es: true); Fragment300 = Fragment(es: true);
    }
    internal static CharacterSourceGxProgram Compile(Cmdl.Material material, Vector4 initialRegister1 = default, float presentationAlphaScale = 1)
        => new(material, initialRegister1, presentationAlphaScale);
    internal static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }

    // Inputs and outputs are detached CPU fixture values. Every stage gathers
    // both equations before either destination is committed.
    internal Vector4 Evaluate(IReadOnlyList<Vector4> textureSamples, Vector4 raster, IReadOnlyList<Vector4>? initial = null)
    {
        Require(textureSamples.Count == Stages.Count && (initial == null || initial.Count == 4), "TEV sample inventory mismatch.");
        var regs = initial?.ToArray() ?? new Vector4[4];
        if (initial == null) regs[2] = InitialRegister1;
        for (int i = 0; i < Stages.Count; i++)
        {
            var s = Stages[i]; var tex = s.Texture == 255 ? Vector4.One : textureSamples[i];
            Vector3 ras = s.Raster == 4 ? new(raster.X, raster.Y, raster.Z) : Vector3.Zero;
            Vector3 C(int v) => v switch { 0 => new(regs[0].X, regs[0].Y, regs[0].Z), 4 => new(regs[2].X, regs[2].Y, regs[2].Z),
                6 => new(regs[3].X, regs[3].Y, regs[3].Z),
                8 => new(tex.X, tex.Y, tex.Z), 10 => ras, 12 => Vector3.One, 15 => Vector3.Zero, _ => throw new InvalidDataException() };
            float A(int v) => v switch { 0 => regs[0].W, 4 => tex.W, 6 => 1, 7 => 0, _ => throw new InvalidDataException() };
            Vector3 a = C(s.A), b = C(s.B), c = C(s.C), d = C(s.D);
            float aa = A(s.Aa), ab = A(s.Ab), ac = A(s.Ac), ad = A(s.Ad);
            Vector3 rgb = Vector3.Clamp(d + a * (Vector3.One - c) + b * c, Vector3.Zero, Vector3.One);
            float alpha = Math.Clamp(ad + aa * (1 - ac) + ab * ac, 0, 1);
            var colorReg = regs[s.ColorDestination]; colorReg.X = rgb.X; colorReg.Y = rgb.Y; colorReg.Z = rgb.Z;
            regs[s.ColorDestination] = colorReg;
            var alphaReg = regs[s.AlphaDestination]; alphaReg.W = alpha; regs[s.AlphaDestination] = alphaReg;
        }
        var result = Vector4.Clamp(regs[0], Vector4.Zero, Vector4.One);
        if (DrawPass == Pass.Opaque) result.W = 1;
        else result.W *= PresentationAlphaScale;
        return result;
    }

    internal static double Clock(double seconds)
    { Require(double.IsFinite(seconds) && seconds >= 0, "Finite nonnegative source material time required."); return seconds % 900; }
    internal static Vector3 Generate(Generator g, Vector3 source, IReadOnlyList<Matrix4x4> primary, IReadOnlyList<Matrix4x4> post)
    {
        Require(primary.Count == 3 && post.Count == 3, "Three explicit primary/post slots required.");
        Vector3 value = g.Primary < 0 ? source : Vector3.Transform(source, primary[g.Primary]);
        if (g.Normalize) value = value.LengthSquared() > 0 ? Vector3.Normalize(value) : Vector3.Zero;
        return g.Post < 0 ? value : Vector3.Transform(value, post[g.Post]);
    }
    internal (Matrix4x4[] Primary, Matrix4x4[] Post) Matrices(double seconds, Matrix4x4 normalToView)
    {
        double t = Clock(seconds);
        var primary = Enumerable.Repeat(Matrix4x4.Identity, 3).ToArray();
        var post = Enumerable.Repeat(Matrix4x4.Identity, 3).ToArray();
        // Only the rotation/scale normal domain is used: translation cannot
        // animate environment coordinates. Caller supplies inverse-transpose.
        normalToView.M41 = normalToView.M42 = normalToView.M43 = 0;
        normalToView.M14 = normalToView.M24 = normalToView.M34 = 0; normalToView.M44 = 1;
        for (int i = 0; i < Animations.Count; i++)
        {
            var a = Animations[i];
            if (a.Mode == 2) primary[i] = Matrix4x4.CreateTranslation((float)(a.S + t * a.RateS), (float)(a.T + t * a.RateT), 0);
            else
            {
                primary[i] = normalToView;
                // STQ: q=1 after environment mapping, including negative Z.
                post[i] = new Matrix4x4(.5f, 0, 0, 0, 0, -.5f, 0, 0, 0, 0, 0, 0, .5f, .5f, 1, 1);
            }
        }
        return (primary, post);
    }

    private string Vertex(bool es)
    {
        var b = new StringBuilder(es ? "#version 300 es\nprecision highp float;\nlayout(location=0) in vec3 a_position;\nlayout(location=1) in vec4 a_color;\nlayout(location=2) in vec3 a_normal;\nlayout(location=3) in vec3 a_texcoord;\n" : "#version 120\n");
        string varying = es ? "out" : "varying";
        b.Append("uniform mat4 gx_projection, gx_view;\nuniform mat4 gx_skin[32];\nuniform vec4 gx_primary[9], gx_post[9];\n");
        for (int i = 0; i < Generators.Count; i++) b.Append($"{varying} vec3 gx_stq{i};\n");
        b.Append($"{varying} vec4 gx_raster;\nvoid main(){{\n");
        string pos = es ? "vec4(a_position,1.0)" : "gl_Vertex", normal = es ? "a_normal" : "gl_Normal", weights = es ? "a_color" : "gl_Color", uv = es ? "a_texcoord" : "gl_MultiTexCoord0.xyz";
        b.Append($"vec3 uvj={uv}; float packedJoints=floor(uvj.z+0.5);\nint j0=int(mod(packedJoints,32.0)); packedJoints=floor(packedJoints/32.0);\nint j1=int(mod(packedJoints,32.0)); packedJoints=floor(packedJoints/32.0);\nint j2=int(mod(packedJoints,32.0)); packedJoints=floor(packedJoints/32.0); int j3=int(mod(packedJoints,32.0));\nvec4 w=max({weights},vec4(0.0)); w/=max(dot(w,vec4(1.0)),0.000001);\nmat4 skin=gx_skin[j0]*w.x+gx_skin[j1]*w.y+gx_skin[j2]*w.z+gx_skin[j3]*w.w;\ngl_Position=gx_projection*gx_view*skin*{pos};\n");
        // Lighting is PN-skinned; texgen intentionally retains raw GLB normal.
        b.Append($"vec3 n=normalize(mat3(gx_view*skin)*{normal});\n");
        b.Append(Lit ? "float nd=max(dot(n,normalize(vec3(-50.0,50.0,90.0))),0.0); gx_raster=vec4(clamp(vec3(102.0,100.0,100.0)/255.0+vec3(0.4)*nd,0.0,1.0),1.0);\n" : "gx_raster=vec4(1.0);\n");
        for (int i = 0; i < Generators.Count; i++)
        {
            var g = Generators[i]; string input = g.Source == 1 ? normal : "vec3(uvj.xy,1.0)";
            b.Append($"vec3 t{i}={input};\n");
            if (g.Primary >= 0) b.Append(Multiply($"t{i}", "gx_primary", g.Primary));
            if (g.Normalize) b.Append($"t{i}=length(t{i})>0.0?normalize(t{i}):vec3(0.0);\n");
            if (g.Post >= 0) b.Append(Multiply($"t{i}", "gx_post", g.Post));
            b.Append($"gx_stq{i}=t{i};\n");
        }
        b.Append("}\n"); return b.ToString();
    }
    private static string Multiply(string v, string matrix, int slot)
        => $"{v}=vec3(dot({matrix}[{slot * 3}],vec4({v},1.0)),dot({matrix}[{slot * 3 + 1}],vec4({v},1.0)),dot({matrix}[{slot * 3 + 2}],vec4({v},1.0)));\n";
    private string Fragment(bool es)
    {
        var b = new StringBuilder(es ? "#version 300 es\nprecision highp float;\nout vec4 gx_output;\n" : "#version 120\n");
        string varying = es ? "in" : "varying", sample = es ? "texture" : "texture2D";
        b.Append($"{varying} vec4 gx_raster;\n");
        for (int i = 0; i < Generators.Count; i++) b.Append($"{varying} vec3 gx_stq{i};\n");
        for (int i = 0; i < Textures.Count; i++) b.Append($"uniform sampler2D gx_tex{i};\n");
        // Keep the established Echoes zero-register shader bytes unchanged.
        // The closed Power profile supplies its original actor C1 value;
        // GLSL owns that value explicitly rather than inheriting driver state.
        string F(float value)
        {
            string text = value.ToString("R", CultureInfo.InvariantCulture);
            return text.Contains('.') || text.Contains('E') ? text : text + ".0";
        }
        string c1 = InitialRegister1 == Vector4.Zero ? "vec4(0.0)"
            : $"vec4({F(InitialRegister1.X)},{F(InitialRegister1.Y)},{F(InitialRegister1.Z)},0.0)";
        b.Append($"void main(){{ vec4 r0=vec4(0.0),r1=vec4(0.0),r2={c1},r3=vec4(0.0);\n");
        for (int i = 0; i < Stages.Count; i++)
        {
            var s = Stages[i];
            b.Append(s.Texture == 255 ? $"vec4 tx{i}=vec4(1.0);\n" : $"vec4 tx{i}={sample}(gx_tex{s.Texture},gx_stq{s.Coordinate}.xy/gx_stq{s.Coordinate}.z);\n");
            string C(int v) => v switch { 0 => "r0.rgb", 4 => "r2.rgb", 6 => "r3.rgb", 8 => $"tx{i}.rgb", 10 => s.Raster == 4 ? "gx_raster.rgb" : "vec3(0.0)", 12 => "vec3(1.0)", _ => "vec3(0.0)" };
            string A(int v) => v switch { 0 => "r0.a", 4 => $"tx{i}.a", 6 => "1.0", _ => "0.0" };
            b.Append($"vec3 c{i}=clamp({C(s.D)}+mix({C(s.A)},{C(s.B)},{C(s.C)}),0.0,1.0);\nfloat a{i}=clamp({A(s.Ad)}+mix({A(s.Aa)},{A(s.Ab)},{A(s.Ac)}),0.0,1.0);\nr{s.ColorDestination}.rgb=c{i}; r{s.AlphaDestination}.a=a{i};\n");
        }
        string alpha = DrawPass == Pass.Opaque ? "1.0" : "clamp(r0.a,0.0,1.0)";
        if (PresentationAlphaScale != 1) alpha += "*" + F(PresentationAlphaScale);
        b.Append($"{(es ? "gx_output" : "gl_FragColor")}=vec4(clamp(r0.rgb,0.0,1.0),{alpha});\n}}\n");
        return b.ToString();
    }
}

// Exact reproducible archive+GLB+source binding is the runtime capability.
// Hashes inside an otherwise structurally valid archive are not authority.
internal sealed class CharacterSourceGxMaterials
{
    internal RetroCharacterMaterialArchive.Archive Archive { get; }
    internal IReadOnlyList<CharacterSourceGxProgram> Programs { get; }
    internal IReadOnlyDictionary<uint, RetroTextureSource.Decoded> Images { get; }
    internal string Policy { get; }
    private CharacterSourceGxMaterials(RetroCharacterMaterialArchive.Archive archive)
    {
        Archive = archive;
        bool trooper = CharacterGeneratedTrooperProfile.Find(archive.Binding.CharacterId) != null;
        Policy = trooper ? CharacterSourceGxProgram.TrooperPolicy : CharacterSourceGxProgram.Policy;
        // Prime's Power Trooper sets GX register1 in actor code rather than the
        // CMDL. SecondHunt 41452f2d CampaignRetroBody.GameRegister1 (MIT,
        // Copyright2020 NoneGiven; repository LICENSE retained) supplies this
        // exact RGB. Other closed profiles keep the existing explicit zeros.
        Vector4 c1 = archive.Binding.CharacterId == "trooper-power" ? new(253 / 255f, 239 / 255f, 86 / 255f, 0) : Vector4.Zero;
        Programs = Array.AsReadOnly(archive.Programs.Select(p => CharacterSourceGxProgram.Compile(archive.ReadProgram(p.Ordinal), c1)).ToArray());
        Images = new System.Collections.ObjectModel.ReadOnlyDictionary<uint, RetroTextureSource.Decoded>(archive.Textures.ToDictionary(t => t.ResourceId, t => archive.DecodeTexture(t.ResourceId)));
    }
    private CharacterSourceGxMaterials(CharacterSourceGxMaterials original)
    {
        Archive = original.Archive; Images = original.Images;
        Policy = original.Policy + ":final-cannon-alpha055-v1";
        Programs = Array.AsReadOnly(Archive.Programs.Select(p => {
            var material = Archive.ReadProgram(p.Ordinal);
            return CharacterSourceGxProgram.Compile(material, presentationAlphaScale:
                material.BlendSrc == 4 && material.BlendDst == 5 ? .55f : 1f);
        }).ToArray());
    }
    internal CharacterSourceGxMaterials ForFinalCannon()
    {
        CharacterSourceGxProgram.Require(Archive.Binding.CharacterId == "darksamus-overcharged-echoes"
            && Archive.GeometrySha256 == CharacterModelPack.GeneratedFinalSourceGlbSha256
            && Archive.Sha256 == "69d27a8e4867b4ec50084260a6fb570936e79edb7b08ad2bbd04a58d5204df8d"
            && Programs.Count == 9 && Programs.All(p => p.PresentationAlphaScale == 1),
            "Only the original qualified Final body archive can produce its cannon shell presentation.");
        return new(this);
    }
    internal static CharacterSourceGxMaterials Read(byte[] bytes, string modelSha, string character)
    {
        var trooper = CharacterGeneratedTrooperProfile.Find(character);
        string expected = character switch {
            "darksamus-echoes" => "6a5666bfb14ee989658c7543543429d8de824820bee2531f0051a21a42be86d7",
            "darksamus-overcharged-echoes" => "69d27a8e4867b4ec50084260a6fb570936e79edb7b08ad2bbd04a58d5204df8d",
            _ => trooper?.MaterialSha ?? throw new InvalidDataException("No qualified source body material profile.") };
        CharacterSourceGxProgram.Require(bytes.Length is > RetroCharacterMaterialArchive.HeaderBytes and <= RetroCharacterMaterialArchive.MaxArchiveBytes
            && Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) == expected,
            "Exact source body material archive required before semantic decode.");
        var a = RetroCharacterMaterialArchive.Read(bytes);
        CharacterSourceGxProgram.Require(a.Sha256 == expected && a.Binding.CharacterId == character
            && a.GeometrySha256 == modelSha && (trooper == null || modelSha == trooper.ModelSha),
            "Source body archive/appearance/geometry identity mismatch.");
        return new(a);
    }
}
