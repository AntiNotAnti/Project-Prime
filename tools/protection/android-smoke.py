#!/usr/bin/env python3
"""Black-box startup smoke for the exact signed protected Android package."""

from __future__ import annotations

import argparse
import html
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path


PACKAGE = "com.antinotanti.projectprime"
REMOTE_UI_DUMP = "/sdcard/project-prime-protection-ui.xml"
SHELL_MARKERS = (
    "PROJECT PRIME",
    "INITIALIZING",
    "PRESS ANY KEY",
    "Sign in",
    "Settings",
)
FATAL_PATTERNS = (
    re.compile(r"FATAL EXCEPTION", re.IGNORECASE),
    re.compile(r"AndroidRuntime.*(?:fatal|exception)", re.IGNORECASE),
    re.compile(r"JNI DETECTED ERROR", re.IGNORECASE),
    re.compile(r"(?:java\.lang\.)?UnsatisfiedLinkError", re.IGNORECASE),
    re.compile(r"(?:java\.lang\.)?NoClassDefFoundError", re.IGNORECASE),
    re.compile(r"(?:System\.)?(?:TypeLoad|MissingMethod)Exception", re.IGNORECASE),
    re.compile(r"(?:mono|dotnet).*\b(?:fatal|unhandled)\b", re.IGNORECASE),
    re.compile(r"Unhandled Exception", re.IGNORECASE),
    re.compile(r"Fatal signal|SIG(?:SEGV|ABRT)", re.IGNORECASE),
    re.compile(r"(?:KeyboardState|MouseState).*(?:reflection|MissingMethod|MissingField|failed)", re.IGNORECASE),
    re.compile(r"OpenTK.*(?:TypeLoad|MissingMethod|MissingField)Exception", re.IGNORECASE),
    re.compile(r"(?:Avalonia|Skia|EGL|renderer).*(?:fatal|initialization failed)", re.IGNORECASE),
    re.compile(r"Compressed assembly.*larger than when the application was built", re.IGNORECASE),
)
SETTINGS_MARKERS = (
    "CONTROLS",
    "DISPLAY & GRAPHICS",
    "AUDIO",
    "ACCESSIBILITY",
    "ABOUT PROJECT PRIME",
)
NO_CONTENT_MARKERS = (
    "Metroid Prime Hunters game files",
    "No game files yet",
    "Choose your .nds file",
)
MORE_OVERLAY_MARKER = "Routes, account, and connection details."
MAX_TOUCH_ATTEMPTS = 4
TOUCH_RETRY_INTERVAL_SECONDS = 1.0
BOUNDS = re.compile(r"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$")


class SmokeFailure(RuntimeError):
    """A user-facing startup-contract failure."""


def parse_devices(output: str) -> dict[str, str]:
    devices: dict[str, str] = {}
    for line in output.splitlines()[1:]:
        fields = line.strip().split()
        if len(fields) >= 2 and not line.startswith("*"):
            devices[fields[0]] = fields[1]
    return devices


def fatal_lines(logcat: str) -> list[str]:
    return [line for line in logcat.splitlines() if any(pattern.search(line) for pattern in FATAL_PATTERNS)]


def is_foreground(package: str, activities: str, windows: str) -> bool:
    activity_keys = ("mResumedActivity", "topResumedActivity", "ResumedActivity")
    window_keys = ("mCurrentFocus", "mFocusedApp")
    package_folded = package.casefold()
    return any(package_folded in line.casefold() and any(key in line for key in activity_keys)
               for line in activities.splitlines()) or any(
        package_folded in line.casefold() and any(key in line for key in window_keys)
        for line in windows.splitlines())


def shell_marker(ui_xml: str) -> str | None:
    folded = ui_xml.casefold()
    return next((marker for marker in SHELL_MARKERS if marker.casefold() in folded), None)


def node_center(ui_xml: str, label: str) -> tuple[int, int] | None:
    try:
        root = ET.fromstring(ui_xml)
    except ET.ParseError:
        return None
    needle = label.casefold()
    for node in root.iter("node"):
        text = " ".join((node.attrib.get("text", ""), node.attrib.get("content-desc", ""))).casefold()
        match = BOUNDS.match(node.attrib.get("bounds", ""))
        if needle in text and match:
            left, top, right, bottom = (int(value) for value in match.groups())
            if right > left and bottom > top:
                return (left + right) // 2, (top + bottom) // 2
    return None


def settings_marker(ui_xml: str) -> str | None:
    folded = html.unescape(ui_xml).casefold()
    return next((marker for marker in SETTINGS_MARKERS if marker.casefold() in folded), None)


def no_content_marker(ui_xml: str) -> str | None:
    folded = html.unescape(ui_xml).casefold()
    return next((marker for marker in NO_CONTENT_MARKERS if marker.casefold() in folded), None)


def front_shell_navigation(ui_xml: str) -> tuple[str, str | None, tuple[int, int]] | None:
    settings = node_center(ui_xml, "Settings")
    if settings is not None:
        return "settings", None, settings
    no_content = no_content_marker(ui_xml)
    more = node_center(ui_xml, "Open more destinations") or node_center(ui_xml, "More")
    if no_content is not None and more is not None:
        return "no-content-more", no_content, more
    return None


def more_overlay_close_control(ui_xml: str) -> tuple[int, int] | None:
    folded = html.unescape(ui_xml).casefold()
    if MORE_OVERLAY_MARKER.casefold() not in folded:
        return None
    if node_center(ui_xml, "Settings") is None:
        return None
    return node_center(ui_xml, "Close")


def no_content_shell_restored(ui_xml: str) -> bool:
    folded = html.unescape(ui_xml).casefold()
    return (no_content_marker(ui_xml) is not None
            and MORE_OVERLAY_MARKER.casefold() not in folded
            and node_center(ui_xml, "Close") is None)


def retry_accessible_tap(adb: Adb, ui_xml: str, labels: tuple[str, ...],
                         attempts: int, last_attempt: float,
                         now: float) -> tuple[int, float, bool]:
    if attempts >= MAX_TOUCH_ATTEMPTS or now - last_attempt < TOUCH_RETRY_INTERVAL_SECONDS:
        return attempts, last_attempt, False
    control = next((point for label in labels if (point := node_center(ui_xml, label)) is not None), None)
    if control is None:
        return attempts, last_attempt, False
    adb.run("shell", "input", "tap", str(control[0]), str(control[1]), timeout=15)
    return attempts + 1, now, True


def launcher_component(output: str, package: str) -> str | None:
    for line in reversed(output.splitlines()):
        value = line.strip()
        if value.startswith(f"{package}/") and " " not in value:
            return value
    return None


class Adb:
    def __init__(self, executable: str, serial: str):
        self.prefix = [executable, "-s", serial]

    def run(self, *arguments: str, timeout: float = 30, check: bool = True,
            binary: bool = False) -> subprocess.CompletedProcess:
        result = subprocess.run(
            [*self.prefix, *arguments],
            capture_output=True,
            text=not binary,
            timeout=timeout,
            check=False,
        )
        if check and result.returncode != 0:
            stderr = result.stderr if isinstance(result.stderr, str) else result.stderr.decode(errors="replace")
            stdout = result.stdout if isinstance(result.stdout, str) else result.stdout.decode(errors="replace")
            detail = (stderr or stdout).strip()
            raise SmokeFailure(f"adb {' '.join(arguments)} failed: {detail}")
        return result


def select_device(adb_executable: str, requested: str | None) -> str:
    try:
        result = subprocess.run(
            [adb_executable, "devices"], capture_output=True, text=True,
            timeout=15, check=False,
        )
    except (OSError, subprocess.TimeoutExpired) as error:
        raise SmokeFailure(f"cannot query adb devices: {error}") from error
    if result.returncode != 0:
        raise SmokeFailure(f"adb devices failed: {(result.stderr or result.stdout).strip()}")
    devices = parse_devices(result.stdout)
    if requested:
        if devices.get(requested) != "device":
            state = devices.get(requested, "not listed")
            raise SmokeFailure(f"requested Android device {requested!r} is {state}, not ready")
        return requested
    ready = sorted(serial for serial, state in devices.items() if state == "device")
    if len(ready) != 1:
        raise SmokeFailure(f"expected exactly one ready Android device, found {len(ready)}")
    return ready[0]


def read_ui_dump(adb: Adb, deadline: float | None = None) -> str:
    try:
        dump_timeout = bounded_timeout(deadline, 15) if deadline is not None else 15
        dumped = adb.run("shell", "uiautomator", "dump", REMOTE_UI_DUMP,
                         timeout=dump_timeout, check=False)
        if dumped.returncode != 0:
            return ""
        read_timeout = bounded_timeout(deadline, 10) if deadline is not None else 10
        result = adb.run("exec-out", "cat", REMOTE_UI_DUMP,
                         timeout=read_timeout, check=False)
        return result.stdout if result.returncode == 0 else ""
    except (OSError, subprocess.TimeoutExpired) as error:
        raise SmokeFailure(f"cannot read Android accessibility tree: {error}") from error


def confirm_immersive_mode(adb: Adb) -> None:
    result = adb.run(
        "shell", "settings", "put", "secure", "immersive_mode_confirmations", "confirmed",
        timeout=15, check=False,
    )
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip()
        raise SmokeFailure(f"cannot suppress Android immersive-mode confirmation: {detail}")


def dismiss_immersive_confirmation(adb: Adb, ui_xml: str) -> bool:
    confirmation = node_center(ui_xml, "Got it")
    if confirmation is None:
        return False
    adb.run("shell", "input", "tap", str(confirmation[0]), str(confirmation[1]), timeout=15)
    return True


def bounded_timeout(deadline: float, maximum: float) -> float:
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise SmokeFailure("protected Android startup wait expired")
    return max(0.1, min(maximum, remaining))


def capture_diagnostics(adb: Adb, directory: Path, ui_xml: str = "") -> str:
    directory.mkdir(parents=True, exist_ok=True)
    outputs: dict[str, tuple[str, ...]] = {
        "logcat.txt": ("logcat", "-d", "-v", "threadtime"),
        "activity.txt": ("shell", "dumpsys", "activity", "activities"),
        "window.txt": ("shell", "dumpsys", "window", "windows"),
    }
    logcat = ""
    for name, arguments in outputs.items():
        try:
            result = adb.run(*arguments, timeout=20, check=False)
            content = result.stdout or result.stderr or ""
        except (OSError, subprocess.TimeoutExpired) as error:
            content = f"diagnostic capture failed: {error}\n"
        (directory / name).write_text(content, encoding="utf-8", errors="replace")
        if name == "logcat.txt":
            logcat = content
    if not ui_xml:
        try:
            ui_xml = read_ui_dump(adb)
        except (OSError, subprocess.TimeoutExpired, SmokeFailure):
            ui_xml = ""
    (directory / "window.xml").write_text(ui_xml, encoding="utf-8", errors="replace")
    try:
        screenshot = adb.run("exec-out", "screencap", "-p", timeout=20,
                             check=False, binary=True)
        if screenshot.returncode == 0 and screenshot.stdout:
            (directory / "screen.png").write_bytes(screenshot.stdout)
    except (OSError, subprocess.TimeoutExpired):
        pass
    try:
        adb.run("shell", "rm", "-f", REMOTE_UI_DUMP, timeout=10, check=False)
    except (OSError, subprocess.TimeoutExpired):
        pass
    return logcat


def launch(adb: Adb) -> str:
    resolved = adb.run(
        "shell", "cmd", "package", "resolve-activity", "--brief",
        "-a", "android.intent.action.MAIN",
        "-c", "android.intent.category.LAUNCHER", PACKAGE,
        timeout=15, check=False,
    )
    component = launcher_component(resolved.stdout or "", PACKAGE)
    if component:
        adb.run("shell", "am", "start", "-W", "-n", component, timeout=30)
        return component
    adb.run(
        "shell", "monkey", "-p", PACKAGE,
        "-c", "android.intent.category.LAUNCHER", "1", timeout=30,
    )
    return "launcher resolved by monkey"


def install_and_verify_replacement(adb: Adb, apk: Path) -> None:
    exact_apk = str(apk.resolve())
    # The first call supports either a clean device or an existing install.
    # Repeating the exact same `-r` operation proves that this signed package
    # can replace an installed copy instead of only installing when absent.
    adb.run("install", "-r", exact_apk, timeout=180)
    adb.run("install", "-r", exact_apk, timeout=180)
    installed = adb.run("shell", "pm", "path", PACKAGE, timeout=15).stdout.strip()
    if not installed.startswith("package:"):
        raise SmokeFailure(f"{PACKAGE} was not installed from {apk}")


def run_smoke(apk: Path, adb_executable: str, serial: str | None,
              timeout_seconds: float, artifacts: Path) -> None:
    if not apk.is_file():
        raise SmokeFailure(f"signed APK does not exist: {apk}")
    selected = select_device(adb_executable, serial)
    adb = Adb(adb_executable, selected)
    ui_xml = ""
    try:
        install_and_verify_replacement(adb, apk)
        adb.run("shell", "am", "force-stop", PACKAGE, timeout=15)
        confirm_immersive_mode(adb)
        adb.run("logcat", "-c", timeout=15)
        component = launch(adb)

        deadline = time.monotonic() + timeout_seconds
        pid = ""
        foreground = False
        marker: str | None = None
        while time.monotonic() < deadline:
            pid = adb.run("shell", "pidof", PACKAGE,
                          timeout=bounded_timeout(deadline, 10), check=False).stdout.strip()
            activities = adb.run("shell", "dumpsys", "activity", "activities",
                                 timeout=bounded_timeout(deadline, 15), check=False).stdout
            windows = adb.run("shell", "dumpsys", "window", "windows",
                              timeout=bounded_timeout(deadline, 15), check=False).stdout
            foreground = is_foreground(PACKAGE, activities, windows)
            logcat = adb.run("logcat", "-d", "-v", "threadtime",
                             timeout=bounded_timeout(deadline, 15), check=False).stdout
            crashes = fatal_lines(logcat)
            if crashes:
                raise SmokeFailure("fatal Java/JNI/.NET startup signature: " + crashes[-1])
            if pid and foreground:
                ui_xml = read_ui_dump(adb, deadline)
                if dismiss_immersive_confirmation(adb, ui_xml):
                    ui_xml = ""
                    time.sleep(min(0.5, max(0, deadline - time.monotonic())))
                    continue
                marker = shell_marker(ui_xml)
                if marker:
                    break
            time.sleep(min(1, max(0, deadline - time.monotonic())))
        if not pid:
            raise SmokeFailure(f"{PACKAGE} did not remain alive within {timeout_seconds:g}s")
        if not foreground:
            raise SmokeFailure(f"{PACKAGE} did not reach a resumed activity/window within {timeout_seconds:g}s")
        if not marker:
            raise SmokeFailure("UI automation did not expose a Project Prime/Avalonia front-shell marker")

        navigation = ""
        settings_page: str | None = None
        navigation_control = front_shell_navigation(ui_xml)
        if navigation_control is None:
            raise SmokeFailure(
                "front shell exposed neither Settings navigation nor the explicit no-content setup path")
        navigation_kind, no_content, control = navigation_control
        if navigation_kind == "no-content-more":
            assert no_content is not None
            adb.run("shell", "input", "tap", str(control[0]), str(control[1]), timeout=15)
            more_attempts = 1
            last_more_attempt = time.monotonic()
            close_control: tuple[int, int] | None = None
            while time.monotonic() < deadline:
                ui_xml = read_ui_dump(adb, deadline)
                close_control = more_overlay_close_control(ui_xml)
                if close_control is not None:
                    break
                more_attempts, last_more_attempt, _ = retry_accessible_tap(
                    adb, ui_xml, ("Open more destinations", "More"),
                    more_attempts, last_more_attempt, time.monotonic())
                time.sleep(min(1, max(0, deadline - time.monotonic())))
            if close_control is None:
                raise SmokeFailure(
                    "no-content More touch did not expose the labeled destinations overlay "
                    f"after {more_attempts} bounded attempts")
            adb.run(
                "shell", "input", "tap", str(close_control[0]), str(close_control[1]), timeout=15)
            close_attempts = 1
            last_close_attempt = time.monotonic()
            restored = False
            while time.monotonic() < deadline:
                ui_xml = read_ui_dump(adb, deadline)
                restored = no_content_shell_restored(ui_xml)
                if restored:
                    break
                close_attempts, last_close_attempt, _ = retry_accessible_tap(
                    adb, ui_xml, ("Close",), close_attempts,
                    last_close_attempt, time.monotonic())
                time.sleep(min(1, max(0, deadline - time.monotonic())))
            if not restored:
                raise SmokeFailure(
                    "More overlay Close touch did not restore the no-content shell "
                    f"after {close_attempts} bounded attempts")
            navigation = (
                f"no-content={no_content!r}; More overlay opened and closed by touch")
        else:
            adb.run("shell", "input", "tap", str(control[0]), str(control[1]), timeout=15)
            while time.monotonic() < deadline:
                ui_xml = read_ui_dump(adb, deadline)
                settings_page = settings_marker(ui_xml)
                if settings_page:
                    break
                time.sleep(min(1, max(0, deadline - time.monotonic())))
            if not settings_page:
                raise SmokeFailure("touch navigation did not reach the Project Prime Settings screen")
            adb.run("shell", "input", "keyevent", "KEYCODE_BACK", timeout=15)
            navigation = f"settings={settings_page!r}; touch/back input exercised"

        pid = adb.run("shell", "pidof", PACKAGE, timeout=10, check=False).stdout.strip()
        if not pid:
            raise SmokeFailure("Project Prime exited while navigating the protected front shell")
        activities = adb.run(
            "shell", "dumpsys", "activity", "activities", timeout=15, check=False).stdout
        windows = adb.run(
            "shell", "dumpsys", "window", "windows", timeout=15, check=False).stdout
        if not is_foreground(PACKAGE, activities, windows):
            raise SmokeFailure("Project Prime was not foreground after Settings/back navigation")

        final_logcat = capture_diagnostics(adb, artifacts, ui_xml)
        crashes = fatal_lines(final_logcat)
        if crashes:
            raise SmokeFailure("fatal Java/JNI/.NET startup signature: " + crashes[-1])
        print(
            f"Protected Android smoke passed on {selected}: {component}; pid={pid}; "
            f"UI={marker!r}; {navigation}.")
        print(f"Diagnostics: {artifacts}")
    except (OSError, subprocess.TimeoutExpired, SmokeFailure) as error:
        capture_diagnostics(adb, artifacts, ui_xml)
        raise SmokeFailure(f"{error}; diagnostics: {artifacts}") from error


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apk", required=True, type=Path,
                        help="Exact signed APK to install and launch")
    parser.add_argument("--serial", help="Exact adb device/emulator serial")
    parser.add_argument("--adb", default="adb", help="adb executable")
    parser.add_argument("--timeout", type=float, default=45,
                        help="Bounded startup wait in seconds (default: 45)")
    parser.add_argument("--artifacts", required=True, type=Path,
                        help="Directory for logcat, UI, activity, window, and screenshot diagnostics")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.timeout <= 0 or args.timeout > 120:
        print("android-smoke: --timeout must be greater than 0 and at most 120", file=sys.stderr)
        return 2
    try:
        run_smoke(args.apk, args.adb, args.serial, args.timeout, args.artifacts.resolve())
        return 0
    except SmokeFailure as error:
        print(f"android-smoke: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
