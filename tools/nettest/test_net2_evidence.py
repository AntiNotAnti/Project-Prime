"""Regression checks for false-green campaigns. Run with python -m unittest discover."""
import copy
import json
import struct
from pathlib import Path
import tempfile
import unittest
from net2_evidence import adjudicate, slow_states


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

    def continuous_terminal_fixture(self, observer=False):
        row = dict(self.client["Shots"][0], Key=dict(self.client["Shots"][0]["Key"], ShotId=6),
                   SourceFrame=92, ContinuousPhase=92, Weapon=7, Kind=3)
        self.client["Shots"].append(dict(row, Stage="offered"))
        self.server["Shots"].append(dict(row, Stage="accepted"))
        rejection = dict(row, Stage="rejected", Reason="continuous owner dead",
                         OwnerHealth=0, CurrentOwnerLife=row["Key"]["LifeId"])
        if observer:
            self.server["Shots"].append(dict(row, Stage="committed"))
            self.client["Shots"].append(dict(row, Stage="accepted"))
            self.client["Shots"].append(rejection)
        else:
            self.server["Shots"].append(rejection)
        return rejection

    def test_exact_dead_continuous_terminal_disposition_accounts_for_source(self):
        self.continuous_terminal_fixture(); self.write()
        result = self.result()
        self.assertEqual(result["status"], "PASS")
        self.assertEqual(result["parity"]["shooter"]["terminalRejectedOfferedShots"], 1)
        self.assertEqual(result["observations"]["server"]["continuousTerminalRejectedCount"], 1)

    def test_exact_life_retired_continuous_disposition_accounts_for_observer(self):
        rejection = self.continuous_terminal_fixture(observer=True)
        rejection.update(Reason="continuous owner life retired", CurrentOwnerLife=5)
        self.write(); result = self.result()
        self.assertEqual(result["status"], "PASS")
        self.assertEqual(result["parity"]["shooter"]["terminalRejectedViewShots"], 1)

    def test_invalid_continuous_terminal_evidence_never_excludes_source_loss(self):
        for field, value in [("Kind", 0), ("Weapon", 4), ("ContinuousPhase", 94),
                             ("SourceFrame", 93), ("Charge", 1), ("OwnerHealth", 1),
                             ("CurrentOwnerLife", 5), ("Reason", "expired")]:
            with self.subTest(field=field):
                before_server, before_client = copy.deepcopy(self.server), copy.deepcopy(self.client)
                rejection = self.continuous_terminal_fixture(); rejection[field] = value
                self.write(); self.assertEqual(self.result()["status"], "FAIL")
                self.server, self.client = before_server, before_client

    def test_terminal_wrong_key_or_missing_local_acceptance_fails(self):
        rejection = self.continuous_terminal_fixture(observer=True)
        rejection["Key"] = dict(rejection["Key"], ShotId=7)
        self.write(); self.assertEqual(self.result()["status"], "FAIL")
        rejection["Key"]["ShotId"] = 6
        self.client["Shots"] = [s for s in self.client["Shots"] if not (s["Stage"] == "accepted" and s["Key"]["ShotId"] == 6)]
        self.write(); self.assertEqual(self.result()["status"], "FAIL")

    def test_terminal_retired_reason_requires_changed_life(self):
        rejection = self.continuous_terminal_fixture()
        rejection["Reason"] = "continuous owner life retired"
        self.write(); self.assertEqual(self.result()["status"], "FAIL")

    def test_terminal_committed_or_paid_continuous_tick_is_conflict(self):
        rejection = self.continuous_terminal_fixture()
        self.server["Shots"].append(dict(rejection, Stage="committed"))
        self.write(); self.assertEqual(self.result()["status"], "FAIL")
        self.server["Shots"].pop()
        owner = dict(rejection["Key"], ShotId=rejection["ContinuousPhase"])
        self.server["ContinuousPayments"] = dict(Rows=[dict(Key=dict(OwnerTick=owner))])
        self.write(); self.assertEqual(self.result()["status"], "FAIL")

    def test_phase_skipped_without_exact_terminal_proof_remains_failure(self):
        rejection = self.continuous_terminal_fixture()
        rejection["Stage"] = "phase-skipped"
        self.write(); self.assertEqual(self.result()["status"], "FAIL")

    def turret_retirement_fixture(self):
        row = dict(self.client["Shots"][0], Key=dict(self.client["Shots"][0]["Key"], ShotId=6),
                   SourceFrame=92, ContinuousPhase=0, Weapon=3, Kind=4, ObservedFrame=96)
        self.client["Shots"].append(dict(row, Stage="offered"))
        self.server["Shots"].append(dict(row, Stage="accepted"))
        source = struct.pack("<IIB3fBHHH",96,50,1,0.,0.,0.,0,0,2,99).hex()
        rejection = dict(row,Stage="rejected",Reason="turret source retired before native emission",
                         OwnerHealth=99,CurrentOwnerLife=4,RetiredFormSource=source)
        self.server["Shots"].append(rejection); self.server["Role"]="server"
        self.server["Forms"]=[dict(Stage="authority",Epoch=111,Match=2,Slot=0,Generation=3,Life=4,
                                   ObservedFrame=96,Source=source,BodyHp=99,TurretHp=0,TurretActive=False,Phase="Biped")]
        self.server["CombatPayments"]=dict(PaidShotKeys=[],EvidenceOverflow=0)
        return rejection

    def test_strict_authority_turret_retirement_separate_from_emission(self):
        self.turret_retirement_fixture();self.write();result=self.result()
        self.assertEqual(result["status"],"PASS",result)
        self.assertEqual(result["observations"]["server"]["authorityRetiredTurretCount"],1)
        self.assertEqual(result["parity"]["shooter"]["committedShots"],1)
        self.assertEqual(result["parity"]["shooter"]["acceptedShots"],2)
        self.assertEqual(result["parity"]["shooter"]["authorityRetiredOfferedTurretCount"],1)
        self.assertEqual(result["parity"]["shooter"]["terminalRejectedOfferedShots"],0)

    def test_double_turret_context_is_exact_accepted_uint32_signature(self):
        self.turret_retirement_fixture()
        for report in (self.server,self.client):
            for row in report["Shots"]:
                if row["Key"]["ShotId"] == 6: row["ContinuousPhase"] = 1 << 29
        self.write(); self.assertEqual(self.result()["status"],"PASS")
        self.server["Shots"][-1]["ContinuousPhase"] = 1 << 30
        self.write(); self.assertEqual(self.result()["status"],"FAIL")

    def test_turret_retirement_bad_key_source_clock_or_form_fails(self):
        for field,value in (("SourceFrame",91),("ObservedFrame",97),("Weapon",7),("Kind",3),
                            ("RetiredFormSource","00"),("OwnerHealth",0),("CurrentOwnerLife",3),("ContinuousPhase",-1),("ContinuousPhase",2**32)):
            with self.subTest(field=field):
                self.setUp();r=self.turret_retirement_fixture();r[field]=value;self.write()
                self.assertEqual(self.result()["status"],"FAIL")
        self.setUp();r=self.turret_retirement_fixture();r["Key"]=dict(r["Key"],ShotId=7);self.write()
        self.assertEqual(self.result()["status"],"FAIL")

    def test_active_same_life_turret_is_not_retired(self):
        r=self.turret_retirement_fixture();source=struct.pack("<IIB3fBHHH",96,50,3,0.,0.,0.,1,45,2,99).hex()
        r["RetiredFormSource"]=source
        self.server["Forms"][0].update(Source=source,Phase="Alt",TurretHp=45,TurretActive=True)
        self.write();self.assertEqual(self.result()["status"],"FAIL")

    def unavailable_turret_expiry_fixture(self):
        row = self.turret_retirement_fixture()
        source = struct.pack("<IIB3fBHHH",170,50,3,0.,0.,0.,0,0,2,99).hex()
        accepted = struct.pack("<IIB3fBHHH",96,50,3,0.,0.,0.,0,0,2,99).hex()
        row.update(ObservedFrame=170,RetiredFormSource=source,Reason="turret unavailable throughout native recovery window",
                   AcceptedObservedFrame=96,AcceptedFormSource=accepted,TurretEverAvailable=False,TurretRecoveryFrames=64)
        self.server["Forms"][0].update(ObservedFrame=170,Source=source,Phase="Alt")
        self.server["Forms"].append(dict(self.server["Forms"][0],ObservedFrame=96,Source=accepted))
        for item in self.server["Shots"]:
            if item["Stage"]=="accepted" and item["Key"]["ShotId"]==6:
                item.update(AcceptedObservedFrame=96,AcceptedFormSource=accepted,TurretEverAvailable=False,TurretRecoveryFrames=64)
        return row

    def test_settled_unavailable_turret_exact_recovery_expiry_is_terminal_not_emitted(self):
        self.unavailable_turret_expiry_fixture();self.write();result=self.result()
        self.assertEqual(result["status"],"PASS",result)
        self.assertEqual(result["observations"]["server"]["authorityRetiredTurretCount"],1)
        self.assertEqual(result["observations"]["server"]["authorityUnavailableTurretCount"],1)
        self.assertEqual(result["parity"]["shooter"]["committedShots"],1)
        self.assertEqual(result["parity"]["shooter"]["acceptedShots"],2)
        self.assertEqual(result["parity"]["shooter"]["authorityRetiredOfferedTurretCount"],1)

    def test_unavailable_turret_expiry_cannot_forge_life_form_or_native_window(self):
        for case in ("life","missing-form","active","positive-hp","morphing","early","generic-expiry","paid"):
            with self.subTest(case=case):
                self.setUp();row=self.unavailable_turret_expiry_fixture();form=self.server["Forms"][0]
                if case=="life":form["Life"]=5
                elif case=="missing-form":self.server["Forms"]=[]
                elif case=="paid":self.server["CombatPayments"]["PaidShotKeys"]=[row["Key"]]
                elif case=="generic-expiry":row.update(Stage="expired",Reason="fire-event recovery retention elapsed")
                else:
                    frame=156 if case=="early" else 170
                    phase=2 if case=="morphing" else 3
                    active=1 if case=="active" else 0
                    hp=1 if case=="positive-hp" else 0
                    source=struct.pack("<IIB3fBHHH",frame,50,phase,0.,0.,0.,active,hp,2,99).hex()
                    row.update(ObservedFrame=frame,RetiredFormSource=source)
                    form.update(ObservedFrame=frame,Source=source,Phase="Morphing" if phase==2 else "Alt",
                                TurretActive=bool(active),TurretHp=hp)
                self.write();self.assertEqual(self.result()["status"],"FAIL")

    def test_unavailable_turret_recovery_proof_must_be_typed_and_exact(self):
        for field,value in (("AcceptedObservedFrame",97),("AcceptedObservedFrame",True),
                            ("AcceptedFormSource","00"),("TurretEverAvailable",True),
                            ("TurretEverAvailable",0),("TurretRecoveryFrames",63),("TurretRecoveryFrames",True)):
            with self.subTest(field=field,value=value):
                self.setUp();row=self.unavailable_turret_expiry_fixture();row[field]=value
                self.write();self.assertEqual(self.result()["status"],"FAIL")
        self.setUp();row=self.unavailable_turret_expiry_fixture();del row["AcceptedFormSource"]
        self.write();self.assertEqual(self.result()["status"],"FAIL")
        self.setUp();self.unavailable_turret_expiry_fixture();self.server["Forms"].pop()
        self.write();self.assertEqual(self.result()["status"],"FAIL")
        self.setUp();self.unavailable_turret_expiry_fixture()
        accepted=next(r for r in self.server["Shots"] if r["Stage"]=="accepted" and r["Key"]["ShotId"]==6)
        accepted["TurretEverAvailable"]=True
        self.write();self.assertEqual(self.result()["status"],"FAIL")
        self.setUp();self.unavailable_turret_expiry_fixture()
        visible=struct.pack("<IIB3fBHHH",120,50,3,0.,0.,0.,1,10,2,99).hex()
        self.server["Forms"].append(dict(self.server["Forms"][0],ObservedFrame=120,Source=visible,TurretActive=True,TurretHp=10))
        self.write();self.assertEqual(self.result()["status"],"FAIL")

    def test_unavailable_turret_uses_authority_elapsed_not_owner_source_clock(self):
        self.unavailable_turret_expiry_fixture()
        for report in (self.server,self.client):
            for row in report["Shots"]:
                if row["Key"]["ShotId"]==6:row["SourceFrame"]=40000
        self.write();self.assertEqual(self.result()["status"],"PASS")

    def test_recovery_form_clocks_wrap_and_future_started_forms_fail(self):
        row=self.unavailable_turret_expiry_fixture()
        accepted_at=2**32-33;terminal_at=40;started=2**32-100
        accepted=struct.pack("<IIB3fBHHH",accepted_at,started,3,0.,0.,0.,0,0,2,99).hex()
        terminal=struct.pack("<IIB3fBHHH",terminal_at,started,3,0.,0.,0.,0,0,2,99).hex()
        row.update(ObservedFrame=terminal_at,AcceptedObservedFrame=accepted_at,
                   AcceptedFormSource=accepted,RetiredFormSource=terminal)
        offered=next(r for r in self.server["Shots"] if r["Stage"]=="accepted" and r["Key"]["ShotId"]==6)
        offered.update(ObservedFrame=accepted_at,AcceptedObservedFrame=accepted_at,AcceptedFormSource=accepted)
        self.server["Forms"][0].update(ObservedFrame=terminal_at,Source=terminal)
        self.server["Forms"][1].update(ObservedFrame=accepted_at,Source=accepted)
        self.write();self.assertEqual(self.result()["status"],"PASS")
        future=struct.pack("<IIB3fBHHH",terminal_at,terminal_at+1,3,0.,0.,0.,0,0,2,99).hex()
        row["RetiredFormSource"]=future;self.server["Forms"][0]["Source"]=future
        self.write();self.assertEqual(self.result()["status"],"FAIL")
        row["RetiredFormSource"]=terminal;self.server["Forms"][0]["Source"]=terminal
        future=struct.pack("<IIB3fBHHH",accepted_at,accepted_at+1,3,0.,0.,0.,0,0,2,99).hex()
        row["AcceptedFormSource"]=offered["AcceptedFormSource"]=future;self.server["Forms"][1]["Source"]=future
        self.write();self.assertEqual(self.result()["status"],"FAIL")

    def test_exact_newer_owner_life_proves_retired_source_without_sampled_death(self):
        r=self.turret_retirement_fixture();r["CurrentOwnerLife"]=5;self.server["Forms"][0]["Life"]=5
        self.write();self.assertEqual(self.result()["status"],"PASS")
        self.server["Forms"][0]["Life"]=4;self.write();self.assertEqual(self.result()["status"],"FAIL")

    def test_turret_retirement_cannot_have_payment_or_emission_or_missing_payment_proof(self):
        for case in ("paid","committed","missing","overflow"):
            with self.subTest(case=case):
                self.setUp();r=self.turret_retirement_fixture()
                if case=="paid":self.server["CombatPayments"]["PaidShotKeys"]=[r["Key"]]
                elif case=="committed":self.server["Shots"].append(dict(r,Stage="committed"))
                elif case=="missing":del self.server["CombatPayments"]["PaidShotKeys"]
                else:self.server["CombatPayments"]["EvidenceOverflow"]=1
                self.write();self.assertEqual(self.result()["status"],"FAIL")

    def test_observer_turret_retirement_cannot_excuse_authority_commit(self):
        r=self.turret_retirement_fixture();self.server["Shots"].remove(r)
        self.server["Shots"].append(dict(r,Stage="committed"))
        self.client["Shots"] += [dict(r,Stage="accepted"),r]
        self.write();result=self.result();self.assertEqual(result["status"],"FAIL")
        self.assertEqual(result["observations"]["server"]["authorityRetiredTurretCount"],0)
        self.assertGreater(result["parity"]["shooter"]["viewMissingCommits"],0)

    def test_turret_generic_expiry_remains_unexplained_loss(self):
        r=self.turret_retirement_fixture();r["Reason"]="turret cooldown or pose refused"
        self.write();result=self.result();self.assertEqual(result["status"],"FAIL")
        self.assertEqual(result["observations"]["server"]["authorityRetiredTurretCount"],0)

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
    def slow(revision, frame, kills=0, deaths=0, generation=3, epoch=111, match=2, context=0,
             protocol=41, cosmetic=None, form=None):
        # Raw PlayerSlowState payload excludes the outer transport packet header.
        payload = struct.pack("<HQIBBH", match, epoch, revision, 1, 0, generation)
        payload += struct.pack("<BhHH", 0, 0, kills, deaths)
        if protocol >= 46:
            payload += bytes(18) if cosmetic is None else cosmetic
        if protocol >= 49:
            payload += bytes(30) if form is None else form
        payload += bytes(64)
        return dict(Epoch=epoch, Match=match, Revision=revision, Frame=frame,
                    Context=context, Payload=payload.hex())

    def lanes(self):
        for report in (self.server, self.client):
            report["Schema"] = 2
            report["SlowStates"] = [self.slow(1, 6), self.slow(2, 180, kills=1)]
            report["States"][-1]["Kills"] = 1

    def test_protocol46_slow_sidecar_preserves_scores_and_exact_payload_parity(self):
        self.lanes()
        for report in (self.server, self.client):
            report["Protocol"] = 46
            report["SlowStates"] = [self.slow(1, 6, protocol=46),
                                     self.slow(2, 180, kills=1, protocol=46)]
        self.assertEqual(slow_states(self.server)[-1]["scores"][(0, 3)],
                         struct.pack("<BhHH", 0, 0, 1, 0))
        self.write()
        result = adjudicate(self.out, self.processes, expected_protocol=46)
        self.assertEqual(result["status"], "PASS")
        self.assertEqual(result["coverage"]["shockCoilDrainKillParity"], "NOT_RUN")
        self.client["SlowStates"][-1] = self.slow(2, 180, kills=1, protocol=46,
                                                  cosmetic=b"\x01" + bytes(17))
        self.write()
        self.assertEqual(adjudicate(self.out, self.processes, expected_protocol=46)["status"], "FAIL")

    def test_protocol46_slow_stride_cannot_masquerade_as_legacy_or_reverse(self):
        for protocol, wire_protocol in [(46, 45), (45, 46)]:
            with self.subTest(protocol=protocol):
                report = dict(Protocol=protocol, SlowStates=[self.slow(1, 6, protocol=wire_protocol)])
                with self.assertRaises(ValueError):
                    slow_states(report)

    def test_protocol46_slow_multiple_entries_and_time_offset(self):
        row = self.slow(1, 6, kills=1, protocol=46)
        data = bytearray.fromhex(row["Payload"])
        data[14] = 2
        second = struct.pack("<BH", 1, 4) + struct.pack("<BhHH", 0, 0, 2, 3) + bytes(18)
        row["Payload"] = (data[:43] + second + struct.pack("<16f", *([1.0] * 16))).hex()
        report = dict(Protocol=46, SlowStates=[row])
        self.assertEqual(slow_states(report)[0]["scores"][(1, 4)],
                         struct.pack("<BhHH", 0, 0, 2, 3))
        row["Payload"] = (bytes.fromhex(row["Payload"])[:-64]
                          + struct.pack("<16f", float("nan"), *([1.0] * 15))).hex()
        with self.assertRaises(ValueError):
            slow_states(report)

    def test_protocol49_slow_form_sidecar_preserves_scores_and_exact_payload_parity(self):
        self.lanes()
        for report in (self.server, self.client):
            report["Protocol"] = 49
            report["SlowStates"] = [self.slow(1, 6, protocol=49),
                                    self.slow(2, 180, kills=1, protocol=49)]
        self.assertEqual(slow_states(self.server)[-1]["scores"][(0, 3)], struct.pack("<BhHH", 0, 0, 1, 0))
        self.write()
        self.assertEqual(adjudicate(self.out, self.processes, expected_protocol=49)["status"], "PASS")
        self.client["SlowStates"][-1] = self.slow(2, 180, kills=1, protocol=49, form=b"\x01" + bytes(29))
        self.write()
        self.assertEqual(adjudicate(self.out, self.processes, expected_protocol=49)["status"], "FAIL")

    def test_protocol49_stride_cannot_masquerade_as_previous_or_legacy(self):
        for protocol, wire in ((49, 48), (49, 45), (48, 49), (45, 49)):
            with self.subTest(protocol=protocol, wire=wire):
                with self.assertRaises(ValueError):
                    slow_states(dict(Protocol=protocol, SlowStates=[self.slow(1, 6, protocol=wire)]))
        for protocol in (45, 46, 47, 48, 49):
            self.assertEqual(len(slow_states(dict(Protocol=protocol, SlowStates=[self.slow(1, 6, protocol=protocol)]))), 1)

    def test_protocol49_rejects_obsolete_56byte_slow_entry(self):
        old = self.slow(1, 6, protocol=49, form=bytes(28))
        with self.assertRaises(ValueError):
            slow_states(dict(Protocol=49, SlowStates=[old]))

    def test_protocol49_multiple_entries_use_correct_time_offset(self):
        row = self.slow(1, 6, kills=1, protocol=49)
        data = bytearray.fromhex(row["Payload"]); data[14] = 2
        second = struct.pack("<BH", 1, 4) + struct.pack("<BhHH", 0, 0, 2, 3) + bytes(48)
        row["Payload"] = (data[:73] + second + struct.pack("<16f", *([1.0] * 16))).hex()
        report = dict(Protocol=49, SlowStates=[row])
        self.assertEqual(slow_states(report)[0]["scores"][(1, 4)], struct.pack("<BhHH", 0, 0, 2, 3))
        row["Payload"] = (bytes.fromhex(row["Payload"])[:-64] + struct.pack("<16f", float("nan"), *([1.0] * 15))).hex()
        with self.assertRaises(ValueError): slow_states(report)

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

    def test_schema2_validated_later_membership_removal_uses_authority_clock(self):
        self.lanes()
        removal = self.slow(3, 181, kills=1)
        payload = bytearray.fromhex(removal["Payload"])
        # Replace the one slot with slot 1; slot 0 left after the final fast state.
        payload[15] = 1
        removal["Payload"] = payload.hex()
        self.server["SlowStates"].append(copy.deepcopy(removal))
        removal["Frame"] = 12  # Deliberately unrelated client reception clock.
        self.client["SlowStates"].append(removal)
        self.write()
        result = self.result()
        self.assertEqual(result["status"], "PASS")
        self.assertEqual(result["parity"]["shooter"]["finalScoreConvergence"], "PASS")

    def test_schema2_later_slow_receipt_does_not_hide_same_frame_changed_score(self):
        self.lanes()
        later = self.slow(3, 181, kills=1)
        self.server["SlowStates"] += [later, self.slow(4, 181, kills=2)]
        self.client["SlowStates"].append(copy.deepcopy(later))
        self.write()
        result = self.result()
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["parity"]["shooter"]["slowLaneCompleteness"], "PASS")
        self.assertEqual(result["parity"]["shooter"]["finalScoreConvergence"], "FAIL")

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

    def test_schema2_new_context_reapplication_with_exact_payload_and_recovery_passes(self):
        self.lanes()
        self.client["SlowStates"] += [self.slow(1, 181, context=1),
                                      self.slow(2, 182, kills=1, context=1)]
        self.write()
        result = self.result()
        self.assertEqual(result["status"], "PASS")
        self.assertEqual(result["parity"]["shooter"]["slowSamplesCompared"], 4)

    def test_schema2_final_bootstrap_reseed_to_stale_scores_fails(self):
        self.lanes(); self.client["SlowStates"].append(self.slow(1, 181, context=1))
        self.write()
        result = self.result()
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["parity"]["shooter"]["slowLaneCompleteness"], "PASS")
        self.assertEqual(result["parity"]["shooter"]["finalScoreConvergence"], "FAIL")

    def test_schema2_same_context_reapplication_fails(self):
        self.lanes(); self.client["SlowStates"].append(self.slow(2, 181, kills=1))
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_schema2_new_context_does_not_authorize_forged_payload(self):
        self.lanes(); self.client["SlowStates"].append(self.slow(2, 181, kills=2, context=1))
        self.write()
        self.assertEqual(self.result()["status"], "FAIL")

    def test_schema2_context_required_and_bounded(self):
        self.lanes()
        for context in (None, -1, 2**31, True, "1"):
            with self.subTest(context=context):
                self.lanes(); row = self.client["SlowStates"][-1]
                if context is None: del row["Context"]
                else: row["Context"] = context
                self.write(); self.assertEqual(self.result()["status"], "FAIL")

    def test_schema2_authority_context_must_remain_zero(self):
        self.lanes(); self.server["SlowStates"][-1]["Context"] = 1
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


class RunnerRoleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        import importlib.util
        spec = importlib.util.spec_from_file_location("net2_runner", Path(__file__).with_name("run-net2.py"))
        cls.runner = importlib.util.module_from_spec(spec); spec.loader.exec_module(cls.runner)

    def args(self, *extra):
        return self.runner.parse_args(["--build", "build", "--paths", "paths.txt", "--source", "test", "--out", "out", *extra])

    def test_alt_morph_headless_late_observer_and_real_shooter_roles(self):
        args = self.args("--scenario", "alt-morph", "--players", "3", "--seconds", "20", "--observer-delay", "6", "--profile", "extreme")
        self.assertEqual(args.protocol, self.runner.current_protocol())
        self.assertFalse(args.rendered)
        self.assertEqual(self.runner.client_role(args, 0), ("Samus", False, True))
        self.assertEqual(self.runner.client_role(args, 1), ("Weavel", False, False))
        self.assertEqual(self.runner.client_role(args, 2), ("Trace", True, False))
        self.assertEqual(self.args("--protocol", "41").protocol, 41)

    def test_existing_bomb_and_shot_roles_preserved(self):
        bomb = self.args("--scenario", "bomb-kanden", "--players", "3", "--observer-delay", "4")
        self.assertEqual(self.runner.client_role(bomb, 0), ("Kanden", False, False))
        self.assertEqual(self.runner.client_role(bomb, 2), ("Kanden", True, False))
        duel = self.args("--scenario", "duel", "--players", "3")
        self.assertEqual(self.runner.client_role(duel, 2), ("Trace", False, True))
        duel.rendered = True
        self.assertEqual(self.runner.client_role(duel, 2), ("Trace", True, False))

    def test_fault_overrides_preserve_named_profiles_and_record_effective_values(self):
        self.assertEqual(tuple(self.runner.fault_profile(self.args("--profile", "moderate")).values()),
                         (100, 20, 1, 0, 0))
        args = self.args("--profile", "poor", "--rtt-ms", "150", "--jitter-ms", "20",
                         "--loss-percent", "5", "--reorder-percent", "3.5", "--duplicate-percent", "1")
        self.assertEqual(self.runner.fault_arguments(args, 2),
                         ["-netlag", "150:20", "-netloss", "5%", "-netreorder", "3.5%",
                          "-netduplicate", "1%", "-netseed", "8130"])

    def test_invalid_fault_protocol_seed_and_nonfinite_durations_fail_before_launch(self):
        from contextlib import redirect_stderr
        import io
        invalid = (("--rtt-ms", "10001"), ("--jitter-ms", "-1"), ("--loss-percent", "nan"),
                   ("--reorder-percent", "inf"), ("--duplicate-percent", "101"),
                   ("--protocol", "0"), ("--protocol", "256"), ("--seconds", "nan"),
                   ("--round-minutes", "inf"), ("--drain-seconds", "nan"),
                   ("--observer-delay", "nan"), ("--seed", "2147483647"))
        for flags in invalid:
            with self.subTest(flags=flags), redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                self.args(*flags)

    def test_historical_protocol_does_not_require_checkout_contract(self):
        from unittest.mock import patch
        with patch.object(self.runner, "current_protocol", side_effect=ValueError("missing")):
            self.assertEqual(self.args("--protocol", "41").protocol, 41)
            from contextlib import redirect_stderr
            import io
            with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                self.args()

    def test_observer_delay_rejects_wrong_scenario_player_count_or_duration(self):
        from contextlib import redirect_stderr
        import io
        for extra in (("--scenario", "duel", "--players", "3"),
                      ("--scenario", "alt-morph", "--players", "2"),
                      ("--scenario", "alt-morph", "--players", "3", "--seconds", "4")):
            with self.subTest(extra=extra), redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                self.args(*extra, "--observer-delay", "4")


if __name__ == "__main__":
    unittest.main()
