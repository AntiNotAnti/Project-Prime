using System;
using System.Text.Json;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

/// <summary>Asset-free checks: capability facts and sample aggregation never invoke GL.</summary>
internal static class OpenGlModernizationCheck
{
    internal static int Run()
    {
        int failures = 0;
        void Check(bool condition, string name)
        {
            Console.WriteLine($"[glmodernizationcheck] {(condition ? "PASS" : "FAIL")}: {name}");
            if (!condition) failures++;
        }
        Check(Shaders.FragmentShader.Contains("depth_only && !use_override && !player_outline_mask", StringComparison.Ordinal)
            && Shaders.FragmentShader.Contains("cosmetic_skin == 0 && cosmetic_effect == 0 && cosmetic_dissolve <= 0.0", StringComparison.Ordinal)
            && Shaders.FragmentShader.Contains("apply_cosmetics(col);", StringComparison.Ordinal),
            "depth shader preserves full fallback for overrides, outlines and cosmetic coverage");
        Check(Shaders.FragmentShader.Contains("if (mat_mode == 1) a = mat_alpha * color.a;", StringComparison.Ordinal)
            && Shaders.FragmentShader.Contains("a = mat_alpha * texa * color.a;", StringComparison.Ordinal)
            && Shaders.FragmentShader.Contains("a = color.a * (mat_alpha * texa);", StringComparison.Ordinal)
            && Shaders.FragmentShader.Contains("else a = color.a * mat_alpha;", StringComparison.Ordinal),
            "depth shader retains native material alpha multiplication order");
#if ANDROID
        Check(EsShaders.VertexShader.Contains("uniform bool weighted_skinning;", StringComparison.Ordinal)
            && EsShaders.VertexShader.Contains("mtx_stack[j3] * weights.w", StringComparison.Ordinal)
            && EsShaders.VertexShader.Contains("!weighted_skinning && vtx_in_color.a == 0.0", StringComparison.Ordinal),
            "GLES palette deformation mirrors weighted desktop geometry and color semantics");
        Check(EsShaders.FragmentShader.Contains("if (alpha_test == 1 && a < 1.0) discard;", StringComparison.Ordinal)
            && EsShaders.FragmentShader.Contains("if (alpha_test == 2 && a >= 1.0) discard;", StringComparison.Ordinal),
            "GLES depth shader retains original alpha-test comparisons before early return");
#endif
        Check(Shaders.RttFragmentShader.Contains("hud_zero_alpha_discard && gl_FragColor.a == 0.0", StringComparison.Ordinal),
            "HUD discard only rejects exact zero final alpha behind a uniform gate");
#if ANDROID
        Check(EsShaders.RttFragmentShader.Contains("hud_zero_alpha_discard && frag_color.a == 0.0", StringComparison.Ordinal),
            "GLES HUD discard matches desktop exact-zero predicate");
        EsShaders.CheckInSync();
#endif
        var coverage = new RenderItem { Type = RenderItemType.Mesh };
        bool Coverage() => DepthCoveragePreparation.CanUse(coverage, true, false);
        Check(Coverage()
            && !DepthCoveragePreparation.CanUse(coverage, false, false)
            && !DepthCoveragePreparation.CanUse(coverage, true, true),
            "depth CPU preparation requires actual depth shader and excludes color and outline draws");
        foreach (RenderItemType type in Enum.GetValues<RenderItemType>())
        {
            if (type == RenderItemType.Mesh) continue;
            coverage.Type = type;
            Check(!Coverage(), "procedural " + type + " preserves full preparation");
        }
        coverage.Type = RenderItemType.Mesh;
        coverage.OverrideColor = Vector4.One;
        Check(!Coverage(), "alpha-bearing override forces full preparation");
        coverage.OverrideColor = null;
        coverage.Cosmetics = default(MphRead.Mods.Cosmetics.CosmeticSurface) with { Skin = 1 };
        Check(!Coverage(), "active cosmetic skin forces full preparation");
        coverage.Cosmetics = default(MphRead.Mods.Cosmetics.CosmeticSurface) with { Effect = 1 };
        Check(!Coverage(), "active cosmetic effect forces full preparation");
        coverage.Cosmetics = default(MphRead.Mods.Cosmetics.CosmeticSurface) with { Dissolve = .01f };
        Check(!Coverage(), "dissolving geometry keeps original discard preparation");
        coverage.Cosmetics = default(MphRead.Mods.Cosmetics.CosmeticSurface) with { Dissolve = float.NaN };
        Check(!Coverage(), "unknown dissolve state fails closed");
        coverage.Cosmetics = default(MphRead.Mods.Cosmetics.CosmeticSurface) with { Dissolve = float.PositiveInfinity };
        Check(!Coverage(), "nonfinite positive dissolve preserves full preparation");
        coverage.Cosmetics = default(MphRead.Mods.Cosmetics.CosmeticSurface) with { Dissolve = -.1f };
        Check(Coverage(), "nonactive negative dissolve follows exact shader guard");
        coverage.Cosmetics = default;
        coverage.Lighting = true; coverage.TexgenMode = TexgenMode.Normal;
        coverage.BillboardMode = BillboardMode.Sphere; coverage.WeightedSkinning = true;
        coverage.ViewModel = true; coverage.HasTexture = true; coverage.TextureBindingId = 123;
        coverage.Alpha = .375f; coverage.PolygonMode = PolygonMode.Toon;
        coverage.Diffuse = new Vector3(.2f, .3f, .4f); coverage.Ambient = new Vector3(.7f);
        coverage.Specular = new Vector3(.8f); coverage.Emission = new Vector3(.9f);
        coverage.PaletteOverride = new Vector4(.1f, .2f, .3f, .4f);
        coverage.Transform = Matrix4.CreateTranslation(1, 2, 3);
        coverage.TexcoordMatrix = Matrix4.CreateScale(2, 3, 1);
        coverage.MatrixStackCount = 1; coverage.MatrixStack[0] = .25f;
        var coveragePalette = coverage.MatrixStack;
        var coverageTransform = coverage.Transform; var coverageUv = coverage.TexcoordMatrix;
        Check(Coverage() && ReferenceEquals(coverage.MatrixStack, coveragePalette)
            && coverage.MatrixStack[0] == .25f && coverage.Transform == coverageTransform
            && coverage.TexcoordMatrix == coverageUv && coverage.Alpha == .375f
            && coverage.TextureBindingId == 123 && coverage.Lighting && coverage.WeightedSkinning
            && coverage.BillboardMode == BillboardMode.Sphere && coverage.ViewModel,
            "eligibility preserves current geometry, normal UV, weights, alpha and base texture data");
        coverage.Diffuse = Vector3.Zero; coverage.Ambient = Vector3.Zero;
        coverage.Specular = Vector3.Zero; coverage.Emission = Vector3.Zero;
        coverage.PaletteOverride = null;
        Check(Coverage(), "RGB material and palette changes do not alter alpha-only eligibility");
        coverage.HasTexture = false; coverage.PolygonMode = PolygonMode.Decal;
        Check(Coverage(), "untextured and decal alpha arithmetic remains eligible without texture assumptions");

        var legacy = new OpenGlCapabilities("2.1 Apple", "", _ => true);
        Check(!legacy.IsEs && legacy.Major == 2 && legacy.Minor == 1,
            "macOS compatibility version remains GL2.1");
        Check(!legacy.VertexArrays && !legacy.TimerQueries && !legacy.UniformBuffers && !legacy.PersistentStorage,
            "GL2.1 requires advertised extensions");
        Check(!legacy.FramebufferBlit
            && new OpenGlCapabilities("2.1", "GL_EXT_framebuffer_blit", _ => true).FramebufferBlit
            && !new OpenGlCapabilities("2.1", "GL_EXT_framebuffer_blit", n => n != "glBlitFramebuffer").FramebufferBlit
            && new OpenGlCapabilities("OpenGL ES 3.0", "", _ => true).FramebufferBlit,
            "depth blit requires context support and callable entry points");
        var missing = new OpenGlCapabilities("4.6 vendor", "", name => name != "glBindVertexArray");
        Check(!missing.VertexArrays && missing.UniformBuffers,
            "missing required entry point disables only affected capability");
        var token = new OpenGlCapabilities("2.1", "GL_ARB_vertex_array_object_fake GL_ARB_timer_query_extra", _ => true);
        Check(!token.VertexArrays && !token.TimerQueries, "extension names match whole tokens");
        var extension = new OpenGlCapabilities("2.1", "GL_ARB_vertex_array_object", _ => true);
        Check(extension.VertexArrays, "desktop ARB VAO extension is independently supported");
        var es = new OpenGlCapabilities("OpenGL ES 3.0 vendor",
            "GL_ARB_timer_query GL_ARB_buffer_storage GL_ARB_vertex_array_object", _ => true);
        Check(es.IsEs && es.VertexArrays && es.UniformBuffers && !es.TimerQueries && !es.PersistentStorage,
            "GLES3 core features do not inherit desktop-only extensions");
        var es2 = new OpenGlCapabilities("OpenGL ES 2.0", "GL_ARB_vertex_array_object", _ => true);
        Check(!es2.VertexArrays, "GLES2 cannot inherit ARB VAO");
        var disjoint = new OpenGlCapabilities("OpenGL ES 3.0", "GL_EXT_disjoint_timer_query", _ => true);
        Check(disjoint.DisjointTimerQueries && !disjoint.TimerQueries,
            "GLES disjoint timer support remains separate");
        Check(!legacy.VertexArrays && extension.VertexArrays, "separate context facts never reuse capability decisions");
        var apple = new OpenGlCapabilities("2.1 Apple", "GL_APPLE_vertex_array_object", _ => true);
        Check(!apple.VertexArrays && apple.AppleVertexArrays
            && apple.DesktopVertexArrayApi == OpenGlVertexArrayApi.Apple,
            "GL2.1 selects verified APPLE VAO without changing core capability fact");
        foreach (string missingEntry in new[] { "glGenVertexArraysAPPLE", "glBindVertexArrayAPPLE",
            "glDeleteVertexArraysAPPLE", "glIsVertexArrayAPPLE" })
        {
            var incompleteApple = new OpenGlCapabilities("2.1 Apple", "GL_APPLE_vertex_array_object",
                name => name != missingEntry);
            Check(!incompleteApple.AppleVertexArrays
                && incompleteApple.DesktopVertexArrayApi == OpenGlVertexArrayApi.None,
                missingEntry + " is required for APPLE VAO selection");
        }
        var falseApple = new OpenGlCapabilities("2.1", "GL_APPLE_vertex_array_object_fake", _ => true);
        Check(!falseApple.AppleVertexArrays, "APPLE extension matching uses exact whole tokens");
        var esApple = new OpenGlCapabilities("OpenGL ES 3.0", "GL_APPLE_vertex_array_object", _ => true);
        Check(esApple.VertexArrays && !esApple.AppleVertexArrays
            && esApple.DesktopVertexArrayApi == OpenGlVertexArrayApi.None,
            "GLES core fact is preserved while desktop APPLE selection is excluded");
        var bothVao = new OpenGlCapabilities("3.2", "GL_APPLE_vertex_array_object", _ => true);
        Check(bothVao.DesktopVertexArrayApi == OpenGlVertexArrayApi.Core,
            "verified core VAO takes precedence over APPLE");
        var arbApple = new OpenGlCapabilities("2.1", "GL_ARB_vertex_array_object GL_APPLE_vertex_array_object", _ => true);
        Check(arbApple.DesktopVertexArrayApi == OpenGlVertexArrayApi.Core,
            "verified ARB VAO takes precedence over APPLE on GL2.1");
        Check(legacy.DesktopVertexArrayApi == OpenGlVertexArrayApi.None
            && apple.DesktopVertexArrayApi == OpenGlVertexArrayApi.Apple,
            "independent GL2.1 contexts cannot inherit APPLE VAO capability");
        var malformed = new OpenGlCapabilities("unknown", "", _ => true);
        Check(malformed.Major == 0 && !malformed.VertexArrays, "invalid version fails closed");
        Check(!OpenGlOptimizationPolicy.Select(true, false, null), "unvalidated experiment defaults off");
        Check(OpenGlOptimizationPolicy.Select(true, false, "1"), "explicit supported experiment enabled");
        Check(!OpenGlOptimizationPolicy.Select(false, false, "1", true), "unsupported feature always disabled");
        Check(!OpenGlOptimizationPolicy.Select(true, true, "1", true), "forced legacy takes precedence");
        Check(!OpenGlOptimizationPolicy.Select(true, false, "0", true), "explicit zero overrides validated default");
        Check(!OpenGlOptimizationPolicy.Select(true, false, "true"), "invalid experiment token cannot opt in");
        Check(OpenGlOptimizationPolicy.Select(true, false, null, true), "validated default requires capability");

        foreach (string flag in new[] { "VAO", "DYNAMIC_STREAM", "UNIFORM_REVISIONS", "STATIC_CACHE", "SHADOW_FAST", "HUD_SAMPLERS",
            "ATLAS_EVICTION", "HUD_DISCARD", "DEPTH_SHADER", "GLES_UNIFORMS", "DEPTH_BATCH", "HUD_GLYPHS", "HUD_SPRITES", "DEPTH_PREP", "PROCEDURAL_BATCH", "ROOM_CACHE", "DEPTH_KEEP", "SHADER_VARIANTS", "ROOM_GROUPS", "PLAIN_SHADER", "SHADOW_PREP", "ROOM_IDENTITY", "MESH_OBJECT", "UNLIT_SHADER", "SHADOW_GROUPS" })
        {
            Check(OpenGlOptimizationPolicy.DefaultEnabled(flag)
                && OpenGlOptimizationPolicy.Select(true, false, null, OpenGlOptimizationPolicy.DefaultEnabled(flag)),
                flag + " defaults enabled when supported");
            Check(!OpenGlOptimizationPolicy.Select(true, false, "0", OpenGlOptimizationPolicy.DefaultEnabled(flag)),
                flag + " respects explicit disable");
            Check(!OpenGlOptimizationPolicy.Select(true, true, "1", OpenGlOptimizationPolicy.DefaultEnabled(flag)),
                flag + " respects force legacy");
        }
        foreach (string flag in new[] { "DEPTH_VERTEX", "PROCEDURAL_STREAM" })
        {
            Check(!OpenGlOptimizationPolicy.DefaultEnabled(flag)
                && !OpenGlOptimizationPolicy.Select(true, false, null, OpenGlOptimizationPolicy.DefaultEnabled(flag)),
                flag + " remains opt-in after mixed frame measurements");
            Check(OpenGlOptimizationPolicy.Select(true, false, "1", false)
                && !OpenGlOptimizationPolicy.Select(true, true, "1", false), flag + " opt-in respects legacy");
        }
        Check(ReferenceEquals(Shaders.SelectVertexShader(false), Shaders.LegacyVertexShader)
            && ReferenceEquals(Shaders.SelectVertexShader(true), Shaders.VertexShader), "vertex source selection uses stable legacy and fast sources");
        Check(!Shaders.LegacyVertexShader.Contains("depth_vertex_fast", StringComparison.Ordinal)
            && !Shaders.LegacyVertexShader.Contains("uniform bool depth_only;", StringComparison.Ordinal)
            && Shaders.LegacyVertexShader.Contains("vec4 vtx_color = weighted_skinning", StringComparison.Ordinal)
            && Shaders.LegacyVertexShader.Contains("transpose(tex_mtx * (use_light ? view_mtx", StringComparison.Ordinal),
            "legacy vertex removes specialization while preserving color, skinning and texgen");
#if ANDROID
        Check(ReferenceEquals(EsShaders.Translate(Shaders.LegacyVertexShader), EsShaders.LegacyVertexShader)
            && !EsShaders.LegacyVertexShader.Contains("depth_vertex_fast", StringComparison.Ordinal)
            && EsShaders.LegacyVertexShader.Contains("mtx_stack[j0] * weights.x", StringComparison.Ordinal),
            "legacy GLES source translates and retains weighted skinning");
#endif
        Check(!OpenGlOptimizationPolicy.DefaultEnabled("UNKNOWN_FUTURE_FEATURE"), "future feature is not implicitly enabled");

        var palette = new MatrixPaletteUploadCache();
        object source = new(), otherSource = new();
        Check(!palette.Matches(7, 2, 4, source, 1, 1f), "unrecorded palette cannot skip upload");
        palette.Record(7, 2, 4, source, 1, 1f);
        Check(palette.Matches(7, 2, 4, source, 1, 1f), "same immutable palette stamp can skip upload");
        Check(!palette.Matches(8, 2, 4, source, 1, 1f)
            && !palette.Matches(7, 3, 4, source, 1, 1f)
            && !palette.Matches(7, 2, 5, source, 1, 1f),
            "program, uniform location and palette length each isolate uploads");
        Check(!palette.Matches(7, 2, 4, otherSource, 1, 1f)
            && !palette.Matches(7, 2, 4, source, 2, 1f)
            && !palette.Matches(7, 2, 4, source, 1, 2f),
            "source identity, pose revision and scale each isolate uploads");
        Check(!palette.Matches(0, 2, 4, source, 1, 1f)
            && !palette.Matches(7, -1, 4, source, 1, 1f)
            && !palette.Matches(7, 2, 4, null, 1, 1f)
            && !palette.Matches(7, 2, 4, source, 0, 1f),
            "unknown source/revision/program/location always fall back");
        palette.Invalidate();
        Check(!palette.Matches(7, 2, 4, source, 1, 1f), "ordinary writes and lifecycle invalidation erase stamp");
        palette.Record(8, 3, 2, otherSource, 3, 0.5f);
        Check(palette.Matches(8, 3, 2, otherSource, 3, 0.5f)
            && !palette.Matches(7, 2, 4, source, 1, 1f), "replacement stamp never revives prior palette");

        palette.RegisterLayout(7, 40, 41);
        palette.Record(7, 2, 4, source, 1, 1f);
        palette.MatrixWrite(7, 40);
        Check(palette.Matches(7, 2, 4, source, 1, 1f), "declared independent camera matrix preserves palette stamp");
        palette.RegisterLayout(8, 50, 51);
        palette.MatrixWrite(7, 40);
        Check(palette.Matches(7, 2, 4, source, 1, 1f), "registering another program retains original independent matrix layout");
        palette.ForgetLayout(8); palette.MatrixWrite(7, 41);
        Check(palette.Matches(7, 2, 4, source, 1, 1f), "relink or delete of another program preserves original palette layout and stamp");
        palette.ForgetLayout(7);
        Check(!palette.Matches(7, 2, 4, source, 1, 1f), "own program relink or deletion invalidates its palette stamp");
        palette.Record(7, 2, 4, source, 1, 1f); palette.MatrixWrite(7, 40);
        Check(!palette.Matches(7, 2, 4, source, 1, 1f), "deleted program layout cannot survive recycled locations");
        palette.RegisterLayout(7, 40, 41); palette.Record(7, 2, 4, source, 1, 1f);
        palette.MatrixWrite(7, 3);
        Check(!palette.Matches(7, 2, 4, source, 1, 1f), "unknown or interior array writes invalidate palette");
        palette.Record(7, 2, 4, source, 1, 1f);
        palette.ForgetLayout();
        Check(!palette.Matches(7, 2, 4, source, 1, 1f), "relink erases layout and palette stamp");
        palette.Record(7, 2, 4, source, 1, 1f);
        palette.MatrixWrite(7, 40);
        Check(!palette.Matches(7, 2, 4, source, 1, 1f), "forgotten layout cannot preserve stale camera location");

#if !MPHREAD_SERVER
        var matrixCache = new LegacyGlUniformCache();
        Matrix4 cachedIdentity = Matrix4.Identity;
        var multiProgramMatrices = new LegacyGlUniformCache();
        multiProgramMatrices.RegisterMatrixArrayLayout(7, new[] { 10, 30 }, new[] { 41 });
        multiProgramMatrices.RegisterMatrixArrayLayout(8, new[] { 20, 40 }, new[] { 51 });
        multiProgramMatrices.UseProgram(7); multiProgramMatrices.SubmitMatrix4(41, false, in cachedIdentity);
        multiProgramMatrices.InvalidateMatrix4Array(10, 1);
        Check(!multiProgramMatrices.SubmitMatrix4(41, false, in cachedIdentity), "other program registration preserves independent matrix cache");
        multiProgramMatrices.InvalidateProgram(8);
        multiProgramMatrices.SubmitMatrix4(41, false, in cachedIdentity); multiProgramMatrices.InvalidateMatrix4Array(10, 1);
        Check(!multiProgramMatrices.SubmitMatrix4(41, false, in cachedIdentity), "other program relink or deletion retains original verified array layout");
        multiProgramMatrices.InvalidateProgram(7);
        multiProgramMatrices.SubmitMatrix4(41, false, in cachedIdentity); multiProgramMatrices.InvalidateMatrix4Array(10, 1);
        Check(multiProgramMatrices.SubmitMatrix4(41, false, in cachedIdentity), "own program relink or deletion restores conservative matrix fallback");
        multiProgramMatrices.RegisterMatrixArrayLayout(7, new[] { 10 }, new[] { 41 }); multiProgramMatrices.ResetContext();
        multiProgramMatrices.UseProgram(7); multiProgramMatrices.SubmitMatrix4(41, false, in cachedIdentity);
        multiProgramMatrices.InvalidateMatrix4Array(10, 1);
        Check(multiProgramMatrices.SubmitMatrix4(41, false, in cachedIdentity), "context reset forgets all program layouts");
        int[] arrayLocations = { 10, 30, 80 }; // Deliberately noncontiguous driver locations.
        int[] independentLocations = { 41, 90, -1 };
        Check(matrixCache.RegisterMatrixArrayLayout(7, arrayLocations, independentLocations),
            "explicit noncontiguous palette layout accepts disjoint independent matrices");
        matrixCache.UseProgram(8); matrixCache.SubmitMatrix4(41, false, in cachedIdentity);
        matrixCache.UseProgram(7);
        matrixCache.SubmitMatrix4(41, false, in cachedIdentity);
        matrixCache.SubmitMatrix4(90, false, in cachedIdentity);
        matrixCache.SubmitMatrix4(10, false, in cachedIdentity);
        matrixCache.SubmitMatrix4(31, false, in cachedIdentity);
        // Registration owns its proof; mutation of caller arrays cannot change it.
        arrayLocations[1] = 999; independentLocations[0] = 10;
        matrixCache.InvalidateMatrix4Array(30, 2);
        Check(!matrixCache.SubmitMatrix4(41, false, in cachedIdentity)
            && !matrixCache.SubmitMatrix4(90, false, in cachedIdentity)
            && matrixCache.SubmitMatrix4(10, false, in cachedIdentity)
            && matrixCache.SubmitMatrix4(31, false, in cachedIdentity),
            "known palette write preserves only declared independent matrices without guessed location stride");
        matrixCache.UseProgram(8);
        Check(matrixCache.SubmitMatrix4(41, false, in cachedIdentity),
            "targeted retention never mistakes another program's same location for current state");
        matrixCache.UseProgram(7);
        matrixCache.InvalidateMatrix4Array(30, 3);
        Check(matrixCache.SubmitMatrix4(41, false, in cachedIdentity),
            "oversize/interior palette writes use full matrix invalidation fallback");
        matrixCache.InvalidateMatrix4Array(77, 1);
        Check(matrixCache.SubmitMatrix4(41, false, in cachedIdentity),
            "unknown array write invalidates independent single matrices conservatively");
        matrixCache.InvalidateMatrix4Array(10, 0);
        Check(matrixCache.SubmitMatrix4(41, false, in cachedIdentity),
            "zero/invalid array count cannot authorize retention");
        Check(!matrixCache.RegisterMatrixArrayLayout(7, new[] { 10, 30 }, new[] { 30, 41 }),
            "overlapping array/independent declarations reject layout and erase prior proof");
        matrixCache.InvalidateMatrix4Array(10, 1);
        Check(matrixCache.SubmitMatrix4(41, false, in cachedIdentity),
            "failed layout registration cannot revive prior independent retention");
        Check(!matrixCache.RegisterMatrixArrayLayout(7, new[] { 10, -1 }, new[] { 41 })
            && !matrixCache.RegisterMatrixArrayLayout(7, new[] { 10, 10 }, new[] { 41 })
            && !matrixCache.RegisterMatrixArrayLayout(0, new[] { 10 }, new[] { 41 })
            && !matrixCache.RegisterMatrixArrayLayout(7, new[] { 10 }, new[] { -1 }),
            "inactive/duplicate array locations, unknown programs and empty independent proofs fail closed");
        matrixCache.RegisterMatrixArrayLayout(7, new[] { 10, 30 }, new[] { 41 });
        matrixCache.SubmitMatrix4(41, false, in cachedIdentity);
        matrixCache.InvalidateAll(); // Same operation used for relink/delete.
        matrixCache.SubmitMatrix4(41, false, in cachedIdentity);
        matrixCache.InvalidateMatrix4Array(10, 1);
        Check(matrixCache.SubmitMatrix4(41, false, in cachedIdentity),
            "relink invalidation forgets explicit palette layout");
        matrixCache.RegisterMatrixArrayLayout(7, new[] { 10 }, new[] { 41 });
        matrixCache.ResetContext(); matrixCache.UseProgram(7);
        matrixCache.SubmitMatrix4(41, false, in cachedIdentity);
        matrixCache.InvalidateMatrix4Array(10, 1);
        Check(matrixCache.SubmitMatrix4(41, false, in cachedIdentity),
            "context reset forgets independent matrix proof");
        matrixCache.RegisterMatrixArrayLayout(7, new[] { 10 }, new[] { 41 });
        matrixCache.SubmitMatrix4(10, false, in cachedIdentity);
        matrixCache.InvalidateMatrix4Array(10, 1); // Warm removal storage before measuring.
        long matrixStart = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            matrixCache.SubmitMatrix4(10, false, in cachedIdentity);
            matrixCache.InvalidateMatrix4Array(10, 1);
            matrixCache.SubmitMatrix4(41, false, in cachedIdentity);
        }
        Check(GC.GetAllocatedBytesForCurrentThread() == matrixStart,
            "warmed targeted matrix invalidation retains reusable removal storage without allocations");
        int[] ownerLocations = { 40 };
        palette.RegisterLayout(7, ownerLocations); palette.Record(7, 2, 4, source, 1, 1f);
        ownerLocations[0] = 3;
        palette.MatrixWrite(7, 40);
        Check(palette.Matches(7, 2, 4, source, 1, 1f), "palette layout owns independent location proof snapshot");

#endif
        var samples = new OpenGlSampleWindow(4);
        using (JsonDocument empty = JsonDocument.Parse(JsonSerializer.Serialize(samples.Summary())))
            Check(empty.RootElement.GetProperty("samples").GetInt32() == 0
                && empty.RootElement.GetProperty("p99Ms").ValueKind == JsonValueKind.Null,
                "absent samples have null percentiles");
        Check(!samples.Add(double.NaN) && !samples.Add(double.PositiveInfinity) && !samples.Add(-1)
            && samples.Count == 0, "invalid samples do not enter statistics");
        Check(samples.Add(4) && samples.Add(1) && samples.Add(3) && samples.Add(2) && !samples.Add(5),
            "sample storage is bounded");
        using (JsonDocument summary = JsonDocument.Parse(JsonSerializer.Serialize(samples.Summary())))
            Check(summary.RootElement.GetProperty("p50Ms").GetDouble() == 2
                && summary.RootElement.GetProperty("p95Ms").GetDouble() == 4
                && summary.RootElement.GetProperty("p99Ms").GetDouble() == 4,
                "nearest-rank percentiles preserve independent tails");
        samples.Reset();
        Check(samples.Count == 0 && samples.Add(0), "window reset drops prior samples and accepts measured zero");
        using (JsonDocument summary = JsonDocument.Parse(JsonSerializer.Serialize(samples.Summary())))
            Check(summary.RootElement.GetProperty("p99Ms").GetDouble() == 0, "reset cannot leak prior percentile");
        // Stale/default scope tokens must be safe after a context reset without a GL context.
        OpenGlPassProfiler.Scope stale = OpenGlPassProfiler.Measure(OpenGlCpuPass.World);
        OpenGlPassProfiler.ResetContext();
        stale.Dispose();
        default(OpenGlPassProfiler.Scope).Dispose();
        new OpenGlPassProfiler.Scope(-1, (OpenGlCpuPass)(-1), 1).Dispose();
        OpenGlPassProfiler.CancelFrame();
        OpenGlPassProfiler.EndFrame();
        Check(true, "context reset safely invalidates inactive scope tokens");
        return failures == 0 ? 0 : 1;
    }
}
