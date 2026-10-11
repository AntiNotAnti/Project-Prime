"""Regression checks for false-green campaigns. Run with python -m unittest discover."""
import copy
import json
import struct
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
        shot = dict(Stage="committed", Key=key, Weapon=2, Charge=0, Kind=0, ObservedFrame=90)
        fact = dict(Type=101, Epoch=111, Match=2, Id=1, Tick=60, Baseline=False,
                    Award=False, AwardFrontier=0, Payload="CAFE")
        self.report = dict(Schema=1, Protocol=41, Frames=180, Windows=[{}], States=[dict(state, Frame=6), state],
            Transport=dict(QueueDrops=0, SocketErrors=0), AliveFrames=[180] + [0] * 7,
            Overflow=0, ConnectionLost=False, SemanticOverrun=False, SemanticDisconnects=0, LastError=None,
            OfferedCarriers=180, AcceptedCarriers=180, AllocatedBytes=10, HeapBytes=10, WorkingSet=10,
            ServerSegments=[dict(StepFailures=0)], Shots=[shot], Facts=[fact])
        self.server = copy.deepcopy(self.report)
        self.server["Shots"].append(dict(shot, Stage="accepted"))
        self.client = copy.deepcopy(self.report)
        self.client["Shots"].append(dict(shot, Stage="offered"))
        self.client["Facts"].insert(0, dict(fact, Id=0, Tick=0, Baseline=True, Payload="BEEF"))
        self.manifest = dict(settings=dict(players=1, seconds=3), assemblySha256="same", assemblyAfterSha256="same")
        (self.out / "manifest.json").write_text(json.dumps(self.manifest))
        self.write()

    def write(self):
        for name, data in (("server", self.server), ("shooter", self.client)):
            (self.out / f"{name}.json").write_text(json.dumps(data))
            (self.out / f"{name}.log").write_text("RESULT: PASS\n")

    def result(self):
        return adjudicate(self.out, self.processes)

    def test_complete_matching_run(self):
        self.assertEqual(self.result()["status"], "PASS")

    def test_missing_identity_manifest_is_incomplete(self):
        (self.out / "manifest.json").unlink()
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_soak_coverage_survives_readjudication(self):
        self.manifest["settings"].update(players=8, seconds=1200, soak=True)
        (self.out / "manifest.json").write_text(json.dumps(self.manifest))
        self.processes[-1]["requestedSeconds"] = 1200
        result = self.result()
        self.assertEqual(result["coverage"]["sustained20Minutes"], "INCOMPLETE")
        self.assertIn("soak duration, load, bot, spectator or map-rotation coverage missing", result["incomplete"])

    def test_short_soak_cannot_satisfy_twenty_minutes(self):
        self.manifest["settings"].update(players=8, seconds=3, soak=True)
        (self.out / "manifest.json").write_text(json.dumps(self.manifest))
        self.processes[-1]["requestedSeconds"] = 3
        self.client["Windows"] = [dict(Seconds=3)]
        self.write()
        self.assertEqual(self.result()["coverage"]["sustained20Minutes"], "INCOMPLETE")

    def rotation_fixture(self):
        self.manifest["settings"].update(seconds=6, rooms="A,B")
        (self.out / "manifest.json").write_text(json.dumps(self.manifest))
        self.processes[-1]["requestedSeconds"] = 6
        for report in (self.server, self.client):
            report["Windows"] = [dict(Epoch=111, Match=match, Frame=frame, Room=room)
                                  for match, room in ((2, "A"), (3, "B")) for frame in (6, 180)]
            report["States"] += [dict(s, Match=3) for s in report["States"]]
            report["ServerSegments"] = [dict(AuthorityEpoch=111, CurrentMatchId=match,
                                               Frames=180, StepFailures=0) for match in (2, 3)]

    def test_short_rotation_proves_actual_matches_without_soak(self):
        self.rotation_fixture()
        self.write()
        result = self.result()
        self.assertEqual(result["status"], "PASS")
        self.assertEqual(result["coverage"]["mapRotation"], "PASS")
        self.assertEqual(result["coverage"]["sustained20Minutes"], "NOT_RUN")

    def test_two_room_labels_without_new_match_are_incomplete(self):
        self.server["Windows"] = [dict(Epoch=111, Match=2, Frame=6, Room="A"),
                                  dict(Epoch=111, Match=2, Frame=180, Room="B")]
        self.write()
        result = self.result()
        self.assertEqual(result["status"], "INCOMPLETE")
        self.assertEqual(result["coverage"]["mapRotation"], "INCOMPLETE")

    def test_repeated_room_with_distinct_matches_proves_rotation(self):
        self.rotation_fixture()
        for report in (self.server, self.client):
            for window in report["Windows"]:
                window["Room"] = "A"
        self.write()
        self.assertEqual(self.result()["coverage"]["mapRotation"], "PASS")

    def test_reused_match_number_in_different_epoch_is_distinct(self):
        self.rotation_fixture()
        for report in (self.server, self.client):
            for window in report["Windows"]:
                if window["Match"] == 3:
                    window.update(Epoch=222, Match=2)
            for state in report["States"]:
                if state["Match"] == 3:
                    state.update(Epoch=222, Match=2)
            for segment in report["ServerSegments"]:
                if segment["CurrentMatchId"] == 3:
                    segment.update(AuthorityEpoch=222, CurrentMatchId=2)
        self.write()
        self.assertEqual(self.result()["coverage"]["mapRotation"], "PASS")

    def test_frozen_new_match_cannot_prove_rotation(self):
        self.rotation_fixture()
        self.client["Windows"][-1]["Frame"] = 6
        self.write()
        self.assertEqual(self.result()["coverage"]["mapRotation"], "INCOMPLETE")

    def test_old_match_health_cannot_hide_missing_new_match_health(self):
        self.rotation_fixture()
        self.client["States"] = [s for s in self.client["States"] if s["Match"] == 2]
        self.write()
        result = self.result()
        self.assertEqual(result["status"], "INCOMPLETE")
        self.assertEqual(result["coverage"]["mapRotation"], "INCOMPLETE")

    def test_one_new_match_health_frame_cannot_prove_rotation(self):
        self.rotation_fixture()
        self.client["States"] = [s for s in self.client["States"] if s["Match"] == 2 or s["Frame"] == 180]
        self.write()
        self.assertEqual(self.result()["coverage"]["mapRotation"], "INCOMPLETE")

    def test_health_different_epoch_cannot_prove_new_match(self):
        self.rotation_fixture()
        for state in self.client["States"]:
            if state["Match"] == 3:
                state["Epoch"] = 222
        self.write()
        self.assertEqual(self.result()["coverage"]["mapRotation"], "INCOMPLETE")

    def test_full_duration_peer_stuck_on_old_match_is_incomplete(self):
        self.rotation_fixture()
        self.client["Windows"] = [w for w in self.client["Windows"] if w["Match"] == 2]
        self.write()
        self.assertEqual(self.result()["coverage"]["mapRotation"], "INCOMPLETE")

    def test_late_join_needs_only_match_it_observed(self):
        self.rotation_fixture()
        late = copy.deepcopy(self.client)
        late["Windows"] = [w for w in late["Windows"] if w["Match"] == 3]
        late["States"] = [s for s in late["States"] if s["Match"] == 3]
        late["Shots"] = []
        late["Facts"] = [dict(f, Match=3) for f in late["Facts"]]
        # Exercise the independent new-match shot/semantic denominators as well.
        for report in (self.server, self.client, late):
            report["Shots"] += [dict(s, Key=dict(s["Key"], MatchId=3)) for s in self.client["Shots"][:1]]
        self.client["Shots"].append(dict(self.client["Shots"][-1], Stage="offered"))
        self.server["Shots"].append(dict(self.server["Shots"][-1], Stage="accepted"))
        self.server["Facts"] += [dict(self.server["Facts"][0], Match=3)]
        self.client["Facts"] += [dict(f, Match=3) for f in self.client["Facts"]]
        self.manifest["settings"]["players"] = 2
        (self.out / "manifest.json").write_text(json.dumps(self.manifest))
        self.processes.append(dict(name="late", exit=0, timedOut=False, expectsShots=False, requestedSeconds=3))
        self.write()
        (self.out / "late.json").write_text(json.dumps(late))
        (self.out / "late.log").write_text("RESULT: PASS")
        self.assertEqual(self.result()["status"], "PASS")

    def test_unadvanced_server_segment_cannot_prove_rotation(self):
        self.rotation_fixture()
        self.server["ServerSegments"][-1]["Frames"] = 1
        self.write()
        self.assertEqual(self.result()["coverage"]["mapRotation"], "INCOMPLETE")

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

    def test_successful_but_unexpected_server_exit(self):
        self.processes[0]["unexpectedExit"] = True
        self.assertEqual(self.result()["status"], "FAIL")

    def test_missing_expected_participants(self):
        (self.out / "manifest.json").write_text(json.dumps(dict(settings=dict(players=8),
            assemblySha256="same", assemblyAfterSha256="same")))
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_runner_failure_survives_readjudication(self):
        (self.out / "manifest.json").write_text(json.dumps(dict(settings=dict(players=1),
            assemblySha256="same", assemblyAfterSha256="same", runnerError="startup failed")))
        self.assertEqual(self.result()["status"], "FAIL")

    def test_silent_reconnect_is_a_failure(self):
        (self.out / "shooter.log").write_text("[net] no word from the server; re-announcing (#1)\nRESULT: PASS")
        self.assertEqual(self.result()["status"], "FAIL")

    def test_invalid_resurrection_is_a_failure(self):
        (self.out / "shooter.log").write_text("life: invalid resurrection=1")
        self.assertEqual(self.result()["status"], "FAIL")

    def test_malformed_offered_only_row(self):
        del self.client["Shots"][1]["Weapon"]
        self.write()
        self.assertEqual(self.result()["status"], "INCOMPLETE")

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
        for row in self.client["States"]: row["Life"] = 99
        self.write()
        self.assertEqual(self.result()["status"], "INCOMPLETE")

    def test_delivery_to_server_does_not_prove_observer_commit(self):
        observer = copy.deepcopy(self.client)
        observer["Shots"] = []
        (self.out / "observer.json").write_text(json.dumps(observer))
        (self.out / "observer.log").write_text("RESULT: PASS")
        self.processes.append(dict(name="observer", exit=0, timedOut=False, expectsShots=False))
        self.assertEqual(self.result()["status"], "FAIL")

    def test_phase_skip_explains_but_does_not_waive_authority_gap(self):
        self.server["Shots"][0]["Stage"] = "phase-skipped"
        self.write()
        result = self.result()
        self.assertEqual(result["status"], "FAIL")
        context = result["parity"]["shooter"]["authorityGapContext"]
        self.assertEqual(context["recordedPhaseSkips"], 1)
        self.assertEqual(context["arrivedOrLocallyOffered"], 1)

    def test_arrived_view_shot_at_death_still_fails(self):
        self.client["Shots"][0]["Stage"] = "accepted"
        self.server["States"].append(dict(self.server["States"][0], Frame=96, Hp=0))
        self.write()
        result = self.result()
        self.assertEqual(result["status"], "FAIL")
        context = result["parity"]["shooter"]["viewGapContext"]
        self.assertEqual(context["arrivedOrLocallyOffered"], 1)
        self.assertEqual(context["nearAuthorityDeathWithin12Frames"], 1)

    def test_death_context_keeps_full_actor_identity(self):
        self.client["Shots"][0]["Stage"] = "accepted"
        self.server["States"].append(dict(self.server["States"][0], Frame=96, Hp=0, Life=7))
        self.write()
        result = self.result()
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["parity"]["shooter"]["viewGapContext"]["nearAuthorityDeathWithin12Frames"], 0)

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

    def test_protocol_42_requires_explicit_expected_protocol(self):
        self.server["Protocol"] = self.client["Protocol"] = 42
        self.write()
        default_result = self.result()
        self.assertEqual(default_result["status"], "FAIL")
        self.assertIn("server: schema/protocol mismatch", default_result["failures"])
        self.assertIn("shooter: schema/protocol mismatch", default_result["failures"])
        self.assertEqual(adjudicate(self.out, self.processes, expected_protocol=42)["status"], "PASS")

    def test_historical_protocol_41_default_remains_accepted(self):
        self.assertEqual(self.result()["status"], "PASS")
        explicit_new = adjudicate(self.out, self.processes, expected_protocol=42)
        self.assertEqual(explicit_new["status"], "FAIL")
        self.assertIn("server: schema/protocol mismatch", explicit_new["failures"])

    @staticmethod
    def slow(revision, frame, kills=0, deaths=0, generation=3, epoch=111, match=2):
        # Raw PlayerSlowState payload excludes the outer transport packet header.
        payload = struct.pack("<HQIBBH", match, epoch, revision, 1, 0, generation)
        payload += struct.pack("<BhHH", 0, 0, kills, deaths) + bytes(64)
        return dict(Epoch=epoch, Match=match, Revision=revision, Frame=frame, Payload=payload.hex())

    def lanes(self):
        for report in (self.server, self.client):
            report["Schema"] = 2
            report["SlowStates"] = [self.slow(1, 6), self.slow(2, 180, kills=1)]
            report["States"][-1]["Kills"] = 1

    def test_schema1_score_lag_still_fails(self):
        self.server["States"][-1]["Kills"] = 1
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_schema2_matching_independent_lanes_allow_cached_score_lag(self):
        self.lanes()
        self.client["States"][-1]["Kills"] = 0
        # Client-local reception clock is not an authority-frame join key.
        self.client["SlowStates"][-1]["Frame"] = 9999
        self.write()
        result = self.result()
        self.assertEqual(result["status"], "PASS")
        self.assertEqual(result["parity"]["shooter"]["healthMismatches"], 0)
        self.assertEqual(result["parity"]["shooter"]["slowSamplesCompared"], 2)
        self.assertEqual(result["parity"]["shooter"]["finalScoreConvergence"], "PASS")

    def test_schema2_fast_health_lag_still_fails(self):
        self.lanes(); self.client["States"][-1]["Hp"] = 50
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_schema2_wrong_slow_score_payload_fails(self):
        self.lanes(); self.client["SlowStates"][-1] = self.slow(2, 180, kills=2)
        self.write()
        result = self.result()
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["parity"]["shooter"]["slowLaneCompleteness"], "FAIL")

    def test_schema2_full_slow_payload_not_just_scores_must_match(self):
        self.lanes()
        row = self.client["SlowStates"][-1]; payload = bytearray.fromhex(row["Payload"])
        struct.pack_into("<f", payload, len(payload) - 4, 1)
        row["Payload"] = payload.hex(); self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_schema2_missing_and_empty_slow_lanes_fail(self):
        for endpoint in ("server", "client"):
            for missing in (True, False):
                with self.subTest(endpoint=endpoint, missing=missing):
                    self.lanes(); report = getattr(self, endpoint)
                    if missing: del report["SlowStates"]
                    else: report["SlowStates"] = []
                    self.write(); self.assertEqual(self.result()["status"], "FAIL")

    def test_schema2_unknown_slow_revision_fails(self):
        self.lanes(); self.client["SlowStates"][-1] = self.slow(3, 180, kills=1)
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_schema2_row_payload_identity_mismatch_fails(self):
        self.lanes(); self.client["SlowStates"][-1]["Revision"] = 3
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_schema2_other_stream_cannot_supply_slow_scores(self):
        self.lanes(); self.client["SlowStates"] = [self.slow(1, 6, epoch=222), self.slow(2, 180, kills=1, epoch=222)]
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_schema2_final_stale_score_fails_even_if_received_payload_is_exact(self):
        self.lanes(); self.client["SlowStates"] = self.client["SlowStates"][:1]
        self.write()
        result = self.result()
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["parity"]["shooter"]["slowLaneCompleteness"], "PASS")
        self.assertEqual(result["parity"]["shooter"]["finalScoreConvergence"], "FAIL")

    def test_schema2_terminal_score_uses_server_frame_not_ever_matching_revision(self):
        self.lanes(); self.server["SlowStates"].append(self.slow(3, 180, kills=2))
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_schema2_future_authority_score_cannot_rewrite_terminal_expected_score(self):
        self.lanes(); self.server["SlowStates"].append(self.slow(3, 181, kills=2))
        self.write()
        self.assertEqual(self.result()["status"], "PASS")

    def test_schema2_terminal_generation_must_be_present_in_slow_lane(self):
        self.lanes()
        for report in (self.server, self.client):
            report["SlowStates"] = [self.slow(1, 6, generation=4), self.slow(2, 180, kills=1, generation=4)]
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_schema2_malformed_slow_payloads_fail(self):
        self.lanes(); good = self.client["SlowStates"][-1]["Payload"]
        for payload in ("bad", good[:-2], good + "00", "ZZ", good[:28] + "09" + good[30:]):
            with self.subTest(payload=payload[:30]):
                self.lanes(); self.client["SlowStates"][-1]["Payload"] = payload
                self.write(); self.assertEqual(self.result()["status"], "FAIL")

    def test_schema2_duplicate_revision_fails(self):
        self.lanes(); self.server["SlowStates"].append(self.server["SlowStates"][-1])
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_schema2_does_not_waive_semantic_gap(self):
        self.lanes(); self.server["Facts"].append(dict(self.server["Facts"][0], Id=2, Payload="DEAD"))
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_mixed_evidence_schemas_fail(self):
        self.lanes(); self.client["Schema"] = 1
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_wrong_protocol(self):
        self.client["Protocol"] = 42
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")


if __name__ == "__main__":
    unittest.main()
