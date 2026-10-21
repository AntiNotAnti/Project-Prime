using System;

namespace MphRead.Mods.Launcher.RmlUi.Host
{
    public enum RmlUiLocalDocumentSurface { Home, Offline, Adventure, Shell, OfflineArena }
    /// <summary>Constrains launcher presentation and dispatch to the services supplied by its host.</summary>
    public sealed record RmlUiLauncherCapabilities(bool LocalOnly = false, bool AdventureEnabled = false,
        bool AdventureSaveManagementEnabled = false)
    {
        public static RmlUiLauncherCapabilities Unrestricted { get; } = new();
        public static RmlUiLauncherCapabilities ConfiguredLocal { get; } = new(LocalOnly: true);
        public static RmlUiLauncherCapabilities ConfiguredLocalAdventure { get; } = new(LocalOnly: true, AdventureEnabled: true);
        public static RmlUiLauncherCapabilities ConfiguredLocalAdventureSaves { get; } = new(LocalOnly: true,
            AdventureEnabled: true, AdventureSaveManagementEnabled: true);

        // These are authored DOM IDs, not publisher model keys. Apply after projection so
        // periodic desktop publishers cannot reveal controls the local host cannot serve.
        private static readonly string[] UnsupportedShellElements = { "nav_hunters", "nav_theatre", "nav_community", "nav_studio", "profile", "header_settings", "build_version" };
        private static readonly string[] UnsupportedHomeElements =
        {
            "drawer_browser", "drawer_training", "drawer_adventure",
            "home_drawer_multiplayer", "home_drawer_training", "home_drawer_adventure",
            "home_feature_rail", "home_social", "home_party_slot2", "home_party_slot3", "home_social_summary", "home_social_rail", "home_social_open"
        };
        private static readonly string[] UnsupportedOfflineElements = { "offline_training_panel", "offline_rules_open" };
        private static readonly string[] UnsupportedArenaElements = { "offline_arena_search", "offline_arena_search_apply" };

        public bool Allows(in RmlUiIntent intent)
        {
            if (!LocalOnly) return true;
            if (!RmlUiIntentRegistry.IsValid(intent.Kind, intent.Argument)) return false;
            return intent.Kind switch
            {
                RmlUiIntentKind.Navigate => intent.Argument is (int)RmlUiRouteArgument.Home
                    or (int)RmlUiRouteArgument.Offline || AdventureEnabled && intent.Argument == (int)RmlUiRouteArgument.Adventure,
                RmlUiIntentKind.StageSelect or RmlUiIntentKind.StagePreview => intent.Argument == 2 || AdventureEnabled && intent.Argument == 3,
                RmlUiIntentKind.AdventureSelectSlot or RmlUiIntentKind.AdventureNextHunter
                    or RmlUiIntentKind.AdventureContinue or RmlUiIntentKind.AdventureNewRun
                    or RmlUiIntentKind.AdventureConfirmNewRun or RmlUiIntentKind.AdventureCancelNewRun
                    or RmlUiIntentKind.AdventureRefresh => AdventureEnabled,
                RmlUiIntentKind.CampaignShipAction => AdventureEnabled && intent.Argument == 10,
                RmlUiIntentKind.AdventureCopy or RmlUiIntentKind.AdventureCopyDestination
                    or RmlUiIntentKind.AdventureConfirmCopy or RmlUiIntentKind.AdventureArchive
                    or RmlUiIntentKind.AdventureConfirmArchive or RmlUiIntentKind.AdventureCancelSaveChange
                    => AdventureEnabled && AdventureSaveManagementEnabled,
                RmlUiIntentKind.OfflineChoice or RmlUiIntentKind.OfflineChoicePrevious => intent.Argument is >= 0 and <= 6 or 16,
                RmlUiIntentKind.Quit or RmlUiIntentKind.HomeDrawerOpen
                    or RmlUiIntentKind.HomeDrawerClose or RmlUiIntentKind.HomeDeploy
                    or RmlUiIntentKind.OfflineOpenArena or RmlUiIntentKind.OfflineSelectArena
                    or RmlUiIntentKind.OfflineArenaPage or RmlUiIntentKind.OfflineCancelArena
                    or RmlUiIntentKind.OfflineLaunchMatch
                    or RmlUiIntentKind.HunterOpen or RmlUiIntentKind.HunterSelect
                    or RmlUiIntentKind.HunterSuit or RmlUiIntentKind.HunterApply
                    or RmlUiIntentKind.HunterCancel or RmlUiIntentKind.HunterResetPreview => true,
                _ => false
            };
        }

        public void Apply(RmlUiHost host, RmlUiDocumentToken document, RmlUiLocalDocumentSurface surface)
        {
            ArgumentNullException.ThrowIfNull(host);
            if (!Enum.IsDefined(surface)) throw new ArgumentOutOfRangeException(nameof(surface));
            if (!LocalOnly || !host.IsAlive(document)) return;
            if (surface != RmlUiLocalDocumentSurface.OfflineArena)
                foreach (string id in UnsupportedShellElements) host.SetBool(document, "visible:" + id, false);
            string[] specific = surface switch
            {
                RmlUiLocalDocumentSurface.Home => UnsupportedHomeElements,
                RmlUiLocalDocumentSurface.Offline => UnsupportedOfflineElements,
                RmlUiLocalDocumentSurface.OfflineArena => UnsupportedArenaElements,
                _ => Array.Empty<string>()
            };
            foreach (string id in specific)
                if (!AdventureEnabled || id is not ("drawer_adventure" or "home_drawer_adventure"))
                    host.SetBool(document, "visible:" + id, false);
            if (surface == RmlUiLocalDocumentSurface.Home)
                host.SetText(document, "home_drawer_footer", AdventureEnabled
                    ? "LOCAL PLAY AND ADVENTURE // NO 3D PREVIEW IN THIS ROUTE"
                    : "CONFIGURED LOCAL PLAY // NO 3D PREVIEW IN THIS ROUTE");
        }
    }
}
