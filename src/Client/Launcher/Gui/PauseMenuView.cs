using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MphRead.Mods.Network;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// What Escape shows during a match: resume, the settings, and the two ways
    /// out.
    ///
    /// A view rather than a window, because there is a platform with no windows
    /// on it to be. <c>DesktopGameOverlayWindow</c> wraps this on the desktop,
    /// where one persistent native overlay over a still-running match is the
    /// right shape;
    /// Android shows the same object through the launcher's full-screen
    /// overlay, which is what <see cref="HomeView"/> already does with the
    /// settings. One menu either way, so an entry added here turns up on both
    /// rather than on whichever was remembered.
    ///
    /// It decides nothing itself. Every entry raises an event and the host acts
    /// on it: leaving a match is closing a window on one platform and swapping
    /// two views on the other, and neither of those belongs in a menu.
    /// </summary>
    internal sealed class PauseMenuView : UserControl
    {
        public event EventHandler? Resumed;
        public event EventHandler? SettingsRequested;
        public event EventHandler? LeaveRequested;
        public event EventHandler? QuitRequested;
        public event EventHandler? FullscreenRequested;
        public event EventHandler? SpectateRequested;
        public event EventHandler? RejoinRequested;
        public event EventHandler? RecordToggleRequested;
        /// <summary>Raised after the player confirms a restart proposal.</summary>
        public event EventHandler? RestartMatchRequested;
        /// <summary>Raised after the player confirms opening the map picker.</summary>
        public event EventHandler? ChangeMapRequested;
        /// <summary>Raised for one explicit yes/no response to an active ballot.</summary>
        public event Action<bool>? TransitionVoteRequested;

        private readonly MenuEntry _resume;
        private readonly IMatchTransitionMenuActions? _transitionActions;
        private Border? _transitionGroup;
        private TextBlock? _transitionStatus;
        private TextBlock? _transitionDetails;
        private TextBlock? _transitionConfirmation;
        private MenuEntry? _restartMatch;
        private MenuEntry? _changeMap;
        private MenuEntry? _voteYes;
        private MenuEntry? _voteNo;
        private MenuEntry? _confirmTransition;
        private MenuEntry? _cancelTransition;
        private MenuEntry? _transitionFocusRestore;
        private TransitionIntent _pendingTransition;

        private enum TransitionIntent
        {
            None,
            Restart,
            ChangeMap
        }

        /// <param name="offerWindowMode">
        /// Show the fullscreen/windowed entry. False on a phone, which has one
        /// window, it is already the whole screen, and there is no F11.
        /// </param>
        public PauseMenuView(bool offerWindowMode,
            IMatchTransitionMenuActions? transitionActions = null)
        {
            _transitionActions = transitionActions;
            // The host is the size of the game, on every platform: a phone's
            // overlay is the screen and the desktop's window now covers the
            // one the match is being played in. So the entries are always a
            // panel of a stated width in the middle, never a column stretched
            // across whatever the match happens to be running at -- 1024 or
            // 3840 -- which is a menu you have to hunt across.
            const double panelWidth = 500;
            var stack = new StackPanel { Spacing = 10 };
            stack.Children.Add(BuildHeader());

            _resume = Entry("Resume session",
                () => Resumed?.Invoke(this, EventArgs.Empty), GuiTheme.Accent, primary: true);
            _resume.Height = 48;
            stack.Children.Add(_resume);

            if (CanShowTransitionMenu())
            {
                _transitionGroup = BuildTransitionGroup();
                stack.Children.Add(_transitionGroup);
                RefreshTransitionPresentation();
            }

            // Keep every secondary action in one bounded control sector. The
            // hosts still decide what the actions mean; this view only makes
            // the hierarchy visible instead of presenting seven equal exits.
            var session = new StackPanel { Spacing = 2 };
            Add(session, "Settings",
                () => SettingsRequested?.Invoke(this, EventArgs.Empty));
            if (offerWindowMode)
            {
                var windowEntry = new MenuEntry(WindowLabel(), titleSize: 16);
                windowEntry.Click += (_, _) =>
                {
                    FullscreenRequested?.Invoke(this, EventArgs.Empty);
                    // The game thread does it on the next frame; reflect it
                    // here straight away so the label is not a lie for 16
                    // milliseconds.
                    windowEntry.Title = WindowMode.IsFullscreen ? "Windowed" : "Fullscreen";
                };
                session.Children.Add(windowEntry);
            }
            if (!ReplayPlayback.IsActive)
            {
                if (SpectatorMode.IsSpectating)
                {
                    Add(session, "Rejoin match",
                        () => RejoinRequested?.Invoke(this, EventArgs.Empty));
                }
                else if (SpectatorMode.CanSpectate)
                {
                    Add(session, "Spectate",
                        () => SpectateRequested?.Invoke(this, EventArgs.Empty));
                }
                if (AuthoritativePlay.Current != null)
                {
                    MenuEntry record = Add(session,
                        ReplayRecorder.IsRecording ? "Stop recording" : "Record replay",
                        () => RecordToggleRequested?.Invoke(this, EventArgs.Empty));
                    record.Accent = ReplayRecorder.IsRecording ? GuiTheme.Warm : GuiTheme.Accent;
                }
            }
            stack.Children.Add(BuildGroup("System & session", session));

            var exit = new StackPanel { Spacing = 2 };
            MenuEntry leave = Add(exit, "Leave match",
                () => LeaveRequested?.Invoke(this, EventArgs.Empty));
            leave.Accent = GuiTheme.Warm;
            MenuEntry quit = Add(exit, "Quit game",
                () => QuitRequested?.Invoke(this, EventArgs.Empty));
            quit.Accent = GuiTheme.Bad;
            stack.Children.Add(BuildGroup("Disengage", exit));
            stack.Children.Add(BuildFooter());

            var panel = new Border
            {
                Background = GuiTheme.PanelBrush,
                // An edge, because what is behind this is a scrim of nearly
                // the same colour over a running match: without one the panel
                // and the dimmed game are two shades of the same dark and the
                // menu has no shape.
                BorderBrush = GuiTheme.EdgeBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(20, 18, 20, 18),
                Child = stack
            };
            panel.MaxWidth = panelWidth;
            panel.CornerRadius = new CornerRadius(6);
            panel.HorizontalAlignment = HorizontalAlignment.Center;
            panel.VerticalAlignment = VerticalAlignment.Center;
            // What the panel needs, worked out from what was just put in it
            // rather than measured later: every entry states its own height,
            // so this is a fact about the menu and not a guess about layout.
            double needed = PanelPadding;
            foreach (Control child in stack.Children)
            {
                // Every entry here states its height; anything that did not
                // would measure as NaN and take the whole sum with it.
                needed += (Double.IsNaN(child.Height) ? 0 : child.Height) + stack.Spacing;
            }
            _neededHeight = needed;
            // Shrunk to fit, then scrolled if even that is not enough.
            //
            // A maximum width rather than a fixed one, and a scroller under
            // it, because the host is the game window and the game window is
            // whatever size the player dragged it to -- but a scrollbar is a
            // poor answer for a pause menu: what it produces is a panel with
            // its top and bottom cut off, which is what "the menu is always
            // bitten" was. Scaling is the better one at this size, because
            // there is nothing here to reflow: seven entries in a column stay
            // seven entries in a column, just smaller. It only ever shrinks --
            // a menu that grew to fill a 4K window would be a menu in
            // 40-point type.
            _scaler = new LayoutTransformControl
            {
                Child = panel,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var scroller = new ScrollViewer
            {
                Content = _scaler,
                Padding = new Thickness(12),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            scroller.SizeChanged += (_, e) => FitToHost(e.NewSize.Height);
            Content = scroller;
        }

        internal IMatchTransitionMenuActions? TransitionActions => _transitionActions;
        internal bool TransitionMenuVisible => _transitionGroup?.IsVisible == true;
        internal bool TransitionConfirmationVisible
            => _pendingTransition != TransitionIntent.None;

        private bool CanShowTransitionMenu()
            => _transitionActions?.TransitionMenuSupported == true
                && !ReplayPlayback.IsActive && !ReplayPlayback.IsModern
                && AuthoritativePlay.Current?.IsObserver != true;

        private Border BuildTransitionGroup()
        {
            var entries = new StackPanel { Spacing = 2 };
            _transitionStatus = new TextBlock
            {
                Height = 24,
                TextWrapping = TextWrapping.Wrap,
                Foreground = GuiTheme.TextDimBrush,
                FontSize = 12
            };
            _transitionDetails = new TextBlock
            {
                Height = 42,
                TextWrapping = TextWrapping.Wrap,
                Foreground = GuiTheme.TextBrush,
                FontSize = 12
            };
            entries.Children.Add(_transitionStatus);
            entries.Children.Add(_transitionDetails);

            _restartMatch = Add(entries, "Restart match",
                () => BeginTransitionConfirmation(TransitionIntent.Restart));
            _restartMatch.Subtitle = "Ask the lobby to start a fresh arena.";
            _changeMap = Add(entries, "Change map",
                () => BeginTransitionConfirmation(TransitionIntent.ChangeMap));
            _changeMap.Subtitle = "Choose another map hosted by this Node.";
            _voteYes = Add(entries, "Vote yes",
                () => TransitionVoteRequested?.Invoke(true));
            _voteYes.Accent = GuiTheme.Accent;
            _voteNo = Add(entries, "Vote no",
                () => TransitionVoteRequested?.Invoke(false));
            _voteNo.Accent = GuiTheme.Warm;

            _transitionConfirmation = new TextBlock
            {
                Height = 44,
                TextWrapping = TextWrapping.Wrap,
                Foreground = GuiTheme.WarmBrush,
                FontSize = 12,
                IsVisible = false
            };
            entries.Children.Add(_transitionConfirmation);
            _confirmTransition = Add(entries, "Confirm",
                ConfirmPendingTransition);
            _confirmTransition.Accent = GuiTheme.Warm;
            _confirmTransition.IsVisible = false;
            _cancelTransition = Add(entries, "Cancel",
                CancelPendingTransition);
            _cancelTransition.Accent = GuiTheme.TextDim;
            _cancelTransition.IsVisible = false;

            var group = BuildGroup("Match transition", entries);
            group.IsVisible = true;
            return group;
        }

        private void BeginTransitionConfirmation(TransitionIntent intent)
        {
            if (_transitionActions is not { TransitionMenuSupported: true }
                || _transitionActions.TransitionRequestInFlight)
                return;
            if (intent == TransitionIntent.ChangeMap
                && _transitionActions.AvailableTransitionMaps.Count == 0)
                return;
            _pendingTransition = intent;
            _transitionFocusRestore = intent == TransitionIntent.Restart
                ? _restartMatch : _changeMap;
            if (_transitionConfirmation != null)
            {
                string action = intent == TransitionIntent.Restart
                    ? "Restart this match for everyone?"
                    : "Choose a different hosted map for everyone?";
                _transitionConfirmation.Text = action + " Confirm to propose it.";
                _transitionConfirmation.IsVisible = true;
            }
            if (_confirmTransition != null) _confirmTransition.IsVisible = true;
            if (_cancelTransition != null) _cancelTransition.IsVisible = true;
            if (_restartMatch != null) _restartMatch.IsEnabled = false;
            if (_changeMap != null) _changeMap.IsEnabled = false;
            if (_voteYes != null) _voteYes.IsEnabled = false;
            if (_voteNo != null) _voteNo.IsEnabled = false;
            _confirmTransition?.Focus();
        }

        internal void BeginRestartConfirmation()
            => BeginTransitionConfirmation(TransitionIntent.Restart);

        internal void BeginChangeMapConfirmation()
            => BeginTransitionConfirmation(TransitionIntent.ChangeMap);

        internal void ConfirmPendingTransition()
        {
            TransitionIntent intent = _pendingTransition;
            if (intent == TransitionIntent.None) return;
            MenuEntry? restoreFocus = _transitionFocusRestore;
            _pendingTransition = TransitionIntent.None;
            _transitionFocusRestore = null;
            if (_transitionConfirmation != null) _transitionConfirmation.IsVisible = false;
            if (_confirmTransition != null) _confirmTransition.IsVisible = false;
            if (_cancelTransition != null) _cancelTransition.IsVisible = false;
            RefreshTransitionPresentation();
            if (intent == TransitionIntent.Restart)
                RestartMatchRequested?.Invoke(this, EventArgs.Empty);
            else
                ChangeMapRequested?.Invoke(this, EventArgs.Empty);
            if (restoreFocus?.IsEnabled == true) restoreFocus.Focus();
        }

        internal void CancelPendingTransition()
        {
            if (_pendingTransition == TransitionIntent.None) return;
            MenuEntry? restoreFocus = _transitionFocusRestore;
            _pendingTransition = TransitionIntent.None;
            _transitionFocusRestore = null;
            if (_transitionConfirmation != null) _transitionConfirmation.IsVisible = false;
            if (_confirmTransition != null) _confirmTransition.IsVisible = false;
            if (_cancelTransition != null) _cancelTransition.IsVisible = false;
            RefreshTransitionPresentation();
            if (restoreFocus?.IsEnabled == true) restoreFocus.Focus();
        }

        /// <summary>Refreshes the presentation after a Node snapshot changes.</summary>
        internal void RefreshTransitionPresentation()
        {
            if (_transitionGroup == null || _transitionActions == null) return;
            if (!CanShowTransitionMenu())
            {
                _transitionGroup.IsVisible = false;
                return;
            }
            _transitionGroup.IsVisible = true;
            NodeMatchTransitionVoteSnapshot? vote = _transitionActions.TransitionVote;
            bool active = vote?.State == MatchTransitionVoteState.Pending;
            bool busy = _transitionActions.TransitionRequestInFlight;
            if (_transitionStatus != null)
            {
                string? error = _transitionActions.TransitionError;
                _transitionStatus.Text = busy ? "Sending transition request…"
                    : error is { Length: > 0 } ? error : TransitionStatus(vote);
                _transitionStatus.Foreground = error is { Length: > 0 }
                    ? GuiTheme.ErrorBrush : GuiTheme.TextDimBrush;
            }
            if (_transitionDetails != null)
            {
                TextBlock details = _transitionDetails;
                details.Text = active && vote is { } ballot
                    ? $"{Describe(ballot)}\n{ballot.Yes}/{ballot.Eligible} yes · {ballot.No}/{ballot.Eligible} no · need {ballot.Needed}"
                    : vote is { } terminal && terminal.State != MatchTransitionVoteState.Pending
                        ? $"{Describe(terminal)}\n{TransitionStatus(terminal)}"
                        : "Restart or change the map with a lobby-wide vote.";
            }
            if (_restartMatch != null) _restartMatch.IsVisible = !active;
            if (_changeMap != null)
            {
                _changeMap.IsVisible = !active
                    && _transitionActions.AvailableTransitionMaps.Count > 0;
                _changeMap.Subtitle = _transitionActions.AvailableTransitionMaps.Count == 0
                    ? "No other hosted maps are installed locally."
                    : $"{_transitionActions.AvailableTransitionMaps.Count} other hosted map(s).";
            }
            if (_voteYes != null) _voteYes.IsVisible = active;
            if (_voteNo != null) _voteNo.IsVisible = active;
            bool canVote = active && !busy && vote!.OwnVote == null;
            if (_voteYes != null) _voteYes.IsEnabled = canVote;
            if (_voteNo != null) _voteNo.IsEnabled = canVote;
            if (_restartMatch != null) _restartMatch.IsEnabled = !active && !busy;
            if (_changeMap != null) _changeMap.IsEnabled = !active && !busy
                && _transitionActions.AvailableTransitionMaps.Count > 0;
            if (active && vote!.OwnVote is { } own && _transitionDetails is { } ownedDetails)
            {
                string response = own ? "You voted yes." : "You voted no.";
                ownedDetails.Text += "\n" + response;
            }
        }

        private static string Describe(NodeMatchTransitionVoteSnapshot vote)
            => vote.Choice == MatchTransitionChoice.Restart
                ? "Restart match requested."
                : $"Map change requested: {vote.TargetMapKey}.";

        private static string TransitionStatus(NodeMatchTransitionVoteSnapshot? vote)
            => vote?.State switch
            {
                MatchTransitionVoteState.Pending => "A lobby vote is active.",
                MatchTransitionVoteState.Approved => "Transition approved; preparing the next match.",
                MatchTransitionVoteState.Rejected => "Transition vote rejected.",
                MatchTransitionVoteState.Expired => "Transition vote expired.",
                MatchTransitionVoteState.Failed => "Transition failed.",
                _ => "No transition vote is active."
            };

        /// <summary>The panel's own top and bottom padding, plus the scroller's.</summary>
        private const double PanelPadding = 18 + 18 + 12 + 12;

        /// <summary>How tall the panel wants to be, at full size.</summary>
        private readonly double _neededHeight;
        private readonly LayoutTransformControl _scaler;

        /// <summary>
        /// Fit the panel to the height it has been given, down to half size.
        ///
        /// Below that the scroller takes over: text that small is not a menu
        /// either, and a window that short is not one anybody is playing in.
        /// </summary>
        private void FitToHost(double height)
        {
            if (height <= 0 || _neededHeight <= 0)
            {
                return;
            }
            double scale = Math.Clamp(height / _neededHeight, 0.5, 1);
            var current = _scaler.LayoutTransform as ScaleTransform;
            if (current != null && Math.Abs(current.ScaleY - scale) < 0.001)
            {
                return;
            }
            _scaler.LayoutTransform = scale >= 1 ? null : new ScaleTransform(scale, scale);
        }

        /// <summary>
        /// Somebody who just asked for this is looking at a short list and
        /// expects the top entry to be the one already chosen.
        /// </summary>
        public void FocusResume()
        {
            Dispatcher.UIThread.Post(() => _resume.Focus(), DispatcherPriority.Background);
        }

        private static string WindowLabel()
        {
            return WindowMode.IsFullscreen ? "Windowed" : "Fullscreen";
        }

        private static Border BuildHeader()
        {
            var content = new StackPanel { Spacing = 2 };
            content.Children.Add(new TextBlock
            {
                Text = $"SYSTEM OVERLAY // {SessionStatus()}",
                FontFamily = GuiTheme.Display,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = GuiTheme.AccentBrush
            });
            content.Children.Add(new TextBlock
            {
                Text = "START MENU",
                FontFamily = GuiTheme.Display,
                FontSize = 27,
                FontWeight = FontWeight.SemiBold,
                Foreground = GuiTheme.TextBrush
            });
            content.Children.Add(new TextBlock
            {
                Text = "Session controls remain available while the match is active.",
                FontFamily = GuiTheme.Display,
                FontSize = 11,
                Foreground = GuiTheme.TextDimBrush
            });
            return new Border
            {
                Height = 78,
                Background = GuiTheme.PanelLightBrush,
                BorderBrush = GuiTheme.EdgeBrush,
                BorderThickness = new Thickness(1, 1, 1, 0),
                Padding = new Thickness(14, 10),
                Child = content
            };
        }

        private static string SessionStatus()
        {
            if (ReplayPlayback.IsActive)
            {
                return "REPLAY PLAYBACK";
            }
            if (SpectatorMode.IsSpectating)
            {
                return "SPECTATOR SESSION";
            }
            if (AuthoritativePlay.Current != null)
            {
                return "AUTHORITATIVE SESSION";
            }
            return "GAME SESSION";
        }

        private static Border BuildGroup(string title, StackPanel entries)
        {
            int count = entries.Children.Count;
            entries.Children.Insert(0, new Caption(title));
            return new Border
            {
                Height = 18 + 26 + count * 42 + count * entries.Spacing,
                Background = new SolidColorBrush(GuiTheme.Shade(GuiTheme.Panel, -0.12)),
                BorderBrush = GuiTheme.EdgeBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 10),
                Child = entries
            };
        }

        private static Border BuildFooter()
        {
            var hints = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            hints.Children.Add(Hint("[ESC]", " RESUME   "));
            hints.Children.Add(Hint("[ENTER]", " CONFIRM   "));
            hints.Children.Add(Hint("[TAB]", " NAVIGATE"));
            return new Border
            {
                Height = 42,
                BorderBrush = GuiTheme.EdgeBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(8, 7, 8, 0),
                Child = hints
            };
        }

        private static TextBlock Hint(string key, string action)
        {
            var text = new TextBlock
            {
                FontFamily = GuiTheme.Display,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold
            };
            text.Inlines!.Add(new Avalonia.Controls.Documents.Run(key)
            {
                Foreground = GuiTheme.AccentBrush
            });
            text.Inlines.Add(new Avalonia.Controls.Documents.Run(action)
            {
                Foreground = GuiTheme.TextDimBrush
            });
            return text;
        }

        private static MenuEntry Entry(string text, Action action, Color accent,
            bool primary = false)
        {
            var entry = new MenuEntry(text, titleSize: primary ? 17 : 16)
            {
                Accent = accent,
                Primary = primary
            };
            entry.Click += (_, _) => action();
            return entry;
        }

        private static MenuEntry Add(StackPanel stack, string text, Action action)
        {
            MenuEntry entry = Entry(text, action, GuiTheme.Accent);
            stack.Children.Add(entry);
            return entry;
        }
    }
}
