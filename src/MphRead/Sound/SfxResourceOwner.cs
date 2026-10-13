using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Audio.OpenAL;

namespace MphRead.Sound;

// Only acquisition/release crosses this seam. Playback keeps its existing AL path.
internal interface ISfxNativeBackend
{
    ALDevice OpenDevice();
    ALContext CreateContext(ALDevice device);
    bool MakeCurrent(ALContext context);
    void GenBuffers(Span<int> ids);
    void GenSources(Span<int> ids);
    void DeleteBuffer(int id);
    void DeleteSource(int id);
    void DestroyContext(ALContext context);
    void CloseDevice(ALDevice device);
}

internal sealed class SfxOpenAlBackend : ISfxNativeBackend
{
    internal static readonly SfxOpenAlBackend Instance = new();
    public ALDevice OpenDevice() => ALC.OpenDevice(null);
    public ALContext CreateContext(ALDevice device) => ALC.CreateContext(device, new ALContextAttributes());
    public bool MakeCurrent(ALContext context) => ALC.MakeContextCurrent(context);
    public void GenBuffers(Span<int> ids) { AL.GenBuffers(ids); CheckError(); }
    public void GenSources(Span<int> ids) { AL.GenSources(ids); CheckError(); }
    public void DeleteBuffer(int id) { AL.DeleteBuffer(id); CheckError(); }
    public void DeleteSource(int id) { AL.DeleteSource(id); CheckError(); }
    public void DestroyContext(ALContext context) => ALC.DestroyContext(context);
    public void CloseDevice(ALDevice device)
    {
        if (!ALC.CloseDevice(device)) throw new InvalidOperationException("OpenAL device did not close.");
    }
    private static void CheckError()
    {
        ALError error = AL.GetError();
        if (error != ALError.NoError) throw new InvalidOperationException($"OpenAL allocation failed: {error}");
    }
}

/// <summary>One scene owns these handles, including acquisitions before a failed load.</summary>
internal sealed class SfxResourceOwner
{
    // OpenAL device creation/destruction can contend in the native backend.
    // One ordered close worker keeps pending shutdown work off the render lane.
    private static readonly object NativeGate = new();
    private static Task _pendingClose = Task.CompletedTask;
    // A failed native close leaves ownership unknown. Reopening after it can
    // accumulate devices on every scene/probe, even when the worker completed.
    // Guarded by NativeGate; only process restart can clear this uncertainty.
    private static Exception? _unconfirmedDeviceClose;
    private readonly ISfxNativeBackend _backend;
    private readonly bool _closeOnWorker;
    private ALDevice _device;
    private ALContext _context;
    private bool _current, _released;
    internal int[] Buffers { get; }
    internal int[] Sources { get; }
    internal Task Completion { get; private set; } = Task.CompletedTask;

    internal SfxResourceOwner(ISfxNativeBackend backend, int bufferCount, int sourceCount, bool closeOnWorker)
    {
        _backend = backend;
        _closeOnWorker = closeOnWorker;
        Buffers = new int[bufferCount];
        Sources = new int[sourceCount];
    }

    internal void Initialize(TimeSpan? previousCloseTimeout = null)
    {
        // Acquisition happens during scene loading, never during playback.
        // Drain the previous close before reopening so repeated transitions
        // cannot accumulate native devices or queued shutdown work.
        Task previousClose = Volatile.Read(ref _pendingClose);
        try
        {
            if (!previousClose.Wait(previousCloseTimeout ?? TimeSpan.FromSeconds(2)))
                throw new TimeoutException("Previous OpenAL cleanup did not finish; continuing without new SFX.");
        }
        catch (AggregateException ex) { Console.Error.WriteLine($"[sound] previous audio cleanup: {ex.GetBaseException().Message}"); }
        lock (NativeGate)
        {
            if (_released || _device.Handle != IntPtr.Zero)
                throw new InvalidOperationException("Audio resource owner cannot be initialized twice.");
            if (_unconfirmedDeviceClose != null)
                throw new InvalidOperationException("Previous OpenAL device did not close; continuing without new SFX until restart.",
                    _unconfirmedDeviceClose);
            try
            {
                _device = _backend.OpenDevice();
                if (_device.Handle == IntPtr.Zero) throw new InvalidOperationException("OpenAL device unavailable.");
                _context = _backend.CreateContext(_device);
                if (_context.Handle == IntPtr.Zero) throw new InvalidOperationException("OpenAL context unavailable.");
                _current = _backend.MakeCurrent(_context);
                if (!_current) throw new InvalidOperationException("OpenAL context could not become current.");
                // Store IDs directly in owned arrays so partial batch failures are releasable.
                if (Buffers.Length != 0) _backend.GenBuffers(Buffers);
                if (Sources.Length != 0) _backend.GenSources(Sources);
                foreach (int id in Buffers) if (id == 0) throw new InvalidOperationException("OpenAL buffer allocation returned no handle.");
                foreach (int id in Sources) if (id == 0) throw new InvalidOperationException("OpenAL source allocation returned no handle.");
            }
            catch
            {
                Release();
                throw;
            }
        }
    }

    internal void Release()
    {
        // The scene owner may time out before acquiring anything. Do not
        // join the native gate or append empty work behind a stalled close.
        if (_device.Handle == IntPtr.Zero && _context.Handle == IntPtr.Zero)
        {
            _released = true;
            return;
        }
        lock (NativeGate)
        {
            if (_released) return;
            _released = true;
            var errors = new List<Exception>();
            void Attempt(Action action) { try { action(); } catch (Exception ex) { errors.Add(ex); } }
            // Delete while this context is current. Android contexts do not
            // reclaim mixer dictionaries, so every individual ID must be released.
            if (_current)
            {
                // A capability probe may have replaced/unbound the current
                // context since acquisition. Never delete IDs in another owner.
                bool rebound = false;
                Attempt(() =>
                {
                    rebound = _backend.MakeCurrent(_context);
                    if (!rebound) throw new InvalidOperationException("OpenAL owner context could not become current for cleanup.");
                });
                if (rebound)
                {
                    foreach (int id in Sources) if (id != 0) Attempt(() => _backend.DeleteSource(id));
                    foreach (int id in Buffers) if (id != 0) Attempt(() => _backend.DeleteBuffer(id));
                }
                Attempt(() =>
                {
                    if (!_backend.MakeCurrent(ALContext.Null))
                        throw new InvalidOperationException("OpenAL context did not detach.");
                });
                _current = false;
            }
            ALDevice device = _device;
            ALContext context = _context;
            _device = ALDevice.Null;
            _context = ALContext.Null;
            void Close()
            {
                if (context.Handle != IntPtr.Zero) Attempt(() => _backend.DestroyContext(context));
                if (device.Handle != IntPtr.Zero)
                {
                    try { _backend.CloseDevice(device); }
                    catch (Exception ex)
                    {
                        _unconfirmedDeviceClose ??= ex;
                        errors.Add(ex);
                    }
                }
                if (errors.Count != 0) throw new AggregateException("Audio cleanup failed.", errors);
            }
            if (_closeOnWorker)
            {
                // Continuations form one bounded worker lane; no idle thread
                // pool workers block waiting for earlier native destruction.
                Completion = _pendingClose = _pendingClose.ContinueWith(_ =>
                {
                    lock (NativeGate) Close();
                }, default, TaskContinuationOptions.None, TaskScheduler.Default);
            }
            else
            {
                // Android CloseDevice is shared-mixer bookkeeping. Complete it
                // now so a stale worker cannot stop the next scene's voices.
                try { Close(); } catch (Exception ex) { Completion = Task.FromException(ex); }
            }
        }
    }
}
