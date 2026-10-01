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
    Check(DesktopFramePacing.UseDisplayPacing(cap, hz),
        $"native {cap} cap uses the {hz:0.##} Hz display clock");
}
Check(DesktopFramePacing.UseDisplayPacing(0, 144), "Display always uses display pacing");
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
    "numeric caps never trigger the Linux display fallback");

Check(DesktopFramePacing.SoftwareFrequency(120, 144,
        displayPaced: false, modernPresentationBlocks: false,
        linuxVSyncFallback: false) == 120,
    "non-native cap uses software pacing when modern present is nonblocking");
Check(DesktopFramePacing.SoftwareFrequency(120, 144,
        displayPaced: false, modernPresentationBlocks: true,
        linuxVSyncFallback: false) == 0,
    "FIFO fallback never stacks a software cap on blocking presentation");
Check(DesktopFramePacing.SoftwareFrequency(144, 144,
        displayPaced: true, modernPresentationBlocks: true,
        linuxVSyncFallback: false) == 0,
    "native cap has exactly one display pacing clock");
Check(Math.Abs(DesktopFramePacing.SoftwareFrequency(0, 144,
        displayPaced: false, modernPresentationBlocks: false,
        linuxVSyncFallback: true) - 144) < 0.001,
    "Linux ignored-VSync fallback paces at monitor refresh");

return failures == 0 ? 0 : 1;
