using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// One window, for the whole program.
    ///
    /// The loop this replaces was: open an Avalonia window, wait for it to
    /// close, open a GL window, run the match in it, destroy it, open another
    /// Avalonia window. Two toolkits, two windows, and a visible seam at every
    /// boundary -- the launcher vanished from the taskbar when a match
    /// started, the match vanished when it ended, and anything that went wrong
    /// in between left the player looking at an empty desktop.
    ///
    /// Now the GL window is opened once and never closed until the player
    /// leaves. The launcher is drawn inside it (<see cref="UiSurface"/>,
    /// <see cref="UiOverlay"/>), a match is a <see cref="Scene"/> built into
    /// that same window, and leaving a match unloads the scene and puts the
    /// front screen back up. The pause menu and the settings are screens in
    /// the same surface rather than borderless windows chasing the game's
    /// rectangle.
    ///
    /// This is the shape the Android head has always had -- one surface, one
    /// view stack over it, matches loaded and unloaded underneath -- which is
    /// why the screens needed no changes to be drawn here.
    ///
    /// Everything below runs on the game's own thread, between frames. The
    /// toolkit is set up on that thread and the screens post their work to its
    /// dispatcher, which <see cref="UiSurface.Tick"/> drains once a frame; a
    /// decision a screen makes is therefore acted on at the top of the next
    /// frame rather than in the middle of the one that is being drawn.
    /// </summary>
    internal static partial class Shell
    {
        /// <summary>True while the shell window is the one running.</summary>
        public static bool Active { get; private set; }

        /// <summary>Is a screen up and taking the input?</summary>
        public static bool UiVisible
        {
            get
            {
#if MPHREAD_RMLUI_POC
                if (RmlUiPrototype.Active) return true;
#endif
                return UiSurface.Current?.Visible == true;
            }
        }

        /// <summary>The one window, for anything that needs to own a dialog.</summary>
        internal static RenderWindow? Window => _window;

        private static RenderWindow? _window;
        internal static event Action<IReadOnlyList<string>>? FilesDropped;

        private static void OnFilesDropped(FileDropEventArgs e)
        {
            // OpenTK's strings are only guaranteed for the duration of the
            // native callback, so copy before handing them to a screen.
            string[] files=e.FileNames?.ToArray()??Array.Empty<string>();
            if(files.Length>0)FilesDropped?.Invoke(files);
        }

        /// <summary>
        /// Hand the game window to whatever needs it as a parent.
        ///
        /// One thing does: a native file dialog has to be *owned* by the game
        /// window or Windows is free to put it behind a borderless-fullscreen
        /// game -- a modal dialog nobody can see, which from the player's side
        /// is a button that does nothing and then a program that has stopped
        /// answering. See <see cref="NativeFilePicker"/>.
        /// </summary>
        private static void PublishNativeHandle(RenderWindow window)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }
            try
            {
                unsafe
                {
                    NativeFilePicker.Owner = OpenTK.Windowing.GraphicsLibraryFramework.GLFW
                        .GetWin32Window(window.WindowPtr);
                }
            }
            catch (Exception)
            {
                // An owner is an improvement, not a requirement.
            }
        }
        private static StartScreen? _front;
        private static InGameMenu? _menu;
        private static MenuSettings _settings = new MenuSettings();
        private static IReadOnlyList<string> _rooms = Array.Empty<string>();
        private static LaunchPlan? _pending;
        private static bool _endMatch;
        private static bool _quit;
        internal static bool OpenStudioOnStart { get; set; }
        private static MapGen.MapDefinition? _studioPreview;
        internal static void PrepareStudioPreview(MapGen.MapDefinition definition) => _studioPreview = definition;
        // While a persistent-lobby client has finished its local room build but
        // the server is still waiting for the other participants, keep the
        // lobby/loading surface over the scene. Reveal on the committed
        // countdown edge instead of whichever frame the InMatch packet arrives.
        private static bool _matchLoading;
        private static CancellationTokenSource? _startupWorkCancel;
        private static Task? _startupWork;
        private static int _firstFrameStarted;
        private static IReadOnlyList<MapGen.MapDefinition>? _deferredCustomRoomsPending;
        private static CancellationToken _deferredCustomRoomsToken;

        /// <summary>
        /// Open the window and run until the player quits.
        ///
        /// False means there is nothing to run in: no display, no GL, or a
        /// toolkit that will not start on this machine. The text launcher
        /// plays the same matches and is what the caller falls back to.
        /// </summary>
        internal static string? StudioProjectPath { get; set; }
        internal static bool StudioWindow { get; set; }

        public static bool Run()
        {
            bool result = RunSession();
#if !MPHREAD_SERVER
            if (GraphicsBackendPolicy.Requested == GraphicsBackend.Auto
                && ModernGraphicsCompat.RecoveryFailure is Exception failure)
                return RendererCompatibilityRestart.Start(failure.Message);
#endif
            return result;
        }

        private static bool RunSession()
        {
            LifecycleTiming.Startup("shell session begin");
#if MPHREAD_RMLUI_POC
            bool rmlUiOnlyStartup = RmlUiPrototype.Requested;
#else
            bool rmlUiOnlyStartup = false;
#endif
            if (!rmlUiOnlyStartup)
            {
                if (UiSurface.Ensure() == null)
                {
                    return false;
                }
                LifecycleTiming.Startup("UI surface ready");
            }
            else
            {
                LifecycleTiming.Startup("RmlUi proof requested; Avalonia surface deferred");
            }
            LauncherPrefs.Load();
            SocialPresenceClient.Start();
            LifecycleTiming.Startup("launcher preferences loaded");
            Interlocked.Exchange(ref _firstFrameStarted, 0);
            // The backdrop is GL's from here on: this is the one head with a
            // window under the screens, and the photograph is worth the
            // window's own pixels rather than the screens' capped ones. Said
            // before the first screen is built, because it decides what goes
            // into the bake. See Mods.Render.LauncherPhoto.
            Mods.Render.LauncherPhoto.Enabled = true;
            if (GameFiles.Ready)
            {
                // Upstream's CheckSetup does this before anything runs; the
                // launcher is dispatched before that check, so it does it here
                // -- and tolerates the files being absent, which is the whole
                // reason it goes first.
                GameFiles.ApplyPaths();
                LifecycleTiming.Startup("game paths ready");
            }
            // How the window opens: the way this one was left, unless the
            // command line said otherwise for this run.
            if (!Mods.WindowMode.StartupForced)
            {
                Mods.WindowMode.Startup = LauncherPrefs.WindowMode;
            }
            RenderWindow.LogCreatingWindow();
            LifecycleTiming.Startup("creating native window");
            RenderWindow? window = null;
            bool sessionCompleted = false;
            try
            {
                window = RenderWindow.Create(shell: true);
                LifecycleTiming.Startup("native window created");
                if (StudioWindow) { window.Title = "Project Prime · Map Studio"; window.WindowState = OpenTK.Windowing.Common.WindowState.Maximized; }
                window.FileDrop += OnFilesDropped;
                PublishNativeHandle(window);
                _window = window;
                Active = true;
                OfflineRematch.StartNext = PlayAnother;
                bool showClassicFront = true;
#if MPHREAD_RMLUI_POC
                if (RmlUiPrototype.Requested)
                {
                    bool rmlUiActivated = RmlUiPrototype.TryActivate(window);
                    if (!rmlUiActivated && RmlUiPrototype.CaptureRequested)
                        return false;
                    showClassicFront = !rmlUiActivated;
                }
#endif
                if (showClassicFront)
                {
#if MPHREAD_RMLUI_POC
                    if (!GuiLauncher.EnsureSetup() || UiSurface.Ensure() == null)
                        return false;
#endif
                    ShowFrontScreen();
                }
                LifecycleTiming.Startup("front screen ready");
                window.Run();
                sessionCompleted = true;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"The window could not be opened: {ex.Message}");
                Mods.DebugLog.Exception("launcher", ex);
                // If the user had already committed to quitting, do not turn a
                // teardown-time renderer exception into a text-launcher fallback.
                return _quit;
            }
            finally
            {
                bool processEnding = sessionCompleted || _quit;
                if (processEnding)
                {
                    LifecycleTiming.BeginShutdown("shell session ending");
                    ReplayWritePump.BeginProcessShutdown();
                    MphRead.Sound.AudioLifetime.BeginShutdown();
                }
                CancellationTokenSource? startupCancel = _startupWorkCancel;
                _startupWorkCancel = null;
                startupCancel?.Cancel();
                startupCancel?.Dispose();
                _startupWork = null;
                _deferredCustomRoomsPending = null;
                _deferredCustomRoomsToken = default;
                SocialPresenceClient.Stop();
                Active = false;
                OfflineRematch.StartNext = null;
                _window = null;
                _pending = null;
                _endMatch = false;
                _quit = false;
                _matchLoading = false;
                _studioPreview = null;
                OpenStudioOnStart = false;
                // Both own a worker thread and a bound socket; leaving the
                // program must not leave either behind.
                Mods.DebugLog.Line("shutdown", "stopping network session");
                NetSession.Stop();
                NetHostSession.Stop();
                Mods.DebugLog.Line("shutdown", "network session stopped");
                if (processEnding) LifecycleTiming.Shutdown("network session stopped");
                if (window != null)
                {
                    window.FileDrop -= OnFilesDropped;
                    if (!Mods.Render.GraphicsBackendPolicy.ModernGameplayRequested)
                    {
                        window.Context.MakeCurrent();
#if MPHREAD_RMLUI_POC
                        // The RmlUi bridge owns GL resources and must be torn
                        // down while this compatibility context is still current.
                        RmlUiPrototype.Shutdown();
#endif
                        // Program names are context-local. Any desktop
                        // compatibility-context handoff invalidates the legacy
                        // uniform cache before GL work resumes on this window.
#if !ANDROID && !MPHREAD_SERVER
                        Mods.Render.GraphicsApi.ResetLegacyState();
#endif
                        UiSurface.Current?.ReleaseMapRenderer();
                    }
                }
                _front?.Dispose();
                _front = null;
                Mods.DebugLog.Line("shutdown", "disposing native window");
                try { window?.Dispose(); }
                finally
                {
#if !MPHREAD_SERVER
                    ModernGraphicsCompat.Shutdown();
#endif
                }
                Mods.DebugLog.Line("shutdown", "native window disposed");
                if (processEnding) LifecycleTiming.Shutdown("native window disposed");
            }
        }

        // --------------------------------------------------------- the frame

        /// <summary>
        /// Act on what the screens decided since the last frame: start a
        /// match, end one, or leave.
        /// </summary>
        internal static void BeforeFrame(RenderWindow window)
        {
            if (!Active)
            {
                return;
            }
#if MPHREAD_RMLUI_POC
            if (RmlUiPrototype.Active && !window.HasScene
                && _deferredCustomRoomsPending is { } rmlPending)
            {
                PublishDeferredCustomRooms(rmlPending, _deferredCustomRoomsToken);
            }
#endif
#if MPHREAD_RMLUI_POC
            // The proof owns only the front screen. If it fails after startup,
            // or if one of its buttons selects a destination, restore the
            // existing authoritative shell and continue from there.
            if (RmlUiPrototype.Requested && RmlUiPrototype.Failed && !window.HasScene
                && UiSurface.Current?.Visible != true)
            {
                if (GuiLauncher.EnsureSetup() && UiSurface.Ensure() != null)
                    ShowFrontScreen();
                else
                    RequestQuit();
            }
            while (RmlUiPrototype.Active && RmlUiPrototype.TryTakeCommand(out string rmlCommand))
            {
                if (rmlCommand == "quit")
                {
                    RequestQuit();
                    break;
                }

                PrimeRoute? rmlRoute = rmlCommand switch
                {
                    "route:play" => PrimeRoute.Play,
                    "route:offline" => PrimeRoute.Offline,
                    "route:hunter" => PrimeRoute.HunterLicense,
                    "route:forge" => PrimeRoute.Forge,
                    "route:theatre" => PrimeRoute.Theatre,
                    "route:settings" => PrimeRoute.Settings,
                    "route:news" => PrimeRoute.News,
                    _ => null
                };
                if (rmlRoute is { } target)
                {
                    RmlUiPrototype.Shutdown();
                    if (!GuiLauncher.EnsureSetup() || UiSurface.Ensure() == null)
                    {
                        RequestQuit();
                        break;
                    }
                    ShowFrontScreen();
                    _front?.Prime.Router.Navigate(target);
                    break;
                }
            }
#endif
            if (window.HasScene && window.Scene.AimTrainer is { Completed: true, ResultsShown: false } training
                && UiSurface.Ensure() is { } trainingSurface)
            {
                training.ResultsShown = true;
                trainingSurface.Show(new AimTrainerResultsView(training,
                    () => { _endMatch = true; _pending = AimTrainerLaunch.Create(training.Definition.Retry(), training.Plan.Hunter, LauncherPrefs.LastColor); },
                    () => { _focusTrainingOnReturn = true; RequestEndMatch(); }, RequestEndMatch));
            }
            if (_quit)
            {
                _quit = false;
                window.Close();
                return;
            }
            if (window.HasScene && NetSession.PersistentLobby && NetSession.IsInLobby && !_endMatch)
                EndNetworkMatchToLobby(window);
            if (window.HasScene && (NetSession.Refused || NetSession.SessionTimedOut)) _endMatch = true;
            if (_endMatch)
            {
                _endMatch = false;
                EndMatch(window);
            }
            if (_pending is LaunchPlan plan)
            {
                // MatchStart is intentionally synchronous because scene/GPU
                // creation belongs to this thread. Do not make its CPU preflight
                // synchronous too: large custom maps can spend seconds compiling,
                // reading and decoding before a Scene exists, which used to stop
                // window presentation and look like a hard freeze.
                bool roomLaunch = plan.Kind is LaunchKind.Online or LaunchKind.Offline
                    or LaunchKind.Host or LaunchKind.AimTrainer;
                if (roomLaunch && !String.IsNullOrWhiteSpace(plan.RoomKey)
                    && Mods.RoomPrewarm.Begin(plan.RoomKey)
                    && !Mods.RoomPrewarm.TryGetPreparationResult(plan.RoomKey, out _))
                {
                    // Leave the plan pending and keep drawing/pumping the shell.
                    return;
                }

                _pending = null;
                StartMatch(window, plan);
            }

            // A replay error must never strand the player behind a hidden shell.
            // Restore the already-open Theatre editor automatically so Escape is
            // not the only recovery path from a frozen fullscreen frame.
            if (window.HasScene && _played?.Kind == LaunchKind.Demo
                && DemoPlayback.IsActive && DemoPlayback.LastResult != ReplayOpenResult.Success
                && _front != null && UiSurface.Ensure() is { } replaySurface
                && !replaySurface.Visible)
            {
                _front.ShowReplayEditor(RequestEndMatch, () => replaySurface.Hide());
                replaySurface.Show(_front);
            }

            if (_matchLoading && window.HasScene)
            {
                if (!NetSession.FreezeGameplay)
                {
                    _matchLoading = false;
                    UiSurface.Current?.Hide();
                }
                else if (NetSession.Refused || NetSession.SessionTimedOut)
                {
                    _matchLoading = false;
                    _endMatch = true;
                }
            }
        }

        /// <summary>
        /// Draw whatever screen is up into the overlay texture.
        ///
        /// Not only in the shell: the pause menu is the same surface, and a
        /// window opened by one of the harness commands has one too.
        /// </summary>
        internal static void TickUi(RenderWindow window)
        {
#if MPHREAD_RMLUI_POC
            if (RmlUiPrototype.Active)
            {
                // RmlUi renders itself later in UiOverlay.DrawAlone. Nothing
                // here rasterizes an Avalonia surface or uploads a UI bitmap.
                NotePointerBasis(window);
                UiOverlay.Visible = false;
                RmlUiPrototype.Tick(window);
                return;
            }
#endif
            UiSurface? surface = UiSurface.Current;
            if (surface == null)
            {
                UiOverlay.Visible = false;
                return;
            }
            // The window's size, every frame, whether or not a screen is up.
            // It used to be read only while one was, so a window made bigger
            // during a match told the surface nothing: Escape then built the
            // pause menu against the size the window had when the match
            // started.
            surface.Resize(window.FramebufferSize.X, window.FramebufferSize.Y);
            surface.PrepareMapRenderer();
            NotePointerBasis(window);
            if (!surface.Visible)
            {
                UiOverlay.Visible = false;
                return;
            }
            surface.Tick();
        }

        private static int _basisWidth;
        private static int _basisHeight;

        /// <summary>
        /// The two sizes the pointer is converted between, once each time they
        /// change. A vertical bias of a few tens of pixels -- "I have to aim
        /// slightly above the row I want" -- is this ratio not being one, and
        /// it is invisible from inside the toolkit, whose own arithmetic is
        /// consistent either way.
        /// </summary>
        private static void NotePointerBasis(RenderWindow window)
        {
            int fw = window.FramebufferSize.X;
            int fh = window.FramebufferSize.Y;
            if (fw == _basisWidth && fh == _basisHeight)
            {
                return;
            }
            _basisWidth = fw;
            _basisHeight = fh;
            int cw = window.ClientSize.X;
            int ch = window.ClientSize.Y;
            double sx = cw > 0 ? fw / (double)cw : 1;
            double sy = ch > 0 ? fh / (double)ch : 1;
            Mods.DebugLog.Line("ui", $"pointer basis: framebuffer {fw}x{fh}, "
                + $"client {cw}x{ch}, pointer scaled by {sx:0.####}x{sy:0.####}"
                + (Math.Abs(sx - 1) > 0.001 || Math.Abs(sy - 1) > 0.001
                    ? " -- not 1, so clicks land off by that fraction" : ""));
        }

        private static EndPanelView? _endPanel;

        /// <summary>
        /// Whether the deck panel is up over the results, so the HUD's own
        /// picker knows to leave the right-hand side alone.
        ///
        /// Read by <c>ModDrawEndScreen</c>, which draws the ballot and the
        /// hunter as arrows and swatches beside a 32x32 sprite. Only that
        /// panel steps aside -- the scoreboard beside it is the engine's own
        /// screen and stays exactly as it is.
        /// </summary>
        public static bool EndPanelUp => _endPanel != null;

        /// <summary>
        /// Put the results panel up while the results are up, and take it down
        /// after.
        ///
        /// Driven from the frame rather than from whatever ends a match: the
        /// results screen arrives because the server said the match is over,
        /// and there is no one place on this machine that hears it. The state
        /// is already published -- EndScreen.Available is the same question
        /// the HUD asks -- so this watches it.
        ///
        /// Never over the pause menu. Escape during the results is a thing a
        /// player can still do, and two panels over one match is one too many.
        /// </summary>
        internal static void TickEndPanel()
        {
            bool want = _window?.HasScene == true && !_matchLoading
                && Mods.EndScreen.PanelAvailable && _menu == null;
            if (want && !EndPanelUp)
            {
                // Ensure, not Current: a session that went straight into a
                // match -- -connect, or anything else that never opened a
                // screen -- has no surface yet, and reading one that is not
                // there left the results with the HUD's own arrows and
                // swatches. Android's TickEndPanel does the same.
                UiSurface? surface = UiSurface.Ensure();
                if (surface == null)
                {
                    return;
                }
                var panel = new EndPanelView();
                _endPanel = panel;
                Mods.EndScreen.PanelUp = true;
                surface.Show(panel);
                return;
            }
            if (!want && EndPanelUp)
            {
                EndPanelView? closing = _endPanel;
                _endPanel = null;
                Mods.EndScreen.PanelUp = false;
                // The lobby or pause menu may already have replaced the
                // results panel on the shared UiSurface earlier in this frame.
                // Never hide that newer view while cleaning up the stale panel.
                UiSurface? surface = UiSurface.Current;
                if (surface != null && ReferenceEquals(surface.View, closing))
                {
                    surface.Hide();
                }
                return;
            }
            _endPanel?.Refresh();
        }

        // -------------------------------------------------------- the screens

        private static void ShowFrontScreen()
        {
            if (_deferredCustomRoomsPending is { } pending)
            {
                PublishDeferredCustomRooms(pending, _deferredCustomRoomsToken);
            }
            UiSurface? surface = UiSurface.Current;
            if (surface == null)
            {
                return;
            }
            PauseMenu.Reset();
            // Read again rather than reusing the object from the last time
            // round: the settings page opened from the pause menu loads and
            // commits its own copy, so after a match this one is stale and
            // would write the old values back over it.
            _settings = GameState.LoadSettings();
            // LoadSettings only fills in Features; the rest of the file
            // reaches the engine through Mods.GameSettings.
            Mods.GameSettings.Apply(_settings);
            LauncherPrefs.Load();
            if (_rooms.Count == 0 && GameFiles.Ready)
            {
                // Needs the game files: the room list is read out of them.
                _rooms = ThumbnailGenerator.MultiplayerRooms();
            }
            if (_window != null)
            {
                _window.Title = StudioWindow ? "Project Prime · Map Studio" : Mods.Branding.Name;
            }
            if (_front == null)
            {
                // Before the screen that offers "Random" as a hunter: the roll
                // is held for one launch so the joined server and the loaded
                // player agree, and this is where a launch begins.
                Hunters.Reroll();
                _front = new StartScreen(_settings, _rooms);
                _front.Done += (_, plan) => Decided(plan);
                _front.MatchRequested += (_, plan) => Decided(plan);
            }
            else
            {
                // One front screen for the life of the process, the way the
                // Android head has always kept one: Reset is what makes it
                // usable again -- the stack emptied, the hunter rerolled, the
                // room list and the version line read afresh.
                _front.Reset(_settings);
            }
            surface.Show(_front);
            if (OpenStudioOnStart)
            {
                OpenStudioOnStart = false;
                _front.OpenMapStudio();
            }
        }

        private static void Decided(LaunchPlan plan)
        {
            if (plan.Kind == LaunchKind.None)
            {
                RequestQuit();
                return;
            }
            _pending = plan;
        }

        // -------------------------------------------------------- the match

        /// <summary>
        /// What the running match was started from, so another can be started
        /// like it. See <see cref="PlayAnother"/>.
        /// </summary>
        private static LaunchPlan? _played;
        private static bool _focusTrainingOnReturn;

        /// <summary>
        /// Whether "play the map the results screen picked" means anything
        /// here: an offline match of one's own, started from this shell.
        ///
        /// Online there is a server with a rotation and this is not its
        /// business; a story match and a demo have no next map to pick.
        /// </summary>
        public static bool CanPlayAnother => Active && _pending == null && !NetSession.Active
            && _played is LaunchPlan plan && plan.Kind == LaunchKind.Offline;

        /// <summary>
        /// Load another map, exactly as this match was loaded, with the room
        /// the results screen chose.
        ///
        /// The same two requests the pause menu's "Leave match" and the front
        /// screen's "Start" already use, sent together: the frame that ends the
        /// match starts the next one, so the launcher is built and hidden
        /// again without ever being drawn. Offline that is the whole of what a
        /// rotation is.
        /// </summary>
        public static bool PlayAnother(string roomKey)
        {
            if (!CanPlayAnother || _played is not LaunchPlan plan
                || !OfflineRematch.TryPlan(plan, roomKey, out var next))
            {
                return false;
            }
            _endMatch = true;
            _pending = next;
            return true;
        }

        private static void StartMatch(RenderWindow window, LaunchPlan plan)
        {
            _played = plan;
            _front?.SuspendLobby();
            _matchLoading = false;
            try
            {
                MapGen.MapDefinition? preview = _studioPreview;
                _studioPreview = null;
                if (preview != null)
                {
                    Metadata.RegisterStudioPreview(preview);
                }
                if (!MatchStart.Begin(window, _settings, plan))
                {
                    string failure = MatchStart.LastError ?? "The map could not be loaded.";
                    NetSession.ReportMatchLoadFailed(failure);
                    EndMatch(window);
                    if (plan.Kind == LaunchKind.Demo)
                    {
                        _front?.ShowReplayLaunchFailure(failure);
                    }
                    else if (plan.Kind == LaunchKind.AimTrainer) _front?.ShowTrainingLaunchFailure(failure);
                    return;
                }

                if (NetSession.PersistentLobby && NetSession.IsStarting)
                {
                    // Do not reveal a locally loaded scene while another player
                    // is still loading. The authority already waits for every
                    // expected MatchLoaded ack; keeping the front surface up
                    // until it publishes InMatch removes the black gap and gives
                    // every participant the same visible start boundary.
                    _matchLoading = true;
                }
                else if (plan.Kind == LaunchKind.Demo && _front != null)
                {
                    _front.ShowReplayEditor(RequestEndMatch, () => FullscreenReplay(window));
                    UiSurface.Current?.Show(_front);
                }
                else
                {
                    UiSurface.Current?.Hide();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"The game could not start: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                // The Windows build is a GUI binary with no console behind it,
                // so the two lines above reach nobody: from the player's side
                // the game simply disappears while a map is loading. This is
                // the one place that can still be read afterwards -- and the
                // whole reason the switch in the settings exists.
                Mods.DebugLog.Line("crash", "the match could not start");
                Mods.DebugLog.Exception("crash", ex);
                // Back to the front screen rather than out of the program: a
                // map that will not load is a reason to pick another one.
                NetSession.ReportMatchLoadFailed(ex.Message);
                EndMatch(window);
                if (plan.Kind == LaunchKind.Demo)
                {
                    _front?.ShowReplayLaunchFailure("Could not open replay: " + ex.Message);
                }
                else if (plan.Kind == LaunchKind.AimTrainer) _front?.ShowTrainingLaunchFailure(ex.Message);
            }
        }

        private static void FullscreenReplay(RenderWindow window)
        {
            UiSurface.Current?.Hide();
            // The Replay Studio button owns toolkit focus. Hiding the toolkit
            // does not reliably return keyboard focus to GLFW on every window
            // manager, which makes fullscreen playback appear frozen because
            // Space/Escape and the replay keyboard bindings stop arriving.
            try { window.Focus(); }
            catch (Exception) { /* pointer/gamepad remain usable */ }
        }

        private static void EndNetworkMatchToLobby(RenderWindow window)
        {
            _matchLoading = false;
            CloseMenu(); window.EndScene(); MatchStart.AfterMatch();
            NetSession.ResetMatchState(); PauseMenu.Reset();
            if (_front != null) { UiSurface.Current?.Show(_front); _front.ResumeLobby(); }
        }

        private static void EndMatch(RenderWindow window)
        {
            _matchLoading = false;
            CloseMenu();
            window.EndScene();
            // Both own a worker thread and a bound socket; a match that ends
            // any way at all must not leave either behind.
            NetSession.Stop();
            NetHostSession.Stop();
            MatchStart.AfterMatch();
            ShowFrontScreen();
            if (_played?.Kind == LaunchKind.AimTrainer) _front?.OpenTraining(_focusTrainingOnReturn);
            _focusTrainingOnReturn = false;
        }

        /// <summary>The match is over; the launcher comes back.</summary>
        public static void RequestEndMatch()
        {
            if (!Active)
            {
                return;
            }
            _endMatch = true;
        }

        /// <summary>Leave the program.</summary>
        internal static bool PreserveForgeRecovery() => _front?.PreserveForgeRecovery() ?? true;

        public static void RequestQuit()
        {
            LifecycleTiming.BeginShutdown("quit requested");
            _startupWorkCancel?.Cancel();
            ReplayWritePump.BeginProcessShutdown();
            MphRead.Sound.AudioLifetime.BeginShutdown();
            _quit = true;
        }

        /// <summary>
        /// What the pause menu's "Leave match" and "Quit" mean to a window.
        ///
        /// In the shell they are the two requests above; in one of the harness
        /// windows, where there is no launcher to come back to, both still
        /// mean what they always meant -- close the window and end the run.
        /// </summary>
        internal static void LeaveMatch(OpenTK.Windowing.Desktop.GameWindow window)
        {
            if (Active)
            {
                RequestEndMatch();
                return;
            }
            window.Close();
        }

        internal static void Quit(OpenTK.Windowing.Desktop.GameWindow window)
        {
            if (Active)
            {
                RequestQuit();
                return;
            }
            window.Close();
        }

        // ---------------------------------------------------- the pause menu

        /// <summary>
        /// Open the in-game menu. False when there is no surface to draw it on
        /// -- Escape then keeps its old meaning rather than doing nothing.
        /// </summary>
        internal static bool OpenPauseMenu()
        {
            UiSurface? surface = UiSurface.Ensure();
            if (surface == null)
            {
                return false;
            }
            if (_menu != null)
            {
                return true;
            }
            // The settings page in the menu commits its own copy of the file,
            // so the one the menu is built with is read now rather than kept
            // from whenever the match started.
            ScenePlayerRegistry? players = _window is { HasScene: true } window
                ? window.Scene.Players : null;
            var menu = new InGameMenu(GameState.LoadSettings(), players);
            menu.Emptied += (_, _) => CloseMenu();
            _menu = menu;
            surface.Show(menu);
            return true;
        }

        /// <summary>Take it down and give the match its input back.</summary>
        internal static void CloseMenu()
        {
            if (_menu == null)
            {
                return;
            }
            _menu = null;
            UiSurface.Current?.Hide();
            PauseMenu.MarkClosed();
        }

        // ------------------------------------------------------------ capture

        private static string? _shotDirectory;
        private static int _shotStep;
        private static int _shotWait;

        /// <summary>
        /// Clicks and hovers the script asked for and the screen did not have.
        ///
        /// Counted rather than printed, because printed is what it was: the
        /// front screen's words became deck buttons, every predicate here went
        /// on matching nothing, and the capture wrote its pictures and exited
        /// zero for weeks while proving none of the pointer arithmetic it
        /// exists to prove. <c>-shellshot</c> now fails when a step could not
        /// press what it named.
        /// </summary>
        public static int ShotMisses { get; private set; }

        /// <summary>
        /// Photograph the shell rather than play in it: `-shellshot DIR`.
        ///
        /// <c>-uishot</c> renders the screens on their own and proves the
        /// layout; this proves what replaced the windows. It walks the whole
        /// loop the change is about -- front screen, a match loaded into the
        /// same window, the pause menu over it, leaving the match, the front
        /// screen again -- and photographs the *window* at each stop, so a
        /// picture that is black or a step that never arrives is a failure
        /// with a name rather than a report that "the launcher looks wrong".
        ///
        /// Escape is pressed on the front screen before anything else: on that
        /// screen it is the quit prompt, so a second picture that differs from
        /// the first is the keyboard reaching a screen that is no longer a
        /// window.
        /// </summary>
        public static void RequestShots(string directory)
        {
            _shotDirectory = directory;
            _shotStep = 0;
            _shotWait = 0;
            ShotMisses = 0;
            // No modal dialog may open while the script is running; see the
            // property, and ClickSettings for what presses one.
            NativeFilePicker.Suppressed = true;
            // The window is the capture's for the duration, not the player's:
            // the script maximizes it half way through, and a screenshot run
            // must not be how somebody's window size changes. See
            // WindowGeometry.Enabled.
            Mods.WindowGeometry.Enabled = false;
        }

        /// <summary>
        /// Between the draw and the buffer swap, which is the only moment the
        /// window's back buffer holds this frame. Called from both halves of
        /// the frame -- with a match and without one.
        ///
        /// A list of steps rather than a switch on a frame number: the script
        /// is a sequence, and every insertion into a numbered one renumbered
        /// the rest.
        /// </summary>
        internal static void AfterDraw(RenderWindow window)
        {
            if (Interlocked.Exchange(ref _firstFrameStarted, 1) == 0)
            {
                LifecycleTiming.FirstFrame();
                StartBackgroundStartupWork();
            }
            Diagnostics.LauncherWindowCheck.AfterDraw(window);
#if MPHREAD_RMLUI_POC
            RmlUiPrototype.AfterDraw(window);
#endif
            ObserveSamusReturn(window);
            if (_shotDirectory == null)
            {
                return;
            }
            if (_shotWait > 0)
            {
                _shotWait--;
                return;
            }
            Action<RenderWindow>[] script = Script;
            if (_shotStep >= script.Length)
            {
                _shotDirectory = null;
                window.Close();
                return;
            }
            script[_shotStep++](window);
        }


        private static void StartBackgroundStartupWork()
        {
            var cancel = new CancellationTokenSource();
            CancellationTokenSource? previous = Interlocked.Exchange(
                ref _startupWorkCancel, cancel);
            previous?.Cancel();
            previous?.Dispose();
            CancellationToken token = cancel.Token;
            _startupWork = Task.Run(() =>
            {
                try
                {
                    DebugLog.Line("startup", "post-first-frame work begin");
                    string[] builtInRooms = Metadata.RoomList
                        .Select(room => room.Name).ToArray();
                    MapGen.MapDefinition[] deferred = MapGen.CustomRooms.DeferInitialRegistration
                        ? MapGen.CustomRooms.DeferredDefinitions(builtInRooms).ToArray()
                        : Array.Empty<MapGen.MapDefinition>();
                    token.ThrowIfCancellationRequested();
                    if (deferred.Length > 0)
                    {
#if MPHREAD_RMLUI_POC
                        if (RmlUiPrototype.Active)
                        {
                            // Keep the RmlUi-only front screen free of an
                            // Avalonia dispatcher dependency. The next game
                            // frame publishes this snapshot on the owner thread.
                            _deferredCustomRoomsPending = deferred;
                            _deferredCustomRoomsToken = token;
                        }
                        else
#endif
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(
                                () => PublishDeferredCustomRooms(deferred, token),
                                Avalonia.Threading.DispatcherPriority.Background);
                        }
                    }

                    Maintenance.RunStartup();
                    token.ThrowIfCancellationRequested();
                    ThumbnailGenerator.EnsureCustomPreviews(
                        line => DebugLog.Line("thumbnails", line), token);
                    token.ThrowIfCancellationRequested();
#if MPHREAD_RMLUI_POC
                    if (!RmlUiPrototype.Active)
#endif
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(
                            () => _front?.BeginDeferredPreviewCatchup(token),
                            Avalonia.Threading.DispatcherPriority.Background);
                    }
                    DebugLog.Line("startup", "post-first-frame work complete");
                }
                catch (OperationCanceledException)
                {
                    DebugLog.Line("startup", "post-first-frame work cancelled");
                }
                catch (Exception ex)
                {
                    // Nothing here is required to reach or use the launcher.
                    // Keep failures diagnostic rather than turning deferred
                    // housekeeping back into a startup failure.
                    DebugLog.Exception("startup-background", ex);
                }
            }, token);
        }


        private static void PublishDeferredCustomRooms(
            IReadOnlyList<MapGen.MapDefinition> definitions, CancellationToken token)
        {
            if (!Active || token.IsCancellationRequested) return;
            // Runtime room metadata is a between-scenes snapshot. If the player
            // launches faster than background catalog discovery finishes, hold
            // publication until ShowFrontScreen runs after that scene unloads.
            if (_window?.HasScene == true)
            {
                _deferredCustomRoomsPending = definitions;
                _deferredCustomRoomsToken = token;
                DebugLog.Line("startup",
                    $"deferred {definitions.Count} custom map registration(s) until the next front screen");
                return;
            }
            _deferredCustomRoomsPending = null;
            try
            {
                foreach (MapGen.MapDefinition definition in definitions)
                {
                    if (token.IsCancellationRequested) return;
                    Metadata.RegisterDownloadedMap(definition);
                }
                IReadOnlyList<string> rooms = ThumbnailGenerator.MultiplayerRooms();
                _rooms = rooms;
                _front?.RefreshDeferredRooms(rooms);
                DebugLog.Line("startup",
                    $"registered {definitions.Count} deferred custom map(s)");
            }
            catch (Exception ex)
            {
                DebugLog.Exception("startup-map-registration", ex);
            }
        }

        private static Vector2i _shotWindowedSize, _shotWindowedLocation;
        private static unsafe void CheckFullscreen(RenderWindow window, WindowStartMode mode)
        {
            var monitor = OpenTK.Windowing.Desktop.Monitors.GetMonitorFromWindow(window);
            var video = GLFW.GetVideoMode(monitor.Handle.ToUnsafePtr<OpenTK.Windowing.GraphicsLibraryFramework.Monitor>());
            bool attached = Mods.WindowMode.HasMonitor(window);
            bool native = mode == WindowStartMode.Fullscreen;
            bool fills = video != null
                && window.ClientSize.X == video->Width
                && (native
                    ? window.ClientSize.Y == video->Height
                    : Math.Abs(window.ClientSize.Y - video->Height) <= 1);
            bool shape = native
                ? attached && window.AutoIconify && !Mods.WindowMode.IsTopmost
                : !attached && !window.AutoIconify
                    && window.WindowBorder == OpenTK.Windowing.Common.WindowBorder.Hidden;
            if (!shape || !fills || Mods.WindowMode.Current != mode)
            {
                ShotMisses++;
                Console.WriteLine($"[shellshot] {mode} invalid: monitor={attached}, client={window.ClientSize}, "
                    + $"display={(video == null ? "unknown" : $"{video->Width}x{video->Height}")}, "
                    + $"border={window.WindowBorder}, autoIconify={window.AutoIconify}, topmost={Mods.WindowMode.IsTopmost}");
            }
            else Console.WriteLine($"[shellshot] {mode} covers the monitor with the expected focus policy: {window.ClientSize}");
        }
        private static void CheckWindowed(RenderWindow window)
        {
            if (Mods.WindowMode.HasMonitor(window) || Mods.WindowMode.IsFullscreen
                || window.ClientSize != _shotWindowedSize || window.Location != _shotWindowedLocation)
            {
                ShotMisses++;
                Console.WriteLine($"[shellshot] windowed geometry was not restored: {window.ClientSize} at {window.Location}");
            }
        }

        /// <summary>
        /// What the capture does, in order. Each step ends by saying how many
        /// frames to leave before the next one -- a window is not on the
        /// screen the moment it is created (a buffer read before the window
        /// manager has mapped it comes back black under Mesa), a room loads
        /// into a fade, and a window mode takes a few frames to settle.
        /// </summary>
        private static Action<RenderWindow>[] Script =>
            Environment.GetCommandLineArgs().Contains("-samusreturncheck") ? SamusReturnScript :
            (Environment.GetCommandLineArgs().Contains("-shelllifecycleonly") ? StandardScript.Take(3) : StandardScript)
            .Concat(_lifecycleTail ??= BuildLifecycleTail()).ToArray();
        private static Action<RenderWindow>[] StandardScript => new Action<RenderWindow>[]
        {
            _ => Wait(20),
            w =>
            {
                Shot(w, "shell-startup");
                var startup = _front?.GetVisualDescendants().OfType<PrimeStartupScreen>().FirstOrDefault();
                if (startup == null) { ShotMisses++; Console.WriteLine("[shellshot] startup gate was missing"); }
                else Key(Keys.Enter);
                Wait(12);
            },
            w => { Shot(w, "shell-startup-transition"); Wait(25); },
            _ =>
            {
                // Asset-free CI opens setup after the startup gate. Dismiss
                // that sheet separately so the next two Escapes exercise
                // opening and closing quit confirmation in either environment.
                if (_front?.Prime.Overlays.GetVisualDescendants().OfType<SetupScreen>().Any() == true)
                    Escape();
                Wait(10);
            },
            w =>
            {
                if (_front?.Prime is not { IsVisible: true, IsEnabled: true })
                { ShotMisses++; Console.WriteLine("[shellshot] startup did not reveal the shell"); }
                if (_front?.Prime.Overlays.IsOpen == true)
                { ShotMisses++; Console.WriteLine("[shellshot] startup overlay still blocks the shell"); }
                Shot(w, "shell-start"); Escape(); Wait(15);
            },
            // Escape on the front screen is the quit prompt, so a second
            // picture that differs from the first is the keyboard reaching a
            // screen that is no longer a window.
            w => { Shot(w, "shell-escape"); Escape(); Wait(10); },
            // The pointer resting on a word, pressing nothing. Every word
            // glides towards the accent and a little to the right over about
            // 140 ms, and this is the one picture that says so -- fifteen
            // frames is a quarter of a second, so the glide is over by the
            // time it is taken. Settings rather than Play, because Play starts
            // amber and was the only word whose reaction was ever visible.
            _ => { HoverFront(); Wait(15); },
            w => { Shot(w, "shell-hover"); Wait(2); },
            // A click on a word rather than a key: the pointer has its own
            // arithmetic between GLFW and a screen drawn into the frame, and a
            // picture of the settings page is that arithmetic being right.
            _ => { ClickSettings(); Wait(15); },
            w => { Shot(w, "shell-click"); Escape(); Wait(10); },
            // Resized, maximised and fullscreen, all on the front screen: a
            // screen that does not follow the window is the fault this
            // sequence exists for.
            w => { w.ClientSize = new Vector2i(1000, 620); Wait(20); },
            w => { Shot(w, "shell-resized"); w.WindowState = OpenTK.Windowing.Common.WindowState.Maximized; Wait(20); },
            w => { Shot(w, "shell-maximized"); w.WindowState = OpenTK.Windowing.Common.WindowState.Normal; Wait(20); },
            w =>
            {
                _shotWindowedSize = w.ClientSize; _shotWindowedLocation = w.Location;
                Mods.WindowMode.Set(w, WindowStartMode.Fullscreen); Wait(25);
            },
            // The click that matters: at 1.5x, where a pointer conversion off
            // by the scale factor puts every press in the corner of the screen
            // and nothing can be pressed at all. The windowed click above
            // cannot catch that -- the scale there is 1.
            w => { CheckFullscreen(w, WindowStartMode.Fullscreen); Shot(w, "shell-fullscreen"); ClickSettings(); Wait(15); },
            w =>
            {
                Shot(w, "shell-fullscreen-click");
                Escape();
                Mods.WindowMode.Toggle(w);
                Wait(25);
            },
            w =>
            {
                CheckWindowed(w); Shot(w, "shell-windowed");
                Mods.WindowMode.Set(w, WindowStartMode.BorderlessFullscreen); Wait(25);
            },
            w =>
            {
                CheckFullscreen(w, WindowStartMode.BorderlessFullscreen); Shot(w, "shell-borderless");
                Mods.WindowMode.Set(w, WindowStartMode.Fullscreen); Wait(20);
            },
            w =>
            {
                CheckFullscreen(w, WindowStartMode.Fullscreen);
                // Xvfb supplies an X server but no window manager. Asking GLFW
                // to iconify a monitor-owned fullscreen window there aborts
                // inside the native X11 path instead of simulating Alt+Tab.
                // The state we own is still testable here: switching from
                // borderless to normal fullscreen must keep the monitor
                // attached, set AutoIconify, and Sync must not mistake that
                // attached fullscreen window for an external fullscreen exit.
                Mods.WindowMode.Sync(w);
                if (!Mods.WindowMode.HasMonitor(w)
                    || Mods.WindowMode.Current != WindowStartMode.Fullscreen
                    || !w.AutoIconify)
                {
                    ShotMisses++;
                    Console.WriteLine("[shellshot] fullscreen focus policy lost its monitor/state");
                }
                Mods.WindowMode.Leave(w); Wait(25);
            },
            w => { CheckWindowed(w); Wait(5); },
            // F11 on the front screen. The shell takes the whole keyboard
            // while a screen is up, so this is the one gesture that has to be
            // handled before it does -- and it was not, which made fullscreen
            // the one thing the launcher could not do.
            w => { WindowKey(w, Keys.F11); Wait(20); },
            w =>
            {
                Shot(w, "shell-launcher-fullscreen");
                if (!Mods.WindowMode.IsFullscreen)
                {
                    ShotMisses++;
                    Console.WriteLine("[shellshot] F11 did not reach the window on the front screen");
                }
                WindowKey(w, Keys.F11);
                Wait(20);
            },
            // Through the screens rather than by handing the shell a plan:
            // picking a map is two presses now -- one to select the row, one
            // on the tick -- and the picture after the first of them has to be
            // the play screen and not a match.
            _ => { ClickIfReady(c => FrontAction(c, "PLAY")); Wait(20); },
            // A server that answered, and the drawer it opens beside the list.
            // The browser is the one face where the picture after the click is
            // not the same screen plus a highlight.
            w =>
            {
                Shot(w, "shell-play");
                // Directory availability is external to the layout smoke test.
                UiSurface.Current?.ClickOn(c => c is ServerRow row && row.IsLive);
                Wait(25);
            },
            w =>
            {
                Shot(w, "shell-server-side");
                ClickIfReady(c => c.GetValue(ControllerNav.NavIdProperty) == "multiplayer.create");
                Wait(15);
            },
            w =>
            {
                Shot(w, "shell-create-lobby");
                // Clean CI machines reach Game Files setup instead of Create Lobby.
                if (GameFiles.Ready && (_front?.Prime.Overlays.IsOpen != true || Mods.Render.LauncherHunter.Drawn))
                { ShotMisses++; Console.WriteLine("[shellshot] create lobby must cover the native hunter preview"); }
                Escape(); Wait(90);
            },
            w => { Shot(w, "shell-create-lobby-closed"); ClickIfReady(c => FrontAction(c, "OFFLINE")); Wait(15); },
            // A card, not a row: the offline face is every map at once now.
            // See DeckTile.
            // Land the pointer in the middle of the grid, then scroll it for
            // two seconds: this is the one step that measures a steady state
            // rather than photographing a moment. See Scroll.
            w =>
            {
                Shot(w, "shell-play-offline");
                UiSurface.Current?.PointerMoved(w.ClientSize.X / 2.0, w.ClientSize.Y / 2.0);
                Scroll(60);
                Scroll(60, 1);
                Wait(10);
            },
            w => { ClickIfReady(c => c.GetValue(ControllerNav.NavIdProperty) == "offline.map"); Wait(25); },
            w =>
            {
                // Still the play screen, with the drawer open beside it: a
                // press on a card picks it and starts nothing. A picture of a
                // room here is the regression.
                Shot(w, "shell-play-selected");
                ClickIfReady(c => c is DeckTile);
                ClickIfReady(c => c.GetValue(ControllerNav.NavIdProperty) == "map-picker.use");
                ClickIfReady(c => c.GetValue(ControllerNav.NavIdProperty) == "offline.start");
                Wait(40);
            },
            w =>
            {
                if (w.HasScene)
                {
                    Wait(0);
                    return;
                }
                // CI intentionally has no extracted game assets. In that
                // environment the shell/UI checks above are the complete test:
                // there is no legal room to start, so falling through to a
                // direct match request only converts "no assets" into a false
                // failure.
                if (!GameFiles.Ready || _rooms.Count == 0)
                {
                    Console.WriteLine("[shellshot] no game files; screen-only checks complete");
                    _shotDirectory = null;
                    w.Close();
                    return;
                }

                // A real installation had rooms available but the UI failed
                // to start one. Fall back to the direct shell plan so the rest
                // of the match/pause/result lifecycle can still be exercised,
                // while the earlier missed press remains visible in ShotMisses.
                Console.WriteLine("[shellshot] the play screen started nothing; asking directly");
                if (!StartShotMatch())
                {
                    ShotMisses++;
                    Console.WriteLine("[shellshot] game files are ready but no playable room was available");
                    _shotDirectory = null;
                    w.Close();
                    return;
                }
                Wait(40);
            },
            // Made bigger *during* the match, with no screen up: the surface
            // hears nothing about the window until a screen is shown again,
            // which is the state Escape then has to size itself against.
            w => { Shot(w, "shell-match"); w.WindowState = OpenTK.Windowing.Common.WindowState.Maximized; Wait(30); },
            w => { PauseMenu.HandleEscape(w); Wait(20); },
            w =>
            {
                Shot(w, "shell-pause-maximized");
                PauseMenu.HandleEscape(w);
                w.WindowState = OpenTK.Windowing.Common.WindowState.Normal;
                Wait(30);
            },
            w => { Mods.WindowMode.Toggle(w); Wait(30); },
            // The pause menu over a fullscreen match, which is where a menu
            // drawn into the wrong viewport shows up worst.
            w => { Shot(w, "shell-match-fullscreen"); PauseMenu.HandleEscape(w); Wait(20); },
            w => { Shot(w, "shell-pause-fullscreen"); Mods.WindowMode.Toggle(w); Wait(30); },
            // The settings, in a match and in the smallest window the
            // sequence uses: the page that has to fit is this one, and the
            // in-game screens are drawn larger than the launcher's.
            w => { Shot(w, "shell-pause"); Click(c => FrontAction(c, "SETTINGS")); Wait(20); },
            w => { Shot(w, "shell-settings-ingame"); Escape(); Wait(15); },
            // The match *ending*, in fullscreen, rather than being left: a
            // different path out (the engine fades and quits the scene itself)
            // and the one the front screen comes back from. "The text is small
            // again in the launcher when the game finishes" was about this
            // frame and not about the one Leave match produces.
            w => { Escape(); Mods.WindowMode.Toggle(w); Wait(30); },
            // The results, held rather than allowed to run their clock: the
            // engine's own scoreboard on the left and the deck panel on the
            // right, which is the one picture that shows the two halves
            // together. The real screen runs for ten seconds and then
            // rotates, and a capture that has to be caught inside ten seconds
            // is a capture that fails on a slow machine.
            _ => { HoldResults(); Wait(60); },
            w =>
            {
                Shot(w, "shell-endgame");
                // Some rule sets do not expose character changes at results.
                UiSurface.Current?.ClickOn(c => c is DeckButton tab && tab.Text == "Change hunter");
                Wait(30);
            },
            w => { Shot(w, "shell-endgame-hunter"); MapPick.Reset(); ReleaseResults(); Wait(90); },
            w =>
            {
                CheckShotRematch(w, _played?.RoomKey);
                Shot(w, "shell-bot-rematch"); HoldResults(); Wait(20);
            },
            _ =>
            {
                _shotNextRoom = MapPick.Order.FirstOrDefault(room => !MapPick.IsReturnToLobby(room));
                if (_shotNextRoom != null) MapPick.Choose(MapPick.IndexOf(_shotNextRoom));
                ReleaseResults(); Wait(90);
            },
            w =>
            {
                CheckShotRematch(w, _shotNextRoom);
                Shot(w, "shell-bot-next-map"); RequestEndMatch(); Wait(90);
            },
            w => { Shot(w, "shell-back-fullscreen"); Mods.WindowMode.Toggle(w); Wait(25); },
            w => { Shot(w, "shell-back"); Wait(5); }
        };

        private static Action<RenderWindow>[]? _lifecycleTail;
        private static ModernGraphicsCompat.ResourceCounts? _lifecycleResources;
        private static uint _lifecycleSeekTarget;
        private static int _lifecycleDeviceGeneration;

        // Optional content-backed acceptance extension. Ordinary shell captures
        // keep their existing sequence; this path requires a validated recording.
        private static Action<RenderWindow>[] BuildLifecycleTail()
        {
            string[] args = Environment.GetCommandLineArgs();
            int option = Array.IndexOf(args, "-shelllifecycle");
            if (option < 0) return Array.Empty<Action<RenderWindow>>();
            if (option + 1 >= args.Length || args[option + 1].StartsWith('-'))
                throw new ArgumentException("-shelllifecycle requires a playable replay file.");
            string replay = args[option + 1];
            using (var reader = DemoReader.Open(replay, out var result, metadataOnly: true))
            {
                if (reader == null) throw new InvalidOperationException($"Lifecycle replay metadata rejected: {result}");
                Console.WriteLine($"[shelllifecycle] replay format={reader.FormatVersion} protocol={reader.ProtocolVersion} frames={reader.DurationFrames}");
            }
            bool advanced = Array.IndexOf(args, "-renderadvanced") >= 0;
            var steps = new List<Action<RenderWindow>>();
            for (int cycle = 1; cycle <= 2; cycle++)
            {
                int pass = cycle;
                steps.Add(w => { if (advanced) { RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Extreme);
                    RenderOptions.InternalHdr = true; RenderOptions.DeferredPbr = true;
                    RenderOptions.AntiAliasing = AntiAliasingMode.Taa; }
                    RequireLifecycle(StartShotMatch(), "playable match"); Wait(90); });
                steps.Add(w =>
                {
                    RequireLifecycle(w.HasScene, "match started");
                    if (advanced && ModernGraphicsCompat.Active)
                        RequireLifecycle(w.Scene.ValidateModernAdvancedRendering(), "advanced scene targets and temporal history");
                    if (pass == 1 && ModernGraphicsCompat.Active)
                    {
                        _lifecycleDeviceGeneration = ModernGraphicsCompat.DeviceGeneration;
                        ModernGraphicsCompat.DestroyDeviceForCheck();
                    }
                    Wait(10);
                });
                steps.Add(w =>
                {
                    if (pass == 1 && _lifecycleDeviceGeneration != 0)
                    {
                        RequireLifecycle(ModernGraphicsCompat.Active
                            && ModernGraphicsCompat.DeviceGeneration == _lifecycleDeviceGeneration + 1,
                            "actual scene recovered its modern device without fallback");
                        RequireLifecycle(FinalCompositeCapture.Read(w.FramebufferSize.X, w.FramebufferSize.Y).Any(b => b > 3),
                            "recovered scene is not fully black");
                        if (advanced) RequireLifecycle(w.Scene.ValidateModernAdvancedRendering(),
                            "advanced targets and temporal history restored after device loss");
                        Shot(w, "lifecycle-device-recovery");
                    }
                    SpectatorMode.Start(); Wait(30);
                });
                steps.Add(w =>
                {
                    RequireLifecycle(SpectatorMode.IsSpectating, "spectator transition");
                    Shot(w, $"lifecycle-{pass}-spectator"); SpectatorMode.Rejoin(); Wait(30);
                });
                steps.Add(w =>
                {
                    RequireLifecycle(!SpectatorMode.IsSpectating, "player rejoin");
                    Shot(w, $"lifecycle-{pass}-rejoin"); RequestEndMatch(); Wait(90);
                });
                steps.Add(w => { RequireLifecycle(!w.HasScene, "match teardown"); _front!.OpenMapStudio(); Wait(45); });
                steps.Add(w =>
                {
                    RequireLifecycle(_front!.Prime.Router.Current == PrimeRoute.Forge
                        && _front.GetVisualDescendants().OfType<MapStudioScreen>().Any(), "Map Studio opened");
                    Shot(w, $"lifecycle-{pass}-forge"); _front.Prime.Overlays.Clear();
                    _front.Prime.Router.Navigate(PrimeRoute.Play); Wait(30);
                });
                steps.Add(w =>
                {
                    Decided(new LaunchPlan { Kind = LaunchKind.Demo, DemoPath = replay, Hunter = Hunter.Samus });
                    Wait(90);
                });
                steps.Add(w =>
                {
                    RequireLifecycle(w.HasScene && DemoPlayback.IsActive && DemoPlayback.LastResult == ReplayOpenResult.Success,
                        "replay opened: " + DemoPlayback.LastError);
                    Shot(w, $"lifecycle-{pass}-replay");
                    // Simulate another presented scene consuming the process-wide
                    // notification: this replica still must observe the mode itself.
                    Replay.ReplayCamera.SetMode(Replay.ReplayCameraMode.Free);
                    Replay.ReplayCamera.Changed = false; Wait(3);
                });
                steps.Add(w =>
                {
                    RequireLifecycle(w.Scene.IsFreeCam, "replay free camera after consumed global notification");
                    Replay.ReplayCamera.SetMode(Replay.ReplayCameraMode.FirstPerson);
                    Replay.ReplayCamera.Changed = false; Wait(3);
                });
                steps.Add(w =>
                {
                    RequireLifecycle(w.Scene.CameraMode == CameraMode.Player && !w.Scene.IsFreeCam,
                        "replay first-person camera after consumed global notification");
                    _lifecycleSeekTarget = Math.Min(300u, DemoPlayback.LastFrame);
                    ReplayController.Seek(_lifecycleSeekTarget, resume: false); Wait(90);
                });
                steps.Add(w =>
                {
                    RequireLifecycle(!ReplayController.IsSeeking && DemoPlayback.CurrentFrame == _lifecycleSeekTarget,
                        $"replay seek {_lifecycleSeekTarget}, actual {DemoPlayback.CurrentFrame}");
                    RequireLifecycle(DemoPlayback.PresentationScene != null, "replay replica ready for presentation");
                    RequireLifecycle(w.Scene.CameraMode == CameraMode.Player && !w.Scene.IsFreeCam,
                        "first-person replay camera initialized on the presented replica");
                    Console.WriteLine($"[shelllifecycle] replay camera={w.Scene.CameraPosition} main={w.Scene.Players.MainPlayerIndex} actor={w.Scene.Players.Main.Position} warning={DemoPlayback.LastWarning}");
                    Shot(w, $"lifecycle-{pass}-replay-seek"); RequestEndMatch(); Wait(90);
                });
                steps.Add(w =>
                {
                    RequireLifecycle(!w.HasScene && !DemoPlayback.IsActive, "replay teardown");
                    Shot(w, $"lifecycle-{pass}-return");
                    if (ModernGraphicsCompat.Active)
                    {
                        var now = ModernGraphicsCompat.LiveResources;
                        RequireLifecycle(now.BindGroups == 0, "no retained bind groups at lifecycle boundary");
                        if (_lifecycleResources is { } before)
                            RequireLifecycle(now.Textures <= before.Textures && now.Renderbuffers <= before.Renderbuffers
                                && now.Programs <= before.Programs && now.Lists <= before.Lists
                                && now.Views <= before.Views && now.Samplers <= before.Samplers
                                && now.Geometry <= before.Geometry && now.Surfaces == before.Surfaces
                                && now.ShaderModules <= before.ShaderModules,
                                $"retained resources grew: before={before}, after={now}");
                        // Uniform/transient buffers and pipelines are retained high-water caches.
                        // Log them rather than treating different bot draw loads as identical workloads.
                        _lifecycleResources = now;
                        Console.WriteLine($"[shelllifecycle] pass={pass} resources={now}");
                    }
                    Console.WriteLine($"[shelllifecycle] PASS cycle={pass} spectator/rejoin/Forge/replay/seek/return");
                    Wait(10);
                });
            }
            return steps.ToArray();
        }
        private static void RequireLifecycle(bool valid, string message)
        {
            if (!valid) { ShotMisses++; throw new InvalidOperationException("Shell lifecycle failed: " + message); }
        }

        private static void Wait(int frames)
        {
            _shotWait = frames;
        }

        // Setup and quit confirmation are dismissed before targeting the header.
        private static void ClickSettings() => Click(c => FrontAction(c, "SETTINGS"));
        private static void HoverFront() => Hover(c => FrontAction(c, "SETTINGS"));

        /// <summary>
        /// The steps that need a room to load. Skipped rather than failed
        /// where there are no game files: a browser with no rooms in it has no
        /// row to select and no tick to press, and that is the machine CI is,
        /// not a fault.
        /// </summary>
        private static bool FrontAction(Control control, string label) =>
            control is HubNavButton hub
                && String.Equals(hub.Label, label, StringComparison.OrdinalIgnoreCase)
            || control is DeckButton deck
                && String.Equals(deck.Text, label, StringComparison.OrdinalIgnoreCase);

        private static void ClickIfReady(Func<Control, bool> match)
        {
            if (GameFiles.Ready)
            {
                Click(match);
            }
        }

        private static void Hover(Func<Control, bool> match)
        {
            if (!(UiSurface.Current?.HoverOn(match) ?? false))
            {
                ShotMisses++;
                Console.WriteLine("[shellshot] nothing on screen matched the hover");
            }
        }

        private static void Click(Func<Control, bool> match)
        {
            if (!(UiSurface.Current?.ClickOn(match) ?? false))
            {
                ShotMisses++;
                Console.WriteLine("[shellshot] nothing on screen matched the click");
            }
        }

        private static void Key(Keys key)
        {
            UiSurface? surface = UiSurface.Current;
            surface?.KeyDown(key, Avalonia.Input.RawInputModifiers.None);
            surface?.KeyUp(key, Avalonia.Input.RawInputModifiers.None);
        }

        /// <summary>
        /// Scroll whatever is under the pointer, a notch at a time, for a
        /// number of frames.
        ///
        /// Here because "the grid does not scroll smoothly" is a report about
        /// a *steady state* and every other step in this harness is a single
        /// act. A notch a frame across a screenful of cards is what the
        /// player is doing when they say it is slow, and the debug log's
        /// per-second `ui` line is what answers it -- which is why this is a
        /// step rather than something measured by hand.
        /// </summary>
        private static void Scroll(int frames, double notches = -1)
        {
            UiSurface? surface = UiSurface.Current;
            if (surface == null)
            {
                return;
            }
            for (int i = 0; i < frames; i++)
            {
                surface.PointerWheel(0, notches);
                Wait(1);
            }
        }

        /// <summary>
        /// A key put through the *window's* handler rather than handed to the
        /// surface.
        ///
        /// <see cref="Key"/> is for testing a screen and goes straight to
        /// UiSurface; that skips everything the window claims first, so it can
        /// never catch a key the window was supposed to take and did not.
        /// </summary>
        private static void WindowKey(RenderWindow window, Keys key)
        {
            window.FeedKey(new OpenTK.Windowing.Common.KeyboardKeyEventArgs(
                key, scanCode: 0, 0,
                isRepeat: false));
        }

        private static void Escape()
        {
            UiSurface? surface = UiSurface.Current;
            surface?.KeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Escape,
                Avalonia.Input.RawInputModifiers.None);
            surface?.KeyUp(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Escape,
                Avalonia.Input.RawInputModifiers.None);
        }

        /// <summary>An offline match on the first room the build knows.</summary>
        private static bool StartShotMatch()
        {
            if (_rooms.Count == 0)
            {
                return false;
            }
            Decided(new LaunchPlan
            {
                Kind = LaunchKind.Offline,
                RoomKey = _rooms[0],
                Mode = GameMode.Battle,
                Hunter = Hunter.Samus,
                Bots = 1,
                BotLevel = 1
            });
            return true;
        }

        /// <summary>
        /// End the match the way running out of time does: the engine plays
        /// its own end sequence, fades and quits the scene. Shortened to a
        /// fifth of a second of results, since the capture is about what comes
        /// after it.
        /// </summary>
        /// <summary>
        /// Put the match at its results and keep it there.
        ///
        /// The continuation check shortens the results to a fifth of a
        /// second because the capture it belongs to is about what comes
        /// *after* them. This one is about the results themselves, so the
        /// clock is given thirty seconds instead -- the screen it photographs
        /// is the engine's scoreboard and the deck panel beside it, and both
        /// have to be settled before the shutter.
        ///
        /// The ballot is begun by hand as well: on a server it arrives in a
        /// packet, and there is no server here.
        /// </summary>
        private static void HoldResults()
        {
            // `Ending`, not `GameOver`. Both satisfy EndScreen.Available, so
            // the deck panel comes up either way -- but the HUD draws the
            // scoreboard on `Ending` and only the words GAME OVER on
            // `GameOver`, and the scoreboard is the half of this picture the
            // theme does not touch. A capture without it is a capture of half
            // the screen.
            GameState.MatchState = MatchState.Ending;
            GameState.MatchTime = 30;
            try
            {
                MapPick.Begin(_settings.RoomKey ?? "", open: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[shellshot] no ballot to show: {ex.Message}");
            }
        }

        /// <summary>Let the clock run again, so the sequence can leave.</summary>
        private static void ReleaseResults()
        {
            GameState.MatchTime = 0.2f;
        }

        private static string? _shotNextRoom;
        private static void CheckShotRematch(RenderWindow window, string? expected)
        {
            int expectedId = expected == null ? -1 : Metadata.GetRoomByName(expected).Item2;
            if (!window.HasScene || GameState.MatchState != MatchState.InProgress
                || expectedId < 0 || window.Scene.RoomId != expectedId)
            {
                ShotMisses++;
                Console.WriteLine($"[shellshot] bot rematch failed: expected {expected}, scene={window.HasScene}, state={GameState.MatchState}");
            }
            else Console.WriteLine($"[shellshot] bot rematch loaded {expected}");
        }

        private static void Shot(RenderWindow window, string name)
        {
            string path = System.IO.Path.Combine(_shotDirectory!, $"{name}.png");
            System.IO.Directory.CreateDirectory(_shotDirectory!);
            Console.WriteLine($"[shellshot] {name}: pixels={window.FramebufferSize} "
                + $"scene={(window.HasScene ? window.Scene.Size.ToString() : "no match")} "
                + $"{UiSurface.Current?.Describe()}");
            bool saved = Mods.ScreenCapture.SaveWindow(
                window.FramebufferSize.X, window.FramebufferSize.Y, path);
            if (saved)
                Mods.Render.FinalCompositeCapture.WriteEvidence(path, window.FramebufferSize.X,
                    window.FramebufferSize.Y, "Application final composite: after scene and shell overlay/hunter, before Present");
            else ShotMisses++;
            Console.WriteLine(saved
                ? $"[shellshot] {path}"
                : $"[shellshot] {name} could not be read from the window");
        }

        // -------------------------------------------------------------- input

        public static void PointerMoved(double x, double y)
        {
#if MPHREAD_RMLUI_POC
            if (RmlUiPrototype.Active) { RmlUiPrototype.PointerMoved(x, y); return; }
#endif
            UiSurface.Current?.PointerMoved(x, y);
        }

        public static void PointerButton(MouseButton button, double x, double y, bool down)
        {
#if MPHREAD_RMLUI_POC
            if (RmlUiPrototype.Active) { RmlUiPrototype.PointerButton(button, x, y, down); return; }
#endif
            UiSurface? surface = UiSurface.Current;
            if (surface == null)
            {
                return;
            }
            // Where the click landed, before the click itself: the pointer is
            // the system's while a screen is up, so this is the first the
            // toolkit hears of it if the player moved and clicked between two
            // frames.
            surface.PointerMoved(x, y);
            surface.PointerButton(Translate(button), down);
        }

        public static void PointerWheel(double deltaX, double deltaY)
        {
#if MPHREAD_RMLUI_POC
            if (RmlUiPrototype.Active) { RmlUiPrototype.PointerWheel(deltaX, deltaY); return; }
#endif
            UiSurface.Current?.PointerWheel(deltaX, deltaY);
        }

        public static void KeyDown(KeyboardKeyEventArgs e)
        {
#if MPHREAD_RMLUI_POC
            if (RmlUiPrototype.Active) { RmlUiPrototype.KeyDown(e); return; }
#endif
            UiSurface.Current?.KeyDown(e.Key, Modifiers(e));
        }

        public static void KeyUp(KeyboardKeyEventArgs e)
        {
#if MPHREAD_RMLUI_POC
            if (RmlUiPrototype.Active) { RmlUiPrototype.KeyUp(e); return; }
#endif
            UiSurface.Current?.KeyUp(e.Key, Modifiers(e));
        }

        public static void TextInput(string text)
        {
#if MPHREAD_RMLUI_POC
            if (RmlUiPrototype.Active) { RmlUiPrototype.TextInput(text); return; }
#endif
            UiSurface.Current?.TextInput(text);
        }

        private static Avalonia.Input.MouseButton Translate(MouseButton button)
        {
            return button switch
            {
                MouseButton.Button2 => Avalonia.Input.MouseButton.Right,
                MouseButton.Button3 => Avalonia.Input.MouseButton.Middle,
                MouseButton.Button4 => Avalonia.Input.MouseButton.XButton1,
                MouseButton.Button5 => Avalonia.Input.MouseButton.XButton2,
                _ => Avalonia.Input.MouseButton.Left
            };
        }

        private static Avalonia.Input.RawInputModifiers Modifiers(KeyboardKeyEventArgs e)
        {
            Avalonia.Input.RawInputModifiers modifiers = Avalonia.Input.RawInputModifiers.None;
            if (e.Shift)
            {
                modifiers |= Avalonia.Input.RawInputModifiers.Shift;
            }
            if (e.Control)
            {
                modifiers |= Avalonia.Input.RawInputModifiers.Control;
            }
            if (e.Alt)
            {
                modifiers |= Avalonia.Input.RawInputModifiers.Alt;
            }
            if (e.Command)
            {
                // Avalonia's native macOS text shortcuts use Meta. Without
                // forwarding OpenTK's Command modifier, Cmd+C/X/V reached the
                // focused TextBox as plain C/X/V instead of clipboard commands.
                modifiers |= Avalonia.Input.RawInputModifiers.Meta;
            }
            return modifiers;
        }
    }
}
