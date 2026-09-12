using System;
using System.Threading;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>UI-thread command ownership. Leaving always preempts pending recap commands.</summary>
internal sealed class PostMatchCommandScope : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _vote;
    private CancellationTokenSource? _hunter;
    private bool _leaving;
    private bool _disposed;
    public bool TryBeginVote(out CancellationToken token)
    {
        lock (_gate)
        {
            token = default;
            if (_disposed || _leaving || _vote != null || _hunter != null) return false;
            _vote = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            token = _vote.Token;
            return true;
        }
    }
    public bool TryBeginHunter(out CancellationToken token)
    {
        lock (_gate)
        {
            token = default;
            if (_disposed || _leaving || _vote != null || _hunter != null) return false;
            _hunter = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            token = _hunter.Token;
            return true;
        }
    }
    public bool TryBeginLeave(out CancellationToken token)
    {
        CancellationTokenSource? vote;
        CancellationTokenSource? hunter;
        lock (_gate)
        {
            token = default;
            if (_disposed || _leaving) return false;
            _leaving = true;
            vote = _vote;
            hunter = _hunter;
            token = _lifetime.Token;
        }
        vote?.Cancel();
        hunter?.Cancel();
        return true;
    }
    public void CompleteVote()
    {
        CancellationTokenSource? vote;
        lock (_gate)
        {
            vote = _vote;
            _vote = null;
        }
        vote?.Dispose();
    }
    public void CompleteHunter()
    {
        CancellationTokenSource? hunter;
        lock (_gate)
        {
            hunter = _hunter;
            _hunter = null;
        }
        hunter?.Dispose();
    }
    public void CompleteLeave()
    {
        lock (_gate) _leaving = false;
    }
    public void Dispose()
    {
        CancellationTokenSource? vote;
        CancellationTokenSource? hunter;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            vote = _vote;
            _vote = null;
            hunter = _hunter;
            _hunter = null;
        }
        _lifetime.Cancel();
        vote?.Cancel();
        hunter?.Cancel();
        vote?.Dispose();
        hunter?.Dispose();
        _lifetime.Dispose();
    }
}
