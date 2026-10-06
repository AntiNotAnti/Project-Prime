using System.Collections.Concurrent;
using System.Reflection;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio.Replay;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;

internal static partial class Program
{
    private sealed class QueuedReplayContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _pending = new();
        internal int Pending => _pending.Count;
        public override SynchronizationContext CreateCopy() => this;
        public override void Post(SendOrPostCallback callback, object? state) => _pending.Enqueue((callback, state));
        internal void Drain()
        {
            while (_pending.TryDequeue(out var item)) item.Callback(item.State);
        }
    }

    private static async Task CheckReplaySaveContinuationCloseAsync(string source, StudioPaths paths, string data)
    {
        var document = await ReplayStudioDocument.OpenAsync(StudioDocumentKind.Replay, source, paths);
        var previous = document.Session!; var previousHost = document.Host;
        try
        {
            previous.Player.OnGraphicsInitialize(256, 192);
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (!previous.Player.Status.Ready && wait.Elapsed < TimeSpan.FromSeconds(30))
            {
                previous.Player.Advance(TimeSpan.Zero);
                if (previous.Player.Status.State == "Error") throw new InvalidOperationException(previous.Player.Status.Error);
                await Task.Delay(1);
            }
            Check(previous.Player.Status.Ready, "Save As close race uses an actual prepared canonical replay document");
            previous.Player.SetRange(30, 60); previous.Player.Advance(TimeSpan.Zero);
            string immutable = (string)previous.Player.GetType().GetField("_playbackPath", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(previous.Player)!;
            string target = Path.Combine(data, "closed-continuation.ppclip");
            var queued = new QueuedReplayContext(); SynchronizationContext? originalContext = SynchronizationContext.Current;
            Task save;
            try
            {
                SynchronizationContext.SetSynchronizationContext(queued);
                save = document.SaveAsync(target, CancellationToken.None);
            }
            finally { SynchronizationContext.SetSynchronizationContext(originalContext); }
            wait.Restart();
            while (queued.Pending == 0 && !save.IsCompleted && wait.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(1);
            Check(queued.Pending > 0 && !save.IsCompleted && File.Exists(target),
                "canonical Save As worker finishes before its owner UI continuation is deliberately released");

            await document.CloseAsync(CancellationToken.None); await document.DisposeAsync();
            queued.Drain(); bool rejected = false;
            try { await save; }
            catch (ObjectDisposedException) { rejected = true; }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && document.State == StudioDocumentState.Closed && ReferenceEquals(document.Session, previous)
                && ReferenceEquals(document.Host, previousHost) && document.Path == source && !document.CanSave,
                "completed Save As cannot resurrect a closed document or replace its disposed native host");
            MapDiskCache.Prune(Path.GetDirectoryName(Path.GetDirectoryName(immutable)!)!, 0, TimeSpan.Zero);
            Check(!File.Exists(immutable), "closed Save As continuation leaves no hidden replacement preparation or source owner pin");
        }
        finally { await document.DisposeAsync(); }
    }
}
