#!/usr/bin/env python3
"""N0 independent-process UDP campaigns. Mac/Windows only; stdlib only.

Build first. An output directory is immutable: use a new name for every attempt.
No public server/master, production profile, or existing process is touched.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import signal
import subprocess
import time
from net2_evidence import adjudicate

PROFILES = {"lan": (0, 0, 0, 0, 0), "moderate": (100, 20, 1, 0, 0),
            "poor": (250, 40, 2, 0, 0), "extreme": (400, 80, 5, 3, 1)}
HUNTERS = ["Samus", "Kanden", "Trace", "Sylux", "Noxus", "Spire", "Weavel", "Samus"]


def digest(path):
    value = hashlib.sha256()
    with open(path, "rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def stop(process):
    if process.poll() is None:
        process.terminate()
    try:
        return process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.kill()
        return process.wait(timeout=10)


def run(args):
    if platform.system() not in ("Darwin", "Windows"):
        raise RuntimeError("This campaign is restricted to Mac and Windows")
    args.out = args.out.resolve()
    args.out.mkdir(parents=True, exist_ok=False)
    stage = args.out / "bin"
    shutil.copytree(args.build.resolve(), stage)
    assembly = stage / "ProjectPrime.dll"
    if not assembly.exists():
        raise RuntimeError("Build directory has no ProjectPrime.dll")
    profile_text = args.paths.resolve().read_text(encoding="utf-8-sig")
    asset_files = []
    for line in profile_text.splitlines():
        if "=" not in line:
            continue
        key, value = line.split("=", 1)
        if value and key != "Export":
            root = Path(value)
            if not root.is_absolute() or not root.is_dir():
                raise RuntimeError("paths.txt asset paths must be absolute existing directories")
            for file in sorted(root.rglob("*")):
                if file.is_file():
                    asset_files.append({"version": key, "path": file.relative_to(root).as_posix(),
                                        "size": file.stat().st_size, "sha256": digest(file)})
    if not asset_files:
        raise RuntimeError("No extracted game assets in paths.txt")
    save(args.out / "assets.json", asset_files)
    rotation = args.out / "rotation.txt"
    rooms = args.rooms.split(",")
    rotation.write_text("\n".join(f"{room} | Battle | {args.round_minutes} | 999" for room in rooms), encoding="utf-8")
    dotnet = str(Path(args.dotnet).resolve()) if Path(args.dotnet).is_file() else args.dotnet
    manifest = {"source": args.source, "platform": platform.platform(), "python": platform.python_version(),
        "dotnetInfo": subprocess.check_output([dotnet, "--info"], text=True),
        "assemblySha256": digest(assembly), "assetManifestSha256": digest(args.out / "assets.json"),
        "harnessSha256": {name: digest(Path(__file__).with_name(name)) for name in ("run-net2.py", "net2_evidence.py")},
        "settings": {k: str(v) if isinstance(v, Path) else v for k, v in vars(args).items()},
        "commands": [], "startedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
    save(args.out / "manifest.json", manifest)
    processes, handles, records = [], [], []
    server = None
    started = time.monotonic()

    def launch(name, extra, duration, expects_shots=True):
        profile = args.out / name
        profile.mkdir()
        exports = profile / "exports"
        exports.mkdir()
        isolated_paths = [line for line in profile_text.splitlines() if not line.startswith("Export=")]
        (profile / "paths.txt").write_text("\n".join([*isolated_paths, f"Export={exports}", ""]), encoding="utf-8")
        maps = profile / "empty-maps"
        maps.mkdir()
        # Do not silently inherit a developer's experimental/test flags into a
        # supposedly reproducible control. Keep ordinary OS/runtime environment.
        env = {key: value for key, value in os.environ.items()
               if not key.startswith(("PRIME_", "MPHREAD_", "PROJECT_PRIME_"))}
        env.update(PROJECT_PRIME_USER_DATA=str(profile), ALSOFT_DRIVERS="null",
                   MPHREAD_PHASE_SECONDS="2", PRIME_NET2_EVIDENCE=str(args.out / f"{name}.json"))
        if args.soak:
            env["PRIME_NET2_SOAK"] = "1"
        command = [dotnet, str(assembly), "-mapdir", str(maps), "-noupdate", "-noautoupdate", *extra]
        handle = open(args.out / f"{name}.log", "w", encoding="utf-8")
        handles.append(handle)
        process = subprocess.Popen(command, cwd=stage, env=env, stdin=subprocess.DEVNULL,
                                   stdout=handle, stderr=subprocess.STDOUT)
        processes.append(process)
        record = {"name": name, "pid": process.pid, "startedSeconds": time.monotonic() - started,
                  "requestedSeconds": duration, "expectsShots": expects_shots, "exit": None, "timedOut": False}
        records.append(record)
        manifest["commands"].append({"name": name, "argv": command,
            "environment": {k: env[k] for k in ("PROJECT_PRIME_USER_DATA", "ALSOFT_DRIVERS", "MPHREAD_PHASE_SECONDS", "PRIME_NET2_EVIDENCE")},
            "inheritedGameEnvironment": "cleared PRIME_*, MPHREAD_*, PROJECT_PRIME_*",
            "soak": args.soak})
        save(args.out / "manifest.json", manifest)
        return process, record

    try:
        server, server_record = launch("server", ["-server", "-port", str(args.port), "-players", str(args.players),
            "-nomaster", "-noserverreplays", "-rotation", str(rotation), "-netdebug"], args.seconds, False)
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            if server.poll() is not None:
                raise RuntimeError("server exited during startup")
            if "listening on UDP" in (args.out / "server.log").read_text(errors="replace"):
                break
            time.sleep(.1)
        else:
            server_record["timedOut"] = True
            raise RuntimeError("server loader/startup timeout")
        time.sleep(2)
        clients = []
        rtt, jitter, loss, reorder, duplicate = PROFILES[args.profile]

        def client(slot, name, seconds):
            extra = ["-netcheck", "127.0.0.1", "-port", str(args.port), "-name", name,
                "-hunter", HUNTERS[slot], "-seconds", str(seconds), "-size", "480x270",
                "-netlag", f"{rtt}:{jitter}", "-netloss", f"{loss}%", "-netreorder", f"{reorder}%",
                "-netduplicate", f"{duplicate}%", "-netseed", str(args.seed + slot)]
            if not args.rendered:
                extra.append("-nographics")
            if args.record:
                extra.append("-recorddemo")
            if args.rendered and args.players == 3:
                shots = args.out / f"shots-{name}"
                shots.mkdir()
                extra += ["-shots", str(shots)]
            if args.scenario != "tour":
                extra += ["-hitrig", args.scenario]
            observer = args.players == 3 and args.rendered and slot == 2
            if observer:
                extra += ["-spectate", "0"]
            if args.soak and slot == args.players - 2:
                extra += ["-spectate", "90", "-rejoin", "150"]
            expects = not observer and (args.soak or args.scenario in ("tour", "duel", "shockcoil-all") or slot % 2 == 0)
            p, record = launch(name, extra, seconds, expects)
            clients.append((p, record, time.monotonic() + seconds + 120))

        # Soak: one late join, then a normal departure and a fresh process/connection.
        client_start = time.monotonic()
        for slot in range(args.players - (1 if args.soak else 0)):
            client(slot, f"peer-{slot}", args.seconds)
            time.sleep(.25)
        late_started = reconnect_started = False
        while True:
            elapsed = time.monotonic() - client_start
            bot_finished = not args.soak or "[net2] bot fill complete" in (args.out / "server.log").read_text(errors="replace")
            if args.soak and not late_started and elapsed >= 30 and (bot_finished or elapsed >= 120):
                client(args.players - 1, "peer-late", 180)
                late_started = True
                late_started_seconds = elapsed
            if args.soak and late_started and not reconnect_started and elapsed >= late_started_seconds + 210:
                client(args.players - 1, "peer-reconnect", max(1, args.seconds - elapsed))
                reconnect_started = True
            for process, record, limit in clients:
                if record["exit"] is not None:
                    continue
                if process.poll() is None and time.monotonic() > limit:
                    record["timedOut"] = True
                    stop(process)
                if process.poll() is not None:
                    record["exit"] = process.returncode
                    record["endedSeconds"] = time.monotonic() - started
            if server.poll() is not None:
                server_record["unexpectedExit"] = True
                raise RuntimeError("server exited before clients finished")
            if all(r["exit"] is not None for _, r, _ in clients) and (not args.soak or reconnect_started):
                break
            time.sleep(.2)
        (args.out / "server.json.stop").touch()
        try:
            server_record["exit"] = server.wait(timeout=20)
        except subprocess.TimeoutExpired:
            server_record["timedOut"] = True
            # Frozen main predates the marker; retain that limitation, do not claim PASS.
            if platform.system() == "Darwin":
                server.send_signal(signal.SIGINT)
            server_record["exit"] = stop(server)
    except (RuntimeError, OSError) as error:
        manifest["runnerError"] = str(error)
    finally:
        for process, record in zip(processes, records):
            if process.poll() is None:
                record["timedOut"] = True
                stop(process)
            record["exit"] = process.returncode
        for handle in handles:
            handle.close()
        manifest["elapsedSeconds"] = time.monotonic() - started
        manifest["assemblyAfterSha256"] = digest(assembly)
        save(args.out / "manifest.json", manifest)
        summary = adjudicate(args.out, records, expected_protocol=args.protocol)
        if len(records) < args.players + 1 or manifest.get("runnerError") or manifest["assemblyAfterSha256"] != manifest["assemblySha256"]:
            summary["status"] = "FAIL"
            summary["failures"].append(manifest.get("runnerError", "missing participants or modified running assembly"))
        save(args.out / "summary.json", summary)
        print(json.dumps({"status": summary["status"], "out": str(args.out), "failures": summary["failures"], "incomplete": summary["incomplete"]}), flush=True)
    return 0 if summary["status"] == "PASS" else 1


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--build", type=Path, required=True)
    p.add_argument("--paths", type=Path, required=True)
    p.add_argument("--source", required=True, help="Exact source SHA plus dirty diff hash if applicable")
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--dotnet", default="dotnet")
    p.add_argument("--protocol", type=int, default=43, help="Expected evidence protocol (historical campaigns may specify 41)")
    p.add_argument("--players", type=int, choices=(2, 3, 4, 8), default=2)
    p.add_argument("--seconds", type=float, default=90)
    p.add_argument("--port", type=int, default=27991)
    p.add_argument("--seed", type=int, default=8128)
    p.add_argument("--profile", choices=PROFILES, default="lan")
    p.add_argument("--scenario", choices=("tour", "duel", "shockcoil", "shockcoil-all", "missile"), default="tour")
    p.add_argument("--rooms", default="MP1 SANCTORUS")
    p.add_argument("--round-minutes", type=float, default=30)
    p.add_argument("--rendered", action="store_true")
    p.add_argument("--record", action="store_true")
    p.add_argument("--soak", action="store_true")
    args = p.parse_args()
    if args.seconds <= 0 or args.round_minutes <= 0 or not 1024 <= args.port <= 65535:
        p.error("duration, round length and port must be positive and valid")
    if args.soak and (args.players != 8 or args.seconds < 1200 or args.seconds > 1800):
        p.error("--soak requires eight slots and 1200–1800 seconds")
    raise SystemExit(run(args))
