using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectPrime.Server.Shared;
using MphRead.Identity;
using MphRead.Cosmetics;
using MphRead.Mods;
using MphRead.Mods.Accounts;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Theme;
using MphRead.Mods.Network;
using MphRead.Mods.Update;
using AvaloniaButton = Avalonia.Controls.Button;
using Scene = MphRead.Scene;

namespace MphRead.Mods.Launcher.Gui;

internal enum GatewayForm
{
    Landing,
    SignIn,
    Register,
    Confirm
}

internal readonly record struct CosmeticSyncAttempt(
    Guid LobbyId,
    long Revision,
    CosmeticLoadoutIds Ids);

/// <summary>Bounds automatic retries for one exact authoritative lobby state.
/// A changed revision or selection is a new operation and may start at once.</summary>
internal sealed class CosmeticSyncRetryPolicy
{
    internal const int MaximumAttempts = 3;
    private CosmeticSyncAttempt? _failedAttempt;
    private int _attempts;
    private bool _retryArmed;

    public bool CanStart(CosmeticSyncAttempt attempt)
    {
        if (_failedAttempt != attempt)
        {
            Reset();
            return true;
        }
        if (!_retryArmed || _attempts >= MaximumAttempts) return false;
        _retryArmed = false;
        return true;
    }

    public TimeSpan? RecordFailure(CosmeticSyncAttempt attempt)
    {
        if (_failedAttempt != attempt)
        {
            _failedAttempt = attempt;
            _attempts = 0;
        }
        _attempts++;
        _retryArmed = false;
        if (_attempts >= MaximumAttempts) return null;
        return TimeSpan.FromMilliseconds(250 * (1 << (_attempts - 1)));
    }

    public bool ArmRetry(CosmeticSyncAttempt attempt)
    {
        if (_failedAttempt != attempt || _attempts >= MaximumAttempts) return false;
        _retryArmed = true;
        return true;
    }

    public void Reset()
    {
        _failedAttempt = null;
        _attempts = 0;
        _retryArmed = false;
    }
}

/// <summary>
/// The production Project Prime shell. It owns one native Avalonia tree on
/// desktop and Android; account, Node, career, ranking, replay, and settings
/// services remain behind their existing boundaries.
/// </summary>
internal sealed partial class PrimeShellView : UserControl, IAsyncDisposable
{
    private MenuSettings _settings;
    private readonly List<string> _rooms;
    private readonly bool _restoreOnActivate;
    private readonly bool _ignoreGameFileGate;
    private readonly bool _captureMode;
    private readonly PrimeShellState _shell = new();
    private readonly GatewayController _gateway;
    private readonly Func<CancellationToken, Task<bool>> _restoreSession;
    private readonly bool _restoreUsesGateway;
    private readonly Func<TimeSpan, CancellationToken, Task> _startupDelay;
    private readonly PlayController _play;
    private readonly ClientOnlineRuntime _online;
    private readonly bool _ownsOnline;
    private readonly PlayPresentationState _playPresentation = new();
    private readonly HunterLicenseController _license;
    private readonly HunterAppearanceController _appearance
        = new(CosmeticCatalog.BuiltIn);
    private readonly ArmoryController _armory = new();
    private readonly RankingsController _rankings;
    private readonly TheatreController _theatre = new();
    private readonly PreviewImageService _previewImages = new();
    private readonly MapPreviewService _mapPreviews;
    private readonly HunterPreviewService _hunterPreviews;
    private readonly WeaponPreviewService _weaponPreviews;
    private readonly DispatcherTimer _inputTimer;
    private readonly List<(AvaloniaButton Button, PrimeRoute Route)> _navigationButtons = new();
    private readonly List<(AvaloniaButton Button, PrimeRoute Route)> _mobileNavigationButtons = new();
    private readonly PrimeRouteViewState _routeViewState = new();
    private readonly PrimeInputNavigator _inputNavigator = new(focusMemoryCapacity: 128);
    private readonly PrimeSeatOfferModalState _seatOfferModal = new();
    private readonly CancellationTokenSource _titleLifetime = new();
    private readonly PrimeTitleScreenLifecycle _titleLifecycle;
    private readonly PrimeTitleInputState _titleInput = new();
    private readonly PrimeTitleScreenCaptureState? _titleCaptureState;
    private Task? _titleDismissalTask;
    private PrimeShellNavigationAdapter _navigationAdapter = null!;
    private PrimeMotionLease? _routeMotion;
    private PrimeMotionLease? _overlayMotion;
    private PrimeMotionLease? _notificationMotion;
    private PrimeScanAccentLease? _routeScanMotion;
    private string? _renderedNotificationKey;
    private PrimeRoute _renderedRoute = PrimeRoute.Gateway;
    private CancellationTokenSource _lifetime = new();
    private readonly CancellationTokenSource _restoreLifetime = new();
    private Task? _startupRestoreTask;
    private int _startupRestoreState = (int)PrimeStartupRestoreState.NotRestored;
    private bool _restoreStarted;
    private bool _active;
    private bool _disposed;
    private bool _finished;
    private bool _theatreLoaded;
    private bool _previewCatchupStarted;
    private bool _rankingsInitialLoadPending;
    private bool _playRefreshPending;
    private bool _cosmeticSyncInFlight;
    private bool _cosmeticSyncDirty;
    private bool _accountCosmeticsLoaded;
    private readonly CosmeticSyncRetryPolicy _cosmeticSyncRetries = new();
    private CancellationTokenSource? _cosmeticSyncRetryDelay;
    private PlayState? _capturePlayState;
    private GatewayState? _captureGatewayState;
    private bool _captureExpandAdvancedNetwork;
    private HunterLicensePageState? _captureLicenseState;
    private IReadOnlyList<HunterDossier>? _captureLicenseHunters;
    private bool _captureHunterPreviewFailure;
    private RankingsState? _captureRankingsState;
    private Update.UpdateInfo? _update;
    private Update.UpdateStatus _updateStatus = Update.UpdateCoordinator.Shared.Status;
    private Hunter _selectedLicenseHunter = Hunter.Samus;
    private BeamType _selectedArmoryWeapon = BeamType.PowerBeam;
    private readonly Dictionary<Hunter, string> _hunterPreviewPaths = new();
    private readonly HashSet<Hunter> _hunterPreviewLoads = new();
    private readonly Dictionary<Hunter, DateTimeOffset> _hunterPreviewRetryAfter = new();
    private readonly Dictionary<Hunter, string> _hunterDeathPreviewKeys = new();
    private readonly Dictionary<Hunter, string> _hunterPreviewRequestKeys = new();
    private readonly Dictionary<BeamType, string> _weaponPreviewPaths = new();
    private readonly HashSet<BeamType> _weaponPreviewLoads = new();
    private readonly Dictionary<BeamType, DateTimeOffset> _weaponPreviewRetryAfter = new();
    private int _hunterPreviewLoadStarts;
    private int _weaponPreviewLoadStarts;
    private string? _modelPreviewIdentity;
    private string? _pendingReplayDeleteId;
    private GamepadButtons _previousPadButtons;
    private int _previousPadDirection;
    private DateTimeOffset _nextPadRepeat;
    private ComboBox? _padCombo;
    private int _padComboOriginalIndex;
    private ComboBox? _hunterPadCombo;
    private DeferredControllerSelection<Hunter>? _hunterPadSelection;
    private LobbyChatPanel? _activeLobbyChatPanel;
    private PrimeTitleScreenView? _titleScreen;
    private MatchTransitionView? _matchTransition;
    private GatewayForm _gatewayForm;
    private string _settingsFocusCategory = "Player";
    private SettingsView? _settingsActionView;
    private SettingsActionBar? _settingsActionBar;
    private string? _backgroundStatus;
    private int _busyOperations;
    private string? _overlayModalId;
    private KeyboardNavigationMode _overlayTabNavigationBeforeOpen;
    private bool _overlayTabNavigationCaptured;
    private const string SeatOfferModalPrefix = "seat-offer:";
    private const string MatchTransitionModalId = "match-transition";
    private const string SeatOfferAvailableAnnouncement =
        "A player seat is available. Accept or decline before it expires.";
    private static readonly TimeSpan InitialRestoreUiBudget = TimeSpan.FromSeconds(3);

    public LaunchPlan Plan { get; private set; }
    internal ClientOnlineRuntime Online => _online;
    /// <summary>The shared transition command boundary used by Android's activity overlay.</summary>
    internal IMatchTransitionMenuActions TransitionMenuActions => _play;
    public event EventHandler<LaunchPlan>? Done;
    internal event EventHandler? MatchTransitionReturnToLobbyRequested;

    public PrimeShellView(MenuSettings settings, IReadOnlyList<string> rooms,
        bool restoreOnActivate = true, bool ignoreGameFileGate = false,
        bool captureMode = false, PrimeShellCaptureState? captureState = null,
        ClientOnlineRuntime? onlineRuntime = null, bool showTitleScreen = true,
        PrimeTitleScreenCaptureState? titleCaptureState = null,
        Func<CancellationToken, Task<bool>>? restoreSession = null,
        Func<TimeSpan, CancellationToken, Task>? startupDelay = null,
        Func<PrimeShellState, GatewayController>? gatewayFactory = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _rooms = rooms?.Distinct(StringComparer.Ordinal).ToList() ?? new List<string>();
        _restoreOnActivate = restoreOnActivate;
        _ignoreGameFileGate = ignoreGameFileGate;
        _captureMode = captureMode || captureState != null;
        _titleCaptureState = titleCaptureState;
        PrimeTitleScreenPhase initialTitlePhase = !showTitleScreen
            ? PrimeTitleScreenPhase.Hidden
            : titleCaptureState?.Phase ?? PrimeTitleScreenPhase.Loading;
        _titleLifecycle = new PrimeTitleScreenLifecycle(initialTitlePhase);
        _online = onlineRuntime ?? new ClientOnlineRuntime();
        _ownsOnline = onlineRuntime == null;
        InitializeComponent();
        AddHandler(KeyDownEvent, PreviewTitleKeyDown,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, PreviewTitleKeyUp,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, PreviewTitlePointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, PreviewTitlePointerReleased,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        _navigationAdapter = new PrimeShellNavigationAdapter(this);
        Resources["PrimeReducedMotion"] = LauncherPrefs.ReducedMotion;

        PrimeAccessibility.SetName(AccountButton, "Open account menu");
        PrimeAccessibility.SetName(SettingsButton, "Open settings");
        PrimeAccessibility.SetName(NotificationDismissButton, "Dismiss notification");
        PrimeAccessibility.SetName(InputHintText, "Controller and keyboard controls");
        PrimeAccessibility.SetStatus(ConnectionText, "Offline");

        _gateway = gatewayFactory?.Invoke(_shell) ?? new GatewayController(_shell);
        _restoreUsesGateway = restoreSession is null;
        _restoreSession = restoreSession ?? _gateway.RestoreAsync;
        _startupDelay = startupDelay ?? Task.Delay;
        _play = new PlayController(_shell, _rooms, onlineRuntime: _online);
        _license = new HunterLicenseController(_shell);
        _rankings = new RankingsController(_shell);
        _mapPreviews = new MapPreviewService(_previewImages);
        _hunterPreviews = new HunterPreviewService(_previewImages);
        _weaponPreviews = new WeaponPreviewService(_previewImages);

        InitializeTitleScreen(showTitleScreen, titleCaptureState);

        ApplyCaptureState(captureState);

        _shell.Navigator.Changed += ShellNavigationChanged;
        _shell.PropertyChanged += ShellPropertyChanged;
        _gateway.Changed += GatewayChanged;
        _gateway.IdentityChanged += GatewayIdentityChanged;
        _play.Changed += PlayChanged;
        _play.PresenceChanged += PlayPresenceChanged;
        _play.Launch += LaunchRequested;
        _license.Changed += LicenseChanged;
        _rankings.Changed += RankingsChanged;
        _theatre.Changed += TheatreChanged;
        _theatre.Launch += LaunchRequested;
        Update.UpdateCoordinator.Shared.StatusChanged += UpdateStatusChanged;
        SizeChanged += (_, e) => ApplyResponsiveLayout(e.NewSize.Width, e.NewSize.Height);
        AccountButton.Click += (_, _) => ShowAccountMenu();
        SettingsButton.Click += (_, _) => Navigate(PrimeRoute.Settings);
        NotificationDismissButton.Click += (_, _) => _shell.DismissNotification();

        _inputTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _inputTimer.Tick += (_, _) => PollInput();
        BuildNavigation();
        BuildMobileNavigation();
        RenderRoute(_shell.CurrentRoute);
        RefreshChrome();
    }

    /// <summary>
    /// Build a deterministic, offline capture fixture. It uses a clearly
    /// synthetic preview identity only to unlock authenticated route layout;
    /// no account, Node, or network request is made by this path.
    /// </summary>
    internal static PrimeShellView CreateCapture(MenuSettings settings,
        IReadOnlyList<string> rooms, PrimeRoute route,
        PrimeShellCaptureState? captureState = null)
    {
        var view = new PrimeShellView(settings, rooms, restoreOnActivate: false,
            ignoreGameFileGate: true, captureMode: true, captureState: captureState,
            showTitleScreen: false);
        if (captureState == null && PrimeRouteInfo.IsAuthenticated(route))
        {
            view._shell.SetIdentity(new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                "Capture Preview", emailEligible: true);
        }
        if (route == PrimeRoute.Armory)
            view._routeViewState.SelectHunterSection(HunterSection.Arsenal);
        view._shell.Navigator.NavigateRoot(PrimeRoutePresentation.Normalize(route));
        return view;
    }

    /// <summary>Offline fixture for the joined-lobby layout. It supplies only
    /// immutable view state and never creates a Node transport.</summary>
    internal static PrimeShellView CreateLobbyCapture(MenuSettings settings,
        IReadOnlyList<string> rooms, PrimeShellCaptureState? captureState = null)
    {
        var view = new PrimeShellView(settings, rooms, restoreOnActivate: false,
            ignoreGameFileGate: true, captureMode: true, captureState: captureState,
            showTitleScreen: false);
        if (captureState != null)
        {
            string captureMapKey = captureState.Play?.Lobby?.MapKey ??
                rooms.FirstOrDefault() ?? "UNIT1 ALINOS LANDFALL";
            view._play.SetCaptureMapCatalog(rooms.Count > 0 ? rooms : new[] { captureMapKey });
            view._shell.SetNodeStatus(captureState.Play?.Node?.Session != null,
                "SOL-77", "US-East");
            view._shell.Navigator.NavigateRoot(PrimeRoute.Play);
            return view;
        }
        var playerId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Guid sessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        view._shell.SetIdentity(playerId, "Capture Preview", emailEligible: true);
        string mapKey = rooms.FirstOrDefault() ?? "UNIT1 ALINOS LANDFALL";
        ImmutableArray<LobbyMember> members =
        [
            new(sessionId, playerId.Value, "Capture Preview", Hunter.Samus, 0, true, false),
            new(Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Lastraven",
                Hunter.Sylux, 0, true, false),
            new(Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Vuum",
                Hunter.Noxus, 0, true, false),
            new(Guid.Parse("55555555-5555-5555-5555-555555555555"),
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "Sambot-7",
                Hunter.Kanden, 1, true, false),
            new(Guid.Parse("66666666-6666-6666-6666-666666666666"),
                Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), "Ghostbot-02",
                Hunter.Spire, 1, true, false),
            new(Guid.Parse("77777777-7777-7777-7777-777777777777"),
                Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), "Kikere",
                Hunter.Trace, 1, true, false)
        ];
        var lobby = new LobbySnapshot(
            Guid.Parse("88888888-8888-8888-8888-888888888888"), "Lobby 883",
            LobbyVisibility.Public, sessionId, LobbyPhase.Open, 9, 8, 16,
            members, ImmutableArray<LobbyChatEntry>.Empty, mapKey, MatchMode.TeamBattle,
            BotCount: 0, TimeLimitSeconds: 600, PointGoal: 12);
        var session = new NodeSessionSnapshot(sessionId, playerId.Value, "Capture Preview",
            Guid.Parse("99999999-9999-9999-9999-999999999999"), new string('A', 43));
        var node = new NodeControlClient.ViewState(Session: session, Lobby: lobby);
        view._capturePlayState = new PlayState(PlayPhase.Lobby, Array.Empty<NodeListing>(),
            node, Hunter.Samus, lobby.Name, Loading: false, Revision: lobby.Revision);
        view._play.SetCaptureMapCatalog(rooms.Count > 0 ? rooms : new[] { mapKey });
        view._shell.SetNodeStatus(true, "SOL-77", "US-East");
        view._shell.Navigator.NavigateRoot(PrimeRoute.Play);
        return view;
    }

    /// <summary>Build an offline, deterministic fixture around the real shell
    /// title layer. Underlying route state is intentionally left untouched.</summary>
    internal static PrimeShellView CreateTitleCapture(MenuSettings settings,
        IReadOnlyList<string> rooms, PrimeTitleScreenCaptureState titleState,
        PrimeShellCaptureState? captureState = null)
    {
        var view = new PrimeShellView(settings, rooms, restoreOnActivate: false,
            ignoreGameFileGate: true, captureMode: true,
            captureState: captureState, showTitleScreen: true,
            titleCaptureState: titleState);
        if (captureState?.Play is { } play)
        {
            view._play.SetCaptureMapCatalog(rooms.Count > 0 ? rooms
                : new[] { play.Lobby?.MapKey ?? "MP3 PROVING GROUND" });
            view._shell.SetNodeStatus(play.Node?.Session is not null,
                "SOL-77", "US-East");
            view._shell.Navigator.NavigateRoot(PrimeRoute.Play);
        }
        return view;
    }

    private void ApplyCaptureState(PrimeShellCaptureState? captureState)
    {
        if (captureState is null) return;

        _captureGatewayState = captureState.Gateway;
        _gateway.SetPendingRegistrationForCapture(captureState.PendingRegistration);
        _capturePlayState = captureState.Play;
        _captureExpandAdvancedNetwork = captureState.ExpandAdvancedNetwork;
        _captureLicenseState = captureState.License;
        _captureLicenseHunters = captureState.Hunters;
        _captureHunterPreviewFailure = captureState.HunterPreviewFailure;
        _captureRankingsState = captureState.Rankings;
        if (captureState.PlaySubsection is { } playSubsection)
            _playPresentation.Subsection = playSubsection;
        if (captureState.HunterSection is { } hunterSection)
            _routeViewState.SelectHunterSection(hunterSection);

        switch (captureState.Identity)
        {
            case PrimeShellCaptureIdentity.SignedIn:
                _shell.SetIdentity(captureState.Gateway?.PlayerId
                    ?? new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                    captureState.Gateway?.DisplayName ?? "Capture Preview",
                    captureState.Gateway?.OfficialEligible ?? true);
                break;
            case PrimeShellCaptureIdentity.Guest:
                _shell.SelectGuest(captureState.Gateway?.DisplayName ?? "Capture Guest");
                break;
        }
    }

    private bool IsTitleBlocking => _titleLifecycle.IsBlocking;
    internal PrimeTitleScreenPhase TitlePhase => _titleLifecycle.Phase;
    internal PrimeTitleScreenView? TitleScreen => _titleScreen;
    internal PrimeRoute CurrentRoute => _shell.CurrentRoute;
    internal PrimeStartupRestoreState StartupRestoreState
        => (PrimeStartupRestoreState)Volatile.Read(ref _startupRestoreState);
    internal static TimeSpan StartupRestoreUiBudget => InitialRestoreUiBudget;
    internal Task StartupRestoreTask => _startupRestoreTask ?? Task.CompletedTask;
    internal bool SeatOfferOverlayVisible => IsSeatOfferOverlayOpen();
    internal bool ShellInputEnabled => ShellGrid.IsEnabled
        && ShellGrid.IsHitTestVisible;
    internal Task TitleDismissalTask => _titleDismissalTask ?? Task.CompletedTask;

    private void InitializeTitleScreen(bool showTitleScreen,
        PrimeTitleScreenCaptureState? captureState)
    {
        if (!showTitleScreen || _titleLifecycle.Phase == PrimeTitleScreenPhase.Hidden)
        {
            TitleHost.IsVisible = false;
            return;
        }

        Func<Stream>? background = captureState?.ForceFallback == true
            ? () => throw new FileNotFoundException("Title capture fallback requested.")
            : null;
        _titleScreen = new PrimeTitleScreenView(_titleLifecycle.Phase,
            captureState?.ReducedMotion, allowBlink: captureState is null,
            openBackground: background);
        PrimeInputDevice input = captureState?.InputDevice
            ?? PrimeInputDevice.KeyboardMouse;
        ControllerFamily family = captureState?.ControllerFamily
            ?? ControllerFamily.Generic;
        _titleScreen.SetInputPrompt(input, family);
        TitleHost.Content = _titleScreen;
        TitleHost.IsVisible = true;
        ShellGrid.IsEnabled = false;
        ShellGrid.IsHitTestVisible = false;
    }

    internal bool HandleTitleKeyDown(Key key)
    {
        bool wasHeld = _titleInput.IsKeyHeld(key);
        if (!IsTitleBlocking) return wasHeld;
        bool edge = _titleInput.PressKey(key);
        SetTitleInputPrompt(PrimeInputDevice.KeyboardMouse, ControllerFamily.Generic);
        if (edge && _titleLifecycle.Phase == PrimeTitleScreenPhase.Ready)
            BeginTitleDismissal();
        return true;
    }

    internal bool HandleTitleKeyUp(Key key)
    {
        bool consume = IsTitleBlocking || _titleInput.IsKeyHeld(key);
        _titleInput.ReleaseKey(key);
        return consume;
    }

    internal bool HandleTitlePointerPressed(PointerType pointerType)
    {
        if (!IsTitleBlocking) return false;
        bool edge = _titleInput.PressPointer();
        PrimeInputDevice input = pointerType == PointerType.Mouse
            ? PrimeInputDevice.KeyboardMouse
            : PrimeInputDevice.Touch;
        SetTitleInputPrompt(input, ControllerFamily.Generic);
        if (edge && _titleLifecycle.Phase == PrimeTitleScreenPhase.Ready)
            BeginTitleDismissal();
        return true;
    }

    internal bool HandleTitlePointerReleased()
    {
        bool consume = IsTitleBlocking || _titleInput.PointerHeld;
        _titleInput.ReleasePointer();
        return consume;
    }

    private void PreviewTitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsTitleBlocking && !_titleInput.IsKeyHeld(e.Key)) return;
        e.Handled = HandleTitleKeyDown(e.Key);
    }

    private void PreviewTitleKeyUp(object? sender, KeyEventArgs e)
    {
        if (!IsTitleBlocking && !_titleInput.IsKeyHeld(e.Key)) return;
        e.Handled = HandleTitleKeyUp(e.Key);
    }

    private void PreviewTitlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsTitleBlocking && !_titleInput.PointerHeld) return;
        HandleTitlePointerPressed(e.Pointer.Type);
        e.Handled = true;
    }

    private void PreviewTitlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsTitleBlocking && !_titleInput.PointerHeld) return;
        HandleTitlePointerReleased();
        e.Handled = true;
    }

    internal void MarkTitleReady()
    {
        if (_disposed || !_titleLifecycle.MarkReady()) return;
        _titleScreen?.SetPhase(PrimeTitleScreenPhase.Ready);
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _titleLifecycle.Phase != PrimeTitleScreenPhase.Ready)
                return;
            _titleScreen?.Focus();
        }, DispatcherPriority.Background);
    }

    private void SetTitleInputPrompt(PrimeInputDevice input,
        ControllerFamily family)
    {
        _shell.SetLastInputDevice(input);
        _titleScreen?.SetInputPrompt(input, family);
    }

    internal void HandleTitleControllerState(GamepadState state)
    {
        GamepadButtons pressed = _titleInput.ObserveController(state);
        if (!state.Connected) return;
        SetTitleInputPrompt(PrimeInputDevice.Gamepad, state.Family);
        if (pressed != GamepadButtons.None
            && _titleLifecycle.Phase == PrimeTitleScreenPhase.Ready)
            BeginTitleDismissal();
    }

    private void BeginTitleDismissal()
    {
        if (_disposed || _titleLifecycle.Phase != PrimeTitleScreenPhase.Ready)
            return;
        AbandonStartupRestore(navigateToGateway: true);
        if (!_titleLifecycle.BeginDismissal()) return;
        PrimeTitleScreenView? title = _titleScreen;
        if (title is null)
        {
            CompleteTitleDismissal();
            return;
        }
        title.SetPhase(PrimeTitleScreenPhase.Dismissing);
        _titleDismissalTask = DismissTitleAsync(title);
    }

    private async Task DismissTitleAsync(PrimeTitleScreenView title)
    {
        try
        {
            await title.FadeOutAsync(_titleLifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        PostUi(() =>
        {
            if (_disposed || !ReferenceEquals(_titleScreen, title)
                || _titleLifecycle.Phase != PrimeTitleScreenPhase.Dismissing)
                return;
            CompleteTitleDismissal();
        });
    }

    private void CompleteTitleDismissal(bool teardown = false)
    {
        if (teardown) _titleLifecycle.HideForTeardown();
        else if (!_titleLifecycle.CompleteDismissal()) return;
        PrimeTitleScreenView? title = _titleScreen;
        _titleScreen = null;
        TitleHost.Content = null;
        TitleHost.IsVisible = false;
        ShellGrid.IsEnabled = true;
        ShellGrid.IsHitTestVisible = true;
        title?.Dispose();
        if (_disposed || teardown) return;

        // Seed both title and normal navigation state so the continue press
        // cannot leak into the route beneath the fading layer.
        if (!_captureMode)
            GamepadInput.PollPlatformForMenu();
        _titleInput.SeedController(_captureMode ? default : GamepadInput.State);
        _previousPadButtons = _captureMode
            ? GamepadButtons.None : GamepadInput.EffectiveButtons;
        _previousPadDirection = PadDirection(_previousPadButtons);
        ReconcileSeatOfferModal();
        if (!OverlayRoot.IsVisible)
            ReconcileNavigationFocus();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Activate();
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            if (IsTitleBlocking) _titleScreen?.Focus();
            else ReconcileNavigationFocus();
        }, DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_titleLifecycle.Phase == PrimeTitleScreenPhase.Dismissing)
            CompleteTitleDismissal(teardown: true);
        RememberCurrentNavigationFocus();
        Deactivate();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Activate the shell without disposing its Node session.</summary>
    public void Activate()
    {
        if (_disposed || _active) return;
        _active = true;
        _shell.Activate();
        _play.SetHandoffEnabled(_shell.CurrentRoute == PrimeRoute.Play
            && _shell.HasNetworkIdentity);
        _play.SetPresenceRefreshEnabled(_shell.CurrentRoute == PrimeRoute.Play
            && _shell.HasNetworkIdentity && !_captureMode);
        // Desktop polling owns the freshest physical state. Sample only after
        // it runs, then seed held buttons so activation/reconnect is edge-free.
        if (!_captureMode)
            GamepadInput.PollPlatformForMenu();
        GamepadState titlePadState = _captureMode ? default : GamepadInput.State;
        _titleInput.SeedController(titlePadState);
        _previousPadButtons = _captureMode
            ? GamepadButtons.None : GamepadInput.EffectiveButtons;
        _previousPadDirection = PadDirection(_previousPadButtons);
        _nextPadRepeat = DateTimeOffset.UtcNow.AddMilliseconds(350);
        if (!_captureMode)
            _inputTimer.Start();
        Dispatcher.UIThread.Post(ReconcileNavigationFocus,
            DispatcherPriority.Background);
        if (!_captureMode)
            StartPreviewCatchup();
        if (_restoreOnActivate && !_restoreStarted)
        {
            _restoreStarted = true;
            if (LauncherPrefs.UpdatePolicy != UpdatePolicy.Off && Update.Updater.Configured)
            {
                Update.Updater.CheckInBackground(update => PostUi(() =>
                {
                    _update = update;
                    RefreshChrome();
                }), () => PostUi(() => _ = TryInstallStaged()));
            }
            Interlocked.Exchange(ref _startupRestoreState,
                (int)PrimeStartupRestoreState.Pending);
            _startupRestoreTask = RunStartupRestoreAsync();
        }
        else if (!_restoreOnActivate && _titleCaptureState is null)
            MarkTitleReady();
        // Android returns here after a match and after the install-source
        // settings screen. Retrying on every activation lets a staged APK be
        // submitted without requiring a second background check.
        if (_restoreOnActivate) _ = TryInstallStaged();
    }

    private async Task RunStartupRestoreAsync()
    {
        CancellationToken cancellationToken = _restoreLifetime.Token;
        try
        {
            Task<bool> restoreSource = _restoreSession(cancellationToken);
            ObserveAbandonedTask(restoreSource);
            Task<bool> restore = restoreSource.WaitAsync(cancellationToken);

            Task delaySource = _startupDelay(InitialRestoreUiBudget,
                cancellationToken);
            ObserveAbandonedTask(delaySource);
            Task budget = delaySource.WaitAsync(cancellationToken);

            Task winner = await Task.WhenAny(restore, budget).ConfigureAwait(false);
            if (ReferenceEquals(winner, budget))
            {
                await budget.ConfigureAwait(false);
                PostUi(() =>
                {
                    if (!_disposed
                        && StartupRestoreState == PrimeStartupRestoreState.Pending)
                        MarkTitleReady();
                });
            }

            bool restored = await restore.ConfigureAwait(false);
            PostUi(() => CompleteStartupRestore(restored));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Continue and disposal intentionally cancel only the automatic
            // restore attempt; saved credentials and token rotation remain.
        }
        catch
        {
            PostUi(() => CompleteStartupRestore(restored: false));
        }
    }

    private void CompleteStartupRestore(bool restored)
    {
        if (_disposed) return;
        PrimeStartupRestoreState completed = restored
            ? PrimeStartupRestoreState.Restored
            : PrimeStartupRestoreState.NotRestored;
        if (Interlocked.CompareExchange(ref _startupRestoreState, (int)completed,
            (int)PrimeStartupRestoreState.Pending)
            != (int)PrimeStartupRestoreState.Pending)
            return;

        // The route beneath the title is selected before Ready becomes
        // visible, so a fast Continue can never reveal the wrong surface.
        _shell.Navigator.NavigateRoot(restored ? PrimeRoute.Play : PrimeRoute.Gateway);
        MarkTitleReady();
    }

    private bool AbandonStartupRestore(bool navigateToGateway)
    {
        PrimeStartupRestoreState target;
        bool cancelRestore;
        if (_restoreUsesGateway)
        {
            cancelRestore = _gateway.TryAbandonAutomaticRestore();
            target = cancelRestore
                ? PrimeStartupRestoreState.Abandoned
                : _gateway.AutomaticRestoreState
                    == AutomaticRestoreCommitState.Committed
                    ? PrimeStartupRestoreState.Restored
                    : PrimeStartupRestoreState.NotRestored;
        }
        else
        {
            cancelRestore = true;
            target = PrimeStartupRestoreState.Abandoned;
        }

        int prior = Interlocked.CompareExchange(ref _startupRestoreState,
            (int)target, (int)PrimeStartupRestoreState.Pending);
        bool transitioned = prior == (int)PrimeStartupRestoreState.Pending;
        if (transitioned && cancelRestore)
            _restoreLifetime.Cancel();

        if (navigateToGateway && transitioned && !_disposed)
            _shell.Navigator.NavigateRoot(target == PrimeStartupRestoreState.Restored
                ? PrimeRoute.Play : PrimeRoute.Gateway);
        return transitioned
            && target == PrimeStartupRestoreState.Abandoned;
    }

    private static void ObserveAbandonedTask(Task task)
        => _ = task.ContinueWith(completed => _ = completed.Exception,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted
                | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>
    /// Deactivate UI polling and transient requests while retaining Node
    /// ownership. Android uses this during a match; the next activation can
    /// resume the same session.
    /// </summary>
    public void Deactivate()
    {
        if (!_active) return;
        if (_titleLifecycle.Phase == PrimeTitleScreenPhase.Dismissing)
            CompleteTitleDismissal(teardown: true);
        _active = false;
        SuspendSeatOfferModal();
        _activeLobbyChatPanel = null;
        _shell.Deactivate();
        _play.SetHandoffEnabled(false);
        _play.SetPresenceRefreshEnabled(false);
        _inputTimer.Stop();
        ResetCosmeticSyncRetry();
        _lifetime.Cancel();
        _lifetime.Dispose();
        _lifetime = new CancellationTokenSource();
    }

    internal PlayController Play => _play;
    internal GatewayController Gateway => _gateway;
    internal bool CaptureMode => _captureMode;

    /// <summary>
    /// Narrow route boundary used by the development-only semantic control
    /// adapter. The adapter never receives a mutable navigator or controller;
    /// it can request only the existing Play route through this owner.
    /// </summary>
    internal bool TryOpenPlayFromSemanticControl()
    {
        if (IsTitleBlocking || _disposed) return false;
        Navigate(PrimeRoute.Play);
        return true;
    }

    internal int HunterPreviewLoadStarts => _hunterPreviewLoadStarts;
    internal int WeaponPreviewLoadStarts => _weaponPreviewLoadStarts;
    internal GatewayState? CaptureGatewayState => _captureGatewayState;
    internal PlayState? CapturePlayState => _capturePlayState;
    internal HunterLicensePageState? CaptureLicenseState => _captureLicenseState;
    internal RankingsState? CaptureRankingsState => _captureRankingsState;
    internal Control? CaptureRouteContent => PageHost.Content as Control;

    /// <summary>
    /// Mount a deterministic route presentation inside the real shell chrome.
    /// This keeps screenshot fixtures on the same inherited styles, viewport,
    /// navigation, and footer surface as production without starting route I/O.
    /// </summary>
    internal void SetRouteContentForCapture(PrimeRoute route, Control content)
    {
        if (!_captureMode)
            throw new InvalidOperationException("Route content injection is capture-only.");
        PrimeRoute normalized = PrimeRoutePresentation.Normalize(route);
        _shell.Navigator.NavigateRoot(normalized);
        if (PageHost.Content is IDisposable previous)
            previous.Dispose();
        PageHost.Content = content ?? throw new ArgumentNullException(nameof(content));
        RouteTitle.Text = PrimeRouteInfo.Label(normalized);
        _renderedRoute = normalized;
        RefreshNavigation();
        RefreshChrome();
    }
    internal MatchTransitionView? ActiveMatchTransition => _matchTransition;
    internal void SetMenuInputEnabled(bool enabled)
    {
        if (enabled && _active && !_captureMode) _inputTimer.Start();
        else _inputTimer.Stop();
    }

    public void ShowMatchOutcome(MatchRunResult result)
    {
        if (result.Reason is MatchExitReason.Completed or MatchExitReason.LeftMatch) return;
        _shell.NotifyGlobal("match-outcome", PrimeNotificationKind.Error,
            (result.Reason == MatchExitReason.FailedToStart ? "Match could not start. " : "Match connection lost. ")
            + result.Message + " Return to your lobby and try again.");
    }

    internal void ShowMatchTransition(MatchTransitionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_disposed || IsTitleBlocking) return;
        if (_matchTransition is not null
            && String.Equals(_overlayModalId, MatchTransitionModalId,
                StringComparison.Ordinal))
        {
            _matchTransition.Update(state);
            return;
        }

        var transition = new MatchTransitionView(state);
        transition.ReturnToLobbyRequested += MatchTransitionReturnRequested;
        ShowOverlay(transition, MatchTransitionModalId);
        if (ReferenceEquals(OverlayHost.Content, transition))
        {
            _matchTransition = transition;
            return;
        }

        transition.ReturnToLobbyRequested -= MatchTransitionReturnRequested;
        transition.Dispose();
    }

    internal void UpdateMatchTransition(MatchTransitionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_disposed || _matchTransition is null
            || !String.Equals(_overlayModalId, MatchTransitionModalId,
                StringComparison.Ordinal))
            return;
        _matchTransition.Update(state);
    }

    internal void FailMatchTransition(string? detail)
    {
        if (_disposed)
            return;
        string failure = String.IsNullOrWhiteSpace(detail)
            ? "The match could not continue." : detail.Trim();
        if (_matchTransition is null
            || !String.Equals(_overlayModalId, MatchTransitionModalId,
                StringComparison.Ordinal))
        {
            ShowMatchTransition(new MatchTransitionState(
                MatchTransitionStage.Failed, Detail: failure));
            return;
        }
        _matchTransition.Update(_matchTransition.State with
        {
            Stage = MatchTransitionStage.Failed,
            Detail = failure
        });
    }

    internal void CloseMatchTransition()
    {
        if (!String.Equals(_overlayModalId, MatchTransitionModalId,
            StringComparison.Ordinal))
            return;
        CloseOverlay();
    }

    private void MatchTransitionReturnRequested(object? sender, EventArgs args)
    {
        CloseMatchTransition();
        MatchTransitionReturnToLobbyRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DetachMatchTransition()
    {
        MatchTransitionView? transition = _matchTransition;
        _matchTransition = null;
        if (transition is null) return;
        transition.ReturnToLobbyRequested -= MatchTransitionReturnRequested;
        transition.Dispose();
    }

    public void Reset()
    {
        if (_disposed) return;
        _settings = ClientSettings.LoadSettings();
        _finished = false;
        Plan = default;
        Hunters.Reroll();
        _shell.ClearNotification();
        _theatreLoaded = false;
        PrimeRoute root = _shell.HasNetworkIdentity ? PrimeRoute.Play : PrimeRoute.Gateway;
        bool alreadyAtRoot = _shell.Navigator.CurrentRoute == root
            && _shell.Navigator.HistoryCount == 0;
        _shell.Navigator.NavigateRoot(root);
        // NavigateRoot intentionally suppresses a redundant navigation event.
        // Match return is not redundant: Deactivate replaced the shell lifetime,
        // so every command callback must be rebuilt against the fresh token.
        if (alreadyAtRoot) RenderRoute(root);

    }

    /// <summary>Re-arms the persistent shell for a Node-owned continuation
    /// without resetting its route, session, or transition presentation.</summary>
    internal void PrepareForContinuationLaunch()
    {
        if (_disposed) return;
        _finished = false;
        Plan = default;
    }

    /// <summary>Handle Escape or Android back without ending a match.</summary>
    public bool GoBack()
    {
        if (IsTitleBlocking) return true;
        // This overlay is a view of coordinator-owned state, not a normal
        // dismissible modal. Removing it would leave loading running without
        // coverage, or strand a failed transition without its recovery action.
        if (_matchTransition != null
            && String.Equals(_overlayModalId, MatchTransitionModalId,
                StringComparison.Ordinal))
            return true;
        if (IsSeatOfferOverlayOpen())
        {
            if (_seatOfferModal.Active is { } key)
                BeginSeatOfferAction(key, accept: false);
            else CloseSeatOfferOverlay();
            return true;
        }
        if (OverlayRoot.IsVisible)
        {
            CloseOverlay();
            return true;
        }
        if (PrimeRoutePresentation.Normalize(_shell.CurrentRoute) == PrimeRoute.Play)
        {
            if (_activeLobbyChatPanel?.TryExitEditing() == true)
            {
                _playPresentation.TryExitChatEditing();
                return true;
            }
            if (_playPresentation.TryExitChatEditing()) return true;
        }
        if (_shell.Navigator.GoBack()) return true;
        PrimeRoute root = _shell.HasNetworkIdentity ? PrimeRoute.Play : PrimeRoute.Gateway;
        if (_shell.CurrentRoute != root)
        {
            _shell.Navigator.NavigateRoot(root);
            return true;
        }
        return false;
    }

    /// <summary>Use the shared PauseMenuView over a running match.</summary>
    public void ShowPauseMenu(Scene scene, Action onResume, Action onLeave, Action onQuit,
        IMatchTransitionMenuActions? transitionActions = null)
    {
        ArgumentNullException.ThrowIfNull(onResume);
        ArgumentNullException.ThrowIfNull(onLeave);
        ArgumentNullException.ThrowIfNull(onQuit);
        IMatchTransitionMenuActions actions = transitionActions ?? _play;
        var view = new PauseMenuView(offerWindowMode: false, transitionActions: actions);
        void RefreshTransition()
        {
            PostUi(() =>
            {
                if (ReferenceEquals(OverlayHost.Content, view))
                    view.RefreshTransitionPresentation();
            });
        }
        void ActionsChanged(object? sender, EventArgs args) => RefreshTransition();
        actions.Changed += ActionsChanged;
        void Close()
        {
            actions.Changed -= ActionsChanged;
            CloseOverlay();
        }
        view.Resumed += (_, _) => { Close(); onResume(); };
        view.LeaveRequested += (_, _) => { Close(); onLeave(); };
        view.QuitRequested += (_, _) => { Close(); onQuit(); };
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
            if (ReplayRecorder.IsRecording) ReplayRecorder.Stop();
            else ReplayRecorder.Start();
            Close();
            onResume();
        };
        view.RestartMatchRequested += (_, _) => RunCommand("Restart match", async () =>
        {
            try { await actions.RequestRestartMatchAsync(_lifetime.Token).ConfigureAwait(false); }
            finally { RefreshTransition(); }
        });
        view.ChangeMapRequested += (_, _) =>
        {
            Close();
            ShowTransitionMapPicker(scene, actions, onResume, onLeave, onQuit);
        };
        view.HunterChangeRequested += hunter => RunCommand("Change hunter", async () =>
        {
            try { await actions.RequestHunterChangeAsync(hunter, _lifetime.Token)
                    .ConfigureAwait(false); }
            finally { RefreshTransition(); }
        });
        view.TransitionVoteRequested += accept => RunCommand("Transition vote", async () =>
        {
            try { await actions.RequestTransitionVoteAsync(accept, _lifetime.Token)
                    .ConfigureAwait(false); }
            finally { RefreshTransition(); }
        });
        view.SettingsRequested += (_, _) =>
        {
            Close();
            var settings = new SettingsView(_settings, inGame: true, scene: scene,
                identity: SettingsIdentityContext.From(_shell,
                    () => CloseAndNavigate(PrimeRoute.Hunter)));
            settings.SaveSucceeded += (_, _) =>
            {
                _shell.NotifyGlobal("settings-saved", PrimeNotificationKind.Success,
                    "Settings saved to this device.");
                RefreshChrome();
            };
            settings.Closed += (_, _) =>
            {
                Resources["PrimeReducedMotion"] = LauncherPrefs.ReducedMotion;
                CloseOverlay();
                ShowPauseMenu(scene, onResume, onLeave, onQuit, actions);
            };
            ShowOverlay(settings, "pause-settings");
        };
        ShowOverlay(view, "pause-menu");
        view.FocusResume();
    }

    private void ShowTransitionMapPicker(Scene scene,
        IMatchTransitionMenuActions actions, Action onResume, Action onLeave, Action onQuit)
    {
        IReadOnlyList<string> maps = actions.AvailableTransitionMaps;
        if (maps.Count == 0) return;
        var picker = new MapPickerView(maps, actions.CurrentMapKey ?? "",
            excludeCurrent: true);
        EventHandler? closed = null;
        closed = (_, _) =>
        {
            picker.Closed -= closed;
            CloseOverlay();
            string? map = picker.RoomKey;
            ShowPauseMenu(scene, onResume, onLeave, onQuit, actions);
            if (map == null) return;
            RunCommand("Change map", async () =>
            {
                try { await actions.RequestChangeMapAsync(map, _lifetime.Token)
                        .ConfigureAwait(false); }
                finally
                {
                    PostUi(() =>
                    {
                        if (OverlayHost.Content is PauseMenuView next)
                            next.RefreshTransitionPresentation();
                    });
                }
            });
        };
        picker.Closed += closed;
        ShowOverlay(picker, "pause-map-picker");
        picker.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Handled) return;
        _shell.SetLastInputDevice(PrimeInputDevice.KeyboardMouse);
        if (e.Key == Key.Escape)
        {
            if (!GoBack()) Finish(default);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Handled) return;
        base.OnKeyUp(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.Handled) return;
        _shell.SetLastInputDevice(e.Pointer.Type == PointerType.Mouse
            ? PrimeInputDevice.KeyboardMouse
            : PrimeInputDevice.Touch);
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (e.Handled) return;
        base.OnPointerReleased(e);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        AbandonStartupRestore(navigateToGateway: false);
        _titleLifetime.Cancel();
        if (_titleLifecycle.IsBlocking)
            CompleteTitleDismissal(teardown: true);
        Deactivate();
        CloseSeatOfferOverlay();
        _seatOfferModal.Reset();
        _activeLobbyChatPanel = null;
        _restoreLifetime.Cancel();
        _shell.Navigator.Changed -= ShellNavigationChanged;
        _shell.PropertyChanged -= ShellPropertyChanged;
        _gateway.Changed -= GatewayChanged;
        _gateway.IdentityChanged -= GatewayIdentityChanged;
        _play.Changed -= PlayChanged;
        _play.PresenceChanged -= PlayPresenceChanged;
        _play.Launch -= LaunchRequested;
        _license.Changed -= LicenseChanged;
        _rankings.Changed -= RankingsChanged;
        _theatre.Changed -= TheatreChanged;
        _theatre.Launch -= LaunchRequested;
        Update.UpdateCoordinator.Shared.StatusChanged -= UpdateStatusChanged;
        _routeMotion?.Dispose();
        _overlayMotion?.Dispose();
        _notificationMotion?.Dispose();
        _routeScanMotion?.Dispose();
        DetachMatchTransition();
        if (_overlayTabNavigationCaptured)
        {
            KeyboardNavigation.SetTabNavigation(OverlayRoot,
                _overlayTabNavigationBeforeOpen);
            _overlayTabNavigationCaptured = false;
        }
        if (_startupRestoreTask is not null)
        {
            try { await _startupRestoreTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        await _gateway.DisposeAsync().ConfigureAwait(false);
        await _play.DisposeAsync().ConfigureAwait(false);
        if (_ownsOnline) await _online.DisposeAsync().ConfigureAwait(false);
        _license.Dispose();
        _rankings.Dispose();
        _theatre.Dispose();
        _previewImages.Dispose();
        _shell.Dispose();
        _lifetime.Dispose();
        _restoreLifetime.Dispose();
        _titleLifetime.Dispose();
    }

    private void BuildNavigation()
    {
        NavPanel.Children.Clear();
        _navigationButtons.Clear();
        foreach (PrimeRoute route in PrimeRouteInfo.Navigation)
        {
            var button = MakeButton(PrimeRouteInfo.Label(route), () => Navigate(route),
                quiet: true);
            button.MinHeight = PrimeLayoutMetrics.MinimumTouchTargetDip;
            ToolTip.SetTip(button, PrimeRouteInfo.Label(route));
            _navigationButtons.Add((button, route));
            NavPanel.Children.Add(button);
        }
    }

    private void BuildMobileNavigation()
    {
        MobileNavPanel.Children.Clear();
        _mobileNavigationButtons.Clear();
        int column = 0;
        foreach (PrimeRoute route in PrimeRoutePresentation.MobilePrimary)
        {
            var button = MakeButton(
                PrimeRoutePresentation.NavigationLabel(route, PrimeShellBreakpoint.Mobile),
                () => Navigate(route), quiet: true);
            button.MinHeight = PrimeLayoutMetrics.MinimumTouchTargetDip;
            PrimeAccessibility.SetName(button, $"Open {PrimeRouteInfo.Label(route)}");
            _mobileNavigationButtons.Add((button, route));
            MobileNavPanel.Children.Add(button);
            Grid.SetColumn(button, column++);
        }
        var more = MakeButton("More", ShowMoreMenu, quiet: true);
        more.MinHeight = PrimeLayoutMetrics.MinimumTouchTargetDip;
        PrimeAccessibility.SetName(more, "Open more destinations");
        MobileNavPanel.Children.Add(more);
        Grid.SetColumn(more, 3);
    }

    private void ShowMoreMenu()
    {
        var content = Stack(
            Text("More", "prime-title"),
            Text("Routes, account, and connection details.", "prime-muted"));
        content.Children.Add(MakeButton("Theatre", () => CloseAndNavigate(PrimeRoute.Theatre)));
        content.Children.Add(MakeButton("Maps", () => CloseAndNavigate(PrimeRoute.Maps)));
        content.Children.Add(MakeButton("Settings", () => CloseAndNavigate(PrimeRoute.Settings)));
        content.Children.Add(MakeButton("Account", ShowAccountMenu));
        content.Children.Add(MakeButton("Connection", ShowConnectionDetails));
        content.Children.Add(MakeButton("About", ShowAbout));
        content.Children.Add(MakeButton("Close", CloseOverlay, quiet: true));
        ShowOverlay(Card(content), "more-menu");
    }

    private void ShowAccountMenu()
    {
        var content = Stack(
            Text(_shell.HasNetworkIdentity ? _shell.DisplayName : "Account", "prime-title"),
            Text(_shell.SignedIn ? "Prime account" : _shell.GuestSelected
                ? "Guest session" : "Not signed in", "prime-muted"));
        if (_shell.SignedIn)
            content.Children.Add(MakeButton("Hunter Profile", () => CloseAndNavigate(PrimeRoute.Hunter)));
        else if (_shell.GuestSelected)
            content.Children.Add(MakeButton("Create Account", () => CloseAndNavigate(PrimeRoute.Gateway)));
        content.Children.Add(MakeButton("Settings", () => CloseAndNavigate(PrimeRoute.Settings)));
        content.Children.Add(MakeButton("Connection", ShowConnectionDetails));
        if (_shell.HasNetworkIdentity)
        {
            string label = _shell.SignedIn ? "Sign Out" : "Leave Guest Session";
            Action exit = PrimeShellNavigationAdapter.RequiresIdentityExitConfirmation(
                _play.State.Lobby, _play.State.Handoff)
                ? () => ShowIdentityExitConfirmation(label)
                : () => SignOutOrLeaveGuest(label);
            content.Children.Add(MakeButton(label, exit));
        }
        else content.Children.Add(MakeButton("Sign In", () => CloseAndNavigate(PrimeRoute.Gateway)));
        content.Children.Add(MakeButton("Close", CloseOverlay, quiet: true));
        ShowOverlay(Card(content), "account-menu");
    }

    private void ShowIdentityExitConfirmation(string operation)
    {
        string lobbyName = _play.State.Lobby?.Name ?? "the current lobby";
        var content = Stack(
            Text("Leave lobby?", "prime-title"),
            Text($"{operation} will disconnect you from {lobbyName} and abandon recoverable lobby membership.",
                "prime-body"),
            MakeButton($"Confirm {operation}", () => SignOutOrLeaveGuest(operation),
                primary: true),
            MakeButton("Keep lobby", ShowAccountMenu, quiet: true));
        ShowOverlay(Card(content), "identity-exit-confirmation");
    }

    private void SignOutOrLeaveGuest(string operation)
    {
        CloseOverlay();
        RunCommand(operation, async () =>
        {
            if (await _gateway.SignOutAsync(_lifetime.Token).ConfigureAwait(false))
                PostUi(() => _shell.Navigator.NavigateRoot(PrimeRoute.Gateway));
        });
    }

    private void ShowConnectionDetails()
    {
        string summary = _shell.NodeConnected
            ? $"{_shell.NodeRegion} · Connected".TrimStart(' ', '·')
            : _shell.BackendConnected ? "Online services available" : "Offline";
        ShowOverlay(Card(Stack(
            Text("Connection", "prime-title"),
            Text(summary, "prime-body"),
            Text("Detailed network telemetry is available only in diagnostics.", "prime-muted"),
            MakeButton("Close", CloseOverlay, quiet: true))), "connection-details");
    }

    private void ShowAbout()
        => ShowOverlay(Card(Stack(
            Text("Project Prime", "prime-title"),
            Text("A multiplayer-focused rebuild of Metroid Prime Hunters.", "prime-body"),
            MakeButton("Close", CloseOverlay, quiet: true))), "about");

    private void CloseAndNavigate(PrimeRoute route)
    {
        CloseOverlay();
        Navigate(route);
    }

    private void Navigate(PrimeRoute route)
    {
        if (IsTitleBlocking) return;
        _shell.Navigator.Navigate(route);
        RefreshSelectedAccountRoute(route);
    }

    /// <summary>
    /// Hunter and Rankings are live account projections. Selecting either
    /// destination is an explicit refresh request even when the navigator is
    /// already on that route and therefore emits no navigation change.
    /// </summary>
    private void RefreshSelectedAccountRoute(PrimeRoute route)
    {
        if (_captureMode || !_shell.SignedIn) return;
        switch (PrimeRoutePresentation.Normalize(route))
        {
            case PrimeRoute.Hunter:
                RunCommand("Refresh Hunter", RefreshLicenseOverviewAsync);
                break;
            case PrimeRoute.Rankings:
                RunCommand("Refresh Rankings", () =>
                    _rankings.LoadAsync(cancellationToken: _lifetime.Token));
                break;
        }
    }

    private PrimeFocusScope FocusScope(PrimeRoute route)
    {
        PrimeRoute normalized = PrimeRoutePresentation.Normalize(route);
        string? subsection = normalized switch
        {
            PrimeRoute.Hunter => _routeViewState.HunterSection.ToString(),
            PrimeRoute.Play => _playPresentation.Subsection.ToString(),
            PrimeRoute.Settings => _settingsFocusCategory,
            _ => null
        };
        return new PrimeFocusScope(normalized, subsection);
    }

    private IReadOnlyList<PrimeShellNavigationTarget> CaptureNavigationTargets()
    {
        PrimeRoute route = PrimeRoutePresentation.Normalize(_shell.CurrentRoute);
        var chrome = new Control[] { HeaderBorder, FooterBorder,
            MobileNavigationBorder };
        Control? openEditor = _padCombo?.IsDropDownOpen == true ? _padCombo : null;
        return _navigationAdapter.Capture(PageHost, chrome, OverlayHost,
            OverlayRoot.IsVisible, route, route == PrimeRoute.Settings
                ? _settingsFocusCategory : null, _overlayModalId, openEditor);
    }

    private void RememberCurrentNavigationFocus()
    {
        if (IsTitleBlocking) return;
        IReadOnlyList<PrimeShellNavigationTarget> targets = CaptureNavigationTargets();
        object? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (PrimeShellNavigationAdapter.FindFocusedId(targets, focused) is { } focusId)
            _inputNavigator.TrackFocused(focusId);
        _inputNavigator.RememberFocused();
    }

    private void ReconcileNavigationFocus()
    {
        if (_disposed || IsTitleBlocking) return;
        PrimeRoute route = PrimeRoutePresentation.Normalize(_shell.CurrentRoute);
        IReadOnlyList<PrimeShellNavigationTarget> targets = CaptureNavigationTargets();
        if (route == PrimeRoute.Settings
            && PrimeShellNavigationAdapter.SelectedSettingsSection(targets) is { } selected)
            _settingsFocusCategory = selected;
        PrimeFocusScope scope = FocusScope(route);
        PrimeNavigationResult result = _inputNavigator.EnterScope(scope,
            targets.Select(target => target.Candidate));
        PrimeShellNavigationAdapter.Apply(targets, result);
    }

    private void RestoreNavigationFocus(PrimeRoute route)
    {
        if (IsTitleBlocking) return;
        PrimeFocusScope scope = FocusScope(route);
        IReadOnlyList<PrimeShellNavigationTarget> targets = CaptureNavigationTargets();
        PrimeNavigationResult result = _inputNavigator.EnterScope(scope,
            targets.Select(target => target.Candidate));
        PrimeShellNavigationAdapter.Apply(targets, result);

        // Layout may not have measured a freshly-built route yet. Re-measure
        // once at background priority so bounds, scroll hosts, and focus all
        // use the final shell coordinate root.
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || IsTitleBlocking || OverlayRoot.IsVisible
                || PrimeRoutePresentation.Normalize(_shell.CurrentRoute) != route)
                return;
            IReadOnlyList<PrimeShellNavigationTarget> rebuilt =
                CaptureNavigationTargets();
            PrimeNavigationResult restored = _inputNavigator.RestoreScope(scope,
                rebuilt.Select(target => target.Candidate));
            PrimeShellNavigationAdapter.Apply(rebuilt, restored);
        }, DispatcherPriority.Background);
    }

    private static PrimeNavigationDirection NavigationDirection(int direction)
        => direction switch
        {
            < -1 => PrimeNavigationDirection.Left,
            -1 => PrimeNavigationDirection.Up,
            1 => PrimeNavigationDirection.Down,
            > 1 => PrimeNavigationDirection.Right,
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

    private static bool SupportsControllerAdjustment(object? focused)
        => focused is ChoiceRow or SliderRow or ToggleRow;

    private void RenderRoute(PrimeRoute route)
    {
        PrimeRoute normalizedRoute = PrimeRoutePresentation.Normalize(route);
        bool routeChanged = _renderedRoute != normalizedRoute;
        if (normalizedRoute == PrimeRoute.Hunter && _shell.GuestSelected)
            _routeViewState.PrepareHunterEntry(signedIn: false);
        RememberCurrentNavigationFocus();
        _routeMotion?.Dispose();
        _routeMotion = null;
        _routeScanMotion?.Dispose();
        _routeScanMotion = null;
        if (route == PrimeRoute.Armory)
            _routeViewState.SelectHunterSection(HunterSection.Arsenal);
        if (PageHost.Content != null)
            _routeViewState.CaptureScroll(_renderedRoute, PageScroller.Offset);
        _padCombo = null;
        _padComboOriginalIndex = -1;
        _hunterPadCombo = null;
        _hunterPadSelection = null;
        _activeLobbyChatPanel = null;
        _play.SetHandoffEnabled(_active && normalizedRoute == PrimeRoute.Play
            && _shell.HasNetworkIdentity);
        _play.SetPresenceRefreshEnabled(_active && normalizedRoute == PrimeRoute.Play
            && _shell.HasNetworkIdentity && !_captureMode);
        if (!_ignoreGameFileGate && !GameFiles.Ready)
        {
            RouteTitle.Text = "Game files";
            PageHost.Content = BuildGameFilesPage();
            _renderedRoute = normalizedRoute;
            RestoreRouteScroll(normalizedRoute);
            RefreshNavigation();
            RefreshChrome();
            if (PageHost.Content is Control gatedContent)
            {
                if (_captureMode) gatedContent.Opacity = 1;
                else if (routeChanged)
                    _routeMotion = PrimeMotion.AnimateEntry(gatedContent);
            }
            if (!OverlayRoot.IsVisible)
                RestoreNavigationFocus(normalizedRoute);
            ReconcileSeatOfferModal();
            return;
        }
        RouteTitle.Text = PrimeRouteInfo.Label(normalizedRoute);
        PageHost.Content = normalizedRoute switch
        {
            PrimeRoute.Gateway => BuildGatewayPage(),
            PrimeRoute.Play => BuildPlayPage(),
            PrimeRoute.Maps => new MapsHubView(_captureMode),
            PrimeRoute.Hunter => BuildHunterPage(),
            PrimeRoute.Theatre => BuildTheatrePage(),
            PrimeRoute.Rankings => BuildRankingsPage(),
            PrimeRoute.Settings => BuildSettingsPage(),
            _ => BuildGatewayPage()
        };
        _renderedRoute = normalizedRoute;
        RestoreRouteScroll(normalizedRoute);
        RefreshNavigation();
        RefreshChrome();
        if (PageHost.Content is Control routeContent)
        {
            if (_captureMode) routeContent.Opacity = 1;
            else if (routeChanged)
                _routeMotion = PrimeMotion.AnimateEntry(routeContent);
        }
        if (!_captureMode && routeChanged)
            _routeScanMotion = PrimeMotion.AnimateScan(RouteScanAccent,
                Math.Max(240, PageGrid.Bounds.Height));
        if (!OverlayRoot.IsVisible)
            RestoreNavigationFocus(normalizedRoute);
        ReconcileSeatOfferModal();
    }

    private void RestoreRouteScroll(PrimeRoute route)
    {
        Vector offset = _routeViewState.ScrollFor(route);
        Dispatcher.UIThread.Post(() => PageScroller.Offset = offset,
            DispatcherPriority.Background);
    }

    private Control BuildGatewayPage()
    {
        GatewayState gateway = _captureGatewayState ?? _gateway.State;
        PendingRegistration? pending = _gateway.PendingRegistration;
        GatewayForm form = _captureGatewayState is not null
            ? gateway.Phase switch
            {
                GatewayPhase.SigningIn => GatewayForm.SignIn,
                GatewayPhase.Registering => GatewayForm.Register,
                GatewayPhase.Confirming => GatewayForm.Confirm,
                _ => GatewayForm.Landing
            }
            : pending is not null ? GatewayForm.Confirm : _gatewayForm;
        var root = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        var frameContent = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,320"),
            RowDefinitions = new RowDefinitions("Auto"),
            ColumnSpacing = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = 1080,
            Margin = new Thickness(24, 16)
        };
        frameContent.Classes.Add("prime-gateway-layout");

        GatewayForm activeForm = form;
        string headingTitle = activeForm switch
        {
            GatewayForm.Register => "Create account",
            GatewayForm.Confirm => "Confirm your email",
            _ => "Sign in"
        };
        string headingKicker = activeForm switch
        {
            GatewayForm.Register => "ACCOUNT SETUP",
            GatewayForm.Confirm => "ACCOUNT ACCESS",
            _ => "SIGN IN"
        };
        var content = Stack();
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.MaxWidth = 580;
        content.HorizontalAlignment = HorizontalAlignment.Center;

        var heading = PrimeControlFactory.PageHeading(headingTitle, headingKicker,
            subtitle: "Welcome to Project Prime.");
        CenterHeading(heading);
        content.Children.Add(heading);
        content.Children.Add(Text("Sign in or continue as a guest.", "prime-muted"));
        // Keep the legacy landing prompt available to older capture consumers
        // while the production route uses the signed-out auth language.
        if (_captureMode)
            content.Children.Add(Text("Sign in, create an account, or continue as a guest.",
                "prime-muted"));
        if (_captureMode)
        {
            content.Children.Add(new TextBlock
            {
                Text = "ENTER THE ARENA",
                IsVisible = false,
                Classes = { "prime-kicker" }
            });
        }

        Control formContent = form switch
        {
            GatewayForm.SignIn => BuildGatewaySignInForm(),
            GatewayForm.Register => BuildGatewayRegistrationForm(),
            GatewayForm.Confirm => BuildGatewayConfirmationForm(pending),
            _ => BuildGatewayLandingForm()
        };
        var card = Card(formContent);
        card.HorizontalAlignment = HorizontalAlignment.Stretch;
        var cardHost = new Border { Child = card, HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = 580 };
        content.Children.Add(cardHost);
        bool redundantSignedOutMessage = gateway.Phase == GatewayPhase.Gateway
            && (gateway.Message.Contains("saved session", StringComparison.OrdinalIgnoreCase)
                || gateway.Message.Contains("choose sign in",
                    StringComparison.OrdinalIgnoreCase));
        bool showStatus = !redundantSignedOutMessage
            && (gateway.Phase != GatewayPhase.Gateway
                || !String.Equals(gateway.Message, GatewayState.Initial.Message,
                    StringComparison.Ordinal));
        if (showStatus)
        {
            TextBlock status = Text(PrimeRoutePresentation.GatewaySummary(
                gateway.Phase, gateway.Message), "prime-status-text");
            PrimeAccessibility.SetStatus(status, status.Text ?? String.Empty,
                gateway.Phase == GatewayPhase.Failed
                    ? PrimeStatusKind.Error : PrimeStatusKind.Info);
            content.Children.Add(status);
        }
        if (gateway.Phase == GatewayPhase.Failed
            && !String.IsNullOrWhiteSpace(gateway.Message))
        {
            content.Children.Add(new Expander
            {
                Header = "Details",
                Content = Text(PrimeRoutePresentation.GatewayDetails(gateway.Message),
                    "prime-muted")
            });
        }
        frameContent.Children.Add(content);
        Grid.SetColumn(content, 0);
        var side = BuildGatewaySidePanel();
        frameContent.Children.Add(side);
        Grid.SetColumn(side, 1);

        PrimeTechFrame frame = PrimeControlFactory.TechFrame(frameContent);
        frame.HorizontalAlignment = HorizontalAlignment.Center;
        frame.MaxWidth = 1080;
        root.Children.Add(frame);
        root.SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width > 0 && e.NewSize.Width < 860;
            frameContent.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,320");
            frameContent.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto");
            frameContent.ColumnSpacing = narrow ? 0 : 32;
            content.MaxWidth = 580;
            Grid.SetColumn(side, 0);
            Grid.SetRow(side, narrow ? 1 : 0);
            Grid.SetColumn(side, narrow ? 0 : 1);
            Grid.SetColumnSpan(side, narrow ? 1 : 1);
            Grid.SetRow(content, 0);
            side.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(0);
        };
        return root;
    }

    private static Control BuildGatewaySidePanel()
    {
        var side = PrimeControlFactory.Panel(Stack(
            Text("ACCOUNT SERVICE", "prime-kicker"),
            Text("Play together", "prime-heading"),
            Text("Sign in to keep your Hunter profile and official results with you.",
                "prime-body"),
            PrimeControlFactory.Divider(),
            Text("Online service", "prime-label"),
            Text("Account access is optional; ranked eligibility is checked after sign-in.",
                "prime-muted"),
            Text("Connection", "prime-label"),
            Text("No connection is opened until you choose an action.", "prime-muted"),
            Text("Version", "prime-label"),
            Text(BuildVersion.Display, "prime-muted")), secondary: true);
        side.Classes.Add("prime-gateway-side");
        PrimeAccessibility.SetName(side, "Project Prime account information");
        PrimeAccessibility.SetDescription(side,
            "Online service, connection, and version information.");
        return side;
    }

    private Control BuildGatewayLandingForm()
    {
        Action guest = () => RunCommand("Guest access", async () =>
            {
                if (await _gateway.UseGuestAsync(_lifetime.Token).ConfigureAwait(false))
                    PostUi(() => _shell.Navigator.NavigateRoot(PrimeRoute.Play));
            });
        Action signIn = () => ShowGatewayForm(GatewayForm.SignIn);
        Action createAccount = () => ShowGatewayForm(GatewayForm.Register);
        var content = Stack(
            MakeButton("Sign in", signIn, primary: true),
            MakeButton("Continue as guest", guest),
            MakeButton("Create account", createAccount, quiet: true));
        if (_captureMode)
        {
            content.Children.Add(LegacyButtonAlias("Play as Guest", guest));
            content.Children.Add(LegacyButtonAlias("Sign In", signIn));
            content.Children.Add(LegacyButtonAlias("Create Account", createAccount));
        }
        return content;
    }

    private Control BuildGatewaySignInForm()
    {
        var email = Input("", "Email address");
        var emailError = FieldError();
        var password = Input("", "Password");
        var passwordError = FieldError();
        password.PasswordChar = '•';
        AvaloniaButton? reveal = null;
        reveal = MakeButton("Show", () =>
        {
            bool revealed = !password.RevealPassword;
            password.RevealPassword = revealed;
            reveal!.Content = revealed ? "Hide" : "Show";
            PrimeAccessibility.SetName(reveal, revealed ? "Hide password" : "Show password");
        }, quiet: true);
        var passwordHost = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        passwordHost.Children.Add(password);
        passwordHost.Children.Add(reveal);
        Grid.SetColumn(reveal, 1);
        var validation = InlineValidation();
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        email.TextChanged += (_, _) => ClearFieldError(email, emailError);
        password.TextChanged += (_, _) => ClearFieldError(password, passwordError);
        AvaloniaButton? submit = null;
        submit = MakeButton("Sign in", () =>
        {
            string address = email.Text?.Trim() ?? "";
            string secret = password.Text ?? "";
            if (!ValidateCredentials(address, secret, email, emailError,
                password, passwordError, validation)) return;
            password.Text = "";
            submit!.IsEnabled = false;
            email.IsEnabled = false;
            password.IsEnabled = false;
            reveal!.IsEnabled = false;
            actions.IsEnabled = false;
            SetInlineStatus(validation, "Signing in…", PrimeStatusKind.Info);
            RunCommand("Sign in", async () =>
            {
                try
                {
                    if (await _gateway.SignInAsync(address, secret, _lifetime.Token)
                        .ConfigureAwait(false))
                        PostUi(() => _shell.Navigator.NavigateRoot(PrimeRoute.Play));
                }
                finally
                {
                    PostUi(() =>
                    {
                        if (submit != null) submit.IsEnabled = true;
                        email.IsEnabled = true;
                        password.IsEnabled = true;
                        reveal!.IsEnabled = true;
                        actions.IsEnabled = true;
                        if (validation.Text == "Signing in…")
                            validation.IsVisible = false;
                    });
                }
            });
        }, primary: true);
        actions.Children.Add(submit);
        actions.Children.Add(MakeButton("Resend Verification", () =>
        {
            string address = email.Text?.Trim() ?? "";
            if (!ValidateEmail(address, email, emailError, validation)) return;
            RunCommand("Resend verification", () =>
                _gateway.ResendForEmailAsync(address, _lifetime.Token));
        }));
        actions.Children.Add(MakeButton("Back", () => ShowGatewayForm(
            GatewayForm.Landing), quiet: true));
        return Stack(Text("Sign in", "prime-heading"),
            FormField("Email", email, emailError),
            FormField("Password", passwordHost, passwordError),
            validation, actions);
    }

    private Control BuildGatewayRegistrationForm()
    {
        var email = Input("", "Email address");
        var emailError = FieldError();
        var password = Input("", "Password");
        var passwordError = FieldError();
        password.PasswordChar = '•';
        AvaloniaButton? reveal = null;
        reveal = MakeButton("Show", () =>
        {
            bool revealed = !password.RevealPassword;
            password.RevealPassword = revealed;
            reveal!.Content = revealed ? "Hide" : "Show";
            PrimeAccessibility.SetName(reveal, revealed ? "Hide password" : "Show password");
        }, quiet: true);
        var passwordHost = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        passwordHost.Children.Add(password);
        passwordHost.Children.Add(reveal);
        Grid.SetColumn(reveal, 1);
        var displayName = Input(LauncherPrefs.PlayerName, "Display name");
        var displayNameError = FieldError();
        var validation = InlineValidation();
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        email.TextChanged += (_, _) => ClearFieldError(email, emailError);
        password.TextChanged += (_, _) => ClearFieldError(password, passwordError);
        displayName.TextChanged += (_, _) => ClearFieldError(displayName, displayNameError);
        AvaloniaButton? submit = null;
        Action submitRegistration = () =>
        {
            string address = email.Text?.Trim() ?? "";
            string secret = password.Text ?? "";
            string name = displayName.Text?.Trim() ?? "";
            if (!ValidateRegistration(address, secret, name, email, emailError,
                password, passwordError, displayName, displayNameError, validation)) return;
            GatewayForm submittedForm = _gatewayForm;
            password.Text = "";
            submit!.IsEnabled = false;
            email.IsEnabled = false;
            password.IsEnabled = false;
            displayName.IsEnabled = false;
            reveal!.IsEnabled = false;
            actions.IsEnabled = false;
            SetInlineStatus(validation, "Creating account…", PrimeStatusKind.Info);
            RunCommand("Register", async () =>
            {
                try
                {
                    AccountRegistration? result = await _gateway.RegisterAsync(address,
                        secret, name, _lifetime.Token).ConfigureAwait(false);
                    PostUi(() =>
                    {
                        // A delayed completion must not replace a form the player
                        // already left, or override an identity established by a
                        // newer operation.
                        if (_gatewayForm != submittedForm
                            || _shell.HasNetworkIdentity
                            || PrimeRoutePresentation.Normalize(_shell.CurrentRoute)
                                != PrimeRoute.Gateway)
                            return;
                        if (result is null)
                        {
                            // Stay on Register and rebuild only to surface the
                            // controller's sanitized error summary/details.
                            RenderRoute(PrimeRoute.Gateway);
                            return;
                        }
                        ShowGatewayForm(result.ConfirmationRequired
                            ? GatewayForm.Confirm : GatewayForm.SignIn);
                    });
                }
                finally
                {
                    PostUi(() =>
                    {
                        if (submit != null) submit.IsEnabled = true;
                        email.IsEnabled = true;
                        password.IsEnabled = true;
                        displayName.IsEnabled = true;
                        reveal!.IsEnabled = true;
                        actions.IsEnabled = true;
                        if (validation.Text == "Creating account…")
                            validation.IsVisible = false;
                    });
                }
            });
        };
        submit = MakeButton("Create account", submitRegistration, primary: true);
        actions.Children.Add(submit);
        actions.Children.Add(LegacyButtonAlias("Register", submitRegistration));
        actions.Children.Add(MakeButton("Resend Verification", () =>
        {
            string address = email.Text?.Trim() ?? "";
            if (!ValidateEmail(address, email, emailError, validation)) return;
            RunCommand("Resend verification", () =>
                _gateway.ResendForEmailAsync(address, _lifetime.Token));
        }));
        actions.Children.Add(MakeButton("Back", () => ShowGatewayForm(
            GatewayForm.Landing), quiet: true));
        return Stack(Text("Create account", "prime-heading"),
            FormField("Email", email, emailError),
            FormField("Password", passwordHost, passwordError),
            FormField("Display name", displayName, displayNameError),
            validation, actions);
    }

    private Control BuildGatewayConfirmationForm(PendingRegistration? pending)
    {
        var confirmation = Input("", "Confirmation code");
        var confirmationError = FieldError();
        var validation = InlineValidation();
        confirmation.TextChanged += (_, _) => ClearFieldError(confirmation,
            confirmationError);
        string maskedEmail = pending is not null
            ? MaskEmail(pending.Email)
            : "your email address";
        AvaloniaButton? confirm = null;
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        confirm = MakeButton("Confirm", () =>
        {
            string code = confirmation.Text?.Trim() ?? "";
            if (String.IsNullOrWhiteSpace(code))
            {
                ShowFieldError(confirmation, confirmationError,
                    "Enter the confirmation code.");
                return;
            }
            ClearFieldError(confirmation, confirmationError);
            confirmation.IsEnabled = false;
            confirm!.IsEnabled = false;
            actions.IsEnabled = false;
            SetInlineStatus(validation, "Confirming your email…", PrimeStatusKind.Info);
            RunCommand("Confirm email", async () =>
            {
                try
                {
                    if (await _gateway.ConfirmPendingAsync(code, _lifetime.Token)
                        .ConfigureAwait(false))
                        PostUi(() => ShowGatewayForm(GatewayForm.SignIn));
                }
                finally
                {
                    PostUi(() =>
                    {
                        confirmation.IsEnabled = true;
                        confirm!.IsEnabled = true;
                        actions.IsEnabled = true;
                        if (validation.Text == "Confirming your email…")
                            validation.IsVisible = false;
                    });
                }
            });
        }, primary: true);
        actions.Children.Add(confirm);
        actions.Children.Add(MakeButton("Resend Code", () =>
            RunCommand("Resend confirmation", () =>
                _gateway.ResendPendingAsync(_lifetime.Token))));
        return Stack(Text("CHECK YOUR EMAIL", "prime-heading"),
            Text("We sent a confirmation code to:", "prime-muted"),
            Text(maskedEmail, "prime-body"),
            FormField("Confirmation code", confirmation, confirmationError),
            validation, actions);
    }

    private void ShowGatewayForm(GatewayForm form)
    {
        if (_disposed) return;
        _gatewayForm = form;
        if (PrimeRoutePresentation.Normalize(_shell.CurrentRoute) == PrimeRoute.Gateway)
            RenderRoute(PrimeRoute.Gateway);
    }

    internal static string MaskEmail(string email)
    {
        string value = email?.Trim() ?? "";
        int separator = value.IndexOf('@');
        if (separator <= 0 || separator == value.Length - 1)
            return "your email address";
        return $"{value[0]}•••••{value[separator..]}";
    }

    private Control BuildGameFilesPage()
    {
        var status = Text(GameFiles.Problem()
            ?? "Game files are ready. You can re-select your cartridge dump if repair is needed.",
            "prime-muted");
        var progressRow = new ProgressRow();
        var root = Stack(
            Text("Metroid Prime Hunters game files", "prime-heading"),
            Text("Project Prime needs your own cartridge dump. It extracts only the local files the game needs; the ROM is not uploaded or bundled.", "prime-body"),
            status,
            progressRow,
            MakeButton(GameFiles.Ready ? "Repair from .nds file" : "Choose your .nds file",
                () => RunCommand("Set up game files",
                    () => ChooseGameFilesAsync(status, progressRow)),
                primary: true));
        root.Children.Add(MakeButton("Back", () => RenderRoute(_shell.CurrentRoute), quiet: true));
        return Card(root);
    }

    private async Task ChooseGameFilesAsync(TextBlock status, ProgressRow progressRow)
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top == null) throw new InvalidOperationException("The file picker is unavailable.");
        var options = new FilePickerOpenOptions
        {
            Title = "Your Metroid Prime Hunters cartridge dump",
            AllowMultiple = false
        };
        if (!OperatingSystem.IsAndroid())
        {
            options.FileTypeFilter = new[]
            {
                new FilePickerFileType("Nintendo DS ROM") { Patterns = new[] { "*.nds" } },
                new FilePickerFileType("Every file") { Patterns = new[] { "*" } }
            };
        }
        IReadOnlyList<IStorageFile> picked = await top.StorageProvider.OpenFilePickerAsync(options);
        if (picked.Count == 0) return;

        progressRow.Set(0, "Starting");
        var progress = new SetupProgressUpdateQueue(status.Text ?? "", PostUi,
            (text, fraction, stage) =>
            {
                status.Text = text;
                progressRow.Set(fraction, stage);
            });

        string? path = picked[0].TryGetLocalPath();
        string? scratch = null;
        try
        {
            if (path == null)
            {
                Directory.CreateDirectory(GameFiles.Root);
                scratch = Path.Combine(GameFiles.Root, "picked-prime-setup.nds");
                status.Text = "Copying the selected file into the app's private storage…";
                await using Stream source = await picked[0].OpenReadAsync();
                await using var target = File.Create(scratch);
                await source.CopyToAsync(target, _lifetime.Token).ConfigureAwait(false);
                path = scratch;
            }
            bool ready = await Task.Run(() => GameFiles.RunSetup(path, progress.Report),
                _lifetime.Token)
                .ConfigureAwait(false);
            if (!ready) throw new InvalidOperationException(GameFiles.Problem() ?? "Game-file setup did not finish.");
            GameFiles.ApplyPaths();
            string[] rooms = ThumbnailGenerator.MultiplayerRooms().ToArray();
            _rooms.Clear();
            _rooms.AddRange(rooms);
            _play.SetMaps(rooms);
            if (ThumbnailHost.CanRender)
            {
                await ThumbnailHost.RenderMissingAsync(progress.Report).ConfigureAwait(false);
            }
            progress.Finish(ok: true, "Game files are ready.");
            PostUi(() =>
            {
                status.Text = "Game files are ready.";
                RenderRoute(_shell.CurrentRoute);
            });
        }
        catch
        {
            progress.Finish(ok: false, "Setup did not finish.");
            throw;
        }
        finally
        {
            if (scratch != null)
            {
                try { File.Delete(scratch); }
                catch (IOException) { }
            }
        }
    }

    private Control BuildPlayPage()
    {
        PlayState state = _capturePlayState ?? _play.State;
        return state.Lobby is { } lobby
            ? BuildActiveLobbyPage(state, lobby)
            : BuildPlayPresentation(state);
    }

    private Control BuildPlayPresentation(PlayState state)
    {
        return PlayPresentation.Build(CreatePlayPresentationContext(state));
    }

    private PlayPresentationContext CreatePlayPresentationContext(PlayState state)
        => new(
            _shell,
            _play,
            state,
            _rooms,
            _playPresentation,
            _lifetime.Token,
            RunCommand,
            PostUi,
            () => RenderRoute(PrimeRoute.Play),
            () => Navigate(PrimeRoute.Gateway),
            BuildPreviewStage,
            HostMatchAsync,
            ConfigureMatchAsync,
            TrackHunterSelection,
            () => PlayEditorLostFocus(null, new RoutedEventArgs()),
            _shell.Notify,
            ExpandAdvancedNetwork: _captureExpandAdvancedNetwork,
            OpenNetworkSettings: OpenNetworkSettings,
            SeatOffersHandledExternally: true,
            TrackChatPanel: panel => _activeLobbyChatPanel = panel);

    private bool TryUpdatePlayHomePresence()
        => PageHost.Content is Control content
            && PlayPresentation.TryUpdateHomePresence(content,
                CreatePlayPresentationContext(_capturePlayState ?? _play.State));

    internal bool RefreshPlayHomePresenceForTest(
        PresencePresentationState? presence = null)
    {
        if (!_captureMode)
            throw new InvalidOperationException(
                "The presence refresh test seam is available only in capture mode.");
        if (presence != null) _play.SetPresenceForCapture(presence);
        return TryUpdatePlayHomePresence();
    }

    private void TrackHunterSelection(ComboBox combo,
        DeferredControllerSelection<Hunter> selection)
    {
        _hunterPadCombo = combo;
        _hunterPadSelection = selection;
    }

    private async Task HostMatchAsync(HostMatchDraft draft)
    {
        if (!draft.TryBuildRules(out LobbyRulesOptions rules, out string error))
            throw new InvalidOperationException(error);
        await _play.HostLobbyAsync(draft.Name, draft.PlayerLimit, draft.ObserverLimit,
            draft.SeatPolicy, _lifetime.Token).ConfigureAwait(false);

        // HostLobbyAsync waits for the Node response. The short bounded wait
        // below only bridges the event publication to PlayState; it never
        // creates a local lobby or treats the draft as authoritative.
        LobbySnapshot? lobby = _play.State.Lobby;
        for (int attempt = 0; lobby == null && attempt < 100; attempt++)
        {
            await Task.Delay(10, _lifetime.Token).ConfigureAwait(false);
            lobby = _play.State.Lobby;
        }
        if (lobby == null)
            throw new InvalidOperationException(
                "The server created the lobby but did not return its current state.");

        string mapKey = draft.MapKey;
        if (string.IsNullOrWhiteSpace(mapKey))
            mapKey = _play.AvailableMaps.FirstOrDefault() ?? "";
        await _play.ConfigureLobbyAsync(mapKey, draft.Mode, draft.BotCount,
            draft.BotDifficulty, rules, _lifetime.Token).ConfigureAwait(false);
        PostUi(() =>
        {
            _playPresentation.Subsection = PlaySubsection.Home;
            _playPresentation.ClearEdit();
            if (!_disposed && _shell.CurrentRoute == PrimeRoute.Play)
                RenderRoute(PrimeRoute.Play);
        });
    }

    private async Task ConfigureMatchAsync(HostMatchDraft draft)
    {
        if (!draft.TryBuildRules(out LobbyRulesOptions rules, out string error))
            throw new InvalidOperationException(error);
        await _play.ConfigureLobbyAsync(draft.MapKey, draft.Mode, draft.BotCount,
            draft.BotDifficulty, rules, _lifetime.Token).ConfigureAwait(false);
        PostUi(() =>
        {
            _playPresentation.ClearEdit();
            if (!_disposed && _shell.CurrentRoute == PrimeRoute.Play)
                RenderRoute(PrimeRoute.Play);
        });
    }

    private Control BuildActiveLobbyPage(PlayState state, LobbySnapshot lobby)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        return BuildPlayPresentation(state);
    }

    internal static bool TryParseLobbyPointGoal(string? text, out int? pointGoal)
    {
        string value = text?.Trim() ?? "";
        if (value.Length == 0 || value.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            pointGoal = null;
            return true;
        }
        if (Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            && parsed is >= 1 and <= ushort.MaxValue)
        {
            pointGoal = parsed;
            return true;
        }
        pointGoal = null;
        return false;
    }

    internal static bool TryParseLobbyTimeLimit(string? text, out int? seconds)
    {
        string value = text?.Trim() ?? "";
        if (value.Length == 0 || value.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            seconds = null;
            return true;
        }
        if (Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
            out int rawSeconds))
        {
            seconds = rawSeconds is >= 1 and <= 3600 ? rawSeconds : null;
            return seconds.HasValue;
        }
        string[] parts = value.Split(':');
        if (parts.Length == 2
            && Int32.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture,
                out int minutes)
            && Int32.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture,
                out int remainder)
            && minutes >= 0 && remainder is >= 0 and < 60)
        {
            int total = minutes * 60 + remainder;
            seconds = total is >= 1 and <= 3600 ? total : null;
            return seconds.HasValue;
        }
        seconds = null;
        return false;
    }

    private Control BuildHunterPage()
    {
        RefreshModelPreviewIdentity();
        HunterLicensePageState state = _captureLicenseState ?? _license.State;
        IReadOnlyList<HunterDossier> hunters = _captureLicenseHunters ?? _license.Hunters;
        bool canLoadMore = _captureLicenseState is null
            ? _license.CanLoadMoreMatches
            : state.NextHistoryCursor.HasValue;
        return HunterPresentation.Build(new HunterPresentationContext(
            _shell.SignedIn,
            _routeViewState.HunterSection,
            state,
            hunters,
            canLoadMore,
            SelectHunterSection,
            () => Navigate(PrimeRoute.Gateway),
            RunCommand,
            () => _license.LoadMatchesAsync(older: true, _lifetime.Token),
            () => _license.LoadMatchesAsync(cancellationToken: _lifetime.Token),
            () => BuildArmoryPage(includeHeading: false),
            BuildHunterRosterPage,
            BuildLicenseOverview));
    }

    private void SelectHunterSection(HunterSection section)
    {
        _routeViewState.SelectHunterSection(section);
        if (!_shell.SignedIn && HunterPresentation.RequiresAccount(section))
        {
            RenderRoute(PrimeRoute.Hunter);
            return;
        }
        switch (section)
        {
            case HunterSection.Overview:
                RunCommand("Load Hunter overview", LoadLicenseOverviewAsync);
                break;
            case HunterSection.Hunters:
                RunCommand("Load Hunters", () => _license.LoadHuntersAsync(_lifetime.Token));
                break;
            case HunterSection.Arsenal:
                RenderRoute(PrimeRoute.Hunter);
                break;
            case HunterSection.Career:
                RunCommand("Load career", () => _license.LoadCareerAsync(_lifetime.Token));
                break;
            case HunterSection.Matches:
                RunCommand("Load matches", () => _license.LoadMatchesAsync(
                    cancellationToken: _lifetime.Token));
                break;
        }
    }

    private Control BuildHunterRosterPage(IReadOnlyList<HunterDossier> hunters)
    {
        if (hunters.Count == 0)
            return PrimeControlFactory.SectionPanel(new PrimeEmptyState("No hunters are available in this license."));

        HunterDossier selected = hunters.FirstOrDefault(dossier => dossier.Hunter == _selectedLicenseHunter)
            ?? hunters[0];
        _appearance.SelectHunter(selected.Hunter);
        StartHunterPreview(selected.Hunter, _appearance.State.Preview.SkinKey);

        var roster = Stack(Text("Hunters", "prime-heading"),
            Text("Select a hunter to inspect their profile and affinity weapon.", "prime-muted"));
        foreach (HunterDossier dossier in hunters)
        {
            HunterDossier captured = dossier;
            var row = Stack(Text(dossier.Name, "prime-body"),
                Text($"Affinity weapon · {dossier.AffinityWeapon}", "prime-muted"));
            AddHunterBadges(row, dossier);
            AvaloniaButton button = MakeButton("", () =>
            {
                _selectedLicenseHunter = captured.Hunter;
                RenderRoute(PrimeRoute.Hunter);
            }, quiet: true);
            button.Content = row;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            PrimeAccessibility.SetName(button, $"Select Hunter {dossier.Name}");
            PrimeSelectedRow selectedRow = PrimeControlFactory.SelectedRow(button,
                dossier.Hunter == selected.Hunter);
            selectedRow.Classes.Add("prime-hunter-row");
            if (dossier.Hunter == selected.Hunter)
                selectedRow.Classes.Add("prime-hunter-selected");
            roster.Children.Add(selectedRow);
        }

        var preview = Stack(Text("Preview", "prime-heading"),
            BuildHunterPreviewStage(selected.Hunter));
        var detail = Stack(HunterOverviewPresentation.BuildIdentityHeader(selected),
            Text($"Affinity weapon · {selected.AffinityWeapon}", "prime-body"));
        AddHunterBadges(detail, selected);
        detail.Children.Add(BuildHunterAppearance(selected.Hunter));
        detail.Children.Add(PrimeControlFactory.Divider());
        detail.Children.Add(Text("Profile favorite", "prime-label"));
        if (!selected.IsFavorite)
            detail.Children.Add(MakeButton("Set as favorite", () => RunCommand(
                "Update favorite Hunter", () => _license.SetFavoriteHunterAsync(
                    selected.Hunter, _lifetime.Token)), primary: true));
        else detail.Children.Add(new PrimeStatusChip("Favorite"));
        detail.Children.Add(Text("The selected hunter is used only for this profile action; lobby hunter selection remains on Play.", "prime-muted"));

        return ResponsiveThreeColumn(PrimeControlFactory.SectionPanel(roster),
            PrimeControlFactory.SectionPanel(preview), PrimeControlFactory.SectionPanel(detail));
    }

    private Control BuildLicenseOverview(HunterLicensePageState state,
        IReadOnlyList<HunterDossier> hunters)
    {
        HunterDossier profile = HunterOverviewPresentation.SelectProfile(hunters,
            _selectedLicenseHunter);
        _appearance.SelectHunter(profile.Hunter);
        StartHunterPreview(profile.Hunter, _appearance.State.Preview.SkinKey);
        return HunterOverviewPresentation.BuildSummary(state, hunters,
            _selectedLicenseHunter, BuildHunterPreviewStage,
            name => RunCommand("Update display name",
                () => _license.UpdateDisplayNameAsync(name, _lifetime.Token)));
    }

    private Control BuildHunterPreviewStage(Hunter hunter)
    {
        Control content;
        bool compactFailure = false;
        bool deathStage = _hunterDeathPreviewKeys.ContainsKey(hunter);
        if (_hunterPreviewPaths.TryGetValue(hunter, out string? path))
        {
            Control image = new PrimeLocalImage(path, 330) { Stretch = Stretch.Uniform };
            content = deathStage
                ? Stack(image, Text("Death-stage still · isolated cosmetic preview",
                    "prime-muted"))
                : image;
        }
        else if (_hunterPreviewRetryAfter.ContainsKey(hunter))
        {
            compactFailure = true;
            content = Stack(HunterPreviewFallback.ForHunter(hunter),
                LegacyCaptureMarker("Preview unavailable."),
                MakeButton("Retry preview", () => RetryHunterPreview(hunter)));
        }
        else
        {
            content = Stack(HunterPreviewFallback.ForHunter(hunter),
                _captureMode
                    ? LegacyCaptureMarker("Preview omitted for offline capture.")
                    : Text(deathStage
                        ? "Preparing isolated death-stage still…"
                        : "Preparing hunter preview…", "prime-muted"));
        }
        if (content is TextBlock text)
        {
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
        }
        var stage = PrimeControlFactory.PreviewStage(content);
        stage.Classes.Add("prime-hunter-preview");
        stage.Height = HunterPreviewHeight(compactFailure);
        return stage;
    }

    private Control BuildHunterAppearance(Hunter hunter)
    {
        _appearance.SelectHunter(hunter);
        void Select(Action selection)
        {
            selection();
            _hunterDeathPreviewKeys.Remove(hunter);
            _hunterPreviewRequestKeys.Remove(hunter);
            _hunterPreviewPaths.Remove(hunter);
            _hunterPreviewLoads.Remove(hunter);
            _hunterPreviewRetryAfter.Remove(hunter);
            RenderRoute(PrimeRoute.Hunter);
        }
        return HunterAppearancePresentation.Build(new HunterAppearancePresentationContext(
            _appearance.State,
            () => Select(_appearance.SelectPreviousSkin),
            () => Select(_appearance.SelectNextSkin),
            () => Select(_appearance.SelectPreviousArmorEffect),
            () => Select(_appearance.SelectNextArmorEffect),
            () => Select(_appearance.SelectPreviousDeathEffect),
            () => Select(_appearance.SelectNextDeathEffect),
            _appearance.State.Preview.DeathEffectKey
                != CosmeticKeys.DefaultDeathEffect,
            () => StartHunterDeathPreview(hunter,
                _appearance.State.Preview.SkinKey,
                _appearance.State.Preview.ArmorEffectKey,
                _appearance.State.Preview.DeathEffectKey),
            () =>
            {
                CosmeticLoadout selected = _appearance.State.Preview;
                RunCommand("Equip appearance", () => EquipAppearanceAsync(hunter, selected));
            },
            () => Select(_appearance.ResetPreview)));
    }

    private async Task EquipAppearanceAsync(Hunter hunter, CosmeticLoadout loadout)
    {
        if (!CosmeticCatalog.BuiltIn.TryResolve(loadout, hunter, out _, out _))
            throw new InvalidOperationException("The selected appearance is no longer available.");

        AuthenticatedCosmeticOperation? operation =
            CaptureAuthenticatedCosmeticOperation();
        if (operation is { } authenticated)
        {
            // A signed-in account is authoritative. Failure stops here and is
            // never converted into an implicit guest write.
            await authenticated.Account.PutCosmeticsAsync(hunter, loadout,
                _lifetime.Token).ConfigureAwait(false);
            if (!IsAccountCurrent(authenticated))
            {
                DebugLog.Line("cosmetics/skin",
                    "Discarded a stale authenticated appearance update.");
                return;
            }
        }
        else if (_shell.SignedIn)
        {
            // Shell/account identity convergence is a prerequisite for an
            // authenticated write. Never fall through to guest persistence.
            throw new InvalidOperationException(
                "The signed-in appearance identity changed. Try again.");
        }
        else if (_appearance.UsesAccountStorage)
        {
            throw new InvalidOperationException(
                "The appearance identity changed. Try again.");
        }

        PostUi(() =>
        {
            if (operation is { } expected
                && (!IsAccountCurrent(expected)
                    || _appearance.AccountPrincipal != expected.Principal)) return;
            if (_appearance.State.Hunter != hunter
                || _appearance.State.Preview != loadout) return;
            _appearance.Equip();
            TrySynchronizeLobbyCosmetics();
            RenderRoute(PrimeRoute.Hunter);
        });
    }

    private async Task LoadAccountCosmeticsAsync()
    {
        AuthenticatedCosmeticOperation? operation =
            CaptureAuthenticatedCosmeticOperation();
        if (operation is not { } authenticated) return;
        IReadOnlyList<AccountCosmeticLoadout> loadouts = await authenticated.Account
            .GetCosmeticsAsync(_lifetime.Token).ConfigureAwait(false);
        if (!IsAccountCurrent(authenticated))
        {
            DebugLog.Line("cosmetics/skin",
                "Discarded a stale authenticated appearance load.");
            return;
        }
        PostUi(() =>
        {
            if (!IsAccountCurrent(authenticated)
                || _appearance.AccountPrincipal != authenticated.Principal) return;
            foreach (AccountCosmeticLoadout entry in loadouts)
                _appearance.ApplyAuthoritative(entry.Hunter, entry.Loadout);
            _accountCosmeticsLoaded = true;
            TrySynchronizeLobbyCosmetics();
            if (_shell.CurrentRoute == PrimeRoute.Hunter)
                RenderRoute(PrimeRoute.Hunter);
        });
    }

    private AuthenticatedCosmeticOperation? CaptureAuthenticatedCosmeticOperation()
    {
        AccountSession? account = AccountSessions.Current;
        PlayerId? principal = account?.Identity?.PlayerId;
        if (account is not { IsSignedIn: true } || principal == null
            || !_shell.SignedIn || _shell.PlayerId != principal)
        {
            return null;
        }
        return new AuthenticatedCosmeticOperation(account, principal.Value,
            _shell.IdentityGeneration);
    }

    private bool IsAccountCurrent(AuthenticatedCosmeticOperation operation)
        => operation.IsAccountCurrent(AccountSessions.Current, _shell.PlayerId,
            _shell.IdentityGeneration);

    private void TrySynchronizeLobbyCosmetics()
    {
        if (_captureMode || _disposed || _lifetime.IsCancellationRequested)
            return;
        if (AccountSessions.Current is { IsSignedIn: true }
            && !_accountCosmeticsLoaded)
            return;
        NodeControlClient.ViewState? node = _play.State.Node;
        LobbySnapshot? lobby = node?.Lobby;
        Guid? sessionId = node?.Session?.SessionId;
        if (lobby == null || sessionId == null
            || lobby.Phase is not (LobbyPhase.Open or LobbyPhase.InMatch
                or LobbyPhase.PostMatch))
        {
            ResetCosmeticSyncRetry();
            return;
        }
        LobbyMember? member = lobby.Members.FirstOrDefault(value =>
            value.SessionId == sessionId.Value);
        if (member == null || member.Observer
            || member.Hunter != _play.State.LobbyHunter)
        {
            ResetCosmeticSyncRetry();
            return;
        }
        CosmeticLoadout loadout = _appearance.GetEquipped(member.Hunter);
        if (!CosmeticCatalog.BuiltIn.TryResolve(loadout, member.Hunter,
                out CosmeticLoadoutIds ids, out _)
            || member.Cosmetics == ids)
        {
            ResetCosmeticSyncRetry();
            return;
        }
        var attempt = new CosmeticSyncAttempt(lobby.LobbyId, lobby.Revision, ids);
        if (_cosmeticSyncInFlight)
        {
            _cosmeticSyncDirty = true;
            return;
        }
        if (!_cosmeticSyncRetries.CanStart(attempt)) return;
        CancelCosmeticSyncRetryDelay();
        _cosmeticSyncInFlight = true;
        _cosmeticSyncDirty = false;
        _ = SynchronizeLobbyCosmeticsAsync(attempt);
    }

    private async Task SynchronizeLobbyCosmeticsAsync(CosmeticSyncAttempt attempt)
    {
        bool succeeded = false;
        bool canceled = false;
        string? failure = null;
        try
        {
            await _play.SelectLobbyCosmeticsAsync(attempt.Ids, _lifetime.Token)
                .ConfigureAwait(false);
            succeeded = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            canceled = true;
        }
        catch (Exception error)
        {
            failure = PrimeRoutePresentation.PlayerFacingNetworkError(error.Message,
                "Could not synchronize the equipped appearance.");
        }
        finally
        {
            PostUi(() =>
            {
                _cosmeticSyncInFlight = false;
                if (succeeded || canceled)
                {
                    ResetCosmeticSyncRetry();
                }
                else
                {
                    TimeSpan? retryAfter = _cosmeticSyncRetries.RecordFailure(attempt);
                    if (retryAfter is { } delay)
                        ScheduleCosmeticSyncRetry(attempt, delay);
                    else
                        _shell.NotifyTransient("appearance-sync",
                            PrimeNotificationKind.Warning, failure!);
                }
                if (_cosmeticSyncDirty)
                {
                    _cosmeticSyncDirty = false;
                    TrySynchronizeLobbyCosmetics();
                }
            });
        }
    }

    private void ScheduleCosmeticSyncRetry(CosmeticSyncAttempt attempt, TimeSpan delay)
    {
        CancelCosmeticSyncRetryDelay();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _cosmeticSyncRetryDelay = cancellation;
        _ = WaitForCosmeticSyncRetryAsync(attempt, delay, cancellation);
    }

    private async Task WaitForCosmeticSyncRetryAsync(CosmeticSyncAttempt attempt,
        TimeSpan delay, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(delay, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        PostUi(() =>
        {
            if (!ReferenceEquals(_cosmeticSyncRetryDelay, cancellation)) return;
            _cosmeticSyncRetryDelay = null;
            cancellation.Dispose();
            if (_cosmeticSyncRetries.ArmRetry(attempt))
                TrySynchronizeLobbyCosmetics();
        });
    }

    private void ResetCosmeticSyncRetry()
    {
        _cosmeticSyncRetries.Reset();
        CancelCosmeticSyncRetryDelay();
    }

    private void CancelCosmeticSyncRetryDelay()
    {
        CancellationTokenSource? cancellation = _cosmeticSyncRetryDelay;
        _cosmeticSyncRetryDelay = null;
        if (cancellation == null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    internal static double HunterPreviewHeight(bool compactFailure)
        => compactFailure ? 180 : 360;

    private static void AddHunterBadges(StackPanel target, HunterDossier dossier)
    {
        if (!dossier.IsFavorite && !dossier.IsMostPlayed && !dossier.IsBest) return;
        var badges = new WrapPanel { Orientation = Orientation.Horizontal };
        if (dossier.IsFavorite) badges.Children.Add(new PrimeStatusChip("Favorite"));
        if (dossier.IsMostPlayed) badges.Children.Add(new PrimeStatusChip("Most played"));
        if (dossier.IsBest) badges.Children.Add(new PrimeStatusChip("Best"));
        target.Children.Add(badges);
    }

    private async Task LoadLicenseOverviewAsync()
    {
        await _license.LoadOverviewAsync(_lifetime.Token).ConfigureAwait(false);
        if (!_lifetime.IsCancellationRequested)
            await _license.LoadRecentMatchesAsync(_lifetime.Token).ConfigureAwait(false);
    }

    private async Task RefreshLicenseOverviewAsync()
    {
        _license.Invalidate();
        await LoadLicenseOverviewAsync().ConfigureAwait(false);
    }

    private Control BuildArmoryPage(bool includeHeading = true)
    {
        RefreshModelPreviewIdentity();
        IReadOnlyList<PrimeWeaponDetails> weapons = _armory.Weapons;
        if (weapons.Count == 0)
            return includeHeading
                ? Stack(PrimeControlFactory.PageHeading("Arsenal", "WEAPON REGISTRY",
                    "No weapon metadata is available in the installed game data."))
                : PrimeControlFactory.SectionPanel(new PrimeEmptyState(
                    "No weapon metadata is available in the installed game data."));
        PrimeWeaponDetails selected = weapons.FirstOrDefault(weapon => weapon.Beam == _selectedArmoryWeapon)
            ?? weapons[0];
        _selectedArmoryWeapon = selected.Beam;
        StartWeaponPreview(selected.Beam);

        var root = Stack();
        if (includeHeading)
            root.Children.Add(PrimeControlFactory.PageHeading("Arsenal", "WEAPON REGISTRY",
                "Review each weapon's combat profile, affinity, and projectile behavior."));
        else
            root.Children.Add(Text("Canonical combat profiles from installed game data.", "prime-muted"));
        var roster = Stack(Text("Weapons", "prime-heading"));
        foreach (PrimeWeaponDetails weapon in weapons)
        {
            PrimeWeaponDetails captured = weapon;
            var row = Stack(Text(weapon.Name, "prime-body"),
                Text($"Affinity · {weapon.AffinityHunters}", "prime-muted"));
            var stats = Text($"{weapon.UnchargedDamage}/{weapon.ChargedDamage} damage · {weapon.AmmoCost} ammo", "prime-muted");
            row.Children.Add(stats);
            AvaloniaButton button = MakeButton("", () =>
            {
                _selectedArmoryWeapon = captured.Beam;
                RenderRoute(PrimeRoute.Hunter);
            }, quiet: true);
            button.Content = row;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            PrimeAccessibility.SetName(button, $"Select weapon {weapon.Name}");
            PrimeSelectedRow selectedRow = PrimeControlFactory.SelectedRow(button,
                weapon.Beam == selected.Beam);
            selectedRow.Classes.Add("prime-weapon-row");
            if (weapon.Beam == selected.Beam)
                selectedRow.Classes.Add("prime-weapon-selected");
            roster.Children.Add(selectedRow);
        }

        var preview = Stack(Text("Preview", "prime-heading"), BuildWeaponPreviewStage(selected.Beam));
        var detail = Stack(Text(selected.Name, "prime-hero"),
            Text(selected.Description, "prime-body"),
            Text($"Affinity hunters · {selected.AffinityHunters}", "prime-muted"));
        detail.Children.Add(PrimeControlFactory.Divider());
        detail.Children.Add(BuildWeaponStatGrid(selected));
        root.Children.Add(ResponsiveThreeColumn(PrimeControlFactory.SectionPanel(roster),
            PrimeControlFactory.SectionPanel(preview), PrimeControlFactory.SectionPanel(detail)));
        return root;
    }

    private static Control BuildWeaponStatGrid(PrimeWeaponDetails weapon)
    {
        var stats = new StackPanel { Spacing = 0 };
        stats.Classes.Add("prime-weapon-stat-grid");
        AddWeaponStat(stats, "Uncharged damage",
            $"{weapon.UnchargedDamage.ToString(CultureInfo.InvariantCulture)} damage");
        AddWeaponStat(stats, "Charged damage",
            $"{weapon.ChargedDamage.ToString(CultureInfo.InvariantCulture)} damage");
        AddWeaponStat(stats, "Ammo cost",
            $"{weapon.AmmoCost.ToString(CultureInfo.InvariantCulture)} per shot");
        AddWeaponStat(stats, "Projectiles",
            weapon.ChargedProjectiles.ToString(CultureInfo.InvariantCulture));
        AddWeaponStat(stats, "Projectile speed",
            weapon.ChargedSpeed.ToString(CultureInfo.InvariantCulture));
        AddWeaponStat(stats, "Affinity", weapon.AffinityHunters);
        return stats;
    }

    private static void AddWeaponStat(StackPanel stats, string label, string value)
    {
        if (stats.Children.Count > 0)
            stats.Children.Add(PrimeControlFactory.Divider());
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            MinHeight = 34,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(Text(label, "prime-label"));
        TextBlock valueText = Text(value, "prime-body");
        valueText.HorizontalAlignment = HorizontalAlignment.Right;
        row.Children.Add(valueText);
        Grid.SetColumn(valueText, 1);
        stats.Children.Add(row);
    }

    private Control BuildWeaponPreviewStage(BeamType beam)
    {
        Control content;
        if (_weaponPreviewPaths.TryGetValue(beam, out string? path))
        {
            content = new PrimeLocalImage(path, 330) { Stretch = Stretch.Uniform };
        }
        else if (_weaponPreviewRetryAfter.ContainsKey(beam))
        {
            PrimeWeaponDetails? weapon = _armory.Weapons.FirstOrDefault(
                candidate => candidate.Beam == beam);
            content = weapon is { } selected
                ? Stack(HunterPreviewFallback.ForWeapon(selected),
                    LegacyCaptureMarker("Preview unavailable."),
                    MakeButton("Retry preview", () => RetryWeaponPreview(beam)))
                : Stack(Text("Weapon preview is unavailable.", "prime-muted"),
                    MakeButton("Retry preview", () => RetryWeaponPreview(beam)));
        }
        else
        {
            PrimeWeaponDetails? weapon = _armory.Weapons.FirstOrDefault(
                candidate => candidate.Beam == beam);
            content = weapon is { } selected
                ? Stack(HunterPreviewFallback.ForWeapon(selected),
                    _captureMode
                        ? LegacyCaptureMarker("Preview omitted for offline capture.")
                        : Text("Preparing weapon preview…", "prime-muted"))
                : Text("Weapon preview is unavailable.", "prime-muted");
        }
        if (content is TextBlock text)
        {
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
        }
        var stage = PrimeControlFactory.PreviewStage(content);
        stage.Height = 360;
        return stage;
    }

    private static TextBlock LegacyCaptureMarker(string text)
        => new()
        {
            Text = text,
            IsVisible = false,
            Classes = { "prime-muted" }
        };

    private void StartHunterPreview(Hunter hunter, string? skinKey = null)
    {
        _hunterDeathPreviewKeys.TryGetValue(hunter, out string? deathEffectKey);
        string? armorEffectKey = _appearance.State.Hunter == hunter
            ? _appearance.State.Preview.ArmorEffectKey : null;
        StartHunterPreview(hunter, skinKey, deathEffectKey, armorEffectKey);
    }

    private void StartHunterDeathPreview(Hunter hunter, string? skinKey,
        string? armorEffectKey, string deathEffectKey)
    {
        if (String.Equals(deathEffectKey, CosmeticKeys.DefaultDeathEffect,
                StringComparison.Ordinal))
            return;
        _hunterDeathPreviewKeys[hunter] = deathEffectKey;
        _hunterPreviewPaths.Remove(hunter);
        _hunterPreviewLoads.Remove(hunter);
        _hunterPreviewRetryAfter.Remove(hunter);
        StartHunterPreview(hunter, skinKey, deathEffectKey, armorEffectKey);
        RenderRoute(PrimeRoute.Hunter);
    }

    private void StartHunterPreview(Hunter hunter, string? skinKey,
        string? deathEffectKey, string? armorEffectKey = null)
    {
        if (_captureMode)
        {
            // Capture must never invoke ModelPreviewGenerator: its managed
            // worker is a separate process and requires the game-file setup
            // gate that the offline fixture intentionally bypasses. Keep an
            // explicit unavailable marker for every capture. The
            // failure fixture additionally exposes the production retry
            // affordance; other fixtures render a deterministic omission
            // rather than implying that a worker is still running.
            if (_captureHunterPreviewFailure)
                _hunterPreviewRetryAfter[hunter] = DateTimeOffset.MaxValue;
            return;
        }
        RefreshModelPreviewIdentity();
        string requestKey = $"{skinKey ?? CosmeticKeys.DefaultSkin(hunter)}|"
            + (armorEffectKey ?? CosmeticKeys.NoArmorEffect) + "|"
            + (deathEffectKey ?? "alive");
        if (_hunterPreviewRequestKeys.TryGetValue(hunter, out string? currentRequest)
            && !String.Equals(currentRequest, requestKey, StringComparison.Ordinal))
        {
            _hunterPreviewPaths.Remove(hunter);
            _hunterPreviewLoads.Remove(hunter);
            _hunterPreviewRetryAfter.Remove(hunter);
        }
        _hunterPreviewRequestKeys[hunter] = requestKey;
        if (_hunterPreviewPaths.ContainsKey(hunter) || !_hunterPreviewLoads.Add(hunter)) return;
        if (_hunterPreviewRetryAfter.TryGetValue(hunter, out DateTimeOffset retryAfter))
        {
            _hunterPreviewLoads.Remove(hunter);
            if (retryAfter > DateTimeOffset.UtcNow) return;
            _hunterPreviewRetryAfter.Remove(hunter);
        }
        _hunterPreviewLoadStarts++;
        _ = LoadHunterPreviewAsync(hunter, skinKey, armorEffectKey,
            deathEffectKey, requestKey,
            _modelPreviewIdentity);
    }

    private async Task LoadHunterPreviewAsync(Hunter hunter, string? skinKey,
        string? armorEffectKey, string? deathEffectKey, string requestKey,
        string? identity)
    {
        string? path = null;
        try
        {
            PrimePreviewImage? image = deathEffectKey == null
                ? await _hunterPreviews.LoadAsync(hunter, skinKey, armorEffectKey,
                    _restoreLifetime.Token).ConfigureAwait(false)
                : await _hunterPreviews.LoadDeathStageAsync(hunter, skinKey,
                    armorEffectKey, deathEffectKey, _restoreLifetime.Token)
                    .ConfigureAwait(false);
            path = image?.Path;
        }
        catch (OperationCanceledException) when (_restoreLifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[previews] hunter {hunter}: {error.Message}");
        }
        PostUi(() =>
        {
            if (!String.Equals(identity, _modelPreviewIdentity, StringComparison.Ordinal)
                || !_hunterPreviewRequestKeys.TryGetValue(hunter,
                    out string? currentRequest)
                || !String.Equals(currentRequest, requestKey,
                    StringComparison.Ordinal)) return;
            _hunterPreviewLoads.Remove(hunter);
            if (path != null)
            {
                _hunterPreviewPaths[hunter] = path;
                _hunterPreviewRetryAfter.Remove(hunter);
            }
            else _hunterPreviewRetryAfter[hunter] = DateTimeOffset.UtcNow.AddSeconds(10);
            if (PrimeRoutePresentation.Normalize(_shell.CurrentRoute) == PrimeRoute.Hunter)
                RenderRoute(PrimeRoute.Hunter);
        });
    }

    private void StartWeaponPreview(BeamType beam)
    {
        if (_captureMode)
        {
            return;
        }
        RefreshModelPreviewIdentity();
        if (_weaponPreviewPaths.ContainsKey(beam) || !_weaponPreviewLoads.Add(beam)) return;
        if (_weaponPreviewRetryAfter.TryGetValue(beam, out DateTimeOffset retryAfter))
        {
            _weaponPreviewLoads.Remove(beam);
            if (retryAfter > DateTimeOffset.UtcNow) return;
            _weaponPreviewRetryAfter.Remove(beam);
        }
        _weaponPreviewLoadStarts++;
        _ = LoadWeaponPreviewAsync(beam, _modelPreviewIdentity);
    }

    private async Task LoadWeaponPreviewAsync(BeamType beam, string? identity)
    {
        string? path = null;
        try
        {
            PrimePreviewImage? image = await _weaponPreviews.LoadAsync(
                beam, _restoreLifetime.Token).ConfigureAwait(false);
            path = image?.Path;
        }
        catch (OperationCanceledException) when (_restoreLifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[previews] weapon {beam}: {error.Message}");
        }
        PostUi(() =>
        {
            if (!String.Equals(identity, _modelPreviewIdentity, StringComparison.Ordinal)) return;
            _weaponPreviewLoads.Remove(beam);
            if (path != null)
            {
                _weaponPreviewPaths[beam] = path;
                _weaponPreviewRetryAfter.Remove(beam);
            }
            else _weaponPreviewRetryAfter[beam] = DateTimeOffset.UtcNow.AddSeconds(10);
            if (PrimeRoutePresentation.Normalize(_shell.CurrentRoute) == PrimeRoute.Hunter
                && _routeViewState.HunterSection == HunterSection.Arsenal)
                RenderRoute(PrimeRoute.Hunter);
        });
    }

    private void RetryHunterPreview(Hunter hunter)
    {
        _hunterPreviewRetryAfter.Remove(hunter);
        StartHunterPreview(hunter);
        RenderRoute(PrimeRoute.Hunter);
    }

    private void RetryWeaponPreview(BeamType beam)
    {
        _weaponPreviewRetryAfter.Remove(beam);
        StartWeaponPreview(beam);
        RenderRoute(PrimeRoute.Hunter);
    }

    private void RefreshModelPreviewIdentity()
    {
        if (_captureMode) return;
        string? identity;
        try
        {
            (string version, string hash) = ContentEnvironment.GetContentIdentity();
            identity = $"{version}|{hash}";
        }
        catch { identity = null; }
        if (String.Equals(identity, _modelPreviewIdentity, StringComparison.Ordinal)) return;
        _modelPreviewIdentity = identity;
        _hunterPreviewPaths.Clear();
        _hunterPreviewLoads.Clear();
        _hunterPreviewRetryAfter.Clear();
        _hunterPreviewRequestKeys.Clear();
        _weaponPreviewPaths.Clear();
        _weaponPreviewLoads.Clear();
        _weaponPreviewRetryAfter.Clear();
        _previewImages.Clear();
    }

    private Control BuildTheatrePage()
    {
        TheatreState state = _theatre.State;
        if (!_theatreLoaded && !_captureMode)
        {
            _theatreLoaded = true;
            RunCommand("Load Theatre", () => _theatre.LoadAsync(_lifetime.Token));
        }
        return TheatrePresentation.Build(new TheatrePresentationContext(
            state,
            _theatre.Library.SupportsImport,
            _theatre.Library.SupportsExport,
            _theatre.Library.SupportsRename,
            _pendingReplayDeleteId,
            RunCommand,
            () => _theatre.LoadAsync(_lifetime.Token),
            ImportReplayWithPickerAsync,
            path => _theatre.ImportAsync(path, _lifetime.Token),
            _theatre.Select,
            replay => _theatre.PlayAsync(replay, _lifetime.Token),
            _theatre.SelectHighlight,
            index => _theatre.PlayHighlightAsync(index, cancellationToken: _lifetime.Token),
            () => _theatre.PlayHighlightReelAsync(cancellationToken: _lifetime.Token),
            ExportReplayWithPickerAsync,
            (replay, path) => _theatre.ExportAsync(path, replay, _lifetime.Token),
            (replay, name) => _theatre.RenameAsync(name, replay),
            DeleteReplayAsync,
            replay =>
            {
                _pendingReplayDeleteId = replay.Id;
                RenderRoute(PrimeRoute.Theatre);
            },
            () =>
            {
                _pendingReplayDeleteId = null;
                RenderRoute(PrimeRoute.Theatre);
            },
            _theatre.Library.SupportsReveal,
            replay => _theatre.RevealAsync(replay, _lifetime.Token),
            mapKey => _mapPreviews.LoadAsync(mapKey, _lifetime.Token),
            _theatre.ApplyFilters,
            _theatre.SetClipIn,
            _theatre.SetClipOut,
            (label, focus) => _theatre.SaveClipAsync(label, focus, _lifetime.Token),
            _theatre.ResetClipRange,
            favorite => _theatre.SetReplayFavoriteAsync(favorite, _lifetime.Token),
            favorite => _theatre.SetSelectedHighlightFavoriteAsync(favorite,
                _lifetime.Token),
            (clipId, favorite) => _theatre.SetClipFavoriteAsync(clipId, favorite,
                _lifetime.Token),
            marker => _theatre.PlayEventAsync(marker, _lifetime.Token),
            clip => _theatre.PreviewClipAsync(clip, _lifetime.Token)));
    }

    private async Task DeleteReplayAsync(PrimeReplayEntry replay)
    {
        await _theatre.DeleteAsync(replay).ConfigureAwait(false);
        PostUi(() =>
        {
            _pendingReplayDeleteId = null;
            RenderRoute(PrimeRoute.Theatre);
        });
    }

    private async Task ImportReplayWithPickerAsync()
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is not { CanOpen: true } storage)
            throw new InvalidOperationException("Replay import is unavailable on this platform.");
        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Import Project Prime replay",
                AllowMultiple = false,
                FileTypeFilter = OperatingSystem.IsAndroid() ? null :
                [new FilePickerFileType("Project Prime replay") { Patterns = ["*.fpreplay"] }]
            });
        if (files.Count == 0) return;
        string? localPath = files[0].TryGetLocalPath();
        string? scratch = null;
        try
        {
            if (localPath == null)
            {
                scratch = Path.Combine(Path.GetTempPath(), $"prime-import-{Guid.NewGuid():N}.fpreplay");
                await using Stream source = await files[0].OpenReadAsync();
                await using Stream destination = File.Create(scratch);
                await source.CopyToAsync(destination, _lifetime.Token).ConfigureAwait(false);
                localPath = scratch;
            }
            if (!await _theatre.ImportAsync(localPath, _lifetime.Token).ConfigureAwait(false))
                throw new InvalidOperationException("The selected replay could not be imported.");
        }
        finally
        {
            if (scratch != null)
            {
                try { File.Delete(scratch); }
                catch (IOException) { }
            }
        }
    }

    private async Task ExportReplayWithPickerAsync(PrimeReplayEntry replay)
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is not { CanSave: true } storage)
            throw new InvalidOperationException("Replay export is unavailable on this platform.");
        IStorageFile? destination = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Project Prime replay",
            SuggestedFileName = replay.FileName,
            DefaultExtension = "fpreplay",
            FileTypeChoices = OperatingSystem.IsAndroid() ? null :
                [new FilePickerFileType("Project Prime replay") { Patterns = ["*.fpreplay"] }]
        });
        if (destination == null) return;
        string scratch = Path.Combine(Path.GetTempPath(),
            $"prime-export-{Guid.NewGuid():N}.fpreplay");
        try
        {
            if (!await _theatre.ExportAsync(scratch, replay, _lifetime.Token).ConfigureAwait(false))
                throw new InvalidOperationException("The selected replay could not be exported.");
            await using Stream source = File.OpenRead(scratch);
            await using Stream target = await destination.OpenWriteAsync();
            if (target.CanSeek) target.SetLength(0);
            await source.CopyToAsync(target, _lifetime.Token).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(scratch); }
            catch (IOException) { }
        }
    }

    private Control BuildRankingsPage()
    {
        RankingsState state = _captureRankingsState ?? _rankings.State;
        if (!_captureMode && _shell.SignedIn && !_rankingsInitialLoadPending
            && !state.Loading && state.Rows.IsEmpty && state.Error is null)
        {
            _rankingsInitialLoadPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _rankingsInitialLoadPending = false;
                if (!_disposed && _shell.CurrentRoute == PrimeRoute.Rankings
                    && !_rankings.State.Loading && _rankings.State.Rows.IsEmpty
                    && _rankings.State.Error is null)
                    RunCommand("Load Rankings", () =>
                        _rankings.LoadAsync(cancellationToken: _lifetime.Token));
            }, DispatcherPriority.Background);
        }
        return RankingsPresentation.Build(new RankingsPresentationContext(
            _shell.SignedIn,
            state,
            () => Navigate(PrimeRoute.Gateway),
            RunCommand,
            ChangeRankingMetricAsync,
            ChangeRankingHunterAsync,
            next => _rankings.LoadAsync(next, _lifetime.Token)));
    }

    private async Task ChangeRankingMetricAsync(string metric)
    {
        _rankings.SetMetric(metric);
        await _rankings.LoadAsync(cancellationToken: _lifetime.Token).ConfigureAwait(false);
    }

    private async Task ChangeRankingHunterAsync(Hunter? hunter)
    {
        _rankings.SetHunterFilter(hunter);
        await _rankings.LoadAsync(cancellationToken: _lifetime.Token).ConfigureAwait(false);
    }

    private Control BuildSettingsPage()
    {
        var existing = new SettingsView(_settings, identity:
            SettingsIdentityContext.From(_shell,
                () => Navigate(PrimeRoute.Hunter)), embedActionBar: false)
        {
            Height = Math.Max(420, Bounds.Height > 0 ? Bounds.Height - 203 : 520)
        };
        var backend = Input(LauncherPrefs.BackendAddress, "Server address");
        var backendCard = Stack(Text("Network · Advanced", "prime-heading"), backend,
            MakeButton("Save Server", () => RunCommand("Save server", async () =>
            {
                if (!Uri.TryCreate(backend.Text?.Trim(), UriKind.Absolute, out Uri? uri))
                    throw new InvalidOperationException("Enter a valid server address.");
                await _gateway.ConfigureBackendAsync(uri, _lifetime.Token).ConfigureAwait(false);
                PostUi(() => _shell.Navigator.NavigateRoot(PrimeRoute.Gateway));
            }), primary: true));
        existing.AddNetworkAdvanced(backendCard);
        existing.ShowSection(_settingsFocusCategory);
        existing.SaveSucceeded += (_, _) =>
        {
            _shell.NotifyGlobal("settings-saved", PrimeNotificationKind.Success,
                "Settings saved to this device.");
            RefreshChrome();
        };
        bool openingGameFiles = false;
        existing.Closed += (_, _) =>
        {
            Resources["PrimeReducedMotion"] = LauncherPrefs.ReducedMotion;
            if (!openingGameFiles)
            {
                // Settings raises Closed from inside the Save/Discard input
                // callback. Replacing PageHost there re-enters Avalonia's
                // active visual-tree dispatch and can leave the destination
                // route measured without content. Finish the callback first,
                // then restore the route on the next UI turn.
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_disposed
                        && PrimeRoutePresentation.Normalize(_shell.CurrentRoute)
                            == PrimeRoute.Settings)
                    {
                        GoBack();
                    }
                }, DispatcherPriority.Background);
            }
        };
        existing.GameFilesRequested += (_, _) =>
        {
            openingGameFiles = true;
            PostUi(() =>
            {
                RouteTitle.Text = "Game files";
                PageHost.Content = BuildGameFilesPage();
                RefreshActionBar();
            });
        };
        return existing;
    }

    private void OpenNetworkSettings()
    {
        _settingsFocusCategory = "Network";
        if (PrimeRoutePresentation.Normalize(_shell.CurrentRoute) == PrimeRoute.Settings
            && PageHost.Content is SettingsView settingsView)
        {
            settingsView.ShowSection(_settingsFocusCategory);
            return;
        }
        Navigate(PrimeRoute.Settings);
    }

    private void ReconcileSeatOfferModal()
    {
        if (_disposed || IsTitleBlocking) return;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PlayState state = _capturePlayState ?? _play.State;
        LobbySnapshot? lobby = state.Lobby;
        LobbyQueueOffer? offer = lobby?.Waitlist?.SelfOffer;
        bool routeAvailable = PrimeRoutePresentation.Normalize(_shell.CurrentRoute)
            == PrimeRoute.Play;
        bool connected = _captureMode
            ? state.Node?.Session is not null
            : _online.Node is { Connected: true };
        PrimeSeatOfferObservation? observation = routeAvailable && connected
            && lobby is not null && offer is not null
            ? new PrimeSeatOfferObservation(
                new PrimeSeatOfferKey(lobby.LobbyId, offer.OfferId),
                lobby.Revision, offer.ExpiresAt)
            : null;

        PrimeSeatOfferTransition transition = _seatOfferModal.Observe(observation, now);
        switch (transition)
        {
            case PrimeSeatOfferTransition.Open:
                OpenSeatOfferModal(lobby!, offer!);
                break;
            case PrimeSeatOfferTransition.Close:
                CloseSeatOfferOverlay();
                _shell.ClearNotification("seat-offer");
                if (routeAvailable && connected && offer is null)
                    _shell.NotifyRoute("seat-offer-unavailable", PrimeNotificationKind.Warning,
                        "Player seat offer is no longer available.");
                RefreshChrome();
                break;
            case PrimeSeatOfferTransition.Expired:
                CloseSeatOfferOverlay();
                _shell.ClearNotification("seat-offer");
                _shell.NotifyRoute("seat-offer-expired", PrimeNotificationKind.Warning,
                    "Player seat offer expired.");
                RefreshChrome();
                break;
        }
    }

    private void OpenSeatOfferModal(LobbySnapshot lobby, LobbyQueueOffer offer)
    {
        var key = new PrimeSeatOfferKey(lobby.LobbyId, offer.OfferId);
        string modalId = SeatOfferModalId(key);
        if (OverlayRoot.IsVisible
            && String.Equals(_overlayModalId, modalId, StringComparison.Ordinal))
            return;

        var card = new SeatOfferCard(offer,
            () => BeginSeatOfferAction(key, accept: true),
            () => BeginSeatOfferAction(key, accept: false))
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 520
        };
        PrimeAccessibility.SetStatus(card,
            SeatOfferAvailableAnnouncement,
            PrimeStatusKind.Warning);
        ShowOverlay(card, modalId);
        _shell.NotifyRoute("seat-offer", PrimeNotificationKind.Info,
            SeatOfferAvailableAnnouncement);
        RefreshChrome();
    }

    private void BeginSeatOfferAction(PrimeSeatOfferKey key, bool accept)
    {
        if (!_playPresentation.TryBeginOfferAction(key.OfferId)) return;

        PlayState state = _capturePlayState ?? _play.State;
        LobbySnapshot? lobby = state.Lobby;
        LobbyQueueOffer? offer = lobby?.Waitlist?.SelfOffer;
        if (lobby is null || offer is null
            || new PrimeSeatOfferKey(lobby.LobbyId, offer.OfferId) != key
            || offer.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _playPresentation.ReleaseOfferAction(key.OfferId);
            ReconcileSeatOfferModal();
            return;
        }

        if (!_seatOfferModal.Complete(key))
        {
            _playPresentation.ReleaseOfferAction(key.OfferId);
            return;
        }
        CloseSeatOfferOverlay();
        _shell.ClearNotification("seat-offer");
        string operation = accept ? "Accept seat" : "Decline seat";
        RunCommand(operation, async () =>
        {
            try
            {
                if (accept)
                    await _play.AcceptWaitlistAsync(key.LobbyId, lobby.Revision,
                        key.OfferId, _lifetime.Token).ConfigureAwait(false);
                else
                    await _play.DeclineWaitlistAsync(key.LobbyId, lobby.Revision,
                        key.OfferId, _lifetime.Token).ConfigureAwait(false);
            }
            catch
            {
                PostUi(() =>
                {
                    _playPresentation.ReleaseOfferAction(key.OfferId);
                    _seatOfferModal.Release(key);
                    ReconcileSeatOfferModal();
                });
                throw;
            }

            PostUi(() =>
            {
                _playPresentation.CompleteOfferAction(key.OfferId);
                ReconcileSeatOfferModal();
                if (_seatOfferModal.Active is null)
                {
                    _shell.NotifyTransient("seat-offer-result", PrimeNotificationKind.Success,
                        accept ? "Player seat accepted." : "Player seat declined.");
                    RefreshChrome();
                }
            });
        });
    }

    private void SuspendSeatOfferModal()
    {
        _seatOfferModal.Suspend();
        CloseSeatOfferOverlay();
    }

    private void CloseSeatOfferOverlay()
    {
        if (IsSeatOfferOverlayOpen()) CloseOverlay();
    }

    private bool IsSeatOfferOverlayOpen()
        => _overlayModalId?.StartsWith(SeatOfferModalPrefix,
            StringComparison.Ordinal) == true;

    private static string SeatOfferModalId(PrimeSeatOfferKey key)
        => $"{SeatOfferModalPrefix}{key.LobbyId:N}:{key.OfferId:N}";

    private void ShowOverlay(Control control, string modalId = "shell-overlay")
    {
        ArgumentNullException.ThrowIfNull(control);
        if (IsTitleBlocking) return;
        if (OverlayRoot.IsVisible)
            CloseOverlay();
        RememberCurrentNavigationFocus();
        if (!_overlayTabNavigationCaptured)
        {
            _overlayTabNavigationBeforeOpen = KeyboardNavigation.GetTabNavigation(
                OverlayRoot);
            _overlayTabNavigationCaptured = true;
        }
        OverlayHost.Content = control;
        OverlayRoot.IsVisible = true;
        KeyboardNavigation.SetTabNavigation(OverlayRoot,
            KeyboardNavigationMode.Cycle);
        _overlayModalId = modalId;
        IReadOnlyList<PrimeShellNavigationTarget> targets =
            CaptureNavigationTargets();
        PrimeNavigationResult opened = _inputNavigator.OpenModal(modalId,
            targets.Select(target => target.Candidate), FocusScope(
                _shell.CurrentRoute).WithModal(modalId));
        PrimeShellNavigationAdapter.Apply(targets, opened);
        _overlayMotion?.Dispose();
        if (_captureMode) control.Opacity = 1;
        else _overlayMotion = PrimeMotion.AnimateEntry(control);
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !OverlayRoot.IsVisible
                || !String.Equals(_overlayModalId, modalId,
                    StringComparison.Ordinal))
                return;
            IReadOnlyList<PrimeShellNavigationTarget> rebuilt =
                CaptureNavigationTargets();
            PrimeNavigationResult restored = _inputNavigator.RestoreScope(
                FocusScope(_shell.CurrentRoute).WithModal(modalId),
                rebuilt.Select(target => target.Candidate));
            PrimeShellNavigationAdapter.Apply(rebuilt, restored);
        }, DispatcherPriority.Background);
    }

    private void CloseOverlay()
    {
        if (!OverlayRoot.IsVisible)
            return;
        string? modalId = _overlayModalId;
        _overlayMotion?.Dispose();
        _overlayMotion = null;
        OverlayRoot.IsVisible = false;
        OverlayHost.Content = null;
        if (String.Equals(modalId, MatchTransitionModalId,
            StringComparison.Ordinal))
            DetachMatchTransition();
        if (_overlayTabNavigationCaptured)
        {
            KeyboardNavigation.SetTabNavigation(OverlayRoot,
                _overlayTabNavigationBeforeOpen);
            _overlayTabNavigationCaptured = false;
        }
        _overlayModalId = null;
        if (modalId is null)
            return;

        IReadOnlyList<PrimeShellNavigationTarget> targets =
            CaptureNavigationTargets();
        PrimeNavigationResult closed = _inputNavigator.CloseModal(modalId,
            targets.Select(target => target.Candidate));
        PrimeShellNavigationAdapter.Apply(targets, closed);
    }

    private void Finish(LaunchPlan plan)
    {
        if (_finished) return;
        _finished = true;
        Plan = plan;
        Done?.Invoke(this, plan);
    }

    private void LaunchRequested(object? sender, LaunchPlan plan)
    {
        PrimeRoute expectedRoute = ReferenceEquals(sender, _play)
            ? PrimeRoute.Play : PrimeRoute.Theatre;
        PostUi(() =>
        {
            if (!_active || _disposed || _shell.CurrentRoute != expectedRoute) return;
            Finish(plan);
        });
    }

    private void ShellNavigationChanged(object? sender, PrimeNavigationChangedEventArgs args)
        => PostUi(() =>
        {
            if (args.Route != PrimeRoute.Play) _play.CancelPendingHandoff();
            RenderRoute(args.Route);
            if (args.Route == PrimeRoute.Play && _restoreOnActivate
                && _shell.HasNetworkIdentity && _play.State.Lobbies == null
                && _play.State.Lobby == null)
                RunCommand("Browse lobbies", () => _play.BrowseLobbiesAsync(_lifetime.Token));
        });

    private void ShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        => PostUi(RefreshChrome);

    private void GatewayChanged(object? sender, EventArgs args)
        => PostUi(RefreshChrome);

    private void GatewayIdentityChanged(object? sender, EventArgs args)
    {
        PostUi(() =>
        {
            if (_shell.SignedIn && _shell.PlayerId is PlayerId principal)
                _appearance.UseAccountStorage(principal);
            else
                _appearance.UseGuestStorage();
            _accountCosmeticsLoaded = AccountSessions.Current is not { IsSignedIn: true };
            ResetCosmeticSyncRetry();
            _play.CancelIdentityOperations();
            _routeViewState.ResetGuestHunterEntry();
            if (!_shell.HasNetworkIdentity)
                _gatewayForm = GatewayForm.Landing;
            RefreshNavigation();
            if (_shell.SignedIn && _shell.CurrentRoute == PrimeRoute.Gateway)
                _shell.Navigator.NavigateRoot(PrimeRoute.Play);
        });
        if (_shell.SignedIn)
            RunCommand("Load cosmetics", LoadAccountCosmeticsAsync);
    }

    private void PlayChanged(object? sender, EventArgs args)
        => PostUi(() =>
        {
            if (_captureMode) return;
            if (_shell.CurrentRoute == PrimeRoute.Play && IsPlayEditorFocused())
            {
                _playRefreshPending = true;
                RefreshChrome();
            }
            else if (_shell.CurrentRoute == PrimeRoute.Play)
            {
                _playRefreshPending = false;
                RenderRoute(PrimeRoute.Play);
            }
            else RefreshChrome();
            TrySynchronizeLobbyCosmetics();
        });

    private void PlayPresenceChanged(object? sender, EventArgs args)
        => PostUi(() =>
        {
            if (_captureMode) return;
            if (_shell.CurrentRoute != PrimeRoute.Play
                || _playPresentation.Subsection != PlaySubsection.Home)
                return;
            if (IsPlayEditorFocused())
                _playRefreshPending = true;
            else if (!TryUpdatePlayHomePresence())
                RenderRoute(PrimeRoute.Play);
        });

    private void LicenseChanged(object? sender, EventArgs args)
        => PostUi(() =>
        {
            if (PrimeRoutePresentation.Normalize(_shell.CurrentRoute) == PrimeRoute.Hunter)
                RenderRoute(PrimeRoute.Hunter);
        });

    private void RankingsChanged(object? sender, EventArgs args)
        => PostUi(() => { if (_shell.CurrentRoute == PrimeRoute.Rankings) RenderRoute(PrimeRoute.Rankings); });

    private void TheatreChanged(object? sender, EventArgs args)
        => PostUi(() => { if (_shell.CurrentRoute == PrimeRoute.Theatre) RenderRoute(PrimeRoute.Theatre); });

    private void RefreshNavigation()
    {
        NavPanel.IsVisible = _shell.HasNetworkIdentity;
        foreach ((AvaloniaButton button, PrimeRoute route) in _navigationButtons)
        {
            button.Classes.Remove("prime-primary");
            if (PrimeRoutePresentation.Normalize(_shell.CurrentRoute) == route)
                button.Classes.Add("prime-primary");
        }
        foreach ((AvaloniaButton button, PrimeRoute route) in _mobileNavigationButtons)
        {
            button.IsEnabled = _shell.HasNetworkIdentity;
            button.Classes.Remove("prime-primary");
            if (PrimeRoutePresentation.Normalize(_shell.CurrentRoute) == route)
                button.Classes.Add("prime-primary");
        }
    }

    private void RefreshChrome()
    {
        string? notificationMessage = _shell.Notification?.Message;
        bool signedOutGateway = !_shell.HasNetworkIdentity
            && PrimeRoutePresentation.Normalize(_shell.CurrentRoute) == PrimeRoute.Gateway;
        AccountButton.Content = _shell.HasNetworkIdentity ? $"{_shell.DisplayName} ▾" : "Sign In";
        AccountButton.IsVisible = !signedOutGateway;
        ConnectionText.Text = _shell.NodeConnected
            ? $"{_shell.NodeRegion} · Connected".TrimStart(' ', '·')
            : _shell.BackendConnected ? "Online" : "Offline";
        PrimeAccessibility.SetStatus(ConnectionText, ConnectionText.Text,
            _shell.NodeConnected || _shell.BackendConnected
                ? PrimeStatusKind.Info : PrimeStatusKind.Warning);
        string? footerStatus = _shell.BusyOperation ?? _backgroundStatus
            ?? DescribeUpdateStatus();
        StatusText.Text = footerStatus ?? "";
        StatusText.IsVisible = !String.IsNullOrWhiteSpace(footerStatus);
        if (StatusText.IsVisible)
            PrimeAccessibility.SetStatus(StatusText, StatusText.Text,
                PrimeStatusKind.Info);
        PrimeAccessibility.SetName(InputHintText,
            "Controller and keyboard controls");
        InputHintText.Text = _shell.LastInputDevice switch
        {
            PrimeInputDevice.Gamepad => string.Join("    ",
                PrimeControllerGlyphs.Prompt("Select", GamepadButtons.A,
                    GamepadInput.State.Family),
                PrimeControllerGlyphs.Prompt("Back", GamepadButtons.B,
                    GamepadInput.State.Family)),
            PrimeInputDevice.Touch => "",
            PrimeInputDevice.KeyboardMouse => "Enter Select    Esc Back",
            _ => "Enter Select    Esc Back"
        };
        InputHintText.IsVisible = _shell.LastInputDevice != PrimeInputDevice.Touch;
        if (_shell.Notification is { } notification)
        {
            NotificationBar.IsVisible = true;
            NotificationText.Text = notificationMessage;
            PrimeStatusKind statusKind = notification.Kind switch
            {
                PrimeNotificationKind.Success => PrimeStatusKind.Success,
                PrimeNotificationKind.Warning => PrimeStatusKind.Warning,
                PrimeNotificationKind.Error => PrimeStatusKind.Error,
                _ => PrimeStatusKind.Info
            };
            NotificationText.Foreground = notification.Kind switch
            {
                PrimeNotificationKind.Success => GuiTheme.SuccessBrush,
                PrimeNotificationKind.Warning => GuiTheme.WarningBrush,
                PrimeNotificationKind.Error => GuiTheme.ErrorBrush,
                _ => GuiTheme.TextBrush
            };
            PrimeAccessibility.SetStatus(NotificationBar, notificationMessage!,
                statusKind);
            if (!StringComparer.Ordinal.Equals(_renderedNotificationKey,
                    notification.Key))
            {
                _notificationMotion?.Dispose();
                _notificationMotion = _captureMode ? null
                    : PrimeMotion.AnimateEntry(NotificationBar,
                        PrimeMotionPreset.FadeAndSlide,
                        duration: PrimeMotion.FastDuration,
                        translation: new Vector(0, -6));
                _renderedNotificationKey = notification.Key;
            }
        }
        else
        {
            _notificationMotion?.Dispose();
            _notificationMotion = null;
            _renderedNotificationKey = null;
            NotificationBar.IsVisible = false;
        }
        RefreshActionBar();
        RefreshNavigation();
    }

    private void RefreshActionBar()
    {
        if (PrimeRoutePresentation.Normalize(_shell.CurrentRoute) == PrimeRoute.Settings
            && PageHost.Content is SettingsView settings)
        {
            if (!ReferenceEquals(_settingsActionView, settings)
                || _settingsActionBar == null)
            {
                _settingsActionView = settings;
                _settingsActionBar = new SettingsActionBar(settings, inGame: false);
            }
            if (ActionBar.Children.Count != 1
                || !ReferenceEquals(ActionBar.Children[0], _settingsActionBar))
            {
                ActionBar.Children.Clear();
                ActionBar.Children.Add(_settingsActionBar);
            }
            return;
        }
        _settingsActionView = null;
        _settingsActionBar = null;
        ActionBar.Children.Clear();
        if (Update.Updater.Configured && _update is { } update)
        {
            AvaloniaButton install = MakeButton($"Update available · {update.Tag}",
                () => RunCommand("Install update", () => DownloadAndInstall(update)),
                primary: true);
            install.HorizontalAlignment = HorizontalAlignment.Right;
            ActionBar.Children.Add(install);
        }
    }

    private async Task TryInstallStaged()
    {
        if (_disposed || !_active
            || LauncherPrefs.UpdatePolicy != UpdatePolicy.Automatic
            || Update.Updater.Coordinator.Status.State is not
                (UpdateState.Staged or UpdateState.WaitingForSafePoint))
            return;
        Update.IUpdateInstaller? installer = Update.UpdateInstall.Current;
        if (installer == null)
            return;
        if (!installer.Allowed)
        {
            installer.RequestPermission();
            return;
        }
        bool started = await Update.Updater.InstallStagedAsync().ConfigureAwait(true);
        if (started && installer.ExitAfterInstall)
            Finish(default);
    }

    private async Task DownloadAndInstall(Update.UpdateInfo update)
    {
        if (_disposed || !_active) return;
        Update.UpdateCoordinator coordinator = Update.Updater.Coordinator;
        coordinator.SetSafeToRestart(true);
        bool started = await Update.Updater.DownloadAndInstallAsync()
            .ConfigureAwait(true);
        if (!started)
        {
            _shell.NotifyGlobal("update-failed", PrimeNotificationKind.Warning,
                coordinator.Status.Message ?? "the update could not be staged");
            RefreshChrome();
            return;
        }
        Update.IUpdateInstaller? installer = Update.UpdateInstall.Current;
        if (installer?.ExitAfterInstall == true) Finish(default);
        else RefreshChrome();
    }

    private async Task UpdateAndRetryNodes()
    {
        bool started = await Update.Updater.DownloadAndInstallAsync()
            .ConfigureAwait(true);
        if (!started)
        {
            _shell.NotifyGlobal("update-failed", PrimeNotificationKind.Warning,
                Update.Updater.Coordinator.Status.Message ?? "the update could not be staged");
            RefreshChrome();
            return;
        }
        Update.IUpdateInstaller? installer = Update.UpdateInstall.Current;
        if (installer?.ExitAfterInstall == true)
        {
            Finish(default);
            return;
        }
        _shell.NotifyGlobal("update-submitted", PrimeNotificationKind.Success,
            "Update submitted. Return here after Android finishes installing, then retry server discovery.");
        RefreshChrome();
    }

    private string? DescribeUpdateStatus()
    {
        Update.UpdateStatus status = _updateStatus;
        return status.State switch
        {
            UpdateState.Downloading or UpdateState.Verifying when status.TotalBytes > 0
                => $"Updating {status.BytesReceived / (1024 * 1024)} / "
                    + $"{status.TotalBytes / (1024 * 1024)} MB",
            UpdateState.Downloading or UpdateState.Verifying => "Downloading update",
            UpdateState.Available when status.AvailableVersion != null
                => $"Update available v{status.AvailableVersion}",
            UpdateState.Staged => "Update staged; ready to restart",
            UpdateState.WaitingForSafePoint => "Update staged; waiting for a safe point",
            UpdateState.Installing or UpdateState.Restarting => status.Message,
            _ => null
        };
    }

    private void UpdateStatusChanged(object? sender, Update.UpdateStatus status)
    {
        PostUi(() =>
        {
            _updateStatus = status;
            if (status.State == UpdateState.Failed
                && !String.IsNullOrWhiteSpace(status.Message))
                _shell.NotifyGlobal("update-failed", PrimeNotificationKind.Error,
                    status.Message);
            RefreshChrome();
            if ((status.State is UpdateState.Available or UpdateState.Staged
                    or UpdateState.WaitingForSafePoint)
                && _shell.CurrentRoute == PrimeRoute.Play)
                RenderRoute(PrimeRoute.Play);
            if (status.State == UpdateState.Staged
                && LauncherPrefs.UpdatePolicy == UpdatePolicy.Automatic)
                _ = TryInstallStaged();
        });
    }

    private void ApplyResponsiveLayout(double width, double height)
    {
        PrimeShellBreakpoint breakpoint = PrimeRoutePresentation.Breakpoint(
            width > 0 ? width : PrimeRoutePresentation.WideLowerBound);
        double menuScale = PrimeRoutePresentation.WindowedMenuScale(
            width > 0 ? width : PrimeRoutePresentation.WideLowerBound,
            height > 0 ? height : PrimeRoutePresentation.FullScaleMenuContentHeight
                + PrimeRoutePresentation.DesktopVerticalChrome);
        bool narrow = breakpoint == PrimeShellBreakpoint.Mobile;
        bool compactHeader = breakpoint == PrimeShellBreakpoint.Compact;
        NavPanel.Orientation = Orientation.Horizontal;
        foreach ((AvaloniaButton button, PrimeRoute route) in _navigationButtons)
            button.Content = PrimeRoutePresentation.NavigationLabel(route, breakpoint);
        HeaderBorder.Padding = narrow ? new Thickness(4, 0) : new Thickness(20, 0);
        HeaderGrid.ColumnSpacing = narrow ? 4 : 16;
        if (narrow)
        {
            HeaderGrid.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto");
            HeaderGrid.RowDefinitions = new RowDefinitions("Auto");
            Grid.SetColumn(BrandPanel, 0);
            Grid.SetRow(BrandPanel, 0);
            Grid.SetColumn(NavPanel, 1);
            Grid.SetRow(NavPanel, 0);
            Grid.SetColumn(AccountPanel, 2);
            Grid.SetRow(AccountPanel, 0);
            BrandTextPanel.IsVisible = true;
            BrandPanel.IsVisible = true;
            BrandPanel.HorizontalAlignment = HorizontalAlignment.Left;
            NavPanel.HorizontalAlignment = HorizontalAlignment.Left;
            AccountPanel.HorizontalAlignment = HorizontalAlignment.Right;
            PageGrid.Margin = new Thickness(12);
        }
        else
        {
            HeaderGrid.RowDefinitions = new RowDefinitions("Auto");
            HeaderGrid.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto");
            Grid.SetColumn(BrandPanel, 0);
            Grid.SetRow(BrandPanel, 0);
            Grid.SetColumn(NavPanel, 1);
            Grid.SetRow(NavPanel, 0);
            Grid.SetColumn(AccountPanel, 2);
            Grid.SetRow(AccountPanel, 0);
            BrandTextPanel.IsVisible = !compactHeader;
            BrandPanel.IsVisible = true;
            NavPanel.HorizontalAlignment = HorizontalAlignment.Center;
            AccountPanel.HorizontalAlignment = HorizontalAlignment.Right;
            PageGrid.Margin = new Thickness(24, 20);
        }
        NavPanel.IsVisible = !narrow && _shell.HasNetworkIdentity;
        AccountPanel.IsVisible = !narrow;
        ConnectionChip.IsVisible = breakpoint == PrimeShellBreakpoint.Wide;
        FooterBorder.IsVisible = !narrow;
        MobileNavigationBorder.IsVisible = narrow;
        MobileNavigationBorder.Height = PrimeLayoutMetrics.MobileNavigationHeightDip;
        HeaderBorder.Height = narrow ? 64 : 58;
        FooterBorder.Height = 34;
        ShellGrid.RowDefinitions = new RowDefinitions(narrow ? "64,*,72" : "58,*,34");
        PageGrid.Margin = narrow
            ? PrimeLayoutMetrics.ResolveMobileContentMargin(default)
            : new Thickness(24, 20);
        PageScaleHost.LayoutTransform = menuScale < .999
            ? new ScaleTransform(menuScale, menuScale)
            : null;
        PrimeShellNavigationAdapter.ApplySafeArea(HeaderBorder,
            MobileNavigationBorder, narrow);
        PrimeShellNavigationAdapter.ApplyTouchTarget(AccountButton);
        PrimeShellNavigationAdapter.ApplyTouchTarget(SettingsButton);
        if (_shell.CurrentRoute == PrimeRoute.Settings
            && PageHost.Content is SettingsView settingsView)
        {
            // Settings owns its own fixed footer and scrolling content. Give
            // it the shell viewport rather than letting the outer scroller
            // measure it as an unbounded page and push Apply below the fold.
            settingsView.Height = Math.Max(420, (height - 203) / menuScale);
            _settingsActionBar?.ApplyLayout(width < 620);
        }
    }

    private bool IsPlayEditorFocused()
    {
        if (_padCombo?.IsDropDownOpen == true) return true;
        object? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused is TextBox or ComboBox
            && PageHost.GetVisualDescendants().Any(control => ReferenceEquals(control, focused));
    }

    private void PlayEditorLostFocus(object? sender, RoutedEventArgs args)
        => Dispatcher.UIThread.Post(() =>
        {
            if (_playRefreshPending && _shell.CurrentRoute == PrimeRoute.Play
                && !IsPlayEditorFocused())
            {
                _playRefreshPending = false;
                RenderRoute(PrimeRoute.Play);
            }
        }, DispatcherPriority.Background);

    private void PollInput()
    {
        if (!_active || _disposed) return;
        GamepadInput.PollPlatformForMenu();
        if (IsTitleBlocking)
        {
            HandleTitleControllerState(GamepadInput.State);
            return;
        }
        ReconcileSeatOfferModal();
        if (!GamepadInput.Active)
        {
            _previousPadButtons = GamepadButtons.None;
            _previousPadDirection = 0;
            return;
        }
        GamepadButtons buttons = GamepadInput.EffectiveButtons;
        GamepadButtons pressed = buttons & ~_previousPadButtons;
        IReadOnlyList<PrimeShellNavigationTarget> targets = CaptureNavigationTargets();
        TrackCurrentNavigationFocus(targets);

        // Bumpers are a separate section channel. They must not be treated as
        // horizontal D-pad travel, and only routes with real major sections
        // consume them. Triggers intentionally remain available to gameplay
        // and are not hijacked by the shell.
        GamepadButtons sectionButton = pressed
            & (GamepadButtons.LeftBumper | GamepadButtons.RightBumper);
        if (sectionButton != GamepadButtons.None)
            MoveMajorSection(sectionButton, targets);

        if ((pressed & GamepadButtons.Start) != 0 && !OverlayRoot.IsVisible)
            Navigate(PrimeRoute.Settings);

        int direction = PadDirection(buttons);
        object? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        ComboBox? activeCombo = _padCombo?.IsDropDownOpen == true
            ? _padCombo : focused as ComboBox;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool repeat = direction != 0 && direction == _previousPadDirection && now >= _nextPadRepeat;
        if (direction != 0 && (direction != _previousPadDirection || repeat))
        {
            if (activeCombo is { IsDropDownOpen: true } openCombo
                && Math.Abs(direction) == 1)
            {
                ApplyComboPad(openCombo, direction > 0
                    ? ControllerComboCommand.Next : ControllerComboCommand.Previous);
            }
            else MovePadFocus(direction, targets);
            _nextPadRepeat = now.AddMilliseconds(repeat ? 120 : 350);
        }
        if ((pressed & GamepadButtons.A) != 0)
        {
            Action? acceptCombo = activeCombo is ComboBox combo
                ? () => ApplyComboPad(combo, ControllerComboCommand.Accept)
                : null;
            TryActivateControllerTarget(focused, acceptCombo);
        }
        if ((pressed & GamepadButtons.B) != 0)
        {
            if (activeCombo is { IsDropDownOpen: true } combo)
                ApplyComboPad(combo, ControllerComboCommand.Cancel);
            else GoBack();
        }
        if (GamepadInput.InUse) _shell.SetLastInputDevice(PrimeInputDevice.Gamepad);
        _previousPadButtons = buttons;
        _previousPadDirection = direction;
    }

    /// <summary>
    /// Applies the same activation semantics as controller A to the focused
    /// shell target. ToggleButton is intentionally handled before Button:
    /// Avalonia's Button.ClickEvent only dispatches the event and does not run
    /// ToggleButton's state transition, so browser filters would never update
    /// their IsCheckedChanged-backed query state. After the explicit
    /// two-state transition, dispatch one click for any command-style handler.
    /// </summary>
    internal static bool TryActivateControllerTarget(object? focused,
        Action? acceptCombo = null)
    {
        switch (focused)
        {
            case PrimeButton action:
                action.Invoke();
                return true;
            case ToggleButton toggle when toggle.IsEffectivelyEnabled:
                toggle.IsChecked = toggle.IsChecked != true;
                toggle.RaiseEvent(new RoutedEventArgs(AvaloniaButton.ClickEvent));
                return true;
            case AvaloniaButton button when button.IsEffectivelyEnabled:
                button.RaiseEvent(new RoutedEventArgs(AvaloniaButton.ClickEvent));
                return true;
            case ComboBox:
                if (acceptCombo is null)
                    return false;
                acceptCombo();
                return true;
            case IControllerNavigable controller:
                controller.ControllerActivate();
                return true;
            default:
                if (acceptCombo is null)
                    return false;
                acceptCombo();
                return true;
        }
    }

    private void TrackCurrentNavigationFocus(
        IReadOnlyList<PrimeShellNavigationTarget> targets)
    {
        object? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (PrimeShellNavigationAdapter.FindFocusedId(targets, focused) is { } focusId)
        {
            _inputNavigator.TrackFocused(focusId);
            if (_shell.CurrentRoute == PrimeRoute.Settings
                && PrimeShellNavigationAdapter.SelectedSettingsSection(targets) is { } selected)
                _settingsFocusCategory = selected;
        }
    }

    private void MoveMajorSection(GamepadButtons button,
        IReadOnlyList<PrimeShellNavigationTarget> targets)
    {
        if (!PrimeSectionNavigation.TryGetDirection(button,
                out PrimeNavigationSectionDirection direction))
            return;
        PrimeRoute route = PrimeRoutePresentation.Normalize(_shell.CurrentRoute);
        if (route is not (PrimeRoute.Hunter or PrimeRoute.Settings))
            return;
        IReadOnlyList<PrimeNavigationSection> sections =
            PrimeShellNavigationAdapter.SectionsFor(route, targets);
        if (sections.Count < 2)
            return;
        string? current = route == PrimeRoute.Hunter
            ? _routeViewState.HunterSection.ToString()
            : PrimeShellNavigationAdapter.SelectedSettingsSection(targets)
                ?? _settingsFocusCategory;
        PrimeShellNavigationTarget[] sectionTargets = targets
            .Where(target => target.Candidate.SectionId is not null)
            .ToArray();
        PrimeSectionNavigationResult result = _inputNavigator.MoveSection(current,
            sections, direction, sectionTargets.Select(target => target.Candidate));
        if (!result.Changed || result.RestoredFocusId is not { } focusId)
            return;
        if (route == PrimeRoute.Settings)
            _settingsFocusCategory = result.SectionId ?? _settingsFocusCategory;
        PrimeShellNavigationAdapter.FocusById(sectionTargets, focusId);
        PrimeShellNavigationTarget? target = sectionTargets.FirstOrDefault(candidate =>
            String.Equals(candidate.FocusId, focusId, StringComparison.Ordinal));
        if (route == PrimeRoute.Settings && result.SectionId is { } settingsSection
            && PageHost.Content is SettingsView settingsView)
            settingsView.ShowSection(settingsSection);
        else if (target?.Control is IControllerNavigable navigable)
            navigable.ControllerActivate();
        else if (target?.Control is AvaloniaButton buttonTarget)
            buttonTarget.RaiseEvent(new RoutedEventArgs(AvaloniaButton.ClickEvent));
    }

    private void ApplyComboPad(ComboBox combo, ControllerComboCommand command)
    {
        bool tracked = ReferenceEquals(_padCombo, combo);
        int original = tracked ? _padComboOriginalIndex : combo.SelectedIndex;
        var state = new ControllerComboState(
            combo.Tag is ControllerComboSelector selector
                ? selector : ControllerComboSelector.Generic,
            combo.SelectedIndex, original, combo.IsDropDownOpen);
        ControllerComboState next = ControllerComboNavigation.Transition(
            state, combo.Items.Count, command);
        bool hunter = ReferenceEquals(combo, _hunterPadCombo)
            && _hunterPadSelection != null;
        if (hunter && command == ControllerComboCommand.Accept && !state.IsOpen
            && combo.SelectedItem is Hunter openingHunter)
            _hunterPadSelection!.Begin(openingHunter);
        combo.SelectedIndex = next.SelectedIndex;
        combo.IsDropDownOpen = next.IsOpen;
        if (hunter && state.IsOpen)
        {
            if (command == ControllerComboCommand.Cancel)
            {
                Hunter restored = _hunterPadSelection!.Cancel();
                combo.SelectedItem = restored;
            }
            else if (command == ControllerComboCommand.Accept)
                _hunterPadSelection!.Accept();
        }
        if (next.IsOpen)
        {
            _padCombo = combo;
            _padComboOriginalIndex = next.OriginalIndex;
        }
        else
        {
            _padCombo = null;
            _padComboOriginalIndex = -1;
        }
    }

    internal static int PadDirection(GamepadButtons buttons)
    {
        GamepadState state = GamepadInput.State;
        if ((buttons & GamepadButtons.DpadRight) != 0 || state.LeftX > 0.65f) return 2;
        if ((buttons & GamepadButtons.DpadLeft) != 0 || state.LeftX < -0.65f) return -2;
        if ((buttons & GamepadButtons.DpadDown) != 0 || state.LeftY < -0.65f) return 1;
        if ((buttons & GamepadButtons.DpadUp) != 0 || state.LeftY > 0.65f) return -1;
        return 0;
    }

    private void MovePadFocus(int direction,
        IReadOnlyList<PrimeShellNavigationTarget>? measuredTargets = null)
    {
        object? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        IReadOnlyList<PrimeShellNavigationTarget> targets = measuredTargets
            ?? CaptureNavigationTargets();
        TrackCurrentNavigationFocus(targets);
        if (Math.Abs(direction) == 2 && focused is IControllerNavigable adjustable
            && SupportsControllerAdjustment(focused))
        {
            adjustable.ControllerAdjust(Math.Sign(direction));
            _inputNavigator.RememberFocused();
            return;
        }
        PrimeNavigationResult result = _inputNavigator.Move(
            NavigationDirection(direction), targets.Select(target => target.Candidate));
        PrimeShellNavigationAdapter.Apply(targets, result);
    }

    private void RunCommand(string operation, Func<Task> work)
    {
        PrimeRoute owner = PrimeRoutePresentation.Normalize(_shell.CurrentRoute);
        _ = ExecuteCommandAsync(operation, work, owner);
    }

    private void StartPreviewCatchup()
    {
        if (_previewCatchupStarted || !GameFiles.Ready || !ThumbnailHost.CanRender) return;
        try
        {
            if (ThumbnailGenerator.MissingThumbnails().Count == 0) return;
        }
        catch (Exception error)
        {
            _shell.NotifyRoute("map-preview-check", PrimeNotificationKind.Warning,
                $"Map preview check failed: {error.Message}");
            return;
        }
        _previewCatchupStarted = true;
        _ = CatchUpPreviewsAsync();
    }

    private async Task CatchUpPreviewsAsync()
    {
        try
        {
            await ThumbnailHost.RenderMissingAsync(line => PostUi(() =>
            {
                _backgroundStatus = line;
                RefreshChrome();
            })).ConfigureAwait(false);
            PostUi(() => _shell.NotifyTransient("map-previews-ready",
                PrimeNotificationKind.Success, "Local map previews are ready."));
        }
        catch (Exception error)
        {
            PostUi(() => _shell.NotifyRoute("map-preview-render",
                PrimeNotificationKind.Warning,
                $"Map preview rendering did not finish: {error.Message}"));
        }
        finally
        {
            PostUi(() =>
            {
                _backgroundStatus = null;
                RefreshChrome();
            });
        }
    }

    private async Task ExecuteCommandAsync(string operation, Func<Task> work,
        PrimeRoute owner)
    {
        if (_disposed) return;
        Interlocked.Increment(ref _busyOperations);
        _shell.SetBusy(operation);
        try { await work().ConfigureAwait(false); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            PostUi(() => _shell.NotifyRoute($"command:{operation}",
                PrimeNotificationKind.Error, error.Message, owner));
        }
        finally
        {
            PostUi(() =>
            {
                if (Interlocked.Decrement(ref _busyOperations) == 0)
                    _shell.SetBusy(null);
            });
        }
    }

    private void PostUi(Action action)
    {
        if (_disposed) return;
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed) action();
        });
    }

    private static void CenterHeading(PrimePageHeading heading)
    {
        foreach (TextBlock text in heading.Children.OfType<TextBlock>())
        {
            text.HorizontalAlignment = HorizontalAlignment.Stretch;
            text.TextAlignment = TextAlignment.Center;
        }
    }

    private static Grid ResponsiveThreeColumn(Control left, Control center, Control right)
    {
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnDefinitions = new ColumnDefinitions("260,*,330"),
            RowDefinitions = new RowDefinitions("Auto"),
            ColumnSpacing = 16
        };
        var leftScroll = new ScrollViewer
        {
            Content = left,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var leftSlot = new Border { Child = leftScroll, HorizontalAlignment = HorizontalAlignment.Stretch, Width = 260 };
        var centerSlot = new Border { Child = center, HorizontalAlignment = HorizontalAlignment.Stretch };
        var rightSlot = new Border { Child = right, HorizontalAlignment = HorizontalAlignment.Stretch, Width = 330 };
        grid.Children.Add(leftSlot);
        grid.Children.Add(centerSlot);
        grid.Children.Add(rightSlot);

        void Apply(double width)
        {
            bool narrow = width > 0 && width < 720;
            bool medium = !narrow && width > 0 && width < 1100;
            grid.ColumnDefinitions = new ColumnDefinitions(narrow ? "*"
                : medium ? "240,*" : "260,*,330");
            grid.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto,Auto"
                : medium ? "Auto,Auto" : "Auto");
            grid.ColumnSpacing = narrow ? 0 : 16;
            grid.RowSpacing = narrow ? 12 : medium ? 12 : 0;
            leftSlot.Width = narrow || medium ? double.NaN : 260;
            rightSlot.Width = narrow || medium ? double.NaN : 330;
            leftSlot.MaxHeight = narrow ? 360 : medium ? 760 : 780;
            Grid.SetColumn(leftSlot, 0);
            Grid.SetColumn(centerSlot, narrow ? 0 : 1);
            Grid.SetColumn(rightSlot, narrow ? 0 : medium ? 0 : 2);
            Grid.SetRow(leftSlot, 0);
            Grid.SetRow(centerSlot, narrow ? 1 : 0);
            Grid.SetRow(rightSlot, narrow ? 2 : medium ? 1 : 0);
            Grid.SetColumnSpan(rightSlot, medium ? 2 : 1);
        }

        grid.SizeChanged += (_, e) => Apply(e.NewSize.Width);
        return grid;
    }

    private static Control BuildPreviewStage(string? path, double height, string emptyMessage)
    {
        Control content;
        if (path is { Length: > 0 })
            content = new PrimeLocalImage(path, Math.Max(1, height - 20)) { Stretch = Stretch.Uniform };
        else
            content = Text(emptyMessage, "prime-muted");
        if (content is TextBlock text)
        {
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
        }
        var stage = PrimeControlFactory.PreviewStage(content);
        stage.Height = height;
        return stage;
    }

    private static StackPanel Stack(params Control[] controls)
    {
        var stack = new StackPanel { Spacing = 8 };
        foreach (Control control in controls) stack.Children.Add(control);
        return stack;
    }

    private static TextBlock Text(string value, string? classes = null)
    {
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        if (!string.IsNullOrEmpty(classes)) text.Classes.Add(classes);
        return text;
    }

    private static TextBox Input(string value, string watermark)
    {
        var input = new TextBox { Text = value, Watermark = watermark };
        input.Classes.Add("prime-input");
        PrimeAccessibility.SetName(input, watermark);
        return input;
    }

    private static TextBlock InlineValidation()
        => new()
        {
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap,
            Classes = { "prime-status-text" }
        };

    private static TextBlock FieldError()
        => new()
        {
            Text = "",
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap,
            Classes = { "prime-field-error-text", "prime-status-error" }
        };

    private static Control FormField(string label, Control input,
        TextBlock error)
    {
        var field = new StackPanel { Spacing = 4 };
        field.Children.Add(Text(label, "prime-label"));
        field.Children.Add(input);
        field.Children.Add(error);
        return field;
    }

    private static bool ValidateCredentials(string address, string secret,
        TextBox email, TextBlock emailError, TextBox password,
        TextBlock passwordError, TextBlock validation)
    {
        ClearFieldError(email, emailError);
        ClearFieldError(password, passwordError);
        if (!ValidateEmail(address, email, emailError, validation)) return false;
        if (String.IsNullOrWhiteSpace(secret))
        {
            ShowFieldError(password, passwordError, "Enter your password.");
            return false;
        }
        validation.IsVisible = false;
        return true;
    }

    private static bool ValidateRegistration(string address, string secret,
        string displayName, TextBox email, TextBlock emailError,
        TextBox password, TextBlock passwordError, TextBox displayNameInput,
        TextBlock displayNameError, TextBlock validation)
    {
        ClearFieldError(displayNameInput, displayNameError);
        if (!ValidateCredentials(address, secret, email, emailError,
            password, passwordError, validation)) return false;
        if (String.IsNullOrWhiteSpace(displayName))
        {
            ShowFieldError(displayNameInput, displayNameError,
                "Choose a display name.");
            return false;
        }
        if (displayName.Length is < 1 or > 16)
        {
            ShowFieldError(displayNameInput, displayNameError,
                "Use 1–16 characters for your display name.");
            return false;
        }
        validation.IsVisible = false;
        return true;
    }

    private static bool ValidateEmail(string address, TextBox email,
        TextBlock emailError, TextBlock validation)
    {
        int separator = address.IndexOf('@');
        if (separator <= 0 || separator == address.Length - 1)
        {
            ShowFieldError(email, emailError, "Enter a valid email address.");
            return false;
        }
        ClearFieldError(email, emailError);
        validation.IsVisible = false;
        return true;
    }

    private static void ClearFieldError(TextBox field, TextBlock error)
    {
        error.Text = "";
        error.IsVisible = false;
        field.Classes.Remove("prime-field-error");
        field.ClearValue(TemplatedControl.BorderBrushProperty);
    }

    private static void ShowFieldError(TextBox field, TextBlock error,
        string message)
    {
        error.Text = message;
        error.IsVisible = true;
        field.Classes.Add("prime-field-error");
        field.BorderBrush = GuiTheme.ErrorBrush;
        PrimeAccessibility.SetStatus(error, message, PrimeStatusKind.Error);
    }

    private static void SetInlineStatus(TextBlock status, string message,
        PrimeStatusKind kind)
    {
        status.Text = message;
        status.IsVisible = true;
        PrimeAccessibility.SetStatus(status, message, kind);
    }

    private static AvaloniaButton MakeButton(string label, Action action,
        bool primary = false, bool quiet = false)
    {
        AvaloniaButton button = PrimeControlFactory.Button(label, action,
            primary, quiet);
        PrimeShellNavigationAdapter.ApplyTouchTarget(button, primary);
        if (!String.IsNullOrWhiteSpace(label))
            PrimeAccessibility.SetName(button, label);
        return button;
    }

    private static AvaloniaButton LegacyButtonAlias(string label, Action action)
    {
        AvaloniaButton button = MakeButton("", action, quiet: true);
        button.Content = new LegacyButtonLabel(label);
        button.IsVisible = false;
        return button;
    }

    private sealed class LegacyButtonLabel
    {
        private readonly string _label;

        internal LegacyButtonLabel(string label) => _label = label;

        public override string ToString() => _label;
    }

    private static Border Card(Control child)
    {
        return new PrimeCard(child);
    }

    private static string DisplayOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string FormatDecimal(decimal? value)
        => value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—";

}
