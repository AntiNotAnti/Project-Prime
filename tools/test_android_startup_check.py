import importlib.util
from pathlib import Path
import tempfile
import sys
import unittest
from unittest.mock import Mock, patch

spec = importlib.util.spec_from_file_location('startup', Path(__file__).with_name('android-startup-check.py'))
startup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(startup)
COMPONENT = startup.PACKAGE + '/crc64.MainActivity'


def row(pid, tag, message):
    return f'10-12 22:10:20.147 {pid:5} {pid:5} F {tag:<15}: {message}\n'


class StartupAcceptanceTests(unittest.TestCase):
    def fixture(self, scenario):
        calls = []
        def fake_adb(args, timeout=30):
            command = args[1:]
            calls.append(command)
            if command[:3] == ['shell', 'getprop', 'ro.product.cpu.abi']:
                return 'x86_64\n' if scenario == 'wrong-abi' else 'arm64-v8a\n'
            if command[:4] == ['shell', 'cmd', 'package', 'resolve-activity']:
                return COMPONENT + '\n'
            if command[:3] == ['shell', 'am', 'start']:
                return 'Status: ok\n'
            if command[:2] == ['shell', 'pidof']:
                count = sum(c[:2] == ['shell', 'pidof'] for c in calls)
                if scenario == 'terminated' and count > 1:
                    return ''
                if scenario == 'restarted' and count > 1:
                    return '124\n'
                return '123\n'
            if command[:2] == ['logcat', '-d']:
                return {'mono': row(123, 'mono', '* Assertion at mini-generic-sharing.c:123, condition not met'),
                        'native': row(123, 'libc', 'Fatal signal 6 (SIGABRT) in tid 123'),
                        'managed': row(123, 'AndroidRuntime', 'FATAL EXCEPTION: main'),
                        'unrelated': row(444, 'AndroidRuntime', 'FATAL EXCEPTION: main') + row(444, 'AndroidRuntime', 'Process: unrelated.app, PID: 444')}.get(scenario, row(123, 'Launcher', 'launcher ready'))
            if command[:3] == ['shell', 'dumpsys', 'activity']:
                return '' if scenario == 'not-resumed' else 'mResumedActivity: ActivityRecord ' + COMPONENT
            return 'Success\n'
        return fake_adb, calls

    def test_normal_signed_apk_launcher(self):
        fake, calls = self.fixture('success')
        with tempfile.TemporaryDirectory() as folder, patch.object(startup, 'run', fake):
            startup.check_startup('adb', Path('/release-Signed.apk'), Path(folder), seconds=0)
            self.assertTrue((Path(folder) / 'result.txt').read_text().startswith('PASS:'))
        self.assertIn(['install', '-r', '/release-Signed.apk'], calls)
        self.assertIn(['shell', 'am', 'start', '-W', '-n', COMPONENT], calls)

    def test_real_adb_subprocess_pidof_exit_one_preserves_failure_reason(self):
        # Exercise subprocess.check_output, including Android pidof's exit status.
        for crash, later, expected in [(False, False, 'exited immediately'), (True, False, 'Mono assertion'), (False, True, 'terminated or restarted')]:
            with self.subTest(crash=crash, later=later), tempfile.TemporaryDirectory() as folder:
                output = Path(folder) / 'evidence'
                fake = Path(folder) / 'adb'
                pid_action = "sys.exit(1)"
                if later:
                    counter = str(Path(folder) / 'pid-seen')
                    pid_action = f"seen = Path({counter!r})\n    if seen.exists(): sys.exit(1)\n    seen.write_text('1')\n    print('123')"
                fake.write_text('#!' + sys.executable + '\n' + """import sys
from pathlib import Path
args = sys.argv[1:]
if args[:2] == ['shell', 'getprop']:
    print('arm64-v8a')
elif args[:4] == ['shell', 'cmd', 'package', 'resolve-activity']:
    print('com.projectprime.game/crc64.MainActivity')
elif args[:3] == ['shell', 'am', 'start']:
    print('Status: ok')
elif args[:2] == ['shell', 'pidof']:
    PID_ACTION
elif args[:2] == ['logcat', '-d']:
    print(LOG_TEXT)
else:
    print('Success')
""".replace('PID_ACTION', pid_action).replace('LOG_TEXT', repr(row(900, 'ActivityManager', 'Start proc 123:com.projectprime.game/u0a123 for activity') + (row(123, 'mono', "* Assertion at object.c:8004, condition `size >= 0' not met") if crash else row(123, 'Launcher', 'launcher stopped')))))
                fake.chmod(0o755)
                with self.assertRaisesRegex(RuntimeError, expected):
                    startup.check_startup(str(fake), Path('/release-Signed.apk'), output, seconds=0)
                self.assertIn('Assertion' if crash else 'launcher stopped', (output / 'logcat.txt').read_text())
                self.assertFalse((output / 'result.txt').exists())

    def test_unrelated_process_crash_does_not_fail_launcher(self):
        fake, _ = self.fixture('unrelated')
        with tempfile.TemporaryDirectory() as folder, patch.object(startup, 'run', fake):
            startup.check_startup('adb', Path('/release-Signed.apk'), Path(folder), seconds=0)
            self.assertTrue((Path(folder) / 'result.txt').exists())
            self.assertIn('unrelated.app', (Path(folder) / 'logcat.txt').read_text())

    def test_scoped_fatal_log_identities(self):
        unrelated = row(444, 'AndroidRuntime', 'FATAL EXCEPTION: main') + row(444, 'AndroidRuntime', 'Process: unrelated.app, PID: 444')
        self.assertEqual([], startup.app_fatal_lines(unrelated, '123'))
        self.assertEqual([], startup.app_fatal_lines(unrelated))
        cases = [
            row(900, 'ActivityManager', 'Start proc 123:com.projectprime.game/u0a123 for activity') + row(123, 'mono', '* Assertion at object.c:8004, condition not met'),
            row(123, 'AndroidRuntime', 'FATAL EXCEPTION: main') + row(123, 'AndroidRuntime', 'Process: com.projectprime.game, PID: 123'),
            row(999, 'DEBUG', 'Cmdline: com.projectprime.game') + row(999, 'DEBUG', "Abort message: '* Assertion at object.c:8004, condition not met'"),
            row(999, 'DEBUG', 'pid: 123, tid: 123, name: ojectprime.game >>> com.projectprime.game <<<') + row(999, 'DEBUG', 'signal 6 (SIGABRT), code -1'),
        ]
        for text in cases:
            with self.subTest(text=text):
                self.assertEqual(1, len(startup.app_fatal_lines(unrelated + text)))
        # Capture assertions before the identity marker in a buffered snapshot.
        text = row(123, 'monodroid', 'Mono assertion failed') + row(999, 'DEBUG', 'Cmdline: com.projectprime.game') + row(999, 'DEBUG', 'pid: 123, tid: 123 >>> com.projectprime.game <<<')
        self.assertEqual(1, len(startup.app_fatal_lines(text)))

    def test_failed_rerun_removes_stale_pass(self):
        fake, _ = self.fixture('wrong-abi')
        with tempfile.TemporaryDirectory() as folder, patch.object(startup, 'run', fake):
            output = Path(folder)
            (output / 'result.txt').write_text('PASS: previous run')
            with self.assertRaises(RuntimeError):
                startup.check_startup('adb', Path('/release-Signed.apk'), output, seconds=0)
            self.assertFalse((output / 'result.txt').exists())

    def test_emulator_disks_removed_and_log_closed_after_startup_failure(self):
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / 'evidence'
            output.mkdir()
            (output / 'result.txt').write_text('PASS: stale')
            apk = Path(folder) / 'release-Signed.apk'
            apk.touch()
            emulator = Mock()
            emulator.poll.return_value = None
            captured = {}
            def start_emulator(args, stdout, stderr):
                captured['log'] = stdout
                captured['avds'] = Path(startup.os.environ['ANDROID_AVD_HOME'])
                self.assertTrue(captured['avds'].exists())
                self.assertFalse(captured['avds'].is_relative_to(output))
                (captured['avds'] / 'guest-disk.img').touch()
                return emulator
            def command(args, timeout=30):
                if args == ['uname', '-m']: return 'arm64'
                if args[-1] == 'get-state': return 'device'
                return '1'
            def create_avd(*args, **kwargs):
                config = Path(startup.os.environ['ANDROID_AVD_HOME']) / 'prime-startup.avd/config.ini'
                config.parent.mkdir()
                config.write_text('disk.dataPartition.size=6G\nsdcard.size=512M\n')
            with patch.dict(startup.os.environ, {'ANDROID_HOME': '/fake-sdk'}), \
                    patch.object(sys, 'argv', ['startup', str(apk), '--output', str(output), '--boot-emulator']), \
                    patch.object(startup, 'run', command), \
                    patch.object(startup.subprocess, 'run', create_avd), \
                    patch.object(startup.subprocess, 'Popen', start_emulator), \
                    patch.object(startup, 'check_startup', side_effect=RuntimeError('fixture startup failed')):
                with self.assertRaisesRegex(RuntimeError, 'fixture startup failed'):
                    startup.main()
            self.assertFalse(captured['avds'].exists())
            self.assertTrue(captured['log'].closed)
            emulator.terminate.assert_called_once()
            emulator.wait.assert_called_once_with(timeout=10)
            self.assertFalse((output / 'result.txt').exists())
            self.assertFalse((output / 'avds').exists())

    def test_smoke_avd_storage_config_preserves_other_settings(self):
        with tempfile.TemporaryDirectory() as folder:
            config = Path(folder) / 'config.ini'
            config.write_text('hw.ramSize=2048\ndisk.dataPartition.size=6G\nsdcard.size=512M\n')
            startup.configure_smoke_avd(config)
            text = config.read_text()
            self.assertIn('hw.ramSize=2048', text)
            self.assertIn('disk.dataPartition.size=1G', text)
            self.assertIn('sdcard.size=128M', text)
            self.assertEqual(1, text.count('disk.dataPartition.size='))

    def test_adb_readiness_fails_immediately_when_emulator_exits(self):
        emulator = Mock()
        emulator.poll.return_value = 1
        with patch.object(startup, 'run') as command, self.assertRaisesRegex(RuntimeError, 'exited before adb'):
            startup.wait_for_emulator('adb', emulator)
        command.assert_not_called()

    def test_adb_readiness_detects_exit_during_adb_probe(self):
        emulator = Mock()
        emulator.poll.side_effect = [None, 1]
        with patch.object(startup, 'run', side_effect=startup.subprocess.TimeoutExpired('adb', 5)), self.assertRaisesRegex(RuntimeError, 'exited before adb'):
            startup.wait_for_emulator('adb', emulator)

    def test_startup_failure_classes(self):
        for scenario in ['wrong-abi', 'terminated', 'restarted', 'mono', 'native', 'managed', 'not-resumed']:
            with self.subTest(scenario=scenario), tempfile.TemporaryDirectory() as folder:
                fake, _ = self.fixture(scenario)
                with patch.object(startup, 'run', fake), self.assertRaises(RuntimeError):
                    startup.check_startup('adb', Path('/release-Signed.apk'), Path(folder), seconds=0)
                self.assertFalse((Path(folder) / 'result.txt').exists())


if __name__ == '__main__':
    unittest.main()
