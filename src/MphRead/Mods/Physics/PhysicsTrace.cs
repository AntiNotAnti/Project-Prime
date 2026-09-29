using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MphRead.Mods.Physics
{
    public readonly record struct TraceVector(float X, float Y, float Z)
    {
        public double Distance(TraceVector other) => Math.Sqrt(Math.Pow((double)X - other.X, 2) + Math.Pow((double)Y - other.Y, 2) + Math.Pow((double)Z - other.Z, 2));
        public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);
    }

    public sealed record NativeMovementStages
    {
        public required int SampleHz { get; init; }
        public TraceVector? PreMovement { get; init; }
        public TraceVector? AfterAcceleration { get; init; }
        public TraceVector? AfterDamping { get; init; }
        public TraceVector? AfterGravity { get; init; }
        public TraceVector? AfterIntegration { get; init; }
        public TraceVector? AfterCollision { get; init; }
        public List<PhysicsContact> Contacts { get; init; } = new();
    }

    /// <summary>Post-movement state. Frame denotes an elapsed 60 Hz boundary;
    /// native 30 Hz captures must map their ticks to 0, 2, 4, ... explicitly.</summary>
    public sealed record PhysicsSample
    {
        public int SchemaVersion { get; init; } = 1;
        public PhysicsStages? Stages { get; init; }
        public NativeMovementStages? NativeStages { get; init; }
        public required ulong Frame { get; init; }
        public int Substep => (int)(Frame % 2);
        public required int Player { get; init; }
        public required string Hunter { get; init; }
        public required string Form { get; init; }
        public required TraceVector PositionBefore { get; init; }
        public required TraceVector VelocityBefore { get; init; }
        public required TraceVector PositionActual { get; init; }
        public required TraceVector VelocityActual { get; init; }
        public required float Heading { get; init; }
        public required bool Standing { get; init; }
        public required bool Grounded { get; init; }
        public required uint CollisionFlags { get; init; }
        public required int TimeSinceGrounded { get; init; }
        public bool? JumpPadActive { get; init; }
        public TraceVector? Acceleration { get; init; }
        public float? SpeedCap { get; init; }
        public float? SpeedFactor { get; init; }
        public float? Gravity { get; init; }
        // Unknown values stay null, never synthetic zeroes that imply measurement.
        public string? Input { get; init; }
        public float? Traction { get; init; }
        public TraceVector? CollisionPlane { get; init; }
        public float? CollisionDepth { get; init; }
        public TraceVector? CollisionPushout { get; init; }
    }

    public static class PhysicsTrace
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            RespectRequiredConstructorParameters = true
        };
        private static StreamWriter? _writer;
        private static readonly object Gate = new();
        public static bool Enabled => _writer != null;
        public static void Start(string path)
        {
            lock (Gate)
            {
                if (_writer != null) throw new InvalidOperationException("Physics capture already running.");
                string full = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                // Do not overwrite evidence from another run.
                _writer = new StreamWriter(new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
            }
        }
        public static void Stop() { lock (Gate) { _writer?.Dispose(); _writer = null; } }
        public static void Write(PhysicsSample sample)
        {
            lock (Gate) _writer?.WriteLine(JsonSerializer.Serialize(sample, Json));
        }

        public sealed record Difference(ulong Frame, int Player, string Hunter, string Form,
            double PositionError, double VelocityError, double HeadingError,
            bool CollisionStateMismatch, bool GroundedMismatch, bool StandingMismatch, bool TimerMismatch)
        {
            public IReadOnlyDictionary<string, string> Stages { get; init; } = new Dictionary<string, string>();
            public bool Passed => !Stages.Values.Contains("MISMATCH") && PositionError <= 0.0001 && VelocityError <= 0.0001 && HeadingError <= 0.001
                && !CollisionStateMismatch && !GroundedMismatch && !StandingMismatch && !TimerMismatch;
        }

        public static IReadOnlyList<Difference> Compare(string expectedPath, string actualPath)
        {
            var expected = Read(expectedPath);
            var actual = Read(actualPath);
            if (!expected.Keys.ToHashSet().SetEquals(actual.Keys))
                throw new InvalidDataException("Reference and actual traces must contain identical even-frame/player keys; missing or extra boundaries are not ignored.");
            var differences = new List<Difference>();
            foreach (var key in expected.Keys.OrderBy(k => k.Frame).ThenBy(k => k.Player))
            {
                var e = expected[key];
                var a = actual[key];
                if (e.SchemaVersion != a.SchemaVersion) throw new InvalidDataException("Trace boundary schema versions differ.");
                if (e.Hunter != a.Hunter || e.Form != a.Form) throw new InvalidDataException($"Hunter/form mismatch at {key}.");
                double angle = Math.Abs((double)e.Heading - a.Heading) % 360;
                differences.Add(new(a.Frame, a.Player, a.Hunter, a.Form, e.PositionActual.Distance(a.PositionActual),
                    e.VelocityActual.Distance(a.VelocityActual), Math.Min(angle, 360 - angle),
                    e.CollisionFlags != a.CollisionFlags, e.Grounded != a.Grounded, e.Standing != a.Standing,
                    e.TimeSinceGrounded != a.TimeSinceGrounded) { Stages = CompareStages(e, a) });
            }
            return differences;
        }

        private static IReadOnlyDictionary<string, string> CompareStages(PhysicsSample e, PhysicsSample a)
        {
            var result = new Dictionary<string, string>();
            void Vector(string name, TraceVector? x, TraceVector? y)
                => result[name] = x == null || y == null ? "UNAVAILABLE"
                    : x.Value.Distance(y.Value) <= 0.0001 ? "MATCH" : "MISMATCH";
            void Scalar(string name, float? x, float? y)
                => result[name] = x == null || y == null ? "UNAVAILABLE"
                    : Math.Abs((double)x.Value - y.Value) <= 0.0001 ? "MATCH" : "MISMATCH";
            result["jumpPadActive"] = e.JumpPadActive == null || a.JumpPadActive == null ? "UNAVAILABLE"
                : e.JumpPadActive == a.JumpPadActive ? "MATCH" : "MISMATCH";
            var es = e.Stages; var ac = a.Stages;
            if (es != null && ac != null && es.SampleHz != ac.SampleHz)
                throw new InvalidDataException("Managed movement stages use different cadences.");
            // Whole original-ROM stages can only be paired with a whole native
            // diagnostic operation, never with the last half of a 60 Hz pair.
            var native = ac?.SampleHz == 30 && e.NativeStages?.SampleHz == 30 ? e.NativeStages : null;
            Vector("preMovement", es?.PreMovement ?? native?.PreMovement, ac?.PreMovement);
            Vector("acceleration", es?.AfterAcceleration ?? native?.AfterAcceleration, ac?.AfterAcceleration);
            Vector("damping", es?.AfterDamping ?? native?.AfterDamping, ac?.AfterDamping);
            Vector("gravity", es?.AfterGravity ?? native?.AfterGravity, ac?.AfterGravity);
            Vector("predictedPosition", es?.PredictedPosition, ac?.PredictedPosition);
            Vector("integration", es?.AfterIntegration ?? native?.AfterIntegration, ac?.AfterIntegration);
            Vector("collision", es?.AfterCollision ?? native?.AfterCollision, ac?.AfterCollision);
            Vector("traction", es?.AfterTraction, ac?.AfterTraction);
            Vector("speedDelta", es?.SpeedDelta, ac?.SpeedDelta);
            Scalar("gravityCoefficient", e.Gravity, a.Gravity);
            Scalar("dampingCoefficient", e.SpeedFactor, a.SpeedFactor);
            Scalar("speedCapBefore", es?.SpeedCapBefore, ac?.SpeedCapBefore);
            Scalar("speedCapAfter", es?.SpeedCapAfter, ac?.SpeedCapAfter);
            Scalar("moveX", es?.MoveX, ac?.MoveX); Scalar("moveY", es?.MoveY, ac?.MoveY);
            Scalar("analogScaleX", es?.AnalogScaleX, ac?.AnalogScaleX);
            Scalar("analogScaleY", es?.AnalogScaleY, ac?.AnalogScaleY);
            Scalar("tractionX", es?.TractionX, ac?.TractionX); Scalar("tractionY", es?.TractionY, ac?.TractionY);
            result["contacts"] = "UNAVAILABLE";
            if (es != null && ac != null)
                result["jumpInput"] = es.JumpPressed == ac.JumpPressed && es.JumpApplied == ac.JumpApplied ? "MATCH" : "MISMATCH";
            var expectedContacts = es?.Contacts ?? native?.Contacts;
            if (expectedContacts != null && ac != null)
            {
                bool same = expectedContacts.Count == ac.Contacts.Count;
                for (int i = 0; same && i < expectedContacts.Count; i++)
                {
                    var x = expectedContacts[i]; var y = ac.Contacts[i];
                    same = Math.Abs(x.Plane.X - y.Plane.X) <= .0001
                        && Math.Abs(x.Plane.Y - y.Plane.Y) <= .0001
                        && Math.Abs(x.Plane.Z - y.Plane.Z) <= .0001
                        && Math.Abs(x.Plane.W - y.Plane.W) <= .0001
                        && Math.Abs(x.PenetrationDepth - y.PenetrationDepth) <= .0001
                        && x.Pushout.Distance(y.Pushout) <= .0001;
                }
                result["contacts"] = same ? "MATCH" : "MISMATCH";
            }
            return result;
        }

        private static Dictionary<(ulong Frame, int Player), PhysicsSample> Read(string path)
        {
            var result = new Dictionary<(ulong, int), PhysicsSample>();
            var allKeys = new HashSet<(ulong, int)>();
            foreach (string line in File.ReadLines(path))
            {
                var sample = JsonSerializer.Deserialize<PhysicsSample>(line, Json) ?? throw new InvalidDataException("Null trace row.");
                if (sample.SchemaVersion is not (1 or 2)) throw new InvalidDataException("Unknown trace schema version.");
                if (!sample.PositionBefore.IsFinite || !sample.VelocityBefore.IsFinite || !sample.PositionActual.IsFinite
                    || !sample.VelocityActual.IsFinite || !float.IsFinite(sample.Heading)
                    || string.IsNullOrWhiteSpace(sample.Hunter) || string.IsNullOrWhiteSpace(sample.Form))
                    throw new InvalidDataException("Nonfinite or incomplete trace row.");
                if (sample.Stages != null && sample.Stages.SampleHz is not (30 or 60)
                    || sample.NativeStages != null && sample.NativeStages.SampleHz != 30)
                    throw new InvalidDataException("Unsupported movement-stage cadence.");
                var key = (sample.Frame, sample.Player);
                if (!allKeys.Add(key)) throw new InvalidDataException("Duplicate frame/player key: " + key);
                if (sample.Frame % 2 == 0) result.Add(key, sample);
            }
            if (result.Count == 0) throw new InvalidDataException("Trace contains no even boundaries.");
            return result;
        }

        public static int Check(string? expected, string? actual, string output, bool alt)
        {
            Directory.CreateDirectory(output);
            if (expected == null || actual == null)
            {
                string[] hunters = { "Samus", "Kanden", "Spire", "Noxus", "Trace", "Sylux", "Weavel" };
                if (alt) File.WriteAllText(Path.Combine(output, "alt-form-matrix.md"),
                    "# Alt-form evidence gate\n\nNo native reconstruction or parity is asserted. Shared-physics acceptance must precede alt tuning.\n\n"
                    + "| Hunter | Acceleration | Friction | Air | Collision | Attack | Transform | Native parity |\n|---|---|---|---|---|---|---|---|\n"
                    + string.Join("\n", hunters.Select(h => $"| {h} | NeedsResearch | NeedsResearch | NeedsResearch | NeedsResearch | NeedsResearch | NeedsResearch | Unknown |")) + "\n");
                Console.Error.WriteLine("[fpsphysics] Evidence required: -fpsreference native.jsonl -fpsactual current.jsonl. Exit 2 means unverified, not passed.");
                return 2;
            }
            var differences = Compare(expected, actual);
            if (alt)
            {
                var hunters = differences.Where(d => d.Form == "Alt").Select(d => d.Hunter).ToHashSet(StringComparer.Ordinal);
                if (!new[] { "Samus", "Kanden", "Spire", "Noxus", "Trace", "Sylux", "Weavel" }.All(hunters.Contains))
                    throw new InvalidDataException("Alt check requires Alt samples from all seven hunters together.");
            }
            string name = alt ? "native-alt-comparison" : "physics-comparison";
            File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(differences, Json));
            using (var csv = new StreamWriter(Path.Combine(output, name + ".csv")))
            {
                csv.WriteLine("frame,player,positionError,velocityError,headingError,collisionStateMismatch,groundedMismatch,standingMismatch,timerMismatch,passed");
                foreach (var d in differences) csv.WriteLine(FormattableString.Invariant($"{d.Frame},{d.Player},{d.PositionError:R},{d.VelocityError:R},{d.HeadingError:R},{d.CollisionStateMismatch},{d.GroundedMismatch},{d.StandingMismatch},{d.TimerMismatch},{d.Passed}"));
            }
            int failures = differences.Count(d => !d.Passed);
            Console.WriteLine($"[fpsphysics] {differences.Count} boundaries compared, {failures} failures. This verifies supplied traces only; native provenance and scenario coverage require separate review.");
            return failures == 0 ? 0 : 1;
        }
    }
}
