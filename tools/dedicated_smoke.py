#!/usr/bin/env python3
"""Bounded process-owned dedicated-server smoke. Works with argv containing spaces."""
import argparse
import os
from pathlib import Path
import shutil
import signal
import socket
import struct
import subprocess
import sys
import tempfile
import time


class ProcessTree:
    def __init__(self, command, output, env=None, stdin=subprocess.DEVNULL):
        self.job = None
        flags = {}
        if os.name == "nt":
            # Assign the suspended root before it can create descendants. All
            # descendants inherit the job; closing it terminates the entire tree.
            flags["creationflags"] = subprocess.CREATE_NEW_PROCESS_GROUP | 0x00000004
        else:
            flags["start_new_session"] = True
        self.process = subprocess.Popen(command, stdin=stdin, stdout=output,
                                        stderr=subprocess.STDOUT, env=env, **flags)
        if os.name == "nt":
            try:
                self.job = WindowsJob(self.process.pid)
            except BaseException:
                self.process.kill()
                self.process.wait(timeout=3)
                raise

    def close(self):
        if self.job is not None:
            self.job.close()
        else:
            # Signal the group even after the wrapper exited: its descendants
            # can still own the group and inherited log/UDP handles.
            for sig in (signal.SIGTERM, signal.SIGKILL):
                try:
                    os.killpg(self.process.pid, sig)
                except ProcessLookupError:
                    pass
                except PermissionError:
                    # macOS can report EPERM for a group consisting solely of
                    # orphan zombies. They own no running work or handles.
                    states = subprocess.run(["ps", "-axo", "pgid=,stat="], capture_output=True,
                                            text=True, timeout=3, check=True).stdout.splitlines()
                    live = any(len(parts := row.split()) >= 2 and parts[0] == str(self.process.pid)
                               and not parts[1].startswith("Z") for row in states)
                    if live:
                        raise
                try:
                    self.process.wait(timeout=1)
                except subprocess.TimeoutExpired:
                    continue
        try:
            self.process.wait(timeout=3)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait(timeout=3)


class WindowsJob:
    def __init__(self, pid):
        import ctypes as C
        from ctypes import wintypes as W
        self.api = C.WinDLL("kernel32", use_last_error=True)
        class Limits(C.Structure):
            _fields_ = [("process_time", C.c_longlong), ("job_time", C.c_longlong), ("flags", W.DWORD),
                        ("min_working", C.c_size_t), ("max_working", C.c_size_t), ("active", W.DWORD),
                        ("affinity", C.c_size_t), ("priority", W.DWORD), ("scheduling", W.DWORD)]
        class Counters(C.Structure):
            _fields_ = [(name, C.c_ulonglong) for name in ("read_ops", "write_ops", "other_ops", "read_bytes", "write_bytes", "other_bytes")]
        class Extended(C.Structure):
            _fields_ = [("limits", Limits), ("io", Counters), ("process_memory", C.c_size_t),
                        ("job_memory", C.c_size_t), ("peak_process", C.c_size_t), ("peak_job", C.c_size_t)]
        class Thread(C.Structure):
            _fields_ = [("size", W.DWORD), ("usage", W.DWORD), ("id", W.DWORD), ("owner", W.DWORD),
                        ("base", W.LONG), ("delta", W.LONG), ("flags", W.DWORD)]
        for name, args, result in (
            ("CreateJobObjectW", [W.LPVOID, W.LPCWSTR], W.HANDLE),
            ("SetInformationJobObject", [W.HANDLE, C.c_int, W.LPVOID, W.DWORD], W.BOOL),
            ("OpenProcess", [W.DWORD, W.BOOL, W.DWORD], W.HANDLE),
            ("AssignProcessToJobObject", [W.HANDLE, W.HANDLE], W.BOOL),
            ("CloseHandle", [W.HANDLE], W.BOOL),
            ("CreateToolhelp32Snapshot", [W.DWORD, W.DWORD], W.HANDLE),
            ("Thread32First", [W.HANDLE, C.POINTER(Thread)], W.BOOL),
            ("Thread32Next", [W.HANDLE, C.POINTER(Thread)], W.BOOL),
            ("OpenThread", [W.DWORD, W.BOOL, W.DWORD], W.HANDLE),
            ("ResumeThread", [W.HANDLE], W.DWORD),
        ):
            method = getattr(self.api, name); method.argtypes = args; method.restype = result
        self.handle = self.api.CreateJobObjectW(None, None)
        if not self.handle:
            raise C.WinError(C.get_last_error())
        try:
            limits = Extended(); limits.limits.flags = 0x00002000  # KILL_ON_JOB_CLOSE
            if not self.api.SetInformationJobObject(self.handle, 9, C.byref(limits), C.sizeof(limits)):
                raise C.WinError(C.get_last_error())
            process = self.api.OpenProcess(0x0100 | 0x0001, False, pid)
            if not process:
                raise C.WinError(C.get_last_error())
            try:
                if not self.api.AssignProcessToJobObject(self.handle, process):
                    raise C.WinError(C.get_last_error())
            finally:
                self.api.CloseHandle(process)
            snapshot = self.api.CreateToolhelp32Snapshot(4, 0)
            if snapshot == C.c_void_p(-1).value:
                raise C.WinError(C.get_last_error())
            resumed = False
            try:
                thread = Thread(); thread.size = C.sizeof(thread)
                have = self.api.Thread32First(snapshot, C.byref(thread))
                while have:
                    if thread.owner == pid:
                        handle = self.api.OpenThread(2, False, thread.id)
                        if not handle:
                            raise C.WinError(C.get_last_error())
                        try:
                            if self.api.ResumeThread(handle) == 0xFFFFFFFF:
                                raise C.WinError(C.get_last_error())
                            resumed = True
                        finally:
                            self.api.CloseHandle(handle)
                    have = self.api.Thread32Next(snapshot, C.byref(thread))
            finally:
                self.api.CloseHandle(snapshot)
            if not resumed:
                raise OSError("No suspended child thread could be resumed")
        except BaseException:
            self.close()
            raise

    def close(self):
        if self.handle:
            self.api.CloseHandle(self.handle)
            self.handle = None


def command_for(directory):
    directory = Path(directory).resolve()
    for name in ("ProjectPrimeServer.exe", "ProjectPrimeServer", "ProjectPrime.exe", "ProjectPrime",
                 "MphReadServer.exe", "MphReadServer", "MphRead.exe", "MphRead"):
        candidate = directory / name
        if candidate.is_file():
            if os.name != "nt":
                candidate.chmod(candidate.stat().st_mode | 0o111)
            return [str(candidate)]
    dll = directory / "ProjectPrime.dll"
    if dll.is_file():
        runtime = shutil.which("dotnet")
        if runtime is None:
            raise OSError("dotnet is required for this framework-dependent publish")
        return [runtime, str(dll)]
    raise OSError(f"No runnable Project Prime server binary in {directory}")


def wait_until(predicate, timeout):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return True
        time.sleep(0.05)
    return bool(predicate())


def run(command, startup_timeout=15, exit_timeout=5):
    errors = []
    def check(condition, label):
        print(("ok:   " if condition else "FAIL: ") + label, flush=True)
        if not condition:
            errors.append(label)
    with tempfile.TemporaryDirectory(prefix="prime server smoke ") as work:
        work = Path(work)
        master_log, server_log = work / "master.log", work / "server.log"
        trees = []
        # Reserve distinct ephemeral UDP ports rather than sharing fixed CI ports.
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as master_reservation, socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as server_reservation:
            master_reservation.bind(("127.0.0.1", 0)); server_reservation.bind(("127.0.0.1", 0))
            master_port, server_port = master_reservation.getsockname()[1], server_reservation.getsockname()[1]
        environment = dict(os.environ, PROJECT_PRIME_USER_DATA=str(work / "user data"))
        try:
            with master_log.open("wb") as output:
                master = ProcessTree(command + ["-masterserver", "-port", str(master_port), "-noupdate", "-noautoupdate"], output, environment)
                trees.append(master)
            ready = wait_until(lambda: f"listening on UDP {master_port}" in master_log.read_text(errors="replace")
                               or master.process.poll() is not None, startup_timeout)
            check(ready and master.process.poll() is None, "directory reached startup and remained alive")
            check(f"listening on UDP {master_port}" in master_log.read_text(errors="replace"), "directory bound its selected UDP port")
            with server_log.open("wb") as output:
                server = ProcessTree(command + ["-server", "-port", str(server_port), "-players", "8",
                    "-servername", "CI smoke test", "-server_replays=true", "-server_replay_storage_gb=7",
                    "-server_replay_retention_days=3", "-server_replay_keep_last=9", "-master", "127.0.0.1",
                    "-masterport", str(master_port), "-rotation", str(work / "map rotation.txt"), "-noupdate", "-noautoupdate"], output, environment)
                trees.append(server)
            exited = wait_until(lambda: server.process.poll() is not None, exit_timeout)
            text = server_log.read_text(errors="replace")
            check("canonical server recording enabled; storage 7 GB, retention 3 days, keep newest 9" in text, "replay retention flags reached recorder policy")
            check("cannot run the match:" in text, "server explained its missing-game-files refusal")
            check("Put the game files on this machine and paths.txt beside the binary" in text, "refusal tells an operator how to fix installation")
            check(exited and server.process.returncode == 1, "server actually exited with status 1")
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as query:
                query.settimeout(3)
                try:
                    query.sendto(bytes([18, 3]), ("127.0.0.1", master_port))
                    response, _ = query.recvfrom(2048)
                    check(len(response) >= 3 and response[0] == 19, "directory answered list query")
                    check(len(response) >= 3 and response[1:3] == bytes(2), "directory lists no server that refused startup")
                except socket.timeout:
                    check(False, "directory answered list query")
        finally:
            cleanup_errors = []
            for tree in reversed(trees):
                try:
                    tree.close()
                except (OSError, subprocess.SubprocessError) as error:
                    cleanup_errors.append(error)
            if cleanup_errors:
                raise OSError("Process-tree cleanup failed: " + "; ".join(map(str, cleanup_errors)))
        if errors:
            print("--- server log ---\n" + (server_log.read_text(errors="replace") if server_log.exists() else ""))
            print("--- directory log ---\n" + master_log.read_text(errors="replace"))
    return 1 if errors else 0


def install_signal_handlers():
    def abort(signum, _frame):
        raise SystemExit(128 + signum)
    signal.signal(signal.SIGTERM, abort)
    signal.signal(signal.SIGINT, abort)


def run_preflight(command, asset_paths=None, startup_timeout=15):
    """Reject incomplete roots/rotation before listing; optional licensed-data corruption checks."""
    errors = []
    def check(condition, label):
        print(("ok:   " if condition else "FAIL: ") + label, flush=True)
        if not condition:
            errors.append(label)
    with tempfile.TemporaryDirectory(prefix="prime preflight ") as temporary:
        work = Path(temporary)
        trees = []
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as reservation:
            reservation.bind(("127.0.0.1", 0)); master_port = reservation.getsockname()[1]
        master_log = work / "master.log"
        env = dict(os.environ, PROJECT_PRIME_USER_DATA=str(work / "master data"))
        try:
            with master_log.open("wb") as output:
                master = ProcessTree(command + ["-masterserver", "-port", str(master_port), "-noupdate", "-noautoupdate"], output, env)
                trees.append(master)
            check(wait_until(lambda: f"listening on UDP {master_port}" in master_log.read_text(errors="replace"), startup_timeout),
                  "preflight directory reached startup")
            cases = [("empty-existing-root", "missing or incomplete"),
                     ("truncated-root-model", "missing or incomplete"),
                     ("invalid-later-mode", "invalid multiplayer mode")]
            configured = None
            if asset_paths:
                source = Path(asset_paths).resolve()
                entries = {}
                for line in source.read_text().splitlines()[1:]:
                    if "=" in line:
                        key, value = line.split("=", 1)
                        if value.strip():
                            entries[key.strip()] = (source.parent / value.strip()).resolve()
                configured = next(((key, root) for key, root in entries.items()
                                   if key.startswith("AMH") and (root / "_archives/mp1/mp1_Model.bin").is_file()), None)
                if not configured:
                    raise ValueError("--asset-paths must configure an extracted MPH root containing mp1_Model.bin")
                cases += [("corrupt-selected-model", "Rotation entry 1"), ("unknown-later-map", "Rotation entry 2")]
                try:
                    healthy = run_ready_shutdown(command, work, master_port, configured)
                except (OSError, RuntimeError, subprocess.SubprocessError) as error:
                    print(f"      {error}", flush=True)
                    healthy = False
                check(healthy, "complete assets readiness, active Bye, owned farewell and port reuse")
            for name, expected in cases:
                case = work / name; case.mkdir()
                data = case / "user data"; data.mkdir()
                root = case / "AMHE1"; root.mkdir()
                rotation = case / "map rotation.txt"
                rotation.write_text("MP1 SANCTORUS | Battle | 7 | 7\n")
                key = "AMHE1"
                if name == "truncated-root-model":
                    for relative, size in [("_archives/mp1/mp1_Model.bin", 20),
                                           ("_archives/mp1/mp1_Collision.bin", 84), ("levels/entities/mp1_Ent.bin", 60)]:
                        path = root / relative; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(bytes(size))
                elif name == "invalid-later-mode":
                    rotation.write_text("MP1 SANCTORUS | Battle | 7 | 7\nMP3 PROVING GROUND | 99999 | 7 | 7\n")
                elif name in ("corrupt-selected-model", "unknown-later-map") and configured:
                    key, original = configured
                    if name == "corrupt-selected-model":
                        def link_or_copy(source, target):
                            try:
                                os.link(source, target)
                            except OSError:
                                shutil.copy2(source, target)
                        shutil.copytree(original, root, copy_function=link_or_copy, dirs_exist_ok=True)
                        damaged = root / "_archives/mp1/mp1_Model.bin"
                        # Unlink the copied hard link before replacing its bytes;
                        # the user's original extracted file is never modified.
                        damaged.unlink(); damaged.write_bytes(bytes(100))
                    else:
                        root = original
                        rotation.write_text("MP1 SANCTORUS | Battle | 7 | 7\nUNKNOWN LATER MAP | Battle | 7 | 7\n")
                (data / "paths.txt").write_text("0.35.1.0\n" + key + "=" + str(root) + "\n")
                with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as reservation:
                    reservation.bind(("127.0.0.1", 0)); port = reservation.getsockname()[1]
                log = case / "server.log"
                environment = dict(os.environ, PROJECT_PRIME_USER_DATA=str(data))
                with log.open("wb") as output:
                    server = ProcessTree(command + ["-server", "-port", str(port), "-servername", "preflight " + name,
                        "-rotation", str(rotation), "-usermapdirectory", str(case / "maps"), "-mapdirectory", str(case / "maps"),
                        "-master", "127.0.0.1", "-masterport", str(master_port), "-noserverreplays", "-noupdate", "-noautoupdate"], output, environment)
                    trees.append(server)
                exited = wait_until(lambda: server.process.poll() is not None, startup_timeout)
                text = log.read_text(errors="replace")
                # Native diagnostics may redirect stderr before the top-level
                # rotation-parser exception is reported. Include those owned logs.
                for diagnostic in (data / "logs").glob("*.txt"):
                    text += "\n" + diagnostic.read_text(errors="replace")
                check(exited and server.process.returncode == 1, name + " exits with status 1")
                check(expected in text, name + " reports the failed preflight stage")
                check("authoritative server ready" not in text, name + " never advertises readiness")
                if exited:
                    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as rebound:
                        rebound.bind(("127.0.0.1", port))
                    check(True, name + " releases its UDP port")
                if not exited or expected not in text:
                    print(text)
                response = b""
                with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as query:
                    query.settimeout(.4)
                    # Respect the directory's four-queries/s per-address limit;
                    # short failing startups can otherwise finish inside .25 s.
                    for _ in range(4):
                        query.sendto(bytes([18, 3]), ("127.0.0.1", master_port))
                        try:
                            response, _ = query.recvfrom(2048); break
                        except socket.timeout:
                            continue
                check(len(response) >= 3 and response[0] == 19 and response[1:3] == bytes(2), name + " is absent from directory")
        finally:
            for tree in reversed(trees):
                tree.close()
    return 1 if errors else 0


def run_ready_shutdown(command, work, master_port, configured, timeout=20):
    """Exercise an ordinary real match before the malformed-install regressions."""
    case = work / "ready-shutdown"; case.mkdir()
    data = case / "user data"; data.mkdir()
    key, root = configured
    (data / "paths.txt").write_text("0.35.1.0\n" + key + "=" + str(root) + "\n")
    rotation = case / "map rotation.txt"; rotation.write_text("MP1 SANCTORUS | Battle | 7 | 7\n")
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as reservation:
        reservation.bind(("127.0.0.1", 0)); port = reservation.getsockname()[1]
    import uuid
    token = uuid.uuid4().hex
    env = dict(os.environ, PROJECT_PRIME_USER_DATA=str(data), PROJECT_PRIME_OWNED_SERVER_CONTROL=token)
    log = case / "server.log"
    started = time.monotonic()
    with log.open("wb") as output:
        server = ProcessTree(command + ["-server", "-port", str(port), "-rotation", str(rotation),
            "-usermapdirectory", str(case / "maps"), "-mapdirectory", str(case / "maps"),
            "-master", "127.0.0.1", "-masterport", str(master_port), "-noserverreplays", "-noupdate", "-noautoupdate"],
            output, env, stdin=subprocess.PIPE)
    try:
        if not wait_until(lambda: "authoritative server ready" in log.read_text(errors="replace") or server.process.poll() is not None, timeout):
            raise RuntimeError("Complete assets did not reach readiness.")
        if server.process.poll() is not None:
            raise RuntimeError("Complete assets failed preflight: " + log.read_text(errors="replace"))
        ready = time.monotonic()
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as peer:
            peer.settimeout(.5)
            endpoint = ("127.0.0.1", port)
            hello = bytes([1, 42, 255]) + struct.pack("<I", 0x5A71D17)
            deadline = time.monotonic() + timeout
            joined = False
            while time.monotonic() < deadline and server.process.poll() is None:
                peer.sendto(hello, endpoint)
                try:
                    packet, source = peer.recvfrom(65535)
                    if source == endpoint and (packet[:1] == bytes([2]) or len(packet) > 1 and packet[:2] == bytes([0xD7, 2])):
                        joined = True; break
                except socket.timeout:
                    continue
            if not joined:
                raise RuntimeError("Ready server did not admit an ordinary first player: " + log.read_text(errors="replace"))
            welcome = time.monotonic()
            # A successful listing proves the child's reporter endpoint owns it.
            def listed(expected):
                with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as query:
                    query.settimeout(.4); query.sendto(bytes([18, 3]), ("127.0.0.1", master_port))
                    try:
                        response, _ = query.recvfrom(2048)
                        return len(response) >= 3 and response[0] == 19 and response[1] == expected and response[2] == expected
                    except socket.timeout:
                        return False
            if not wait_until(lambda: listed(1), 5):
                raise RuntimeError("Ready occupied child was not listed.")
            stopping = time.monotonic()
            server.process.stdin.write(("PROJECT_PRIME_STOP " + token + "\n").encode()); server.process.stdin.flush()
            deadline = time.monotonic() + 5
            terminal = False
            while time.monotonic() < deadline:
                try:
                    packet, source = peer.recvfrom(65535)
                    if source == endpoint and (packet[:1] == bytes([5]) or len(packet) > 1 and packet[:2] == bytes([0xD7, 5])):
                        terminal = True; break
                except socket.timeout:
                    if server.process.poll() is not None: break
            if not terminal:
                raise RuntimeError("Active player received no shutdown Bye.")
            server.process.wait(timeout=5)
            stopped = time.monotonic()
            if server.process.returncode != 0:
                raise RuntimeError("Owned stop exited " + str(server.process.returncode) + ": " + log.read_text(errors="replace"))
            if not wait_until(lambda: listed(0), 3):
                raise RuntimeError("Owned stop exited cleanly but its directory entry remained: " + log.read_text(errors="replace"))
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as rebound:
            rebound.bind(("127.0.0.1", port))
        print(f"      readiness {(ready - started) * 1000:.1f} ms; first-player {(welcome - ready) * 1000:.1f} ms; owned stop {(stopped - stopping) * 1000:.1f} ms", flush=True)
        return True
    finally:
        server.close()


if __name__ == "__main__":
    install_signal_handlers()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", nargs="?", default="publish/linux-x64")
    parser.add_argument("--asset-paths", help="Optional licensed paths.txt for corrupt-current/unknown-later stock map regressions")
    args = parser.parse_args()
    try:
        command = command_for(args.directory)
        status = run(command)
        sys.exit(status or run_preflight(command, args.asset_paths))
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
