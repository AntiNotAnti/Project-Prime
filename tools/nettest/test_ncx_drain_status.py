"""Same-publication reads and narrowly bounded sharing-race recovery."""
import ctypes
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import net2_status


def sharing_error(code):
    error = OSError("synthetic native failure")
    error.winerror = code
    return error


class Clock:
    def __init__(self):
        self.now = 0
        self.sleeps = []

    def monotonic(self):
        return self.now

    def sleep(self, seconds):
        self.sleeps.append(seconds)
        self.now += seconds


class DrainStatusTests(unittest.TestCase):
    def test_one_second_freshness_and_stale_parse_skipped(self):
        with patch.object(net2_status, "_read_snapshot", return_value=(99, b'{"Frame":17}')), \
                patch.object(net2_status.time, "time", return_value=100):
            self.assertEqual(net2_status.read_drain_status("unused"), {"Frame": 17})
        with patch.object(net2_status, "_read_snapshot", return_value=(98.999, b'not JSON')), \
                patch.object(net2_status.time, "time", return_value=100):
            self.assertIsNone(net2_status.read_drain_status("unused"))

    def test_parse_failure_propagates_after_handle_is_closed_without_retry(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "status.json"
            path.write_bytes(b'{invalid')
            stream = net2_status._open_status(path)
            original_parse = json.loads

            def parse(payload):
                self.assertTrue(stream.closed)
                return original_parse(payload)

            with patch.object(net2_status, "_open_status", return_value=stream) as opener, \
                    patch.object(net2_status.json, "loads", side_effect=parse), \
                    patch.object(net2_status.time, "sleep") as sleep:
                with self.assertRaises(json.JSONDecodeError):
                    net2_status.read_drain_status(path)
                self.assertEqual(opener.call_count, 1)
                sleep.assert_not_called()

    def test_metadata_and_payload_belong_to_same_handle_across_replace(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "status.json"
            replacement = Path(temporary) / "next.json"
            path.write_text('{"Frame":1}')
            replacement.write_text('{"Frame":2}')
            os.utime(path, (100, 100))
            os.utime(replacement, (1, 1))
            original_open = net2_status._open_status
            original_fstat = os.fstat
            seen = []

            def open_then_publish(name):
                stream = original_open(name)
                try:
                    if os.name == "nt":
                        from ctypes import wintypes
                        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
                        replace = kernel.ReplaceFileW
                        replace.argtypes = (wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.LPCWSTR,
                                            wintypes.DWORD, wintypes.LPVOID, wintypes.LPVOID)
                        replace.restype = wintypes.BOOL
                        if not replace(str(path), str(replacement), None, 0, None, None):
                            raise ctypes.WinError(ctypes.get_last_error())
                    else:
                        os.replace(replacement, path)
                except BaseException:
                    stream.close()
                    raise
                seen.append(stream)
                return stream

            with patch.object(net2_status, "_open_status", side_effect=open_then_publish), \
                    patch.object(net2_status.os, "fstat", wraps=original_fstat) as fstat, \
                    patch.object(net2_status.time, "time", return_value=100):
                self.assertEqual(net2_status.read_drain_status(path), {"Frame": 1})
                fstat.assert_called_once()
            self.assertTrue(seen[0].closed)
            self.assertEqual(json.loads(path.read_text()), {"Frame": 2})
            self.assertEqual(path.stat().st_mtime, 1)

    def test_windows_only_error_32_reopens_and_then_succeeds(self):
        clock = Clock()
        with patch.object(net2_status, "IS_WINDOWS", True), \
                patch.object(net2_status, "_read_snapshot", side_effect=[sharing_error(32), sharing_error(32), (100, b'{}')]) as reader, \
                patch.object(net2_status.time, "time", return_value=100), \
                patch.object(net2_status.time, "monotonic", side_effect=clock.monotonic), \
                patch.object(net2_status.time, "sleep", side_effect=clock.sleep):
            self.assertEqual(net2_status.read_drain_status("unused"), {})
            self.assertEqual(reader.call_count, 3)
            self.assertEqual(clock.sleeps, [.002, .002])

    def test_other_windows_errors_and_posix_32_propagate_without_retry(self):
        for windows, code in ((True, 2), (True, 5), (True, 33), (False, 32)):
            with self.subTest(windows=windows, code=code), \
                    patch.object(net2_status, "IS_WINDOWS", windows), \
                    patch.object(net2_status, "_read_snapshot", side_effect=sharing_error(code)) as reader, \
                    patch.object(net2_status.time, "sleep") as sleep:
                with self.assertRaises(OSError) as raised:
                    net2_status.read_drain_status("unused")
                self.assertEqual(raised.exception.winerror, code)
                self.assertEqual(reader.call_count, 1)
                sleep.assert_not_called()

    def test_repeated_sharing_violations_stop_at_fifty_milliseconds(self):
        clock = Clock()
        with patch.object(net2_status, "IS_WINDOWS", True), \
                patch.object(net2_status, "_read_snapshot", side_effect=sharing_error(32)) as reader, \
                patch.object(net2_status.time, "monotonic", side_effect=clock.monotonic), \
                patch.object(net2_status.time, "sleep", side_effect=clock.sleep):
            with self.assertRaises(OSError) as raised:
                net2_status.read_drain_status("unused")
            self.assertEqual(raised.exception.winerror, 32)
            self.assertGreater(reader.call_count, 1)
            self.assertLessEqual(reader.call_count, 26)
            self.assertLessEqual(sum(clock.sleeps), .050)
            self.assertTrue(all(0 < delay <= .002 for delay in clock.sleeps))

    def test_fstat_failure_closes_handle(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "status.json"
            path.write_bytes(b'{}')
            stream = net2_status._open_status(path)
            with patch.object(net2_status, "_open_status", return_value=stream), \
                    patch.object(net2_status.os, "fstat", side_effect=sharing_error(5)):
                with self.assertRaises(OSError):
                    net2_status.read_drain_status(path)
            self.assertTrue(stream.closed)


if __name__ == "__main__":
    unittest.main()
