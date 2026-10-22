using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Real offline input and authored native collision. Initial lane position/facing
// is an explicit fixture seam, followed by neutral collision settling. All measured
// speeds, slope support, jumps and surfing are earned by the production input path.
internal static class TraversalSceneTests
{
    internal const string Room = "TRAVERSAL TRAINING";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static int _checks;
    private sealed record Command(bool Forward = false, bool Jump = false);
    private sealed record Tick(int Number, float X, float Y, float Z, float PlanarSpeed, float VelocityY,
        bool StandingBefore, bool Standing, bool JumpPressed, bool Accepted, float ExpectedRampLift,
        float? NativeJumpImpulse, float SupportY, bool FreshSupport, bool SlopeContact, bool TaggedContact,
        float? MinimumSlopeClearance, bool? SolverGrounded, float? TelemetrySpeed, string? FormattedTelemetrySpeed,
        float? BeforeCollisionSpeed, bool WallContact);
    private sealed record Capture(string Hunter, string Model, string Scenario, Command[] Commands, Tick[] Ticks,
        int[] JumpTicks, int[] LandingTicks, float PathLength, float PeakSpeed, long SolverSteps);
    private sealed record State(int Index, PlayerEntity Player, bool Jumped);
    private static readonly FieldInfo Support = typeof(PlayerEntity).GetField("_modRampSupport", Hidden)!;
    private static readonly FieldInfo SupportFrame = typeof(PlayerEntity).GetField("_modRampSupportFrame", Hidden)!;
    private static readonly FieldInfo Contacts = typeof(PlayerEntity).GetField("_collisionScratch", Hidden)!;
    private static void Require(bool good, string label)
    { _checks++; if (!good) throw new InvalidOperationException(label); }
    private static void Near(float a, float b, string label, float tolerance = .00001f)
        => Require(float.IsFinite(a) && MathF.Abs(a - b) <= tolerance, $"{label}: {a} != {b}");

    internal static int Run(string? outputPath)
    {
        _checks = 0;
        var rows = new List<object>(); var failures = new List<string>();
        try
        {
            Require(!NetSession.Active && !DemoPlayback.IsActive && !DemoRecorder.IsRecording,
                "fresh offline nonrecording scene fixture");
            Require(ServerSim.Available(out string reason), reason); Headless.Enter();
            void Case(string name, Action work)
            {
                try { work(); Console.WriteLine("TRAVERSAL SCENE PASS " + name); }
                catch (Exception error) { failures.Add(name + ": " + error); Console.Error.WriteLine("TRAVERSAL SCENE FAIL " + name + ": " + error); }
            }
            foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire, Hunter.Noxus,
                Hunter.Trace, Hunter.Sylux, Hunter.Weavel })
            {
                Case(hunter + "/held-hop", () =>
                {
                    Command Hold(State _) => new(Jump: true);
                    Vector3 seed = new(42, .5f, 20);
                    Capture traversal = CaptureScene(hunter, MovementModel.Traversal, "flat-held-hop", seed,
                        Vector3.UnitZ, 300, Hold);
                    Capture quake = CaptureScene(hunter, MovementModel.Quake3, "flat-held-hop", seed,
                        Vector3.UnitZ, 300, Hold);
                    Capture classic = CaptureScene(hunter, MovementModel.Native, "flat-held-hop", seed,
                        Vector3.UnitZ, 300, Hold);
                    rows.Add(new { scenario = "held-hop", traversal, quake, classic });
                    Require(traversal.JumpTicks.Length >= 4 && traversal.LandingTicks.Length >= 3,
                        hunter + " held input repeats across genuine native collision landings");
                    Require(traversal.Ticks.Count(t => t.JumpPressed) == 1
                        && traversal.Ticks.Where(t => t.Accepted).All(t => t.StandingBefore),
                        hunter + " one held edge never fabricates airborne jump impulses");
                    Require(classic.JumpTicks.SequenceEqual(new[] { 1 }) && classic.SolverSteps == 0,
                        hunter + " identical held input preserves Classic edge-only jump ownership");
                    Require(traversal.JumpTicks.SequenceEqual(quake.JumpTicks)
                        && traversal.LandingTicks.SequenceEqual(quake.LandingTicks), "flat held jump/contact tick parity with Quake III");
                    for (int index = 0; index < traversal.Ticks.Length; index++)
                    {
                        Near(traversal.Ticks[index].Y, quake.Ticks[index].Y, "flat native Y parity");
                        Near(traversal.Ticks[index].VelocityY, quake.Ticks[index].VelocityY, "flat native gravity parity");
                    }
                });
            }
            Case("postcollision-speed-telemetry", () =>
            {
                Capture wall = CaptureScene(Hunter.Samus, MovementModel.Traversal, "wall-telemetry",
                    new(42, .5f, 20), Vector3.UnitX, 140, _ => new(Forward: true));
                rows.Add(new { scenario = "postcollision-speed-telemetry", wall });
                Tick[] contact = wall.Ticks.Where(t => t.WallContact).ToArray();
                Require(contact.Length > 3 && wall.PathLength > 1 && wall.PeakSpeed > 1,
                    "keyboard movement earns speed and reaches the authored east boundary");
                Require(contact.Any(t => t.BeforeCollisionSpeed > t.PlanarSpeed + .01f),
                    "actual native wall response changes the velocity after the solver seam");
                Require(contact.All(t => t.TelemetrySpeed.HasValue
                    && MathF.Abs(t.TelemetrySpeed.Value - t.PlanarSpeed) <= .00001f),
                    "HUD speed uses actual body velocity after wall collision");
                Require(contact.All(t => t.FormattedTelemetrySpeed == "HORIZ "
                    + t.PlanarSpeed.ToString("0.0", CultureInfo.InvariantCulture) + " U/S"),
                    "formatted speed reflects postcollision body units per second");
            });
            Case("morph-invalidates-stale-telemetry", () =>
            {
                Scene scene = Load(Hunter.Samus, MovementModel.Traversal, out KeyboardState keyboard);
                try
                {
                    PlayerEntity p = scene.Players.Main!;
                    SetKey(keyboard, Keys.W, true);
                    for (int index = 0; index < 5; index++) scene.OnSimulationFrame();
                    Require(scene.MovementTelemetry is { Speed: > 0 }, "eligible movement publishes current measured telemetry");
                    SetKey(keyboard, Keys.Tab, true); scene.OnSimulationFrame();
                    Require((p.IsMorphing || p.IsAltForm) && !p.ModMomentumEligible
                        && scene.MovementTelemetry == null, "actual morph input hides prior eligible movement telemetry");
                    rows.Add(new { scenario = "morph-invalidates-stale-telemetry", p.IsMorphing,
                        p.IsAltForm, eligible = p.ModMomentumEligible, telemetryPresent = scene.MovementTelemetry.HasValue });
                }
                finally { MomentumSceneTests.Release(scene); }
            });
            Case("earned-uphill-ramp-jump", () =>
            {
                Command Uphill(State s) => new(true, !s.Jumped && s.Player.Position.Z > -2
                    && s.Player.Position.Z < 4 && s.Player.Flags1.TestFlag(PlayerFlags1.Standing)
                    && s.Player.Speed.Y > .001f);
                Capture uphill = CaptureScene(Hunter.Samus, MovementModel.Traversal, "uphill", new(-30, .5f, -20),
                    Vector3.UnitZ, 320, Uphill);
                Capture quake = CaptureScene(Hunter.Samus, MovementModel.Quake3, "uphill-identical-input",
                    new(-30, .5f, -20), Vector3.UnitZ, 320, s => uphill.Commands[s.Index]);
                Capture flat = CaptureScene(Hunter.Samus, MovementModel.Traversal, "flat-identical-input",
                    new(-42, .5f, -20), Vector3.UnitZ, 320, s => uphill.Commands[s.Index]);
                rows.Add(new { scenario = "earned-uphill-ramp-jump", uphill, quake, flat });
                Require(uphill.JumpTicks.Length == 1 && quake.JumpTicks.SequenceEqual(uphill.JumpTicks)
                    && flat.JumpTicks.SequenceEqual(uphill.JumpTicks), "paired profiles accept the same single keyboard jump");
                Tick earned = uphill.Ticks.Single(t => t.Accepted);
                Tick comparison = quake.Ticks.Single(t => t.Accepted);
                Tick flatJump = flat.Ticks.Single(t => t.Accepted);
                Require(earned.FreshSupport && earned.SupportY > .5f && earned.SupportY < .99f
                    && earned.ExpectedRampLift > .001f && earned.StandingBefore,
                    "ramp jump has preceding real sloped support and earned uphill velocity");
                Near(earned.NativeJumpImpulse!.Value, comparison.NativeJumpImpulse!.Value + earned.ExpectedRampLift, "actual native jump includes earned ramp lift");
                Require(earned.NativeJumpImpulse > comparison.NativeJumpImpulse + .001f,
                    "Traversal uphill takeoff is stronger than identical-input Quake III takeoff");
                Near(flatJump.ExpectedRampLift, 0, "flat ground has no ramp lift");
                Near(flatJump.NativeJumpImpulse!.Value, comparison.NativeJumpImpulse!.Value,
                    "flat Traversal keeps the native Quake III jump impulse");
                Require(uphill.PathLength > 10 && uphill.PeakSpeed > 1, "ramp speed is earned by actual keyboard traversal");
                Require(uphill.Ticks.Skip(earned.Number).Max(t => t.Y) > quake.Ticks.Skip(earned.Number).Max(t => t.Y) + .01f,
                    "earned ramp lift changes actual collision-body apex");
            });
            Case("earned-downhill-no-lift", () =>
            {
                Command Downhill(State s) => new(true, !s.Jumped && s.Player.Position.Z < 1
                    && s.Player.Position.Z > -5 && s.Player.Flags1.TestFlag(PlayerFlags1.Standing));
                Capture downhill = CaptureScene(Hunter.Samus, MovementModel.Traversal, "downhill",
                    new(-30, 4, 6), -Vector3.UnitZ, 180, Downhill);
                Capture quake = CaptureScene(Hunter.Samus, MovementModel.Quake3, "downhill-identical-input",
                    new(-30, 4, 6), -Vector3.UnitZ, 180, s => downhill.Commands[s.Index]);
                rows.Add(new { scenario = "earned-downhill-no-lift", downhill, quake });
                Require(downhill.JumpTicks.Length == 1 && quake.JumpTicks.SequenceEqual(downhill.JumpTicks),
                    "earned downhill paired trajectories accept one actual jump");
                Tick jump = downhill.Ticks.Single(t => t.Accepted);
                Require(jump.FreshSupport && jump.SupportY > .5f && jump.SupportY < .99f,
                    "downhill control also has actual sloped support");
                Near(jump.ExpectedRampLift, 0, "downhill motion receives no ramp lift");
                Near(jump.NativeJumpImpulse!.Value, quake.Ticks.Single(t => t.Accepted).NativeJumpImpulse!.Value,
                    "downhill jump retains native impulse");
            });
            Case("tagged-surf-vs-identical-untagged-slope", () =>
            {
                bool released = false;
                Command Surf(State s)
                { released |= s.Player.Position.Z >= -.5f; return new(!released); }
                Capture tagged = CaptureScene(Hunter.Samus, MovementModel.Traversal, "tagged-surf",
                    new(-10, 8.5f, -20), Vector3.UnitZ, 360, Surf);
                Capture untagged = CaptureScene(Hunter.Samus, MovementModel.Traversal, "untagged-control",
                    new(10, 8.5f, -20), Vector3.UnitZ, 360, s => tagged.Commands[s.Index]);
                Capture quake = CaptureScene(Hunter.Samus, MovementModel.Quake3, "tagged-Quake3-control",
                    new(-10, 8.5f, -20), Vector3.UnitZ, 360, s => tagged.Commands[s.Index]);
                rows.Add(new { scenario = "tagged-surf-vs-identical-untagged-slope", tagged, untagged, quake });
                Tick[] contacts = tagged.Ticks.Where(t => t.SlopeContact && t.TaggedContact).ToArray();
                Tick[] control = untagged.Ticks.Where(t => t.SlopeContact).ToArray();
                Require(contacts.Length >= 3 && control.Length > 0,
                    "earned trajectories make real finite-face contact in both authored lanes");
                Require(contacts.All(t => !t.Standing) && contacts.Any(t => t.SolverGrounded == false),
                    "tagged Traversal surf retains airborne control without native Standing");
                Require(control.Any(t => t.Standing) && quake.Ticks.Any(t => t.TaggedContact && t.Standing),
                    "untagged Traversal and tagged Quake III retain ordinary native slope standing");
                Require(contacts.All(t => t.MinimumSlopeClearance >= -.02f)
                    && control.All(t => t.MinimumSlopeClearance >= -.02f), "native positional correction prevents plane penetration");
                Require(contacts.Max(t => t.PlanarSpeed) > control.Max(t => t.PlanarSpeed) + .01f,
                    "tagged downhill surf preserves more actual speed than identical untagged contact");
                Require(tagged.JumpTicks.Length == 0 && tagged.PathLength > 20
                    && tagged.Commands.Any(c => !c.Forward), "surf speed comes from earned run-up and gravity after input release");
            });
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        if (outputPath != null)
        {
            string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { status = failures.Count == 0 ? "PASS" : "FAIL",
                checks = _checks, failures, scope = "Asset-backed offline CPU scene; synthetic keyboard, native spawn/intro skip, authored training collision, explicit initial lane placement only; no fabricated speeds/support/jump/owner flags; no physical device, GL, online, replay or competitive-balance acceptance",
                room = Room, profileVersion = MovementModelRules.ProfileVersion(MovementModel.Traversal),
                conditions = "initial Reposition/PrevPosition lane seam; twelve neutral collision steps settle; each speed then earned through keyboard input; uphill Q3/flat and downhill Q3 replay identical raw keyboard trace; surf lanes differ only in authored tag and X translation", rows },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"TRAVERSAL SCENE {(failures.Count == 0 ? "PASS" : "FAIL")} {_checks} assertions; {failures.Count} failed cases");
        return failures.Count == 0 ? 0 : 1;
    }

    private static Capture CaptureScene(Hunter hunter, MovementModel model, string scenario, Vector3 seed,
        Vector3 facing, int length, Func<State, Command> choose)
    {
        Scene scene = Load(hunter, model, out KeyboardState keyboard);
        try
        {
            PlayerEntity p = scene.Players.Main!;
            Require(p.Speed.X == 0 && p.Speed.Z == 0, "native spawn settled without fabricated horizontal speed");
            // The only spatial fixture write. Native collision settles this lane before recording.
            p.Reposition(seed, facing, NodeRef.None); p.PrevPosition = seed;
            for (int index = 0; index < 12; index++) scene.OnSimulationFrame();
            Require(p.Flags1.TestFlag(PlayerFlags1.Standing), "initial lane has actual solid support");
            var commands = new Command[length]; var ticks = new Tick[length];
            var jumps = new List<int>(); var landings = new List<int>();
            long startSteps = scene.DeveloperMomentumSteps;
            Vector3 previous = p.Position; float previousY = p.Speed.Y, path = 0, peak = 0;
            bool jumped = false, wasAirborne = false, jumpWasDown = false;
            for (int index = 0; index < length; index++)
            {
                Command command = choose(new(index, p, jumped)); commands[index] = command;
                bool standingBefore = p.Flags1.TestFlag(PlayerFlags1.Standing);
                Vector3 support = (Vector3)Support.GetValue(p)!;
                ulong supportFrame = (ulong)SupportFrame.GetValue(p)!;
                bool fresh = supportFrame == scene.FrameCount - 1;
                float lift = model == MovementModel.Traversal && fresh && standingBefore
                    ? TraversalMovementProfileV1.RampLift(p.Speed, support) : 0;
                SetKey(keyboard, Keys.W, command.Forward); SetKey(keyboard, Keys.Space, command.Jump);
                long before = scene.DeveloperMomentumSteps; ulong frame = scene.FrameCount;
                scene.OnSimulationFrame();
                Require(!NetSession.Active && p.ModIsInPlay && p.Health > 0 && !p.IsBot && !p.IsAltForm,
                    "genuine living offline local biped");
                Require(p.Controls.MoveUp.IsDown == command.Forward && p.Controls.Jump.IsDown == command.Jump
                    && p.Controls.Jump.IsPressed == (command.Jump && !jumpWasDown), "production controls gather exact keyboard levels/edges");
                bool accepted = p.Controls.Jump.IsPressed && p.Speed.Y > .01f && p.Speed.Y > previousY + .01f;
                float? impulse = null; bool? solverGrounded = null; float? beforeCollisionSpeed = null;
                long steps = scene.DeveloperMomentumSteps - before;
                Require(model == MovementModel.Native ? steps == 0 : steps is >= 0 and <= 1,
                    "Classic has no horizontal solver; one eligible adapter at most");
                if (steps == 1)
                {
                    var sample = scene.LastMomentumStep!.Value;
                    Require(sample.SimulationFrame == frame && sample.Slot == p.SlotIndex, "same-tick body solver witness");
                    Require(BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y)
                        == BitConverter.SingleToInt32Bits(sample.NativeVelocityAfter.Y), "horizontal solver preserves native Y bits");
                    Vector3 inputSpeed = new(sample.NativeVelocityBefore.X * 30, 0, sample.NativeVelocityBefore.Z * 30);
                    if (sample.GroundSupport.HasValue)
                        inputSpeed = GroundSlopeMovement.Flatten(sample.NativeVelocityBefore) * 30;
                    Vector3 expected = model == MovementModel.Traversal
                        ? TraversalMovementProfileV4.Step(inputSpeed, sample.Input, sample.Crouched, sample.Sliding).Velocity
                        : Quake3MovementProfileV3.Step(inputSpeed, sample.Input).Velocity;
                    Near(sample.NativeVelocityAfter.X, expected.X / 30, "actual selected solver X");
                    Near(sample.NativeVelocityAfter.Z, expected.Z / 30, "actual selected solver Z");
                    solverGrounded = sample.Input.Grounded;
                    beforeCollisionSpeed = sample.NativeVelocityAfter.Xz.Length * 30;
                    accepted = sample.ContactPhase == MomentumContactPhase.LegalJump;
                    if (accepted) impulse = sample.NativeVelocityBefore.Y;
                }
                if (accepted) { jumps.Add(index + 1); jumped = true; }
                bool standing = p.Flags1.TestFlag(PlayerFlags1.Standing);
                if (wasAirborne && standing) landings.Add(index + 1);
                wasAirborne = !standing;
                bool slopeContact = false, taggedContact = false, wallContact = false; float? minimum = null;
                foreach (CollisionResult contact in (CollisionResult[])Contacts.GetValue(p)!)
                {
                    if (contact.Field0 == 0 && contact.EntityCollision == null && contact.Plane.X < -.99f
                        && MathF.Abs(contact.Plane.Y) < .001f && MathF.Abs(contact.Plane.Z) < .001f
                        && p.Position.X > 47 && MathF.Abs(Vector3.Dot(p.Position, contact.Plane.Xyz)
                            - Fixed.ToFloat(p.Values.BipedColRadius) - contact.Plane.W) <= .025f)
                        wallContact = true;
                    if (contact.Field0 != 0 || MathF.Abs(contact.Plane.Y - .7071068f) > .002f
                        || contact.Plane.Z < .7f || contact.EntityCollision != null) continue;
                    float radius = Fixed.ToFloat(p.Values.BipedColRadius);
                    Vector3 bottom = p.Position.AddY(Fixed.ToFloat(p.Values.MinPickupHeight) + radius);
                    Vector3 top = p.Position.AddY(Fixed.ToFloat(p.Values.MaxPickupHeight) - radius);
                    float gap = MathF.Min(Vector3.Dot(bottom, contact.Plane.Xyz), Vector3.Dot(top, contact.Plane.Xyz))
                        - radius - contact.Plane.W;
                    if (gap > .025f || p.Position.Z < -.5f || p.Position.Z > 8.5f) continue;
                    slopeContact = true; taggedContact |= contact.Flags.TestFlag(CollisionFlags.TraversalSurfSurface);
                    minimum = minimum.HasValue ? MathF.Min(minimum.Value, gap) : gap;
                }
                Require(float.IsFinite(p.Position.LengthSquared) && float.IsFinite(p.Speed.LengthSquared),
                    "finite authored native collision body");
                float planar = p.Speed.Xz.Length * 30; peak = MathF.Max(peak, planar);
                MovementTelemetrySample? telemetry = scene.MovementTelemetry;
                if (steps == 1)
                {
                    Require(telemetry.HasValue, "eligible adapter publishes finalized current telemetry");
                    Near(telemetry!.Value.Speed, planar, "finalized telemetry matches actual postcollision body speed");
                }
                else Require(!telemetry.HasValue, "absent or ineligible adapter cannot expose stale movement telemetry");
                path += (p.Position - previous).Length;
                ticks[index] = new(index + 1, p.Position.X, p.Position.Y, p.Position.Z, planar, p.Speed.Y,
                    standingBefore, standing, p.Controls.Jump.IsPressed, accepted, lift, impulse,
                    support.Y, fresh, slopeContact, taggedContact, minimum, solverGrounded, telemetry?.Speed,
                    telemetry.HasValue ? MovementTelemetryHud.Format(telemetry.Value).Speed : null,
                    beforeCollisionSpeed, wallContact);
                previous = p.Position; previousY = p.Speed.Y; jumpWasDown = command.Jump;
            }
            return new(hunter.ToString(), model.ToString(), scenario, commands, ticks, jumps.ToArray(),
                landings.ToArray(), path, peak, scene.DeveloperMomentumSteps - startSteps);
        }
        finally { MomentumSceneTests.Release(scene); }
    }

    private static Scene Load(Hunter hunter, MovementModel model, out KeyboardState keyboard)
    {
        Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
        keyboard = SyntheticInput.CreateKeyboard(); Scene? scene = null;
        try
        {
            scene = new Scene(new Vector2i(256, 192), keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { },
                lifetimeOwned: value => scene = value);
            scene.AddPlayer(hunter); scene.AddRoom(Room, GameMode.Battle, playerCount: 1);
            new MatchDefinition { RoomKey = Room, Mode = GameMode.Battle, MovementModel = model,
                TimeLimitSeconds = 0, PointGoal = 10000 }.ApplyInitialConfiguration(scene.GameState);
            typeof(Scene).GetField("_developerMomentumDiagnostics", Hidden)!.SetValue(scene, true);
            scene.OnLoad();
            var inputMode = typeof(Scene).GetField("_inputMode", Hidden)!;
            inputMode.SetValue(scene, Enum.Parse(inputMode.FieldType, "All"));
            scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
            PlayerEntity p = scene.Players.Items[0];
            foreach (var control in p.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            p.Controls.MoveUp.Key = Keys.W; p.Controls.MoveDown.Key = Keys.S;
            p.Controls.MoveLeft.Key = Keys.A; p.Controls.MoveRight.Key = Keys.D;
            p.Controls.Jump.Key = Keys.Space; p.Controls.Shoot.Key = Keys.Enter; p.Controls.Morph.Key = Keys.Tab;
            p.Controls.MouseAim = false; p.Controls.KeyboardAim = true;
            SetKey(keyboard, Keys.Enter, true);
            for (int index = 0; index < 600 && !p.LoadFlags.TestFlag(LoadFlags.Spawned); index++) scene.OnSimulationFrame();
            SetKey(keyboard, Keys.Enter, false); Require(p.LoadFlags.TestFlag(LoadFlags.Spawned), "native keyboard shoot-to-spawn");
            scene.CameraSequences.Current?.End();
            for (int index = 0; index < 120; index++) scene.OnSimulationFrame();
            Require(ReferenceEquals(p, scene.Players.Main) && p.Hunter == hunter && !p.IsBot
                && NetHooks.LocalSlot == 0 && p.Health > 0 && p.ModIsInPlay, "native offline owned player");
            Require(scene.TraversalTrainingSurfaces && scene.GameState.MovementModel == model
                && !scene.DeveloperMomentumActive && scene.DeveloperMomentumPracticeTuning == null,
                "authored training opt-in and explicit immutable match rule own scene");
            if (model != MovementModel.Native) Require(p.ModMomentumEligible, "normal owned biped admitted to movement adapter");
            return scene;
        }
        catch { if (scene != null) MomentumSceneTests.Release(scene); throw; }
    }
}
