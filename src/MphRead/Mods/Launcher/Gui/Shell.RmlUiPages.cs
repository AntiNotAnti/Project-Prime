#if MPHREAD_RMLUI_POC && !ANDROID
using System;
using System.Collections.Generic;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Adventure;
using MphRead.Mods.Launcher.RmlUi.Pages.InGame;
using MphRead.Mods.Launcher.RmlUi.Pages.License;
using MphRead.Mods.Launcher.RmlUi.Pages.Offline;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Launcher.RmlUi.Pages.Studio;
using MphRead.Mods.Launcher.RmlUi.Pages.Community;
using MphRead.Mods.Launcher.RmlUi.Pages.Social;
using MphRead.Mods.Launcher.RmlUi.Pages.News;
using MphRead.Mods.Launcher.RmlUi.Presenters;
using MphRead.Mods.Render;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    private static OfflinePagePresenter? _nativeOffline;
    private static OfflineController? _nativeOfflineController;
    private static AdventurePagePresenter? _nativeAdventure;
    private static AdventureController? _nativeAdventureController;
    private static LicensePagePresenter? _nativeLicense;
    private static RmlLobbyAdminPresenter? _nativeAdmin;
    private static RmlHunterSelectionPresenter? _nativeHunters;
    private static HunterSelectionController? _licenseSelection;
    private static InGamePagePresenter? _nativePause;
    private static InGameController? _nativePauseController;
    private static TheatrePagePresenter? _nativeTheatre;
    private static TheatreController? _nativeTheatreController, _nativeReturnTheatre;
    private static TheatrePlaybackPagePresenter? _nativePlayback;
    private static TheatrePlaybackController? _nativePlaybackController;
    private static StudioPagePresenter? _nativeStudio;
    private static CommunityPagePresenter? _nativeCommunity;
    private static CommunityController? _nativeCommunityController;
    private static SocialPagePresenter? _nativeSocial;
    private static NewsPagePresenter? _nativeNews;
    private static NewsController? _nativeNewsController;
    private static Action<string>? _nativeLaunchFailure;
    private static RmlUiDocumentToken _nativeLaunchDocument;

    private static bool HasNativePage => _nativeOffline != null || _nativeAdventure != null
        || _nativeLicense != null || _nativePause != null || _nativeTheatre != null || _nativePlayback != null
        || _nativeStudio != null || _nativeCommunity != null || _nativeSocial != null || _nativeSettings != null || _nativeHud != null
        || _nativeResults != null || _nativeAimResults != null || _nativeSetup != null || _nativeNews != null;
    internal static bool HasNativeHunterPreview => _nativeHunters?.Active == true;

    private static void WireNativePages()
    {
        RmlUiPrototype.BackRequested = BackNativePage;
        RmlUiPrototype.HunterPreviewDrawOverride = DrawNativeHunterPreview;
        RmlUiPrototype.GamepadActionOverride = NativeGamepadAction;
        RmlUiPrototype.InputReleaseRequested = ReleaseNativePageInput;
        RmlUiPrototype.PresentationRetiring = RetireNativePresentation;
    }

    private static void RetireNativePages(bool cancelLaunch = true)
    {
        _nativePresentationPolicy.Clear();
        CancelDeferredStudioPlaytest();
        RetireNativeQueue();
        CancelNativeSocialJoin();
        RetireNativeOwner(ref _nativeHunters);
        RetireNativeOwner(ref _nativeAdmin);
        CosmeticPreview.Loadout = null;
        CosmeticPreview.LoopDeath = false;
        CosmeticPreview.Mode = SkinContext.Biped;
        CosmeticPreview.Yaw = 0; CosmeticPreview.Zoom = 1; CosmeticPreview.DeathRequest = 0;
        RetireNativeOwner(ref _nativeOffline);
        RetireNativeOwner(ref _nativeOfflineController);
        RetireNativeOwner(ref _nativeAdventure);
        RetireNativeOwner(ref _nativeAdventureController);
        RetireNativeOwner(ref _nativeLicense);
        RetireNativeOwner(ref _licenseSelection);
        RetireNativeOwner(ref _nativePause);
        RetireNativeOwner(ref _nativePauseController);
        RetireNativeOwner(ref _nativeTheatre);
        RetireNativeOwner(ref _nativeTheatreController);
        RetireNativeOwner(ref _nativePlayback);
        RetireNativeOwner(ref _nativePlaybackController);
        RetireNativeOwner(ref _nativeStudio);
        RetireNativeOwner(ref _nativeCommunity);
        RetireNativeOwner(ref _nativeCommunityController);
        RetireNativeOwner(ref _nativeSocial);
        RetireNativeOwner(ref _nativeNews);
        RetireNativeOwner(ref _nativeNewsController);
        RetireNativeSettings();
        RetireNativeResults();
        if (cancelLaunch)
        {
            if (_nativeLaunchFailure != null) _pending = null;
            _nativeLaunchFailure = null;
            _nativeLaunchDocument = default;
        }
    }

    private static void RetireNativeOwner<T>(ref T? owner) where T : class, IDisposable
    {
        T? retiring = owner; owner = null;
        if (retiring == null) return;
        try { retiring.Dispose(); }
        catch (Exception error) { DebugLog.Exception("native-presentation-retirement", error); }
    }

    private static bool OpenNativePage(LauncherPage page, bool navigate = true, string? item = null)
    {
        if (!RmlUiPrototype.Active || RmlUiPrototype.Pages is not { } composition) return false;
        if (NativeSetupBlocksNavigation) return true;
        if (page is not (LauncherPage.Offline or LauncherPage.Adventure or LauncherPage.License or LauncherPage.Theatre
            or LauncherPage.StudioLaunch or LauncherPage.Community or LauncherPage.Social or LauncherPage.Settings or LauncherPage.News)) return false;
        if (navigate && NativeSettingsForeground && page != LauncherPage.Settings)
        { _nativeSettings!.RequestLeave(() => OpenNativePage(page, item: item)); return true; }
        if (navigate && ApplicationRouter.Current == new LauncherRoute(page, item) && HasNativePage) return true;
        if (page == LauncherPage.Settings && item == "setup") return OpenNativeSetupPage(!GameFiles.Ready, navigate);
        if (navigate && ApplicationRouter.Navigate(new(page, item)) == LauncherNavigationOutcome.Blocked) return true;
        RetireNativePages();
        _rmlLobbyRules?.ResetSession();
        _rmlMultiplayer?.Cancel();
        composition.Suspend();
        RmlUiHost host = RmlUiPrototype.Runtime;
        switch (page)
        {
            case LauncherPage.Offline:
                IReadOnlyList<string> rooms = _rooms.Count != 0 ? _rooms
                    : GameFiles.Ready ? ThumbnailGenerator.MultiplayerRooms() : Array.Empty<string>();
                _nativeOfflineController = new(GameState.LoadSettings(), rooms);
                _nativeOffline = new(host, composition.Manager, _nativeOfflineController);
                _nativeOffline.Open();
                break;
            case LauncherPage.Adventure:
                _nativeAdventureController = new(GameState.LoadSettings());
                _nativeAdventure = new(host, composition.Manager, _nativeAdventureController);
                _nativeAdventure.Open();
                break;
            case LauncherPage.License:
                _nativeLicense = new(host, composition.Manager, new LicenseController());
                _nativeLicense.Closed += ReturnNativePage;
                _nativeLicense.CustomizationRequested += OpenNativeHunters;
                _nativeLicense.Open(loadProfile: !RmlUiPrototype.CaptureRequested);
                break;
            case LauncherPage.Theatre:
                _nativeTheatreController = _nativeReturnTheatre ?? new(new TheatreEngineBackend());
                _nativeReturnTheatre = null;
                _nativeTheatre = new(host, composition.Manager, _nativeTheatreController);
                _nativeTheatre.Open();
                break;
            case LauncherPage.StudioLaunch:
                _nativeStudio = new(host, composition.Manager, new StudioController(new LauncherStudioBackend()));
                _nativeStudio.Closed += ReturnNativePage;
                _nativeStudio.Open(item);
                break;
            case LauncherPage.Community:
                _nativeCommunityController = new(new CommunityEngineBackend());
                _nativeCommunity = new(host, composition.Manager, _nativeCommunityController);
                _nativeCommunity.Open();
                break;
            case LauncherPage.Social:
                _nativeSocial = new(host, composition.Manager, new SocialController());
                _nativeSocial.Closed += ReturnNativePage;
                _nativeSocial.Open(refresh: !RmlUiPrototype.CaptureRequested);
                break;
            case LauncherPage.Settings:
                OpenNativeSettings(_window?.HasScene == true);
                break;
            case LauncherPage.News:
                _nativeNewsController = new(new BundledNewsProvider(), new NewsEngineLinkLauncher());
                _nativeNews = new(host, composition.Manager, _nativeNewsController);
                _nativeNews.Open();
                break;
        }
        composition.PresentChrome(composition.Manager.Page);
        WireNativePages();
        RmlUiPrototype.Show();
        return true;
    }

    private static void ReturnNativePage()
    {
        if (!RmlUiPrototype.Active || RmlUiPrototype.Pages is not { } composition) return;
        RetireNativePages();
        ApplicationRouter.Back();
        LauncherPage page = ApplicationRouter.Current.Page;
        if (OpenNativePage(page, navigate: false)) return;
        composition.ShowBaseline(page == LauncherPage.Play ? RmlUiMenuPage.Play
            : _rmlLobby != null ? RmlUiMenuPage.Lobby : RmlUiMenuPage.Home);
        if (page == LauncherPage.Play) composition.SetBool("play_browser_mode", true);
        RmlUiPrototype.Show();
    }

    private static bool BackNativePage()
    {
        if (_nativeQueue != default && RmlUiPrototype.Pages?.Manager.Top == _nativeQueue)
        { _rmlMultiplayer?.QueueLeave(); TickNativeQueue(); return true; }
        if (_nativeHunters?.Active == true) { _nativeHunters.Dispose(); _nativeHunters = null; return true; }
        if (_nativeAdmin?.Active == true)
        {
            _nativeAdmin.Back();
            if (!_nativeAdmin.Active) _nativeAdmin = null;
            return true;
        }
        if (_nativePause?.Active == true) { _nativePause.Back(); DrainNativePageEffects(); return true; }
        if (_nativeHud?.IsOpen == true) { _nativeHud.Back(); TickNativeSettings(); return true; }
        if (NativeSettingsForeground) return _nativeSettings!.Back();
        if (_nativeSetup?.Back() == true) return true;
        if (_nativeResults?.Active == true) { _nativeResults.Back(); DrainNativeResultEffects(); return true; }
        if (_nativeAimResults?.Active == true) { _nativeAimResults.Back(); DrainNativeResultEffects(); return true; }
        if (_nativePlayback?.IsOpen == true) { RequestEndMatch(); return true; }
        if (_nativeTheatre?.Back() == true) return true;
        if (_nativeStudio?.Back() == true) return true;
        if (_nativeSocial?.Back() == true) return true;
        if (_nativeNews?.Back() == true) return true;
        if (_nativeAdventure?.Back() == true) return true;
        if (!HasNativePage) return false;
        if (RmlUiPrototype.Pages?.Manager.ModalCount > 0)
        {
            RmlUiPrototype.Pages.Manager.Back();
            _nativeOffline?.Refresh();
            return true;
        }
        ReturnNativePage();
        return true;
    }

    private static void OpenNativeHunters()
    {
        if (!RmlUiPrototype.Active || NativeSetupBlocksNavigation) return;
        _nativeAdmin?.Dispose(); _nativeAdmin = null;
        _nativeHunters?.Dispose();
        if (_nativeLicense != null) _licenseSelection ??= new(_rmlLobby);
        _nativeHunters = new(RmlUiPrototype.Runtime, _rmlLobby, existingSelection: _licenseSelection);
        _nativeHunters.PreviewChanged += PresentNativeHunterPreview;
        _nativeHunters.Closed += () =>
        {
            CosmeticPreview.Loadout = null; CosmeticPreview.LoopDeath = false;
            CosmeticPreview.Mode = SkinContext.Biped; CosmeticPreview.Yaw = 0;
            CosmeticPreview.Zoom = 1; CosmeticPreview.DeathRequest = 0;
        };
        _nativeHunters.Open();
        WireNativePages();
    }

    private static void PresentNativeHunterPreview(HunterSelectionSnapshot state)
    {
        CosmeticPreview.Loadout = state.PreviewLoadout;
        CosmeticPreview.Mode = state.PreviewMode;
        CosmeticPreview.Yaw = state.Yaw;
        CosmeticPreview.Zoom = state.Zoom;
        CosmeticPreview.LoopDeath = state.LoopDeath;
        CosmeticPreview.DeathRequest = state.DeathRequest;
        LauncherHunter.Hunter = state.Hunter;
        LauncherHunter.Suit = state.Color;
    }

    private static bool DrawNativeHunterPreview(RenderWindow window, int width, int height)
    {
        if (_nativeHunters?.Active != true) return false;
        if (!RmlUiPrototype.Runtime.TryGetElementBounds(_nativeHunters.Document, "hunter_preview_space",
            out float left, out float top, out float areaWidth, out float areaHeight)) return true;
        PresentNativeHunterPreview(_nativeHunters.Snapshot);
        LauncherHunter.Wanted = GameFiles.Ready;
        LauncherHunter.CanPresent = () => _nativeHunters?.Active == true && RmlUiPrototype.Visible;
        LauncherHunter.PreviewSlot = -1;
        LauncherHunter.Left = left / width;
        LauncherHunter.Top = top / height;
        LauncherHunter.Right = (left + areaWidth) / width;
        LauncherHunter.Bottom = (top + areaHeight) / height;
        LauncherHunter.DistanceScale = 1;
        LauncherHunter.TransparentBackground = true;
        LauncherHunter.Draw(window, width, height);
        _nativeHunters.SetPreviewStatus(Scene.PreviewDrawnLastFrame
            ? "HUNTER PREVIEW READY" : GameFiles.Ready ? "PREPARING HUNTER PREVIEW" : "SET UP GAME FILES TO SHOW THE HUNTER PREVIEW.");
        return true;
    }

    private static bool HandleNativePageIntent(in RmlUiIntent intent)
    {
        if (HandleNativeQueue(intent)) return true;
        if (_nativeHunters?.Active == true) { _nativeHunters.Handle(intent); return true; }
        if (_nativeAdmin?.Active == true) { _nativeAdmin.Handle(intent); return true; }
        if (HasNativePage && RmlUiPrototype.Pages is { } activePages && intent.Document != activePages.Manager.Top) return true;
        if (_nativeOffline?.HandleAction(intent) == true || _nativeAdventure?.HandleIntent(intent) == true
            || _nativeLicense?.Handle(intent) == true || _nativePause?.Handle(intent) == true
            || _nativeTheatre?.HandleIntent(intent) == true || _nativePlayback?.HandleIntent(intent) == true
            || _nativeStudio?.Handle(intent) == true || _nativeCommunity?.HandleAction(intent) == true
            || _nativeSocial?.Handle(intent) == true || _nativeNews?.HandleAction(intent) == true || _nativeSettings?.HandleAction(intent) == true
            || _nativeHud?.HandleIntent(intent) == true || _nativeResults?.Handle(intent) == true || _nativeAimResults?.Handle(intent) == true)
        {
            DrainNativePageEffects(); return true;
        }
        if (_nativeSetup?.HandleAction(intent) == true) return true;
        if (_nativeSetup != null && !_nativeSetup.Controller.CanLeave) return true;
        if (intent.Kind == RmlUiIntentKind.HunterOpen) { OpenNativeHunters(); return true; }
        if (intent.Kind == RmlUiIntentKind.OpenStudio) return OpenNativePage(LauncherPage.StudioLaunch);
        if (intent.Kind == RmlUiIntentKind.LobbyAdminOpen && _rmlLobby != null)
        {
            _nativeHunters?.Dispose(); _nativeHunters = null;
            _nativeAdmin?.Dispose();
            _nativeAdmin = new(RmlUiPrototype.Runtime, _rmlLobby);
            _nativeAdmin.Open(); WireNativePages(); return true;
        }
        if (intent.Kind != RmlUiIntentKind.Navigate) return false;
        return NavigateNativeRoute((RmlUiRouteArgument)intent.Argument);
    }

    private static bool NavigateNativeRoute(RmlUiRouteArgument route)
    {
        if (NativeSetupBlocksNavigation) return true;
        if (NativeSettingsForeground && route != RmlUiRouteArgument.Settings)
        {
            // The approved destination outlives the settings document that
            // requested it. Never replay that document's retired input packet.
            _nativeSettings!.RequestLeave(() => NavigateNativeRoute(route));
            return true;
        }
        if (route is RmlUiRouteArgument.Home or RmlUiRouteArgument.Play)
        {
            if (RmlUiPrototype.Pages is not { } composition) return false;
            bool play = route == RmlUiRouteArgument.Play;
            if (ApplicationRouter.Navigate(new(play ? LauncherPage.Play : LauncherPage.Home)) == LauncherNavigationOutcome.Blocked) return true;
            _rmlMultiplayer?.Cancel();
            RetireNativePages();
            composition.ShowBaseline(play ? RmlUiMenuPage.Play : _rmlLobby != null ? RmlUiMenuPage.Lobby : RmlUiMenuPage.Home);
            if (play) { composition.SetBool("play_browser_mode", true); EnsureRmlMultiplayer().Open(quickPlay: false); }
            return true;
        }
        LauncherPage? page = route switch
        {
            RmlUiRouteArgument.Offline => LauncherPage.Offline,
            RmlUiRouteArgument.Adventure => LauncherPage.Adventure,
            RmlUiRouteArgument.HunterLicense => LauncherPage.License,
            RmlUiRouteArgument.Community => LauncherPage.Community,
            RmlUiRouteArgument.Theatre => LauncherPage.Theatre,
            RmlUiRouteArgument.Social => LauncherPage.Social,
            RmlUiRouteArgument.Settings => LauncherPage.Settings,
            RmlUiRouteArgument.News => LauncherPage.News,
            _ => null
        };
        return page.HasValue && OpenNativePage(page.Value);
    }

    private static bool RecoverNativeTrainingLaunchFailure(RenderWindow window, LaunchPlan plan, string failure)
    {
        if (!RmlUiPrototype.Active || plan.Kind != LaunchKind.AimTrainer) return false;
        EndMatch(window);
        if (!OpenNativePage(LauncherPage.Offline) || _nativeOffline == null) return false;
        _nativeOffline.FocusTraining();
        _nativeOffline.ReportLaunchFailure(failure);
        return true;
    }

    private static void TickNativePages()
    {
        _nativeOffline?.Refresh();
        _nativeAdventure?.Present();
        _nativeLicense?.Refresh();
        _nativePause?.Refresh();
        _nativeTheatre?.Present();
        _nativePlayback?.Present();
        _nativeStudio?.Refresh();
        _nativeCommunity?.Tick();
        _nativeSocial?.Refresh();
        _nativeNews?.Refresh();
        TickNativeSocial();
        TickNativeSettings();
        TickNativeQueue();
        _nativeResults?.Refresh();
        _nativeAdmin?.Update();
        _nativeHunters?.Update();
        if (HasNativePage && RmlUiPrototype.Pages is { } composition)
            composition.PresentChrome(composition.Manager.Page);
        DrainNativePageEffects();
        DrainNativeResultEffects();
        ApplyNativePresentationPolicy();
    }

    private static void QueueNativePageLaunch(LaunchPlan plan, RmlUiDocumentToken document, Action<string> failure)
    {
        _nativeLaunchDocument = document;
        _nativeLaunchFailure = failure;
        Decided(plan);
    }

    private static void DrainNativePageEffects()
    {
        if (_nativeCommunity?.TryTakeHostRequest(out CommunityHostRequest request) == true)
        {
            RetireNativePages();
            ApplicationRouter.Navigate(new(LauncherPage.Play));
            RmlUiPrototype.Pages?.ShowBaseline(RmlUiMenuPage.Play);
            EnsureRmlMultiplayer().OpenCreateForMap(request);
        }
        if (_pending == null)
        {
            if (_nativeOffline?.TryTakeLaunch(out LaunchPlan offline) == true)
                QueueNativePageLaunch(offline, _nativeOffline.Document, _nativeOffline.ReportLaunchFailure);
            else if (_nativeAdventure?.TryTakeLaunch(out LaunchPlan adventure) == true)
                QueueNativePageLaunch(adventure, _nativeAdventure.Document, _nativeAdventure.ReportLaunchFailure);
            else if (_nativeTheatre?.TryTakeLaunch(out LaunchPlan replay) == true)
                QueueNativePageLaunch(replay, _nativeTheatre.Document, _nativeTheatre.ReportLaunchFailure);
        }
        if (_nativePause?.TryTakeEffect(out InGameEffect effect) == true)
        {
            if (effect == InGameEffect.Resume) CloseNativePause();
            else if (effect == InGameEffect.Replay) OpenNativePlayback();
            else if (effect == InGameEffect.Settings) OpenNativePage(LauncherPage.Settings);
        }
        if (_nativePlayback?.TryTakeEngineCommand(out TheatrePlaybackAction transport) == true)
        {
            if (transport == TheatrePlaybackAction.Back) RequestEndMatch();
            else if (transport == TheatrePlaybackAction.Fullscreen)
            {
                RetireNativePages();
                RmlUiPrototype.Hide();
                if (_window != null) FullscreenReplay(_window);
            }
        }
    }

    private static void CompleteNativePageLaunch(LaunchPlan plan)
    {
        if (plan.Kind == LaunchKind.Demo && _nativeTheatreController != null)
        {
            _nativeReturnTheatre?.Dispose();
            _nativeReturnTheatre = _nativeTheatreController;
            _nativeTheatreController = null;
        }
        if (_nativeLaunchFailure != null) RetireNativePages();
        if (plan.Kind == LaunchKind.Demo && RmlUiPrototype.Active) OpenNativePlayback();
    }

    private static bool OpenNativePlayback()
    {
        if (_window?.HasScene != true || !DemoPlayback.IsActive || !RmlUiPrototype.Active
            || RmlUiPrototype.Pages is not { } composition) return false;
        RetireNativePages();
        composition.Suspend();
        _nativePlaybackController = new(new TheatreEngineBackend());
        _nativePlayback = new(RmlUiPrototype.Runtime, composition.Manager, _nativePlaybackController);
        _nativePlayback.Open();
        WireNativePages(); RmlUiPrototype.ShowGameplayMenu();
        PauseMenu.MarkClosed();
        return true;
    }

    internal static Vector4i? NativeReplayViewportBounds(int width, int height)
    {
        if (!RmlUiPrototype.Visible || _nativePlayback?.IsOpen != true
            || !RmlUiPrototype.Runtime.TryGetElementBounds(_nativePlayback.Document, "replay_viewport",
                out float x, out float y, out float w, out float h)) return null;
        int left = Math.Clamp((int)Math.Round(x), 0, width), top = Math.Clamp((int)Math.Round(y), 0, height);
        int right = Math.Clamp((int)Math.Round(x + w), left, width), bottom = Math.Clamp((int)Math.Round(y + h), top, height);
        return right > left && bottom > top ? new(left, top, right - left, bottom - top) : null;
    }

    internal static bool PollNativeReplayViewport(Scene scene)
    {
        if (!RmlUiPrototype.Visible || _nativePlayback?.IsOpen != true) return false;
        _nativePlayback.Viewport.Poll(scene);
        return true;
    }

    private static bool NativeReplayPointer(MouseButton button, double x, double y, bool down)
    {
        if (_nativePlayback?.IsOpen != true || _window == null || button != MouseButton.Button1) return false;
        if (!down) { _nativePlayback.Viewport.PointerUp(); return false; }
        Vector2i framebuffer = _window.FramebufferSize, logical = _window.ClientSize;
        var bounds = NativeReplayViewportBounds(framebuffer.X, framebuffer.Y);
        double px = logical.X > 0 ? x * framebuffer.X / logical.X : x;
        double py = logical.Y > 0 ? y * framebuffer.Y / logical.Y : y;
        if (bounds is { } area && px >= area.X && py >= area.Y && px < area.X + area.Z && py < area.Y + area.W)
        { _nativePlayback.Viewport.PointerDownInWindow(x, y); return true; }
        _nativePlayback.Viewport.Release();
        return false;
    }

    private static bool NativeReplayShortcut(Keys key, RmlUiInputModifiers modifiers)
    {
        if (_nativePlayback?.IsOpen != true || _nativePlaybackController == null
            || RmlUiPrototype.Runtime.FocusedElement() == "replay_position" || modifiers != RmlUiInputModifiers.None) return false;
        if (key == Keys.Space) _nativePlaybackController.Dispatch(TheatrePlaybackAction.TogglePause);
        else if (key is Keys.Left or Keys.Right) _nativePlaybackController.Jump(key == Keys.Left ? -60 : 60);
        else return false;
        _nativePlayback.Present(); return true;
    }

    private static bool _nativeCapturePageApplied;
    private static void ApplyNativeCapturePage()
    {
        if (_nativeCapturePageApplied || !RmlUiPrototype.Active || !RmlUiPrototype.CaptureRequested) return;
        _nativeCapturePageApplied = true;
        string[] arguments = Environment.GetCommandLineArgs();
        for (int index = 0; index + 1 < arguments.Length; index++)
        {
            if (!arguments[index].Equals("-rmluipage", StringComparison.OrdinalIgnoreCase)) continue;
            if (arguments[index + 1].Equals("hud", StringComparison.OrdinalIgnoreCase))
            { OpenNativePage(LauncherPage.Settings); OpenNativeHud(); return; }
            if (!LauncherRouteCatalog.TryParse(arguments[index + 1], out LauncherRoute route))
                throw new ArgumentException("Unknown native capture route.");
            if (route.Page is LauncherPage.Home or LauncherPage.Play)
            {
                NavigateNativeRoute(route.Page == LauncherPage.Home ? RmlUiRouteArgument.Home : RmlUiRouteArgument.Play);
                return;
            }
            if (route.Page == LauncherPage.Hunters) { OpenNativeHunters(); return; }
            if (!OpenNativePage(route.Page, item: route.Item))
                throw new InvalidOperationException("The selected native capture route has no registered presenter.");
            return;
        }
    }

    private static bool RecoverNativeLaunchFailure(RenderWindow window, string error)
    {
        if (_nativeLaunchFailure is not { } report || !RmlUiPrototype.Runtime.IsAlive(_nativeLaunchDocument)) return false;
        _matchLoading = false;
        window.EndScene();
        NetSession.Stop(); NetHostSession.Stop();
        MatchStart.AfterMatch(); PauseMenu.Reset();
        _played = null;
        _nativeLaunchFailure = null; _nativeLaunchDocument = default;
        report(error);
        RmlUiPrototype.Show();
        return true;
    }

    private static bool OpenNativePause()
    {
        if (_window?.HasScene != true || !RmlUiPrototype.Active || RmlUiPrototype.Pages is not { } composition) return false;
        if (_nativePause?.Active == true) return true;
        RetireNativePages();
        composition.Suspend();
        _nativePauseController = new();
        _nativePause = new(RmlUiPrototype.Runtime, composition.Manager, _nativePauseController);
        _nativePause.Open();
        WireNativePages();
        RmlUiPrototype.ShowGameplayMenu();
        return true;
    }

    private static bool CloseNativePause()
    {
        if (_nativePause == null) return false;
        RetireNativePages();
        RmlUiPrototype.Hide();
        PauseMenu.MarkClosed();
        return true;
    }
}
#endif
