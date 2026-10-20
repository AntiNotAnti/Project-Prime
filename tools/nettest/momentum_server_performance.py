#!/usr/bin/env python3
"""Sequential fresh-process Native/Momentum eight-bot elapsed-time experiment.

Runs separate diagnostic verification, then >=2 ABBA cycles. Uses only the
operator's paths.txt; game data is neither copied nor downloaded. The 5% p95
initial gate fails closed. Workload distributions remain inspectable and need
review; passing this benchmark is not UDP, device, or full PR09 acceptance.
Tiered compilation, tiered PGO and ReadyToRun are explicitly disabled in every
fresh arm so virtual simulation warm-up is not mistaken for JIT wall-time warm-up.
"""
import argparse
import json
import math
import os
from pathlib import Path
import shutil
import statistics
import subprocess
import sys


def mean(rows, path):
    values = []
    for row in rows:
        value = row
        for key in path:
            value = value[key]
        values.append(value)
    return statistics.mean(values)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True, type=Path)
    parser.add_argument("--dll", required=True, type=Path)
    parser.add_argument("--assets", required=True, type=Path)
    parser.add_argument("--room", default="MP3 PROVING GROUND")
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--cycles", type=int, default=2)
    parser.add_argument("--seconds", type=float, default=60)
    parser.add_argument("--timeout", type=float, default=600)
    parser.add_argument("--commit", default="working-tree")
    parser.add_argument("--thread-cycles", action="store_true", help="Windows-only pinned simulation-thread cycle diagnostic; never changes wall gate")
    args = parser.parse_args()
    if args.cycles < 2 or args.cycles > 20:
        parser.error("use 2..20 ABBA cycles; one pair is not repeated evidence")
    if not math.isfinite(args.seconds) or args.seconds < 60 or args.seconds > 600:
        parser.error("seconds must cover 3600..36000 fixed simulation steps")
    dotnet, dll, assets, output = (p.resolve() for p in (args.dotnet, args.dll, args.assets, args.output))
    if not (assets / "paths.txt").is_file():
        parser.error("assets must contain an operator-supplied paths.txt")
    output.mkdir(parents=True, exist_ok=True)
    summary_path = output / "paired-summary.json"
    if summary_path.exists():
        parser.error("output already contains paired-summary.json; choose a new output directory")
    runs = []
    verification = []

    def run(name, model, phase):
        directory = output / name
        directory.mkdir(exist_ok=False)
        config = directory / "user-data"
        config.mkdir()
        shutil.copy2(assets / "paths.txt", config / "paths.txt")
        env = os.environ.copy()
        env["PROJECT_PRIME_USER_DATA"] = str(config)
        env["PRIME_BENCHMARK_COMMIT"] = args.commit
        if args.thread_cycles:
            env["PRIME_BENCHMARK_THREAD_CYCLES"] = "1"
        else:
            env.pop("PRIME_BENCHMARK_THREAD_CYCLES", None)
        env["DOTNET_TieredCompilation"] = "0"
        env["DOTNET_TieredPGO"] = "0"
        env["DOTNET_ReadyToRun"] = "0"
        env["PRIME_SHOT_PROOF"] = "shadow"
        env["PRIME_ACCEPTED_SHOTS"] = "1"
        # Bound instrumentation has a separate acceptance test. Do not let an
        # inherited evidence-output setting contaminate one timing experiment.
        for setting in ("PRIME_SERVER_PERF_OUTPUT", "PRIME_NET2_EVIDENCE",
                        "PRIME_NET2_EVIDENCE_ROW_LIMIT", "PRIME_MOVEMENT_VALIDATION_SHADOW",
                        "PRIME_TIMED_GRANT_PROOF", "PRIME_NET2_SOAK", "PRIME_NET_AI_SERVER_BOT",
                        "PRIME_NET2_DRAIN_STATUS", "PRIME_NET2_DRAIN_RELEASE", "PRIME_NET2_DRAIN_ROLE",
                        "PRIME_NET2_DRAIN_SECONDS"):
            env.pop(setting, None)
        result_path = directory / "results.json"
        command = [str(dotnet), str(dll), "--momentum-server-performance", str(config),
                   args.room, model, str(result_path), str(args.seconds), phase]
        with (directory / "run.txt").open("w", encoding="utf-8") as log:
            completed = subprocess.run(command, cwd=config, env=env, stdin=subprocess.DEVNULL,
                                       stdout=log, stderr=subprocess.STDOUT, timeout=args.timeout, check=False)
        if not result_path.is_file():
            raise RuntimeError(f"{name}: no JSON evidence, exit {completed.returncode}")
        row = json.loads(result_path.read_text(encoding="utf-8"))
        if completed.returncode or row.get("failure"):
            raise RuntimeError(f"{name}: failed, exit {completed.returncode}; see {directory / 'run.txt'}")
        if row.get("acceptedPerformanceEvidence") != (phase == "timing") or row.get("model") != model:
            raise RuntimeError(f"{name}: evidence scope/model does not match requested arm")
        row["artifact"] = str(result_path)
        print(f"{name}: {model} {phase}, p95={row['timing']['p95']:.6f} ms", flush=True)
        return row

    try:
        for model in ("Native", "Momentum"):
            verification.append(run("verify-" + model.lower(), model, "verify"))
        for cycle in range(args.cycles):
            for at, model in enumerate(("Native", "Momentum", "Momentum", "Native")):
                runs.append(run(f"cycle-{cycle + 1:02d}-{at + 1}-{model.lower()}", model, "timing"))
        native = [row for row in runs if row["model"] == "Native"]
        momentum = [row for row in runs if row["model"] == "Momentum"]
        native_p95 = statistics.median(row["timing"]["p95"] for row in native)
        momentum_p95 = statistics.median(row["timing"]["p95"] for row in momentum)
        if native_p95 <= 0:
            raise RuntimeError("Native p95 is not a positive usable baseline")
        increase = momentum_p95 / native_p95 - 1
        cycles = []
        for cycle in range(args.cycles):
            block = runs[cycle * 4:(cycle + 1) * 4]
            a = statistics.mean(row["timing"]["p95"] for row in (block[0], block[3]))
            b = statistics.mean(row["timing"]["p95"] for row in (block[1], block[2]))
            cycles.append({"cycle": cycle + 1, "nativeOuterP95Mean": a,
                           "momentumInnerP95Mean": b, "increase": b / a - 1})

        def aggregate(rows):
            return {"runs": len(rows), "p95Median": statistics.median(row["timing"]["p95"] for row in rows),
                    "p95Min": min(row["timing"]["p95"] for row in rows),
                    "p95Max": max(row["timing"]["p95"] for row in rows),
                    "p99Median": statistics.median(row["timing"]["p99"] for row in rows),
                    "worstFrame": max(row["timing"]["worst"] for row in rows),
                    "meanStepMean": mean(rows, ("timing", "mean")),
                    "stepBytesPerAlivePlayerStepMean": mean(rows, ("allocation", "bytesPerAlivePlayerStep")),
                    "alivePlayerStepsMean": mean(rows, ("allocation", "aliveSteps")),
                    "shotsMean": mean(rows, ("workload", "shotsFired")),
                    "slotWorkloads": [{"artifact": row["artifact"], "slots": row["workload"]["slots"],
                                       "aliveOccupancyHistogram": row["workload"]["aliveOccupancyHistogram"]} for row in rows]}
        cycle_diagnostic = None
        if args.thread_cycles:
            samples = [row.get("threadCycles") for row in runs]
            if all(sample is not None for sample in samples):
                for row in runs:
                    sample = row["threadCycles"]
                    if sample["p95"] <= 0 or len(sample["rawStepCycles"]) != row["scenario"]["steps"]:
                        raise RuntimeError("requested thread-cycle diagnostic has unusable samples")
                affinities = {(row["threadCycles"]["allowedAffinityMask"], row["threadCycles"]["selectedAffinityMask"],
                               row["threadCycles"]["startProcessor"], row["threadCycles"]["endProcessor"]) for row in runs}
                if len(affinities) != 1 or any(a[2] != a[3] for a in affinities):
                    raise RuntimeError("thread-cycle diagnostic requires the same pinned CPU across all arms")
                n = statistics.median(row["threadCycles"]["p95"] for row in native)
                m = statistics.median(row["threadCycles"]["p95"] for row in momentum)
                cycle_diagnostic = {"unit": "thread CPU execution cycles", "diagnosticOnly": True,
                    "nativeP95Median": n, "momentumP95Median": m, "p95RelativeIncrease": m / n - 1,
                    "scope": "pinned current simulation thread; no conversion to time; cross-model AI/world work differs; no new CPU acceptance gate"}
            elif any(sample is not None for sample in samples):
                raise RuntimeError("thread-cycle support differs between timing arms")
        summary = {"schema": 1, "scope": "isolated sequential actual eight-bot authority ABBA experiment; no transport/device acceptance",
                   "room": args.room, "secondsOfSimulationPerRun": args.seconds, "cycles": cycles,
                   "runtimeSettings": {"DOTNET_TieredCompilation": "0", "DOTNET_TieredPGO": "0", "DOTNET_ReadyToRun": "0",
                                       "PRIME_SHOT_PROOF": "shadow", "PRIME_ACCEPTED_SHOTS": "1"},
                   "verificationArtifacts": [row["artifact"] for row in verification],
                   "native": aggregate(native), "momentum": aggregate(momentum),
                   "p95RelativeIncrease": increase, "p95IncreaseLimit": .05,
                   "initialP95GatePassed": increase <= .05,
                   "workloadReviewRequired": True,
                   "threadCycleDiagnosticRequested": args.thread_cycles, "threadCycleDiagnostic": cycle_diagnostic,
                   "gateInterpretation": "above 5% stops for investigation; a passing elapsed-wall-time proxy does not certify CPU tails, workload equivalence or full PR09 acceptance",
                   "timingArtifacts": [row["artifact"] for row in runs]}
        summary_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
        print(f"p95 increase {increase:.2%}; initial 5% gate {'PASS' if increase <= .05 else 'FAIL'}; workload review required", flush=True)
        return 0 if increase <= .05 else 1
    except (OSError, RuntimeError, subprocess.TimeoutExpired, ValueError, KeyError) as error:
        summary_path.write_text(json.dumps({"failure": str(error), "initialP95GatePassed": False,
                                           "completedTimingArtifacts": [row["artifact"] for row in runs],
                                           "verificationArtifacts": [row["artifact"] for row in verification]}, indent=2) + "\n", encoding="utf-8")
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
