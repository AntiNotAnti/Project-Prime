#!/usr/bin/env python3
"""Subprocess ABI/fault seam tests. No adapter, device or window is required.

These prove errors cross the C boundary without aborting and diagnostics stay
bounded. Hardware recovery still requires the renderer/device-loss fixtures.
"""
import argparse
import ctypes
import subprocess
import sys
from pathlib import Path

FUNCTIONS = {
    1: ('primeWgpuSurfaceConfigure', [ctypes.c_void_p, ctypes.c_void_p]),
    2: ('primeWgpuSurfaceAcquire', [ctypes.c_void_p, ctypes.c_void_p]),
    3: ('primeWgpuSurfacePresent', [ctypes.c_void_p]),
    4: ('primeWgpuSurfaceDiscard', [ctypes.c_void_p]),
    5: ('primeWgpuQueueSubmit', [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p]),
}


def child(path, operation, outcome):
    lib = ctypes.CDLL(str(path))
    inject = lib.primeWgpuInjectSurfaceOutcome
    inject.argtypes = [ctypes.c_uint32, ctypes.c_uint32]
    name, arguments = FUNCTIONS[operation]
    function = getattr(lib, name)
    function.argtypes = arguments + [ctypes.c_void_p, ctypes.c_size_t]
    function.restype = ctypes.c_uint32
    message = ctypes.create_string_buffer(b'?'*15, 16)
    inject(operation, outcome)
    status = function(*([None, 0, None] if operation == 5 else [None]*len(arguments)), message, 8)
    assert status == outcome, (operation, status, outcome)
    assert message.raw[7] == 0 and message.raw[8:] == b'?'*7+b'\x00', message.raw
    # One-shot injection: a second null call is validation, not a stale fault.
    assert function(*([None, 0, None] if operation == 5 else [None]*len(arguments)), message, 8) == 6


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('library', type=Path)
    parser.add_argument('--fault-library', action='store_true')
    parser.add_argument('--child', nargs=2, type=int)
    args = parser.parse_args()
    args.library = args.library.resolve()
    if args.child:
        child(args.library, *args.child)
    else:
        lib = ctypes.CDLL(str(args.library))
        version = lib.primeWgpuBridgeVersion
        version.restype = ctypes.c_uint32
        assert version() == 1
        if sys.platform == 'darwin':
            getattr(lib, 'primeInstanceCreateSurfaceAppKit')
        for name, _ in FUNCTIONS.values():
            getattr(lib, name)
        period = lib.primeWgpuQueueGetTimestampPeriod
        period.argtypes = [ctypes.c_void_p]
        period.restype = ctypes.c_float
        assert period(None) == 0
        has_injection = hasattr(lib, 'primeWgpuInjectSurfaceOutcome')
        assert has_injection == args.fault_library, 'test-only injection export in production runtime'
        count = 0
        if args.fault_library:
            for operation in FUNCTIONS:
                for outcome in range(1, 8):
                    subprocess.run([sys.executable, __file__, str(args.library), '--child', str(operation), str(outcome)], check=True)
                    count += 1
        print(f'Prime native ABI verified; {count} subprocess fault cases passed (no hardware recovery claim).')
