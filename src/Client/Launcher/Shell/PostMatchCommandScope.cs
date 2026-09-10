using System;
using System.Threading;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>UI-thread command ownership. Leaving always preempts a pending vote.</summary>
internal sealed class PostMatchCommandScope : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _vote;
    private bool _leaving;
    private bool _disposed;
    public bool TryBeginVote(out CancellationToken token)
    {
        token = default;
        if (_disposed || _leaving || _vote != null) return false;
        _vote = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        token = _vote.Token;
        return true;
    }
    public bool TryBeginLeave(out CancellationToken token)
    {
        token = default;
        if (_disposed || _leaving) return false;
        _leaving = true;
        _vote?.Cancel();
        token = _lifetime.Token;
        return true;
    }
    public void CompleteVote() { _vote?.Dispose(); _vote = null; }
    public void CompleteLeave() => _leaving = false;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _vote?.Cancel();
        _lifetime.Dispose();
        // The vote operation disposes its child when its canceled await unwinds.
    }
}
