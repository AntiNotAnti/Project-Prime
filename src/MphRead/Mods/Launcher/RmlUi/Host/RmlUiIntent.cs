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
        LobbySlotTeamNext = 89,
        LobbyMapOpen = 90, LobbyMapSelect = 91, LobbyMapPrevious = 92, LobbyMapNext = 93, LobbyMapClose = 94, LobbyMapCategory = 95,
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
        AdventureCopy = 127,
        AdventureCopyDestination = 128,
        AdventureConfirmCopy = 129,
        AdventureArchive = 130,
        AdventureConfirmArchive = 131,
        AdventureCancelSaveChange = 132,
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
        LicenseOpen = 191,
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
        HudProperty = 272,
        HunterPreviewReset = 273,
        CampaignShipAction = 274,
        HunterLoadoutDefault = 275,
        CommunityTransferAction = 276,
        HunterRandom = 277,
        HunterBeamNext = 278, HunterBeamColorNext = 279,
        HunterBeamSelect = 280, HunterBeamColorSelect = 281
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
        public const int LobbyRuleChoiceCount = 19;
        public const int OfflineRuleChoiceCount = 15;

        public static int ReplayActionCount => ReplayActions.Length;
        public const uint ProtocolVersion = 1;
        public const uint NativeIntentSize = 40;
        private static readonly string[] Routes =
        {
            "home", "play", "offline", "hunter", "forge", "theatre", "settings",
            "news", "adventure", "social", "training"
        };
        private static readonly string[] PlayQueueActions = { "queue-join", "queue-accept", "queue-decline", "queue-leave" };
        private static readonly string[] InGameActions = { "resume", "fullscreen", "settings", "replay", "vote", "spectate", "rejoin", "recorder", "return-lobby", "leave", "quit", "confirm", "cancel", "map-next", "vote-submit", "vote-yes", "vote-no", "bots", "bot-select", "bot-hunter", "bot-suit", "bot-skill", "bot-team", "bot-handicap", "bot-remove", "bot-add", "bot-apply", "journal", "journal-map", "journal-scans", "journal-category", "journal-previous", "journal-next", "journal-back", "custom-equip", "custom-return-native" };
        private static readonly string[] TheatreActions = { "search", "next-filter", "next-sort", "previous-page", "next-page", "refresh", "watch", "favorite", "validate", "recover", "cancel-job", "export", "rename", "organize", "delete", "confirm-delete", "cancel-delete", "reveal", "studio", "import", "import-path", "favorite-filtered", "validate-filtered", "clear-search", "cancel-launch" };
        private static readonly string[] ReplayActions = {"toggle-pause", "jump-back", "jump-forward", "restart", "step", "next-rate", "next-camera", "previous-player", "next-player", "seek", "studio", "back", "fullscreen", "bookmarks", "bookmark-previous", "bookmark-next", "bookmark-add", "bookmark-jump", "bookmark-remove", "bookmark-confirm-remove", "bookmark-cancel", "bookmark-refresh", "ranges", "mark-in", "mark-out", "range-save-highlight", "range-create-virtual", "range-refresh", "range-cancel", "range-save-materialized", "range-cancel-materialized", "camera-editor", "camera-refresh", "camera-free", "camera-fov-less", "camera-fov-more", "camera-roll-less", "camera-roll-more", "camera-previous", "camera-next", "camera-preview", "camera-add", "camera-update", "camera-remove", "camera-confirm", "camera-cancel-confirm", "camera-close", "camera-track", "export-open", "export-refresh", "export-preset", "export-resolution", "export-fps", "export-game-hud", "export-replay-overlay", "export-encoder", "export-source", "export-camera", "export-queue", "export-previous", "export-next", "export-cancel-active", "export-retry", "export-clear-pending", "export-confirm", "export-cancel-confirm", "export-close", "export-open-output", "editor-open", "editor-refresh", "editor-tab", "editor-player", "editor-event-filter", "editor-previous", "editor-next", "editor-seek", "editor-select-range", "editor-add-selection-to-reel", "editor-add-highlight-to-reel", "editor-add-top-highlights", "editor-move-up", "editor-move-down", "editor-trim", "editor-cycle-camera", "editor-remove", "editor-clear-reel", "editor-remove-named-highlight", "editor-confirm", "editor-cancel", "editor-close", "editor-previous-shot", "editor-shot-at-playhead", "editor-next-shot", "editor-toggle-analytics-hud", "editor-toggle-network-hud", "editor-toggle-combat-hud", "editor-toggle-shot-rays", "camera-tools-open", "camera-tools-refresh", "camera-tools-previous", "camera-tools-next", "camera-tools-toggle-selection", "camera-tools-range-selection", "camera-tools-select-all", "camera-tools-clear-selection", "camera-tools-move", "camera-tools-copy", "camera-tools-paste", "camera-tools-duplicate", "camera-tools-snap-event", "camera-tools-snap-kill", "camera-tools-snap-bookmark", "camera-tools-interpolation", "camera-tools-ease", "camera-tools-look-at", "camera-tools-remove-selected", "camera-tools-constant-speed", "camera-tools-collision", "camera-tools-director", "camera-tools-preview", "camera-tools-confirm", "camera-tools-cancel", "camera-tools-close", "camera-tools-nearest", "timeline-open", "timeline-refresh", "timeline-zoom-in", "timeline-zoom-out", "timeline-pan-left", "timeline-pan-right", "timeline-center", "timeline-reset", "timeline-cancel-drag", "timeline-close", "timeline-activate", "spatial-open", "spatial-analyze", "spatial-kind", "spatial-player", "spatial-range", "spatial-cancel", "spatial-close", "studio-tools-open", "studio-tools-run", "studio-tools-operation", "studio-tools-game-events", "studio-tools-combat-feedback", "studio-tools-cancel", "studio-tools-close", "camera-tools-transform", "camera-tools-handles", "four-views", "studio-tools-preview", "studio-tools-stop-preview", "camera-tools-curve-key", "spatial-world", "studio-cue-event", "studio-cue-bus", "studio-cue-previous", "studio-cue-next", "studio-cue-add", "studio-cue-update", "studio-cue-remove", "workspace-open", "workspace-save-as", "workspace-settings", "workspace-seek", "workspace-rate-0p25", "workspace-rate-0p5", "workspace-rate-1", "workspace-rate-2", "workspace-rate-4", "workspace-refresh", "workspace-tab-camera", "workspace-tab-markers", "workspace-tab-combat", "workspace-tab-analysis", "workspace-tab-audio", "workspace-tab-export", "workspace-camera-graph", "workspace-camera-apply", "workspace-preview-hud", "workspace-preview-overlay", "workspace-preview-path", "workspace-camera-mode-FirstPerson", "workspace-camera-mode-Free", "workspace-camera-mode-Chase", "workspace-camera-mode-Orbit", "workspace-track-camera", "workspace-track-events", "workspace-track-bookmarks", "workspace-track-ranges", "workspace-track-reel", "workspace-track-kills", "workspace-track-damage", "workspace-track-shots", "workspace-track-spawns", "workspace-track-objectives", "workspace-track-audio", "workspace-track-cuts", "workspace-track-exportrange", "workspace-editor-tab-Events", "workspace-editor-tab-AutomaticHighlights", "workspace-editor-tab-NamedHighlights", "workspace-editor-tab-Reel", "workspace-editor-tab-Analytics", "workspace-combat-choice-0", "workspace-combat-choice-1", "workspace-combat-choice-2", "workspace-combat-choice-3", "workspace-combat-choice-4", "workspace-combat-choice-5", "workspace-combat-choice-6", "workspace-combat-choice-7", "workspace-combat-choice-8", "workspace-combat-choice-9", "workspace-combat-choice-10", "workspace-combat-choice-11", "workspace-combat-choice-12", "workspace-combat-choice-13", "workspace-combat-choice-14", "workspace-combat-choice-15", "workspace-combat-shooter-view", "workspace-combat-shooter-pov", "workspace-combat-authority", "workspace-operation-AudioMix", "workspace-operation-CompareRecording", "workspace-operation-CompareWorld", "workspace-operation-SaveBuildEvidence", "workspace-operation-CompareBuildEvidence", "workspace-operation-PortableBundle", "workspace-operation-ImportPortable", "workspace-operation-DiagnosticBundle", "workspace-operation-MuxVideo", "workspace-export-source-Selection", "workspace-export-source-FullReplay", "workspace-export-source-ExistingReel", "workspace-export-camera-FirstPerson", "workspace-export-camera-Chase", "workspace-export-camera-Orbit", "workspace-export-camera-Director", "workspace-export-camera-SavedCameraTrack", "workspace-export-fps-24", "workspace-export-fps-30", "workspace-export-fps-48", "workspace-export-fps-60", "workspace-export-fps-90", "workspace-export-fps-120", "workspace-export-fps-144", "workspace-export-resolution-0", "workspace-export-resolution-1", "workspace-export-resolution-2", "workspace-export-resolution-3", "workspace-camera-overview", "workspace-comparison-preview", "workspace-comparison-stop", "studio-audio-enabled", "studio-audio-save", "workspace-camera-interpolation-0", "workspace-camera-interpolation-1", "workspace-camera-interpolation-2", "workspace-camera-interpolation-3", "workspace-camera-interpolation-4", "workspace-camera-ease-0", "workspace-camera-ease-1", "workspace-camera-ease-2", "workspace-camera-ease-3", "workspace-camera-lookat-none", "workspace-camera-lookat-0", "workspace-camera-lookat-1", "workspace-camera-lookat-2", "workspace-camera-lookat-3", "workspace-camera-lookat-4", "workspace-camera-lookat-5", "workspace-camera-lookat-6", "workspace-camera-lookat-7", "workspace-camera-slot-0", "workspace-camera-slot-1", "workspace-camera-slot-2", "workspace-camera-slot-3", "workspace-camera-slot-4", "workspace-camera-slot-5", "workspace-camera-slot-6", "workspace-camera-slot-7", "workspace-camera-key-interpolation-0", "workspace-camera-key-interpolation-1", "workspace-camera-key-interpolation-2", "workspace-camera-key-interpolation-3", "workspace-camera-key-interpolation-4", "workspace-camera-key-ease-0", "workspace-camera-key-ease-1", "workspace-camera-key-ease-2", "workspace-camera-key-ease-3", "workspace-camera-key-lookat-none", "workspace-camera-key-lookat-0", "workspace-camera-key-lookat-1", "workspace-camera-key-lookat-2", "workspace-camera-key-lookat-3", "workspace-camera-key-lookat-4", "workspace-camera-key-lookat-5", "workspace-camera-key-lookat-6", "workspace-camera-key-lookat-7", "workspace-event-search", "workspace-replay-tags-save", "workspace-save-view", "workspace-music-choose", "workspace-cue-choose"};
        private static readonly string[] LicenseActions = { "overview", "customization", "stats", "history", "achievements", "emblems", "titles", "comparison", "account", "refresh", "cancel-refresh", "previous-page", "next-page", "send-verification", "finish-password", "recover", "link-google", "link-github", "link-discord", "refresh-account", "back", "hide-profile", "show-profile", "confirm-public", "cancel-public", "card-record", "card-combat", "card-skill", "card-objectives", "card-time" };
        private static readonly string[] StudioActions = { "launch", "pick-map", "open-path", "recover", "cancel", "refresh", "previous-page", "next-page", "open-selected", "create-project", "project-library", "duplicate", "delete", "confirm-delete", "cancel-delete", "select-entry-0", "select-entry-1", "select-entry-2", "select-entry-3", "select-entry-4", "select-entry-5", "select-entry-6", "select-entry-7", "select-template-0", "select-template-1", "select-template-2", "select-template-3", "select-template-4", "select-template-5", "select-template-6", "select-template-7", "edit-current", "editor-back", "editor-undo", "editor-redo", "editor-save", "editor-move", "editor-rotate", "editor-scale", "editor-frame-all", "editor-frame-selection", "editor-perspective", "editor-top", "editor-front", "editor-side", "editor-previous", "editor-next", "editor-select-0", "editor-select-1", "editor-select-2", "editor-select-3", "editor-select-4", "editor-select-5", "editor-select-6", "editor-select-7", "project-change-save", "project-change-preserve", "project-change-cancel", "project-change-overwrite", "project-change-keep-file", "editor-create-box", "editor-create-wedge", "editor-create-prism", "editor-create-convex", "editor-create-mesh", "editor-create-spawn", "editor-create-pickup", "editor-create-jump-pad", "editor-create-link", "editor-duplicate", "editor-delete", "editor-confirm-delete", "editor-cancel-delete", "property-previous", "property-next", "property-apply", "property-reset", "property-cycle-0", "property-cycle-1", "property-cycle-2", "property-cycle-3", "property-cycle-4", "property-cycle-5", "property-cycle-6", "property-cycle-7", "resource-materials", "resource-assets", "resource-objects", "material-previous", "material-next", "material-add", "material-duplicate", "material-assign-selection", "material-select-usages", "material-delete-unused", "material-confirm-delete", "material-cancel-delete", "material-property-previous", "material-property-next", "material-property-apply", "material-property-reset", "material-property-cycle-0", "material-property-cycle-1", "material-property-cycle-2", "material-property-cycle-3", "material-property-cycle-4", "material-property-cycle-5", "material-property-cycle-6", "material-property-cycle-7", "material-select-0", "material-select-1", "material-select-2", "material-select-3", "material-select-4", "material-select-5", "material-select-6", "material-select-7", "asset-previous", "asset-next", "asset-rename", "asset-remove-unused", "asset-confirm-remove", "asset-cancel-remove", "asset-cycle-channel", "asset-select-0", "asset-select-1", "asset-select-2", "asset-select-3", "asset-select-4", "asset-select-5", "asset-select-6", "asset-select-7", "asset-bind-channel", "asset-clear-channel", "resource-refresh-cache", "asset-find-usages", "material-use-as-replacement", "material-replace-usages", "resource-reset-selection", "object-mode", "face-mode", "edge-mode", "vertex-mode", "tool-move", "tool-rotate", "tool-scale", "axis-free", "axis-x", "axis-y", "axis-z", "axis-xy", "axis-xz", "axis-yz", "toggle-local", "snap-apply", "pivot-cycle", "cursor-apply", "component-select-all", "component-clear", "component-linked", "component-grow", "component-shrink", "component-boundary", "component-previous", "component-next", "component-select-0", "component-select-1", "component-select-2", "component-select-3", "component-select-4", "component-select-5", "component-select-6", "component-select-7", "convert-to-mesh", "apply-component-transform", "uv-apply", "uv-fit64", "uv-density", "uv-copy", "uv-paste", "uv-rotate-positive", "uv-rotate-negative", "uv-flip-u", "uv-flip-v", "uv-explicit", "uv-projected", "uv-checker-toggle", "selection-mode-cycle", "gesture-cancel", "reset-uv-draft", "asset-choose-source", "asset-review-import", "asset-apply-import", "asset-review-replace", "asset-apply-replace", "asset-review-export", "asset-confirm-export", "asset-confirm-overwrite", "asset-cancel-io", "asset-reload-source", "build-validate", "build-runtime", "build-package", "build-play-camera", "build-play-selection", "build-navigation", "build-audit", "build-cancel", "build-problems-previous", "build-problems-next", "build-budgets-previous", "build-budgets-next", "build-problem-0", "build-problem-1", "build-problem-2", "build-problem-3", "build-problem-4", "build-problem-5", "build-problem-6", "build-problem-7", "build-clear", "build-play-team-a", "build-play-team-b", "build-panel" };
        private static readonly string[] SocialActions = { "friends", "players", "requests", "invites", "party", "recent", "blocked", "refresh", "cancel", "lookup", "search", "previous-page", "next-page", "select-0", "select-1", "select-2", "select-3", "select-4", "select-5", "select-6", "select-7", "send-friend-request", "accept-friend-request", "decline-friend-request", "cancel-friend-request", "remove-friend", "block-player", "unblock-player", "send-game-invite", "join-friend", "accept-game-invite", "decline-game-invite", "cancel-game-invite", "invite-party", "accept-party-invite", "decline-party-invite", "cancel-party-invite", "leave-party", "disband-party", "kick-party-member", "promote-party-member", "invite-party-lobby", "follow-party-travel", "join-party-leader", "decline-party-travel", "cancel-reservation", "confirm", "dismiss", "presence-next", "activity-next", "invites-next", "dnd", "back", "reserve-party-seats" };
        private static readonly string[] ResultsActions = { "close", "search", "previous-page", "next-page", "rematch", "clear-search" };
        private static readonly string[] AimResultsActions = { "retry", "change-drill", "exit" };
        private static readonly string[] HudActions = { "open", "use", "cancel", "next-preset", "undo", "redo", "lock-all", "unlock-all", "align-left", "align-top", "native-elements", "reset-hud", "toggle-visible", "toggle-lock", "next-anchor", "next-visibility", "reset-element", "reset-section", "next-aspect", "next-hunter", "next-scenario", "next-grid", "toggle-guides", "save-named", "load-named", "export-json", "import-json", "property-previous", "property-next", "property-apply", "property-reset", "next-palette", "next-crosshair-target", "toggle-crosshair-override", "next-crosshair-preset", "share-crosshair", "import-crosshair", "next-radar-preset", "nudge-left", "nudge-right", "nudge-up", "nudge-down", "scale-down", "scale-up", "apply-layout" };
        private static readonly string[] Stages = { "quick", "browser", "offline", "adventure", "training" };
        private static readonly IReadOnlyDictionary<RmlUiIntentKind, string> LegacyActions =
            new Dictionary<RmlUiIntentKind, string>
            {
                [RmlUiIntentKind.Quit] = "quit",
                [RmlUiIntentKind.LicenseOpen] = "license:open",
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
                [RmlUiIntentKind.LobbyMapOpen] = "lobby:map-open",
                [RmlUiIntentKind.LobbyMapPrevious] = "lobby:map-previous",
                [RmlUiIntentKind.LobbyMapNext] = "lobby:map-next",
                [RmlUiIntentKind.LobbyMapClose] = "lobby:map-close",
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
                [RmlUiIntentKind.HunterRandom] = "hunter:random",
                [RmlUiIntentKind.HunterSpectator] = "hunter:spectator",
                [RmlUiIntentKind.HunterApply] = "hunter:apply",
                [RmlUiIntentKind.HunterCancel] = "hunter:cancel",
                [RmlUiIntentKind.HunterSkinNext] = "hunter:skin-next",
                [RmlUiIntentKind.HunterArmorNext] = "hunter:armor-next",
                [RmlUiIntentKind.HunterDeathNext] = "hunter:death-next",
                [RmlUiIntentKind.HunterBeamNext] = "hunter:beam-next",
                [RmlUiIntentKind.HunterBeamColorNext] = "hunter:beam-color-next",
                [RmlUiIntentKind.HunterCosmeticsReset] = "hunter:cosmetics-reset",
                [RmlUiIntentKind.HunterPreviewDeath] = "hunter:preview-death",
                [RmlUiIntentKind.HunterCosmeticsSave] = "hunter:cosmetics-save",
                [RmlUiIntentKind.HunterLoopDeath] = "hunter:loop-death",
                [RmlUiIntentKind.HunterCompareNative] = "hunter:compare-native",
                [RmlUiIntentKind.HunterResetPreview] = "hunter:reset-preview",
                [RmlUiIntentKind.HunterPreviewReset] = "hunter:preview-reset",
                [RmlUiIntentKind.HunterLoadoutDefault] = "hunter:loadout-default",
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
                [RmlUiIntentKind.AdventureCopy] = "adventure:copy",
                [RmlUiIntentKind.AdventureConfirmCopy] = "adventure:confirm-copy",
                [RmlUiIntentKind.AdventureArchive] = "adventure:archive",
                [RmlUiIntentKind.AdventureConfirmArchive] = "adventure:confirm-archive",
                [RmlUiIntentKind.AdventureCancelSaveChange] = "adventure:cancel-save-change",
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
            RmlUiIntentKind.CommunityTab => argument is >= 0 and < 5,
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
            RmlUiIntentKind.CommunityTransferAction => argument is >= 0 and < 4,
            RmlUiIntentKind.ResultsAction => argument is >= 0 and < 6,
            RmlUiIntentKind.AimResultsAction => argument is >= 0 and < 3,
            RmlUiIntentKind.SetupAction => argument is >= 0 and < 15,
            RmlUiIntentKind.SetupRelease => argument is >= 0 and < 30,
            RmlUiIntentKind.HudAction => argument is >= 0 and < 45,
            RmlUiIntentKind.ResultsMap => argument is >= 0 and < 4096,
            RmlUiIntentKind.HudElement => argument is >= 0 and < 14,
            RmlUiIntentKind.HudProperty => argument is >= 0 and < 8,
            RmlUiIntentKind.CampaignShipAction => argument is >= 0 and < 11,
            RmlUiIntentKind.SocialAction => argument is >= 0 and < 54,
            RmlUiIntentKind.NewsAction => argument is >= 0 and < 12,
            RmlUiIntentKind.PlayQueueAction => argument is >= 0 and < 4,
            RmlUiIntentKind.Navigate => argument >= 0 && argument < Routes.Length,
            RmlUiIntentKind.StageSelect or RmlUiIntentKind.StagePreview => argument is >= 0 and < 5,
            RmlUiIntentKind.LobbyMapSelect => argument is >= 0 and < 6,
            RmlUiIntentKind.LobbyMapCategory => argument is >= 0 and < 5,
            RmlUiIntentKind.PlayServer => argument is >= 0 and < 8,
            RmlUiIntentKind.LobbyRulesToggle => argument is >= 0 and < LobbyRuleChoiceCount,
            RmlUiIntentKind.LobbyPlayerSelect or RmlUiIntentKind.LobbySlotTeamNext => argument is >= 0 and < 8,
            RmlUiIntentKind.HunterSelect => argument is >= 0 and < 7,
            RmlUiIntentKind.HunterBeamSelect => argument is >= 0 and < 9,
            RmlUiIntentKind.HunterBeamColorSelect => argument is >= 0 and < 9,
            RmlUiIntentKind.HunterSuit => argument is >= 0 and < 4,
            RmlUiIntentKind.HunterPreviewMode => argument is >= 0 and < 4,
            RmlUiIntentKind.SettingsAction => argument is >= 0 and <= 14 or >= 16 and <= 255,
            RmlUiIntentKind.SettingsCategory => argument is >= 0 and < 13,
            RmlUiIntentKind.AdventureSelectSlot or RmlUiIntentKind.AdventureCopyDestination => argument is >= 1 and < 4,
            RmlUiIntentKind.OfflineChoice => argument is >= 0 and < 16,
            RmlUiIntentKind.OfflineRuleToggle => argument is >= 0 and < OfflineRuleChoiceCount,
            RmlUiIntentKind.OfflineTrainingToggle => argument is >= 0 and < 5,
            RmlUiIntentKind.HunterRotate => argument is >= 0 and < 2,
            RmlUiIntentKind.LobbyTeamSelect => argument is >= 0 and < 4,
            RmlUiIntentKind.HunterZoom => argument is >= 0 and < 2,
            RmlUiIntentKind.OfflineSelectArena => argument is >= 0 and < 16,
            RmlUiIntentKind.OfflineArenaPage => argument is -1 or 1,
            RmlUiIntentKind.TheatreAction => argument is >= 0 and < 25,
            RmlUiIntentKind.TheatreEntry => argument is >= 0 and < 8,
            RmlUiIntentKind.TheatreThumbnail => argument is >= 0 and < 3,
            RmlUiIntentKind.ReplayAction => argument >= 0 && argument < ReplayActions.Length,
            RmlUiIntentKind.LicenseAction => argument is >= 0 and < 30,
            RmlUiIntentKind.StudioAction => argument is >= 0 and < 224,
            RmlUiIntentKind.InGameAction => argument is >= 0 and < 36,
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
            else if (TryIndex(action, "lobby:rules-toggle:", LobbyRuleChoiceCount, out argument))
                intent = new(RmlUiIntentKind.LobbyRulesToggle, argument, document, sequence);
            else if (TryIndex(action, "lobby:map-category:", 5, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.LobbyMapCategory, argument, document, sequence);
            else if (TryIndex(action, "lobby:map-select:", 6, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.LobbyMapSelect, argument, document, sequence);
            else if (TryIndex(action, "lobby:slot-team:", 8, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.LobbySlotTeamNext, argument, document, sequence);
            else if (TryIndex(action, "lobby:player:", 8, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.LobbyPlayerSelect, argument, document, sequence);
            else if (TryIndex(action, "hunter:select:", 7, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.HunterSelect, argument, document, sequence);
            else if (TryIndex(action, "hunter:beam-select:", 9, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.HunterBeamSelect, argument, document, sequence);
            else if (TryIndex(action, "hunter:beam-color-select:", 9, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.HunterBeamColorSelect, argument, document, sequence);
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
            else if (TryIndex(action, "adventure:copy-destination:", 4, out argument) && argument >= 1)
                intent = new(RmlUiIntentKind.AdventureCopyDestination, argument, document, sequence);
            else if (TryIndex(action, "offline:choice:", 16, out argument) && argument >= 0)
                intent = new(RmlUiIntentKind.OfflineChoice, argument, document, sequence);
            else if (TryIndex(action, "offline:rule:", OfflineRuleChoiceCount, out argument) && argument >= 0)
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
            else if (TryIndex(action, "community:tab:", 5, out argument))
                intent = new(RmlUiIntentKind.CommunityTab, argument, document, sequence);
            else if (TryIndex(action, "community:transfer:", 4, out argument))
                intent = new(RmlUiIntentKind.CommunityTransferAction, argument, document, sequence);
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
            else if (TryIndex(action, "campaign-ship:", 11, out argument))
                intent = new(RmlUiIntentKind.CampaignShipAction, argument, document, sequence);
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
                RmlUiIntentKind.LobbyMapCategory => "lobby:map-category:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.LobbyMapSelect => "lobby:map-select:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.LobbySlotTeamNext => "lobby:slot-team:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.LobbyPlayerSelect => "lobby:player:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.HunterSelect => "hunter:select:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.HunterBeamSelect => "hunter:beam-select:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.HunterBeamColorSelect => "hunter:beam-color-select:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.HunterSuit => "hunter:suit:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.HunterPreviewMode => "hunter:preview-mode:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.SettingsAction => "settings:action:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.SettingsCategory => "settings:category:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.AdventureSelectSlot => "adventure:slot:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
                RmlUiIntentKind.AdventureCopyDestination => "adventure:copy-destination:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
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
                RmlUiIntentKind.CommunityTransferAction => "community:transfer:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
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
                RmlUiIntentKind.CampaignShipAction => "campaign-ship:" + intent.Argument.ToString(CultureInfo.InvariantCulture),
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
