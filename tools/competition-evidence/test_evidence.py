"""Synthetic contract tests only: these never establish renderer/device performance."""
import copy
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

from evidence import audit, compare, sha256, source_lock


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.manifest = {
            "schemaVersion": 1,
            "source": {"repository": "test-only/fixture", "sha": "a" * 40, "dirty": False},
            "build": {"sourceSha": "a" * 40, "sourceDirty": False, "configuration": "Release",
                      "toolchain": "test fixture", "command": "test fixture", "flags": {"optimized": True}},
            "device": {"machineId": "TEST-ONLY", "kind": "physical", "os": "test OS",
                       "cpu": "test CPU", "gpu": "test GPU", "driver": "test driver"},
            "workload": {"fixtureVersion": "cpp00-v2", "mode": "offline", "room": "MP3 PROVING GROUND",
                         "mapSha256": "b" * 64, "replaySha256": "c" * 64, "simulationSha256": "d" * 64,
                         "simulationHz": 60, "players": 8, "hunter": "Samus", "detail": "high",
                         "width": 1920, "height": 1080, "renderScale": 1, "presentationHz": 144,
                         "runtimeSettings": {"shadows": "High", "textureQuality": "High", "lighting": True,
                                             "resolutionScale": 1, "vsync": "Off", "effectiveCap": 144}, "seed": 12345,
                         "rngSeeds": {"rng1": 12345, "rng2": 98765},
                         "measurementStartRng": {"rng1": 111, "rng2": 222}, "measurementEndRng": {"rng1": 333, "rng2": 444},
                         "inputExecution": "frozen-device-snapshots-with-bot-ai", "replayExecuted": False,
                         "roster": [{"slot": i, "hunter": h, "bot": True} for i, h in enumerate(
                             ("Samus", "Kanden", "Trace", "Sylux", "Noxus", "Spire", "Weavel", "Samus"))],
                         "warmupSimulationSteps": 240, "measurementSimulationSteps": 1200,
                         "harness": "native-render-check-v2", "timingScope": "completed-frame-includes-swap",
                         "profiling": False, "readback": False, "phase": "entry"},
            "measurements": {name: {"status": "measured", "value": value} for name, value in
                             (("frameP50Ms", 1), ("frameP95Ms", 2), ("frameP99Ms", 3),
                              ("allocatedBytes", 0), ("gcCollections", 0), ("residentBytes", 1000))},
            "artifacts": [],
        }
        self.manifest["measurements"].update({name: {"status": "unavailable", "value": None,
            "reason": "test does not measure this"} for name in
            ("cpuMs", "gpuMs", "gcPauseMs", "directNativeBytes", "inputLatencyMs")})
        raw = {"complete": True, "room": "MP3 PROVING GROUND", "cap": 144,
               "framebuffer": [1920, 1080], "renderer": "test GPU", "droppedSimulationSteps": 0,
               "stalls": 0, "samples": 1000, "frameIntervalP50Ms": 1, "frameIntervalP95Ms": 2,
               "frameIntervalP99Ms": 3, "allocatedBytes": 0, "workingSetBytes": 1000, "gcCollections": [0, 0, 0],
               **self.manifest["workload"]["runtimeSettings"]}
        lock = {"sourceSha": "a" * 40, "dirty": False}
        contents = {"raw-result": raw, "source-lock": lock, "asset-manifest": {"sha256": "b" * 64},
                    "simulation-report": {"canonicalHash": "d" * 64}}
        for role in ("client-binary", "harness-binary", "raw-result", "raw-log", "source-lock", "build-log",
                     "asset-manifest", "replay-fixture", "simulation-report", "simulation-verify-log"):
            path = self.root / role
            path.write_text(json.dumps(contents.get(role, role)))
            self.manifest["artifacts"].append({"role": role, "path": role, "sha256": sha256(path)})
            if role == "replay-fixture":
                self.manifest["workload"]["replaySha256"] = sha256(path)

        raw["simulationFrames"] = 1440
        raw["assemblyMvid"] = "12345678-1234-1234-1234-123456789abc"
        raw["observedWorkload"] = {
            "schemaVersion": 1, "eligibleNormalTiming": True, "failure": None,
            "mode": "offline", "phase": "entry", "inputExecution": "frozen-device-snapshots-with-bot-ai", "replayExecuted": False,
            "seedAssignmentReadback": {"rng1": 12345, "rng2": 98765},
            "measurementStartRng": {"rng1": 111, "rng2": 222}, "measurementEndRng": {"rng1": 333, "rng2": 444},
            "measurementStartTick": 240, "measurementEndTick": 1440,
            "startRoster": copy.deepcopy(self.manifest["workload"]["roster"]),
            "endRoster": copy.deepcopy(self.manifest["workload"]["roster"]),
            "clientBinary": {"name": "ProjectPrime", "mvid": raw["assemblyMvid"], "sha256": next(a["sha256"] for a in self.manifest["artifacts"] if a["role"] == "client-binary")},
            "harnessBinary": {"name": "native-render-check", "mvid": "23456789-1234-1234-1234-123456789abc", "sha256": next(a["sha256"] for a in self.manifest["artifacts"] if a["role"] == "harness-binary")}}
        self.replace_raw(raw, self.manifest)

    def replace_raw(self, raw, manifest):
        path = self.root / "raw-result"
        path.write_text(json.dumps(raw))
        next(a for a in manifest["artifacts"] if a["role"] == "raw-result")["sha256"] = sha256(path)

    def test_complete_matched_fixture_has_no_performance_pass(self):
        self.assertEqual(audit(self.manifest, self.root), [])
        result = compare(self.manifest, copy.deepcopy(self.manifest), self.root, self.root)
        self.assertEqual(result["status"], "COMPARABLE")
        self.assertNotIn("PASS", json.dumps(result))

    def test_mismatched_devices_maps_settings_and_capture_modes_refused(self):
        for group, key, value in (("device", "gpu", "different GPU"), ("device", "driver", "different driver"),
                ("device", "machineId", "different machine"), ("device", "os", "different OS"),
                ("workload", "mapSha256", "e" * 64), ("workload", "replaySha256", "e" * 64),
                ("workload", "simulationSha256", "e" * 64), ("workload", "detail", "low"),
                ("workload", "presentationHz", 240), ("workload", "runtimeSettings", {"shadows": "Low"}),
                ("build", "flags", {"optimized": False}), ("workload", "phase", "reentry")):
            with self.subTest(group=group, key=key):
                other = copy.deepcopy(self.manifest)
                other[group][key] = value
                self.assertEqual(compare(self.manifest, other, self.root, self.root)["status"], "NOT_COMPARABLE")

    def test_different_source_and_binary_are_permitted_comparison_variables(self):
        other = copy.deepcopy(self.manifest)
        other["source"]["repository"] = "test-only/other-fork"
        other["source"]["sha"] = other["build"]["sourceSha"] = "f" * 40
        lock = self.root / "other-source-lock"
        lock.write_text(json.dumps({"sourceSha": "f" * 40, "dirty": False}))
        other["artifacts"] = [a for a in other["artifacts"] if a["role"] != "source-lock"]
        other["artifacts"].append({"role": "source-lock", "path": lock.name, "sha256": sha256(lock)})
        self.assertEqual(compare(self.manifest, other, self.root, self.root)["status"], "COMPARABLE")

    def test_dirty_source_or_build_never_counts_as_exact_head(self):
        for group, key, value in (("source", "dirty", True), ("source", "dirty", None),
                                  ("build", "sourceDirty", True), ("build", "sourceSha", "f" * 40)):
            with self.subTest(group=group, key=key):
                data = copy.deepcopy(self.manifest)
                data[group][key] = value
                self.assertTrue(audit(data, self.root))

    def test_missing_fields_on_both_sides_still_refused(self):
        for group, key in (("device", "gpu"), ("workload", "mapSha256"), ("workload", "seed"),
                           ("build", "flags"), ("measurements", "frameP99Ms")):
            with self.subTest(group=group, key=key):
                data = copy.deepcopy(self.manifest)
                del data[group][key]
                self.assertEqual(compare(data, data, self.root, self.root)["status"], "NOT_COMPARABLE")

    def test_synthetic_emulator_profiled_readback_runs_refused(self):
        for group, key, value in (("device", "kind", "synthetic"), ("device", "kind", "emulator"),
                                  ("workload", "profiling", True), ("workload", "readback", True)):
            with self.subTest(group=group, key=key):
                data = copy.deepcopy(self.manifest)
                data[group][key] = value
                self.assertTrue(audit(data, self.root))

    def test_local_bots_cannot_be_relabeled_dedicated_online(self):
        self.manifest["workload"]["mode"] = "dedicated-online"
        self.assertTrue(any("offline fixture" in e for e in audit(self.manifest, self.root)))

    def test_tampered_artifact_and_wrong_source_lock_refused(self):
        (self.root / "client-binary").write_text("changed")
        self.assertTrue(any("client-binary" in e for e in audit(self.manifest, self.root)))
        lock = self.root / "source-lock"
        lock.write_text(json.dumps({"sourceSha": "a" * 40, "dirty": True}))
        next(a for a in self.manifest["artifacts"] if a["role"] == "source-lock")["sha256"] = sha256(lock)
        self.assertTrue(any("clean matching source" in e for e in audit(self.manifest, self.root)))

    def test_native_raw_metrics_and_observations_must_match(self):
        for key, value in (("complete", False), ("droppedSimulationSteps", 1), ("samples", 0),
                           ("frameIntervalP99Ms", 4), ("renderer", "different GPU"), ("gcCollections", [0])):
            with self.subTest(key=key):
                raw = json.loads((self.root / "raw-result").read_text())
                raw[key] = value
                path = self.root / "changed-result"
                path.write_text(json.dumps(raw))
                data = copy.deepcopy(self.manifest)
                artifact = next(a for a in data["artifacts"] if a["role"] == "raw-result")
                artifact.update(path=path.name, sha256=sha256(path))
                self.assertTrue(audit(data, self.root))

    def test_unavailable_is_null_and_explicit_never_zero_or_nan(self):
        for item in ({"status": "unavailable", "value": 0, "reason": "unsupported"},
                     {"status": "unavailable", "value": None},
                     {"status": "measured", "value": float("nan")},
                     {"status": "measured", "value": float("inf")},
                     {"status": "measured", "value": True}):
            with self.subTest(item=item):
                data = copy.deepcopy(self.manifest)
                data["measurements"]["gpuMs"] = item
                self.assertTrue(audit(data, self.root))

    def test_unknown_settings_and_malformed_source_lock_fail_closed(self):
        for flags in ({"optimized": None}, {"optimized": "unknown"}, {"optimized": float("nan")}):
            with self.subTest(flags=flags):
                data = copy.deepcopy(self.manifest)
                data["build"]["flags"] = flags
                self.assertTrue(audit(data, self.root))
        path = self.root / "source-lock"
        path.write_text("[]")
        next(a for a in self.manifest["artifacts"] if a["role"] == "source-lock")["sha256"] = sha256(path)
        self.assertTrue(audit(self.manifest, self.root))

    def test_observed_workload_missing_old_or_diagnostic_receipts_refused(self):
        for key, value in (("observedWorkload", None), ("transitionGc", {}), ("captures", [{"readback": True}])):
            with self.subTest(key=key):
                data = copy.deepcopy(self.manifest)
                raw = json.loads((self.root / "raw-result").read_text())
                original = copy.deepcopy(raw)
                raw[key] = value
                self.replace_raw(raw, data)
                self.assertTrue(audit(data, self.root))
                self.replace_raw(original, self.manifest)

    def test_observed_workload_exact_roster_seed_ticks_binary_and_execution_required(self):
        mutations = [
            ("schemaVersion", 0), ("eligibleNormalTiming", False), ("eligibleNormalTiming", 1),
            ("failure", "module changed"), ("phase", "reentry"), ("mode", "dedicated-online"),
            ("replayExecuted", True), ("inputExecution", "replay"),
            ("seedAssignmentReadback", {"rng1": 12345, "rng2": 1}),
            ("measurementStartRng", {"rng1": False, "rng2": 1}),
            ("measurementEndRng", {"rng1": 0x100000000, "rng2": 1}),
            ("measurementStartTick", 239), ("measurementEndTick", 1441),
            ("startRoster", []), ("endRoster", []),
            ("clientBinary", {"name": "ProjectPrime", "sha256": "a" * 64, "mvid": "bad"}),
            ("harnessBinary", None)]
        original = json.loads((self.root / "raw-result").read_text())
        for key, value in mutations:
            with self.subTest(key=key, value=value):
                data = copy.deepcopy(self.manifest)
                raw = copy.deepcopy(original)
                raw["observedWorkload"][key] = value
                self.replace_raw(raw, data)
                self.assertTrue(audit(data, self.root))
        self.replace_raw(original, self.manifest)
        for field_name, value in (("hunter", "Trace"), ("bot", False), ("slot", 3)):
            data = copy.deepcopy(self.manifest)
            raw = copy.deepcopy(original)
            raw["observedWorkload"]["startRoster"][0][field_name] = value
            raw["observedWorkload"]["endRoster"][0][field_name] = value
            self.replace_raw(raw, data)
            self.assertTrue(audit(data, self.root))
        self.replace_raw(original, self.manifest)

    def test_attached_reference_replay_cannot_be_claimed_as_executed(self):
        data = copy.deepcopy(self.manifest)
        data["workload"]["replayExecuted"] = True
        raw = json.loads((self.root / "raw-result").read_text())
        raw["observedWorkload"]["replayExecuted"] = True
        self.replace_raw(raw, data)
        self.assertTrue(any("replay" in e for e in audit(data, self.root)))

    def test_roster_and_seed_are_comparison_dimensions(self):
        other = copy.deepcopy(self.manifest)
        other["workload"]["roster"][1]["bot"] = False
        self.assertEqual(compare(self.manifest, other, self.root, self.root)["status"], "NOT_COMPARABLE")
        other = copy.deepcopy(self.manifest)
        other["workload"]["rngSeeds"]["rng2"] += 1
        self.assertEqual(compare(self.manifest, other, self.root, self.root)["status"], "NOT_COMPARABLE")

    def test_source_lock_observes_untracked_and_tracked_changes(self):
        repo = self.root / "repo"
        repo.mkdir()
        def git(*args):
            return subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True)
        git("init", "-q")
        (repo / "file").write_text("original")
        git("add", "file")
        git("-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-qm", "fixture")
        self.assertFalse(source_lock(repo)["dirty"])
        (repo / "untracked").write_text("new")
        self.assertTrue(source_lock(repo)["dirty"])
        (repo / "untracked").unlink()
        (repo / "file").write_text("modified")
        self.assertTrue(source_lock(repo)["dirty"])


if __name__ == "__main__":
    unittest.main()
