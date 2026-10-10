"""Regression checks for false-green campaigns. Run with python -m unittest discover."""
import copy
import json
from pathlib import Path
import tempfile
import unittest
from net2_evidence import adjudicate


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.out = Path(self.temp.name)
        self.processes = [{"name": n, "exit": 0, "timedOut": False, "expectsShots": n != "server"}
                          for n in ("server", "shooter")]
        key = dict(AuthorityEpoch=111, MatchId=2, ShooterSlot=0, Generation=3, LifeId=4, ShotId=5)
        state = dict(Epoch=111, Match=2, Frame=180, Slot=0, Generation=3, Life=4,
                     Hp=99, TurretHp=0, Kills=0, Deaths=0, DamageEvent=2)
        shot = dict(Stage="committed", Key=key, Weapon=2, Charge=0, Kind=0)
        fact = dict(Type=101, Epoch=111, Match=2, Id=1, Tick=60, Baseline=False,
                    Award=False, AwardFrontier=0, Payload="CAFE")
        self.report = dict(Schema=1, Protocol=41, Frames=180, Windows=[{}], States=[state],
            Transport=dict(QueueDrops=0, SocketErrors=0), AliveFrames=[180] + [0] * 7,
            Overflow=0, ConnectionLost=False, SemanticOverrun=False, SemanticDisconnects=0, LastError=None,
            OfferedCarriers=180, AcceptedCarriers=180, AllocatedBytes=10, HeapBytes=10, WorkingSet=10,
            ServerSegments=[dict(StepFailures=0)], Shots=[shot], Facts=[fact])
        self.server = copy.deepcopy(self.report)
        self.server["Shots"].append(dict(shot, Stage="accepted"))
        self.client = copy.deepcopy(self.report)
        self.client["Shots"].append(dict(shot, Stage="offered"))
        self.client["Facts"].insert(0, dict(fact, Id=0, Tick=0, Baseline=True, Payload="BEEF"))
        self.write()

    def write(self):
        for name, data in (("server", self.server), ("shooter", self.client)):
            (self.out / f"{name}.json").write_text(json.dumps(data))
            (self.out / f"{name}.log").write_text("RESULT: PASS\n")

    def result(self):
        return adjudicate(self.out, self.processes)

    def test_complete_matching_run(self):
        self.assertEqual(self.result()["status"], "PASS")

    def test_success_exit_cannot_hide_timeout(self):
        (self.out / "shooter.log").write_text("RESULT: PASS\npeer timed out\n")
        self.assertEqual(self.result()["status"], "FAIL")

    def test_nested_diagnostic_failure_is_not_ignored(self):
        directory = self.out / "shooter" / "logs"
        directory.mkdir(parents=True)
        (directory / "netlog.txt").write_text("semantic history overrun")
        self.assertEqual(self.result()["status"], "FAIL")

    def test_killed_participant(self):
        self.processes[-1]["timedOut"] = True
        self.assertEqual(self.result()["status"], "FAIL")

    def test_missing_report(self):
        (self.out / "shooter.json").unlink()
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_partial_json(self):
        (self.out / "shooter.json").write_text('{"Schema":')
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_no_shots_is_incomplete(self):
        self.client["Shots"] = []
        self.write()
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_no_alive_denominator(self):
        self.client["AliveFrames"] = [0] * 8
        self.write()
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_same_shot_id_different_life_is_not_paired(self):
        self.server["Shots"][0]["Key"]["LifeId"] = 9
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_missing_accepted_carrier(self):
        self.server["Shots"] = [self.server["Shots"][0]]
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_duplicate_commit(self):
        self.client["Shots"].append(self.client["Shots"][0])
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_wrong_weapon(self):
        self.server["Shots"][0]["Weapon"] = 7
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_health_mismatch_at_same_authority_frame(self):
        self.client["States"][0]["Hp"] = 50
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_health_different_life_is_not_compared(self):
        self.client["States"][0]["Life"] = 99
        self.write()
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_one_matching_fact_does_not_hide_semantic_gap(self):
        self.server["Facts"].append(dict(self.server["Facts"][0], Id=2, Payload="DEAD"))
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_semantic_same_id_different_payload(self):
        self.client["Facts"][1]["Payload"] = "BAD0"
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_no_semantic_baseline_is_incomplete(self):
        self.client["Facts"] = self.client["Facts"][1:]
        self.write()
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_overflow_fails_closed(self):
        self.client["Overflow"] = 1
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_transient_connection_loss_survives_recovery(self):
        self.client["ConnectionLost"] = True
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_wrong_protocol(self):
        self.client["Protocol"] = 42
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")


if __name__ == "__main__":
    unittest.main()
