using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace MphRead.Mods.Launcher.RmlUi.Host
{
    public enum RmlUiMenuPage { Home, Play, Lobby, Rules, Maps, Hunters, Admin, Offline, OfflineArena, OfflineTraining, Adventure, AdventureConfirm, Theatre, OfflineRules, PartyQueue }

    public static class RmlUiMenuPages
    {
        public static readonly RmlUiPageSpec Home = new("home", "pages/home/home.rml", "activity_selector");
        public static readonly RmlUiPageSpec Play = new("play", "pages/play/browser.rml", "play_create_open");
        public static readonly RmlUiPageSpec Lobby = new("lobby", "pages/lobby/lobby.rml", "lobby_ready");
        public static readonly RmlUiPageSpec Rules = new("rules", "pages/lobby/rules.rml", "rules_close_top");
        public static readonly RmlUiPageSpec Maps = new("maps", "pages/lobby/maps.rml", "map_close");
        public static readonly RmlUiPageSpec Hunters = new("hunters", "pages/hunters/identity.rml", "hunter_cancel");
        public static readonly RmlUiPageSpec Admin = new("admin", "pages/lobby/admin.rml", "admin_close");
        public static readonly RmlUiPageSpec Offline = new("offline", "pages/offline/setup.rml", "offline_arena");
        public static readonly RmlUiPageSpec OfflineArena = new("offline-arena", "pages/offline/arena.rml", "offline_arena_search");
        public static readonly RmlUiPageSpec OfflineTraining = new("offline-training", "pages/offline/training.rml", "offline_training_done");
        public static readonly RmlUiPageSpec OfflineRules = new("offline-rules", "pages/offline/rules.rml", "offline_rules_cancel");
        public static readonly RmlUiPageSpec Adventure = new("adventure", "pages/adventure/saves.rml", "adventure_slot1");
        public static readonly RmlUiPageSpec AdventureConfirm = new("adventure-confirm", "pages/adventure/confirm.rml", "adventure_cancel_new_run");
        public static readonly RmlUiPageSpec Theatre = new("theatre", "pages/theatre/library.rml", "theatre_search");
        public static readonly RmlUiPageSpec PartyQueue = new("party-queue", "pages/play/queue.rml", "play_queue_leave");

        public static RmlUiPageSpec Spec(RmlUiMenuPage page) => page switch
        {
            RmlUiMenuPage.Home => Home, RmlUiMenuPage.Play => Play,
            RmlUiMenuPage.Lobby => Lobby, RmlUiMenuPage.Rules => Rules, RmlUiMenuPage.Maps => Maps,
            RmlUiMenuPage.Hunters => Hunters, RmlUiMenuPage.Admin => Admin,
            RmlUiMenuPage.Offline => Offline, RmlUiMenuPage.OfflineArena => OfflineArena,
            RmlUiMenuPage.OfflineTraining => OfflineTraining,
            RmlUiMenuPage.OfflineRules => OfflineRules,
            RmlUiMenuPage.Adventure => Adventure, RmlUiMenuPage.AdventureConfirm => AdventureConfirm,
            RmlUiMenuPage.Theatre => Theatre,
            RmlUiMenuPage.PartyQueue => PartyQueue,
            _ => throw new ArgumentOutOfRangeException(nameof(page))
        };
    }

    /// <summary>Projects the existing menu state onto independent DOM documents.
    /// Targets mirror the data-prime attributes in the authored RML. Fields are never projected.</summary>
    public static class RmlUiMenuPageBindings
    {
        private readonly record struct Binding(RmlUiBindingKind Kind, string Target, string Expression);

        private static readonly Binding[] ShellBindings =
        {
            new(RmlUiBindingKind.Boolean, "class:screen:reduce-motion", "reduce_motion"),
            new(RmlUiBindingKind.Boolean, "class:screen:lobby", "lobby_mode"),
            new(RmlUiBindingKind.Text, "profile", "{{player_name}}"),
            new(RmlUiBindingKind.Text, "system_status_toast", "{{system_status}}"),
            new(RmlUiBindingKind.Boolean, "class:diagnostics_panel:open", "diagnostics_visible"),
            new(RmlUiBindingKind.Text, "text_renderer_name_1", "{{renderer_name}}"),
            new(RmlUiBindingKind.Text, "text_game_data_state_1", "{{game_data_state}}"),
            new(RmlUiBindingKind.Text, "text_ui_cost_1", "{{ui_cost}}"),
            new(RmlUiBindingKind.Text, "input_debug", "{{input_debug}}"),
            new(RmlUiBindingKind.Text, "build_version", "BUILD {{build_version}}"),
            new(RmlUiBindingKind.Boolean, "visible:footer_actions", "home_mode"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_footer_state", "lobby_mode"),
            new(RmlUiBindingKind.Text, "lobby_footer_state", "{{lobby_status}}"),
            new(RmlUiBindingKind.Boolean, "disabled:nav_hunters", "lobby_mode"),
            new(RmlUiBindingKind.Boolean, "disabled:nav_community", "lobby_mode"),
            new(RmlUiBindingKind.Boolean, "disabled:nav_studio", "lobby_mode"),
        };

        private static readonly Binding[] HomeBindings =
        {
            new(RmlUiBindingKind.Boolean, "class:drawer_training:selected", "activity_index == 4"),
            new(RmlUiBindingKind.Boolean, "visible:drawer_training_active", "activity_index == 4"),
            new(RmlUiBindingKind.Boolean, "class:activity_compact:hidden", "activity_selector_open"),
            new(RmlUiBindingKind.Text, "text_activity_group_1", "{{activity_group}}"),
            new(RmlUiBindingKind.Text, "text_activity_title_1", "{{activity_title}}"),
            new(RmlUiBindingKind.Text, "text_activity_description_1", "{{activity_description}}"),
            new(RmlUiBindingKind.Text, "text_activity_hint_1", "{{activity_hint}}"),
            new(RmlUiBindingKind.Text, "text_activity_hint_2", "{{activity_hint}}"),
            new(RmlUiBindingKind.Text, "text_activity_group_2", "{{activity_group}} ACTIVITY"),
            new(RmlUiBindingKind.Text, "text_activity_action_1", "{{activity_action}}"),
            new(RmlUiBindingKind.Boolean, "class:activity_drawer:open", "activity_selector_open"),
            new(RmlUiBindingKind.Boolean, "class:drawer_browser:selected", "activity_index == 1"),
            new(RmlUiBindingKind.Boolean, "visible:visible_activity_index____1_1", "activity_index == 1"),
            new(RmlUiBindingKind.Boolean, "class:drawer_offline:selected", "activity_index == 2"),
            new(RmlUiBindingKind.Boolean, "visible:visible_activity_index____2_1", "activity_index == 2"),
            new(RmlUiBindingKind.Boolean, "class:drawer_adventure:selected", "activity_index == 3"),
            new(RmlUiBindingKind.Boolean, "visible:visible_activity_index____3_1", "activity_index == 3"),
            new(RmlUiBindingKind.Text, "hunter_name", "{{hunter_name}}"),
            new(RmlUiBindingKind.Text, "text_player_name_2", "{{player_name}}"),
            new(RmlUiBindingKind.Text, "text_hunter_name_1", "{{hunter_name}} // READY"),
            new(RmlUiBindingKind.Text, "home_party_count", "{{home_party_count}}"),
            new(RmlUiBindingKind.Text, "home_party_capacity_hint", "{{home_party_capacity_hint}}"),
            new(RmlUiBindingKind.Text, "home_party_slot1_name", "{{home_party_slot1_name}}"),
            new(RmlUiBindingKind.Text, "home_party_slot1_detail", "{{home_party_slot1_detail}}"),
            new(RmlUiBindingKind.Boolean, "class:home_social:occupied", "home_party_slot1_occupied"),
            new(RmlUiBindingKind.Text, "home_party_slot2_name", "{{home_party_slot2_name}}"),
            new(RmlUiBindingKind.Text, "home_party_slot2_detail", "{{home_party_slot2_detail}}"),
            new(RmlUiBindingKind.Boolean, "class:home_party_slot2:occupied", "home_party_slot2_occupied"),
            new(RmlUiBindingKind.Boolean, "disabled:home_party_slot2", "!home_social_ready"),
            new(RmlUiBindingKind.Text, "home_party_slot3_name", "{{home_party_slot3_name}}"),
            new(RmlUiBindingKind.Text, "home_party_slot3_detail", "{{home_party_slot3_detail}}"),
            new(RmlUiBindingKind.Boolean, "class:home_party_slot3:occupied", "home_party_slot3_occupied"),
            new(RmlUiBindingKind.Boolean, "disabled:home_party_slot3", "!home_social_ready"),
            new(RmlUiBindingKind.Text, "home_social_summary", "{{home_social_summary}}"),
            new(RmlUiBindingKind.Boolean, "disabled:home_social", "!home_social_ready"),



            new(RmlUiBindingKind.Text, "home_friends_online", "{{home_friends_online}}"),
            new(RmlUiBindingKind.Text, "home_invites_count", "{{home_invites_count}}"),
            new(RmlUiBindingKind.Text, "home_requests_count", "{{home_requests_count}}"),

            new(RmlUiBindingKind.Text, "home_session_state", "{{home_session_state}}"),
            new(RmlUiBindingKind.Boolean, "class:home_feature_rail:hidden", "activity_selector_open"),
            new(RmlUiBindingKind.Boolean, "class:home_feature_rail:empty", "!home_feature_available"),
            new(RmlUiBindingKind.Text, "home_feature_category", "{{home_feature_category}}"),
            new(RmlUiBindingKind.Text, "home_feature_title", "{{home_feature_title}}"),
            new(RmlUiBindingKind.Text, "home_feature_summary", "{{home_feature_summary}}"),
            new(RmlUiBindingKind.Boolean, "visible:home_news0", "home_news0_visible"),
            new(RmlUiBindingKind.Text, "home_news0_title", "{{home_news0_title}}"),
            new(RmlUiBindingKind.Text, "home_news0_category", "{{home_news0_category}}"),
            new(RmlUiBindingKind.Boolean, "visible:home_news1", "home_news1_visible"),
            new(RmlUiBindingKind.Text, "home_news1_title", "{{home_news1_title}}"),
            new(RmlUiBindingKind.Text, "home_news1_category", "{{home_news1_category}}"),
            new(RmlUiBindingKind.Boolean, "visible:home_social_rail", "home_social_rail_visible"),
            new(RmlUiBindingKind.Boolean, "visible:home_social_open", "home_social_rail_visible"),
            new(RmlUiBindingKind.Boolean, "visible:home_social_empty", "home_social_empty_visible"),
            new(RmlUiBindingKind.Text, "home_social_empty", "{{home_social_empty_text}}"),
            new(RmlUiBindingKind.Text, "home_social_count", "{{home_social_count}}"),
            new(RmlUiBindingKind.Boolean, "visible:home_social_alert", "home_social_alert_visible"),
            new(RmlUiBindingKind.Text, "home_social_alert_text", "{{home_social_alert_text}}"),
            new(RmlUiBindingKind.Boolean, "visible:home_party_preview", "home_party_preview_visible"),
            new(RmlUiBindingKind.Text, "home_party_preview_name", "{{home_party_preview_name}}"),
            new(RmlUiBindingKind.Text, "home_party_preview_role", "{{home_party_preview_role}}"),
            new(RmlUiBindingKind.Boolean, "visible:home_friend_section", "home_friends_visible"),
            new(RmlUiBindingKind.Text, "home_friend_count", "{{home_friend_count}}"),
            new(RmlUiBindingKind.Boolean, "visible:home_friend0", "home_friend0_visible"),
            new(RmlUiBindingKind.Boolean, "class:home_friend0:joinable", "home_friend0_joinable"),
            new(RmlUiBindingKind.Text, "home_friend0_name", "{{home_friend0_name}}"),
            new(RmlUiBindingKind.Text, "home_friend0_activity", "{{home_friend0_activity}}"),
            new(RmlUiBindingKind.Text, "home_friend0_status", "{{home_friend0_status}}"),
            new(RmlUiBindingKind.Boolean, "visible:home_friend1", "home_friend1_visible"),
            new(RmlUiBindingKind.Boolean, "class:home_friend1:joinable", "home_friend1_joinable"),
            new(RmlUiBindingKind.Text, "home_friend1_name", "{{home_friend1_name}}"),
            new(RmlUiBindingKind.Text, "home_friend1_activity", "{{home_friend1_activity}}"),
            new(RmlUiBindingKind.Text, "home_friend1_status", "{{home_friend1_status}}"),
            new(RmlUiBindingKind.Boolean, "visible:home_friend2", "home_friend2_visible"),
            new(RmlUiBindingKind.Boolean, "class:home_friend2:joinable", "home_friend2_joinable"),
            new(RmlUiBindingKind.Text, "home_friend2_name", "{{home_friend2_name}}"),
            new(RmlUiBindingKind.Text, "home_friend2_activity", "{{home_friend2_activity}}"),
            new(RmlUiBindingKind.Text, "home_friend2_status", "{{home_friend2_status}}"),
        };

        private static readonly Binding[] PlayBindings =
        {
            new(RmlUiBindingKind.Boolean, "visible:play_search", "play_random_search"),
            new(RmlUiBindingKind.Text, "play_search_elapsed", "{{play_search_elapsed}}"),
            new(RmlUiBindingKind.Boolean, "disabled:play_quick", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_create_open", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_refresh", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_join", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_player_name", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_join_address", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_entry0", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_entry1", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_entry2", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_entry3", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_entry4", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_entry5", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_entry6", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_entry7", "play_actions_locked"),
            new(RmlUiBindingKind.Text, "text_play_status_1", "{{play_status}}"),
            new(RmlUiBindingKind.Boolean, "visible:play_browser", "play_browser_mode"),
            new(RmlUiBindingKind.Text, "text_play_server_count_1", "{{play_server_count}}"),
            new(RmlUiBindingKind.Boolean, "visible:play_entry0", "play_server0_present"),
            new(RmlUiBindingKind.Text, "text_play_server0_name_1", "{{play_server0_name}}"),
            new(RmlUiBindingKind.Text, "text_play_server0_details_1", "{{play_server0_details}}"),
            new(RmlUiBindingKind.Boolean, "visible:play_entry1", "play_server1_present"),
            new(RmlUiBindingKind.Text, "text_play_server1_name_1", "{{play_server1_name}}"),
            new(RmlUiBindingKind.Text, "text_play_server1_details_1", "{{play_server1_details}}"),
            new(RmlUiBindingKind.Boolean, "visible:play_entry2", "play_server2_present"),
            new(RmlUiBindingKind.Text, "text_play_server2_name_1", "{{play_server2_name}}"),
            new(RmlUiBindingKind.Text, "text_play_server2_details_1", "{{play_server2_details}}"),
            new(RmlUiBindingKind.Boolean, "visible:play_entry3", "play_server3_present"),
            new(RmlUiBindingKind.Text, "text_play_server3_name_1", "{{play_server3_name}}"),
            new(RmlUiBindingKind.Text, "text_play_server3_details_1", "{{play_server3_details}}"),
            new(RmlUiBindingKind.Boolean, "visible:play_entry4", "play_server4_present"),
            new(RmlUiBindingKind.Text, "text_play_server4_name_1", "{{play_server4_name}}"),
            new(RmlUiBindingKind.Text, "text_play_server4_details_1", "{{play_server4_details}}"),
            new(RmlUiBindingKind.Boolean, "visible:play_entry5", "play_server5_present"),
            new(RmlUiBindingKind.Text, "text_play_server5_name_1", "{{play_server5_name}}"),
            new(RmlUiBindingKind.Text, "text_play_server5_details_1", "{{play_server5_details}}"),
            new(RmlUiBindingKind.Boolean, "visible:play_entry6", "play_server6_present"),
            new(RmlUiBindingKind.Text, "text_play_server6_name_1", "{{play_server6_name}}"),
            new(RmlUiBindingKind.Text, "text_play_server6_details_1", "{{play_server6_details}}"),
            new(RmlUiBindingKind.Boolean, "visible:play_entry7", "play_server7_present"),
            new(RmlUiBindingKind.Text, "text_play_server7_name_1", "{{play_server7_name}}"),
            new(RmlUiBindingKind.Text, "text_play_server7_details_1", "{{play_server7_details}}"),
            new(RmlUiBindingKind.Boolean, "visible:play_browser_empty", "play_no_servers"),
            new(RmlUiBindingKind.Boolean, "visible:play_connect", "play_browser_mode"),
            new(RmlUiBindingKind.Boolean, "visible:play_create", "play_create_mode"),
            new(RmlUiBindingKind.Text, "text_play_create_host_1", "{{play_create_host}} "),
            new(RmlUiBindingKind.Text, "play_create_map_label", "{{play_create_map}}"),
            new(RmlUiBindingKind.Text, "play_create_mode_label", "{{play_create_mode_name}}"),
            new(RmlUiBindingKind.Text, "play_create_source", "{{play_create_source}}"),
            new(RmlUiBindingKind.Text, "play_create_capacity", "{{play_create_capacity}}"),
            new(RmlUiBindingKind.Boolean, "disabled:play_next_map", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_next_mode", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_toggle_host", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_create_name", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_create_submit", "play_actions_locked"),
            new(RmlUiBindingKind.Boolean, "disabled:play_create_cancel", "play_actions_locked"),
        };

        private static readonly Binding[] PartyQueueBindings =
        {
            new(RmlUiBindingKind.Text, "play_queue_status", "{{party_queue_status}}"),
            new(RmlUiBindingKind.Text, "play_queue_phase", "{{party_queue_phase}}"),
            new(RmlUiBindingKind.Text, "play_queue_position", "{{party_queue_position}}"),
            new(RmlUiBindingKind.Text, "play_queue_seats", "{{party_queue_seats}}"),
            new(RmlUiBindingKind.Text, "play_queue_seconds", "{{party_queue_seconds}}"),
            new(RmlUiBindingKind.Boolean, "visible:play_queue_offer", "party_queue_offer"),
            new(RmlUiBindingKind.Boolean, "disabled:play_queue_accept", "!party_queue_can_accept"),
            new(RmlUiBindingKind.Boolean, "disabled:play_queue_decline", "!party_queue_can_decline"),
            new(RmlUiBindingKind.Boolean, "disabled:play_queue_leave", "!party_queue_can_leave")
        };

        private static readonly Binding[] LobbyBindings =
        {
            new(RmlUiBindingKind.Boolean, "disabled:lobby_social_access", "!home_social_ready"),
            new(RmlUiBindingKind.Text, "lobby_brief_title", "{{lobby_brief_title}}"),
            new(RmlUiBindingKind.Text, "lobby_brief_count", "{{lobby_brief_count}}"),
            new(RmlUiBindingKind.Text, "lobby_brief_detail", "{{lobby_brief_detail}}"),
            new(RmlUiBindingKind.Text, "lobby_field_caption", "{{lobby_field_caption}}"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot0:active", "slot0_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot0:ready", "slot0_ready"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot0:local", "slot0_local"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot0:ready", "slot0_ready"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot1:active", "slot1_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot1:ready", "slot1_ready"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot1:local", "slot1_local"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot1:ready", "slot1_ready"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot2:active", "slot2_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot2:ready", "slot2_ready"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot2:local", "slot2_local"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot2:ready", "slot2_ready"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot3:active", "slot3_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot3:ready", "slot3_ready"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot3:local", "slot3_local"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot3:ready", "slot3_ready"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot4:active", "slot4_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot4:ready", "slot4_ready"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot4:local", "slot4_local"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot4:ready", "slot4_ready"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot5:active", "slot5_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot5:ready", "slot5_ready"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot5:local", "slot5_local"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot5:ready", "slot5_ready"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot6:active", "slot6_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot6:ready", "slot6_ready"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot6:local", "slot6_local"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot6:ready", "slot6_ready"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot7:active", "slot7_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot7:ready", "slot7_ready"),
            new(RmlUiBindingKind.Boolean, "class:lobby_brief_slot7:local", "slot7_local"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot7:ready", "slot7_ready"),
            new(RmlUiBindingKind.Text, "lobby_time", "{{lobby_time}}"),
            new(RmlUiBindingKind.Text, "lobby_score", "{{lobby_score}}"),
            new(RmlUiBindingKind.Text, "lobby_countdown_map", "{{lobby_map}}"),
            new(RmlUiBindingKind.Text, "lobby_countdown_number", "{{lobby_countdown_number}}"),
            new(RmlUiBindingKind.Text, "lobby_countdown_detail", "{{lobby_countdown_detail}}"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_countdown", "lobby_starting"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_slot0", "slot0_occupied"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_slot1", "slot1_occupied"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_slot2", "slot2_occupied"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_slot3", "slot3_occupied"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_slot4", "slot4_occupied"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_slot5", "slot5_occupied"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_slot6", "slot6_occupied"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_slot7", "slot7_occupied"),
            new(RmlUiBindingKind.Text, "lobby_chat_history", "{{lobby_chat_history}}"),
            new(RmlUiBindingKind.Text, "lobby_chat_status", "{{lobby_chat_status}}"),
            new(RmlUiBindingKind.Text, "image:lobby_map_image", "{{lobby_map_image}}"),
            new(RmlUiBindingKind.Text, "lobby_map_preview_status", "{{lobby_map_preview_status}}"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_map_image", "lobby_map_image_ready"),
            new(RmlUiBindingKind.Text, "text_lobby_name_1", "{{lobby_name}}"),
            new(RmlUiBindingKind.Text, "text_lobby_status_1", "{{lobby_status}}"),
            new(RmlUiBindingKind.Text, "text_lobby_mode_name_1", "{{lobby_mode_name}}"),
            new(RmlUiBindingKind.Text, "text_lobby_map_1", "{{lobby_map}}"),
            new(RmlUiBindingKind.Text, "text_lobby_format_1", "{{lobby_format}}"),
            new(RmlUiBindingKind.Text, "text_lobby_player_count_1", "{{lobby_player_count}}"),
            new(RmlUiBindingKind.Text, "text_lobby_ready_count_1", "{{lobby_ready_count}}"),
            new(RmlUiBindingKind.Text, "text_lobby_player_count_2", "{{lobby_player_count}}"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot0_occupied_1:ready", "slot0_ready"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot0_occupied_1:local", "slot0_local"),
            new(RmlUiBindingKind.Text, "text_slot0_name_1", "{{slot0_name}}"),
            new(RmlUiBindingKind.Text, "text_slot0_hunter_1", "{{slot0_hunter}}"),
            new(RmlUiBindingKind.Text, "text_slot0_state_1", "{{slot0_state}}"),
            new(RmlUiBindingKind.Text, "lobby_team0", "{{slot0_team}}"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_team0", "slot0_has_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_team0", "!slot0_can_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_player0", "!slot0_can_select"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_add_bot0", "slot0_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_add_bot0", "!slot0_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_slot0", "!slot0_can_select"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot0_occupied_1:empty", "!slot0_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot0:empty", "!slot0_occupied"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot0_occupied_1:team0", "slot0_team0"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot0:team0", "slot0_team0"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot0_occupied_1:team1", "slot0_team1"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot0:team1", "slot0_team1"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot0_occupied_1:team2", "slot0_team2"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot0:team2", "slot0_team2"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot0_occupied_1:team3", "slot0_team3"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot0:team3", "slot0_team3"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot1_occupied_1:ready", "slot1_ready"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot1_occupied_1:local", "slot1_local"),
            new(RmlUiBindingKind.Text, "text_slot1_name_1", "{{slot1_name}}"),
            new(RmlUiBindingKind.Text, "text_slot1_hunter_1", "{{slot1_hunter}}"),
            new(RmlUiBindingKind.Text, "text_slot1_state_1", "{{slot1_state}}"),
            new(RmlUiBindingKind.Text, "lobby_team1", "{{slot1_team}}"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_team1", "slot1_has_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_team1", "!slot1_can_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_player1", "!slot1_can_select"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_add_bot1", "slot1_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_add_bot1", "!slot1_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_slot1", "!slot1_can_select"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot1_occupied_1:empty", "!slot1_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot1:empty", "!slot1_occupied"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot1_occupied_1:team0", "slot1_team0"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot1:team0", "slot1_team0"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot1_occupied_1:team1", "slot1_team1"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot1:team1", "slot1_team1"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot1_occupied_1:team2", "slot1_team2"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot1:team2", "slot1_team2"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot1_occupied_1:team3", "slot1_team3"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot1:team3", "slot1_team3"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot2_occupied_1:ready", "slot2_ready"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot2_occupied_1:local", "slot2_local"),
            new(RmlUiBindingKind.Text, "text_slot2_name_1", "{{slot2_name}}"),
            new(RmlUiBindingKind.Text, "text_slot2_hunter_1", "{{slot2_hunter}}"),
            new(RmlUiBindingKind.Text, "text_slot2_state_1", "{{slot2_state}}"),
            new(RmlUiBindingKind.Text, "lobby_team2", "{{slot2_team}}"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_team2", "slot2_has_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_team2", "!slot2_can_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_player2", "!slot2_can_select"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_add_bot2", "slot2_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_add_bot2", "!slot2_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_slot2", "!slot2_can_select"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot2_occupied_1:empty", "!slot2_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot2:empty", "!slot2_occupied"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot2_occupied_1:team0", "slot2_team0"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot2:team0", "slot2_team0"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot2_occupied_1:team1", "slot2_team1"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot2:team1", "slot2_team1"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot2_occupied_1:team2", "slot2_team2"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot2:team2", "slot2_team2"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot2_occupied_1:team3", "slot2_team3"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot2:team3", "slot2_team3"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot3_occupied_1:ready", "slot3_ready"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot3_occupied_1:local", "slot3_local"),
            new(RmlUiBindingKind.Text, "text_slot3_name_1", "{{slot3_name}}"),
            new(RmlUiBindingKind.Text, "text_slot3_hunter_1", "{{slot3_hunter}}"),
            new(RmlUiBindingKind.Text, "text_slot3_state_1", "{{slot3_state}}"),
            new(RmlUiBindingKind.Text, "lobby_team3", "{{slot3_team}}"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_team3", "slot3_has_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_team3", "!slot3_can_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_player3", "!slot3_can_select"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_add_bot3", "slot3_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_add_bot3", "!slot3_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_slot3", "!slot3_can_select"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot3_occupied_1:empty", "!slot3_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot3:empty", "!slot3_occupied"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot3_occupied_1:team0", "slot3_team0"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot3:team0", "slot3_team0"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot3_occupied_1:team1", "slot3_team1"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot3:team1", "slot3_team1"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot3_occupied_1:team2", "slot3_team2"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot3:team2", "slot3_team2"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot3_occupied_1:team3", "slot3_team3"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot3:team3", "slot3_team3"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot4_occupied_1:ready", "slot4_ready"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot4_occupied_1:local", "slot4_local"),
            new(RmlUiBindingKind.Text, "text_slot4_name_1", "{{slot4_name}}"),
            new(RmlUiBindingKind.Text, "text_slot4_hunter_1", "{{slot4_hunter}}"),
            new(RmlUiBindingKind.Text, "text_slot4_state_1", "{{slot4_state}}"),
            new(RmlUiBindingKind.Text, "lobby_team4", "{{slot4_team}}"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_team4", "slot4_has_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_team4", "!slot4_can_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_player4", "!slot4_can_select"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_add_bot4", "slot4_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_add_bot4", "!slot4_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_slot4", "!slot4_can_select"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot4_occupied_1:empty", "!slot4_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot4:empty", "!slot4_occupied"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot4_occupied_1:team0", "slot4_team0"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot4:team0", "slot4_team0"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot4_occupied_1:team1", "slot4_team1"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot4:team1", "slot4_team1"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot4_occupied_1:team2", "slot4_team2"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot4:team2", "slot4_team2"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot4_occupied_1:team3", "slot4_team3"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot4:team3", "slot4_team3"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot5_occupied_1:ready", "slot5_ready"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot5_occupied_1:local", "slot5_local"),
            new(RmlUiBindingKind.Text, "text_slot5_name_1", "{{slot5_name}}"),
            new(RmlUiBindingKind.Text, "text_slot5_hunter_1", "{{slot5_hunter}}"),
            new(RmlUiBindingKind.Text, "text_slot5_state_1", "{{slot5_state}}"),
            new(RmlUiBindingKind.Text, "lobby_team5", "{{slot5_team}}"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_team5", "slot5_has_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_team5", "!slot5_can_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_player5", "!slot5_can_select"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_add_bot5", "slot5_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_add_bot5", "!slot5_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_slot5", "!slot5_can_select"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot5_occupied_1:empty", "!slot5_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot5:empty", "!slot5_occupied"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot5_occupied_1:team0", "slot5_team0"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot5:team0", "slot5_team0"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot5_occupied_1:team1", "slot5_team1"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot5:team1", "slot5_team1"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot5_occupied_1:team2", "slot5_team2"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot5:team2", "slot5_team2"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot5_occupied_1:team3", "slot5_team3"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot5:team3", "slot5_team3"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot6_occupied_1:ready", "slot6_ready"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot6_occupied_1:local", "slot6_local"),
            new(RmlUiBindingKind.Text, "text_slot6_name_1", "{{slot6_name}}"),
            new(RmlUiBindingKind.Text, "text_slot6_hunter_1", "{{slot6_hunter}}"),
            new(RmlUiBindingKind.Text, "text_slot6_state_1", "{{slot6_state}}"),
            new(RmlUiBindingKind.Text, "lobby_team6", "{{slot6_team}}"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_team6", "slot6_has_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_team6", "!slot6_can_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_player6", "!slot6_can_select"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_add_bot6", "slot6_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_add_bot6", "!slot6_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_slot6", "!slot6_can_select"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot6_occupied_1:empty", "!slot6_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot6:empty", "!slot6_occupied"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot6_occupied_1:team0", "slot6_team0"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot6:team0", "slot6_team0"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot6_occupied_1:team1", "slot6_team1"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot6:team1", "slot6_team1"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot6_occupied_1:team2", "slot6_team2"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot6:team2", "slot6_team2"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot6_occupied_1:team3", "slot6_team3"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot6:team3", "slot6_team3"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot7_occupied_1:ready", "slot7_ready"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot7_occupied_1:local", "slot7_local"),
            new(RmlUiBindingKind.Text, "text_slot7_name_1", "{{slot7_name}}"),
            new(RmlUiBindingKind.Text, "text_slot7_hunter_1", "{{slot7_hunter}}"),
            new(RmlUiBindingKind.Text, "text_slot7_state_1", "{{slot7_state}}"),
            new(RmlUiBindingKind.Text, "lobby_team7", "{{slot7_team}}"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_team7", "slot7_has_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_team7", "!slot7_can_team"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_player7", "!slot7_can_select"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_add_bot7", "slot7_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_add_bot7", "!slot7_can_add_bot"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_slot7", "!slot7_can_select"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot7_occupied_1:empty", "!slot7_occupied"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot7:empty", "!slot7_occupied"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot7_occupied_1:team0", "slot7_team0"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot7:team0", "slot7_team0"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot7_occupied_1:team1", "slot7_team1"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot7:team1", "slot7_team1"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot7_occupied_1:team2", "slot7_team2"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot7:team2", "slot7_team2"),
            new(RmlUiBindingKind.Boolean, "class:visible_slot7_occupied_1:team3", "slot7_team3"),
            new(RmlUiBindingKind.Boolean, "class:lobby_slot7:team3", "slot7_team3"),
            new(RmlUiBindingKind.Text, "text_slot0_name_2", "{{slot0_name}}"),
            new(RmlUiBindingKind.Text, "text_slot0_hunter_2", "{{slot0_hunter}} // {{slot0_state}}"),
            new(RmlUiBindingKind.Text, "text_slot1_name_2", "{{slot1_name}}"),
            new(RmlUiBindingKind.Text, "text_slot1_hunter_2", "{{slot1_hunter}}"),
            new(RmlUiBindingKind.Text, "text_slot2_name_2", "{{slot2_name}}"),
            new(RmlUiBindingKind.Text, "text_slot2_hunter_2", "{{slot2_hunter}}"),
            new(RmlUiBindingKind.Text, "text_slot3_name_2", "{{slot3_name}}"),
            new(RmlUiBindingKind.Text, "text_slot3_hunter_2", "{{slot3_hunter}}"),
            new(RmlUiBindingKind.Text, "text_slot4_name_2", "{{slot4_name}}"),
            new(RmlUiBindingKind.Text, "text_slot4_hunter_2", "{{slot4_hunter}}"),
            new(RmlUiBindingKind.Text, "text_slot5_name_2", "{{slot5_name}}"),
            new(RmlUiBindingKind.Text, "text_slot5_hunter_2", "{{slot5_hunter}}"),
            new(RmlUiBindingKind.Text, "text_slot6_name_2", "{{slot6_name}}"),
            new(RmlUiBindingKind.Text, "text_slot6_hunter_2", "{{slot6_hunter}}"),
            new(RmlUiBindingKind.Text, "text_slot7_name_2", "{{slot7_name}}"),
            new(RmlUiBindingKind.Text, "text_slot7_hunter_2", "{{slot7_hunter}}"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_ready", "lobby_require_ready"),
            new(RmlUiBindingKind.Text, "lobby_ready", "{{lobby_ready_action}}"),
            new(RmlUiBindingKind.Text, "lobby_hunter", "HUNTER // {{lobby_local_hunter}}"),
            new(RmlUiBindingKind.Boolean, "visible:lobby_start", "lobby_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:lobby_start", "!lobby_can_start"),
        };

        private static readonly Binding[] RulesBindings =
        {
            new(RmlUiBindingKind.Text, "text_rules_map_1", "{{rules_map}}"),
            new(RmlUiBindingKind.Text, "text_rules_mode_1", "{{rules_mode}}"),
            new(RmlUiBindingKind.Text, "text_rules_format_1", "{{rules_format}}"),
            new(RmlUiBindingKind.Text, "text_rules_goal_label_1", "{{rules_goal_label}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle0_1", "{{rules_toggle0}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle1_1", "{{rules_toggle1}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle2_1", "{{rules_toggle2}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle3_1", "{{rules_toggle3}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle4_1", "{{rules_toggle4}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle5_1", "{{rules_toggle5}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle6_1", "{{rules_toggle6}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle7_1", "{{rules_toggle7}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle8_1", "{{rules_toggle8}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle9_1", "{{rules_toggle9}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle10_1", "{{rules_toggle10}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle11_1", "{{rules_toggle11}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle12_1", "{{rules_toggle12}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle13_1", "{{rules_toggle13}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle14_1", "{{rules_toggle14}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle15_1", "{{rules_toggle15}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle16_1", "{{rules_toggle16}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle17_1", "{{rules_toggle17}}"),
            new(RmlUiBindingKind.Text, "text_rules_toggle18_1", "{{rules_toggle18}}"),
            new(RmlUiBindingKind.Text, "rules_status", "{{rules_status}}"),
            new(RmlUiBindingKind.Boolean, "visible:visible_rules_owner_1", "rules_owner"),
            new(RmlUiBindingKind.Boolean, "visible:visible__rules_owner_1", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "visible:rules_apply", "rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_map_next", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_mode_next", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_format_next", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_time", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_goal", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle0", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle1", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle2", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle3", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle4", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle5", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle6", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle7", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle8", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle9", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle10", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle11", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle12", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle13", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle14", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle15", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle16", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle17", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_toggle18", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_classic", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_apply", "!rules_owner"),
        };

        private static readonly Binding[] MapBindings = BuildMapBindings();

        private static Binding[] BuildMapBindings()
        {
            var bindings = new List<Binding>
            {
                new(RmlUiBindingKind.Text, "map_status", "{{map_status}}"),
                new(RmlUiBindingKind.Text, "map_page", "{{map_page}}"),
                new(RmlUiBindingKind.Text, "map_empty", "{{map_empty_text}}"),
                new(RmlUiBindingKind.Boolean, "visible:map_empty", "map_empty"),
                new(RmlUiBindingKind.Boolean, "class:map_vanilla:selected", "map_vanilla_selected"),
                new(RmlUiBindingKind.Boolean, "class:map_community:selected", "map_community_selected"),
                new(RmlUiBindingKind.Boolean, "class:map_echoes:selected", "map_echoes_selected"),
                new(RmlUiBindingKind.Boolean, "class:map_halo:selected", "map_halo_selected"),
                new(RmlUiBindingKind.Boolean, "class:map_quake:selected", "map_quake_selected"),
                new(RmlUiBindingKind.Boolean, "disabled:map_previous", "!map_can_previous"),
                new(RmlUiBindingKind.Boolean, "disabled:map_next", "!map_can_next")
            };
            for (int i = 0; i < 6; i++)
            {
                bindings.Add(new(RmlUiBindingKind.Text, $"map_name{i}", $"{{{{map_choice{i}_name}}}}"));
                bindings.Add(new(RmlUiBindingKind.Text, $"map_hint{i}", $"{{{{map_choice{i}_hint}}}}"));
                bindings.Add(new(RmlUiBindingKind.Text, $"image:map_image{i}", $"{{{{map_choice{i}_image}}}}"));
                bindings.Add(new(RmlUiBindingKind.Boolean, $"visible:map_image{i}", $"map_choice{i}_image_ready"));
                bindings.Add(new(RmlUiBindingKind.Boolean, $"visible:map_art_label{i}", $"!map_choice{i}_image_ready"));
                bindings.Add(new(RmlUiBindingKind.Text, $"map_art_label{i}", $"{{{{map_choice{i}_preview_status}}}}"));
                bindings.Add(new(RmlUiBindingKind.Boolean, $"visible:map_choice{i}", $"map_choice{i}_visible"));
                bindings.Add(new(RmlUiBindingKind.Boolean, $"disabled:map_choice{i}", $"!map_choice{i}_enabled"));
            }
            return bindings.ToArray();
        }

        private static readonly Binding[] HunterIdentityBindings = BuildHunterIdentityBindings();

        private static Binding[] BuildHunterIdentityBindings()
        {
            var bindings = new List<Binding>
            {
                new(RmlUiBindingKind.Text, "hunter_name", "{{hunter_identity_name}}"),
                new(RmlUiBindingKind.Text, "hunter_status", "{{hunter_status}}"),
                new(RmlUiBindingKind.Text, "hunter_restriction", "{{hunter_restriction}}"),
                new(RmlUiBindingKind.Boolean, "disabled:hunter_apply", "!hunter_apply_enabled"),
                new(RmlUiBindingKind.Boolean, "disabled:hunter_random", "!hunter_random_allowed")
            };
            for (int i = 0; i < 7; i++)
            {
                bindings.Add(new(RmlUiBindingKind.Boolean, $"class:hunter_choice{i}:selected",
                    $"hunter_choice{i}_selected"));
                bindings.Add(new(RmlUiBindingKind.Boolean, $"disabled:hunter_choice{i}",
                    $"!hunter_choice{i}_allowed"));
            }
            for (int i = 0; i < 4; i++)
                bindings.Add(new(RmlUiBindingKind.Boolean, $"class:hunter_suit{i}:selected",
                    $"hunter_suit{i}_selected"));
            return bindings.ToArray();
        }

        private static readonly Binding[] AdminBindings = BuildAdminBindings();

        private static Binding[] BuildAdminBindings()
        {
            var values = new List<Binding>();
            foreach (string id in new[]
            {
                "admin_title", "admin_status", "admin_selected_name",
                "admin_selected_details", "admin_handicap", "admin_bot_hunter",
                "admin_bot_suit", "admin_bot_level", "admin_teams", "admin_team_lock",
                "admin_map_status", "admin_rotation_state", "admin_permission_reason",
                "admin_chat_history", "admin_chat_hint", "admin_confirmation_text"
            })
                values.Add(new(RmlUiBindingKind.Text, id, "{{" + id + "}}"));

            values.Add(new(RmlUiBindingKind.Boolean,
                "visible:admin_confirmation", "admin_confirmation_open"));
            foreach (string name in new[]
            {
                "team", "handicap", "kick", "transfer", "bot_add", "bot_apply",
                "bot_remove", "bot_hunter", "bot_suit", "bot_level", "team_auto",
                "team_lock", "map_retry", "start", "close_lobby", "chat_send"
            })
            {
                string key = name is "bot_hunter" or "bot_suit" or "bot_level"
                    ? "admin_bot_options_enabled"
                    : name == "team_auto" ? "admin_team_enabled"
                    : "admin_" + name + "_enabled";
                values.Add(new(RmlUiBindingKind.Boolean, "disabled:admin_" + name, "!" + key));
            }
            values.Add(new(RmlUiBindingKind.Boolean, "disabled:admin_close", "admin_confirmation_open"));
            values.Add(new(RmlUiBindingKind.Boolean, "disabled:admin_chat_input", "admin_confirmation_open"));
            for (int i = 0; i < 8; i++)
            {
                values.Add(new(RmlUiBindingKind.Boolean,
                    $"visible:admin_slot{i}", $"admin_slot{i}_present"));
                values.Add(new(RmlUiBindingKind.Boolean,
                    $"disabled:admin_slot{i}", $"!admin_slot{i}_present"));
                values.Add(new(RmlUiBindingKind.Boolean,
                    $"class:admin_slot{i}:selected", $"admin_slot{i}_selected"));
                values.Add(new(RmlUiBindingKind.Text,
                    $"admin_player{i}_name", $"{{{{admin_player{i}_name}}}}"));
                values.Add(new(RmlUiBindingKind.Text,
                    $"admin_player{i}_details", $"{{{{admin_player{i}_details}}}}"));
            }
            for (int i = 0; i < 4; i++)
            {
                values.Add(new(RmlUiBindingKind.Text,
                    $"admin_team{i}", $"{{{{admin_team{i}_label}}}}"));
                values.Add(new(RmlUiBindingKind.Boolean,
                    $"visible:admin_team{i}", $"admin_team{i}_present"));
                values.Add(new(RmlUiBindingKind.Boolean,
                    $"disabled:admin_team{i}", $"!admin_team{i}_enabled"));
                values.Add(new(RmlUiBindingKind.Boolean,
                    $"class:admin_team{i}:selected", $"admin_team{i}_selected"));
            }
            return values.ToArray();
        }

        private static readonly Binding[] OfflineBindings =
        {
            new(RmlUiBindingKind.Boolean, "visible:offline_match_panel", "!offline_training_mode"),
            new(RmlUiBindingKind.Boolean, "visible:offline_training_panel", "offline_training_mode"),
            new(RmlUiBindingKind.Text, "offline_eyebrow", "{{offline_eyebrow_label}}"),
            new(RmlUiBindingKind.Text, "offline_heading", "{{offline_heading_label}}"),
            new(RmlUiBindingKind.Text, "offline_description", "{{offline_description_label}}"),
            new(RmlUiBindingKind.Text, "offline_arena_value", "{{offline_arena_value}}"),
            new(RmlUiBindingKind.Text, "offline_mode_value", "{{offline_mode_value}}"),
            new(RmlUiBindingKind.Text, "offline_matchup_value", "{{offline_matchup_value}}"),
            new(RmlUiBindingKind.Text, "offline_bots_value", "{{offline_bots_value}}"),
            new(RmlUiBindingKind.Text, "offline_skill_value", "{{offline_skill_value}}"),
            new(RmlUiBindingKind.Text, "offline_hunter_value", "{{offline_hunter_value}}"),
            new(RmlUiBindingKind.Text, "offline_suit_value", "{{offline_suit_value}}"),
            new(RmlUiBindingKind.Text, "offline_character_value", "{{offline_character_value}}"),
            new(RmlUiBindingKind.Text, "offline_character_notice", "{{offline_character_notice}}"),
            new(RmlUiBindingKind.Text, "offline_start", "{{offline_start_label}}"),
            new(RmlUiBindingKind.Text, "offline_summary", "{{offline_summary}}"),
            new(RmlUiBindingKind.Text, "offline_error", "{{offline_error}}"),
            new(RmlUiBindingKind.Text, "offline_availability", "{{offline_availability}}"),
            new(RmlUiBindingKind.Boolean, "disabled:offline_start", "!offline_can_start"),
            new(RmlUiBindingKind.Text, "offline_training_hunter_value", "{{offline_training_hunter_value}}"),
            new(RmlUiBindingKind.Text, "offline_drill_value", "{{offline_drill_value}}"),
            new(RmlUiBindingKind.Text, "offline_weapon_value", "{{offline_weapon_value}}"),
            new(RmlUiBindingKind.Text, "offline_duration_value", "{{offline_duration_value}}"),
            new(RmlUiBindingKind.Text, "offline_targets_value", "{{offline_targets_value}}"),
            new(RmlUiBindingKind.Text, "offline_movement_value", "{{offline_movement_value}}"),
            new(RmlUiBindingKind.Boolean, "disabled:offline_training_start", "!offline_training_can_start"),
            new(RmlUiBindingKind.Boolean, "disabled:offline_targets", "!offline_training_can_targets"),
            new(RmlUiBindingKind.Boolean, "disabled:offline_weapon", "!offline_training_can_weapon")
        };

        private static readonly Binding[] OfflineRulesBindings = BuildOfflineRulesBindings();

        private static Binding[] BuildOfflineRulesBindings()
        {
            var bindings = new List<Binding>
            {
                new(RmlUiBindingKind.Text, "offline_rules_error", "{{offline_rules_error}}"),
                new(RmlUiBindingKind.Text, "offline_damage_value", "{{offline_damage_value}}"),
                new(RmlUiBindingKind.Boolean, "disabled:offline_rules_apply", "offline_rules_busy"),
                new(RmlUiBindingKind.Boolean, "disabled:offline_damage_value", "offline_rules_busy"),
                new(RmlUiBindingKind.Boolean, "disabled:offline_point_goal", "offline_rules_busy"),
                new(RmlUiBindingKind.Boolean, "disabled:offline_time_limit", "offline_rules_busy"),
                new(RmlUiBindingKind.Boolean, "disabled:offline_time_goal", "offline_rules_busy"),
                new(RmlUiBindingKind.Boolean, "visible:offline_point_goal_row", "offline_point_goal_visible"),
                new(RmlUiBindingKind.Boolean, "visible:offline_time_limit_row", "offline_time_limit_visible"),
                new(RmlUiBindingKind.Boolean, "visible:offline_time_goal_row", "offline_time_goal_visible"),
                new(RmlUiBindingKind.Boolean, "visible:offline_chamber_help", "offline_chamber_help_visible"),
                new(RmlUiBindingKind.Boolean, "visible:offline_rule_1", "offline_fire_visible"),
                new(RmlUiBindingKind.Boolean, "visible:offline_rule_0", "offline_auto_reset_visible"),
                new(RmlUiBindingKind.Boolean, "visible:offline_rule_13", "offline_movement_visible"),
                new(RmlUiBindingKind.Boolean, "visible:offline_movement_help", "offline_movement_visible")
            };
            for (int i = 0; i < RmlUiIntentRegistry.OfflineRuleChoiceCount; i++)
                if (i != 5) // One in the Chamber is a mode, not an independent toggle.
                {
                    bindings.Add(new(RmlUiBindingKind.Text, $"offline_rule_{i}", "{{offline_rule" + i + "_label}}"));
                    bindings.Add(new(RmlUiBindingKind.Boolean, $"disabled:offline_rule_{i}", "offline_rules_busy"));
                }
            return bindings.ToArray();
        }

        private static readonly Binding[] OfflineTrainingBindings = BuildOfflineTrainingBindings();

        private static Binding[] BuildOfflineTrainingBindings()
        {
            var bindings = new List<Binding>
            {
                new(RmlUiBindingKind.Text, "offline_training_difficulty_value", "{{offline_training_difficulty_value}}"),
                new(RmlUiBindingKind.Text, "offline_training_distance_value", "{{offline_training_distance_value}}"),
                new(RmlUiBindingKind.Text, "offline_training_scope_value", "{{offline_training_scope_value}}"),
                new(RmlUiBindingKind.Boolean, "disabled:offline_training_toggle_0", "!offline_training_can_headshots")
            };
            for (int i = 0; i < 5; i++)
                bindings.Add(new(RmlUiBindingKind.Text,
                    $"offline_training_toggle_{i}", "{{offline_training_toggle" + i + "}}"));
            return bindings.ToArray();
        }

        private static readonly Binding[] OfflineArenaBindings = BuildOfflineArenaBindings();

        private static Binding[] BuildOfflineArenaBindings()
        {
            var bindings = new List<Binding>
            {
                new(RmlUiBindingKind.Text, "offline_arena_page", "{{offline_arena_page}}"),
                new(RmlUiBindingKind.Text, "offline_arena_picker_error", "{{offline_arena_picker_error}}"),
                new(RmlUiBindingKind.Boolean, "disabled:offline_arena_previous", "!offline_arena_can_previous"),
                new(RmlUiBindingKind.Boolean, "disabled:offline_arena_next", "!offline_arena_can_next")
            };
            for (int i = 0; i < 16; i++)
            {
                bindings.Add(new(RmlUiBindingKind.Text,
                    $"offline_arena_name_{i}", "{{offline_arena_name" + i + "}}"));
                bindings.Add(new(RmlUiBindingKind.Text,
                    $"offline_arena_reason_{i}", "{{offline_arena_reason" + i + "}}"));
                bindings.Add(new(RmlUiBindingKind.Boolean,
                    $"visible:offline_arena_row_{i}", $"offline_arena_row{i}_present"));
                bindings.Add(new(RmlUiBindingKind.Boolean,
                    $"disabled:offline_arena_pick_{i}", $"!offline_arena_row{i}_present"));
            }
            return bindings.ToArray();
        }

        private static readonly Binding[] AdventureBindings = BuildAdventureBindings();

        private static Binding[] BuildAdventureBindings()
        {
            var values = new List<Binding>
            {
                new(RmlUiBindingKind.Text, "adventure_selected_slot", "{{adventure_selected_slot}}"),
                new(RmlUiBindingKind.Text, "adventure_save_detail", "{{adventure_save_detail}}"),
                new(RmlUiBindingKind.Text, "adventure_hunter", "{{adventure_hunter}}"),
                new(RmlUiBindingKind.Text, "adventure_launch_status", "{{adventure_launch_status}}"),
                new(RmlUiBindingKind.Text, "adventure_copy_destination", "{{adventure_copy_destination}}"),
                new(RmlUiBindingKind.Boolean, "disabled:adventure_copy", "!adventure_can_copy"),
                new(RmlUiBindingKind.Boolean, "disabled:adventure_archive", "!adventure_can_archive"),
                new(RmlUiBindingKind.Boolean, "visible:adventure_cancel_save_work", "adventure_save_busy"),
                new(RmlUiBindingKind.Boolean, "disabled:adventure_next_hunter", "adventure_save_busy"),
                new(RmlUiBindingKind.Boolean, "visible:adventure_ship", "adventure_ship_available"),
                new(RmlUiBindingKind.Text, "adventure_error_text", "{{adventure_error_text}}"),
                new(RmlUiBindingKind.Text, "adventure_file_problem", "{{adventure_file_problem}}"),
                new(RmlUiBindingKind.Boolean, "visible:adventure_files_error", "adventure_game_files_missing"),
                new(RmlUiBindingKind.Boolean, "visible:adventure_error", "adventure_error_visible"),
                new(RmlUiBindingKind.Boolean, "disabled:adventure_continue", "!adventure_can_resume"),
                new(RmlUiBindingKind.Boolean, "disabled:adventure_new_run", "!adventure_can_new_run")
            };
            for (int i = 1; i <= 3; i++)
            {
                values.Add(new(RmlUiBindingKind.Text, "adventure_slot" + i + "_label",
                    "{{adventure_slot" + i + "_label}}"));
                values.Add(new(RmlUiBindingKind.Boolean,
                    "class:adventure_slot" + i + ":selected",
                    "adventure_slot" + i + "_selected"));
                values.Add(new(RmlUiBindingKind.Boolean, "disabled:adventure_slot" + i, "adventure_save_busy"));
                values.Add(new(RmlUiBindingKind.Boolean, "class:adventure_destination" + i + ":selected", "adventure_destination" + i + "_selected"));
                values.Add(new(RmlUiBindingKind.Boolean, "disabled:adventure_destination" + i, "adventure_destination" + i + "_disabled"));
            }
            return values.ToArray();
        }

        private static readonly Binding[] AdventureConfirmBindings =
        {
            new(RmlUiBindingKind.Text, "adventure_confirmation_kind", "{{adventure_confirmation_kind}}"),
            new(RmlUiBindingKind.Text, "adventure_confirmation_description", "{{adventure_confirmation_description}}"),
            new(RmlUiBindingKind.Boolean, "visible:adventure_cancel_new_run", "adventure_new_run_confirmation"),
            new(RmlUiBindingKind.Boolean, "visible:adventure_confirm_new_run", "adventure_new_run_confirmation"),
            new(RmlUiBindingKind.Boolean, "visible:adventure_cancel_save_change", "adventure_management_confirmation"),
            new(RmlUiBindingKind.Boolean, "visible:adventure_confirm_copy", "adventure_copy_confirmation"),
            new(RmlUiBindingKind.Boolean, "visible:adventure_confirm_archive", "adventure_archive_confirmation"),
            new(RmlUiBindingKind.Text, "adventure_overwrite_title", "{{adventure_overwrite_title}}"),
            new(RmlUiBindingKind.Text, "adventure_confirmation_error_text",
                "{{adventure_confirmation_error_text}}")
        };

        private static readonly Binding[] TheatreBindings = BuildTheatreBindings();

        private static Binding[] BuildTheatreBindings()
        {
            var bindings = new List<Binding>();
            foreach (string field in new[]
            {
                "theatre_summary", "theatre_empty", "theatre_filter_label",
                "theatre_sort_label", "theatre_page_number", "theatre_status",
                "theatre_error", "theatre_selected_title", "theatre_metadata",
                "theatre_hero", "theatre_no_preview", "theatre_launch_problem", "theatre_delete_prompt",
                "theatre_studio", "theatre_workflow_hint"
            })
                bindings.Add(new(RmlUiBindingKind.Text, field, "{{" + field + "}}"));
            foreach (var (target, expr) in new[]
            {
                ("theatre_watch", "!theatre_watch_enabled"),
                ("theatre_studio", "!theatre_watch_enabled"),
                ("theatre_favorite", "!theatre_favorite_enabled"),
                ("theatre_previous_page", "!theatre_can_previous"),
                ("theatre_next_page", "!theatre_can_next"),
                ("theatre_cancel_job", "!theatre_busy"),
                ("theatre_validate", "!theatre_edit_enabled"), ("theatre_recover", "!theatre_recover_enabled"),
                ("theatre_export", "!theatre_export_enabled"), ("theatre_rename", "!theatre_edit_enabled"),
                ("theatre_rename_apply", "!theatre_edit_enabled"), ("theatre_tags", "!theatre_edit_enabled"),
                ("theatre_collections", "!theatre_edit_enabled"), ("theatre_organize", "!theatre_edit_enabled"),
                ("theatre_delete", "!theatre_edit_enabled"), ("theatre_import_path", "!theatre_import_enabled"),
                ("theatre_import_path_apply", "!theatre_import_enabled")
            })
                bindings.Add(new(RmlUiBindingKind.Boolean, "disabled:" + target, expr));
            bindings.Add(new(RmlUiBindingKind.Boolean, "visible:theatre_delete_confirmation", "theatre_delete_confirmation"));
            foreach (string notice in new[] { "theatre_empty", "theatre_status", "theatre_error", "theatre_launch_problem" })
                bindings.Add(new(RmlUiBindingKind.Boolean, "visible:" + notice, notice + "_visible"));
            bindings.Add(new(RmlUiBindingKind.Boolean, "visible:theatre_cancel_job", "theatre_busy"));
            bindings.Add(new(RmlUiBindingKind.Boolean, "visible:theatre_recover", "theatre_advanced_tools"));
            bindings.Add(new(RmlUiBindingKind.Boolean, "visible:theatre_export", "theatre_advanced_tools"));
            for (int i = 0; i < 8; i++)
            {
                bindings.Add(new(RmlUiBindingKind.Text, $"theatre_entry{i}_title",
                    $"{{{{theatre_entry{i}_title}}}}"));
                bindings.Add(new(RmlUiBindingKind.Text, $"theatre_entry{i}_detail",
                    $"{{{{theatre_entry{i}_detail}}}}"));
                bindings.Add(new(RmlUiBindingKind.Boolean, $"visible:theatre_entry{i}",
                    $"theatre_entry{i}_present"));
                bindings.Add(new(RmlUiBindingKind.Boolean, $"disabled:theatre_entry{i}",
                    $"!theatre_entry{i}_present"));
                bindings.Add(new(RmlUiBindingKind.Boolean, $"class:theatre_entry{i}:selected",
                    $"theatre_entry{i}_selected"));
            }
            return bindings.ToArray();
        }

        public static IReadOnlyDictionary<string, RmlUiBindingValue> Project(RmlUiMenuPage page,
            IReadOnlyDictionary<string, RmlUiBindingValue> model)
        {
            ArgumentNullException.ThrowIfNull(model);
            var output = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal);
            if (page is not (RmlUiMenuPage.Rules or RmlUiMenuPage.Maps or RmlUiMenuPage.Hunters or RmlUiMenuPage.Admin or RmlUiMenuPage.OfflineArena or RmlUiMenuPage.OfflineTraining or RmlUiMenuPage.OfflineRules or RmlUiMenuPage.AdventureConfirm or RmlUiMenuPage.PartyQueue)) Add(ShellBindings);
            Add(page switch
            {
                RmlUiMenuPage.Home => HomeBindings, RmlUiMenuPage.Play => PlayBindings,
                RmlUiMenuPage.Lobby => LobbyBindings, RmlUiMenuPage.Rules => RulesBindings,
                RmlUiMenuPage.Offline => OfflineBindings, RmlUiMenuPage.OfflineArena => OfflineArenaBindings,
                RmlUiMenuPage.OfflineTraining => OfflineTrainingBindings,
                RmlUiMenuPage.OfflineRules => OfflineRulesBindings,
                RmlUiMenuPage.Adventure => AdventureBindings,
                RmlUiMenuPage.AdventureConfirm => AdventureConfirmBindings,
                RmlUiMenuPage.Theatre => TheatreBindings,
                RmlUiMenuPage.PartyQueue => PartyQueueBindings,
                RmlUiMenuPage.Maps => MapBindings, RmlUiMenuPage.Hunters => HunterIdentityBindings,
                RmlUiMenuPage.Admin => AdminBindings,
                _ => throw new ArgumentOutOfRangeException(nameof(page))
            });
            return new ReadOnlyDictionary<string, RmlUiBindingValue>(output);

            void Add(Binding[] bindings)
            {
                foreach (var binding in bindings)
                    output.Add(binding.Target, binding.Kind == RmlUiBindingKind.Text
                        ? RmlUiBindingValue.FromText(Interpolate(binding.Expression, model))
                        : RmlUiBindingValue.FromBoolean(Evaluate(binding.Expression, model)));
            }
        }

        public static IReadOnlyDictionary<string, RmlUiBindingValue> ProjectShell(
            IReadOnlyDictionary<string, RmlUiBindingValue> model)
        {
            ArgumentNullException.ThrowIfNull(model);
            var output = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal);
            foreach (var binding in ShellBindings)
                output.Add(binding.Target, binding.Kind == RmlUiBindingKind.Text
                    ? RmlUiBindingValue.FromText(Interpolate(binding.Expression, model))
                    : RmlUiBindingValue.FromBoolean(Evaluate(binding.Expression, model)));
            return new ReadOnlyDictionary<string, RmlUiBindingValue>(output);
        }

        private static bool Evaluate(string expression, IReadOnlyDictionary<string, RmlUiBindingValue> model)
        {
            if (expression.StartsWith("!", StringComparison.Ordinal)) return !Evaluate(expression[1..], model);
            int equals = expression.IndexOf(" == ", StringComparison.Ordinal);
            if (equals >= 0)
                return String.Equals(Text(expression[..equals], model), expression[(equals + 4)..], StringComparison.Ordinal);
            return model.TryGetValue(expression, out var value)
                && (value.Kind == RmlUiBindingKind.Boolean ? value.Boolean : value.Text is "true" or "1");
        }

        private static string Text(string key, IReadOnlyDictionary<string, RmlUiBindingValue> model)
        {
            if (!model.TryGetValue(key, out var value)) return "";
            return value.Kind == RmlUiBindingKind.Boolean ? (value.Boolean ? "true" : "false") : value.Text;
        }

        private static string Interpolate(string expression, IReadOnlyDictionary<string, RmlUiBindingValue> model)
        {
            var result = new StringBuilder(expression.Length);
            int cursor = 0;
            while (cursor < expression.Length)
            {
                int start = expression.IndexOf("{{", cursor, StringComparison.Ordinal);
                if (start < 0) { result.Append(expression, cursor, expression.Length - cursor); break; }
                result.Append(expression, cursor, start - cursor);
                int end = expression.IndexOf("}}", start + 2, StringComparison.Ordinal);
                if (end < 0) throw new InvalidOperationException("Unterminated authored text binding.");
                result.Append(Text(expression[(start + 2)..end], model));
                cursor = end + 2;
            }
            return result.ToString();
        }
    }
}
