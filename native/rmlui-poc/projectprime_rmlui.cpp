#include <RmlUi/Core.h>
#include <RmlUi/Core/Input.h>
#include <RmlUi/Core/StringUtilities.h>
#include "RmlUi_Renderer_GL2.h"

#include <algorithm>
#include <chrono>
#include <cstring>
#include <deque>
#include <filesystem>
#include <memory>
#include <string>
#include <vector>

#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#include <GL/gl.h>
#elif defined(__APPLE__)
#include <OpenGL/gl.h>
#else
#include <GL/gl.h>
#endif

#if defined(_WIN32)
#define PP_EXPORT extern "C" __declspec(dllexport)
#else
#define PP_EXPORT extern "C" __attribute__((visibility("default")))
#endif

PP_EXPORT void pp_rmlui_shutdown();

namespace {

class PrimeSystemInterface final : public Rml::SystemInterface {
public:
    PrimeSystemInterface() : started(std::chrono::steady_clock::now()) {}

    double GetElapsedTime() override
    {
        return std::chrono::duration<double>(std::chrono::steady_clock::now() - started).count();
    }

private:
    std::chrono::steady_clock::time_point started;
};

struct SocialRow {
    Rml::String prime_id;
    Rml::String name;
    Rml::String activity;
    Rml::String detail;
    Rml::String relation;
    bool online = false;
    bool friend_online = false;
};

struct PrimeMenuData {
    Rml::String player_name = "PLAYER";
    Rml::String profile_state = "LOCAL PROFILE";
    Rml::String hunter_name = "SAMUS";
    Rml::String game_data_state = "GAME DATA UNKNOWN";
    Rml::String build_version = "local";
    Rml::String renderer_name = "OPENGL // RMLUI 6.3";
    Rml::String ui_cost = "RMLUI DIRECT GPU OVERLAY // MEASURING";
    bool reduce_motion = false;
    bool diagnostics_visible = false;

    int activity_index = 0;
    Rml::String activity_title = "QUICK PLAY";
    Rml::String activity_description = "Find the best compatible public hunt and deploy immediately.";
    Rml::String activity_hint = "MATCHMAKING ENTRY";
    Rml::String activity_action = "DEPLOY";

    bool social_open = false;
    bool social_loading = false;
    bool social_context_open = false;
    int social_tab = 0;
    Rml::String social_search;
    Rml::String social_status = "CONNECTING";
    Rml::String social_badge;
    Rml::String social_online_count = "0 ONLINE";
    Rml::String social_friend_count = "0 ONLINE";
    Rml::String social_request_count = "0 REQUESTS";
    std::vector<SocialRow> social_rows;
    std::vector<SocialRow> home_friends;

    Rml::String selected_prime_id;
    Rml::String selected_name;
    Rml::String selected_activity;
    Rml::String selected_detail;
    Rml::String selected_relation;
    bool selected_can_add = false;
    bool selected_can_accept = false;
    bool selected_can_decline = false;
    bool selected_can_cancel = false;
    bool selected_can_remove = false;
    bool selected_can_block = false;
    bool selected_can_unblock = false;
};

class PrimeMenuModel {
public:
    bool Setup(Rml::Context* context)
    {
        Rml::DataModelConstructor model = context->CreateDataModel("prime_menu");
        if (!model)
            return false;

        model.Bind("player_name", &data.player_name);
        model.Bind("profile_state", &data.profile_state);
        model.Bind("hunter_name", &data.hunter_name);
        model.Bind("game_data_state", &data.game_data_state);
        model.Bind("build_version", &data.build_version);
        model.Bind("renderer_name", &data.renderer_name);
        model.Bind("ui_cost", &data.ui_cost);
        model.Bind("reduce_motion", &data.reduce_motion);
        model.Bind("diagnostics_visible", &data.diagnostics_visible);
        model.Bind("activity_index", &data.activity_index);
        model.Bind("activity_title", &data.activity_title);
        model.Bind("activity_description", &data.activity_description);
        model.Bind("activity_hint", &data.activity_hint);
        model.Bind("activity_action", &data.activity_action);

        if (auto row = model.RegisterStruct<SocialRow>()) {
            row.RegisterMember("prime_id", &SocialRow::prime_id);
            row.RegisterMember("name", &SocialRow::name);
            row.RegisterMember("activity", &SocialRow::activity);
            row.RegisterMember("detail", &SocialRow::detail);
            row.RegisterMember("relation", &SocialRow::relation);
            row.RegisterMember("online", &SocialRow::online);
            row.RegisterMember("friend_online", &SocialRow::friend_online);
        }
        model.RegisterArray<std::vector<SocialRow>>();
        model.Bind("social_open", &data.social_open);
        model.Bind("social_loading", &data.social_loading);
        model.Bind("social_context_open", &data.social_context_open);
        model.Bind("social_tab", &data.social_tab);
        model.Bind("social_search", &data.social_search);
        model.Bind("social_status", &data.social_status);
        model.Bind("social_badge", &data.social_badge);
        model.Bind("social_online_count", &data.social_online_count);
        model.Bind("social_friend_count", &data.social_friend_count);
        model.Bind("social_request_count", &data.social_request_count);
        model.Bind("social_rows", &data.social_rows);
        model.Bind("home_friends", &data.home_friends);
        model.Bind("selected_prime_id", &data.selected_prime_id);
        model.Bind("selected_name", &data.selected_name);
        model.Bind("selected_activity", &data.selected_activity);
        model.Bind("selected_detail", &data.selected_detail);
        model.Bind("selected_relation", &data.selected_relation);
        model.Bind("selected_can_add", &data.selected_can_add);
        model.Bind("selected_can_accept", &data.selected_can_accept);
        model.Bind("selected_can_decline", &data.selected_can_decline);
        model.Bind("selected_can_cancel", &data.selected_can_cancel);
        model.Bind("selected_can_remove", &data.selected_can_remove);
        model.Bind("selected_can_block", &data.selected_can_block);
        model.Bind("selected_can_unblock", &data.selected_can_unblock);

        model.BindEventCallback("select_quick", &PrimeMenuModel::SelectQuick, this);
        model.BindEventCallback("select_browser", &PrimeMenuModel::SelectBrowser, this);
        model.BindEventCallback("select_offline", &PrimeMenuModel::SelectOffline, this);
        model.BindEventCallback("select_adventure", &PrimeMenuModel::SelectAdventure, this);
        model.BindEventCallback("deploy", &PrimeMenuModel::Deploy, this);
        model.BindEventCallback("nav_hunters", &PrimeMenuModel::OpenHunters, this);
        model.BindEventCallback("nav_community", &PrimeMenuModel::OpenCommunity, this);
        model.BindEventCallback("nav_studio", &PrimeMenuModel::OpenStudio, this);
        model.BindEventCallback("open_profile", &PrimeMenuModel::OpenProfile, this);
        model.BindEventCallback("open_settings", &PrimeMenuModel::OpenSettings, this);
        model.BindEventCallback("open_classic", &PrimeMenuModel::OpenClassic, this);
        model.BindEventCallback("quit_game", &PrimeMenuModel::Quit, this);
        model.BindEventCallback("open_social", &PrimeMenuModel::OpenSocial, this);
        model.BindEventCallback("close_social", &PrimeMenuModel::CloseSocial, this);
        model.BindEventCallback("social_friends", &PrimeMenuModel::SocialFriends, this);
        model.BindEventCallback("social_players", &PrimeMenuModel::SocialPlayers, this);
        model.BindEventCallback("social_requests", &PrimeMenuModel::SocialRequests, this);
        model.BindEventCallback("social_blocks", &PrimeMenuModel::SocialBlocks, this);
        model.BindEventCallback("social_refresh", &PrimeMenuModel::SocialRefresh, this);
        model.BindEventCallback("social_filter", &PrimeMenuModel::SocialFilter, this);
        model.BindEventCallback("social_select", &PrimeMenuModel::SocialSelect, this);
        model.BindEventCallback("social_context_close", &PrimeMenuModel::SocialContextClose, this);
        model.BindEventCallback("social_add", &PrimeMenuModel::SocialAdd, this);
        model.BindEventCallback("social_accept", &PrimeMenuModel::SocialAccept, this);
        model.BindEventCallback("social_decline", &PrimeMenuModel::SocialDecline, this);
        model.BindEventCallback("social_cancel", &PrimeMenuModel::SocialCancel, this);
        model.BindEventCallback("social_remove", &PrimeMenuModel::SocialRemove, this);
        model.BindEventCallback("social_block", &PrimeMenuModel::SocialBlock, this);
        model.BindEventCallback("social_unblock", &PrimeMenuModel::SocialUnblock, this);
        model.BindEventCallback("noop", &PrimeMenuModel::Noop, this);

        handle = model.GetModelHandle();
        return true;
    }

    void SetText(const std::string& name, const std::string& value)
    {
        if (name == "player_name") data.player_name = value;
        else if (name == "profile_state") data.profile_state = value;
        else if (name == "hunter_name") data.hunter_name = value;
        else if (name == "game_data_state") data.game_data_state = value;
        else if (name == "build_version") data.build_version = value;
        else if (name == "renderer_name") data.renderer_name = value;
        else if (name == "ui_cost") data.ui_cost = value;
        else if (name == "social_search") data.social_search = value;
        else if (name == "social_status") data.social_status = value;
        else if (name == "social_badge") data.social_badge = value;
        else if (name == "social_online_count") data.social_online_count = value;
        else if (name == "social_friend_count") data.social_friend_count = value;
        else if (name == "social_request_count") data.social_request_count = value;
        else return;
        handle.DirtyVariable(name);
    }

    void SetBool(const std::string& name, bool value)
    {
        if (name == "reduce_motion") data.reduce_motion = value;
        else if (name == "diagnostics_visible") data.diagnostics_visible = value;
        else if (name == "social_open") data.social_open = value;
        else if (name == "social_loading") data.social_loading = value;
        else if (name == "social_context_open") data.social_context_open = value;
        else return;
        handle.DirtyVariable(name);
    }

    bool TakeAction(std::string& result)
    {
        if (actions.empty()) return false;
        result = std::move(actions.front());
        actions.pop_front();
        return true;
    }

    void ClearSocial()
    {
        data.social_rows.clear();
        data.home_friends.clear();
    }

    void AddSocialRow(const SocialRow& row) { data.social_rows.push_back(row); }
    void AddHomeFriend(const SocialRow& row) { data.home_friends.push_back(row); }

    void CommitSocial()
    {
        handle.DirtyVariable("social_rows");
        handle.DirtyVariable("home_friends");
    }

    bool Back()
    {
        if (data.social_context_open) {
            data.social_context_open = false;
            handle.DirtyVariable("social_context_open");
            return true;
        }
        if (data.social_open) {
            data.social_open = false;
            handle.DirtyVariable("social_open");
            Emit("social:close");
            return true;
        }
        return false;
    }

private:
    PrimeMenuData data;
    Rml::DataModelHandle handle;
    std::deque<std::string> actions;

    void Emit(const char* action) { actions.emplace_back(action); }

    void SetActivity(int index, const char* title, const char* description,
        const char* hint, const char* action)
    {
        data.activity_index = index;
        data.activity_title = title;
        data.activity_description = description;
        data.activity_hint = hint;
        data.activity_action = action;
        handle.DirtyVariable("activity_index");
        handle.DirtyVariable("activity_title");
        handle.DirtyVariable("activity_description");
        handle.DirtyVariable("activity_hint");
        handle.DirtyVariable("activity_action");
    }

    void SelectQuick(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        SetActivity(0, "QUICK PLAY",
            "Find the best compatible public hunt and deploy immediately.",
            "MATCHMAKING ENTRY", "DEPLOY");
        Emit("stage:quick");
    }

    void SelectBrowser(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        SetActivity(1, "SERVER BROWSER",
            "Browse live lobbies, inspect arena and rules, then choose your hunt.",
            "LIVE DIRECTORY", "BROWSE SERVERS");
        Emit("stage:browser");
    }

    void SelectOffline(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        SetActivity(2, "OFFLINE BATTLE",
            "Configure bots, arena, rules and hunter loadout without going online.",
            "LOCAL SESSION", "CONFIGURE MATCH");
        Emit("stage:offline");
    }

    void SelectAdventure(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        SetActivity(3, "ADVENTURE",
            "Continue an existing save or begin a new solo campaign.",
            "SAVE DATA", "CONTINUE");
        Emit("stage:adventure");
    }

    void Deploy(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        switch (data.activity_index) {
        case 0: Emit("route:play"); break;
        case 1: Emit("route:play"); break;
        case 2: Emit("route:offline"); break;
        case 3: Emit("route:offline"); break;
        default: break;
        }
    }

    void OpenHunters(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("route:hunter"); }
    void OpenCommunity(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("route:forge"); }
    void OpenStudio(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("route:theatre"); }
    void OpenProfile(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("route:hunter"); }
    void OpenSettings(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("route:settings"); }
    void OpenClassic(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("route:news"); }
    void Quit(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("quit"); }

    void OpenSocial(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        data.social_open = true;
        data.social_context_open = false;
        handle.DirtyVariable("social_open");
        handle.DirtyVariable("social_context_open");
        Emit("social:open");
    }

    void CloseSocial(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        data.social_open = false;
        data.social_context_open = false;
        handle.DirtyVariable("social_open");
        handle.DirtyVariable("social_context_open");
        Emit("social:close");
    }

    void SelectSocialTab(int tab, const char* action)
    {
        data.social_tab = tab;
        data.social_context_open = false;
        handle.DirtyVariable("social_tab");
        handle.DirtyVariable("social_context_open");
        Emit(action);
    }

    void SocialFriends(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
        { SelectSocialTab(0, "social:tab:0"); }
    void SocialPlayers(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
        { SelectSocialTab(1, "social:tab:1"); }
    void SocialRequests(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
        { SelectSocialTab(2, "social:tab:2"); }
    void SocialBlocks(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
        { SelectSocialTab(3, "social:tab:3"); }
    void SocialRefresh(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
        { Emit("social:refresh"); }

    void SocialFilter(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        if (data.social_search.size() > 48)
            data.social_search.resize(48);
        handle.DirtyVariable("social_search");
        Emit((std::string("social:search:") + data.social_search).c_str());
    }

    const SocialRow* FindRow(const Rml::String& prime_id) const
    {
        for (const SocialRow& row : data.social_rows)
            if (row.prime_id == prime_id) return &row;
        return nullptr;
    }

    void SocialSelect(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList& arguments)
    {
        if (arguments.empty()) return;
        const Rml::String prime_id = arguments[0].Get<Rml::String>();
        const SocialRow* row = FindRow(prime_id);
        if (!row) return;

        data.selected_prime_id = row->prime_id;
        data.selected_name = row->name;
        data.selected_activity = row->activity;
        data.selected_detail = row->detail;
        data.selected_relation = row->relation;
        data.selected_can_add = row->relation == "PLAYER";
        data.selected_can_accept = row->relation == "INCOMING";
        data.selected_can_decline = row->relation == "INCOMING";
        data.selected_can_cancel = row->relation == "OUTGOING";
        data.selected_can_remove = row->relation == "FRIEND";
        data.selected_can_block = row->relation != "BLOCKED";
        data.selected_can_unblock = row->relation == "BLOCKED";
        data.social_context_open = true;

        handle.DirtyVariable("selected_prime_id");
        handle.DirtyVariable("selected_name");
        handle.DirtyVariable("selected_activity");
        handle.DirtyVariable("selected_detail");
        handle.DirtyVariable("selected_relation");
        handle.DirtyVariable("selected_can_add");
        handle.DirtyVariable("selected_can_accept");
        handle.DirtyVariable("selected_can_decline");
        handle.DirtyVariable("selected_can_cancel");
        handle.DirtyVariable("selected_can_remove");
        handle.DirtyVariable("selected_can_block");
        handle.DirtyVariable("selected_can_unblock");
        handle.DirtyVariable("social_context_open");
        Emit((std::string("social:context:") + data.selected_prime_id).c_str());
    }

    void SocialContextClose(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        data.social_context_open = false;
        handle.DirtyVariable("social_context_open");
        Emit("social:context-close");
    }

    void EmitSelected(const char* action)
    {
        if (data.selected_prime_id.empty()) return;
        actions.emplace_back(std::string("social:") + action + ":" + data.selected_prime_id);
        data.social_context_open = false;
        handle.DirtyVariable("social_context_open");
    }

    void SocialAdd(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { EmitSelected("add"); }
    void SocialAccept(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { EmitSelected("accept"); }
    void SocialDecline(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { EmitSelected("decline"); }
    void SocialCancel(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { EmitSelected("cancel"); }
    void SocialRemove(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { EmitSelected("remove"); }
    void SocialBlock(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { EmitSelected("block"); }
    void SocialUnblock(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { EmitSelected("unblock"); }
    void Noop(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) {}
};

PrimeSystemInterface g_system;
std::unique_ptr<RenderInterface_GL2> g_renderer;
Rml::Context* g_context = nullptr;
Rml::ElementDocument* g_document = nullptr;
std::unique_ptr<PrimeMenuModel> g_model;
bool g_initialized = false;

int ConvertModifiers(int modifiers)
{
    int result = 0;
    if (modifiers & 1) result |= Rml::Input::KM_SHIFT;
    if (modifiers & 2) result |= Rml::Input::KM_CTRL;
    if (modifiers & 4) result |= Rml::Input::KM_ALT;
    if (modifiers & 8) result |= Rml::Input::KM_META;
    return result;
}

Rml::Input::KeyIdentifier ConvertKey(int key)
{
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
    default: return Rml::Input::KI_UNKNOWN;
    }
}

std::filesystem::path RootPath(const char* root, const char* child)
{
    return std::filesystem::path(root ? root : "") / child;
}

void RestoreGlState()
{
    glDepthMask(GL_TRUE);
    glDisableClientState(GL_VERTEX_ARRAY);
    glDisableClientState(GL_COLOR_ARRAY);
    glDisableClientState(GL_TEXTURE_COORD_ARRAY);
    glDisable(GL_SCISSOR_TEST);
    glDisable(GL_STENCIL_TEST);
    glDisable(GL_TEXTURE_2D);
    glBindTexture(GL_TEXTURE_2D, 0);
    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
    glEnable(GL_DEPTH_TEST);
    glMatrixMode(GL_MODELVIEW);
}

} // namespace

PP_EXPORT int pp_rmlui_initialize(int width, int height, float density, const char* asset_root)
{
    if (g_initialized) return 1;
    if (!asset_root || width <= 0 || height <= 0) return 0;

    g_renderer = std::make_unique<RenderInterface_GL2>();
    g_renderer->SetViewport(width, height);
    Rml::SetSystemInterface(&g_system);
    Rml::SetRenderInterface(g_renderer.get());

    if (!Rml::Initialise()) return 0;
    g_initialized = true;

    const std::string regular = RootPath(asset_root, "fonts/Rajdhani-SemiBold.ttf").string();
    const std::string bold = RootPath(asset_root, "fonts/Rajdhani-Bold.ttf").string();
    const std::string mono = RootPath(asset_root, "fonts/JetBrainsMono-Regular.ttf").string();
    if (!Rml::LoadFontFace(regular) || !Rml::LoadFontFace(bold) || !Rml::LoadFontFace(mono)) {
        pp_rmlui_shutdown();
        return 0;
    }

    g_context = Rml::CreateContext("project-prime-rmlui-poc", {width, height});
    if (!g_context) {
        pp_rmlui_shutdown();
        return 0;
    }
    g_context->SetDensityIndependentPixelRatio(std::max(density, 1.0f));

    g_model = std::make_unique<PrimeMenuModel>();
    if (!g_model->Setup(g_context)) {
        pp_rmlui_shutdown();
        return 0;
    }

    const std::string document_path = RootPath(asset_root, "prime_home.rml").string();
    g_document = g_context->LoadDocument(document_path);
    if (!g_document) {
        pp_rmlui_shutdown();
        return 0;
    }
    g_document->Show();
    if (Rml::Element* first = g_document->GetElementById("mode_quick")) first->Focus();
    return 1;
}

PP_EXPORT void pp_rmlui_shutdown()
{
    if (!g_initialized && !g_renderer) return;
    if (g_document) {
        g_document->Close();
        g_document = nullptr;
    }
    g_model.reset();
    if (g_context) {
        Rml::RemoveContext("project-prime-rmlui-poc");
        g_context = nullptr;
    }
    if (g_initialized) Rml::Shutdown();
    g_initialized = false;
    Rml::SetRenderInterface(nullptr);
    Rml::SetSystemInterface(nullptr);
    g_renderer.reset();
}

PP_EXPORT void pp_rmlui_update()
{
    if (g_context) g_context->Update();
}

PP_EXPORT void pp_rmlui_render(int width, int height)
{
    if (!g_context || !g_renderer || width <= 0 || height <= 0) return;

    // Project Prime renders the cinematic ground and Hunter first. The sample
    // backend normally owns an empty framebuffer, while this bridge is an
    // overlay in somebody else's frame, so establish only the state RmlUi
    // needs and never clear color.
#if !defined(_WIN32)
    glActiveTexture(GL_TEXTURE0);
#endif
    glDisable(GL_DEPTH_TEST);
    glDepthMask(GL_FALSE);
    glDisable(GL_CULL_FACE);
    glDisable(GL_ALPHA_TEST);

    g_renderer->SetViewport(width, height);
    g_renderer->BeginFrame();
    glClearStencil(0);
    glClear(GL_STENCIL_BUFFER_BIT);
    g_context->Render();
    g_renderer->EndFrame();
    RestoreGlState();
}

PP_EXPORT void pp_rmlui_resize(int width, int height, float density)
{
    if (!g_context || !g_renderer || width <= 0 || height <= 0) return;
    g_renderer->SetViewport(width, height);
    g_context->SetDimensions({width, height});
    g_context->SetDensityIndependentPixelRatio(std::max(density, 1.0f));
}

PP_EXPORT int pp_rmlui_mouse_move(int x, int y, int modifiers)
{
    return g_context ? (g_context->ProcessMouseMove(x, y, ConvertModifiers(modifiers)) ? 1 : 0) : 0;
}

PP_EXPORT int pp_rmlui_mouse_button(int button, int down, int modifiers)
{
    if (!g_context) return 0;
    const bool propagated = down
        ? g_context->ProcessMouseButtonDown(button, ConvertModifiers(modifiers))
        : g_context->ProcessMouseButtonUp(button, ConvertModifiers(modifiers));
    return propagated ? 1 : 0;
}

PP_EXPORT int pp_rmlui_mouse_wheel(float delta_y, int modifiers)
{
    return g_context ? (g_context->ProcessMouseWheel(-delta_y, ConvertModifiers(modifiers)) ? 1 : 0) : 0;
}

PP_EXPORT int pp_rmlui_key(int key, int down, int modifiers)
{
    if (!g_context) return 0;
    const Rml::Input::KeyIdentifier converted = ConvertKey(key);
    if (converted == Rml::Input::KI_UNKNOWN) return 0;
    const bool propagated = down
        ? g_context->ProcessKeyDown(converted, ConvertModifiers(modifiers))
        : g_context->ProcessKeyUp(converted, ConvertModifiers(modifiers));
    return propagated ? 1 : 0;
}

PP_EXPORT int pp_rmlui_text(unsigned int codepoint)
{
    return g_context ? (g_context->ProcessTextInput(static_cast<Rml::Character>(codepoint)) ? 1 : 0) : 0;
}

PP_EXPORT void pp_rmlui_set_text(const char* name, const char* value)
{
    if (g_model && name) g_model->SetText(name, value ? value : "");
}

PP_EXPORT void pp_rmlui_set_bool(const char* name, int value)
{
    if (g_model && name) g_model->SetBool(name, value != 0);
}

PP_EXPORT void pp_rmlui_social_clear()
{
    if (g_model) g_model->ClearSocial();
}

PP_EXPORT void pp_rmlui_social_add_row(const char* prime_id, const char* name,
    const char* activity, const char* detail, const char* relation, int online, int friend_online)
{
    if (!g_model) return;
    SocialRow row;
    row.prime_id = prime_id ? prime_id : "";
    row.name = name ? name : "";
    row.activity = activity ? activity : "";
    row.detail = detail ? detail : "";
    row.relation = relation ? relation : "";
    row.online = online != 0;
    row.friend_online = friend_online != 0;
    g_model->AddSocialRow(row);
}

PP_EXPORT void pp_rmlui_social_add_home_friend(const char* prime_id, const char* name,
    const char* activity, const char* detail)
{
    if (!g_model) return;
    SocialRow row;
    row.prime_id = prime_id ? prime_id : "";
    row.name = name ? name : "";
    row.activity = activity ? activity : "";
    row.detail = detail ? detail : "";
    row.relation = "FRIEND";
    row.online = true;
    row.friend_online = true;
    g_model->AddHomeFriend(row);
}

PP_EXPORT void pp_rmlui_social_commit()
{
    if (g_model) g_model->CommitSocial();
}

PP_EXPORT int pp_rmlui_back()
{
    if (!g_model || !g_model->Back()) return 0;
    if (g_document) {
        if (Rml::Element* social = g_document->GetElementById("social"))
            social->Focus();
    }
    return 1;
}

PP_EXPORT int pp_rmlui_focus(const char* id)
{
    if (!g_document || !id) return 0;
    if (Rml::Element* element = g_document->GetElementById(id)) {
        element->Focus();
        return 1;
    }
    return 0;
}

PP_EXPORT int pp_rmlui_take_action(unsigned char* buffer, int capacity)
{
    if (!g_model || !buffer || capacity <= 1) return 0;
    std::string action;
    if (!g_model->TakeAction(action)) return 0;
    const int length = std::min(static_cast<int>(action.size()), capacity - 1);
    std::memcpy(buffer, action.data(), static_cast<size_t>(length));
    buffer[length] = 0;
    return length;
}
