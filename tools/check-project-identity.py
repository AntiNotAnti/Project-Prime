#!/usr/bin/env python3
"""Check the tracked Project Prime identity and its user-facing credits."""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import subprocess
import sys


CREDITS = Path("src/Client.Presentation/Runtime/Credits.cs")
README = Path("README.md")
HANDOFF_DEPLOY = Path("deploy-server.sh")
HANDOFF_TESTS = Path("tools/tests/test_deploy_server.py")
HANDOFF_SERVICE = Path("tools/systemd/projectprime-stack.service")
FRUIT = "fru" + "ity"
PRIME = "pri" + "me"
HUNTERS = "hun" + "ters"
FRUIT_TITLE = "Fru" + "ity"
PRIME_TITLE = "Pri" + "me"
HUNTERS_TITLE = "Hun" + "ters"
LIVE = "li" + "ve"
TEK = "te" + "k"
METROID = "met" + "roid"
SUPPORT_URL = "https://ko-fi.com/" + "tterraj"


def _credit_names() -> tuple[str, ...]:
    return ("Live" + "Tek", FRUIT_TITLE + PRIME_TITLE)


def _variants() -> tuple[str, ...]:
    values = {FRUIT}
    values.update(FRUIT + separator + PRIME for separator in ("", " ", "-", "_"))
    values.update(PRIME + separator + HUNTERS for separator in ("", " ", "-", "_"))
    values.add(LIVE + TEK)
    values.update("p" + "h" + "-" + suffix for suffix in ("node", "match"))
    return tuple(sorted(values, key=len, reverse=True))


OLD_VARIANTS = _variants()
OLD_PATTERNS = tuple((value, re.compile(re.escape(value), re.IGNORECASE)) for value in OLD_VARIANTS)
FRANCHISE = re.compile(
    re.escape(METROID) + r"[ _-]+" + re.escape(PRIME) + r"[ _-]+" + re.escape(HUNTERS),
    re.IGNORECASE,
)


LEGACY_NODE = FRUIT_TITLE + PRIME_TITLE + "Server"
LEGACY_BACKEND = PRIME_TITLE + HUNTERS_TITLE + ".Backend"
LEGACY_WORKER = FRUIT_TITLE + PRIME_TITLE + ".Server.Worker"
LEGACY_SPLIT_NODE = "'" + FRUIT_TITLE + "'+'" + PRIME_TITLE + "Server'"
LEGACY_SPLIT_BACKEND = "'" + PRIME_TITLE + "'+'" + HUNTERS_TITLE + ".Backend'"
LEGACY_SPLIT_WORKER = "'" + FRUIT_TITLE + "'+'" + PRIME_TITLE + ".Server.Worker'"
DEPLOY_LEGACY_LAYOUT = (
    "    ((" + LEGACY_SPLIT_NODE + "),'start-stack-dev.sh','worker/'+(" + LEGACY_SPLIT_WORKER
    + "),'backend/'+(" + LEGACY_SPLIT_BACKEND + ")),"
)
DEPLOY_PROCESS_LAYOUT = (
    "        {\n"
    "            'node':app/(" + LEGACY_SPLIT_NODE + "),\n"
    "            'backend':app/'backend'/(" + LEGACY_SPLIT_BACKEND + "),\n"
    "            'worker':app/'worker'/(" + LEGACY_SPLIT_WORKER + "),\n"
    "        },"
)
DEPLOY_STALE_EXECUTABLES = (
    "relative=(\n"
    "    (" + LEGACY_SPLIT_NODE + "),\n"
    "    'backend/'+(" + LEGACY_SPLIT_BACKEND + "),\n"
    "    'worker/'+(" + LEGACY_SPLIT_WORKER + "),\n"
    "    'ProjectPrimeServer',\n"
    "    'backend/ProjectPrime.Backend',\n"
    "    'worker/ProjectPrime.Server.Worker',\n"
    ")"
)
SERVICE_LEGACY_READ_WRITE = (
    "ReadWritePaths=-__ROOT__/current/" + LEGACY_NODE
    + " -__ROOT__/current/worker/" + LEGACY_WORKER
)


def _split_identity_hits(text: str) -> list[tuple[int, int, str]]:
    """Return split-string compatibility references that require exact contracts."""
    result: list[tuple[int, int, str]] = []
    pairs = (
        (FRUIT_TITLE, PRIME_TITLE + "Server"),
        (FRUIT_TITLE, PRIME_TITLE + ".Server.Worker"),
        (PRIME_TITLE, HUNTERS_TITLE + ".Backend"),
    )
    for first, second in pairs:
        for quote in ("'", '"'):
            pattern = re.compile(
                re.escape(quote + first + quote) + r"\s*\+\s*" + re.escape(quote + second + quote),
                re.IGNORECASE,
            )
            result.extend((match.start(), match.end(), first + second) for match in pattern.finditer(text))
    return sorted(result, key=lambda item: (item[0], item[1]))


def _franchise_context(text: str, start: int) -> bool:
    return any(match.start() <= start < match.end() for match in FRANCHISE.finditer(text))


def _hits(text: str) -> list[tuple[int, int, str]]:
    """Return non-overlapping old-identity matches, longest match first."""
    candidates: list[tuple[int, int, str]] = []
    for value, pattern in OLD_PATTERNS:
        candidates.extend((match.start(), match.end(), value) for match in pattern.finditer(text))
    candidates.sort(key=lambda item: (item[0], -(item[1] - item[0])))
    result: list[tuple[int, int, str]] = []
    for candidate in candidates:
        if any(candidate[0] < end and start < candidate[1] for start, end, _ in result):
            continue
        result.append(candidate)
    return result


def scan_text(
    text: str,
    location: str,
    *,
    allow_franchise: bool = True,
    allowed_ranges: tuple[tuple[int, int], ...] = (),
) -> list[str]:
    errors = []
    for start, end, value in _hits(text):
        if allow_franchise and _franchise_context(text, start):
            continue
        if any(begin <= start < finish for begin, finish in allowed_ranges):
            continue
        line = text.count("\n", 0, start) + 1
        errors.append(f"{location}:{line}: retired identity {value!r}")
    return errors


def _tracked(root: Path) -> list[str]:
    result = subprocess.run(
        ["git", "-C", str(root), "ls-files", "-z"],
        check=True,
        capture_output=True,
    )
    return [value.decode("utf-8") for value in result.stdout.split(b"\0") if value]


def _readme_credit_section(text: str) -> tuple[int, int] | None:
    """Return the body range of the exact level-two Credits section."""
    offset = 0
    fence: str | None = None
    section_start: int | None = None
    for line in text.splitlines(keepends=True):
        content = line.rstrip("\r\n")
        stripped = content.lstrip()
        if fence is not None:
            if stripped.startswith(fence):
                fence = None
            offset += len(line)
            continue
        if stripped.startswith("```"):
            fence = "```"
            offset += len(line)
            continue
        if stripped.startswith("~~~"):
            fence = "~~~"
            offset += len(line)
            continue
        match = re.match(r"^(#{1,6})[ \t]+(.+?)[ \t]*$", content)
        if match is not None:
            level = len(match.group(1))
            title = re.sub(r"[ \t]+#+[ \t]*$", "", match.group(2)).strip()
            if section_start is None:
                if level == 2 and title.casefold() == "credits":
                    section_start = offset + len(line)
            elif level <= 2:
                return section_start, offset
        offset += len(line)
    if section_start is None:
        return None
    return section_start, len(text)


def _credit_matches(text: str, name: str) -> list[re.Match[str]]:
    pattern = re.compile(
        r"(?<![A-Za-z0-9_-])" + re.escape(name) + r"(?![A-Za-z0-9_-])",
        re.IGNORECASE,
    )
    return list(pattern.finditer(text))


def _readme_credit_contract(root: Path) -> tuple[list[str], tuple[tuple[int, int], ...]]:
    path = root / README
    if not path.is_file():
        return [f"missing {README.as_posix()}"], ()
    text = path.read_text(encoding="utf-8")
    section = _readme_credit_section(text)
    if section is None:
        return [f"{README.as_posix()}: expected a level-two Credits section"], ()
    errors = []
    allowed_ranges: list[tuple[int, int]] = []
    for name in _credit_names():
        matches = [match for match in _credit_matches(text, name)
                   if section[0] <= match.start() < section[1]]
        if len(matches) != 1:
            errors.append(
                f"{README.as_posix()}: expected exactly one credit mention for {name!r}, found {len(matches)}"
            )
        else:
            allowed_ranges.append((matches[0].start(), matches[0].end()))
    if SUPPORT_URL not in text:
        errors.append("README.md: expected the Project Prime support link")
    return errors, tuple(allowed_ranges)


def _credit_contract(root: Path) -> list[str]:
    path = root / CREDITS
    if not path.is_file():
        return [f"missing {CREDITS.as_posix()}"]
    text = path.read_text(encoding="utf-8")
    errors = []
    entries = _credit_names()
    allowed_ranges: list[tuple[int, int]] = []
    for name in entries:
        needle = f'new Entry("{name}",'
        count = text.count(needle)
        if count != 1:
            errors.append(f"{CREDITS.as_posix()}: expected exactly one {needle!r}, found {count}")
        elif count == 1:
            offset = text.find(needle)
            name_start = offset + len('new Entry("')
            allowed_ranges.append((name_start, name_start + len(name)))
    support_line = f'public const string SupportUrl = "{SUPPORT_URL}";'
    if text.count(support_line) != 1:
        errors.append(f"{CREDITS.as_posix()}: SupportUrl must be exactly {SUPPORT_URL}")
    for start, _, value in _hits(text):
        if _franchise_context(text, start):
            continue
        if any(begin <= start < end for begin, end in allowed_ranges):
            continue
        line = text.count("\n", 0, start) + 1
        errors.append(f"{CREDITS.as_posix()}:{line}: unexpected credit identity {value!r}")
    return errors


def _handoff_specs(relative: str) -> tuple[tuple[str, str, int], ...]:
    if relative == HANDOFF_DEPLOY.as_posix():
        return (
            ("legacy deployment layout", DEPLOY_LEGACY_LAYOUT, 1),
            ("legacy process layout", DEPLOY_PROCESS_LAYOUT, 1),
            ("legacy stale-process executable layout", DEPLOY_STALE_EXECUTABLES, 1),
        )
    if relative == HANDOFF_TESTS.as_posix():
        return (
            ("legacy node assertion", LEGACY_SPLIT_NODE, 1),
            ("legacy backend assertion", LEGACY_SPLIT_BACKEND, 1),
            ("legacy worker assertion", LEGACY_SPLIT_WORKER, 1),
            ("legacy current-path assertion", "current/" + LEGACY_NODE, 1),
        )
    if relative == HANDOFF_SERVICE.as_posix():
        return (("legacy rollback ReadWritePaths", SERVICE_LEGACY_READ_WRITE, 1),)
    return ()


def _exact_ranges(
    text: str,
    specs: tuple[tuple[str, str, int], ...],
) -> tuple[list[str], tuple[tuple[int, int], ...]]:
    errors: list[str] = []
    allowed_ranges: list[tuple[int, int]] = []
    for label, snippet, expected in specs:
        matches = list(re.finditer(re.escape(snippet), text))
        if len(matches) != expected:
            errors.append(f"{label}: expected exactly {expected} occurrence(s), found {len(matches)}")
            continue
        allowed_ranges.extend((match.start(), match.end()) for match in matches)
    return errors, tuple(allowed_ranges)


def _handoff_contract(root: Path) -> tuple[list[str], dict[str, tuple[tuple[int, int], ...]]]:
    errors: list[str] = []
    allowed_ranges: dict[str, tuple[tuple[int, int], ...]] = {}
    for relative in (HANDOFF_DEPLOY.as_posix(), HANDOFF_TESTS.as_posix(), HANDOFF_SERVICE.as_posix()):
        path = root / relative
        if not path.is_file():
            errors.append(f"missing {relative}")
            continue
        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError as error:
            errors.append(f"{relative}: expected UTF-8 text ({error})")
            continue
        spec_errors, ranges = _exact_ranges(text, _handoff_specs(relative))
        errors.extend(f"{relative}: {error}" for error in spec_errors)
        allowed_ranges[relative] = ranges
    return errors, allowed_ranges


def _inspection_paths(root: Path) -> list[str]:
    paths = _tracked(root)
    service = HANDOFF_SERVICE.as_posix()
    if service not in paths and (root / HANDOFF_SERVICE).is_file():
        paths.append(service)
    return paths


def inspect(root: Path) -> list[str]:
    """Inspect tracked paths and required handoff files, then verify contracts."""
    root = root.resolve()
    errors: list[str] = []
    readme_errors, readme_ranges = _readme_credit_contract(root)
    handoff_errors, handoff_ranges = _handoff_contract(root)
    errors.extend(readme_errors)
    errors.extend(handoff_errors)
    for relative in _inspection_paths(root):
        errors.extend(scan_text(relative, relative, allow_franchise=True))
        path = root / relative
        if relative == CREDITS.as_posix() or not path.is_file():
            continue
        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
        allowed_ranges = handoff_ranges.get(relative, ())
        if relative == README.as_posix():
            allowed_ranges = readme_ranges
        errors.extend(scan_text(text, relative, allow_franchise=True, allowed_ranges=allowed_ranges))
        for start, end, value in _split_identity_hits(text):
            if any(begin <= start and end <= finish for begin, finish in allowed_ranges):
                continue
            line = text.count("\n", 0, start) + 1
            errors.append(f"{relative}:{line}: uncontracted split identity {value!r}")
    errors.extend(_credit_contract(root))
    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args(argv)
    try:
        errors = inspect(args.root)
    except (OSError, subprocess.CalledProcessError) as error:
        print(f"FAIL: unable to inspect tracked identity: {error}", file=sys.stderr)
        return 2
    if errors:
        for error in errors:
            print(f"FAIL: {error}", file=sys.stderr)
        return 1
    print("Project Prime identity guard passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
