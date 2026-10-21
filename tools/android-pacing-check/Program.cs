using MphRead.Droid;

int failures = 0;
void Check(bool result, string name)
{
    Console.WriteLine($"ANDROIDPACING {(result ? "PASS" : "FAIL")} {name}");
    if (!result) failures++;
}

foreach (double hz in new[] { 59.94, 60, 90, 120, 144 })
{
    var pacer = new AndroidFramePacer(500);
    pacer.Reset(0);
    double now = 0, addedWait = 0, elapsed = 0;
    for (int i = 0; i < 600; i++)
    {
        double deadline = pacer.Deadline(now, 0, presentationPaced: true);
        double wait = Math.Max(0, deadline - now);
        now += wait;
        addedWait += wait;
        elapsed += pacer.BeginFrame(now);
        now += 1 / hz; // Work plus blocking presentation already occupies one refresh.
    }
    Check(addedWait < 1e-9 && Math.Abs(elapsed - 599 / hz) < 1e-8,
        $"display {hz}Hz adds no second wait and preserves elapsed time");
}
{
    var pacer = new AndroidFramePacer(500);
    pacer.Reset(0);
    pacer.Deadline(0, 0, presentationPaced: true);
    pacer.BeginFrame(0);
    // A presentation queue can free a buffer between display boundaries.
    // The former 60Hz software timer added ~8.67ms here on top of VSync.
    Check(pacer.Deadline(.008, 0, presentationPaced: true) <= .008,
        "early presentation return does not wait for an unrelated 60Hz software phase");
}
foreach (int cap in new[] { 60, 90, 120, 144 })
{
    var pacer = new AndroidFramePacer(500);
    pacer.Reset(0);
    double now = 0, addedWait = 0, elapsed = 0;
    for (int i = 0; i < 600; i++)
    {
        double deadline = pacer.Deadline(now, cap, presentationPaced: true);
        double wait = Math.Max(0, deadline - now);
        now += wait;
        addedWait += wait;
        elapsed += pacer.BeginFrame(now);
        now += 1.0 / cap; // compositor/native refresh owns the cadence
    }
    Check(addedWait < 1e-9 && Math.Abs(elapsed - 599.0 / cap) < 1e-8,
        $"native explicit {cap} cap adds no second software wait");
}
{
    var pacer = new AndroidFramePacer(500);
    pacer.Reset(0);
    pacer.Deadline(0, 120, presentationPaced: true);
    pacer.BeginFrame(0);
    Check(pacer.Deadline(.008, 120, presentationPaced: true) <= .008,
        "native 120Hz cap follows compositor timing rather than an 8.33ms sleep phase");
}

foreach (int cap in new[] { 30, 60, 90, 120, 144, 500 })
{
    var pacer = new AndroidFramePacer(500);
    pacer.Reset(0);
    double now = 0, elapsed = 0;
    for (int i = 0; i < 600; i++)
    {
        now = Math.Max(now, pacer.Deadline(now, cap));
        elapsed += pacer.BeginFrame(now);
        now += 0.001;
    }
    Check(Math.Abs(elapsed - 599.0 / cap) < 1e-8, $"explicit {cap} cap retains its deadline cadence");
}
{
    var pacer = new AndroidFramePacer(500);
    pacer.Reset(0);
    Check(pacer.Deadline(0, -1) == 0, "Unlimited starts without a software deadline");
    pacer.BeginFrame(0);
    Check(pacer.Deadline(0.0001, -1) <= 0.0001,
        "Unlimited does not inherit the old 500Hz safety ceiling");
    pacer.Deadline(0, 0, displayRefreshRate: 120);
    pacer.BeginFrame(0);
    Check(Math.Abs(pacer.Deadline(0.0001, 0, displayRefreshRate: 120) - 1.0 / 120) < 1e-9,
        "Display with failed swap interval enforces observed refresh fallback");
    pacer.Reset(0);
    pacer.Deadline(0, 0);
    pacer.BeginFrame(0);
    Check(Math.Abs(pacer.Deadline(0.0001, 0) - 1.0 / 60) < 1e-9,
        "Display with unknown refresh uses conservative 60Hz fallback");
    pacer.Deadline(0.01, 30);
    pacer.BeginFrame(0.01);
    Check(pacer.Deadline(0.015, 120) == 0.015, "cap changes discard the old render deadline");
    pacer.BeginFrame(0.015);
    pacer.Deadline(1, 120);
    double elapsed = pacer.BeginFrame(1);
    Check(Math.Abs(elapsed - .985) < 1e-9 && pacer.Deadline(1, 120) > 1,
        "stall preserves simulation elapsed but creates no render catch-up burst");
    pacer.Reset(100);
    pacer.Deadline(100, 60);
    Check(pacer.BeginFrame(100) == 0, "resume excludes background time");
}
Check(!AndroidFramePacer.PresentationOwnsCadence(120, double.NaN, 1),
    "unknown active refresh retains numeric cap enforcement");
Check(!AndroidFramePacer.PresentationOwnsCadence(120, 144, 1),
    "ignored 120Hz request on active 144Hz panel retains software cap");
Check(AndroidFramePacer.PresentationOwnsCadence(120, 120, 1),
    "observed 120Hz with successfully applied interval owns cadence");
Check(!AndroidFramePacer.PresentationOwnsCadence(120, 120, -1),
    "failed interval application cannot claim presentation ownership");
Check(!AndroidFramePacer.PresentationOwnsCadence(120, 144, 0),
    "active refresh change at unchanged cap restores managed deadline");
Check(AndroidFramePacer.PresentationOwnsCadence(120, 60, 1),
    "slower active display already enforces the selected upper bound");
Check(!AndroidFramePacer.PresentationOwnsCadence(-1, 120, 1),
    "Unlimited has no managed numeric cap");
Check(!AndroidFramePacer.PresentationOwnsCadence(0, 120, -1),
    "Display cannot assume failed swap interval succeeded");
{
    var request = new AndroidPresentationRequest();
    Check(request.ShouldAttempt(-1, 120, 0), "first Unlimited requests surface rate");
    request.Complete(-1, 120, 0, false);
    Check(!request.ShouldAttempt(-1, 120, 49) && request.ShouldAttempt(-1, 120, 50),
        "failed request retries after bounded backoff");
    request.Complete(-1, 120, 50, true);
    Check(!request.ShouldAttempt(-1, 120, 100), "successful request avoids redundant Binder calls");
    Check(request.ShouldAttempt(-1, 144, 100), "display mode change invalidates rate request");
    request.Reset();
    Check(request.ShouldAttempt(-1, 120, 100), "replacement surface resends Unlimited request");
    Check(AndroidPresentationRequest.RetryDelayMs(999) == 1600, "surface retry delay is bounded");
}

{
    var completed = new AndroidPresentedFrameClock(1000);
    Check(!completed.TryObserve(100, out _), "first completion establishes anchor instead of a zero sample");
    // Prior work took 2ms; this frame takes 47ms plus 3ms presentation. Its
    // interval must contain this 50ms work, not the preceding 2ms start delta.
    Check(completed.TryObserve(150, out double current) && Math.Abs(current - .050) < 1e-9,
        "long current frame pairs with its completion interval, not previous short work");
    Check(completed.TryObserve(152, out double next) && Math.Abs(next - .002) < 1e-9,
        "following short frame is not charged the preceding long stage");
    completed.Reset();
    Check(!completed.TryObserve(10000, out _)
        && completed.TryObserve(10003, out double resumed) && Math.Abs(resumed - .003) < 1e-9,
        "surface rebase starts a new completion segment without background interval");
}

Check(AndroidGcTelemetry.ParseCounter("98") == 98, "ART GC numeric runtime stat");
Check(AndroidGcTelemetry.ParseCounter("missing") == null, "missing ART GC runtime stat");
Check(AndroidGcTelemetry.CounterDelta(11, 7) == "4", "ART GC counter delta");
Check(AndroidGcTelemetry.CounterDelta(null, 7) == "n/a", "unavailable ART GC delta");
Check(AndroidGcTelemetry.CounterDelta(1, 5) == "n/a", "reset ART GC counter does not produce negative delta");

{
    var uploads = new MphRead.Mods.Render.ModernTexturePromotionPacer();
    Check(uploads.BeginFrame(), "no Android HD promotion latency at baseline");
    uploads.RecordPromotion(1.1);
    Check(uploads.BeginFrame(), "small HD promotion does not delay next frame");
    uploads.RecordPromotion(7);
    Check(!uploads.BeginFrame() && !uploads.BeginFrame() && !uploads.BeginFrame()
        && uploads.BeginFrame(), "7ms upload spaces later work by three frames");
    uploads.RecordPromotion(double.PositiveInfinity);
    Check(uploads.FramesToSkip == 0 && uploads.BeginFrame(), "invalid GPU duration never starves texture queue");
    uploads.RecordPromotion(9999);
    Check(uploads.FramesToSkip == 6, "excessive stalls have bounded recovery delay");
}

{
    string a = MphRead.Mods.Render.ShaderProgramCacheKey.Compute("vertex", "fragment", "Adreno X");
    Check(a == MphRead.Mods.Render.ShaderProgramCacheKey.Compute("vertex", "fragment", "Adreno X"),
        "shader key is stable for same sources and driver");
    Check(a != MphRead.Mods.Render.ShaderProgramCacheKey.Compute("vertex changed", "fragment", "Adreno X")
        && a != MphRead.Mods.Render.ShaderProgramCacheKey.Compute("vertex", "fragment", "Mali Y"),
        "shader cache invalidates for source or driver changes");
    Check(a != MphRead.Mods.Render.ShaderProgramCacheKey.Compute("vertex", "fragment", "Adreno X", "changed-link-ABI")
        && a != MphRead.Mods.Render.ShaderProgramCacheKey.Compute("vertex", "fragment", "Adreno X", translator: "changed-translator"),
        "shader identity includes link ABI and translator schema");
    Check(MphRead.Mods.Render.ShaderProgramCacheKey.AcceptBinaryLength(1024)
        && !MphRead.Mods.Render.ShaderProgramCacheKey.AcceptBinaryLength(0)
        && !MphRead.Mods.Render.ShaderProgramCacheKey.AcceptBinaryLength(int.MaxValue),
        "cached shader binaries are strictly size bounded");
}

{
    Check(MphRead.Shaders.WithoutDepthVertexSpecialization(
        MphRead.Shaders.VertexShader.Replace("\r\n", "\n").Replace("\n", "\r\n")) == MphRead.Shaders.LegacyVertexShader,
        "Windows CRLF source transforms match canonical LF shader variants");
    string desktop = MphRead.Shaders.VertexShader;
    string prepared = MphRead.Mods.Render.EsShaders.PrepareSource(desktop);
    string fragment = MphRead.Mods.Render.EsShaders.PrepareSource(MphRead.Shaders.FragmentShader);
    string receiver = MphRead.Mods.Render.EsShaders.PrepareSource(MphRead.Shaders.ShadowFragmentShader);
    Check(receiver.StartsWith("#version 300 es") && receiver.Contains("receive_world_shadow")
        && receiver.Contains("texture(world_shadow_tex") && !receiver.Contains("texture2D(world_shadow_tex"),
        "optional shadow receiver uses prepared GLES sampler/source");
    Check(!MphRead.Shaders.FragmentShader.Contains("receive_world_shadow")
        && receiver.IndexOf("col.rgb *= world_shadow_visibility();\n    if (use_emissive_map)", StringComparison.Ordinal) >= 0,
        "native fallback source is independent and shadow precedes authored emissive addition");
    string key = MphRead.Mods.Render.ShaderProgramCacheKey.Compute(prepared, fragment, "driver");
    Check(prepared.StartsWith("#version 300 es") && prepared != desktop,
        "binary identity prepares the production translated GLES source");
    Check(key != MphRead.Mods.Render.ShaderProgramCacheKey.Compute(prepared + "\n// GLES-only fix", fragment, "driver"),
        "GLES-only change invalidates binary with unchanged desktop input");
    Check(key != MphRead.Mods.Render.ShaderProgramCacheKey.Compute(
        MphRead.Mods.Render.EsShaders.PrepareSource(MphRead.Shaders.LegacyVertexShader), fragment, "driver"),
        "translated depth variants retain distinct binary identities");
    Check(MphRead.Mods.Render.EsShaders.PrepareSource(prepared) == prepared,
        "prepared source remains unchanged at compiler boundary");
}

Check(AndroidAudioRatePolicy.Select(32728, 48000, true) == 48000,
    "native Android sample rate experiment prefers valid device rate");
Check(AndroidAudioRatePolicy.Select(32728, 48000, false) == 32728,
    "disabled native rate experiment preserves original 32728 Hz");
Check(AndroidAudioRatePolicy.Select(32728, 0, true) == 32728
    && AndroidAudioRatePolicy.Select(32728, 999999, true) == 32728,
    "invalid device properties preserve original playback rate");
Check(AndroidAudioRatePolicy.BufferPeriodMs(192, 48000) == 4,
    "192-frame device burst at 48 kHz is 4ms");

Check(MphRead.Mods.Render.GpuCompressedTextureMath.LevelBytes(1, 1) == 16
    && MphRead.Mods.Render.GpuCompressedTextureMath.LevelBytes(4, 4) == 16
    && MphRead.Mods.Render.GpuCompressedTextureMath.LevelBytes(5, 4) == 32
    && MphRead.Mods.Render.GpuCompressedTextureMath.LevelBytes(7, 9) == 96,
    "ETC2/ASTC 4x4 compressed block sizes are byte-exact");
Check(MphRead.Mods.Render.GpuCompressedTextureMath.MipLevelCount(1, 1) == 1
    && MphRead.Mods.Render.GpuCompressedTextureMath.MipLevelCount(8, 4) == 4
    && MphRead.Mods.Render.GpuCompressedTextureMath.MipLevelCount(8192, 8192) == 14,
    "compressed texture mip bounds follow the largest axis");
bool invalidCompressedDimension = false;
try { MphRead.Mods.Render.GpuCompressedTextureMath.LevelBytes(0, 8); }
catch (ArgumentOutOfRangeException) { invalidCompressedDimension = true; }
Check(invalidCompressedDimension, "compressed GPU texture rejects zero dimensions");

return failures == 0 ? 0 : 1;
