"""Synthetic audit-protocol fixtures only; no native form/network/render acceptance."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import unittest

SPEC = importlib.util.spec_from_file_location("hunter_form_audit_tests", Path(__file__).with_name("hunter-form-audit.py"))
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)


def fixture(hunter="Trace"):
    forms = []
    for frame, phase in ((3, 2), (4, 3), (5, 3), (6, 4), (7, 1)):
        active = hunter == "Weavel" and phase in (2, 3)
        hp = 37 if active else 0
        source = struct.pack("<IIB3fBHHH", frame, 2, phase, 0., 0., 0., active, hp, 0, 100).hex()
        forms.append(dict(Stage="authority", Epoch=111, Match=2, Slot=1, Generation=1, Life=1,
            Source=source, ObservedFrame=frame, Phase=AUDIT.legacy.PHASES[phase], BodyHp=100,
            TurretHp=hp, TurretActive=active, X=0., Y=0., Z=0., BodyX=float(frame-3), BodyY=0., BodyZ=0.,
            ObservedDamageEvent=0, BodyPredictionDebit=0, LocalSlot=None, Spectating=None,
            AuthoritySpectating=None, Hunter=hunter))
    markers = dict(OwnerFormScope=AUDIT.OWNER_SCOPE, OwnerFormCountingUnit=AUDIT.OWNER_UNIT, OwnerForms=[])
    server = dict(Forms=copy.deepcopy(forms), FormHunterScope=AUDIT.HUNTER_SCOPE, **copy.deepcopy(markers))
    peers = {}
    for slot in (0, 1, 2):
        rows = copy.deepcopy(forms)
        for row in rows:
            row.update(Stage="owner" if slot == 1 else "observer", LocalSlot=slot,
                       Spectating=False, AuthoritySpectating=False)
        native = []
        if slot == 1:
            for index, row in enumerate(rows):
                value = {key: row[key] for key in AUDIT.NATIVE_FIELDS if key in row}
                value.update(Stage="owner-native-after-simulation", Frame=row["ObservedFrame"],
                             SceneFrame=index, NativeForm=row["Source"])
                native.append(value)
        peers[f"peer-{slot}"] = dict(Forms=rows, Windows=[dict(Epoch=111, Match=2, LocalSlot=slot)],
            Role="simulation-client", FormHunterScope=AUDIT.HUNTER_SCOPE, **copy.deepcopy(markers))
        peers[f"peer-{slot}"]["OwnerForms"] = native
    source = json.dumps(dict(head="1" * 40, files=[dict(path=AUDIT.CAPTURE_PATH, sha256="2" * 64)])).encode()
    pin = AUDIT.OwnerProvenance("3" * 64, hashlib.sha256(source).hexdigest(), "2" * 64)
    identity = "1" * 40 + "+dirty-manifest:" + pin.sourceManifestSha256
    manifest = dict(source=identity, assemblySha256=pin.clientDllSha256, assemblyAfterSha256=pin.clientDllSha256,
        settings=dict(source=identity, scenario="alt-morph", form_hunter=hunter))
    summary = dict(status="PASS", processes=[dict(name=name) for name in ("server", *peers)],
                   coverage=dict(boundedHeadlessDrain="PASS"))
    return server, peers, summary, manifest, pin, source


class HunterFormAuditTests(unittest.TestCase):
    def setUp(self):
        self.server, self.peers, self.summary, self.manifest, self.pin, self.source = fixture()

    def audit(self, hunter="Trace", **kwargs):
        return AUDIT.audit(self.server, self.peers, self.summary, self.manifest, hunter,
            expected_owner_provenance=kwargs.get("pin", self.pin), source_manifest_bytes=kwargs.get("source", self.source))

    def test_all_seven_explicit_hunters_need_actual_native_and_independent_cycles(self):
        for hunter in AUDIT.HUNTERS:
            with self.subTest(hunter=hunter):
                server, peers, summary, manifest, pin, source = fixture(hunter)
                result = AUDIT.audit(server, peers, summary, manifest, hunter,
                    expected_owner_provenance=pin, source_manifest_bytes=source)
                self.assertEqual(result["status"], "PASS", result)
                self.assertEqual(result["metrics"]["hunterTransitions"]["qualifiedLives"][0]["independentObservers"], ["peer-0", "peer-2"])
                self.assertFalse(result["metrics"]["hunterTransitions"]["acceptedSourceOwnerRowsAreNativeProof"])

    def test_default_is_exact_unchanged_legacy_dictionary(self):
        server, peers, summary, manifest, _, _ = fixture("Weavel")
        expected = AUDIT.legacy.audit(server, peers, summary, manifest)
        self.assertEqual(AUDIT.audit(server, peers, summary, manifest), expected)
        peers["peer-2"]["Forms"].pop(-2)
        self.assertEqual(AUDIT.audit(server, peers, summary, manifest), AUDIT.legacy.audit(server, peers, summary, manifest))

    def test_mixed_hunters_and_only_preselection_biped_setup_are_not_target_cycles(self):
        # Real pilot shape: authority's actual slot1 native starts as Samus
        # Biped before Weavel installation, without changing its full life key.
        for hunter in ("Trace", "Weavel"):
            server, peers, summary, manifest, pin, source = fixture(hunter)
            def biped(slot, actual_hunter):
                row = copy.deepcopy(server["Forms"][0])
                row.update(Slot=slot, Hunter=actual_hunter, Phase="Biped", ObservedFrame=1,
                    Source=struct.pack("<IIB3fBHHH", 1, 0, 1, 0., 0., 0., False, 0, 0, 100).hex(),
                    TurretHp=0, TurretActive=False)
                return row
            setup = biped(1, "Samus")
            others = [biped(0, "Samus"), biped(2, "Spire")]
            server["Forms"] = [setup, *others, *server["Forms"]]
            for name, peer in peers.items():
                slot = peer["Windows"][0]["LocalSlot"]
                rows = copy.deepcopy([setup, *others])
                for row in rows:
                    row.update(Stage="owner" if row["Slot"] == slot else "observer", LocalSlot=slot,
                        Spectating=False, AuthoritySpectating=False)
                peer["Forms"] = rows + peer["Forms"]
            result = AUDIT.audit(server, peers, summary, manifest, hunter,
                expected_owner_provenance=pin, source_manifest_bytes=source)
            self.assertEqual(result["status"], "PASS", result)
            self.assertEqual(result["metrics"]["hunterTransitions"]["preselectionBipedSetupRows"],
                {"server": 1, "peer-0": 1, "peer-1": 1, "peer-2": 1})
            self.assertEqual(len(result["metrics"]["hunterTransitions"]["qualifiedLives"]), 1)

    def test_wrong_hunter_transition_or_after_selection_cannot_be_discounted(self):
        # A wrong native Hunter during first Morphing is a failure, not setup.
        self.server["Forms"][0]["Hunter"] = "Samus"
        self.assertEqual(self.audit()["status"], "FAIL")
        self.setUp()
        # Even settled Biped must retain the selected Hunter once bound.
        self.server["Forms"][-1]["Hunter"] = "Samus"
        self.assertEqual(self.audit()["status"], "FAIL")
        self.setUp(); self.peers["peer-0"]["Forms"][-1]["Hunter"] = "Samus"
        self.assertEqual(self.audit()["status"], "FAIL")
        self.setUp()
        # A predicted owner's Biped cannot hide a source Morphing transition.
        row = self.peers["peer-1"]["Forms"][0]
        row.update(Hunter="Samus", Phase="Biped")
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_only_immediately_previous_exact_authority_observation_can_repeat(self):
        original = copy.deepcopy(self.server["Forms"][0])
        self.server["Forms"].insert(1, copy.deepcopy(original))
        result = self.audit()
        self.assertEqual(result["status"], "PASS", result)
        self.assertEqual(result["metrics"]["hunterTransitions"]["repeatedAuthorityObservations"], 1)
        self.setUp()
        conflict = copy.deepcopy(self.server["Forms"][0]); conflict["BodyX"] += 1
        self.server["Forms"].insert(1, conflict)
        self.assertEqual(self.audit()["status"], "FAIL")
        self.setUp()
        # Global set dedup would hide this stale packet after a newer frame.
        self.server["Forms"].insert(2, copy.deepcopy(self.server["Forms"][0]))
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_missing_actual_hunter_producer_is_incomplete_without_backfill(self):
        before = copy.deepcopy(self.peers)
        for report in (self.server, *self.peers.values()):
            report.pop("FormHunterScope")
            for row in report["Forms"]: row.pop("Hunter")
        result = self.audit()
        self.assertEqual(result["status"], "INCOMPLETE", result)
        self.assertTrue(any("producer fields" in reason for reason in result["incomplete"]))
        self.assertNotEqual(self.peers, before)
        self.assertTrue(all("Hunter" not in row for peer in self.peers.values() for row in peer["Forms"]))

    def test_null_hunter_is_incomplete_wrong_hunter_or_scope_fails(self):
        for process in ("authority", "observer"):
            self.setUp()
            report = self.server if process == "authority" else self.peers["peer-0"]
            report["Forms"][0]["Hunter"] = None
            self.assertEqual(self.audit()["status"], "INCOMPLETE")
            report["Forms"][0]["Hunter"] = "Spire"
            self.assertEqual(self.audit()["status"], "FAIL")
        self.setUp(); self.server["FormHunterScope"] = "future"
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_accepted_source_owner_cycle_is_never_native_owner_proof(self):
        self.peers["peer-1"]["OwnerForms"] = []
        self.assertEqual(self.audit()["status"], "INCOMPLETE")
        self.peers["peer-1"]["OwnerForms"] = copy.deepcopy(self.peers["peer-1"]["Forms"])
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_partial_native_schema_and_native_wrong_identity_or_hunter_fail(self):
        for field, value in (("Stage", "owner"), ("Hunter", "Spire"), ("Life", 2), ("LocalSlot", 0),
                             ("SceneFrame", -1), ("Spectating", True), ("BodyHp", 99)):
            self.setUp(); self.peers["peer-1"]["OwnerForms"][0][field] = value
            self.assertEqual(self.audit()["status"], "FAIL", (field, self.audit()))
        self.setUp(); self.peers["peer-0"].pop("OwnerFormScope")
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_owner_native_monotonic_frames_and_scene_frames_required(self):
        for field in ("Frame", "SceneFrame"):
            self.setUp(); native = self.peers["peer-1"]["OwnerForms"]
            native[1][field] = native[0][field]
            self.assertEqual(self.audit()["status"], "FAIL")

    def test_two_distinct_observers_must_finish_same_target_life(self):
        self.peers["peer-2"]["Forms"].pop(-2)
        self.assertEqual(self.audit()["status"], "INCOMPLETE")
        self.setUp()
        for row in self.peers["peer-2"]["Forms"]: row["Phase"] = "Biped"
        self.assertEqual(self.audit()["status"], "FAIL")
        self.setUp(); self.summary["processes"].pop(); self.peers.pop("peer-2")
        self.assertEqual(self.audit()["status"], "INCOMPLETE")

    def test_cannot_stitch_cycle_across_lives_or_process_owner_samples(self):
        for report in (self.server, *self.peers.values()):
            for row in report["Forms"][3:]: row["Life"] = 2
        for row in self.peers["peer-1"]["OwnerForms"][3:]: row["Life"] = 2
        self.assertEqual(self.audit()["status"], "INCOMPLETE")

    def test_exact_authority_sources_and_fresh_order_are_required(self):
        self.peers["peer-0"]["Forms"][0]["Source"] = "00"
        self.assertEqual(self.audit()["status"], "FAIL")
        self.setUp(); self.server["Forms"][0], self.server["Forms"][1] = self.server["Forms"][1], self.server["Forms"][0]
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_provenance_is_independent_and_pins_exact_manifest_capture_and_dll(self):
        self.assertEqual(self.audit(pin=None)["status"], "FAIL")
        self.assertEqual(self.audit(source=self.source + b" ")["status"], "FAIL")
        self.manifest["assemblyAfterSha256"] = "4" * 64
        self.assertEqual(self.audit()["status"], "FAIL")
        self.setUp(); self.manifest["source"] = "1" * 40
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_source_manifest_capture_entry_must_be_unique_and_pinned(self):
        for entries in ([], [dict(path=AUDIT.CAPTURE_PATH, sha256="4" * 64)],
                        [dict(path=AUDIT.CAPTURE_PATH, sha256="2" * 64)] * 2):
            self.setUp()
            self.source = json.dumps(dict(head="1" * 40, files=entries)).encode()
            self.pin = AUDIT.OwnerProvenance("3" * 64, hashlib.sha256(self.source).hexdigest(), "2" * 64)
            identity = "1" * 40 + "+dirty-manifest:" + self.pin.sourceManifestSha256
            self.manifest["source"] = self.manifest["settings"]["source"] = identity
            self.assertEqual(self.audit()["status"], "FAIL")

    def test_authority_cannot_supply_native_owner_rows_or_observer_become_owner(self):
        self.server["OwnerForms"] = copy.deepcopy(self.peers["peer-1"]["OwnerForms"])
        self.assertEqual(self.audit()["status"], "FAIL")
        self.setUp(); self.peers["peer-0"]["Forms"][0]["Stage"] = "owner"
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_non_weavel_cannot_pass_with_observed_or_source_turret_state(self):
        self.peers["peer-0"]["Forms"][0]["TurretHp"] = 1
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_incomplete_native_schema_is_distinct_from_malformed_partial_schema(self):
        for marker in AUDIT.MARKERS: self.peers["peer-1"].pop(marker)
        self.assertEqual(self.audit()["status"], "INCOMPLETE")
        self.peers["peer-1"]["OwnerFormScope"] = AUDIT.OWNER_SCOPE
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_base_health_drain_overflow_and_weavel_turret_gates_remain(self):
        for state in ("FAIL", "INCOMPLETE"):
            self.setUp(); self.summary["status"] = state
            self.assertEqual(self.audit()["status"], state)
        self.setUp(); self.summary["coverage"]["boundedHeadlessDrain"] = "INCOMPLETE"
        self.assertEqual(self.audit()["status"], "INCOMPLETE")
        self.setUp(); self.peers["peer-0"]["Overflow"] = 1
        self.assertEqual(self.audit()["status"], "FAIL")
        server, peers, summary, manifest, pin, source = fixture("Weavel")
        peers["peer-0"]["Forms"][0]["TurretHp"] -= 1
        result = AUDIT.audit(server, peers, summary, manifest, "Weavel", expected_owner_provenance=pin, source_manifest_bytes=source)
        self.assertEqual(result["status"], "FAIL", result)
        self.assertTrue(any("health" in reason or "mismatch" in reason for reason in result["failures"]))

    def test_selected_hunter_must_match_explicit_dispatch(self):
        self.assertEqual(self.audit("Spire")["status"], "FAIL")
        self.manifest["settings"].pop("form_hunter")
        self.assertEqual(self.audit()["status"], "FAIL")


if __name__ == "__main__": unittest.main()
