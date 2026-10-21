"""Read one atomic drain publication with metadata from the same open file.

Windows readers share delete access so File.Replace can publish while a reader
holds the old version. A sharing violation alone admits a bounded reopen retry;
stale or malformed publications remain subject to the controller's usual gates.
"""
import ctypes
import json
import os
from pathlib import Path
import time

IS_WINDOWS = os.name == "nt"
RETRY_SECONDS = .050
RETRY_SLEEP_SECONDS = .002


def _open_status(path):
    if not IS_WINDOWS:
        return open(path, "rb")
    import msvcrt
    from ctypes import wintypes

    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    create = kernel.CreateFileW
    create.argtypes = (wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                       wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE)
    create.restype = wintypes.HANDLE
    close = kernel.CloseHandle
    close.argtypes = (wintypes.HANDLE,)
    close.restype = wintypes.BOOL
    # GENERIC_READ; FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE;
    # OPEN_EXISTING; FILE_ATTRIBUTE_NORMAL. No publication may be created here.
    handle = create(str(Path(path)), 0x80000000, 1 | 2 | 4, None, 3, 0x80, None)
    if handle == ctypes.c_void_p(-1).value:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        descriptor = msvcrt.open_osfhandle(handle, os.O_RDONLY | os.O_BINARY)
    except BaseException:
        close(handle)
        raise
    try:
        return os.fdopen(descriptor, "rb")  # stream now owns the native handle
    except BaseException:
        os.close(descriptor)
        raise


def _read_snapshot(path):
    with _open_status(path) as stream:
        modified = os.fstat(stream.fileno()).st_mtime
        payload = stream.read()
    return modified, payload


def read_drain_status(path, max_age_seconds=1):
    """Return one fresh JSON publication, or None when its same-handle age > 1s.

    Parsing occurs after close. Only Windows winerror 32 is retried, reopening
    on each attempt, for at most a 50ms monotonic retry window. Other I/O,
    decoding and JSON errors propagate to the caller without retry.
    """
    deadline = time.monotonic() + RETRY_SECONDS
    while True:
        try:
            modified, payload = _read_snapshot(path)
            break
        except OSError as error:
            if not IS_WINDOWS or getattr(error, "winerror", None) != 32:
                raise
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise
            time.sleep(min(RETRY_SLEEP_SECONDS, remaining))
            if time.monotonic() >= deadline:
                raise
    if time.time() - modified > max_age_seconds:
        return None
    return json.loads(payload.decode("utf-8"))
