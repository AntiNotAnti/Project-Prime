using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Actual offline keyboard/mouse input, native jumps and authored collision.
// Reflection reuses the existing spawn/intro fixture and supplies input samples;
// no body, facing, speed, jump-state or ownership/acceptance fields are written.
internal static class Quake3AdvancedSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly Action<MouseState, Vector2> SetPointer = typeof(MouseState)
        .GetProperty("Position", Hidden | BindingFlags.Public)!.SetMethod!.CreateDelegate<Action<MouseState, Vector2>>();
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static int _checks;

    private sealed record Command(bool Forward, bool Left, bool Right, bool Jump, float MouseDeltaX);
    private sealed record Tick(int Number, float X, float Y, float Z, float VelocityY, float PlanarSpeed,
        bool StandingBefore, bool NewContactBefore, bool Standing, bool Grounded, bool UsedJump, bool JumpPressed,
        bool Accepted, float FacingYaw, float GatheredMouseDeltaX, string? Phase, float? SolverIncoming,
        float? SolverOutgoing, bool SurvivingAirGain);
    private sealed record Capture(Command[] Commands, Tick[] Ticks, int[] AcceptedTicks, int[] LandingTicks,
        float PeakSpeed, float PathLength, int AirGainSteps, int SurvivingAirGainSteps, int AimingDiagonalSteps,
        long SolverSteps);
    private sealed record State(int Index, PlayerEntity Player, bool JumpWasDown);

    private static void Require(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Near(float a, float b, string label)
        => Require(float.IsFinite(a) && MathF.Abs(a - b) <= .00001f, $"{label}: {a} != {b}");

    internal static int Run(string? outputPath)
    {
        var summaries = new List<object>();
        string? failure = null;
        try
        {
            _checks = 0;
            Require(!NetSession.Active && !DemoPlayback.IsActive && !DemoRecorder.IsRecording,
                "fresh offline non-recording process");
            Require(ServerSim.Available(out string reason), reason);
            Require(!PointerInput.StylusMode && !PointerDevice.Active && InputSettings.MouseSensitivity == 1
                && !InputSettings.InvertMouseX && !Features.FixedAimCamera,
                "default dynamic, noninverted sensitivity-one mouse fixture");
            Headless.Enter();
            foreach (Hunter hunter in Hunters)
            {
                Command Hold(State _) => new(false, false, false, true, 0);
                Command Manual(State s) => new(false, false, false,
                    (s.Index == 0 || NewContact(s.Player)) && !s.JumpWasDown, 0);
                Capture held = CaptureScene(hunter, MovementModel.Quake3, 360, Hold);
                Capture manual = CaptureScene(hunter, MovementModel.Quake3, 360, Manual);
                Capture classicHeld = CaptureScene(hunter, MovementModel.Native, 360, Hold);
                Capture classicManual = CaptureScene(hunter, MovementModel.Native, 360, Manual);
                Require(held.AcceptedTicks.Length >= 4 && held.LandingTicks.Length >= 3,
                    hunter + " held input repeats actual jumps across at least three collision landings");
                Require(classicHeld.AcceptedTicks.SequenceEqual(new[] { 1 }),
                    hunter + " Classic held input accepts only its first jump");
                CompareVertical(held, manual, hunter + " held/manual");
                CompareVertical(manual, classicManual, hunter + " manual/Classic manual");
                Require(held.Ticks.Count(t => t.JumpPressed) == 1,
                    hunter + " hop chain uses one actual input edge");
                Require(held.Ticks.Where(t => t.Accepted).All(t => t.StandingBefore && (t.JumpPressed || t.NewContactBefore)),
                    hunter + " held requests accepted only from physical Standing");
                Require(manual.Ticks.Count(t => t.JumpPressed) == manual.AcceptedTicks.Length,
                    hunter + " released manual control rearms one edge per landing");
                summaries.Add(new { hunter = hunter.ToString(), scenario = "hold-hop-vs-Classic-and-manual",
                    held, manual, classicHeld, classicManual });

                // This dormant stock ability is enabled only in these paired fixtures.
                // The native jump flags, impulses, landing reset and consumption remain production code.
                Capture abilityHeld = CaptureScene(hunter, MovementModel.Quake3, 180, Hold, spaceJump: true);
                Require(abilityHeld.LandingTicks.Length >= 2, hunter + " held ability fixture lands repeatedly");
                int firstLanding = abilityHeld.LandingTicks[0];
                Require(abilityHeld.Ticks.Take(firstLanding - 1).All(t => !t.UsedJump),
                    hunter + " held ground jump does not consume the native airborne jump");
                Require(abilityHeld.AcceptedTicks.TakeWhile(t => t < firstLanding).SequenceEqual(new[] { 1 }),
                    hunter + " holding never repeats a native airborne impulse");
                Command AirEdge(State s) => new(false, false, false, s.Index != 9, 0);
                Capture abilityEdge = CaptureScene(hunter, MovementModel.Quake3, 180, AirEdge, spaceJump: true);
                Capture classicAbilityEdge = CaptureScene(hunter, MovementModel.Native, 180, AirEdge, spaceJump: true);
                Require(abilityEdge.AcceptedTicks.Take(2).SequenceEqual(new[] { 1, 11 })
                    && classicAbilityEdge.AcceptedTicks.SequenceEqual(new[] { 1, 11 }),
                    hunter + " fresh release/press preserves the native second airborne jump");
                Require(!abilityEdge.Ticks[10].StandingBefore && abilityEdge.Ticks[10].UsedJump,
                    hunter + " fresh airborne edge consumes the native ability once");
                int edgeLanding = abilityEdge.LandingTicks[0];
                Require(abilityEdge.AcceptedTicks.TakeWhile(t => t < edgeLanding).Count() == 2,
                    hunter + " subsequent airborne hold cannot manufacture a third jump");
                for (int i = 0; i < edgeLanding; i++)
                {
                    Near(abilityEdge.Ticks[i].Y, classicAbilityEdge.Ticks[i].Y, hunter + " native air-jump Y");
                    Near(abilityEdge.Ticks[i].VelocityY, classicAbilityEdge.Ticks[i].VelocityY,
                        hunter + " native air-jump velocity");
                }
                summaries.Add(new { hunter = hunter.ToString(), scenario = "native-air-jump-needs-fresh-edge",
                    abilityHeld, abilityEdge, classicAbilityEdge });
                Console.WriteLine("QUAKE3 ADVANCED SCENE PASS " + hunter + "/landing-and-air-jump");
            }

            // The inputs include raw pointer positions, not facing writes or private aim calls.
            // Record each practice trajectory then give Classic the identical raw command trace.
            foreach (bool left in new[] { true, false })
            {
                int direction = left ? 1 : -1;
                Command Circle(State s) => new(true, left && s.Index >= 24, !left && s.Index >= 24,
                    s.Index == 24, s.Index >= 24 ? -4 * direction : 0);
                Command Straight(State s) => new(true, false, false, s.Index == 24, 0);
                Capture circle = CaptureScene(Hunter.Samus, MovementModel.Quake3, 144, Circle);
                Capture straight = CaptureScene(Hunter.Samus, MovementModel.Quake3, 144, Straight);
                Capture classicCircle = CaptureScene(Hunter.Samus, MovementModel.Native, 144,
                    s => circle.Commands[s.Index]);
                Require(circle.Commands.SequenceEqual(classicCircle.Commands), "circle pair has identical raw input trace");
                Require(circle.AimingDiagonalSteps > 10 && circle.AirGainSteps > 10
                    && circle.SurvivingAirGainSteps > 10, "circle aim+diagonal causes actual surviving airborne gains");
                Require(circle.PeakSpeed > straight.PeakSpeed + .01f,
                    "circle jump exceeds identical-jump straight-forward control");
                Require(circle.AcceptedTicks.Contains(25) && circle.LandingTicks.Length > 0 && circle.PathLength > 1,
                    "circle jump produces actual input takeoff, native landing and traversal");
                summaries.Add(new { scenario = left ? "circle-jump-left" : "circle-jump-right",
                    circle, straight, classicCircle });

                Command Strafe(State s)
                {
                    bool turn = s.Index >= 24;
                    float delta = 0;
                    if (turn)
                    {
                        Vector2 velocity = new(s.Player.Speed.X * 30, s.Player.Speed.Z * 30);
                        float theta = velocity.Length > .1f
                            ? MathHelper.RadiansToDegrees(MathF.Acos(Math.Clamp(
                                Quake3MovementProfileV2.GroundSpeed * (1 - 1f / 60) / velocity.Length, 0, .97f))) : 45;
                        // Native left-diagonal wish yaw is facing+45 degrees; right is facing-45.
                        float target = Yaw(s.Player.Speed) + direction * (theta - 45);
                        delta = MouseForYaw(s.Player.FacingVector, s.Player.ModGunVector, target);
                    }
                    return new(true, turn && left, turn && !left, turn, delta);
                }
                Capture strafe = CaptureScene(Hunter.Samus, MovementModel.Quake3, 360, Strafe);
                Capture classicStrafe = CaptureScene(Hunter.Samus, MovementModel.Native, 360,
                    s => strafe.Commands[s.Index]);
                Capture manualStrafe = CaptureScene(Hunter.Samus, MovementModel.Quake3, 360,
                    s => strafe.Commands[s.Index] with { Jump = s.Index >= 24
                        && (s.Index == 24 || NewContact(s.Player)) && !s.JumpWasDown });
                Require(strafe.AcceptedTicks.Length >= 4 && strafe.LandingTicks.Length >= 3,
                    "actual strafe hold-hop crosses multiple native landings");
                Require(strafe.Ticks.Count(t => t.JumpPressed) == 1
                    && strafe.Ticks.Where(t => t.Accepted).All(t => t.StandingBefore && (t.JumpPressed || t.NewContactBefore)),
                    "moving hold-hop has one input edge and no airborne manufactured impulses");
                Require(strafe.AimingDiagonalSteps > 20 && strafe.SurvivingAirGainSteps > 20,
                    "mouse steering and diagonal control gain speed through actual airborne body steps");
                int secondJump = strafe.AcceptedTicks[1];
                float firstFlightPeak = strafe.Ticks.Take(secondJump - 1).Max(t => t.PlanarSpeed);
                float laterFlightPeak = strafe.Ticks.Skip(secondJump - 1).Max(t => t.PlanarSpeed);
                Require(laterFlightPeak > firstFlightPeak + .01f,
                    "a later actual hop builds speed beyond the first flight");
                CompareBodies(strafe, manualStrafe, "held/manual moving strafe");
                Require(classicStrafe.AcceptedTicks.Length == 1 && classicStrafe.SolverSteps == 0,
                    "identical held strafe input keeps Classic edge-only jump ownership");
                Require(strafe.Commands.SequenceEqual(classicStrafe.Commands), "strafe pair has identical raw input trace");
                summaries.Add(new { scenario = left ? "multi-hop-strafe-left" : "multi-hop-strafe-right",
                    firstFlightPeak, laterFlightPeak, strafe, manualStrafe, classicStrafe });
                Console.WriteLine("QUAKE3 ADVANCED SCENE PASS " + (left ? "left" : "right") + "/circle-and-multi-hop-strafe");
            }
        }
        catch (Exception ex) { failure = ex.ToString(); Console.Error.WriteLine("QUAKE3 ADVANCED SCENE FAIL " + ex); }
        if (outputPath != null)
        {
            string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { status = failure == null ? "PASS" : "FAIL", failure,
                checks = _checks, profileVersion = MovementModelRules.ProfileVersion(MovementModel.Quake3),
                scope = "Actual offline CPU input/collision; synthetic keyboard and raw mouse samples, native spawn/intro skip, fixed 60 Hz; no physical device, GL, online, bot, replay, competitive-balance or maximum-speed acceptance",
                conditions = "MP3 PROVING GROUND; normal admitted match rule, diagnostics only; native SpaceJump ability bit enabled only in explicitly labeled paired probes; no body/velocity/facing/jump-state or authority writes",
                schedules = "stationary hold: Space continuously; manual: one-tick pulse after each physical Standing contact; airborne edge: hold ticks1-9, release10, hold11 onward; circle: W run-up24 ticks, diagonal/raw mouse +/-4 from tick25, jump25 only; strafe: same run-up, diagonal+read-only velocity-directed raw mouse and held Jump from tick25; Classic replays exact recorded raw commands",
                summaries }, new JsonSerializerOptions { WriteIndented = true }));
        }
        if (failure != null) return 1;
        Console.WriteLine($"QUAKE3 ADVANCED SCENE PASS {_checks} assertions"); return 0;
    }

    private static Capture CaptureScene(Hunter hunter, MovementModel model, int length,
        Func<State, Command> command, bool spaceJump = false)
    {
        object?[] arguments = { hunter, model, null };
        Scene scene = (Scene)typeof(Quake3SceneTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        var keyboard = (KeyboardState)arguments[2]!;
        try
        {
            PlayerEntity player = scene.Players.Main!;
            Require(scene.GameState.MovementModel == model && !scene.DeveloperMomentumActive
                && scene.DeveloperMomentumPracticeTuning == null, "normal immutable match profile owns capture");
            player.Controls.MouseAim = true; player.Controls.KeyboardAim = false;
            Require(!player.Controls.InvertAimX, "native noninverted mouse binding");
            if (spaceJump)
            {
                var abilities = typeof(PlayerEntity).GetField("_abilities", Hidden)!;
                abilities.SetValue(player, (AbilityFlags)abilities.GetValue(player)! | AbilityFlags.SpaceJump);
            }
            var mouse = (MouseState)typeof(Scene).GetField("_mouseState", Hidden)!.GetValue(scene)!;
            var input = (PlayerEntity.PlayerInput)typeof(PlayerEntity).GetProperty("Input", Hidden)!.GetValue(player)!;
            Vector2 pointer = mouse.Position;
            Vector3 previousPosition = player.Position;
            float previousY = player.Speed.Y, peak = 0, path = 0;
            int airGain = 0, survivingAirGain = 0, diagonalAim = 0;
            long startSteps = scene.DeveloperMomentumSteps;
            var commands = new Command[length]; var ticks = new Tick[length];
            var accepted = new List<int>(); var landings = new List<int>();
            bool jumpWasDown = false, wasAirborne = false;
            for (int i = 0; i < length; i++)
            {
                bool standingBefore = player.Flags1.TestFlag(PlayerFlags1.Standing);
                bool newContactBefore = NewContact(player);
                Command c = command(new(i, player, jumpWasDown)); commands[i] = c;
                SetKey(keyboard, Keys.W, c.Forward); SetKey(keyboard, Keys.A, c.Left);
                SetKey(keyboard, Keys.D, c.Right); SetKey(keyboard, Keys.Space, c.Jump);
                float previousPointerX = pointer.X;
                pointer.X += c.MouseDeltaX;
                float requestedDelta = pointer.X - previousPointerX;
                SetPointer(mouse, pointer);
                long before = scene.DeveloperMomentumSteps; ulong frame = scene.FrameCount;
                scene.OnSimulationFrame();
                Require(player.ModIsInPlay && player.Health > 0 && !player.IsBot && !player.IsAltForm
                    && !NetSession.Active && !DemoPlayback.IsActive, "actual living offline local biped");
                Require(player.Controls.MoveUp.IsDown == c.Forward && player.Controls.MoveLeft.IsDown == c.Left
                    && player.Controls.MoveRight.IsDown == c.Right && player.Controls.Jump.IsDown == c.Jump
                    && player.Controls.Jump.IsPressed == (c.Jump && !jumpWasDown), "actual production keyboard levels and edges");
                // Position accumulation may quantize the requested raw delta; compare the value actually supplied.
                Near(input.MouseDeltaX, requestedDelta, "raw mouse sample reaches production input");
                Near(input.MouseDeltaY, 0, "zero-pitch synthetic pointer");
                bool impulse = player.Controls.Jump.IsPressed && player.Speed.Y > .01f && player.Speed.Y > previousY + .01f;
                bool standing = player.Flags1.TestFlag(PlayerFlags1.Standing);
                bool grounded = player.Flags1.TestFlag(PlayerFlags1.Grounded);
                if (wasAirborne && standing) landings.Add(i + 1);
                wasAirborne = !standing;
                float planar = new Vector2(player.Speed.X, player.Speed.Z).Length * 30;
                peak = MathF.Max(peak, planar);
                path += new Vector2(player.Position.X - previousPosition.X, player.Position.Z - previousPosition.Z).Length;
                string? phase = null; float? incoming = null, outgoing = null; bool survived = false;
                long steps = scene.DeveloperMomentumSteps - before;
                Require(model == MovementModel.Native ? steps == 0 : steps is >= 0 and <= 1,
                    "at most one eligible horizontal solver; Classic has none");
                if (steps == 1)
                {
                    var sample = scene.LastMomentumStep!.Value;
                    Require(sample.SimulationFrame == frame && sample.Slot == player.SlotIndex,
                        "same-tick actual player solver sample");
                    Require(BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y)
                        == BitConverter.SingleToInt32Bits(sample.NativeVelocityAfter.Y), "horizontal solver preserves native Y bits");
                    Vector3 incomingSolver = sample.GroundSupport.HasValue
                        ? GroundSlopeMovement.Flatten(sample.NativeVelocityBefore) * 30
                        : new(sample.NativeVelocityBefore.X * 30, 0, sample.NativeVelocityBefore.Z * 30);
                    Vector3 expected = Quake3MovementProfileV4.Step(incomingSolver, sample.Input).Velocity;
                    Near(sample.NativeVelocityAfter.X, expected.X / 30, "actual immutable solver X");
                    Near(sample.NativeVelocityAfter.Z, expected.Z / 30, "actual immutable solver Z");
                    Require(!sample.SpeedLimited && !sample.IncomingOverCap, "uncapped profile diagnostics");
                    phase = sample.ContactPhase.ToString();
                    bool legal = sample.ContactPhase == MomentumContactPhase.LegalJump;
                    if (legal)
                    {
                        Require(c.Jump && (standingBefore || player.Controls.Jump.IsPressed),
                            "accepted native impulse uses physical contact or native fresh airborne edge");
                        Near(sample.NativeVelocityBefore.Y, Fixed.ToFloat(player.Values.JumpSpeed),
                            "accepted native Hunter jump impulse");
                    }
                    // Sample is taken before collision. Body impulse inference is only used by Classic;
                    // the production nativeJumpAccepted contact phase identifies practice jumps exactly.
                    impulse = legal;
                    incoming = new Vector2(sample.NativeVelocityBefore.X, sample.NativeVelocityBefore.Z).Length * 30;
                    outgoing = new Vector2(sample.NativeVelocityAfter.X, sample.NativeVelocityAfter.Z).Length * 30;
                    bool gain = !sample.Input.Grounded && outgoing > incoming + .00001f;
                    if (gain) airGain++;
                    survived = gain && MathF.Abs(planar - outgoing.Value) <= .0001f;
                    if (survived) survivingAirGain++;
                    if (sample.Input.MoveAxes.X != 0 && sample.Input.MoveAxes.Y > 0 && input.MouseDeltaX != 0)
                        diagonalAim++;
                }
                if (impulse) accepted.Add(i + 1);
                Require(float.IsFinite(player.Position.LengthSquared) && float.IsFinite(player.Speed.LengthSquared),
                    "finite actual native collision body");
                ticks[i] = new(i + 1, player.Position.X, player.Position.Y, player.Position.Z, player.Speed.Y,
                    planar, standingBefore, newContactBefore, standing, grounded, player.Flags1.TestFlag(PlayerFlags1.UsedJump),
                    player.Controls.Jump.IsPressed, impulse, Yaw(player.FacingVector), input.MouseDeltaX,
                    phase, incoming, outgoing, survived);
                previousPosition = player.Position; previousY = player.Speed.Y; jumpWasDown = c.Jump;
            }
            return new(commands, ticks, accepted.ToArray(), landings.ToArray(), peak, path,
                airGain, survivingAirGain, diagonalAim, scene.DeveloperMomentumSteps - startSteps);
        }
        finally { MomentumSceneTests.Release(scene); }
    }

    private static void CompareVertical(Capture a, Capture b, string label)
    {
        Require(a.AcceptedTicks.SequenceEqual(b.AcceptedTicks), label + " actual impulse tick parity");
        Require(a.LandingTicks.SequenceEqual(b.LandingTicks), label + " physical collision landing parity");
        for (int i = 0; i < a.Ticks.Length; i++)
        {
            Near(a.Ticks[i].Y, b.Ticks[i].Y, label + " Y");
            Near(a.Ticks[i].VelocityY, b.Ticks[i].VelocityY, label + " vertical speed");
            Require(a.Ticks[i].Standing == b.Ticks[i].Standing && a.Ticks[i].Grounded == b.Ticks[i].Grounded
                && a.Ticks[i].UsedJump == b.Ticks[i].UsedJump, label + " native contact/jump flags");
        }
    }
    private static void CompareBodies(Capture a, Capture b, string label)
    {
        CompareVertical(a, b, label);
        for (int i = 0; i < a.Ticks.Length; i++)
        {
            Near(a.Ticks[i].X, b.Ticks[i].X, label + " body X");
            Near(a.Ticks[i].Z, b.Ticks[i].Z, label + " body Z");
            Near(a.Ticks[i].PlanarSpeed, b.Ticks[i].PlanarSpeed, label + " actual planar speed");
            Near(a.Ticks[i].FacingYaw, b.Ticks[i].FacingYaw, label + " native facing");
        }
    }
    private static bool NewContact(PlayerEntity player) => player.Flags1.TestFlag(PlayerFlags1.Standing)
        && !player.Flags1.TestFlag(PlayerFlags1.StandingPrevious);
    private static float Yaw(Vector3 value) => MathHelper.RadiansToDegrees(MathF.Atan2(value.X, value.Z));
    private static float Wrap(float value) => (value + 540) % 360 - 180;

    // Read-only model already used by the ordinary mouse/input acceptance fixture.
    // Solve for a raw mouse sample; production UpdatePointer/UpdateAimX performs the turn.
    private static float PredictYaw(Vector3 facing, Vector3 gun, float delta)
    {
        static double Ease(double body, double aim)
        {
            double difference = (body - aim + 3 * Math.PI) % (2 * Math.PI) - Math.PI;
            if (Math.Cos(difference) < 3956d / 4096)
                difference = Math.CopySign(Math.Atan2(1060, 3956), difference);
            return aim + Math.Atan2(.9 * Math.Sin(difference), .9 * Math.Cos(difference) + .1);
        }
        double body = Ease(Math.Atan2(facing.X, facing.Z), Math.Atan2(gun.X, gun.Z));
        double aim = Math.Atan2(gun.X, gun.Z) - delta / 4d * Math.PI / 180;
        return (float)(Ease(body, aim) * 180 / Math.PI);
    }
    private static float MouseForYaw(Vector3 facing, Vector3 gun, float target)
    {
        float baseline = PredictYaw(facing, gun, 0), low = -256, high = 256;
        float goal = Math.Clamp(Wrap(target - baseline), Wrap(PredictYaw(facing, gun, high) - baseline),
            Wrap(PredictYaw(facing, gun, low) - baseline));
        for (int i = 0; i < 20; i++)
        {
            float middle = (low + high) / 2;
            if (Wrap(PredictYaw(facing, gun, middle) - baseline) > goal) low = middle;
            else high = middle;
        }
        return MathF.Round((low + high) / 2 * 256, MidpointRounding.AwayFromZero) / 256;
    }
}
