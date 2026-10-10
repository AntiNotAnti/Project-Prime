"""Offline fixture comparator contract; no .NET, GPU or game files needed."""
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).parent))
from compare import EXPECTED, compare, load


class PixelSuiteTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.ref = self.root / "reference"
        self.cand = self.root / "candidate"
        for folder in (self.ref, self.cand):
            folder.mkdir()
            pixels = bytes([255, 0, 0, 255]) * (64 * 64)
            for filename in EXPECTED:
                (folder / filename).write_bytes(pixels)
            (folder / "evidence.json").write_text(json.dumps({
                "schema": 1, "width": 64, "height": 64,
                "driver": "fixture GL 2.1",
                "captures": {filename: hashlib.sha256(pixels).hexdigest().upper()
                             for filename in EXPECTED}}))

    def test_equal(self):
        self.assertTrue(compare(self.ref, self.cand)["passed"])

    def test_capture_difference_fails(self):
        filename = EXPECTED[2]
        pixels = bytearray((self.cand / filename).read_bytes())
        pixels[0] ^= 0xFF
        (self.cand / filename).write_bytes(pixels)
        meta = json.loads((self.cand / "evidence.json").read_text())
        meta["captures"][filename] = hashlib.sha256(pixels).hexdigest().upper()
        (self.cand / "evidence.json").write_text(json.dumps(meta))
        self.assertFalse(compare(self.ref, self.cand)["passed"])

    def test_corrupt_checksum_fails_closed(self):
        (self.cand / EXPECTED[0]).write_bytes(bytes(64 * 64 * 4))
        with self.assertRaisesRegex(ValueError, "SHA256 mismatch"):
            load(self.cand)

    def test_missing_capture_fails_closed(self):
        (self.cand / EXPECTED[1]).unlink()
        with self.assertRaises(OSError):
            load(self.cand)

    def test_frame_repetition_fails_closed(self):
        pixel = bytearray((self.cand / EXPECTED[1]).read_bytes())
        pixel[0] ^= 1
        (self.cand / EXPECTED[1]).write_bytes(pixel)
        meta = json.loads((self.cand / "evidence.json").read_text())
        meta["captures"][EXPECTED[1]] = hashlib.sha256(pixel).hexdigest().upper()
        (self.cand / "evidence.json").write_text(json.dumps(meta))
        with self.assertRaisesRegex(ValueError, "deterministic redraw"):
            load(self.cand)

    def test_cross_driver_comparison_refused(self):
        meta = json.loads((self.cand / "evidence.json").read_text())
        meta["driver"] = "other gpu"
        (self.cand / "evidence.json").write_text(json.dumps(meta))
        with self.assertRaisesRegex(ValueError, "driver mismatch"):
            compare(self.ref, self.cand)


if __name__ == "__main__":
    unittest.main()
