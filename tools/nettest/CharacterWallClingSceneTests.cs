using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Owned-cartridge offline CPU traversal. The main search uses only native
// keyboard controls and fixed OnSimulationFrame steps after real initialization.
// The separately identified fall arm uses native Spawn at an observed real wall
// to obtain a reproducible no-jump fall; no cling fields or hooks are written.
// No art exports, GL, networking, recording, career save or replay qualification.
internal static class CharacterWallClingSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private readonly record struct Input(bool Forward, bool Jump, bool Turn);
    private readonly record struct Tick(Vector3 Position, Vector3 Speed, bool Cling, bool JumpPressed);
    private sealed record Wall(Vector3 Position, Vector3 Normal);
    private sealed record FallCapture(Wall Wall, int Steps, float Drop);
    private sealed record Capture(List<Input> Inputs, List<Tick> Ticks, List<Wall> Walls, int Clings, int Jumps, int Timeouts, float Drift);
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static void Check(bool value, string label, bool verbose = true)
    {
        if (!value) throw new InvalidOperationException(label);
        _checks++; if (verbose) Console.WriteLine("WALL CLING SCENE PASS " + label);
    }
    private static void Apply(KeyboardState keyboard, Input input)
    {
        SetKey(keyboard, Keys.W, input.Forward); SetKey(keyboard, Keys.Space, input.Jump); SetKey(keyboard, Keys.E, input.Turn);
    }
    private static Scene Load(bool trooper, string room, out KeyboardState keyboard)
    {
        Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
        keyboard = SyntheticInput.CreateKeyboard(); Scene? scene = null;
        try
        {
            scene = new Scene(new Vector2i(256, 192), keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { }, lifetimeOwned: value => scene = value);
            var plan = new LaunchPlan { Kind = LaunchKind.Offline, Hunter = Hunter.Weavel, RoomKey = room,
                Mode = GameMode.Battle, Bots = 0, IsPlaytest = true,
                OfflineCharacter = trooper ? OfflineCharacterMode.TrooperWallClingPractice : OfflineCharacterMode.Native,
                MatchRules = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle, TimeLimitSeconds = 0, PointGoal = 10000 } };
            plan.MatchRules.ApplyInitialConfiguration(scene.GameState);
            scene.AddPlayer(Hunter.Weavel);
            var player = scene.Players.Items[0];
            Check(!player.IsBot, "exact native local Weavel owner created");
            Check(OfflineCharacterGameplay.TryBind(scene, plan, out string bindReason), "actual offline LaunchPlan admission and preinitialize binding seam " + bindReason);
            Check(player.ModCharacterGameplay == (trooper ? CharacterGameplayRules.TrooperV1 : CharacterGameplayRules.Native)
                && scene.PrivateCharacterGameplayUnsupported == trooper,
                "actual offline plan selects exact player rule and private replay exclusion");
            scene.AddRoom(room, GameMode.Battle, playerCount: 1);
            scene.OnLoad();
            var inputMode = typeof(Scene).GetField("_inputMode", Hidden)!;
            inputMode.SetValue(scene, Enum.Parse(inputMode.FieldType, "All"));
            scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
            foreach (var control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            player.Controls.MoveUp.Key = Keys.W; player.Controls.Jump.Key = Keys.Space;
            player.Controls.AimRight.Key = Keys.E; player.Controls.Shoot.Key = Keys.Enter;
            player.Controls.MouseAim = false; player.Controls.KeyboardAim = true;
            SetKey(keyboard, Keys.Enter, true);
            for (int i = 0; i < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); i++) scene.OnSimulationFrame();
            SetKey(keyboard, Keys.Enter, false);
            Check(player.LoadFlags.TestFlag(LoadFlags.Spawned), "native shoot-to-spawn path completed");
            scene.CameraSequences.Current?.End();
            for (int i = 0; i < 120; i++) scene.OnSimulationFrame();
            Check(player.ModIsInPlay && player.Health > 0 && !player.IsAltForm && !NetSession.Active, "fully initialized living offline native biped");
            return scene;
        }
        catch { if (scene != null) Release(scene); throw; }
    }
    private static void Release(Scene scene)
    {
        var commit = PostMatchCommitContext.Capture(scene.GameState, 0, SaveWhen.Never, 0);
        try { scene.DoCleanup(); }
        finally
        {
            try { scene.UnloadGl(); }
            finally { commit.Commit(); commit.Release(); ForegroundSceneBinding.TryRelease(commit); }
        }
    }

    internal static int Run(string assets, string? outputPath, string room = "MP3 PROVING GROUND")
    {
        try
        {
            _checks = 0;
            Check(!NetSession.Active && !DemoPlayback.IsActive, "fresh offline non-replay owner");
            Paths.SetPath(Ver.AMHE1, Path.GetFullPath(assets)); Paths.MphKey = Ver.AMHE1;
            Check(ServerSim.Available(out string reason), "owned AMHE1 filesystem available " + reason);
            Headless.Enter();
            Capture trooper = Search(room);
            Check(trooper.Clings >= 2 && trooper.Jumps >= 1 && trooper.Timeouts >= 1,
                "real controls produce jump-admitted cling, native wall kick and full hold timeout");
            NativeComparison(room, trooper);
            var fall = Fall(room, trooper.Walls);
            float nativeFallDrop = NativeFall(room, fall);
            if (outputPath != null)
            {
                string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { scope = "owned AMHE1 offline CPU initialized Scene; real native keyboard controls/collision; native Spawn relocation in separate no-jump fall arm; no art export, GL, network, recording, career or replay acceptance",
                    room, checks = _checks, trooper.Clings, trooper.Jumps, trooper.Timeouts, maximumHoldDrift = trooper.Drift,
                    mainSearchTicks = trooper.Ticks.Count, sameInputNativeTicks = trooper.Ticks.Count, observedWalls = trooper.Walls.Count, fall = new { startingPlacement = "public native Spawn at observed wall+2.5Y+0.15normal", simulationSteps = fall.Steps, actualDrop = fall.Drop, jumpPressed = false, nativeFallDrop } }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"WALL CLING SCENE PASS {_checks} assertions; actual owned arena input/collision, no graphics/network/source-character parity claim.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("WALL CLING SCENE FAIL " + ex); return 1; }
    }

    private static Capture Search(string room)
    {
        Scene scene = Load(true, room, out var keyboard);
        try
        {
            var p = scene.Players.Main; var inputs = new List<Input>(); var ticks = new List<Tick>(); var walls = new List<Wall>();
            int pushed = 0, walked = 0, turning = 0, clingCount = 0, held = 0, settle = 0;
            bool wasClinging = false, previousJump = false; Vector3 anchor = Vector3.Zero, normal = Vector3.Zero;
            float drift = 0; int kickFollow = 0, fallFollow = 0; Vector3 kickOrigin = default; float kickApex = 0, timeoutY = 0;
            for (int frame = 0; frame < 12000; frame++)
            {
                bool standing = p.Flags1.TestFlag(PlayerFlags1.Standing);
                bool jump = false, forward = true, turn = false;
                if (p.WallClinging)
                {
                    // First observed wall must time out. Second must wall jump.
                    jump = clingCount % 2 == 0 && held >= 36 && !previousJump;
                }
                else if (settle > 0)
                {
                    settle--; forward = false;
                }
                else if (turning > 0)
                { turning--; turn = true; forward = false; }
                else if (standing && p.Flags1.TestFlag(PlayerFlags1.CollidingLateral))
                {
                    pushed++; walked = 0;
                    jump = pushed >= 6 && !previousJump;
                    if (pushed > 90) { turning = 20; pushed = 0; }
                }
                else if (standing)
                {
                    pushed = 0;
                    if (++walked > 240) { turning = 14; walked = 0; }
                }
                var input = new Input(forward, jump, turn); Apply(keyboard, input); previousJump = jump;
                int beforeJumps = p.WallJumps, beforeTimeouts = p.WallClingTimeouts;
                scene.OnSimulationFrame(); inputs.Add(input);
                CheckTick(p);
                ticks.Add(new Tick(p.Position, p.Speed, p.WallClinging, p.Controls.Jump.IsPressed));
                if (p.WallClinging && !wasClinging)
                {
                    clingCount++; held = 0; anchor = p.Position; normal = p.WallClingNormal;
                    walls.Add(new Wall(anchor, normal));
                    Check(p.WallClingStartRose && p.WallClingTimer == 150, "real native jump note admits actual arena wall with timer150");
                    Console.WriteLine($"WALL CLING SCENE observed wall{clingCount} at tick{frame}, timer={p.WallClingTimer}");
                }
                else if (p.WallClinging)
                {
                    held++;
                    Check(p.WallClingTimer == 150 - held, "real fixed-step hold timer follows exact150 countdown", verbose: false);
                    drift = MathF.Max(drift, (p.Position - anchor).Length);
                    Check((p.Position - anchor).Length < .03f && p.Speed.Length < .0001f, "real input/gravity/collision hold remains stationary within native pushout tolerance", verbose: false);
                }
                if (p.WallJumps > beforeJumps)
                {
                    Check(p.Controls.Jump.IsPressed && p.Speed.Y > 0 && Vector3.Dot(p.Speed, normal) > .1f,
                        "real pressed jump causes upward outward native wall kick");
                    kickOrigin = anchor; kickApex = p.Position.Y; kickFollow = 12; pushed = 0;
                }
                if (p.WallClingTimeouts > beforeTimeouts)
                {
                    Check(held == 149 && p.WallClingLongest == 150 && !p.WallClinging,
                        "actual arena hold exits exactly150 subsequent simulation steps");
                    timeoutY = p.Position.Y; fallFollow = 30; settle = 30; pushed = 0;
                }
                if (kickFollow > 0)
                {
                    kickApex = MathF.Max(kickApex, p.Position.Y);
                    if (--kickFollow == 0) Check(Vector3.Dot(p.Position - kickOrigin, normal) >= .2f && kickApex >= kickOrigin.Y + .2f,
                        "12 real ticks after kick body moved outward and upward");
                }
                if (fallFollow > 0 && --fallFollow == 0)
                    Check(p.Flags1.TestFlag(PlayerFlags1.Grounded) || p.Position.Y < timeoutY - .15f,
                        "native gravity resumes and body falls or lands after timeout");
                wasClinging = p.WallClinging;
                if (p.WallJumps > 0 && p.WallClingTimeouts > 0 && kickFollow == 0 && fallFollow == 0)
                    return new Capture(inputs, ticks, walls, p.WallClings, p.WallJumps, p.WallClingTimeouts, drift);
                if (frame % 1200 == 1199) Console.WriteLine($"WALL CLING SCENE search tick{frame + 1}: clings={p.WallClings}, jumps={p.WallJumps}, timeouts={p.WallClingTimeouts}, grounded={p.Flags1.TestFlag(PlayerFlags1.Grounded)}");
            }
            throw new InvalidOperationException($"No complete real-control wall traversal after12000 steps: clings={p.WallClings}, jumps={p.WallJumps}, timeouts={p.WallClingTimeouts}.");
        }
        finally { Release(scene); }
    }
    private static void CheckTick(PlayerEntity p)
    {
        if (!p.ModIsInPlay || p.Health == 0 || !float.IsFinite(p.Position.Y) || !float.IsFinite(p.Speed.Y))
            throw new InvalidOperationException("Actual traversal lost living finite local biped owner.");
        if (p.WallClingChainCount > 3) throw new InvalidOperationException("Actual traversal exceeded literal3 airborne chain bound.");
    }
    private static void NativeComparison(string room, Capture reference)
    {
        Scene scene = Load(false, room, out var keyboard);
        try
        {
            var p = scene.Players.Main; int common = reference.Ticks.FindIndex(tick => tick.Cling);
            bool jumpObserved = false;
            for (int i = 0; i < reference.Inputs.Count; i++)
            {
                Apply(keyboard, reference.Inputs[i]); scene.OnSimulationFrame(); CheckTick(p);
                jumpObserved |= p.Controls.Jump.IsPressed && p.Speed.Y > 0;
                Check(!p.WallClinging && p.WallClings == 0 && p.ModCharacterGameplay == CharacterGameplayRules.Native,
                    "same recorded real-control schedule leaves default Weavel native", verbose: false);
                if (i < common) Check((p.Position - reference.Ticks[i].Position).Length < .00001f && (p.Speed - reference.Ticks[i].Speed).Length < .00001f,
                    "default native and Trooper trajectories match before first cling transition", verbose: false);
            }
            Check(jumpObserved, "native comparison observes actual accepted ordinary jump");
        }
        finally { Release(scene); }
    }
    private static FallCapture Fall(string room, List<Wall> walls)
    {
        foreach (Wall wall in walls)
        {
            Scene scene = Load(true, room, out var keyboard);
            try
            {
                var p = scene.Players.Main;
                // Public native Spawn owns all life/ground/jump resets. This is
                // a deliberate synthetic starting placement in an actual arena.
                p.Spawn(wall.Position + Vector3.UnitY * 2.5f + wall.Normal * .15f, -wall.Normal, Vector3.UnitY, NodeRef.None, respawn: false);
                scene.CameraSequences.Current?.End();
                bool accepted = false; int fallSteps = 0;
                for (; fallSteps < 180; fallSteps++)
                {
                    Apply(keyboard, new Input(true, false, false)); scene.OnSimulationFrame(); CheckTick(p);
                    if (p.Controls.Jump.IsPressed || p.WallClingRose) throw new InvalidOperationException("No-jump fall arm acquired a jump note.");
                    if (p.WallClinging)
                    {
                        Check(!p.WallClingStartRose && p.WallClingStartDrop >= 2 && p.WallClingTimer == 150,
                            "actual native Spawn/no-jump fall admits cling only after>=2-unit real collision-body drop");
                        accepted = true; break;
                    }
                    if (p.Flags1.TestFlag(PlayerFlags1.Standing)) break;
                }
                if (accepted) return new FallCapture(wall, fallSteps + 1, p.WallClingStartDrop);
            }
            finally { Release(scene); }
        }
        throw new InvalidOperationException("Observed actual arena walls did not admit separate native-Spawn no-jump fall arm; select a taller source wall fixture.");
    }
    private static float NativeFall(string room, FallCapture reference)
    {
        Scene scene = Load(false, room, out var keyboard);
        try
        {
            var p = scene.Players.Main; Wall wall = reference.Wall;
            p.Spawn(wall.Position + Vector3.UnitY * 2.5f + wall.Normal * .15f, -wall.Normal, Vector3.UnitY, NodeRef.None, respawn: false);
            scene.CameraSequences.Current?.End();
            float top = p.Position.Y;
            for (int i = 0; i < reference.Steps; i++)
            {
                Apply(keyboard, new Input(true, false, false)); scene.OnSimulationFrame(); CheckTick(p);
                top = MathF.Max(top, p.Position.Y);
                Check(!p.Controls.Jump.IsPressed && !p.WallClinging && p.WallClings == 0
                    && p.ModCharacterGameplay == CharacterGameplayRules.Native,
                    "same actual arena no-jump fall schedule keeps native Weavel native", verbose: false);
            }
            float drop = top - p.Position.Y;
            Check(drop >= 2 && p.Speed.Y < 0, "native comparison falls>=2 units under same actual wall/control setup and keeps native gravity");
            return drop;
        }
        finally { Release(scene); }
    }

}
