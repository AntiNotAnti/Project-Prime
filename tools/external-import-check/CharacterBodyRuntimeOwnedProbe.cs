using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Import;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.Platform;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Explicit owned-source integration fixture. Only hashes/counts leave memory.
// Keys/mouse drive native input; the one lethal damage call is declared setup,
// not a claim that a combat opponent earned the kill.
internal static class CharacterBodyRuntimeOwnedProbe
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> Key = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly Action<MouseState, Vector2> MousePosition = typeof(MouseState)
        .GetProperty("Position", Hidden | BindingFlags.Public)!.SetMethod!.CreateDelegate<Action<MouseState, Vector2>>();
    private static readonly Keys[] ScriptKeys = [Keys.Enter, Keys.W, Keys.S, Keys.A, Keys.D, Keys.Space, Keys.B, Keys.Right];
    private sealed record RunResult(string Identity, string[] States, Dictionary<string, string>[] Components, object[] Stages, int SourceFrames,
        int NativeFallbackFrames, int DrawRepeats, bool Jumped, bool Morphed, bool Died, bool Respawned,
        bool Fired, bool Charged, bool Turned, bool Moved, string? Generation, string[] PixelHashes);
    private sealed record Stage(string Name, int Steps);
    private static readonly Stage[] Script = [new("idle", 15), new("forward", 30), new("left", 20),
        new("backward", 20), new("fire", 16), new("charge", 100), new("release", 20), new("turn", 20),
        new("jump", 90), new("settle", 60), new("morph", 100), new("unmorph", 100), new("death-respawn", 420)];

    internal static int Run(string rvz)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "prime-body-runtime-" + Guid.NewGuid().ToString("N"));
        string cwd = Environment.CurrentDirectory;
        string? oldData = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        string? native = Environment.GetEnvironmentVariable("PRIME_BODY_TEST_ASSETS");
        string? captureDirectory = Environment.GetEnvironmentVariable("PRIME_BODY_TEST_CAPTURE_DIR");
        string? before = null, after = null, nativeBefore = null, nativeAfter = null;
        string? failure = null, errorType = null, errorHash = null, renderer = null, glVersion = null;
        bool cleaned = false;
        int checks = 0;
        var results = new List<RunResult>();
        var captures = new List<object>();
        var parity = new List<object>();
        bool allParity = true;
        bool oldHd = RenderOptions.CharacterModelReplacements, oldBright = RenderOptions.BrightSkins,
            oldCosmetics = RenderOptions.ShowCustomCosmetics, oldFps = RenderOptions.ShowFps,
            oldForce = MapAudit.ForceEveryone, oldScript = NetTestScript.Enabled;
        bool focused = GamepadContexts.Focused, menu = GamepadContexts.MenuVisible,
            capturing = GamepadContexts.Capturing, text = GamepadContexts.TextEntryActive,
            stylus = PointerInput.StylusMode;
        int cap = FrameTiming.FrameRateCap;
        ShadowQuality oldShadow = RenderOptions.Shadows;
        var watch = Stopwatch.StartNew();
        void Check(bool value, string label)
        {
            if (!value) { failure = label; throw new InvalidDataException(label); }
            checks++;
        }
        void Bound() { if (watch.Elapsed.TotalMinutes > 10) throw new TimeoutException("Owned body fixture exceeded ten minutes."); }
        try
        {
            Check(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64
                && !Headless.Active && !NetSession.Active && !DemoPlayback.IsActive && !ReplayCapture.LiveRecordingActive,
                "fresh Windows x64 native graphics process without live session or replay");
            Check(!string.IsNullOrWhiteSpace(native) && Directory.Exists(native),
                "PRIME_BODY_TEST_ASSETS must identify the existing owned AMHE1 directory");
            rvz = Path.GetFullPath(rvz); native = Path.GetFullPath(native!);
            if (!string.IsNullOrWhiteSpace(captureDirectory))
            {
                captureDirectory = Path.GetFullPath(captureDirectory);
                string artifactRoot = Path.GetFullPath(Path.Combine(cwd, "artifacts")) + Path.DirectorySeparatorChar;
                Check(captureDirectory.StartsWith(artifactRoot, StringComparison.OrdinalIgnoreCase),
                    "optional gameplay captures must stay inside this worktree artifacts directory");
                Directory.CreateDirectory(captureDirectory);
            }
            before = HashFile(rvz); nativeBefore = Inventory(native);
            string user = Path.Combine(temporary, "user"), imports = Path.Combine(temporary, "imports");
            Directory.CreateDirectory(user);
            File.WriteAllText(Path.Combine(user, "paths.txt"), Ver.AMHE1 + "=" + native + "\nExport=" + Path.Combine(user, "Export") + "\n");
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", user);
            Directory.SetCurrentDirectory(user);
            ConsoleSetup.Run(); Paths.UpdatePaths(); Paths.SetPath(Ver.AMHE1, native); Paths.MphKey = Ver.AMHE1;
            GraphicsBackendPolicy.Configure("opengl");
            RenderOptions.CharacterModelReplacements = false; RenderOptions.BrightSkins = false;
            RenderOptions.ShowCustomCosmetics = true; RenderOptions.ShowFps = false;
            RenderOptions.Shadows = ShadowQuality.Off;
            MapAudit.ForceEveryone = true; NetTestScript.Enabled = false;
            GamepadManager.ClearAll(); GamepadContexts.Focused = true;
            GamepadContexts.MenuVisible = GamepadContexts.Capturing = GamepadContexts.TextEntryActive = false;
            PointerInput.StylusMode = false; PointerInput.Reset(); PointerDevice.Reset();
            Scene.LauncherPreview = false; Scene.PreviewWanted = false;
            RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz, imports,
                selectedIds: ["darksamus-echoes", "darksamus-overcharged-echoes"]);
            var normal = CharacterSourcePreviewSelection.Load("darksamus-echoes", CancellationToken.None, imports);
            var final = CharacterSourcePreviewSelection.Load("darksamus-overcharged-echoes", CancellationToken.None, imports);
            Check(normal.SelectedAppearance != final.SelectedAppearance, "normal and Overcharged resolve different appearance authority");
            var settings = DesktopGlContext.Settings(background: true);
            settings.ClientSize = new(480, 360); settings.StartVisible = true; settings.StartFocused = false;
            settings.Title = "Project Prime owned offline imported-body runtime probe";
            using var window = new NativeWindow(settings);
            using var graphics = new DesktopGraphicsSession(window);
            GLFW.SwapInterval(0);
            renderer = GL.GetString(StringName.Renderer); glVersion = GL.GetString(StringName.Version);
            foreach (var selection in new CharacterSourcePreviewSelection?[] { null, normal, final })
            {
                Bound();
                RunResult result = Exercise(selection);
                RunResult? baseline = results.FirstOrDefault();
                results.Add(result);
                if (baseline != null)
                {
                    Check(result.States.Length == baseline.States.Length, "native and imported traces have equal lengths");
                    int mismatch = Enumerable.Range(0, result.States.Length).FirstOrDefault(i => result.States[i] != baseline.States[i], -1);
                    string[] differing = mismatch < 0 ? Array.Empty<string>() : result.Components[mismatch]
                        .Where(part => baseline.Components[mismatch][part.Key] != part.Value).Select(part => part.Key).ToArray();
                    parity.Add(new { result.Identity, FirstMismatch = mismatch, DifferingComponents = differing });
                    Console.WriteLine(JsonSerializer.Serialize(new { Probe = "owned-body-parity", result.Identity,
                        FirstMismatch = mismatch, DifferingComponents = differing }));
                    allParity &= mismatch < 0;
                    Check(result.PixelHashes.Where((hash, i) => hash != baseline.PixelHashes[i]).Any(),
                        result.Identity + " actual framebuffer differs from corresponding native checkpoints");
                }
            }
            Check(allParity, "every imported native gameplay trace matches seeded native baseline; see component diagnostics");

            RunResult Exercise(CharacterSourcePreviewSelection? selection)
            {
                FrameTiming.Reset(); PointerInput.Reset(); PointerDevice.Reset();
                Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
                var keyboard = SyntheticInput.CreateKeyboard(); var mouse = SyntheticInput.CreateMouse();
                Scene? scene = null;
                try
                {
                    scene = new(window.FramebufferSize, keyboard, mouse, _ => { }, () => { });
                    scene.Random.SetRng1(123456); scene.Random.SetRng2(654321);
                    scene.AddPlayer(Hunter.Samus);
                    scene.AddRoom("MP3 PROVING GROUND", GameMode.Battle, playerCount: 1, nodeLayerMask: 0);
                    scene.OnLoad(); scene.OnResize();
                    var inputMode = typeof(Scene).GetField("_inputMode", Hidden)!;
                    inputMode.SetValue(scene, Enum.Parse(inputMode.FieldType, "All"));
                    scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
                    scene.GameState.SpawnProtection = false; scene.GameState.EnhancedHunters = false;
                    scene.GameState.ShadowFreeze = false; scene.GameState.ThirdPersonCamera = true;
                    var player = scene.Players.Main; player.IsBot = false; player.IgnoreItemPickups = true;
                    var nativeBipedLists = player.BipedModel2.Model.Meshes.Select(mesh => mesh.ListId).Where(id => id > 0).ToHashSet();
                    Check(nativeBipedLists.Count > 0, "initialized native biped owns positive geometry list identities");
                    foreach (var button in player.Controls.All) { button.Type = ButtonType.Key; button.Key = Keys.Unknown; }
                    player.Controls.Shoot.Key = Keys.Enter; player.Controls.MoveUp.Key = Keys.W;
                    player.Controls.MoveDown.Key = Keys.S; player.Controls.MoveLeft.Key = Keys.A;
                    player.Controls.MoveRight.Key = Keys.D; player.Controls.Jump.Key = Keys.Space;
                    player.Controls.Morph.Key = Keys.B; player.Controls.AimRight.Key = Keys.Right;
                    // Native keyboard aiming is unfinished and suppresses mouse
                    // deltas when its arrow bind is held. Movement stays on the
                    // real keyboard producer; turning uses only the real mouse.
                    player.Controls.MouseAim = true; player.Controls.KeyboardAim = false;
                    Key(keyboard, Keys.Enter, true);
                    for (int n = 0; n < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); n++) scene.OnSimulationFrame();
                    Key(keyboard, Keys.Enter, false); scene.CameraSequences.Current?.End();
                    for (int n = 0; n < 240; n++) scene.OnSimulationFrame();
                    scene.CameraSequences.Current?.End();
                    Check(player.Health > 0 && player.ModIsInPlay && !player.IsAltForm, "real native shoot-to-spawn reaches living Samus");
                    if (selection != null) player.ModBindSourceBody(selection);
                    CharacterSourceBodyOwner? owner = player.ModSourceBody;
                    Check(selection == null ? owner == null : owner is { Current: true }, "load barrier installs exactly requested body owner");
                    if (owner != null)
                        Check(owner.Model.Joints.Count == 39 && owner.Model.Segments.Count > 0
                            && owner.Model.Segments.All(segment => segment.SourceJointPalette is { Count: > 0 and <= 32 }
                                && segment.SourceGx is { } binding && GL.IsProgram(binding.ProgramId)
                                && binding.Textures.Count > 0 && binding.Textures.All(GL.IsTexture)),
                            "actual body retains full39 source rig and live source programs/textures with bounded segment palettes");
                    var states = new List<string>(); var components = new List<Dictionary<string, string>>();
                    var stages = new List<object>(); var pixels = new List<string>();
                    int sourceFrames = 0, fallbackFrames = 0, repeats = 0;
                    int sourceFireFrames = 0, sourceChargeFrames = 0;
                    bool jumped = false, morphed = false, died = false, respawned = false, fired = false, charged = false,
                        moved = false, turned = false;
                    Vector3 initialPosition = player.Position, initialFacing = player.FacingVector;
                    var paletteHashes = new HashSet<string>();
                    var capturedLabels = new HashSet<string>(StringComparer.Ordinal);
                    Picture(false); // Establish native draw-derived anchors before invariance assertions.
                    // Scene load, native first-picture preparation and optional
                    // source compilation have different one-time work. Seed the
                    // identical authoritative fixture only after that barrier.
                    // No draw or simulation step below may reset or omit RNG.
                    Rng.SetRng1(123456); Rng.SetRng2(654321);
                    scene.Random.SetRng1(123456); scene.Random.SetRng2(654321);
                    foreach (Stage stage in Script)
                    {
                        int sourceBefore = sourceFrames, fallbackBefore = fallbackFrames;
                        Vector3 stagePosition = player.Position, stageFacing = player.FacingVector;
                        float stageMoveSquared = 0, stageAimSquared = 0;
                        if (stage.Name == "death-respawn")
                        {
                            player.TakeDamage(10000, DamageFlags.Death | DamageFlags.IgnoreInvuln, null, null);
                            died = player.Health == 0;
                        }
                        for (int age = 0; age < stage.Steps; age++)
                        {
                            Bound(); foreach (Keys key in ScriptKeys) Key(keyboard, key, false);
                            if (stage.Name == "forward") Key(keyboard, Keys.W, true);
                            if (stage.Name == "left") Key(keyboard, Keys.A, true);
                            if (stage.Name == "backward") Key(keyboard, Keys.S, true);
                            if (stage.Name == "turn")
                            {
                                MousePosition(mouse, mouse.Position + new Vector2(16, 0));
                            }
                            if (stage.Name == "fire" && age < 2 || stage.Name == "charge" || stage.Name == "death-respawn" && age % 30 == 0)
                                Key(keyboard, Keys.Enter, true);
                            if (stage.Name == "jump" && age == 0) Key(keyboard, Keys.Space, true);
                            if (stage.Name is "morph" or "unmorph" && age == 0) Key(keyboard, Keys.B, true);
                            FrameTiming.Reset(); scene.OnSimulationFrame();
                            jumped |= player.Speed.Y > .01f && stage.Name == "jump";
                            morphed |= player.IsAltForm;
                            respawned |= died && player.Health > 0 && player.ModIsInPlay;
                            fired |= player.EquipInfo.Beams.Any(b => b.Lifespan > 0 && ReferenceEquals(b.Owner, player));
                            charged |= player.EquipInfo.ChargeLevel >= player.EquipInfo.Weapon.MinCharge * 2
                                && player.EquipInfo.ChargeLevel > 0 && stage.Name == "charge";
                            stageMoveSquared = MathF.Max(stageMoveSquared, (player.Position - stagePosition).LengthSquared);
                            stageAimSquared = MathF.Max(stageAimSquared, (player.FacingVector - stageFacing).LengthSquared);
                            moved |= stage.Name is "forward" or "left" or "backward" && stageMoveSquared > .01f;
                            turned |= stage.Name == "turn" && stageAimSquared > .0001f;
                            Picture(true, stage.Name);
                            if (BodyItems(scene).Count > 0 && stage.Name == "fire") sourceFireFrames++;
                            if (BodyItems(scene).Count > 0 && stage.Name == "charge") sourceChargeFrames++;
                            states.Add(State(scene, player));
                            components.Add(StateParts(scene, player));
                            if (owner != null) paletteHashes.Add(Palette(owner.Model));
                        }
                        byte[] picture = scene.ReadSceneTarget(out int width, out int height)
                            ?? throw new InvalidDataException("Scene framebuffer read failed.");
                        Check(width > 0 && height > 0 && picture.Length > 0, "actual bounded Scene target read");
                        string pixelHash = Sha(picture); pixels.Add(pixelHash);
                        Console.WriteLine(JsonSerializer.Serialize(new { Probe = "owned-body-stage", Identity = selection?.SelectedAppearance.SkinSource ?? "native",
                            stage.Name, MaxMovementSquared = stageMoveSquared, MaxAimDeltaSquared = stageAimSquared,
                            SourceFrames = sourceFrames - sourceBefore, FallbackFrames = fallbackFrames - fallbackBefore,
                            BodyReady = owner?.Ready, LowerAnimation = (PlayerAnimation)((ModelInstance)typeof(PlayerEntity).GetField("_bipedModel1", Hidden)!.GetValue(player)!).AnimInfo.Index[0],
                            UpperAnimation = (PlayerAnimation)player.BipedModel2.AnimInfo.Index[0],
                            LocalBodyOpacity = scene.ModThirdPersonLocalBodyOpacity,
                            BodyStateSupported = typeof(PlayerEntity).GetProperty("SourceBodyStateSupported", Hidden)!.GetValue(player),
                            SourceFireFrames = sourceFireFrames, SourceChargeFrames = sourceChargeFrames }));
                        stages.Add(new { stage.Name, stage.Steps, PixelSha256 = pixelHash,
                            SourceFrames = sourceFrames - sourceBefore, NativeFallbackFrames = fallbackFrames - fallbackBefore,
                            BodyReady = owner?.Ready, MaterialSeconds = owner?.MaterialSeconds,
                            Segments = owner?.Model.Segments.Count, SourceJoints = owner?.Model.Joints.Count,
                            Programs = owner?.Model.Segments.Select(segment => segment.SourceGx!.ProgramId).Distinct().Count(),
                            Textures = owner?.Model.Segments.SelectMany(segment => segment.SourceGx!.Textures).Distinct().Count() });
                    }
                    Check(jumped && morphed && died && respawned && fired && charged,
                        "actual native keyboard script earns jump, morph, native shot/charge, injected death and native respawn");
                    Check(moved, "native keyboard movement stages earn actor displacement");
                    Check(turned, "native mouse turn stage earns facing change");
                    if (owner != null)
                    {
                        Check(sourceFrames > 0 && fallbackFrames > 0 && paletteHashes.Count > 1,
                            "actual body submissions coexist with native fallback and changing source skin");
                        Check(sourceFireFrames > 0 && sourceChargeFrames > 0,
                            "native fire and charge stages visibly submit complete imported bodies");
                        foreach (Keys key in ScriptKeys) Key(keyboard, key, false);
                        for (int settle = 0; settle < 300 && BodyItems(scene).Count != owner.Model.Segments.Count; settle++)
                        { Bound(); FrameTiming.Reset(); scene.OnSimulationFrame(); Picture(false); }
                        Check(BodyItems(scene).Count == owner.Model.Segments.Count && owner.Ready,
                            "shadow boundary starts from a supported complete imported body frame");
                        string beforeShadowState = State(scene, player), beforeShadowPalette = Palette(owner.Model);
                        double beforeShadowClock = owner.MaterialSeconds;
                        RenderOptions.Shadows = ShadowQuality.Low;
                        try
                        {
                            Picture(false);
                            Check(BodyItems(scene).Count == 0 && WorldItems(scene).Any(item => nativeBipedLists.Contains(item.ListId)),
                                "Low dynamic shadows choose complete native biped fallback before imported submission");
                        }
                        finally { RenderOptions.Shadows = ShadowQuality.Off; }
                        Picture(false);
                        Check(BodyItems(scene).Count == owner.Model.Segments.Count
                            && State(scene, player) == beforeShadowState && Palette(owner.Model) == beforeShadowPalette
                            && owner.MaterialSeconds == beforeShadowClock,
                            "shadow Off resumes frozen imported body without simulation, native state or clock changes");

                        // This is a second source resource/pose owner over the
                        // SAME actual actor model, not a fabricated second actor
                        // or qualification of two simultaneously drawn bodies.
                        using (var independent = new CharacterSourceBodyOwner(scene, player, selection!))
                        {
                            Check(independent.Current && !ReferenceEquals(independent.Model, owner.Model)
                                && independent.Model.Segments.Count == owner.Model.Segments.Count
                                && independent.Model.Segments.Zip(owner.Model.Segments).All(pair =>
                                    !ReferenceEquals(pair.First.SourceMatrixPalette, pair.Second.SourceMatrixPalette)
                                    && !ReferenceEquals(pair.First.SourcePresentationPalette, pair.Second.SourcePresentationPalette)),
                                "two source owners over one native actor model retain independent raw and presentation palette arrays");
                        }
                        Check(owner.Current && Palette(owner.Model) == beforeShadowPalette && owner.MaterialSeconds == beforeShadowClock,
                            "disposing independent same-model owner preserves installed owner, skin and clock");
                        double clock = owner.MaterialSeconds; string raw = Palette(owner.Model);
                        int pausedPackets = BodyItems(scene).Count;
                        scene.GameState.PauseMenu();
                        try
                        {
                            for (int n = 0; n < 4; n++) { scene.OnSimulationFrame(); Picture(false); }
                            Check(owner.MaterialSeconds == clock && Palette(owner.Model) == raw,
                                "paused simulation and render do not advance source clock or raw palettes");
                            Check(BodyItems(scene).Count == 0 || BodyItems(scene).Count == owner.Model.Segments.Count,
                                "pause submits a complete frozen body or native camera fallback without partial source geometry");
                            Console.WriteLine(JsonSerializer.Serialize(new { Probe = "owned-body-pause",
                                Identity = selection!.SelectedAppearance.SkinSource, BeforePackets = pausedPackets,
                                PausedPackets = BodyItems(scene).Count, MaterialClockFrozen = owner.MaterialSeconds == clock,
                                RawSkinFrozen = Palette(owner.Model) == raw }));
                        }
                        finally { scene.GameState.UnpauseMenu(); }
                        int[] lists = owner.Model.Segments.Select(s => s.ListId).ToArray();
                        CharacterModelRuntime.Release(scene);
                        Check(!owner.Current && !owner.Prepare(Matrix4.Identity), "retired context cannot resurrect body owner");
                        Check(lists.All(id => !GL.IsList(id)), "context retirement deletes every captured source display list");
                        scene.OnDrawFrame(); Check(BodyItems(scene).Count == 0, "retired owner falls back before source submission");
                    }
                    return new(selection?.SelectedAppearance.SkinSource ?? "native", states.ToArray(), components.ToArray(), stages.ToArray(), sourceFrames,
                        fallbackFrames, repeats, jumped, morphed, died, respawned, fired, charged, turned, moved,
                        selection?.Generation, pixels.ToArray());

                    void Picture(bool verify, string? captureLabel = null)
                    {
                        scene.OnDrawFrame();
                        var items = BodyItems(scene);
                        if (owner != null)
                        {
                            if (!owner.Ready) Check(items.Count == 0, "unready source owner submits no imported segments");
                            if (player.Health <= 0) Check(items.Count == 0, "actual dead player submits no imported segments");
                            if (player.IsAltForm) Check(items.Count == 0, "actual alternate form submits no imported segments");
                            if (player.IsMorphing) Check(items.Count == 0, "actual morph transition submits no imported segments");
                            if (player.IsUnmorphing) Check(items.Count == 0, "actual unmorph transition submits no imported segments");
                        }
                        if (items.Count > 0)
                        {
                            Check(owner != null && owner.Current && items.Count == owner.Model.Segments.Count
                                && items.All(item => item.SourceGx is { } draw && ReferenceEquals(draw.BodyOwner, owner)
                                    && ReferenceEquals(draw.Binding.Owner, scene) && item.WeightedSkinning
                                    && item.MatrixStackCount is > 0 and <= 32 && draw.Seconds == owner.MaterialSeconds),
                                "production world list contains exactly one complete current source body");
                            Check(WorldItems(scene).All(item => item.SourceGx == null), "GX packets never leak into native multipass lists");
                            Check(WorldItems(scene).All(item => !nativeBipedLists.Contains(item.ListId)),
                                "complete imported body replaces every native biped mesh while allowing other native geometry");
                            sourceFrames++;
                        }
                        else if (owner != null) fallbackFrames++;
                        Check(scene.OnRenderFrame(), "real Scene world renderer accepts frame");
                        if (owner != null && items.Count > 0 && !string.IsNullOrWhiteSpace(captureDirectory)
                            && captureLabel is "idle" or "fire" && capturedLabels.Add(captureLabel))
                        {
                            string imagePath = Path.Combine(captureDirectory, selection!.SelectedAppearance.SkinSource + "-" + captureLabel + ".png");
                            Check(ScreenCapture.SaveWindow(scene, imagePath), "explicitly enabled gameplay capture saved");
                            captures.Add(new { Identity = selection.SelectedAppearance.SkinSource, Label = captureLabel,
                                Path = imagePath, Sha256 = HashFile(imagePath), SourcePackets = items.Count });
                        }
                        if (verify)
                        {
                            string gameplay = State(scene, player); string? raw = owner == null ? null : Palette(owner.Model);
                            double? clock = owner?.MaterialSeconds;
                            scene.OnDrawFrame(); Check(scene.OnRenderFrame(), "repeated native world render accepts frame");
                            Check(State(scene, player) == gameplay && owner?.MaterialSeconds == clock
                                && (owner == null || Palette(owner.Model) == raw),
                                "repeated draw preserves native health, actor, ammo, aim, projectiles, RNG and source clock/raw skin");
                            repeats++;
                        }
                        Check(GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError, "actual body/native world frame leaves no GL error");
                        DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
                    }
                }
                finally { if (scene != null) Release(scene); }
            }
        }
        catch (Exception exception)
        {
            errorType = exception.GetType().Name; errorHash = Sha(Encoding.UTF8.GetBytes(exception.Message));
            Console.Error.WriteLine("OWNED BODY RUNTIME failure: " + exception);
        }
        finally
        {
            try
            {
                Directory.SetCurrentDirectory(cwd); Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", oldData);
                string target = Path.GetFullPath(temporary), expectedParent = Path.GetFullPath(Path.GetTempPath());
                if (!target.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(target).StartsWith("prime-body-runtime-", StringComparison.Ordinal))
                    throw new InvalidDataException("Derived cleanup target escaped expected temporary root.");
                if (Directory.Exists(target)) Directory.Delete(target, true);
                cleaned = !Directory.Exists(target);
                if (before != null) after = HashFile(rvz);
                if (nativeBefore != null) nativeAfter = Inventory(native!);
                Check(cleaned && before != null && before == after && nativeBefore != null && nativeBefore == nativeAfter,
                    "isolated derived data deleted and both owned sources byte-identical");
            }
            catch (Exception exception)
            { errorType ??= exception.GetType().Name; errorHash ??= Sha(Encoding.UTF8.GetBytes(exception.Message)); }
            RenderOptions.CharacterModelReplacements = oldHd; RenderOptions.BrightSkins = oldBright;
            RenderOptions.ShowCustomCosmetics = oldCosmetics; RenderOptions.ShowFps = oldFps;
            RenderOptions.Shadows = oldShadow;
            MapAudit.ForceEveryone = oldForce; NetTestScript.Enabled = oldScript;
            GamepadContexts.Focused = focused; GamepadContexts.MenuVisible = menu;
            GamepadContexts.Capturing = capturing; GamepadContexts.TextEntryActive = text;
            PointerInput.StylusMode = stylus; PointerInput.Reset(); PointerDevice.Reset();
            FrameTiming.FrameRateCap = cap; FrameTiming.Reset();
            Console.WriteLine(JsonSerializer.Serialize(new { Pass = errorType == null, Checks = checks,
                Renderer = renderer, GlVersion = glVersion, FailedCheck = failure, ErrorType = errorType, ErrorMessageSha256 = errorHash,
                RvzSha256Before = before, RvzSha256After = after, NativeSha256Before = nativeBefore, NativeSha256After = nativeAfter,
                TemporaryDerivedDataDeleted = cleaned,
                Captures = captures,
                Parity = parity,
                Runs = results.Select(r => new { r.Identity, r.Generation, Frames = r.States.Length,
                    GameplayTraceSha256 = Sha(Encoding.UTF8.GetBytes(string.Join("\n", r.States))), r.Stages,
                    r.SourceFrames, r.NativeFallbackFrames, r.DrawRepeats, r.Jumped, r.Morphed, r.Died, r.Respawned,
                    r.Fired, r.Charged, r.Turned, r.Moved }),
                Scope = "Actual owned offline Windows x64 Samus Scene/input/world GL; direct lethal damage and common pre-script RNG seed are declared fixture setup; optional gameplay PNGs require explicit capture environment",
                Unqualified = new[] { "physical devices", "visual/art judgment from hashes", "online or replay", "two actors or simultaneous source rendering",
                    "all status/crouch/LOD/bright-skin fallback cases", "actual opponent-earned death", "achieved display refresh" }
            }));
        }
        return errorType == null ? 0 : 1;
    }

    private static List<RenderItem> BodyItems(Scene scene) => (List<RenderItem>)typeof(Scene).GetField("_sourceBodyItems", Hidden)!.GetValue(scene)!;
    private static IEnumerable<RenderItem> WorldItems(Scene scene) => new[] { "_nonDecalItems", "_translucentItems", "_decalItems" }
        .SelectMany(name => (List<RenderItem>)typeof(Scene).GetField(name, Hidden)!.GetValue(scene)!);
    private static string Palette(CharacterWeightedRenderModel model)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (var segment in model.Segments)
            foreach (float value in segment.SourceMatrixPalette!) writer.Write(value);
        return Sha(stream.ToArray());
    }
    private static string State(Scene scene, PlayerEntity player)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        void Vector(Vector3 vector) { writer.Write(vector.X); writer.Write(vector.Y); writer.Write(vector.Z); }
        writer.Write(scene.FrameCount); writer.Write(scene.Random.Rng1); writer.Write(scene.Random.Rng2);
        writer.Write(player.Health); Vector(player.Position); Vector(player.Speed); Vector(player.FacingVector);
        writer.Write((int)player.CurrentWeapon); writer.Write(player.IsAltForm); writer.Write(player.IsMorphing); writer.Write(player.IsUnmorphing);
        writer.Write(player.EquipInfo.ChargeLevel); writer.Write((uint)player.Flags1);
        foreach (int value in scene.GameState.Kills) writer.Write(value);
        foreach (int value in scene.GameState.Deaths) writer.Write(value);
        foreach (int value in scene.GameState.Points) writer.Write(value);
        foreach (string name in new[] { "_aimVec", "_gunVec1", "_gunVec2", "_muzzlePos" })
            Vector((Vector3)typeof(PlayerEntity).GetField(name, Hidden)!.GetValue(player)!);
        foreach (int amount in (int[])typeof(PlayerEntity).GetField("_ammo", Hidden)!.GetValue(player)!) writer.Write(amount);
        foreach (var beam in player.EquipInfo.Beams)
        {
            writer.Write(beam.Lifespan); Vector(beam.Position); Vector(beam.Direction);
            writer.Write((int)beam.Flags); writer.Write(ReferenceEquals(beam.Owner, player));
        }
        return Sha(stream.ToArray());
    }
    private static Dictionary<string, string> StateParts(Scene scene, PlayerEntity player)
    {
        static string Hash(Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            write(writer); return Sha(stream.ToArray());
        }
        static void Vector(BinaryWriter writer, Vector3 vector)
        { writer.Write(vector.X); writer.Write(vector.Y); writer.Write(vector.Z); }
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Frame"] = Hash(w => w.Write(scene.FrameCount)),
            ["Rng1"] = Hash(w => w.Write(scene.Random.Rng1)), ["Rng2"] = Hash(w => w.Write(scene.Random.Rng2)),
            ["Health"] = Hash(w => w.Write(player.Health)),
            ["Position"] = Hash(w => Vector(w, player.Position)), ["Speed"] = Hash(w => Vector(w, player.Speed)),
            ["Facing"] = Hash(w => Vector(w, player.FacingVector)),
            ["WeaponChargeFlagsForms"] = Hash(w => { w.Write((int)player.CurrentWeapon); w.Write(player.EquipInfo.ChargeLevel);
                w.Write((uint)player.Flags1); w.Write(player.IsAltForm); w.Write(player.IsMorphing); w.Write(player.IsUnmorphing); }),
            ["Ammo"] = Hash(w => { foreach (int amount in (int[])typeof(PlayerEntity).GetField("_ammo", Hidden)!.GetValue(player)!) w.Write(amount); }),
            ["BeamLifespans"] = Hash(w => { foreach (var beam in player.EquipInfo.Beams) w.Write(beam.Lifespan); }),
            ["BeamPositions"] = Hash(w => { foreach (var beam in player.EquipInfo.Beams) Vector(w, beam.Position); }),
            ["BeamDirections"] = Hash(w => { foreach (var beam in player.EquipInfo.Beams) Vector(w, beam.Direction); }),
            ["BeamFlags"] = Hash(w => { foreach (var beam in player.EquipInfo.Beams) w.Write((int)beam.Flags); }),
            ["BeamOwners"] = Hash(w => { foreach (var beam in player.EquipInfo.Beams) w.Write(ReferenceEquals(beam.Owner, player)); }),
            ["MatchTotals"] = Hash(w => { foreach (int value in scene.GameState.Kills.Concat(scene.GameState.Deaths).Concat(scene.GameState.Points)) w.Write(value); })
        };
        foreach (string name in new[] { "_aimVec", "_gunVec1", "_gunVec2", "_muzzlePos" })
            parts.Add(name, Hash(w => Vector(w, (Vector3)typeof(PlayerEntity).GetField(name, Hidden)!.GetValue(player)!)));
        return parts;
    }
    private static void Release(Scene scene)
    {
        var commit = PostMatchCommitContext.Capture(scene.GameState, 0, SaveWhen.Never, 0);
        try { scene.DoCleanup(); }
        finally { try { scene.UnloadGl(); } finally { commit.Commit(); commit.Release(); ForegroundSceneBinding.TryRelease(commit); } }
    }
    private static string Inventory(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var pending = new Stack<string>(); pending.Push(root); int count = 0; long bytes = 0;
        while (pending.Count != 0)
        {
            string directory = pending.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Native inventory rejects directory aliases.");
            foreach (string child in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal).Reverse()) pending.Push(child);
            foreach (string file in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
            {
                if (++count > 65536 || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0
                    || (bytes += new FileInfo(file).Length) > 1024L * 1024 * 1024)
                    throw new InvalidDataException("Native inventory exceeds bounded read budget.");
                hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, file))); hash.AppendData(new byte[] { 0 });
                hash.AppendData(Convert.FromHexString(HashFile(file)));
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
