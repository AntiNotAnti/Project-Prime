#if MPHREAD_RMLUI_ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher.RmlUi.Pages.Community;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Launcher.RmlUi.Pages.Studio;
using MphRead.Mods.Launcher.RmlUi.Pages.Social;
using MphRead.Mods.Launcher.RmlUi.Pages.News;
using MphRead.Mods.Launcher.RmlUi.Settings;
using MphRead.Mods.Launcher.RmlUi.Setup;
using OpenTK.Windowing.GraphicsLibraryFramework;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Adventure;
using MphRead.Mods.Launcher.RmlUi.Pages.License;
using MphRead.Mods.Launcher.RmlUi.Pages.Offline;
using MphRead.Mods.Launcher.RmlUi.Presenters;
using MphRead.Mods;

namespace MphRead.Droid;

// Shared Core controllers and native documents; Android owns only the window,
// input and handoff to its existing match/legacy platform workflows.
internal sealed partial class AndroidRmlUiSession : IDisposable
{
    internal RmlUiHost Host { get; } = new();
    internal RmlUiLauncherPages Pages { get; private set; } = null!;
    private readonly MenuSettings _settings;
    private IReadOnlyList<string> _rooms;
    private readonly Action<LaunchPlan> _launch;
    private readonly Action<LaunchPlan> _connectedLobby;
    private RmlMultiplayerController _multiplayer;
    private readonly Action<RmlUiRouteArgument> _platformRoute;
    private readonly Action _quit;
    private readonly GamepadUiRouter _gamepad = new();
    private readonly AndroidRmlUiVisualPolicy _visualPolicy = new();
    private OfflineController? _offlineController;
    private AdventureController? _adventureController;
    private CommunityController? _communityController;
    private CommunityPagePresenter? _community;
    private TheatreController? _theatreController;
    private TheatrePagePresenter? _theatre;
    private StudioController? _studioController;
    private StudioPagePresenter? _studio;
    private SettingsPagePresenter? _settingsPage;
    private AndroidRmlUiHudSession? _hud;
    private SocialController? _socialController;
    private SocialPagePresenter? _social;
    private NewsController? _newsController;
    private NewsPagePresenter? _news;
    private SetupPagePresenter? _setup;
    private readonly Func<Task<Stream?>>? _pickRom;
    private readonly Queue<Action> _afterDispatch = new();
    private readonly NativeUpdateMonitor _updates = new();
    private OfflinePagePresenter? _offline;
    private AdventurePagePresenter? _adventure;
    private LicensePagePresenter? _license;
    private LicenseController? _licenseController;
    private RmlHunterSelectionPresenter? _hunter;
    private RmlUiRouteArgument _route = RmlUiRouteArgument.Home;
    private bool _disposed;

    internal AndroidRmlUiSession(string root, int width, int height, float density,
        MenuSettings settings, IReadOnlyList<string> rooms, Action<LaunchPlan> launch,
        Action<RmlUiRouteArgument> platformRoute, Action quit, Action<LaunchPlan> connectedLobby, Func<Task<Stream?>>? pickRom = null)
    {
        _settings = settings; _rooms = rooms; _launch = launch; _platformRoute = platformRoute; _quit = quit; _connectedLobby = connectedLobby;
        _pickRom = pickRom;
        if (!Host.Initialize(width, height, density, root, RmlUiRenderBackend.DrawList))
            throw new InvalidOperationException("The native Android RmlUi host refused initialization.");
        Host.Input.SetFramebufferScale(1, 1); // MotionEvent already uses surface pixels.
        Host.Input.DiagnosticsEnabled = true;
        Pages = new(Host);
        _multiplayer = CreateMultiplayer();
        _gamepad.Action += Gamepad; _gamepad.Reset();
        RefreshChrome(); Pages.ShowBaseline();
        InitializeLobby();
        if (_lobby == null) RmlSplashPage.Open(Host,Pages,new TheatreImageCache(decode: AndroidRmlUiImages.Decode,maximumDimension:2048));
    }
    private void RefreshChrome()
    {
        HubSnapshot state = HubState.Capture();
        Pages.SetText("player_name", state.PlayerName.ToUpperInvariant());
        Pages.SetText("hunter_name", state.DisplayHunter.ToString().ToUpperInvariant());
        Pages.SetText("profile_state", state.GameFilesReady ? "LOCAL PROFILE // GAME DATA READY" : "LOCAL PROFILE // SETUP REQUIRED");
        Pages.SetText("game_data_state", state.GameFilesReady ? "GAME DATA READY" : "GAME DATA NOT CONFIGURED");
        Pages.SetText("build_version", Mods.Update.BuildVersion.Display);
        Pages.SetText("renderer_name", "ANDROID // RMLUI 6.3");
        Pages.SetBool("reduce_motion", LauncherPrefs.ReduceMotion);
    }
    internal void Update()
    {
        RefreshChrome();
        _gamepad.Update(GamepadManager.Snapshot, GamepadContext.Menu, Environment.TickCount64);
        _multiplayer.Tick(); PumpLobby(); PresentQueue();
        _community?.Tick(); _theatre?.Present(); _studio?.Refresh(); _settingsPage?.Refresh(); _social?.Refresh(); _setup?.Refresh(); _news?.Refresh();
        _offline?.Refresh(); _adventure?.Present(); _license?.Refresh(); _hunter?.Update(); _hud?.Present();
        Pages.Flush(); Host.Update(); Pages.AfterUpdate();
        for (int i = 0; i < 128 && Host.TryTakeIntent(out var intent); i++)
        {
            if (_mapPicker?.Handle(intent) == true || HandleQueue(intent) || _hud?.HandleIntent(intent) == true || _admin?.Handle(intent) == true || _hunter?.Handle(intent) == true || _offline?.HandleAction(intent) == true
                || _adventure?.HandleIntent(intent) == true || _license?.Handle(intent) == true
                || _community?.HandleAction(intent) == true || _theatre?.HandleIntent(intent) == true || _studio?.Handle(intent) == true
                || _settingsPage?.HandleAction(intent) == true || _social?.Handle(intent) == true || _setup?.HandleAction(intent) == true || _news?.HandleAction(intent) == true) continue;
            if (Pages.HandleIntent(intent, out var forwarded))
            { if (forwarded.Kind != 0) Handle(forwarded); }
            else if (Pages.Manager.Accept(intent)) Handle(intent);
        }
        for (int i = 0; i < 32 && _afterDispatch.Count > 0; i++) _afterDispatch.Dequeue()();
        if (_offline?.TryTakeLaunch(out LaunchPlan offline) == true) _launch(offline);
        if (_theatre?.TryTakeLaunch(out LaunchPlan replay) == true) _launch(replay);
        if (_adventure?.TryTakeLaunch(out LaunchPlan adventure) == true) _launch(adventure);
        if (_community?.TryTakeHostRequest(out CommunityHostRequest hostRequest) == true)
        {
            ClosePresenters(); _route = RmlUiRouteArgument.Play; Pages.ShowBaseline(RmlUiMenuPage.Play);
            _multiplayer.OpenCreateForMap(hostRequest);
        }
        if (_socialController is { } social && social.TryTakeJoin(out SocialJoinRequest? join) && join != null)
        {
            // The shared coordinator owns this verified locator/admission;
            // retain Social until success or an owner-thread failure returns.
            _multiplayer.JoinSocial(join, error =>
            { if (ReferenceEquals(_socialController, social)) social.ReportJoinFailure(error); });
        }
        _updates.Tick(_setup == null && _lobby == null && _route == RmlUiRouteArgument.Home && !Pages.Suspended && Pages.Manager.ModalCount == 0);
        if (_updates.TryTakeAvailable(out var update)) { OpenSetup(required: false); _setup!.OpenLatest(update, automatic: true); }
        if (Pages.Manager.PageKey=="splash")RmlSplashPage.Layout(Host,Pages);
        else if (Pages.Manager.Page != default) Pages.PresentChrome(Pages.Manager.Page);
        _visualPolicy.Apply(Host, _settings, Host.HomeDocument, Pages.Manager.Page, Pages.Manager.Top,
            _hunter?.Document ?? default, _admin?.Document ?? default);
        _visualPolicy.ApplyRoute(Host, Host.HomeDocument, "home");
        _visualPolicy.ApplyRoute(Host, Pages.Manager.Page, Pages.Manager.PageKey);
        _visualPolicy.ApplyRoute(Host, Pages.Manager.Top, Pages.Manager.PageKey);
        _visualPolicy.ApplyRoute(Host, _hunter?.Document ?? default, "hunters");
        _visualPolicy.ApplyRoute(Host, _admin?.Document ?? default, "lobby");
    }
    private void Handle(RmlUiIntent intent)
    {
        switch (intent.Kind)
        {
            case RmlUiIntentKind.Navigate:
                if (_lobby != null) { Pages.SetText("lobby_status", "LEAVE THE LOBBY BEFORE CHANGING ACTIVITY."); break; }
                Open((RmlUiRouteArgument)intent.Argument); break;
            case RmlUiIntentKind.HunterOpen: OpenHunter(); break;
            case RmlUiIntentKind.Quit: _quit(); break;
            case RmlUiIntentKind.PlayQuick: EnsurePlay(true); break;
            case RmlUiIntentKind.PlayBrowse: EnsurePlay(false); break;
            case RmlUiIntentKind.PlayCreateOpen: EnsurePlay(false); _multiplayer.OpenCreate(); break;
            case RmlUiIntentKind.PlayCreate: _multiplayer.Create(Pages.ReadField("play_create_name"), LauncherPrefs.PlayerName); break;
            case RmlUiIntentKind.PlayJoin: _multiplayer.JoinEndpoint(Pages.ReadField("play_join_address"), false); break;
            case RmlUiIntentKind.PlayServer: _multiplayer.JoinSelected(intent.Argument); break;
            case RmlUiIntentKind.PlayNextMap: _multiplayer.NextMap(); break;
            case RmlUiIntentKind.PlayNextMode: _multiplayer.NextMode(); break;
            case RmlUiIntentKind.PlayToggleHost: _multiplayer.ToggleHost(); break;
            case RmlUiIntentKind.PlayCancel: _multiplayer.Cancel(); if (Pages.Page == RmlUiMenuPage.Home) _route = RmlUiRouteArgument.Home; break;
            case RmlUiIntentKind.LobbyMapOpen: HandleLobby(intent); break;
            case >= RmlUiIntentKind.LobbyReady and <= RmlUiIntentKind.LobbyBotLevelNext: HandleLobby(intent); break;
            case RmlUiIntentKind.OpenStudio: OpenStudio(); break;
        }
    }
    internal void Open(RmlUiRouteArgument route, bool refreshLicense = true)
    {
        if (_settingsPage != null)
        {
            _settingsPage.RequestLeave(() => _afterDispatch.Enqueue(() => { _settingsPage?.Dispose(); _settingsPage = null; Open(route, refreshLicense); }));
            return;
        }
        bool leavingSplash=Pages.Manager.PageKey=="splash";
        ClosePresenters(); _route = route;
        if(leavingSplash&&!GameFiles.Ready){OpenSetup(required:true);return;}
        switch (route)
        {
            case RmlUiRouteArgument.Home: _route = RmlUiRouteArgument.Home; Pages.ShowBaseline(RmlUiMenuPage.Home); break;
            case RmlUiRouteArgument.News:
                Pages.Suspend(); _newsController = new(new BundledNewsProvider(), new NewsEngineLinkLauncher());
                _news = new(Host, Pages.Manager, _newsController); _news.Open(); break;
            case RmlUiRouteArgument.Play: Pages.ShowBaseline(RmlUiMenuPage.Play); _multiplayer.Open(false); break;
            case RmlUiRouteArgument.Training:
                Open(RmlUiRouteArgument.Offline); _offline?.FocusTraining(); break;
            case RmlUiRouteArgument.Offline:
                Pages.Suspend(); _offlineController = new(_settings, _rooms); _offline = new(Host, Pages.Manager, _offlineController); _offline.Open(); break;
            case RmlUiRouteArgument.Adventure:
                Pages.Suspend(); _adventureController = new(_settings); _adventure = new(Host, Pages.Manager, _adventureController); _adventure.Open(); break;
            case RmlUiRouteArgument.Community:
                Pages.Suspend(); _communityController = new(new CommunityEngineBackend());
                _community = new(Host, Pages.Manager, _communityController); _community.Open(); break;
            case RmlUiRouteArgument.Settings:
                Pages.Suspend();
                _settingsPage = new(Host, Pages.Manager, _settings, new SceneGameState(new ScenePlayerRegistry()),
                    () => _afterDispatch.Enqueue(() => { _settingsPage?.Dispose(); _settingsPage = null; Open(RmlUiRouteArgument.Home); }),
                    () => _settingsPage?.RequestLeave(() => _afterDispatch.Enqueue(() => OpenSetup(required: false))),
                    editHud: () => { _hud ??= new(Host, Pages.Manager, _settingsPage!); _hud.Open(); });
                _settingsPage.Open(); break;
            case RmlUiRouteArgument.Social:
                Pages.Suspend(); _socialController = new(); _social = new(Host, Pages.Manager, _socialController);
                _social.Closed += () => Open(RmlUiRouteArgument.Home); _social.Open(); break;
            case RmlUiRouteArgument.Theatre:
                Pages.Suspend(); _theatreController = new(new TheatreEngineBackend());
                _theatre = new(Host, Pages.Manager, _theatreController, new TheatreImageCache(decode: AndroidRmlUiImages.Decode)); _theatre.Open(); break;
            case RmlUiRouteArgument.HunterLicense:
                Pages.Suspend(); _licenseController = new(); _license = new(Host, Pages.Manager, _licenseController);
                _license.Closed += () => Open(RmlUiRouteArgument.Home);
                _license.CustomizationRequested += OpenHunter; _license.Open(loadProfile: refreshLicense); break;
            default: _platformRoute(route); break;
        }
        Console.WriteLine("[rmlui-android] route " + route);
    }
    internal void OpenTraining(bool focusTrainer)
    {
        Open(RmlUiRouteArgument.Offline);
        if (focusTrainer && _offline != null)
        { Host.Update(); Host.FocusDocument(_offline.Document, "offline_training_start"); }
    }
    private void EnsurePlay(bool quick)
    {
        if (_route != RmlUiRouteArgument.Play)
        { ClosePresenters(); _route = RmlUiRouteArgument.Play; Pages.ShowBaseline(RmlUiMenuPage.Play); _multiplayer.Open(quick); }
        else if (quick) _multiplayer.QuickPlay(); else _multiplayer.Browse();
    }
    private void Connected(LaunchPlan plan)
    {
        _multiplayer.Cancel();
        if (NetSession.Active && NetSession.PersistentLobby) OpenLobby(plan.Lobby);
        else _launch(plan);
    }
    private void OpenStudio()
    {
        ClosePresenters(); _route = RmlUiRouteArgument.Community; Pages.Suspend();
        _studioController = new(new LauncherStudioBackend()); _studio = new(Host, Pages.Manager, _studioController);
        _studio.Closed += () => Open(RmlUiRouteArgument.Home); _studio.Open();
    }
    private void OpenSetup(bool required)
    {
        ClosePresenters(); Pages.Suspend();
        _setup = new(Host, Pages.Manager, () => _afterDispatch.Enqueue(() => { RefreshRoomCatalog(); Open(RmlUiRouteArgument.Home); }),
            _quit, _pickRom, required: required);
        _setup.Open();
    }
    private RmlMultiplayerController CreateMultiplayer()
    {
        var multiplayer = new RmlMultiplayerController(_rooms, (id,value) => Pages.SetText(id,value),
            (id,value) => Pages.SetBool(id,value), (id,value) => Pages.SetField(id,value), id => Pages.ReadField(id));
        multiplayer.Connected += Connected;
        return multiplayer;
    }
    private void RefreshRoomCatalog()
    {
        if (_lobby != null || NetSession.Active) return;
        _rooms = GameFiles.Ready ? ThumbnailGenerator.MultiplayerRooms().ToArray() : Array.Empty<string>();
        _multiplayer.Connected -= Connected; _multiplayer.Dispose(); _multiplayer = CreateMultiplayer();
        _rules?.ResetSession();
        _rules = new(_rooms, (id,value) => Pages.SetText(id,value), (id,value) => Pages.SetBool(id,value),
            (id,value) => Pages.SetField(id,value));
    }
    private void OpenHunter()
    {
        if (_hunter?.Active == true) return;
        _hunter?.Dispose(); _hunter = new(Host, _lobby);
        _hunter.SetPreviewStatus("ANDROID NATIVE PREVIEW IS NOT YET AVAILABLE. HUNTER AND COSMETIC CHOICES USE THE SHARED PROFILE SERVICES.");
        _hunter.Open();
    }
    internal void Back()
    {
        if (_multiplayer.QueueSnapshot.Visible) { _multiplayer.QueueLeave(); PresentQueue(); return; }
        if (_mapPicker?.Active == true) { _mapPicker.Dispose(); _mapPicker=null; return; }
        if (_hud?.Back() == true) return;
        if (_admin?.Active == true) { _admin.Back(); return; }
        if (_hunter?.Active == true) { _hunter.Dispose(); _hunter = null; return; }
        if (_setup?.Back() == true || _settingsPage?.Back() == true || _social?.Back() == true || _theatre?.Back() == true || _adventure?.Back() == true || _news?.Back() == true || Pages.Manager.Back()) return;
        if (!Pages.Suspended && Pages.Back(out var forwarded)) { if (forwarded.Kind != 0) Handle(forwarded); return; }
        if (_route != RmlUiRouteArgument.Home) { Open(RmlUiRouteArgument.Home); return; }
        _quit();
    }
    internal void ReleaseInput() { _hud?.ReleaseInput(); Host.ReleaseInput(); _gamepad.Reset(); }
    internal RmlUiTextInputBounds? CaptureHudCanvas() => _hud?.CaptureCanvasBounds();
    internal void DispatchTouch(AndroidRmlUiTouchEvent touch)
    {
        if (_hud?.Active == true && touch.Document == _hud.Document && touch.Document == Host.CurrentInputDocument)
            _hud.DispatchPointer(touch.PointerId, touch.Kind, touch.X, touch.Y, touch: true);
    }
    internal void Presented(bool usable) => _settingsPage?.ObservePresentedFrame(usable);
    internal void Dispatch(RmlUiPlatformInputEvent input)
    {
        if (_settingsPage != null && input.Kind == RmlUiPlatformInputKind.KeyDown
            && _settingsPage.TryCaptureKey(AndroidRmlUiKeys.Physical(input.Code))) return;
        if (_settingsPage != null && input.Kind == RmlUiPlatformInputKind.Wheel
            && _settingsPage.TryCaptureWheel((float)input.Delta)) return;
        if (_settingsPage != null && input.Kind == RmlUiPlatformInputKind.PointerDown
            && input.Device == RmlUiInputDevice.Pointer && _settingsPage.TryCaptureMouse(MouseButton.Left)) return;
        Host.Input.Dispatch(input);
    }
    private void Gamepad(UiAction action)
    {
        if (action == UiAction.Back) { Back(); return; }
        int key = action switch { UiAction.Accept => 2, UiAction.Up => 5, UiAction.Down => 6,
            UiAction.Left => 7, UiAction.Right => 8, UiAction.PageUp => 11, UiAction.PageDown => 12, _ => 1 };
        var mods = action == UiAction.PreviousTab ? RmlUiInputModifiers.Shift : RmlUiInputModifiers.None;
        Host.Input.Dispatch(new(Host.CurrentInputDocument, RmlUiPlatformInputKind.KeyDown, RmlUiInputDevice.Gamepad, Code: key, Modifiers: mods));
        Host.Input.Dispatch(new(Host.CurrentInputDocument, RmlUiPlatformInputKind.KeyUp, RmlUiInputDevice.Gamepad, Code: key, Modifiers: mods));
    }
    private void ClosePresenters()
    {
        _mapPicker?.Dispose(); _mapPicker=null;
        _hud?.Dispose(); _hud = null;
        _multiplayer.Cancel();
        PresentQueue();
        _admin?.Dispose(); _admin = null;
        _hunter?.Dispose(); _hunter = null;
        _offline?.Dispose(); _offline = null; _offlineController?.Dispose(); _offlineController = null;
        _adventure?.Dispose(); _adventure = null; _adventureController?.Dispose(); _adventureController = null;
        _community?.Dispose(); _community = null; _communityController?.Dispose(); _communityController = null;
        _theatre?.Dispose(); _theatre = null; _theatreController?.Dispose(); _theatreController = null;
        _studio?.Dispose(); _studio = null; _studioController?.Dispose(); _studioController = null;
        _settingsPage?.Dispose(); _settingsPage = null;
        _social?.Dispose(); _social = null; _socialController?.Dispose(); _socialController = null;
        _news?.Dispose(); _news = null; _newsController?.Dispose(); _newsController = null;
        _setup?.Dispose(); _setup = null;
        _license?.Dispose(); _license = null; _licenseController?.Dispose(); _licenseController = null;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try { _updates.Dispose(); ClosePresenters(); RetireLobby(stopConnection: Host.Active); _multiplayer.Dispose(); Pages.Dispose(); }
        finally { Host.Dispose(); }
    }
}
#endif
