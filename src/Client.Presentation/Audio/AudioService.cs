using System;

namespace MphRead.Sound;

/// <summary>
/// Owns the process audio lifetime.  Scene presentation is a binding, not the
/// owner of the OpenAL context or the SoundFlow output.  This is important for
/// the persistent shell: leaving a match stops and detaches presentation, but
/// does not tear down the process audio banks or device.
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly AudioDeviceLifetime _deviceLifetime;
    private readonly object _stateGate = new();
    private bool _disposed;
    private Scene? _boundScene;

    /// <summary>
    /// The one audio owner used by the desktop and Android compatibility
    /// facades.  A process has one OpenAL context and one SoundFlow output.
    /// </summary>
    public static AudioService Process { get; } = new();

    internal AudioService()
    {
        _deviceLifetime = new AudioDeviceLifetime();
    }

    /// <summary>Whether a scene currently owns the presentation binding.</summary>
    public bool IsPresentationBound
    {
        get
        {
            lock (_stateGate) return _boundScene != null;
        }
    }

    public bool IsDisposed
    {
        get
        {
            lock (_stateGate) return _disposed;
        }
    }

    /// <summary>The scene receiving presentation audio requests, if any.</summary>
    public Scene? BoundScene
    {
        get
        {
            lock (_stateGate) return _boundScene;
        }
    }

    /// <summary>
    /// Load the process SFX banks once and bind the supplied scene.  Repeated
    /// calls reuse a loaded bank and only replace its presentation binding.
    /// </summary>
    public void Load(Scene? scene)
    {
        Execute(() =>
        {
            Sfx.LoadCore(scene);
            SetBoundScene(Sfx.HasPresentationBinding ? scene : null);
        });
    }

    /// <summary>
    /// Rebinds scene-owned <see cref="AudioRequests"/> without reconstructing
    /// the process audio device or already-loaded SFX banks.
    /// </summary>
    public void BindPresentation(Scene scene, bool stopCurrent = true)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Execute(() =>
        {
            Sfx.BindPresentationCore(scene, stopCurrent);
            SetBoundScene(scene);
        });
    }

    /// <summary>
    /// Stops current presentation and detaches its scene while retaining
    /// process-owned banks and devices for the next match or Theatre session.
    /// </summary>
    public void StopPresentation()
    {
        TryExecute(() =>
        {
            Sfx.StopPresentationCore();
            SetBoundScene(null);
        });
    }

    /// <summary>
    /// Releases a presentation only when it still owns the process binding.
    /// Replay/killcam cleanup can therefore retire a failed or stale replay
    /// without interrupting a live scene that has already reclaimed audio.
    /// </summary>
    internal void StopPresentation(Scene owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        TryExecute(() =>
        {
            if (!ReferenceEquals(_boundScene, owner)) return;
            Sfx.StopPresentationCore();
            SetBoundScene(null);
        });
    }

    /// <summary>
    /// Releases the current SFX context synchronously.  Unlike
    /// <see cref="StopPresentation"/>, this is a resource shutdown and is
    /// intended for diagnostic/process teardown paths.
    /// </summary>
    internal void ShutdownCurrentAudio()
    {
        TryExecute(() =>
        {
            Sfx.ShutDownCore();
            SetBoundScene(null);
        });
    }

    /// <summary>Runs a presentation operation while the audio owner is live.</summary>
    internal void Execute(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _deviceLifetime.Execute(() =>
        {
            lock (_stateGate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                action();
            }
        });
    }

    /// <summary>Best-effort operation boundary for stale scene callbacks.</summary>
    internal bool TryExecute(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _deviceLifetime.TryExecute(() =>
        {
            lock (_stateGate)
            {
                if (_disposed) return;
                action();
            }
        });
    }

    /// <summary>
    /// Final process shutdown.  The OpenAL context, SoundFlow output, and
    /// decoder task are all drained while the owner gate is held; no detached
    /// destruction task can outlive this call.
    /// </summary>
    public void Shutdown()
    {
        _deviceLifetime.Shutdown(() =>
        {
            lock (_stateGate)
            {
                if (_disposed) return;
                Sfx.ShutDownCore();
                MusicPlayer.ShutdownOutput();
                _boundScene = null;
                _disposed = true;
            }
        });
    }

    public void Dispose() => Shutdown();

    private void SetBoundScene(Scene? scene)
    {
        lock (_stateGate) _boundScene = scene;
    }
}

/// <summary>
/// Small deterministic gate for operations that touch a native audio device.
/// It deliberately uses synchronous ownership: a context/device cannot be
/// destroyed on a detached worker while a scene callback is still using it.
/// </summary>
internal sealed class AudioDeviceLifetime : IDisposable
{
    private readonly object _gate = new();
    private bool _disposed;

    internal void Execute(Action action)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            action();
        }
    }

    internal bool TryExecute(Action action)
    {
        lock (_gate)
        {
            if (_disposed) return false;
            action();
            return true;
        }
    }

    internal void Shutdown(Action action)
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                // Keep the owner live for the duration of its synchronous
                // cleanup. Nested cleanup (for example optional SoundFlow
                // players released by Sfx.ShutDownCore) still runs on this
                // same thread/gate; callers waiting on the gate cannot enter
                // until every native resource has been released.
                action();
            }
            finally
            {
                _disposed = true;
            }
        }
    }

    public void Dispose() => Shutdown(static () => { });
}
