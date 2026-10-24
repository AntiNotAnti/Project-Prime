using System;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Replay;

// One owner per playback/export, borrowed by any number of replacement scenes.
internal sealed class ReplayOwnerPresentationSource : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    internal Task<ReplayOwnerTrack?> Load { get; }
    private bool _disposed;
    internal ReplayOwnerPresentationSource(string path) => Load = ReplayOwnerTrack.LoadAsyncBounded(path, _cancellation.Token);
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _cancellation.Cancel();
        _ = Load.ContinueWith(task => { if (task.IsCompletedSuccessfully) task.Result?.Dispose(); _cancellation.Dispose(); }, TaskScheduler.Default);
    }
}
