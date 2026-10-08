#include <RmlUi/Core.h>
#include <RmlUi/Core/Input.h>
#include <RmlUi/Core/StringUtilities.h>
#include <RmlUi/Core/Elements/ElementFormControlInput.h>
#include "projectprime_rmlui_renderer.h"
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

namespace {

void FocusElement(const char* id);
void QueueHomeAction(const std::string& action);
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
private:
    Rml::String clipboard;
    std::chrono::steady_clock::time_point started;
};

#include "projectprime_rmlui_menu.h"

PrimeSystemInterface g_system;
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
        {"lobby:rules-mode", PrimeIntentKind::LobbyRulesMode, 0}, {"lobby:rules-format", PrimeIntentKind::LobbyRulesFormat, 0}
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
    return false;
}
void QueueAction(const std::string& action, uint64_t document)
{
    auto* source = GetDocument(document);
    if (!source) return;
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
        for (auto* element = event.GetTargetElement(); element; element = element->GetParentNode()) {
            const Rml::String action = element->GetAttribute<Rml::String>("data-action", "");
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
    element->AddEventListener("click", &g_action_listener);
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


void PositionLobbyNameplates()
{
    if (!g_context || !g_document || !g_lobby_anchor_dirty) return;
    Rml::Element* stage = g_document->GetElementById("lobby_stage");
    if (!stage) return;

    const Rml::Vector2f stageOrigin = stage->GetAbsoluteOffset(Rml::BoxArea::Border);
    const Rml::Vector2i dimensions = g_context->GetDimensions();
    // On 720p the foreground local pad is close to the action strip. Keep
    // the player label above that strip without inventing per-DPI RCSS offsets.
    // RmlUi's GetAbsoluteOffset is non-const even for layout queries.
    Rml::Element* actions = g_document->GetElementById("lobby_actions");
    const float actionTop = actions
        ? actions->GetAbsoluteOffset(Rml::BoxArea::Border).y
        : float(dimensions.y);
    for (int index = 0; index < 8; ++index) {
        const std::string id = "lobby_slot" + std::to_string(index);
        Rml::Element* label = g_document->GetElementById(id);
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
        element->Focus();
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
    g_context = Rml::CreateContext("project-prime-rmlui", {width, height});
    if (!g_context) { pp_rmlui_shutdown(); return 0; }
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
    // Documents and their model listeners are released while both the RmlUi core
    // and render adapter still exist. RemoveContext flushes deferred Close().
    for (auto& entry : g_documents) {
        entry.second.element->RemoveEventListener("click", &g_action_listener);
        entry.second.element->Close();
    }
    g_documents.clear(); g_document_order.clear(); g_actions.clear();
    g_document = nullptr; g_home_id = 0;
    if (g_context) {
        Rml::RemoveContext("project-prime-rmlui"); g_context = nullptr;
    }
    g_model.reset();
    g_lobby_anchor_dirty = true; g_lobby_anchors = {};
    if (g_initialized) Rml::Shutdown();
    g_initialized = false;
    Rml::SetRenderInterface(nullptr); Rml::SetSystemInterface(nullptr);
    g_renderer.reset(); g_asset_root.clear(); g_system.ClearClipboard();
    ++g_generation; // Invalidates worker completions even before a reinit.
}

PP_EXPORT void pp_rmlui_update()
{
    if (!OwnerThread() || !g_context) return;
    g_context->Update();
    PositionLobbyNameplates();
    if (g_model) g_model->ApplyPendingFocus();
}

PP_EXPORT void pp_rmlui_render(int width, int height)
{
    if (!OwnerThread() || !g_context || !g_renderer || width <= 0 || height <= 0) return;

    g_renderer->SetViewport(width, height);
    g_renderer->BeginFrame();
    g_context->Render();
    g_renderer->EndFrame();
}

PP_EXPORT void pp_rmlui_resize(int width, int height, float density)
{
    if (!OwnerThread() || !g_context || !g_renderer || width <= 0 || height <= 0) return;
    g_renderer->SetViewport(width, height);
    g_context->SetDimensions({width, height});
    if (std::isfinite(density) && density > 0) g_context->SetDensityIndependentPixelRatio(std::max(density, 1.0f));
    g_lobby_anchor_dirty = true;
}

PP_EXPORT void pp_rmlui_set_lobby_anchor(int slot, float center_x, float center_y)
{
    if (!OwnerThread() || slot < 0 || slot >= int(g_lobby_anchors.size()) || !std::isfinite(center_x) || !std::isfinite(center_y)) return;
    g_lobby_anchors[slot] = {center_x, center_y};
    g_lobby_anchor_dirty = true;
}

PP_EXPORT int pp_rmlui_mouse_move(int x, int y, int modifiers)
{
    return OwnerThread() && g_context ? (g_context->ProcessMouseMove(x, y, ConvertModifiers(modifiers)) ? 1 : 0) : 0;
}

PP_EXPORT int pp_rmlui_mouse_button(int button, int down, int modifiers)
{
    if (!OwnerThread() || !g_context) return 0;
    const bool propagated = down
        ? g_context->ProcessMouseButtonDown(button, ConvertModifiers(modifiers))
        : g_context->ProcessMouseButtonUp(button, ConvertModifiers(modifiers));
    return propagated ? 1 : 0;
}

PP_EXPORT int pp_rmlui_mouse_wheel(float delta_y, int modifiers)
{
    return OwnerThread() && g_context ? (g_context->ProcessMouseWheel(-delta_y, ConvertModifiers(modifiers)) ? 1 : 0) : 0;
}

PP_EXPORT int pp_rmlui_key(int key, int down, int modifiers)
{
    if (!OwnerThread() || !g_context) return 0;
    const Rml::Input::KeyIdentifier converted = ConvertKey(key);
    if (converted == Rml::Input::KI_UNKNOWN) return 0;
    const bool propagated = down
        ? g_context->ProcessKeyDown(converted, ConvertModifiers(modifiers))
        : g_context->ProcessKeyUp(converted, ConvertModifiers(modifiers));
    return propagated ? 1 : 0;
}

PP_EXPORT int pp_rmlui_text(unsigned int codepoint)
{
    if (codepoint == 0 || codepoint > 0x10ffff || (codepoint >= 0xd800 && codepoint <= 0xdfff)) return 0;
    return OwnerThread() && g_context ? (g_context->ProcessTextInput(static_cast<Rml::Character>(codepoint)) ? 1 : 0) : 0;
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
PP_EXPORT int pp_rmlui_element_bounds(const char* id, float* x, float* y,
    float* width, float* height)
{
    if (!OwnerThread() || !g_context || !g_document || !id || !x || !y || !width || !height)
        return 0;
    g_context->Update();
    Rml::Element* element = FindElementById(id);
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

// Bounded POC diagnostic for real native-input regression at each density.
// This reads only the one shipped STUDIO control; no action is synthesized.
PP_EXPORT int pp_rmlui_studio_bounds(float* x, float* y, float* width, float* height)
{
    if (!OwnerThread() || !g_context || !g_document || !x || !y || !width || !height) return 0;
    g_context->Update();
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
    document->element->Close();
    g_documents.erase(document_id);
    g_document_order.erase(std::remove(g_document_order.begin(), g_document_order.end(), document_id), g_document_order.end());
    g_actions.erase(std::remove_if(g_actions.begin(), g_actions.end(), [document_id](const QueuedAction& action) {
        return action.intent.document_id == document_id;
    }), g_actions.end());
    g_context->Update(); // Flush the document's deferred detach/release on owner thread.
    if (auto* restore = GetDocument(restore_document)) {
        restore->element->Show(restore->layer == 1 ? Rml::ModalFlag::Modal : Rml::ModalFlag::None, Rml::FocusFlag::Keep);
        if (!restore_element.empty()) pp_rmlui_document_focus(restore_document, restore_element.c_str());
    } else if (auto* home = GetDocument(g_home_id)) {
        home->element->Show(Rml::ModalFlag::None, Rml::FocusFlag::Keep);
    }
    return 1;
}
PP_EXPORT int pp_rmlui_document_show(uint64_t document_id, int show)
{
    auto* document = GetDocument(document_id);
    if (!document) return 0;
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
    g_context->Update();
    auto* element = document->element->GetElementById(element_id);
    return element && element->IsVisible(true) && !element->HasAttribute("disabled") && element->Focus() ? 1 : 0;
}
PP_EXPORT int pp_rmlui_document_set_text(uint64_t document_id, const char* name, const char* value)
{
    auto* document = GetDocument(document_id);
    if (!document || !name) return 0;
    const std::string text = value ? value : "";
    const auto previous = document->texts.find(name);
    if (previous != document->texts.end() && previous->second == text) return 1;
    if (document_id == g_home_id && g_model) g_model->SetText(name, text);
    else {
        auto* element = document->element->GetElementById(name);
        if (!element) return 0;
        element->SetInnerRML(Rml::StringUtilities::EncodeRml(text));
    }
    document->texts[name] = text;
    return 1;
}
PP_EXPORT int pp_rmlui_document_set_bool(uint64_t document_id, const char* name, int value)
{
    auto* document = GetDocument(document_id);
    if (!document || !name) return 0;
    const bool boolean = value != 0;
    const auto previous = document->booleans.find(name);
    if (previous != document->booleans.end() && previous->second == boolean) return 1;
    if (document_id == g_home_id && g_model) g_model->SetBool(name, boolean);
    else {
        auto* element = document->element->GetElementById(name);
        if (!element) return 0;
        if (boolean) element->RemoveProperty("display");
        else element->SetProperty("display", "none");
    }
    document->booleans[name] = boolean;
    return 1;
}
PP_EXPORT int pp_rmlui_document_set_field(uint64_t document_id, const char* element_id, const char* value)
{
    auto* document = GetDocument(document_id);
    if (!document || !element_id) return 0;
    if (document_id == g_home_id && g_model) { g_model->SetInputText(element_id, value ? value : ""); return 1; }
    auto* element = dynamic_cast<Rml::ElementFormControlInput*>(document->element->GetElementById(element_id));
    if (!element) return 0;
    element->SetValue(value ? value : ""); return 1;
}
PP_EXPORT int pp_rmlui_document_read_field(uint64_t document_id, const char* element_id, unsigned char* buffer, int capacity)
{
    auto* document = GetDocument(document_id);
    if (!document || !element_id || !buffer || capacity <= 1) return 0;
    auto* element = dynamic_cast<Rml::ElementFormControlInput*>(document->element->GetElementById(element_id));
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
    document->element->Close();
    document->element = replacement;
    document->texts.clear(); document->booleans.clear();
    replacement->AddEventListener("click", &g_action_listener);
    replacement->Show(document->layer == 1 ? Rml::ModalFlag::Modal : Rml::ModalFlag::None);
    g_actions.erase(std::remove_if(g_actions.begin(), g_actions.end(), [document_id](const QueuedAction& action) {
        return action.intent.document_id == document_id;
    }), g_actions.end());
    g_context->Update(); return 1;
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
