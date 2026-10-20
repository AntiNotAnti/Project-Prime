#!/usr/bin/env python3
"""Benchmark Project Prime dedicated server processes on Linux.

Active mode (the default) launches real headless UDP clients and refuses to
call a sleeping server an active 60 Hz match. Idle mode is an explicit control.
The harness never downloads data; local client CPU competes with server CPU,
so measured capacity is a *co-located load test*, not a remote-client claim.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import tempfile
import time

STEP_RE = re.compile(r"(\d+) step\(s\), ([\d.]+) ms mean, ([\d.]+) ms p95, "
                     r"([\d.]+) ms p99, ([\d.]+) ms worst, (\d+) allocated bytes, "
                     r"(\d+) overrun, (\d+) dropped, (\d+) stall\(s\)"
                     r"(?:, (\d+) FAILED)?")


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as file:
        for block in iter(lambda: file.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def read_proc(pid):
    try:
        stat = Path(f"/proc/{pid}/stat").read_text().split()
        status = Path(f"/proc/{pid}/status").read_text()
        rss = re.search(r"^VmRSS:\s+(\d+)\s+kB", status, re.M)
        return int(stat[13]) + int(stat[14]), int(rss.group(1)) if rss else 0
    except (OSError, ValueError, IndexError):
        return None


def summarize(values):
    if not values:
        return dict(count=0, mean=None, maximum=None)
    return dict(count=len(values), mean=sum(values) / len(values), maximum=max(values))


def arm(args, count, root):
    folder = root / f"{count}-servers"
    folder.mkdir(parents=True, exist_ok=True)
    rotation = folder / "rotation.txt"
    rotation.write_text(f"{args.room} | Battle | 0 | 999\n")
    env = dict(os.environ)
    processes, logs, clients, client_logs = [], [], [], []
    try:
        for i in range(count):
            log = open(folder / f"server-{i}.log", "w", encoding="utf-8")
            logs.append(log)
            cmd = [str(args.binary), "-server", "-port", str(args.base_port + i),
                   "-players", "8", "-rotation", str(rotation), "-nomaster",
                   "-noupdate", "-noautoupdate", "-noserverreplays", "-netdebug"]
            processes.append(subprocess.Popen(cmd, cwd=args.workdir, env=env,
                stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT))
        deadline = time.monotonic() + args.startup_timeout
        while time.monotonic() < deadline:
            if any(p.poll() is not None for p in processes):
                raise RuntimeError("a server exited during startup; inspect arm logs")
            ready = 0
            for path in folder.glob("server-*.log"):
                if "listening on UDP" in path.read_text(errors="replace"):
                    ready += 1
            if ready == count:
                break
            time.sleep(.1)
        else:
            raise RuntimeError("server startup deadline exceeded")

        if args.mode == "active":
            # Real, independent clients must join each isolated server. A
            # continuous server otherwise advertises itself while _sim is null.
            client_binary = args.client_binary or args.binary
            for i in range(count):
                for player in range(args.clients_per_match):
                    log = open(folder / f"client-{i}-{player}.log", "w", encoding="utf-8")
                    client_logs.append(log)
                    command = [str(client_binary), "-netcheck", "127.0.0.1",
                               "-port", str(args.base_port + i),
                               "-name", f"capacity_{i}_{player}", "-hunter", "Samus",
                               "-seconds", str(args.seconds + args.join_timeout
                                               + args.warmup_seconds + 60),
                               "-nographics", "-ai-net-client", "2",
                               "-ai-seed", str(1000 + i * 16 + player),
                               "-noupdate", "-noautoupdate"]
                    clients.append(subprocess.Popen(
                        command, cwd=args.client_workdir or args.workdir, env=env,
                        stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT))
            join_deadline = time.monotonic() + args.join_timeout
            while time.monotonic() < join_deadline:
                if any(p.poll() is not None for p in processes + clients):
                    raise RuntimeError("server/client exited before admission; inspect arm logs")
                admitted = [len(re.findall(r"joined as slot",
                              (folder / f"server-{i}.log").read_text(errors="replace")))
                            for i in range(count)]
                if all(n >= args.clients_per_match for n in admitted):
                    break
                time.sleep(.25)
            else:
                raise RuntimeError("real UDP client admissions did not complete; inspect arm logs")
            time.sleep(args.warmup_seconds)
        ticks = os.sysconf(os.sysconf_names["SC_CLK_TCK"])
        samples = [[] for _ in processes]
        rss = [[] for _ in processes]
        previous = [read_proc(p.pid) for p in processes]
        previous_at = time.monotonic()
        end = previous_at + args.seconds
        while time.monotonic() < end:
            if any(p.poll() is not None for p in processes + clients):
                raise RuntimeError("a measured server/client exited; inspect arm logs")
            time.sleep(args.sample_interval)
            now = time.monotonic()
            elapsed = max(.001, now - previous_at)
            for i, p in enumerate(processes):
                current = read_proc(p.pid)
                if current is None or previous[i] is None:
                    continue
                cpu = max(0, current[0] - previous[i][0]) / ticks / elapsed * 100
                samples[i].append(cpu)
                rss[i].append(current[1] / 1024.0)
                previous[i] = current
            previous_at = now

        per_process = []
        for i, p in enumerate(processes):
            log_text = (folder / f"server-{i}.log").read_text(errors="replace")
            steps = STEP_RE.findall(log_text)
            per_process.append(dict(index=i, pid=p.pid, cpuPercent=summarize(samples[i]),
                rssMiB=summarize(rss[i]), lastServerStep=(dict(
                    steps=int(steps[-1][0]), meanMilliseconds=float(steps[-1][1]),
                    p95Milliseconds=float(steps[-1][2]), p99Milliseconds=float(steps[-1][3]),
                    worstMilliseconds=float(steps[-1][4]), allocatedBytes=int(steps[-1][5]),
                    overruns=int(steps[-1][6]), droppedTicks=int(steps[-1][7]),
                    stalls=int(steps[-1][8]),
                    stepFailures=int(steps[-1][9] or 0)) if steps else None)))
        aggregate_cpu = [sum(row) for row in zip(*samples)] if samples and all(samples) else []
        aggregate_rss = [sum(row) for row in zip(*rss)] if rss and all(rss) else []
        # A successful client handshake is not proof of simulation. Each
        # server must also publish an actual nonzero authoritative step count.
        verified = args.mode == "active" and all(
            p["lastServerStep"] is not None and p["lastServerStep"]["steps"] >= 60
            for p in per_process)
        healthy = verified and all(
            p["lastServerStep"]["droppedTicks"] == 0
            and p["lastServerStep"]["stepFailures"] == 0
            and p["lastServerStep"]["p99Milliseconds"] <= args.max_p99_ms
            for p in per_process)
        result = dict(instances=count, seconds=args.seconds, mode=args.mode,
            clientsPerMatch=args.clients_per_match if args.mode == "active" else 0,
            activeSimulationVerified=verified, healthySimulation=healthy,
            stepP99BudgetMilliseconds=args.max_p99_ms,
            serverBinarySha256=args.server_binary_hash,
            clientBinarySha256=args.client_binary_hash if args.mode == "active" else None,
            colocatedClientLoad=args.mode == "active",
            aggregateCpuPercent=summarize(aggregate_cpu),
            aggregateRssMiB=summarize(aggregate_rss), processes=per_process,
            host=dict(cpuCount=os.cpu_count(), platform=os.uname().sysname + " " + os.uname().release))
        (folder / "summary.json").write_text(json.dumps(result, indent=2) + "\n")
        print(json.dumps(result), flush=True)
        if args.mode == "active" and not verified:
            raise RuntimeError("active simulation not verified: missing step evidence; "
                               "increase --seconds or inspect server logs")
        if args.mode == "active" and args.require_healthy and not healthy:
            raise RuntimeError("simulated authority exceeded p99/dropped-step/failure guardrail; "
                               "see per-arm summary and server logs")
        return result
    finally:
        for p in clients:
            if p.poll() is None:
                p.send_signal(signal.SIGINT)
        for p in clients:
            try: p.wait(timeout=8)
            except subprocess.TimeoutExpired:
                p.kill(); p.wait()
        for log in client_logs:
            log.close()
        for p in processes:
            if p.poll() is None:
                p.send_signal(signal.SIGINT)
        for p in processes:
            try: p.wait(timeout=8)
            except subprocess.TimeoutExpired:
                p.kill(); p.wait()
        for log in logs:
            log.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--binary", type=Path, required=True)
    parser.add_argument("--workdir", type=Path, required=True,
                        help="server directory containing paths.txt and game configuration")
    parser.add_argument("--room", default="MP1 SANCTORUS")
    parser.add_argument("--mode", choices=("active", "idle"), default="active",
                        help="active is a real-client workload; idle is a control only")
    parser.add_argument("--client-binary", type=Path,
                        help="headless -netcheck-capable client; defaults to --binary")
    parser.add_argument("--client-workdir", type=Path,
                        help="configured client directory; defaults to --workdir")
    parser.add_argument("--clients-per-match", type=int, default=2)
    parser.add_argument("--join-timeout", type=int, default=90)
    parser.add_argument("--warmup-seconds", type=float, default=10.0)
    parser.add_argument("--max-p99-ms", type=float, default=16.67,
                        help="maximum permitted per-step p99, used by --require-healthy")
    parser.add_argument("--require-healthy", action="store_true",
                        help="fail an active arm on missed deadlines/step failures/p99 overrun")
    parser.add_argument("--instances", default="1,2,4,8")
    parser.add_argument("--seconds", type=int, default=120)
    parser.add_argument("--sample-interval", type=float, default=1)
    parser.add_argument("--startup-timeout", type=int, default=60)
    parser.add_argument("--base-port", type=int, default=29000)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.binary = args.binary.resolve(); args.workdir = args.workdir.resolve(); args.output = args.output.resolve()
    if args.client_binary is not None: args.client_binary = args.client_binary.resolve()
    if args.client_workdir is not None: args.client_workdir = args.client_workdir.resolve()
    if not Path("/proc").is_dir():
        parser.error("this benchmark currently requires Linux /proc")
    if not args.binary.is_file() or not os.access(args.binary, os.X_OK):
        parser.error("binary must be an executable published dedicated server")
    if not (args.workdir / "paths.txt").is_file():
        parser.error("workdir must contain configured paths.txt")
    if args.mode == "active":
        client_binary = args.client_binary or args.binary
        client_workdir = args.client_workdir or args.workdir
        if not client_binary.is_file() or not os.access(client_binary, os.X_OK):
            parser.error("client binary must be a runnable -netcheck -nographics application")
        if not (client_workdir / "paths.txt").is_file():
            parser.error("client workdir must contain configured paths.txt")
        if not 2 <= args.clients_per_match <= 8:
            parser.error("active matches need 2..8 real independent headless clients")
        if args.seconds < 35 or args.join_timeout <= 0 or args.warmup_seconds < 0:
            parser.error("active mode requires >=35 measured seconds and valid join/warmup windows")
    counts = sorted({int(v) for v in args.instances.split(",") if v.strip()})
    if not counts or counts[0] < 1 or args.seconds < 1 or args.sample_interval <= 0 or args.max_p99_ms <= 0:
        parser.error("instances/seconds/sample-interval must be positive")
    args.output.mkdir(parents=True, exist_ok=True)
    args.server_binary_hash = sha256(args.binary)
    args.client_binary_hash = sha256(args.client_binary or args.binary) if args.mode == "active" else None
    results = [arm(args, count, args.output) for count in counts]
    (args.output / "capacity-summary.json").write_text(json.dumps(
        dict(room=args.room, mode=args.mode, arms=results), indent=2) + "\n")


if __name__ == "__main__":
    main()
