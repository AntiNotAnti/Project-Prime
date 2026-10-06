using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using ProjectPrime.Studio;
using ProjectPrime.Studio.Jobs;
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;

internal static partial class Program
{
    private static async Task CheckHeldAssetThumbnailLifecycleAsync(StudioWindow window, StudioPaths paths, string texture, string output)
    {
        var document = new MapStudioDocument(paths, window, window.Jobs);
        document.NewProject("HELD_THUMBNAIL_FIXTURE", example: true); window.Documents.Add(document);
        object screen = document.Host.GetType().GetField("_screen", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(document.Host)!;
        var gate = (SemaphoreSlim)screen.GetType().GetField("_thumbnailGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(screen)!;
        int held = 0;
        try
        {
            await MapFacadeAsync(document, "ImportAssetAsync", texture, CancellationToken.None);
            await MapFacadeAsync(document, "WaitForAssetThumbnailsAsync");
            string definition = document.Host.Document!.Project.Definition.Serialize();
            var state = document.Host.Document.CurrentStateId; int history = document.Host.Document.History.CommandCount;
            await HoldPermitsAsync();
            var previous = window.Jobs.Jobs.Select(job => job.Id).ToHashSet();
            document.Host.OpenAssetBrowser(); var browser = document.AssetBrowserWindow!;
            SetAssetSearch(browser, "AcceptanceTexture HD"); PumpLayout(browser);
            StudioJob[] waiting = await WaitForPreviewJobsAsync(previous);
            Check(waiting.Length > 0 && waiting.All(job => job.State == StudioJobState.Running),
                "real loaded-thumbnail jobs remain observable while both canonical preparation permits are held");
            Check(!await window.Documents.RequestCloseAsync(document, _ => Task.FromResult(StudioCloseDecision.Cancel), _ => Task.FromResult<string?>(null))
                && document.State == StudioDocumentState.Open && waiting.All(job => job.State == StudioJobState.Running),
                "dirty close preflight Cancel retains its document and does not pause admitted thumbnail requests");

            using (var cancellation = new CancellationTokenSource())
            {
                var canceledIds = waiting.Select(job => job.Id).ToHashSet();
                void CancelAfterPreviewStops()
                {
                    if (window.Jobs.Jobs.Any(job => canceledIds.Contains(job.Id) && job.State == StudioJobState.Cancelled)) cancellation.Cancel();
                }
                window.Jobs.Changed += CancelAfterPreviewStops;
                bool canceled = false;
                try
                {
                    try { await document.CloseAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (OperationCanceledException) { canceled = true; }
                }
                finally { window.Jobs.Changed -= CancelAfterPreviewStops; cancellation.Cancel(); }
                Check(canceled && document.State == StudioDocumentState.Open && window.Documents.Documents.Contains(document)
                    && document.Host.Document.Project.Definition.Serialize() == definition
                    && document.Host.Document.CurrentStateId == state && document.Host.Document.History.CommandCount == history,
                    "caller cancellation after preview pause drains held work and retains every authored field and history state");
            }
            ReleasePermits(); await MapFacadeAsync(document, "WaitForAssetThumbnailsAsync"); PumpLayout(browser);
            Check(browser.GetVisualDescendants().OfType<Image>().Any(image => image.Source is not null)
                && window.Jobs.Jobs.Any(job => !previous.Contains(job.Id) && job.Title == "Prepare map asset thumbnail" && job.State == StudioJobState.Completed),
                "canceled close renews preview ownership and attached real thumbnail pixels finish on the retained document");
            using (var image = browser.CaptureRenderedFrame())
                image?.Save(Path.Combine(output, "map-studio-thumbnail-preflight-resumed.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

            await HoldPermitsAsync(); previous = window.Jobs.Jobs.Select(job => job.Id).ToHashSet();
            SetAssetSearch(browser, "no acceptance asset matches"); SetAssetSearch(browser, "AcceptanceTexture HD"); PumpLayout(browser);
            waiting = await WaitForPreviewJobsAsync(previous);
            Check(waiting.Length > 0, "successful close fixture admits fresh actual previews before releasing its native browser");
            bool closed = await window.Documents.RequestCloseAsync(document, _ => Task.FromResult(StudioCloseDecision.Discard),
                _ => Task.FromResult<string?>(null)).WaitAsync(TimeSpan.FromSeconds(5));
            // Production disposal owns this semaphore now. No permit release is
            // needed or valid after its successful resource teardown.
            held = 0;
            Check(closed && document.State == StudioDocumentState.Closed && document.AssetBrowserWindow is null
                && !window.Documents.Documents.Contains(document) && waiting.All(job => job.State == StudioJobState.Cancelled),
                "actual discard closes and drains thumbnail requests without waiting for held permits, releasing owned browser and document");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Held thumbnail primary failure: " + error); throw;
        }
        finally
        {
            ReleasePermits();
            if (window.Documents.Documents.Contains(document))
                await window.Documents.RequestCloseAsync(document, _ => Task.FromResult(StudioCloseDecision.Discard), _ => Task.FromResult<string?>(null));
            await document.DisposeAsync();
        }

        async Task HoldPermitsAsync()
        {
            await MapFacadeAsync(document, "WaitForAssetThumbnailsAsync");
            for (int i = 0; i < 2; i++) { await gate.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5)); held++; }
        }
        void ReleasePermits()
        {
            while (held > 0) { held--; try { gate.Release(); } catch (ObjectDisposedException) { held = 0; } }
        }
        async Task<StudioJob[]> WaitForPreviewJobsAsync(HashSet<Guid> previous)
        {
            var wait = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                var jobs = window.Jobs.Jobs.Where(job => !previous.Contains(job.Id) && job.Title == "Prepare map asset thumbnail"
                    && job.State == StudioJobState.Running).ToArray();
                if (jobs.Length > 0) return jobs;
                await Task.Delay(1); PumpLayout(document.AssetBrowserWindow!);
            } while (wait.Elapsed < TimeSpan.FromSeconds(5));
            throw new TimeoutException("Actual browser did not admit a held thumbnail request.");
        }
    }
}
