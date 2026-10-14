using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Render;

internal static class ModalPreviewChecks
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool ok, string message)
        {
            checks++;
            if (!ok) throw new InvalidOperationException("modal ownership: " + message);
            Console.WriteLine("MODALPREVIEW PASS " + message);
        }
        var background = new HunterStand { Width = 240, Height = 160 };
        var overlays = new PrimeOverlayHost();
        var layout = new Grid();
        layout.Children.Add(background);
        layout.Children.Add(overlays);
        var window = new Window { Width = 1280, Height = 720, Content = layout };
        bool oldWanted = LauncherHunter.Wanted;
        Func<bool>? oldCanPresent = LauncherHunter.CanPresent;
        try
        {
            window.Show();
            void Settle()
            {
                for (int i = 0; i < 4; i++)
                { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
            }
            Settle();
            Check(background.CanPresentPreview(), "background eligible before a modal opens");
            var screen = new CreateServerScreen(Array.Empty<string>(), discoverHosts: false);
            overlays.Show(screen);
            Settle();
            var owner = screen.GetVisualDescendants().OfType<HunterStand>().Single();
            Check(owner.CanPresentPreview(), "active modal stand eligible in its actual visual tree");
            Check(!background.CanPresentPreview(), "background presentation guard rejects active modal");
            Check(ShellModalPreviewCheck.CreateLobby(window, true, owner.CanPresentPreview,
                true, true, out _), "oracle accepts modal-owned request and draw");
            Check(ShellModalPreviewCheck.CreateLobby(window, true, null,
                false, false, out _), "oracle permits the existing portrait fallback");
            Check(!ShellModalPreviewCheck.CreateLobby(window, true, background.CanPresentPreview,
                true, true, out _), "oracle rejects background-owned native request/draw");
            Check(!ShellModalPreviewCheck.CreateLobby(window, true, () => true,
                true, true, out _), "oracle rejects an unowned true predicate");
            Check(!ShellModalPreviewCheck.CreateLobby(window, false, owner.CanPresentPreview,
                true, true, out _), "oracle requires the actual open overlay");

            // Drive the existing production publish guard, not a copied policy.
            LauncherHunter.Wanted = true;
            typeof(HunterStand).GetMethod("Beat", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(background, null);
            Check(!LauncherHunter.Wanted, "background heartbeat cannot publish under active modal");
            LauncherHunter.Wanted = true;
            LauncherHunter.CanPresent = background.CanPresentPreview;
            // Its existing guard must return before accessing the window or GL.
            LauncherHunter.Draw(null!, 1280, 720);
            Check(!LauncherHunter.Drawn, "actual native draw guard rejects background without GL");
            owner.IsVisible = false;
            Check(!ShellModalPreviewCheck.CreateLobby(window, true, owner.CanPresentPreview,
                true, true, out _), "hidden modal stand cannot own a published draw");
            owner.IsVisible = true;
            layout.Children.Remove(overlays);
            Check(!owner.CanPresentPreview(), "detached modal stand cannot present");
            Check(!ShellModalPreviewCheck.CreateLobby(window, true, owner.CanPresentPreview,
                true, true, out _), "oracle rejects stale owner after modal detachment");
            Console.WriteLine("MODALPREVIEW PASS " + checks + " assertions");
        }
        finally
        {
            window.Close();
            LauncherHunter.Wanted = oldWanted;
            LauncherHunter.CanPresent = oldCanPresent;
        }
    }
}
