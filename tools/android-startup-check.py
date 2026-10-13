#!/usr/bin/env python3
"""Install the delivered signed APK and prove its normal launcher survives startup.

Use --boot-emulator on a physical ARM64 Mac with HVF; hosted macOS ARM64
runners lack nested virtualization. Without it, use an already booted device.
Only diagnostic evidence is written to --output; emulator disks use temporary
storage removed after shutdown. Each run clears the previous PASS result.
No Debug rebuild, acceptance activity, game files, or run-as access is involved.
"""
import argparse
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time

PACKAGE = 'com.projectprime.game'
CRASH = re.compile(r'assertion.*(?:failed|not met|should not be reached)|(?:mono|monodroid).*assert|FATAL EXCEPTION|Fatal signal|SIGABRT|SIGSEGV|Unhandled Exception', re.I)


THREADTIME = re.compile(r'^\d\d-\d\d\s+\d\d:\d\d:\d\d\.\d+\s+(\d+)\s+\d+\s+[VDIWEF]\s+([^:]+):\s*(.*)$')


def app_fatal_lines(text, known_pid=''):
    """Scope fatal checks to app processes and their debuggerd crash reporters."""
    rows = []
    app_pids = set(known_pid.split())
    reporters = set()
    package = re.escape(PACKAGE)
    for line in text.splitlines():
        match = THREADTIME.match(line)
        if not match:
            continue
        pid, tag, body = match.groups()
        rows.append((pid, line))
        started = re.search(r'\bStart proc (\d+):' + package + r'(?:/|\s|$)', body)
        runtime = re.search(r'\bProcess:\s*' + package + r',\s*PID:\s*(\d+)', body)
        native = re.search(r'\bpid:\s*(\d+),.*>>>\s*' + package + r'\s*<<<', body)
        for identity in (started, runtime, native):
            if identity:
                app_pids.add(identity.group(1))
        if tag.strip() == 'DEBUG' and (
                re.search(r'\bCmdline:\s*' + package + r'\s*$', body) or native):
            reporters.add(pid)
    return [line for pid, line in rows if pid in app_pids | reporters and CRASH.search(line)]


def run(args, timeout=30):
    return subprocess.check_output(args, stderr=subprocess.STDOUT, timeout=timeout, text=True)


def check_startup(adb, apk, output, seconds=60, interval=2):
    output.mkdir(parents=True, exist_ok=True)
    (output / 'result.txt').unlink(missing_ok=True)
    def command(*args):
        return run([adb, *args])
    def process_id():
        try:
            return command('shell', 'pidof', PACKAGE).strip()
        except subprocess.CalledProcessError as error:
            # Android pidof returns 1 when the package has no running process.
            if error.returncode == 1:
                return ''
            raise
    pid = ''
    def logs():
        text = command('logcat', '-d', '-v', 'threadtime')
        (output / 'logcat.txt').write_text(text)
        fatal = app_fatal_lines(text, pid)
        if fatal:
            raise RuntimeError('Android startup emitted a managed/native fatal error or Mono assertion: ' + fatal[0])
    if command('shell', 'getprop', 'ro.product.cpu.abi').strip() != 'arm64-v8a':
        raise RuntimeError('Startup acceptance requires an ARM64 Android device')
    command('install', '-r', str(apk))
    command('shell', 'am', 'force-stop', PACKAGE)
    command('logcat', '-c')
    component = command('shell', 'cmd', 'package', 'resolve-activity', '--brief',
                        '-a', 'android.intent.action.MAIN', '-c', 'android.intent.category.LAUNCHER', PACKAGE).strip().splitlines()[-1]
    if not component.startswith(PACKAGE + '/'):
        raise RuntimeError('Could not resolve the normal launcher activity')
    launch = command('shell', 'am', 'start', '-W', '-n', component)
    (output / 'launch.txt').write_text(launch)
    if 'Status: ok' not in launch or 'Error:' in launch:
        logs()
        raise RuntimeError('Android did not start the launcher successfully')
    pid = process_id()
    if not pid:
        logs()
        raise RuntimeError('Launcher process exited immediately')
    deadline = time.monotonic() + seconds
    while True:
        logs()
        current = process_id()
        if current != pid:
            logs()
            raise RuntimeError('Launcher process terminated or restarted unexpectedly')
        if time.monotonic() >= deadline:
            break
        time.sleep(interval)
    activity = command('shell', 'dumpsys', 'activity', 'activities')
    (output / 'activities.txt').write_text(activity)
    if not any(('ResumedActivity' in line or 'topResumedActivity' in line) and component in line for line in activity.splitlines()):
        raise RuntimeError('Normal launcher is not the resumed activity after startup')
    (output / 'result.txt').write_text(f'PASS: ARM64 signed Release launcher stayed alive as PID {pid} for {seconds}s\n')


def configure_smoke_avd(config):
    """Keep this launcher-only device small; it never stores extracted game data."""
    values = {'disk.dataPartition.size': '1G', 'sdcard.size': '128M'}
    lines = config.read_text().splitlines()
    lines = [line for line in lines if line.split('=', 1)[0] not in values]
    lines.extend(f'{key}={value}' for key, value in values.items())
    config.write_text('\n'.join(lines) + '\n')


def wait_for_emulator(adb, emulator, timeout=120, interval=2):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if emulator.poll() is not None:
            raise RuntimeError('ARM64 emulator exited before adb became ready; see emulator.log')
        try:
            remaining = max(0.1, deadline - time.monotonic())
            ready = run([adb, 'get-state'], timeout=min(5, remaining)).strip() == 'device'
        except subprocess.SubprocessError:
            ready = False
        if emulator.poll() is not None:
            raise RuntimeError('ARM64 emulator exited before adb became ready; see emulator.log')
        if ready:
            return
        time.sleep(min(interval, max(0, deadline - time.monotonic())))
    raise RuntimeError('ARM64 emulator did not appear in adb within 120 seconds; see emulator.log')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('apk', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--seconds', type=int, default=60)
    parser.add_argument('--boot-emulator', action='store_true')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / 'result.txt').unlink(missing_ok=True)
    if not args.apk.is_file() or args.seconds < 1:
        parser.error('Provide an existing signed Release APK and positive observation duration')
    sdk_home = os.environ.get('ANDROID_HOME') or os.environ.get('ANDROID_SDK_ROOT')
    sdk = Path(sdk_home) if sdk_home else None
    adb = str(sdk / 'platform-tools/adb') if sdk else 'adb'
    emulator = None
    emulator_log = None
    avd_home = None
    try:
        if args.boot_emulator:
            if sdk is None:
                raise RuntimeError('Set ANDROID_HOME to the configured Android SDK')
            os.environ['ANDROID_SERIAL'] = 'emulator-5580'
            if run(['uname', '-m']).strip() != 'arm64':
                raise RuntimeError('ARM64 emulator requires an ARM64 host')
            tools = sdk / 'cmdline-tools/latest/bin'
            image = 'system-images;android-36;google_apis;arm64-v8a'
            run([str(tools / 'sdkmanager'), '--sdk_root=' + str(sdk), 'platform-tools', 'emulator', image], timeout=480)
            avd_home = tempfile.TemporaryDirectory(prefix='prime-startup-avds-')
            os.environ['ANDROID_AVD_HOME'] = avd_home.name
            subprocess.run([str(tools / 'avdmanager'), 'create', 'avd', '--force', '--name', 'prime-startup', '--package', image], input='no\n', text=True, check=True, timeout=60)
            configure_smoke_avd(Path(avd_home.name) / 'prime-startup.avd/config.ini')
            emulator_log = (args.output / 'emulator.log').open('w')
            emulator = subprocess.Popen([str(sdk / 'emulator/emulator'), '-avd', 'prime-startup', '-port', '5580', '-no-window', '-no-audio', '-no-snapshot', '-no-boot-anim', '-gpu', 'swiftshader', '-accel', 'on'], stdout=emulator_log, stderr=subprocess.STDOUT)
            wait_for_emulator(adb, emulator)
            deadline = time.monotonic() + 300
            while run([adb, 'shell', 'getprop', 'sys.boot_completed']).strip() != '1':
                if emulator.poll() is not None or time.monotonic() > deadline:
                    raise RuntimeError('ARM64 emulator failed to boot within five minutes')
                time.sleep(2)
        check_startup(adb, args.apk, args.output, args.seconds)
        print((args.output / 'result.txt').read_text())
    finally:
        try:
            (args.output / 'logcat.txt').write_text(run([adb, 'logcat', '-d', '-v', 'threadtime'], timeout=10))
        except (OSError, subprocess.SubprocessError):
            pass
        try:
            if emulator:
                emulator.terminate()
                try:
                    emulator.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    emulator.kill()
                    emulator.wait(timeout=10)
        finally:
            if emulator_log:
                emulator_log.close()
            if avd_home:
                avd_home.cleanup()


if __name__ == '__main__':
    main()
