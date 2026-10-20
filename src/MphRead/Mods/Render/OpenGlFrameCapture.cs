using System;
using System.IO;
using System.Text.Json;

namespace MphRead.Mods.Render;

// Value storage only: no scene/resource references, sorting, I/O or per-frame allocation.
internal readonly record struct OpenGlFrameSample(
    long PresentTimestamp, long Segment, long PresentationGeneration, ulong SimulationTick,
    double IntervalMs, double SimulationMs, double InputPollMs, double RenderMs,
    double UiMs, double SwapMs, double UnattributedGapMs, double? InputToPresentProxyMs,
    double PreparationMs, double DrawItemsMs, double WorldMs, double ItemSetupMs,
    double GeometrySubmissionMs, double ShadowMs, double HudMs, double UploadMs,
    double CompositeMs, long UploadBytes, long BufferOrphans, long AllocatedBytes,
    int Gen0, int Gen1, int Gen2, int SimulationSteps, long DroppedSimulationSteps,
    int Cap, string Vsync, string PrimarySource);

internal sealed class OpenGlFrameCapture
{
    internal const int DefaultCapacity = 262144;
    internal const int MaximumCapacity = 1048576;
    private readonly OpenGlFrameSample[] _samples;
    internal int Count { get; private set; }
    internal long Dropped { get; private set; }
    internal long Cancelled { get; private set; }
    internal long Segment { get; private set; }
    internal OpenGlFrameCapture(int capacity = DefaultCapacity)
    {
        if (capacity < 1 || capacity > MaximumCapacity) throw new ArgumentOutOfRangeException(nameof(capacity));
        _samples = new OpenGlFrameSample[capacity];
    }
    internal void Cancel() { Cancelled++; Segment++; }
    internal void Add(in OpenGlFrameSample sample)
    {
        if (!double.IsFinite(sample.IntervalMs) || sample.IntervalMs <= 0
            || !double.IsFinite(sample.SimulationMs) || !double.IsFinite(sample.RenderMs)
            || !double.IsFinite(sample.SwapMs)) { Dropped++; Cancel(); return; }
        if (Count == _samples.Length) { Dropped++; return; }
        _samples[Count++] = sample;
    }
    internal static double ElapsedMs(long start, long end, long frequency)
        // Compare before subtraction and use floating point to avoid signed overflow.
        => start >= 0 && end >= start && frequency > 0
            ? ((double)end - start) * 1000.0 / frequency : double.NaN;

    internal static string Attribute(double interval, double simulation, double render,
        double ui, double swap, double upload, bool gc)
    {
        if (!(interval > 0) || gc) return "unknown/mixed";
        // UI/upload are inclusive children of render. Select only a dominant
        // observed scope; residual scheduler/other work cannot prove a cause.
        double threshold = interval * .6;
        if (simulation >= threshold) return "simulation";
        if (swap >= threshold) return "sync/present";
        if (ui >= threshold) return "ui";
        if (upload >= threshold) return "texture-upload-cpu";
        if (render - ui >= threshold) return "gl-cpu";
        return "unknown/mixed";
    }

    internal object Summary()
    {
        if (Count == 0) return new { samples = 0, status = "NOT_RUN" };
        var intervals = new double[Count];
        double sum = 0;
        long late15 = 0, late2 = 0, budgetSamples = 0;
        for (int i = 0; i < Count; i++)
        {
            var s = _samples[i]; intervals[i] = s.IntervalMs; sum += s.IntervalMs;
            // Display/Unlimited have no known numeric budget. Never invent panel Hz.
            if (s.Cap > 0)
            {
                double budget = 1000.0 / s.Cap; budgetSamples++;
                if (s.IntervalMs > 1.5 * budget) late15++;
                if (s.IntervalMs > 2 * budget) late2++;
            }
        }
        Array.Sort(intervals);
        double P(double percentile) => intervals[Math.Clamp((int)Math.Ceiling(Count * percentile / 100) - 1, 0, Count - 1)];
        int tailCount = Math.Max(1, (int)Math.Ceiling(Count * .01));
        double tail = 0;
        for (int i = Count - tailCount; i < Count; i++) tail += intervals[i];
        return new
        {
            samples = Count, meanCompletedFrameFps = 1000.0 * Count / sum,
            frameIntervalP50Ms = P(50), frameIntervalP95Ms = P(95),
            frameIntervalP99Ms = P(99), frameIntervalP999Ms = Count >= 1000 ? (double?)P(99.9) : null,
            onePercentLowEstimateFps = 1000.0 / P(99),
            trueOnePercentLowFps = 1000.0 * tailCount / tail,
            trueOnePercentLowMethod = "1000 / mean(slowest ceil(samples * 0.01) intervals in ms)",
            tailSamples = tailCount, numericBudgetSamples = budgetSamples,
            lateOver1Point5Budget = late15, lateOver2Budget = late2,
            lateOver1Point5Fraction = budgetSamples > 0 ? (double?)late15 / budgetSamples : null,
            lateOver2Fraction = budgetSamples > 0 ? (double?)late2 / budgetSamples : null,
            qualification = "Diagnostic only; long-duration, uncontended native controls required"
        };
    }

    // Caller must stop recording before this method. Never called from Add or EndPresent.
    internal void Write(string path, object? context, long frequency, string reason)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var json = new Utf8JsonWriter(stream);
        json.WriteStartObject(); json.WriteNumber("schema", 2);
        json.WriteString("stream", "glframe-capture");
        json.WriteString("intervalMeaning", "application Present return to Present return; not physical scanout");
        json.WriteString("inputProxyMeaning", "software sample-call entry to Present return; no photon or Reflex claim");
        json.WriteString("gapMeaning", "interval minus current-frame simulation/render/swap; includes scheduler and uninstrumented work, not proven pacing wait");
        json.WriteString("scopeMeaning", "CPU passes inclusive and nested; do not sum");
        json.WriteString("stopReason", reason); json.WriteNumber("timestampFrequency", frequency);
        json.WriteNumber("capacity", _samples.Length); json.WriteNumber("droppedRecords", Dropped);
        json.WriteNumber("cancelledFrames", Cancelled);
        json.WriteString("overheadBudget", "bounded storage; no per-sample allocation/serialization/I/O; end-to-end overhead UNVERIFIED");
        json.WritePropertyName("context"); JsonSerializer.Serialize(json, context);
        json.WritePropertyName("summary"); JsonSerializer.Serialize(json, Summary());
        json.WriteStartArray("frames");
        for (int i = 0; i < Count; i++) JsonSerializer.Serialize(json, _samples[i]);
        json.WriteEndArray(); json.WriteEndObject();
    }
}
