using System.Numerics;
using MphRead.Mods.Import.Retro;
using MphRead.Mods.Render.Characters;

// Invented inputs and literal arithmetic oracles. These cover the admitted
// floating preview contract, not source-asset admission or GX hardware parity.
internal static class CharacterGxProgramAcceptance
{
    private static int _checks;
    internal static void Run()
    {
        _checks = 0;
        EquationsAndDestinations();
        Dependencies();
        ClockAndAnimation();
        TexgenRowsAndDomains();
        ShaderAndSnapshots();
        Refusals();
        Console.WriteLine($"PASS: source GX observed-subset equations/dependencies/STQ/admission ({_checks} checks; synthetic CPU only)");
    }

    private static void EquationsAndDestinations()
    {
        var initial = new[] { new Vector4(.125f, .25f, .5f, .25f), Vector4.Zero,
            Vector4.Zero, new Vector4(.5f, .125f, .75f, .75f) };
        var before = initial.ToArray();
        var tex = new Vector4(.75f, .5f, .25f, .5f);
        var stage = S(new[] { 15, 8, 12, 15 }, new[] { 0, 7, 7, 7 }, alphaDestination: 3);
        var p = Compile(stage);
        Near(p.Evaluate(new[] { tex }, Vector4.Zero, initial), new(.75f, .5f, .25f, .25f), "RGB write preserves PREV alpha while alpha targets C2");
        Check(p.Stages[0].ColorDestination == 0 && p.Stages[0].AlphaDestination == 3, "separate destination identities retained");
        Check(initial.SequenceEqual(before), "evaluation cannot mutate initial registers");

        p = Compile(S(new[] { 15, 8, 12, 15 }, new[] { 7, 6, 6, 7 }));
        Near(p.Evaluate(new[] { tex }, Vector4.Zero, initial), new(.75f, .5f, .25f, 1), "simultaneous same-register RGB and KAlpha0 one");
        p = Compile(S(new[] { 0, 6, 8, 10 }, new[] { 0, 4, 4, 7 }));
        // RGB = RAS + PREV*(1-TEX) + C2*TEX. Alpha=.25*.5+.5*.5.
        Near(p.Evaluate(new[] { tex }, new(.125f, .25f, .5f, 0), initial),
            new(.53125f, .4375f, 1, .375f), "component-wise mix, additive raster and clamp literal oracle");
        p = Compile(S(new[] { 15, 10, 12, 15 }, OneAlpha(), raster: 6));
        Near(p.Evaluate(new[] { tex }, Vector4.One), new(0, 0, 0, 1), "null raster channel does not borrow supplied raster");
        p = Compile(S(new[] { 15, 10, 12, 15 }, OneAlpha(), raster: 4));
        Near(p.Evaluate(new[] { tex }, new(.125f, .5f, .875f, 0)), new(.125f, .5f, .875f, 1), "COLOR0 raster is selected explicitly");

        var writeC2 = S(new[] { 15, 8, 12, 15 }, OneAlpha(), colorDestination: 3);
        var readC2 = S(new[] { 15, 6, 12, 15 }, new[] { 7, 4, 6, 7 });
        p = Compile(writeC2, readC2);
        Near(p.Evaluate(new[] { tex, new Vector4(0, 0, 0, .125f) }, Vector4.Zero, initial),
            new(.75f, .5f, .25f, .125f), "later stage reads committed C2 RGB with independent alpha");
        p = Compile(readC2, writeC2);
        Near(p.Evaluate(new[] { tex, tex }, Vector4.Zero, initial), new(.5f, .125f, .75f, 1), "reversing stage order reads original C2 and later alpha write");

        var opaque = M(S(new[] { 15, 8, 12, 15 }, new[] { 7, 4, 6, 7 }));
        opaque.BlendSrc = 1; opaque.BlendDst = 0; opaque.Flags = Cmdl.DepthWrite;
        p = CharacterSourceGxProgram.Compile(opaque);
        Near(p.Evaluate(new[] { tex }, Vector4.Zero), new(.75f, .5f, .25f, 1), "opaque preview forces output alpha one");
        Check(p.DrawPass == CharacterSourceGxProgram.Pass.Opaque && p.SourceDepthWrite && !p.SourceDepthSorting, "opaque source pass metadata");
        opaque.BlendDst = 1; opaque.Flags |= Cmdl.DepthSort;
        p = CharacterSourceGxProgram.Compile(opaque);
        Near(p.Evaluate(new[] { tex }, Vector4.Zero), tex, "additive pass retains equation alpha");
        Check(p.DrawPass == CharacterSourceGxProgram.Pass.Additive, "ONE/ONE additive classification");
    }

    private static void Dependencies()
    {
        void D(int[] c, int expected, string why)
            => Check(Compile(S(c, OneAlpha())).InitialRegisterDependencies == expected, why);
        D(new[] { 0, 15, 12, 15 }, 0, "ONE factor removes dead PREV input");
        D(new[] { 15, 6, 15, 15 }, 0, "ZERO factor removes dead C2 input");
        D(new[] { 6, 6, 0, 15 }, 8, "equal mix inputs remove irrelevant PREV factor dependency");
        D(new[] { 15, 15, 15, 6 }, 8, "additive C2 dependency survives");
        Check(Compile(S(new[] { 15, 8, 12, 15 }, new[] { 0, 7, 7, 7 })).InitialRegisterDependencies == 16,
            "PREV alpha dependency remains separate from initialized RGB");
        var write = S(new[] { 15, 8, 12, 15 }, OneAlpha(), colorDestination: 3);
        var read = S(new[] { 15, 6, 12, 15 }, OneAlpha());
        Check(Compile(write, read).InitialRegisterDependencies == 0, "earlier C2 write discharges initial C2 dependency");
        Check(Compile(read, write).InitialRegisterDependencies == 8, "later C2 write cannot discharge earlier PREV result");
        var alphaEqual = S(new[] { 15, 8, 12, 15 }, new[] { 4, 4, 0, 7 });
        Check(Compile(alphaEqual).InitialRegisterDependencies == 0, "equal alpha inputs remove irrelevant PREV-alpha factor");
    }

    private static void ClockAndAnimation()
    {
        foreach (var (input, expected) in new[] { (0d, 0d), (2d, 2d), (899d, 899d), (900d, 0d), (902d, 2d), (1802d, 2d) })
            Check(CharacterSourceGxProgram.Clock(input) == expected, "900-second bounded clock literal " + input);
        double large = CharacterSourceGxProgram.Clock(double.MaxValue);
        Check(double.IsFinite(large) && large >= 0 && large < 900, "finite large clock stays bounded");
        foreach (double t in new[] { -1d, double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            Refuses(() => CharacterSourceGxProgram.Clock(t), "invalid material clock");

        var m = M(); m.TexGens[0].Mtx = 0;
        m.UvAnims = new[] { new Cmdl.UvAnim { Mode = 2, Params = new[] { .125, -.25, .25, .125 } } };
        var p = CharacterSourceGxProgram.Compile(m); var uv = new Vector3(.25f, .5f, 1);
        foreach (var (t, expected) in new[] { (0d, new Vector3(.375f, .25f, 1)), (2d, new Vector3(.875f, .5f, 1)),
            (902d, new Vector3(.875f, .5f, 1)), (900d, new Vector3(.375f, .25f, 1)) })
        {
            var matrices = p.Matrices(t, Matrix4x4.Identity);
            Near(CharacterSourceGxProgram.Generate(p.Generators[0], uv, matrices.Primary, matrices.Post), expected, "scroll offsets and rates at " + t);
            Check(matrices.Primary[1] == Matrix4x4.Identity && matrices.Post[2] == Matrix4x4.Identity, "unused slots reset to identity");
        }
        var a = p.Matrices(2, Matrix4x4.Identity); a.Primary[0] = default;
        var b = p.Matrices(0, Matrix4x4.Identity); var again = p.Matrices(2, Matrix4x4.Identity);
        Near(CharacterSourceGxProgram.Generate(p.Generators[0], uv, again.Primary, again.Post), new(.875f, .5f, 1), "matrix outputs detached across A/B/A sampling");
        Check(b.Primary[0].M41 == .125f && b.Primary[0].M42 == -.25f, "time zero retains authored offsets");

        // Asymmetric top-origin 2x2 texture: R/G above B/Y. A positive T rate
        // moves from red to blue; neither vertical flip nor rate negation fits.
        m.UvAnims[0].Params = new[] { 0d, 0d, 0d, .25d };
        p = CharacterSourceGxProgram.Compile(m); var mat = p.Matrices(1, Matrix4x4.Identity);
        var stq = CharacterSourceGxProgram.Generate(p.Generators[0], new(.25f, .375f, 1), mat.Primary, mat.Post);
        Near(stq, new(.25f, .625f, 1), "positive top-origin T scroll literal");
        Near(p.Evaluate(new[] { SampleInvented(stq) }, Vector4.Zero), new(0, 0, 1, 1), "asymmetric authored texture domain selects lower-left blue");
    }

    private static void TexgenRowsAndDomains()
    {
        var primary = Identities(); var post = Identities();
        primary[0] = Rows(3, 0, 0, 0, 0, 4, 0, 0, 0, 0, 1, 0);
        post[0] = Rows(2, 0, 0, .25f, 0, 3, 0, -.5f, 0, 0, 0, 2);
        var g = new CharacterSourceGxProgram.Generator(1, 0, true, 0);
        Near(CharacterSourceGxProgram.Generate(g, new(1, 1, 0), primary, post), new(1.45f, 1.9f, 2), "primary then normalize then post STQ literal");
        Near(CharacterSourceGxProgram.Generate(g with { Normalize = false }, new(1, 1, 0), primary, post), new(6.25f, 11.5f, 2), "normalization occurs between both transforms");
        primary[0] = Rows(1, 2, 3, 4, 5, 6, 7, 8, 0, 0, 0, 2);
        Near(CharacterSourceGxProgram.Generate(g with { Normalize = false, Post = -1 }, new(.25f, .5f, 1), primary, post),
            new(8.25f, 19.25f, 2), "nonsymmetric row-major affine STQ does not transpose or predivide");

        var m = M(); m.TexIds = new uint[] { 17, 29 };
        m.TexGens = new[] { G(4, 0, 6), G(5, 3, 61), G(6, 30, 0) };
        m.Stages = new[] { S(new[] { 15, 8, 12, 15 }, OneAlpha(), texture: 1, coordinate: 0),
            S(new[] { 15, 8, 12, 15 }, OneAlpha(), texture: 0, coordinate: 1) };
        var p = CharacterSourceGxProgram.Compile(m);
        Check(p.Generators[0] == new CharacterSourceGxProgram.Generator(4, 0, false, 2)
            && p.Generators[1] == new CharacterSourceGxProgram.Generator(5, 1, false, -1)
            && p.Generators[2] == new CharacterSourceGxProgram.Generator(6, -1, false, 0), "source matrix selectors retain independent primary/post slots and identity sentinels");
        Check(p.Fragment120.Contains("texture2D(gx_tex1,gx_stq0.xy/gx_stq0.z)", StringComparison.Ordinal)
            && p.Fragment120.Contains("texture2D(gx_tex0,gx_stq1.xy/gx_stq1.z)", StringComparison.Ordinal)
            && p.Fragment300.Contains("texture(gx_tex1,gx_stq0.xy/gx_stq0.z)", StringComparison.Ordinal)
            && p.Fragment300.Contains("texture(gx_tex0,gx_stq1.xy/gx_stq1.z)", StringComparison.Ordinal), "texture unit and coordinate generator stay independent in both shader dialects");
        primary = Identities(); post = Identities();
        primary[0] = Matrix4x4.CreateTranslation(.125f, -.25f, 0);
        primary[1] = Matrix4x4.CreateTranslation(-.25f, .125f, 0);
        post[2] = Rows(2, 0, 0, .125f, 0, 1, 0, .25f, 0, 0, 0, 1);
        Near(CharacterSourceGxProgram.Generate(p.Generators[0], new(.25f, .5f, 1), primary, post), new(.875f, .5f, 1), "slot0 primary plus slot2 post");
        Near(CharacterSourceGxProgram.Generate(p.Generators[1], new(.25f, .5f, 1), primary, post), new(0, .625f, 1), "slot1 primary plus identity post");

        m = M(); m.TexGens[0] = G(1, 0, 0); m.TexGens[0].Normalize = true;
        m.UvAnims = new[] { new Cmdl.UvAnim { Mode = 0 } }; p = CharacterSourceGxProgram.Compile(m);
        var view = Rows(0, -1, 0, 100, 1, 0, 0, -70, 0, 0, 1, 5);
        var env = p.Matrices(0, view);
        Near(CharacterSourceGxProgram.Generate(p.Generators[0], new(0, 2, 0), env.Primary, env.Post), new(0, .5f, 1), "environment view rotation strips translation then normalizes");
        env = p.Matrices(0, Matrix4x4.Identity);
        Near(CharacterSourceGxProgram.Generate(p.Generators[0], new(0, 0, -1), env.Primary, env.Post), new(.5f, .5f, 1), "environment post restores Q one for negative Z");
        Near(CharacterSourceGxProgram.Generate(p.Generators[0], new(1, 0, 0), env.Primary, env.Post), new(1, .5f, 1), "raw-normal texgen named preview domain");
        Near(CharacterSourceGxProgram.Generate(p.Generators[0], new(0, 1, 0), env.Primary, env.Post), new(.5f, 0, 1), "weighted-normal alternative has observably different coordinates");
        Refuses(() => CharacterSourceGxProgram.Generate(g, Vector3.One, new Matrix4x4[2], post), "primary slot table length admission");
        Refuses(() => CharacterSourceGxProgram.Generate(g, Vector3.One, primary, new Matrix4x4[4]), "post slot table length admission");
    }

    private static void ShaderAndSnapshots()
    {
        var m = M(); m.Channels[0] = 0x3001; m.TexGens[0] = G(1, 0, 0); m.TexGens[0].Normalize = true;
        m.UvAnims = new[] { new Cmdl.UvAnim { Mode = 2, Params = new[] { .125, .25, .5, .75 } } };
        var p = CharacterSourceGxProgram.Compile(m); string vs = p.Vertex120, fs = p.Fragment120;
        m.TexIds[0] = 999; m.Stages[0].ColorIn[1] = 15; m.TexGens[0] = G(4, 30, 61); m.UvAnims[0].Params[0] = 99;
        Check(p.Textures[0] == 17 && p.Stages[0].B == 8 && p.Generators[0].Source == 1 && p.Animations[0].S == .125,
            "compiled program detaches every mutable source table");
        Check(p.Vertex120 == vs && p.Fragment120 == fs && p.Lit, "source mutation cannot rewrite compiled shaders or channel metadata");
        Check(CharacterSourceGxProgram.Policy == "echoes-body-gx-preview-v1-raw-normal-zero-registers-white-channels-linear-top-repeat", "explicit preview policy identity");
        foreach (var (vertex, fragment, raw, sample) in new[] { (p.Vertex120, p.Fragment120, "gl_Normal", "texture2D"),
            (p.Vertex300, p.Fragment300, "a_normal", "texture") })
        {
            Check(vertex.Contains("vec3 t0=" + raw + ";", StringComparison.Ordinal)
                && vertex.Contains("mat3(gx_view*skin)*" + raw, StringComparison.Ordinal), "raw texgen normal remains separate from skinned lighting normal");
            int primary = vertex.IndexOf("dot(gx_primary[0]", StringComparison.Ordinal), normalize = vertex.IndexOf("t0=length", StringComparison.Ordinal), post = vertex.IndexOf("dot(gx_post[0]", StringComparison.Ordinal);
            Check(primary >= 0 && normalize > primary && post > normalize, "both shader dialects preserve primary/normalize/post order");
            Check(fragment.Contains(sample + "(gx_tex0,gx_stq0.xy/gx_stq0.z)", StringComparison.Ordinal), "STQ divides after interpolation at fragment sample");
            int color = fragment.IndexOf("vec3 c0=", StringComparison.Ordinal), alpha = fragment.IndexOf("float a0=", StringComparison.Ordinal), write = fragment.IndexOf("r0.rgb=c0", StringComparison.Ordinal);
            Check(color >= 0 && alpha > color && write > alpha, "both equations gather before either register write");
            Check(fragment.Contains("r0=vec4(0.0),r1=vec4(0.0),r2=vec4(0.0),r3=vec4(0.0)", StringComparison.Ordinal), "all shader initial registers explicitly zero");
        }
        Check(p.Vertex120.StartsWith("#version 120\n", StringComparison.Ordinal)
            && p.Vertex300.StartsWith("#version 300 es\n", StringComparison.Ordinal), "portable shader dialect headers");
        Refuses(() => p.Evaluate(Array.Empty<Vector4>(), Vector4.Zero), "missing stage samples");
        Refuses(() => p.Evaluate(new[] { Vector4.One }, Vector4.Zero, new Vector4[3]), "wrong initial-register inventory");
    }

    private static void Refusals()
    {
        void Bad(Action<Cmdl.Material> edit, string why) { var m = M(); edit(m); Refuses(() => CharacterSourceGxProgram.Compile(m), why); }
        Bad(m => m.TexIds = Array.Empty<uint>(), "empty texture inventory");
        Bad(m => m.TexIds = new uint[5], "texture cap");
        Bad(m => m.Stages = Array.Empty<Cmdl.TevStage>(), "empty program");
        Bad(m => m.Stages = Enumerable.Range(0, 5).Select(_ => S()).ToArray(), "stage cap");
        Bad(m => m.TexGens = Array.Empty<Cmdl.TexGen>(), "empty texgens");
        Bad(m => m.TexGens = Enumerable.Repeat(G(4, 30, 61), 5).ToArray(), "texgen cap");
        Bad(m => m.UvAnims = Enumerable.Range(0, 4).Select(_ => new Cmdl.UvAnim()).ToArray(), "animation-slot cap");
        Bad(m => m.Konst = new[] { new byte[4] }, "source konst not in observed subset");
        Bad(m => m.IndTex = 17, "indirect texture");
        Bad(m => m.Flags |= Cmdl.IndStage, "indirect stage");
        Bad(m => m.Flags |= Cmdl.AlphaTest, "alpha test");
        Bad(m => m.Flags &= ~Cmdl.DepthWrite, "unknown depth-write policy");
        Bad(m => m.Flags &= ~Cmdl.DepthSort, "blended pass missing source sort flag");
        Bad(m => m.BlendSrc = 2, "unknown blend factor");
        foreach (uint channel in new uint[] { 0, 0x3002, 0x3003, uint.MaxValue }) Bad(m => m.Channels[0] = channel, "unknown compressed channel");
        Bad(m => m.Channels = new uint[] { 0x3000, 0x3000 }, "extra channel");
        Bad(m => m.TexGens[0].Type = 1, "unknown texgen type");
        Bad(m => m.TexGens[0].Src = 7, "unproved UV channel");
        Bad(m => m.TexGens[0].Mtx = 1, "unaligned matrix selector");
        Bad(m => m.TexGens[0].Post = 3, "unobserved post slot");
        Bad(m => m.UvAnims = new[] { new Cmdl.UvAnim { Mode = 1 } }, "unknown animation mode");
        Bad(m => m.UvAnims = new[] { new Cmdl.UvAnim { Mode = 0, Params = new[] { 1d } } }, "environment parameter extent");
        Bad(m => m.UvAnims = new[] { new Cmdl.UvAnim { Mode = 2, Params = new[] { 0d, 0d, 0d } } }, "scroll parameter extent");
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 10001d, -10001d })
            Bad(m => m.UvAnims = new[] { new Cmdl.UvAnim { Mode = 2, Params = new[] { value, 0d, 0d, 0d } } }, "invalid scroll scalar");
        foreach (uint op in new uint[] { 0, 0x101, 0x110, 0x140, 0x300 })
        {
            Bad(m => m.Stages[0].RawColorOp = op, "unknown color op/bias/scale/destination");
            Bad(m => m.Stages[0].RawAlphaOp = op, "unknown alpha op/bias/scale/destination");
        }
        var rawAuthority = M(); rawAuthority.Stages[0].ColorOp = new Cmdl.TevOp { Out = 99, Op = 1, Bias = 3, Scale = 3 };
        rawAuthority.Stages[0].AlphaOp = new Cmdl.TevOp { Out = -1, Op = 15 };
        var canonical = CharacterSourceGxProgram.Compile(rawAuthority);
        Check(canonical.Stages[0].ColorDestination == 0 && canonical.Stages[0].AlphaDestination == 0,
            "exact raw op is authoritative over contradictory decoded destination/operator fields");
        Near(canonical.Evaluate(new[] { new Vector4(.25f, .5f, .75f, .125f) }, Vector4.Zero), new(.25f, .5f, .75f, 1),
            "raw authority cannot be retargeted by decoded output fields");
        Bad(m => m.Stages[0].KAlpha = 28, "unobserved KAlpha fraction");
        Bad(m => m.Stages[0].KColor = 1, "unobserved KColor");
        Bad(m => m.Stages[0].Ras = 5, "unknown raster selector");
        Bad(m => m.Stages[0].ColorIn = new[] { 15, 8, 12 }, "color operand extent");
        Bad(m => m.Stages[0].AlphaIn[0] = 3, "unsupported alpha register read");
        Bad(m => m.Stages[0].ColorIn[0] = 1, "unsupported replicated alpha color read");
        Bad(m => m.Stages[0].Tex = 1, "texture reference outside inventory");
        Bad(m => m.Stages[0].TexCoord = 1, "texgen reference outside inventory");
    }

    private static Cmdl.Material M(params Cmdl.TevStage[] stages) => new()
    {
        Flags = Cmdl.DepthWrite | Cmdl.DepthSort, BlendSrc = 4, BlendDst = 5,
        TexIds = new uint[] { 17 }, Channels = new uint[] { 0x3000 },
        TexGens = new[] { G(4, 30, 61) }, Stages = stages.Length == 0 ? new[] { S() } : stages
    };
    private static CharacterSourceGxProgram Compile(params Cmdl.TevStage[] stages) => CharacterSourceGxProgram.Compile(M(stages));
    private static Cmdl.TexGen G(int source, int primary, int post) => new() { Type = 0, Src = source, Mtx = primary, Post = post };
    private static int[] OneAlpha() => new[] { 7, 6, 6, 7 };
    private static Cmdl.TevStage S(int[]? color = null, int[]? alpha = null, int colorDestination = 0, int alphaDestination = 0,
        int texture = 0, int coordinate = 0, int raster = 4)
    {
        uint c = colorDestination == 3 ? 0x700u : 0x100u, a = alphaDestination == 3 ? 0x700u : 0x100u;
        return new() { ColorIn = color ?? new[] { 15, 8, 12, 15 }, AlphaIn = alpha ?? OneAlpha(),
            RawColorOp = c, RawAlphaOp = a, ColorOp = Cmdl.TevOp.From(c), AlphaOp = Cmdl.TevOp.From(a),
            Tex = texture, TexCoord = coordinate, Ras = raster };
    }
    private static Matrix4x4[] Identities() => Enumerable.Repeat(Matrix4x4.Identity, 3).ToArray();
    // Literal mathematical rows, transposed into System.Numerics row-vector storage.
    private static Matrix4x4 Rows(float a, float b, float c, float d, float e, float f, float g, float h, float i, float j, float k, float l)
        => new(a, e, i, 0, b, f, j, 0, c, g, k, 0, d, h, l, 1);
    private static Vector4 SampleInvented(Vector3 stq)
    {
        float u = stq.X / stq.Z, v = stq.Y / stq.Z;
        int x = (int)MathF.Floor((u - MathF.Floor(u)) * 2), y = (int)MathF.Floor((v - MathF.Floor(v)) * 2);
        return (x, y) switch { (0, 0) => new(1, 0, 0, .25f), (1, 0) => new(0, 1, 0, .5f), (0, 1) => new(0, 0, 1, .75f), _ => new(1, 1, 0, 1) };
    }
    private static void Near(Vector3 actual, Vector3 expected, string why)
        => Check(Vector3.Distance(actual, expected) <= .000002f, why + $"; expected {expected}, got {actual}");
    private static void Near(Vector4 actual, Vector4 expected, string why)
        => Check(Vector4.Distance(actual, expected) <= .000002f, why + $"; expected {expected}, got {actual}");
    private static void Refuses(Action action, string why)
    {
        try { action(); } catch (InvalidDataException) { _checks++; return; }
        throw new Exception("GX expected InvalidDataException: " + why);
    }
    private static void Check(bool condition, string why)
    { _checks++; if (!condition) throw new Exception("GX acceptance: " + why); }
}
