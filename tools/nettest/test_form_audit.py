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

    def sample_roles(self):
        for slot in (0, 1, 2):
            for item in self.peers[f"peer-{slot}"]["Forms"]:
                item.update(LocalSlot=slot, Spectating=False, AuthoritySpectating=False)

    def test_sample_time_owner_before_later_spectator_window(self):
        self.sample_roles()
        self.peers["peer-1"]["Windows"][0]["Spectating"] = True
        self.peers["peer-1"]["Forms"][2]["Phase"] = "Unmorphing"
        self.assertEqual(self.audit()["status"], "PASS")

    def test_sample_time_spectator_has_no_phase_waiver(self):
        for field in ("Spectating", "AuthoritySpectating"):
            self.setUp(); self.sample_roles()
            self.peers["peer-1"]["Forms"][2].update({field: True, "Phase": "Unmorphing"})
            self.assertEqual(self.audit()["status"], "FAIL")

    def test_sample_time_roles_reject_partial_malformed_membership(self):
        for field, value in (("LocalSlot", 0), ("Spectating", 1), ("AuthoritySpectating", "false"), ("Spectating", None)):
            self.setUp(); self.sample_roles()
            self.peers["peer-1"]["Forms"][0][field] = value
            self.assertEqual(self.audit()["status"], "FAIL")

    def test_sample_time_all_null_roles_are_not_legacy(self):
        self.sample_roles()
        self.peers["peer-1"]["Forms"][0].update(LocalSlot=None, Spectating=None, AuthoritySpectating=None)
        self.assertEqual(self.audit()["status"], "FAIL")

    def test_sample_time_bootstrap_self_only_first_observer(self):
        self.sample_roles()
        self.peers["peer-1"]["Forms"][0]["Stage"] = "observer"
        self.assertEqual(self.audit()["status"], "PASS")
        self.peers["peer-1"]["Forms"][1]["Stage"] = "observer"
        self.assertEqual(self.audit()["status"], "FAIL")

    def audit(self): return module.audit(self.server, self.peers, self.summary, self.manifest)

    def test_positive_exact_sources_native_owner_phase_lead(self):
        self.peers["peer-1"]["Forms"][2]["Phase"] = "Unmorphing"
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
                self.assertEqual(self.audit()["status"], "PASS" if kind == "initial" else "INCOMPLETE")
                if kind == "initial":
                    self.assertEqual(self.audit()["metrics"]["peers"]["peer-2"]["lateInitialAnchorCoverage"], "INACTIVE")

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

    def terminal_body_fixture(self):
        # Authority's next form sample has already begun another transition;
        # its retained terminal nevertheless preserves the intermediate ACK.
        sample = self.peers["peer-0"]["Forms"][0]
        sample.update(BodyHp=27, ObservedDamageEvent=1,
                      BodyAckSource=struct.pack("<HBBHHHHHIBI", 1, 1, 1, 1, 1, 73, 27, 100, 1, 64, 5).hex())
        key = dict(AuthorityEpoch=111, MatchId=2, ShooterSlot=0, Generation=1, LifeId=2, ShotId=5)
        outcome = dict(ShotId=5, ClaimId=1, Result=1, VictimSlot=1, VictimGeneration=1, VictimLife=1,
                       DamageApplied=73, HealthAfter=27, HalfturretHealthAfter=100, DamageSequence=1, Flags=64)
        terminal = dict(Ordinal=5, ObservedFrame=4, ShooterSlot=0, Key=key, Outcome=outcome)
        self.server["TimedCaseJoins"] = dict(Schema=1, Terminals=[terminal], Budgets=dict(
            Terminals=dict(Observed=1, Count=1, Dropped=0)))
        receipt = dict(Stage="terminal-delivered", Key=copy.deepcopy(key), ClaimId=1, Victim=1,
                       VictimGeneration=1, VictimLife=1, DamageEventId=1, Value=73, Result=1, LocalFrame=3)
        self.peers["peer-0"]["ShotTimeline"] = dict(Schema=2, Enabled=True, Complete=True, Overflow=0, UnkeyedRows=0, Rows=[receipt])
        self.server["Forms"].append(row(8, 2, True, started=8, damage=1, body_hp=14, hp=13))
        return sample, terminal, receipt

    def test_independent_terminal_body_outcome_crosses_unsampled_transition(self):
        self.terminal_body_fixture()
        result = self.audit()
        self.assertEqual(result["status"], "PASS", result)
        proof = result["metrics"]["terminalAckBodyJoins"]
        self.assertEqual(len(proof), 1)
        self.assertEqual(proof[0]["proof"]["outcome"]["HealthAfter"], 27)
        self.assertEqual(proof[0]["proof"]["authorityFrame"], 4)

    def test_terminal_body_wrong_authority_outcome_never_supplies_basis(self):
        for field in ("HealthAfter", "HalfturretHealthAfter", "VictimLife", "VictimGeneration", "VictimSlot",
                      "ShotId", "ClaimId", "DamageSequence", "Result", "DamageApplied", "Flags"):
            with self.subTest(field=field):
                self.setUp(); _, terminal, _ = self.terminal_body_fixture()
                terminal["Outcome"][field] += 1
                self.assertEqual(self.audit()["status"], "INCOMPLETE")

    def test_terminal_body_wrong_receipt_full_key_or_outcome_never_supplies_basis(self):
        for kind, field in (("Key", f) for f in ("AuthorityEpoch", "MatchId", "ShooterSlot", "Generation", "LifeId", "ShotId")):
            with self.subTest(field=field):
                self.setUp(); _, _, receipt = self.terminal_body_fixture()
                receipt[kind][field] += 1
                self.assertEqual(self.audit()["status"], "INCOMPLETE")
        for field in ("ClaimId", "Victim", "VictimGeneration", "VictimLife", "DamageEventId", "Value", "Result", "LocalFrame"):
            with self.subTest(field=field):
                self.setUp(); _, _, receipt = self.terminal_body_fixture()
                receipt[field] += 1
                self.assertEqual(self.audit()["status"], "INCOMPLETE")

    def test_terminal_body_missing_ambiguous_unsupported_incomplete_evidence(self):
        for kind in ("absent-terminal", "absent-receipt", "duplicate-terminal", "conflicting-terminal", "duplicate-receipt",
                     "schema", "timeline-schema", "budget-drop", "budget-count", "timeline-incomplete", "timeline-overflow"):
            with self.subTest(kind=kind):
                self.setUp(); _, terminal, receipt = self.terminal_body_fixture()
                joins = self.server["TimedCaseJoins"]; timeline = self.peers["peer-0"]["ShotTimeline"]
                if kind == "absent-terminal": del self.server["TimedCaseJoins"]
                elif kind == "absent-receipt": del self.peers["peer-0"]["ShotTimeline"]
                elif kind in ("duplicate-terminal", "conflicting-terminal"):
                    other = copy.deepcopy(terminal)
                    if kind == "conflicting-terminal": other["Outcome"]["HealthAfter"] += 1
                    joins["Terminals"].append(other); joins["Budgets"]["Terminals"].update(Observed=2, Count=2)
                elif kind == "duplicate-receipt": timeline["Rows"].append(copy.deepcopy(receipt))
                elif kind == "schema": joins["Schema"] = 2
                elif kind == "timeline-schema": timeline["Schema"] = 1
                elif kind == "budget-drop": joins["Budgets"]["Terminals"]["Dropped"] = 1
                elif kind == "budget-count": joins["Budgets"]["Terminals"]["Count"] = 2
                elif kind == "timeline-incomplete": timeline["Complete"] = False
                elif kind == "timeline-overflow": timeline["Overflow"] = 1
                result = self.audit()
                self.assertEqual(result["status"], "INCOMPLETE", result)
                self.assertEqual(result["metrics"]["terminalAckBodyJoins"], [])

    def test_terminal_body_does_not_waive_form_presence_phase_or_prediction(self):
        for field, value in (("TurretActive", False), ("TurretHp", 99), ("Phase", "Alt"),
                             ("BodyPredictionDebit", 1), ("ObservedDamageEvent", 0), ("BodyAckSource", None)):
            with self.subTest(field=field):
                self.setUp(); sample, _, _ = self.terminal_body_fixture(); sample[field] = value
                result = self.audit()
                self.assertNotEqual(result["status"], "PASS", result)
                self.assertEqual(result["metrics"]["terminalAckBodyJoins"], [])

    def test_terminal_body_malformed_typed_proof_is_not_qualified(self):
        for kind in ("false-overflow", "true-event", "float-result", "true-claim", "float-value", "true-victim",
                     "false-shooter", "receipt-nondict", "terminal-nondict", "receipt-not-list", "outcome-nondict"):
            with self.subTest(kind=kind):
                self.setUp(); sample, terminal, receipt = self.terminal_body_fixture()
                timeline = self.peers["peer-0"]["ShotTimeline"]
                if kind == "false-overflow": timeline["Overflow"] = False
                elif kind == "true-event": receipt["DamageEventId"] = True
                elif kind == "float-result": receipt["Result"] = 1.0
                elif kind == "true-claim": receipt["ClaimId"] = True
                elif kind == "float-value": receipt["Value"] = 73.0
                elif kind == "true-victim": receipt["Victim"] = True
                elif kind == "false-shooter": terminal["ShooterSlot"] = False
                elif kind == "receipt-nondict": timeline["Rows"].append(None)
                elif kind == "terminal-nondict": self.server["TimedCaseJoins"]["Terminals"][0] = None
                elif kind == "receipt-not-list": timeline["Rows"] = {}
                elif kind == "outcome-nondict": terminal["Outcome"] = None
                result = self.audit()
                self.assertEqual(result["status"], "INCOMPLETE", result)
                self.assertEqual(result["metrics"]["terminalAckBodyJoins"], [])
        # Direct helper callers receive the same typed-data contract.
        for field, value in (("BodyPredictionDebit", False), ("BodyHp", 27.0), ("ObservedDamageEvent", True)):
            self.setUp(); sample, _, _ = self.terminal_body_fixture(); sample[field] = value
            identity, _, source = module.decode({**sample, "BodyHp": 27})
            self.assertIsNone(module.terminal_ack_body_join(self.server, self.peers["peer-0"], sample,
                                                         identity, source, 0))

    def test_terminal_body_requires_keyed_timeline_and_body_channel(self):
        for value in (1, False, 0.0, None):
            self.setUp(); self.terminal_body_fixture()
            timeline = self.peers["peer-0"]["ShotTimeline"]
            if value is None: del timeline["UnkeyedRows"]
            else: timeline["UnkeyedRows"] = value
            self.assertEqual(self.audit()["status"], "INCOMPLETE")
        self.setUp(); sample, terminal, _ = self.terminal_body_fixture()
        raw = bytearray.fromhex(sample["BodyAckSource"]); raw[18] |= 32
        sample["BodyAckSource"] = raw.hex(); terminal["Outcome"]["Flags"] |= 32
        self.assertEqual(self.audit()["status"], "INCOMPLETE")

    def test_terminal_body_source_association_conflicts_before_key_filter(self):
        for kind in ("unknown", "generation", "life", "outcome-shot"):
            self.setUp(); _, terminal, _ = self.terminal_body_fixture()
            other = copy.deepcopy(terminal)
            if kind == "unknown": other["Key"] = None
            elif kind == "generation": other["Key"]["Generation"] += 1
            elif kind == "life": other["Key"]["LifeId"] += 1
            else: other["Outcome"]["ShotId"] += 1
            joins = self.server["TimedCaseJoins"]
            joins["Terminals"].append(other); joins["Budgets"]["Terminals"].update(Count=2, Observed=2)
            self.assertEqual(self.audit()["status"], "INCOMPLETE")
        for field in ("ClaimId", "ShotId"):
            self.setUp(); _, terminal, _ = self.terminal_body_fixture()
            other = copy.deepcopy(terminal); other["Outcome"][field] += 1
            if field == "ShotId": other["Key"]["ShotId"] += 1
            joins = self.server["TimedCaseJoins"]
            joins["Terminals"].append(other); joins["Budgets"]["Terminals"].update(Count=2, Observed=2)
            self.assertEqual(self.audit()["status"], "PASS")

    def test_inactive_anchor_not_location_proof_but_active_anchor_required(self):
        sample = self.peers["peer-0"]["Forms"][-1]
        sample.update(X=100, Y=2, Z=5)
        self.assertEqual(self.audit()["status"], "PASS")
        sample = self.peers["peer-0"]["Forms"][0]; sample["X"] = 100
        self.assertEqual(self.audit()["status"], "FAIL")

    def lethal_floor_fixture(self):
        sample = self.peers["peer-0"]["Forms"][0]
        sample.update(BodyHp=1, TurretHp=0, TurretActive=False, ObservedDamageEvent=1,
            BodyAckSource=struct.pack("<HBBHHHHHIBI",1,1,1,1,1,100,0,0,1,66,5).hex())
        self.server["Forms"].append(row(8, 3, False, hp=0, damage=1, body_hp=0))
        return sample

    def test_exact_lethal_ack_floor_needs_positive_older_source_and_authority_death(self):
        sample=self.lethal_floor_fixture()
        result=self.audit();self.assertEqual(result["status"],"PASS",result)
        self.assertEqual(len(result["metrics"]["lethalAckFloorJoins"]),1)
        del sample["BodyAckSource"]
        self.assertEqual(self.audit()["status"],"INCOMPLETE")

    def test_lethal_ack_floor_wrong_victim_result_health_flag_sequence_fail(self):
        for offset,value in ((2,4),(3,2),(4,2),(6,2),(10,1),(14,2),(18,64),(19,0)):
            with self.subTest(offset=offset):
                self.setUp();sample=self.lethal_floor_fixture();raw=bytearray.fromhex(sample["BodyAckSource"])
                raw[offset]=value;sample["BodyAckSource"]=raw.hex()
                self.assertEqual(self.audit()["status"],"FAIL")
        self.setUp();sample=self.lethal_floor_fixture();sample["BodyHp"]=2
        self.assertEqual(self.audit()["status"],"FAIL")
        self.setUp();sample=self.lethal_floor_fixture();sample["BodyAckSource"]="00"
        self.assertEqual(self.audit()["status"],"FAIL")

    def test_lethal_floor_cannot_match_different_authority_life_transition_or_outcome(self):
        self.lethal_floor_fixture();self.server["Forms"][-1]["Life"]=2
        self.assertEqual(self.audit()["status"],"INCOMPLETE")
        self.setUp();self.lethal_floor_fixture()
        raw=bytearray.fromhex(self.server["Forms"][-1]["Source"]);raw[22]=1
        self.server["Forms"][-1].update(Source=raw.hex(),TurretHp=1)
        self.assertEqual(self.audit()["status"],"FAIL")
        self.setUp();self.lethal_floor_fixture()
        self.server["Forms"][-1].update(Source=row(8,3,False,damage=1,body_hp=1)["Source"],BodyHp=1)
        result=self.audit();self.assertEqual(result["status"],"PASS")
        self.assertEqual(result["metrics"]["lethalAckFloorJoins"],[])

    def test_observed_owner_cycle_can_lead_source_but_must_reach_actual_biped(self):
        source=row(7,4)
        self.server["Forms"][-1]=source
        self.server["Forms"].append(row(8,1))
        for name in ("peer-0","peer-2"):
            self.peers[name]["Forms"][-1]={**row(8,1),"Stage":"observer"}
        owner=self.peers["peer-1"]["Forms"]
        owner[-1]={**source,"Stage":"owner","Phase":"Biped"}
        initial={**row(2,1),"Stage":"owner"}
        owner.insert(0,initial);self.server["Forms"].append({**initial,"Stage":"authority"})
        self.assertEqual(self.audit()["status"],"PASS")
        owner[-1]["Phase"]="Unmorphing"
        self.assertEqual(self.audit()["status"],"INCOMPLETE")

    def test_peer_inventory_and_drain_required(self):
        del self.peers["peer-2"]
        self.assertEqual(self.audit()["status"], "INCOMPLETE")
        self.setUp(); self.summary["coverage"] = {}
        self.assertEqual(self.audit()["status"], "INCOMPLETE")


if __name__ == "__main__": unittest.main()
