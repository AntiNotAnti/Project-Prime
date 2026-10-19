"""Synthetic protocol tests. These do not contain or certify an original DS trace."""
import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest

import compare_ds as oracle


def project_bytes(project):
    scenario = project["scenario"]
    canonical = [scenario[k] for k in ("id", "version", "ticks", "seed", "captureInterval", "content", "tier")]
    for point in project["checkpoints"]:
        flattened = [point["tick"]]
        for key, value in sorted(point["values"].items()):
            flattened.extend((key, value))
        canonical.append(flattened)
    project["canonicalHash"] = oracle.sha256(json.dumps(canonical, separators=(",", ":")).encode())
    return json.dumps(project).encode()


def fixture(event=False):
    fields = ([("weapon", "integer", "endpoint"), ("shotsFired", "integer", "sum-pair")] if event else
              [("position4096.y", "fixed4096", "endpoint"), ("velocity4096.y", "fixed4096", "endpoint")])
    points = []
    deltas = [1, 2, 1, 0, 1, 1]
    for tick in range(1, 7):
        values = {"weapon": 0, "shotsFired": deltas[tick - 1]} if event else {
            "position4096.y": tick * 10 if tick % 2 == 0 else 9000,
            "velocity4096.y": 20 - tick if tick % 2 == 0 else -9000}
        points.append({"tick": tick, "values": values})
    project = {"format": 1, "scenario": {"id": "synthetic.fixture", "version": 1, "ticks": 6,
               "seed": 123456, "content": "synthetic:AMHE1:ROOM", "captureInterval": 1,
               "tier": "F2", "description": "Synthetic protocol test only; not actual engine evidence."},
               "engineRevision": "a" * 40, "canonicalHash": "", "checkpoints": points}
    encoded = project_bytes(project)
    rows = ["f,emuFrame," + ",".join(f"v{i}" for i in range(len(fields)))]
    for frame in range(1, 11):
        project_tick = max(2, min(6, frame - 4))
        project_tick -= project_tick % 2
        cells = []
        for target, encoding, sampling in fields:
            value = points[project_tick - 1]["values"][target]
            if sampling == "sum-pair":
                value += points[project_tick - 2]["values"][target]
            cells.append(f"{value / 4096:.6f}" if encoding == "fixed4096" else str(value))
        rows.append(f"{frame},{200 + frame}," + ",".join(cells))
    ds_data = ("\n".join(rows) + "\n").encode()
    setup, inputs = oracle.sha256(b"synthetic setup"), oracle.sha256(b"synthetic input")
    manifest = {"format": 1, "case": "weapon.cadence" if event else "movement.jump",
                "evidenceKind": "synthetic-protocol-test",
                "reference": {"sha256": oracle.sha256(ds_data), "captureKind": "synthetic", "game": "AMHE1",
                    "romSha1": oracle.AMHE1_SHA1, "logicHz": 30, "sampleHz": 60, "samplePhase": "post-step",
                    "logicUpdateParity": 0, "tool": {"name": "synthetic", "version": "test", "core": "none"},
                    "workflowRevision": oracle.UPSTREAM_REVISION, "routeSha256": oracle.sha256(b"synthetic route"),
                    "initialStateSha256": oracle.sha256(b"synthetic state"), "room": "ROOM", "setupSha256": setup,
                    "inputsSha256": inputs},
                "project": {"sha256": oracle.sha256(encoded), "sourceRevision": "a" * 40,
                    "sourceStateSha256": oracle.sha256(b"synthetic source state"),
                    "binarySha256": oracle.sha256(b"synthetic binary"), "simulationHz": 60, "balancedMode": False,
                    "protocolVersion": 53, "replayFormatVersion": 1, "replayFingerprint": oracle.sha256(b"synthetic replay"),
                    "collisionSha256": oracle.sha256(b"synthetic collision"), "room": "ROOM", "setupSha256": setup,
                    "inputsSha256": inputs},
                "alignment": {"dsFrameColumn": "f", "firstDsFrame": 6, "firstProjectTick": 2, "logicTicks": 3},
                "fields": [{"dsColumn": f"v{i}", "projectField": target, "encoding": encoding,
                    "projectSampling": sampling, "absoluteTolerance": 0, "rationale": "Synthetic exact-match protocol fixture only."}
                    for i, (target, encoding, sampling) in enumerate(fields)]}
    return manifest, ds_data, encoded, project


def cadence_producer_fixture():
    """Synthetic witness protocol only; no DS or actual engine values."""
    initial = {"capture.simulationHz": 60, "capture.sampleHz": 60, "capture.postStep": 0,
               "capture.balancedMode": 0, "capture.warmupTicks": 120, "capture.diagnosticOnly": 1,
               "capture.originalTraceVerified": 0, "actorSlot": 1, "hunter": 0, "weapon": 0,
               "roomId": 0, "health": 99, "charge": 0, "ammoUa": 400, "ammoMissile": 50,
               "rng1": 123, "rng2": 456, "shotsFired.counter": 7}
    for name in ("position", "velocity", "facing"):
        initial.update({f"{name}4096.{axis}": 0 for axis in "xyz"})
    points = [{"tick": 0, "values": initial}]
    counter, old_down = 7, False
    for tick in range(1, 181):
        down = ((tick - 1) // 2) % 15 < 6
        delta = int(down and not old_down)
        values = {"capture.simulationHz": 60, "capture.sampleHz": 60, "capture.postStep": 1,
                  "capture.balancedMode": 0, "capture.diagnosticOnly": 1,
                  "capture.originalTraceVerified": 0, "weapon": 0,
                  "input.shootDown": int(down), "input.shootPressed": int(down and not old_down),
                  "input.shootReleased": int(not down and old_down), "shotsFired.counterBefore": counter,
                  "shotsFired.counter": counter + delta, "shotsFired": delta, "projectiles": 1}
        points.append({"tick": tick, "values": values})
        counter, old_down = counter + delta, down
    return {"format": 1, "scenario": {"id": oracle.CADENCE_SCENARIO, "version": 1, "ticks": 180,
            "seed": 123456, "content": "extracted:AMHE1:MP3 PROVING GROUND:cadence-v1",
            "captureInterval": 1, "tier": "F2", "description": "Synthetic producer witness protocol only."},
            "engineRevision": "a" * 40, "canonicalHash": "", "checkpoints": points}


class CadenceProducerWitnessTests(unittest.TestCase):
    def setUp(self):
        self.project = cadence_producer_fixture()

    def validate(self):
        return oracle.validate_project(oracle.read_json(project_bytes(self.project)))

    def test_synthetic_witness_validation_never_promotes_original_accuracy(self):
        before = json.loads(json.dumps(self.project))
        points = self.validate()
        self.assertEqual(sum(points[t]["shotsFired"] for t in range(1, 181)), 6)
        self.assertEqual(sum(points[t]["projectiles"] for t in range(1, 181)), 180)
        self.assertEqual(points[1]["shotsFired.counterBefore"], 7)
        self.assertEqual(oracle.coverage()["originalAccuracy"], "NOT_VERIFIED")
        before["canonicalHash"] = self.project["canonicalHash"]
        self.assertEqual(self.project, before)

    def test_missing_frame_or_initial_state_rejected(self):
        for index in (0, 1, 89, 180):
            with self.subTest(index=index):
                self.project = cadence_producer_fixture()
                del self.project["checkpoints"][index]
                with self.assertRaisesRegex(oracle.TraceError, "initial state and every"):
                    self.validate()

    def test_wrong_counter_delta_rewind_and_saturation_rejected(self):
        for key, value in (("shotsFired", 9), ("shotsFired.counterBefore", 0),
                           ("shotsFired.counter", 0), ("shotsFired.counter", 2**31 - 1)):
            with self.subTest(key=key, value=value):
                self.project = cadence_producer_fixture()
                self.project["checkpoints"][2]["values"][key] = value
                with self.assertRaisesRegex(oracle.TraceError, "Cadence"):
                    self.validate()

    def test_projectile_population_cannot_replace_event_count(self):
        for point in self.project["checkpoints"][1:]:
            point["values"]["shotsFired"] = point["values"]["projectiles"]
        with self.assertRaisesRegex(oracle.TraceError, "canonical accepted-shot counter"):
            self.validate()

    def test_wrong_input_edges_and_unpaired_timeline_rejected(self):
        for tick, key in ((1, "input.shootPressed"), (2, "input.shootDown"),
                          (13, "input.shootReleased"), (31, "input.shootPressed")):
            with self.subTest(tick=tick, key=key):
                self.project = cadence_producer_fixture()
                fields = self.project["checkpoints"][tick]["values"]
                fields[key] = 1 - fields[key]
                with self.assertRaisesRegex(oracle.TraceError, "paired capture route"):
                    self.validate()

    def test_changed_rules_phase_hz_or_evidence_claim_rejected(self):
        for tick, key, value in ((0, "capture.balancedMode", 1), (1, "capture.postStep", 0),
                                 (2, "capture.sampleHz", 30), (0, "capture.originalTraceVerified", 1),
                                 (180, "capture.diagnosticOnly", 0)):
            with self.subTest(tick=tick, key=key):
                self.project = cadence_producer_fixture()
                self.project["checkpoints"][tick]["values"][key] = value
                with self.assertRaisesRegex(oracle.TraceError, "metadata missing or changed"):
                    self.validate()

    def test_changed_contract_or_missing_initial_witness_rejected(self):
        self.project["scenario"]["seed"] += 1
        with self.assertRaisesRegex(oracle.TraceError, "producer contract"):
            self.validate()
        self.project = cadence_producer_fixture()
        del self.project["checkpoints"][0]["values"]["facing4096.x"]
        with self.assertRaisesRegex(oracle.TraceError, "initial-state witnesses"):
            self.validate()


class ComparisonProtocolTests(unittest.TestCase):
    def setUp(self):
        self.manifest, self.ds, self.encoded, self.project = fixture()

    def compare(self):
        return oracle.compare(self.manifest, self.ds, self.encoded, allow_synthetic=True)

    def update_project(self):
        self.encoded = project_bytes(self.project)
        self.manifest["project"]["sha256"] = oracle.sha256(self.encoded)

    def update_ds(self, data):
        self.ds = data
        self.manifest["reference"]["sha256"] = oracle.sha256(data)

    def rejected(self, fragment):
        with self.assertRaisesRegex(oracle.TraceError, fragment):
            self.compare()

    def test_explicit_offset_and_two_to_one_endpoint_mapping(self):
        report = self.compare()
        self.assertEqual(report["status"], "SYNTHETIC_PROTOCOL_MATCH")
        self.assertEqual(report["originalAccuracy"], "NOT_VERIFIED")
        self.assertEqual(report["alignment"]["lastDsFrame"], 10)
        self.assertEqual(report["alignment"]["lastProjectTick"], 6)
        self.assertEqual(report["fields"][0]["samples"], 3)

    def test_synthetic_requires_explicit_opt_in(self):
        with self.assertRaisesRegex(oracle.TraceError, "--allow-synthetic"):
            oracle.compare(self.manifest, self.ds, self.encoded)

    def test_synthetic_cannot_be_labeled_as_emulator(self):
        self.manifest["reference"]["captureKind"] = "emulator"
        self.rejected("Capture kind")

    def test_first_difference_is_earliest_tick_then_field(self):
        self.project["checkpoints"][3]["values"]["position4096.y"] += 2
        self.project["checkpoints"][5]["values"]["velocity4096.y"] += 10
        self.update_project()
        report = self.compare()
        self.assertEqual(report["status"], "SYNTHETIC_PROTOCOL_DIVERGED")
        self.assertEqual(report["firstDifference"]["dsFrame"], 8)
        self.assertEqual(report["firstDifference"]["projectTick"], 4)
        self.assertEqual(report["firstDifference"]["absoluteError"], 2)
        self.assertEqual(report["fields"][1]["maxAbsoluteError"], 10)

    def test_tolerance_boundary_is_inclusive_and_reported(self):
        self.manifest["fields"][0]["absoluteTolerance"] = 2
        self.project["checkpoints"][3]["values"]["position4096.y"] += 2
        self.update_project()
        report = self.compare()
        self.assertIsNone(report["firstDifference"])
        self.assertEqual(report["fields"][0]["maxAbsoluteError"], 2)

    def test_no_search_for_better_temporal_alignment(self):
        self.manifest["alignment"]["firstProjectTick"] = 3
        self.manifest["alignment"]["logicTicks"] = 2
        self.assertIsNotNone(self.compare()["firstDifference"])

    def test_phase_must_match_declared_logic_parity(self):
        self.manifest["alignment"]["firstDsFrame"] = 5
        self.rejected("phase")

    def test_event_counts_include_odd_and_even_project_frames(self):
        self.manifest, self.ds, self.encoded, self.project = fixture(event=True)
        self.assertIsNone(self.compare()["firstDifference"])
        self.project["checkpoints"][0]["values"]["shotsFired"] += 1
        self.update_project()
        report = self.compare()
        self.assertEqual(report["firstDifference"]["projectTick"], 2)
        self.assertEqual(report["firstDifference"]["expected"], 3)
        self.assertEqual(report["firstDifference"]["actual"], 4)

    def test_event_endpoint_capture_cannot_hide_odd_frame_events(self):
        self.manifest, self.ds, self.encoded, self.project = fixture(event=True)
        self.manifest["fields"][1]["projectSampling"] = "endpoint"
        self.rejected("Event counters")

    def test_event_comparison_rejects_sparse_checkpoints(self):
        self.manifest, self.ds, self.encoded, self.project = fixture(event=True)
        self.project["checkpoints"] = self.project["checkpoints"][1::2]
        self.project["scenario"]["captureInterval"] = 2
        self.update_project()
        self.rejected("both 60 Hz")

    def test_event_deltas_cannot_be_negative(self):
        self.manifest, self.ds, self.encoded, self.project = fixture(event=True)
        self.project["checkpoints"][0]["values"]["shotsFired"] = -1
        self.update_project()
        self.rejected("negative")

    def test_discrete_fields_have_no_numeric_tolerance(self):
        self.manifest, self.ds, self.encoded, self.project = fixture(event=True)
        self.manifest["fields"][0]["absoluteTolerance"] = 1
        self.rejected("exact agreement")

    def test_balanced_mode_is_separate(self):
        self.manifest["project"]["balancedMode"] = True
        self.rejected("Balanced Mode")

    def test_matching_setup_and_input_are_required(self):
        for key in ("room", "setupSha256", "inputsSha256"):
            with self.subTest(key=key):
                original = self.manifest["project"][key]
                self.manifest["project"][key] = "OTHER" if key == "room" else "b" * 64
                self.rejected(key + " mismatch")
                self.manifest["project"][key] = original

    def test_source_binary_replay_and_collision_identities_are_required(self):
        for key in ("sourceRevision", "sourceStateSha256", "binarySha256", "replayFingerprint", "collisionSha256"):
            with self.subTest(key=key):
                original = self.manifest["project"][key]
                self.manifest["project"][key] = "short"
                self.rejected("full lowercase hash")
                self.manifest["project"][key] = original

    def test_project_revision_is_bound_to_capture(self):
        self.manifest["project"]["sourceRevision"] = "b" * 40
        self.rejected("revision differs")

    def test_regression_pack_cannot_impersonate_engine_capture(self):
        self.project["scenario"]["tier"] = "F1"
        self.update_project()
        self.rejected("actual-engine F2")

    def test_artifact_hashes_are_checked(self):
        self.ds += b"\n"
        self.rejected("artifact hash mismatch")

    def test_project_canonical_hash_is_checked(self):
        self.project["checkpoints"][1]["values"]["position4096.y"] += 1
        self.encoded = json.dumps(self.project).encode()
        self.manifest["project"]["sha256"] = oracle.sha256(self.encoded)
        self.rejected("canonical hash mismatch")

    def test_all_current_baseline_canonical_hashes_are_compatible(self):
        paths = sorted(Path(__file__).with_name("baselines").glob("*.json"))
        self.assertEqual(len(paths), 47)
        for path in paths:
            with self.subTest(scenario=path.stem):
                oracle.validate_project(oracle.read_json(path.read_bytes()))

    def test_required_case_fields_cannot_be_omitted(self):
        self.manifest["fields"].pop()
        self.rejected("missing required observables")

    def test_duplicate_targets_and_wrong_units_are_rejected(self):
        self.manifest["fields"][1]["projectField"] = self.manifest["fields"][0]["projectField"]
        self.rejected("Duplicate")
        self.manifest["fields"][1]["projectField"] = "velocity4096.y"
        self.manifest["fields"][1]["encoding"] = "integer"
        self.rejected("encoding disagrees")

    def test_missing_project_checkpoint_or_field_fails(self):
        self.project["checkpoints"].pop(3)
        self.update_project()
        self.rejected("checkpoint or observable")

    def test_duplicate_or_out_of_order_project_ticks_fail(self):
        self.project["checkpoints"][2]["tick"] = 2
        self.update_project()
        self.rejected("unique and increasing")

    def test_unknown_json_properties_are_rejected(self):
        self.manifest["automaticAlignment"] = True
        self.rejected("unknown properties")

    def test_duplicate_json_properties_are_rejected(self):
        with self.assertRaisesRegex(oracle.TraceError, "duplicate"):
            oracle.read_json(b'{"format":1,"format":2}')
        with self.assertRaisesRegex(oracle.TraceError, "integer exceeds normalized int64"):
            oracle.read_json(b'{"format":' + b'1' * 5000 + b'}')

    def test_booleans_cannot_impersonate_integer_versions(self):
        self.manifest["project"]["protocolVersion"] = True
        self.rejected("integer outside")

    def test_ds_frame_gap_duplicate_order_and_ragged_rows_fail(self):
        original = self.ds
        rows = original.decode().splitlines()
        for changed in (rows[:3] + rows[4:], rows[:3] + [rows[2]] + rows[3:],
                        rows[:3] + [rows[4], rows[3]] + rows[5:], rows[:3] + [rows[3] + ",1"] + rows[4:]):
            with self.subTest(rows=changed[3:5]):
                self.update_ds(("\n".join(changed) + "\n").encode())
                self.rejected("DS CSV")

    def test_missing_or_duplicate_ds_columns_fail(self):
        for header in ("f,emuFrame,v0,v0", "f,emuFrame,v0,missing"):
            self.update_ds((header + "\n" + "\n".join(self.ds.decode().splitlines()[1:]) + "\n").encode())
            self.rejected("DS CSV")

    def test_nan_infinity_fraction_and_overflow_fail(self):
        for value in ("NaN", "Infinity", "1e200", "1e1000000", "1e99999999999999999999999999999", "1_0", " 0", ""):
            with self.subTest(value=value):
                rows = self.ds.decode().splitlines()
                cells = rows[6].split(",")
                cells[2] = value
                rows[6] = ",".join(cells)
                self.update_ds(("\n".join(rows) + "\n").encode())
                self.rejected("DS cell")
        with self.assertRaisesRegex(oracle.TraceError, "fractional"):
            oracle.normalize_ds("0.5", "integer")

    def test_fixed_point_rounds_midpoints_away_from_zero(self):
        self.assertEqual(oracle.normalize_ds("0.0001220703125", "fixed4096"), 1)
        self.assertEqual(oracle.normalize_ds("-0.0001220703125", "fixed4096"), -1)

    def test_no_complete_pair_or_out_of_range_window_fails(self):
        self.manifest["alignment"]["logicTicks"] = 4
        self.rejected("exceeds capture")

    def test_status_never_promotes_framework_checks_to_original_acceptance(self):
        report = oracle.coverage()
        self.assertEqual(report["committedOriginalTraces"], 0)
        self.assertEqual(report["originalAccuracy"], "NOT_VERIFIED")
        self.assertEqual(len(report["cases"]), 13)
        self.assertTrue(all(case["status"] == "NOT_VERIFIED" for case in report["cases"]))

    def test_cli_missing_capture_is_nonzero_and_machine_readable(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(io.StringIO()) as output:
            code = oracle.main(["compare", str(Path(directory) / "missing.json"), "--ds-csv", "missing.csv", "--project-json", "missing.json"])
        self.assertEqual(code, 2)
        self.assertEqual(json.loads(output.getvalue())["status"], "NOT_COMPARABLE")

    def test_cli_synthetic_match_and_divergence_exit_codes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            root.joinpath("manifest.json").write_text(json.dumps(self.manifest))
            root.joinpath("ds.csv").write_bytes(self.ds)
            root.joinpath("project.json").write_bytes(self.encoded)
            args = ["compare", str(root / "manifest.json"), "--ds-csv", str(root / "ds.csv"),
                    "--project-json", str(root / "project.json"), "--allow-synthetic"]
            with contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(oracle.main(args), 0)
            self.assertEqual(json.loads(output.getvalue())["originalAccuracy"], "NOT_VERIFIED")
            self.project["checkpoints"][1]["values"]["position4096.y"] += 1
            self.update_project()
            root.joinpath("manifest.json").write_text(json.dumps(self.manifest))
            root.joinpath("project.json").write_bytes(self.encoded)
            with contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(oracle.main(args), 1)
            self.assertEqual(json.loads(output.getvalue())["status"], "SYNTHETIC_PROTOCOL_DIVERGED")


if __name__ == "__main__":
    unittest.main(verbosity=2)
