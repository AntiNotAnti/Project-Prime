#!/usr/bin/env python3
"""Exercise shipped RmlUi documents through the real native bridge.

CPU backend 1 captures real RmlUi draw lists without a graphics context. Optional
Windows/macOS GLFW captures use backend 0 and an actual hidden OpenGL 2.1 context.
Fixture publications stand in for service data; they are not physical input or
end-to-end service tests. Failures are collected so old and new assets can use
exactly the same harness.
"""
import argparse
import ctypes as C
import hashlib
import json
import math
import os
import re
from pathlib import Path
import struct
import sys
import time
import zlib

SIZES = [(1280, 720), (1920, 1080), (2560, 1440), (3840, 2160),
         (1280, 800), (2560, 1080), (3440, 1440), (800, 600), (640, 360), (480, 320)]
# SIZES are framebuffer pixels at density 1. The three HiDPI cases retain
# useful logical dimensions rather than halving already-small viewports.
CASES = [(w, h, 1.0) for w, h in SIZES] + [(1280, 720, 2.0), (2560, 1440, 2.0), (3840, 2160, 2.0), (1920, 1080, 1.5)]
FIXTURES = {
    'pages/home/home.rml': ['activity_selector', 'deploy', 'home_feature_open', 'home_social_open', 'profile', 'header_settings'],
    'pages/lobby/rules.rml': ['rules_map_next', 'rules_time', 'rules_goal', 'rules_toggle0', 'rules_toggle18', 'rules_classic', 'rules_cancel', 'rules_apply'],
    'pages/lobby/lobby.rml': ['lobby_player0', 'lobby_player7', 'lobby_match_rules', 'lobby_chat_input', 'lobby_chat_send', 'lobby_ready', 'lobby_start', 'lobby_leave'],
    'pages/hunters/native-picker.rml': ['hunter_choice0', 'hunter_skin_option6', 'hunter_category4', 'native_hunter_status', 'hunter_apply', 'hunter_cancel'],
    'pages/community/native-discover.rml': ['native_community_public', 'native_community_find', 'native_community_row0', 'native_community_row7', 'native_community_install', 'native_community_back'],
    'pages/license/native-overview.rml': ['native_license_close', 'native_license_refresh'],
    'pages/social/social.rml': ['social_friends', 'social_search', 'social_row_7', 'social_command_29', 'social_privacy_toggle', 'social_back'],
    'pages/social/native-party.rml': ['party_members', 'party_invite', 'party_row_0', 'party_row_7', 'party_follow', 'party_publish', 'party_back'],
    'pages/settings/quick.rml': ['quick_motion', 'quick_apply', 'quick_close'],
    'pages/settings/native-full.rml': ['native_settings_category_0', 'native_settings_value_0', 'native_settings_value_19', 'native_settings_apply'],
    'pages/updates/versions.rml': ['versions_row_0', 'versions_close'],
    'pages/adventure/saves.rml': ['adventure_slot1', 'adventure_continue', 'adventure_copy', 'adventure_back'],
    'pages/adventure/confirm.rml': ['adventure_cancel_new_run', 'adventure_confirm_new_run'],
    'pages/adventure/ship.rml': ['ship_open_map', 'ship_exit', 'ship_back'],
    'pages/adventure/ending.rml': ['campaign_ending_continue'],
    'pages/offline/setup.rml': ['offline_arena', 'offline_rules_open', 'offline_start', 'offline_back'],
    'pages/offline/training.rml': ['offline_training_difficulty', 'offline_training_toggle_0', 'offline_training_toggle_4', 'offline_training_done'],
    'pages/offline/rules.rml': ['offline_point_goal', 'offline_rule_0', 'offline_rules_apply', 'offline_rules_cancel'],
    'pages/offline/arena.rml': ['offline_arena_search', 'offline_arena_pick_0', 'offline_arena_pick_15', 'offline_arena_cancel'],
    'pages/play/browser.rml': ['play_quick', 'play_entry0', 'play_entry7', 'play_join', 'play_back'],
    'pages/play/queue.rml': ['play_queue_accept', 'play_queue_decline', 'play_queue_leave'],
    'pages/news/news.rml': ['news_filter_0', 'news_read', 'news_article_3', 'news_back'],
    'pages/news/detail.rml': ['news_detail_close'],
    'pages/updates/available.rml': ['update_later', 'update_confirm'],
    'pages/common/quit.rml': ['quit_cancel', 'quit_confirm'],
    'pages/ingame/pause.rml': ['ingame_resume', 'ingame_settings', 'ingame_quit'],
    'pages/ingame/confirm.rml': ['ingame_cancel', 'ingame_confirm'],
    'pages/ingame/journal.rml': ['journal_map', 'journal_category', 'journal_next', 'journal_close'],
    'pages/setup/native-game-files.rml': ['native_setup_choose', 'native_setup_previews', 'native_setup_back'],
    'pages/setup/native-game-files-confirm.rml': ['native_setup_decline', 'native_setup_confirm'],
    'pages/theatre/confirm-delete.rml': ['theatre_cancel_delete', 'theatre_confirm_delete'],
    'pages/theatre/library.rml': ['theatre_search_submit', 'theatre_entry0', 'theatre_entry7', 'theatre_watch', 'theatre_studio'],
    'pages/theatre/playback.rml': ['replay_pause', 'replay_position', 'replay_rate', 'replay_studio', 'replay_back'],
    'pages/studio/native-entry.rml': ['native_studio_theatre', 'native_studio_map', 'native_studio_chooser_back'],
    'pages/theatre/studio-workspace.rml': ['studio_open', 'studio_save_as', 'studio_settings', 'replay_pause', 'studio_frame', 'studio_seek', 'studio_tab_camera', 'replay_back'],
}


# Alternate states use the same authored documents and service-owned IDs. They
# test local presentation only, without issuing network/account/game actions.
SCENARIOS = {
    'pages/community/native-discover.rml': {
        'empty': ['native_community_search', 'native_community_find', 'native_community_back'],
        'loading': ['native_community_cancel_install', 'native_community_back'],
        'large-paginated-collection': ['native_community_next', 'native_community_row7', 'native_community_install', 'native_community_back'],
    },
    'pages/social/native-party.rml': {
        'one-member': ['party_row_0', 'party_invite', 'party_back'],
        'four-members': ['party_row_0', 'party_row_3', 'party_invite', 'party_back'],
        'empty': ['party_invite', 'party_back'],
    },
    'pages/offline/setup.rml': {'aim-lab': ['offline_weapon', 'offline_targets', 'offline_training_options', 'offline_training_start', 'offline_back']},
    'pages/play/browser.rml': {'create-lobby': ['play_create_name', 'play_next_map', 'play_next_mode', 'play_create_submit', 'play_create_cancel']},
    'pages/license/native-overview.rml': {'account-long-status': ['native_license_email', 'native_license_status', 'native_license_close']},
}


def seed_scenario(bridge, doc, path, scenario, touch=False, large_text=False):
    seed(bridge, doc, path, touch=touch, large_text=large_text)
    if path == 'pages/community/native-discover.rml':
        if scenario in ['empty', 'loading']:
            for i in range(8): bridge.boolean(doc, f'visible:native_community_row{i}', False)
            bridge.boolean(doc, 'visible:native_community_notice', True)
            bridge.text(doc, 'native_community_title', 'NO COMMUNITY MAP SELECTED')
            bridge.text(doc, 'native_community_description', '')
            bridge.boolean(doc, 'visible:native_community_technical', False)
            bridge.text(doc, 'native_community_notice', 'No compatible packages match your filters.' if scenario == 'empty' else 'Downloading the selected verified package. You can cancel this transfer.')
            bridge.boolean(doc, 'visible:native_community_cancel_install', scenario == 'loading')
        if scenario == 'large-paginated-collection':
            bridge.text(doc, 'native_community_status', 'PAGE 1 OF 128 // 1,024 COMMUNITY PACKAGES // 8 RESULTS ON THIS PAGE')
    if path == 'pages/social/native-party.rml':
        count = {'one-member': 1, 'four-members': 4, 'empty': 0}[scenario]
        for i in range(8):
            bridge.boolean(doc, f'visible:party_row_{i}', i < count)
            if i == 0: bridge.text(doc, 'party_name_0', 'Samus')
        bridge.boolean(doc, 'visible:party_empty', count == 0)
        bridge.text(doc, 'party_summary', f'{count} / 8 MEMBERS // SYNTHETIC LOCAL PRESENTATION')
    if scenario == 'aim-lab':
        bridge.boolean(doc, 'visible:offline_match_panel', False)
        bridge.boolean(doc, 'visible:offline_training_panel', True)
        for key, value in {'offline_heading': 'AIM LAB', 'offline_training_hunter_value': 'SAMUS', 'offline_drill_value': 'MULTI TARGET FLICK',
                           'offline_weapon_value': 'IMPERIALIST', 'offline_duration_value': '5 MINUTES', 'offline_targets_value': '5 TARGETS',
                           'offline_movement_value': 'GROUND AND ELEVATED'}.items(): bridge.text(doc, key, value)
    if scenario == 'create-lobby':
        for element in ['play_browser', 'play_connect']: bridge.boolean(doc, 'visible:' + element, False)
        bridge.boolean(doc, 'visible:play_create', True)
        for key, value in {'play_create_map_label': 'ALIMBIC CITADEL', 'play_create_mode_label': 'BATTLE',
                           'text_play_create_host_1': 'THIS COMPUTER', 'play_create_capacity': '8 HUNTERS // PUBLIC LOBBY',
                           'play_create_source': 'VERIFIED LOCAL ARENA PACKAGE'}.items(): bridge.text(doc, key, value)
    if scenario == 'account-long-status':
        bridge.boolean(doc, 'visible:native_license_face_overview', False)
        bridge.boolean(doc, 'visible:native_license_face_account', True)
        bridge.text(doc, 'native_license_status', 'Verification email requested. Check your inbox to finish verifying this account. ' + LONG_DESCRIPTION * 3)
    for element in SCENARIOS[path][scenario]: bridge.document_set_enabled(doc, encode(element), 1)


def scenario_checks(bridge, doc, path, scenario, width, height, density, touch=False):
    checks = []
    viewport = [0, 0, width, height]
    actions = SCENARIOS[path][scenario]
    primary = actions[-2:] if scenario == 'create-lobby' else actions[-1:]
    if width/density >= 1920 and height/density >= 1080 and not touch:
        for element in primary:
            box = bridge.bounds(doc, element)
            assertion(checks, 'scenario_primary_visible_without_focus_scroll:' + element, inside(box, viewport), bounds=box)
    for element in actions:
        focused = bool(bridge.document_focus(doc, encode(element)))
        bridge.update()
        box = bridge.bounds(doc, element)
        assertion(checks, 'scenario_action_reachable:' + element, focused and inside(box, viewport), bounds=box)
        assertion(checks, 'scenario_actual_focus:' + element, bridge.focused() == element, actual=bridge.focused())
        if touch and path == 'pages/license/native-overview.rml' and element != 'native_license_status':
            assertion(checks, 'scenario_touch_48dp:' + element, bool(box) and box[3] >= 48*density-2, bounds=box)
    return checks


def encode(value):
    return str(value).encode('utf-8')


def milliseconds(call):
    start = time.perf_counter_ns()
    result = call()
    return result, (time.perf_counter_ns() - start) / 1_000_000


class Bridge:
    def __init__(self, path):
        self.lib = C.CDLL(str(path))
        def bind(name, result, *args):
            f = getattr(self.lib, 'pp_rmlui_' + name)
            f.restype, f.argtypes = result, list(args)
            setattr(self, name, f)
        u64, integer, string = C.c_uint64, C.c_int, C.c_char_p
        bind('protocol_version', C.c_uint32)
        bind('initialize_backend', integer, integer, integer, C.c_float, string, integer)
        bind('shutdown', None)
        bind('home_document', u64)
        bind('document_open', u64, string, integer)
        bind('document_close', integer, u64)
        bind('document_show', integer, u64, integer)
        bind('document_focus', integer, u64, string)
        bind('document_set_text', integer, u64, string, string)
        bind('document_set_bool', integer, u64, string, integer)
        bind('document_set_enabled', integer, u64, string, integer)
        bind('document_element_bounds', integer, u64, string, *([C.POINTER(C.c_float)] * 4))
        bind('update', None)
        bind('render', None, integer, integer)
        bind('resize', None, integer, integer, C.c_float)
        bind('draw_command_count', integer)
        bind('draw_geometry_count', integer)
        bind('draw_texture_count', integer)
        bind('draw_features', C.c_uint32)
        bind('document_count', integer)
        bind('document_accessibility_snapshot', integer, u64, C.c_void_p, integer)
        bind('focused_element', integer, C.c_void_p, integer)
        if self.protocol_version() != 1:
            raise RuntimeError('Expected bridge protocol version 1')

    def bounds(self, doc, element):
        values = [C.c_float() for _ in range(4)]
        if not self.document_element_bounds(doc, encode(element), *(C.byref(v) for v in values)):
            return None
        return [round(v.value, 3) for v in values]

    def focused(self):
        buffer = C.create_string_buffer(4096)
        self.focused_element(buffer, len(buffer))
        return buffer.value.decode('utf-8')

    def snapshot(self, doc):
        buffer = C.create_string_buffer(2 * 1024 * 1024)
        size = self.document_accessibility_snapshot(doc, buffer, len(buffer))
        if size <= 0 or size >= len(buffer):
            raise RuntimeError('Missing or truncated accessibility snapshot')
        return json.loads(buffer.value.decode('utf-8'))

    def boolean(self, doc, key, value):
        return bool(self.document_set_bool(doc, encode(key), int(value)))

    def text(self, doc, key, value):
        return bool(self.document_set_text(doc, encode(key), encode(value)))


def inside(box, container, tolerance=2):
    if not box or not container or not all(math.isfinite(n) for n in box + container):
        return False
    x, y, w, h = box
    a, b, c, d = container
    return (w > 0 and h > 0 and x >= a - tolerance and y >= b - tolerance
            and x + w <= a + c + tolerance and y + h <= b + d + tolerance)


LONG_NAME = 'Crimson Hunter of the Alimbic Cluster'
LONG_DESCRIPTION = ('A restored Alimbic arena with interconnected vertical routes, moving platforms, '
                    'and a central courtyard. Published by the community for four-player objective matches. '
                    'This deliberately long description verifies wrapping without hiding install controls.')
CONTENT_ELEMENTS = {
    'pages/lobby/rules.rml': ['lobby_rules_sheet', 'rules_columns', 'rules_status'],
    'pages/lobby/lobby.rml': ['lobby_roster_panel', 'lobby_left_panels', 'lobby_chat_panel'],
    'pages/home/home.rml': ['home_left_rail', 'home_right_rail', 'activity_selector', 'deploy'],
    'pages/hunters/native-picker.rml': ['native_hunter_panel', 'native_hunter_picker', 'native_hunter_options', 'native_hunter_preview_panel', 'hunter_cosmetics_visibility'],
    'pages/community/native-discover.rml': ['native_community', 'native_community_row0', 'native_community_name0', 'native_community_meta0', 'native_community_title', 'native_community_description', 'native_community_hash'],
    'pages/license/native-overview.rml': ['native_license', 'native_license_card', 'native_license_details', 'native_license_name', 'native_license_recent'],
    'pages/social/social.rml': ['social_page', 'social_self_id', 'social_row_0', 'social_selected_name', 'social_selected_detail'],
    'pages/social/native-party.rml': ['social_page', 'party_summary', 'party_travel', 'party_reservation', 'party_selected'],
    'pages/settings/quick.rml': ['native_quick_settings', 'quick_motion', 'quick_apply'],
    'pages/settings/native-full.rml': ['native_settings_title', 'native_settings_row_0', 'native_settings_value_0', 'native_settings_subpages'],
    'pages/updates/versions.rml': ['update_card', 'versions_rows', 'versions_row_0', 'versions_status'],
    'pages/adventure/saves.rml': ['adventure_page', 'adventure_save_detail'],
    'pages/adventure/confirm.rml': ['adventure_confirmation', 'adventure_confirmation_description'],
    'pages/adventure/ship.rml': ['ship_page', 'ship_scroll', 'ship_detail'],
    'pages/adventure/ending.rml': ['campaign_ending_page', 'campaign_ending_scroll', 'campaign_ending_credits'],
    'pages/offline/setup.rml': ['offline_page', 'offline_match_panel', 'offline_summary'],
    'pages/offline/training.rml': ['offline_training_difficulty', 'offline_training_toggle_0', 'offline_training_toggle_4'],
    'pages/offline/rules.rml': ['offline_point_goal_row', 'offline_point_goal'],
    'pages/offline/arena.rml': ['offline_arena_page', 'offline_arena_row_0', 'offline_arena_reason_0'],
    'pages/play/browser.rml': ['play_header', 'play_browser', 'play_connect', 'play_entry0'],
    'pages/play/queue.rml': ['play_queue_panel', 'play_queue_status'],
    'pages/news/news.rml': ['news_page', 'news_hero', 'news_feed', 'news_summary'],
    'pages/news/detail.rml': ['news_detail', 'news_detail_title', 'news_detail_body'],
    'pages/updates/available.rml': ['update_card', 'update_actions'],
    'pages/common/quit.rml': ['update_card', 'update_actions'],
    'pages/ingame/pause.rml': ['ingame_pause', 'ingame_session'],
    'pages/ingame/confirm.rml': ['ingame_question'],
    'pages/ingame/journal.rml': ['journal_summary', 'journal_row0', 'journal_detail0'],
    'pages/setup/native-game-files.rml': ['native_setup_panel', 'native_setup_log', 'native_setup_status'],
    'pages/setup/native-game-files-confirm.rml': ['native_setup_confirmation'],
    'pages/theatre/confirm-delete.rml': ['theatre_confirmation', 'theatre_delete_title', 'theatre_delete_path'],
    'pages/theatre/library.rml': ['theatre_page', 'theatre_results', 'theatre_selected_scroll', 'theatre_metadata'],
    'pages/theatre/playback.rml': ['replay_page', 'replay_viewport', 'replay_controls'],
    'pages/studio/native-entry.rml': ['native_studio_entry', 'native_studio_chooser', 'native_studio_status'],
    'pages/theatre/studio-workspace.rml': ['studio_workspace', 'studio_transport', 'studio_main', 'studio_inspector', 'studio_inspector_scroll', 'replay_viewport', 'studio_timeline', 'studio_tabs'],
}


def fixture_ids(assets, path):
    source = (assets/path).read_text(encoding='utf-8')
    if 'prime-shell' in source:
        source += (assets/'components/app-shell.rml').read_text(encoding='utf-8')
    return set(re.findall(r'\bid=["\']([^"\']+)', source))


def seed(bridge, doc, path, touch=False, large_text=False):
    """Explicit service-state fixture using only actual source markup IDs."""
    # Only shell pages own Profile and the diagnostics surface.
    if path in ['pages/home/home.rml', 'pages/lobby/lobby.rml', 'pages/hunters/native-picker.rml',
                'pages/community/native-discover.rml', 'pages/social/social.rml',
                'pages/settings/quick.rml', 'pages/settings/native-full.rml',
                'pages/adventure/saves.rml', 'pages/offline/setup.rml', 'pages/play/browser.rml',
                'pages/news/news.rml', 'pages/theatre/library.rml', 'pages/studio/native-entry.rml']:
        bridge.text(doc, 'profile', 'HUNTER')
        bridge.boolean(doc, 'visible:diagnostics_panel', False)
    bridge.boolean(doc, 'class:@document:reduce-motion', True)
    bridge.boolean(doc, 'class:@document:touch-targets', touch)
    bridge.boolean(doc, 'class:@document:large-text', large_text)
    if path == 'pages/home/home.rml':
        for element in ['activity_compact', 'home_left_rail', 'home_right_rail', 'session_panel']:
            bridge.boolean(doc, 'visible:' + element, True)
        bridge.boolean(doc, 'class:activity_compact:hidden', False)
        bridge.boolean(doc, 'class:activity_drawer:open', False)
        for key, value in {'text_activity_title_1': 'ONLINE', 'text_activity_action_1': 'FIND MATCH',
                           'text_activity_description_1': 'Browse public matches', 'text_player_name_2': LONG_NAME}.items():
            bridge.text(doc, key, value)
    if path == 'pages/hunters/native-picker.rml':
        bridge.text(doc, 'native_hunter_name', 'SAMUS ARAN')
        bridge.text(doc, 'native_hunter_status', 'LOADOUT DRAFT // Equip your selected hunter and cosmetic loadout for the next session.')
        bridge.text(doc, 'hunter_skin_description', 'Classic armor with community color accents and a verified cosmetic loadout.')
        bridge.text(doc, 'hunter_cosmetics_visibility', 'Preview your detached cosmetic draft. Apply changes to use this hunter in your next session.')
    if path == 'pages/lobby/rules.rml':
        bridge.boolean(doc, 'visible:visible__rules_owner_1', False)
        bridge.text(doc, 'text_rules_map_1', 'ALIMBIC CITADEL')
        bridge.text(doc, 'text_rules_mode_1', 'BATTLE')
        bridge.text(doc, 'text_rules_format_1', 'FREE FOR ALL')
        bridge.text(doc, 'text_rules_goal_label_1', 'POINT GOAL')
        bridge.text(doc, 'rules_status', 'LOCAL RULE DRAFT // APPLY TO UPDATE THE NEXT MATCH')
        for i in range(19): bridge.text(doc, f'text_rules_toggle{i}_1', 'ON' if i%2 else 'OFF')
    if path == 'pages/lobby/lobby.rml':
        bridge.boolean(doc, 'visible:lobby_countdown', False)
        for key, value in {'text_lobby_name_1': 'ALIMBIC OPEN LOBBY', 'text_lobby_status_1': 'Public // North America',
                           'text_lobby_mode_name_1': 'BATTLE', 'text_lobby_map_1': 'Alimbic Citadel',
                           'lobby_time': '7:00', 'lobby_score': '25 POINTS', 'text_lobby_format_1': 'TEAM BATTLE',
                           'text_lobby_player_count_1': '8 / 8', 'text_lobby_ready_count_1': '7 / 8',
                           'lobby_brief_title': 'ONE HUNTER NOT READY', 'lobby_brief_count': '7 / 8 READY',
                           'lobby_ready': 'READY', 'lobby_hunter': 'HUNTER', 'lobby_chat_history': 'Samus: Ready when you are.\nTrace: Good luck!',
                           'lobby_field_caption': 'SERVER CONNECTED'}.items(): bridge.text(doc, key, value)
        for i, hunter in enumerate(['SAMUS', 'KANDEN', 'TRACE', 'SYLUX', 'NOXUS', 'SPIRE', 'WEAVEL', 'SAMUS']):
            bridge.text(doc, f'text_slot{i}_name_1', f'Hunter {i+1}')
            bridge.text(doc, f'text_slot{i}_hunter_1', hunter)
            bridge.text(doc, f'text_slot{i}_state_1', 'READY' if i < 7 else 'WAITING')
            bridge.text(doc, f'lobby_team{i}', 'ORANGE' if i % 2 == 0 else 'GREEN')
            bridge.boolean(doc, f'visible:lobby_slot{i}', False)
            bridge.boolean(doc, f'visible:lobby_add_bot{i}', False)
            bridge.boolean(doc, f'class:visible_slot{i}_occupied_1:ready', i < 7)
    if path == 'pages/community/native-discover.rml':
        for element in ['native_community_cancel_install', 'native_community_archive', 'native_community_restore',
                        'native_community_creator_confirm', 'native_community_creator_cancel', 'native_community_notice', 'native_community_preview']:
            bridge.boolean(doc, 'visible:' + element, False)
        for i in range(8):
            bridge.boolean(doc, f'visible:native_community_row{i}', True)
            bridge.boolean(doc, f'visible:native_community_thumbnail{i}', False)
            bridge.text(doc, f'native_community_name{i}', f'{i+1}. Alimbic Citadel — Extended Community Arena')
            bridge.text(doc, f'native_community_meta{i}', f'Creator: {LONG_NAME} // Revision {i+1} // 4 players // Verified package')
        bridge.text(doc, 'native_community_title', 'ALIMBIC CITADEL — EXTENDED COMMUNITY ARENA')
        bridge.text(doc, 'native_community_description', LONG_DESCRIPTION)
        bridge.boolean(doc, 'visible:native_community_technical', True)
        bridge.text(doc, 'native_community_hash', 'SHA256: ' + 'a1b2c3d4'*8)
        bridge.text(doc, 'native_community_status', '8 verified community maps // Updated just now')
    if path == 'pages/license/native-overview.rml':
        for face in ['overview', 'stats', 'history', 'achievements', 'comparison', 'account', 'emblems', 'titles']:
            bridge.boolean(doc, 'visible:native_license_face_' + face, face == 'overview')
        for element in ['native_license_publish_confirmation', 'native_license_cancel_refresh']:
            bridge.boolean(doc, 'visible:' + element, False)
        bridge.text(doc, 'native_license_name', LONG_NAME)
        bridge.text(doc, 'native_license_recent', 'Recent match: Alimbic Citadel // Four-player objective battle // Victory after overtime')
        bridge.text(doc, 'native_license_rank', 'ELITE HUNTER // VERIFIED')
        for key, value in {'matches': '1,024', 'wins': '640', 'losses': '384', 'kills': '12,345',
                           'deaths': '9,876', 'assists': '3,210', 'win_rate': '62.5%', 'kd': '1.25'}.items():
            bridge.text(doc, 'native_license_' + key, value)
    if path == 'pages/social/social.rml':
        bridge.boolean(doc, 'visible:social_empty', False)
        bridge.boolean(doc, 'visible:social_error', False)
        bridge.boolean(doc, 'visible:social_cancel_refresh', False)
        for i in range(8):
            bridge.text(doc, f'social_row_name_{i}', LONG_NAME + f' {i+1}')
            bridge.text(doc, f'social_row_detail_{i}', 'Online // Alimbic Citadel // Open party, two seats available')
        for i in range(21, 34):
            bridge.boolean(doc, f'visible:social_command_{i}', i in [28, 29, 33])
        bridge.text(doc, 'social_selected_name', LONG_NAME)
        bridge.text(doc, 'social_selected_detail', 'This friend is hosting an open party. Join their session or invite them to yours. ' + LONG_DESCRIPTION)
        bridge.text(doc, 'social_self_id', 'PP-1234-5678-9ABC-DEF0-1234')
        bridge.text(doc, 'social_counts', '8 FRIENDS // 2 PENDING GAME INVITES // EXPLICIT LOCAL FIXTURE')
        bridge.text(doc, 'social_party_summary', '4 / 8 IN PARTY // LEADER ' + LONG_NAME)
        bridge.text(doc, 'social_session_notice', 'SHARED PRESENCES // VERIFIED DIRECTORY')
    if path == 'pages/social/native-party.rml':
        for key, value in {'party_self': 'PP-1234-5678-9ABC-DEF0-1234',
                           'party_summary': '8 / 8 MEMBERS // LEADER ' + LONG_NAME,
                           'party_travel': 'LEADER LOBBY // Alimbic Citadel // DESTINATION AVAILABLE',
                           'party_reservation': '8 VERIFIED SEATS // ACCEPT BEFORE JOINING',
                           'party_selected': LONG_NAME, 'party_selected_id': 'PP-1234-5678-9ABC-DEF0-1234',
                           'party_status': 'EXPLICIT LOCAL LAYOUT FIXTURE // NO ACCOUNT OR NETWORK'}.items(): bridge.text(doc, key, value)
        bridge.boolean(doc, 'visible:party_empty', False)
        for i in range(8):
            bridge.text(doc, f'party_name_{i}', LONG_NAME + f' {i+1}')
            bridge.text(doc, f'party_detail_{i}', 'LEADER // IN LOBBY' if i == 0 else 'MEMBER // PRESENCE NOT SHARED')
    if path == 'pages/settings/quick.rml':
        bridge.text(doc, 'quick_motion_state', 'OFF')
        bridge.text(doc, 'quick_updates_state', 'ON')
        bridge.text(doc, 'quick_debug_state', 'OFF')
        bridge.text(doc, 'quick_setting_status', 'Preferences apply when you choose APPLY. Unsaved changes can be discarded.')
    if path == 'pages/settings/native-full.rml':
        for element in ['native_settings_video', 'native_settings_capture', 'native_settings_confirmation',
                        'native_settings_controller_identity', 'native_settings_hud', 'native_settings_reset_replay',
                        'native_settings_touch_visual', 'native_settings_stylus_visual', 'native_settings_outline_visual',
                        'native_settings_import_arenas', 'native_settings_character_previews', 'native_settings_cancel_import', 'native_settings_files']:
            bridge.boolean(doc, 'visible:' + element, False)
        # Accept both baseline180 and current96-slot authored documents.
        for i in range(180):
            if not bridge.boolean(doc, f'visible:native_settings_row_{i}', i < 20):
                break
            # Only authored slots participate in the publication benchmark.
            if i < 20:
                labels = ['FIELD OF VIEW', 'MENU SCALE', 'BRIGHTNESS', 'FRAME LIMIT', 'DISPLAY MODE', 'RENDER SCALE', 'ANTIALIASING', 'DYNAMIC LIGHTS', 'SHADOW DETAIL', 'TEXTURE FILTERING', 'ANISOTROPIC FILTERING', 'PARTICLE DENSITY', 'VIEW DISTANCE', 'MOTION BLUR', 'BLOOM INTENSITY', 'CONTRAST', 'SATURATION', 'HUD SCALE', 'DAMAGE FLASH', 'SCREEN SHAKE']
                bridge.text(doc, f'native_settings_label_{i}', labels[i])
                bridge.text(doc, f'native_settings_hint_{i}', 'Adjust the presentation to suit your display. Changes remain a draft until you apply them.')
                bridge.boolean(doc, f'visible:native_settings_cycle_{i}', False)
                bridge.boolean(doc, f'visible:native_settings_slider_{i}', False)
        bridge.text(doc, 'native_settings_title', 'DISPLAY')
        bridge.text(doc, 'native_settings_subpage_0', 'VIDEO')
        for i in range(1, 4):
            bridge.boolean(doc, f'visible:native_settings_subpage_{i}', False)
        for element in ['native_settings_system', 'native_settings_profiles', 'native_settings_maintenance']:
            bridge.boolean(doc, 'visible:' + element, False)
    if path == 'pages/updates/versions.rml':
        for i in range(30):
            bridge.boolean(doc, f'visible:versions_row_{i}', i < 4)
            if i < 4:
                bridge.text(doc, f'versions_row_{i}', f'BUILD 2026.10.{19-i} // CURRENT STABLE RELEASE WITH PLATFORM COMPATIBILITY IMPROVEMENTS')
        for element in ['versions_confirmation', 'versions_progress', 'versions_cancel', 'versions_confirm']:
            bridge.boolean(doc, 'visible:' + element, False)
        bridge.text(doc, 'versions_status', 'Installed build is up to date. Select a release to inspect its details and compatibility notes.')
        bridge.text(doc, 'versions_installed', '2026.10.19-macos-arm64-release')
    if path == 'pages/adventure/saves.rml':
        for element in ['adventure_cancel_save_work', 'adventure_files_error', 'adventure_error']:
            bridge.boolean(doc, 'visible:' + element, False)
        for i in range(1, 4):
            bridge.text(doc, f'adventure_slot{i}_label', f'SLOT {i} // SAMUS // ALIMBIC CITADEL // 12 HOURS')
        bridge.text(doc, 'adventure_save_detail', 'CURRENT SAVE // Alimbic Citadel. Return to your last checkpoint or manage a recoverable copy. ' + LONG_DESCRIPTION)
    if path == 'pages/adventure/confirm.rml':
        for element in ['adventure_confirmation_error', 'adventure_cancel_save_change', 'adventure_confirm_copy', 'adventure_confirm_archive']:
            bridge.boolean(doc, 'visible:' + element, False)
        bridge.text(doc, 'adventure_overwrite_title', 'REPLACE SLOT 1 WITH A NEW RUN?')
        bridge.text(doc, 'adventure_confirmation_description', 'Your existing progress remains recoverable in its archive. This confirmation names the exact selected slot. ' + LONG_DESCRIPTION)
    if path == 'pages/adventure/ship.rml':
        bridge.boolean(doc, 'visible:ship_map_face', False)
        bridge.boolean(doc, 'visible:ship_error', False)
        bridge.text(doc, 'ship_identity', 'SLOT 1 // SAMUS // VERIFIED LOCAL SAVE')
        bridge.text(doc, 'ship_detail', 'DOCKED AT ALIMBIC CITADEL. ' + LONG_DESCRIPTION)
    if path == 'pages/adventure/ending.rml':
        bridge.text(doc, 'campaign_ending_result', 'MISSION COMPLETE // OCTOLITHS RECOVERED')
        bridge.text(doc, 'campaign_ending_detail', LONG_DESCRIPTION)
        bridge.text(doc, 'campaign_ending_totals', 'MISSION TIME: 12:34:56\nSCAN COMPLETION: 100%\nITEM COMPLETION: 100%')
        bridge.text(doc, 'campaign_ending_credits', ('PROJECT PRIME // CONTRIBUTORS\nDesign, engineering, sound and community testing.\n' * 12))
    if path == 'pages/offline/setup.rml':
        # Explicit activity fixture; route exclusivity behavior belongs to PR870.
        bridge.boolean(doc, 'visible:offline_training_panel', False)
        for key, value in {'offline_arena_value':'ALIMBIC CITADEL', 'offline_mode_value':'BATTLE', 'offline_matchup_value':'FREE FOR ALL',
                           'offline_bots_value':'7 BOTS', 'offline_skill_value':'ELITE', 'offline_hunter_value':'SAMUS', 'offline_suit_value':'BLUE',
                           'offline_summary':'8 combatants // Alimbic Citadel // 10 minute match'}.items():
            bridge.text(doc, key, value)
    if path == 'pages/offline/arena.rml':
        bridge.text(doc, 'offline_arena_page', 'PAGE 1 OF 4 // 16 COMPATIBLE ARENAS')
        bridge.boolean(doc, 'visible:offline_arena_picker_error', False)
        for i in range(16):
            bridge.text(doc, f'offline_arena_name_{i}', f'{i+1}. ALIMBIC CITADEL // EXTENDED ARENA')
            bridge.text(doc, f'offline_arena_reason_{i}', 'VERIFIED // COMPATIBLE WITH THE SELECTED MATCH RULES')
    if path == 'pages/offline/training.rml':
        for key, value in {'offline_training_difficulty_value': 'ELITE', 'offline_training_distance_value': 'LONG RANGE', 'offline_training_scope_value': 'HOLD TO AIM',
                           'offline_training_toggle_0': 'INFINITE AMMO: ON', 'offline_training_toggle_1': 'MOVING TARGETS: ON',
                           'offline_training_toggle_2': 'IMPERIALIST CLICK RELOAD: OFF', 'offline_training_toggle_3': 'TARGET RESPAWN DELAY: SHORT',
                           'offline_training_toggle_4': 'SESSION FEEDBACK: ON'}.items():
            bridge.text(doc, key, value)
    if path == 'pages/offline/rules.rml':
        bridge.boolean(doc, 'visible:offline_rules_error', False)
        labels = ['AUTO RESET: OFF', 'FRIENDLY FIRE: OFF', 'DAMAGE MODIFIER: NORMAL', 'DROPS: ENABLED', 'RADAR: ON', '', 'HEADSHOTS: ON', 'AMMO PICKUPS: ON', 'HEALTH PICKUPS: ON', 'WEAPON PICKUPS: ON', 'TEAMS: BALANCED', 'SPAWN PROTECTION: ON', 'FALL DAMAGE: OFF', 'MOVEMENT: NATIVE', 'RIFTLINE M7: OFF']
        for i, label in enumerate(labels):
            if label: bridge.text(doc, f'offline_rule_{i}', label)
        bridge.text(doc, 'offline_damage_value', 'DAMAGE: 100%')
    if path == 'pages/play/browser.rml':
        for element in ['play_create', 'play_search', 'play_browser_empty']:
            bridge.boolean(doc, 'visible:' + element, False)
        bridge.boolean(doc, 'visible:play_browser', True)
        bridge.boolean(doc, 'visible:play_connect', True)
        bridge.text(doc, 'text_play_status_1', '8 compatible public sessions // Exact map packages verified')
        for i in range(8):
            bridge.text(doc, f'text_play_server{i}_name_1', LONG_NAME + f' — SESSION {i+1}')
            bridge.text(doc, f'text_play_server{i}_details_1', '4/8 PLAYERS // 32 MS')
    if path == 'pages/play/queue.rml':
        bridge.text(doc, 'play_queue_phase', 'PLAYER SLOT AVAILABLE')
        bridge.text(doc, 'play_queue_status', 'A party seat is reserved for your group. Accept before the offer expires to join the server.')
        bridge.text(doc, 'play_queue_position', 'QUEUE POSITION: 1 OF 8')
        bridge.text(doc, 'play_queue_seconds', '30')
    if path == 'pages/news/news.rml':
        bridge.boolean(doc, 'visible:news_error', False)
        bridge.text(doc, 'news_headline', 'Community arenas and the next Project Prime update')
        bridge.text(doc, 'news_summary', LONG_DESCRIPTION)
        for i in range(4):
            bridge.text(doc, f'news_article_title_{i}', f'COMMUNITY UPDATE {i+1} // NEW ARENAS AND PLAYTEST NOTES')
            bridge.text(doc, f'news_article_summary_{i}', 'Read the latest progress from the community, including compatible map packages and upcoming events.')
    if path == 'pages/news/detail.rml':
        bridge.text(doc, 'news_detail_title', 'COMMUNITY UPDATE // NEW ARENAS AND PLAYTEST NOTES')
        bridge.text(doc, 'news_detail_body', (LONG_DESCRIPTION + '\n\n') * 8)
    if path == 'pages/updates/available.rml':
        bridge.text(doc, 'update_installed', '2026.10.19-desktop-stable')
        bridge.text(doc, 'update_available', '2026.10.20-desktop-stable')
    if path == 'pages/ingame/pause.rml':
        bridge.boolean(doc, 'visible:ingame_vote', False)
        bridge.boolean(doc, 'visible:ingame_bots_panel', False)
        bridge.text(doc, 'ingame_session', 'ALIMBIC CITADEL // LOCAL BATTLE // 8 COMBATANTS')
    if path == 'pages/ingame/confirm.rml':
        bridge.text(doc, 'ingame_question', 'LEAVE THIS SESSION AND RETURN TO THE MAIN MENU?')
        bridge.boolean(doc, 'visible:ingame_confirmation_error', False)
    if path == 'pages/ingame/journal.rml':
        bridge.boolean(doc, 'visible:journal_empty', False)
        bridge.text(doc, 'journal_summary', 'ALIMBIC CITADEL // 12 MISSION LOCATIONS // 128 SCANNED ENTRIES')
        bridge.text(doc, 'journal_page', '1 OF 8')
        for i in range(4):
            bridge.text(doc, f'journal_title{i}', f'LOGBOOK ENTRY {i+1} // ALIMBIC DEFENSE NETWORK')
            bridge.text(doc, f'journal_detail{i}', LONG_DESCRIPTION)
    if path == 'pages/setup/native-game-files.rml':
        bridge.text(doc, 'native_setup_status', 'NO VERIFIED GAME FILES // CHOOSE YOUR LOCAL SOURCE')
        bridge.text(doc, 'native_setup_log', 'CHECKING LOCAL SOURCE\nOptional previews are generated independently after installation.\n' * 8)
    if path == 'pages/setup/native-game-files-confirm.rml':
        bridge.text(doc, 'native_setup_confirmation', 'Install verified game files from this local source? Existing personal settings and saved games will be preserved.')
    if path == 'pages/theatre/confirm-delete.rml':
        bridge.text(doc, 'theatre_delete_title', 'DELETE ALIMBIC CITADEL EIGHT PLAYER OBJECTIVE BATTLE RECORDED DURING THE COMMUNITY PLAYTEST?')
        bridge.text(doc, 'theatre_delete_path', '/synthetic/local/replays/community-playtest/2026-10-20/extended-alimbic-citadel-objective-battle-with-eight-hunters.prime-replay')
    if path == 'pages/theatre/library.rml':
        for element in ['theatre_empty', 'theatre_error', 'theatre_launch_problem', 'theatre_delete_confirmation', 'theatre_cancel_job']:
            bridge.boolean(doc, 'visible:' + element, False)
        for i in range(8):
            bridge.text(doc, f'theatre_entry{i}_title', f'{i+1}. ALIMBIC CITADEL — EIGHT PLAYER BATTLE')
            bridge.text(doc, f'theatre_entry{i}_detail', 'RECORDED 2026-10-20 // 12m34s // VERIFIED REPLAY')
        bridge.text(doc, 'theatre_selected_title', 'ALIMBIC CITADEL — EIGHT PLAYER BATTLE')
        bridge.text(doc, 'theatre_metadata', ('Map: Alimbic Citadel\nPlayers: 8\nDuration: 12m34s\n' + LONG_DESCRIPTION))
        bridge.text(doc, 'theatre_status', '8 LOCAL REPLAYS // SELECTED REPLAY IS READY TO WATCH')
    if path in ['pages/theatre/playback.rml', 'pages/theatre/studio-workspace.rml']:
        bridge.boolean(doc, 'visible:replay_error', False)
        bridge.boolean(doc, 'visible:replay_inactive', False)
        bridge.boolean(doc, 'visible:replay_view_labels', False)
        bridge.text(doc, 'replay_state', 'PAUSED')
        bridge.text(doc, 'replay_time', '03:12 / 12:34')
        bridge.text(doc, 'replay_rate', '1.00x')
    if path == 'pages/theatre/studio-workspace.rml':
        for section in ['markers','combat','analysis','audio','export']:
            bridge.boolean(doc, 'visible:studio_inspector_' + section, False)
        bridge.text(doc, 'studio_notice', 'LOCAL REPLAY DRAFT // CAMERA CHANGES REMAIN UNSAVED')
    if path == 'pages/studio/native-entry.rml':
        # Launcher chooser mode; standalone Studio substitutes replay_open.
        bridge.boolean(doc, 'visible:native_studio_theatre', True)
        bridge.boolean(doc, 'visible:native_studio_replay_open', False)
        bridge.text(doc, 'native_studio_status', 'OPEN A LOCAL REPLAY OR CHOOSE A CREATION WORKSPACE')
    # Essential controls are enabled by a service owner in normal usable states.
    for element in FIXTURES.get(path, []):
        bridge.document_set_enabled(doc, encode(element), 1)


def assertion(checks, name, passed, **detail):
    checks.append(dict(name=name, passed=bool(passed), **detail))


def geometry_checks(bridge, doc, path, width, height, density, touch=False, large_text=False):
    checks = []
    viewport = [0, 0, width, height]
    stage = bridge.bounds(doc, 'stage')
    # Record before focus scrolls any container. Only ordinary configuration
    # actions require immediate 1080p visibility; long collections may scroll.
    initial_actions = {
        'pages/home/home.rml': ['home_social_open'],
        'pages/lobby/rules.rml': ['rules_apply', 'rules_cancel'],
        'pages/offline/setup.rml': ['offline_start', 'offline_rules_open'],
        'pages/offline/training.rml': ['offline_training_done'],
        'pages/offline/rules.rml': ['offline_rules_apply', 'offline_rules_cancel'],
        'pages/settings/quick.rml': ['quick_apply', 'quick_close'],
        'pages/settings/native-full.rml': ['native_settings_apply'],
        'pages/hunters/native-picker.rml': ['hunter_apply', 'hunter_cancel'],
        'pages/play/browser.rml': ['play_join', 'play_back'],
        'pages/lobby/lobby.rml': ['lobby_ready', 'lobby_start', 'lobby_leave'],
    }
    if width/density >= 1920 and height/density >= 1080 and not touch:
        for element in initial_actions.get(path, []):
            box = bridge.bounds(doc, element)
            assertion(checks, 'primary_action_visible_without_focus_scroll:' + element,
                      inside(box, stage or viewport), bounds=box, container=stage or viewport)
    if stage:
        assertion(checks, 'shell_stage_inside_viewport', inside(stage, viewport), bounds=stage)
        top = bridge.bounds(doc, 'topbar')
        if top:
            assertion(checks, 'stage_below_header', stage[1] >= top[1] + top[3] - 2, header=top, stage=stage)
        if width / density <= 800:
            focused = bool(bridge.document_focus(doc, b'profile'))
            bridge.update()
            box = bridge.bounds(doc, 'profile')
            assertion(checks, 'compact_profile_visible_focusable', focused and inside(box, viewport), bounds=box, focus=focused)
    pinned_actions = {
        'pages/home/home.rml': ['home_social_open'],
        'pages/hunters/native-picker.rml': ['hunter_apply', 'hunter_cancel'],
        'pages/lobby/rules.rml': ['rules_apply', 'rules_cancel'],
        'pages/offline/rules.rml': ['offline_rules_apply', 'offline_rules_cancel'],
        'pages/offline/training.rml': ['offline_training_done'],
    }
    for element in pinned_actions.get(path, []):
        box = bridge.bounds(doc, element)
        assertion(checks, 'persistent_action_initially_visible:' + element, inside(box, stage or viewport), bounds=box)
    # Restore the first content control before measuring collection columns;
    # a previous mode may have scrolled to its final action.
    bridge.document_focus(doc, encode(FIXTURES[path][0]))
    bridge.update()
    for element in CONTENT_ELEMENTS[path]:
        box = bridge.bounds(doc, element)
        container = stage or viewport
        horizontal = bool(box) and box[0] >= container[0]-2 and box[0]+box[2] <= container[0]+container[2]+2
        assertion(checks, 'content_horizontal_bounds:' + element, horizontal, bounds=box, container=container)
    if path == 'pages/lobby/lobby.rml':
        # Checking the column and row boxes alone misses an auto-width status
        # column collapsing while the flex name button consumes the whole row.
        # Rows may scroll on short/touch surfaces, but their children must still
        # fit their own row in every density and accessibility configuration.
        for i in range(8):
            row = bridge.bounds(doc, f'visible_slot{i}_occupied_1')
            copy = bridge.bounds(doc, f'lobby_player{i}')
            state = bridge.bounds(doc, f'text_slot{i}_state_1')
            team = bridge.bounds(doc, f'lobby_team{i}')
            assertion(checks, f'roster_ready_state_inside_row:{i}', inside(state, row), bounds=state, row=row)
            assertion(checks, f'roster_team_inside_row:{i}', inside(team, row), bounds=team, row=row)
            assertion(checks, f'roster_name_before_team:{i}',
                      bool(copy and team) and copy[0]+copy[2] <= team[0]+2,
                      name_button=copy, team_button=team)
    if path == 'pages/lobby/lobby.rml' and width / density >= 1280 and height / density >= 720 and not touch:
        roster = bridge.bounds(doc, 'lobby_roster_panel')
        for i in range(8):
            box = bridge.bounds(doc, f'visible_slot{i}_occupied_1')
            assertion(checks, f'eight_roster_rows_initially_visible:{i}', inside(box, roster), bounds=box, roster=roster)
    for element in FIXTURES[path]:
        focused = bool(bridge.document_focus(doc, encode(element)))
        bridge.update()
        box = bridge.bounds(doc, element)
        # Header/footer controls belong to chrome; content actions belong to stage.
        container = viewport if element in ['profile', 'header_settings'] else stage or viewport
        assertion(checks, 'essential_action_reachable:' + element,
                  focused and inside(box, container), focus=focused, bounds=box, container=container)
        assertion(checks, 'actual_focus_matches:' + element, bridge.focused() == element, focused_id=bridge.focused())
        if path == 'pages/hunters/native-picker.rml' and element in ['hunter_choice0', 'hunter_skin_option6', 'hunter_category4']:
            scrollport = bridge.bounds(doc, 'native_hunter_options')
            assertion(checks, 'focused_control_inside_scrollport:' + element, inside(box, scrollport),
                      bounds=box, scrollport=scrollport)
        if touch and path in ['pages/ingame/journal.rml', 'pages/theatre/studio-workspace.rml', 'pages/lobby/rules.rml']:
            assertion(checks, 'touch_action_minimum_48dp:' + element,
                      bool(box) and box[3] >= 48*density - 2, bounds=box)
    if path == 'pages/lobby/rules.rml':
        content, status, footer = [bridge.bounds(doc, key) for key in ['rules_columns', 'rules_status', 'rules_apply']]
        assertion(checks, 'rules_content_above_status', bool(content and status) and content[1]+content[3] <= status[1]+2,
                  content=content, status=status)
        assertion(checks, 'rules_status_above_footer', bool(status and footer) and status[1]+status[3] <= footer[1]+2,
                  status=status, footer=footer)
    # A long list must scroll to its last control, then return to its first.
    first_action = FIXTURES[path][0]
    restored = bool(bridge.document_focus(doc, encode(first_action)))
    bridge.update()
    assertion(checks, 'focus_scroll_returns_to_first', restored and inside(bridge.bounds(doc, first_action), viewport),
              bounds=bridge.bounds(doc, first_action))
    bridge.document_focus(doc, encode(FIXTURES[path][-1]))
    bridge.update()
    last_action = FIXTURES[path][-1]
    seed(bridge, doc, path, touch=touch, large_text=large_text)
    bridge.update()
    assertion(checks, 'same_state_publication_preserves_focus', bridge.focused() == last_action,
              expected=last_action, actual=bridge.focused())
    if path == 'pages/settings/native-full.rml' and touch:
        bridge.document_focus(doc, b'native_settings_category_0')
        bridge.update()
        button = bridge.bounds(doc, 'native_settings_category_0')
        # Sidebar has a class rather than ID; compare button's visible viewport
        # with next content heading and the stage as independent constraints.
        heading = bridge.bounds(doc, 'native_settings_title')
        assertion(checks, 'touch_category_minimum_48dp', bool(button) and button[3] >= 48*density - 2, bounds=button)
        if width/density <= 760 or height/density <= 500:
            assertion(checks, 'touch_category_within_strip', bool(button and heading)
                      and inside(button, viewport) and (button[1] + button[3] <= heading[1] + 2
                          or button[0] + button[2] <= heading[0] + 2), category=button, next_heading=heading)
        bridge.document_focus(doc, b'native_settings_subpage_0')
        bridge.update()
        subpage = bridge.bounds(doc, 'native_settings_subpage_0')
        assertion(checks, 'touch_subpage_minimum_48dp', bool(subpage) and subpage[3] >= 48*density - 2, bounds=subpage)
    if path == 'pages/updates/versions.rml':
        bridge.document_focus(doc, b'versions_row_0')
        bridge.update()
        card = bridge.bounds(doc, 'update_card')
        rows = bridge.bounds(doc, 'versions_rows')
        assertion(checks, 'versions_rows_inside_card', inside(rows, card), rows=rows, card=card)
    return checks


def run_cpu(bridge, assets):
    results = []
    pages = sorted(p.relative_to(assets).as_posix() for p in (assets/'pages').rglob('*.rml'))
    if not pages:
        raise RuntimeError('Assets must be a packaged root containing pages/ and prime_home.rml')
    for width, height, density in CASES:
        initialized, init_ms = milliseconds(lambda: bridge.initialize_backend(width, height, density, encode(assets), 1))
        case = dict(width=width, height=height, density=density, logical_width=width/density, logical_height=height/density, init_ms=init_ms, initialized=bool(initialized), pages=[])
        results.append(case)
        if not initialized:
            continue
        try:
            bridge.document_show(bridge.home_document(), 0)
            for path, scenario in [(p, None) for p in pages] + [(p, v) for p, variants in SCENARIOS.items() for v in variants]:
                row = dict(path=path, scenario=scenario, checks=[])
                case['pages'].append(row)
                doc, row['cold_open_ms'] = milliseconds(lambda: bridge.document_open(encode(path), 0))
                row['opened'] = bool(doc)
                if not doc:
                    continue
                try:
                    bridge.document_show(doc, 1)
                    bridge.document_show(bridge.home_document(), 0)
                    if scenario:
                        seed_scenario(bridge, doc, path, scenario)
                    elif path in FIXTURES:
                        seed(bridge, doc, path)
                    _, row['update_ms'] = milliseconds(bridge.update)
                    _, row['render_ms'] = milliseconds(lambda: bridge.render(width, height))
                    row['draw_commands'] = bridge.draw_command_count()
                    row['geometry_count'] = bridge.draw_geometry_count()
                    row['texture_count'] = bridge.draw_texture_count()
                    row['unsupported_features'] = bridge.draw_features()
                    if path in FIXTURES:
                        nodes = bridge.snapshot(doc)['nodes']
                        row['initial_scroll_regions'] = [n for n in nodes if n['actions'] & 4]
                        row['initial_offscreen_controls'] = [n for n in nodes if n['actions'] & 1 and n['offscreen']]
                    idle = []
                    for _ in range(3):
                        _, elapsed = milliseconds(lambda: (bridge.update(), bridge.render(width, height)))
                        idle.append(elapsed)
                    row['idle_update_render_ms'] = idle
                    if scenario:
                        for group, touch, large_text in [('checks', False, False), ('touch_checks', True, False), ('combined_accessibility_checks', True, True)]:
                            seed_scenario(bridge, doc, path, scenario, touch=touch, large_text=large_text)
                            bridge.update()
                            row[group] = scenario_checks(bridge, doc, path, scenario, width, height, density, touch=touch)
                    elif path in FIXTURES:
                        row['checks'] = geometry_checks(bridge, doc, path, width, height, density)
                        if path in FIXTURES:
                            seed(bridge, doc, path, touch=True)
                            bridge.update()
                            row['touch_checks'] = geometry_checks(bridge, doc, path, width, height, density, touch=True)
                        if (width/density, height/density) in [(1280, 720), (640, 360)] or path == 'pages/theatre/confirm-delete.rml':
                            seed(bridge, doc, path, large_text=True)
                            bridge.update()
                            row['large_text_checks'] = geometry_checks(bridge, doc, path, width, height, density, large_text=True)
                            if path in ['pages/theatre/library.rml', 'pages/theatre/studio-workspace.rml', 'pages/ingame/journal.rml', 'pages/theatre/confirm-delete.rml']:
                                seed(bridge, doc, path, touch=True, large_text=True)
                                bridge.update()
                                row['combined_accessibility_checks'] = geometry_checks(bridge, doc, path, width, height, density, touch=True, large_text=True)
                except Exception as error:
                    row['error'] = repr(error)
                finally:
                    row['closed'] = bool(bridge.document_close(doc))
                    bridge.update()
                    row['remaining_documents'] = bridge.document_count()
        finally:
            bridge.shutdown()
    return pages, results


def write_png(path, width, height, rgba):
    def chunk(kind, payload):
        return struct.pack('!I', len(payload)) + kind + payload + struct.pack('!I', zlib.crc32(kind + payload) & 0xffffffff)
    # GL pixels begin at the bottom left; PNG rows begin at the top left.
    stride = width * 4
    raw = b''.join(b'\0' + rgba[y*stride:(y+1)*stride] for y in reversed(range(height)))
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('!2I5B', width, height, 8, 6, 0, 0, 0))
                     + chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b''))


def run_gl(bridge, assets, glfw_path, output_dir, selected_pages=None, selected_cases=None, touch=False, large_text=False):
    if os.name != 'nt' and sys.platform != 'darwin':
        return dict(error='Optional --glfw screenshot pass supports Windows and macOS')
    glfw = C.CDLL(str(glfw_path))
    for name, result, args in [
        ('glfwInit', C.c_int, []), ('glfwTerminate', None, []),
        ('glfwWindowHint', None, [C.c_int, C.c_int]),
        ('glfwCreateWindow', C.c_void_p, [C.c_int, C.c_int, C.c_char_p, C.c_void_p, C.c_void_p]),
        ('glfwMakeContextCurrent', None, [C.c_void_p]),
        ('glfwDestroyWindow', None, [C.c_void_p]),
        ('glfwGetProcAddress', C.c_void_p, [C.c_char_p])]:
        f = getattr(glfw, name)
        f.restype, f.argtypes = result, args
    gl = C.WinDLL('opengl32.dll') if os.name == 'nt' else C.CDLL('/System/Library/Frameworks/OpenGL.framework/OpenGL')
    for name, args in [
        ('glClearColor', [C.c_float]*4), ('glClear', [C.c_uint]),
        ('glReadPixels', [C.c_int, C.c_int, C.c_int, C.c_int, C.c_uint, C.c_uint, C.c_void_p]),
        ('glFinish', []), ('glGenTextures', [C.c_int, C.POINTER(C.c_uint)]),
        ('glBindTexture', [C.c_uint, C.c_uint]), ('glTexParameteri', [C.c_uint, C.c_uint, C.c_int]),
        ('glTexImage2D', [C.c_uint, C.c_int, C.c_int, C.c_int, C.c_int, C.c_int, C.c_uint, C.c_uint, C.c_void_p]),
        ('glDeleteTextures', [C.c_int, C.POINTER(C.c_uint)]), ('glViewport', [C.c_int]*4)]:
        f = getattr(gl, name)
        f.restype, f.argtypes = None, args
    if not glfw.glfwInit():
        return dict(error='glfwInit failed')
    window = None
    captures = []
    output_dir.mkdir(parents=True, exist_ok=True)
    try:
        glfw.glfwWindowHint(0x00020004, 0)  # GLFW_VISIBLE
        glfw.glfwWindowHint(0x00022002, 2)  # GLFW_CONTEXT_VERSION_MAJOR
        glfw.glfwWindowHint(0x00022003, 1)  # GLFW_CONTEXT_VERSION_MINOR
        # The hidden window only owns a context. Requested screenshot dimensions
        # belong to an FBO and are independent of remote-desktop window limits.
        window = glfw.glfwCreateWindow(64, 64, b'RmlUi offscreen qualification', None, None)
        if not window:
            return dict(error='glfwCreateWindow failed')
        glfw.glfwMakeContextCurrent(window)
        def extension(name, result, *args):
            address = glfw.glfwGetProcAddress(encode(name)) or glfw.glfwGetProcAddress(encode(name+'EXT'))
            if not address:
                raise RuntimeError('Required FBO extension unavailable: '+name)
            return (C.WINFUNCTYPE if os.name == 'nt' else C.CFUNCTYPE)(result, *args)(address)
        gen_fbo = extension('glGenFramebuffers', None, C.c_int, C.POINTER(C.c_uint))
        bind_fbo = extension('glBindFramebuffer', None, C.c_uint, C.c_uint)
        attach_texture = extension('glFramebufferTexture2D', None, C.c_uint, C.c_uint, C.c_uint, C.c_uint, C.c_int)
        check_fbo = extension('glCheckFramebufferStatus', C.c_uint, C.c_uint)
        delete_fbo = extension('glDeleteFramebuffers', None, C.c_int, C.POINTER(C.c_uint))
        gen_rb = extension('glGenRenderbuffers', None, C.c_int, C.POINTER(C.c_uint))
        bind_rb = extension('glBindRenderbuffer', None, C.c_uint, C.c_uint)
        store_rb = extension('glRenderbufferStorage', None, C.c_uint, C.c_uint, C.c_int, C.c_int)
        attach_rb = extension('glFramebufferRenderbuffer', None, C.c_uint, C.c_uint, C.c_uint, C.c_uint)
        delete_rb = extension('glDeleteRenderbuffers', None, C.c_int, C.POINTER(C.c_uint))
        for width, height, density in selected_cases or ([(w, h, 1.0) for w, h in SIZES] + [(2560, 1440, 2.0), (1920, 1080, 1.5)]):
            framebuffer, texture, stencil = C.c_uint(), C.c_uint(), C.c_uint()
            gen_fbo(1, C.byref(framebuffer))
            bind_fbo(0x8D40, framebuffer.value)  # GL_FRAMEBUFFER
            gl.glGenTextures(1, C.byref(texture))
            gl.glBindTexture(0x0DE1, texture.value)
            gl.glTexParameteri(0x0DE1, 0x2801, 0x2601)
            gl.glTexParameteri(0x0DE1, 0x2800, 0x2601)
            gl.glTexImage2D(0x0DE1, 0, 0x8058, width, height, 0, 0x1908, 0x1401, None)  # RGBA8
            attach_texture(0x8D40, 0x8CE0, 0x0DE1, texture.value, 0)
            gen_rb(1, C.byref(stencil))
            bind_rb(0x8D41, stencil.value)
            store_rb(0x8D41, 0x8D48, width, height)  # GL_STENCIL_INDEX8
            attach_rb(0x8D40, 0x8D20, 0x8D41, stencil.value)
            try:
                status = check_fbo(0x8D40)
                if status != 0x8CD5:
                    captures.append(dict(width=width, height=height, error='Incomplete FBO', status=hex(status)))
                    continue
                if not bridge.initialize_backend(width, height, density, encode(assets), 0):
                    captures.append(dict(width=width, height=height, error='GL2 bridge initialization failed'))
                    continue
                try:
                    for path, scenario in ([(p, None) for p in FIXTURES] + [(p, v) for p, variants in SCENARIOS.items() for v in variants]):
                        if selected_pages and path not in selected_pages:
                            continue
                        doc = bridge.document_open(encode(path), 0)
                        if not doc:
                            captures.append(dict(path=path, error='document_open failed'))
                            continue
                        try:
                            bridge.document_show(doc, 1)
                            bridge.document_show(bridge.home_document(), 0)
                            if scenario:
                                seed_scenario(bridge, doc, path, scenario, touch=touch or density == 2, large_text=large_text)
                            else:
                                seed(bridge, doc, path, touch=touch or density == 2, large_text=large_text)
                            bridge.update()
                            bind_fbo(0x8D40, framebuffer.value)
                            gl.glViewport(0, 0, width, height)
                            gl.glClearColor(0.01, 0.025, 0.04, 1)
                            gl.glClear(0x00004000 | 0x00000400)  # color and stencil
                            bridge.render(width, height)
                            gl.glFinish()
                            pixels = (C.c_ubyte * (width*height*4))()
                            gl.glReadPixels(0, 0, width, height, 0x1908, 0x1401, pixels)
                            target = output_dir/(path.replace('/', '_').replace('.rml', '') + ('_' + scenario if scenario else '') + f'_{width}x{height}_d{density:g}.png')
                            write_png(target, width, height, bytes(pixels))
                            captures.append(dict(png_sha256=hashlib.sha256(target.read_bytes()).hexdigest(), path=path, scenario=scenario, width=width, height=height, density=density, touch=touch or density == 2, large_text=large_text, png=str(target), target='offscreen RGBA8+stencil FBO'))
                        finally:
                            bridge.document_close(doc)
                finally:
                    bridge.shutdown()
            finally:
                bind_fbo(0x8D40, 0)
                delete_rb(1, C.byref(stencil))
                gl.glDeleteTextures(1, C.byref(texture))
                delete_fbo(1, C.byref(framebuffer))
    except Exception as error:
        return dict(error=repr(error), captures=captures)
    finally:
        if window:
            glfw.glfwDestroyWindow(window)
        glfw.glfwTerminate()
    return dict(captures=captures, target='offscreen FBO; hidden 64x64 context window')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--library', type=Path, required=True)
    parser.add_argument('--assets', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True, help='JSON result path')
    parser.add_argument('--allow-failures', action='store_true', help='Write baseline known failures without a failing process exit')
    parser.add_argument('--glfw', type=Path, help='Optional GLFW library for actual hidden GL2 screenshots on Windows/macOS')
    parser.add_argument('--gl-only', action='store_true', help='Capture GL fixtures without repeating the CPU matrix')
    parser.add_argument('--gl-touch', action='store_true', help='Enable touch target styling on every GL capture')
    parser.add_argument('--gl-large-text', action='store_true', help='Enable large text styling on every GL capture')
    parser.add_argument('--gl-page', action='append', choices=sorted(FIXTURES), help='Repeat to capture selected fixture paths and their scenarios only')
    parser.add_argument('--gl-case', action='append', metavar='WIDTHxHEIGHT@DENSITY', help='Repeat to capture selected framebuffer cases; for example 1280x720@1 or 2560x1440@2')
    args = parser.parse_args()
    selected_cases = None
    if args.gl_case:
        selected_cases = []
        for value in args.gl_case:
            match = re.fullmatch(r'(\d+)x(\d+)@(\d+(?:\.\d+)?)', value)
            if not match or not all(float(v) > 0 for v in match.groups()):
                parser.error('--gl-case requires positive WIDTHxHEIGHT@DENSITY')
            w, h, d = match.groups()
            selected_cases.append((int(w), int(h), float(d)))
    library, assets = args.library.resolve(), args.assets.resolve()
    args.output = args.output.resolve()
    report = dict(schema_version=2, harness_sha256=hashlib.sha256(Path(__file__).resolve().read_bytes()).hexdigest(), library=str(library), library_sha256=hashlib.sha256(library.read_bytes()).hexdigest(),
                  assets=str(assets), asset_markup_sha256={p.relative_to(assets).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                      for p in sorted(assets.rglob('*')) if p.suffix in ['.rml', '.rcss']},
                  backend='native CPU draw-list backend 1', populated_fixtures=list(FIXTURES), alternate_states=SCENARIOS, framebuffer_cases=[dict(width=w, height=h, density=d, logical_width=w/d, logical_height=h/d) for w, h, d in CASES],
                  limitations=['Synthetic service fixtures; no physical input, game/service rendering, or end-to-end workflow claim.',
                               'Border-box checks establish viewport/known-container containment, not pixel occlusion.',
                               'Cold-open timings include process-local caches; idle timings are three immediate update/render samples.'])
    try:
        report['fixture_id_validation'] = {path: sorted(set(FIXTURES[path] + CONTENT_ELEMENTS[path] + [e for variant in SCENARIOS.get(path, {}).values() for e in variant]) - fixture_ids(assets, path)) for path in FIXTURES}
        bridge = Bridge(library)
        report['inventory'], report['cases'] = ([], []) if args.gl_only else run_cpu(bridge, assets)
        checks = [check for case in report['cases'] for page in case['pages']
                  for group in ['checks', 'touch_checks', 'large_text_checks', 'combined_accessibility_checks'] for check in page.get(group, [])]
        report['summary'] = dict(documents=len(report['inventory']), fixtures=len(FIXTURES), alternate_states=sum(len(v) for v in SCENARIOS.values()), cases=len(report['cases']),
                                 checks=len(checks), failures=sum(not c['passed'] for c in checks),
                                 initialization_failures=sum(not c['initialized'] for c in report['cases']),
                                 page_errors=sum('error' in p for c in report['cases'] for p in c['pages']),
                                 document_leaks=sum(p.get('opened', False) and p.get('remaining_documents') != 1 for c in report['cases'] for p in c['pages']),
                                 open_failures=sum(not p.get('opened', False) for c in report['cases'] for p in c['pages']),
                                 close_failures=sum(p.get('opened', False) and not p.get('closed', False) for c in report['cases'] for p in c['pages']))
        if args.glfw:
            report['opengl'] = run_gl(bridge, assets, args.glfw.resolve(), args.output.resolve().parent/'screenshots', args.gl_page, selected_cases, args.gl_touch, args.gl_large_text)
    except Exception as error:
        report['fatal_error'] = repr(error)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, allow_nan=False) + '\n', encoding='utf-8')
    print(json.dumps(report.get('summary', {'fatal_error': report.get('fatal_error')})))
    # Always write the full report; only an explicit baseline run may allow failures.
    if 'fatal_error' in report:
        return 1
    summary = report['summary']
    failed = any(summary.get(k, 0) for k in ['failures', 'initialization_failures', 'page_errors', 'document_leaks', 'open_failures', 'close_failures'])
    failed = failed or any(report['fixture_id_validation'].values())
    if args.glfw:
        failed = failed or 'error' in report['opengl'] or any('error' in c for c in report['opengl'].get('captures', []))
    return int(failed and not args.allow_failures)


if __name__ == '__main__':
    raise SystemExit(main())
