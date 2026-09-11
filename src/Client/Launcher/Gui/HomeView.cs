using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Accounts;
using MphRead.Mods.Network;
using MphRead.Mods.Update;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// The front screen: a picture, the things you can do, and nothing to read
    /// before you can play.
    ///
    /// **This is the whole front screen on every platform.** It is a
    /// <see cref="UserControl"/> rather than a <see cref="Window"/> for one
    /// reason: Android has no windows. The desktop heads put it in
    /// <c>HomeWindow</c>, which is a frame and nothing else; the Android
    /// head hands the same object to Avalonia as its single view. There is no
    /// second screen to keep in step, which is the point -- a phone-shaped copy
    /// of this file was the previous arrangement and it drifted within a
    /// release.
    ///
    /// What differs between the two is layout and not content: below
    /// <see cref="_narrowWidth"/> the picture becomes a band across the top and
    /// the panel takes the full width, because a 400-pixel column beside a
    /// photograph does not fit on a phone. And the two windows this screen
    /// opens -- settings and the map grid -- are shown as an overlay where
    /// there is no second window to open, which is what
    /// <see cref="ShowOverlay"/> is.
    /// </summary>
    internal sealed class HomeView : UserControl, IAsyncDisposable, IDisposable
    {
        private readonly MenuSettings _settings;
        private readonly List<string> _playable = new();
        private readonly SplashView _splash = new();
        private readonly Panel _cards = new();
        private readonly Grid _layout = new();
        private readonly Panel _overlay;
        private readonly Border _panel;
        // The classic surface is only a compatibility presentation. Keep the
        // same canonical controller/runtime pair alive behind it so the old
        // overlay cannot grow its own account, directory, or lobby owner.
        private readonly PrimeShellState _classicShell = new();
        private readonly ClientOnlineRuntime _classicOnline = new(enabled: false);
        private readonly PlayController _classicPlay;

        private readonly ProgressRow _setupProgress = new();
        private MenuEntry _setupBack = null!;
        private MenuEntry? _previewEntry;
        private MenuEntry? _previewProgress;
        private Control _homeCard = null!;
        private Control _setupCard = null!;
        private Control? _current;
        private bool _finished;
        private bool _observingUpdates;
        private int _disposed;

        /// <summary>Below this width the screen folds into one column.</summary>
        private const double _narrowWidth = 720;

        /// <summary>What the screen decided. Kind None means it was closed.</summary>
        public LaunchPlan Plan { get; private set; }

        /// <summary>
        /// Raised once, when the screen is done with: with a plan to start, or
        /// with <see cref="LaunchKind.None"/> when the answer was "quit".
        /// </summary>
        public event EventHandler<LaunchPlan>? Done;

        public HomeView(MenuSettings settings, IReadOnlyList<string> rooms)
        {
            _settings = settings;
            foreach (string room in rooms)
            {
                _playable.Add(room);
            }
            _classicPlay = new PlayController(_classicShell, _playable,
                onlineRuntime: _classicOnline);

            Background = GuiTheme.PanelBrush;

            // The cards scroll: on a phone a card of a dozen rows is taller
            // than the screen, and on the desktop this costs nothing because
            // nothing overflows.
            var scroll = new ScrollViewer
            {
                Content = _cards,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            // The credits are a section of the settings now. A wall of names
            // under every card was the first thing the eye landed on and the
            // last thing anybody needed while choosing a match.
            var stack = new Grid { RowDefinitions = new RowDefinitions("*") };
            Grid.SetRow(scroll, 0);
            stack.Children.Add(scroll);
            _panel = new Border
            {
                Background = GuiTheme.PanelBrush,
                Padding = new Thickness(22, 20, 22, 16),
                Child = stack
            };
            _updateBadge.Click += (_, _) => UpdateNow();
            _updateBadge.HorizontalAlignment = HorizontalAlignment.Left;
            _updateBadge.VerticalAlignment = VerticalAlignment.Bottom;
            _updateBadge.Margin = new Thickness(24, 0, 24, 22);
            _layout.Children.Add(_splash);
            // After the splash and in the same cell, so they draw on top of
            // it: the badge in one bottom corner, the version in the other.
            _layout.Children.Add(_updateBadge);
            _layout.Children.Add(BuildVersionLine());
            _layout.Children.Add(BuildDebugSwitch());
            _layout.Children.Add(_panel);
            ApplyLayout(narrow: false);

            _overlay = new Panel
            {
                Background = GuiTheme.InkBrush,
                IsVisible = false
            };
            var root = new Panel();
            root.Children.Add(_layout);
            root.Children.Add(_overlay);
            Content = root;
            SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width < _narrowWidth);

            _homeCard = BuildHomeCard();
            _setupCard = BuildSetupCard();

            _setupBack.IsVisible = GameFiles.Ready;
            ShowCard(GameFiles.Ready ? _homeCard : _setupCard);
            RefreshSplash();
            RefreshPreviewEntry();
            if (NodeSessions.Current != null) Dispatcher.UIThread.Post(OpenJoin);

            if (LauncherPrefs.UpdatePolicy != UpdatePolicy.Off && Update.Updater.Configured)
            {
                // In the background, and never blocking the window: a launcher
                // that will not draw until GitHub answers looks broken on a bad
                // connection.
                Update.Updater.CheckInBackground(
                    update => Dispatcher.UIThread.Post(() => ShowUpdate(update)),
                    () => Dispatcher.UIThread.Post(() =>
                    {
                        RefreshVersionLine();
                        _ = TryInstallStaged();
                    }));
            }
            _ = CatchUpPreviews();
        }

        /// <summary>
        /// Render the pictures of any map that does not have one yet, without
        /// being asked.
        ///
        /// This is what a map arriving looks like from here. A custom map is a
        /// file dropped into maps/ -- a bundle handed over by somebody, and one
        /// day a download from the server a player is joining -- and the room
        /// itself needs nothing more: the room list is built at startup and the
        /// binaries are built with it. The picture was the part that waited,
        /// because it is rendered from the room and nothing rendered it until
        /// somebody found the button. So the new map sat in the picker as a
        /// name over an empty frame, which reads as a map that failed rather
        /// than one that arrived.
        ///
        /// Nothing happens in the ordinary case, which is every map already
        /// having one; the work is one worker process per missing picture. It
        /// stays off the UI thread throughout, and the splash and the button
        /// are refreshed at the end, on it.
        /// </summary>
        private async Task CatchUpPreviews()
        {
            if (!GameFiles.Ready || !ThumbnailHost.CanRender
                || ThumbnailGenerator.MissingThumbnails().Count == 0)
            {
                return;
            }
            await ThumbnailHost.RenderMissingAsync(line => Dispatcher.UIThread.Post(() =>
            {
                if (_previewProgress != null)
                {
                    _previewProgress.Subtitle = line;
                }
            }));
            Dispatcher.UIThread.Post(() =>
            {
                RefreshSplash();
                RefreshPreviewEntry();
            });
        }

        /// <summary>
        /// Beside the picture, or under a band of it.
        ///
        /// The same controls either way -- this moves them between cells rather
        /// than building a second tree, so a card added to the screen appears
        /// on a phone without anybody having to remember to add it twice.
        /// </summary>
        private void ApplyLayout(bool narrow)
        {
            if (_laidOut && narrow == _narrow)
            {
                return;
            }
            _narrow = narrow;
            _laidOut = true;
            if (narrow)
            {
                _layout.ColumnDefinitions = new ColumnDefinitions("*");
                _layout.RowDefinitions = new RowDefinitions("Auto,*");
                _splash.Height = 150;
                _panel.Width = Double.NaN;
                Grid.SetColumn(_splash, 0);
                Grid.SetRow(_splash, 0);
                Grid.SetColumn(_updateBadge, 0);
                Grid.SetRow(_updateBadge, 0);
                Grid.SetColumn(_versionBox, 0);
                Grid.SetRow(_versionBox, 0);
                Grid.SetColumn(_debugRow, 0);
                Grid.SetRow(_debugRow, 0);
                Grid.SetColumn(_panel, 0);
                Grid.SetRow(_panel, 1);
                return;
            }
            _layout.ColumnDefinitions = new ColumnDefinitions("*,Auto");
            _layout.RowDefinitions = new RowDefinitions("*");
            _splash.Height = Double.NaN;
            _panel.Width = 400;
            Grid.SetColumn(_splash, 0);
            Grid.SetRow(_splash, 0);
            Grid.SetColumn(_updateBadge, 0);
            Grid.SetRow(_updateBadge, 0);
            Grid.SetColumn(_versionBox, 0);
            Grid.SetRow(_versionBox, 0);
            Grid.SetColumn(_debugRow, 0);
            Grid.SetRow(_debugRow, 0);
            Grid.SetColumn(_panel, 1);
            Grid.SetRow(_panel, 0);
        }

        private bool _narrow;
        private bool _laidOut;

        /// <summary>
        /// Answer a "back" gesture: the pointer's Escape and the phone's back
        /// button are the same question. True when this screen dealt with it.
        /// </summary>
        public bool GoBack()
        {
            if (_overlay.IsVisible)
            {
                // The overlay's own view owns its Escape; reaching here means
                // it declined, so take the whole thing down.
                CloseOverlay();
                return true;
            }
            if (_current != _homeCard && GameFiles.Ready)
            {
                ShowCard(_homeCard);
                return true;
            }
            return false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (!GoBack())
                {
                    Finish(default);
                }
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_observingUpdates) return;
            _observingUpdates = true;
            Update.Updater.Coordinator.StatusChanged += UpdateStatusChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_observingUpdates)
            {
                Update.Updater.Coordinator.StatusChanged -= UpdateStatusChanged;
                _observingUpdates = false;
            }
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>
        /// Release the classic compatibility controller only when the owning
        /// HomeView is finished. Overlay detachment is deliberately not a
        /// lifetime boundary: Android and desktop overlays may attach this
        /// view again while the shell remains active.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (_observingUpdates)
            {
                Update.Updater.Coordinator.StatusChanged -= UpdateStatusChanged;
                _observingUpdates = false;
            }
            await _classicPlay.DisposeAsync().ConfigureAwait(false);
            await _classicOnline.DisposeAsync().ConfigureAwait(false);
            _classicShell.Dispose();
        }

        public void Dispose()
            => DisposeAsync().AsTask().GetAwaiter().GetResult();

        private void UpdateStatusChanged(object? sender, UpdateStatus status)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_observingUpdates) return;
                string number = VersionNumber();
                switch (status.State)
                {
                    case UpdateState.Downloading when status.TotalBytes > 0:
                        int percent = (int)Math.Min(100,
                            status.BytesReceived * 100 / status.TotalBytes);
                        SayVersion($"{number} : downloading update... {percent}%",
                            GuiTheme.Warm);
                        break;
                    case UpdateState.Verifying:
                        SayVersion($"{number} : verifying update...", GuiTheme.Warm);
                        break;
                    case UpdateState.Staged:
                    case UpdateState.WaitingForSafePoint:
                        SayVersion($"{number} : update ready; waiting for a safe point",
                            GuiTheme.Warm, pressable: true);
                        if (status.State == UpdateState.Staged
                            && LauncherPrefs.UpdatePolicy == UpdatePolicy.Automatic)
                            _ = TryInstallStaged();
                        break;
                    case UpdateState.Failed:
                        SayVersion($"{number} : {status.Message ?? "update failed"}",
                            GuiTheme.Warm, pressable: true);
                        break;
                }
            });
        }

        /// <summary>Hand the answer back, once.</summary>
        private void Finish(LaunchPlan plan)
        {
            if (_finished)
            {
                return;
            }
            _finished = true;
            Plan = plan;
            Done?.Invoke(this, plan);
        }

        /// <summary>
        /// Come back from a match and be usable again. Android keeps this view
        /// alive across a match, where the desktop builds a new one each time
        /// round <c>GuiLauncher</c>'s loop.
        /// </summary>
        public void Reset()
        {
            _finished = false;
            Plan = default;
            // Android keeps one of these for the life of the app, so this is
            // where a launch begins there -- see GuiLauncher for why the roll
            // behind "Random" is held for exactly one.
            Hunters.Reroll();
            LauncherPrefs.Load();
            RefreshRooms();
            ShowCard(GameFiles.Ready ? _homeCard : _setupCard);
            RefreshSplash();
            RefreshPreviewEntry();
            _ = TryInstallStaged();
        }

        // ------------------------------------------------------------ overlays

        /// <summary>
        /// Show a view over the whole screen and wait for it to say it is done.
        ///
        /// What a modal dialog is where there are no windows to be modal to.
        /// The desktop takes the dialog path instead -- see
        /// <see cref="OpenSettings"/> -- because a real window can be moved,
        /// resized and put beside the launcher, and that is worth having where
        /// it exists.
        /// </summary>
        private Task ShowOverlay(Control view, Action<EventHandler> subscribeClosed)
        {
            var done = new TaskCompletionSource();
            // Held so that *any* way the overlay comes down finishes the wait,
            // not only the view raising Closed. GoBack takes it down directly
            // when the view declines Escape, and whoever is awaiting this would
            // otherwise wait for ever -- which on this path means the settings
            // never run the code after them (the jump to the game-files card),
            // and the continuation is never collected.
            _overlayDone = done;
            subscribeClosed((_, _) =>
            {
                CloseOverlay();
            });
            _overlay.Children.Clear();
            _overlay.Children.Add(view);
            _overlay.IsVisible = true;
            Dispatcher.UIThread.Post(() => view.Focus(), DispatcherPriority.Background);
            return done.Task;
        }

        private TaskCompletionSource? _overlayDone;

        /// <summary>
        /// The pause menu, over a running match.
        ///
        /// The platform with no windows takes this path; the desktop opens
        /// <c>PauseMenuWindow</c>, which is a real window over the game
        /// window. Both show the same <see cref="PauseMenuView"/>, so the menu
        /// cannot drift into being two menus.
        ///
        /// The settings come back to this rather than dropping the player into
        /// the match: they were reached from the pause menu and that is where
        /// closing them should land.
        /// </summary>
        public void ShowPauseMenu(Scene scene, Action onResume, Action onLeave, Action onQuit)
        {
            var view = new PauseMenuView(offerWindowMode: false);
            EventHandler? closed = null;
            void Close()
            {
                closed?.Invoke(view, EventArgs.Empty);
            }
            view.Resumed += (_, _) => { Close(); onResume(); };
            view.LeaveRequested += (_, _) => { Close(); onLeave(); };
            view.QuitRequested += (_, _) => { Close(); onQuit(); };
            // Spectating and replay recording, which this screen offers and
            // nothing was listening for.
            //
            // PauseMenuView builds the same entries on every platform -- it is
            // the desktop's pause menu, shown as an overlay here because a
            // phone has no second window to put it in -- so "Spectate",
            // "Rejoin match" and "Record replay" were all drawn, all pressable,
            // and all did nothing but close the menu, because only Resume,
            // Leave, Quit and Settings were wired up. Same handlers as
            // PauseMenuWindow's.
            view.SpectateRequested += (_, _) =>
            {
                Close();
                SpectatorMode.Start(scene);
                onResume();
            };
            view.RejoinRequested += (_, _) =>
            {
                Close();
                SpectatorMode.Rejoin(scene);
                onResume();
            };
            view.RecordToggleRequested += (_, _) =>
            {
                if (ReplayRecorder.IsRecording)
                {
                    Console.WriteLine($"[replay] recording saved to {ReplayRecorder.CurrentPath}");
                    ReplayRecorder.Stop();
                }
                else
                {
                    ReplayRecorder.Start();
                }
                Close();
                onResume();
            };
            view.SettingsRequested += async (_, _) =>
            {
                await OpenSettings(scene);
                ShowPauseMenu(scene, onResume, onLeave, onQuit);
            };
            _ = ShowOverlay(view, handler => closed += handler);
            view.FocusResume();
        }

        private void CloseOverlay()
        {
            _overlay.IsVisible = false;
            _overlay.Children.Clear();
            TaskCompletionSource? done = _overlayDone;
            _overlayDone = null;
            done?.TrySetResult();
        }

        /// <summary>True when this screen is inside a real window.</summary>
        private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

        private void ShowCard(Control card)
        {
            _cards.Children.Clear();
            _cards.Children.Add(card);
            _current = card;
            // The front card only. Which build this is is not a question
            // anybody has while choosing a server or a hunter, and the corner
            // it sits in is where those cards put their own things.
            if (_versionBox != null)
            {
                bool home = ReferenceEquals(card, _homeCard);
                _versionBox.IsVisible = home;
                if (_debugRow != null)
                {
                    _debugRow.IsVisible = home;
                    // The logs may have appeared since this card was last up.
                    RefreshShareButton();
                }
                if (home && _updateBadge.IsVisible)
                {
                    _updateBadge.IsVisible = false;
                }
                else if (!home && Update.Updater.Configured && Update.Updater.Available != null)
                {
                    _updateBadge.IsVisible = true;
                }
            }
            if (!_narrow)
            {
                _panel.Width = 400;
            }
            // The splash is a map preview everywhere but the setup card, so it
            // has to be told which one is up.
            RefreshSplash();
            // The first thing on the card takes the keyboard, so the screen can
            // be used without touching the mouse.
            Dispatcher.UIThread.Post(() => card.GetVisualDescendants()
                .OfType<Control>().FirstOrDefault(c => c.Focusable)?.Focus(),
                DispatcherPriority.Background);
        }

        private static StackPanel Card() => new() { Spacing = 2 };

        private static MenuEntry Back(Action go)
        {
            var entry = new MenuEntry("Back", titleSize: 13) { Accent = GuiTheme.TextDim };
            entry.Click += (_, _) => go();
            return entry;
        }

        // ---------------------------------------------------------------- home

        private readonly UpdateBadge _updateBadge = new();
        private MenuEntry _onlineEntry = null!;
        private MenuEntry _hostEntry = null!;
        private MenuEntry _replayEntry = null!;

        private Control BuildHomeCard()
        {
            var card = Card();
            // No descriptions under any of these. "Join" does not need a line
            // saying it joins; what the line was worth was the space it took
            // and the reading it asked for. The only subtitles left anywhere
            // are the ones that report something the player could not
            // otherwise know -- missing game files, a replay that would not
            // open -- and those are set when they happen.
            _hostEntry = new MenuEntry("Host");
            _hostEntry.Click += (_, _) => OpenHost();
            _onlineEntry = new MenuEntry("Join");
            _onlineEntry.Click += (_, _) => OpenJoin();
            _replayEntry = new MenuEntry("Replays");
            _replayEntry.Click += async (_, _) => await ChooseReplay();
            var settings = new MenuEntry("Settings");
            settings.Click += async (_, _) => await OpenSettings();
            var account = new MenuEntry("Hunter License");
            account.Click += async (_, _) =>
            {
                var view = new AccountView();
                await ShowOverlay(view, handler => view.Closed += handler);
            };
            var quit = new MenuEntry("Quit");
            quit.Click += (_, _) => Finish(default);

            // Both public multiplayer entries use the Node lobby flow. Host
            // opens the same view with an instruction to create a public lobby.
            card.Children.Add(_hostEntry);
            card.Children.Add(_onlineEntry);
            card.Children.Add(_replayEntry);
            card.Children.Add(account);
            card.Children.Add(settings);
            card.Children.Add(quit);
            RefreshVersionLine();
            return card;
        }

        private TextBlock _versionLine = null!;
        private Border _versionBox = null!;

        /// <summary>
        /// The build, in the bottom corner of the picture -- and, when there
        /// is one to take, the update.
        ///
        /// Over the splash rather than in the menu, for the update badge's own
        /// reason: knowing which build you are on is not one of the things
        /// somebody came to the launcher to do, so it does not belong in a
        /// list of them. Only on the front card, though, unlike the badge --
        /// reading a version number while picking a server or a hunter is
        /// nobody's question.
        ///
        /// It is the whole button rather than a label with one beside it. The
        /// state and the action are the same fact here: green is "nothing to
        /// do" and amber is "press this", and two controls saying that would
        /// be one of them redundant in either state.
        ///
        /// A panel behind it because a splash is a map preview and a map
        /// preview is whatever colour that room is. Eleven points of dim grey
        /// on an unknown picture is a line that is legible half the time.
        /// </summary>
        private Border BuildVersionLine()
        {
            _versionLine = new TextBlock
            {
                Text = VersionNumber(),
                FontSize = 11,
                Foreground = GuiTheme.TextDimBrush
            };
            _versionBox = new Border
            {
                Background = GuiTheme.ScrimBrush,
                BorderBrush = GuiTheme.EdgeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(9, 4, 9, 4),
                Margin = new Thickness(0, 0, 24, 22),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsVisible = false,
                Child = _versionLine
            };
            _versionBox.PointerPressed += (_, e) =>
            {
                if (_updatable)
                {
                    e.Handled = true;
                    UpdateNow();
                }
            };
            return _versionBox;
        }

        private TextBlock _debugLine = null!;
        private Border _debugBox = null!;
        private TextBlock _shareLine = null!;
        private Border _shareBox = null!;
        private StackPanel _debugRow = null!;

        /// <summary>
        /// The switch that turns the log file on, in the corner under the
        /// version.
        ///
        /// Deliberately the smallest thing on the screen. It is not a feature
        /// anybody came here for: it is what somebody is asked to turn on when
        /// they report a crash nobody else can reproduce, and it costs a
        /// growing directory and a lock on every line the program prints, so
        /// it should be findable when described over a chat window and
        /// invisible the rest of the time. The corner it is in is the one that
        /// already carries the things about the program rather than about the
        /// match -- the build, and the update -- and it is on the front card
        /// only, for the same reason those are.
        ///
        /// It says where the file went once it is on, because "turn on logging
        /// and send me the file" has a second half.
        /// </summary>
        private StackPanel BuildDebugSwitch()
        {
            _debugLine = new TextBlock
            {
                FontSize = 10,
                Foreground = GuiTheme.TextDimBrush
            };
            _debugBox = new Border
            {
                Background = GuiTheme.ScrimBrush,
                BorderBrush = GuiTheme.EdgeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = _debugLine
            };
            _debugBox.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                ToggleDebugLogs();
            };
            _shareLine = new TextBlock
            {
                Text = "\u2197 Share logs",
                FontSize = 10,
                Foreground = GuiTheme.TextDimBrush
            };
            _shareBox = new Border
            {
                Background = GuiTheme.ScrimBrush,
                BorderBrush = GuiTheme.EdgeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                IsVisible = false,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = _shareLine
            };
            ToolTip.SetTip(_shareBox,
                "Zip the log files and hand them to another app.");
            _shareBox.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                ShareLogs();
            };
            // A row rather than two placed boxes: what the switch says changes
            // with its state ("Enable debugging logs" / "Debugging logs on"),
            // so a Share button positioned by its own margin would sit at the
            // wrong distance in one of them.
            _debugRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                // Directly below the version corner, sharing its right edge.
                // The version's own margin is what it is and does not move --
                // it is the corner people know -- so this fits into the 22
                // points beneath it rather than pushing it up: tighter
                // padding, and it sits 2 clear of the bottom edge.
                Margin = new Thickness(0, 0, 24, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsVisible = false
            };
            _debugRow.Children.Add(_shareBox);
            _debugRow.Children.Add(_debugBox);
            RefreshDebugSwitch();
            return _debugRow;
        }

        /// <summary>
        /// Zip the logs and hand them over.
        ///
        /// Off the UI thread, because it reads and compresses however many
        /// files <c>DebugLog</c> is keeping, and a front screen that stops
        /// painting is a front screen that looks hung. The chooser itself goes
        /// back on the UI thread: on Android it is an activity.
        ///
        /// Everything it can say, it says on the button, for the same reason
        /// the update entry does -- there is nowhere else on this screen for a
        /// sentence, and a control that greys out and reports nothing is one
        /// people press again.
        /// </summary>
        private async void ShareLogs()
        {
            if (_sharing || Mods.LogShare.Current is not Mods.ILogShare sharer)
            {
                return;
            }
            _sharing = true;
            _shareLine.Text = "\u2197 Zipping\u2026";
            string name = Mods.LogArchive.FileName();
            string path = "";
            string error = "";
            bool built = await Task.Run(() =>
            {
                try
                {
                    path = sharer.StagingPath(name);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
                return Mods.LogArchive.Create(path, out error);
            });
            if (built)
            {
                built = sharer.Share(path, name, out error);
            }
            _sharing = false;
            if (!built)
            {
                _shareLine.Text = $"\u2197 {error}";
                _shareLine.Foreground = new SolidColorBrush(GuiTheme.Warm);
                return;
            }
            RefreshShareButton();
        }

        private bool _sharing;

        /// <summary>
        /// Whether there is anything to send, asked every time the corner is
        /// drawn rather than once: the first log appears the moment the switch
        /// beside this is turned on, and a button that only checked at startup
        /// would stay hidden for the rest of the run.
        /// </summary>
        private void RefreshShareButton()
        {
            if (_shareBox == null)
            {
                return;
            }
            if (_sharing)
            {
                return;
            }
            _shareLine.Text = "\u2197 Share logs";
            _shareLine.Foreground = GuiTheme.TextDimBrush;
            _shareBox.IsVisible = Mods.LogShare.Available;
        }

        private void ToggleDebugLogs()
        {
            LauncherPrefs.DebugLogs = !LauncherPrefs.DebugLogs;
            LauncherPrefs.Save();
            if (LauncherPrefs.DebugLogs)
            {
                // Straight away, so the run that is about to crash is the run
                // in the file -- being asked to restart first is where a
                // report like this is usually lost.
                Mods.DebugLog.Attach();
                Mods.DebugLog.Line("launcher", "debug logging turned on from the front screen");
            }
            else
            {
                Mods.DebugLog.Line("launcher", "debug logging turned off from the front screen");
                Mods.DebugLog.Detach();
            }
            RefreshDebugSwitch();
        }

        private void RefreshDebugSwitch()
        {
            if (_debugLine == null)
            {
                return;
            }
            RefreshShareButton();
            if (!LauncherPrefs.DebugLogs)
            {
                _debugLine.Text = "\u25A2 Enable debugging logs";
                _debugLine.Foreground = GuiTheme.TextDimBrush;
                ToolTip.SetTip(_debugBox, "Write everything this build can say "
                    + "about itself to a file, for a bug report.");
                return;
            }
            _debugLine.Text = "\u25A3 Debugging logs on";
            _debugLine.Foreground = new SolidColorBrush(GuiTheme.Warm);
            ToolTip.SetTip(_debugBox, Mods.DebugLog.Path is string path
                ? $"Writing to {path}"
                : "Logging starts with the next run.");
        }

        /// <summary>
        /// "1.2.3", or what to say instead when this build is not a release.
        ///
        /// Not <c>BuildVersion.Display</c>, which puts a v in front: this is a
        /// corner of a picture rather than a sentence, and the v is a letter
        /// that carries nothing.
        /// </summary>
        private static string VersionNumber()
        {
            Version? current = BuildVersion.Current;
            return current == null ? "a local build" : current.ToString(3);
        }

        /// <summary>Whether pressing the corner would do anything.</summary>
        private bool _updatable;

        /// <summary>What the corner says, and in what colour.</summary>
        private void SayVersion(string text, Color colour, bool pressable = false)
        {
            if (_versionLine == null)
            {
                return;
            }
            _versionLine.Text = text;
            _versionLine.Foreground = new SolidColorBrush(colour);
            _updatable = pressable;
            _versionBox.Cursor = new Cursor(
                pressable ? StandardCursorType.Hand : StandardCursorType.Arrow);
        }

        /// <summary>
        /// What the version line says and what colour it is.
        ///
        /// Three states, not two. Green is "this is the published build";
        /// amber is "there is a newer one", which is the only state that also
        /// puts a button above it; and dim is everything else -- a local
        /// build, or a check that has not answered yet or could not. Painting
        /// "no answer" green would be the one wrong thing this can do: a
        /// server refuses a client on a different build at Hello, so being
        /// told you are current when nobody has checked is worse than being
        /// told nothing.
        /// </summary>
        private void RefreshVersionLine()
        {
            if (_versionLine == null)
            {
                return;
            }
            if (_updating)
            {
                // Mid-download; the coordinator owns the operation until it is done.
                return;
            }
            string number = VersionNumber();
            if (Update.Updater.Configured && Update.Updater.Available != null)
            {
                SayVersion($"{number} : Update available ! Click here to update",
                    GuiTheme.Warm, pressable: true);
                return;
            }
            // Dim, not green, for a local build and for a check that has not
            // answered -- and those are two different things from "up to
            // date". A server refuses a client on a different build at Hello,
            // so being told you are current when nobody has asked is worse
            // than being told nothing.
            SayVersion(number, BuildVersion.IsRelease && Update.Updater.Checked
                ? GuiTheme.Good
                : GuiTheme.TextDim);
        }

        private void ShowUpdate(UpdateInfo update)
        {
            // The badge stays for every card *but* the front one, which is
            // where the version corner does the same job in the other corner
            // of the same picture. Keeping it elsewhere is what it was put
            // over the splash for: a fresh install sits on the game-files
            // card, which is exactly where an out-of-date copy is most likely
            // to be, and that card has no version corner.
            _updateBadge.Show(update.AssetName.Length > 0
                ? $"{update.Tag} is out -- get {update.AssetName}"
                : $"{update.Tag} is out");
            _updateBadge.IsVisible = !ReferenceEquals(_current, _homeCard);
            // The picture's own caption moves up out of the way.
            _splash.BottomInset = _updateBadge.DesiredSize.Height + 22;
            RefreshVersionLine();
        }

        /// <summary>
        /// Take the update.
        ///
        /// A package already verified and staged by Automatic is applied
        /// directly. NotifyOnly uses the same coordinator download/install
        /// path when the player explicitly presses this entry; it never falls
        /// back to a browser for an installable artifact.
        /// </summary>
        private void UpdateNow()
        {
            UpdateInfo? found = Update.Updater.Available;
            if (found == null)
            {
                if (Update.Updater.Coordinator.Status.State is
                    UpdateState.Staged or UpdateState.WaitingForSafePoint)
                    _ = DownloadAndInstall(null);
                return;
            }
            UpdateInfo update = found.Value;
            if (Update.Updater.Coordinator.Status.State is
                UpdateState.Staged or UpdateState.WaitingForSafePoint)
            {
                _ = TryInstallStaged(explicitRequest: true);
                return;
            }
            _ = DownloadAndInstall(update);
        }

        private async Task TryInstallStaged(bool explicitRequest = false)
        {
            if (_updating || (!explicitRequest
                && LauncherPrefs.UpdatePolicy != UpdatePolicy.Automatic))
                return;
            if (Update.Updater.Coordinator.Status.State is not
                (UpdateState.Staged or UpdateState.WaitingForSafePoint))
                return;
            IUpdateInstaller? installer = Update.UpdateInstall.Current;
            if (installer == null)
                return;
            if (!installer.Allowed)
            {
                installer.RequestPermission();
                SayVersion($"{VersionNumber()} : allow installs from this app, then press again",
                    GuiTheme.Warm, pressable: true);
                return;
            }
            _updating = true;
            bool started = await Update.Updater.InstallStagedAsync().ConfigureAwait(true);
            if (!started)
            {
                _updating = false;
                RefreshVersionLine();
                return;
            }
            if (installer.ExitAfterInstall)
                Finish(default);
            else
                _updating = false;
        }

        private async Task DownloadAndInstall(UpdateInfo? update)
        {
            if (_updating)
            {
                return;
            }
            string number = VersionNumber();
            _updating = true;
            string label = update is UpdateInfo selected
                ? (selected.AssetName.Length > 0 ? selected.AssetName : selected.Tag)
                : "the verified package";
            SayVersion($"{number} : downloading {label}...", GuiTheme.Warm);
            bool started = await Update.Updater.DownloadAndInstallAsync().ConfigureAwait(true);
            _updating = false;
            if (!started)
            {
                string message = Update.Updater.Coordinator.Status.Message
                    ?? "the update could not be staged";
                SayVersion($"{number} : {message}", GuiTheme.Warm, pressable: true);
                return;
            }
            Update.IUpdateInstaller? installer = Update.UpdateInstall.Current;
            SayVersion(installer?.ExitAfterInstall == true
                ? $"{number} : restarting to finish..."
                : $"{number} : waiting for the system installer...", GuiTheme.Warm);
            if (installer?.ExitAfterInstall == true) Finish(default);
        }

        private bool _updating;

        /// <summary>
        /// Everything but "game files" is unusable until there is something to
        /// load, and says so rather than failing when pressed.
        /// </summary>
        private void RefreshGameFilesState()
        {
            bool ready = GameFiles.Ready;
            _onlineEntry.IsEnabled = ready;
            _hostEntry.IsEnabled = ready;
            _replayEntry.IsEnabled = ready;
            // Game files moved into the settings, so there is no row here to
            // colour any more. The two entries that need an extract stay
            // disabled until setup is complete.
            _hostEntry.Subtitle = ready ? "" : GameFiles.Describe();
            _hostEntry.SubtitleColor = ready ? GuiTheme.TextDim : GuiTheme.Warm;
            _onlineEntry.Subtitle = ready ? "" : GameFiles.Describe();
            _onlineEntry.SubtitleColor = ready ? GuiTheme.TextDim : GuiTheme.Warm;
        }

        // --------------------------------------------------------------- setup

        private Control BuildSetupCard()
        {
            var card = Card();
            var log = new Note("");
            var scroll = new ScrollViewer
            {
                Height = 150,
                Content = log,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var choose = new MenuEntry("Choose your .nds file", "", titleSize: 15)
            {
                Primary = true,
                Height = 44
            };
            choose.Click += async (_, _) => await ChooseRom(choose, log);

            card.Children.Add(new Caption("Game files"));
            card.Children.Add(new Note(
                Mods.Branding.Name + " needs your own Metroid Prime Hunters cartridge dump. It "
                + "unpacks what it needs next to this program and leaves the file "
                + "alone. No game data is included in this download, and none is "
                + "downloaded."));
            if (GameFiles.InProcessSetup)
            {
                card.Children.Add(new Note("The unpacked files land in " + GameFiles.Root
                    + " -- this device's own folder for the app, which shows up over USB "
                    + "under Android/data. Files already copied there are found without "
                    + "picking anything.", GuiTheme.TextDim));
            }
            card.Children.Add(choose);
            // Previews are rendered here from the files that are here. A run
            // can be interrupted, and files can arrive after one, so asking
            // for the missing ones has to be possible without setting up again.
            _previewEntry = new MenuEntry("Render map previews", "", titleSize: 13)
            {
                Accent = GuiTheme.TextDim
            };
            _previewEntry.Click += async (_, _) =>
            {
                _previewEntry.IsEnabled = false;
                _previewEntry.Title = "Rendering...";
                // The subtitle as well as the log: on Android the run is
                // offscreen and in other processes, so this row is the only
                // thing on the screen that says it is happening.
                _previewProgress = _previewEntry;
                await RenderPreviews(log);
                _previewProgress = null;
                _previewEntry.IsEnabled = true;
                _previewEntry.Title = "Render map previews";
                RefreshPreviewEntry();
            };
            card.Children.Add(_previewEntry);
            card.Children.Add(_setupProgress);
            card.Children.Add(scroll);
            // Hidden until there is something to go back to. Before the game
            // files are set up this card is the whole launcher: the menu behind
            // it has no map previews to show on the left and four of its five
            // entries refused, which is a worse first impression than one
            // screen asking for the one thing it needs.
            _setupBack = Back(() => ShowCard(_homeCard));
            _setupBack.IsVisible = false;
            card.Children.Add(_setupBack);
            return card;
        }

        private async Task ChooseRom(MenuEntry button, Note log)
        {
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top == null)
            {
                return;
            }
            var options = new FilePickerOpenOptions
            {
                Title = "Your Metroid Prime Hunters cartridge dump",
                AllowMultiple = false
            };
            if (!OperatingSystem.IsAndroid())
            {
                // Patterns are what Windows, Linux and the browser filter on.
                // Android filters by MIME type, and .nds has none -- inventing
                // one there produces a picker in which every file is refused.
                options.FileTypeFilter = new[]
                {
                    new FilePickerFileType("Nintendo DS ROM")
                    {
                        Patterns = new[] { "*.nds" }
                    },
                    new FilePickerFileType("Every file") { Patterns = new[] { "*" } }
                };
            }
            IReadOnlyList<IStorageFile> picked =
                await top.StorageProvider.OpenFilePickerAsync(options);
            if (picked.Count == 0)
            {
                return;
            }
            button.IsEnabled = false;
            button.Title = "Working...";
            log.Text = "";
            var progress = new SetupProgress();
            _setupProgress.IsVisible = true;
            _setupProgress.Set(0, "Starting");
            string? path = picked[0].TryGetLocalPath();
            string? scratch = null;
            if (path == null)
            {
                // Android hands back a content:// document with no path behind
                // it. Copying is the only way to give the extractor a file, and
                // it is the player's own cartridge dump, so it is copied into
                // the app's directory and deleted afterwards.
                log.Text = "Copying the file onto this device...";
                try
                {
                    scratch = Path.Combine(GameFiles.Root, "picked.nds");
                    await using (Stream source = await picked[0].OpenReadAsync())
                    await using (var target = File.Create(scratch))
                    {
                        await source.CopyToAsync(target);
                    }
                    path = scratch;
                }
                catch (Exception ex)
                {
                    log.Text = $"The file could not be read: {ex.Message}";
                    button.IsEnabled = true;
                    button.Title = "Choose your .nds file";
                    return;
                }
            }
            // Extraction takes minutes. Off the UI thread, or the screen stops
            // answering at the exact moment it is doing the one thing a fresh
            // install needs.
            bool ok = await Task.Run(() => GameFiles.RunSetup(path, line =>
                Dispatcher.UIThread.Post(() =>
                {
                    log.Text = Tail(log.Text, line);
                    if (progress.Observe(line))
                    {
                        _setupProgress.Set(progress.Fraction, progress.Stage);
                    }
                })));
            if (scratch != null)
            {
                try
                {
                    File.Delete(scratch);
                }
                catch (IOException)
                {
                    // A copy left behind is untidy, not a failure worth saying.
                }
            }
            if (ok)
            {
                await RenderPreviews(log, progress);
            }
            progress.Finish(ok);
            _setupProgress.Set(progress.Fraction, progress.Stage);
            button.IsEnabled = true;
            button.Title = "Choose your .nds file";
            log.Text = Tail(log.Text, ok ? "Ready to play." : "Setup did not finish.");
            RefreshGameFilesState();
            if (ok)
            {
                RefreshRooms();
            }
            RefreshSplash();
            if (ok)
            {
                // The launcher proper, now that there is something behind it:
                // map previews to show on the left, and every entry usable.
                _setupBack.IsVisible = true;
                _setupProgress.IsVisible = false;
                ShowCard(_homeCard);
            }
        }

        /// <summary>
        /// Pick a recorded replay and hand it to <c>MatchStart</c> as a
        /// launch plan.
        ///
        /// This machine's own recordings first, listed from the folder they
        /// are written to, and the system file picker behind an "Open a
        /// file..." entry rather than in front of everything.
        ///
        /// The order used to be the other way round -- the picker was the
        /// whole of it -- and on Android that could not reach the recordings
        /// at all. <see cref="ReplayRecorder"/> writes into the app's own
        /// directory, and since Android 11 <c>Android/data</c> is excluded
        /// from the Storage Access Framework: the picker cannot be pointed at
        /// it and it cannot be navigated to, however absolute the path handed
        /// over is. Nothing about that stops *this* app reading the folder --
        /// it owns it, and needs no permission for it.
        /// </summary>
        private async Task ChooseReplay()
        {
            var view = new ReplayPickerView(ReplayLibrary.List(), ReplayLibrary.Directory);
            await ShowOverlay(view, handler => view.Closed += handler);
            if (view.Path != null)
            {
                await PlayReplay(view.Path);
                return;
            }
            if (view.ImportRequested)
            {
                await ImportReplay();
            }
        }

        /// <summary>
        /// The system file picker, for a replay that came from somewhere else on
        /// the device.
        /// </summary>
        private async Task ImportReplay()
        {
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top == null)
            {
                return;
            }
            var options = new FilePickerOpenOptions
            {
                Title = "Replays",
                AllowMultiple = false
            };
            if (!OperatingSystem.IsAndroid())
            {
                // Patterns are what Windows, Linux and the browser filter on.
                // Android filters by MIME type, and a replay file has none --
                // the same trap the cartridge picker is written around, and
                // the first of the two reasons this screen could open a replay
                // everywhere except on the platform it was drawn for.
                options.FileTypeFilter = new[]
                {
                    new FilePickerFileType($"{Branding.Name} replay") { Patterns = new[] { $"*{ReplayFile.Extension}" } },
                    new FilePickerFileType("Every file") { Patterns = new[] { "*" } }
                };
            }
            try
            {
                // Absolute, because Paths.Export is normally empty and the
                // recorder's own combine is therefore relative to the working
                // directory -- and a relative path is one no storage provider
                // can resolve. Worth setting on the desktop, where the picker
                // will honour it; on Android it is ignored, since the folder
                // is one the Storage Access Framework does not show. That is
                // what ReplayPickerView is for.
                string replayDir = ReplayLibrary.Directory;
                if (Directory.Exists(replayDir))
                {
                    options.SuggestedStartLocation =
                        await top.StorageProvider.TryGetFolderFromPathAsync(replayDir);
                }
            }
            catch (IOException)
            {
                // No default folder is a worse first run than one with a
                // clean slate, not a reason to refuse the picker outright.
            }
            IReadOnlyList<IStorageFile> picked = await top.StorageProvider.OpenFilePickerAsync(options);
            if (picked.Count == 0)
            {
                return;
            }
            string? path = picked[0].TryGetLocalPath();
            if (path == null)
            {
                // The second reason. Android hands back a content:// document
                // with no file behind it, and the replay reader takes a path --
                // so the document is copied into the app's own directory and
                // played from there. Kept afterwards rather than deleted: the
                // reader holds the file open for the whole session, and the
                // next replay overwrites it.
                try
                {
                    string scratch = Path.Combine(GameFiles.Root, $"picked{ReplayFile.Extension}");
                    await using (Stream source = await picked[0].OpenReadAsync())
                    await using (var target = File.Create(scratch))
                    {
                        await source.CopyToAsync(target);
                    }
                    path = scratch;
                }
                catch (Exception ex)
                {
                    _replayEntry.Subtitle = $"That file could not be read: {ex.Message}";
                    _replayEntry.SubtitleColor = GuiTheme.Warm;
                    return;
                }
            }
            await PlayReplay(path);
        }

        /// <summary>Load a replay file and, if it reads, start playing it.</summary>
        private async Task PlayReplay(string path)
        {
            // Joined here, not inside MatchStart: a failure has to land back
            // on a screen that is still open to show it on. Console.WriteLine
            // is where ReplayPlayback.Join otherwise says why -- invisible on
            // the Windows build, which has no console for anything the
            // launcher starts (only a typed command gets one). Silently
            // returning to the menu with the real reason nowhere the player
            // could see it is the bug this replaces.
            _replayEntry.IsEnabled = false;
            _replayEntry.Title = "Loading...";
            bool joined = await Task.Run(() => ReplayPlayback.Prepare(path));
            if (!joined)
            {
                _replayEntry.IsEnabled = true;
                _replayEntry.Title = "Replays";
                _replayEntry.Subtitle = ReplayPlayback.LastError ?? "That file could not be read as a replay.";
                _replayEntry.SubtitleColor = GuiTheme.Warm;
                return;
            }
            Finish(new LaunchPlan
            {
                Kind = LaunchKind.Replay,
                ReplayPath = path,
                Hunter = Hunter.Samus,
                PlayerName = "",
                RoomKey = ""
            });
        }

        /// <summary>
        /// Render the map previews that are missing.
        ///
        /// The pictures are made here, on this machine, from the files that
        /// were just unpacked -- worker processes on the desktop, the phone's
        /// own GL thread on Android. Nothing is downloaded and no picture ships
        /// with the program.
        /// </summary>
        private async Task RenderPreviews(Note log, SetupProgress? progress = null)
        {
            if (!ThumbnailHost.CanRender)
            {
                return;
            }
            log.Text = Tail(log.Text, "Rendering map previews...");
            await ThumbnailHost.RenderMissingAsync(line => Dispatcher.UIThread.Post(() =>
            {
                log.Text = Tail(log.Text, line);
                if (_previewProgress != null)
                {
                    _previewProgress.Subtitle = line;
                }
                if (progress != null && progress.Observe(line))
                {
                    _setupProgress.Set(progress.Fraction, progress.Stage);
                }
            }));
            RefreshSplash();
        }

        /// <summary>How many previews are still to render, or nothing to say.</summary>
        private void RefreshPreviewEntry()
        {
            if (_previewEntry == null)
            {
                return;
            }
            if (!GameFiles.Ready || !ThumbnailHost.CanRender)
            {
                _previewEntry.IsVisible = false;
                return;
            }
            int missing = ThumbnailGenerator.MissingThumbnails().Count;
            _previewEntry.IsVisible = true;
            _previewEntry.Subtitle = missing == 0
                ? "Every map has one"
                : $"{missing} still to render, from your own files";
            _previewEntry.IsEnabled = missing > 0;
        }

        /// <summary>Keep the last few lines; the extraction prints hundreds.</summary>
        private static string Tail(string? existing, string line)
        {
            // The separator matters: without it each new line was glued onto
            // the end of the previous one and the whole log read as one
            // unbroken paragraph.
            string[] lines = ((existing ?? "") + "\n" + line)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return String.Join("\n", lines.Skip(Math.Max(0, lines.Length - 8)));
        }

        // --------------------------------------------------------------- nodes

        /// <summary>
        /// Host and Join deliberately share the Node browser. The Node owns
        /// lobby membership and Worker placement; the client only performs the
        /// authenticated Node handshake and the later UDP Worker handoff.
        /// </summary>
        private async void OpenNodeBrowser(bool createLobby)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            AccountIdentity? identity = AccountSessions.Current?.IsSignedIn == true
                ? AccountSessions.Current.Identity : null;
            if (identity is { } account)
                _classicShell.SetIdentity(account.PlayerId, LauncherPrefs.PlayerName,
                    account.EmailEligibleForOfficialPlay);
            else
                _classicShell.SelectGuest(LauncherPrefs.PlayerName);
            _classicPlay.SetMaps(_playable);
            var view = new NodeBrowserView(_classicPlay, createLobby);
            view.Launch += (_, plan) => { CloseOverlay(); Finish(plan); };
            await ShowOverlay(view, handler => view.Closed += handler);
        }

        private void OpenHost() => OpenNodeBrowser(createLobby: true);

        private void OpenJoin() => OpenNodeBrowser(createLobby: false);

        // ------------------------------------------------------------ settings

        /// <summary>
        /// The settings, over the launcher.
        ///
        /// A window where there are windows -- the same one the pause menu
        /// opens mid-match -- and the same view as a full-screen overlay where
        /// there are not. Either way it is <see cref="SettingsView"/>: a rail
        /// of eight sections and several dozen rows, which is not a thing to
        /// page through in a 400-pixel column beside a picture.
        /// </summary>
        private async Task OpenSettings(Scene? scene = null)
        {
            try
            {
                var view = new SettingsView(_settings, inGame: scene != null, scene: scene);
                // Noted while the settings are up and acted on once they are
                // down: showing a card behind a window that is still open is
                // how you end up on a screen you cannot see.
                bool gameFiles = false;
                view.GameFilesRequested += (_, _) => gameFiles = true;
                // The same overlay the map picker uses, for the same reason.
                // The pause menu still opens SettingsWindow: it is over a game
                // window, and there is no front screen there to lay this on.
                await ShowOverlay(view, handler => view.Closed += handler);
                if (gameFiles)
                {
                    ShowCard(_setupCard);
                    return;
                }
            }
            catch (Exception ex)
            {
                // A failure here used to be an unhandled exception over a
                // launcher, which is a front screen that simply vanishes.
                Console.WriteLine($"[launcher] the settings could not be opened: {ex.Message}");
                return;
            }
            RefreshSplash();
        }

        // --------------------------------------------------------------- shared

        private void RefreshSplash()
        {
            RefreshGameFilesState();
            string? room = _playable.Contains(_settings.RoomKey) ? _settings.RoomKey : null;
            // Nothing from the game while the game is still being unpacked.
            // The setup card is up for the whole of the first run -- the
            // extraction and then the preview rendering -- and map pictures
            // appearing one at a time beside it is the generation being
            // watched, which is the thing it was moved offscreen to avoid.
            if (ReferenceEquals(_current, _setupCard))
            {
                room = null;
            }
            _splash.ShowRoom(room);
        }

        /// <summary>
        /// Pick up rooms that did not exist when this screen was built -- a
        /// fresh install opens with no game files, so the room list came up
        /// empty. First-time setup is the only path that changes it while
        /// this screen is up.
        /// </summary>
        private void RefreshRooms()
        {
            if (!GameFiles.Ready)
            {
                return;
            }
            _playable.Clear();
            foreach (string room in ThumbnailGenerator.MultiplayerRooms())
            {
                _playable.Add(room);
            }
            _classicPlay.SetMaps(_playable);
        }
    }
}
