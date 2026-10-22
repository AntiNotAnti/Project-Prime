using System;
using System.Collections;
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
using MphRead.Hud;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Opt-in actual owned-AMHE1 native world/HUD GL acceptance. Keyboard events own
// form, attack and hover; no health, timers, transforms or drone state are forged.
// Pixel buffers exist only in memory and are cleared after count/hash witnesses.
// Extra HUD-only pictures call the same public native HUD owner after a normal
// Scene render, on a cleared buffer. They are explicitly separate from gameplay
// pictures; this is not an independent renderer or an imported Trooper preview.
internal static class ScoutDroneGlTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Failure : Exception { internal Failure(string label) : base(label) { } }
    private sealed record OwnedManifest(int Files, long Bytes, string Sha256);
    private readonly record struct Controls(bool Morph = false, bool Attack = false, bool Jump = false, bool Zoom = false);
    private readonly record struct State(ulong Tick, Vector3 Position, Vector3 Speed, int Health, int BodyHealth,
        bool Alt, bool Morphing, bool Unmorphing, int FireState, int FireFrame, int Cooldown, int Shots, int Damage,
        int AnimationFrame, string Flights);
    private sealed record Pixels(int Width, int Height, int ColoredPixels, string Sha256);
    private static readonly Stopwatch Deadline = new();
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static int _checks;
    private static void Check(bool value, string label) { _checks++; if (!value) throw new Failure(label); }
    private static void Bound() { if (Deadline.Elapsed > TimeSpan.FromSeconds(120)) throw new Failure("native drone GL exceeded bounded120-second scene budget"); }
    private static object? Get(object owner, string field)
    {
        for (Type? type = owner.GetType(); type != null; type = type.BaseType)
            if (type.GetField(field, Hidden) is { } member) return member.GetValue(owner);
        throw new Failure("expected native render owner diagnostic field unavailable");
    }

    internal static int Run(string assets, string? outputPath = null, string room = "MP3 PROVING GROUND")
    {
        assets = Path.GetFullPath(assets);
        if (!Directory.Exists(assets) || outputPath != null && File.Exists(outputPath)) return 2;
        string temporary = Path.Combine(Path.GetTempPath(), "prime-drone-gl-" + Guid.NewGuid().ToString("N"));
        string cwd = Directory.GetCurrentDirectory(); string? priorData = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        string priorKey = Paths.MphKey, priorAssets = Paths.FileSystem;
        byte priorSlot = Menu.SaveSlot; SaveWhen priorSave = Menu.NeededSave;
        bool priorFocus = GamepadContexts.Focused, priorSkip = Cheats.SkipPlanetIntros;
        bool priorReplacements = RenderOptions.CharacterModelReplacements, priorCosmetics = RenderOptions.ShowCustomCosmetics;
        float priorReticle = Features.ReticleOpacity; int priorCap = FrameTiming.FrameRateCap;
        string corePath = typeof(Scene).Assembly.Location, toolPath = typeof(ScoutDroneGlTests).Assembly.Location;
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
            // These values must precede Scene construction: Reset can commit a prior save.
            Menu.SaveSlot = 0; Menu.NeededSave = SaveWhen.Never; GamepadContexts.Focused = true;
            Cheats.SkipPlanetIntros = true; GamepadManager.ClearAll(); PointerDevice.Reset();
            stage = "CreateNativeContext"; Deadline.Restart(); GraphicsBackendPolicy.Configure("opengl");
            var settings = DesktopGlContext.Settings(background: true); settings.ClientSize = new(640, 480);
            settings.StartVisible = true; settings.StartFocused = false; settings.Title = "Project Prime native Scout Drone diagnostic";
            using (var window = new NativeWindow(settings))
            using (var graphics = new DesktopGraphicsSession(window))
            {
                GLFW.SwapInterval(0); renderer = GL.GetString(StringName.Renderer); version = GL.GetString(StringName.Version);
                RunScene(window, room, cases, label => stage = label);
                RunLaserScene(window, room, cases, label => stage = label);
            }
            stage = "SaveAbsence";
            savesAbsent = !Directory.Exists(Path.Combine(temporary, "Savedata"))
                && !Directory.EnumerateFiles(temporary, "*", SearchOption.AllDirectories).Any(p => Path.GetExtension(p).Equals(".sav", StringComparison.OrdinalIgnoreCase));
            Check(savesAbsent, "slot0/Never owner produced no campaign save files");
            stage = "HashOwnedAfter"; after = HashOwned(assets); Check(before == after, "entire owned source inventory unchanged");
            Check(HashFile(corePath) == coreBefore && HashFile(toolPath) == toolBefore, "core/tool binaries unchanged during GL acceptance");
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
            if (Directory.Exists(temporary)) savesAbsent = !Directory.Exists(Path.Combine(temporary, "Savedata"))
                && !Directory.EnumerateFiles(temporary, "*", SearchOption.AllDirectories).Any(p => Path.GetExtension(p).Equals(".sav", StringComparison.OrdinalIgnoreCase));
            Menu.SaveSlot = priorSlot; Menu.NeededSave = priorSave; GamepadContexts.Focused = priorFocus; Cheats.SkipPlanetIntros = priorSkip;
            RenderOptions.CharacterModelReplacements = priorReplacements; RenderOptions.ShowCustomCosmetics = priorCosmetics;
            Features.ReticleOpacity = priorReticle; FrameTiming.FrameRateCap = priorCap; FrameTiming.Reset(); PointerDevice.Reset(); GamepadManager.ClearAll(); Read.ClearCache();
            Paths.MphKey = priorKey; Paths.SetPath(priorKey, priorAssets);
            Directory.SetCurrentDirectory(cwd); Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA", priorData);
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
            string json = JsonSerializer.Serialize(new { Schema = 1, Status = status, Stage = stage, Checks = _checks,
                FailureType = failureType, FailureLabel = failureLabel, FailureMessageSha256 = failureHash, renderer, version,
                CoreSha256Before = coreBefore, CoreSha256After = HashFile(corePath), ToolSha256Before = toolBefore, ToolSha256After = HashFile(toolPath),
                OwnedBefore = before, OwnedAfter = after, TemporaryContextDeleted = !Directory.Exists(temporary), SaveFilesAbsent = savesAbsent,
                PicturesSaved = 0, SourcePayloadsWritten = false, cases,
                Scope = "Actual owned-AMHE1 offline local Weavel practice Scene/shaders/world/HUD and native keyboard Morph/Jump/AltAttack/Zoom. Separate native laser Scene checks cached57..122 rays, real TrailSingle pixel contribution, merge/death/teardown and draw-only immutability. Psycho Bit mobile and native half-turret planted fallbacks only. HUD-only/opacity-control pictures are render-only diagnostics. No imported Trooper/source parity, standing-body cylinder/headshots, laser opponent damage, online, replay, physical input, display cadence or performance qualification."
            }, new JsonSerializerOptions { WriteIndented = true });
            if (outputPath != null) { string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, json); }
            Console.WriteLine(json);
        }
    }

    private static void RunScene(NativeWindow window, string room, List<object> cases, Action<string> stage)
    {
        Scene? scene = null; var keyboard = SyntheticInput.CreateKeyboard(); int simulationSteps = 0, pictures = 0, hudOnlyPictures = 0;
        BeamProjectileEntity[] retainedPool = Array.Empty<BeamProjectileEntity>(); ModelInstance? mobile = null;
        var stateHashes = new Dictionary<string, string>(); bool animated = false; int initialAnimation = -1, maxNativeTrails = 0, maxBodyMeshes = 0;
        try
        {
            stage("LoadOwnedNativeScene"); Bound();
            scene = new Scene(window.FramebufferSize, keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { });
            var plan = new LaunchPlan { Kind = LaunchKind.Offline, Hunter = Hunter.Weavel, RoomKey = room, Mode = GameMode.Battle,
                Bots = 0, IsPlaytest = true, OfflineCharacter = OfflineCharacterMode.TrooperScoutDronePractice,
                MatchRules = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle, TimeLimitSeconds = 0, PointGoal = 10000 } };
            plan.MatchRules.ApplyInitialConfiguration(scene.GameState); scene.AddPlayer(Hunter.Weavel);
            Check(OfflineCharacterGameplay.TryBind(scene, plan, out _), "real explicit offline plan binds before player initialization");
            var player = scene.Players.Main;
            Check(player.ModCharacterGameplay == CharacterGameplayRules.TrooperDroneV1 && scene.PrivateCharacterGameplayUnsupported,
                "actual owner carries distinct drone rule and one-way replay exclusion");
            scene.AddRoom(room, GameMode.Battle, playerCount: 1); MphRead.Sound.Sfx.Load(scene); scene.OnLoad(); scene.OnResize();
            RenderOptions.CharacterModelReplacements = false; RenderOptions.ShowCustomCosmetics = false; Features.ReticleOpacity = 1;
            Check((int)Get(scene, "_shaderProgramId")! > 0 && GL.IsProgram((int)Get(scene, "_shaderProgramId")!), "actual Scene shader program linked in real context");
            foreach (var control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            player.Controls.Shoot.Key = Keys.Enter; player.Controls.Morph.Key = Keys.B; player.Controls.AltAttack.Key = Keys.F; player.Controls.Jump.Key = Keys.Space;
            player.Controls.MouseAim = false; player.Controls.KeyboardAim = true;
            SetKey(keyboard, Keys.Enter, true);
            for (int i = 0; i < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); i++) { Step(); if (i % 60 == 0) Picture("spawn", false); }
            SetKey(keyboard, Keys.Enter, false); Check(player.LoadFlags.TestFlag(LoadFlags.Spawned), "native shoot-to-spawn completed through real input");
            scene.CameraSequences.Current?.End(); for (int i = 0; i < 240; i++) Step();
            Check(player.ModIsInPlay && player.Health > 0 && !player.IsAltForm && scene.CameraMode == CameraMode.Player,
                "fully initialized living local native player camera");
            mobile = (ModelInstance?)Get(player, "_droneModel");
            Check(mobile != null && mobile.Model.Name == "PsychoBit" && player.GetModels().Contains(mobile)
                && mobile.AnimInfo.Index[0] == 3 && mobile.AnimInfo.FrameCount[0] > 1, "actual registered Psycho Bit fallback has native hover animation3");
            initialAnimation = mobile!.AnimInfo.Frame[0]; Picture("biped", false);
            Split();
            Check(player.DroneBody.Health > 0 && ReferenceEquals(player.DroneBody.Owner, player)
                && ContainsEntity(scene, player.DroneBody)
                && player.DroneBody.GetModels().Any(m => m.Model.Name == "WeavelAlt_Turret_lod0"), "planted body is live exact native form-owned turret model");
            retainedPool = player.DroneBeams.ToArray(); Check(retainedPool.Length == 40, "private native projectile pool40 admitted");
            Picture("ready", true);
            for (int i = 0; i < 24; i++) Step(new(Jump: true));
            Check(player.Controls.Jump.IsDown && float.IsFinite(player.DroneFloorGap), "real Jump input reaches finite native hover owner");
            Picture("hover", true);
            Step(new(Attack: true)); Check(player.DroneFireState == 1, "real AltAttack edge starts native unfold state"); Picture("unfolding", true);
            for (int i = 0; i < 240 && player.DroneFireState != 2; i++) Step(new(Attack: true));
            Check(player.DroneFireState == 2, "held real AltAttack reaches firing state");
            for (int i = 0; i < 12 && player.DroneShots == 0; i++) Step(new(Attack: true));
            Check(player.DroneShots == 2 && player.DroneBeams.Any(b => ReferenceEquals(b.Owner, player) && b.Lifespan > 0), "first actual paired volley owns live native flights");
            Picture("firing", true);
            Check(maxNativeTrails > 0, "live private native beams submit their exact bound native trails for actual Scene rendering");
            for (int i = 0; i < 180 && player.DroneFireState == 2; i++) Step(new(Attack: true));
            Check(player.DroneFireState == 3 && player.DroneShots == 38, "actual full primary burst reaches native fold with38 shots"); Picture("folding", true);
            for (int i = 0; i < 32 && player.DroneFireState != 0; i++) Step();
            Check(player.DroneFireState == 0 && player.DroneCooldown > 0, "native fold completes into cooldown"); Picture("cooling", true);
            Check(stateHashes.Values.Distinct().Count() >= 4, "native HUD-only status-line pixels distinguish ready/unfold/fire/fold/cooling states");
            Check(animated, "native Psycho Bit animation advances through actual simulation steps");
            Check(maxBodyMeshes > 0, "native planted-body model contributes real Scene mesh submissions");
            Step(new(Morph: true));
            for (int i = 0; i < 120 && (player.IsAltForm || player.IsUnmorphing); i++) Step();
            Check(!player.IsAltForm && !player.IsMorphing && !player.IsUnmorphing, "real Morph input completes native merge"); Step(); Picture("merged", false);
            Check(!player.Flags2.TestFlag(PlayerFlags2.Halfturret) && player.DroneBody.Health == 0, "native merge retires planted body without fabricated health");
            // Re-enter through real input and leave real flights/body live for terminal cleanup.
            Split(); for (int i = 0; i < 180 && player.DroneCooldown > 0; i++) Step();
            for (int i = 0; i < 60 && player.DroneShots == 38; i++) Step(new(Attack: true));
            Check(player.DroneShots > 38 && player.DroneBeams.Any(b => b.Owner != null && b.Lifespan > 0), "terminal cleanup starts with actual live private flights");
            Picture("cleanup-live", true);
            cases.Add(new { Kind = "Timeline", SimulationSteps = simulationSteps, SceneTicks = scene.FrameCount, FullScenePictures = pictures,
                ExtraHudOnlyPictures = hudOnlyPictures, StateLineWitnesses = stateHashes, MaximumMatchedNativeTrails = maxNativeTrails, MaximumBodyMeshes = maxBodyMeshes,
                player.DroneShots, player.DroneBursts, AnimationAdvanced = animated });

            void Step(Controls controls = default)
            {
                Bound(); SetKey(keyboard, Keys.B, controls.Morph); SetKey(keyboard, Keys.F, controls.Attack); SetKey(keyboard, Keys.Space, controls.Jump);
                scene.OnSimulationFrame(); simulationSteps++;
                Check(!NetSession.Active && player.Health > 0 && Finite(player.Position) && Finite(player.Speed), "finite living offline native simulation step");
                if (mobile != null && mobile.AnimInfo.Frame[0] != initialAnimation) animated = true;
            }
            void Split()
            {
                Check(!player.IsAltForm, "split starts as biped"); Step(new(Morph: true)); Check(player.Controls.Morph.IsPressed, "real native Morph edge observed");
                for (int i = 0; i < 120 && (!player.IsAltForm || player.IsMorphing); i++) Step(); Step();
                Check(player.IsAltForm && !player.IsMorphing && player.Flags2.TestFlag(PlayerFlags2.Halfturret), "native form owner reaches drone/body split");
            }
            State Capture() => new(scene.FrameCount, player.Position, player.Speed, player.Health, player.DroneBody.Health,
                player.IsAltForm, player.IsMorphing, player.IsUnmorphing, player.DroneFireState, player.DroneFireFrame, player.DroneCooldown,
                player.DroneShots, player.DroneDamageTotal, mobile?.AnimInfo.Frame[0] ?? -1, FlightWitness(player.DroneBeams));
            void Picture(string label, bool drone)
            {
                Bound(); State state = Capture(); scene.OnDrawFrame();
                var items = new[] { "_decalItems", "_nonDecalItems", "_translucentItems" }.SelectMany(n => (List<RenderItem>)Get(scene, n)!).Distinct().ToArray();
                var mobileIds = mobile?.Model.Meshes.Select(m => m.ListId).Where(id => id != 0).ToHashSet() ?? new HashSet<int>();
                var bodyIds = player.DroneBody.GetModels().SelectMany(m => m.Model.Meshes).Select(m => m.ListId).Where(id => id != 0).ToHashSet();
                int mobileMeshes = items.Count(i => i.Type == RenderItemType.Mesh && mobileIds.Contains(i.ListId));
                int bodyMeshes = items.Count(i => i.Type == RenderItemType.Mesh && bodyIds.Contains(i.ListId)); maxBodyMeshes = Math.Max(maxBodyMeshes, bodyMeshes);
                var active = player.DroneBeams.Where(b => b.Owner == player && b.Lifespan > 0).ToArray();
                int nativeTrails = items.Count(i => i.Type == RenderItemType.TrailSingle && active.Any(b =>
                    i.TextureBindingId == (int)Get(b, "_bindingId")! && (i.Transform.Row3.Xyz - b.ModBeamDrawBack).LengthSquared < .0000001f));
                maxNativeTrails = Math.Max(maxNativeTrails, nativeTrails);
                Check(scene.OnRenderFrame(), "actual Scene world/HUD render remains active"); pictures++;
                Check(GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError, "actual Scene native render is GL-clean");
                Check(Capture() == state, "full Scene drawing never advances drone simulation/health/animation/flight state");
                var reticle = (HudObjectInstance)Get(player, "_targetCircleInst")!;
                Pixels? hudStatus = null, hudInstructions = null; int reticleChanged = 0;
                if (drone)
                {
                    Check(mobileMeshes > 0, "actual native mobile Psycho Bit mesh submitted in player-camera picture");
                    Check(reticle.Enabled && reticle.BindingId > 0 && GL.IsTexture(reticle.BindingId)
                        && float.IsFinite(reticle.PositionX) && float.IsFinite(reticle.PositionY), "native drone reticle enabled with finite projection and actual texture");
                    var bounds = scene.GetHudObjectBounds(reticle, scale: Crosshair.Scale);
                    Check(float.IsFinite(bounds.Left) && float.IsFinite(bounds.Top) && bounds.Width > 0 && bounds.Height > 0,
                        "native drone reticle resolves finite positive pixel bounds");
                    byte[] on = HudOnly(1), off = HudOnly(0);
                    try
                    {
                        reticleChanged = Changed(on, off, scene.Size.X, scene.Size.Y, bounds.Left, bounds.Top, bounds.Width, bounds.Height);
                        Check(reticleChanged > 0, "native drone reticle contributes actual pixels under opacity control without simulation steps");
                        hudStatus = Region(on, scene.Size.X, scene.Size.Y, 0, scene.Size.Y * 128 / 192, scene.Size.X, scene.Size.Y * 14 / 192);
                        hudInstructions = Region(on, scene.Size.X, scene.Size.Y, 0, scene.Size.Y * 141 / 192, scene.Size.X, scene.Size.Y * 13 / 192);
                        Check(hudStatus.ColoredPixels > 0 && hudInstructions.ColoredPixels > 0, "actual native HUD owner emits nonempty state and instruction pixel bands");
                        stateHashes[label] = hudStatus.Sha256;
                    }
                    finally { Array.Clear(on); Array.Clear(off); Features.ReticleOpacity = 1; }
                    // Restore an ordinary whole-world picture after the diagnostic HUD-only pictures.
                    scene.OnDrawFrame(); Check(scene.OnRenderFrame(), "ordinary native Scene resumes after HUD-only diagnostic"); pictures++;
                    Check(GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError && Capture() == state, "extra pictures and opacity controls leave simulation unchanged and GL-clean");
                }
                DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
                Check(Capture() == state, "present and AfterRenderFrame never tick gameplay");
                cases.Add(new { Kind = "Picture", Label = label, SimulationTick = state.Tick, mobileMeshes, bodyMeshes, LiveFlights = active.Length,
                    MatchedNativeTrails = nativeTrails, QueuedEffects = items.Count(i => i.Type == RenderItemType.Particle),
                    NativeSingleParticles = (int)Get(scene, "_singleParticleCount")!, HudStatus = hudStatus, HudInstructions = hudInstructions, ReticleChangedPixels = reticleChanged,
                    player.DroneFireState, player.DroneFireFrame, player.DroneCooldown, player.DroneShots });
            }
            byte[] HudOnly(float opacity)
            {
                Bound(); Features.ReticleOpacity = opacity;
                GL.Disable(EnableCap.ScissorTest); GL.ClearColor(0, 0, 0, 0); GL.Clear(ClearBufferMask.ColorBufferBit);
                player.DrawHudObjects(); hudOnlyPictures++;
                int alignment = GL.GetInteger(GetPName.PackAlignment); byte[] pixels = new byte[checked(scene.Size.X * scene.Size.Y * 4)];
                try { GL.PixelStore(PixelStoreParameter.PackAlignment, 1); GL.ReadPixels(0, 0, scene.Size.X, scene.Size.Y, PixelFormat.Rgba, PixelType.UnsignedByte, pixels); }
                finally { GL.PixelStore(PixelStoreParameter.PackAlignment, alignment); }
                Check(GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError, "same native HUD owner pixel witness is GL-clean"); return pixels;
            }
        }
        finally
        {
            if (scene != null)
            {
                var player = scene.Players.Main; retainedPool = player.DroneBeams.ToArray();
                int[] textures = ((HashSet<int>)Get(scene, "_ownedTextures")!).ToArray();
                try { MomentumSceneTests.Release(scene); }
                finally
                {
                    Check(retainedPool.All(b => b.Owner == null && b.Equip == null && b.Effect == null && b.MuzzleEffect == null && b.Target == null && b.Lifespan == 0),
                        "teardown releases every retained private beam owner/effect/muzzle/target/equip");
                    Check(retainedPool.All(b => !ContainsEntity(scene, b)) && player.DroneBeams.Count == 0
                        && Get(player, "_droneModel") == null && Get(player, "_droneEquip") == null, "teardown unlinks all private flights and removes fallback owner references");
                    Check(((HashSet<int>)Get(scene, "_ownedTextures")!).Count == 0 && textures.All(id => !GL.IsTexture(id)),
                        "Scene unload releases actual scene-owned GL textures while context remains live");
                    Check(scene.PrivateCharacterGameplayUnsupported && !player.ScoutDroneEnabled, "teardown retires gameplay but retains Scene replay exclusion");
                    Check(GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError, "native Scene teardown is GL-clean");
                    cases.Add(new { Kind = "Teardown", RetainedPool = retainedPool.Length, RetiredSceneTextures = textures.Length,
                        PrivateOwnersRemaining = player.DroneBeams.Count, ReplayExclusionRetained = scene.PrivateCharacterGameplayUnsupported });
                }
            }
        }
    }

    // Separate real Scene keeps the original full primary773 acceptance intact.
    // This adds actual cached-ray rendering, not bot damage/source-model parity.
    private static void RunLaserScene(NativeWindow window, string room, List<object> cases, Action<string> stage)
    {
        Scene? scene = null; var keyboard = SyntheticInput.CreateKeyboard();
        int steps = 0, pictures = 0, extraPictures = 0, maximumTrails = 0, pixelWitnesses = 0;
        try
        {
            stage("LaserLoadOwnedNativeScene"); Bound();
            scene = new Scene(window.FramebufferSize, keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { });
            var plan = new LaunchPlan { Kind = LaunchKind.Offline, Hunter = Hunter.Weavel, RoomKey = room, Mode = GameMode.Battle,
                Bots = 0, IsPlaytest = true, OfflineCharacter = OfflineCharacterMode.TrooperScoutDronePractice,
                MatchRules = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle, TimeLimitSeconds = 0, PointGoal = 10000 } };
            plan.MatchRules.ApplyInitialConfiguration(scene.GameState); scene.AddPlayer(Hunter.Weavel);
            Check(OfflineCharacterGameplay.TryBind(scene, plan, out _), "laser Scene binds the actual zero-bot private plan before initialization");
            var player = scene.Players.Main;
            scene.AddRoom(room, GameMode.Battle, playerCount: 1); MphRead.Sound.Sfx.Load(scene); scene.OnLoad(); scene.OnResize();
            RenderOptions.CharacterModelReplacements = false; RenderOptions.ShowCustomCosmetics = false; Features.ReticleOpacity = 1;
            foreach (var control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            player.Controls.Shoot.Key = Keys.Enter; player.Controls.Morph.Key = Keys.B; player.Controls.AltAttack.Key = Keys.F;
            player.Controls.Jump.Key = Keys.Space; player.Controls.Zoom.Key = Keys.Z;
            player.Controls.MouseAim = false; player.Controls.KeyboardAim = true;
            SetKey(keyboard, Keys.Enter, true);
            for (int i = 0; i < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); i++) Step();
            SetKey(keyboard, Keys.Enter, false);
            Check(player.LoadFlags.TestFlag(LoadFlags.Spawned), "laser fixture uses real native shoot-to-spawn");
            scene.CameraSequences.Current?.End(); for (int i = 0; i < 240; i++) Step();
            Check(player.ModIsInPlay && !player.IsAltForm && player.ScoutDroneEnabled, "living native biped enters real laser fixture");
            Split();
            stage("LaserCompleteCachedTimeline");
            StartLaser(); int shotsBefore = player.DroneShots;
            for (int frame = 0; frame <= 133; frame++)
            {
                Step(); Check(player.DroneLaserProcessedFrame == frame, "actual simulation visits exact laser source frame" + frame);
                bool visible = frame >= 57 && frame <= 122;
                Check(player.DroneLaserOn == visible, "cached laser visibility has exact57..122 domain at frame" + frame);
                Check(player.DroneShots == shotsBefore && player.DroneFireState == 0, "laser does not start a primary volley in a draw or simulation step");
                if (visible)
                    Check(Finite(player.DroneLaserOrigin) && Finite(player.DroneLaserEnd)
                        && (player.DroneLaserEnd - player.DroneLaserOrigin).LengthSquared > .000001f,
                        "visible actual collision-bounded cached ray is finite and nonempty");
                else Check(player.DroneLaserOrigin == Vector3.Zero && player.DroneLaserEnd == Vector3.Zero,
                    "off-frame cached source geometry is cleared rather than rendered late");
                if (new[] { 56, 57, 87, 113, 117, 122, 123 }.Contains(frame)) Draw("laser-frame" + frame, visible, pixelControl: visible);
            }
            Check(player.DroneLaserFrame == -1 && player.DroneLaserCooldown == 600
                && player.DroneLaserOpportunities == 15 && player.DroneLaserSoundCount == 1,
                "full native laser ends134 steps with600 wait,15 opportunities and one shot cue");
            Check(pixelWitnesses > 0 && maximumTrails == 2, "actual source TrailSingle pair contributes pixels through the shared native renderer");

            stage("LaserMergeCancellation");
            DrainLaserWait(); StartLaser();
            for (int i = 0; i < 58; i++) Step();
            Check(player.DroneLaserProcessedFrame == 57 && player.DroneLaserOn, "merge cancellation begins with an actual visible cached ray");
            Draw("laser-before-merge", true, pixelControl: false);
            Step(new(Morph: true));
            Check(!player.DroneLaserOn && player.DroneLaserFrame == -1, "native Morph start immediately cancels actual laser ownership");
            for (int i = 0; i < 120 && (player.IsAltForm || player.IsUnmorphing); i++) Step();
            Check(!player.IsAltForm && !player.IsUnmorphing, "native merge completes without forged form or health");
            Draw("laser-after-merge", false, pixelControl: false);

            stage("LaserNativeDeathCancellation");
            Split(); DrainLaserWait(); StartLaser();
            for (int i = 0; i < 58; i++) Step();
            Check(player.DroneLaserProcessedFrame == 57 && player.DroneLaserOn, "native death cancellation starts with a real visible laser");
            Draw("laser-before-death", true, pixelControl: false);
            player.TakeDamage(9999, DamageFlags.Death | DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln | DamageFlags.NoSfx, null, null);
            Check(player.Health == 0 && !player.DroneLaserOn, "actual native death immediately fences the cached laser from drawing");
            for (int i = 0; i < 8; i++) Step(requireAlive: false);
            Check(player.DroneLaserFrame == -1 && player.DroneLaserOrigin == Vector3.Zero && player.DroneLaserEnd == Vector3.Zero,
                "native death simulation clears cached laser frame and ray ownership");
            Draw("laser-after-death", false, pixelControl: false);
            cases.Add(new { Kind = "LaserTimeline", SimulationSteps = steps, FullScenePictures = pictures,
                ExtraOpacityControlPictures = extraPictures, MaximumMatchedLaserTrails = maximumTrails,
                PixelWitnesses = pixelWitnesses, player.DroneLasers, player.DroneLaserOpportunities,
                player.DroneLaserHitAttempts, player.DroneLaserSoundCount,
                Scope = "real zero-bot native Scene, keyboard Zoom/Morph and native death; no opponent damage/cross-platform/source parity claim" });

            void Step(Controls controls = default, bool requireAlive = true)
            {
                Bound(); SetKey(keyboard, Keys.B, controls.Morph); SetKey(keyboard, Keys.F, controls.Attack);
                SetKey(keyboard, Keys.Space, controls.Jump); SetKey(keyboard, Keys.Z, controls.Zoom);
                scene.OnSimulationFrame(); steps++;
                Check(!NetSession.Active && (!requireAlive || player.Health > 0) && Finite(player.Position) && Finite(player.Speed),
                    "finite native laser simulation has exact offline and living/death owner");
            }
            void Split()
            {
                Check(!player.IsAltForm, "laser split starts through native biped owner");
                Step(new(Morph: true)); Check(player.Controls.Morph.IsPressed, "laser fixture observes real Morph edge");
                for (int i = 0; i < 120 && (!player.IsAltForm || player.IsMorphing); i++) Step(); Step();
                Check(player.IsAltForm && !player.IsMorphing && player.DroneBody.Health > 0,
                    "actual native split owns the living planted body before laser input");
            }
            void DrainLaserWait()
            {
                for (int i = 0; i < 601 && player.DroneLaserCooldown > 0; i++) Step();
                Check(player.DroneLaserCooldown == 0 && player.DroneLaserFrame == -1, "only native simulation exhausts laser wait");
            }
            void StartLaser()
            {
                int count = player.DroneLasers; Step(new(Zoom: true));
                Check(player.Controls.Zoom.IsPressed && player.DroneLasers == count + 1 && player.DroneLaserFrame == 0
                    && player.DroneLaserProcessedFrame == -1 && !player.DroneLaserOn,
                    "real Zoom edge starts exactly one actual windup without a premature cached ray");
            }
            string Capture()
            {
                // Only the private in-memory digest leaves this method. Include
                // simulation, HP, animations, flights, cached endpoints and all
                // public counters; no original pose/geometry arrays are exported.
                static string Bits(Vector3 v) => BitConverter.SingleToInt32Bits(v.X) + ","
                    + BitConverter.SingleToInt32Bits(v.Y) + "," + BitConverter.SingleToInt32Bits(v.Z);
                string value = scene.FrameCount + "|" + player.Health + "|" + player.DroneBody.Health + "|"
                    + Bits(player.Position) + "|" + Bits(player.Speed) + "|" + player.IsAltForm + "|" + player.IsMorphing + "|" + player.IsUnmorphing + "|"
                    + player.DroneFireState + "|" + player.DroneFireFrame + "|" + player.DroneCooldown + "|" + player.DroneShots + "|"
                    + player.DroneDamageTotal + "|" + player.DroneLaserFrame + "|" + player.DroneLaserProcessedFrame + "|"
                    + player.DroneLaserCooldown + "|" + player.DroneLaserOn + "|" + player.DroneLaserPattern + "|" + BitConverter.SingleToInt32Bits(player.DroneLaserDistance) + "|"
                    + Bits(player.DroneLaserOrigin) + "|" + Bits(player.DroneLaserEnd) + "|" + player.DroneLasers + "|" + player.DroneLaserOpportunities + "|"
                    + player.DroneLaserHitAttempts + "|" + player.DroneLaserSoundCount + "|" + FlightWitness(player.DroneBeams);
                foreach (var model in player.GetModels()) value += "|" + string.Join(",", model.AnimInfo.Frame);
                return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
            }
            void Draw(string label, bool visible, bool pixelControl)
            {
                Bound(); string before = Capture(); scene.OnDrawFrame();
                var items = new[] { "_decalItems", "_nonDecalItems", "_translucentItems" }
                    .SelectMany(n => (List<RenderItem>)Get(scene, n)!).Distinct().ToArray();
                int binding = (int)Get(player, "_trailBindingId1")!;
                var trails = items.Where(i => i.Type == RenderItemType.TrailSingle
                    && i.Diffuse == new Vector3(1, .15f, .1f) && i.TextureBindingId == binding).ToArray();
                Check(trails.Length == (visible ? 2 : 0), "actual native queue contains exactly two cached laser crossed strips iff visible: " + label);
                maximumTrails = Math.Max(maximumTrails, trails.Length);
                if (visible)
                {
                    Vector3 span = player.DroneLaserEnd - player.DroneLaserOrigin;
                    Check(binding > 0 && GL.IsTexture(binding), "laser strips use actual scene-owned native trail texture");
                    Check(trails.All(t => !t.Lighting && t.ItemCount == 8 && t.Points.Length >= 8
                        && t.Points.Take(8).All(Finite) && (t.Transform.Row3.Xyz - player.DroneLaserOrigin).LengthSquared < .0000001f
                        && ((t.Points[1] + t.Points[3]) / 2 - span).LengthSquared < .0000001f
                        && (t.Points[5] + t.Points[7]).LengthSquared < .0000001f),
                        "submitted native strips exactly consume cached collision-bounded endpoints and finite crossed geometry");
                }
                Check(scene.OnRenderFrame(), "actual world/HUD renderer consumes cached laser picture"); pictures++;
                Check(GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError && Capture() == before,
                    "native laser picture is GL-clean and cannot tick, damage, advance animation or move cached ray");
                int changed = 0; string? pixelSha = null;
                if (pixelControl)
                {
                    byte[] on = ReadPixels(); byte[] off = Array.Empty<byte>();
                    float[] alphas = trails.Select(t => t.Alpha).ToArray();
                    try
                    {
                        foreach (var trail in trails) trail.Alpha = 0;
                        Check(scene.OnRenderFrame(), "same cached native frame supports explicitly render-only zero-opacity control"); extraPictures++;
                        off = ReadPixels();
                        changed = Changed(on, off, scene.Size.X, scene.Size.Y, 0, 0, scene.Size.X, scene.Size.Y);
                        Check(changed > 0, "cached actual laser strips make a nonzero framebuffer contribution: " + label);
                        pixelWitnesses++;
                        pixelSha = Convert.ToHexStringLower(SHA256.HashData(on));
                    }
                    finally
                    {
                        for (int i = 0; i < trails.Length; i++) trails[i].Alpha = alphas[i];
                        Array.Clear(on); Array.Clear(off);
                    }
                    Check(Capture() == before, "extra actual laser opacity-control picture preserves simulation/HP/cached ray state");
                    Check(scene.OnRenderFrame(), "actual native frame restored after render-only opacity control"); extraPictures++;
                }
                DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
                Check(GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError && Capture() == before,
                    "laser present/AfterRenderFrame and all extra pictures remain GL-clean and simulation immutable");
                cases.Add(new { Kind = "LaserPicture", Label = label, SceneTick = scene.FrameCount,
                    player.DroneLaserProcessedFrame, player.DroneLaserOn, MatchedTrails = trails.Length, ChangedPixels = changed,
                    PixelSha256 = pixelSha, RenderOnlyExtraPictures = pixelControl ? 2 : 0 });
            }
            byte[] ReadPixels()
            {
                int alignment = GL.GetInteger(GetPName.PackAlignment); var bytes = new byte[checked(scene.Size.X * scene.Size.Y * 4)];
                try { GL.PixelStore(PixelStoreParameter.PackAlignment, 1); GL.ReadPixels(0, 0, scene.Size.X, scene.Size.Y, PixelFormat.Rgba, PixelType.UnsignedByte, bytes); }
                finally { GL.PixelStore(PixelStoreParameter.PackAlignment, alignment); }
                Check(GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError, "in-memory native laser framebuffer witness is GL-clean"); return bytes;
            }
        }
        finally
        {
            if (scene != null)
            {
                var player = scene.Players.Main; var pool = player.DroneBeams.ToArray();
                int[] textures = ((HashSet<int>)Get(scene, "_ownedTextures")!).ToArray();
                try { MomentumSceneTests.Release(scene); }
                finally
                {
                    Check(!player.DroneLaserOn && player.DroneLaserFrame == -1 && player.DroneLaserOrigin == Vector3.Zero
                        && player.DroneLaserEnd == Vector3.Zero && !player.ScoutDroneEnabled,
                        "actual Scene teardown clears all cached laser ownership and geometry");
                    Check(pool.All(b => b.Owner == null && b.Lifespan == 0 && !ContainsEntity(scene, b))
                        && player.DroneBeams.Count == 0 && Get(player, "_droneModel") == null,
                        "laser Scene teardown retires native flight/fallback ownership");
                    Check(((HashSet<int>)Get(scene, "_ownedTextures")!).Count == 0 && textures.All(id => !GL.IsTexture(id)),
                        "laser Scene releases actual texture resources with GL context alive");
                    Check(GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError, "laser Scene cleanup remains GL-clean");
                    cases.Add(new { Kind = "LaserTeardown", RetiredSceneTextures = textures.Length,
                        CachedRayRemaining = player.DroneLaserOn, PrivateFlightsRemaining = player.DroneBeams.Count });
                }
            }
        }
    }

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
