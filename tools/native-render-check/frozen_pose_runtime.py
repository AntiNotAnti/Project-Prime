"""Resolve an explicitly selected frozen engine and a native .NET executable."""
import os
from pathlib import Path
import shutil


def frozen_runtime(test):
    selected = os.environ.get("PROJECT_PRIME_POSE_REFERENCE_DLL")
    if not selected:
        test.skipTest("Set PROJECT_PRIME_POSE_REFERENCE_DLL to an explicit frozen engine; native world acceptance NOT_RUN")
    reference = Path(selected).expanduser().resolve()
    test.assertTrue(reference.is_file(), "Explicit frozen engine DLL is missing: " + str(reference))
    requested = os.environ.get("DOTNET_EXE")
    executable = requested or shutil.which("dotnet") or shutil.which("dotnet.exe")
    if not executable:
        test.skipTest("Native .NET SDK unavailable; set DOTNET_EXE; native world acceptance NOT_RUN")
    resolved = shutil.which(executable) or executable
    dotnet = Path(resolved).expanduser().resolve()
    test.assertTrue(dotnet.is_file(), "Selected .NET executable is missing: " + str(dotnet))
    return dotnet, reference
