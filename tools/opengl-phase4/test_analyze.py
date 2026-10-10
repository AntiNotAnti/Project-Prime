"""Pure benchmark gate tests: no hardware and no proprietary game content."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).parent))
from analyze import ARMS, evaluate, read_samples


def line(prefix, entry):
    return prefix + json.dumps(entry) + "\n"


class GateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.logs = {}
        for name, mode in ARMS.items():
            path = self.root / f"{name}.log"
            lines = []
            for _ in range(4):
                lines.append(line("[glframe-json] ", {
                    "schema": 1, "samples": 240,
                    "vbo": mode[0], "bindingCache": mode[1],
                    "renderCpuP95Ms": 2, "renderCpuP99Ms": 2.5,
                    "presentWallP99Ms": 0.3,
                    "frameIntervalP95Ms": 8, "frameIntervalP99Ms": 8 if name == "baseline" else 7.6,
                    "simulationHz": 60,
                    "onePercentLowEstimateFps": 125 if name == "baseline" else 131,
                    "droppedSimulationSteps": 0, "stalls": 0}))
                lines.append(line("[glgpu-json] ", {
                    "schema": 1, "samples": 240, "worldGpuP95Ms": 1,
                    "worldGpuP99Ms": 1.5, "droppedQueries": 0}))
                lines.append(line("[glprofile-json] ", {
                    "schema": 1, "samples": 240, "worldCpuP95Ms": 1.8,
                    "worldCpuP99Ms": 2.0, "averageMaterialBatches": 20}))
            path.write_text("".join(lines))
            self.logs[name] = path
        self.visual = {"source": "native-game-on-device", "passed": True,
                       "cases": ["ice-hive-walls", "ice-hive-shadows",
                                 "glass-and-portals", "studio-and-hud", "android-gles"]}

    def test_benchmark_with_hardware_evidence_is_eligible(self):
        report = evaluate(self.logs, self.visual)
        self.assertTrue(report["arms"]["vbo"]["eligible"])
        self.assertFalse(report["automaticActivation"])

    def test_missing_visual_acceptance_blocks_activation(self):
        self.assertFalse(evaluate(self.logs, {"passed": True, "source": "synthetic"})["arms"]["combined"]["eligible"])

    def test_missing_gpu_samples_blocks_activation(self):
        x = self.logs["vbo"]
        x.write_text("\n".join(y for y in x.read_text().splitlines() if "[glgpu-json]" not in y))
        self.assertFalse(evaluate(self.logs, self.visual)["arms"]["vbo"]["eligible"])

    def test_bad_optimization_mode_fails(self):
        with self.assertRaisesRegex(ValueError, "claimed optimization flags"):
            read_samples(self.logs["vbo"], (False, False))

    def test_software_stalls_fail_closed(self):
        x = self.logs["baseline"]
        x.write_text(x.read_text().replace('"stalls": 0', '"stalls": 1'))
        with self.assertRaisesRegex(ValueError, "dropped simulation"):
            evaluate(self.logs, self.visual)

    def test_missing_warmup_window_fails_closed(self):
        x = self.logs["binding"]
        x.write_text("[glframe-json] {}")
        with self.assertRaises(ValueError):
            evaluate(self.logs, self.visual)


if __name__ == "__main__":
    unittest.main()
