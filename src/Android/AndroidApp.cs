using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.UI.Adapters;
using MphRead.Mods.UI.AppShell;
using MphRead.Mods.UI.Dialogs;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;

namespace MphRead.Droid
{
    /// <summary>
    /// The Avalonia application on Android.
    ///
    /// A phone has one view rather than a desktop full of windows, so this is a
    /// single view lifetime whose root is the same persistent
    /// <see cref="AppShellView"/> used by desktop.
    ///
    /// What is left here is the front half of the loop the desktop's
    /// <c>GuiLauncher</c> runs: read the settings, ask the screen, start what it
    /// asked for. The difference is that a match is a view swap in
    /// <see cref="MainActivity"/> rather than a window on this thread.
    /// </summary>
    public class AndroidApp : Application
    {
        private DispatcherTimer? _sessionTimer;

        internal static ClientUiRuntime? Runtime { get; private set; }
        internal static AppShellView? Shell => Runtime?.Shell;

        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            base.Initialize();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            {
                Runtime = BuildRuntime();
                Runtime.LaunchRequested += (_, plan) =>
                {
                    if (plan.Kind == LaunchKind.None) MainActivity.Instance?.Finish();
                    else MainActivity.Instance?.StartMatch(plan);
                };
                single.MainView = Runtime.Shell;
                _sessionTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16),
                    DispatcherPriority.Background,
                    (_, _) => Runtime?.PollShell());
                _sessionTimer.Start();
            }
            base.OnFrameworkInitializationCompleted();
        }

        private static ClientUiRuntime BuildRuntime()
        {
            LauncherPrefs.Load();
            // Keys, mouse feel, pad bindings and the touch layout. The
            // desktop reads these from ModEntry.TryHandleHeadless, which the
            // head never runs -- so nothing here ever loaded them, and every
            // control the player changed was back to its default the next
            // time the app opened. After LauncherPrefs.Load, since
            // controls.txt sits in the directory that has just been named.
            Mods.InputSettings.Load();
            // After the load, because the head has just pointed the
            // preferences at the app's own data directory -- the package's
            // directory is read-only, and that is also where the log has to
            // go. Same switch, same file, same corner of the same screen as
            // the desktop's.
            Mods.DebugLog.Attach();
            MenuSettings settings = ClientSettings.LoadSettings();
            GameSettings.Apply(settings);
            IReadOnlyList<string> rooms = Array.Empty<string>();
            if (GameFiles.Ready)
            {
                // The room list is read out of the extracted files, and the
                // launcher runs before upstream's own setup check does.
                GameFiles.ApplyPaths();
                rooms = ThumbnailGenerator.MultiplayerRooms();
            }
            return new ClientUiRuntime(settings, rooms, isAndroid: true);
        }

        internal static void ShowPauseMenu(Action resume, Action leaveMatch, Action leaveServer,
            Action quit)
        {
            if (Runtime is null) return;
            Runtime.State.Router.OpenModal("session-menu",
                new PauseOverlayView(new AndroidPauseController(Runtime, resume, leaveMatch,
                    leaveServer, quit)));
        }

        internal static void ClosePauseMenu() => Runtime?.State.Router.CloseModal();

        internal static void Shutdown()
        {
            Runtime?.Dispose();
            Runtime = null;
        }

        private sealed class AndroidPauseController : IPauseOverlayController
        {
            private readonly ClientUiRuntime _runtime;
            private readonly Action _resume;
            private readonly Action _leaveMatch;
            private readonly Action _leaveServer;
            private readonly Action _quit;

            public AndroidPauseController(ClientUiRuntime runtime, Action resume,
                Action leaveMatch, Action leaveServer, Action quit)
            {
                _runtime = runtime;
                _resume = resume;
                _leaveMatch = leaveMatch;
                _leaveServer = leaveServer;
                _quit = quit;
            }

            public bool IsAuthoritativeMatch => AuthoritativePlay.Current != null;

            public System.Threading.Tasks.Task<UiActionResult> InvokeAsync(PauseAction action,
                System.Threading.CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (action)
                {
                    case PauseAction.Resume:
                        _resume();
                        break;
                    case PauseAction.Settings:
                        _runtime.State.Router.CloseModal();
                        _runtime.State.Router.Navigate(UiRoute.Settings);
                        break;
                    case PauseAction.HunterLicense:
                        _runtime.State.Router.CloseModal();
                        _runtime.State.Router.Navigate(UiRoute.HunterLicense);
                        break;
                    case PauseAction.LeaveMatch:
                        _leaveMatch();
                        break;
                    case PauseAction.LeaveServer:
                        _leaveServer();
                        break;
                    case PauseAction.Quit:
                        _quit();
                        break;
                    case PauseAction.MutePlayers:
                        return System.Threading.Tasks.Task.FromResult(
                            UiActionResult.Failure("Player muting is unavailable for this session."));
                }
                return System.Threading.Tasks.Task.FromResult(UiActionResult.Success());
            }
        }
    }
}
