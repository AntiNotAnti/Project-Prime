using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Content;
using MphRead.Mods.Render;
using MphRead.Mods.Update;
using MphRead.Runtime.Content;
using FrameTiming = MphRead.Mods.Render.FrameTiming;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>
    /// Settings, in the same language as the front screen: a rail of sections,
    /// one page at a time beside it, everything painted here.
    ///
    /// The same view opens from the front screen, from the pause menu inside a
    /// match, and from the Android head -- which is why nothing here needs a
    /// restart to take effect except the window mode, and why it is a
    /// <see cref="UserControl"/> rather than a <see cref="Window"/>: a phone has
    /// no second window to open it in. <c>SettingsWindow</c> is the frame
    /// the desktop puts around it.
    ///
    /// Below <see cref="_narrowWidth"/> the rail turns into a strip across the
    /// top and the footer drops to the bottom. The sections, the rows and every
    /// value they read and write are the same objects either way.
    /// </summary>
    internal sealed class SettingsView : UserControl
    {
        private readonly MenuSettings _settings;
        private readonly bool _inGame;
        private readonly StackPanel _rail = new() { Spacing = 2 };

        /// <summary>
        /// The same section buttons, wrapped over as many lines as they need.
        ///
        /// A narrow screen used to put them in a row inside a horizontal
        /// scroller, and the row could not be scrolled with a finger: every
        /// button in it takes the pointer for itself, so the drag never
        /// reached the scroller and the sections past the fourth were simply
        /// unreachable on a phone. Wrapping needs no gesture at all.
        /// </summary>
        private readonly WrapPanel _railWrap = new();
        private readonly Panel _pages = new();
        private readonly List<(MenuEntry Button, Control Page)> _sections = new();
        private readonly Grid _grid = new();
        private readonly Border _railPanel;
        private Control? _heading;
        private readonly Border _footerPanel;
        private readonly Grid _footerGrid = new();
        private Control _footerContext = null!;
        private Control _footerActions = null!;
        private readonly ScrollViewer _railScroll;
        private bool _narrow;
        private bool _laidOut;

        /// <summary>Below this width the rail goes across the top.</summary>
        private const double _narrowWidth = 720;

        /// <summary>Raised when this view is finished with, saved or not.</summary>
        public event EventHandler? Closed;

        /// <summary>
        /// The player asked for the game-files screen, which lives on the
        /// front screen because extracting a ROM is a thing you do before
        /// there is anything to configure. Raised, not acted on: this view
        /// does not know what is behind it.
        /// </summary>
        public event EventHandler? GameFilesRequested;

        private ChoiceRow? _windowRow;
        private SliderRow _resolutionScale = null!;
        private ToggleRow _lightingRow = null!;
        private ToggleRow _fogRow = null!;
        private ChoiceRow _graphicsPresetRow = null!;
        private ChoiceRow _textureFilteringPresetRow = null!;
        private ChoiceRow _anisotropyRow = null!;
        private ChoiceRow _msaaRow = null!;
        private ToggleRow _bloomRow = null!;
        private ToggleRow _dynamicVisualLightsRow = null!;
        private ToggleRow _celRow = null!;
        private ToggleRow _fpsRow = null!;
        private ToggleRow _advancedNetworkRow = null!;
        private StackPanel _networkPage = null!;
        private ChoiceRow _hitMarkerRow = null!, _radarStyleRow = null!, _radarOrientationRow = null!;
        private ChoiceRow _radarPositionRow = null!, _radarSizeRow = null!;
        private ToggleRow _headshotCueRow = null!, _killConfirmationRow = null!;

        /// <summary>
        /// The stops the FPS limit slides over, and the cap each one means.
        ///
        /// Stops rather than a free number, because a slider dragged across a
        /// free range lands on 143 and 167 as easily as on 144 and 165, and a
        /// limit that is one frame under the monitor's rate is the one number
        /// nobody wants. Every common refresh rate is here up to 240.
        ///
        /// "Display" is VSync at the monitor's own rate and is the default: it
        /// is the only tear-free setting, and on a 144 Hz screen it *is* 144.
        /// An explicit number turns VSync off, because asking for 120 on a
        /// 144 Hz screen with VSync on gets 72.
        ///
        /// None of them move the simulation, which runs at 60 Hz on every
        /// setting -- see Mods/Render/FrameTiming.cs.
        /// </summary>
        private static readonly (string Label, int Cap)[] _fpsLimitStops = new[]
        {
            ("Display (VSync)", FrameTiming.DisplayRate),
            ("30 fps", 30),
            ("60 fps", 60),
            ("75 fps", 75),
            ("90 fps", 90),
            ("100 fps", 100),
            ("120 fps", 120),
            ("144 fps", 144),
            ("165 fps", 165),
            ("180 fps", 180),
            ("200 fps", 200),
            ("240 fps", 240),
            ("Unlimited", FrameTiming.MaxCap)
        };

        private static int FpsLimitStopIndex(int cap)
        {
            int index = Array.FindIndex(_fpsLimitStops, stop => stop.Cap == cap);
            if (index >= 0)
            {
                return index;
            }
            // A settings.json written by hand, or by a build with a different
            // table: land on the nearest stop that does not exceed what was
            // asked for, rather than silently jumping to the default.
            int best = 0;
            for (int i = 1; i < _fpsLimitStops.Length; i++)
            {
                if (_fpsLimitStops[i].Cap <= cap)
                {
                    best = i;
                }
            }
            return best;
        }
        private SliderRow _fpsLimitRow = null!;
        private ToggleRow _proHud = null!;
        private ChoiceRow _proHudWeaponRow = null!;
        private ChoiceRow _hitMarkerTimingRow = null!;
        private ChoiceRow _crosshairSizeRow = null!;
        private ChoiceRow _crosshairStyleRow = null!;
        private SliderRow _sfxVolume = null!;
        private SliderRow _feedbackVolume = null!;
        private SliderRow _musicVolume = null!;
        private ChoiceRow _announcerPackRow = null!, _musicPackRow = null!;
        private IReadOnlyList<InstalledOptionalPresentationPack> _announcerPacks
            = Array.Empty<InstalledOptionalPresentationPack>();
        private IReadOnlyList<InstalledOptionalPresentationPack> _musicPacks
            = Array.Empty<InstalledOptionalPresentationPack>();
        private ChoiceRow _languageRow = null!;
        private SliderRow _sensitivity = null!;
        private ToggleRow _invertY = null!;
        private ToggleRow _invertX = null!;
        private ToggleRow _scrollAllWeapons = null!;
        private ChoiceRow _gamepadPresetRow = null!;
        private SliderRow _gamepadHorizontalSensitivity = null!;
        private SliderRow _gamepadVerticalSensitivity = null!;
        private ToggleRow _gamepadAimAssist = null!;
        private SliderRow _gamepadAimAssistStrength = null!;
        private SliderRow _gamepadLook = null!;
        private SliderRow _gamepadDeadZone = null!;
        private SliderRow _gamepadLookDeadZone = null!;
        private SliderRow _gamepadOuterDeadZone = null!;
        private ToggleRow _gamepadOuterBoost = null!;
        private SliderRow _gamepadTriggerPress = null!;
        private SliderRow _gamepadTriggerRelease = null!;
        private SliderRow _gamepadZoomMultiplier = null!;
        private ToggleRow _gamepadInvertY = null!;
        private ToggleRow _gamepadGyro = null!, _gamepadGyroInvertX = null!;
        private ToggleRow _gamepadGyroInvertY = null!, _gamepadHaptics = null!;
        private ToggleRow _inputBalanceTelemetry = null!;
        private SliderRow _gamepadGyroSensitivity = null!;
        private ToggleRow? _stylusAiming, _stylusInvertY, _stylusClassicGestures,
            _stylusDoubleTapJump, _stylusFlickBoost, _stylusPressureToFire;
        private SliderRow? _stylusSensitivity, _stylusPressureThreshold;
        private ChoiceRow? _stylusPrimary, _stylusSecondary;
        private FieldRow _pointGoal = null!;
        private FieldRow _timeLimit = null!;
        private ChoiceRow _damageRow = null!;
        private ToggleRow _teamPlay = null!;
        private ToggleRow _friendlyFire = null!;
        private ToggleRow _radar = null!;
        private ToggleRow _affinity = null!;
        private FieldRow _playerName = null!;
        private ChoiceRow _hunterRow = null!;
        private ChoiceRow _autoUpdate = null!;
        private ToggleRow _reducedMotion = null!;
        private Note _saveError = null!;

        private const double _railWidth = 244;

        /// <summary>True when the user pressed save rather than closing.</summary>
        public bool Saved { get; private set; }

        /// <summary>What a frame around this should be titled.</summary>
        public string WindowTitle => $"{Mods.Branding.Name} settings";

        /// <summary>True when this was opened over a match rather than the launcher.</summary>
        public bool InGame => _inGame;

        private readonly Scene? _scene;

        public SettingsView(MenuSettings settings, bool inGame = false, Scene? scene = null)
        {
            _scene = scene;
            _settings = settings;
            _inGame = inGame;

            Background = GuiTheme.InkBrush;
            Focusable = true;

            _railScroll = new ScrollViewer
            {
                Content = _rail,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Control footer = BuildFooter();
            _railPanel = new Border
            {
                Background = GuiTheme.PanelBrush,
                BorderBrush = GuiTheme.EdgeBrush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = _railScroll
            };
            _footerPanel = new Border
            {
                Background = GuiTheme.PanelBrush,
                BorderBrush = GuiTheme.EdgeBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = footer
            };
            // A grid rather than a docked panel so that the sections come
            // before the footer in the tab order: a DockPanel fills with its
            // *last* child, which would have put Save and Cancel first and made
            // the first Tab in the window a press away from closing it.
            _grid.Children.Add(_railPanel);
            _grid.Children.Add(_pages);
            _grid.Children.Add(_footerPanel);
            ApplyLayout(narrow: false);
            Content = _grid;
            SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width < _narrowWidth);

            _heading = BuildRailHeading();
            _rail.Children.Add(_heading);
            BuildPages();
            // The sections did not exist when the first layout ran, so the one
            // that is wanted is chosen again now that they do.
            _laidOut = false;
            ApplyLayout(_narrow);
            ShowPage(_sections[0].Page);
        }

        /// <summary>
        /// A rail beside the pages, or a strip of sections above them.
        ///
        /// One tree moved between cells rather than two trees: a section added
        /// to <see cref="BuildPages"/> appears on a phone without anybody
        /// having to remember it twice.
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
                _grid.ColumnDefinitions = new ColumnDefinitions("*");
                _grid.RowDefinitions = new RowDefinitions("Auto,*,Auto");
                MoveSections(_railWrap);
                _railScroll.Content = _railWrap;
                _railScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                _railScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                _railPanel.Width = Double.NaN;
                _railPanel.Padding = new Thickness(12, 8, 12, 6);
                _railPanel.BorderThickness = new Thickness(0, 0, 0, 1);
                _footerPanel.Width = Double.NaN;
                _footerPanel.Padding = new Thickness(12, 6, 12, 10);
                _footerGrid.ColumnDefinitions = new ColumnDefinitions("*");
                _footerGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
                Place(_footerContext, 0, 0, rowSpan: 1);
                Place(_footerActions, 1, 0, rowSpan: 1);
                Place(_railPanel, 0, 0, rowSpan: 1);
                Place(_pages, 1, 0, rowSpan: 1);
                Place(_footerPanel, 2, 0, rowSpan: 1);
                return;
            }
            _grid.ColumnDefinitions = new ColumnDefinitions("Auto,*");
            _grid.RowDefinitions = new RowDefinitions("*,Auto");
            MoveSections(_rail);
            _railScroll.Content = _rail;
            _railScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _railScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _railPanel.Width = _railWidth;
            _railPanel.Padding = new Thickness(18, 20, 14, 4);
            _railPanel.BorderThickness = new Thickness(0, 0, 1, 0);
            _footerPanel.Width = Double.NaN;
            _footerPanel.Padding = new Thickness(26, 10, 26, 12);
            _footerGrid.ColumnDefinitions = new ColumnDefinitions("*,Auto");
            _footerGrid.RowDefinitions = new RowDefinitions("Auto");
            Place(_footerContext, 0, 0, rowSpan: 1);
            Place(_footerActions, 0, 1, rowSpan: 1);
            Place(_railPanel, 0, 0, rowSpan: 2);
            Place(_pages, 0, 1, rowSpan: 1);
            Place(_footerPanel, 1, 1, rowSpan: 1);
        }

        /// <summary>
        /// Put the section buttons in one panel or the other.
        ///
        /// One set of buttons moved between two panels rather than two sets
        /// kept in step: a section added to <see cref="BuildPages"/> turns up
        /// in both shapes without anybody having to remember it twice, which
        /// is the same reason the pages themselves are shared.
        ///
        /// The heading goes with them only down the column. Across the top it
        /// would be a fifth thing on the first line that is not a section.
        /// </summary>
        private void MoveSections(Panel target)
        {
            if (_sections.Count == 0 || ReferenceEquals(_sections[0].Button.Parent, target))
            {
                return;
            }
            _rail.Children.Clear();
            _railWrap.Children.Clear();
            if (ReferenceEquals(target, _rail) && _heading != null)
            {
                _rail.Children.Add(_heading);
            }
            foreach ((MenuEntry button, Control _) in _sections)
            {
                target.Children.Add(button);
            }
        }

        private static void Place(Control control, int row, int column, int rowSpan)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            Grid.SetRowSpan(control, rowSpan);
        }

        /// <summary>
        /// Put the keyboard on the first section as the view appears.
        ///
        /// Without it the window opens with nothing focused, and the first Tab
        /// goes to whatever the tree happens to offer first rather than to the
        /// rail -- which is the difference between a window that can be driven
        /// from the keyboard and one that can be driven from the keyboard once
        /// you have found out how.
        /// </summary>
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Dispatcher.UIThread.Post(() => _sections[0].Button.Focus(),
                DispatcherPriority.Background);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        private void Close() => Closed?.Invoke(this, EventArgs.Empty);

        // ----------------------------------------------------------- structure

        /// <summary>
        /// A settings page remembers the sector currently being populated.
        /// Existing builders can keep their simple Heading/Add rhythm while
        /// every heading starts a real tactical panel instead of another line
        /// in one long, visually flat list.
        /// </summary>
        private sealed class SettingsPage : StackPanel
        {
            public StackPanel? ActiveSector { get; set; }
        }

        private Control BuildRailHeading()
        {
            var title = new TextBlock
            {
                Text = "PARAMETER SECTORS",
                FontFamily = GuiTheme.Display,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = GuiTheme.TextBrush
            };
            var context = new TextBlock
            {
                Text = _inGame ? "ACTIVE MATCH CONFIG" : "LOCAL PROFILE CONFIG",
                FontFamily = GuiTheme.Display,
                FontSize = 9,
                Foreground = GuiTheme.AccentBrush,
                Margin = new Thickness(0, 2, 0, 10)
            };
            var stack = new StackPanel();
            stack.Children.Add(title);
            stack.Children.Add(context);
            return stack;
        }

        private StackPanel AddSection(string name)
        {
            var page = new SettingsPage { Spacing = 10 };
            // The inset is the page's margin rather than the scroll viewer's
            // padding: padding is not taken off the width the content is
            // measured with, so every wrapped note ran off the right edge of
            // the window by exactly that much.
            page.Margin = new Thickness(26, 18, 26, 20);
            page.Children.Add(BuildPageHeader(name));
            var scroll = new ScrollViewer
            {
                Content = page,
                IsVisible = false,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var button = new MenuEntry(name, SectionSubtitle(name), titleSize: 13)
            {
                Height = 48,
                Margin = new Thickness(0, 0, 0, 3)
            };
            button.Click += (_, _) => ShowPage(scroll);
            _rail.Children.Add(button);
            _pages.Children.Add(scroll);
            _sections.Add((button, scroll));
            return page;
        }

        private Control BuildPageHeader(string name)
        {
            var marker = new TextBlock
            {
                Text = _inGame
                    ? "SYS CONFIG MATRIX // ACTIVE_SESSION"
                    : "SYS CONFIG MATRIX // LOCAL_RUNTIME",
                FontFamily = GuiTheme.Display,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = GuiTheme.AccentBrush
            };
            var title = new TextBlock
            {
                Text = SectionTitle(name),
                FontFamily = GuiTheme.Display,
                FontSize = 22,
                FontWeight = FontWeight.SemiBold,
                Foreground = GuiTheme.TextBrush,
                Margin = new Thickness(0, 4, 0, 2)
            };
            var detail = new TextBlock
            {
                Text = SectionDescription(name),
                FontFamily = GuiTheme.Display,
                FontSize = 12,
                Foreground = GuiTheme.TextDimBrush,
                TextWrapping = TextWrapping.Wrap
            };
            var stack = new StackPanel();
            stack.Children.Add(marker);
            stack.Children.Add(title);
            stack.Children.Add(detail);
            return new Border
            {
                BorderBrush = GuiTheme.EdgeBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(2, 0, 2, 12),
                Margin = new Thickness(0, 0, 0, 2),
                Child = stack
            };
        }

        private static string SectionSubtitle(string name) => name switch
        {
            "Gameplay" => "MATCH RULES / PROFILE",
            "Controls" => "MOUSE / PAD / KEYS",
            "Graphics" => "DISPLAY / QUALITY / HUD",
            "Audio" => "MIX / PACKS / LANGUAGE",
            "Network" => "DIAGNOSTICS / BACKEND",
            "Accessibility" => "MOTION OPTIONS",
            "About" => "CREDITS / SUPPORT",
            _ => "CONFIGURATION"
        };

        private static string SectionTitle(string name) => name switch
        {
            "Gameplay" => "GAMEPLAY & PILOT PROFILE",
            "Controls" => "CONTROL INPUT MATRIX",
            "Graphics" => "DISPLAY & GRAPHICS",
            "Audio" => "AUDIO & PRESENTATION",
            "Network" => "NETWORK DIAGNOSTICS",
            "Accessibility" => "ACCESSIBILITY & MOTION",
            "About" => "PROJECT PRIME SYSTEM",
            _ => name.ToUpperInvariant()
        };

        private static string SectionDescription(string name) => name switch
        {
            "Gameplay" => "Configure local match rules, pilot identity and preferred hunter.",
            "Controls" => "Tune mouse, gamepad, touch and direct action bindings.",
            "Graphics" => "Set the display path, render budget, visual quality and combat HUD.",
            "Audio" => "Balance the mix and select installed presentation content.",
            "Network" => "Control the diagnostics shown while connected to a Node.",
            "Accessibility" => "Reduce optional interface motion without changing gameplay animation.",
            "About" => "Project attribution, upstream foundations and support information.",
            _ => "Configure Project Prime."
        };

        /// <summary>
        /// Open on a named section rather than the first one.
        ///
        /// For <c>-uishot</c>, which is the only way any of these pages can be
        /// looked at from a machine with no display -- and which could
        /// otherwise photograph nothing but Display, every other page being
        /// behind a click.
        /// </summary>
        internal void ShowSection(string name)
        {
            foreach ((MenuEntry button, Control page) in _sections)
            {
                if (String.Equals(button.Title, name, StringComparison.OrdinalIgnoreCase))
                {
                    ShowPage(page);
                    return;
                }
            }
        }

        /// <summary>Adds shell-owned Backend configuration to Network without
        /// transferring its command or persistence ownership to SettingsView.</summary>
        internal void AddNetworkAdvanced(Control control)
        {
            Heading(_networkPage, "Backend service");
            Add(_networkPage, control);
        }

        private void ShowPage(Control page)
        {
            foreach ((MenuEntry button, Control candidate) in _sections)
            {
                candidate.IsVisible = ReferenceEquals(candidate, page);
                button.Selected = ReferenceEquals(candidate, page);
            }
        }

        private static Caption Heading(StackPanel page, string text)
        {
            StackPanel target = page;
            if (page is SettingsPage settingsPage)
            {
                target = new StackPanel { Spacing = 2 };
                var panel = new Border
                {
                    Background = GuiTheme.PanelBrush,
                    BorderBrush = GuiTheme.EdgeBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(14, 10, 14, 12),
                    Child = target
                };
                settingsPage.Children.Add(panel);
                settingsPage.ActiveSector = target;
            }
            var caption = new Caption(text) { Height = 28, Margin = new Thickness(0, 0, 0, 4) };
            target.Children.Add(caption);
            return caption;
        }

        private static StackPanel ActiveSector(StackPanel page)
        {
            return page is SettingsPage settingsPage && settingsPage.ActiveSector != null
                ? settingsPage.ActiveSector
                : page;
        }

        private static Note Explain(StackPanel page, string text, Color? color = null)
        {
            var note = new Note(text, color);
            ActiveSector(page).Children.Add(note);
            return note;
        }

        private static T Add<T>(StackPanel page, T control) where T : Control
        {
            ActiveSector(page).Children.Add(control);
            return control;
        }

        private void BuildPages()
        {
            BuildMatch();
            BuildControls();
            BuildDisplay();
            BuildAudio();
            BuildNetwork();
            BuildAccessibility();
            BuildCredits();
        }

        /// <summary>
        /// Who this is built on, in full.
        ///
        /// It used to be four dim lines in the corner of the front screen,
        /// where it was the first thing the eye landed on and the last thing
        /// anybody needed while choosing a match. Here it is out of the way
        /// and, being a page rather than a corner, it can say what each person
        /// actually did.
        /// </summary>
        private void BuildCredits()
        {
            StackPanel page = AddSection("About");
            Heading(page, "About Project Prime");
            Explain(page, Mods.Credits.Summary);
            Add(page, new Caption(Mods.Credits.Author));
            Add(page, new Note(Mods.Credits.ForkWork));
            // The address is put in the row itself when there is no browser to
            // hand it to -- a headless session, or a handler that refused --
            // so the button says something either way rather than appearing to
            // do nothing. Same fallback the update badge uses.
            var support = new MenuEntry("\u2615 Support this project", titleSize: 15);
            support.Click += (_, _) =>
            {
                if (!Mods.Update.Updater.OpenLink(Mods.Credits.SupportUrl))
                {
                    support.Subtitle = Mods.Credits.SupportUrl;
                }
            };
            Add(page, support);
            Heading(page, "Built on");
            foreach (Mods.Credits.Entry entry in Mods.Credits.Entries)
            {
                Add(page, new Caption(entry.Who));
                string what = entry.What;
                if (entry.Where.Length > 0)
                {
                    what += "\n" + entry.Where;
                }
                Add(page, new Note(what));
            }
        }

        // ------------------------------------------------------------- display

        private void BuildDisplay()
        {
            StackPanel page = AddSection("Graphics");
            // A phone has one window, it is already the whole screen, and it
            // has no F11. Everything in this group is about a desktop window.
            if (!OperatingSystem.IsAndroid())
            {
                Heading(page, "Window");
                _windowRow = Add(page, new ChoiceRow("Mode",
                    new[] { "Windowed", "Fullscreen (borderless)" },
                    LauncherPrefs.WindowMode == WindowStartMode.BorderlessFullscreen ? 1 : 0));
            }

            Heading(page, "Performance");
            _resolutionScale = Add(page, new SliderRow("Render scale",
                RenderOptions.ResolutionScale,
                v => $"{Math.Max(RenderOptions.MinScale, v)}%"));
            // Under the render scale because they are the same question asked
            // from both ends -- how much picture, and how often -- and because
            // the two of them are what somebody who is not getting a smooth
            // game comes to this page to change.
            _fpsLimitRow = Add(page, new SliderRow("FPS limit",
                FpsLimitStopIndex(FrameTiming.FrameRateCap),
                v => _fpsLimitStops[Math.Clamp(v, 0, _fpsLimitStops.Length - 1)].Label,
                min: 0, max: _fpsLimitStops.Length - 1, keyStep: 1));
            Heading(page, "Quality");
            _graphicsPresetRow = Add(page, new ChoiceRow("Graphics quality",
                RenderOptions.GraphicsPresetLabels, (int)RenderOptions.GraphicsPreset));
            _textureFilteringPresetRow = Add(page, new ChoiceRow("Texture filtering",
                RenderOptions.TextureFilteringLabels, (int)RenderOptions.TextureFilteringPreset));
            _anisotropyRow = Add(page, new ChoiceRow("Texture detail",
                RenderOptions.AnisotropyLabels, RenderOptions.AnisotropyIndex(RenderOptions.Anisotropy)));
            _msaaRow = Add(page, new ChoiceRow("Edge smoothing",
                RenderOptions.MsaaLabels, RenderOptions.MsaaIndex(RenderOptions.Msaa)));
            _bloomRow = Add(page, new ToggleRow("Bloom", RenderOptions.Bloom));
            _dynamicVisualLightsRow = Add(page,
                new ToggleRow("Dynamic lighting", RenderOptions.DynamicVisualLights));
            _graphicsPresetRow.Changed += (_, _) => ApplyGraphicsPresetToRows();
            _lightingRow = Add(page, new ToggleRow("Lighting", RenderOptions.Lighting));
            _fogRow = Add(page, new ToggleRow("Fog", RenderOptions.Fog));
            _fpsRow = Add(page, new ToggleRow("FPS counter", RenderOptions.ShowFps));

            Heading(page, "Cel shading");
            _celRow = Add(page, new ToggleRow("Cel shading", RenderOptions.CelShading));

            // One switch, and none of what it drives.
            //
            // Pro mode is the whole HUD decision now: helmet and visor,
            // crosshair, weapon list and its size, and where energy, ammo and
            // the score are drawn. Off is the game as the DS drew it; on is
            // the competitive layout. The six settings underneath were six
            // ways to end up somewhere between the two, and a player who has
            // to answer six questions to get one look has been handed the
            // design problem. They keep working -- Features still holds them,
            // -nohelmet still sets two of them -- they simply are not asked
            // about here.
            Heading(page, "HUD");
            _proHud = Add(page, new ToggleRow("Pro mode HUD", Features.ProHud));
            _proHudWeaponRow = Add(page, new ChoiceRow("Weapon", new[] { "Static", "Dynamic" },
                Features.ProHudFixedWeapon ? 0 : 1));
            _hitMarkerRow = Add(page, new ChoiceRow("Hit markers", new[] { "Off", "Visual", "Visual + audio" }, (int)Combat.CombatFeedbackSettings.HitMarkers));
            _hitMarkerTimingRow = Add(page, new ChoiceRow("Hit marker timing",
                new[] { "Confirmed", "Instant" }, (int)Combat.CombatFeedbackSettings.Timing));
            _headshotCueRow = Add(page, new ToggleRow("Headshot cue", Combat.CombatFeedbackSettings.HeadshotCue));
            _killConfirmationRow = Add(page, new ToggleRow("Kill confirmation", Combat.CombatFeedbackSettings.KillConfirmation));
            _radarStyleRow = Add(page, new ChoiceRow("Radar", new[] { "Classic", "Minimap" }, (int)global::MphRead.Hud.Radar.RadarSettings.Style));
            _radarOrientationRow = Add(page, new ChoiceRow("Radar orientation", new[] { "Heading", "North" }, (int)global::MphRead.Hud.Radar.RadarSettings.Orientation));
            _radarPositionRow = Add(page, new ChoiceRow("Radar position",
                new[] { "Top Right", "Top Left", "Bottom Right", "Bottom Left" },
                Math.Clamp((int)global::MphRead.Hud.Radar.RadarSettings.Anchor, 0, 3)));
            int radarSize = global::MphRead.Hud.Radar.RadarSettings.Scale < .9f ? 0
                : global::MphRead.Hud.Radar.RadarSettings.Scale > 1.1f ? 2 : 1;
            _radarSizeRow = Add(page, new ChoiceRow("Radar size", new[] { "Small", "Medium", "Large" }, radarSize));
            // The crosshair questions belong to Pro mode and nothing else --
            // the DS HUD draws its own reticle sprite and has no use for
            // them -- so they are only asked while it is on. Shown rather than
            // greyed: a row that cannot be answered is still a row to read
            // past, and this page is long enough.
            _crosshairSizeRow = Add(page, new ChoiceRow("Crosshair size",
                Crosshair.SizeNames, (int)Crosshair.Size));
            _crosshairStyleRow = Add(page, new ChoiceRow("Crosshair type",
                Crosshair.StyleNames, (int)Crosshair.Style));
            _crosshairStyleRow.Preview = (context, area) => CrosshairPreview.Draw(context, area,
                (CrosshairStyle)_crosshairStyleRow.Index, (CrosshairSize)_crosshairSizeRow.Index);
            // The preview lives on the type row and answers both rows, so the
            // size row has to ask for it to be repainted.
            _crosshairSizeRow.Changed += (_, _) => _crosshairStyleRow.InvalidateVisual();
            _proHud.Changed += (_, _) => ShowProHudRows();
            ShowProHudRows();
        }

        private void ApplyGraphicsPresetToRows()
        {
            GraphicsPreset preset = (GraphicsPreset)_graphicsPresetRow.Index;
            _textureFilteringPresetRow.Index = (int)RenderOptions.TextureFilteringFor(preset);
            _anisotropyRow.Index = RenderOptions.AnisotropyIndex(RenderOptions.AnisotropyFor(preset));
            _msaaRow.Index = RenderOptions.MsaaIndex(RenderOptions.MsaaFor(preset));
            _bloomRow.On = RenderOptions.BloomFor(preset);
            _dynamicVisualLightsRow.On = RenderOptions.DynamicVisualLightsFor(preset);
        }

        private void ShowProHudRows()
        {
            bool visible = Features.ShowProHudWeaponSetting(_proHud.On);
            _proHudWeaponRow.IsVisible = visible;
            _crosshairSizeRow.IsVisible = visible;
            _crosshairStyleRow.IsVisible = visible;
        }

        // --------------------------------------------------------------- audio

        private void BuildAudio()
        {
            StackPanel page = AddSection("Audio");
            Heading(page, "Volume");
            _feedbackVolume = Add(page, new SliderRow("Combat feedback", (int)(Combat.FeedbackAudio.Volume * 100), v => $"{v}%"));
            _sfxVolume = Add(page, new SliderRow("Sound effects",
                Percent(_settings.SfxVolume, 35)));
            _musicVolume = Add(page, new SliderRow("Music", Percent(_settings.MusicVolume, 50)));
            Heading(page, "Presentation packs");
            ClientPresentationContentState content = ClientPresentationContent.Refresh();
            _announcerPacks = ClientPresentationContent.Packs(content, OptionalPresentationKind.Announcer);
            _musicPacks = ClientPresentationContent.Packs(content, OptionalPresentationKind.Music);
            _announcerPackRow = Add(page, new ChoiceRow("Announcer",
                PackLabels(_announcerPacks), SelectedPackIndex(_announcerPacks, LauncherPrefs.AnnouncerPack)));
            _musicPackRow = Add(page, new ChoiceRow("Music pack",
                PackLabels(_musicPacks), SelectedPackIndex(_musicPacks, LauncherPrefs.MusicPack)));
            Explain(page, $"Data-only packs are discovered in {LauncherPrefs.OptionalContentDirectory}. "
                + "Missing or invalid selections use built-in presentation; changes apply to the next match.");
            Heading(page, "Language");
            string[] languages = Enum.GetNames<Language>();
            _languageRow = Add(page, new ChoiceRow("Text", languages,
                Math.Max(0, Array.IndexOf(languages, _settings.Language))));
        }

        private static int Percent(string stored, int fallback)
        {
            return Single.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture,
                out float parsed)
                ? Math.Clamp((int)Math.Round(parsed * 100), 0, 100)
                : fallback;
        }

        private static string[] PackLabels(IReadOnlyList<InstalledOptionalPresentationPack> packs)
        {
            var labels = new string[packs.Count + 1];
            labels[0] = "Built-in";
            for (int i = 0; i < packs.Count; i++)
            {
                ContentPackIdentity identity = packs[i].Identity;
                labels[i + 1] = $"{identity.StableId} {identity.Version} ({identity.ContentHash[..8]})";
            }
            return labels;
        }

        private static int SelectedPackIndex(IReadOnlyList<InstalledOptionalPresentationPack> packs,
            ContentPackIdentity? selected)
        {
            if (!selected.HasValue) return 0;
            for (int i = 0; i < packs.Count; i++)
            {
                ContentPackIdentity identity = packs[i].Identity;
                if (String.Equals(identity.StableId, selected.Value.StableId, StringComparison.Ordinal)
                    && String.Equals(identity.Version, selected.Value.Version, StringComparison.Ordinal)
                    && String.Equals(identity.ContentHash, selected.Value.ContentHash,
                        StringComparison.OrdinalIgnoreCase)) return i + 1;
            }
            return 0;
        }

        // ------------------------------------------------------------ controls

        private void BuildControls()
        {
            StackPanel page = AddSection("Controls");
            Heading(page, "Mouse");
            _sensitivity = Add(page, new SliderRow("Sensitivity",
                SensitivityToSlider(InputSettings.MouseSensitivity),
                v => $"{SliderToSensitivity(v).ToString("0.00", CultureInfo.InvariantCulture)}x"));
            _invertY = Add(page, new ToggleRow("Invert vertical aim", InputSettings.InvertMouseY));
            _invertX = Add(page, new ToggleRow("Invert horizontal aim", InputSettings.InvertMouseX));
            _scrollAllWeapons = Add(page, new ToggleRow("Wheel cycles every weapon",
                InputSettings.ScrollAllWeapons));

            BuildTouchControls(page);
            BuildStylusControls(page);

            // Its own section rather than more rows under "Mouse": a pad has
            // its own feel, and somebody who inverts one of the two very often
            // does not invert the other.
            Heading(page, "Gamepad");
            // No "use a connected gamepad" toggle. A pad that is not being
            // held changes nothing on its own -- see GamepadInput.Active --
            // and on a phone the touch controls now step aside for a pad by
            // themselves and come back at the first touch, so the one thing
            // the toggle was ever asked to do is done without asking.
            _gamepadPresetRow = Add(page, new ChoiceRow("Preset",
                Enum.GetNames<Mods.Input.ControllerPreset>(),
                (int)InputSettings.ControllerPreset));
            _gamepadHorizontalSensitivity = Add(page, new SliderRow("Horizontal sensitivity",
                LookToSlider(InputSettings.GamepadHorizontalSensitivity),
                v => $"{SliderToLook(v).ToString("0.00", CultureInfo.InvariantCulture)}x"));
            _gamepadVerticalSensitivity = Add(page, new SliderRow("Vertical sensitivity",
                LookToSlider(InputSettings.GamepadVerticalSensitivity),
                v => $"{SliderToLook(v).ToString("0.00", CultureInfo.InvariantCulture)}x"));
            _gamepadInvertY = Add(page, new ToggleRow("Invert vertical aim (stick)",
                InputSettings.GamepadInvertY));
            _gamepadAimAssist = Add(page, new ToggleRow("Aim assist",
                InputSettings.GamepadAimAssistEnabled));
            _gamepadAimAssistStrength = Add(page, new SliderRow("Aim assist strength",
                (int)Math.Round(InputSettings.GamepadAimAssistStrength * 100),
                v => $"{v}%"));

            Heading(page, "Gamepad advanced");
            _gamepadGyro = Add(page, new ToggleRow("Gyro aiming (SDL controllers)",
                InputSettings.GamepadGyroEnabled));
            _gamepadGyroSensitivity = Add(page, new SliderRow("Gyro sensitivity",
                LookToSlider(InputSettings.GamepadGyroSensitivity),
                v => $"{SliderToLook(v).ToString("0.00", CultureInfo.InvariantCulture)}x"));
            _gamepadGyroInvertX = Add(page, new ToggleRow("Invert gyro horizontal aim",
                InputSettings.GamepadGyroInvertX));
            _gamepadGyroInvertY = Add(page, new ToggleRow("Invert gyro vertical aim",
                InputSettings.GamepadGyroInvertY));
            _gamepadHaptics = Add(page, new ToggleRow("Rumble and haptics",
                InputSettings.GamepadHapticsEnabled));
            _inputBalanceTelemetry = Add(page, new ToggleRow(
                "Local input-balance diagnostics",
                InputSettings.InputBalanceTelemetryEnabled));
            Explain(page, "Diagnostics stay on this device and contain no account or session identity.");
            _gamepadDeadZone = Add(page, new SliderRow("Move dead zone",
                DeadZoneToSlider(InputSettings.GamepadMoveDeadZone),
                v => $"{SliderToDeadZone(v).ToString("0.00", CultureInfo.InvariantCulture)}"));
            _gamepadLookDeadZone = Add(page, new SliderRow("Look dead zone",
                DeadZoneToSlider(InputSettings.GamepadLookDeadZone),
                v => $"{SliderToDeadZone(v).ToString("0.00", CultureInfo.InvariantCulture)}"));
            _gamepadOuterDeadZone = Add(page, new SliderRow("Outer dead zone",
                DeadZoneToSlider(InputSettings.GamepadOuterDeadZone),
                v => $"{SliderToDeadZone(v).ToString("0.00", CultureInfo.InvariantCulture)}"));
            _gamepadLook = Add(page, new SliderRow("Response exponent",
                ExponentToSlider(InputSettings.GamepadLookExponent),
                v => $"{SliderToExponent(v).ToString("0.00", CultureInfo.InvariantCulture)}"));
            _gamepadOuterBoost = Add(page, new ToggleRow("Outer-ring boost",
                InputSettings.GamepadOuterBoostEnabled));
            _gamepadTriggerPress = Add(page, new SliderRow("Trigger press",
                ThresholdToSlider(InputSettings.GamepadTriggerPressThreshold),
                v => $"{SliderToThreshold(v).ToString("0.00", CultureInfo.InvariantCulture)}"));
            _gamepadTriggerRelease = Add(page, new SliderRow("Trigger release",
                ThresholdToSlider(InputSettings.GamepadTriggerReleaseThreshold),
                v => $"{SliderToThreshold(v).ToString("0.00", CultureInfo.InvariantCulture)}"));
            _gamepadZoomMultiplier = Add(page, new SliderRow("Zoom multiplier",
                LookToSlider(InputSettings.GamepadZoomMultiplier),
                v => $"{SliderToLook(v).ToString("0.00", CultureInfo.InvariantCulture)}x"));

            Heading(page, "Gamepad buttons");
            var padRows = new List<PadRow>();
            foreach (Mods.Input.PadAction action in Mods.Input.PadBindings.Actions)
            {
                PadRow row = Add(page, new PadRow(action));
                row.Rebound += (_, _) => _gamepadPresetRow.Index = (int)Mods.Input.ControllerPreset.Custom;
                padRows.Add(row);
            }
            _gamepadPresetRow.Changed += (_, _) =>
            {
                InputSettings.ApplyPreset((Mods.Input.ControllerPreset)_gamepadPresetRow.Index);
                foreach (PadRow row in padRows) row.InvalidateVisual();
            };

            Heading(page, "Keys");
            var rows = new List<KeyRow>();
            foreach (PropertyInfo property in InputSettings.Bindings)
            {
                rows.Add(Add(page, new KeyRow(property)));
            }
            var reset = new MenuEntry("Reset to defaults", titleSize: 13)
            {
                Height = 30,
                Accent = GuiTheme.Warm,
                Margin = new Thickness(0, 8, 0, 0)
            };
            reset.Click += (_, _) =>
            {
                InputSettings.Reset();
                _sensitivity.Value = SensitivityToSlider(InputSettings.MouseSensitivity);
                _invertY.On = InputSettings.InvertMouseY;
                _invertX.On = InputSettings.InvertMouseX;
                _scrollAllWeapons.On = InputSettings.ScrollAllWeapons;
                _gamepadPresetRow.Index = (int)InputSettings.ControllerPreset;
                _gamepadHorizontalSensitivity.Value = LookToSlider(InputSettings.GamepadHorizontalSensitivity);
                _gamepadVerticalSensitivity.Value = LookToSlider(InputSettings.GamepadVerticalSensitivity);
                _gamepadInvertY.On = InputSettings.GamepadInvertY;
                _gamepadAimAssist.On = InputSettings.GamepadAimAssistEnabled;
                _gamepadAimAssistStrength.Value = (int)Math.Round(InputSettings.GamepadAimAssistStrength * 100);
                _gamepadGyro.On = InputSettings.GamepadGyroEnabled;
                _gamepadGyroSensitivity.Value = LookToSlider(InputSettings.GamepadGyroSensitivity);
                _gamepadGyroInvertX.On = InputSettings.GamepadGyroInvertX;
                _gamepadGyroInvertY.On = InputSettings.GamepadGyroInvertY;
                _gamepadHaptics.On = InputSettings.GamepadHapticsEnabled;
                _inputBalanceTelemetry.On = InputSettings.InputBalanceTelemetryEnabled;
                _gamepadDeadZone.Value = DeadZoneToSlider(InputSettings.GamepadMoveDeadZone);
                _gamepadLookDeadZone.Value = DeadZoneToSlider(InputSettings.GamepadLookDeadZone);
                _gamepadOuterDeadZone.Value = DeadZoneToSlider(InputSettings.GamepadOuterDeadZone);
                _gamepadLook.Value = ExponentToSlider(InputSettings.GamepadLookExponent);
                _gamepadOuterBoost.On = InputSettings.GamepadOuterBoostEnabled;
                _gamepadTriggerPress.Value = ThresholdToSlider(InputSettings.GamepadTriggerPressThreshold);
                _gamepadTriggerRelease.Value = ThresholdToSlider(InputSettings.GamepadTriggerReleaseThreshold);
                _gamepadZoomMultiplier.Value = LookToSlider(InputSettings.GamepadZoomMultiplier);
                // InputSettings.Reset puts the pad's buttons back too, so
                // these only have to be redrawn.
                foreach (PadRow row in padRows)
                {
                    row.InvalidateVisual();
                }
                foreach (KeyRow row in rows)
                {
                    row.InvalidateVisual();
                }
                _touchButtonsRow!.On = Mods.Input.TouchSettings.ButtonsVisible;
                foreach ((Mods.Input.TouchControl control, ToggleRow row) in _touchRows)
                {
                    row.On = Mods.Input.TouchSettings.IsEnabled(control);
                }
                if (_stylusAiming != null)
                {
                    _stylusAiming.On = InputSettings.StylusAimingEnabled;
                    _stylusSensitivity!.Value = LookToSlider(InputSettings.StylusSensitivity);
                    _stylusInvertY!.On = InputSettings.StylusInvertY;
                    _stylusPrimary!.Index = (int)InputSettings.StylusPrimaryAction;
                    _stylusSecondary!.Index = (int)InputSettings.StylusSecondaryAction;
                    _stylusClassicGestures!.On = InputSettings.StylusClassicGestures;
                    _stylusDoubleTapJump!.On = InputSettings.StylusDoubleTapJump;
                    _stylusFlickBoost!.On = InputSettings.StylusFlickBoost;
                    _stylusPressureToFire!.On = InputSettings.StylusPressureToFire;
                    _stylusPressureThreshold!.Value = (int)Math.Round(
                        InputSettings.StylusPressureThreshold * 100);
                }
            };
            Add(page, reset);
        }

        private ToggleRow? _touchButtonsRow;

        private readonly List<(Mods.Input.TouchControl Control, ToggleRow Row)> _touchRows = new();

        /// <summary>
        /// Which on-screen buttons the phone draws.
        ///
        /// Only on a touch screen: on the desktop these decide nothing, and a
        /// page of eleven switches that do nothing is worse than no page. The
        /// master switch is first and takes the rest away with it, since
        /// "turn them all off" is the answer most people who come here want
        /// and it should not be eleven presses.
        /// </summary>
        private void BuildTouchControls(StackPanel page)
        {
            if (!OperatingSystem.IsAndroid())
            {
                return;
            }
            Heading(page, "On-screen buttons");
            _touchButtonsRow = Add(page, new ToggleRow("Show on-screen buttons",
                Mods.Input.TouchSettings.ButtonsVisible));
            Add(page, new Note("The stick, aiming, the double tap that jumps and the flick "
                + "that boosts are not buttons, so they keep working with every one of these off."));
            foreach ((Mods.Input.TouchControl control, string label) in Mods.Input.TouchSettings.Order)
            {
                ToggleRow row = Add(page, new ToggleRow(label,
                    Mods.Input.TouchSettings.IsEnabled(control)));
                _touchRows.Add((control, row));
            }
            void ShowTouchRows()
            {
                foreach ((_, ToggleRow row) in _touchRows)
                {
                    row.IsVisible = _touchButtonsRow.On;
                }
            }
            _touchButtonsRow.Changed += (_, _) => ShowTouchRows();
            ShowTouchRows();
        }

        private void BuildStylusControls(StackPanel page)
        {
            if (!OperatingSystem.IsAndroid()) return;
            Heading(page, "Stylus");
            _stylusAiming = Add(page, new ToggleRow("Stylus aiming",
                InputSettings.StylusAimingEnabled));
            _stylusSensitivity = Add(page, new SliderRow("Sensitivity",
                LookToSlider(InputSettings.StylusSensitivity),
                v => $"{SliderToLook(v).ToString("0.00", CultureInfo.InvariantCulture)}x"));
            _stylusInvertY = Add(page, new ToggleRow("Invert vertical aim",
                InputSettings.StylusInvertY));
            string[] actions = Enum.GetNames<Mods.Input.StylusAction>();
            _stylusPrimary = Add(page, new ChoiceRow("Primary button", actions,
                (int)InputSettings.StylusPrimaryAction));
            _stylusSecondary = Add(page, new ChoiceRow("Secondary button", actions,
                (int)InputSettings.StylusSecondaryAction));
            _stylusClassicGestures = Add(page, new ToggleRow("Classic gestures",
                InputSettings.StylusClassicGestures));
            _stylusDoubleTapJump = Add(page, new ToggleRow("Double tap to jump",
                InputSettings.StylusDoubleTapJump));
            _stylusFlickBoost = Add(page, new ToggleRow("Flick to boost",
                InputSettings.StylusFlickBoost));
            Heading(page, "Stylus advanced");
            _stylusPressureToFire = Add(page, new ToggleRow("Pressure to fire",
                InputSettings.StylusPressureToFire));
            _stylusPressureThreshold = Add(page, new SliderRow("Pressure threshold",
                (int)Math.Round(InputSettings.StylusPressureThreshold * 100),
                v => $"{v}%"));
        }

        private static int SensitivityToSlider(float sensitivity)
        {
            return Math.Clamp((int)Math.Round((sensitivity - 0.1f) / 2.9f * 100), 0, 100);
        }

        private static float SliderToSensitivity(int value)
        {
            return 0.1f + value / 100f * 2.9f;
        }

        // The pad's look runs 0.25x to 3x, which is 50 to 630 degrees a second
        // -- slower than anybody plays at one end and faster at the other.
        private static int LookToSlider(float look)
        {
            return Math.Clamp((int)Math.Round((look - 0.25f) / 2.75f * 100), 0, 100);
        }

        private static float SliderToLook(int value)
        {
            return 0.25f + value / 100f * 2.75f;
        }

        private static int ExponentToSlider(float exponent)
        {
            return Math.Clamp((int)Math.Round((exponent - 0.5f) / 2.5f * 100), 0, 100);
        }

        private static float SliderToExponent(int value)
        {
            return 0.5f + value / 100f * 2.5f;
        }

        private static int ThresholdToSlider(float threshold)
        {
            return Math.Clamp((int)Math.Round(threshold * 100), 0, 100);
        }

        private static float SliderToThreshold(int value)
        {
            return Math.Clamp(value, 0, 100) / 100f;
        }

        // Up to half the stick's travel. Past that a pad is broken rather than
        // worn, and a dead zone that large makes the game feel worse than the
        // drift it was hiding.
        private static int DeadZoneToSlider(float dead)
        {
            return Math.Clamp((int)Math.Round(dead / 0.5f * 100), 0, 100);
        }

        private static float SliderToDeadZone(int value)
        {
            return value / 100f * 0.5f;
        }

        // ---------------------------------------------------------- match rules

        private void BuildMatch()
        {
            StackPanel page = AddSection("Gameplay");
            Heading(page, "Match rules");
            _pointGoal = Add(page, new FieldRow("Point goal", _settings.PointGoal, boxWidth: 120));
            _timeLimit = Add(page, new FieldRow("Time limit", _settings.TimeLimit, boxWidth: 120));
            _timeLimit.Box.Watermark = "m:ss";
            string[] damage = { "low", "medium", "high" };
            _damageRow = Add(page, new ChoiceRow("Damage", damage,
                Math.Max(0, Array.IndexOf(damage, _settings.DamageLevel))));
            _teamPlay = Add(page, new ToggleRow("Team play", _settings.TeamPlay == "on"));
            _friendlyFire = Add(page, new ToggleRow("Friendly fire", _settings.FriendlyFire == "on"));
            _radar = Add(page, new ToggleRow("Hunter radar", _settings.HunterRadar == "on"));
            _affinity = Add(page, new ToggleRow("Affinity weapons",
                _settings.AffinityWeapons == "on"));
            BuildLauncher(page);
        }

        /// <summary>
        /// Who you are and how the launcher behaves: the launcher's own preferences,
        /// which live in launcher.txt rather than in the game's settings.json.
        ///
        /// They were on a card of the front screen while this window was
        /// Windows-only and the other platforms had nothing else. They belong
        /// here: the Node browser owns public multiplayer discovery and lobby
        /// admission, so this page does not persist direct server endpoints.
        /// </summary>
        private void BuildLauncher(StackPanel page)
        {
            Heading(page, "You");
            _playerName = Add(page, new FieldRow("Your name", LauncherPrefs.PlayerName,
                boxWidth: 200));
            // The seven playable hunters and Random, the same list the front
            // screen offers -- not every name in the enum, which also holds the
            // Guardian and the enemies' entries.
            string[] hunters = Enumerable.Range(0, 7)
                .Select(i => ((Hunter)i).ToString())
                .Append(Hunter.Random.ToString()).ToArray();
            _hunterRow = Add(page, new ChoiceRow("Hunter", hunters,
                Math.Max(0, Array.IndexOf(hunters, LauncherPrefs.LastHunter.ToString()))));

            _autoUpdate = new ChoiceRow("Updates", new[] { "Automatic", "Notify only", "Off" },
                (int)LauncherPrefs.UpdatePolicy);
            if (Update.Updater.Configured) Add(page, _autoUpdate);
            if (Update.Updater.Configured)
            {
                Explain(page, "Automatic downloads and stages signed updates. Notify only keeps installation manual. Off skips automatic checks.");
            }

            Heading(page, "Game files");
            var files = new MenuEntry("Game files", GameFiles.Describe(), titleSize: 15);
            files.SubtitleColor = GameFiles.Ready ? GuiTheme.Good : GuiTheme.Warm;
            files.Click += (_, _) =>
            {
                GameFilesRequested?.Invoke(this, EventArgs.Empty);
                Close();
            };
            Add(page, files);
        }

        private void BuildNetwork()
        {
            StackPanel page = _networkPage = AddSection("Network");
            Heading(page, "Diagnostics");
            _advancedNetworkRow = Add(page, new ToggleRow("Network diagnostics",
                global::MphRead.Hud.Network.NetworkHealthSettings.Advanced));
            Explain(page, "Shows additional live network details while connected.");
        }

        private void BuildAccessibility()
        {
            StackPanel page = AddSection("Accessibility");
            Heading(page, "Motion");
            _reducedMotion = Add(page, new ToggleRow("Reduce interface motion",
                LauncherPrefs.ReducedMotion));
            Explain(page, "Disables optional Project Prime shell transitions and decorative motion. Gameplay animation is unchanged.");
        }

        // -------------------------------------------------------------- footer

        private Control BuildFooter()
        {
            var save = new MenuEntry("Apply & save settings", titleSize: 13)
            {
                Primary = true,
                Height = 40,
                MinWidth = 180
            };
            save.Click += (_, _) => TryCommit();
            var cancel = new MenuEntry("Cancel", titleSize: 13)
            {
                Height = 40,
                MinWidth = 72,
                Accent = GuiTheme.TextDim,
                Margin = new Thickness(0, 0, 10, 0)
            };
            cancel.Click += (_, _) => Close();
            _saveError = new Note("", GuiTheme.Warm) { IsVisible = false };

            var actionLabel = new TextBlock
            {
                Text = _inGame ? "ACTIVE MATCH CONFIGURATION" : "LOCAL PROFILE CONFIGURATION",
                FontFamily = GuiTheme.Display,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = GuiTheme.TextBrush
            };
            var actionDetail = new TextBlock
            {
                Text = _inGame
                    ? "Save changes and return to the start menu."
                    : "Save changes to this device and close settings.",
                FontFamily = GuiTheme.Display,
                FontSize = 10,
                Foreground = GuiTheme.TextDimBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 12, 0)
            };
            var context = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 0
            };
            context.Children.Add(actionLabel);
            context.Children.Add(actionDetail);
            context.Children.Add(_saveError);
            _footerContext = context;

            var actions = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(cancel, 0);
            Grid.SetColumn(save, 1);
            actions.Children.Add(cancel);
            actions.Children.Add(save);
            _footerActions = actions;

            _footerGrid.Children.Add(_footerContext);
            _footerGrid.Children.Add(_footerActions);
            return _footerGrid;
        }

        /// <summary>
        /// Save, and say so on the window if it does not work.
        ///
        /// Writing settings.json touches the disk, and the disk is allowed to
        /// say no -- a read-only folder, a file open elsewhere, a full drive.
        /// That is worth a line on the screen, not an exception out of a window
        /// that may be sitting over a match still being played.
        /// </summary>
        private void TryCommit()
        {
            try
            {
                Commit();
            }
            catch (Exception ex)
            {
                _saveError.Text = $"Could not save: {ex.Message}";
                _saveError.IsVisible = true;
            }
        }

        private void Commit()
        {
            // Display
            if (_windowRow != null)
            {
                LauncherPrefs.WindowMode = _windowRow.Index == 1
                    ? WindowStartMode.BorderlessFullscreen
                    : WindowStartMode.Windowed;
                WindowMode.Startup = LauncherPrefs.WindowMode;
            }
            _settings.ResolutionScale = Math.Max(RenderOptions.MinScale, _resolutionScale.Value)
                .ToString(CultureInfo.InvariantCulture);
            _settings.Lighting = RenderOptions.OnOff(_lightingRow.On);
            _settings.Fog = RenderOptions.OnOff(_fogRow.On);
            GraphicsPreset graphicsPreset = (GraphicsPreset)_graphicsPresetRow.Index;
            TextureFilteringPreset filteringPreset
                = (TextureFilteringPreset)_textureFilteringPresetRow.Index;
            _settings.GraphicsPreset = RenderOptions.FormatGraphicsPreset(graphicsPreset);
            _settings.TextureFilteringPreset
                = RenderOptions.FormatTextureFilteringPreset(filteringPreset);
            // Keep the legacy field synchronized for older builds that read
            // settings.json without the R12 keys.
            _settings.TextureFiltering = RenderOptions.OnOff(
                filteringPreset != TextureFilteringPreset.Original);
            _settings.Anisotropy
                = RenderOptions.FormatAnisotropy(RenderOptions.AnisotropyAtIndex(_anisotropyRow.Index));
            _settings.Msaa
                = RenderOptions.FormatMsaa(RenderOptions.MsaaAtIndex(_msaaRow.Index));
            _settings.Bloom = RenderOptions.OnOff(_bloomRow.On);
            _settings.DynamicVisualLights = RenderOptions.OnOff(_dynamicVisualLightsRow.On);
            _settings.ShowFps = RenderOptions.OnOff(_fpsRow.On);
            _settings.AdvancedNetwork = RenderOptions.OnOff(_advancedNetworkRow.On);
            _settings.HitMarkers = ((Combat.HitMarkerMode)_hitMarkerRow.Index).ToString();
            _settings.HitMarkerTiming = ((Combat.HitMarkerTiming)_hitMarkerTimingRow.Index).ToString();
            _settings.HeadshotCue = RenderOptions.OnOff(_headshotCueRow.On);
            _settings.KillConfirmation = RenderOptions.OnOff(_killConfirmationRow.On);
            _settings.RadarStyle = ((global::MphRead.Hud.Radar.RadarStyle)_radarStyleRow.Index).ToString();
            _settings.RadarOrientation = ((global::MphRead.Hud.Radar.RadarOrientation)_radarOrientationRow.Index).ToString();
            _settings.RadarPosition = ((global::MphRead.Hud.Radar.RadarAnchor)_radarPositionRow.Index).ToString();
            _settings.RadarScale = (_radarSizeRow.Index switch { 0 => .8f, 2 => 1.2f, _ => 1f })
                .ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            int cap = _fpsLimitStops[Math.Clamp(_fpsLimitRow.Value, 0,
                _fpsLimitStops.Length - 1)].Cap;
            FrameTiming.FrameRateCap = cap;
            _settings.FrameRateCap = FrameTiming.CapString(cap);
            _settings.CelShading = RenderOptions.OnOff(_celRow.On);
            _settings.CelBands = "8";
            _settings.CelEdge = "50";
            Features.ProHud = _proHud.On;
            Features.ProHudFixedWeapon = _proHudWeaponRow.Index == 0;
            Crosshair.Size = (CrosshairSize)_crosshairSizeRow.Index;
            Crosshair.Style = (CrosshairStyle)_crosshairStyleRow.Index;
            // Audio
            _settings.SfxVolume = (_sfxVolume.Value / 100f).ToString(CultureInfo.InvariantCulture);
            _settings.FeedbackVolume = (_feedbackVolume.Value / 100f).ToString(CultureInfo.InvariantCulture);
            _settings.MusicVolume = (_musicVolume.Value / 100f).ToString(CultureInfo.InvariantCulture);
            LauncherPrefs.AnnouncerPack = _announcerPackRow.Index == 0
                ? null : _announcerPacks[_announcerPackRow.Index - 1].Identity;
            LauncherPrefs.MusicPack = _musicPackRow.Index == 0
                ? null : _musicPacks[_musicPackRow.Index - 1].Identity;
            _settings.Language = _languageRow.Value;
            // Controls
            InputSettings.MouseSensitivity = SliderToSensitivity(_sensitivity.Value);
            InputSettings.InvertMouseY = _invertY.On;
            InputSettings.InvertMouseX = _invertX.On;
            InputSettings.ScrollAllWeapons = _scrollAllWeapons.On;
            InputSettings.ControllerPreset = (Mods.Input.ControllerPreset)_gamepadPresetRow.Index;
            InputSettings.GamepadHorizontalSensitivity = SliderToLook(_gamepadHorizontalSensitivity.Value);
            InputSettings.GamepadVerticalSensitivity = SliderToLook(_gamepadVerticalSensitivity.Value);
            InputSettings.GamepadInvertY = _gamepadInvertY.On;
            InputSettings.GamepadAimAssistEnabled = _gamepadAimAssist.On;
            InputSettings.GamepadAimAssistStrength = _gamepadAimAssistStrength.Value / 100f;
            InputSettings.GamepadGyroEnabled = _gamepadGyro.On;
            InputSettings.GamepadGyroSensitivity = SliderToLook(_gamepadGyroSensitivity.Value);
            InputSettings.GamepadGyroInvertX = _gamepadGyroInvertX.On;
            InputSettings.GamepadGyroInvertY = _gamepadGyroInvertY.On;
            InputSettings.GamepadHapticsEnabled = _gamepadHaptics.On;
            InputSettings.InputBalanceTelemetryEnabled = _inputBalanceTelemetry.On;
            if (!InputSettings.GamepadGyroEnabled) Mods.Input.GamepadGyro.Reset();
            if (!InputSettings.GamepadHapticsEnabled) Mods.Input.GamepadHaptics.Stop();
            InputSettings.GamepadMoveDeadZone = SliderToDeadZone(_gamepadDeadZone.Value);
            InputSettings.GamepadLookDeadZone = SliderToDeadZone(_gamepadLookDeadZone.Value);
            InputSettings.GamepadOuterDeadZone = SliderToDeadZone(_gamepadOuterDeadZone.Value);
            InputSettings.GamepadLookExponent = SliderToExponent(_gamepadLook.Value);
            InputSettings.GamepadOuterBoostEnabled = _gamepadOuterBoost.On;
            InputSettings.GamepadTriggerPressThreshold = SliderToThreshold(_gamepadTriggerPress.Value);
            InputSettings.GamepadTriggerReleaseThreshold = SliderToThreshold(_gamepadTriggerRelease.Value);
            InputSettings.GamepadZoomMultiplier = SliderToLook(_gamepadZoomMultiplier.Value);
            if (_touchButtonsRow != null)
            {
                Mods.Input.TouchSettings.ButtonsVisible = _touchButtonsRow.On;
                foreach ((Mods.Input.TouchControl control, ToggleRow row) in _touchRows)
                {
                    Mods.Input.TouchSettings.SetEnabled(control, row.On);
                }
            }
            if (_stylusAiming != null)
            {
                InputSettings.StylusAimingEnabled = _stylusAiming.On;
                InputSettings.StylusSensitivity = SliderToLook(_stylusSensitivity!.Value);
                InputSettings.StylusInvertY = _stylusInvertY!.On;
                InputSettings.StylusPrimaryAction = (Mods.Input.StylusAction)_stylusPrimary!.Index;
                InputSettings.StylusSecondaryAction = (Mods.Input.StylusAction)_stylusSecondary!.Index;
                InputSettings.StylusClassicGestures = _stylusClassicGestures!.On;
                InputSettings.StylusDoubleTapJump = _stylusDoubleTapJump!.On;
                InputSettings.StylusFlickBoost = _stylusFlickBoost!.On;
                InputSettings.StylusPressureToFire = _stylusPressureToFire!.On;
                InputSettings.StylusPressureThreshold = _stylusPressureThreshold!.Value / 100f;
            }
            InputSettings.Save();
            // The players in the match already have their own copies of these.
            if (_scene != null) InputSettings.ApplyToPlayers(_scene);
            // Match rules
            _settings.PointGoal = _pointGoal.Value;
            _settings.TimeLimit = _timeLimit.Value;
            _settings.DamageLevel = _damageRow.Value;
            _settings.TeamPlay = _teamPlay.On ? "on" : "off";
            _settings.FriendlyFire = _friendlyFire.On ? "on" : "off";
            _settings.HunterRadar = _radar.On ? "on" : "off";
            _settings.AffinityWeapons = _affinity.On ? "on" : "off";
            // Launcher preferences
            if (_playerName.Value.Trim().Length > 0)
            {
                LauncherPrefs.PlayerName = _playerName.Value.Trim();
            }
            LauncherPrefs.LastHunter = Enum.Parse<Hunter>(_hunterRow.Value);
            LauncherPrefs.UpdatePolicy = (UpdatePolicy)_autoUpdate.Index;
            LauncherPrefs.ReducedMotion = _reducedMotion.On;
            ClientSettings.CommitSettings(_settings);
            LauncherPrefs.Save();
            ClientPresentationContent.Refresh();
            // Written and *applied*: the volumes, the language and the match
            // rules were only ever put in the file, so a music slider moved
            // here would otherwise leave the music exactly where it was --
            // during a match as well as before one, since this same window
            // opens from the pause menu.
            Mods.GameSettings.Apply(_settings);
            Saved = true;
            Close();
        }
    }
}
