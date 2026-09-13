"""Focused tests for single-owner, source-safe map cooking."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "cook-maps.py"
SPEC = importlib.util.spec_from_file_location("prime_map_cook", SCRIPT)
assert SPEC and SPEC.loader
COOK = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(COOK)


class MapCookTests(unittest.TestCase):
    def test_android_map_assets_use_the_sdk_platform_identifier(self):
        targets = ET.parse(ROOT / "build" / "ProjectPrime.MapArtifacts.targets")
        target = targets.find(".//Target[@Name='PrimeIncludeMapArtifactsForAndroid']")
        self.assertIsNotNone(target)
        self.assertEqual(
            "'$(TargetPlatformIdentifier)' == 'Android' and "
            "'$(PrimeMapOwnsMapArtifacts)' == 'true'",
            target.attrib.get("Condition"),
        )
        asset = target.find(".//AndroidAsset")
        self.assertIsNotNone(asset)
        self.assertEqual(
            "Assets\\maps\\%(_PrimeAndroidMapArtifact.Filename)"
            "%(_PrimeAndroidMapArtifact.Extension)",
            asset.attrib.get("Link"),
        )

    def test_source_inputs_exclude_existing_generated_bundles(self):
        with tempfile.TemporaryDirectory(prefix="prime-map-cook-") as directory:
            source = Path(directory)
            (source / "room.json").write_text('{"name":"ROOM"}', encoding="utf-8")
            (source / "room.fpmap").write_bytes(b"old output")
            self.assertEqual([source / "room.json"], COOK.source_files(source))

    def test_manifest_requires_a_nonempty_existing_artifact_list(self):
        with tempfile.TemporaryDirectory(prefix="prime-map-cook-") as directory:
            manifest = Path(directory) / "cook-manifest.json"
            expected = {"format": 1, "fingerprint": "abc"}
            manifest.write_text('{"format":1,"fingerprint":"abc"}', encoding="utf-8")
            self.assertFalse(COOK.manifest_matches(manifest, expected))
            manifest.write_text(
                '{"format":1,"fingerprint":"abc","artifacts":["room.fpmap"]}',
                encoding="utf-8",
            )
            self.assertFalse(COOK.manifest_matches(manifest, expected))
            (Path(directory) / "room.fpmap").write_bytes(b"bundle")
            self.assertTrue(COOK.manifest_matches(manifest, expected))

    def test_run_cook_uses_an_isolated_source_snapshot(self):
        with tempfile.TemporaryDirectory(prefix="prime-map-cook-") as directory:
            root = Path(directory)
            source = root / "source"
            output = root / "output"
            source.mkdir()
            output.mkdir()
            (source / "room.json").write_text('{"name":"ROOM"}', encoding="utf-8")
            (source / "room.fpmap").write_bytes(b"stale")

            def fake_run(command, *, cwd, check):
                staging = Path(command[command.index("-mapbundle-output") + 1])
                self.assertNotEqual(source, Path(command[command.index("-mapdir") + 1]))
                self.assertFalse((Path(command[command.index("-mapdir") + 1]) / "room.fpmap").exists())
                (staging / "room.fpmap").write_bytes(b"fresh")

            with mock.patch.object(COOK.subprocess, "run", side_effect=fake_run):
                self.assertEqual(["room.fpmap"], COOK.run_cook(source, output, "Release"))
            self.assertEqual(b"stale", (source / "room.fpmap").read_bytes())
            self.assertEqual(b"fresh", (output / "room.fpmap").read_bytes())

    def test_failed_compiler_keeps_the_last_good_artifact_set(self):
        with tempfile.TemporaryDirectory(prefix="prime-map-cook-") as directory:
            root = Path(directory)
            source = root / "source"
            output = root / "output"
            source.mkdir()
            output.mkdir()
            (source / "room.json").write_text('{"name":"ROOM"}', encoding="utf-8")
            (output / "room.fpmap").write_bytes(b"last good")
            (output / "cook-manifest.json").write_text(
                '{"fingerprint":"last-good","artifacts":["room.fpmap"]}\n',
                encoding="utf-8",
            )

            def failed_run(command, *, cwd, check):
                raise COOK.subprocess.CalledProcessError(17, command)

            with mock.patch.object(COOK.subprocess, "run", side_effect=failed_run):
                with self.assertRaises(COOK.subprocess.CalledProcessError):
                    COOK.run_cook(source, output, "Release")
            self.assertEqual(b"last good", (output / "room.fpmap").read_bytes())
            self.assertEqual(
                '{"fingerprint":"last-good","artifacts":["room.fpmap"]}\n',
                (output / "cook-manifest.json").read_text(encoding="utf-8"),
            )

    def test_cook_rejects_an_output_path_inside_the_source_tree(self):
        with tempfile.TemporaryDirectory(prefix="prime-map-cook-") as directory:
            source = Path(directory) / "maps"
            source.mkdir()
            args = type("CookArgs", (), {
                "source": source,
                "output": source / "artifacts",
                "configuration": "Release",
                "compiler_version": "test",
                "schema_version": "1",
                "package_version": "test",
                "force": False,
            })()
            with self.assertRaises(SystemExit):
                COOK.cook(args)


if __name__ == "__main__":
    unittest.main()
