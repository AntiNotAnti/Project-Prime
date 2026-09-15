using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using MphRead.Mods.Accounts;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// Entry point for the graphical launcher, on every platform.
    ///
    /// The loop is the one every game with a front screen has: one launcher,
    /// then a match, then the launcher again. "Leave match" in the pause menu
    /// comes back here; "Quit" and closing the launcher are what end the
    /// program.
    ///
    /// The toolkit is set up **once, on the thread that calls in** -- which is
    /// the game's own thread, the one the GL context will belong to -- and each
    /// visit to the launcher is a nested dispatcher loop on it rather than a
    /// fresh application. Three things make that the right shape and not an
    /// optimisation:
    ///
    /// - Avalonia allows one application per process. A second
    ///   <c>AppBuilder.Setup</c> throws, so a launcher that stood one up per
    ///   visit worked exactly once and fell back to the text screen on the way
    ///   back from the first match.
    /// - macOS will not accept windows off the main thread. AppKit is not
    ///   thread-safe and a window created anywhere else does not draw, which
    ///   rules out the private UI thread the WinForms launcher used.
    /// - The pause menu needs the toolkit *during* a match, on the thread the
    ///   render loop is running on. Nothing else can pump it.
    /// </summary>
    public static class GuiLauncher
    {
        private static bool _setUp;
        private static bool _failed;

        /// <summary>
        /// Developer-only rollback for comparing the pre-Prime shell. The
        /// production default is always PrimeShellView; this flag is set only
        /// by the explicit <c>-ui classic</c> command-line switch.
        /// </summary>
        public static bool ClassicUi { get; set; }

        /// <summary>
        /// Show the launcher, or say why it could not be shown.
        ///
        /// Returns false when there is no usable display -- a machine with no X
        /// or Wayland session, an SSH login without forwarding, a container, or
        /// a system missing the client libraries Avalonia binds. That is not an
        /// error worth stopping for: the text launcher does the same job, and
        /// falling back to it is the difference between "this build has no
        /// launcher on my machine" and "this build does not start".
        /// </summary>
        public static bool TryRun()
        {
            if (!EnsureSetup())
            {
                return false;
            }
            try
            {
                Run();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[launcher] the window could not be opened: {ex.Message}");
                Console.WriteLine("[launcher] falling back to the text launcher");
                Mods.DebugLog.Exception("launcher", ex);
                return false;
            }
        }

        /// <summary>
        /// Stand the toolkit up, once per process, on this thread.
        ///
        /// Also what the pause menu calls: in a session started from a command
        /// line rather than from the launcher, nothing has set the toolkit up
        /// and the first Escape is where it is needed.
        /// </summary>
        internal static bool EnsureSetup()
        {
            if (_setUp)
            {
                if (!PauseMenu.HasPresenter)
                    PauseMenu.RegisterPresenter(LegacyPauseMenuPresenter.Instance);
                return true;
            }
            if (_failed || !Probe())
            {
                return false;
            }
            try
            {
#if ANDROID
                // Android stands the toolkit up itself, from the activity, and
                // has no desktop backend to detect. Nothing here runs there:
                // this whole class is the desktop launcher loop, and the head
                // in src/MphRead.Android is the entry point instead.
                _setUp = false;
                return false;
#else
                AppBuilder.Configure<LauncherApp>()
                    .UsePlatformDetect()
                    .WithInterFont()
                    .SetupWithoutStarting();
                _setUp = true;
                PauseMenu.RegisterPresenter(LegacyPauseMenuPresenter.Instance);
                return true;
#endif
            }
            catch (Exception ex)
            {
                // Remembered, because everything that asks is in a loop or a
                // frame: a toolkit that could not start on this machine must be
                // asked once, not once a frame.
                _failed = true;
                Console.WriteLine($"[launcher] the window toolkit could not start: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Is there a display at all? Checked before Avalonia is initialised
        /// rather than by catching its failure, because the failure is a native
        /// abort in some configurations and there is nothing to catch.
        /// </summary>
        private static bool Probe()
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            {
                return true;
            }
            string? display = Environment.GetEnvironmentVariable("DISPLAY");
            string? wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            if (String.IsNullOrEmpty(display) && String.IsNullOrEmpty(wayland))
            {
                Console.WriteLine("[launcher] no DISPLAY or WAYLAND_DISPLAY; "
                    + "using the text launcher");
                return false;
            }
            return true;
        }

        private static void Run()
        {
            LauncherPrefs.Load();
            AccountSessions.UseSecureStore(
                SecureSessionStoreFactory.CreateDefault());
            if (GameFiles.Ready)
            {
                // Upstream's CheckSetup does this before anything runs; the
                // launcher is dispatched before that check, so it does it here
                // -- and tolerates the files being absent, which is the whole
                // reason it goes first.
                GameFiles.ApplyPaths();
                // A map added after the install was set up has no picture and
                // no sweep coming to give it one.
                Mods.DesktopThumbnailGenerator.EnsureCustomPreviews();
            }
            IReadOnlyList<string> rooms = Array.Empty<string>();

            HomeWindow? persistentWindow = null;
            // The semantic control endpoint is attached once to the same
            // persistent shell that owns the dispatcher, Node session, and
            // match handoff. It is never recreated for a rematch or nested
            // launcher visit.
            HomeWindow.SemanticControlAdapter? semanticControl = null;
            // Native window, GPU device and device caches belong to this GUI session.
            // MatchStart consumes only the per-round scene on a supplied host.
            SdlGameHost? persistentHost = null;
            var coordinator = new ClientSessionCoordinator();
            // One coordinator-scoped owner crosses the shell, persistent SDL
            // host, and overlay; no surface creates a process-wide input bus.
            var presentationSurface = new DesktopTransitionSurface(
                () => persistentWindow, () => persistentHost, Pump);
            var presentationCoordinator = new DesktopTransitionCoordinator(
                presentationSurface);
            using var desktopOverlay = new DesktopGameOverlayCoordinator(
                () => persistentHost, Pump, presentationCoordinator.InputOwner);
            if (!ClassicUi)
            {
                PauseMenu.RegisterPresenter(desktopOverlay);
            }
            MatchRunResult? lastResult = null;
            try
            {
                while (true)
                {
                    PauseMenu.Reset();
                    // Read again rather than reusing the object from the last time
                    // round: the pause menu's settings window loads and commits its
                    // own copy, so after a match this one is stale and would write
                    // the old values back over it.
                    MenuSettings settings = ClientSettings.LoadSettings();
                    // LoadSettings only fills in Features; the rest of the file
                    // reaches the engine through Mods.GameSettings.
                    Mods.GameSettings.Apply(settings);
                    LauncherPrefs.Load();
                    Mods.WindowMode.Startup = LauncherPrefs.WindowMode;
                    if (rooms.Count == 0 && GameFiles.Ready)
                    {
                        // Needs the game files: the room list is read out of them.
                        rooms = ThumbnailGenerator.MultiplayerRooms();
                    }

                    // Before the screen that offers "Random" as a hunter: the
                    // roll is held for one launch so the joined server and the
                    // loaded player agree, and this is where a launch begins.
                    Hunters.Reroll();
                    LaunchPlan plan;
                    if (ClassicUi) plan = AskClassic(settings, rooms);
                    else
                    {
                        if (persistentWindow == null)
                        {
                            persistentWindow = new HomeWindow(settings, rooms,
                                desktopOverlay);
                            semanticControl = persistentWindow
                                .CreateSemanticControlAdapter();
                            desktopOverlay.AttachTransitionActions(
                                persistentWindow.TransitionMenuActions);
                            persistentWindow.ResultsCloseRequested += (_, _) =>
                            {
                                persistentHost?.Close();
                                if (!persistentWindow.IsClosed) persistentWindow.Close();
                            };
                        }
                        coordinator = persistentWindow.SessionCoordinator;
                        plan = Ask(persistentWindow, lastResult, presentationCoordinator,
                            () => persistentHost,
                            () =>
                            {
                                if (persistentHost != null || persistentWindow.IsClosed) return;
                                try
                                {
                                    persistentHost = new SdlGameHost(showWindow: false);
                                    desktopOverlay.AttachHost(persistentHost);
                                    persistentHost.SetInitialPosition(
                                        new OpenTK.Mathematics.Vector2i(
                                            persistentWindow.Position.X,
                                            persistentWindow.Position.Y));
                                    DebugLog.Line("transition",
                                        "prewarmed persistent SDL/GPU host");
                                }
                                catch (Exception ex)
                                {
                                    // Prewarming is opportunistic. A later match
                                    // launch performs the normal surfaced retry.
                                    DebugLog.Exception("transition-prewarm", ex);
                                }
                            });
                    }
                    if (plan.Kind == LaunchKind.None)
                    {
                        return;
                    }
                    if (coordinator.Phase == ClientSessionPhase.ReturningToLobby)
                        coordinator.ShowHome(persistentWindow?.Online.Node != null,
                            persistentWindow?.Online.Node?.Lobby != null);
                    try
                    {
                        coordinator.BeginLaunch();
                        ulong transitionGeneration = 0;
                        Action<ulong>? firstFramePresented = null;
                        Action<ulong>? windowPrepared = null;
                        Action<MatchLoadStatus>? progress = null;
                        if (!ClassicUi)
                        {
                            MatchTransitionState preparing = TransitionState(plan,
                                presentationCoordinator.State
                                    == DesktopTransitionState.PreparingContinuation
                                    ? MatchTransitionStage.LoadingNextRound
                                    : MatchTransitionStage.Preparing,
                                "Preparing the match request.");
                            transitionGeneration = presentationCoordinator.State
                                == DesktopTransitionState.PreparingContinuation
                                ? presentationCoordinator.BeginContinuation(preparing)
                                : presentationCoordinator.BeginMatchLaunch(preparing);
                            ulong generation = transitionGeneration;
                            firstFramePresented = generation =>
                                presentationCoordinator.GameFirstFramePresented(generation);
                            windowPrepared = preparedGeneration =>
                            {
                                presentationCoordinator.UpdateLoading(preparedGeneration,
                                    TransitionState(plan, MatchTransitionStage.EnteringMatch,
                                        "The arena is ready. Presenting the first frame."));
                                Pump();
                                if (persistentWindow?.IsClosed == true)
                                    persistentHost?.Close();
                                else
                                    presentationCoordinator.GameWindowPrepared(preparedGeneration);
                            };
                            progress = status =>
                            {
                                MatchTransitionStage stage = status.Stage
                                    == MatchTransitionStage.Preparing
                                    && presentationCoordinator.State
                                        == DesktopTransitionState.PreparingContinuation
                                    ? MatchTransitionStage.LoadingNextRound
                                    : status.Stage;
                                presentationCoordinator.UpdateLoading(generation,
                                    TransitionState(plan, stage, status.Detail,
                                        status.MapName));
                                Pump();
                                if (persistentWindow?.IsClosed == true)
                                    persistentHost?.Close();
                            };
                        }
                        if (!ClassicUi && persistentHost == null)
                        {
                            persistentHost = new SdlGameHost(showWindow: false);
                            desktopOverlay.AttachHost(persistentHost);
                            if (persistentWindow != null)
                                persistentHost.SetInitialPosition(new OpenTK.Mathematics.Vector2i(
                                    persistentWindow.Position.X, persistentWindow.Position.Y));
                        }
                        if (persistentWindow?.IsClosed == true)
                            persistentHost?.Close();
                        Func<MatchResultsSnapshot?, Func<bool>, MatchResultsPresentationResult>?
                            presentResults = persistentWindow == null ? null : (results, pump) =>
                                persistentWindow.PresentResults(results, pump,
                                    presentationCoordinator.BeginResults,
                                    state => { presentationCoordinator.BeginContinuation(state); });
                        lastResult = MatchStart.Run(settings, plan, coordinator.NotifyMatchStarted,
                            presentResults,
                            persistentHost, transitionGeneration, firstFramePresented,
                            progress, windowPrepared, persistentWindow?.Online);
                        if (!ClassicUi && lastResult.Reason == MatchExitReason.LeftMatch
                            && persistentWindow != null)
                        {
                            try
                            {
                                persistentWindow.CompleteLocalMatchExitAsync()
                                    .GetAwaiter().GetResult();
                            }
                            catch (Exception error)
                            {
                                DebugLog.Exception("match-return-to-lobby", error);
                            }
                        }
                        if (!ClassicUi && lastResult.Reason is MatchExitReason.FailedToStart
                            or MatchExitReason.ClientError)
                            presentationCoordinator.FailLaunch(transitionGeneration,
                                lastResult.Message);
                        if (!ClassicUi && lastResult.Reason == MatchExitReason.Transitioning)
                        {
                            MatchTransitionState continuationState = TransitionState(plan,
                                MatchTransitionStage.LoadingNextRound,
                                "Preparing the next match.",
                                persistentWindow?.Online.Node?.Lobby?.MapKey ?? plan.RoomKey);
                            // Results continuations already committed this state
                            // inside PresentResults. Updating that generation
                            // preserves its ownership of the Results surface;
                            // active-match restart enters it here.
                            if (presentationCoordinator.State
                                == DesktopTransitionState.PreparingContinuation)
                            {
                                presentationCoordinator.UpdateLoading(
                                    presentationCoordinator.CurrentGeneration,
                                    continuationState);
                            }
                            else
                            {
                                presentationCoordinator.BeginContinuation(
                                    continuationState);
                            }
                        }
                        coordinator.NotifyMatchEnded(lastResult);
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Exception("match", ex);
                        lastResult = new MatchRunResult(MatchExitReason.FailedToStart, Message: ex.Message);
                        if (!ClassicUi)
                            presentationCoordinator.FailLaunch(
                                presentationCoordinator.CurrentGeneration, ex.Message);
                        coordinator.NotifyMatchEnded(lastResult);
                    }
                    finally
                    {
                        // The Worker UDP client is the only gameplay resource this
                        // client owns. Public hosting is Node-owned and has no
                        // local server process to stop here.
                        (persistentWindow?.Online ?? ClientOnlineRuntime.Current)
                            ?.ReleaseMatch(dispose: true);
                        NetSession.Stop();
                        // Close a settings window opened from the pause menu on the
                        // frame the match ended.
                        PauseMenuWindow.CloseIfOpen();
                    }
                    if (lastResult?.Reason == MatchExitReason.QuitApplication)
                    {
                        return;
                    }
                }
            }
            finally
            {
                desktopOverlay.CloseForTransition();
                presentationCoordinator.Close();
                PauseMenu.UnregisterPresenter(desktopOverlay);
                PauseMenu.RegisterPresenter(LegacyPauseMenuPresenter.Instance);
                semanticControl?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                persistentHost?.Dispose();
                coordinator.Quit();
                if (persistentWindow is { IsClosed: false }) persistentWindow.Close();
            }
        }

        /// <summary>
        /// Show the front screen and wait for an answer.
        ///
        /// A nested dispatcher loop rather than an application lifetime: the
        /// loop ends on launch or application close. Match return reactivates
        /// the same window and shell on the same toolkit.
        /// </summary>
        private static LaunchPlan Ask(HomeWindow window, MatchRunResult? result,
            DesktopTransitionCoordinator presentationCoordinator,
            Func<SdlGameHost?> gameHost,
            Action prewarmGameHost)
        {
            var frame = new DispatcherFrame();
            var sdlPump = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            void Done(object? sender, EventArgs args) => frame.Continue = false;
            void ReturnFromFailure(object? sender, EventArgs args)
                => presentationCoordinator.CompleteFailedReturn();
            void ContinuationFailed(string message)
            {
                if (presentationCoordinator.State == DesktopTransitionState.PreparingContinuation)
                    presentationCoordinator.FailLaunch(
                        presentationCoordinator.CurrentGeneration, message);
            }
            void PrewarmGameHost(object? sender, EventArgs args)
                => prewarmGameHost();
            void PumpSdlInput(object? sender, EventArgs args)
            {
                SdlGameHost? host = gameHost();
                if (host != null && !host.PumpShellEvents() && !window.IsClosed)
                    window.Close();
            }
            window.Closed += Done;
            window.LaunchRequested += Done;
            window.TransitionReturnToLobbyRequested += ReturnFromFailure;
            window.ContinuationFailed += ContinuationFailed;
            window.GameHostPrewarmRequested += PrewarmGameHost;
            sdlPump.Tick += PumpSdlInput;
            sdlPump.Start();
            try
            {
                // Prepare the returned shell first; the coordinator performs
                // the target-first hide/focus ordering once the dispatcher is
                // ready. The initial visit remains the active native window.
                window.Resume(result, activate: result == null);
                bool returnToShell = ShouldReturnToShellAfterResume(result);
                if (result == null || returnToShell
                    || presentationCoordinator.State == DesktopTransitionState.Failed)
                    presentationCoordinator.InputOwner.SetOwner(DesktopInputOwnerKind.Shell);
                if (returnToShell) presentationCoordinator.BeginReturnToShell();
                Dispatcher.UIThread.PushFrame(frame);
                Pump();
                return window.Plan;
            }
            finally
            {
                window.Closed -= Done;
                window.LaunchRequested -= Done;
                window.TransitionReturnToLobbyRequested -= ReturnFromFailure;
                window.ContinuationFailed -= ContinuationFailed;
                window.GameHostPrewarmRequested -= PrewarmGameHost;
                sdlPump.Stop();
                sdlPump.Tick -= PumpSdlInput;
                if (presentationCoordinator.InputOwner.Owns(DesktopInputOwnerKind.Shell))
                    presentationCoordinator.InputOwner.SetOwner(DesktopInputOwnerKind.None);
            }
        }

        private static MatchTransitionState TransitionState(LaunchPlan plan,
            MatchTransitionStage stage, string? detail, string? mapName = null)
            => new(stage,
                Map: String.IsNullOrWhiteSpace(mapName) ? plan.RoomKey : mapName,
                Mode: plan.Mode.ToString(), Hunter: plan.Hunter.ToString(),
                Detail: detail);

        internal static bool ShouldReturnToShellAfterResume(MatchRunResult? result)
            => result != null && result.Reason != MatchExitReason.Transitioning;

        private static LaunchPlan AskClassic(MenuSettings settings, IReadOnlyList<string> rooms)
        {
            var view = new HomeView(settings, rooms);
            var window = new Window
            {
                Title = Mods.Branding.Name + " (classic UI)",
                Icon = GuiTheme.AppIcon.Value,
                Width = 940,
                Height = 560,
                MinWidth = 780,
                MinHeight = 480,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = GuiTheme.PanelBrush,
                RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
                Content = view
            };
            view.Done += (_, _) => window.Close();
            window.Closed += (_, _) => _ = view.DisposeAsync().AsTask();
            var frame = new DispatcherFrame();
            window.Closed += (_, _) => frame.Continue = false;
            window.Show();
            Dispatcher.UIThread.PushFrame(frame);
            Pump();
            return view.Plan;
        }

        /// <summary>
        /// Give the toolkit a slice of this frame.
        ///
        /// Called once a frame by the game while the pause menu is up. The
        /// posted job runs after everything already queued -- native input
        /// included -- and ends the loop, so this processes what is pending and
        /// returns rather than taking the thread over.
        /// </summary>
        internal static void Pump()
        {
            if (!_setUp)
            {
                return;
            }
            var frame = new DispatcherFrame(exitWhenRequested: false);
            Dispatcher.UIThread.Post(() => frame.Continue = false,
                DispatcherPriority.Background);
            Dispatcher.UIThread.PushFrame(frame);
        }
    }

    /// <summary>
    /// The Avalonia application object. Fluent is here for the handful of stock
    /// controls the screens use -- the text boxes and the scroll bars; every
    /// other control on them is drawn by this code, because a launcher whose
    /// controls are half themed reads as broken rather than as a choice.
    /// </summary>
    internal sealed class LauncherApp : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            base.Initialize();
        }
    }
}
