using System;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;

static void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name);
    Console.WriteLine("RML STUDIO PASS " + name);
}

var gateway = new Fake();
using (var studio = new NativeStudioEntryController(gateway))
{
    Check(studio.Current().Status.Contains("READY") && gateway.Calls == 0,
        "opening native Studio entry never launches or creates a process");
    gateway.AllowLaunch = false;
    Check(!studio.Launch() && !studio.Current().Closed && gateway.Calls == 1
        && studio.Current().Status.Contains("UNAVAILABLE"),
        "missing desktop Studio keeps the native page open and explains recovery");
    gateway.AllowLaunch = true;
    Check(studio.Launch() && studio.Current().Closed && gateway.Calls == 2,
        "explicit launch forwards once to the current Map Studio route");
    Check(!studio.Launch() && gateway.Calls == 2,
        "retired native page cannot duplicate Studio launch");
}

using (var cancel = new NativeStudioEntryController(new Fake()))
{
    cancel.Back();
    Check(cancel.Current().Closed && !cancel.Launch(),
        "Back safely closes the UI without starting Studio");
}

using (var broken = new NativeStudioEntryController(new Fake { ThrowOnLaunch = true }))
{
    Check(!broken.Launch() && !broken.Current().Closed
        && broken.Current().Status.Contains("FAILED"),
        "existing Studio launch failures are visible, not silently acknowledged");
}
using (var native = new NativeStudioEntryController(new Fake()))
{
    Exception? wrongThread = null;
    Task.Run(() =>
    {
        try { native.Launch(); }
        catch (Exception e) { wrongThread = e; }
    }).GetAwaiter().GetResult();
    Check(wrongThread is InvalidOperationException,
        "native Studio actions run only on the launcher owner thread");
}
Console.WriteLine("RML Studio entry contracts complete.");

sealed class Fake : INativeStudioLauncher
{
    public int Calls;
    public bool AllowLaunch;
    public bool ThrowOnLaunch;
    public bool LaunchMapStudio()
    {
        Calls++;
        if (ThrowOnLaunch) throw new InvalidOperationException("simulated backend refusal");
        return AllowLaunch;
    }
}
