import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest
import uuid


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools/worker-soak/analyze-capacity.py"
SPEC = importlib.util.spec_from_file_location("analyze_capacity", SCRIPT)
ANALYZER = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(ANALYZER)


def _jsonl(path: Path, records):
    path.write_text("".join(json.dumps(record, sort_keys=True) + "\n" for record in records), encoding="utf-8")


class CapacityAnalyzerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="fruity-capacity-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self._build_fixture()

    def _build_fixture(self):
        provenance = {
            "kind": "provenance",
            "host": "fixture-host",
            "dotnet": "9.0.0",
            "os": "fixture-os",
            "osBuild": "fixture-build",
            "architecture": "arm64",
            "cpuModel": "fixture-cpu",
            "logicalCpuCount": 4,
            "memoryBytes": 16 * 1024 * 1024,
            "workerAssembly": "/fixture/worker.dll",
            "workerSha256": "a" * 64,
            "soakAssembly": "/fixture/soak.dll",
            "soakSha256": "b" * 64,
            "workerRuntimeManifest": {"root": "/fixture", "fileCount": 1, "sha256": "c" * 64},
            "soakRuntimeManifest": {"root": "/fixture", "fileCount": 1, "sha256": "d" * 64},
            "dataDirectory": "/fixture/data",
            "dataDirectoryManifest": {"root": "/fixture/data", "fileCount": 1, "sha256": "e" * 64},
            "seconds": 10,
        }
        rows = []
        for shape_index, (matches, workers, lanes, family) in enumerate(ANALYZER.EXPECTED_SHAPES):
            for players, bots, observers in ANALYZER.EXPECTED_ROSTERS:
                run_id = f"{shape_index:03d}-{family}-{players}-{bots}-{observers}"
                rows.append({
                    "kind": "run",
                    "id": run_id,
                    "family": family,
                    "totalMatches": matches * workers,
                    "workers": workers,
                    "matchesPerWorker": matches,
                    "lanes": lanes,
                    "roster": {"players": players, "bots": bots, "observers": observers},
                    "args": [],
                    "exitStatus": 0,
                })
        _jsonl(self.root / "matrix.jsonl", [{"kind": "matrix", "format": 1}, provenance, *rows])
        for row_index, row in enumerate(rows):
            run = self.root / row["id"]
            run.mkdir()
            (run / "receipts").mkdir()
            (run / "outbox").mkdir()
            (run / "outbox" / ".owner").write_text("", encoding="utf-8")
            for worker_index in range(row["workers"]):
                worker_dir = run / f"worker-{worker_index}"
                (worker_dir / "reports").mkdir(parents=True)
                (worker_dir / "replays").mkdir()

            match_id = str(uuid.UUID(int=row_index + 1))
            worker_ids = [str(uuid.UUID(int=1000 + row_index * 10 + index + 1))
                          for index in range(row["workers"])]
            incarnation_ids = [str(uuid.UUID(int=2000 + row_index * 10 + index + 1))
                               for index in range(row["workers"])]

            def sample(elapsed, counter):
                workers = []
                for worker_index in range(row["workers"]):
                    lanes = []
                    for lane_id in range(row["lanes"]):
                        lanes.append({
                            "laneId": lane_id,
                            "Ticks": 100 + counter,
                            "CatchUpTicks": 2 + counter,
                            "DroppedTicks": 1 + counter,
                            "P50Milliseconds": 1,
                            "P95Milliseconds": 3 + counter * 5,
                            "P99Milliseconds": 4 + counter * 5,
                            "MaxMilliseconds": 5 + counter * 5,
                        })
                    workers.append({
                        "WorkerId": {"Value": worker_ids[worker_index]},
                        "Incarnation": incarnation_ids[worker_index],
                        "Health": {
                            "Status": "Ready",
                            "UptimeMilliseconds": elapsed * 1000,
                            "TickP99Milliseconds": 4 + counter,
                            "WorkingSetBytes": 1000 + counter * 100,
                            "Diagnostics": {
                                "Lanes": lanes,
                                "CpuPercent": 10 + counter,
                                "ManagedHeapBytes": 2000 + counter * 100,
                                "Gen0Collections": counter,
                                "Gen1Collections": 0,
                                "Gen2Collections": 0,
                                "PacketsReceived": 10 + counter * 10,
                                "PacketsSent": 20 + counter * 10,
                                "BytesReceived": 100 + counter * 100,
                                "BytesSent": 200 + counter * 100,
                                "QueueDrops": counter,
                                "PacketsRejected": counter + 1,
                            },
                        },
                    })
                return workers

            replay = {
                "kind": "artifact",
                "MatchId": {"Value": match_id},
                "name": match_id + ".fpdemo",
                "Length": 50,
                "hash": "f" * 64,
                "validation": {"RecordCount": 2, "CheckpointCount": 1, "FirstFrame": 0, "LastFrame": 1},
            }
            telemetry = {
                "kind": "artifact",
                "MatchId": {"Value": match_id},
                "name": match_id + ".telemetry.json",
                "Length": 60,
                "hash": "e" * 64,
                "validation": {"Events": 3, "DroppedEvents": 0},
            }
            summary = {
                "kind": "complete",
                "scenario": {
                    "Seconds": 10,
                    "MatchesPerWorker": row["matchesPerWorker"],
                    "Lanes": row["lanes"],
                    "Workers": row["workers"],
                    "Roster": row["roster"],
                },
                "requestedSeconds": 10,
                "workloadSeconds": 10,
                "created": 1,
                "completed": 1,
                "interrupted": 0,
                "failures": 0,
                "ingested": 1,
                "backend": {
                    "PersistedReports": 1,
                    "Attempts": 1,
                    "OutageResponses": 0,
                    "PayloadHashMismatches": 0,
                    "Reports": [{"MatchId": match_id, "PayloadHash": "9" * 64, "Bytes": 10}],
                    "DatabasePath": str(run / "soak.sqlite"),
                },
                "outbox": {
                    "Ready": True,
                    "ReservedReports": 0,
                    "ReservedBytes": 0,
                    "Quarantined": 0,
                    "DurablePending": 0,
                    "QueuedPending": 0,
                    "OldestAgeSeconds": 0,
                    "LastError": None,
                },
                "terminals": {"completed": 1},
                "activeAtEnd": 0,
                "passed": True,
            }
            events = [
                {"kind": "start", "status": "running"},
                {"kind": "lobby_started", "MatchId": {"Value": match_id}},
                {"kind": "sample", "elapsed": 0, "workers": sample(0, 0)},
                {"kind": "sample", "elapsed": 5, "workers": sample(5, 1)},
                {"kind": "sample", "elapsed": 11, "workers": sample(11, 2)},
                replay,
                telemetry,
                summary,
            ]
            (run / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
            _jsonl(run / "metrics-00.jsonl", events[:4])
            _jsonl(run / "metrics-01.jsonl", events[4:])
            trends = []
            for elapsed, cpu_seconds in ((0, 0), (5, 1), (11, 2)):
                trends.append({
                    "elapsed": elapsed,
                    "processes": [
                        {
                            "role": "Worker",
                            "workerId": worker_ids[index].replace("-", ""),
                            "incarnation": incarnation_ids[index].replace("-", ""),
                            "process": {"CpuSeconds": cpu_seconds, "WorkingSet64": 1000 + cpu_seconds},
                        }
                        for index in range(row["workers"])
                    ],
                })
            _jsonl(run / "trend.jsonl", trends)
            _jsonl(run / "critical-events.jsonl", [
                {"kind": "start", "component": "Harness+Node+Backend"},
                {"kind": "terminal", "matchId": match_id.replace("-", ""),
                 "workerId": worker_ids[0].replace("-", ""),
                 "incarnation": incarnation_ids[0].replace("-", ""), "status": "completed"},
            ])
            (run / "receipts" / (match_id + ".json")).write_text(json.dumps({
                "MatchId": match_id,
                "Hash": "9" * 64,
                "Worker": {"Value": worker_ids[0]},
                "Incarnation": incarnation_ids[0],
            }), encoding="utf-8")

    def test_ordering_and_missing_rows_are_rejected(self):
        records = (self.root / "matrix.jsonl").read_text(encoding="utf-8").splitlines()
        records[2], records[3] = records[3], records[2]
        (self.root / "matrix.jsonl").write_text("\n".join(records) + "\n", encoding="utf-8")
        with self.assertRaises(ANALYZER.AnalysisError):
            ANALYZER.analyze_capacity(self.root)

        shutil.rmtree(self.root)
        self.root.mkdir()
        self._build_fixture()
        shutil.rmtree(self.root / "006-two-worker-scaling-4-0-0")
        with self.assertRaises(ANALYZER.AnalysisError):
            ANALYZER.analyze_capacity(self.root)

    def test_multi_worker_grouping_and_counter_deltas_are_per_incarnation(self):
        report = ANALYZER.analyze_capacity(self.root)
        row = report["shapes"][-1]["rosters"][0]
        self.assertTrue(row["valid"])
        self.assertEqual(len(row["observed"]["workerGroups"]), 4)
        self.assertEqual(len(row["observed"]["counterDeltasByWorkerIncarnation"]), 2)
        for worker in row["observed"]["workers"]:
            self.assertEqual(worker["network"]["activeWindow"]["delta"]["packetsReceived"], 10)
            self.assertEqual(worker["network"]["activeWindow"]["delta"]["queueDrops"], 1)
            self.assertEqual(worker["network"]["activeWindow"]["delta"]["packetsRejected"], 1)
            self.assertEqual(worker["scheduler"]["activeWindow"]["delta"]["ticks"], 2)
            self.assertEqual(worker["gc"]["activeWindow"]["delta"]["gen0Collections"], 1)

    def test_percentiles_keep_worst_observation_and_filter_drain_samples(self):
        report = ANALYZER.analyze_capacity(self.root)
        row = report["shapes"][0]["rosters"][0]
        self.assertEqual(row["observed"]["activeSampleCount"], 2)
        lane = row["observed"]["workerGroups"][0]
        self.assertEqual(lane["latencyMilliseconds"], {
            "p50Worst": 1, "p95Worst": 8, "p99Worst": 9, "maxWorst": 10,
        })
        self.assertEqual(row["observed"]["workers"][0]["network"]["activeWindow"]["durationSeconds"], 5)
        self.assertEqual(row["observed"]["artifacts"]["replay"]["bytes"], 50)
        self.assertEqual(row["observed"]["artifacts"]["telemetry"]["events"]["events"], 3)

    def test_every_metrics_segment_is_parsed(self):
        segment = self.root / "000-matrix-1-2-1" / "metrics-01.jsonl"
        segment.write_text(segment.read_text(encoding="utf-8") + "not-json\n", encoding="utf-8")
        with self.assertRaises(ANALYZER.AnalysisError):
            ANALYZER.analyze_capacity(self.root)


if __name__ == "__main__":
    unittest.main()
