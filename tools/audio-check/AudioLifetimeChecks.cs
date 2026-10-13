using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Sound;
using MphRead.Sound;
using OpenTK.Audio.OpenAL;

static class AudioLifetimeChecks
{
    static int _assertions;
    static void Require(bool condition, string message)
    { _assertions++; if (!condition) throw new Exception(message); }

    internal static void Run()
    {
        // Characterize the pre-fix Android close: neither context destruction
        // nor mixer Close removes even a single live dictionary entry.
        int oldBuffer = SfxMixer.NewBuffer(), oldSource = SfxMixer.NewSource();
        SfxMixer.Close();
        Require(MixerCount("_buffers") == 1 && MixerCount("_voices") == 1, "Legacy mixer close characterization");
        SfxMixer.DeleteSource(oldSource); SfxMixer.DeleteBuffer(oldBuffer);

        for (int cycle = 0; cycle < 120; cycle++)
        {
            var backend = new Backend(useMixer: true);
            var owner = new SfxResourceOwner(backend, 65, 129, closeOnWorker: false);
            owner.Initialize();
            foreach (int id in owner.Buffers)
                SfxMixer.FillBuffer(id, ALFormat.Mono16, new byte[128], 22050);
            Require(MixerCount("_buffers") == 65 && MixerCount("_voices") == 129, "Scene ownership count");
            owner.Release(); owner.Release();
            Require(owner.Completion.IsCompletedSuccessfully && MixerCount("_buffers") == 0
                && MixerCount("_voices") == 0, "Android scene left owned objects");
            Require(backend.Closed == 1 && backend.Destroyed == 1, "Repeated release duplicated native close");
        }

        // Fail after acquiring a device/context, during each partial batch,
        // and after returning null handles or failed context activation.
        foreach (int failure in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            var backend = new Backend(failure: failure);
            var owner = new SfxResourceOwner(backend, 65, 129, closeOnWorker: false);
            bool failed = false;
            try { owner.Initialize(); } catch (InvalidOperationException) { failed = true; }
            owner.Release();
            Require(failed && backend.Buffers.Count == 0 && backend.Sources.Count == 0,
                $"Partial initialization retained IDs at failure {failure}");
            Require(backend.Devices == 0 && backend.Contexts == 0 && owner.Completion.IsCompletedSuccessfully,
                $"Partial initialization retained device/context at failure {failure}");
        }

        var contextBackend = new Backend();
        var foreground = new SfxResourceOwner(contextBackend, 2, 2, closeOnWorker: false);
        var probe = new SfxResourceOwner(contextBackend, 0, 0, closeOnWorker: false);
        foreground.Initialize(); probe.Initialize(); probe.Release();
        foreground.Release();
        Require(foreground.Completion.IsCompletedSuccessfully && contextBackend.Buffers.Count == 0
            && contextBackend.Sources.Count == 0, "Probe invalidated foreground context cleanup");
        var failedProbe = new Backend(cleanupFailure: true);
        Require(Sfx.CheckAudioLoad(failedProbe, () => true) == MphRead.SoundCapability.None
            && failedProbe.Devices == 0 && failedProbe.Contexts == 0, "Probe cleanup failure escaped silent capability fallback");

        var workerBackend = new Backend();
        Task lastClose = Task.CompletedTask;
        for (int cycle = 0; cycle < 120; cycle++)
        {
            var owner = new SfxResourceOwner(workerBackend, 65, 129, closeOnWorker: true);
            owner.Initialize();
            Require(workerBackend.Devices == 1 && workerBackend.Contexts == 1, "Pending worker close retained prior scene");
            owner.Release(); lastClose = owner.Completion;
        }
        Require(lastClose.Wait(TimeSpan.FromSeconds(5)) && workerBackend.Devices == 0
            && workerBackend.Contexts == 0 && workerBackend.PeakDevices == 1, "Desktop close lane failed to bound device ownership");

        var cleanupBackend = new Backend(cleanupFailure: true);
        var cleanupOwner = new SfxResourceOwner(cleanupBackend, 1, 1, closeOnWorker: false);
        cleanupOwner.Initialize(); cleanupOwner.Release();
        Require(cleanupBackend.Devices == 0 && cleanupBackend.Contexts == 0
            && cleanupOwner.Completion.IsFaulted, "Deletion error prevented later native cleanup");
        _ = cleanupOwner.Completion.Exception;
        var recoveredBackend = new Backend();
        var recoveredOwner = new SfxResourceOwner(recoveredBackend, 1, 1, closeOnWorker: false);
        recoveredOwner.Initialize(); recoveredOwner.Release();
        Require(recoveredOwner.Completion.IsCompletedSuccessfully && recoveredBackend.Devices == 0,
            "Confirmed device close after earlier cleanup errors prevented recovery");
        var workerCleanupBackend = new Backend(cleanupFailure: true);
        var workerCleanupOwner = new SfxResourceOwner(workerCleanupBackend, 1, 1, closeOnWorker: true);
        workerCleanupOwner.Initialize(); workerCleanupOwner.Release();
        try { workerCleanupOwner.Completion.GetAwaiter().GetResult(); } catch (AggregateException) { }
        var workerRecovery = new SfxResourceOwner(recoveredBackend, 1, 1, closeOnWorker: true);
        workerRecovery.Initialize(); workerRecovery.Release();
        workerRecovery.Completion.GetAwaiter().GetResult();
        Require(workerCleanupBackend.Devices == 0 && workerCleanupOwner.Completion.IsFaulted
            && recoveredBackend.Devices == 0, "Faulted worker cleanup with confirmed device close prevented recovery");

        // Pause destruction to prove native acquisition cannot overlap it.
        using var entered = new ManualResetEventSlim();
        using var unblock = new ManualResetEventSlim();
        var delayedBackend = new Backend(entered: entered, unblock: unblock);
        var delayed = new SfxResourceOwner(delayedBackend, 1, 1, closeOnWorker: true);
        delayed.Initialize(); delayed.Release();
        Require(entered.Wait(TimeSpan.FromSeconds(5)), "Native close worker did not start");
        var nextBackend = new Backend();
        var next = new SfxResourceOwner(nextBackend, 1, 1, closeOnWorker: true);
        var timedOutBackend = new Backend();
        var timedOut = new SfxResourceOwner(timedOutBackend, 1, 1, closeOnWorker: true);
        bool timedOutAsExpected = false;
        Task unavailable = Task.Run(() =>
        {
            try { timedOut.Initialize(TimeSpan.FromMilliseconds(50)); }
            catch (TimeoutException) { timedOutAsExpected = true; }
            finally { timedOut.Release(); }
        });
        try
        {
            Require(unavailable.Wait(TimeSpan.FromSeconds(1)) && timedOutAsExpected,
                "Stalled native close blocked silent fallback cleanup");
            Require(timedOutBackend.Devices == 0 && timedOut.Completion.IsCompletedSuccessfully,
                "Timed out owner opened a device or queued empty cleanup");
        }
        catch { unblock.Set(); throw; }
        Task acquire = Task.Run(() => next.Initialize());
        try
        {
            Require(!acquire.Wait(TimeSpan.FromMilliseconds(100)), "Acquisition overlapped native destruction");
            Require(nextBackend.Devices == 0, "Device opened during native destruction");
        }
        finally { unblock.Set(); }
        Require(acquire.Wait(TimeSpan.FromSeconds(5)), "Acquisition did not resume after close");
        next.Release();
        Require(Task.WaitAll(new[] { delayed.Completion, next.Completion }, TimeSpan.FromSeconds(5)), "Close lane did not drain");
        Require(delayedBackend.Devices == 0 && nextBackend.Devices == 0, "Worker close retained device");
        // Silent fallback teardown must retain the failed native load's
        // completion so process shutdown still sees/waits for pending cleanup.
        var completionProperty = typeof(Sfx).GetProperty("ShutdownCompletion", BindingFlags.NonPublic | BindingFlags.Static)!;
        var instanceProperty = typeof(Sfx).GetProperty("Instance")!;
        Task previousCompletion = Sfx.ShutdownCompletion;
        var failedLoadCleanup = new TaskCompletionSource();
        completionProperty.SetValue(null, failedLoadCleanup.Task);
        instanceProperty.SetValue(null, new SfxInstanceBase());
        Sfx.ShutDown();
        Require(ReferenceEquals(Sfx.ShutdownCompletion, failedLoadCleanup.Task), "Silent fallback hid pending native cleanup");
        failedLoadCleanup.SetResult(); completionProperty.SetValue(null, previousCompletion);

        // The dedicated-server branch must stay asset/device independent.
        MphRead.Mods.Headless.Enter();
        for (int cycle = 0; cycle < 120; cycle++)
        {
            Sfx.Load(null!);
            Require(Sfx.Instance is { } silent && silent.GetType() == typeof(SfxInstanceBase), "Headless load opened native sound");
            Sfx.ShutDown(); Sfx.ShutDown();
            Require(Sfx.Instance == null && Sfx.ShutdownCompletion.IsCompletedSuccessfully, "Headless cleanup is not idempotent");
        }
        Console.WriteLine($"PASS: {_assertions} lifecycle assertions; 120 Android-mixer and 120 worker-close cycles; zero owned buffers/sources/devices/contexts after release; 120 headless cycles; eight initialization failures; cleanup failure; idempotence; serialized native close");
    }

    static int MixerCount(string name) => ((IDictionary)typeof(SfxMixer)
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).Count;

    sealed class Backend : ISfxNativeBackend
    {
        readonly bool _mixer, _cleanupFailure;
        readonly int _failure;
        readonly ManualResetEventSlim? _entered, _unblock;
        int _nextId, _nextContext, _currentContext;
        readonly Dictionary<int, int> _bufferContexts = new(), _sourceContexts = new();
        internal readonly HashSet<int> Buffers = new(), Sources = new();
        internal int Devices, Contexts, Closed, Destroyed, PeakDevices;
        internal Backend(bool useMixer = false, int failure = 0, bool cleanupFailure = false,
            ManualResetEventSlim? entered = null, ManualResetEventSlim? unblock = null)
        { _mixer = useMixer; _failure = failure; _cleanupFailure = cleanupFailure; _entered = entered; _unblock = unblock; }
        public ALDevice OpenDevice()
        {
            if (_failure == 1) throw new InvalidOperationException("open");
            if (_failure == 6) return ALDevice.Null;
            Devices++; PeakDevices = Math.Max(PeakDevices, Devices); return new ALDevice(new IntPtr(1));
        }
        public ALContext CreateContext(ALDevice device)
        {
            if (_failure == 2) throw new InvalidOperationException("context");
            if (_failure == 7) return ALContext.Null;
            Contexts++; return new ALContext(new IntPtr(++_nextContext));
        }
        public bool MakeCurrent(ALContext context)
        {
            if (context.Handle != IntPtr.Zero)
            {
                if (_failure == 3) throw new InvalidOperationException("make current");
                if (_failure == 8) return false;
            }
            _currentContext = context.Handle.ToInt32();
            return true;
        }
        public void GenBuffers(Span<int> ids)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = _mixer ? SfxMixer.NewBuffer() : ++_nextId; Buffers.Add(ids[i]); _bufferContexts[ids[i]] = _currentContext;
                if (_failure == 4 && i == 3) throw new InvalidOperationException("partial buffers");
            }
        }
        public void GenSources(Span<int> ids)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = _mixer ? SfxMixer.NewSource() : ++_nextId; Sources.Add(ids[i]); _sourceContexts[ids[i]] = _currentContext;
                if (_failure == 5 && i == 3) throw new InvalidOperationException("partial sources");
            }
        }
        public void DeleteBuffer(int id)
        {
            if (_bufferContexts[id] != _currentContext) throw new Exception("buffer deletion used another context");
            if (!Buffers.Remove(id)) throw new Exception("duplicate buffer delete");
            if (_mixer) SfxMixer.DeleteBuffer(id);
        }
        public void DeleteSource(int id)
        {
            if (_sourceContexts[id] != _currentContext) throw new Exception("source deletion used another context");
            if (!Sources.Remove(id)) throw new Exception("duplicate source delete");
            if (_mixer) SfxMixer.DeleteSource(id);
            if (_cleanupFailure) throw new InvalidOperationException("delete source");
        }
        public void DestroyContext(ALContext context)
        {
            _entered?.Set(); _unblock?.Wait(); Destroyed++; Contexts--;
            if (_cleanupFailure) throw new InvalidOperationException("destroy context after release");
        }
        public void CloseDevice(ALDevice device)
        {
            if (_mixer) SfxMixer.Close(); Closed++; Devices--;
        }
    }
}
