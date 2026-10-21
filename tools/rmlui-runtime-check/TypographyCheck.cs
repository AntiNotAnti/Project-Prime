using MphRead.Mods.Launcher.RmlUi.Host;

internal static class TypographyCheck
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        var bridge = new FakeBridge();
        using var host = new RmlUiHost(bridge);
        Check(!host.SetTypography(-1, -1, -1), "inactive typography cannot call native");
        host.Initialize(640, 360, 1, ".");
        Check(host.SetTypography(4, 12, 33) && bridge.TypographyCalls == 1,
            "three independent font roles reach native owner");
        for (int frame = 0; frame < 1000; frame++) host.SetTypography(4, 12, 33);
        Check(bridge.TypographyCalls == 1, "unchanged font selection does not rebuild per frame");
        Check(host.SetTypography(-1, -1, -1) && bridge.TypographyCalls == 2,
            "disabling role overrides reaches authored typeface restoration");
        Check(Task.Run(() => { try { host.SetTypography(1, 2, 3); return false; }
            catch (InvalidOperationException) { return true; } }).Result, "font publication retains owner thread guard");
        foreach (int invalid in new[] { -2, 34 })
            Check(Reject(() => host.SetTypography(invalid, 0, 0)), "invalid font selection rejected before native");
        host.Reinitialize(); host.SetTypography(-1, -1, -1);
        Check(bridge.TypographyCalls == 3, "new native lifetime republishes even unchanged font selection");
        bridge.TypographyMissing = true;
        Check(!host.SetTypography(1, 2, 3) && !host.SetTypography(2, 3, 4) && bridge.TypographyCalls == 4,
            "older bridge missing optional typography export is probed once");
        Console.WriteLine($"PASS {checks} typography lifetime/cache/owner/fallback checks");
    }
    private static bool Reject(Action action) { try { action(); return false; } catch (ArgumentOutOfRangeException) { return true; } }
}

sealed partial class FakeBridge
{
    internal int TypographyCalls;
    internal bool TypographyMissing;
    public int Typography(int body, int headings, int data)
    {
        TypographyCalls++;
        if (TypographyMissing) throw new EntryPointNotFoundException();
        return 1;
    }
}
