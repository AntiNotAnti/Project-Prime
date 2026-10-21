using System.Text.Json;
using MphRead.Mods.Render;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void Near(double actual, double expected, string message)
    => Check(Math.Abs(actual - expected) <= Math.Max(1e-10, Math.Abs(expected) * 1e-12), $"{message}: {actual} != {expected}");
static JsonElement Summary(OpenGlFrameCapture capture) => JsonSerializer.SerializeToElement(capture.Summary());
static OpenGlFrameSample Sample(double interval, long timestamp = 0, int cap = 0)
    => new(timestamp, 0, 0, 0, interval, 0, 0, 0, 0, 0, 0, null,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, cap, "off", "unknown/mixed");

var empty = new OpenGlFrameCapture(1);
Check(Summary(empty).GetProperty("status").GetString() == "NOT_RUN", "empty capture status");
Check(Summary(empty).GetProperty("samples").GetInt32() == 0, "empty capture count");
foreach (int capacity in new[] { 0, -1, OpenGlFrameCapture.MaximumCapacity + 1 })
{
    try { _ = new OpenGlFrameCapture(capacity); throw new Exception("invalid capacity accepted"); }
    catch (ArgumentOutOfRangeException) { }
}

var partial = new OpenGlFrameCapture(10);
partial.Add(Sample(10));
var one = Summary(partial);
Near(one.GetProperty("trueOnePercentLowFps").GetDouble(), 100, "single-sample low");
Check(one.GetProperty("frameIntervalP999Ms").ValueKind == JsonValueKind.Null, "partial p999 unavailable");
Check(one.GetProperty("numericBudgetSamples").GetInt64() == 0, "unknown cap excludes budget");
Check(one.GetProperty("lateOver2Fraction").ValueKind == JsonValueKind.Null, "unknown budget fraction");

var tail = new OpenGlFrameCapture(1000);
for (int i = 1; i <= 1000; i++) tail.Add(Sample(i));
var stats = Summary(tail);
Near(stats.GetProperty("frameIntervalP50Ms").GetDouble(), 500, "nearest-rank p50");
Near(stats.GetProperty("frameIntervalP95Ms").GetDouble(), 950, "nearest-rank p95");
Near(stats.GetProperty("frameIntervalP99Ms").GetDouble(), 990, "nearest-rank p99");
Near(stats.GetProperty("frameIntervalP999Ms").GetDouble(), 999, "nearest-rank p999");
Near(stats.GetProperty("onePercentLowEstimateFps").GetDouble(), 1000.0 / 990, "legacy reciprocal-p99 estimate");
Near(stats.GetProperty("trueOnePercentLowFps").GetDouble(), 1000.0 / 995.5, "true slowest-one-percent mean");
Near(stats.GetProperty("meanCompletedFrameFps").GetDouble(), 1000.0 / 500.5, "mean completed fps");
Check(stats.GetProperty("tailSamples").GetInt32() == 10, "tail count");
var below = new OpenGlFrameCapture(999);
for (int i = 0; i < 999; i++) below.Add(Sample(10));
Check(Summary(below).GetProperty("frameIntervalP999Ms").ValueKind == JsonValueKind.Null, "999 samples cannot qualify p999");

var budget = new OpenGlFrameCapture(4);
foreach (var sample in new[] { Sample(15, cap: 100), Sample(20, cap: 100), Sample(21, cap: 100), Sample(100, cap: 0) }) budget.Add(sample);
var b = Summary(budget);
Check(b.GetProperty("numericBudgetSamples").GetInt64() == 3, "numeric caps only");
Check(b.GetProperty("lateOver1Point5Budget").GetInt64() == 2, "strict 1.5 budget boundary");
Check(b.GetProperty("lateOver2Budget").GetInt64() == 1, "strict 2 budget boundary");
Near(b.GetProperty("lateOver2Fraction").GetDouble(), 1.0 / 3, "budget denominator");

Near(OpenGlFrameCapture.ElapsedMs(10, 20, 1000), 10, "timer units");
Near(OpenGlFrameCapture.ElapsedMs(10, 10, 1000), 0, "zero duration");
foreach (var t in new[] { (-1L, 10L, 1000L), (20L, 10L, 1000L), (0L, 10L, 0L), (0L, 10L, -1L), (long.MinValue, long.MaxValue, 1L) })
    Check(double.IsNaN(OpenGlFrameCapture.ElapsedMs(t.Item1, t.Item2, t.Item3)), "invalid timer must be NaN");
Near(OpenGlFrameCapture.ElapsedMs(0, long.MaxValue, 1), (double)long.MaxValue * 1000, "timer multiplication cannot overflow integer");

Check(OpenGlFrameCapture.Attribute(10, 8, 0, 0, 0, 0, true) == "unknown/mixed", "GC coincidence is not attribution");
Check(OpenGlFrameCapture.Attribute(10, 0, 9, 7, 0, 0, false) == "ui", "inclusive UI attributed before parent");
Check(OpenGlFrameCapture.Attribute(10, 0, 9, 0, 0, 7, false) == "texture-upload-cpu", "inclusive upload attributed before parent");
Check(OpenGlFrameCapture.Attribute(10, 0, 7, 3, 0, 0, false) == "unknown/mixed", "subtract UI before GL dominance");
Check(OpenGlFrameCapture.Attribute(10, 0, 7, 0, 0, 0, false) == "gl-cpu", "dominant GL scope");
Check(OpenGlFrameCapture.Attribute(10, 0, 0, 0, 7, 0, false) == "sync/present", "dominant swap");
Check(OpenGlFrameCapture.Attribute(0, 0, 0, 0, 0, 0, false) == "unknown/mixed", "invalid interval attribution");

var bounded = new OpenGlFrameCapture(2);
bounded.Add(Sample(10, 100)); bounded.Cancel(); bounded.Add(Sample(20, 200)); bounded.Add(Sample(30, 300)); bounded.Cancel();
Check(bounded.Count == 2 && bounded.Dropped == 1 && bounded.Cancelled == 2 && bounded.Segment == 2, "bounded storage and cancellation accounting");
for (int i = 0; i < 7; i++) bounded.Boundary(i, "test-boundary", 0, 0);
Check(bounded.DroppedBoundaryRecords == 3, "bounded lifecycle records count overflow");
string directory = Path.Combine(Path.GetTempPath(), "frame-capture-check-" + Guid.NewGuid().ToString("N"));
string path = Path.Combine(directory, "capture.json");
try
{
    bounded.Write(path, new { scenario = "synthetic" }, 1000, "test");
    byte[] original = File.ReadAllBytes(path);
    using var document = JsonDocument.Parse(original);
    var root = document.RootElement;
    Check(root.GetProperty("schema").GetInt32() == 3, "schema version");
    Check(root.GetProperty("droppedBoundaryRecords").GetInt64() == 3 && root.GetProperty("boundaries").GetArrayLength() == 4, "boundary overflow is explicit in export");
    Check(root.GetProperty("droppedRecords").GetInt64() == 1 && root.GetProperty("cancelledFrames").GetInt64() == 2, "export accounting");
    var frames = root.GetProperty("frames");
    Check(frames.GetArrayLength() == 2 && frames[0].GetProperty("PresentTimestamp").GetInt64() == 100 && frames[1].GetProperty("PresentTimestamp").GetInt64() == 200, "chronological retained records");
    Check(root.GetProperty("summary").TryGetProperty("onePercentLowEstimateFps", out _), "legacy metric exported");
    try { bounded.Write(path, null, 1000, "overwrite"); throw new Exception("existing export overwritten"); }
    catch (IOException) { }
    Check(original.SequenceEqual(File.ReadAllBytes(path)), "existing export preserved");
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

// Isolate value-storage costs after JIT warmup. This does not measure the renderer.
static long Allocations(OpenGlFrameCapture? capture, in OpenGlFrameSample sample, int count)
{
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < count; i++) capture?.Add(in sample);
    return GC.GetAllocatedBytesForCurrentThread() - before;
}
var value = Sample(10);
var warm = new OpenGlFrameCapture(10000);
for (int i = 0; i < 10; i++) { Allocations(warm, in value, 1000); Allocations(null, in value, 1000); }
var storage = new OpenGlFrameCapture(10000);
Check(Allocations(null, in value, 10000) == 0, "disabled Add path allocates");
Check(Allocations(storage, in value, 10000) == 0, "storage Add path allocates");
Check(Allocations(storage, in value, 10000) == 0 && storage.Dropped == 10000, "full storage drop path allocates");
Console.WriteLine("PASS: frame capture arithmetic, bounds, attribution, export, and warmed Add allocation checks (synthetic; no native benchmark).");
