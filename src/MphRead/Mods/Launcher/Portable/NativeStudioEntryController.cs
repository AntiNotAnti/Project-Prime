using System;

namespace MphRead.Mods.Launcher;

/// <summary>
/// Presentation-neutral native Studio command boundary. The current game
/// launcher owns process creation, preview returns, and document recovery.
/// </summary>
public interface INativeStudioLauncher
{
    bool LaunchMapStudio();
}

public sealed class NativeStudioEntryController : IDisposable
{
    private readonly INativeStudioLauncher _launcher;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private bool _disposed;
    private long _revision = 1;
    private string _status = "STUDIO READY // SELECT AN ACTIVITY";
    public bool Closed { get; private set; }

    public NativeStudioEntryController(INativeStudioLauncher launcher)
        => _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));

    public readonly record struct Snapshot(long Revision, string Status, bool Closed);
    public Snapshot Current()
    {
        Check();
        return new(_revision, _status, Closed);
    }

    public bool Launch()
    {
        Check();
        if (Closed) return false;
        bool success;
        try { success = _launcher.LaunchMapStudio(); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            _status = "STUDIO LAUNCH FAILED: " + error.Message;
            _revision++;
            return false;
        }
        if (!success)
        {
            _status = "STUDIO UNAVAILABLE // CHECK DESKTOP GAME FILES";
            _revision++;
            return false;
        }
        Closed = true;
        _revision++;
        return true;
    }

    public void Back()
    {
        Check();
        Closed = true;
        _revision++;
    }

    private void Check()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Studio entry navigation belongs to its owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Check();
        _disposed = true;
    }
}
