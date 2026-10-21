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

// Explicit qualified desktop GPU transport/presentation probe. The product importer and
// LauncherHunter side Scene are exercised; no source picture/pose is retained.
internal static class CharacterGeneratedPreviewOwnedProbe
{
    private const string Id = "darksamus-echoes";
    private const string FinalId = "darksamus-overcharged-echoes";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo SideScene = typeof(LauncherHunter).GetField("_scene", BindingFlags.Static | BindingFlags.NonPublic)!;
    private sealed record ClockSample(int SubmittedPackets, bool SubmittedSecondsMatch, double? MinimumSubmittedSeconds,
        double? MaximumSubmittedSeconds, int ProgramZeroAliases, int ProgramZeroLastSubmittedMaterial, int ActivePrimaryRows, string GpuPrimaryRowsSha256,
        string CpuIdentityPrimaryRowsSha256, bool ProgramZeroAllMode2, bool GpuRowsMatchCpuIdentity,
        bool AnyNonzeroScrollRates, int TextureUniqueRgbaCount);
    private sealed record Frame(string Pixels, string Palette, int[] Lists, int[] Textures, int[] Programs, int Segments, int Joints, bool Visible, CharacterWeightedRenderModel Model, ClockSample? Clock);

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
        var bodyPrograms = new List<object>();
        var materialFrames = new List<object>();
        var finalFrames = new List<object>();
        var admissionFailures = new List<object>();
        void Check(bool value, string why) { if (!value) { failedCheck ??= why; throw new InvalidDataException(why); } checks++; }
        try
        {
            Check(CharacterSourceRootWitness.QualifiedForCurrentProcess,
                "This actual GPU proof requires a qualified Windows x64 or macOS arm64 source profile.");
            rvz = Path.GetFullPath(rvz); rvzBefore = HashFile(rvz);
            // Read only the existing configured path. Never copy original MPH
            // companions into the derived root or change the user's paths.txt.
            string? explicitNative = Environment.GetEnvironmentVariable("PRIME_BODY_TEST_ASSETS");
            if (!string.IsNullOrWhiteSpace(explicitNative)) nativeRoot = Path.GetFullPath(explicitNative);
            else
            {
                string config = Path.Combine(cwd, "paths.txt");
                if (!File.Exists(config)) config = AppPaths.PathsFile;
                Check(new FileInfo(config).Length is > 0 and <= 65536, "A bounded existing paths.txt is required.");
                string[] matches = File.ReadAllLines(config).Select(l => l.Split('=', 2))
                    .Where(p => p.Length == 2 && p[0].Trim() == Ver.AMHE1).Select(p => p[1].Trim()).ToArray();
                Check(matches.Length == 1 && matches[0].Length > 0, "Exactly one configured owned AMHE1 root is required.");
                nativeRoot = Path.GetFullPath(matches[0], Path.GetDirectoryName(Path.GetFullPath(config))!);
            }
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
            RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz, imports, selectedIds: new[] { Id, FinalId });
            var normalSelection = CharacterSourcePreviewSelection.Load(Id, CancellationToken.None, imports);
            var finalSelection = CharacterSourcePreviewSelection.Load(FinalId, CancellationToken.None, imports);
            Check(normalSelection.HasCannon && finalSelection.HasCannon && !normalSelection.RuntimeCannon && !finalSelection.RuntimeCannon
                && normalSelection.SelectedAppearance != finalSelection.SelectedAppearance,
                "Normal and Final selections retain distinct body identities and cannon capability.");
            var capturedMaterials = new Dictionary<CharacterSourcePreviewSelection, CharacterSourceGxMaterials>();
            foreach (var captured in new[] { normalSelection, finalSelection })
            {
                bool found=captured.Pack.TryResolvePrivate(Hunter.Samus,captured.SelectedAppearance,CharacterModelPart.Biped,0,out var asset);
                Check(found,"Selected source body must resolve before creating a GL context.");
                bool issued=captured.Pack.TryCreateSourcePreviewLease(asset,out var lease);
                Check(issued&&lease.SourceMaterials!=null,"Selected source body must capture its complete source material archive.");
                capturedMaterials.Add(captured,lease.SourceMaterials!);
            }
            var selection = normalSelection;
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
                var contextPrograms=new HashSet<int>();var contextTextures=new HashSet<int>();var contextLists=new HashSet<int>();
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
                    LauncherHunter.SourcePreviewRequest=null;
                    Clear(width,height);LauncherHunter.Draw(window,width,height);GL.Finish();frames++;
                    Check(LauncherHunter.Drawn&&!Preview(CurrentScene()).TryGetSourceRigPreview(out _)&&GL.GetError()==ErrorCode.NoError,
                        "Ordinary native side Scene initializes before dirty upload-state and source admission checks.");
                    var failedResidents=AdmissionFailure();
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
                    var successfulResidents=Get(CurrentScene(),"_characterTextureAssets") as TextureAssetManager;
                    Check(successfulResidents!=null&&successfulResidents.ResidentCount==failedResidents.Count
                        &&successfulResidents.ResidentBytes==failedResidents.Bytes,
                        "Fresh-owner successful source admission reuses all bounded image residents from the rolled-back program attempt.");
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
                    MaterialClock(chosen, chosenClip.Duration * .5, "normal");
                    // A second source appearance uses the same actual Scene and
                    // renderer. Its complete nine-program body must not borrow
                    // the normal appearance's model, textures or source owner.
                    selection = finalSelection;
                    var finalClip=selection.Clips.First(c=>c.Duration>0);
                    var finalA=Draw(finalClip.Ordinal,finalClip.Duration*.5,0);
                    var finalB=Draw(finalClip.Ordinal,finalClip.Duration*.5,90);
                    var finalAgain=Draw(finalClip.Ordinal,finalClip.Duration*.5,0);
                    Check(finalA.Visible&&finalB.Visible&&finalAgain.Visible&&finalA.Pixels!=finalB.Pixels
                        &&finalA.Pixels==finalAgain.Pixels&&finalA.Palette==finalB.Palette&&finalA.Palette==finalAgain.Palette
                        &&finalA.Lists.SequenceEqual(finalB.Lists)&&finalA.Lists.SequenceEqual(finalAgain.Lists)
                        &&finalA.Textures.SequenceEqual(finalB.Textures)&&finalA.Textures.SequenceEqual(finalAgain.Textures)
                        &&finalA.Programs.SequenceEqual(finalB.Programs)&&finalA.Programs.SequenceEqual(finalAgain.Programs),
                        "Final nine-program body is visible, rotatable and deterministic with stable GPU resources.");
                    finalFrames.Add(new{Context=context,Character=FinalId,ClipOrdinal=finalClip.Ordinal,Triangles=12734,Programs=9,
                        PixelSha256=finalA.Pixels,RotatedPixelSha256=finalB.Pixels,PaletteSha256=finalA.Palette,
                        DisplayLists=finalA.Lists.Length,TextureBindings=finalA.Textures.Distinct().Count(),HasCannon=selection.HasCannon});
                    MaterialClock(finalClip.Ordinal,finalClip.Duration*.5,"Final");
                    var finalCannonA=Draw(32,0,0,CharacterModelPart.ViewModel);
                    var finalCannonB=Draw(32,0,90,CharacterModelPart.ViewModel);
                    var finalCannonAgain=Draw(32,0,0,CharacterModelPart.ViewModel);
                    Check(finalCannonA.Visible&&finalCannonB.Visible&&finalCannonAgain.Visible&&finalCannonA.Pixels!=finalCannonB.Pixels
                        &&finalCannonA.Pixels==finalCannonAgain.Pixels&&finalCannonA.Palette==finalCannonB.Palette&&finalCannonA.Palette==finalCannonAgain.Palette
                        &&finalCannonA.Lists.SequenceEqual(finalCannonB.Lists)&&finalCannonA.Lists.SequenceEqual(finalCannonAgain.Lists)
                        &&finalCannonA.Textures.SequenceEqual(finalCannonB.Textures)&&finalCannonA.Programs.SequenceEqual(finalCannonB.Programs),
                        "Final's own four-material cannon is visible and rotatable with fixed source pose and stable private GX resources.");
                    var finalBodyRestored=Draw(finalClip.Ordinal,finalClip.Duration*.5,0);
                    Check(finalBodyRestored.Visible&&finalBodyRestored.Model.SourceCannon==null
                        &&finalBodyRestored.Palette==finalA.Palette&&finalBodyRestored.Pixels==finalA.Pixels
                        &&capturedMaterials[finalSelection].Programs.All(p=>p.PresentationAlphaScale==1),
                        "Returning from Final cannon restores its exact nine-program body without shell alpha mutation.");
                    cannonFrames.Add(new{Context=context,Character=FinalId,Triangles=793,Indices=2379,
                        MaterialOrdinals=new[]{3,4,5,7},MaterialTriangles=new[]{392,66,314,21},ClipOrdinal=32,Seconds=0,
                        PixelSha256=finalCannonA.Pixels,RotatedPixelSha256=finalCannonB.Pixels,
                        FixedPaletteSha256=finalCannonA.Palette,PrivateShellAlpha=.55f,ReturnedBody=true});
                    selection=normalSelection;
                    var restoredNormal=Draw(chosen,0,0);
                    Check(restoredNormal.Visible&&restoredNormal.Palette==a.Palette&&restoredNormal.Pixels==a.Pixels,
                        "Returning from Final restores the normal captured appearance and exact original body frame.");
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

                    (int Count,long Bytes) AdmissionFailure()
                    {
                        var scene=CurrentScene();var preview=Preview(scene);var materials=capturedMaterials[normalSelection];
                        var createdPrograms=new List<int>();var createdShaders=new List<int>();int links=0;bool injected=false;
                        var prior=OpenGlFailureInjection.ShaderObjectCallback;
                        int oldUnit=GL.GetInteger(GetPName.ActiveTexture),oldProgram=GL.GetInteger(GetPName.CurrentProgram);
                        GL.ActiveTexture(TextureUnit.Texture0);int oldTexture=GL.GetInteger(GetPName.TextureBinding2D);
                        int oldPbo=GL.GetInteger(GetPName.PixelUnpackBufferBinding),oldAlignment=GL.GetInteger(GetPName.UnpackAlignment),
                            oldRows=GL.GetInteger(GetPName.UnpackRowLength),oldSkipRows=GL.GetInteger(GetPName.UnpackSkipRows),
                            oldSkipPixels=GL.GetInteger(GetPName.UnpackSkipPixels);
                        int pbo=GL.GenBuffer();
                        try
                        {
                            GL.BindBuffer(BufferTarget.PixelUnpackBuffer,pbo);GL.BufferData(BufferTarget.PixelUnpackBuffer,4096,IntPtr.Zero,BufferUsageHint.StaticDraw);
                            GL.PixelStore(PixelStoreParameter.UnpackAlignment,8);GL.PixelStore(PixelStoreParameter.UnpackRowLength,7);
                            GL.PixelStore(PixelStoreParameter.UnpackSkipRows,1);GL.PixelStore(PixelStoreParameter.UnpackSkipPixels,2);
                            GL.ActiveTexture(TextureUnit.Texture1);
                            OpenGlFailureInjection.ShaderObjectCallback=(stage,handle)=>
                            {
                                if(!stage.StartsWith("source-body-gx-",StringComparison.Ordinal))return;
                                if(stage=="source-body-gx-program")
                                {createdPrograms.Add(handle);if(++links==2)throw new OpenGlInjectedFailureException(stage);}
                                else createdShaders.Add(handle);
                            };
                            try{scene.PrepareSourceGxMaterials(materials);}
                            catch(OpenGlInjectedFailureException){injected=true;}
                            Check(injected&&links==2&&createdPrograms.Count==2&&createdShaders.Count==4,
                                "Actual second source body program link failure reaches the injected bounded rollback boundary.");
                            Check(createdPrograms.All(id=>!GL.IsProgram(id))&&createdShaders.All(id=>!GL.IsShader(id))
                                &&!preview.TryGetSourceRigPreview(out _)
                                &&Get(scene,"_sourceGxPrograms") is System.Collections.IDictionary cache&&cache.Count==0,
                                "Failed body preparation deletes every new shader/program and publishes neither a cache entry nor source model.");
                            Check(GL.GetInteger(GetPName.ActiveTexture)==(int)TextureUnit.Texture1
                                &&GL.GetInteger(GetPName.CurrentProgram)==oldProgram
                                &&GL.GetInteger(GetPName.PixelUnpackBufferBinding)==pbo&&GL.GetInteger(GetPName.UnpackAlignment)==8
                                &&GL.GetInteger(GetPName.UnpackRowLength)==7&&GL.GetInteger(GetPName.UnpackSkipRows)==1
                                &&GL.GetInteger(GetPName.UnpackSkipPixels)==2,
                                "Failed real source preparation restores caller program, active unit, bound PBO and dirty unpack strides/skips.");
                            GL.ActiveTexture(TextureUnit.Texture0);
                            Check(GL.GetInteger(GetPName.TextureBinding2D)==oldTexture&&GL.GetError()==ErrorCode.NoError,
                                "Failed real source upload restores unit-zero texture binding without GL errors.");
                            var images=Get(scene,"_characterTextureAssets") as TextureAssetManager;
                            Check(images!=null&&images.ResidentCount is >0 and <=10&&images.ResidentBytes>0
                                &&images.ResidentBytes<=materials.Archive.DecodedBytes,
                                "Failed program admission keeps only the bounded measured source-image residents for Scene-owned reuse.");
                            admissionFailures.Add(new{Context=context,FailedProgramLinks=links,CreatedPrograms=createdPrograms.Count,
                                CreatedShaders=createdShaders.Count,AllCreatedObjectsDeleted=true,SourceModelPublished=false,
                                UnpackAndPboRestored=true,ImageResidents=images!.ResidentCount,ImageResidentBytes=images.ResidentBytes});
                            return(images.ResidentCount,images.ResidentBytes);
                        }
                        finally
                        {
                            OpenGlFailureInjection.ShaderObjectCallback=prior;
                            GL.PixelStore(PixelStoreParameter.UnpackAlignment,oldAlignment);GL.PixelStore(PixelStoreParameter.UnpackRowLength,oldRows);
                            GL.PixelStore(PixelStoreParameter.UnpackSkipRows,oldSkipRows);GL.PixelStore(PixelStoreParameter.UnpackSkipPixels,oldSkipPixels);
                            GL.BindBuffer(BufferTarget.PixelUnpackBuffer,oldPbo);GL.DeleteBuffer(pbo);
                            GL.ActiveTexture(TextureUnit.Texture0);GL.BindTexture(TextureTarget.Texture2D,oldTexture);
                            GL.UseProgram(oldProgram);GL.ActiveTexture((TextureUnit)oldUnit);
                        }
                    }

                    void MaterialClock(int ordinal,double poseSeconds,string label)
                    {
                        // The invented Scene GX pixel oracle independently proves
                        // visible scroll timing against known texels. These owned
                        // bodies have no visibility/quantization oracle for the
                        // scrolling material. Normal at0/30.25 produced identical
                        // full-body images at all four angles in attempt03; the
                        // cause is unknown. Require exact actual clock/uniform
                        // transport, and report pixel changes only as observations.

                        foreach(int angle in new[]{0,90,180,270})
                        {
                            var clockA=Draw(ordinal,poseSeconds,angle,materialSeconds:0,clockDiagnostics:true);
                            var clockB=Draw(ordinal,poseSeconds,angle,materialSeconds:30.25,clockDiagnostics:true);
                            var clockAgain=Draw(ordinal,poseSeconds,angle,materialSeconds:0,clockDiagnostics:true);
                            materialFrames.Add(new{Context=context,Character=label,ClipOrdinal=ordinal,ViewAngle=angle,
                                APixelSha256=clockA.Pixels,BPixelSha256=clockB.Pixels,AgainPixelSha256=clockAgain.Pixels,
                                PaletteSha256=clockA.Palette,PixelsChanged=clockA.Pixels!=clockB.Pixels,
                                PaletteUnchanged=clockA.Palette==clockB.Palette&&clockA.Palette==clockAgain.Palette,
                                StableModel=ReferenceEquals(clockA.Model,clockB.Model)&&ReferenceEquals(clockA.Model,clockAgain.Model),
                                A=clockA.Clock,B=clockB.Clock,Again=clockAgain.Clock,
                                PixelChangeIsObservationOnly=true,UnchangedPixelCause="unknown; no owned visibility or quantization oracle"});
                            Check(clockA.Visible&&clockB.Visible&&clockAgain.Visible&&clockA.Pixels==clockAgain.Pixels,
                                label+" material-clock view remains visible and returns exact A/B/A pixels.");
                            Check(clockA.Palette==clockB.Palette&&clockA.Palette==clockAgain.Palette
                                &&ReferenceEquals(clockA.Model,clockB.Model)&&ReferenceEquals(clockA.Model,clockAgain.Model)
                                &&clockA.Lists.SequenceEqual(clockB.Lists)&&clockA.Lists.SequenceEqual(clockAgain.Lists)
                                &&clockA.Textures.SequenceEqual(clockB.Textures)&&clockA.Textures.SequenceEqual(clockAgain.Textures)
                                &&clockA.Programs.SequenceEqual(clockB.Programs)&&clockA.Programs.SequenceEqual(clockAgain.Programs),
                                label+" material clock is independent of source pose and reuses the exact model/list/texture/program packets.");
                            Check(clockA.Clock?.SubmittedSecondsMatch==true&&clockB.Clock?.SubmittedSecondsMatch==true&&clockAgain.Clock?.SubmittedSecondsMatch==true,
                                label+" every actual submitted source packet carries the requested independent material seconds.");
                            Check(new[]{clockA.Clock!,clockB.Clock!,clockAgain.Clock!}.All(c=>c.ProgramZeroAllMode2
                                    &&c.ActivePrimaryRows==(label=="normal"?9:6)&&c.GpuRowsMatchCpuIdentity),
                                label+" every actual program-zero primary row matches the exact CPU mode2 time calculation bit for bit.");
                            Check(clockA.Clock!.GpuPrimaryRowsSha256!=clockB.Clock!.GpuPrimaryRowsSha256
                                &&clockA.Clock.GpuPrimaryRowsSha256==clockAgain.Clock!.GpuPrimaryRowsSha256,
                                label+" material clock changes actual GPU primary uniforms and returns exact A/B/A independently of pose.");
                        }
                    }

                    Frame Draw(int ordinal, double seconds, int angle, CharacterModelPart part = CharacterModelPart.Biped, double materialSeconds = 0, bool clockDiagnostics = false)
                    {
                        GlCheckpoint("before-clear");
                        CosmeticPreview.Mode = part == CharacterModelPart.ViewModel ? SkinContext.ViewModel : SkinContext.Biped;
                        CosmeticPreview.Yaw = angle * MathF.PI / 180;
                        // The native model/part owner advances at60Hz. A tight
                        // readback loop can request cannon->body before its
                        // next real frame; allow one native step, then still
                        // require this single draw to succeed without fallback.
                        if (Preview(CurrentScene()).Mode != CosmeticPreview.Mode)
                            Thread.Sleep(20);
                        LauncherHunter.SourcePreviewRequest = new(owner, selection.Pack, selection.SelectedAppearance, ordinal, seconds, part, materialSeconds);
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
                            && LauncherHunter.SourcePreviewRequest.Part == part && LauncherHunter.SourcePreviewRequest.Appearance == selection.SelectedAppearance
                            && LauncherHunter.SourcePreviewRequest.MaterialSeconds == materialSeconds && preview.Mode == CosmeticPreview.Mode,
                            "Part changes retain the captured source owner/pack and matching preview mode.");
                        Check(ReferenceEquals(model!.Asset, selection.Pack.TryResolvePrivate(Hunter.Samus, selection.SelectedAppearance,
                            part, 0, out var asset) ? asset : null), "GPU model belongs to the captured generated pack and part.");
                        if (part == CharacterModelPart.ViewModel)
                        {
                            bool finalCannon=ReferenceEquals(selection,finalSelection);
                            Check(selection.HasCannon&&(finalCannon?model.Segments.All(s=>s.SourceGx!=null&&s.AlbedoBinding==null)
                                :ReferenceEquals(selection,normalSelection)&&model.Segments.All(s=>s.SourceGx==null)),
                                "Final cannon uses its own source GX while normal retains its unchanged diagnostic cannon materials.");
                            var nativeMaterials = ((ModelInstance)Get(preview, "_model")!).Model.Materials;
                            Check(model.Segments.All(s => (uint)s.NativeMaterialIndex < nativeMaterials.Count
                                && nativeMaterials[s.NativeMaterialIndex].Alpha == 31
                                && float.IsFinite(nativeMaterials[s.NativeMaterialIndex].CurrentAlpha)
                                && nativeMaterials[s.NativeMaterialIndex].CurrentAlpha >= .99999f
                                && nativeMaterials[s.NativeMaterialIndex].RenderMode == RenderMode.Normal
                                && nativeMaterials[s.NativeMaterialIndex].PolygonMode == MphRead.PolygonMode.Modulate),
                                "Every cannon segment uses an opaque native anchor rather than an invisible animated glow.");
                            var source = Get(model, "_sourceGeometry") as CharacterWeightedModelData;
                            Check(ordinal == 32 && seconds == 0 && model.SourceCannon?.IsGenerated == true && source != null
                                && source.SourceAuthoringPose is { Count: 40 } && source.SourceRig?.Nodes.Count == 40,
                                "Cannon uses the complete generated fixed source pose at root32/framezero.");
                            int[] ordinals=finalCannon?new[]{3,4,5,7}:new[]{2,5};
                            int[] counts=finalCannon?new[]{392,66,314,21}:new[]{66,326};
                            Check(source!=null&&source.Primitives.Sum(p=>p.Indices.Length)==(finalCannon?2379:1176)
                                &&source.Primitives.Select(p=>p.SourceMaterialOrdinal).Distinct().Order().SequenceEqual(ordinals)
                                &&ordinals.Select(m=>source.Primitives.Where(p=>p.SourceMaterialOrdinal==m).Sum(p=>p.Indices.Length/3)).SequenceEqual(counts)
                                &&source.Primitives.SelectMany(p=>p.SourceTriangleIds??Array.Empty<int>()).Distinct().Count()==(finalCannon?793:392),
                                "GPU cannon retains its own exact complete source triangle/material inventory.");
                            if(finalCannon)
                            {
                                var materials=capturedMaterials[finalSelection];var variant=materials.ForFinalCannon();
                                Check(ReferenceEquals(variant.Archive,materials.Archive)&&ReferenceEquals(variant.Images,materials.Images)
                                    &&materials.Programs.All(p=>p.PresentationAlphaScale==1)
                                    &&model.Segments.Select((segment,i)=>segment.SourceGx is{} gx&&ReferenceEquals(gx.Owner,current)
                                        &&gx.Program.Vertex120==materials.Programs[source!.Primitives[i].SourceMaterialOrdinal].Vertex120
                                        &&gx.Program.Fragment120==variant.Programs[source!.Primitives[i].SourceMaterialOrdinal].Fragment120
                                        &&gx.Program.PresentationAlphaScale==(gx.Program.DrawPass==CharacterSourceGxProgram.Pass.Alpha?.55f:1f)
                                        &&GL.IsProgram(gx.ProgramId)&&gx.Textures.Count==gx.Program.Textures.Count&&gx.Textures.All(GL.IsTexture)).All(v=>v)
                                    &&model.Segments.Any(s=>s.SourceGx!.Program.PresentationAlphaScale==.55f),
                                    "Final cannon submits own four source GX bindings with alpha055 only on alpha passes and unchanged body archive/images/programs.");
                                int[][] cut=Enumerable.Range(0,9).Select(m=>source!.Primitives.Where(p=>p.SourceMaterialOrdinal==m)
                                    .SelectMany(p=>p.SourceTriangleIds??Array.Empty<int>()).Order().ToArray()).ToArray();
                                Check(Sha(JsonSerializer.SerializeToUtf8Bytes(cut))=="7a91aae9dbe748d23143b8c2ea655435df1db1403ec59fb84a43242577cdd594",
                                    "Actual Final GPU cannon reproduces independently frozen nine-array793-triangle identity after twelve raw-UV trim removals.");
                            }
                        }
                        else
                        {
                            Check(model.SourceCannon == null, "Returning to body cannot retain the cannon cut.");
                            bool final=ReferenceEquals(selection,finalSelection);int programCount=final?9:7,triangleCount=final?12734:6340;
                            var materials=capturedMaterials[selection];
                            var source=Get(model,"_sourceGeometry") as CharacterWeightedModelData;
                            Check(source!=null&&model.Segments.Count==source.Primitives.Count
                                &&source.Primitives.Select(p=>p.SourceMaterialOrdinal).Distinct().Order().SequenceEqual(Enumerable.Range(0,programCount))
                                &&source.Primitives.Sum(p=>p.Indices.Length/3)==triangleCount&&model.IndexCount/3==triangleCount
                                &&source.Primitives.SelectMany(p=>p.SourceTriangleIds??Array.Empty<int>()).Order().SequenceEqual(Enumerable.Range(0,triangleCount)),
                                "GPU body retains every original material ordinal and source triangle exactly once.");
                            Check(materials.Programs.Count==programCount&&materials.Archive.Textures.Count==(final?12:10)
                                &&model.Segments.Select((segment,i)=>segment.SourceGx is { } gx
                                    &&ReferenceEquals(gx.Owner,current)&&ReferenceEquals(gx.Program,materials.Programs[source!.Primitives[i].SourceMaterialOrdinal])
                                    &&gx.ProgramId!=0&&GL.IsProgram(gx.ProgramId)&&segment.AlbedoBinding==null
                                    &&gx.Textures.Count==gx.Program.Textures.Count&&gx.Textures.All(t=>t!=0&&GL.IsTexture(t))).All(v=>v),
                                "Every body segment binds its captured source program and every declared live source texture slot without diagnostic albedo fallback.");
                            Check(model.Segments.SelectMany(segment=>segment.SourceGx!.Program.Textures).Distinct().Order()
                                .SequenceEqual(materials.Archive.Textures.Select(t=>t.ResourceId).Order()),
                                "All original source texture resource IDs participate in complete body bindings.");
                            bodyPrograms.Add(new{Context=context,Frame=frames,Character=final?FinalId:Id,SourcePrograms=programCount,
                                SourceTriangles=triangleCount,Segments=model.Segments.Count,SourceTextures=materials.Archive.Textures.Count,
                                ProgramBindings=model.Segments.Select(s=>s.SourceGx!.ProgramId).Distinct().Count(),
                                LiveTextureBindings=model.Segments.SelectMany(s=>s.SourceGx!.Textures).Distinct().Count(),
                                MaterialArchiveSha256=materials.Archive.Sha256,Complete=true});
                        }
                        int[] lists = model.Segments.Select(s => s.ListId).ToArray();
                        bool sourcePrograms=part==CharacterModelPart.Biped||ReferenceEquals(selection,finalSelection);
                        int[] textures = sourcePrograms?model.Segments.SelectMany(s=>s.SourceGx!.Textures).ToArray()
                            :model.Segments.Select(s=>s.AlbedoBinding??0).ToArray();
                        int[] programs = sourcePrograms?model.Segments.Select(s=>s.SourceGx!.ProgramId).ToArray():Array.Empty<int>();
                        Check(model.IsSourcePreview && model.Joints.Count == 39 && lists.All(GL.IsList)
                            && textures.Length>0&&textures.All(t => t != 0 && GL.IsTexture(t)), "Complete source rig and live source-program or diagnostic-cannon GPU resources required.");
                        if(sourcePrograms)
                        {
                            foreach(int program in programs)contextPrograms.Add(program);
                            foreach(int texture in textures)contextTextures.Add(texture);
                            foreach(int list in lists)contextLists.Add(list);
                        }
                        GlCheckpoint("after-resource-queries");
                        string palette = PaletteHash(model);
                        ClockSample? clock=clockDiagnostics?ClockDiagnostics(current,model,materialSeconds,capturedMaterials[selection]):null;
                        GlCheckpoint("after-clock-diagnostics");
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
                            return new(Sha(pixels), palette, lists, textures, programs, model.Segments.Count, model.Joints.Count, visible, model, clock);
                        }
                        finally { Array.Clear(pixels); }

                        void GlCheckpoint(string stage)
                        {
                            ErrorCode status = GL.GetError();
                            checkpoints.Add(new { Context = context, Frame = frames, ClipOrdinal = ordinal,
                                Stage = stage, Character = ReferenceEquals(selection, finalSelection) ? FinalId : Id,
                                MaterialSeconds = materialSeconds, GlError = status.ToString(), Drawn = LauncherHunter.Drawn });
                            Check(status == ErrorCode.NoError, "GPU checkpoint " + stage + ": " + status);
                        }
                    }
                }
                finally
                {
                    selection=normalSelection;
                    LauncherHunter.SourcePreviewRequest = null; LauncherHunter.ReleaseSourcePreview(owner); LauncherHunter.Reset();
                    if (SideScene.GetValue(null) is Scene scene) { try { scene.UnloadGl(); } finally { SideScene.SetValue(null, null); } }
                    if(contextPrograms.Count>0)
                        Check(contextPrograms.All(id=>!GL.IsProgram(id))&&contextTextures.Count>0
                            &&contextTextures.All(id=>!GL.IsTexture(id))&&contextLists.All(id=>!GL.IsList(id)),
                            "Actual Scene retirement releases every captured body shader, source texture and display list before context teardown.");
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
                Check(cleaned && (rvzBefore == null || rvzBefore == rvzAfter) && (nativeBefore == null || nativeBefore == nativeAfter),
                    "All derived data deleted and both owned sources unchanged.");
                Check(coreBefore == HashFile(core) && toolBefore == HashFile(tool), "Core/tool assemblies unchanged during GPU proof.");
            }
            catch (Exception ex) { error ??= ex.GetType().Name; errorSha ??= Sha(Encoding.UTF8.GetBytes(ex.Message)); }
            Console.WriteLine(JsonSerializer.Serialize(new { Scope = "Actual normal and Final Echoes source-program bodies, normal diagnostic cannon and Final private-alpha source GX cannon in LauncherHunter sideScene; no gameplay/UI-intent/hardware-parity claim",
                Pass = error == null, Checks = checks, Frames = frames, Generation = generation, ChosenClipOrdinal = chosen,
                SourceSha256Before = rvzBefore, SourceSha256After = rvzAfter, NativeInventorySha256Before = nativeBefore,
                NativeInventorySha256After = nativeAfter, CoreSha256 = coreBefore, ToolSha256 = toolBefore,
                Contexts = observations, CandidateFrames = candidateFrames, AngleFrames = angleFrames, CannonFrames = cannonFrames, BodyPrograms = bodyPrograms, MaterialClockFrames = materialFrames, FinalFrames = finalFrames, AdmissionFailures = admissionFailures, GlCheckpoints = checkpoints, TemporaryPayloadsDeleted = cleaned, FailedCheck = failedCheck, ErrorType = error, ErrorMessageSha256 = errorSha }));
        }
        return error == null ? 0 : 1;
    }
    private static ClockSample ClockDiagnostics(Scene scene,CharacterWeightedRenderModel model,double seconds,CharacterSourceGxMaterials materials)
    {
        var geometry=Get(model,"_sourceGeometry") as CharacterWeightedModelData
            ??throw new InvalidDataException("Clock diagnostic requires the captured source geometry.");
        var zero=model.Segments.Select((s,i)=>(Segment:s,Ordinal:geometry.Primitives[i].SourceMaterialOrdinal))
            .First(p=>p.Ordinal==0).Segment.SourceGx??throw new InvalidDataException("Clock diagnostic requires original material zero.");
        var items=(IEnumerable<RenderItem>)Get(scene,"_previewItems")!;
        var packets=items.Select(i=>i.SourceGx).Where(g=>g!=null).Select(g=>g!).ToArray();
        var primary=zero.Program.Matrices(seconds,System.Numerics.Matrix4x4.Identity).Primary;
        var gpuRows=new List<float>();var cpuRows=new List<float>();int active=0;
        for(int i=0;i<9;i++)
        {
            int location=GL.GetUniformLocation(zero.ProgramId,"gx_primary["+i+"]");if(location<0)continue;
            var actual=new float[4];GL.GetUniform(zero.ProgramId,location,actual);gpuRows.AddRange(actual);active++;
            var m=primary[i/3];
            cpuRows.AddRange((i%3) switch{0=>new[]{m.M11,m.M21,m.M31,m.M41},1=>new[]{m.M12,m.M22,m.M32,m.M42},_=>new[]{m.M13,m.M23,m.M33,m.M43}});
        }
        if(gpuRows.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Nonfinite actual source primary uniform.");
        string Digest(IEnumerable<float> rows)
        {using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);foreach(float v in rows)hash.AppendData(BitConverter.GetBytes(v));return Convert.ToHexStringLower(hash.GetHashAndReset());}
        var programs=model.Segments.Select(s=>s.SourceGx!.Program).Distinct().ToArray();
        int uniqueRgba=materials.Archive.Textures.Select(t=>t.RgbaSha256).Distinct(StringComparer.Ordinal).Count();
        var lastZero=packets.OrderBy(p=>(int)p.Binding.Program.DrawPass).LastOrDefault(p=>p.Binding.ProgramId==zero.ProgramId);
        int lastOrdinal=lastZero==null?-1:Enumerable.Range(0,materials.Programs.Count).First(i=>ReferenceEquals(materials.Programs[i],lastZero.Binding.Program));
        return new(packets.Length,packets.Length==model.Segments.Count&&packets.All(p=>p.Seconds==seconds),
            packets.Length==0?null:packets.Min(p=>p.Seconds),packets.Length==0?null:packets.Max(p=>p.Seconds),
            programs.Count(p=>model.Segments.Any(s=>ReferenceEquals(s.SourceGx!.Program,p)&&s.SourceGx.ProgramId==zero.ProgramId)),
            lastOrdinal,active,Digest(gpuRows),Digest(cpuRows),zero.Program.Animations.All(a=>a.Mode==2),
            gpuRows.Select(BitConverter.SingleToInt32Bits).SequenceEqual(cpuRows.Select(BitConverter.SingleToInt32Bits)),
            programs.SelectMany(p=>p.Animations).Any(a=>a.Mode==2&&(a.RateS!=0||a.RateT!=0)),uniqueRgba);
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
