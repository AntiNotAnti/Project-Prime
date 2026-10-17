#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Pages.News;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
using MphRead.Mods.Launcher.RmlUi.Pages.Hunters;
using MphRead.Mods.Launcher.RmlUi.Pages.Community;
using MphRead.Mods.Launcher.RmlUi.Pages.License;
using MphRead.Mods.Launcher.RmlUi.Pages.Studio;
using MphRead.Mods.Launcher.RmlUi.Pages.Social;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.RmlUi.Render;

/// <summary>
/// Opt-in native OpenGL launcher on the existing RenderWindow. The composed
/// Home/Play/Lobby documents share one page manager and the current game's
/// authoritative services. Unmigrated workspaces still route to Avalonia.
/// </summary>
internal static class RmlUiOpenGlSmoke
{
    private static readonly bool Requested =
        String.Equals(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE"),
            "1", StringComparison.Ordinal);

    private static RmlUiHost? _host;
    private static RmlUiMenuController? _controller;
    private static RmlUiLiveMenuServices? _services;
    private static RmlUiPageManager? _pages;
    private static RmlUiLauncherPages? _launcherPages;
    private static NewsPagePresenter? _news;
    private static NewsController? _newsController;
    private static QuickSettingsPagePresenter? _quickSettings;
    private static NativeHunterPagePresenter? _hunterChoice;
    private static NativeCommunityDiscoverPage? _community;
    private static NativeLicenseOverviewPage? _license;
    private static NativeStudioEntryPage? _studio;
    private static NativeSocialFriendsPage? _social;
    private static long _lastPresentedRevision;
    private static bool _shown;
    private static bool _attempted, _disabled;
    private static int _width, _height;
    private static float _density;

    internal static bool CapturesInput => !_disabled && _shown && _host is { Active: true };

    /// <summary>
    /// Called before compositing the shell on a scene-free window. A positive
    /// result means RmlUi has initialized and will paint this frame; Avalonia's
    /// chrome must NOT also be drawn underneath its translucent page.
    /// </summary>
    internal static bool BeforeLauncherComposite(RenderWindow window)
    {
        if (!Requested || _disabled || window.HasScene
            || window.FramebufferSize.X <= 0 || window.FramebufferSize.Y <= 0
            || !Shell.RmlUiCanPresent)
        {
            if (_shown && _host is { Active: true } current) current.ReleaseInput();
            RmlUiHunterStageRenderer.Hide();
            _shown = false;
            return false;
        }
        if (!TryStart(window)) { _shown = false; return false; }
        _shown = true;
        return true;
    }

    internal static void Draw(RenderWindow window)
    {
        if (!BeforeLauncherComposite(window)) return;
        RmlUiHost host = _host!;
        try
        {
            int width = window.FramebufferSize.X;
            int height = window.FramebufferSize.Y;
            float density = Density(window);
            if (_width != width || _height != height || _density != density)
            {
                host.Resize(width, height, density);
                _width = width;
                _height = height;
                _density = density;
            }
            host.Input.SetFramebufferScale(
                (float)width / Math.Max(1, window.ClientSize.X),
                (float)height / Math.Max(1, window.ClientSize.Y));
            // Native events are validated by the versioned ABI. Application
            // commands go to one owner-thread controller, not native callbacks
            // or a second matchmaking/network pump.
            RmlUiMenuController controller = _controller!;
            for (int i = 0; i < 64 && host.TryTakeIntent(out RmlUiIntent intent); i++)
            {
                // The composed page manager owns foreground document/sequence
                // admission. Historical home-only events no longer bypass it.
                bool baselineIntent = _launcherPages is { Suspended: false }
                    && _news == null && _quickSettings == null && _hunterChoice == null
                    && _community == null && _license == null && _studio == null
                    && _social == null;
                if (baselineIntent)
                {
                    if (!_launcherPages!.HandleIntent(intent, out RmlUiIntent forwarded))
                        continue;
                    if (forwarded.Kind == 0) continue;
                    intent = forwarded;
                }
                if (_social != null && intent.Kind == RmlUiIntentKind.SocialAction)
                {
                    NativeSocialFriendsPage page = _social;
                    page.Handle(intent);
                    if (page.Closed) CloseNativeSocial();
                    continue;
                }
                if (_studio != null && intent.Kind == RmlUiIntentKind.StudioAction)
                {
                    NativeStudioEntryPage studio = _studio;
                    studio.Handle(intent);
                    if (studio.Closed) CloseNativeStudio();
                    continue;
                }
                if (intent.Kind == RmlUiIntentKind.OpenStudio)
                {
                    // Baseline intents were admitted by the composed page manager.
                    // Background controls from a suspended page cannot launch Studio.
                    if (!baselineIntent && _pages?.Page != default) continue;
                    CloseNativeSocial();
                    CloseNativeStudio();
                    CloseNativeLicense();
                    CloseNativeCommunity();
                    CloseNativeHunter();
                    CloseNativeQuickSettings();
                    CloseNativeNews();
                    OpenNativeStudio();
                    continue;
                }
                if (_license != null && intent.Kind == RmlUiIntentKind.LicenseAction)
                {
                    NativeLicenseOverviewPage license = _license;
                    license.Handle(intent);
                    if (license.Closed) CloseNativeLicense();
                    else if (license.OpenFullLicenseRequested)
                    {
                        CloseNativeLicense();
                        controller.Dispatch(RmlUiIntentKind.Navigate,
                            (int)RmlUiRouteArgument.HunterLicense);
                    }
                    continue;
                }
                // Native Community installs immutable packages through the shared
                // verified queue; creator actions still belong to Studio.
                if (_community != null && intent.Kind is
                    (RmlUiIntentKind.CommunityRefresh or RmlUiIntentKind.CommunitySearch
                     or RmlUiIntentKind.CommunitySort or RmlUiIntentKind.CommunityPage
                     or RmlUiIntentKind.CommunitySelect or RmlUiIntentKind.CommunityImport
                     or RmlUiIntentKind.CommunityDetailAction or RmlUiIntentKind.CommunityCancelWork
                     or RmlUiIntentKind.CommunityTab or RmlUiIntentKind.CommunityCreatorAction
                     or RmlUiIntentKind.CommunityConfirm or RmlUiIntentKind.CommunityCancel))
                {
                    NativeCommunityDiscoverPage page = _community;
                    page.Handle(intent);
                    if (page.OpenFullCommunityRequested)
                    {
                        CloseNativeCommunity();
                        controller.Dispatch(RmlUiIntentKind.Navigate,
                            (int)RmlUiRouteArgument.Community);
                    }
                    continue;
                }
                // Foreground document, sequence and generation fences reject
                // stale input from pages hidden by the native router.
                if (_hunterChoice != null && intent.Kind is
                    (RmlUiIntentKind.HunterSelect or RmlUiIntentKind.HunterSuit
                     or RmlUiIntentKind.HunterApply or RmlUiIntentKind.HunterCancel
                     or RmlUiIntentKind.HunterResetPreview or RmlUiIntentKind.HunterOpen
                     or RmlUiIntentKind.HunterSkinNext or RmlUiIntentKind.HunterArmorNext
                     or RmlUiIntentKind.HunterDeathNext or RmlUiIntentKind.HunterCosmeticsReset
                     or RmlUiIntentKind.HunterCosmeticsSave))
                {
                    NativeHunterPagePresenter hunter = _hunterChoice;
                    hunter.Handle(intent);
                    if (hunter.Closed) CloseNativeHunter();
                    else if (hunter.OpenFullLicenseRequested)
                    {
                        CloseNativeHunter();
                        OpenNativeLicense();
                    }
                    continue;
                }
                if (_quickSettings != null && intent.Kind is
                    (RmlUiIntentKind.SettingsAction or RmlUiIntentKind.SettingsApply
                        or RmlUiIntentKind.SettingsDiscard or RmlUiIntentKind.SettingsClose))
                {
                    QuickSettingsPagePresenter quick = _quickSettings;
                    quick.Handle(intent);
                    if (quick.Closed) CloseNativeQuickSettings();
                    else if (quick.OpenFullSettingsRequested)
                    {
                        CloseNativeQuickSettings();
                        controller.Dispatch(RmlUiIntentKind.Navigate,
                            (int)RmlUiRouteArgument.Settings);
                    }
                    continue;
                }
                if (intent.Kind == RmlUiIntentKind.NewsAction)
                {
                    _news?.HandleAction(intent);
                    continue;
                }
                if (intent.Kind == RmlUiIntentKind.Navigate)
                {
                    // Treat unsaved native drafts like the current legacy route
                    // guard. A header/tab click must not discard silently.
                    if ((_quickSettings is { CanNavigateAway: false })
                        || (_hunterChoice is { CanNavigateAway: false })) continue;
                    if (!baselineIntent && _pages != null && _pages.Page != default
                        && !_pages.Accept(intent)) continue;
                    if (intent.Argument == (int)RmlUiRouteArgument.Social && _social == null)
                    {
                        CloseNativeSocial();
                        CloseNativeStudio();
                        CloseNativeLicense();
                        CloseNativeCommunity();
                        CloseNativeHunter();
                        CloseNativeQuickSettings();
                        CloseNativeNews();
                        OpenNativeSocial(host);
                        continue;
                    }
                    if (intent.Argument == (int)RmlUiRouteArgument.News && _news == null)
                    {
                        CloseNativeSocial();
                        CloseNativeStudio();
                        CloseNativeLicense();
                        CloseNativeCommunity();
                        CloseNativeQuickSettings();
                        CloseNativeHunter();
                        OpenNativeNews(host);
                        continue;
                    }
                    if (intent.Argument == (int)RmlUiRouteArgument.Settings && _quickSettings == null)
                    {
                        CloseNativeSocial();
                        CloseNativeStudio();
                        CloseNativeLicense();
                        CloseNativeCommunity();
                        CloseNativeNews();
                        CloseNativeHunter();
                        OpenNativeQuickSettings();
                        continue;
                    }
                    if (intent.Argument == (int)RmlUiRouteArgument.HunterLicense
                        && _hunterChoice == null)
                    {
                        CloseNativeSocial();
                        CloseNativeStudio();
                        CloseNativeLicense();
                        CloseNativeCommunity();
                        CloseNativeNews();
                        CloseNativeQuickSettings();
                        OpenNativeHunter();
                        continue;
                    }
                    if (intent.Argument == (int)RmlUiRouteArgument.Community
                        && _community == null)
                    {
                        CloseNativeSocial();
                        CloseNativeStudio();
                        CloseNativeLicense();
                        CloseNativeNews();
                        CloseNativeHunter();
                        CloseNativeQuickSettings();
                        OpenNativeCommunity(host);
                        continue;
                    }
                    if (intent.Argument == (int)RmlUiRouteArgument.Home
                        && (_news != null || _quickSettings != null
                            || _hunterChoice != null || _community != null
                            || _license != null || _studio != null || _social != null))
                    {
                        CloseNativeSocial();
                        CloseNativeStudio();
                        CloseNativeLicense();
                        CloseNativeCommunity();
                        CloseNativeHunter();
                        CloseNativeQuickSettings();
                        CloseNativeNews();
                        continue;
                    }
                }
                // Page-specific actions never fall through to the Home model.
                if (_hunterChoice != null || _quickSettings != null
                    || _news != null || _community != null || _license != null
                    || _studio != null || _social != null)
                {
                    if (intent.Kind != RmlUiIntentKind.Navigate) continue;
                    // A native page never remains alive underneath a legacy
                    // destination. Cancel Social/Studio and retire their
                    // documents before forwarding to the existing router.
                    CloseNativeSocial();
                    CloseNativeStudio();
                    CloseNativeLicense();
                    CloseNativeCommunity();
                    CloseNativeHunter();
                    CloseNativeQuickSettings();
                    CloseNativeNews();
                }
                if (_news != null)
                {
                    // Native navigation to an existing, nonmigrated workspace
                    // retires this page before passing its intent to the
                    // existing application router.
                    CloseNativeNews();
                }
                // Fields belong to their composed page or modal. Reading the
                // hidden native Home document loses typed names/rule edits.
                RmlUiLauncherPages? layout = _launcherPages;
                string address = intent.Kind == RmlUiIntentKind.PlayJoin
                    ? layout?.ReadField("play_join_address") ?? "" : "";
                string player = intent.Kind is RmlUiIntentKind.PlayJoin or RmlUiIntentKind.PlayServer
                    ? layout?.ReadField("play_player_name") ?? "" : "";
                string ruleTime = intent.Kind == RmlUiIntentKind.LobbyRulesApply
                    ? layout?.ReadField("rules_time") ?? "" : "";
                string ruleGoal = intent.Kind == RmlUiIntentKind.LobbyRulesApply
                    ? layout?.ReadField("rules_goal") ?? "" : "";
                string createName = intent.Kind == RmlUiIntentKind.PlayCreate
                    ? layout?.ReadField("play_create_name") ?? "" : "";
                if (intent.Kind == RmlUiIntentKind.PlayCreate)
                    player = layout?.ReadField("play_create_player_name") ?? "";
                string chatField = controller.AdminOpen ? "admin_chat_input"
                    : "lobby_chat_input";
                string adminChat = intent.Kind == RmlUiIntentKind.LobbyChatSend
                    ? layout?.ReadField(chatField) ?? "" : "";
                string arenaSearch = intent.Kind == RmlUiIntentKind.OfflineArenaSearch
                    ? layout?.ReadField("offline_arena_search") ?? "" : "";
                string theatreSearch = intent.Kind == RmlUiIntentKind.TheatreAction
                    && intent.Argument == 0 ? layout?.ReadField("theatre_search") ?? "" : "";
                bool handled = controller.Dispatch(intent.Kind, intent.Argument,
                    address, player, ruleTime, ruleGoal, createName, adminChat, arenaSearch,
                    theatreSearch);
                if (handled && intent.Kind == RmlUiIntentKind.TheatreAction
                    && intent.Argument == 23) layout?.SetField("theatre_search", "");
                if (handled && intent.Kind == RmlUiIntentKind.LobbyChatSend)
                    layout?.SetField(chatField, "");
                if (handled && intent.Kind is RmlUiIntentKind.LobbyRulesOpen
                    or RmlUiIntentKind.LobbyRulesMode or RmlUiIntentKind.LobbyRulesFormat)
                {
                    if (controller.RuleInputs is { } fields)
                    {
                        _launcherPages?.SetField("rules_time", fields.Time);
                        _launcherPages?.SetField("rules_goal", fields.Goal);
                    }
                }
            }
            // A native navigation may have opened the existing setup/create
            // modal or legacy workspace within this very frame. Yield capture
            // immediately, not one presented frame later.
            if (!Shell.RmlUiCanPresent)
            {
                _shown = false;
                host.ReleaseInput();
                return;
            }
            _community?.Refresh();
            _social?.Refresh();
            _license?.Refresh();
            _hunterChoice?.Pump();
            RmlUiMenuSnapshot snapshot = controller.Tick();
            RmlUiLauncherPages? launcher = _launcherPages;
            if (launcher != null)
            {
                bool routeOwned = _news != null || _quickSettings != null
                    || _hunterChoice != null || _community != null || _license != null
                    || _studio != null || _social != null;
                if (routeOwned && !launcher.Suspended) launcher.Suspend();
                else if (!routeOwned && launcher.Suspended) launcher.Resume();

                if (snapshot.Revision > _lastPresentedRevision)
                {
                    foreach (var item in snapshot.Bindings)
                    {
                        if (item.Value.Kind == RmlUiBindingKind.Text)
                            launcher.SetText(item.Key, item.Value.Text);
                        else
                            launcher.SetBool(item.Key, item.Value.Boolean);
                    }
                    _lastPresentedRevision = snapshot.Revision;
                }
                launcher.Flush();
            }
            host.Update();
            launcher?.AfterUpdate();
            // Preserve the original composition order: one procedural chamber,
            // real engine Hunter meshes and atmosphere, then RmlUi UI. Never
            // invent remote Hunter poses or launch another simulation.
            if (_services is { } services && launcher != null)
            {
                RmlUiLobbyView lobby = services.Lobby();
                RmlUiAdminSnapshot? roster = launcher.Page == RmlUiMenuPage.Lobby && lobby.Active
                    ? services.ReadAdmin() : null;
                // A visual-only preview must never take the entire launcher
                // down. Retire its state and keep navigation/controls usable.
                try
                {
                    RmlUiHunterStageRenderer.Draw(window, host, launcher.Page,
                        launcher.Suspended, services.Hub(), lobby, roster, width, height);
                }
                catch (Exception stageError) when (Recoverable(stageError))
                {
                    RmlUiHunterStageRenderer.Hide();
                    Mods.DebugLog.Line("rmlui",
                        "3D stage unavailable; retaining native page controls: "
                        + stageError.Message);
                }
            }
            RmlUiOpenGlFrame.Draw(host, width, height);
            // The full native Hunter picker owns its own model rectangle.
            // Unlike the page's opaque chrome, its isolated viewport is drawn
            // last so the actual model cannot disappear behind a UI card.
            if (_hunterChoice is { } picker && HubState.Capture().GameFilesReady)
            {
                try
                {
                    var selected = picker.PreviewSelection;
                    RmlUiHunterStageRenderer.DrawPicker(window, host,
                        picker.PreviewDocument, selected.Hunter, selected.Suit,
                        selected.Cosmetics, width, height);
                }
                catch (Exception previewError) when (Recoverable(previewError))
                {
                    Mods.DebugLog.Line("rmlui",
                        "native Hunter preview failed; controls remain active: "
                        + previewError.Message);
                }
            }
        }
        catch (Exception error) when (Recoverable(error))
        {
            Fail(error);
        }
    }

    private static bool TryStart(RenderWindow window)
    {
        if (_host is { Active: true }) return true;
        if (_attempted) return false;
        _attempted = true;
        string root = Path.Combine(AppContext.BaseDirectory, "rmlui");
        if (!File.Exists(Path.Combine(root, "prime_home.rml")))
        {
            Fail(new FileNotFoundException("Opt-in RmlUi home document is missing.", root));
            return false;
        }

        try
        {
            var host = new RmlUiHost();
            _host = host;
            int width = window.FramebufferSize.X;
            int height = window.FramebufferSize.Y;
            float density = Density(window);
            if (!host.Initialize(width, height, density, root, RmlUiRenderBackend.OpenGl))
            {
                Fail(new InvalidOperationException("Native RmlUi refused the OpenGL session."));
                return false;
            }
            _width = width;
            _height = height;
            _density = density;
            var services = new RmlUiLiveMenuServices();
            _services = services;
            _controller = new RmlUiMenuController(services);
            _pages = new RmlUiPageManager(host);
            _launcherPages = new RmlUiLauncherPages(host, _pages);
            _launcherPages.ShowBaseline();
            _lastPresentedRevision = 0;
            string player = HubState.Capture().PlayerName;
            _launcherPages.SetField("play_player_name", player);
            _launcherPages.SetField("play_create_player_name", player);
            Mods.DebugLog.Line("rmlui", "opt-in native Home/Play/Lobby session active");
            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            Fail(error);
            return false;
        }
    }

    private sealed class NativeNewsLinks : INewsLinkLauncher
    {
        public bool Open(string uri) => Mods.Update.Updater.OpenLink(uri);
    }

    private static void OpenNativeNews(RmlUiHost host)
    {
        if (_pages == null || _news != null) return;
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "rmlui",
            "pages", "news", "news.rml")))
            throw new FileNotFoundException("The native News page is not packaged.");
        _launcherPages?.Suspend();
        var controller = new NewsController(new BundledNewsProvider(), new NativeNewsLinks());
        var presenter = new NewsPagePresenter(host, _pages, controller);
        try
        {
            presenter.Open();
            _newsController = controller;
            _news = presenter;
        }
        catch
        {
            presenter.Dispose();
            controller.Dispose();
            throw;
        }
    }

    private static void CloseNativeNews()
    {
        var presenter = _news;
        var controller = _newsController;
        _news = null;
        _newsController = null;
        try { presenter?.Dispose(); }
        finally { controller?.Dispose(); }
    }

    private static void OpenNativeQuickSettings()
    {
        if (_pages == null || _quickSettings != null) return;
        string page = Path.Combine(AppContext.BaseDirectory, "rmlui",
            "pages", "settings", "quick.rml");
        if (!File.Exists(page))
            throw new FileNotFoundException("Native quick settings page missing.", page);
        _launcherPages?.Suspend();
        var next = new QuickSettingsPagePresenter(_pages, new LauncherQuickPreferenceStore());
        try
        {
            next.Open();
            _quickSettings = next;
        }
        catch
        {
            next.Dispose();
            throw;
        }
    }

    private static void CloseNativeQuickSettings()
    {
        var old = _quickSettings;
        _quickSettings = null;
        old?.Dispose();
    }

    private static void OpenNativeHunter()
    {
        if (_pages == null || _hunterChoice != null) return;
        string path = Path.Combine(AppContext.BaseDirectory, "rmlui",
            "pages", "hunters", "native-picker.rml");
        if (!File.Exists(path))
            throw new FileNotFoundException("Native Hunter picker is not packaged.", path);
        _launcherPages?.Suspend();
        var next = new NativeHunterPagePresenter(_pages, new LauncherHunterChoiceStore());
        try
        {
            next.Open();
            _hunterChoice = next;
        }
        catch
        {
            next.Dispose();
            throw;
        }
    }

    private static void CloseNativeHunter()
    {
        var old = _hunterChoice;
        _hunterChoice = null;
        old?.Dispose();
    }

    private static void OpenNativeSocial(RmlUiHost host)
    {
        if (_pages == null || _social != null) return;
        string path = Path.Combine(AppContext.BaseDirectory, "rmlui",
            "pages", "social", "social.rml");
        if (!File.Exists(path))
            throw new FileNotFoundException("Native Social page is not packaged.", path);
        _launcherPages?.Suspend();
        var next = new NativeSocialFriendsPage(host, _pages, new CurrentSocialFriendsService());
        try
        {
            next.Open();
            _social = next;
        }
        catch
        {
            next.Dispose();
            throw;
        }
    }

    private static void CloseNativeSocial()
    {
        NativeSocialFriendsPage? page = _social;
        _social = null;
        page?.Dispose();
    }

    private static void OpenNativeCommunity(RmlUiHost host)
    {
        if (_pages == null || _community != null) return;
        string path = Path.Combine(AppContext.BaseDirectory, "rmlui",
            "pages", "community", "native-discover.rml");
        if (!File.Exists(path))
            throw new FileNotFoundException("Native Community browser is not packaged.", path);
        _launcherPages?.Suspend();
        var next = new NativeCommunityDiscoverPage(host, _pages,
            new NativeCommunityReader(), new NativeCommunityInstaller(),
            new NativeCommunityCreatorService());
        try
        {
            next.Open();
            _community = next;
        }
        catch
        {
            next.Dispose();
            throw;
        }
    }

    private static void CloseNativeCommunity()
    {
        var old = _community;
        _community = null;
        old?.Dispose();
    }

    private static void OpenNativeLicense()
    {
        if (_pages == null || _license != null) return;
        string path = Path.Combine(AppContext.BaseDirectory, "rmlui",
            "pages", "license", "native-overview.rml");
        if (!File.Exists(path))
            throw new FileNotFoundException("Native Hunter License overview is not packaged.", path);
        _launcherPages?.Suspend();
        var next = new NativeLicenseOverviewPage(_pages,
            new CurrentHunterLicenseOverviewReader());
        try
        {
            next.Open();
            _license = next;
        }
        catch
        {
            next.Dispose();
            throw;
        }
    }

    private static void CloseNativeLicense()
    {
        var old = _license;
        _license = null;
        old?.Dispose();
    }

    private static void OpenNativeStudio()
    {
        if (_pages == null || _studio != null) return;
        string path = Path.Combine(AppContext.BaseDirectory, "rmlui",
            "pages", "studio", "native-entry.rml");
        if (!File.Exists(path))
            throw new FileNotFoundException("Native Studio entry is not packaged.", path);
        _launcherPages?.Suspend();
        var next = new NativeStudioEntryPage(_pages, new CurrentStudioLauncher());
        try
        {
            next.Open();
            _studio = next;
        }
        catch
        {
            next.Dispose();
            throw;
        }
    }

    private static void CloseNativeStudio()
    {
        var old = _studio;
        _studio = null;
        old?.Dispose();
    }

    private static float Density(RenderWindow window)
    {
        float value = (float)window.FramebufferSize.X / Math.Max(1, window.ClientSize.X);
        return Single.IsFinite(value) && value > 0 ? value : 1;
    }

    internal static bool PointerMoved(double x, double y)
        => Forward(host => host.Input.PointerMoved(x, y));

    internal static bool PointerButton(MouseButton button, double x, double y, bool down)
    {
        int code = button switch
        {
            MouseButton.Button1 => 0,
            MouseButton.Button2 => 1,
            MouseButton.Button3 => 2,
            _ => -1
        };
        return Forward(host =>
        {
            if (code >= 0) host.Input.PointerButton(code, x, y, down);
        });
    }

    internal static bool Wheel(double delta)
        => Forward(host => host.Input.PointerWheel(delta));

    internal static bool KeyDown(KeyboardKeyEventArgs key)
    {
        // Window-level F11 and Alt+Enter must remain operational while the
        // native menu has focus. The OpenTK window, not either UI toolkit,
        // owns fullscreen changes.
        if (key.Key == Keys.F11 || (key.Key == Keys.Enter && key.Alt))
            return false;
        return Forward(host => RmlUiDesktopInput.KeyDown(host, key));
    }

    internal static bool KeyUp(KeyboardKeyEventArgs key)
        => Forward(host => RmlUiDesktopInput.KeyUp(host, key));

    internal static bool TextInput(string text)
        => Forward(host => host.Input.Text(text));

    private static bool Forward(Action<RmlUiHost> action)
    {
        RmlUiHost? host = _host;
        if (_disabled || !_shown || host is not { Active: true }) return false;
        try
        {
            action(host);
            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            Fail(error);
            return false;
        }
    }

    private static bool Recoverable(Exception error)
        => error is not OutOfMemoryException and not StackOverflowException
            and not AccessViolationException;

    private static void Fail(Exception error)
    {
        _disabled = true;
        try { Console.Error.WriteLine("[rmlui] developer GL smoke disabled: " + error.Message); }
        catch { }
        try { Mods.DebugLog.Line("rmlui", "developer GL smoke disabled: " + error.Message); }
        catch { }
        Shutdown();
    }

    internal static void Shutdown()
    {
        _shown = false;
        RmlUiHunterStageRenderer.Hide();
        _services = null;
        RmlUiMenuController? controller = _controller;
        _controller = null;
        if (controller != null)
        {
            try { controller.Dispose(); }
            catch (Exception error) when (Recoverable(error))
            {
                try { Mods.DebugLog.Line("rmlui", "controller teardown: " + error.Message); }
                catch { }
            }
        }
        try { CloseNativeSocial(); }
        catch (Exception error) when (Recoverable(error))
        {
            try { Mods.DebugLog.Line("rmlui", "social teardown: " + error.Message); }
            catch { }
        }
        try { CloseNativeStudio(); }
        catch (Exception error) when (Recoverable(error))
        {
            try { Mods.DebugLog.Line("rmlui", "studio teardown: " + error.Message); }
            catch { }
        }
        try { CloseNativeLicense(); }
        catch (Exception error) when (Recoverable(error))
        {
            try { Mods.DebugLog.Line("rmlui", "license teardown: " + error.Message); }
            catch { }
        }
        try { CloseNativeCommunity(); }
        catch (Exception error) when (Recoverable(error))
        {
            try { Mods.DebugLog.Line("rmlui", "Community teardown: " + error.Message); }
            catch { }
        }
        try { CloseNativeHunter(); }
        catch (Exception error) when (Recoverable(error))
        {
            try { Mods.DebugLog.Line("rmlui", "hunter picker teardown: " + error.Message); }
            catch { }
        }
        try { CloseNativeQuickSettings(); }
        catch (Exception error) when (Recoverable(error))
        {
            try { Mods.DebugLog.Line("rmlui", "quick settings teardown: " + error.Message); }
            catch { }
        }
        try { CloseNativeNews(); }
        catch (Exception error) when (Recoverable(error))
        {
            try { Mods.DebugLog.Line("rmlui", "news teardown: " + error.Message); }
            catch { }
        }
        var launcher = _launcherPages;
        _launcherPages = null;
        if (launcher != null)
        {
            try { launcher.Dispose(); }
            catch (Exception error) when (Recoverable(error))
            {
                try { Mods.DebugLog.Line("rmlui", "launcher document teardown: " + error.Message); }
                catch { }
            }
        }
        var pages = _pages;
        _pages = null;
        if (pages != null)
        {
            try { pages.Dispose(); }
            catch (Exception error) when (Recoverable(error))
            {
                try { Mods.DebugLog.Line("rmlui", "page teardown: " + error.Message); }
                catch { }
            }
        }
        var old = _host;
        _host = null;
        if (old == null) return;
        try { old.Dispose(); }
        catch (Exception error) when (Recoverable(error))
        {
            try { Mods.DebugLog.Line("rmlui", "native teardown: " + error.Message); }
            catch { }
        }
    }
}
#endif
