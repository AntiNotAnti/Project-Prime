using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Real offline local controls, native missile allocation/flight and room collision.
// Setup selects match rules and bindings only; no body, aim, velocity, health,
// projectile, immunity or transport ownership fields are seeded.
internal static class RocketLaunchSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly Action<MouseState, Vector2> SetPointer = typeof(MouseState)
        .GetProperty("Position", Hidden | BindingFlags.Public)!.SetMethod!.CreateDelegate<Action<MouseState, Vector2>>();
    private static int _checks;
    private sealed record Tick(int Frame, float[] Body, float[] Speed, int Health, bool Protected,
        bool Standing, bool Jump, bool Shoot, bool Crouched, float[]? Projectile, bool Collided);
    private sealed record Result(string Hunter, string Model, int FireTick, int JumpTick, bool Crouch,
        bool ProtectedAtStart, bool ProtectedAtFire, int EmittedTick, int ImpactTick,
        bool ImmediateImpact, bool FloorImpact, int Damage, float Rise, float MaximumYSpeed,
        float[] Spawn, float[] Impact, float SplashRadius, int SplashCurve, Tick[] Ticks);
    private static float[] V(Vector3 v) => new[] { v.X, v.Y, v.Z };
    private static void Require(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException(label); }

    internal static int Run(string? outputPath)
    {
        var rows = new List<object>(); string? failure = null;
        try
        {
            _checks = 0;
            Require(!NetSession.Active && !DemoPlayback.IsActive && !DemoRecorder.IsRecording,
                "fresh offline non-recording launch process");
            Require(ServerSim.Available(out string reason), reason);
            Require(!PointerInput.StylusMode && !PointerDevice.Active && !InputSettings.InvertMouseY
                && float.IsFinite(InputSettings.MouseSensitivity) && InputSettings.MouseSensitivity > 0,
                "ordinary noninverted raw mouse input");
            Headless.Enter();
            foreach (MovementModel model in new[] { MovementModel.Quake3, MovementModel.Traversal })
            foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
                Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel })
            {
                Scene scene = Load(hunter, model, out var keyboard);
                try
                {
                    Result first = Capture(scene, keyboard, fireTick: 0, jumpTick: 0);
                    Require(first.ProtectedAtStart && first.ProtectedAtFire,
                        hunter + "/" + model + " first genuine missile starts inside the native match protection window");
                    Settle(scene, keyboard);
                    Result repeat = Capture(scene, keyboard, fireTick: 0, jumpTick: 0);
                    Require(!repeat.ProtectedAtStart, "successful first shot surrenders protection for repeat");
                    rows.Add(new { scenario = "first-protected-and-repeat", first, repeat });
                }
                finally { MomentumSceneTests.Release(scene); }
            }
            foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum,
                MovementModel.Quake3, MovementModel.Traversal })
            foreach (var schedule in new[] { (Fire: 0, Jump: -1), (Fire: 0, Jump: 1),
                (Fire: 1, Jump: 0), (Fire: 3, Jump: 0), (Fire: 6, Jump: 0) })
            {
                Scene scene = Load(Hunter.Samus, model, out var keyboard);
                try
                {
                    // Let the actual timer expire; no immunity field mutation.
                    Settle(scene, keyboard);
                    Result result = Capture(scene, keyboard, schedule.Fire, schedule.Jump);
                    Require(!result.ProtectedAtStart, "timing comparison starts after actual protection expiry");
                    rows.Add(new { scenario = "native-launch-timing", result });
                }
                finally { MomentumSceneTests.Release(scene); }
            }
            {
                Scene scene = Load(Hunter.Samus, MovementModel.Traversal, out var keyboard);
                try
                {
                    Settle(scene, keyboard);
                    SetKey(keyboard, Keys.LeftControl, true);
                    for (int i = 0; i < 3; i++) scene.OnSimulationFrame();
                    Require(scene.Players.Main!.ModCrouched, "genuine crouch accepted before downward launch");
                    rows.Add(new { scenario = "crouched-floor-launch", result = Capture(scene, keyboard, 0, -1, crouch: true) });
                }
                finally { MomentumSceneTests.Release(scene); }
            }
        }
        catch (Exception error) { failure = error.ToString(); Console.Error.WriteLine("ROCKET LAUNCH SCENE FAIL " + error); }
        if (outputPath != null)
        {
            string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { status = failure == null ? "PASS" : "FAIL",
                failure, checks = _checks, protocol = NetConfig.ProtocolVersion,
                scope = "Actual offline local human in multiplayer Battle rules; synthetic keyboard/raw mouse, native spawn/intro skip and missile lifecycle; no fabricated projectile/body/velocity/health/immunity, no bot AI or transport qualification",
                conditions = "MP3 PROVING GROUND; protection enabled before spawn; first protected/repeat for every native Hunter in Quake3 and Traversal; Samus timing controls in all four movement modes; Traversal crouch", rows },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        if (failure != null) return 1;
        Console.WriteLine($"ROCKET LAUNCH SCENE PASS {_checks} assertions"); return 0;
    }

    private static Scene Load(Hunter hunter, MovementModel model, out KeyboardState keyboard)
    {
        const string room = "MP3 PROVING GROUND";
        Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
        keyboard = SyntheticInput.CreateKeyboard(); var mouse = SyntheticInput.CreateMouse();
        Scene? scene = null;
        try
        {
            scene = new Scene(new Vector2i(256, 192), keyboard, mouse, _ => { }, () => { }, lifetimeOwned: s => scene = s);
            scene.AddPlayer(hunter); scene.AddRoom(room, GameMode.Battle, playerCount: 1);
            new MatchDefinition { RoomKey = room, Mode = GameMode.Battle, MovementModel = model,
                SpawnProtection = true, TimeLimitSeconds = 0, PointGoal = 10000 }.ApplyInitialConfiguration(scene.GameState);
            scene.OnLoad();
            var mode = typeof(Scene).GetField("_inputMode", Hidden)!;
            mode.SetValue(scene, Enum.Parse(mode.FieldType, "All"));
            scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
            var player = scene.Players.Items[0];
            foreach (var control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            player.Controls.Jump.Key = Keys.Space; player.Controls.Shoot.Key = Keys.Enter;
            player.Controls.Missile.Key = Keys.M; player.Controls.Crouch.Key = Keys.LeftControl;
            player.Controls.MouseAim = true; player.Controls.KeyboardAim = false;
            Require(!player.Controls.InvertAimY, "noninverted native aim binding");
            SetKey(keyboard, Keys.Enter, true);
            for (int i = 0; i < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); i++) scene.OnSimulationFrame();
            SetKey(keyboard, Keys.Enter, false);
            Require(player.LoadFlags.TestFlag(LoadFlags.Spawned), "genuine shoot-to-spawn activates local body");
            scene.CameraSequences.Current?.End();
            // Weapon selection and pitch are real input. Bounded raw pointer
            // deltas ask for steep aim; native sensitivity and pitch bounds apply.
            for (int i = 0; i < 45; i++)
            {
                SetKey(keyboard, Keys.M, i == 2);
                if (i >= 3 && i < 15 && player.ModGunVector.Y > -.96f)
                    SetPointer(mouse, mouse.Position + new Vector2(0, 120 / InputSettings.MouseSensitivity));
                scene.OnSimulationFrame();
            }
            Require(!NetSession.Active && !player.IsBot && player.ModIsInPlay && player.Health > 0
                && player.CurrentWeapon == BeamType.Missile && player.ModGunVector.Y < -.9f,
                $"actual local living missile owner aims downward through production input hunter={hunter} model={model} active={NetSession.Active} bot={player.IsBot} inPlay={player.ModIsInPlay} hp={player.Health} weapon={player.CurrentWeapon} gun={player.ModGunVector} mouse={mouse.Position} protected={player.ModMatchSpawnProtectionActive} flags={player.Flags1}/{player.Flags2}");
            Require(player.Flags1.TestFlag(PlayerFlags1.Standing), "real collision establishes stationary floor support");
            return scene;
        }
        catch { if (scene != null) MomentumSceneTests.Release(scene); throw; }
    }

    private static void Settle(Scene scene, KeyboardState keyboard)
    {
        SetKey(keyboard, Keys.Enter, false); SetKey(keyboard, Keys.Space, false); SetKey(keyboard, Keys.LeftControl, false);
        for (int i = 0; i < 210; i++) scene.OnSimulationFrame();
        Require(scene.Players.Main is { Health: > 0 } player && player.Flags1.TestFlag(PlayerFlags1.Standing),
            "real unforced body survives flight and settles on collision floor");
    }

    private static Result Capture(Scene scene, KeyboardState keyboard, int fireTick, int jumpTick, bool crouch = false)
    {
        var player = scene.Players.Main!;
        bool protectedStart = player.ModMatchSpawnProtectionActive, protectedFire = false;
        float initialY = player.Position.Y, peakY = initialY, maximumYSpeed = player.Speed.Y;
        int health = player.Health, emitted = -1, impacted = -1; bool floorImpact = false;
        BeamProjectileEntity? shot = null; Vector3 spawn = default, impact = default;
        float radius = 0; int curve = -1; var ticks = new List<Tick>();
        for (int i = 0; i < 100; i++)
        {
            SetKey(keyboard, Keys.Enter, i == fireTick); SetKey(keyboard, Keys.Space, i == jumpTick);
            SetKey(keyboard, Keys.LeftControl, crouch);
            var before = player.EquipInfo.Beams.Select(b => b.Lifespan).ToArray();
            if (i == fireTick) protectedFire = player.ModMatchSpawnProtectionActive;
            scene.OnSimulationFrame();
            if (shot == null)
                for (int b = 0; b < player.EquipInfo.Beams.Length; b++)
                {
                    var candidate = player.EquipInfo.Beams[b];
                    if (candidate.Owner == player && candidate.Beam == BeamType.Missile
                        && candidate.Lifespan > before[b] + .001f)
                    {
                        Require(shot == null, "one press emits exactly one actual native missile");
                        shot = candidate; emitted = i; spawn = candidate.SpawnPosition;
                        radius = candidate.SplashRadius; curve = candidate.SplashDamageType;
                    }
                }
            if (shot != null && impacted < 0 && shot.Flags.TestFlag(BeamFlags.Collided))
            {
                impacted = i; impact = shot.Position;
                CollisionResult floor = default;
                floorImpact = CollisionDetection.CheckBetweenPoints(impact + Vector3.UnitY * .2f,
                    impact - Vector3.UnitY * .3f, TestFlags.Beams, scene, ref floor) && floor.Plane.Y > .5f;
            }
            peakY = MathF.Max(peakY, player.Position.Y); maximumYSpeed = MathF.Max(maximumYSpeed, player.Speed.Y);
            Require(float.IsFinite(player.Position.LengthSquared) && float.IsFinite(player.Speed.LengthSquared),
                "native launch and collision keep finite body velocity");
            ticks.Add(new(i, V(player.Position), V(player.Speed), player.Health, player.ModMatchSpawnProtectionActive,
                player.Flags1.TestFlag(PlayerFlags1.Standing), player.Controls.Jump.IsDown,
                player.Controls.Shoot.IsDown, player.ModCrouched, shot == null ? null : V(shot.Position),
                shot?.Flags.TestFlag(BeamFlags.Collided) == true));
        }
        Require(shot != null && emitted == fireTick, "requested genuine input press emits on its actual fire tick");
        Require(impacted >= emitted && floorImpact, "actual authored missile flight terminates at the room floor");
        Require(!player.ModMatchSpawnProtectionActive, "successful genuine shot releases match protection");
        bool currentBlastProfile = scene.GameState.MovementModel is MovementModel.Quake3 or MovementModel.Traversal
            && MovementModelRules.ProfileVersion(scene.GameState.MovementModel) >= 3;
        if (currentBlastProfile && jumpTick == 0 && fireTick is 0 or 1)
        {
            Require(health - player.Health == 24,
                "current capsule policy retains the uncharged native missile's exact 24HP self cost");
            Require(maximumYSpeed > Fixed.ToFloat(player.Values.JumpSpeed) + .05f,
                "simultaneous and one-tick delayed floor missiles produce meaningful lift beyond the native jump");
            Require(impacted > 0 && ticks[impacted].Speed[1] > ticks[impacted - 1].Speed[1] + .05f,
                "meaningful current-profile lift occurs at the actual missile floor impact");
        }
        Console.WriteLine($"ROCKET LAUNCH {player.Hunter}/{scene.GameState.MovementModel} fire={fireTick} jump={jumpTick} protected={protectedFire} impact={impacted} damage={health-player.Health} peakY={maximumYSpeed:R}");
        return new(player.Hunter.ToString(), scene.GameState.MovementModel.ToString(), fireTick, jumpTick, crouch,
            protectedStart, protectedFire, emitted, impacted, impacted == emitted, floorImpact, health-player.Health,
            peakY-initialY, maximumYSpeed, V(spawn), V(impact), radius, curve, ticks.ToArray());
    }
}
