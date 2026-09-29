#if MPHREAD_SHELL
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MphRead.Mods.Launcher.Gui;

internal static class ReplayUiCheck
{
    internal static int Run()
    {
        try
        {
            if (!GuiLauncher.EnsureSetup(requireDisplay: false)) throw new InvalidOperationException("UI initialization failed.");
            Dispatcher.UIThread.Invoke(() =>
            {
                using var theatre = new TheatreWorkspace(manageStorage: false);
                var window = new Window { Width = 1280, Height = 720, Content = theatre };
                window.Show();
                try
                {
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        theatre.ShowEditor(() => { }, () => { });
                        window.UpdateLayout();
                        Dispatcher.UIThread.RunJobs();
                        if (!theatre.EditorActive || !theatre.GetVisualDescendants().OfType<ReplayViewport>().Any(v => v.Bounds.Width > 0 && v.Bounds.Height > 0))
                            throw new InvalidOperationException("Replay editor did not attach a visible viewport.");
                        theatre.CloseEditor();
                        window.UpdateLayout();
                        if (theatre.EditorActive) throw new InvalidOperationException("Replay editor did not close.");
                    }
                }
                finally { window.Close(); }
            });
            Console.WriteLine("[replayuicheck] PASS: studio opens, lays out, closes and reopens.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[replayuicheck] FAIL: " + ex); return 1; }
    }
}
#endif
