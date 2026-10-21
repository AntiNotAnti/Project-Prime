using MphRead.Mods.Render;

int failures = 0;
CheckDisplaySentinel();
// Dropped or stalled simulation steps advance FrameTiming.PresentationGeneration.
// They must not reprogram VSync or OpenTK's frame deadline every draw.
Check(!DesktopFramePacing.SourceChanged(false, -1, -1, 144, 144),
    "unlimited pacing remains stable across simulation catch-up fences");
Check(!DesktopFramePacing.SourceChanged(false, 0, 0, 120, 120),
    "display pacing remains stable across simulation catch-up fences");
Check(DesktopFramePacing.SourceChanged(true, -1, -1, 144, 144),
    "focus and context transitions invalidate pacing");
Check(DesktopFramePacing.SourceChanged(false, 144, 60, 144, 144),
    "numeric cap change invalidates pacing");
Check(DesktopFramePacing.SourceChanged(false, -1, -1, 120, 144),
    "monitor refresh change invalidates pacing");
Check(!DesktopFramePacing.SourceChanged(false, -1, -1, 144.4, 144),
    "minor monitor refresh jitter does not invalidate pacing");
void CheckDisplaySentinel()
{
    if (DesktopFramePacing.DisplayRateCap != 0)
        throw new InvalidOperationException("Desktop Display/VSync must use the zero sentinel");
}
void Check(bool result, string name)
{
    Console.WriteLine($"DESKTOPPACING {(result ? "PASS" : "FAIL")} {name}");
    if (!result) failures++;
}

foreach (double hz in new[] { 59.94, 60.0, 90.0, 120.0, 144.0, 165.0, 240.0 })
{
    int cap = (int)Math.Round(hz);
    Check(!DesktopFramePacing.UseDisplayPacing(cap, hz),
        $"numeric {cap} cap never aliases {hz:0.##} Hz Display/VSync");
    Check(DesktopFramePacing.SoftwareFrequency(cap, hz,
            displayPaced: DesktopFramePacing.UseDisplayPacing(cap, hz),
            modernPresentationBlocks: false,
            displayVSyncFallback: false) == cap,
        $"numeric {cap} cap retains its software deadline at {hz:0.##} Hz");
}
Check(DesktopFramePacing.UseDisplayPacing(0, 144), "Display always uses display pacing");
Check(!DesktopFramePacing.UseDisplayPacing(-1, 144),
    "Unlimited never aliases Display/VSync");
Check(!DesktopFramePacing.UseDisplayPacing(120, 144),
    "120 cap on 144 Hz remains an explicit software-paced request");
Check(!DesktopFramePacing.UseDisplayPacing(144, 120),
    "144 cap on 120 Hz remains an explicit software-paced request");

Check(!DesktopFramePacing.LinuxVSyncIgnored(true, 0, 144, 144.8, false),
    "normal Linux compositor jitter does not trigger fallback");
Check(DesktopFramePacing.LinuxVSyncIgnored(true, 0, 144, 180, false),
    "Linux display mode detects ignored swap interval");
Check(DesktopFramePacing.LinuxVSyncIgnored(true, 0, 144, 144, true),
    "Linux ignored-VSync fallback stays latched for the monitor");
Check(!DesktopFramePacing.LinuxVSyncIgnored(false, 0, 144, 300, false),
    "ignored-VSync fallback is Linux-only");
Check(!DesktopFramePacing.LinuxVSyncIgnored(true, 120, 144, 300, false),
    "non-native numeric caps never trigger the Linux display fallback");
Check(!DesktopFramePacing.LinuxVSyncIgnored(true, -1, 144, 300, false),
    "Unlimited never triggers the Linux VSync fallback");
Check(!DesktopFramePacing.LinuxVSyncIgnored(true, 144, 144, 180, false),
    "Linux native-refresh numeric caps never enter the Display/VSync fallback");

Check(!DesktopFramePacing.MacVSyncIgnored(true, 120, 120, 189, false),
    "macOS native-refresh numeric caps never enter the Display/VSync fallback");
Check(DesktopFramePacing.MacVSyncIgnored(true, 0, 120, 189, false),
    "macOS display cap detects ignored swap interval");
Check(DesktopFramePacing.MacVSyncIgnored(true, 0, 120, 120, true),
    "macOS Display/VSync fallback stays latched after software pacing recovers");
Check(!DesktopFramePacing.MacVSyncIgnored(true, 120, 120, 120, true),
    "macOS numeric cap does not inherit a prior Display/VSync fallback");
Check(!DesktopFramePacing.MacVSyncIgnored(true, 0, 120, 120.8, false),
    "normal macOS Display/VSync jitter keeps a single pacing clock");
Check(!DesktopFramePacing.MacVSyncIgnored(true, 144, 120, 189, false),
    "macOS non-native numeric caps keep their existing software pacing");
Check(!DesktopFramePacing.MacVSyncIgnored(true, -1, 120, 189, false),
    "macOS unlimited remains uncapped");
Check(!DesktopFramePacing.MacVSyncIgnored(false, 120, 120, 189, false),
    "macOS fallback does not change Windows presentation policy");

Check(DesktopFramePacing.SoftwareFrequency(120, 144,
        displayPaced: false, modernPresentationBlocks: false,
        displayVSyncFallback: false) == 120,
    "non-native cap uses software pacing when modern present is nonblocking");
Check(DesktopFramePacing.SoftwareFrequency(-1, 144,
        displayPaced: false, modernPresentationBlocks: false,
        displayVSyncFallback: false) == 0,
    "Unlimited leaves the OpenTK frame loop genuinely uncapped");
Check(DesktopFramePacing.SoftwareFrequency(120, 144,
        displayPaced: false, modernPresentationBlocks: true,
        displayVSyncFallback: false) == 0,
    "FIFO fallback never stacks a software cap on blocking presentation");
Check(DesktopFramePacing.SoftwareFrequency(144, 144,
        displayPaced: false, modernPresentationBlocks: false,
        displayVSyncFallback: false) == 144,
    "native-refresh numeric cap has its own 144 FPS software deadline");
Check(DesktopFramePacing.SoftwareFrequency(0, 144,
        displayPaced: true, modernPresentationBlocks: false,
        displayVSyncFallback: false) == 0,
    "Display/VSync mode does not double-pace with a software limiter");
Check(Math.Abs(DesktopFramePacing.SoftwareFrequency(0, 144,
        displayPaced: false, modernPresentationBlocks: false,
        displayVSyncFallback: true) - 144) < 0.001,
    "Linux/macOS ignored-VSync fallback paces Display mode at monitor refresh");

// Reproduce Unlimited → Display with an already-fast diagnostic window.
// The transition interval and every partial new window must be excluded.
var measurement = new DesktopPacingMeasurement();
void Frames(int count, double interval, long generation = 1, bool focused = true)
{
    for (int i = 0; i < count; i++)
        measurement.Observe(interval, generation, sourceChanged: false, focused);
}
measurement.Observe(1d / 300, 1, sourceChanged: true, focused: true);
Frames(601, 1d / 300);
Check(measurement.FrameHz > 299, "fast prior source has measurement evidence");
measurement.Observe(1d / 300, 1, sourceChanged: true, focused: true);
Frames(119, 1d / 60);
Check(measurement.FrameHz == 0, "cap/monitor transition excludes prior fast and partial windows");
Frames(2, 1d / 60);
Check(Math.Abs(measurement.FrameHz - 60) < .001,
    "only a full new-source window supplies Display fallback evidence");
Check(!DesktopFramePacing.MacVSyncIgnored(true, 0, 60, measurement.FrameHz, false),
    "fast previous cap cannot falsely latch Display fallback");
measurement.Observe(1d / 180, 1, sourceChanged: true, focused: true);
Frames(361, 1d / 180);
Check(DesktopFramePacing.LinuxVSyncIgnored(true, 0, 144, measurement.FrameHz, false),
    "genuinely ignored VSync still trips after clean full window");
measurement.Observe(1d / 180, 2, sourceChanged: false, focused: true);
Check(measurement.FrameHz == 0, "presentation reset discards previous measurement");
Frames(359, 1d / 180, 2);
Check(measurement.FrameHz == 0, "presentation reset requires a fresh full window");
Frames(3, 1d / 180, 2);
Check(measurement.FrameHz > 179, "measurement recovers after reset window");
measurement.Observe(1d / 180, 2, sourceChanged: false, focused: false);
Check(measurement.FrameHz == 0, "focus loss discards evidence");
Frames(359, 1d / 180, 2);
Check(measurement.FrameHz == 0, "focus recovery cannot reuse earlier intervals");
foreach (double invalid in new[] { .5, -1, double.NaN, double.PositiveInfinity })
{
    Frames(3, 1d / 180, 2);
    measurement.Observe(invalid, 2, sourceChanged: false, focused: true);
    Check(measurement.FrameHz == 0, $"invalid/stalled interval {invalid} clears fallback evidence");
}

return failures == 0 ? 0 : 1;
