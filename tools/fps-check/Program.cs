using System.Text.Json;
using MphRead.Mods.Physics;

if (args.Length > 0 && args[0] == "-fpsconvertaudit")
    return FpsConversionAudit.Run(args.Length > 1 ? args[1] : Directory.GetCurrentDirectory(), args.Length > 2 ? args[2] : "artifacts/fps-audit");

int checks = 0;
void Assert(bool value, string name)
{
    if (!value) throw new Exception(name);
    checks++;
}
void Near(double actual, double expected, string name, double epsilon = 1e-6)
    => Assert(double.IsFinite(actual) && Math.Abs(actual - expected) <= epsilon, $"{name}: {actual:R} != {expected:R}");
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); } catch (T) { checks++; return; }
    throw new Exception("Expected " + typeof(T).Name + ": " + name);
}

// An edge on either half must reach exactly one native operation, isolated per player.
var cadenceMode = NativeMovementMode.DiagnosticCadence60;
bool firstPending = false, secondPending = false;
Assert(!NativeKernelDiagnostic.BeginBiped(cadenceMode, ref firstPending, 0, true, out bool jump) && !jump, "defer first-half edge");
Assert(NativeKernelDiagnostic.BeginBiped(cadenceMode, ref secondPending, 1, false, out jump) && !jump, "player input isolation");
Assert(NativeKernelDiagnostic.BeginBiped(cadenceMode, ref firstPending, 1, false, out jump) && jump, "consume buffered edge");
Assert(!NativeKernelDiagnostic.BeginBiped(cadenceMode, ref firstPending, 2, false, out jump) && !jump, "next first half");
Assert(NativeKernelDiagnostic.BeginBiped(cadenceMode, ref firstPending, 3, false, out jump) && !jump, "edge not replayed");
Assert(NativeKernelDiagnostic.BeginBiped(cadenceMode, ref firstPending, 5, true, out jump) && jump, "second-half edge");
firstPending = true;
Assert(NativeKernelDiagnostic.BeginBiped(NativeMovementMode.Legacy60, ref firstPending, 0, true, out jump) && jump && !firstPending,
    "default cadence unchanged and stale edge cleared");

// Strict checkpoint appendix: preserve both cadence phases and reject malformed state.
var cadenceBytes = new NativeCadenceCheckpoint(cadenceMode, [true, false]).Encode();
var cadenceRestored = NativeCadenceCheckpoint.Decode(cadenceBytes, 2);
Assert(cadenceRestored.Mode == cadenceMode && cadenceRestored.Pending.SequenceEqual(new[] { true, false }), "cadence state round trip");
foreach (int length in Enumerable.Range(0, cadenceBytes.Length))
    Throws<InvalidDataException>(() => NativeCadenceCheckpoint.Decode(cadenceBytes.AsSpan(0, length), 2), "truncated cadence appendix");
Throws<InvalidDataException>(() => NativeCadenceCheckpoint.Decode([1, 2, 2, 1, 0, 0], 2), "trailing cadence bytes");
Throws<InvalidDataException>(() => NativeCadenceCheckpoint.Decode([3, 2, 2, 1, 0], 2), "unknown cadence version");
Throws<InvalidDataException>(() => NativeCadenceCheckpoint.Decode([1, 3, 2, 1, 0], 2), "unknown cadence mode");
Throws<InvalidDataException>(() => NativeCadenceCheckpoint.Decode([1, 2, 1, 1, 0], 2), "cadence player count");
Throws<InvalidDataException>(() => NativeCadenceCheckpoint.Decode([1, 2, 2, 2, 0], 2), "noncanonical cadence boolean");
Throws<InvalidDataException>(() => NativeCadenceCheckpoint.Decode([1, 0, 2, 1, 0], 2), "edge in legacy checkpoint");
Throws<InvalidDataException>(() => new NativeCadenceCheckpoint(NativeMovementMode.Legacy60, [true]).Encode(), "reject stale edge when writing");

var fireAppendix = new NativeCadenceCheckpoint(cadenceMode, [true, false], [false, true]).Encode();
var fireRestored = NativeCadenceCheckpoint.Decode(fireAppendix, 2);
Assert(fireRestored.Pending.SequenceEqual(new[] { true, false })
    && fireRestored.FirePending!.SequenceEqual(new[] { false, true }), "independent jump/fire checkpoint edges");
Assert(NativeCadenceCheckpoint.Decode([1, 2, 2, 1, 0], 2).FirePending!.All(value => !value), "v1 appendix defaults fire edges");
Throws<InvalidDataException>(() => NativeCadenceCheckpoint.Decode([2, 2, 2, 4, 0], 2), "unknown edge bits rejected");
Throws<InvalidDataException>(() => new NativeCadenceCheckpoint(cadenceMode, [false], [false, true]).Encode(), "fire edge count mismatch");

// Literal AMHE1 observations and arithmetic edge cases, not float-approximation oracles.
Near(NativeFixedMath.MultiplyTruncate(450f / 4096, 3604f / 4096), 395f / 4096, "ROM forward damping", 0);
Near(NativeFixedMath.MultiplyTruncate(-450f / 4096, 3604f / 4096), -395f / 4096, "ROM negative damping", 0);
Near(NativeFixedMath.MultiplyRound(-1f / 4096, .5f), 0, "native negative halfway rounding", 0);
Near(NativeFixedMath.MultiplyRound(1f / 4096, .5f), 1f / 4096, "native positive halfway rounding", 0);
Near(NativeFixedMath.ProjectPlaneDistance(0, -4095f / 4096, 0, -14753f / 4096), -14745f / 4096, "ROM ceiling plane projection", 0);
Near(NativeFixedMath.ProjectPlaneDistance(-3133f / 4096, 2568f / 4096, 600f / 4096, -95626f / 4096),
    -95587f / 4096, "ROM steep plane projection", 0);
foreach (ulong n in new ulong[] { 0, 1, 2, 3, 4, 8, 9, 15, 16, 65535, 65536, uint.MaxValue, ulong.MaxValue })
{
    ulong root = NativeFixedMath.IntegerSquareRoot(n);
    Assert(root == 0 ? n == 0 : root <= n / root, "integer sqrt lower bound");
    Assert(root + 1 > n / (root + 1), "integer sqrt upper bound");
}
Throws<DivideByZeroException>(() => NativeFixedMath.DivideRound(1, 0), "undefined native diagnostic division fails closed");

foreach (float f in new[] { 0f, 0.01f, 0.25f, 0.5f, 0.9f, 0.96f, 0.999f, 1f })
{
    float q = FrameRateMath.HalfStepDamping(f);
    Near(q * q, f, "damping composition");
    float a = FrameRateMath.HalfStepInterpolation(f);
    Near(a + (1 - a) * a, f, "interpolation composition");
    Near(FrameRateMath.ConvertDamping(f, 30, 60), q, "generic damping");
    Near(FrameRateMath.ConvertInterpolation(f, 30, 60), a, "generic interpolation");
    Near(FrameRateMath.ConvertDamping(f, 60, 30), f * f, "reverse damping");
    Near(FrameRateMath.ConvertInterpolation(f, 60, 30), 1 - (1 - f) * (1 - f), "reverse interpolation");
}
Near(FrameRateMath.ConvertInterpolation(1e-12f, 30, 60), 5e-13, "small interpolation", 1e-19);
Near(FrameRateMath.ScaleLinearPerTick(-2), -1, "negative rate");
Near(FrameRateMath.ConvertLinearRate(2, 30, 120), .5, "linear rate");
foreach (float invalid in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity, -1f, 1.1f })
{
    Throws<ArgumentOutOfRangeException>(() => FrameRateMath.HalfStepDamping(invalid), "invalid damping");
    Throws<ArgumentOutOfRangeException>(() => FrameRateMath.HalfStepInterpolation(invalid), "invalid interpolation");
}
foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
{
    Throws<ArgumentOutOfRangeException>(() => FrameRateMath.ConvertDamping(.9f, invalid, 60), "invalid native Hz");
    Throws<ArgumentOutOfRangeException>(() => FrameRateMath.ConvertLinearRate(1, 30, invalid), "invalid target Hz");
}
Throws<ArgumentOutOfRangeException>(() => FrameRateMath.ConvertLinearRate(float.MaxValue, 60, 1), "rate overflow");

var scenario = new FpsScenario { Name = "edges", Room = "test", Spawn = new float[] { 0, 0, 0 },
    Facing = new float[] { 0, 0, 1 }, NativeTicks = 4,
    Inputs = new[] { new ScenarioInput { Tick = 0, MoveY = 1, Jump = true, Fire = true },
        new ScenarioInput { Tick = 2, MoveX = -1, Jump = true } } };
scenario.Validate();
Assert(Enumerable.Range(0, 8).Select(i => scenario.AtPrimeTick(i).Fire).SequenceEqual(new[] { true, true, true, true, false, false, false, false }), "fire held continuously across both substeps and released at row");
Assert(Enumerable.Range(0, 8).Where(i => scenario.AtPrimeTick(i).Jump).SequenceEqual(new[] { 0, 4 }), "jump edge only on intended first substep");
Assert(Enumerable.Range(0, 4).All(i => scenario.AtPrimeTick(i).MoveY == 1), "continuous input held two ticks and until next row");
Assert(Enumerable.Range(4, 4).All(i => scenario.AtPrimeTick(i).MoveX == -1 && scenario.AtPrimeTick(i).MoveY == 0), "input transition native boundary");
Throws<InvalidDataException>(() => (scenario with { Spawn = null! }).Validate(), "null scenario spawn");
Throws<InvalidDataException>(() => (scenario with { Inputs = new ScenarioInput[] { null! } }).Validate(), "null input");
Throws<InvalidDataException>(() => (scenario with { Inputs = new[] { new ScenarioInput { Tick = 0, MoveX = 2 } } }).Validate(), "invalid input axis");

Throws<InvalidDataException>(() => (scenario with { Inputs = new[] {
    new ScenarioInput { Tick = 0, Jump = true }, new ScenarioInput { Tick = 1, Jump = true } } }).Validate(), "native edges need release tick");

(scenario with { InitialImpulse = new ScenarioImpulse { Velocity = [.375f, .1875f, 0], Acceleration = [.015625f, 0, 0], NativeTicks = 2 } }).Validate();
Throws<InvalidDataException>(() => (scenario with { InitialImpulse = new ScenarioImpulse { Velocity = [float.NaN, 0, 0], Acceleration = [0, 0, 0], NativeTicks = 1 } }).Validate(), "nonfinite impulse");
Throws<InvalidDataException>(() => (scenario with { InitialImpulse = new ScenarioImpulse { Velocity = [.1f, 0, 0], Acceleration = [0, 0, 0], NativeTicks = 1 } }).Validate(), "non-native impulse units");
Throws<InvalidDataException>(() => (scenario with { InitialImpulse = new ScenarioImpulse { Velocity = [0, 0, 0], Acceleration = [0, 0, 0], NativeTicks = 5 } }).Validate(), "impulse longer than fixture");

// Analytical fixture only: fixed coefficients, no caps, no input changes or collisions.
// Native order: v = F*(v+a); x += v. The affine square root preserves BOTH states.
foreach (double factor in new[] { 0.0, 0.25, 0.9, 1.0 })
foreach (double acceleration in new[] { -0.04, 0.0, 0.12 })
{
    double nativeX = 0, nativeV = 0.5, halfX = 0, halfV = 0.5;
    double q = Math.Sqrt(factor), b = factor * acceleration / (1 + q);
    double c = factor / (1 + q), d = (factor * acceleration - c * b) / 2;
    for (int tick = 0; tick < 120; tick++)
    {
        nativeV = factor * (nativeV + acceleration);
        nativeX += nativeV;
        for (int step = 0; step < 2; step++)
        {
            halfX += c * halfV + d;
            halfV = q * halfV + b;
        }
        Near(halfV, nativeV, "affine velocity", 1e-10);
        Near(halfX, nativeX, "affine position", 1e-10);
    }
}
// Prove sensitivity to the existing approximations so this suite cannot pass by
// comparing two implementations of the same approximate update.
Assert(Math.Abs(Math.Pow((1 + .9) / 2, 2) - .9) > .001, "detect approximate damping");
Near((.5 - .04 / 2) / 2 + (.5 - .04) / 2 - (.5 - .04), .01, "detect gravity integration drift");

string temp = Path.Combine(Path.GetTempPath(), "fps-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    string source = Path.Combine(temp, "src", "MphRead", "Entities");
    Directory.CreateDirectory(source);
    File.WriteAllText(Path.Combine(source, "Example.cs"), """
        class Example {
            private void Move() {
                // todo: FPS stuff
                Speed += (speedMul - Speed) / 2; // sktodo: FPS stuff?
                Position += Speed / 2;
                timer = nativeFrames * 2; // todo FPS stuff
                value *= 0.9f;
                // value /= 2;
                string text = "value /= 2;";
            }
        }
        """);
    var sites = FpsConversionAudit.Scan(temp);
    Assert(sites.Count == 5, "marker variants and unmarked candidates without comment/string arithmetic");
    Assert(sites.Any(s => s.Category == "PositionIntegration" && s.Risk == "CRITICAL"), "integration triage");
    Assert(sites.Any(s => s.Category == "Damping" && s.Risk == "HIGH"), "damping triage");
    Assert(sites.All(s => s.Status == "NeedsResearch" && s.Native30Expression == null), "no invented native evidence");
    Assert(sites.All(s => s.Method == "Move"), "method label");
    string output = Path.Combine(temp, "reports");
    FpsConversionAudit.Run(temp, output);
    Assert(JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "fps-conversion-report.json"))).RootElement.GetArrayLength() == sites.Count, "JSON report");
    var sample = new PhysicsSample
    {
        Frame = 2, Player = 0, Hunter = "Samus", Form = "Biped",
        PositionBefore = new(0, 0, 0), VelocityBefore = new(0, 0, 0),
        PositionActual = new(1, 2, 3), VelocityActual = new(0, 0, 0), Heading = 359,
        Standing = true, Grounded = true, CollisionFlags = 0, TimeSinceGrounded = 0
    };
    string expected = Path.Combine(temp, "native.jsonl"), actual = Path.Combine(temp, "actual.jsonl");
    void Save(string path, params PhysicsSample[] rows) => File.WriteAllLines(path, rows.Select(s => JsonSerializer.Serialize(s, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })));
    Save(expected, sample);
    Save(actual, sample with { Frame = 1, PositionActual = new(10, 10, 10) }, sample);
    Assert(PhysicsTrace.Compare(expected, actual).Single().Passed, "odd intermediate state excluded");
    Save(actual, sample with { Heading = -1 });
    Assert(PhysicsTrace.Compare(expected, actual).Single().Passed, "heading wrap");
    Save(actual, sample with { PositionActual = new(2, 2, 3), Standing = false, Grounded = false, CollisionFlags = 1, TimeSinceGrounded = 1 });
    var diff = PhysicsTrace.Compare(expected, actual).Single();
    Assert(!diff.Passed && diff.PositionError == 1 && diff.StandingMismatch && diff.GroundedMismatch && diff.CollisionStateMismatch && diff.TimerMismatch, "state differences");
    var staged = sample with { SchemaVersion = 2, Stages = new PhysicsStages { AfterGravity = new(0, -1, 0), AfterIntegration = new(1, 2, 3) } };
    Save(expected, staged);
    Save(actual, staged with { Stages = new PhysicsStages { AfterGravity = new(0, -1, 0), AfterIntegration = new(1, 2.1f, 3) } });
    var stageDiff = PhysicsTrace.Compare(expected, actual).Single();
    Assert(!stageDiff.Passed && stageDiff.Stages["gravity"] == "MATCH" && stageDiff.Stages["integration"] == "MISMATCH", "localize stage divergence even if final state matches");
    Assert(stageDiff.Stages["damping"] == "UNAVAILABLE", "missing stages are not fabricated matches");
    var original = sample with { SchemaVersion = 2, NativeStages = new NativeMovementStages
        { SampleHz = 30, AfterGravity = new(0, -1, 0) } };
    Save(expected, original);
    Save(actual, sample with { SchemaVersion = 2, Stages = new PhysicsStages { SampleHz = 60, AfterGravity = new(0, -.5f, 0) } });
    Assert(PhysicsTrace.Compare(expected, actual).Single().Stages["gravity"] == "UNAVAILABLE", "native whole step is not a 60 Hz half step");
    Save(actual, sample with { SchemaVersion = 2, Stages = new PhysicsStages { SampleHz = 30, AfterGravity = new(0, -.5f, 0) } });
    Assert(!PhysicsTrace.Compare(expected, actual).Single().Passed, "native diagnostic stage mismatch fails");
    Save(actual, sample with { SchemaVersion = 2, Stages = new PhysicsStages { SampleHz = 30, AfterGravity = new(0, -1, 0) } });
    Assert(PhysicsTrace.Compare(expected, actual).Single().Stages["gravity"] == "MATCH", "native whole-stage agreement");
    Save(actual, sample with { SchemaVersion = 2, Stages = new PhysicsStages { SampleHz = 120 } });
    Throws<InvalidDataException>(() => PhysicsTrace.Compare(expected, actual), "unsupported stage cadence");
    Save(expected, staged);
    Save(actual, staged with { Stages = new PhysicsStages { SampleHz = 30 } });
    Throws<InvalidDataException>(() => PhysicsTrace.Compare(expected, actual), "mixed managed stage cadences");
    Save(expected, sample);
    Save(actual, sample, sample);
    Throws<InvalidDataException>(() => PhysicsTrace.Compare(expected, actual), "duplicate boundary");
    Save(actual, sample with { Frame = 4 });
    Throws<InvalidDataException>(() => PhysicsTrace.Compare(expected, actual), "missing boundary");
    Save(actual, sample with { Hunter = "Spire" });
    Throws<InvalidDataException>(() => PhysicsTrace.Compare(expected, actual), "hunter mismatch");
    File.WriteAllText(actual, "{}");
    Throws<JsonException>(() => PhysicsTrace.Compare(expected, actual), "missing fields");
    File.WriteAllText(actual, "");
    Throws<InvalidDataException>(() => PhysicsTrace.Compare(expected, actual), "empty trace");
    Assert(PhysicsTrace.Check(null, null, output, true) == 2, "no evidence cannot pass alt gate");
    Save(actual, sample);
    Throws<InvalidDataException>(() => PhysicsTrace.Check(expected, actual, output, true), "incomplete hunter coverage");
    string capture = Path.Combine(temp, "capture.jsonl");
    PhysicsTrace.Start(capture);
    PhysicsTrace.Write(sample);
    PhysicsTrace.Stop();
    Assert(PhysicsTrace.Compare(expected, capture).Single().Passed, "capture roundtrip");
    Throws<IOException>(() => PhysicsTrace.Start(capture), "evidence overwrite prevented");
}
finally { PhysicsTrace.Stop(); Directory.Delete(temp, true); }
Console.WriteLine($"[fps-check] {checks} assertions passed. Analytical fixtures are not live/native parity acceptance.");
return 0;
