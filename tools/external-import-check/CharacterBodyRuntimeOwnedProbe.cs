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
    private sealed record RunResult(string Identity, string[] States, object[] Stages, int SourceFrames,
        int NativeFallbackFrames, int DrawRepeats, bool Jumped, bool Morphed, bool Died, bool Respawned,
        bool Fired, bool Charged, bool Turned, bool Moved, string? Generation, string[] PixelHashes);
    private sealed record Stage(string Name, int Steps);
    private static readonly Stage[] Script = [new("idle", 15), new("forward", 30), new("left", 20),
        new("backward", 20), new("turn", 20), new("fire", 16), new("charge", 100), new("release", 20),
        new("jump", 90), new("settle", 60), new("morph", 100), new("unmorph", 100), new("death-respawn", 420)];

    internal static int Run(string rvz)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "prime-body-runtime-" + Guid.NewGuid().ToString("N"));
        string cwd = Environment.CurrentDirectory;
        string? oldData = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        string? native = Environment.GetEnvironmentVariable("PRIME_BODY_TEST_ASSETS");
        string? before = null, after = null, nativeBefore = null, nativeAfter = null;
        string? failure = null, errorType = null, errorHash = null, renderer = null, glVersion = null;
        bool cleaned = false;
        int checks = 0;
        var results = new List<RunResult>();
        bool oldHd = RenderOptions.CharacterModelReplacements, oldBright = RenderOptions.BrightSkins,
            oldCosmetics = RenderOptions.ShowCustomCosmetics, oldFps = RenderOptions.ShowFps,
            oldForce = MapAudit.ForceEveryone, oldScript = NetTestScript.Enabled;
        bool focused = GamepadContexts.Focused, menu = GamepadContexts.MenuVisible,
            capturing = GamepadContexts.Capturing, text = GamepadContexts.TextEntryActive,
            stylus = PointerInput.StylusMode;
        int cap = FrameTiming.FrameRateCap;
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
                    Check(mismatch < 0, result.Identity + " native gameplay trace parity; first mismatch index=" + mismatch);
                    Check(result.PixelHashes.Where((hash, i) => hash != baseline.PixelHashes[i]).Any(),
                        result.Identity + " actual framebuffer differs from corresponding native checkpoints");
                }
            }

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
                    foreach (var button in player.Controls.All) { button.Type = ButtonType.Key; button.Key = Keys.Unknown; }
                    player.Controls.Shoot.Key = Keys.Enter; player.Controls.MoveUp.Key = Keys.W;
                    player.Controls.MoveDown.Key = Keys.S; player.Controls.MoveLeft.Key = Keys.A;
                    player.Controls.MoveRight.Key = Keys.D; player.Controls.Jump.Key = Keys.Space;
                    player.Controls.Morph.Key = Keys.B; player.Controls.AimRight.Key = Keys.Right;
                    player.Controls.MouseAim = true; player.Controls.KeyboardAim = true;
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
                    var states = new List<string>(); var stages = new List<object>(); var pixels = new List<string>();
                    int sourceFrames = 0, fallbackFrames = 0, repeats = 0;
                    bool jumped = false, morphed = false, died = false, respawned = false, fired = false, charged = false,
                        moved = false, turned = false;
                    Vector3 initialPosition = player.Position, initialFacing = player.FacingVector;
                    var paletteHashes = new HashSet<string>();
                    Picture(false); // Establish native draw-derived anchors before invariance assertions.
                    foreach (Stage stage in Script)
                    {
                        int sourceBefore = sourceFrames, fallbackBefore = fallbackFrames;
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
                                Key(keyboard, Keys.Right, true);
                                MousePosition(mouse, mouse.Position + new Vector2(1, 0));
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
                            moved |= stage.Name == "forward" && (player.Position - initialPosition).LengthSquared > .01f;
                            turned |= stage.Name == "turn" && (player.FacingVector - initialFacing).LengthSquared > .0001f;
                            Picture(true);
                            states.Add(State(scene, player));
                            if (owner != null) paletteHashes.Add(Palette(owner.Model));
                        }
                        byte[] picture = scene.ReadSceneTarget(out int width, out int height)
                            ?? throw new InvalidDataException("Scene framebuffer read failed.");
                        Check(width > 0 && height > 0 && picture.Length > 0, "actual bounded Scene target read");
                        string pixelHash = Sha(picture); pixels.Add(pixelHash);
                        stages.Add(new { stage.Name, stage.Steps, PixelSha256 = pixelHash,
                            SourceFrames = sourceFrames - sourceBefore, NativeFallbackFrames = fallbackFrames - fallbackBefore,
                            BodyReady = owner?.Ready, MaterialSeconds = owner?.MaterialSeconds,
                            Segments = owner?.Model.Segments.Count, SourceJoints = owner?.Model.Joints.Count,
                            Programs = owner?.Model.Segments.Select(segment => segment.SourceGx!.ProgramId).Distinct().Count(),
                            Textures = owner?.Model.Segments.SelectMany(segment => segment.SourceGx!.Textures).Distinct().Count() });
                    }
                    Check(jumped && morphed && died && respawned && fired && charged,
                        "actual native keyboard script earns jump, morph, native shot/charge, injected death and native respawn");
                    Check(moved && turned, "actual keyboard/mouse changes movement and aim");
                    if (owner != null)
                    {
                        Check(sourceFrames > 0 && fallbackFrames > 0 && paletteHashes.Count > 1,
                            "actual body submissions coexist with native fallback and changing source skin");
                        double clock = owner.MaterialSeconds; string raw = Palette(owner.Model);
                        int pausedPackets = BodyItems(scene).Count;
                        scene.GameState.PauseMenu();
                        try
                        {
                            for (int n = 0; n < 4; n++) { scene.OnSimulationFrame(); Picture(false); }
                            Check(owner.MaterialSeconds == clock && Palette(owner.Model) == raw,
                                "paused simulation and render do not advance source clock or raw palettes");
                            Check(BodyItems(scene).Count == pausedPackets
                                && (pausedPackets == 0 || pausedPackets == owner.Model.Segments.Count),
                                "pause preserves a complete frozen source body or its existing native fallback");
                        }
                        finally { scene.GameState.UnpauseMenu(); }
                        int[] lists = owner.Model.Segments.Select(s => s.ListId).ToArray();
                        CharacterModelRuntime.Release(scene);
                        Check(!owner.Current && !owner.Prepare(Matrix4.Identity), "retired context cannot resurrect body owner");
                        Check(lists.All(id => !GL.IsList(id)), "context retirement deletes every captured source display list");
                        scene.OnDrawFrame(); Check(BodyItems(scene).Count == 0, "retired owner falls back before source submission");
                    }
                    return new(selection?.SelectedAppearance.SkinSource ?? "native", states.ToArray(), stages.ToArray(), sourceFrames,
                        fallbackFrames, repeats, jumped, morphed, died, respawned, fired, charged, turned, moved,
                        selection?.Generation, pixels.ToArray());

                    void Picture(bool verify)
                    {
                        scene.OnDrawFrame();
                        var items = BodyItems(scene);
                        if (owner != null && (!owner.Ready || player.Health <= 0 || player.IsAltForm || player.IsMorphing || player.IsUnmorphing))
                            Check(items.Count == 0, "native death/form states fall back before any imported segment submits");
                        if (items.Count > 0)
                        {
                            Check(owner != null && owner.Current && items.Count == owner.Model.Segments.Count
                                && items.All(item => item.SourceGx is { } draw && ReferenceEquals(draw.BodyOwner, owner)
                                    && ReferenceEquals(draw.Binding.Owner, scene) && item.WeightedSkinning
                                    && item.MatrixStackCount is > 0 and <= 32 && draw.Seconds == owner.MaterialSeconds),
                                "production world list contains exactly one complete current source body");
                            Check(WorldItems(scene).All(item => item.SourceGx == null), "GX packets never leak into native multipass lists");
                            sourceFrames++;
                        }
                        else if (owner != null) fallbackFrames++;
                        Check(scene.OnRenderFrame(), "real Scene world renderer accepts frame");
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
                        Check(GL.GetError() == ErrorCode.NoError, "actual body/native world frame leaves no GL error");
                        DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
                    }
                }
                finally { if (scene != null) Release(scene); }
            }
        }
        catch (Exception exception)
        {
            errorType = exception.GetType().Name; errorHash = Sha(Encoding.UTF8.GetBytes(exception.Message));
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
            MapAudit.ForceEveryone = oldForce; NetTestScript.Enabled = oldScript;
            GamepadContexts.Focused = focused; GamepadContexts.MenuVisible = menu;
            GamepadContexts.Capturing = capturing; GamepadContexts.TextEntryActive = text;
            PointerInput.StylusMode = stylus; PointerInput.Reset(); PointerDevice.Reset();
            FrameTiming.FrameRateCap = cap; FrameTiming.Reset();
            Console.WriteLine(JsonSerializer.Serialize(new { Pass = errorType == null, Checks = checks,
                Renderer = renderer, GlVersion = glVersion, FailedCheck = failure, ErrorType = errorType, ErrorMessageSha256 = errorHash,
                RvzSha256Before = before, RvzSha256After = after, NativeSha256Before = nativeBefore, NativeSha256After = nativeAfter,
                TemporaryDerivedDataDeleted = cleaned,
                Runs = results.Select(r => new { r.Identity, r.Generation, Frames = r.States.Length,
                    GameplayTraceSha256 = Sha(Encoding.UTF8.GetBytes(string.Join("\n", r.States))), r.Stages,
                    r.SourceFrames, r.NativeFallbackFrames, r.DrawRepeats, r.Jumped, r.Morphed, r.Died, r.Respawned,
                    r.Fired, r.Charged, r.Turned, r.Moved }),
                Scope = "Actual owned offline Windows x64 Samus Scene/input/world GL; direct lethal damage is declared fixture setup; source images never saved",
                Unqualified = new[] { "physical devices", "visual/art judgment from hashes", "online or replay", "two simultaneous body owners",
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
