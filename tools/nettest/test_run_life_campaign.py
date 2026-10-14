"""Lightweight runner integrity/ownership tests; no game, GL, audio or assets."""
import importlib.util
import json
import os
from pathlib import Path
import signal
import struct
import subprocess
import sys
import tempfile
import time
import unittest
import zlib

spec = importlib.util.spec_from_file_location('life_runner', Path(__file__).with_name('run_life_campaign.py'))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


def png(path, pixel, color=2):
    def chunk(kind, payload):
        return struct.pack('>I', len(payload)) + kind + payload + struct.pack('>I', zlib.crc32(kind + payload) & 0xffffffff)
    header = struct.pack('>IIBBBBB', 1, 1, 8, color, 0, 0, 0)
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', header) + chunk(b'IDAT', zlib.compress(b'\0' + bytes(pixel))) + chunk(b'IEND', b''))


class RunnerTests(unittest.TestCase):
    def test_source_and_loaded_binary_qualification_is_exact(self):
        source = {'sha': 'abc', 'status': ''}
        before = {'commit': 'abc', 'tree': 'tree', 'dirty': ''}
        data = {'status': 'PASS_FOCUSED_MERGED_INTEGRATION', 'sourceBefore': before, 'sourceAfter': before.copy(),
                'actualLoadedClient': {'sourceQualifiedPhysicalClient': True, 'sha256': '123', 'mvid': 'mvid'}}
        self.assertEqual(runner.qualification_receipt(data, source, '123')['mvid'], 'mvid')
        with self.assertRaisesRegex(ValueError, 'different head/binary'):
            runner.qualification_receipt(data, source, '456')
        data['sourceAfter']['commit'] = 'new'
        with self.assertRaises(ValueError):
            runner.qualification_receipt(data, source, '123')

    def test_environment_removes_all_game_controls(self):
        environment, removed = runner.clean_environment({'PATH': '/bin', 'PRIME_FOO': '1', 'mph_bar': '2', 'PROJECT_OTHER': '3', 'PROJECT_PRIME_USER_DATA': 'old'}, '/isolated')
        self.assertEqual(environment, {'PATH': '/bin', 'PROJECT_PRIME_USER_DATA': '/isolated'})
        self.assertEqual(len(removed), 4)

    @unittest.skipUnless(os.name == 'posix', 'POSIX owned group qualification')
    def test_successful_owned_process_is_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            receipt = runner.run_owned([sys.executable, '-c', 'print("positive")'], directory, os.environ.copy(), Path(directory) / 'success.log', 5)
            self.assertEqual(receipt['status'], 'PASS')
            self.assertTrue(receipt['ownership']['closed'])
            self.assertFalse(receipt['ownership']['membersAfterClose'])

    @unittest.skipUnless(os.name == 'posix', 'POSIX owned group qualification')
    def test_timeout_closes_term_resistant_descendant(self):
        with tempfile.TemporaryDirectory() as directory:
            child = 'import signal,time; signal.signal(signal.SIGTERM,signal.SIG_IGN); print("descendant",flush=True); time.sleep(60)'
            parent = 'import subprocess,sys,time; subprocess.Popen([sys.executable,"-c",sys.argv[1]]); time.sleep(60)'
            receipt = runner.run_owned([sys.executable, '-c', parent, child], directory, os.environ.copy(), Path(directory) / 'timeout.log', .5)
            self.assertEqual(receipt['status'], 'FAIL')
            self.assertTrue(receipt['timedOut'])
            self.assertGreaterEqual(len(receipt['ownership']['membersBeforeClose']), 2)
            self.assertIn(signal.SIGKILL.name, receipt['ownership']['signals'])
            self.assertTrue(receipt['ownership']['closed'])
            self.assertFalse(runner.live_members(receipt['ownership']['membersAfterClose']))

    @unittest.skipUnless(os.name == 'posix', 'POSIX owned group qualification')
    def test_executor_termination_closes_its_independent_group(self):
        with tempfile.TemporaryDirectory() as directory:
            script = ('import json,sys; sys.path.insert(0,sys.argv[1]); import run_life_campaign as r; '
                      'print(json.dumps(r.run_owned([sys.executable,"-c","import time; time.sleep(60)"], '
                      'sys.argv[2],__import__("os").environ.copy(),sys.argv[2]+"/interrupt.log",60)),flush=True)')
            wrapper = subprocess.Popen([sys.executable, '-c', script, str(Path(__file__).parent), directory], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, start_new_session=True)
            try:
                deadline = time.monotonic() + 5
                while not (Path(directory) / 'interrupt.log').exists() and time.monotonic() < deadline:
                    time.sleep(.02)
                time.sleep(.2)
                wrapper.send_signal(signal.SIGTERM)
                stdout, stderr = wrapper.communicate(timeout=10)
                self.assertEqual(wrapper.returncode, 0, stderr)
                receipt = json.loads(stdout)
                self.assertEqual(receipt['interrupted'], 'SIGTERM')
                self.assertEqual(receipt['status'], 'FAIL')
                self.assertTrue(receipt['ownership']['closed'])
            finally:
                if wrapper.poll() is None:
                    os.killpg(wrapper.pid, signal.SIGKILL)
                    wrapper.wait(timeout=5)

    def test_png_positive_and_black_alpha_controls(self):
        with tempfile.TemporaryDirectory() as directory:
            picture = Path(directory) / 'capture.png'
            png(picture, [20, 30, 40])
            self.assertTrue(runner.png_receipt(picture)['nonblack'])
            png(picture, [0, 0, 0, 255], color=6)
            with self.assertRaisesRegex(ValueError, 'all-black'):
                runner.png_receipt(picture)
            png(picture, [20, 30, 40])
            corrupted = bytearray(picture.read_bytes()); corrupted[-1] ^= 1; picture.write_bytes(corrupted)
            with self.assertRaisesRegex(ValueError, 'checksum'):
                runner.png_receipt(picture)

    def test_respawn_rejects_empty_or_partial_campaign(self):
        with self.assertRaises(ValueError):
            runner.respawn_receipt('RESPAWNRENDER PASS room=MP3 cycles=9/9 checked=0 failures=0')
        with self.assertRaises(ValueError):
            runner.respawn_receipt('RESPAWNRENDER PASS room=MP3 cycles=8/9')
        text = ('RESPAWNRENDER PASS room=MP3 cycles=9/9 deaths=10 respawns=10 checked=300 '
                'invariants=0 gl=0 rendererGl=0 failures=0 controlComparisons=14\n')
        text += ''.join(f'RESPAWNRENDER coverage {family}=1\n' for family in ['opponent', 'self', 'environment', 'beam-source', 'rapid', 'disruption', 'whiteout', 'scoreboard', 'combined'])
        self.assertEqual(len(runner.respawn_receipt(text)['coverage']), 9)

    def test_shell_requires_real_assets_and_two_completion_markers(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            shell = root / 'src/MphRead/Mods/Launcher/Gui/Shell.cs'
            shell.parent.mkdir(parents=True)
            shell.write_text('private static Action<RenderWindow>[] StandardScript Shot(w, "shell-bot-rematch"); Shot(w, "shell-bot-next-map"); private static Action<RenderWindow>[]? _lifecycleTail')
            text = '\n'.join(f'[shelllifecycle] PASS cycle={cycle} spectator/rejoin/Forge/replay/seek/return' for cycle in [1, 2])
            with self.assertRaisesRegex(ValueError, 'assetsReady=False'):
                runner.shell_receipt(root, root, text)
            with self.assertRaises(FileNotFoundError):
                runner.shell_receipt(root, root, text + '\ngame files ready=True')
            names = {'shell-bot-rematch', 'shell-bot-next-map'}
            names.update(f'lifecycle-{cycle}-{stage}' for cycle in [1, 2] for stage in ['spectator', 'rejoin', 'forge', 'replay', 'replay-seek', 'return'])
            for name in names:
                image = root / (name + '.png'); png(image, [20, 30, 40])
                image.with_suffix('.png.evidence.json').write_text(json.dumps({'actualBackend': 'OpenGL', 'width': 1, 'height': 1}))
            receipt = runner.shell_receipt(root, root, text + '\ngame files ready=True')
            self.assertEqual(receipt['expectedCaptures'], 14)
            self.assertEqual(len(receipt['captures']), 14)


if __name__ == '__main__':
    unittest.main()
