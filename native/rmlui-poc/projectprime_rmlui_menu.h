#pragma once

// Private compatibility presenter; included inside the runtime namespace.
// It owns no RmlUi context, renderer, network session, or document lifetime.
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
    bool lobby_require_ready = false;
    bool lobby_starting = false;
    bool lobby_rules_open = false;
    bool rules_owner = false;
    bool rules_dirty = false;
    bool rules_pending = false;
    Rml::String rules_map = "WAITING FOR ARENA";
    Rml::String rules_mode = "BATTLE";
    Rml::String rules_format = "FFA";
    Rml::String rules_goal_label = "SCORE GOAL";
    Rml::String rules_status = "READING SERVER RULES";
    std::array<Rml::String, 16> rules_toggles{};
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
        model.Bind("lobby_require_ready", &data.lobby_require_ready);
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
        model.Bind("lobby_rules_open", &data.lobby_rules_open);
        model.Bind("rules_owner", &data.rules_owner);
        model.Bind("rules_dirty", &data.rules_dirty);
        model.Bind("rules_pending", &data.rules_pending);
        model.Bind("rules_map", &data.rules_map);
        model.Bind("rules_mode", &data.rules_mode);
        model.Bind("rules_format", &data.rules_format);
        model.Bind("rules_goal_label", &data.rules_goal_label);
        model.Bind("rules_status", &data.rules_status);
        model.Bind("rules_toggle0", &data.rules_toggles[0]);
        model.Bind("rules_toggle1", &data.rules_toggles[1]);
        model.Bind("rules_toggle2", &data.rules_toggles[2]);
        model.Bind("rules_toggle3", &data.rules_toggles[3]);
        model.Bind("rules_toggle4", &data.rules_toggles[4]);
        model.Bind("rules_toggle5", &data.rules_toggles[5]);
        model.Bind("rules_toggle6", &data.rules_toggles[6]);
        model.Bind("rules_toggle7", &data.rules_toggles[7]);
        model.Bind("rules_toggle8", &data.rules_toggles[8]);
        model.Bind("rules_toggle9", &data.rules_toggles[9]);
        model.Bind("rules_toggle10", &data.rules_toggles[10]);
        model.Bind("rules_toggle11", &data.rules_toggles[11]);
        model.Bind("rules_toggle12", &data.rules_toggles[12]);
        model.Bind("rules_toggle13", &data.rules_toggles[13]);
        model.Bind("rules_toggle14", &data.rules_toggles[14]);
        model.Bind("rules_toggle15", &data.rules_toggles[15]);
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
        model.BindEventCallback("lobby_rules_open", &PrimeMenuModel::LobbyRulesOpen, this);
        model.BindEventCallback("lobby_rules_close", &PrimeMenuModel::LobbyRulesClose, this);
        model.BindEventCallback("lobby_rules_apply", &PrimeMenuModel::LobbyRulesApply, this);
        model.BindEventCallback("lobby_rules_map", &PrimeMenuModel::LobbyRulesMap, this);
        model.BindEventCallback("lobby_rules_mode", &PrimeMenuModel::LobbyRulesMode, this);
        model.BindEventCallback("lobby_rules_format", &PrimeMenuModel::LobbyRulesFormat, this);
        model.BindEventCallback("lobby_rules_toggle0", &PrimeMenuModel::LobbyRulesToggle0, this);
        model.BindEventCallback("lobby_rules_toggle1", &PrimeMenuModel::LobbyRulesToggle1, this);
        model.BindEventCallback("lobby_rules_toggle2", &PrimeMenuModel::LobbyRulesToggle2, this);
        model.BindEventCallback("lobby_rules_toggle3", &PrimeMenuModel::LobbyRulesToggle3, this);
        model.BindEventCallback("lobby_rules_toggle4", &PrimeMenuModel::LobbyRulesToggle4, this);
        model.BindEventCallback("lobby_rules_toggle5", &PrimeMenuModel::LobbyRulesToggle5, this);
        model.BindEventCallback("lobby_rules_toggle6", &PrimeMenuModel::LobbyRulesToggle6, this);
        model.BindEventCallback("lobby_rules_toggle7", &PrimeMenuModel::LobbyRulesToggle7, this);
        model.BindEventCallback("lobby_rules_toggle8", &PrimeMenuModel::LobbyRulesToggle8, this);
        model.BindEventCallback("lobby_rules_toggle9", &PrimeMenuModel::LobbyRulesToggle9, this);
        model.BindEventCallback("lobby_rules_toggle10", &PrimeMenuModel::LobbyRulesToggle10, this);
        model.BindEventCallback("lobby_rules_toggle11", &PrimeMenuModel::LobbyRulesToggle11, this);
        model.BindEventCallback("lobby_rules_toggle12", &PrimeMenuModel::LobbyRulesToggle12, this);
        model.BindEventCallback("lobby_rules_toggle13", &PrimeMenuModel::LobbyRulesToggle13, this);
        model.BindEventCallback("lobby_rules_toggle14", &PrimeMenuModel::LobbyRulesToggle14, this);
        model.BindEventCallback("lobby_rules_toggle15", &PrimeMenuModel::LobbyRulesToggle15, this);
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
        else if (name == "rules_map") data.rules_map = value;
        else if (name == "rules_mode") data.rules_mode = value;
        else if (name == "rules_format") data.rules_format = value;
        else if (name == "rules_goal_label") data.rules_goal_label = value;
        else if (name == "rules_status") data.rules_status = value;
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
            for (int i = 0; i < int(data.rules_toggles.size()); ++i) {
                if (name == "rules_toggle" + std::to_string(i)) {
                    data.rules_toggles[i] = value;
                    handle.DirtyVariable(name);
                    return;
                }
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
                data.lobby_rules_open = false;
                handle.DirtyVariable("lobby_rules_open");
                handle.DirtyVariable("multiplayer_mode");
                handle.DirtyVariable("play_create_mode");
                handle.DirtyVariable("play_browser_mode");
            }
            g_lobby_anchor_dirty = true;
            handle.DirtyVariable("lobby_mode");
            handle.DirtyVariable("home_mode");
            RequestFocus(value ? (data.lobby_require_ready ? "lobby_ready" : "lobby_hunter")
                : "activity_selector");
            return;
        }
        else if (name == "home_mode") data.home_mode = value;
        else if (name == "lobby_owner") data.lobby_owner = value;
        else if (name == "lobby_local_ready") data.lobby_local_ready = value;
        else if (name == "lobby_require_ready") {
            if (data.lobby_require_ready == value) return;
            data.lobby_require_ready = value;
            handle.DirtyVariable("lobby_require_ready");
            if (data.lobby_mode)
                RequestFocus(value ? "lobby_ready" : "lobby_hunter");
            return;
        }
        else if (name == "lobby_starting") data.lobby_starting = value;
        else if (name == "lobby_rules_open") {
            data.lobby_rules_open = value;
            handle.DirtyVariable("lobby_rules_open");
            RequestFocus(value ? (data.rules_owner ? "rules_map_next" : "rules_close_top")
                : "lobby_rules");
            return;
        }
        else if (name == "rules_owner") data.rules_owner = value;
        else if (name == "rules_dirty") data.rules_dirty = value;
        else if (name == "rules_pending") data.rules_pending = value;
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

    bool Back()
    {
        if (data.lobby_mode) {
            if (data.lobby_rules_open) {
                CloseLobbyRules();
                return true;
            }
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
                DirtyVisual();
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
    std::string pending_focus;
    std::unordered_map<std::string, std::string> pending_inputs;

    void Emit(const char* action) { QueueHomeAction(action); }
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
        QueueHomeAction(std::string(preview ? "stage-preview:" : "stage:")
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
    void CloseLobbyRules()
    {
        if (!data.lobby_rules_open) return;
        data.lobby_rules_open = false;
        handle.DirtyVariable("lobby_rules_open");
        Emit("lobby:rules-close");
        RequestFocus("lobby_rules");
    }
    void LobbyRulesOpen(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode) Emit("lobby:rules-open"); }
    void LobbyRulesClose(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { CloseLobbyRules(); }
    void LobbyRulesApply(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-apply"); }
    void LobbyRulesMap(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-map"); }
    void LobbyRulesMode(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-mode"); }
    void LobbyRulesFormat(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-format"); }
    void LobbyRulesToggle0(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:0"); }
    void LobbyRulesToggle1(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:1"); }
    void LobbyRulesToggle2(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:2"); }
    void LobbyRulesToggle3(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:3"); }
    void LobbyRulesToggle4(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:4"); }
    void LobbyRulesToggle5(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:5"); }
    void LobbyRulesToggle6(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:6"); }
    void LobbyRulesToggle7(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:7"); }
    void LobbyRulesToggle8(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:8"); }
    void LobbyRulesToggle9(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:9"); }
    void LobbyRulesToggle10(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:10"); }
    void LobbyRulesToggle11(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:11"); }
    void LobbyRulesToggle12(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:12"); }
    void LobbyRulesToggle13(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:13"); }
    void LobbyRulesToggle14(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:14"); }
    void LobbyRulesToggle15(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    { if (data.lobby_mode && data.lobby_rules_open && data.rules_owner) Emit("lobby:rules-toggle:15"); }
    void Noop(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) {}
};
