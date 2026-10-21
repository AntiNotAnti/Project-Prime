using System;
using System.Diagnostics;
using MphRead;
using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Asset-free contracts for explicit rule selection and the stateless profile.
// These checks do not claim live input, collision, ramp contact or weapon acceptance.
internal static class TraversalRulesTests
{
    private static int _checks;
    private static readonly MovementModel[] Models = { MovementModel.Native, MovementModel.Momentum,
        MovementModel.Quake3, MovementModel.Traversal, MovementModel.Parkour };
    private const MatchModifierFlags MovementBits = MatchModifierFlags.MomentumMovement
        | MatchModifierFlags.Quake3Movement | MatchModifierFlags.TraversalMovement | MatchModifierFlags.ParkourMovement;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException(label); }
    private static void Near(float actual, float expected, string label, float tolerance = .000002f)
        => Check(float.IsFinite(actual) && MathF.Abs(actual - expected) <= tolerance,
            $"{label}: {actual} != {expected}");
    private static bool Finite(Vector3 value) => float.IsFinite(value.X)
        && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static float PlanarSpeed(Vector3 value) => new Vector2(value.X, value.Z).Length;
    private static double Heading(Vector3 value) => Math.Atan2(value.Z, value.X);
    private static void Reject(Action action, string label)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, label);
    }
    private static MomentumInput Input(Vector2 axes, bool grounded = false, float dt = 1f / 60)
        => new(axes, Vector3.UnitZ, Vector3.UnitX, TraversalMovementProfileV1.GroundSpeed, grounded, dt);
    private static MatchDefinition Definition(MovementModel model) => new()
    {
        RoomKey = "MP3 PROVING GROUND", Mode = GameMode.Battle, MovementModel = model,
        TimeLimitSeconds = 300, PointGoal = 7
    };

    internal static int Run()
    {
        try
        {
            _checks = 0;
            SelectionAndRules(); ConfigurationAndWire(); AirControl(); GroundAndVerticalParity();
            FiniteContract(); RampContract(); AllocationContract();
            Console.WriteLine($"TRAVERSAL RULES PASS {_checks} assertions; asset-free selection, profile and ramp math; no gameplay qualification");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("TRAVERSAL RULES FAIL " + error); return 1; }
    }

    private static void SelectionAndRules()
    {
        Check((byte)MovementModel.Native == 0 && (byte)MovementModel.Momentum == 1
            && (byte)MovementModel.Quake3 == 2 && (byte)MovementModel.Traversal == 3 && (byte)MovementModel.Parkour == 4,
            "five stable explicit movement enum values");
        Check((uint)MatchModifierFlags.TraversalMovement == 1u << 17,
            "Traversal has its own bit17 rule identity");
        Check(new MatchDefinition().MovementModel == MovementModel.Native
            && new SceneGameState(new ScenePlayerRegistry()).MovementModel == MovementModel.Native,
            "match and scene defaults remain Classic");
        foreach (string? text in new string?[] { null, "", "Native", "native", "Classic", "CLASSIC" })
            Check(MovementModelRules.TryParse(text, out var model) && model == MovementModel.Native,
                "default and Classic names remain Native");
        foreach (var item in new[] { ("Momentum", MovementModel.Momentum), ("momentum", MovementModel.Momentum),
            ("Quake3", MovementModel.Quake3), ("Quake III", MovementModel.Quake3),
            ("Traversal", MovementModel.Traversal), ("traversal", MovementModel.Traversal),
            ("TRAVERSAL", MovementModel.Traversal) })
            Check(MovementModelRules.TryParse(item.Item1, out var model) && model == item.Item2,
                "explicit movement name parses " + item.Item1);
        foreach (string text in new[] { "0", "1", "2", "3", "255", " Traversal", "Traversal ",
            "Traversal,Quake3", "unknown", " " })
            Check(!MovementModelRules.TryParse(text, out _), "reject noncanonical movement name " + text);
        for (int index = 0; index < Models.Length; index++)
        {
            MovementModel model = Models[index];
            Check(MovementModelRules.IsDefined(model), "all five movement choices are defined");
            Check(MovementModelRules.Next(model) == Models[(index + 1) % Models.Length],
                "selection cycles all five explicit modes");
            Check(MovementModelRules.UsesMomentumSolver(model) == (model != MovementModel.Native),
                "Classic excludes the horizontal adapter");
            Check(MovementModelRules.FromRules(MovementModelRules.Modifier(model)) == model,
                "each explicit modifier maps back to its mode");
            Check(MovementModelRules.ProfileVersion(model) == new[] { 0, 1, 4, 5, 1 }[index],
                "existing profile versions remain distinct from live Traversal v2");
            Check(MovementModelRules.DisplayName(model).Length > 0
                && MovementModelRules.DisplayName(model) != "Unsupported", "all modes have presentation labels");
            var match = Definition(model);
            Check((match.Rules & MovementBits) == MovementModelRules.Modifier(model)
                && MovementModelRules.ValidRules(match.Rules), "definition selects exactly its canonical movement bit");
            Check(LobbyRules.ValidateDefinition(match, out _) == LobbyResultCode.Ok,
                "ordinary match admits selected mode");
            foreach (GameMode legacyMode in new[] { GameMode.InstaGib, GameMode.OneInTheChamber })
                Check((match with { Mode = legacyMode }).NormalizeLegacy().MovementModel == model,
                    "legacy game-mode normalization preserves movement selection");
            if (model != MovementModel.Native)
            {
                Check(LobbyRules.ValidateDefinition(match with { BalancedMode = true }, out _)
                    == LobbyResultCode.InvalidConfiguration, "custom profile excludes Balanced gameplay");
                Check(LobbyRules.ValidateDefinition(match with { EnhancedHunters = true }, out _, allowRetiredEnhanced: true)
                    == LobbyResultCode.InvalidConfiguration, "custom profile excludes archived Enhanced gameplay");
            }
        }
        Check(MovementModelRules.DisplayName(MovementModel.Native) == "Classic"
            && MovementModelRules.DisplayName(MovementModel.Traversal) == "Traversal",
            "explicit selection labels preserve Classic default");
        foreach (MovementModel first in Models)
        foreach (MovementModel second in Models)
        {
            if (first == MovementModel.Native || second == MovementModel.Native || first == second) continue;
            MatchModifierFlags conflicting = MovementModelRules.Modifier(first) | MovementModelRules.Modifier(second);
            Check(!MovementModelRules.ValidRules(conflicting)
                && !MovementModelRules.IsDefined(MovementModelRules.FromRules(conflicting)),
                "every pair of nondefault movement bits is mutually exclusive");
        }
        Check(!MovementModelRules.ValidRules(MovementBits), "all four custom bits cannot be combined");
        foreach (MovementModel invalid in new[] { (MovementModel)5, (MovementModel)255 })
        {
            Check(!MovementModelRules.IsDefined(invalid), "unknown movement enum remains undefined");
            Reject(() => MovementModelRules.Modifier(invalid), "unknown enum cannot become a rule bit");
            Reject(() => MovementModelRules.ProfileVersion(invalid), "unknown enum cannot borrow a profile version");
            Reject(() => MovementModelRules.Next(invalid), "unknown enum cannot enter selection cycle");
            Check(LobbyRules.ValidateDefinition(Definition(invalid), out _) == LobbyResultCode.InvalidConfiguration,
                "unknown enum fails match admission");
        }
        Check(!MovementModelRules.ValidRules((MatchModifierFlags)(1u << 20)),
            "next unassigned modifier bit remains invalid");
    }

    private static void ConfigurationAndWire()
    {
        var state = new SceneGameState(new ScenePlayerRegistry());
        // Start from Traversal, then explicitly visit each other mode to expose stale opt-in state.
        foreach (MovementModel model in new[] { MovementModel.Traversal, MovementModel.Native,
            MovementModel.Momentum, MovementModel.Quake3, MovementModel.Traversal, MovementModel.Parkour, MovementModel.Native })
        {
            var match = Definition(model) with { ThirdPersonCamera = true, FriendlyFire = true };
            match.ApplyInitialConfiguration(state);
            Check(state.MovementModel == model && state.ThirdPersonCamera && state.FriendlyFire
                && state.MatchTime == 300 && state.PointGoal == 7, "initial scene configuration owns selected complete rules");
            Check(state.ForScene(new ScenePlayerRegistry()).MovementModel == model,
                "scene bootstrap retains explicit movement without inheriting a prior mode");
            match.ApplyModifiers(state);
            Check(state.MovementModel == model && state.ThirdPersonCamera, "modifier application preserves independent camera rule");
            var packet = new SessionStatePacket { Phase = SessionPhase.Lobby, Policy = ServerSessionPolicy.Lobby,
                Revision = 9, MatchId = 3, AuthorityEpoch = 7, OwnerSlot = 0, MaxPlayers = 8,
                Match = match, WorldProfile = MatchWorldProfile.Resolve(8), MapGeneration = 1 };
            byte[] bytes = new byte[SessionStatePacket.Size]; packet.Write(bytes);
            Check(SessionStatePacket.TryRead(bytes, out var decoded) && decoded.Match == match,
                "session packet roundtrip preserves selected movement and independent modifiers");
            var matchPacket = new MatchStatePacket { RoomKey = match.RoomKey, NextRoomKey = "",
                Mode = (byte)GameMode.Battle, MatchId = 3, AuthorityEpoch = 7, RuleBits = (uint)match.Rules };
            byte[] matchBytes = new byte[MatchStatePacket.Size]; matchPacket.Write(matchBytes);
            var read = MatchStatePacket.Read(matchBytes);
            Check(read.RuleBits == matchPacket.RuleBits
                && MovementModelRules.FromRules((MatchModifierFlags)read.RuleBits) == model,
                "match packet preserves full movement identity including bit17");
        }
        Check(Definition(MovementModel.Traversal).ModifierSummary.Contains("Traversal", StringComparison.Ordinal),
            "match summary exposes explicit Traversal selection");
    }

    private static void AirControl()
    {
        float ground = TraversalMovementProfileV1.GroundSpeed;
        Check(TraversalMovementProfileV1.ProfileVersion == 1 && ground == Quake3MovementProfileV2.GroundSpeed
            && TraversalMovementProfileV1.AirTurnRadiansPerSecond == 1.8f,
            "reviewed Traversal v1 speed and turn constants");
        Check(TraversalMovementProfileV1.Tuning.AirAcceleration == 2
            && TraversalMovementProfileV1.Tuning.AirAcceleration > Quake3MovementProfileV2.Tuning.AirAcceleration,
            "Traversal explicitly increases air acceleration");
        var forward = Input(new Vector2(0, 1));
        Vector3 rest = new(0, -.125f, 0);
        Vector3 traversal = TraversalMovementProfileV1.Step(rest, forward).Velocity;
        Vector3 quake = Quake3MovementProfileV2.Step(rest, forward).Velocity;
        Near(PlanarSpeed(traversal), ground * 2 / 60, "air acceleration from rest");
        Near(PlanarSpeed(traversal), PlanarSpeed(quake) * 2, "air acceleration doubles Quake III");
        Vector3 incoming = new(ground * 2, -.125f, 0);
        Vector3 accelerated = MomentumMath.Step(incoming, forward, TraversalMovementProfileV1.Tuning);
        Vector3 steered = TraversalMovementProfileV1.Step(incoming, forward).Velocity;
        Vector3 quakeTurn = Quake3MovementProfileV2.Step(incoming, forward).Velocity;
        Near(PlanarSpeed(steered), PlanarSpeed(accelerated), "air steering preserves acceleration result magnitude");
        Near((float)(Heading(steered) - Heading(accelerated)), 1.8f / 60, "air turn has fixed-time angular bound");
        Check(Heading(steered) > Heading(quakeTurn) && steered.Z > quakeTurn.Z,
            "perpendicular input turns more strongly than Quake III");
        Check(PlanarSpeed(steered) > ground * 2 && !TraversalMovementProfileV1.Step(incoming, forward).SpeedLimited
            && !TraversalMovementProfileV1.Step(incoming, forward).IncomingOverCap,
            "perpendicular air acceleration retains externally supplied overspeed without a total-speed cap");
        var partial = forward with { MoveAxes = new Vector2(0, .25f) };
        Vector3 partialAccelerated = MomentumMath.Step(incoming, partial, TraversalMovementProfileV1.Tuning);
        Vector3 partialTurn = TraversalMovementProfileV1.Step(incoming, partial).Velocity;
        Near((float)(Heading(partialTurn) - Heading(partialAccelerated)), 1.8f / 60 * .25f,
            "quarter-stick scales angular control");
        Near(PlanarSpeed(TraversalMovementProfileV1.Step(rest, partial).Velocity), ground * 2 / 60 * .25f,
            "quarter-stick scales acceleration");
        Vector3 coasting = new(ground * 3, -7, ground * 2);
        Check(TraversalMovementProfileV1.Step(coasting, Input(Vector2.Zero)).Velocity == coasting,
            "no-input airborne motion coasts exactly without turn, drag or magnitude clamp");
        Check(TraversalMovementProfileV1.Step(coasting, forward with { DeltaSeconds = 0 }).Velocity == coasting,
            "zero simulation time cannot accelerate or rotate");
        Check(TraversalMovementProfileV1.Step(incoming, forward with { DesiredSpeed = ground * 99 }).Velocity == steered,
            "external desired speed cannot mutate canonical profile");
        // MovementInput defines projected normalized basis directions; their source lengths do not tune steering.
        var diagonal = Input(new Vector2(-1, 1));
        Vector3 alongDiagonal = new(-ground, -.125f, ground);
        Check((TraversalMovementProfileV1.Step(alongDiagonal, diagonal with { FlatForward = Vector3.UnitZ * 8,
            FlatRight = Vector3.UnitX * 3 }).Velocity
            - TraversalMovementProfileV1.Step(alongDiagonal, diagonal).Velocity).Length <= .000002f,
            "basis scaling preserves normalized acceleration and steering directions");
    }

    private static void GroundAndVerticalParity()
    {
        foreach (Vector2 axes in new[] { Vector2.Zero, new Vector2(0, 1), new Vector2(-1, 1), new Vector2(.25f, -.5f) })
        foreach (Vector3 speed in new[] { Vector3.Zero, new Vector3(1, -.125f, 3), new Vector3(-17, 2, 11) })
        {
            var input = Input(axes, grounded: true);
            Check(TraversalMovementProfileV1.Step(speed, input) == Quake3MovementProfileV2.Step(speed, input),
                "grounded friction and acceleration remain exact Quake III behavior");
        }
        Check(TraversalMovementProfileV1.Tuning.GroundAcceleration == Quake3MovementProfileV2.Tuning.GroundAcceleration
            && TraversalMovementProfileV1.Tuning.GroundFriction == Quake3MovementProfileV2.Tuning.GroundFriction
            && TraversalMovementProfileV1.Tuning.StopSpeed == Quake3MovementProfileV2.Tuning.StopSpeed,
            "ground tuning does not change with stronger air control");
        foreach (float y in new[] { 0f, BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),
            float.Epsilon, -float.Epsilon, .125f, -23f, float.MaxValue, -float.MaxValue })
        foreach (bool grounded in new[] { false, true })
        {
            Vector3 velocity = new(4, y, 7);
            Vector3 result = TraversalMovementProfileV1.Step(velocity, Input(new Vector2(-1, 1), grounded)).Velocity;
            Check(BitConverter.SingleToInt32Bits(result.Y) == BitConverter.SingleToInt32Bits(y),
                "all finite Y values, including negative zero, remain bit-exact");
        }
    }

    private static void FiniteContract()
    {
        var valid = Input(new Vector2(0, 1));
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            foreach (Vector3 vector in new[] { new Vector3(invalid, 0, 0), new Vector3(0, invalid, 0), new Vector3(0, 0, invalid) })
            {
                Reject(() => TraversalMovementProfileV1.Step(vector, valid), "nonfinite incoming component rejected");
                Reject(() => TraversalMovementProfileV1.Step(Vector3.Zero, valid with { FlatForward = vector }),
                    "nonfinite forward basis rejected");
                Reject(() => TraversalMovementProfileV1.Step(Vector3.Zero, valid with { FlatRight = vector }),
                    "nonfinite right basis rejected");
            }
            Reject(() => TraversalMovementProfileV1.Step(Vector3.Zero, valid with { MoveAxes = new Vector2(invalid, 0) }),
                "nonfinite strafe axis rejected");
            Reject(() => TraversalMovementProfileV1.Step(Vector3.Zero, valid with { MoveAxes = new Vector2(0, invalid) }),
                "nonfinite forward axis rejected");
            Reject(() => TraversalMovementProfileV1.Step(Vector3.Zero, valid with { DeltaSeconds = invalid }),
                "nonfinite simulation time rejected");
        }
        Reject(() => TraversalMovementProfileV1.Step(Vector3.Zero, valid with { DeltaSeconds = -1 }), "negative time rejected");
        Reject(() => TraversalMovementProfileV1.Step(Vector3.Zero, valid with { FlatForward = Vector3.Zero }),
            "zero movement basis rejected");
        Reject(() => TraversalMovementProfileV1.Step(Vector3.Zero, valid with { FlatRight = Vector3.UnitZ }),
            "nonorthogonal basis rejected");
        Vector3 extreme = new(float.MaxValue, 0, float.MaxValue);
        Check(TraversalMovementProfileV1.Step(extreme, valid with { MoveAxes = Vector2.Zero }).Velocity == extreme,
            "finite large coasting components remain exact");
        Reject(() => TraversalMovementProfileV1.Step(extreme, valid),
            "steering result outside finite Vector3 range is rejected rather than returning Infinity");
        foreach (float dt in new[] { float.Epsilon, 1f / 240, 1f / 60, 1f, float.MaxValue })
        foreach (Vector2 axes in new[] { Vector2.Zero, new Vector2(1, 1), new Vector2(float.MaxValue, float.MaxValue) })
            Check(Finite(TraversalMovementProfileV1.Step(new Vector3(17, -3, 19), Input(axes, dt: dt)).Velocity),
                "representable finite extreme controls and time remain finite");
    }

    private static void RampContract()
    {
        Vector3 normal = new(-.6f, .8f, 0);
        Near(TraversalMovementProfileV1.RampLift(new Vector3(4, 3, 0), normal), 3,
            "uphill support preserves positive vertical slope velocity");
        Near(TraversalMovementProfileV1.RampLift(new Vector3(2, 9, 0), normal), 2,
            "ramp lift cannot exceed planar speed");
        Near(TraversalMovementProfileV1.RampLift(new Vector3(0, 3, 4), new Vector3(0, .8f, -.6f)), 3,
            "ramp lift works on the other horizontal axis");
        foreach (Vector3 velocity in new[] { new Vector3(-4, 3, 0), new Vector3(0, 3, 4),
            new Vector3(4, 0, 0), new Vector3(4, -3, 0), new Vector3(0, 3, 0), Vector3.Zero })
            Check(TraversalMovementProfileV1.RampLift(velocity, normal) == 0,
                "downhill, cross-slope, stationary and nonascending motion receive no ramp lift");
        foreach (Vector3 support in new[] { Vector3.UnitY, new Vector3(-.01f, .99f, 0),
            new Vector3(-.8f, .5f, 0), new Vector3(-1, 0, 0), -Vector3.UnitY, Vector3.Zero })
            Check(TraversalMovementProfileV1.RampLift(new Vector3(4, 3, 0), support) == 0,
                "flat, near-flat, steep, wall and absent support receive no ramp lift");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        foreach (Vector3 vector in new[] { new Vector3(invalid, 0, 0), new Vector3(0, invalid, 0), new Vector3(0, 0, invalid) })
        {
            Check(TraversalMovementProfileV1.RampLift(vector, normal) == 0, "nonfinite velocity supplies no ramp lift");
            Check(TraversalMovementProfileV1.RampLift(new Vector3(4, 3, 0), vector) == 0,
                "nonfinite support supplies no ramp lift");
        }
    }

    private static void AllocationContract()
    {
        var input = Input(new Vector2(-1, 1));
        Vector3 speed = new(4, -.125f, 7); float lift = 0;
        for (int index = 0; index < 10000; index++)
        {
            speed = TraversalMovementProfileV1.Step(speed, input).Velocity;
            lift += TraversalMovementProfileV1.RampLift(new Vector3(4, 3, 0), new Vector3(-.6f, .8f, 0));
        }
        long started = Stopwatch.GetTimestamp();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100000; index++)
        {
            speed = TraversalMovementProfileV1.Step(speed, input).Velocity;
            lift += TraversalMovementProfileV1.RampLift(new Vector3(4, 3, 0), new Vector3(-.6f, .8f, 0));
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        long elapsed = Stopwatch.GetTimestamp() - started;
        Check(allocated == 0 && Finite(speed) && float.IsFinite(lift) && lift > 0,
            "warmed valid movement and ramp paths allocate zero bytes");
        Console.WriteLine($"TRAVERSAL PROFILE BENCHMARK iterations=100000 elapsedMs={Stopwatch.GetElapsedTime(0, elapsed).TotalMilliseconds:F3} allocatedBytes={allocated}; observation only, no timing-budget assertion");
    }
}
