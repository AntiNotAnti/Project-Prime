using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Import;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Graphics.OpenGL;
using NVector4 = System.Numerics.Vector4;

// Explicit owned Windows preview qualification. Disposable imports and optional
// ignored QA pictures are not actor, gameplay, launcher-intent or GX-parity proof.
internal static class CharacterTrooperGeneratedOwnedProbe
{
    private static readonly string[] Ids = { "trooper-power", "trooper-ice", "trooper-wave", "trooper-plasma" };
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo SideScene = typeof(LauncherHunter).GetField("_scene", BindingFlags.Static | BindingFlags.NonPublic)!;
    private sealed record Captured(string Id, string Folder, CharacterSourcePreviewSelection Selection,
        CharacterModelAsset Asset, CharacterModelPack.SourcePreviewLease Lease, CharacterSourceGxMaterials Materials,
        Dictionary<string, string> Files, int Joints = 35, int Triangles = 1970);
    private sealed record Frame(string Pixels, string Palette, int[] Lists, int[] Textures, int[] Programs,
        bool Visible, CharacterWeightedRenderModel Model);

    internal static int Run(string rvz)
    {
        string cwd = Environment.CurrentDirectory;
        string temporary = Path.Combine(Path.GetTempPath(), "prime-trooper-generated-gl-" + Guid.NewGuid().ToString("N"));
        string imports = Path.Combine(temporary, "imports"), user = Path.Combine(temporary, "user");
        string? oldData = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        string? native = Environment.GetEnvironmentVariable("PRIME_BODY_TEST_ASSETS");
        string? captures = Environment.GetEnvironmentVariable("PRIME_BODY_TEST_CAPTURE_DIR");
        string? echoes = Environment.GetEnvironmentVariable("PRIME_TROOPER_TEST_ECHOES_SOURCE");
        string core = typeof(Scene).Assembly.Location, tool = typeof(CharacterTrooperGeneratedOwnedProbe).Assembly.Location;
        string coreBefore = HashFile(core), toolBefore = HashFile(tool);
        string? before = null, after = null, nativeBefore = null, nativeAfter = null, echoesBefore = null, echoesAfter = null;
        string? failedCheck = null, error = null, errorSha = null, renderer = null, glVersion = null;
        bool cleaned = false; int checks = 0, frames = 0, refusals = 0;
        var cases = new List<string>(); var profiles = new List<object>(); var observations = new List<object>();
        var pictures = new List<object>(); var contexts = new List<object>(); var rollback = new List<object>();
        var captured = new List<Captured>(); var chosen = new Dictionary<string, int>(); var firstPalettes = new Dictionary<string, string>();
        bool oldHd = RenderOptions.CharacterModelReplacements, oldBright = RenderOptions.BrightSkins,
            oldCosmetics = RenderOptions.ShowCustomCosmetics, oldFps = RenderOptions.ShowFps;
        ShadowQuality oldShadows = RenderOptions.Shadows;
        void Check(bool condition, string why)
        { if (!condition) { failedCheck = why; throw new InvalidDataException(why); } checks++; }
        void Reject(Action work, string why)
        {
            try { work(); }
            catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException)
            { cases.Add(why); refusals++; return; }
            failedCheck = why; throw new InvalidDataException("Expected refusal: " + why);
        }
        try
        {
            Check(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64
                && !Headless.Active && !NetSession.Active && !DemoPlayback.IsActive && !ReplayCapture.LiveRecordingActive,
                "Fresh Windows x64 desktop process without active session/replay required.");
            Check(!string.IsNullOrWhiteSpace(native) && Directory.Exists(native), "PRIME_BODY_TEST_ASSETS must identify the existing owned AMHE1 directory.");
            rvz = Path.GetFullPath(rvz); native = Path.GetFullPath(native!);
            before = HashFile(rvz); nativeBefore = Inventory(native);
            using (var disc = GameCubeDiscReader.Open(rvz))
            {
                Check(disc.GameId == "GM8E01" && disc.Revision == 2 && disc.DiscNumber == 0, "Pinned Prime USA Rev2 source edition required.");
                Check(ExternalSourceRequirements.VerifyKnown(disc).All(r => r.Status == ExternalArchiveStatus.Verified), "Both original Prime Rev2 PAK fingerprints must verify before export.");
            }
            if (!string.IsNullOrWhiteSpace(captures))
            {
                captures = Path.GetFullPath(captures);
                string artifacts = Path.GetFullPath(Path.Combine(cwd, "artifacts")) + Path.DirectorySeparatorChar;
                Check(captures.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase), "Optional QA pictures must remain under this worktree artifacts directory.");
                Directory.CreateDirectory(captures);
            }
            Directory.CreateDirectory(user);
            File.WriteAllText(Path.Combine(user, "paths.txt"), Ver.AMHE1 + "=" + native + "\nExport=" + Path.Combine(user, "Export") + "\n");
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", user); Directory.SetCurrentDirectory(user);
            ConsoleSetup.Run(); Paths.UpdatePaths(); Paths.SetPath(Ver.AMHE1, native); Paths.MphKey = Ver.AMHE1;
            GraphicsBackendPolicy.Configure("opengl");
            RenderOptions.CharacterModelReplacements = false; RenderOptions.BrightSkins = false;
            RenderOptions.ShowCustomCosmetics = false; RenderOptions.ShowFps = false; RenderOptions.Shadows = ShadowQuality.Off;
            var exported = RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz, imports, selectedIds: Ids);
            Check(exported.Count == 4, "Public exporter publishes the all-four current transaction.");
            foreach (string id in Ids)
            {
                var pin = CharacterGeneratedTrooperProfile.Find(id)!;
                var spec = RetroCharacterPreviewExporter.BundleSpec(id, ExternalSourceEdition.PrimeUsaRevision2);
                var state = LocalImportBundle.Inspect(imports, id, spec);
                Check(spec.Version == 6 && state.State == ImportBundleState.Ready && state.Receipt?.Version == 6
                    && spec.RequiredComponents.Length == 13 && spec.RequiredComponents.Contains(id + ".materials.bin")
                    && spec.RequiredComponents.Contains(id + ".cannon-source.json"), id + " current v6 recipe includes raw materials and its fixed cannon recipe.");
                string folder = Path.GetDirectoryName(state.Files.Single(f => Path.GetFileName(f) == "characters.json"))!;
                Check(Path.GetFileName(folder) == state.Receipt!.Generation && state.Files.All(f => Path.GetDirectoryName(f) == folder), id + " has one immutable committed generation.");
                foreach (string file in state.Files) LocalImportBundle.CheckPath(file, imports);
                var selection = CharacterSourcePreviewSelection.Load(id, CancellationToken.None, imports);
                Check(selection.NativeHunter == Hunter.Weavel && selection.SelectedAppearance == new PrivateCharacterAppearance("trooper", pin.Skin)
                    && selection.HasCannon && !selection.RuntimeCannon && selection.Generation == state.Receipt.Generation,
                    id + " captures its closed Weavel viewer appearance and cannon asset without runtime gun activation.");
                bool found = selection.Pack.TryResolvePrivate(Hunter.Weavel, selection.SelectedAppearance, CharacterModelPart.Biped, 0, out var asset);
                Check(found && !selection.Pack.TryResolvePrivate(Hunter.Samus, selection.SelectedAppearance, CharacterModelPart.Biped, 0, out _)
                    && selection.Pack.TryResolvePrivate(Hunter.Weavel, selection.SelectedAppearance, CharacterModelPart.ViewModel, 0, out _)
                    && !selection.Pack.TryResolvePrivate(Hunter.Samus, selection.SelectedAppearance, CharacterModelPart.ViewModel, 0, out _),
                    id + " resolves its own Weavel cannon asset and cannot alias Samus.");
                Check(selection.Pack.TryCreateSourcePreviewLease(asset, out var lease) && lease.SourceMaterials != null
                    && lease.SourceAnimations != null && lease.ModelSha256 == pin.ModelSha, id + " lease captures exact whole-pinned model/material/animation authority.");
                var materials = lease.SourceMaterials!;
                Check(materials.Archive.Sha256 == pin.MaterialSha && materials.Archive.Binding.CharacterId == id
                    && materials.Archive.GeometrySha256 == pin.ModelSha && materials.Programs.Count == 3 && materials.Archive.Textures.Count == 7
                    && materials.Policy == CharacterSourceGxProgram.TrooperPolicy, id + " has three complete exact source programs and seven textures.");
                var c1 = id == "trooper-power" ? new NVector4(253/255f, 239/255f, 86/255f, 0) : NVector4.Zero;
                Check(materials.Programs.All(p => p.InitialRegister1 == c1) && materials.Programs[1].Generators.Any(g => g.Post == 1), id + " has Power-only yellow C1 and original material1 post slot1.");
                var geometry = CharacterWeightedModelLoader.LoadSourcePreview(asset, lease);
                Check(geometry.Joints.Count == 35 && geometry.SourceRig?.Nodes.Count == 36 && geometry.IndexCount/3 == 1970
                    && geometry.Primitives.Select(p => p.SourceMaterialOrdinal).Distinct().Order().SequenceEqual(Enumerable.Range(0, 3))
                    && geometry.Primitives.SelectMany(p => p.SourceTriangleIds ?? Array.Empty<int>()).Order().SequenceEqual(Enumerable.Range(0, 1970)),
                    id + " retains all35 joints,36 nodes, three material ordinals and1,970 unique complete triangles.");
                Check(selection.Clips.Count == 113 && selection.Clips.Select(c => c.Ordinal).Distinct().Count() == 113
                    && selection.Clips.All(c => lease.SourceAnimations!.Data.Roots.Any(r => r.Ordinal == c.Ordinal && r.CharacterDeclared && r.StrictPlay)), id + " exposes exactly113 genuine strict source clips.");
                var localBody = CharacterSourcePreviewSelection.LoadForLocalBody(id, CancellationToken.None, imports);
                Check(localBody.NativeHunter == Hunter.Weavel && localBody.SelectedAppearance == selection.SelectedAppearance
                    && localBody.Generation == selection.Generation && localBody.HasCannon && localBody.RuntimeCannon
                    && localBody.Clips.Select(c => c.Ordinal).SequenceEqual(selection.Clips.Select(c => c.Ordinal)),
                    id + " explicit local-body selection preserves its Weavel generation/strict clips and enables its exact source cannon.");
                Reject(() => CharacterSourceClip.Load(lease, asset, geometry.SourceRig!, int.MaxValue), id + ": undeclared clip refuses");
                Check(!selection.Pack.TryCreateSourcePreviewLease(asset with { }, out _), id + " copied asset cannot mint owner authority.");
                captured.Add(new(id, folder, selection, asset, lease, materials, state.Files.ToDictionary(f => f, HashFile)));
                profiles.Add(new { Character = id, selection.Generation, ModelSha256 = pin.ModelSha, MaterialSha256 = pin.MaterialSha,
                    ProvenanceSha256 = pin.ProvenanceSha, SourceTriangles = 1970, Joints = 35, Nodes = 36, StrictClips = selection.Clips.Count,
                    SourcePrograms = 3, SourceTextures = 7, C1 = new[] { c1.X, c1.Y, c1.Z, c1.W },
                    HasCannon = selection.HasCannon, RuntimeCannon = selection.RuntimeCannon, LocalBodyRuntimeCannon = localBody.RuntimeCannon });
            }
            // Mutations use disposable flat packs. No original generation is
            // edited and no rejected profile is submitted to a GL owner.
            foreach (var c in captured)
            {
                string mutable = Path.Combine(temporary, "mutations", c.Id); Directory.CreateDirectory(mutable);
                foreach (string file in c.Files.Keys) File.Copy(file, Path.Combine(mutable, Path.GetFileName(file)));
                string materialPath = Path.Combine(mutable, c.Id + ".materials.bin"); byte[] original = File.ReadAllBytes(materialPath);
                void LoadMutable()
                {
                    var pack = CharacterModelPack.Load(mutable);
                    if (!pack.TryResolvePrivate(Hunter.Weavel, c.Selection.SelectedAppearance, CharacterModelPart.Biped, 0, out var asset)) throw new InvalidDataException("Expected source asset absent.");
                    pack.CaptureSourceMaterials(asset, c.Id);
                }
                File.Delete(materialPath);
                try { Reject(LoadMutable, c.Id + ": missing archive before GPU publication"); } finally { File.WriteAllBytes(materialPath, original); }
                byte[] corrupt = (byte[])original.Clone(); corrupt[^1] ^= 1; File.WriteAllBytes(materialPath, corrupt);
                try { Reject(LoadMutable, c.Id + ": corrupted archive before GPU publication"); } finally { File.WriteAllBytes(materialPath, original); Array.Clear(corrupt); }
                var other = captured[(Array.IndexOf(Ids, c.Id) + 1) % captured.Count];
                File.WriteAllBytes(materialPath, File.ReadAllBytes(Path.Combine(other.Folder, other.Id + ".materials.bin")));
                try { Reject(LoadMutable, c.Id + ": cross-appearance archive before GPU publication"); } finally { File.WriteAllBytes(materialPath, original); Array.Clear(original); }
                string manifestPath = Path.Combine(mutable, "characters.json"); byte[] manifest = File.ReadAllBytes(manifestPath);
                foreach (var change in new (string Label, Action<JsonObject> Edit)[] {
                    ("wrong-native-hunter", e => e["hunter"] = "Samus"), ("cross-appearance-skin", e => e["skinSource"] = other.Selection.SelectedAppearance.SkinSource) })
                {
                    var document = JsonNode.Parse(manifest)!.AsObject(); change.Edit(document["models"]![0]!.AsObject());
                    File.WriteAllText(manifestPath, document.ToJsonString());
                    try { Reject(LoadMutable, c.Id + ": " + change.Label); } finally { File.WriteAllBytes(manifestPath, manifest); }
                }
                c.Lease.Require(c.Asset);
                Check(c.Files.All(p => HashFile(p.Key) == p.Value), c.Id + " negative fixtures preserve committed bytes.");
            }
            if (!string.IsNullOrWhiteSpace(echoes))
            {
                echoes = Path.GetFullPath(echoes); echoesBefore = HashFile(echoes);
                string[] echoIds = { "darksamus-echoes", "darksamus-overcharged-echoes" };
                RetroCharacterPreviewExporter.ExportFromOwnedGame(echoes, imports, selectedIds: echoIds);
                foreach (string id in echoIds)
                {
                    var selection = CharacterSourcePreviewSelection.Load(id, CancellationToken.None, imports);
                    var state = LocalImportBundle.Inspect(imports, id, RetroCharacterPreviewExporter.BundleSpec(id));
                    Check(selection.NativeHunter == Hunter.Samus && selection.Pack.TryResolvePrivate(Hunter.Samus, selection.SelectedAppearance, CharacterModelPart.Biped, 0, out _), id + " optional Echoes body remains independently selected.");
                    selection.Pack.TryResolvePrivate(Hunter.Samus, selection.SelectedAppearance, CharacterModelPart.Biped, 0, out var asset);
                    Check(selection.Pack.TryCreateSourcePreviewLease(asset, out var lease) && lease.SourceMaterials != null, id + " optional source materials captured.");
                    Check(lease.SourceMaterials!.Programs.All(p => p.InitialRegister1 == NVector4.Zero)
                        && lease.SourceMaterials.Policy == CharacterSourceGxProgram.Policy, id + " preserves Echoes zero registers and policy.");
                    string folder = Path.GetDirectoryName(state.Files.Single(f => Path.GetFileName(f) == "characters.json"))!;
                    captured.Add(new(id, folder, selection, asset, lease, lease.SourceMaterials, state.Files.ToDictionary(f => f, HashFile), 39, id == echoIds[0] ? 6340 : 12734));
                }
            }
            RunGpu();
            Check(captured.All(c => c.Files.All(p => HashFile(p.Key) == p.Value)), "Rendering leaves every committed source component unchanged.");
        }
        catch (Exception e) { error = e.GetType().Name; errorSha = Sha(Encoding.UTF8.GetBytes(e.Message)); }
        finally
        {
            Directory.SetCurrentDirectory(cwd); Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", oldData);
            RenderOptions.CharacterModelReplacements = oldHd; RenderOptions.BrightSkins = oldBright;
            RenderOptions.ShowCustomCosmetics = oldCosmetics; RenderOptions.ShowFps = oldFps; RenderOptions.Shadows = oldShadows;
            try
            {
                string resolvedTemporary = Path.GetFullPath(temporary);
                string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!resolvedTemporary.StartsWith(tempParent, StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(resolvedTemporary).StartsWith("prime-trooper-generated-gl-", StringComparison.Ordinal))
                    throw new InvalidDataException("Temporary cleanup target escaped its explicitly owned directory.");
                if (Directory.Exists(resolvedTemporary)) Directory.Delete(resolvedTemporary, true); cleaned = !Directory.Exists(resolvedTemporary);
                if (before != null) after = HashFile(rvz); if (nativeBefore != null) nativeAfter = Inventory(native!);
                if (echoesBefore != null) echoesAfter = HashFile(echoes!);
                Check(cleaned && before != null && before == after && nativeBefore != null && nativeBefore == nativeAfter
                    && (echoesBefore == null || echoesBefore == echoesAfter), "Temporary imports deleted and all owned sources unchanged.");
                Check(coreBefore == HashFile(core) && toolBefore == HashFile(tool), "Core/tool binaries unchanged during owned GPU proof.");
            }
            catch (Exception e) { error ??= e.GetType().Name; errorSha ??= Sha(Encoding.UTF8.GetBytes(e.Message)); }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Schema = 1, Passed = error == null, Checks = checks, Refusals = refusals, Cases = cases,
            Frames = frames, FailedCheck = failedCheck, ErrorType = error, ErrorMessageSha256 = errorSha, Renderer = renderer, GlVersion = glVersion,
            SourceBeforeSha256 = before, SourceAfterSha256 = after, NativeBeforeSha256 = nativeBefore, NativeAfterSha256 = nativeAfter,
            EchoesSourceBeforeSha256 = echoesBefore, EchoesSourceAfterSha256 = echoesAfter,
            CoreSha256 = coreBefore, ToolSha256 = toolBefore, TemporaryDerivedPayloadsDeleted = cleaned, Profiles = profiles,
            PoseCandidates = observations, Contexts = contexts, AdmissionRollback = rollback, Captures = pictures,
            Scope = "Owned Prime Rev2 source-program bodies in real Windows Weavel HunterPreview sideScene; optional Echoes same-Scene switches; no actor/gameplay/native-UI-session/hardware-parity claim" }));
        return error == null ? 0 : 1;

        void RunGpu()
        {
            object owner = new();
            int expectedPrograms = captured.SelectMany(c => c.Materials.Programs).Select(p => p.Vertex120 + p.Fragment120).Distinct(StringComparer.Ordinal).Count();
            Check(expectedPrograms <= 28, "All selected complete profiles fit the closed Scene shader budget.");
            for (int context = 0; context < 2; context++)
            {
                using var window = new RenderWindow(shell: true); window.ClientSize = new(640, 480); window.Context.MakeCurrent();
                int width = window.FramebufferSize.X, height = window.FramebufferSize.Y;
                Check(!window.HasScene && width is > 0 and <= 4096 && height is > 0 and <= 4096, "Bounded real shell window has no gameplay Scene.");
                renderer ??= GL.GetString(StringName.Renderer); glVersion ??= GL.GetString(StringName.Version);
                var gpuPrograms = new HashSet<int>(); var gpuTextures = new HashSet<int>(); var gpuLists = new HashSet<int>();
                Scene? scene = null;
                try
                {
                    LauncherHunter.Reset(); LauncherHunter.Wanted = true; LauncherHunter.CanPresent = () => !window.HasScene;
                    LauncherHunter.Hunter = Hunter.Weavel; LauncherHunter.Suit = 0; LauncherHunter.TransparentBackground = true;
                    LauncherHunter.CinematicLighting = true; LauncherHunter.PreviewSlot = -1; LauncherHunter.Formation = false;
                    LauncherHunter.Left = .05f; LauncherHunter.Top = .05f; LauncherHunter.Right = .95f; LauncherHunter.Bottom = .95f;
                    CosmeticPreview.Loadout = CosmeticLoadout.Default; CosmeticPreview.Mode = SkinContext.Biped;
                    CosmeticPreview.Yaw = 0; CosmeticPreview.Zoom = 1; CosmeticPreview.LoopDeath = false; CosmeticPreview.DeathRequest = 0;
                    Clear(width, height); LauncherHunter.Draw(window, width, height); GL.Finish(); frames++;
                    scene = CurrentScene(); var preview = Preview(scene);
                    Check(LauncherHunter.Drawn && !preview.TryGetSourceRigPreview(out _) && GL.GetError() == ErrorCode.NoError,
                        "Real native Weavel sideScene initializes before source admission.");
                    Rollback(scene, preview, captured[0].Materials);
                    foreach (var c in captured)
                    {
                        var candidates = c.Selection.Clips.Where(k => k.Duration > 0 && (!chosen.ContainsKey(c.Id) || k.Ordinal == chosen[c.Id])).Take(24).ToArray();
                        Frame? a = null, b = null; CharacterSourcePreviewClipChoice? choice = null;
                        foreach (var clip in candidates)
                        {
                            a = Draw(c, clip.Ordinal, 0); b = Draw(c, clip.Ordinal, clip.Duration * .37);
                            observations.Add(new { Context = context, Character = c.Id, Clip = clip.Ordinal, AVisible = a.Visible, BVisible = b.Visible,
                                PaletteChanged = a.Palette != b.Palette, PixelsChanged = a.Pixels != b.Pixels });
                            if (a.Visible && b.Visible && a.Palette != b.Palette && a.Pixels != b.Pixels) { choice = clip; chosen[c.Id] = clip.Ordinal; break; }
                        }
                        Check(choice != null && a != null && b != null, c.Id + " real nonzero-time pose changes both palette and GPU pixels.");
                        var again = Draw(c, choice!.Ordinal, 0, capture: context == 0);
                        Check(a!.Palette == again.Palette && a.Pixels == again.Pixels && ReferenceEquals(a.Model, b!.Model)
                            && ReferenceEquals(a.Model, again.Model) && a.Lists.SequenceEqual(b.Lists) && a.Lists.SequenceEqual(again.Lists)
                            && a.Textures.SequenceEqual(b.Textures) && a.Programs.SequenceEqual(b.Programs), c.Id + " A/B/A is deterministic with stable GPU resources.");
                        string raw = PaletteHash(again.Model);
                        Clear(width, height); LauncherHunter.Draw(window, width, height); GL.Finish(); frames++;
                        Check(GL.GetError() == ErrorCode.NoError && raw == PaletteHash(again.Model), c.Id + " repeat draw cannot mutate raw source skin palettes.");
                        if (context == 0) firstPalettes.Add(c.Id, again.Palette);
                        else Check(firstPalettes[c.Id] == again.Palette, c.Id + " recreated context retains exact source palette content.");
                        if (c.Joints == 35)
                        {
                            var ready = c.Selection.Clips.SingleOrDefault(k => k.Ordinal == 14);
                            Check(ready != null && ready.Name == "B_ready_pirate" && ready.Duration > 0,
                                c.Id + " neutral QA uses the declared strict B_ready_pirate ordinal14.");
                            var readyA = Draw(c, ready!.Ordinal, 0, capture: context == 0, captureTag: "ready-zero");
                            var readyB = Draw(c, ready.Ordinal, ready.Duration * .37, capture: context == 0, captureTag: "ready-offgrid037");
                            var readyAgain = Draw(c, ready.Ordinal, 0);
                            Check(readyA.Visible && readyB.Visible && readyAgain.Visible
                                && readyA.Palette == readyAgain.Palette && readyA.Pixels == readyAgain.Pixels,
                                c.Id + " ready zero/offgrid/zero views stay visible and return exact palette and framebuffer content.");
                            Check(ReferenceEquals(again.Model, readyA.Model) && ReferenceEquals(readyA.Model, readyB.Model)
                                && ReferenceEquals(readyA.Model, readyAgain.Model)
                                && readyA.Lists.SequenceEqual(readyB.Lists) && readyA.Lists.SequenceEqual(readyAgain.Lists)
                                && readyA.Textures.SequenceEqual(readyB.Textures) && readyA.Programs.SequenceEqual(readyB.Programs),
                                c.Id + " ready clip/time changes reuse the complete captured model and GPU resources.");
                            observations.Add(new { Context = context, Character = c.Id, Clip = ready.Ordinal, ClipName = ready.Name,
                                Scope = "neutral-ready-visual-QA", OffgridSeconds = ready.Duration * .37,
                                ZeroPixelSha256 = readyA.Pixels, OffgridPixelSha256 = readyB.Pixels,
                                ZeroPaletteSha256 = readyA.Palette, OffgridPaletteSha256 = readyB.Palette,
                                PaletteChanged = readyA.Palette != readyB.Palette, PixelsChanged = readyA.Pixels != readyB.Pixels,
                                TimeChangeIsObservationOnly = true });
                        }
                    }
                    Check(((IDictionary)Get(scene, "_sourceGxPrograms")!).Count == expectedPrograms,
                        "Same Scene contains exactly the deduplicated complete selected-profile shader set.");
                    var first = captured[0]; var restored = Draw(first, chosen[first.Id], 0);
                    Check(restored.Visible && restored.Palette == firstPalettes[first.Id], "Returning from all appearances restores the first captured profile.");
                    preview = Preview(scene);
                    double scheduled = (double)Get(scene, "_launcherPreviewNextStep")!; long steps = (long)Get(preview, "_previewSteps")!;
                    Set(scene, "_launcherPreviewNextStep", double.MaxValue);
                    try
                    {
                        LauncherHunter.SourcePreviewRequest = null; Clear(width, height); LauncherHunter.Draw(window, width, height); GL.Finish(); frames++;
                        Check(LauncherHunter.Drawn && !preview.TryGetSourceRigPreview(out _) && (long)Get(preview, "_previewSteps")! == steps,
                            "Removing owner request clears source immediately without a native simulation step.");
                        Check(restored.Lists.All(id => !GL.IsList(id)), "Immediate source release retires its display lists.");
                    }
                    finally { Set(scene, "_launcherPreviewNextStep", scheduled); }
                    object refusedOwner = new();
                    var refusedRequest = new CharacterSourceStageRequest(refusedOwner, first.Selection.Pack, first.Selection.SelectedAppearance, int.MaxValue, 0);
                    LauncherHunter.SourcePreviewRequest = refusedRequest;
                    Check(!preview.SyncSourceStage(refusedRequest)
                        && !preview.TryGetSourceRigPreview(out _), "Invalid clip cannot publish a partially attached source model.");
                    int refusedPrograms = ((IDictionary)Get(scene, "_sourceGxPrograms")!).Count;
                    var refusedAgain = refusedRequest with { ClipOrdinal = chosen[first.Id] };
                    LauncherHunter.SourcePreviewRequest = refusedAgain;
                    Check(!preview.SyncSourceStage(refusedAgain)
                        && !preview.TryGetSourceRigPreview(out _) && ((IDictionary)Get(scene, "_sourceGxPrograms")!).Count == refusedPrograms,
                        "Same refused owner cannot resurrect a source or recompile by changing only clip ordinal.");
                    owner = new();
                    var reopened = Draw(first, chosen[first.Id], 0);
                    Check(reopened.Visible && reopened.Palette == restored.Palette, "A fresh explicit owner can reopen the exact immutable source after refusal.");
                    LauncherHunter.ReleaseSourcePreview(owner);
                    var invalidClock = new CharacterSourceStageRequest(owner, first.Selection.Pack, first.Selection.SelectedAppearance, chosen[first.Id], double.NaN);
                    LauncherHunter.SourcePreviewRequest = invalidClock;
                    Check(!preview.SyncSourceStage(invalidClock)
                        && !preview.TryGetSourceRigPreview(out _), "Invalid source clock is refused before publication.");
                    contexts.Add(new { Context = context, SelectedAppearances = captured.Count, ExpectedPrograms = expectedPrograms, LivePrograms = gpuPrograms.Count,
                        LiveTextures = gpuTextures.Count, ObservedLists = gpuLists.Count, AllSelectedSameScene = true, SourceOwnerReleased = true });

                    Frame Draw(Captured c, int ordinal, double seconds, bool capture = false, string captureTag = "pose")
                    {
                        var request = new CharacterSourceStageRequest(owner, c.Selection.Pack, c.Selection.SelectedAppearance, ordinal, seconds);
                        Check(request.NativeHunter == c.Selection.NativeHunter, c.Id + " request computes its real native owner.");
                        LauncherHunter.Hunter = c.Selection.NativeHunter;
                        LauncherHunter.SourcePreviewRequest = request; Clear(width, height); LauncherHunter.Draw(window, width, height); GL.Finish(); frames++;
                        Check(LauncherHunter.Drawn && GL.GetError() == ErrorCode.NoError && ReferenceEquals(CurrentScene(), scene)
                            && scene.SideScene && !window.HasScene && !RenderOptions.CharacterModelReplacements,
                            c.Id + " complete production sideScene draw succeeds without Scene replacement or global replacement setting.");
                        var currentPreview = Preview(scene);
                        Check(currentPreview.TryGetSourceRigPreview(out var model) && ReferenceEquals(model.Asset, c.Asset)
                            && model.Joints.Count == c.Joints && model.IndexCount/3 == c.Triangles && model.SourceCannon == null,
                            c.Id + " active GPU model is the exact captured complete body.");
                        var geometry = Get(model, "_sourceGeometry") as CharacterWeightedModelData;
                        Check(geometry != null && geometry.Primitives.Count == model.Segments.Count
                            && geometry.Primitives.SelectMany(p => p.SourceTriangleIds ?? Array.Empty<int>()).Order().SequenceEqual(Enumerable.Range(0, c.Triangles)),
                            c.Id + " GPU segments preserve every original source triangle.");
                        for (int i = 0; i < model.Segments.Count; i++)
                        {
                            var segment = model.Segments[i]; int material = geometry!.Primitives[i].SourceMaterialOrdinal;
                            Check(segment.SourceGx is { } binding && ReferenceEquals(binding.Owner, scene)
                                && ReferenceEquals(binding.Program, c.Materials.Programs[material]) && GL.IsProgram(binding.ProgramId)
                                && segment.AlbedoBinding == null && binding.Textures.Count == binding.Program.Textures.Count && binding.Textures.All(GL.IsTexture),
                                c.Id + " each segment uses its captured material program/all source textures without albedo fallback.");
                        }
                        var packets = ((IEnumerable<RenderItem>)Get(scene, "_previewItems")!).Select(i => i.SourceGx).Where(g => g != null).Select(g => g!).ToArray();
                        Check(packets.Length == model.Segments.Count && packets.All(p => p.Seconds == 0), c.Id + " all actual packets retain independent zero material clock.");
                        if (c.Joints == 35)
                        {
                            var one = model.Segments.Where((_, i) => geometry!.Primitives[i].SourceMaterialOrdinal == 1).First().SourceGx!;
                            float[][] expected = { new[] { .5f, 0, 0, .5f }, new[] { 0, -.5f, 0, .5f }, new[] { 0, 0, 0, 1f } };
                            for (int row = 0; row < 3; row++)
                            {
                                int location = GL.GetUniformLocation(one.ProgramId, "gx_post[" + (row + 3) + "]"); var actual = new float[4];
                                Check(location >= 0, c.Id + " actual material1 keeps post slot1 row active.");
                                GL.GetUniform(one.ProgramId, location, actual);
                                Check(actual.Select(BitConverter.SingleToInt32Bits).SequenceEqual(expected[row].Select(BitConverter.SingleToInt32Bits)),
                                    c.Id + " submitted post slot1 row matches literal environment matrix.");
                            }
                        }
                        int[] lists = model.Segments.Select(s => s.ListId).ToArray();
                        int[] textures = model.Segments.SelectMany(s => s.SourceGx!.Textures).ToArray();
                        int[] programs = model.Segments.Select(s => s.SourceGx!.ProgramId).ToArray();
                        Check(lists.All(GL.IsList) && textures.Length > 0 && programs.All(GL.IsProgram), c.Id + " complete live GPU resources required.");
                        foreach (int value in lists) gpuLists.Add(value); foreach (int value in textures) gpuTextures.Add(value); foreach (int value in programs) gpuPrograms.Add(value);
                        byte[] pixels = new byte[checked(width * height * 4)]; int pack = GL.GetInteger(GetPName.PackAlignment);
                        try
                        {
                            GL.PixelStore(PixelStoreParameter.PackAlignment, 1); GL.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                            Check(GL.GetError() == ErrorCode.NoError, c.Id + " bounded actual framebuffer readback succeeds.");
                            if (capture && captures != null)
                            {
                                string path = Path.Combine(captures, c.Id + "-clip" + ordinal + "-" + captureTag + "-generated-context" + context + ".png");
                                Check(!File.Exists(path) && ScreenCapture.SaveWindow(width, height, path), c.Id + " optional ignored QA capture saved.");
                                pictures.Add(new { Character = c.Id, Context = context, ClipOrdinal = ordinal, Seconds = seconds, Label = captureTag,
                                    Path = path, Sha256 = HashFile(path), DeleteAfterVisualReview = true });
                            }
                            return new(Sha(pixels), PaletteHash(model), lists, textures, programs, pixels.Where((_, i) => i % 4 != 3).Any(v => v != 0), model);
                        }
                        finally { GL.PixelStore(PixelStoreParameter.PackAlignment, pack); Array.Clear(pixels); }
                    }
                }
                finally
                {
                    LauncherHunter.SourcePreviewRequest = null; LauncherHunter.ReleaseSourcePreview(owner); LauncherHunter.Reset();
                    if (scene != null) { try { scene.UnloadGl(); } finally { SideScene.SetValue(null, null); } }
                    Check(gpuPrograms.All(id => !GL.IsProgram(id)) && gpuTextures.All(id => !GL.IsTexture(id)) && gpuLists.All(id => !GL.IsList(id)),
                        "Scene retirement deletes every observed source program, image and list before context teardown.");
                    CosmeticPreview.Loadout = null; CosmeticPreview.Yaw = 0; CosmeticPreview.Zoom = 1;
                }
            }
        }
        void Rollback(Scene scene, HunterPreviewEntity preview, CharacterSourceGxMaterials materials)
        {
            var prior = OpenGlFailureInjection.ShaderObjectCallback; var shaders = new List<int>(); var programs = new List<int>(); bool injected = false;
            try
            {
                OpenGlFailureInjection.ShaderObjectCallback = (stage, handle) =>
                {
                    if (!stage.StartsWith("source-body-gx-", StringComparison.Ordinal)) return;
                    if (stage == "source-body-gx-program") { programs.Add(handle); if (programs.Count == 2) throw new OpenGlInjectedFailureException(stage); }
                    else shaders.Add(handle);
                };
                try { scene.PrepareSourceGxMaterials(materials); } catch (OpenGlInjectedFailureException) { injected = true; }
                Check(injected && programs.Count == 2 && programs.All(id => !GL.IsProgram(id)) && shaders.All(id => !GL.IsShader(id))
                    && ((IDictionary)Get(scene, "_sourceGxPrograms")!).Count == 0 && !preview.TryGetSourceRigPreview(out _),
                    "Second actual source program-link failure rolls back every new object without partial source publication.");
                rollback.Add(new { ProgramsCreated = programs.Count, ShaderObjectsCreated = shaders.Count, AllDeleted = true, SourcePublished = false });
            }
            finally { OpenGlFailureInjection.ShaderObjectCallback = prior; }
        }
    }
    private static Scene CurrentScene() => SideScene.GetValue(null) as Scene ?? throw new InvalidDataException("Expected launcher sideScene absent.");
    private static HunterPreviewEntity Preview(Scene scene) => Get(scene, "_preview") as HunterPreviewEntity ?? throw new InvalidDataException("Expected HunterPreview owner absent.");
    private static object? Get(object value, string field) => value.GetType().GetField(field, Hidden)!.GetValue(value);
    private static void Set(object value, string field, object data) => value.GetType().GetField(field, Hidden)!.SetValue(value, data);
    private static void Clear(int width, int height)
    { GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest); GL.ClearColor(0, 0, 0, 1); GL.ClearDepth(1); GL.DepthMask(true); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit); }
    private static string PaletteHash(CharacterWeightedRenderModel model)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var segment in model.Segments) foreach (float v in segment.SourceMatrixPalette ?? throw new InvalidDataException("Source palette absent."))
        { if (!float.IsFinite(v)) throw new InvalidDataException("Nonfinite source matrix."); hash.AppendData(BitConverter.GetBytes(v)); }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static string Inventory(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var pending = new Stack<string>(); pending.Push(root);
        int files = 0; long bytes = 0;
        while (pending.Count != 0)
        {
            string folder = pending.Pop(); if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Native inventory does not follow directory aliases.");
            foreach (string child in Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal).Reverse()) pending.Push(child);
            foreach (string file in Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal))
            {
                if (++files > 65536 || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || (bytes += new FileInfo(file).Length) > 1024L*1024*1024)
                    throw new InvalidDataException("Native inventory exceeds its bounded owned-root budget.");
                hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file))); hash.AppendData(new byte[] { 0 }); hash.AppendData(Convert.FromHexString(HashFile(file)));
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static string HashFile(string file) { using var stream = File.OpenRead(file); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
