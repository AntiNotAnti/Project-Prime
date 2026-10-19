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
using MphRead.Mods.Network;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.RmlUi.Render;

/// <summary>
/// Native OpenGL launcher on the existing RenderWindow. The composed
/// Home/Play/Lobby documents share one page manager and the current game's
/// authoritative services. Native-enabled builds use it by default; the explicit
/// environment opt-out is retained for legacy developer diagnostics.
/// </summary>
internal static partial class RmlUiOpenGlSmoke
{
    private static readonly bool Requested =
        !String.Equals(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_GL_SMOKE"),
            "0", StringComparison.Ordinal);

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
    private static NativePartyPage? _party;
    private static NativePresenceController? _presence;
    private static long _lastPresentedRevision;
    private static bool _shown;
    private static bool _attempted, _disabled;
    private static int _width, _height;
    private static float _density;

    internal static bool CapturesInput => !_disabled && _shown && _host is { Active: true };
    internal static bool NativeUiRequired => Requested;

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
            if (_shown && _host is { Active: true } current) ReleaseNativeInput(current);
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
        if (_presence != null)
        {
            string activity = NetSession.Active ? NetSession.IsInLobby ? "lobby" : "in_match"
                : window.HasScene ? "in_match" : "menu";
            if (NetSession.Active && !NetSession.IsInLobby && NetSession.LocalSlot >= 0
                && NetSession.LocalSlot < NetSession.SlotSpectating.Length
                && NetSession.SlotSpectating[NetSession.LocalSlot]) activity = "spectating";
            _presence.Tick(activity, NetSession.ServerSession?.Match.RoomKey ?? "");
        }
        if (window.HasScene) { CloseNativeQuit(); CloseNativeVersions(); CloseNativeUpdate(); CloseNativeStartup(teardown: true); CloseNativeGameFiles(); CloseNativeCampaignEnding(); CloseNativeCampaignShip(); DrawGameplayOverlay(window); return; }
        if (_settingsReturnToPause) CloseFullSettings();
        if (GameplayOpen) CloseGameplayOverlays();
        if (!BeforeLauncherComposite(window)) return;
        RmlUiHost host = _host!;
        try
        {
            int width = window.FramebufferSize.X;
            int height = window.FramebufferSize.Y;
            float density = Density(window);
            if (_width != width || _height != height || _density != density)
            {
                NativeViewportChanged();
                host.Resize(width, height, density);
                _width = width;
                _height = height;
                _density = density;
            }
            host.Input.SetFramebufferScale(
                (float)width / Math.Max(1, window.ClientSize.X),
                (float)height / Math.Max(1, window.ClientSize.Y));
            PumpNativeInput(window, host);
            // Native events are validated by the versioned ABI. Application
            // commands go to one owner-thread controller, not native callbacks
            // or a second matchmaking/network pump.
            RmlUiMenuController controller = _controller!;
            PumpPrimeStudioApplication(window);
            for (int i = 0; i < 64 && host.TryTakeIntent(out RmlUiIntent intent); i++)
            {
                if (HandleNativeQuit(intent)) continue;
                if (HandleNativeStartup(intent)) continue;
                if (HandleNativeGameFiles(intent)) continue;
                if (HandleNativeUpdate(intent)) continue;
                if (HandleNativeVersions(intent)) continue;
                if (intent.Kind == RmlUiIntentKind.SettingsAction && intent.Argument == 250)
                {
                    if (_pages?.Accept(intent) == true) Shell.RmlUiOpenVersions();
                    continue;
                }
                if (HandleFullSettings(intent)) continue;
                if (intent.Kind == RmlUiIntentKind.Quit)
                {
                    if (_pages?.Accept(intent) == true) OpenNativeQuit();
                    continue;
                }
                if (_campaignEnding is { } ending)
                {
                    ending.Handle(intent);
                    ApplyCampaignEndingReturn();
                    continue;
                }
                if (_campaignShip is { } ship)
                {
                    ship.Handle(intent);
                    ApplyCampaignShipEffect();
                    continue;
                }
                // The composed page manager owns foreground document/sequence
                // admission. Historical home-only events no longer bypass it.
                bool baselineIntent = _launcherPages is { Suspended: false }
                    && _news == null && _quickSettings == null && _fullSettings == null && _hudEditor == null && _hunterChoice == null
                    && _community == null && _license == null && _studio == null && _studioEditor == null
                    && _social == null && _party == null && _campaignEnding == null && _campaignShip == null && _startupNative == null;
                if (baselineIntent)
                {
                    if (!_launcherPages!.HandleIntent(intent, out RmlUiIntent forwarded))
                        continue;
                    if (forwarded.Kind == 0) continue;
                    intent = forwarded;
                }
                // Native header routes share the controller's admission lock.
                // Do not let a session finish joining behind another native page.
                if (controller.Joining && intent.Kind is not
                    (RmlUiIntentKind.PlayCancel or RmlUiIntentKind.PlayQueueAction or RmlUiIntentKind.Quit))
                {
                    controller.Dispatch(intent.Kind, intent.Argument);
                    continue;
                }
                if (HandleNativeStudioRoute(intent)) continue;
                if (intent.Kind == RmlUiIntentKind.LicenseOpen)
                {
                    if (_license != null) continue;
                    if (!baselineIntent && (_pages == null || !_pages.Accept(intent))) continue;
                    if (_pages is { } licensePages && licensePages.Page != default) OpenNativeLicense(overlay: true);
                    continue;
                }
                if (_party != null && intent.Kind == RmlUiIntentKind.SocialAction)
                {
                    _party.Handle(intent);
                    if (ApplyNativePartyJoin()) continue;
                    if (_party.Closed) { CloseNativeSocial(); OpenNativeSocial(host); }
                    continue;
                }
                if (_social != null && intent.Kind == RmlUiIntentKind.SocialAction)
                {
                    NativeSocialFriendsPage page = _social;
                    page.Handle(intent);
                    ApplyNativeSocialJoin();
                    if (page.Closed) CloseNativeSocial();
                    else if (page.OpenPartyRequested)
                    {
                        string target = page.PartyInvitePrimeId;
                        CloseNativeSocial();
                        OpenNativeParty(host, target);
                    }
                    continue;
                }
                if (intent.Kind == RmlUiIntentKind.OpenStudio)
                {
                    // Baseline intents were admitted by the composed page manager.
                    // Standalone header commands must belong to the foreground document.
                    if (!baselineIntent && (_pages == null || !_pages.Accept(intent))) continue;
                    if (_studio != null) continue;
                    if (_license is { IsOverlay: true }) CloseNativeLicense();
                    if (!PrepareFullSettingsNavigation()) continue;
                    if ((_quickSettings is { } studioSettings && !studioSettings.PrepareNavigation())
                        || (_hunterChoice is { } studioHunterDraft && !studioHunterDraft.PrepareNavigation())) continue;
                    OpenPrimeStudioDestination();
                    continue;
                }
                if (_license != null && intent.Kind == RmlUiIntentKind.LicenseAction)
                {
                    NativeLicenseOverviewPage license = _license;
                    license.Handle(intent);
                    if (license.Closed) CloseNativeLicense();
                    else if (license.OpenCustomizationRequested)
                    {
                        CloseNativeLicense();
                        if ((_quickSettings is { CanNavigateAway: false })
                            || (_hunterChoice is { } licenseHunterDraft && !licenseHunterDraft.PrepareNavigation())) continue;
                        CloseNativeSocial();
                        CloseNativeStudio();
                        CloseNativeCommunity();
                        CloseNativeNews();
                        CloseNativeQuickSettings();
                        OpenNativeHunter(focusBeamGallery: true);
                    }
                    continue;
                }
                // Native Community installs immutable packages through the shared
                // verified queue; creator actions still belong to Studio.
                if (_community != null && RmlUiPageCommandRouting.Community(intent.Kind))
                {
                    NativeCommunityDiscoverPage page = _community;
                    page.Handle(intent);
                    continue;
                }
                // Foreground document, sequence and generation fences reject
                // stale input from pages hidden by the native router.
                if (_hunterChoice != null && RmlUiPageCommandRouting.Hunter(intent.Kind))
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
                        TryOpenNativeFullSettings(window);
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
                    if (Shell.StudioWindow)
                    {
                        if (intent.Argument == (int)RmlUiRouteArgument.Home)
                        {
                            if ((baselineIntent || _pages?.Accept(intent) == true) && PrepareFullSettingsNavigation()) OpenPrimeStudioDestination();
                            continue;
                        }
                        if (intent.Argument != (int)RmlUiRouteArgument.Theatre
                            && intent.Argument != (int)RmlUiRouteArgument.Settings) continue;
                    }
                    if (_studio is { ShowingChooser: false } mapStudio
                        && intent.Document == mapStudio.Document
                        && intent.Argument == (int)RmlUiRouteArgument.Theatre) continue;
                    if (!baselineIntent && (_pages == null || !_pages.Accept(intent))) continue;
                    if (_license is { IsOverlay: true }) CloseNativeLicense();
                    // An active tab is idempotent, including while its draft
                    // is dirty. It must never fall through to the old router.
                    if (intent.Argument == (int)RmlUiRouteArgument.Community && _community != null
                        || intent.Argument == (int)RmlUiRouteArgument.HunterLicense && _hunterChoice != null
                        || intent.Argument == (int)RmlUiRouteArgument.Settings && (_quickSettings != null || _fullSettings != null)
                        || intent.Argument == (int)RmlUiRouteArgument.News && _news != null
                        || intent.Argument == (int)RmlUiRouteArgument.Social && _social != null) continue;
                    // Treat unsaved native drafts like the current legacy route
                    // guard. A header/tab click must not discard silently.
                    if (!PrepareFullSettingsNavigation()) continue;
                    if ((_quickSettings is { } settingsDraft && !settingsDraft.PrepareNavigation())
                        || (_hunterChoice is { } hunterDraft && !hunterDraft.PrepareNavigation())) continue;
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
                        TryOpenNativeFullSettings(window);
                        continue;
                    }
                    if (intent.Argument == (int)RmlUiRouteArgument.HunterLicense
                        && _hunterChoice != null) continue;
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
                        && (_news != null || _quickSettings != null || _fullSettings != null
                            || _hunterChoice != null || _community != null
                            || _license != null || _studio != null || _social != null || _party != null))
                    {
                        CloseNativeSocial();
                        CloseNativeStudio();
                        CloseNativeLicense();
                        CloseNativeCommunity();
                        CloseNativeHunter();
                        CloseNativeQuickSettings();
                        CloseNativeNews();
                        controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home);
                        continue;
                    }
                }
                // Page-specific actions never fall through to the Home model.
                if (_hunterChoice != null || _quickSettings != null || _fullSettings != null
                    || _news != null || _community != null || _license != null
                    || _studio != null || _social != null || _party != null)
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
                string player = intent.Kind is RmlUiIntentKind.PlayJoin or RmlUiIntentKind.PlayServer or RmlUiIntentKind.PlayQuick
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
                    theatreSearch,
                    offlinePoints: intent.Kind == RmlUiIntentKind.OfflineApplyRules ? layout?.ReadField("offline_point_goal") ?? "" : "",
                    offlineLimit: intent.Kind == RmlUiIntentKind.OfflineApplyRules ? layout?.ReadField("offline_time_limit") ?? "" : "",
                    offlineGoal: intent.Kind == RmlUiIntentKind.OfflineApplyRules ? layout?.ReadField("offline_time_goal") ?? "" : "",
                    theatreInputs: intent.Kind == RmlUiIntentKind.TheatreAction ? new(
                        intent.Argument == 12 ? layout?.ReadField("theatre_rename") ?? "" : "",
                        intent.Argument == 13 ? layout?.ReadField("theatre_tags") ?? "" : "",
                        intent.Argument == 13 ? layout?.ReadField("theatre_collections") ?? "" : "",
                        (intent.Argument is 19 or 20) ? layout?.ReadField("theatre_import_path", 16384) ?? "" : "") : default);
                if (handled && intent.Kind == RmlUiIntentKind.TheatreEntry)
                {
                    var replayFields = controller.TheatreInputs;
                    layout?.SetField("theatre_rename", replayFields.Name);
                    layout?.SetField("theatre_tags", replayFields.Tags);
                    layout?.SetField("theatre_collections", replayFields.Collections);
                }
                if (handled && intent.Kind == RmlUiIntentKind.OfflineOpenRules
                    && controller.OfflineRuleInputs is { } offlineFields)
                {
                    layout?.SetField("offline_point_goal", offlineFields.PointGoal);
                    layout?.SetField("offline_time_limit", offlineFields.TimeLimit);
                    layout?.SetField("offline_time_goal", offlineFields.TimeGoal);
                }
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
                ReleaseNativeInput(host);
                return;
            }
            RefreshNativeGameFiles();
            TickNativeVersions();
            TickFullSettings();
            _community?.Refresh();
            if (_community is { } community && community.TryTakeHostRequest(out var hostedMap)
                && hostedMap != null)
            {
                if (controller.OpenCreateLobbyForInstalled(hostedMap)) CloseNativeCommunity();
                else community.ShowHostFailure("HOST HANDOFF UNAVAILABLE // LEAVE THE ACTIVE LOBBY OR FINISH CONNECTING");
            }
            _social?.Refresh();
            ApplyNativeSocialJoin();
            _party?.Refresh();
            ApplyNativePartyJoin();
            _license?.Refresh();
            if (_license?.Closed == true) CloseNativeLicense();
            _studio?.Refresh();
            RefreshNativeStudioRoute();
            if (_studio?.Closed == true) CloseNativeStudio();
            _hunterChoice?.Pump();
            _campaignEnding?.Refresh();
            _campaignShip?.Refresh();
            RmlUiMenuSnapshot snapshot = controller.Tick();
            if (Shell.StudioWindow && !Shell.NativeStartupPending && !controller.TheatreOpen && _studio == null && _quickSettings == null
                && _fullSettings == null && _hudEditor == null
                && _gameFiles == null && !window.HasScene) OpenNativeStudio();
            RmlUiLauncherPages? launcher = _launcherPages;
            if (launcher != null)
            {
                launcher.SetBool("reduce_motion", LauncherPrefs.ReduceMotion);
                bool routeOwned = _startupNative != null || _gameFiles != null || _news != null || _quickSettings != null || _fullSettings != null || _hudEditor != null
                    || _hunterChoice != null || _community != null || _license != null
                    || _studio != null || _studioEditor != null || _social != null || _party != null || _campaignEnding != null || _campaignShip != null;
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
            _startupNative?.Refresh(width, height, LauncherPrefs.ReduceMotion);
            PresentApplicationHeader(host);
            PresentNativeFeedback(host);
            host.Update();
            PublishNativeStudioLayoutBasis(window, width, height);
            RefreshNativeCandidate();
            launcher?.AfterUpdate();
            _gameFiles?.AfterUpdate();
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
                    LauncherBackdropScene? background = _hunterChoice != null ? LauncherBackdropScene.Home
                        : _social != null || _party != null ? LauncherBackdropScene.Play
                        : _news != null || _community != null ? LauncherBackdropScene.ReplayStudio
                        : _fullSettings != null || _quickSettings != null || _hudEditor != null ? LauncherBackdropScene.Settings
                        : _studio != null ? LauncherBackdropScene.ReplayStudio
                        : _launcherPages?.Suspended == true ? LauncherBackdropScene.Home : null;
                    RmlUiHunterStageRenderer.Draw(window, host, launcher.Page,
                        launcher.Suspended && _license?.IsOverlay != true && background == null,
                        services.Hub(), lobby, roster, width, height, background, launcher.Document);
                }
                catch (Exception stageError) when (Recoverable(stageError))
                {
                    RmlUiHunterStageRenderer.Hide();
                    Mods.DebugLog.Line("rmlui",
                        "3D stage unavailable; retaining native page controls: "
                        + stageError.Message);
                }
            }
            _studioEditor?.DrawViewport(width, height, window.ClientSize.X, window.ClientSize.Y);
            RmlUiOpenGlFrame.Draw(host, width, height);
            // Keep becomes available only after the foreground confirmation
            // has reached a usable native frame in the requested video mode.
            if (window.IsFocused && width > 0 && height > 0)
                _fullSettings?.AcknowledgeUsableFrame();
            if (_controller?.HunterOpen == true && launcher?.Page == RmlUiMenuPage.Lobby
                && _pages != null && HubState.Capture().GameFilesReady)
                RmlUiHunterStageRenderer.DrawLobbyHunterCards(window,host,_pages.Top,
                    _controller.HunterDraftSuit,width,height);
            // The full native Hunter picker owns its own model rectangle.
            // Unlike the page's opaque chrome, its isolated viewport is drawn
            // last so the actual model cannot disappear behind a UI card.
            if (_license == null && _hunterChoice is { } picker && HubState.Capture().GameFilesReady)
            {
                try
                {
                    var selected = picker.PreviewSelection;
                    RmlUiHunterStageRenderer.DrawPicker(window, host,
                        picker.PreviewDocument, selected.Hunter, selected.Suit,
                        selected.Cosmetics, picker.PreviewState, width, height);
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
            var ending = _campaignEnding?.Receipt;
            var dock = _campaignShip?.Dock;
            Fail(error);
            if (ending.HasValue) Shell.OpenNativeCampaignEndingHandoff(ending.Value);
            else if (dock.HasValue) Shell.OpenNativeCampaignShipHandoff(new(dock.Value.Slot, (Campaign.CampaignPlanet)dock.Value.Planet));
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
            var services = new RmlUiLiveMenuServices(() => _presence?.Current());
            _services = services;
            _controller = new RmlUiMenuController(services);
            _pages = new RmlUiPageManager(host);
            _pages.IntentAccepted += NativeIntentFeedback;
            AttachNativeInput(host,window);
            _launcherPages = new RmlUiLauncherPages(host, _pages);
            _launcherPages.ShowBaseline();
            _presence = Shell.StudioWindow ? null : new(new AuthenticatedNativePresenceService());
            Mods.Replay.ReplayInput.EditingAllowed = Shell.StudioWindow;
            _lastPresentedRevision = 0;
            string player = HubState.Capture().PlayerName;
            _launcherPages.SetField("play_player_name", player);
            _launcherPages.SetField("play_create_player_name", player);
            if (Shell.StudioWindow && !Shell.NativeStartupPending) OpenNativeStudio();
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
        CloseFullSettings();
        var old = _quickSettings;
        _quickSettings = null;
        old?.Dispose();
    }

    private static void OpenNativeHunter(bool focusBeamGallery = false)
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
            next.Open(focusBeamGallery);
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
        var next = new NativeSocialFriendsPage(host, _pages, new CurrentSocialFriendsService(), _presence);
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
        _controller?.CancelSocialConnection();
        var party = _party;
        _party = null;
        _services?.SetHomePartyPageActive(false);
        party?.Dispose();
        NativeSocialFriendsPage? page = _social;
        _social = null;
        page?.Dispose();
    }

    private static void ApplyNativeSocialJoin()
    {
        if (_social is not { } page) return;
        if (!page.JoinPending) _controller?.CancelSocialConnection();
        if (!page.TryTakeJoin(out var target)) return;
        if (_controller?.ConnectSocial(target, HubState.Capture().PlayerName,
            () => ReferenceEquals(_social, page) && page.IsJoinCurrent(target),
            (accepted, message) => {
                page.JoinCompleted(target, accepted, message);
                if (accepted && ReferenceEquals(_social, page)) CloseNativeSocial();
            }) != true)
            page.JoinCompleted(target, false, "FINISH THE CURRENT REQUEST OR LEAVE THE CURRENT SESSION, THEN RETRY");
    }

    private static void OpenNativeParty(RmlUiHost host, string target)
    {
        if (_pages == null || _party != null) return;
        _launcherPages?.Suspend();
        var next = new NativePartyPage(host, _pages, new AuthenticatedNativePartyAuthority(),
            _services?.HomePartyController);
        try { next.Open(target); _party = next; _services?.SetHomePartyPageActive(true); }
        catch { next.Dispose(); throw; }
    }

    internal static Func<INativeCommunityReader>? CommunityReaderOverride { get; set; }
    private static void OpenNativeCommunity(RmlUiHost host)
    {
        if (_pages == null || _community != null) return;
        string path = Path.Combine(AppContext.BaseDirectory, "rmlui",
            "pages", "community", "native-discover.rml");
        if (!File.Exists(path))
            throw new FileNotFoundException("Native Community browser is not packaged.", path);
        _launcherPages?.Suspend();
        var next = new NativeCommunityDiscoverPage(host, _pages,
            CommunityReaderOverride?.Invoke() ?? new NativeCommunityReader(), new NativeCommunityInstaller(),
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

    private static void OpenNativeLicense(bool overlay = false)
    {
        if (_pages == null || _license != null) return;
        string path = Path.Combine(AppContext.BaseDirectory, "rmlui",
            "pages", "license", "native-overview.rml");
        if (!File.Exists(path))
            throw new FileNotFoundException("Native Hunter License overview is not packaged.", path);
        _launcherPages?.Suspend();
        var next = new NativeLicenseOverviewPage(_pages,
            new CurrentHunterLicenseOverviewReader(), _host);
        try
        {
            next.Open(overlay);
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

    private static void OpenNativeStudio(bool showChooser = true)
    {
        if (_pages == null || _studio != null) return;
        string path = Path.Combine(AppContext.BaseDirectory, "rmlui",
            "pages", "studio", "native-entry.rml");
        if (!File.Exists(path))
            throw new FileNotFoundException("Native Studio entry is not packaged.", path);
        _launcherPages?.Suspend();
        var next = new NativeStudioEntryPage(_host!, _pages, new CurrentStudioLauncher(), Shell.StudioWindow);
        try
        {
            next.Open(showChooser);
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
        CloseNativeStudioProjectChange();
        CloseNativeStudioPublish();
        CloseNativeStudioEditor();
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
        => Forward(host =>
        {
            if (!NativeStudioInputReady(host)) return;
            if (_fullSettings?.VisualEditorOpen==true && _inputWindow is {} visualWindow)
            {
                _fullSettings.VisualPointerMove((float)(x*visualWindow.FramebufferSize.X/Math.Max(1,visualWindow.ClientSize.X)),
                    (float)(y*visualWindow.FramebufferSize.Y/Math.Max(1,visualWindow.ClientSize.Y)));
                host.Input.PointerMoved(x,y);return;
            }
            HudPointerMove(x, y);
            _playback?.Viewport.PointerMoveInWindow(x,y);
            _studioEditor?.PointerMoved(x, y);
            host.Input.PointerMoved(x,y);
            if(_inputWindow is{} timelineWindow&&_playback?.TimelineOpen==true)
            {
                var metrics = ReplayTimelineMetrics(timelineWindow);
                if (!_playback.StudioTimelinePointerMove(x,y,metrics,timelineWindow.IsFocused))
                    _playback.TimelinePointerMove(x,y,metrics,timelineWindow.IsFocused);
            }
            if (_inputWindow is { } curveWindow && _playback?.CameraCurvesOpen == true)
                _playback.CameraCurvePointerMove(x, y, ReplayTimelineMetrics(curveWindow), curveWindow.IsFocused);
        });

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
            if (!NativeStudioInputReady(host)) return;
            if (_fullSettings?.VisualEditorOpen==true && _inputWindow is {} visualWindow)
            {
                float px=(float)(x*visualWindow.FramebufferSize.X/Math.Max(1,visualWindow.ClientSize.X));
                float py=(float)(y*visualWindow.FramebufferSize.Y/Math.Max(1,visualWindow.ClientSize.Y));
                if (code==0) { if(down)_fullSettings.VisualPointerDown(px,py);else _fullSettings.VisualPointerUp(); }
                if(code>=0)host.Input.PointerButton(code,x,y,down);return;
            }
            if (down && _fullSettings?.MouseDown(button) == true) return;
            if (HudPointerButton(code, x, y, down)) return;
            if (_studioEditor?.PointerButton(code, x, y, down) == true) return;
            if (_inputWindow is { } curveWindow && _playback?.CameraCurvesOpen == true
                && _playback.CameraCurveInputAt(x,y,ReplayTimelineMetrics(curveWindow)))
            {
                if (code >= 0) host.Input.PointerButton(code, x, y, down);
                _playback.CameraCurvePointerButton(code, x, y, down, ReplayTimelineMetrics(curveWindow), curveWindow.IsFocused,
                    HudModifier(Keys.LeftAlt,Keys.RightAlt),HudModifier(Keys.LeftShift,Keys.RightShift));
                return;
            }
            if(_inputWindow is{} timelineWindow&&_playback?.TimelineOpen==true
                && _playback.TimelineInputAt(x,y,ReplayTimelineMetrics(timelineWindow)))
            {
                if(code>=0)host.Input.PointerButton(code,x,y,down);
                var metrics=ReplayTimelineMetrics(timelineWindow);
                if (!_playback.StudioTimelinePointerButton(code,x,y,down,metrics,timelineWindow.IsFocused,
                    HudModifier(Keys.LeftShift,Keys.RightShift)))
                    _playback.TimelinePointerButton(code,x,y,down,metrics,timelineWindow.IsFocused);
                return;
            }
            ReplayPointerButton(code,x,y,down);
            if (code >= 0) host.Input.PointerButton(code, x, y, down);
        });
    }

    internal static bool Wheel(double delta)
        => Forward(host =>
        {
            if (!NativeStudioInputReady(host)) return;
            if (_inputWindow is { } window && _studioEditor?.Wheel(window.MousePosition.X, window.MousePosition.Y, delta) == true) return;
            if (!HudModifier(Keys.LeftAlt,Keys.RightAlt) && _inputWindow is { } timelineWindow && _playback?.StudioTimelineWheel(timelineWindow.MousePosition.X,
                timelineWindow.MousePosition.Y,delta,HudModifier(Keys.LeftShift,Keys.RightShift),
                ReplayTimelineMetrics(timelineWindow),timelineWindow.IsFocused)==true) return;
            if (_fullSettings?.ScrollBinding(delta > 0) == true) return;
            host.Input.PointerWheel(delta);
        });

    private static bool StudioTimelineShortcut(KeyboardKeyEventArgs key)
    {
        string command=key.Key switch
        {
            Keys.Space=>"play", Keys.Left=>key.Shift?"previous-second":"previous-frame",
            Keys.Right=>key.Shift?"next-second":"next-frame",Keys.Home=>"home",Keys.End=>"end",_=>""
        };
        return command.Length>0 && _playback?.StudioTimelineShortcut(command)==true;
    }

    internal static bool KeyDown(KeyboardKeyEventArgs key)
    {
        // Window-level F11 and Alt+Enter must remain operational while the
        // native menu has focus. The OpenTK window, not either UI toolkit,
        // owns fullscreen changes.
        if (key.Key == Keys.F11 || (key.Key == Keys.Enter && key.Alt))
            return _fullSettings?.ConfirmingVideo == true;
        if (NativeReplayKeyboardFocused && CapturesInput)
            return Forward(_ => { if (!StudioTimelineShortcut(key)) ReplayKeyDown(key.Key); });
        if (key.Key == Keys.Escape)
            return Forward(_ => BackNativeMenu());
        return Forward(host =>
        {
            var modifiers = (key.Shift ? RmlUiInputModifiers.Shift : 0)
                | (key.Control ? RmlUiInputModifiers.Control : 0) | (key.Alt ? RmlUiInputModifiers.Alt : 0);
            if (StudioTimelineShortcut(key)) return;
            if (_hudEditor?.KeyDown(key.Key, modifiers) == true || _fullSettings?.KeyDown(key.Key, modifiers) == true) return;
            if (!RmlUiLinuxDesktopInput.Process(_linuxIme,key,released:false))
                RmlUiDesktopInput.KeyDown(host, key);
        });
    }

    internal static bool KeyUp(KeyboardKeyEventArgs key)
        => Forward(host =>
        {
            if (ReplayKeyUp(key.Key)) return;
            if (!RmlUiLinuxDesktopInput.Process(_linuxIme,key,released:true))
                RmlUiDesktopInput.KeyUp(host,key);
        });

    internal static bool TextInput(string text)
        => Forward(host =>
        {
            if (_linuxIme?.SuppressCharacterCallback() == true) return;
            if (host.TryGetTextInputState(out var state) && !state.Composing)
                host.Input.Dispatch(new(state.Document,RmlUiPlatformInputKind.TextCommitted,
                    RmlUiInputDevice.Keyboard,state.FocusEpoch,Text:text));
        });

    private static bool Forward(Action<RmlUiHost> action)
    {
        RmlUiHost? host = _host;
        if (_disabled || !_shown || host is not { Active: true }) return false;
        if (_inputWindow is not { IsFocused:true }) return true;
        if (GameplayOpen && !GameplayLeaseCurrent)
        { CloseGameplayOverlays(); _shown = false; return false; }
        bool gameplay = GameplayOpen;
        try
        {
            action(host);
            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            if (gameplay)
            {
                RecoverGameplayPresentation(error);
                return true;
            }
            Fail(error);
            return false;
        }
    }

    private static bool Recoverable(Exception error)
        => error is not OutOfMemoryException and not StackOverflowException
            and not AccessViolationException;

    private static void Fail(Exception error)
    {
        _ = TakeNativeGameFilesFallback();
        _disabled = true;
        try { Console.Error.WriteLine("[rmlui] native UI failed; closing the application: " + error.Message); }
        catch { }
        try { Mods.DebugLog.Line("rmlui", "native UI failed; closing the application: " + error.Message); }
        catch { }
        Shutdown();
        Environment.ExitCode = 1;
        Shell.RequestQuit();
    }

    internal static void Shutdown()
    {
        DisposeNativeInput();
        CloseNativeQuit();
        CloseNativeVersions();
        CloseFullSettings();
        try { CloseNativeUpdate(); }
        catch (Exception error) when (Recoverable(error)) { Mods.DebugLog.Line("rmlui", "update teardown: " + error.Message); }
        try { CloseNativeStartup(teardown: true); }
        catch (Exception error) when (Recoverable(error)) { Mods.DebugLog.Line("rmlui", "startup teardown: " + error.Message); }
        try { CloseNativeGameFiles(teardown: true); }
        catch (Exception error) when (Recoverable(error)) { Mods.DebugLog.Line("rmlui", "setup teardown: " + error.Message); }
        try { CloseNativeCampaignEnding(); }
        catch (Exception error) when (Recoverable(error)) { Mods.DebugLog.Line("rmlui", "ending teardown: " + error.Message); }
        try { CloseNativeCampaignShip(); }
        catch (Exception error) when (Recoverable(error)) { Mods.DebugLog.Line("rmlui", "ship teardown: " + error.Message); }
        var presence = _presence; _presence = null; presence?.Dispose();
        CloseGameplayOverlays();
        _shown = false;
        RmlUiHunterStageRenderer.Hide();
        var services = _services; _services = null; services?.Dispose();
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
