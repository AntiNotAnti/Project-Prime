using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace MphRead.Mods.Launcher.RmlUi.Host
{
    public enum RmlUiMenuPage { Home, Play, Lobby, Rules }

    public static class RmlUiMenuPages
    {
        public static readonly RmlUiPageSpec Home = new("home", "pages/home/home.rml", "activity_selector");
        public static readonly RmlUiPageSpec Play = new("play", "pages/play/browser.rml", "play_quick");
        public static readonly RmlUiPageSpec Lobby = new("lobby", "pages/lobby/lobby.rml", "lobby_ready");
        public static readonly RmlUiPageSpec Rules = new("rules", "pages/lobby/rules.rml", "rules_close_top");

        public static RmlUiPageSpec Spec(RmlUiMenuPage page) => page switch
        {
            RmlUiMenuPage.Home => Home, RmlUiMenuPage.Play => Play,
            RmlUiMenuPage.Lobby => Lobby, RmlUiMenuPage.Rules => Rules,
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
            new(RmlUiBindingKind.Boolean, "disabled:profile", "lobby_mode"),
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
            new(RmlUiBindingKind.Text, "home_party_state", "{{home_party_state}}"),
            new(RmlUiBindingKind.Text, "home_social_summary", "{{home_social_summary}}"),
            new(RmlUiBindingKind.Text, "home_signal_activity", "{{activity_title}}"),
            new(RmlUiBindingKind.Text, "home_signal_hunter", "{{hunter_name}}"),
            new(RmlUiBindingKind.Text, "home_signal_status", "{{activity_hint}}"),
            new(RmlUiBindingKind.Text, "home_friends_online", "{{home_friends_online}}"),
            new(RmlUiBindingKind.Text, "home_invites_count", "{{home_invites_count}}"),
            new(RmlUiBindingKind.Text, "home_requests_count", "{{home_requests_count}}"),
            new(RmlUiBindingKind.Boolean, "visible:home_friend0", "home_friend0_visible"),
            new(RmlUiBindingKind.Text, "home_friend0_name", "{{home_friend0_name}}"),
            new(RmlUiBindingKind.Text, "home_friend0_state", "{{home_friend0_state}}"),
            new(RmlUiBindingKind.Boolean, "visible:home_friend1", "home_friend1_visible"),
            new(RmlUiBindingKind.Text, "home_friend1_name", "{{home_friend1_name}}"),
            new(RmlUiBindingKind.Text, "home_friend1_state", "{{home_friend1_state}}"),
            new(RmlUiBindingKind.Boolean, "visible:home_friend2", "home_friend2_visible"),
            new(RmlUiBindingKind.Text, "home_friend2_name", "{{home_friend2_name}}"),
            new(RmlUiBindingKind.Text, "home_friend2_state", "{{home_friend2_state}}"),
            new(RmlUiBindingKind.Boolean, "visible:home_social_empty", "home_social_empty"),
        };

        private static readonly Binding[] PlayBindings =
        {
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
        };

        private static readonly Binding[] LobbyBindings =
        {
            new(RmlUiBindingKind.Text, "lobby_time", "{{lobby_time}}"),
            new(RmlUiBindingKind.Text, "lobby_score", "{{lobby_score}}"),
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
            new(RmlUiBindingKind.Boolean, "disabled:rules_classic", "!rules_owner"),
            new(RmlUiBindingKind.Boolean, "disabled:rules_apply", "!rules_owner"),
        };

        public static IReadOnlyDictionary<string, RmlUiBindingValue> Project(RmlUiMenuPage page,
            IReadOnlyDictionary<string, RmlUiBindingValue> model)
        {
            ArgumentNullException.ThrowIfNull(model);
            var output = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal);
            if (page != RmlUiMenuPage.Rules) Add(ShellBindings);
            Add(page switch
            {
                RmlUiMenuPage.Home => HomeBindings, RmlUiMenuPage.Play => PlayBindings,
                RmlUiMenuPage.Lobby => LobbyBindings, RmlUiMenuPage.Rules => RulesBindings,
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
