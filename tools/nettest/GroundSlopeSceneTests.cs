using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats.Culling;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Production keyboard, immutable match rules and authored native collision. The
// sole spatial fixture seam is the labeled initial placement followed by neutral
// settling. Velocity, support, slide state and accepted jumps are never written.
internal static class GroundSlopeSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly FieldInfo Support = typeof(PlayerEntity).GetField("_modRampSupport", Hidden)!;
    private static readonly FieldInfo SupportFrame = typeof(PlayerEntity).GetField("_modRampSupportFrame", Hidden)!;
    private static int _checks;
    private static object? _lastCapture;
    private sealed record Command(bool Forward = false, bool Crouch = false, bool Jump = false);
    private sealed record State(int Index, PlayerEntity Player, Scene Scene);
    private sealed record Tick(int Number, float X, float Y, float Z, float Speed, float PlanarSpeed,
        float VelocityY, float BeforeSpeed, bool StandingBefore, bool Standing, bool Forward,
        bool Sliding, bool AcceptedJump, bool GroundTravel, float SupportY, float ExpectedSpeed,
        bool FreshSupportAfter, float SupportAfterY);
    private sealed record Capture(string Hunter, string Model, string Scenario, float[] Placement,
        float BodyRadius, Tick[] Ticks, long SolverSteps);
    private static void Check(bool good, string message)
    { _checks++; if (!good) throw new InvalidOperationException(message); }
    private static void Near(float actual, float expected, string message, float tolerance = .00003f)
        => Check(float.IsFinite(actual) && MathF.Abs(actual - expected) <= tolerance,
            $"{message}: {actual} != {expected}");
    private static bool Sloped(Tick t) => t.GroundTravel && t.SupportY > .5f && t.SupportY < .99f;

    internal static int Run(string? output)
    {
        _checks = 0; var rows = new List<object>(); var failures = new List<string>();
        try
        {
            Check(!NetSession.Active && !DemoPlayback.IsActive && !DemoRecorder.IsRecording,
                "fresh offline nonrecording process");
            Check(ServerSim.Available(out string reason), reason); Headless.Enter();
            void Case(string label, Action work)
            {
                try { _lastCapture = null; work(); Console.WriteLine("GROUND SLOPE SCENE PASS " + label); }
                catch (Exception error)
                {
                    failures.Add(label + ": " + error);
                    rows.Add(new { label, failure = error.ToString(), partial = _lastCapture });
                    Console.Error.WriteLine("GROUND SLOPE SCENE FAIL " + label + ": " + error);
                }
            }
            foreach (Hunter hunter in Hunters)
            {
                foreach (MovementModel model in new[] { MovementModel.Quake3, MovementModel.Traversal })
                {
                    Case(hunter + "/" + model + "/uphill-crest", () =>
                    {
                        Capture run = Record(hunter, model, "earned-uphill-crest", new(-30, .5f, -16),
                            Vector3.UnitZ, 280, _ => new(true)); rows.Add(run);
                        Tick[] slope = run.Ticks.Where(t => Sloped(t) && t.Z > -4 && t.Z < 4).ToArray();
                        Check(slope.Length > 30, "sustained genuine walkable uphill support");
                        Check(slope.All(t => t.Standing && t.VelocityY > 0), "uphill travel climbs with real standing contact");
                        Check(slope.All(t => MathF.Abs(t.Speed - Quake3MovementProfileV4.GroundSpeed) < .03f),
                            "sustained uphill retains ordinary ground surface speed");
                        Check(slope.All(t => t.PlanarSpeed < t.Speed - .1f),
                            "uphill speed budget includes ascent rather than restoring planar speed separately");
                        Check(run.Ticks.Any(t => t.Z > 9 && !t.Standing && !t.GroundTravel),
                            "crest departure retires cached ground redirection while airborne");
                        Check(run.Ticks.Any(t => t.Z > 9 && t.Standing && t.Y < 1),
                            "real crest drop subsequently lands on the authored base floor");
                    });
                    Case(hunter + "/" + model + "/downhill", () =>
                    {
                        Capture run = Record(hunter, model, "earned-downhill", new(-30, 4, 6),
                            -Vector3.UnitZ, 170, _ => new(true)); rows.Add(run);
                        Tick[] slope = run.Ticks.Where(t => Sloped(t) && t.Z > -5 && t.Z < 3).ToArray();
                        Check(slope.Length > 30 && slope.Count(t => t.Standing && t.VelocityY < 0) > 25,
                            "sustained downhill follows fresh walkable physical support");
                        Check(slope.All(t => MathF.Abs(t.Speed - Quake3MovementProfileV4.GroundSpeed) < .03f),
                            "ordinary downhill walking has no geometry-generated acceleration");
                        Check(run.Ticks.Any(t => t.Z < -9 && t.Standing && t.SupportAfterY > .99f),
                            "downhill-to-flat transition obtains a new real flat support");
                    });
                    Case(hunter + "/" + model + "/neutral", () =>
                    {
                        bool released = false;
                        Command Neutral(State s) { released |= s.Player.Position.Z > -2; return new(!released); }
                        Capture run = Record(hunter, model, "earned-uphill-neutral", new(-30, .5f, -16),
                            Vector3.UnitZ, 260, Neutral); rows.Add(run);
                        Tick[] neutral = run.Ticks.Where(t => Sloped(t) && !t.Forward).ToArray();
                        Check(neutral.Length > 20 && neutral[0].BeforeSpeed > 6,
                            "neutral slope braking begins from keyboard-earned running speed");
                        Check(neutral.All(t => t.Speed <= t.BeforeSpeed + .003f),
                            "ordinary neutral ramp contact cannot manufacture momentum");
                        Check(run.Ticks[^1].Standing && run.Ticks[^1].Speed < .002f,
                            "ordinary neutral player settles to rest on the actual slope");
                    });
                    Case(hunter + "/" + model + "/cross-slope", () =>
                    {
                        Capture run = Record(hunter, model, "earned-cross-slope", new(-32.5f, 2.5f, 0),
                            Vector3.UnitX, 44, _ => new(true)); rows.Add(run);
                        Tick[] slope = run.Ticks.Where(t => Sloped(t) && t.Number > 14).ToArray();
                        Check(slope.Length > 20 && run.Ticks[^1].X > -29,
                            "real keyboard movement crosses the authored ramp");
                        Check(slope.All(t => MathF.Abs(t.VelocityY) < .0001f),
                            "cross-slope travel has no invented upward or downward impulse");
                        Check(slope.All(t => MathF.Abs(t.Speed - Quake3MovementProfileV4.GroundSpeed) < .03f),
                            "cross-slope surface speed equals ordinary ground speed");
                    });
                    Case(hunter + "/" + model + "/wall", () =>
                    {
                        Capture run = Record(hunter, model, "earned-flat-wall-stop", new(42, .5f, 20),
                            Vector3.UnitX, 150, _ => new(true)); rows.Add(run);
                        Tick[] end = run.Ticks.TakeLast(20).ToArray();
                        Check(run.Ticks.Any(t => t.Speed > 6) && end.All(t => t.X > 47 && t.X < 48),
                            "earned running speed reaches actual east wall collision");
                        Check(end.All(t => t.Speed < .001f && t.Standing),
                            "support redirection never restores velocity removed by native wall collision");
                    });
                    Case(hunter + "/" + model + "/ramp-jump-landing", () =>
                    {
                        bool requested = false;
                        Command Jump(State s)
                        {
                            bool jump = !requested && s.Player.Position.Z > -2
                                && s.Player.Flags1.TestFlag(PlayerFlags1.Standing);
                            requested |= jump; return new(true, Jump: jump);
                        }
                        Capture run = Record(hunter, model, "earned-ramp-jump-landing", new(-30, .5f, -16),
                            Vector3.UnitZ, 300, Jump); rows.Add(run);
                        Tick[] jumps = run.Ticks.Where(t => t.AcceptedJump).ToArray();
                        Check(jumps.Length == 1 && jumps[0].StandingBefore && !jumps[0].GroundTravel,
                            "one real ramp jump bypasses ground redirection in its acceptance tick");
                        Check(run.Ticks.Any(t => t.Number > jumps[0].Number && !t.Standing),
                            "accepted jump actually leaves collision support");
                        Check(run.Ticks.Any(t => t.Number > jumps[0].Number && !t.StandingBefore && t.Standing
                            && !t.GroundTravel), "first airborne landing retains native collision ownership");
                    });
                }
                SlideCases(hunter, rows, Case);
                foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum })
                    Case(hunter + "/" + model + "/control", () =>
                    {
                        Capture run = Record(hunter, model, "unchanged-model-uphill-control", new(-30, .5f, -16),
                            Vector3.UnitZ, 240, _ => new(true)); rows.Add(run);
                        Check(run.Ticks.All(t => !t.GroundTravel && !t.Sliding),
                            "Classic and immutable Momentum v1 never enter the slope adapter");
                        Check(run.Ticks.Max(t => t.Y) > 1.5f && run.Ticks.Max(t => t.Speed) > 1,
                            "control really moves uphill through authored collision");
                        Check(model == MovementModel.Native ? run.SolverSteps == 0 : run.SolverSteps > 0,
                            "Classic native ownership and Momentum v1 selection remain distinct");
                    });
            }
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        if (output != null)
        {
            string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { schema = 1,
                scope = "Offline CPU keyboard, admitted match models and authored native collision; initial placement only; no manufactured speed/support/jumps; no GPU, network or replay acceptance",
                checks = _checks, failures, rows }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"GROUND SLOPE SCENE {(failures.Count == 0 ? "PASS" : "FAIL")} {_checks} assertions, {failures.Count} failed cases");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void SlideCases(Hunter hunter, List<object> rows, Action<string, Action> runCase)
    {
        runCase(hunter + "/Traversal/earned-uphill-slide", () =>
        {
            bool upPressed = false;
            Command Uphill(State s) { upPressed |= s.Player.Position.Z > -3; return new(true, upPressed); }
            Capture up = Record(hunter, MovementModel.Traversal, "earned-uphill-slide",
                new(-30, .5f, -16), Vector3.UnitZ, 260, Uphill); rows.Add(up);
            Tick[] uphill = up.Ticks.Where(t => Sloped(t) && t.Sliding).ToArray();
            Check(uphill.Length > 25 && uphill[0].BeforeSpeed > 6,
                "fresh Ctrl enters an actual earned uphill slide");
            Check(uphill.All(t => t.Speed < t.BeforeSpeed && t.BeforeSpeed - t.Speed < .2f),
                "uphill slide loses speed gradually on each actual slope tick");
            Check(uphill[^1].Speed < uphill[0].Speed - .5f,
                "uphill slide loses a measurable amount of speed over time");
            Check(up.Ticks.Count(t => t.Sliding) <= TraversalMovementProfileV5.SlideTicks,
                "held Ctrl cannot refresh a finite uphill slide window");
        });

        runCase(hunter + "/Traversal/elevated-deck-floor-contact", () =>
        {
            // Crouching on the continuous elevated deck must not lose a real floor
            // because the shortened broad query rounded outside its resting plane.
            bool deckPressed = false;
            Command Deck(State s) { deckPressed |= s.Player.Position.Z > -3; return new(true, deckPressed); }
            Capture deck = Record(hunter, MovementModel.Traversal, "elevated-deck-crouch-retains-real-floor",
                new(10, 8.5f, -12), Vector3.UnitZ, 230, Deck); rows.Add(deck);
            int deckEntry = Array.FindIndex(deck.Ticks, t => t.Sliding);
            Check(deckEntry >= 0, "actual running deck approach earns a fresh crouch slide");
            Tick[] deckInterior = deck.Ticks.Skip(deckEntry).Where(t => t.Z < -.5f).ToArray();
            Check(deckInterior.Length > 12 && deckInterior.All(t => t.Standing && t.Sliding
                && t.GroundTravel && t.SupportY > .99f && t.FreshSupportAfter),
                "crouched elevated-deck travel retains continuous real floor contact and the active slide");
            Check(deckInterior.All(t => MathF.Abs(t.Y - 8.5f) < .0001f),
                "retaining the floor candidate does not inflate the physical body or move it vertically");
            Check(deck.Ticks.Any(t => t.Z > -.5f && t.Z < 0 && t.Standing && t.Sliding),
                "the slide survives the continuous deck up to its actual end");
        });

        runCase(hunter + "/Traversal/real-crest-ledge-cancellation", () =>
        {
            bool crestPressed = false;
            Command Crest(State s) { crestPressed |= s.Player.Position.Z > 6.5f; return new(true, crestPressed); }
            Capture crest = Record(hunter, MovementModel.Traversal, "uphill-crest-real-ledge-cancels-slide",
                new(-30, .5f, -16), Vector3.UnitZ, 320, Crest); rows.Add(crest);
            int departure = Array.FindIndex(crest.Ticks, t => t.Sliding && !t.Standing);
            Check(departure > 0 && departure + 1 < crest.Ticks.Length,
                "an earned uphill slide actually leaves the authored crest and its supporting geometry");
            Check(MathF.Abs(crest.Ticks[departure].Z - 8) <= crest.BodyRadius + .002f
                && crest.Ticks[departure].Y > 4,
                "actual loss of support occurs within the biped radius of the elevated crest");
            Tick preceding = crest.Ticks[departure - 1];
            Check(preceding.Standing && preceding.FreshSupportAfter
                && preceding.SupportAfterY > .5f && preceding.SupportAfterY < .99f,
                "the departure immediately follows a fresh physical sloped support witness");
            Check(!crest.Ticks[departure + 1].StandingBefore && !crest.Ticks[departure + 1].Sliding
                && !crest.Ticks[departure + 1].GroundTravel && crest.Ticks[departure + 1].Z > 8,
                "the next input tick cancels the slide after a genuine ledge departure");
            Check(crest.Ticks.Skip(departure + 1).All(t => !t.Sliding),
                "held Ctrl cannot restart a cancelled slide while airborne or after landing");
            Check(crest.Ticks.Skip(departure + 1).Any(t => !t.Standing && !t.GroundTravel)
                && crest.Ticks.Skip(departure + 1).Any(t => t.Standing && t.Y < 1),
                "real ledge departure has unsupported flight followed by an actual lower-floor landing");
        });

        runCase(hunter + "/Traversal/supported-downhill-modest-gain", () =>
        {
            bool downPressed = false;
            Command Downhill(State s)
            {
                downPressed |= ReadyForDownhillSlide(s);
                return new(true, downPressed);
            }
            Capture down = Record(hunter, MovementModel.Traversal, "earned-supported-downhill-slide",
                new(10, 7.8f, 1), Vector3.UnitZ, 180, Downhill, settleFrames: 120); rows.Add(down);
            Check(down.Ticks[0].BeforeSpeed < .002f,
                "modest-gain slope fixture starts at real neutral rest rather than retaining a deck-drop impulse");
            Tick[] downhill = down.Ticks.Where(t => Sloped(t) && t.Sliding && t.SupportY < .8f).ToArray();
            Check(downhill.Length > 15, "earned slide obtains actual untagged steep walkable descent support");
            Check(downhill.Any(t => t.Speed > t.BeforeSpeed + .005f),
                "downhill slope force can modestly overcome ordinary slide friction");
            Check(downhill.All(t => t.Speed <= t.BeforeSpeed + GroundSlopeMovement.SlideGravity / 60 + .003f),
                "actual downhill gain is bounded by one fixed-tick slope force");
            Check(down.Ticks.Count(t => t.Sliding) <= TraversalMovementProfileV5.SlideTicks,
                "held Ctrl cannot refresh a finite slide window on slopes");
        });

        runCase(hunter + "/Traversal/deck-descent-overspeed-braking", () =>
        {
            // The deck approach can naturally earn a large fall-derived speed before
            // supported slide entry. Above the force/friction equilibrium it should
            // brake, so it is intentionally separate from the modest-gain fixture.
            bool downPressed = false;
            Command Downhill(State s)
            {
                downPressed |= ReadyForDownhillSlide(s);
                return new(true, downPressed);
            }
            Capture overspeed = Record(hunter, MovementModel.Traversal, "deck-descent-earned-overspeed-braking",
                new(10, 8.5f, -12), Vector3.UnitZ, 230, Downhill); rows.Add(overspeed);
            Tick[] braking = overspeed.Ticks.Where(t => Sloped(t) && t.Sliding && t.SupportY < .8f
                && t.BeforeSpeed > GroundSlopeMovement.SlideGravity * MathF.Sqrt(1 - t.SupportY * t.SupportY)
                    / TraversalMovementProfileV5.SlideFriction + .1f).ToArray();
            Check(braking.Length > 1 && braking[0].BeforeSpeed > Quake3MovementProfileV4.GroundSpeed * 1.5f,
                "actual deck descent earns repeated supported slide samples above the slope force/friction equilibrium");
            Check(braking.All(t => t.Speed < t.BeforeSpeed),
                "slope force does not defeat friction for already excessive downhill speed");
            Check(braking.All(t => t.Speed <= t.BeforeSpeed + GroundSlopeMovement.SlideGravity / 60 + .003f),
                "overspeed descent still applies at most one bounded slope-force step");
            Check(overspeed.Ticks.Count(t => t.Sliding) <= TraversalMovementProfileV5.SlideTicks,
                "overspeed cannot refresh a finite slide window");
        });
    }

    private static bool ReadyForDownhillSlide(State s)
    {
        Vector3 support = (Vector3)Support.GetValue(s.Player)!;
        bool fresh = s.Scene.FrameCount > 0
            && (ulong)SupportFrame.GetValue(s.Player)! == s.Scene.FrameCount - 1;
        return fresh && s.Player.Flags1.TestFlag(PlayerFlags1.Standing)
            && support.Y > .5f && support.Y < .8f && GroundSlopeMovement.Walkable(support)
            && MathF.Abs(Vector3.Dot(s.Player.Speed, support)) <= .0005f
            && s.Player.Speed.Length * 30 >= TraversalMovementProfileV5.SlideMinimumSpeed;
    }

    private static Capture Record(Hunter hunter, MovementModel model, string scenario,
        Vector3 placement, Vector3 facing, int length, Func<State, Command> choose, int settleFrames = 12)
    {
        object?[] arguments = { hunter, model, null };
        Scene scene = (Scene)typeof(TraversalSceneTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        var keyboard = (KeyboardState)arguments[2]!;
        try
        {
            PlayerEntity p = scene.Players.Main!; p.Controls.Crouch.Key = Keys.LeftControl;
            Check(p.Speed.X == 0 && p.Speed.Z == 0, "native spawn has no fabricated horizontal speed");
            p.Reposition(placement, facing, NodeRef.None); p.PrevPosition = placement;
            for (int index = 0; index < settleFrames; index++) scene.OnSimulationFrame();
            Check(p.Flags1.TestFlag(PlayerFlags1.Standing), "initial placement settles on real authored support");
            var ticks = new Tick[length]; long firstSteps = scene.DeveloperMomentumSteps;
            int overflows = p.ModCollisionContactOverflows, recovery = p.ModCollisionRecoveryFallbacks;
            bool lastJump = false, lastCrouch = false;
            for (int index = 0; index < length; index++)
            {
                Command command = choose(new(index, p, scene));
                bool standingBefore = p.Flags1.TestFlag(PlayerFlags1.Standing);
                float beforeSpeed = p.Speed.Length * 30;
                Vector3 previous = p.Position;
                Vector3 previousSupport = (Vector3)Support.GetValue(p)!;
                bool freshBefore = scene.FrameCount > 0
                    && (ulong)SupportFrame.GetValue(p)! == scene.FrameCount - 1;
                SetKey(keyboard, Keys.W, command.Forward); SetKey(keyboard, Keys.Space, command.Jump);
                SetKey(keyboard, Keys.LeftControl, command.Crouch);
                long steps = scene.DeveloperMomentumSteps; ulong frame = scene.FrameCount;
                scene.OnSimulationFrame();
                _lastCapture = new { hunter = hunter.ToString(), model = model.ToString(), scenario,
                    tick = index + 1, position = new[] { p.Position.X, p.Position.Y, p.Position.Z },
                    velocity = new[] { p.Speed.X, p.Speed.Y, p.Speed.Z }, beforeSpeed, standingBefore,
                    standing = p.Flags1.TestFlag(PlayerFlags1.Standing), freshBefore,
                    support = new[] { previousSupport.X, previousSupport.Y, previousSupport.Z },
                    solverSample = scene.LastMomentumStep is { } captured ? new {
                        captured.SimulationFrame, phase = captured.ContactPhase.ToString(),
                        captured.Input.Grounded, captured.Sliding,
                        before = new[] { captured.NativeVelocityBefore.X, captured.NativeVelocityBefore.Y, captured.NativeVelocityBefore.Z },
                        after = new[] { captured.NativeVelocityAfter.X, captured.NativeVelocityAfter.Y, captured.NativeVelocityAfter.Z },
                        groundSupport = captured.GroundSupport is { } witness ? new[] { witness.X, witness.Y, witness.Z } : null
                    } : null, recordedTicks = ticks.Take(index).ToArray() };
                Check(p.Health > 0 && p.ModIsInPlay && !p.IsBot && !p.IsAltForm && !NetSession.Active,
                    "living offline owner biped follows production input");
                Check(p.Controls.MoveUp.IsDown == command.Forward && p.Controls.Jump.IsDown == command.Jump
                    && p.Controls.Jump.IsPressed == (command.Jump && !lastJump)
                    && p.Controls.Crouch.IsDown == command.Crouch
                    && p.Controls.Crouch.IsPressed == (command.Crouch && !lastCrouch),
                    "keyboard levels and edges reach real controls");
                Check(float.IsFinite(p.Position.LengthSquared) && float.IsFinite(p.Speed.LengthSquared),
                    "finite production position and velocity");
                Check((p.Position - previous).Length < 1, "bounded native collision displacement");
                Check(p.ModCollisionContactOverflows == overflows && p.ModCollisionRecoveryFallbacks == recovery,
                    "no contact overflow or recovery fallback hides the slope response");
                bool groundTravel = false, accepted = false; float supportY = 0, expectedSpeed = 0;
                long count = scene.DeveloperMomentumSteps - steps;
                Check(model == MovementModel.Native ? count == 0 : count is >= 0 and <= 1,
                    "Classic never solves and eligible models solve at most once per tick");
                if (count == 1)
                {
                    MomentumStepSample sample = scene.LastMomentumStep!.Value;
                    Check(sample.SimulationFrame == frame && sample.Slot == p.SlotIndex, "same-tick solver witness");
                    Check(BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y)
                        == BitConverter.SingleToInt32Bits(sample.NativeVelocityAfter.Y),
                        "pure planar solver preserves native Y bits");
                    accepted = sample.ContactPhase == MomentumContactPhase.LegalJump;
                    groundTravel = sample.GroundSupport.HasValue;
                    Vector3 solverInput = groundTravel
                        ? GroundSlopeMovement.Flatten(sample.NativeVelocityBefore) * 30
                        : new(sample.NativeVelocityBefore.X * 30, 0, sample.NativeVelocityBefore.Z * 30);
                    Vector3 solved = model == MovementModel.Traversal
                        ? TraversalMovementProfileV5.Step(solverInput, sample.Input, sample.Crouched, sample.Sliding).Velocity
                        : model == MovementModel.Quake3 ? Quake3MovementProfileV4.Step(solverInput, sample.Input).Velocity
                        : MomentumProfileV1.Step(solverInput, sample.Input).Velocity;
                    Near(sample.NativeVelocityAfter.X, solved.X / 30, "selected solver X output");
                    Near(sample.NativeVelocityAfter.Z, solved.Z / 30, "selected solver Z output");
                    if (sample.GroundSupport is { } support)
                    {
                        Check(model is MovementModel.Quake3 or MovementModel.Traversal
                            && standingBefore && sample.Input.Grounded && !accepted,
                            "ground travel requires a genuine eligible nonjumping grounded tick");
                        Check(freshBefore && GroundSlopeMovement.Walkable(previousSupport)
                            && (support - previousSupport).Length < .00001f,
                            "ground redirection uses the preceding physical support witness");
                        supportY = support.Y;
                        Vector3 surface = GroundSlopeMovement.Follow(solved, support);
                        if (model == MovementModel.Traversal && sample.Sliding)
                            surface = GroundSlopeMovement.Slide(surface, support, sample.Input.DeltaSeconds);
                        expectedSpeed = surface.Length;
                        Check(p.Speed.Length * 30 <= expectedSpeed + .003f,
                            "collision cannot restore a greater magnitude than this tick's grounded solve");
                        if (supportY > .5f && supportY < .99f && p.Flags1.TestFlag(PlayerFlags1.Standing)
                            && scenario != "earned-cross-slope")
                            Near(p.Speed.Length * 30, expectedSpeed, "interior ramp contact preserves solved surface magnitude", .003f);
                    }
                    Check(!accepted || !groundTravel, "accepted jump cannot redirect its vertical impulse onto support");
                    Check(standingBefore || !groundTravel, "airborne and first-landing ticks cannot normalize fall energy");
                }
                Vector3 afterSupport = (Vector3)Support.GetValue(p)!;
                bool freshAfter = (ulong)SupportFrame.GetValue(p)! == frame;
                ticks[index] = new(index + 1, p.Position.X, p.Position.Y, p.Position.Z, p.Speed.Length * 30,
                    p.Speed.Xz.Length * 30, p.Speed.Y, beforeSpeed, standingBefore,
                    p.Flags1.TestFlag(PlayerFlags1.Standing), command.Forward, p.ModSliding, accepted,
                    groundTravel, supportY, expectedSpeed, freshAfter, afterSupport.Y);
                lastJump = command.Jump; lastCrouch = command.Crouch;
            }
            return new(hunter.ToString(), model.ToString(), scenario,
                new[] { placement.X, placement.Y, placement.Z }, p.Values.BipedColRadius / 4096f,
                ticks, scene.DeveloperMomentumSteps - firstSteps);
        }
        finally { MomentumSceneTests.Release(scene); }
    }
}
