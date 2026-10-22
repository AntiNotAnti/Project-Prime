"""Negative-only physical evidence tests. No synthetic sample may claim hardware acceptance."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).parent))
from validate import EvidenceError, evaluate, _read_log

class PhysicalEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.base = self.root / "before.log"
        self.enabled = self.root / "after.log"
        self.data = {
            "source": "synthetic", "emulator": True,
            "reviewed": False, "reviewed_by": "",
            "build_sha": "0" * 40, "binary_sha256": "0" * 64,
            "device": {"platform": "android-gles", "gpu": "fixture",
                       "driver": "fixture", "model": "fixture", "refresh_hz": "120"},
            "assembly_mvid": "fixture",
            "visual_cases": {}
        }

    def _log(self, path, count=4, drift=False, blend=False):
        context = {"room": "fixture", "width": 1280, "height": 720,
                   "cap": 120, "renderer": "fixture", "glVersion": "4.6",
                   "assemblyMvid": "fixture", "shadows": "On", "renderScale": 100}
        with path.open("w") as f:
            for i in range(count):
                row = {"schema": 1, "samples": 240, "context": context,
                       "frameIntervalP95Ms": 7, "frameIntervalP99Ms": 8,
                       "renderCpuP99Ms": 1.5, "onePercentLowEstimateFps": 125,
                       "simulationHz": 58 if drift else 60,
                       "droppedSimulationSteps": 0, "stalls": 0,
                       "animationBlend": blend}
                f.write("[glframe-json] " + json.dumps(row) + "\n")
                f.write("[glgpu-json] " + json.dumps({
                    "schema": 1, "samples": 240, "worldGpuP99Ms": 1.5,
                    "droppedQueries": 0}) + "\n")

    def test_does_not_accept_emulator_or_synthetic_claim(self):
        self._log(self.base)
        self._log(self.enabled, blend=True)
        with self.assertRaisesRegex(EvidenceError, "synthetic/virtual"):
            evaluate(self.data, self.base, self.enabled, self.root)

    def test_requires_attested_independent_native_visual_review(self):
        self._log(self.base)
        self._log(self.enabled, blend=True)
        self.data["source"] = "native-game-on-physical-device"
        self.data["emulator"] = False
        with self.assertRaisesRegex(EvidenceError, "human visual review"):
            evaluate(self.data, self.base, self.enabled, self.root)

    def test_android_gles_requires_native_profiler(self):
        self._log(self.base)
        self._log(self.enabled, blend=True)
        self.data.update(source="native-game-on-physical-device",
                         emulator=False, reviewed=True, reviewed_by="fixture")
        with self.assertRaisesRegex(EvidenceError, "Android GLES profiler is not yet instrumented"):
            evaluate(self.data, self.base, self.enabled, self.root)

    def test_requires_all_hunter_boss_map_and_replay_cases(self):
        self._log(self.base)
        self._log(self.enabled, blend=True)
        self.data.update(source="native-game-on-physical-device",
                         emulator=False, reviewed=True, reviewed_by="fixture")
        # Android deliberately stops at the unsupported profiler gate before
        # visual-case validation. Select an instrumented desktop platform so
        # this test actually reaches the missing-capture check.
        self.data["device"]["platform"] = "windows-opengl"
        with self.assertRaisesRegex(EvidenceError, "required Hunter"):
            evaluate(self.data, self.base, self.enabled, self.root)

    def test_missing_windows_fail_closed(self):
        self._log(self.base, count=3)
        with self.assertRaisesRegex(EvidenceError, "four 240-frame"):
            _read_log(self.base)

    def test_noncanonical_simulation_drift_fails(self):
        self._log(self.base, drift=True)
        with self.assertRaisesRegex(EvidenceError, "60 Hz"):
            _read_log(self.base)

    def test_gl_error_fails(self):
        self._log(self.base)
        with self.base.open("a") as log:
            log.write("GL error 1282\n")
        with self.assertRaisesRegex(EvidenceError, "GL error"):
            _read_log(self.base)

    def test_valid_synthetic_frame_stream_is_only_parsed_not_accepted(self):
        self._log(self.base)
        context, frames, gpu = _read_log(self.base)
        self.assertEqual(len(context), 9)
        self.assertEqual(len(frames), 3)
        self.assertEqual(len(gpu), 3)

if __name__ == "__main__":
    unittest.main()
