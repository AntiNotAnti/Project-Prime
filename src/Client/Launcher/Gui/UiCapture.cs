using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Mods.UI.AppShell;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.Screens;
using MphRead.Mods.UI.State;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>Headless layout captures for the active persistent application shell.</summary>
    internal static class UiCapture
    {
        private static readonly Size[] _acceptanceViewports =
        [
            new(1280, 720),
            new(1920, 1080),
            new(2560, 1440),
            new(3440, 1440),
            new(360, 640),
            new(768, 1024)
        ];

        public static int Run(string directory)
        {
            if (!GuiLauncher.EnsureSetup())
            {
                Console.WriteLine("[uishot] no Avalonia backend on this machine; nothing captured");
                return 1;
            }
            Directory.CreateDirectory(directory);
            int written = 0;
            Dispatcher.UIThread.Invoke(() =>
            {
                foreach ((string name, Control view, Size size) in Screens())
                {
                    string path = Path.Combine(directory, $"{name}.png");
                    if (Capture(view, path, size))
                    {
                        written++;
                        Console.WriteLine($"[uishot] {path}");
                    }
                }
            });
            Console.WriteLine($"[uishot] {written} screen(s) written to {directory}");
            return written > 0 ? 0 : 1;
        }

        private static IEnumerable<(string, Control, Size)> Screens()
        {
            foreach ((string name, UiRoute route) in new[]
            {
                ("home", UiRoute.Home),
                ("play", UiRoute.Play),
                ("serverbrowser", UiRoute.ServerBrowser),
                ("private-match", UiRoute.PrivateMatch),
                ("hunter-license", UiRoute.HunterLicense),
                ("replays", UiRoute.Replays),
                ("settings", UiRoute.Settings),
                ("postmatch", UiRoute.PostMatch),
                ("account", UiRoute.Account)
            })
            {
                UiScreenServices? services = route == UiRoute.PostMatch
                    ? CapturePostMatchServices() : null;
                foreach (Size size in _acceptanceViewports)
                    yield return ($"{name}-{Label(size)}", CreateShell(route, services), size);
            }

            foreach ((string name, string mode, bool teams) in new[]
            {
                ("lobby-ffa-full-observers", "Battle", false),
                ("lobby-teams-full-observers", "TeamBattle", true)
            })
            {
                UiScreenServices services = CaptureServices(LobbySnapshot(mode, teams));
                foreach (Size size in _acceptanceViewports)
                    yield return ($"{name}-{Label(size)}", CreateShell(UiRoute.Lobby, services), size);
            }

            yield return ("pausemenu-1280x720", new PauseMenuView(offerWindowMode: true),
                new Size(1280, 720));
            yield return ("pausemenu-560x320", new PauseMenuView(offerWindowMode: true),
                new Size(560, 320));
        }

        private static AppShellView CreateShell(UiRoute route, UiScreenServices? services = null)
        {
            var state = new AppShellState();
            state.Router.Replace(route);
            return new AppShellView(state,
                new UiScreenFactory(state.Router, services ?? new UiScreenServices()));
        }

        private static string Label(Size size) => $"{(int)size.Width}x{(int)size.Height}";

        private static UiScreenServices CaptureServices(UiLobbySnapshot snapshot)
            => new() { Lobby = new CaptureLobbyController(snapshot) };

        private static UiScreenServices CapturePostMatchServices()
        {
            var rows = ImmutableArray.CreateBuilder<UiPostMatchRow>(8);
            for (int index = 0; index < 8; index++)
                rows.Add(new UiPostMatchRow(index + 1, $"Hunter {index + 1}",
                    index % 2 == 0 ? "Samus" : "Noxus", index % 2, Bot: index >= 6,
                    Points: 7 - index, Kills: 12 - index, Deaths: 3 + index, Assists: index,
                    Damage: 1200 - index * 75, Headshots: index + 1,
                    ObjectivePrimary: index, ObjectiveSecondary: 0, ObjectiveTertiary: 0));
            var summary = new UiPostMatchSummary(42, 8, 9, TimeSpan.FromMinutes(7),
                "Sanctorus", "TeamBattle", "ScoreLimit", rows.MoveToImmutable(),
                RatingUpdateState.Updated, RatingDelta: 18, RatingPoints: 1518);
            return new UiScreenServices { PostMatch = new CapturePostMatchController(summary) };
        }

        private static UiLobbySnapshot LobbySnapshot(string mode, bool teams)
        {
            var members = ImmutableArray.CreateBuilder<UiLobbyMember>(24);
            for (int index = 0; index < 8; index++)
            {
                members.Add(new UiLobbyMember($"Hunter {index + 1}",
                    index % 2 == 0 ? "Samus" : "Noxus", teams ? index % 2 : 0,
                    Ready: index < 4, Loading: false, Observer: false,
                    DisconnectedGrace: false, Bot: index >= 6, Host: index == 0,
                    Admin: index == 0, RatingEligible: index < 6, PingMs: 20 + index,
                    Local: index == 0));
            }
            for (int index = 0; index < 16; index++)
            {
                members.Add(new UiLobbyMember($"Observer {index + 1}", "Trace", 0,
                    Ready: false, Loading: false, Observer: true,
                    DisconnectedGrace: index == 0, Bot: false, Host: false, Admin: false,
                    RatingEligible: false, PingMs: 40 + index));
            }
            return new UiLobbySnapshot(42, 7, "Open", "Persistent", "Sanctorus", mode,
                "Classic · 8 players · 07:00 · FF Off · Radar On · Spawn Default · Late join Off",
                members.MoveToImmutable(),
                ImmutableArray.Create(new UiLobbyChatLine("Host", "Welcome Hunters.", false,
                    Host: true, Admin: true)),
                new Dictionary<UiLobbyAction, string>());
        }

        private sealed class CaptureLobbyController(UiLobbySnapshot snapshot)
            : ILobbyScreenController
        {
            public UiLobbySnapshot? Snapshot { get; } = snapshot;
            public event Action? Changed { add { } remove { } }

            public Task<UiActionResult> RequestAsync(UiLobbyCommand command,
                uint expectedRevision, CancellationToken cancellationToken)
                => Task.FromResult(UiActionResult.Success());
        }

        private sealed class CapturePostMatchController(UiPostMatchSummary summary)
            : IPostMatchScreenController
        {
            public UiPostMatchSummary? Summary { get; } = summary;
            public event Action? Changed { add { } remove { } }
            public Task<UiPostMatchSummary> AwaitRatingAsync(uint matchId,
                CancellationToken cancellationToken) => Task.FromResult(Summary!);
            public Task<UiActionResult> InvokeAsync(PostMatchAction action,
                CancellationToken cancellationToken) => Task.FromResult(UiActionResult.Success());
        }

        private static bool Capture(Control view, string path, Size size)
        {
            Window? window = null;
            try
            {
                window = new Window
                {
                    Width = size.Width,
                    Height = size.Height,
                    Background = GuiTheme.PanelBrush,
                    RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
                    SystemDecorations = SystemDecorations.None,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Position = new PixelPoint(-4000, -4000),
                    Content = view
                };
                window.Show();
                for (int i = 0; i < 8; i++) Dispatcher.UIThread.RunJobs();
                window.Measure(size);
                window.Arrange(new Rect(size));
                Dispatcher.UIThread.RunJobs();
                // The shell focuses its initial action for keyboard/controller
                // use. Move focus to the shell root before the capture so a
                // long fixture cannot auto-scroll away from its top edge.
                view.Focus();
                ScrollViewer[] scrollers = window.GetVisualDescendants().OfType<ScrollViewer>().ToArray();
                foreach (ScrollViewer scroller in scrollers)
                    scroller.Offset = default;
                Dispatcher.UIThread.RunJobs();
                foreach (ScrollViewer scroller in scrollers)
                    scroller.Offset = default;
                var bitmap = new RenderTargetBitmap(
                    new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(path);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[uishot] {Path.GetFileName(path)} could not be rendered: {ex.Message}");
                return false;
            }
            finally
            {
                window?.Close();
            }
        }
    }
}
