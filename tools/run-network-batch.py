#!/usr/bin/env python3
"""Seeded local/remote authoritative rendered checks; see run-batch.sh for settings."""
import json
import os
from pathlib import Path
import random
import re
import shutil
import signal
import socket
import subprocess
import sys
import time

TOOLS = Path(__file__).resolve().parent
MAPS = [
    "AD1 TRANSFER LOCK BT", "AD1 TRANSFER LOCK DM", "AD2 ALINOS PERCH", "AD2 MAGMA VENTS",
    "CTF1 FAULT LINE - EXPANDED", "CTF1_FAULT LINE", "E3 FIRST HUNT", "Gorea Prison",
    "MP1 SANCTORUS", "MP10 OVERLOAD", "MP11 BREAKTHROUGH", "MP12 SIC TRANSIT",
    "MP13 ACCELERATOR", "MP14 OUTER REACH", "MP2 HARVESTER", "MP3 PROVING GROUND",
    "MP4 HIGHGROUND", "MP4 HIGHGROUND - EXPANDED", "MP5 FUEL SLUICE", "MP6 HEADSHOT",
    "MP7 PROCESSOR CORE", "MP8 FIRE CONTROL", "MP9 CRYOCHASM", "UNIT 3 VESPER STARPORT",
    "UNIT 4 ARCTERRA BASE", "UNIT1 ALINOS LANDFALL", "UNIT2 LANDING BAY",
]
HUNTERS = ["Samus", "Kanden", "Trace", "Sylux", "Noxus", "Spire", "Weavel"]
NAMES = ["ALPHA", "BRAVO", "CHARLIE", "DELTA", "ECHO", "FOXTROT", "GOLF", "HOTEL"]


def bounded(value, lower, upper, name):
    result = int(value)
    if not lower <= result <= upper:
        raise ValueError(f"{name} must be between {lower} and {upper}")
    return result


def free_port():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def interrupted(signum, frame):
    raise KeyboardInterrupt


def main():
    remote = os.environ.get("MPH_BATCH_REMOTE") == "1"
    if len(sys.argv) > 3:
        raise ValueError("usage: run-batch.sh [runs] [seed]")
    runs = bounded(sys.argv[1] if len(sys.argv) > 1 else 10, 1, 10000, "runs")
    seed = int(sys.argv[2]) if len(sys.argv) > 2 else random.SystemRandom().randrange(2**32)
    rng = random.Random(seed)
    minimum = bounded(os.environ.get("BATCH_MIN_SECONDS", 70), 10, 300, "BATCH_MIN_SECONDS")
    maximum = bounded(os.environ.get("BATCH_MAX_SECONDS", 129), minimum, 300, "BATCH_MAX_SECONDS")
    stagger = bounded(os.environ.get("BATCH_STAGGER_SECONDS", 3), 0, 10, "BATCH_STAGGER_SECONDS")
    players_override = os.environ.get("BATCH_PLAYERS")
    if players_override:
        players_override = bounded(players_override, 2, 8, "BATCH_PLAYERS")
    host = os.environ.get("MPH_SERVER_HOST", "") if remote else "127.0.0.1"
    if not host:
        raise ValueError("remote batches require explicit MPH_SERVER_HOST; no server is modified")
    remote_port = bounded(os.environ.get("MPH_SERVER_PORT", 27888), 1, 65535, "MPH_SERVER_PORT")
    max_extra = bounded(os.environ.get("BATCH_MAX_EXTRA_MS", 150), 0, 1000, "BATCH_MAX_EXTRA_MS")
    data_value = os.environ.get("GAME_DATA_DIRECTORY")
    if not data_value:
        raise ValueError("GAME_DATA_DIRECTORY must name extracted game data for rendered clients")
    data = Path(data_value).expanduser().resolve()
    if not data.is_dir() or not (data / "models").is_dir():
        raise ValueError(f"extracted game data directory is missing models: {data}")
    if "=" in str(data) or "\n" in str(data):
        raise ValueError("game data path cannot contain '=' or a newline (paths.txt format)")
    version = os.environ.get("GAME_DATA_VERSION", "AMHE1")
    if version not in ("AMHE0", "AMHE1", "AMHP0", "AMHP1", "AMHJ0", "AMHJ1", "AMHK0", "A76E0"):
        raise ValueError("unsupported GAME_DATA_VERSION")
    build = Path(os.environ.get("GAME_BUILD_DIRECTORY",
                 TOOLS.parent / "src/Client/bin/Release/net10.0")).expanduser().resolve()
    for name in ("FruityPrime.dll", "FruityPrime.deps.json", "FruityPrime.runtimeconfig.json"):
        if not (build / name).is_file():
            raise ValueError(f"missing runtime file: {build / name}; set GAME_BUILD_DIRECTORY")
    dotnet = os.environ.get("DOTNET") or shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    if not shutil.which(dotnet):
        raise ValueError("dotnet not found; set DOTNET to its executable path")
    dotnet = shutil.which(dotnet)
    rotation_value = os.environ.get("BATCH_ROTATION")
    rotation = Path(rotation_value).expanduser().resolve() if rotation_value else None
    if rotation and (remote or not rotation.is_file()):
        raise ValueError("BATCH_ROTATION must be an existing local rotation file; remote rotation belongs to its operator")
    output = Path(os.environ.get("BATCH_OUTPUT", TOOLS / f"{'batchpi' if remote else 'batch'}-{seed}")).resolve()
    output.mkdir(parents=True, exist_ok=False)
    runtime = output / "runtime"
    runtime.mkdir()
    # Copy only runtime content into our fresh directory; never change build files,
    # existing paths.txt, settings, logs, or extracted assets.
    for source in build.iterdir():
        if source.is_file() and source.suffix in (".dll", ".json", ".so", ".dylib"):
            if source.suffix != ".json" or source.name.endswith((".deps.json", ".runtimeconfig.json")):
                shutil.copy2(source, runtime / source.name)
    for name in ("runtimes", "maps"):
        if (build / name).is_dir():
            shutil.copytree(build / name, runtime / name)
    # This is the current extractor's format version, not a ROM revision.
    (runtime / "paths.txt").write_text(f"0.35.1.0\n{version}={data}\n")
    env = os.environ.copy()
    env.setdefault("MESA_GL_VERSION_OVERRIDE", "4.5COMPAT")
    env.setdefault("ALSOFT_DRIVERS", "null")
    env["LD_LIBRARY_PATH"] = str(runtime) + os.pathsep + env.get("LD_LIBRARY_PATH", "")
    game = [dotnet, str(runtime / "FruityPrime.dll")]
    failures = 0
    with (output / "summary.txt").open("w") as summary:
        def say(message):
            print(message, flush=True)
            summary.write(message + "\n")
            summary.flush()

        say(f"batch seed={seed} runs={runs} remote={remote} output={output}")
        for index in range(1, runs + 1):
            directory = output / f"{index:02d}"
            directory.mkdir()
            players = players_override or (8 if index % 5 == 0 else rng.randint(2, 6))
            seconds = rng.randint(minimum, maximum)
            lag = rng.choice([0, 0, min(30, max_extra), min(60, max_extra), min(100, max_extra), max_extra]
                             if remote else [0, 15, 30, 60, 100, 150, 250])
            loss = rng.choice([0, 0, 0, 1, 2, 3])
            rooms = rng.sample(MAPS, 2)
            minutes, goal = rng.randint(1, 3), rng.randint(2, 5)
            roster = [rng.choice(HUNTERS) for _ in range(players)]
            scenario = dict(seed=seed, run=index, players=players, seconds=seconds, oneWayMs=lag,
                            jitterMs=lag // 5, lossPercent=loss, hunters=roster,
                            rooms="operator controlled" if remote else rooms,
                            rotation=str(rotation) if rotation else None, minutes=minutes, pointGoal=goal)
            (directory / "scenario.json").write_text(json.dumps(scenario, indent=2) + "\n")
            say(f"run {index}: {json.dumps(scenario)}")
            children, streams, clients = [], [], []
            problems = []

            def start(command, name, ready=None):
                log = directory / f"{name}.log"
                stream = log.open("w")
                streams.append(stream)
                process = subprocess.Popen(command, cwd=runtime, env=env, stdin=subprocess.DEVNULL,
                                           stdout=stream, stderr=subprocess.STDOUT)
                children.append(process)
                if ready:
                    deadline = time.monotonic() + 30
                    while time.monotonic() < deadline:
                        match = re.search(ready, log.read_text(errors="replace"))
                        if match and process.poll() is None:
                            return process, match
                        if process.poll() is not None:
                            raise RuntimeError(f"{name} exited {process.returncode} before readiness; see {log}")
                        time.sleep(0.05)
                    raise RuntimeError(f"{name} did not become ready; see {log}")
                return process, log

            try:
                target_host, port = host, remote_port
                if not remote:
                    local_rotation = directory / "maprotation.txt"
                    if rotation:
                        shutil.copy2(rotation, local_rotation)
                    else:
                        local_rotation.write_text("".join(f"{room} | Battle | {minutes} | {goal}\n" for room in rooms))
                    server, match = start(game + ["-server", "-data", str(data), "-dataversion", version,
                        "-rotation", str(local_rotation), "-port", "0", "-players", "8", "-nomaster", "-noupdate"],
                        "server", r"listening on UDP (\d+)")
                    port = int(match.group(1))
                if lag or loss:
                    proxy_port = free_port()
                    env["MPHREAD_LAG_SEED"] = str(seed + index)
                    start([sys.executable, str(TOOLS / "udp-lag.py"), str(proxy_port), host, str(port),
                           str(lag), str(lag // 5), str(loss)], "lag", r"\[lag\] :\d+ ->")
                    target_host, port = "127.0.0.1", proxy_port
                for slot, hunter in enumerate(roster):
                    name = f"R{index:04d}{NAMES[slot]}"
                    process, log = start(game + ["-netcheck", target_host, "-port", str(port), "-name", name,
                        "-hunter", hunter, "-seconds", str(seconds), "-size", "320x180", "-noupdate"], name)
                    clients.append((process, log))
                    if slot + 1 < players:
                        time.sleep(stagger)
                deadline = time.monotonic() + seconds + 45
                for process, log in clients:
                    code = process.wait(timeout=max(0.1, deadline - time.monotonic()))
                    if code:
                        problems.append(f"{log.name}: client exit {code}")
                for process in children:
                    if all(process is not client for client, _ in clients) and process.poll() is not None:
                        problems.append(f"server/proxy exited unexpectedly: {process.returncode}")
            except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
                problems.append(str(error))
            finally:
                # Popen retains each child's identity and exit status. Never scan
                # process names, signal a process group, or touch a remote host.
                for process in children:
                    if process.poll() is None:
                        process.terminate()
                for process in children:
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait()
                for stream in streams:
                    stream.close()
            if clients:
                with (directory / "cross.txt").open("w") as cross:
                    checked = subprocess.run([sys.executable, str(TOOLS / "compare-reports.py"),
                        *[str(log) for _, log in clients]], stdout=cross, stderr=subprocess.STDOUT, check=False)
                if checked.returncode:
                    problems.append("authoritative report validation failed; see cross.txt")
            if len(clients) != players:
                problems.append(f"only {len(clients)}/{players} clients started")
            for path in runtime.glob(f"netlog-R{index:04d}*.txt"):
                shutil.move(str(path), directory / path.name)
            (directory / "problems.txt").write_text("".join(problem + "\n" for problem in problems))
            failures += bool(problems)
            say(f"run {index}: " + ("FAIL: " + "; ".join(problems) if problems else "PASS"))
        say(f"{runs - failures}/{runs} runs passed")
    return int(failures != 0)


signal.signal(signal.SIGTERM, interrupted)
try:
    sys.exit(main())
except KeyboardInterrupt:
    print("batch interrupted; owned children stopped", file=sys.stderr)
    sys.exit(130)
except (OSError, ValueError) as error:
    print(f"batch: {error}", file=sys.stderr)
    sys.exit(2)
