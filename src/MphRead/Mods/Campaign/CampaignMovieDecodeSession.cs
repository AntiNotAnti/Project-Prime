using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Formats;

namespace MphRead.Mods.Campaign
{
    /// <summary>
    /// One movie's pair of private VX decoders. Only decoding runs in a worker.
    /// No worker captures Scene, mutates a scene image, calls OpenAL, or sets a
    /// fade. Cancellation never waits on the game/render thread; the old
    /// decoders remain owned by their tasks until those tasks finish.
    /// </summary>
    internal sealed class CampaignMovieDecodeSession
    {
        private readonly CancellationTokenSource _cancel = new();
        private int _stopped;

        public VxDecoder Top { get; }
        public VxDecoder? Bottom { get; }
        public Task TopTask { get; }
        public Task? BottomTask { get; }
        public CancellationToken Token { get; }

        public bool IsCancellationRequested => Volatile.Read(ref _stopped) != 0;
        public bool IsQuiescent => TopTask.IsCompleted && (BottomTask?.IsCompleted ?? true);
        public bool IsFaulted => TopTask.IsFaulted || BottomTask?.IsFaulted == true;

        private CampaignMovieDecodeSession(VxDecoder top, VxDecoder? bottom,
            Func<CancellationToken, Task> decodeTop, Func<CancellationToken, Task>? decodeBottom)
        {
            Top = top;
            Bottom = bottom;
            CancellationToken token = _cancel.Token;
            Token = token;
            TopTask = Task.Run(() => decodeTop(token), token);
            if (decodeBottom != null)
                BottomTask = Task.Run(() => decodeBottom(token), token);
        }

        public static CampaignMovieDecodeSession Start(string topPath, string? bottomPath)
        {
            ArgumentException.ThrowIfNullOrEmpty(topPath);
            var top = new VxDecoder();
            top.Reset();
            VxDecoder? bottom = null;
            if (bottomPath != null)
            {
                bottom = new VxDecoder();
                bottom.Reset();
            }
            // Capture only path, decoder and token. A cancelled previous film
            // can never mutate a new film, even in a different Scene.
            VxDecoder? second = bottom;
            return new CampaignMovieDecodeSession(top, second,
                token => top.Decode(topPath, token: token),
                second == null ? null : token => second.Decode(bottomPath!, token: token));
        }

        // Asset-free lifetime regression fixture. Production always uses Start.
        internal static CampaignMovieDecodeSession StartWorkers(
            Func<CancellationToken, Task> top, Func<CancellationToken, Task>? bottom = null)
        {
            ArgumentNullException.ThrowIfNull(top);
            return new CampaignMovieDecodeSession(new VxDecoder(),
                bottom == null ? null : new VxDecoder(), top, bottom);
        }

        public void Cancel()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
            _cancel.Cancel();
            CancellationTokenSource owner = _cancel;
            Task retirement = BottomTask == null ? TopTask : Task.WhenAll(TopTask, BottomTask);
            // Observe faults and release the token only after both workers
            // retire. This continuation owns *no Scene* and never calls GL/AL.
            _ = retirement.ContinueWith((task, state) =>
            {
                _ = task.Exception;
                ((CancellationTokenSource)state!).Dispose();
            }, owner, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
