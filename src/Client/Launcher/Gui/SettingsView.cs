using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using MphRead.Mods.Launcher.Settings;
using MphRead.Mods.Update;
using MphRead.Runtime.Content;
using FrameTiming = MphRead.Mods.Render.FrameTiming;

namespace MphRead.Mods.Launcher.Gui
{
    internal enum SettingsIdentityKind
    {
        SignedOut,
        Guest,
        Authenticated
    }

    /// <summary>
    /// Immutable identity presentation for one Settings view lifetime. The
    /// view receives a snapshot so account, guest and signed-out names cannot
    /// be confused with the local launcher preference or a live Node identity.
    /// </summary>
    internal sealed record SettingsIdentityContext(
        SettingsIdentityKind Kind, string DisplayName, Action? HunterProfileRequested)
    {
        public bool IsAuthenticated => Kind == SettingsIdentityKind.Authenticated;
        public bool IsGuest => Kind == SettingsIdentityKind.Guest;
        public bool CanOpenHunterProfile => IsAuthenticated;

        public static SettingsIdentityContext SignedOut { get; }
            = new(SettingsIdentityKind.SignedOut, "Guest", null);

        public static SettingsIdentityContext Guest(string displayName)
            => new(SettingsIdentityKind.Guest, Normalize(displayName, "Guest"), null);

        public static SettingsIdentityContext Authenticated(string displayName,
            Action? hunterProfileRequested = null)
            => new(SettingsIdentityKind.Authenticated,
                Normalize(displayName, "Account"), hunterProfileRequested);

        public static SettingsIdentityContext From(PrimeShellState shell,
            Action? hunterProfileRequested = null)
        {
            ArgumentNullException.ThrowIfNull(shell);
            if (shell.SignedIn)
            {
                return Authenticated(shell.DisplayName, hunterProfileRequested);
            }
            return shell.GuestSelected ? Guest(shell.DisplayName) : SignedOut;
        }

        private static string Normalize(string value, string fallback)
        {
            string normalized = value?.Trim() ?? "";
            return normalized.Length == 0 ? fallback : normalized;
        }
    }

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
    internal sealed class SettingsView : UserControl, IDisposable
    {
        private readonly MenuSettings _settings;
        private readonly bool _inGame;
        private readonly bool _captureTouchControls;
        private readonly bool? _captureGyroSupported;
        private readonly bool? _captureAdvancedControllerExpanded;
        private readonly SettingsIdentityContext _identity;
        private bool _observingControllerCapabilities;
        private bool _disposed;
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
        private Note _graphicsPresetNote = null!;
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
        private ChoiceRow _radarPositionRow = null!;
        private SliderRow _radarScaleRow = null!, _radarOffsetXRow = null!, _radarOffsetYRow = null!;
        private ToggleRow _headshotCueRow = null!, _killConfirmationRow = null!, _killcamRow = null!;

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
        private SliderRow _reticleOpacity = null!;
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
        private ToggleRow _morphBallMouseFlickBoost = null!, _morphBallStickFlickBoost = null!;
        private ToggleRow? _morphBallSwipeBoost;
        private KeyRow _chatKeyRow = null!;
        private ChoiceRow _gamepadPresetRow = null!;
        private SliderRow _gamepadHorizontalSensitivity = null!;
        private SliderRow _gamepadVerticalSensitivity = null!;
        private SliderRow _gamepadLook = null!;
        private SliderRow _gamepadDeadZone = null!;
        private SliderRow _gamepadLookDeadZone = null!;
        private SliderRow _gamepadOuterDeadZone = null!;
        private ToggleRow _gamepadOuterBoost = null!;
        private SliderRow _gamepadTriggerPress = null!;
        private SliderRow _gamepadTriggerRelease = null!;
        private SliderRow _gamepadZoomMultiplier = null!;
        private SliderRow _gamepadMoveActivate = null!, _gamepadMoveRelease = null!;
        private SliderRow _gamepadYawRate = null!, _gamepadPitchRate = null!;
        private SliderRow _gamepadOuterBoostStart = null!, _gamepadOuterYawBoost = null!,
            _gamepadOuterPitchBoost = null!, _gamepadBoostDelay = null!, _gamepadBoostRamp = null!;
        private ToggleRow _gamepadInvertY = null!;
        private ChoiceRow _gamepadGyro = null!, _gamepadGyroActivation = null!;
        private ChoiceRow _gamepadResponseCurve = null!, _gamepadTurnAcceleration = null!;
        private ToggleRow _gamepadGyroInvertX = null!;
        private ToggleRow _gamepadGyroInvertY = null!, _gamepadHaptics = null!;
        private ToggleRow _inputBalanceTelemetry = null!;
        private Note? _gyroCapabilityNote;
        private Expander? _advancedControllerExpander;
        private SliderRow _gamepadGyroSensitivity = null!, _gamepadHapticsStrength = null!;
        private ToggleRow? _stylusAiming, _stylusInvertY, _stylusClassicGestures,
            _stylusDoubleTapJump, _stylusFlickBoost, _stylusPressureToFire;
        private SliderRow? _stylusSensitivity, _stylusPressureThreshold;
        private ChoiceRow? _stylusPrimary, _stylusSecondary;
        private FieldRow _playerName = null!;
        private ChoiceRow _hunterRow = null!;
        private ChoiceRow _autoUpdate = null!;
        private ChoiceRow _preferredRegion = null!;
        private IReadOnlyList<PreferredRegionOption> _preferredRegionOptions
            = Array.Empty<PreferredRegionOption>();
        private MenuEntry? _hunterProfileAction;
        private ToggleRow _debugLogging = null!;
        private MenuEntry _shareLogs = null!;
        private Note _updateStatus = null!;
        private ToggleRow _reducedMotion = null!;
        private Note _saveError = null!;

        private readonly HashSet<string> _renderedRowIds = new(StringComparer.Ordinal);
        private bool _refreshingControllerRows;
        private bool _synchronizingControllerPairs;
        // SliderRow stores an integer presentation value, while controller
        // preferences are persisted as floats. Keep the raw preference when
        // a row was only populated from that preference; only a real user
        // edit opts into the lossy presentation-to-preference conversion.
        private readonly HashSet<SliderRow> _editedControllerSliders = new();
        private bool _refreshingGraphicsRows;
        private bool _observingUpdates;

        private static readonly string[] _graphicsPresetChoices =
            RenderOptions.GraphicsPresetLabels.Concat(new[] { "Custom" }).ToArray();
        private const int CustomGraphicsPresetIndex = 3;
        private GraphicsPreset _graphicsPresetBase;

        /// <summary>
        /// IDs assigned to controls actually inserted into this view. This is
        /// intentionally populated by <see cref="Add{T}"/>, not copied from
        /// the metadata inventory, so coverage can detect a missing concrete
        /// row while platform-gated controls remain honest.
        /// </summary>
        internal IReadOnlyCollection<string> RenderedSettingRowIds => _renderedRowIds;

        /// <summary>Short compatibility name for UI coverage callers.</summary>
        internal IReadOnlyCollection<string> RenderedRowIds => _renderedRowIds;

        internal bool HasTouchControlRows => _touchButtonsRow != null;
        internal bool? RenderedTouchButtonsVisible => _touchButtonsRow?.On;
        internal bool RenderedGyroControlsEnabled
            => _gamepadGyro.IsEnabled && _gamepadGyroSensitivity.IsEnabled
                && _gamepadGyroActivation.IsEnabled
                && _gamepadGyroInvertX.IsEnabled && _gamepadGyroInvertY.IsEnabled;
        internal string? RenderedGyroCapabilityNote => _gyroCapabilityNote?.Text;
        internal bool? RenderedAdvancedControllerExpanded
            => _advancedControllerExpander?.IsExpanded;
        internal bool? CaptureAdvancedControllerExpanded
            => _captureAdvancedControllerExpanded;
        internal SettingsIdentityContext IdentityContext => _identity;
        internal bool HasHunterProfileAction => _hunterProfileAction != null;
        internal bool IsObservingControllerCapabilities => _observingControllerCapabilities;

        private const double _railWidth = 244;

        /// <summary>True when the user pressed save rather than closing.</summary>
        public bool Saved { get; private set; }

        /// <summary>What a frame around this should be titled.</summary>
        public string WindowTitle => $"{Mods.Branding.Name} settings";

        /// <summary>True when this was opened over a match rather than the launcher.</summary>
        public bool InGame => _inGame;

        private readonly Scene? _scene;

        public SettingsView(MenuSettings settings, bool inGame = false, Scene? scene = null,
            SettingsIdentityContext? identity = null)
            : this(settings, inGame, scene, captureTouchControls: false,
                captureGyroSupported: null, captureAdvancedControllerExpanded: null,
                identity: identity)
        {
        }

        internal SettingsView(MenuSettings settings, bool inGame, Scene? scene,
            bool captureTouchControls, bool? captureGyroSupported,
            bool? captureAdvancedControllerExpanded,
            SettingsIdentityContext? identity = null)
        {
            _scene = scene;
            _settings = settings;
            _inGame = inGame;
            _captureTouchControls = captureTouchControls;
            _captureGyroSupported = captureGyroSupported;
            _captureAdvancedControllerExpanded = captureAdvancedControllerExpanded;
            _identity = identity ?? SettingsIdentityContext.SignedOut;

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
            if (_disposed)
            {
                return;
            }
            if (!_observingUpdates)
            {
                _observingUpdates = true;
                Update.Updater.Coordinator.StatusChanged += UpdateStatusChanged;
            }
            SubscribeControllerCapabilities();
            ApplyControllerCapabilities(ControllerCapabilities.Current);
            Dispatcher.UIThread.Post(() => _sections[0].Button.Focus(),
                DispatcherPriority.Background);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            UnsubscribeControllerCapabilities();
            if (_observingUpdates)
            {
                Update.Updater.Coordinator.StatusChanged -= UpdateStatusChanged;
                _observingUpdates = false;
            }
            base.OnDetachedFromVisualTree(e);
        }

        private void SubscribeControllerCapabilities()
        {
            if (_observingControllerCapabilities || _disposed)
            {
                return;
            }
            _observingControllerCapabilities = true;
            ControllerCapabilities.Changed += ControllerCapabilitiesChanged;
        }

        private void UnsubscribeControllerCapabilities()
        {
            if (!_observingControllerCapabilities)
            {
                return;
            }
            ControllerCapabilities.Changed -= ControllerCapabilitiesChanged;
            _observingControllerCapabilities = false;
        }

        private void ControllerCapabilitiesChanged(object? sender,
            ControllerCapabilityChangedEventArgs _)
        {
            // SDL/Android publishers may run on their input thread. Never
            // mutate Avalonia controls from that callback, and discard a
            // queued update after this view has been replaced or disposed.
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed && _observingControllerCapabilities)
                {
                    // The store raises outside its lock. A callback already
                    // queued here may be older than a replacement owner by
                    // the time Avalonia runs it, so the store's current
                    // immutable snapshot is the only safe value to apply.
                    ApplyControllerCapabilities(ControllerCapabilities.Current);
                }
            });
        }

        private void ApplyControllerCapabilities(ControllerCapabilitySnapshot snapshot)
        {
            if (_gamepadGyro == null || _gamepadGyroSensitivity == null
                || _gamepadGyroActivation == null || _gamepadGyroInvertX == null
                || _gamepadGyroInvertY == null)
            {
                return;
            }
            bool enabled = _captureGyroSupported ??
                (snapshot.IsAvailable && snapshot.HasGyroscope == true);
            _gamepadGyro.IsEnabled = enabled;
            _gamepadGyroActivation.IsEnabled = enabled;
            _gamepadGyroSensitivity.IsEnabled = enabled;
            _gamepadGyroInvertX.IsEnabled = enabled;
            _gamepadGyroInvertY.IsEnabled = enabled;
            if (_gyroCapabilityNote == null)
            {
                return;
            }
            _gyroCapabilityNote.Text = _captureGyroSupported.HasValue
                ? (_captureGyroSupported.Value
                    ? "Gyro controls are enabled for this capture capability state."
                    : "Gyro aiming is disabled for this capture capability state.")
                : snapshot.IsAvailable && snapshot.HasGyroscope == true
                    ? "Gyro controls are enabled for the connected controller."
                    : snapshot.IsAvailable && snapshot.HasGyroscope == false
                        ? "The connected controller does not report a gyroscope."
                        : snapshot.IsAvailable
                            ? "Gyro support is unknown for this controller; the saved preference is preserved."
                            : "Connect a controller with a reported gyroscope to enable these controls; the saved preference is preserved.";
        }

        /// <summary>Release external observers when this view's lifetime ends.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            UnsubscribeControllerCapabilities();
            if (_observingUpdates)
            {
                Update.Updater.Coordinator.StatusChanged -= UpdateStatusChanged;
                _observingUpdates = false;
            }
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
                Text = "SETTINGS",
                FontFamily = GuiTheme.Display,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = GuiTheme.TextBrush
            };
            var context = new TextBlock
            {
                Text = _identity.IsAuthenticated ? "ACCOUNT SETTINGS"
                    : _identity.IsGuest ? "GUEST SETTINGS" : "SIGNED-OUT SETTINGS",
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
            "Player" => "IDENTITY / HUNTER",
            "Controls" => "MOUSE / PAD / KEYS",
            "Graphics" => "DISPLAY / QUALITY / HUD",
            "Audio" => "MIX / PACKS / LANGUAGE",
            "System" => "UPDATES / FILES / LOGS",
            "Network" => "REGION / DIAGNOSTICS",
            "Accessibility" => "MOTION OPTIONS",
            "About" => "CREDITS / SUPPORT",
            _ => "CONFIGURATION"
        };

        private static string SectionTitle(string name) => name switch
        {
            "Player" => "PLAYER PROFILE",
            "Controls" => "CONTROLS",
            "Graphics" => "DISPLAY & GRAPHICS",
            "Audio" => "AUDIO & PRESENTATION",
            "System" => "SYSTEM & SUPPORT",
            "Network" => "NETWORK PREFERENCES",
            "Accessibility" => "ACCESSIBILITY & MOTION",
            "About" => "PROJECT PRIME SYSTEM",
            _ => name.ToUpperInvariant()
        };

        private static string SectionDescription(string name) => name switch
        {
            "Player" => "Configure your account or guest display name and preferred hunter. Online match rules belong to the lobby host.",
            "Controls" => "Tune mouse, gamepad, touch and direct action bindings.",
            "Graphics" => "Set the display path, render budget, visual quality and combat HUD.",
            "Audio" => "Balance the mix and select installed presentation content.",
            "System" => "Check updates, game-file readiness and local diagnostics.",
            "Network" => "Choose a preferred discovery region and control connection diagnostics.",
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
            // Retain the former navigation token while presenting the page
            // under its clearer Player identity. Persisted setting IDs and
            // launcher keys remain unchanged.
            if (String.Equals(name, "Gameplay", StringComparison.OrdinalIgnoreCase))
            {
                name = "Player";
            }
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

        private T Add<T>(StackPanel page, T control, string? rowId = null) where T : Control
        {
            if (!String.IsNullOrWhiteSpace(rowId))
            {
                control.Name = rowId;
                _renderedRowIds.Add(rowId);
            }
            ActiveSector(page).Children.Add(control);
            return control;
        }

        private void BuildPages()
        {
            BuildMatch();
            BuildControls();
            BuildDisplay();
            BuildAudio();
            BuildSystem();
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
            Heading(page, "Window");
            _windowRow = Add(page, new ChoiceRow("Mode",
                new[] { "Windowed", "Fullscreen (borderless)" },
                LauncherPrefs.WindowMode == WindowStartMode.BorderlessFullscreen ? 1 : 0),
                SettingRowIds.WindowMode);
            if (OperatingSystem.IsAndroid())
            {
                _windowRow.IsEnabled = false;
                Explain(page, "Window mode is managed by Android and cannot be changed here.");
            }

            Heading(page, "Performance");
            _resolutionScale = Add(page, new SliderRow("Render scale",
                Math.Max(RenderOptions.MinScale, RenderOptions.ResolutionScale),
                v => $"{Math.Max(RenderOptions.MinScale, v)}%",
                min: RenderOptions.MinScale), SettingRowIds.RenderScale);
            // Under the render scale because they are the same question asked
            // from both ends -- how much picture, and how often -- and because
            // the two of them are what somebody who is not getting a smooth
            // game comes to this page to change.
            _fpsLimitRow = Add(page, new SliderRow("FPS limit",
                FpsLimitStopIndex(FrameTiming.FrameRateCap),
                v => _fpsLimitStops[Math.Clamp(v, 0, _fpsLimitStops.Length - 1)].Label,
                min: 0, max: _fpsLimitStops.Length - 1, keyStep: 1), SettingRowIds.FpsLimit);
            Heading(page, "Quality");
            _graphicsPresetBase = RenderOptions.GraphicsPreset;
            bool graphicsPresetCustom = GraphicsQualityHasOverrides(_graphicsPresetBase);
            _graphicsPresetRow = Add(page, new ChoiceRow("Graphics quality",
                _graphicsPresetChoices,
                graphicsPresetCustom ? CustomGraphicsPresetIndex : (int)_graphicsPresetBase),
                SettingRowIds.GraphicsPreset);
            _textureFilteringPresetRow = Add(page, new ChoiceRow("Texture filtering",
                RenderOptions.TextureFilteringLabels, (int)RenderOptions.TextureFilteringPreset), SettingRowIds.TextureFiltering);
            _anisotropyRow = Add(page, new ChoiceRow("Texture detail",
                RenderOptions.AnisotropyLabels, RenderOptions.AnisotropyIndex(RenderOptions.Anisotropy)), SettingRowIds.Anisotropy);
            _msaaRow = Add(page, new ChoiceRow("Edge smoothing",
                RenderOptions.MsaaLabels, RenderOptions.MsaaIndex(RenderOptions.Msaa)), SettingRowIds.Msaa);
            _bloomRow = Add(page, new ToggleRow("Bloom", RenderOptions.Bloom), SettingRowIds.Bloom);
            _dynamicVisualLightsRow = Add(page,
                new ToggleRow("Dynamic lighting", RenderOptions.DynamicVisualLights), SettingRowIds.DynamicLighting);
            _graphicsPresetNote = Explain(page, graphicsPresetCustom
                ? GraphicsPresetCustomDescription()
                : "The quality preset supplies defaults for the detail rows below; adjust them for a custom mix.");
            _graphicsPresetRow.Changed += (_, _) => ApplyGraphicsPresetToRows();
            _textureFilteringPresetRow.Changed += (_, _) => MarkGraphicsPresetCustom();
            _anisotropyRow.Changed += (_, _) => MarkGraphicsPresetCustom();
            _msaaRow.Changed += (_, _) => MarkGraphicsPresetCustom();
            _bloomRow.Changed += (_, _) => MarkGraphicsPresetCustom();
            _dynamicVisualLightsRow.Changed += (_, _) => MarkGraphicsPresetCustom();
            _lightingRow = Add(page, new ToggleRow("Lighting", RenderOptions.Lighting), SettingRowIds.Lighting);
            _fogRow = Add(page, new ToggleRow("Fog", RenderOptions.Fog), SettingRowIds.Fog);
            _fpsRow = Add(page, new ToggleRow("FPS counter", RenderOptions.ShowFps), SettingRowIds.FpsCounter);

            Heading(page, "Cel shading");
            _celRow = Add(page, new ToggleRow("Cel shading", RenderOptions.CelShading), SettingRowIds.CelShading);

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
            _proHud = Add(page, new ToggleRow("Pro mode HUD", Features.ProHud), SettingRowIds.ProHud);
            _proHudWeaponRow = Add(page, new ChoiceRow("Weapon", new[] { "Static", "Dynamic" },
                Features.ProHudFixedWeapon ? 0 : 1), SettingRowIds.ProHudWeapon);
            _hitMarkerRow = Add(page, new ChoiceRow("Hit markers", new[] { "Off", "Visual", "Visual + audio" }, (int)Combat.CombatFeedbackSettings.HitMarkers), SettingRowIds.HitMarkers);
            _hitMarkerTimingRow = Add(page, new ChoiceRow("Hit marker timing",
                new[] { "Confirmed", "Instant" }, (int)Combat.CombatFeedbackSettings.Timing), SettingRowIds.HitMarkerTiming);
            _headshotCueRow = Add(page, new ToggleRow("Headshot cue", Combat.CombatFeedbackSettings.HeadshotCue), SettingRowIds.HeadshotCue);
            _killConfirmationRow = Add(page, new ToggleRow("Kill confirmation", Combat.CombatFeedbackSettings.KillConfirmation), SettingRowIds.KillConfirmation);
            _killcamRow = Add(page, new ToggleRow("Killcam", GameSettings.KillcamEnabled), SettingRowIds.Killcam);
            _radarStyleRow = Add(page, new ChoiceRow("Radar style", new[] { "Classic", "Enhanced" }, (int)global::MphRead.Hud.Radar.RadarSettings.Style), SettingRowIds.RadarStyle);
            _radarOrientationRow = Add(page, new ChoiceRow("Radar orientation", new[] { "Heading", "North" }, (int)global::MphRead.Hud.Radar.RadarSettings.Orientation), SettingRowIds.RadarOrientation);
            _radarPositionRow = Add(page, new ChoiceRow("Radar position",
                new[] { "Top Right", "Top Left", "Bottom Right", "Bottom Left", "Custom" },
                Math.Clamp((int)global::MphRead.Hud.Radar.RadarSettings.Anchor, 0, 4)), SettingRowIds.RadarAnchor);
            _radarScaleRow = Add(page, new SliderRow("Radar scale",
                RadarScaleToSlider(global::MphRead.Hud.Radar.RadarSettings.Scale),
                v => SliderToRadarScale(v).ToString("0.00", CultureInfo.InvariantCulture) + "x"), SettingRowIds.RadarScale);
            _radarOffsetXRow = Add(page, new SliderRow("Radar horizontal offset",
                (int)Math.Round(global::MphRead.Hud.Radar.RadarSettings.OffsetX),
                v => v.ToString(CultureInfo.InvariantCulture), min: -256, max: 256, keyStep: 8), SettingRowIds.RadarOffsetX);
            _radarOffsetYRow = Add(page, new SliderRow("Radar vertical offset",
                (int)Math.Round(global::MphRead.Hud.Radar.RadarSettings.OffsetY),
                v => v.ToString(CultureInfo.InvariantCulture), min: -192, max: 192, keyStep: 8), SettingRowIds.RadarOffsetY);
            var resetRadar = new MenuEntry("Reset radar position",
                "Top right, 1.00x scale and zero offsets", titleSize: 13)
            {
                Height = 42,
                Accent = GuiTheme.Warm,
                Margin = new Thickness(0, 4, 0, 0)
            };
            resetRadar.Click += (_, _) => ResetRadarPosition();
            Add(page, resetRadar, SettingRowIds.RadarAnchor + ".reset");
            _radarStyleRow.Preview = (context, area) => DrawRadarPreview(context, area,
                (global::MphRead.Hud.Radar.RadarStyle)_radarStyleRow.Index,
                (global::MphRead.Hud.Radar.RadarOrientation)_radarOrientationRow.Index);
            // The crosshair questions belong to Pro mode and nothing else --
            // the DS HUD draws its own reticle sprite and has no use for
            // them -- so they are only asked while it is on. Shown rather than
            // greyed: a row that cannot be answered is still a row to read
            // past, and this page is long enough.
            _crosshairSizeRow = Add(page, new ChoiceRow("Crosshair size",
                Crosshair.SizeNames, (int)Crosshair.Size), SettingRowIds.CrosshairSize);
            _crosshairStyleRow = Add(page, new ChoiceRow("Crosshair type",
                Crosshair.StyleNames, (int)Crosshair.Style), SettingRowIds.CrosshairStyle);
            _reticleOpacity = Add(page, new SliderRow("Reticle opacity",
                OpacityToSlider(Features.ReticleOpacity),
                v => $"{SliderToOpacity(v) * 100:0}%"), SettingRowIds.ReticleOpacity);
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
            if (_graphicsPresetRow.Index == CustomGraphicsPresetIndex)
            {
                UpdateGraphicsPresetDescription(custom: true);
                return;
            }
            GraphicsPreset preset = (GraphicsPreset)_graphicsPresetRow.Index;
            _graphicsPresetBase = preset;
            _refreshingGraphicsRows = true;
            try
            {
                _textureFilteringPresetRow.Index = (int)RenderOptions.TextureFilteringFor(preset);
                _anisotropyRow.Index = RenderOptions.AnisotropyIndex(RenderOptions.AnisotropyFor(preset));
                _msaaRow.Index = RenderOptions.MsaaIndex(RenderOptions.MsaaFor(preset));
                _bloomRow.On = RenderOptions.BloomFor(preset);
                _dynamicVisualLightsRow.On = RenderOptions.DynamicVisualLightsFor(preset);
            }
            finally
            {
                _refreshingGraphicsRows = false;
            }
            UpdateGraphicsPresetDescription(custom: false);
        }

        private void MarkGraphicsPresetCustom()
        {
            if (_refreshingGraphicsRows
                || _graphicsPresetRow.Index == CustomGraphicsPresetIndex)
            {
                return;
            }
            _graphicsPresetRow.Index = CustomGraphicsPresetIndex;
            UpdateGraphicsPresetDescription(custom: true);
        }

        private void UpdateGraphicsPresetDescription(bool custom)
        {
            _graphicsPresetNote.Text = custom
                ? GraphicsPresetCustomDescription()
                : "The quality preset supplies defaults for the detail rows below; adjust them for a custom mix.";
        }

        private string GraphicsPresetCustomDescription()
            => $"Custom detail overrides are active; {_graphicsPresetBase} remains the compatibility baseline when saved.";

        private static bool GraphicsQualityHasOverrides(GraphicsPreset preset)
        {
            return RenderOptions.TextureFilteringPreset != RenderOptions.TextureFilteringFor(preset)
                || RenderOptions.Anisotropy != RenderOptions.AnisotropyFor(preset)
                || RenderOptions.Msaa != RenderOptions.MsaaFor(preset)
                || RenderOptions.Bloom != RenderOptions.BloomFor(preset)
                || RenderOptions.DynamicVisualLights != RenderOptions.DynamicVisualLightsFor(preset);
        }

        private void ResetRadarPosition()
        {
            _radarPositionRow.Index = (int)global::MphRead.Hud.Radar.RadarAnchor.TopRight;
            _radarScaleRow.Value = RadarScaleToSlider(1f);
            _radarOffsetXRow.Value = 0;
            _radarOffsetYRow.Value = 0;
        }

        private void ShowProHudRows()
        {
            bool enabled = Features.ShowProHudWeaponSetting(_proHud.On);
            // Keep dependent settings discoverable and explain their state by
            // rendering them disabled instead of making the page jump while
            // the master HUD switch changes.
            _proHudWeaponRow.IsVisible = true;
            _crosshairSizeRow.IsVisible = true;
            _crosshairStyleRow.IsVisible = true;
            _proHudWeaponRow.IsEnabled = enabled;
            _crosshairSizeRow.IsEnabled = enabled;
            _crosshairStyleRow.IsEnabled = enabled;
        }

        private void ShowOuterBoostRows()
        {
            bool enabled = _gamepadOuterBoost.On;
            _gamepadOuterBoostStart.IsEnabled = enabled;
            _gamepadOuterYawBoost.IsEnabled = enabled;
            _gamepadOuterPitchBoost.IsEnabled = enabled;
            _gamepadBoostDelay.IsEnabled = enabled;
            _gamepadBoostRamp.IsEnabled = enabled;
        }

        private void SyncControllerThresholdPair(SliderRow activation, SliderRow release)
        {
            if (_refreshingControllerRows || _synchronizingControllerPairs)
            {
                return;
            }
            if (release.Value <= activation.Value)
            {
                return;
            }
            _synchronizingControllerPairs = true;
            try
            {
                // The runtime setters use the activation/press value as the
                // upper bound for release. Mirror that rule immediately in
                // the two rows so the saved pair cannot surprise the player.
                release.Value = activation.Value;
            }
            finally
            {
                _synchronizingControllerPairs = false;
            }
        }

        private void MarkControllerCustom()
        {
            if (!_refreshingControllerRows
                && _gamepadPresetRow.Index != (int)Mods.Input.ControllerPreset.Custom)
            {
                _gamepadPresetRow.Index = (int)Mods.Input.ControllerPreset.Custom;
            }
        }

        private void MarkControllerSliderEdited(SliderRow row)
        {
            if (!_refreshingControllerRows)
            {
                _editedControllerSliders.Add(row);
            }
            MarkControllerCustom();
        }

        private bool ControllerSliderWasEdited(SliderRow row)
            => _editedControllerSliders.Contains(row);

        private void ClearEditedControllerSliders(params SliderRow[] rows)
        {
            foreach (SliderRow row in rows)
            {
                _editedControllerSliders.Remove(row);
            }
        }

        private void RefreshControllerPresetRow()
        {
            bool wasRefreshing = _refreshingControllerRows;
            _refreshingControllerRows = true;
            try
            {
                _gamepadPresetRow.Index = (int)InputSettings.ControllerPreset;
            }
            finally
            {
                _refreshingControllerRows = wasRefreshing;
            }
        }

        private void RefreshControllerRows()
        {
            _refreshingControllerRows = true;
            try
            {
                _gamepadPresetRow.Index = (int)InputSettings.ControllerPreset;
                RefreshControllerAimRowsCore();
                RefreshControllerAdvancedRowsCore();
                _gamepadHaptics.On = InputSettings.GamepadHapticsEnabled;
                _gamepadHapticsStrength.Value = (int)Math.Round(
                    InputSettings.GamepadHapticsStrength * 100);
                _inputBalanceTelemetry.On = InputSettings.InputBalanceTelemetryEnabled;
            }
            finally
            {
                _refreshingControllerRows = false;
            }
            // Every controller slider was repopulated from authoritative
            // state, so no old UI edit marker may survive this full reset.
            _editedControllerSliders.Clear();
        }

        private void RefreshControllerAimRows()
        {
            _refreshingControllerRows = true;
            try
            {
                RefreshControllerAimRowsCore();
            }
            finally
            {
                _refreshingControllerRows = false;
            }
            ClearEditedControllerSliders(
                _gamepadHorizontalSensitivity, _gamepadVerticalSensitivity,
                _gamepadGyroSensitivity, _gamepadDeadZone, _gamepadLookDeadZone,
                _gamepadMoveActivate, _gamepadMoveRelease, _gamepadLook,
                _gamepadYawRate, _gamepadPitchRate, _gamepadZoomMultiplier);
        }

        private void RefreshControllerGeneralRows()
        {
            _refreshingControllerRows = true;
            try
            {
                _gamepadHorizontalSensitivity.Value = LookToSlider(
                    InputSettings.GamepadHorizontalSensitivity);
                _gamepadVerticalSensitivity.Value = LookToSlider(
                    InputSettings.GamepadVerticalSensitivity);
                _gamepadInvertY.On = InputSettings.GamepadInvertY;
                _gamepadZoomMultiplier.Value = LookToSlider(InputSettings.GamepadZoomMultiplier);
                _gamepadHaptics.On = InputSettings.GamepadHapticsEnabled;
                _gamepadHapticsStrength.Value = (int)Math.Round(
                    InputSettings.GamepadHapticsStrength * 100);
            }
            finally
            {
                _refreshingControllerRows = false;
            }
            ClearEditedControllerSliders(_gamepadHorizontalSensitivity,
                _gamepadVerticalSensitivity, _gamepadZoomMultiplier,
                _gamepadHapticsStrength);
        }

        private void RefreshControllerGyroRows()
        {
            _refreshingControllerRows = true;
            try
            {
                _gamepadGyro.Index = (int)InputSettings.GamepadGyroMode;
                _gamepadGyroActivation.Index = (int)InputSettings.GamepadGyroActivation;
                _gamepadGyroSensitivity.Value = LookToSlider(InputSettings.GamepadGyroSensitivity);
                _gamepadGyroInvertX.On = InputSettings.GamepadGyroInvertX;
                _gamepadGyroInvertY.On = InputSettings.GamepadGyroInvertY;
            }
            finally
            {
                _refreshingControllerRows = false;
            }
            ClearEditedControllerSliders(_gamepadGyroSensitivity);
        }

        private void RefreshControllerAimRowsCore()
        {
            _gamepadHorizontalSensitivity.Value = LookToSlider(InputSettings.GamepadHorizontalSensitivity);
            _gamepadVerticalSensitivity.Value = LookToSlider(InputSettings.GamepadVerticalSensitivity);
            _gamepadInvertY.On = InputSettings.GamepadInvertY;
            _gamepadGyro.Index = (int)InputSettings.GamepadGyroMode;
            _gamepadGyroActivation.Index = (int)InputSettings.GamepadGyroActivation;
            _gamepadGyroSensitivity.Value = LookToSlider(InputSettings.GamepadGyroSensitivity);
            _gamepadGyroInvertX.On = InputSettings.GamepadGyroInvertX;
            _gamepadGyroInvertY.On = InputSettings.GamepadGyroInvertY;
            _gamepadDeadZone.Value = DeadZoneToSlider(InputSettings.GamepadMoveDeadZone, .9f);
            _gamepadLookDeadZone.Value = DeadZoneToSlider(InputSettings.GamepadLookDeadZone, .9f);
            _gamepadMoveActivate.Value = ThresholdToSlider(InputSettings.GamepadMoveActivateThreshold);
            _gamepadMoveRelease.Value = ThresholdToSlider(InputSettings.GamepadMoveReleaseThreshold);
            _gamepadLook.Value = ExponentToSlider(InputSettings.GamepadLookExponent);
            _gamepadResponseCurve.Index = (int)InputSettings.GamepadResponseCurve;
            _gamepadYawRate.Value = (int)Math.Round(InputSettings.GamepadYawRate);
            _gamepadPitchRate.Value = (int)Math.Round(InputSettings.GamepadPitchRate);
            _gamepadZoomMultiplier.Value = LookToSlider(InputSettings.GamepadZoomMultiplier);
        }

        private void RefreshControllerAdvancedRows()
        {
            _refreshingControllerRows = true;
            try
            {
                RefreshControllerAdvancedRowsCore();
            }
            finally
            {
                _refreshingControllerRows = false;
            }
            ClearEditedControllerSliders(
                _gamepadDeadZone, _gamepadLookDeadZone, _gamepadOuterDeadZone,
                _gamepadMoveActivate, _gamepadMoveRelease, _gamepadLook,
                _gamepadYawRate, _gamepadPitchRate, _gamepadOuterBoostStart,
                _gamepadOuterYawBoost, _gamepadOuterPitchBoost, _gamepadBoostDelay,
                _gamepadBoostRamp, _gamepadTriggerPress, _gamepadTriggerRelease);
        }

        private void RefreshControllerAdvancedRowsCore()
        {
            _gamepadDeadZone.Value = DeadZoneToSlider(InputSettings.GamepadMoveDeadZone, .9f);
            _gamepadLookDeadZone.Value = DeadZoneToSlider(InputSettings.GamepadLookDeadZone, .9f);
            _gamepadMoveActivate.Value = ThresholdToSlider(InputSettings.GamepadMoveActivateThreshold);
            _gamepadMoveRelease.Value = ThresholdToSlider(InputSettings.GamepadMoveReleaseThreshold);
            _gamepadLook.Value = ExponentToSlider(InputSettings.GamepadLookExponent);
            _gamepadYawRate.Value = (int)Math.Round(InputSettings.GamepadYawRate);
            _gamepadPitchRate.Value = (int)Math.Round(InputSettings.GamepadPitchRate);
            _gamepadOuterDeadZone.Value = DeadZoneToSlider(InputSettings.GamepadOuterDeadZone, .5f);
            _gamepadOuterBoost.On = InputSettings.GamepadOuterBoostEnabled;
            _gamepadTurnAcceleration.Index = (int)InputSettings.GamepadTurnAcceleration;
            _gamepadOuterBoostStart.Value = ThresholdToSlider(InputSettings.GamepadOuterBoostStart);
            _gamepadOuterYawBoost.Value = (int)Math.Round(InputSettings.GamepadOuterYawBoost);
            _gamepadOuterPitchBoost.Value = (int)Math.Round(InputSettings.GamepadOuterPitchBoost);
            _gamepadBoostDelay.Value = BoostSecondsToSlider(InputSettings.GamepadBoostDelaySeconds);
            _gamepadBoostRamp.Value = BoostSecondsToSlider(InputSettings.GamepadBoostRampSeconds, .001f);
            _gamepadTriggerPress.Value = ThresholdToSlider(InputSettings.GamepadTriggerPressThreshold);
            _gamepadTriggerRelease.Value = ThresholdToSlider(InputSettings.GamepadTriggerReleaseThreshold);
            ShowOuterBoostRows();
        }

        private void RefreshControlRows()
        {
            _sensitivity.Value = SensitivityToSlider(InputSettings.MouseSensitivity);
            _invertY.On = InputSettings.InvertMouseY;
            _invertX.On = InputSettings.InvertMouseX;
            _scrollAllWeapons.On = InputSettings.ScrollAllWeapons;
            _morphBallMouseFlickBoost.On = InputSettings.MorphBallMouseFlickBoost;
            _morphBallStickFlickBoost.On = InputSettings.MorphBallStickFlickBoost;
            if (_morphBallSwipeBoost != null)
            {
                _morphBallSwipeBoost.On = InputSettings.MorphBallSwipeBoost;
            }
            RefreshControllerRows();
            if (_touchButtonsRow != null)
            {
                _touchButtonsRow.On = Mods.Input.TouchSettings.ButtonsVisible;
                foreach ((Mods.Input.TouchControl control, ToggleRow row) in _touchRows)
                {
                    row.On = Mods.Input.TouchSettings.IsEnabled(control);
                }
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
        }

        private static void DrawRadarPreview(DrawingContext context, Rect area,
            global::MphRead.Hud.Radar.RadarStyle style,
            global::MphRead.Hud.Radar.RadarOrientation orientation)
        {
            double radius = Math.Min(area.Width, area.Height) * .34;
            Point center = area.Center;
            var pen = new Pen(GuiTheme.TextDimBrush, 1);
            context.DrawEllipse(null, pen, center, radius, radius);
            context.DrawEllipse(null, new Pen(GuiTheme.EdgeBrush, 1), center,
                radius * .66, radius * .66);
            double angle = orientation == global::MphRead.Hud.Radar.RadarOrientation.North
                ? -Math.PI / 2 : 0;
            Point heading = new(center.X + Math.Cos(angle) * radius,
                center.Y + Math.Sin(angle) * radius);
            context.DrawLine(new Pen(GuiTheme.AccentBrush, 2), center, heading);
            if (style == global::MphRead.Hud.Radar.RadarStyle.Enhanced)
            {
                context.DrawEllipse(GuiTheme.WarmBrush, null,
                    new Point(center.X + radius * .4, center.Y - radius * .25), 2.5, 2.5);
                context.DrawEllipse(GuiTheme.GoodBrush, null,
                    new Point(center.X - radius * .5, center.Y + radius * .2), 2.5, 2.5);
            }
            context.DrawEllipse(GuiTheme.AccentBrush, null, center, 3, 3);
        }

        // --------------------------------------------------------------- audio

        private void BuildAudio()
        {
            StackPanel page = AddSection("Audio");
            Heading(page, "Volume");
            _feedbackVolume = Add(page, new SliderRow("Combat feedback", (int)(Combat.FeedbackAudio.Volume * 100), v => $"{v}%"), SettingRowIds.FeedbackVolume);
            _sfxVolume = Add(page, new SliderRow("Sound effects",
                Percent(_settings.SfxVolume, 35)), SettingRowIds.SfxVolume);
            _musicVolume = Add(page, new SliderRow("Music", Percent(_settings.MusicVolume, 50)), SettingRowIds.MusicVolume);
            Heading(page, "Presentation packs");
            ClientPresentationContentState content = ClientPresentationContent.Refresh();
            _announcerPacks = ClientPresentationContent.Packs(content, OptionalPresentationKind.Announcer);
            _musicPacks = ClientPresentationContent.Packs(content, OptionalPresentationKind.Music);
            _announcerPackRow = Add(page, new ChoiceRow("Announcer",
                PackLabels(_announcerPacks), SelectedPackIndex(_announcerPacks, LauncherPrefs.AnnouncerPack)), SettingRowIds.AnnouncerPack);
            _musicPackRow = Add(page, new ChoiceRow("Music pack",
                PackLabels(_musicPacks), SelectedPackIndex(_musicPacks, LauncherPrefs.MusicPack)), SettingRowIds.MusicPack);
            Explain(page, $"Data-only packs are discovered in {LauncherPrefs.OptionalContentDirectory}. "
                + "Missing or invalid selections use built-in presentation; changes apply to the next match.");
            Heading(page, "Language");
            string[] languages = Enum.GetNames<Language>();
            _languageRow = Add(page, new ChoiceRow("Text", languages,
                Math.Max(0, Array.IndexOf(languages, _settings.Language))), SettingRowIds.Language);
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
                v => $"{SliderToSensitivity(v).ToString("0.00", CultureInfo.InvariantCulture)}x"), SettingRowIds.MouseSensitivity);
            _invertY = Add(page, new ToggleRow("Invert vertical aim", InputSettings.InvertMouseY), SettingRowIds.MouseInvertY);
            _invertX = Add(page, new ToggleRow("Invert horizontal aim", InputSettings.InvertMouseX), SettingRowIds.MouseInvertX);
            _scrollAllWeapons = Add(page, new ToggleRow("Wheel cycles every weapon",
                InputSettings.ScrollAllWeapons), SettingRowIds.ScrollAllWeapons);
            _morphBallMouseFlickBoost = Add(page, new ToggleRow(
                "Morph Ball mouse flick boost", InputSettings.MorphBallMouseFlickBoost),
                SettingRowIds.MorphBallMouseFlickBoost);

            _chatKeyRow = Add(page, new KeyRow("Chat key",
                () => new MphRead.Entities.Keybind(InputSettings.ChatKey),
                (type, key, _) =>
                {
                    if (type == ButtonType.Key)
                    {
                        InputSettings.ChatKey = key;
                    }
                }), SettingRowIds.ChatKey);

            BuildTouchControls(page);
            if (OperatingSystem.IsAndroid())
            {
                _morphBallSwipeBoost = Add(page, new ToggleRow(
                    "Morph Ball swipe boost", InputSettings.MorphBallSwipeBoost),
                    SettingRowIds.MorphBallSwipeBoost);
            }
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
                (int)InputSettings.ControllerPreset), SettingRowIds.ControllerPreset);
            _morphBallStickFlickBoost = Add(page, new ToggleRow(
                "Morph Ball stick flick boost", InputSettings.MorphBallStickFlickBoost),
                SettingRowIds.MorphBallStickFlickBoost);
            _gamepadHorizontalSensitivity = Add(page, new SliderRow("Horizontal sensitivity",
                LookToSlider(InputSettings.GamepadHorizontalSensitivity),
                v => $"{SliderToLook(v).ToString("0.00", CultureInfo.InvariantCulture)}x"), SettingRowIds.ControllerHorizontalSensitivity);
            _gamepadVerticalSensitivity = Add(page, new SliderRow("Vertical sensitivity",
                LookToSlider(InputSettings.GamepadVerticalSensitivity),
                v => $"{SliderToLook(v).ToString("0.00", CultureInfo.InvariantCulture)}x"), SettingRowIds.ControllerVerticalSensitivity);
            _gamepadInvertY = Add(page, new ToggleRow("Invert vertical aim (stick)",
                InputSettings.GamepadInvertY), SettingRowIds.ControllerInvertY);
            _gamepadZoomMultiplier = Add(page, new SliderRow("Zoom multiplier",
                LookToSlider(InputSettings.GamepadZoomMultiplier),
                v => $"{SliderToLook(v).ToString("0.00", CultureInfo.InvariantCulture)}x"), SettingRowIds.ControllerZoom);
            var resetAim = new MenuEntry("Reset aim settings",
                "Restore controller aim defaults without changing bindings", titleSize: 13)
            {
                Height = 42,
                Accent = GuiTheme.Warm,
                Margin = new Thickness(0, 4, 0, 0)
            };
            resetAim.Click += (_, _) =>
            {
                InputSettings.ResetControllerAimSettings();
                RefreshControllerAimRows();
            };
            Add(page, resetAim, SettingRowIds.ControllerHorizontalSensitivity + ".reset");

            var resetGeneral = new MenuEntry("Reset controller general",
                "Restore sensitivity, invert, zoom and haptics without changing bindings or tuning", titleSize: 13)
            {
                Height = 42,
                Accent = GuiTheme.Warm,
                Margin = new Thickness(0, 4, 0, 0)
            };
            resetGeneral.Click += (_, _) =>
            {
                InputSettings.ResetControllerGeneral();
                RefreshControllerGeneralRows();
            };
            Add(page, resetGeneral, SettingRowIds.ControllerHorizontalSensitivity + ".reset-general");

            StackPanel sectionPage = page;
            var advancedPage = new StackPanel { Spacing = 10 };
            _advancedControllerExpander = new Expander
            {
                Header = "Gamepad advanced",
                Content = advancedPage,
                // The capture seam controls only the initial presentation of
                // this real production section. Live settings start collapsed
                // to keep the common controller path readable; once opened,
                // the expander retains that choice for this view lifetime.
                IsExpanded = _captureAdvancedControllerExpanded ?? false
            };
            ActiveSector(sectionPage).Children.Add(_advancedControllerExpander);
            page = advancedPage;
            Heading(page, "Gyro");
            _gamepadGyro = Add(page, new ChoiceRow("Gyro aiming",
                new[] { "Off", "Always", "Zoom Only", "Hold Button" },
                (int)InputSettings.GamepadGyroMode), SettingRowIds.ControllerGyro);
            _gamepadGyroActivation = Add(page, new ChoiceRow("Gyro hold button",
                new[] { "Left Trigger", "Left Bumper", "Right Thumb" },
                (int)InputSettings.GamepadGyroActivation),
                SettingRowIds.ControllerGyroActivation);
            _gamepadGyroSensitivity = Add(page, new SliderRow("Gyro sensitivity",
                LookToSlider(InputSettings.GamepadGyroSensitivity),
                v => $"{SliderToLook(v).ToString("0.00", CultureInfo.InvariantCulture)}x"), SettingRowIds.ControllerGyroSensitivity);
            _gamepadGyroInvertX = Add(page, new ToggleRow("Invert gyro horizontal aim",
                InputSettings.GamepadGyroInvertX), SettingRowIds.ControllerGyroInvertX);
            _gamepadGyroInvertY = Add(page, new ToggleRow("Invert gyro vertical aim",
                InputSettings.GamepadGyroInvertY), SettingRowIds.ControllerGyroInvertY);
            _gyroCapabilityNote = Explain(page, "Checking controller gyro capability…", GuiTheme.Warm);
            var recalibrateGyro = new MenuEntry("Recalibrate gyro",
                "Keep the controller still while a fresh bias is measured", titleSize: 13)
            {
                Height = 42,
                Accent = GuiTheme.Warm,
                Margin = new Thickness(0, 4, 0, 0)
            };
            recalibrateGyro.Click += (_, _) =>
            {
                Mods.Input.GamepadGyro.Recalibrate();
                _gyroCapabilityNote.Text = "Calibrating gyro… keep the controller still.";
            };
            Add(page, recalibrateGyro, SettingRowIds.ControllerGyro + ".recalibrate");
            var resetGyro = new MenuEntry("Reset gyro",
                "Disable gyro and restore sensor sensitivity and inversion", titleSize: 13)
            {
                Height = 42,
                Accent = GuiTheme.Warm,
                Margin = new Thickness(0, 4, 0, 0)
            };
            resetGyro.Click += (_, _) =>
            {
                InputSettings.ResetControllerGyro();
                RefreshControllerGyroRows();
            };
            Add(page, resetGyro, SettingRowIds.ControllerGyro + ".reset");
            Heading(page, "Feedback and diagnostics");
            _gamepadHaptics = Add(page, new ToggleRow("Rumble and haptics",
                InputSettings.GamepadHapticsEnabled), SettingRowIds.ControllerHaptics);
            _gamepadHapticsStrength = Add(page, new SliderRow("Vibration strength",
                (int)Math.Round(InputSettings.GamepadHapticsStrength * 100),
                v => $"{v}%"), SettingRowIds.ControllerHapticsStrength);
            _inputBalanceTelemetry = Add(page, new ToggleRow(
                "Local input-balance diagnostics",
                InputSettings.InputBalanceTelemetryEnabled), SettingRowIds.ControllerTelemetry);
            Explain(page, "Diagnostics stay on this device and contain no account or session identity.");
            Heading(page, "Stick response");
            _gamepadDeadZone = Add(page, new SliderRow("Move dead zone",
                DeadZoneToSlider(InputSettings.GamepadMoveDeadZone, .9f),
                v => $"{SliderToDeadZone(v, .9f).ToString("0.00", CultureInfo.InvariantCulture)}"), SettingRowIds.ControllerMoveDeadZone);
            _gamepadLookDeadZone = Add(page, new SliderRow("Look dead zone",
                DeadZoneToSlider(InputSettings.GamepadLookDeadZone, .9f),
                v => $"{SliderToDeadZone(v, .9f).ToString("0.00", CultureInfo.InvariantCulture)}"), SettingRowIds.ControllerLookDeadZone);
            _gamepadOuterDeadZone = Add(page, new SliderRow("Outer dead zone",
                DeadZoneToSlider(InputSettings.GamepadOuterDeadZone, .5f),
                v => $"{SliderToDeadZone(v, .5f).ToString("0.00", CultureInfo.InvariantCulture)}"), SettingRowIds.ControllerOuterDeadZone);
            Heading(page, "Movement thresholds");
            _gamepadMoveActivate = Add(page, new SliderRow("Move activation threshold",
                ThresholdToSlider(InputSettings.GamepadMoveActivateThreshold),
                ThresholdLabel), SettingRowIds.ControllerMoveActivate);
            _gamepadMoveRelease = Add(page, new SliderRow("Move release threshold",
                ThresholdToSlider(InputSettings.GamepadMoveReleaseThreshold),
                ThresholdLabel), SettingRowIds.ControllerMoveRelease);
            Heading(page, "Look response");
            _gamepadResponseCurve = Add(page, new ChoiceRow("Response curve",
                Enum.GetNames<Mods.Input.GamepadResponseCurvePreset>(),
                (int)InputSettings.GamepadResponseCurve),
                SettingRowIds.ControllerResponseCurve);
            _gamepadLook = Add(page, new SliderRow("Response exponent",
                ExponentToSlider(InputSettings.GamepadLookExponent),
                v => $"{SliderToExponent(v).ToString("0.00", CultureInfo.InvariantCulture)}"), SettingRowIds.ControllerExponent);
            _gamepadYawRate = Add(page, new SliderRow("Yaw rate",
                (int)Math.Round(InputSettings.GamepadYawRate),
                v => $"{v}°/s", min: 0, max: 2000, keyStep: 25), SettingRowIds.ControllerYawRate);
            _gamepadPitchRate = Add(page, new SliderRow("Pitch rate",
                (int)Math.Round(InputSettings.GamepadPitchRate),
                v => $"{v}°/s", min: 0, max: 2000, keyStep: 25), SettingRowIds.ControllerPitchRate);
            Heading(page, "Outer ring");
            _gamepadTurnAcceleration = Add(page, new ChoiceRow("Turn acceleration",
                Enum.GetNames<Mods.Input.GamepadTurnAccelerationPreset>(),
                (int)InputSettings.GamepadTurnAcceleration),
                SettingRowIds.ControllerTurnAcceleration);
            _gamepadOuterBoost = Add(page, new ToggleRow("Outer-ring boost",
                InputSettings.GamepadOuterBoostEnabled), SettingRowIds.ControllerOuterBoost);
            _gamepadOuterBoostStart = Add(page, new SliderRow("Outer boost threshold",
                ThresholdToSlider(InputSettings.GamepadOuterBoostStart),
                ThresholdLabel), SettingRowIds.ControllerOuterBoostStart);
            _gamepadOuterYawBoost = Add(page, new SliderRow("Outer yaw boost",
                (int)Math.Round(InputSettings.GamepadOuterYawBoost),
                v => $"{v}°/s", min: 0, max: 2000, keyStep: 25), SettingRowIds.ControllerOuterYawBoost);
            _gamepadOuterPitchBoost = Add(page, new SliderRow("Outer pitch boost",
                (int)Math.Round(InputSettings.GamepadOuterPitchBoost),
                v => $"{v}°/s", min: 0, max: 2000, keyStep: 25), SettingRowIds.ControllerOuterPitchBoost);
            _gamepadBoostDelay = Add(page, new SliderRow("Outer boost delay",
                BoostSecondsToSlider(InputSettings.GamepadBoostDelaySeconds),
                v => $"{BoostSliderToSeconds(v):0.000}s", min: 0, max: 10_000, keyStep: 10), SettingRowIds.ControllerBoostDelay);
            _gamepadBoostRamp = Add(page, new SliderRow("Outer boost ramp",
                BoostSecondsToSlider(InputSettings.GamepadBoostRampSeconds, .001f),
                v => $"{BoostSliderToSeconds(v, .001f):0.000}s", min: 1, max: 10_000, keyStep: 10), SettingRowIds.ControllerBoostRamp);
            Heading(page, "Triggers");
            _gamepadTriggerPress = Add(page, new SliderRow("Trigger press",
                ThresholdToSlider(InputSettings.GamepadTriggerPressThreshold),
                ThresholdLabel), SettingRowIds.ControllerTriggerPress);
            _gamepadTriggerRelease = Add(page, new SliderRow("Trigger release",
                ThresholdToSlider(InputSettings.GamepadTriggerReleaseThreshold),
                ThresholdLabel), SettingRowIds.ControllerTriggerRelease);
            _gamepadOuterBoost.Changed += (_, _) =>
            {
                ShowOuterBoostRows();
                MarkControllerCustom();
            };
            ShowOuterBoostRows();

            _gamepadMoveActivate.ValueChanged += (_, _) =>
                SyncControllerThresholdPair(_gamepadMoveActivate, _gamepadMoveRelease);
            _gamepadMoveRelease.ValueChanged += (_, _) =>
                SyncControllerThresholdPair(_gamepadMoveActivate, _gamepadMoveRelease);
            _gamepadTriggerPress.ValueChanged += (_, _) =>
                SyncControllerThresholdPair(_gamepadTriggerPress, _gamepadTriggerRelease);
            _gamepadTriggerRelease.ValueChanged += (_, _) =>
                SyncControllerThresholdPair(_gamepadTriggerPress, _gamepadTriggerRelease);
            _gamepadResponseCurve.Changed += (_, _) =>
            {
                if (_refreshingControllerRows) return;
                var preset = (Mods.Input.GamepadResponseCurvePreset)
                    _gamepadResponseCurve.Index;
                if (preset == Mods.Input.GamepadResponseCurvePreset.Custom) return;
                InputSettings.GamepadResponseCurve = preset;
                _gamepadLook.Value = ExponentToSlider(InputSettings.GamepadLookExponent);
            };
            _gamepadTurnAcceleration.Changed += (_, _) =>
            {
                if (_refreshingControllerRows) return;
                InputSettings.GamepadTurnAcceleration =
                    (Mods.Input.GamepadTurnAccelerationPreset)
                    _gamepadTurnAcceleration.Index;
                _gamepadOuterBoost.On = InputSettings.GamepadOuterBoostEnabled;
                _gamepadOuterBoostStart.Value = ThresholdToSlider(
                    InputSettings.GamepadOuterBoostStart);
                _gamepadOuterYawBoost.Value = (int)Math.Round(
                    InputSettings.GamepadOuterYawBoost);
                _gamepadOuterPitchBoost.Value = (int)Math.Round(
                    InputSettings.GamepadOuterPitchBoost);
                _gamepadBoostDelay.Value = BoostSecondsToSlider(
                    InputSettings.GamepadBoostDelaySeconds);
                _gamepadBoostRamp.Value = BoostSecondsToSlider(
                    InputSettings.GamepadBoostRampSeconds, .001f);
                ShowOuterBoostRows();
            };

            var resetAdvanced = new MenuEntry("Reset advanced tuning",
                "Restore stick, movement, look, outer-ring and trigger response defaults", titleSize: 13)
            {
                Height = 42,
                Accent = GuiTheme.Warm,
                Margin = new Thickness(0, 4, 0, 0)
            };
            resetAdvanced.Click += (_, _) =>
            {
                InputSettings.ResetControllerAdvancedTuning();
                RefreshControllerAdvancedRows();
            };
            Add(page, resetAdvanced, SettingRowIds.ControllerOuterBoost + ".reset");

            foreach (SliderRow row in new[] { _gamepadHorizontalSensitivity,
                _gamepadVerticalSensitivity, _gamepadDeadZone,
                _gamepadLookDeadZone, _gamepadOuterDeadZone, _gamepadMoveActivate,
                _gamepadMoveRelease, _gamepadLook, _gamepadYawRate, _gamepadPitchRate,
                _gamepadOuterBoostStart, _gamepadOuterYawBoost, _gamepadOuterPitchBoost,
                _gamepadBoostDelay, _gamepadBoostRamp, _gamepadTriggerPress,
                _gamepadTriggerRelease, _gamepadZoomMultiplier,
                _gamepadHapticsStrength })
            {
                row.ValueChanged += (_, _) => MarkControllerSliderEdited(row);
            }
            foreach (ToggleRow row in new[] { _gamepadInvertY,
                _gamepadGyroInvertX, _gamepadGyroInvertY, _gamepadHaptics,
                _inputBalanceTelemetry })
            {
                row.Changed += (_, _) => MarkControllerCustom();
            }
            foreach (ChoiceRow row in new[] { _gamepadGyro,
                _gamepadGyroActivation, _gamepadResponseCurve,
                _gamepadTurnAcceleration })
            {
                row.Changed += (_, _) => MarkControllerCustom();
            }

            page = sectionPage;
            Heading(page, "Gamepad buttons");
            var padRows = new List<PadRow>();
            foreach (Mods.Input.PadAction action in Mods.Input.PadBindings.Actions)
            {
                PadRow row = Add(page, new PadRow(action), SettingRowIds.PadBinding(action.ToString()));
                row.Rebound += (_, _) => _gamepadPresetRow.Index = (int)Mods.Input.ControllerPreset.Custom;
                padRows.Add(row);
            }
            _gamepadPresetRow.Changed += (_, _) =>
            {
                Mods.Input.ControllerPreset preset =
                    (Mods.Input.ControllerPreset)_gamepadPresetRow.Index;
                InputSettings.ApplyPreset(preset);

                // A named preset is an explicit authoritative refresh boundary.
                // Slider edits mark the row Custom, so that transition must not
                // discard the still-pending response edits or their dirty markers.
                if (!_refreshingControllerRows && preset != Mods.Input.ControllerPreset.Custom)
                {
                    RefreshControllerRows();
                }
                foreach (PadRow row in padRows) row.InvalidateVisual();
            };

            var resetController = new MenuEntry("Reset controller defaults",
                "Restore all controller aim, tuning and button defaults", titleSize: 13)
            {
                Height = 42,
                Accent = GuiTheme.Warm,
                Margin = new Thickness(0, 4, 0, 0)
            };
            resetController.Click += (_, _) =>
            {
                InputSettings.ResetController();
                RefreshControllerRows();
                foreach (PadRow row in padRows) row.InvalidateVisual();
            };
            Add(page, resetController, SettingRowIds.ControllerPreset + ".reset-all");

            Heading(page, "Keys");
            var rows = new List<KeyRow>();
            foreach (PropertyInfo property in InputSettings.Bindings)
            {
                rows.Add(Add(page, new KeyRow(property), SettingRowIds.KeyBinding(property.Name)));
            }
            var reset = new MenuEntry("Reset bindings",
                "Restore keyboard, chat and controller bindings only", titleSize: 13)
            {
                Height = 42,
                Accent = GuiTheme.Warm,
                Margin = new Thickness(0, 8, 0, 0)
            };
            reset.Click += (_, _) =>
            {
                InputSettings.ResetBindings();
                RefreshControllerPresetRow();
                foreach (PadRow row in padRows)
                {
                    row.InvalidateVisual();
                }
                foreach (KeyRow row in rows)
                {
                    row.InvalidateVisual();
                }
                _chatKeyRow.InvalidateVisual();
            };
            Add(page, reset, SettingRowIds.ChatKey + ".reset");

            var resetAll = new MenuEntry("Reset all input settings",
                "Restore mouse, controller, touch, stylus and bindings", titleSize: 13)
            {
                Height = 42,
                Accent = GuiTheme.Warm,
                Margin = new Thickness(0, 4, 0, 0)
            };
            resetAll.Click += (_, _) =>
            {
                InputSettings.Reset();
                RefreshControlRows();
                foreach (PadRow row in padRows)
                {
                    row.InvalidateVisual();
                }
                foreach (KeyRow row in rows)
                {
                    row.InvalidateVisual();
                }
                _chatKeyRow.InvalidateVisual();
            };
            Add(page, resetAll, SettingRowIds.ChatKey + ".reset-all-input");
            ApplyControllerCapabilities(ControllerCapabilities.Current);
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
            if (!OperatingSystem.IsAndroid() && !_captureTouchControls)
            {
                return;
            }
            Heading(page, "On-screen buttons");
            _touchButtonsRow = Add(page, new ToggleRow("Show on-screen buttons",
                Mods.Input.TouchSettings.ButtonsVisible), SettingRowIds.TouchButtons);
            Add(page, new Note("The stick, aiming, the double tap that jumps and the flick "
                + "that boosts are not buttons, so they keep working with every one of these off."));
            foreach ((Mods.Input.TouchControl control, string label) in Mods.Input.TouchSettings.Order)
            {
                ToggleRow row = Add(page, new ToggleRow(label,
                    Mods.Input.TouchSettings.IsEnabled(control)), SettingRowIds.TouchBinding(control.ToString()));
                _touchRows.Add((control, row));
            }
            void ShowTouchRows()
            {
                foreach ((_, ToggleRow row) in _touchRows)
                {
                    row.IsVisible = true;
                    row.IsEnabled = _touchButtonsRow.On;
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
                InputSettings.StylusAimingEnabled), SettingRowIds.StylusAiming);
            _stylusSensitivity = Add(page, new SliderRow("Sensitivity",
                LookToSlider(InputSettings.StylusSensitivity),
                v => $"{SliderToLook(v).ToString("0.00", CultureInfo.InvariantCulture)}x"), SettingRowIds.StylusSensitivity);
            Add(page, new StylusTurnPreview(
                () => SliderToLook(_stylusSensitivity.Value)));
            _stylusInvertY = Add(page, new ToggleRow("Invert vertical aim",
                InputSettings.StylusInvertY), SettingRowIds.StylusInvertY);
            string[] actions = Enum.GetNames<Mods.Input.StylusAction>();
            _stylusPrimary = Add(page, new ChoiceRow("Primary button", actions,
                (int)InputSettings.StylusPrimaryAction), SettingRowIds.StylusPrimary);
            _stylusSecondary = Add(page, new ChoiceRow("Secondary button", actions,
                (int)InputSettings.StylusSecondaryAction), SettingRowIds.StylusSecondary);
            _stylusClassicGestures = Add(page, new ToggleRow("Classic gestures",
                InputSettings.StylusClassicGestures), SettingRowIds.StylusClassicGestures);
            _stylusDoubleTapJump = Add(page, new ToggleRow("Double tap to jump",
                InputSettings.StylusDoubleTapJump), SettingRowIds.StylusDoubleTapJump);
            _stylusFlickBoost = Add(page, new ToggleRow("Flick to boost",
                InputSettings.StylusFlickBoost), SettingRowIds.StylusFlickBoost);
            Heading(page, "Stylus advanced");
            _stylusPressureToFire = Add(page, new ToggleRow("Pressure to fire",
                InputSettings.StylusPressureToFire), SettingRowIds.StylusPressureToFire);
            _stylusPressureThreshold = Add(page, new SliderRow("Pressure threshold",
                (int)Math.Round(InputSettings.StylusPressureThreshold * 100),
                v => $"{v}%"), SettingRowIds.StylusPressureThreshold);
        }

        private static int SensitivityToSlider(float sensitivity)
        {
            return Math.Clamp((int)Math.Round((sensitivity - 0.1f) / 2.9f * 100), 0, 100);
        }

        private static float SliderToSensitivity(int value)
        {
            return 0.1f + value / 100f * 2.9f;
        }

        // Controller and stylus multipliers share the persisted .01x-10x
        // range. A single mapping keeps those values round-trippable through
        // the integer slider while retaining the old controls-file aliases.
        private static int LookToSlider(float look)
        {
            return Math.Clamp((int)Math.Round((look - .01f) / 9.99f * 100), 0, 100);
        }

        private static float SliderToLook(int value)
        {
            return .01f + Math.Clamp(value, 0, 100) / 100f * 9.99f;
        }

        private static int ExponentToSlider(float exponent)
        {
            return Math.Clamp((int)Math.Round((exponent - .05f) / 7.95f * 100), 0, 100);
        }

        private static float SliderToExponent(int value)
        {
            return .05f + Math.Clamp(value, 0, 100) / 100f * 7.95f;
        }

        private static int ThresholdToSlider(float threshold)
        {
            return Math.Clamp((int)Math.Round(threshold * 100), 0, 100);
        }

        /// <summary>Render threshold slider values as their actual percentage.</summary>
        internal static string ThresholdLabel(int value)
            => $"{Math.Clamp(value, 0, 100)}%";

        private static float SliderToThreshold(int value)
        {
            return Math.Clamp(value, 0, 100) / 100f;
        }

        // Up to half the stick's travel. Past that a pad is broken rather than
        // worn, and a dead zone that large makes the game feel worse than the
        // drift it was hiding.
        private static int DeadZoneToSlider(float dead, float maximum)
        {
            return Math.Clamp((int)Math.Round(dead / maximum * 100), 0, 100);
        }

        private static float SliderToDeadZone(int value, float maximum)
        {
            return Math.Clamp(value, 0, 100) / 100f * maximum;
        }

        private const int BoostMaximumMilliseconds = 10_000;

        /// <summary>
        /// Map the controller boost timing to integer milliseconds. The old
        /// percentage mapping had only 100 steps across ten seconds, so the
        /// shipped .18/.12 second defaults were changed to .2/.101 on a
        /// no-op save. A millisecond step keeps the full supported range
        /// precise while retaining SliderRow's integer navigation model.
        /// </summary>
        internal static int BoostSecondsToSlider(float seconds, float minimum = 0)
        {
            float minimumSeconds = Math.Clamp(minimum, 0, 10);
            float clampedSeconds = float.IsFinite(seconds)
                ? Math.Clamp(seconds, minimumSeconds, 10)
                : minimumSeconds;
            return Math.Clamp((int)Math.Round(clampedSeconds * 1000,
                MidpointRounding.AwayFromZero),
                (int)Math.Round(minimumSeconds * 1000, MidpointRounding.AwayFromZero),
                BoostMaximumMilliseconds);
        }

        internal static float BoostSliderToSeconds(int value, float minimum = 0)
        {
            int minimumMilliseconds = (int)Math.Round(Math.Clamp(minimum, 0, 10) * 1000,
                MidpointRounding.AwayFromZero);
            return Math.Clamp(value, minimumMilliseconds, BoostMaximumMilliseconds) / 1000f;
        }

        private static int RadarScaleToSlider(float scale)
            => Math.Clamp((int)Math.Round((scale - global::MphRead.Hud.Radar.RadarSettings.MinimumScale)
                / (global::MphRead.Hud.Radar.RadarSettings.MaximumScale
                    - global::MphRead.Hud.Radar.RadarSettings.MinimumScale) * 100), 0, 100);

        private static float SliderToRadarScale(int value)
            => global::MphRead.Hud.Radar.RadarSettings.MinimumScale
                + Math.Clamp(value, 0, 100) / 100f
                * (global::MphRead.Hud.Radar.RadarSettings.MaximumScale
                    - global::MphRead.Hud.Radar.RadarSettings.MinimumScale);

        private static int OpacityToSlider(float opacity)
            => Math.Clamp((int)Math.Round((Math.Clamp(opacity, .2f, 1f) - .2f) / .8f * 100), 0, 100);

        private static float SliderToOpacity(int value)
            => .2f + Math.Clamp(value, 0, 100) / 100f * .8f;

        // ---------------------------------------------------------- match rules

        private void BuildMatch()
        {
            StackPanel page = AddSection("Player");
            Heading(page, _identity.IsAuthenticated ? "Account identity" : "Guest profile");
            BuildProfile(page);
        }

        private void BuildProfile(StackPanel page)
        {
            bool authenticated = _identity.IsAuthenticated;
            _playerName = Add(page, new FieldRow(
                authenticated ? "Account display name" : "Guest display name",
                authenticated || _identity.IsGuest
                    ? _identity.DisplayName : LauncherPrefs.PlayerName,
                boxWidth: 200), SettingRowIds.PlayerName);
            if (authenticated)
            {
                _playerName.Box.IsReadOnly = true;
                _playerName.Box.IsEnabled = false;
                _hunterProfileAction = new MenuEntry("Hunter Profile",
                    "Manage the account display name and favorite Hunter", titleSize: 13)
                {
                    Height = 42,
                    Accent = GuiTheme.Accent,
                    IsEnabled = _identity.HunterProfileRequested != null,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                _hunterProfileAction.Click += (_, _) =>
                    _identity.HunterProfileRequested?.Invoke();
                Add(page, _hunterProfileAction, SettingRowIds.PlayerName + ".profile");
                Explain(page, _identity.HunterProfileRequested != null
                    ? "Your account display name is authoritative. Open Hunter Profile to change it."
                    : "Your account display name is authoritative and is managed from the Hunter Profile page.");
            }
            else
            {
                Explain(page, "This guest display name is local to this device and is never used as an account identity.");
            }
            string[] hunters = Enumerable.Range(0, 7)
                .Select(i => ((Hunter)i).ToString())
                .Append(Hunter.Random.ToString()).ToArray();
            _hunterRow = Add(page, new ChoiceRow("Hunter", hunters,
                Math.Max(0, Array.IndexOf(hunters, LauncherPrefs.LastHunter.ToString()))),
                SettingRowIds.Hunter);
            Explain(page, "Online match rules are selected by the lobby host and cannot be changed from a local profile.");
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
        private void BuildSystem()
        {
            StackPanel page = AddSection("System");
            Heading(page, "Updates");
            _autoUpdate = new ChoiceRow("Updates", new[] { "Automatic", "Notify only", "Off" },
                (int)LauncherPrefs.UpdatePolicy);
            _autoUpdate.IsEnabled = Update.Updater.Configured;
            Add(page, _autoUpdate, SettingRowIds.Updates);
            _updateStatus = Explain(page, DescribeUpdateStatus(),
                Update.Updater.Configured ? null : GuiTheme.Warm);
            Explain(page, Update.Updater.Configured
                ? "Automatic downloads and stages signed updates. Notify only keeps installation manual. Off skips automatic checks."
                : "Updates are unavailable in this build because no signed update repository is configured.");

            Heading(page, "Game files and diagnostics");
            var files = new MenuEntry("Game files", GameFiles.Describe(), titleSize: 15);
            files.SubtitleColor = GameFiles.Ready ? GuiTheme.Good : GuiTheme.Warm;
            files.Click += (_, _) =>
            {
                GameFilesRequested?.Invoke(this, EventArgs.Empty);
                Close();
            };
            Add(page, files, SettingRowIds.GameFiles);

            _debugLogging = Add(page, new ToggleRow("Debug logging", LauncherPrefs.DebugLogs),
                SettingRowIds.DebugLogging);
            Explain(page, "Writes local diagnostics for troubleshooting; no account or session identity is included.");
            _shareLogs = new MenuEntry("Share logs", titleSize: 15);
            _shareLogs.Click += (_, _) => ShareLogs();
            Add(page, _shareLogs, SettingRowIds.ShareLogs);
            RefreshShareLogs();
        }

        private static string DescribeUpdateStatus()
        {
            if (!Update.Updater.Configured)
            {
                return "Updater unavailable in this build.";
            }
            if (Update.Updater.Disabled)
            {
                return "Updater disabled for this run.";
            }
            UpdateStatus status = Update.Updater.Coordinator.Status;
            return status.State switch
            {
                UpdateState.Available when status.AvailableVersion != null
                    => $"Update available: v{status.AvailableVersion}",
                UpdateState.Staged or UpdateState.WaitingForSafePoint
                    => status.Message ?? "Update staged; waiting for a safe point.",
                UpdateState.Failed => status.Message ?? "Last update check failed.",
                UpdateState.Checking => "Checking for updates…",
                UpdateState.Downloading => "Downloading update…",
                UpdateState.Verifying => "Verifying update…",
                UpdateState.UpToDate => status.Message ?? "Project Prime is up to date.",
                _ => status.Message ?? "No update check has run yet."
            };
        }

        private void UpdateStatusChanged(object? sender, UpdateStatus status)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_observingUpdates)
                {
                    _updateStatus.Text = DescribeUpdateStatus();
                }
            });
        }

        private void RefreshShareLogs()
        {
            if (_shareLogs == null)
            {
                return;
            }
            if (Mods.LogShare.Current == null)
            {
                _shareLogs.IsEnabled = false;
                _shareLogs.Subtitle = "Log sharing is unavailable on this platform.";
                _shareLogs.SubtitleColor = GuiTheme.TextDim;
            }
            else if (!Mods.LogArchive.Any())
            {
                _shareLogs.IsEnabled = false;
                _shareLogs.Subtitle = "Enable debug logging and apply once to create a log.";
                _shareLogs.SubtitleColor = GuiTheme.TextDim;
            }
            else
            {
                _shareLogs.IsEnabled = !_sharingLogs;
                _shareLogs.Subtitle = _sharingLogs ? "Preparing log archive…" : "Open the system share sheet.";
                _shareLogs.SubtitleColor = GuiTheme.TextDim;
            }
        }

        private bool _sharingLogs;

        private async void ShareLogs()
        {
            if (_sharingLogs || Mods.LogShare.Current is not Mods.ILogShare sharer)
            {
                return;
            }
            _sharingLogs = true;
            RefreshShareLogs();
            string error = "";
            string path = "";
            string name = Mods.LogArchive.FileName();
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
            _sharingLogs = false;
            if (!built)
            {
                _shareLogs.Subtitle = error.Length == 0 ? "Could not prepare logs." : error;
                _shareLogs.SubtitleColor = GuiTheme.Warm;
                _shareLogs.IsEnabled = Mods.LogShare.Current != null;
                return;
            }
            RefreshShareLogs();
        }

        private static void ApplyDebugLogging()
        {
            if (LauncherPrefs.DebugLogs)
            {
                Mods.DebugLog.Attach();
                Mods.DebugLog.Line("launcher", "debug logging enabled from settings");
            }
            else if (Mods.DebugLog.Active)
            {
                Mods.DebugLog.Line("launcher", "debug logging disabled from settings");
                Mods.DebugLog.Detach();
            }
        }

        private void BuildNetwork()
        {
            StackPanel page = _networkPage = AddSection("Network");
            Heading(page, "Region");
            _preferredRegionOptions = LauncherPrefs.PreferredRegionOptions.ToArray();
            string[] regionLabels = _preferredRegionOptions.Select(option => option.Label).ToArray();
            int selectedRegion = -1;
            for (int i = 0; i < _preferredRegionOptions.Count; i++)
            {
                if (String.Equals(_preferredRegionOptions[i].Id,
                    LauncherPrefs.PreferredRegion, StringComparison.Ordinal))
                {
                    selectedRegion = i;
                    break;
                }
            }
            _preferredRegion = Add(page, new ChoiceRow("Preferred region", regionLabels,
                Math.Max(0, selectedRegion)),
                SettingRowIds.PreferredRegion);
            Explain(page, "Automatic chooses the best available Node. Friendly labels are presentation only; the saved value remains the exact region ID observed from discovery.");
            Heading(page, "Diagnostics");
            _advancedNetworkRow = Add(page, new ToggleRow("Network diagnostics",
                global::MphRead.Hud.Network.NetworkHealthSettings.Advanced), SettingRowIds.NetworkDiagnostics);
            Explain(page, "Shows additional live network details while connected.");
        }

        private void BuildAccessibility()
        {
            StackPanel page = AddSection("Accessibility");
            Heading(page, "Motion");
            _reducedMotion = Add(page, new ToggleRow("Reduce interface motion",
                LauncherPrefs.ReducedMotion), SettingRowIds.ReducedMotion);
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
                    ? "Save changes and return to the active match."
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

        /// <summary>Test seam for exercising the same save path as the footer.</summary>
        internal void CommitForTests() => Commit();

        private void Commit()
        {
            // Display
            if (_windowRow != null && _windowRow.IsEnabled)
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
            // GraphicsPreset has no Custom enum. Keep the last named preset as
            // a compatibility baseline while the explicit subordinate keys
            // carry the user's custom mix; the view will detect that mismatch
            // and show Custom again on the next load.
            _settings.GraphicsPreset = RenderOptions.FormatGraphicsPreset(
                _graphicsPresetRow.Index == CustomGraphicsPresetIndex
                    ? _graphicsPresetBase : graphicsPreset);
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
            _settings.Killcam = RenderOptions.OnOff(_killcamRow.On);
            _settings.RadarStyle = ((global::MphRead.Hud.Radar.RadarStyle)_radarStyleRow.Index).ToString();
            _settings.RadarOrientation = ((global::MphRead.Hud.Radar.RadarOrientation)_radarOrientationRow.Index).ToString();
            _settings.RadarPosition = ((global::MphRead.Hud.Radar.RadarAnchor)_radarPositionRow.Index).ToString();
            _settings.RadarScale = SliderToRadarScale(_radarScaleRow.Value)
                .ToString("0.00", CultureInfo.InvariantCulture);
            _settings.RadarOffsetX = _radarOffsetXRow.Value.ToString(CultureInfo.InvariantCulture);
            _settings.RadarOffsetY = _radarOffsetYRow.Value.ToString(CultureInfo.InvariantCulture);
            int cap = _fpsLimitStops[Math.Clamp(_fpsLimitRow.Value, 0,
                _fpsLimitStops.Length - 1)].Cap;
            FrameTiming.FrameRateCap = cap;
            _settings.FrameRateCap = FrameTiming.CapString(cap);
            _settings.CelShading = RenderOptions.OnOff(_celRow.On);
            _settings.CelBands = "8";
            _settings.CelEdge = "50";
            Features.ProHud = _proHud.On;
            Features.ProHudFixedWeapon = _proHudWeaponRow.Index == 0;
            Features.ReticleOpacity = SliderToOpacity(_reticleOpacity.Value);
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
            InputSettings.MorphBallMouseFlickBoost = _morphBallMouseFlickBoost.On;
            InputSettings.MorphBallStickFlickBoost = _morphBallStickFlickBoost.On;
            if (_morphBallSwipeBoost != null)
            {
                InputSettings.MorphBallSwipeBoost = _morphBallSwipeBoost.On;
            }
            InputSettings.ControllerPreset = (Mods.Input.ControllerPreset)_gamepadPresetRow.Index;
            InputSettings.GamepadInvertY = _gamepadInvertY.On;
            InputSettings.GamepadGyroMode = (Mods.Input.GamepadGyroMode)_gamepadGyro.Index;
            InputSettings.GamepadGyroActivation =
                (Mods.Input.GamepadGyroActivation)_gamepadGyroActivation.Index;
            InputSettings.GamepadGyroInvertX = _gamepadGyroInvertX.On;
            InputSettings.GamepadGyroInvertY = _gamepadGyroInvertY.On;
            InputSettings.GamepadHapticsEnabled = _gamepadHaptics.On;
            InputSettings.InputBalanceTelemetryEnabled = _inputBalanceTelemetry.On;
            if (!InputSettings.GamepadGyroEnabled) Mods.Input.GamepadGyro.SuppressOutput();
            if (!InputSettings.GamepadHapticsEnabled) Mods.Input.GamepadHaptics.Stop();
            InputSettings.GamepadOuterBoostEnabled = _gamepadOuterBoost.On;
            CommitControllerSliders();
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
            // Launcher preferences
            if (!_identity.IsAuthenticated && _playerName.Value.Trim().Length > 0)
            {
                LauncherPrefs.PlayerName = _playerName.Value.Trim();
            }
            LauncherPrefs.LastHunter = Enum.Parse<Hunter>(_hunterRow.Value);
            LauncherPrefs.UpdatePolicy = (UpdatePolicy)_autoUpdate.Index;
            if (_preferredRegionOptions.Count > 0)
            {
                int index = Math.Clamp(_preferredRegion.Index, 0,
                    _preferredRegionOptions.Count - 1);
                LauncherPrefs.PreferredRegion = _preferredRegionOptions[index].Id;
            }
            LauncherPrefs.DebugLogs = _debugLogging.On;
            LauncherPrefs.ReducedMotion = _reducedMotion.On;
            ApplyDebugLogging();
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

        /// <summary>
        /// Commit only controller sliders that the player changed. Their
        /// integer UI values cannot represent every persisted float, so an
        /// untouched row must not round a loaded value during an unrelated
        /// settings save (including a disabled gyro row).
        /// </summary>
        private void CommitControllerSliders()
        {
            if (ControllerSliderWasEdited(_gamepadHorizontalSensitivity))
            {
                InputSettings.GamepadHorizontalSensitivity
                    = SliderToLook(_gamepadHorizontalSensitivity.Value);
            }
            if (ControllerSliderWasEdited(_gamepadVerticalSensitivity))
            {
                InputSettings.GamepadVerticalSensitivity
                    = SliderToLook(_gamepadVerticalSensitivity.Value);
            }
            if (ControllerSliderWasEdited(_gamepadGyroSensitivity))
            {
                InputSettings.GamepadGyroSensitivity
                    = SliderToLook(_gamepadGyroSensitivity.Value);
            }
            if (ControllerSliderWasEdited(_gamepadHapticsStrength))
            {
                InputSettings.GamepadHapticsStrength
                    = _gamepadHapticsStrength.Value / 100f;
                if (InputSettings.GamepadHapticsStrength <= 0)
                    Mods.Input.GamepadHaptics.Stop();
            }
            if (ControllerSliderWasEdited(_gamepadDeadZone))
            {
                InputSettings.GamepadMoveDeadZone
                    = SliderToDeadZone(_gamepadDeadZone.Value, .9f);
            }
            if (ControllerSliderWasEdited(_gamepadLookDeadZone))
            {
                InputSettings.GamepadLookDeadZone
                    = SliderToDeadZone(_gamepadLookDeadZone.Value, .9f);
            }
            if (ControllerSliderWasEdited(_gamepadOuterDeadZone))
            {
                InputSettings.GamepadOuterDeadZone
                    = SliderToDeadZone(_gamepadOuterDeadZone.Value, .5f);
            }
            if (ControllerSliderWasEdited(_gamepadMoveActivate))
            {
                InputSettings.GamepadMoveActivateThreshold
                    = SliderToThreshold(_gamepadMoveActivate.Value);
            }
            if (ControllerSliderWasEdited(_gamepadMoveRelease))
            {
                InputSettings.GamepadMoveReleaseThreshold
                    = SliderToThreshold(_gamepadMoveRelease.Value);
            }
            if (ControllerSliderWasEdited(_gamepadLook))
            {
                InputSettings.GamepadLookExponent
                    = SliderToExponent(_gamepadLook.Value);
                InputSettings.GamepadResponseCurve
                    = Mods.Input.GamepadResponseCurvePreset.Custom;
            }
            if (ControllerSliderWasEdited(_gamepadYawRate))
            {
                InputSettings.GamepadYawRate = _gamepadYawRate.Value;
            }
            if (ControllerSliderWasEdited(_gamepadPitchRate))
            {
                InputSettings.GamepadPitchRate = _gamepadPitchRate.Value;
            }
            if (ControllerSliderWasEdited(_gamepadOuterBoostStart))
            {
                InputSettings.GamepadOuterBoostStart
                    = SliderToThreshold(_gamepadOuterBoostStart.Value);
            }
            if (ControllerSliderWasEdited(_gamepadOuterYawBoost))
            {
                InputSettings.GamepadOuterYawBoost = _gamepadOuterYawBoost.Value;
            }
            if (ControllerSliderWasEdited(_gamepadOuterPitchBoost))
            {
                InputSettings.GamepadOuterPitchBoost = _gamepadOuterPitchBoost.Value;
            }
            if (ControllerSliderWasEdited(_gamepadBoostDelay))
            {
                InputSettings.GamepadBoostDelaySeconds
                    = BoostSliderToSeconds(_gamepadBoostDelay.Value);
            }
            if (ControllerSliderWasEdited(_gamepadBoostRamp))
            {
                InputSettings.GamepadBoostRampSeconds
                    = BoostSliderToSeconds(_gamepadBoostRamp.Value, .001f);
            }
            if (ControllerSliderWasEdited(_gamepadTriggerPress))
            {
                InputSettings.GamepadTriggerPressThreshold
                    = SliderToThreshold(_gamepadTriggerPress.Value);
            }
            if (ControllerSliderWasEdited(_gamepadTriggerRelease))
            {
                InputSettings.GamepadTriggerReleaseThreshold
                    = SliderToThreshold(_gamepadTriggerRelease.Value);
            }
            if (ControllerSliderWasEdited(_gamepadZoomMultiplier))
            {
                InputSettings.GamepadZoomMultiplier
                    = SliderToLook(_gamepadZoomMultiplier.Value);
            }
        }
    }
}
