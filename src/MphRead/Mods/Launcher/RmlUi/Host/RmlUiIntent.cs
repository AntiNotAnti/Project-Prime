using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MphRead.Mods.Launcher.RmlUi.Host
{
    // These numeric values are a wire contract. Add values; never renumber them.
    public enum RmlUiIntentKind : uint
    {
        Navigate = 1, Quit = 2, OpenStudio = 3, StageSelect = 4, StagePreview = 5,
        HomeDrawerOpen = 6, HomeDrawerClose = 7, HomeDeploy = 8,
        PlayQuick = 10, PlayBrowse = 11, PlayCreateOpen = 12, PlayCreate = 13,
        PlayJoin = 14, PlayCancel = 15, PlayNextMap = 16, PlayNextMode = 17,
        PlayToggleHost = 18, PlayServer = 19,
        PlayQueueAction = 20,
        LobbyReady = 30, LobbyStart = 31, LobbyLeave = 32, LobbyNextHunter = 33,
        LobbyNextSuit = 34, LobbyClassic = 35, LobbyRulesOpen = 36,
        LobbyRulesClose = 37, LobbyRulesApply = 38, LobbyRulesMap = 39,
        LobbyRulesMode = 40, LobbyRulesFormat = 41, LobbyRulesToggle = 42,
        LobbyAdminOpen = 50,
        LobbyPlayerSelect = 51,
        LobbyKick = 52,
        LobbyTransferOwner = 53,
        LobbyClose = 54,
        LobbyBotAdd = 55,
        LobbyBotRemove = 56,
        LobbyBotConfigure = 57,
        LobbyHandicapNext = 58,
        LobbyTeamNext = 59,
        LobbyTeamsAuto = 60,
        LobbyTeamLockToggle = 61,
        LobbyChatSend = 62,
        LobbyMapRetry = 63,
        LobbyBotHunterNext = 64,
        LobbyBotSuitNext = 65,
        LobbyBotLevelNext = 66,
        LobbyAdminConfirm = 67,
        LobbyAdminCancel = 68,
        LobbyAdminClose = 69,
        HunterOpen = 70,
        HunterSelect = 71,
        HunterSuit = 72,
        HunterSpectator = 73,
        HunterApply = 74,
        HunterCancel = 75,
        HunterSkinNext = 76,
        HunterArmorNext = 77,
        HunterDeathNext = 78,
        HunterCosmeticsReset = 79,
        HunterPreviewMode = 80,
        HunterPreviewDeath = 81,
        HunterCosmeticsSave = 82,
        HunterLoopDeath = 85,
        HunterCompareNative = 86,
        HunterResetPreview = 87,
        HunterRotate = 83,
        HunterZoom = 84,
        LobbyTeamSelect = 88,
        SettingsApply = 100,
        SettingsDiscard = 101,
        SettingsCategory = 102,
        SettingsResetCategory = 103,
        SettingsClose = 104,
        SettingsKeepVideo = 105,
        SettingsRevertVideo = 106,
        SettingsAction = 107,
        AdventureSelectSlot = 120,
        AdventureNextHunter = 121,
        AdventureContinue = 122,
        AdventureNewRun = 123,
        AdventureConfirmNewRun = 124,
        AdventureCancelNewRun = 125,
        AdventureRefresh = 126,
        OfflineChoice = 140,
        OfflineRuleToggle = 141,
        OfflineTrainingToggle = 142,
        OfflineDamage = 143,
        OfflineLaunchMatch = 144,
        OfflineOpenRules = 145,
        OfflineCancelRules = 146,
        OfflineApplyRules = 147,
        OfflineLaunchTraining = 148,
        OfflineOpenTrainingOptions = 149,
        OfflineCloseTrainingOptions = 150,
        OfflineOpenArena = 151,
        OfflineSelectArena = 152,
        OfflineArenaPage = 153,
        OfflineArenaSearch = 154,
        OfflineCancelArena = 155,
        TheatreAction = 160,
        TheatreEntry = 161,
        TheatreThumbnail = 162,
        ReplayAction = 170,
        InGameAction = 180,
        LicenseAction = 190,
        StudioAction = 200,
        CommunityRefresh = 210,
        CommunityTab = 211,
        CommunitySort = 212,
        CommunityLifecycle = 213,
        CommunitySearch = 214,
        CommunityPage = 215,
        CommunitySelect = 216,
        CommunityDetailAction = 217,
        CommunityRevisionPage = 218,
        CommunitySelectRevision = 219,
        CommunityCreatorAction = 220,
        CommunityConfirm = 221,
        CommunityCancel = 222,
        CommunityCancelWork = 223,
        CommunityUpload = 224,
        CommunityVisibility = 225,
        CommunityReport = 226,
        CommunityReportReason = 227,
        CommunityConflict = 228,
        CommunityImport = 229,
        SocialAction = 230,
        NewsAction = 240,
        ResultsAction = 250,
        ResultsMap = 251,
        AimResultsAction = 252,
        SetupAction = 260,
        SetupRelease = 261,
        HudAction = 270,
        HudElement = 271,
        HudProperty = 272
    }

    public enum RmlUiRouteArgument
    {
        Home = 0, Play = 1, Offline = 2, HunterLicense = 3, Community = 4,
        Theatre = 5, Settings = 6, News = 7, Adventure = 8, Social = 9, Training = 10
    }

    public readonly record struct RmlUiDocumentToken(ulong Generation, ulong DocumentId);

    public readonly record struct RmlUiIntent(
        RmlUiIntentKind Kind, int Argument, RmlUiDocumentToken Document, ulong Sequence);

    [StructLayout(LayoutKind.Sequential)]
    public struct RmlUiNativeIntent
    {
        public uint Size;
        public uint Version;
        public uint Kind;
        public int Argument;
        public ulong Generation;
        public ulong DocumentId;
        public ulong Sequence;
    }

    /// <summary>One registry for the versioned ABI and the transitional string buttons.</summary>
    public static class RmlUiIntentRegistry
    {
        public const uint ProtocolVersion = 1;
        public const uint NativeIntentSize = 40;
        private static readonly string[] Routes =
        {
            "home", "play", "offline", "hunter", "forge", "theatre", "settings",
            "news", "adventure", "social", "training"
        };
        private static readonly string[] PlayQueueActions = { "queue-join", "queue-accept", "queue-decline", "queue-leave" };
        private static readonly string[] InGameActions = { "resume", "fullscreen", "settings", "replay", "vote", "spectate", "rejoin", "recorder", "return-lobby", "leave", "quit", "confirm", "cancel", "map-next", "vote-submit", "vote-yes", "vote-no", "bots", "bot-select", "bot-hunter", "bot-suit", "bot-skill", "bot-team", "bot-handicap", "bot-remove", "bot-add", "bot-apply" };
        private static readonly string[] TheatreActions = { "search", "next-filter", "next-sort", "previous-page", "next-page", "refresh", "watch", "favorite", "validate", "recover", "cancel-job", "export", "rename", "organize", "delete", "confirm-delete", "cancel-delete", "reveal", "studio", "import", "import-path", "favorite-filtered", "validate-filtered", "clear-search", "cancel-launch" };
        private static readonly string[] ReplayActions = { "toggle-pause", "jump-back", "jump-forward", "restart", "step", "next-rate", "next-camera", "previous-player", "next-player", "seek", "studio", "back", "fullscreen" };
        private static readonly string[] LicenseActions = { "overview", "customization", "stats", "history", "achievements", "emblems", "titles", "comparison", "account", "refresh", "cancel-refresh", "previous-page", "next-page", "send-verification", "finish-password", "recover", "link-google", "link-github", "link-discord", "refresh-account", "back" };
        private static readonly string[] StudioActions = { "launch", "pick-map", "open-path", "recover", "cancel" };
        private static readonly string[] SocialActions = { "friends", "players", "requests", "invites", "party", "recent", "blocked", "refresh", "cancel", "lookup", "search", "previous-page", "next-page", "select-0", "select-1", "select-2", "select-3", "select-4", "select-5", "select-6", "select-7", "send-friend-request", "accept-friend-request", "decline-friend-request", "cancel-friend-request", "remove-friend", "block-player", "unblock-player", "send-game-invite", "join-friend", "accept-game-invite", "decline-game-invite", "cancel-game-invite", "invite-party", "accept-party-invite", "decline-party-invite", "cancel-party-invite", "leave-party", "disband-party", "kick-party-member", "promote-party-member", "invite-party-lobby", "follow-party-travel", "join-party-leader", "decline-party-travel", "cancel-reservation", "confirm", "dismiss", "presence-next", "activity-next", "invites-next", "dnd", "back" };
        private static readonly string[] ResultsActions = { "close", "search", "previous-page", "next-page", "rematch", "clear-search" };
        private static readonly string[] AimResultsActions = { "retry", "change-drill", "exit" };
        private static readonly string[] HudActions = { "open", "use", "cancel", "next-preset", "undo", "redo", "lock-all", "unlock-all", "align-left", "align-top", "native-elements", "reset-hud", "toggle-visible", "toggle-lock", "next-anchor", "next-visibility", "reset-element", "reset-section", "next-aspect", "next-hunter", "next-scenario", "next-grid", "toggle-guides", "save-named", "load-named", "export-json", "import-json", "property-previous", "property-next", "property-apply", "property-reset", "next-palette", "next-crosshair-target", "toggle-crosshair-override", "next-crosshair-preset", "share-crosshair", "import-crosshair", "next-radar-preset", "nudge-left", "nudge-right", "nudge-up", "nudge-down", "scale-down", "scale-up", "apply-layout" };
        private static readonly string[] Stages = { "quick", "browser", "offline", "adventure", "training" };
        private static readonly IReadOnlyDictionary<RmlUiIntentKind, string> LegacyActions =
            new Dictionary<RmlUiIntentKind, string>
            {
                [RmlUiIntentKind.Quit] = "quit",
                [RmlUiIntentKind.CommunityRefresh] = "community:refresh",
                [RmlUiIntentKind.CommunitySearch] = "community:search",
                [RmlUiIntentKind.CommunityConfirm] = "community:confirm",
                [RmlUiIntentKind.CommunityCancel] = "community:cancel",
                [RmlUiIntentKind.CommunityCancelWork] = "community:cancel-work",
                [RmlUiIntentKind.CommunityReport] = "community:report",
                [RmlUiIntentKind.CommunityImport] = "community:import",

                [RmlUiIntentKind.HomeDrawerOpen] = "home:drawer-open",
                [RmlUiIntentKind.HomeDrawerClose] = "home:drawer-close",
                [RmlUiIntentKind.HomeDeploy] = "home:deploy",
                [RmlUiIntentKind.OpenStudio] = "studio:open",
                [RmlUiIntentKind.PlayQuick] = "play:quick",
                [RmlUiIntentKind.PlayBrowse] = "play:browse",
                [RmlUiIntentKind.PlayCreateOpen] = "play:create-open",
                [RmlUiIntentKind.PlayCreate] = "play:create",
                [RmlUiIntentKind.PlayJoin] = "play:join",
                [RmlUiIntentKind.PlayCancel] = "play:cancel",
                [RmlUiIntentKind.PlayNextMap] = "play:next-map",
                [RmlUiIntentKind.PlayNextMode] = "play:next-mode",
                [RmlUiIntentKind.PlayToggleHost] = "play:toggle-host",
                [RmlUiIntentKind.LobbyReady] = "lobby:ready",
                [RmlUiIntentKind.LobbyStart] = "lobby:start",
                [RmlUiIntentKind.LobbyLeave] = "lobby:leave",
                [RmlUiIntentKind.LobbyNextHunter] = "lobby:next-hunter",
                [RmlUiIntentKind.LobbyNextSuit] = "lobby:next-suit",
                [RmlUiIntentKind.LobbyClassic] = "lobby:classic",
                [RmlUiIntentKind.LobbyRulesOpen] = "lobby:rules-open",
                [RmlUiIntentKind.LobbyRulesClose] = "lobby:rules-close",
                [RmlUiIntentKind.LobbyRulesApply] = "lobby:rules-apply",
                [RmlUiIntentKind.LobbyRulesMap] = "lobby:rules-map",
                [RmlUiIntentKind.LobbyRulesMode] = "lobby:rules-mode",
                [RmlUiIntentKind.LobbyRulesFormat] = "lobby:rules-format",
                [RmlUiIntentKind.LobbyAdminOpen] = "lobby:admin-open",
                [RmlUiIntentKind.LobbyKick] = "lobby:kick",
                [RmlUiIntentKind.LobbyTransferOwner] = "lobby:transfer-owner",
                [RmlUiIntentKind.LobbyClose] = "lobby:close",
                [RmlUiIntentKind.LobbyBotAdd] = "lobby:bot-add",
                [RmlUiIntentKind.LobbyBotRemove] = "lobby:bot-remove",
                [RmlUiIntentKind.LobbyBotConfigure] = "lobby:bot-configure",
                [RmlUiIntentKind.LobbyHandicapNext] = "lobby:handicap-next",
                [RmlUiIntentKind.LobbyTeamNext] = "lobby:team-next",
                [RmlUiIntentKind.LobbyTeamsAuto] = "lobby:teams-auto",
                [RmlUiIntentKind.LobbyTeamLockToggle] = "lobby:team-lock-toggle",
                [RmlUiIntentKind.LobbyChatSend] = "lobby:chat-send",
                [RmlUiIntentKind.LobbyMapRetry] = "lobby:map-retry",
                [RmlUiIntentKind.LobbyBotHunterNext] = "lobby:bot-hunter-next",
                [RmlUiIntentKind.LobbyBotSuitNext] = "lobby:bot-suit-next",
                [RmlUiIntentKind.LobbyBotLevelNext] = "lobby:bot-level-next",
                [RmlUiIntentKind.LobbyAdminConfirm] = "lobby:admin-confirm",
                [RmlUiIntentKind.LobbyAdminCancel] = "lobby:admin-cancel",
                [RmlUiIntentKind.LobbyAdminClose] = "lobby:admin-close",
                [RmlUiIntentKind.HunterOpen] = "hunter:open",
                [RmlUiIntentKind.HunterSpectator] = "hunter:spectator",
                [RmlUiIntentKind.HunterApply] = "hunter:apply",
                [RmlUiIntentKind.HunterCancel] = "hunter:cancel",
                [RmlUiIntentKind.HunterSkinNext] = "hunter:skin-next",
                [RmlUiIntentKind.HunterArmorNext] = "hunter:armor-next",
                [RmlUiIntentKind.HunterDeathNext] = "hunter:death-next",
                [RmlUiIntentKind.HunterCosmeticsReset] = "hunter:cosmetics-reset",
                [RmlUiIntentKind.HunterPreviewDeath] = "hunter:preview-death",
                [RmlUiIntentKind.HunterCosmeticsSave] = "hunter:cosmetics-save",
                [RmlUiIntentKind.HunterLoopDeath] = "hunter:loop-death",
                [RmlUiIntentKind.HunterCompareNative] = "hunter:compare-native",
                [RmlUiIntentKind.HunterResetPreview] = "hunter:reset-preview",
                [RmlUiIntentKind.SettingsApply] = "settings:apply",
                [RmlUiIntentKind.SettingsDiscard] = "settings:discard",
                [RmlUiIntentKind.SettingsResetCategory] = "settings:reset-category",
                [RmlUiIntentKind.SettingsClose] = "settings:close",
                [RmlUiIntentKind.SettingsKeepVideo] = "settings:keep-video",
                [RmlUiIntentKind.SettingsRevertVideo] = "settings:revert-video",
                [RmlUiIntentKind.AdventureNextHunter] = "adventure:next-hunter",
                [RmlUiIntentKind.AdventureContinue] = "adventure:continue",
                [RmlUiIntentKind.AdventureNewRun] = "adventure:new-run",
                [RmlUiIntentKind.AdventureConfirmNewRun] = "adventure:confirm-new-run",
                [RmlUiIntentKind.AdventureCancelNewRun] = "adventure:cancel-new-run",
                [RmlUiIntentKind.AdventureRefresh] = "adventure:refresh",
                [RmlUiIntentKind.OfflineDamage] = "offline:damage",
                [RmlUiIntentKind.OfflineLaunchMatch] = "offline:launch-match",
                [RmlUiIntentKind.OfflineOpenRules] = "offline:open-rules",
                [RmlUiIntentKind.OfflineCancelRules] = "offline:cancel-rules",
                [RmlUiIntentKind.OfflineApplyRules] = "offline:apply-rules",
                [RmlUiIntentKind.OfflineLaunchTraining] = "offline:launch-training",
                [RmlUiIntentKind.OfflineOpenTrainingOptions] = "offline:open-training-options",
                [RmlUiIntentKind.OfflineCloseTrainingOptions] = "offline:close-training-options",
                [RmlUiIntentKind.OfflineOpenArena] = "offline:open-arena",
                [RmlUiIntentKind.OfflineArenaSearch] = "offline:arena-search",
                [RmlUiIntentKind.OfflineCancelArena] = "offline:cancel-arena"
            };

        public static bool IsValid(RmlUiIntentKind kind, int argument) => kind switch
        {
            RmlUiIntentKind.CommunityTab => argument is >= 0 and < 3,
            RmlUiIntentKind.CommunitySort => argument is >= 0 and < 3,
            RmlUiIntentKind.CommunityLifecycle => argument is >= 0 and < 4,
            RmlUiIntentKind.CommunityPage => argument is -1 or 1,
            RmlUiIntentKind.CommunitySelect => argument is >= 0 and < 8,
            RmlUiIntentKind.CommunityDetailAction => argument is >= 0 and < 10,
            RmlUiIntentKind.CommunityRevisionPage => argument is -1 or 1,
            RmlUiIntentKind.CommunitySelectRevision => argument is >= 0 and < 8,
            RmlUiIntentKind.CommunityCreatorAction => argument is >= 0 and < 5,
            RmlUiIntentKind.CommunityUpload => argument is >= 0 and < 2,
            RmlUiIntentKind.CommunityVisibility => argument is >= 0 and < 3,
            RmlUiIntentKind.CommunityReportReason => argument is >= 0 and < 6,
            RmlUiIntentKind.CommunityConflict => argument is >= 0 and < 5,
            RmlUiIntentKind.ResultsAction => argument is >= 0 and < 6,
            RmlUiIntentKind.AimResultsAction => argument is >= 0 and < 3,
            RmlUiIntentKind.SetupAction => argument is >= 0 and < 15,
            RmlUiIntentKind.SetupRelease => argument is >= 0 and < 30,
            RmlUiIntentKind.HudAction => argument is >= 0 and < 45,
            RmlUiIntentKind.ResultsMap => argument is >= 0 and < 4096,
            RmlUiIntentKind.HudElement => argument is >= 0 and < 14,
            RmlUiIntentKind.HudProperty => argument is >= 0 and < 8,
            RmlUiIntentKind.SocialAction => argument is >= 0 and < 53,
            RmlUiIntentKind.NewsAction => argument is >= 0 and < 12,
            RmlUiIntentKind.PlayQueueAction => argument is >= 0 and < 4,
            RmlUiIntentKind.Navigate => argument >= 0 && argument < Routes.Length,
            RmlUiIntentKind.StageSelect or RmlUiIntentKind.StagePreview => argument is >= 0 and < 5,
            RmlUiIntentKind.PlayServer => argument is >= 0 and < 8,
            RmlUiIntentKind.LobbyRulesToggle => argument is >= 0 and < 16,
            RmlUiIntentKind.LobbyPlayerSelect => argument is >= 0 and < 8,
            RmlUiIntentKind.HunterSelect => argument is >= 0 and < 7,
            RmlUiIntentKind.HunterSuit => argument is >= 0 and < 4,
            RmlUiIntentKind.HunterPreviewMode => argument is >= 0 and < 4,
            RmlUiIntentKind.SettingsAction => argument is >= 0 and <= 14 or >= 16 and <= 255,
            RmlUiIntentKind.SettingsCategory => argument is >= 0 and < 13,
            RmlUiIntentKind.AdventureSelectSlot => argument is >= 1 and < 4,
            RmlUiIntentKind.OfflineChoice => argument is >= 0 and < 16,
            RmlUiIntentKind.OfflineRuleToggle => argument is >= 0 and < 12,
            RmlUiIntentKind.OfflineTrainingToggle => argument is >= 0 and < 5,
            RmlUiIntentKind.HunterRotate => argument is >= 0 and < 2,
            RmlUiIntentKind.LobbyTeamSelect => argument is >= 0 and < 4,
            RmlUiIntentKind.HunterZoom => argument is >= 0 and < 2,
            RmlUiIntentKind.OfflineSelectArena => argument is >= 0 and < 16,
            RmlUiIntentKind.OfflineArenaPage => argument is -1 or 1,
            RmlUiIntentKind.TheatreAction => argument is >= 0 and < 25,
            RmlUiIntentKind.TheatreEntry => argument is >= 0 and < 8,
            RmlUiIntentKind.TheatreThumbnail => argument is >= 0 and < 3,
            RmlUiIntentKind.ReplayAction => argument is >= 0 and < 13,
            RmlUiIntentKind.LicenseAction => argument is >= 0 and < 21,
            RmlUiIntentKind.StudioAction => argument is >= 0 and < 5,
            RmlUiIntentKind.InGameAction => argument is >= 0 and < 27,
            _ => argument == 0 && LegacyActions.ContainsKey(kind)
        };

        public static bool TryDecode(in RmlUiNativeIntent packet, out RmlUiIntent intent)
        {
            var kind = (RmlUiIntentKind)packet.Kind;
            if (packet.Size != NativeIntentSize || packet.Version != ProtocolVersion
                || packet.Generation == 0 || packet.DocumentId == 0 || packet.Sequence == 0
                || !IsValid(kind, packet.Argument))
            {
                intent = default;
                return false;
            }
            intent = new(kind, packet.Argument,
                new(packet.Generation, packet.DocumentId), packet.Sequence);
            return true;
        }

        public static bool TryParseLegacy(string action, RmlUiDocumentToken document,
            ulong sequence, out RmlUiIntent intent)
        {
            foreach (var pair in LegacyActions)
            {
                if (String.Equals(pair.Value, action, StringComparison.Ordinal))
                {
                    intent = new(pair.Key, 0, document, sequence);
                    return true;
                }
            }
            if (TryNamed(action, "route:", Routes, out int argument))
                intent = new(RmlUiIntentKind.Navigate, argument, document, sequence);
            else if (TryNamed(action, "stage:", Stages, out argument))
                intent = new(RmlUiIntentKind.StageSelect, argument, document, sequence);
            else if (TryNamed(action, "stage-preview:", Stages, out argument))
                intent = new(RmlUiIntentKind.StagePreview, argument, document, sequence);
            else if (TryNamed(action, "play:", PlayQueueActions, out argument))
                intent = new(RmlUiIntentKind.PlayQueueAction, argument, document, sequence);
            else if (TryIndex(action, "play:server:", 8, out argument))
                intent = new(RmlUiIntentKind.PlayServer, argument, document, sequence);
            else if (TryIndex(action, "lobby:rules-toggle:", 16, out argument))
                intent = new(RmlUiIntentKind.LobbyRulesToggle, argument, document, sequence);
            else if (TryIndex(action, "lobby:player:", 8, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.LobbyPlayerSelect, argument, document, sequence);
            else if (TryIndex(action, "hunter:select:", 7, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.HunterSelect, argument, document, sequence);
            else if (TryIndex(action, "hunter:suit:", 4, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.HunterSuit, argument, document, sequence);
            else if (TryIndex(action, "hunter:preview-mode:", 4, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.HunterPreviewMode, argument, document, sequence);
            else if (TryIndex(action, "settings:action:", 256, out argument) && argument != 15)
                intent = new(RmlUiIntentKind.SettingsAction, argument, document, sequence);
            else if (TryIndex(action, "settings:category:", 13, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.SettingsCategory, argument, document, sequence);
            else if (TryIndex(action, "adventure:slot:", 4, out argument) && argument >= 1)
                intent = new(RmlUiIntentKind.AdventureSelectSlot, argument, document, sequence);
            else if (TryIndex(action, "offline:choice:", 16, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.OfflineChoice, argument, document, sequence);
            else if (TryIndex(action, "offline:rule:", 12, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.OfflineRuleToggle, argument, document, sequence);
            else if (TryIndex(action, "offline:training-toggle:", 5, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.OfflineTrainingToggle, argument, document, sequence);
            else if (TryIndex(action, "hunter:rotate:", 2, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.HunterRotate, argument, document, sequence);
            else if (TryIndex(action, "lobby:team:", 4, out argument))
                intent = new(RmlUiIntentKind.LobbyTeamSelect, argument, document, sequence);
            else if (TryIndex(action, "hunter:zoom:", 2, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.HunterZoom, argument, document, sequence);
            else if (TryIndex(action, "offline:arena:", 16, out argument))
                intent = new(RmlUiIntentKind.OfflineSelectArena, argument, document, sequence);
            else if (action == "offline:arena-page:previous" || action == "offline:arena-page:next")
                intent = new(RmlUiIntentKind.OfflineArenaPage, action.EndsWith("previous", StringComparison.Ordinal) ? -1 : 1, document, sequence);
            else if (TryNamed(action, "theatre:", TheatreActions, out argument))
                intent = new(RmlUiIntentKind.TheatreAction, argument, document, sequence);
            else if (TryIndex(action, "theatre:entry:", 8, out argument))
                intent = new(RmlUiIntentKind.TheatreEntry, argument, document, sequence);
            else if (TryIndex(action, "theatre:thumbnail:", 3, out argument))
                intent = new(RmlUiIntentKind.TheatreThumbnail, argument, document, sequence);
            else if (TryNamed(action, "replay:", ReplayActions, out argument))
                intent = new(RmlUiIntentKind.ReplayAction, argument, document, sequence);
            else if (TryNamed(action, "license:", LicenseActions, out argument))
                intent = new(RmlUiIntentKind.LicenseAction, argument, document, sequence);
            else if (TryNamed(action, "studio:", StudioActions, out argument))
                intent = new(RmlUiIntentKind.StudioAction, argument, document, sequence);
            else if (TryIndex(action, "community:tab:", 3, out argument))
                intent = new(RmlUiIntentKind.CommunityTab, argument, document, sequence);
            else if (TryIndex(action, "community:sort:", 3, out argument))
                intent = new(RmlUiIntentKind.CommunitySort, argument, document, sequence);
            else if (TryIndex(action, "community:lifecycle:", 4, out argument))
                intent = new(RmlUiIntentKind.CommunityLifecycle, argument, document, sequence);
            else if (action == "community:page:previous" || action == "community:page:next")
                intent = new(RmlUiIntentKind.CommunityPage, action.EndsWith("previous", StringComparison.Ordinal) ? -1 : 1, document, sequence);
            else if (TryIndex(action, "community:select:", 8, out argument))
                intent = new(RmlUiIntentKind.CommunitySelect, argument, document, sequence);
            else if (TryIndex(action, "community:detail:", 10, out argument))
                intent = new(RmlUiIntentKind.CommunityDetailAction, argument, document, sequence);
            else if (action == "community:revision-page:previous" || action == "community:revision-page:next")
                intent = new(RmlUiIntentKind.CommunityRevisionPage, action.EndsWith("previous", StringComparison.Ordinal) ? -1 : 1, document, sequence);
            else if (TryIndex(action, "community:revision:", 8, out argument))
                intent = new(RmlUiIntentKind.CommunitySelectRevision, argument, document, sequence);
            else if (TryIndex(action, "community:creator:", 5, out argument))
                intent = new(RmlUiIntentKind.CommunityCreatorAction, argument, document, sequence);
            else if (TryIndex(action, "community:upload:", 2, out argument))
                intent = new(RmlUiIntentKind.CommunityUpload, argument, document, sequence);
            else if (TryIndex(action, "community:visibility:", 3, out argument))
                intent = new(RmlUiIntentKind.CommunityVisibility, argument, document, sequence);
            else if (TryIndex(action, "community:report-reason:", 6, out argument))
                intent = new(RmlUiIntentKind.CommunityReportReason, argument, document, sequence);
            else if (TryIndex(action, "community:conflict:", 5, out argument))
                intent = new(RmlUiIntentKind.CommunityConflict, argument, document, sequence);
            else if (TryNamed(action, "results:", ResultsActions, out argument))
                intent = new(RmlUiIntentKind.ResultsAction, argument, document, sequence);
            else if (TryNamed(action, "aim-results:", AimResultsActions, out argument))
                intent = new(RmlUiIntentKind.AimResultsAction, argument, document, sequence);
            else if (TryIndex(action, "setup:action:", 15, out argument))
                intent = new(RmlUiIntentKind.SetupAction, argument, document, sequence);
            else if (TryIndex(action, "setup:release:", 30, out argument))
                intent = new(RmlUiIntentKind.SetupRelease, argument, document, sequence);
            else if (TryNamed(action, "hud:", HudActions, out argument))
                intent = new(RmlUiIntentKind.HudAction, argument, document, sequence);
            else if (TryIndex(action, "results:map:", 4096, out argument))
                intent = new(RmlUiIntentKind.ResultsMap, argument, document, sequence);
            else if (TryIndex(action, "hud:element:", 14, out argument))
                intent = new(RmlUiIntentKind.HudElement, argument, document, sequence);
            else if (TryIndex(action, "hud:property:", 8, out argument))
                intent = new(RmlUiIntentKind.HudProperty, argument, document, sequence);
            else if (TryNamed(action, "social:", SocialActions, out argument))
                intent = new(RmlUiIntentKind.SocialAction, argument, document, sequence);
            else if (TryIndex(action, "news:action:", 12, out argument))
                intent = new(RmlUiIntentKind.NewsAction, argument, document, sequence);
            else if (TryNamed(action, "ingame:", InGameActions, out argument))
                intent = new(RmlUiIntentKind.InGameAction, argument, document, sequence);
            else
            {
                intent = default;
                return false;
            }
            return true;
        }

        public static string ToLegacy(in RmlUiIntent intent)
        {
            if (!IsValid(intent.Kind, intent.Argument))
                throw new ArgumentException("Invalid RmlUi intent.", nameof(intent));
            return intent.Kind switch
            {
                RmlUiIntentKind.Navigate => "route:" + Routes[intent.Argument],
                RmlUiIntentKind.StageSelect => "stage:" + Stages[intent.Argument],
                RmlUiIntentKind.StagePreview => "stage-preview:" + Stages[intent.Argument],
                RmlUiIntentKind.PlayQueueAction => "play:" + PlayQueueActions[intent.Argument],
                RmlUiIntentKind.PlayServer => "play:server:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.LobbyRulesToggle => "lobby:rules-toggle:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.LobbyPlayerSelect => "lobby:player:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.HunterSelect => "hunter:select:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.HunterSuit => "hunter:suit:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.HunterPreviewMode => "hunter:preview-mode:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.SettingsAction => "settings:action:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.SettingsCategory => "settings:category:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.AdventureSelectSlot => "adventure:slot:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.OfflineChoice => "offline:choice:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.OfflineRuleToggle => "offline:rule:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.OfflineTrainingToggle => "offline:training-toggle:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.HunterRotate => "hunter:rotate:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.LobbyTeamSelect => "lobby:team:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.HunterZoom => "hunter:zoom:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.OfflineSelectArena => "offline:arena:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.OfflineArenaPage => "offline:arena-page:" + (intent.Argument == -1 ? "previous" : "next"),
                RmlUiIntentKind.TheatreAction => "theatre:" + TheatreActions[intent.Argument],
                RmlUiIntentKind.TheatreEntry => "theatre:entry:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.TheatreThumbnail => "theatre:thumbnail:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.ReplayAction => "replay:" + ReplayActions[intent.Argument],
                RmlUiIntentKind.LicenseAction => "license:" + LicenseActions[intent.Argument],
                RmlUiIntentKind.StudioAction => "studio:" + StudioActions[intent.Argument],
                RmlUiIntentKind.CommunityTab => "community:tab:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.CommunitySort => "community:sort:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.CommunityLifecycle => "community:lifecycle:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.CommunityPage => "community:page:" + (intent.Argument == -1 ? "previous" : "next"),
                RmlUiIntentKind.CommunitySelect => "community:select:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.CommunityDetailAction => "community:detail:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.CommunityRevisionPage => "community:revision-page:" + (intent.Argument == -1 ? "previous" : "next"),
                RmlUiIntentKind.CommunitySelectRevision => "community:revision:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.CommunityCreatorAction => "community:creator:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.CommunityUpload => "community:upload:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.CommunityVisibility => "community:visibility:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.CommunityReportReason => "community:report-reason:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.CommunityConflict => "community:conflict:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.ResultsAction => "results:" + ResultsActions[intent.Argument],
                RmlUiIntentKind.AimResultsAction => "aim-results:" + AimResultsActions[intent.Argument],
                RmlUiIntentKind.SetupAction => "setup:action:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.SetupRelease => "setup:release:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.HudAction => "hud:" + HudActions[intent.Argument],
                RmlUiIntentKind.ResultsMap => "results:map:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.HudElement => "hud:element:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.HudProperty => "hud:property:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.SocialAction => "social:" + SocialActions[intent.Argument],
                RmlUiIntentKind.NewsAction => "news:action:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.InGameAction => "ingame:" + InGameActions[intent.Argument],
                _ => LegacyActions[intent.Kind]
            };
        }

        private static bool TryNamed(string action, string prefix, string[] names, out int argument)
        {
            argument = action.StartsWith(prefix, StringComparison.Ordinal)
                ? Array.IndexOf(names, action[prefix.Length..]) : -1;
            return argument >= 0;
        }

        private static bool TryIndex(string action, string prefix, int limit, out int argument)
        {
            argument = -1;
            if (!action.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string number = action[prefix.Length..];
            return Int32.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out argument)
                && argument >= 0 && argument < limit
                && number == argument.ToString(CultureInfo.InvariantCulture);
        }
    }
}
