from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools/worker-soak/run-capacity-matrix.sh"


class WorkerSoakCapacityMatrixTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = SCRIPT.read_text()

    def test_matrix_has_explicit_rows_and_roster_variants(self):
        for row in (
            '"2:1:1:matrix"', '"4:1:1:matrix"', '"4:1:2:matrix"',
            '"8:1:2:matrix"', '"8:1:4:matrix"', '"16:1:4:matrix"',
            '"4:2:2:two-worker-scaling"',
        ):
            self.assertIn(row, self.source)
        for roster in ('"1:2:1"', '"2:1:2"', '"4:0:0"'):
            self.assertIn(roster, self.source)
        self.assertIn("seconds=180", self.source)

    def test_each_run_records_hashes_arguments_host_and_exit_status(self):
        for field in ('workerSha256', 'soakSha256', 'dataDirectory', 'osBuild',
                      'architecture', 'cpuModel', 'logicalCpuCount', 'memoryBytes',
                      'args', 'exitStatus'):
            self.assertIn(field, self.source)
        self.assertIn('set +e', self.source)
        self.assertIn('set -e', self.source)
        self.assertIn('matrix.jsonl', self.source)
        self.assertNotIn('statistics.mean', self.source.lower())


if __name__ == "__main__":
    unittest.main()
