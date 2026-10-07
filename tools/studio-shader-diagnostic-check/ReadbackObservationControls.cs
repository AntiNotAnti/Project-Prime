using System.Globalization;
using MphRead.Mods.Render;

internal static class ReadbackObservationControls
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string description)
        {
            if (!value) throw new InvalidOperationException(description);
            checks++;
        }
        var observation = new ShaderDiagnosticReadbackObservation();
        var writer = new StringWriter();
        int clocks = 0;
        long ticks = 10 * System.Diagnostics.Stopwatch.Frequency;
        long Clock() { clocks++; return ticks += System.Diagnostics.Stopwatch.Frequency; }
        DateTimeOffset Utc() => new(2026, 10, 7, 7, 0, 0, TimeSpan.Zero);
        Check(!observation.Mark("poll-wait", "START"), "outside fixture cannot write");
        using (observation.BeginReadback(0, 0, 1, 1))
            Check(!observation.Mark("poll-wait", "START") && clocks == 0, "outside fixture cannot acquire readback owner or sample clock");
        foreach (string? bad in new string?[] { null, "", "0", "true", "01", " 1", "1 " })
            for (int position = 0; position < 3; position++)
            {
                string?[] flags = { "1", "1", "1" }; flags[position] = bad;
                using var disabled = observation.BeginLayeredScope(flags[0], flags[1], flags[2], writer, Clock, Utc);
                using var noRead = observation.BeginReadback(0, 0, 1, 1);
                Check(!observation.Mark("poll-wait", "START") && clocks == 0 && writer.ToString().Length == 0,
                    "every nonexact triple opt-in avoids clocks and sink output");
            }
        IDisposable scope = observation.BeginLayeredScope("1", "1", "1", writer, Clock, Utc);
        Check(!observation.Mark("poll-wait", "START") && clocks == 1, "check scope alone cannot emit a readback marker");
        IDisposable read = observation.BeginReadback(6, 4, 1, 1);
        using (observation.BeginLayeredScope("1", "1", "1", writer, Clock, Utc))
        using (observation.BeginReadback(99, 99, 1, 1))
            Check(observation.Mark("read-target", "START"), "nested inactive owners cannot steal or revoke actual readback lease");
        string record = writer.ToString().Trim();
        Check(record.Contains("scope=1 read=1 phase=read-target START utc=2026-10-07T07:00:00.0000000+00:00", StringComparison.Ordinal),
            "actual helper records explicit scope, read, phase and UTC timestamp");
        Check(record.Contains("elapsedMs=2000.000 readElapsedMs=1000.000 sequence=1", StringComparison.Ordinal)
            && record.Contains("monotonicTicks=", StringComparison.Ordinal)
            && record.Contains("frequency=" + System.Diagnostics.Stopwatch.Frequency, StringComparison.Ordinal)
            && record.EndsWith("rect=6,4,1,1", StringComparison.Ordinal), "actual helper records truthful monotonic elapsed and original readback bounds");
        int sampled = clocks;
        Check(!observation.Mark("poll-wait\nforged", "START") && !observation.Mark("poll-wait", "complete")
            && clocks == sampled, "unknown phase and edge cannot forge native-boundary records");
        bool wrongThread = Task.Run(() =>
        {
            using var otherScope = observation.BeginLayeredScope("1", "1", "1", writer, Clock, Utc);
            using var otherRead = observation.BeginReadback(1, 1, 1, 1);
            return observation.Mark("poll-wait", "START");
        }).GetAwaiter().GetResult();
        Check(!wrongThread && clocks == sampled, "unrelated worker cannot emit or revoke the owner-thread observation");
        Check(observation.Mark("read-target", "DONE"), "original owner remains active after unrelated scope disposal");
        read.Dispose(); read.Dispose();
        Check(!observation.Mark("poll-wait", "START"), "disposed read lease emits nothing inside still-live fixture");
        using (observation.BeginReadback(2, 3, 1, 1))
        {
            read.Dispose();
            Check(observation.Mark("map-async", "START") && writer.ToString().Contains("read=2 phase=map-async", StringComparison.Ordinal),
                "stale readback disposal cannot disable the next readback ordinal");
        }
        scope.Dispose(); scope.Dispose();
        Check(!observation.Mark("poll-wait", "START"), "fixture disposal leaves no active observation owner");
        using (observation.BeginLayeredScope("1", "1", "1", writer, Clock, Utc))
        using (observation.BeginReadback(1, 1, 1, 1))
        {
            scope.Dispose();
            Check(observation.Mark("poll-wait", "START"), "stale fixture disposal cannot disable next owner");
        }

        var badClock = new ShaderDiagnosticReadbackObservation();
        using (badClock.BeginLayeredScope("1", "1", "1", writer, () => throw new IOException("clock failed"), Utc))
        using (badClock.BeginReadback(0, 0, 1, 1))
            Check(!badClock.Mark("poll-wait", "START"), "scope clock failure is contained and creates no owner");
        var brokenReadClock = new ShaderDiagnosticReadbackObservation();
        int readClocks = 0;
        using (brokenReadClock.BeginLayeredScope("1", "1", "1", writer,
            () => ++readClocks == 2 ? throw new IOException("read clock failed") : readClocks, Utc))
        {
            using (brokenReadClock.BeginReadback(0, 0, 1, 1))
                Check(!brokenReadClock.Mark("poll-wait", "START"), "failed readback clock creates no active lease");
            using (brokenReadClock.BeginReadback(0, 0, 1, 1))
                Check(brokenReadClock.Mark("poll-wait", "START"), "failed readback clock does not strand fixture ownership");
        }
        var brokenMarkerClock = new ShaderDiagnosticReadbackObservation();
        int markerClocks = 0;
        using (brokenMarkerClock.BeginLayeredScope("1", "1", "1", writer,
            () => ++markerClocks <= 2 ? markerClocks : throw new IOException("marker clock failed"), Utc))
        using (brokenMarkerClock.BeginReadback(0, 0, 1, 1))
        {
            bool[] attempts = Enumerable.Range(0, ShaderDiagnosticReadbackObservation.MaximumReadbackMarkers + 8)
                .Select(_ => brokenMarkerClock.Mark("poll-wait", "START")).ToArray();
            Check(attempts.All(v => !v) && markerClocks == ShaderDiagnosticReadbackObservation.MaximumReadbackMarkers + 2,
                "failed marker clock attempts are bounded before sink access");
        }
        var backwardsClock = new ShaderDiagnosticReadbackObservation();
        int clockIndex = 0;
        long[] clockValues = { 10, 20, 1, 30 };
        var backwardsWriter = new StringWriter();
        using (backwardsClock.BeginLayeredScope("1", "1", "1", backwardsWriter, () => clockValues[clockIndex++], Utc))
        using (backwardsClock.BeginReadback(0, 0, 1, 1))
        {
            Check(!backwardsClock.Mark("poll-wait", "START") && backwardsWriter.ToString().Length == 0,
                "backwards injected tick cannot emit a fabricated nonnegative duration");
            Check(backwardsClock.Mark("poll-wait", "DONE"), "invalid injected timestamp does not replace rendering ownership");
        }
        var failedSink = new ShaderDiagnosticReadbackObservation();
        int samples = 0;
        using (failedSink.BeginLayeredScope("1", "1", "1", new BrokenWriter(),
            () => { samples++; return samples; }, Utc))
        using (failedSink.BeginReadback(0, 0, 1, 1))
        {
            bool[] attempts = Enumerable.Range(0, ShaderDiagnosticReadbackObservation.MaximumReadbackMarkers + 8)
                .Select(_ => failedSink.Mark("poll-wait", "START")).ToArray();
            Check(attempts.All(v => !v) && samples == ShaderDiagnosticReadbackObservation.MaximumReadbackMarkers + 2,
                "broken sink attempts stay bounded and never replace native work");
        }
        var failedUtc = new ShaderDiagnosticReadbackObservation();
        int utcSamples = 0;
        using (failedUtc.BeginLayeredScope("1", "1", "1", writer, () => 1,
            () => { utcSamples++; throw new IOException("UTC failed"); }))
        using (failedUtc.BeginReadback(0, 0, 1, 1))
        {
            bool[] attempts = Enumerable.Range(0, ShaderDiagnosticReadbackObservation.MaximumReadbackMarkers + 8)
                .Select(_ => failedUtc.Mark("poll-wait", "START")).ToArray();
            Check(attempts.All(v => !v) && utcSamples == ShaderDiagnosticReadbackObservation.MaximumReadbackMarkers,
                "broken UTC clock attempts stay bounded");
        }
        var bounded = new ShaderDiagnosticReadbackObservation();
        var cappedWriter = new StringWriter();
        for (int fixture = 0; fixture < 2; fixture++)
        {
            using var cappedScope = bounded.BeginLayeredScope("1", "1", "1", cappedWriter, () => 1, Utc);
            for (int ordinal = 1; ordinal <= 8; ordinal++)
            {
                using var cappedRead = bounded.BeginReadback(0, 0, 1, 1);
                bool[] marks = Enumerable.Range(0, ShaderDiagnosticReadbackObservation.MaximumReadbackMarkers + 1)
                    .Select(_ => bounded.Mark("poll-wait", "START")).ToArray();
                Check(marks.Take(ShaderDiagnosticReadbackObservation.MaximumReadbackMarkers).All(v => v) && !marks[^1],
                    "each readback receives its own bounded attempts even after preceding saturated reads");
                if (ordinal == 6)
                    Check(cappedWriter.ToString().Contains($"scope={fixture + 1} read=6 phase=poll-wait START", StringComparison.Ordinal),
                        "fifth saturated read cannot exhaust the targeted sixth readback budget");
            }
            using var beyondScope = bounded.BeginReadback(0, 0, 1, 1);
            Check(!bounded.Mark("poll-wait", "START"), "eight saturated reads respect the fixture-wide cap");
        }
        using (bounded.BeginLayeredScope("1", "1", "1", cappedWriter, () => throw new IOException("exhausted"), Utc))
        using (bounded.BeginReadback(0, 0, 1, 1))
            Check(!bounded.Mark("poll-wait", "START")
                && cappedWriter.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length
                    == ShaderDiagnosticReadbackObservation.MaximumProcessMarkers, "whole-process cap survives repeated fixture scopes");

        var order = new List<int>();
        IDisposable composite = ShaderDiagnosticReadbackObservation.Combine(new BrokenDispose(order, 1), new BrokenDispose(order, 2));
        composite.Dispose(); composite.Dispose();
        Check(order.SequenceEqual(new[] { 2, 1 }), "combined diagnostic teardown contains failures and releases both once in reverse order");
        var original = new InvalidOperationException("original GPU assertion");
        bool cleanup = false;
        try
        {
            using var lease = ShaderDiagnosticReadbackObservation.Combine(new BrokenDispose(order, 3), new BrokenDispose(order, 4));
            try { throw original; } finally { cleanup = true; }
        }
        catch (Exception ex) { Check(ReferenceEquals(ex, original) && cleanup, "failed diagnostic Dispose cannot replace original exception/finally"); }

        string repo = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(repo, "src/MphRead/Mods/Render/ModernGraphicsCompat.World.cs")))
            repo = Directory.GetParent(repo)?.FullName ?? throw new InvalidOperationException("Repository sources missing.");
        string helper = File.ReadAllText(Path.Combine(repo, "src/MphRead/Mods/Render/ShaderDiagnosticReadbackObservation.cs"));
        Check(helper.StartsWith("#if !ANDROID && !MPHREAD_SERVER", StringComparison.Ordinal), "CPU observation implementation remains desktop only");
        foreach (string path in new[] { "ModernGraphicsCompat.World.cs", "ModernGraphicsCompat.Commands.cs" })
        {
            string text = File.ReadAllText(Path.Combine(repo, "src/MphRead/Mods/Render", path));
            bool desktopBlock = false;
            foreach (string line in text.Split('\n'))
            {
                if (line.Trim() == "#if !ANDROID") desktopBlock = true;
                if (line.Contains("NativeReadbackObservationForCheck", StringComparison.Ordinal))
                    Check(desktopBlock, "every shared renderer marker/lease reference is excluded on Android");
                if (line.Trim() == "#endif") desktopBlock = false;
            }
        }
        return checks;
    }

    private sealed class BrokenWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("observation sink failed");
    }
    private sealed class BrokenDispose : IDisposable
    {
        private readonly List<int> _order;
        private readonly int _value;
        internal BrokenDispose(List<int> order, int value) => (_order, _value) = (order, value);
        public void Dispose() { _order.Add(_value); throw new IOException("observation Dispose failed"); }
    }
}
