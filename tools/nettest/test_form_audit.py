import copy
import importlib.util
from pathlib import Path
import struct
import unittest

spec = importlib.util.spec_from_file_location("form_audit", Path(__file__).with_name("form-audit.py"))
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)


def row(frame, phase, active=False, body=(0, 0, 0), hp=100, damage=0, started=2, body_hp=100):
    anchor = (0., 0., 0.)
    source = struct.pack("<IIB3fBHHH", frame, started, phase, *anchor, active, hp if active else 0, damage, body_hp).hex()
    return dict(Stage="authority", Epoch=111, Match=2, Slot=1, Generation=1, Life=1,
                Source=source, ObservedFrame=frame, Phase=module.PHASES[phase], BodyHp=body_hp,
                TurretHp=hp if active else 0, TurretActive=active, X=0., Y=0., Z=0.,
                BodyX=body[0], BodyY=body[1], BodyZ=body[2], ObservedDamageEvent=damage, BodyPredictionDebit=0)


class FormAuditTests(unittest.TestCase):
    def setUp(self):
        self.server = dict(Forms=[row(3, 2, True), row(4, 3, True, body=(1, 0, 0)),
                                  row(5, 3, True, body=(2, 0, 0)), row(6, 4), row(7, 1)])
        self.peers = {}
        for slot in (0, 1, 2):
            rows = copy.deepcopy(self.server["Forms"])
            for item in rows: item["Stage"] = "owner" if slot == 1 else "observer"
            self.peers[f"peer-{slot}"] = dict(Forms=rows, Windows=[dict(Epoch=111, Match=2, LocalSlot=slot)])
        self.summary = dict(status="PASS", processes=[dict(name=n) for n in ("server", *self.peers)],
                            coverage=dict(boundedHeadlessDrain="PASS"))
        self.manifest = dict(scenario="alt-morph", observerDelay=3)

    def audit(self): return module.audit(self.server, self.peers, self.summary, self.manifest)

    def test_positive_exact_sources_native_owner_phase_lead(self):
        self.peers["peer-1"]["Forms"][0]["Phase"] = "Alt"
        result = self.audit()
        self.assertEqual(result["status"], "PASS", result)
        self.assertEqual(result["metrics"]["ownerPhaseDifferences"], 1)
        self.assertGreater(result["metrics"]["independentAnchorWitnesses"], 0)

    def test_obsolete_form26_and_source_body_health_mismatch_fail(self):
        self.peers["peer-0"]["Forms"][0]["Source"] = self.peers["peer-0"]["Forms"][0]["Source"][:-4]
        self.assertEqual(self.audit()["status"], "FAIL")
        self.setUp(); self.server["Forms"][0]["BodyHp"] = 99
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_wrong_full_key_source_or_missing_source_fail(self):
        for field, value in (("Life", 2), ("Epoch", 222), ("Source", "00")):
            with self.subTest(field=field):
                self.setUp(); self.peers["peer-0"]["Forms"][0][field] = value
                self.assertEqual(self.audit()["status"], "FAIL")

    def test_anchor_phase_presence_or_hp_mismatch_fail(self):
        for field, value in (("X", 1), ("Phase", "Biped"), ("TurretActive", False), ("TurretHp", 99), ("BodyHp", 99)):
            with self.subTest(field=field):
                self.setUp(); self.peers["peer-0"]["Forms"][0][field] = value
                self.assertEqual(self.audit()["status"], "FAIL")

    def test_duplicate_or_stale_applied_stream_fail(self):
        for duplicate in (True, False):
            self.setUp(); rows = self.peers["peer-0"]["Forms"]
            if duplicate: rows.append(copy.deepcopy(rows[0]))
            else: rows[0], rows[1] = rows[1], rows[0]
            self.assertEqual(self.audit()["status"], "FAIL")

    def test_stale_transition_even_with_newer_source_frame_fails(self):
        stale = row(8, 3, True, started=1)
        self.server["Forms"].append(stale)
        self.peers["peer-0"]["Forms"].append({**stale, "Stage": "observer"})
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_missing_phase_active_initial_anchor_or_body_motion_incomplete(self):
        for kind in ("phase", "initial", "motion", "body_fields"):
            with self.subTest(kind=kind):
                self.setUp(); rows = self.peers["peer-2"]["Forms"]
                if kind == "phase": rows.pop(-2)
                elif kind == "initial":
                    self.server["Forms"].insert(0, row(2, 1)); rows.insert(0, {**row(2, 1), "Stage": "observer"})
                elif kind == "motion":
                    for item in rows: item["BodyX"] = 1
                else:
                    for item in rows: del item["BodyX"]
                self.assertEqual(self.audit()["status"], "INCOMPLETE")

    def test_newer_health_requires_exact_authority_same_life_transition(self):
        item = self.peers["peer-0"]["Forms"][0]
        item.update(TurretHp=90, BodyHp=95, ObservedDamageEvent=1)
        self.assertEqual(self.audit()["status"], "INCOMPLETE")
        newer = row(8, 3, True, hp=90, damage=1, body_hp=95)
        self.server["Forms"].append(newer)
        self.assertEqual(self.audit()["status"], "PASS")
        newer["Life"] = 2
        self.assertEqual(self.audit()["status"], "INCOMPLETE")

    def test_authority_conflict_overflow_and_base_fail_not_hidden(self):
        self.server["Forms"][0]["X"] = 1
        self.assertEqual(self.audit()["status"], "FAIL")
        self.setUp(); self.summary["status"] = "FAIL"
        self.assertEqual(self.audit()["status"], "FAIL")
        self.setUp(); self.peers["peer-0"]["Overflow"] = 1
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_wrong_owner_role_cannot_waive_observer_phase(self):
        self.peers["peer-0"]["Forms"][0].update(Stage="owner", Phase="Biped")
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_actual_membership_and_first_bootstrap_self_observer(self):
        self.peers["peer-1"]["Forms"][0]["Stage"] = "observer"
        self.assertEqual(self.audit()["status"], "PASS")
        self.peers["peer-1"]["Forms"][1]["Stage"] = "observer"
        self.assertEqual(self.audit()["status"], "FAIL")
        self.setUp()
        self.peers["peer-0"], self.peers["peer-1"] = self.peers["peer-1"], self.peers["peer-0"]
        self.assertEqual(self.audit()["status"], "PASS")

    def test_bootstrap_self_observer_does_not_get_owner_phase_waiver(self):
        self.peers["peer-1"]["Forms"][0].update(Stage="observer", Phase="Biped")
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_spectator_self_remains_observer_and_checks_phase(self):
        spectator = row(3, 1); spectator["Slot"] = 2
        self.server["Forms"].append(spectator)
        self.peers["peer-2"]["Windows"][0]["Spectating"] = True
        self.peers["peer-2"]["Forms"] += [{**spectator, "Stage": "observer"},
            {**spectator, "Source": row(4, 1)["Source"], "Stage": "observer", "ObservedFrame": 4}]
        self.server["Forms"].append({**spectator, "Source": row(4, 1)["Source"], "ObservedFrame": 4})
        self.assertEqual(self.audit()["status"], "PASS")
        self.peers["peer-2"]["Forms"][-1]["Phase"] = "Alt"
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_non_weavel_body_health_retains_base_fast_lane_gate(self):
        other = row(3, 1); other["Slot"] = 0
        self.server["Forms"].append(other)
        self.peers["peer-2"]["Forms"].append({**other, "Stage": "observer", "BodyHp": 27})
        self.assertEqual(self.audit()["status"], "PASS")
        self.summary["status"] = "FAIL"
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_early_biped_does_not_replace_settled_phase_after_unmorph(self):
        for rows in (self.server["Forms"], *(p["Forms"] for p in self.peers.values())):
            old = rows.pop(); old["Source"] = row(2, 1)["Source"]; old["ObservedFrame"] = 2; rows.insert(0, old)
        self.manifest["observerDelay"] = 0
        self.assertEqual(self.audit()["status"], "INCOMPLETE")

    def test_actual_runner_manifest_settings_supported(self):
        self.manifest = {"settings": self.manifest}
        self.assertEqual(self.audit()["status"], "PASS")

    def test_exact_remote_prediction_debit_and_nonlethal_floor(self):
        sample = self.peers["peer-0"]["Forms"][0]
        sample.update(BodyHp=90, BodyPredictionDebit=10)
        self.assertEqual(self.audit()["status"], "PASS")
        sample["BodyHp"] = 89
        self.assertEqual(self.audit()["status"], "FAIL")
        sample.update(BodyHp=1, BodyPredictionDebit=1000)
        self.assertEqual(self.audit()["status"], "PASS")
        sample["BodyPredictionDebit"] = -1
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_missing_debit_incomplete_and_local_debit_forbidden(self):
        sample = self.peers["peer-0"]["Forms"][0]
        sample["BodyHp"] = 90; del sample["BodyPredictionDebit"]
        self.assertEqual(self.audit()["status"], "INCOMPLETE")
        self.setUp(); self.peers["peer-1"]["Forms"][0].update(BodyHp=90, BodyPredictionDebit=10)
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_newer_authority_hp_basis_still_requires_exact_debit(self):
        sample = self.peers["peer-0"]["Forms"][0]
        sample.update(BodyHp=85, TurretHp=90, ObservedDamageEvent=1, BodyPredictionDebit=10)
        self.server["Forms"].append(row(8, 3, True, hp=90, damage=1, body_hp=95))
        self.assertEqual(self.audit()["status"], "PASS")
        sample["BodyHp"] = 84
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_inactive_anchor_not_location_proof_but_active_anchor_required(self):
        sample = self.peers["peer-0"]["Forms"][-1]
        sample.update(X=100, Y=2, Z=5)
        self.assertEqual(self.audit()["status"], "PASS")
        sample = self.peers["peer-0"]["Forms"][0]; sample["X"] = 100
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_peer_inventory_and_drain_required(self):
        del self.peers["peer-2"]
        self.assertEqual(self.audit()["status"], "INCOMPLETE")
        self.setUp(); self.summary["coverage"] = {}
        self.assertEqual(self.audit()["status"], "INCOMPLETE")


if __name__ == "__main__": unittest.main()
