using System;
using MphRead;
using MphRead.Mods.Movement;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Classification/bob contracts remain independent of the dedicated asset poses.
// Native animation-clock coverage belongs to TraversalWalkPoseTests; walking and
// slide acceptance each inspect their own targets rather than a retimed clip.
internal static class TraversalCrouchGaitTests
{
    private static int _checks;
    private static float _allocationSink;
    private static void Check(bool good, string label)
    { _checks++; if (!good) throw new InvalidOperationException(label); }
    private static void Near(float actual, float expected, string label)
        => Check(float.IsFinite(actual) && MathF.Abs(actual - expected) < .00001f,
            $"{label}: {actual} != {expected}");

    internal static int CheckMath()
    {
        _checks = 0;
        var gait = new TraversalGaitPresentation();
        Check(!gait.Ready && !gait.Moving && !gait.Gliding, "fresh classification retains no travel or slide");
        Near(gait.SampleBob(.5f), 0, "fresh camera bob is zero");
        Near(gait.SampleBob(.5f, true), 0, "fresh weapon bob is zero");
        gait.Capture(10, Vector3.Zero, true, false, 2, 4);
        Check(gait.Ready && !gait.Moving && !gait.Gliding, "initial capture cannot infer completed travel");
        Near(gait.SampleBob(0), 2, "initial camera bob has no invented previous endpoint");
        Near(gait.SampleBob(0, true), 4, "initial gun bob has its own accepted endpoint");
        Vector3 position = Vector3.UnitZ * .02f;
        gait.Capture(11, position, true, false, 6, 12);
        Check(gait.Moving && !gait.Gliding, "ordinary accepted low walking travel stays a walk");
        Near(gait.SampleBob(.25f), 3, "camera interpolates captured completed native contributions");
        Near(gait.SampleBob(.25f, true), 6, "weapon interpolates its separate completed native contributions");
        for (int repeat = 0; repeat < 4; repeat++)
        {
            Near(gait.SampleBob(.25f), 3, "repeated pictures do not advance camera bob");
            Near(gait.SampleBob(.25f, true), 6, "repeated pictures do not advance weapon bob");
            Check(gait.Ready && gait.Moving && !gait.Gliding, "sampling cannot replace travel classification");
        }
        gait.Capture(11, new(999, 999, 999), false, true, 100, 200);
        Check(gait.Moving && !gait.Gliding, "duplicate capture cannot overwrite the accepted walking/slide levels");
        Near(gait.SampleBob(.25f), 3, "duplicate capture cannot replace completed camera endpoints");
        position = position.AddY(2);
        gait.Capture(12, position, true, false, 10, 20);
        Check(!gait.Moving && !gait.Gliding, "vertical movement alone never qualifies horizontal low travel");
        Near(gait.SampleBob(.25f), 7, "vertical native motion still captures camera bob endpoints");
        Near(gait.SampleBob(.25f, true), 14, "vertical native motion still captures weapon bob endpoints");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Near(gait.SampleBob(invalid), 10, "invalid alpha selects completed camera bob");
            Near(gait.SampleBob(invalid, true), 20, "invalid alpha selects completed weapon bob");
        }
        Near(gait.SampleBob(-1), 6, "negative alpha clamps to the previous camera endpoint");
        Near(gait.SampleBob(2), 10, "oversized alpha clamps to the completed camera endpoint");

        position += Vector3.UnitZ * .1f;
        gait.Capture(13, position, true, false, 1, 2);
        Check(gait.Moving && gait.Gliding, "fast accepted low travel provides the read-only remote glide classification");
        position += Vector3.UnitZ * .1f;
        gait.Capture(14, position, false, false, 2, 3);
        Check(gait.Moving && !gait.Gliding, "fast movement outside the low walking level does not infer a glide");
        gait.Capture(15, position, false, true, 3, 4);
        Check(!gait.Moving && gait.Gliding, "an explicitly accepted physical slide level remains authoritative");
        position += Vector3.UnitZ * .02f;
        gait.Capture(16, position, true, false, 4, 5);
        Check(gait.Moving && !gait.Gliding, "ordinary travel resumes immediately without a retained glide latch");

        gait.Capture(20, position + Vector3.UnitZ * .02f, true, false, 3, 9);
        Check(!gait.Moving && !gait.Gliding, "a missing simulation tick cannot be spent as completed travel");
        Near(gait.SampleBob(.25f), 3, "frame discontinuity reseeds camera interpolation from its accepted endpoint");
        Near(gait.SampleBob(.25f, true), 9, "frame discontinuity independently reseeds weapon bob");
        gait.Capture(21, position + Vector3.UnitX * 5, true, false, 5, 15);
        Check(!gait.Moving && !gait.Gliding, "teleport-sized movement cannot be classified as low-speed movement");
        Near(gait.SampleBob(.5f), 5, "teleport reseeds camera bob without blending old source time");
        gait.Capture(22, new(0, float.NaN, 0), true, false, 6, 18);
        Check(!gait.Moving && !gait.Gliding, "a nonfinite vertical coordinate invalidates the completed position delta");
        gait.Capture(23, Vector3.Zero, true, false, 7, 21);
        Check(!gait.Moving && !gait.Gliding, "a finite position after nonfinite input first reseeds continuity");
        gait.Capture(24, Vector3.UnitZ * .02f, true, false, 8, 24);
        Check(gait.Moving && !gait.Gliding, "valid completed travel resumes after continuity recovers");
        gait.Capture(ulong.MaxValue, Vector3.Zero, true, false, 1, 3);
        gait.Capture(0, Vector3.UnitZ * .02f, true, false, 2, 6);
        Check(!gait.Moving && !gait.Gliding, "unsigned frame wrap cannot appear to be a consecutive accepted tick");
        Near(gait.SampleBob(.5f), 2, "frame wrap resets the captured bob interval");

        gait.Reset();
        Check(!gait.Ready && !gait.Moving && !gait.Gliding, "reset clears every classification level");
        Near(gait.SampleBob(1), 0, "reset clears camera bob"); Near(gait.SampleBob(1, true), 0, "reset clears weapon bob");
        gait.Capture(1, Vector3.Zero, false, false, 7, 11);
        Near(gait.SampleBob(0), 7, "fresh life starts from the new accepted camera contribution");
        Near(gait.SampleBob(0, true), 11, "fresh life starts from the new accepted weapon contribution");

        float thresholdDistance = TraversalMovementProfileV4.CrouchSpeed * 1.05f / FrameTiming.SimulationHz;
        gait.Capture(2, Vector3.UnitZ * (thresholdDistance - .001f), true, false, 0, 0);
        Check(gait.Moving && !gait.Gliding, "travel below the existing threshold remains walking");
        gait.Capture(3, Vector3.UnitZ * (2 * thresholdDistance), true, false, 0, 0);
        Check(gait.Moving && gait.Gliding, "travel above the existing threshold infers a visual glide");

        CheckAcceptedSpeedCadence();
        CheckAcceptedSpeedThreshold();

        ulong frame = 3; Vector3 allocationPosition = Vector3.UnitZ * (2 * thresholdDistance);
        for (int index = 0; index < 2048; index++) AllocationStep(ref gait, ref frame, ref allocationPosition);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 2048; index++) AllocationStep(ref gait, ref frame, ref allocationPosition);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0 && float.IsFinite(_allocationSink),
            "warmed classification capture and separate bob sampling allocate zero managed bytes");
        return _checks;
    }

    private static void CheckAcceptedSpeedCadence()
    {
        float speed = TraversalMovementProfileV4.CrouchSpeed;
        float distance = speed / FrameTiming.SimulationHz;
        foreach (float[] arrivals in new[] {
            new[] { 0f, 0f, 2f, 2f, 4f, 4f, 4f, 7f, 7f, 10f },
            new[] { 0f, 0f, 0f, 0f, 4f, 4f, 4f, 4f, 8f, 8f, 8f, 12f } })
        {
            var gait = new TraversalGaitPresentation();
            gait.Capture(1, Vector3.Zero, true, false, 0, 0, acceptedSpeed: speed);
            Check(!gait.Gliding, "accepted speed cannot infer a glide before capture continuity exists");
            for (int index = 1; index < arrivals.Length; index++)
            {
                float bob = index * .25f, gunBob = index * -.125f;
                gait.Capture((ulong)index + 1, Vector3.UnitZ * (arrivals[index] * distance), true, false,
                    bob, gunBob, acceptedSpeed: speed);
                Check(gait.Ready && !gait.Gliding,
                    "accepted crouch speed stays walking across held, doubled and batched positional arrivals");
                Near(gait.SampleBob(.5f), (index - .5f) * .25f,
                    "arrival cadence does not replace the completed native camera-bob interval");
                Near(gait.SampleBob(.5f, true), (index - .5f) * -.125f,
                    "arrival cadence does not replace the separate native weapon-bob interval");
                TraversalGaitPresentation captured = gait;
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    Near(gait.SampleBob(1), bob, "repeated cadence pictures retain completed camera bob");
                    Near(gait.SampleBob(1, true), gunBob, "repeated cadence pictures retain completed weapon bob");
                }
                Check(gait.Equals(captured), "sampling a batched arrival cannot mutate accepted classification state");
                gait.Capture((ulong)index + 1, new(100, 100, 100), true, true, 99, 99,
                    acceptedSpeed: speed * 2);
                Check(gait.Equals(captured), "duplicate arrival cannot overwrite speed, slide classification or bob");
            }
        }

        var fast = new TraversalGaitPresentation();
        fast.Capture(1, Vector3.Zero, true, false, 0, 0, acceptedSpeed: speed * 2);
        fast.Capture(2, Vector3.Zero, true, false, 1, 2, acceptedSpeed: speed * 2);
        Check(fast.Gliding, "accepted fast speed infers a visual glide even while its next position is held");
        fast.Capture(3, Vector3.UnitZ * (distance * 4), true, false, 2, 4, acceptedSpeed: speed * 2);
        Check(fast.Gliding, "accepted fast speed retains its classification when a batched position arrives");
        fast.Capture(4, Vector3.UnitZ * (distance * 8), true, false, 3, 6, acceptedSpeed: 0);
        Check(!fast.Gliding, "accepted zero speed ends inferred gliding despite a positional burst");
        fast.Capture(5, Vector3.UnitZ * (distance * 12), false, false, 4, 8, acceptedSpeed: speed * 2);
        Check(!fast.Gliding, "a false low-walking level ends inferred gliding despite accepted fast speed");
        ulong frame = 5; Vector3 position = Vector3.UnitZ * (distance * 12);
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            position += Vector3.UnitZ * (distance * 4);
            fast.Capture(++frame, position, true, false, 0, 0, acceptedSpeed: invalid);
            Check(!fast.Gliding, "invalid supplied speed fails closed instead of falling back to a positional burst");
            fast.Capture(++frame, position, false, true, 0, 0, acceptedSpeed: invalid);
            Check(fast.Gliding, "accepted physical slide level retains precedence over invalid inferred speed");
        }
        fast.Capture(frame + 2, position, true, false, 0, 0, acceptedSpeed: speed * 2);
        Check(!fast.Gliding, "accepted speed cannot bridge a missing capture tick to infer movement");
    }

    private static void CheckAcceptedSpeedThreshold()
    {
        float speed = TraversalMovementProfileV4.CrouchSpeed;
        var gait = new TraversalGaitPresentation();
        ulong frame = 1;
        gait.Capture(frame, Vector3.Zero, true, false, 0, 0, speed);
        gait.Capture(++frame, Vector3.Zero, true, false, 0, 0, speed * 1.04f);
        Check(!gait.Gliding, "a fresh accepted speed inside the deadband stays walking");
        gait.Capture(++frame, Vector3.Zero, true, false, 0, 0, speed * 1.06f);
        Check(gait.Gliding, "accepted speed above the existing entry threshold starts inferred glide");
        for (int tick = 0; tick < 30; tick++)
        {
            gait.Capture(++frame, Vector3.Zero, true, false, 0, 0, speed * (tick % 2 == 0 ? 1.04f : 1.06f));
            Check(gait.Gliding, "threshold jitter cannot alternate remote walk and glide envelopes");
        }
        gait.Capture(++frame, Vector3.Zero, true, false, 0, 0, speed);
        Check(!gait.Gliding, "accepted walking speed immediately retires an inferred glide");
        gait.Capture(++frame, Vector3.Zero, true, false, 0, 0, speed * 1.04f);
        Check(!gait.Gliding, "retired glide cannot rearm inside the deadband");
        gait.Capture(++frame, Vector3.Zero, true, false, 0, 0, speed * 1.06f);
        gait.Capture(frame += 2, Vector3.Zero, true, false, 0, 0, speed * 1.04f);
        Check(!gait.Gliding, "missing capture clears deadband history");
        gait.Capture(++frame, Vector3.Zero, true, false, 0, 0, speed * 1.06f);
        gait.Capture(++frame, Vector3.Zero, true, false, 0, 0, float.NaN);
        gait.Capture(++frame, Vector3.Zero, true, false, 0, 0, speed * 1.04f);
        Check(!gait.Gliding, "invalid accepted speed clears deadband history");
        gait.Reset(); frame = 1;
        gait.Capture(frame, Vector3.Zero, true, false, 0, 0);
        gait.Capture(++frame, Vector3.UnitZ * (speed * 1.06f / 60), true, false, 0, 0);
        Check(gait.Gliding, "local completed fast travel retains its original classification");
        gait.Capture(++frame, Vector3.UnitZ * (speed * 2.1f / 60), true, false, 0, 0);
        Check(!gait.Gliding, "local classification does not acquire remote accepted-speed hysteresis");
    }

    private static void AllocationStep(ref TraversalGaitPresentation gait, ref ulong frame, ref Vector3 position)
    {
        position += Vector3.UnitZ * .02f;
        gait.Capture(++frame, position, true, false, (frame % 7) * .1f, (frame % 9) * -.1f);
        _allocationSink = gait.SampleBob(.25f) + gait.SampleBob(.75f, true);
    }

    internal static object Capture(Hunter hunter) => new { hunter = hunter.ToString(),
        scope = "Dedicated procedural walking replaces retimed native-clip foot excursions with canonical/source invariance checks",
        walking = TraversalWalkPoseTests.Capture(hunter) };
}
