#!/usr/bin/env python3
"""Keep retired online facades behind an explicit compatibility allowlist."""
from __future__ import annotations

import argparse
import importlib.util
from pathlib import Path
import re
import sys


LEGACY_ONLINE_REFERENCE = re.compile(
    r"\b(?:AuthoritativePlay\.(?:Current|Active)|NodeSessions\.Current)\b"
)
COMPATIBILITY_FILES = {
    "src/Client/Networking/AuthoritativePlay.cs",
    "src/Client/Networking/ClientOnlineRuntime.cs",
}
RESIDUAL_SHIM_FILES = {
    # Launcher and renderer owners still need an explicit scene/runtime handoff.
    "src/Client/Launcher/Gui/HomeView.cs",
    "src/Client/Launcher/Gui/PauseMenuView.cs",
    "src/Client/Launcher/Shell/Play/PlayPresentation.cs",
    "src/Client/Rendering/RenderInterpolation.cs",
    "src/Client/Rendering/Renderer.cs",
}
ALLOWED_FILES = COMPATIBILITY_FILES | RESIDUAL_SHIM_FILES
PRODUCTION_ROOTS = ("src/Client/", "src/Android/")


def inspect_diff(diff: str) -> list[str]:
    """Return violations for added production lines in a unified diff."""
    violations: list[str] = []
    path: str | None = None
    line_number = 0
    for raw in diff.splitlines():
        if raw.startswith("+++ b/"):
            path = raw[6:]
            line_number = 0
            continue
        if raw.startswith("@@"):
            # Unified hunks are of the form @@ -old,count +new,count @@.
            match = re.search(r"\+(\d+)", raw)
            line_number = int(match.group(1)) - 1 if match else 0
            continue
        if path is None or not raw.startswith("+") or raw.startswith("+++"):
            if raw and not raw.startswith("-"):
                line_number += 1
            continue
        line_number += 1
        if not any(path.startswith(root) for root in PRODUCTION_ROOTS):
            continue
        if path in COMPATIBILITY_FILES:
            continue
        match = LEGACY_ONLINE_REFERENCE.search(raw[1:])
        if match:
            violations.append(
                f"{path}:{line_number}: new production reference to retired online facade "
                f"{match.group()}"
            )
    return sorted(set(violations))


def inspect(root: Path) -> list[str]:
    root = root.resolve()
    spec = importlib.util.spec_from_file_location(
        "client_online_tokenizer", root / "tools/check-multiplayer-only.py")
    if spec is None or spec.loader is None:
        raise RuntimeError("cannot load source tokenizer")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    violations: list[str] = []
    for relative_root in PRODUCTION_ROOTS:
        source_root = root / relative_root
        if not source_root.is_dir():
            continue
        for source in sorted(source_root.rglob("*.cs")):
            if {"bin", "obj"}.intersection(source.relative_to(root).parts):
                continue
            relative = source.relative_to(root).as_posix()
            code = module.mask_non_code(source.read_text(encoding="utf-8-sig"))
            if relative in ALLOWED_FILES:
                continue
            for match in LEGACY_ONLINE_REFERENCE.finditer(code):
                line = code.count("\n", 0, match.start()) + 1
                violations.append(
                    f"{relative}:{line}: production reference to retired online facade "
                    f"{match.group()}"
                )
    return sorted(set(violations))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path,
                        default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    try:
        violations = inspect(args.root)
    except (OSError, RuntimeError) as error:
        print(f"client online runtime boundary: {error}", file=sys.stderr)
        return 2
    for violation in violations:
        print(violation)
    print(f"client online runtime boundary: {len(violations)} violation(s)")
    return int(bool(violations))


if __name__ == "__main__":
    sys.exit(main())
