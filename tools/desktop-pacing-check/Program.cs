using MphRead.Mods.Render;

int failures = 0;
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

return failures == 0 ? 0 : 1;
