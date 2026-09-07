#!/usr/bin/env python3
"""Render a server unit without changing its existing port, name or match rules."""
import argparse
import pathlib
import re
import shlex


def quote(value: str) -> str:
    if "\n" in value or "\r" in value or "\0" in value:
        raise ValueError("Unit values cannot contain line breaks or NUL bytes")
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"').replace("%", "%%") + '"'


def set_argument(arguments: str, flag: str, value: str | None) -> str:
    # Preserve raw spelling of untouched arguments, including systemd %
    # specifiers and quoted names that happen to contain flag-like text.
    tokens = list(re.finditer(r'''(?:[^\s"'\\]+|\\.|"(?:\\.|[^"\\])*"|'[^']*')+''', arguments))
    remove = []
    for index, token in enumerate(tokens):
        if shlex.split(token[0])[0] != flag:
            continue
        end = token.end()
        if flag != "-nomaster":
            if index + 1 >= len(tokens) or shlex.split(tokens[index + 1][0])[0].startswith("-"):
                raise ValueError("Existing unit has no value for " + flag)
            end = tokens[index + 1].end()
        remove.append((token.start(), end))
    for begin, end in reversed(remove):
        arguments = arguments[:begin] + arguments[end:]
    arguments = arguments.rstrip()
    return arguments if value is None else arguments + " " + flag + " " + quote(value)


def render(source: str, user: str, directory: str, data: str | None, version: str, master: str | None) -> str:
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_.-]*\$?", user):
        raise ValueError("Invalid service user")
    if not directory.startswith("/") or data is not None and not data.startswith("/"):
        raise ValueError("Install and content directories must be absolute remote paths")
    if version not in {"AMHE0", "AMHE1", "AMHP0", "AMHP1", "AMHJ0", "AMHJ1", "AMHK0"}:
        raise ValueError("Unsupported content version")
    source = source.replace("__USER__", user).replace('"__DIR__"', quote(directory))
    source = source.replace('"__DIR__/FruityPrime"', quote(directory + "/FruityPrime"))
    source = source.replace('"__DATA__"', quote(data or ""))
    source = source.replace('"__DATA_VERSION__"', quote(version)).replace("__LISTING__", "-nomaster")
    lines = source.splitlines()
    executable_lines = [index for index, line in enumerate(lines) if line.startswith("ExecStart=")]
    if len(executable_lines) != 1:
        raise ValueError("Expected exactly one ExecStart; configure custom service wrappers manually")
    index = executable_lines[0]
    command = lines[index][len("ExecStart="):]
    program = re.match(r'''(?:"(?:\\.|[^"\\])*"|[^\s]+)''', command)
    if not program or pathlib.PurePosixPath(shlex.split(program[0])[0]).name not in {"MphRead", "FruityPrime"}:
        raise ValueError("Expected an existing MphRead/FruityPrime executable; configure wrappers manually")
    arguments = command[program.end():]
    if data is not None:
        arguments = set_argument(arguments, "-data", data)
        arguments = set_argument(arguments, "-dataversion", version)
        if master is not None:
            if not re.fullmatch(r"[A-Za-z0-9._-]+:[0-9]{1,5}", master) or not 1 <= int(master.rsplit(":", 1)[1]) <= 65535:
                raise ValueError("MPH_SERVER_MASTER must be HOST:PORT")
            arguments = set_argument(arguments, "-nomaster", None)
            arguments = set_argument(arguments, "-masterport", None)
            arguments = set_argument(arguments, "-master", master)
    lines[index] = "ExecStart=" + quote(directory + "/FruityPrime") + arguments
    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=pathlib.Path)
    parser.add_argument("--user", required=True)
    parser.add_argument("--directory", required=True)
    parser.add_argument("--data")
    parser.add_argument("--version", default="AMHE1")
    parser.add_argument("--master")
    args = parser.parse_args()
    try:
        print(render(args.source.read_text(), args.user, args.directory, args.data, args.version, args.master), end="")
    except ValueError as error:
        parser.error(str(error))


if __name__ == "__main__":
    main()
