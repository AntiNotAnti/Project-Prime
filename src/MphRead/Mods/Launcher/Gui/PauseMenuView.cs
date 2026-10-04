using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// What Escape shows during a match.
    ///
    /// The same shape as the front screen -- a column of words in the
    /// bottom-left corner over a dim line saying where you are -- because it
    /// is the front screen's job during a match, and a pause menu that looks
    /// like a different program is a pause menu that has to be read rather
    /// than glanced at. What differs is the backdrop: the scrim alone, so the
    /// match shows through. A networked match cannot be paused, and covering
    /// it with a photograph would be a lie about what the program is doing.
    ///
    /// The entries are not fewer than they were. Voting on a map, going
    /// fullscreen, spectating and recording are things you can only want
    /// *during* a match, so this is the one screen they can live on -- the
    /// list is shorter everywhere else precisely so it can be long here.
    ///
    /// A view rather than a window, because nothing shows it in one any
    /// more: the desktop pushes it onto <see cref="InGameMenu"/>'s stack,
    /// which is rendered into the game window itself, and Android pushes it
    /// onto <see cref="StartScreen"/>'s. One menu either way, so an entry
    /// added here turns up on both.
    ///
    /// It decides nothing itself. Every entry raises an event and the host
    /// acts on it: leaving a match is closing a window on one platform and
    /// swapping two views on the other, and neither belongs in a menu.
    /// </summary>
    internal sealed class PauseMenuView : UserControl
    {
        public event EventHandler? Resumed;
        public event EventHandler? SettingsRequested;
        public event EventHandler? LeaveRequested;
        public event EventHandler? ReturnToLobbyRequested;
        public event EventHandler? QuitRequested;
        public event EventHandler? FullscreenRequested;
        public event EventHandler? SpectateRequested;
        public event EventHandler? RejoinRequested;
        public event EventHandler? RecordToggleRequested;
        public event EventHandler? VoteMapRequested;
        public event EventHandler? ReplayControlsRequested;

        private readonly HubNavButton _resume;
        private readonly HubNavButton _voteYes;
        private readonly HubNavButton _voteNo;
        private readonly StackPanel _menu;

        /// <param name="offerWindowMode">
        /// Show the fullscreen/windowed entry. False on a phone, which has one
        /// window, it is already the whole screen, and there is no F11.
        /// </param>
        public PauseMenuView(bool offerWindowMode)
        {
            Background = Brushes.Transparent;
            Focusable = true;

            // Resume is intentionally separated from the long action list. The
            // first question when Escape opens is "go back to the match?", while
            // voting, spectating, recording and system actions are secondary.
            var resumeStack = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _resume = Add(resumeStack, "RESUME",
                () => Resumed?.Invoke(this, EventArgs.Empty),
                HubTheme.Accent, primary: true);
            ControllerNav.Identify(_resume, "pause.resume", initial: true);
            _resume.MinHeight = 48;

            var menu = new StackPanel
            {
                Spacing = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _menu = menu;

            if (DemoPlayback.IsActive)
            {
                Add(menu, "REPLAY STUDIO",
                    () => ReplayControlsRequested?.Invoke(this, EventArgs.Empty),
                    HubTheme.Accent);
            }

            _voteYes = Add(menu, "ACCEPT MAP VOTE", () => AnswerVote(true), HubTheme.Good);
            _voteNo = Add(menu, "DENY MAP VOTE", () => AnswerVote(false), HubTheme.Danger);
            RefreshVote();

            var voteTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            voteTimer.Tick += (_, _) => RefreshVote();
            AttachedToVisualTree += (_, _) => voteTimer.Start();
            DetachedFromVisualTree += (_, _) => voteTimer.Stop();

            if (!DemoPlayback.IsActive && NetSession.Active)
            {
                Add(menu, "VOTE MAP", () => VoteMapRequested?.Invoke(this, EventArgs.Empty));
            }

            if (!DemoPlayback.IsActive)
            {
                if (SpectatorMode.IsSpectating)
                {
                    Add(menu, "JOIN MATCH",
                        () => RejoinRequested?.Invoke(this, EventArgs.Empty));
                }
                else if (SpectatorMode.CanSpectate)
                {
                    Add(menu, "SPECTATE", () => SpectateRequested?.Invoke(this, EventArgs.Empty));
                }
            }

            if (offerWindowMode)
            {
                Add(menu, WindowLabel().ToUpperInvariant(),
                    () => FullscreenRequested?.Invoke(this, EventArgs.Empty));
            }

            if (!DemoPlayback.IsActive && NetSession.Active)
            {
                Add(menu, DemoRecorder.IsRecording ? "STOP REPLAY RECORDING" : "RECORD REPLAY",
                    () => RecordToggleRequested?.Invoke(this, EventArgs.Empty));
            }

            if (!DemoPlayback.IsActive && NetSession.LocalIsLobbyOwner)
            {
                menu.Children.Add(new Expander
                {
                    Header = "MANAGE BOTS",
                    Content = new ScrollViewer
                    {
                        MaxHeight = 220,
                        Content = new BotManagementView()
                    }
                });
            }

            if (!DemoPlayback.IsActive && NetSession.PersistentLobby
                && NetSession.LocalIsLobbyOwner && !NetSession.IsInLobby)
            {
                Add(menu, "RETURN TO LOBBY",
                    () => ReturnToLobbyRequested?.Invoke(this, EventArgs.Empty),
                    HubTheme.Warm);
            }

            Add(menu, "SETTINGS", () => SettingsRequested?.Invoke(this, EventArgs.Empty));
            Add(menu, "LEAVE MATCH", () => LeaveRequested?.Invoke(this, EventArgs.Empty),
                HubTheme.Warm);
            Add(menu, "QUIT", () => QuitRequested?.Invoke(this, EventArgs.Empty),
                HubTheme.Danger);

            foreach (Control child in menu.Children)
                child.HorizontalAlignment = HorizontalAlignment.Stretch;

            bool live = NetSession.Active && !DemoPlayback.IsActive;
            string title = DemoPlayback.IsActive
                ? "REPLAY PAUSED"
                : live ? "LIVE MATCH" : "MATCH PAUSED";
            string state = live
                ? "NETWORK SESSION  //  GAMEPLAY CONTINUES"
                : DemoPlayback.IsActive
                    ? "REPLAY PLAYBACK  //  PAUSED"
                    : "LOCAL SESSION  //  PAUSED";
            string detail = live
                ? "The match continues behind this menu. Resume immediately or manage the current session."
                : DemoPlayback.IsActive
                    ? "Playback is paused while Replay Studio and presentation controls remain available."
                    : "Resume the match or adjust this local session.";

            var context = PrimeChrome.Stack(
                new PrimeBadge("MATCH MENU"),
                PrimeChrome.HeroTitle(title),
                PrimeChrome.Eyebrow(state,
                    live ? HubTheme.WarmBrush : PrimeTheme.HighlightBrush),
                PrimeChrome.Text(detail, PrimeTypography.Body,
                    PrimeTheme.TextSecondaryBrush),
                new Border { MinHeight = 8 },
                resumeStack,
                PrimeChrome.Text("ESC / START  //  RESUME",
                    PrimeTypography.DataSmall, PrimeTheme.TextSecondaryBrush, data: true));

            var actions = PrimeChrome.Stack(
                PrimeChrome.Eyebrow("SESSION & SYSTEM"),
                PrimeChrome.Title("MATCH ACTIONS"),
                PrimeChrome.Text(
                    "Session tools stay here so Resume remains a single, obvious first action.",
                    PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush),
                new ScrollViewer
                {
                    Content = menu,
                    MaxHeight = 520,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                });

            Control body;
            if (Deck.Phone)
            {
                var phone = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,*"),
                    RowSpacing = 10
                };
                phone.Children.Add(new PrimePanel(context, raised: true));
                var actionPanel = new PrimePanel(actions);
                Grid.SetRow(actionPanel, 1);
                phone.Children.Add(actionPanel);
                body = phone;
            }
            else
            {
                body = PrimeChrome.Columns("0.9*,1.1*",
                    new PrimePanel(context, raised: true),
                    new PrimePanel(actions));
            }

            _scaler = new LayoutTransformControl
            {
                Child = body,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };

            Content = UiLayout.Page(overGame: true, UiLayout.WellPlay, "",
                strip: null, body: _scaler, centreBody: true);
            SizeChanged += (_, e) => FitToHost(e.NewSize.Height);
        }

        private readonly LayoutTransformControl _scaler;

        /// <summary>
        /// What the column needs at full size: eight words, their spacing, and
        /// the corner it is anchored in.
        /// </summary>
        private double NeededHeight
        {
            get
            {
                int count = 1; // Resume lives in the context panel.
                foreach (Control child in _menu.Children)
                {
                    if (child.IsVisible) count++;
                }
                // Desktop uses two columns, so only the denser action side drives
                // height. Phone stacks both zones and gets more vertical budget.
                double actions = count * 42 + Math.Max(0, count - 1) * 5;
                return (Deck.Phone ? actions + 250 : Math.Max(actions + 120, 360))
                    + UiLayout.WellTop + UiLayout.WellBottom;
            }
        }

        internal void RefreshVote()
        {
            bool visible = MapVote.Active && !MapVote.Answered && !DemoPlayback.IsActive;
            if (_voteYes.IsVisible == visible && _voteNo.IsVisible == visible) return;
            bool refocus = !visible && (_voteYes.IsFocused || _voteNo.IsFocused);
            _voteYes.IsVisible = _voteNo.IsVisible = visible;
            if (refocus) _resume.Focus();
            FitToHost(Bounds.Height);
        }

        private void AnswerVote(bool yes)
        {
            MapVote.Cast(yes);
            RefreshVote();
            Resumed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Fit the column to the height it has been given, down to half size.
        /// Below that there is no menu either way, and a window that short is
        /// not one anybody is playing in.
        /// </summary>
        private void FitToHost(double height)
        {
            if (height <= 0)
            {
                return;
            }
            double scale = Math.Clamp(height / NeededHeight, 0.5, 1);
            if (_scaler.LayoutTransform is ScaleTransform current
                && Math.Abs(current.ScaleY - scale) < 0.001)
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

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Resumed?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        private static string WindowLabel()
        {
            return WindowMode.IsFullscreen ? "Windowed" : "Fullscreen";
        }

        /// <summary>
        /// One entry. Still titles only -- what changed is that an entry is
        /// now an object you press rather than a word that brightens, which
        /// is what lets a menu over a running match read as a menu rather
        /// than as text that happens to be on top of the game.
        /// </summary>
        private static HubNavButton Add(StackPanel menu, string text, Action action,
            Color? accent = null, bool primary = false)
        {
            var entry = new HubNavButton(text, primary: primary, compact: true,
                accent: accent)
            {
                MinHeight = 40,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            entry.Click += (_, _) => action();
            menu.Children.Add(entry);
            return entry;
        }
    }
}
