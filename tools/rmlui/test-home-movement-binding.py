#!/usr/bin/env python3
"""Regress Home data-model rule17 Movement and rule18 M7 bindings/callbacks.

Usage: test-home-movement-binding.py NATIVE_LIBRARY RUNTIME_RMLUI_ASSETS
Uses native CPU backend1 and a minimal invented prime_home.rml attached to the
actual prime_menu model. It intentionally has NO data-action fallback: real
pointer clicks must reach the model's guarded lobby_rules_toggle17/18 callbacks.
Only the three openly licensed runtime fonts are copied into temporary assets.
The same test must fail before the missing binding/callback fix and pass after.
"""
from __future__ import annotations

import argparse
import ctypes as C
import hashlib
import json
from pathlib import Path
import shutil
import tempfile


class Intent(C.Structure):
    _fields_ = [("size", C.c_uint32), ("version", C.c_uint32), ("kind", C.c_uint32), ("argument", C.c_int32),
                ("generation", C.c_uint64), ("document", C.c_uint64), ("sequence", C.c_uint64)]


class HomeProbe:
    def __init__(self, library):
        self.lib = C.CDLL(str(library))
        self.checks, self.failures = 0, []
        self.trace = []
        self.last_sequence = 0
        signatures = {
            "initialize_backend": (C.c_int, [C.c_int, C.c_int, C.c_float, C.c_char_p, C.c_int]),
            "generation": (C.c_uint64, []), "home_document": (C.c_uint64, []),
            "shutdown": (None, []), "update": (None, []),
            "document_set_text": (C.c_int, [C.c_uint64, C.c_char_p, C.c_char_p]),
            "document_set_bool": (C.c_int, [C.c_uint64, C.c_char_p, C.c_int]),
            "document_element_bounds": (C.c_int, [C.c_uint64, C.c_char_p] + [C.POINTER(C.c_float)] * 4),
            "document_accessibility_snapshot": (C.c_int, [C.c_uint64, C.POINTER(C.c_uint8), C.c_int]),
            "mouse_move": (C.c_int, [C.c_int, C.c_int, C.c_int]),
            "mouse_button": (C.c_int, [C.c_int, C.c_int, C.c_int]),
            "take_intent": (C.c_int, [C.POINTER(Intent)]),
        }
        for name, (result, arguments) in signatures.items():
            function = getattr(self.lib, "pp_rmlui_" + name)
            function.restype, function.argtypes = result, arguments
            setattr(self, name, function)

    def check(self, condition, label):
        self.checks += 1
        if not condition:
            self.failures.append(label)

    def require(self, condition, label):
        self.check(condition, label)
        if not condition:
            raise RuntimeError(label)

    def flags(self, home, lobby=True, opened=True, owner=True):
        # Entering lobby mode itself closes its rules in the actual model;
        # set the explicit rules flag afterwards, exactly as an owner would.
        for key, value in (("lobby_mode", lobby), ("lobby_rules_open", opened), ("rules_owner", owner)):
            self.require(self.document_set_bool(home, key.encode(), int(value)) == 1, "existing_bool_owner_accepts_" + key)
        self.update()
        self.trace.append({"stage": "set_flags", "requested": {"lobby": lobby, "opened": opened, "owner": owner},
                           "model": self.snapshot(home).get("guard_state", {}).get("label")})

    def snapshot(self, home):
        buffer = (C.c_uint8 * (1024 * 1024 + 1))()
        count = self.document_accessibility_snapshot(home, buffer, len(buffer))
        self.require(0 < count < len(buffer), "bounded_live_accessibility_projection_available")
        snapshot = json.loads(bytes(buffer[:count]).decode("utf-8"))
        self.require(snapshot["version"] == 1 and snapshot["generation"] == self.generation()
                     and snapshot["document"] == home, "actual_home_semantic_snapshot_has_current_identity")
        return {node["id"]: node for node in snapshot["nodes"] if node["id"]}

    def intents(self):
        result = []
        for _ in range(257):
            intent = Intent(size=40, version=1)
            if not self.take_intent(C.byref(intent)):
                return result
            result.append(intent)
        raise RuntimeError("bounded_native_intent_queue_did_not_drain")

    def click(self, home, element, stage="initial"):
        self.require(not self.intents(), "fixture_enters_each_click_without_pending_intents")
        x, y, width, height = (C.c_float() for _ in range(4))
        self.require(self.document_element_bounds(home, element.encode(), C.byref(x), C.byref(y), C.byref(width), C.byref(height)) == 1
                     and width.value > 0 and height.value > 0, "actual_dom_button_has_clickable_bounds_" + element)
        self.mouse_move(round(x.value + width.value / 2), round(y.value + height.value / 2), 0)
        self.mouse_button(0, 1, 0)
        self.mouse_button(0, 0, 0)
        self.update()
        intents = self.intents()
        self.trace.append({"stage": stage, "element": element,
                           "bounds": [x.value, y.value, width.value, height.value],
                           "model": self.snapshot(home).get("guard_state", {}).get("label"),
                           "actions": [{"kind": p.kind, "argument": p.argument, "generation": p.generation,
                                        "document": p.document, "sequence": p.sequence} for p in intents]})
        return intents

    def accepted(self, home, element, argument, stage="initial"):
        intents = self.click(home, element, stage)
        self.check(len(intents) == 1, "model_callback_emits_exactly_once_" + stage + "_" + element)
        if len(intents) == 1:
            event = intents[0]
            self.check(event.size == 40 and event.version == 1 and event.kind == 42 and event.argument == argument,
                       "model_callback_uses_literal_lobby_rule_kind42_argument_" + str(argument))
            self.check(event.generation == self.generation() and event.document == home and event.sequence > self.last_sequence,
                       "model_callback_preserves_home_owner_and_monotonic_sequence_" + element)
            self.last_sequence = event.sequence

    def run(self, assets):
        self.require(self.initialize_backend(640, 360, 1, str(assets).encode(), 1) == 1, "actual_home_cpu_backend_initialized")
        home = self.home_document()
        self.require(home != 0 and self.generation() != 0, "actual_home_lifetime_nonzero")
        self.flags(home)
        for key, value in ((b"rules_toggle16", b"OWN_CONTROL_MODE"), (b"rules_toggle17", b"OWN_MOVEMENT_MODE"),
                           (b"rules_toggle18", b"OWN_M7_MODE")):
            self.require(self.document_set_text(home, key, value) == 1, "existing_home_text_owner_accepts_" + key.decode())
        self.update()
        nodes = self.snapshot(home)
        self.check(nodes.get("control", {}).get("label") == "CONTROL OWN_CONTROL_MODE", "known_seventeenth_binding_renders_updated_text")
        self.check(nodes.get("movement", {}).get("label") == "MOVEMENT OWN_MOVEMENT_MODE", "eighteenth_movement_binding_renders_updated_text")
        self.check(nodes.get("m7", {}).get("label") == "M7 OWN_M7_MODE", "nineteenth_m7_binding_renders_updated_text")
        # A known predecessor proves this fixture actually reaches Home's model
        # event machinery. No registry/data-action surrogate can make it green.
        self.accepted(home, "control", 16)
        self.accepted(home, "movement", 17)
        self.accepted(home, "m7", 18)
        self.accepted(home, "routed_m7", 18, "generic_route")
        self.check(not self.click(home, "out_of_range", "generic_route"), "generic_action_rejects_first_unsupported_rule19")
        for key, value in ((b"rules_toggle16", b"OWN_CONTROL_CHANGED"), (b"rules_toggle17", b"OWN_MOVEMENT_CHANGED"),
                           (b"rules_toggle18", b"OWN_M7_CHANGED")):
            self.require(self.document_set_text(home, key, value) == 1, "repeat_home_text_owner_accepts_" + key.decode())
        self.update()
        nodes = self.snapshot(home)
        self.check(nodes.get("control", {}).get("label") == "CONTROL OWN_CONTROL_CHANGED", "seventeenth_binding_reacts_to_second_snapshot")
        self.check(nodes.get("movement", {}).get("label") == "MOVEMENT OWN_MOVEMENT_CHANGED", "movement_binding_reacts_to_second_snapshot")
        self.check(nodes.get("m7", {}).get("label") == "M7 OWN_M7_CHANGED", "m7_binding_reacts_to_second_snapshot")
        for name, flags in (("not_owner", dict(owner=False)), ("rules_closed", dict(opened=False)), ("not_lobby", dict(lobby=False))):
            self.flags(home, **flags)
            for element in ("control", "movement", "m7"):
                self.check(not self.click(home, element, name), "model_owner_guard_refuses_" + name + "_" + element)
        self.flags(home)
        self.accepted(home, "control", 16, "restored")
        self.accepted(home, "movement", 17, "restored")
        self.accepted(home, "m7", 18, "restored")


def assets(root, source):
    (root / "fonts").mkdir()
    for name in ("Rajdhani-SemiBold.ttf", "Rajdhani-Bold.ttf", "JetBrainsMono-Regular.ttf"):
        font = source / "fonts" / name
        if not font.is_file() or font.is_symlink():
            raise ValueError("missing regular runtime font: " + name)
        shutil.copyfile(font, root / "fonts" / name)
    # Deliberately avoid disabled/data-if attributes: the three production model
    # callback predicates themselves must refuse an otherwise clickable button.
    (root / "prime_home.rml").write_text("""<rml><head><title>Own Home movement fixture</title><style>
        body {font-family:Rajdhani;font-weight:600;font-size:24px;margin:0px;}
        button {display:block;width:480px;height:48px;margin:8px;tab-index:auto;}
        </style></head><body data-model="prime_menu">
        <button id="control" data-event-click="lobby_rules_toggle16">CONTROL {{rules_toggle16}}</button>
        <button id="movement" data-event-click="lobby_rules_toggle17">MOVEMENT {{rules_toggle17}}</button>
        <button id="m7" data-event-click="lobby_rules_toggle18">M7 {{rules_toggle18}}</button>
        <button id="routed_m7" data-action="lobby:rules-toggle:18">ROUTED M7</button>
        <button id="out_of_range" data-action="lobby:rules-toggle:19">INVALID RULE</button>
        <div id="guard_state" role="status" style="position:absolute;left:500px;top:8px;width:140px;height:80px;">L {{lobby_mode}} R {{lobby_rules_open}} O {{rules_owner}}</div>
        </body></rml>""")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("library", type=Path)
    parser.add_argument("runtime_assets", type=Path)
    args = parser.parse_args()
    if C.sizeof(Intent) != 40:
        raise RuntimeError("host_ctypes_intent_layout_mismatch")
    library = args.library.resolve(strict=True)
    digest = hashlib.sha256(library.read_bytes()).hexdigest()
    probe = HomeProbe(library)
    try:
        with tempfile.TemporaryDirectory(prefix="prime-home-movement-own-") as temporary:
            root = Path(temporary)
            assets(root, args.runtime_assets.resolve(strict=True))
            try:
                probe.run(root)
            finally:
                probe.shutdown()
    except (ValueError, RuntimeError, KeyError, OSError) as error:
        probe.failures.append("fixture_setup_or_native_boundary: " + str(error))
    probe.check(hashlib.sha256(library.read_bytes()).hexdigest() == digest, "native_library_unchanged")
    print(json.dumps({"result": "FAIL" if probe.failures else "PASS", "checks": probe.checks, "failures": probe.failures, "actions": probe.trace,
                      "library_sha256": digest, "backend": 1, "temporary_assets_removed": True,
                      "scope": "invented Home markup using real model binding/callbacks; no GL or game media"}, sort_keys=True))
    return int(bool(probe.failures))


if __name__ == "__main__":
    raise SystemExit(main())
