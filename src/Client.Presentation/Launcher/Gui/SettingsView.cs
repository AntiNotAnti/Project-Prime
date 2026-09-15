using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MphRead.Cosmetics;
using MphRead.Cosmetics.Presentation;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Content;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using MphRead.Mods.Launcher.Settings;
using MphRead.Mods.Launcher.Theme;
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
    /// Below <see cref="NarrowWidth"/> the rail turns into a strip across the
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
        private readonly bool _embedActionBar;
        private readonly InputSettings.Snapshot _inputSnapshot;
        private readonly int _fieldOfViewSnapshot;
        private readonly global::MphRead.Hud.Radar.RadarProfile _radarProfileSnapshot;
        private readonly IReadOnlyDictionary<string, global::MphRead.Hud.Radar.RadarProfile>
            _radarModeProfilesSnapshot;
        private readonly IReadOnlyDictionary<global::MphRead.Hud.Radar.RadarDeviceClass,
            global::MphRead.Hud.Radar.RadarProfile> _radarDeviceProfilesSnapshot;
        private bool _draftSnapshotCompleted;
        private bool _closed;
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
        private readonly List<ControlsTab> _controlTabs = new();
        private SettingsPage _mouseKeyboardControlsPage = null!;
        private SettingsPage _gamepadControlsPage = null!;
        private SettingsPage? _touchControlsPage;
        private SettingsPage? _stylusControlsPage;
        private readonly Grid _grid = new();
        private readonly Border _railPanel;
        private Control? _heading;
        private SettingsActionBar? _footerPanel;
        private readonly ScrollViewer _railScroll;
        private bool _narrow;
        private bool _laidOut;
        private readonly List<(string Id, Control Control)> _trackedRows = new();
        private SettingsDirtyTracker? _dirtyTracker;

        /// <summary>Below the shared compact breakpoint the rail wraps above the page.</summary>
        private static double NarrowWidth => PrimeLayoutMetrics.CompactWidth;

        /// <summary>Raised when this view is finished with, saved or not.</summary>
        public event EventHandler? Closed;

        /// <summary>
        /// Raised after the persisted settings and launcher preferences have
        /// both succeeded, immediately before the view closes. The action bar
        /// uses this as a polite live-region seam for assistive clients.
        /// </summary>
        internal event EventHandler? SaveSucceeded;

        /// <summary>
        /// The player asked for the game-files screen, which lives on the
        /// front screen because extracting a ROM is a thing you do before
        /// there is anything to configure. Raised, not acted on: this view
        /// does not know what is behind it.
        /// </summary>
        public event EventHandler? GameFilesRequested;

        private ChoiceRow? _windowRow;
        private SliderRow _resolutionScale = null!;
        private SliderRow _fieldOfView = null!;
        private ToggleRow _lightingRow = null!;
        private ToggleRow _fogRow = null!;
        private ChoiceRow _graphicsPresetRow = null!;
        private Note _graphicsPresetNote = null!;
        private ChoiceRow _textureFilteringPresetRow = null!;
        private ChoiceRow _anisotropyRow = null!;
        private ChoiceRow _msaaRow = null!;
        private ToggleRow _bloomRow = null!;
        private ToggleRow _dynamicVisualLightsRow = null!;
        private ChoiceRow _texturePackRow = null!;
        private ChoiceRow _visualStyleRow = null!;
        private IReadOnlyList<TexturePackOption> _texturePacks
            = Array.Empty<TexturePackOption>();
        private ToggleRow _fpsRow = null!;
        private ToggleRow _advancedNetworkRow = null!;
        private StackPanel _networkPage = null!;
        private ChoiceRow _hitMarkerRow = null!, _radarStyleRow = null!, _radarOrientationRow = null!;
        private ChoiceRow _radarPositionRow = null!;
        private SliderRow _radarScaleRow = null!, _radarOffsetXRow = null!, _radarOffsetYRow = null!;
        private SliderRow _radarRangeRow = null!, _radarOpacityRow = null!;
        private ToggleRow _radarElevationRow = null!;
        private ChoiceRow _radarPresetRow = null!, _radarColorRow = null!, _radarFloorRow = null!, _radarZoomRow = null!;
        private SliderRow _radarMarkerScaleRow = null!, _radarMarkerOpacityRow = null!, _radarElevationThresholdRow = null!;
        private SliderRow _radarAutoMinimumRow = null!, _radarAutoMaximumRow = null!, _radarZoomSmoothingRow = null!;
        private SliderRow _radarFloorBrightnessRow = null!, _radarAdjacentOpacityRow = null!;
        private SliderRow _radarBackgroundDimRow = null!, _radarBackgroundBlurRow = null!;
        private SliderRow _radarPersistenceRow = null!, _radarEdgeScaleRow = null!;
        private ToggleRow _radarMarkerOutlineRow = null!, _radarEdgeArrowsRow = null!, _radarLabelsRow = null!;
        private ToggleRow _radarObjectiveEmphasisRow = null!, _radarEnemiesRow = null!, _radarTeammatesRow = null!;
        private ToggleRow _radarObjectivesRow = null!, _radarFlagsRow = null!, _radarBasesRow = null!;
        private ToggleRow _radarNodesRow = null!, _radarDefendersRow = null!, _radarMapFillRow = null!;
        private ToggleRow _radarResourcesRow = null!, _radarWeaponsRow = null!, _radarAmmoRow = null!;
        private ToggleRow _radarHealthRow = null!, _radarPowerupsRow = null!;
        private ToggleRow _radarMapOutlinesRow = null!, _radarGridRow = null!, _radarRingsRow = null!;
        private ToggleRow _radarCompassRow = null!, _radarPulseRow = null!, _radarPriorityRow = null!;
        private bool _applyingRadarPreset;
        private bool _synchronizingRadarRange;
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
            ("Display refresh (VSync)", FrameTiming.DisplayRate),
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
        private static readonly string[] _windowModeChoices = new[]
        {
            "Windowed", "Fullscreen (borderless)"
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
        private SliderRow _reticleOpacity = null!, _reticleScale = null!;
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
        private Note _announcerPackDetails = null!;
        private Note _musicPackDetails = null!;
        private Expander _announcerPackDetailsExpander = null!;
        private Expander _musicPackDetailsExpander = null!;
        private ChoiceRow _languageRow = null!;
        private SliderRow _dynamicCrosshairTravel = null!;
        private SliderRow _dynamicCrosshairSensitivity = null!;
        private SliderRow _dynamicCrosshairTurnSpeed = null!;
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
        private SliderRow _gamepadZoomVerticalMultiplier = null!;
        private SliderRow _gamepadMoveActivate = null!, _gamepadMoveRelease = null!;
        private SliderRow _gamepadYawRate = null!, _gamepadPitchRate = null!;
        private SliderRow _gamepadOuterBoostStart = null!, _gamepadOuterYawBoost = null!,
            _gamepadOuterPitchBoost = null!, _gamepadBoostDelay = null!, _gamepadBoostRamp = null!;
        private ToggleRow _gamepadInvertY = null!, _gamepadAutoCalibration = null!;
        private ChoiceRow _gamepadGyro = null!, _gamepadGyroActivation = null!;
        private ChoiceRow _gamepadResponseCurve = null!, _gamepadTurnAcceleration = null!,
            _gamepadStickAimMode = null!;
        private ToggleRow _gamepadGyroInvertX = null!;
        private ToggleRow _gamepadGyroInvertY = null!, _gamepadHaptics = null!;
        private ToggleRow _inputBalanceTelemetry = null!;
        private Note? _gyroCapabilityNote;
        private Expander? _advancedControllerExpander;
        private SliderRow _gamepadGyroSensitivity = null!, _gamepadHapticsStrength = null!;
        private Note? _controllerDiagnosticsSummary;
        private Note? _controllerDiagnosticsDetails;
        private MenuEntry? _controllerDiagnosticsExport;
        private DispatcherTimer? _controllerDiagnosticsTimer;
        private ToggleRow? _stylusAiming, _stylusInvertY, _stylusClassicGestures,
            _stylusDoubleTapJump, _stylusFlickBoost, _stylusPressureToFire;
        private SliderRow? _stylusSensitivity, _stylusPressureThreshold,
            _bottomScreenScale, _bottomScreenCenterX, _bottomScreenCenterY,
            _bottomScreenOpacity, _bottomScreenCursorSensitivity,
            _bottomScreenCursorStartX, _bottomScreenCursorStartY,
            _bottomScreenPowerBeamX, _bottomScreenPowerBeamY,
            _bottomScreenMissileX, _bottomScreenMissileY,
            _bottomScreenNextWeaponX, _bottomScreenNextWeaponY,
            _bottomScreenWeaponSelectX, _bottomScreenWeaponSelectY,
            _bottomScreenAltFormX, _bottomScreenAltFormY;
        private SliderRow[] _bottomScreenAffinityRows = Array.Empty<SliderRow>();
        private bool _dynamicCrosshairTravelEdited, _dynamicCrosshairSensitivityEdited,
            _dynamicCrosshairTurnSpeedEdited, _mouseSensitivityEdited,
            _stylusSensitivityEdited;
        private ChoiceRow? _stylusPrimary, _stylusSecondary;
        private ChoiceRow? _bottomScreenMode, _bottomScreenActivation,
            _bottomScreenStyle;
        private ToggleRow? _bottomScreenLabels, _bottomScreenDirectionalSwipeAssist;
        private FieldRow _playerName = null!;
        private ChoiceRow _hunterRow = null!;
        private ToggleRow _showOnlinePresence = null!;
        private ChoiceRow _autoUpdate = null!;
        private ChoiceRow _preferredRegion = null!;
        private IReadOnlyList<PreferredRegionOption> _preferredRegionOptions
            = Array.Empty<PreferredRegionOption>();
        private MenuEntry? _hunterProfileAction;
        private ToggleRow _debugLogging = null!;
        private MenuEntry _shareLogs = null!;
        private Note _updateStatus = null!;
        private ToggleRow _reducedMotion = null!;
        private ChoiceRow _cosmeticQuality = null!;
        private ToggleRow _showOtherPlayerCosmetics = null!;
        private ToggleRow _reduceCosmeticFlashes = null!;
        private ToggleRow _forceStrongTeamColors = null!;
        private ToggleRow _disableCosmeticDistortion = null!;
        private ToggleRow _disableCosmeticParticles = null!;

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

        /// <summary>
        /// Input-device tabs are local to the Controls page. Their pages are
        /// created once and only reparented/hidden, so switching tabs cannot
        /// discard a pending edit or reset a controller capability observer.
        /// </summary>
        internal IReadOnlyList<string> ControlsTabNames
            => _controlTabs.Select(tab => tab.Button.Title).ToArray();

        internal IReadOnlyList<string> VisibleControlsTabNames
            => _controlTabs.Where(tab => tab.Page.IsVisible)
                .Select(tab => tab.Button.Title).ToArray();

        internal string? ActiveControlsTab
            => _controlTabs.FirstOrDefault(tab => tab.Page.IsVisible)?.Button.Title;

        internal bool HasTouchControlRows => _touchButtonsRow != null;
        internal bool? RenderedTouchButtonsVisible => _touchButtonsRow?.On;
        internal bool RenderedGyroControlsEnabled
            => _gamepadGyro.IsEnabled && _gamepadGyroSensitivity.IsEnabled
                && _gamepadGyroActivation.IsEnabled
                && _gamepadGyroInvertX.IsEnabled && _gamepadGyroInvertY.IsEnabled;
        internal string? RenderedGyroCapabilityNote => _gyroCapabilityNote?.Text;
        internal bool? RenderedAdvancedControllerExpanded
            => _advancedControllerExpander?.IsExpanded;
        internal bool PresentationPackDetailsCollapsed
            => !_announcerPackDetailsExpander.IsExpanded
                && !_musicPackDetailsExpander.IsExpanded;
        internal bool? CaptureAdvancedControllerExpanded
            => _captureAdvancedControllerExpanded;
        internal SettingsIdentityContext IdentityContext => _identity;
        internal bool HasHunterProfileAction => _hunterProfileAction != null;
        internal bool IsObservingControllerCapabilities => _observingControllerCapabilities;
        internal string? RenderedControllerDiagnosticsSummary
            => _controllerDiagnosticsSummary?.Text;
        internal string? RenderedControllerDiagnosticsDetails
            => _controllerDiagnosticsDetails?.Text;
        internal string? ControllerDiagnosticsExportStatus
            => _controllerDiagnosticsExport?.Subtitle;

        private const double _railWidth = 244;

        /// <summary>True when the user pressed save rather than closing.</summary>
        public bool Saved { get; private set; }

        /// <summary>What a frame around this should be titled.</summary>
        public string WindowTitle => $"{Mods.Branding.Name} settings";

        /// <summary>True when this was opened over a match rather than the launcher.</summary>
        public bool InGame => _inGame;

        private readonly Scene? _scene;

        public SettingsView(MenuSettings settings, bool inGame = false, Scene? scene = null,
            SettingsIdentityContext? identity = null, bool embedActionBar = true)
            : this(settings, inGame, scene, captureTouchControls: false,
                captureGyroSupported: null, captureAdvancedControllerExpanded: null,
                identity: identity, embedActionBar: embedActionBar)
        {
        }

        internal SettingsView(MenuSettings settings, bool inGame, Scene? scene,
            bool captureTouchControls, bool? captureGyroSupported,
            bool? captureAdvancedControllerExpanded,
            SettingsIdentityContext? identity = null, bool embedActionBar = true)
        {
            _scene = scene;
            _settings = settings;
            _inGame = inGame;
            _captureTouchControls = captureTouchControls;
            _captureGyroSupported = captureGyroSupported;
            _captureAdvancedControllerExpanded = captureAdvancedControllerExpanded;
            _identity = identity ?? SettingsIdentityContext.SignedOut;
            _embedActionBar = embedActionBar;
            _inputSnapshot = InputSettings.CaptureSnapshot();
            _fieldOfViewSnapshot = RenderOptions.FieldOfView;
            _radarProfileSnapshot = global::MphRead.Hud.Radar.RadarSettings.DefaultProfile;
            _radarModeProfilesSnapshot = global::MphRead.Hud.Radar.RadarSettings.ModeProfiles;
            _radarDeviceProfilesSnapshot = global::MphRead.Hud.Radar.RadarSettings.DeviceProfiles;
            DetachedFromVisualTree += (_, _) => CompleteDraftSnapshot();

            Background = GuiTheme.InkBrush;
            Focusable = true;

            _railScroll = new ScrollViewer
            {
                Content = _rail,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _railPanel = new Border
            {
                Background = GuiTheme.PanelBrush,
                BorderBrush = GuiTheme.EdgeBrush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = _railScroll
            };
            // A grid rather than a docked panel so that the sections come
            // before the footer in the tab order: a DockPanel fills with its
            // *last* child, which would have put Save and Discard first and made
            // the first Tab in the window a press away from closing it.
            _grid.Children.Add(_railPanel);
            _grid.Children.Add(_pages);
            ApplyLayout(narrow: false);
            Content = _grid;
            SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width < NarrowWidth);

            _heading = BuildRailHeading();
            _rail.Children.Add(_heading);
            BuildPages();
            // The sections did not exist when the first layout ran, so the one
            // that is wanted is chosen again now that they do.
            InitializeDirtyTracking();
            if (_embedActionBar)
            {
                _footerPanel = new SettingsActionBar(this, _inGame);
                _grid.Children.Add(_footerPanel);
            }
            _laidOut = false;
            ApplyLayout(_narrow);
            ShowPage(_sections[0].Page);
        }

        /// <summary>
        /// Attach one change stream after every row has been populated. This
        /// ordering is important: a setter used while initialising a row is a
        /// presentation update, while the same setter after construction is a
        /// player edit. The tracker compares the complete draft, so returning
        /// to the opening value becomes clean again automatically.
        /// </summary>
        private void InitializeDirtyTracking()
        {
            _dirtyTracker = new SettingsDirtyTracker(CaptureDraftValues);
            foreach ((string _, Control control) in _trackedRows)
            {
                switch (control)
                {
                    case ChoiceRow row:
                        row.Changed += DraftControlChanged;
                        break;
                    case SliderRow row:
                        row.ValueChanged += DraftControlChanged;
                        break;
                    case ToggleRow row:
                        row.Changed += DraftControlChanged;
                        break;
                    case FieldRow row:
                        row.Changed += DraftControlChanged;
                        break;
                    case KeyRow row:
                        row.Rebound += DraftControlChanged;
                        break;
                    case PadRow row:
                        row.Rebound += DraftControlChanged;
                        break;
                }
            }
        }

        private void DraftControlChanged(object? sender, EventArgs e)
            => _dirtyTracker?.Refresh();

        private IReadOnlyDictionary<string, string?> CaptureDraftValues()
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach ((string id, Control control) in _trackedRows)
            {
                string? value = control switch
                {
                    ChoiceRow row => row.Value,
                    SliderRow row => row.Value.ToString(CultureInfo.InvariantCulture),
                    ToggleRow row => row.On ? "on" : "off",
                    FieldRow row => row.Value,
                    KeyRow row => row.CurrentValue,
                    PadRow row => row.CurrentValue,
                    _ => null
                };
                if (value != null)
                {
                    values[id] = value;
                }
            }
            return values;
        }

        private IDisposable? SuppressDirtyTracking()
            => _dirtyTracker?.Suppress();

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
                _grid.RowDefinitions = new RowDefinitions(
                    _embedActionBar ? "Auto,*,Auto" : "Auto,*");
                MoveSections(_railWrap);
                _railScroll.Content = _railWrap;
                _railScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                _railScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                _railPanel.Width = Double.NaN;
                _railPanel.Padding = new Thickness(12, 8, 12, 6);
                _railPanel.BorderThickness = new Thickness(0, 0, 0, 1);
                if (_footerPanel != null)
                {
                    _footerPanel.Width = Double.NaN;
                    _footerPanel.Padding = new Thickness(12, 6, 12, 10);
                    _footerPanel.ApplyLayout(narrow: true, honorRequested: true);
                }
                Place(_railPanel, 0, 0, rowSpan: 1);
                Place(_pages, 1, 0, rowSpan: 1);
                if (_footerPanel != null) Place(_footerPanel, 2, 0, rowSpan: 1);
                return;
            }
            _grid.ColumnDefinitions = new ColumnDefinitions("Auto,*");
            _grid.RowDefinitions = new RowDefinitions(_embedActionBar ? "*,Auto" : "*");
            MoveSections(_rail);
            _railScroll.Content = _rail;
            _railScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _railScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _railPanel.Width = _railWidth;
            _railPanel.Padding = new Thickness(18, 20, 14, 4);
            _railPanel.BorderThickness = new Thickness(0, 0, 1, 0);
            if (_footerPanel != null)
            {
                _footerPanel.Width = Double.NaN;
                _footerPanel.Padding = new Thickness(26, 10, 26, 12);
                _footerPanel.ApplyLayout(narrow: false, honorRequested: true);
            }
            Place(_railPanel, 0, 0, rowSpan: _embedActionBar ? 2 : 1);
            Place(_pages, 0, 1, rowSpan: 1);
            if (_footerPanel != null) Place(_footerPanel, 1, 1, rowSpan: 1);
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
            StartControllerDiagnosticsRefresh();
            Dispatcher.UIThread.Post(() => _sections[0].Button.Focus(),
                DispatcherPriority.Background);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            StopControllerDiagnosticsRefresh();
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
            RefreshControllerDiagnostics();
        }

        /// <summary>
        /// Refresh the diagnostics panel from the immutable capability store
        /// and the managed GamepadInput snapshot. This deliberately does not
        /// poll a platform backend: the SDL host (or the Android owner) is
        /// the only input owner and this view only observes what it has
        /// already published.
        /// </summary>
        private void RefreshControllerDiagnostics()
        {
            if (_controllerDiagnosticsSummary == null
                || _controllerDiagnosticsDetails == null)
            {
                return;
            }

            ControllerCapabilitySnapshot capabilities = ControllerCapabilities.Current;
            GamepadState raw = GamepadInput.State;
            ControllerDiagnosticReport report = CaptureControllerDiagnostics(
                capabilities, raw);
            _controllerDiagnosticsSummary.Text = DescribeControllerDiagnostics(capabilities,
                raw);
            _controllerDiagnosticsDetails.Text = DescribeControllerDiagnosticsDetails(report);
        }

        private static ControllerDiagnosticReport CaptureControllerDiagnostics(
            ControllerCapabilitySnapshot capabilities, in GamepadState raw)
        {
            GamepadMovementSample movement = GamepadInput.Movement;
            return ControllerDiagnosticReportBuilder.Capture(
                capabilities,
                raw,
                GamepadInput.EffectiveButtons,
                movement.Vector.X,
                movement.Vector.Y,
                GamepadInput.AimDeltaX,
                GamepadInput.AimDeltaY,
                GamepadInput.AimAngularVelocity.X,
                GamepadInput.AimAngularVelocity.Y);
        }

        private static string DescribeControllerDiagnostics(
            ControllerCapabilitySnapshot capabilities, in GamepadState raw)
        {
            if (!capabilities.IsBackendAvailable)
            {
                return "Controller diagnostics unavailable: no input backend is publishing a snapshot."
                    + " This is not a physical-controller acceptance result.";
            }
            if (!capabilities.IsConnected)
            {
                return $"{capabilities.Backend} input backend is available, but it reports no connected controller."
                    + " This is not a physical-controller acceptance result.";
            }
            if (capabilities.Backend != ControllerBackend.Sdl)
            {
                return $"{capabilities.Backend} input backend reports {raw.Name ?? "an unnamed controller"}."
                    + " SDL identity and physical acceptance are unavailable on this backend.";
            }
            return $"SDL reports {capabilities.DeviceName ?? raw.Name ?? "an unnamed controller"}"
                + $" ({capabilities.Family}); managed input is {(raw.Connected ? "connected" : "not connected")}."
                + " This panel observes the active owner; it does not perform physical acceptance.";
        }

        private static string DescribeControllerDiagnosticsDetails(
            ControllerDiagnosticReport report)
        {
            var text = new StringBuilder(4096);
            text.Append(ControllerDiagnosticReportBuilder.SerializeText(report));
            text.AppendLine("controllerTuning:");
            text.AppendLine($"  responseCurve={InputSettings.GamepadResponseCurve}");
            text.AppendLine($"  lookExponent={FiniteSetting(InputSettings.GamepadLookExponent)}");
            text.AppendLine($"  turnAcceleration={InputSettings.GamepadTurnAcceleration}");
            text.AppendLine($"  stickAimMode={InputSettings.GamepadStickAimMode}");
            text.AppendLine($"  gyroMode={InputSettings.GamepadGyroMode}");
            text.AppendLine($"  gyroSensitivity={FiniteSetting(InputSettings.GamepadGyroSensitivity)}");
            text.AppendLine($"  hapticsStrength={FiniteSetting(InputSettings.GamepadHapticsStrength)}");
            text.AppendLine($"  outerBoostEnabled={InputSettings.GamepadOuterBoostEnabled}");
            text.AppendLine($"  outerBoostStart={FiniteSetting(InputSettings.GamepadOuterBoostStart)}");
            return text.ToString();
        }

        private static string FiniteSetting(float value)
            => float.IsFinite(value)
                ? value.ToString("R", CultureInfo.InvariantCulture)
                : "unknown";

        private void StartControllerDiagnosticsRefresh()
        {
            if (_controllerDiagnosticsTimer != null)
            {
                return;
            }
            RefreshControllerDiagnostics();
            _controllerDiagnosticsTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(200), DispatcherPriority.Background,
                (_, _) => RefreshControllerDiagnostics());
            _controllerDiagnosticsTimer.Start();
        }

        private void StopControllerDiagnosticsRefresh()
        {
            _controllerDiagnosticsTimer?.Stop();
            _controllerDiagnosticsTimer = null;
        }

        private void ExportControllerDiagnostics()
        {
            if (_controllerDiagnosticsExport == null)
            {
                return;
            }
            try
            {
                ControllerCapabilitySnapshot capabilities = ControllerCapabilities.Current;
                ControllerDiagnosticReport report = CaptureControllerDiagnostics(
                    capabilities, GamepadInput.State);
                (string jsonPath, string textPath) = ControllerDiagnosticReportBuilder
                    .WriteToDirectory(Path.Combine(LauncherPrefs.Directory, "logs"), report);
                _controllerDiagnosticsExport.Subtitle = $"Saved {Path.GetFileName(jsonPath)}"
                    + $" and {Path.GetFileName(textPath)}";
                _controllerDiagnosticsExport.SubtitleColor = GuiTheme.Good;
            }
            catch (Exception ex)
            {
                // Do not surface a launcher/data path in the view. Keep the
                // diagnostic context in the local debug stream instead.
                Console.WriteLine($"[settings] controller diagnostics export failed: {ex.Message}");
                _controllerDiagnosticsExport.Subtitle = "Export failed; no report was overwritten.";
                _controllerDiagnosticsExport.SubtitleColor = GuiTheme.Warm;
            }
        }

        /// <summary>Release external observers when this view's lifetime ends.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            CompleteDraftSnapshot();
            StopControllerDiagnosticsRefresh();
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

        private void Close()
        {
            if (_closed) return;
            _closed = true;
            CompleteDraftSnapshot();
            if (!Saved)
            {
                _dirtyTracker?.MarkDiscarded();
            }
            Closed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Apply the current draft through the existing persistence path.</summary>
        internal bool ApplyAndSave(out string? error)
        {
            error = null;
            if (_closed) return Saved;
            try
            {
                Commit();
                return true;
            }
            catch (Exception ex)
            {
                error = $"Could not save: {ex.Message}";
                return false;
            }
        }

        /// <summary>Discard this draft and restore eager preview mutations.</summary>
        internal void DiscardChanges() => Close();

        /// <summary>
        /// Compatibility name for hosts that still call the close action
        /// Cancel. The action bar presents the player-facing Discard label.
        /// </summary>
        internal void Cancel() => DiscardChanges();

        private void CompleteDraftSnapshot()
        {
            if (_draftSnapshotCompleted) return;
            if (!Saved)
            {
                _inputSnapshot.Restore();
                RenderOptions.FieldOfView = _fieldOfViewSnapshot;
                RestoreRadarSnapshot();
            }
            _draftSnapshotCompleted = true;
        }

        private void RestoreRadarSnapshot()
        {
            global::MphRead.Hud.Radar.RadarSettings.Apply(_radarProfileSnapshot);
            global::MphRead.Hud.Radar.RadarSettings.ClearModeProfiles();
            foreach ((string mode, global::MphRead.Hud.Radar.RadarProfile profile)
                in _radarModeProfilesSnapshot)
            {
                global::MphRead.Hud.Radar.RadarSettings.SetModeProfile(mode, profile);
            }
            global::MphRead.Hud.Radar.RadarSettings.ClearDeviceProfiles();
            foreach ((global::MphRead.Hud.Radar.RadarDeviceClass device,
                global::MphRead.Hud.Radar.RadarProfile profile) in _radarDeviceProfilesSnapshot)
            {
                global::MphRead.Hud.Radar.RadarSettings.SetDeviceProfile(device, profile);
            }
        }

        /// <summary>Test seam for the rollback path used by Discard and Escape.</summary>
        internal void CancelForTests() => Cancel();

        internal bool HasEmbeddedActionBar => _embedActionBar;
        internal bool IsDirty => _dirtyTracker?.IsDirty == true;
        internal int UnsavedChangeCount => _dirtyTracker?.ChangedCount ?? 0;
        internal SettingsDirtyTracker DirtyTracker
            => _dirtyTracker ?? throw new InvalidOperationException(
                "Settings dirty tracking has not been initialized.");
        internal bool IsSettingsFooterVisible => _footerPanel?.IsVisible == true;

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

        private sealed class ControlsTab
        {
            public ControlsTab(string name, string subtitle, SettingsPage page)
            {
                Button = new MenuEntry(name, subtitle, titleSize: 12)
                {
                    Height = 42,
                    Margin = new Thickness(0, 0, 6, 6)
                };
                Page = page;
            }

            public MenuEntry Button { get; }
            public SettingsPage Page { get; }
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
            bool complex = String.Equals(name, "Controls", StringComparison.Ordinal)
                || String.Equals(name, "Graphics", StringComparison.Ordinal);
            var page = new SettingsPage
            {
                Spacing = 10,
                MaxWidth = complex ? PrimeLayoutMetrics.ComplexFormMaxWidth
                    : PrimeLayoutMetrics.SimpleFormMaxWidth,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
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
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalContentAlignment = HorizontalAlignment.Center
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
            var heading = PrimeControlFactory.PageHeading(SectionTitle(name),
                $"SETTINGS / {name.ToUpperInvariant()}", SectionDescription(name));
            heading.Margin = new Thickness(2, 4, 2, 8);
            return heading;
        }

        private static string SectionSubtitle(string name) => name switch
        {
            "Player" => "IDENTITY / HUNTER",
            "Controls" => "MOUSE / PAD / PEN / KEYS",
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
            "Player" => "Player",
            "Controls" => "Controls",
            "Graphics" => "Graphics",
            "Audio" => "Audio",
            "System" => "System",
            "Network" => "Network",
            "Accessibility" => "Accessibility",
            "About" => "About Project Prime",
            _ => name
        };

        private static string SectionDescription(string name) => name switch
        {
            "Player" => "Manage your display name and preferred Hunter.",
            "Controls" => "Tune mouse, gamepad, stylus, touch and direct action bindings.",
            "Graphics" => "Choose a preset, display settings, visual quality, and HUD.",
            "Audio" => "Adjust volume and presentation packs.",
            "System" => "Manage updates, game files, and diagnostics.",
            "Network" => "Choose a region and manage network diagnostics.",
            "Accessibility" => "Choose how much motion appears in launcher menus.",
            "About" => "Credits, project history, and support.",
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
            ArgumentNullException.ThrowIfNull(control);
            var advanced = new Expander
            {
                Header = "Advanced",
                Content = control,
                IsExpanded = false,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Add(_networkPage, advanced);
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
                PrimeSectionPanel panel = PrimeControlFactory.SectionPanel(target);
                settingsPage.Children.Add(panel);
                settingsPage.ActiveSector = target;
            }
            var caption = new Caption(text)
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 4)
            };
            target.Children.Add(caption);
            return caption;
        }

        private static StackPanel ActiveSector(StackPanel page)
        {
            return page is SettingsPage settingsPage && settingsPage.ActiveSector != null
                ? settingsPage.ActiveSector
                : page;
        }

        private static void AddPageRoot(StackPanel page, Control control)
        {
            if (page is SettingsPage settingsPage)
            {
                settingsPage.ActiveSector = null;
            }
            page.Children.Add(control);
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
                _trackedRows.Add((rowId, control));
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
            Heading(page, "Project Prime");
            Explain(page, Mods.Branding.NameAndVersion);
            // Protocol and build metadata is useful when troubleshooting, but
            // it is not part of the normal project story. Keep it available
            // without making the About page lead with implementation details.
            Add(page, new Expander
            {
                Header = "Build details",
                Content = new Note($"Protocol version: {Mods.Network.NetHeader.Version}\n"
                    + $"Build: {BuildVersion.Display}"),
                IsExpanded = false,
                Margin = new Thickness(0, 0, 0, 2)
            });
            Heading(page, "About Project Prime");
            Explain(page, Mods.Credits.Summary);
            Add(page, new Caption(Mods.Credits.Author));
            Explain(page, "Project Prime adds multiplayer infrastructure, dedicated servers, "
                + "the launcher, custom maps, Android support, and the Pro HUD.");
            // The address is put in the row itself when there is no browser to
            // hand it to -- a headless session, or a handler that refused --
            // so the button says something either way rather than appearing to
            // do nothing. Same fallback the update badge uses.
            var support = new MenuEntry("Support Project Prime", titleSize: 15);
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
            Heading(page, "Graphics preset");
            _graphicsPresetBase = RenderOptions.GraphicsPreset;
            bool graphicsPresetCustom = GraphicsQualityHasOverrides(_graphicsPresetBase);
            _graphicsPresetRow = Add(page, new ChoiceRow("Preset",
                _graphicsPresetChoices,
                graphicsPresetCustom ? CustomGraphicsPresetIndex : (int)_graphicsPresetBase),
                SettingRowIds.GraphicsPreset);
            _graphicsPresetNote = Explain(page, graphicsPresetCustom
                ? GraphicsPresetCustomDescription()
                : "The quality preset supplies defaults for the detail rows below; adjust them for a custom mix.");
            _graphicsPresetRow.Changed += (_, _) => ApplyGraphicsPresetToRows();

            // A phone has one window, it is already the whole screen, and it
            // has no F11. Everything in this group is about a desktop window.
            // The two compact panels share a row on wide Settings pages and
            // naturally stack when the available width becomes narrow.
            var displayColumn = new StackPanel { Spacing = 10 };
            var performanceColumn = new StackPanel { Spacing = 10 };
            var displayPerformance = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                ColumnSpacing = 12
            };
            displayPerformance.Children.Add(
                PrimeControlFactory.SectionPanel(displayColumn));
            displayPerformance.Children.Add(
                PrimeControlFactory.SectionPanel(performanceColumn));
            void ApplyDisplayPerformanceLayout(double width)
            {
                bool compact = width < PrimeLayoutMetrics.CompactWidth;
                displayPerformance.ColumnDefinitions = compact
                    ? new ColumnDefinitions("*")
                    : new ColumnDefinitions("*,*");
                Grid.SetColumn(displayPerformance.Children[0], 0);
                Grid.SetColumn(displayPerformance.Children[1], compact ? 0 : 1);
                Grid.SetRow(displayPerformance.Children[0], 0);
                Grid.SetRow(displayPerformance.Children[1], compact ? 1 : 0);
                displayPerformance.RowDefinitions = compact
                    ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
            }
            displayPerformance.SizeChanged += (_, args) =>
                ApplyDisplayPerformanceLayout(args.NewSize.Width);
            ApplyDisplayPerformanceLayout(PrimeLayoutMetrics.WideWidth);
            AddPageRoot(page, displayPerformance);

            Heading(displayColumn, "Display");
            _windowRow = Add(displayColumn, new ChoiceRow("Window mode",
                _windowModeChoices,
                LauncherPrefs.WindowMode == WindowStartMode.BorderlessFullscreen ? 1 : 0),
                SettingRowIds.WindowMode);
            if (OperatingSystem.IsAndroid())
            {
                _windowRow.IsEnabled = false;
                Explain(displayColumn, "Window mode is managed by Android and cannot be changed here.");
            }

            _fieldOfView = Add(displayColumn, new SliderRow("Field of view",
                RenderOptions.FieldOfView,
                v => v == RenderOptions.DefaultFieldOfView
                    ? $"{v}° · Original" : $"{v}°",
                min: RenderOptions.MinFieldOfView,
                max: RenderOptions.MaxFieldOfView,
                keyStep: 1), SettingRowIds.FieldOfView);
            _fieldOfView.ValueChanged += (_, _) =>
                RenderOptions.FieldOfView = _fieldOfView.Value;
            Explain(displayColumn, "Vertical camera angle, previewed immediately. 78° is the "
                + "original view. Weapon zoom keeps the same ratio; cutscenes and "
                + "spectator cameras keep their authored values.");

            Heading(performanceColumn, "Performance");
            _resolutionScale = Add(performanceColumn, new SliderRow("Render scale",
                Math.Max(RenderOptions.MinScale, RenderOptions.ResolutionScale),
                v => $"{Math.Max(RenderOptions.MinScale, v)}%",
                min: RenderOptions.MinScale), SettingRowIds.RenderScale);
            // Under the render scale because they are the same question asked
            // from both ends -- how much picture, and how often -- and because
            // the two of them are what somebody who is not getting a smooth
            // game comes to this page to change.
            _fpsLimitRow = Add(performanceColumn, new SliderRow("Frame limit",
                FpsLimitStopIndex(FrameTiming.FrameRateCap),
                v => _fpsLimitStops[Math.Clamp(v, 0, _fpsLimitStops.Length - 1)].Label,
                min: 0, max: _fpsLimitStops.Length - 1, keyStep: 1), SettingRowIds.FpsLimit);
            Heading(page, "Quality");
            _textureFilteringPresetRow = Add(page, new ChoiceRow("Texture filtering",
                RenderOptions.TextureFilteringLabels, (int)RenderOptions.TextureFilteringPreset), SettingRowIds.TextureFiltering);
            _anisotropyRow = Add(page, new ChoiceRow("Texture detail",
                RenderOptions.AnisotropyLabels, RenderOptions.AnisotropyIndex(RenderOptions.Anisotropy)), SettingRowIds.Anisotropy);
            _msaaRow = Add(page, new ChoiceRow("Edge smoothing",
                RenderOptions.MsaaLabels, RenderOptions.MsaaIndex(RenderOptions.Msaa)), SettingRowIds.Msaa);
            _bloomRow = Add(page, new ToggleRow("Bloom", RenderOptions.Bloom), SettingRowIds.Bloom);
            _dynamicVisualLightsRow = Add(page,
                new ToggleRow("Dynamic lighting", RenderOptions.DynamicVisualLights), SettingRowIds.DynamicLighting);
            _textureFilteringPresetRow.Changed += (_, _) => MarkGraphicsPresetCustom();
            _anisotropyRow.Changed += (_, _) => MarkGraphicsPresetCustom();
            _msaaRow.Changed += (_, _) => MarkGraphicsPresetCustom();
            _bloomRow.Changed += (_, _) => MarkGraphicsPresetCustom();
            _dynamicVisualLightsRow.Changed += (_, _) => MarkGraphicsPresetCustom();
            _lightingRow = Add(page, new ToggleRow("Lighting", RenderOptions.Lighting), SettingRowIds.Lighting);
            _fogRow = Add(page, new ToggleRow("Fog", RenderOptions.Fog), SettingRowIds.Fog);
            _fpsRow = Add(page, new ToggleRow("FPS counter", RenderOptions.ShowFps), SettingRowIds.FpsCounter);

            Heading(page, "Textures and style");
            _texturePacks = TexturePackCatalog.Discover(LauncherPrefs.Directory,
                RenderOptions.TexturePackId);
            int texturePackIndex = 0;
            for (int i = 0; i < _texturePacks.Count; i++)
            {
                if (String.Equals(_texturePacks[i].Id, RenderOptions.TexturePackId,
                    StringComparison.Ordinal))
                {
                    texturePackIndex = i;
                    break;
                }
            }
            _texturePackRow = Add(page, new ChoiceRow("Texture pack",
                _texturePacks.Select(pack => pack.Label).ToArray(), texturePackIndex),
                SettingRowIds.TexturePack);
            Explain(page, "Original uses the game assets. Installed replacement packs are read from enhancements/<pack>/materials.json and apply to the next scene.");
            _visualStyleRow = Add(page, new ChoiceRow("Visual style",
                RenderOptions.VisualStyleLabels, (int)RenderOptions.VisualStyle),
                SettingRowIds.VisualStyle);
            Explain(page, "Cel shaded adds ink and banded lighting; Flat removes texture detail; Pixelated keeps hard texels; Retro reduces and dithers color.");

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
            _proHud = Add(page, new ToggleRow("Pro HUD", Features.ProHud), SettingRowIds.ProHud);
            Explain(page, "A clean competitive layout with unobstructed vision, compact edge readouts, "
                + "a high-contrast crosshair and an always-visible weapon column.");
            _proHudWeaponRow = Add(page, new ChoiceRow("Weapon motion", new[] { "Static", "Dynamic" },
                Features.ProHudFixedWeapon ? 0 : 1), SettingRowIds.ProHudWeapon);
            _hitMarkerRow = Add(page, new ChoiceRow("Hit markers", new[] { "Off", "Visual", "Visual + audio" }, (int)Combat.CombatFeedbackSettings.HitMarkers), SettingRowIds.HitMarkers);
            _hitMarkerTimingRow = Add(page, new ChoiceRow("Hit marker timing",
                new[] { "Confirmed", "Instant" }, (int)Combat.CombatFeedbackSettings.Timing), SettingRowIds.HitMarkerTiming);
            _headshotCueRow = Add(page, new ToggleRow("Headshot cue", Combat.CombatFeedbackSettings.HeadshotCue), SettingRowIds.HeadshotCue);
            _killConfirmationRow = Add(page, new ToggleRow("Kill confirmation", Combat.CombatFeedbackSettings.KillConfirmation), SettingRowIds.KillConfirmation);
            _killcamRow = Add(page, new ToggleRow("Killcam", GameSettings.KillcamEnabled), SettingRowIds.Killcam);
            Heading(page, "Radar");
            StackPanel radarBasicSector = ActiveSector(page);
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
            _radarRangeRow = Add(page, new SliderRow("Radar range",
                (int)Math.Round(global::MphRead.Hud.Radar.RadarSettings.Range),
                v => v.ToString(CultureInfo.InvariantCulture) + " m",
                min: (int)global::MphRead.Hud.Radar.RadarSettings.MinimumRange,
                max: (int)global::MphRead.Hud.Radar.RadarSettings.MaximumRange,
                keyStep: 5), SettingRowIds.RadarRange);
            _radarOpacityRow = Add(page, new SliderRow("Radar opacity",
                (int)Math.Round(global::MphRead.Hud.Radar.RadarSettings.Opacity * 100),
                v => v.ToString(CultureInfo.InvariantCulture) + "%",
                min: (int)(global::MphRead.Hud.Radar.RadarSettings.MinimumOpacity * 100),
                max: 100, keyStep: 5), SettingRowIds.RadarOpacity);
            _radarElevationRow = Add(page, new ToggleRow("Elevation markers",
                global::MphRead.Hud.Radar.RadarSettings.ElevationIndicators), SettingRowIds.RadarElevation);
            global::MphRead.Hud.Radar.RadarProfile radar = global::MphRead.Hud.Radar.RadarSettings.DefaultProfile;
            _radarPresetRow = Add(page, new ChoiceRow("Radar preset",
                new[] { "Classic MPH", "Competitive", "Minimal", "Accessibility", "Objective Focus", "Broadcast", "Custom" },
                (int)radar.Preset), SettingRowIds.RadarPreset);
            _radarColorRow = Add(page, new ChoiceRow("Radar colors",
                new[] { "Classic", "High contrast", "Deuteranopia", "Protanopia", "Tritanopia", "Monochrome", "Custom" },
                (int)radar.ColorPreset), SettingRowIds.RadarColors);
            _radarMarkerScaleRow = Add(page, PercentSlider("Marker size", radar.MarkerScale, 50, 200), SettingRowIds.RadarMarkerScale);
            _radarMarkerOpacityRow = Add(page, PercentSlider("Marker opacity", radar.MarkerOpacity, 10, 100), SettingRowIds.RadarMarkerOpacity);
            _radarMarkerOutlineRow = Add(page, new ToggleRow("Marker outlines", radar.MarkerOutline), SettingRowIds.RadarMarkerOutline);
            _radarEdgeArrowsRow = Add(page, new ToggleRow("Off-screen arrows", radar.EdgeArrows), SettingRowIds.RadarEdgeArrows);
            _radarLabelsRow = Add(page, new ToggleRow("Contact labels", radar.Labels), SettingRowIds.RadarLabels);
            _radarObjectiveEmphasisRow = Add(page, new ToggleRow("Emphasize objectives", radar.ObjectiveEmphasis), SettingRowIds.RadarObjectiveEmphasis);
            int radarAdvancedStart = page.Children.Count;
            Heading(page, "Radar contacts");
            _radarEnemiesRow = Add(page, new ToggleRow("Enemies", radar.ShowEnemies), SettingRowIds.RadarEnemies);
            _radarTeammatesRow = Add(page, new ToggleRow("Teammates", radar.ShowTeammates), SettingRowIds.RadarTeammates);
            _radarObjectivesRow = Add(page, new ToggleRow("Objectives", radar.ShowObjectives), SettingRowIds.RadarObjectives);
            _radarFlagsRow = Add(page, new ToggleRow("Flags", radar.ShowFlags), SettingRowIds.RadarFlags);
            _radarBasesRow = Add(page, new ToggleRow("Bases", radar.ShowBases), SettingRowIds.RadarBases);
            _radarNodesRow = Add(page, new ToggleRow("Nodes", radar.ShowNodes), SettingRowIds.RadarNodes);
            _radarDefendersRow = Add(page, new ToggleRow("Defenders", radar.ShowDefenders), SettingRowIds.RadarDefenders);
            Heading(page, "Radar resources");
            _radarResourcesRow = Add(page, new ToggleRow("Resources", radar.ShowResources), SettingRowIds.RadarResources);
            _radarWeaponsRow = Add(page, new ToggleRow("Weapons", radar.ShowWeapons), SettingRowIds.RadarWeapons);
            _radarAmmoRow = Add(page, new ToggleRow("Ammo", radar.ShowAmmo), SettingRowIds.RadarAmmo);
            _radarHealthRow = Add(page, new ToggleRow("Health", radar.ShowHealth), SettingRowIds.RadarHealth);
            _radarPowerupsRow = Add(page, new ToggleRow("Powerups", radar.ShowPowerups), SettingRowIds.RadarPowerups);
            Heading(page, "Radar map");
            _radarFloorRow = Add(page, new ChoiceRow("Visible floors", new[] { "Current", "Adjacent", "All" },
                (int)radar.FloorMode), SettingRowIds.RadarFloors);
            _radarElevationThresholdRow = Add(page, new SliderRow("Elevation threshold",
                (int)Math.Round(radar.ElevationThreshold * 100), v => (v / 100f).ToString("0.00", CultureInfo.InvariantCulture) + " m",
                min: 25, max: 2000, keyStep: 25), SettingRowIds.RadarElevationThreshold);
            _radarMapFillRow = Add(page, new ToggleRow("Map fill", radar.MapFill), SettingRowIds.RadarMapFill);
            _radarMapOutlinesRow = Add(page, new ToggleRow("Map outlines", radar.MapOutlines), SettingRowIds.RadarMapOutlines);
            _radarFloorBrightnessRow = Add(page, PercentSlider("Floor brightness", radar.FloorBrightness, 0, 150), SettingRowIds.RadarFloorBrightness);
            _radarAdjacentOpacityRow = Add(page, PercentSlider("Adjacent floor opacity", radar.AdjacentFloorOpacity), SettingRowIds.RadarAdjacentOpacity);
            _radarBackgroundDimRow = Add(page, PercentSlider("Background dim", radar.BackgroundDim), SettingRowIds.RadarBackgroundDim);
            _radarBackgroundBlurRow = Add(page, PercentSlider("Background softness", radar.BackgroundBlur), SettingRowIds.RadarBackgroundBlur);
            _radarGridRow = Add(page, new ToggleRow("Grid", radar.Grid), SettingRowIds.RadarGrid);
            _radarRingsRow = Add(page, new ToggleRow("Distance rings", radar.DistanceRings), SettingRowIds.RadarRings);
            _radarCompassRow = Add(page, new ToggleRow("Compass", radar.Compass), SettingRowIds.RadarCompass);
            Heading(page, "Radar behavior");
            _radarZoomRow = Add(page, new ChoiceRow("Zoom", new[] { "Fixed", "Automatic", "Combat sensitive" },
                (int)radar.ZoomMode), SettingRowIds.RadarZoom);
            _radarAutoMinimumRow = Add(page, new SliderRow("Automatic minimum range", (int)radar.AutomaticMinimumRange,
                v => v + " m", min: 20, max: 80, keyStep: 4), SettingRowIds.RadarAutoMinimum);
            _radarAutoMaximumRow = Add(page, new SliderRow("Automatic maximum range", (int)radar.AutomaticMaximumRange,
                v => v + " m", min: 20, max: 80, keyStep: 4), SettingRowIds.RadarAutoMaximum);
            _radarZoomSmoothingRow = Add(page, new SliderRow("Zoom smoothing", (int)Math.Round(radar.ZoomSmoothingSeconds * 1000),
                v => v + " ms", min: 0, max: 2000, keyStep: 50), SettingRowIds.RadarZoomSmoothing);
            _radarPersistenceRow = Add(page, new SliderRow("Contact persistence", (int)Math.Round(radar.ContactPersistenceSeconds * 1000),
                v => v + " ms", min: 0, max: 5000, keyStep: 100), SettingRowIds.RadarPersistence);
            _radarPulseRow = Add(page, new ToggleRow("Priority contact pulse", radar.ContactPulse), SettingRowIds.RadarPulse);
            _radarPriorityRow = Add(page, new ToggleRow("Prioritize objectives at contact limit", radar.PrioritizeObjectives), SettingRowIds.RadarPriority);
            _radarEdgeScaleRow = Add(page, PercentSlider("Edge arrow size", radar.ClampedEdgeScale, 50, 200), SettingRowIds.RadarEdgeScale);
            _radarPresetRow.Changed += (_, _) =>
            {
                var preset = (global::MphRead.Hud.Radar.RadarPreset)_radarPresetRow.Index;
                if (!_applyingRadarPreset
                    && preset != global::MphRead.Hud.Radar.RadarPreset.Custom)
                {
                    ApplyRadarPresetToRows(
                        global::MphRead.Hud.Radar.RadarProfile.Create(preset));
                }
            };
            var resetRadar = new MenuEntry("Reset radar position",
                "Top right, 1.00x scale and zero offsets", titleSize: 13)
            {
                Height = 42,
                Accent = GuiTheme.Warm,
                Margin = new Thickness(0, 4, 0, 0)
            };
            resetRadar.Click += (_, _) => ResetRadarPosition();
            Add(page, resetRadar, SettingRowIds.RadarAnchor + ".reset");
            var exportRadar = new MenuEntry("Copy radar profile",
                "Includes custom colors plus mode and device layouts", titleSize: 13)
            { Height = 54, Accent = GuiTheme.Accent, Margin = new Thickness(0, 4, 0, 0) };
            exportRadar.Click += async (_, _) =>
            {
                Avalonia.Input.Platform.IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard == null) return;
                await clipboard.SetTextAsync(global::MphRead.Hud.Radar.RadarProfileSerializer.ExportBundle(
                    BuildRadarProfileFromRows(), global::MphRead.Hud.Radar.RadarSettings.ModeProfiles,
                    global::MphRead.Hud.Radar.RadarSettings.DeviceProfiles));
                exportRadar.Subtitle = "Copied to clipboard";
            };
            Add(page, exportRadar, SettingRowIds.RadarPreset + ".export");
            var importRadar = new MenuEntry("Paste radar profile",
                "Validates before replacing the preview", titleSize: 13)
            { Height = 54, Accent = GuiTheme.Warm, Margin = new Thickness(0, 4, 0, 0) };
            importRadar.Click += async (_, _) =>
            {
                Avalonia.Input.Platform.IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                string? json = clipboard == null ? null
                    : await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
                try
                {
                    global::MphRead.Hud.Radar.RadarProfileBundle bundle
                        = global::MphRead.Hud.Radar.RadarProfileSerializer.ImportBundle(json ?? "");
                    ApplyRadarPresetToRows(bundle.Profile);
                    global::MphRead.Hud.Radar.RadarSettings.ClearModeProfiles();
                    foreach ((string mode, global::MphRead.Hud.Radar.RadarProfile profile) in bundle.Modes)
                        global::MphRead.Hud.Radar.RadarSettings.SetModeProfile(mode, profile);
                    global::MphRead.Hud.Radar.RadarSettings.ClearDeviceProfiles();
                    foreach ((string device, global::MphRead.Hud.Radar.RadarProfile profile) in bundle.Devices)
                        if (Enum.TryParse(device, true, out global::MphRead.Hud.Radar.RadarDeviceClass parsed))
                            global::MphRead.Hud.Radar.RadarSettings.SetDeviceProfile(parsed, profile);
                    importRadar.Subtitle = "Profile loaded; Save to keep it";
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException
                    or System.Text.Json.JsonException or NotSupportedException)
                {
                    importRadar.Subtitle = "Clipboard does not contain a valid radar profile";
                }
            };
            Add(page, importRadar, SettingRowIds.RadarPreset + ".import");
            _radarStyleRow.Preview = (context, area) => DrawRadarPreview(context, area,
                BuildRadarProfileFromRows());
            var radarLayoutEditor = new RadarLayoutEditorPreview(
                (context, area) => DrawRadarPreview(context, area, BuildRadarProfileFromRows()),
                delta => ApplyRadarPresetToRows(global::MphRead.Hud.Radar.RadarLayoutEditor.Drag(
                    BuildRadarProfileFromRows(), new OpenTK.Mathematics.Vector2(
                        (float)delta.X, (float)delta.Y))),
                delta => ApplyRadarPresetToRows(global::MphRead.Hud.Radar.RadarLayoutEditor.Resize(
                    BuildRadarProfileFromRows(), delta)))
            { Margin = new Thickness(0, 6, 0, 2) };
            PrimeAccessibility.SetName(radarLayoutEditor,
                "Radar layout preview. Drag or use arrow keys to move; use the mouse wheel or plus and minus keys to resize.");
            Add(radarBasicSector, radarLayoutEditor, SettingRowIds.RadarAnchor + ".editor");
            EventHandler refreshRadarPreview = (_, _) => radarLayoutEditor.InvalidateVisual();
            foreach (ChoiceRow row in new[] { _radarPresetRow, _radarColorRow, _radarStyleRow,
                _radarOrientationRow, _radarPositionRow, _radarFloorRow, _radarZoomRow })
                row.Changed += refreshRadarPreview;
            foreach (ToggleRow row in new[] { _radarElevationRow, _radarMarkerOutlineRow,
                _radarEdgeArrowsRow, _radarLabelsRow, _radarObjectiveEmphasisRow,
                _radarEnemiesRow, _radarTeammatesRow, _radarObjectivesRow, _radarFlagsRow,
                _radarBasesRow, _radarNodesRow, _radarDefendersRow, _radarResourcesRow,
                _radarWeaponsRow, _radarAmmoRow, _radarHealthRow, _radarPowerupsRow,
                _radarMapFillRow,
                _radarMapOutlinesRow, _radarGridRow, _radarRingsRow, _radarCompassRow,
                _radarPulseRow, _radarPriorityRow })
                row.Changed += refreshRadarPreview;
            foreach (SliderRow row in new[] { _radarScaleRow, _radarOffsetXRow, _radarOffsetYRow,
                _radarRangeRow, _radarOpacityRow, _radarMarkerScaleRow, _radarMarkerOpacityRow,
                _radarElevationThresholdRow, _radarFloorBrightnessRow, _radarAdjacentOpacityRow,
                _radarBackgroundDimRow, _radarBackgroundBlurRow, _radarAutoMinimumRow,
                _radarAutoMaximumRow, _radarZoomSmoothingRow, _radarPersistenceRow,
                _radarEdgeScaleRow })
                row.ValueChanged += refreshRadarPreview;
            foreach (ChoiceRow row in new[] { _radarColorRow, _radarStyleRow,
                _radarOrientationRow, _radarPositionRow, _radarFloorRow, _radarZoomRow })
                row.Changed += (_, _) => MarkRadarPresetCustom();
            foreach (ToggleRow row in new[] { _radarElevationRow, _radarMarkerOutlineRow,
                _radarEdgeArrowsRow, _radarLabelsRow, _radarObjectiveEmphasisRow,
                _radarEnemiesRow, _radarTeammatesRow, _radarObjectivesRow, _radarFlagsRow,
                _radarBasesRow, _radarNodesRow, _radarDefendersRow, _radarResourcesRow,
                _radarWeaponsRow, _radarAmmoRow, _radarHealthRow, _radarPowerupsRow,
                _radarMapFillRow,
                _radarMapOutlinesRow, _radarGridRow, _radarRingsRow, _radarCompassRow,
                _radarPulseRow, _radarPriorityRow })
                row.Changed += (_, _) => MarkRadarPresetCustom();
            foreach (SliderRow row in new[] { _radarScaleRow, _radarOffsetXRow,
                _radarOffsetYRow, _radarRangeRow, _radarOpacityRow, _radarMarkerScaleRow,
                _radarMarkerOpacityRow, _radarElevationThresholdRow,
                _radarFloorBrightnessRow, _radarAdjacentOpacityRow,
                _radarBackgroundDimRow, _radarBackgroundBlurRow, _radarAutoMinimumRow,
                _radarAutoMaximumRow, _radarZoomSmoothingRow, _radarPersistenceRow,
                _radarEdgeScaleRow })
                row.ValueChanged += (_, _) => MarkRadarPresetCustom();
            _radarAutoMinimumRow.ValueChanged += (_, _) => SynchronizeRadarRange(
                changedMinimum: true);
            _radarAutoMaximumRow.ValueChanged += (_, _) => SynchronizeRadarRange(
                changedMinimum: false);
            Control[] radarAdvancedSections = page.Children
                .Skip(radarAdvancedStart).ToArray();
            var radarAdvancedContent = new StackPanel { Spacing = 8 };
            var radarMarkerContent = new StackPanel { Spacing = 2 };
            radarMarkerContent.Children.Add(new Caption("Radar markers")
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 4)
            });
            foreach (Control markerControl in new Control[]
            {
                _radarElevationRow, _radarMarkerScaleRow, _radarMarkerOpacityRow,
                _radarMarkerOutlineRow, _radarEdgeArrowsRow, _radarLabelsRow,
                _radarObjectiveEmphasisRow
            })
            {
                radarBasicSector.Children.Remove(markerControl);
                radarMarkerContent.Children.Add(markerControl);
            }
            radarAdvancedContent.Children.Add(
                PrimeControlFactory.SectionPanel(radarMarkerContent));
            foreach (Control section in radarAdvancedSections)
            {
                page.Children.Remove(section);
                radarAdvancedContent.Children.Add(section);
            }
            var radarAdvanced = new Expander
            {
                Header = "Advanced radar",
                Content = radarAdvancedContent,
                IsExpanded = false,
                Margin = new Thickness(0, 4, 0, 2)
            };
            PrimeAccessibility.SetName(radarAdvanced, "Advanced radar settings");
            AddPageRoot(page, radarAdvanced);

            void RefreshRadarOptionVisibility()
            {
                bool enhanced = _radarStyleRow.Index
                    == (int)global::MphRead.Hud.Radar.RadarStyle.Enhanced;
                bool customPosition = _radarPositionRow.Index
                    == (int)global::MphRead.Hud.Radar.RadarAnchor.Custom;
                bool automaticZoom = _radarZoomRow.Index
                    != (int)global::MphRead.Hud.Radar.RadarZoomMode.Fixed;
                bool objectives = _radarObjectivesRow.On;
                bool resources = _radarResourcesRow.On;
                _radarOffsetXRow.IsVisible = customPosition;
                _radarOffsetYRow.IsVisible = customPosition;
                radarAdvanced.IsVisible = enhanced;
                _radarRangeRow.IsVisible = !automaticZoom;
                _radarAutoMinimumRow.IsVisible = automaticZoom;
                _radarAutoMaximumRow.IsVisible = automaticZoom;
                _radarZoomSmoothingRow.IsVisible = automaticZoom;
                _radarFlagsRow.IsVisible = objectives;
                _radarBasesRow.IsVisible = objectives;
                _radarNodesRow.IsVisible = objectives;
                _radarDefendersRow.IsVisible = objectives;
                _radarObjectiveEmphasisRow.IsVisible = objectives;
                _radarWeaponsRow.IsEnabled = resources;
                _radarAmmoRow.IsEnabled = resources;
                _radarHealthRow.IsEnabled = resources;
                _radarPowerupsRow.IsEnabled = resources;
                _radarElevationThresholdRow.IsVisible = _radarElevationRow.On;
                _radarEdgeScaleRow.IsVisible = _radarEdgeArrowsRow.On;
                _radarFloorBrightnessRow.IsVisible = _radarMapFillRow.On;
                _radarAdjacentOpacityRow.IsVisible = _radarFloorRow.Index
                    != (int)global::MphRead.Hud.Radar.RadarFloorMode.Current;
            }
            _radarStyleRow.Changed += (_, _) => RefreshRadarOptionVisibility();
            _radarPositionRow.Changed += (_, _) => RefreshRadarOptionVisibility();
            _radarZoomRow.Changed += (_, _) => RefreshRadarOptionVisibility();
            _radarObjectivesRow.Changed += (_, _) => RefreshRadarOptionVisibility();
            _radarResourcesRow.Changed += (_, _) => RefreshRadarOptionVisibility();
            _radarElevationRow.Changed += (_, _) => RefreshRadarOptionVisibility();
            _radarEdgeArrowsRow.Changed += (_, _) => RefreshRadarOptionVisibility();
            _radarMapFillRow.Changed += (_, _) => RefreshRadarOptionVisibility();
            _radarFloorRow.Changed += (_, _) => RefreshRadarOptionVisibility();
            RefreshRadarOptionVisibility();
            // The crosshair questions belong to Pro mode and nothing else --
            // the DS HUD draws its own reticle sprite and has no use for
            // them -- so they are only asked while it is on. Shown rather than
            // greyed: a row that cannot be answered is still a row to read
            // past, and this page is long enough.
            Heading(page, "Crosshair");
            _crosshairSizeRow = Add(page, new ChoiceRow("Crosshair size",
                Crosshair.SizeNames, (int)Crosshair.Size), SettingRowIds.CrosshairSize);
            _crosshairStyleRow = Add(page, new ChoiceRow("Crosshair type",
                Crosshair.StyleNames, (int)Crosshair.Style), SettingRowIds.CrosshairStyle);
            _reticleOpacity = Add(page, new SliderRow("Reticle opacity",
                OpacityToSlider(Features.ReticleOpacity),
                v => $"{SliderToOpacity(v) * 100:0}%"), SettingRowIds.ReticleOpacity);
            _reticleScale = Add(page, new SliderRow("Reticle size",
                ReticleScaleToSlider(Features.ReticleScale),
                v => $"{SliderToReticleScale(v) * 100:0}%",
                min: (int)(Features.MinimumReticleScale * 100),
                max: (int)(Features.MaximumReticleScale * 100),
                keyStep: 5), SettingRowIds.ReticleScale);
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
            IDisposable? suppression = SuppressDirtyTracking();
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
                suppression?.Dispose();
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
        {
            int index = Math.Clamp((int)_graphicsPresetBase, 0,
                RenderOptions.GraphicsPresetLabels.Count - 1);
            return $"Custom · Modified from {RenderOptions.GraphicsPresetLabels[index]}.";
        }

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
            _dirtyTracker?.Refresh();
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
            IDisposable? suppression = SuppressDirtyTracking();
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
                suppression?.Dispose();
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
            IDisposable? suppression = SuppressDirtyTracking();
            bool wasRefreshing = _refreshingControllerRows;
            _refreshingControllerRows = true;
            try
            {
                _gamepadPresetRow.Index = (int)InputSettings.ControllerPreset;
            }
            finally
            {
                _refreshingControllerRows = wasRefreshing;
                suppression?.Dispose();
            }
        }

        private void RefreshControllerRows()
        {
            IDisposable? suppression = SuppressDirtyTracking();
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
                suppression?.Dispose();
            }
            // Every controller slider was repopulated from authoritative
            // state, so no old UI edit marker may survive this full reset.
            _editedControllerSliders.Clear();
        }

        private void RefreshControllerAimRows()
        {
            IDisposable? suppression = SuppressDirtyTracking();
            _refreshingControllerRows = true;
            try
            {
                RefreshControllerAimRowsCore();
            }
            finally
            {
                _refreshingControllerRows = false;
                suppression?.Dispose();
            }
            ClearEditedControllerSliders(
                _gamepadHorizontalSensitivity, _gamepadVerticalSensitivity,
                _gamepadGyroSensitivity, _gamepadDeadZone, _gamepadLookDeadZone,
                _gamepadMoveActivate, _gamepadMoveRelease, _gamepadLook,
                _gamepadYawRate, _gamepadPitchRate, _gamepadZoomMultiplier,
                _gamepadZoomVerticalMultiplier);
        }

        private void RefreshControllerGeneralRows()
        {
            IDisposable? suppression = SuppressDirtyTracking();
            _refreshingControllerRows = true;
            try
            {
                _gamepadHorizontalSensitivity.Value = LookToSlider(
                    InputSettings.GamepadHorizontalSensitivity);
                _gamepadVerticalSensitivity.Value = LookToSlider(
                    InputSettings.GamepadVerticalSensitivity);
                _gamepadInvertY.On = InputSettings.GamepadInvertY;
                _gamepadZoomMultiplier.Value = LookToSlider(
                    InputSettings.GamepadZoomHorizontalMultiplier);
                _gamepadZoomVerticalMultiplier.Value = LookToSlider(
                    InputSettings.GamepadZoomVerticalMultiplier);
                _gamepadHaptics.On = InputSettings.GamepadHapticsEnabled;
                _gamepadHapticsStrength.Value = (int)Math.Round(
                    InputSettings.GamepadHapticsStrength * 100);
            }
            finally
            {
                _refreshingControllerRows = false;
                suppression?.Dispose();
            }
            ClearEditedControllerSliders(_gamepadHorizontalSensitivity,
                _gamepadVerticalSensitivity, _gamepadZoomMultiplier,
                _gamepadZoomVerticalMultiplier,
                _gamepadHapticsStrength);
        }

        private void RefreshControllerGyroRows()
        {
            IDisposable? suppression = SuppressDirtyTracking();
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
                suppression?.Dispose();
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
            _gamepadZoomMultiplier.Value = LookToSlider(
                InputSettings.GamepadZoomHorizontalMultiplier);
            _gamepadZoomVerticalMultiplier.Value = LookToSlider(
                InputSettings.GamepadZoomVerticalMultiplier);
            _gamepadStickAimMode.Index = (int)InputSettings.GamepadStickAimMode;
        }

        private void RefreshControllerAdvancedRows()
        {
            IDisposable? suppression = SuppressDirtyTracking();
            _refreshingControllerRows = true;
            try
            {
                RefreshControllerAdvancedRowsCore();
            }
            finally
            {
                _refreshingControllerRows = false;
                suppression?.Dispose();
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
            _gamepadAutoCalibration.On = InputSettings.GamepadAutoCalibrationEnabled;
            ShowOuterBoostRows();
        }

        private void RefreshControlRows()
        {
            IDisposable? suppression = SuppressDirtyTracking();
            try
            {
                _dynamicCrosshairTravel.Value = (int)Math.Round(
                    InputSettings.DynamicCrosshairTravelDegrees);
                _dynamicCrosshairSensitivity.Value = (int)Math.Round(
                    InputSettings.DynamicCrosshairSensitivity * 100);
                _dynamicCrosshairTurnSpeed.Value = (int)Math.Round(
                    InputSettings.DynamicCrosshairTurnSpeed * 100);
                _dynamicCrosshairTravelEdited = false;
                _dynamicCrosshairSensitivityEdited = false;
                _dynamicCrosshairTurnSpeedEdited = false;
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
                    _bottomScreenMode!.Index = (int)InputSettings.BottomScreenMode;
                    _bottomScreenActivation!.Index
                        = (int)InputSettings.BottomScreenActivation;
                    _bottomScreenCursorSensitivity!.Value = (int)Math.Round(
                        InputSettings.BottomScreenCursorSensitivity * 100);
                    _bottomScreenCursorStartX!.Value = (int)Math.Round(
                        InputSettings.BottomScreenCursorStartX * 100);
                    _bottomScreenCursorStartY!.Value = (int)Math.Round(
                        InputSettings.BottomScreenCursorStartY * 100);
                    _bottomScreenPowerBeamX!.Value = (int)Math.Round(
                        InputSettings.BottomScreenPowerBeamX * 100);
                    _bottomScreenPowerBeamY!.Value = (int)Math.Round(
                        InputSettings.BottomScreenPowerBeamY * 100);
                    _bottomScreenMissileX!.Value = (int)Math.Round(
                        InputSettings.BottomScreenMissileX * 100);
                    _bottomScreenMissileY!.Value = (int)Math.Round(
                        InputSettings.BottomScreenMissileY * 100);
                    _bottomScreenNextWeaponX!.Value = (int)Math.Round(
                        InputSettings.BottomScreenNextWeaponX * 100);
                    _bottomScreenNextWeaponY!.Value = (int)Math.Round(
                        InputSettings.BottomScreenNextWeaponY * 100);
                    _bottomScreenWeaponSelectX!.Value = (int)Math.Round(
                        InputSettings.BottomScreenWeaponSelectX * 100);
                    _bottomScreenWeaponSelectY!.Value = (int)Math.Round(
                        InputSettings.BottomScreenWeaponSelectY * 100);
                    _bottomScreenAltFormX!.Value = (int)Math.Round(
                        InputSettings.BottomScreenAltFormX * 100);
                    _bottomScreenAltFormY!.Value = (int)Math.Round(
                        InputSettings.BottomScreenAltFormY * 100);
                    float[] affinityValues = BottomScreenAffinityValues();
                    for (int index = 0; index < _bottomScreenAffinityRows.Length; index++)
                    {
                        _bottomScreenAffinityRows[index].Value =
                            (int)Math.Round(affinityValues[index] * 100);
                    }
                    _bottomScreenDirectionalSwipeAssist!.On
                        = InputSettings.BottomScreenDirectionalSwipeAssist;
                    _bottomScreenStyle!.Index = (int)InputSettings.BottomScreenStyle;
                    _bottomScreenScale!.Value = (int)Math.Round(
                        InputSettings.BottomScreenScale * 100);
                    _bottomScreenCenterX!.Value = (int)Math.Round(
                        InputSettings.BottomScreenCenterX * 100);
                    _bottomScreenCenterY!.Value = (int)Math.Round(
                        InputSettings.BottomScreenCenterY * 100);
                    _bottomScreenOpacity!.Value = (int)Math.Round(
                        InputSettings.BottomScreenOpacity * 100);
                    _bottomScreenLabels!.On = InputSettings.BottomScreenLabels;
                }
            }
            finally
            {
                suppression?.Dispose();
            }
        }

        private global::MphRead.Hud.Radar.RadarProfile BuildRadarProfileFromRows()
        {
            var preset = (global::MphRead.Hud.Radar.RadarPreset)_radarPresetRow.Index;
            global::MphRead.Hud.Radar.RadarColorPreset colorPreset
                = (global::MphRead.Hud.Radar.RadarColorPreset)_radarColorRow.Index;
            global::MphRead.Hud.Radar.RadarColors colors
                = colorPreset == global::MphRead.Hud.Radar.RadarColorPreset.Custom
                    ? global::MphRead.Hud.Radar.RadarSettings.DefaultProfile.Colors
                    : global::MphRead.Hud.Radar.RadarColors.For(colorPreset);
            return (global::MphRead.Hud.Radar.RadarSettings.DefaultProfile with
            {
                Name = global::MphRead.Hud.Radar.RadarProfile.Create(preset).Name,
                Preset = preset,
                Style = (global::MphRead.Hud.Radar.RadarStyle)_radarStyleRow.Index,
                Orientation = (global::MphRead.Hud.Radar.RadarOrientation)_radarOrientationRow.Index,
                Anchor = (global::MphRead.Hud.Radar.RadarAnchor)_radarPositionRow.Index,
                Scale = SliderToRadarScale(_radarScaleRow.Value), OffsetX = _radarOffsetXRow.Value,
                OffsetY = _radarOffsetYRow.Value, Range = _radarRangeRow.Value,
                Opacity = _radarOpacityRow.Value / 100f, ElevationIndicators = _radarElevationRow.On,
                ColorPreset = colorPreset, Colors = colors,
                MarkerScale = _radarMarkerScaleRow.Value / 100f,
                MarkerOpacity = _radarMarkerOpacityRow.Value / 100f,
                MarkerOutline = _radarMarkerOutlineRow.On, EdgeArrows = _radarEdgeArrowsRow.On,
                Labels = _radarLabelsRow.On, ObjectiveEmphasis = _radarObjectiveEmphasisRow.On,
                ShowEnemies = _radarEnemiesRow.On, ShowTeammates = _radarTeammatesRow.On,
                ShowObjectives = _radarObjectivesRow.On, ShowFlags = _radarFlagsRow.On,
                ShowBases = _radarBasesRow.On, ShowNodes = _radarNodesRow.On,
                ShowDefenders = _radarDefendersRow.On,
                ShowResources = _radarResourcesRow.On, ShowWeapons = _radarWeaponsRow.On,
                ShowAmmo = _radarAmmoRow.On, ShowHealth = _radarHealthRow.On,
                ShowPowerups = _radarPowerupsRow.On,
                FloorMode = (global::MphRead.Hud.Radar.RadarFloorMode)_radarFloorRow.Index,
                ElevationThreshold = _radarElevationThresholdRow.Value / 100f,
                MapFill = _radarMapFillRow.On, MapOutlines = _radarMapOutlinesRow.On,
                FloorBrightness = _radarFloorBrightnessRow.Value / 100f,
                AdjacentFloorOpacity = _radarAdjacentOpacityRow.Value / 100f,
                BackgroundDim = _radarBackgroundDimRow.Value / 100f,
                BackgroundBlur = _radarBackgroundBlurRow.Value / 100f,
                Grid = _radarGridRow.On, DistanceRings = _radarRingsRow.On, Compass = _radarCompassRow.On,
                ZoomMode = (global::MphRead.Hud.Radar.RadarZoomMode)_radarZoomRow.Index,
                AutomaticMinimumRange = _radarAutoMinimumRow.Value,
                AutomaticMaximumRange = _radarAutoMaximumRow.Value,
                ZoomSmoothingSeconds = _radarZoomSmoothingRow.Value / 1000f,
                ContactPersistenceSeconds = _radarPersistenceRow.Value / 1000f,
                ContactPulse = _radarPulseRow.On, ClampedEdgeScale = _radarEdgeScaleRow.Value / 100f,
                PrioritizeObjectives = _radarPriorityRow.On
            }).Normalize();
        }

        private void MarkRadarPresetCustom()
        {
            if (_applyingRadarPreset
                || _radarPresetRow.Index
                    == (int)global::MphRead.Hud.Radar.RadarPreset.Custom)
            {
                return;
            }
            _radarPresetRow.Index = (int)global::MphRead.Hud.Radar.RadarPreset.Custom;
        }

        private void SynchronizeRadarRange(bool changedMinimum)
        {
            if (_synchronizingRadarRange) return;
            _synchronizingRadarRange = true;
            try
            {
                if (changedMinimum && _radarAutoMinimumRow.Value > _radarAutoMaximumRow.Value)
                    _radarAutoMaximumRow.Value = _radarAutoMinimumRow.Value;
                else if (!changedMinimum
                    && _radarAutoMaximumRow.Value < _radarAutoMinimumRow.Value)
                    _radarAutoMinimumRow.Value = _radarAutoMaximumRow.Value;
            }
            finally
            {
                _synchronizingRadarRange = false;
            }
        }

        private void ApplyRadarPresetToRows(global::MphRead.Hud.Radar.RadarProfile profile)
        {
            _applyingRadarPreset = true;
            try
            {
            _radarPresetRow.Index = (int)profile.Preset;
            _radarStyleRow.Index = (int)profile.Style;
            _radarOrientationRow.Index = (int)profile.Orientation;
            _radarPositionRow.Index = (int)profile.Anchor;
            _radarScaleRow.Value = RadarScaleToSlider(profile.Scale);
            _radarOffsetXRow.Value = (int)profile.OffsetX; _radarOffsetYRow.Value = (int)profile.OffsetY;
            _radarRangeRow.Value = (int)profile.Range; _radarOpacityRow.Value = (int)(profile.Opacity * 100);
            _radarElevationRow.On = profile.ElevationIndicators; _radarColorRow.Index = (int)profile.ColorPreset;
            _radarMarkerScaleRow.Value = (int)(profile.MarkerScale * 100);
            _radarMarkerOpacityRow.Value = (int)(profile.MarkerOpacity * 100);
            _radarMarkerOutlineRow.On = profile.MarkerOutline; _radarEdgeArrowsRow.On = profile.EdgeArrows;
            _radarLabelsRow.On = profile.Labels; _radarObjectiveEmphasisRow.On = profile.ObjectiveEmphasis;
            _radarEnemiesRow.On = profile.ShowEnemies; _radarTeammatesRow.On = profile.ShowTeammates;
            _radarObjectivesRow.On = profile.ShowObjectives; _radarFlagsRow.On = profile.ShowFlags;
            _radarBasesRow.On = profile.ShowBases; _radarNodesRow.On = profile.ShowNodes;
            _radarDefendersRow.On = profile.ShowDefenders;
            _radarResourcesRow.On = profile.ShowResources; _radarWeaponsRow.On = profile.ShowWeapons;
            _radarAmmoRow.On = profile.ShowAmmo; _radarHealthRow.On = profile.ShowHealth;
            _radarPowerupsRow.On = profile.ShowPowerups;
            _radarFloorRow.Index = (int)profile.FloorMode;
            _radarElevationThresholdRow.Value = (int)(profile.ElevationThreshold * 100);
            _radarMapFillRow.On = profile.MapFill; _radarMapOutlinesRow.On = profile.MapOutlines;
            _radarFloorBrightnessRow.Value = (int)(profile.FloorBrightness * 100);
            _radarAdjacentOpacityRow.Value = (int)(profile.AdjacentFloorOpacity * 100);
            _radarBackgroundDimRow.Value = (int)(profile.BackgroundDim * 100);
            _radarBackgroundBlurRow.Value = (int)(profile.BackgroundBlur * 100);
            _radarGridRow.On = profile.Grid; _radarRingsRow.On = profile.DistanceRings;
            _radarCompassRow.On = profile.Compass; _radarZoomRow.Index = (int)profile.ZoomMode;
            _radarAutoMinimumRow.Value = (int)profile.AutomaticMinimumRange;
            _radarAutoMaximumRow.Value = (int)profile.AutomaticMaximumRange;
            _radarZoomSmoothingRow.Value = (int)(profile.ZoomSmoothingSeconds * 1000);
            _radarPersistenceRow.Value = (int)(profile.ContactPersistenceSeconds * 1000);
            _radarPulseRow.On = profile.ContactPulse; _radarEdgeScaleRow.Value = (int)(profile.ClampedEdgeScale * 100);
            _radarPriorityRow.On = profile.PrioritizeObjectives;
            }
            finally
            {
                _applyingRadarPreset = false;
            }
        }

        private static SliderRow PercentSlider(string label, float value, int minimum = 0, int maximum = 100)
            => new(label, (int)Math.Round(value * 100), v => v + "%", min: minimum, max: maximum, keyStep: 5);

        private static void DrawRadarPreview(DrawingContext context, Rect area,
            global::MphRead.Hud.Radar.RadarProfile profile)
        {
            global::MphRead.Hud.Radar.RadarLayout layout
                = global::MphRead.Hud.Radar.RadarLayoutCalculator.Calculate(profile.Anchor,
                    profile.Scale, profile.OffsetX, profile.OffsetY, 1);
            double unit = Math.Min(area.Width / 256, area.Height / 192);
            double radius = Math.Max(4, layout.Radius * unit);
            Point center = new(area.X + layout.CenterX / 256 * area.Width,
                area.Y + layout.CenterY / 192 * area.Height);
            var radarBounds = new Rect(
                area.X + layout.Left / 256 * area.Width,
                area.Y + layout.Top / 192 * area.Height,
                Math.Max(8, layout.Width / 256 * area.Width),
                Math.Max(8, layout.Height / 192 * area.Height));
            IBrush background = Brush(profile.Colors.Background,
                profile.Opacity * profile.BackgroundDim);
            var borderPen = new Pen(Brush(profile.Colors.Border, profile.Opacity), 1.35);
            var ringPen = new Pen(Brush(profile.Colors.Ring, profile.Opacity), 1);
            context.DrawRectangle(background, ringPen, radarBounds);
            DrawRadarPreviewCorners(context, radarBounds, borderPen);
            if (profile.DistanceRings)
            {
                context.DrawEllipse(null, ringPen, center, radius * .5, radius * .5);
                context.DrawEllipse(null, ringPen, center, radius, radius);
            }
            if (profile.Grid)
            {
                context.DrawLine(ringPen, new Point(center.X - radius, center.Y),
                    new Point(center.X + radius, center.Y));
                context.DrawLine(ringPen, new Point(center.X, center.Y - radius),
                    new Point(center.X, center.Y + radius));
            }
            if (profile.Style == global::MphRead.Hud.Radar.RadarStyle.Enhanced)
            {
                global::MphRead.Hud.Radar.RadarFrame preview = global::MphRead.Hud.Radar.RadarPreview.CreateFrame();
                foreach (global::MphRead.Hud.Radar.RadarContact contact in preview.Contacts)
                {
                    if (!global::MphRead.Hud.Radar.RadarPresentationPolicy.IsVisible(profile, contact)) continue;
                    global::MphRead.Hud.Radar.RadarPoint point = global::MphRead.Hud.Radar.RadarWidget.Project(
                        contact, preview.Origin, preview.Facing, profile.Orientation, profile.Range, profile.ElevationThreshold);
                    if (point.Clamped && !profile.EdgeArrows) continue;
                    global::MphRead.Hud.Radar.RadarColor color = contact.Type switch
                    {
                        global::MphRead.Hud.Radar.RadarContactType.Enemy => profile.Colors.Enemy,
                        global::MphRead.Hud.Radar.RadarContactType.Teammate => profile.Colors.Teammate,
                        global::MphRead.Hud.Radar.RadarContactType.PrimeHunter => profile.Colors.PrimeHunter,
                        _ => profile.Colors.Objective
                    };
                    double markerScale = profile.MarkerScale
                        * (contact.Type == global::MphRead.Hud.Radar.RadarContactType.Objective
                            && profile.ObjectiveEmphasis ? 1.3 : 1);
                    double contactRadius = point.Clamped
                        ? global::MphRead.Hud.Radar.RadarPresentationPolicy.EdgeMarkerRadius(
                            (float)radius, (float)markerScale, profile.ClampedEdgeScale)
                        : radius;
                    var contactCenter = new Point(
                        center.X + point.RelativePosition.X * contactRadius,
                        center.Y + point.RelativePosition.Y * contactRadius);
                    IBrush fill = Brush(color, profile.Opacity * profile.MarkerOpacity);
                    if (point.Clamped)
                    {
                        DrawRadarPreviewEdgeMarker(context, contactCenter,
                            new Vector(point.RelativePosition.X, point.RelativePosition.Y),
                            3.5 * markerScale * profile.ClampedEdgeScale, fill,
                            profile.MarkerOutline);
                    }
                    else
                    {
                        DrawRadarPreviewMarker(context, contactCenter,
                            2.8 * markerScale,
                            global::MphRead.Hud.Radar.RadarPresentationPolicy.MarkerShape(contact),
                            fill, profile.MarkerOutline, background);
                    }
                    if (profile.ElevationIndicators
                        && point.Elevation != global::MphRead.Hud.Radar.RadarElevation.Same)
                    {
                        global::MphRead.Hud.Radar.RadarColor elevation
                            = point.Elevation == global::MphRead.Hud.Radar.RadarElevation.Below
                                ? profile.Colors.Below : profile.Colors.Above;
                        double direction = point.Elevation
                            == global::MphRead.Hud.Radar.RadarElevation.Above ? -1 : 1;
                        var elevationPen = new Pen(Brush(elevation, profile.Opacity), 1.25);
                        Point tip = contactCenter + new Vector(5 * markerScale,
                            direction * 4.5 * markerScale);
                        context.DrawLine(elevationPen, tip,
                            tip + new Vector(-1.7 * markerScale, -direction * 2 * markerScale));
                        context.DrawLine(elevationPen, tip,
                            tip + new Vector(1.7 * markerScale, -direction * 2 * markerScale));
                    }
                }
            }
            DrawRadarPreviewMarker(context, center, 3.3,
                global::MphRead.Hud.Radar.RadarMarkerShape.Triangle,
                Brush(profile.Colors.Player, profile.Opacity), outline: true, background);

            var forwardPen = new Pen(Brush(profile.Colors.Objective, profile.Opacity), 1.5);
            context.DrawLine(forwardPen,
                new Point(center.X, radarBounds.Top + 2),
                new Point(center.X, radarBounds.Top + 7));
        }

        private static void DrawRadarPreviewCorners(DrawingContext context,
            Rect bounds, Pen pen)
        {
            double length = Math.Min(10, Math.Min(bounds.Width, bounds.Height) / 4);
            context.DrawLine(pen, bounds.TopLeft, bounds.TopLeft + new Vector(length, 0));
            context.DrawLine(pen, bounds.TopLeft, bounds.TopLeft + new Vector(0, length));
            context.DrawLine(pen, bounds.TopRight, bounds.TopRight + new Vector(-length, 0));
            context.DrawLine(pen, bounds.TopRight, bounds.TopRight + new Vector(0, length));
            context.DrawLine(pen, bounds.BottomLeft, bounds.BottomLeft + new Vector(length, 0));
            context.DrawLine(pen, bounds.BottomLeft, bounds.BottomLeft + new Vector(0, -length));
            context.DrawLine(pen, bounds.BottomRight, bounds.BottomRight + new Vector(-length, 0));
            context.DrawLine(pen, bounds.BottomRight, bounds.BottomRight + new Vector(0, -length));
        }

        private static void DrawRadarPreviewMarker(DrawingContext context, Point center,
            double size, global::MphRead.Hud.Radar.RadarMarkerShape shape,
            IBrush fill, bool outline, IBrush background)
        {
            Pen? pen = outline ? new Pen(Brushes.Black, 1.2) : null;
            if (shape == global::MphRead.Hud.Radar.RadarMarkerShape.Square)
            {
                context.DrawRectangle(fill, pen,
                    new Rect(center.X - size, center.Y - size, size * 2, size * 2));
                return;
            }
            Point[] points = shape == global::MphRead.Hud.Radar.RadarMarkerShape.Triangle
                ? [center + new Vector(0, -size), center + new Vector(-size, size),
                    center + new Vector(size, size)]
                : [center + new Vector(0, -size), center + new Vector(size, 0),
                    center + new Vector(0, size), center + new Vector(-size, 0)];
            context.DrawGeometry(fill, pen, Polygon(points));
            if (shape == global::MphRead.Hud.Radar.RadarMarkerShape.DoubleDiamond)
            {
                double inner = size * .42;
                context.DrawGeometry(background, null, Polygon(
                    [center + new Vector(0, -inner), center + new Vector(inner, 0),
                        center + new Vector(0, inner), center + new Vector(-inner, 0)]));
            }
        }

        private static void DrawRadarPreviewEdgeMarker(DrawingContext context,
            Point center, Vector direction, double size, IBrush fill, bool outline)
        {
            double length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            Vector forward = length > .0001 ? direction / length : new Vector(0, -1);
            Vector tangent = new(-forward.Y, forward.X);
            context.DrawGeometry(fill, outline ? new Pen(Brushes.Black, 1.2) : null,
                Polygon([center + forward * size, center - forward * size
                    + tangent * size * .7, center - forward * size - tangent * size * .7]));
        }

        private static StreamGeometry Polygon(IReadOnlyList<Point> points)
        {
            var geometry = new StreamGeometry();
            using StreamGeometryContext path = geometry.Open();
            path.BeginFigure(points[0], isFilled: true);
            for (int index = 1; index < points.Count; index++) path.LineTo(points[index]);
            path.EndFigure(isClosed: true);
            return geometry;
        }

        private static IBrush Brush(global::MphRead.Hud.Radar.RadarColor color,
            float opacity = 1)
        {
            byte alpha = (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255);
            return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        // --------------------------------------------------------------- audio

        private void BuildAudio()
        {
            StackPanel page = AddSection("Audio");
            Heading(page, "Volume");
            _sfxVolume = Add(page, new SliderRow("Sound effects",
                Percent(_settings.SfxVolume, 35)), SettingRowIds.SfxVolume);
            _feedbackVolume = Add(page, new SliderRow("Combat feedback", (int)(Combat.FeedbackAudio.Volume * 100), v => $"{v}%"), SettingRowIds.FeedbackVolume);
            _musicVolume = Add(page, new SliderRow("Music", Percent(_settings.MusicVolume, 50)), SettingRowIds.MusicVolume);
            Heading(page, "Presentation packs");
            ClientPresentationContentState content = ClientPresentationContent.Refresh();
            _announcerPacks = ClientPresentationContent.Packs(content, OptionalPresentationKind.Announcer);
            _musicPacks = ClientPresentationContent.Packs(content, OptionalPresentationKind.Music);
            _announcerPackRow = Add(page, new ChoiceRow("Announcer",
                PackLabels(_announcerPacks), SelectedPackIndex(_announcerPacks, LauncherPrefs.AnnouncerPack)), SettingRowIds.AnnouncerPack);
            _announcerPackDetails = new Note(
                PackTechnicalDetails(_announcerPacks, _announcerPackRow.Index));
            _announcerPackDetailsExpander = Add(page, new Expander
            {
                Header = "Package details",
                Content = _announcerPackDetails,
                IsExpanded = false,
                Margin = new Thickness(0, 0, 0, 2)
            });
            _announcerPackRow.Changed += (_, _) =>
                _announcerPackDetails.Text = PackTechnicalDetails(
                    _announcerPacks, _announcerPackRow.Index);
            _musicPackRow = Add(page, new ChoiceRow("Music pack",
                PackLabels(_musicPacks), SelectedPackIndex(_musicPacks, LauncherPrefs.MusicPack)), SettingRowIds.MusicPack);
            _musicPackDetails = new Note(
                PackTechnicalDetails(_musicPacks, _musicPackRow.Index));
            _musicPackDetailsExpander = Add(page, new Expander
            {
                Header = "Package details",
                Content = _musicPackDetails,
                IsExpanded = false,
                Margin = new Thickness(0, 0, 0, 2)
            });
            _musicPackRow.Changed += (_, _) =>
                _musicPackDetails.Text = PackTechnicalDetails(
                    _musicPacks, _musicPackRow.Index);
            Explain(page, "Missing or invalid selections use built-in presentation; changes apply to the next match.");
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
                labels[i + 1] = PresentationPackLabel(identity,
                    packs[i].Manifest.DisplayName);
            }
            return labels;
        }

        internal static string PresentationPackLabel(ContentPackIdentity identity,
            string? displayName)
        {
            string primary = String.IsNullOrWhiteSpace(displayName)
                ? identity.StableId : displayName.Trim();
            return $"{primary} · Version {identity.Version}";
        }

        private static string PackTechnicalDetails(
            IReadOnlyList<InstalledOptionalPresentationPack> packs, int index)
        {
            if (index <= 0 || index > packs.Count)
            {
                return "Built-in presentation has no package ID or content hash.\n"
                    + $"Content folder: {LauncherPrefs.OptionalContentDirectory}";
            }
            ContentPackIdentity identity = packs[index - 1].Identity;
            return $"Package ID: {identity.StableId}\n"
                + $"Content Hash: {identity.ContentHash}\n"
                + $"Content folder: {LauncherPrefs.OptionalContentDirectory}";
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

        private bool IsAndroidSettingsPlatform
            => OperatingSystem.IsAndroid() || _captureTouchControls;

        private void BuildControlsTabs(SettingsPage section)
        {
            var title = new Caption("Input device");
            section.Children.Add(title);

            var tabStrip = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 4)
            };
            section.Children.Add(tabStrip);

            var tabHost = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            section.Children.Add(tabHost);

            if (IsAndroidSettingsPlatform)
            {
                // Keep the existing mouse/keyboard rows alive for their
                // shared commit/reset paths, but do not present a tab for an
                // input source the Android surface does not advertise.
                _mouseKeyboardControlsPage = new SettingsPage
                {
                    Spacing = 10,
                    IsVisible = false
                };
                section.Children.Add(_mouseKeyboardControlsPage);
                _touchControlsPage = AddControlsTab(tabStrip, tabHost,
                    "Touch", "On-screen controls and gestures");
            }
            else
            {
                _mouseKeyboardControlsPage = AddControlsTab(tabStrip, tabHost,
                    "Mouse & Keyboard", "Mouse, keyboard and chat");
            }

            _gamepadControlsPage = AddControlsTab(tabStrip, tabHost,
                "Gamepad", "Controller aim, response and bindings");

            // SDL exposes real pen events on desktop too. Keep stylus
            // configuration visible anywhere the dedicated input path exists
            // instead of silently enabling an input source the player cannot
            // tune outside Android.
            _stylusControlsPage = AddControlsTab(tabStrip, tabHost,
                "Stylus", "DS-style pen aiming and gestures");

            // At least Gamepad is always present. The first page is selected
            // once, after all pages have been created, and later tab changes
            // only toggle visibility on these same controls.
            ShowControlsTab(_controlTabs[0].Button.Title);
        }

        private SettingsPage AddControlsTab(WrapPanel tabStrip, Grid tabHost,
            string name, string subtitle)
        {
            var page = new SettingsPage
            {
                Spacing = 10,
                IsVisible = false
            };
            var tab = new ControlsTab(name, subtitle, page);
            tab.Button.Click += (_, _) => ShowControlsTab(name);
            _controlTabs.Add(tab);
            tabStrip.Children.Add(tab.Button);
            tabHost.Children.Add(page);
            return page;
        }

        internal void ShowControlsTab(string name)
        {
            ControlsTab? selected = _controlTabs.FirstOrDefault(tab =>
                String.Equals(tab.Button.Title, name, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
            {
                return;
            }
            foreach (ControlsTab tab in _controlTabs)
            {
                bool isSelected = ReferenceEquals(tab, selected);
                tab.Page.IsVisible = isSelected;
                tab.Button.Selected = isSelected;
            }
        }

        private void BuildControls()
        {
            SettingsPage section = (SettingsPage)AddSection("Controls");
            Heading(section, "Dynamic crosshair");
            Explain(section, "Shared by mouse, controller, touch and stylus aiming.");
            _dynamicCrosshairTravel = Add(section, new SliderRow("Free-aim travel",
                (int)Math.Round(InputSettings.DynamicCrosshairTravelDegrees),
                value => $"{value}°", min: 0, max: 30, keyStep: 1),
                SettingRowIds.DynamicCrosshairTravel);
            _dynamicCrosshairSensitivity = Add(section, new SliderRow(
                "Crosshair sensitivity",
                (int)Math.Round(InputSettings.DynamicCrosshairSensitivity * 100),
                value => $"{value / 100f:0.00}x", min: 10,
                max: (int)(DynamicCrosshairTuning.MaximumMovementSensitivity * 100),
                keyStep: 10),
                SettingRowIds.DynamicCrosshairSensitivity);
            _dynamicCrosshairTurnSpeed = Add(section, new SliderRow("Camera turn speed",
                (int)Math.Round(InputSettings.DynamicCrosshairTurnSpeed * 100),
                value => $"{value / 100f:0.00}x", min: 10,
                max: (int)(DynamicCrosshairTuning.MaximumTurnSpeed * 100),
                keyStep: 10),
                SettingRowIds.DynamicCrosshairTurnSpeed);
            _dynamicCrosshairTravel.ValueChanged += (_, _) =>
                _dynamicCrosshairTravelEdited = true;
            _dynamicCrosshairSensitivity.ValueChanged += (_, _) =>
                _dynamicCrosshairSensitivityEdited = true;
            _dynamicCrosshairTurnSpeed.ValueChanged += (_, _) =>
                _dynamicCrosshairTurnSpeedEdited = true;
            BuildControlsTabs(section);
            StackPanel page = _mouseKeyboardControlsPage;
            if (!IsAndroidSettingsPlatform)
            {
                Heading(page, "Aiming");
            }
            _sensitivity = Add(page, new SliderRow("Sensitivity",
                SensitivityToSlider(InputSettings.MouseSensitivity),
                v => $"{SliderToSensitivity(v).ToString("0.00", CultureInfo.InvariantCulture)}x",
                min: MouseSensitivitySliderMinimum,
                max: MouseSensitivitySliderMaximum,
                keyStep: 1), SettingRowIds.MouseSensitivity);
            _sensitivity.ValueChanged += (_, _) => _mouseSensitivityEdited = true;
            _invertY = Add(page, new ToggleRow("Invert vertical aim", InputSettings.InvertMouseY), SettingRowIds.MouseInvertY);
            _invertX = Add(page, new ToggleRow("Invert horizontal aim", InputSettings.InvertMouseX), SettingRowIds.MouseInvertX);
            Heading(page, "Weapons and chat");
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

            if (_touchControlsPage != null)
            {
                BuildTouchControls(_touchControlsPage);
            }
            if (IsAndroidSettingsPlatform)
            {
                _morphBallSwipeBoost = Add(_touchControlsPage ?? page, new ToggleRow(
                    "Morph Ball swipe boost", InputSettings.MorphBallSwipeBoost),
                    SettingRowIds.MorphBallSwipeBoost);
            }
            if (_stylusControlsPage != null)
            {
                BuildStylusControls(_stylusControlsPage);
            }

            // Its own section rather than more rows under "Mouse": a pad has
            // its own feel, and somebody who inverts one of the two very often
            // does not invert the other.
            page = _gamepadControlsPage;
            Heading(page, "Gamepad");
            // No "use a connected gamepad" toggle. A pad that is not being
            // held changes nothing on its own -- see GamepadInput.Active --
            // and on a phone the touch controls now step aside for a pad by
            // themselves and come back at the first touch, so the one thing
            // the toggle was ever asked to do is done without asking.
            Heading(page, "Basic");
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
            _gamepadZoomMultiplier = Add(page, new SliderRow("Zoom horizontal sensitivity",
                LookToSlider(InputSettings.GamepadZoomHorizontalMultiplier),
                v => $"{SliderToLook(v).ToString("0.00", CultureInfo.InvariantCulture)}x"), SettingRowIds.ControllerZoom);
            _gamepadZoomVerticalMultiplier = Add(page, new SliderRow(
                "Zoom vertical sensitivity",
                LookToSlider(InputSettings.GamepadZoomVerticalMultiplier),
                v => $"{SliderToLook(v).ToString("0.00", CultureInfo.InvariantCulture)}x"),
                SettingRowIds.ControllerZoomVertical);
            _gamepadStickAimMode = Add(page, new ChoiceRow("Right-stick aim mode",
                new[] { "Traditional", "Flick Stick" },
                (int)InputSettings.GamepadStickAimMode),
                SettingRowIds.ControllerStickAimMode);
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
                _dirtyTracker?.Refresh();
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
                _dirtyTracker?.Refresh();
            };
            Add(page, resetGeneral, SettingRowIds.ControllerHorizontalSensitivity + ".reset-general");

            StackPanel sectionPage = page;
            var advancedPage = new StackPanel { Spacing = 10 };
            _advancedControllerExpander = new Expander
            {
                Header = "Advanced controller tuning",
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
                _dirtyTracker?.Refresh();
            };
            Add(page, resetGyro, SettingRowIds.ControllerGyro + ".reset");
            Heading(page, "Feedback and diagnostics");
            Heading(page, "Controller diagnostics");
            _controllerDiagnosticsSummary = Explain(page,
                "Reading the active input owner…", GuiTheme.Warm);
            _controllerDiagnosticsDetails = new Note("");
            var controllerDiagnosticsDetails = new Expander
            {
                Header = "Raw, effective, and configured controller state",
                Content = _controllerDiagnosticsDetails,
                IsExpanded = true,
                Margin = new Thickness(0, 0, 0, 2)
            };
            PrimeAccessibility.SetName(controllerDiagnosticsDetails,
                "Raw, effective, and configured controller state");
            Add(page, controllerDiagnosticsDetails);
            _controllerDiagnosticsExport = new MenuEntry(
                "Export controller diagnostics",
                "Write privacy-safe JSON and text reports to local logs",
                titleSize: 13)
            {
                Height = 48,
                Accent = GuiTheme.Accent,
                Margin = new Thickness(0, 4, 0, 0)
            };
            _controllerDiagnosticsExport.Click += (_, _) => ExportControllerDiagnostics();
            Add(page, _controllerDiagnosticsExport);
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
            _gamepadAutoCalibration = Add(page, new ToggleRow(
                "Automatic stick calibration",
                InputSettings.GamepadAutoCalibrationEnabled),
                SettingRowIds.ControllerAutoCalibration);
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
                IDisposable? suppression = SuppressDirtyTracking();
                try
                {
                    _gamepadLook.Value = ExponentToSlider(InputSettings.GamepadLookExponent);
                }
                finally
                {
                    suppression?.Dispose();
                }
            };
            _gamepadTurnAcceleration.Changed += (_, _) =>
            {
                if (_refreshingControllerRows) return;
                InputSettings.GamepadTurnAcceleration =
                    (Mods.Input.GamepadTurnAccelerationPreset)
                    _gamepadTurnAcceleration.Index;
                IDisposable? suppression = SuppressDirtyTracking();
                try
                {
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
                }
                finally
                {
                    suppression?.Dispose();
                }
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
                _dirtyTracker?.Refresh();
            };
            Add(page, resetAdvanced, SettingRowIds.ControllerOuterBoost + ".reset");

            foreach (SliderRow row in new[] { _gamepadHorizontalSensitivity,
                _gamepadVerticalSensitivity, _gamepadDeadZone,
                _gamepadLookDeadZone, _gamepadOuterDeadZone, _gamepadMoveActivate,
                _gamepadMoveRelease, _gamepadLook, _gamepadYawRate, _gamepadPitchRate,
                _gamepadOuterBoostStart, _gamepadOuterYawBoost, _gamepadOuterPitchBoost,
                _gamepadBoostDelay, _gamepadBoostRamp, _gamepadTriggerPress,
                _gamepadTriggerRelease, _gamepadZoomMultiplier,
                _gamepadZoomVerticalMultiplier,
                _gamepadHapticsStrength })
            {
                row.ValueChanged += (_, _) => MarkControllerSliderEdited(row);
            }
            foreach (ToggleRow row in new[] { _gamepadInvertY,
                _gamepadGyroInvertX, _gamepadGyroInvertY, _gamepadHaptics,
                _gamepadAutoCalibration,
                _inputBalanceTelemetry })
            {
                row.Changed += (_, _) => MarkControllerCustom();
            }
            foreach (ChoiceRow row in new[] { _gamepadGyro,
                _gamepadGyroActivation, _gamepadResponseCurve,
                _gamepadTurnAcceleration, _gamepadStickAimMode })
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
                _dirtyTracker?.Refresh();
            };
            Add(page, resetController, SettingRowIds.ControllerPreset + ".reset-all");

            page = _mouseKeyboardControlsPage;
            Heading(page, "Keyboard bindings");
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
                _dirtyTracker?.Refresh();
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
                _dirtyTracker?.Refresh();
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
            if (!IsAndroidSettingsPlatform)
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

        private static float[] BottomScreenAffinityValues()
        {
            NativeBottomScreenAffinityLayoutOptions layout
                = InputSettings.CurrentBottomScreenAffinityLayout;
            return new[]
            {
                layout.VoltDriverX, layout.VoltDriverY,
                layout.BattlehammerX, layout.BattlehammerY,
                layout.ImperialistX, layout.ImperialistY,
                layout.JudicatorX, layout.JudicatorY,
                layout.MagmaulX, layout.MagmaulY,
                layout.ShockCoilX, layout.ShockCoilY,
                layout.PowerBeamX, layout.PowerBeamY,
                layout.MissileX, layout.MissileY,
                layout.AltFormX, layout.AltFormY
            };
        }

        private void BuildStylusControls(StackPanel page)
        {
            Heading(page, "Stylus");
            Explain(page, "A compatible pen takes over aiming automatically while it touches "
                + "the play area. Classic gestures mirror the Nintendo DS controls: double tap "
                + "to jump and flick while in Morph Ball to boost.");
            _bottomScreenMode = Add(page, new ChoiceRow("DS bottom screen",
                new[] { "Off", "Popup", "Always visible" },
                (int)InputSettings.BottomScreenMode), SettingRowIds.BottomScreenMode);
            _bottomScreenActivation = Add(page, new ChoiceRow("Touch screen bind",
                new[] { "Toggle", "Hold" },
                (int)InputSettings.BottomScreenActivation),
                SettingRowIds.BottomScreenActivation);
            _bottomScreenCursorSensitivity = Add(page, new SliderRow(
                "Touch cursor sensitivity",
                (int)Math.Round(InputSettings.BottomScreenCursorSensitivity * 100),
                v => $"{v / 100f:0.00}x", min: 10, max: 400, keyStep: 5),
                SettingRowIds.BottomScreenCursorSensitivity);
            _bottomScreenCursorStartX = Add(page, new SliderRow(
                "Touch cursor start horizontal",
                (int)Math.Round(InputSettings.BottomScreenCursorStartX * 100),
                v => $"{v}%"), SettingRowIds.BottomScreenCursorStartX);
            _bottomScreenCursorStartY = Add(page, new SliderRow(
                "Touch cursor start vertical",
                (int)Math.Round(InputSettings.BottomScreenCursorStartY * 100),
                v => $"{v}%"), SettingRowIds.BottomScreenCursorStartY);
            _bottomScreenStyle = Add(page, new ChoiceRow("Screen layout",
                new[] { "Classic DS", "Affinity selector" },
                (int)InputSettings.BottomScreenStyle), SettingRowIds.BottomScreenStyle);
            _bottomScreenDirectionalSwipeAssist = Add(page, new ToggleRow(
                "Directional swipe assist",
                InputSettings.BottomScreenDirectionalSwipeAssist),
                SettingRowIds.BottomScreenDirectionalSwipeAssist);
            _bottomScreenScale = Add(page, new SliderRow("Screen size",
                (int)Math.Round(InputSettings.BottomScreenScale * 100),
                v => $"{v}%", min: 40, max: 100), SettingRowIds.BottomScreenScale);
            _bottomScreenCenterX = Add(page, new SliderRow("Popup position X",
                (int)Math.Round(InputSettings.BottomScreenCenterX * 100),
                v => $"{v}%"), SettingRowIds.BottomScreenCenterX);
            _bottomScreenCenterY = Add(page, new SliderRow("Popup position Y",
                (int)Math.Round(InputSettings.BottomScreenCenterY * 100),
                v => $"{v}%"), SettingRowIds.BottomScreenCenterY);
            _bottomScreenPowerBeamX = Add(page, new SliderRow(
                "Power Beam position X",
                (int)Math.Round(InputSettings.BottomScreenPowerBeamX * 100),
                v => $"{v}%"), SettingRowIds.BottomScreenPowerBeamX);
            _bottomScreenPowerBeamY = Add(page, new SliderRow(
                "Power Beam position Y",
                (int)Math.Round(InputSettings.BottomScreenPowerBeamY * 100),
                v => $"{v}%"), SettingRowIds.BottomScreenPowerBeamY);
            _bottomScreenMissileX = Add(page, new SliderRow(
                "Missile position X",
                (int)Math.Round(InputSettings.BottomScreenMissileX * 100),
                v => $"{v}%"), SettingRowIds.BottomScreenMissileX);
            _bottomScreenMissileY = Add(page, new SliderRow(
                "Missile position Y",
                (int)Math.Round(InputSettings.BottomScreenMissileY * 100),
                v => $"{v}%"), SettingRowIds.BottomScreenMissileY);
            _bottomScreenNextWeaponX = Add(page, new SliderRow(
                "Next weapon position X",
                (int)Math.Round(InputSettings.BottomScreenNextWeaponX * 100),
                v => $"{v}%"), SettingRowIds.BottomScreenNextWeaponX);
            _bottomScreenNextWeaponY = Add(page, new SliderRow(
                "Next weapon position Y",
                (int)Math.Round(InputSettings.BottomScreenNextWeaponY * 100),
                v => $"{v}%"), SettingRowIds.BottomScreenNextWeaponY);
            _bottomScreenWeaponSelectX = Add(page, new SliderRow(
                "Weapon selector position X",
                (int)Math.Round(InputSettings.BottomScreenWeaponSelectX * 100),
                v => $"{v}%"), SettingRowIds.BottomScreenWeaponSelectX);
            _bottomScreenWeaponSelectY = Add(page, new SliderRow(
                "Weapon selector position Y",
                (int)Math.Round(InputSettings.BottomScreenWeaponSelectY * 100),
                v => $"{v}%"), SettingRowIds.BottomScreenWeaponSelectY);
            _bottomScreenAltFormX = Add(page, new SliderRow(
                "Alt form position X",
                (int)Math.Round(InputSettings.BottomScreenAltFormX * 100),
                v => $"{v}%"), SettingRowIds.BottomScreenAltFormX);
            _bottomScreenAltFormY = Add(page, new SliderRow(
                "Alt form position Y",
                (int)Math.Round(InputSettings.BottomScreenAltFormY * 100),
                v => $"{v}%"), SettingRowIds.BottomScreenAltFormY);
            var affinityPlacement = new StackPanel { Spacing = 2 };
            Add(page, new Expander
            {
                Header = "Affinity button placement",
                Content = affinityPlacement,
                IsExpanded = false,
                Margin = new Thickness(0, 4, 0, 2)
            });
            Explain(affinityPlacement,
                "Move the six affinity icons and the Beam, Missile, and Alt form controls. "
                + "Overlapping controls select the nearest center.");
            SliderRow AffinitySlider(string label, float value, string rowId)
                => Add(affinityPlacement, new SliderRow(label,
                    (int)Math.Round(value * 100), v => $"{v}%"), rowId);
            _bottomScreenAffinityRows = new[]
            {
                AffinitySlider("Volt Driver position X", InputSettings.BottomScreenAffinityVoltDriverX,
                    SettingRowIds.BottomScreenAffinityVoltDriverX),
                AffinitySlider("Volt Driver position Y", InputSettings.BottomScreenAffinityVoltDriverY,
                    SettingRowIds.BottomScreenAffinityVoltDriverY),
                AffinitySlider("Battlehammer position X", InputSettings.BottomScreenAffinityBattlehammerX,
                    SettingRowIds.BottomScreenAffinityBattlehammerX),
                AffinitySlider("Battlehammer position Y", InputSettings.BottomScreenAffinityBattlehammerY,
                    SettingRowIds.BottomScreenAffinityBattlehammerY),
                AffinitySlider("Imperialist position X", InputSettings.BottomScreenAffinityImperialistX,
                    SettingRowIds.BottomScreenAffinityImperialistX),
                AffinitySlider("Imperialist position Y", InputSettings.BottomScreenAffinityImperialistY,
                    SettingRowIds.BottomScreenAffinityImperialistY),
                AffinitySlider("Judicator position X", InputSettings.BottomScreenAffinityJudicatorX,
                    SettingRowIds.BottomScreenAffinityJudicatorX),
                AffinitySlider("Judicator position Y", InputSettings.BottomScreenAffinityJudicatorY,
                    SettingRowIds.BottomScreenAffinityJudicatorY),
                AffinitySlider("Magmaul position X", InputSettings.BottomScreenAffinityMagmaulX,
                    SettingRowIds.BottomScreenAffinityMagmaulX),
                AffinitySlider("Magmaul position Y", InputSettings.BottomScreenAffinityMagmaulY,
                    SettingRowIds.BottomScreenAffinityMagmaulY),
                AffinitySlider("Shock Coil position X", InputSettings.BottomScreenAffinityShockCoilX,
                    SettingRowIds.BottomScreenAffinityShockCoilX),
                AffinitySlider("Shock Coil position Y", InputSettings.BottomScreenAffinityShockCoilY,
                    SettingRowIds.BottomScreenAffinityShockCoilY),
                AffinitySlider("Power Beam position X", InputSettings.BottomScreenAffinityPowerBeamX,
                    SettingRowIds.BottomScreenAffinityPowerBeamX),
                AffinitySlider("Power Beam position Y", InputSettings.BottomScreenAffinityPowerBeamY,
                    SettingRowIds.BottomScreenAffinityPowerBeamY),
                AffinitySlider("Missile position X", InputSettings.BottomScreenAffinityMissileX,
                    SettingRowIds.BottomScreenAffinityMissileX),
                AffinitySlider("Missile position Y", InputSettings.BottomScreenAffinityMissileY,
                    SettingRowIds.BottomScreenAffinityMissileY),
                AffinitySlider("Alt form position X", InputSettings.BottomScreenAffinityAltFormX,
                    SettingRowIds.BottomScreenAffinityAltFormX),
                AffinitySlider("Alt form position Y", InputSettings.BottomScreenAffinityAltFormY,
                    SettingRowIds.BottomScreenAffinityAltFormY)
            };
            _bottomScreenOpacity = Add(page, new SliderRow("Screen opacity",
                (int)Math.Round(InputSettings.BottomScreenOpacity * 100),
                v => $"{v}%", min: 5, max: 100), SettingRowIds.BottomScreenOpacity);
            _bottomScreenLabels = Add(page, new ToggleRow("Show button labels",
                InputSettings.BottomScreenLabels), SettingRowIds.BottomScreenLabels);
            Explain(page, "Classic DS restores the original button arrangement. The "
                + "customizable Touch screen binding starts a cursor at the configured "
                + "normalized position; sensitivity and movement stay inside the panel. "
                + "Toggle uses left click and closes after a selection. Hold drags a "
                + "synthetic contact until the binding is released. Tap or drag through "
                + "SEL to open the nine-target affinity selector.");
            _stylusAiming = Add(page, new ToggleRow("Stylus aiming",
                InputSettings.StylusAimingEnabled), SettingRowIds.StylusAiming);
            _stylusSensitivity = Add(page, new SliderRow("Sensitivity",
                LookToSlider(InputSettings.StylusSensitivity),
                v => $"{SliderToLook(v).ToString("0.00", CultureInfo.InvariantCulture)}x"), SettingRowIds.StylusSensitivity);
            _stylusSensitivity.ValueChanged += (_, _) => _stylusSensitivityEdited = true;
            Add(page, new StylusTurnPreview(
                () => SliderToLook(_stylusSensitivity.Value)));
            _stylusInvertY = Add(page, new ToggleRow("Invert vertical aim",
                InputSettings.StylusInvertY), SettingRowIds.StylusInvertY);
            string[] actions = Enum.GetNames<Mods.Input.StylusAction>();
            _stylusPrimary = Add(page, new ChoiceRow("Primary button", actions,
                (int)InputSettings.StylusPrimaryAction), SettingRowIds.StylusPrimary);
            _stylusSecondary = Add(page, new ChoiceRow("Secondary button", actions,
                (int)InputSettings.StylusSecondaryAction), SettingRowIds.StylusSecondary);
            _stylusClassicGestures = Add(page, new ToggleRow("DS-style gestures",
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
            float normalized = InputSettings.NormalizeMouseSensitivity(sensitivity);
            return Math.Clamp(
                (int)Math.Round(normalized / InputSettings.MouseSensitivityStep),
                MouseSensitivitySliderMinimum, MouseSensitivitySliderMaximum);
        }

        private static float SliderToSensitivity(int value)
        {
            int clamped = Math.Clamp(value,
                MouseSensitivitySliderMinimum, MouseSensitivitySliderMaximum);
            return InputSettings.NormalizeMouseSensitivity(
                clamped * InputSettings.MouseSensitivityStep);
        }

        private static int MouseSensitivitySliderMinimum
            => (int)Math.Round(InputSettings.MinimumMouseSensitivity
                / InputSettings.MouseSensitivityStep);

        private static int MouseSensitivitySliderMaximum
            => (int)Math.Round(InputSettings.UiMaximumMouseSensitivity
                / InputSettings.MouseSensitivityStep);

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

        private static int ReticleScaleToSlider(float scale)
            => (int)Math.Round(Math.Clamp(scale, Features.MinimumReticleScale,
                Features.MaximumReticleScale) * 100);

        private static float SliderToReticleScale(int value)
            => Math.Clamp(value / 100f, Features.MinimumReticleScale,
                Features.MaximumReticleScale);

        // ---------------------------------------------------------- match rules

        private void BuildMatch()
        {
            StackPanel page = AddSection("Player");
            Heading(page, "Profile");
            BuildProfile(page);
        }

        private void BuildProfile(StackPanel page)
        {
            bool authenticated = _identity.IsAuthenticated;
            var identityContent = new StackPanel { Spacing = 3 };
            identityContent.Children.Add(new TextBlock
            {
                Text = authenticated ? "ACCOUNT" : "GUEST",
                Classes = { "prime-kicker" }
            });
            identityContent.Children.Add(new TextBlock
            {
                Text = _identity.DisplayName,
                Classes = { "prime-heading" }
            });
            identityContent.Children.Add(new TextBlock
            {
                Text = authenticated ? "Signed in" : "Saved on this device",
                Classes = { "prime-muted" }
            });
            var identityHeader = PrimeControlFactory.CompactPanel(identityContent);
            PrimeAccessibility.SetName(identityHeader,
                $"{(authenticated ? "Account" : "Guest")}: {_identity.DisplayName}");
            PrimeAccessibility.SetDescription(identityHeader, authenticated
                ? "Your account identity and preferred Hunter."
                : "Your guest identity is saved only on this device.");
            page.Children.Add(identityHeader);
            _playerName = Add(page, new FieldRow(
                "Display name",
                authenticated || _identity.IsGuest
                    ? _identity.DisplayName : LauncherPrefs.PlayerName,
                boxWidth: 200), SettingRowIds.PlayerName);
            if (authenticated)
            {
                _playerName.Box.IsReadOnly = true;
                _playerName.Box.IsEnabled = false;
                _hunterProfileAction = new MenuEntry("Hunter profile",
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
                Explain(page, "Your guest name is saved only on this device.");
            }
            string[] hunters = PlayableHunterCatalog.All
                .Select(hunter => hunter.ToString())
                .Append(Hunter.Random.ToString()).ToArray();
            _hunterRow = Add(page, new ChoiceRow("Preferred Hunter", hunters,
                Math.Max(0, Array.IndexOf(hunters, LauncherPrefs.LastHunter.ToString()))),
                SettingRowIds.Hunter);
            Explain(page, "Match rules are controlled by the lobby host.");

            Heading(page, "Online");
            _showOnlinePresence = Add(page, new ToggleRow("Show me in Online Players",
                LauncherPrefs.ShowOnlinePresence), SettingRowIds.ShowOnlinePresence);
            Explain(page, "Allow other players to see your display name and activity.");
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
            string initialUpdateStatus = DescribeUpdateStatus();
            _updateStatus = Explain(page, initialUpdateStatus,
                Update.Updater.Configured ? null : GuiTheme.Warm);
            SetUpdateStatusAccessibility(initialUpdateStatus,
                Update.Updater.Configured ? PrimeStatusKind.Info
                    : PrimeStatusKind.Warning);
            Explain(page, Update.Updater.Configured
                ? "Automatic downloads and stages signed updates. Notify only keeps installation manual. Off skips automatic checks."
                : "Updates are unavailable in this build because no signed update repository is configured.");

            Heading(page, "Game files");
            var files = new MenuEntry("Metroid Prime Hunters",
                GameFiles.Ready ? "Ready" : GameFiles.Describe(), titleSize: 15);
            files.SubtitleColor = GameFiles.Ready ? GuiTheme.Good : GuiTheme.Warm;
            files.Click += (_, _) =>
            {
                GameFilesRequested?.Invoke(this, EventArgs.Empty);
                Close();
            };
            Add(page, files, SettingRowIds.GameFiles);

            Heading(page, "Diagnostics");
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
                    SetUpdateStatusAccessibility(DescribeUpdateStatus(),
                        PrimeStatusKind.Info);
                }
            });
        }

        private void SetUpdateStatusAccessibility(string status,
            PrimeStatusKind kind)
        {
            string value = String.IsNullOrWhiteSpace(status)
                ? "Update status unavailable."
                : status;
            _updateStatus.Text = value;
            PrimeAccessibility.SetStatus(_updateStatus, value, kind);
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

        private void ShareLogs() => _ = ObserveUiTask(ShareLogsAsync(), "share logs");

        private async Task ShareLogsAsync()
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

        private static async Task ObserveUiTask(Task operation, string description)
        {
            try
            {
                await operation;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[settings] {description} failed: {ex.Message}");
            }
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
            Explain(page, "Automatic selects the best available region.");
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
            Heading(page, "Cosmetics");
            CosmeticPresentationSettings cosmetics = CosmeticPresentationPreferences.Parse(
                _settings, OperatingSystem.IsAndroid()
                    || RenderOptions.GraphicsPreset == GraphicsPreset.Performance);
            _cosmeticQuality = Add(page, new ChoiceRow("Cosmetic effects",
                Enum.GetNames<CosmeticQuality>(), (int)cosmetics.Quality),
                SettingRowIds.CosmeticQuality);
            Explain(page, "Off hides optional armor effects. Reduced lowers particles, ribbons, and distortion.");
            _showOtherPlayerCosmetics = Add(page,
                new ToggleRow("Show other player cosmetics",
                    cosmetics.ShowOtherPlayerCosmetics),
                SettingRowIds.ShowOtherPlayerCosmetics);
            _reduceCosmeticFlashes = Add(page,
                new ToggleRow("Reduce cosmetic flashes", cosmetics.ReduceCosmeticFlashes),
                SettingRowIds.ReduceCosmeticFlashes);
            _forceStrongTeamColors = Add(page,
                new ToggleRow("Use strong team colors", cosmetics.ForceStrongTeamColors),
                SettingRowIds.ForceStrongTeamColors);
            _disableCosmeticDistortion = Add(page,
                new ToggleRow("Disable cosmetic distortion",
                    cosmetics.DisableCosmeticDistortion),
                SettingRowIds.DisableCosmeticDistortion);
            _disableCosmeticParticles = Add(page,
                new ToggleRow("Disable cosmetic particles",
                    cosmetics.DisableCosmeticParticles),
                SettingRowIds.DisableCosmeticParticles);
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
            _settings.FieldOfView = Math.Clamp(_fieldOfView.Value,
                RenderOptions.MinFieldOfView, RenderOptions.MaxFieldOfView)
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
            _settings.CosmeticQuality = ((CosmeticQuality)_cosmeticQuality.Index).ToString();
            _settings.ShowOtherPlayerCosmetics = RenderOptions.OnOff(_showOtherPlayerCosmetics.On);
            _settings.ReduceCosmeticFlashes = RenderOptions.OnOff(_reduceCosmeticFlashes.On);
            _settings.ForceStrongTeamColors = RenderOptions.OnOff(_forceStrongTeamColors.On);
            _settings.DisableCosmeticDistortion = RenderOptions.OnOff(_disableCosmeticDistortion.On);
            _settings.DisableCosmeticParticles = RenderOptions.OnOff(_disableCosmeticParticles.On);
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
            _settings.RadarRange = _radarRangeRow.Value.ToString(CultureInfo.InvariantCulture);
            _settings.RadarOpacity = (_radarOpacityRow.Value / 100f)
                .ToString("0.00", CultureInfo.InvariantCulture);
            _settings.RadarElevationIndicators = RenderOptions.OnOff(_radarElevationRow.On);
            global::MphRead.Hud.Radar.RadarProfile radarProfile = BuildRadarProfileFromRows();
            global::MphRead.Hud.Radar.RadarSettings.Apply(radarProfile);
            _settings.RadarProfileJson
                = global::MphRead.Hud.Radar.RadarProfileSerializer.Export(radarProfile);
            _settings.RadarModeProfilesJson
                = global::MphRead.Hud.Radar.RadarProfileSerializer.ExportModes(
                    global::MphRead.Hud.Radar.RadarSettings.ModeProfiles);
            _settings.RadarDeviceProfilesJson
                = global::MphRead.Hud.Radar.RadarProfileSerializer.ExportModes(
                    global::MphRead.Hud.Radar.RadarSettings.DeviceProfiles.ToDictionary(
                        pair => pair.Key.ToString(), pair => pair.Value));
            int cap = _fpsLimitStops[Math.Clamp(_fpsLimitRow.Value, 0,
                _fpsLimitStops.Length - 1)].Cap;
            FrameTiming.FrameRateCap = cap;
            _settings.FrameRateCap = FrameTiming.CapString(cap);
            VisualStyle visualStyle = (VisualStyle)_visualStyleRow.Index;
            _settings.VisualStyle = RenderOptions.FormatVisualStyle(visualStyle);
            _settings.TexturePack = _texturePacks[Math.Clamp(_texturePackRow.Index,
                0, _texturePacks.Count - 1)].Id;
            // Keep the legacy switch synchronized for older builds.
            _settings.CelShading = RenderOptions.OnOff(visualStyle == VisualStyle.Cel);
            _settings.CelBands = "8";
            _settings.CelEdge = "50";
            Features.ProHud = _proHud.On;
            Features.ProHudFixedWeapon = _proHudWeaponRow.Index == 0;
            Features.ReticleOpacity = SliderToOpacity(_reticleOpacity.Value);
            Features.ReticleScale = SliderToReticleScale(_reticleScale.Value);
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
            if (_dynamicCrosshairTravelEdited)
            {
                InputSettings.DynamicCrosshairTravelDegrees = _dynamicCrosshairTravel.Value;
            }
            if (_dynamicCrosshairSensitivityEdited)
            {
                InputSettings.DynamicCrosshairSensitivity
                    = _dynamicCrosshairSensitivity.Value / 100f;
            }
            if (_dynamicCrosshairTurnSpeedEdited)
            {
                InputSettings.DynamicCrosshairTurnSpeed = _dynamicCrosshairTurnSpeed.Value / 100f;
            }
            if (_mouseSensitivityEdited)
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
            InputSettings.GamepadAutoCalibrationEnabled = _gamepadAutoCalibration.On;
            InputSettings.GamepadStickAimMode =
                (Mods.Input.GamepadStickAimMode)_gamepadStickAimMode.Index;
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
                if (_stylusSensitivityEdited)
                    InputSettings.StylusSensitivity = SliderToLook(_stylusSensitivity!.Value);
                InputSettings.StylusInvertY = _stylusInvertY!.On;
                InputSettings.StylusPrimaryAction = (Mods.Input.StylusAction)_stylusPrimary!.Index;
                InputSettings.StylusSecondaryAction = (Mods.Input.StylusAction)_stylusSecondary!.Index;
                InputSettings.StylusClassicGestures = _stylusClassicGestures!.On;
                InputSettings.StylusDoubleTapJump = _stylusDoubleTapJump!.On;
                InputSettings.StylusFlickBoost = _stylusFlickBoost!.On;
                InputSettings.StylusPressureToFire = _stylusPressureToFire!.On;
                InputSettings.StylusPressureThreshold = _stylusPressureThreshold!.Value / 100f;
                InputSettings.BottomScreenMode = (Mods.Input.NativeBottomScreenMode)
                    _bottomScreenMode!.Index;
                InputSettings.BottomScreenActivation
                    = (Mods.Input.NativeBottomScreenActivationMode)
                        _bottomScreenActivation!.Index;
                InputSettings.BottomScreenCursorSensitivity
                    = _bottomScreenCursorSensitivity!.Value / 100f;
                InputSettings.BottomScreenCursorStartX
                    = _bottomScreenCursorStartX!.Value / 100f;
                InputSettings.BottomScreenCursorStartY
                    = _bottomScreenCursorStartY!.Value / 100f;
                InputSettings.BottomScreenDirectionalSwipeAssist
                    = _bottomScreenDirectionalSwipeAssist!.On;
                InputSettings.BottomScreenPowerBeamX
                    = _bottomScreenPowerBeamX!.Value / 100f;
                InputSettings.BottomScreenPowerBeamY
                    = _bottomScreenPowerBeamY!.Value / 100f;
                InputSettings.BottomScreenMissileX
                    = _bottomScreenMissileX!.Value / 100f;
                InputSettings.BottomScreenMissileY
                    = _bottomScreenMissileY!.Value / 100f;
                InputSettings.BottomScreenNextWeaponX
                    = _bottomScreenNextWeaponX!.Value / 100f;
                InputSettings.BottomScreenNextWeaponY
                    = _bottomScreenNextWeaponY!.Value / 100f;
                InputSettings.BottomScreenWeaponSelectX
                    = _bottomScreenWeaponSelectX!.Value / 100f;
                InputSettings.BottomScreenWeaponSelectY
                    = _bottomScreenWeaponSelectY!.Value / 100f;
                InputSettings.BottomScreenAltFormX
                    = _bottomScreenAltFormX!.Value / 100f;
                InputSettings.BottomScreenAltFormY
                    = _bottomScreenAltFormY!.Value / 100f;
                if (_bottomScreenAffinityRows.Length == 18)
                {
                    InputSettings.BottomScreenAffinityVoltDriverX
                        = _bottomScreenAffinityRows[0].Value / 100f;
                    InputSettings.BottomScreenAffinityVoltDriverY
                        = _bottomScreenAffinityRows[1].Value / 100f;
                    InputSettings.BottomScreenAffinityBattlehammerX
                        = _bottomScreenAffinityRows[2].Value / 100f;
                    InputSettings.BottomScreenAffinityBattlehammerY
                        = _bottomScreenAffinityRows[3].Value / 100f;
                    InputSettings.BottomScreenAffinityImperialistX
                        = _bottomScreenAffinityRows[4].Value / 100f;
                    InputSettings.BottomScreenAffinityImperialistY
                        = _bottomScreenAffinityRows[5].Value / 100f;
                    InputSettings.BottomScreenAffinityJudicatorX
                        = _bottomScreenAffinityRows[6].Value / 100f;
                    InputSettings.BottomScreenAffinityJudicatorY
                        = _bottomScreenAffinityRows[7].Value / 100f;
                    InputSettings.BottomScreenAffinityMagmaulX
                        = _bottomScreenAffinityRows[8].Value / 100f;
                    InputSettings.BottomScreenAffinityMagmaulY
                        = _bottomScreenAffinityRows[9].Value / 100f;
                    InputSettings.BottomScreenAffinityShockCoilX
                        = _bottomScreenAffinityRows[10].Value / 100f;
                    InputSettings.BottomScreenAffinityShockCoilY
                        = _bottomScreenAffinityRows[11].Value / 100f;
                    InputSettings.BottomScreenAffinityPowerBeamX
                        = _bottomScreenAffinityRows[12].Value / 100f;
                    InputSettings.BottomScreenAffinityPowerBeamY
                        = _bottomScreenAffinityRows[13].Value / 100f;
                    InputSettings.BottomScreenAffinityMissileX
                        = _bottomScreenAffinityRows[14].Value / 100f;
                    InputSettings.BottomScreenAffinityMissileY
                        = _bottomScreenAffinityRows[15].Value / 100f;
                    InputSettings.BottomScreenAffinityAltFormX
                        = _bottomScreenAffinityRows[16].Value / 100f;
                    InputSettings.BottomScreenAffinityAltFormY
                        = _bottomScreenAffinityRows[17].Value / 100f;
                }
                InputSettings.BottomScreenStyle = (Mods.Input.NativeBottomScreenStyle)
                    _bottomScreenStyle!.Index;
                InputSettings.BottomScreenScale = _bottomScreenScale!.Value / 100f;
                InputSettings.BottomScreenCenterX = _bottomScreenCenterX!.Value / 100f;
                InputSettings.BottomScreenCenterY = _bottomScreenCenterY!.Value / 100f;
                InputSettings.BottomScreenOpacity = _bottomScreenOpacity!.Value / 100f;
                InputSettings.BottomScreenLabels = _bottomScreenLabels!.On;
            }
            InputSettings.Save();
            // The players in the match already have their own copies of these.
            if (_scene != null)
                InputSettings.ApplyToPlayers(_scene.Players.Select(
                    player => player.GetPresentation().Bindings));
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
            LauncherPrefs.ShowOnlinePresence = _showOnlinePresence.On;
            ApplyDebugLogging();
            ClientSettings.CommitSettings(_settings);
            if (!LauncherPrefs.Save(notifyPresenceChange: true))
            {
                // Keep the draft open and dirty when the launcher preference
                // file could not be written. In particular, do not let the
                // action bar report a saved presence value that never reached
                // disk or publish the live-update seam.
                throw new InvalidOperationException(
                    "Launcher preferences could not be saved.");
            }
            ClientPresentationContent.Refresh();
            // Written and *applied*: the volumes, the language and the match
            // rules were only ever put in the file, so a music slider moved
            // here would otherwise leave the music exactly where it was --
            // during a match as well as before one, since this same window
            // opens from the pause menu.
            Mods.GameSettings.Apply(_settings);
            _dirtyTracker?.MarkSaved();
            Saved = true;
            SaveSucceeded?.Invoke(this, EventArgs.Empty);
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
                InputSettings.GamepadZoomHorizontalMultiplier
                    = SliderToLook(_gamepadZoomMultiplier.Value);
            }
            if (ControllerSliderWasEdited(_gamepadZoomVerticalMultiplier))
            {
                InputSettings.GamepadZoomVerticalMultiplier
                    = SliderToLook(_gamepadZoomVerticalMultiplier.Value);
            }
        }
    }
}
