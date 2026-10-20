using System;
using System.Globalization;
using MphRead.Mods.Movement;

namespace MphRead.NetTest;

internal static class MovementTelemetryHudTests
{
    internal static int Run()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        int checks = 0;
        void Check(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException(message); }
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var air = new MovementTelemetrySample(12.5f, .75f, true, 15.26f);
            Check(MovementTelemetryHud.Format(air)
                == ("HORIZ 12.5 U/S", "AIR EFF 75%", "TOTAL 15.3 U/S | AIR"),
                "Horizontal speed, total speed and air efficiency use invariant formatting.");
            Check(MovementTelemetryHud.Format(new(0, -1, true)).Efficiency == "AIR EFF --"
                && MovementTelemetryHud.Format(new(0, .75f, false, Grounded: true)).Efficiency == "AIR EFF --",
                "No-input and grounded samples have no air efficiency.");
            Check(MovementTelemetryHud.Format(new(float.NaN, float.NaN, true))
                == ("HORIZ -- U/S", "AIR EFF --", "TOTAL -- U/S | AIR"),
                "Missing and non-finite telemetry use placeholders.");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
            {
                var formatted = MovementTelemetryHud.Format(new(invalid, invalid, true, invalid));
                Check(formatted.Speed == "HORIZ -- U/S" && formatted.Efficiency == "AIR EFF --"
                    && formatted.Details == "TOTAL -- U/S | AIR", "Invalid speed/efficiency stays out of HUD text.");
            }
            Check(MovementTelemetryHud.Format(new(0, 2, true, 0)).Efficiency == "AIR EFF 100%",
                "Air efficiency presentation is bounded to 100 percent.");
            foreach (bool grounded in new[] { false, true })
            foreach (bool crouched in new[] { false, true })
            foreach (bool sliding in new[] { false, true })
            {
                var sample = new MovementTelemetrySample(0, -1, !grounded, 0, grounded, crouched, sliding);
                string state = !grounded ? "AIR" : sliding ? "SLIDING" : crouched ? "CROUCH" : "GROUND";
                Check(MovementTelemetryHud.Format(sample).Details == "TOTAL 0.0 U/S | " + state,
                    "Actual grounded state takes precedence over sliding and crouched flags.");
            }
            Check(MovementTelemetryHud.Format(new(0, -1, false)).Details == "TOTAL -- U/S | AIR",
                "Legacy three-argument samples cannot invent grounded contact or total speed.");
            var cache = new MovementTelemetryHud.TextCache();
            var first = cache.Get(air);
            for (int frame = 0; frame < 1000; frame++)
            {
                var repeated = cache.Get(air);
                if (!ReferenceEquals(first.Speed, repeated.Speed) || !ReferenceEquals(first.Efficiency, repeated.Efficiency)
                    || !ReferenceEquals(first.Details, repeated.Details))
                    throw new InvalidOperationException("Repeated render samples reuse all three formatted lines.");
            }
            Check(true, "Repeated render samples retain cached line references.");
            Check(cache.Get(air with { TotalSpeed = 22 }).Details == "TOTAL 22.0 U/S | AIR",
                "A changed total speed invalidates the text cache.");
            Check(cache.Get(air with { Airborne = false, Grounded = true, Crouched = true }).Details == "TOTAL 15.3 U/S | CROUCH"
                && cache.Get(air with { Airborne = false, Grounded = true, Sliding = true }).Details == "TOTAL 15.3 U/S | SLIDING",
                "Accepted stance and ground changes invalidate the text cache.");
            var missing = new MovementTelemetrySample(float.NaN, float.NaN, true);
            var missingFirst = cache.Get(missing);
            var missingRepeated = cache.Get(missing);
            Check(ReferenceEquals(missingFirst.Speed, missingRepeated.Speed)
                && ReferenceEquals(missingFirst.Efficiency, missingRepeated.Efficiency)
                && ReferenceEquals(missingFirst.Details, missingRepeated.Details),
                "Repeated non-finite samples also reuse all three cached lines.");
            var otherScene = new MovementTelemetryHud.TextCache();
            otherScene.Get(new(999, 1, true, 1000));
            Check(cache.Get(air) == first, "Independent scene text caches cannot contaminate one another.");
            var landing = MovementTelemetrySample.Completed(12, 15, .9f, true, true, true);
            Check(landing.Grounded && !landing.Airborne && landing.StrafeEfficiency == -1
                && landing.Speed == 12 && landing.TotalSpeed == 15
                && MovementTelemetryHud.Format(landing).Details == "TOTAL 15.0 U/S | SLIDING"
                && MovementTelemetryHud.Format(landing).Efficiency == "AIR EFF --",
                "Postcollision landing replaces the incoming airborne phase and clears air efficiency.");
            var leavingGround = MovementTelemetrySample.Completed(12, 16, .5f, false, true, true);
            Check(!leavingGround.Grounded && leavingGround.Airborne && leavingGround.StrafeEfficiency == .5f
                && MovementTelemetryHud.Format(leavingGround).Details == "TOTAL 16.0 U/S | AIR",
                "Completed contact wins over crouch/slide latches and native grounded grace.");
            var standing = MovementTelemetrySample.Completed(0, 0, 1, true, false, false);
            Check(MovementTelemetryHud.Format(standing)
                == ("HORIZ 0.0 U/S", "AIR EFF --", "TOTAL 0.0 U/S | GROUND"),
                "Stationary completed support has finite zero speeds and no air efficiency.");
            float maximum = TraversalSlideFeedback.MaximumAmount;
            Check(maximum == 0xFFFF * .18f
                && TraversalSlideFeedback.Amount(10, 10, true, true) == maximum
                && TraversalSlideFeedback.Amount(1000, 10, true, true) == maximum,
                "Slide feedback is capped at eighteen percent even above the reference speed.");
            Check(Math.Abs(TraversalSlideFeedback.Amount(5, 10, true, true) - maximum / 2) < .001f,
                "Slide feedback scales with completed horizontal speed below the quiet cap.");
            foreach (bool supported in new[] { false, true })
            foreach (bool sliding in new[] { false, true })
                Check(TraversalSlideFeedback.Amount(10, 10, supported, sliding)
                    == (supported && sliding ? maximum : 0),
                    "Airborne and non-slide states never request Traversal slide audio.");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, 0f })
                Check(TraversalSlideFeedback.Amount(invalid, 10, true, true) == 0
                    && TraversalSlideFeedback.Amount(10, invalid, true, true) == 0,
                    "Stationary, invalid and non-positive reference values never reach the native loop.");
            Console.WriteLine($"Movement telemetry HUD formatting/cache and slide amount PASS ({checks})");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Movement telemetry HUD formatting FAIL: " + error);
            return 1;
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
