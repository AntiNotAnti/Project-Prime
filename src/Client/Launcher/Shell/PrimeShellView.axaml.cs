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
    private readonly PlayController _play;
    private readonly ClientOnlineRuntime _online;
    private readonly bool _ownsOnline;
    private readonly PlayPresentationState _playPresentation = new();
    private readonly HunterLicenseController _license;
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
    private PrimeShellNavigationAdapter _navigationAdapter = null!;
    private PrimeMotionLease? _routeMotion;
    private PrimeMotionLease? _overlayMotion;
    private PrimeRoute _renderedRoute = PrimeRoute.Gateway;
    private CancellationTokenSource _lifetime = new();
    private readonly CancellationTokenSource _restoreLifetime = new();
    private bool _restoreStarted;
    private bool _active;
    private bool _disposed;
    private bool _finished;
    private bool _theatreLoaded;
    private bool _previewCatchupStarted;
    private bool _playRefreshPending;
    private PlayState? _capturePlayState;
    private GatewayState? _captureGatewayState;
    private PlayerId? _capturePendingConfirmationPlayerId;
    private bool _captureExpandAdvancedNetwork;
    private HunterLicensePageState? _captureLicenseState;
    private IReadOnlyList<HunterDossier>? _captureLicenseHunters;
    private bool _captureHunterPreviewFailure;
    private Guid? _lobbyConfigurePendingId;
    private Update.UpdateInfo? _update;
    private Update.UpdateStatus _updateStatus = Update.UpdateCoordinator.Shared.Status;
    private string? _mapDraft;
    private MatchMode? _modeDraft;
    private int? _botCountDraft;
    private string? _timeLimitDraft;
    private string? _pointGoalDraft;
    private Hunter _selectedLicenseHunter = Hunter.Samus;
    private BeamType _selectedArmoryWeapon = BeamType.PowerBeam;
    private readonly Dictionary<Hunter, string> _hunterPreviewPaths = new();
    private readonly HashSet<Hunter> _hunterPreviewLoads = new();
    private readonly Dictionary<Hunter, DateTimeOffset> _hunterPreviewRetryAfter = new();
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
    private string _settingsFocusCategory = "Gameplay";
    private string? _overlayModalId;
    private KeyboardNavigationMode _overlayTabNavigationBeforeOpen;
    private bool _overlayTabNavigationCaptured;
    private const string SeatOfferModalPrefix = "seat-offer:";
    private const string SeatOfferAvailableAnnouncement =
        "A player seat is available. Accept or decline before it expires.";

    public LaunchPlan Plan { get; private set; }
    internal ClientOnlineRuntime Online => _online;
    public event EventHandler<LaunchPlan>? Done;

    public PrimeShellView(MenuSettings settings, IReadOnlyList<string> rooms,
        bool restoreOnActivate = true, bool ignoreGameFileGate = false,
        bool captureMode = false, PrimeShellCaptureState? captureState = null,
        ClientOnlineRuntime? onlineRuntime = null, bool showTitleScreen = true,
        PrimeTitleScreenCaptureState? titleCaptureState = null,
        Func<CancellationToken, Task<bool>>? restoreSession = null)
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
        _navigationAdapter = new PrimeShellNavigationAdapter(this);
        Resources["PrimeReducedMotion"] = LauncherPrefs.ReducedMotion;

        PrimeAccessibility.SetName(AccountButton, "Open account menu");
        PrimeAccessibility.SetName(SettingsButton, "Open settings");
        PrimeAccessibility.SetName(InputHintText, "Controller and keyboard controls");
        PrimeAccessibility.SetStatus(ConnectionText, "Offline");

        _gateway = new GatewayController(_shell);
        _restoreSession = restoreSession ?? _gateway.RestoreAsync;
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
        _play.Launch += LaunchRequested;
        _license.Changed += LicenseChanged;
        _rankings.Changed += RankingsChanged;
        _theatre.Changed += TheatreChanged;
        _theatre.Launch += LaunchRequested;
        Update.UpdateCoordinator.Shared.StatusChanged += UpdateStatusChanged;
        SizeChanged += (_, e) => ApplyResponsiveLayout(e.NewSize.Width, e.NewSize.Height);
        AccountButton.Click += (_, _) => ShowAccountMenu();
        SettingsButton.Click += (_, _) => Navigate(PrimeRoute.Settings);

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
        _capturePendingConfirmationPlayerId = captureState.PendingConfirmationPlayerId;
        _capturePlayState = captureState.Play;
        _captureExpandAdvancedNetwork = captureState.ExpandAdvancedNetwork;
        _captureLicenseState = captureState.License;
        _captureLicenseHunters = captureState.Hunters;
        _captureHunterPreviewFailure = captureState.HunterPreviewFailure;
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
    internal bool SeatOfferOverlayVisible => IsSeatOfferOverlayOpen();
    internal bool ShellInputEnabled => ShellGrid.IsEnabled
        && ShellGrid.IsHitTestVisible;

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
        if (_disposed || !_titleLifecycle.BeginDismissal()) return;
        PrimeTitleScreenView? title = _titleScreen;
        if (title is null)
        {
            CompleteTitleDismissal();
            return;
        }
        title.SetPhase(PrimeTitleScreenPhase.Dismissing);
        _ = DismissTitleAsync(title);
    }

    private async Task DismissTitleAsync(PrimeTitleScreenView title)
    {
        try
        {
            await title.FadeOutAsync(_titleLifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_titleLifetime.IsCancellationRequested)
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
            GamepadDesktop.PollForMenu();
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
        // Desktop polling owns the freshest physical state. Sample only after
        // it runs, then seed held buttons so activation/reconnect is edge-free.
        if (!_captureMode)
            GamepadDesktop.PollForMenu();
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
            RunCommand("Restore session", async () =>
            {
                try
                {
                    if (await _restoreSession(_restoreLifetime.Token).ConfigureAwait(false))
                        PostUi(() => _shell.Navigator.NavigateRoot(PrimeRoute.Play));
                }
                finally
                {
                    PostUi(() =>
                    {
                        if (!_disposed) MarkTitleReady();
                    });
                }
            });
        }
        else if (_titleCaptureState is null)
            MarkTitleReady();
        // Android returns here after a match and after the install-source
        // settings screen. Retrying on every activation lets a staged APK be
        // submitted without requiring a second background check.
        if (_restoreOnActivate) _ = TryInstallStaged();
    }

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
        _inputTimer.Stop();
        _lifetime.Cancel();
        _lifetime.Dispose();
        _lifetime = new CancellationTokenSource();
    }

    internal PlayController Play => _play;
    internal bool CaptureMode => _captureMode;
    internal int HunterPreviewLoadStarts => _hunterPreviewLoadStarts;
    internal int WeaponPreviewLoadStarts => _weaponPreviewLoadStarts;
    internal GatewayState? CaptureGatewayState => _captureGatewayState;
    internal PlayState? CapturePlayState => _capturePlayState;
    internal HunterLicensePageState? CaptureLicenseState => _captureLicenseState;
    internal void SetMenuInputEnabled(bool enabled)
    {
        if (enabled && _active && !_captureMode) _inputTimer.Start();
        else _inputTimer.Stop();
    }

    public void ShowMatchOutcome(MatchRunResult result)
    {
        if (result.Reason is MatchExitReason.Completed or MatchExitReason.LeftMatch) return;
        _shell.Notify(PrimeNotificationKind.Error,
            (result.Reason == MatchExitReason.FailedToStart ? "Match could not start. " : "Match connection lost. ")
            + result.Message + " Return to your lobby and try again.");
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
        _shell.Navigator.NavigateRoot(_shell.HasNetworkIdentity ? PrimeRoute.Play : PrimeRoute.Gateway);

    }

    /// <summary>Handle Escape or Android back without ending a match.</summary>
    public bool GoBack()
    {
        if (IsTitleBlocking) return true;
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
    public void ShowPauseMenu(Scene scene, Action onResume, Action onLeave, Action onQuit)
    {
        ArgumentNullException.ThrowIfNull(onResume);
        ArgumentNullException.ThrowIfNull(onLeave);
        ArgumentNullException.ThrowIfNull(onQuit);
        var view = new PauseMenuView(offerWindowMode: false);
        void Close() => CloseOverlay();
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
        view.SettingsRequested += (_, _) =>
        {
            Close();
            ShowOverlay(new SettingsView(_settings, inGame: true, scene: scene,
                identity: SettingsIdentityContext.From(_shell,
                    () => CloseAndNavigate(PrimeRoute.Hunter))),
                "pause-settings");
        };
        ShowOverlay(view, "pause-menu");
        view.FocusResume();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (HandleTitleKeyDown(e.Key))
        {
            e.Handled = true;
            return;
        }
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
        if (HandleTitleKeyUp(e.Key))
        {
            e.Handled = true;
            return;
        }
        base.OnKeyUp(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (HandleTitlePointerPressed(e.Pointer.Type))
        {
            e.Handled = true;
            return;
        }
        _shell.SetLastInputDevice(e.Pointer.Type == PointerType.Mouse
            ? PrimeInputDevice.KeyboardMouse
            : PrimeInputDevice.Touch);
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (HandleTitlePointerReleased())
        {
            e.Handled = true;
            return;
        }
        base.OnPointerReleased(e);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
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
        _play.Launch -= LaunchRequested;
        _license.Changed -= LicenseChanged;
        _rankings.Changed -= RankingsChanged;
        _theatre.Changed -= TheatreChanged;
        _theatre.Launch -= LaunchRequested;
        Update.UpdateCoordinator.Shared.StatusChanged -= UpdateStatusChanged;
        _routeMotion?.Dispose();
        _overlayMotion?.Dispose();
        if (_overlayTabNavigationCaptured)
        {
            KeyboardNavigation.SetTabNavigation(OverlayRoot,
                _overlayTabNavigationBeforeOpen);
            _overlayTabNavigationCaptured = false;
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
        RememberCurrentNavigationFocus();
        _routeMotion?.Dispose();
        _routeMotion = null;
        if (route == PrimeRoute.Armory)
            _routeViewState.SelectHunterSection(HunterSection.Arsenal);
        if (PageHost.Content != null)
            _routeViewState.CaptureScroll(_renderedRoute, PageScroller.Offset);
        _padCombo = null;
        _padComboOriginalIndex = -1;
        _hunterPadCombo = null;
        _hunterPadSelection = null;
        _activeLobbyChatPanel = null;
        _play.SetHandoffEnabled(normalizedRoute == PrimeRoute.Play && _shell.HasNetworkIdentity);
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
        PlayerId? pendingConfirmationPlayerId = _capturePendingConfirmationPlayerId
            ?? _gateway.PendingConfirmationPlayerId;
        var root = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        var content = Stack();
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.Width = 580;
        content.MaxWidth = 580;
        content.Margin = new Thickness(0, 16);

        var mark = new PrimeDiamond
        {
            Width = 46,
            Height = 46,
            StrokeBrush = GuiTheme.AccentBrush,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(mark);
        var heading = PrimeControlFactory.PageHeading("Project Prime",
            "ENTER THE ARENA",
            "Play as a guest or sign in to continue your career.");
        CenterHeading(heading);
        content.Children.Add(heading);

        var email = Input("", "Email address");
        var password = Input("", "Password");
        password.PasswordChar = '•';
        var registrationEmail = Input("", "Email address");
        var registrationPassword = Input("", "Password");
        registrationPassword.PasswordChar = '•';
        var displayName = Input(LauncherPrefs.PlayerName, "Display name");
        var confirmationPlayer = Input(pendingConfirmationPlayerId?.ToString() ?? "",
            "Player ID from registration");
        var confirmation = Input("", "Confirmation code");
        PrimeSectionPanel confirmationPanel = null!;

        var signIn = Stack(
            Text("Sign in", "prime-heading"),
            Text("Email", "prime-label"), email,
            Text("Password", "prime-label"), password);
        signIn.IsVisible = gateway.Phase == GatewayPhase.SigningIn;
        signIn.Children.Add(MakeButton("Sign in", () =>
        {
            string address = email.Text?.Trim() ?? "";
            string secret = password.Text ?? "";
            password.Text = "";
            RunCommand("Sign in", async () =>
            {
                if (await _gateway.SignInAsync(address, secret, _lifetime.Token).ConfigureAwait(false))
                    PostUi(() => _shell.Navigator.NavigateRoot(PrimeRoute.Play));
            });
        }));

        AvaloniaButton signInToggle = null!;
        signInToggle = MakeButton("Sign In", () =>
        {
            signIn.IsVisible = true;
            signInToggle.IsVisible = false;
        });

        var registration = Stack(
            Text("Create account", "prime-heading"),
            Text("Email", "prime-label"), registrationEmail,
            Text("Password", "prime-label"), registrationPassword,
            Text("Display name", "prime-label"), displayName);
        registration.IsVisible = gateway.Phase == GatewayPhase.Registering;
        registration.Children.Add(MakeButton("Register", () =>
        {
            string address = registrationEmail.Text?.Trim() ?? "";
            string secret = registrationPassword.Text ?? "";
            string name = displayName.Text?.Trim() ?? "";
            registrationPassword.Text = "";
            RunCommand("Register", async () =>
            {
                AccountRegistration? result = await _gateway.RegisterAsync(address, secret, name, _lifetime.Token)
                    .ConfigureAwait(false);
                if (result?.ConfirmationRequired == true)
                    PostUi(() =>
                    {
                        confirmationPlayer.Text = result.PlayerId.ToString();
                        confirmationPanel.IsVisible = true;
                    });
            });
        }));
        AvaloniaButton registrationToggle = null!;
        registrationToggle = MakeButton("Create Account", () =>
        {
            registration.IsVisible = true;
            registrationToggle.IsVisible = false;
        }, quiet: true);

        var confirmationActions = new WrapPanel { Orientation = Orientation.Horizontal };
        confirmationActions.Children.Add(MakeButton("Confirm", () =>
        {
            RunCommand("Confirm email", async () =>
            {
                string rawPlayer = confirmationPlayer.Text?.Trim() ?? "";
                if (!Guid.TryParse(rawPlayer, out Guid playerGuid) || playerGuid == Guid.Empty)
                    throw new InvalidOperationException("Enter the Player ID returned during registration.");
                await _gateway.ConfirmEmailAsync(new PlayerId(playerGuid),
                    confirmation.Text?.Trim() ?? "", _lifetime.Token).ConfigureAwait(false);
            });
        }, primary: true));
        confirmationActions.Children.Add(MakeButton("Resend", () => RunCommand("Resend confirmation",
            () => _gateway.ResendConfirmationAsync(registrationEmail.Text?.Trim() ?? "", _lifetime.Token))));
        confirmationPanel = PrimeControlFactory.SectionPanel(Stack(
            Text("Confirm email", "prime-heading"),
            Text("Enter the Player ID returned by registration and the code delivered to your email.", "prime-muted"),
            Text("Player ID", "prime-label"), confirmationPlayer,
            Text("Confirmation code", "prime-label"), confirmation,
            confirmationActions));
        confirmationPanel.IsVisible = pendingConfirmationPlayerId.HasValue
            || gateway.Phase == GatewayPhase.Confirming;

        // Capture fixtures select the concrete panel that their injected
        // GatewayState describes. The normal GatewayState starts with both
        // panels hidden and keeps these toggles as the user-facing entry path.
        if (_captureGatewayState != null)
        {
            signInToggle.IsVisible = gateway.Phase != GatewayPhase.SigningIn;
            registrationToggle.IsVisible = gateway.Phase != GatewayPhase.Registering;
            if (gateway.Phase == GatewayPhase.Confirming)
            {
                registrationToggle.IsVisible = false;
                registration.IsVisible = false;
            }
        }

        var guestPanel = PrimeControlFactory.SectionPanel(Stack(
            Text("Play now", "prime-heading"),
            Text("Use a temporary profile. Career progress requires an account.", "prime-body"),
            MakeButton("Play as Guest", () => RunCommand("Guest access", async () =>
        {
            if (await _gateway.UseGuestAsync(_lifetime.Token).ConfigureAwait(false))
                PostUi(() => _shell.Navigator.NavigateRoot(PrimeRoute.Play));
        }), primary: true)));
        guestPanel.BorderBrush = GuiTheme.WarmBrush;

        var cardContent = Stack(guestPanel, signInToggle, signIn, registrationToggle, registration,
            confirmationPanel);
        var card = Card(cardContent);
        card.HorizontalAlignment = HorizontalAlignment.Stretch;
        var cardHost = new Border { Child = card, HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = 580 };
        content.Children.Add(cardHost);
        content.Children.Add(Text(PrimeRoutePresentation.GatewaySummary(
            gateway.Phase, gateway.Message),
            gateway.Phase == GatewayPhase.Failed ? "prime-body" : "prime-muted"));
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
        root.Children.Add(content);
        root.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width > 0)
            {
                double width = Math.Min(580, Math.Max(0, e.NewSize.Width - 24));
                content.Width = width;
                cardHost.Width = width;
            }
        };
        return root;
    }

    private Control BuildGameFilesPage()
    {
        var status = Text(GameFiles.Problem()
            ?? "Game files are ready. You can re-select your cartridge dump if repair is needed.",
            "prime-muted");
        var root = Stack(
            Text("Metroid Prime Hunters game files", "prime-heading"),
            Text("Project Prime needs your own cartridge dump. It extracts only the local files the game needs; the ROM is not uploaded or bundled.", "prime-body"),
            status,
            MakeButton(GameFiles.Ready ? "Repair from .nds file" : "Choose your .nds file",
                () => RunCommand("Set up game files", () => ChooseGameFilesAsync(status)),
                primary: true));
        if (GameFiles.Ready)
            root.Children.Add(MakeButton("Back", () => RenderRoute(_shell.CurrentRoute), quiet: true));
        return Card(root);
    }

    private async Task ChooseGameFilesAsync(TextBlock status)
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
            bool ready = await Task.Run(() => GameFiles.RunSetup(path, line =>
                PostUi(() => status.Text = TailStatus(status.Text, line))), _lifetime.Token)
                .ConfigureAwait(false);
            if (!ready) throw new InvalidOperationException(GameFiles.Problem() ?? "Game-file setup did not finish.");
            GameFiles.ApplyPaths();
            string[] rooms = ThumbnailGenerator.MultiplayerRooms().ToArray();
            _rooms.Clear();
            _rooms.AddRange(rooms);
            _play.SetMaps(rooms);
            if (ThumbnailHost.CanRender)
            {
                await ThumbnailHost.RenderMissingAsync(line =>
                    PostUi(() => status.Text = TailStatus(status.Text, line))).ConfigureAwait(false);
            }
            PostUi(() =>
            {
                status.Text = "Game files are ready.";
                RenderRoute(_shell.CurrentRoute);
            });
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
        return PlayPresentation.Build(new PlayPresentationContext(
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
            TrackChatPanel: panel => _activeLobbyChatPanel = panel));
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
                "The Node created the lobby but did not publish its authoritative snapshot.");

        string mapKey = draft.MapKey;
        if (string.IsNullOrWhiteSpace(mapKey))
            mapKey = _play.AvailableMaps.FirstOrDefault() ?? "";
        await _play.ConfigureLobbyAsync(mapKey, draft.Mode, draft.BotCount, rules,
            _lifetime.Token).ConfigureAwait(false);
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
        await _play.ConfigureLobbyAsync(draft.MapKey, draft.Mode, draft.BotCount, rules,
            _lifetime.Token).ConfigureAwait(false);
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

    private Control BuildLobbyDeploymentBanner(LobbySnapshot lobby, int playerCount, bool owner)
    {
        var identity = Stack(Text(lobby.Phase.ToString().ToUpperInvariant(), "prime-label"),
            Text($"{lobby.Name}  ·  {playerCount}/{lobby.PlayerLimit} players", "prime-body"));
        identity.HorizontalAlignment = HorizontalAlignment.Right;

        var content = Stack();
        bool configurePending = _lobbyConfigurePendingId == lobby.LobbyId;
        IReadOnlyList<string> availableMaps = _play.AvailableMaps;
        if (owner && lobby.Phase == LobbyPhase.Open
            && _play.MapCatalogState == NodeMapCatalogState.Available)
        {
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            TextBlock headerLabel = Text(
                "OWNER MATCH SETTINGS  ·  APPLYING CHANGES RESETS READINESS", "prime-label");
            header.Children.Add(headerLabel);
            header.Children.Add(identity);
            Grid.SetColumn(identity, 1);
            header.SizeChanged += (_, e) =>
            {
                bool narrow = e.NewSize.Width > 0 && e.NewSize.Width < 720;
                header.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,Auto");
                header.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto");
                Grid.SetColumn(identity, narrow ? 0 : 1);
                Grid.SetRow(identity, narrow ? 1 : 0);
                identity.HorizontalAlignment = narrow
                    ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            };
            content.Children.Add(header);

            string selectedMap = _mapDraft != null
                && availableMaps.Contains(_mapDraft, StringComparer.Ordinal)
                    ? _mapDraft : availableMaps.Contains(lobby.MapKey, StringComparer.Ordinal)
                        ? lobby.MapKey : availableMaps[0];
            var map = new ComboBox
            {
                ItemsSource = availableMaps,
                SelectedItem = selectedMap,
                Tag = ControllerComboSelector.Map,
                MinHeight = 40,
                IsEnabled = !configurePending
            };
            var mode = new ComboBox
            {
                ItemsSource = Enum.GetValues<MatchMode>(),
                SelectedItem = _modeDraft ?? lobby.Mode,
                Tag = ControllerComboSelector.Mode,
                MinHeight = 40,
                IsEnabled = !configurePending
            };
            int maximumBots = Math.Max(0, lobby.PlayerLimit - playerCount);
            int selectedBots = Math.Clamp(_botCountDraft ?? lobby.BotCount, 0, maximumBots);
            var bots = new ComboBox
            {
                ItemsSource = Enumerable.Range(0, maximumBots + 1).ToArray(),
                SelectedItem = selectedBots,
                Tag = ControllerComboSelector.Bots,
                MinHeight = 40,
                IsEnabled = !configurePending
            };
            var time = Input(_timeLimitDraft ?? FormatLobbyTimeEditor(lobby.TimeLimitSeconds),
                "Default or m:ss");
            time.MinHeight = 40;
            time.IsEnabled = !configurePending;
            MatchMode selectedMode = mode.SelectedItem is MatchMode initialMode
                ? initialMode : lobby.Mode;
            var pointGoal = Input(PointGoalApplies(selectedMode)
                    ? _pointGoalDraft ?? FormatLobbyPointGoalEditor(lobby.PointGoal) : "N/A",
                "Default or points");
            pointGoal.MinHeight = 40;
            pointGoal.IsEnabled = !configurePending && PointGoalApplies(selectedMode);

            map.SelectionChanged += (_, _) => _mapDraft = map.SelectedItem as string;
            mode.SelectionChanged += (_, _) =>
            {
                _modeDraft = mode.SelectedItem is MatchMode value ? value : null;
                bool applies = _modeDraft is { } draftMode && PointGoalApplies(draftMode);
                pointGoal.Text = applies
                    ? _pointGoalDraft ?? FormatLobbyPointGoalEditor(lobby.PointGoal) : "N/A";
                pointGoal.IsEnabled = !configurePending && applies;
            };
            bots.SelectionChanged += (_, _) =>
                _botCountDraft = bots.SelectedItem is int value ? value : null;
            time.TextChanged += (_, _) => _timeLimitDraft = time.Text;
            pointGoal.TextChanged += (_, _) =>
            {
                if (pointGoal.IsEnabled) _pointGoalDraft = pointGoal.Text;
            };
            map.LostFocus += PlayEditorLostFocus;
            mode.LostFocus += PlayEditorLostFocus;
            bots.LostFocus += PlayEditorLostFocus;
            time.LostFocus += PlayEditorLostFocus;
            pointGoal.LostFocus += PlayEditorLostFocus;
            ToolTip.SetTip(map, "Choose a map hosted by this Node and installed locally.");
            ToolTip.SetTip(bots, "Bots reserve player slots and are frozen into the match roster.");
            ToolTip.SetTip(time, "Use Default, seconds, or m:ss. Allowed range: 1 second to 60 minutes.");
            ToolTip.SetTip(pointGoal,
                "Point limit is 1-65535. Battle points usually come from clean kills; Survival uses this as lives. Objective-time modes use N/A.");

            var editors = new WrapPanel { Orientation = Orientation.Horizontal };
            editors.Children.Add(LobbySetting("MAP", map, 300));
            editors.Children.Add(LobbySetting("MODE", mode, 180));
            editors.Children.Add(LobbySetting("BOTS", bots, 90));
            editors.Children.Add(LobbySetting("TIME LIMIT", time, 150));
            editors.Children.Add(LobbySetting("POINT LIMIT", pointGoal, 145));
            AvaloniaButton? apply = null;
            apply = MakeButton("APPLY MATCH SETTINGS", () =>
            {
                string mapKey = map.SelectedItem as string ?? "";
                MatchMode matchMode = mode.SelectedItem is MatchMode selectedMode
                    ? selectedMode : lobby.Mode;
                int botCount = bots.SelectedItem is int selectedBotCount
                    ? selectedBotCount : lobby.BotCount;
                if (!TryParseLobbyTimeLimit(time.Text, out int? timeLimitSeconds))
                    throw new InvalidOperationException(
                        "Time limit must be Default, seconds, or m:ss between 0:01 and 60:00.");
                int? pointLimit = null;
                if (PointGoalApplies(matchMode)
                    && !TryParseLobbyPointGoal(pointGoal.Text, out pointLimit))
                    throw new InvalidOperationException(
                        "Point limit must be Default or a whole number between 1 and 65535.");
                _mapDraft = mapKey;
                _modeDraft = matchMode;
                _botCountDraft = botCount;
                _timeLimitDraft = FormatLobbyTimeEditor(timeLimitSeconds);
                _pointGoalDraft = FormatLobbyPointGoalEditor(pointLimit);
                BeginLobbyConfigure(lobby.LobbyId, mapKey, matchMode, botCount,
                    timeLimitSeconds, pointLimit, map, mode, bots, time, pointGoal, apply!);
            }, primary: true);
            apply.MinHeight = 40;
            apply.MinWidth = 190;
            apply.IsEnabled = !configurePending;
            apply.VerticalAlignment = VerticalAlignment.Bottom;
            editors.Children.Add(apply);
            content.Children.Add(editors);
            if (configurePending)
                content.Children.Add(Text("Applying match settings…", "prime-muted"));
        }
        else
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("2*,*,Auto"),
                ColumnSpacing = 18,
                VerticalAlignment = VerticalAlignment.Center
            };
            var deployment = Stack(Text("DEPLOYMENT MAP", "prime-label"),
                Text(DisplayMapKey(lobby.MapKey), "prime-heading"));
            var match = Stack(Text("MATCH FORMAT", "prime-label"),
                Text(FormatMode(lobby.Mode), "prime-body"));
            grid.Children.Add(deployment);
            grid.Children.Add(match);
            grid.Children.Add(identity);
            Grid.SetColumn(match, 1);
            Grid.SetColumn(identity, 2);
            grid.SizeChanged += (_, e) =>
            {
                bool narrow = e.NewSize.Width > 0 && e.NewSize.Width < 720;
                grid.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "2*,*,Auto");
                grid.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto,Auto" : "Auto");
                grid.RowSpacing = narrow ? 8 : 0;
                Grid.SetColumn(match, narrow ? 0 : 1);
                Grid.SetColumn(identity, narrow ? 0 : 2);
                Grid.SetRow(deployment, 0);
                Grid.SetRow(match, narrow ? 1 : 0);
                Grid.SetRow(identity, narrow ? 2 : 0);
                identity.HorizontalAlignment = narrow
                    ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            };
            content.Children.Add(grid);
            if (owner && lobby.Phase == LobbyPhase.Open)
                content.Children.Add(Text(_play.MapCatalogMessage, "prime-muted"));
        }
        Border banner = PrimeControlFactory.SectionPanel(content);
        banner.BorderBrush = GuiTheme.AccentBrush;
        banner.BorderThickness = new Thickness(3, 1, 1, 1);
        return banner;
    }

    private static Control LobbySetting(string label, Control editor, double width)
    {
        var field = Stack(Text(label, "prime-label"), editor);
        field.Width = width;
        field.Margin = new Thickness(0, 0, 10, 8);
        return field;
    }

    private static Control BuildLobbyRoster(string title, IReadOnlyList<LobbyMember> members,
        LobbySnapshot lobby, Guid? sessionId, IBrush accent)
    {
        var roster = Stack();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        TextBlock heading = Text(title, "prime-heading");
        heading.Foreground = accent;
        TextBlock count = Text($"{members.Count} slots occupied", "prime-label");
        header.Children.Add(heading);
        header.Children.Add(count);
        Grid.SetColumn(count, 1);
        roster.Children.Add(header);

        foreach (LobbyMember member in members)
        {
            bool own = sessionId.HasValue && member.SessionId == sessionId.Value;
            string role = member.Observer ? "Observer"
                : member.SessionId == lobby.OwnerSessionId ? "Owner" : "Member";
            var identity = Stack(Text(member.DisplayName, "prime-body"),
                Text($"{member.Hunter}  ·  {role}{(own ? "  ·  You" : "")}", "prime-muted"));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
            row.Children.Add(identity);
            var readiness = new PrimeStatusChip(member.Observer ? "Spectating"
                : member.Ready ? "Ready" : "Choosing",
                member.Ready ? GuiTheme.AccentBrush : GuiTheme.WarmBrush);
            row.Children.Add(readiness);
            Grid.SetColumn(readiness, 1);
            Border selected = PrimeControlFactory.SelectedRow(row, own);
            selected.BorderBrush = own ? accent : GuiTheme.EdgeBrush;
            selected.BorderThickness = new Thickness(3, 1, 1, 1);
            roster.Children.Add(selected);
        }
        if (members.Count == 0)
            roster.Children.Add(new PrimeEmptyState("No members in this group."));
        return PrimeControlFactory.SectionPanel(roster);
    }

    private Control BuildLobbyCommandCenter(PlayState state, LobbySnapshot lobby,
        LobbyMember? currentMember, bool owner, IReadOnlyList<LobbyMember> observers)
    {
        string? mapPath = !string.IsNullOrWhiteSpace(lobby.MapKey)
            && ThumbnailGenerator.Exists(lobby.MapKey)
            ? ThumbnailGenerator.PathFor(lobby.MapKey) : null;
        var center = Stack(BuildPreviewStage(mapPath, 165,
            "No local map preview is available for this arena."));
        center.Children.Add(Text("ACTIVE ARENA", "prime-kicker"));
        center.Children.Add(Text(DisplayMapKey(lobby.MapKey), "prime-heading"));

        var stats = new WrapPanel { Orientation = Orientation.Horizontal };
        stats.Children.Add(PrimeControlFactory.StatTile("MODE", FormatMode(lobby.Mode)));
        stats.Children.Add(PrimeControlFactory.StatTile("TIME LIMIT",
            lobby.TimeLimitSeconds is { } seconds ? FormatDuration(seconds) : "Default"));
        stats.Children.Add(PrimeControlFactory.StatTile("POINT LIMIT",
            PointGoalApplies(lobby.Mode)
                ? lobby.PointGoal?.ToString(CultureInfo.InvariantCulture) ?? "Default" : "N/A"));
        stats.Children.Add(PrimeControlFactory.StatTile("BOTS",
            lobby.BotCount.ToString(CultureInfo.InvariantCulture)));
        stats.Children.Add(PrimeControlFactory.StatTile("SEAT POLICY",
            FormatWords(lobby.SeatPolicy.ToString())));
        center.Children.Add(stats);

        int playerCount = lobby.Members.Count(member => !member.Observer);
        int readyCount = lobby.Members.Count(member => !member.Observer && member.Ready);
        LobbyStartEligibility startEligibility = LobbyStartEligibility.Evaluate(lobby);
        var status = Stack();
        var readiness = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        var phase = Stack(Text("LOBBY STATUS", "prime-label"),
            Text(startEligibility.CanStart ? "READY TO DEPLOY" : startEligibility.Message,
                "prime-body"));
        TextBlock readySummary = Text(
            $"{readyCount}/{playerCount} ready  ·  {playerCount}/{lobby.PlayerLimit} players",
            "prime-kicker");
        readySummary.VerticalAlignment = VerticalAlignment.Center;
        readiness.Children.Add(phase);
        readiness.Children.Add(readySummary);
        Grid.SetColumn(readySummary, 1);
        status.Children.Add(readiness);

        bool configurePending = _lobbyConfigurePendingId == lobby.LobbyId;
        NodeMapCatalogState mapCatalogState = _play.MapCatalogState;
        if (owner && lobby.Phase == LobbyPhase.Open
            && mapCatalogState == NodeMapCatalogState.Available)
        {
            var configuration = new WrapPanel { Orientation = Orientation.Horizontal };
            AvaloniaButton start = MakeButton("START MATCH", () => RunCommand("Start match",
                () => _play.StartMatchAsync(_lifetime.Token)), primary: true);
            start.IsEnabled = startEligibility.CanStart && !configurePending;
            start.MinWidth = 180;
            configuration.Children.Add(start);
            status.Children.Add(configuration);
            if (configurePending)
                status.Children.Add(Text("Waiting for the Node to confirm match settings.", "prime-muted"));
        }
        else if (owner && lobby.Phase == LobbyPhase.Open)
            status.Children.Add(Text(_play.MapCatalogMessage, "prime-muted"));
        else if (lobby.Phase == LobbyPhase.Open)
            status.Children.Add(Text("The lobby owner controls map, mode, and deployment.", "prime-muted"));

        if (lobby.Mode.IsTeamMode() && observers.Count > 0)
            status.Children.Add(Text($"Observers · {String.Join(", ", observers.Select(member => member.DisplayName))}",
                "prime-muted"));
        center.Children.Add(PrimeControlFactory.SectionPanel(status));
        return PrimeControlFactory.SectionPanel(center);
    }

    private Control BuildLobbyActionStrip(PlayState state, LobbySnapshot lobby,
        LobbyMember? currentMember)
    {
        if (lobby.Phase != LobbyPhase.Open)
        {
            var recovery = Stack(Text(lobby.Phase == LobbyPhase.PostMatch
                ? "Match finished. The server is choosing the next round. You can leave the lobby here."
                : state.Message, "prime-muted"));
            if (lobby.Phase == LobbyPhase.InMatch && state.Handoff != null)
            {
                recovery.Children.Add(MakeButton("Rejoin match", () => RunCommand("Rejoin match",
                    () => _play.RejoinWorkerAsync(_lifetime.Token)), primary: true));
                recovery.Children.Add(MakeButton("Retry connection", () => RunCommand("Retry connection",
                    () => _play.RetryHandoffAsync(_lifetime.Token))));
            }
            if (lobby.Phase == LobbyPhase.PostMatch && lobby.OwnerSessionId == state.Node?.Session?.SessionId)
                recovery.Children.Add(MakeButton("Return to lobby", () => RunCommand("Return to lobby",
                    () => _play.ReturnToLobbyAsync(_lifetime.Token))));
            recovery.Children.Add(MakeButton("Leave lobby", () => RunCommand("Leave lobby",
                () => _play.LeaveLobbyAsync(_lifetime.Token)), quiet: true));
            return PrimeControlFactory.SectionPanel(recovery);
        }
        var strip = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        var hunterChoice = new ComboBox
        {
            ItemsSource = Enum.GetValues<Hunter>().Where(value => value <= Hunter.Weavel).ToArray(),
            SelectedItem = state.LobbyHunter,
            Tag = ControllerComboSelector.Hunter,
            MinWidth = 180,
            MinHeight = 44,
            IsEnabled = currentMember is { Observer: false } && lobby.Phase == LobbyPhase.Open
        };
        var hunterSelection = new DeferredControllerSelection<Hunter>(
            state.LobbyHunter, selected => RunCommand("Select lobby hunter",
                () => _play.SelectLobbyHunterAsync(selected, _lifetime.Token)));
        _hunterPadCombo = hunterChoice;
        _hunterPadSelection = hunterSelection;
        hunterChoice.SelectionChanged += (_, _) =>
        {
            if (hunterChoice.SelectedItem is Hunter selected)
            {
                if (hunterSelection.Active) hunterSelection.Preview(selected);
                else if (selected != state.LobbyHunter)
                    hunterSelection.CommitImmediate(selected);
            }
        };
        var hunter = Stack(Text("LOBBY HUNTER", "prime-label"), hunterChoice);
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        bool ready = currentMember?.Ready ?? false;
        if (currentMember is { Observer: false } && lobby.Phase == LobbyPhase.Open)
            actions.Children.Add(MakeButton(ready ? "NOT READY" : "READY", () => RunCommand(
                ready ? "Clear ready" : "Set ready",
                () => _play.SetReadyAsync(!ready, _lifetime.Token)), primary: !ready));
        if (state.Handoff != null)
            actions.Children.Add(MakeButton("REJOIN MATCH", () => RunCommand("Rejoin match",
                () => _play.RejoinWorkerAsync(_lifetime.Token)), primary: ready));
        actions.Children.Add(MakeButton("LEAVE LOBBY", () => RunCommand("Leave lobby",
            () => _play.LeaveLobbyAsync(_lifetime.Token)), quiet: true));
        strip.Children.Add(hunter);
        strip.Children.Add(actions);
        Grid.SetColumn(actions, 1);
        strip.SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width > 0 && e.NewSize.Width < 720;
            strip.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,Auto");
            strip.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto");
            Grid.SetColumn(hunter, 0);
            Grid.SetColumn(actions, narrow ? 0 : 1);
            Grid.SetRow(hunter, 0);
            Grid.SetRow(actions, narrow ? 1 : 0);
            actions.HorizontalAlignment = narrow
                ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        };
        return PrimeControlFactory.SectionPanel(strip);
    }

    private static string DisplayMapKey(string value)
        => string.IsNullOrWhiteSpace(value) ? "MAP NOT CONFIGURED"
            : value.Replace('_', ' ').ToUpperInvariant();

    private static string FormatMode(MatchMode mode) => FormatWords(mode.ToString()).ToUpperInvariant();

    private static string FormatWords(string value)
        => System.Text.RegularExpressions.Regex.Replace(value, "(?<!^)([A-Z])", " $1");

    private static string FormatDuration(int seconds)
        => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    private static string FormatLobbyTimeEditor(int? seconds)
        => seconds is { } value ? $"{value / 60}:{value % 60:00}" : "Default";

    private static bool PointGoalApplies(MatchMode mode) => mode is not
        (MatchMode.Defender or MatchMode.TeamDefender or MatchMode.PrimeHunter);

    private static string FormatLobbyPointGoalEditor(int? pointGoal)
        => pointGoal?.ToString(CultureInfo.InvariantCulture) ?? "Default";

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
        StartHunterPreview(selected.Hunter);

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
            roster.Children.Add(PrimeControlFactory.SelectedRow(button,
                dossier.Hunter == selected.Hunter));
        }

        var preview = Stack(Text("Preview", "prime-heading"),
            BuildHunterPreviewStage(selected.Hunter));
        var detail = Stack(Text(selected.Name, "prime-title"),
            Text($"Affinity weapon · {selected.AffinityWeapon}", "prime-body"));
        AddHunterBadges(detail, selected);
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
        HunterDossier profile = hunters.FirstOrDefault(dossier => dossier.IsFavorite)
            ?? hunters.FirstOrDefault(dossier => dossier.Hunter == _selectedLicenseHunter)
            ?? hunters.First();
        StartHunterPreview(profile.Hunter);

        var hero = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("210,*,260"),
            ColumnSpacing = 16
        };
        var profilePreview = Stack(BuildHunterPreviewStage(profile.Hunter),
            Text(profile.Name, "prime-heading"));
        AddHunterBadges(profilePreview, profile);
        hero.Children.Add(profilePreview);

        var identity = Stack(Text(state.License!.DisplayName, "prime-title"),
            Text(state.License.Title, "prime-muted"),
            Text($"{state.License.Points} RP · Tier {state.License.Tier}", "prime-body"),
            Text($"Joined {state.License.JoinedAt:yyyy-MM-dd}", "prime-muted"),
            Text(state.License.NextThreshold is { } next
                ? $"Next threshold · {next} RP" : "No next threshold reported.", "prime-muted"),
            Text(state.License.LastOfficialDelta is { } delta
                ? $"Last official change · {delta:+#;-#;0} RP" : "No official match delta reported.", "prime-muted"));
        hero.Children.Add(identity);

        var profileAction = Stack(Text("Profile display name", "prime-label"));
        var displayName = Input(state.License.DisplayName, "Display name");
        profileAction.Children.Add(displayName);
        profileAction.Children.Add(MakeButton("Save display name", () => RunCommand("Update display name",
            () => _license.UpdateDisplayNameAsync(displayName.Text ?? "", _lifetime.Token))));
        hero.Children.Add(profileAction);
        Grid.SetColumn(profilePreview, 0);
        Grid.SetColumn(identity, 1);
        Grid.SetColumn(profileAction, 2);
        hero.SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width > 0 && e.NewSize.Width < 900;
            hero.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "210,*,260");
            hero.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto,Auto" : "Auto");
            hero.ColumnSpacing = narrow ? 0 : 16;
            hero.RowSpacing = narrow ? 12 : 0;
            Grid.SetColumn(profilePreview, 0);
            Grid.SetColumn(identity, narrow ? 0 : 1);
            Grid.SetColumn(profileAction, narrow ? 0 : 2);
            Grid.SetRow(profilePreview, 0);
            Grid.SetRow(identity, narrow ? 1 : 0);
            Grid.SetRow(profileAction, narrow ? 2 : 0);
        };

        var overview = Stack(Text("Overview", "prime-heading"), hero);
        var stats = new WrapPanel { Orientation = Orientation.Horizontal };
        CareerTotals? totals = state.Career?.Totals;
        stats.Children.Add(PrimeControlFactory.StatTile("RANKING POINTS",
            state.License.Points.ToString(CultureInfo.InvariantCulture), $"Tier {state.License.Tier}"));
        stats.Children.Add(PrimeControlFactory.StatTile("MATCHES",
            totals?.Matches.ToString(CultureInfo.InvariantCulture) ?? "—",
            totals is null ? "Career not loaded" : $"{totals.Wins} wins"));
        stats.Children.Add(PrimeControlFactory.StatTile("WIN RATIO",
            FormatDecimal(totals?.WinRatio), totals is null ? null : $"{totals.Kills} kills"));
        stats.Children.Add(PrimeControlFactory.StatTile("KILL / DEATH",
            FormatDecimal(totals?.KillDeathRatio), totals is null ? null : $"{totals.Deaths} deaths"));
        overview.Children.Add(stats);
        if (state.Career != null)
            overview.Children.Add(PrimeControlFactory.SectionPanel(
                HunterPresentation.BuildCareer(state.Career)));
        return PrimeControlFactory.SectionPanel(overview);
    }

    private Control BuildHunterPreviewStage(Hunter hunter)
    {
        Control content;
        if (_hunterPreviewPaths.TryGetValue(hunter, out string? path))
        {
            content = new PrimeLocalImage(path, 330) { Stretch = Stretch.Uniform };
        }
        else if (_captureMode && !_captureHunterPreviewFailure)
        {
            content = Text("Preview omitted for offline capture.", "prime-muted");
        }
        else if (_hunterPreviewRetryAfter.ContainsKey(hunter))
        {
            content = Stack(Text("Preview unavailable.", "prime-muted"),
                MakeButton("Retry preview", () => RetryHunterPreview(hunter)));
        }
        else
        {
            content = Text("Generating local preview…", "prime-muted");
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
            roster.Children.Add(PrimeControlFactory.SelectedRow(button,
                weapon.Beam == selected.Beam));
        }

        var preview = Stack(Text("Preview", "prime-heading"), BuildWeaponPreviewStage(selected.Beam));
        var detail = Stack(Text(selected.Name, "prime-title"),
            Text(selected.Description, "prime-body"),
            Text($"Affinity hunters · {selected.AffinityHunters}", "prime-muted"));
        detail.Children.Add(PrimeControlFactory.Divider());
        var statTiles = new WrapPanel { Orientation = Orientation.Horizontal };
        statTiles.Children.Add(PrimeControlFactory.StatTile("UN CHARGED",
            selected.UnchargedDamage.ToString(CultureInfo.InvariantCulture), "damage"));
        statTiles.Children.Add(PrimeControlFactory.StatTile("CHARGED",
            selected.ChargedDamage.ToString(CultureInfo.InvariantCulture), "damage"));
        statTiles.Children.Add(PrimeControlFactory.StatTile("AMMO COST",
            selected.AmmoCost.ToString(CultureInfo.InvariantCulture), "per shot"));
        statTiles.Children.Add(PrimeControlFactory.StatTile("PROJECTILES",
            selected.ChargedProjectiles.ToString(CultureInfo.InvariantCulture),
            $"charged speed {selected.ChargedSpeed}"));
        detail.Children.Add(statTiles);
        root.Children.Add(ResponsiveThreeColumn(PrimeControlFactory.SectionPanel(roster),
            PrimeControlFactory.SectionPanel(preview), PrimeControlFactory.SectionPanel(detail)));
        return root;
    }

    private Control BuildWeaponPreviewStage(BeamType beam)
    {
        Control content;
        if (_weaponPreviewPaths.TryGetValue(beam, out string? path))
        {
            content = new PrimeLocalImage(path, 330) { Stretch = Stretch.Uniform };
        }
        else if (_captureMode)
        {
            content = Text("Preview omitted for offline capture.", "prime-muted");
        }
        else if (_weaponPreviewRetryAfter.ContainsKey(beam))
        {
            content = Stack(Text("Preview unavailable.", "prime-muted"),
                MakeButton("Retry preview", () => RetryWeaponPreview(beam)));
        }
        else
        {
            content = Text("Generating local preview…", "prime-muted");
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

    private void StartHunterPreview(Hunter hunter)
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
            _hunterPreviewRetryAfter[hunter] = DateTimeOffset.MaxValue;
            return;
        }
        RefreshModelPreviewIdentity();
        if (_hunterPreviewPaths.ContainsKey(hunter) || !_hunterPreviewLoads.Add(hunter)) return;
        if (_hunterPreviewRetryAfter.TryGetValue(hunter, out DateTimeOffset retryAfter))
        {
            _hunterPreviewLoads.Remove(hunter);
            if (retryAfter > DateTimeOffset.UtcNow) return;
            _hunterPreviewRetryAfter.Remove(hunter);
        }
        _hunterPreviewLoadStarts++;
        _ = LoadHunterPreviewAsync(hunter, _modelPreviewIdentity);
    }

    private async Task LoadHunterPreviewAsync(Hunter hunter, string? identity)
    {
        string? path = null;
        try
        {
            PrimePreviewImage? image = await _hunterPreviews.LoadAsync(
                hunter, _restoreLifetime.Token).ConfigureAwait(false);
            path = image?.Path;
        }
        catch (OperationCanceledException) when (_restoreLifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[previews] hunter {hunter}: {error.Message}");
        }
        PostUi(() =>
        {
            if (!String.Equals(identity, _modelPreviewIdentity, StringComparison.Ordinal)) return;
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
            _weaponPreviewRetryAfter[beam] = DateTimeOffset.MaxValue;
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
        _weaponPreviewPaths.Clear();
        _weaponPreviewLoads.Clear();
        _weaponPreviewRetryAfter.Clear();
        _previewImages.Clear();
    }

    private Control BuildTheatrePage()
    {
        TheatreState state = _theatre.State;
        if (!_theatreLoaded)
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
            }));
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
        => RankingsPresentation.Build(new RankingsPresentationContext(
            _shell.SignedIn,
            _rankings.State,
            () => Navigate(PrimeRoute.Gateway),
            RunCommand,
            ChangeRankingMetricAsync,
            ChangeRankingHunterAsync,
            next => _rankings.LoadAsync(next, _lifetime.Token)));

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
                () => Navigate(PrimeRoute.Hunter)))
        {
            Height = Math.Max(420, Bounds.Height > 0 ? Bounds.Height - 203 : 520)
        };
        var backend = Input(LauncherPrefs.BackendAddress, "Backend address");
        var backendCard = Stack(Text("Network · Advanced", "prime-heading"), backend,
            MakeButton("Save Backend", () => RunCommand("Save Backend", async () =>
            {
                if (!Uri.TryCreate(backend.Text?.Trim(), UriKind.Absolute, out Uri? uri))
                    throw new InvalidOperationException("Enter a valid Backend address.");
                await _gateway.ConfigureBackendAsync(uri, _lifetime.Token).ConfigureAwait(false);
                PostUi(() => _shell.Navigator.NavigateRoot(PrimeRoute.Gateway));
            }), primary: true));
        existing.AddNetworkAdvanced(backendCard);
        existing.ShowSection(_settingsFocusCategory);
        existing.Closed += (_, _) =>
        {
            Resources["PrimeReducedMotion"] = LauncherPrefs.ReducedMotion;
            GoBack();
        };
        existing.GameFilesRequested += (_, _) => PostUi(() =>
        {
            RouteTitle.Text = "Game files";
            PageHost.Content = BuildGameFilesPage();
        });
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
                if (routeAvailable && connected && offer is null)
                    _shell.Notify(PrimeNotificationKind.Warning,
                        "Player seat offer is no longer available.");
                else if (String.Equals(_shell.Notification?.Message,
                    SeatOfferAvailableAnnouncement, StringComparison.Ordinal))
                    _shell.ClearNotification();
                RefreshChrome();
                break;
            case PrimeSeatOfferTransition.Expired:
                CloseSeatOfferOverlay();
                _shell.Notify(PrimeNotificationKind.Warning, "Player seat offer expired.");
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
        _shell.Notify(PrimeNotificationKind.Info, SeatOfferAvailableAnnouncement);
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
                    _shell.Notify(PrimeNotificationKind.Success,
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
        => PostUi(() =>
        {
            StatusText.Text = _gateway.State.Message;
            RefreshChrome();
        });

    private void GatewayIdentityChanged(object? sender, EventArgs args)
        => PostUi(() =>
        {
            _play.CancelIdentityOperations();
            RefreshNavigation();
            if (_shell.SignedIn && _shell.CurrentRoute == PrimeRoute.Gateway)
                _shell.Navigator.NavigateRoot(PrimeRoute.Play);
        });

    private void PlayChanged(object? sender, EventArgs args)
        => PostUi(() =>
        {
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
        GatewayState gateway = _captureGatewayState ?? _gateway.State;
        bool sanitizeGatewayError = _shell.CurrentRoute == PrimeRoute.Gateway
            && gateway.Phase == GatewayPhase.Failed;
        string? notificationMessage = _shell.Notification?.Message;
        if (sanitizeGatewayError)
            notificationMessage = PrimeRoutePresentation.GatewaySummary(
                gateway.Phase, gateway.Message);
        AccountButton.Content = _shell.HasNetworkIdentity ? $"{_shell.DisplayName} ▾" : "Sign In";
        ConnectionText.Text = _shell.NodeConnected
            ? $"{_shell.NodeRegion} · Connected".TrimStart(' ', '·')
            : _shell.BackendConnected ? "Online" : "Offline";
        PrimeAccessibility.SetStatus(ConnectionText, ConnectionText.Text,
            _shell.NodeConnected || _shell.BackendConnected
                ? PrimeStatusKind.Success : PrimeStatusKind.Warning);
        StatusText.Text = _shell.BusyOperation ?? notificationMessage
            ?? DescribeUpdateStatus()
            ?? (_shell.CurrentRoute == PrimeRoute.Gateway
                ? PrimeRoutePresentation.GatewaySummary(gateway.Phase, gateway.Message)
                : "Ready");
        PrimeAccessibility.SetStatus(StatusText, StatusText.Text,
            _shell.Notification?.Kind switch
            {
                PrimeNotificationKind.Error => PrimeStatusKind.Error,
                PrimeNotificationKind.Warning => PrimeStatusKind.Warning,
                PrimeNotificationKind.Success => PrimeStatusKind.Success,
                _ => PrimeStatusKind.Info
            });
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
            NotificationText.Foreground = notification.Kind switch
            {
                PrimeNotificationKind.Success => GuiTheme.GoodBrush,
                PrimeNotificationKind.Warning => GuiTheme.WarmBrush,
                PrimeNotificationKind.Error => GuiTheme.BadBrush,
                _ => GuiTheme.TextBrush
            };
        }
        else NotificationBar.IsVisible = false;
        RefreshActionBar();
        RefreshNavigation();
    }

    private void RefreshActionBar()
    {
        ActionBar.Children.Clear();
        if (_shell.Navigator.CanGoBack)
            ActionBar.Children.Add(MakeButton("Back", () => GoBack(), quiet: true));
        if (Update.Updater.Configured && _update is { } update)
        {
            ActionBar.Children.Add(MakeButton($"Update available · {update.Tag}",
                () => RunCommand("Install update", () => DownloadAndInstall(update)),
                primary: true));
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
            _shell.Notify(PrimeNotificationKind.Warning,
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
            _shell.Notify(PrimeNotificationKind.Warning,
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
        _shell.Notify(PrimeNotificationKind.Success,
            "Update submitted. Return here after Android finishes installing, then retry Node discovery.");
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
            UpdateState.Failed => status.Message,
            _ => null
        };
    }

    private void UpdateStatusChanged(object? sender, Update.UpdateStatus status)
    {
        PostUi(() =>
        {
            _updateStatus = status;
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
        ShellGrid.RowDefinitions = new RowDefinitions(narrow ? "64,*,72" : "64,*,48");
        PageGrid.Margin = narrow
            ? PrimeLayoutMetrics.ResolveMobileContentMargin(default)
            : new Thickness(24, 20);
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
            settingsView.Height = Math.Max(420, height - 203);
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
        GamepadDesktop.PollForMenu();
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
        => _ = ExecuteCommandAsync(operation, work);

    private void BeginLobbyConfigure(Guid lobbyId, string mapKey, MatchMode mode,
        int botCount, int? timeLimitSeconds, int? pointGoal, params Control[] editors)
    {
        if (_lobbyConfigurePendingId == lobbyId) return;
        _lobbyConfigurePendingId = lobbyId;
        foreach (Control editor in editors) editor.IsEnabled = false;
        RunCommand("Configure lobby", async () =>
        {
            try
            {
                await _play.ConfigureLobbyAsync(mapKey, mode, botCount, timeLimitSeconds, pointGoal,
                    _lifetime.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                PostUi(() =>
                {
                    if (_lobbyConfigurePendingId != lobbyId) return;
                    _lobbyConfigurePendingId = null;
                    if (!_disposed && _shell.CurrentRoute == PrimeRoute.Play)
                        RenderRoute(PrimeRoute.Play);
                });
            }
        });
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
            _shell.Notify(PrimeNotificationKind.Warning,
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
            await ThumbnailHost.RenderMissingAsync(line =>
                PostUi(() => StatusText.Text = line)).ConfigureAwait(false);
            PostUi(() => _shell.Notify(PrimeNotificationKind.Success,
                "Local map previews are ready."));
        }
        catch (Exception error)
        {
            PostUi(() => _shell.Notify(PrimeNotificationKind.Warning,
                $"Map preview rendering did not finish: {error.Message}"));
        }
    }

    private async Task ExecuteCommandAsync(string operation, Func<Task> work)
    {
        if (_disposed) return;
        _shell.SetBusy(operation);
        try { await work().ConfigureAwait(false); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            PostUi(() => _shell.Notify(PrimeNotificationKind.Error, error.Message));
        }
        finally { PostUi(() => _shell.SetBusy(null)); }
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

    private static Border Card(Control child)
    {
        return new PrimeCard(child);
    }

    private static string DisplayOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string FormatDecimal(decimal? value)
        => value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—";

    private static string TailStatus(string? existing, string line)
    {
        string[] lines = ((existing ?? "") + "\n" + line)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return String.Join("\n", lines.Skip(Math.Max(0, lines.Length - 8)));
    }
}
