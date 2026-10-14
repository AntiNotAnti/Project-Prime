import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import validate_context_campaign as validator


STACK = "2 calls for 8192 bytes:\n0 libsystem_malloc.dylib 0x100 malloc + 1\n1 com.apple.AGXMetalG16X 0x120 FragmentProgramVariant + 2\n"
VM = "Physical footprint: 12M\nREGION TYPE\nMALLOC  16M 8M 4M 0K\n"


class CampaignTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        binary = self.root / "native-render-check.dll"
        binary.write_bytes(b"harness")
        (self.root / "ProjectPrime.dll").write_bytes(b"client")
        identity = {"harnessSha256": validator.sha256(binary), "clientSha256": validator.sha256(self.root / "ProjectPrime.dll")}
        native_manifest = {**identity, "processId": 42, "assemblyMvid": "mvid", "harnessAssemblyMvid": "harness-mvid", "runtime": ".NET 10", "openTkVersion": "4.9", "os": "macOS"}
        phases = ["context-00-before-workload", "context-00-before-disposal", "context-00-after-disposal"]
        self.manifest = {"schema": 2, "binary": str(binary), "identity": identity, "identityMatches": True,
                         "sourceSha": "c" * 40, "sourceDirty": False, "fixtureIdentity": {"identityMatches": True}, "host": {"os": "macOS", "architecture": "arm64"},
                         "isolatedUserData": True, "dryRun": False, "complete": True, "exitCode": 0,
                         "command": ["dotnet", str(binary), "-resource-context-cycles", "2", "-resourceframes", "120", "-resource-arm", "map-gl", "-resource-shader-source-ledger"],
                         "environment": {"MallocStackLogging": "1"}, "expectedNativePhases": phases,
                         "expectedNativeCaptures": 3, "nativeCaptureCount": 3, "nativeCapturesComplete": True, "captures": []}
        records = [{"context": 0, "phase": phase} for phase in ("before-context-creation", "context-created", "before-context-disposal", "after-context-disposal")]
        records[1].update(exists=True, contextCurrent=True)
        records[3]["exists"] = False
        records += [{"context": 0, "phase": "post-context-disposal", "sample": sample, "memory": {"processHandleCount": None, "workingSetBytes": 0}} for sample in range(2)]
        self.result = {"manifest": native_manifest, "complete": True, "error": None, "contextCycles": [2], "postDisposalSamples": 2, "driverDrainRequested": False, "records": records}
        self.workload = {"manifest": native_manifest, "contextCount": 1, "cyclesRequested": 2, "cyclesCompleted": 2,
                         "complete": True, "error": None, "drainError": None, "arm": "map-gl", "simulationFramesPerArm": 120,
                         "workload": {"simulationHz": 60, "fixedRng": [12345, 98765], "shadows": "High", "actualFramebuffer": [1920, 1080]},
                         "records": [{"cycle": cycle, "candidate": candidate, "simulationFrame": 120} for cycle in range(2) for candidate in (False, True)],
                         "shaderSourceLedger": {"compileAttemptCallbacks": 4, "linkAttemptCallbacks": 2, "overflowCallbacks": 0,
                                                "sourceStageKeys": 1, "uniqueSourceHashes": 1,
                                                "sources": [{"stage": "world-vertex", "sourceSha256": "a" * 64, "callbacks": 4}]}}
        self.workload_path = self.root / "context.json.contexts/context-00-2cycles.json"
        self.workload_path.parent.mkdir()
        self.manifest["resultArtifacts"] = [{"context": 0, "kind": "context-workload", "path": str(self.workload_path)}]
        for phase in phases:
            steps = []
            for tool, body, cmd in [("vmmap", VM, ["vmmap", "-summary", "42"]), ("malloc-history", STACK, ["malloc_history", "42", "-allBySize", "-fullStacks"])]:
                path = self.root / (phase + "." + tool + ".txt")
                path.write_text(body)
                steps.append({"command": cmd, "exitCode": 0, "path": str(path), "sha256": validator.sha256(path)})
            self.manifest["captures"].append({"phase": phase, "pid": 42, "steps": steps})
        (self.root / "context.log").write_text("NATIVERENDER CONTEXT PASS\n")
        asset_entries = [{"key": "rom/file", "bytes": 3, "sha256": "d" * 64}]
        asset_hash = validator.hashlib.sha256(json.dumps(asset_entries, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
        self.manifest["fixtureIdentity"]["sha256"] = asset_hash
        (self.root / "asset-manifest.json").write_text(json.dumps({"sha256": asset_hash, "files": asset_entries}))
        self.save_receipts()

    def save_receipts(self):
        (self.root / "context.json").write_text(json.dumps(self.result))
        self.workload_path.write_text(json.dumps(self.workload))
        self.manifest["resultSha256"] = validator.sha256(self.root / "context.json")
        self.manifest["logSha256"] = validator.sha256(self.root / "context.log")
        self.manifest["resultArtifacts"][0]["sha256"] = validator.sha256(self.workload_path)

    def validate(self):
        return validator.validate_campaign(self.manifest, self.root)

    def assert_invalid(self, gate=None):
        result = self.validate()
        self.assertEqual(result["status"], "INVALID", result)
        if gate:
            self.assertEqual(next(item for item in result["gates"] if item["name"] == gate)["status"], "INVALID")

    def test_complete_immutable_campaign_valid_but_never_bounded(self):
        result = self.validate()
        self.assertEqual(result["status"], "VALID", result)
        self.assertEqual(result["boundedness"], "UNVERIFIED")
        self.assertEqual(result["nativeAttribution"][0]["categories"]["agxFragmentVariant"]["mallocPrefixBytes"], 8192)
        self.assertIsNone(self.result["records"][-1]["memory"]["processHandleCount"])
        self.assertEqual(self.result["records"][-1]["memory"]["workingSetBytes"], 0)

    def test_missing_duplicate_unexpected_and_reordered_phases_invalid(self):
        original = copy.deepcopy(self.manifest["captures"])
        for changed in [original[:-1], original[:1] + original[:1] + original[2:], original[::-1], original + [{"phase": "unexpected", "steps": []}]]:
            with self.subTest(phases=[item["phase"] for item in changed]):
                self.manifest["captures"] = changed
                self.assert_invalid("capturePhases")

    def test_changed_binary_capture_and_result_hashes_invalid(self):
        for path in [self.root / "native-render-check.dll", Path(self.manifest["captures"][0]["steps"][0]["path"]), self.root / "context.json", self.workload_path]:
            with self.subTest(path=path):
                prior = path.read_bytes()
                path.write_bytes(prior + b" ")
                self.assert_invalid()
                path.write_bytes(prior)

    def test_failed_or_empty_capture_steps_cannot_pass(self):
        self.manifest["captures"][0]["steps"][1]["exitCode"] = -1
        self.assert_invalid("nativeStacks")
        self.manifest["captures"][0]["steps"] = []
        self.assert_invalid("nativeStacks")

    def test_mixed_process_and_context_indices_invalid(self):
        self.manifest["captures"][0]["pid"] = 43
        self.assert_invalid("contextLifecycle")
        self.manifest["captures"][0]["pid"] = 42
        self.result["records"][-1]["context"] = 1
        self.save_receipts()
        self.assert_invalid("contextLifecycle")

    def test_incomplete_missing_duplicate_cycles_and_frame_gaps_invalid(self):
        original = copy.deepcopy(self.workload)
        mutations = [lambda data: data.update(cyclesCompleted=1),
                     lambda data: data["records"].pop(),
                     lambda data: data["records"].append(data["records"][-1]),
                     lambda data: data["records"][0].update(simulationFrame=119)]
        for mutation in mutations:
            self.workload = copy.deepcopy(original)
            mutation(self.workload)
            self.save_receipts()
            self.assert_invalid("workload")

    def test_missing_post_disposal_sample_invalid(self):
        self.result["records"].pop()
        self.save_receipts()
        self.assert_invalid("contextLifecycle")

    def test_silent_error_in_successful_receipt_invalid(self):
        self.workload["drainError"] = "background failure"
        self.save_receipts()
        self.assert_invalid("execution")

    def test_observed_compile_link_status_counts_and_unknown_coverage(self):
        ledger = self.workload["shaderSourceLedger"]
        ledger.update(compileStatusCallbacks=4, compileSuccesses=4, compileFailures=0,
                      linkStatusCallbacks=2, linkSuccesses=2, linkFailures=0)
        self.save_receipts()
        self.assertEqual(self.validate()["status"], "VALID")
        ledger.update(compileStatusCallbacks=3, compileSuccesses=3)
        self.save_receipts()
        self.assertEqual(self.validate()["status"], "UNVERIFIED")
        ledger.update(compileStatusCallbacks=4, compileSuccesses=3, compileFailures=1)
        self.save_receipts()
        self.assert_invalid("shaderSources")

    def test_shader_duplicate_collision_and_callback_gap_invalid(self):
        original = copy.deepcopy(self.workload["shaderSourceLedger"])
        for variant in ("duplicate", "collision", "gap"):
            ledger = copy.deepcopy(original)
            if variant == "duplicate":
                ledger["sources"].append(copy.deepcopy(ledger["sources"][0]))
            elif variant == "collision":
                ledger["sources"][0]["sourceKey"] = "same"
                ledger["sources"].append({"stage": "world-fragment", "sourceSha256": "b" * 64, "callbacks": 1, "sourceKey": "same"})
                ledger.update(sourceStageKeys=2, uniqueSourceHashes=2, compileAttemptCallbacks=5)
            else:
                ledger["compileAttemptCallbacks"] = 5
            self.workload["shaderSourceLedger"] = ledger
            self.save_receipts()
            self.assert_invalid("shaderSources")

    def test_source_overflow_and_dirty_source_are_unverified(self):
        self.workload["shaderSourceLedger"].update(overflowCallbacks=1, compileAttemptCallbacks=5)
        self.save_receipts()
        self.assertEqual(self.validate()["status"], "UNVERIFIED")
        self.manifest["sourceDirty"] = True
        self.assertEqual(self.validate()["status"], "UNVERIFIED")

    def test_unsupported_format_stays_unverified_and_null(self):
        step = self.manifest["captures"][0]["steps"][1]
        path = Path(step["path"])
        path.write_text("New OS stack format unavailable\n")
        step["sha256"] = validator.sha256(path)
        result = self.validate()
        self.assertEqual(result["status"], "UNVERIFIED")
        self.assertIsNone(result["nativeAttribution"][0]["categories"])
        self.assertFalse(result["nativeAttribution"][0]["supported"])

    def test_truncated_recognized_stack_rejected(self):
        for text in [STACK + "1 calls for 4096 bytes:\n", STACK + "2 libsystem_malloc.dylib 0x", STACK + "1 calls for 4096", "[output truncated]\n" + STACK]:
            with self.subTest(text=text):
                self.assertEqual(validator.inspect_stacks(text)[0], "INVALID")
        self.assertEqual(validator.inspect_stacks("Different OS output\n")[0], "UNVERIFIED")
        self.assertEqual(validator.inspect_stacks(STACK.rstrip())[0], "VALID")

    def test_forbidden_cumulative_malloc_command_invalid(self):
        self.manifest["captures"][0]["steps"][1]["command"].append("-highWaterMark")
        self.assert_invalid("nativeStacks")

    def test_fixture_change_source_collision_and_capture_alias_invalid(self):
        self.manifest["fixtureIdentityMatches"] = False
        self.assert_invalid("provenance")
        self.manifest["fixtureIdentityMatches"] = True
        source = self.manifest["captures"][0]["steps"][0]
        self.manifest["captures"][1]["steps"][0] = copy.deepcopy(source)
        self.assert_invalid("artifactIntegrity")

    def test_log_failure_cannot_hide_behind_complete_receipt(self):
        (self.root / "context.log").write_text("NATIVERENDER CONTEXT FAIL campaign.json\n")
        self.manifest["logSha256"] = validator.sha256(self.root / "context.log")
        self.assert_invalid("execution")

    def test_optional_host_query_failure_is_unverified(self):
        self.manifest["host"]["macOsBuild"] = {"exitCode": None, "error": "tool unsupported", "output": None}
        self.assertEqual(self.validate()["status"], "UNVERIFIED")

    def test_malformed_receipt_returns_invalid_without_throwing(self):
        self.manifest["schema"] = "not an integer"
        self.assert_invalid()

    def test_driver_drain_phases_and_samples_validate(self):
        self.manifest["command"].append("-resource-context-driver-drain")
        self.result["driverDrainRequested"] = True
        for phase in ("after-glfw-poll", "after-glfw-terminate"):
            self.result["records"].extend({"context": 0, "phase": phase, "sample": index} for index in range(2))
            self.manifest["expectedNativePhases"].append(phase)
            capture = copy.deepcopy(self.manifest["captures"][0])
            capture["phase"] = phase
            for step in capture["steps"]:
                path = self.root / (phase + "." + Path(step["path"]).suffix + Path(step["path"]).stem.split(".")[-1])
                path.write_bytes(Path(step["path"]).read_bytes())
                step["path"] = str(path)
            self.manifest["captures"].append(capture)
        self.manifest["expectedNativeCaptures"] = self.manifest["nativeCaptureCount"] = 5
        self.save_receipts()
        self.assertEqual(self.validate()["status"], "VALID", self.validate())
        self.result["records"].pop()
        self.save_receipts()
        self.assert_invalid("contextLifecycle")

    def test_duplicate_json_key_is_not_silently_overwritten(self):
        path = self.root / "duplicate.json"
        path.write_text('{"complete": false, "complete": true}')
        with self.assertRaisesRegex(ValueError, "duplicate JSON key"):
            validator.read_json(path)

    def test_cli_machine_readable_invalid_nonzero_and_valid_zero(self):
        manifest_path = self.root / "runner-manifest.json"
        for valid in (True, False):
            self.manifest["exitCode"] = 0 if valid else 1
            manifest_path.write_text(json.dumps(self.manifest))
            run = subprocess.run([sys.executable, str(Path(validator.__file__).resolve()), str(self.root)], capture_output=True, text=True)
            self.assertEqual(run.returncode, 0 if valid else 1, run.stderr)
            self.assertEqual(json.loads(run.stdout)["status"], "VALID" if valid else "INVALID")


if __name__ == "__main__":
    unittest.main()
