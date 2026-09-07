#!/usr/bin/env bash
# Data-free checks validate the content requirement, directory and (when built)
# authoritative connection fixture. GAME_DATA_DIRECTORY enables the real server.
# Synthetic fixture success is not gameplay validation.
set -euo pipefail

PYTHON=python3
command -v "$PYTHON" >/dev/null 2>&1 || PYTHON=python
"$PYTHON" - "${1:-publish/linux-x64-server}" <<'PY'
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import sys
import tempfile
import time

package = Path(sys.argv[1]).resolve()
dotnet = os.environ.get("DOTNET", "dotnet")
assembly = package / "FruityPrimeServer.dll"
if assembly.is_file() and shutil.which(dotnet):
    binary = [dotnet, str(assembly)]
else:
    names = ("FruityPrimeServer.exe", "FruityPrimeServer")
    executable = next((package / name for name in names if (package / name).is_file()), None)
    if executable is None:
        raise SystemExit(f"FAIL: no server binary in {package}")
    binary = [str(executable)]


def require(condition, message):
    if not condition:
        raise RuntimeError(message)
    print("ok: " + message, flush=True)


with tempfile.TemporaryDirectory(prefix="fruity-server-check-") as temporary:
    root = Path(temporary)
    children, streams = [], []

    def start(command, name):
        log = root / f"{name}.log"
        stream = log.open("w")
        streams.append(stream)
        process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT,
                                   stdin=subprocess.DEVNULL)
        children.append(process)
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            text = log.read_text(errors="replace")
            match = re.search(r"listening on UDP (\d+)", text)
            if match and process.poll() is None:
                return process, int(match.group(1))
            if process.poll() is not None:
                raise RuntimeError(f"{name} exited before binding: {text}")
            time.sleep(0.05)
        raise RuntimeError(f"{name} never reported a listening socket")

    def query(port, request):
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
            sock.settimeout(3)
            sock.sendto(request, ("127.0.0.1", port))
            return sock.recvfrom(2048)[0]

    try:
        master, master_port = start(binary + ["-masterserver", "-port", "0", "-hostports", "none", "-noupdate"], "directory")
        response = query(master_port, bytes([18, 7]))
        require(response == bytes([19, 0, 0, 2, 8]),
                "fresh directory advertises authoritative family/protocol and an empty list")
        data = os.environ.get("GAME_DATA_DIRECTORY")
        nettest = Path(os.environ.get("NETTEST_DLL", str(package / "nettest.dll"))).resolve()
        server_port = None
        if data:
            server, server_port = start(binary + ["-server", "-data", str(Path(data).resolve()),
                "-dataversion", os.environ.get("GAME_DATA_VERSION", "AMHE1"),
                "-port", "0", "-nomaster", "-noupdate", "-servername", "CI smoke test"], "server")
            require(server.poll() is None, "authoritative server starts with supplied game content")
            for flag, expected in (("-noprojectilecatchup", "lagCompEnabled=True projectileCatchUpEnabled=False"),
                                   ("-nolagcomp", "lagCompEnabled=False projectileCatchUpEnabled=False")):
                name = flag[1:]
                controlled, _ = start(binary + ["-server", "-data", str(Path(data).resolve()),
                    "-dataversion", os.environ.get("GAME_DATA_VERSION", "AMHE1"),
                    "-port", "0", "-nomaster", "-noupdate", flag], name)
                deadline = time.monotonic() + 3
                while expected not in (root / f"{name}.log").read_text() and time.monotonic() < deadline:
                    time.sleep(0.05)
                require(expected in (root / f"{name}.log").read_text(), flag + " reaches authoritative combat settings")
                controlled.terminate()
                controlled.wait(timeout=5)
        else:
            rejected = subprocess.run(binary + ["-server", "-port", "0", "-nomaster", "-noupdate"],
                text=True, capture_output=True, timeout=15, check=False)
            output = rejected.stdout + rejected.stderr
            require(rejected.returncode != 0 and "requires -data" in output.lower(),
                    "authoritative server rejects startup without explicitly supplied content")
            if nettest.is_file():
                fixture, server_port = start([dotnet, str(nettest), "--connection-server", "0"], "connection-fixture")
                print("Checking synthetic authoritative states; no game simulation is running.", flush=True)
            else:
                print("Protocol fixture unavailable in this publish directory; content gate and directory checked.", flush=True)
        if server_port is not None:
            response = query(server_port, bytes([14, 7]))
            # Passive discovery retains its compact status framing. Gameplay
            # uses the distinct 24-byte authoritative connection envelope.
            body = response[1:]
            require(len(body) == 130 and response[0] == 15, "server answers an exact-size discovery status query")
            require(body[95] == 8 and body[96] == 8 and body[129] == 2,
                    "status advertises eight slots, authoritative family and protocol 8")
            require(bool(body[15:55].rstrip(b"\0")), "status names its room")
            if nettest.is_file():
                check = subprocess.run([dotnet, str(nettest), "localhost", str(server_port)],
                    text=True, capture_output=True, timeout=35, check=False)
                print(check.stdout, end="")
                if check.stderr:
                    print(check.stderr, file=sys.stderr, end="")
                require(check.returncode == 0, "authoritative hostname, lifecycle, clock, input and snapshot conformance")
                if data:
                    history = subprocess.run([dotnet, str(nettest), "--history-boundary",
                        str(Path(data).resolve()), os.environ.get("GAME_DATA_VERSION", "AMHE1")],
                        text=True, capture_output=True, timeout=45, check=False)
                    print(history.stdout, end="")
                    if history.stderr:
                        print(history.stderr, file=sys.stderr, end="")
                    require(history.returncode == 0, "history and snapshots share the completed simulation boundary")
                    bombs = subprocess.run([dotnet, str(nettest), "--bomb-pool",
                        str(Path(data).resolve()), os.environ.get("GAME_DATA_VERSION", "AMHE1")],
                        text=True, capture_output=True, timeout=45, check=False)
                    print(bombs.stdout, end="")
                    if bombs.stderr:
                        print(bombs.stderr, file=sys.stderr, end="")
                    require(bombs.returncode == 0, "headless bombs spawn, expire and reuse their bounded pool")
                    for option in ("--catch-up", "--weapon-policy", "--homing"):
                        result = subprocess.run([dotnet, str(nettest), option,
                            str(Path(data).resolve()), os.environ.get("GAME_DATA_VERSION", "AMHE1")],
                            text=True, capture_output=True, timeout=45, check=False)
                        print(result.stdout, end="")
                        if result.stderr:
                            print(result.stderr, file=sys.stderr, end="")
                        require(result.returncode == 0, option + " real-content regression checks")
        require(master.poll() is None, "directory remains running after checks")
    except Exception as error:
        print("FAIL: " + str(error), file=sys.stderr)
        for log in root.glob("*.log"):
            print(f"{log.name}:\n{log.read_text(errors='replace')[-8000:]}", file=sys.stderr)
        sys.exit(1)
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
PY
