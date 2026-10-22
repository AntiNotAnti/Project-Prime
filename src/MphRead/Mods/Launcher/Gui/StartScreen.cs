using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using MphRead.Mods.Network;
using MphRead.Mods.Update;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>Setup, updates and game handoff around one persistent navigation shell.</summary>
    internal sealed class StartScreen : UserControl, IDisposable
    {
        private MenuSettings _settings;
        private readonly List<string> _rooms;
        private readonly PrimeShell _prime;
        private readonly Panel _layers = new();
        private PrimeStartupScreen? _startup;
        private bool _nativeStartupPending;
        internal bool RmlUiStartupOwnerCurrent => !_finished && _nativeStartupPending
            && _startup == null && !_prime.Overlays.IsOpen;
        internal Func<bool>? RmlUiTryOpenStartup { get; set; }
        internal bool RmlUiStartupPending => _nativeStartupPending;
        internal bool RmlUiUpdateOwnerCurrent => !_finished && !_nativeStartupPending && _startup == null
            && !_prime.Overlays.IsOpen && GameFiles.Ready && !NetSession.Active && !DemoPlayback.IsActive;
        internal Func<UpdateInfo, string, Action<bool>, bool>? RmlUiTryOpenUpdatePrompt { get; set; }
        internal void RmlUiPollUpdatePrompt() => TryShowUpdatePrompt(nativeOwnerTick: true);
        internal void RmlUiCompleteStartup()
        {
            if (!RmlUiStartupOwnerCurrent) return;
            _nativeStartupPending = false;
            _prime.IsVisible = true; _prime.IsEnabled = true;
            ShowInitialPrompt();
        }
        private readonly LobbySessionCoordinator _session = new();
        private LobbyScreen? _lobby;
        private bool _finished, _updating, _bypassGuard;
        private bool _returnToMapStudio;
        private bool _spectateNextMatch;
        private readonly PrimeUiPulse _updateWatcher;
        private UpdateInfo? _pendingUpdatePrompt;
        private string? _lastPromptedUpdateTag;
        private bool _loadingVersions;
        public LaunchPlan Plan { get; private set; }
        public event EventHandler<LaunchPlan>? Done;
        public event EventHandler<LaunchPlan>? MatchRequested;
        internal PrimeShell Prime => _prime;
        internal bool RmlUiCanPresent => !_nativeStartupPending && _startup == null && !_prime.Overlays.IsOpen;
        internal event Action? RmlUiReady;
        internal Func<bool>? RmlUiTryOpenGameFiles;
        internal bool RmlUiGameFilesOwnerCurrent => !_finished && !_nativeStartupPending && _startup == null && !_prime.Overlays.IsOpen
            && !NetSession.Active && !DemoPlayback.IsActive;
        internal void RmlUiGameFilesFallback() { if (RmlUiGameFilesOwnerCurrent) OpenSetup(); }
        internal void RmlUiGameFilesCompleted()
        {
            if (!RmlUiGameFilesOwnerCurrent || !GameFiles.Ready) return;
            RefreshRooms(); _prime.Refresh(); BeginDeferredPreviewCatchup();
        }
        internal string[] RmlUiRoomKeys => _rooms.ToArray();
        internal MphRead.Mods.Launcher.RmlUi.Core.RmlUiOfflineRules RmlUiLocalRules
            => MphRead.Mods.Launcher.RmlUi.Core.RmlUiOfflineRules.Capture(_settings);

        internal bool RmlUiNavigate(PrimeRoute route)
            => _startup == null && !_prime.Overlays.IsOpen && _prime.Router.Navigate(route);

        internal bool RmlUiOpenCreateLobby()
        {
            if (_startup != null || _prime.Overlays.IsOpen || NetSession.Active) return false;
            if (!EnsureGameFiles()) return false;
#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
            if (MphRead.Mods.Launcher.RmlUi.Render.RmlUiOpenGlSmoke.NativeUiRequired)
                return MphRead.Mods.Launcher.RmlUi.Render.RmlUiOpenGlSmoke.OpenNativeCreateLobby();
#endif
            OpenCreateServer();
            return _prime.Overlays.IsOpen;
        }

        internal bool RmlUiAcceptJoin(LaunchPlan plan)
        {
            if (_startup != null || _prime.Overlays.IsOpen || !NetSession.Active)
                return false;
            ConnectedOrFinished(plan);
            return true;
        }

        internal bool RmlUiLaunchReplay(MphRead.Mods.Launcher.RmlUi.Core.RmlUiReplayEntry entry,
            bool openEditor = false)
        {
            if (_finished || _startup != null || _prime.Overlays.IsOpen
                || NetSession.Active || !entry.Playable || !CanLaunchLocal())
                return false;
            // A bounded worker performed the same replay protocol/map preflight
            // used by the existing Theatre. The service rechecks the file stamp
            // just before entering this already-established Finish path.
            Finish(new LaunchPlan
            {
                Kind = LaunchKind.Demo, DemoPath = entry.Path,
                Hunter = Hunter.Samus, PlayerName = "", RoomKey = "",
                OpenNativeReplayEditor = openEditor
            });
            return _finished;
        }

        internal bool RmlUiLaunchAdventure(
            MphRead.Mods.Launcher.RmlUi.Core.RmlUiAdventureSlot expected,
            bool newGame, Hunter hunter)
        {
            if (_finished || _startup != null || _prime.Overlays.IsOpen || NetSession.Active
                || (uint)hunter >= 7 || expected.Slot is < 1 or > AdventureSave.SlotCount
                || expected.Unreadable || (!newGame && !expected.Used)
                || !CanLaunchLocal()) return false;
            AdventureSave.SlotInfo latest = AdventureSave.Read(expected.Slot);
            if (latest.Used != expected.Used || latest.Unreadable != expected.Unreadable
                || latest.Area != expected.Area || latest.Octoliths != expected.Octoliths
                || latest.Health != expected.Health || latest.HealthMax != expected.HealthMax
                || AdventureSave.ContentFingerprint(expected.Slot) != expected.Fingerprint)
                return false;
            LaunchPlan plan = AdventureLaunch.Create(expected.Slot, newGame, hunter);
            Finish(plan);
            return _finished;
        }

        internal bool CanManageAdventureSaves => !_finished && _startup == null && !_prime.Overlays.IsOpen
            && !NetSession.Active && !DemoPlayback.IsActive && !MatchStart.HasPendingCommit && Menu.NeededSave == SaveWhen.Never;

        internal MphRead.Mods.Launcher.RmlUi.Core.RmlUiOfflineRulesSaveResult RmlUiSaveOfflineRules(
            MphRead.Mods.Launcher.RmlUi.Core.RmlUiOfflineRulesChange change,
            System.Threading.CancellationToken cancellation)
        {
            string error = "LOCAL RULES REFUSED // CURRENT SETTINGS OWNER UNAVAILABLE";
            if (!CanManageAdventureSaves || cancellation.IsCancellationRequested)
                return new(change, false, error, RmlUiLocalRules);
            // The lease is thread-affine. Acquire, compare, persist and release
            // synchronously on this existing UI/settings owner.
            using var write = MphRead.Mods.Settings.SettingsPersistence.BeginWrite();
            if (write == null)
                return new(change, false, "SETTINGS WERE REPLACED // RESTART BEFORE SAVING", RmlUiLocalRules);
            if (!change.Validate(out error) || !CanManageAdventureSaves || cancellation.IsCancellationRequested)
                return new(change, false, error.Length > 0 ? error : "RULE SAVE REQUEST RETIRED", RmlUiLocalRules);
            bool saved = MphRead.Mods.Launcher.RmlUi.Core.RmlUiOfflineRules.TryCommit(
                _settings, change.Expected, change.Next, GameState.CommitSettings, out error);
            var current = RmlUiLocalRules;
            if (saved && current == change.Next) Mods.GameSettings.Apply(_settings);
            else saved = false;
            return new(change, saved, saved ? "LOCAL MATCH RULES SAVED"
                : error.Length > 0 ? error : "RULE SAVE OUTCOME UNVERIFIED // REOPEN RULES TO VERIFY", current);
        }

        internal bool RmlUiManageAdventureSave(MphRead.Mods.Launcher.RmlUi.Core.RmlUiAdventureSaveChange change, out string status,
            System.Threading.CancellationToken cancellationToken = default)
        {
            status = "SAVE CHANGE REFUSED // ACTIVE OR PENDING SESSION, OR CHANGED SAVE BYTES; REFRESH SAVES";
            if (!CanManageAdventureSaves || !MphRead.Mods.Launcher.RmlUi.Core.RmlUiAdventureSaveIdentity.Valid(change)) return false;
            bool Matches(MphRead.Mods.Launcher.RmlUi.Core.RmlUiAdventureSlot expected)
            {
                AdventureSave.SlotInfo latest = AdventureSave.Read(expected.Slot);
                return latest.Used == expected.Used && latest.Unreadable == expected.Unreadable && latest.Area == expected.Area
                    && latest.Octoliths == expected.Octoliths && latest.Health == expected.Health && latest.HealthMax == expected.HealthMax
                    && AdventureSave.ContentFingerprint(expected.Slot) == expected.Fingerprint;
            }
            if (!Matches(change.Source) || change.Destination is { } destination && !Matches(destination)
                || !CanManageAdventureSaves || cancellationToken.IsCancellationRequested) return false;
            return change.Action == MphRead.Mods.Launcher.RmlUi.Core.RmlUiAdventureSaveAction.Copy
                ? AdventureSave.TryCopy(change.Source.Slot, change.Destination!.Value.Slot, change.Source.Fingerprint, change.Destination.Value.Fingerprint, out status)
                : AdventureSave.TryArchive(change.Source.Slot, change.Source.Fingerprint, out status);
        }

        internal bool RmlUiLaunchCampaignShip(LaunchPlan plan, Campaign.CampaignDockReceipt expected, string fingerprint)
        {
            if (_finished || _startup != null || _prime.Overlays.IsOpen || NetSession.Active
                || plan.Kind != LaunchKind.Adventure || plan.NewGame || (uint)plan.Hunter >= 7
                || plan.SaveSlot != expected.Slot || expected.Slot is < 1 or > AdventureSave.SlotCount
                || plan.AdventureDestination is not { } destination
                || plan.ExitShipToSurface && destination != expected.Planet
                || MatchStart.RecentShipDock != expected
                || !Core.NativeCampaignShipController<LaunchPlan>.ValidFingerprint(fingerprint)
                || AdventureSave.ContentFingerprint(expected.Slot) != fingerprint || !CanLaunchLocal()) return false;
            if (!Campaign.CampaignShipTravel.TryChooseLanding(GameState.PeekSave(expected.Slot),
                MatchStart.RecentShipDock, expected.Slot, newGame: false, destination, out _, out _)
                || MatchStart.RecentShipDock != expected
                || AdventureSave.ContentFingerprint(expected.Slot) != fingerprint) return false;
            Finish(plan);
            return _finished;
        }

        internal bool RmlUiLaunchTraining(MphRead.Mods.Training.AimTrainerDefinition definition,
            Hunter hunter, int suit)
        {
            if (_finished || _startup != null || _prime.Overlays.IsOpen || NetSession.Active
                || (uint)hunter >= 7 || suit is < 0 or > 3 || !CanLaunchLocal()) return false;
            // The shared AimTrainerLaunch validates/sanitizes the native
            // definition, persists preferences and launches the actual local
            // target simulation through the existing StartScreen.Finish flow.
            LaunchPlan plan = AimTrainerLaunch.Create(definition.Sanitize(), hunter, suit);
            Finish(plan);
            return _finished;
        }

        internal bool RmlUiLaunchOffline(MphRead.Mods.Launcher.RmlUi.Core.RmlUiOfflineRequest request)
        {
            if (_finished || _startup != null || _prime.Overlays.IsOpen
                || NetSession.Active || !_rooms.Contains(request.RoomKey)
                || !CanLaunchLocal() || !request.ValidateForm(_rooms)) return false;
            // The expensive map capability probe ran on a worker before the
            // owner-thread handoff. Validate modifiers before persisting any
            // preferences so a refused launch is genuinely side-effect-free.
            LaunchPlan plan;
            try
            {
                LaunchPlan candidate = OfflineLaunch.Preview(_settings, request.RoomKey,
                    request.Mode, request.Hunter, request.Suit, request.Bots, request.BotLevel, request.Character,
                    request.BotRoster, request.LocalTeam);
                if (!Multiplayer.MatchModifierRules.Validate(candidate.MatchRules, out _)) return false;
                plan = OfflineLaunch.Create(_settings, request.RoomKey,
                    request.Mode, request.Hunter, request.Suit, request.Bots, request.BotLevel, request.Character,
                    request.BotRoster, request.LocalTeam);
            }
            catch (ArgumentException ex) { Console.WriteLine("[launcher] " + ex.Message); return false; }
            Finish(plan);
            return _finished;
        }

        internal bool RmlUiLeaveLobby()
        {
            if (_lobby == null || !NetSession.Active) return false;
            // Use exactly the existing lifecycle so coordinator/screens/host
            // shut down once and the previous lobby route is retired.
            _lobby.Leave("");
            return true;
        }

        public void ResumeLobby() { _lobby?.Resume(); _session.Start(); }
        public void SuspendLobby() { _lobby?.Suspend(); }

        public StartScreen(MenuSettings settings, IReadOnlyList<string> rooms)
        {
            _settings = settings; _rooms = new List<string>(rooms); Focusable = true;
            _prime = new PrimeShell(CreateWorkspace, OpenVersionManager);
            _prime.IsVisible = false; _prime.IsEnabled = false;
            _layers.Children.Add(_prime);
#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
            _nativeStartupPending = MphRead.Mods.Launcher.RmlUi.Render.RmlUiOpenGlSmoke.NativeUiRequired;
#endif
            if (!_nativeStartupPending)
            {
                _startup = new PrimeStartupScreen();
                _startup.Continued += ContinueStartup;
                _layers.Children.Add(_startup);
            }
            Content = _layers;
            _prime.Router.CanNavigate = CanNavigate;
            _prime.Router.Changed += _ => { UpdateReplayBackground(); TryShowUpdatePrompt(); };
            _prime.BackRequested = () =>
            {
                if (_prime.Router.Current == PrimeRoute.Lobby)
                {
                    _prime.Router.Navigate(PrimeRoute.News);
                    return true;
                }
                if (_prime.Router.Current == PrimeRoute.News)
                {
                    AskToQuit();
                    return true;
                }
                return false;
            };
            _session.IsForeground = () => _prime.Router.Current == PrimeRoute.Lobby;
            AttachedToVisualTree += (_, _) => _session.Start();
            DetachedFromVisualTree += (_, _) => _session.Stop();
            _prime.Start();
#if ANDROID
            var navigation = new GamepadNavigation();
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input,
                (_, _) => { if (Mods.Input.GamepadContexts.MenuVisible) navigation.Update(this); });
            AttachedToVisualTree += (_, _) => timer.Start();
            DetachedFromVisualTree += (_, _) => timer.Stop();
#endif
            _updateWatcher = new PrimeUiPulse(TimeSpan.FromMinutes(5), CheckForUpdates);
            AttachedToVisualTree += (_, _) => { _updateWatcher.Start(); CheckForUpdates(); };
            DetachedFromVisualTree += (_, _) => _updateWatcher.Stop();
            RefreshVersionLine();
#if ANDROID
            // Android does not run the desktop Shell.AfterDraw first-frame gate.
            // Keep its existing background preview catch-up; desktop starts the
            // same work explicitly after its first presented shell frame.
            BeginDeferredPreviewCatchup();
#endif
        }
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_startup == null) ShowInitialPrompt();
        }
        private void ContinueStartup()
        {
            if (_startup == null) return;
            _prime.IsVisible = true;
            _startup.Reveal(() =>
            {
                var startup = _startup; _startup = null;
                if (startup != null) { _layers.Children.Remove(startup); startup.Dispose(); }
                _prime.IsEnabled = true;
                FocusNavigator.Ensure(_prime);
                ShowInitialPrompt();
            });
        }
        private void ShowInitialPrompt()
        {
            Deck.NextFrame(this, () =>
            {
                if (_nativeStartupPending || _startup != null || TopLevel.GetTopLevel(this) == null) return;
                if (!GameFiles.Ready && !_prime.Overlays.IsOpen) OpenSetup();
                else TryShowUpdatePrompt();
                if (RmlUiCanPresent) RmlUiReady?.Invoke();
            });
        }
        private bool CanNavigate(PrimeRoute route)
        {
            if (_startup != null || _nativeStartupPending) return false;
            if (_bypassGuard) return true;
            if (_prime.Overlays.IsOpen) return false;
            if (route == PrimeRoute.Lobby && _lobby == null) return false;
            if (route == PrimeRoute.Play && _lobby != null && NetSession.Active)
            { _prime.Router.Navigate(PrimeRoute.Lobby); return false; }
            if (_prime.Router.Current == PrimeRoute.Settings
                && _prime.Workspaces.Get(PrimeRoute.Settings) is SettingsView settings && settings.IsDirty)
            {
                ShowUnsaved(settings, () => { _bypassGuard = true; _prime.Router.Navigate(route); _bypassGuard = false; });
                return false;
            }
            return true;
        }
        private Control CreateWorkspace(PrimeRoute route)
        {
            switch (route)
            {
                case PrimeRoute.News:
                    return new NewsWorkspace(_prime.Overlays);
                case PrimeRoute.Play:
                    var play = new PlayWorkspace();
                    play.Closed += (_, _) => _prime.Back();
                    play.CreateLobbyRequested += (_, _) => { if (EnsureGameFiles()) OpenCreateServer(); };
                    play.Launched += (_, plan) => ConnectedOrFinished(plan);
                    play.Overlays = _prime.Overlays;
                    play.CanLaunch = CanLaunchLocal;
                    return play;
                case PrimeRoute.Community:
                    return new CommunityWorkspace(_prime.Overlays);
                case PrimeRoute.Settings:
                    var settings = new SettingsView(_settings, shell: true);
                    settings.Closed += (_, _) => _prime.Back();
                    settings.GameFilesRequested += (_, _) => OpenSetup();
                    return settings;
                case PrimeRoute.HunterLicense:
                    var license = new LicenseWorkspace();
                    license.Closed += (_, _) => _prime.Back();
                    return license;
                case PrimeRoute.Theatre:
                    var theatre = new TheatreWorkspace();
                    theatre.CanLaunch = CanLaunchLocal;
                    theatre.EditorChanged += () => UpdateReplayBackground();
                    theatre.Closed += (_, _) => _prime.Back();
                    theatre.Launched += (_, plan) => Finish(plan);
                    return theatre;
                case PrimeRoute.Offline:
                    var offline = new OfflineWorkspace(_settings, _rooms, _prime.Overlays);
                    offline.Launched += (_, plan) => LaunchLocal(plan);
                    return offline;
                case PrimeRoute.Forge:
#if MPHREAD_SHELL
                    return CreateForgeWorkspace(openLibrary: true);
#else
                    return new PrimePanel(PrimeChrome.Stack(PrimeChrome.Title("FORGE"),
                        PrimeChrome.Text("Map Studio requires the desktop renderer.")));
#endif
                case PrimeRoute.Lobby:
                    return _lobby ?? (Control)new PrimePanel(PrimeChrome.Text("No active lobby."));
                default: throw new ArgumentOutOfRangeException(nameof(route));
            }
        }
        internal void ShowReplayEditor(Action close, Action fullscreen)
        {
            _finished = false;
            if (_prime.Workspaces.Get(PrimeRoute.Theatre) is TheatreWorkspace theatre)
                theatre.ShowEditor(close, fullscreen);
            _prime.Router.Navigate(PrimeRoute.Theatre);
            UpdateReplayBackground();
        }

        internal void ShowTrainingLaunchFailure(string message)
        {
            _finished = false;
#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
            if (MphRead.Mods.Launcher.RmlUi.Render.RmlUiOpenGlSmoke.NativeUiRequired)
            {
                if (Shell.Window is { } window)
                    MphRead.Mods.Launcher.RmlUi.Render.RmlUiOpenGlSmoke.HandleNativeTrainingFailure(window, message);
                return;
            }
#endif
            OpenTraining(true);
            _prime.Overlays.Show(new PrimePanel(PrimeChrome.Stack(
                PrimeChrome.Title("TRAINING COULD NOT START"), PrimeChrome.Text(message),
                new PrimeButton("CLOSE", Pop))), PrimeModalSize.Small);
        }

        internal void ShowReplayLaunchFailure(string message)
        {
            _finished = false;
            _prime.Router.Navigate(PrimeRoute.Theatre);
            if (_prime.Workspaces.Get(PrimeRoute.Theatre) is TheatreWorkspace theatre)
            {
                theatre.CloseEditor();
                theatre.ShowLaunchFailure(message);
            }
            UpdateReplayBackground();
        }
        private void UpdateReplayBackground()
        {
            bool editor = _prime.Router.Current == PrimeRoute.Theatre
                && _prime.Workspaces.TryGet(PrimeRoute.Theatre) is TheatreWorkspace { EditorActive: true };
            _prime.Background = editor ? Brushes.Transparent : PrimeTheme.BackgroundBrush;
        }

        private bool EnsureGameFiles()
        { if (GameFiles.Ready) return true; OpenSetup(); return false; }
        private bool CanLaunchLocal()
        {
            if (GameFiles.SetupInProgress) return false;
            if (DemoPlayback.IsActive)
            {
                _prime.Overlays.Show(new PrimePanel(PrimeChrome.Stack(PrimeChrome.Title("REPLAY SESSION ACTIVE"),
                    PrimeChrome.Text("Return to Theatre and close the current replay before starting another session."),
                    new PrimeButton("CLOSE", Pop))), PrimeModalSize.Small);
                return false;
            }
            if (NetSession.Active && NetSession.PersistentLobby)
            {
                _prime.Overlays.Show(new PrimePanel(PrimeChrome.Stack(PrimeChrome.Title("LOBBY ACTIVE"),
                    PrimeChrome.Text("Leave your multiplayer lobby before starting local gameplay or replay playback."),
                    new PrimeButton("RETURN", Pop))), PrimeModalSize.Small);
                return false;
            }
            return EnsureGameFiles();
        }
        private void LaunchLocal(LaunchPlan plan) { if (CanLaunchLocal()) Finish(plan); }
        public void Reset(MenuSettings settings)
        {
            if (_prime.Workspaces.TryGet(PrimeRoute.Theatre) is TheatreWorkspace theatre) theatre.CloseEditor();
            _finished = false; Plan = default; ShowGround(true); _prime.Overlays.Clear();

            // Shell reloads settings.json after a match so pause-menu changes and
            // match-rule persistence are authoritative. Keep this long-lived front
            // screen on that same object too; otherwise cached workspaces continue
            // editing the pre-match instance while Shell starts with the fresh one.
            _settings = settings;
            bool rebuildCurrent = _prime.Router.Current is PrimeRoute.Settings or PrimeRoute.Offline;
            _prime.Workspaces.Remove(PrimeRoute.Settings);
            _prime.Workspaces.Remove(PrimeRoute.Offline);
            if (rebuildCurrent) _prime.Workspaces.Show(_prime.Router.Current);
            if (_lobby != null && NetSession.Active) { ResumeLobby(); return; }
            if (_lobby != null) { _session.Screen = null; _lobby = null; _prime.Workspaces.Remove(PrimeRoute.Lobby); _prime.Router.Forget(PrimeRoute.Lobby); }
            if (_returnToMapStudio) { _returnToMapStudio = false; RefreshRooms(); }
            Hunters.Reroll(); LauncherPrefs.Load(); RefreshRooms(); _prime.Refresh(); RefreshVersionLine();
            if (_prime.Router.Current == PrimeRoute.Lobby) _prime.Router.Navigate(PrimeRoute.Play);
            if (!GameFiles.Ready) OpenSetup();
        }
        /// <summary>Return straight to the ship console after a real, saved
        /// story boarding. The modal reads the same save slot the scene left.</summary>
        public void OpenCampaignShip()
        {
            if (MatchStart.RecentShipDock == null) return;
            _prime.Router.Navigate(PrimeRoute.Offline);
            if (_prime.Workspaces.Get(PrimeRoute.Offline) is OfflineWorkspace offline)
                offline.OpenCampaignShip();
        }

        /// <summary>Reveal final mission results only after a real, finished
        /// Gorea cinematic and its existing save handoff have completed.</summary>
        public void OpenCampaignEnding()
        {
            if (MatchStart.RecentCampaignEnding is not { } summary) return;
            _prime.Router.Navigate(PrimeRoute.Offline);
            var view = new CampaignEndingView(summary);
            void Done()
            {
                _prime.Overlays.Close();
                MatchStart.ClearRecentCampaignEnding();
            }
            view.Finished += (_, _) => Done();
            _prime.Overlays.Show(view, PrimeModalSize.Medium, cancel: Done);
        }

        public void OpenTraining(bool focusTrainer = true)
        {
            _prime.Router.Navigate(PrimeRoute.Offline);
            if (focusTrainer) Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var workspace = _prime.Workspaces.Get(PrimeRoute.Offline);
                var start = ControllerNav.Find(workspace, "offline.training.start");
                start?.BringIntoView(); start?.Focus();
            });
        }
        public bool GoBack() { if (_startup == null) _prime.Back(); return true; }
        public void Dispose() { _startup?.Dispose(); _startup = null; Content = null; _session.Dispose(); _updateWatcher.Dispose(); _prime.Overlays.Clear(); _prime.Dispose(); }
        private void ShowGround(bool show)
        {
            _prime.Header.IsVisible = show; _prime.Footer.IsVisible = show;
            _prime.Workspaces.IsVisible = show;
            _prime.Background = show ? PrimeTheme.BackgroundBrush : Brushes.Transparent;
        }
        private void Push(Control view) => _prime.Overlays.Show(view);
        private void Pop()
        {
            _prime.Overlays.Close();
            Dispatcher.UIThread.Post(TryShowUpdatePrompt, DispatcherPriority.Background);
            if (RmlUiCanPresent) RmlUiReady?.Invoke();
        }
        private void Finish(LaunchPlan plan)
        {
            if (_finished) return;
            _finished = true; Plan = plan; Done?.Invoke(this, plan);
        }
        internal void OpenMapStudio()
        {
            // An explicit editor launch bypasses the ordinary startup gate.
            if (_startup != null) { _layers.Children.Remove(_startup); _startup.Dispose(); _startup = null; }
            _prime.IsVisible = true; _prime.IsEnabled = true;
            _prime.Router.Navigate(PrimeRoute.Forge);
        }
#if MPHREAD_SHELL
        private MapStudioScreen CreateForgeWorkspace(bool openLibrary)
        {
            var forge = new MapStudioScreen(_prime.Overlays, openLibrary: openLibrary);
            forge.Closed += (_, _) => _prime.Back();
            forge.HostRequested += (_, definition) => { RefreshRooms(); OpenCreateServer(definition.Name); };
            if (openLibrary && Shell.StudioProjectPath is { } projectPath) { Shell.StudioProjectPath=null; forge.Open(projectPath); }
            forge.PlayRequested += (_, definition) =>
            {
                if (!CanLaunchLocal()) return;
                _returnToMapStudio = true; Shell.PrepareStudioPreview(definition);
                Finish(new LaunchPlan { Kind = LaunchKind.Offline, RoomKey = definition.Name, IsPlaytest = true,
                    Hunter = Hunter.Samus, Mode = GameMode.Battle, Bots = 0, BotLevel = 5, PlayerName = "Map author" });
            };
            return forge;
        }
        private MapStudioScreen NativeForgeOwner()
        {
            if (_prime.Workspaces.TryGet(PrimeRoute.Forge) is MapStudioScreen current) return current;
            var owner = CreateForgeWorkspace(openLibrary: false); _prime.Workspaces.Set(PrimeRoute.Forge, owner); return owner;
        }
        internal NativeStudioResult OpenNativeStudioProject(string path, bool recovery)
        {
            return NativeForgeOwner().BeginNativeChange(new(recovery ? NativeStudioChangeKind.Recovery : NativeStudioChangeKind.Open, Path: path));
        }
        internal NativeStudioResult CreateNativeStudioProject(string name, string templateId)
        {
            return NativeForgeOwner().BeginNativeChange(new(NativeStudioChangeKind.Create, TemplateId: templateId, Name: name));
        }
        internal NativeStudioResult DuplicateNativeStudioProject(string path)
        {
            return NativeForgeOwner().BeginNativeChange(new(NativeStudioChangeKind.Duplicate, Path: path));
        }
        internal NativeStudioChangeSnapshot? NativeStudioChange() => (_prime.Workspaces.TryGet(PrimeRoute.Forge) as MapStudioScreen)?.CaptureNativeChange();
        internal NativeStudioResult ResolveNativeStudioChange(Guid ticket, NativeStudioChangeDecision decision, string path)
            => NativeForgeOwner().ResolveNativeChange(ticket, decision, path);
        internal void CancelNativeStudioChange(Guid ticket) => (_prime.Workspaces.TryGet(PrimeRoute.Forge) as MapStudioScreen)?.CancelNativeChange(ticket);
        internal bool TryTakeNativeStudioCommittedEditor(Guid ticket, out INativeStudioEditorSession? session)
        {
            if (_prime.Workspaces.TryGet(PrimeRoute.Forge) is MapStudioScreen owner) return owner.TryTakeNativeCommittedEditor(ticket, out session);
            session = null; return false;
        }
        internal bool NativeStudioOwnsPath(string path)
            => (_prime.Workspaces.TryGet(PrimeRoute.Forge) as MapStudioScreen)?.NativeOwnsProjectPath(path) == true;
        internal bool TryAcquireNativeStudioEditor(out MapStudioScreen.NativeStudioEditorSession? session, out string error)
        {
            if (_prime.Workspaces.TryGet(PrimeRoute.Forge) is MapStudioScreen studio)
                return studio.TryAcquireNativeEditor(out session, out error);
            session = null; error = "OPEN OR CREATE A PROJECT BEFORE NATIVE EDITING"; return false;
        }
        internal bool TryCaptureCommunityPublishIntent(out MapGen.CommunityStudioPublishIntent? intent, out string error)
        {
            if (_prime.Workspaces.TryGet(PrimeRoute.Forge) is MapStudioScreen studio)
                return studio.TryCaptureCommunityPublishIntent(out intent, out error);
            intent = null; error = "NO RETAINED STUDIO PROJECT OWNER"; return false;
        }
#endif
        private void OpenCreateServer(string? firstMap = null)
        {
            if (NetSession.Active) { _prime.Router.Navigate(PrimeRoute.Lobby); return; }
            if (!CanLaunchLocal()) return;
            RefreshRooms();
            var view = new CreateServerScreen(_rooms, firstMap ??
                (LauncherPrefs.LastSelectedMap.Length > 0 ? LauncherPrefs.LastSelectedMap : _settings.RoomKey));
            if (firstMap != null && !Metadata.IsBuiltInRoom(firstMap)) view.ShowDedicated();
            view.Closed += (_, _) => Pop();
            view.Launched += (_, plan) => { Pop(); ConnectedOrFinished(plan); };
            _prime.Overlays.Show(view, cancel: view.RequestBack);
        }
        private void ConnectedOrFinished(LaunchPlan plan)
        {
            if (NetSession.Active && NetSession.PersistentLobby)
            {
                SpectatorMode.SetSessionPreference(plan.Spectate);
                _spectateNextMatch = plan.Spectate;
                _lobby = new LobbyScreen(_rooms, plan.Lobby) { Overlays = _prime.Overlays };
                _lobby.HubRequested += (_, _) => _prime.Router.Navigate(PrimeRoute.News);
                _lobby.MatchRequested += (_, match) =>
                {
                    // The server's start barrier is not optional navigation. Preserve
                    // any configuration draft, close sheets and show the countdown.
                    _prime.Overlays.Clear(); _bypassGuard = true;
                    _prime.Router.Navigate(PrimeRoute.Lobby); _bypassGuard = false;
                    _spectateNextMatch = SpectatorMode.PreferSpectator;
                    MatchRequested?.Invoke(this, match with { Spectate = _spectateNextMatch });
                };
                _lobby.Closed += (_, reason) => LobbyClosed(reason);
                _prime.Workspaces.Set(PrimeRoute.Lobby, _lobby);
                _prime.Router.Navigate(PrimeRoute.Lobby); _prime.Refresh();
                // Assign the coordinator only after every handoff event is wired.
                // Screen assignment performs an immediate hydration tick, and an
                // already-running match can request its scene during that tick.
                _session.Screen = _lobby;
            }
            else Finish(plan);
        }
        private void LobbyClosed(string reason)
        {
            _session.Screen = null; _lobby = null;
            _prime.Overlays.Clear();
            if (_prime.Router.Current == PrimeRoute.Lobby)
            {
                PrimeRoute destination = PrimeRoute.Play;
#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
                // The native Play/Home page retains the News shell route.
                // Play is the legacy workspace and releases native input/rendering.
                if (MphRead.Mods.Launcher.RmlUi.Render.RmlUiOpenGlSmoke.NativeUiRequired)
                    destination = PrimeRoute.News;
#endif
                _prime.Router.Navigate(destination);
            }
            _prime.Router.Forget(PrimeRoute.Lobby); _prime.Workspaces.Remove(PrimeRoute.Lobby);
            if (_prime.Workspaces.Get(PrimeRoute.Play) is PlayWorkspace play) play.SessionEnded(reason);
            _prime.Refresh();
        }
        private void OpenSetup()
        {
            if (RmlUiGameFilesOwnerCurrent && RmlUiTryOpenGameFiles?.Invoke() == true) return;
#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
            if (MphRead.Mods.Launcher.RmlUi.Render.RmlUiOpenGlSmoke.NativeUiRequired) return;
#endif
            var view = new SetupScreen();
            view.Closed += (_, _) =>
            {
                if (!_prime.Overlays.IsTop(view)) return;
                _prime.Overlays.Close(view);
                RefreshRooms();
                _prime.Refresh();
                // Setup is already complete at this point. Fill missing preview
                // art in the background without holding the setup sheet open.
                if (GameFiles.Ready) BeginDeferredPreviewCatchup();
            };
            Push(view);
        }
        private void ShowUnsaved(SettingsView settings, Action continuation)
        {
            _prime.Overlays.Show(new PrimePanel(PrimeChrome.Stack(PrimeChrome.Title("UNSAVED CONFIGURATION"),
                PrimeChrome.Text("Apply your configuration, discard these edits, or keep editing."),
                PrimeChrome.Columns("*,*,*", new PrimeButton("APPLY", () => { if (settings.ApplyDraft()) { Pop(); continuation(); } }, true),
                    new PrimeButton("DISCARD", () => { settings.DiscardDraft(); Pop(); continuation(); }),
                    new PrimeButton("CANCEL", Pop)))), PrimeModalSize.Medium);
        }
        internal bool PreserveForgeRecovery() => (_prime.Workspaces.TryGet(PrimeRoute.Forge) as MapStudioScreen)?.SaveRecovery() ?? true;

        private void AskToQuit()
        {
            if (_prime.Workspaces.TryGet(PrimeRoute.Settings) is SettingsView { IsDirty: true } settings)
            { ShowUnsaved(settings, AskToQuit); return; }
#if MPHREAD_SHELL
            var forge = _prime.Workspaces.TryGet(PrimeRoute.Forge) as MapStudioScreen;
            string prompt = forge?.IsDirty == true
                ? "Quit Project Prime? Unsaved Forge edits will be kept as a recovery copy."
                : "Quit Project Prime?";
#else
            const string prompt = "Quit Project Prime?";
#endif
            var view = new ConfirmScreen(prompt, yes: "exit game", no: "cancel");
            view.Answered += (_, yes) =>
            {
                Pop(); if (!yes) return;
#if MPHREAD_SHELL
                if (forge != null && !forge.SaveRecovery()) { _prime.Router.Navigate(PrimeRoute.Forge); return; }
#endif
                Finish(default);
            };
            Push(view);
        }
        public void ShowPauseMenu(Action onResume, Action onLeave, Action onQuit,
            Action? onSpectate = null, Action? onRejoin = null, ScenePlayerRegistry? players = null,
            bool openSettings = false)
        {
            // Everything on this stack is read over the match: the menu, the
            // settings it opens and the map vote all ask for the scrim alone.
            ShowGround(false);
            var view = new PauseMenuView(offerWindowMode: false,
                campaignAvailable: CampaignJournalView.Available(players));
            view.Resumed += (_, _) => { Pop(); onResume(); };
            view.LeaveRequested += (_, _) => { Pop(); onLeave(); };
            view.QuitRequested += (_, _) => { Pop(); onQuit(); };
            view.SpectateRequested += (_, _) =>
            {
                Pop();
                if (onSpectate != null) onSpectate();
                else SpectatorMode.Start();
                onResume();
            };
            view.RejoinRequested += (_, _) =>
            {
                Pop();
                if (onRejoin != null) onRejoin();
                else SpectatorMode.Rejoin();
                onResume();
            };
            view.RecordToggleRequested += (_, _) =>
            {
                DemoRecorder.ToggleWithFeedback();
                Pop();
                onResume();
            };
            view.ReturnToLobbyRequested += (_, _) =>
            {
                var confirm = new ConfirmScreen(
                    "Return all connected players to the lobby?",
                    yes: "return all", no: "cancel");
                confirm.Answered += (_, yes) =>
                {
                    Pop(); // confirmation
                    if (!yes) return;
                    if (!NetSession.SendLobbyCommand(LobbyCommandType.ReturnToLobby))
                    {
                        Chat.ChatBox.System("could not request a return to the lobby");
                        return;
                    }
                    Pop(); // pause menu
                    onResume();
                };
                Push(confirm);
            };
            view.VoteMapRequested += (_, _) => OpenVote();
            view.CampaignJournalRequested += (_, _) =>
            {
                CampaignJournalView? journal = CampaignJournalView.OpenFor(players);
                if (journal == null) return;
                journal.Closed += (_, _) => Pop();
                Push(journal);
            };
            view.ReplayControlsRequested += (_, _) =>
            {
                var controls = new ReplayControlsView();
                controls.Closed += (_, _) => Pop();
                controls.ResumeRequested += (_, _) =>
                {
                    Pop();
                    Pop();
                    onResume();
                };
                Push(controls);
            };
            void OpenSettings()
            {
                var settings = new SettingsView(_settings, inGame: true, players: players);
                settings.Closed += (_, _) => Pop();
                Push(settings);
            }
            view.SettingsRequested += (_, _) => OpenSettings();
            _prime.Overlays.Show(view, cancel: () => { Pop(); onResume(); });
            view.FocusResume();
            if (openSettings) OpenSettings();
        }

        /// <summary>
        /// Pick a map and put it to the room -- the same screen a match is
        /// chosen from, with the strip of sources taken away.
        ///
        /// The list is read again here rather than taken from the one this
        /// screen was built with. On the head that shows the pause menu on
        /// this stack the front screen is built once, before the game files
        /// have necessarily been found, and it is still that same object
        /// during every match afterwards -- so a launch that started with no
        /// rooms opened a ballot with nothing on it, which is what "there is
        /// no map vote on Android" was. <see cref="InGameMenu.OpenVote"/> does
        /// the same and this is the other half of it.
        /// </summary>
        private void OpenVote()
        {
            string why = MapVote.WhyNotProposing();
            if (why.Length > 0)
            {
                // Said in the game's own chat rather than in a box here: it is
                // one sentence, the player is about to go back to the match,
                // and a dialog for it is a second thing to dismiss.
                Chat.ChatBox.System(why);
                Pop();
                return;
            }
            IReadOnlyList<string> rooms = _rooms;
            if (rooms.Count == 0)
            {
                try
                {
                    rooms = ThumbnailGenerator.MultiplayerRooms();
                }
                catch (Exception ex)
                {
                    Mods.DebugLog.Exception("pause", ex);
                    rooms = Array.Empty<string>();
                }
            }
            if (rooms.Count == 0)
            {
                Chat.ChatBox.System("no maps to vote for");
                Pop();
                return;
            }
            var view = new PlayScreen(_settings, rooms, PlayScreen.Face.Vote,
                overGame: true);
            view.Closed += (_, _) => Pop();
            view.Voted += (_, room) =>
            {
                MapVote.Propose(room);
                // Both this and the pause menu under it: the answer arrives as
                // a prompt over the match, which is not a thing to read through
                // a menu.
                Pop();
                Pop();
            };
            Push(view);
        }

        // -------------------------------------------------------------- version

        /// <summary>
        /// Render the pictures of any map that does not have one yet, without
        /// being asked. Nothing happens in the ordinary case, which is every
        /// map already having one.
        /// </summary>
        internal void BeginDeferredPreviewCatchup(CancellationToken cancel = default)
            => _ = CatchUpPreviews(cancel);

        private async Task CatchUpPreviews(CancellationToken cancel = default)
        {
            try
            {
                cancel.ThrowIfCancellationRequested();
                // The existing ThumbnailHost owns the configured-files read
                // capability and missing-count check for the full async job.
                if (!ThumbnailHost.CanRender)
                {
                    return;
                }
                await ThumbnailHost.RenderMissingAsync(_ => { }, cancel);
                Dispatcher.UIThread.Post(() =>
                {
                    MapShot.Forget();
                    BakedBackdrop.Forget();
                    LauncherBackdrop.Refresh();
                });
            }
            catch (OperationCanceledException)
            {
                // Normal during application shutdown.
            }
            catch (Exception ex)
            {
                // A preview is decoration. A graphics/worker failure here must
                // never turn successful game-file setup into a launcher failure.
                Mods.DebugLog.Exception("thumbnails", ex);
            }
        }

        internal void RefreshDeferredRooms(IReadOnlyList<string> rooms)
        {
            _rooms.Clear();
            _rooms.AddRange(rooms);
            bool rebuild = _prime.Router.Current == PrimeRoute.Offline;
            _prime.Workspaces.Remove(PrimeRoute.Offline);
            if (rebuild) _prime.Workspaces.Show(PrimeRoute.Offline);
            _prime.Refresh();
        }

        private void RefreshRooms()
        {
            if (!GameFiles.Ready)
            {
                return;
            }
            _rooms.Clear();
            foreach (string room in ThumbnailGenerator.MultiplayerRooms())
            {
                _rooms.Add(room);
            }
        }

        /// <summary>
        /// "1.2.3", or what to say instead when this build is not a release.
        /// Not <c>BuildVersion.Display</c>, which puts a v in front: this is a
        /// corner of a picture rather than a sentence.
        /// </summary>
        private static string VersionNumber()
        {
            Version? current = BuildVersion.Current;
            return current == null ? "a local build" : current.ToString(3);
        }

        private void Say(string text, Color colour, bool pressable = false)
        {
            _nativeVersionStatus = text;
            _prime.Footer.SetStatus(text);
        }

        /// <summary>
        /// Three states, not two. Amber is "there is a newer build, press
        /// this"; green is "this is the published one"; dim is everything else
        /// -- a local build, or a check that has not answered. Painting "no
        /// answer" green would be the one wrong thing this can do: a server
        /// refuses a client on a different build at Hello, so being told you
        /// are current when nobody has checked is worse than being told
        /// nothing.
        /// </summary>
        private void RefreshVersionLine()
        {
            if (_updating)
            {
                return;
            }
            string number = VersionNumber();
            if (Updater.Available is UpdateInfo update)
            {
                Say($"UPDATE AVAILABLE  //  {number} > {update.Version.ToString(3)}  //  OPEN VERSIONS",
                    HubTheme.Warm, pressable: true);
                return;
            }
            Say($"SIM: 60 HZ  //  BUILD  //  {number}  //  VERSIONS", BuildVersion.IsRelease && Updater.Checked
                ? GuiTheme.Good : GuiTheme.TextDim);
        }

        private void CheckForUpdates()
        {
            if (!LauncherPrefs.AutoUpdate || Updater.Disabled || _updating)
            {
                return;
            }
            Updater.CheckInBackground(
                update => Dispatcher.UIThread.Post(() =>
                {
                    RefreshVersionLine();
                    QueueUpdatePrompt(update);
                }),
                () => Dispatcher.UIThread.Post(RefreshVersionLine));
        }

        private void QueueUpdatePrompt(UpdateInfo update)
        {
            if (_lastPromptedUpdateTag == update.Tag)
            {
                return;
            }
            _lastPromptedUpdateTag = update.Tag;
            _pendingUpdatePrompt = update;
            TryShowUpdatePrompt();
        }

        /// <summary>
        /// Do not throw a modal over a lobby/settings screen. Remember it and
        /// show it the next time the player reaches the hub instead.
        /// </summary>
        private void TryShowUpdatePrompt() => TryShowUpdatePrompt(nativeOwnerTick: false);
        private void TryShowUpdatePrompt(bool nativeOwnerTick)
        {
            if (_pendingUpdatePrompt is not UpdateInfo update
                || _startup != null || _nativeStartupPending || _prime.Overlays.IsOpen || _prime.Router.Current != PrimeRoute.News
                || !_prime.Header.IsVisible || TopLevel.GetTopLevel(this) == null
                || NetSession.Active || _updating || _loadingVersions
                || !GameFiles.Ready)
            {
                return;
            }
#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
            if (MphRead.Mods.Launcher.RmlUi.Render.RmlUiOpenGlSmoke.NativeUiRequired)
            {
                // Async metadata callbacks only queue the offer. Native GL
                // presentation runs on the existing window owner's frame tick.
                if (nativeOwnerTick && RmlUiTryOpenUpdatePrompt?.Invoke(update, VersionNumber(), accepted =>
                    { if (accepted && RmlUiUpdateOwnerCurrent) InstallVersion(update); }) == true)
                    _pendingUpdatePrompt = null;
                return;
            }
#endif
            _pendingUpdatePrompt = null;
            var prompt = new ConfirmScreen(
                $"Project Prime {update.Tag} is available.\n\n"
                + $"Installed: v{VersionNumber()}\nLatest: {update.Tag}\n\n"
                + "Install it now?",
                yes: "update now", no: "later");
            prompt.Answered += (_, yes) =>
            {
                Pop();
                if (yes)
                {
                    InstallVersion(update);
                }
            };
            Push(prompt);
        }

        private string _nativeVersionStatus = "";
        internal string RmlUiVersionStatus => _nativeVersionStatus;
        internal bool RmlUiVersionBusy => _updating;
        internal void RmlUiInstallVersion(UpdateInfo update)
        {
            if (RmlUiUpdateOwnerCurrent && !_updating) InstallVersion(update);
        }

        private void OpenVersionManager()
        {
            if (_loadingVersions || _updating)
            {
                return;
            }
            if (Updater.Disabled)
            {
                Say($"BUILD  //  {VersionNumber()}  //  VERSION CHECKS DISABLED FOR THIS RUN",
                    GuiTheme.TextDim);
                return;
            }
            _ = LoadVersionManager();
        }

        private async Task LoadVersionManager()
        {
            _loadingVersions = true;
            string number = VersionNumber();
            Say($"BUILD  //  {number}  //  LOADING RELEASES...", GuiTheme.TextDim);
            IReadOnlyList<UpdateInfo> releases =
                await Task.Run(() => UpdateCheck.Releases(30));
            string reason = UpdateCheck.LastReason ?? "no releases were found";
            _loadingVersions = false;
            if (TopLevel.GetTopLevel(this) == null) return;
            if (releases.Count == 0)
            {
                Say($"BUILD  //  {number}  //  {reason}", HubTheme.Warm);
                return;
            }

            var view = new VersionManagerView(releases, BuildVersion.Current);
            view.Closed += (_, _) => Pop();
            view.VersionSelected += AskToSwitchVersion;
            Push(view);
        }

        private void AskToSwitchVersion(UpdateInfo target)
        {
            Version? raw = BuildVersion.Current;
            Version? current = raw == null ? null : BuildVersion.Normalise(raw);
            if (current != null && target.Version == current)
            {
                return;
            }
            bool downgrade = current != null && target.Version < current;
            string from = current == null ? "a local build" : $"v{current.ToString(3)}";
            string verb = downgrade ? "Downgrade" : "Switch";
            var prompt = new ConfirmScreen(
                $"{verb} Project Prime from {from} to {target.Tag}?\n\n"
                + "The application will restart when an in-place switch is supported. "
                + "Any active lobby will be left.",
                yes: downgrade ? "downgrade" : "switch", no: "cancel");
            prompt.Answered += (_, yes) =>
            {
                Pop(); // confirmation
                if (!yes)
                {
                    return;
                }
                Pop(); // version manager
                InstallVersion(target);
            };
            Push(prompt);
        }

        /// <summary>
        /// Explicitly install one published release. Automatic checks never call
        /// this with an older build; only Version Manager can request a
        /// downgrade.
        /// </summary>
        private void InstallVersion(UpdateInfo update)
        {
            Version? raw = BuildVersion.Current;
            Version? current = raw == null ? null : BuildVersion.Normalise(raw);
            bool downgrade = current != null && update.Version < current;

            // Android's package manager rejects a lower versionCode as an
            // in-place install. Do not download 60 MB only to hand the player a
            // guaranteed failure dialog.
            if (OperatingSystem.IsAndroid() && downgrade)
            {
                string currentText = current == null ? "" : $" over v{current.ToString(3)}";
                Say($"ANDROID DOWNGRADE  //  {update.Tag}{currentText} REQUIRES UNINSTALL OR ADB",
                    HubTheme.Warm);
                if (!Updater.OpenPage(update))
                {
                    Say(update.PageUrl, GuiTheme.Warm);
                }
                return;
            }

            if (UpdateInstall.CanInstall(update))
            {
                _ = FetchAndInstall(update, UpdateInstall.Current!);
                return;
            }

            // macOS keeps its signed bundle intact, and a read-only desktop
            // install or unverifiable old package also belongs on the release
            // page rather than being half-applied.
            if (Updater.OpenPage(update))
            {
                string action = OperatingSystem.IsMacOS()
                    ? "REPLACE THE SIGNED APP BUNDLE FROM THE RELEASE PAGE"
                    : "OPENED RELEASE PAGE FOR MANUAL SWITCH";
                Say($"{update.Tag}  //  {action}", HubTheme.Warm);
            }
            else
            {
                Say(update.PageUrl, GuiTheme.Warm);
            }
        }

        private async Task FetchAndInstall(UpdateInfo update, IUpdateInstaller installer)
        {
            if (_updating)
            {
                return;
            }
            string number = VersionNumber();
            if (!installer.Allowed)
            {
                // This stage has to leave the line pressable: on a phone,
                // allowing this app as an install source is a Settings screen,
                // nothing here can wait for it, and the player comes back and
                // presses again.
                Say($"{number} -- allow installs from this app, then press again",
                    GuiTheme.Warm, pressable: true);
                installer.RequestPermission();
                return;
            }
            _updating = true;
            installer.Finished = (ok, message) => Dispatcher.UIThread.Post(() =>
            {
                if (_finished) return;
                _updating = false;
                Say(ok ? number : $"{number} -- {message}",
                    ok ? GuiTheme.TextDim : GuiTheme.Warm, pressable: !ok);
            });
            string label = update.AssetName.Length > 0 ? update.AssetName : update.Tag;
            Say($"{number} -- downloading {label}...", GuiTheme.Warm);
            var reported = new object();
            int shown = -1;
            void Progress(float fraction)
            {
                // Whole percents only, and only when one changes: this is
                // called for every 64 KB and each post crosses to the UI thread.
                int percent = fraction < 0 ? -1 : (int)(fraction * 100);
                lock (reported)
                {
                    if (percent == shown)
                    {
                        return;
                    }
                    shown = percent;
                }
                Dispatcher.UIThread.Post(() =>
                {
                    if (_finished) return;
                    Say(percent < 0 ? $"{number} -- downloading {label}..."
                        : $"{number} -- downloading {label}... {percent}%", GuiTheme.Warm);
                });
            }
            string error = "";
            bool ready = await Task.Run(() => installer.Prepare(update, Progress, out error));
            // Preparation may finish after this launcher owner has gone away.
            // Never start an installation or restart from that stale continuation.
            if (_finished) { _updating = false; return; }
            if (!ready)
            {
                _updating = false;
                Say($"{number} -- {(error.Length > 0 ? error : "the download failed")}",
                    GuiTheme.Warm, pressable: true);
                return;
            }
            Say(installer.ExitAfterInstall
                ? $"{number} -- restarting to finish..."
                : $"{number} -- waiting for the system installer...", GuiTheme.Warm);
            if (!installer.Install(out error))
            {
                _updating = false;
                Say($"{number} -- {(error.Length > 0 ? error : "the install could not be started")}",
                    GuiTheme.Warm, pressable: true);
                return;
            }
            if (installer.ExitAfterInstall)
            {
                // The copying process is already running and waiting for this
                // one to be gone before it touches a single file. Staying open
                // would leave it waiting until its own deadline.
                Finish(default);
            }
        }
    }
}
