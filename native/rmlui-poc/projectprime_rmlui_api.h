#pragma once

#include <cstdint>

#if defined(_WIN32)
#define PP_EXPORT extern "C" __declspec(dllexport)
#else
#define PP_EXPORT extern "C" __attribute__((visibility("default")))
#endif

// Version 1 is an owner-thread ABI. Strings are UTF-8; IDs are opaque and never
// reused within this process. The consumer must reject a different version.
constexpr uint32_t PP_RMLUI_PROTOCOL_VERSION = 1;
// Action arguments are append-only; the packet layout and ABI version remain unchanged.
constexpr uint32_t PP_RMLUI_REPLAY_ACTION_COUNT = 307;
constexpr uint32_t PP_RMLUI_STUDIO_ACTION_COUNT = 224;
enum class PrimeIntentKind : uint32_t {
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
    SettingsPreviousChoice = 108,
    OfflineChoicePrevious = 109,
    LobbyRulesModePrevious = 110,
    LobbyRulesFormatPrevious = 111,
    PlayPreviousMap = 112,
    PlayPreviousMode = 113,
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
    HunterBeamNext = 278,
    HunterBeamColorNext = 279,
    HunterBeamSelect = 280,
    HunterBeamColorSelect = 281,
    HunterSkinSelect = 282, HunterArmorSelect = 283, HunterDeathSelect = 284,
    HunterCosmeticCategory = 285,
    HunterBodySelect = 286,
    HunterDroneSelect = 287
};
// Navigate arguments: Home=0 Play=1 Offline=2 HunterLicense=3 Community=4
// Theatre=5 Settings=6 News=7 Adventure=8 Social=9.
// Stage arguments: Quick=0 Browser=1 Offline=2 Adventure=3.
struct PrimeIntent {
    uint32_t size = sizeof(PrimeIntent);
    uint32_t version = PP_RMLUI_PROTOCOL_VERSION;
    uint32_t kind = 0;
    int32_t argument = 0;
    uint64_t generation = 0;
    uint64_t document_id = 0;
    uint64_t sequence = 0;
};
static_assert(sizeof(PrimeIntent) == 40, "Native intent ABI layout changed");

// Optional v1 scheduling/capture capability. Positive infinity means no timer
// is pending. A draw list may be reused only while flags includes bit 1.
struct PrimeUpdateStatus {
    uint32_t size = sizeof(PrimeUpdateStatus);
    uint32_t version = 1;
    uint64_t generation = 0;
    uint64_t visual_revision = 0;
    double next_update_delay_seconds = 0;
    uint32_t flags = 0; // Dirty=1, DrawListValid=2.
    uint32_t reserved = 0;
};
static_assert(sizeof(PrimeUpdateStatus) == 40, "Native scheduling ABI layout changed");

enum class PrimeDrawCommandKind : uint32_t {
    Geometry = 1, ScissorEnable = 2, ScissorRegion = 3, Transform = 4,
    ClipEnable = 5, ClipGeometry = 6
};
struct PrimeDrawCommand {
    uint32_t size = sizeof(PrimeDrawCommand);
    uint32_t kind = 0;
    uint64_t geometry = 0;
    uint64_t texture = 0;
    float translation_x = 0, translation_y = 0;
    int32_t x = 0, y = 0, width = 0, height = 0;
    uint32_t enabled = 0, operation = 0;
    // Column-major; identity when enabled=0.
    float transform[16] = {};
};
static_assert(sizeof(PrimeDrawCommand) == 120, "Native draw ABI layout changed");
struct PrimeDrawGeometry {
    uint32_t size = sizeof(PrimeDrawGeometry);
    uint32_t vertex_count = 0, index_count = 0, reserved = 0;
    // Vertex layout: float position[2], uint8 rgba[4], float uv[2] (20 bytes).
    const void* vertices = nullptr;
    const int32_t* indices = nullptr;
};
struct PrimeDrawTexture {
    uint32_t size = sizeof(PrimeDrawTexture);
    uint32_t width = 0, height = 0, reserved = 0;
    uint64_t handle = 0;
    // width*height*4 RGBA bytes, premultiplied alpha. Copy before native mutation.
    const uint8_t* pixels = nullptr;
};

// Optional independent canvas ABI1. Input pixels/vertex colors are straight RGBA8;
// the bridge premultiplies once before entering RmlUi. No engine/GPU IDs cross in.
struct PrimeCanvasLease {
    uint32_t size = sizeof(PrimeCanvasLease), version = 1;
    uint64_t generation = 0, document_id = 0, epoch = 0;
};
struct PrimeCanvasTexture {
    uint32_t size = sizeof(PrimeCanvasTexture), version = 1;
    uint32_t width = 0, height = 0, offset = 0, length = 0;
    uint32_t filter = 0, wrap_s = 0, wrap_t = 0, reserved = 0; // nearest/linear, clamp/repeat
};
struct PrimeCanvasVertex { float x = 0, y = 0; uint8_t r = 0, g = 0, b = 0, a = 0; float u = 0, v = 0; };
struct PrimeCanvasBatch { uint32_t texture = UINT32_MAX, first = 0, count = 0, reserved = 0; };
struct PrimeCanvasFrame {
    uint32_t size = sizeof(PrimeCanvasFrame), version = 1;
    uint64_t revision = 0;
    float virtual_width = 0, virtual_height = 0;
    uint32_t vertex_count = 0, index_count = 0, batch_count = 0, reserved = 0;
};
struct PrimeCanvasState {
    uint32_t size = sizeof(PrimeCanvasState), version = 1;
    uint64_t revision = 0;
    uint32_t texture_count = 0, batch_count = 0, flags = 0, reserved = 0; // Failed=1, TableSet=2
};
struct PrimeTextureSampling {
    uint32_t size = sizeof(PrimeTextureSampling), version = 1;
    uint32_t filter = 0, wrap_s = 0, wrap_t = 0, flags = 0; // ExplicitCanvas=1; legacy=0
};
static_assert(sizeof(PrimeCanvasLease) == 32 && sizeof(PrimeCanvasTexture) == 40 && sizeof(PrimeCanvasVertex) == 20
    && sizeof(PrimeCanvasBatch) == 16 && sizeof(PrimeCanvasFrame) == 40 && sizeof(PrimeCanvasState) == 32
    && sizeof(PrimeTextureSampling) == 24, "Canvas ABI layout changed");
PP_EXPORT uint32_t pp_rmlui_canvas_version();
PP_EXPORT int pp_rmlui_canvas_acquire(uint64_t generation, uint64_t document_id, const char* element_id, PrimeCanvasLease* lease);
PP_EXPORT int pp_rmlui_canvas_textures(const PrimeCanvasLease* lease, const PrimeCanvasTexture* textures,
    uint32_t count, const uint8_t* pixels, uint32_t length);
PP_EXPORT int pp_rmlui_canvas_frame(const PrimeCanvasLease* lease, const PrimeCanvasFrame* frame,
    const PrimeCanvasVertex* vertices, const int32_t* indices, const PrimeCanvasBatch* batches);
PP_EXPORT int pp_rmlui_canvas_state(const PrimeCanvasLease* lease, PrimeCanvasState* state);
PP_EXPORT int pp_rmlui_canvas_release(const PrimeCanvasLease* lease);
PP_EXPORT int pp_rmlui_draw_texture_sampling(uint64_t generation, uint64_t handle, PrimeTextureSampling* sampling);

PP_EXPORT uint32_t pp_rmlui_protocol_version();
PP_EXPORT uint64_t pp_rmlui_generation();
// Optional font policy: licensed HUD font indices 0..33, -1 preserves the authored role.
PP_EXPORT int pp_rmlui_typography(int body_font, int heading_font, int data_font);
PP_EXPORT uint64_t pp_rmlui_home_document();
PP_EXPORT int pp_rmlui_take_intent(PrimeIntent* intent);
PP_EXPORT int pp_rmlui_initialize_backend(int width, int height, float density, const char* asset_root, int backend);
PP_EXPORT int pp_rmlui_update_status(PrimeUpdateStatus* status);
PP_EXPORT void pp_rmlui_shutdown();
PP_EXPORT uint64_t pp_rmlui_document_open(const char* relative_path, int layer);
PP_EXPORT int pp_rmlui_document_element_bounds(uint64_t document_id, const char* id, float* x, float* y, float* width, float* height);
PP_EXPORT int pp_rmlui_document_close(uint64_t document_id);
PP_EXPORT int pp_rmlui_document_show(uint64_t document_id, int show);
PP_EXPORT int pp_rmlui_document_focus(uint64_t document_id, const char* element_id);
PP_EXPORT int pp_rmlui_document_set_text(uint64_t document_id, const char* name, const char* value);
PP_EXPORT int pp_rmlui_document_set_bool(uint64_t document_id, const char* name, int value);
PP_EXPORT int pp_rmlui_document_set_field(uint64_t document_id, const char* element_id, const char* value);
PP_EXPORT int pp_rmlui_document_read_field(uint64_t document_id, const char* element_id, unsigned char* buffer, int capacity);
PP_EXPORT int pp_rmlui_text_selection_utf16(uint64_t generation, uint64_t document_id, uint64_t focus_epoch, int* start, int* end);
PP_EXPORT int pp_rmlui_document_accessibility_snapshot(uint64_t document_id, unsigned char* buffer, int capacity);
PP_EXPORT int pp_rmlui_accessibility_action(uint64_t generation, uint64_t document_id, uint64_t revision, const char* node_key, int action);
PP_EXPORT int pp_rmlui_accessibility_set_text(uint64_t generation, uint64_t document_id, uint64_t revision, const char* node_key, const char* value);
PP_EXPORT int pp_rmlui_document_reload(uint64_t document_id); // Debug builds only.
PP_EXPORT int pp_rmlui_document_count();
PP_EXPORT int pp_rmlui_focused_element(unsigned char* buffer, int capacity);
PP_EXPORT int pp_rmlui_text_input_active();
PP_EXPORT void pp_rmlui_focus_lost();
PP_EXPORT int pp_rmlui_draw_command_count();
PP_EXPORT int pp_rmlui_draw_command(int index, PrimeDrawCommand* command);
PP_EXPORT int pp_rmlui_draw_geometry(uint64_t handle, PrimeDrawGeometry* geometry);
PP_EXPORT int pp_rmlui_draw_texture_count();
PP_EXPORT int pp_rmlui_draw_texture(int index, PrimeDrawTexture* texture);
// Bit 0 means a document used unsupported layers/filters/shaders. Never silently
// present such a frame as renderer parity: the consumer must reject/fallback.
PP_EXPORT uint32_t pp_rmlui_draw_features();

PP_EXPORT void pp_rmlui_set_clipboard(const char* text);
PP_EXPORT int pp_rmlui_read_clipboard(unsigned char* buffer, int capacity);

PP_EXPORT int pp_rmlui_draw_geometry_count();

PP_EXPORT int pp_rmlui_document_set_enabled(uint64_t document_id, const char* element_id, int enabled);

#include "projectprime_rmlui_text_protocol.h"
PP_EXPORT int pp_rmlui_text_input_state(PrimeTextInputState* state);
PP_EXPORT int pp_rmlui_composition(uint64_t generation, uint64_t document_id, uint64_t focus_epoch, int stage, const char* text, int cursor, int selection_length);
PP_EXPORT int pp_rmlui_hovered_action(unsigned char* buffer, int capacity);
PP_EXPORT int pp_rmlui_hovered_element(unsigned char* buffer, int capacity);

// Optional independently discovered edit-range ABI; protocol1/state64 unchanged.
#include "projectprime_rmlui_text_edit_protocol.h"
PP_EXPORT int pp_rmlui_text_edit_state_v1(PrimeTextEditState* state);
PP_EXPORT int pp_rmlui_text_edit_v1(const PrimeTextEditRequest* request, const uint8_t* utf8, uint32_t bytes);
