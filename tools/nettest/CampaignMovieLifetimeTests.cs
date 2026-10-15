using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Campaign;

namespace MphRead.NetTest;

/// <summary>
/// No assets, AL, GL or Scene are created. Verify cancellation and retirement
/// under the same worker owner used by real VX films, including delayed tasks,
/// faults, repeated stops and independently scoped frame buffers.
/// </summary>
internal static class CampaignMovieLifetimeTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("CAMP MOVIE LIFETIME FAIL " + name);
            checks++;
            Console.WriteLine("CAMP MOVIE LIFETIME PASS " + name);
        }

        try
        {
            var topStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var bottomStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var bottomRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var a = CampaignMovieDecodeSession.StartWorkers(async token =>
            {
                topStarted.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, token);
            }, async _ =>
            {
                bottomStarted.TrySetResult(true);
                await bottomRelease.Task;
            });
            Check(Task.WaitAll(new[] { topStarted.Task, bottomStarted.Task }, TimeSpan.FromSeconds(3)),
                "both decoder workers start independently");
            Check(!a.IsCancellationRequested && !a.IsQuiescent && !a.IsFaulted,
                "live session stays owned while both workers are pending");
            var b = CampaignMovieDecodeSession.StartWorkers(
                token => Task.Delay(Timeout.Infinite, token));
            Check(!ReferenceEquals(a.Top, b.Top) && !ReferenceEquals(a.Top, a.Bottom)
                && b.Bottom == null, "new and chained films have separate decoder buffers");
            a.Top.UseStaticBuffers = false;
            Check(b.Top.UseStaticBuffers && a.Bottom!.UseStaticBuffers,
                "export-style buffer policy cannot alter a concurrent film's decoder");
            long before = Environment.TickCount64;
            a.Cancel();
            a.Cancel();
            Check(Environment.TickCount64 - before < 250 && a.IsCancellationRequested,
                "repeated cancellation never waits for an uncooperative background worker");
            Check(!a.IsQuiescent, "in-flight bottom worker is not falsely reported retired");
            Check(!b.IsCancellationRequested && !b.IsQuiescent,
                "cancelling previous movie cannot stop the next decoder");
            bottomRelease.SetResult(true);
            Check(SpinWait.SpinUntil(() => a.IsQuiescent, TimeSpan.FromSeconds(4)),
                "both workers eventually retire after the delayed completion");
            b.Cancel();
            Check(SpinWait.SpinUntil(() => b.IsQuiescent, TimeSpan.FromSeconds(4)),
                "chained movie retires on cancellation without sharing token");

            var faulty = CampaignMovieDecodeSession.StartWorkers(
                _ => Task.FromException(new InvalidDataException("corrupt VX")));
            Check(SpinWait.SpinUntil(() => faulty.IsFaulted, TimeSpan.FromSeconds(4)),
                "decoder fault becomes visible to the scene-owned fallback");
            faulty.Cancel();
            Check(SpinWait.SpinUntil(() => faulty.IsQuiescent, TimeSpan.FromSeconds(4)),
                "fault retirement completes and observes exception");

            bool invalidPath = false;
            try { CampaignMovieDecodeSession.Start("", null); }
            catch (ArgumentException) { invalidPath = true; }
            Check(invalidPath, "invalid film paths cannot start orphan workers");

            Console.WriteLine($"CAMP MOVIE LIFETIME PASS total={checks}: scene-safe worker cancellation without native device");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("CAMP MOVIE LIFETIME FAIL " + ex);
            return 1;
        }
    }
}
