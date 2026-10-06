using System;

namespace MphRead.Mods.Render;

// Native-independent reservation/callback ownership for one timestamp slot.
// The callback checks the exact opaque token assigned to its map request. It
// never reads a mutable "current token" from another reservation or GPU handle.
internal sealed class GpuTimingReadbackLease
{
    private enum Phase { Free, Recording, Submitted, Mapping, Completed }
    private readonly object _gate = new();
    private Phase _phase;
    private long _frameId;
    private nint _token;
    private int _status = -1;

    internal nint MapToken { get { lock (_gate) return _token; } }

    internal bool TryReserve(long frameId)
    {
        lock (_gate)
        {
            if (_phase != Phase.Free) return false;
            _phase = Phase.Recording;
            _frameId = frameId;
            return true;
        }
    }

    internal void MarkSubmitted()
    {
        lock (_gate)
        {
            if (_phase != Phase.Recording) throw new InvalidOperationException("GPU timing lease was not recording.");
            _phase = Phase.Submitted;
        }
    }

    internal void BeginMapping(nint token)
    {
        lock (_gate)
        {
            if (_phase != Phase.Submitted || token == 0)
                throw new InvalidOperationException("GPU timing map requires a submitted lease and nonzero token.");
            _token = token;
            _status = -1;
            _phase = Phase.Mapping;
        }
    }

    internal bool TryComplete(nint token, int status)
    {
        lock (_gate)
        {
            if (_phase != Phase.Mapping || token == 0 || token != _token) return false;
            _status = status;
            _phase = Phase.Completed;
            return true;
        }
    }

    internal bool TryGetCompletion(out long frameId, out int status)
    {
        lock (_gate)
        {
            frameId = _frameId;
            status = _status;
            return _phase == Phase.Completed;
        }
    }

    internal void Finish()
    {
        lock (_gate)
        {
            if (_phase != Phase.Completed) throw new InvalidOperationException("GPU timing lease has no completed map.");
            Reset();
        }
    }

    // Token-specific cancellation cannot clear a newer map request. Terminal
    // teardown passes zero and invalidates any reservation before native frees.
    internal bool Cancel(nint expectedToken = 0)
    {
        lock (_gate)
        {
            if (expectedToken != 0 && expectedToken != _token) return false;
            Reset();
            return true;
        }
    }

    // Decide before invalidating callback ownership. The pinned native runtime
    // rejects unmap on idle buffers: only accepted pending maps and successful
    // completed maps need native unmap. A call that throws before returning has
    // not established a pending map, although a synchronous success callback
    // still proves that the native buffer became mapped.
    internal bool CancelAndTakeUnmap(int successfulMapStatus, bool pendingMapAccepted = true,
        nint expectedToken = 0)
    {
        lock (_gate)
        {
            if (expectedToken != 0 && expectedToken != _token) return false;
            bool unmap = (_phase == Phase.Mapping && pendingMapAccepted)
                || (_phase == Phase.Completed && _status == successfulMapStatus);
            Reset();
            return unmap;
        }
    }

    private void Reset()
    {
        _phase = Phase.Free;
        _frameId = 0;
        _token = 0;
        _status = -1;
    }
}
