"""Small deterministic guard tests for the map-load campaign driver."""

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
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

    def test_package_catalog_copies_only_regular_packages_and_preserves_sources(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "catalog"
            nested = source / "nested"
            nested.mkdir(parents=True)
            package_a = source / "published-a.ppmap"
            package_b = nested / "published-b.PPMAP"
            package_a.write_bytes(b"published package a")
            package_b.write_bytes(b"published package b")
            loose_json = source / "loose-recipe.json"
            loose_pk3 = source / "source.pk3"
            loose_json.write_text('{"source":"..\\outside\\custom.tex"}', encoding="utf-8")
            loose_pk3.write_bytes(b"conversion source only")
            source_hashes = {path: run.sha256_file(path) for path in (package_a, package_b, loose_json, loose_pk3)}
            manifest, catalog_hash, package_bytes = run.inventory_package_catalog(source)
            private_root = root / "private"
            private_root.mkdir()
            private_directory = run.package_catalog_path_for(
                private_root, "application-cold", "room-01-fixture", "baseline", 1
            )

            info = run.copy_package_catalog(source, private_directory)

            copied_files = sorted(
                path.relative_to(private_directory).as_posix()
                for path in private_directory.rglob("*") if path.is_file()
            )
            self.assertEqual(copied_files, ["nested/published-b.PPMAP", "published-a.ppmap"])
            self.assertEqual(info["files"], 2)
            self.assertEqual(info["sizeBytes"], package_bytes)
            self.assertEqual(info["sourcePackageCatalogSha256"], catalog_hash)
            self.assertEqual(run.verify_private_package_catalog(info), catalog_hash)
            self.assertNotEqual(private_directory / package_a.name, package_a)
            self.assertFalse((private_directory / loose_json.name).exists())
            self.assertFalse((private_directory / loose_pk3.name).exists())

            unexpected = private_directory / "generated.tex"
            unexpected.write_bytes(b"private generated output")
            with self.assertRaisesRegex(run.CampaignError, "non-.ppmap"):
                run.verify_private_package_catalog(info)
            unexpected.unlink()

            (private_directory / package_a.name).write_bytes(b"private runtime mutation")
            self.assertEqual(package_a.read_bytes(), b"published package a")
            self.assertEqual(package_b.read_bytes(), b"published package b")
            self.assertEqual({path: run.sha256_file(path) for path in source_hashes}, source_hashes)
            with self.assertRaisesRegex(run.CampaignError, "catalog changed"):
                run.verify_private_package_catalog(info)

    def test_package_catalog_rejects_overlap_and_traversal(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "catalog"
            source.mkdir()
            (source / "published.ppmap").write_bytes(b"package")
            with self.assertRaisesRegex(run.CampaignError, "overlaps its source"):
                run.copy_package_catalog(source, source / "nested-private")
            self.assertEqual(sorted(path.name for path in source.iterdir()), ["published.ppmap"])

            manifest = [{"path": "../escape.ppmap", "sizeBytes": 0, "sha256": ""}]
            payload = json.dumps(manifest, sort_keys=True, separators=(",", ":")).encode("utf-8")
            private = root / "private" / "catalog"
            with self.assertRaisesRegex(run.CampaignError, "unsafe package path"):
                run.copy_package_catalog(
                    source,
                    private,
                    expected_manifest=manifest,
                    expected_hash=run.hashlib.sha256(payload).hexdigest(),
                    expected_bytes=0,
                )
            self.assertFalse(private.exists())
            self.assertFalse((root / "escape.ppmap").exists())

    def test_package_catalog_rejects_symlink_sources(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "catalog"
            source.mkdir()
            external = root / "outside.ppmap"
            external.write_bytes(b"do not follow")
            try:
                (source / "linked.ppmap").symlink_to(external)
            except (OSError, NotImplementedError) as exc:
                self.skipTest(f"symlink creation is unavailable: {exc}")
            with self.assertRaisesRegex(run.CampaignError, "symbolic link"):
                run.inventory_package_catalog(source)

    def test_package_catalog_enforces_scan_and_per_package_limits(self):
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / "catalog"
            source.mkdir()
            (source / "published.ppmap").write_bytes(b"12345")
            with patch.object(run, "MAX_MAP_PACKAGE_BYTES", 4):
                with self.assertRaisesRegex(run.CampaignError, "per-package limit"):
                    run.inventory_package_catalog(source)
            (source / "extra.txt").write_bytes(b"x")
            with patch.object(run, "MAX_MAP_CATALOG_ENTRIES", 1):
                with self.assertRaisesRegex(run.CampaignError, "entry scan limit"):
                    run.inventory_package_catalog(source)

    def test_private_profiles_override_global_q3_cache_and_catalog_lifetimes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            paths_file = root / "paths.txt"
            paths_file.write_text("Export=old\n", encoding="utf-8")
            warm_profile = root / "warm-profile"
            run.make_profile(warm_profile, paths_file, {})
            cold_profile = root / "cold-profile"
            run.make_profile(cold_profile, paths_file, {})
            ambient = {"PROJECT_PRIME_Q3_CACHE": "global-cache", "KEEP": "value"}

            warm_preseed_env = run.profile_environment(ambient, warm_profile)
            warm_sample_env = run.profile_environment(ambient, warm_profile)
            cold_env = run.profile_environment(ambient, cold_profile)
            self.assertEqual(warm_preseed_env["PROJECT_PRIME_Q3_CACHE"], str(warm_profile / "Q3ImportCache"))
            self.assertEqual(warm_sample_env["PROJECT_PRIME_Q3_CACHE"], warm_preseed_env["PROJECT_PRIME_Q3_CACHE"])
            self.assertNotEqual(cold_env["PROJECT_PRIME_Q3_CACHE"], warm_preseed_env["PROJECT_PRIME_Q3_CACHE"])
            self.assertNotEqual(cold_env["PROJECT_PRIME_Q3_CACHE"], ambient["PROJECT_PRIME_Q3_CACHE"])
            self.assertEqual(warm_preseed_env["KEEP"], "value")

            private_root = root / "maps"
            cold_one = run.package_catalog_path_for(private_root, "application-cold", "room", "baseline", 1)
            cold_two = run.package_catalog_path_for(private_root, "application-cold", "room", "baseline", 2)
            cold_other_arm = run.package_catalog_path_for(private_root, "application-cold", "room", "candidate", 1)
            warm_preseed = run.package_catalog_path_for(private_root, "persistent-cache-warm", "room", "baseline")
            warm_sample = run.package_catalog_path_for(private_root, "persistent-cache-warm", "room", "baseline")
            warm_other_arm = run.package_catalog_path_for(private_root, "persistent-cache-warm", "room", "candidate")
            self.assertEqual(len({cold_one, cold_two, cold_other_arm}), 3)
            self.assertEqual(warm_preseed, warm_sample)
            self.assertNotEqual(warm_preseed, warm_other_arm)

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
        private_root = Path("private")
        cold_map_one = run.package_catalog_path_for(
            private_root, "application-cold", "room", "baseline", 1
        )
        cold_map_two = run.package_catalog_path_for(
            private_root, "application-cold", "room", "baseline", 2
        )
        warm_map_preseed = run.package_catalog_path_for(
            private_root, "persistent-cache-warm", "room", "baseline"
        )
        warm_map_sample = run.package_catalog_path_for(
            private_root, "persistent-cache-warm", "room", "baseline"
        )
        self.assertNotEqual(cold_map_one, cold_map_two)
        self.assertEqual(warm_map_preseed, warm_map_sample)
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
            package = maps / "published.ppmap"
            package.write_bytes(b"published package bytes")
            loose_recipe = maps / "loose.json"
            loose_recipe.write_text('{"source":"..\\outside\\asset.tex"}', encoding="utf-8")
            pk3 = maps / "conversion-source.pk3"
            pk3.write_bytes(b"pk3 conversion input")
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

            launched = {}

            def factory(command, **kwargs):
                map_directory = Path(command[command.index("-mapdir") + 1])
                launched["command"] = command
                launched["environment"] = kwargs["env"]
                launched["mapDirectory"] = map_directory
                launched["mapFiles"] = sorted(
                    path.relative_to(map_directory).as_posix()
                    for path in map_directory.rglob("*") if path.is_file()
                )
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
            self.assertTrue(campaign["privatePackageCatalogs"][0]["removedAfterRun"])
            self.assertFalse(Path(campaign["privatePackageCatalogs"][0]["privateCopyDirectory"]).exists())
            self.assertNotEqual(launched["mapDirectory"], maps)
            self.assertEqual(launched["mapFiles"], ["published.ppmap"])
            self.assertEqual(
                run_record["environmentOverrides"]["PROJECT_PRIME_Q3_CACHE"],
                str(Path(run_record["projectPrimeUserData"]) / "Q3ImportCache"),
            )
            self.assertEqual(package.read_bytes(), b"published package bytes")
            self.assertEqual(loose_recipe.read_text(encoding="utf-8"), '{"source":"..\\outside\\asset.tex"}')
            self.assertEqual(pk3.read_bytes(), b"pk3 conversion input")
            self.assertEqual((source / "original.bin").read_bytes(), b"keep source untouched")

    def test_campaign_cleans_cold_package_catalog_after_timeout(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "native"
            source.mkdir()
            (source / "original.bin").write_bytes(b"keep source untouched")
            paths_file = root / "paths.txt"
            paths_file.write_text(f"AMHE0={source}\n", encoding="utf-8")
            maps = root / "maps"
            maps.mkdir()
            (maps / "published.ppmap").write_bytes(b"published package bytes")
            baseline = root / "baseline.dll"
            candidate = root / "candidate.dll"
            baseline.write_bytes(b"baseline")
            candidate.write_bytes(b"candidate")
            output = root / "evidence"

            child = FakeProcess([subprocess.TimeoutExpired(["fake"], 1), -15])
            launched = {}

            def factory(command, **kwargs):
                launched["mapDirectory"] = Path(command[command.index("-mapdir") + 1])
                launched["environment"] = kwargs["env"]
                return child

            args = run.argparse.Namespace(
                baseline=baseline,
                candidate=candidate,
                dotnet=sys.executable,
                game_paths=paths_file,
                maps_dir=maps,
                rooms=["fixture-room"],
                repeat=1,
                timeout_seconds=1,
                output=output,
            )

            self.assertEqual(run.run_campaign(args, popen_factory=factory), 1)
            campaign = json.loads((output / "campaign.json").read_text(encoding="utf-8"))
            run_record_path = output / "runs" / "application-cold" / "room-01-fixture-room" / "trial-001-1-baseline" / "run.json"
            run_record = json.loads(run_record_path.read_text(encoding="utf-8"))
            self.assertTrue(child.terminated)
            self.assertEqual(campaign["status"], "failed")
            self.assertTrue(campaign["privatePackageCatalogs"][0]["removedAfterRun"])
            self.assertFalse(launched["mapDirectory"].exists())
            self.assertEqual(run_record["status"], "failed")
            self.assertEqual(
                run_record["environmentOverrides"]["PROJECT_PRIME_Q3_CACHE"],
                launched["environment"]["PROJECT_PRIME_Q3_CACHE"],
            )
            self.assertEqual((maps / "published.ppmap").read_bytes(), b"published package bytes")
            self.assertEqual((source / "original.bin").read_bytes(), b"keep source untouched")

    def test_campaign_uses_private_cold_and_stable_warm_package_catalogs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "native"
            source.mkdir()
            (source / "original.bin").write_bytes(b"native source")
            paths_file = root / "paths.txt"
            paths_file.write_text(f"AMHE0={source}\n", encoding="utf-8")
            maps = root / "maps"
            maps.mkdir()
            package = maps / "published.ppmap"
            package.write_bytes(b"published package bytes")
            (maps / "loose.json").write_text('{"source":"outside.tex"}', encoding="utf-8")
            baseline = root / "baseline.dll"
            candidate = root / "candidate.dll"
            baseline.write_bytes(b"baseline")
            candidate.write_bytes(b"candidate")
            output = root / "evidence"

            class PassingProcess:
                def __init__(self):
                    self.return_code = None

                def poll(self):
                    return self.return_code

                def wait(self, timeout=None):
                    self.return_code = 0
                    return 0

                def terminate(self):
                    self.return_code = -15

                def kill(self):
                    self.return_code = -9

            launched = []

            def factory(command, **kwargs):
                map_directory = Path(command[command.index("-mapdir") + 1])
                report = Path(command[command.index("-maploadreport") + 1])
                room = command[command.index("-maptest") + 1]
                report.write_text(json.dumps(valid_receipt(room)), encoding="utf-8")
                launched.append({
                    "mapDirectory": map_directory,
                    "mapFiles": sorted(
                        path.relative_to(map_directory).as_posix()
                        for path in map_directory.rglob("*") if path.is_file()
                    ),
                    "environment": kwargs["env"],
                })
                return PassingProcess()

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

            self.assertEqual(run.run_campaign(args, popen_factory=factory), 0)
            campaign = json.loads((output / "campaign.json").read_text(encoding="utf-8"))
            summary = json.loads((output / "summary.json").read_text(encoding="utf-8"))
            self.assertEqual(campaign["status"], "complete")
            self.assertEqual(len(launched), 6)
            self.assertTrue(all(item["mapFiles"] == ["published.ppmap"] for item in launched))
            self.assertTrue(all(item["mapDirectory"] != maps for item in launched))
            self.assertTrue(all(item["environment"]["PROJECT_PRIME_Q3_CACHE"]
                                == str(Path(item["environment"]["PROJECT_PRIME_USER_DATA"]) / "Q3ImportCache")
                                for item in launched))

            run_records = [
                (item, json.loads((output / item["runMetadata"]).read_text(encoding="utf-8")))
                for item in summary["runs"]
            ]
            warm_baseline_preseed = next(
                record for item, record in run_records
                if item["cacheKind"] == "persistent-cache-warm"
                and item["arm"] == "baseline" and item["runType"] == "preseed"
            )
            warm_baseline_sample = next(
                record for item, record in run_records
                if item["cacheKind"] == "persistent-cache-warm"
                and item["arm"] == "baseline" and item["runType"] == "sample"
            )
            warm_candidate_preseed = next(
                record for item, record in run_records
                if item["cacheKind"] == "persistent-cache-warm"
                and item["arm"] == "candidate" and item["runType"] == "preseed"
            )
            self.assertEqual(warm_baseline_preseed["mapDirectory"], warm_baseline_sample["mapDirectory"])
            self.assertEqual(
                warm_baseline_preseed["environmentOverrides"]["PROJECT_PRIME_Q3_CACHE"],
                warm_baseline_sample["environmentOverrides"]["PROJECT_PRIME_Q3_CACHE"],
            )
            self.assertNotEqual(
                warm_baseline_preseed["mapDirectory"], warm_candidate_preseed["mapDirectory"]
            )
            self.assertNotEqual(
                warm_baseline_preseed["environmentOverrides"]["PROJECT_PRIME_Q3_CACHE"],
                warm_candidate_preseed["environmentOverrides"]["PROJECT_PRIME_Q3_CACHE"],
            )
            cold_records = [record for item, record in run_records
                            if item["cacheKind"] == "application-cold"]
            self.assertEqual(len(cold_records), 2)
            self.assertEqual(len({record["mapDirectory"] for record in cold_records}), 2)
            self.assertEqual(len({record["environmentOverrides"]["PROJECT_PRIME_Q3_CACHE"]
                                  for record in cold_records}), 2)
            cold_catalogs = [item for item in campaign["privatePackageCatalogs"]
                             if item["cacheKind"] == "application-cold"]
            self.assertEqual(len(cold_catalogs), 2)
            self.assertTrue(all(item["removedAfterRun"] for item in cold_catalogs))
            warm_catalogs = [item for item in campaign["privatePackageCatalogs"]
                             if item["cacheKind"] == "persistent-cache-warm"]
            self.assertEqual(len(warm_catalogs), 2)
            self.assertTrue(all(not Path(item["privateCopyDirectory"]).exists() for item in warm_catalogs))
            self.assertEqual(package.read_bytes(), b"published package bytes")
            self.assertTrue((maps / "loose.json").exists())


if __name__ == "__main__":
    unittest.main()
