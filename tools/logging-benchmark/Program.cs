using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using MphRead.Mods;

// Measures the actual production Line path in this isolated benchmark process.
// Injecting the sink avoids attaching native stderr or changing user preferences.
var sink = typeof(DebugLog).GetField("_writer", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("DebugLog sink changed; update the benchmark.");
if (sink.GetValue(null) != null) throw new InvalidOperationException("Benchmark must start without persistent logging.");
string directory = Path.Combine(Path.GetTempPath(), "prime-log-benchmark-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    object Measure(string storage, int linesPerSample, int flushDelayMs)
    {
        string path = Path.Combine(directory, storage + "-" + linesPerSample + ".log");
        using var stream = storage == "memory-ring" ? null : new FlushDelayStream(
            new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), flushDelayMs);
        using var writer = stream == null ? null : new StreamWriter(stream, Encoding.UTF8) { AutoFlush = false };
        sink.SetValue(null, writer);
        for (int i = 0; i < 320; i++) DebugLog.Line("benchmark", "warmup");
        var samples = new double[300];
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int sample = 0; sample < samples.Length; sample++)
        {
            long started = Stopwatch.GetTimestamp();
            for (int line = 0; line < linesPerSample; line++)
                DebugLog.Line("benchmark", "representative diagnostic event: slot=3 room=MP1 SANCTORUS frame=123456 accepted=true");
            samples[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        writer?.Flush();
        sink.SetValue(null, null);
        Array.Sort(samples);
        return new { storage, linesPerSample, samples = samples.Length, flushDelayMs,
            p50Ms = samples[149], p95Ms = samples[284], p99Ms = samples[296], maximumMs = samples[^1],
            allocatedBytesPerLine = bytes / (300L * linesPerSample) };
    }
    var results = new[] { 1, 10, 100 }.SelectMany(lines => new[] {
        Measure("memory-ring", lines, 0), Measure("local-file", lines, 0),
        Measure("modeled-slow-flush", lines, 5) }).ToArray();
    Console.WriteLine("LOGBENCH " + JsonSerializer.Serialize(new {
        timingContract = "Synchronous production DebugLog.Line including timestamp, global lock, ring and existing 32-line flush policy. Local file uses warm OS cache. The 5 ms flush delay is an artificial sensitivity model, not a measured slow disk or gameplay frame.",
        platform = Environment.OSVersion.ToString(), results }));
}
finally { sink.SetValue(null, null); Directory.Delete(directory, recursive: true); }

sealed class FlushDelayStream(Stream inner, int delayMs) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
    public override void Flush() { if (delayMs != 0) Thread.Sleep(delayMs); inner.Flush(); }
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
