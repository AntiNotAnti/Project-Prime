using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class RandomMatchAcceptance
{
    private static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); Console.WriteLine("RANDOM MATCH PASS " + label); }
    private static bool Flag(RmlUiMenuSnapshot snapshot, string key) => snapshot.Bindings[key].Boolean;
    private static void Pump(RmlUiMenuController controller, Func<bool> done)
    {
        for (int i = 0; i < 200; i++) { controller.Tick(); if (done()) return; Thread.Sleep(1); }
        throw new TimeoutException("Random search fixture did not progress.");
    }
    private sealed class DelayGate
    {
        internal int Calls;
        internal CancellationToken Token;
        internal TaskCompletionSource Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Wait(CancellationToken token)
        {
            Interlocked.Increment(ref Calls); Token = token;
            var pending = Pending;
            return pending.Task.WaitAsync(token);
        }
        internal void Release()
        {
            var old = Pending; Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); old.TrySetResult();
        }
    }
    internal static void Run()
    {
        var a = new RmlUiServerCandidate("A", "a:1", 1, 8, 10, true);
        var b = new RmlUiServerCandidate("B", "b:1", 0, 8, 20, true);
        var rows = new[] { a with { Compatible = false }, a with { Players = 8 },
            a with { Capacity = 0 }, a, a, b };
        Check(RmlUiRandomMatch.Choose(rows, count => { Check(count == 2, "eligible endpoints deduplicated"); return 1; }) == b
            && RmlUiRandomMatch.Choose(rows, _ => 0) == a, "random index reaches each eligible open endpoint");

        var delay = new DelayGate(); int scans = 0, joins = 0;
        var services = new Stub
        {
            RandomScan = _ => { Interlocked.Increment(ref scans); return Task.FromResult<RmlUiServerCandidate?>(null); },
            RandomJoin = _ => { Interlocked.Increment(ref joins); return Task.FromResult(new RmlUiJoinResult(false, default, "full")); }
        };
        long clock = 1000;
        using (var controller = new RmlUiMenuController(services, delay.Wait, () => clock))
        {
            controller.Dispatch(RmlUiIntentKind.PlayQuick);
            Check(scans == 1 && delay.Calls == 1 && controller.Busy, "empty directory waits and keeps search active");
            clock += 5000;
            var searching = controller.Tick();
            Check(Flag(searching, "play_random_search") && searching.Bindings["play_search_elapsed"].Text.Contains("5s"), "visible elapsed search state");
            Check(!controller.Dispatch(RmlUiIntentKind.PlayBrowse) && !controller.Dispatch(RmlUiIntentKind.PlayCreateOpen)
                && !controller.Dispatch(RmlUiIntentKind.PlayJoin, address: "other:1"), "competing actions cannot replace search");
            Check(!controller.Dispatch(RmlUiIntentKind.OpenStudio)
                && !controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.HunterLicense)
                && !controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Community)
                && !controller.Dispatch(RmlUiIntentKind.LicenseOpen)
                && controller.Joining && scans == 1,
                "native header destinations remain locked until search is cancelled or admitted");
            delay.Release(); Pump(controller, () => scans == 2);
            Check(joins == 0, "empty directory continues without admission");
            controller.Dispatch(RmlUiIntentKind.PlayCancel);
            Check(delay.Token.IsCancellationRequested && Flag(controller.Tick(), "play_browser_mode") && !controller.Joining,
                "cancel interrupts retry delay and stays on browser");
        }

        delay = new DelayGate(); scans = joins = 0;
        services = new Stub
        {
            RandomScan = _ => { Interlocked.Increment(ref scans); return Task.FromResult<RmlUiServerCandidate?>(a); },
            RandomJoin = _ => Task.FromResult(++joins == 1 ? new RmlUiJoinResult(false, default, "full")
                : new RmlUiJoinResult(true, new LaunchPlan(), ""))
        };
        using (var controller = new RmlUiMenuController(services, delay.Wait))
        {
            controller.Dispatch(RmlUiIntentKind.PlayQuick);
            Check(joins == 1 && delay.Calls == 1, "admission refusal waits before retry");
            delay.Release(); Pump(controller, () => services.Accepted == 1);
            Check(scans == 2 && joins == 2 && !controller.Joining && !Flag(controller.Tick(), "play_random_search"),
                "successful authoritative handoff terminates search");
        }

        services = new Stub { RandomScan = _ => Task.FromResult<RmlUiServerCandidate?>(a),
            RandomJoin = _ => Task.FromResult(new RmlUiJoinResult(true, new LaunchPlan(), "")) };
        using (var controller = new RmlUiMenuController(services))
        {
            controller.Dispatch(RmlUiIntentKind.PlayQuick); // Successful result is queued, owner has not accepted it.
            controller.Dispatch(RmlUiIntentKind.PlayCancel);
            services.RandomScan = token => Task.Delay(Timeout.Infinite, token)
                .ContinueWith<RmlUiServerCandidate?>(_ => null, token);
            controller.Dispatch(RmlUiIntentKind.PlayQuick);
            controller.Tick();
            Check(services.Accepted == 0 && services.RandomDiscards == 1 && controller.Busy,
                "stale successful admission is discarded without accepting or retiring replacement search");
        }

        var oldScan = new TaskCompletionSource<RmlUiServerCandidate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newScan = new TaskCompletionSource<RmlUiServerCandidate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default; joins = scans = 0;
        services = new Stub { RandomScan = token => { if (++scans == 1) { oldToken = token; return oldScan.Task; } return newScan.Task; },
            RandomJoin = _ => { joins++; return Task.FromResult(new RmlUiJoinResult(true, new LaunchPlan(), "")); } };
        using (var controller = new RmlUiMenuController(services))
        {
            controller.Dispatch(RmlUiIntentKind.PlayQuick);
            controller.Dispatch(RmlUiIntentKind.PlayCancel);
            controller.Dispatch(RmlUiIntentKind.PlayQuick);
            oldScan.SetResult(a);
            Check(oldToken.IsCancellationRequested, "scan cancellation token retired");
            // Old completion is deliberately allowed to arrive after the replacement search.
            for (int i = 0; i < 20; i++) { controller.Tick(); Thread.Sleep(1); }
            Check(joins == 0 && services.Accepted == 0 && controller.Busy, "late cancelled scan cannot join or retire newer search");
            controller.Dispose();
            newScan.SetResult(a);
            for (int i = 0; i < 20; i++) Thread.Sleep(1);
            Check(joins == 0 && services.Accepted == 0, "disposal cancels outstanding discovery without late admission");
        }
    }
}
