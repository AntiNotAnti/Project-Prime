import copy
import json
from pathlib import Path
import unittest
from net2_continuous_audit import audit, read_verdict_log


class ContinuousAuditTests(unittest.TestCase):
    def setUp(self):
        owner = dict(AuthorityEpoch=1, MatchId=2, ShooterSlot=0, Generation=1, LifeId=1, ShotId=10)
        row = dict(Key=dict(OwnerTick=owner, Victim=dict(EncodedSlot=0x82, Generation=1, LifeId=1), Flags=0),
                   Outcome=dict(VictimSlot=1, VictimGeneration=1, VictimLife=1, DamageApplied=2, ShotId=1),
                   DamageTurret=1, HealBody=2, HealTurret=1)
        self.server = dict(Shots=[], ContinuousPayments=dict(Rows=[row], EvidenceCount=1, EvidenceCapacity=8192,
            LifetimePayments=1, LifetimeDamage=2, LifetimeTurretDamage=1, LifetimeHealBody=2, LifetimeHealTurret=1))
        peer = dict(CombatReconciliation=dict(DrainPredicted=3, DrainConfirmed=3, DrainRejected=0,
            DrainExpired=0, DrainCurrentPoints=0, Outstanding=0),
            Shots=[dict(Stage="committed", Weapon=7, Key=dict(owner, ShotId=1)),
                   dict(Stage="committed", Weapon=4, Key=dict(owner, ShotId=2))],
            States=[dict(Epoch=1, Match=2, Slot=0, Generation=1, Life=life, Hp=50, Deaths=life - 1) for life in (1, 2)])
        self.peers = {"peer-0": peer}
        self.summary = dict(status="PASS", processes=[dict(name="server"), dict(name="peer-0")],
            coverage=dict(boundedHeadlessDrain="PASS"),
            parity={"peer-0": dict(healthMismatches=0, healthSamplesCompared=20)})

    def result(self):
        return audit(self.server, self.peers, self.summary)

    def test_complete_bounded_audit_never_claims_all_clients_or_soak(self):
        result = self.result()
        self.assertEqual(result["status"], "PASS")
        self.assertEqual(result["coverage"]["extendedContinuousSoak"], "NOT_RUN")
        self.assertEqual(result["coverage"]["exactTickPaymentsAcrossAllClients"], "NOT_RUN")

    def test_refused_exact_emitted_ack_cannot_be_paid(self):
        self.peers["peer-0"]["CombatReconciliation"].update(CombatAcks=1, Predicted=1)
        row = dict(Key=(1, 2, 0, 1, 1, 1), ClaimId=9, Weapon="ShockCoil",
                   Result="launch frame follows hit frame")
        result = audit(self.server, self.peers, self.summary, verdicts={"peer-0": [row]})
        self.assertTrue(any("refused CombatAck" in x for x in result["failures"]))
        for index in range(6):
            other = dict(row, Key=tuple(v + (1 if i == index else 0) for i, v in enumerate(row["Key"])))
            self.assertEqual(audit(self.server, self.peers, self.summary,
                                  verdicts={"peer-0": [other]})["status"], "PASS")

    def test_missing_or_truncated_exact_ack_evidence_is_incomplete(self):
        self.peers["peer-0"]["CombatReconciliation"].update(CombatAcks=1, Predicted=1)
        self.assertEqual(self.result()["status"], "INCOMPLETE")
        self.assertEqual(audit(self.server, self.peers, self.summary,
                              verdicts={"peer-0": []})["status"], "INCOMPLETE")

    def test_late_receipt_does_not_require_live_debit_ack_count(self):
        self.peers["peer-0"]["CombatReconciliation"].update(CombatAcks=0, Predicted=1)
        receipt = dict(Key=(1, 2, 0, 1, 1, 1), ClaimId=1, Weapon="ShockCoil", Result="already resolved")
        self.assertEqual(audit(self.server, self.peers, self.summary,
                              verdicts={"peer-0": [receipt]})["status"], "PASS")
        self.assertEqual(audit(self.server, self.peers, self.summary,
                              verdicts={"peer-0": []})["status"], "INCOMPLETE")

    def test_saved_two_lan_false_pass_is_detected(self):
        campaign = Path(__file__).resolve().parents[2] / "artifacts/net2-n6/mac-2-lan-mixed-attempt05"
        if not campaign.exists():
            self.skipTest("local immutable campaign unavailable")
        read = lambda name: json.loads((campaign / name).read_text())
        peers = {p.stem: read(p.name) for p in campaign.glob("peer-*.json")
                 if not p.name.endswith(".drain.json")}
        verdicts = {name: read_verdict_log(campaign / name /
                    ("netlog-" + name.replace("-", "_") + ".txt")) for name in peers}
        result = audit(read("server.json"), peers, read("summary.json"), verdicts=verdicts)
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(len(result["metrics"]["exactCombatAckVerdicts"]["peer-0"]["contradictoryPaidKeys"]), 12)

    def test_same_owner_tick_cannot_redirect_or_pay_twice(self):
        ledger = self.server["ContinuousPayments"]
        row = copy.deepcopy(ledger["Rows"][0]); row["Key"]["Victim"]["LifeId"] = 2
        row["Outcome"]["VictimLife"] = 2
        ledger["Rows"].append(row); ledger["EvidenceCount"] = 2
        self.assertTrue(any("more than once" in x for x in self.result()["failures"]))

    def test_corrupt_totals_zero_damage_and_overheal_fail(self):
        for field, value in [("DamageTurret", 0), ("HealBody", 10)]:
            with self.subTest(field=field):
                old = self.server["ContinuousPayments"]["Rows"][0][field]
                self.server["ContinuousPayments"]["Rows"][0][field] = value
                self.assertEqual(self.result()["status"], "FAIL")
                self.server["ContinuousPayments"]["Rows"][0][field] = old
        row = self.server["ContinuousPayments"]["Rows"][0]
        row["Outcome"]["DamageApplied"] = row["DamageTurret"] = 0
        self.assertTrue(any("zero-damage" in x for x in self.result()["failures"]))

    def test_truncated_evidence_is_not_complete(self):
        self.server["ContinuousPayments"]["EvidenceCapacity"] = 1
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_peer_credit_balance_and_outstanding_fail(self):
        receipt = self.peers["peer-0"]["CombatReconciliation"]
        receipt["DrainConfirmed"] = 2
        self.assertEqual(self.result()["status"], "FAIL")
        receipt["DrainConfirmed"] = 3; receipt["Outstanding"] = 1
        self.assertEqual(self.result()["status"], "FAIL")

    def test_missing_exercise_parity_or_barrier_is_incomplete(self):
        self.peers["peer-0"]["Shots"] = []
        self.peers["peer-0"]["States"] = []
        self.summary["coverage"]["boundedHeadlessDrain"] = "NOT_RUN"
        self.summary["parity"] = {}
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_missing_peer_file_and_wrong_outcome_life_fail(self):
        self.assertEqual(audit(self.server, {}, self.summary)["status"], "FAIL")
        self.server["ContinuousPayments"]["Rows"][0]["Outcome"]["VictimLife"] = 2
        self.assertEqual(self.result()["status"], "FAIL")

    def test_life_change_without_death_is_not_respawn_coverage(self):
        for state in self.peers["peer-0"]["States"]:
            state["Deaths"] = 0
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_failed_campaign_or_health_mismatch_stays_failed(self):
        self.summary["status"] = "FAIL"
        self.assertEqual(self.result()["status"], "FAIL")
        self.summary["status"] = "PASS"
        self.summary["parity"]["peer-0"]["healthMismatches"] = 1
        self.assertEqual(self.result()["status"], "FAIL")

    def test_exact_terminal_rejection_is_audited_but_cannot_hide_paid_tick(self):
        owner = self.server["ContinuousPayments"]["Rows"][0]["Key"]["OwnerTick"]
        shot = dict(Key=dict(owner, ShotId=2), Stage="accepted", SourceFrame=10,
                    ContinuousPhase=10, Kind=3, Weapon=7, Charge=0)
        rejected = dict(shot, Stage="rejected", Reason="continuous owner dead",
                        OwnerHealth=0, CurrentOwnerLife=1)
        self.server["Shots"] = [shot, rejected]
        self.assertEqual(self.result()["status"], "FAIL")
        shot["ContinuousPhase"] = shot["SourceFrame"] = 12
        rejected["ContinuousPhase"] = rejected["SourceFrame"] = 12
        result = self.result()
        self.assertEqual(result["status"], "PASS")
        self.assertEqual(result["metrics"]["terminalContinuousRejections"]["server"]["count"], 1)


if __name__ == "__main__":
    unittest.main()
