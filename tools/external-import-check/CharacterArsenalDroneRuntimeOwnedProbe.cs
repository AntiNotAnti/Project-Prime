using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Import;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using MphRead.Mods.Platform;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Focused actual Arsenal ownership integration. Existing solo/full clip oracle
// fixtures stay unchanged. No gameplay owner, camera, native life or emitter is fabricated.
internal static class CharacterArsenalDroneRuntimeOwnedProbe
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> Key = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly Keys[] Used = [Keys.Enter, Keys.B, Keys.F, Keys.Z, Keys.W, Keys.S, Keys.A, Keys.D,
        Keys.Space, Keys.C, Keys.P, Keys.N, Keys.V];
    // Read-only reuse of the already qualified native canonical serializers.
    // No old probe is edited or executed; these methods only snapshot native data.
    private static readonly MethodInfo ArsenalCanonical = typeof(CharacterArsenalRuntimeOwnedProbe).GetMethod("Canonical", Static)!;
    private static readonly MethodInfo DroneCanonical = typeof(CharacterDroneRuntimeOwnedProbe).GetMethod("State", Static)!;
    private static readonly MethodInfo ArsenalState = typeof(CharacterArsenalRuntimeOwnedProbe).GetMethod("State", Static)!;
    private static readonly MethodInfo NativeInventory = typeof(CharacterArsenalRuntimeOwnedProbe).GetMethod("Inventory", Static)!;
    private sealed record Case(LocalBodyAppearance Body, LocalDroneAppearance Drone);
    private sealed class ChoiceStore(LauncherHunterChoice value) : ILauncherHunterChoiceStore
    { internal LauncherHunterChoice Value = value; public LauncherHunterChoice Capture() => Value; public void Save(LauncherHunterChoice value) => Value = value; }

    internal static int Run(string source, bool cpuOnly = false)
    {
        int checks = 0; string? failure = null, error = null; bool sourceIo = false, nativeIo = false, gl = false, rendered = false, cleaned = false;
        string core = typeof(Scene).Assembly.Location, tool = typeof(CharacterArsenalDroneRuntimeOwnedProbe).Assembly.Location;
        string coreHash = Hash(core), toolHash = Hash(tool), cwd = Environment.CurrentDirectory;
        string temp = Path.Combine(Path.GetTempPath(), "prime-arsenal-drone-" + Guid.NewGuid().ToString("N"));
        string? oldData = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA"), native = Environment.GetEnvironmentVariable("PRIME_BODY_TEST_ASSETS");
        string oldFiles = GameFiles.Root, oldPrefs = LauncherPrefs.Directory; byte oldSlot = Menu.SaveSlot;
        var oldBody = LauncherPrefs.LocalBody; var oldDrone = LauncherPrefs.LocalDrone; int oldCap = FrameTiming.FrameRateCap;
        bool oldHd = RenderOptions.CharacterModelReplacements, oldBright = RenderOptions.BrightSkins, oldCosmetic = RenderOptions.ShowCustomCosmetics;
        bool oldForce = MapAudit.ForceEveryone, oldFocus = GamepadContexts.Focused, oldMenu = GamepadContexts.MenuVisible;
        var oldShadow = RenderOptions.Shadows; string? sourceBefore = null, nativeBefore = null; RenderWindow? window = null;
        var cases = new List<object>(); var captures = new List<object>(); var nativeCameras = new Dictionary<string, string>();
        var nativeVisibility = new Dictionary<ulong, string>();
        var nativeGuardStates = new Dictionary<string, string>();
        string? captureRoot = Environment.GetEnvironmentVariable("PRIME_ARSENAL_DRONE_CAPTURE_DIR");
        string[]? baseline = null; var watch = Stopwatch.StartNew();
        void Check(bool value, string label) { if (!value) { failure = label; throw new InvalidDataException(label); } checks++; }
        try
        {
            Check(typeof(LaunchPlan).GetProperty("LocalArsenalDrone")?.PropertyType == typeof(LocalDroneAppearance),
                "explicit Arsenal drone request exists before any owned source or native asset IO");
            Check(OperatingSystem.IsWindows() && !Headless.Active && !NetSession.Active && !DemoPlayback.IsActive && !ReplayCapture.LiveRecordingActive,
                "actual offline Windows graphics process");
            if (!string.IsNullOrWhiteSpace(captureRoot))
            {
                captureRoot = Path.GetFullPath(captureRoot);
                Check(captureRoot.StartsWith(Path.GetFullPath(Path.Combine(cwd, "artifacts", "sh-integration")) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase), "explicit optional captures stay in ignored worktree artifacts");
                Directory.CreateDirectory(captureRoot);
                Check(!Directory.EnumerateFileSystemEntries(captureRoot).Any() && (File.GetAttributes(captureRoot) & FileAttributes.ReparsePoint) == 0,
                    "optional capture directory is fresh and direct");
            }
            else captureRoot = null;
            source = Path.GetFullPath(source); sourceIo = true; sourceBefore = Hash(source);
            Check(sourceBefore == "3fc5ece5ca1badecc1e61a3762294bf81a9f3cf0ed6652eab0838aacd74fe844"
                && native != null && Directory.Exists(native), "exact owned PrimeRev2 and existing AMHE1 sources");
            nativeIo = true; nativeBefore = Inventory(native!);
            string user = Path.Combine(temp, "user"); Directory.CreateDirectory(user);
            File.WriteAllText(Path.Combine(user, "paths.txt"), "0.19.0.0\n" + Ver.AMHE1 + "=" + native + "\nExport=" + Path.Combine(user, "Export") + "\n");
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", user); Directory.SetCurrentDirectory(user);
            GameFiles.Root = LauncherPrefs.Directory = user; Menu.SaveSlot = 0; ConsoleSetup.Run(); Paths.UpdatePaths(); Paths.MphKey = Ver.AMHE1;
            GraphicsBackendPolicy.Configure("opengl"); RenderOptions.CharacterModelReplacements = false; RenderOptions.BrightSkins = false;
            RenderOptions.ShowCustomCosmetics = true; RenderOptions.Shadows = ShadowQuality.Off; MapAudit.ForceEveryone = true;
            GamepadManager.ClearAll(); GamepadContexts.Focused = true; GamepadContexts.MenuVisible = false;
            string imports = RetroCharacterPreviewExporter.DefaultRoot;
            Check(Path.GetFullPath(imports).StartsWith(Path.GetFullPath(temp) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "isolated import root");
            RetroCharacterPreviewExporter.ExportFromOwnedGame(source, selectedIds: ["trooper-power", "trooper-drone-alpha", "trooper-drone-gamma"]);
            foreach (string id in new[] { "trooper-drone-alpha", "trooper-drone-gamma" })
            {
                var selection = CharacterSourceArsenalDroneSelection.Load(id, CancellationToken.None, imports);
                var state = LocalImportBundle.Inspect(imports, id, RetroCharacterPreviewExporter.BundleSpec(id, ExternalSourceEdition.PrimeUsaRevision2));
                Check(state.State == ImportBundleState.Ready && state.Files.Count == 12 && state.Receipt!.Generation == selection.Source.Generation,
                    "Arsenal factory retains complete existing twelve-file independent drone generation");
                using var cancel = new CancellationTokenSource(); cancel.Cancel(); bool refused = false;
                try { CharacterSourceArsenalDroneSelection.Load(id, cancel.Token, imports); } catch (OperationCanceledException) { refused = true; }
                Check(refused, "canceled Arsenal drone capture cannot publish a partial capability");
            }
            CharacterArsenalDroneLaunchOwnedChecks.RunCpu(imports, Check);
            if (!cpuOnly)
            {
            var settings = new MenuSettings { TeamPlay = "off", ThirdPersonCamera = "on", SpawnProtection = "off", MovementModel = "Native" };
            window = new RenderWindow(shell: true); window.ClientSize = new(640, 480); window.Context.MakeCurrent(); GLFW.SwapInterval(0); gl = true;
            foreach (var choice in new[] { new Case(LocalBodyAppearance.Native, LocalDroneAppearance.Native),
                new Case(LocalBodyAppearance.Native, LocalDroneAppearance.Alpha), new Case(LocalBodyAppearance.Native, LocalDroneAppearance.Gamma),
                new Case(LocalBodyAppearance.TrooperPower, LocalDroneAppearance.Alpha), new Case(LocalBodyAppearance.TrooperPower, LocalDroneAppearance.Gamma) })
            {
                FrameTiming.Reset(); FrameTiming.ResetDiagnostics(); FrameTiming.FrameRateCap = 60; PointerInput.Reset(); PointerDevice.Reset();
                Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
                var store = new ChoiceStore(new(Hunter.Weavel, 0));
                using (var draft = new LauncherHunterChoiceController(store))
                    Check(draft.SelectBody((int)choice.Body) && draft.SelectDrone((int)choice.Drone) && draft.Apply(), "Hunters applies independent existing Body/Drone choices");
                LauncherPrefs.LocalBody = store.Value.Body; LauncherPrefs.LocalDrone = store.Value.Drone;
                var plan = OfflineLaunch.Preview(settings, "MP3 PROVING GROUND", GameMode.Battle, Hunter.Weavel, 0, 1, 0, OfflineCharacterMode.TrooperArsenalPractice);
                Check(plan.LocalArsenalBody == choice.Body && plan.LocalArsenalDrone == choice.Drone && plan.LocalBody == LocalBodyAppearance.Native
                    && plan.LocalDrone == LocalDroneAppearance.Native && LocalArsenalDroneRules.TryAdmit(plan, out _), "explicit Arsenal captures both independent requests");
                Check(MatchStart.Begin(window, settings, plan), "real Arsenal MatchStart: " + MatchStart.LastError);
                var scene = window.Scene; var player = scene.Players.Main; var drone = player.ModSourceArsenalDrone; var body = player.ModSourceArsenalBody; var blaster = scene.PirateBlaster!;
                // Match the existing Arsenal fixture's declared pre-Spawn seed
                // boundary; source asset initialization may consume load RNG.
                scene.Random.SetRng1(123456); scene.Random.SetRng2(654321);
                Check(player.Initialized && !player.LoadFlags.TestFlag(LoadFlags.Spawned) && !blaster.Available(player) && !blaster.Owner.IsValid,
                    "real load barrier precedes genuine native Spawn and weapon life");
                Check(player.ModCharacterGameplay == CharacterGameplayRules.TrooperArsenalV1 && player.ModSourceDrone == null
                    && scene.Players.PlayerCount == 2 && scene.Players.Items[1].IsBot && scene.Players.Items[1].ModCharacterGameplay == CharacterGameplayRules.Native,
                    "actual native Arsenal actor and one unmodified native bot own gameplay");
                Check(choice.Drone == LocalDroneAppearance.Native ? drone == null : drone != null && !drone.Current && !drone.Ready
                    && drone.Model.Joints.Count == 33 && drone.Model.IndexCount == 1390 * 3 && drone.Model.Segments.Count == 2
                    && drone.Pose.Clips.Keys.SequenceEqual(new[] { 13, 75, 73, 74, 29, 4, 3, 2, 5, 6, 1, 18 }), "complete closed twelve-clip drone residents bind structurally before Spawn");
                Check(choice.Body == LocalBodyAppearance.Native ? body == null : body != null && !body.Current && body.Model.IndexCount == 1970 * 3
                    && body.CannonModel.IndexCount == 206 * 3, "independent body/cannon residents bind without borrowing drone generation");
                var input = typeof(Scene).GetField("_inputMode", Hidden)!; input.SetValue(scene, Enum.Parse(input.FieldType, "All"));
                scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000; Configure(player); Clear(window);
                Key(window.KeyboardState, Keys.Enter, true);
                for (int n = 0; n < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); n++) scene.OnSimulationFrame();
                Clear(window); scene.CameraSequences.Current?.End(); for (int n = 0; n < 240; n++) scene.OnSimulationFrame(); scene.CameraSequences.Current?.End();
                Check(player.Health > 0 && !player.IsAltForm && blaster.Available(player) && blaster.Selected(player) && blaster.Owner.IsValid,
                    "genuine Shoot spawn establishes native Arsenal weapon life");
                // Common boundary after normal load/first effect preparation, no per-step reseeding.
                scene.OnDrawFrame(); Check(scene.OnRenderFrame(), "initial actual native-camera render"); scene.AfterRenderFrame();
                Rng.SetRng1(123456); Rng.SetRng2(654321); scene.Random.SetRng1(123456); scene.Random.SetRng2(654321);
                var result = Trace(window, scene, player, drone, body, imports, Check, watch, () => rendered = true,
                    choice, captureRoot, captures, nativeCameras, nativeVisibility, nativeGuardStates, baseline);
                if (baseline == null) baseline = result.Rows;
                else { int mismatch = Enumerable.Range(0, Math.Min(baseline.Length, result.Rows.Length)).FirstOrDefault(n => baseline[n] != result.Rows[n], -1);
                    Check(result.Rows.Length == baseline.Length && mismatch < 0, "full native actor/bot/Blaster/grounded body/drone beams/laser/RNG parity; first mismatch=" + mismatch); }
                cases.Add(new { Body = choice.Body.ToString(), Drone = choice.Drone.ToString(), Frames = result.Rows.Length,
                    TraceSha256 = Sha(Encoding.UTF8.GetBytes(string.Join("\n", result.Rows))), BodyGeneration = body?.Generation, DroneGeneration = drone?.Generation, result.Metadata });
                window.EndScene(); Check(drone == null || !drone.Current && !drone.Ready, "EndScene permanently retires drone capability");
                Check(body == null || !body.Current && !body.CannonCurrent, "EndScene permanently retires independent body/viewmodel capability");
                Console.WriteLine(JsonSerializer.Serialize(new { Probe = "arsenal-drone-case", Body = choice.Body.ToString(), Drone = choice.Drone.ToString(), Pass = true }));
            }
            LauncherPrefs.LocalBody = LocalBodyAppearance.TrooperPower; LauncherPrefs.LocalDrone = LocalDroneAppearance.Alpha;
            var lifecycle = OfflineLaunch.Preview(settings, "MP3 PROVING GROUND", GameMode.Battle, Hunter.Weavel, 0, 1, 0, OfflineCharacterMode.TrooperArsenalPractice);
            CharacterArsenalDroneLaunchOwnedChecks.RunLaunch(window, settings, lifecycle, imports, Check);
            }
        }
        catch (Exception ex) { error = ex.GetType().Name; Console.Error.WriteLine("OWNED ARSENAL DRONE failure: " + ex); }
        finally
        {
            try
            {
                if (window != null) { Clear(window); if (window.HasScene) window.EndScene(); window.Dispose(); }
                Directory.SetCurrentDirectory(cwd); GameFiles.Root = oldFiles; LauncherPrefs.Directory = oldPrefs; Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", oldData);
                LauncherPrefs.LocalBody = oldBody; LauncherPrefs.LocalDrone = oldDrone; Menu.SaveSlot = oldSlot; FrameTiming.Reset(); FrameTiming.ResetDiagnostics(); FrameTiming.FrameRateCap = oldCap;
                RenderOptions.CharacterModelReplacements = oldHd; RenderOptions.BrightSkins = oldBright; RenderOptions.ShowCustomCosmetics = oldCosmetic; RenderOptions.Shadows = oldShadow;
                MapAudit.ForceEveryone = oldForce; GamepadContexts.Focused = oldFocus; GamepadContexts.MenuVisible = oldMenu;
                if (sourceBefore != null) Check(Hash(source) == sourceBefore, "owned RVZ unchanged"); if (nativeBefore != null) Check(Inventory(native!) == nativeBefore, "native source inventory unchanged");
                string safe = Path.GetFullPath(temp); Check(safe.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(safe).StartsWith("prime-arsenal-drone-", StringComparison.Ordinal), "cleanup stays inside the explicit disposable fixture root");
                if (Directory.Exists(safe)) Directory.Delete(safe, true); cleaned = !Directory.Exists(safe);
                Check(Hash(core) == coreHash && Hash(tool) == toolHash, "core/tool unchanged during probe");
            }
            catch (Exception ex) { error ??= ex.GetType().Name; Console.Error.WriteLine("ARSENAL DRONE cleanup: " + ex); }
            Console.WriteLine(JsonSerializer.Serialize(new { Pass = error == null, Checks = checks, FailedCheck = failure, ErrorType = error,
                CoreSha256 = coreHash, ToolSha256 = toolHash, SourceIoAttempted = sourceIo, NativeIoAttempted = nativeIo, GraphicsContextActivated = gl,
                RenderingActivated = rendered, SourceSha256Before = sourceBefore, NativeInventoryBefore = nativeBefore, TemporaryRoot = temp, TemporaryDerivedDataDeleted = cleaned, Cases = cases, Captures = captures,
                Scope = cpuOnly ? "CPU-only closed owned capture/cancel/pins; no Scene or graphics context." : "Actual Hunters Apply and MatchStart; one native bot; native input Spawn/cycle/split/camera handoff/motion/burst/laser/hit/merge; declared public grounded Die and player lethal damage; native respawn; repeated GL draw/gathers and pair/context retirement. Captures only through explicit environment hook.",
                Unqualified = new[] { "all four laser patterns", "all six directional clips in actual input", "positive opponent hit/kill/mark authority", "failed second-owner allocation injection", "visual/art fidelity", "physical inputs/native UI clicks", "network/replay", "Android/macOS" } }));
        }
        return error == null ? 0 : 1;
    }

    private sealed record Result(string[] Rows, object Metadata);
    private static Result Trace(RenderWindow window, Scene scene, PlayerEntity player, CharacterSourceArsenalDroneOwner? owner,
        CharacterSourceArsenalBodyOwner? body, string imports, Action<bool, string> check, Stopwatch watch, Action rendered,
        Case choice, string? captureRoot, List<object> captures, Dictionary<string, string> nativeCameras, Dictionary<ulong, string> nativeVisibility,
        Dictionary<string, string> nativeGuardStates, string[]? baselineRows)
    {
        var blaster = scene.PirateBlaster!; var rows = new List<string>(); var ordinals = new HashSet<int>(); var stages = new List<object>();
        int sourcePictures = 0, nativePictures = 0, repeats = 0, cameraFallback = 0, flashPictures = 0, hitPictures = 0, burstPictures = 0, laserPictures = 0, gathers = 0, gpuGathers = 0;
        int sourceBodyPictures = 0, sourceCannonPictures = 0;
        var readbacks = new Dictionary<int, string>(); var inspectedPrograms = new HashSet<string>();
        bool split = false, merged = false, moved = false, turretDied = false, died = false, respawned = false; Vector3 start = player.Position;
        using var locks = new SourceLocks(imports); int reads = CharacterSourceClip.CapturedFileReads;
        check(locks.Count >= 37, "all independent body and two drone generation components are exclusively locked through script");
        var initialLife = blaster.Owner;
        scene.GameState.ThirdPersonCamera = false; Run("first-person-settle", 60);
        Run("biped", 20); Run("private-fire", 41, Keys.Enter); Run("native-select", 2, Keys.P, edge: true);
        check(!blaster.Selected(player), "native Power Beam key deselects only Blaster viewmodel authority");
        Run("native-fire", 12, Keys.Enter); Run("custom-reselect", 2, Keys.C, edge: true); check(blaster.Selected(player), "custom key reselects actual Blaster");
        Run("next-native", 2, Keys.N, edge: true); check(!blaster.Selected(player), "native Next cycles away");
        Run("prev-custom", 2, Keys.V, edge: true); check(blaster.Selected(player), "native Prev restores custom owner selection");
        // Deliberately deselect before split: drone currentness follows Available, not Selected.
        Run("native-before-split", 2, Keys.P, edge: true); check(!blaster.Selected(player), "real native choice is retained before split");
        scene.GameState.ThirdPersonCamera = true; Run("third-person-settle", 60);
        Run("split", 91, Keys.B, edge: true); split = player.IsAltForm;
        check(split && blaster.Available(player) && (owner == null || owner.Current), "real alternate form retains native Arsenal life while Blaster is unselected");
        Run("flight", 60, Keys.W); Run("hover", 75, Keys.Space); Run("descent", 45);
        Run("burst", 220, Keys.F); Run("burst-rest", 130); check(player.DroneShots > 0, "genuine AltAttack produces native drone beams");
        Run("laser", 135, Keys.Z, edge: true); Run("laser-rest", 140); check(player.DroneLasers > 0, "genuine Zoom edge produces native cached laser");
        int wait = 0; while (Timer("_damageInvulnTimer") > 0 || Timer("_spawnInvulnTimer") > 0 || player.ModMatchSpawnProtectionActive)
        { Step("ordinary-damage-wait"); check(++wait <= 600, "native damage protection drains without fabricated timer writes"); }
        int health = player.Health; player.TakeDamage(1, DamageFlags.None, null, null);
        check(player.Health == health - 1 && Timer("_timeSinceDamage") == 0, "public ordinary damage establishes native flash/hit age");
        Picture("ordinary-damage-age0"); Append("ordinary-damage:" + Canonical(scene, player)); Run("hit-tail", 80);
        check(player.DroneBody.Health > 0 && player.Flags2.TestFlag(PlayerFlags2.Halfturret), "genuine split owns a living separate planted body before declared death");
        health = player.Health; var life = blaster.Owner; player.DroneBody.Die(); turretDied = player.DroneBody.Health == 0;
        check(turretDied && player.Health == health && blaster.Owner == life && blaster.Available(player)
            && (owner == null || owner.Current), "declared public grounded-body Die cannot invent or retire the flying player's native life");
        Picture("planted-body-death"); Append("planted-body-death:" + Canonical(scene, player)); Run("body-dead-flight", 30, Keys.W);
        Run("merge", 91, Keys.B, edge: true); merged = !player.IsAltForm;
        check(merged && blaster.Available(player), "native merge after planted body death retains actual living actor");
        Run("reselect-after-merge", 2, Keys.C, edge: true); Run("post-merge", 50);
        var priorLife = blaster.Owner; player.TakeDamage(10000, DamageFlags.Death | DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln | DamageFlags.NoSfx, null, null);
        died = player.Health == 0; check(died && !blaster.Available(player) && (owner == null || !owner.Current), "declared public player death retires drone native-life currentness immediately");
        Picture("player-death"); Append("player-death:" + Canonical(scene, player));
        for (int n = 0; n < 420; n++) { Clear(window); if (n % 30 == 0) Key(window.KeyboardState, Keys.Enter, true); Step("respawn"); }
        Clear(window); respawned = player.Health > 0 && blaster.Available(player) && blaster.Owner != priorLife;
        check(respawned && blaster.Owner.Life != priorLife.Life && (owner == null || owner.Current), "genuine native Shoot respawn creates new semantic life and source history");
        Run("new-life-split", 91, Keys.B, edge: true); Run("new-life-hover", 45, Keys.Space);
        check(moved && split && merged && turretDied && died && respawned && (body == null || sourceBodyPictures > 0 && sourceCannonPictures > 0)
            && (owner == null || sourcePictures > 0 && cameraFallback > 0
            && flashPictures > 0 && hitPictures > 0 && burstPictures > 0 && laserPictures > 0 && gathers > 0), "actual script observes all declared source/native ownership boundaries");
        Guards();
        if (owner != null)
        {
            foreach (int rate in new[] { 90, 120, 240, 540 })
            {
                FrameTiming.Reset(); FrameTiming.FrameRateCap = rate <= FrameTiming.MaxCap ? rate : FrameTiming.Unlimited;
                int steps = 0, zero = 0, fractional = 0; long totalBefore = FrameTiming.TotalSteps;
                long dropsBefore = FrameTiming.DroppedSteps, stallsBefore = FrameTiming.Stalls;
                for (int n = 0; n < rate; n++) { int completed = FrameTiming.Advance(1d / rate); steps += completed; if (completed == 0) zero++;
                    if (FrameTiming.PresentationAlpha > 0 && FrameTiming.PresentationAlpha < 1) fractional++;
                    for (int k = 0; k < completed; k++) Step("fractional", draw: false, record: false, reset: false); Picture("fractional-" + rate); }
                // Independent IEEE-double accumulation of 540*(1/540) leaves
                // the endpoint one rounding quantum below the sixtieth step.
                var expected = rate switch { 90 => (60, 30), 120 => (60, 60), 240 => (60, 180), 540 => (59, 481), _ => throw new InvalidDataException() };
                check(steps == expected.Item1 && zero == expected.Item2 && FrameTiming.TotalSteps - totalBefore == steps && fractional > 0
                    && FrameTiming.DroppedSteps == dropsBefore && FrameTiming.Stalls == stallsBefore,
                    "simulated schedule has exact independently calculated completed/zero steps and fractional pictures");
                stages.Add(new { Rate = rate, Pictures = rate, CompletedSteps = steps, ZeroStepPictures = zero, FractionalPictures = fractional,
                    DroppedSteps = FrameTiming.DroppedSteps - dropsBefore, Stalls = FrameTiming.Stalls - stallsBefore });
            }
            FrameTiming.Reset(); FrameTiming.FrameRateCap = 60;
            var sourcePackets = Items(scene).Select(i => i.SourceGx).ToArray(); int[] lists = owner.Model.Segments.Select(s => s.ListId)
                .Concat(body == null ? [] : body.Model.Segments.Concat(body.CannonModel.Segments).Select(s => s.ListId)).ToArray();
            string nativeBefore = Canonical(scene, player), sourceBefore = SourceState(); CharacterModelRuntime.Release(scene);
            check(!owner.Current && (body == null || !body.Current && !body.CannonCurrent) && sourcePackets.All(p => !p!.ActorCurrent)
                && lists.All(id => !GL.IsList(id)), "context release retires all pair residents and saved packets without changing native life");
            Picture("context-retired"); check(Items(scene).Count == 0 && Canonical(scene, player) == nativeBefore && SourceState() == sourceBefore,
                "retired source cannot reload/resurrect through draw");
        }
        check(CharacterSourceClip.CapturedFileReads == reads, "exclusive component locks and clip-read counter prove no source reload through steps/draws");
        return new(rows.ToArray(), new { SourcePictures = sourcePictures, NativePictures = nativePictures, SourceBodyPictures = sourceBodyPictures,
            SourceCannonPictures = sourceCannonPictures, RepeatedDraws = repeats,
            NativeCameraHandoffFallbackPictures = cameraFallback, NativeFlashPictures = flashPictures, SourceHitPictures = hitPictures,
            SourceBurstPictures = burstPictures, SourceLaserPictures = laserPictures, Full33CpuGatherChecks = gathers, ActualGpuGatherChecks = gpuGathers,
            SceneTargetHashesBySourceOrdinal = readbacks,
            ObservedOrdinals = ordinals.Order().ToArray(), Split = split, Merged = merged, GroundedBodyDied = turretDied, PlayerDied = died, GenuineRespawn = respawned,
            NativeGuardFullCanonical = nativeGuardStates.ToDictionary(p => p.Key, p => p.Value),
            NativePauseFullCanonicalChanged = nativeGuardStates["before-pause"] != nativeGuardStates["after-pause"],
            InitialNativeWeaponLife = initialLife.ToString(), Stages = stages, NativeBots = 1,
            TraceGrain = "Matched completed steps after repeated pictures plus labeled public damage/grounded Die events; two existing full native serializers, including actor/bot/Blaster/effects/AI/RNG and planted body/drone beam/laser state. Guards/fractional/context outside trace." });

        int Timer(string field) => (ushort)typeof(PlayerEntity).GetField(field, Hidden)!.GetValue(player)!;
        void Run(string label, int count, Keys key = Keys.Unknown, bool edge = false)
        { for (int n = 0; n < count; n++) { Clear(window); if (key != Keys.Unknown && (!edge || n == 0)) Key(window.KeyboardState, key, true); Step(label);
            if (captureRoot != null && label == "hover" && n == 45) Capture(); } Clear(window); }
        void Capture()
        {
            var handoff = typeof(Scene).GetField("_thirdPersonFormHandoff", Hidden)!.GetValue(scene)!;
            object? H(string field) => handoff.GetType().GetField(field, Hidden)!.GetValue(handoff);
            float[] MatrixValues(Matrix4 matrix) { var v = new float[16]; for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) v[r*4+c] = matrix[r,c]; return v; }
            Vector3 eye = scene.ViewMatrix.Inverted().Row3.Xyz;
            check(!(bool)H("_blending")! && Timer("_camSwitchTimer") >= player.Values.CamSwitchTime * 2
                && !player.EquipInfo.Zoomed && player.CameraInfo.Fov == Fixed.ToFloat(player.Values.NormalFov) * 2,
                "optional flying picture uses settled actual native camera with normal FOV and no Roam diagnostic");
            string camera = JsonSerializer.Serialize(new { View = MatrixValues(scene.ViewMatrix), Projection = MatrixValues(scene.PerspectiveMatrix),
                Eye = new[] { eye.X, eye.Y, eye.Z }, Fov = player.CameraInfo.Fov, Type = player.CameraType.ToString(), player.EquipInfo.Zoomed,
                CameraSwitchStep = Timer("_camSwitchTimer"), Initialized = H("_initialized"), Blending = H("_blending"), StartTick = H("_startTick"), LastTick = H("_lastTick") });
            string cameraHash = Sha(Encoding.UTF8.GetBytes(camera));
            if (choice.Body == LocalBodyAppearance.Native && choice.Drone == LocalDroneAppearance.Native) nativeCameras.Add("hover45", cameraHash);
            else check(nativeCameras.TryGetValue("hover45", out string? expected) && cameraHash == expected,
                "optional source picture has exactly the Native reference view/projection/eye/FOV/handoff state");
            string before = Canonical(scene, player), raw = SourceState();
            string path = Path.Combine(captureRoot!, "arsenal-drone-" + choice.Body.ToString().ToLowerInvariant() + "-" + choice.Drone.ToString().ToLowerInvariant() + "-hover45.png");
            check(!File.Exists(path) && ScreenCapture.SaveWindow(scene.Size.X, scene.Size.Y, path), "explicit optional current native-camera picture saved");
            check(Canonical(scene, player) == before && SourceState() == raw, "optional capture cannot mutate native state/RNG/source pose or clocks");
            captures.Add(new { Body = choice.Body.ToString(), Drone = choice.Drone.ToString(), scene.FrameCount, SourceOrdinal = owner?.Pose.CurrentOrdinal,
                NativeCamera = JsonSerializer.Deserialize<JsonElement>(camera), CameraSha256 = cameraHash, Path = path, Sha256 = Hash(path), DeleteAfterVisualReview = true });
        }
        void Step(string label, bool draw = true, bool record = true, bool reset = true)
        {
            if (reset) FrameTiming.Reset(); double clock = owner?.MaterialSeconds ?? 0;
            long previousLife = owner == null ? -1 : (long)typeof(CharacterSourceArsenalDroneOwner).GetField("_life", Hidden)!.GetValue(owner)!;
            var previousWeapon = owner == null ? default : (WeaponOwnerContext)typeof(CharacterSourceArsenalDroneOwner).GetField("_weaponLife", Hidden)!.GetValue(owner)!;
            scene.OnSimulationFrame(); moved |= (player.Position - start).Length > 1;
            if (owner != null)
            {
                bool supported = (bool)typeof(PlayerEntity).GetProperty("SourceDroneStateSupported", Hidden)!.GetValue(player)!;
                long lifeNow = (long)typeof(PlayerEntity).GetField("_sourceBodyLife", Hidden)!.GetValue(player)!;
                if (owner.Current && (previousLife != lifeNow || previousWeapon != blaster.Owner)) clock = 0;
                bool ready = owner.Current && supported;
                check(owner.Ready == ready && Math.Abs(owner.MaterialSeconds - clock - (ready ? 1 / 60d : 0)) < 1e-12,
                    "only admitted completed native steps advance drone clock; actual life resets it");
            }
            if (draw) Picture(label); if (record) Append(Canonical(scene, player));
        }
        void Append(string row)
        {
            if (baselineRows != null && (rows.Count >= baselineRows.Length || baselineRows[rows.Count] != row))
            {
                Console.WriteLine(JsonSerializer.Serialize(new { Probe = "arsenal-drone-first-native-mismatch", Row = rows.Count,
                    scene.FrameCount, CurrentVisibility = Visibility(scene, player), NativeControlFrame = nativeVisibility.GetValueOrDefault(scene.FrameCount),
                    NativeHash = rows.Count < baselineRows.Length ? baselineRows[rows.Count] : null, SourceHash = row }));
                check(false, "full canonical native trace first differs at completed row=" + rows.Count);
            }
            rows.Add(row);
        }
        string SourceState() => (owner == null ? "" : owner.CompletedSteps + ":" + Raw(owner.Model) + ":" + Pose(owner.Pose)
            + ":" + owner.Motion.CurrentOrdinal + ":" + owner.Motion.PendingOrdinal + ":" + owner.Motion.PendingSteps)
            + (body == null ? "" : ":" + body.MaterialSeconds + ":" + body.CannonMaterialSeconds + ":" + Raw(body.Model) + Raw(body.CannonModel));
        void Picture(string label)
        {
            check(watch.Elapsed.TotalMinutes < 12, "bounded Arsenal drone runtime fixture"); string sourceBefore = SourceState();
            Draw(); string before = Canonical(scene, player); Draw(); repeats++;
            check(Canonical(scene, player) == before && SourceState() == sourceBefore, "repeated actual render cannot change native actors/AI/weapon/effects/drone/RNG or raw source clocks/palettes: " + label);
            void Draw()
            {
                scene.OnDrawFrame(); var items = Items(scene); var altItems = items.Where(i => i.SourceGx?.AlternateForm == true).ToArray();
                if (choice.Body == LocalBodyAppearance.Native && choice.Drone == LocalDroneAppearance.Native)
                    nativeVisibility[scene.FrameCount] = Visibility(scene, player);
                check(altItems.Length is 0 or 2 && items.Count(i => i.ViewModel) is 0 or 3 && items.Count(i => !i.ViewModel && i.SourceGx?.AlternateForm != true) is 0 or 3,
                    "every independently owned part is complete or entirely native before render");
                if (items.Count(i => i.ViewModel) == 3) { check(body != null && body.CannonCurrent, "only actual selected native Blaster owns source cannon packets"); sourceCannonPictures++; }
                if (items.Count(i => !i.ViewModel && i.SourceGx?.AlternateForm != true) == 3) { check(body != null && body.Current, "only independent current Arsenal actor owns source body packets"); sourceBodyPictures++; }
                if (player.IsAltForm || player.IsMorphing || player.IsUnmorphing)
                    check(items.All(i => i.SourceGx?.AlternateForm == true), "native alternate/morph cannot submit Arsenal biped or cannon packets");
                if (altItems.Length > 0)
                {
                    check(owner != null && owner.Ready && owner.Current && altItems.All(i => ReferenceEquals(i.SourceGx!.BodyOwner, owner))
                        && NativeDronePackets() == 0, "complete source drone owns exact native actor; no duplicate native drone mesh");
                    CheckGathers(owner!, altItems, check); gathers++; sourcePictures++; ordinals.Add(owner!.Pose.CurrentOrdinal);
                    if (player.DroneLaserFrame >= 0) { check(owner.Pose.CurrentOrdinal == 29, "native laser has highest source pose priority"); laserPictures++; }
                    else if (Timer("_timeSinceDamage") is >= 4 and < 72) { check(owner.Pose.CurrentOrdinal == 18 && owner.Pose.ClipSeconds == Timer("_timeSinceDamage") / 60d, "native eligible hit age is absolute without plus-one"); hitPictures++; }
                    else if (player.DroneFireState != 0) burstPictures++;
                }
                else if (player.IsAltForm && !player.IsMorphing && !player.IsUnmorphing && player.Health > 0
                    && !scene.GameState.MenuPause && !scene.GameState.DialogPause
                    && !player.Flags2.TestFlag(PlayerFlags2.HideModel) && (float)typeof(PlayerEntity).GetField("_curAlpha", Hidden)!.GetValue(player)! > 0)
                {
                    if (NativeDronePackets() == 0) Console.WriteLine(JsonSerializer.Serialize(new { Probe = "arsenal-drone-missing-packet-diagnostic", Label = label,
                        scene.FrameCount, player.Health, Position = new[] { player.Position.X, player.Position.Y, player.Position.Z }, Flags1 = (uint)player.Flags1, Flags2 = (uint)player.Flags2, player.CameraType, player.CameraInfo.Fov,
                        CameraStep = Timer("_camSwitchTimer"), DamageAge = Timer("_timeSinceDamage"), player.DroneLaserFrame,
                        NativeAlpha = typeof(PlayerEntity).GetField("_curAlpha", Hidden)!.GetValue(player), OwnerCurrent = owner?.Current, OwnerReady = owner?.Ready,
                        SourceSupported = typeof(PlayerEntity).GetProperty("SourceDroneStateSupported", Hidden)!.GetValue(player), SourceItems = items.Count,
                        WorldItems = WorldItems(scene).Count(), NativeModel = player.ModSourceNativeDrone!.Name,
                        NativeListIds = player.ModSourceNativeDrone.Meshes.Select(m => m.ListId).ToArray(),
                        MatchingWorldItems = WorldItems(scene).Where(i => player.ModSourceNativeDrone.Meshes.Any(m => m.ListId == i.ListId))
                            .Select(i => new { i.Type, i.ListId, i.Alpha, i.ViewModel, HasSourceGx = i.SourceGx != null }).ToArray(),
                        OtherWorldTypes = WorldItems(scene).GroupBy(i => i.Type).Select(g => new { Type = g.Key.ToString(), Count = g.Count() }).ToArray(),
                        NativeControlFrame = nativeVisibility.GetValueOrDefault(scene.FrameCount), CurrentVisibility = Visibility(scene, player),
                        SourcePacketOwners = items.Select(i => new { Part = i.SourceGx?.Part.ToString(), Current = i.SourceGx?.ActorCurrent }).ToArray() }));
                    check(NativeDronePackets() > 0, "unsupported/Native appearance retains actual native drone mesh"); nativePictures++;
                    if (Timer("_camSwitchTimer") < player.Values.CamSwitchTime * 2) cameraFallback++;
                    if (Timer("_timeSinceDamage") < 4) { check(WorldItems(scene).Where(i => player.ModSourceNativeDrone!.Meshes.Any(m => m.ListId == i.ListId))
                        .All(i => i.PaletteOverride == Metadata.RedPalette), "native flash retains actual red palette cue"); flashPictures++; }
                }
                if (player.Health == 0 || player.Flags2.TestFlag(PlayerFlags2.HideModel))
                    check(altItems.Length == 0, "native dead/hidden actor cannot submit source drone packets or invent visible native geometry");
                check(scene.OnRenderFrame() && GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError, "actual source/native world render has no GL errors: " + label);
                if (altItems.Length > 0)
                {
                    foreach (var group in owner!.Model.Segments.GroupBy(s => s.SourceGx!.ProgramId))
                    {
                        string phase = owner.Pose.CurrentOrdinal + ":" + group.Key;
                        if (!inspectedPrograms.Add(phase)) continue;
                        // Final batch for a shared program is the actual retained uniform value.
                        var segment = group.Last();
                        for (int n = 0; n < segment.SourceJointPalette!.Count; n++)
                        {
                            int location = GL.GetUniformLocation(group.Key, "gx_skin[" + n + "]"); var actual = new float[16];
                            check(location >= 0, "actual source GPU skin uniform is active"); GL.GetUniform(group.Key, location, actual);
                            check(actual.SequenceEqual(segment.SourcePresentationPalette!.Skip(n * 16).Take(16)), "actual GPU skin equals exact final physical source batch palette");
                        }
                        gpuGathers++;
                    }
                    if (!readbacks.ContainsKey(owner.Pose.CurrentOrdinal))
                    {
                        byte[] pixels = scene.ReadSceneTarget(out int width, out int height) ?? throw new InvalidDataException("No actual scene target.");
                        check(width == scene.Size.X && height == scene.Size.Y && pixels.Length == width * height * 3,
                            "actual source world scene target can be read in memory without media output");
                        readbacks.Add(owner.Pose.CurrentOrdinal, Sha(pixels));
                    }
                }
                rendered(); scene.AfterRenderFrame();
            }
        }
        int NativeDronePackets() => WorldItems(scene).Count(i => i.Type == RenderItemType.Mesh && player.ModSourceNativeDrone!.Meshes.Any(m => m.ListId == i.ListId));
        void Guards()
        {
            string state = StableGameplay(), raw = SourceState(); var shadows = RenderOptions.Shadows; bool cosmetics = RenderOptions.ShowCustomCosmetics;
            var pauseColumns = NativeColumns(scene, player); string pauseVisibility = Visibility(scene, player);
            Reference("before-pause");
            scene.GameState.PauseMenu(); try { for (int n = 0; n < 3; n++) { scene.OnSimulationFrame(); Picture("pause"); } } finally { scene.GameState.UnpauseMenu(); }
            if (StableGameplay() != state || SourceState() != raw)
            {
                var after = NativeColumns(scene, player); var differences = Enumerable.Range(0, Math.Min(pauseColumns.Bytes.Length, after.Bytes.Length))
                    .Where(n => pauseColumns.Bytes[n] != after.Bytes[n]).ToArray();
                Console.WriteLine(JsonSerializer.Serialize(new { Probe = "arsenal-drone-pause-diagnostic", BeforeVisibility = pauseVisibility, AfterVisibility = Visibility(scene, player),
                    SourceBefore = raw, SourceAfter = SourceState(), BeforeBytes = pauseColumns.Bytes.Length, AfterBytes = after.Bytes.Length,
                    Sections = differences.Select(n => pauseColumns.Marks.First(m => m.End > n).Name).Distinct().ToArray(),
                    Differences = differences.Take(64).Select(n => new { Offset = n, Before = pauseColumns.Bytes[n], After = after.Bytes[n] }).ToArray() }));
            }
            check(StableGameplay() == state && SourceState() == raw, "paused scene preserves native actor/drone/weapon/aim/beam/RNG and source completed-step state");
            Reference("after-pause");
            try { RenderOptions.ShowCustomCosmetics = false; Picture("cosmetics-off"); check(Items(scene).Count == 0, "cosmetics-off refuses all source parts"); Reference("cosmetics-off");
                RenderOptions.ShowCustomCosmetics = cosmetics; RenderOptions.Shadows = ShadowQuality.Low; Picture("shadow-fallback"); check(Items(scene).Count == 0, "shadow mode falls back before partial source submission"); Reference("shadow-fallback"); }
            finally { RenderOptions.ShowCustomCosmetics = cosmetics; RenderOptions.Shadows = shadows; Picture("guards-restored"); }
            check(StableGameplay() == state && SourceState() == raw, "picture-only source guards restore exact native gameplay/source state");
            Reference("guards-restored");
            string StableGameplay() => (string)ArsenalState.Invoke(null, [scene, player, blaster])! + ":" + (string)DroneCanonical.Invoke(null, [scene, player])!;
            void Reference(string phase)
            {
                string full = Canonical(scene, player);
                if (choice.Body == LocalBodyAppearance.Native && choice.Drone == LocalDroneAppearance.Native) nativeGuardStates.Add(phase, full);
                else check(nativeGuardStates.TryGetValue(phase, out string? expected) && full == expected,
                    "same Native guard sequence preserves exact full canonical AI/presentation cache behavior: " + phase);
            }
        }
    }

    private static void Configure(PlayerEntity player)
    {
        foreach (var b in player.Controls.All) { b.Type = ButtonType.Key; b.Key = Keys.Unknown; }
        player.Controls.Shoot.Key = Keys.Enter; player.Controls.Morph.Key = Keys.B; player.Controls.AltAttack.Key = Keys.F; player.Controls.Zoom.Key = Keys.Z;
        player.Controls.Jump.Key = Keys.Space; player.Controls.MoveUp.Key = player.Controls.RollUp.Key = Keys.W;
        player.Controls.MoveDown.Key = player.Controls.RollDown.Key = Keys.S; player.Controls.MoveLeft.Key = player.Controls.RolltLeft.Key = Keys.A;
        player.Controls.MoveRight.Key = player.Controls.RollRight.Key = Keys.D; player.Controls.CustomWeapon.Key = Keys.C; player.Controls.PowerBeam.Key = Keys.P;
        player.Controls.NextWeapon.Key = Keys.N; player.Controls.PrevWeapon.Key = Keys.V; player.Controls.ScrollAllWeapons = true;
        player.Controls.MouseAim = player.Controls.KeyboardAim = false; player.IgnoreItemPickups = true;
    }
    private static void Clear(RenderWindow window) { foreach (var key in Used) Key(window.KeyboardState, key, false); }
    private static string Canonical(Scene scene, PlayerEntity player) => (string)ArsenalCanonical.Invoke(null, [scene, player, scene.PirateBlaster!, false])!
        + ":" + (string)DroneCanonical.Invoke(null, [scene, player])!;
    private static (byte[] Bytes, (string Name, int End)[] Marks) NativeColumns(Scene scene, PlayerEntity player)
    {
        ArsenalCanonical.Invoke(null, [scene, player, scene.PirateBlaster!, true]);
        var list = (List<(byte[] Bytes, (string Name, int End)[] Marks)>)typeof(CharacterArsenalRuntimeOwnedProbe)
            .GetField("CanonicalDiagnostics", Static)!.GetValue(null)!;
        var result = list[^1]; list.RemoveAt(list.Count - 1); return result;
    }
    private static string Visibility(Scene scene, PlayerEntity player) => JsonSerializer.Serialize(new { scene.FrameCount, player.Health,
        Position = new[] { player.Position.X, player.Position.Y, player.Position.Z }, Flags1 = (uint)player.Flags1, Flags2 = (uint)player.Flags2, player.IsAltForm, player.IsMorphing, player.IsUnmorphing, player.CameraType, player.CameraInfo.Fov,
        NativeAlpha = typeof(PlayerEntity).GetField("_curAlpha", Hidden)!.GetValue(player), CameraStep = typeof(PlayerEntity).GetField("_camSwitchTimer", Hidden)!.GetValue(player),
        DamageAge = typeof(PlayerEntity).GetField("_timeSinceDamage", Hidden)!.GetValue(player), player.DroneLaserFrame,
        SourceSupported = typeof(PlayerEntity).GetProperty("SourceDroneStateSupported", Hidden)!.GetValue(player) });
    private static List<RenderItem> Items(Scene scene) => ((List<RenderItem>)typeof(Scene).GetField("_sourceBodyItems", Hidden)!.GetValue(scene)!).ToList();
    private static IEnumerable<RenderItem> WorldItems(Scene scene) => new[] { "_nonDecalItems", "_translucentItems", "_decalItems" }
        .SelectMany(n => (List<RenderItem>)typeof(Scene).GetField(n, Hidden)!.GetValue(scene)!).Distinct();
    private static string Pose(CharacterSourceDronePose pose) => pose.CurrentOrdinal + ":" + pose.ClipSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
        + ":" + pose.BlendAmount.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + pose.FreeCompletedSteps + ":" + pose.ReadyCompletedSteps + ":" + LocalHash(pose.CurrentLocalPose);
    private static string LocalHash(IReadOnlyList<Matrix4> pose)
    { using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes); foreach (var m in pose) for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) writer.Write(m[r,c]); return Sha(bytes.ToArray()); }
    private static string Raw(CharacterWeightedRenderModel model)
    { using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes); foreach (var s in model.Segments) foreach (float v in s.SourceMatrixPalette!) writer.Write(v); return Sha(bytes.ToArray()); }
    private static void CheckGathers(CharacterSourceArsenalDroneOwner owner, RenderItem[] items, Action<bool, string> check)
    {
        var model = owner.Model; var rig = model.SourceRig!; var local = owner.Pose.CurrentLocalPose; var global = new Matrix4[local.Count]; var done = new bool[local.Count];
        Matrix4 Resolve(int n) { if (!done[n]) { int p = rig.Nodes[n].Parent; global[n] = p < 0 ? local[n] : local[n] * Resolve(p); done[n] = true; } return global[n]; }
        var skin = model.Joints.Select((j, n) => j.InverseBind * Resolve(rig.JointNodes[n])).ToArray();
        check(skin.Length == 33, "independent complete33 inverse-bind/hierarchy gather");
        foreach (var item in items)
        {
            var segment = model.Segments.Single(s => s.ListId == item.ListId);
            check(item.WeightedSkinning && !item.ViewModel && item.SourceGx!.AlternateForm && segment.SourceGx!.Textures.All(GL.IsTexture)
                && item.MatrixStackCount == segment.SourceJointPalette!.Count, "actual drone GX packet retains exact part/textures/physical gather");
            for (int n = 0; n < segment.SourceJointPalette!.Count; n++)
            {
                Matrix4 raw = Matrix(segment.SourceMatrixPalette!, n * 16);
                check(Distance(raw, skin[segment.SourceJointPalette[n]]) < .00002f
                    && Distance(Matrix(item.MatrixStack, n * 16), raw * item.Transform) < .0002f, "source33 raw skin and exactly-once native presentation palette match independent hierarchy");
            }
        }
    }
    private static Matrix4 Matrix(IReadOnlyList<float> p, int n) => new(p[n], p[n+1], p[n+2], p[n+3], p[n+4], p[n+5], p[n+6], p[n+7], p[n+8], p[n+9], p[n+10], p[n+11], p[n+12], p[n+13], p[n+14], p[n+15]);
    private static float Distance(Matrix4 a, Matrix4 b) { float d = 0; for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(a[r,c] - b[r,c])); return d; }
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    private static string Inventory(string root) => (string)NativeInventory.Invoke(null, [root])!;
    private sealed class SourceLocks : IDisposable
    {
        private readonly List<FileStream> _files = []; internal int Count => _files.Count;
        internal SourceLocks(string imports) { try { foreach (string file in Directory.GetFiles(imports, "*", SearchOption.AllDirectories)) _files.Add(new(file, FileMode.Open, FileAccess.Read, FileShare.None)); } catch { Dispose(); throw; } }
        public void Dispose() { foreach (var file in _files) file.Dispose(); _files.Clear(); }
    }
}
