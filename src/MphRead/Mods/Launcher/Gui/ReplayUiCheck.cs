#if MPHREAD_SHELL
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MphRead.Mods.Launcher.Gui;

internal static class ReplayUiCheck
{
    private static void CheckLaunchLifecycle(TheatreWorkspace theatre, Window window)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var select = typeof(TheatreWorkspace).GetMethod("Select", flags)!;
        var watch = typeof(TheatreWorkspace).GetMethod("WatchAsync", flags)!;
        var button = (HubNavButton)typeof(TheatreWorkspace).GetField("_watch", flags)!.GetValue(theatre)!;
        var status = (TextBlock)typeof(TheatreWorkspace).GetField("_status", flags)!.GetValue(theatre)!;
        void Select(string path) => select.Invoke(theatre, new object[] { path });
        Task Watch() => (Task)watch.Invoke(theatre, null)!;
        void Pump(Task task)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(10))
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
            if (!task.IsCompleted) throw new Exception("Replay launch did not complete.");
            task.GetAwaiter().GetResult();
        }
        // Initial control binding can queue a search debounce while the empty
        // fixture library attaches. Observe its completion before synthetic
        // selection, so it cannot legitimately clear that later selection.
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var startupSearch = (DispatcherTimer)typeof(TheatreWorkspace).GetField("_searchTimer", flags)!.GetValue(theatre)!;
        if (startupSearch.IsEnabled)
        {
            var frame = new DispatcherFrame();
            EventHandler settled = (_, _) => frame.Continue = false;
            startupSearch.Tick += settled;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var watchdog = deadline.Token.Register(() => Dispatcher.UIThread.Post(() => frame.Continue = false));
            try { Dispatcher.UIThread.PushFrame(frame); }
            finally { startupSearch.Tick -= settled; }
        }
        if (startupSearch.IsEnabled) throw new Exception("Replay library startup search did not settle.");
        string missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".ppdemo");
        Select(missing);
        Pump(Watch());
        if (!button.IsEnabled || button.Label != "WATCH REPLAY" || String.IsNullOrEmpty(status.Text))
            throw new Exception("Failed replay launch did not restore controls and explain failure.");

        // A minimal legacy header is sufficient for the launch metadata probe.
        System.IO.File.WriteAllBytes(missing, DemoFile.Magic.Concat(new byte[] { 1, NetConfig.ProtocolVersion }).ToArray());
        int launches = 0;
        EventHandler<LaunchPlan> launch = (_, _) => { launches++; throw new InvalidOperationException("Launch check failure"); };
        theatre.Launched += launch;
        try
        {
            Pump(Watch());
            if (launches != 1 || !button.IsEnabled || button.Label != "WATCH REPLAY"
                || status.Text?.Contains("LAUNCH CHECK FAILURE") != true)
                throw new Exception("Launch exception stranded the library.");
        }
        finally { theatre.Launched -= launch; System.IO.File.Delete(missing); }

        // Occupy both disk workers so selection/navigation races are deterministic.
        for (int scenario = 0; scenario < 2; scenario++)
        {
            using var release = new ManualResetEventSlim();
            using var started = new CountdownEvent(2);
            Task<bool> Block() => ReplayStorageJobs.Run(() => { started.Signal(); release.Wait(); return true; });
            var workers = new[] { Block(), Block() };
            try
            {
                if (!started.Wait(TimeSpan.FromSeconds(10))) throw new Exception("Disk workers did not start.");
                Select(missing);
                status.Text = "";
                Task pending = Watch();
                if (!Watch().IsCompleted || button.IsEnabled) throw new Exception("Duplicate replay launch was not suppressed.");
                if (scenario == 0) Select(missing + ".other");
                else window.Content = null;
                release.Set();
                Pump(pending);
                if (status.Text != "") throw new Exception("Stale replay launch changed the current screen.");
                if (!button.IsEnabled || button.Label != "WATCH REPLAY")
                    throw new Exception("Cancelled replay launch left controls stuck.");
            }
            finally { release.Set(); Task.WaitAll(workers); window.Content = theatre; }
        }
    }

    internal static int Run()
    {
        try
        {
            if (!GuiLauncher.EnsureSetup(requireDisplay: false)) throw new InvalidOperationException("UI initialization failed.");
            Dispatcher.UIThread.Invoke(() =>
            {
                static void Require(bool value, string message)
                { if (!value) throw new InvalidOperationException(message); }
                GameStudioRouteChecks.QuickReplayControls(Require);
                GameStudioRouteChecks.ReplayInputOwnership(Require);
                GameStudioRouteChecks.ReplayViewportNavigation(Require);
                GameStudioRouteChecks.TheatreQuickSurface(Require);
                using var theatre = new TheatreWorkspace(manageStorage: false);
                var window = new Window { Width = 1280, Height = 720, Content = theatre };
                window.Show();
                try
                {
                    CheckLaunchLifecycle(theatre, window);
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        theatre.ShowEditor(() => { }, () => { });
                        window.UpdateLayout();
                        Dispatcher.UIThread.RunJobs();
                        if (!theatre.EditorActive || !theatre.GetVisualDescendants().OfType<ReplayViewport>().Any(v => v.Bounds.Width > 0 && v.Bounds.Height > 0)
                            || !theatre.GetVisualDescendants().OfType<ReplayQuickControlsView>().Any()
                            || theatre.GetVisualDescendants().Any(view => view.GetType().Name == "ReplayControlsView"))
                            throw new InvalidOperationException("Game replay did not attach a visible viewport with quick transport controls.");
                        theatre.CloseEditor();
                        window.UpdateLayout();
                        if (theatre.EditorActive) throw new InvalidOperationException("Replay editor did not close.");
                    }
                }
                finally { window.Close(); }
            });
            Console.WriteLine("[replayuicheck] PASS: launch failure recovery, duplicate activation, selection/navigation races; quick viewing controls and viewport open, lay out, close and reopen; viewport input precedes game navigation; camera authoring remains in Studio.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[replayuicheck] FAIL: " + ex); return 1; }
    }
}
#endif
