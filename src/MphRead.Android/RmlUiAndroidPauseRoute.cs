#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Droid;

internal enum RmlUiAndroidPauseState { Idle, Requested, Open, Closed, Resume, Settings, Replay, Leave, Quit, Fallback }
internal sealed record RmlUiAndroidPauseSnapshot(long Lifetime, long Revision, RmlUiAndroidPauseState State,
    int Width = 0, int Height = 0);

/// <summary>One native page stack on the caller's existing EGL context.</summary>
internal interface IRmlUiAndroidPauseSurface : IDisposable
{
    RmlUiAndroidInputQueue Input { get; }
    void Open(int width, int height, float density);
    void Back();
    void Render(int width, int height, float density);
    bool TryTakeEffect(out RmlUiAndroidPauseState effect);
    void Suspend();
    void Resume();
}

/// <summary>
/// Copied requests enter from the UI; all page/controller/native work remains
/// on the existing EGL owner. Scene identity is opaque here and never exposed
/// in a UI snapshot. There is no second scene, context, loop or session pump.
/// </summary>
internal sealed class RmlUiAndroidPauseRoute
{
    private readonly object _gate = new();
    private readonly Task<string> _assetRoot;
    private readonly Func<object, string, IRmlUiAndroidPauseSurface> _create;
    private readonly Action<RmlUiAndroidPauseSnapshot> _notify;
    private readonly Action<string> _log;
    private RmlUiAndroidPauseSnapshot _snapshot = new(0, 0, RmlUiAndroidPauseState.Idle);
    private RmlUiAndroidInputQueue? _input;
    private IRmlUiAndroidPauseSurface? _surface;
    private object? _scene;
    private bool _openPending, _backPending, _closePending, _stopped, _disabled;
    private bool _surfaceAvailable, _surfaceSuspended;
    private bool _focused = true;
    private int _owner;

    internal RmlUiAndroidPauseRoute(Task<string> assetRoot,
        Func<object, string, IRmlUiAndroidPauseSurface> create,
        Action<RmlUiAndroidPauseSnapshot> notify, Action<string> log)
        => (_assetRoot, _create, _notify, _log) = (assetRoot, create, notify, log);
    internal RmlUiAndroidPauseSnapshot Current => Volatile.Read(ref _snapshot);
    internal RmlUiAndroidInputQueue? Input => Volatile.Read(ref _input);
    internal bool OwnsInput => Current.State is RmlUiAndroidPauseState.Requested or RmlUiAndroidPauseState.Open;
    internal bool IsCurrent(RmlUiAndroidPauseSnapshot notification) => ReferenceEquals(Current, notification);

    internal bool RequestToggle()
    {
        lock (_gate)
        {
            if (_stopped || _disabled || !_assetRoot.IsCompletedSuccessfully) return false;
            if (OwnsInput) { _backPending = true; return true; }
            _openPending = true; _backPending = false;
            var previous = Current;
            Volatile.Write(ref _snapshot, new(previous.Lifetime + 1, previous.Revision + 1,
                RmlUiAndroidPauseState.Requested));
            return true;
        }
    }

    // UI callbacks invalidate queued input immediately, without native calls.
    internal void SuspendInput() => Input?.Suspend();
    internal void SetFocused(bool focused)
    {
        lock (_gate) _focused = focused;
        if (!focused) SuspendInput();
    }
    internal void RetireForUiHandoff()
    {
        lock (_gate)
        {
            _openPending = _backPending = false; _closePending = true;
            var previous = Current;
            Volatile.Write(ref _snapshot, new(previous.Lifetime, previous.Revision + 1, RmlUiAndroidPauseState.Closed));
        }
        SuspendInput();
    }
    internal void RequestStop()
    {
        lock (_gate)
        {
            _stopped = true; _openPending = _backPending = false;
            var previous = Current;
            Volatile.Write(ref _snapshot, new(previous.Lifetime, previous.Revision + 1, RmlUiAndroidPauseState.Closed));
        }
        SuspendInput();
    }

    internal void SurfaceReady()
    {
        CheckOwner(); _surfaceAvailable = true;
        bool focused, retiring;
        lock (_gate) { focused = _focused; retiring = _stopped || _closePending; }
        if (_surface == null || !_surfaceSuspended || !focused || retiring) return;
        try { _surface.Resume(); _surfaceSuspended = false; }
        catch (Exception error) when (Recoverable(error)) { Fail(error); }
    }
    internal void SurfaceLost()
    {
        CheckOwner(); _surfaceAvailable = false;
        if (_surface == null || _surfaceSuspended) return;
        try { _surface.Suspend(); _surfaceSuspended = true; }
        catch (Exception error) when (Recoverable(error)) { Fail(error); }
    }

    internal void Process(object scene, int width, int height, float density)
    {
        CheckOwner();
        bool open, back, close, stopped;
        lock (_gate)
        {
            open = _openPending; back = _backPending; close = _closePending; stopped = _stopped;
            _openPending = _backPending = _closePending = false;
        }
        if (stopped) { Retire(); return; }
        if (close) Retire();
        if (_scene != null && !ReferenceEquals(_scene, scene))
        { Retire(); Publish(RmlUiAndroidPauseState.Closed); return; }
        if (!_surfaceAvailable)
        {
            lock (_gate) if (!_stopped) { _openPending |= open; _backPending |= back; }
            return;
        }
        try
        {
            if (open && _surface == null)
            {
                _scene = scene;
                _surface = _create(scene, _assetRoot.GetAwaiter().GetResult());
                _surface.Open(width, height, density);
                bool retiring;
                lock (_gate) retiring = _stopped || _closePending;
                if (retiring) { Retire(); return; }
                _surfaceSuspended = false;
                ApplyFocus();
                Volatile.Write(ref _input, _surface.Input);
                if (!Publish(RmlUiAndroidPauseState.Open, width, height)) { Retire(); return; }
            }
            ApplyFocus();
            if (back && _surface != null) _surface.Back();
            TakeEffect();
        }
        catch (Exception error) when (Recoverable(error)) { Fail(error); }
    }

    internal bool Draw(object scene, int width, int height, float density)
    {
        CheckOwner();
        bool retiring;
        lock (_gate) retiring = _stopped || _closePending;
        if (retiring) { Retire(); return false; }
        if (_surface == null || !_surfaceAvailable || !ReferenceEquals(_scene, scene)) return false;
        try
        {
            _surface.Render(width, height, density);
            TakeEffect();
            if (_surface != null && (Current.Width != width || Current.Height != height))
                Publish(RmlUiAndroidPauseState.Open, width, height);
            return true;
        }
        catch (Exception error) when (Recoverable(error)) { Fail(error); return false; }
    }

    internal void DisposeOwner()
    {
        CheckOwner(); RequestStop(); Retire();
    }
    private void TakeEffect()
    {
        if (_surface == null || !_surface.TryTakeEffect(out var effect)) return;
        Retire(); Publish(effect);
    }
    private void ApplyFocus()
    {
        bool focused;
        lock (_gate) focused = _focused;
        if (_surface == null) return;
        if (!focused && !_surfaceSuspended) { _surface.Suspend(); _surfaceSuspended = true; }
        else if (focused && _surfaceSuspended) { _surface.Resume(); _surfaceSuspended = false; }
    }
    private void Retire()
    {
        var surface = _surface;
        _surface = null; _scene = null; _surfaceSuspended = false;
        var input = Interlocked.Exchange(ref _input, null);
        input?.Suspend();
        // A page failure must still reach controller and native cleanup. The
        // concrete surface independently retires each of those owners.
        try { surface?.Dispose(); }
        catch (Exception error) when (Recoverable(error)) { _log("native pause teardown: " + error.Message); }
    }
    private void Fail(Exception error)
    {
        lock (_gate) { _disabled = true; _openPending = _backPending = false; }
        _log("native pause fallback: " + error.Message);
        Retire(); Publish(RmlUiAndroidPauseState.Fallback);
    }
    private bool Publish(RmlUiAndroidPauseState state, int width = 0, int height = 0)
    {
        RmlUiAndroidPauseSnapshot next;
        lock (_gate)
        {
            if (_stopped || _closePending) return false;
            var previous = Current;
            next = new(previous.Lifetime, previous.Revision + 1, state, width, height);
            Volatile.Write(ref _snapshot, next);
        }
        _notify(next);
        return true;
    }
    private void CheckOwner()
    {
        int current = Environment.CurrentManagedThreadId;
        if (_owner == 0) _owner = current;
        else if (_owner != current) throw new InvalidOperationException("Native pause belongs to the existing EGL owner.");
    }
    private static bool Recoverable(Exception error) => error is not (OutOfMemoryException or AccessViolationException);
}

// The C bridge is process-global. A replacement GameView must not initialize
// it while the old asynchronous Stop still owns a native session. Failed
// shutdown quarantines native initialization for the rest of this process.
internal static class RmlUiAndroidPauseNativeLease
{
    private static readonly object Gate = new();
    private static object? _owner;
    private static bool _quarantined;
    internal static bool AvailableForNewOwner { get { lock (Gate) return _owner == null && !_quarantined; } }
    internal static bool TryAcquire(object owner)
    {
        lock (Gate)
        {
            if (_owner != null || _quarantined) return false;
            _owner = owner; return true;
        }
    }
    internal static void Release(object owner, bool shutdownSucceeded)
    {
        lock (Gate)
        {
            if (!ReferenceEquals(owner, _owner)) throw new InvalidOperationException("Native pause lease owner changed.");
            _owner = null; _quarantined |= !shutdownSucceeded;
        }
    }
}
#endif
