using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Import;
using MphRead.Mods.Platform;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Graphics.OpenGL;

// Explicit Mac GPU transport/presentation probe. The product importer and
// LauncherHunter side Scene are exercised; no source picture/pose is retained.
internal static class CharacterGeneratedPreviewOwnedProbe
{
    private const string Id = "darksamus-echoes";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo SideScene = typeof(LauncherHunter).GetField("_scene", BindingFlags.Static | BindingFlags.NonPublic)!;
    private sealed record Frame(string Pixels, string Palette, int[] Lists, int[] Textures, int Segments, int Joints, bool Visible, CharacterWeightedRenderModel Model);

    internal static int Run(string rvz)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "prime-generated-character-gl-" + Guid.NewGuid().ToString("N"));
        string cwd = Environment.CurrentDirectory;
        string? oldData = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        string core = typeof(Scene).Assembly.Location, tool = typeof(CharacterGeneratedPreviewOwnedProbe).Assembly.Location;
        string? rvzBefore = null, rvzAfter = null, nativeBefore = null, nativeAfter = null, nativeRoot = null;
        string coreBefore = HashFile(core), toolBefore = HashFile(tool);
        string? error = null, errorSha = null, generation = null, failedCheck = null;
        bool cleaned = false; int checks = 0, frames = 0, chosen = -1;
        var observations = new List<object>();
        var checkpoints = new List<object>();
        var candidateFrames = new List<object>();
        var angleFrames = new List<object>();
        var cannonFrames = new List<object>();
        void Check(bool value, string why) { if (!value) { failedCheck = why; throw new InvalidDataException(why); } checks++; }
        try
        {
            Check(OperatingSystem.IsMacOS() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                == System.Runtime.InteropServices.Architecture.Arm64, "This actual GPU proof requires the qualified macOS arm64 source profile.");
            rvz = Path.GetFullPath(rvz); rvzBefore = HashFile(rvz);
            // Read only the existing configured path. Never copy original MPH
            // companions into the derived root or change the user's paths.txt.
            string config = Path.Combine(cwd, "paths.txt");
            if (!File.Exists(config)) config = AppPaths.PathsFile;
            Check(new FileInfo(config).Length is > 0 and <= 65536, "A bounded existing paths.txt is required.");
            string[] matches = File.ReadAllLines(config).Select(l => l.Split('=', 2))
                .Where(p => p.Length == 2 && p[0].Trim() == Ver.AMHE1).Select(p => p[1].Trim()).ToArray();
            Check(matches.Length == 1 && matches[0].Length > 0, "Exactly one configured owned AMHE1 root is required.");
            nativeRoot = Path.GetFullPath(matches[0], Path.GetDirectoryName(Path.GetFullPath(config))!);
            nativeBefore = NativeInventory(nativeRoot);
            Directory.CreateDirectory(temporary);
            string user = Path.Combine(temporary, "user-data"), imports = Path.Combine(temporary, "imports");
            Directory.CreateDirectory(user);
            File.WriteAllText(Path.Combine(user, "paths.txt"), Ver.AMHE1 + "=" + nativeRoot + "\nExport=" + Path.Combine(user, "Export") + "\n");
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", user);
            Directory.SetCurrentDirectory(user);
            ConsoleSetup.Run(); Paths.UpdatePaths(); Paths.MphKey = Ver.AMHE1;
            GraphicsBackendPolicy.Configure("opengl");
            RenderOptions.CharacterModelReplacements = false;
            RenderOptions.BrightSkins = false; RenderOptions.ShowCustomCosmetics = false;
            RenderOptions.ShowFps = false;
            RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz, imports, selectedIds: new[] { Id });
            var selection = CharacterSourcePreviewSelection.Load(Id, CancellationToken.None, imports);
            generation = selection.Generation;
            Check(selection.Clips.Any(c => c.Ordinal > 12), "Full imported clips beyond the old sparse twelve must be available.");
            var candidates = selection.Clips.Where(c => c.Ordinal > 12 && c.Duration > 0).Take(16).ToArray();
            Check(candidates.Length > 0, "Bounded genuine post-twelve candidates required.");
            object owner = new();
            string? firstContextPalette = null;
            for (int context = 0; context < 2; context++)
            {
                using var window = new RenderWindow(shell: true);
                window.ClientSize = new(640, 480); window.Context.MakeCurrent();
                Check(!window.HasScene, "Launcher window has no gameplay Scene.");
                int width = window.FramebufferSize.X, height = window.FramebufferSize.Y;
                Check(width is > 0 and <= 4096 && height is > 0 and <= 4096, "Actual framebuffer is bounded.");
                try
                {
                    LauncherHunter.Reset();
                    LauncherHunter.Wanted = true; LauncherHunter.CanPresent = () => !window.HasScene;
                    LauncherHunter.Hunter = Hunter.Samus; LauncherHunter.Suit = 0;
                    LauncherHunter.TransparentBackground = true; LauncherHunter.CinematicLighting = true;
                    LauncherHunter.DistanceScale = 1; LauncherHunter.PreviewSlot = -1; LauncherHunter.Formation = false;
                    LauncherHunter.Left = .05f; LauncherHunter.Top = .05f; LauncherHunter.Right = .95f; LauncherHunter.Bottom = .95f;
                    CosmeticPreview.Loadout = CosmeticLoadout.Default; CosmeticPreview.Mode = SkinContext.Biped;
                    CosmeticPreview.Yaw = 0; CosmeticPreview.Zoom = 1; CosmeticPreview.LoopDeath = false; CosmeticPreview.DeathRequest = 0;
                    Frame? a = null, b = null;
                    foreach (var clip in candidates.Where(c => chosen < 0 || c.Ordinal == chosen))
                    {
                        a = Draw(clip.Ordinal, 0, 0);
                        b = Draw(clip.Ordinal, clip.Duration * .5, 0);
                        candidateFrames.Add(new { Context = context, ClipOrdinal = clip.Ordinal, FirstVisible = a.Visible, HalfVisible = b.Visible,
                            PixelsChanged = a.Pixels != b.Pixels, PaletteChanged = a.Palette != b.Palette });
                        if (a.Visible && b.Visible && a.Palette != b.Palette && a.Pixels != b.Pixels) { chosen = clip.Ordinal; break; }
                    }
                    Check(chosen > 12 && a != null && b != null && a.Visible && b.Visible && a.Palette != b.Palette && a.Pixels != b.Pixels,
                        "An actual post-twelve clip changes source palettes AND GPU pixels.");
                    Check(a!.Lists.SequenceEqual(b!.Lists) && a.Textures.SequenceEqual(b.Textures), "Changing caller time reuses GPU resources.");
                    var chosenClip = candidates.Single(c => c.Ordinal == chosen);
                    var repeated = Draw(chosen, 0, 0);
                    Check(repeated.Palette == a.Palette && repeated.Lists.SequenceEqual(a.Lists) && repeated.Textures.SequenceEqual(a.Textures),
                        "A/B/A source pose and resource identity are deterministic.");
                    if (context == 0) firstContextPalette = repeated.Palette;
                    else Check(repeated.Palette == firstContextPalette, "Recreated context reproduces the same source palette content.");
                    var angles = new List<string>();
                    foreach (int angle in new[] { 0, 90, 180, 270 })
                    {
                        var frame = Draw(chosen, chosenClip.Duration * .5, angle);
                        angles.Add(frame.Pixels);
                        angleFrames.Add(new { Context = context, ViewAngle = angle, frame.Visible, PixelSha256 = frame.Pixels,
                            PaletteSha256 = frame.Palette });
                        Check(frame.Lists.SequenceEqual(a.Lists) && frame.Textures.SequenceEqual(a.Textures), "View angles do not replace model resources.");
                    }
                    Check(angles.Distinct().Count() == 4, "All four real GPU view angles differ.");
                    // Same page/pack changes presentation part. The cannon is
                    // the complete source-pose cut, not a second imported mesh.
                    var cannonA = Draw(32, 0, 0, CharacterModelPart.ViewModel);
                    var cannonB = Draw(32, 0, 90, CharacterModelPart.ViewModel);
                    var cannonAgain = Draw(32, 0, 0, CharacterModelPart.ViewModel);
                    var fit = cannonA.Model.DiagnosticPreviewFit;
                    bool fitFinite = Enumerable.Range(0, 4).All(row => Enumerable.Range(0, 4).All(column => float.IsFinite(fit[row, column])));
                    var native = ((ModelInstance)Get(Preview(CurrentScene()), "_model")!).Model;
                    cannonFrames.Add(new { Context = context, Stage = "fixed-cannon-before-visibility-check", AVisible = cannonA.Visible,
                        BVisible = cannonB.Visible, AgainVisible = cannonAgain.Visible, APixelSha256 = cannonA.Pixels,
                        BPixelSha256 = cannonB.Pixels, AgainPixelSha256 = cannonAgain.Pixels, APaletteSha256 = cannonA.Palette,
                        BPaletteSha256 = cannonB.Palette, AgainPaletteSha256 = cannonAgain.Palette,
                        cannonA.Segments, cannonA.Joints, DisplayLists = cannonA.Lists.Length, Textures = cannonA.Textures.Length,
                        FitFinite = fitFinite, FitPositiveScale = fitFinite && fit.Row0.Xyz.LengthSquared > 0 && fit.Determinant > 0,
                        ReferencedPresentation = PresentationBounds(cannonA.Model),
                        NativeMaterials = native.Materials.Select((m, index) => new { Index = index, m.Name, m.Alpha, m.CurrentAlpha,
                            RenderMode = m.RenderMode.ToString(), PolygonMode = m.PolygonMode.ToString(), m.Lighting, Culling = m.Culling.ToString() }).ToArray(),
                        Anchors = cannonA.Model.Segments.Select(s => new { s.NativeMaterialIndex, s.SourcePrimitiveOrdinal }).ToArray() });
                    Check(cannonA.Visible && cannonB.Visible && cannonAgain.Visible, "Every fixed source cannon view must produce visible pixels.");
                    Check(cannonA.Pixels != cannonB.Pixels, "Rotating the fixed source cannon must change GPU pixels.");
                    Check(cannonA.Palette == cannonB.Palette && cannonA.Palette == cannonAgain.Palette
                        && cannonA.Pixels == cannonAgain.Pixels && cannonA.Lists.SequenceEqual(cannonB.Lists)
                        && cannonA.Lists.SequenceEqual(cannonAgain.Lists) && cannonA.Textures.SequenceEqual(cannonB.Textures)
                        && cannonA.Textures.SequenceEqual(cannonAgain.Textures), "Cannon A/B/A keeps its fixed raw pose and GPU resources.");
                    Check(!ReferenceEquals(a.Model, cannonA.Model), "Changing presentation part replaces the bound body model.");
                    var bodyAgain = Draw(chosen, 0, 0);
                    Check(bodyAgain.Visible && bodyAgain.Palette == a.Palette && bodyAgain.Pixels == a.Pixels,
                        "Returning to body reproduces its original source pose and visible frame.");
                    Check(!ReferenceEquals(cannonA.Model, bodyAgain.Model), "Returning to body replaces the bound cannon model.");
                    cannonFrames.Add(new { Context = context, Triangles = 392, Indices = 1176, Material2Triangles = 66,
                        Material5Triangles = 326, ClipOrdinal = 32, Seconds = 0, FixedPaletteSha256 = cannonA.Palette,
                        PixelSha256 = cannonA.Pixels, RotatedPixelSha256 = cannonB.Pixels, ReturnedBody = true });
                    // Also retire a currently active cannon without waiting
                    // for the native preview's next simulation step.
                    var closingCannon = Draw(32, 0, 0, CharacterModelPart.ViewModel);
                    var scene = CurrentScene(); var entity = Preview(scene);
                    // Force the same already-selected native Hunter to skip its
                    // next60Hz step: request synchronization must still clear.
                    double scheduled = (double)Get(scene, "_launcherPreviewNextStep")!;
                    Set(scene, "_launcherPreviewNextStep", double.MaxValue);
                    long beforeStep = (long)Get(entity, "_previewSteps")!;
                    try
                    {
                        LauncherHunter.SourcePreviewRequest = null;
                        Clear(width, height); LauncherHunter.Draw(window, width, height); GL.Finish(); frames++;
                        Check(LauncherHunter.Drawn && GL.GetError() == ErrorCode.NoError && !entity.TryGetSourceRigPreview(out _)
                            && (long)Get(entity, "_previewSteps")! == beforeStep, "Immediate native frame clears source without a60Hz step.");
                        Check(closingCannon.Lists.All(id => !GL.IsList(id)), "Retired cannon display lists are deleted.");
                    }
                    finally { Set(scene, "_launcherPreviewNextStep", scheduled); }
                    // A subsequent change of native preview part uses its
                    // normal60Hz cadence, independently of the clear proof.
                    Thread.Sleep(20);
                    // Reattach then close explicitly; a wrong owner must not
                    // be able to release another active source presentation.
                    Draw(chosen, 0, 0);
                    LauncherHunter.ReleaseSourcePreview(new object());
                    Check(entity.TryGetSourceRigPreview(out _), "Foreign close owner cannot retire the source preview.");
                    LauncherHunter.ReleaseSourcePreview(owner);
                    Check(!entity.TryGetSourceRigPreview(out _), "Explicit page close retires its actual source binding.");
                    observations.Add(new { Context = context, ClipOrdinal = chosen, a.Segments, a.Joints,
                        PixelSha256 = a.Pixels, HalfPixelSha256 = b.Pixels, PaletteSha256 = a.Palette,
                        HalfPaletteSha256 = b.Palette, ViewPixelSha256 = angles.ToArray(), ImmediateNativeClear = true, ExplicitOwnerClose = true });

                    Frame Draw(int ordinal, double seconds, int angle, CharacterModelPart part = CharacterModelPart.Biped)
                    {
                        GlCheckpoint("before-clear");
                        CosmeticPreview.Mode = part == CharacterModelPart.ViewModel ? SkinContext.ViewModel : SkinContext.Biped;
                        CosmeticPreview.Yaw = angle * MathF.PI / 180;
                        LauncherHunter.SourcePreviewRequest = new(owner, selection.Pack, CharacterSourcePreviewSelection.Appearance, ordinal, seconds, part);
                        Clear(width, height); GlCheckpoint("after-clear");
                        LauncherHunter.Draw(window, width, height); GL.Finish(); frames++;
                        Check(LauncherHunter.Drawn, "Actual launcher-sideScene GPU draw succeeds without native fallback or swallowed render failure.");
                        GlCheckpoint("after-launcher-draw");
                        Check(!RenderOptions.CharacterModelReplacements, "Diagnostic source stage never enables the global replacement setting.");
                        var current = CurrentScene(); var preview = Preview(current);
                        bool sourceReady = preview.TryGetSourceRigPreview(out var model);
                        Check(current.SideScene && !window.HasScene && sourceReady, "Actual generated source binding is active; native fallback does not count.");
                        Check(ReferenceEquals(LauncherHunter.SourcePreviewRequest.Owner, owner)
                            && ReferenceEquals(LauncherHunter.SourcePreviewRequest.Pack, selection.Pack)
                            && LauncherHunter.SourcePreviewRequest.Part == part && preview.Mode == CosmeticPreview.Mode,
                            "Part changes retain the captured source owner/pack and matching preview mode.");
                        Check(ReferenceEquals(model!.Asset, selection.Pack.TryResolvePrivate(Hunter.Samus, CharacterSourcePreviewSelection.Appearance,
                            part, 0, out var asset) ? asset : null), "GPU model belongs to the captured generated pack and part.");
                        if (part == CharacterModelPart.ViewModel)
                        {
                            var nativeMaterials = ((ModelInstance)Get(preview, "_model")!).Model.Materials;
                            Check(model.Segments.All(s => (uint)s.NativeMaterialIndex < nativeMaterials.Count
                                && nativeMaterials[s.NativeMaterialIndex].Alpha == 31
                                && float.IsFinite(nativeMaterials[s.NativeMaterialIndex].CurrentAlpha)
                                && nativeMaterials[s.NativeMaterialIndex].CurrentAlpha >= .99999f
                                && nativeMaterials[s.NativeMaterialIndex].RenderMode == RenderMode.Normal
                                && nativeMaterials[s.NativeMaterialIndex].PolygonMode == PolygonMode.Modulate),
                                "Every cannon segment uses an opaque native anchor rather than an invisible animated glow.");
                            var source = Get(model, "_sourceGeometry") as CharacterWeightedModelData;
                            Check(ordinal == 32 && seconds == 0 && model.SourceCannon?.IsGenerated == true && source != null
                                && source.SourceAuthoringPose is { Count: 40 } && source.SourceRig?.Nodes.Count == 40,
                                "Cannon uses the complete generated fixed source pose at root32/framezero.");
                            Check(source != null && source.Primitives.Sum(p => p.Indices.Length) == 1176
                                && source.Primitives.All(p => p.SourceMaterialOrdinal is 2 or 5)
                                && source.Primitives.Where(p => p.SourceMaterialOrdinal == 2).Sum(p => p.Indices.Length / 3) == 66
                                && source.Primitives.Where(p => p.SourceMaterialOrdinal == 5).Sum(p => p.Indices.Length / 3) == 326
                                && source.Primitives.SelectMany(p => p.SourceTriangleIds ?? Array.Empty<int>()).Distinct().Count() == 392,
                                "GPU cannon retains exactly392 whole source triangles in material2/5 only.");
                        }
                        else Check(model.SourceCannon == null, "Returning to body cannot retain the cannon cut.");
                        int[] lists = model.Segments.Select(s => s.ListId).ToArray(), textures = model.Segments.Select(s => s.AlbedoBinding ?? 0).ToArray();
                        Check(model.IsSourcePreview && model.Joints.Count == 39 && lists.All(GL.IsList)
                            && textures.All(t => t != 0 && GL.IsTexture(t)), "Complete source rig and live diagnostic GPU resources required.");
                        GlCheckpoint("after-resource-queries");
                        string palette = PaletteHash(model);
                        byte[] pixels = new byte[checked(width * height * 4)];
                        int oldPack = GL.GetInteger(GetPName.PackAlignment);
                        try
                        {
                            GL.PixelStore(PixelStoreParameter.PackAlignment, 1); GlCheckpoint("after-pack-alignment");
                            GL.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                            GlCheckpoint("after-read-pixels");
                        }
                        finally { GL.PixelStore(PixelStoreParameter.PackAlignment, oldPack); }
                        GlCheckpoint("after-pack-restore");
                        try
                        {
                            bool visible = pixels.Where((_, i) => i % 4 != 3).Any(x => x != 0);
                            return new(Sha(pixels), palette, lists, textures, model.Segments.Count, model.Joints.Count, visible, model);
                        }
                        finally { Array.Clear(pixels); }

                        void GlCheckpoint(string stage)
                        {
                            ErrorCode status = GL.GetError();
                            checkpoints.Add(new { Context = context, Frame = frames, ClipOrdinal = ordinal,
                                Stage = stage, GlError = status.ToString(), Drawn = LauncherHunter.Drawn });
                            Check(status == ErrorCode.NoError, "GPU checkpoint " + stage + ": " + status);
                        }
                    }
                }
                finally
                {
                    LauncherHunter.SourcePreviewRequest = null; LauncherHunter.ReleaseSourcePreview(owner); LauncherHunter.Reset();
                    if (SideScene.GetValue(null) is Scene scene) { try { scene.UnloadGl(); } finally { SideScene.SetValue(null, null); } }
                    CosmeticPreview.Loadout = null; CosmeticPreview.Yaw = 0; CosmeticPreview.Zoom = 1;
                }
            }
        }
        catch (Exception ex) { error = ex.GetType().Name; errorSha = Sha(Encoding.UTF8.GetBytes(ex.Message)); }
        finally
        {
            try
            {
                Directory.SetCurrentDirectory(cwd); Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", oldData);
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
                cleaned = !Directory.Exists(temporary);
                if (rvzBefore != null) rvzAfter = HashFile(rvz);
                if (nativeRoot != null && nativeBefore != null) nativeAfter = NativeInventory(nativeRoot);
                Check(cleaned && rvzBefore != null && rvzBefore == rvzAfter && nativeBefore != null && nativeBefore == nativeAfter,
                    "All derived data deleted and both owned sources unchanged.");
                Check(coreBefore == HashFile(core) && toolBefore == HashFile(tool), "Core/tool assemblies unchanged during GPU proof.");
            }
            catch (Exception ex) { error ??= ex.GetType().Name; errorSha ??= Sha(Encoding.UTF8.GetBytes(ex.Message)); }
            Console.WriteLine(JsonSerializer.Serialize(new { Scope = "Actual generated normal Echoes diagnostic source GLB/full animation in LauncherHunter sideScene; no gameplay/GX/UI-intent/device-parity claim",
                Pass = error == null, Checks = checks, Frames = frames, Generation = generation, ChosenClipOrdinal = chosen,
                SourceSha256Before = rvzBefore, SourceSha256After = rvzAfter, NativeInventorySha256Before = nativeBefore,
                NativeInventorySha256After = nativeAfter, CoreSha256 = coreBefore, ToolSha256 = toolBefore,
                Contexts = observations, CandidateFrames = candidateFrames, AngleFrames = angleFrames, CannonFrames = cannonFrames, GlCheckpoints = checkpoints, TemporaryPayloadsDeleted = cleaned, FailedCheck = failedCheck, ErrorType = error, ErrorMessageSha256 = errorSha }));
        }
        return error == null ? 0 : 1;
    }
    private static Scene CurrentScene() => SideScene.GetValue(null) as Scene ?? throw new InvalidDataException("Launcher did not create its side Scene.");
    private static HunterPreviewEntity Preview(Scene scene) => Get(scene, "_preview") as HunterPreviewEntity ?? throw new InvalidDataException("Launcher did not create its owned preview.");
    private static object? Get(object value, string field) => value.GetType().GetField(field, Private)!.GetValue(value);
    private static void Set(object value, string field, object data) => value.GetType().GetField(field, Private)!.SetValue(value, data);
    private static void Clear(int width, int height)
    { GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest); GL.ClearColor(0, 0, 0, 1); GL.ClearDepth(1.0); GL.DepthMask(true); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); }
    private static string PaletteHash(CharacterWeightedRenderModel model)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var segment in model.Segments)
            foreach (float value in segment.SourceMatrixPalette ?? throw new InvalidDataException("Source palette absent."))
            { if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite source palette."); hash.AppendData(BitConverter.GetBytes(value)); }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static object PresentationBounds(CharacterWeightedRenderModel model)
    {
        var geometry = Get(model, "_sourceGeometry") as CharacterWeightedModelData
            ?? throw new InvalidDataException("Cannon diagnostic requires captured source geometry.");
        bool finite = true, allInside = true, anyInside = false; int corners = 0;
        for (int p = 0; p < geometry.Primitives.Count; p++)
        {
            var primitive = geometry.Primitives[p];
            var palette = model.Segments[p].SourcePresentationPalette
                ?? throw new InvalidDataException("Cannon diagnostic requires submitted source palettes.");
            foreach (uint index in primitive.Indices)
            {
                var vertex = primitive.Vertices[index]; var result = OpenTK.Mathematics.Vector3.Zero;
                for (int component = 0; component < 4; component++)
                {
                    float weight = vertex.Weights[component]; if (weight == 0) continue;
                    int offset = ((vertex.PackedJoints >> (5 * component)) & 31) * 16;
                    if (offset + 16 > palette.Length) throw new InvalidDataException("Submitted source palette bounds differ.");
                    var matrix = new OpenTK.Mathematics.Matrix4(palette[offset], palette[offset+1], palette[offset+2], palette[offset+3],
                        palette[offset+4], palette[offset+5], palette[offset+6], palette[offset+7], palette[offset+8], palette[offset+9],
                        palette[offset+10], palette[offset+11], palette[offset+12], palette[offset+13], palette[offset+14], palette[offset+15]);
                    result += OpenTK.Mathematics.Vector3.TransformPosition(new(vertex.Position.X, vertex.Position.Y, vertex.Position.Z), matrix) * weight;
                }
                bool valid = float.IsFinite(result.X) && float.IsFinite(result.Y) && float.IsFinite(result.Z);
                bool inside = valid && result.X >= -2 && result.X <= 2 && result.Y >= -.5f && result.Y <= 2.8f && result.Z >= -2 && result.Z <= 2;
                finite &= valid; allInside &= inside; anyInside |= inside; corners++;
            }
        }
        return new { ReferencedCorners = corners, AllFinite = finite, AllInsideExpectedPreviewWorldBox = allInside, AnyInsideExpectedPreviewWorldBox = anyInside };
    }
    private static string NativeInventory(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var pending = new Stack<string>(); pending.Push(root); int count = 0; long bytes = 0;
        while (pending.Count != 0)
        {
            string directory = pending.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Owned root inventory may not follow directory aliases.");
            foreach (string child in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal).Reverse()) pending.Push(child);
            foreach (string file in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
            {
                if (++count > 65536 || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0
                    || (bytes += new FileInfo(file).Length) > 1024L * 1024 * 1024) throw new InvalidDataException("Owned native inventory exceeds its read-only budget.");
                hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file))); hash.AppendData(new byte[] { 0 });
                hash.AppendData(Convert.FromHexString(HashFile(file)));
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static string HashFile(string file) { using var stream = File.OpenRead(file); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
