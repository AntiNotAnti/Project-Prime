using System.Reflection;
using System.Runtime.ExceptionServices;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio;
using ProjectPrime.Studio.Replay;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;

internal static partial class Program
{
    private static async Task CheckReplayPreparationWaitAsync(StudioWindow window, string source, StudioPaths paths)
    {
        var prior = window.Documents.ActiveDocument;
        Type workerType = typeof(MphRead.Mods.StudioReplay.StudioReplayPlayer).Assembly.GetType("MphRead.Mods.Network.ReplayPreparationJob")!;
        var workers = (SemaphoreSlim)workerType.GetField("Workers",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        using var cancellation = new CancellationTokenSource();
        Task? sourceWork = null; ReplayStudioDocument? fresh = null; ValueTask disposal = default;
        int held = 0;
        bool disposalStarted = false; Exception? failure = null;
        try
        {
            for(int index=0;index<2;index++)
            {
                if(!workers.Wait(TimeSpan.FromSeconds(3)))throw new TimeoutException("Actual source admission slot did not become available.");
                held++;
            }
            Check(workers.CurrentCount==0, "source worker barrier holds both actual admission permits before initial preparation reads files");
            fresh = new(StudioDocumentKind.Replay, source, paths);
            object preparation = fresh.Session!.Player.GetType().GetField("_preparation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fresh.Session.Player)!;
            sourceWork = (Task)preparation.GetType().GetProperty("Completion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preparation)!;
            var wait = fresh.Session!.Player.GetType().GetMethod("WaitForPreparationAsync")
                ?? throw new MissingMethodException("Actual source preparation readiness API is missing.");
            Task preparing;
            try { preparing = (Task)wait.Invoke(fresh.Session.Player, [cancellation.Token])!; }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
            Check(!preparing.IsCompleted && !fresh.Session.Player.Status.Ready,
                "initial source readiness awaits the actual outstanding preparation worker before any scene adoption");
            cancellation.Cancel(); bool rejected = false;
            try { await preparing; } catch (OperationCanceledException) { rejected = true; }
            Check(rejected && ReferenceEquals(window.Documents.ActiveDocument, prior) && !window.Documents.Documents.Contains(fresh),
                "canceling the initial source wait leaves the previous actual editor tab intact and never adopts the fresh document");
            disposal = fresh.DisposeAsync(); disposalStarted = true;
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try
            {
                if (fresh is not null && !disposalStarted) { disposal = fresh.DisposeAsync(); disposalStarted = true; }
            }
            finally { if(held>0)workers.Release(held); }
        }
        await disposal;
        if(sourceWork is not null)
        {
            try { await sourceWork; } catch(OperationCanceledException) { }
            string snapshots = Path.Combine(paths.BuildCacheDirectory,"replay","sources");
            MapDiskCache.Prune(snapshots,0,TimeSpan.Zero);
            Check(!Directory.Exists(snapshots)||!Directory.EnumerateFiles(snapshots,"*.ppdemo",SearchOption.AllDirectories).Any(),
                "canceled unadopted source preparation releases all snapshot pins before zero-budget pruning");
        }
        if (fresh is not null)
        {
            await fresh.DisposeAsync();
            Check(fresh.State == StudioDocumentState.Closed && ReferenceEquals(window.Documents.ActiveDocument, prior),
                "canceled initial preparation drains its fresh document while preserving the previous editor owner");
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
