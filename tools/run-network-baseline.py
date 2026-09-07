#!/usr/bin/env python3
"""Run authoritative connection or gameplay checks through the UDP fault injector.

The default uses ServerNetwork and synthetic server-owned states without game
assets. It cannot establish movement/combat correctness. --simulation uses the
real authoritative game server with operator-provided extracted content.
All servers are local and unlisted; only child processes started here are stopped.
"""
import argparse
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import time


SCENARIOS = {
    "lan": (0, 0, 0),
    "good": (50, 10, 1),
    "normal": (100, 20, 2),
    "poor": (150, 30, 3),
    "extreme": (250, 50, 3),
}


def available_port():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def wait_ready(process, log, text):
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError(f"Process exited before ready: {log}")
        if text in log.read_text(errors="replace"):
            return
        time.sleep(0.05)
    raise RuntimeError(f"Process did not become ready: {log}")


def run_case(args, scenario, count, lines):
    work = args.output / f"{scenario}-{count}"
    work.mkdir(parents=True, exist_ok=False)
    processes, logs = [], []

    def start(command, name, ready, env=None):
        log = work / f"{name}.log"
        stream = log.open("w")
        logs.append(stream)
        process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT,
                                   stdin=subprocess.DEVNULL, env=env)
        processes.append(process)
        wait_ready(process, log, ready)
        return process

    try:
        port = available_port()
        if args.simulation:
            command = [args.dotnet, str(args.server), "-server", "-data", str(args.data),
                       "-dataversion", args.dataversion, "-nomaster", "-noupdate", "-port", str(port)]
        else:
            command = [args.dotnet, str(args.nettest), "--connection-server", str(port)]
        start(command, "server", "listening on UDP")
        destinations = []
        for index, (rtt, jitter, loss) in enumerate(lines):
            if rtt == jitter == loss == 0:
                destinations.append(port)
                continue
            proxy = available_port()
            env = dict(os.environ, MPHREAD_LAG_SEED=str(args.seed + index))
            start([sys.executable, str(Path(__file__).with_name("udp-lag.py")),
                   str(proxy), "127.0.0.1", str(port), str(rtt / 2), str(jitter), str(loss)],
                  f"line-{index}", "[lag] :", env)
            destinations.append(proxy)
        result = subprocess.run(
            [args.dotnet, str(args.nettest), "--simulation" if args.simulation else "--baseline", str(args.seconds),
             ",".join(map(str, destinations))], text=True, capture_output=True,
            timeout=args.seconds + 30, check=False)
        (work / "clients.log").write_text(result.stdout + result.stderr)
        if args.simulation:
            rows = [line for line in result.stdout.splitlines() if line.startswith("SIMCHECK ")]
            passed = result.returncode == 0 and len(rows) == count and all(line.endswith("result=PASS") for line in rows)
        else:
            rows = [json.loads(line) for line in result.stdout.splitlines() if line.startswith("{")]
            passed = result.returncode == 0 and len(rows) == count and all(row["healthy"] for row in rows)
        report = {"scenario": scenario, "players": count, "seconds": args.seconds,
                  "seed": args.seed, "impairments": lines, "passed": passed,
                  "evidence": "server gameplay with socket clients; no rendering" if args.simulation
                  else "authoritative ServerNetwork with synthetic server states; no gameplay", "clients": rows}
        (work / "result.json").write_text(json.dumps(report, indent=2) + "\n")
        print(f"{'PASS' if passed else 'FAIL'} {scenario}: {count} clients ({work})", flush=True)
        return report
    finally:
        for process in reversed(processes):
            if process.poll() is None:
                process.terminate()
        for process in reversed(processes):
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
        for stream in logs:
            stream.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--server", type=Path, help="Authoritative game binary, required for --simulation")
    parser.add_argument("--nettest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--seconds", type=int, default=20)
    parser.add_argument("--seed", type=int, default=20260906)
    parser.add_argument("--simulation", action="store_true", help="Run real server gameplay instead of synthetic connection traffic")
    parser.add_argument("--data", type=Path, help="Extracted game directory, required for --simulation")
    parser.add_argument("--dataversion", default="AMHE1")
    parser.add_argument("--scenarios", nargs="+", choices=[*SCENARIOS, "asymmetric"],
                        default=[*SCENARIOS, "asymmetric"])
    parser.add_argument("--players", nargs="+", type=int, choices=[2, 4, 8], default=[2, 4, 8])
    args = parser.parse_args()
    if not 5 <= args.seconds <= 300:
        parser.error("--seconds must be between 5 and 300")
    if args.simulation and (args.server is None or args.data is None or args.seconds < 10):
        parser.error("--simulation requires --server, --data and at least 10 seconds")
    if args.data is not None:
        args.data = args.data.resolve()
    if args.server is not None:
        args.server = args.server.resolve()
    args.nettest, args.output = (p.resolve() for p in (args.nettest, args.output))
    args.output.mkdir(parents=True, exist_ok=False)
    reports = []
    for scenario in args.scenarios:
        if scenario == "asymmetric":
            reports.append(run_case(args, scenario, 4, [(rtt, 0, 0) for rtt in (20, 60, 120, 200)]))
        else:
            for count in args.players:
                reports.append(run_case(args, scenario, count, [SCENARIOS[scenario]] * count))
    (args.output / "summary.json").write_text(json.dumps(reports, indent=2) + "\n")
    return 0 if all(report["passed"] for report in reports) else 1


if __name__ == "__main__":
    sys.exit(main())
