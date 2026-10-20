using System;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Pure completed-tick regression; aggregated by the stance rule checks.
// CheckMath needs no assets, scene, renderer, audio, or wall clock.
internal static class TraversalSlidePresentationTests
{
    private static int _checks;
    private static void Check(bool good, string label)
    { _checks++; if (!good) throw new InvalidOperationException(label); }
    private static void Near(float a, float b, string label)
        => Check(float.IsFinite(a) && MathF.Abs(a - b) < .00001f, label);
    private static float Ease(float value) => value * value * (3 - 2 * value);
    private static void FiniteDirection(Vector2 value)
    {
        Check(float.IsFinite(value.X) && float.IsFinite(value.Y), "direction remains finite");
        Near(value.Length, 1, "direction remains unit length");
    }

    internal static int CheckMath()
    {
        _checks = 0;
        var slide = new TraversalSlidePresentation();
        Check(!slide.Ready && slide.Phase == TraversalSlidePhase.None, "fresh timeline has no slide history");
        Near(slide.Sample(.5f), 0, "fresh timeline has zero visual slide");
        Check(slide.SampleDirection(.5f) == Vector2.UnitY, "fresh direction defaults world +Z");
        slide.Capture(10, false);
        for (int tick = 1; tick <= TraversalSlidePresentation.EntryTicks; tick++)
        {
            float previous = slide.Sample(1);
            slide.Capture((ulong)(10 + tick), true, Vector2.UnitY);
            Near(slide.Sample(0), previous, "entry preserves previous completed endpoint");
            Near(slide.Sample(1), Ease((float)tick / TraversalSlidePresentation.EntryTicks), "entry uses six completed ticks");
            Check(slide.Phase == (tick == TraversalSlidePresentation.EntryTicks ? TraversalSlidePhase.Glide : TraversalSlidePhase.Entry), "entry ends at stable glide");
            Check(slide.Sample(.5f) >= previous && slide.Sample(.5f) <= slide.Sample(1), "entry picture is monotonic");
        }
        for (ulong frame = 17; frame <= 40; frame++)
        {
            slide.Capture(frame, true);
            Near(slide.Sample(.5f), 1, "held glide remains fully posed without cycling");
            Check(slide.Phase == TraversalSlidePhase.Glide, "held glide has a stable phase");
        }
        slide.Capture(40, false, -Vector2.UnitY);
        Check(slide.Phase == TraversalSlidePhase.Glide && slide.Sample(1) == 1
            && slide.SampleDirection(1) == Vector2.UnitY, "duplicate capture cannot replace state or direction");
        for (int tick = 1; tick <= TraversalSlidePresentation.ExitTicks; tick++)
        {
            float previous = slide.Sample(1);
            slide.Capture((ulong)(40 + tick), false);
            Near(slide.Sample(0), previous, "exit preserves previous completed endpoint");
            Near(slide.Sample(1), Ease(1 - (float)tick / TraversalSlidePresentation.ExitTicks), "exit uses six completed ticks, matching crouch recovery");
            Check(slide.Phase == (tick == TraversalSlidePresentation.ExitTicks ? TraversalSlidePhase.None : TraversalSlidePhase.Exit), "exit retires at zero");
        }
        for (ulong frame = (ulong)(40 + TraversalSlidePresentation.ExitTicks + 1); frame <= 49; frame++)
        { slide.Capture(frame, false); Near(slide.Sample(.5f), 0, "inactive holding remains zero"); }

        slide.Capture(50, true); slide.Capture(51, true);
        float interrupted = slide.Sample(1);
        slide.Capture(52, false);
        Near(slide.Sample(0), interrupted, "interrupted entry exits without a visual jump");
        Check(slide.Phase == TraversalSlidePhase.Exit && slide.Sample(1) < interrupted, "entry interruption begins exit");
        float recovering = slide.Sample(1);
        slide.Capture(53, true);
        Near(slide.Sample(0), recovering, "exit interruption reenters from its retained amount");
        Check(slide.Phase == TraversalSlidePhase.Entry && slide.Sample(1) > recovering, "exit interruption begins entry");
        for (ulong frame = 54; frame <= 58; frame++) slide.Capture(frame, true);
        Check(slide.Phase == TraversalSlidePhase.Glide && slide.Sample(1) == 1, "interrupted entry still has a complete six-tick duration");

        slide.Capture(60, false);
        Check(slide.Phase == TraversalSlidePhase.None && slide.Sample(0) == 0 && slide.Sample(1) == 0, "missing tick discards old transition and snaps accepted inactive level");
        slide.Capture(70, true, Vector2.UnitX);
        Check(slide.Phase == TraversalSlidePhase.Glide && slide.Sample(0) == 1 && slide.Sample(1) == 1, "missing history seeds glide without inventing entry");
        slide.Capture(5, false);
        Near(slide.Sample(.5f), 0, "backward frame reseeds rather than rewinding old history");
        slide.Capture(6, true, Vector2.UnitX);
        float before = slide.Sample(0), after = slide.Sample(1), middle = slide.Sample(.5f);
        foreach (float alpha in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Near(slide.Sample(alpha), after, "nonfinite alpha selects completed endpoint");
        Near(slide.Sample(-1), before, "negative alpha clamps to previous endpoint");
        Near(slide.Sample(2), after, "oversized alpha clamps to completed endpoint");
        for (int repeat = 0; repeat < 16; repeat++)
        {
            Near(slide.Sample(.5f), middle, "repeated pictures cannot advance slide timeline");
            FiniteDirection(slide.SampleDirection(.5f));
        }
        for (ulong frame = 7; frame < 80; frame++)
        {
            Vector2 travel = (frame % 6) switch
            {
                0 => new(float.NaN, 1), 1 => new(float.PositiveInfinity, 0),
                2 => new(float.MaxValue, -float.MaxValue), 3 => -Vector2.UnitX,
                4 => Vector2.Zero, _ => Vector2.UnitX
            };
            slide.Capture(frame, frame % 9 < 5, travel);
            foreach (float alpha in new[] { -1f, 0, .25f, .5f, 1, 2, float.NaN })
            {
                float amount = slide.Sample(alpha);
                Check(float.IsFinite(amount) && amount is >= 0 and <= 1, "all transition samples stay finite and bounded");
                FiniteDirection(slide.SampleDirection(alpha));
            }
        }
        slide.Capture(ulong.MaxValue, true);
        slide.Capture(0, false);
        Near(slide.Sample(0), 0, "frame counter wrap is a discontinuity");
        slide.Reset();
        Check(!slide.Ready && slide.Phase == TraversalSlidePhase.None && slide.Sample(1) == 0, "explicit reset clears phase and envelope");
        Check(slide.SampleDirection(1) == Vector2.UnitY, "explicit reset clears retained direction");
        slide.Capture(1, true);
        Near(slide.Sample(0), 1, "new lifetime seeds its accepted level immediately");
        foreach (Vector2 target in new[] { -Vector2.UnitY, Vector2.UnitX, -Vector2.UnitX })
        {
            slide.Reset(); slide.Capture(0, true, Vector2.UnitY);
            float previousAlignment = Vector2.Dot(slide.SampleDirection(1), target);
            for (ulong frame = 1; frame <= 12; frame++)
            {
                Vector2 previousDirection = slide.SampleDirection(1);
                slide.Capture(frame, true, target);
                Vector2 currentDirection = slide.SampleDirection(1);
                float alignment = Vector2.Dot(currentDirection, target);
                Check(alignment > previousAlignment, "reverse and lateral directions converge every completed tick");
                Check(Vector2.Dot(previousDirection, currentDirection)
                    >= MathF.Cos(MathF.PI * TraversalSlidePresentation.DirectionBlend) - .00001f,
                    "completed direction turn stays within the shortest-arc angular bound");
                Check(slide.SampleDirection(0) == previousDirection,
                    "direction sampling retains the exact previous endpoint");
                FiniteDirection(slide.SampleDirection(.5f));
                FiniteDirection(currentDirection);
                if (frame == 1)
                    Check(MathF.Abs(currentDirection.X) > .1f,
                        "exact reverse and lateral requests turn on their first completed tick");
                previousAlignment = alignment;
            }
            Check(Vector2.Dot(slide.SampleDirection(1), target) > .99f,
                "reverse and lateral targets converge within twelve completed ticks");
        }
        return _checks;
    }
}
