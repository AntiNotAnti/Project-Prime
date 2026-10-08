#include <RmlUi/Core.h>
#include <RmlUi/Core/Input.h>
#include <RmlUi/Core/StringUtilities.h>
#include <RmlUi/Core/Elements/ElementFormControlInput.h>
#include <RmlUi/Core/Elements/ElementFormControlTextArea.h>
#include "projectprime_rmlui_renderer.h"
#include "projectprime_rmlui_text_input.h"
#include "projectprime_rmlui_accessibility.h"
#include <RmlUi/Core/EventListener.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cstring>
#include <deque>
#include <filesystem>
#include <memory>
#include <string>
#include <unordered_map>
#include <vector>
#include <thread>
#include <cmath>
#include <atomic>
#include <cerrno>
#include <cstdlib>
#include <cctype>
#include <limits>

namespace {

void FocusElement(const char* id);
void QueueHomeAction(const std::string& action);
void DirtyVisual();
Rml::Element* FindElementById(const char* id);
bool g_lobby_anchor_dirty = true;
struct LobbyAnchor { float x = 0.f; float y = 0.f; };
std::array<LobbyAnchor, 8> g_lobby_anchors{};

class PrimeSystemInterface final : public Rml::SystemInterface {
public:
    PrimeSystemInterface() : started(std::chrono::steady_clock::now()) {}

    double GetElapsedTime() override
    {
        return std::chrono::duration<double>(std::chrono::steady_clock::now() - started).count();
    }

    void SetClipboardText(const Rml::String& text) override { clipboard = text; }
    void GetClipboardText(Rml::String& text) override { text = clipboard; }
    void ClearClipboard() { clipboard.clear(); }
    void JoinPath(Rml::String& translated, const Rml::String& document, const Rml::String& path) override
    {
        // The default browser-style resolver removes the leading '/' from
        // absolute paths. Local engine thumbnails must retain their real path.
        if (!path.empty() && path[0] == '/') translated = path;
        else Rml::SystemInterface::JoinPath(translated, document, path);
    }
private:
    Rml::String clipboard;
    std::chrono::steady_clock::time_point started;
};

#include "projectprime_rmlui_menu.h"

PrimeSystemInterface g_system;
PrimeTextInputHandler g_text_input;
PrimeAccessibilityTree g_accessibility;
std::unique_ptr<PrimeRenderer> g_renderer;
Rml::Context* g_context = nullptr;
Rml::ElementDocument* g_document = nullptr;
std::unique_ptr<PrimeMenuModel> g_model;
std::atomic<bool> g_initialized{false};
std::thread::id g_owner;
std::atomic<uint64_t> g_generation{0};
uint64_t g_next_document_id = 0;
uint64_t g_home_id = 0;
uint64_t g_next_sequence = 0;
std::filesystem::path g_asset_root;
struct QueuedAction { PrimeIntent intent; std::string legacy; };
std::deque<QueuedAction> g_actions;
int g_programmatic_field_mutations = 0;
struct ProgrammaticFieldMutation {
    ProgrammaticFieldMutation() { ++g_programmatic_field_mutations; }
    ~ProgrammaticFieldMutation() { --g_programmatic_field_mutations; }
};
struct Document {
    Rml::ElementDocument* element = nullptr;
    std::string relative_path;
    int layer = 0;
    uint64_t restore_document = 0;
    std::string restore_element;
    std::unordered_map<std::string, std::string> texts;
    std::unordered_map<std::string, bool> booleans;
};
std::unordered_map<uint64_t, Document> g_documents;
std::vector<uint64_t> g_document_order;
bool g_update_dirty = true;
bool g_draw_list_valid = false;
uint64_t g_visual_revision = 0;
uint64_t g_resource_revision = 0;
double g_last_update_time = 0;
int g_backend = -1;
bool g_pointer_known = false;
int g_pointer_x = 0, g_pointer_y = 0, g_pointer_modifiers = 0;
uint32_t g_pointer_buttons = 0;
int g_capture_width = 0, g_capture_height = 0;
bool g_updated_since_render = false;

void DirtyVisual()
{
    g_update_dirty = true;
    g_draw_list_valid = false;
    ++g_visual_revision;
}
void SyncResources()
{
    const uint64_t revision = g_renderer ? g_renderer->ResourceRevision() : 0;
    if (revision == g_resource_revision) return;
    g_resource_revision = revision;
    g_draw_list_valid = false;
    ++g_visual_revision;
}
double RemainingUpdateDelay()
{
    if (!g_context || g_update_dirty) return 0;
    // RmlUi 6.3 returns an interval requested during the last Update(), not a
    // deadline. Account for real elapsed time so caret/animation timers fire.
    const double delay = g_context->GetNextUpdateDelay();
    if (std::isnan(delay) || delay < 0) return 0;
    return std::max(0.0, delay - std::max(0.0, g_system.GetElapsedTime() - g_last_update_time));
}
void EnsureUpdated(bool scheduled = false);

bool OwnerThread() { return g_initialized && std::this_thread::get_id() == g_owner; }
Document* GetDocument(uint64_t id)
{
    if (!OwnerThread()) return nullptr;
    const auto found = g_documents.find(id);
    return found != g_documents.end() ? &found->second : nullptr;
}
uint64_t DocumentId(Rml::ElementDocument* document)
{
    for (const auto& entry : g_documents) if (entry.second.element == document) return entry.first;
    return 0;
}
bool ForegroundDocument(Rml::ElementDocument* document)
{
    Rml::ElementDocument* page = nullptr;
    for (int index = g_context->GetNumDocuments() - 1; index >= 0; --index) {
        auto* candidate = g_context->GetDocument(index);
        if (!candidate || !candidate->IsVisible()) continue;
        if (candidate->IsModal()) return candidate == document;
        auto* record = GetDocument(DocumentId(candidate));
        if (!page && record && record->layer != 2) page = candidate;
    }
    return page == document;
}
bool TranslateAction(const std::string& action, PrimeIntent& intent)
{
    struct Mapping { const char* text; PrimeIntentKind kind; int argument; };
    static constexpr Mapping mappings[] = {
        {"route:home", PrimeIntentKind::Navigate, 0}, {"route:play", PrimeIntentKind::Navigate, 1},
        {"route:offline", PrimeIntentKind::Navigate, 2}, {"route:hunter", PrimeIntentKind::Navigate, 3},
        {"route:forge", PrimeIntentKind::Navigate, 4}, {"route:community", PrimeIntentKind::Navigate, 4},
        {"route:theatre", PrimeIntentKind::Navigate, 5}, {"route:settings", PrimeIntentKind::Navigate, 6},
        {"route:news", PrimeIntentKind::Navigate, 7}, {"route:adventure", PrimeIntentKind::Navigate, 8},
        {"route:social", PrimeIntentKind::Navigate, 9}, {"quit", PrimeIntentKind::Quit, 0},
        {"home:drawer-open", PrimeIntentKind::HomeDrawerOpen, 0},
        {"home:drawer-close", PrimeIntentKind::HomeDrawerClose, 0},
        {"home:deploy", PrimeIntentKind::HomeDeploy, 0},
        {"studio:open", PrimeIntentKind::OpenStudio, 0},
        {"play:quick", PrimeIntentKind::PlayQuick, 0}, {"play:browse", PrimeIntentKind::PlayBrowse, 0},
        {"play:create-open", PrimeIntentKind::PlayCreateOpen, 0}, {"play:create", PrimeIntentKind::PlayCreate, 0},
        {"play:join", PrimeIntentKind::PlayJoin, 0}, {"play:cancel", PrimeIntentKind::PlayCancel, 0},
        {"play:next-map", PrimeIntentKind::PlayNextMap, 0}, {"play:next-mode", PrimeIntentKind::PlayNextMode, 0},
        {"play:toggle-host", PrimeIntentKind::PlayToggleHost, 0},
        {"lobby:ready", PrimeIntentKind::LobbyReady, 0}, {"lobby:start", PrimeIntentKind::LobbyStart, 0},
        {"lobby:leave", PrimeIntentKind::LobbyLeave, 0}, {"lobby:next-hunter", PrimeIntentKind::LobbyNextHunter, 0},
        {"lobby:next-suit", PrimeIntentKind::LobbyNextSuit, 0}, {"lobby:classic", PrimeIntentKind::LobbyClassic, 0},
        {"lobby:rules-open", PrimeIntentKind::LobbyRulesOpen, 0}, {"lobby:rules-close", PrimeIntentKind::LobbyRulesClose, 0},
        {"lobby:rules-apply", PrimeIntentKind::LobbyRulesApply, 0}, {"lobby:rules-map", PrimeIntentKind::LobbyRulesMap, 0},
        {"lobby:rules-mode", PrimeIntentKind::LobbyRulesMode, 0}, {"lobby:rules-format", PrimeIntentKind::LobbyRulesFormat, 0},
        {"lobby:admin-open", PrimeIntentKind::LobbyAdminOpen, 0},
        {"lobby:kick", PrimeIntentKind::LobbyKick, 0},
        {"lobby:transfer-owner", PrimeIntentKind::LobbyTransferOwner, 0},
        {"lobby:close", PrimeIntentKind::LobbyClose, 0},
        {"lobby:bot-add", PrimeIntentKind::LobbyBotAdd, 0},
        {"lobby:bot-remove", PrimeIntentKind::LobbyBotRemove, 0},
        {"lobby:bot-configure", PrimeIntentKind::LobbyBotConfigure, 0},
        {"lobby:handicap-next", PrimeIntentKind::LobbyHandicapNext, 0},
        {"lobby:team-next", PrimeIntentKind::LobbyTeamNext, 0},
        {"lobby:teams-auto", PrimeIntentKind::LobbyTeamsAuto, 0},
        {"lobby:team-lock-toggle", PrimeIntentKind::LobbyTeamLockToggle, 0},
        {"lobby:chat-send", PrimeIntentKind::LobbyChatSend, 0},
        {"lobby:map-retry", PrimeIntentKind::LobbyMapRetry, 0},
        {"lobby:bot-hunter-next", PrimeIntentKind::LobbyBotHunterNext, 0},
        {"lobby:bot-suit-next", PrimeIntentKind::LobbyBotSuitNext, 0},
        {"lobby:bot-level-next", PrimeIntentKind::LobbyBotLevelNext, 0},
        {"lobby:admin-confirm", PrimeIntentKind::LobbyAdminConfirm, 0},
        {"lobby:admin-cancel", PrimeIntentKind::LobbyAdminCancel, 0},
        {"lobby:admin-close", PrimeIntentKind::LobbyAdminClose, 0},
        {"hunter:open", PrimeIntentKind::HunterOpen, 0},
        {"hunter:spectator", PrimeIntentKind::HunterSpectator, 0},
        {"hunter:apply", PrimeIntentKind::HunterApply, 0},
        {"hunter:cancel", PrimeIntentKind::HunterCancel, 0},
        {"hunter:skin-next", PrimeIntentKind::HunterSkinNext, 0},
        {"hunter:armor-next", PrimeIntentKind::HunterArmorNext, 0},
        {"hunter:death-next", PrimeIntentKind::HunterDeathNext, 0},
        {"hunter:cosmetics-reset", PrimeIntentKind::HunterCosmeticsReset, 0},
        {"hunter:preview-death", PrimeIntentKind::HunterPreviewDeath, 0},
        {"hunter:cosmetics-save", PrimeIntentKind::HunterCosmeticsSave, 0},
        {"hunter:loop-death", PrimeIntentKind::HunterLoopDeath, 0},
        {"hunter:compare-native", PrimeIntentKind::HunterCompareNative, 0},
        {"hunter:reset-preview", PrimeIntentKind::HunterResetPreview, 0},
        {"settings:apply", PrimeIntentKind::SettingsApply, 0},
        {"settings:discard", PrimeIntentKind::SettingsDiscard, 0},
        {"settings:reset-category", PrimeIntentKind::SettingsResetCategory, 0},
        {"settings:close", PrimeIntentKind::SettingsClose, 0},
        {"settings:keep-video", PrimeIntentKind::SettingsKeepVideo, 0},
        {"settings:revert-video", PrimeIntentKind::SettingsRevertVideo, 0},
        {"adventure:next-hunter", PrimeIntentKind::AdventureNextHunter, 0},
        {"adventure:continue", PrimeIntentKind::AdventureContinue, 0},
        {"adventure:new-run", PrimeIntentKind::AdventureNewRun, 0},
        {"adventure:confirm-new-run", PrimeIntentKind::AdventureConfirmNewRun, 0},
        {"adventure:cancel-new-run", PrimeIntentKind::AdventureCancelNewRun, 0},
        {"adventure:refresh", PrimeIntentKind::AdventureRefresh, 0},
        {"community:refresh", PrimeIntentKind::CommunityRefresh, 0},
        {"community:search", PrimeIntentKind::CommunitySearch, 0},
        {"community:confirm", PrimeIntentKind::CommunityConfirm, 0},
        {"community:cancel", PrimeIntentKind::CommunityCancel, 0},
        {"community:cancel-work", PrimeIntentKind::CommunityCancelWork, 0},
        {"community:report", PrimeIntentKind::CommunityReport, 0},
        {"community:import", PrimeIntentKind::CommunityImport, 0},
        {"community:page:previous", PrimeIntentKind::CommunityPage, -1},
        {"community:page:next", PrimeIntentKind::CommunityPage, 1},
        {"community:revision-page:previous", PrimeIntentKind::CommunityRevisionPage, -1},
        {"community:revision-page:next", PrimeIntentKind::CommunityRevisionPage, 1},
        {"offline:damage", PrimeIntentKind::OfflineDamage, 0},
        {"offline:launch-match", PrimeIntentKind::OfflineLaunchMatch, 0},
        {"offline:open-rules", PrimeIntentKind::OfflineOpenRules, 0},
        {"offline:cancel-rules", PrimeIntentKind::OfflineCancelRules, 0},
        {"offline:apply-rules", PrimeIntentKind::OfflineApplyRules, 0},
        {"offline:launch-training", PrimeIntentKind::OfflineLaunchTraining, 0},
        {"offline:open-training-options", PrimeIntentKind::OfflineOpenTrainingOptions, 0},
        {"offline:close-training-options", PrimeIntentKind::OfflineCloseTrainingOptions, 0},
        {"offline:open-arena", PrimeIntentKind::OfflineOpenArena, 0},
        {"offline:arena-search", PrimeIntentKind::OfflineArenaSearch, 0},
        {"offline:cancel-arena", PrimeIntentKind::OfflineCancelArena, 0},
        {"offline:arena-page:previous", PrimeIntentKind::OfflineArenaPage, -1},
        {"offline:arena-page:next", PrimeIntentKind::OfflineArenaPage, 1}
    };
    for (const auto& mapping : mappings) if (action == mapping.text) {
        intent.kind = uint32_t(mapping.kind); intent.argument = mapping.argument; return true;
    }
    const char* stages[] = {"quick", "browser", "offline", "adventure"};
    for (int index = 0; index < 4; ++index) {
        if (action == std::string("stage:") + stages[index]) {
            intent.kind = uint32_t(PrimeIntentKind::StageSelect); intent.argument = index; return true;
        }
        if (action == std::string("stage-preview:") + stages[index]) {
            intent.kind = uint32_t(PrimeIntentKind::StagePreview); intent.argument = index; return true;
        }
    }
    for (int index = 0; index < 8; ++index) if (action == "play:server:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::PlayServer); intent.argument = index; return true;
    }
    for (int index = 0; index < 16; ++index) if (action == "lobby:rules-toggle:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::LobbyRulesToggle); intent.argument = index; return true;
    }
    for (int index = 0; index < 8; ++index) if (action == "lobby:player:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::LobbyPlayerSelect); intent.argument = index; return true;
    }
    for (int index = 0; index < 7; ++index) if (action == "hunter:select:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::HunterSelect); intent.argument = index; return true;
    }
    for (int index = 0; index < 4; ++index) if (action == "hunter:suit:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::HunterSuit); intent.argument = index; return true;
    }
    for (int index = 0; index < 4; ++index) if (action == "hunter:preview-mode:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::HunterPreviewMode); intent.argument = index; return true;
    }
    for (int index = 0; index < 64; ++index) if (index != 15 && action == "settings:action:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::SettingsAction); intent.argument = index; return true;
    }
    for (int index = 0; index < 9; ++index) if (action == "settings:category:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::SettingsCategory); intent.argument = index; return true;
    }
    for (int index = 1; index < 4; ++index) if (action == "adventure:slot:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::AdventureSelectSlot); intent.argument = index; return true;
    }
    for (int index = 0; index < 16; ++index) if (action == "offline:choice:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::OfflineChoice); intent.argument = index; return true;
    }
    for (int index = 0; index < 12; ++index) if (action == "offline:rule:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::OfflineRuleToggle); intent.argument = index; return true;
    }
    for (int index = 0; index < 5; ++index) if (action == "offline:training-toggle:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::OfflineTrainingToggle); intent.argument = index; return true;
    }
    for (int index = 0; index < 2; ++index) if (action == "hunter:rotate:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::HunterRotate); intent.argument = index; return true;
    }
    for (int index = 0; index < 4; ++index) if (action == "lobby:team:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::LobbyTeamSelect); intent.argument = index; return true;
    }
    for (int index = 0; index < 2; ++index) if (action == "hunter:zoom:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::HunterZoom); intent.argument = index; return true;
    }
    const char* theatreaction_names[] = {"search", "next-filter", "next-sort", "previous-page", "next-page", "refresh", "watch", "favorite", "validate", "recover", "cancel-job", "export", "rename", "organize", "delete", "confirm-delete", "cancel-delete", "reveal", "studio", "import", "import-path", "favorite-filtered", "validate-filtered", "clear-search", "cancel-launch"};
    for (int index = 0; index < 25; ++index) if (action == std::string("theatre:") + theatreaction_names[index]) {
        intent.kind = uint32_t(PrimeIntentKind::TheatreAction); intent.argument = index; return true;
    }
    const char* replayaction_names[] = {"toggle-pause", "jump-back", "jump-forward", "restart", "step", "next-rate", "next-camera", "previous-player", "next-player", "seek", "studio", "back", "fullscreen"};
    for (int index = 0; index < 13; ++index) if (action == std::string("replay:") + replayaction_names[index]) {
        intent.kind = uint32_t(PrimeIntentKind::ReplayAction); intent.argument = index; return true;
    }
    for (int index = 0; index < 16; ++index) if (action == "offline:arena:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::OfflineSelectArena); intent.argument = index; return true;
    }
    for (int index = 0; index < 8; ++index) if (action == "theatre:entry:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::TheatreEntry); intent.argument = index; return true;
    }
    for (int index = 0; index < 3; ++index) if (action == "theatre:thumbnail:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::TheatreThumbnail); intent.argument = index; return true;
    }
    const char* licenseaction_names[] = {"overview", "customization", "stats", "history", "achievements", "emblems", "titles", "comparison", "account", "refresh", "cancel-refresh", "previous-page", "next-page", "send-verification", "finish-password", "recover", "link-google", "link-github", "link-discord", "refresh-account", "back"};
    for (int index = 0; index < 21; ++index) if (action == std::string("license:") + licenseaction_names[index]) {
        intent.kind = uint32_t(PrimeIntentKind::LicenseAction); intent.argument = index; return true;
    }
    const char* studioaction_names[] = {"launch", "pick-map", "open-path", "recover", "cancel"};
    for (int index = 0; index < 5; ++index) if (action == std::string("studio:") + studioaction_names[index]) {
        intent.kind = uint32_t(PrimeIntentKind::StudioAction); intent.argument = index; return true;
    }
    for (int index = 0; index < 3; ++index) if (action == "community:tab:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::CommunityTab); intent.argument = index; return true;
    }
    for (int index = 0; index < 3; ++index) if (action == "community:sort:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::CommunitySort); intent.argument = index; return true;
    }
    for (int index = 0; index < 4; ++index) if (action == "community:lifecycle:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::CommunityLifecycle); intent.argument = index; return true;
    }
    for (int index = 0; index < 8; ++index) if (action == "community:select:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::CommunitySelect); intent.argument = index; return true;
    }
    for (int index = 0; index < 10; ++index) if (action == "community:detail:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::CommunityDetailAction); intent.argument = index; return true;
    }
    for (int index = 0; index < 8; ++index) if (action == "community:revision:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::CommunitySelectRevision); intent.argument = index; return true;
    }
    for (int index = 0; index < 5; ++index) if (action == "community:creator:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::CommunityCreatorAction); intent.argument = index; return true;
    }
    for (int index = 0; index < 2; ++index) if (action == "community:upload:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::CommunityUpload); intent.argument = index; return true;
    }
    for (int index = 0; index < 3; ++index) if (action == "community:visibility:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::CommunityVisibility); intent.argument = index; return true;
    }
    for (int index = 0; index < 6; ++index) if (action == "community:report-reason:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::CommunityReportReason); intent.argument = index; return true;
    }
    for (int index = 0; index < 5; ++index) if (action == "community:conflict:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::CommunityConflict); intent.argument = index; return true;
    }
    const char* queue_actions[] = {"queue-join", "queue-accept", "queue-decline", "queue-leave"};
    for (int index = 0; index < 4; ++index) if (action == std::string("play:") + queue_actions[index]) {
        intent.kind = uint32_t(PrimeIntentKind::PlayQueueAction); intent.argument = index; return true;
    }
    const char* social_names[] = {"friends", "players", "requests", "invites", "party", "recent", "blocked", "refresh", "cancel", "lookup", "search", "previous-page", "next-page", "select-0", "select-1", "select-2", "select-3", "select-4", "select-5", "select-6", "select-7", "send-friend-request", "accept-friend-request", "decline-friend-request", "cancel-friend-request", "remove-friend", "block-player", "unblock-player", "send-game-invite", "join-friend", "accept-game-invite", "decline-game-invite", "cancel-game-invite", "invite-party", "accept-party-invite", "decline-party-invite", "cancel-party-invite", "leave-party", "disband-party", "kick-party-member", "promote-party-member", "invite-party-lobby", "follow-party-travel", "join-party-leader", "decline-party-travel", "cancel-reservation", "confirm", "dismiss", "presence-next", "activity-next", "invites-next", "dnd", "back"};
    for (int index = 0; index < 53; ++index) if (action == std::string("social:") + social_names[index]) {
        intent.kind = uint32_t(PrimeIntentKind::SocialAction); intent.argument = index; return true;
    }
    for (int index = 0; index < 12; ++index) if (action == "news:action:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::NewsAction); intent.argument = index; return true;
    }
    const char* results_actions[] = {"close", "search", "previous-page", "next-page", "rematch", "clear-search"};
    for (int index = 0; index < 6; ++index) if (action == std::string("results:") + results_actions[index]) {
        intent.kind = uint32_t(PrimeIntentKind::ResultsAction); intent.argument = index; return true;
    }
    const char* aimresults_actions[] = {"retry", "change-drill", "exit"};
    for (int index = 0; index < 3; ++index) if (action == std::string("aim-results:") + aimresults_actions[index]) {
        intent.kind = uint32_t(PrimeIntentKind::AimResultsAction); intent.argument = index; return true;
    }
    for (int index = 0; index < 4096; ++index) if (action == "results:map:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::ResultsMap); intent.argument = index; return true;
    }
    for (int index = 0; index < 15; ++index) if (action == "setup:action:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::SetupAction); intent.argument = index; return true;
    }
    for (int index = 0; index < 30; ++index) if (action == "setup:release:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::SetupRelease); intent.argument = index; return true;
    }
    const char* hud_actions[] = {"open", "use", "cancel", "next-preset", "undo", "redo", "lock-all", "unlock-all", "align-left", "align-top", "native-elements", "reset-hud", "toggle-visible", "toggle-lock", "next-anchor", "next-visibility", "reset-element", "reset-section", "next-aspect", "next-hunter", "next-scenario", "next-grid", "toggle-guides", "save-named", "load-named", "export-json", "import-json", "property-previous", "property-next", "property-apply", "property-reset", "next-palette", "next-crosshair-target", "toggle-crosshair-override", "next-crosshair-preset", "share-crosshair", "import-crosshair", "next-radar-preset", "nudge-left", "nudge-right", "nudge-up", "nudge-down", "scale-down", "scale-up", "apply-layout"};
    for (int index = 0; index < 45; ++index) if (action == std::string("hud:") + hud_actions[index]) {
        intent.kind = uint32_t(PrimeIntentKind::HudAction); intent.argument = index; return true;
    }
    for (int index = 0; index < 14; ++index) if (action == "hud:element:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::HudElement); intent.argument = index; return true;
    }
    for (int index = 0; index < 8; ++index) if (action == "hud:property:" + std::to_string(index)) {
        intent.kind = uint32_t(PrimeIntentKind::HudProperty); intent.argument = index; return true;
    }
    const char* in_game_actions[] = {"resume", "fullscreen", "settings", "replay", "vote", "spectate", "rejoin", "recorder", "return-lobby", "leave", "quit", "confirm", "cancel", "map-next", "vote-submit", "vote-yes", "vote-no", "bots", "bot-select", "bot-hunter", "bot-suit", "bot-skill", "bot-team", "bot-handicap", "bot-remove", "bot-add", "bot-apply"};
    for (int index = 0; index < 27; ++index) if (action == std::string("ingame:") + in_game_actions[index]) {
        intent.kind = uint32_t(PrimeIntentKind::InGameAction); intent.argument = index; return true;
    }
    return false;
}
void QueueAction(const std::string& action, uint64_t document)
{
    auto* source = GetDocument(document);
    if (!source) return;
    DirtyVisual();
    source->booleans.clear(); // DOM callbacks may change presenter booleans.
    PrimeIntent intent;
    if (!TranslateAction(action, intent)) return;
    intent.generation = g_generation;
    intent.document_id = document;
    intent.sequence = ++g_next_sequence;
    // A bounded queue prevents repeated-input accumulation while a host stalls.
    if (g_actions.size() < 256) g_actions.push_back({intent, action});
}
void QueueHomeAction(const std::string& action) { QueueAction(action, g_home_id); }
class DocumentActionListener final : public Rml::EventListener {
public:
    void ProcessEvent(Rml::Event& event) override
    {
        const bool change = event.GetType() == "change";
        // Range SetValue emits the same synchronous change event as user input.
        // Presenter bindings must not feed their authoritative position back as
        // a user seek command. Keyboard, pointer and accessibility edits remain
        // outside this scope and still dispatch through the real DOM listener.
        if (change && g_programmatic_field_mutations != 0) return;
        const bool click = event.GetType() == "click";
        const bool range = event.GetTargetElement()->GetTagName() == "input"
            && event.GetTargetElement()->GetAttribute<Rml::String>("type", "") == "range";
        if ((change && !range) || (click && range)) return;
        for (auto* element = event.GetTargetElement(); element; element = element->GetParentNode()) {
            const Rml::String action = element->GetAttribute<Rml::String>(click || change ? "data-action" : "data-preview", "");
            if (!action.empty()) {
                if (!element->HasAttribute("disabled")) QueueAction(action, DocumentId(element->GetOwnerDocument()));
                break;
            }
        }
    }
};
DocumentActionListener g_action_listener;

uint64_t RegisterDocument(Rml::ElementDocument* element, const std::string& path, int layer)
{
    if (!element) return 0;
    const uint64_t id = ++g_next_document_id;
    Document entry;
    entry.element = element; entry.relative_path = path; entry.layer = layer;
    if (auto* focused = g_context->GetFocusElement()) {
        entry.restore_document = DocumentId(focused->GetOwnerDocument());
        entry.restore_element = focused->GetId();
    }
    g_documents.emplace(id, std::move(entry));
    g_document_order.push_back(id);
    g_lobby_anchor_dirty = true;
    DirtyVisual();
    element->AddEventListener("click", &g_action_listener);
    element->AddEventListener("change", &g_action_listener);
    element->AddEventListener("mouseover", &g_action_listener);
    element->AddEventListener("focus", &g_action_listener, true);
    element->Show(layer == 1 ? Rml::ModalFlag::Modal : Rml::ModalFlag::None);
    return id;
}
int CopyUtf8(const std::string& value, unsigned char* buffer, int capacity)
{
    if (!buffer || capacity <= 1) return 0;
    if (value.size() >= size_t(capacity)) { buffer[0] = 0; return -int(value.size() + 1); }
    std::memcpy(buffer, value.data(), value.size()); buffer[value.size()] = 0;
    return int(value.size());
}

bool ReadFiniteNumbers(const std::string& text, float* values, int count)
{
    const char* cursor = text.c_str();
    for (int i = 0; i < count; ++i) {
        errno = 0;
        char* end = nullptr;
        values[i] = std::strtof(cursor, &end);
        if (end == cursor || errno == ERANGE || !std::isfinite(values[i])) return false;
        cursor = end;
        while (*cursor && std::isspace(static_cast<unsigned char>(*cursor))) ++cursor;
        if (i + 1 < count) { if (*cursor++ != ',') return false; }
        else if (*cursor) return false;
    }
    return true;
}

Rml::ElementFormControl* TextField(Rml::Element* element)
{
    if (auto* input = dynamic_cast<Rml::ElementFormControlInput*>(element)) return input;
    return dynamic_cast<Rml::ElementFormControlTextArea*>(element);
}


void PositionLobbyNameplates()
{
    if (!g_context || !g_lobby_anchor_dirty) return;
    for (auto& entry : g_documents) {
    auto* document = entry.second.element;
    if (!document->IsVisible()) continue;
    Rml::Element* stage = document->GetElementById("lobby_stage");
    if (!stage) continue;

    const Rml::Vector2f stageOrigin = stage->GetAbsoluteOffset(Rml::BoxArea::Border);
    const Rml::Vector2i dimensions = g_context->GetDimensions();
    // On 720p the foreground local pad is close to the action strip. Keep
    // the player label above that strip without inventing per-DPI RCSS offsets.
    // RmlUi's GetAbsoluteOffset is non-const even for layout queries.
    Rml::Element* actions = document->GetElementById("lobby_actions");
    const float actionTop = actions
        ? actions->GetAbsoluteOffset(Rml::BoxArea::Border).y
        : float(dimensions.y);
    for (int index = 0; index < 8; ++index) {
        const std::string id = "lobby_slot" + std::to_string(index);
        Rml::Element* label = document->GetElementById(id);
        if (!label) continue;
        const Rml::Vector2f size = label->GetBox().GetSize(Rml::BoxArea::Border);
        const float left = g_lobby_anchors[index].x * float(dimensions.x)
            - stageOrigin.x - size.x * 0.5f;
        const float desiredTop = g_lobby_anchors[index].y * float(dimensions.y)
            - stageOrigin.y - size.y * 0.5f;
        const float maxTop = actionTop - stageOrigin.y - size.y - 8.f;
        const float top = std::max(0.f, std::min(desiredTop, maxTop));
        label->SetProperty("left", std::to_string(left) + "px");
        label->SetProperty("top", std::to_string(top) + "px");
        DirtyVisual();
    }
    }
    g_lobby_anchor_dirty = false;
}

Rml::Element* FindElementById(const char* id)
{
    return g_document && id ? g_document->GetElementById(id) : nullptr;
}

void FocusElement(const char* id)
{
    if (Rml::Element* element = FindElementById(id))
        if (g_context->GetFocusElement() != element && element->Focus()) DirtyVisual();
}

void EnsureUpdated(bool scheduled)
{
    if (!g_context || (!g_update_dirty && (!scheduled || RemainingUpdateDelay() > 0))) return;
    // An update may dispatch a model's pending focus or position nameplates.
    // Settle those mutations once; a zero-delay animation belongs to the next
    // frame, rather than causing two timer-driven updates in this frame.
    for (int pass = 0; pass < 2; ++pass) {
        g_update_dirty = false;
        g_context->Update();
        g_updated_since_render = true;
        g_last_update_time = g_system.GetElapsedTime();
        ++g_visual_revision;
        g_draw_list_valid = false;
        PositionLobbyNameplates();
        if (g_model) { ProgrammaticFieldMutation binding; g_model->ApplyPendingFocus(); }
        SyncResources();
        if (!g_update_dirty) break;
    }
}

void ScrollFocusedElement(Rml::Element* element)
{
    // A pointer gesture captures its canvas bounds before requesting focus.
    // Authored canvases keep that coordinate space fixed under the finger.
    // Ordinary controls retain nearest scrolling for keyboard/accessibility.
    if (element->GetAttribute<Rml::String>("data-focus-scroll", "") != "none")
        element->ScrollIntoView(Rml::ScrollIntoViewOptions(Rml::ScrollAlignment::Nearest));
}

int ConvertModifiers(int modifiers)
{
    int result = 0;
    if (modifiers & 1) result |= Rml::Input::KM_SHIFT;
    if (modifiers & 2) result |= Rml::Input::KM_CTRL;
    if (modifiers & 4) result |= Rml::Input::KM_ALT;
    if (modifiers & 8) result |= Rml::Input::KM_META;
#if defined(__APPLE__)
    if (modifiers & 8) result |= Rml::Input::KM_CTRL;
#endif
    return result;
}

Rml::Input::KeyIdentifier ConvertKey(int key)
{
    if (key >= 32 && key <= 57) return static_cast<Rml::Input::KeyIdentifier>(int(Rml::Input::KI_A) + key - 32);
    switch (key) {
    case 1: return Rml::Input::KI_TAB;
    case 2: return Rml::Input::KI_RETURN;
    case 3: return Rml::Input::KI_ESCAPE;
    case 4: return Rml::Input::KI_SPACE;
    case 5: return Rml::Input::KI_UP;
    case 6: return Rml::Input::KI_DOWN;
    case 7: return Rml::Input::KI_LEFT;
    case 8: return Rml::Input::KI_RIGHT;
    case 9: return Rml::Input::KI_HOME;
    case 10: return Rml::Input::KI_END;
    case 11: return Rml::Input::KI_PRIOR;
    case 12: return Rml::Input::KI_NEXT;
    case 13: return Rml::Input::KI_BACK;
    case 14: return Rml::Input::KI_DELETE;
    case 15: return Rml::Input::KI_INSERT;
    case 16: return Rml::Input::KI_A;
    case 17: return Rml::Input::KI_C;
    case 18: return Rml::Input::KI_V;
    case 19: return Rml::Input::KI_X;
    case 20: return Rml::Input::KI_Y;
    case 21: return Rml::Input::KI_Z;
    default: return Rml::Input::KI_UNKNOWN;
    }
}

std::filesystem::path RootPath(const char* root, const char* child)
{
    return std::filesystem::path(root ? root : "") / child;
}

} // namespace

PP_EXPORT int pp_rmlui_initialize(int width, int height, float density, const char* asset_root)
{
    return pp_rmlui_initialize_backend(width, height, density, asset_root, 0);
}

PP_EXPORT int pp_rmlui_initialize_backend(int width, int height, float density, const char* asset_root, int backend)
{
    if (g_initialized) return OwnerThread() ? 1 : 0;
    if (!asset_root || width <= 0 || height <= 0 || !std::isfinite(density) || density <= 0) return 0;
    if (backend == 1) g_renderer = CreatePrimeDrawListRenderer();
#if defined(PP_RMLUI_GL2)
    else if (backend == 0) g_renderer = CreatePrimeGl2Renderer();
#endif
    else return 0;
    g_owner = std::this_thread::get_id();
    g_backend = backend;
    g_update_dirty = true; g_draw_list_valid = false;
    g_visual_revision = 0; g_resource_revision = 0;
    g_last_update_time = g_system.GetElapsedTime(); g_pointer_known = false; g_pointer_buttons = 0;
    g_capture_width = 0; g_capture_height = 0;
    g_updated_since_render = false;
    g_renderer->SetViewport(width, height);
    Rml::SetSystemInterface(&g_system);
    Rml::SetRenderInterface(g_renderer->Interface());
    if (!Rml::Initialise()) {
        Rml::SetRenderInterface(nullptr); Rml::SetSystemInterface(nullptr); g_renderer.reset(); return 0;
    }
    g_initialized = true;
    ++g_generation;
    std::error_code error;
    g_asset_root = std::filesystem::weakly_canonical(asset_root, error);
    if (error) { pp_rmlui_shutdown(); return 0; }

    const std::string regular = RootPath(asset_root, "fonts/Rajdhani-SemiBold.ttf").string();
    const std::string bold = RootPath(asset_root, "fonts/Rajdhani-Bold.ttf").string();
    const std::string mono = RootPath(asset_root, "fonts/JetBrainsMono-Regular.ttf").string();
    if (!Rml::LoadFontFace(regular) || !Rml::LoadFontFace(bold) || !Rml::LoadFontFace(mono)) {
        pp_rmlui_shutdown(); return 0;
    }
    const auto japanese = RootPath(asset_root, "fonts/NotoSansJP-Regular.ttf");
    if (std::filesystem::is_regular_file(japanese) && !Rml::LoadFontFace(japanese.string(), true)) {
        pp_rmlui_shutdown(); return 0;
    }
    Rml::SetTextInputHandler(&g_text_input);
    g_context = Rml::CreateContext("project-prime-rmlui", {width, height});
    if (!g_context) { pp_rmlui_shutdown(); return 0; }
    g_text_input.Attach(g_context, g_generation.load(), DocumentId);
    g_context->SetDensityIndependentPixelRatio(std::max(density, 1.0f));
    g_model = std::make_unique<PrimeMenuModel>();
    if (!g_model->Setup(g_context)) { pp_rmlui_shutdown(); return 0; }
    g_home_id = pp_rmlui_document_open("prime_home.rml", 0);
    if (!g_home_id) { pp_rmlui_shutdown(); return 0; }
    g_document = GetDocument(g_home_id)->element;
    FocusElement("activity_selector");
    return 1;
}

PP_EXPORT void pp_rmlui_shutdown()
{
    if (!g_initialized && !g_renderer) return;
    if (g_initialized && !OwnerThread()) return;
    g_text_input.Detach();
    // Documents and their model listeners are released while both the RmlUi core
    // and render adapter still exist. RemoveContext flushes deferred Close().
    for (auto& entry : g_documents) {
        entry.second.element->RemoveEventListener("click", &g_action_listener);
    entry.second.element->RemoveEventListener("change", &g_action_listener);
    entry.second.element->RemoveEventListener("mouseover", &g_action_listener);
    entry.second.element->RemoveEventListener("focus", &g_action_listener, true);
        entry.second.element->Close();
    }
    g_accessibility.Clear();
    g_documents.clear(); g_document_order.clear(); g_actions.clear();
    g_document = nullptr; g_home_id = 0;
    if (g_context) {
        Rml::RemoveContext("project-prime-rmlui"); g_context = nullptr;
    }
    Rml::SetTextInputHandler(nullptr);
    g_model.reset();
    g_lobby_anchor_dirty = true; g_lobby_anchors = {};
    if (g_initialized) Rml::Shutdown();
    g_initialized = false;
    Rml::SetRenderInterface(nullptr); Rml::SetSystemInterface(nullptr);
    g_renderer.reset(); g_asset_root.clear(); g_system.ClearClipboard();
    g_backend = -1; g_update_dirty = true; g_draw_list_valid = false; g_pointer_known = false;
    ++g_generation; // Invalidates worker completions even before a reinit.
}

PP_EXPORT void pp_rmlui_update()
{
    if (!OwnerThread() || !g_context) return;
    EnsureUpdated(true);
}

PP_EXPORT int pp_rmlui_update_status(PrimeUpdateStatus* status)
{
    if (!OwnerThread() || !g_context || !status || status->size != sizeof(PrimeUpdateStatus) || status->version != 1) return 0;
    SyncResources();
    status->generation = g_generation;
    status->visual_revision = g_visual_revision;
    status->next_update_delay_seconds = RemainingUpdateDelay();
    status->flags = (g_update_dirty ? 1u : 0u) | (g_backend == 1 && g_draw_list_valid ? 2u : 0u);
    status->reserved = 0;
    return 1;
}

PP_EXPORT void pp_rmlui_render(int width, int height)
{
    if (!OwnerThread() || !g_context || !g_renderer || width <= 0 || height <= 0) return;
    if (width != g_capture_width || height != g_capture_height) {
        g_capture_width = width; g_capture_height = height; DirtyVisual();
    }
    g_renderer->SetViewport(width, height);
    EnsureUpdated(!g_updated_since_render);
    SyncResources();
    if (g_backend == 1 && g_draw_list_valid) { g_updated_since_render = false; return; }
    g_renderer->BeginFrame();
    g_context->Render();
    g_renderer->EndFrame();
    SyncResources();
    g_draw_list_valid = g_backend == 1 && !g_update_dirty;
    g_updated_since_render = false;
}

PP_EXPORT void pp_rmlui_resize(int width, int height, float density)
{
    if (!OwnerThread() || !g_context || !g_renderer || width <= 0 || height <= 0) return;
    g_renderer->SetViewport(width, height);
    const float dpi = std::isfinite(density) && density > 0 ? std::max(density, 1.0f) : g_context->GetDensityIndependentPixelRatio();
    if (g_context->GetDimensions() == Rml::Vector2i(width, height) && g_context->GetDensityIndependentPixelRatio() == dpi) return;
    g_context->SetDimensions({width, height});
    g_context->SetDensityIndependentPixelRatio(dpi);
    g_lobby_anchor_dirty = true;
    DirtyVisual();
}

PP_EXPORT void pp_rmlui_set_lobby_anchor(int slot, float center_x, float center_y)
{
    if (!OwnerThread() || slot < 0 || slot >= int(g_lobby_anchors.size()) || !std::isfinite(center_x) || !std::isfinite(center_y)) return;
    if (g_lobby_anchors[slot].x == center_x && g_lobby_anchors[slot].y == center_y) return;
    g_lobby_anchors[slot] = {center_x, center_y};
    g_lobby_anchor_dirty = true;
    DirtyVisual();
}

PP_EXPORT int pp_rmlui_mouse_move(int x, int y, int modifiers)
{
    if (!OwnerThread() || !g_context) return 0;
    const bool stationary = g_pointer_known && x == g_pointer_x && y == g_pointer_y && modifiers == g_pointer_modifiers;
    g_pointer_known = true; g_pointer_x = x; g_pointer_y = y; g_pointer_modifiers = modifiers;
    if (!stationary || g_pointer_buttons) DirtyVisual();
    return g_context->ProcessMouseMove(x, y, ConvertModifiers(modifiers)) ? 1 : 0;
}

PP_EXPORT int pp_rmlui_mouse_button(int button, int down, int modifiers)
{
    if (!OwnerThread() || !g_context) return 0;
    DirtyVisual();
    if (button >= 0 && button < 32) {
        if (down) g_pointer_buttons |= 1u << button;
        else g_pointer_buttons &= ~(1u << button);
    }
    const bool propagated = down
        ? g_context->ProcessMouseButtonDown(button, ConvertModifiers(modifiers))
        : g_context->ProcessMouseButtonUp(button, ConvertModifiers(modifiers));
    return propagated ? 1 : 0;
}

PP_EXPORT int pp_rmlui_mouse_wheel(float delta_y, int modifiers)
{
    if (!OwnerThread() || !g_context || !std::isfinite(delta_y)) return 0;
    DirtyVisual();
    return g_context->ProcessMouseWheel(-delta_y, ConvertModifiers(modifiers)) ? 1 : 0;
}

PP_EXPORT int pp_rmlui_key(int key, int down, int modifiers)
{
    if (!OwnerThread() || !g_context) return 0;
    const Rml::Input::KeyIdentifier converted = ConvertKey(key);
    if (converted == Rml::Input::KI_UNKNOWN) return 0;
    DirtyVisual();
    const bool propagated = down
        ? g_context->ProcessKeyDown(converted, ConvertModifiers(modifiers))
        : g_context->ProcessKeyUp(converted, ConvertModifiers(modifiers));
    return propagated ? 1 : 0;
}

PP_EXPORT int pp_rmlui_text(unsigned int codepoint)
{
    if (codepoint == 0 || codepoint > 0x10ffff || (codepoint >= 0xd800 && codepoint <= 0xdfff)) return 0;
    if (!OwnerThread() || !g_context) return 0;
    DirtyVisual();
    return g_context->ProcessTextInput(static_cast<Rml::Character>(codepoint)) ? 1 : 0;
}

PP_EXPORT void pp_rmlui_set_text(const char* name, const char* value)
{
    pp_rmlui_document_set_text(g_home_id, name, value);
}

PP_EXPORT void pp_rmlui_set_bool(const char* name, int value)
{
    pp_rmlui_document_set_bool(g_home_id, name, value);
}

PP_EXPORT void pp_rmlui_set_field(const char* id, const char* value)
{
    pp_rmlui_document_set_field(g_home_id, id, value);
}

PP_EXPORT int pp_rmlui_read_field(const char* id, unsigned char* buffer, int capacity)
{
    return pp_rmlui_document_read_field(g_home_id, id, buffer, capacity);
}

PP_EXPORT int pp_rmlui_back()
{
    if (!OwnerThread()) return 0;
    DirtyVisual();
    if (auto* home = GetDocument(g_home_id)) home->booleans.clear();
    for (auto it = g_document_order.rbegin(); it != g_document_order.rend(); ++it) {
        auto* document = GetDocument(*it);
        if (document && document->layer == 1 && document->element->IsVisible())
            return pp_rmlui_document_close(*it);
    }
    return g_model && g_model->Back() ? 1 : 0;
}

PP_EXPORT int pp_rmlui_take_action(unsigned char* buffer, int capacity)
{
    if (!OwnerThread() || !buffer || capacity <= 1 || g_actions.empty()) return 0;
    const int length = CopyUtf8(g_actions.front().legacy, buffer, capacity);
    if (length >= 0) g_actions.pop_front();
    return length;
}

// Actual RmlUi element bounds are used by the native regression. This does
// not synthesize an action or bypass the DOM's focus/click dispatch.
PP_EXPORT int pp_rmlui_document_element_bounds(uint64_t document_id, const char* id, float* x, float* y,
    float* width, float* height)
{
    auto* document = GetDocument(document_id);
    if (!document || !id || !x || !y || !width || !height)
        return 0;
    EnsureUpdated();
    Rml::Element* element = document->element->GetElementById(id);
    if (!element || !element->IsVisible(true)) return 0;
    const auto offset = element->GetAbsoluteOffset(Rml::BoxArea::Border);
    const auto size = element->GetBox().GetSize(Rml::BoxArea::Border);
    if (size.x <= 0 || size.y <= 0) return 0;
    *x = offset.x;
    *y = offset.y;
    *width = size.x;
    *height = size.y;
    return 1;
}
PP_EXPORT int pp_rmlui_element_bounds(const char* id, float* x, float* y,
    float* width, float* height)
{
    return pp_rmlui_document_element_bounds(g_home_id, id, x, y, width, height);
}

// Bounded POC diagnostic for real native-input regression at each density.
// This reads only the one shipped STUDIO control; no action is synthesized.
PP_EXPORT int pp_rmlui_studio_bounds(float* x, float* y, float* width, float* height)
{
    if (!OwnerThread() || !g_context || !g_document || !x || !y || !width || !height) return 0;
    EnsureUpdated();
    Rml::Element* studio = g_document->GetElementById("nav_studio");
    if (!studio || !studio->IsVisible(true)) return 0;
    const auto offset = studio->GetAbsoluteOffset(Rml::BoxArea::Border);
    const auto size = studio->GetBox().GetSize(Rml::BoxArea::Border);
    if (size.x <= 0 || size.y <= 0) return 0;
    *x = offset.x; *y = offset.y; *width = size.x; *height = size.y;
    return 1;
}

PP_EXPORT uint32_t pp_rmlui_protocol_version() { return PP_RMLUI_PROTOCOL_VERSION; }
PP_EXPORT uint64_t pp_rmlui_generation() { return g_generation; }
PP_EXPORT uint64_t pp_rmlui_home_document() { return OwnerThread() ? g_home_id : 0; }
PP_EXPORT int pp_rmlui_take_intent(PrimeIntent* intent)
{
    if (!OwnerThread() || !intent || intent->size != sizeof(PrimeIntent)
        || intent->version != PP_RMLUI_PROTOCOL_VERSION || g_actions.empty()) return 0;
    *intent = g_actions.front().intent;
    g_actions.pop_front();
    return 1;
}
PP_EXPORT uint64_t pp_rmlui_document_open(const char* relative_path, int layer)
{
    if (!OwnerThread() || !g_context || !relative_path || (layer != 0 && layer != 1)) return 0;
    const std::filesystem::path relative(relative_path);
    if (relative.empty() || relative.is_absolute()) return 0;
    std::error_code error;
    const auto path = std::filesystem::weakly_canonical(g_asset_root / relative, error);
    if (error || path.extension() != ".rml") return 0;
    // The relative-document API cannot escape packaged source-controlled assets,
    // including through symlinks. Resource resolution still uses native RmlUi.
    auto root_part = g_asset_root.begin(); auto path_part = path.begin();
    for (; root_part != g_asset_root.end(); ++root_part, ++path_part)
        if (path_part == path.end() || *root_part != *path_part) return 0;
    return RegisterDocument(g_context->LoadDocument(path.string()), relative.generic_string(), layer);
}
PP_EXPORT int pp_rmlui_document_close(uint64_t document_id)
{
    auto* document = GetDocument(document_id);
    if (!document || document_id == g_home_id) return 0;
    const uint64_t restore_document = document->restore_document;
    const std::string restore_element = document->restore_element;
    document->element->RemoveEventListener("click", &g_action_listener);
    document->element->RemoveEventListener("change", &g_action_listener);
    document->element->RemoveEventListener("mouseover", &g_action_listener);
    document->element->RemoveEventListener("focus", &g_action_listener, true);
    document->element->Close();
    DirtyVisual();
    g_accessibility.Forget(document_id);
    g_documents.erase(document_id);
    g_document_order.erase(std::remove(g_document_order.begin(), g_document_order.end(), document_id), g_document_order.end());
    g_actions.erase(std::remove_if(g_actions.begin(), g_actions.end(), [document_id](const QueuedAction& action) {
        return action.intent.document_id == document_id;
    }), g_actions.end());
    EnsureUpdated(); // Flush the document's deferred detach/release on owner thread.
    if (auto* restore = GetDocument(restore_document)) {
        restore->element->Show(restore->layer == 1 ? Rml::ModalFlag::Modal : Rml::ModalFlag::None, Rml::FocusFlag::Keep);
        if (!restore_element.empty()) pp_rmlui_document_focus(restore_document, restore_element.c_str());
    } else if (auto* home = GetDocument(g_home_id)) {
        home->element->Show(Rml::ModalFlag::None, Rml::FocusFlag::Keep);
    }
    DirtyVisual();
    return 1;
}
PP_EXPORT int pp_rmlui_document_show(uint64_t document_id, int show)
{
    auto* document = GetDocument(document_id);
    if (!document) return 0;
    g_lobby_anchor_dirty = true;
    DirtyVisual();
    if (show) document->element->Show(document->layer == 1 ? Rml::ModalFlag::Modal : Rml::ModalFlag::None, Rml::FocusFlag::Keep);
    else {
        document->element->Hide();
        g_actions.erase(std::remove_if(g_actions.begin(), g_actions.end(), [document_id](const QueuedAction& action) {
            return action.intent.document_id == document_id;
        }), g_actions.end());
    }
    return 1;
}
PP_EXPORT int pp_rmlui_document_focus(uint64_t document_id, const char* element_id)
{
    auto* document = GetDocument(document_id);
    if (!document || !element_id || !document->element->IsVisible()) return 0;
    EnsureUpdated();
    auto* element = document->element->GetElementById(element_id);
    if (!element || !element->IsVisible(true) || element->HasAttribute("disabled") || !element->Focus()) return 0;
    ScrollFocusedElement(element);
    DirtyVisual();
    return 1;
}
PP_EXPORT int pp_rmlui_document_set_text(uint64_t document_id, const char* name, const char* value)
{
    auto* document = GetDocument(document_id);
    if (!document || !name) return 0;
    const std::string text = value ? value : "";
    const auto previous = document->texts.find(name);
    if (previous != document->texts.end() && previous->second == text) return 1;
    const std::string binding(name);
    if (binding.rfind("action:", 0) == 0) {
        auto* element = document->element->GetElementById(binding.substr(7));
        PrimeIntent validated;
        if (!element || !TranslateAction(text, validated)) return 0;
        element->SetAttribute("data-action", text);
    } else if (binding.rfind("rect:", 0) == 0) {
        auto* element = document->element->GetElementById(binding.substr(5));
        float rect[4];
        if (!element || !ReadFiniteNumbers(text, rect, 4) || rect[2] < 0.f || rect[3] < 0.f) return 0;
        for (float value : rect) if (std::abs(value) > 100000.f) return 0;
        static const Rml::PropertyId properties[] = {Rml::PropertyId::Left, Rml::PropertyId::Top, Rml::PropertyId::Width, Rml::PropertyId::Height};
        for (int i = 0; i < 4; ++i) element->SetProperty(properties[i], Rml::Property(rect[i], Rml::Unit::PX));
    } else if (binding.rfind("opacity:", 0) == 0) {
        auto* element = document->element->GetElementById(binding.substr(8));
        float opacity;
        if (!element || !ReadFiniteNumbers(text, &opacity, 1) || opacity < 0.f || opacity > 1.f) return 0;
        element->SetProperty(Rml::PropertyId::Opacity, Rml::Property(opacity, Rml::Unit::NUMBER));
    } else if (binding.rfind("font-size:", 0) == 0) {
        auto* element = document->element->GetElementById(binding.substr(10));
        float size;
        if (!element || !ReadFiniteNumbers(text, &size, 1) || size < 0.f || size > 1000.f) return 0;
        element->SetProperty(Rml::PropertyId::FontSize, Rml::Property(size, Rml::Unit::PX));
    } else if (binding.rfind("color:", 0) == 0 || binding.rfind("ink:", 0) == 0) {
        const bool foreground = binding.rfind("ink:", 0) == 0;
        auto* element = document->element->GetElementById(binding.substr(foreground ? 4 : 6));
        if (!element || (text.size() != 7 && text.size() != 9) || text[0] != '#') return 0;
        for (size_t i = 1; i < text.size(); ++i) if (!std::isxdigit(static_cast<unsigned char>(text[i]))) return 0;
        element->SetProperty(foreground ? "color" : "background-color", text);
    } else if (binding.rfind("image:", 0) == 0) {
        auto* element = document->element->GetElementById(binding.substr(6));
        if (!element || element->GetTagName() != "img" || text.find("://") != std::string::npos
            || text.find('\n') != std::string::npos || text.find('\r') != std::string::npos) return 0;
        // The presenter provides a local path from the authoritative thumbnail
        // service. Attribute assignment never parses supplied text as markup.
        element->SetAttribute("src", text);
    } else if (document_id == g_home_id && g_model) g_model->SetText(name, text);
    else {
        auto* element = document->element->GetElementById(name);
        if (!element) return 0;
        element->SetInnerRML(Rml::StringUtilities::EncodeRml(text));
    }
    document->texts[name] = text;
    DirtyVisual();
    return 1;
}
PP_EXPORT int pp_rmlui_document_set_bool(uint64_t document_id, const char* name, int value)
{
    auto* document = GetDocument(document_id);
    if (!document || !name) return 0;
    const bool boolean = value != 0;
    const auto previous = document->booleans.find(name);
    if (previous != document->booleans.end() && previous->second == boolean) return 1;
    const std::string binding(name);
    if (binding.rfind("disabled:", 0) == 0) {
        auto* element = document->element->GetElementById(binding.substr(9));
        if (!element) return 0;
        if (boolean) element->SetAttribute("disabled", "disabled");
        else element->RemoveAttribute("disabled");
        element->SetClass("unavailable", boolean);
    } else if (binding.rfind("class:", 0) == 0) {
        const size_t separator = binding.find(':', 6);
        if (separator == std::string::npos || separator + 1 == binding.size()) return 0;
        const std::string id = binding.substr(6, separator - 6);
        auto* element = id == "@document" ? document->element : document->element->GetElementById(id);
        if (!element) return 0;
        element->SetClass(binding.substr(separator + 1), boolean);
    } else if (binding.rfind("visible:", 0) == 0) {
        auto* element = document->element->GetElementById(binding.substr(8));
        if (!element) return 0;
        if (boolean) element->RemoveProperty("display");
        else element->SetProperty("display", "none");
    } else if (document_id == g_home_id && g_model) g_model->SetBool(name, boolean);
    else {
        auto* element = document->element->GetElementById(name);
        if (!element) return 0;
        if (boolean) element->RemoveProperty("display");
        else element->SetProperty("display", "none");
    }
    document->booleans[name] = boolean;
    DirtyVisual();
    return 1;
}
PP_EXPORT int pp_rmlui_document_set_field(uint64_t document_id, const char* element_id, const char* value)
{
    auto* document = GetDocument(document_id);
    if (!document || !element_id) return 0;
    auto* element = TextField(document->element->GetElementById(element_id));
    if (!element && document_id == g_home_id && g_model) { g_model->SetInputText(element_id, value ? value : ""); DirtyVisual(); return 1; }
    if (!element) return 0;
    const Rml::String text = value ? value : "";
    const Rml::String previous = element->GetValue();
    bool changed = previous != text;
    if (changed && element->GetTagName() == "input" && element->GetAttribute<Rml::String>("type", "") == "range") {
        // RmlUi formats range values with six decimals. Compare numeric values
        // so an unchanged integer presenter binding does not dirty every frame.
        float previous_number = 0.f, next_number = 0.f;
        if (ReadFiniteNumbers(previous, &previous_number, 1) && ReadFiniteNumbers(text, &next_number, 1))
            changed = previous_number != next_number;
    }
    if (changed) { ProgrammaticFieldMutation binding; element->SetValue(text); DirtyVisual(); }
    return 1;
}
PP_EXPORT int pp_rmlui_document_read_field(uint64_t document_id, const char* element_id, unsigned char* buffer, int capacity)
{
    auto* document = GetDocument(document_id);
    if (!document || !element_id || !buffer || capacity <= 1) return 0;
    auto* element = TextField(document->element->GetElementById(element_id));
    return element ? CopyUtf8(element->GetValue(), buffer, capacity) : 0;
}
PP_EXPORT int pp_rmlui_document_reload(uint64_t document_id)
{
#if !defined(NDEBUG)
    auto* document = GetDocument(document_id);
    if (!document || document_id == g_home_id) return 0;
    auto* replacement = g_context->LoadDocument((g_asset_root / document->relative_path).string());
    if (!replacement) return 0;
    document->element->RemoveEventListener("click", &g_action_listener);
    document->element->RemoveEventListener("change", &g_action_listener);
    document->element->RemoveEventListener("mouseover", &g_action_listener);
    document->element->RemoveEventListener("focus", &g_action_listener, true);
    document->element->Close();
    document->element = replacement;
    g_accessibility.Forget(document_id);
    document->texts.clear(); document->booleans.clear();
    replacement->AddEventListener("click", &g_action_listener);
    replacement->AddEventListener("change", &g_action_listener);
    replacement->AddEventListener("mouseover", &g_action_listener);
    replacement->AddEventListener("focus", &g_action_listener, true);
    replacement->Show(document->layer == 1 ? Rml::ModalFlag::Modal : Rml::ModalFlag::None);
    g_actions.erase(std::remove_if(g_actions.begin(), g_actions.end(), [document_id](const QueuedAction& action) {
        return action.intent.document_id == document_id;
    }), g_actions.end());
    DirtyVisual(); EnsureUpdated(); return 1;
#else
    (void)document_id; return 0;
#endif
}
PP_EXPORT int pp_rmlui_document_count() { return OwnerThread() ? int(g_documents.size()) : 0; }
PP_EXPORT int pp_rmlui_focused_element(unsigned char* buffer, int capacity)
{
    if (!OwnerThread() || !g_context) return 0;
    auto* focused = g_context->GetFocusElement();
    return focused && !dynamic_cast<Rml::ElementDocument*>(focused) ? CopyUtf8(focused->GetId(), buffer, capacity) : 0;
}
PP_EXPORT int pp_rmlui_text_input_active()
{
    if (!OwnerThread() || !g_context) return 0;
    auto* focused = g_context->GetFocusElement();
    if (!focused) return 0;
    if (focused->GetTagName() == "textarea") return 1;
    if (focused->GetTagName() != "input") return 0;
    const auto type = focused->GetAttribute<Rml::String>("type", "text");
    return type == "text" || type == "password" || type == "number" ? 1 : 0;
}
PP_EXPORT void pp_rmlui_focus_lost()
{
    if (!OwnerThread() || !g_context) return;
    DirtyVisual(); g_pointer_known = false; g_pointer_buttons = 0;
    g_context->ProcessMouseLeave();
    for (int button = 0; button < 3; ++button) g_context->ProcessMouseButtonUp(button, 0);
    for (int code = 1; code <= 57; ++code) {
        const auto key = ConvertKey(code);
        if (key != Rml::Input::KI_UNKNOWN) g_context->ProcessKeyUp(key, 0);
    }
    if (auto* focused = g_context->GetFocusElement()) {
        if (auto* document = focused->GetOwnerDocument()) document->Focus();
        else focused->Blur();
    }
}
PP_EXPORT int pp_rmlui_draw_command_count() { return OwnerThread() && g_renderer ? g_renderer->CommandCount() : 0; }
PP_EXPORT int pp_rmlui_draw_command(int index, PrimeDrawCommand* command)
{
    return OwnerThread() && g_renderer && command && command->size == sizeof(PrimeDrawCommand) && g_renderer->ReadCommand(index, *command) ? 1 : 0;
}
PP_EXPORT int pp_rmlui_draw_geometry(uint64_t handle, PrimeDrawGeometry* geometry)
{
    return OwnerThread() && g_renderer && geometry && geometry->size == sizeof(PrimeDrawGeometry) && g_renderer->ReadGeometry(handle, *geometry) ? 1 : 0;
}
PP_EXPORT int pp_rmlui_draw_texture_count() { return OwnerThread() && g_renderer ? g_renderer->TextureCount() : 0; }
PP_EXPORT int pp_rmlui_draw_texture(int index, PrimeDrawTexture* texture)
{
    return OwnerThread() && g_renderer && texture && texture->size == sizeof(PrimeDrawTexture) && g_renderer->ReadTexture(index, *texture) ? 1 : 0;
}
PP_EXPORT uint32_t pp_rmlui_draw_features() { return OwnerThread() && g_renderer ? g_renderer->UnsupportedFeatures() : 0; }

PP_EXPORT void pp_rmlui_set_clipboard(const char* text)
{
    if (OwnerThread()) g_system.SetClipboardText(text ? text : "");
}
PP_EXPORT int pp_rmlui_read_clipboard(unsigned char* buffer, int capacity)
{
    if (!OwnerThread()) return 0;
    Rml::String text; g_system.GetClipboardText(text);
    return CopyUtf8(text, buffer, capacity);
}

PP_EXPORT int pp_rmlui_draw_geometry_count() { return OwnerThread() && g_renderer ? g_renderer->GeometryCount() : 0; }

PP_EXPORT int pp_rmlui_document_set_enabled(uint64_t document_id, const char* element_id, int enabled)
{
    auto* document = GetDocument(document_id);
    if (!document || !element_id) return 0;
    auto* element = document->element->GetElementById(element_id);
    if (!element) return 0;
    if (enabled) element->RemoveAttribute("disabled");
    else element->SetAttribute("disabled", "disabled");
    element->SetClass("unavailable", enabled == 0);
    DirtyVisual();
    return 1;
}

PP_EXPORT int pp_rmlui_document_accessibility_snapshot(uint64_t document_id, unsigned char* buffer, int capacity)
{
    auto* document = GetDocument(document_id);
    if (!document || !document->element->IsVisible()) return 0;
    EnsureUpdated();
    return CopyUtf8(g_accessibility.Snapshot(document->element, g_context->GetFocusElement(), g_generation, document_id), buffer, capacity);
}
PP_EXPORT int pp_rmlui_accessibility_action(uint64_t generation, uint64_t document_id, uint64_t revision, const char* node_key, int action)
{
    auto* document = GetDocument(document_id);
    if (!document || generation != g_generation || action < 0 || action > 3 || !ForegroundDocument(document->element)) return 0;
    EnsureUpdated();
    auto* element = g_accessibility.Resolve(document->element, g_context->GetFocusElement(), generation, document_id, revision, node_key);
    if (!element) return 0;
    const int mask = g_accessibility.ActionMask(document_id, node_key);
    if (action == 0) {
        if (!(mask & PrimeAccessibilityTree::Focus) || !element->Focus()) return 0;
        ScrollFocusedElement(element);
    } else if (action == 1) {
        if (!(mask & PrimeAccessibilityTree::Press)) return 0;
        element->Click();
    } else {
        if (!(mask & PrimeAccessibilityTree::Scroll)) return 0;
        const float distance = std::max(1.f, element->GetClientHeight() * .8f);
        element->SetScrollTop(element->GetScrollTop() + (action == 2 ? distance : -distance));
    }
    DirtyVisual();
    return 1;
}
PP_EXPORT int pp_rmlui_accessibility_set_text(uint64_t generation, uint64_t document_id, uint64_t revision, const char* node_key, const char* value)
{
    auto* document = GetDocument(document_id);
    if (!document || generation != g_generation || !value || !ForegroundDocument(document->element)) return 0;
    EnsureUpdated();
    auto* element = g_accessibility.Resolve(document->element, g_context->GetFocusElement(), generation, document_id, revision, node_key);
    if (!element || !(g_accessibility.ActionMask(document_id, node_key) & PrimeAccessibilityTree::SetText)) return 0;
    auto* field = TextField(element);
    if (!field || element->HasAttribute("readonly")) return 0;
    Rml::String text(value);
    if (text.size() > 128 * 1024) return 0;
    const int maximum = element->GetAttribute<int>("maxlength", -1);
    if (maximum >= 0) {
        const int length = static_cast<int>(Rml::StringUtilities::LengthUTF8(text));
        text.resize(size_t(Rml::StringUtilities::ConvertCharacterOffsetToByteOffset(text, std::min(length, maximum))));
    }
    g_text_input.Cancel();
    // Accessibility is one user edit. Range SetValue already emits change;
    // silence that binding side effect before the single explicit DOM event.
    { ProgrammaticFieldMutation binding; field->SetValue(text); }
    Rml::Dictionary parameters; parameters["value"] = text;
    element->DispatchEvent("change", parameters);
    DirtyVisual();
    return 1;
}

PP_EXPORT int pp_rmlui_text_input_state(PrimeTextInputState* state)
{
    return OwnerThread() && state && g_text_input.State(*state) ? 1 : 0;
}
PP_EXPORT int pp_rmlui_text_selection_utf16(uint64_t generation, uint64_t document_id, uint64_t focus_epoch, int* start, int* end)
{
    if (!OwnerThread() || !start || !end) return 0;
    PrimeTextInputState state;
    if (!g_text_input.State(state) || state.generation != generation || state.document_id != document_id || state.focus_epoch != focus_epoch) return 0;
    auto* field = dynamic_cast<Rml::ElementFormControl*>(g_context->GetFocusElement());
    if (!field) return 0;
    const Rml::String value = field->GetValue();
    auto offset = [&value](int scalar) {
        const int limit = Rml::StringUtilities::ConvertCharacterOffsetToByteOffset(value, std::max(0, scalar));
        int units = 0;
        for (int index = 0; index < limit && size_t(index) < value.size(); ++index) {
            const unsigned char byte = static_cast<unsigned char>(value[size_t(index)]);
            if ((byte & 0xc0) == 0x80) continue;
            units += byte >= 0xf0 && byte <= 0xf4 ? 2 : 1;
        }
        return units;
    };
    *start = offset(state.selection_start); *end = offset(state.selection_end); return 1;
}
PP_EXPORT int pp_rmlui_composition(uint64_t generation, uint64_t document_id, uint64_t focus_epoch,
    int stage, const char* text, int cursor, int selection_length)
{
    if (!OwnerThread() || !g_text_input.Compose(generation, document_id, focus_epoch, stage, text, cursor, selection_length)) return 0;
    DirtyVisual(); return 1;
}
PP_EXPORT int pp_rmlui_hovered_element(unsigned char* buffer, int capacity)
{
    if (!OwnerThread() || !g_context) return 0;
    auto* element = g_context->GetHoverElement();
    while (element && element->GetId().empty()) element = element->GetParentNode();
    return element ? CopyUtf8(element->GetId(), buffer, capacity) : 0;
}
