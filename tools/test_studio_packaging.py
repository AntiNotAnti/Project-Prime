"""Paired portable release assembly and exact-version diagnostic regressions."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace

spec = importlib.util.spec_from_file_location("studio_pair", Path(__file__).with_name("studio-pair.py"))
pair = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pair)


class PairedPackagingTests(unittest.TestCase):
    def fixture(self, root, extension=""):
        game, studio = Path(root) / "game publish", Path(root) / "studio publish"
        game.mkdir(); studio.mkdir()
        (game / ("ProjectPrime" + extension)).write_bytes(b"game executable")
        (studio / ("ProjectPrimeStudio" + extension)).write_bytes(b"studio executable")
        for folder in game, studio: (folder / "shared-native").write_bytes(b"same native library")
        return game, studio

    def test_portable_pair_contains_both_exact_release_metadata(self):
        for extension in "", ".exe":
            with tempfile.TemporaryDirectory() as root:
                game, studio = self.fixture(root, extension)
                pair.merge(game, studio, "1.2.3")
                self.assertEqual(b"studio executable", (game / ("ProjectPrimeStudio" + extension)).read_bytes())
                self.assertEqual({"Version": 1, "GameVersion": "1.2.3", "StudioVersion": "1.2.3", "IpcVersion": 1},
                                 json.loads((game / pair.METADATA).read_text()))

    def test_conflicting_native_file_rejects_before_any_copy(self):
        with tempfile.TemporaryDirectory() as root:
            game, studio = self.fixture(root)
            (studio / "shared-native").write_bytes(b"different runtime")
            with self.assertRaisesRegex(ValueError, "differs"): pair.merge(game, studio, "1.2.3")
            self.assertEqual(b"same native library", (game / "shared-native").read_bytes())
            self.assertFalse((game / "ProjectPrimeStudio").exists())
            self.assertFalse((game / pair.METADATA).exists())

    def test_missing_studio_and_nested_publish_are_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            game, studio = self.fixture(root)
            (studio / "ProjectPrimeStudio").unlink()
            with self.assertRaisesRegex(ValueError, "missing"): pair.merge(game, studio, "1.2.3")
            with self.assertRaisesRegex(ValueError, "separate"): pair.merge(game, game / "studio", "1.2.3")

    def test_release_version_validation(self):
        with tempfile.TemporaryDirectory() as root:
            for invalid in ("local", "1.2", "1.2.3+wrong", "../1.2.3", "1" * 129 + ".2.3", "1.2.3é", "1.2.3\n"):
                with self.assertRaises(ValueError): pair.metadata(root, invalid)
        for label in ("1.2.3", "1.2.3+abc123", "dev-build_2"):
            self.assertTrue(pair.supported_version(label))
        for label in ("", "a" * 129, "1.2.3\n", "1.2.3é", "version label", "1.2.3/"):
            self.assertFalse(pair.supported_version(label))

    def test_smoke_requires_exact_engine_studio_and_current_protocol(self):
        def result(game="1.2.3", studio="1.2.3", linked="1.2.3", ipc=1):
            return [SimpleNamespace(stdout=json.dumps({"gameVersion": game, "studioIpcVersion": ipc})),
                    SimpleNamespace(stdout=json.dumps({"studioVersion": studio, "gameVersion": linked, "studioIpcVersion": ipc}))]
        for values in (result(game="1.2.3+different"), result(studio="1.2.4"), result(linked="1.2.2"), result(ipc=0)):
            with patch.object(pair.subprocess, "run", side_effect=values):
                with self.assertRaisesRegex(ValueError, "mismatch"): pair.smoke("game", "studio", "1.2.3")
        with patch.object(pair.subprocess, "run", side_effect=result()): pair.smoke("game", "studio", "1.2.3")


if __name__ == "__main__": unittest.main()
