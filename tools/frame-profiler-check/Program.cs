using System.Diagnostics;
using System.Text.Json;
using MphRead.Mods.Render;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
string directory = Path.Combine(Path.GetTempPath(), "frame-profiler-check-" + Guid.NewGuid().ToString("N"));
string path = Path.Combine(directory, "capture.json");
Environment.SetEnvironmentVariable("PROJECT_PRIME_GL_FRAME_CAPTURE", path);
Environment.SetEnvironmentVariable("PROJECT_PRIME_GL_FRAME_CAPACITY", "32");
try
{
    var scene = new MphRead.Scene();
    FrameTiming.FrameRateCap = 60;
    static void Present(MphRead.Scene scene, double elapsed = 1d / 60, bool focused = true)
    {
        OpenGlFrameProfiler.BeginCallback();
        OpenGlFrameProfiler.BeginFrame(scene, focused);
        int steps = FrameTiming.Advance(elapsed);
        OpenGlFrameProfiler.BeginSimulation(steps);
        OpenGlFrameProfiler.EndSimulation();
        OpenGlFrameProfiler.BeginRender(scene, "Off");
        OpenGlFrameProfiler.EndRender();
        OpenGlFrameProfiler.BeginPresent();
        Thread.SpinWait(1000);
        OpenGlFrameProfiler.EndPresent();
        OpenGlFrameProfiler.EndCallback();
    }
    OpenGlFrameProfiler.BeginCallback();
    OpenGlFrameProfiler.EndCallback(); // A launcher callback has no scene BeginFrame.
    scene.FrameCount = 0;
    Present(scene); Present(scene); // Gameplay warmup is not launcher work.
    scene.FrameCount = 240;
    Present(scene); // First return establishes the boundary, without a sample.
    Thread.Sleep(750);
    Present(scene);
    // Use the production accumulator, including the actual generation fences.
    Thread.Sleep(200);
    Present(scene, .2);
    Present(scene);
    Thread.Sleep(500);
    Present(scene, .5);
    Present(scene);
    OpenGlFrameProfiler.CancelFrame(); // A known lifecycle interruption segments.
    Present(scene); // Must establish a fresh boundary instead of crossing cancel.
    Present(scene);
    Present(scene, focused: false);
    Present(scene);
    Present(scene);
    FrameTiming.FrameRateCap = 144;
    Present(scene);
    Present(scene);
    var secondScene = new MphRead.Scene();
    Present(secondScene);
    Present(secondScene);
    Present(secondScene);
    OpenGlFrameProfiler.BeginFrame(secondScene, true, paused: true);
    OpenGlFrameProfiler.BeginFrame(secondScene, true);
    OpenGlFrameProfiler.BeginRender(secondScene);
    OpenGlFrameProfiler.EndRender();
    OpenGlFrameProfiler.BeginPresent();
    OpenGlFrameProfiler.PresentFailed();
    OpenGlFrameProfiler.StopCapture("test");
    using var document = JsonDocument.Parse(File.ReadAllBytes(path));
    var frames = document.RootElement.GetProperty("frames");
    Check(frames.GetArrayLength() == 9, "timing triggers/following intervals retained, lifecycle crossings excluded");
    Check(frames[0].GetProperty("IntervalMs").GetDouble() >= 700, "continuous >500ms hitch was lost");
    Check(frames[1].GetProperty("BoundaryReason").GetString() == "catch-up-overflow", "catch-up trigger missing");
    Check(frames[1].GetProperty("DroppedStepsThisFrame").GetInt64() > 0, "drop delta missing");
    Check(frames[3].GetProperty("BoundaryReason").GetString() == "timing-stall", "stall trigger missing");
    Check(frames[3].GetProperty("StallsThisFrame").GetInt64() == 1, "stall delta missing");
    Check(frames[1].GetProperty("PresentationGenerationBefore").GetInt64() < frames[1].GetProperty("PresentationGeneration").GetInt64(), "generation fence missing");
    Check(frames[5].GetProperty("Segment").GetInt64() > frames[0].GetProperty("Segment").GetInt64(), "explicit cancellation did not segment capture");
    Check(document.RootElement.GetProperty("droppedRecords").GetInt64() == 0, "unexpected drops");
    Check(document.RootElement.GetProperty("boundaries").GetArrayLength() >= 4, "initial/final/lifecycle boundaries missing");
    Check(document.RootElement.GetProperty("launcherCallbacks").GetInt64() == 1, "gameplay warmup/focus frames counted as launcher work");
    var reasons = document.RootElement.GetProperty("boundaries").EnumerateArray().Select(row => row.GetProperty("Reason").GetString()).ToArray();
    foreach (string reason in new[] { "focus-transition", "pause-transition", "cap-transition", "scene-transition", "present-failed", "first-present", "capture-stop:test" })
        Check(reasons.Contains(reason), "missing lifecycle boundary: " + reason);
    Check(frames[0].GetProperty("CallbackMs").GetDouble() >= frames[0].GetProperty("CallbackToPresentMs").GetDouble(), "full callback scope excludes present or post-present work");
    Console.WriteLine("PASS: production timing path retains catch-up overflow, stall, following intervals, long hitch and explicit boundaries.");
}
finally
{
    if (Directory.Exists(directory)) Directory.Delete(directory, true);
}
