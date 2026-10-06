import contextlib
import io
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from dedicated_smoke import ProcessTree, run


class DedicatedSmokeTests(unittest.TestCase):
    def fixture(self, root, mode):
        folder = root / "publish with spaces"; folder.mkdir()
        script = folder / "fake server.py"
        script.write_text('''import os, socket, subprocess, sys, time
args=sys.argv[1:]
port=int(args[args.index("-port")+1])
if "-masterserver" in args:
    with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as sock:
        sock.bind(("127.0.0.1",port))
        print("listening on UDP",port,flush=True)
        while True:
            data,address=sock.recvfrom(1024)
            sock.sendto(bytes([19,0,0]),address)
else:
    assert args[args.index("-servername")+1] == "CI smoke test"
    assert "map rotation.txt" in args[args.index("-rotation")+1]
    child=subprocess.Popen([sys.executable,"-c","import time; time.sleep(1000)"])
    open(sys.argv[1],"w").write(str(child.pid))
    print("canonical server recording enabled; storage 7 GB, retention 3 days, keep newest 9",flush=True)
    print("cannot run the match: missing assets",flush=True)
    print("Put the game files on this machine and paths.txt beside the binary",flush=True)
    if sys.argv[2] == "hang": time.sleep(1000)
    sys.exit(1)
''')
        pid_file = root / "descendant.pid"
        return [sys.executable, str(script), str(pid_file), mode], pid_file

    def gone(self, pid):
        if os.name == "nt":
            result = subprocess.run(["tasklist", "/FI", f"PID eq {pid}", "/FO", "CSV", "/NH"], capture_output=True, text=True, timeout=3)
            return f'"{pid}"' not in result.stdout
        try:
            os.kill(pid, 0)
        except ProcessLookupError:
            return True
        state = subprocess.run(["ps", "-o", "stat=", "-p", str(pid)], capture_output=True, text=True, timeout=3).stdout.strip()
        return not state or state.startswith("Z")

    def assert_clean(self, pid_file):
        pid = int(pid_file.read_text())
        deadline = time.monotonic() + 3
        while time.monotonic() < deadline and not self.gone(pid): time.sleep(.05)
        self.assertTrue(self.gone(pid), f"descendant {pid} survived smoke teardown")

    def test_success_preserves_argv_and_reaps_descendant_of_exited_root(self):
        with tempfile.TemporaryDirectory() as directory:
            command, pid_file = self.fixture(Path(directory), "exit")
            with contextlib.redirect_stdout(io.StringIO()):
                result = run(command, startup_timeout=3, exit_timeout=3)
            self.assertEqual(0, result)
            self.assert_clean(pid_file)

    def test_logged_refusal_without_real_exit_fails_and_reaps_entire_tree(self):
        with tempfile.TemporaryDirectory() as directory:
            command, pid_file = self.fixture(Path(directory), "hang")
            start = time.monotonic()
            with contextlib.redirect_stdout(io.StringIO()):
                result = run(command, startup_timeout=3, exit_timeout=.5)
            self.assertEqual(1, result)
            self.assertLess(time.monotonic()-start, 8)
            self.assert_clean(pid_file)

    def test_supervisor_signal_runs_bounded_tree_cleanup(self):
        if os.name == "nt":
            self.skipTest("Windows console signal behavior is covered by job ownership")
        with tempfile.TemporaryDirectory() as directory:
            command, pid_file = self.fixture(Path(directory), "hang")
            driver = Path(directory) / "driver.py"
            driver.write_text("import sys\nsys.path.insert(0, " + repr(str(Path(__file__).resolve().parent))
                              + ")\nfrom dedicated_smoke import install_signal_handlers,run\ninstall_signal_handlers()\nrun("
                              + repr(command) + ",startup_timeout=3,exit_timeout=100)\n")
            with subprocess.Popen([sys.executable,str(driver)], stdout=subprocess.DEVNULL) as supervisor:
                deadline=time.monotonic()+3
                while not pid_file.exists() and time.monotonic()<deadline:time.sleep(.05)
                self.assertTrue(pid_file.exists())
                supervisor.terminate()
                self.assertEqual(143,supervisor.wait(timeout=8))
            self.assert_clean(pid_file)

    def test_parallel_runs_choose_independent_ports(self):
        from concurrent.futures import ThreadPoolExecutor
        with tempfile.TemporaryDirectory() as directory:
            commands=[]
            for i in range(3):
                folder=Path(directory)/str(i); folder.mkdir()
                commands.append(self.fixture(folder,"exit"))
            with ThreadPoolExecutor(max_workers=3) as workers:
                results=list(workers.map(lambda item: run(item[0],startup_timeout=3,exit_timeout=3),commands))
            self.assertEqual([0,0,0],results)
            for _,pid_file in commands: self.assert_clean(pid_file)


if __name__ == "__main__": unittest.main()
