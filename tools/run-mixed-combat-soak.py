#!/usr/bin/env python3
"""Eight real UDP peers and a separate asset-backed server process.

Test conditions: fixed weapon loadouts, infinite ammunition, normal input,
collision, damage, deaths and respawns. No rendered-client evidence. Compare
identical scheduling/impairment parameters and retain all raw workload counts;
exact ON/OFF script equality must be checked by the deterministic companion test.
"""
import argparse
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import time


def port():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def run(args, label, binary):
    work = args.output / label
    work.mkdir()
    children, streams = [], []

    def start(command, name, marker, env=None):
        path = work / (name + ".log")
        stream = path.open("w")
        streams.append(stream)
        child = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL, env=env)
        children.append(child)
        deadline = time.monotonic() + 20
        while marker and marker not in path.read_text(errors="replace"):
            if child.poll() is not None or time.monotonic() >= deadline:
                raise RuntimeError("Readiness failed: " + str(path))
            time.sleep(.05)
        return child

    try:
        server_port = port()
        server = start([args.dotnet, str(binary), "--mixed-soak-server", str(args.data), str(server_port),
                        str(args.seconds), str(work / "server.json"), label, str(args.seed)], "server", "listening on UDP")
        destinations = []
        for i in range(8):
            if not (args.rtt or args.jitter or args.loss):
                destinations.append(server_port)
                continue
            proxy = port()
            start([sys.executable, str(Path(__file__).with_name("udp-lag.py")), str(proxy), "127.0.0.1",
                   str(server_port), str(args.rtt / 2), str(args.jitter), str(args.loss)],
                  f"proxy-{i}", "[lag] :", dict(os.environ, MPHREAD_LAG_SEED=str(args.seed + i)))
            destinations.append(proxy)
        client = start([args.dotnet, str(binary), "--mixed-soak-clients", str(args.seconds),
                        ",".join(map(str, destinations)), str(work / "clients.json"), str(work / "server.json")], "clients", None)
        client_code = client.wait(timeout=args.seconds + 45)
        server_code = server.wait(timeout=25)
        server_report = json.loads((work / "server.json").read_text())
        client_report = json.loads((work / "clients.json").read_text())
        result = {"label": label, "passed": server_code == client_code == 0 and server_report["passed"] and client_report["passed"],
                  "server": server_report, "clients": client_report}
        print(label, "PASS" if result["passed"] else "FAIL", server_report["rootShots"], flush=True)
        return result
    finally:
        for child in reversed(children):
            if child.poll() is None:
                child.terminate()
        for child in reversed(children):
            try:
                child.wait(timeout=5)
            except subprocess.TimeoutExpired:
                child.kill()
                child.wait()
        for stream in streams:
            stream.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--nettest", type=Path, required=True)
    parser.add_argument("--modes", nargs="+", choices=["on", "trace-only", "off"], default=["on", "off"],
                        help="Immutable server instance settings; trace-only isolates projectile catch-up cost")
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--seconds", type=int, default=300)
    parser.add_argument("--rtt", type=float, default=100)
    parser.add_argument("--jitter", type=float, default=20)
    parser.add_argument("--loss", type=float, default=2)
    parser.add_argument("--seed", type=int, default=20260906)
    args = parser.parse_args()
    if not 0 <= args.seed <= 0xffffffff or not 10 <= args.seconds <= 300 or not 0 <= args.loss <= 10 or not 0 <= args.rtt <= 1000 or not 0 <= args.jitter <= 500:
        parser.error("Invalid duration or impairment parameters")
    args.output, args.data, args.nettest = args.output.resolve(), args.data.resolve(), args.nettest.resolve()
    args.output.mkdir(parents=True, exist_ok=False)
    if len(args.modes) != len(set(args.modes)):
        parser.error("--modes must be unique")
    results = [run(args, mode, args.nettest) for mode in args.modes]
    summary = {"schedule": "two clients per weapon; PowerBeam/Imperialist tap, charged affinity missile, continuous ShockCoil",
               "seconds": args.seconds, "rttMs": args.rtt, "jitterMs": args.jitter, "lossPercent": args.loss, "seed": args.seed,
               "evidence": "real UDP/headless simulation; controlled loadouts/infinite ammo; no rendering", "runs": results}
    summary["comparisons"] = []
    reference = results[0]["server"]
    for result in results[1:]:
        baseline = result["server"]
        summary["comparisons"].append({"reference": results[0]["label"], "baseline": result["label"],
            "exactRootShotCountsEqual": reference["rootShots"] == baseline["rootShots"],
            "rootShotCountDelta": [a-b for a, b in zip(reference["rootShots"], baseline["rootShots"])],
            "cpuSecondsDelta": reference["cpuSeconds"] - baseline["cpuSeconds"],
            "allocatedBytesPerTickDelta": reference["allocatedBytesPerTick"] - baseline["allocatedBytesPerTick"],
            "interpretation": "Observed process cost under the same scheduling parameters; differing combat outcomes prevent exact workload equivalence."})
    (args.output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    return 0 if all(result["passed"] for result in results) else 1


if __name__ == "__main__":
    sys.exit(main())
