using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MphRead.Mods.Render;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
string directory = Path.Combine(Path.GetTempPath(), "frame-profiler-check-" + Guid.NewGuid().ToString("N"));
string path = Path.Combine(directory, "capture.json");
Environment.SetEnvironmentVariable("PROJECT_PRIME_GL_FRAME_CAPTURE", path);
Environment.SetEnvironmentVariable("PROJECT_PRIME_GL_FRAME_CAPACITY", "8");
try
{
    var scene = new MphRead.Scene();
    static void Present(MphRead.Scene scene)
    {
        OpenGlFrameProfiler.BeginFrame(scene, true);
        OpenGlFrameProfiler.BeginRender(scene, "Off");
        OpenGlFrameProfiler.EndRender();
        OpenGlFrameProfiler.BeginPresent();
        Thread.SpinWait(1000);
        OpenGlFrameProfiler.EndPresent();
    }
    Present(scene); // First return establishes the boundary, without a sample.
    var prior = typeof(OpenGlFrameProfiler).GetField("_previousPresentEnd", BindingFlags.NonPublic | BindingFlags.Static)!;
    // Inject only the clock boundary so this exercises the actual profiler
    // state machine deterministically, without sleeping or a native renderer.
    prior.SetValue(null, Stopwatch.GetTimestamp() - Stopwatch.Frequency * 3 / 4);
    Present(scene);
    OpenGlFrameProfiler.CancelFrame(); // A known lifecycle interruption segments.
    Present(scene); // Must establish a fresh boundary instead of crossing cancel.
    Present(scene);
    OpenGlFrameProfiler.StopCapture("test");
    using var document = JsonDocument.Parse(File.ReadAllBytes(path));
    var frames = document.RootElement.GetProperty("frames");
    Check(frames.GetArrayLength() == 2, "long hitch retained and cancellation boundary excluded");
    Check(frames[0].GetProperty("IntervalMs").GetDouble() >= 700, "continuous >500ms hitch was lost");
    Check(frames[1].GetProperty("Segment").GetInt64() > frames[0].GetProperty("Segment").GetInt64(), "explicit cancellation did not segment capture");
    Check(document.RootElement.GetProperty("droppedRecords").GetInt64() == 0, "unexpected drops");
    Console.WriteLine("PASS: source-linked profiler retains >500ms continuous hitch and segments explicit cancellation.");
}
finally
{
    if (Directory.Exists(directory)) Directory.Delete(directory, true);
}
