#!/usr/bin/env python3
"""Run the explicitly enabled native Android framework fixtures on an emulator.

The APK must use MphReadRmlUiAndroidCheck=true. The checks execute production
render/input/page ownership; no cartridge files are embedded or fabricated.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apk", required=True, type=Path)
    parser.add_argument("--adb", default="adb")
    parser.add_argument("--serial", required=True)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--hud", action="store_true")
    parser.add_argument("--timeout", type=int, default=120)
    args = parser.parse_args()
    if not 10 <= args.timeout <= 600:
        parser.error("--timeout must be between 10 and 600 seconds")
    args.output.mkdir(parents=True, exist_ok=True)
    package = "com.projectprime.game"
    external_reports = f"/sdcard/Android/data/{package}/files"

    def adb(*command, timeout=15, check=True):
        result = subprocess.run([args.adb, "-s", args.serial, *command],
                                capture_output=True, timeout=timeout)
        if check and result.returncode:
            raise RuntimeError(result.stderr.decode(errors="replace") or result.stdout.decode(errors="replace"))
        return result.stdout.decode(errors="replace").strip()

    reports = {
        "rmlui-android-gles-check.txt": "PASS ES3 ",
        "rmlui-android-ime-check.txt": "PASS Android InputConnection ",
        "rmlui-android-accessibility-check.txt": "PASS Android accessibility ",
    }
    if args.hud:
        reports["rmlui-android-hud-check.txt"] = "PASS Android HUD "
    try:
        adb("install", "-r", str(args.apk.resolve()), timeout=120)
        adb("shell", "am", "force-stop", package)
        adb("shell", "rm", "-f", *[external_reports + "/" + name for name in reports])
        adb("shell", "run-as", package, "rm", "-f", *["files/" + name for name in reports], check=False)
        adb("logcat", "-c")
        resolved = adb("shell", "cmd", "package", "resolve-activity", "--brief", "-a",
                       "android.intent.action.MAIN", "-c", "android.intent.category.LAUNCHER", package)
        component = next((line.strip() for line in reversed(resolved.splitlines())
                          if line.strip().startswith(package + "/")), None)
        if component is None:
            raise RuntimeError("The APK has no resolvable native launcher Activity")
        command = ["shell", "am", "start", "-W", "-n", component,
                   "--es", "rmlui-renderer", "opengl",
                   "--ez", "rmlui-ime-check", "true", "--ez", "rmlui-a11y-check", "true"]
        if args.hud:
            command += ["--ez", "rmlui-hud-check", "true"]
        print(adb(*command, timeout=60), flush=True)
        completed = {}
        deadline = time.monotonic() + args.timeout
        while time.monotonic() < deadline and len(completed) < len(reports):
            logs = adb("logcat", "-d", timeout=20)
            if re.search(r"FATAL EXCEPTION|(?:InputConnection|Accessibility|HUD) check FAILED", logs):
                raise RuntimeError("The Android runtime reported a failed framework fixture")
            for name, prefix in reports.items():
                if name in completed:
                    continue
                report = adb("shell", "cat", external_reports + "/" + name, check=False)
                if not report.startswith(prefix):
                    report = adb("shell", "run-as", package, "cat", "files/" + name, check=False)
                if report.startswith(prefix):
                    if not re.search(r"\b[1-9][0-9]* assertions\b", report):
                        raise RuntimeError("A framework report has no positive assertion count: " + name)
                    completed[name] = report
                    (args.output / name).write_text(report + "\n")
                    print(report, flush=True)
            time.sleep(1)
        if len(completed) != len(reports):
            raise TimeoutError("Missing fresh runtime reports: " + ", ".join(set(reports) - set(completed)))
        adb("shell", "screencap", "-p", "/sdcard/prime-rmlui-check.png")
        adb("pull", "/sdcard/prime-rmlui-check.png", str(args.output / "native-home.png"))
        pid = adb("shell", "pidof", package)
        adb("shell", "input", "keyevent", "KEYCODE_HOME")
        time.sleep(1)
        adb("shell", "am", "start", "-W", "-n", component, timeout=60)
        time.sleep(1)
        if not pid or adb("shell", "pidof", package) != pid:
            raise RuntimeError("Background/resume unexpectedly replaced the native process")
        adb("shell", "screencap", "-p", "/sdcard/prime-rmlui-check.png")
        adb("pull", "/sdcard/prime-rmlui-check.png", str(args.output / "native-resumed.png"))
        manifest = {"apk": args.apk.name, "apk_sha256": hashlib.sha256(args.apk.read_bytes()).hexdigest(),
                    "serial": args.serial, "activity": component, "renderer_requested": "opengl", "reports": completed,
                    "same_process_after_background_resume": True,
                    "physical_ime_talkback_and_gameplay_certified": False}
        (args.output / "runtime-evidence.json").write_text(json.dumps(manifest, indent=2) + "\n")
        print("PASS Android native framework fixture collection and process background/resume", flush=True)
    finally:
        try:
            (args.output / "logcat.txt").write_text(adb("logcat", "-d", timeout=20))
        except (OSError, subprocess.SubprocessError, RuntimeError) as error:
            (args.output / "logcat-unavailable.txt").write_text(str(error) + "\n")


if __name__ == "__main__":
    try:
        main()
    except (OSError, subprocess.SubprocessError, RuntimeError, TimeoutError) as error:
        print("error: Android native runtime acceptance failed: " + str(error), file=sys.stderr)
        sys.exit(1)
