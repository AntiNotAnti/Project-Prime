"""Small deterministic guard tests for the map-load campaign driver."""

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import uuid

import run


def valid_receipt(room="fixture-room"):
    return {
        "schema": 1,
        "kind": "map-load-receipt",
        "room": room,
        "loadedRoom": room,
        "loadedRoomCount": 1,
        "mode": "Battle",
        "requestedPlayers": 8,
        "scenePlayerCount": 8,
        "ready": True,
        "readyElapsedMilliseconds": 123.5,
        "outcome": "passed",
        "exitCode": 0,
        "stages": {
            "firstSuccessfulPresentWithAllPlayersLive": {"elapsedMilliseconds": 123.5},
        },
    }


class FakeProcess:
    def __init__(self, wait_actions):
        self.wait_actions = list(wait_actions)
        self.terminated = False
        self.killed = False
        self.return_code = None

    def poll(self):
        return self.return_code

    def wait(self, timeout=None):
        action = self.wait_actions.pop(0)
        if isinstance(action, BaseException):
            raise action
        self.return_code = action
        return action

    def terminate(self):
        self.terminated = True

    def kill(self):
        self.killed = True


class MapLoadDriverTests(unittest.TestCase):
    def test_rejects_malformed_receipt(self):
        with tempfile.TemporaryDirectory() as temporary:
            receipt = Path(temporary) / "receipt.json"
            receipt.write_text("{broken", encoding="utf-8")
            with self.assertRaisesRegex(run.CampaignError, "malformed"):
                run.validate_receipt(receipt, "fixture-room")

    def test_rejects_not_ready_receipt(self):
        with tempfile.TemporaryDirectory() as temporary:
            receipt = Path(temporary) / "receipt.json"
            data = valid_receipt()
            data["ready"] = False
            data["notReadyReason"] = "players did not spawn"
            receipt.write_text(json.dumps(data), encoding="utf-8")
            with self.assertRaisesRegex(run.CampaignError, "did not reach.*players did not spawn"):
                run.validate_receipt(receipt, "fixture-room")

    def test_nearest_rank_percentiles(self):
        samples = list(range(1, 21))
        self.assertEqual(run.nearest_rank(samples, 50), 10)
        self.assertEqual(run.nearest_rank(samples, 95), 19)
        self.assertEqual(run.nearest_rank(samples, 99), 20)

    def test_baseline_candidate_order_alternates(self):
        self.assertEqual(run.paired_order(0), ("baseline", "candidate"))
        self.assertEqual(run.paired_order(1), ("candidate", "baseline"))
        self.assertEqual(run.paired_order(2), ("baseline", "candidate"))

    def test_timeout_terminates_only_owned_process(self):
        process = FakeProcess([
            subprocess.TimeoutExpired(["fake"], 1),
            -15,
        ])

        def factory(*_args, **_kwargs):
            return process

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            result = run.run_process(
                command=["fake"],
                cwd=root,
                env={"PROJECT_PRIME_USER_DATA": str(root / "profile")},
                timeout_seconds=1,
                stdout_path=root / "stdout.log",
                stderr_path=root / "stderr.log",
                popen_factory=factory,
            )
        self.assertTrue(result["timedOut"])
        self.assertTrue(result["terminatedByRunner"])
        self.assertEqual(result["returnCode"], -15)
        self.assertTrue(process.terminated)

    def test_wait_interruption_terminates_owned_child_and_reraises(self):
        process = FakeProcess([KeyboardInterrupt(), -15])

        def factory(*_args, **_kwargs):
            return process

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with self.assertRaises(KeyboardInterrupt) as caught:
                run.run_process(
                    command=["fake"],
                    cwd=root,
                    env={"PROJECT_PRIME_USER_DATA": str(root / "profile")},
                    timeout_seconds=1,
                    stdout_path=root / "stdout.log",
                    stderr_path=root / "stderr.log",
                    popen_factory=factory,
                )
        self.assertTrue(process.terminated)
        result = caught.exception.map_load_process_result
        self.assertTrue(result["interrupted"])
        self.assertTrue(result["terminatedByRunner"])
        self.assertEqual(result["returnCode"], -15)

    def test_accepts_only_complete_pass_receipt(self):
        with tempfile.TemporaryDirectory() as temporary:
            receipt = Path(temporary) / "receipt.json"
            receipt.write_text(json.dumps(valid_receipt()), encoding="utf-8")
            parsed = run.validate_receipt(receipt, "fixture-room")
        self.assertEqual(parsed["readyElapsedMilliseconds"], 123.5)

    def test_private_asset_copy_rewrites_every_root_and_preserves_sources(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source_a = root / "native-a"
            source_b = root / "native-b"
            source_a.mkdir()
            source_b.mkdir()
            (source_a / "original.bin").write_bytes(b"native-a")
            (source_b / "original.bin").write_bytes(b"native-b")
            paths_file = root / "paths.txt"
            paths_file.write_text(
                f"AMHE0={source_a}\nAMHE1={source_b}\nExport={root / 'old-export'}\n",
                encoding="utf-8",
            )
            before = {source: run.inventory_asset_tree(source)[1]
                      for source in (source_a, source_b)}
            roots = run.configured_asset_roots(paths_file)
            private_root = root / "private"
            private_root.mkdir()
            rewrites, copies = run.copy_asset_roots(roots, private_root)
            profile = private_root / "profile"
            paths_hash = run.make_profile(profile, paths_file, rewrites)
            rewritten = (profile / "paths.txt").read_text(encoding="utf-8")
            lines = rewritten.splitlines()
            values = {line.split("=", 1)[0]: Path(line.split("=", 1)[1])
                      for line in lines if "=" in line}

            self.assertEqual(len(copies), 2)
            self.assertNotEqual(values["AMHE0"], source_a)
            self.assertNotEqual(values["AMHE1"], source_b)
            self.assertEqual((values["AMHE0"] / "original.bin").read_bytes(), b"native-a")
            self.assertEqual((values["AMHE1"] / "original.bin").read_bytes(), b"native-b")
            self.assertEqual(values["Export"], profile / "Export")
            self.assertEqual(run.sha256_file(profile / "paths.txt"), paths_hash)
            self.assertEqual({source: run.inventory_asset_tree(source)[1]
                              for source in (source_a, source_b)}, before)

    def test_asset_copy_rejects_source_overlap_before_writing(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "native"
            source.mkdir()
            (source / "original.bin").write_bytes(b"unchanged")
            paths_file = root / "paths.txt"
            paths_file.write_text(f"AMHE0={source}\n", encoding="utf-8")
            roots = run.configured_asset_roots(paths_file)
            with self.assertRaisesRegex(run.CampaignError, "overlaps its source"):
                run.copy_asset_roots(roots, source / "nested-private")
            self.assertEqual(sorted(path.name for path in source.iterdir()), ["original.bin"])

    def test_runtime_namespaces_isolate_cold_samples_and_reuse_warm_arm(self):
        campaign_id = uuid.UUID("12345678-1234-5678-1234-567812345678")
        cold_one = run.runtime_namespace_for(
            campaign_id, "application-cold", "fixture-room", "baseline", 1
        )
        cold_two = run.runtime_namespace_for(
            campaign_id, "application-cold", "fixture-room", "baseline", 2
        )
        cold_candidate = run.runtime_namespace_for(
            campaign_id, "application-cold", "fixture-room", "candidate", 1
        )
        warm_preseed = run.runtime_namespace_for(
            campaign_id, "persistent-cache-warm", "fixture-room", "baseline"
        )
        warm_sample = run.runtime_namespace_for(
            campaign_id, "persistent-cache-warm", "fixture-room", "baseline"
        )
        warm_candidate = run.runtime_namespace_for(
            campaign_id, "persistent-cache-warm", "fixture-room", "candidate"
        )

        self.assertEqual(len({cold_one, cold_two, cold_candidate}), 3)
        self.assertEqual(warm_preseed, warm_sample)
        self.assertNotEqual(warm_preseed, warm_candidate)
        command = run.build_command(
            "dotnet", Path("ProjectPrime.dll"), "fixture-room", Path("maps"),
            Path("receipt.json"), cold_one,
        )
        self.assertEqual(command[command.index("-customruntimenamespace") + 1], cold_one)

    def test_campaign_records_interruption_before_private_tree_cleanup(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "native"
            source.mkdir()
            (source / "original.bin").write_bytes(b"keep source untouched")
            paths_file = root / "paths.txt"
            paths_file.write_text(f"AMHE0={source}\n", encoding="utf-8")
            maps = root / "maps"
            maps.mkdir()
            baseline = root / "baseline.dll"
            candidate = root / "candidate.dll"
            baseline.write_bytes(b"baseline")
            candidate.write_bytes(b"candidate")
            output = root / "evidence"

            class InterruptedProcess:
                def __init__(self):
                    self.return_code = None
                    self.terminated = False

                def poll(self):
                    return self.return_code

                def terminate(self):
                    self.terminated = True

                def kill(self):
                    self.return_code = -9

                def wait(self, timeout=None):
                    if self.return_code is None and not self.terminated:
                        raise KeyboardInterrupt()
                    self.return_code = -15
                    return self.return_code

            child = InterruptedProcess()

            def factory(*_args, **_kwargs):
                return child

            args = run.argparse.Namespace(
                baseline=baseline,
                candidate=candidate,
                dotnet=sys.executable,
                game_paths=paths_file,
                maps_dir=maps,
                rooms=["fixture-room"],
                repeat=1,
                timeout_seconds=10,
                output=output,
            )
            with self.assertRaises(KeyboardInterrupt):
                run.run_campaign(args, popen_factory=factory)

            campaign = json.loads((output / "campaign.json").read_text(encoding="utf-8"))
            run_record_path = output / "runs" / "application-cold" / "room-01-fixture-room" / "trial-001-1-baseline" / "run.json"
            run_record = json.loads(run_record_path.read_text(encoding="utf-8"))
            self.assertEqual(campaign["status"], "interrupted")
            self.assertEqual(run_record["status"], "interrupted")
            self.assertTrue(child.terminated)
            self.assertEqual(run_record["returnCode"], -15)
            self.assertFalse(Path(campaign["privateAssetCopies"][0]["privateCopyDirectory"]).exists())
            self.assertEqual((source / "original.bin").read_bytes(), b"keep source untouched")


if __name__ == "__main__":
    unittest.main()
