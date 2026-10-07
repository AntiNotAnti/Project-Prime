using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using ProjectPrime.Studio;
using ProjectPrime.Studio.Jobs;
using ProjectPrime.Studio.Replay;

internal static partial class Program
{
    private static async Task CheckReplayJobsAsync(StudioWindow window, ReplayStudioDocument document, string output)
    {
        const string title = "Analyze replay movement and combat";
        var player = document.Session!.Player; var world = player.Snapshot();
        var host = document.Host;
        var analyze = host.GetType().GetMethod("AnalyzeAsync") ?? throw new MissingMethodException("Actual replay analysis job API is missing.");
        var analysis = host.GetType().GetProperty("Analysis")!;
        var pending = host.GetType().GetProperty("HasPendingJobs")!;
        await InvokeAsync();
        object? accepted = analysis.GetValue(host);
        Check(accepted is not null && !(bool)pending.GetValue(host)!
            && window.Jobs.Jobs.Last(job => job.Title == title).State == StudioJobState.Completed,
            "actual replay analysis is observable as a completed central job and adopts its owned result");
        bool canceled = false, admissionCanceled = false;
        void CancelAdmission()
        {
            var job = window.Jobs.Jobs.LastOrDefault(job => job.Title == title && job.State == StudioJobState.Running);
            if (job is not null && !admissionCanceled) { admissionCanceled = true; job.Cancel(); }
        }
        window.Jobs.Changed += CancelAdmission;
        try { await InvokeAsync(); }
        catch (OperationCanceledException) { canceled = true; }
        finally { window.Jobs.Changed -= CancelAdmission; }
        Check(admissionCanceled && canceled && ReferenceEquals(accepted, analysis.GetValue(host))
            && !(bool)pending.GetValue(host)! && window.Jobs.Jobs.Last(job => job.Title == title).State == StudioJobState.Cancelled,
            "central cancellation drains actual replay analysis and retains the previously adopted result");
        var after = player.Snapshot();
        Check(after.GameplayHash == world.GameplayHash && after.PresentationHash == world.PresentationHash && after.FullGraphHash == world.FullGraphHash,
            "analysis completion and explicit central cancellation preserve the entire canonical private world");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> controlled = window.Jobs.RunAsync("Jobs window cancellation fixture", async (progress, cancellation) =>
        {
            progress.Report(new(.37, "Controlled UI cancellation")); entered.SetResult();
            await Task.Delay(Timeout.Infinite, cancellation); return true;
        });
        await entered.Task; window.OpenJobsWindow(); Window jobs = window.JobsWindow!;
        try
        {
            PumpLayout(jobs);
            Check(jobs.IsVisible && jobs.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                text.Text?.Contains(title + " · Cancelled", StringComparison.Ordinal) == true)
                && jobs.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.Contains("Controlled UI cancellation", StringComparison.Ordinal) == true),
                "owned Jobs window presents actual replay terminal status and a running job's measured progress detail");
            Button button = jobs.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "Cancel");
            CheckControlBounds(jobs, button, 30, 10, "central Jobs cancel action stays readable and allocated");
            using var image = jobs.CaptureRenderedFrame() ?? throw new InvalidOperationException("Jobs window did not render.");
            image.Save(Path.Combine(output, "replay-studio-background-jobs.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            Click(jobs, button); bool controlCanceled = false;
            try { await controlled; } catch (OperationCanceledException) { controlCanceled = true; }
            Check(controlCanceled && window.Jobs.Jobs.Last(job => job.Title == "Jobs window cancellation fixture").State == StudioJobState.Cancelled,
                "real pointer action in the central Jobs window cancels and drains its owned operation");
        }
        finally
        {
            window.Jobs.Jobs.Last(job => job.Title == "Jobs window cancellation fixture").Cancel();
            try { await controlled; } catch (OperationCanceledException) { }
            jobs.Close();
        }
        Check(window.JobsWindow is null, "closing central Jobs releases its owned window");

        async Task InvokeAsync()
        {
            try { await (Task)analyze.Invoke(host, null)!; }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
        }
    }
}
