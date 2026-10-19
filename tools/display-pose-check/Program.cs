using System.Buffers.Binary;
using System.Security.Cryptography;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

internal static class Program
{
    private static int _checks;
    private static readonly object Room = new();
    private static readonly DisplayPoseLifetime Life = new(10, Room, 20, 30, 40, 50);

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static Matrix4 Pose(float x, float yaw = 0, float scale = 1)
    {
        Matrix4 result = Matrix4.CreateScale(scale) * Matrix4.CreateRotationY(yaw);
        result.Row3.Xyz = new Vector3(x, x * .25f, -x * .125f);
        return result;
    }

    private static DisplayPoseHistory Moving(out Matrix4 canonical)
    {
        var history = new DisplayPoseHistory();
        history.Reset(Pose(0), Life, 0);
        canonical = Pose(1);
        history.Capture(canonical, Life, 1);
        return history;
    }

    private static bool Near(Vector3 a, Vector3 b) => (a - b).LengthSquared < .0000001f;

    private static void LifetimeChecks()
    {
        var empty = new DisplayPoseHistory();
        Check(!empty.TryGetTransform(Pose(0), Life, 0, .5, out _), "uninitialized history carries nothing");
        empty.Reset(Pose(0), Life, 0);
        Check(!empty.TryGetTransform(Pose(0), Life, 1, .5, out _), "first sample has no previous tick");

        var history = Moving(out Matrix4 canonical);
        Matrix4 originalCanonical = canonical;
        DisplayPoseHistory originalHistory = history;
        for (int i = 0; i < 100; i++)
        {
            Check(history.TryGetTransform(canonical, Life, 2, .5, out var shown)
                && Near(shown.Row3.Xyz, Pose(.5f).Row3.Xyz), "repeated draw samples one completed interval");
            Check(history.TryGetPosition(canonical, Life, 2, .5, out var position)
                && Near(position, shown.Row3.Xyz), "translation-only sampling uses the same timestamp");
        }
        Check(canonical == originalCanonical && history.Equals(originalHistory), "draws mutate neither canonical nor history");
        Check(history.TryGetTransform(canonical, Life, 2, 1, out var current) && current == canonical,
            "60 Hz current-state alpha adds no delay");
        Check(history.TryGetTransform(canonical, Life, 2, -1, out var before) && before == Pose(0)
            && history.TryGetTransform(canonical, Life, 2, 2, out var after) && after == canonical,
            "unsafe finite time clamps to completed interval without extrapolation");
        Check(!history.TryGetTransform(canonical, Life, 2, double.NaN, out _)
            && !history.TryGetPosition(canonical, Life, 2, double.PositiveInfinity, out _), "nonfinite time refused");
        Check(!history.TryGetTransform(canonical, Life, 1, .5, out _)
            && !history.TryGetTransform(canonical, Life, 3, .5, out _), "future and expired capture stamps refused");
        Check(!history.TryGetTransform(Pose(1.01f), Life, 2, .5, out _),
            "small uncaptured canonical edits cannot replay stale carry");

        DisplayPoseLifetime[] boundaries =
        {
            Life with { SceneGeneration = 11 }, Life with { RoomOwner = new object() },
            Life with { AuthorityEpoch = 21 }, Life with { MatchId = 31 },
            Life with { SlotGeneration = 41 }, Life with { LifeId = 51 },
            Life with { Form = 1 }, Life with { Form = 2 }, Life with { Form = 4 },
            Life with { TimelineGeneration = 1 }, Life with { Eligible = false },
            Life with { Replica = true }, Life with { Hunter = 1 }
        };
        foreach (var boundary in boundaries)
        {
            history = Moving(out canonical);
            Check(!history.TryGetTransform(canonical, boundary, 2, .5, out _),
                "identity changes fence carry immediately before capture: " + boundary);
            canonical = Pose(1.25f);
            history.Capture(canonical, boundary, 2);
            Check(!history.TryGetTransform(canonical, boundary, 3, .5, out _),
                "identity changes rebase the capture: " + boundary);
            canonical = Pose(1.5f);
            history.Capture(canonical, boundary, 3);
            Check(history.TryGetTransform(canonical, boundary, 4, .5, out _) == boundary.Eligible,
                "only a consecutive eligible new-lifetime tick resumes carry");
        }

        foreach (ulong discontinuousTick in new ulong[] { 0, 1, 3, ulong.MaxValue })
        {
            history = Moving(out canonical);
            canonical = Pose(1.25f);
            history.Capture(canonical, Life, discontinuousTick);
            Check(!history.TryGetTransform(canonical, Life, unchecked(discontinuousTick + 1), .5, out _),
                "backward, same, gap and overflow ticks rebase");
        }

        history = Moving(out canonical);
        canonical = Pose(8);
        history.Capture(canonical, Life, 2);
        Check(!history.TryGetTransform(canonical, Life, 3, .5, out var teleported) && teleported == canonical,
            "teleport returns current transform without old-world ghost");
        history = Moving(out canonical);
        canonical.Row0.X = float.NaN;
        history.Capture(canonical, Life, 2);
        Check(!history.TryGetTransform(canonical, Life, 3, .5, out _), "nonfinite canonical capture cannot carry");

        history = Moving(out canonical);
        // Pause recaptures this same simulation tick; it does not manufacture
        // a previous tick or advance toward a different pose on each draw.
        history.Capture(canonical, Life, 1);
        for (int i = 0; i < 20; i++)
            Check(!history.TryGetTransform(canonical, Life, 1, i / 20d, out var paused) && paused == canonical,
                "paused same-tick sampling remains canonical");

        history = Moving(out canonical);
        canonical = Pose(.5f); // reverse direction without predicting past it
        history.Capture(canonical, Life, 2);
        Check(history.TryGetTransform(canonical, Life, 3, .5, out var reversed)
            && Near(reversed.Row3.Xyz, Pose(.75f).Row3.Xyz), "motion reversal stays between known poses");
        history.Reset(Pose(0, 179 * MathF.PI / 180, 1), Life, 0);
        canonical = Pose(1, -179 * MathF.PI / 180, 2);
        history.Capture(canonical, Life, 1);
        Check(history.TryGetTransform(canonical, Life, 2, .5, out var rotated)
            && Math.Abs(rotated.ExtractScale().X - 1.5f) < .0001f
            && Vector3.Dot(rotated.Row2.Xyz.Normalized(), -Vector3.UnitZ) > .999f,
            "orientation follows shortest arc with independent scale");

        var visible = new List<(DisplayPoseHistory History, Matrix4 Canonical)> { (Moving(out canonical), canonical) };
        Check(visible.Count(v => v.History.TryGetTransform(v.Canonical, Life, 2, .5, out _)) == 1,
            "owner enumeration draws active entity once");
        visible.Clear();
        Check(visible.Count(v => v.History.TryGetTransform(v.Canonical, Life, 2, .5, out _)) == 0,
            "removed entities have no render registry capable of drawing a ghost");
        history.Reset(Pose(2), Life with { LifeId = 51 }, 2);
        Check(!history.TryGetTransform(Pose(2), Life with { LifeId = 51 }, 3, .5, out _),
            "pooled/new-lifetime reset cannot reuse the previous pose");
    }

    private static void TimingChecks()
    {
        FrameTiming.Reset();
        FrameTiming.ResetDiagnostics();
        long generation = FrameTiming.PresentationGeneration;
        Check(FrameTiming.Advance(1d / 144) == 0 && FrameTiming.PresentationGeneration == generation,
            "ordinary substep does not invalidate history");
        FrameTiming.Reset();
        Check(FrameTiming.PresentationGeneration == generation + 1 && !FrameTiming.Active,
            "focus/session reset fences carry without advancing simulation");
        generation = FrameTiming.PresentationGeneration;
        Check(FrameTiming.Advance(.5) == 1 && FrameTiming.PresentationGeneration == generation + 1
            && FrameTiming.Alpha == 0, "stall keeps existing one-step recovery and fences carry");
        generation = FrameTiming.PresentationGeneration;
        Check(FrameTiming.Advance(.2) == FrameTiming.MaxCatchUpSteps && FrameTiming.DroppedSteps > 0
            && FrameTiming.PresentationGeneration == generation + 1, "discarded time fences carry with unchanged catch-up ceiling");
    }

    private static void AllocationChecks()
    {
        var history = Moving(out Matrix4 canonical);
        double consumed = 0;
        for (int i = 0; i < 1_000; i++)
        {
            history.TryGetTransform(canonical, Life, 2, .5, out _);
            history.TryGetPosition(canonical, Life, 2, .5, out _);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            if (history.TryGetTransform(canonical, Life, 2, .5, out Matrix4 transform)) consumed += transform.Row3.X;
            if (history.TryGetPosition(canonical, Life, 2, .5, out Vector3 position)) consumed += position.X;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(consumed == 10_000 && allocated == 0, "20,000 warmed production pose reads allocate zero managed bytes");
        Console.WriteLine($"Core sampling allocation check: 20,000 warmed reads, {allocated} managed bytes.");
    }

    private static string Schedule(int rate)
    {
        FrameTiming.Reset();
        FrameTiming.ResetDiagnostics();
        FrameTiming.FrameRateCap = rate;
        ulong completed = 0;
        const int entities = 8, ticks = 600;
        var canonical = new Matrix4[entities];
        var histories = new DisplayPoseHistory[entities];
        var previous = new Matrix4[entities];
        var lives = new DisplayPoseLifetime[entities];
        var registered = new bool[entities];
        for (int i = 0; i < entities; i++)
        {
            canonical[i] = Pose(i * .1f);
            lives[i] = Life with { LifeId = (ushort)(i + 1), TimelineGeneration = FrameTiming.PresentationGeneration };
            histories[i].Reset(canonical[i], lives[i], 0);
            registered[i] = true;
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[64];
        int fractionalSamples = 0, draws = 0;
        while (completed < ticks)
        {
            int steps = FrameTiming.Advance(1d / rate);
            for (int step = 0; step < steps && completed < ticks; step++)
            {
                for (int i = 0; i < entities; i++)
                {
                    previous[i] = canonical[i];
                    float position = (float)completed * .015625f + i * .125f;
                    if (completed >= 300) position = (600 - completed) * .015625f + i * .125f;
                    // Stable canonical boundary events, independent of drawrate.
                    if (completed == 120) lives[i] = lives[i] with { Form = 1 };
                    if (completed == 240) lives[i] = lives[i] with { LifeId = (ushort)(i + 20) };
                    if (completed == 360) lives[i] = lives[i] with { TimelineGeneration = lives[i].TimelineGeneration + 1 };
                    registered[i] = i != 7 || completed < 450;
                    canonical[i] = Pose(position, (float)completed * .0078125f, 1 + i * .03125f);
                    if (registered[i]) histories[i].Capture(canonical[i], lives[i], completed);
                    Write(canonical[i], bytes);
                    hash.AppendData(bytes);
                }
                completed++;
            }
            for (int i = 0; i < entities; i++)
            {
                if (!registered[i]) continue; // same existing owner enumeration as EntityBase
                Matrix4 before = canonical[i];
                bool sampled = histories[i].TryGetTransform(canonical[i], lives[i], completed,
                    FrameTiming.PresentationAlpha, out Matrix4 shown);
                Check(canonical[i] == before, "scheduled draw cannot change canonical transform");
                if (rate == 60) Check(shown == canonical[i], "60 Hz schedule draws current state");
                if (sampled && FrameTiming.PresentationAlpha > .0001 && FrameTiming.PresentationAlpha < .9999)
                {
                    fractionalSamples++;
                    Vector3 expected = Vector3.Lerp(previous[i].Row3.Xyz, canonical[i].Row3.Xyz,
                        (float)FrameTiming.PresentationAlpha);
                    Check(Near(shown.Row3.Xyz, expected), "scheduled draw uses real fixed-step fractional timestamp");
                }
            }
            draws++;
        }
        Check(completed == ticks && FrameTiming.TotalSteps == ticks && FrameTiming.DroppedSteps == 0,
            rate + " Hz schedule executes exactly 600 canonical ticks");
        Check(rate == 60 ? fractionalSamples == 0 : fractionalSamples > ticks,
            rate + " Hz schedule samples the expected intermediate poses");
        string digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        Console.WriteLine($"{rate} Hz: {draws} draws, {completed} canonical ticks, {fractionalSamples} fractional poses, canonical SHA-256 {digest}");
        return digest;
    }

    private static void Write(Matrix4 matrix, Span<byte> bytes)
    {
        Span<Vector4> rows = stackalloc Vector4[] { matrix.Row0, matrix.Row1, matrix.Row2, matrix.Row3 };
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 4; column++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.Slice((row * 4 + column) * 4, 4), rows[row][column]);
    }

    private static int Main()
    {
        LifetimeChecks();
        AllocationChecks();
        TimingChecks();
        string[] hashes = new[] { 60, 120, 144, 240 }.Select(Schedule).ToArray();
        Check(hashes.Distinct().Count() == 1, "canonical state stream is identical across display schedules");
        Console.WriteLine($"DISPLAY POSE PASS {_checks} checks. Source-only fixture; no 3D/device/performance acceptance.");
        return 0;
    }
}

// Only logging is stubbed; the linked production FrameTiming arithmetic runs.
namespace MphRead.Mods
{
    internal static class DebugLog
    {
        internal static void Line(string category, string message) { }
    }
}
