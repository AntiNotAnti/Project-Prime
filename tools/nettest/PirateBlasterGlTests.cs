using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using ErrorCode = OpenTK.Graphics.OpenGL.ErrorCode;

namespace MphRead.NetTest;

// Opt-in owned-AMHE1 acceptance through the existing native Scene, not a
// replacement renderer. Setup, bounded hashing and in-memory pixel controls
// follow ScoutDroneGlTests. Never save original pixels, poses or geometry.
internal static class PirateBlasterGlTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Failure : Exception { internal Failure(string label) : base(label) { } }
    private sealed record OwnedManifest(int Files, long Bytes, string Sha256);
    private sealed record Pixels(int Width, int Height, int ColoredPixels, string Sha256);
    private readonly record struct Input(bool Shoot = false, bool Native = false, bool Custom = false);
    private static readonly Stopwatch Deadline = new();
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static int _checks;
    private static void Check(bool value, string label) { _checks++; if (!value) throw new Failure(label); }
    private static void Bound() { if (Deadline.Elapsed > TimeSpan.FromSeconds(180)) throw new Failure("Arsenal GL exceeded bounded180-second scene budget"); }
    private static object? Get(object owner, string field)
    {
        for (Type? type = owner.GetType(); type != null; type = type.BaseType)
            if (type.GetField(field, Hidden) is { } member) return member.GetValue(owner);
        throw new Failure("expected native Arsenal diagnostic field unavailable");
    }
    internal static int Run(string assets, string? outputPath = null)
    {
        assets = Path.GetFullPath(assets);
        if (!Directory.Exists(assets) || outputPath != null && File.Exists(outputPath)) return 2;
        string temporary = Path.Combine(Path.GetTempPath(), "prime-pirate-gl-" + Guid.NewGuid().ToString("N"));
        string cwd = Directory.GetCurrentDirectory(); string? priorData = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        string priorKey = Paths.MphKey, priorAssets = Paths.FileSystem;
        byte priorSlot = Menu.SaveSlot; SaveWhen priorSave = Menu.NeededSave;
        bool priorFocus = GamepadContexts.Focused, priorMenu = GamepadContexts.MenuVisible;
        bool priorCapture = GamepadContexts.Capturing, priorText = GamepadContexts.TextEntryActive, priorSkip = Cheats.SkipPlanetIntros;
        bool priorReplacements = RenderOptions.CharacterModelReplacements, priorCosmetics = RenderOptions.ShowCustomCosmetics;
        var priorHud = HudProfiles.CopyCurrent(); float priorOpacity = Features.HudOpacity, priorReticle = Features.ReticleOpacity;
        int priorCap = FrameTiming.FrameRateCap;
        string corePath = typeof(Scene).Assembly.Location, toolPath = typeof(PirateBlasterGlTests).Assembly.Location;
        string coreBefore = HashFile(corePath), toolBefore = HashFile(toolPath), stage = "Initialize", status = "FAIL";
        string failureType = "", failureLabel = "", failureHash = ""; OwnedManifest? before = null, after = null;
        bool savesAbsent = false; string renderer = "", version = ""; var cases = new List<object>(); _checks = 0;
        try
        {
            Directory.CreateDirectory(temporary); Directory.SetCurrentDirectory(temporary);
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", Path.Combine(temporary, "data"));
            Check(!Headless.Active && !NetSession.Active && !DemoPlayback.IsActive && !ReplayCapture.LiveRecordingActive,
                "fresh nonheadless offline nonreplay nonrecording process required");
            Paths.MphKey = Ver.AMHE1; Paths.SetPath(Ver.AMHE1, assets);
            stage = "HashOwnedBefore"; before = HashOwned(assets);
            // Scene construction can retire a previous save: these precede it.
            Menu.SaveSlot = 0; Menu.NeededSave = SaveWhen.Never; Cheats.SkipPlanetIntros = true;
            GamepadContexts.Focused = true; GamepadContexts.MenuVisible = GamepadContexts.Capturing = GamepadContexts.TextEntryActive = false;
            GamepadManager.ClearAll(); PointerDevice.Reset();
            RenderOptions.CharacterModelReplacements = false; RenderOptions.ShowCustomCosmetics = false;
            HudProfiles.Publish(HudProfileDefaults.Create("Project Prime")); Features.HudOpacity = Features.ReticleOpacity = 1;
            stage = "CreateNativeContext"; Deadline.Restart(); GraphicsBackendPolicy.Configure("opengl");
            var settings = DesktopGlContext.Settings(background: true); settings.ClientSize = new(640, 480);
            settings.StartVisible = true; settings.StartFocused = false; settings.Title = "Project Prime native Arsenal diagnostic";
            using (var window = new NativeWindow(settings))
            using (var graphics = new DesktopGraphicsSession(window))
            {
                GLFW.SwapInterval(0); renderer = GL.GetString(StringName.Renderer); version = GL.GetString(StringName.Version);
                RunScene(window, cases, label => stage = label);
            }
            stage = "SaveAbsence";
            savesAbsent = NoSaves(temporary); Check(savesAbsent, "slot0/Never owner produced no campaign save files");
            stage = "HashOwnedAfter"; after = HashOwned(assets); Check(before == after, "entire owned source inventory unchanged");
            Check(HashFile(corePath) == coreBefore && HashFile(toolPath) == toolBefore, "core/tool binaries unchanged during actual GL acceptance");
            status = "PASS"; return 0;
        }
        catch (Exception error)
        {
            failureType = error.GetType().Name; failureHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(error.Message)));
            if (error is Failure) failureLabel = error.Message;
            return 1;
        }
        finally
        {
            if (before != null && after == null) { try { after = HashOwned(assets); } catch (Exception error) { failureType += "/" + error.GetType().Name; } }
            if (Directory.Exists(temporary)) savesAbsent = NoSaves(temporary);
            Menu.SaveSlot = priorSlot; Menu.NeededSave = priorSave; Cheats.SkipPlanetIntros = priorSkip;
            GamepadContexts.Focused = priorFocus; GamepadContexts.MenuVisible = priorMenu;
            GamepadContexts.Capturing = priorCapture; GamepadContexts.TextEntryActive = priorText;
            RenderOptions.CharacterModelReplacements = priorReplacements; RenderOptions.ShowCustomCosmetics = priorCosmetics;
            HudProfiles.Publish(priorHud); Features.HudOpacity = priorOpacity; Features.ReticleOpacity = priorReticle;
            FrameTiming.FrameRateCap = priorCap; FrameTiming.Reset(); PointerDevice.Reset(); GamepadManager.ClearAll(); Read.ClearCache();
            Paths.MphKey = priorKey; Paths.SetPath(priorKey, priorAssets);
            Directory.SetCurrentDirectory(cwd); Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", priorData);
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
            string json = JsonSerializer.Serialize(new { Schema = 1, Status = status, Stage = stage, Checks = _checks,
                FailureType = failureType, FailureLabel = failureLabel, FailureMessageSha256 = failureHash, renderer, version,
                CoreSha256Before = coreBefore, CoreSha256After = HashFile(corePath), ToolSha256Before = toolBefore, ToolSha256After = HashFile(toolPath),
                OwnedBefore = before, OwnedAfter = after, TemporaryContextDeleted = !Directory.Exists(temporary), SaveFilesAbsent = savesAbsent,
                PicturesSaved = 0, SourcePayloadsWritten = false, cases,
                Scope = "Actual owned-AMHE1 zero-bot native Weavel Arsenal Scene, synthetic native Shoot/PowerBeam/CustomWeapon keys, original gun fallback, orange native bolt trails, native fallback effects observations and UA HUD. Exact burst0/6/12/release/cooldown and repeated draw-only owner/mark/resource checks. Pixel hashes are not OCR. Positive mark creation/pixels, opponent damage, donor asset parity, audio output, online/replay, Android/physical input, display cadence and performance remain unqualified."
            }, new JsonSerializerOptions { WriteIndented = true });
            if (outputPath != null) { string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, json); }
            Console.WriteLine(json);
        }
    }
    private static bool NoSaves(string root) => !Directory.Exists(Path.Combine(root, "Savedata"))
        && !Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(p => Path.GetExtension(p).Equals(".sav", StringComparison.OrdinalIgnoreCase));

    private static void RunScene(NativeWindow window, List<object> cases, Action<string> stage)
    {
        Scene? scene = null; TrooperPirateBlasterRuntime? runtime = null; BeamProjectileEntity[] pool = Array.Empty<BeamProjectileEntity>();
        int steps = 0, pictures = 0, maximumTrails = 0, trailPixelWitnesses = 0; var uaPixels = new Dictionary<string, Pixels>();
        try
        {
            const string room = "MP3 PROVING GROUND";
            stage("LoadOwnedArsenalScene"); Bound(); var keyboard = SyntheticInput.CreateKeyboard();
            scene = new Scene(window.FramebufferSize, keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { });
            var plan = new LaunchPlan { Kind = LaunchKind.Offline, Hunter = Hunter.Weavel, RoomKey = room, Mode = GameMode.Battle,
                Bots = 0, IsPlaytest = true, OfflineCharacter = OfflineCharacterMode.TrooperArsenalPractice,
                MatchRules = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle, TimeLimitSeconds = 0, PointGoal = 10000,
                    MovementModel = MovementModel.Native, RiftlineM7Available = false } };
            plan.MatchRules.ApplyInitialConfiguration(scene.GameState); scene.AddPlayer(Hunter.Weavel);
            Check(OfflineCharacterGameplay.TryBind(scene, plan, out _), "explicit native Arsenal plan binds before player initialization");
            var player = scene.Players.Main;
            Check(player.ModCharacterGameplay == CharacterGameplayRules.TrooperArsenalV1 && scene.PrivateCharacterGameplayUnsupported,
                "private Arsenal rule and one-way replay exclusion have the exact local owner");
            scene.AddRoom(room, GameMode.Battle, playerCount: 1); MphRead.Sound.Sfx.Load(scene); scene.OnLoad(); scene.OnResize();
            foreach (var control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            player.Controls.Shoot.Key = Keys.Enter; player.Controls.PowerBeam.Key = Keys.P; player.Controls.CustomWeapon.Key = Keys.C;
            player.Controls.MouseAim = false; player.Controls.KeyboardAim = true;
            SetKey(keyboard, Keys.Enter, true);
            for (int i = 0; i < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); i++) Step(new(Shoot: true));
            SetKey(keyboard, Keys.Enter, false); Check(player.LoadFlags.TestFlag(LoadFlags.Spawned), "native Shoot input completes spawning");
            scene.CameraSequences.Current?.End(); for (int i = 0; i < 240; i++) Step();
            Check(player.ModIsInPlay && player.Health > 0 && !player.IsAltForm && scene.CameraMode == CameraMode.Player,
                "living native first-person gameplay after genuine spawn");
            runtime = scene.PirateBlaster ?? throw new Failure("native initialization did not create the private Blaster owner");
            pool = runtime.Beams.ToArray(); var gun = (ModelInstance)Get(player, "_gunModel")!;
            Check(pool.Length == 40 && pool.All(b => !player.DroneBeams.Contains(b)), "private Blaster pool40 is distinct from drone flights");
            Check(runtime.Selected(player) && runtime.Snapshot.Ua == 60 && player.ModAmmo.Ua == 60 && runtime.Shots == 0,
                "genuine native spawn establishes selected sharedUA60 without a developer grant or an accidental shot");
            Check(gun.Model.Meshes.Count > 0 && gun.Model.Nodes.Count > 0, "native fallback cannon has real mesh/node ownership");
            int shader = (int)Get(scene, "_shaderProgramId")!;
            Check(shader > 0 && GL.IsProgram(shader), "actual shared Scene shader is linked in the current GL context");
            NoM7(); Picture("ready-ua60", false, gunPixels: true);
            stage("RealPaidBurst"); ulong firstShots = runtime.Shots;
            Step(new(Shoot: true)); ulong firstSourceFrame = ((PirateBurstTimeline)Get(runtime, "_timeline")!).Snapshot.Frame;
            Check(player.Controls.Shoot.IsDown && runtime.Shots == firstShots + 1 && runtime.Snapshot.Ua == 55,
                "sourceburst0 follows actual Shoot input and debits sharedUA5 once");
            Picture("burst0-ua55", false);
            for (int offset = 1; offset <= 12; offset++)
            {
                Step(); Check(!player.Controls.Shoot.IsDown, "paid tails follow actual trigger release");
                Check(((PirateBurstTimeline)Get(runtime, "_timeline")!).Snapshot.Frame == firstSourceFrame + (ulong)offset,
                    "only genuine simulation steps advance the private source frame");
                Check(runtime.Shots == firstShots + (ulong)(offset >= 12 ? 3 : offset >= 6 ? 2 : 1)
                    && runtime.Snapshot.Ua == 55 && player.ModAmmo.Ua == 55, "exact0/6/12 bolts retain the single nativeUA payment");
                if (offset is 6 or 12) Picture("burst" + offset + "-released", true);
            }
            Check(!runtime.Snapshot.Tails, "sourceburst12 consumes the final paid tail");
            for (int offset = 13; offset <= 40; offset++)
            {
                Step(); Check(runtime.Shots == firstShots + 3 && runtime.Snapshot.Cooldown == 40 - offset,
                    "release never invents another burst or advances the40-step cadence in a draw");
                if (offset == 13) Picture("cooldown-released", false);
            }
            Check(runtime.Snapshot.Cooldown == 0 && runtime.Snapshot.Ua == 55, "genuine sourcewait40 ends without ammo restoration");
            Picture("ready-ua55", false, gunPixels: true);
            Check(uaPixels["ready-ua60"].Sha256 != uaPixels["ready-ua55"].Sha256,
                "native ammo-panel pixels differ between actualUA60 andUA55 at ready state");
            Check(maximumTrails > 0 && trailPixelWitnesses > 0, "actual orange native bolt trails have a nonzero framebuffer contribution");
            stage("RealSelectionAndCleanup"); Step(new(Native: true)); Step();
            Check(!runtime.Selected(player) && player.CurrentWeapon == BeamType.PowerBeam && runtime.Snapshot.Ua == 55,
                "native PowerBeam selection retires the custom selection without changing sharedUA");
            Picture("native-powerbeam", false, gunPixels: true);
            Step(new(Custom: true)); Step(); Check(runtime.Selected(player), "actual custom binding reselects the private Arsenal owner");
            Picture("reselected", false);
            Step(new(Shoot: true)); Check(runtime.Shots == firstShots + 4 && runtime.Snapshot.Ua == 50
                && pool.Any(b => ReferenceEquals(b.Owner, player) && b.Lifespan > 0), "teardown begins with a genuinely paid live native flight");
            Picture("live-before-teardown", false);
            cases.Add(new { Kind = "Timeline", SimulationSteps = steps, Pictures = pictures, MaximumMatchedOrangeTrails = maximumTrails,
                TrailPixelWitnesses = trailPixelWitnesses, runtime.Shots, runtime.Hits, Ua = runtime.Snapshot.Ua,
                MarkedTargets = runtime.Snapshot.Marks, PositiveMarkPixelsQualified = false, NativeGunModel = gun.Model.Name,
                Scope = "Actual zero-bot source/input/render path; no forged mark or resource state" });

            void Step(Input input = default)
            {
                Bound(); SetKey(keyboard, Keys.Enter, input.Shoot); SetKey(keyboard, Keys.P, input.Native); SetKey(keyboard, Keys.C, input.Custom);
                scene.OnSimulationFrame(); steps++;
                Check(!NetSession.Active && player.Health > 0 && Finite(player.Position) && Finite(player.Speed), "finite living offline native Arsenal simulation step");
            }
            void NoM7()
            {
                Check(scene.OfflineWeapons == null && scene.CustomCombat == null && scene.CustomRifleModel == null
                    && !(bool)Get(scene, "_customRifleModelAttempted")! && Get(scene, "_customRifleAudio") == null
                    && Get(scene, "_customRifleTracks") == null && !scene.CustomWeaponHud(player).Ready && !scene.CustomWeaponHud(player).Visible,
                    "private Blaster never adopts, grants or prewarms M7 owners/model/audio/HUD");
                Check(!RenderOptions.CharacterModelReplacements && !RenderOptions.ShowCustomCosmetics, "native fallback rendering remains explicit");
            }
            string Capture()
            {
                var text = new StringBuilder(); text.Append(scene.FrameCount).Append('|').Append(scene.GameState.MatchTime)
                    .Append('|').Append(scene.Random.Rng1).Append('|').Append(scene.Random.Rng2)
                    .Append('|').Append(player.Health).Append('|').Append(player.ModAmmo).Append('|').Append(player.Position).Append('|').Append(player.Speed)
                    .Append('|').Append(runtime!.Owner).Append('|').Append(runtime.Snapshot).Append('|').Append(runtime.LastImpact)
                    .Append('|').Append(((PirateBurstTimeline)Get(runtime, "_timeline")!).Snapshot)
                    .Append('|').Append(Get(player, "_muzzlePos")).Append('|').Append(Get(player, "_aimVec")).Append('|').Append(Get(player, "_gunVec1"))
                    .Append('|').Append(FlightWitness(pool));
                foreach (var other in scene.Players.Items) text.Append('|').Append(runtime.IsMarked(player, other));
                foreach (var mark in (Array)Get(runtime, "_marks")!) text.Append('|').Append(mark);
                foreach (var model in player.GetModels()) text.Append('|').AppendJoin(',', model.AnimInfo.Frame);
                return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
            }
            string Resources() => HashText(string.Join(',', ((HashSet<int>)Get(scene, "_ownedTextures")!).Order()) + "|"
                + Get(scene, "_shaderProgramId") + "|" + string.Join(',', gun.Model.Meshes.Select(m => m.ListId)));
            void Picture(string label, bool trailPixels, bool gunPixels = false)
            {
                Bound(); NoM7(); string before = Capture(); scene.OnDrawFrame(); var items = Queued(scene);
                var gunIds = gun.Model.Meshes.Select(m => m.ListId).Where(id => id != 0).ToHashSet();
                var gunItems = items.Where(i => i.Type == RenderItemType.Mesh && i.ViewModel && gunIds.Contains(i.ListId)).ToArray();
                Check(gunItems.Length > 0, "original native cannon contributes actual first-person Mesh submissions: " + label);
                var active = pool.Where(b => runtime!.OwnsFlight(b) && b.Lifespan > 0).ToArray();
                Vector3 orange = new(31 / 31f, 12 / 31f, 4 / 31f);
                Check(active.All(b => PirateBlasterDefinition.Is(b.ModCustomDefinition) && b.DrawFuncId == 0 && b.Color == orange),
                    "live private bolts retain their exact orange native draw0 profile");
                var trails = items.Where(i => i.Type == RenderItemType.TrailSingle && i.Diffuse == orange
                    && active.Any(b => i.TextureBindingId == (int)Get(b, "_bindingId")!
                        && (i.Transform.Row3.Xyz - b.ModBeamDrawBack).LengthSquared < .0000001f)).ToArray();
                maximumTrails = Math.Max(maximumTrails, trails.Length);
                Check(trails.All(t => t.TextureBindingId > 0 && GL.IsTexture(t.TextureBindingId)
                    && t.ItemCount == 8 && t.Points.Length >= 8 && t.Points.Take(8).All(Finite)),
                    "matched orange trails use finite native geometry and live scene-owned textures");
                Check(scene.OnRenderFrame(), "actual shared Scene renderer produces native Arsenal picture"); pictures++;
                Check(GL.GetError() == ErrorCode.NoError && Capture() == before, "world/HUD rendering is GL-clean and gameplay/mark reads are immutable");
                int changedTrails = 0, changedGun = 0;
                if (trailPixels && trails.Length > 0) { changedTrails = OpacityControl(trails); if (changedTrails > 0) trailPixelWitnesses++; }
                if (gunPixels) { changedGun = OpacityControl(gunItems); Check(changedGun > 0, "original native gun contributes visible pixels under render-only control"); }
                byte[] hud = HudOnly(); Pixels ammo;
                try
                {
                    ammo = Region(hud, scene.Size.X, scene.Size.Y, scene.Size.X * 3 / 5, scene.Size.Y * 166 / 192,
                        scene.Size.X * 2 / 5, scene.Size.Y * 26 / 192);
                    if (runtime!.Selected(player)) Check(ammo.ColoredPixels > 0 && runtime.Snapshot.Ua == player.ModAmmo.Ua,
                        "visible native UA panel reads the genuine shared bank without M7 magazine state");
                    uaPixels[label] = ammo;
                }
                finally { Array.Clear(hud); }
                scene.OnDrawFrame(); Check(scene.OnRenderFrame(), "ordinary native picture restored after HUD diagnostic"); pictures++;
                string resources = Resources();
                for (int draw = 0; draw < 3; draw++)
                {
                    scene.OnDrawFrame(); Check(scene.OnRenderFrame(), "draw-only repeat remains active"); pictures++;
                    Check(Capture() == before && Resources() == resources && GL.GetError() == ErrorCode.NoError,
                        "warmed repeated draws preserve source clocks/UA/marks/flights/anchors and GL resource identities");
                }
                NoM7(); DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
                Check(Capture() == before && Resources() == resources && GL.GetError() == ErrorCode.NoError,
                    "Present/AfterRenderFrame neither step gameplay nor retire marked targets/resources");
                cases.Add(new { Kind = "Picture", Label = label, Tick = scene.FrameCount, Width = scene.Size.X, Height = scene.Size.Y,
                    GunMeshes = gunItems.Length, LivePrivateFlights = active.Length, MatchedOrangeTrails = trails.Length,
                    TrailChangedPixels = changedTrails, GunChangedPixels = changedGun, AmmoPanel = ammo,
                    Ua = runtime!.Snapshot.Ua, runtime.Snapshot.Cooldown, runtime.Snapshot.Tails, runtime.Shots, runtime.Hits,
                    Marks = runtime.Snapshot.Marks, PositiveMarkPixelsQualified = false,
                    NativeSingleParticles = (int)Get(scene, "_singleParticleCount")!, ResourceIdentitySha256 = resources });
                int OpacityControl(RenderItem[] selected)
                {
                    byte[] on = ReadPixels(), off = Array.Empty<byte>(); var alphas = selected.Select(t => t.Alpha).ToArray();
                    try
                    {
                        foreach (var item in selected) item.Alpha = 0;
                        Check(scene.OnRenderFrame(), "same cached Scene frame supports render-only zero-opacity control"); pictures++;
                        off = ReadPixels(); return Changed(on, off, scene.Size.X, scene.Size.Y, 0, 0, scene.Size.X, scene.Size.Y);
                    }
                    finally
                    {
                        for (int i = 0; i < selected.Length; i++) selected[i].Alpha = alphas[i];
                        Array.Clear(on); Array.Clear(off);
                        Check(scene.OnRenderFrame() && Capture() == before, "render-only opacity control restores native picture without source mutation"); pictures++;
                    }
                }
            }
            byte[] ReadPixels()
            {
                int alignment = GL.GetInteger(GetPName.PackAlignment); byte[] pixels = new byte[checked(scene.Size.X * scene.Size.Y * 4)];
                try { GL.PixelStore(PixelStoreParameter.PackAlignment, 1); GL.ReadPixels(0, 0, scene.Size.X, scene.Size.Y, PixelFormat.Rgba, PixelType.UnsignedByte, pixels); }
                finally { GL.PixelStore(PixelStoreParameter.PackAlignment, alignment); }
                Check(GL.GetError() == ErrorCode.NoError, "in-memory framebuffer witness is GL-clean"); return pixels;
            }
            byte[] HudOnly()
            {
                GL.Disable(EnableCap.ScissorTest); GL.ClearColor(0, 0, 0, 0); GL.Clear(ClearBufferMask.ColorBufferBit);
                player.DrawHudObjects(); pictures++; return ReadPixels();
            }
        }
        finally
        {
            if (scene != null)
            {
                int[] textures = ((HashSet<int>)Get(scene, "_ownedTextures")!).ToArray();
                try { MomentumSceneTests.Release(scene); }
                finally
                {
                    Check(pool.All(b => b.Owner == null && b.Equip == null && b.ModCustomShot == null && b.Effect == null && b.MuzzleEffect == null && b.Target == null && b.Lifespan == 0
                        && !ContainsEntity(scene, b)), "native Scene teardown clears every private Blaster flight/source/effect/target owner");
                    Check(scene.PirateBlaster == null && (runtime == null || !runtime.Snapshot.Selected && runtime.Snapshot.Marks == 0),
                        "native teardown retires the private Arsenal runtime and mark reads");
                    Check(((HashSet<int>)Get(scene, "_ownedTextures")!).Count == 0 && textures.All(id => !GL.IsTexture(id)),
                        "Scene unload releases actual scene-owned GL textures while the context remains live");
                    Check(GL.GetError() == ErrorCode.NoError, "native Arsenal teardown remains GL-clean");
                    cases.Add(new { Kind = "Teardown", RetainedPool = pool.Length, RetiredSceneTextures = textures.Length });
                }
            }
        }
    }
    private static RenderItem[] Queued(Scene scene) => new[] { "_decalItems", "_nonDecalItems", "_translucentItems" }
        .SelectMany(n => (List<RenderItem>)Get(scene, n)!).Distinct().ToArray();
    private static string HashText(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool ContainsEntity(Scene scene, EntityBase expected)
    {
        foreach (EntityBase entity in scene.Entities)
            if (ReferenceEquals(entity, expected)) return true;
        return false;
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static string FlightWitness(IReadOnlyList<BeamProjectileEntity> beams)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var b in beams)
        {
            // Only a private in-memory digest leaves this function, never positions/poses.
            string row = b.Id + ":" + (b.Owner == null ? 0 : 1) + ":" + BitConverter.SingleToInt32Bits(b.Lifespan) + ":"
                + BitConverter.SingleToInt32Bits(b.Age) + ":" + BitConverter.SingleToInt32Bits(b.Position.X) + ":"
                + BitConverter.SingleToInt32Bits(b.Position.Y) + ":" + BitConverter.SingleToInt32Bits(b.Position.Z);
            hash.AppendData(Encoding.ASCII.GetBytes(row));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static Pixels Region(byte[] bytes, int width, int height, int x, int top, int w, int h)
    {
        x = Math.Clamp(x, 0, width - 1); top = Math.Clamp(top, 0, height - 1); w = Math.Clamp(w, 1, width - x); h = Math.Clamp(h, 1, height - top);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); int colored = 0;
        for (int y = top; y < top + h; y++) for (int xx = x; xx < x + w; xx++)
        { int at = ((height - y - 1) * width + xx) * 4; hash.AppendData(bytes.AsSpan(at, 4)); if (bytes[at] != 0 || bytes[at + 1] != 0 || bytes[at + 2] != 0) colored++; }
        return new(w, h, colored, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
    private static int Changed(byte[] a, byte[] b, int width, int height, float left, float top, float w, float h)
    {
        int x0 = Math.Clamp((int)MathF.Floor(left), 0, width), x1 = Math.Clamp((int)MathF.Ceiling(left + w), 0, width);
        int y0 = Math.Clamp((int)MathF.Floor(top), 0, height), y1 = Math.Clamp((int)MathF.Ceiling(top + h), 0, height), changed = 0;
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
        { int at = ((height - y - 1) * width + x) * 4; if (a[at] != b[at] || a[at + 1] != b[at + 1] || a[at + 2] != b[at + 2]) changed++; }
        return changed;
    }
    private static OwnedManifest HashOwned(string root)
    {
        for (DirectoryInfo? directory = new(root); directory != null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new Failure("owned source ancestry contains a symlink");
        var files = new List<string>(); long total = 0; int entries = 0;
        void Walk(string directory, int depth = 0)
        {
            if (depth > 64) throw new Failure("owned source directory depth exceeds bound");
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > 65536) throw new Failure("owned source inventory exceeds entry bound");
                var attributes = File.GetAttributes(path); if ((attributes & FileAttributes.ReparsePoint) != 0) throw new Failure("owned source entry is a symlink");
                if ((attributes & FileAttributes.Directory) != 0) Walk(path, depth + 1);
                else { files.Add(path); total += new FileInfo(path).Length; }
            }
        }
        Walk(root); Check(files.Count > 0, "owned source inventory nonempty"); files.Sort(StringComparer.Ordinal);
        using var manifest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string path in files) manifest.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, path).Replace('\\', '/') + '\0'
            + new FileInfo(path).Length.ToString(CultureInfo.InvariantCulture) + '\0' + HashFile(path) + '\n'));
        return new(files.Count, total, Convert.ToHexStringLower(manifest.GetHashAndReset()));
    }
    private static string HashFile(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); long length = input.Length;
        if (length > 2L * 1024 * 1024 * 1024) throw new Failure("hash input exceeds bounded file extent");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[65536]; long remaining = length;
        while (remaining > 0) { int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining)); if (read == 0) throw new Failure("hash input truncated"); hash.AppendData(buffer, 0, read); remaining -= read; }
        if (input.ReadByte() != -1 || input.Length != length) throw new Failure("hash input extent changed");
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
