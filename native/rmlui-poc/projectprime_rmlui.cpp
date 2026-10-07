#include <RmlUi/Core.h>
#include <RmlUi/Core/Input.h>
#include <RmlUi/Core/StringUtilities.h>
#include <RmlUi/Core/Elements/ElementFormControlInput.h>
#include "RmlUi_Renderer_GL2.h"

#include <algorithm>
#include <array>
#include <chrono>
#include <cstring>
#include <deque>
#include <filesystem>
#include <memory>
#include <string>
#include <unordered_map>

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

void FocusElement(const char* id);
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

private:
    std::chrono::steady_clock::time_point started;
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
    bool activity_selector_open = false;
    bool home_mode = true;
    bool lobby_mode = false;
    bool lobby_owner = false;
    bool lobby_local_ready = false;
    bool lobby_starting = false;
    bool multiplayer_mode = false;
    bool play_create_mode = false;
    bool play_browser_mode = false;
    bool play_busy = false;
    bool play_no_servers = true;
    Rml::String play_status = "CONTACTING DIRECTORY";
    Rml::String play_create_map = "WAITING FOR MAPS";
    Rml::String play_create_mode_name = "BATTLE";
    Rml::String play_create_host = "HOSTED // ONLINE";
    Rml::String play_server_count = "0 LIVE";
    std::array<Rml::String, 8> play_server_name{};
    std::array<Rml::String, 8> play_server_details{};
    std::array<bool, 8> play_server_present{};

    Rml::String lobby_name = "MULTIPLAYER LOBBY";
    Rml::String lobby_map = "WAITING FOR MAP";
    Rml::String lobby_mode_name = "BATTLE";
    Rml::String lobby_format = "FREE FOR ALL";
    Rml::String lobby_player_count = "1 / 8";
    Rml::String lobby_ready_count = "0 READY";
    Rml::String lobby_status = "WAITING FOR PLAYERS";
    Rml::String lobby_ready_action = "READY";
    Rml::String lobby_local_hunter = "SAMUS";
    std::array<Rml::String, 8> slot_name{};
    std::array<Rml::String, 8> slot_hunter{};
    std::array<Rml::String, 8> slot_state{};
    std::array<bool, 8> slot_occupied{};
    std::array<bool, 8> slot_ready{};
    std::array<bool, 8> slot_local{};

    int activity_index = 0;
    Rml::String activity_group = "MULTIPLAYER";
    Rml::String activity_title = "QUICK PLAY";
    Rml::String activity_description = "Find the best compatible public hunt.";
    Rml::String activity_hint = "PUBLIC MATCHMAKING";
    Rml::String activity_action = "DEPLOY";
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
        model.Bind("activity_selector_open", &data.activity_selector_open);
        model.Bind("home_mode", &data.home_mode);
        model.Bind("lobby_mode", &data.lobby_mode);
        model.Bind("lobby_owner", &data.lobby_owner);
        model.Bind("lobby_local_ready", &data.lobby_local_ready);
        model.Bind("lobby_starting", &data.lobby_starting);
        model.Bind("lobby_name", &data.lobby_name);
        model.Bind("lobby_map", &data.lobby_map);
        model.Bind("lobby_mode_name", &data.lobby_mode_name);
        model.Bind("lobby_format", &data.lobby_format);
        model.Bind("lobby_player_count", &data.lobby_player_count);
        model.Bind("lobby_ready_count", &data.lobby_ready_count);
        model.Bind("lobby_status", &data.lobby_status);
        model.Bind("lobby_ready_action", &data.lobby_ready_action);
        model.Bind("lobby_local_hunter", &data.lobby_local_hunter);
        model.Bind("multiplayer_mode", &data.multiplayer_mode);
        model.Bind("play_create_mode", &data.play_create_mode);
        model.Bind("play_browser_mode", &data.play_browser_mode);
        model.Bind("play_busy", &data.play_busy);
        model.Bind("play_no_servers", &data.play_no_servers);
        model.Bind("play_status", &data.play_status);
        model.Bind("play_create_map", &data.play_create_map);
        model.Bind("play_create_mode_name", &data.play_create_mode_name);
        model.Bind("play_create_host", &data.play_create_host);
        model.Bind("play_server_count", &data.play_server_count);
        model.Bind("play_server0_present", &data.play_server_present[0]);
        model.Bind("play_server0_name", &data.play_server_name[0]);
        model.Bind("play_server0_details", &data.play_server_details[0]);
        model.Bind("play_server1_present", &data.play_server_present[1]);
        model.Bind("play_server1_name", &data.play_server_name[1]);
        model.Bind("play_server1_details", &data.play_server_details[1]);
        model.Bind("play_server2_present", &data.play_server_present[2]);
        model.Bind("play_server2_name", &data.play_server_name[2]);
        model.Bind("play_server2_details", &data.play_server_details[2]);
        model.Bind("play_server3_present", &data.play_server_present[3]);
        model.Bind("play_server3_name", &data.play_server_name[3]);
        model.Bind("play_server3_details", &data.play_server_details[3]);
        model.Bind("play_server4_present", &data.play_server_present[4]);
        model.Bind("play_server4_name", &data.play_server_name[4]);
        model.Bind("play_server4_details", &data.play_server_details[4]);
        model.Bind("play_server5_present", &data.play_server_present[5]);
        model.Bind("play_server5_name", &data.play_server_name[5]);
        model.Bind("play_server5_details", &data.play_server_details[5]);
        model.Bind("play_server6_present", &data.play_server_present[6]);
        model.Bind("play_server6_name", &data.play_server_name[6]);
        model.Bind("play_server6_details", &data.play_server_details[6]);
        model.Bind("play_server7_present", &data.play_server_present[7]);
        model.Bind("play_server7_name", &data.play_server_name[7]);
        model.Bind("play_server7_details", &data.play_server_details[7]);
        model.Bind("slot0_name", &data.slot_name[0]);
        model.Bind("slot0_hunter", &data.slot_hunter[0]);
        model.Bind("slot0_state", &data.slot_state[0]);
        model.Bind("slot0_occupied", &data.slot_occupied[0]);
        model.Bind("slot0_ready", &data.slot_ready[0]);
        model.Bind("slot0_local", &data.slot_local[0]);
        model.Bind("slot1_name", &data.slot_name[1]);
        model.Bind("slot1_hunter", &data.slot_hunter[1]);
        model.Bind("slot1_state", &data.slot_state[1]);
        model.Bind("slot1_occupied", &data.slot_occupied[1]);
        model.Bind("slot1_ready", &data.slot_ready[1]);
        model.Bind("slot1_local", &data.slot_local[1]);
        model.Bind("slot2_name", &data.slot_name[2]);
        model.Bind("slot2_hunter", &data.slot_hunter[2]);
        model.Bind("slot2_state", &data.slot_state[2]);
        model.Bind("slot2_occupied", &data.slot_occupied[2]);
        model.Bind("slot2_ready", &data.slot_ready[2]);
        model.Bind("slot2_local", &data.slot_local[2]);
        model.Bind("slot3_name", &data.slot_name[3]);
        model.Bind("slot3_hunter", &data.slot_hunter[3]);
        model.Bind("slot3_state", &data.slot_state[3]);
        model.Bind("slot3_occupied", &data.slot_occupied[3]);
        model.Bind("slot3_ready", &data.slot_ready[3]);
        model.Bind("slot3_local", &data.slot_local[3]);
        model.Bind("slot4_name", &data.slot_name[4]);
        model.Bind("slot4_hunter", &data.slot_hunter[4]);
        model.Bind("slot4_state", &data.slot_state[4]);
        model.Bind("slot4_occupied", &data.slot_occupied[4]);
        model.Bind("slot4_ready", &data.slot_ready[4]);
        model.Bind("slot4_local", &data.slot_local[4]);
        model.Bind("slot5_name", &data.slot_name[5]);
        model.Bind("slot5_hunter", &data.slot_hunter[5]);
        model.Bind("slot5_state", &data.slot_state[5]);
        model.Bind("slot5_occupied", &data.slot_occupied[5]);
        model.Bind("slot5_ready", &data.slot_ready[5]);
        model.Bind("slot5_local", &data.slot_local[5]);
        model.Bind("slot6_name", &data.slot_name[6]);
        model.Bind("slot6_hunter", &data.slot_hunter[6]);
        model.Bind("slot6_state", &data.slot_state[6]);
        model.Bind("slot6_occupied", &data.slot_occupied[6]);
        model.Bind("slot6_ready", &data.slot_ready[6]);
        model.Bind("slot6_local", &data.slot_local[6]);
        model.Bind("slot7_name", &data.slot_name[7]);
        model.Bind("slot7_hunter", &data.slot_hunter[7]);
        model.Bind("slot7_state", &data.slot_state[7]);
        model.Bind("slot7_occupied", &data.slot_occupied[7]);
        model.Bind("slot7_ready", &data.slot_ready[7]);
        model.Bind("slot7_local", &data.slot_local[7]);
        model.Bind("activity_index", &data.activity_index);
        model.Bind("activity_group", &data.activity_group);
        model.Bind("activity_title", &data.activity_title);
        model.Bind("activity_description", &data.activity_description);
        model.Bind("activity_hint", &data.activity_hint);
        model.Bind("activity_action", &data.activity_action);

        model.BindEventCallback("toggle_activity_selector", &PrimeMenuModel::ToggleActivitySelector, this);
        model.BindEventCallback("close_activity_selector", &PrimeMenuModel::CloseActivitySelector, this);
        model.BindEventCallback("preview_quick", &PrimeMenuModel::PreviewQuick, this);
        model.BindEventCallback("preview_browser", &PrimeMenuModel::PreviewBrowser, this);
        model.BindEventCallback("preview_offline", &PrimeMenuModel::PreviewOffline, this);
        model.BindEventCallback("preview_adventure", &PrimeMenuModel::PreviewAdventure, this);
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
        model.BindEventCallback("lobby_ready", &PrimeMenuModel::LobbyReady, this);
        model.BindEventCallback("lobby_start", &PrimeMenuModel::LobbyStart, this);
        model.BindEventCallback("lobby_leave", &PrimeMenuModel::LobbyLeave, this);
        model.BindEventCallback("lobby_next_hunter", &PrimeMenuModel::LobbyNextHunter, this);
        model.BindEventCallback("lobby_next_suit", &PrimeMenuModel::LobbyNextSuit, this);
        model.BindEventCallback("lobby_classic", &PrimeMenuModel::LobbyClassic, this);
        model.BindEventCallback("play_quick", &PrimeMenuModel::PlayQuick, this);
        model.BindEventCallback("play_browse", &PrimeMenuModel::PlayBrowse, this);
        model.BindEventCallback("play_create_open", &PrimeMenuModel::PlayCreateOpen, this);
        model.BindEventCallback("play_create_submit", &PrimeMenuModel::PlayCreateSubmit, this);
        model.BindEventCallback("play_join", &PrimeMenuModel::PlayJoin, this);
        model.BindEventCallback("play_back", &PrimeMenuModel::PlayBack, this);
        model.BindEventCallback("play_next_map", &PrimeMenuModel::PlayNextMap, this);
        model.BindEventCallback("play_next_mode", &PrimeMenuModel::PlayNextMode, this);
        model.BindEventCallback("play_toggle_host", &PrimeMenuModel::PlayToggleHost, this);
        model.BindEventCallback("play_server0", &PrimeMenuModel::PlayServer0, this);
        model.BindEventCallback("play_server1", &PrimeMenuModel::PlayServer1, this);
        model.BindEventCallback("play_server2", &PrimeMenuModel::PlayServer2, this);
        model.BindEventCallback("play_server3", &PrimeMenuModel::PlayServer3, this);
        model.BindEventCallback("play_server4", &PrimeMenuModel::PlayServer4, this);
        model.BindEventCallback("play_server5", &PrimeMenuModel::PlayServer5, this);
        model.BindEventCallback("play_server6", &PrimeMenuModel::PlayServer6, this);
        model.BindEventCallback("play_server7", &PrimeMenuModel::PlayServer7, this);
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
        else if (name == "lobby_name") data.lobby_name = value;
        else if (name == "lobby_map") data.lobby_map = value;
        else if (name == "lobby_mode_name") data.lobby_mode_name = value;
        else if (name == "lobby_format") data.lobby_format = value;
        else if (name == "lobby_player_count") data.lobby_player_count = value;
        else if (name == "lobby_ready_count") data.lobby_ready_count = value;
        else if (name == "lobby_status") data.lobby_status = value;
        else if (name == "lobby_ready_action") data.lobby_ready_action = value;
        else if (name == "lobby_local_hunter") data.lobby_local_hunter = value;
        else if (name == "play_status") data.play_status = value;
        else if (name == "play_create_map") data.play_create_map = value;
        else if (name == "play_create_mode_name") data.play_create_mode_name = value;
        else if (name == "play_create_host") data.play_create_host = value;
        else if (name == "play_server_count") data.play_server_count = value;
        else {
            for (int i = 0; i < 8; ++i) {
                const std::string prefix = "slot" + std::to_string(i) + "_";
                if (name == prefix + "name") data.slot_name[i] = value;
                else if (name == prefix + "hunter") data.slot_hunter[i] = value;
                else if (name == prefix + "state") data.slot_state[i] = value;
                else if (name == "play_server" + std::to_string(i) + "_name")
                    data.play_server_name[i] = value;
                else if (name == "play_server" + std::to_string(i) + "_details")
                    data.play_server_details[i] = value;
                else continue;
                handle.DirtyVariable(name);
                return;
            }
            return;
        }
        handle.DirtyVariable(name);
    }

    void SetBool(const std::string& name, bool value)
    {
        if (name == "reduce_motion") data.reduce_motion = value;
        else if (name == "diagnostics_visible") data.diagnostics_visible = value;
        else if (name == "activity_selector_open") data.activity_selector_open = value;
        else if (name == "lobby_mode") {
            if (data.lobby_mode == value) return;
            data.lobby_mode = value;
            data.home_mode = !value;
            if (value) {
                data.multiplayer_mode = false;
                data.play_create_mode = false;
                data.play_browser_mode = false;
                handle.DirtyVariable("multiplayer_mode");
                handle.DirtyVariable("play_create_mode");
                handle.DirtyVariable("play_browser_mode");
            }
            g_lobby_anchor_dirty = true;
            handle.DirtyVariable("lobby_mode");
            handle.DirtyVariable("home_mode");
            RequestFocus(value ? "lobby_ready" : "activity_selector");
            return;
        }
        else if (name == "home_mode") data.home_mode = value;
        else if (name == "lobby_owner") data.lobby_owner = value;
        else if (name == "lobby_local_ready") data.lobby_local_ready = value;
        else if (name == "lobby_starting") data.lobby_starting = value;
        else if (name == "multiplayer_mode") {
            data.multiplayer_mode = value;
            data.home_mode = !value && !data.lobby_mode;
            handle.DirtyVariable("multiplayer_mode");
            handle.DirtyVariable("home_mode");
            return;
        }
        else if (name == "play_create_mode") data.play_create_mode = value;
        else if (name == "play_browser_mode") data.play_browser_mode = value;
        else if (name == "play_busy") data.play_busy = value;
        else if (name == "play_no_servers") data.play_no_servers = value;
        else {
            for (int i = 0; i < 8; ++i) {
                const std::string prefix = "slot" + std::to_string(i) + "_";
                if (name == prefix + "occupied") {
                    if (data.slot_occupied[i] != value)
                        g_lobby_anchor_dirty = true;
                    data.slot_occupied[i] = value;
                }
                else if (name == prefix + "ready") data.slot_ready[i] = value;
                else if (name == prefix + "local") data.slot_local[i] = value;
                else if (name == "play_server" + std::to_string(i) + "_present")
                {
                    data.play_server_present[i] = value;
                    handle.DirtyVariable(name);
                    return;
                }
                else continue;
                handle.DirtyVariable(name);
                return;
            }
            return;
        }
        handle.DirtyVariable(name);
    }

    bool TakeAction(std::string& result)
    {
        if (actions.empty()) return false;
        result = std::move(actions.front());
        actions.pop_front();
        return true;
    }

    bool Back()
    {
        if (data.lobby_mode) {
            Emit("lobby:leave");
            return true;
        }
        if (data.multiplayer_mode) {
            if (data.play_create_mode) {
                data.play_create_mode = false;
                data.play_browser_mode = true;
                handle.DirtyVariable("play_create_mode");
                handle.DirtyVariable("play_browser_mode");
                Emit("play:browse");
                RequestFocus("play_quick");
            } else {
                data.multiplayer_mode = false;
                data.home_mode = true;
                data.play_browser_mode = false;
                handle.DirtyVariable("multiplayer_mode");
                handle.DirtyVariable("home_mode");
                handle.DirtyVariable("play_browser_mode");
                Emit("play:cancel");
                RequestFocus("activity_selector");
            }
            return true;
        }
        if (!data.activity_selector_open)
            return false;
        SetSelectorOpen(false);
        EmitStage(data.activity_index, false);
        RequestFocus("activity_selector");
        return true;
    }

    void SetInputText(const std::string& id, const std::string& value)
    {
        pending_inputs[id] = value;
    }

    void ApplyPendingFocus()
    {
        // Inputs are conditional data-if branches. Defer their initial values
        // until after the multiplayer DOM is materialized by Context::Update.
        for (auto it = pending_inputs.begin(); it != pending_inputs.end();) {
            Rml::Element* element = FindElementById(it->first.c_str());
            if (auto* input = dynamic_cast<Rml::ElementFormControlInput*>(element)) {
                input->SetValue(it->second);
                it = pending_inputs.erase(it);
            } else ++it;
        }
        if (pending_focus.empty())
            return;
        std::string id = std::move(pending_focus);
        pending_focus.clear();
        FocusElement(id.c_str());
    }

private:
    PrimeMenuData data;
    Rml::DataModelHandle handle;
    std::deque<std::string> actions;
    std::string pending_focus;
    std::unordered_map<std::string, std::string> pending_inputs;

    void Emit(const char* action) { actions.emplace_back(action); }
    void RequestFocus(const char* id) { pending_focus = id ? id : ""; }

    static const char* StageName(int index)
    {
        switch (index) {
        case 0: return "quick";
        case 1: return "browser";
        case 2: return "offline";
        case 3: return "adventure";
        default: return "quick";
        }
    }

    void EmitStage(int index, bool preview)
    {
        actions.emplace_back(std::string(preview ? "stage-preview:" : "stage:")
            + StageName(index));
    }

    const char* ActivityElement(int index) const
    {
        switch (index) {
        case 0: return "drawer_quick";
        case 1: return "drawer_browser";
        case 2: return "drawer_offline";
        case 3: return "drawer_adventure";
        default: return "drawer_quick";
        }
    }

    void SetSelectorOpen(bool open)
    {
        if (data.activity_selector_open == open)
            return;
        data.activity_selector_open = open;
        handle.DirtyVariable("activity_selector_open");
    }

    void SetActivity(int index, const char* group, const char* title,
        const char* description, const char* hint, const char* action)
    {
        data.activity_index = index;
        data.activity_group = group;
        data.activity_title = title;
        data.activity_description = description;
        data.activity_hint = hint;
        data.activity_action = action;
        handle.DirtyVariable("activity_index");
        handle.DirtyVariable("activity_group");
        handle.DirtyVariable("activity_title");
        handle.DirtyVariable("activity_description");
        handle.DirtyVariable("activity_hint");
        handle.DirtyVariable("activity_action");
    }

    void ToggleActivitySelector(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        const bool opening = !data.activity_selector_open;
        SetSelectorOpen(opening);
        if (opening) {
            // The drawer becomes display:block on the next data-model update.
            // Defer focus until after that update so gamepad focus cannot land
            // on an element that is still display:none this frame.
            RequestFocus(ActivityElement(data.activity_index));
        }
        else {
            EmitStage(data.activity_index, false);
            RequestFocus("activity_selector");
        }
    }

    void CloseActivitySelector(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        if (!data.activity_selector_open) return;
        SetSelectorOpen(false);
        EmitStage(data.activity_index, false);
        RequestFocus("activity_selector");
    }

    void PreviewQuick(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { EmitStage(0, true); }
    void PreviewBrowser(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { EmitStage(1, true); }
    void PreviewOffline(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { EmitStage(2, true); }
    void PreviewAdventure(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { EmitStage(3, true); }

    void SelectQuick(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        SetActivity(0, "MULTIPLAYER", "QUICK PLAY",
            "Find the best compatible public hunt.",
            "PUBLIC MATCHMAKING", "DEPLOY");
        SetSelectorOpen(false);
        EmitStage(0, false);
        RequestFocus("activity_selector");
    }

    void SelectBrowser(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        SetActivity(1, "MULTIPLAYER", "SERVER BROWSER",
            "Browse live public and private sessions.",
            "LIVE DIRECTORY", "BROWSE SERVERS");
        SetSelectorOpen(false);
        EmitStage(1, false);
        RequestFocus("activity_selector");
    }

    void SelectOffline(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        SetActivity(2, "LOCAL PLAY", "OFFLINE BATTLE",
            "Bots, training and custom rules.",
            "LOCAL SESSION", "CONFIGURE MATCH");
        SetSelectorOpen(false);
        EmitStage(2, false);
        RequestFocus("activity_selector");
    }

    void SelectAdventure(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        SetActivity(3, "SOLO", "ADVENTURE",
            "Continue or load a solo save.",
            "SAVE DATA", "CONTINUE");
        SetSelectorOpen(false);
        EmitStage(3, false);
        RequestFocus("activity_selector");
    }

    void Deploy(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        switch (data.activity_index) {
        case 0: OpenMultiplayer(true); break;
        case 1: OpenMultiplayer(false); break;
        case 2: Emit("route:offline"); break;
        case 3: Emit("route:offline"); break;
        default: break;
        }
    }

    void OpenMultiplayer(bool quick)
    {
        if (data.lobby_mode || data.multiplayer_mode) return;
        data.multiplayer_mode = true;
        data.home_mode = false;
        data.play_create_mode = false;
        data.play_browser_mode = true;
        SetSelectorOpen(false);
        handle.DirtyVariable("home_mode");
        handle.DirtyVariable("multiplayer_mode");
        handle.DirtyVariable("play_browser_mode");
        handle.DirtyVariable("play_create_mode");
        Emit(quick ? "play:quick" : "play:browse");
        RequestFocus("play_quick");
    }
    void PlayQuick(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Emit("play:quick"); }
    void PlayBrowse(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Emit("play:browse"); }
    void PlayCreateOpen(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        data.play_create_mode = true;
        data.play_browser_mode = false;
        handle.DirtyVariable("play_create_mode");
        handle.DirtyVariable("play_browser_mode");
        Emit("play:create-open");
        RequestFocus("play_create_name");
    }
    void PlayCreateSubmit(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Emit("play:create"); }
    void PlayJoin(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Emit("play:join"); }
    void PlayBack(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Back(); }
    void PlayNextMap(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Emit("play:next-map"); }
    void PlayNextMode(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Emit("play:next-mode"); }
    void PlayToggleHost(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Emit("play:toggle-host"); }
    void PlayServer0(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Emit("play:server:0"); }
    void PlayServer1(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Emit("play:server:1"); }
    void PlayServer2(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Emit("play:server:2"); }
    void PlayServer3(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Emit("play:server:3"); }
    void PlayServer4(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Emit("play:server:4"); }
    void PlayServer5(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Emit("play:server:5"); }
    void PlayServer6(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Emit("play:server:6"); }
    void PlayServer7(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { Emit("play:server:7"); }

    void OpenHunters(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (!data.lobby_mode && !data.multiplayer_mode) Emit("route:hunter"); }
    void OpenCommunity(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (!data.lobby_mode && !data.multiplayer_mode) Emit("route:forge"); }
    void OpenStudio(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (!data.lobby_mode && !data.multiplayer_mode) Emit("studio:open"); }
    void OpenProfile(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (!data.lobby_mode && !data.multiplayer_mode) Emit("route:hunter"); }
    void OpenSettings(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (!data.lobby_mode && !data.multiplayer_mode) Emit("route:settings"); }
    void OpenClassic(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (!data.lobby_mode && !data.multiplayer_mode) Emit("route:news"); }
    void Quit(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (!data.lobby_mode && !data.multiplayer_mode) Emit("quit"); }
    void LobbyReady(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("lobby:ready"); }
    void LobbyStart(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("lobby:start"); }
    void LobbyLeave(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("lobby:leave"); }
    void LobbyNextHunter(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("lobby:next-hunter"); }
    void LobbyNextSuit(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("lobby:next-suit"); }
    void LobbyClassic(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("lobby:classic"); }
    void Noop(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) {}
};

PrimeSystemInterface g_system;
std::unique_ptr<RenderInterface_GL2> g_renderer;
Rml::Context* g_context = nullptr;
Rml::ElementDocument* g_document = nullptr;
std::unique_ptr<PrimeMenuModel> g_model;
bool g_initialized = false;

void PositionLobbyNameplates()
{
    if (!g_context || !g_document || !g_lobby_anchor_dirty) return;
    Rml::Element* stage = g_document->GetElementById("lobby_stage");
    if (!stage) return;

    const Rml::Vector2f stageOrigin = stage->GetAbsoluteOffset(Rml::BoxArea::Border);
    const Rml::Vector2i dimensions = g_context->GetDimensions();
    for (int index = 0; index < 8; ++index) {
        const std::string id = "lobby_slot" + std::to_string(index);
        Rml::Element* label = g_document->GetElementById(id);
        if (!label) continue;
        const Rml::Vector2f size = label->GetBox().GetSize(Rml::BoxArea::Border);
        const float left = g_lobby_anchors[index].x * float(dimensions.x)
            - stageOrigin.x - size.x * 0.5f;
        const float top = g_lobby_anchors[index].y * float(dimensions.y)
            - stageOrigin.y - size.y * 0.5f;
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
    if (Rml::Element* first = g_document->GetElementById("activity_selector")) first->Focus();
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
    g_lobby_anchor_dirty = true;
    g_lobby_anchors = {};
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
    if (!g_context) return;
    g_context->Update();
    PositionLobbyNameplates();
    if (g_model) g_model->ApplyPendingFocus();
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
    g_lobby_anchor_dirty = true;
}

PP_EXPORT void pp_rmlui_set_lobby_anchor(int slot, float center_x, float center_y)
{
    if (slot < 0 || slot >= int(g_lobby_anchors.size())) return;
    g_lobby_anchors[slot] = {center_x, center_y};
    g_lobby_anchor_dirty = true;
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

PP_EXPORT void pp_rmlui_set_field(const char* id, const char* value)
{
    if (g_model && id)
        g_model->SetInputText(id, value ? value : "");
}

PP_EXPORT int pp_rmlui_read_field(const char* id, unsigned char* buffer, int capacity)
{
    if (!id || !buffer || capacity <= 1) return 0;
    auto* input = dynamic_cast<Rml::ElementFormControlInput*>(FindElementById(id));
    if (!input) return 0;
    const std::string value = input->GetValue();
    const int length = std::min(int(value.size()), capacity - 1);
    std::memcpy(buffer, value.data(), size_t(length));
    buffer[length] = 0;
    return length;
}

PP_EXPORT int pp_rmlui_back()
{
    return g_model && g_model->Back() ? 1 : 0;
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

// Actual RmlUi element bounds are used by the native regression. This does
// not synthesize an action or bypass the DOM's focus/click dispatch.
PP_EXPORT int pp_rmlui_element_bounds(const char* id, float* x, float* y,
    float* width, float* height)
{
    if (!g_context || !g_document || !id || !x || !y || !width || !height)
        return 0;
    g_context->Update();
    Rml::Element* element = FindElementById(id);
    if (!element) return 0;
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
    if (!g_context || !g_document || !x || !y || !width || !height) return 0;
    g_context->Update();
    Rml::Element* studio = g_document->GetElementById("nav_studio");
    if (!studio) return 0;
    const auto offset = studio->GetAbsoluteOffset(Rml::BoxArea::Border);
    const auto size = studio->GetBox().GetSize(Rml::BoxArea::Border);
    if (size.x <= 0 || size.y <= 0) return 0;
    *x = offset.x; *y = offset.y; *width = size.x; *height = size.y;
    return 1;
}
