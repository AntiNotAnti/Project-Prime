"""Focused tests for the protected Android package smoke contract."""

import importlib.util
from pathlib import Path
import subprocess
import sys
from types import SimpleNamespace
import unittest


ROOT = Path(__file__).resolve().parents[2]
SMOKE_PATH = ROOT / "tools/protection/android-smoke.py"
SPEC = importlib.util.spec_from_file_location("android_protection_smoke", SMOKE_PATH)
SMOKE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = SMOKE
SPEC.loader.exec_module(SMOKE)


class AndroidProtectionSmokeTests(unittest.TestCase):
    def test_device_parser_preserves_non_ready_state(self):
        devices = SMOKE.parse_devices(
            "List of devices attached\n"
            "emulator-5554\tdevice product:sdk_gphone64_x86_64\n"
            "phone-1\tunauthorized\n"
        )
        self.assertEqual({"emulator-5554": "device", "phone-1": "unauthorized"}, devices)

    def test_foreground_requires_package_on_resumed_or_focused_line(self):
        activities = "mResumedActivity: ActivityRecord{a com.antinotanti.projectprime/.MainActivity}"
        self.assertTrue(SMOKE.is_foreground(SMOKE.PACKAGE, activities, ""))
        self.assertFalse(SMOKE.is_foreground(SMOKE.PACKAGE, "Task com.antinotanti.projectprime", ""))

    def test_shell_marker_accepts_front_screen_text(self):
        xml = '<node class="android.view.View" text="PRESS ANY KEY TO CONTINUE" />'
        self.assertEqual("PRESS ANY KEY", SMOKE.shell_marker(xml))
        self.assertIsNone(SMOKE.shell_marker('<node text="Another application" />'))

    def test_accessibility_bounds_drive_settings_touch_navigation(self):
        xml = (
            '<hierarchy><node text="Settings" content-desc="" '
            'bounds="[100,200][300,260]" /></hierarchy>')
        self.assertEqual((200, 230), SMOKE.node_center(xml, "Settings"))
        self.assertIsNone(SMOKE.node_center("<broken", "Settings"))
        self.assertEqual(
            "DISPLAY & GRAPHICS",
            SMOKE.settings_marker('<node text="DISPLAY &amp; GRAPHICS" />'))

    def test_no_content_setup_uses_accessible_more_navigation_path(self):
        xml = (
            '<hierarchy><node text="PROJECT PRIME" bounds="[0,0][1,1]" />'
            '<node text="No game files yet" bounds="[20,40][300,80]" />'
            '<node text="" content-desc="Open more destinations" '
            'bounds="[40,700][200,780]" /></hierarchy>')
        self.assertEqual("No game files yet", SMOKE.no_content_marker(xml))
        self.assertEqual(
            ("no-content-more", "No game files yet", (120, 740)),
            SMOKE.front_shell_navigation(xml))
        self.assertIsNone(SMOKE.front_shell_navigation(
            '<hierarchy><node text="Back" bounds="[40,700][200,780]" /></hierarchy>'))

    def test_no_content_more_overlay_requires_marker_settings_and_close(self):
        overlay = (
            '<hierarchy><node text="Routes, account, and connection details." '
            'bounds="[0,0][1,1]" />'
            '<node text="Settings" bounds="[10,20][110,60]" />'
            '<node text="Close" bounds="[40,700][200,780]" /></hierarchy>')
        self.assertEqual((120, 740), SMOKE.more_overlay_close_control(overlay))
        self.assertIsNone(SMOKE.more_overlay_close_control(
            '<hierarchy><node text="Settings" bounds="[10,20][110,60]" />'
            '<node text="Close" bounds="[40,700][200,780]" /></hierarchy>'))

    def test_no_content_shell_restoration_requires_overlay_disappearance(self):
        shell = '<hierarchy><node text="No game files yet" bounds="[0,0][10,10]" /></hierarchy>'
        overlay = (
            '<hierarchy><node text="No game files yet" bounds="[0,0][10,10]" />'
            '<node text="Routes, account, and connection details." bounds="[0,0][1,1]" />'
            '<node text="Close" bounds="[0,0][10,10]" /></hierarchy>')
        self.assertTrue(SMOKE.no_content_shell_restored(shell))
        self.assertFalse(SMOKE.no_content_shell_restored(overlay))

    def test_accessible_touch_retry_handles_dropped_tap_and_caps_attempts(self):
        class RecordingAdb:
            def __init__(self):
                self.calls = []

            def run(self, *arguments, **_options):
                self.calls.append(arguments)
                return SimpleNamespace(returncode=0, stdout="", stderr="")

        adb = RecordingAdb()
        xml = '<hierarchy><node text="More" bounds="[100,200][300,280]" /></hierarchy>'
        attempts, last_attempt, tapped = SMOKE.retry_accessible_tap(
            adb, xml, ("Open more destinations", "More"), 1, 10.0, 11.5)
        self.assertEqual((2, 11.5, True), (attempts, last_attempt, tapped))
        self.assertEqual(("shell", "input", "tap", "200", "240"), adb.calls[-1])

        attempts, last_attempt, tapped = SMOKE.retry_accessible_tap(
            adb, xml, ("More",), SMOKE.MAX_TOUCH_ATTEMPTS, 11.5, 20.0)
        self.assertEqual((SMOKE.MAX_TOUCH_ATTEMPTS, 11.5, False),
                         (attempts, last_attempt, tapped))
        self.assertEqual(1, len(adb.calls))

    def test_settings_path_remains_preferred_when_both_controls_are_visible(self):
        xml = (
            '<hierarchy><node text="Settings" bounds="[10,20][110,60]" />'
            '<node text="No game files yet" bounds="[0,0][10,10]" />'
            '<node text="Back" bounds="[0,0][10,10]" /></hierarchy>')
        self.assertEqual(("settings", None, (60, 40)), SMOKE.front_shell_navigation(xml))

    def test_immersive_confirmation_is_suppressed_and_overlay_can_be_tapped(self):
        class RecordingAdb:
            def __init__(self, returncode=0):
                self.returncode = returncode
                self.calls = []

            def run(self, *arguments, **_options):
                self.calls.append(arguments)
                return SimpleNamespace(returncode=self.returncode, stdout="", stderr="denied")

        adb = RecordingAdb()
        SMOKE.confirm_immersive_mode(adb)
        self.assertEqual(
            ("shell", "settings", "put", "secure", "immersive_mode_confirmations", "confirmed"),
            adb.calls[0])
        xml = '<hierarchy><node text="Got it" bounds="[100,200][300,280]" /></hierarchy>'
        self.assertTrue(SMOKE.dismiss_immersive_confirmation(adb, xml))
        self.assertEqual(("shell", "input", "tap", "200", "240"), adb.calls[-1])
        with self.assertRaises(SMOKE.SmokeFailure):
            SMOKE.confirm_immersive_mode(RecordingAdb(returncode=1))

    def test_ui_dump_timeout_is_reported_as_smoke_failure(self):
        class TimingOutAdb:
            def run(self, *_arguments, **_options):
                raise subprocess.TimeoutExpired("adb", 15)

        with self.assertRaisesRegex(SMOKE.SmokeFailure, "cannot read Android accessibility tree"):
            SMOKE.read_ui_dump(TimingOutAdb())

    def test_fatal_signatures_cover_android_jni_and_managed_startup(self):
        logcat = "\n".join((
            "E/AndroidRuntime: FATAL EXCEPTION: main",
            "A/libc: Fatal signal 11 (SIGSEGV)",
            "E/monodroid: System.TypeLoadException: Could not resolve type",
        ))
        self.assertEqual(3, len(SMOKE.fatal_lines(logcat)))
        self.assertEqual([], SMOKE.fatal_lines("I/ProjectPrime: launcher ready"))

    def test_reflection_bridge_and_renderer_failures_are_fatal(self):
        logcat = "\n".join((
            "E/ProjectPrime: KeyboardState reflection failed to find SetKeyState",
            "E/ProjectPrime: Avalonia renderer initialization failed",
            "E/monodroid: Compressed assembly '<assembly_store>' is larger than when the application was built",
        ))
        self.assertEqual(3, len(SMOKE.fatal_lines(logcat)))

    def test_launcher_component_must_belong_to_expected_package(self):
        self.assertEqual(
            "com.antinotanti.projectprime/.MainActivity",
            SMOKE.launcher_component(
                "priority=0\ncom.antinotanti.projectprime/.MainActivity\n", SMOKE.PACKAGE),
        )
        self.assertIsNone(SMOKE.launcher_component("com.example/.MainActivity", SMOKE.PACKAGE))

    def test_update_replacement_installs_the_exact_apk_twice(self):
        class RecordingAdb:
            def __init__(self):
                self.calls = []

            def run(self, *arguments, **_options):
                self.calls.append(arguments)
                stdout = "package:/data/app/project-prime/base.apk\n" if arguments[:3] == (
                    "shell", "pm", "path") else "Success\n"
                return SimpleNamespace(returncode=0, stdout=stdout, stderr="")

        adb = RecordingAdb()
        apk = ROOT / "ProjectPrime-Signed.apk"
        SMOKE.install_and_verify_replacement(adb, apk)
        install_calls = [call for call in adb.calls if call[:2] == ("install", "-r")]
        self.assertEqual(2, len(install_calls))
        self.assertEqual(install_calls[0], install_calls[1])
        self.assertEqual(str(apk.resolve()), install_calls[0][2])


if __name__ == "__main__":
    unittest.main()
