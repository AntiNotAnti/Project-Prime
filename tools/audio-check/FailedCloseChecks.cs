using System;
using System.Diagnostics;
using System.Reflection;
using MphRead.Sound;
using OpenTK.Audio.OpenAL;

static class FailedCloseChecks
{
    internal static void Run()
    {
        // A failed close deliberately fences this process. Use fresh processes
        // for each path rather than adding a production reset of unknown ownership.
        foreach (string mode in new[] { "sync-false", "worker-false", "probe-false", "sync-throw", "worker-throw", "probe-throw", "partial-throw" })
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            if (System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add(mode);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000))
            {
                process.Kill(entireProcessTree: true);
                throw new Exception($"Failed-close fixture timed out: {mode}");
            }
            string result = output.GetAwaiter().GetResult();
            string diagnostics = errors.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new Exception($"{mode}: {result}\n{diagnostics}");
            Console.Write(result);
        }
    }

    internal static void RunChild(string mode)
    {
        if (mode is not ("sync-false" or "worker-false" or "probe-false" or "sync-throw" or "worker-throw" or "probe-throw" or "partial-throw"))
            throw new Exception("Unknown failed-close fixture mode.");
        var backend = new Backend(mode);
        for (int cycle = 0; cycle < 120; cycle++)
        {
            if (mode.StartsWith("probe"))
            {
                if (Sfx.CheckAudioLoad(backend, () => true) != MphRead.SoundCapability.None)
                    throw new Exception("Probe advertised audio after unknown device close.");
            }
            else
            {
                var owner = new SfxResourceOwner(backend, 0, 0, mode.StartsWith("worker"));
                bool failed = false;
                try { owner.Initialize(); }
                catch (InvalidOperationException) { failed = true; }
                if (cycle > 0 && !failed) throw new Exception("Reopened after unconfirmed device close.");
                owner.Release(); owner.Release();
                try { owner.Completion.GetAwaiter().GetResult(); }
                catch (AggregateException) { }
            }
            if (backend.Opened != 1 || backend.Devices != 1 || backend.Contexts != 0 || backend.CloseAttempts != 1)
                throw new Exception($"Unbounded ownership: {mode} cycle={cycle} opens={backend.Opened} devices={backend.Devices} contexts={backend.Contexts} closes={backend.CloseAttempts}");
        }
        // The fence covers different owners/backends too, including a later probe.
        var other = new Backend("sync-false");
        if (Sfx.CheckAudioLoad(other, () => true) != MphRead.SoundCapability.None || other.Opened != 0)
            throw new Exception("A different backend bypassed the process-wide reopen fence.");
        Console.WriteLine($"PASS: {mode}; 120 attempts; opens=1 outstandingDevices=1 contexts=0 closeAttempts=1; different-backend probe opens=0");
    }

    sealed class Backend(string mode) : ISfxNativeBackend
    {
        internal int Opened, Devices, Contexts, CloseAttempts;
        public ALDevice OpenDevice() { Devices++; return new ALDevice(new IntPtr(++Opened)); }
        public ALContext CreateContext(ALDevice device)
        {
            if (mode == "partial-throw") throw new InvalidOperationException("Context initialization failed.");
            Contexts++; return new ALContext(device.Handle);
        }
        public bool MakeCurrent(ALContext context) => true;
        public void GenBuffers(Span<int> ids) { }
        public void GenSources(Span<int> ids) { }
        public void DeleteBuffer(int id) { }
        public void DeleteSource(int id) { }
        public void DestroyContext(ALContext context) { Contexts--; }
        public void CloseDevice(ALDevice device)
        {
            CloseAttempts++;
            // Model ALC.CloseDevice(false) as the real adapter's exception,
            // and a native exception before ownership can be confirmed released.
            throw new InvalidOperationException(mode.EndsWith("false")
                ? "OpenAL device did not close." : "Native close threw before releasing the device.");
        }
    }
}
