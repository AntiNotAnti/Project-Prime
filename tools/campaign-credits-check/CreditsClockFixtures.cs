using MphRead.Mods.Campaign;

// Invented timestamps only. No wall clock, source files, timeline, UI, audio,
// rendering or campaign time-record owner participates in these fixtures.
internal static class CreditsClockFixtures
{
    private static int _checks;

    internal static void Run()
    {
        _checks = 0;
        InvalidTimestamps(); InitialAndReset(); SteadyCadences(); Fractions();
        InactiveAndResume(); BackwardClock(); HitchBounds();
        Console.WriteLine($"CREDITS CLOCK PASS {_checks} invented scheduling checks; explicit30Hz presentation, no wall clock or gameplay records.");
    }

    private static void InvalidTimestamps()
    {
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d, -double.Epsilon })
        foreach (bool active in new[] { false, true })
        {
            var clock = new CampaignCreditsClock();
            Check(clock.Advance(0, true) == 0 && clock.Advance(1d / 60, true) == 0,
                "invalid-input witness begins with half a retained tick");
            bool rejected = false;
            try { clock.Advance(invalid, active); }
            catch (ArgumentOutOfRangeException error) { rejected = error.ParamName == "monotonicSeconds"; }
            Check(rejected, "negative and nonfinite timestamps refuse for both availability states");
            Check(clock.Advance(1d / 30, true) == 1,
                "rejected timestamp does not change the prior active baseline or fraction");
        }
        var finite = new CampaignCreditsClock();
        Check(finite.Advance(double.MaxValue, true) == 0 && finite.Advance(double.MaxValue, true) == 0,
            "finite maximum timestamp is a valid initial baseline and duplicate");
        Check(new CampaignCreditsClock().Advance(-0d, true) == 0, "negative zero is a finite nonnegative baseline");
    }

    private static void InitialAndReset()
    {
        var clock = new CampaignCreditsClock();
        Check(clock.Advance(1234, true) == 0, "first active observation never replays time since process origin");
        Check(clock.Advance(1234 + 1d / 30, true) == 1, "active baseline starts one presentation tick later");
        clock.Reset(); clock.Reset();
        Check(clock.Advance(5000, true) == 0 && clock.Advance(5000 + 1d / 30, true) == 1,
            "reset is idempotent and requires a fresh active baseline");
        var fraction = new CampaignCreditsClock();
        Check(fraction.Advance(0, true) == 0 && fraction.Advance(1d / 40, true) == 0,
            "reset witness retains three quarters of a tick");
        fraction.Reset();
        Check(fraction.Advance(10, true) == 0 && fraction.Advance(10 + 1d / 120, true) == 0
            && fraction.Advance(10 + 1d / 30, true) == 1,
            "reset discards the old fractional tick as well as elapsed time");
    }

    private static void SteadyCadences()
    {
        const int seconds = 4;
        foreach (int hz in new[] { 29, 30, 60, 120, 144 })
        {
            var clock = new CampaignCreditsClock();
            Check(clock.Advance(0, true) == 0, "steady cadence starts without an elapsed tick");
            int total = 0, maximum = 0;
            for (int frame = 1; frame <= hz * seconds; frame++)
            {
                int steps = clock.Advance((double)frame / hz, true);
                total += steps; maximum = Math.Max(maximum, steps);
                int expectedPrefix = frame * 30 / hz;
                Check(steps is >= 0 and <= 8 && total == expectedPrefix,
                    $"{hz}Hz prefix {frame} preserves the mathematical30Hz schedule");
            }
            Check(total == seconds * 30 && maximum == (hz < 30 ? 2 : 1),
                $"{hz}Hz produces120 presentation steps over four active seconds");
        }
        // One observation per second is a hitch, so the explicit250ms policy
        // deliberately admits7.5 ticks/call rather than replaying30 ticks.
        var slow = new CampaignCreditsClock();
        Check(slow.Advance(0, true) == 0, "1Hz observation establishes its baseline");
        int slowTotal = 0;
        for (int frame = 1; frame <= 8; frame++)
        {
            int steps = slow.Advance(frame, true); slowTotal += steps;
            Check(steps == (frame % 2 == 1 ? 7 : 8), "1Hz hitch cadence alternates7/8 under the250ms cap");
        }
        Check(slowTotal == 60, "eight1Hz observations admit two seconds of capped presentation work");
    }

    private static void Fractions()
    {
        var clock = new CampaignCreditsClock();
        Check(clock.Advance(0, true) == 0 && clock.Advance(1d / 120, true) == 0
            && clock.Advance(2d / 120, true) == 0 && clock.Advance(3d / 120, true) == 0
            && clock.Advance(4d / 120, true) == 1, "four quarter-tick observations produce exactly one tick");
        Check(clock.Advance(5d / 120, true) == 0 && clock.Advance(6d / 120, true) == 0,
            "a consumed tick does not consume the next fractional accumulation");
        Check(clock.Advance(6d / 120, true) == 0 && clock.Advance(8d / 120, true) == 1,
            "duplicate timestamp emits no tick and preserves a previously retained half tick");
    }

    private static void InactiveAndResume()
    {
        foreach (string unavailable in new[] { "inactive", "loading", "modal-covered" })
        {
            var clock = new CampaignCreditsClock();
            Check(clock.Advance(0, true) == 0 && clock.Advance(1d / 40, true) == 0,
                unavailable + " witness begins with a retained fraction");
            Check(clock.Advance(1, false) == 0 && clock.Advance(100, false) == 0,
                unavailable + " neither advances nor stores suspended wall time");
            Check(clock.Advance(1000, true) == 0, unavailable + " resume establishes a new baseline without catch-up");
            Check(clock.Advance(1000 + 1d / 120, true) == 0 && clock.Advance(1000 + 1d / 30, true) == 1,
                unavailable + " resume discards pre-suspension fraction and schedules new elapsed time only");
        }
        var initiallyLoading = new CampaignCreditsClock();
        Check(initiallyLoading.Advance(500, false) == 0 && initiallyLoading.Advance(900, true) == 0
            && initiallyLoading.Advance(900 + 1d / 30, true) == 1,
            "initial loading does not start the timeline until its first available baseline");
    }

    private static void BackwardClock()
    {
        var clock = new CampaignCreditsClock();
        Check(clock.Advance(10, true) == 0 && clock.Advance(10 + 1d / 40, true) == 0,
            "backward-clock witness holds a fractional tick");
        Check(clock.Advance(1, true) == 0 && clock.Advance(1 + 1d / 120, true) == 0
            && clock.Advance(1 + 1d / 30, true) == 1,
            "backward observation rebases and clears stale fractional work without negative steps");
        Check(clock.Advance(0, false) == 0 && clock.Advance(100, true) == 0,
            "backward inactive observation also requires a new active baseline");
    }

    private static void HitchBounds()
    {
        var clock = new CampaignCreditsClock();
        Check(clock.Advance(0, true) == 0 && clock.Advance(.25, true) == 7
            && clock.Advance(.5, true) == 8, "two exact250ms intervals retain their half tick and admit15 total steps");
        Check(clock.Advance(1000, true) == 7 && clock.Advance(1000, true) == 0
            && clock.Advance(1000 + 1d / 60, true) == 1,
            "long hitch discards excess wall time, retains only admitted fraction and has no later catch-up");
        for (int i = 1; i <= 20; i++)
            Check(clock.Advance(1000 + i * 1000d, true) is >= 0 and <= 8,
                "repeated large hitches never exceed eight steps per observation");
        var extreme = new CampaignCreditsClock();
        Check(extreme.Advance(0, true) == 0 && extreme.Advance(double.MaxValue, true) == 7
            && extreme.Advance(double.MaxValue, true) == 0,
            "finite extreme jump is capped without integer overflow or repeated deferred work");
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("Credits clock: " + message);
    }
}
