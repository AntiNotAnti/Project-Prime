#!/usr/bin/env python3
"""Run inside dbus-run-session and Xvfb; never replace the user's desktop bus."""
import argparse
import os
import pathlib
import subprocess
import sys
import tempfile
import time

parser = argparse.ArgumentParser()
parser.add_argument("--native", required=True)
parser.add_argument("--dotnet", default="dotnet")
parser.add_argument("--isolated", action="store_true", help=argparse.SUPPRESS)
args = parser.parse_args()
if sys.platform != "linux":
    raise RuntimeError("Use dbus-run-session -- xvfb-run -a python3 run-ibus-check.py ... on Linux")
if not args.isolated:
    command = ["dbus-run-session", "--", "xvfb-run", "-a", sys.executable, str(pathlib.Path(__file__).resolve()),
               "--native", str(pathlib.Path(args.native).resolve()), "--dotnet", args.dotnet, "--isolated"]
    sys.exit(subprocess.run(command, check=False).returncode)
if not os.environ.get("DBUS_SESSION_BUS_ADDRESS"):
    raise RuntimeError("Isolated D-Bus session is missing")
directory = pathlib.Path(__file__).resolve().parent
with tempfile.TemporaryDirectory(prefix="prime-ibus-check-") as temporary:
    scratch = pathlib.Path(temporary)
    environment = os.environ.copy()
    environment.pop("IBUS_ADDRESS", None)
    environment.pop("IBUS_USE_PORTAL", None)
    environment["XDG_CONFIG_HOME"] = str(scratch / "config")
    environment["XDG_CACHE_HOME"] = str(scratch / "cache")
    environment["XMODIFIERS"] = "@im=ibus"
    processes = []
    with (scratch / "ibus.log").open("w+", encoding="utf-8") as log:
        try:
            daemon = subprocess.Popen(["ibus-daemon", "--panel", "disable", "--config", "disable"], env=environment, stdout=log, stderr=log)
            processes.append(daemon)
            connected = False
            for _ in range(100):
                if daemon.poll() is not None:
                    break
                probe = subprocess.run(["ibus", "address"], env=environment, capture_output=True, text=True)
                if probe.returncode == 0 and probe.stdout.strip().startswith("unix:"):
                    connected = True
                    break
                time.sleep(0.05)
            if not connected:
                raise RuntimeError("Isolated IBus daemon failed to start")
            ready = scratch / "ready"
            engine = subprocess.Popen([sys.executable, str(directory / "ibus-fixture.py"), str(ready)], env=environment, stdout=log, stderr=log)
            processes.append(engine)
            for _ in range(100):
                if ready.exists() or engine.poll() is not None:
                    break
                time.sleep(0.05)
            if not ready.exists():
                raise RuntimeError("Deterministic engine failed to register")
            subprocess.run(["ibus", "engine", "prime-rmlui-test"], env=environment, check=True, timeout=10)
            subprocess.run([args.dotnet, "run", "--project", str(directory / "rmlui-linux-ime-check.csproj"), "--", "--native",
                            str(pathlib.Path(args.native).resolve()), "--ibus"], env=environment, check=True, timeout=90)
        except Exception:
            log.flush()
            log.seek(0)
            sys.stderr.write(log.read())
            raise
        finally:
            for process in reversed(processes):
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=3)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait()
