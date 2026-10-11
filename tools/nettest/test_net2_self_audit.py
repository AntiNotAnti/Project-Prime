import copy
import unittest
from net2_self_audit import audit


class SelfAuditTests(unittest.TestCase):
    def setUp(self):
        self.shot = dict(AuthorityEpoch=1, MatchId=2, ShooterSlot=0, Generation=1, LifeId=1, ShotId=10)
        self.key = dict(Shot=self.shot, Victim=0, Generation=1, Life=1, Component=1, Kind=2)
        self.ack = dict(ShotId=10, ClaimId=0, Result=0, VictimSlot=0, VictimGeneration=1,
                        VictimLife=1, DamageApplied=10, HealthAfter=89, DamageSequence=1, Flags=64)
        paid = dict(Key=self.key, AuthorityFrame=10, Outcome=self.ack, Impulse=[0, 2, 0])
        ledger = dict(Enabled=True, Observed=1, Verified=0, Refused=0, CapacityRefused=0,
                      EvidenceOverflow=0, Paid=[], Receipts=[], Feedback=[])
        self.server = dict(SelfSplash=dict(ledger, Paid=[paid]), States=[])
        def peer(slot):
            return dict(Windows=[dict(LocalSlot=slot)], SelfSplash=copy.deepcopy(ledger),
                        Drain=dict(ClaimsPending=0),
                        CombatReconciliation=dict(Predicted=0, Outstanding=0, SelfPredicted=0,
                            SelfConfirmed=0, SelfDeathsPredicted=0, SelfOutstanding=0, SelfPendingClaims=0))
        self.peers = {"peer-0": peer(0), "peer-1": peer(1)}
        self.peers["peer-0"]["CombatReconciliation"].update(SelfPredicted=1, SelfConfirmed=1)
        self.peers["peer-0"]["SelfSplash"]["Receipts"] = [dict(paid, Outcome=dict(self.ack, ClaimId=1, Result=1))]
        self.peers["peer-0"]["SelfSplash"]["Feedback"] = [dict(AuthorityEpoch=1, MatchId=2, VictimSlot=0, Generation=1, Life=1,
            DamageSequence=1, Predicted=[0, 1, 0], Authority=[0, 2, 0], Before=[0, 4, 0], After=[0, 5, 0])]
        self.summary = dict(status="PASS", processes=[dict(name=n) for n in ("server", *self.peers)],
            coverage=dict(boundedHeadlessDrain="PASS"),
            parity={n: dict(healthMismatches=0, healthSamplesCompared=20) for n in self.peers})
        self.logs = {"peer-0": [dict(Key=tuple(self.shot.values()), ClaimId=1, Result="already resolved", Weapon="Missile")], "peer-1": []}

    def result(self, scenario="self-moving"):
        return audit(self.server, self.peers, self.summary, scenario, self.logs)

    def test_complete_bounded_self_audit(self):
        result = self.result()
        self.assertEqual(result["status"], "PASS", result)
        self.assertEqual(result["coverage"]["allSelfSplashWeapons"], "NOT_RUN")
        self.assertEqual(result["coverage"]["burnSourceParity"], "NOT_RUN")

    def test_duplicate_component_payment_fails(self):
        self.server["SelfSplash"]["Paid"] *= 2
        self.assertEqual(self.result()["status"], "FAIL")

    def test_other_victim_is_not_self_payment(self):
        self.key["Victim"] = 1
        self.assertEqual(self.result()["status"], "FAIL")

    def test_different_lifecycle_is_not_joined(self):
        for field in ("AuthorityEpoch", "MatchId", "Generation", "LifeId", "ShotId"):
            saved = copy.deepcopy(self.peers["peer-0"]["SelfSplash"]["Receipts"])
            row = copy.deepcopy(saved[0])
            row["Key"]["Shot"][field] += 1
            self.peers["peer-0"]["SelfSplash"]["Receipts"] = [row]
            self.assertEqual(self.result()["status"], "FAIL")
            self.peers["peer-0"]["SelfSplash"]["Receipts"] = saved

    def test_false_accepted_ack_without_payment_fails(self):
        self.server["SelfSplash"]["Paid"] = []
        self.assertTrue(any("no exact authority payment" in x for x in self.result()["failures"]))

    def test_refused_exact_component_cannot_be_paid(self):
        self.peers["peer-0"]["SelfSplash"]["Receipts"][0]["Outcome"]["Result"] = 6
        self.logs["peer-0"][0]["Result"] = "wrong lifecycle"
        self.assertTrue(any("refused exact self ACK" in x for x in self.result()["failures"]))

    def test_distinct_component_refusal_does_not_contradict_payment(self):
        row = copy.deepcopy(self.peers["peer-0"]["SelfSplash"]["Receipts"][0])
        row["Key"]["Component"] = 2
        row["Outcome"].update(ClaimId=2, Result=6, DamageApplied=0)
        self.peers["peer-0"]["SelfSplash"]["Receipts"].append(row)
        self.logs["peer-0"].append(dict(self.logs["peer-0"][0], ClaimId=2, Result="wrong lifecycle"))
        self.assertEqual(self.result()["status"], "PASS")

    def test_missing_component_receipt_is_incomplete(self):
        self.peers["peer-0"]["SelfSplash"]["Receipts"] = []
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_receipt_log_result_disagreement_fails(self):
        self.logs["peer-0"][0]["Result"] = "wrong lifecycle"
        self.assertEqual(self.result()["status"], "FAIL")

    def test_missing_log_is_incomplete(self):
        del self.logs["peer-0"]
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_truncated_log_is_incomplete(self):
        self.logs["peer-0"] = []
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_wrong_weapon_receipt_fails(self):
        self.logs["peer-0"][0]["Weapon"] = "Battlehammer"
        self.assertEqual(self.result()["status"], "FAIL")

    def test_nonfinite_impulse_evidence_fails(self):
        self.server["SelfSplash"]["Paid"][0]["Impulse"] = [0, float("nan"), 0]
        self.assertEqual(self.result()["status"], "FAIL")

    def test_paid_outcome_identity_damage_sequence_must_match(self):
        self.peers["peer-0"]["SelfSplash"]["Receipts"][0]["Outcome"]["DamageSequence"] = 2
        self.assertEqual(self.result()["status"], "FAIL")

    def test_overflow_and_capacity_fail(self):
        for field in ("EvidenceOverflow", "CapacityRefused"):
            self.server["SelfSplash"][field] = 1
            self.assertEqual(self.result()["status"], "FAIL")
            self.server["SelfSplash"][field] = 0

    def test_pending_self_prediction_or_claim_fails(self):
        for field in ("SelfOutstanding", "SelfPendingClaims"):
            self.peers["peer-0"]["CombatReconciliation"][field] = 1
            self.assertEqual(self.result()["status"], "FAIL")
            self.peers["peer-0"]["CombatReconciliation"][field] = 0

    def test_all_expected_peer_evidence_required(self):
        del self.peers["peer-1"]
        self.assertEqual(self.result()["status"], "FAIL")

    def test_base_failure_is_never_waived(self):
        self.summary["status"] = "FAIL"
        self.assertEqual(self.result()["status"], "FAIL")

    def test_missing_health_scoreboard_parity_is_incomplete(self):
        self.summary["parity"]["peer-1"] = {}
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_impulse_added_twice_fails(self):
        self.peers["peer-0"]["SelfSplash"]["Feedback"][0]["After"] = [0, 6, 0]
        self.assertEqual(self.result()["status"], "FAIL")

    def test_impulse_identity_or_value_mismatch_fails(self):
        self.peers["peer-0"]["SelfSplash"]["Feedback"][0]["Authority"] = [0, 3, 0]
        self.assertEqual(self.result()["status"], "FAIL")

    def test_duplicate_feedback_fails(self):
        self.peers["peer-0"]["SelfSplash"]["Feedback"] *= 2
        self.assertEqual(self.result()["status"], "FAIL")

    def test_no_applied_feedback_is_incomplete(self):
        self.peers["peer-0"]["SelfSplash"]["Feedback"] = []
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_feedback_different_epoch_cannot_join(self):
        self.peers["peer-0"]["SelfSplash"]["Feedback"][0]["AuthorityEpoch"] = 2
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_direct_and_splash_components_are_distinct_payments(self):
        row = copy.deepcopy(self.server["SelfSplash"]["Paid"][0])
        row["Key"]["Kind"] = 1
        row["Outcome"]["DamageSequence"] = 2
        self.server["SelfSplash"]["Paid"].append(row)
        self.assertEqual(self.result()["status"], "PASS")

    def test_self_wall_requires_actual_paid_death_and_respawn(self):
        self.assertEqual(self.result("self-wall")["status"], "INCOMPLETE")
        row = copy.deepcopy(self.server["SelfSplash"]["Paid"][0])
        self.ack.update(HealthAfter=0, Flags=66)
        row["Key"]["Shot"].update(LifeId=2, ShotId=20)
        row["Key"]["Life"] = 2
        row["Outcome"].update(ShotId=20, VictimLife=2, DamageSequence=2)
        self.server["SelfSplash"]["Paid"].append(row)
        self.server["States"] = [dict(Epoch=1, Match=2, Slot=0, Generation=1, Life=l, Hp=99, Deaths=l-1) for l in (1, 2)]
        self.peers["peer-0"]["SelfSplash"]["Receipts"][0]["Outcome"].update(HealthAfter=0, Flags=66)
        self.assertEqual(self.result("self-wall")["status"], "PASS")


if __name__ == "__main__":
    unittest.main()
