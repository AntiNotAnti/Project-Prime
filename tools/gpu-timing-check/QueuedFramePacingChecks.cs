using MphRead.Mods.Render;

internal static class QueuedFramePacingChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string description)
        {
            if (!value) throw new InvalidOperationException(description);
            Console.WriteLine("QUEUEDPACING PASS " + description);
            checks++;
        }
        void Cadence(int cap, double displayHz, bool paced, double expected, string description)
        {
            Check(QueuedFramePacing.SelectedCadenceHz(cap, displayHz, paced) == expected, description);
        }
        Cadence(60, 120, true, 60, "FIFO 120 Hz plus a 60 cap declares its 60 Hz submission budget");
        Cadence(120, 60, true, 60, "FIFO 60 Hz plus a 120 cap declares its 60 Hz presentation ceiling");
        Cadence(144, 120, true, 120, "FIFO remains the ceiling for a faster numeric budget");
        Cadence(90, 120, true, 90, "slower numeric budget remains effective on a faster FIFO display");
        Cadence(0, 120, true, 120, "display pacing declares the active display clock");
        Cadence(-1, 120, true, 120, "unlimited with unavoidable FIFO declares the display clock");
        Cadence(-1, 120, false, 0, "unlimited nonblocking presentation stays unpaced");
        Cadence(60, 120, false, 60, "nonblocking 60 cap declares the software budget");
        Cadence(240, 60, false, 240, "nonblocking caps are not limited by the monitor probe");
        foreach (double invalid in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
        {
            Cadence(60, invalid, true, 60, "known numeric budget survives invalid display probe: " + invalid);
            Cadence(0, invalid, true, 0, "unavailable display cadence stays unknown: " + invalid);
        }

        // Use an exact synthetic Stopwatch clock. The production helper starts
        // the budget before acquisition, recording, submission and presentation.
        // It therefore subtracts all those phases from one shared deadline.
        const long start = 1_000_000, frequency = 1_000_000;
        double Remaining(long microseconds) => QueuedFramePacing.RemainingMilliseconds(
            start, start + microseconds, frequency, 60);
        const double budget = 1000d / 60;
        bool Near(double actual, double expected) => Math.Abs(actual - expected) < 1e-9;
        Check(Near(Remaining(0), budget), "new frame owns exactly one 60 Hz budget");
        Check(Near(Remaining(2_000), budget - 2), "acquisition consumes the frame budget");
        Check(Near(Remaining(7_000), budget - 7), "acquisition plus recording consume the same budget");
        Check(Near(Remaining(12_000), budget - 12), "blocking presentation consumes remaining budget without another full wait");
        Check(Remaining(16_667) == 0, "completed 60 Hz budget adds no software wait");
        Check(Remaining(30_000) == 0, "slow acquisition or presentation never creates a second deadline");
        Check(QueuedFramePacing.RemainingMilliseconds(start, start + 12_000, frequency, 0) == 0
            && QueuedFramePacing.RemainingMilliseconds(start, start + 12_000, frequency, -1) == 0,
            "display and unlimited modes add no numeric software budget");
        Check(Near(QueuedFramePacing.RemainingMilliseconds(start, start + 2_000, frequency, 240),
            1000d / 240 - 2), "high-refresh budget retains sub-millisecond precision");
        bool rejected = false;
        try { QueuedFramePacing.RemainingMilliseconds(start, start - 1, frequency, 60); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "reversed frame clock is rejected");
        rejected = false;
        try { QueuedFramePacing.RemainingMilliseconds(start, start, 0, 60); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "invalid clock frequency is rejected");
        return checks;
    }
}
